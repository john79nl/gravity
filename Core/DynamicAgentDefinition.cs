using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Gravity.Core
{
    public class DynamicAgentDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("system")]
        public string System { get; set; } = "";

        [JsonPropertyName("permissions")]
        public List<string>? Permissions { get; set; }

        [JsonPropertyName("verbs")]
        public List<string>? Verbs { get; set; }

        [JsonPropertyName("scripts")]
        public Dictionary<string, string>? Scripts { get; set; }

        [JsonPropertyName("hidden")]
        public bool Hidden { get; set; }

        [JsonPropertyName("maxSteps")]
        public int MaxSteps { get; set; } = 5;
    }
}
