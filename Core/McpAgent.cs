using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    public class McpAgent : IAgent
    {
        private readonly AgentDescriptor _descriptor;
        private readonly McpClient _client;
        private readonly List<McpToolDefinition> _tools;

        public McpAgent(McpServerConfig config)
        {
            _client = new McpClient(config);
            ServerName = config.Name;
            _tools = new List<McpToolDefinition>();
            _descriptor = new AgentDescriptor
            {
                Name = config.Name,
                Description = $"MCP server: {config.Name}",
                CanWrite = false,
                SupportedVerbs = Array.Empty<string>(),
                Actions = new List<ActionMetadata>()
            };
        }

        public string ServerName { get; }
        public AgentDescriptor Descriptor => _descriptor;

        public async Task InitializeAsync(CancellationToken ct)
        {
            _tools.Clear();
            var tools = await _client.ConnectAsync(ct);
            _tools.AddRange(tools);

            _descriptor.SupportedVerbs = tools.Select(t => t.Name).ToArray();
            _descriptor.Actions = tools.Select(t => new ActionMetadata
            {
                Name = t.Name,
                Description = t.Description,
                IsMutation = false,
                Parameters = ExtractParameters(t.InputSchema),
                OptionalParameters = new List<string>()
            }).ToList();
            _descriptor.Description = $"MCP server: {ServerName} — {tools.Count} tools available";
        }

        public async Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken ct)
        {
            var toolName = request.Verb;
            var tool = _tools.Find(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
            if (tool == null)
                return new AgentResult { Success = false, Output = $"Unknown tool '{toolName}' on MCP server '{ServerName}'." };

            try
            {
                var result = await _client.CallToolAsync(tool.Name, request.ArgMap, ct);
                var output = result.ValueKind == JsonValueKind.String
                    ? result.GetString() ?? ""
                    : result.GetRawText();

                return new AgentResult { Success = true, Output = output };
            }
            catch (Exception ex)
            {
                return new AgentResult { Success = false, Output = $"MCP tool '{toolName}' error: {ex.Message}" };
            }
        }

        private static Dictionary<string, string> ExtractParameters(JsonElement? schema)
        {
            var result = new Dictionary<string, string>();
            if (schema == null) return result;

            if (schema.Value.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in props.EnumerateObject())
                {
                    var desc = prop.Value.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                    result[prop.Name] = desc;
                }
            }

            return result;
        }
    }
}
