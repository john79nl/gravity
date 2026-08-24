using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    public enum DebugSessionState { Idle, Running, Paused, Stopped }

    public class Breakpoint
    {
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    public class DebugService : IDisposable
    {
        private readonly ConcurrentDictionary<string, List<Breakpoint>> _breakpoints = new();
        private Process? _debugProcess;
        private CancellationTokenSource? _cts;
        private DebugSessionState _state = DebugSessionState.Idle;

        public DebugSessionState State
        {
            get => _state;
            private set
            {
                _state = value;
                OnStateChanged?.Invoke(value);
            }
        }

        public event Action<Breakpoint>? OnBreakpointToggled;
        public event Action<Breakpoint>? OnBreakpointHit;
        public event Action<string, bool>? OnDebugOutput; // message, isError
        public event Action<DebugSessionState>? OnStateChanged;

        // ── Breakpoint Management ─────────────────────────────────────────────

        public IReadOnlyList<Breakpoint> GetBreakpoints(string filePath)
        {
            filePath = Normalize(filePath);
            return _breakpoints.TryGetValue(filePath, out var list)
                ? list.AsReadOnly()
                : Array.Empty<Breakpoint>();
        }

        public IReadOnlyList<Breakpoint> GetAllBreakpoints() =>
            _breakpoints.Values.SelectMany(b => b).ToList();

        public Breakpoint ToggleBreakpoint(string filePath, int line)
        {
            filePath = Normalize(filePath);
            var list = _breakpoints.GetOrAdd(filePath, _ => new List<Breakpoint>());
            lock (list)
            {
                var existing = list.FirstOrDefault(b => b.LineNumber == line);
                if (existing != null)
                {
                    list.Remove(existing);
                    OnBreakpointToggled?.Invoke(existing);
                    return existing;
                }
                var bp = new Breakpoint { FilePath = filePath, LineNumber = line };
                list.Add(bp);
                OnBreakpointToggled?.Invoke(bp);
                return bp;
            }
        }

        public bool HasBreakpoint(string filePath, int line)
        {
            filePath = Normalize(filePath);
            return _breakpoints.TryGetValue(filePath, out var list)
                   && list.Any(b => b.LineNumber == line && b.IsEnabled);
        }

        public void ClearBreakpoints(string filePath)
        {
            filePath = Normalize(filePath);
            _breakpoints.TryRemove(filePath, out _);
        }

        // ── Debug Session ─────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the CLI command and arguments for a given file path or directory.
        /// Returns (command, args, label) or null if unrecognised.
        /// </summary>
        public static (string command, string args, string label)? ResolveRunTarget(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            var ext = Path.GetExtension(path).ToLowerInvariant();
            var name = Path.GetFileName(path);

            // ── Per-extension dispatch ──────────────────────────────────────
            switch (ext)
            {
                // .NET
                case ".csproj":
                case ".sln":
                    return ("dotnet", $"run --project \"{path}\"", $"dotnet run  [{name}]");

                // Python
                case ".py":
                    var pyBin = FindOnPath("python3") != null ? "python3" : "python";
                    return (pyBin, $"\"{path}\"", $"{pyBin}  [{name}]");

                // JavaScript / TypeScript
                case ".js":
                case ".mjs":
                case ".cjs":
                    return ("node", $"\"{path}\"", $"node  [{name}]");

                case ".ts":
                    if (FindOnPath("ts-node") != null)
                        return ("ts-node", $"\"{path}\"", $"ts-node  [{name}]");
                    return ("npx", $"ts-node \"{path}\"", $"npx ts-node  [{name}]");

                // PowerShell
                case ".ps1":
                    var psBin = FindOnPath("pwsh") != null ? "pwsh" : "powershell";
                    return (psBin, $"-NoProfile -ExecutionPolicy Bypass -File \"{path}\"", $"PowerShell  [{name}]");

                // Shell script
                case ".sh":
                    return ("bash", $"\"{path}\"", $"bash  [{name}]");

                // Go (single-file run)
                case ".go":
                    return ("go", $"run \"{path}\"", $"go run  [{name}]");

                // PHP
                case ".php":
                    return ("php", $"\"{path}\"", $"php  [{name}]");

                // Ruby
                case ".rb":
                    return ("ruby", $"\"{path}\"", $"ruby  [{name}]");

                // Rust
                case ".rs":
                    return ("rustc", $"\"{path}\"", $"rustc  [{name}]");

                // Java
                case ".java":
                    var dir = Path.GetDirectoryName(path) ?? ".";
                    var cls = Path.GetFileNameWithoutExtension(path);
                    return ("cmd", $"/c javac \"{path}\" && java -cp \"{dir}\" {cls}", $"java  [{name}]");

                // Executables
                case ".exe":
                    return (path, "", $"run  [{name}]");
            }

            // ── Directory: auto-detect project type ─────────────────────────
            if (Directory.Exists(path))
            {
                return DetectDirectoryTarget(path);
            }

            return null;
        }

        private static (string command, string args, string label)? DetectDirectoryTarget(string dir)
        {
            if (!Directory.Exists(dir)) return null;

            var csproj = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly);
            if (csproj.Length > 0)
                return ("dotnet", $"run --project \"{csproj[0]}\"", $"dotnet run  [{Path.GetFileName(csproj[0])}]");

            var sln = Directory.GetFiles(dir, "*.sln", SearchOption.TopDirectoryOnly);
            if (sln.Length > 0)
                return ("dotnet", $"run --project \"{sln[0]}\"", $"dotnet run  [{Path.GetFileName(sln[0])}]");

            var pkg = Path.Combine(dir, "package.json");
            if (File.Exists(pkg))
            {
                try
                {
                    var raw = File.ReadAllText(pkg);
                    var script = raw.Contains("\"start\"") ? "start"
                               : raw.Contains("\"dev\"")   ? "run dev"
                               : "start";
                    return ("npm", script, $"npm {script}");
                }
                catch { return ("npm", "start", "npm start"); }
            }

            foreach (var entry in new[] { "main.py", "app.py", "run.py", "__main__.py" })
            {
                if (File.Exists(Path.Combine(dir, entry)))
                {
                    var pyBin = FindOnPath("python3") != null ? "python3" : "python";
                    return (pyBin, $"\"{Path.Combine(dir, entry)}\"", $"{pyBin}  [{entry}]");
                }
            }

            if (Directory.GetFiles(dir, "*.go", SearchOption.TopDirectoryOnly).Length > 0)
                return ("go", "run .", "go run .");

            if (File.Exists(Path.Combine(dir, "Cargo.toml")))
                return ("cargo", "run", "cargo run");

            var parent = Directory.GetParent(dir);
            while (parent != null && parent.Exists)
            {
                var pCsprojs = parent.GetFiles("*.csproj");
                if (pCsprojs.Length > 0)
                    return ("dotnet", $"run --project \"{pCsprojs[0].FullName}\"", $"dotnet run  [{pCsprojs[0].Name}]");

                var pSlns = parent.GetFiles("*.sln");
                if (pSlns.Length > 0)
                    return ("dotnet", $"run --project \"{pSlns[0].FullName}\"", $"dotnet run  [{pSlns[0].Name}]");

                parent = parent.Parent;
            }

            var exes = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
            var targetExe = exes.FirstOrDefault(f => !f.EndsWith(".vshost.exe", StringComparison.OrdinalIgnoreCase));
            if (targetExe != null)
                return (targetExe, "", $"run  [{Path.GetFileName(targetExe)}]");

            return null;
        }

        private static string? FindOnPath(string bin)
        {
            var pathVar = System.Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathVar.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var full = Path.Combine(dir.Trim(), bin);
                if (File.Exists(full)) return full;
                if (File.Exists(full + ".exe")) return full + ".exe";
            }
            return null;
        }

        public async Task StartAsync(string projectPath, string workingDirectory)
        {
            if (State == DebugSessionState.Running) return;

            _cts = new CancellationTokenSource();
            State = DebugSessionState.Running;

            var resolved = ResolveRunTarget(projectPath);
            string command, args;

            if (resolved.HasValue)
            {
                command = resolved.Value.command;
                args    = resolved.Value.args;
            }
            else
            {
                command = "cmd";
                args    = $"/c \"{projectPath}\"";
            }

            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                _debugProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _debugProcess.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        OnDebugOutput?.Invoke(e.Data, false);
                        CheckOutputForBreakpoints(e.Data);
                    }
                };
                _debugProcess.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        OnDebugOutput?.Invoke(e.Data, true);
                        CheckOutputForBreakpoints(e.Data);
                    }
                };
                _debugProcess.Exited += (s, e) =>
                {
                    State = DebugSessionState.Stopped;
                    OnDebugOutput?.Invoke($"\n[Process exited with code {_debugProcess.ExitCode}]", false);
                };

                _debugProcess.Start();
                _debugProcess.BeginOutputReadLine();
                _debugProcess.BeginErrorReadLine();

                OnDebugOutput?.Invoke($"[Debug] Started: {command} {args}", false);

                await Task.Run(async () =>
                {
                    try
                    {
                        await _debugProcess.WaitForExitAsync(_cts.Token);
                    }
                    catch (OperationCanceledException) { }
                });
            }
            catch (Exception ex)
            {
                OnDebugOutput?.Invoke($"[Debug Error] {ex.Message}", true);
                State = DebugSessionState.Stopped;
            }
        }

        private void CheckOutputForBreakpoints(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            var breakpoints = GetAllBreakpoints().Where(b => b.IsEnabled).ToList();
            if (breakpoints.Count == 0) return;

            foreach (var bp in breakpoints)
            {
                string fileName = Path.GetFileName(bp.FilePath);
                if (string.IsNullOrEmpty(fileName)) continue;

                bool fileMatch = line.Contains(fileName, StringComparison.OrdinalIgnoreCase) ||
                                 (!string.IsNullOrEmpty(bp.FilePath) && line.Contains(bp.FilePath, StringComparison.OrdinalIgnoreCase));

                if (fileMatch)
                {
                    string lineStr = bp.LineNumber.ToString();
                    bool lineMatch = line.Contains($":line {lineStr}", StringComparison.OrdinalIgnoreCase) ||
                                     line.Contains($":{lineStr}", StringComparison.OrdinalIgnoreCase) ||
                                     line.Contains($"({lineStr})", StringComparison.OrdinalIgnoreCase) ||
                                     line.Contains($", line {lineStr}", StringComparison.OrdinalIgnoreCase) ||
                                     line.Contains($"line {lineStr}", StringComparison.OrdinalIgnoreCase) ||
                                     line.Contains($"L{lineStr}", StringComparison.OrdinalIgnoreCase);

                    if (lineMatch)
                    {
                        State = DebugSessionState.Paused;
                        OnDebugOutput?.Invoke($"\n🛑 [BREAKPOINT HIT] Paused at {fileName}:{bp.LineNumber}", true);
                        OnBreakpointHit?.Invoke(bp);
                        break;
                    }
                }
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            try
            {
                if (_debugProcess != null && !_debugProcess.HasExited)
                {
                    _debugProcess.Kill(entireProcessTree: true);
                }
            }
            catch { }
            State = DebugSessionState.Stopped;
        }

        public void Pause()
        {
            if (State == DebugSessionState.Running)
            {
                State = DebugSessionState.Paused;
                OnDebugOutput?.Invoke("[Debug] Execution paused.", false);
            }
        }

        public void Resume()
        {
            if (State == DebugSessionState.Paused)
            {
                State = DebugSessionState.Running;
                OnDebugOutput?.Invoke("[Debug] Execution resumed.", false);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string Normalize(string path) =>
            Path.GetFullPath(path).ToLowerInvariant();

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            _debugProcess?.Dispose();
        }
    }
}
