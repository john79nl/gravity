using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Gravity.Core
{
    public class GravityAgent : IAgent
    {
        private readonly IProjectContext _projectContext;
        private readonly IArtifactService _artifactService;
        private readonly IServiceProvider _serviceProvider;

        public AgentDescriptor Descriptor { get; }

        public GravityAgent(IProjectContext projectContext, IArtifactService artifactService, IServiceProvider serviceProvider)
        {
            _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
            _artifactService = artifactService ?? throw new ArgumentNullException(nameof(artifactService));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            Descriptor = new AgentDescriptor
            {
                Name = "gravity",
                Description = "Internal meta-tool for planning, diagnostics, and artifact management.",
                CanWrite = true,
                SupportedVerbs = new[] { "about", "context", "plan", "propose", "walkthrough", "help" },
                Actions = new List<ActionMetadata>
                {
                    new ActionMetadata { Name = "about", Description = "Describe the Gravity architecture.", IsMutation = false },
                    new ActionMetadata { Name = "context", Description = "Get high-level project diagnostics.", IsMutation = false },
                    new ActionMetadata { Name = "help", Description = "Get real-time documentation for all tools.", IsMutation = false },
                    new ActionMetadata { Name = "plan", Description = "Create/Update a Task Plan. REQUIRED: title, tasks.", IsMutation = true, Parameters = new Dictionary<string, string> { ["title"] = "Plan title", ["tasks"] = "List of tasks" } },
                    new ActionMetadata { Name = "propose", Description = "Create an Implementation Plan. REQUIRED: title, content.", IsMutation = true, Parameters = new Dictionary<string, string> { ["title"] = "Plan title", ["content"] = "Detailed proposal" } },
                    new ActionMetadata { Name = "walkthrough", Description = "Create a final Walkthrough. REQUIRED: title, content.", IsMutation = true, Parameters = new Dictionary<string, string> { ["title"] = "Walkthrough title", ["content"] = "Summary of changes" } }
                }
            };
        }

        public async Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Verb))
                return new AgentResult { Success = false, Output = "Invalid request." };

            switch (request.Verb.ToLowerInvariant())
            {
                case "about":
                    return new AgentResult { Success = true, Output = GetAboutInternal() };
                case "context":
                    return new AgentResult { Success = true, Output = GetContextInternal() };
                case "help":
                    return new AgentResult { Success = true, Output = GetHelpInternal() };
                case "plan":
                    return await HandlePlanAsync(request, ct);
                case "propose":
                    return HandleArtifactAsync(ArtifactType.ImplementationPlan, request);
                case "walkthrough":
                    return HandleArtifactAsync(ArtifactType.Walkthrough, request);
                default:
                    return new AgentResult { Success = false, Output = $"Unknown gravity verb '{request.Verb}'." };
            }
        }

        private string GetHelpInternal()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Gravity Tool Registry (Real-time)");
            var agents = _serviceProvider.GetServices<IAgent>();
            foreach (var agent in agents.OrderBy(a => a.Descriptor.Name))
            {
                var d = agent.Descriptor;
                sb.AppendLine($"\n## Agent: {d.Name}");
                sb.AppendLine(d.Description);
                sb.AppendLine("### Actions:");
                foreach (var action in d.Actions)
                {
                    sb.AppendLine($"- **{action.Name}**: {action.Description}");
                    if (action.Parameters != null && action.Parameters.Any())
                    {
                        sb.AppendLine("  Parameters:");
                        foreach (var p in action.Parameters)
                            sb.AppendLine($"    - {p.Key}: {p.Value}");
                    }
                }
            }
            return sb.ToString();
        }

        private async Task<AgentResult> HandlePlanAsync(AgentRequest request, CancellationToken ct)
        {
            var title = request.GetStringArgument("title", "Task Plan");
            var tasksRaw = request.GetStringArgument("tasks");
            if (string.IsNullOrWhiteSpace(tasksRaw)) return new AgentResult { Success = false, Output = "Missing 'tasks' argument." };

            // Find existing plan or create new
            var existing = _artifactService.GetArtifacts().FirstOrDefault(a => a.Type == ArtifactType.TaskPlan && a.Title == title) as TaskArtifact;
            var artifact = existing ?? _artifactService.CreateArtifact(ArtifactType.TaskPlan, title, "Agent's current roadmap.") as TaskArtifact;

            if (artifact == null) return new AgentResult { Success = false, Output = "Failed to create/access task artifact." };

            // Parse tasks (handle comma-separated or simple lines)
            var newTasks = tasksRaw.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim());
            foreach (var t in newTasks)
            {
                if (!artifact.Tasks.Any(x => x.Title.Equals(t, StringComparison.OrdinalIgnoreCase)))
                {
                    artifact.Tasks.Add(new TaskItem { Title = t });
                }
            }

            _artifactService.UpdateArtifact(artifact);
            return new AgentResult { Success = true, Output = $"Task plan '{title}' updated with {artifact.Tasks.Count} items." };
        }

        private AgentResult HandleArtifactAsync(ArtifactType type, AgentRequest request)
        {
            var title = request.GetStringArgument("title");
            var content = request.GetStringArgument("content");
            if (string.IsNullOrWhiteSpace(title)) return new AgentResult { Success = false, Output = "Missing 'title' argument." };
            if (string.IsNullOrWhiteSpace(content)) return new AgentResult { Success = false, Output = "Missing 'content' argument." };

            _artifactService.CreateArtifact(type, title, content);
            return new AgentResult { Success = true, Output = $"Artifact '{title}' ({type}) created." };
        }

        private string GetAboutInternal()
        {
            return @"# 🌌 Gravity Architecture: System Blueprint

Gravity is a **Multi-Agent Orchestration Platform** built for local autonomous development.

### 1. High-Level Flow
```mermaid
graph TD
    User([User Intent]) --> Orch[Orchestrator]
    Orch --> Pool{Agent Pool}
    Pool --> Agent[AgentInstance]
    Agent --> Reasoning[Reasoning Model]
    Reasoning --> Monologue[< internal_monologue >]
    Monologue --> Action{Action JSON}
    Action -- Fallback --> Executor[Executor Model]
    Executor --> Router[Reasoning Router]
    Router --> Tools[Agents]
    Tools --> Obs[Observation]
    Obs --> Agent
```

### 2. Core Architectural Pillars

| Component | Responsibility | Technical Implementation |
| :--- | :--- | :--- |
| **Orchestrator** | Multi-task pool management. | `Orchestrator.cs` |
| **Agent Instance** | Isolated state and reasoning. | `AgentInstance.cs` |
| **Reasoning Router** | Hardened command resolution. | `ReasoningRouter.cs` |
| **Context Engine** | Semantics and Knowledge. | `RagService.cs` / `KnowledgeService.cs` |

### 3. Design Philosophy
- **Verification First**: All code changes are verified via `shell: run (dotnet build)`.
- **Strategic Research**: Direct pathing via [PROJECT ARCHITECTURE OVERVIEW].
- **Self-Improving Memory**: Automated extraction of technical landmarks.";
        }

        private string GetContextInternal()
        {
            var root = _projectContext.ProjectDirectory;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return "> [!WARNING]\n> Context unavailable: No project loaded.";

            try
            {
                var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories);
                var dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
                var csproj = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();

                return $@"# 🔍 Project Context Diagnostics

| Metric | Value |
| :--- | :--- |
| **Root Path** | `{root}` |
| **Primary Project** | `{Path.GetFileName(csproj ?? "None Found")}` |
| **Platform** | `Windows 10.0+` |
| **Target Runtime** | `.NET 10 (Preview)` |

### Workspace Statistics
- **Total Files**: `{files.Length}`
- **Directories**: `{dirs.Length}`
- **Agent Memory**: `Active`
- **Knowledge Base**: `Connected`";
            }
            catch (Exception ex)
            {
                return $"> [!CAUTION]\n> Error gathering diagnostics: {ex.Message}";
            }
        }
    }
}
