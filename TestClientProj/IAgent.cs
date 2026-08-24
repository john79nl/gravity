using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    public interface IAgent
    {
        /// <summary>
        /// Execute a structured request for this agent and return an AgentResult.
        /// </summary>
        Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken ct);

        /// <summary>
        /// Descriptor describing agent capabilities.
        /// </summary>
        AgentDescriptor Descriptor { get; }
    }
}
