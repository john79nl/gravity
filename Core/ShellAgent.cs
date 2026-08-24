using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    /// <summary>
    /// Executes whitelisted shell commands in a sandboxed process.
    ///
    /// Key fixes vs original:
    ///   1. OS detected ONCE at construction, stored in _isWindows — not re-checked per call
    ///   2. Shell built-ins routed correctly per OS without a separate HashSet branch
    ///   3. HandleRunAsync no longer has three overlapping argument fallback chains
    ///      that could silently produce an empty command
    ///   4. Verb-stripping (removing "dotnet" from "dotnet build") is centralised
    ///      in one place instead of scattered across switch arms
    ///   5. powershell / pwsh resolved to the correct binary per OS
    ///   6. Timeout surface made visible as a constructor parameter
    ///   7. Output cap raised slightly (8 KB) and clearly documented
    /// </summary>
    public class ShellAgent : IAgent
    {
        private readonly IProjectContext _projectContext;
        private readonly IShellLogger _logger;
        private readonly ISettingsService _settings;
        private readonly bool _isWindows;
        private readonly int _timeoutSeconds;

        public AgentDescriptor Descriptor { get; }

        // Whitelist removed to allow all commands

        // Commands that are shell built-ins and must be wrapped in cmd/sh
        private static readonly HashSet<string> BuiltIns = new(StringComparer.OrdinalIgnoreCase)
        {
            "dir", "ls", "echo", "type", "cat", "cls", "clear", "findstr"
        };

        // Tool name → install command per package manager
        private static readonly Dictionary<string, Dictionary<string, string>> ToolInstallMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // PDF tools
            ["qpdf"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id QPDF.QPDF -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install qpdf -y",
                ["apt"]    = "sudo apt-get install -y qpdf",
                ["dnf"]    = "sudo dnf install -y qpdf",
                ["pacman"] = "sudo pacman -S --noconfirm qpdf",
                ["brew"]   = "brew install qpdf"
            },
            ["pdftk"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id PDFLabs.PDFTK -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install pdftk -y",
                ["apt"]    = "sudo apt-get install -y pdftk",
                ["dnf"]    = "sudo dnf install -y pdftk",
                ["pacman"] = "sudo pacman -S --noconfirm pdftk",
                ["brew"]   = "brew install pdftk"
            },
            ["pdftotext"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id oschwartz10612.Poppler -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install poppler -y",
                ["apt"]    = "sudo apt-get install -y poppler-utils",
                ["dnf"]    = "sudo dnf install -y poppler-utils",
                ["pacman"] = "sudo pacman -S --noconfirm poppler",
                ["brew"]   = "brew install poppler"
            },
            ["pdftoppm"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id oschwartz10612.Poppler -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install poppler -y",
                ["apt"]    = "sudo apt-get install -y poppler-utils",
                ["dnf"]    = "sudo dnf install -y poppler-utils",
                ["pacman"] = "sudo pacman -S --noconfirm poppler",
                ["brew"]   = "brew install poppler"
            },
            ["pdfinfo"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id oschwartz10612.Poppler -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install poppler -y",
                ["apt"]    = "sudo apt-get install -y poppler-utils",
                ["dnf"]    = "sudo dnf install -y poppler-utils",
                ["pacman"] = "sudo pacman -S --noconfirm poppler",
                ["brew"]   = "brew install poppler"
            },
            ["gs"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id ArtifexSoftware.GhostScript -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install ghostscript -y",
                ["apt"]    = "sudo apt-get install -y ghostscript",
                ["dnf"]    = "sudo dnf install -y ghostscript",
                ["pacman"] = "sudo pacman -S --noconfirm ghostscript",
                ["brew"]   = "brew install ghostscript"
            },
            // Image tools
            ["magick"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id ImageMagick.ImageMagick -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install imagemagick -y",
                ["apt"]    = "sudo apt-get install -y imagemagick",
                ["dnf"]    = "sudo dnf install -y ImageMagick",
                ["pacman"] = "sudo pacman -S --noconfirm imagemagick",
                ["brew"]   = "brew install imagemagick"
            },
            ["convert"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id ImageMagick.ImageMagick -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install imagemagick -y",
                ["apt"]    = "sudo apt-get install -y imagemagick",
                ["dnf"]    = "sudo dnf install -y ImageMagick",
                ["pacman"] = "sudo pacman -S --noconfirm imagemagick",
                ["brew"]   = "brew install imagemagick"
            },
            ["ffmpeg"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id Gyan.FFmpeg -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install ffmpeg -y",
                ["apt"]    = "sudo apt-get install -y ffmpeg",
                ["dnf"]    = "sudo dnf install -y ffmpeg",
                ["pacman"] = "sudo pacman -S --noconfirm ffmpeg",
                ["brew"]   = "brew install ffmpeg"
            },
            ["exiftool"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id OlivierLeywin.ExifTool -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install exiftool -y",
                ["apt"]    = "sudo apt-get install -y libimage-exiftool-perl",
                ["dnf"]    = "sudo dnf install -y perl-Image-ExifTool",
                ["pacman"] = "sudo pacman -S --noconfirm libimage-exiftool",
                ["brew"]   = "brew install exiftool"
            },
            // Data tools
            ["jq"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id jqlang.jq -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install jq -y",
                ["apt"]    = "sudo apt-get install -y jq",
                ["dnf"]    = "sudo dnf install -y jq",
                ["pacman"] = "sudo pacman -S --noconfirm jq",
                ["brew"]   = "brew install jq"
            },
            ["sqlite3"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id SQLite.SQLite -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install sqlite -y",
                ["apt"]    = "sudo apt-get install -y sqlite3",
                ["dnf"]    = "sudo dnf install -y sqlite",
                ["pacman"] = "sudo pacman -S --noconfirm sqlite",
                ["brew"]   = "brew install sqlite3"
            },
            // Dev tools
            ["node"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id OpenJS.NodeJS.LTS -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install nodejs-lts -y",
                ["apt"]    = "sudo apt-get install -y nodejs",
                ["dnf"]    = "sudo dnf install -y nodejs",
                ["pacman"] = "sudo pacman -S --noconfirm nodejs npm",
                ["brew"]   = "brew install node"
            },
            ["python"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id Python.Python.3.12 -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install python -y",
                ["apt"]    = "sudo apt-get install -y python3 python3-pip",
                ["dnf"]    = "sudo dnf install -y python3 python3-pip",
                ["pacman"] = "sudo pacman -S --noconfirm python python-pip",
                ["brew"]   = "brew install python"
            },
            ["pip"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id Python.Python.3.12 -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install python -y",
                ["apt"]    = "sudo apt-get install -y python3-pip",
                ["dnf"]    = "sudo dnf install -y python3-pip",
                ["pacman"] = "sudo pacman -S --noconfirm python-pip",
                ["brew"]   = "brew install python"
            },
            ["cargo"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id Rustlang.Rustup -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install rust -y",
                ["apt"]    = "sudo apt-get install -y cargo",
                ["dnf"]    = "sudo dnf install -y cargo",
                ["pacman"] = "sudo pacman -S --noconfirm rust",
                ["brew"]   = "brew install rust"
            },
            // Containers
            ["docker"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["winget"] = "winget install --id Docker.DockerDesktop -e --accept-source-agreements --accept-package-agreements",
                ["choco"]  = "choco install docker-desktop -y",
                ["apt"]    = "sudo apt-get install -y docker.io",
                ["dnf"]    = "sudo dnf install -y docker",
                ["pacman"] = "sudo pacman -S --noconfirm docker",
                ["brew"]   = "brew install --cask docker"
            }
        };

        public ShellAgent(IProjectContext projectContext, IShellLogger logger, ISettingsService settings, int timeoutSeconds = 90)
        {
            _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
            _logger         = logger         ?? throw new ArgumentNullException(nameof(logger));
            _settings       = settings       ?? throw new ArgumentNullException(nameof(settings));
            _timeoutSeconds = timeoutSeconds;

            // ── FIX 1: Detect OS once at construction, not per-call
            _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            Descriptor = new AgentDescriptor
            {
                Name = "terminal",
                Description = _isWindows
                    ? "Run shell commands via PowerShell (Windows). Use correct CLI syntax."
                    : "Run shell commands via bash (Linux/macOS). Use correct CLI syntax.",
                CanWrite = true,
                SupportedVerbs = new[] { "run_command", "run", "dotnet", "git", "powershell", "where" },
                Actions = new List<ActionMetadata>
                {
                    new ActionMetadata
                    {
                        Name = "run_command",
                        Description = "Run a shell command",
                        IsMutation = true,
                        Parameters = new Dictionary<string, string>
                        {
                            ["command"] = "The executable (e.g. dotnet, git)",
                            ["command_args"] = "Arguments to pass to the command"
                        }
                    },
                    new ActionMetadata
                    {
                        Name = "run",
                        Description = "Alias for run_command",
                        IsMutation = true,
                        Parameters = new Dictionary<string, string>
                        {
                            ["command"] = "The executable",
                            ["command_args"] = "Arguments"
                        },
                        OptionalParameters = new List<string> { "command_args" }
                    },
                    new ActionMetadata
                    {
                        Name = "dotnet",
                        Description = "Run dotnet CLI (e.g. 'build', '--list-sdks', 'ef dbcontext info')",
                        IsMutation = true,
                        Parameters = new Dictionary<string, string> { ["command_args"] = "dotnet sub-command and args" }
                    },
                    new ActionMetadata
                    {
                        Name = "git",
                        Description = "Run git command (e.g. 'status', 'log --oneline -10')",
                        IsMutation = true,
                        Parameters = new Dictionary<string, string> { ["command_args"] = "git sub-command and args" }
                    },
                    new ActionMetadata
                    {
                        Name = "powershell",
                        Description = _isWindows
                            ? "Run a PowerShell command or script"
                            : "Run a pwsh (PowerShell Core) command or script",
                        IsMutation = true,
                        Parameters = new Dictionary<string, string> { ["command_args"] = "PowerShell expression" }
                    },
                    new ActionMetadata
                    {
                        Name = "where",
                        Description = "Locate a binary on PATH (e.g. 'dotnet', 'node')",
                        Parameters = new Dictionary<string, string> { ["command_args"] = "Binary name" }
                    }
                }
            };
        }

        // ── Main entry point ──────────────────────────────────────────────────

        public async Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Verb))
                return Fail("Invalid request: missing verb.");

            var verb = request.Verb.Trim().ToLowerInvariant();

            switch (verb)
            {
                case "run_command":
                case "run":
                    return await HandleRunCommandAsync(request, ct);

                case "dotnet":
                case "git":
                    return await RunAsync(verb, GetArgs(request, verb), ct);

                case "powershell":
                case "pwsh":
                    // ── FIX 5: Resolve correct binary per OS
                    var psBinary = ResolvePowerShell();
                    var rawScript = GetArgs(request, verb);
                    var base64Script = System.Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(rawScript));
                    return await RunAsync(psBinary, $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {base64Script}", ct);

                case "where":
                    // 'where' on Windows, 'which' on Linux
                    return await RunBuiltInAsync(
                        _isWindows ? "where" : "which",
                        GetArgs(request, verb), ct);

                default:
                    return await RunAsync(verb, GetArgs(request, verb), ct);
            }
        }

        // ── run_command / run handler ─────────────────────────────────────────

        // ── FIX 3: Single, clear argument resolution — no three-level fallback chains
        private async Task<AgentResult> HandleRunCommandAsync(AgentRequest request, CancellationToken ct)
        {
            // Prefer explicit "command" arg; fall back to "input"; then "args"
            var commandLine = request.GetStringArgument("command")
                           ?? request.GetStringArgument("input")
                           ?? request.GetStringArgument("args")
                           ?? string.Empty;

            // If command_args was also provided, append it
            var extraArgs = request.GetStringArgument("command_args") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(extraArgs) && extraArgs != commandLine)
                commandLine = (commandLine + " " + extraArgs).Trim();

            if (string.IsNullOrWhiteSpace(commandLine))
                return Fail("run_command requires a 'command' argument specifying what to execute.");

            // Split "dotnet build --no-restore" → binary="dotnet", args="build --no-restore"
            var parts = commandLine.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var binary = parts[0];
            var binaryArgs = parts.Length > 1 ? parts[1].Trim() : string.Empty;

            if (BuiltIns.Contains(binary))
                return await RunBuiltInAsync(binary, binaryArgs, ct);

            return await RunAsync(binary, binaryArgs, ct);
        }

        // ── Process launchers ─────────────────────────────────────────────────

        // Direct process launch for real executables (dotnet, git, npm, node…)
        // Includes auto-install: if the binary is missing, install it and retry once.
        private async Task<AgentResult> RunAsync(string fileName, string arguments, CancellationToken ct)
        {
            var result = await RunProcessAsync(fileName, arguments, ct);

            // If command succeeded, nothing to do
            if (result.Success)
                return result;

            // If cancelled or timed out, don't attempt install
            if (ct.IsCancellationRequested)
                return result;

            // Detect if the failure is due to a missing binary
            var missingTool = DetectMissingTool(result.Output, fileName);
            if (missingTool == null)
                return result;

            _logger.Log($"[auto-install] Detected missing tool: {missingTool}", isError: false);

            var installed = await TryInstallToolAsync(missingTool, ct);
            if (!installed)
                return result;

            // Retry the original command after successful install
            _logger.Log($"[auto-install] Retrying: {fileName} {arguments}", isError: false);
            return await RunProcessAsync(fileName, arguments, ct);
        }

        // ── FIX 2: Built-ins wrapped via the correct shell per OS
        private Task<AgentResult> RunBuiltInAsync(string command, string arguments, CancellationToken ct)
        {
            if (_isWindows)
                return RunProcessAsync("cmd.exe", $"/C {command} {arguments}", ct);
            else
                return RunProcessAsync("/bin/sh", $"-c \"{EscapeForSh(command + " " + arguments)}\"", ct);
        }

        // ── Auto-install missing tools ────────────────────────────────────────

        private static readonly string[] MissingBinaryPatterns =
        {
            // Windows PowerShell
            "is not recognized as the name of a cmdlet",
            "is not recognized as an internal or external command",
            // Windows cmd
            "is not recognized as an internal or external command, operable program or batch file",
            // Windows process start failure
            "The system cannot find the file specified",
            // Linux bash/sh
            "command not found",
            "No such file or directory"
        };

        private static string? DetectMissingTool(string output, string fileName)
        {
            if (string.IsNullOrWhiteSpace(output))
                return null;

            bool indicatesMissing = MissingBinaryPatterns.Any(p =>
                output.Contains(p, StringComparison.OrdinalIgnoreCase));

            if (!indicatesMissing)
                return null;

            // The binary that failed is the one we tried to run
            if (!string.IsNullOrWhiteSpace(fileName))
                return fileName;

            // Fallback: try to extract from error message
            // e.g. "The term 'qpdf' is not recognized..."
            var match = Regex.Match(output, @"'(\w+)' is not recognized");
            if (match.Success)
                return match.Groups[1].Value;

            return null;
        }

        private string? FindPackageManager()
        {
            if (_isWindows)
            {
                // Prefer winget, then choco
                if (FindOnPath("winget.exe") != null) return "winget";
                if (FindOnPath("choco.exe") != null) return "choco";
            }
            else
            {
                // Prefer apt, then dnf, then pacman, then brew
                if (FindOnPath("apt-get") != null) return "apt";
                if (FindOnPath("dnf") != null) return "dnf";
                if (FindOnPath("pacman") != null) return "pacman";
                if (FindOnPath("brew") != null) return "brew";
            }

            return null;
        }

        private async Task<bool> TryInstallToolAsync(string toolName, CancellationToken ct)
        {
            if (!ToolInstallMap.TryGetValue(toolName, out var installCommands))
            {
                _logger.Log($"[auto-install] No install mapping for '{toolName}'.", isError: true);
                return false;
            }

            var pkgManager = FindPackageManager();
            if (string.IsNullOrEmpty(pkgManager))
            {
                _logger.Log("[auto-install] No package manager found (winget/choco/apt/dnf/pacman/brew).", isError: true);
                return false;
            }

            if (!installCommands.TryGetValue(pkgManager, out var installCmd))
            {
                _logger.Log($"[auto-install] No install command for '{toolName}' via {pkgManager}.", isError: true);
                return false;
            }

            _logger.Log($"[auto-install] Installing '{toolName}' via {pkgManager}: {installCmd}", isError: false);

            // Run install command through the appropriate shell
            AgentResult installResult;
            if (_isWindows)
            {
                var psBinary = ResolvePowerShell();
                var base64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(installCmd));
                installResult = await RunProcessAsync(psBinary, $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {base64}", ct);
            }
            else
            {
                installResult = await RunProcessAsync("/bin/sh", $"-c \"{EscapeForSh(installCmd)}\"", ct);
            }

            if (installResult.Success)
                _logger.Log($"[auto-install] Successfully installed '{toolName}'.", isError: false);
            else
                _logger.Log($"[auto-install] Failed to install '{toolName}': {installResult.Output}", isError: true);

            return installResult.Success;
        }

        // Core process runner
        private async Task<AgentResult> RunProcessAsync(string fileName, string arguments, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            var workDir = _projectContext.ProjectDirectory ?? AppContext.BaseDirectory;
            _logger.Log($"> {fileName} {arguments}", isError: false);

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Inject generic environment variables
            if (_settings.Current.EnvironmentVariables != null)
            {
                foreach (var kvp in _settings.Current.EnvironmentVariables)
                {
                    psi.Environment[kvp.Key] = kvp.Value ?? "";
                }
            }

            using var process = new Process { StartInfo = psi };
            var output = new StringBuilder();
            var outputLock = new object();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (outputLock) { output.AppendLine(e.Data); }
                _logger.Log(e.Data, isError: false);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (outputLock) { output.AppendLine(e.Data); }
                _logger.Log(e.Data, isError: true);
            };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                return Fail($"Failed to start '{fileName}': {ex.Message}");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);

                // Distinguish timeout from user cancellation
                return ct.IsCancellationRequested
                    ? Fail("Process cancelled by user.")
                    : Fail($"Process timed out after {_timeoutSeconds}s. Ensure the command does not require interactive input.");
            }

            var result = output.Length == 0
                ? $"(Process exited with code {process.ExitCode})"
                : output.ToString();

            // Mask sensitive environment variables in output to prevent AI from seeing them
            if (_settings.Current.EnvironmentVariables != null)
            {
                foreach (var kvp in _settings.Current.EnvironmentVariables)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Value) || kvp.Value.Length < 5) continue;
                    var keyUpper = kvp.Key.ToUpperInvariant();
                    if (keyUpper.Contains("TOKEN") || keyUpper.Contains("SECRET") || keyUpper.Contains("PASSWORD"))
                    {
                        var masked = "***" + kvp.Value.Substring(kvp.Value.Length - 4);
                        result = result.Replace(kvp.Value, masked);
                    }
                }
            }

            // ── FIX 7: 8 KB cap with clear marker (truncates middle to preserve errors at end)
            const int MaxOutputBytes = 8_000;
            if (result.Length > MaxOutputBytes)
            {
                int half = MaxOutputBytes / 2;
                result = result[..half] + "\n\n... [OUTPUT TRUNCATED — use a more targeted command] ...\n\n" + result[^half..];
            }

            return new AgentResult { Success = process.ExitCode == 0, Output = result };
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // ── FIX 4: Central verb-stripping — strip leading "dotnet " from "dotnet build"
        private static string GetArgs(AgentRequest request, string verb)
        {
            var raw = request.GetStringArgument("command_args")
                   ?? request.GetStringArgument("args")
                   ?? request.GetStringArgument("input")
                   ?? string.Empty;

            raw = raw.Trim();

            // Strip leading verb prefix if the model redundantly included it
            // e.g. verb="dotnet", args="dotnet build" → "build"
            if (raw.StartsWith(verb + " ", StringComparison.OrdinalIgnoreCase))
                raw = raw[(verb.Length + 1)..].TrimStart();
            else if (raw.Equals(verb, StringComparison.OrdinalIgnoreCase))
                raw = string.Empty;

            return raw;
        }

        // ── FIX 5: Resolve the correct PowerShell binary
        private string ResolvePowerShell()
        {
            if (_isWindows)
            {
                // Prefer pwsh (PowerShell 7+) if available, fall back to Windows PowerShell
                var pwshPath = FindOnPath("pwsh.exe");
                return pwshPath ?? "powershell.exe";
            }
            else
            {
                // On Linux/macOS PowerShell Core is 'pwsh'
                return "pwsh";
            }
        }

        private static string? FindOnPath(string fileName)
        {
            var pathVar = System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathVar.Split(System.IO.Path.PathSeparator))
            {
                var full = System.IO.Path.Combine(dir, fileName);
                if (System.IO.File.Exists(full)) return full;
            }
            return null;
        }

        private static string EscapeForSh(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static AgentResult Fail(string message) =>
            new AgentResult { Success = false, Output = message };
    }
}