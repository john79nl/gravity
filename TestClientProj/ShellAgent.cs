using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    public class ShellAgent : IAgent
    {
        private readonly IProjectContext _projectContext;
        private readonly IShellLogger _logger;

        public AgentDescriptor Descriptor { get; }



        public ShellAgent(IProjectContext projectContext, IShellLogger logger)
        {
            _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            Descriptor = new AgentDescriptor
            {
                Name = "terminal",
                Description = "Run system commands. CRITICAL: Use correct CLI syntax (e.g., 'dotnet --list-sdks' NOT 'dotnet list-sdks'). Use 'dir' to list files.",
                CanWrite = true,
                SupportedVerbs = new[] { "run_command", "dotnet", "git", "dir", "powershell", "where" },
                Actions = new List<ActionMetadata>
                {
                    new ActionMetadata { Name = "run_command", Description = "Run a shell command", IsMutation = true, Parameters = new Dictionary<string, string> { ["command"] = "The command", ["command_args"] = "Arguments for the command" } },
                    new ActionMetadata { Name = "run", Description = "Legacy alias for run_command", IsMutation = true, Parameters = new Dictionary<string, string> { ["command"] = "The command", ["command_args"] = "Arguments for the command" }, OptionalParameters = new List<string> { "command", "command_args" } },
                    new ActionMetadata { Name = "dotnet", Description = "Run dotnet CLI (e.g. '--list-sdks', 'build')", IsMutation = true, Parameters = new Dictionary<string, string> { ["command_args"] = "Command args" } },
                    new ActionMetadata { Name = "git", Description = "Run git command", IsMutation = true, Parameters = new Dictionary<string, string> { ["command_args"] = "Command args (e.g. status, log)" } },
                    new ActionMetadata { Name = "powershell", Description = "Run PowerShell command", IsMutation = true, Parameters = new Dictionary<string, string> { ["command_args"] = "PowerShell script or command" } },
                    new ActionMetadata { Name = "where", Description = "Check path of a command (e.g. 'where dotnet')", Parameters = new Dictionary<string, string> { ["command_args"] = "Binary name" } }
                }
            };
        }

        public async Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Verb))
                return new AgentResult { Success = false, Output = "Invalid request." };

            var args = request.GetStringArgument("command_args", request.GetStringArgument("args", request.GetStringArgument("input", "")));

            switch (request.Verb.ToLowerInvariant())
            {
                case "run_command":
                case "run":
                    return await HandleRunAsync(request, ct);
                case "dotnet":
                    return await RunProcessAsync("dotnet", args, ct);
                case "git":
                    return await RunProcessAsync("git", args, ct);
                case "powershell":
                case "pwsh":
                    return await RunProcessAsync(request.Verb, args, ct);
                default:
                    return await RunProcessAsync(request.Verb, args, ct);
            }
        }

        private async Task<AgentResult> HandleRunAsync(AgentRequest request, CancellationToken ct)
        {
            var command = request.GetStringArgument("command", request.Verb);
            if (command == "run" || command == "run_command") command = request.GetStringArgument("input", "");

            if (string.IsNullOrWhiteSpace(command))
            {
                var fallbackCmd = request.GetStringArgument("args");
                if (!string.IsNullOrWhiteSpace(fallbackCmd)) command = fallbackCmd;
            }

            if (string.IsNullOrWhiteSpace(command))
                return new AgentResult { Success = false, Output = "Missing 'command' argument." };

            var args = request.GetStringArgument("command_args", request.GetStringArgument("args", ""));
            
            string fullLine = command;
            if (!string.IsNullOrWhiteSpace(args) && args != command && args != request.GetStringArgument("input", ""))
            {
                fullLine += " " + args;
            }

            fullLine = fullLine.Trim();
            var parts = fullLine.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var baseCommand = parts.Length > 0 ? parts[0] : fullLine;
            var finalArgs = parts.Length > 1 ? parts[1].Trim() : "";

                return await RunProcessAsync(baseCommand, finalArgs, ct);
        }

        private async Task<AgentResult> RunProcessAsync(string fileName, string arguments, CancellationToken ct)
        {
            try
            {
                var workDir = _projectContext.ProjectDirectory ?? AppContext.BaseDirectory;
                
                // Handle shell built-ins on Windows (dir, echo, etc.)
                var finalFileName = fileName;
                var finalArgs = arguments;
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    var builtIns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dir", "echo", "type", "cls", "set", "ver" };
                    if (builtIns.Contains(fileName))
                    {
                        finalFileName = "cmd.exe";
                        finalArgs = $"/C {fileName} {arguments}";
                    }
                }

                _logger.Log($"> {finalFileName} {finalArgs}", false);

                var psi = new ProcessStartInfo
                {
                    FileName = finalFileName,
                    Arguments = finalArgs,
                    WorkingDirectory = workDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                var output = new System.Text.StringBuilder();

                process.OutputDataReceived += (s, e) => 
                { 
                    if (e.Data != null) 
                    {
                        output.AppendLine(e.Data);
                        _logger.Log(e.Data, false);
                    }
                };
                process.ErrorDataReceived += (s, e) => 
                { 
                    if (e.Data != null) 
                    {
                        output.AppendLine(e.Data);
                        _logger.Log(e.Data, true);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(ct);

                var outputStr = output.Length == 0 ? $"(Process exited with code {process.ExitCode})" : output.ToString();
                if (outputStr.Length > 4000) {
                    outputStr = outputStr.Substring(0, 4000) + "\n\n... [OUTPUT TRUNCATED: Result too large for AI context. Use more specific commands or grep/findstr] ...";
                }

                return new AgentResult 
                { 
                    Success = process.ExitCode == 0, 
                    Output = outputStr 
                };
            }
            catch (Exception ex)
            {
                _logger.Log($"Error: {ex.Message}", true);
                return new AgentResult { Success = false, Output = $"Failed to execute {fileName}: {ex.Message}" };
            }
        }
    }
}
