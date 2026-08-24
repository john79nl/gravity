using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Gravity.Core.Agents;
namespace Gravity.Core
{
    public class DynamicAgent : IAgent
    {
        private readonly AgentDescriptor _descriptor;
        private readonly DynamicAgentDefinition _definition;
        private readonly IModelClient _model;
        private readonly ReasoningRouter _router;
        private readonly ISettingsService _settings;

        public DynamicAgent(DynamicAgentDefinition def, IModelClient model, ReasoningRouter router, ISettingsService settings)
        {
            _definition = def;
            _model = model;
            _router = router;
            _settings = settings;

            var verbs = def.Verbs ?? new List<string> { def.Name };
            _descriptor = new AgentDescriptor
            {
                Name = def.Name,
                Description = def.Description,
                CanWrite = false,
                SupportedVerbs = verbs.ToArray(),
                Actions = verbs.Select(v => new ActionMetadata
                {
                    Name = v,
                    Description = $"Execute {def.Name}:{v}",
                    IsMutation = false,
                    Parameters = new(),
                    OptionalParameters = new List<string> { "input", "args" }
                }).ToList()
            };
        }

        public DynamicAgentDefinition Definition => _definition;
        public AgentDescriptor Descriptor => _descriptor;

        public async Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken ct)
        {
            try
            {
                if (_definition.Scripts != null && _definition.Scripts.TryGetValue(request.Verb, out var scriptPath))
                {
                    return await ExecuteNativeScriptAsync(scriptPath, request.ArgMap, ct);
                }

                var userPrompt = $"Task: {request.Verb}";
                if (request.ArgMap.Count > 0)
                    userPrompt += "\n" + JsonSerializer.Serialize(request.ArgMap);

                var sysPrompt = _definition.System;
                sysPrompt += "\n\nCRITICAL OUTPUT RULE:\nAlways finish by calling action_final (or your final tool). Your final summary must be highly professional, structured, and polished. Use clear Markdown formatting (like bullet points, bold text, or clean tables) to present data gracefully. Do NOT dump raw JSON or messy data to the user. Maintain a courteous, formal, and helpful tone.";

            var messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "system", Content = sysPrompt },
                    new ChatMessage { Role = "user", Content = userPrompt }
                };

