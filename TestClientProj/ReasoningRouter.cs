using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    public class ReasoningRouter
    {
        private readonly ConcurrentDictionary<string, IAgent> _agents = new();
        private readonly ConcurrentDictionary<string, bool> _agentWriteEnabled = new();

        public ReasoningRouter(IEnumerable<IAgent> agents)
        {
            foreach (var agent in agents)
            {
                RegisterAgent(agent.Descriptor.Name, agent);
            }
        }

        public void RegisterAgent(string name, IAgent agent)
        {
            var key = name ?? throw new ArgumentNullException(nameof(name));
            _agents[key.ToLowerInvariant()] = agent ?? throw new ArgumentNullException(nameof(agent));
            
            // initialize write-enabled flag from descriptor
            _agentWriteEnabled[key.ToLowerInvariant()] = agent.Descriptor.CanWrite;
        }

        public virtual async Task<AgentResult> RouteAsync(string rawCommand, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(rawCommand))
                return new AgentResult { Success = false, Output = "No command provided." };

            // Check if it's a JSON command or a legacy string command
            if (rawCommand.TrimStart().StartsWith("{"))
            {
                try
                {
                    var routerRequest = JsonSerializer.Deserialize<RouterRequest>(rawCommand, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (routerRequest != null)
                    {
                        return await ExecuteStructuredAsync(routerRequest.Agent, routerRequest.Request, ct);
                    }
                }
                catch (Exception ex)
                {
                    return new AgentResult { Success = false, Output = "Failed to parse JSON router request: " + ex.Message };
                }
            }

            // Legacy parsing: "agent: verb arg1=val1 arg2=val2"
            var parts = rawCommand.Split(new[] { ':' }, 2);
            if (parts.Length == 2)
            {
                var agentName = parts[0].Trim().ToLowerInvariant();
                var payload = parts[1].Trim();
                
                // Convert legacy payload to AgentRequest
                var request = ParseLegacyPayload(payload);
                return await ExecuteStructuredAsync(agentName, request, ct);
            }

            // Fallback to default agent named 'file' if present
            return await RouteAsync("file: " + rawCommand, ct);
        }

        private async Task<AgentResult> ExecuteStructuredAsync(string? agentName, AgentRequest? request, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(agentName))
                return new AgentResult { Success = false, Output = "Agent name missing." };
            if (request == null)
                return new AgentResult { Success = false, Output = "Request missing." };

            // Handle Dot-Notation Hallucinations (e.g. "file.list" instead of tool: "file", verb: "list")
            if (agentName.Contains('.'))
            {
                var dotParts = agentName.Split('.', 2);
                agentName = dotParts[0];
                request.Verb = dotParts[1];
            }

            var targetAgent = agentName.ToLowerInvariant();
            
            // CLI Aliases: If AI treats 'dotnet', 'git' etc. as agents, route to 'shell'
            var cliAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dotnet", "git", "npm", "node", "powershell", "pwsh" };
            if (cliAliases.Contains(targetAgent) && !_agents.ContainsKey(targetAgent))
            {
                // Reroute to shell
                if (_agents.TryGetValue("shell", out var shellAgent))
                {
                    // If the verb is generic like 'run', use the agent name as the command
                    if (request.Verb.Equals("run", StringComparison.OrdinalIgnoreCase) || 
                        request.Verb.Equals("execute", StringComparison.OrdinalIgnoreCase))
                    {
                        request.ArgMap["command"] = targetAgent;
                    }
                    else
                    {
                        // The verb itself might be the command args, e.g. agent: dotnet, verb: build
                        // We transform this into agent: shell, verb: dotnet, args: build
                        request.ArgMap["args"] = request.Verb;
                        request.Verb = targetAgent;
                    }
                    return await shellAgent.ExecuteAsync(request, ct);
                }
            }

            if (_agents.TryGetValue(targetAgent, out var agent))
            {
                // Generic Permission Check using Metadata
                var action = agent.Descriptor.Actions.FirstOrDefault(a => a.Name.Equals(request.Verb, StringComparison.OrdinalIgnoreCase))
                             ?? new ActionMetadata { Name = request.Verb, IsMutation = request.Verb.Equals("replace", StringComparison.OrdinalIgnoreCase) || request.Verb.Equals("apply_diff", StringComparison.OrdinalIgnoreCase) || request.Verb.Equals("write_file", StringComparison.OrdinalIgnoreCase) }; // fallback

                // Centralized Pre-Validation
                var missingParams = ValidateParameters(request, action);
                if (missingParams.Any())
                {
                    var paramSummary = string.Join(", ", action.Parameters.Select(p => $"\"{p.Key}\": \"{p.Value}\""));
                    return new AgentResult 
                    { 
                        Success = false, 
                        Output = $"ERROR: Missing required arguments for verb '{request.Verb}'.\n" +
                                 $"Expected Schema: {{ {paramSummary} }}\n" +
                                 $"Missing keys: {string.Join(", ", missingParams)}" 
                    };
                }

                if (action.IsMutation && !GetAgentWriteEnabled(targetAgent))
                {
                    return new AgentResult
                    {
                        Success = false,
                        Output = $"ERROR: Agent '{agentName}' does not have write permissions to execute mutation '{request.Verb}'."
                    };
                }

                return await agent.ExecuteAsync(request, ct);
            }

            return new AgentResult { Success = false, Output = $"Unknown agent '{agentName}'." };
        }

        private List<string> ValidateParameters(AgentRequest request, ActionMetadata action)
        {
            var missing = new List<string>();
            if (action.Parameters == null) return missing;

            foreach (var p in action.Parameters)
            {
                // Skip if the parameter is marked as optional
                if (action.OptionalParameters != null && action.OptionalParameters.Contains(p.Key))
                    continue;

                // Check if the parameter exists in ArgMap or is resolved via hardening synonyms
                var val = request.GetStringArgument(p.Key);
                if (string.IsNullOrEmpty(val))
                {
                    missing.Add(p.Key);
                }
            }
            return missing;
        }

        private AgentRequest ParseLegacyPayload(string payload)
        {
            var parts = payload.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var verb = parts.Length > 0 ? parts[0] : "none";
            var rest = parts.Length > 1 ? parts[1] : string.Empty;

            var request = new AgentRequest { Verb = verb };
            
            // Very simple parser for legacy: if it's search or read, everything else is the 'path' or 'pattern'
            if (verb.Equals("search", StringComparison.OrdinalIgnoreCase))
                request.ArgMap["pattern"] = rest;
            else if (verb.Equals("read", StringComparison.OrdinalIgnoreCase))
                request.ArgMap["path"] = rest;
            else if (verb.Equals("replace", StringComparison.OrdinalIgnoreCase))
            {
                // very brittle, but keeping it for backward compatibility during migration
                request.ArgMap["raw"] = rest; 
            }
            else
            {
                request.ArgMap["input"] = rest;
            }

            return request;
        }

        public async Task<string> RouteAsStringAsync(string command, CancellationToken ct)
        {
            var res = await RouteAsync(command, ct);
            return res?.Output ?? string.Empty;
        }

        public IEnumerable<string> GetAgentNames() => _agents.Keys;

        public IEnumerable<AgentDescriptor> GetAgentDescriptors() => _agents.Values.Select(a => a.Descriptor);

        public IAgent? GetAgent(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _agents.TryGetValue(name.ToLowerInvariant(), out var agent) ? agent : null;
        }

        public void SetAgentWriteEnabled(string name, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _agentWriteEnabled[name.ToLowerInvariant()] = enabled;
        }

        public bool GetAgentWriteEnabled(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return _agentWriteEnabled.TryGetValue(name.ToLowerInvariant(), out var v) && v;
        }

        private class RouterRequest
        {
            public string? Agent { get; set; }
            public AgentRequest? Request { get; set; }
        }
    }
}
