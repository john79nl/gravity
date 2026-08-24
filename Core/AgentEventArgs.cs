using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Gravity.Core.Agents
{
    // ── Promoted from AgentInstance nested type ───────────────────────────────
    /// <summary>
    /// Represents a single tool/action the engine wants to perform.
    /// Raised via <see cref="AppEngine.ApprovalRequested"/> before execution.
    /// </summary>
    public sealed class AgentAction
    {
        [JsonPropertyName("action")]    public string?       Action    { get; set; }
        [JsonPropertyName("tool")]      public string?       Tool      { get; set; }
        [JsonPropertyName("operation")] public string?       Operation { get; set; }
        [JsonPropertyName("params")]    public AgentRequest? Params    { get; set; }
        [JsonPropertyName("output")]    public string?       Output    { get; set; }
    }

    // ── Event args ────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when the engine requires user approval before executing an action.
    /// The handler must call <see cref="Completion"/>.SetResult to unblock the engine.
    /// </summary>
    public sealed class ApprovalRequestedEventArgs : EventArgs
    {
        public AgentAction Action { get; }

        /// <summary>
        /// Set <c>true</c> to allow or <c>false</c> to deny.
        /// The engine awaits this before proceeding.
        /// </summary>
        public TaskCompletionSource<bool> Completion { get; }
            = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ApprovalRequestedEventArgs(AgentAction action) => Action = action;
    }

    /// <summary>Raised at the start of each reasoning step.</summary>
    public sealed class StepStartedEventArgs : EventArgs
    {
        public int    Step  { get; }
        public string Label { get; }
        public StepStartedEventArgs(int step, string label) { Step = step; Label = label; }
    }
}