                var maxSteps = _definition.MaxSteps;
                for (int step = 1; step <= maxSteps; step++)
                {
                    ct.ThrowIfCancellationRequested();

                    var sb = new System.Text.StringBuilder();
                    var progress = new SynchronousProgress<string>(t => sb.Append(t));
                    var availableAgents = _router.GetAgentDescriptors()
                        .Where(a => !string.Equals(a.Name, _definition.Name, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var res = await _model.StreamResponseAsync(messages, progress, ct, null, ModelRole.Reasoning, availableAgents);

                    if (res.ToolCalls.Count == 0 && !string.IsNullOrWhiteSpace(res.Content))
                    {
                        var parsed = PlanParser.TryParseToolCallsFromContent(res.Content);
                        if (parsed.Count > 0)
                        {
                            res.ToolCalls.AddRange(parsed);
                            res.Content = string.Empty;
                        }
                    }

                    if (res.ToolCalls.Count > 0)
                    {
                        messages.Add(new ChatMessage { Role = "assistant", Content = res.Content ?? "", ToolCalls = res.ToolCalls });

                        foreach (var tc in res.ToolCalls)
                        {
                            var tcName = tc.Function.Name;
                            if (string.Equals(tcName, "action-final", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(tcName, "action_final", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(tcName, "action.final", StringComparison.OrdinalIgnoreCase))
                            {
                                var outMsg = tc.Function.Arguments;
                                try
                                {
                                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(tc.Function.Arguments, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                    if (dict != null && dict.TryGetValue("output", out var o) && o != null)
                                        outMsg = o.ToString() ?? "";
                                }
                                catch { }
                                return new AgentResult { Success = true, Output = outMsg };
                            }

                            var result = await ExecuteToolCall(tc, ct);
                            messages.Add(new ChatMessage
                            {
                                Role = "tool",
                                ToolCallId = tc.Id,
                                Name = tcName,
                                Content = TruncateOutput(result.Output ?? "Success")
                            });
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(res.Content))
                    {
                        return new AgentResult { Success = true, Output = res.Content };
                    }
                }

                return new AgentResult { Success = false, Output = "Max steps reached without conclusive answer." };
            }
            catch (Exception ex)
            {
                return new AgentResult { Success = false, Output = $"DynamicAgent error: {ex.Message}" };
            }
        }

        private async Task<AgentResult> ExecuteNativeScriptAsync(string scriptPath, Dictionary<string, object> args, CancellationToken ct)
        {
            var fullPath = System.IO.Path.GetFullPath(scriptPath);
            if (!System.IO.File.Exists(fullPath))
                return new AgentResult { Success = false, Output = $"Script not found: {fullPath}" };

            var argsJson = JsonSerializer.Serialize(args);
            var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            var binary = isWindows ? "powershell.exe" : "pwsh";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = binary,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{fullPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            psi.Environment["GRAVITY_TOOL_ARGS"] = argsJson;

            // Optional: Inject global settings environment variables if needed
            if (_settings.Current.EnvironmentVariables != null)
            {
                foreach (var kvp in _settings.Current.EnvironmentVariables)
                {
                    psi.Environment[kvp.Key] = kvp.Value ?? "";
                }
            }

            using var process = new System.Diagnostics.Process { StartInfo = psi };
            var output = new System.Text.StringBuilder();
            var outputLock = new object();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (outputLock) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (outputLock) output.AppendLine(e.Data); };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(ct);
                
                var res = output.ToString();
                return new AgentResult { Success = process.ExitCode == 0, Output = string.IsNullOrWhiteSpace(res) ? $"(Exited with code {process.ExitCode})" : res };
            }
            catch (Exception ex)
            {
                return new AgentResult { Success = false, Output = $"Failed to execute script: {ex.Message}" };
            }
        }

        private async Task<AgentResult> ExecuteToolCall(ToolCall tc, CancellationToken ct)
        {
            var tcName = tc.Function.Name;
            var tcArgs = tc.Function.Arguments;

            // Direct Script execution intercept
            var rawVerb = tcName;
            if (rawVerb.Contains('.')) rawVerb = rawVerb.Substring(rawVerb.IndexOf('.') + 1);

            if (_definition.Scripts != null && _definition.Scripts.TryGetValue(rawVerb, out var scriptPath))
            {
                var dictArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(tcArgs, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                return await ExecuteNativeScriptAsync(scriptPath, dictArgs, ct);
            }

            // Parse "agent.verb" tool naming (e.g. "gravity.about", "file.search")
            var dotIdx = tcName.IndexOf('.');
            var agentName = dotIdx > 0 ? tcName.Substring(0, dotIdx) : tcName;
            var operation = dotIdx > 0 ? tcName.Substring(dotIdx + 1) : "";


            if (!string.IsNullOrEmpty(agentName))
            {
                try
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(tcArgs, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                    if (string.IsNullOrEmpty(operation))
                        operation = dict.TryGetValue("operation", out var o) ? o?.ToString() ?? "" : "";

                    var routerPayload = JsonSerializer.Serialize(new { Agent = agentName, Request = new { operation, arguments = dict } });
                    return await _router.RouteAsync(routerPayload, ct);
                }
                catch (Exception ex)
                {
                    return new AgentResult { Success = false, Output = $"Tool call error: {ex.Message}" };
                }
            }

            if (string.Equals(tcName, "action-final", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tcName, "action_final", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tcName, "action.final", StringComparison.OrdinalIgnoreCase))
            {
                return new AgentResult { Success = true, Output = tcArgs };
            }

            return new AgentResult { Success = false, Output = $"Unknown tool: {tcName}" };
        }



        private static string TruncateOutput(string output, int maxLen = 2000)
        {
            if (string.IsNullOrEmpty(output) || output.Length <= maxLen) return output;
            int half = maxLen / 2;
            return string.Concat(output.AsSpan(0, half), "\n\n...[TRUNCATED]...\n\n", output.AsSpan(output.Length - half));
        }
    }
}
