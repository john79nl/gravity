using System;
using System.IO;
using System.Text.Json;

namespace Gravity.Core
{
    public enum DevelopmentMode { Autopilot, Review, Assisted }
    public enum LlmProvider { OpenAI, LMStudio, Ollama, OpenRouter, Groq, GoogleAIStudio, TogetherAI, GitHubModels, Vllm, NvidiaAI, KimiAI, LocalGguf, HuggingFace, Cloudflare, Anthropic }
    
    public class AppSettings
    {
        public string BaseUrl { get; set; } = "http://localhost:11434";
        public string ApiKey { get; set; } = "not-needed";
        public string ModelName { get; set; } = "qwen2.5-coder:7b";
        public string ReasoningModelName { get; set; } = "qwen2.5-coder:7b";
        public bool DebugJson { get; set; } = false;
        public bool UseOllama { get; set; } = false; // Legacy - kept for migration
        public LlmProvider Provider { get; set; } = LlmProvider.Ollama;
        public DevelopmentMode DevMode { get; set; } = DevelopmentMode.Autopilot;
        
        public int ContextWindowSize { get; set; } = 4096;
        public int MaxTokens { get; set; } = 8192;
        public int MaxHistoryMessages { get; set; } = 12;
        public int MaxObservationLength { get; set; } = 2000;
        public int MaxSteps { get; set; } = 15;
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
            _filePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            _current = Load();
        }

        private AppSettings Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save(AppSettings settings)
        {
            _current = settings ?? throw new ArgumentNullException(nameof(settings));
            try
            {
                var json = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch { }
        }
    }
}
