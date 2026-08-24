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
                SupportedVerbs = new[] { "about", "context", "plan", "read_plan", "propose", "walkthrough", "help", "check_update", "apply_update" },
                Actions = new List<ActionMetadata>
                {
                    new ActionMetadata { Name = "about", Description = "Describe the Gravity architecture.", IsMutation = false },
                    new ActionMetadata { Name = "context", Description = "Get high-level project diagnostics.", IsMutation = false },
                    new ActionMetadata { Name = "help", Description = "Get real-time documentation for all tools.", IsMutation = false },
                    new ActionMetadata { Name = "check_update", Description = "Check for Gravity application updates.", IsMutation = false },
                    new ActionMetadata { Name = "apply_update", Description = "Apply an update and restart Gravity. REQUIRED: download_url.", IsMutation = true, Parameters = new Dictionary<string, string> { ["download_url"] = "The URL to download the update from" } },
                    new ActionMetadata { Name = "plan", Description = "Create/Update a Task Plan. REQUIRED: title, tasks.", IsMutation = true, Parameters = new Dictionary<string, string> { ["title"] = "Plan title", ["tasks"] = "List of tasks" } },
                    new ActionMetadata { Name = "read_plan", Description = "Read the current master implementation plan.", IsMutation = false },
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
                case "read_plan":
                    return HandleReadPlanAsync();
                case "propose":
                    return HandleArtifactAsync(ArtifactType.ImplementationPlan, request);
                case "walkthrough":
                    return HandleArtifactAsync(ArtifactType.Walkthrough, request);
                case "check_update":
                    return await HandleCheckUpdateAsync();
                case "apply_update":
                    return await HandleApplyUpdateAsync(request);
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

            // Parse tasks BEFORE touching the artifact so the content is ready
            var incomingTitles = tasksRaw.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                         .Select(t => t.Trim())
                                         .Where(t => !string.IsNullOrWhiteSpace(t))
                                         .ToList();

            // Find existing plan or create new
            var existing = _artifactService.GetArtifacts().FirstOrDefault(a => a.Type == ArtifactType.TaskPlan && a.Title == title) as TaskArtifact;

            // Merge incoming titles with existing tasks (preserve completion state)
            var allTitles = new List<string>();
            if (existing != null)
                allTitles.AddRange(existing.Tasks.Select(t => t.Title));
            foreach (var t in incomingTitles)
            {
                if (!allTitles.Any(x => x.Equals(t, StringComparison.OrdinalIgnoreCase)))
                    allTitles.Add(t);
            }

            // Build content preview so the artifact has content at creation time
            var contentPreview = string.Join("\n", allTitles.Select(t => $"[ ] {t}"));

            string engineId = "";
            if (request.ArgMap.TryGetValue("__engine_id", out var engineIdVal) && engineIdVal != null)
            {
                engineId = engineIdVal.ToString();
            }

            var artifact = existing ?? _artifactService.CreateArtifact(ArtifactType.TaskPlan, title, contentPreview, engineId) as TaskArtifact;

            if (artifact == null) return new AgentResult { Success = false, Output = "Failed to create/access task artifact." };

            // Merge incoming tasks (preserve existing completion state)
            foreach (var t in incomingTitles)
            {
                var existingTask = artifact.Tasks.FirstOrDefault(x => x.Title.Equals(t, StringComparison.OrdinalIgnoreCase));
                if (existingTask == null)
                    artifact.Tasks.Add(new TaskItem { Title = t });
            }

            // Sync content from tasks so the card and panel display the checklist
            artifact.Content = string.Join("\n", artifact.Tasks.Select(t =>
                $"{(t.IsCompleted ? "[x]" : "[ ]")} {t.Title}"));

            _artifactService.UpdateArtifact(artifact);
            return new AgentResult { Success = true, Output = $"Task plan '{title}' updated with {artifact.Tasks.Count} items." };
        }

        private AgentResult HandleArtifactAsync(ArtifactType type, AgentRequest request)
        {
            var title = request.GetStringArgument("title");
            var content = request.GetStringArgument("content");
            if (string.IsNullOrWhiteSpace(title)) return new AgentResult { Success = false, Output = "Missing 'title' argument." };
            if (string.IsNullOrWhiteSpace(content)) return new AgentResult { Success = false, Output = "Missing 'content' argument." };

            string engineId = "";
            if (request.ArgMap.TryGetValue("__engine_id", out var engineIdVal) && engineIdVal != null)
            {
                engineId = engineIdVal.ToString();
            }

            _artifactService.CreateArtifact(type, title, content, engineId);

            bool needsApproval = type == ArtifactType.ImplementationPlan;
            return new AgentResult
            {
                Success = true,
                Output = needsApproval
                    ? $"Implementation plan '{title}' created and is now awaiting user approval. Do NOT proceed with any execution until the user explicitly approves."
                    : $"Artifact '{title}' ({type}) created.",
                RequiresPlanApproval = needsApproval
            };
        }

        private AgentResult HandleReadPlanAsync()
        {
            var planArtifact = _artifactService.GetArtifacts()
                .FirstOrDefault(a => a.Type == ArtifactType.ImplementationPlan);
            
            if (planArtifact != null)
            {
                return new AgentResult { Success = true, Output = $"# {planArtifact.Title}\n\n{planArtifact.Content}" };
            }
            return new AgentResult { Success = false, Output = "No active implementation plan found." };
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
                var topDirs = Directory.GetDirectories(root).Select(Path.GetFileName).OrderBy(x => x).ToList();
                var topFiles = Directory.GetFiles(root, "*.cs", SearchOption.TopDirectoryOnly).Concat(
                               Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly)).Select(Path.GetFileName).OrderBy(x => x).ToList();
                var csFiles = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
                var entry = csFiles.FirstOrDefault(f => f.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase) || f.Contains("Entry") || f.Contains("Main"));

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
- **Knowledge Base**: `Connected`

