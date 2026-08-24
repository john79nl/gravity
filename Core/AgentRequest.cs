using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Gravity.Core
{
    public class AgentRequest
    {
        [JsonPropertyName("operation")]
        public string Verb { get; set; } = string.Empty;

        [JsonPropertyName("verb")]
        public string LegacyVerb { set => Verb = value; }

        [JsonIgnore]
        public Dictionary<string, object> ArgMap { get; set; } = new();
 
        [JsonPropertyName("params")]
        public System.Text.Json.JsonElement Params
        {
            get => System.Text.Json.JsonSerializer.SerializeToElement(ArgMap);
            set => SetArguments(value);
        }

        [JsonPropertyName("arguments")]
        public System.Text.Json.JsonElement LegacyArguments 
        { 
            get => System.Text.Json.JsonSerializer.SerializeToElement(ArgMap);
            set => SetArguments(value); 
        }

        private void SetArguments(System.Text.Json.JsonElement value)
        {
            if (value.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                ArgMap = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(value.GetRawText()) ?? new();
            }
            else if (value.ValueKind == System.Text.Json.JsonValueKind.Array && value.GetArrayLength() > 0)
            {
                var first = value[0];
                string val = first.ValueKind == System.Text.Json.JsonValueKind.String ? first.GetString() ?? "" : first.GetRawText().Trim('"');
                ArgMap = new Dictionary<string, object> { ["args"] = val, ["input"] = val };
            }
        }
 
        [JsonExtensionData]
        public Dictionary<string, System.Text.Json.JsonElement> ExtraProperties { get; set; } = new();
 
        public string GetStringArgument(string key, string defaultValue = "")
        {
            // Primary check
            if (TryGetFromSource(key, out var val)) return val;

            // Synonym mapping for common model hallucinations
            var synonyms = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = new[] { "skill_name", "target", "filename", "file" },
                ["pattern"] = new[] { "query", "search", "term", "search_term" },
                ["path"] = new[] { "file_path", "target_path", "filename", "file", "args", "input", "pattern", "query", "target" },
                ["content"] = new[] { "body", "text", "value", "code", "data" },
                ["command_args"] = new[] { "command", "args", "arguments", "input", "command_line" }
            };

            if (synonyms.TryGetValue(key, out var syns))
            {
                foreach (var syn in syns)
                {
                    if (TryGetFromSource(syn, out var synVal)) return synVal;
                }
            }

            return defaultValue;
        }

        private bool TryGetFromSource(string key, out string value)
        {
            value = "";
            if (ArgMap.TryGetValue(key, out var val) && val != null)
            {
                value = FormatValue(val);
                return true;
            }

            if (ExtraProperties.TryGetValue(key, out var jsonElem))
            {
                value = FormatValue(jsonElem);
                return true;
            }

            return false;
        }

        private string FormatValue(object val)
        {
            if (val is string s) return s;
            if (val is System.Text.Json.JsonElement elem)
            {
                if (elem.ValueKind == System.Text.Json.JsonValueKind.String) return elem.GetString() ?? "";
                if (elem.ValueKind == System.Text.Json.JsonValueKind.Array && elem.GetArrayLength() > 0)
                {
                    var first = elem[0];
                    return first.ValueKind == System.Text.Json.JsonValueKind.String ? first.GetString() ?? "" : first.GetRawText().Trim('"');
                }
                return elem.GetRawText().Trim('"');
            }
            return val?.ToString() ?? "";
        }
    }
}
