using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Gravity.Core.Agents
{
    public enum AgentTaskState
    {
        Planning,
        Executing
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AppEngine  —  single execution unit driven by the Orchestrator.
    //
    // The engine is fully decoupled from the UI:
    //   - All progress / approval surfaces are proper C# events.
    //   - The UI (or any other consumer) subscribes/unsubscribes; the engine
    //     never references WinForms or any presentation layer.
    //
    // Key design notes:
    //   1. Environment context injected into system prompt (OS, shell, workdir)
    //   2. Parse errors fed back as SYSTEM role, never USER role
    //   3. Conversational intents detected BEFORE the loop, not inside it
    //   4. action.final checked via Orchestrator.ParseModelResponse (single parser)
    //   5. History always: system → user → (assistant → tool/system)*
    //      Two consecutive user messages no longer possible
    //   6. Parse-error retry capped separately from step counter
    //   7. CompressHistoryAsync preserves role alternation after compression
    //   8. Web-search failure advisory injected as SYSTEM, not USER
    //   9. Approval gate uses TaskCompletionSource so the UI can respond async
    // ─────────────────────────────────────────────────────────────────────────

    public class AppEngine
    {
        // ── Dependencies ──────────────────────────────────────────────────────
        private readonly IRouterService    _router;
        private readonly ISettingsService  _settingsService;
        private readonly IArtifactService  _artifactService;
        private readonly IRagService       _ragService;
        private readonly IKnowledgeService _knowledgeService;
        private readonly ProjectContext    _projectContext;
        private readonly IModelClient      _model;

        // ── Internal accessors (for BranchExecutor) ─────────────────────────
        internal IRouterService    Router           => _router;
        internal ISettingsService  SettingsService  => _settingsService;
        internal IArtifactService  ArtifactService  => _artifactService;
        internal IRagService       RagService       => _ragService;
        internal IKnowledgeService KnowledgeService => _knowledgeService;
        internal ProjectContext    ProjectContext    => _projectContext;
        internal IModelClient      Model            => _model;

        // ── State ─────────────────────────────────────────────────────────────
        public string               Id                 { get; set; } = Guid.NewGuid().ToString();
        public string               UserIntent         { get; set; } = string.Empty;
        public string               FinalOutput        { get; set; } = string.Empty;
        public List<ChatMessage>    History            { get; }      = new();
        public CancellationTokenSource? Cts            { get; private set; }
        public EnvironmentContext   EnvironmentContext { get; set; } = EnvironmentContext.Detect();
        public AgentStatus          Status             { get; set; } = AgentStatus.Idle;
        public AgentTaskState       TaskState          { get; set; } = AgentTaskState.Planning;
        public string?              OwnerTag           { get; set; }
        public TaskPlan?            CurrentPlan        { get; set; }
        public int                  CurrentStepIndex   { get; set; } = -1;
        /// <summary>Set by the Orchestrator when the user's raw input was rephrased
        /// by the Intent Clarifier. Null if the input was clear enough to use as-is.</summary>
        public string?              ClarifiedIntent    { get; set; }
        public IntentClassification? CurrentClassification { get; set; }

        // ── Background critic ────────────────────────────────────────────────
        private readonly ConcurrentQueue<string> _advisoryQueue = new();
        private BackgroundCritic? _critic;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Fired for every log line emitted by the engine.</summary>
        public event EventHandler<string>?                   LogEmitted;

        /// <summary>Fired when the engine status changes (Idle → Running → Finished/Error).</summary>
        public event EventHandler<AgentStatus>?              StatusChanged;

        /// <summary>Fired for each streamed token from the model.</summary>
        public event EventHandler<string>?                   StreamReceived;

        /// <summary>Fired at the start of each reasoning step.</summary>
        public event EventHandler<StepStartedEventArgs>?     StepStarted;

        /// <summary>
        /// Fired when the engine needs user approval before executing an action.
        /// The handler MUST call <see cref="ApprovalRequestedEventArgs.Completion"/>.SetResult
        /// to unblock the engine. Failure to do so will block the engine until the
        /// CancellationToken is triggered.
        /// </summary>
        public event EventHandler<ApprovalRequestedEventArgs>? ApprovalRequested;

        /// <summary>Fired when a tool action completes and telemetry is available.</summary>
        public event EventHandler<ActionTelemetry>?          TelemetryCaptured;

        /// <summary>Fired immediately after the model output is parsed and a tool call is identified,
        /// before the tool is executed. Use this to update UI labels in real-time.</summary>
        public event EventHandler<ActionParsedEventArgs>?    ActionParsed;

        /// <summary>Fired after a Roslyn blast-radius analysis produces new impact data.</summary>
        public event EventHandler<object>?                   TacticalContextUpdated;

        /// <summary>Fired when the engine needs user approval for a generated plan.</summary>
        public event EventHandler<PlanRequestedEventArgs>?   PlanRequested;

        /// <summary>
        /// External entry point for logging. Fires LogEmitted from within the class
        /// so that BranchExecutor and other external callers can emit logs.
        /// </summary>
        internal void EmitLog(string message) => LogEmitted?.Invoke(this, message);

        // ── Construction ──────────────────────────────────────────────────────
        public AppEngine(
            IRouterService    router,
            ISettingsService  settingsService,
            IArtifactService  artifactService,
            IRagService       ragService,
            IKnowledgeService knowledgeService,
            ProjectContext    projectContext,
            IModelClient      model)
        {
            _router           = router           ?? throw new ArgumentNullException(nameof(router));
            _settingsService  = settingsService  ?? throw new ArgumentNullException(nameof(settingsService));
            _artifactService  = artifactService  ?? throw new ArgumentNullException(nameof(artifactService));
            _ragService       = ragService       ?? throw new ArgumentNullException(nameof(ragService));
            _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
            _projectContext   = projectContext   ?? throw new ArgumentNullException(nameof(projectContext));
            _model            = model            ?? throw new ArgumentNullException(nameof(model));
            Cts = new CancellationTokenSource();
        }

        // ── Public API ────────────────────────────────────────────────────────
        public void SeedPlan(TaskPlan plan)
        {
            CurrentPlan = plan;
            CurrentStepIndex = -1;
            TaskState = AgentTaskState.Planning;
        }

        public async Task RunAsync(int maxSteps = 10)
        {
            Status = AgentStatus.Running;
            StatusChanged?.Invoke(this, Status);
            var logFile = Path.Combine(Path.GetTempPath(), $"gravity_{Id}.log");
            await ExecuteAsync(UserIntent, logFile, Cts?.Token ?? CancellationToken.None, maxSteps);
            Status = AgentStatus.Finished;
            StatusChanged?.Invoke(this, Status);
        }

        public async Task RunWithPlanAsync(TaskPlan plan, int maxSteps = 10)
        {
            SeedPlan(plan);
            await RunAsync(maxSteps);
        }

        public void Stop()
        {
            try { Cts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        // ── Core execution loop ───────────────────────────────────────────────
        public async Task ExecuteStepAsync(PlannedStep step, string logFile, CancellationToken ct, int maxSteps = 15)
        {
            CurrentStepIndex++;

            // If this step has a sub-plan, execute sub-steps in isolated branches
            if (step.SubPlan?.Steps != null && step.SubPlan.Steps.Count > 0)
            {
                LogEmitted?.Invoke(this, $"[SubPlan] Executing {step.SubPlan.Steps.Count} sub-steps for: {step.Description}");

                var executor = new BranchExecutor(this);
                var results = await executor.ExecuteBranchesAsync(
                    step.SubPlan.Steps, logFile, ct, maxSteps);

                // Aggregate child outputs into FinalOutput for parent visibility
                var summary = new StringBuilder();
                summary.AppendLine($"Sub-plan for '{step.Description}' completed ({results.Count} steps):");
                foreach (var (childStep, output) in results)
                {
                    summary.AppendLine($"- {childStep.Tool}.{childStep.Verb}: {(output.Length > 200 ? output[..200] + "..." : output)}");
                }
                FinalOutput = summary.ToString();

                LogEmitted?.Invoke(this, $"[SubPlan] Sub-plan completed for: {step.Description}");
                return;
            }

            // Execute the step itself — guide the model to achieve the step goal, not railroad it
            var stepIntent = $"[Planned Step {CurrentStepIndex}]: {step.Description}\n" +
                $"Suggested starting point: {step.Tool}.{step.Verb} — but adapt freely based on what you discover.\n" +
                $"CRITICAL: Inspect files and check line numbers FIRST if parameters are unknown. You may deviate from the suggestion if the situation calls for it. Do NOT call action.final until AFTER completing this step.";
            CurrentClassification = new IntentClassification { Type = IntentType.CodeEdit, Shape = PlanShape.TaskList };
            await ExecuteAsync(stepIntent, logFile, ct, maxSteps);
        }

        public async Task ExecuteAsync(
            string            intent,
            string            logFile,
            CancellationToken ct,
            int               maxSteps = 15,
            ImageAttachment?  imageAttachment = null)
        {
            UserIntent = intent;

            // Detect conversational intent BEFORE entering the loop so we don't
            // spin up the full tool-calling system for simple greetings.
            if (IsConversationalIntent(intent))
            {
                FinalOutput = await GetConversationalReplyAsync(intent, ct);
                LogEmitted?.Invoke(this, $"[Final Message] {FinalOutput}");
                return;
            }

            var cheatSheet   = BuildAgentCheatSheet();
            var systemPrompt = BuildSystemPrompt(cheatSheet);

            lock (History)
            {
                if (History.Count == 0)
                {
                    History.Add(new ChatMessage { Role = "system", Content = systemPrompt });
                }
                else
                {
                    History[0].Content = systemPrompt; // Refresh environment context
                }
                History.Add(new ChatMessage { Role = "user", Content = intent, Image = imageAttachment });
            }

            LogEmitted?.Invoke(this, $"[Gravity] Starting engine {Id}. Environment: {EnvironmentContext}");
            LogEmitted?.Invoke(this, $"[Gravity] Intent: \"{intent}\"");

            // ── Start background critic ──────────────────────────────────────
            if (_settingsService.Current.CriticEnabled)
            {
                _critic = new BackgroundCritic(_advisoryQueue);
                _critic.Start();
            }

            int stepNumber               = 0;
            int parseErrorStreak         = 0;
            const int MAX_PARSE_ERRORS   = 3;
            int webSearchFailures        = 0;
            bool hasExecutedTools        = false;
            int plainTextNudgeStreak     = 0;
            int acceptanceReviewAttempts = 0;
            // Stagnation detection: if the agent calls the exact same tool+args N times in a row, break
            const int MAX_STAGNATION     = 4;
            string? lastToolFingerprint  = null;
            int stagnationCount          = 0;

            while (!ct.IsCancellationRequested && (maxSteps <= 0 || stepNumber < maxSteps))
            {
                stepNumber++;
                StepStarted?.Invoke(this, new StepStartedEventArgs(stepNumber, $"Step {stepNumber}: Reasoning..."));

                // ── Drain critic advisories ──────────────────────────────────
                while (_advisoryQueue.TryDequeue(out var advisory))
                {
                    LogEmitted?.Invoke(this, $"[Critic] {advisory}");
                    AddToolResult("system", null, $"SYSTEM_ADVICE: {advisory}");
                }

                int totalChars = 0;
                lock (History) { totalChars = History.Sum(m => m.Content?.Length ?? 0); }

                if (History.Count > 12 || totalChars > 24000)
                {
                    LogEmitted?.Invoke(this, "[Context] History size growing. Intelligently compressing reasoning context...");
                    await CompressHistoryAsync(systemPrompt, ct);
                }

                // ── Model call ────────────────────────────────────────────────
                List<ChatMessage> snapshot;
                lock (History) { snapshot = History.ToList(); }

                var tokenBuffer     = new StringBuilder();
                var tokenBufferLock = new object();
                var streamProgress  = new Progress<string>(token =>
                {
                    lock (tokenBufferLock) { tokenBuffer.Append(token); }
                    StreamReceived?.Invoke(this, token);
                });

                var tools = CurrentClassification?.Shape == PlanShape.DirectAnswer
                    ? null
                    : _router.GetAgentDescriptors();

                ModelResponse? modelResponse = null;
                try
                {
                    modelResponse = await _model.StreamResponseAsync(
                        snapshot, streamProgress, ct, null,
                        ModelRole.Reasoning, tools);
                }
                catch (Exception ex) when (ex.Message.Contains("413") || ex.Message.Contains("token", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("context", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("Large", StringComparison.OrdinalIgnoreCase))
                {
                    var firstLine = ex.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? ex.Message;
                    LogEmitted?.Invoke(this, $"[Autonomic Recovery] Token/context budget error ({firstLine}). Compressing reasoning context and retrying...");

                    await CompressHistoryAsync(systemPrompt, ct);

                    lock (History) { snapshot = History.ToList(); }
                    try
                    {
                        modelResponse = await _model.StreamResponseAsync(
                            snapshot, streamProgress, ct, null,
                            ModelRole.Reasoning, tools);
                    }
                    catch (Exception retryEx)
                    {
                        LogEmitted?.Invoke(this, $"[Autonomic Error] Retry failed after compression: {retryEx.Message}");
                        break;
                    }
                }
                catch (Exception ex) when (ex.Message.Contains("429") || ex.Message.Contains("rate limited", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase))
                {
                    var firstLine = ex.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? ex.Message;
                    LogEmitted?.Invoke(this, $"[Rate Limit Pause] API rate limited ({firstLine}). Pausing 10 seconds before retrying reasoning step...");

                    await Task.Delay(10000, ct);

                    lock (History) { snapshot = History.ToList(); }
                    try
                    {
                        modelResponse = await _model.StreamResponseAsync(
                            snapshot, streamProgress, ct, null,
                            ModelRole.Reasoning, tools);
                    }
                    catch (Exception retryEx)
                    {
                        LogEmitted?.Invoke(this, $"[Autonomic Error] Retry failed after rate limit pause: {retryEx.Message}");
                        FinalOutput = $"Reasoning loop paused due to rate limits (429): {retryEx.Message}";
                        break;
                    }
                }
                catch (OperationCanceledException ex)
                {
                    if (ct.IsCancellationRequested)
                    {
                        LogEmitted?.Invoke(this, "[Cancel] Task execution was stopped by the user.");
                        FinalOutput = "Task was cancelled by the user.";
                    }
                    else
                    {
                        LogEmitted?.Invoke(this, $"[Network Timeout] Streaming response timed out or was disconnected by provider: {ex.Message}");
                        FinalOutput = "Reasoning stream timed out or was disconnected by the remote AI provider.";
                    }
                    break;
                }

                if (modelResponse != null && string.IsNullOrWhiteSpace(modelResponse.Content))
                    modelResponse.Content = tokenBuffer.ToString();

                if (modelResponse == null || string.IsNullOrWhiteSpace(modelResponse.Content))
                {
                    LogEmitted?.Invoke(this, "[Error] Model returned empty response. Aborting.");
                    break;
                }

                // ── Parse ─────────────────────────────────────────────────────
                var nativeCalls = modelResponse.ToolCalls?
                    .Select(tc => new ToolCallRaw
                    {
                        Id             = tc.Id,
                        Name           = tc.Function?.Name ?? "",
                        ArgumentsJson  = tc.Function?.Arguments ?? "{}"
                    })
                    .ToList();

                ParsedModelResponse parsed;
                if (CurrentClassification?.Shape == PlanShape.DirectAnswer)
                {
                    parsed = ParsedModelResponse.AsConversational(modelResponse.Content.Trim());
                }
                else
                {
                    parsed = Orchestrator.StaticParseModelResponse(modelResponse.Content, nativeCalls);
                }
                LogEmitted?.Invoke(this, $"[Parse] Kind={parsed.Kind}  Tool={parsed.ToolName ?? "—"}");

                if (string.Equals(modelResponse.FinishReason, "length", StringComparison.OrdinalIgnoreCase) || 
                    string.Equals(modelResponse.FinishReason, "max_tokens", StringComparison.OrdinalIgnoreCase))
                {
                    LogEmitted?.Invoke(this, "[Warning] Model response was truncated due to max tokens limit.");
                    if (parsed.Kind == ModelResponseKind.ParseError)
                    {
                        parsed = ParsedModelResponse.AsError(
                            modelResponse.Content, 
                            "Output was truncated due to the token length limit. The JSON was incomplete. Consider breaking the task into smaller steps, or writing the file in chunks.");
                    }
                }

                // ── Handle parse error ────────────────────────────────────────
                if (parsed.Kind == ModelResponseKind.ParseError)
                {
                    parseErrorStreak++;
                    LogEmitted?.Invoke(this, $"[Parse Error #{parseErrorStreak}] {parsed.ParseErrorReason}");

                    if (parseErrorStreak >= MAX_PARSE_ERRORS)
                    {
                        LogEmitted?.Invoke(this, "[Abort] Too many consecutive parse errors. Ending session.");
                        FinalOutput = "I was unable to produce a valid response after several attempts. Please rephrase your request.";
                        _critic?.Dispose(); _critic = null;
                        return;
                    }

                    lock (History)
                    {
                        History.Add(new ChatMessage { Role = "assistant", Content = modelResponse.Content });
                        History.Add(Orchestrator.BuildParseErrorFeedback(parsed));
                    }
                    continue;
                }

                parseErrorStreak = 0;

                // ── Conversational reply ──────────────────────────────────────
                if (parsed.Kind == ModelResponseKind.Conversational)
                {
                    // Prevent premature exit if tools are available and the model hasn't called action.final or reached max nudges
                    if (tools != null && tools.Any() && (maxSteps <= 0 || stepNumber < maxSteps) && CurrentClassification?.Type != IntentType.Conversational && plainTextNudgeStreak < 2)
                    {
                        plainTextNudgeStreak++;
                        LogEmitted?.Invoke(this, $"[Reasoning Loop] Plain text reasoning detected without tool call (Nudge #{plainTextNudgeStreak}). Requesting tool execution or explicit completion...");
                        lock (History)
                        {
                            History.Add(new ChatMessage { Role = "assistant", Content = modelResponse.Content });
                            History.Add(new ChatMessage { Role = "user", Content = "Your response contained text reasoning without executing a tool call or outputting [action.final].\n- If your task is complete, output [action.final] {\"output\": \"...\"}.\n- Otherwise, invoke the required tool (e.g. view_file, edit_lines, run_command) to continue." });
                        }
                        continue;
                    }

                    FinalOutput = parsed.Output ?? modelResponse.Content;
                    LogEmitted?.Invoke(this, $"[Final Message] {FinalOutput}");
                    _critic?.Dispose(); _critic = null;
                    return;
                }

                plainTextNudgeStreak = 0;

                // ── action.final ──────────────────────────────────────────────
                if (parsed.Kind == ModelResponseKind.Final)
                {
                    FinalOutput = parsed.Output ?? string.Empty;
                    LogEmitted?.Invoke(this, $"[Final Message] {FinalOutput}");
                    await File.AppendAllTextAsync(logFile, $"[Final Message]\n{FinalOutput}\n", ct);
                    _critic?.Dispose(); _critic = null;
                    await ExtractAndPersistKnowledgeAsync(ct);
                    return;
                }

                hasExecutedTools = true;

                // ── Tool call ─────────────────────────────────────────────────
                var toolName  = parsed.ToolName!;
                var toolArgs  = parsed.ToolArguments ?? new Dictionary<string, object>();

                // ── Stagnation guard ──────────────────────────────────────────
                var fingerprint = toolName + "|" + System.Text.Json.JsonSerializer.Serialize(toolArgs);
                if (fingerprint == lastToolFingerprint)
                {
                    stagnationCount++;
                    if (stagnationCount >= MAX_STAGNATION)
                    {
                        // Don't terminate — force the LLM to reassess and either try a different approach
                        // or explicitly ask the user for the missing information it needs to proceed.
                        LogEmitted?.Invoke(this, $"[Stagnation] '{toolName}' repeated {stagnationCount}x. Injecting mandatory pivot advisory.");
                        stagnationCount = 0; // reset so next reassessment attempt gets a fresh count
                        lastToolFingerprint = null;
                        lock (History)
                        {
                            History.Add(new ChatMessage
                            {
                                Role = "user",
                                Content =
                                    $"[SYSTEM — STAGNATION DETECTED] You have called '{toolName}' {MAX_STAGNATION} times in a row with the same arguments and are not making progress. " +
                                    "You MUST do one of the following:\n" +
                                    "  A) Try a completely different tool or approach to solve the same problem.\n" +
                                    "  B) If you are blocked because information is missing, call action.final with a clear question asking the user exactly what you need.\n" +
                                    "Do NOT repeat the same tool call again."
                            });
                        }
                        continue; // re-enter loop so LLM sees the advisory
                    }
                    LogEmitted?.Invoke(this, $"[Stagnation Warning] Repeated call #{stagnationCount} to '{toolName}' — same args as last time.");
                }
                else
                {
                    stagnationCount = 0;
                    lastToolFingerprint = fingerprint;
                }
                var agentName = toolName.Split('.')[0];
                var operation = toolName.Contains('.') ? toolName.Split('.')[1] : toolName;

                // Fire immediately so the UI can update the step label before execution
                ActionParsed?.Invoke(this, new ActionParsedEventArgs(stepNumber, toolName, ExtractPath(toolArgs)));

                lock (History)
                {
                    History.Add(new ChatMessage
                    {
                        Role      = "assistant",
                        Content   = modelResponse.Content,
                        ToolCalls = modelResponse.ToolCalls
                    });
                }

                // Logic agent — pin fact and continue
                if (string.Equals(agentName, "logic", StringComparison.OrdinalIgnoreCase))
                {
                    var fact = toolArgs.TryGetValue("fact",  out var f) ? f?.ToString() ?? ""
                             : toolArgs.TryGetValue("input", out var i) ? i?.ToString() ?? "" : "";
                    PinFact(fact);
                    AddToolResult(toolName, null, "Fact successfully pinned to Gravity memory.");
                    continue;
                }


                if (toolArgs == null)
                {
                    toolArgs = new Dictionary<string, object>();
                }
                toolArgs["__engine_id"] = Id;

                // ── Security gate ─────────────────────────────────────────────
                var act = new AgentAction
                {
                    Tool      = agentName,
                    Operation = operation,
                    Params    = new AgentRequest
                    {
                        Verb   = operation,
                        ArgMap = toolArgs
                    }
                };

                bool approved = await CheckApprovalAsync(act, toolName, ct);
                if (!approved) continue;

                // ── Artifact registration (before write) ──────────────────────
                RegisterArtifactIfWrite(act);

                // ── Route to tool ─────────────────────────────────────────────
                LogEmitted?.Invoke(this, $">> Execute: {act.Tool}.{act.Operation}");
                var routerPayload = JsonSerializer.Serialize(new { Agent = act.Tool, Request = act.Params });
                var agentRes = await _router.RouteAsync(routerPayload, ct)
                    ?? new AgentResult { Success = false, Output = $"Agent '{act.Tool}' returned no result." };

                TelemetryCaptured?.Invoke(this, BuildTelemetry(agentRes));

                await RunRoslynAnalysisIfNeededAsync(act, ct);

                // ── Plan approval gate ────────────────────────────────────────
                // gravity.propose creates an ImplementationPlan artifact that the
                // user must explicitly approve before ANY execution continues.
                if (agentRes.RequiresPlanApproval)
                {
                    LogEmitted?.Invoke(this, "[Plan Gate] Implementation plan created. Awaiting user approval...");
                    var planAct = new AgentAction { Tool = "gravity", Operation = "propose", Params = act.Params };
                    bool planApproved = await CheckApprovalAsync(planAct, "gravity.propose", ct);

                    if (!planApproved)
                    {
                        LogEmitted?.Invoke(this, "[Plan Gate] Plan rejected by user. Stopping execution.");
                        FinalOutput = "The implementation plan was rejected. Please tell me what changes you'd like to make to the proposal and I'll revise it.";
                        lock (History)
                        {
                            History.Add(new ChatMessage
                            {
                                Role = "system",
                                Content = "CRITICAL: The user REJECTED the implementation plan. " +
                                          "Do NOT re-propose the same plan. Do NOT execute any code changes. " +
                                          "Ask the user what they want changed in the proposal."
                            });
                        }
                        _critic?.Dispose(); _critic = null;
                        return;
                    }

                    LogEmitted?.Invoke(this, "[Plan Gate] Plan approved. Proceeding with execution.");
                    // Transition to execution mode so gravity.propose is hidden from the tool list
                    TaskState = AgentTaskState.Executing;
                    AddToolResult(toolName, parsed.ToolCallId, "Plan approved by user. Now proceeding with execution.");
                    continue;
                }

                // ── Evaluate result ───────────────────────────────────────────
                var advice = EvaluateToolResult(act, agentRes);
                if (advice != null)
                {
                    LogEmitted?.Invoke(this, $"[Advice] {advice}");
                    AddToolResult(toolName, null, $"SYSTEM_ADVICE: {advice}");
                    continue;
                }

                var rawOutput = agentRes.Output ?? "Success";

                // Web-search failure advisory
                bool isWebSearch = agentName == "search" && operation is "web" or "docs" or "";
                if (isWebSearch && rawOutput.Contains("No results", StringComparison.OrdinalIgnoreCase))
                {
                    webSearchFailures++;
                    if (webSearchFailures >= 2)
                    {
                        rawOutput = "[SYSTEM ADVISORY] Web/docs search returned no results multiple times. " +
                                    "search.web and search.docs cannot access LOCAL project files. " +
                                    "Use code_editor.read_file, code_editor.glob, or code_editor.list_directory instead.";
                        LogEmitted?.Invoke(this, "[Advisory] Web search blocked after repeated failures.");
                    }
                }

                // Truncate oversized observations
                string truncated;
                if (_settingsService.Current.TruncateObservations)
                {
                    var maxLen = _settingsService.Current.MaxObservationLength;
                    if (rawOutput.Length > maxLen)
                    {
                        int half = maxLen / 2;
                        truncated = string.Concat(rawOutput.AsSpan(0, half), "\n\n...[OBSERVATION TRUNCATED]...\n\n", rawOutput.AsSpan(rawOutput.Length - half));
                    }
                    else
                    {
                        truncated = rawOutput;
                    }
                }
                else
                {
                    truncated = rawOutput;
                }

                LogEmitted?.Invoke(this, $"[Observation] {truncated}");
                await File.AppendAllTextAsync(logFile, $"[Observation]\n{truncated}\n\n", ct);
                AddToolResult(toolName, parsed.ToolCallId, truncated);

                // ── Notify critic of step completion ─────────────────────────
                _critic?.NotifyStepCompleted(
                    History.ToList(),
                    toolName,
                    agentRes.Success,
                    act.Operation is "write_file" or "apply_diff" or "apply_patches" or "replace_block" or "delete",
                    stepNumber);
            }

            // ── Stop background critic ───────────────────────────────────────
            if (_critic != null)
            {
                _critic.Stop();
                _critic.Dispose();
                _critic = null;
            }

            // ── Drain any remaining advisories ───────────────────────────────
            while (_advisoryQueue.TryDequeue(out var finalAdvisory))
            {
                LogEmitted?.Invoke(this, $"[Critic] {finalAdvisory}");
            }

            // ── Step limit or Cancellation ────────────────────────────────────
            if (ct.IsCancellationRequested && string.IsNullOrWhiteSpace(FinalOutput))
            {
                FinalOutput = "Task was cancelled by the user.";
            }
            else if (string.IsNullOrWhiteSpace(FinalOutput))
            {
                FinalOutput = maxSteps > 0
                    ? $"I reached my maximum step limit ({maxSteps}) while working on the task. The task may be incomplete. Please review my progress and provide further instructions to continue."
                    : "Task completed successfully.";
            }

            if (!string.IsNullOrWhiteSpace(FinalOutput))
            {
                LogEmitted?.Invoke(this, $"[Final Message] {FinalOutput}");
                await File.AppendAllTextAsync(logFile, $"[Final Message]\n{FinalOutput}\n", ct);
            }
        }

        // ── History helpers ───────────────────────────────────────────────────

        /// <summary>
        /// All tool results flow through this single method.
        /// Respects DisableNativeToolCalls to choose between "tool" role (native)
        /// and "user" role (content-based). Never injects a bare error as a user message.
        /// </summary>
        private void AddToolResult(string toolName, string? toolCallId, string content)
        {
            lock (History)
            {
                if (_settingsService.Current.DisableNativeToolCalls)
                {
                    var newContent = $"[Result from {toolName}]:\n{content}";
                    if (History.Count > 0 && History[^1].Role == "user")
                    {
                        History[^1].Content += $"\n\n{newContent}";
                    }
                    else
                    {
                        History.Add(new ChatMessage
                        {
                            Role    = "user",
                            Content = newContent
                        });
                    }
                }
                else
                {
                    History.Add(new ChatMessage
                    {
                        Role       = "tool",
                        ToolCallId = toolCallId,
                        Name       = toolName,
                        Content    = content
                    });
                }
            }
        }

        // ── Conversational handling ───────────────────────────────────────────

        private bool IsConversationalIntent(string intent)
        {
            if (string.IsNullOrWhiteSpace(intent)) return true;
            var t = intent.Trim().ToLowerInvariant();
            if (t.Contains('.') || t.Contains('/') || t.Contains('\\') || t.Length > 60)
                return false;

            var conversationalPhrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "hello", "hi", "hey", "how are you", "good morning",
                "good afternoon", "good evening", "greetings", "sup", "yo"
            };
            return conversationalPhrases.Contains(t);
        }

        private async Task<string> GetConversationalReplyAsync(string intent, CancellationToken ct)
        {
            var reply = await _model.CompleteAsync(
                intent, ct,
                "You are Gravity, an elite AI coding assistant. Be concise, direct, and highly technical in your responses.",
                ModelRole.Primary);
            return reply?.Content ?? "Hello! How can I help you with your codebase today?";
        }

        // ── System prompt ─────────────────────────────────────────────────────

        private string BuildSystemPrompt(string cheatSheet)
        {
            var sb = new StringBuilder();
            if (CurrentClassification?.Shape == PlanShape.DirectAnswer)
            {
                sb.AppendLine("You are Gravity, an elite AI coding assistant.");
                sb.AppendLine("Provide a direct, high-quality, and technical response to the user's request. No JSON formatting or tool calls are required.");
                sb.AppendLine();

                sb.AppendLine("## Response Style & Adaptation");
                sb.AppendLine("Dynamically adapt your response style to match the user's intent:");
                sb.AppendLine("- For code & implementation tasks: act as an expert engineer — quote exact code values, reference file paths/line numbers, be precise and direct.");
                sb.AppendLine("- For conceptual or educational tasks: explain clearly with structured sections, plain language, and relevant examples.");
                sb.AppendLine("- For status or executive tasks: lead with a concise summary and key takeaways.");
                return sb.ToString();
            }

            sb.AppendLine("You are Gravity, an elite, autonomous AI software engineer.");
            sb.AppendLine("Your goal is to solve the user's task perfectly, efficiently, and safely. You operate via tool execution in a strict JSON loop.");
            sb.AppendLine();

            sb.AppendLine("## Runtime Environment");
            sb.AppendLine($"- OS: {EnvironmentContext.OS}");
            sb.AppendLine($"- Shell: {EnvironmentContext.Shell}");
            sb.AppendLine($"- Working Directory: {EnvironmentContext.WorkingDirectory}");
            sb.AppendLine();

            sb.AppendLine("## Core Operating Rules");
            sb.AppendLine("1. VERIFY THEN ACT (CRITICAL): Never edit a file blindly. You must always use read_file or read_range to retrieve the current file content and its exact line numbers before making any modification.");
            sb.AppendLine("2. PREFER edit_lines (CRITICAL): The preferred surgical edit tool is code_editor.edit_lines — it takes start_line, end_line, and new_content. Because you already have line numbers from read_file, this approach is zero-ambiguity and never fails due to whitespace differences. Use apply_diff only as a fallback when a line-range approach is impractical.");
            sb.AppendLine("3. ONE ACTION AT A TIME: Output exactly ONE tool call JSON per turn. No conversational filler, no markdown blocks, just the raw JSON object.");
            sb.AppendLine("4. ERROR RECOVERY: If a command or tool fails, analyze the error. Do NOT blindly repeat the exact same tool call.");
            sb.AppendLine($"5. PLATFORM CONTEXT: Use ONLY valid {EnvironmentContext.Shell} commands. Always use appropriate path separators for {EnvironmentContext.OS}.");
            sb.AppendLine("6. FILE SEARCH: NEVER use list_directory with recursive:true on the root directory; use search_in_files or glob instead.");
            sb.AppendLine("7. COMPLETION & SELF-TERMINATION (CRITICAL): You have NO hard step limit — you run until YOU decide the task is done. " +
                          "Call `action.final` when: (a) the objective is 100% verified complete, OR (b) you need specific information from the user that you cannot obtain by any tool. " +
                          "If you are stuck or repeating yourself, STOP and try a completely different approach or tool before calling action.final. " +
                          "Never call action.final just because something was hard — only because it is truly complete or you are genuinely blocked on user input.");
            sb.AppendLine("8. VALIDATE CHANGES & FAIL FAST: After editing code, YOU MUST run the appropriate compiler or tests (e.g., `dotnet build` or `npm run build`) to validate your changes. Do not assume your edit worked. If a code edit fails or causes a compiler error twice in a row, STOP. Use a read tool to check the exact lines you are trying to modify before attempting another edit. Do not repeat the same sequence over and over.");
            sb.AppendLine("9. PROACTIVE AGENT CREATION: If a requested task cannot be completed because a tool or capability is missing (e.g., 'Unknown tool' error or unsupported action), proactively suggest to the user that you can write a new agent or script to add that capability yourself.");
            sb.AppendLine("10. AVOID OUTPUT TRUNCATION: Construct shell commands that minimize noise to prevent the output from being truncated. Use flags like --quiet, grep/findstr, or redirect verbose logs to a file.");
            
            if (TaskState == AgentTaskState.Planning)
            {
                sb.AppendLine("11. PLANNING MODE: You are currently in Planning Mode. Create a formal Implementation Plan artifact using `gravity.propose` detailing the title, background, architectural changes, and step-by-step implementation roadmap before executing code changes.");
                sb.AppendLine();
                sb.AppendLine("## Planning Tools");
                sb.AppendLine("- gravity.propose — Create a formal Implementation Plan artifact. Parameters: { \"title\": \"...\", \"content\": \"markdown content\" }");
                sb.AppendLine("- gravity.plan — Create/update the live task checklist. Parameters: { \"title\": \"...\", \"tasks\": \"task1, task2, ...\" }");
                sb.AppendLine("- gravity.walkthrough — Summarise what was done after completion. Parameters: { \"title\": \"...\", \"content\": \"markdown content\" }");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("10. EXECUTION MODE: You are currently executing a specific step from a master plan. Do NOT create new implementation plans. Focus solely on completing the assigned step.");
                sb.AppendLine();
            }

            sb.AppendLine("[AVAILABLE TOOLS]");
            sb.Append(cheatSheet);
            sb.AppendLine("- action.final — Conclude the task. Parameters: { \"output\": \"message to user\" }");
            sb.AppendLine();

            sb.AppendLine("## Response Format");
            sb.AppendLine("Output ONLY this JSON — no markdown, no commentary:");
            sb.AppendLine("{");
            sb.AppendLine("  \"name\": \"namespace.tool_name\",");
            sb.AppendLine("  \"arguments\": { \"key\": \"value\" }");
            sb.AppendLine("}");

            sb.AppendLine();

            // ── Response style (dynamic tone adaptation) ───────────────────────
            sb.AppendLine("## Response Style & Adaptation");
            sb.AppendLine("Dynamically adapt your final answer to match the user's intent:");
            sb.AppendLine("- For code & implementation tasks: act as an expert engineer — quote exact values, line numbers, file paths, be terse, precise, and actionable.");
            sb.AppendLine("- For conceptual or educational tasks: explain clearly with structured sections, plain language, and relevant examples.");
            sb.AppendLine("- For status or executive tasks: lead with a concise summary and key decision points.");
            sb.AppendLine();

            return sb.ToString();
        }

        private string BuildAgentCheatSheet()
        {
            var sb = new StringBuilder();
            foreach (var desc in _router.GetAgentDescriptors())
            {
                var name = desc.Name.ToLowerInvariant();
                if (name is "knowledge") continue;

                if (desc.Actions?.Any() == true)
                {
                    foreach (var action in desc.Actions)
                    {
                        // Hide planning tools when in strict execution mode
                        if (TaskState == AgentTaskState.Executing && desc.Name == "gravity" && (action.Name == "propose" || action.Name == "plan"))
                            continue;

                        var hint = string.IsNullOrWhiteSpace(action.Description)
                            ? desc.Description : action.Description;
                        sb.AppendLine($"- {desc.Name}.{action.Name} — {hint}");
                    }
                }
            }
            return sb.ToString();
        }

        // ── Context compression ───────────────────────────────────────────────

        /// <summary>
        /// Compresses the conversation history into a short recap.
        /// Preserves role alternation: system → user → assistant → ...
        /// </summary>
        private async Task CompressHistoryAsync(string systemPrompt, CancellationToken ct)
        {
            try
            {
                string historyText;
                lock (History)
                {
                    int countToSummarize = Math.Max(0, History.Count - 5);
                    historyText = string.Join("\n",
                        History.Skip(1).Take(countToSummarize).Select(m =>
                        {
                            var text = m.Content ?? "";
                            if (text.Contains(";base64,", StringComparison.OrdinalIgnoreCase))
                            {
                                text = System.Text.RegularExpressions.Regex.Replace(text, @"data:image/[^;]+;base64,[A-Za-z0-9+/=]+", "[IMAGE DATA]");
                            }
                            if (text.Length > 2000)
                            {
                                int half = 1000;
                                text = string.Concat(text.AsSpan(0, half), "\n...[TRUNCATED]...\n", text.AsSpan(text.Length - half));
                            }
                            return $"[{m.Role}]: {text}";
                        }));
                }

                var sb     = new StringBuilder();
                var sbLock = new object();
                await _model.StreamResponseAsync(
                    new List<ChatMessage>(),
                    new Progress<string>(t => { lock (sbLock) { sb.Append(t); } }),
                    ct,
                    $"Summarize the following agent session in under 120 words. " +
                    $"Keep: file paths found, architectural decisions made, current task progress. " +
                    $"Discard: raw code dumps, verbose tool output.\n\n{historyText}",
                    ModelRole.Primary);

                var recap = sb.ToString().Trim();
                if (string.IsNullOrWhiteSpace(recap)) return;

                lock (History)
                {
                    if (History.Count > 6)
                    {
                        var systemMsg = History[0];
                        var firstUser = History[1];
                        var recentMessages = History.Skip(History.Count - 4).ToList();

                        History.Clear();
                        History.Add(systemMsg);
                        History.Add(firstUser);
                        History.Add(new ChatMessage
                        {
                            Role    = "assistant",
                            Content = $"[SESSION RECAP] {recap}"
                        });
                        History.AddRange(recentMessages);
                    }
                }
            }
            catch (Exception ex)
            {
                LogEmitted?.Invoke(this, $"[Compression Error] {ex.Message}");
            }
        }

        // ── Security gate ─────────────────────────────────────────────────────

        private async Task<bool> CheckApprovalAsync(AgentAction act, string toolName, CancellationToken ct)
        {
            var mode = _settingsService.Current.DevMode;

            bool requiresApproval = mode == DevelopmentMode.Review
                || (mode == DevelopmentMode.Assisted && (
                    act.Tool is "terminal" or "shell"
                    || (act.Tool is "code_editor" or "file"
                        && act.Operation is "apply_diff" or "apply_patches" or "replace_block" or "write_file" or "replace" or "write" or "delete")));

            if (!requiresApproval || ApprovalRequested == null) return true;

            LogEmitted?.Invoke(this, "[Security] Awaiting user approval...");

            var args = new ApprovalRequestedEventArgs(act);
            ApprovalRequested.Invoke(this, args);

            // The UI sets the result on args.Completion when the user decides.
            // WaitAsync propagates cancellation so Stop() unblocks immediately.
            bool approved = await args.Completion.Task.WaitAsync(ct);

            if (!approved)
            {
                LogEmitted?.Invoke(this, "[Security] Action denied by user.");
                AddToolResult(toolName, null,
                    $"SYSTEM_ADVICE: Action {act.Tool}.{act.Operation} was DENIED. Try a different approach.");
            }

            return approved;
        }

        // ── Artifact registration ─────────────────────────────────────────────
        private void RegisterArtifactIfWrite(AgentAction act)
        {
            if (act.Tool is not ("code_editor" or "file")) return;
            if (act.Operation is not ("apply_diff" or "apply_patches" or "replace_block" or "replace" or "write_file" or "write")) return;

            var path    = act.Params?.GetStringArgument("path");
            var isDiff  = act.Operation is "apply_diff" or "apply_patches" or "replace_block" or "replace";
            var content = act.Params?.GetStringArgument("content")
                       ?? act.Params?.GetStringArgument("code")
                       ?? "No content provided.";

            var artifact = _artifactService.CreateArtifact(
                isDiff ? ArtifactType.Diff    : ArtifactType.General,
                isDiff ? $"Modified: {path}"  : $"Written: {path}",
                isDiff ? $"Diff recorded for engine {Id}\n\n{content}" : content,
                Id);
            _artifactService.UpdateArtifact(artifact);
            if (path != null)
            {
                _ragService.NotifyFileChanged(path);
                if (_router.GetAgent("roslyn") is RoslynService roslyn)
                {
                    roslyn.InvalidateProjectCache();
                }
            }
        }

        // ── Roslyn blast radius ───────────────────────────────────────────────
        private async Task RunRoslynAnalysisIfNeededAsync(AgentAction act, CancellationToken ct)
        {
            if (act.Tool is not ("code_editor" or "file")) return;
            if (act.Operation is not ("write_file" or "write" or "apply_diff" or "apply_patches" or "replace_block" or "replace")) return;

            var filePath = act.Params?.GetStringArgument("path")
                        ?? act.Params?.GetStringArgument("targetfile");

            if (string.IsNullOrEmpty(filePath) ||
                !filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return;
            if (_projectContext.ProjectDirectory == null) return;

            var csproj = Directory
                .GetFiles(_projectContext.ProjectDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

            if (csproj == null) return;

            if (_router.GetAgent("roslyn") is RoslynService roslyn)
            {
                var impacts = await roslyn.GetBlastRadiusAsync(csproj, filePath);
                TacticalContextUpdated?.Invoke(this, impacts);
            }
        }

        // ── Telemetry ─────────────────────────────────────────────────────────
        private static ActionTelemetry BuildTelemetry(AgentResult res)
        {
            if (res.Metadata == null ||
                !res.Metadata.TryGetValue("telemetry_type", out var type))
                return new ActionTelemetry();

            var detail = type switch
            {
                "Explored" => $"Analyzed {res.Metadata.GetValueOrDefault("file")} {res.Metadata.GetValueOrDefault("range")}",
                "Edited"   => $"Edited file {res.Metadata.GetValueOrDefault("file")}",
                _          => string.Empty
            };

            return new ActionTelemetry { Type = type, Detail = detail, Count = 1 };
        }

        /// <summary>Extracts a human-readable target (path, command summary, query) from tool arguments for UI display.</summary>
        private static string? ExtractPath(Dictionary<string, object> toolArgs)
        {
            if (toolArgs == null || toolArgs.Count == 0) return null;

            // 1. File path arguments
            string[] pathKeys = { "path", "targetfile", "TargetFile", "file", "filePath", "filepath" };
            foreach (var key in pathKeys)
            {
                if (toolArgs.TryGetValue(key, out var p) && p is string s && !string.IsNullOrWhiteSpace(s))
                    return System.IO.Path.GetFileName(s);
            }

            // 2. Command line arguments
            string[] cmdKeys = { "command", "cmd", "CommandLine", "commandline", "script" };
            foreach (var key in cmdKeys)
            {
                if (toolArgs.TryGetValue(key, out var c) && c is string cmdStr && !string.IsNullOrWhiteSpace(cmdStr))
                {
                    var trimmed = cmdStr.Trim();
                    var firstLine = trimmed.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? trimmed;
                    if (firstLine.Length > 45) firstLine = firstLine.Substring(0, 42) + "...";
                    return firstLine;
                }
            }

            // 3. Search query or URL arguments
            string[] queryKeys = { "query", "Query", "url", "Url", "search_term" };
            foreach (var key in queryKeys)
            {
                if (toolArgs.TryGetValue(key, out var q) && q is string qStr && !string.IsNullOrWhiteSpace(qStr))
                {
                    var trimmed = qStr.Trim();
                    if (trimmed.Length > 35) trimmed = trimmed.Substring(0, 32) + "...";
                    return $"\"{trimmed}\"";
                }
            }

            return null;
        }

        // ── Tool result evaluation ────────────────────────────────────────────
        private string? EvaluateToolResult(AgentAction action, AgentResult result)
        {
            if (result == null) return "Tool returned no result.";
            if (!result.Success && result.Output != null)
            {
                if (result.Output.Contains("Unknown agent", StringComparison.OrdinalIgnoreCase))
                    return $"Agent '{action.Tool}' is not registered. Available: {string.Join(", ", _router.GetAgentNames())}. If the user asked for a capability you lack, explicitly suggest that you can write a new agent to provide it.";

                if (result.Output.Contains("Unknown tool", StringComparison.OrdinalIgnoreCase))
                    return $"{result.Output}. If the user asked for a capability you lack, explicitly suggest that you can write a new agent to provide it.";

                if (result.Output.Contains("Missing required arguments", StringComparison.OrdinalIgnoreCase))
                    return result.Output;
            }
            return null;
        }

        // ── Knowledge extraction ──────────────────────────────────────────────
        private async Task ExtractAndPersistKnowledgeAsync(CancellationToken ct)
        {
            try
            {
                if (!HasWriteOperations())
                {
                    LogEmitted?.Invoke(this, "[Memory] Read-only session. Skipping knowledge extraction.");
                    return;
                }

                List<ChatMessage> historyCopy;
                lock (History)
                {
                    if (History.Count < 3) return;
                    historyCopy = History.ToList();
                }

                LogEmitted?.Invoke(this, "[Memory] Analyzing session for technical discoveries...");

                var historySummary = string.Join("\n", historyCopy.Select(m =>
                    $"[{m.Role}]: {(m.Content.Length > 500 ? string.Concat(m.Content.AsSpan(0, 500), "...") : m.Content)}"));

                var prompt = $@"Analyze the following session and extract a reusable technical knowledge item.

[SESSION HISTORY]
{historySummary}

RULES:
1. Only extract if the discovery is specific and reusable (file paths, API quirks, architectural patterns).
2. If nothing significant was found, respond with exactly: NONE
3. If knowledge is found, respond with a JSON block, then '---', then Markdown content.

JSON schema:
{{
  ""name"": ""Concise Technical Title"",
  ""description"": ""Single sentence summary"",
  ""tags"": [""tag1"", ""tag2""]
}}";

                var response = (await _model.CompleteAsync(
                    prompt, ct,
                    "You are a technical knowledge extraction engine.",
                    ModelRole.Primary))?.Content;

                if (string.IsNullOrWhiteSpace(response)
                    || response.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase))
                {
                    LogEmitted?.Invoke(this, "[Memory] No new discoveries.");
                    return;
                }

                string jsonPart = "", markdownPart = "";
                int sep = response.IndexOf("---");
                if (sep != -1)
                {
                    jsonPart     = response[..sep];
                    markdownPart = response[(sep + 3)..].Trim();
                }
                else if (PlanParser.TryExtractJson(response, out var j))
                {
                    jsonPart     = j;
                    markdownPart = response.Replace(j, "").Trim();
                }

                if (PlanParser.TryExtractJson(jsonPart, out var finalJson))
                {
                    var item = JsonSerializer.Deserialize<KnowledgeItem>(finalJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (item != null)
                    {
                        await _knowledgeService.AddKnowledgeAsync(item, markdownPart);
                        LogEmitted?.Invoke(this, $"[Memory] Persisted: {item.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogEmitted?.Invoke(this, $"[Memory] Extraction failed: {ex.Message}");
            }
        }

        // ── Misc helpers ──────────────────────────────────────────────────────
        private void PinFact(string fact) { /* handled by memory layers */ }

        private bool HasWriteOperations() =>
            History.Any(m =>
                m.Role == "assistant" &&
                m.Content.Contains("write_file", StringComparison.OrdinalIgnoreCase) ||
                m.Content?.Contains("apply_diff", StringComparison.OrdinalIgnoreCase) == true);
    }

    // ── Support types ─────────────────────────────────────────────────────────
    public class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;
        public SynchronousProgress(Action<T> callback) =>
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        public void Report(T value) => _callback(value);
    }

    public class ActionTelemetry
    {
        public string Type       { get; set; } = string.Empty;
        public string Detail     { get; set; } = string.Empty;
        public int    Count      { get; set; }
        public double DurationMs { get; set; }
    }

    public class PlanRequestedEventArgs : EventArgs
    {
        public string PlanContent { get; }
        public TaskCompletionSource<bool> Completion { get; } = new();

        public PlanRequestedEventArgs(string planContent)
        {
            PlanContent = planContent;
        }
    }

    public class ActionParsedEventArgs : EventArgs
    {
        public int    Step       { get; }
        public string ToolName   { get; }
        public string? TargetPath { get; }

        public ActionParsedEventArgs(int step, string toolName, string? targetPath)
        {
            Step       = step;
            ToolName   = toolName;
            TargetPath = targetPath;
        }
    }
}