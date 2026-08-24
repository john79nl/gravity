using System;
using System.Collections.Generic;

namespace Gravity.Core
{
    public class ModelSpec
    {
        public int ContextWindowSize { get; set; } = 32768;
        public int MaxTokens { get; set; } = 8192;
        public bool UseMaxCompletionTokens { get; set; } = false;
    }

    public static class ModelInfoService
    {
        private static readonly Dictionary<string, ModelSpec> Registry = new(StringComparer.OrdinalIgnoreCase)
        {
            // ── OpenAI Models ──
            ["gpt-4o"]                  = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 16384 },
            ["gpt-4o-mini"]             = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 16384 },
            ["o1"]                      = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 100000, UseMaxCompletionTokens = true },
            ["o1-mini"]                 = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 65536, UseMaxCompletionTokens = true },
            ["o3-mini"]                 = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 100000, UseMaxCompletionTokens = true },
            ["gpt-4-turbo"]             = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 4096 },
            ["gpt-3.5-turbo"]           = new ModelSpec { ContextWindowSize = 16385,  MaxTokens = 4096 },

            // ── Anthropic & Fable Models ──
            ["claude-3-5-sonnet-20241022"] = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 64000 },
            ["claude-3-5-sonnet"]          = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 64000 },
            ["claude-3-5-haiku-20241022"]  = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 64000 },
            ["claude-3-5-haiku"]           = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 64000 },
            ["claude-3-5-fable"]           = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 64000 },
            ["claude-fable-5"]             = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 64000 },
            ["anthropic/claude-fable-5"]   = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 64000 },
            ["anthropic/claude-3.5-fable"] = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 64000 },
            ["fable"]                      = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 64000 },
            ["claude-3-5-opus"]            = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 64000 },
            ["claude-opus-5"]              = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 64000 },
            ["anthropic/claude-opus-5"]    = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 64000 },
            ["anthropic/claude-3.5-opus"]  = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 64000 },
            ["claude-3-opus-20240229"]     = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 32000 },
            ["claude-3-haiku-20240307"]    = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 32000 },
            ["anthropic/claude-3.5-sonnet"] = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 64000 },
            ["anthropic/claude-3-5-haiku"]  = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 64000 },
            ["anthropic/claude-3-opus"]    = new ModelSpec { ContextWindowSize = 200000, MaxTokens = 32000 },

            // ── Google AI Studio Models ──
            ["google/gemini-2.5-flash"] = new ModelSpec { ContextWindowSize = 1048576, MaxTokens = 8192 },
            ["gemini-2.5-flash"]        = new ModelSpec { ContextWindowSize = 1048576, MaxTokens = 8192 },
            ["gemini-2.0-flash-exp"]    = new ModelSpec { ContextWindowSize = 1048576, MaxTokens = 8192 },
            ["gemini-1.5-pro"]          = new ModelSpec { ContextWindowSize = 2097152, MaxTokens = 8192 },
            ["gemini-1.5-flash"]        = new ModelSpec { ContextWindowSize = 1048576, MaxTokens = 8192 },

            // ── Groq Models ──
            ["llama-3.3-70b-versatile"] = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 32768 },
            ["llama3-8b-8192"]          = new ModelSpec { ContextWindowSize = 8192,   MaxTokens = 8192 },
            ["mixtral-8x7b-32768"]      = new ModelSpec { ContextWindowSize = 32768,  MaxTokens = 32768 },

            // ── Cloudflare Workers AI Models (Active — August 2026) ──
            // Best for Gravity: reasoning + function calling
            ["@cf/openai/gpt-oss-120b"]                        = new ModelSpec { ContextWindowSize = 128000,  MaxTokens = 16384 },
            ["@cf/openai/gpt-oss-20b"]                         = new ModelSpec { ContextWindowSize = 128000,  MaxTokens = 16384 },
            ["@cf/deepseek-ai/deepseek-v4-pro-0813"]           = new ModelSpec { ContextWindowSize = 1048576, MaxTokens = 8192, UseMaxCompletionTokens = true },
            ["@cf/deepseek-ai/deepseek-v4-flash-0731"]         = new ModelSpec { ContextWindowSize = 1048576, MaxTokens = 8192, UseMaxCompletionTokens = true },
            ["@cf/meta/llama-4-scout-17b-16e-instruct"]        = new ModelSpec { ContextWindowSize = 131000,  MaxTokens = 8192 },
            ["@cf/meta/llama-3.3-70b-instruct-fp8-fast"]       = new ModelSpec { ContextWindowSize = 24000,   MaxTokens = 8192 },
            ["@cf/qwen/qwen3-30b-a3b-fp8"]                     = new ModelSpec { ContextWindowSize = 32768,   MaxTokens = 8192, UseMaxCompletionTokens = true },
            ["@cf/qwen/qwq-32b"]                               = new ModelSpec { ContextWindowSize = 24000,   MaxTokens = 8192, UseMaxCompletionTokens = true },
            ["@cf/google/gemma-4-26b-a4b-it"]                  = new ModelSpec { ContextWindowSize = 256000,  MaxTokens = 8192 },
            ["@cf/google/gemma-3-12b-it"]                      = new ModelSpec { ContextWindowSize = 128000,  MaxTokens = 8192 },
            ["@cf/nvidia/nemotron-3-120b-a12b"]                = new ModelSpec { ContextWindowSize = 256000,  MaxTokens = 8192 },
            ["@cf/zai-org/glm-5.2"]                            = new ModelSpec { ContextWindowSize = 262144,  MaxTokens = 8192 },
            ["@cf/zai-org/glm-4.7-flash"]                      = new ModelSpec { ContextWindowSize = 131072,  MaxTokens = 8192 },
            ["@cf/moonshotai/kimi-k2.7"]                       = new ModelSpec { ContextWindowSize = 262144,  MaxTokens = 8192 },
            ["@cf/moonshotai/kimi-k2.7-code"]                  = new ModelSpec { ContextWindowSize = 262144,  MaxTokens = 8192 },
            ["@cf/moonshotai/kimi-k2.6"]                       = new ModelSpec { ContextWindowSize = 262144,  MaxTokens = 8192 },
            // Mid-tier / support
            ["@cf/mistralai/mistral-small-3.1-24b-instruct"]   = new ModelSpec { ContextWindowSize = 128000,  MaxTokens = 8192 },
            ["@cf/deepseek-ai/deepseek-r1-distill-qwen-32b"]   = new ModelSpec { ContextWindowSize = 80000,   MaxTokens = 8192, UseMaxCompletionTokens = true },
            ["@cf/meta/llama-3.1-8b-instruct-fp8"]             = new ModelSpec { ContextWindowSize = 32000,   MaxTokens = 8192 },
            ["@cf/meta/llama-3.1-8b-instruct-fast"]            = new ModelSpec { ContextWindowSize = 60000,   MaxTokens = 8192 },
            ["@cf/meta/llama-3.2-3b-instruct"]                 = new ModelSpec { ContextWindowSize = 80000,   MaxTokens = 4096 },
            ["@cf/meta/llama-3.2-1b-instruct"]                 = new ModelSpec { ContextWindowSize = 60000,   MaxTokens = 4096 },
            ["@cf/qwen/qwen2.5-coder-32b-instruct"]            = new ModelSpec { ContextWindowSize = 32768,   MaxTokens = 8192 },
            ["@cf/ibm/granite-4.0-h-micro"]                    = new ModelSpec { ContextWindowSize = 128000,  MaxTokens = 4096 },

            // ── DeepSeek Models ──
            ["deepseek-coder-v2"]       = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            ["deepseek-chat"]           = new ModelSpec { ContextWindowSize = 64000,  MaxTokens = 8192 },
            ["deepseek-reasoner"]       = new ModelSpec { ContextWindowSize = 64000,  MaxTokens = 8192 },
            ["deepseek-r1"]             = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },

            // ── Qwen & Ollama Models ──
            ["qwen2.5-coder:1.5b"]              = new ModelSpec { ContextWindowSize = 32768,  MaxTokens = 4096 },
            ["qwen2.5-coder:7b"]                = new ModelSpec { ContextWindowSize = 32768,  MaxTokens = 8192 },
            ["Qwen/Qwen2.5-Coder-32B-Instruct"] = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            ["llama3.2:1b"]                     = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 4096 },

            // ── Kimi AI / Moonshot ──
            ["kimi-k2"]                 = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            ["kimi-k2-reasoning"]       = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            ["kimi-k1.5-thinking"]      = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            ["moonshot-v1-8k"]          = new ModelSpec { ContextWindowSize = 8192,   MaxTokens = 4096 },
            ["moonshot-v1-32k"]         = new ModelSpec { ContextWindowSize = 32768,  MaxTokens = 8192 },
            ["moonshot-v1-128k"]        = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },

            // ── NVIDIA / Meta / Other Models ──
            ["meta/llama-3.3-70b-instruct"]           = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            ["meta-llama/Llama-3.3-70B-Instruct"]     = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            ["meta-llama/Llama-3.3-70B-Instruct-Turbo"]= new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            ["Phi-3-mini-4k-instruct"]                = new ModelSpec { ContextWindowSize = 4096,   MaxTokens = 4096 },
            ["microsoft/phi-4"]                       = new ModelSpec { ContextWindowSize = 16384,  MaxTokens = 4096 },

            // ── Cactus Needle Models ──
            ["needle-2"]                                 = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            ["needle-2-45m"]                             = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            ["cactus-needle-2"]                          = new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
        };

        public static ModelSpec GetSpec(LlmProvider provider, string? modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return GetDefaultSpecForProvider(provider);

            var trimmed = modelName.Trim();

            // 1. Direct registry lookup
            if (Registry.TryGetValue(trimmed, out var spec))
                return spec;

            // 2. Try matching without provider prefix (e.g. "anthropic/claude-3.5-sonnet" vs "claude-3.5-sonnet")
            var slashIdx = trimmed.LastIndexOf('/');
            if (slashIdx >= 0 && slashIdx < trimmed.Length - 1)
            {
                var shortName = trimmed.Substring(slashIdx + 1);
                if (Registry.TryGetValue(shortName, out var shortSpec))
                    return shortSpec;
            }

            // 3. Heuristic pattern matching for context window sizes embedded in model names
            var lower = trimmed.ToLowerInvariant();
            int inferredContext = 32768;
            int inferredMaxTokens = 8192;

            if (lower.Contains("2m") || lower.Contains("2000k"))
                inferredContext = 2097152;
            else if (lower.Contains("1m") || lower.Contains("1000k"))
                inferredContext = 1048576;
            else if (lower.Contains("200k"))
                inferredContext = 200000;
            else if (lower.Contains("128k") || lower.Contains("70b") || lower.Contains("llama-3.3") || lower.Contains("llama-3.1") || lower.Contains("gpt-4o"))
                inferredContext = 128000;
            else if (lower.Contains("64k"))
                inferredContext = 64000;
            else if (lower.Contains("32k"))
                inferredContext = 32768;
            else if (lower.Contains("16k"))
                inferredContext = 16384;
            else if (lower.Contains("8k") || lower.Contains("8b"))
                inferredContext = 8192;
            else if (lower.Contains("4k") || lower.Contains("1.5b") || lower.Contains("3b"))
                inferredContext = 4096;

            bool isReasoningModel = lower.StartsWith("o1") || lower.StartsWith("o3") || lower.Contains("thinking") || lower.Contains("reasoning");

            return new ModelSpec
            {
                ContextWindowSize = inferredContext,
                MaxTokens = inferredMaxTokens,
                UseMaxCompletionTokens = isReasoningModel
            };
        }

        private static ModelSpec GetDefaultSpecForProvider(LlmProvider provider) => provider switch
        {
            LlmProvider.OpenAI         => new ModelSpec { ContextWindowSize = 128000, MaxTokens = 16384 },
            LlmProvider.GoogleAIStudio => new ModelSpec { ContextWindowSize = 1048576, MaxTokens = 8192 },
            LlmProvider.OpenRouter     => new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            LlmProvider.Groq           => new ModelSpec { ContextWindowSize = 128000, MaxTokens = 32768 },
            LlmProvider.TogetherAI     => new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            LlmProvider.Cloudflare     => new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            LlmProvider.NvidiaAI       => new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            LlmProvider.KimiAI         => new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            LlmProvider.Ollama         => new ModelSpec { ContextWindowSize = 32768,  MaxTokens = 8192 },
            LlmProvider.LocalGguf      => new ModelSpec { ContextWindowSize = 4096,   MaxTokens = 4096 },
            LlmProvider.Needle2        => new ModelSpec { ContextWindowSize = 128000, MaxTokens = 8192 },
            _                          => new ModelSpec { ContextWindowSize = 32768,  MaxTokens = 8192 }
        };

        private static readonly System.Net.Http.HttpClient SharedHttpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        public static async Task<List<string>> FetchModelsFromApiAsync(LlmProvider provider, string baseUrl, string apiKey, string accountId = "")
        {
            var models = new List<string>();
            var cleanUrl = (baseUrl ?? "").TrimEnd('/');
            var cleanKey = (apiKey ?? "").Trim();

            try
            {
                if (provider == LlmProvider.Ollama)
                {
                    var ollamaUrl = string.IsNullOrWhiteSpace(cleanUrl) ? "http://localhost:11434" : cleanUrl;
                    if (!ollamaUrl.EndsWith("/api/tags", StringComparison.OrdinalIgnoreCase))
                        ollamaUrl += "/api/tags";

                    using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, ollamaUrl);
                    using var resp = await SharedHttpClient.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync();
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("models", out var modelsArr) && modelsArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var item in modelsArr.EnumerateArray())
                            {
                                if (item.TryGetProperty("name", out var nameProp))
                                {
                                    var name = nameProp.GetString();
                                    if (!string.IsNullOrWhiteSpace(name)) models.Add(name);
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Generic /v1/models endpoint
                    string endpoint = cleanUrl;
                    if (string.IsNullOrWhiteSpace(endpoint))
                    {
                        endpoint = provider switch
                        {
                            LlmProvider.OpenAI => "https://api.openai.com/v1",
                            LlmProvider.OpenRouter => "https://openrouter.ai/api/v1",
                            LlmProvider.Groq => "https://api.groq.com/openai/v1",
                            LlmProvider.GoogleAIStudio => "https://generativelanguage.googleapis.com/v1beta/openai",
                            LlmProvider.TogetherAI => "https://api.together.xyz/v1",
                            LlmProvider.GitHubModels => "https://models.inference.ai.azure.com",
                            LlmProvider.LMStudio => "http://localhost:1234/v1",
                            LlmProvider.Vllm => "http://localhost:8000/v1",
                            LlmProvider.NvidiaAI => "https://integrate.api.nvidia.com/v1",
                            LlmProvider.KimiAI => "https://api.moonshot.ai/v1",
                            LlmProvider.HuggingFace => "https://router.huggingface.co/v1",
                            _ => ""
                        };
                    }

                    if (!string.IsNullOrWhiteSpace(endpoint))
                    {
                        if (!endpoint.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
                            endpoint += "/models";

                        using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, endpoint);
                        if (!string.IsNullOrWhiteSpace(cleanKey) && cleanKey != "not-needed")
                        {
                            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {cleanKey}");
                            req.Headers.TryAddWithoutValidation("api-key", cleanKey);
                        }

                        using var resp = await SharedHttpClient.SendAsync(req);
                        if (resp.IsSuccessStatusCode)
                        {
                            var json = await resp.Content.ReadAsStringAsync();
                            using var doc = System.Text.Json.JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                foreach (var item in dataArr.EnumerateArray())
                                {
                                    if (item.TryGetProperty("id", out var idProp))
                                    {
                                        var id = idProp.GetString();
                                        if (!string.IsNullOrWhiteSpace(id)) models.Add(id);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore network/API errors; caller will fallback or retain existing lists
            }

            return models;
        }
    }
}

