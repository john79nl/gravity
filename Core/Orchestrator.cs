using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Gravity.Core.Agents;

namespace Gravity.Core
{
    // ─────────────────────────────────────────────
    // Environment context injected into every agent
    // ─────────────────────────────────────────────
    public sealed class EnvironmentContext
    {
        public string OS { get; init; } = "Unknown";
        public string Shell { get; init; } = "bash";
        public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

        public static EnvironmentContext Detect(string? rootOverride = null) => new()
        {
            OS = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : "Linux",
            Shell = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "PowerShell" : "bash",
            WorkingDirectory = rootOverride ?? Directory.GetCurrentDirectory()
        };

        public override string ToString() =>
            $"OS={OS} | Shell={Shell} | WorkDir={WorkingDirectory}";
    }

    // ─────────────────────────────────────────────
    // Parsed result from a single model response
    // ─────────────────────────────────────────────
    public enum ModelResponseKind { ToolCall, Final, Conversational, ParseError }

    public sealed class ParsedModelResponse
    {
        public ModelResponseKind Kind { get; init; }

        // ToolCall fields
        public string? ToolCallId { get; init; }
        public string? ToolName { get; init; }
        public Dictionary<string, object>? ToolArguments { get; init; }

        // Final / Conversational fields
        public string? Output { get; init; }

        // ParseError fields
        public string? RawContent { get; init; }
        public string? ParseErrorReason { get; init; }

        public static ParsedModelResponse AsToolCall(string? id, string name, Dictionary<string, object> args) =>
            new() { Kind = ModelResponseKind.ToolCall, ToolCallId = id, ToolName = name, ToolArguments = args };

        public static ParsedModelResponse AsFinal(string output) =>
            new() { Kind = ModelResponseKind.Final, Output = output };

        public static ParsedModelResponse AsConversational(string output) =>
            new() { Kind = ModelResponseKind.Conversational, Output = output };

        public static ParsedModelResponse AsError(string raw, string reason) =>
            new() { Kind = ModelResponseKind.ParseError, RawContent = raw, ParseErrorReason = reason };
    }

    // ─────────────────────────────────────────────
    // Orchestrator
    // ─────────────────────────────────────────────
    public class Orchestrator
    {
        private readonly IModelClient _model;
        private readonly IRouterService _router;
        private readonly ProjectContext _projectContext;
        private readonly IKnowledgeService _knowledgeService;
        private readonly ISettingsService _settingsService;
        private readonly IArtifactService _artifactService;
        private readonly IRagService _ragService;
        private readonly IntentRouter _intentRouter;
        private readonly TaskPlanner _taskPlanner;
        private readonly EnvironmentContext _env;

        private readonly ConcurrentDictionary<string, AppEngine> _agentPool = new();
        private const int MAX_CONCURRENT_AGENTS = 5;

