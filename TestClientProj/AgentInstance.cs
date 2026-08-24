using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace Gravity.Core
{
    public enum AgentStatus
    {
        Idle,
        Running,
        Paused,
        Finished,
        Error
    }

    public class AgentInstance
    {
        private readonly IModelClient _model;
        private readonly ReasoningRouter _router;
        private readonly IProjectContext _projectContext;
        private readonly KnowledgeService _knowledgeService;
        private readonly ISettingsService _settingsService;
        private readonly IArtifactService _artifactService;
        private readonly RagService _ragService;
        private readonly List<string> _sessionMemory;
        private readonly List<string> _pinnedFacts = new();

        public string Id { get; }
        public string UserIntent { get; }
        public AgentStatus Status { get; private set; } = AgentStatus.Idle;
        public List<ChatMessage> History { get; } = new();
        public CancellationTokenSource? Cts { get; private set; }
        public string? FinalOutput { get; private set; }

        public event Action<string>? OnLog;
        public event Action<string>? OnStream;
        public event Action<int, string>? OnStepStarted;
        public event Action? OnStatusChanged;
        public event Func<AgentAction, Task<bool>>? OnRequestApproval;
        public event Action<List<ImpactInfo>>? OnTacticalContextUpdated;
        public event Action<ActionTelemetry>? OnActionCaptured;

        public AgentInstance(string id, string intent, IModelClient model, ReasoningRouter router, IProjectContext projectContext, KnowledgeService knowledgeService, ISettingsService settingsService, IArtifactService artifactService, RagService ragService, List<string> sessionMemory)
        {
            Id = id;
            UserIntent = intent;
            _model = model;
            _router = router;
            _projectContext = projectContext;
            _knowledgeService = knowledgeService;
            _settingsService = settingsService;
            _artifactService = artifactService;
            _ragService = ragService;
            _sessionMemory = sessionMemory;
        }

        private void PinFact(string fact)
        {
            if (string.IsNullOrWhiteSpace(fact)) return;
            string formatted = $"[Fact] {fact}";
            if (!_pinnedFacts.Contains(formatted)) _pinnedFacts.Add(formatted);
            if (!_sessionMemory.Contains(formatted)) _sessionMemory.Add(formatted);
            OnLog?.Invoke($"[Memory] Pinned: {fact}");
        }

        public async Task RunAsync(int maxSteps = 10)
        {
            if (Status == AgentStatus.Running) return;

            Cts = new CancellationTokenSource();
            Status = AgentStatus.Running;
            OnStatusChanged?.Invoke();

            try
            {
                await AgentLoopAsync(UserIntent, Cts.Token, maxSteps);
                Status = AgentStatus.Finished;
                
                // Trigger Knowledge Extraction on success
                await ExtractAndPersistKnowledgeAsync(Cts.Token);
            }
            catch (OperationCanceledException)
            {
                Status = AgentStatus.Paused;
                OnLog?.Invoke("[System] Agent paused by user.");
            }
            catch (Exception ex)
            {
                Status = AgentStatus.Error;
                OnLog?.Invoke($"[Critical Error] {ex.Message}");
            }
            finally
            {
                OnStatusChanged?.Invoke();
            }
        }

        public void Stop()
        {
            Cts?.Cancel();
        }

        private async Task AgentLoopAsync(string userIntent, CancellationToken ct, int maxSteps)
        {
            // Initial Task Plan Artifact
            var taskArtifact = (TaskArtifact)_artifactService.CreateArtifact(ArtifactType.TaskPlan, "Task Plan", $"Planning for: {userIntent}");
            taskArtifact.AgentId = Id;
            taskArtifact.Tasks.Add(new TaskItem { Title = userIntent, IsInProgress = true });
            _artifactService.UpdateArtifact(taskArtifact);

            // 1. Load and Select Knowledge (RAG Lite)
            await _knowledgeService.RefreshKnowledgeAsync();
            var matchedKnowledge = _knowledgeService.MatchKnowledge(userIntent);
            var knowledgePrompt = new StringBuilder();
            if (matchedKnowledge.Any())
            {
                OnLog?.Invoke($"[Knowledge Detected] {string.Join(", ", matchedKnowledge.Select(k => k.Name))}");
                knowledgePrompt.AppendLine("\n[RELEVANT KNOWLEDGE AVAILABLE]");
                knowledgePrompt.AppendLine("The following Knowledge Base items matched your intent. Use the 'knowledge' tool with operation 'read' to view their full contents:");
                foreach (var k in matchedKnowledge)
                {
                    knowledgePrompt.AppendLine($"- {k.Name}: {k.Description}");
                }
            }

            var os = RuntimeInformation.OSDescription;
            var arch = RuntimeInformation.OSArchitecture;
            var shell = os.Contains("Windows") ? "PowerShell / CMD" : "Bash";
            var platformContext = $"\n[PLATFORM CONTEXT]\nOS: {os}\nArchitecture: {arch}\nShell: {shell}";

            // Build a lightweight "Cheat Sheet" of agents and their primary verbs to replace the heavy JSON schema
            var agentCheatSheet = new StringBuilder();
            foreach (var desc in _router.GetAgentDescriptors())
            {
                var verbs = desc.Actions != null && desc.Actions.Any() 
                    ? string.Join("/", desc.Actions.Select(a => a.Name)) 
                    : string.Join("/", desc.SupportedVerbs ?? Array.Empty<string>());
                agentCheatSheet.AppendLine($"- {desc.Name}: {verbs} ({desc.Description})");
            }

            var baseContext = $@"You are Gravity, a premium autonomous AI development assistant.
{platformContext}

[OPERATIONAL GUIDELINES]
1. VERIFICATION FIRST: Before reporting a task as complete, you MUST verify your work (e.g. dotnet build) if code was changed.
2. NATIVE TOOLS: Use 'call_gravity_agent' to interact with the system. 
Available Agents & Verbs:
{agentCheatSheet}
3. FINALIZATION: Call the 'action-final' tool when the user intent is fully resolved.
{knowledgePrompt}";

            var reasoningPrompt = baseContext + "\n\nAnalyze the current workspace and define the best next action using your available tools.";
            
            History.Add(new ChatMessage { Role = "user", Content = $"USER_INTENT: {userIntent}" });

            for (int step = 1; step <= maxSteps; step++)
            {
                ct.ThrowIfCancellationRequested();

                if (History.Count > 30)
                {
                    OnLog?.Invoke("[Context] Saturation detected. Compressing history...");
                    await CompressHistoryAsync(History, baseContext, ct);
                }

                OnLog?.Invoke($"\n[Thinking - Step {step}]");

                // RAG: Inject workspace context relevant to current history tail
                var ragQuery = History.LastOrDefault(m => (m.Role == "user" || m.Role == "assistant") && !string.IsNullOrWhiteSpace(m.Content))?.Content ?? userIntent;
                var ragContext = _ragService.BuildContextBlock(ragQuery);
                
                var memoryContext = _pinnedFacts.Any() || _sessionMemory.Any()
                    ? "\n[MEMORY CACHE]\n" + string.Join("\n", _pinnedFacts.Concat(_sessionMemory).Distinct())
                    : "";

                var stepSystemPrompt = reasoningPrompt + ragContext + memoryContext;

                OnStepStarted?.Invoke(step, stepSystemPrompt);

                var sb = new StringBuilder();
                var tokenTracker = new SynchronousProgress<string>(t =>
                {
                    sb.Append(t);
                    OnStream?.Invoke(t);
                });

                var timer = System.Diagnostics.Stopwatch.StartNew();
                var res = await _model.StreamResponseAsync(History, tokenTracker, ct, stepSystemPrompt, ModelRole.Reasoning, _router.GetAgentDescriptors());
                timer.Stop();
                var actionContent = res.Content;

                OnActionCaptured?.Invoke(new ActionTelemetry { Type = "Thought", DurationMs = timer.Elapsed.TotalSeconds, Detail = $"Thought for {Math.Round(timer.Elapsed.TotalSeconds, 1)}s" });

                if (res.ToolCalls.Count == 0 && string.IsNullOrWhiteSpace(actionContent))
                {
                    History.Add(new ChatMessage { Role = "assistant", Content = string.Empty });
                    History.Add(new ChatMessage { Role = "user", Content = "SYSTEM_ERROR: You did not provide any text or tool call. If you are finished, invoke 'action-final'." });
                    continue;
                }

                History.Add(new ChatMessage { Role = "assistant", Content = actionContent, ToolCalls = res.ToolCalls });

                if (res.ToolCalls.Count == 0)
                {
                    History.Add(new ChatMessage { Role = "user", Content = "SYSTEM_ERROR: You provided reasoning text but failed to invoke a tool. Please invoke a tool (like action-final if you are done)." });
                    continue;
                }

                foreach (var tc in res.ToolCalls)
                {
                    var tcName = tc.Function.Name;
                    var tcArgs = tc.Function.Arguments;

                    if (string.Equals(tcName, "action-final", StringComparison.OrdinalIgnoreCase) || string.Equals(tcName, "action_final", StringComparison.OrdinalIgnoreCase))
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(tcArgs, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                        var outMsg = dict.TryGetValue("output", out var o) ? o : actionContent;
                        
                        taskArtifact.Tasks.ForEach(t => { t.IsInProgress = false; t.IsCompleted = true; });
                        _artifactService.UpdateArtifact(taskArtifact);

                        var walkthrough = _artifactService.CreateArtifact(ArtifactType.Walkthrough, "Task Completed", outMsg);
                        walkthrough.AgentId = Id;
                        _artifactService.UpdateArtifact(walkthrough);

                        OnLog?.Invoke($"[Final] {outMsg}");
                        FinalOutput = outMsg;
                        return;
                    }

                    if (string.Equals(tcName, "logic-pin", StringComparison.OrdinalIgnoreCase) || string.Equals(tcName, "logic_pin", StringComparison.OrdinalIgnoreCase))
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(tcArgs, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                        var fact = dict.TryGetValue("fact", out var f) ? f : "";
                        PinFact(fact);
                        History.Add(new ChatMessage { Role = "tool", ToolCallId = tc.Id, Name = tcName, Content = "Fact pinned to memory." });
                        continue;
                    }

                    string agentName = "";
                    string operation = "";
                    var pms = new Dictionary<string, object>();

                    if (string.Equals(tcName, "call_gravity_agent", StringComparison.OrdinalIgnoreCase))
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(tcArgs, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                        agentName = dict.TryGetValue("agent", out var a) ? a.ToString() ?? "" : "";
                        operation = dict.TryGetValue("operation", out var o) ? o.ToString() ?? "" : "";
                        
                        if (dict.TryGetValue("arguments", out var argVal) && argVal != null)
                        {
                            var argStr = argVal.ToString();
                            if (!string.IsNullOrWhiteSpace(argStr) && argStr.TrimStart().StartsWith("{"))
                            {
                                try { pms = JsonSerializer.Deserialize<Dictionary<string, object>>(argStr, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
                                catch (Exception ex)
                                {
                                    History.Add(new ChatMessage { Role = "tool", ToolCallId = tc.Id, Name = tcName, Content = $"SYSTEM_ERROR: Failed to parse tool arguments. Ensure they are valid JSON. Error: {ex.Message}" });
                                    continue;
                                }
                            }
                            else if (!string.IsNullOrWhiteSpace(argStr))
                            {
                                pms["input"] = argStr;
                            }
                        }

                        // Special case: Redirect 'logic' agent to internal PinFact memory
                        if (string.Equals(agentName, "logic", StringComparison.OrdinalIgnoreCase))
                        {
                            var fact = pms.TryGetValue("fact", out var f) ? f?.ToString() ?? "" : (pms.TryGetValue("input", out var i) ? i?.ToString() ?? "" : "");
                            PinFact(fact);
                            History.Add(new ChatMessage { Role = "tool", ToolCallId = tc.Id, Name = tcName, Content = "Fact successfully pinned to Gravity memory." });
                            continue;
                        }
                    }
                    else
                    {
                        // Fallback for any other tool names (backward compatibility or direct calls)
                        agentName = tcName;
                        if (tcName.Contains("-"))
                        {
                            var pts = tcName.Split(new[] { '-' }, 2);
                            agentName = pts[0];
                            operation = pts[1];
                        }
                        else if (tcName.Contains("_"))
                        {
                            var pts = tcName.Split(new[] { '_' }, 2);
                            agentName = pts[0];
                            operation = pts[1];
                        }

                        try { pms = JsonSerializer.Deserialize<Dictionary<string, object>>(tcArgs, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
                        catch (Exception ex)
                        {
                            History.Add(new ChatMessage { Role = "tool", ToolCallId = tc.Id, Name = tcName, Content = $"SYSTEM_ERROR: Failed to parse tool arguments. Ensure they are valid JSON. Error: {ex.Message}" });
                            continue;
                        }
                    }

                    if (pms != null && pms.TryGetValue("operation", out var opObj) && string.IsNullOrEmpty(operation))
                        operation = opObj?.ToString() ?? "";
                    
                    var act = new AgentAction { Tool = agentName, Operation = operation, Params = new AgentRequest { Verb = operation, ArgMap = pms } };

                    OnLog?.Invoke($">> Native Execute: {act.Tool}.{act.Operation}");

                    bool approved = true;
                    var mode = _settingsService.Current.DevMode;

                    if (mode == DevelopmentMode.Review) approved = false;
                    else if (mode == DevelopmentMode.Assisted)
                    {
                        if (act.Tool == "terminal" || act.Tool == "shell") approved = false;
                        else if (act.Tool == "code_editor" || act.Tool == "file")
                        {
                            if (act.Operation == "apply_diff" || act.Operation == "write_file" || act.Operation == "replace" || act.Operation == "write") approved = false;
                        }
                    }

                    if (!approved && OnRequestApproval != null)
                    {
                        OnLog?.Invoke("[Security] Waiting for user approval...");
                        approved = await OnRequestApproval.Invoke(act);
                    }

                    if (!approved)
                    {
                        OnLog?.Invoke("[Security] Action denied by user.");
                        History.Add(new ChatMessage { Role = "tool", ToolCallId = tc.Id, Name = tcName, Content = $"SYSTEM_ADVICE: Action {act.Tool}.{act.Operation} was DENIED by the user. Please try a different approach." });
                        continue;
                    }

                    if ((act.Tool == "code_editor" || act.Tool == "file") && (act.Operation == "apply_diff" || act.Operation == "replace"))
                    {
                        var path = act.Params?.GetStringArgument("path");
                        var diff = _artifactService.CreateArtifact(ArtifactType.Diff, $"Modified: {path}", $"Diff recorded for agent {Id}");
                        diff.AgentId = Id;
                        _artifactService.UpdateArtifact(diff);
                        if (path != null) _ragService.NotifyFileChanged(path);
                    }

                    var routerPayload = JsonSerializer.Serialize(new { Agent = act.Tool, Request = act.Params });
                    var agentRes = await _router.RouteAsync(routerPayload, ct);

                    if (agentRes.Metadata != null && agentRes.Metadata.TryGetValue("telemetry_type", out var type))
                    {
                        var detail = "";
                        if (type == "Explored") detail = $"Analyzed C# {agentRes.Metadata.GetValueOrDefault("file")} {agentRes.Metadata.GetValueOrDefault("range")}";
                        else if (type == "Edited") detail = $"Edited file {agentRes.Metadata.GetValueOrDefault("file")}";

                        OnActionCaptured?.Invoke(new ActionTelemetry { Type = type, Detail = detail, Count = 1 });
                    }

                    if ((act.Tool == "code_editor" || act.Tool == "file") && (act.Operation == "write_file" || act.Operation == "write" || act.Operation == "apply_diff" || act.Operation == "replace"))
                    {
                        var filePath = act.Params?.GetStringArgument("path") ?? act.Params?.GetStringArgument("targetfile");
                        if (!string.IsNullOrEmpty(filePath) && _projectContext.ProjectDirectory != null)
                        {
                            var roslyn = _router.GetAgent("roslyn") as RoslynService;
                            if (roslyn != null)
                            {
                                var impacts = await roslyn.GetBlastRadiusAsync(_projectContext.ProjectDirectory, filePath);
                                OnTacticalContextUpdated?.Invoke(impacts);
                            }
                        }
                    }

                    var advice = EvaluateToolResult(act, agentRes);
                    if (advice != null)
                    {
                        OnLog?.Invoke($"[Advice] {advice}");
                        History.Add(new ChatMessage { Role = "tool", ToolCallId = tc.Id, Name = tcName, Content = $"SYSTEM_ADVICE: {advice}" });
                    }
                    else
                    {
                        OnLog?.Invoke($"[Observation] {agentRes.Output?.Split('\n').FirstOrDefault() ?? "Success"}...");
                        var rawOutContent = agentRes.Output ?? "Success";
                        // Allow larger context windows before truncation
                        var outContent = rawOutContent.Length > 4000 
                            ? rawOutContent.Substring(0, 2000) + "\n...[OBSERVATION TRUNCATED]...\n" + rawOutContent.Substring(rawOutContent.Length - 2000, 2000)
                            : rawOutContent;

                        History.Add(new ChatMessage { Role = "tool", ToolCallId = tc.Id, Name = tcName, Content = outContent });
                    }
                }
            }
        }

        private async Task CompressHistoryAsync(List<ChatMessage> messages, string systemPrompt, CancellationToken ct)
        {
            var historyText = string.Join("\n", messages.Skip(1).Take(messages.Count - 4).Select(m => $"[{m.Role}]: {m.Content.Substring(0, Math.Min(m.Content.Length, 500))}"));
            var summaryPrompt = $@"{systemPrompt}

[SUMMARY TASK]
Summarize the current technical state of the session. 
1. Retain important technical facts, code structures, and architecture relationships discovered.
2. Clearly state what has been attempted and what the current task progress is.
3. Keep the summary concise but do not discard important context needed for the next steps.

CURRENT TRACE:
{historyText}";

            var sb = new StringBuilder();
            await _model.StreamResponseAsync(new List<ChatMessage>(), new Progress<string>(t => sb.Append(t)), ct, summaryPrompt, ModelRole.Primary);
            
            // Remove the old verbose messages and inject the clean recap
            messages.RemoveRange(1, messages.Count - 4);
            messages.Insert(1, new ChatMessage { Role = "user", Content = $"SESSION_RECAP (Simplified for context): {sb}" });
        }

        private string? EvaluateToolResult(AgentAction action, AgentResult result)
        {
            if (result == null) return "Tool returned no result.";
            if (!result.Success)
            {
                if (result.Output?.Contains("Unknown agent", StringComparison.OrdinalIgnoreCase) == true)
                    return $"Agent '{action.Tool}' not registered. Registered: {string.Join(", ", _router.GetAgentNames())}";

                if (result.Output?.Contains("Missing required arguments", StringComparison.OrdinalIgnoreCase) == true)
                    return result.Output; // The router already provides a schema hint
            }
            return null;
        }

        private async Task ExtractAndPersistKnowledgeAsync(CancellationToken ct)
        {
            try
            {
                // We only extract if there was some actual activity (more than just the start message)
                if (History.Count < 3) return;

                OnLog?.Invoke("\n[Memory] Analyzing session for technical discoveries...");
                
                var historySummary = string.Join("\n", History.Select(m => $"[{m.Role}]: {(m.Content.Length > 500 ? m.Content.Substring(0, 500) + "..." : m.Content)}"));
                var extractionPrompt = @$"Analyze the following session history and extract any significant technical discoveries, architectural patterns, or reusable fixes into a 'Knowledge Item'.

[SESSION HISTORY]
{historySummary}

---
RULES:
1. ONLY extract knowledge if the discovery is specific and reusable (e.g. line numbers, specific API quirks, architectural relationships).
2. If nothing significant was found, respond with 'NONE'.
3. If knowledge is found, respond with a JSON metadata block, then a '---' separator, then the full Markdown content.

JSON Schema:
{{
  ""name"": ""Concise Technical Title"",
  ""description"": ""Single sentence summary"",
  ""tags"": [""tag1"", ""tag2""]
}}";

                var responseObj = await _model.CompleteAsync(extractionPrompt, ct, "You are a technical knowledge extraction engine for Gravity AI.", ModelRole.Primary);
                var response = responseObj.Content;
                
                if (string.IsNullOrWhiteSpace(response) || response.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase))
                {
                    OnLog?.Invoke("[Memory] No new technical landmarks identified.");
                    return;
                }

                string jsonPart = "";
                string markdownPart = "";

                if (response.Contains("---"))
                {
                    var parts = response.Split(new[] { "---" }, 2, StringSplitOptions.RemoveEmptyEntries);
                    jsonPart = parts[0];
                    markdownPart = parts[1];
                }
                else if (PlanParser.TryExtractJson(response, out var json))
                {
                    jsonPart = json;
                    markdownPart = response.Replace(json, "").Trim();
                }

                if (PlanParser.TryExtractJson(jsonPart, out var finalJson))
                {
                    var item = JsonSerializer.Deserialize<KnowledgeItem>(finalJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (item != null)
                    {
                        await _knowledgeService.AddKnowledgeAsync(item, markdownPart);
                        OnLog?.Invoke($"[Memory] Persisted technical discovery: **{item.Name}**");
                    }
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[Memory] Knowledge extraction failed: {ex.Message}");
            }
        }

        public class AgentAction
        {
            [JsonPropertyName("action")] public string? Action { get; set; }
            [JsonPropertyName("tool")] public string? Tool { get; set; }
            [JsonPropertyName("operation")] public string? Operation { get; set; }
            [JsonPropertyName("params")] public AgentRequest? Params { get; set; }
            [JsonPropertyName("output")] public string? Output { get; set; }
        }
    }

    internal class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;
        public SynchronousProgress(Action<T> callback) => _callback = callback;
        public void Report(T value) => _callback(value);
    }

    public class ActionTelemetry
    {
        public string Type { get; set; } = string.Empty; // "Edited", "Thought", "Explored"
        public string Detail { get; set; } = string.Empty; // e.g. "AgentInstance.cs #L1-280"
        public int Count { get; set; }
        public double DurationMs { get; set; }
    }
}
