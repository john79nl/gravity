using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gravity.Core
{
    public enum DevelopmentMode { Autopilot, Review, Assisted }
    public enum LlmProvider { OpenAI, LMStudio, Ollama, OpenRouter, Groq, GoogleAIStudio, TogetherAI, GitHubModels, Vllm, NvidiaAI, KimiAI, LocalGguf, HuggingFace, Cloudflare, Anthropic, Needle2 }

    /// <summary>
    /// Controls the tone and depth of Gravity's final answers.
    /// Educator  = clear explanations for non-technical readers.
    /// Engineer  = terse, source-linked, developer-style with exact values and next-step suggestions.
    /// Executive = high-level business impact summary, minimal technical detail.
    /// </summary>
    public enum ResponseStyle { Educator, Engineer, Executive }
    public enum SearchProvider { DuckDuckGo, LangSearch }

    /// <summary>
    /// Controls how the Orchestrator routes user intent before agent execution.
    /// PrePlanned = legacy: Clarify → Classify → TryPlan → execute rigid steps (good for small/local models).
    /// Adaptive   = default: Classify only; LLM drives its own tool loop with a context seed (recommended).
    /// FreeForm   = no pre-processing at all; raw user intent goes straight to the agent loop (maximum autonomy).
    /// </summary>
    public enum PlanningMode { PrePlanned, Adaptive, FreeForm }
    
    public class EmailConfig
    {
        public string Provider { get; set; } = "SMTP";
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public string UserId { get; set; } = "";
        public string DefaultFrom { get; set; } = "";
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 587;

        public Dictionary<string, EmailProviderSettings> ProviderSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class EmailProviderSettings
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public string UserId { get; set; } = "";
        public string DefaultFrom { get; set; } = "";
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 587;
    }

    public class AppSettings
    {
        public string BaseUrl { get; set; } = "http://localhost:11434";
        public string ApiKey { get; set; } = "not-needed";
        public string ModelName { get; set; } = "qwen2.5-coder:1.5b";
        public string ReasoningModelName { get; set; } = "qwen2.5-coder:1.5b";
        public bool DebugJson { get; set; } = false;
        public bool UseOllama { get; set; } = false;
        public LlmProvider Provider { get; set; } = LlmProvider.Ollama;
        public DevelopmentMode DevMode { get; set; } = DevelopmentMode.Autopilot;
        public ResponseStyle Style { get; set; } = ResponseStyle.Engineer;
        
        public int ContextWindowSize { get; set; } = 4096;
        public int MaxTokens { get; set; } = 8192;
        public bool UseMaxTokens { get; set; } = true;
        public bool UseMaxCompletionTokens { get; set; } = false;
        public int MaxHistoryMessages { get; set; } = 12;
        public int MaxObservationLength { get; set; } = 5000;
        public int MaxSteps { get; set; } = 0; // 0 = unlimited; stopped only by task completion or cancellation

        public bool DisableNativeToolCalls { get; set; } = true;
        public bool TruncateObservations { get; set; } = true;
        public bool PersistSession { get; set; } = true;
        public bool CriticEnabled { get; set; } = true;
        public PlanningMode PlanningMode { get; set; } = PlanningMode.Adaptive;

        public Dictionary<string, string> EnvironmentVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public EmailConfig Email { get; set; } = new EmailConfig();

        public Dictionary<string, McpServerConfig>? McpServers { get; set; }

        /// <summary>Per-provider connection settings so each provider remembers its own URL, key, and model.</summary>
        public Dictionary<string, ProviderConnection> ProviderConnections { get; set; } = new();

        // ── SearXNG web search ──────────────────────────────────────────────
        /// <summary>Base URL of the SearXNG instance (e.g. http://localhost:8080). Leave empty to use the DuckDuckGo fallback.</summary>
        public string SearxngUrl { get; set; } = "";
        /// <summary>Optional Bearer token for SearXNG instances that require authentication.</summary>
        public string SearxngToken { get; set; } = "";

        // ── Search provider ────────────────────────────────────────────────
        /// <summary>Which web search backend the search agent uses.</summary>
        public SearchProvider SearchProvider { get; set; } = SearchProvider.DuckDuckGo;
        /// <summary>API key for the LangSearch web search provider.</summary>
        public string LangSearchApiKey { get; set; } = "";

        // ── Local llama.cpp server ─────────────────────────────────────────
        /// <summary>Full path to the GGUF model file to load with llama-server.</summary>
        public string GgufModelPath { get; set; } = "";
        /// <summary>Full path to llama-server.exe (from llama.cpp releases).</summary>
        public string LlamaCppExePath { get; set; } = "";
        /// <summary>Number of model layers to offload to GPU. 0 = CPU-only.</summary>
        public int LlamaCppGpuLayers { get; set; } = 0;
    }

    public class ProviderConnection
    {
        public string BaseUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string ModelName { get; set; } = "";
        public string ReasoningModelName { get; set; } = "";
        public string AccountId { get; set; } = "";
        public List<string> FetchedModels { get; set; } = new();
    }

    public interface ISettingsService
    {
        AppSettings Current { get; }
        void Save(AppSettings settings);
    }

    public class SettingsService : ISettingsService
    {
        private readonly string _filePath;
        private AppSettings _current;

        public AppSettings Current => _current;

        public SettingsService()
        {
            // Save to user-scoped path so settings survive rebuilds
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gravity");
            Directory.CreateDirectory(appDataDir);
            _filePath = Path.Combine(appDataDir, "settings.json");
            _current = Load();
        }

        private AppSettings Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        DecryptEmailSecrets(settings);
                        EnsureProviderConnection(settings);
                        return settings;
                    }
                }
            }
            catch { }

            // Fallback: read from the project's appsettings.json for first-run defaults
            var projectFile = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(projectFile))
            {
                try
                {
                    var json = File.ReadAllText(projectFile);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        EnsureProviderConnection(settings);
                        // Migrate to user path on first load
                        _current = settings;
                        Save(settings);
                        return settings;
                    }
                }
                catch { }
            }

            return new AppSettings();
        }

        private static void EnsureProviderConnection(AppSettings settings)
        {
            var key = settings.Provider.ToString();
            settings.ProviderConnections ??= new Dictionary<string, ProviderConnection>();
            if (!settings.ProviderConnections.ContainsKey(key))
            {
                settings.ProviderConnections[key] = new ProviderConnection
                {
                    BaseUrl = settings.BaseUrl,
                    ApiKey = settings.ApiKey,
                    ModelName = settings.ModelName,
                    ReasoningModelName = settings.ReasoningModelName
                };
            }
        }

        public void Save(AppSettings settings)
        {
            _current = settings ?? throw new ArgumentNullException(nameof(settings));
            EnsureProviderConnection(_current);

            // Encrypt a deep-copy's sensitive fields before persisting
            var toSave = CloneForStorage(_current);
            var json = JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }

        // ── DPAPI helpers (Windows-only; falls back to plain text on other OS) ──

        private static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;
            if (!OperatingSystem.IsWindows()) return plainText;
            var bytes = Encoding.UTF8.GetBytes(plainText);
            var enc   = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(enc);
        }

        private static string Unprotect(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;
            if (!OperatingSystem.IsWindows()) return cipherText;
            try
            {
                var bytes = Convert.FromBase64String(cipherText);
                var dec   = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dec);
            }
            catch
            {
                // Not encrypted yet (first run / manual edit) — return as-is
                return cipherText;
            }
        }

        /// <summary>Returns a shallow-cloned AppSettings whose email secrets are encrypted.</summary>
        private static AppSettings CloneForStorage(AppSettings src)
        {
            var clone = new AppSettings
            {
                BaseUrl            = src.BaseUrl,
                ApiKey             = src.ApiKey,
                ModelName          = src.ModelName,
                ReasoningModelName = src.ReasoningModelName,
                DebugJson          = src.DebugJson,
                UseOllama          = src.UseOllama,
                Provider           = src.Provider,
                DevMode            = src.DevMode,
                Style              = src.Style,
                ContextWindowSize  = src.ContextWindowSize,
                MaxTokens          = src.MaxTokens,
                UseMaxTokens       = src.UseMaxTokens,
                UseMaxCompletionTokens = src.UseMaxCompletionTokens,
                MaxHistoryMessages = src.MaxHistoryMessages,
                MaxObservationLength = src.MaxObservationLength,
                MaxSteps           = src.MaxSteps,
                DisableNativeToolCalls = src.DisableNativeToolCalls,
                TruncateObservations   = src.TruncateObservations,
                PersistSession     = src.PersistSession,
                CriticEnabled      = src.CriticEnabled,
                PlanningMode       = src.PlanningMode,
                McpServers         = src.McpServers,
                ProviderConnections = src.ProviderConnections,
                EnvironmentVariables = src.EnvironmentVariables == null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(src.EnvironmentVariables, StringComparer.OrdinalIgnoreCase),
                SearxngUrl         = src.SearxngUrl,
                SearxngToken       = src.SearxngToken,
                SearchProvider     = src.SearchProvider,
                LangSearchApiKey   = src.LangSearchApiKey,
                GgufModelPath      = src.GgufModelPath,
                LlamaCppExePath    = src.LlamaCppExePath,
                LlamaCppGpuLayers  = src.LlamaCppGpuLayers,
                Email = new EmailConfig
                {
                    Provider     = src.Email.Provider,
                    UserId       = src.Email.UserId,
                    DefaultFrom  = src.Email.DefaultFrom,
                    SmtpHost     = src.Email.SmtpHost,
                    SmtpPort     = src.Email.SmtpPort,
                    ClientId     = src.Email.ClientId,
                    // Sensitive — encrypt before writing to disk
                    AccessToken  = Protect(src.Email.AccessToken),
                    RefreshToken = Protect(src.Email.RefreshToken),
                    ClientSecret = Protect(src.Email.ClientSecret),
                    ProviderSettings = new Dictionary<string, EmailProviderSettings>(StringComparer.OrdinalIgnoreCase)
                }
            };

            if (src.Email.ProviderSettings != null)
            {
                foreach (var kvp in src.Email.ProviderSettings)
                {
                    clone.Email.ProviderSettings[kvp.Key] = new EmailProviderSettings
                    {
                        UserId = kvp.Value.UserId,
                        DefaultFrom = kvp.Value.DefaultFrom,
                        SmtpHost = kvp.Value.SmtpHost,
                        SmtpPort = kvp.Value.SmtpPort,
                        ClientId = kvp.Value.ClientId,
                        AccessToken = Protect(kvp.Value.AccessToken),
                        RefreshToken = Protect(kvp.Value.RefreshToken),
                        ClientSecret = Protect(kvp.Value.ClientSecret)
                    };
                }
            }

            return clone;
        }

        /// <summary>Decrypts sensitive fields in-place after deserialisation.</summary>
        private static void DecryptEmailSecrets(AppSettings settings)
        {
            settings.Email.AccessToken  = Unprotect(settings.Email.AccessToken);
            settings.Email.RefreshToken = Unprotect(settings.Email.RefreshToken);
            settings.Email.ClientSecret = Unprotect(settings.Email.ClientSecret);

            if (settings.Email.ProviderSettings != null)
            {
                foreach (var kvp in settings.Email.ProviderSettings)
                {
                    kvp.Value.AccessToken = Unprotect(kvp.Value.AccessToken);
                    kvp.Value.RefreshToken = Unprotect(kvp.Value.RefreshToken);
                    kvp.Value.ClientSecret = Unprotect(kvp.Value.ClientSecret);
                }
            }
        }
    }
}
