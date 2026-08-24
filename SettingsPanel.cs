using System;
using System.Windows.Forms;
using Gravity.Core;

namespace Gravity
{
    public partial class SettingsPanel : System.Windows.Forms.UserControl
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Action? OnCloseRequested { get; set; }

        private readonly ISettingsService _settingsService;
        private readonly LlamaCppServerManager? _llamaCppManager;
        private AppSettings _tempSettings;
        private LlmProvider _previousProvider;

        public SettingsPanel(ISettingsService settingsService, LlamaCppServerManager? llamaCppManager = null)
        {
            InitializeComponent();
            _settingsService  = settingsService;
            _llamaCppManager  = llamaCppManager;

            if (_llamaCppManager != null)
                _llamaCppManager.OnLog += _ => UpdateServerStatus();

            LoadGitConfig();

            // Deep-clone current settings so Cancel discards changes
            _tempSettings = new AppSettings
            {
                BaseUrl                = _settingsService.Current.BaseUrl,
                ApiKey                 = _settingsService.Current.ApiKey,
                ModelName              = _settingsService.Current.ModelName,
                ReasoningModelName     = _settingsService.Current.ReasoningModelName,
                Provider               = _settingsService.Current.Provider,
                DebugJson              = _settingsService.Current.DebugJson,
                DevMode                = _settingsService.Current.DevMode,
                UseOllama              = _settingsService.Current.UseOllama,
                ContextWindowSize      = _settingsService.Current.ContextWindowSize,
                MaxTokens              = _settingsService.Current.MaxTokens,
                UseMaxTokens           = _settingsService.Current.UseMaxTokens,
                UseMaxCompletionTokens = _settingsService.Current.UseMaxCompletionTokens,
                MaxHistoryMessages     = _settingsService.Current.MaxHistoryMessages,
                MaxObservationLength   = _settingsService.Current.MaxObservationLength,
                MaxSteps               = _settingsService.Current.MaxSteps,
                DisableNativeToolCalls = _settingsService.Current.DisableNativeToolCalls,
                TruncateObservations   = _settingsService.Current.TruncateObservations,
                PersistSession         = _settingsService.Current.PersistSession,
                McpServers             = _settingsService.Current.McpServers,
                ProviderConnections    = _settingsService.Current.ProviderConnections,
                Style                  = _settingsService.Current.Style,
                SearchProvider         = _settingsService.Current.SearchProvider,
                LangSearchApiKey       = _settingsService.Current.LangSearchApiKey,
                GgufModelPath          = _settingsService.Current.GgufModelPath,
                LlamaCppExePath        = _settingsService.Current.LlamaCppExePath,
                LlamaCppGpuLayers      = _settingsService.Current.LlamaCppGpuLayers,
                PlanningMode           = _settingsService.Current.PlanningMode,
            };

            lstProviders.Items.AddRange(new[]
            {
                "OpenAI", "LM Studio", "Ollama", "OpenRouter", "Groq",
                "Google AI Studio", "Together AI", "GitHub Models", "vLLM",
                "NVIDIA NIM", "Kimi AI", "Local GGUF (llama.cpp)", "Hugging Face",
                "Cloudflare Workers AI", "Anthropic Direct", "Needle 2 (Cactus)"
            });
            lstProviders.DrawItem += LstProviders_DrawItem;
            lstProviders.SelectedIndexChanged += OnProviderListChanged;
            lstProviders.SelectedIndex = (int)_tempSettings.Provider;


            cmbModel.Leave          += OnModelLeave;
            cmbModel.KeyDown        += OnModelKeyDown;
            cmbModel.SelectedIndexChanged += OnModelSelectionChanged;
            cmbModel.TextChanged          += OnModelSelectionChanged;
            cmbReasoningModel.Leave += OnModelLeave;
            cmbReasoningModel.KeyDown += OnModelKeyDown;
            txtAccountId.TextChanged += OnAccountIdTextChanged;

            cmbDevMode.Items.AddRange(Enum.GetNames(typeof(DevelopmentMode)));
            cmbDevMode.SelectedItem = _tempSettings.DevMode.ToString();

            // ── Planning Mode ──────────────────────────────────────────────────
            cmbPlanningMode.Items.Clear();
            cmbPlanningMode.Items.AddRange(new[]
            {
                "Adaptive (Recommended — LLM drives its own tool loop)",
                "PrePlanned (Legacy — rigid JSON plan, good for small models)",
                "FreeForm (Zero pre-processing — maximum autonomy)"
            });
            cmbPlanningMode.SelectedIndex = (int)_tempSettings.PlanningMode;

            _previousProvider = (LlmProvider)lstProviders.SelectedIndex;

            // Load the current provider's saved connection into the text boxes
            LoadProviderFields(_previousProvider);
            chkDebugJson.Checked               = _tempSettings.DebugJson;
            chkDisableNativeTools.Checked      = _previousProvider == LlmProvider.Ollama || _previousProvider == LlmProvider.LocalGguf || _tempSettings.DisableNativeToolCalls;
            chkTruncateObservations.Checked    = _tempSettings.TruncateObservations;
            chkPersistSession.Checked          = _tempSettings.PersistSession;
            numMaxSteps.Value      = Math.Max(0,   Math.Min(9999,  _tempSettings.MaxSteps));
            numMaxObservation.Value = Math.Max(500, Math.Min(500000, _tempSettings.MaxObservationLength));
            numMaxTokens.Value     = Math.Max(1, Math.Min(1000000, _tempSettings.MaxTokens));

            // Load llama.cpp paths
            txtGgufPath.Text    = _tempSettings.GgufModelPath;
            txtLlamaCppExe.Text = _tempSettings.LlamaCppExePath;
            numGpuLayers.Value  = Math.Max(0, Math.Min(128, _tempSettings.LlamaCppGpuLayers));

            // Show/hide the llama.cpp group based on current provider
            grpLlamaCpp.Visible = _previousProvider == LlmProvider.LocalGguf;
            UpdateServerStatus();

            this.Resize += (s, e) => AdjustResponsiveLayout();
            this.Load   += (s, e) => AdjustResponsiveLayout();
            this.Layout += (s, e) => AdjustResponsiveLayout();
        }