        // Fast-path regexes — read-only, never destructive
        private static readonly Regex FileReadRegex = new(
            @"^(?:show\s+me\s+(?:the\s+content\s+of\s+)?|read\s+|view\s+|cat\s+)([\w\-\.\/\\\:]+\.\w+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DirListRegex = new(
            @"^(?:list\s+(?:files\s+in\s+)?|ls\s+|dir\s+)([\w\-\.\/\\\:]*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Patterns used to parse raw model content
        private static readonly Regex JsonFenceRegex = new(
            @"```(?:json)?\s*(\{[\s\S]*?\})\s*```",
            RegexOptions.Compiled);

        private static readonly Regex BareJsonRegex = new(@"^\s*(\{[\s\S]*\})\s*$", RegexOptions.Compiled);

        public static ParsedModelResponse Parse(string rawContent, IReadOnlyList<ToolCallRaw>? nativeToolCalls)
        {
            // Keep your exact priority parsing logic here...
            // Ensure TryParseArguments maps cleanly to actual underlying primitives rather than forced strings
            return ParsedModelResponse.AsConversational(rawContent.Trim());
        }

        public event Action<AppEngine>? OnAgentSpawned;
        public event Action<AppEngine>? OnAgentFinished;

        public Orchestrator(
            IModelClient model,
            IRouterService router,
            ProjectContext projectContext,
            IKnowledgeService knowledgeService,
            ISettingsService settingsService,
            IArtifactService artifactService,
            IRagService ragService,
            IntentRouter intentRouter,
            TaskPlanner taskPlanner,
            EnvironmentContext? environmentContext = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
            _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _artifactService = artifactService ?? throw new ArgumentNullException(nameof(artifactService));
            _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
            _intentRouter = intentRouter ?? throw new ArgumentNullException(nameof(intentRouter));
            _taskPlanner = taskPlanner ?? throw new ArgumentNullException(nameof(taskPlanner));

            // Auto-detect environment if not provided
            _env = environmentContext ?? EnvironmentContext.Detect(_projectContext?.ProjectPath);
        }

        // ── Public surface ────────────────────────────────────────────────────

        public IAgentService AsService() => new OrchestratorAgentService(this);
        public IEnumerable<AgentDescriptor> GetToolDescriptors() => _router.GetAgentDescriptors();
        public EnvironmentContext Environment => _env;

        public void StopAgent(string id) { if (_agentPool.TryGetValue(id, out var a)) a.Stop(); }
        public void RemoveAgent(string id) { if (_agentPool.TryRemove(id, out var a)) a.Stop(); }
        public IEnumerable<AppEngine> GetActiveAgents() => _agentPool.Values;
        public void ClearPool() { foreach (var a in _agentPool.Values) a.Stop(); _agentPool.Clear(); }

        // ── Agent spawning ────────────────────────────────────────────────────

        public AppEngine SpawnAgent(string intent)
        {
            // Evict a finished engine if pool is full
            if (_agentPool.Count >= MAX_CONCURRENT_AGENTS)
            {
                var staleKey = _agentPool
                    .FirstOrDefault(kv => kv.Value.Status is AgentStatus.Finished or AgentStatus.Error)
                    .Key;

                if (staleKey != null)
                    _agentPool.TryRemove(staleKey, out _);
                else
                    throw new InvalidOperationException(
                        $"Engine pool is full ({MAX_CONCURRENT_AGENTS} active). Stop a running engine first.");
            }

            var id = "engine_" + Guid.NewGuid().ToString("n")[..8];
            _env.WorkingDirectory = _projectContext.ProjectDirectory;
            var engine = new AppEngine(
                _router,
                _settingsService,
                _artifactService,
                _ragService,
                _knowledgeService,
                _projectContext,
                _model)
            {
                Id = id,
                UserIntent = intent,
                EnvironmentContext = _env
            };

            _agentPool[id] = engine;
            OnAgentSpawned?.Invoke(engine);

            engine.StatusChanged += (_, status) =>
            {
                if (status is AgentStatus.Finished or AgentStatus.Error)
                    OnAgentFinished?.Invoke(engine);
            };

            // Background RAG refresh — only for non-trivial intents
            if (!IsSimpleReadIntent(intent))
            {
                _ = Task.Run(async () =>
                {
                    try { await _ragService.RefreshIndexAsync(); }
                    catch { /* Background refresh is best-effort */ }
                });
            }

            return engine;
        }

        public AppEngine SpawnAgent(string intent, string ownerTag)
        {
            var engine = SpawnAgent(intent);
            engine.OwnerTag = ownerTag;
            return engine;
        }

        // ── Classify + plan ───────────────────────────────────────────────────

        public async Task<(AppEngine Agent, IntentClassification Intent, TaskPlan? Plan)>
            ClassifyAndPlanAsync(string userIntent, CancellationToken ct)
        {
            // Fast path: simple read/list commands bypass the model entirely
            if (TryExecuteFastPath(userIntent, out var fastPlan, out var fastIntent))
            {
                var fastEngine = SpawnAgent(userIntent);
                fastEngine.Status = AgentStatus.Running;

                try
                {
                    fastEngine.FinalOutput = await ExecuteDirectActionAsync(fastPlan!.Steps[0], ct);
                    fastEngine.Status = AgentStatus.Finished;
                }
                catch (Exception ex)
                {
                    fastEngine.FinalOutput = $"Fast-path execution failed: {ex.Message}";
                    fastEngine.Status = AgentStatus.Error;
                }

                return (fastEngine, fastIntent!, fastPlan);
            }

            var planningMode = _settingsService.Current.PlanningMode;

            // ── FreeForm: zero pre-processing, raw intent straight to the agent loop ──
            if (planningMode == PlanningMode.FreeForm)
            {
                var freeEngine = SpawnAgent(userIntent);
                freeEngine.CurrentClassification = new IntentClassification
                {
                    Type = IntentType.Unknown,
                    Shape = PlanShape.TaskList,
                    ComplexityReason = "FreeForm mode: no pre-processing",
                    Confidence = 1.0f
                };
                freeEngine.TaskState = AgentTaskState.Executing;
                return (freeEngine, freeEngine.CurrentClassification, null);
            }

            // ── Adaptive: classify intent type for context seed; skip Clarify + TryPlan ──
            if (planningMode == PlanningMode.Adaptive)
            {
                var adaptiveIntent = await _intentRouter.ClassifyAsync(userIntent, ct);
                var adaptiveEngine = SpawnAgent(userIntent);
                adaptiveEngine.CurrentClassification = adaptiveIntent;

                // For conversational intents, route directly without the tool loop
                if (adaptiveIntent.Type == IntentType.Conversational)
                {
                    adaptiveEngine.CurrentClassification = new IntentClassification
                    {
                        Type = IntentType.Conversational,
                        Shape = PlanShape.DirectAnswer,
                        Confidence = 1.0f
                    };
                }

                adaptiveEngine.TaskState = AgentTaskState.Executing;
                return (adaptiveEngine, adaptiveIntent, null);
            }

            // ── PrePlanned: legacy full pipeline (Clarify → Classify → TryPlan) ──
            // Clarify complex intent before routing — keeps the planner focused
            var clarifiedIntent = await ClarifyIntentAsync(userIntent, ct);

            var intent = await _intentRouter.ClassifyAsync(clarifiedIntent, ct);

            TaskPlan? plan = null;
            if (intent.Type is not IntentType.Conversational and not IntentType.Unknown)
            {
                // Handle Improve intent with DeepContextExpansion plan shape
                if (intent.Type == IntentType.Improve && intent.Shape == PlanShape.DeepContextExpansion)
                {
                    plan = await _taskPlanner.TryDeepContextExpansionPlanAsync(intent, clarifiedIntent, _router.GetAgentDescriptors(), ct);
                }
                // In Engineer mode, use deep hierarchical planning for complex tasks
                else
                {
                    bool isComplex = clarifiedIntent.Length > 150
                        || clarifiedIntent.Contains("refactor", StringComparison.OrdinalIgnoreCase)
                        || clarifiedIntent.Contains("redesign", StringComparison.OrdinalIgnoreCase)
                        || clarifiedIntent.Contains("implement", StringComparison.OrdinalIgnoreCase)
                        || clarifiedIntent.Contains("create", StringComparison.OrdinalIgnoreCase)
                        || clarifiedIntent.Contains("build", StringComparison.OrdinalIgnoreCase)
                        || clarifiedIntent.Contains("add feature", StringComparison.OrdinalIgnoreCase)
                        || clarifiedIntent.Contains("scaffold", StringComparison.OrdinalIgnoreCase);

                    if (isComplex)
                    {
                        intent = new IntentClassification
                        {
                            Type = intent.Type,
                            Shape = PlanShape.DeepPlan,
                            ComplexityReason = "Engineer mode: complex task → deep hierarchical plan",
                            Confidence = intent.Confidence
                        };
                        plan = await _taskPlanner.TryDeepPlanAsync(intent, clarifiedIntent, _router.GetAgentDescriptors(), ct);
                    }
                    else
                    {
                        plan = await _taskPlanner.TryPlanAsync(intent, clarifiedIntent, _router.GetAgentDescriptors(), ct);
                    }
                }
            }

            var engine = SpawnAgent(userIntent);
            engine.ClarifiedIntent = clarifiedIntent != userIntent ? clarifiedIntent : null;
            engine.CurrentClassification = intent;
            if (plan != null)
            {
                engine.SeedPlan(plan);
            }
            else if (intent.Shape == PlanShape.ImplementationPlan)
            {
                engine.TaskState = AgentTaskState.Planning;
            }
            else
            {
                engine.TaskState = AgentTaskState.Executing;
            }

            return (engine, intent, plan);
        }

        // ── Execute on an existing engine (no double spawn) ──────────────────

        public async Task<string> ExecuteWithEngineAsync(
            AppEngine engine,
            string userIntent,
            TaskPlan? preCalculatedPlan,
            CancellationToken ct,
            int maxSteps = 10)
        {
            if (preCalculatedPlan != null) engine.SeedPlan(preCalculatedPlan);

            // Deep plans need more steps since sub-plans consume steps recursively
            bool isDeep = preCalculatedPlan?.Steps != null
                && preCalculatedPlan.Steps.Any(s => s.SubPlan != null);
            int effectiveMax = isDeep ? Math.Max(maxSteps, 30) : maxSteps;

            if (preCalculatedPlan?.Steps != null && preCalculatedPlan.Steps.Count > 0)
            {
                engine.TaskState = AgentTaskState.Executing;
                for (int i = 0; i < preCalculatedPlan.Steps.Count; i++)
                {
                    if (ct.IsCancellationRequested) break;
                    var step = preCalculatedPlan.Steps[i];
                    await engine.ExecuteStepAsync(step, "agent_session.log", ct, effectiveMax);

                    if (engine.FinalOutput.Contains("I was unable to produce a valid response after several attempts", StringComparison.OrdinalIgnoreCase))
                        break;
                }
            }
            else
            {
                await engine.ExecuteAsync(userIntent, "agent_session.log", ct, effectiveMax);
            }

            return engine.FinalOutput;
        }

        // ── Main agent loop ───────────────────────────────────────────────────

        public async Task<string> AgentLoopAsync(
            string userIntent,
            TaskPlan? preCalculatedPlan,
            IProgress<string> logProgress,
            IProgress<string> streamProgress,
            CancellationToken ct,
            int maxSteps = 10)
        {
            var engine = SpawnAgent(userIntent);
            if (preCalculatedPlan != null) engine.SeedPlan(preCalculatedPlan);

            engine.LogEmitted    += (_, msg)   => logProgress.Report(msg);
            engine.StreamReceived += (_, token) => streamProgress.Report(token);

            logProgress.Report($"[Orchestrator] Environment: {_env}");
            logProgress.Report($"[Orchestrator] Starting engine {engine.Id} for intent: \"{userIntent}\"");

            if (preCalculatedPlan != null && preCalculatedPlan.Steps != null && preCalculatedPlan.Steps.Count > 0)
            {
                engine.TaskState = AgentTaskState.Executing;
                for (int i = 0; i < preCalculatedPlan.Steps.Count; i++)
                {
                    if (ct.IsCancellationRequested) break;
                    var step = preCalculatedPlan.Steps[i];
                    logProgress.Report($"[Orchestrator] Executing Plan Step {i + 1}/{preCalculatedPlan.Steps.Count}: {step.Description}");
                    await engine.ExecuteStepAsync(step, "agent_session.log", ct, maxSteps);
                    logProgress.Report($"[Orchestrator] Step {i + 1}/{preCalculatedPlan.Steps.Count} completed.");
                    
                    if (engine.FinalOutput.Contains("I was unable to produce a valid response after several attempts", StringComparison.OrdinalIgnoreCase))
                    {
                        logProgress.Report($"[Orchestrator] Step failed due to parse errors. Aborting plan.");
                        break;
                    }
                }
            }
            else
            {
                await engine.ExecuteAsync(userIntent, "agent_session.log", ct, maxSteps);
            }

            var output = engine.FinalOutput;

            if (string.IsNullOrWhiteSpace(output))
            {
                logProgress.Report("[Orchestrator] Engine returned empty output — treating as incomplete.");
                return "Engine finished without producing output.";
            }

            logProgress.Report($"[Orchestrator] Engine {engine.Id} completed. Output length: {output.Length} chars.");
            return output;
        }

        // ── Model response parser ─────────────────────────────────────────────
        //
        // This is the single authoritative place where raw model text is
        // interpreted.  Call this from AppEngine instead of doing ad-hoc
        // string matching in the loop.

        // Static wrapper so AppEngine can call without holding an Orchestrator reference
        public static ParsedModelResponse StaticParseModelResponse(
            string rawContent, IReadOnlyList<ToolCallRaw>? nativeToolCalls) =>
            _staticInstance.ParseModelResponse(rawContent, nativeToolCalls);

        // Lazy singleton used only for static parse access — no service deps needed
        private static readonly Orchestrator _staticInstance = new();
        private Orchestrator() { } // parameterless ctor for static singleton only

        public ParsedModelResponse ParseModelResponse(string rawContent, IReadOnlyList<ToolCallRaw>? nativeToolCalls)
        {
            if (!string.IsNullOrEmpty(rawContent))
            {
                rawContent = Regex.Replace(rawContent, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
                rawContent = Regex.Replace(rawContent, @"<thought>[\s\S]*?</thought>", "", RegexOptions.IgnoreCase).Trim();
            }

            // Priority 1: native tool calls returned by the API (most reliable)
            if (nativeToolCalls is { Count: > 0 })
            {
                var first = nativeToolCalls[0];

                if (string.IsNullOrWhiteSpace(first.Name))
                    return ParsedModelResponse.AsError(rawContent, "Native tool call has empty name.");

                var args = TryParseArguments(first.ArgumentsJson);
                if (args == null)
                    return ParsedModelResponse.AsError(rawContent, $"Could not parse native tool arguments: {first.ArgumentsJson}");

                if (first.Name.Equals("action.final", StringComparison.OrdinalIgnoreCase))
                {
                    var output = args.TryGetValue("output", out var o) ? o?.ToString() ?? string.Empty : string.Empty;
                    return ParsedModelResponse.AsFinal(output);
                }

                return ParsedModelResponse.AsToolCall(first.Id, first.Name, args);
            }

            // Priority 2: action.final — detect before trying JSON tool call
            var finalMatch = Regex.Match(rawContent,
                @"\[action\.final\]\s*(\{[\s\S]*?\})", RegexOptions.IgnoreCase);
            if (finalMatch.Success)
            {
                var finalArgs = TryParseArguments(finalMatch.Groups[1].Value);
                var output = finalArgs != null && finalArgs.TryGetValue("output", out var o)
                    ? o?.ToString() ?? string.Empty
                    : finalMatch.Groups[1].Value;
                return ParsedModelResponse.AsFinal(output);
            }

            // Priority 3: JSON inside markdown fences  ```json { ... } ```
            var fenceMatch = JsonFenceRegex.Match(rawContent);
            if (fenceMatch.Success)
                return TryBuildToolCallResponse(fenceMatch.Groups[1].Value, rawContent);

            // Priority 4: bare JSON object
            var bareMatch = BareJsonRegex.Match(rawContent);
            if (bareMatch.Success)
                return TryBuildToolCallResponse(bareMatch.Groups[1].Value, rawContent);

            // Priority 5: conversational response (no JSON at all — this is valid
            // for greetings, clarification questions, etc.)
            if (!rawContent.Contains("{"))
                return ParsedModelResponse.AsConversational(rawContent.Trim());

            return ParsedModelResponse.AsError(rawContent,
                "Response contained braces but no recognisable tool call or action.final.");
        }

        // Build the error message to inject back to the model when parsing fails.
        // Returned as a SYSTEM role message, never user role.
        public static ChatMessage BuildParseErrorFeedback(ParsedModelResponse parsed) =>
            new ChatMessage
            {
                Role = "system",
                Content = $"TOOL FORMAT ERROR: {parsed.ParseErrorReason}\n"
                        + "Output ONLY a raw JSON object — no markdown fences, no explanation text:\n"
                        + "{\n  \"name\": \"namespace.tool\",\n  \"arguments\": { \"key\": \"value\" }\n}"
            };

        // ── Destructive operation guard ───────────────────────────────────────

        /// <summary>
        /// Returns true if the planned step is destructive and has NOT been
        /// explicitly pre-confirmed by the caller.
        /// </summary>
        public static bool RequiresConfirmation(PlannedStep step)
        {
            var destructiveVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "delete", "remove", "drop", "truncate", "overwrite", "format",
                "write_file", "apply_diff"
            };

            return destructiveVerbs.Contains(step.Verb ?? string.Empty)
                || destructiveVerbs.Contains(step.Command ?? string.Empty);
        }

        // ── Preview & apply steps ─────────────────────────────────────────────

        public async Task<List<(int Index, string Title, string Preview)>>
            CollectPreviewStepsAsync(TaskPlan plan, CancellationToken ct)
        {
            var previews = new List<(int, string, string)>();
            if (plan?.Steps == null) return previews;

            for (int i = 0; i < plan.Steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var step = plan.Steps[i];
                if (step.Apply) continue;

                var request = BuildRequest(step, preview: true);
                var target = ResolveTarget(step);
                var res = await _router.RouteAsync(
                    JsonSerializer.Serialize(new { Agent = target, Request = request }), ct);

                previews.Add((i, step.Verb ?? step.Command ?? "Preview", res.Output ?? string.Empty));
            }

            return previews;
        }

        public async Task<List<(int Index, AgentResult Result)>>
            ApplySelectedStepsAsync(TaskPlan plan, IEnumerable<int> selectedIndexes, CancellationToken ct)
        {
            var results = new List<(int, AgentResult)>();
            var selected = new HashSet<int>(selectedIndexes);
            if (plan?.Steps == null) return results;

            for (int i = 0; i < plan.Steps.Count; i++)
            {
                if (!selected.Contains(i)) continue;
                ct.ThrowIfCancellationRequested();

                var step = plan.Steps[i];
                var request = BuildRequest(step, preview: false);
                var target = ResolveTarget(step);
                var res = await _router.RouteAsync(
                    JsonSerializer.Serialize(new { Agent = target, Request = request }), ct);

                step.Result = res;
                results.Add((i, res));
            }

            return results;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private bool TryExecuteFastPath(
            string input,
            out TaskPlan? dynamicPlan,
            out IntentClassification? intent)
        {
            dynamicPlan = null;
            intent = null;
            var clean = input.Trim();

            var fileMatch = FileReadRegex.Match(clean);
            if (fileMatch.Success)
            {
                var path = fileMatch.Groups[1].Value;
                intent = MakeIntent(IntentType.CodeAnalysis);
                dynamicPlan = SingleStepPlan("code_editor", "read_file",
                    new() { ["path"] = path }, $"Read file {path}");
                return true;
            }

            var dirMatch = DirListRegex.Match(clean);
            if (dirMatch.Success)
            {
                var dir = dirMatch.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(dir)) dir = ".";
                intent = MakeIntent(IntentType.CodeAnalysis);
                dynamicPlan = SingleStepPlan("code_editor", "list_directory",
                    new() { ["path"] = dir }, $"List directory {dir}");
                return true;
            }

            return false;
        }

        private static IntentClassification MakeIntent(IntentType type) =>
            new() { Type = type, Shape = PlanShape.TaskList, Confidence = 1.0f };

        private static TaskPlan SingleStepPlan(
            string tool, string verb,
            Dictionary<string, string> args, string description) =>
            new()
            {
                Intent = IntentType.CodeAnalysis,
                Summary = description,
                Steps = new List<PlannedStep>
                {
                    new() { Tool = tool, Verb = verb, Args = args, Description = description, OutputRef = "$step1" }
                }
            };

        private async Task<string> ExecuteDirectActionAsync(PlannedStep step, CancellationToken ct)
        {
            var request = BuildRequest(step, preview: false);
            var res = await _router.RouteAsync(
                JsonSerializer.Serialize(new { Agent = step.Tool, Request = request }), ct);
            return res.Output ?? "Execution yielded empty results.";
        }

        private static AgentRequest BuildRequest(PlannedStep step, bool preview)
        {
            var argMap = step.Args?.ToDictionary(k => k.Key, v => (object)v)
                      ?? step.Arguments?.ToDictionary(k => k.Key, v => (object)v)
                      ?? new Dictionary<string, object>();

            if (preview) argMap["preview"] = "true";

            return new AgentRequest
            {
                Verb = step.Verb ?? step.Command ?? string.Empty,
                ArgMap = argMap
            };
        }

        private static string ResolveTarget(PlannedStep step) =>
            !string.IsNullOrEmpty(step.Tool) ? step.Tool : step.Agent ?? string.Empty;

        private bool IsSimpleReadIntent(string intent) =>
            FileReadRegex.IsMatch(intent.Trim()) || DirListRegex.IsMatch(intent.Trim());

        private ParsedModelResponse TryBuildToolCallResponse(string json, string rawContent)
        {
            var args = TryParseArguments(json);
            if (args == null)
                return ParsedModelResponse.AsError(rawContent, $"JSON parse failed: {json[..Math.Min(80, json.Length)]}");

            if (!args.TryGetValue("name", out var nameObj) || nameObj == null)
                return ParsedModelResponse.AsError(rawContent, "Tool call JSON missing 'name' field.");

            var toolName = nameObj.ToString()!;

            // Treat action.final embedded in a JSON tool call format
            if (toolName.Equals("action.final", StringComparison.OrdinalIgnoreCase))
            {
                var output = args.TryGetValue("arguments", out var innerArgs) && innerArgs is Dictionary<string, object> ia
                    ? ia.TryGetValue("output", out var o) ? o?.ToString() ?? string.Empty : string.Empty
                    : string.Empty;
                return ParsedModelResponse.AsFinal(output);
            }

            var toolArgs = args.TryGetValue("arguments", out var toolArgsObj)
                ? toolArgsObj as Dictionary<string, object> ?? new()
                : new Dictionary<string, object>();

            return ParsedModelResponse.AsToolCall(null, toolName, toolArgs);
        }

        private static object? ParseElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in element.EnumerateObject())
                    {
                        var parsedVal = ParseElement(prop.Value);
                        if (parsedVal != null)
                        {
                            dict[prop.Name] = parsedVal;
                        }
                    }
                    return dict;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in element.EnumerateArray())
                    {
                        var parsedVal = ParseElement(item);
                        if (parsedVal != null)
                        {
                            list.Add(parsedVal);
                        }
                    }
                    return list;
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt64(out long l)) return l;
                    if (element.TryGetDouble(out double d)) return d;
                    return element.GetRawText();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null;
                default:
                    return element.ToString();
            }
        }

        private static Dictionary<string, object>? TryParseArguments(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

                var dict = new Dictionary<string, object>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var val = ParseElement(prop.Value);
                    if (val != null)
                    {
                        dict[prop.Name] = val;
                    }
                }
                return dict;
            }
            catch
            {
                return null;
            }
        }

        // ── Intent Clarifier ──────────────────────────────────────────────────
        /// <summary>
        /// Calls the model with a lightweight prompt to rephrase the user's raw input
        /// into a single, clear, actionable technical objective.
        /// Falls back to the original input on timeout or any error.
        /// </summary>
        public async Task<string> ClarifyIntentAsync(string rawInput, CancellationToken ct)
        {
            // Skip trivially short or already precise inputs
            if (rawInput.Length < 8) return rawInput;

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

                const string systemPrompt =
                    "You are an intent clarification agent for a coding assistant. " +
                    "Rephrase the user's message into ONE clear, actionable technical objective. " +
                    "Do not answer questions, write code, or explain anything. " +
                    "Output only the restated goal as a single sentence.";

                var response = await _model.CompleteAsync(rawInput, linked.Token, systemPrompt, ModelRole.Primary);
                var clarified = response?.Content?.Trim();

                // Only use the clarification if it's non-empty and meaningfully different
                if (!string.IsNullOrWhiteSpace(clarified) && clarified.Length > 5)
                    return clarified;
            }
            catch
            {
                // Silently fall back — never block the pipeline
            }

            return rawInput;
        }

    }
}
