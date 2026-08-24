using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    public class GenericOpenAIClient : IModelClient
    {
        private readonly HttpClient _httpClient;
        private readonly ISettingsService _settings;

        public GenericOpenAIClient(ISettingsService settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            // Increased timeout to 10 minutes to accommodate slower local LLMs
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        }

        public async Task<ModelResponse> StreamResponseAsync(List<ChatMessage> promptMessages, IProgress<string> tokenProgress, CancellationToken ct, string? systemPrompt = null, ModelRole role = ModelRole.Primary, IEnumerable<AgentDescriptor>? availableTools = null)
        {
            var config = _settings.Current;
            var messages = new List<object>();
            
            if (!string.IsNullOrEmpty(systemPrompt))
                messages.Add(new { role = "system", content = systemPrompt });
            
            foreach (var m in promptMessages)
            {
                messages.Add(new { role = m.Role, content = m.Content });
            }

            var modelName = (role == ModelRole.Reasoning && !string.IsNullOrEmpty(config.ReasoningModelName)) 
                ? config.ReasoningModelName 
                : config.ModelName;

            var requestBody = new
            {
                model = modelName,
                messages = messages,
                stream = true,
                tools = BuildToolsPayload(availableTools)
            };

            var request = new HttpRequestMessage(HttpMethod.Post, GetEndpoint(config));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.StatusCode}).\nAPI Error Detail: {errorBody}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            var finalResponse = new ModelResponse();
            var contentBuilder = new StringBuilder();
            var toolCallIdMap = new Dictionary<int, string>();
            var toolCallNameMap = new Dictionary<int, string>();
            var toolCallArgsMap = new Dictionary<int, StringBuilder>();

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null) break; // End of stream reached safely
                
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6).Trim();
                    if (data == "[DONE]") break;

                    try
                    {
                        using var doc = JsonDocument.Parse(data);
                        var delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta");
                        if (delta.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.Null)
                        {
                            var text = content.GetString() ?? "";
                            contentBuilder.Append(text);
                            tokenProgress.Report(text);
                        }

                        if (delta.TryGetProperty("tool_calls", out var toolCallsArr))
                        {
                            foreach (var toolCall in toolCallsArr.EnumerateArray())
                            {
                                int index = toolCall.GetProperty("index").GetInt32();
                                if (toolCall.TryGetProperty("id", out var tcId))
                                {
                                    toolCallIdMap[index] = tcId.GetString() ?? "";
                                }
                                
                                if (toolCall.TryGetProperty("function", out var func))
                                {
                                    if (func.TryGetProperty("name", out var tcName))
                                        toolCallNameMap[index] = tcName.GetString() ?? "";
                                        
                                    if (func.TryGetProperty("arguments", out var tcArgs))
                                    {
                                        if (!toolCallArgsMap.ContainsKey(index))
                                            toolCallArgsMap[index] = new StringBuilder();
                                        toolCallArgsMap[index].Append(tcArgs.GetString());
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            finalResponse.Content = contentBuilder.ToString();
            foreach (var kvp in toolCallIdMap)
            {
                finalResponse.ToolCalls.Add(new ToolCall
                {
                    Id = kvp.Value,
                    Type = "function",
                    Function = new ToolCallFunction
                    {
                        Name = toolCallNameMap.TryGetValue(kvp.Key, out var n) ? n : "",
                        Arguments = toolCallArgsMap.TryGetValue(kvp.Key, out var a) ? a.ToString() : "{}"
                    }
                });
            }

            return finalResponse;
        }

        public Task PrepareAsync(CancellationToken ct) => Task.CompletedTask;

        public async Task<ModelResponse> CompleteAsync(string prompt, CancellationToken ct, string? systemPrompt = null, ModelRole role = ModelRole.Primary, IEnumerable<AgentDescriptor>? availableTools = null)
        {
            return await CompleteWithSystemAsync(prompt, systemPrompt, ct, role, availableTools);
        }

        private async Task<ModelResponse> CompleteWithSystemAsync(string prompt, string? systemPrompt, CancellationToken ct, ModelRole role = ModelRole.Primary, IEnumerable<AgentDescriptor>? availableTools = null)
        {
            var config = _settings.Current;
            var modelName = (role == ModelRole.Reasoning && !string.IsNullOrEmpty(config.ReasoningModelName)) 
                ? config.ReasoningModelName 
                : config.ModelName;
            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
                messages.Add(new { role = "system", content = systemPrompt });
            messages.Add(new { role = "user", content = prompt });

            var requestBody = new
            {
                model = modelName,
                messages = messages,
                stream = false,
                tools = BuildToolsPayload(availableTools)
            };

            var request = new HttpRequestMessage(HttpMethod.Post, GetEndpoint(config));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.StatusCode}).\nAPI Error Detail: {errorBody}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var resultMessage = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

            var finalResponse = new ModelResponse();
            if (resultMessage.TryGetProperty("content", out var resContent) && resContent.ValueKind != JsonValueKind.Null)
            {
                finalResponse.Content = resContent.GetString() ?? "";
            }

            if (resultMessage.TryGetProperty("tool_calls", out var tcArr))
            {
                foreach (var tc in tcArr.EnumerateArray())
                {
                    finalResponse.ToolCalls.Add(new ToolCall
                    {
                        Id = tc.GetProperty("id").GetString() ?? "",
                        Type = "function",
                        Function = new ToolCallFunction
                        {
                            Name = tc.GetProperty("function").GetProperty("name").GetString() ?? "",
                            Arguments = tc.GetProperty("function").GetProperty("arguments").GetString() ?? "{}"
                        }
                    });
                }
            }

            return finalResponse;
        }

        private object? BuildToolsPayload(IEnumerable<AgentDescriptor>? availableTools)
        {
            if (availableTools == null) return null;

            var tools = new List<object>();

            // Universal Agent Gateway: A single tool replacing all individual custom APIs
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "call_gravity_agent",
                    description = "Execute a Gravity system tool.",
                    parameters = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            { "agent", new { type = "string", description = "The target agent (e.g. shell, file, code_editor, logic)." } },
                            { "operation", new { type = "string", description = "The verb/action to perform." } },
                            { "arguments", new { type = "string", description = "A JSON string of arguments for the tool." } }
                        },
                        required = new[] { "agent", "operation" }
                    }
                }
            });

            // Action final is still good to have natively so it cleanly breaks loops
            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "action-final",
                    description = "Conclude the task or respond to the user if the intent is fully resolved.",
                    parameters = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            { "output", new { type = "string", description = "The final message to the user." } }
                        },
                        required = new[] { "output" }
                    }
                }
            });

            return tools;
        }

        private string GetEndpoint(AppSettings config)
        {
            var baseUrl = config.BaseUrl.TrimEnd('/');

            // Migrate legacy UseOllama flag
            var provider = config.UseOllama ? LlmProvider.Ollama : config.Provider;

            return provider switch
            {
                LlmProvider.Ollama or LlmProvider.Cloudflare => baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                    ? $"{baseUrl}/chat/completions"
                    : $"{baseUrl}/v1/chat/completions",
                // LM Studio and OpenAI both use standard path; LM Studio's default URL already has /v1
                _ => $"{baseUrl}/chat/completions"
            };
        }
    }
}