        private void LstProviders_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= lstProviders.Items.Count) return;
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var rect = e.Bounds;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var provider = (LlmProvider)e.Index;
            string name = lstProviders.Items[e.Index]?.ToString() ?? "";

            System.Drawing.Color bg = selected ? System.Drawing.Color.FromArgb(45, 75, 140) : System.Drawing.Color.FromArgb(30, 33, 48);
            System.Drawing.Color fg = selected ? System.Drawing.Color.White : System.Drawing.Color.FromArgb(210, 220, 240);

            using (var br = new System.Drawing.SolidBrush(bg)) g.FillRectangle(br, rect);

            if (selected)
            {
                using var barBr = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0, 220, 255));
                g.FillRectangle(barBr, rect.X, rect.Y, 4, rect.Height);
            }

            using var font = new System.Drawing.Font("Segoe UI", 9f, selected ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular);
            using var textBr = new System.Drawing.SolidBrush(fg);
            g.DrawString($"🤖 {name}", font, textBr, rect.X + 10, rect.Y + 6);

            var key = ProviderKey(provider);
            bool hasKey = _tempSettings.ProviderConnections.TryGetValue(key, out var conn) && !string.IsNullOrWhiteSpace(conn.ApiKey);
            bool hasFetched = conn != null && conn.FetchedModels != null && conn.FetchedModels.Count > 0;

            string statusNote = hasFetched ? $"✔ {conn.FetchedModels.Count} models" : (hasKey ? "🔑 Key set" : "");
            if (!string.IsNullOrEmpty(statusNote))
            {
                using var statusFont = new System.Drawing.Font("Segoe UI", 7.5f, System.Drawing.FontStyle.Italic);
                using var statusBr = new System.Drawing.SolidBrush(selected ? System.Drawing.Color.LightSkyBlue : System.Drawing.Color.FromArgb(140, 160, 190));
                g.DrawString(statusNote, statusFont, statusBr, rect.X + 26, rect.Y + 22);
            }
        }

        private void OnProviderListChanged(object? sender, EventArgs e)
        {
            if (lstProviders.SelectedIndex < 0) return;
            SaveProviderConnection(_previousProvider);
            _previousProvider = (LlmProvider)lstProviders.SelectedIndex;
            if (lstProviders.SelectedIndex != lstProviders.SelectedIndex)
                lstProviders.SelectedIndex = lstProviders.SelectedIndex;

            LoadProviderFields(_previousProvider);
            lblProviderDetailTitle.Text = $"🤖 {lstProviders.SelectedItem} Configuration";

            chkDisableNativeTools.Checked = _previousProvider == LlmProvider.Ollama || _previousProvider == LlmProvider.LocalGguf
                ? true
                : _tempSettings.DisableNativeToolCalls;
            grpLlamaCpp.Visible = _previousProvider == LlmProvider.LocalGguf;
            UpdateServerStatus();
            if (lblModelFetchStatus != null)
                lblModelFetchStatus.Text = "Ready to sync provider models.";

            lstProviders.Invalidate();
        }

        private void AdjustResponsiveLayout()
        {
            try
            {
                int totalWidth = pnlProviderDetail.ClientSize.Width;
                if (totalWidth <= 0) return;

                int rightPadding = 25;
                int leftMargin = 10;
                int availableWidth = Math.Max(260, totalWidth - leftMargin - rightPadding);

                // Card 1: Provider Endpoint
                grpProviderEndpoint.Width = availableWidth;
                txtBaseUrl.Width = availableWidth - 26;
                txtAccountId.Width = availableWidth - 26;

                txtApiKey.Width = Math.Max(100, availableWidth - 170);
                btnToggleApiKey.Left = txtApiKey.Left + txtApiKey.Width + 6;
                lnkGetApiKey.Left = btnToggleApiKey.Left + btnToggleApiKey.Width + 8;

                // Card 2: Model Selection
                grpModelSelection.Width = availableWidth;
                cmbModel.Width = Math.Max(100, availableWidth - 190);
                btnRefreshModels.Left = cmbModel.Left + cmbModel.Width + 10;
                btnRefreshModels.Width = 158;
                lblModelFetchStatus.Width = Math.Max(100, availableWidth - 26);
                cmbReasoningModel.Width = Math.Max(100, availableWidth - 26);

                // Card 3: Local llama.cpp
                grpLlamaCpp.Width = availableWidth;
                txtGgufPath.Width = Math.Max(100, availableWidth - 205);
                btnBrowseGguf.Left = txtGgufPath.Left + txtGgufPath.Width + 10;
                txtLlamaCppExe.Width = Math.Max(100, availableWidth - 205);
                btnBrowseLlamaCpp.Left = txtLlamaCppExe.Left + txtLlamaCppExe.Width + 10;

                // Behavior Tab
                int behaviorWidth = Math.Max(280, tabControlSettings.ClientSize.Width - 50);
                grpModes.Width = behaviorWidth;
                cmbDevMode.Width = Math.Max(120, behaviorWidth - 150);
                cmbPlanningMode.Width = Math.Max(120, behaviorWidth - 150);

                grpLimits.Width = behaviorWidth;
                numMaxTokens.Width = Math.Max(120, behaviorWidth - 170);
                numMaxSteps.Width = Math.Max(120, behaviorWidth - 170);
                numMaxObservation.Width = Math.Max(120, behaviorWidth - 170);

                grpFlags.Width = behaviorWidth;
                chkDebugJson.Width = Math.Max(100, behaviorWidth - 28);
                chkDisableNativeTools.Width = Math.Max(100, behaviorWidth - 28);
                chkTruncateObservations.Width = Math.Max(100, behaviorWidth - 28);
                chkPersistSession.Width = Math.Max(100, behaviorWidth - 28);

                // System Tab
                grpGitConfig.Width = behaviorWidth;
                txtGitName.Width = Math.Max(120, behaviorWidth - 150);
                txtGitEmail.Width = Math.Max(120, behaviorWidth - 150);
                btnGitApply.Left = Math.Max(20, behaviorWidth - btnGitApply.Width - 16);

                grpAppUpdate.Width = behaviorWidth;
                btnUpdateApp.Width = Math.Max(100, behaviorWidth - 28);

                // Bottom Bar
                btnCancel.Left = pnlBottomBar.ClientSize.Width - btnCancel.Width - 25;
                btnSave.Left = btnCancel.Left - btnSave.Width - 12;
            }
            catch { }
        }

        // ── Model combo helpers ───────────────────────────────────────────────

        private void OnModelLeave(object? sender, EventArgs e)
        {
            if (sender is ComboBox cmb && !string.IsNullOrWhiteSpace(cmb.Text) && !cmb.Items.Contains(cmb.Text))
                cmb.Items.Add(cmb.Text);
        }

        private void OnModelKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                OnModelLeave(sender, EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void OnModelSelectionChanged(object? sender, EventArgs e)
        {
            var provider = (LlmProvider)lstProviders.SelectedIndex;
            var spec = ModelInfoService.GetSpec(provider, cmbModel.Text);
            _tempSettings.ContextWindowSize = spec.ContextWindowSize;
            _tempSettings.UseMaxCompletionTokens = spec.UseMaxCompletionTokens;
            numMaxTokens.Value = Math.Max(1, Math.Min(1000000, spec.MaxTokens));
        }

        // ── Provider switching ────────────────────────────────────────────────

        private void OnProviderChanged(object? sender, EventArgs e)
        {
            SaveProviderConnection(_previousProvider);
            _previousProvider = (LlmProvider)lstProviders.SelectedIndex;
            LoadProviderFields(_previousProvider);
            chkDisableNativeTools.Checked = _previousProvider == LlmProvider.Ollama || _previousProvider == LlmProvider.LocalGguf
                ? true
                : _tempSettings.DisableNativeToolCalls;
            grpLlamaCpp.Visible = _previousProvider == LlmProvider.LocalGguf;
            UpdateServerStatus();
            if (lblModelFetchStatus != null)
                lblModelFetchStatus.Text = "Ready to sync provider models.";
        }

        private string ProviderKey(LlmProvider p) => p.ToString();

        private void OnAccountIdTextChanged(object? sender, EventArgs e)
        {
            if (lstProviders == null || txtAccountId == null || txtBaseUrl == null) return;
            var provider = (LlmProvider)lstProviders.SelectedIndex;
            if (provider == LlmProvider.Cloudflare)
            {
                var accId = txtAccountId.Text.Trim();
                var idToUse = string.IsNullOrWhiteSpace(accId) ? "{ACCOUNT_ID}" : accId;
                txtBaseUrl.Text = $"https://api.cloudflare.com/client/v4/accounts/{idToUse}/ai/v1";
            }
        }

        private void SaveProviderConnection(LlmProvider provider)
        {
            var key = ProviderKey(provider);
            var existingFetched = _tempSettings.ProviderConnections.TryGetValue(key, out var oldConn) && oldConn.FetchedModels != null
                ? oldConn.FetchedModels
                : new List<string>();

            _tempSettings.ProviderConnections[key] = new ProviderConnection
            {
                BaseUrl            = txtBaseUrl.Text.Trim(),
                ApiKey             = txtApiKey.Text.Trim(),
                ModelName          = cmbModel.Text.Trim(),
                ReasoningModelName = cmbReasoningModel.Text.Trim(),
                AccountId          = txtAccountId?.Text.Trim() ?? "",
                FetchedModels      = existingFetched
            };
        }

        private void PopulateModelDropdowns(LlmProvider provider)
        {
            cmbModel.Items.Clear();
            cmbReasoningModel.Items.Clear();
            string[] commonModels = provider switch
            {
                LlmProvider.OpenAI        => new[] { "gpt-4o", "gpt-4o-mini", "o1", "o3-mini", "gpt-4-turbo" },
                LlmProvider.Ollama        => new[] { "qwen2.5-coder:1.5b", "qwen2.5-coder:7b", "llama3.2:1b", "deepseek-coder-v2" },
                LlmProvider.Groq          => new[] { "llama-3.3-70b-versatile", "llama3-8b-8192", "mixtral-8x7b-32768" },
                LlmProvider.OpenRouter    => new[] { "anthropic/claude-3.5-sonnet", "google/gemini-2.5-flash", "meta-llama/llama-3.3-70b-instruct" },
                LlmProvider.GoogleAIStudio => new[] { "gemini-2.5-flash", "gemini-2.0-flash-exp", "gemini-1.5-pro" },
                LlmProvider.TogetherAI    => new[] { "meta-llama/Llama-3.3-70B-Instruct-Turbo", "mistralai/Mixtral-8x7B-Instruct-v0.1" },
                LlmProvider.GitHubModels  => new[] { "gpt-4o", "gpt-4o-mini", "Phi-3-mini-4k-instruct" },
                LlmProvider.Vllm          => new[] { "default", "meta-llama/Llama-3-70B-Instruct", "mistralai/Mixtral-8x7B-Instruct-v0.1" },
                LlmProvider.NvidiaAI      => new[] { "meta/llama-3.3-70b-instruct", "mistralai/mistral-nemo-12b-instruct", "microsoft/phi-4", "nvidia/llama-3.1-nemotron-70b-instruct", "google/gemma-3-27b-it" },
                LlmProvider.KimiAI        => new[] { "kimi-k2", "kimi-k2-reasoning", "kimi-k1.5-thinking", "moonshot-v1-8k", "moonshot-v1-32k", "moonshot-v1-128k" },
                LlmProvider.LocalGguf     => new[] { "local", "default" },
                LlmProvider.HuggingFace   => new[] { "Qwen/Qwen2.5-Coder-32B-Instruct", "meta-llama/Llama-3.3-70B-Instruct", "mistralai/Mistral-7B-Instruct-v0.3", "HuggingFaceH4/zephyr-7b-beta", "microsoft/Phi-3-mini-4k-instruct" },
                LlmProvider.Cloudflare    => new[] {
                    "@cf/openai/gpt-oss-120b",
                    "@cf/openai/gpt-oss-20b",
                    "@cf/deepseek-ai/deepseek-v4-pro-0813",
                    "@cf/deepseek-ai/deepseek-v4-flash-0731",
                    "@cf/meta/llama-4-scout-17b-16e-instruct",
                    "@cf/meta/llama-3.3-70b-instruct-fp8-fast",
                    "@cf/qwen/qwen3-30b-a3b-fp8",
                    "@cf/qwen/qwq-32b",
                    "@cf/google/gemma-4-26b-a4b-it",
                    "@cf/google/gemma-3-12b-it",
                    "@cf/nvidia/nemotron-3-120b-a12b",
                    "@cf/zai-org/glm-5.2",
                    "@cf/zai-org/glm-4.7-flash",
                    "@cf/moonshotai/kimi-k2.7",
                    "@cf/moonshotai/kimi-k2.7-code",
                    "@cf/moonshotai/kimi-k2.6",
                    "@cf/mistralai/mistral-small-3.1-24b-instruct",
                    "@cf/deepseek-ai/deepseek-r1-distill-qwen-32b",
                    "@cf/meta/llama-3.1-8b-instruct-fp8",
                    "@cf/meta/llama-3.1-8b-instruct-fast",
                    "@cf/meta/llama-3.2-3b-instruct",
                    "@cf/meta/llama-3.2-1b-instruct",
                    "@cf/qwen/qwen2.5-coder-32b-instruct",
                    "@cf/ibm/granite-4.0-h-micro",
                },
                LlmProvider.Anthropic     => new[] { "claude-3-5-sonnet-20241022", "claude-3-5-haiku-20241022", "anthropic/claude-fable-5", "fable", "claude-3-5-fable", "anthropic/claude-opus-5", "claude-3-5-opus", "claude-3-opus-20240229", "claude-3-haiku-20240307" },
                LlmProvider.Needle2       => new[] { "needle-2", "needle-2-45m", "cactus-needle-2" },
                _                         => new string[] { }
            };

            var key = ProviderKey(provider);
            var allModels = new List<string>(commonModels);
            if (_tempSettings.ProviderConnections.TryGetValue(key, out var conn) && conn.FetchedModels != null)
            {
                foreach (var fm in conn.FetchedModels)
                {
                    if (!allModels.Contains(fm)) allModels.Add(fm);
                }
            }

            cmbModel.Items.AddRange(allModels.ToArray());
            cmbReasoningModel.Items.AddRange(allModels.ToArray());
        }

        private void LoadProviderFields(LlmProvider provider)
        {
            PopulateModelDropdowns(provider);

            if (lblAccountId != null && txtAccountId != null)
            {
                if (provider == LlmProvider.Cloudflare)
                {
                    lblAccountId.Text = "Cloudflare Account ID:";
                    lblAccountId.Visible = true;
                    txtAccountId.Visible = true;
                }
                else if (provider == LlmProvider.OpenAI)
                {
                    lblAccountId.Text = "Organization / Project ID (Optional):";
                    lblAccountId.Visible = true;
                    txtAccountId.Visible = true;
                }
                else
                {
                    lblAccountId.Visible = false;
                    txtAccountId.Visible = false;
                }
            }

            var key = ProviderKey(provider);
            if (_tempSettings.ProviderConnections.TryGetValue(key, out var conn)
                && !string.IsNullOrEmpty(conn.BaseUrl))
            {
                txtAccountId.Text      = conn.AccountId ?? "";
                txtBaseUrl.Text        = conn.BaseUrl;
                txtApiKey.Text         = conn.ApiKey;
                cmbModel.Text          = string.IsNullOrEmpty(conn.ModelName)          ? _tempSettings.ModelName          : conn.ModelName;
                cmbReasoningModel.Text = string.IsNullOrEmpty(conn.ReasoningModelName) ? _tempSettings.ReasoningModelName : conn.ReasoningModelName;

                if (!string.IsNullOrWhiteSpace(cmbModel.Text)          && !cmbModel.Items.Contains(cmbModel.Text))          cmbModel.Items.Add(cmbModel.Text);
                if (!string.IsNullOrWhiteSpace(cmbReasoningModel.Text) && !cmbReasoningModel.Items.Contains(cmbReasoningModel.Text)) cmbReasoningModel.Items.Add(cmbReasoningModel.Text);
            }
            else
            {
                txtAccountId.Text = "";
                // First time for this provider — auto-fill URL and model defaults
                txtBaseUrl.Text = provider switch
                {
                    LlmProvider.OpenAI         => "https://api.openai.com/v1",
                    LlmProvider.LMStudio       => "http://localhost:1234/v1",
                    LlmProvider.Ollama         => "http://localhost:11434",
                    LlmProvider.OpenRouter     => "https://openrouter.ai/api/v1",
                    LlmProvider.Groq           => "https://api.groq.com/openai/v1",
                    LlmProvider.GoogleAIStudio => "https://generativelanguage.googleapis.com/v1beta/openai",
                    LlmProvider.TogetherAI     => "https://api.together.xyz/v1",
                    LlmProvider.GitHubModels   => "https://models.inference.ai.azure.com",
                    LlmProvider.Vllm           => "http://localhost:8000/v1",
                    LlmProvider.NvidiaAI       => "https://integrate.api.nvidia.com/v1",
                    LlmProvider.KimiAI         => "https://api.moonshot.ai/v1",
                    LlmProvider.LocalGguf      => "http://localhost:8080",
                    LlmProvider.HuggingFace    => "https://router.huggingface.co/v1",
                    LlmProvider.Cloudflare     => "https://api.cloudflare.com/client/v4/accounts/{ACCOUNT_ID}/ai/v1",
                    LlmProvider.Anthropic      => "https://api.anthropic.com/v1",
                    LlmProvider.Needle2        => "http://localhost:8080/v1",
                    _                          => ""
                };
                txtApiKey.Text = provider == LlmProvider.LocalGguf ? "not-needed" : _tempSettings.ApiKey;
                cmbModel.Text = provider switch
                {
                    LlmProvider.OpenAI         => "gpt-4o",
                    LlmProvider.LMStudio       => _tempSettings.ModelName,
                    LlmProvider.Ollama         => "qwen2.5-coder:1.5b",
                    LlmProvider.OpenRouter     => "anthropic/claude-3.5-sonnet",
                    LlmProvider.Groq           => "llama-3.3-70b-versatile",
                    LlmProvider.GoogleAIStudio => "gemini-2.0-flash-exp",
                    LlmProvider.TogetherAI     => "meta-llama/Llama-3.3-70B-Instruct-Turbo",
                    LlmProvider.GitHubModels   => "gpt-4o-mini",
                    LlmProvider.Vllm           => "default",
                    LlmProvider.NvidiaAI       => "meta/llama-3.3-70b-instruct",
                    LlmProvider.KimiAI         => "kimi-k2",
                    LlmProvider.LocalGguf      => "local",
                    LlmProvider.HuggingFace    => "Qwen/Qwen2.5-Coder-32B-Instruct",
                    LlmProvider.Cloudflare     => "@cf/openai/gpt-oss-120b",
                    LlmProvider.Anthropic      => "claude-3-5-sonnet-20241022",
                    LlmProvider.Needle2        => "needle-2",
                    _                          => _tempSettings.ModelName
                };
                cmbReasoningModel.Text = cmbModel.Text;
            }

            UpdateApiKeyLink(provider);
            OnModelSelectionChanged(null, EventArgs.Empty);
        }

        // ── API Key link helpers ──────────────────────────────────────────────

        private string? GetApiKeyUrl(LlmProvider provider) => provider switch
        {
            LlmProvider.OpenAI         => "https://platform.openai.com/api-keys",
            LlmProvider.OpenRouter     => "https://openrouter.ai/keys",
            LlmProvider.Groq           => "https://console.groq.com/keys",
            LlmProvider.GoogleAIStudio => "https://aistudio.google.com/apikey",
            LlmProvider.TogetherAI     => "https://api.together.ai/settings/api-keys",
            LlmProvider.GitHubModels   => "https://github.com/settings/tokens",
            LlmProvider.NvidiaAI       => "https://build.nvidia.com/",
            LlmProvider.KimiAI         => "https://platform.moonshot.cn/console/api-keys",
            LlmProvider.HuggingFace    => "https://huggingface.co/settings/tokens",
            LlmProvider.Cloudflare     => "https://dash.cloudflare.com/profile/api-tokens",
            LlmProvider.Anthropic      => "https://console.anthropic.com/settings/keys",
            LlmProvider.Needle2        => "https://github.com/cactus-compute/needle",
            // Local providers don't need API keys
            LlmProvider.LMStudio       => null,
            LlmProvider.Ollama         => null,
            LlmProvider.Vllm           => null,
            LlmProvider.LocalGguf      => null,
            _                          => null
        };

        private void UpdateApiKeyLink(LlmProvider provider)
        {
            var url = GetApiKeyUrl(provider);
            lnkGetApiKey.Visible = url != null;
            lnkGetApiKey.Tag = url; // store for click handler
        }

        private void lnkGetApiKey_Click(object? sender, EventArgs e)
        {
            if (lnkGetApiKey.Tag is string url && !string.IsNullOrEmpty(url))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch { }
            }
        }

        private void btnToggleApiKey_Click(object? sender, EventArgs e)
        {
            if (txtApiKey == null || btnToggleApiKey == null) return;
            if (txtApiKey.PasswordChar == '*')
            {
                txtApiKey.PasswordChar = '\0';
                btnToggleApiKey.Text = "🙈";
            }
            else
            {
                txtApiKey.PasswordChar = '*';
                btnToggleApiKey.Text = "👁";
            }
        }

        // ── Save / Cancel ─────────────────────────────────────────────────────

        private void btnSave_Click(object sender, EventArgs e)
        {
            if ((LlmProvider)lstProviders.SelectedIndex == LlmProvider.Cloudflare && txtBaseUrl.Text.Contains("{ACCOUNT_ID}", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Please replace '{ACCOUNT_ID}' in the Base URL with your actual Cloudflare Account ID.\n\nYou can find your 32-character Account ID in your Cloudflare Dashboard (dash.cloudflare.com) under Account Overview.", "Cloudflare Account ID Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtBaseUrl.Focus();
                return;
            }

            // Save current fields to the selected provider's slot
            SaveProviderConnection((LlmProvider)lstProviders.SelectedIndex);

            // Copy the selected provider's connection to the top-level active fields
            var key = ProviderKey((LlmProvider)lstProviders.SelectedIndex);
            if (_tempSettings.ProviderConnections.TryGetValue(key, out var conn))
            {
                _tempSettings.BaseUrl            = conn.BaseUrl;
                _tempSettings.ApiKey             = conn.ApiKey;
                _tempSettings.ModelName          = conn.ModelName;
                _tempSettings.ReasoningModelName = conn.ReasoningModelName;
            }

            _tempSettings.Provider               = (LlmProvider)lstProviders.SelectedIndex;
            _tempSettings.DebugJson              = chkDebugJson.Checked;
            _tempSettings.DevMode                = (DevelopmentMode)Enum.Parse(typeof(DevelopmentMode), cmbDevMode.SelectedItem?.ToString() ?? "Standard");
            _tempSettings.DisableNativeToolCalls = chkDisableNativeTools.Checked;
            _tempSettings.TruncateObservations   = chkTruncateObservations.Checked;
            _tempSettings.PersistSession         = chkPersistSession.Checked;
            _tempSettings.MaxSteps               = (int)numMaxSteps.Value;
            _tempSettings.MaxObservationLength   = (int)numMaxObservation.Value;
            _tempSettings.MaxTokens              = (int)numMaxTokens.Value;
            _tempSettings.UseMaxTokens           = true;
            _tempSettings.UseMaxCompletionTokens = ModelInfoService.GetSpec(_tempSettings.Provider, _tempSettings.ModelName).UseMaxCompletionTokens;
            _tempSettings.PlanningMode            = (PlanningMode)cmbPlanningMode.SelectedIndex;
            _tempSettings.SearchProvider         = _settingsService.Current.SearchProvider;
            _tempSettings.LangSearchApiKey       = _settingsService.Current.LangSearchApiKey;

            // Save llama.cpp-specific fields
            _tempSettings.GgufModelPath     = txtGgufPath.Text.Trim();
            _tempSettings.LlamaCppExePath   = txtLlamaCppExe.Text.Trim();
            _tempSettings.LlamaCppGpuLayers = (int)numGpuLayers.Value;

            _settingsService.Save(_tempSettings);
            MessageBox.Show("Settings saved.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            OnCloseRequested?.Invoke();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            OnCloseRequested?.Invoke();
        }

        // ── Browse buttons ────────────────────────────────────────────────────

        private void btnBrowseGguf_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "Select GGUF Model File",
                Filter = "GGUF files (*.gguf)|*.gguf|All files (*.*)|*.*"
            };
            if (!string.IsNullOrWhiteSpace(txtGgufPath.Text))
                dlg.InitialDirectory = System.IO.Path.GetDirectoryName(txtGgufPath.Text);

            if (dlg.ShowDialog() == DialogResult.OK)
                txtGgufPath.Text = dlg.FileName;
        }

        private void btnBrowseLlamaCpp_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "Select llama-server.exe",
                Filter = "llama-server (llama-server.exe)|llama-server.exe|Executable files (*.exe)|*.exe|All files (*.*)|*.*"
            };
            if (!string.IsNullOrWhiteSpace(txtLlamaCppExe.Text))
                dlg.InitialDirectory = System.IO.Path.GetDirectoryName(txtLlamaCppExe.Text);

            if (dlg.ShowDialog() == DialogResult.OK)
                txtLlamaCppExe.Text = dlg.FileName;
        }

        // ── Start / Stop server ───────────────────────────────────────────────

        private async void btnStartServer_Click(object sender, EventArgs e)
        {
            if (_llamaCppManager == null) return;

            SaveProviderConnection((LlmProvider)lstProviders.SelectedIndex);
            _settingsService.Current.GgufModelPath     = txtGgufPath.Text.Trim();
            _settingsService.Current.LlamaCppExePath    = txtLlamaCppExe.Text.Trim();
            _settingsService.Current.LlamaCppGpuLayers   = (int)numGpuLayers.Value;
            _settingsService.Current.Provider           = LlmProvider.LocalGguf;
            _settingsService.Current.ProviderConnections = _tempSettings.ProviderConnections;

            btnStartServer.Enabled = false;
            btnStartServer.Text    = "Starting…";
            try
            {
                await _llamaCppManager.StartAsync();
            }
            finally
            {
                btnStartServer.Enabled = true;
                btnStartServer.Text    = "▶ Start Server";
                UpdateServerStatus();
            }
        }

        private void btnStopServer_Click(object sender, EventArgs e)
        {
            _llamaCppManager?.Stop();
            UpdateServerStatus();
        }

        private void UpdateServerStatus()
        {
            if (InvokeRequired) { BeginInvoke(UpdateServerStatus); return; }

            bool running = _llamaCppManager?.IsRunning ?? false;
            lblLlamaStatus.Text      = running ? "⬤ Running"  : "⬤ Stopped";
            lblLlamaStatus.ForeColor = running
                ? System.Drawing.Color.LimeGreen
                : System.Drawing.Color.Gray;
        }

        // ── Update app ────────────────────────────────────────────────────────

        private async void btnUpdateApp_Click(object sender, EventArgs e)
        {
            btnUpdateApp.Enabled = false;
            btnUpdateApp.Text    = "⏳ Checking...";
            try
            {
                var updateService = new UpdateService(new System.Net.Http.HttpClient(), "https://raw.githubusercontent.com/example/repo/main/update.json");
                var updateInfo = await updateService.CheckForUpdatesAsync();
                if (updateInfo != null)
                {
                    var result = MessageBox.Show($"A new update is available (Version {updateInfo.LatestVersion})!\n\nDo you want to update now?", "Update Available", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                        await updateService.DownloadAndApplyUpdateAsync(updateInfo.DownloadUrl);
                }
                else
                {
                    MessageBox.Show("Your application is up to date.", "Up to Date", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error checking for updates: {ex.Message}", "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnUpdateApp.Enabled = true;
                btnUpdateApp.Text    = "🔄 Check for Updates";
            }
        }

        // ── Git config ────────────────────────────────────────────────────────

        private void LoadGitConfig()
        {
            try
            {
                txtGitName.Text  = RunGitCommand("config --global user.name");
                txtGitEmail.Text = RunGitCommand("config --global user.email");
            }
            catch { }
        }

        private void btnGitApply_Click(object sender, EventArgs e)
        {
            try
            {
                RunGitCommand($"config --global user.name \"{txtGitName.Text.Trim()}\"");
                RunGitCommand($"config --global user.email \"{txtGitEmail.Text.Trim()}\"");
                MessageBox.Show("Git configuration updated.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error updating Git config: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string RunGitCommand(string args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", args)
            {
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return "";
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return proc.ExitCode == 0 ? output.Trim() : "";
        }

        // ── Custom Tab Control Drawing ────────────────────────────────────────
        private void tabControlSettings_DrawItem(object? sender, DrawItemEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var tabRect = tabControlSettings.GetTabRect(e.Index);
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            System.Drawing.Color bg = selected ? System.Drawing.Color.FromArgb(38, 42, 60) : System.Drawing.Color.FromArgb(24, 26, 38);
            System.Drawing.Color fg = selected ? System.Drawing.Color.FromArgb(255, 255, 255) : System.Drawing.Color.FromArgb(160, 165, 185);

            using (var brush = new System.Drawing.SolidBrush(bg))
                g.FillRectangle(brush, tabRect);

            if (selected)
            {
                using var accentBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(98, 126, 234));
                g.FillRectangle(accentBrush, tabRect.X, tabRect.Y, tabRect.Width, 3);
            }

            var sf = new System.Drawing.StringFormat
            {
                Alignment = System.Drawing.StringAlignment.Center,
                LineAlignment = System.Drawing.StringAlignment.Center
            };

            using (var textBrush = new System.Drawing.SolidBrush(fg))
            using (var font = new System.Drawing.Font(this.Font.FontFamily, 9F, selected ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular))
            {
                g.DrawString(tabControlSettings.TabPages[e.Index].Text, font, textBrush, tabRect, sf);
            }
        }

        // ── Model Refresh / Sync Button Handler ───────────────────────────────
        private async void btnRefreshModels_Click(object? sender, EventArgs e)
        {
            var provider = (LlmProvider)lstProviders.SelectedIndex;
            btnRefreshModels.Enabled = false;
            btnRefreshModels.Text = "⏳ Syncing...";
            lblModelFetchStatus.Text = "Fetching available models from provider API...";
            lblModelFetchStatus.ForeColor = System.Drawing.Color.FromArgb(255, 200, 100);

            try
            {
                var fetched = await ModelInfoService.FetchModelsFromApiAsync(provider, txtBaseUrl.Text.Trim(), txtApiKey.Text.Trim(), txtAccountId.Text.Trim());
                if (fetched != null && fetched.Count > 0)
                {
                    var currentPrimary = string.IsNullOrWhiteSpace(cmbModel.Text) ? fetched[0] : cmbModel.Text;
                    var currentReasoning = string.IsNullOrWhiteSpace(cmbReasoningModel.Text) ? fetched[0] : cmbReasoningModel.Text;

                    cmbModel.Items.Clear();
                    cmbReasoningModel.Items.Clear();

                    foreach (var m in fetched)
                    {
                        if (!cmbModel.Items.Contains(m)) cmbModel.Items.Add(m);
                        if (!cmbReasoningModel.Items.Contains(m)) cmbReasoningModel.Items.Add(m);
                    }

                    if (!string.IsNullOrEmpty(currentPrimary) && !cmbModel.Items.Contains(currentPrimary))
                        cmbModel.Items.Add(currentPrimary);
                    cmbModel.Text = currentPrimary;

                    if (!string.IsNullOrEmpty(currentReasoning) && !cmbReasoningModel.Items.Contains(currentReasoning))
                        cmbReasoningModel.Items.Add(currentReasoning);
                    cmbReasoningModel.Text = currentReasoning;

                    // Immediately persist fetched models into ProviderConnection
                    var key = ProviderKey(provider);
                    _tempSettings.ProviderConnections[key] = new ProviderConnection
                    {
                        BaseUrl            = txtBaseUrl.Text.Trim(),
                        ApiKey             = txtApiKey.Text.Trim(),
                        ModelName          = cmbModel.Text.Trim(),
                        ReasoningModelName = cmbReasoningModel.Text.Trim(),
                        AccountId          = txtAccountId?.Text.Trim() ?? "",
                        FetchedModels      = fetched
                    };

                    lblModelFetchStatus.Text = $"✔ Successfully loaded {fetched.Count} models from API!";
                    lblModelFetchStatus.ForeColor = System.Drawing.Color.LimeGreen;
                }
                else
                {
                    lblModelFetchStatus.Text = "⚠ Could not fetch remote models. Using registry defaults.";
                    lblModelFetchStatus.ForeColor = System.Drawing.Color.Orange;
                }
            }
            catch (Exception ex)
            {
                lblModelFetchStatus.Text = $"⚠ Error fetching models: {ex.Message}";
                lblModelFetchStatus.ForeColor = System.Drawing.Color.IndianRed;
            }
            finally
            {
                btnRefreshModels.Enabled = true;
                btnRefreshModels.Text = "🔄 Fetch Live Models";
            }
        }
    }
}
