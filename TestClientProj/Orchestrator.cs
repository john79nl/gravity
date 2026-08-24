using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text;

namespace Gravity.Core
{
    public class Orchestrator
    {
        private readonly IModelClient _model;
        private readonly ReasoningRouter _router;
        private readonly IProjectContext _projectContext;
        private readonly KnowledgeService _knowledgeService;
        private readonly ISettingsService _settingsService;
        private readonly IArtifactService _artifactService;
        private readonly RagService _ragService;
        private readonly List<string> _sessionMemory = new();

        private readonly ConcurrentDictionary<string, AgentInstance> _agentPool = new();
        private const int MAX_CONCURRENT_AGENTS = 5;

        public event Action<AgentInstance>? OnAgentSpawned;
        public event Action<AgentInstance>? OnAgentFinished;

        public Orchestrator(IModelClient model, ReasoningRouter router, IProjectContext projectContext, KnowledgeService knowledgeService, ISettingsService settingsService, IArtifactService artifactService, RagService ragService)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
            _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _artifactService = artifactService ?? throw new ArgumentNullException(nameof(artifactService));
            _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        }

        public AgentInstance SpawnAgent(string intent)
        {
            // Only purge if we are at the limit and have finished agents
            if (_agentPool.Count >= MAX_CONCURRENT_AGENTS)
            {
                var finished = _agentPool.Where(kv => kv.Value.Status == AgentStatus.Finished || kv.Value.Status == AgentStatus.Error).Select(kv => kv.Key).FirstOrDefault();
                if (finished != null) _agentPool.TryRemove(finished, out _);
                else throw new InvalidOperationException($"Maximum concurrent agents ({MAX_CONCURRENT_AGENTS}) reached. Stop a running agent first.");
            }

            var id = "agent_" + Guid.NewGuid().ToString("n").Substring(0, 8);
            var agent = new AgentInstance(id, intent, _model, _router, _projectContext, _knowledgeService, _settingsService, _artifactService, _ragService, _sessionMemory);
            
            _agentPool[id] = agent;
            OnAgentSpawned?.Invoke(agent);

            // Kick off a background index refresh so RAG context is ready
            _ = _ragService.RefreshIndexAsync();

            agent.OnStatusChanged += () => {
                if (agent.Status == AgentStatus.Finished || agent.Status == AgentStatus.Error)
                {
                    OnAgentFinished?.Invoke(agent);
                }
            };

            return agent;
        }

        public void StopAgent(string id)
        {
            if (_agentPool.TryGetValue(id, out var agent))
            {
                agent.Stop();
            }
        }

        public void RemoveAgent(string id)
        {
            if (_agentPool.TryRemove(id, out var agent))
            {
                agent.Stop();
            }
        }

        public IEnumerable<AgentInstance> GetActiveAgents() => _agentPool.Values;

        public void ClearPool()
        {
            foreach (var agent in _agentPool.Values) agent.Stop();
            _agentPool.Clear();
        }

        // --- LEGACY COMPATIBILITY WRAPPERS (To prevent breaking Form1 immediately) ---
        public async Task<string> AgentLoopAsync(string userIntent, IProgress<string> logProgress, IProgress<string> streamProgress, CancellationToken ct, int maxSteps = 10)
        {
            var agent = SpawnAgent(userIntent);
            agent.OnLog += (s) => logProgress.Report(s);
            agent.OnStream += (s) => streamProgress.Report(s);
            
            await agent.RunAsync(maxSteps);
            return agent.FinalOutput ?? "Agent workflow completed.";
        }

        private ToolEvaluation EvaluateToolResult(AgentAction action, AgentResult result)
        {
            if (result == null) return new ToolEvaluation { IsAcceptable = false, Advice = "Tool returned no result." };
            // ... (rest as before but this is now essentially dead code, keeping for logic reference if needed or just deleting)
            return new ToolEvaluation { IsAcceptable = true };
        }

        private class ToolEvaluation
        {
            public bool IsAcceptable { get; set; }
            public string? Advice { get; set; }
        }

