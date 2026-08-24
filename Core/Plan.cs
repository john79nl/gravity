using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Gravity.Core
{
    public class Plan
    {
        [JsonPropertyName("steps")]
        public List<PlanStep>? Steps { get; set; }
    }

    public class PlanStep
    {
        [JsonPropertyName("agent")]
        public string? Agent { get; set; }

        [JsonPropertyName("command")]
        public string? Command { get; set; }

        [JsonPropertyName("arguments")]
        public Dictionary<string, string>? Arguments { get; set; }

        [JsonPropertyName("apply")]
        public bool? Apply { get; set; }

        [JsonPropertyName("result")]
        public AgentResult? Result { get; set; }
    }
}
