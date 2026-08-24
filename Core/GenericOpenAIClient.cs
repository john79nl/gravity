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

        public event Action<string>? OnDebugLog;

        /// <summary>
        /// Initializes the client. 
        /// Injected HttpClient should be managed via IHttpClientFactory to avoid socket exhaustion.
        /// </summary>
        public GenericOpenAIClient(HttpClient httpClient, ISettingsService settings)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _httpClient.Timeout = Timeout.InfiniteTimeSpan;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        private (string endpoint, string apiKey, string modelName) ResolveConnection(AppSettings config, ModelRole role)
        {
            var key = config.Provider.ToString();
            string baseUrl, apiKey, modelName, reasoningModelName;

            if (config.ProviderConnections != null && config.ProviderConnections.TryGetValue(key, out var conn)
                && !string.IsNullOrEmpty(conn.BaseUrl))
            {
                baseUrl = conn.BaseUrl;
                apiKey = conn.ApiKey;
                modelName = conn.ModelName;
                reasoningModelName = conn.ReasoningModelName;
            }
            else
            {
                baseUrl = config.BaseUrl;
                apiKey = config.ApiKey;
                modelName = config.ModelName;
                reasoningModelName = config.ReasoningModelName;
            }

            // Fall back to top-level for any empty fields from per-provider
            if (string.IsNullOrEmpty(baseUrl)) baseUrl = config.BaseUrl;
            if (string.IsNullOrEmpty(apiKey)) apiKey = config.ApiKey;
            if (string.IsNullOrEmpty(modelName)) modelName = config.ModelName;
            if (string.IsNullOrEmpty(reasoningModelName)) reasoningModelName = config.ReasoningModelName;

            var effectiveModel = (role == ModelRole.Reasoning && !string.IsNullOrEmpty(reasoningModelName))
                ? reasoningModelName
                : modelName;

            // Handle provider-specific endpoint adjustments cleanly
            var normalizedBaseUrl = baseUrl.TrimEnd('/');
            if (normalizedBaseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) || normalizedBaseUrl.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
            {
                return (normalizedBaseUrl, apiKey, effectiveModel);
            }

            if (config.Provider == LlmProvider.Anthropic)
            {
                var anthropicEndpoint = normalizedBaseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                    ? $"{normalizedBaseUrl}/messages"
                    : $"{normalizedBaseUrl}/v1/messages";
                return (anthropicEndpoint, apiKey, effectiveModel);
            }

            var shouldAppendV1 = !normalizedBaseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                && (config.Provider == LlmProvider.Ollama || config.Provider == LlmProvider.Vllm || config.Provider == LlmProvider.LocalGguf || config.Provider == LlmProvider.Cloudflare || config.Provider == LlmProvider.Needle2);
            var endpoint = shouldAppendV1
                ? $"{normalizedBaseUrl}/v1/chat/completions"
                : $"{normalizedBaseUrl}/chat/completions";

            return (endpoint, apiKey, effectiveModel);
        }

        public async Task<ModelResponse> StreamResponseAsync(
            List<ChatMessage> promptMessages,
            IProgress<string> tokenProgress,
            CancellationToken ct,
            string? systemPrompt = null,
            ModelRole role = ModelRole.Primary,
            IEnumerable<AgentDescriptor>? availableTools = null)
        {
            var config = _settings.Current;
            var (endpoint, apiKey, effectiveModel) = ResolveConnection(config, role);

            if (config.Provider == LlmProvider.Cloudflare && (endpoint.Contains("{ACCOUNT_ID}", StringComparison.OrdinalIgnoreCase) || endpoint.Contains("%7BACCOUNT_ID%7D", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Cloudflare Account ID is missing in Settings!\n\nPlease open Gravity Settings, select 'Cloudflare Workers AI', and replace '{ACCOUNT_ID}' in the Base URL with your actual 32-character Cloudflare Account ID (found in dash.cloudflare.com under Account Overview).\n\nExample Base URL:\nhttps://api.cloudflare.com/client/v4/accounts/a1b2c3d4e5f678901234567890abcdef/ai/v1");
            }

            var (sanitizedMessages, effectiveMaxTokens) = SanitizeAndFitTokenBudget(promptMessages, systemPrompt, config.Provider, config.MaxTokens, effectiveModel);

            var messages = new List<object>();
            var anthropicSystemParts = new List<string>();

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                if (config.Provider == LlmProvider.Cloudflare)
                    messages.Add(new { role = "user", content = $"[System Instructions]:\n{systemPrompt}" });
                else if (config.Provider == LlmProvider.Anthropic)
                    anthropicSystemParts.Add(systemPrompt);
                else
                    messages.Add(new { role = "system", content = systemPrompt });
            }

            foreach (var m in sanitizedMessages)
            {
                if (m.Role == "system" && config.Provider == LlmProvider.Cloudflare)
                {
                    messages.Add(new { role = "user", content = $"[System Instructions]:\n{m.Content}" });
                }
                else if (m.Role == "system" && config.Provider == LlmProvider.Anthropic)
                {
                    // Anthropic: accumulate into top-level system field instead
                    if (!string.IsNullOrWhiteSpace(m.Content))
                        anthropicSystemParts.Add(m.Content);
                }
                else if (m.Role == "tool")
                {
                    if (config.DisableNativeToolCalls)
                    {
                        messages.Add(new { role = "user", content = $"[Result from {m.Name}]:\n{m.Content}" });
                    }
                    else
                    {
                        messages.Add(new
                        {
                            role = m.Role,
                            tool_call_id = m.ToolCallId ?? string.Empty,
                            name = m.Name ?? string.Empty,
                            content = m.Content
                        });
                    }
                }
                else if (m.Role == "assistant" && m.ToolCalls != null && m.ToolCalls.Count > 0)
                {
                    if (config.DisableNativeToolCalls)
                    {
                        messages.Add(new { role = m.Role, content = m.Content });
                    }
                    else
                    {
                        var toolCallsPayload = new List<object>();
                        foreach (var tc in m.ToolCalls)
                        {
                            toolCallsPayload.Add(new
                            {
                                id = tc.Id,
                                type = tc.Type,
                                function = new { name = tc.Function.Name, arguments = tc.Function.Arguments }
                            });
                        }
                        messages.Add(new
                        {
                            role = m.Role,
                            content = string.IsNullOrEmpty(m.Content) ? (string?)null : m.Content,
                            tool_calls = toolCallsPayload
                        });
                    }
                }
                else
                {
                    // Multimodal vision: if the message carries an image, build a content array
                    if (m.Image != null && !string.IsNullOrEmpty(m.Image.Base64Data))
                    {
                        var dataUrl = $"data:{m.Image.MimeType};base64,{m.Image.Base64Data}";
                        var contentParts = new List<object>
                        {
                            new { type = "text", text = m.Content },
                            new { type = "image_url", image_url = new { url = dataUrl } }
                        };
                        messages.Add(new { role = m.Role, content = (object)contentParts });
                    }
                    else
                    {
                        messages.Add(new { role = m.Role, content = m.Content });
                    }
                }

            }

            var requestBody = new Dictionary<string, object?>
            {
                ["model"] = effectiveModel,
                ["messages"] = messages,
                ["stream"] = true
            };

            // Anthropic requires system as a top-level field
            if (config.Provider == LlmProvider.Anthropic && anthropicSystemParts.Count > 0)
                requestBody["system"] = string.Join("\n\n", anthropicSystemParts);
            if (config.UseMaxTokens)
            {
                requestBody[config.UseMaxCompletionTokens ? "max_completion_tokens" : "max_tokens"] = effectiveMaxTokens;
            }
            if (!config.DisableNativeToolCalls)
            {
                var toolsPayload = BuildToolsPayload(availableTools);
                if (toolsPayload != null)
                    requestBody["tools"] = toolsPayload;
            }

            var requestBodyJson = JsonSerializer.Serialize(requestBody);
            OnDebugLog?.Invoke($">>>> REQUEST ({effectiveModel})\n{PrettifyJson(requestBodyJson)}\n");

            for (int attempt = 0; attempt < 7; attempt++)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

                if (config.Provider == LlmProvider.Anthropic)
                {
                    request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
                    request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                }

                var providerKey = config.Provider.ToString();
                if (config.ProviderConnections != null && config.ProviderConnections.TryGetValue(providerKey, out var conn) && !string.IsNullOrWhiteSpace(conn.AccountId))
                {
                    var accId = conn.AccountId.Trim();
                    if (config.Provider == LlmProvider.OpenAI)
                    {
                        if (accId.StartsWith("proj_", StringComparison.OrdinalIgnoreCase))
                            request.Headers.TryAddWithoutValidation("OpenAI-Project", accId);
                        else
                            request.Headers.TryAddWithoutValidation("OpenAI-Organization", accId);
                    }
                }

                request.Content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    OnDebugLog?.Invoke($"!!!! ERROR 401 Unauthorized: {errorBody}");
                    throw new HttpRequestException($"API request unauthorized (401). Please check your API key in Settings.\nProvider: {config.Provider}\nAPI Error Detail: {errorBody}");
                }

                bool isTransient = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                                   response.StatusCode == System.Net.HttpStatusCode.BadGateway ||
                                   response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                                   response.StatusCode == System.Net.HttpStatusCode.GatewayTimeout;

                if (isTransient)
                {
                    if (attempt < 6)
                    {
                        int delaySec = GetRetryDelaySeconds(response, attempt);
                        OnDebugLog?.Invoke($"API rate limited / busy ({(int)response.StatusCode}), retrying ({attempt + 2}/7) in {delaySec}s...");
                        await Task.Delay(delaySec * 1000, ct).ConfigureAwait(false);
                        continue;
                    }
                    var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    OnDebugLog?.Invoke($"!!!! ERROR {(int)response.StatusCode} rate limit exhausted: {errorBody}\n");
                    throw new HttpRequestException($"Reasoning loop failed: {config.Provider} API rate limited ({(int)response.StatusCode}) after retries.\nStatus: {(int)response.StatusCode}. Detail: {errorBody}\n\nTip: Rate limit/quota exceeded for {config.Provider} ({effectiveModel}). Try waiting a moment or selecting another model in Settings.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    OnDebugLog?.Invoke($"!!!! ERROR {response.StatusCode}: {errorBody}");
                    throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.StatusCode}).\nAPI Error Detail: {errorBody}\n\nRequest URL: {request.RequestUri}\nModel: {effectiveModel}");
                }

                using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(stream);

                var finalResponse = new ModelResponse();
                var contentBuilder = new StringBuilder();   // actual response content (tool call JSON / final answer)
                var thinkingBuilder = new StringBuilder();  // reasoning_content / thinking — streamed to UI only, NOT parsed
                var toolCallIdMap = new Dictionary<int, string>();
                var toolCallNameMap = new Dictionary<int, string>();
                var toolCallArgsMap = new Dictionary<int, StringBuilder>();
                int unindexedToolCallCounter = 0;

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        var data = line.Substring(5).Trim();
                        if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase)) break;
                        try
                        {
                            using var doc = JsonDocument.Parse(data);
                            if (doc.RootElement.TryGetProperty("choices", out var choicesArr) && choicesArr.ValueKind == JsonValueKind.Null)
                            {
                                // choices is null or missing, check if finish_reason or error is present
                            }
                            else if (choicesArr.ValueKind == JsonValueKind.Array && choicesArr.GetArrayLength() > 0)
                            {
                                var firstChoice = choicesArr[0];
                                if (firstChoice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
                                {
                                    if (delta.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.Null)
                                    {
                                        var text = content.GetString() ?? "";
                                        contentBuilder.Append(text);
                                        tokenProgress.Report(text);
                                    }

                                    // reasoning_content / thinking are the model's internal chain-of-thought.
                                    // Stream them to the UI for display, but keep them OUT of contentBuilder
                                    // so they do NOT corrupt the tool-call JSON that the parser expects.
                                    if (delta.TryGetProperty("reasoning_content", out var reasoning) && reasoning.ValueKind != JsonValueKind.Null)
                                    {
                                        var text = reasoning.GetString() ?? "";
                                        thinkingBuilder.Append(text);
                                        tokenProgress.Report(text);
                                    }
                                    else if (delta.TryGetProperty("thinking", out var thinking) && thinking.ValueKind != JsonValueKind.Null)
                                    {
                                        var text = thinking.GetString() ?? "";
                                        thinkingBuilder.Append(text);
                                        tokenProgress.Report(text);
                                    }

                                    if (delta.TryGetProperty("tool_calls", out var toolCallsArr) && toolCallsArr.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var toolCall in toolCallsArr.EnumerateArray())
                                        {
                                            // Safely fallback if 'index' is omitted by custom OpenAI-compatible runtimes to prevent structural corruption
                                            int index = toolCall.TryGetProperty("index", out var idxProp)
                                                ? idxProp.GetInt32()
                                                : unindexedToolCallCounter++;

                                            if (toolCall.TryGetProperty("id", out var tcId))
                                                toolCallIdMap[index] = tcId.GetString() ?? "";
                                            if (toolCall.TryGetProperty("function", out var func) && func.ValueKind == JsonValueKind.Object)
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

                                if (firstChoice.TryGetProperty("finish_reason", out var finishReason) && finishReason.ValueKind == JsonValueKind.String)
                                {
                                    var reason = finishReason.GetString();
                                    if (!string.IsNullOrEmpty(reason))
                                    {
                                        finalResponse.FinishReason = reason;
                                        OnDebugLog?.Invoke($"[SSE] finish_reason '{reason}' detected, breaking stream early.");
                                        break;
                                    }
                                }
                            }
                        }
                        catch (Exception parseEx)
                        {
                            var preview = data.Length > 200 ? data.Substring(0, 200) : data;
                            OnDebugLog?.Invoke($"[SSE Parse Error] {parseEx.Message}\nRaw: {preview}");
                        }
                    }
                    else
                    {
                        var preview = line.Length > 200 ? line.Substring(0, 200) : line;
                        OnDebugLog?.Invoke($"[Non-SSE Line] {preview}");
                    }
                }

                finalResponse.Content = contentBuilder.ToString();

                // If the model only produced thinking tokens (no actual content), fall back to the
                // thinking text so the engine at least has something to parse / display.
                if (string.IsNullOrWhiteSpace(finalResponse.Content) && thinkingBuilder.Length > 0)
                    finalResponse.Content = thinkingBuilder.ToString();
                
                var allIndices = toolCallNameMap.Keys.Union(toolCallArgsMap.Keys).Distinct();
                foreach (var index in allIndices)
                {
                    if (!toolCallIdMap.TryGetValue(index, out var tcId) || string.IsNullOrEmpty(tcId))
                        tcId = "call_" + Guid.NewGuid().ToString("N").Substring(0, 12);

                    finalResponse.ToolCalls.Add(new ToolCall
                    {
                        Id = tcId,
                        Type = "function",
                        Function = new ToolCallFunction
                        {
                            Name = toolCallNameMap.TryGetValue(index, out var n) ? n : "",
                            Arguments = toolCallArgsMap.TryGetValue(index, out var a) ? a.ToString() : "{}"
                        }
                    });
                }

                if (finalResponse.ToolCalls.Count == 0 && !string.IsNullOrWhiteSpace(finalResponse.Content))
                {
                    var parsedToolCalls = TryParseToolCallsFromContent(finalResponse.Content);
                    if (parsedToolCalls.Count > 0 && IsLikelyToolCall(parsedToolCalls[0].Function?.Name))
                    {
                        finalResponse.ToolCalls.AddRange(parsedToolCalls);
                        // NOTE: Do NOT clear Content here — AgentInstance uses Content as the
                        // primary signal. ToolCalls are a secondary supplement for native providers.
                    }
                }

                OnDebugLog?.Invoke($"<<<< RESPONSE\n{PrettifyJson(JsonSerializer.Serialize(finalResponse))}\n");
                return finalResponse;
            }

            throw new HttpRequestException("Max retries exceeded.");
        }

        public Task PrepareAsync(CancellationToken ct) => Task.CompletedTask;

        public async Task<ModelResponse> CompleteAsync(System.Collections.Generic.List<ChatMessage> messages, CancellationToken ct, ModelRole role = ModelRole.Primary)
        {
            var sb = new System.Text.StringBuilder();
            var res = await StreamResponseAsync(messages, new System.Progress<string>(s => sb.Append(s)), ct, null, role).ConfigureAwait(false);
            if (string.IsNullOrEmpty(res.Content)) res.Content = sb.ToString();
            return res;
        }

        public async Task<ModelResponse> CompleteAsync(string prompt, CancellationToken ct, string? systemPrompt = null, ModelRole role = ModelRole.Primary, IEnumerable<AgentDescriptor>? availableTools = null)
        {
            return await CompleteWithSystemAsync(prompt, systemPrompt, ct, role, availableTools).ConfigureAwait(false);
        }

        private async Task<ModelResponse> CompleteWithSystemAsync(string prompt, string? systemPrompt, CancellationToken ct, ModelRole role = ModelRole.Primary, IEnumerable<AgentDescriptor>? availableTools = null)
        {
            var config = _settings.Current;
            var (endpoint, apiKey, effectiveModel) = ResolveConnection(config, role);
            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                // Anthropic requires system as a top-level field, not a role:"system" message
                if (config.Provider != LlmProvider.Anthropic)
                    messages.Add(new { role = "system", content = systemPrompt });
            }
            messages.Add(new { role = "user", content = prompt });

            var requestBody = new Dictionary<string, object?>
            {
                ["model"] = effectiveModel,
                ["messages"] = messages,
                ["stream"] = false
            };

            // Anthropic: system prompt goes at top level
            if (config.Provider == LlmProvider.Anthropic && !string.IsNullOrEmpty(systemPrompt))
                requestBody["system"] = systemPrompt;
            if (config.UseMaxTokens)
            {
                requestBody[config.UseMaxCompletionTokens ? "max_completion_tokens" : "max_tokens"] = Math.Min(10000, config.MaxTokens);
            }
            if (!config.DisableNativeToolCalls)
            {
                var toolsPayload = BuildToolsPayload(availableTools);
                if (toolsPayload != null)
                    requestBody["tools"] = toolsPayload;
            }

            var requestBodyJson = JsonSerializer.Serialize(requestBody);
            OnDebugLog?.Invoke($">>>> REQUEST ({effectiveModel})\n{PrettifyJson(requestBodyJson)}\n");

            for (int attempt = 0; attempt < 7; attempt++)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    OnDebugLog?.Invoke($"!!!! ERROR 401 Unauthorized: {errorBody}\n");
                    throw new HttpRequestException($"API request unauthorized (401). Please check your API key in Settings.\nProvider: {config.Provider}\nAPI Error Detail: {errorBody}");
                }

                bool isTransient = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                                   response.StatusCode == System.Net.HttpStatusCode.BadGateway ||
                                   response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                                   response.StatusCode == System.Net.HttpStatusCode.GatewayTimeout;

                if (isTransient)
                {
                    if (attempt < 6)
                    {
                        int delaySec = GetRetryDelaySeconds(response, attempt);
                        OnDebugLog?.Invoke($"API rate limited / busy ({(int)response.StatusCode}), retrying ({attempt + 2}/7) in {delaySec}s...");
                        await Task.Delay(delaySec * 1000, ct).ConfigureAwait(false);
                        continue;
                    }
                    var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    OnDebugLog?.Invoke($"!!!! ERROR {(int)response.StatusCode}: {errorBody}\n");
                    throw new HttpRequestException($"Reasoning loop failed: {config.Provider} API rate limited ({(int)response.StatusCode}) after retries.\nStatus: {(int)response.StatusCode}. Detail: {errorBody}\n\nTip: Rate limit/quota exceeded for {config.Provider} ({effectiveModel}). Try waiting a moment or selecting another model in Settings.");
                }
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    OnDebugLog?.Invoke($"!!!! ERROR: {errorBody}\n");
                    throw new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.StatusCode}).\nAPI Error Detail: {errorBody}");
                }

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                OnDebugLog?.Invoke($"<<<< RESPONSE (Complete)\n{PrettifyJson(json)}\n");
                using var doc = JsonDocument.Parse(json);
                var resultMessage = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

                var finalResponse = new ModelResponse();
                if (resultMessage.TryGetProperty("content", out var resContent) && resContent.ValueKind != JsonValueKind.Null)
                    finalResponse.Content = resContent.GetString() ?? "";

                if (string.IsNullOrEmpty(finalResponse.Content))
                {
                    if (resultMessage.TryGetProperty("reasoning_content", out var resReasoning) && resReasoning.ValueKind != JsonValueKind.Null)
                        finalResponse.Content = resReasoning.GetString() ?? "";
                    else if (resultMessage.TryGetProperty("thinking", out var resThinking) && resThinking.ValueKind != JsonValueKind.Null)
                        finalResponse.Content = resThinking.GetString() ?? "";
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

                if (finalResponse.ToolCalls.Count == 0 && !string.IsNullOrWhiteSpace(finalResponse.Content))
                {
                    var parsedToolCalls = TryParseToolCallsFromContent(finalResponse.Content);
                    if (parsedToolCalls.Count > 0 && IsLikelyToolCall(parsedToolCalls[0].Function?.Name))
                    {
                        finalResponse.ToolCalls.AddRange(parsedToolCalls);
                        // NOTE: Do NOT clear Content — keep it so the agent can always read the response.
                    }
                }

                return finalResponse;
            }

            throw new HttpRequestException("Max retries exceeded.");
        }

        private object? BuildToolsPayload(IEnumerable<AgentDescriptor>? availableTools)
        {
            if (availableTools == null) return null;

            var tools = new List<object>();

            var allowedActions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["code_editor"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "read_file", "write_file", "apply_diff", "apply_patches", "replace_block",
                    "list_directory", "search_in_files", "read_range", "glob", "grep", "delete"
                },
                ["terminal"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "run_command"
                },
                ["search"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "web"
                },
                ["pdf"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "merge", "split", "extract_text", "extract_pages", "compress",
                    "rotate", "watermark", "encrypt", "decrypt", "metadata",
                    "info", "to_images", "from_images", "search"
                }
            };

            foreach (var agent in availableTools)
            {
                var agentName = agent.Name.ToLowerInvariant();

                if (agentName == "knowledge" || agentName == "gravity")
                    continue;

                if (!allowedActions.TryGetValue(agentName, out var actionsSet))
                    continue;

                if (agent.Actions != null && agent.Actions.Count > 0)
                {
                    foreach (var action in agent.Actions)
                    {
                        var actionName = action.Name.ToLowerInvariant();
                        if (!actionsSet.Contains(actionName))
                            continue;

                        var toolName = $"{agent.Name}.{action.Name}";

                        var properties = new Dictionary<string, object>();
                        var requiredList = new List<string>();

                        if (action.Parameters != null)
                        {
                            foreach (var param in action.Parameters)
                            {
                                properties.Add(param.Key, new { type = "string", description = param.Value });

                                bool isOptional = action.OptionalParameters != null && action.OptionalParameters.Contains(param.Key);
                                if (!isOptional)
                                {
                                    requiredList.Add(param.Key);
                                }
                            }
                        }

                        tools.Add(new
                        {
                            type = "function",
                            function = new
                            {
                                name = toolName,
                                description = action.Description,
                                parameters = new
                                {
                                    type = "object",
                                    properties = properties,
                                    required = requiredList.ToArray()
                                }
                            }
                        });
                    }
                }
            }



            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "action.final",
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

            tools.Add(new
            {
                type = "function",
                function = new
                {
                    name = "action-final",
                    description = "Conclude the task or respond to the user if the intent is fully resolved (legacy format).",
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

            return tools.Count > 0 ? tools : null;
        }

        /// <summary>
        /// Returns true if the tool name looks like a real agent.action call (e.g. "code_editor.read_file", "action.final").
        /// This guards against false positives where a plain-text JSON snippet gets mistakenly parsed as a tool call.
        /// </summary>
        private static bool IsLikelyToolCall(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            // Must contain a dot (agent.action format) or be one of the reserved action names
            return name.Contains('.') || name.Contains('-');
        }

        private static List<ToolCall> TryParseToolCallsFromContent(string content)
        {
            var list = new List<ToolCall>();
            if (string.IsNullOrWhiteSpace(content)) return list;

            if (PlanParser.TryExtractJson(content, out var jsonStr))
            {
                try
                {
                    using var doc = JsonDocument.Parse(jsonStr);
                    var root = doc.RootElement;

                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in root.EnumerateArray())
                        {
                            var tc = ParseSingleToolCall(elem);
                            if (tc != null) list.Add(tc);
                        }
                    }
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("tool_calls", out var tcArr) && tcArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var elem in tcArr.EnumerateArray())
                            {
                                var tc = ParseSingleToolCall(elem);
                                if (tc != null) list.Add(tc);
                            }
                        }
                        else
                        {
                            var tc = ParseSingleToolCall(root);
                            if (tc != null) list.Add(tc);
                        }
                    }
                }
                catch
                {
                    // Ignore parsing issues
                }
            }

            return list;
        }

        private static ToolCall? ParseSingleToolCall(JsonElement elem)
        {
            string? name = null;
            string? arguments = null;
            string id = "call_" + Guid.NewGuid().ToString("N").Substring(0, 12);

            if (elem.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
            {
                name = nameProp.GetString();
            }
            else if (elem.TryGetProperty("function", out var funcProp))
            {
                if (funcProp.ValueKind == JsonValueKind.String)
                {
                    name = funcProp.GetString();
                }
                else if (funcProp.ValueKind == JsonValueKind.Object && funcProp.TryGetProperty("name", out var funcNameProp) && funcNameProp.ValueKind == JsonValueKind.String)
                {
                    name = funcNameProp.GetString();
                }
            }

            if (string.IsNullOrEmpty(name)) return null;

            if (elem.TryGetProperty("arguments", out var argsProp))
            {
                if (argsProp.ValueKind == JsonValueKind.String)
                {
                    arguments = argsProp.GetString();
                }
                else if (argsProp.ValueKind == JsonValueKind.Object)
                {
                    arguments = JsonSerializer.Serialize(argsProp);
                }
            }
            else if (elem.TryGetProperty("function", out var funcProp2) && funcProp2.ValueKind == JsonValueKind.Object && funcProp2.TryGetProperty("arguments", out var funcArgsProp))
            {
                if (funcArgsProp.ValueKind == JsonValueKind.String)
                {
                    arguments = funcArgsProp.GetString();
                }
                else if (funcArgsProp.ValueKind == JsonValueKind.Object)
                {
                    arguments = JsonSerializer.Serialize(funcArgsProp);
                }
            }
            else if (elem.TryGetProperty("parameters", out var paramsProp))
            {
                if (paramsProp.ValueKind == JsonValueKind.String)
                {
                    arguments = paramsProp.GetString();
                }
                else if (paramsProp.ValueKind == JsonValueKind.Object)
                {
                    arguments = JsonSerializer.Serialize(paramsProp);
                }
            }

            if (elem.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            {
                id = idProp.GetString() ?? id;
            }

            return new ToolCall
            {
                Id = id,
                Type = "function",
                Function = new ToolCallFunction
                {
                    Name = name,
                    Arguments = arguments ?? "{}"
                }
            };
        }

        private static string PrettifyJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return json;
            }
        }

        private static (List<ChatMessage> messages, int effectiveMaxTokens) SanitizeAndFitTokenBudget(
            List<ChatMessage> promptMessages,
            string? systemPrompt,
            LlmProvider provider,
            int configuredMaxTokens,
            string? modelName = null)
        {
            int effectiveMaxTokens = configuredMaxTokens;

            // 1. Sanitize text content: strip oversized base64 data URIs from text observations
            var sanitized = new List<ChatMessage>();
            foreach (var m in promptMessages)
            {
                var content = m.Content ?? string.Empty;
                if (content.Contains(";base64,", StringComparison.OrdinalIgnoreCase))
                {
                    content = System.Text.RegularExpressions.Regex.Replace(content, @"data:image/[^;]+;base64,[A-Za-z0-9+/=]+", "[IMAGE DATA OMITTED FOR TOKEN BUDGET]");
                }
                sanitized.Add(new ChatMessage
                {
                    Role = m.Role,
                    Content = content,
                    Name = m.Name,
                    ToolCallId = m.ToolCallId,
                    ToolCalls = m.ToolCalls,
                    Image = m.Image
                });
            }

            // 2. Token Budget calculation for Cloudflare Workers AI & context-constrained models
            var spec = ModelInfoService.GetSpec(provider, modelName);
            int modelContextLimit = spec.ContextWindowSize > 0 ? spec.ContextWindowSize : 128000;
            int hardContextLimit = provider == LlmProvider.Cloudflare ? Math.Min(modelContextLimit - 1000, modelContextLimit) : 120000;

            static int EstimateTokens(string? text) => string.IsNullOrEmpty(text) ? 0 : (text.Length / 4) + 4;

            int systemTokens = EstimateTokens(systemPrompt);
            int currentInputTokens = systemTokens;

            foreach (var m in sanitized)
            {
                currentInputTokens += EstimateTokens(m.Content);
            }

            // If input tokens + requested max_tokens exceed hardContextLimit:
            if (currentInputTokens + effectiveMaxTokens > hardContextLimit)
            {
                int targetInputBudget = Math.Max(4000, hardContextLimit - effectiveMaxTokens);

                // Cap effectiveMaxTokens dynamically for Cloudflare if input budget is tight
                if (provider == LlmProvider.Cloudflare && targetInputBudget < 14000)
                {
                    effectiveMaxTokens = Math.Max(1024, hardContextLimit - currentInputTokens);
                    targetInputBudget = Math.Max(4000, hardContextLimit - effectiveMaxTokens);
                }

                // If input tokens still exceed budget, prune older intermediate history messages
                if (currentInputTokens > targetInputBudget && sanitized.Count > 4)
                {
                    var pruned = new List<ChatMessage>();
                    int keepStart = 2; // Keep initial prompt
                    int keepEnd = Math.Max(0, sanitized.Count - 6); // Keep latest 6 messages

                    for (int i = 0; i < sanitized.Count; i++)
                    {
                        if (i < keepStart || i >= keepEnd)
                        {
                            pruned.Add(sanitized[i]);
                        }
                        else if (i == keepStart)
                        {
                            pruned.Add(new ChatMessage
                            {
                                Role = "user",
                                Content = "... [INTERMEDIATE REASONING STEPS OMITTED TO FIT MODEL TOKEN CONTEXT BUDGET] ..."
                            });
                        }
                    }
                    sanitized = pruned;
                }
            }

            return (sanitized, effectiveMaxTokens);
        }

        private static int GetRetryDelaySeconds(HttpResponseMessage response, int attempt)
        {
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue)
                {
                    var sec = (int)Math.Ceiling(response.Headers.RetryAfter.Delta.Value.TotalSeconds);
                    if (sec > 0 && sec <= 60) return sec;
                }
                else if (response.Headers.RetryAfter.Date.HasValue)
                {
                    var sec = (int)Math.Ceiling((response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds);
                    if (sec > 0 && sec <= 60) return sec;
                }
            }

            if (response.Headers.TryGetValues("retry-after-ms", out var msValues) &&
                long.TryParse(msValues.FirstOrDefault(), out var ms) && ms > 0)
            {
                return Math.Min(60, (int)Math.Ceiling(ms / 1000.0));
            }

            if (response.Headers.TryGetValues("x-ratelimit-reset-requests", out var resetValues) &&
                long.TryParse(resetValues.FirstOrDefault(), out var resetSec) && resetSec > 0)
            {
                return Math.Min(60, (int)resetSec);
            }

            var delays = new[] { 2, 4, 8, 16, 25, 35, 45 };
            return attempt < delays.Length ? delays[attempt] : 45;
        }
    }
}

