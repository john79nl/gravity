using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Gravity.Core.Agents;
namespace Gravity.Core
{
    internal class OrchestratorAgentService : IAgentService
    {
        private readonly Orchestrator _orch;

        public OrchestratorAgentService(Orchestrator orch)
        {
            _orch = orch ?? throw new ArgumentNullException(nameof(orch));
        }

        public Task<(AppEngine Agent, IntentClassification Intent, TaskPlan? Plan)> ClassifyAndPlanAsync(string userIntent, CancellationToken ct)
            => _orch.ClassifyAndPlanAsync(userIntent, ct);

        public Task<string> RunAgentLoopAsync(string userIntent, IProgress<string> logProgress, IProgress<string> streamProgress, CancellationToken ct, int maxSteps = 10)
            => _orch.AgentLoopAsync(userIntent, null, logProgress, streamProgress, ct, maxSteps);

        public IEnumerable<AgentDescriptor> GetToolDescriptors() => _orch.GetToolDescriptors();

        public AppEngine SpawnAgent(string intent) => _orch.SpawnAgent(intent);

        public void StopAgent(string id) => _orch.StopAgent(id);

        public void RemoveAgent(string id) => _orch.RemoveAgent(id);

        public IEnumerable<AppEngine> GetActiveAgents() => _orch.GetActiveAgents();
    }
}