### Project Structure
- **Entry Point**: `{(entry != null ? Path.GetRelativePath(root, entry) : "Not detected")}`
- **Source Files (.cs)**: `{csFiles.Length}`
- **Top Directories**: `{string.Join(", ", topDirs)}`
- **Key Config Files**: `{string.Join(", ", topFiles)}`";
            }
            catch (Exception ex)
            {
                return $"> [!CAUTION]\n> Error gathering diagnostics: {ex.Message}";
            }
        }

        private async Task<AgentResult> HandleCheckUpdateAsync()
        {
            var updateService = _serviceProvider.GetRequiredService<UpdateService>();
            var result = await updateService.CheckForUpdatesAsync();
            if (result.UpdateAvailable)
            {
                return new AgentResult { Success = true, Output = $"# 🚀 Update Available!\n\n**Version:** {result.LatestVersion}\n**Release Notes:** {result.ReleaseNotes}\n**Download URL:** {result.DownloadUrl}\n\nTo apply this update, use the `apply_update` action with the download_url." };
            }
            return new AgentResult { Success = true, Output = "Gravity is up to date." };
        }

        private async Task<AgentResult> HandleApplyUpdateAsync(AgentRequest request)
        {
            var downloadUrl = request.GetStringArgument("download_url");
            if (string.IsNullOrWhiteSpace(downloadUrl)) return new AgentResult { Success = false, Output = "Missing 'download_url' argument." };

            var updateService = _serviceProvider.GetRequiredService<UpdateService>();
            var success = await updateService.DownloadAndApplyUpdateAsync(downloadUrl);
            
            if (success)
            {
                 return new AgentResult { Success = true, Output = "Update applied. Gravity will now restart." };
            }
            return new AgentResult { Success = false, Output = "Failed to apply update. Check the console logs." };
        }
    }
}
