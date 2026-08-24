using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    public interface IRouterService
    {
        IEnumerable<string> GetAgentNames();
        IEnumerable<AgentDescriptor> GetAgentDescriptors();
        IAgent GetAgent(string name);
        Task<AgentResult> RouteAsync(string rawCommand, CancellationToken ct);
    }

    public interface IRagService
    {
        Task RefreshIndexAsync(CancellationToken ct = default);
        void NotifyFileChanged(string filePath);
    }

    public interface IKnowledgeService
    {
        Task RefreshKnowledgeAsync();
        List<KnowledgeItem> MatchKnowledge(string intent, int limit = 5);
        Task<string> GetContentAsync(KnowledgeItem item);
        Task AddKnowledgeAsync(KnowledgeItem item, string content);
        List<KnowledgeItem> GetKnowledgeItems();
    }

    public enum AgentStatus
    {
        Idle,
        Running,
        Paused,
        Finished,
        Error
    }
}
