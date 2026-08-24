using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Gravity.Core
{
    public class FileSearchService
    {
        private readonly IProjectContext _projectContext;
        private readonly string? _overrideRoot;
        private readonly string[] _excludeDirs = { "bin", "obj", ".vs", ".git", ".gemini", "node_modules" };

        public string RootPath => _overrideRoot ?? _projectContext.ProjectDirectory ?? AppContext.BaseDirectory;

        public FileSearchService(IProjectContext projectContext)
        {
            _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
        }

        // Back-compat constructor: allow passing a root path directly (used by tests)
        public FileSearchService(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath)) throw new ArgumentNullException(nameof(rootPath));
            _overrideRoot = Path.GetFullPath(rootPath);
            // create a default project context to satisfy other APIs
            _projectContext = new ProjectContext();
        }

        public Task<List<string>> EnumerateProjectFilesAsync(string projectFilePath, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var results = new List<string>();
                if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
                    return results;

                var root = RootPath;
                var projectDir = Path.GetDirectoryName(projectFilePath) ?? root;
                try
                {
                    var doc = XDocument.Load(projectFilePath);
                    var isSdk = doc.Root?.Attribute("Sdk") != null;

                    if (isSdk)
                    {
                        // Modern SDK-style project: everything in the folder is included by default
                        return Directory.EnumerateFiles(projectDir, "*.*", SearchOption.AllDirectories)
                            .Where(f => !_excludeDirs.Any(d => f.Contains(Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar) || f.Contains(Path.AltDirectorySeparatorChar + d + Path.AltDirectorySeparatorChar)))
                            .Select(Path.GetFullPath)
                            .ToList();
                    }

                    // Legacy style: parse XML Includes
                    var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                    var includes = new List<string>();
                    foreach (var elemName in new[] { "Compile", "None", "Content" })
                    {
                        foreach (var el in doc.Descendants(ns + elemName))
                        {
                            var inc = el.Attribute("Include")?.Value;
                            if (!string.IsNullOrEmpty(inc)) includes.Add(inc);
                        }
                    }

                    foreach (var inc in includes)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (inc.IndexOfAny(new[] { '*', '?' }) >= 0)
                        {
                            var fullPattern = Path.Combine(projectDir, inc);
                            var dir = Path.GetDirectoryName(fullPattern) ?? projectDir;
                            var pattern = Path.GetFileName(fullPattern);
                            if (Directory.Exists(dir))
                            {
                                var found = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly);
                                foreach (var f in found) results.Add(Path.GetFullPath(f));
                            }
                        }
                        else
                        {
                            var abs = Path.IsPathRooted(inc) ? inc : Path.Combine(projectDir, inc);
                            if (File.Exists(abs)) results.Add(Path.GetFullPath(abs));
                        }
                    }

                    return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch
                {
                    return results;
                }
            }, ct);
        }

        public Task<List<string>> ListDirectoryAsync(string relativePath, bool recursive, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var root = RootPath;
                var targetDir = ResolvePath(relativePath);
                if (!Directory.Exists(targetDir)) return new List<string>();

                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var results = new List<string>();

                // Get Subdirectories
                var dirs = Directory.EnumerateDirectories(targetDir, "*", option)
                    .Where(d => !_excludeDirs.Any(x => d.EndsWith(Path.DirectorySeparatorChar + x) || d.Contains(Path.DirectorySeparatorChar + x + Path.DirectorySeparatorChar)))
                    .Select(d => "[DIR] " + Path.GetRelativePath(root, d));
                
                results.AddRange(dirs);

                // Get Files
                var files = Directory.EnumerateFiles(targetDir, "*.*", option)
                    .Where(f => !_excludeDirs.Any(x => f.Contains(Path.DirectorySeparatorChar + x + Path.DirectorySeparatorChar)))
                    .Select(f => "[FILE] " + Path.GetRelativePath(root, f));

                results.AddRange(files);

                return results.ToList();
            }, ct);
        }

        public Task<List<string>> SearchFilesAsync(string pattern, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var root = RootPath;
                var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(f => !_excludeDirs.Any(d => f.Contains(Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar)))
                    .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var results = new List<string>();
                
                // Special case: '*' means everything
                if (pattern == "*")
                {
                    return files.Select(Path.GetFullPath).ToList();
                }

                // Clean pattern: strip leading/trailing * for simpler matching
                var cleanPattern = pattern.Trim('*');
                if (string.IsNullOrWhiteSpace(cleanPattern)) return results;

                Regex? rx = null;
                try { rx = new Regex(cleanPattern, RegexOptions.IgnoreCase); } catch { }

                foreach (var f in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var content = File.ReadAllText(f);
                    var fileName = Path.GetFileName(f);

                    if (rx != null && (rx.IsMatch(content) || rx.IsMatch(fileName)))
                    {
                        results.Add(Path.GetFullPath(f));
                    }
                    else if (content.IndexOf(cleanPattern, StringComparison.OrdinalIgnoreCase) >= 0 || fileName.IndexOf(cleanPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        results.Add(Path.GetFullPath(f));
                    }
                }
                return results;
            }, ct);
        }

        private string ResolvePath(string relativePath)
        {
            var root = RootPath;
            if (Path.IsPathRooted(relativePath)) return relativePath;

            relativePath = relativePath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            
            var dirName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(dirName))
            {
                var normalizedRel = relativePath.Replace('\\', '/');
                while (normalizedRel.StartsWith(dirName + "/", StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = relativePath.Substring(dirName.Length + 1).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    normalizedRel = relativePath.Replace('\\', '/');
                }
            }

            return Path.GetFullPath(Path.Combine(root, relativePath));
        }

        public Task<string> ReadFileAsync(string relativePath, CancellationToken ct)
        {
            var abs = ResolvePath(relativePath);
            return Task.Run(() => File.ReadAllText(abs), ct);
        }

        public Task WriteFileAsync(string relativePath, string content, CancellationToken ct)
        {
            var abs = ResolvePath(relativePath);
            return Task.Run(() =>
            {
                var dir = Path.GetDirectoryName(abs);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(abs))
                {
                    var bak = abs + ".bak." + DateTime.UtcNow.Ticks;
                    File.Copy(abs, bak);
                }
                File.WriteAllText(abs, content);
            }, ct);
        }

        public async Task<List<string>> GetFileChunksAsync(string relativePath, int chunkSize = 1000, CancellationToken ct = default)
        {
            var content = await ReadFileAsync(relativePath, ct);
            if (string.IsNullOrEmpty(content)) return new List<string>();

            var chunks = new List<string>();
            for (int i = 0; i < content.Length; i += chunkSize)
            {
                var length = Math.Min(chunkSize, content.Length - i);
                chunks.Add(content.Substring(i, length));
            }
            return chunks;
        }
    }
}
