using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    /// <summary>
    /// Manages workspace indexing and provides semantic context retrieval for agents.
    /// Wraps RagIndex with incremental refresh, file-change awareness, and formatted output.
    /// </summary>
    public class RagService
    {
        private readonly RagIndex _index = new();
        private readonly IProjectContext _projectContext;

        private readonly HashSet<string> _indexedFiles = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastFullIndex = DateTime.MinValue;
        private static readonly TimeSpan _reindexInterval = TimeSpan.FromMinutes(5);

        private static readonly string[] _indexableExtensions = { ".cs", ".ts", ".js", ".py", ".go", ".md", ".json" };
        private static readonly string[] _excludeDirs = { "bin", "obj", ".vs", "node_modules", ".git", "dist" };

        // Context budget: max characters injected per step
        private const int MAX_CONTEXT_CHARS = 700;
        private const int CHUNKS_PER_STEP = 2;

        public int IndexedFileCount => _indexedFiles.Count;

        public RagService(IProjectContext projectContext)
        {
            _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
        }

        /// <summary>
        /// Build or refresh the workspace index. Incremental — only re-indexes changed/new files.
        /// </summary>
        public async Task RefreshIndexAsync(CancellationToken ct = default)
        {
            var root = _projectContext.ProjectDirectory;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

            bool fullRefreshNeeded = (DateTime.UtcNow - _lastFullIndex) > _reindexInterval;

            await Task.Run(() =>
            {
                var allFiles = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(f => _indexableExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .Where(f => !_excludeDirs.Any(d => f.Contains(Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar)))
                    .ToList();

                if (fullRefreshNeeded)
                {
                    _index.Clear();
                    _indexedFiles.Clear();
                }

                foreach (var file in allFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (!fullRefreshNeeded && _indexedFiles.Contains(file))
                        {
                            // Use last-write-time to detect changes
                            var lastWrite = File.GetLastWriteTimeUtc(file);
                            if (lastWrite <= _lastFullIndex) continue;
                        }

                        var content = File.ReadAllText(file);
                        _index.IndexFile(file, content);
                        _indexedFiles.Add(file);
                    }
                    catch { /* skip unreadable files */ }
                }

                if (fullRefreshNeeded) _lastFullIndex = DateTime.UtcNow;

            }, ct);
        }

        /// <summary>
        /// Returns a formatted markdown block of the most relevant code chunks for a given query.
        /// Ready to be injected directly into the agent's system prompt.
        /// </summary>
        public string BuildContextBlock(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || IndexedFileCount == 0)
                return string.Empty;

            var chunks = _index.RetrieveTopK(query, CHUNKS_PER_STEP);
            if (!chunks.Any()) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("\n---\n[WORKSPACE CONTEXT — most relevant code for this step]");

            int totalChars = 0;
            foreach (var chunk in chunks)
            {
                var relativePath = Path.GetRelativePath(_projectContext.ProjectDirectory ?? ".", chunk.FilePath);
                var header = $"\n### `{relativePath}` (line {chunk.StartLine}, symbol: {chunk.Symbol})\n```csharp\n";
                var footer = "\n```\n";

                // Respect context budget - truncate chunks to prevent massive context window bloat for local LLMs
                var chunkContent = chunk.Content.Length > 500 ? chunk.Content.Substring(0, 500) + "\n// ... (truncated)" : chunk.Content;
                int blockLen = header.Length + chunkContent.Length + footer.Length;

                if (totalChars + blockLen > MAX_CONTEXT_CHARS) break;

                sb.Append(header);
                sb.Append(chunkContent);
                sb.Append(footer);
                totalChars += blockLen;
            }

            sb.AppendLine("---");
            return sb.ToString();
        }

        /// <summary>
        /// Force re-index of a specific file (e.g. after a file:write tool call by the agent).
        /// </summary>
        public void NotifyFileChanged(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var content = File.ReadAllText(filePath);
                    _index.IndexFile(filePath, content);
                    _indexedFiles.Add(filePath);
                }
                else
                {
                    _index.RemoveFile(filePath);
                    _indexedFiles.Remove(filePath);
                }
            }
            catch { }
        }
    }
}
