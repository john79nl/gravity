using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Gravity.Core;
using Gravity.Core.Agents;

namespace Gravity
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Form5  –  Conversation-driven swarm UI with camera controls.
    //
    //  Mouse wheel   : zoom in / out
    //  Left-drag     : pan the view
    //  Right-click   : pick project folder
    //  Ask button    : open prompt input
    //  Click a dot   : expand / collapse detail panel
    // ─────────────────────────────────────────────────────────────────────────

    public class Form5 : Form
    {
        private readonly SwarmCanvas5 _canvas;
        private readonly IAgentService _agentService;
        private readonly Orchestrator _orchestrator;

        public Form5(IAgentService agentService, Orchestrator orchestrator, ProjectContext projectContext)
        {
            _agentService = agentService;
            _orchestrator = orchestrator;
            SuspendLayout();
            ClientSize    = new Size(1200, 800);
            Name          = "Form5";
            Text          = "Gravity AI  –  Swarm Interface";
            BackColor     = Color.FromArgb(10, 14, 23);
            StartPosition = FormStartPosition.CenterScreen;
            ResumeLayout(false);

            _canvas = new SwarmCanvas5 { Dock = DockStyle.Fill };
            _canvas.AgentService = _agentService;
            _canvas.Orchestrator = _orchestrator;
            _canvas.ProjectContext = projectContext;
            Controls.Add(_canvas);

            this.Shown += (_, __) => _canvas.SyncExistingAgents();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  State enum
    // ─────────────────────────────────────────────────────────────────────────
    internal enum CanvasState { Idle, InputOpen, Processing, Swarm }

    // ─────────────────────────────────────────────────────────────────────────
    //  SwarmCanvas5
    // ─────────────────────────────────────────────────────────────────────────
    public class SwarmCanvas5 : UserControl
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public IAgentService? AgentService { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Orchestrator? Orchestrator { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ProjectContext? ProjectContext { get; set; }

        // ── timing ────────────────────────────────────────────────────────────
        private readonly System.Windows.Forms.Timer _timer =
            new System.Windows.Forms.Timer { Interval = 16 };

        // ── agents ────────────────────────────────────────────────────────────
        private readonly List<Agent5> _agents   = new List<Agent5>();
        private readonly Random       _rand      = new Random();
        private const   int           MaxAgents  = 22;
        private const   int           AgentsPerTurn = 3;

        // ── master node (centre in world space) ──────────────────────────────
        private PointF _master;

        // ── camera ────────────────────────────────────────────────────────────
        private float  _zoom = 1f;
        private PointF _pan  = PointF.Empty;
        private bool   _isDragging;
        private PointF _dragStart;
        private PointF _panStart;
        private const float ZoomMin = 0.2f;
        private const float ZoomMax = 4f;

        // ── state machine ─────────────────────────────────────────────────────
        private CanvasState _state = CanvasState.Idle;
        private int _targetAgentCount;
        private int  _spawnTimer        = 0;

        // ── input box ─────────────────────────────────────────────────────────
        private float    _boxOpenFrac = 0f;
        private bool     _boxReady    = false;
        private TextBox? _textBox;
        private Label?   _hint;

        // ── ask button ────────────────────────────────────────────────────────
        private Button? _askButton;

        // ── expanded detail panel ─────────────────────────────────────────────
        private Agent5? _expandedAgent;
        private Panel?  _detailPanel;
        private TextBox? _detailContent;
        private Label?   _detailHeader;
        private Label?   _detailChevron;
        private bool     _detailExpanded = true;
        private readonly System.Windows.Forms.Timer _detailRefreshTimer;

        // ── final answer display ──────────────────────────────────────────────
        private string _lastAnswer = "";
        private readonly System.Windows.Forms.Timer _answerFadeTimer;
        private float _answerAlpha = 0f;

        // ── context points (spinning info particles) ─────────────────────────
        private readonly List<ContextPoint> _contextPoints = new();
        private const int MaxContextPoints = 200;

        // ── context point info panel ─────────────────────────────────────────
        private ContextPoint? _selectedContextPoint;
        private Panel? _cpInfoPanel;
        private Label? _cpInfoLabel;
        private Label? _cpInfoTitle;

        // ── cosmetic ──────────────────────────────────────────────────────────
        private double _tick;
        private int _conversationTurns;

        // ── project folder ───────────────────────────────────────────────────
        private string? _projectFolder;

        // ── colours ───────────────────────────────────────────────────────────
        private static readonly Color Col_Cyan   = Color.FromArgb(0,   245, 212);
        private static readonly Color Col_Purple = Color.FromArgb(114,   9, 183);
        private static readonly Color Col_Blue   = Color.FromArgb(0,   180, 216);
        private static readonly Color Col_Dark   = Color.FromArgb(13,   17,  28);
        private static readonly Color Col_Glass  = Color.FromArgb(30,   38,  60);
        private static readonly Color Col_Hint   = Color.FromArgb(90,   200, 210, 220);
        private static readonly Color Col_Green  = Color.FromArgb(0,   200, 80);
        private static readonly Color Col_Orange = Color.FromArgb(255, 165, 0);
        private static readonly Color Col_Red    = Color.FromArgb(220, 50, 50);
        private static readonly Color Col_Faded  = Color.FromArgb(80,  90, 100);

        // ── box geometry ─────────────────────────────────────────────────────
        private const int BoxW = 420;
        private const int BoxH = 50;

        // ─────────────────────────────────────────────────────────────────────
        public SwarmCanvas5()
        {
            DoubleBuffered = true;
            BackColor      = Col_Dark;
            Cursor         = Cursors.Default;
            _master        = new PointF(400, 300);

            _timer.Tick += OnTick;
            _timer.Start();

            _detailRefreshTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _detailRefreshTimer.Tick += (_, __) => RefreshDetailContent();

            _answerFadeTimer = new System.Windows.Forms.Timer { Interval = 50 };
            _answerFadeTimer.Tick += OnAnswerFadeTick;

            MouseDown     += OnMouseDown;
            MouseUp       += OnMouseUp;
            MouseMove     += OnMouseMove;
            MouseWheel    += OnMouseWheel;
            Resize        += (_, __) =>
            {
                _master = new PointF(Width / 2f, Height / 2f);
                foreach (var a in _agents) a.ResetBounds(Width, Height);
                if (_boxReady) PositionBoxControls();
                if (_detailPanel != null) PositionDetailPanel();
                if (_cpInfoPanel != null && _selectedContextPoint != null) PositionContextPointInfo(_selectedContextPoint);
                PositionAskButton();
            };
        }

        // ── coordinate transforms ─────────────────────────────────────────────
        private PointF WorldToScreen(PointF world)
        {
            return new PointF(
                (world.X + _pan.X) * _zoom + Width / 2f,
                (world.Y + _pan.Y) * _zoom + Height / 2f);
        }

        private PointF ScreenToWorld(PointF screen)
        {
            return new PointF(
                (screen.X - Width / 2f) / _zoom - _pan.X,
                (screen.Y - Height / 2f) / _zoom - _pan.Y);
        }

        // ── main loop ─────────────────────────────────────────────────────────
        private void OnTick(object? sender, EventArgs e)
        {
            _tick += 0.04;

            switch (_state)
            {
                case CanvasState.Idle:
                    break;

                case CanvasState.InputOpen:
                    if (_boxOpenFrac < 1f)
                    {
                        _boxOpenFrac = Math.Min(_boxOpenFrac + 0.08f, 1f);
                        if (_boxOpenFrac >= 1f && !_boxReady)
                            CreateBoxControls();
                    }
                    break;

                case CanvasState.Processing:
                case CanvasState.Swarm:
                    foreach (var a in _agents) a.Update(_master, Width, Height);
                    float cpSpeed = _agents.Exists(a => a.Status == AgentStatus.Running) ? 4.0f : 0.3f;
                    foreach (var cp in _contextPoints) cp.Update(_master, cpSpeed);
                    break;
            }

            Invalidate();
        }

        // ── mouse: wheel zoom ─────────────────────────────────────────────────
        private void OnMouseWheel(object? sender, MouseEventArgs e)
        {
            float oldZoom = _zoom;
            float delta = e.Delta > 0 ? 1.15f : 1f / 1.15f;
            _zoom = Math.Clamp(_zoom * delta, ZoomMin, ZoomMax);

            // Zoom toward cursor: adjust pan so the world point under the cursor stays put
            PointF worldBefore = ScreenToWorld(e.Location);
            PointF worldAfter  = ScreenToWorld(e.Location);
            _pan.X += worldAfter.X - worldBefore.X;
            _pan.Y += worldAfter.Y - worldBefore.Y;

            Invalidate();
        }

        // ── mouse: drag to pan ────────────────────────────────────────────────
        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                PickProjectFolder();
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                // Check if clicking on an agent dot first (swarm state only)
                if (_state == CanvasState.Swarm && TryHandleAgentClick(e.Location))
                    return;

                // Check if clicking on a context point
                if (_state == CanvasState.Swarm && TryHandleContextPointClick(e.Location))
                    return;

                // Start drag for panning
                _isDragging = true;
                _dragStart = e.Location;
                _panStart  = _pan;
                Cursor     = Cursors.SizeAll;

                // Only open input if we didn't drag (handled in MouseUp)
                return;
            }
        }

        private void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (_isDragging)
                {
                    float dx = e.Location.X - _dragStart.X;
                    float dy = e.Location.Y - _dragStart.Y;
                    bool wasDrag = Math.Abs(dx) > 4 || Math.Abs(dy) > 4;
                    _isDragging = false;
                    Cursor = Cursors.Default;

                    if (!wasDrag)
                    {
                        // It was a click, not a drag
                        switch (_state)
                        {
                            case CanvasState.Idle:
                                if (string.IsNullOrEmpty(_projectFolder))
                                {
                                    PickProjectFolder();
                                    if (!string.IsNullOrEmpty(_projectFolder))
                                        ShowAskButton();
                                }
                                else
                                {
                                    ShowAskButton();
                                }
                                break;

                            case CanvasState.InputOpen:
                                if (!BoxRect().Contains(e.Location))
                                    CloseInput(sendPrompt: false);
                                break;

                            case CanvasState.Swarm:
                                if (_expandedAgent != null && _detailPanel != null && !_detailPanel.Bounds.Contains(e.Location))
                                    CollapseDetail();
                                if (_selectedContextPoint != null && _cpInfoPanel != null && !_cpInfoPanel.Bounds.Contains(e.Location))
                                    CollapseContextPointInfo();
                                break;
                        }
                    }
                }
            }
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                float dx = (e.Location.X - _dragStart.X) / _zoom;
                float dy = (e.Location.Y - _dragStart.Y) / _zoom;
                _pan = new PointF(_panStart.X + dx, _panStart.Y + dy);
            }
        }

        // ── agent click hit-testing ───────────────────────────────────────────
        private bool TryHandleAgentClick(Point loc)
        {
            PointF worldLoc = ScreenToWorld(loc);
            for (int i = _agents.Count - 1; i >= 0; i--)
            {
                var ag = _agents[i];
                float d = Dist(worldLoc, ag.Position);
                float hitRadius = Math.Max(ag.Size + 8, 18);

                if (d < hitRadius)
                {
                    if (_expandedAgent == ag)
                        CollapseDetail();
                    else
                        ExpandAgent(ag);
                    return true;
                }
            }

            // Check if clicking outside detail panel to close it
            if (_expandedAgent != null && _detailPanel != null && !_detailPanel.Bounds.Contains(loc))
            {
                CollapseDetail();
                return false;
            }

            return false;
        }

        // ── context point click hit-testing ──────────────────────────────────
        private bool TryHandleContextPointClick(Point loc)
        {
            PointF worldLoc = ScreenToWorld(loc);
            for (int i = _contextPoints.Count - 1; i >= 0; i--)
            {
                var cp = _contextPoints[i];
                float d = Dist(worldLoc, cp.Position);
                float hitRadius = cp.Size + 12;

                if (d < hitRadius)
                {
                    if (_selectedContextPoint == cp)
                        CollapseContextPointInfo();
                    else
                        ShowContextPointInfo(cp);
                    return true;
                }
            }
            return false;
        }

        // ── context point info panel ─────────────────────────────────────────
        private void ShowContextPointInfo(ContextPoint cp)
        {
            CollapseContextPointInfo();
            _selectedContextPoint = cp;

            int panelW = 320;
            int panelH = 120;

            _cpInfoPanel = new Panel
            {
                Size = new Size(panelW, panelH),
                BackColor = Color.FromArgb(210, 20, 28, 45),
                BorderStyle = BorderStyle.None,
            };

            var path = new GraphicsPath();
            float r = 12;
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(panelW - r * 2, 0, r * 2, r * 2, 270, 90);
            path.AddArc(panelW - r * 2, panelH - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(0, panelH - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            _cpInfoPanel.Region = new Region(path);

            string typeLabel = cp.Col == Col_Purple ? "Step"
                             : cp.Col == Col_Orange ? "Command"
                             : cp.Col == Col_Blue ? "Observation"
                             : cp.Col == Col_Red ? "Context"
                             : cp.Col == Col_Green ? "Project File"
                             : cp.Col == Col_Cyan ? "Final Answer"
                             : cp.Col == Col_Faded ? "Status"
                             : "Event";

            _cpInfoTitle = new Label
            {
                Text = typeLabel,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = cp.Col,
                Location = new Point(14, 10),
                AutoSize = true,
            };

            var sep = new Panel
            {
                Height = 1,
                Location = new Point(10, 32),
                Size = new Size(panelW - 20, 1),
                BackColor = Color.FromArgb(60, cp.Col),
            };

            _cpInfoLabel = new Label
            {
                Text = cp.Label ?? "(no details)",
                Font = new Font("Consolas", 9f),
                ForeColor = Color.FromArgb(200, 210, 220),
                Location = new Point(12, 40),
                Size = new Size(panelW - 24, panelH - 50),
                TextAlign = ContentAlignment.TopLeft,
            };

            _cpInfoPanel.Controls.Add(_cpInfoLabel);
            _cpInfoPanel.Controls.Add(sep);
            _cpInfoPanel.Controls.Add(_cpInfoTitle);

            Controls.Add(_cpInfoPanel);
            _cpInfoPanel.BringToFront();
            PositionContextPointInfo(cp);
        }

        private void PositionContextPointInfo(ContextPoint cp)
        {
            if (_cpInfoPanel == null) return;

            PointF screenPos = WorldToScreen(cp.Position);
            int pw = _cpInfoPanel.Width;
            int ph = _cpInfoPanel.Height;

            float px = screenPos.X + cp.Size * _zoom + 14;
            float py = screenPos.Y - ph / 2f;

            if (px + pw > Width - 10)
                px = screenPos.X - cp.Size * _zoom - 14 - pw;
            if (py < 10) py = 10;
            if (py + ph > Height - 10) py = Height - ph - 10;

            _cpInfoPanel.Location = new Point((int)px, (int)py);
        }

        private void CollapseContextPointInfo()
        {
            _selectedContextPoint = null;
            if (_cpInfoPanel != null)
            {
                Controls.Remove(_cpInfoPanel);
                _cpInfoPanel.Dispose();
                _cpInfoPanel = null;
            }
            _cpInfoLabel = null;
            _cpInfoTitle = null;
        }

        // ── detail panel ──────────────────────────────────────────────────────
        private void ExpandAgent(Agent5 agent)
        {
            CollapseDetail();
            _expandedAgent = agent;
            _detailExpanded = true;
            CreateDetailPanel();
            RefreshDetailContent();
            _detailRefreshTimer.Start();
        }

        private void CollapseDetail()
        {
            _detailRefreshTimer.Stop();
            _expandedAgent = null;
            if (_detailPanel != null)
            {
                Controls.Remove(_detailPanel);
                _detailPanel.Dispose();
                _detailPanel = null;
            }
            _detailContent = null;
            _detailHeader = null;
            _detailChevron = null;
        }

        private void CreateDetailPanel()
        {
            if (_expandedAgent == null) return;

            int panelW = 340;
            int panelH = 220;

            _detailPanel = new Panel
            {
                Size = new Size(panelW, panelH),
                BackColor = Color.FromArgb(200, 20, 28, 45),
                BorderStyle = BorderStyle.None,
            };

            var path = new GraphicsPath();
            float r = 12;
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(panelW - r * 2, 0, r * 2, r * 2, 270, 90);
            path.AddArc(panelW - r * 2, panelH - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(0, panelH - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            _detailPanel.Region = new Region(path);

            _detailHeader = new Label
            {
                Text = BuildDetailHeader(),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Col_Cyan,
                Location = new Point(14, 10),
                AutoSize = true,
                MaximumSize = new Size(panelW - 60, 0),
            };

            _detailChevron = new Label
            {
                Text = "v",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Col_Hint,
                Location = new Point(panelW - 26, 10),
                AutoSize = true,
                Cursor = Cursors.Hand,
            };
            _detailChevron.Click += (_, __) =>
            {
                _detailExpanded = !_detailExpanded;
                _detailChevron.Text = _detailExpanded ? "v" : ">";
                if (_detailContent != null)
                    _detailContent.Visible = _detailExpanded;
                PositionDetailPanel();
            };

            var sep = new Panel
            {
                Height = 1,
                Dock = DockStyle.None,
                Location = new Point(10, 32),
                Size = new Size(panelW - 20, 1),
                BackColor = Color.FromArgb(60, Col_Cyan),
            };

            _detailContent = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(15, 18, 30),
                ForeColor = Color.FromArgb(180, 190, 200),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 8.5f),
                Location = new Point(10, 38),
                Size = new Size(panelW - 20, panelH - 48),
                ScrollBars = ScrollBars.Vertical,
                ShortcutsEnabled = true,
                Visible = _detailExpanded,
            };

            _detailPanel.Controls.Add(_detailContent);
            _detailPanel.Controls.Add(sep);
            _detailPanel.Controls.Add(_detailChevron);
            _detailPanel.Controls.Add(_detailHeader);

            Controls.Add(_detailPanel);
            _detailPanel.BringToFront();
            PositionDetailPanel();
        }

        private void PositionDetailPanel()
        {
            if (_detailPanel == null || _expandedAgent == null) return;

            PointF screenPos = WorldToScreen(_expandedAgent.Position);
            int pw = _detailPanel.Width;
            int ph = _detailPanel.Height;

            float px = screenPos.X + _expandedAgent.Size * _zoom + 14;
            float py = screenPos.Y - ph / 2f;

            if (px + pw > Width - 10)
                px = screenPos.X - _expandedAgent.Size * _zoom - 14 - pw;
            if (py < 10) py = 10;
            if (py + ph > Height - 10) py = Height - ph - 10;

            _detailPanel.Location = new Point((int)px, (int)py);
        }

        private string BuildDetailHeader()
        {
            if (_expandedAgent == null) return "";
            var ag = _expandedAgent;
            if (ag.StepNumber > 0 && !string.IsNullOrEmpty(ag.ToolName))
                return $"Step {ag.StepNumber}: {ag.ToolName}";
            if (!string.IsNullOrEmpty(ag.Label))
                return ag.Label;
            return $"Agent ({ag.StatusLabel})";
        }

        private void RefreshDetailContent()
        {
            if (_expandedAgent == null || _detailContent == null) return;

            var ag = _expandedAgent;
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(ag.Command))
            {
                sb.AppendLine($"Command: {ag.Command}");
                sb.AppendLine();
            }

            if (ag.Output.Length > 0)
            {
                string text = ag.Output.ToString();
                if (text.Length > 3000)
                    text = "..." + text.Substring(text.Length - 3000);
                sb.Append(text);
            }
            else if (ag.Status is AgentStatus.Finished or AgentStatus.Error)
            {
                sb.AppendLine("(no output)");
            }
            else
            {
                sb.AppendLine("Waiting for response...");
            }

            _detailContent.Text = sb.ToString();
            _detailContent.SelectionStart = _detailContent.TextLength;
            _detailContent.ScrollToCaret();

            if (_detailHeader != null)
                _detailHeader.Text = BuildDetailHeader();
        }

        // ── prompt submission ─────────────────────────────────────────────────
        private async void SubmitPrompt(string prompt)
        {
            if (Orchestrator == null || AgentService == null) return;

            _state = CanvasState.Processing;

            try
            {
                var ct = CancellationToken.None;

                var (engine, intent, plan) = await Orchestrator.ClassifyAndPlanAsync(prompt, ct);

                var agent = CreateAgentNode(engine.Id);
                agent.BoundEngine = engine;
                agent.Label = intent.Type.ToString();
                agent.Status = AgentStatus.Running;

                BindEngineEvents(engine, agent);

                await Task.Run(async () =>
                {
                    try
                    {
                        await Orchestrator.ExecuteWithEngineAsync(engine, prompt, plan, ct);
                    }
                    catch (Exception ex)
                    {
                        SafeBeginInvoke(() =>
                        {
                            agent.Status = AgentStatus.Error;
                            agent.Output.AppendLine($"\n[Error] {ex.Message}");
                        });
                    }
                }, ct);
            }
            catch (Exception ex)
            {
                _state = _agents.Count > 0 ? CanvasState.Swarm : CanvasState.Idle;
                _lastAnswer = $"Error: {ex.Message}";
                _answerAlpha = 1f;
                _answerFadeTimer.Start();
            }

            if (_state == CanvasState.Processing)
                _state = CanvasState.Swarm;
        }

        private void BindEngineEvents(AppEngine engine, Agent5 agent)
        {
            engine.StatusChanged += (_, status) => SafeBeginInvoke(() =>
            {
                agent.Status = status;
                if (status is AgentStatus.Finished or AgentStatus.Error)
                {
                    SpawnContextPoint(Col_Faded, status == AgentStatus.Error ? "error" : "done");
                    if (_expandedAgent == agent)
                    {
                        RefreshDetailContent();
                        _detailRefreshTimer.Stop();
                    }
                }
            });

            engine.StepStarted += (_, e) => SafeBeginInvoke(() =>
            {
                agent.StepNumber = e.Step;
                agent.ToolName = e.Label;
                agent.Label = $"Step {e.Step}";
                SpawnContextPoint(Col_Purple, $"step {e.Step}: {e.Label}");
            });

            engine.StreamReceived += (_, token) => SafeBeginInvoke(() =>
            {
                agent.Output.Append(token);
            });

            engine.LogEmitted += (_, msg) => SafeBeginInvoke(() =>
            {
                if (msg.StartsWith("[Final Message]"))
                {
                    var answer = msg.Substring("[Final Message]".Length).TrimStart(':', ' ');
                    _lastAnswer = answer;
                    _answerAlpha = 1f;
                    _answerFadeTimer.Start();
                    agent.Output.AppendLine($"\n--- Answer ---\n{answer}");
                    SpawnContextPoint(Col_Cyan, "final answer");
                    foreach (var cp in _contextPoints) cp.IsKnowledge = true;
                }
                else if (msg.StartsWith(">> Execute:"))
                {
                    var cmd = msg.Substring(">> Execute:".Length).Trim();
                    agent.Command = cmd;
                    SpawnContextPoint(Col_Orange, cmd.Length > 25 ? cmd.Substring(0, 25) + "..." : cmd);
                }
                else if (msg.StartsWith("[Observation]") || msg.StartsWith("[Advice]"))
                {
                    var label = msg.Split(']').Length > 1 ? msg.Split(']')[1].Trim().TrimStart(':') : msg;
                    if (label.Length > 30) label = label.Substring(0, 30) + "...";
                    SpawnContextPoint(Col_Blue, label);
                    agent.Output.AppendLine(msg);
                }
                else if (msg.StartsWith("[Critic]"))
                {
                    SpawnContextPoint(Col_Orange, "critic review");
                    agent.Output.AppendLine(msg);
                }
                else if (msg.StartsWith("[Context]") || msg.StartsWith("[Compression"))
                {
                    SpawnContextPoint(Col_Red, "context compress");
                }
                else if (!msg.StartsWith("[Gravity]") && !msg.StartsWith("[Memory]"))
                {
                    agent.Output.AppendLine(msg);
                }
            });
        }

        private void SafeBeginInvoke(Action action)
        {
            if (IsHandleCreated && !IsDisposed)
                this.BeginInvoke(action);
        }

        private void SpawnContextPoint(Color color, string label)
        {
            if (_contextPoints.Count >= MaxContextPoints) return;
            _contextPoints.Add(new ContextPoint(_rand, _master, color, label));
        }

        // ── answer fade ───────────────────────────────────────────────────────
        private void OnAnswerFadeTick(object? sender, EventArgs e)
        {
            if (_answerAlpha > 0.3f)
                _answerAlpha -= 0.005f;
            else
            {
                _answerAlpha -= 0.02f;
                if (_answerAlpha <= 0f)
                {
                    _answerAlpha = 0f;
                    _answerFadeTimer.Stop();
                }
            }
            Invalidate();
        }

        // ── state transitions ─────────────────────────────────────────────────
        private void ShowAskButton()
        {
            if (_askButton == null)
            {
                _askButton = new Button
                {
                    Text      = "Ask",
                    Size      = new Size(80, 36),
                    FlatStyle = FlatStyle.Flat,
                    Font      = new Font("Segoe UI", 10f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(10, 14, 23),
                    BackColor = Col_Cyan,
                    Cursor    = Cursors.Hand,
                    Region    = RoundedRectRegion(new RectangleF(0, 0, 80, 36), 10),
                };
                _askButton.FlatAppearance.BorderSize = 0;
                _askButton.Click += (_, __) =>
                {
                    HideAskButton();
                    OpenInput();
                };
                Controls.Add(_askButton);
            }
            PositionAskButton();
            _askButton.Visible = true;
            _askButton.BringToFront();
        }

        private void HideAskButton()
        {
            if (_askButton != null) _askButton.Visible = false;
        }

        private void PositionAskButton()
        {
            if (_askButton == null) return;
            _askButton.Location = new Point(Width - 100, Height - 56);
        }

        private void OpenInput()
        {
            _state       = CanvasState.InputOpen;
            _boxOpenFrac = 0f;
            _boxReady    = false;
        }

        private void CloseInput(bool sendPrompt, string prompt = "")
        {
            DestroyBoxControls();

            if (sendPrompt && !string.IsNullOrWhiteSpace(prompt))
            {
                _conversationTurns++;
                _targetAgentCount = Math.Min(_conversationTurns * AgentsPerTurn, MaxAgents);
                _state = CanvasState.Processing;
                SubmitPrompt(prompt);
            }
            else
            {
                _state = _agents.Count > 0 ? CanvasState.Swarm : CanvasState.Idle;
            }

            _boxOpenFrac = 0f;
            _boxReady    = false;
        }

        // ── project folder ────────────────────────────────────────────────────
        private void PickProjectFolder()
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Select project folder (brain center)",
                ShowNewFolderButton = false,
            };
            if (!string.IsNullOrEmpty(_projectFolder))
                dlg.SelectedPath = _projectFolder;

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _projectFolder = dlg.SelectedPath;
                if (ProjectContext != null)
                    ProjectContext.ProjectPath = dlg.SelectedPath;
                SeedKnowledgePoints();
            }
        }

        private void SeedKnowledgePoints()
        {
            if (string.IsNullOrEmpty(_projectFolder)) return;
            try
            {
                var files = System.IO.Directory.GetFiles(_projectFolder, "*.*", System.IO.SearchOption.AllDirectories);
                int count = Math.Min(files.Length, 40);
                for (int i = 0; i < count; i++)
                {
                    string name = System.IO.Path.GetFileName(files[i]);
                    SpawnContextPoint(Col_Green, name);
                }
            }
            catch { }
        }

        // ── agent node creation ───────────────────────────────────────────────
        private Agent5 CreateAgentNode(string engineId)
        {
            var a = new Agent5(_rand, Width, Height)
            {
                EngineId = engineId,
            };
            a.SpawnAt(_master, _rand);
            _agents.Add(a);
            return a;
        }

        // ── sync existing agents from Orchestrator pool ──────────────────────
        public void SyncExistingAgents()
        {
            if (Orchestrator == null) return;

            foreach (var engine in Orchestrator.GetActiveAgents())
            {
                if (_agents.Exists(a => a.EngineId == engine.Id))
                    continue;

                var agent = CreateAgentNode(engine.Id);
                agent.BoundEngine = engine;
                agent.Status = engine.Status;
                agent.Label = engine.UserIntent;
                agent.Command = "";

                agent.Output.Clear();
                if (!string.IsNullOrEmpty(engine.FinalOutput))
                    agent.Output.AppendLine(engine.FinalOutput);

                BindEngineEvents(engine, agent);

                SpawnContextPoint(Col_Purple, engine.UserIntent.Length > 30
                    ? engine.UserIntent.Substring(0, 30) + "..." : engine.UserIntent);

                if (engine.Status == AgentStatus.Running)
                {
                    SpawnContextPoint(Col_Green, "running");
                }
                else if (engine.Status == AgentStatus.Finished && !string.IsNullOrEmpty(engine.FinalOutput))
                {
                    SpawnContextPoint(Col_Cyan, "final answer");
                }
                else if (engine.Status == AgentStatus.Error)
                {
                    SpawnContextPoint(Col_Red, "error");
                }
            }

            if (_agents.Count > 0)
            {
                _state = CanvasState.Swarm;
                foreach (var cp in _contextPoints)
                    cp.IsKnowledge = true;
            }
        }

        // ── input box controls ────────────────────────────────────────────────
        private void CreateBoxControls()
        {
            _boxReady = true;
            var r = BoxRect();

            _hint = new Label
            {
                Text      = "Ask Gravity AI...",
                ForeColor = Col_Hint,
                BackColor = Color.Transparent,
                Font      = new Font("Segoe UI", 11f, FontStyle.Italic),
                AutoSize  = false,
                Size      = new Size(r.Width - 20, r.Height),
                Location  = new Point(r.X + 14, r.Y + 2),
            };

            _textBox = new TextBox
            {
                Location    = new Point(r.X + 8, r.Y + 10),
                Size        = new Size(r.Width - 16, r.Height - 14),
                Font        = new Font("Segoe UI", 12f),
                ForeColor   = Col_Cyan,
                BackColor   = Col_Glass,
                BorderStyle = BorderStyle.None,
            };

            _textBox.TextChanged += (_, __) =>
            {
                if (_hint != null)
                    _hint.Visible = string.IsNullOrEmpty(_textBox.Text);
            };

            _textBox.KeyDown += (_, ke) =>
            {
                if (ke.KeyCode == Keys.Escape)
                {
                    ke.SuppressKeyPress = true;
                    CloseInput(sendPrompt: false);
                }
                else if (ke.KeyCode == Keys.Return)
                {
                    ke.SuppressKeyPress = true;
                    string prompt = _textBox?.Text.Trim() ?? "";
                    CloseInput(sendPrompt: true, prompt: prompt);
                }
            };

            _hint.Click += (_, __) => _textBox?.Focus();

            Controls.Add(_hint);
            Controls.Add(_textBox);
            _hint.BringToFront();
            _textBox.BringToFront();
            _textBox.Focus();
        }

        private void PositionBoxControls()
        {
            if (!_boxReady) return;
            var r = BoxRect();
            if (_textBox != null) _textBox.Location = new Point(r.X + 8, r.Y + 10);
            if (_hint    != null) _hint.Location    = new Point(r.X + 14, r.Y + 2);
        }

        private void DestroyBoxControls()
        {
            if (_textBox != null) { Controls.Remove(_textBox); _textBox.Dispose(); _textBox = null; }
            if (_hint    != null) { Controls.Remove(_hint);    _hint.Dispose();    _hint    = null; }
            _boxReady = false;
        }

        private Rectangle BoxRect()
        {
            int bx = (int)(_master.X - BoxW / 2f);
            int by = (int)(_master.Y + 46);
            bx = Math.Clamp(bx, 12, Math.Max(12, Width  - BoxW - 12));
            by = Math.Clamp(by, 12, Math.Max(12, Height - BoxH - 12));
            return new Rectangle(bx, by, BoxW, BoxH);
        }

        // ── painting ──────────────────────────────────────────────────────────
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode      = SmoothingMode.AntiAlias;
            g.TextRenderingHint  = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            if (_agents.Count > 0) DrawConnections(g);
            DrawContextPoints(g);
            DrawMasterNode(g);
            DrawAgents(g);

            if (_state == CanvasState.InputOpen)
                DrawInputBox(g);

            if (_state == CanvasState.Idle && string.IsNullOrEmpty(_projectFolder))
                DrawIdleHint(g);

            if (_answerAlpha > 0f && !string.IsNullOrEmpty(_lastAnswer))
                DrawAnswerOverlay(g);
        }

        private void DrawIdleHint(Graphics g)
        {
            double alpha = 128 + 80 * Math.Sin(_tick * 1.5);
            using var f    = new Font("Segoe UI", 10f, FontStyle.Regular);
            string    text = "click to set project folder";
            var       sz   = g.MeasureString(text, f);
            using var br   = new SolidBrush(Color.FromArgb((int)alpha, Col_Hint));
            g.DrawString(text, f, br, _master.X - sz.Width / 2f, _master.Y + 36);
        }

        private void DrawConnections(Graphics g)
        {
            using var pen = new Pen(Color.Transparent, 1f);
            for (int i = 0; i < _agents.Count; i++)
            {
                PointF si = WorldToScreen(_agents[i].Position);
                for (int j = i + 1; j < _agents.Count; j++)
                {
                    PointF sj = WorldToScreen(_agents[j].Position);
                    float d = Dist(si, sj);
                    if (d < 130f)
                    {
                        int alpha = (int)(170 * (1f - d / 130f));
                        pen.Color = Color.FromArgb(alpha, Col_Blue);
                        g.DrawLine(pen, si, sj);
                    }
                }
                PointF sm = WorldToScreen(_master);
                float dm = Dist(si, sm);
                if (dm < 260f)
                {
                    int alpha = (int)(110 * (1f - dm / 260f));
                    using var pm = new Pen(Color.FromArgb(alpha, Col_Purple), 1f);
                    g.DrawLine(pm, si, sm);
                }
            }
        }

        private void DrawContextPoints(Graphics g)
        {
            bool isRunning = _agents.Exists(a => a.Status == AgentStatus.Running);
            float glowBoost = isRunning ? 1.4f : 1f;

            // ── draw mesh lines between nearby context points ────────────────
            float linkDist = 180f;
            using var linePen = new Pen(Color.Empty, 0.8f);
            for (int i = 0; i < _contextPoints.Count; i++)
            {
                PointF si = WorldToScreen(_contextPoints[i].Position);
                for (int j = i + 1; j < _contextPoints.Count; j++)
                {
                    PointF sj = WorldToScreen(_contextPoints[j].Position);
                    float d = Dist(si, sj);
                    if (d < linkDist)
                    {
                        float fade = 1f - d / linkDist;
                        int la = (int)(fade * 90 * glowBoost);
                        linePen.Color = Color.FromArgb(la, _contextPoints[i].Col);
                        g.DrawLine(linePen, si, sj);
                    }
                }

                // Thin line to master node
                PointF sm = WorldToScreen(_master);
                float dm = Dist(si, sm);
                if (dm < 260f)
                {
                    float fade = 1f - dm / 260f;
                    linePen.Color = Color.FromArgb((int)(fade * 40), Col_Purple);
                    g.DrawLine(linePen, si, sm);
                }
            }

            // ── draw the points ─────────────────────────────────────────────
            foreach (var cp in _contextPoints)
            {
                PointF screenPos = WorldToScreen(cp.Position);

                float baseAlpha = cp.IsKnowledge ? 0.5f : 1f;
                int a = (int)(baseAlpha * 200 * glowBoost);

                float sz = cp.Size * _zoom;

                // Outer glow
                float glowSize = sz * (isRunning ? 3f : 2.5f);
                using var glow = new SolidBrush(Color.FromArgb(Math.Min(a / 4, 255), cp.Col));
                g.FillEllipse(glow, screenPos.X - glowSize, screenPos.Y - glowSize, glowSize * 2, glowSize * 2);

                // Core dot
                using var core = new SolidBrush(Color.FromArgb(Math.Min(a, 255), cp.Col));
                g.FillEllipse(core, screenPos.X - sz, screenPos.Y - sz, sz * 2, sz * 2);

                // White center highlight
                float hlR = sz * 0.4f;
                using var hl = new SolidBrush(Color.FromArgb(Math.Min(a + 30, 255), Color.White));
                g.FillEllipse(hl, screenPos.X - hlR, screenPos.Y - hlR, hlR * 2, hlR * 2);
            }
        }

        private void DrawMasterNode(Graphics g)
        {
            PointF sp = WorldToScreen(_master);
            float pulse = (14f + 4f * (float)Math.Sin(_tick * 2.2)) * _zoom;

            using var ring = new SolidBrush(Color.FromArgb(120, Col_Purple));
            g.FillEllipse(ring, sp.X - pulse, sp.Y - pulse, pulse * 2, pulse * 2);

            float coreR = 7f * _zoom;
            using var core = new SolidBrush(Col_Cyan);
            g.FillEllipse(core, sp.X - coreR, sp.Y - coreR, coreR * 2, coreR * 2);

            // Project folder name
            string label = !string.IsNullOrEmpty(_projectFolder)
                ? System.IO.Path.GetFileName(_projectFolder)
                : "";
            if (!string.IsNullOrEmpty(label))
            {
                using var f = new Font("Segoe UI", Math.Max(7f, 9f * _zoom), FontStyle.Bold);
                using var br = new SolidBrush(Col_Cyan);
                var sz = g.MeasureString(label, f);
                g.DrawString(label, f, br, sp.X - sz.Width / 2f, sp.Y + pulse + 8);
            }
        }

        private void DrawAgents(Graphics g)
        {
            foreach (var ag in _agents)
            {
                PointF sp = WorldToScreen(ag.Position);
                Color statusColor = GetAgentColor(ag);
                float gs = ag.Size * _zoom;

                int glowAlpha = (_expandedAgent == ag)
                    ? 80 + (int)(40 * Math.Sin(_tick * 4))
                    : 50 + (int)(30 * Math.Sin(_tick * 3.2 + ag.Speed));

                if (_expandedAgent == ag) gs += 2f * _zoom;

                // Glow
                using var glow = new SolidBrush(Color.FromArgb(glowAlpha, statusColor));
                g.FillEllipse(glow, sp.X - gs, sp.Y - gs, gs * 2, gs * 2);

                // Core dot
                float dotR = 3.5f * _zoom;
                using var dot = new SolidBrush(Color.White);
                g.FillEllipse(dot, sp.X - dotR, sp.Y - dotR, dotR * 2, dotR * 2);

                // Status ring
                if (ag.Status == AgentStatus.Running)
                {
                    using var ringPen = new Pen(Color.FromArgb(160, statusColor), 1.5f);
                    float ringSize = gs + (3f + (float)Math.Sin(_tick * 5 + ag.Speed) * 2f) * _zoom;
                    g.DrawEllipse(ringPen, sp.X - ringSize, sp.Y - ringSize, ringSize * 2, ringSize * 2);
                }

                // Label
                string text = ag.StepNumber > 0 ? $"Step {ag.StepNumber}" : ag.Label;
                if (!string.IsNullOrEmpty(text))
                {
                    using var f = new Font("Segoe UI", Math.Max(5f, 7f * _zoom), FontStyle.Bold);
                    using var br = new SolidBrush(Color.FromArgb(170, Col_Hint));
                    var sz = g.MeasureString(text, f);
                    g.DrawString(text, f, br, sp.X - sz.Width / 2f, sp.Y - gs - sz.Height - 4);
                }

                // Tool name
                if (ag.Status == AgentStatus.Running && !string.IsNullOrEmpty(ag.ToolName))
                {
                    using var f = new Font("Segoe UI", Math.Max(4.5f, 6.5f * _zoom));
                    using var br = new SolidBrush(Color.FromArgb(130, statusColor));
                    var toolText = ag.ToolName.Length > 20 ? ag.ToolName.Substring(0, 20) + "..." : ag.ToolName;
                    var sz = g.MeasureString(toolText, f);
                    g.DrawString(toolText, f, br, sp.X - sz.Width / 2f, sp.Y + gs + 4);
                }

                // Selection ring
                if (_expandedAgent == ag)
                {
                    using var selPen = new Pen(Color.FromArgb(200, Col_Cyan), 2f);
                    float selSize = gs + 6f * _zoom;
                    g.DrawEllipse(selPen, sp.X - selSize, sp.Y - selSize, selSize * 2, selSize * 2);
                }
            }
        }

        private Color GetAgentColor(Agent5 ag)
        {
            return ag.Status switch
            {
                AgentStatus.Running  => Col_Green,
                AgentStatus.Finished => Col_Faded,
                AgentStatus.Error    => Col_Red,
                AgentStatus.Paused   => Col_Orange,
                _ => Col_Cyan,
            };
        }

        private void DrawInputBox(Graphics g)
        {
            float frac = EaseOut(_boxOpenFrac);
            if (frac <= 0f) return;

            var baseRect = BoxRect();
            float cx = baseRect.X + baseRect.Width  / 2f;
            float cy = baseRect.Y + baseRect.Height / 2f;
            float w  = baseRect.Width  * frac;
            float h  = baseRect.Height * frac;
            var   r  = new RectangleF(cx - w / 2f, cy - h / 2f, w, h);

            using var gBrush = new SolidBrush(Color.FromArgb((int)(35 * frac), Col_Cyan));
            g.FillRectangle(gBrush, r.X - 8, r.Y - 8, r.Width + 16, r.Height + 16);

            using var bg = new SolidBrush(Color.FromArgb((int)(230 * frac), Col_Glass));
            using var gp = RoundedRect(r, 10);
            g.FillPath(bg, gp);

            using var border = new Pen(Color.FromArgb((int)(210 * frac), Col_Cyan), 1.5f);
            g.DrawPath(border, gp);

            using var linePen = new Pen(Color.FromArgb((int)(80 * frac), Col_Cyan), 1f)
                { DashStyle = DashStyle.Dot };
            g.DrawLine(linePen,
                new PointF(_master.X, _master.Y + 14),
                new PointF(_master.X, r.Y));
        }

        private void DrawAnswerOverlay(Graphics g)
        {
            if (string.IsNullOrEmpty(_lastAnswer)) return;

            int alpha = (int)(Math.Clamp(_answerAlpha, 0f, 1f) * 220);
            if (alpha <= 0) return;

            using var f = new Font("Segoe UI", 10f);
            string text = _lastAnswer.Length > 200 ? _lastAnswer.Substring(0, 200) + "..." : _lastAnswer;
            var sz = g.MeasureString(text, f, Width - 100);

            PointF sp = WorldToScreen(_master);
            float px = sp.X - sz.Width / 2f;
            float py = sp.Y + 60;
            var bg = new RectangleF(px - 12, py - 6, sz.Width + 24, sz.Height + 12);

            using var bgBrush = new SolidBrush(Color.FromArgb(alpha / 3, 20, 28, 45));
            using var bgPath = RoundedRect(bg, 8);
            g.FillPath(bgBrush, bgPath);

            using var textBrush = new SolidBrush(Color.FromArgb(alpha, Col_Cyan));
            g.DrawString(text, f, textBrush, px, py);
        }

        // ── helpers ───────────────────────────────────────────────────────────
        private static float Dist(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        private static GraphicsPath RoundedRect(RectangleF r, float rad)
        {
            var gp = new GraphicsPath();
            gp.AddArc(r.X,              r.Y,               rad * 2, rad * 2, 180, 90);
            gp.AddArc(r.Right - rad*2,  r.Y,               rad * 2, rad * 2, 270, 90);
            gp.AddArc(r.Right - rad*2,  r.Bottom - rad*2,  rad * 2, rad * 2,   0, 90);
            gp.AddArc(r.X,              r.Bottom - rad*2,  rad * 2, rad * 2,  90, 90);
            gp.CloseFigure();
            return gp;
        }

        private static Region RoundedRectRegion(RectangleF r, float rad)
        {
            using var gp = RoundedRect(r, rad);
            return new Region(gp);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Agent5  –  visual node bound to a real AppEngine
    // ─────────────────────────────────────────────────────────────────────────
    public class Agent5
    {
        public PointF Position;
        private PointF _vel;
        public  float  Speed;
        public  float  Size;
        private Random _rand;
        private int    _maxX, _maxY;

        // ── AI state ──────────────────────────────────────────────────────────
        public string   EngineId   { get; set; } = "";
        public string   Label      { get; set; } = "";
        public int      StepNumber { get; set; }
        public string   ToolName   { get; set; } = "";
        public string   Command    { get; set; } = "";
        public StringBuilder Output { get; }      = new();
        public AgentStatus Status  { get; set; }  = AgentStatus.Idle;
        public AppEngine?  BoundEngine { get; set; }

        public string StatusLabel => Status switch
        {
            AgentStatus.Running  => "running",
            AgentStatus.Finished => "done",
            AgentStatus.Error    => "error",
            AgentStatus.Paused   => "paused",
            _ => "idle",
        };

        public Agent5(Random r, int maxX, int maxY)
        {
            _rand = r;
            _maxX = maxX;
            _maxY = maxY;
            Position = new PointF(maxX / 2f, maxY / 2f);
            _vel     = new PointF(
                (float)(r.NextDouble() * 4 - 2),
                (float)(r.NextDouble() * 4 - 2));
            Speed = (float)(r.NextDouble() * 1.5 + 1.0);
            Size  = r.Next(10, 22);
        }

        public void SpawnAt(PointF origin, Random r)
        {
            Position = origin;
            double angle = r.NextDouble() * Math.PI * 2;
            float  kick  = (float)(r.NextDouble() * 4 + 2);
            _vel = new PointF(
                (float)Math.Cos(angle) * kick,
                (float)Math.Sin(angle) * kick);
        }

        public void ResetBounds(int w, int h) { _maxX = w; _maxY = h; }

        public void Update(PointF target, int w, int h)
        {
            float dx = target.X - Position.X;
            float dy = target.Y - Position.Y;
            const float pull = 0.003f;

            _vel.X += dx * pull + (float)(_rand.NextDouble() * 0.4 - 0.2);
            _vel.Y += dy * pull + (float)(_rand.NextDouble() * 0.4 - 0.2);

            float spd = MathF.Sqrt(_vel.X * _vel.X + _vel.Y * _vel.Y);
            float max  = Speed * 3f;
            if (spd > max) { _vel.X = _vel.X / spd * max; _vel.Y = _vel.Y / spd * max; }

            Position.X += _vel.X;
            Position.Y += _vel.Y;

            if (Position.X < 0 || Position.X > w) _vel.X *= -0.8f;
            if (Position.Y < 0 || Position.Y > h) _vel.Y *= -0.8f;
            Position.X = Math.Clamp(Position.X, 0, w);
            Position.Y = Math.Clamp(Position.Y, 0, h);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ContextPoint  –  persistent dot with natural velocity-based movement.
    //  Points never disappear — they become permanent "knowledge" after final.
    // ─────────────────────────────────────────────────────────────────────────
    internal class ContextPoint
    {
        public PointF Position;
        private PointF _vel;
        public float Speed;
        public float Size;
        public Color Col;
        public string Label;
        public bool IsKnowledge;

        private Random _rand;

        public ContextPoint(Random rand, PointF master, Color color, string label)
        {
            _rand = rand;
            Col = color;
            Label = label;
            IsKnowledge = false;
            Speed = (float)(rand.NextDouble() * 0.6 + 0.3);
            Size  = (float)(rand.NextDouble() * 2.5 + 1.5);

            // Start somewhere around the master node — wider spread
            float angle = (float)(rand.NextDouble() * Math.PI * 2);
            float dist  = (float)(rand.NextDouble() * 200 + 30);
            Position = new PointF(
                master.X + MathF.Cos(angle) * dist,
                master.Y + MathF.Sin(angle) * dist);

            // Initial velocity: tangential + random
            float tangentialSpeed = Speed * 0.5f;
            _vel = new PointF(
                (float)(-Math.Sin(angle) * tangentialSpeed + rand.NextDouble() * 0.8 - 0.4),
                (float)( Math.Cos(angle) * tangentialSpeed + rand.NextDouble() * 0.8 - 0.4));
        }

        public void Update(PointF master, float speedMult = 1f)
        {
            // Gentle attraction toward center — keeps the mesh from drifting off-screen
            float dx = Position.X - master.X;
            float dy = Position.Y - master.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            float maxDist = 280f;

            if (dist > maxDist)
            {
                float pull = (dist - maxDist) * 0.003f * speedMult;
                _vel.X -= (dx / dist) * pull;
                _vel.Y -= (dy / dist) * pull;
            }

            // Tangential drift — creates gentle swirling motion
            if (dist > 0.1f)
            {
                float tangentX = -dy / dist;
                float tangentY =  dx / dist;
                _vel.X += tangentX * Speed * 0.004f * speedMult;
                _vel.Y += tangentY * Speed * 0.004f * speedMult;
            }

            // Random perturbation — organic wandering
            _vel.X += (float)(_rand.NextDouble() * 0.2 - 0.1) * speedMult;
            _vel.Y += (float)(_rand.NextDouble() * 0.2 - 0.1) * speedMult;

            // Damping
            _vel.X *= 0.988f;
            _vel.Y *= 0.988f;

            // Clamp velocity
            float spd = MathF.Sqrt(_vel.X * _vel.X + _vel.Y * _vel.Y);
            float maxSpd = Speed * 2.5f * speedMult;
            if (spd > maxSpd) { _vel.X = _vel.X / spd * maxSpd; _vel.Y = _vel.Y / spd * maxSpd; }

            Position.X += _vel.X;
            Position.Y += _vel.Y;
        }
    }
}
