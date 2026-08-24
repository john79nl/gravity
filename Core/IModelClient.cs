using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    /// <summary>Holds an image to be sent inline with a chat message (multimodal vision).</summary>
    public class ImageAttachment
    {
        /// <summary>MIME type, e.g. "image/jpeg", "image/png", "image/webp".</summary>
        public string MimeType { get; set; } = "image/jpeg";

        /// <summary>Raw base-64 encoded image bytes.</summary>
        public string Base64Data { get; set; } = string.Empty;

        /// <summary>Original local file path (informational only, not sent to the model).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? FilePath { get; set; }
    }

    public class ChatMessage
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = string.Empty;

        /// <summary>Optional image attachment for multimodal vision requests.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public ImageAttachment? Image { get; set; }

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
    public class ToolCallRaw
    {
        public string? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ArgumentsJson { get; set; } = string.Empty;
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
        Task<ModelResponse> CompleteAsync(System.Collections.Generic.List<ChatMessage> messages, CancellationToken ct, ModelRole role = ModelRole.Primary);
        Task<ModelResponse> CompleteAsync(string prompt, CancellationToken ct, string? systemPrompt = null, ModelRole role = ModelRole.Primary, System.Collections.Generic.IEnumerable<AgentDescriptor>? availableTools = null);
    }
}

