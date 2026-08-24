using System.Text.Json.Serialization;

namespace Gravity.Core
{
    public class AgentResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("output")]
        public string? Output { get; set; }

        [JsonPropertyName("data")]
        public object? Data { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, string> Metadata { get; set; } = new();

        /// <summary>
        /// When true, the engine must pause and await explicit user approval
        /// before continuing (used by gravity.propose).
        /// </summary>
        [JsonIgnore]
        public bool RequiresPlanApproval { get; set; }
    }
}
