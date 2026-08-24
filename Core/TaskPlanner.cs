using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    /// <summary>
    /// Layer 2 of the Router-Worker pipeline.
    /// 
    /// A pure, domain-agnostic compilation engine. It delegates 100% of the planning
    /// intelligence to the LLM. It maps available runtime metadata into a structured 
    /// planning prompt, executes the call, and parses the output into a deterministic TaskPlan.
    /// </summary>
    public class TaskPlanner
    {
        private readonly IModelClient _model;

        public TaskPlanner(IModelClient model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
        }

        /// <summary>
        /// Compiles a dynamic step-by-step TaskPlan using the LLM based on intent and available capabilities.
        /// </summary>
        public async Task<TaskPlan?> TryPlanAsync(
            IntentClassification intent,
            string userIntent,
            IEnumerable<AgentDescriptor> tools,
            CancellationToken ct)
        {
            // Enforce structural routing boundaries set by Layer 1
            if (intent.Shape == PlanShape.DirectAnswer || intent.Shape == PlanShape.ImplementationPlan)
                return null;

            if (intent.Type == IntentType.Conversational || intent.Type == IntentType.Unknown)
                return null;

            // Handle DeepContextExpansion: create a plan for 3-level deep file creation
            if (intent.Shape == PlanShape.DeepContextExpansion)
            {
                return await TryDeepContextExpansionPlanAsync(intent, userIntent, tools, ct);
            }

            try
            {
                // Task planning is budgeted tightly to keep overall agent response latency low
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

                var toolSummary = BuildToolSummary(tools);
                var prompt = BuildPlanningPrompt(intent, userIntent, toolSummary);

                const string systemPrompt =
                    "You are an executive task planner for an AI system. " +
                    "Output ONLY a valid JSON object matching the schema shown. " +
                    "No prose, no markdown code block backticks (```json), no conversational commentary.";

                var response = await _model.CompleteAsync(prompt, linked.Token, systemPrompt, ModelRole.Primary).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(response.Content))
                    return null;

                return ParsePlan(intent.Type, userIntent, response.Content);
            }
            catch
            {
                // Fallback to the unplanned reasoning execution loop on model failure or timeout
                return null;
            }
        }

        // ── Agnostic Prompt Assembly ─────────────────────────────────────────

        private static string BuildToolSummary(IEnumerable<AgentDescriptor> tools)
        {
            var sb = new StringBuilder();
            foreach (var t in tools)
            {
                var verbs = t.Actions?.Select(a => a.Name) ?? t.SupportedVerbs ?? Array.Empty<string>();
                sb.AppendLine($"- {t.Name}: {string.Join(", ", verbs)}");
                if (t.Actions != null)
                {
                    foreach (var action in t.Actions.Where(a => !string.IsNullOrEmpty(a.Description)))
                    {
                        sb.AppendLine($"  * {action.Name}: {action.Description}");
                    }
                }
            }
            return sb.ToString();
        }

        private static string BuildPlanningPrompt(IntentClassification intent, string userIntent, string toolSummary)
        {
            return $@"TASK: {userIntent}
ROUTING INTENT CATEGORY: {intent.Type}

AVAILABLE CAPABILITIES AND TOOLS:
{toolSummary}

DATA PASSING SYSTEM:
You can chain steps together by passing the output of a prior step into the arguments of a subsequent step.
Reference the 1-indexed output of a step using the format ""$stepN"" (e.g., ""$step1"", ""$step2"").

OUTPUT FORMAT SCHEMA (REQUIRED):
{{
  ""summary"": ""A high-level single-sentence description of the compiled plan approach."",
  ""steps"": [
    {{
      ""tool"": ""target_agent_name"",
      ""verb"": ""target_operation_name"",
      ""args"": {{ ""parameter_key"": ""parameter_value"" }},
      ""description"": ""A clear sentence stating what this step will achieve.""
    }}
  ]
}}

CONSTRAINTS:
1. Use as few steps as strictly necessary — 1 step is valid if the task is simple. Maximum 5 steps.
2. Never reference or generate tool names or verbs that are not explicitly defined in the directory above.
3. Keep step descriptions high-level and goal-oriented. Do NOT guess line numbers, code snippets, or parameters for files you have not read yet.";
        }

        // ── Structured Serialization Verification ────────────────────────────

        private static TaskPlan? ParsePlan(IntentType intent, string userIntent, string content)
        {
            if (!PlanParser.TryExtractJson(content, out var json))
                return null;

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var rawPlan = JsonSerializer.Deserialize<RawPlan>(json, options);
                if (rawPlan?.Steps == null || rawPlan.Steps.Count == 0)
                    return null;

                var validatedSteps = new List<PlannedStep>();
                for (int i = 0; i < rawPlan.Steps.Count; i++)
                {
                    var step = rawPlan.Steps[i];
                    if (string.IsNullOrWhiteSpace(step.Tool) || string.IsNullOrWhiteSpace(step.Verb))
                        continue;

                    validatedSteps.Add(new PlannedStep
                    {
                        Tool = step.Tool.Trim(),
                        Verb = step.Verb.Trim(),
                        Args = step.Args ?? new Dictionary<string, string>(),
                        Description = step.Description,
                        OutputRef = step.OutputRef ?? $"$step{i + 1}"
                    });
                }

                if (validatedSteps.Count == 0) return null;

                return new TaskPlan
                {
                    Intent = intent,
                    Summary = rawPlan.Summary ?? userIntent,
                    Steps = validatedSteps
                };
            }
            catch
            {
                return null;
            }
        }

        // ── Data Transfer Objects ───────────────────────────────────────

        private class RawPlan
        {
            public string? Summary { get; set; }
            public List<RawStep>? Steps { get; set; }
        }

        private class RawStep
        {
            public string? Tool { get; set; }
            public string? Verb { get; set; }
            public Dictionary<string, string>? Args { get; set; }
            public string? Description { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("output_ref")]
            public string? OutputRef { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("sub_steps")]
            public List<RawStep>? SubSteps { get; set; }
        }

        // ── Deep Plan (Engineer Mode) ────────────────────────────────────────

        /// <summary>
        /// Generates a hierarchical plan with nested sub-plans for complex tasks.
        /// Used when ResponseStyle is Engineer and the prompt warrants extensive planning.
        /// Each top-level step can contain its own sub-steps.
        /// </summary>
        public async Task<TaskPlan?> TryDeepPlanAsync(
            IntentClassification intent,
            string userIntent,
            IEnumerable<AgentDescriptor> tools,
            CancellationToken ct)
        {
            if (intent.Type == IntentType.Conversational || intent.Type == IntentType.Unknown)
                return null;

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

                var toolSummary = BuildToolSummary(tools);
                var prompt = BuildDeepPlanningPrompt(intent, userIntent, toolSummary);

                const string systemPrompt =
                    "You are a senior software architect planning a complex engineering task. " +
                    "Break the work into phases. Each phase can have sub-tasks for detailed execution. " +
                    "Output ONLY a valid JSON object matching the schema shown. " +
                    "No prose, no markdown code block backticks, no conversational commentary.";

                var response = await _model.CompleteAsync(prompt, linked.Token, systemPrompt, ModelRole.Primary).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(response.Content))
                    return null;

                return ParseDeepPlan(intent.Type, userIntent, response.Content);
            }
            catch
            {
                return null;
            }
        }

        // ── Deep Context Expansion (Improve Mode) ────────────────────────────────────────

        /// <summary>
        /// Generates a plan for deep context expansion when user says "improve".
        /// Creates a 3-level deep file creation plan:
        /// Level 1: Main requested files
        /// Level 2: Files referenced in Level 1 files
        /// Level 3: Files referenced in Level 2 files
        /// </summary>
        public async Task<TaskPlan?> TryDeepContextExpansionPlanAsync(
            IntentClassification intent,
            string userIntent,
            IEnumerable<AgentDescriptor> tools,
            CancellationToken ct)
        {
            if (intent.Type != IntentType.Improve)
                return null;

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

                var toolSummary = BuildToolSummary(tools);
                var prompt = BuildDeepContextExpansionPrompt(userIntent, toolSummary);

                const string systemPrompt =
                    "You are an expert in recursive file creation and deep context expansion. " +
                    "When a user says 'improve' or 'enhance', you must create a plan that generates " +
                    "all referenced files up to 3 levels deep. " +
                    "Level 1: Main requested files. " +
                    "Level 2: Files referenced in Level 1 files. " +
                    "Level 3: Files referenced in Level 2 files. " +
                    "Output ONLY a valid JSON object matching the schema shown. " +
                    "No prose, no markdown code block backticks, no conversational commentary.";

                var response = await _model.CompleteAsync(prompt, linked.Token, systemPrompt, ModelRole.Primary).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(response.Content))
                    return null;

                return ParseDeepContextExpansionPlan(userIntent, response.Content);
            }
            catch
            {
                return null;
            }
        }

        private static string BuildDeepContextExpansionPrompt(string userIntent, string toolSummary)
        {
            return $@"TASK: {userIntent}
PLANNING MODE: Deep Context Expansion (3-level recursive file creation)

AVAILABLE CAPABILITIES AND TOOLS:
{toolSummary}

OUTPUT FORMAT SCHEMA (REQUIRED):
{{
  ""summary"": ""A high-level description of the 3-level deep file creation plan."",
  ""steps"": [
    {{
      ""tool"": ""code_editor"",
      ""verb"": ""write_file"",
      ""args"": {{ ""path"": ""file_path"", ""content"": ""file_content"" }},
      ""description"": ""Level 1: Create main requested file"",
      ""sub_steps"": [
        {{
          ""tool"": ""code_editor"",
          ""verb"": ""write_file"",
          ""args"": {{ ""path"": ""referenced_file_path"", ""content"": ""referenced_file_content"" }},
          ""description"": ""Level 2: Create files referenced in Level 1""
        }},
        {{
          ""tool"": ""code_editor"",
          ""verb"": ""write_file"",
          ""args"": {{ ""path"": ""deep_referenced_file_path"", ""content"": ""deep_referenced_file_content"" }},
          ""description"": ""Level 3: Create files referenced in Level 2""
        }}
      ]
    }}
  ]
}}

PLANNING GUIDELINES (DEEP CONTEXT EXPANSION):
1. Analyze the user's request to identify all files that need to be created.
2. For Level 1 files: Create the main requested files with full content.
3. For Level 2 files: Identify all references (links, imports, includes) in Level 1 files and create those files.
4. For Level 3 files: Identify all references in Level 2 files and create those files.
5. Stop at Level 3 - do not go deeper.
6. Each file should have appropriate content for its level:
   - Level 1: Full implementation
   - Level 2: Functional stubs with basic structure
   - Level 3: Minimal stubs with placeholder content
7. Use sub_steps to organize the hierarchical file creation.
8. Include file paths and content in the args for each step.
9. Maximum 5 Level 1 files, maximum 3 Level 2 files per Level 1 file, maximum 2 Level 3 files per Level 2 file.";
        }

        private static TaskPlan? ParseDeepContextExpansionPlan(string userIntent, string content)
        {
            if (!PlanParser.TryExtractJson(content, out var json))
                return null;

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var rawPlan = JsonSerializer.Deserialize<RawPlan>(json, options);
                if (rawPlan?.Steps == null || rawPlan.Steps.Count == 0)
                    return null;

                var validatedSteps = new List<PlannedStep>();
                for (int i = 0; i < rawPlan.Steps.Count; i++)
                {
                    var step = rawPlan.Steps[i];
                    if (string.IsNullOrWhiteSpace(step.Tool) || string.IsNullOrWhiteSpace(step.Verb))
                        continue;

                    var plannedStep = new PlannedStep
                    {
                        Tool = step.Tool.Trim(),
                        Verb = step.Verb.Trim(),
                        Args = step.Args ?? new Dictionary<string, string>(),
                        Description = step.Description ?? $"Level 1: File creation {i + 1}",
                        OutputRef = step.OutputRef ?? $"$step{i + 1}"
                    };

                    // Parse sub-steps for Level 2 and Level 3 files
                    if (step.SubSteps != null && step.SubSteps.Count > 0)
                    {
                        var subSteps = new List<PlannedStep>();
                        for (int j = 0; j < step.SubSteps.Count; j++)
                        {
                            var sub = step.SubSteps[j];
                            if (string.IsNullOrWhiteSpace(sub.Tool) || string.IsNullOrWhiteSpace(sub.Verb))
                                continue;

                            var subStep = new PlannedStep
                            {
                                Tool = sub.Tool.Trim(),
                                Verb = sub.Verb.Trim(),
                                Args = sub.Args ?? new Dictionary<string, string>(),
                                Description = sub.Description ?? $"Level 2/3: File creation {j + 1}",
                                OutputRef = sub.OutputRef ?? $"sub_{i + 1}_{j + 1}"
                            };

                            // Parse Level 3 sub-sub-steps if present
                            if (sub.SubSteps != null && sub.SubSteps.Count > 0)
                            {
                                var subSubSteps = new List<PlannedStep>();
                                for (int k = 0; k < sub.SubSteps.Count; k++)
                                {
                                    var subSub = sub.SubSteps[k];
                                    if (string.IsNullOrWhiteSpace(subSub.Tool) || string.IsNullOrWhiteSpace(subSub.Verb))
                                        continue;

                                    subSubSteps.Add(new PlannedStep
                                    {
                                        Tool = subSub.Tool.Trim(),
                                        Verb = subSub.Verb.Trim(),
                                        Args = subSub.Args ?? new Dictionary<string, string>(),
                                        Description = subSub.Description ?? $"Level 3: File creation {k + 1}",
                                        OutputRef = subSub.OutputRef ?? $"sub_{i + 1}_{j + 1}_{k + 1}"
                                    });
                                }

                                if (subSubSteps.Count > 0)
                                {
                                    subStep.SubPlan = new TaskPlan
                                    {
                                        Intent = IntentType.Improve,
                                        Summary = $"Level 3 files for: {subStep.Description}",
                                        Steps = subSubSteps
                                    };
                                }
                            }

                            subSteps.Add(subStep);
                        }

                        if (subSteps.Count > 0)
                        {
                            plannedStep.SubPlan = new TaskPlan
                            {
                                Intent = IntentType.Improve,
                                Summary = $"Level 2 files for: {plannedStep.Description}",
                                Steps = subSteps
                            };
                        }
                    }

                    validatedSteps.Add(plannedStep);
                }

                if (validatedSteps.Count == 0) return null;

                return new TaskPlan
                {
                    Intent = IntentType.Improve,
                    Summary = rawPlan.Summary ?? $"Deep context expansion: {userIntent}",
                    Steps = validatedSteps
                };
            }
            catch
            {
                return null;
            }
        }

        private static string BuildDeepPlanningPrompt(IntentClassification intent, string userIntent, string toolSummary)
        {
            return $@"TASK: {userIntent}
ROUTING INTENT CATEGORY: {intent.Type}
PLANNING MODE: Deep hierarchical (Engineer mode — extensive, professional plan)

AVAILABLE CAPABILITIES AND TOOLS:
{toolSummary}

DATA PASSING SYSTEM:
You can chain steps together by passing the output of a prior step into the arguments of a subsequent step.
Reference the 1-indexed output of a step using the format ""$stepN"" (e.g., ""$step1"", ""$step2"").

OUTPUT FORMAT SCHEMA (REQUIRED):
{{
  ""summary"": ""A high-level single-sentence description of the overall plan approach."",
  ""steps"": [
    {{
      ""tool"": ""target_agent_name"",
      ""verb"": ""target_operation_name"",
      ""args"": {{ ""parameter_key"": ""parameter_value"" }},
      ""description"": ""A clear sentence stating what this phase will achieve."",
      ""sub_steps"": [
        {{
          ""tool"": ""target_agent_name"",
          ""verb"": ""target_operation_name"",
          ""args"": {{ ""parameter_key"": ""parameter_value"" }},
          ""description"": ""Detailed sub-task description.""
        }}
      ]
    }}
  ]
}}

PLANNING GUIDELINES (ENGINEER MODE):
1. Generate only the phases that this specific task actually requires. Minimum 1, maximum 8. Do NOT pad with phases the task does not need.
2. Complex phases MUST include sub_steps (2-5 sub-tasks each) for detailed execution.
3. Simple phases (like ""read a file"") can have an empty or missing sub_steps array.
4. Each phase should be a logically cohesive unit of work.
5. Sub-steps should be specific, actionable tool calls that directly contribute to the parent phase.
6. Order phases logically: analyze before implementing, test before finalizing.
7. Reference outputs between steps using $stepN notation where data flows between phases.
8. Never reference tool names or verbs not defined in the directory above.
9. Maximum 8 top-level steps, maximum 5 sub-steps per phase.";
        }

        private static TaskPlan? ParseDeepPlan(IntentType intent, string userIntent, string content)
        {
            if (!PlanParser.TryExtractJson(content, out var json))
                return null;

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var rawPlan = JsonSerializer.Deserialize<RawPlan>(json, options);
                if (rawPlan?.Steps == null || rawPlan.Steps.Count == 0)
                    return null;

                var validatedSteps = new List<PlannedStep>();
                for (int i = 0; i < rawPlan.Steps.Count; i++)
                {
                    var step = rawPlan.Steps[i];
                    if (string.IsNullOrWhiteSpace(step.Tool) || string.IsNullOrWhiteSpace(step.Verb))
                        continue;

                    var plannedStep = new PlannedStep
                    {
                        Tool = step.Tool.Trim(),
                        Verb = step.Verb.Trim(),
                        Args = step.Args ?? new Dictionary<string, string>(),
                        Description = step.Description ?? $"Phase {i + 1}",
                        OutputRef = step.OutputRef ?? $"$step{i + 1}"
                    };

                    // Parse sub-steps if present
                    if (step.SubSteps != null && step.SubSteps.Count > 0)
                    {
                        var subSteps = new List<PlannedStep>();
                        for (int j = 0; j < step.SubSteps.Count; j++)
                        {
                            var sub = step.SubSteps[j];
                            if (string.IsNullOrWhiteSpace(sub.Tool) || string.IsNullOrWhiteSpace(sub.Verb))
                                continue;

                            subSteps.Add(new PlannedStep
                            {
                                Tool = sub.Tool.Trim(),
                                Verb = sub.Verb.Trim(),
                                Args = sub.Args ?? new Dictionary<string, string>(),
                                Description = sub.Description ?? $"Sub-task {j + 1}",
                                OutputRef = sub.OutputRef ?? $"sub_{i + 1}_{j + 1}"
                            });
                        }

                        if (subSteps.Count > 0)
                        {
                            plannedStep.SubPlan = new TaskPlan
                            {
                                Intent = intent,
                                Summary = $"Sub-plan for: {plannedStep.Description}",
                                Steps = subSteps
                            };
                        }
                    }

                    validatedSteps.Add(plannedStep);
                }

                if (validatedSteps.Count == 0) return null;

                return new TaskPlan
                {
                    Intent = intent,
                    Summary = rawPlan.Summary ?? userIntent,
                    Steps = validatedSteps
                };
            }
            catch
            {
                return null;
            }
        }
    }
}