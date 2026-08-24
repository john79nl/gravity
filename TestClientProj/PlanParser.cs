using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Gravity.Core
{
    public static class PlanParser
    {
        // Fix common AI JSON mistakes, specifically unescaped backslashes in Windows paths.
        public static string SanitizeJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;

            // This regex finds backslashes that are NOT part of a valid JSON escape sequence.
            // Valid sequences: \", \\, \/, \b, \f, \n, \r, \t, \uXXXX
            // We use a negative lookahead to identify backslashes that don't match these.
            // Note: We specifically target backslashes in potential path patterns like C:\ or \Users.
            return Regex.Replace(json, @"(?<!\\)\\(?![uU][0-9a-fA-F]{4}|[""/\\bfnrt])", @"\\");
        }

        // Try to extract a JSON object/array from model output by finding a balanced top-level brace.
        public static bool TryExtractJson(string text, out string json)
        {

            json = string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();

            // 1) Check for fenced code block with json
            var fenced = Regex.Match(text, "```json\\s*(.*?)\\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
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
    }
}
