using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    public class McpServerConfig
    {
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string[]? Args { get; set; }
        public Dictionary<string, string>? Env { get; set; }
    }

    public class McpToolDefinition
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public JsonElement? InputSchema { get; set; }
    }

    public class McpClient : IDisposable
    {
        private readonly McpServerConfig _config;
        private Process? _process;
        private StreamWriter? _stdin;
        private StreamReader? _stdout;
        private int _requestId = 0;
        private bool _connected;

        public McpClient(McpServerConfig config)
        {
            _config = config;
        }

        public async Task<List<McpToolDefinition>> ConnectAsync(CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _config.Command,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (_config.Args != null)
                foreach (var arg in _config.Args)
                    psi.ArgumentList.Add(arg);

            if (_config.Env != null)
                foreach (var kv in _config.Env)
                    psi.EnvironmentVariables[kv.Key] = kv.Value;

            _process = new Process { StartInfo = psi };
            _process.Start();
            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;

            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    System.Diagnostics.Debug.WriteLine($"[MCP:{_config.Name} stderr] {e.Data}");
            };
            _process.BeginErrorReadLine();

            // Initialize session
            var init = await SendRequestAsync("initialize", new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "gravity", version = "1.0.0" }
            }, ct);

            _connected = true;

            // List tools
            var toolsResult = await SendRequestAsync("tools/list", new { }, ct);
            var tools = new List<McpToolDefinition>();

            if (toolsResult.TryGetProperty("tools", out var toolsArray) && toolsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in toolsArray.EnumerateArray())
                {
                    tools.Add(new McpToolDefinition
                    {
                        Name = t.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        Description = t.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                        InputSchema = t.TryGetProperty("inputSchema", out var s) ? s : null
                    });
                }
            }

            return tools;
        }

        public async Task<JsonElement> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken ct)
        {
            if (!_connected)
                throw new InvalidOperationException("MCP client not connected.");

            var result = await SendRequestAsync("tools/call", new
            {
                name = toolName,
                arguments
            }, ct);

            return result;
        }

        private async Task<JsonElement> SendRequestAsync(string method, object parameters, CancellationToken ct)
        {
            if (_stdin == null || _stdout == null)
                throw new InvalidOperationException("MCP client not started.");

            var id = Interlocked.Increment(ref _requestId);
            var request = new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params = parameters
            };

            var json = JsonSerializer.Serialize(request);
            await _stdin.WriteLineAsync(json);
            await _stdin.FlushAsync();

            var responseLine = await _stdout.ReadLineAsync();
            if (responseLine == null)
                throw new InvalidOperationException("MCP server returned no response.");

            using var doc = JsonDocument.Parse(responseLine);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                var msg = error.TryGetProperty("message", out var em) ? em.GetString() ?? "unknown error" : "unknown error";
                throw new InvalidOperationException($"MCP error: {msg}");
            }

            return root.TryGetProperty("result", out var result) ? result.Clone() : default;
        }

        public void Dispose()
        {
            if (_process != null && !_process.HasExited)
            {
                try { _process.Kill(); } catch { }
                _process.Dispose();
            }
            _connected = false;
        }
    }
}
