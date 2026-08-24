using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Gravity.Core
{
    /// <summary>
    /// Classifies the user's high-level intent so the pipeline can route
    /// to the cheapest, fastest handler rather than always spawning the full
    /// autonomous agent loop.
    /// </summary>
    public enum IntentType
    {
        /// <summary>Greetings, small-talk, status questions — no code tools needed.</summary>
        Conversational,

        /// <summary>Read-only code exploration: count lines, find usages, search, explain.</summary>
        CodeAnalysis,

        /// <summary>Write operations: fix bugs, add features, refactor, rename.</summary>
        CodeEdit,

        /// <summary>Shell / build / test commands.</summary>
        ShellTask,

        /// <summary>Questions about architecture, patterns, or "what does X do" — needs
        /// file reads but no edits and no shell commands.</summary>
        KnowledgeQuery,

        /// <summary>File meta-stat requests like counting lines, file size, or existence checks.</summary>
        FileStatsCount,

        /// <summary>Deep context expansion: when user says "improve", automatically create
        /// all referenced files up to 3 levels deep (main files → referenced files →
        /// files referenced by those files).</summary>
        Improve,

        /// <summary>Could not be confidently classified; fall back to the full agent loop.</summary>
        Unknown
    }

    /// <summary>
    /// Describes the shape of plan the agent should generate.
    /// Drives how TaskPlanner and AgentInstance construct and execute work.
    /// </summary>
    public enum PlanShape
    {
        /// <summary>
        /// No tools needed — respond directly.
        /// Used for pure greetings and small-talk.
        /// </summary>
        DirectAnswer,

        /// <summary>
        /// A flat, sequential list of tool calls (1-5 steps).
        /// Suitable for lookups, single-file edits, build commands,
        /// and config / connection-string queries.
        /// </summary>
        TaskList,

        /// <summary>
        /// A structured multi-phase plan.
        /// The agent MUST call gravity.propose (implementation plan artifact)
        /// then gravity.plan (task checklist) before executing any code changes.
        /// Suitable for new features, refactoring, scaffolding, or any work
        /// that touches multiple files or components.
        /// </summary>
        ImplementationPlan,

        /// <summary>
        /// A deep, hierarchical plan with nested sub-plans.
        /// Used in Engineer mode for complex tasks that require extensive planning.
        /// Each top-level step can contain its own sub-steps (sub-plan).
        /// </summary>
        DeepPlan,

        /// <summary>
        /// Deep context expansion: when user says "improve", automatically create
        /// all referenced files up to 3 levels deep. This triggers recursive file
        /// creation where each level creates files for all references found in the
        /// previous level.
        /// </summary>
        DeepContextExpansion
    }

    /// <summary>Result produced by IntentRouter.ClassifyAsync.</summary>
    public class IntentClassification
    {
        public IntentType Type { get; init; }

        /// <summary>
        /// True when the request is complex enough to warrant a full implementation
        /// plan (gravity.propose) followed by a sub-task checklist (gravity.plan).
        /// Derived from Shape for backwards compatibility.
        /// </summary>
        public bool IsComplex => Shape == PlanShape.ImplementationPlan;

        /// <summary>
        /// The plan shape chosen by IntentRouter after analysing both intent type
        /// and prompt complexity.
        /// DirectAnswer → reply directly, no tools.
        /// TaskList     → 1-5 deterministic tool-call steps.
        /// ImplementationPlan → full reasoning loop: gravity.propose → gravity.plan → execute.
        /// </summary>
        public PlanShape Shape { get; init; } = PlanShape.TaskList;

        /// <summary>Human-readable explanation of why this plan shape was chosen.</summary>
        public string ComplexityReason { get; init; } = string.Empty;

        /// <summary>Human-readable explanation used for debug logs.</summary>
        public string Reasoning { get; init; } = string.Empty;

        /// <summary>0.0–1.0. Heuristic matches are always 1.0; LLM fallback varies.</summary>
        public float Confidence { get; init; } = 1.0f;

        /// <summary>True if the classification came from heuristics (no LLM call was made).</summary>
        public bool IsHeuristic { get; init; }
    }

    /// <summary>
    /// A concrete, pre-planned sequence of tool calls produced by TaskPlanner.
    /// The agent execution loop (RunWithPlanAsync) executes these steps
    /// deterministically and only calls the LLM once at the end for synthesis.
    /// </summary>
    public class TaskPlan
    {
        public IntentType Intent { get; set; }

        /// <summary>One-line summary of what the plan will do (shown in debug log).</summary>
        public string Summary { get; set; } = string.Empty;

        public List<PlannedStep> Steps { get; set; } = new();
    }

    public class PlannedStep
    {
        public string Tool { get; set; } = string.Empty;
        public string Verb { get; set; } = string.Empty;
        public Dictionary<string, string> Args { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string Description { get; set; } = string.Empty;
        public string OutputRef { get; set; } = string.Empty;

        /// <summary>
        /// Optional nested sub-plan for this step. When set, the step is executed
        /// as a mini-plan: each sub-step is executed sequentially before the
        /// parent step's own tool call runs. Used for deep hierarchical planning
        /// in Engineer mode.
        /// </summary>
        public TaskPlan? SubPlan { get; set; }

        // Match properties mapping legacy Plan references
        public string? Command { get; set; }
        public string? Agent { get; set; }
        public Dictionary<string, string>? Arguments { get; set; }
        public bool Apply { get; set; } = false;
        public AgentResult? Result { get; set; }
    }
}
