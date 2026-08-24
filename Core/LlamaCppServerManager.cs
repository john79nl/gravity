using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    /// <summary>
    /// Manages the lifecycle of a local llama-server.exe child process.
    /// The server exposes an OpenAI-compatible REST API that the existing
    /// <see cref="GenericOpenAIClient"/> connects to transparently via
    /// http://localhost:{port}/v1/chat/completions.
    /// </summary>
    public sealed class LlamaCppServerManager : IDisposable
    {
        private readonly ISettingsService _settings;
        private readonly HttpClient _healthClient;
        private Process? _serverProcess;
        private bool _ownsProcess;

        /// <summary>Fired with diagnostic lines — forward these to your debug log.</summary>
        public event Action<string>? OnLog;

        /// <summary>True while the child process is alive.</summary>
        public bool IsRunning => _serverProcess != null && !_serverProcess.HasExited;

        public LlamaCppServerManager(ISettingsService settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static int ParsePort(string baseUrl)
        {
            if (Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) && uri.Port > 0)
                return uri.Port;
            return 8080;
        }

        private async Task<bool> IsHealthyAsync(string healthUrl, CancellationToken ct)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(2500);
                var response = await _healthClient.GetAsync(healthUrl, cts.Token).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private void Log(string message) => OnLog?.Invoke(message);

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Launches llama-server.exe if the current provider is LocalGguf and
        /// the configured paths are valid. Skips launch if the port already responds.
        /// Waits up to 120 seconds for the server to become healthy.
        /// </summary>
        public async Task StartAsync(CancellationToken ct = default)
        {
            var cfg = _settings.Current;
            if (cfg.Provider != LlmProvider.LocalGguf) return;

            var exePath = cfg.LlamaCppExePath?.Trim();
            var modelPath = cfg.GgufModelPath?.Trim();

            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                Log("llama.cpp: LlamaCppExePath is not configured or file not found — skipping auto-launch.");
                return;
            }
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                Log("llama.cpp: GgufModelPath is not configured or file not found — skipping auto-launch.");
                return;
            }

            // Resolve port from the stored ProviderConnection BaseUrl
            string baseUrl = "http://localhost:8080";
            if (cfg.ProviderConnections != null &&
                cfg.ProviderConnections.TryGetValue("LocalGguf", out var conn) &&
                !string.IsNullOrEmpty(conn.BaseUrl))
                baseUrl = conn.BaseUrl;

            var port = ParsePort(baseUrl);
            var healthUrl = $"http://localhost:{port}/health";

            // If the port is already serving (e.g. user started it manually), skip launch
            if (await IsHealthyAsync(healthUrl, ct).ConfigureAwait(false))
            {
                Log($"llama.cpp: Server already running on port {port} — skipping launch.");
                return;
            }

            var ngl = cfg.LlamaCppGpuLayers;
            var ctxSize = cfg.ContextWindowSize > 0 ? cfg.ContextWindowSize : 4096;
            var args = $"-m \"{modelPath}\" --port {port} -ngl {ngl} -c {ctxSize}";
            Log($"llama.cpp: Launching → {exePath}");
            Log($"llama.cpp: Args      → {args}");

            var psi = new ProcessStartInfo(exePath, args)
            {
                UseShellExecute = false,
                CreateNoWindow  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
            };

            _serverProcess = Process.Start(psi);
            if (_serverProcess == null)
            {
                Log("llama.cpp: Process.Start returned null — launch failed.");
                return;
            }
            _ownsProcess = true;

            // Pipe server stdout/stderr to the log
            _serverProcess.OutputDataReceived += (_, e) => { if (e.Data != null) Log($"[llama] {e.Data}"); };
            _serverProcess.ErrorDataReceived  += (_, e) => { if (e.Data != null) Log($"[llama] {e.Data}"); };
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();

            Log($"llama.cpp: Waiting for server to be ready on port {port} (up to 120 s)…");

            var deadline = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (_serverProcess.HasExited)
                {
                    Log($"llama.cpp: Process exited unexpectedly (exit code {_serverProcess.ExitCode}).");
                    return;
                }

                if (await IsHealthyAsync(healthUrl, ct).ConfigureAwait(false))
                {
                    Log("llama.cpp: Server is ready ✓");
                    return;
                }

                await Task.Delay(1500, ct).ConfigureAwait(false);
            }

            Log("llama.cpp: Timed out waiting for server health endpoint.");
        }

        /// <summary>
        /// Kills the child process if it was started by this manager.
        /// Safe to call even if the server is not running.
        /// </summary>
        public void Stop()
        {
            if (_serverProcess == null || !_ownsProcess) return;
            try
            {
                if (!_serverProcess.HasExited)
                {
                    Log("llama.cpp: Stopping server…");
                    _serverProcess.Kill(entireProcessTree: true);
                    _serverProcess.WaitForExit(3000);
                    Log("llama.cpp: Server stopped.");
                }
            }
            catch (Exception ex)
            {
                Log($"llama.cpp: Error stopping server: {ex.Message}");
            }
            finally
            {
                _serverProcess.Dispose();
                _serverProcess = null;
                _ownsProcess = false;
            }
        }

        public void Dispose()
        {
            Stop();
            _healthClient.Dispose();
        }
    }
}