        public async Task<List<(int Index, string Title, string Preview)>> CollectPreviewStepsAsync(Plan plan, CancellationToken ct)
        {
            var previews = new List<(int, string, string)>();
            if (plan?.Steps == null) return previews;

            for (int i = 0; i < plan.Steps.Count; i++)
            {
                var step = plan.Steps[i];
                ct.ThrowIfCancellationRequested();

                if (step.Apply == false) 
                {
                    var request = new AgentRequest 
                    { 
                        Verb = step.Command ?? string.Empty, 
                        ArgMap = step.Arguments?.ToDictionary(k => k.Key, v => (object)v) ?? new Dictionary<string, object>() 
                    };
                    request.ArgMap["preview"] = "true";

                    var res = await _router.RouteAsync(JsonSerializer.Serialize(new { Agent = step.Agent, Request = request }), ct);
                    previews.Add((i, step.Command ?? "Preview", res.Output ?? string.Empty));
                }
            }

            return previews;
        }

        public async Task<List<(int Index, AgentResult Result)>> ApplySelectedStepsAsync(Plan plan, IEnumerable<int> selectedIndexes, CancellationToken ct)
        {
            var results = new List<(int, AgentResult)>();
            var set = new HashSet<int>(selectedIndexes);
            if (plan?.Steps == null) return results;

            for (int i = 0; i < plan.Steps.Count; i++)
            {
                if (!set.Contains(i)) continue;
                var step = plan.Steps[i];
                ct.ThrowIfCancellationRequested();

                var request = new AgentRequest 
                { 
                    Verb = step.Command ?? string.Empty, 
                    ArgMap = step.Arguments?.ToDictionary(k => k.Key, v => (object)v) ?? new Dictionary<string, object>() 
                };
                
                var res = await _router.RouteAsync(JsonSerializer.Serialize(new { Agent = step.Agent, Request = request }), ct);
                results.Add((i, res));
                step.Result = res;
            }

            return results;
        }

        public async Task<string> ExecuteIntentAsync(string userIntent, CancellationToken ct)
        {
            // Keeping legacy plan-based execution for explicit 'plan:' calls
            var agents = string.Join(", ", _router.GetAgentNames());
            var planPrompt = $"Generate a JSON plan for: {userIntent}\nAgents: {agents}\nRespond with: {{ \"steps\": [ {{ \"agent\": \"...\", \"command\": \"verb\", \"arguments\": {{ ... }}, \"apply\": true/false }} ] }}";
            
            var planObj = await _model.CompleteAsync(planPrompt, ct);
            var planJsonRaw = planObj.Content;
            if (!PlanParser.TryExtractJson(planJsonRaw, out var planJson)) return "Failed to extract plan.";

            var plan = JsonSerializer.Deserialize<Plan>(planJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (plan == null || plan.Steps == null) return "Invalid plan.";

            var log = new List<string>();
            foreach (var step in plan.Steps)
            {
                var request = new AgentRequest 
                { 
                    Verb = step.Command ?? "", 
                    ArgMap = step.Arguments?.ToDictionary(k => k.Key, v => (object)v) ?? new Dictionary<string, object>() 
                };
                var res = await _router.RouteAsync(JsonSerializer.Serialize(new { Agent = step.Agent, Request = request }), ct);
                log.Add(res.Output ?? "");
            }

            return string.Join("\n\n", log);
        }

        public string BuildPlanPromptForExternalCall(string intent)
        {
            var agents = string.Join(", ", _router.GetAgentNames());
            return $"Generate a JSON plan for: {intent}\nAgents: {agents}\nRespond with: {{ \"steps\": [ {{ \"agent\": \"...\", \"command\": \"verb\", \"arguments\": {{ ... }}, \"apply\": true/false }} ] }}";
        }

        private class AgentAction
        {
            [JsonPropertyName("action")] public string? Action { get; set; }
            [JsonPropertyName("tool")] public string? Tool { get; set; }
            [JsonPropertyName("request")] public AgentRequest? Request { get; set; }
            [JsonPropertyName("output")] public string? Output { get; set; }
        }
    }
}
