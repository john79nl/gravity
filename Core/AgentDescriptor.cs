using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Gravity.Core
{
    public class AgentDescriptor
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("canWrite")]
        public bool CanWrite { get; set; }

        [JsonPropertyName("supportedVerbs")]
        public string[] SupportedVerbs { get; set; } = System.Array.Empty<string>();

        [JsonPropertyName("actions")]
        public List<ActionMetadata> Actions { get; set; } = new();
    }

    public class ActionMetadata
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("isMutation")]
        public bool IsMutation { get; set; }

        [JsonPropertyName("parameters")]
        public Dictionary<string, string> Parameters { get; set; } = new();

        [JsonPropertyName("optionalParameters")]
        public List<string> OptionalParameters { get; set; } = new();
    }
}
