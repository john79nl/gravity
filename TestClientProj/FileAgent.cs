using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    public class FileAgent : IAgent
    {
        private readonly FileSearchService _searchService;

        public AgentDescriptor Descriptor { get; }

        public FileAgent(FileSearchService searchService)
        {
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
            Descriptor = new AgentDescriptor
            {
                Name = "code_editor",
                Description = "PRIMARY tool for searching, reading, and exploring the project codebase and internal logic.",
                CanWrite = true,
                SupportedVerbs = new[] { "list_directory", "read_file", "read_range", "write_file", "search_in_files", "delete", "apply_diff" },
                Actions = new List<ActionMetadata>
                {
                    new ActionMetadata { Name = "list_directory", Description = "List details of a directory. Defaults to root. Use for exploration.", IsMutation = false, Parameters = new Dictionary<string, string> { ["path"] = "directory to list", ["recursive"] = "optional 'true'/'false' (default false)" }, OptionalParameters = new List<string> { "path", "recursive" } },
                    new ActionMetadata { Name = "read_file", Description = "Read file content. Use for exploring or copying code.", IsMutation = false, Parameters = new Dictionary<string, string> { ["path"] = "file path" } },
                    new ActionMetadata { Name = "read_range", Description = "Read specific line range from a file.", IsMutation = false, Parameters = new Dictionary<string, string> { ["path"] = "file path", ["start_line"] = "start line number", ["end_line"] = "end line number" } },
                    new ActionMetadata { Name = "write_file", Description = "Create or overwrite a file with specific content.", IsMutation = true, Parameters = new Dictionary<string, string> { ["path"] = "file path", ["content"] = "file content" } },
                    new ActionMetadata { Name = "search_in_files", Description = "Search codebase. REQUIRED: pattern (Internal keyword or regex).", IsMutation = false, Parameters = new Dictionary<string, string> { ["pattern"] = "Keyword or regex" } },
                    new ActionMetadata { Name = "delete", Description = "Permanently remove a file.", IsMutation = true, Parameters = new Dictionary<string, string> { ["path"] = "file path" } },
                    new ActionMetadata { Name = "apply_diff", Description = "Atomically replace text. REQUIRED: path, from, to.", IsMutation = true, Parameters = new Dictionary<string, string> { ["path"] = "file path", ["from"] = "original text", ["to"] = "new text" } }
                }
            };
        }

        public async Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Verb))
                return new AgentResult { Success = false, Output = "Invalid request." };

            switch (request.Verb.ToLowerInvariant())
            {
                case "search_in_files":
                case "search":
                    return await HandleSearchAsync(request, ct);
                case "read_file":
                case "read_range":
                case "read":
                    return await HandleReadAsync(request, ct);
                case "apply_diff":
                case "replace":
                    return await HandleReplaceAsync(request, ct);
                case "write_file":
                case "write":
                    return await HandleWriteAsync(request, ct);
                case "delete":
                    return await HandleDeleteAsync(request, ct);
                case "list_directory":
                case "list":
                    return await HandleListAsync(request, ct);
                default:
                    return new AgentResult { Success = false, Output = $"Unknown verb '{request.Verb}'." };
            }
        }

        private string GetPathArgument(AgentRequest request)
        {
            return request.GetStringArgument("path");
        }

        private async Task<AgentResult> HandleWriteAsync(AgentRequest request, CancellationToken ct)
        {
            var path = GetPathArgument(request);
            var content = request.GetStringArgument("content");
            if (string.IsNullOrEmpty(path)) return new AgentResult { Success = false, Output = "Missing 'path' argument." };
            if (string.IsNullOrEmpty(content)) return new AgentResult { Success = false, Output = "Missing 'content' argument." };

            await _searchService.WriteFileAsync(path, content, ct);
            return new AgentResult 
            { 
                Success = true, 
                Output = $"File '{path}' successfully written.",
                Metadata = new Dictionary<string, string> { ["telemetry_type"] = "Edited", ["file"] = path }
            };
        }

        private async Task<AgentResult> HandleDeleteAsync(AgentRequest request, CancellationToken ct)
        {
            var path = GetPathArgument(request);
            if (string.IsNullOrEmpty(path)) return new AgentResult { Success = false, Output = "Missing 'path' argument." };

            // For safety, we only delete if the file exists in the context
            try {
                var fullPath = System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(_searchService.RootPath, path);
                if (System.IO.File.Exists(fullPath)) {
                    System.IO.File.Delete(fullPath);
                    return new AgentResult { Success = true, Output = $"File '{path}' deleted." };
                }
                return new AgentResult { Success = false, Output = "File not found." };
            } catch (Exception ex) {
                return new AgentResult { Success = false, Output = $"Delete failed: {ex.Message}" };
            }
        }

        private async Task<AgentResult> HandleListAsync(AgentRequest request, CancellationToken ct)
        {
            var path = GetPathArgument(request);
            if (string.IsNullOrEmpty(path)) path = "./";

            bool recursive = request.GetStringArgument("recursive").Equals("true", StringComparison.OrdinalIgnoreCase);

            var results = await _searchService.ListDirectoryAsync(path, recursive, ct);
            
            var sb = new StringBuilder();
            sb.AppendLine($"Listing: {path} ({(recursive ? "recursive" : "top-level only")})");
            sb.AppendLine($"Found {results.Count} items:");
            foreach (var r in results) 
            {
                sb.AppendLine($"- {r}");
            }
            
            var output = sb.ToString();
            if (output.Length > 24000) {
                output = output.Substring(0, 24000) + "\n\n... [OUTPUT TRUNCATED: Use 'search' or drill down into subdirectories.] ...";
            }
            
            return new AgentResult { Success = true, Output = output, Data = results };
        }

        private async Task<AgentResult> HandleSearchAsync(AgentRequest request, CancellationToken ct)
        {
            var pattern = request.GetStringArgument("pattern");
            if (string.IsNullOrEmpty(pattern))
                return new AgentResult { Success = false, Output = "Missing 'pattern' argument." };

            var results = await _searchService.SearchFilesAsync(pattern, ct);
            if (results.Count == 0)
                return new AgentResult { Success = true, Output = "No files matched.", Data = results };
            
            var sb = new StringBuilder();
            foreach (var r in results) sb.AppendLine(r);
            
            var outputStr = sb.ToString();
            if (outputStr.Length > 24000) {
                outputStr = outputStr.Substring(0, 24000) + "\n\n... [OUTPUT TRUNCATED: Result too large. Try a more specific search pattern] ...";
            }
            
            return new AgentResult { Success = true, Output = outputStr, Data = results };
        }

        private async Task<AgentResult> HandleReadAsync(AgentRequest request, CancellationToken ct)
        {
            var path = GetPathArgument(request);
            if (string.IsNullOrEmpty(path))
                return new AgentResult { Success = false, Output = "Missing 'path' argument." };

            string content;
            try
            {
                content = await _searchService.ReadFileAsync(path, ct);
            }
            catch (Exception ex) when (ex is System.IO.FileNotFoundException || ex is System.IO.DirectoryNotFoundException)
            {
                return new AgentResult { Success = false, Output = $"File not found at path: {path} (Details: {ex.Message}). Tip: Use the 'list' verb or 'search' to verify the path." };
            }
            catch (Exception ex)
            {
                return new AgentResult { Success = false, Output = $"Failed to read file: {ex.Message}" };
            }
            
            var startLineStr = request.GetStringArgument("start_line");
            var endLineStr = request.GetStringArgument("end_line");
            
            var lines = content.Replace("\r\n", "\n").Split('\n');
            int startLine = 1;
            int endLine = lines.Length;

            if (int.TryParse(startLineStr, out int sl)) startLine = Math.Max(1, sl);
            if (int.TryParse(endLineStr, out int el)) endLine = Math.Min(lines.Length, Math.Max(startLine, el));

            if (startLine > 1 || endLine < lines.Length)
            {
                var sb = new StringBuilder();
                for (int i = startLine - 1; i < endLine; i++)
                {
                    sb.AppendLine(lines[i]);
                }
                content = sb.ToString();
            }

            string output = content;
            if (output.Length > 24000) {
                output = output.Substring(0, 24000) + $"\n\n... [OUTPUT TRUNCATED: File too large ({lines.Length} lines). Try reading specific lines using 'start_line' and 'end_line' parameters.] ...";
            }
            else if (startLine > 1 || endLine < lines.Length)
            {
                output = $"[Lines {startLine}-{endLine} of {lines.Length}]\n" + output;
            }
            
            return new AgentResult 
            { 
                Success = true, 
                Output = output,
                Metadata = new Dictionary<string, string> 
                { 
                    ["telemetry_type"] = "Explored", 
                    ["file"] = Path.GetFileName(path), 
                    ["range"] = $"#L{startLine}-{endLine}" 
                }
            };
        }

        private async Task<AgentResult> HandleReplaceAsync(AgentRequest request, CancellationToken ct)
        {
            var path = GetPathArgument(request);
            if (string.IsNullOrEmpty(path))
                return new AgentResult { Success = false, Output = "Missing 'path' argument." };

            bool isPreview = request.GetStringArgument("preview").Equals("true", StringComparison.OrdinalIgnoreCase);

            string from = request.GetStringArgument("from");
            string to = request.GetStringArgument("to");

            // Check for modern args vs legacy raw arg
            if (string.IsNullOrEmpty(from) && string.IsNullOrEmpty(to) && request.ArgMap.TryGetValue("raw", out var rawObj))
            {
                var raw = rawObj?.ToString() ?? "";
                // legacy: "<from> => <to> [preview]"
                var arrow = "=>";
                var idx = raw.IndexOf(arrow);
                if (idx < 0) return new AgentResult { Success = false, Output = "replace raw syntax: <from> => <to>" };
                from = raw.Substring(0, idx).Trim();
                var right = raw.Substring(idx + arrow.Length).Trim();
                if (right.EndsWith("preview", StringComparison.OrdinalIgnoreCase))
                {
                    isPreview = true;
                    to = right.Substring(0, right.Length - 7).Trim();
                }
                else
                {
                    to = right;
                }
            }

            if (string.IsNullOrEmpty(from))
            {
                return new AgentResult { Success = false, Output = "Missing 'from' argument." };
            }

            var orig = await _searchService.ReadFileAsync(path, ct);
            
            var origNormalized = orig.Replace("\r\n", "\n");
            var fromNormalized = from.Replace("\r\n", "\n");
            
            if (!origNormalized.Contains(fromNormalized)) 
                return new AgentResult { Success = false, Output = "'from' text not found in file." };
            
            var toNormalized = to.Replace("\r\n", "\n");
            var updated = origNormalized.Replace(fromNormalized, toNormalized);

            if (orig.Contains("\r\n"))
            {
                updated = updated.Replace("\n", "\r\n");
            }

            if (isPreview)
            {
                var preview = $"--- {path} (preview)\n--- original length: {orig.Length}\n+++ updated length: {updated.Length}\n\n{GetPreviewSnippet(orig, updated)}";
                return new AgentResult { Success = true, Output = preview };
            }

            await _searchService.WriteFileAsync(path, updated, ct);
            return new AgentResult 
            { 
                Success = true, 
                Output = "File updated.",
                Metadata = new Dictionary<string, string> { ["telemetry_type"] = "Edited", ["file"] = path }
            };
        }

        private string GetPreviewSnippet(string original, string updated, int context = 40)
        {
            if (original == updated) return "(no changes)";
            var len = Math.Min(original.Length, updated.Length);
            var diffIndex = -1;
            for (int i = 0; i < len; i++)
            {
                if (original[i] != updated[i])
                {
                    diffIndex = i;
                    break;
                }
            }
            if (diffIndex == -1) diffIndex = len;

            var start = Math.Max(0, diffIndex - context);
            var oSnippet = original.Substring(start, Math.Min(context * 2, original.Length - start));
            var uSnippet = updated.Substring(start, Math.Min(context * 2, updated.Length - start));

            return $"--- original snippet:\n{oSnippet}\n\n+++ updated snippet:\n{uSnippet}";
        }
    }
}
