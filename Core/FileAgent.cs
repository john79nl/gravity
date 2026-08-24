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
        private readonly GitService _gitService;
        private readonly DocxPreviewService? _docxPreview;

        public AgentDescriptor Descriptor { get; }

        public FileAgent(FileSearchService searchService, GitService gitService, DocxPreviewService? docxPreview = null)
        {
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            _docxPreview = docxPreview;
            Descriptor = new AgentDescriptor
            {
                Name = "code_editor",
                Description = "PRIMARY tool for searching, reading, and exploring the project codebase and internal logic.",
                CanWrite = true,
                SupportedVerbs = new[] { "list_directory", "read_file", "read_range", "write_file", "search_in_files", "glob", "grep", "delete", "apply_diff", "apply_patches", "replace_block", "edit_lines" },
                Actions = new List<ActionMetadata>
                {
                    new ActionMetadata { Name = "list_directory", Description = "List details of a directory. Defaults to root. Use for exploration.", IsMutation = false, Parameters = new Dictionary<string, string> { ["path"] = "directory to list", ["recursive"] = "optional 'true'/'false' (default false)" }, OptionalParameters = new List<string> { "path", "recursive" } },
                    new ActionMetadata { Name = "read_file", Description = "Read file content. Use for exploring or copying code.", IsMutation = false, Parameters = new Dictionary<string, string> { ["path"] = "file path" } },
                    new ActionMetadata { Name = "read_range", Description = "Read specific line range from a file.", IsMutation = false, Parameters = new Dictionary<string, string> { ["path"] = "file path", ["start_line"] = "start line number", ["end_line"] = "end line number" } },
                    new ActionMetadata { Name = "write_file", Description = "Create a new file or completely write content. For editing existing code files, PREFER edit_lines or apply_diff to prevent code loss.", IsMutation = true, Parameters = new Dictionary<string, string> { ["path"] = "file path", ["content"] = "file content" }, OptionalParameters = new List<string> { "content" } },
                    new ActionMetadata { Name = "search_in_files", Description = "Search codebase. REQUIRED: pattern (Internal keyword or regex).", IsMutation = false, Parameters = new Dictionary<string, string> { ["pattern"] = "Keyword or regex" } },
                    new ActionMetadata { Name = "glob", Description = "Find files by glob pattern. Examples: **/*.cs, **/*.csproj", IsMutation = false, Parameters = new Dictionary<string, string> { ["pattern"] = "Glob pattern (e.g. **/*.csproj)" } },
                    new ActionMetadata { Name = "grep", Description = "Alias for search_in_files — search file contents by keyword or regex.", IsMutation = false, Parameters = new Dictionary<string, string> { ["pattern"] = "Keyword or regex" } },
                    new ActionMetadata { Name = "delete", Description = "Permanently remove a file.", IsMutation = true, Parameters = new Dictionary<string, string> { ["path"] = "file path" } },
                    new ActionMetadata { Name = "apply_diff", Description = "Replace text in a file. REQUIRED: path, from, to. Include large enough context — entire methods, not just one line. For multiple changes at once, use apply_patches instead.", IsMutation = true, Parameters = new Dictionary<string, string> { ["path"] = "file path", ["from"] = "original text (include full context — entire method/block)", ["to"] = "new text" } },
                    new ActionMetadata { Name = "apply_patches", Description = "Apply multiple text replacements to a file in one call. REQUIRED: path, patches (JSON array of {from, to} objects). Use this instead of multiple apply_diff calls. Each patch is a {from, to} pair.", IsMutation = true, Parameters = new Dictionary<string, string> { ["path"] = "file path", ["patches"] = "JSON array: [{\"from\": \"old1\", \"to\": \"new1\"}, {\"from\": \"old2\", \"to\": \"new2\"}]" } },
                    new ActionMetadata { Name = "replace_block", Description = "Replace an entire method, class, or block by name. REQUIRED: path, block_name, new_body. Finds the block (method/class) containing block_name and replaces it entirely. Use for large changes to methods or classes.", IsMutation = true, Parameters = new Dictionary<string, string> { ["path"] = "file path", ["block_name"] = "name of the method/class/block to replace (e.g. 'MyMethod', 'MyClass')", ["new_body"] = "the complete new replacement for the block" } },
                    new ActionMetadata { Name = "edit_lines", Description = "PREFERRED surgical edit tool. Replace a specific line range in a file. REQUIRED: path, start_line, end_line, new_content. Always read the file first to get exact line numbers. Returns a diff of what changed. Use this instead of apply_diff whenever possible.", IsMutation = true, Parameters = new Dictionary<string, string> { ["path"] = "file path", ["start_line"] = "first line to replace (1-indexed, inclusive)", ["end_line"] = "last line to replace (1-indexed, inclusive)", ["new_content"] = "replacement text (replaces lines start_line through end_line)" } }
                }
            };
        }

        public async Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Verb))
                return new AgentResult { Success = false, Output = "Invalid request." };

            try
            {
                switch (request.Verb.ToLowerInvariant())
                {
                    case "search_in_files":
                    case "search":
                    case "grep":
                        return await HandleSearchAsync(request, ct);
                    case "read_file":
                    case "read_range":
                    case "read":
                        return await HandleReadAsync(request, ct);
                    case "apply_diff":
                    case "replace":
                    case "edit":
                        return await HandleReplaceAsync(request, ct);
                    case "apply_patches":
                    case "patches":
                        return await HandleApplyPatchesAsync(request, ct);
                    case "replace_block":
                    case "block":
                        return await HandleReplaceBlockAsync(request, ct);
                    case "edit_lines":
                    case "edit_range":
                        return await HandleEditLinesAsync(request, ct);
                    case "write_file":
                    case "write":
                        return await HandleWriteAsync(request, ct);
                    case "delete":
                        return await HandleDeleteAsync(request, ct);
                    case "list_directory":
                    case "list":
                        return await HandleListAsync(request, ct);
                    case "glob":
                        return await HandleGlobAsync(request, ct);
                    default:
                        return new AgentResult { Success = false, Output = $"Unknown verb '{request.Verb}'." };
                }
            }
            catch (Exception ex)
            {
                return new AgentResult { Success = false, Output = $"File operation failed: {ex.Message}. Verify that the path is correct and exists." };
            }
        }

        private string GetPathArgument(AgentRequest request)
        {
            return request.GetStringArgument("path");
        }

        private async Task<AgentResult> HandleWriteAsync(AgentRequest request, CancellationToken ct)
        {
            var path = GetPathArgument(request);
            if (string.IsNullOrEmpty(path)) return new AgentResult { Success = false, Output = "Missing 'path' argument." };
            var content = request.GetStringArgument("content", string.Empty);

            try
            {
                if (path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    var fullPath = System.IO.Path.IsPathRooted(path)
                        ? path
                        : System.IO.Path.GetFullPath(System.IO.Path.Combine(_searchService.RootPath ?? ".", path));
                    
                    DocxPreviewService.WriteDocxFromTextOrHtml(fullPath, content);

                    if (_docxPreview != null)
                    {
                        _docxPreview.QueuePreview(fullPath);
                    }
                }
                else
                {
                    await _searchService.WriteFileAsync(path, content, ct);
                }

                return new AgentResult
                {
                    Success = true,
                    Output = $"File '{path}' successfully written.",
                    Metadata = new Dictionary<string, string> { ["telemetry_type"] = "Edited", ["file"] = path }
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new AgentResult { Success = false, Output = $"Access denied writing to '{path}'. Check file permissions or whether the file is locked." };
            }
            catch (System.IO.DirectoryNotFoundException)
            {
                return new AgentResult { Success = false, Output = $"Directory not found for path '{path}'. Verify the parent directory exists." };
            }
            catch (Exception ex)
            {
                return new AgentResult { Success = false, Output = $"Failed to write file '{path}': {ex.Message}" };
            }
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

            // Safety guard: block recursive listing on the workspace root.
            // A recursive root scan returns thousands of items and floods the model context.
            if (recursive)
            {
                var root = _searchService.RootPath ?? "";
                var resolvedPath = System.IO.Path.IsPathRooted(path)
                    ? path
                    : System.IO.Path.GetFullPath(System.IO.Path.Combine(root, path));
                var resolvedRoot = System.IO.Path.GetFullPath(root);

                if (string.Equals(resolvedPath.TrimEnd('\\', '/'), resolvedRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
                    path == "./" || path == "." || path == "/")
                {
                    return new AgentResult
                    {
                        Success = false,
                        Output = "[BLOCKED] Recursive listing of the workspace root is not allowed — it returns thousands of items and will crash your context.\n" +
                                 "Instead, do ONE of the following:\n" +
                                 "  1. Use code_editor.list_directory on a specific subdirectory (e.g. path='Pages' or path='Data')\n" +
                                 "  2. Use code_editor.glob to find files by type (e.g. pattern='**/*.cs' or '**/*.csproj')\n" +
                                 "  3. Use code_editor.read_file on a specific file like 'Program.cs' or the .csproj file"
                    };
                }
            }

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
                // Try to find it if it's just a filename or partial path
                if (!path.Contains("/") && !path.Contains("\\"))
                {
                    try
                    {
                        var matches = await _searchService.SearchFilesAsync($"**/{path}", ct);
                        if (matches != null && matches.Count == 1)
                        {
                            var resolvedContent = await _searchService.ReadFileAsync(matches[0], ct);
                            return new AgentResult { Success = true, Output = resolvedContent };
                        }
                        else if (matches != null && matches.Count > 1)
                        {
                            var matchPaths = string.Join("\n", matches.Take(10));
                            return new AgentResult { Success = false, Output = $"Multiple files match '{path}'. Please specify the exact path from these options:\n{matchPaths}" };
                        }
                    }
                    catch { }
                }

                return new AgentResult { Success = false, Output = $"File not found at path: {path} (Details: {ex.Message}). Tip: Use the 'list' verb or 'search' to verify the path." };
            }
            catch (UnauthorizedAccessException uaEx)
            {
                // Surface directory vs ACL/permissions issues separately
                if (uaEx.Message.Contains("is a directory") || uaEx.Message.Contains("cannot be read as a file"))
                {
                    return new AgentResult { Success = false, Output = $"Failed to read file: Path '{path}' is a directory. Use 'list' to inspect directory contents or provide a file path." };
                }
                return new AgentResult { Success = false, Output = $"Failed to read file: Access denied for path '{path}'. This may be due to file system permissions or locked files. Run the tool with an account that has read access or adjust the path." };
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

            // Attach git diff if file has uncommitted changes
            if (startLine == 1 && endLine == lines.Length)
            {
                try
                {
                    var diff = await _gitService.GetFileDiffAsync(path);
                    if (!string.IsNullOrEmpty(diff))
                    {
                        output += "\n\n" + diff;
                    }
                }
                catch { }
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
            var toNormalized = to.Replace("\r\n", "\n");
            bool fuzzyUsed;
            var updated = ReplaceWithFuzzyFallback(origNormalized, fromNormalized, toNormalized, out fuzzyUsed);

            if (updated == "AMBIGUOUS_MULTIPLE_MATCHES")
            {
                return new AgentResult { Success = false, Output = "Target 'from' text was found MULTIPLE times in the file. Please include more surrounding context lines (e.g. full method signature or enclosing braces) to make the replacement unique." };
            }

            if (updated == null)
            {
                var lines = orig.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var snippet = string.Join("\n", lines.Select((l, idx) => $"{idx + 1}: {l}").Take(150));
                return new AgentResult { Success = false, Output = $"'from' text not found in file (even with fuzzy whitespace matching).\n\n[Auto-Diagnostics] The file might have already been updated, or the text is different. Here are the first 150 lines of the current file:\n{snippet}" };
            }

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

        private async Task<AgentResult> HandleApplyPatchesAsync(AgentRequest request, CancellationToken ct)
        {
            var path = GetPathArgument(request);
            if (string.IsNullOrEmpty(path))
                return new AgentResult { Success = false, Output = "Missing 'path' argument." };

            var patchesRaw = request.GetStringArgument("patches");
            if (string.IsNullOrEmpty(patchesRaw))
                return new AgentResult { Success = false, Output = "Missing 'patches' argument. Expected JSON array: [{\"from\": \"old\", \"to\": \"new\"}, ...]" };

            // Parse patches JSON
            List<KeyValuePair<string, string>> patches;
            try
            {
                patches = ParsePatches(patchesRaw);
            }
            catch (Exception ex)
            {
                return new AgentResult { Success = false, Output = $"Failed to parse patches JSON: {ex.Message}" };
            }

            if (patches.Count == 0)
                return new AgentResult { Success = false, Output = "No patches provided." };

            var content = await _searchService.ReadFileAsync(path, ct);
            var contentNorm = content.Replace("\r\n", "\n");

            var applied = new List<string>();
            var failed = new List<string>();

            for (int i = 0; i < patches.Count; i++)
            {
                var fromNorm = patches[i].Key.Replace("\r\n", "\n");
                var toNorm = patches[i].Value.Replace("\r\n", "\n");

                bool fuzzyUsed;
                var updatedContent = ReplaceWithFuzzyFallback(contentNorm, fromNorm, toNorm, out fuzzyUsed);

                if (updatedContent == null)
                {
                    failed.Add($"Patch {i + 1}: 'from' text not found");
                    continue;
                }

                contentNorm = updatedContent;
                applied.Add($"Patch {i + 1}: applied{(fuzzyUsed ? " (fuzzy match)" : "")}");
            }

            if (applied.Count == 0)
            {
                var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var snippet = string.Join("\n", lines.Select((l, idx) => $"{idx + 1}: {l}").Take(150));
                var msg = $"All {failed.Count} patches failed:\n{string.Join("\n", failed)}\n\n[Auto-Diagnostics] The 'from' text was not found. The file might have already been updated, or the text is different. Here are the first 150 lines of the current file:\n{snippet}";
                return new AgentResult { Success = false, Output = msg };
            }

            // Preserve original line endings
            if (content.Contains("\r\n"))
                contentNorm = contentNorm.Replace("\n", "\r\n");

            await _searchService.WriteFileAsync(path, contentNorm, ct);

            var resultMsg = $"Applied {applied.Count}/{patches.Count} patches to '{path}'.\n{string.Join("\n", applied)}";
            if (failed.Count > 0)
                resultMsg += $"\nFailed patches:\n{string.Join("\n", failed)}";

            return new AgentResult
            {
                Success = true,
                Output = resultMsg,
                Metadata = new Dictionary<string, string> { ["telemetry_type"] = "Edited", ["file"] = path }
            };
        }

        private string? ReplaceWithFuzzyFallback(string content, string from, string to, out bool usedFuzzy)
        {
            usedFuzzy = false;

            // Exact match check (after CRLF normalisation)
            int firstExact = content.IndexOf(from, StringComparison.Ordinal);
            if (firstExact >= 0)
            {
                int secondExact = content.IndexOf(from, firstExact + from.Length, StringComparison.Ordinal);
                if (secondExact >= 0)
                {
                    return "AMBIGUOUS_MULTIPLE_MATCHES";
                }
                return content.Remove(firstExact, from.Length).Insert(firstExact, to);
            }

            // Fuzzy fallback: only relax *internal* whitespace between tokens.
            var parts = from.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null; // 'from' was only whitespace — nothing to match

            var escapedParts = parts.Select(System.Text.RegularExpressions.Regex.Escape);
            var innerPattern = string.Join(@"\s+", escapedParts);

            var matches = System.Text.RegularExpressions.Regex.Matches(
                content, innerPattern, System.Text.RegularExpressions.RegexOptions.Multiline);

            if (matches.Count == 0) return null;
            if (matches.Count > 1) return "AMBIGUOUS_MULTIPLE_MATCHES";

            var match = matches[0];

            // Preserve the leading indentation of the matched region
            var matchStart  = match.Index;
            var lineStart   = content.LastIndexOf('\n', matchStart < 1 ? 0 : matchStart - 1) + 1;
            var leadingIndent = new string(content.Skip(lineStart).TakeWhile(c => c == ' ' || c == '\t').ToArray());

            // Re-indent every line of 'to' to match the original indentation
            var toLines   = to.Split('\n');
            var reindented = string.Join("\n", toLines.Select((line, idx) =>
                idx == 0 ? line : (string.IsNullOrWhiteSpace(line) ? line : leadingIndent + line.TrimStart())));

            usedFuzzy = true;
            return content.Remove(matchStart, match.Length).Insert(matchStart, reindented);
        }

        private static List<KeyValuePair<string, string>> ParsePatches(string json)
        {
            var result = new List<KeyValuePair<string, string>>();
            json = json.Trim();

            // Manual parse: find {from:..., to:...} blocks
            // Supports both {"from":"...","to":"..."} and { "from": "...", "to": "..." }
            int i = 0;
            while (i < json.Length)
            {
                // Find next "from"
                int fromIdx = json.IndexOf("\"from\"", i, StringComparison.OrdinalIgnoreCase);
                if (fromIdx < 0) break;

                int colonAfterFrom = json.IndexOf(':', fromIdx + 6);
                if (colonAfterFrom < 0) break;

                var fromResult = ExtractJsonValue(json, colonAfterFrom + 1);
                if (fromResult == null) break;
                var fromParsed = fromResult.Value;

                int toIdx = json.IndexOf("\"to\"", fromParsed.EndIndex, StringComparison.OrdinalIgnoreCase);
                if (toIdx < 0) break;

                int colonAfterTo = json.IndexOf(':', toIdx + 4);
                if (colonAfterTo < 0) break;

                var toResult = ExtractJsonValue(json, colonAfterTo + 1);
                if (toResult == null) break;
                var toParsed = toResult.Value;

                result.Add(new KeyValuePair<string, string>(fromParsed.Value, toParsed.Value));
                i = toParsed.EndIndex;
            }

            return result;
        }

        private static (string Value, int EndIndex)? ExtractJsonValue(string json, int startIndex)
        {
            // Skip whitespace
            while (startIndex < json.Length && char.IsWhiteSpace(json[startIndex]))
                startIndex++;

            if (startIndex >= json.Length) return null;

            if (json[startIndex] == '"')
            {
                // String value
                var sb = new StringBuilder();
                int j = startIndex + 1;
                while (j < json.Length)
                {
                    if (json[j] == '\\' && j + 1 < json.Length)
                    {
                        sb.Append(json[j + 1]);
                        j += 2;
                    }
                    else if (json[j] == '"')
                    {
                        return (sb.ToString(), j + 1);
                    }
                    else
                    {
                        sb.Append(json[j]);
                        j++;
                    }
                }
            }
            else
            {
                // Non-string value — read until comma or closing brace
                int j = startIndex;
                while (j < json.Length && json[j] != ',' && json[j] != '}' && json[j] != ']')
                    j++;
                return (json.Substring(startIndex, j - startIndex).Trim(), j);
            }

            return null;
        }

        private async Task<AgentResult> HandleReplaceBlockAsync(AgentRequest request, CancellationToken ct)
        {
            var path = GetPathArgument(request);
            if (string.IsNullOrEmpty(path))
                return new AgentResult { Success = false, Output = "Missing 'path' argument." };

            var blockName = request.GetStringArgument("block_name");
            if (string.IsNullOrEmpty(blockName))
                return new AgentResult { Success = false, Output = "Missing 'block_name' argument. Provide the name of the method/class/block to replace." };

            var newBody = request.GetStringArgument("new_body");
            if (string.IsNullOrEmpty(newBody))
                return new AgentResult { Success = false, Output = "Missing 'new_body' argument. Provide the complete replacement for the block." };

            var content = await _searchService.ReadFileAsync(path, ct);
            var lines = content.Replace("\r\n", "\n").Split('\n');

            // Find the block containing blockName
            // Strategy: find the line containing blockName, then walk up to find the opening brace
            // and down to find the matching closing brace
            int blockLineIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(blockName, StringComparison.Ordinal))
                {
                    blockLineIndex = i;
                    break;
                }
            }

            if (blockLineIndex < 0)
                return new AgentResult { Success = false, Output = $"Block '{blockName}' not found in '{path}'." };

            // Walk up to find opening brace of the block (look for 'public/private/protected/internal/static' line)
            int blockStart = blockLineIndex;
            for (int i = blockLineIndex; i >= 0; i--)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("public") || trimmed.StartsWith("private") || trimmed.StartsWith("protected") ||
                    trimmed.StartsWith("internal") || trimmed.StartsWith("static") || trimmed.StartsWith("override") ||
                    trimmed.StartsWith("virtual") || trimmed.StartsWith("async") || trimmed.StartsWith("sealed") ||
                    trimmed.StartsWith("abstract") || trimmed.StartsWith("new"))
                {
                    blockStart = i;
                    break;
                }
                // Also handle non-access-modifier declarations (e.g. local functions, lambda)
                if (trimmed.Contains('{') || trimmed.Contains("=>"))
                {
                    blockStart = i;
                    break;
                }
            }

            // Find the opening brace
            int braceStart = -1;
            for (int i = blockStart; i <= blockLineIndex; i++)
            {
                if (lines[i].Contains('{'))
                {
                    braceStart = i;
                    break;
                }
                // Expression-bodied: "public int Foo() => ..."
                if (lines[i].Contains("=>"))
                {
                    braceStart = i;
                    break;
                }
            }

            if (braceStart < 0)
                return new AgentResult { Success = false, Output = $"Could not find the opening brace for block '{blockName}'." };

            // If expression-bodied (=>), replace until semicolon
            if (lines[braceStart].Contains("=>") && !lines[braceStart].Contains('{'))
            {
                int exprEnd = -1;
                for (int i = braceStart; i < lines.Length; i++)
                {
                    if (lines[i].TrimEnd().EndsWith(";"))
                    {
                        exprEnd = i;
                        break;
                    }
                }
                if (exprEnd < 0)
                    return new AgentResult { Success = false, Output = $"Could not find end of expression body for '{blockName}'." };

                // Replace lines blockStart..exprEnd with newBody
                var newLines = new List<string>(lines);
                var newBodyLines = newBody.Replace("\r\n", "\n").Split('\n');
                newLines.RemoveRange(blockStart, exprEnd - blockStart + 1);
                newLines.InsertRange(blockStart, newBodyLines);

                var newContent = string.Join("\n", newLines);
                if (content.Contains("\r\n"))
                    newContent = newContent.Replace("\n", "\r\n");

                await _searchService.WriteFileAsync(path, newContent, ct);
                return new AgentResult
                {
                    Success = true,
                    Output = $"Replaced block '{blockName}' (expression body, lines {blockStart + 1}-{exprEnd + 1}).",
                    Metadata = new Dictionary<string, string> { ["telemetry_type"] = "Edited", ["file"] = path }
                };
            }

            // Find matching closing brace using brace counting
            int braceDepth = 0;
            int blockEnd = -1;
            for (int i = braceStart; i < lines.Length; i++)
            {
                foreach (char c in lines[i])
                {
                    if (c == '{') braceDepth++;
                    else if (c == '}') braceDepth--;
                }
                if (braceDepth == 0)
                {
                    blockEnd = i;
                    break;
                }
            }

            if (blockEnd < 0)
                return new AgentResult { Success = false, Output = $"Could not find the closing brace for block '{blockName}'." };

            // Replace lines blockStart..blockEnd with newBody
            var replacementLines = new List<string>(lines);
            var replacementBodyLines = newBody.Replace("\r\n", "\n").Split('\n');
            replacementLines.RemoveRange(blockStart, blockEnd - blockStart + 1);
            replacementLines.InsertRange(blockStart, replacementBodyLines);

            var updatedContent = string.Join("\n", replacementLines);
            if (content.Contains("\r\n"))
                updatedContent = updatedContent.Replace("\n", "\r\n");

            await _searchService.WriteFileAsync(path, updatedContent, ct);
            return new AgentResult
            {
                Success = true,
                Output = $"Replaced block '{blockName}' (lines {blockStart + 1}-{blockEnd + 1}).",
                Metadata = new Dictionary<string, string> { ["telemetry_type"] = "Edited", ["file"] = path }
            };
        }

        private async Task<AgentResult> HandleGlobAsync(AgentRequest request, CancellationToken ct)
        {
            var pattern = request.GetStringArgument("pattern");
            if (string.IsNullOrEmpty(pattern))
                return new AgentResult { Success = false, Output = "Missing 'pattern' argument." };

            try
            {
                var root = _searchService.RootPath;
                if (string.IsNullOrEmpty(root))
                    return new AgentResult { Success = false, Output = "No workspace root set." };

                // Handle **/ recursive glob patterns
                string searchDir = root;
                string filePattern = pattern;
                SearchOption option = SearchOption.TopDirectoryOnly;

                if (pattern.StartsWith("**/", StringComparison.Ordinal))
                {
                    filePattern = pattern.Substring(3);
                    option = SearchOption.AllDirectories;
                }
                else
                {
                    var doubleStarIdx = pattern.IndexOf("/**/", StringComparison.Ordinal);
                    if (doubleStarIdx >= 0)
                    {
                        var dirPart = pattern.Substring(0, doubleStarIdx);
                        searchDir = Path.IsPathRooted(dirPart) ? dirPart : Path.Combine(root, dirPart);
                        filePattern = pattern.Substring(doubleStarIdx + 4);
                        option = SearchOption.AllDirectories;
                    }
                    else
                    {
                        searchDir = root;
                        filePattern = pattern;
                        // Single-level pattern (e.g. *.cs) stays TopDirectoryOnly
                        if (filePattern.Contains('/') || filePattern.Contains('\\'))
                        {
                            var fileDir = Path.GetDirectoryName(filePattern);
                            if (!string.IsNullOrEmpty(fileDir))
                            {
                                searchDir = Path.Combine(root, fileDir);
                                filePattern = Path.GetFileName(filePattern);
                            }
                            option = SearchOption.TopDirectoryOnly;
                        }
                    }
                }

                if (!Directory.Exists(searchDir))
                    return new AgentResult { Success = false, Output = $"Directory not found: {searchDir}" };

                var excludeDirs = new[] { "bin", "obj", ".vs", ".git", ".gemini", "node_modules" };
                var files = Directory.EnumerateFiles(searchDir, filePattern, option)
                    .Where(f => !excludeDirs.Any(d => f.Contains(Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar)
                        || f.Contains(Path.AltDirectorySeparatorChar + d + Path.AltDirectorySeparatorChar)))
                    .Select(f => Path.GetRelativePath(root, f))
                    .ToList();

                if (files.Count == 0)
                    return new AgentResult { Success = true, Output = $"No files matched '{pattern}'." };

                var sb = new StringBuilder();
                sb.AppendLine($"Found {files.Count} file(s) matching '{pattern}':");
                foreach (var f in files)
                    sb.AppendLine($"  {f}");

                var output = sb.ToString();
                if (output.Length > 24000)
                {
                    output = output.Substring(0, 24000) + $"\n\n... [OUTPUT TRUNCATED: {files.Count} files matched. Use a more specific pattern.] ...";
                }

                return new AgentResult { Success = true, Output = output, Data = files };
            }
            catch (Exception ex)
            {
                return new AgentResult { Success = false, Output = $"Glob failed: {ex.Message}" };
            }
        }

        // ── edit_lines ────────────────────────────────────────────────────────
        // Replaces a contiguous block of lines (start_line..end_line, 1-indexed,
        // both inclusive) with new_content. This is the preferred surgical edit
        // primitive because it is anchored to line numbers already visible in
        // read_file output — no fragile string matching required.
        private async Task<AgentResult> HandleEditLinesAsync(AgentRequest request, CancellationToken ct)
        {
            var path = GetPathArgument(request);
            if (string.IsNullOrEmpty(path))
                return new AgentResult { Success = false, Output = "Missing 'path' argument." };

            var startLineStr = request.GetStringArgument("start_line");
            var endLineStr   = request.GetStringArgument("end_line");
            var newContent   = request.GetStringArgument("new_content") ?? string.Empty;

            if (!int.TryParse(startLineStr, out int startLine) || startLine < 1)
                return new AgentResult { Success = false, Output = $"Invalid 'start_line': '{startLineStr}'. Must be a positive integer (1-indexed)." };

            if (!int.TryParse(endLineStr, out int endLine) || endLine < startLine)
                return new AgentResult { Success = false, Output = $"Invalid 'end_line': '{endLineStr}'. Must be an integer >= start_line ({startLine})." };

            string original;
            try
            {
                original = await _searchService.ReadFileAsync(path, ct);
            }
            catch (Exception ex)
            {
                return new AgentResult { Success = false, Output = $"Failed to read '{path}': {ex.Message}" };
            }

            var useCrlf = original.Contains("\r\n");
            var lines   = original.Replace("\r\n", "\n").Split('\n').ToList();

            if (startLine > lines.Count)
                return new AgentResult { Success = false, Output = $"start_line ({startLine}) exceeds file length ({lines.Count} lines). Read the file first to verify line numbers." };

            // Clamp end_line to actual file length
            endLine = Math.Min(endLine, lines.Count);

            // Capture removed lines for the diff summary
            var removedLines = lines.Skip(startLine - 1).Take(endLine - startLine + 1).ToList();

            // Split replacement into lines; strip a trailing empty entry caused by a trailing \n
            var replacementLines = newContent.Replace("\r\n", "\n").Split('\n').ToList();
            if (replacementLines.Count > 0 && replacementLines[^1] == string.Empty && newContent.EndsWith("\n"))
                replacementLines.RemoveAt(replacementLines.Count - 1);

            // Splice
            lines.RemoveRange(startLine - 1, endLine - startLine + 1);
            lines.InsertRange(startLine - 1, replacementLines);

            var updated = string.Join("\n", lines);
            if (useCrlf)
                updated = updated.Replace("\n", "\r\n");

            try
            {
                await _searchService.WriteFileAsync(path, updated, ct);
            }
            catch (Exception ex)
            {
                return new AgentResult { Success = false, Output = $"Failed to write '{path}': {ex.Message}" };
            }

            // Build a mini unified diff for the observation
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"edit_lines applied to '{path}' — lines {startLine}-{endLine} replaced.");
            sb.AppendLine();
            sb.AppendLine("--- removed");
            foreach (var l in removedLines)  sb.AppendLine($"- {l}");
            sb.AppendLine("+++ added");
            foreach (var l in replacementLines) sb.AppendLine($"+ {l}");

            return new AgentResult
            {
                Success  = true,
                Output   = sb.ToString(),
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
