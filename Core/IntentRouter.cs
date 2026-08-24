using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    /// <summary>
    /// Layer 1 of the Router-Worker pipeline.
    /// 
    /// A pure, domain-agnostic classification router. It evaluates minimal structural properties
    /// before delegating intent and plan shape classification exclusively to the LLM brain.
    /// </summary>
    public class IntentRouter
    {
        private readonly IModelClient _model;

        public IntentRouter(IModelClient model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
        }

        /// <summary>
        /// Classifies the user's raw message into a system intent type and structural execution plan shape.
        /// </summary>
        public async Task<IntentClassification> ClassifyAsync(string userIntent, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userIntent))
            {
                return CreateClassification(
                    IntentType.Conversational,
                    PlanShape.DirectAnswer,
                    "Empty message provided.",
                    "No content",
                    1.0f,
                    true);
            }

            // Quick structural validation bypassing the model for ultra-short strings
            var normalized = userIntent.Trim();
            if (normalized.Length <= 3)
            {
                return CreateClassification(
                    IntentType.Conversational,
                    PlanShape.DirectAnswer,
                    "Input minimal length guard matched.",
                    "Too brief for complex task orchestration.",
                    1.0f,
                    true);
            }

            // Check for explicit deep context expansion triggers
            var expandKeywords = new[] { "deep expansion", "expand context", "build out all references", "recursive build", "deep context expansion" };
            bool isImproveIntent = expandKeywords.Any(k => normalized.Contains(k, StringComparison.OrdinalIgnoreCase));
            
            if (isImproveIntent)
            {
                return CreateClassification(
                    IntentType.Improve,
                    PlanShape.DeepContextExpansion,
                    "Deep context expansion requested: will create all referenced files up to 3 levels deep.",
                    "DeepContextExpansion for recursive file creation",
                    1.0f,
                    true);
            }

            try
            {
                // Strict latency budget for route classification
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

                const string systemPrompt =
                    "You are a high-speed routing classifier for an automated agent framework.\n" +
                    "Analyze the user's message and output EXACTLY two words separated by a single space.\n\n" +
                    "WORD 1 (Intent category):\n" +
                    "  CHAT      - Casual conversational greetings, pleasantries, or basic interactions.\n" +
                    "  ANALYSIS  - Inspecting, searching, auditing, reading, or processing data.\n" +
                    "  EDIT      - Making updates, additions, adjustments, refactoring, or code changes.\n" +
                    "  SHELL     - Direct execution commands, script operations, compiling, or building.\n" +
                    "  KNOWLEDGE - Deep contextual explanations, architectural inquiries, or concept summaries.\n" +
                    "  STATS     - Computing line counts, sizes, file quantities, or descriptive metrics.\n" +
                    "  UNKNOWN   - Unclear intents that require generalized adaptive execution.\n\n" +
                    "WORD 2 (Execution plan shape structure):\n" +
                    "  DIRECT    - Basic textual output required. No tools or step planning are needed. Examples: 'hello', 'what is X', 'explain Y'.\n" +
                    "  TASKLIST  - A precise, flat, single-phase target recipe (1-5 consecutive steps max). Examples: 'read this file', 'run dotnet build', 'search for X', 'show me the contents of Y'.\n" +
                    "  PLAN      - A large, complex, multi-file or multi-phase dependency roadmap requiring user approval before execution. Use PLAN when: the task touches more than 2 files, involves refactoring or new features, has uncertain or open-ended scope, requires architectural decisions, or involves scaffolding. Examples: 'add a new feature to X', 'refactor the Y system', 'implement Z across multiple files', 'redesign how A works', 'fix this complex bug across the codebase'.\n\n" +
                    "CRITICAL: Output absolutely nothing else. No punctuation, no markdown syntax formatting block wrappers.";

                var response = await _model.CompleteAsync(
                    $"User message: \"{normalized}\"\nClassify:",
                    linked.Token,
                    systemPrompt,
                    ModelRole.Primary).ConfigureAwait(false);

                var content = response.Content ?? "";
                content = System.Text.RegularExpressions.Regex.Replace(content, @"<think>.*?</think>", "", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                content = System.Text.RegularExpressions.Regex.Replace(content, @"<thought>.*?</thought>", "", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                var tokens = content.Trim().ToUpperInvariant()
                    .Split(new[] { ' ', '\n', '\r', '\t', ',', '.', ';' }, StringSplitOptions.RemoveEmptyEntries);

                var validIntents = new HashSet<string> { "CHAT", "ANALYSIS", "EDIT", "SHELL", "KNOWLEDGE", "STATS", "UNKNOWN" };
                var validShapes = new HashSet<string> { "DIRECT", "TASKLIST", "PLAN" };

                var intentToken = "UNKNOWN";
                var shapeToken = "TASKLIST";

                for (int i = tokens.Length - 1; i >= 0; i--)
                {
                    var cleanToken = new string(tokens[i].Where(char.IsLetter).ToArray());
                    if (intentToken == "UNKNOWN" && validIntents.Contains(cleanToken))
                        intentToken = cleanToken;
                    if (shapeToken == "TASKLIST" && validShapes.Contains(cleanToken))
                        shapeToken = cleanToken;
                }

                var finalType = intentToken switch
                {
                    "CHAT" => IntentType.Conversational,
                    "ANALYSIS" => IntentType.CodeAnalysis,
                    "EDIT" => IntentType.CodeEdit,
                    "SHELL" => IntentType.ShellTask,
                    "KNOWLEDGE" => IntentType.KnowledgeQuery,
                    "STATS" => IntentType.FileStatsCount,
                    _ => IntentType.Unknown
                };

                var finalShape = shapeToken switch
                {
                    "DIRECT" => PlanShape.DirectAnswer,
                    "TASKLIST" => PlanShape.TaskList,
                    "PLAN" => PlanShape.ImplementationPlan,
                    _ => PlanShape.TaskList
                };

                // Structural guarantee override: chat interactions never generate plan steps
                if (finalType == IntentType.Conversational)
                {
                    finalShape = PlanShape.DirectAnswer;
                }

                // Heuristic guard: only allow ImplementationPlan for genuinely large tasks.
                // Small LLMs over-classify simple requests as PLAN. If the message is short
                // and contains no explicit multi-file / architectural keywords, downgrade to TaskList.
                if (finalShape == PlanShape.ImplementationPlan)
                {
                    var massiveTaskKeywords = new[]
                    {
                        "refactor", "redesign", "migrate", "overhaul", "rewrite",
                        "across all", "across the entire", "across every",
                        "all files", "all classes", "all components",
                        "new architecture", "new system", "new module",
                        "plugin system", "plugin architecture"
                    };
                    bool looksLarge = normalized.Length > 150
                        || massiveTaskKeywords.Any(k => normalized.Contains(k, StringComparison.OrdinalIgnoreCase));

                    if (!looksLarge)
                    {
                        finalShape = PlanShape.TaskList;
                    }
                }

                return CreateClassification(
                    finalType,
                    finalShape,
                    $"Dynamic LLM route optimization classification resolved to '{intentToken}'.",
                    $"LLM architecture parsing profile shape matched to '{shapeToken}'.",
                    0.95f,
                    false);
            }
            catch (OperationCanceledException)
            {
                return CreateClassification(IntentType.Unknown, PlanShape.TaskList, "Model router classification timeout expired.", "Fallback default router active.", 0.0f, false);
            }
            catch (Exception ex)
            {
                return CreateClassification(IntentType.Unknown, PlanShape.TaskList, $"Model router failed: {ex.Message}", "Fallback default router active.", 0.0f, false);
            }
        }

        private static IntentClassification CreateClassification(
            IntentType type,
            PlanShape shape,
            string reasoning,
            string complexityReason,
            float confidence,
            bool isHeuristic)
        {
            return new IntentClassification
            {
                Type = type,
                Shape = shape,
                Reasoning = reasoning,
                ComplexityReason = complexityReason,
                Confidence = confidence,
                IsHeuristic = isHeuristic
            };
        }
    }
}