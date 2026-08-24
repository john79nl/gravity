using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    public class ChatMessage
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = string.Empty;
        
        // Native tool calling props
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }
        
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        [System.Text.Json.Serialization.JsonPropertyName("tool_call_id")]
        public string? ToolCallId { get; set; }
        
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        [System.Text.Json.Serialization.JsonPropertyName("tool_calls")]
        public System.Collections.Generic.List<ToolCall>? ToolCalls { get; set; }
    }

    public class ToolCall
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [System.Text.Json.Serialization.JsonPropertyName("function")]
        public ToolCallFunction Function { get; set; } = new();
    }

    public class ToolCallFunction
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("arguments")]
        public string Arguments { get; set; } = "{}";
    }

    public class ModelResponse
    {
        public string Content { get; set; } = string.Empty;
        public System.Collections.Generic.List<ToolCall> ToolCalls { get; set; } = new();
        public string? FinishReason { get; set; }
    }

    public enum ModelRole
    {
        Primary,
        Reasoning
    }

    public interface IModelClient
    {
        /// <summary>
        /// Stream response tokens for the given prompt.
        /// </summary>
        Task<ModelResponse> StreamResponseAsync(System.Collections.Generic.List<ChatMessage> messages, IProgress<string> tokenProgress, CancellationToken ct, string? systemPrompt = null, ModelRole role = ModelRole.Primary, System.Collections.Generic.IEnumerable<AgentDescriptor>? availableTools = null);

        /// <summary>
        /// Return a completed response (non-streaming).
        /// </summary>
        Task<ModelResponse> CompleteAsync(string prompt, CancellationToken ct, string? systemPrompt = null, ModelRole role = ModelRole.Primary, System.Collections.Generic.IEnumerable<AgentDescriptor>? availableTools = null);
    }
}
