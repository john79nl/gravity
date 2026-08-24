using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Gravity.Core.Agents;
namespace Gravity.Core
{
    /// <summary>
    /// Facade that exposes orchestration and agent operations to UI layers.
    /// Keeps UI decoupled from Orchestrator internals so the UI can consume a stable surface.
    /// </summary>
    public interface IAgentService
    {
        Task<(AppEngine Agent, IntentClassification Intent, TaskPlan? Plan)> ClassifyAndPlanAsync(string userIntent, CancellationToken ct);

        Task<string> RunAgentLoopAsync(string userIntent, IProgress<string> logProgress, IProgress<string> streamProgress, CancellationToken ct, int maxSteps = 10);

        IEnumerable<AgentDescriptor> GetToolDescriptors();

        AppEngine SpawnAgent(string intent);

        void StopAgent(string id);

        void RemoveAgent(string id);

        IEnumerable<AppEngine> GetActiveAgents();
    }
}
