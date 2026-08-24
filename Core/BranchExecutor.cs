using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gravity.Core.Agents;

namespace Gravity.Core
{
    /// <summary>
    /// Read-only snapshot of parent context passed to child branch engines.
    /// Each child gets a clean History but inherits the parent's task understanding.
    /// </summary>
    public sealed class ContextSnapshot
    {
        public string TaskDescription { get; init; } = string.Empty;
        public List<string> PinnedFacts { get; init; } = new();
        public List<string> FilesModifiedByPriorSteps { get; init; } = new();
        public string WorkingDirectory { get; init; } = string.Empty;
        public EnvironmentContext EnvironmentContext { get; init; } = EnvironmentContext.Detect();
    }

    /// <summary>
    /// Executes sub-steps in isolated branches. Each branch gets its own AppEngine
    /// with an independent History, preventing sibling interference in deep trees.
    ///
    /// Usage:
    ///   var executor = new BranchExecutor(parentEngine);
    ///   var result = await executor.ExecuteBranchAsync(step, logFile, ct, maxSteps);
    ///
    /// The parent engine's History is NEVER modified by child branches.
    /// Only the FinalOutput from each child is returned to the parent.
    /// </summary>
    public sealed class BranchExecutor
    {
        private readonly AppEngine _parent;

        public BranchExecutor(AppEngine parent)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        }

        /// <summary>
        /// Creates a ContextSnapshot from the parent engine's current state.
        /// Call this BEFORE executing any sub-steps to capture the parent's context.
        /// </summary>
        public ContextSnapshot CaptureContext()
        {
            var facts = new List<string>();
            lock (_parent.History)
            {
                facts.AddRange(_parent.History
                    .Where(m => m.Role == "system" && m.Content?.StartsWith("FACT:") == true)
                    .Select(m => m.Content!));
            }

            return new ContextSnapshot
            {
                TaskDescription = _parent.UserIntent,
                PinnedFacts = facts,
                WorkingDirectory = _parent.EnvironmentContext.WorkingDirectory,
                EnvironmentContext = _parent.EnvironmentContext
            };
        }

        /// <summary>
        /// Executes a single step in an isolated branch.
        /// Returns only the FinalOutput — the child's History is discarded.
        /// </summary>
        public async Task<string> ExecuteBranchAsync(
            PlannedStep step,
            string logFile,
            CancellationToken ct,
            int maxSteps = 15,
            ContextSnapshot? parentContext = null)
        {
            // Create a fresh engine — independent History, same services
            var childEngine = new AppEngine(
                _parent.Router,
                _parent.SettingsService,
                _parent.ArtifactService,
                _parent.RagService,
                _parent.KnowledgeService,
                _parent.ProjectContext,
                _parent.Model);

            // Copy pinned facts from parent context into child's history
            if (parentContext != null)
            {
                foreach (var fact in parentContext.PinnedFacts)
                {
                    childEngine.History.Add(new ChatMessage { Role = "system", Content = fact });
                }

                // Tell the child what files the parent already modified
                if (parentContext.FilesModifiedByPriorSteps.Count > 0)
                {
                    var priorWork = string.Join("\n", parentContext.FilesModifiedByPriorSteps.Select(f => $"- {f}"));
                    childEngine.History.Add(new ChatMessage
                    {
                        Role = "system",
                        Content = $"PRIOR WORK completed by parent steps:\n{priorWork}\nDo NOT re-read these files unless you need to verify content."
                    });
                }
            }

            // Child engine runs independently — no event wiring needed.
            // Parent only sees the FinalOutput, not intermediate logs.
            childEngine.TaskState = AgentTaskState.Executing;

            // Build the step intent — explicit tool call instruction
            var stepIntent = $"[STEP] Call {step.Tool}.{step.Verb} now with the required parameters. " +
                $"Description: {step.Description}. " +
                $"Do NOT call action.final until AFTER you have received the observation from this tool call.";

            // Execute in the child's isolated context
            await childEngine.ExecuteAsync(stepIntent, logFile, ct, maxSteps);

            return childEngine.FinalOutput ?? string.Empty;
        }

        /// <summary>
        /// Executes multiple sub-steps sequentially, each in its own isolated branch.
        /// Returns a list of (step, output) pairs.
        /// </summary>
        public async Task<List<(PlannedStep Step, string Output)>> ExecuteBranchesAsync(
            IReadOnlyList<PlannedStep> steps,
            string logFile,
            CancellationToken ct,
            int maxSteps = 15)
        {
            var parentContext = CaptureContext();
            var results = new List<(PlannedStep Step, string Output)>();
            var filesModified = new List<string>();

            for (int i = 0; i < steps.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var step = steps[i];
                _parent.EmitLog(
                    $"[Branch] Sub-step {i + 1}/{steps.Count}: {step.Description}");

                // Update context with files modified by previous steps in this batch
                var enrichedContext = new ContextSnapshot
                {
                    TaskDescription = parentContext.TaskDescription,
                    PinnedFacts = parentContext.PinnedFacts,
                    FilesModifiedByPriorSteps = filesModified.ToList(),
                    WorkingDirectory = parentContext.WorkingDirectory,
                    EnvironmentContext = parentContext.EnvironmentContext
                };

                var output = await ExecuteBranchAsync(step, logFile, ct, maxSteps, enrichedContext);
                results.Add((step, output));

                // Track files modified by this step for subsequent steps
                if (step.SubPlan?.Steps != null)
                {
                    filesModified.AddRange(step.SubPlan.Steps
                        .Where(s => s.Verb is "write_file" or "apply_diff" or "replace_block")
                        .Select(s => s.Description));
                }
                if (step.Verb is "write_file" or "apply_diff" or "replace_block")
                {
                    filesModified.Add(step.Description);
                }

                // Check for abort condition
                if (output.Contains("I was unable to produce a valid response after several attempts",
                    StringComparison.OrdinalIgnoreCase))
                {
                    _parent.EmitLog("[Branch] Sub-step failed. Aborting branch.");
                    break;
                }
            }

            return results;
        }
    }
}
