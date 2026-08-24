using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Gravity.Core
{
    /// <summary>
    /// Non-blocking background critic that monitors agent progress via heuristic checks
    /// and pushes advisory strings into a ConcurrentQueue when problems are detected.
    ///
    /// Zero LLM calls — purely rule-based pattern detection.
    /// The main agent loop drains the queue each step and injects advisories into history.
    /// </summary>
    public class BackgroundCritic : IDisposable
    {
        private readonly ConcurrentQueue<string> _advisoryQueue;
        private readonly ManualResetEventSlim _stepSignal;
        private readonly Thread _thread;
        private volatile bool _running;
        private volatile bool _disposed;

        // ── Tracking state (written by main thread, read by critic thread) ───
        private readonly object _stateLock = new();
        private List<ChatMessage> _lastSnapshot = new();
        private string _lastToolName = string.Empty;
        private int _totalSteps;
        private int _consecutiveFailures;
        private int _totalWrites;

        // ── Heuristic thresholds ─────────────────────────────────────────────
        private const int RepetitionThreshold = 3;
        private const int FailureLoopThreshold = 3;
        private const int StagnationThreshold = 8;

        public BackgroundCritic(ConcurrentQueue<string> advisoryQueue)
        {
            _advisoryQueue = advisoryQueue ?? throw new ArgumentNullException(nameof(advisoryQueue));
            _stepSignal = new ManualResetEventSlim(false);
            _thread = new Thread(RunLoop) { IsBackground = true, Name = "Gravity.Critic" };
        }

        // ── Public API ───────────────────────────────────────────────────────

        public void Start()
        {
            _running = true;
            _thread.Start();
        }

        /// <summary>
        /// Called by the main agent loop after each step completes.
        /// Updates tracking state and wakes the critic thread.
        /// </summary>
        public void NotifyStepCompleted(
            List<ChatMessage> historySnapshot,
            string lastToolName,
            bool lastToolSuccess,
            bool hadWriteOperation,
            int totalSteps)
        {
            if (_disposed) return;

            lock (_stateLock)
            {
                _lastSnapshot = historySnapshot;
                _lastToolName = lastToolName;
                _totalSteps = totalSteps;

                if (hadWriteOperation)
                {
                    _totalWrites++;
                }

                if (lastToolSuccess)
                {
                    _consecutiveFailures = 0;
                }
                else
                {
                    _consecutiveFailures++;
                }
            }

            _stepSignal.Set();
        }

        public void Stop()
        {
            _running = false;
            if (!_disposed)
                _stepSignal.Set();
        }

        // ── Background thread loop ───────────────────────────────────────────

        private void RunLoop()
        {
            while (_running)
            {
                try
                {
                    _stepSignal.Wait();
                    _stepSignal.Reset();

                    if (!_running) break;

                    Analyze();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Critic should never crash the agent — just keep running
                }
            }
        }

        // ── Heuristic analysis ───────────────────────────────────────────────

        private void Analyze()
        {
            List<ChatMessage> snapshot;
            string lastTool;
            int consecutiveFailures;
            int totalSteps;

            lock (_stateLock)
            {
                snapshot = _lastSnapshot;
                lastTool = _lastToolName;
                consecutiveFailures = _consecutiveFailures;
                totalSteps = _totalSteps;
            }

            if (snapshot.Count < 4 || totalSteps < 2) return;

            // ── Rule 1: Repetition — same tool called many times ──────────────
            var recentToolCalls = snapshot
                .Where(m => m.Role == "assistant" && m.ToolCalls != null)
                .SelectMany(m => m.ToolCalls!)
                .ToList();

            var recentToolNames = recentToolCalls
                .Select(tc => tc.Function?.Name ?? "")
                .ToList();

            if (recentToolNames.Count >= RepetitionThreshold)
            {
                var lastN = recentToolNames.TakeLast(RepetitionThreshold).ToList();
                if (lastN.All(t => t == lastN[0]) && !string.IsNullOrEmpty(lastN[0]))
                {
                    Push($"You've called '{lastN[0]}' {RepetitionThreshold} times in a row with no clear progress. Try a different approach or simplify your strategy.");
                    return;
                }
            }

            // ── Rule 2: Failure loop — consecutive tool failures ──────────────
            if (consecutiveFailures >= FailureLoopThreshold)
            {
                Push($"Your last {consecutiveFailures} tool calls have failed. Reconsider your approach — check file paths, verify tool availability, or ask the user for clarification.");
                return;
            }

            // ── Rule 3: Stagnation — many steps, no writes, not converging ───
            if (totalSteps >= StagnationThreshold && _totalWrites == 0)
            {
                Push($"After {totalSteps} steps with no file modifications, the task doesn't seem to be converging. Consider using code_editor.write_file or code_editor.apply_diff to make concrete changes.");
                return;
            }

            // ── Rule 4: Over-analysis — long conversation, few tool calls ────
            var toolCallCount = recentToolNames.Count;
            var messageCount = snapshot.Count(m => m.Role == "assistant");
            if (messageCount >= 6 && toolCallCount < messageCount / 3)
            {
                Push($"You've been generating text responses for {messageCount} steps but only used tools {toolCallCount} times. Use your tools more actively to make progress on the task.");
                return;
            }

            // ── Rule 5: File Thrashing — editing the same file repeatedly ────
            var recentWrites = recentToolCalls
                .Where(tc => tc.Function?.Name?.Contains("code_editor") == true &&
                            (tc.Function.Name.Contains("write_file") || tc.Function.Name.Contains("apply_diff") || tc.Function.Name.Contains("edit_lines")))
                .ToList();

            if (recentWrites.Count >= 4)
            {
                var last4Writes = recentWrites.TakeLast(4).ToList();
                var paths = last4Writes.Select(tc => {
                    var match = System.Text.RegularExpressions.Regex.Match(tc.Function?.Arguments ?? "", "\"path\"\\s*:\\s*\"([^\"]+)\"");
                    return match.Success ? match.Groups[1].Value : string.Empty;
                }).ToList();

                if (paths.All(p => p == paths[0] && !string.IsNullOrEmpty(p)))
                {
                    Push($"You have modified '{System.IO.Path.GetFileName(paths[0])}' {paths.Count} times in a row. You are likely stuck trying to fix the symptom rather than the root cause. Stop editing this file and check the HTML/DOM structure or parent components instead.");
                    return;
                }
            }
        }

        private void Push(string advisory)
        {
            if (!string.IsNullOrWhiteSpace(advisory))
                _advisoryQueue.Enqueue(advisory);
        }

        // ── Cleanup ──────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
            try { _stepSignal.Set(); } catch { }
            try { _stepSignal.Dispose(); } catch { }
        }
    }
}
