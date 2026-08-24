using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gravity.Core
{
    public static class PlanParser
    {
        // Fix common AI JSON mistakes, specifically:
        // 1. Unescaped backslashes in Windows paths
        // 2. Unquoted identifier values after "name": (common with local LLMs)
        public static string SanitizeJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;

            // Fix 1: unescaped backslashes in Windows paths
            json = Regex.Replace(json, @"(?<!\\)\\(?![uU][0-9a-fA-F]{4}|[""/\\bfnrt])", @"\\");

            // Fix 2: unquoted identifiers after "name": (e.g. "name": call_gravity_agent)
            json = Regex.Replace(json, @"""name""\s*:\s*([a-zA-Z_][a-zA-Z0-9_\-]*)", @"""name"": ""$1""");

            return json;
        }

        // Try to extract a JSON object/array from model output by finding a balanced top-level brace.
        public static bool TryExtractJson(string text, out string json)
        {

            json = string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();

            // 0) Check for <tool>...</tool> tags first to avoid being thrown off by other JSON/braces in thoughts
            var toolTagMatch = Regex.Match(text, @"<tool\b[^>]*>(.*?)</tool>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (toolTagMatch.Success)
            {
                var content = toolTagMatch.Groups[1].Value.Trim();
                if (TryExtractJson(content, out var finalJson))
                {
                    json = finalJson;
                    return true;
                }
            }

            // 1) Check for fenced code block with optional json marker
            var fenced = Regex.Match(text, @"```(?:json|JSON)?\s*(.*?)\s*```", RegexOptions.Singleline);
            if (fenced.Success)
            {
                var content = fenced.Groups[1].Value.Trim();
                // Recursively call to find the actual JSON start within this block
                if (TryExtractJson(content, out var finalJson))
                {
                    json = finalJson;
                    return true;
                }
            }

            // 2) Find first object or array starter and extract balanced block
            int startIdx = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '{' || text[i] == '[')
                {
                    startIdx = i;
                    break;
                }
            }

            if (startIdx >= 0)
            {
                var sb = new StringBuilder();
                int level = 0;
                bool inString = false;
                for (int i = startIdx; i < text.Length; i++)
                {
                    var c = text[i];
                    sb.Append(c);

                    if (c == '"')
                    {
                        // toggle string state unless escaped
                        var esc = i > 0 && text[i - 1] == '\\';
                        if (!esc) inString = !inString;
                    }

                    if (inString) continue;

                    if (c == '{' || c == '[') level++;
                    else if (c == '}' || c == ']') level--;

                    if (level == 0)
                    {
                        json = sb.ToString();
                        return true;
                    }
                }
            }

            return false;
        }

        // Regex for valid tool/function names — must be identifier-safe (no spaces). Also supports dots for dot-notation.
        private static readonly Regex ValidNameRegex = new Regex(@"^[a-zA-Z0-9_\-\.]+$", RegexOptions.Compiled);

        // Try to repair common JSON mistakes heuristically
        private static bool TryRepairJson(string input, out string repaired)
        {
            repaired = input;
            if (string.IsNullOrWhiteSpace(input)) return false;

            try
            {
                // Remove trailing commas before closing braces/brackets
                repaired = Regex.Replace(repaired, @",\s*(\}|\])", "$1");

                // Strip obvious trailing non-json characters (e.g. stray ')' or text after the JSON block)
                var endIdx = repaired.LastIndexOf('}');
                var endIdx2 = repaired.LastIndexOf(']');
                var last = Math.Max(endIdx, endIdx2);
                if (last >= 0 && last < repaired.Length - 1)
                {
                    repaired = repaired.Substring(0, last + 1);
                }

                // Fix unescaped newlines inside strings by replacing literal newlines with \n
                repaired = repaired.Replace("\r\n", "\\n").Replace("\n", "\\n");

                // Final sanitize pass (backslashes, unquoted names)
                repaired = SanitizeJson(repaired);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tries to extract one or more ToolCall objects from raw content text.
        /// Supports:
        ///   - Legacy format: {"action":"tool","tool":"X","request":{...}}
        ///   - Legacy final:  {"action":"final","output":"Y"}
        ///   - Modern native: {"name":"X","arguments":{...}} or {"function":{"name":"X",...}}
        ///   - Wrapped:       {"tool_calls":[...]}
        ///   - Array:         [{...},{...}]
        /// </summary>
        public static List<ToolCall> TryParseToolCallsFromContent(string content)
        {
            var list = new List<ToolCall>();
            if (string.IsNullOrWhiteSpace(content)) return list;

            // Check for arrow format: tool.name => { ... }
            var arrowMatches = Regex.Matches(content, @"([a-zA-Z0-9_\-\.]+)\s*=>\s*(\{)");
            foreach (Match match in arrowMatches)
            {
                var name = match.Groups[1].Value.Trim();
                int braceIndex = match.Groups[2].Index;
                var restOfContent = content.Substring(braceIndex);
                if (TryExtractJson(restOfContent, out var jsonArgs))
                {
                    string id = "call_" + Guid.NewGuid().ToString("N").Substring(0, 12);
                    list.Add(new ToolCall
                    {
                        Id = id,
                        Type = "function",
                        Function = new ToolCallFunction { Name = name, Arguments = jsonArgs }
                    });
                }
            }
            if (list.Count > 0) return list;

            if (!TryExtractJson(content, out var jsonStr)) return list;

            try
            {
                var sanitized = SanitizeJson(jsonStr);
                JsonDocument doc = null!;
                try
                {
                    doc = JsonDocument.Parse(sanitized);
                }
                catch
                {
                    // Attempt to repair common JSON mistakes and retry
                    if (TryRepairJson(sanitized, out var repaired))
                    {
                        try { doc = JsonDocument.Parse(repaired); }
                        catch { /* fallthrough to outer catch */ }
                    }
                }

                if (doc == null) return list;
                using (doc)
                {
                    var root = doc.RootElement;

                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in root.EnumerateArray())
                        {
                            var tc = ParseSingleToolCall(elem);
                            if (tc != null) list.Add(tc);
                        }
                    }
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("tool_calls", out var tcArr) && tcArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var elem in tcArr.EnumerateArray())
                            {
                                var tc = ParseSingleToolCall(elem);
                                if (tc != null) list.Add(tc);
                            }
                        }
                        else
                        {
                            var tc = ParseSingleToolCall(root);
                            if (tc != null) list.Add(tc);
                        }
                    }
                }
            }
            catch
            {
                // Ignore malformed JSON after repair attempts
            }

            return list;
        }

        private static ToolCall? ParseSingleToolCall(JsonElement elem)
        {
            string? name = null;
            string? arguments = null;
            string id = "call_" + Guid.NewGuid().ToString("N").Substring(0, 12);

            // ---- Legacy format: {"action":"tool","tool":"X","request":{...}} ----
            if (elem.TryGetProperty("action", out var actionProp) && actionProp.ValueKind == JsonValueKind.String)
            {
                var action = actionProp.GetString();
                if (string.Equals(action, "final", StringComparison.OrdinalIgnoreCase))
                {
                    name = "action.final";
                    var outputVal = elem.TryGetProperty("output", out var op) ? op.GetString() : null;
                    arguments = JsonSerializer.Serialize(new { output = outputVal ?? string.Empty });
                    return new ToolCall { Id = id, Type = "function", Function = new ToolCallFunction { Name = name, Arguments = arguments } };
                }
                else if (string.Equals(action, "tool", StringComparison.OrdinalIgnoreCase))
                {
                    if (elem.TryGetProperty("tool", out var toolProp) && toolProp.ValueKind == JsonValueKind.String)
                        name = toolProp.GetString();

                    if (!string.IsNullOrEmpty(name) && ValidNameRegex.IsMatch(name!))
                    {
                        // Map legacy request block to arguments JSON
                        if (elem.TryGetProperty("request", out var reqProp) && reqProp.ValueKind == JsonValueKind.Object)
                            arguments = JsonSerializer.Serialize(reqProp);
                        else
                            arguments = "{}";
                        return new ToolCall { Id = id, Type = "function", Function = new ToolCallFunction { Name = name, Arguments = arguments } };
                    }
                    return null;
                }
            }

            // ---- Modern native: top-level "name" property ----
            if (elem.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                name = nameProp.GetString();

            // ---- Nested function object: {"function":{"name":"X","arguments":"..."}} ----
            if (string.IsNullOrEmpty(name) && elem.TryGetProperty("function", out var funcProp))
            {
                if (funcProp.ValueKind == JsonValueKind.String)
                    name = funcProp.GetString();
                else if (funcProp.ValueKind == JsonValueKind.Object && funcProp.TryGetProperty("name", out var funcNameProp))
                    name = funcNameProp.GetString();
            }

            // Strict validation — reject names with spaces or special chars (prevents false positives)
            if (string.IsNullOrEmpty(name)) return null;
            if (!ValidNameRegex.IsMatch(name!))
            {
                // attempt to extract a valid token from the provided name (strip trailing punctuation)
                var m = ValidNameRegex.Match(name!);
                if (m.Success) name = m.Value;
                else return null;
            }

            // Extract arguments
            if (elem.TryGetProperty("arguments", out var argsProp))
            {
                arguments = argsProp.ValueKind == JsonValueKind.String
                    ? argsProp.GetString()
                    : JsonSerializer.Serialize(argsProp);
            }
            else if (elem.TryGetProperty("parameters", out var paramsProp))
            {
                arguments = paramsProp.ValueKind == JsonValueKind.String
                    ? paramsProp.GetString()
                    : JsonSerializer.Serialize(paramsProp);
            }
            else if (elem.TryGetProperty("params", out var paramsProp2))
            {
                arguments = paramsProp2.ValueKind == JsonValueKind.String
                    ? paramsProp2.GetString()
                    : JsonSerializer.Serialize(paramsProp2);
            }
            else if (elem.TryGetProperty("function", out var f2) && f2.ValueKind == JsonValueKind.Object)
            {
                if (f2.TryGetProperty("arguments", out var funcArgsProp))
                {
                    arguments = funcArgsProp.ValueKind == JsonValueKind.String
                        ? funcArgsProp.GetString()
                        : JsonSerializer.Serialize(funcArgsProp);
                }
                else if (f2.TryGetProperty("parameters", out var funcParamsProp))
                {
                    arguments = funcParamsProp.ValueKind == JsonValueKind.String
                        ? funcParamsProp.GetString()
                        : JsonSerializer.Serialize(funcParamsProp);
                }
                else if (f2.TryGetProperty("params", out var funcParamsProp2))
                {
                    arguments = funcParamsProp2.ValueKind == JsonValueKind.String
                        ? funcParamsProp2.GetString()
                        : JsonSerializer.Serialize(funcParamsProp2);
                }
            }

            if (elem.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                id = idProp.GetString() ?? id;

            return new ToolCall
            {
                Id = id,
                Type = "function",
                Function = new ToolCallFunction { Name = name!, Arguments = arguments ?? "{}" }
            };
        }
    }
}
