using System;
using System.Collections.Generic;
using System.IO;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Gravity.Core;
using Gravity.Core.Agents;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using System.Windows.Forms.Integration;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;
using Color = System.Drawing.Color;
using FontFamily = System.Windows.Media.FontFamily;
using System.Reflection;
using Gravity.UI;

namespace Gravity
{
    public partial class Form1 : MaterialSkin.Controls.MaterialForm
    {
        private readonly IModelClient _modelClient;
        private readonly ReasoningRouter _router;
        private readonly Orchestrator _orchestrator;
        private readonly IAgentService _agentService;
        private readonly BuildService _buildService;
        private readonly DebugService _debugService;
        private readonly RoslynService _roslyn;
        private Gravity.UI.DebugPanel? _debugPanel;
        private readonly FileSearchService _fileSearchService;
        private readonly GitService _gitService;
        private readonly IProjectContext _projectContext;
        private readonly IShellLogger _shellLogger;
        private readonly IThemeService _themeService;
        private readonly ISettingsService _settingsService;
        private readonly ISessionService _sessionService;
        private readonly IKnowledgeService _knowledgeService;
        private readonly LlamaCppServerManager _llamaCppServerManager;

        // Router-Worker pipeline
        private readonly IntentRouter _intentRouter;
        private readonly TaskPlanner _taskPlanner;
        private readonly DocxPreviewService _docxPreviewService;

        private CancellationTokenSource? _cts;
        private TaskPlan? _lastPlan;
        private AppEngine? _persistentEngine;

        private SettingsPanel _settingsPanel;
        private System.Windows.Forms.Timer _sidebarAnimationTimer;
        private int _targetSplitterDistance;
        private Bitmap _iconExplorer;
        private Bitmap _iconSessions;
        private Bitmap _iconSettings;
        private Bitmap _iconTheme;
        private Bitmap _iconClose;
        private Control? _targetSidebarPanel;
        private int _sidebarMaxWidth = 390;
        private Panel _chatPanel = null!;
        private Panel _chatScrollContainer = null!;
        private Panel _agentManagerPanel = null!;
        private FlowLayoutPanel _agentListFlow = null!;
        private string? _selectedAgentId;
        private readonly Dictionary<string, int> _unreadCounts = new();
        private RichTextBox _debugLogBox = null!;
        private readonly List<Panel> _allPanels = new();
        private readonly Dictionary<string, Gravity.UI.ChatRow?> _activeBubbles = new();
        private readonly IArtifactService _artifactService;
        private Gravity.UI.ConnectedAgentsControl? _connectedAgentsControl;
        private ToolStripButton? _chatTabBtn;
        private ToolStripButton? _debugTabBtn;
        private ToolStripButton? _settingsTabBtn;
        private readonly Dictionary<string, ApprovalRequestedEventArgs> _pendingPlanApprovals = new();

        // Unified chat row tracking (replaces CollapsibleStepGroup + CollapsibleStepPanel + ChatMessageBubble)
        private readonly Dictionary<string, Gravity.UI.ChatRow> _activeStepPanels = new();
        // Legacy step-group dictionary kept for compatibility with shutdown/clear code paths
        private readonly Dictionary<Gravity.Core.Agents.AppEngine, Gravity.UI.CollapsibleStepGroup?> _stepGroups = new();
        private static readonly Regex LinkRegex = new Regex(
            @"\[(?<label>[^\]\r\n]+)\]\((?:file:\/\/\/)?(?<path>[A-Za-z]:[^)\r\n]+)\)|(?<!\(|file:\/\/\/)\b(?<path>[A-Za-z]:[\\/][a-zA-Z0-9_\-\.\/\\ \(\)]+)(?:\.[a-zA-Z0-9]+)?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Thinking bubble fields
        private Panel? _thinkingBubblePanel;
        private System.Windows.Forms.Timer? _thinkingTimer;

        // Touch/drag scroll state
        private Point _scrollStartPoint;
        private int _scrollStartOffset;
        private bool _isScrollDragging;
        private int _thinkingFrame;
        private Label? _lblThinkingText;

        private FileSystemWatcher? _fileWatcher;
        private System.Windows.Forms.Timer? _fileAnimationTimer;
        private Gravity.UI.DocxPreviewPanel? _docxPreviewPanel; // Reused across previews
        private readonly Dictionary<string, DateTime> _recentReads = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _recentWrites = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastFileSystemEvent = DateTime.MinValue;
        private System.Windows.Forms.Timer? _debounceTimer;

        // �"?�"? Image attachment state �"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?
        private ImageAttachment? _pendingImageAttachment;
        private Panel? _imagePreviewStrip;  // thumbnail bar above the input panel
        private PictureBox? _imageThumbBox;      // small thumbnail inside the strip

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>When set, this prompt is auto-submitted when Form1 finishes loading.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string? InitialPrompt { get; set; }

        /// <summary>When set, this file or folder path is opened when Form1 finishes loading.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string? InitialFilePath { get; set; }

        public async void HandleExternalFileReceived(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                if (this.WindowState == FormWindowState.Minimized)
                {
                    this.WindowState = FormWindowState.Normal;
                }
                this.Activate();
                this.BringToFront();
                SetForegroundWindow(this.Handle);

                if (System.IO.Directory.Exists(path))
                {
                    var csprojFiles = System.IO.Directory.GetFiles(path, "*.csproj");
                    if (csprojFiles.Length > 0)
                        _projectContext.ProjectPath = csprojFiles[0];
                    else
                        _projectContext.ProjectPath = System.IO.Path.Combine(path, "workspace.csproj");

                    await RefreshSolutionExplorerAsync();
                }
                else if (System.IO.File.Exists(path))
                {
                    await OpenFileInTabAsync(path);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Form1] Error handling external file: {ex.Message}");
            }
        }

        public Form1(
            IModelClient modelClient,
            ReasoningRouter router,
            Orchestrator orchestrator,
            IAgentService agentService,
            BuildService buildService,
            DebugService debugService,
            RoslynService roslyn,
            FileSearchService fileSearchService,
            GitService gitService,
            IProjectContext projectContext,
            IShellLogger shellLogger,
            IThemeService themeService,
            ISettingsService settingsService,
            IArtifactService artifactService,
            IntentRouter intentRouter,
            TaskPlanner taskPlanner,
            LlamaCppServerManager llamaCppServerManager,
            DocxPreviewService docxPreviewService)
        {
            InitializeComponent();
            this.mainSplitContainer.BringToFront();
            this.mainDocumentArea.BringToFront();
            _modelClient = modelClient;
            _router = router;
            _orchestrator = orchestrator;
            _agentService = agentService;
            _buildService = buildService;
            _debugService = debugService;
            _roslyn = roslyn;
            _fileSearchService = fileSearchService;
            _gitService = gitService;
            _projectContext = projectContext;
            _shellLogger = shellLogger;
            _themeService = themeService;
            _settingsService = settingsService;
            _artifactService = artifactService;
            _intentRouter = intentRouter;
            _taskPlanner = taskPlanner;
            _docxPreviewService = docxPreviewService;
            _llamaCppServerManager = llamaCppServerManager;
            _sessionService = new SessionService();
            _knowledgeService = new KnowledgeService(_projectContext);

            // ── UserInputAgent ───────────────────────────────────────────────
            // Register after construction so we can inject the UI handler callback.
            var userInputAgent = new UserInputAgent(HandleUserInputAsync);
            _router.RegisterAgent("user_input", userInputAgent);

            var materialSkinManager = MaterialSkin.MaterialSkinManager.Instance;
            materialSkinManager.AddFormToManage(this);
            materialSkinManager.Theme = MaterialSkin.MaterialSkinManager.Themes.DARK;
            materialSkinManager.ColorScheme = new MaterialSkin.ColorScheme(
                MaterialSkin.Primary.BlueGrey800,
                MaterialSkin.Primary.BlueGrey900,
                MaterialSkin.Primary.BlueGrey500,
                MaterialSkin.Accent.LightBlue200,
                MaterialSkin.TextShade.WHITE);

            _roslyn.OnDiagnosticMessage += (msg) => AppendChat("Roslyn", msg);

            _artifactService.OnArtifactCreated += (artifact) => {
                this.BeginInvoke(new Action(() => {
                    ShowArtifactCard(artifact);
                }));
            };

            // ── DOCX auto-preview ────────────────────────────────────────────
            docxPreviewService.DocxPreviewReady += (filePath, html) =>
            {
                this.BeginInvoke(new Action(() =>
                {
                    ShowDocxPreview(filePath, html);
                }));
            };

            if (_modelClient is GenericOpenAIClient openAiClient)
            {
                openAiClient.OnDebugLog += (msg) => {
                    this.BeginInvoke(() => {
                        AppendDebugLog(msg);
                        if (_settingsService.Current.DebugJson)
                        {
                            var agentId = _selectedAgentId;
                            if (agentId != null && _activeBubbles.TryGetValue(agentId, out var bubble) && bubble != null)
                            {
                                if (msg.Contains(">>>> REQUEST"))
                                    bubble.AppendContent("\n[FULL PROMPT]\n" + msg);
                                else if (msg.Contains("<<<< RESPONSE") || msg.Contains("ERROR") || msg.Contains("error"))
                                    bubble.AppendContent("\n[MODEL RESPONSE]\n" + msg);
                            }
                        }
                    });
                };
            }

            SetupTabs();
            ApplyTheme();

            // Dynamic Set Folder button handled by ribbonBar now

            _shellLogger.OnLogReceived += (msg, isErr) => AppendShellLog(msg, isErr);

            // Initial view
            this.Load += async (s, e) => {
                mainSplitContainer.SplitterDistance = (int)(this.Width * 0.3);

                if (!string.IsNullOrWhiteSpace(InitialFilePath))
                {
                    await Task.Delay(300);
                    HandleExternalFileReceived(InitialFilePath);
                }

                // If a prompt was captured from the splash screen, auto-submit it
                if (!string.IsNullOrWhiteSpace(InitialPrompt))
                {
                    await Task.Delay(500); // small delay so the form is fully rendered
                    await RunPromptAsync(InitialPrompt);
                }
            };
            // AskForProjectFile(); // Removed per user request

            var connectedAgents = _router.GetAgentNames().ToList();
            if (connectedAgents.Any())
            {
                _connectedAgentsControl = new Gravity.UI.ConnectedAgentsControl(connectedAgents);
                _connectedAgentsControl.Dock = DockStyle.Top;
                _connectedAgentsControl.AgentClicked += (s, agentName) =>
                {
                    if (agentName.Equals("email", StringComparison.OrdinalIgnoreCase))
                    {
                        using var dlg = new Gravity.UI.EmailConfigDialog(_settingsService);
                        ApplyTheme(); // Restore Form1's custom backgrounds after the dialog registers with MaterialSkinManager
                        dlg.ShowDialog(this);
                    }
                    else if (agentName.Equals("whatsapp", StringComparison.OrdinalIgnoreCase))
                    {
                        using var dlg = new InputDialog("WhatsApp Number", "Please enter the recipient's WhatsApp phone number (with country code):");
                        if (dlg.ShowDialog(this) == DialogResult.OK)
                        {
                            string number = dlg.Text;
                            _ = RunPromptAsync($"Send a WhatsApp message to {number}");
                        }
                    }
                    else if (agentName.Equals("knowledge", StringComparison.OrdinalIgnoreCase))
                    {
                        var knowledgeItems = _knowledgeService?.GetKnowledgeItems();
                        using var knowledgeForm = new Gravity.UI.KnowledgeListForm(knowledgeItems);
                        knowledgeForm.ShowDialog(this);
                    }
                    else if (agentName.Equals("search", StringComparison.OrdinalIgnoreCase))
                    {
                        using var dlg = new Gravity.UI.SearchAgentConfigDialog(_settingsService);
                        ApplyTheme();
                        dlg.ShowDialog(this);
                    }
                    else if (agentName.Equals("pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        using var dlg = new Gravity.UI.PdfAgentConfigDialog(_settingsService);
                        ApplyTheme();
                        dlg.ShowDialog(this);
                    }
                };
                _chatPanel.Controls.Add(_connectedAgentsControl);
                // Ensure the chat scroll container (Dock.Fill) is frontmost in z-order 
                // so it gets laid out last and yields space to the Top-docked cognitive panel
                _chatScrollContainer.BringToFront();
            }
            else
            {
                AppendChat("System", "WARNING: No AI Agents connected. Check Program.cs registrations.");
            }

            _fileSearchService.OnFileReading += (path) => { lock (_recentReads) _recentReads[path] = DateTime.UtcNow; };
            _fileSearchService.OnFileWriting += (path) => { lock (_recentWrites) _recentWrites[path] = DateTime.UtcNow; };

            _debounceTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _debounceTimer.Tick += async (s, e) => {
                _debounceTimer.Stop();
                if (!this.IsDisposed) await RefreshSolutionExplorerAsync();
            };

            _fileAnimationTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _fileAnimationTimer.Tick += (s, e) => UpdateTreeAnimations();
            _fileAnimationTimer.Start();

            EnableDoubleBuffering(mainSplitContainer);
            EnableDoubleBuffering(mainSplitContainer.Panel1);
            EnableDoubleBuffering(mainSplitContainer.Panel2);
        }

        public static void EnableDoubleBuffering(Control control)
        {
            var prop = typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            prop?.SetValue(control, true, null);
        }

        private void SetupTabs()
        {
            tabStrip.Visible = true;

            _chatPanel = new Panel { Dock = DockStyle.Fill, Visible = true };

            _agentManagerPanel = new Panel { Dock = DockStyle.Top, Height = 85, BackColor = Color.FromArgb(30, 30, 40), Padding = new Padding(5) };
            _agentListFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = true };
            _agentManagerPanel.Controls.Add(_agentListFlow);

            var btnNewAgent = new Button
            {
                Text = "+ NEW TASK",
                Dock = DockStyle.Right,
                Width = 120,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(45, 48, 65),
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                Margin = new Padding(5)
            };
            btnNewAgent.FlatAppearance.BorderSize = 0;
            btnNewAgent.Click += (s, e) => { inputBox.Focus(); AppendChat("System", "Type your task in the box below to spawn a new agent."); };
            _agentManagerPanel.Controls.Add(btnNewAgent);

            _chatFlow = new Gravity.UI.DoubleBufferedFlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(30, 30, 40),
                Padding = new Padding(10, 10, 10, 100)
            };
            _chatFlow.SizeChanged += (s, e) =>
            {
                int chatWidth = _chatFlow.ClientSize.Width;
                if (chatWidth < 100) return;

                _chatFlow.SuspendLayout();
                foreach (Control ctrl in _chatFlow.Controls)
                {
                    ResizeChatControl(ctrl, chatWidth);
                }
                _chatFlow.ResumeLayout(true);
            };
            _chatScrollContainer = new Gravity.UI.DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(30, 30, 40)
            };
            _chatScrollContainer.SizeChanged += (s, e) =>
            {
                int w = _chatScrollContainer.ClientSize.Width;
                if (w > 100)
                {
                    _chatFlow.Width = w;
                }
            };
            _chatScrollContainer.Controls.Add(_chatFlow);
            _chatScrollContainer.MouseDown += ChatScrollContainer_MouseDown;
            _chatScrollContainer.MouseMove += ChatScrollContainer_MouseMove;
            _chatScrollContainer.MouseUp += ChatScrollContainer_MouseUp;
            RegisterMouseWheelRecursive(_chatScrollContainer);
            _chatPanel.Controls.Add(_chatScrollContainer);
            _chatScrollContainer.BringToFront();
            _allPanels.Add(_chatPanel);
            mainDocumentArea.Controls.Add(_chatPanel);

            _chatTabBtn = CreateTabButton("Chat", _chatPanel, "chat");
            SelectTab(_chatTabBtn); // Set it as the internally active tab

            _orchestrator.OnAgentSpawned += (agent) =>
            {
                if (IsHandleCreated && !IsDisposed)
                    this.BeginInvoke(() => CreateAgentCard(agent));
            };

            _iconExplorer = LoadIconSafely("Resources\\icon_explorer.png");
            _iconSessions = LoadIconSafely("Resources\\icon_sessions.png");
            _iconSettings = LoadIconSafely("Resources\\icon_settings.png");
            _iconTheme = LoadIconSafely("Resources\\icon_theme.png");
            _iconClose = LoadIconSafely("Resources\\icon_close.png");

            _sidebarAnimationTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _sidebarAnimationTimer.Tick += SidebarAnimationTimer_Tick;

            activityBar.Visible = true;


            // Settings Panel Setup (Main Document Tab)
            _settingsPanel = new SettingsPanel(_settingsService, _llamaCppServerManager);
            _settingsPanel.Dock = DockStyle.Fill;
            var settingsHostPanel = new Panel { Dock = DockStyle.Fill, BackColor = System.Drawing.Color.FromArgb(20, 22, 34) };
            settingsHostPanel.Controls.Add(_settingsPanel);
            settingsHostPanel.Tag = "__settings__";
            _allPanels.Add(settingsHostPanel);
            mainDocumentArea.Controls.Add(settingsHostPanel);
            _settingsTabBtn = CreateTabButton("⚙ Settings", settingsHostPanel, "__settings__");
            _settingsPanel.OnCloseRequested = () => {
                if (_chatTabBtn != null) SelectTab(_chatTabBtn);
            };

            // ── Debug Panel Tab & Toolbar ───────────────────────────────────────
            _debugPanel = new Gravity.UI.DebugPanel(_debugService, _themeService);
            _debugPanel.Dock = DockStyle.Fill;
            _debugPanel.OnStartRequested = async () => await StartDebugSessionAsync();

            _debugService.OnBreakpointHit += (bp) =>
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    this.BeginInvoke(async () =>
                    {
                        if (!string.IsNullOrEmpty(bp.FilePath) && System.IO.File.Exists(bp.FilePath))
                        {
                            await OpenFileInTabAsync(bp.FilePath);
                        }
                    });
                }
            };

            var debugHostPanel = new Panel { Dock = DockStyle.Fill, BackColor = System.Drawing.Color.FromArgb(12, 14, 35) };
            debugHostPanel.Controls.Add(_debugPanel);
            debugHostPanel.Tag = "__debug__";
            _allPanels.Add(debugHostPanel);
            mainDocumentArea.Controls.Add(debugHostPanel);
            _debugTabBtn = CreateTabButton("🐞 Debug", debugHostPanel, "__debug__");

            SetupRibbonRunButton();

            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;

            this.Load += async (s, e) =>
            {
                mainSplitContainer.Panel1Collapsed = true;
                UpdateSidebarButtonHighlights();
                if (!string.IsNullOrEmpty(_projectContext.ProjectDirectory))
                {
                    ToggleSidebarPanel(explorerPanel);
                    await RefreshSolutionExplorerAsync();
                }
            };
        }

        private void Form1_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5 && !e.Shift)
            {
                e.Handled = true;
                if (_debugService.State == DebugSessionState.Running)
                {
                    var debugHost = _allPanels.FirstOrDefault(p => p.Tag as string == "__debug__");
                    if (debugHost != null) SwitchToPanel(debugHost);
                }
                else
                {
                    _ = StartDebugSessionAsync();
                }
            }
            else if (e.KeyCode == Keys.F5 && e.Shift)
            {
                e.Handled = true;
                _debugService.Stop();
            }
        }

        private void SwitchToPanel(Control? contentPan)
        {
            if (contentPan == null) return;

            foreach (var p in _allPanels)
            {
                p.Visible = (p == contentPan);
                if (p == contentPan) p.BringToFront();
            }

            // Update Breadcrumb and Status Bar for the new panel
            string? path = contentPan.Tag as string;
            bool isCodeTab = !string.IsNullOrEmpty(path) && System.IO.File.Exists(path);
            breadcrumbBar.Visible = isCodeTab;
            if (btnSaveFile != null)
            {
                btnSaveFile.Visible = isCodeTab;
                btnSaveFile.Tag = path;
            }
            UpdateBreadcrumb(isCodeTab ? path : null);

            if (isCodeTab)
            {
                foreach (Control outer in contentPan.Controls)
                {
                    if (outer is Panel)
                    {
                        foreach (Control inner in outer.Controls)
                        {
                            if (inner is ElementHost host && host.Child is TextEditor editor)
                            {
                                UpdateStatusBar(editor);
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                statusLabelLeft.Text = "";
                statusLabelRight.Text = "";
            }
        }

        private void CreateAgentCard(AppEngine engine)
        {
            var c = _themeService.Colors;
            var card = new Panel
            {
                Width = 160,
                Height = 60,
                Margin = new Padding(5),
                BackColor = Color.FromArgb(45, 45, 60),
                Cursor = Cursors.Hand,
                Tag = engine.Id
            };

            var lblId = new Label
            {
                Text = engine.Id.ToUpper(),
                Dock = DockStyle.Top,
                ForeColor = Color.Gray,
                AutoSize = true,
                Font = new Font("Segoe UI", 7, FontStyle.Bold),
                Padding = new Padding(5, 2, 0, 0)
            };

            var lblIntent = new Label
            {
                Text = engine.UserIntent,
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(25, 0, 20, 0) // Leave space for status dot and close button
            };

            var statusDot = new Panel
            {
                Width = 10,
                Height = 10,
                Location = new Point(8, 25),
                BackColor = GetStatusColor(engine.Status)
            };
            // Round the dot
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddEllipse(0, 0, 10, 10);
            statusDot.Region = new Region(path);

            var btnClose = new Label
            {
                Text = "�-",
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 12),
                Size = new Size(20, 20),
                Location = new Point(135, 5),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.TopCenter
            };
            btnClose.Click += (s, e) => {
                _orchestrator.RemoveAgent(engine.Id);
                _stepGroups.Remove(engine);
                _agentListFlow.Controls.Remove(card);
                if (_selectedAgentId == engine.Id)
                {
                    _selectedAgentId = null;
                    _chatFlow.Controls.Clear();
                    AppendChat("System", "Select an agent to view conversation.");
                }
            };
            btnClose.MouseEnter += (s, e) => btnClose.ForeColor = Color.White;
            btnClose.MouseLeave += (s, e) => btnClose.ForeColor = Color.Gray;

            var unreadBadge = new Label
            {
                Text = "",
                BackColor = c.Accent,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 7, FontStyle.Bold),
                Size = new Size(16, 16),
                Location = new Point(135, 35),
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };
            var badgePath = new System.Drawing.Drawing2D.GraphicsPath();
            badgePath.AddEllipse(0, 0, 16, 16);
            unreadBadge.Region = new Region(badgePath);

            card.Controls.Add(unreadBadge);
            card.Controls.Add(btnClose);
            card.Controls.Add(statusDot);
            card.Controls.Add(lblIntent);
            card.Controls.Add(lblId);

            Action selectAction = () => SelectAgent(engine);
            card.Click += (s, e) => selectAction();
            lblIntent.Click += (s, e) => selectAction();
            lblId.Click += (s, e) => selectAction();

            _agentListFlow.Controls.Add(card);

            engine.StatusChanged += (_, status) => this.BeginInvoke(() => {
                statusDot.BackColor = GetStatusColor(status);
                if (_selectedAgentId == engine.Id)
                {
                    if (status == AgentStatus.Running) ShowThinkingBubble();
                    else HideThinkingBubble();
                }
                if (status != AgentStatus.Running)
                {
                    _connectedAgentsControl?.ClearAllActive();
                }

                if (status == AgentStatus.Finished || status == AgentStatus.Error || status == AgentStatus.Idle)
                {
                    if (_stepGroups.TryGetValue(engine, out var group) && group != null)
                    {
                        group.Collapse();
                    }
                }
            });

            engine.LogEmitted += (_, msg) => {
                this.BeginInvoke(() => {
                    if (msg.StartsWith("[Step ") || msg.StartsWith("[Memory]") || msg.Contains("[Thinking - Step") || msg.StartsWith(">>") || msg.StartsWith("[Context]") || msg.StartsWith("[Executor]"))
                    {
                        AppendDebugLog("[Agent Log] " + msg);
                    }
                    if (msg.StartsWith(">> Execute: "))
                    {
                        var toolPart = msg.Substring(">> Execute: ".Length).Trim();
                        var agentName = toolPart.Split('.')[0];
                        _connectedAgentsControl?.SetAgentActiveByTool(agentName, true);
                    }

                    // Final answer and critical errors are ALWAYS written to the bubble,
                    // regardless of which agent is currently selected. Without this, if the
                    // user has switched away (or the UI hasn't selected the agent yet), the
                    // answer lands only in the unread badge and is never displayed.
                    if (msg.StartsWith("[Final Message]") || msg.StartsWith("[Final]"))
                    {
                        HideThinkingBubble();
                        var answer = msg.StartsWith("[Final Message]")
                            ? msg.Substring("[Final Message]".Length).TrimStart(':', ' ')
                            : msg.Substring("[Final]".Length).TrimStart(':', ' ');
                        var bubble = EnsureAgentBubble(engine);
                        bubble.AppendContent(answer);
                        ScrollToBottom();

                        // Replace raw tool-call JSON in the step panel with a clean summary
                        if (_activeStepPanels.TryGetValue(engine.Id, out var stepPanel) && stepPanel != null)
                        {
                            var cleanAnswer = answer.Length > 200 ? answer.Substring(0, 200) + "..." : answer;
                            stepPanel.SetContent($"Answer delivered.\n\n{cleanAnswer}");
                        }
                        return;
                    }

                    if (msg.StartsWith("[Critical Error]"))
                    {
                        HideThinkingBubble();
                        AppendChat("System", msg);
                        return;
                    }

                    if (_selectedAgentId == engine.Id)
                    {
                        if (msg.Contains("[Thinking - Step"))
                        {
                            // Already handled
                        }
                        else if (msg.StartsWith("[Warning]"))
                        {
                            AppendChat("System", msg);
                        }
                        else if (msg.StartsWith("[Observation]") || msg.StartsWith("[Advice]"))
                        {
                            var content = msg.Contains("]:") ? msg.Substring(msg.IndexOf("]:") + 2).Trim() : msg;
                            if (_activeStepPanels.TryGetValue(engine.Id, out var activePanel) && activePanel != null)
                            {
                                activePanel.AppendContent("\n" + content + "\n");
                            }
                        }
                    }
                    else
                    {
                        IncrementUnread(engine.Id, unreadBadge);
                    }
                });
            };
            engine.StreamReceived += (_, msg) => {
                this.BeginInvoke(() => {
                    if (_selectedAgentId == engine.Id)
                    {
                        if (_activeStepPanels.TryGetValue(engine.Id, out var activePanel) && activePanel != null)
                        {
                            activePanel.AppendContent(msg);
                        }
                        else
                        {
                            var row = AddChatRow(Gravity.UI.ChatRowType.Step, "Thinking...", "");
                            _activeStepPanels[engine.Id] = row;
                            row.AppendContent(msg);
                        }
                    }
                });
            };
            engine.StepStarted += (_, e) => {
                this.BeginInvoke(() => {
                    _connectedAgentsControl?.ClearAllActive();
                    if (_selectedAgentId == engine.Id)
                    {
                        var row = AddChatRow(Gravity.UI.ChatRowType.Step, $"Step {e.Step}", "Reasoning...");
                        _activeStepPanels[engine.Id] = row;
                        ScrollToBottom();
                    }
                });
            };

            engine.ApprovalRequested += async (_, e) => {
                if (e.Action.Tool == "gravity" && e.Action.Operation == "propose")
                {
                    _pendingPlanApprovals[engine.Id] = e;
                }
                else
                {
                    bool approved = await RequestToolApprovalAsync(engine, e.Action);
                    e.Completion.SetResult(approved);
                }
            };

            engine.ActionParsed += (_, e) => {
                this.BeginInvoke(() => {
                    var actionLabel = BuildActionLabel(e.ToolName, e.TargetPath);
                    if (_selectedAgentId == engine.Id &&
                        _activeStepPanels.TryGetValue(engine.Id, out var stepRow) &&
                        stepRow != null)
                    {
                        stepRow.UpdateLabel(actionLabel);
                    }
                    if (_lblThinkingText != null && !_lblThinkingText.IsDisposed)
                    {
                        _lblThinkingText.Text = actionLabel;
                    }
                });
            };

            engine.TelemetryCaptured += (_, telemetry) => {
                this.BeginInvoke(() => {
                    _connectedAgentsControl?.ClearAllActive();
                    if (_selectedAgentId == engine.Id && !string.IsNullOrEmpty(telemetry.Detail))
                    {
                        AddChatRow(Gravity.UI.ChatRowType.Step, "Info", telemetry.Detail);
                        ScrollToBottom();
                    }
                });
            };

            // Auto-select if no engine is currently selected
            if (string.IsNullOrEmpty(_selectedAgentId)) SelectAgent(engine);
        }

        /// <summary>Maps a raw tool name + optional target/command into a human-readable step label.</summary>
        private static string BuildActionLabel(string toolName, string? path)
        {
            var op = toolName.Contains('.') ? toolName.Split('.')[1].ToLowerInvariant() : toolName.ToLowerInvariant();
            var target = string.IsNullOrEmpty(path) ? "" : $" {path}";
            return op switch
            {
                "read_file" or "read_range" or "read"         => $"\U0001f4d6 Reading{target}",
                "replace"   or "edit"       or "apply_diff"   => $"\u270f\ufe0f Editing{target}",
                "apply_patches" or "patches"                   => $"\u270f\ufe0f Patching{target}",
                "write_file" or "write"                        => $"\U0001f4be Writing{target}",
                "search_in_files" or "search" or "grep"        => $"\U0001f50d Searching{target}",
                "run_command" or "shell" or "powershell"       => $"\u2699\ufe0f Running{target}",
                "list_directory" or "list"                     => $"\U0001f4c2 Listing{target}",
                "propose"                                      => $"\U0001f4cb Creating plan{target}",
                "search_web" or "web"                          => $"\U0001f310 Searching web{target}",
                _                                              => $"\u2699\ufe0f {toolName}{target}"
            };
        }

        private async Task<bool> RequestToolApprovalAsync(AppEngine engine, AgentAction action)
        {
            var tcs = new TaskCompletionSource<bool>();
            this.Invoke(() => {
                try
                {
                    var verb = action.Params?.Verb ?? action.Operation ?? "";
                    var argMap = action.Params?.ArgMap ?? new Dictionary<string, object>();
                    var command = verb + " " + string.Join(" ", argMap.Select(kv => $"{kv.Key}=\"{kv.Value}\""));

                    int targetWidth = Math.Max(200, _chatFlow.ClientSize.Width - 50);

                    var cardPanel = new Panel
                    {
                        Width = targetWidth,
                        Height = 44,
                        Margin = new Padding(12, 6, 12, 6),
                        BackColor = Color.FromArgb(24, 26, 38),
                        Tag = "ApprovalCardPanel"
                    };

                    var flow = new FlowLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        FlowDirection = FlowDirection.LeftToRight,
                        WrapContents = false,
                        Padding = new Padding(10, 8, 10, 8),
                        BackColor = Color.Transparent
                    };

                    var label = new Label
                    {
                        Text = $"⚡ Action Required: {verb}",
                        ForeColor = Color.FromArgb(230, 230, 240),
                        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                        AutoSize = true,
                        Margin = new Padding(0, 3, 10, 0),
                        TextAlign = ContentAlignment.MiddleLeft
                    };

                    var toolTip = new ToolTip();
                    toolTip.SetToolTip(label, command);

                    var viewBtn = new Button
                    {
                        Text = "🔍 View",
                        Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                        ForeColor = Color.FromArgb(180, 185, 210),
                        BackColor = Color.FromArgb(36, 39, 56),
                        FlatStyle = FlatStyle.Flat,
                        Cursor = Cursors.Hand,
                        Size = new Size(68, 26),
                        Margin = new Padding(0, 0, 12, 0)
                    };
                    viewBtn.FlatAppearance.BorderSize = 1;
                    viewBtn.FlatAppearance.BorderColor = Color.FromArgb(60, 64, 85);
                    toolTip.SetToolTip(viewBtn, command);

                    viewBtn.Click += (s, e) =>
                    {
                        MessageBox.Show(command, $"Command Details ({verb})", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    };

                    var allowBtn = new Button
                    {
                        Text = "✔",
                        Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                        ForeColor = Color.White,
                        BackColor = Color.FromArgb(46, 160, 67),
                        FlatStyle = FlatStyle.Flat,
                        Cursor = Cursors.Hand,
                        Size = new Size(32, 26),
                        Margin = new Padding(0, 0, 8, 0)
                    };
                    allowBtn.FlatAppearance.BorderSize = 0;
                    toolTip.SetToolTip(allowBtn, "Allow / Execute Command");

                    var denyBtn = new Button
                    {
                        Text = "✖",
                        Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                        ForeColor = Color.White,
                        BackColor = Color.FromArgb(218, 54, 51),
                        FlatStyle = FlatStyle.Flat,
                        Cursor = Cursors.Hand,
                        Size = new Size(32, 26),
                        Margin = new Padding(0, 0, 0, 0)
                    };
                    denyBtn.FlatAppearance.BorderSize = 0;
                    toolTip.SetToolTip(denyBtn, "Decline / Deny Command");

                    allowBtn.Click += (s, e) =>
                    {
                        tcs.SetResult(true);
                        _chatFlow.Controls.Remove(cardPanel);
                        cardPanel.Dispose();
                    };

                    denyBtn.Click += (s, e) =>
                    {
                        tcs.SetResult(false);
                        _chatFlow.Controls.Remove(cardPanel);
                        cardPanel.Dispose();
                    };

                    flow.Controls.Add(label);
                    flow.Controls.Add(viewBtn);
                    flow.Controls.Add(allowBtn);
                    flow.Controls.Add(denyBtn);

                    cardPanel.Controls.Add(flow);
                    var targetEngine = _stepGroups.Keys.FirstOrDefault(e => e.Id == engine.Id);
                    if (targetEngine != null && _stepGroups.TryGetValue(targetEngine, out var group) && group != null)
                    {
                        group.AddArtifactCard(cardPanel);
                    }
                    else
                    {
                        _chatFlow.Controls.Add(cardPanel);
                    }
                    _chatFlow.PerformLayout();
                    this.BeginInvoke(new Action(() => { ScrollToBottom(); }));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error showing approval panel: " + ex.ToString());
                    tcs.SetResult(false);
                }
            });
            return await tcs.Task;
        }

        // ── HandleUserInputAsync ─────────────────────────────────────────────────
        // Called by UserInputAgent when the LLM needs user input mid-task.
        // Renders the appropriate widget in the chat and awaits the response.
        private Task<string> HandleUserInputAsync(UserInputRequest req)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            this.Invoke(() =>
            {
                try
                {
                    int targetWidth = Math.Max(200, _chatFlow.ClientSize.Width - 50);

                    switch (req.Kind)
                    {
                        // ── ask ─────────────────────────────────────────────────
                        case UserInputKind.Ask:
                        {
                            var card = new Panel
                            {
                                Width     = targetWidth,
                                AutoSize  = true,
                                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                Margin    = new Padding(12, 6, 12, 6),
                                BackColor = Color.FromArgb(28, 30, 45),
                                Tag       = "UserInputCard"
                            };
                            var vstack = new FlowLayoutPanel
                            {
                                Dock          = DockStyle.Fill,
                                FlowDirection = FlowDirection.TopDown,
                                WrapContents  = false,
                                AutoSize      = true,
                                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                                Padding       = new Padding(12, 10, 12, 10),
                                BackColor     = Color.Transparent
                            };

                            var questionLabel = new Label
                            {
                                Text      = "🤔 " + req.Question,
                                ForeColor = Color.FromArgb(200, 210, 240),
                                Font      = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                                AutoSize  = true,
                                MaximumSize = new Size(targetWidth - 40, 0),
                                Margin    = new Padding(0, 0, 0, 8)
                            };

                            var textBox = new TextBox
                            {
                                Width       = targetWidth - 40,
                                Height      = 30,
                                BackColor   = Color.FromArgb(36, 39, 56),
                                ForeColor   = Color.FromArgb(220, 225, 245),
                                BorderStyle = BorderStyle.FixedSingle,
                                Font        = new Font("Segoe UI", 9.5f),
                                PlaceholderText = req.Placeholder,
                                Margin      = new Padding(0, 0, 0, 8)
                            };

                            var sendBtn = new Button
                            {
                                Text      = "Send ↵",
                                Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                                ForeColor = Color.White,
                                BackColor = Color.FromArgb(46, 120, 200),
                                FlatStyle = FlatStyle.Flat,
                                Cursor    = Cursors.Hand,
                                Size      = new Size(80, 28),
                                Margin    = new Padding(0)
                            };
                            sendBtn.FlatAppearance.BorderSize = 0;

                            void Submit()
                            {
                                var answer = textBox.Text.Trim();
                                tcs.TrySetResult(answer);
                                _chatFlow.Controls.Remove(card);
                                card.Dispose();
                            }

                            sendBtn.Click    += (_, __) => Submit();
                            textBox.KeyDown  += (_, ke)  => { if (ke.KeyCode == Keys.Return) { ke.SuppressKeyPress = true; Submit(); } };

                            vstack.Controls.Add(questionLabel);
                            vstack.Controls.Add(textBox);
                            vstack.Controls.Add(sendBtn);
                            card.Controls.Add(vstack);
                            _chatFlow.Controls.Add(card);
                            _chatFlow.PerformLayout();
                            textBox.Focus();
                            break;
                        }

                        // ── confirm ─────────────────────────────────────────────
                        case UserInputKind.Confirm:
                        {
                            var card = new Panel
                            {
                                Width     = targetWidth,
                                Height    = 52,
                                Margin    = new Padding(12, 6, 12, 6),
                                BackColor = Color.FromArgb(28, 30, 45),
                                Tag       = "UserInputCard"
                            };
                            var row = new FlowLayoutPanel
                            {
                                Dock          = DockStyle.Fill,
                                FlowDirection = FlowDirection.LeftToRight,
                                WrapContents  = false,
                                Padding       = new Padding(10, 10, 10, 10),
                                BackColor     = Color.Transparent
                            };

                            var lbl = new Label
                            {
                                Text      = "❓ " + req.Question,
                                ForeColor = Color.FromArgb(200, 210, 240),
                                Font      = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                                AutoSize  = true,
                                Margin    = new Padding(0, 3, 12, 0)
                            };

                            var yesBtn = new Button
                            {
                                Text      = "✔ Yes",
                                Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                                ForeColor = Color.White,
                                BackColor = Color.FromArgb(46, 160, 67),
                                FlatStyle = FlatStyle.Flat,
                                Cursor    = Cursors.Hand,
                                Size      = new Size(64, 28),
                                Margin    = new Padding(0, 0, 8, 0)
                            };
                            yesBtn.FlatAppearance.BorderSize = 0;

                            var noBtn = new Button
                            {
                                Text      = "✖ No",
                                Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                                ForeColor = Color.White,
                                BackColor = Color.FromArgb(218, 54, 51),
                                FlatStyle = FlatStyle.Flat,
                                Cursor    = Cursors.Hand,
                                Size      = new Size(60, 28),
                                Margin    = new Padding(0)
                            };
                            noBtn.FlatAppearance.BorderSize = 0;

                            yesBtn.Click += (_, __) => { tcs.TrySetResult("yes"); _chatFlow.Controls.Remove(card); card.Dispose(); };
                            noBtn.Click  += (_, __) => { tcs.TrySetResult("no");  _chatFlow.Controls.Remove(card); card.Dispose(); };

                            row.Controls.Add(lbl);
                            row.Controls.Add(yesBtn);
                            row.Controls.Add(noBtn);
                            card.Controls.Add(row);
                            _chatFlow.Controls.Add(card);
                            _chatFlow.PerformLayout();
                            break;
                        }

                        // ── choose ──────────────────────────────────────────────
                        case UserInputKind.Choose:
                        {
                            var options = req.Options ?? Array.Empty<string>();
                            var card = new Panel
                            {
                                Width    = targetWidth,
                                AutoSize = true,
                                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                Margin   = new Padding(12, 6, 12, 6),
                                BackColor = Color.FromArgb(28, 30, 45),
                                Tag      = "UserInputCard"
                            };
                            var vstack = new FlowLayoutPanel
                            {
                                Dock          = DockStyle.Fill,
                                FlowDirection = FlowDirection.TopDown,
                                WrapContents  = false,
                                AutoSize      = true,
                                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                                Padding       = new Padding(12, 10, 12, 10),
                                BackColor     = Color.Transparent
                            };

                            var questionLabel = new Label
                            {
                                Text      = "📋 " + req.Question,
                                ForeColor = Color.FromArgb(200, 210, 240),
                                Font      = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                                AutoSize  = true,
                                MaximumSize = new Size(targetWidth - 40, 0),
                                Margin    = new Padding(0, 0, 0, 8)
                            };
                            vstack.Controls.Add(questionLabel);

                            // Pill buttons for each option
                            var pillRow = new FlowLayoutPanel
                            {
                                FlowDirection = FlowDirection.LeftToRight,
                                WrapContents  = true,
                                AutoSize      = true,
                                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                                Width         = targetWidth - 40,
                                BackColor     = Color.Transparent,
                                Margin        = new Padding(0)
                            };

                            foreach (var opt in options)
                            {
                                var pill = new Button
                                {
                                    Text      = opt.Trim(),
                                    Font      = new Font("Segoe UI", 9f, FontStyle.Regular),
                                    ForeColor = Color.FromArgb(200, 215, 245),
                                    BackColor = Color.FromArgb(40, 44, 65),
                                    FlatStyle = FlatStyle.Flat,
                                    Cursor    = Cursors.Hand,
                                    AutoSize  = true,
                                    Margin    = new Padding(0, 0, 6, 6)
                                };
                                pill.FlatAppearance.BorderSize  = 1;
                                pill.FlatAppearance.BorderColor = Color.FromArgb(70, 80, 110);

                                var captured = opt.Trim();
                                pill.Click += (_, __) =>
                                {
                                    tcs.TrySetResult(captured);
                                    _chatFlow.Controls.Remove(card);
                                    card.Dispose();
                                };
                                pillRow.Controls.Add(pill);
                            }

                            vstack.Controls.Add(pillRow);
                            card.Controls.Add(vstack);
                            _chatFlow.Controls.Add(card);
                            _chatFlow.PerformLayout();
                            break;
                        }

                        // ── approve_command ──────────────────────────────────────
                        case UserInputKind.ApproveCommand:
                        {
                            // Reuse the same ⚡ approval bar pattern as RequestToolApprovalAsync
                            var cardPanel = new Panel
                            {
                                Width     = targetWidth,
                                Height    = 44,
                                Margin    = new Padding(12, 6, 12, 6),
                                BackColor = Color.FromArgb(24, 26, 38),
                                Tag       = "ApprovalCardPanel"
                            };
                            var flow = new FlowLayoutPanel
                            {
                                Dock          = DockStyle.Fill,
                                FlowDirection = FlowDirection.LeftToRight,
                                WrapContents  = false,
                                Padding       = new Padding(10, 8, 10, 8),
                                BackColor     = Color.Transparent
                            };

                            var label = new Label
                            {
                                Text      = $"⚡ {req.CommandVerb}: {req.Command}",
                                ForeColor = Color.FromArgb(230, 230, 240),
                                Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                                AutoSize  = true,
                                Margin    = new Padding(0, 3, 10, 0)
                            };

                            var toolTip = new ToolTip();
                            toolTip.SetToolTip(label, req.Command);

                            var viewBtn = new Button
                            {
                                Text      = "🔍 View",
                                Font      = new Font("Segoe UI", 9f, FontStyle.Regular),
                                ForeColor = Color.FromArgb(180, 185, 210),
                                BackColor = Color.FromArgb(36, 39, 56),
                                FlatStyle = FlatStyle.Flat,
                                Cursor    = Cursors.Hand,
                                Size      = new Size(68, 26),
                                Margin    = new Padding(0, 0, 12, 0)
                            };
                            viewBtn.FlatAppearance.BorderSize  = 1;
                            viewBtn.FlatAppearance.BorderColor = Color.FromArgb(60, 64, 85);
                            viewBtn.Click += (_, __) => MessageBox.Show(req.Command, $"Command ({req.CommandVerb})", MessageBoxButtons.OK, MessageBoxIcon.Information);

                            var allowBtn = new Button
                            {
                                Text      = "✔",
                                Font      = new Font("Segoe UI", 11f, FontStyle.Bold),
                                ForeColor = Color.White,
                                BackColor = Color.FromArgb(46, 160, 67),
                                FlatStyle = FlatStyle.Flat,
                                Cursor    = Cursors.Hand,
                                Size      = new Size(32, 26),
                                Margin    = new Padding(0, 0, 8, 0)
                            };
                            allowBtn.FlatAppearance.BorderSize = 0;

                            var denyBtn = new Button
                            {
                                Text      = "✖",
                                Font      = new Font("Segoe UI", 11f, FontStyle.Bold),
                                ForeColor = Color.White,
                                BackColor = Color.FromArgb(218, 54, 51),
                                FlatStyle = FlatStyle.Flat,
                                Cursor    = Cursors.Hand,
                                Size      = new Size(32, 26),
                                Margin    = new Padding(0)
                            };
                            denyBtn.FlatAppearance.BorderSize = 0;

                            allowBtn.Click += (_, __) => { tcs.TrySetResult("approved"); _chatFlow.Controls.Remove(cardPanel); cardPanel.Dispose(); };
                            denyBtn.Click  += (_, __) => { tcs.TrySetResult("denied");   _chatFlow.Controls.Remove(cardPanel); cardPanel.Dispose(); };

                            flow.Controls.Add(label);
                            flow.Controls.Add(viewBtn);
                            flow.Controls.Add(allowBtn);
                            flow.Controls.Add(denyBtn);
                            cardPanel.Controls.Add(flow);
                            var targetEngine = _persistentEngine ?? _stepGroups.Keys.FirstOrDefault();
                            if (targetEngine != null && _stepGroups.TryGetValue(targetEngine, out var group) && group != null)
                            {
                                group.AddArtifactCard(cardPanel);
                            }
                            else
                            {
                                _chatFlow.Controls.Add(cardPanel);
                            }
                            _chatFlow.PerformLayout();
                            break;
                        }

                        default:
                            tcs.TrySetResult("[Unknown input kind]");
                            break;
                    }

                    this.BeginInvoke(new Action(() => ScrollToBottom()));
                }
                catch (Exception ex)
                {
                    tcs.TrySetResult($"[Error showing user input widget: {ex.Message}]");
                }
            });

            return tcs.Task;
        }



        private void ShowArtifactCard(Artifact artifact)
        {
            if (artifact.Type != ArtifactType.TaskPlan && artifact.Type != ArtifactType.ImplementationPlan) return;

            bool showExecute = _settingsService.Current.DevMode != DevelopmentMode.Autopilot;

            int targetWidth = Math.Max(200, _chatFlow.ClientSize.Width - 50);

            // ── compact bar — same style as approve_command ─────────────────
            var cardPanel = new Panel
            {
                Width     = targetWidth,
                Height    = 44,
                Margin    = new Padding(12, 6, 12, 6),
                BackColor = Color.FromArgb(24, 26, 38),
                Tag       = "ArtifactCardPanel"
            };

            var flow = new FlowLayoutPanel
            {
                Dock          = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                Padding       = new Padding(10, 8, 10, 8),
                BackColor     = Color.Transparent
            };

            var typeIcon = artifact.Type == ArtifactType.ImplementationPlan ? "📋" : "📝";
            var label = new Label
            {
                Text      = $"{typeIcon} {artifact.Title}",
                ForeColor = Color.FromArgb(220, 225, 245),
                Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                AutoSize  = true,
                Margin    = new Padding(0, 3, 10, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var toolTip = new ToolTip();
            toolTip.SetToolTip(label, artifact.Content?.Length > 200
                ? artifact.Content.Substring(0, 200) + "…"
                : artifact.Content ?? "");

            var viewBtn = new Button
            {
                Text      = "🔍 View",
                Font      = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(180, 185, 210),
                BackColor = Color.FromArgb(36, 39, 56),
                FlatStyle = FlatStyle.Flat,
                Cursor    = Cursors.Hand,
                Size      = new Size(68, 26),
                Margin    = new Padding(0, 0, 8, 0)
            };
            viewBtn.FlatAppearance.BorderSize  = 1;
            viewBtn.FlatAppearance.BorderColor = Color.FromArgb(60, 64, 85);
            viewBtn.Click += (_, __) => OpenArtifactTab(artifact);

            flow.Controls.Add(label);
            flow.Controls.Add(viewBtn);

            if (artifact.Type == ArtifactType.ImplementationPlan && showExecute)
            {
                var execBtn = new Button
                {
                    Text      = "▶ Execute",
                    Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                    ForeColor = Color.White,
                    BackColor = Color.FromArgb(46, 160, 67),
                    FlatStyle = FlatStyle.Flat,
                    Cursor    = Cursors.Hand,
                    Size      = new Size(78, 26),
                    Margin    = new Padding(0)
                };
                execBtn.FlatAppearance.BorderSize = 0;
                execBtn.Click += (_, __) =>
                {
                    execBtn.Enabled   = false;
                    execBtn.Text      = "Running…";
                    execBtn.BackColor = Color.FromArgb(36, 120, 55);

                    if (!string.IsNullOrEmpty(artifact.AgentId) &&
                        _pendingPlanApprovals.TryGetValue(artifact.AgentId, out var pendingArgs))
                    {
                        _pendingPlanApprovals.Remove(artifact.AgentId);
                        pendingArgs.Completion.TrySetResult(true);
                    }
                    else
                    {
                        ExecuteArtifact(artifact);
                    }
                };
                flow.Controls.Add(execBtn);
            }

            cardPanel.Controls.Add(flow);

            var targetEngine = _stepGroups.Keys.FirstOrDefault(e => e.Id == artifact.AgentId);
            if (targetEngine != null && _stepGroups.TryGetValue(targetEngine, out var group) && group != null)
                group.AddArtifactCard(cardPanel);
            else
                _chatFlow.Controls.Add(cardPanel);

            _chatFlow.PerformLayout();
            this.BeginInvoke(new Action(() => ScrollToBottom()));
        }

        private async void ExecuteArtifact(Artifact a)
        {
            if (a == null) return;
            string prompt = $"Please execute this plan: {a.Title}";
            if (a is TaskArtifact ta && ta.Tasks.Count > 0)
            {
                prompt += "\n\nTasks:\n" + string.Join("\n", ta.Tasks.Select(t => $"- {t.Title}"));
            }
            else if (!string.IsNullOrWhiteSpace(a.Content))
            {
                prompt += "\n\nContent:\n" + a.Content;
            }
            await RunPromptAsync(prompt);
        }

        private void OpenArtifactTab(Artifact artifact)
        {
            // check if artifact tab already exists
            foreach (Control p in _allPanels)
            {
                if (p.Tag as string == "artifacts")
                {
                    // find tab button
                    foreach (ToolStripItem item in tabStrip.Items)
                    {
                        if (item is ToolStripButton btn && btn.Tag == p)
                        {
                            SelectTab(btn);

                            foreach (Control c in p.Controls)
                            {
                                if (c is ElementHost h && h.Child is Gravity.UI.ArtifactPanel ap)
                                {
                                    ap.SelectArtifact(artifact);
                                    break;
                                }
                            }
                            return;
                        }
                    }
                }
            }

            var outerPanel = new Panel { Dock = DockStyle.Fill };
            var wpfHost = new ElementHost { Dock = DockStyle.Fill };
            var artifactPanel = new Gravity.UI.ArtifactPanel(_artifactService);
            artifactPanel.SelectArtifact(artifact);
            wpfHost.Child = artifactPanel;
            outerPanel.Controls.Add(wpfHost);
            outerPanel.Tag = "artifacts";
            mainDocumentArea.Controls.Add(outerPanel);

            tabStrip.Visible = true;
            _allPanels.Add(outerPanel);
            var newTab = CreateTabButton("Artifacts", outerPanel, "artifacts");

            this.BeginInvoke(new Action(() => {
                SelectTab(newTab);
            }));
        }

        // ── DOCX Preview Tab ───────────────────────────────────────────────────
        private void ShowDocxPreview(string filePath, string htmlContent)
        {
            // Reuse an existing tab for the SAME file
            foreach (Control p in _allPanels)
            {
                if (p.Tag as string == filePath)
                {
                    foreach (ToolStripItem item in tabStrip.Items)
                    {
                        if (item is ToolStripButton btn && btn.Tag == p)
                        {
                            SelectTab(btn);
                            if (p.Controls.Count > 0 && p.Controls[0] is System.Windows.Forms.Integration.ElementHost host && host.Child is Gravity.UI.DocxPreviewPanel panel)
                            {
                                panel.ShowPreview(filePath, htmlContent);
                            }
                            return;
                        }
                    }
                }
            }

            // New tab for this file
            _docxPreviewPanel = new Gravity.UI.DocxPreviewPanel(_docxPreviewService, _themeService);
            _docxPreviewPanel.ShowPreview(filePath, htmlContent);

            var outerPanel = new Panel { Dock = DockStyle.Fill };
            var wpfHost = new System.Windows.Forms.Integration.ElementHost { Dock = DockStyle.Fill };
            wpfHost.Child = _docxPreviewPanel;
            outerPanel.Controls.Add(wpfHost);
            outerPanel.Tag = filePath;  // keyed by path so reuse works
            mainDocumentArea.Controls.Add(outerPanel);

            tabStrip.Visible = true;
            _allPanels.Add(outerPanel);
            var tabLabel = "📄 " + System.IO.Path.GetFileName(filePath);
            var newTab = CreateTabButton(tabLabel, outerPanel, filePath);

            this.BeginInvoke(new Action(() =>
            {
                SelectTab(newTab);
            }));
        }

        private Color GetStatusColor(AgentStatus status)
        {
            return status switch
            {
                AgentStatus.Running => Color.FromArgb(50, 200, 50), // Green
                AgentStatus.Finished => Color.FromArgb(88, 166, 255), // Blue
                AgentStatus.Error => Color.FromArgb(248, 81, 73), // Red
                AgentStatus.Paused => Color.FromArgb(200, 200, 50), // Yellow
                _ => Color.Gray
            };
        }

        private void IncrementUnread(string agentId, Label badge)
        {
            if (!_unreadCounts.ContainsKey(agentId)) _unreadCounts[agentId] = 0;
            _unreadCounts[agentId]++;
            badge.Text = _unreadCounts[agentId] > 9 ? "9+" : _unreadCounts[agentId].ToString();
            badge.Visible = true;
        }

        /// <summary>
        /// Creates a ChatRow of the given type, sizes it to the chat flow width,
        /// adds it to _chatFlow, scrolls to bottom, and returns the new row.
        /// </summary>
        private Gravity.UI.ChatRow AddChatRow(Gravity.UI.ChatRowType type, string sender, string content = "")
        {
            var row = new Gravity.UI.ChatRow(type, sender, content);
            int flowW = _chatFlow.ClientSize.Width;
            if (flowW < 50) flowW = Math.Max(300, this.ClientSize.Width - 250);
            row.Width = Math.Max(300, flowW - 10);
            _chatFlow.Controls.Add(row);
            if (_thinkingBubblePanel != null)
                _chatFlow.Controls.SetChildIndex(_thinkingBubblePanel, _chatFlow.Controls.Count - 1);
            ScrollToBottom();
            return row;
        }

        private Gravity.UI.ChatRow EnsureAgentBubble(AppEngine engine)
        {
            if (!_activeBubbles.TryGetValue(engine.Id, out var row) || row == null)
            {
                row = AddChatRow(Gravity.UI.ChatRowType.Agent, "Agent", "");
                _activeBubbles[engine.Id] = row;
            }
            return row;
        }

        private void SelectAgent(AppEngine engine)
        {
            bool wasAlreadySelected = _selectedAgentId == engine.Id;
            bool switchingBetweenAgents = !string.IsNullOrEmpty(_selectedAgentId) && _selectedAgentId != engine.Id;

            _selectedAgentId = engine.Id;
            _unreadCounts[engine.Id] = 0;

            HideThinkingBubble();

            foreach (Control c in _agentListFlow.Controls)
            {
                if (c is Panel p)
                {
                    p.BackColor = (p.Tag as string == engine.Id) ? Color.FromArgb(80, 80, 120) : Color.FromArgb(45, 45, 60);
                    if (p.Tag as string == engine.Id)
                    {
                        foreach (Control child in p.Controls) if (child is Label l && l.BackColor == _themeService.Colors.Accent) l.Visible = false;
                    }
                }
            }

            // Only clear and rebuild the chat when explicitly switching between agents.
            // On first agent auto-select, preserve startup/workspace messages already in the chat.
            if (!switchingBetweenAgents) return;

            _chatFlow.Controls.Clear();

            string cleanIntent = engine.UserIntent;
            AppendChat("You", cleanIntent);

            foreach (var msg in engine.History)
            {
                if (msg.Role == "user" && !msg.Content.StartsWith("USER_INTENT:"))
                {
                    AppendChat("You", msg.Content);
                }
                else if (msg.Role == "system" || (msg.Role == "tool" && msg.Content.StartsWith("SYSTEM_ERROR:")))
                {
                    AppendChat("System", msg.Content);
                }
            }

            var agentBubble = AddChatRow(Gravity.UI.ChatRowType.Agent, "Agent", engine.FinalOutput ?? "");
            _activeBubbles[engine.Id] = agentBubble;

            ScrollToBottom();

            if (engine.Status == AgentStatus.Running)
            {
                ShowThinkingBubble();
            }
        }

        // Tab system for documents
        private void SelectTab(ToolStripButton btn)
        {
            if (btn == null) return;

            foreach (ToolStripItem item in tabStrip.Items)
            {
                if (item is ToolStripButton b)
                {
                    b.Checked = (b == btn);
                    b.BackColor = (b == btn) ? Color.FromArgb(10, 15, 45) : Color.FromArgb(5, 7, 28);
                    b.ForeColor = (b == btn) ? Color.FromArgb(245, 200, 50) : Color.FromArgb(140, 160, 220);
                }
            }

            var contentPan = btn.Tag as Control;
            if (contentPan != null)
            {
                SwitchToPanel(contentPan);
                // Track active file path for agent context
                if (contentPan.Tag is string filePath && !string.IsNullOrEmpty(filePath))
                    _projectContext.ActiveFilePath = filePath;
            }
        }

        private ToolStripButton CreateTabButton(string text, Control contentPan, string? path = null)
        {
            contentPan.Tag = path;
            contentPan.Dock = DockStyle.Fill;
            // mainDocumentArea.Controls.Add(contentPan); // Handled by caller

            var btn = new ToolStripButton(text) { Tag = contentPan, CheckOnClick = false, AutoToolTip = false, Margin = new Padding(2, 2, 0, 0), Padding = new Padding(10, 5, 10, 5), ToolTipText = path ?? text };
            if (text != "Chat")
            {
                btn.Text += "  �o.";
            }

            btn.Click += (s, e) => {
                // Check if the click was on the right side of the button (where the 'X' is)
                // ToolStripButton doesn't provide precise click coordinates in the event args,
                // but we can use Control.MousePosition relative to the button's screen bounds.
                if (text != "Chat")
                {
                    var screenPos = Control.MousePosition;
                    var bounds = btn.Bounds;
                    // Note: ToolStripButton.Bounds is relative to the ToolStrip
                    var stripPoint = tabStrip.PointToClient(screenPos);

                    // Simple heuristic: if click is in the last 20% of the button width
                    if (stripPoint.X > (btn.Bounds.Left + btn.Bounds.Width * 0.8f))
                    {
                        CloseTab(btn);
                        return;
                    }
                }
                SelectTab(btn);
            };
            btn.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Middle && text != "Chat")
                {
                    CloseTab(btn);
                }
            };

            tabStrip.Items.Add(btn);
            return btn;
        }

        private void CloseTab(ToolStripButton btn)
        {
            var contentPan = btn.Tag as Control;
            tabStrip.Items.Remove(btn);
            if (contentPan != null)
            {
                _allPanels.Remove((Panel)contentPan);
                mainDocumentArea.Controls.Remove(contentPan);
                contentPan.Dispose();
            }
            btn.Dispose();
            if (tabStrip.Items.Count > 0)
            {
                SelectTab((ToolStripButton)tabStrip.Items[tabStrip.Items.Count - 1]);
            }
        }

        // EnsureStepGroup removed — steps are now added directly as ChatRow items via AddChatRow.

        private void ApplyTheme()
        {
            var c = _themeService.Colors;
            bool isDark = _themeService.CurrentMode == ThemeMode.Dark;

            if (MaterialSkin.MaterialSkinManager.Instance != null)
            {
                MaterialSkin.MaterialSkinManager.Instance.Theme = isDark
                    ? MaterialSkin.MaterialSkinManager.Themes.DARK
                    : MaterialSkin.MaterialSkinManager.Themes.LIGHT;
            }

            // �"?�"? Palettes �"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?
            Color bgDeep, bgPanel, bgHeader, bgInput, bgStatus, fgMain, fgSoft, accent, accentGrn;

            if (isDark)
            {
                bgDeep = Color.FromArgb(5, 7, 28);
                bgPanel = Color.FromArgb(10, 15, 45);
                bgHeader = Color.FromArgb(8, 12, 40);
                bgInput = Color.FromArgb(12, 22, 58);
                bgStatus = Color.FromArgb(6, 10, 32);
                fgMain = Color.FromArgb(210, 220, 255);
                fgSoft = Color.FromArgb(140, 160, 220);
                accent = Color.FromArgb(245, 200, 50);
                accentGrn = Color.FromArgb(80, 210, 130);
            }
            else
            {
                bgDeep = Color.FromArgb(245, 247, 252);
                bgPanel = Color.FromArgb(235, 238, 248);
                bgHeader = Color.FromArgb(255, 255, 255);
                bgInput = Color.FromArgb(225, 230, 245);
                bgStatus = Color.FromArgb(220, 225, 240);
                fgMain = Color.FromArgb(25, 30, 60);
                fgSoft = Color.FromArgb(90, 105, 155);
                accent = Color.FromArgb(60, 100, 220);
                accentGrn = Color.FromArgb(30, 160, 80);
            }

            this.BackColor = bgDeep;
            this.ForeColor = fgMain;

            // �"?�"? Ribbon bar �"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?
            if (ribbonBar != null)
            {
                ribbonBar.BackColor = bgHeader;
                btnRibbonFolder.BackColor = bgHeader;
                btnRibbonFolder.ForeColor = fgMain;
                btnRibbonFolder.FlatAppearance.MouseOverBackColor = isDark ? Color.FromArgb(20, 30, 70) : Color.FromArgb(210, 218, 240);
                btnRibbonRun.BackColor = bgHeader;
                btnRibbonRun.ForeColor = accentGrn;
                btnRibbonRun.FlatAppearance.MouseOverBackColor = isDark ? Color.FromArgb(20, 30, 70) : Color.FromArgb(210, 218, 240);
                btnRibbonSettings.BackColor = bgHeader;
                btnRibbonSettings.ForeColor = fgMain;
                btnRibbonSettings.FlatAppearance.MouseOverBackColor = isDark ? Color.FromArgb(20, 30, 70) : Color.FromArgb(210, 218, 240);
                btnRibbonHelp.BackColor = bgHeader;
                btnRibbonHelp.ForeColor = accent;
                btnRibbonHelp.FlatAppearance.MouseOverBackColor = isDark ? Color.FromArgb(20, 30, 70) : Color.FromArgb(210, 218, 240);
            }

            // �"?�"? Activity bar �"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?
            activityBar.BackColor = bgHeader;
            foreach (Control ctrl in activityBar.Controls)
            {
                if (ctrl is Button btn)
                {
                    btn.BackColor = System.Drawing.Color.Transparent;
                    btn.ForeColor = fgMain;
                    btn.FlatAppearance.MouseOverBackColor = isDark ? Color.FromArgb(20, 35, 80) : Color.FromArgb(210, 218, 240);
                    btn.FlatAppearance.MouseDownBackColor = accent;
                }
            }

            // �"?�"? Explorer panel �"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?
            explorerPanel.BackColor = bgPanel;
            explorerLabel.BackColor = bgHeader;
            explorerLabel.ForeColor = fgMain;
            fileTreeView.BackColor = bgPanel;
            fileTreeView.ForeColor = fgMain;
            SetupSolutionExplorerContextMenu();

            // �"?�"? Chat area �"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?
            _chatPanel.BackColor = bgDeep;
            _chatFlow.BackColor = bgDeep;
            if (_chatScrollContainer != null) _chatScrollContainer.BackColor = bgDeep;

            // �"?�"? Input panel �"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?
            ApplyPremiumInputPanel(bgInput, fgMain, accent, isDark);

            // �"?�"? Breadcrumb bar �"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?
            breadcrumbBar.BackColor = bgHeader;
            breadcrumbLabel.BackColor = bgHeader;
            breadcrumbLabel.ForeColor = fgSoft;

            // �"?�"? Status bar �"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?�"?
            statusBar.BackColor = bgStatus;
            statusLabelLeft.BackColor = bgStatus;
            statusLabelLeft.ForeColor = fgSoft;
            statusLabelRight.BackColor = bgStatus;
            statusLabelRight.ForeColor = fgSoft;
            _spinner.SetColors(bgStatus, accent);

            // Update all open panels and editors
            foreach (var contentPan in _allPanels)
            {
                contentPan.BackColor = bgDeep;
                foreach (Control ctrl in contentPan.Controls)
                {
                    contentPan.BackColor = bgDeep;

                    if (ctrl is Panel outerPanel)
                    {
                        outerPanel.BackColor = bgDeep;
                        foreach (Control inner in outerPanel.Controls)
                        {
                            if (inner is ElementHost host && host.Child is TextEditor editor)
                            {
                                var colorizer = editor.TextArea.TextView.LineTransformers.OfType<SemanticColorizer>().FirstOrDefault();
                                if (colorizer != null) { colorizer.IsDarkTheme = isDark; editor.TextArea.TextView.Redraw(); }
                                ApplyEditorTheme(editor, c);
                            }
                            if (inner is Gravity.UI.MinimapPanel mp)
                            {
                                mp.SetColors(c.Background, isDark);
                            }
                        }
                    }
                }   
                    
            }
            btnNavTheme.Text = isDark ? "🌙 Dark" : "☀️ Light";

            tabStrip.Invalidate();
        }

        /// <summary>Applies a glassy rounded-pill style to the input area to match the current theme.</summary>
        private void ApplyPremiumInputPanel(Color bgInput, Color fgMain, Color accent, bool isDark)
        {
            inputPanel.BackColor = isDark ? Color.FromArgb(8, 12, 40) : Color.FromArgb(240, 243, 252);
            inputPanel.Paint -= InputPanel_PaintRounded;
            inputPanel.Paint += InputPanel_PaintRounded;
            inputPanel.Tag = isDark; // pass mode to paint handler
            inputPanel.Invalidate();

            inputBox.BackColor = bgInput;
            inputBox.ForeColor = fgMain;
            inputBox.BorderStyle = BorderStyle.None;
            inputBox.Margin = new Padding(0);

            // Send button
            sendButton.BackColor = isDark ? Color.FromArgb(200, 155, 20) : accent;
            sendButton.ForeColor = isDark ? Color.FromArgb(10, 10, 20) : Color.White;
            sendButton.FlatStyle = FlatStyle.Flat;
            sendButton.FlatAppearance.BorderSize = 0;
            sendButton.FlatAppearance.MouseOverBackColor = isDark ? Color.FromArgb(230, 185, 40) : Color.FromArgb(40, 80, 200);
            sendButton.FlatAppearance.MouseDownBackColor = isDark ? Color.FromArgb(170, 125, 10) : Color.FromArgb(20, 60, 180);
            sendButton.Font = new Font("Segoe UI", 13f, FontStyle.Bold);

            // Stop button
            stopButton.BackColor = isDark ? Color.FromArgb(25, 35, 75) : Color.FromArgb(220, 225, 240);
            stopButton.ForeColor = isDark ? Color.FromArgb(180, 200, 255) : Color.FromArgb(60, 80, 160);
            stopButton.FlatStyle = FlatStyle.Flat;
            stopButton.FlatAppearance.BorderSize = 0;
            stopButton.FlatAppearance.MouseOverBackColor = isDark ? Color.FromArgb(38, 55, 110) : Color.FromArgb(200, 210, 235);

            // Attach-image button
            attachImageButton.BackColor = isDark ? Color.FromArgb(18, 28, 70) : Color.FromArgb(225, 230, 245);
            attachImageButton.ForeColor = isDark ? Color.FromArgb(200, 155, 20) : Color.FromArgb(50, 80, 200);
            attachImageButton.FlatStyle = FlatStyle.Flat;
            attachImageButton.FlatAppearance.BorderSize = 0;
            attachImageButton.FlatAppearance.MouseOverBackColor = isDark ? Color.FromArgb(30, 45, 100) : Color.FromArgb(200, 210, 235);
            attachImageButton.FlatAppearance.MouseDownBackColor = isDark ? Color.FromArgb(12, 20, 55) : Color.FromArgb(180, 195, 225);
            attachImageButton.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
        }

        private void InputPanel_PaintRounded(object? sender, PaintEventArgs e)
        {
            if (sender is not Panel panel) return;
            bool isDark = panel.Tag is bool b ? b : true;

            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var bounds = new System.Drawing.Rectangle(8, 6, panel.Width - 16, panel.Height - 12);
            float radius = 22f;

            // Colors based on theme
            Color centerGlow = isDark ? Color.FromArgb(0, 200, 160, 0) : Color.FromArgb(0, 60, 100, 220);
            Color surroundGlow = isDark ? Color.FromArgb(30, 200, 160, 0) : Color.FromArgb(20, 60, 100, 220);
            Color pillColor = isDark ? Color.FromArgb(12, 22, 58) : Color.FromArgb(225, 230, 245);
            Color highlightColor = isDark ? Color.FromArgb(40, 200, 160, 0) : Color.FromArgb(40, 255, 255, 255);
            Color borderColor = isDark ? Color.FromArgb(70, 190, 145, 0) : Color.FromArgb(80, 160, 180, 230);

            // Outer glow
            var glowRect = bounds;
            glowRect.Inflate(3, 3);
            using var glowPath = RoundedRectPath(glowRect, radius + 3);
            using var glowBr = new System.Drawing.Drawing2D.PathGradientBrush(glowPath);
            glowBr.CenterColor = centerGlow;
            glowBr.SurroundColors = new[] { surroundGlow };
            g.FillPath(glowBr, glowPath);

            // Pill body
            using var pillPath = RoundedRectPath(bounds, radius);
            using var pillBr = new System.Drawing.SolidBrush(pillColor);
            g.FillPath(pillBr, pillPath);

            // Top highlight
            using var hlPen = new System.Drawing.Pen(highlightColor, 1f);
            g.DrawLine(hlPen, bounds.Left + (int)radius, bounds.Top + 1, bounds.Right - (int)radius, bounds.Top + 1);

            // Border
            using var borderPen = new System.Drawing.Pen(borderColor, 1.5f);
            g.DrawPath(borderPen, pillPath);
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRectPath(System.Drawing.Rectangle r, float rad)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            p.AddArc(r.Left, r.Top, rad * 2, rad * 2, 180, 90);
            p.AddArc(r.Right - rad * 2, r.Top, rad * 2, rad * 2, 270, 90);
            p.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
            p.AddArc(r.Left, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Legacy bubble/step re-theming stub �?" bubble colours are intentionally set by
        // the deep-blue palette in ApplyTheme and do not need to be re-applied here.
        private void ApplyTheme_Legacy() { }

        private void ApplyEditorTheme(TextEditor editor, ThemeColors c)
        {
            editor.Background = new SolidColorBrush(WpfColor.FromRgb(c.Background.R, c.Background.G, c.Background.B));
            editor.Foreground = new SolidColorBrush(WpfColor.FromRgb(c.Foreground.R, c.Foreground.G, c.Foreground.B));
            bool isDark = _themeService.CurrentMode == ThemeMode.Dark;
            byte lnR = isDark ? (byte)106 : (byte)150;
            editor.LineNumbersForeground = new SolidColorBrush(WpfColor.FromRgb(lnR, lnR, lnR));
        }

        private void UpdateBreadcrumb(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                breadcrumbLabel.Text = "";
                return;
            }
            var projectDir = _projectContext.ProjectDirectory ?? "";
            string rel = filePath.Replace(projectDir, "").TrimStart('\\', '/');
            var parts = rel.Split(new[] { '\\', '/' }, System.StringSplitOptions.RemoveEmptyEntries);
            breadcrumbLabel.Text = "  " + string.Join("  �?�  ", parts);
        }

        private void UpdateStatusBar(TextEditor editor)
        {
            // Use BeginInvoke (non-blocking) to avoid deadlock when called
            // from WinForms UI thread before the WPF content has fully rendered.
            editor.Dispatcher.BeginInvoke(new Action(() =>
            {
                int line = editor.TextArea.Caret.Line;
                int col = editor.TextArea.Caret.Column;
                if (statusLabelLeft.InvokeRequired)
                    statusLabelLeft.BeginInvoke(new Action(() =>
                    {
                        statusLabelLeft.Text = $"  Ln {line}, Col {col}";
                        statusLabelRight.Text = $"Spaces: 4  UTF-8  C#  ";
                    }));
                else
                {
                    statusLabelLeft.Text = $"  Ln {line}, Col {col}";
                    statusLabelRight.Text = $"Spaces: 4  UTF-8  C#  ";
                }
            }));
        }

        // Removed mainTabControl_SelectedIndexChanged

        private void btnNavExplorer_Click(object? sender, EventArgs e)
        {
            ToggleSidebarPanel(explorerPanel);
        }

        private void btnNavSessions_Click(object? sender, EventArgs e)
        {
            ToggleSidebarPanel(sessionPanel);
            if (sessionPanel.Visible) RefreshSessionList();
        }

        private void ToggleSidebarPanel(Control? targetPanel)
        {
            if (targetPanel == null)
            {
                mainSplitContainer.Panel1Collapsed = true;
                explorerPanel.Visible = false;
                sessionPanel.Visible = false;
                _targetSidebarPanel = null;
            }
            else if (!mainSplitContainer.Panel1Collapsed && targetPanel.Visible)
            {
                mainSplitContainer.Panel1Collapsed = true;
                explorerPanel.Visible = false;
                sessionPanel.Visible = false;
                _targetSidebarPanel = null;
            }
            else
            {
                _targetSidebarPanel = targetPanel;

                explorerPanel.Visible = false;
                sessionPanel.Visible = false;

                targetPanel.Visible = true;
                targetPanel.BringToFront();

                mainSplitContainer.Panel1MinSize = 0;
                mainSplitContainer.SplitterDistance = _sidebarMaxWidth;
                mainSplitContainer.Panel1Collapsed = false;
            }

            UpdateSidebarButtonHighlights();
        }

        private static Bitmap LoadIconSafely(string relativePath)
        {
            try
            {
                if (System.IO.File.Exists(relativePath))
                    return new Bitmap(relativePath);

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string path1 = System.IO.Path.Combine(baseDir, relativePath);
                if (System.IO.File.Exists(path1))
                    return new Bitmap(path1);

                string path2 = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", relativePath));
                if (System.IO.File.Exists(path2))
                    return new Bitmap(path2);
            }
            catch { }

            return new Bitmap(16, 16);
        }

        private Bitmap TintImage(Bitmap? source, System.Drawing.Color tint)
        {
            if (source == null) return new Bitmap(16, 16);
            Bitmap bmp = new Bitmap(source.Width, source.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                System.Drawing.Imaging.ColorMatrix colorMatrix = new System.Drawing.Imaging.ColorMatrix(new float[][]
                {
                    new float[] {tint.R/255f, 0, 0, 0, 0},
                    new float[] {0, tint.G/255f, 0, 0, 0},
                    new float[] {0, 0, tint.B/255f, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {0, 0, 0, 0, 1}
                });
                System.Drawing.Imaging.ImageAttributes attributes = new System.Drawing.Imaging.ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);
                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return bmp;
        }

        private void UpdateSidebarButtonHighlights()
        {
            var accent = System.Drawing.Color.Gold;
            var defaultColor = _themeService.CurrentMode == ThemeMode.Dark ? System.Drawing.Color.FromArgb(200, 200, 200) : System.Drawing.Color.FromArgb(120, 120, 120);
            var bgHeader = _themeService.CurrentMode == ThemeMode.Dark ? System.Drawing.Color.FromArgb(8, 12, 40) : System.Drawing.Color.FromArgb(255, 255, 255);

            btnNavExplorer.BackColor = System.Drawing.Color.Transparent;
            btnNavSessions.BackColor = System.Drawing.Color.Transparent;
            btnNavSettings.BackColor = System.Drawing.Color.Transparent;
            btnNavTheme.BackColor = System.Drawing.Color.Transparent;
            btnNavClose.BackColor = System.Drawing.Color.Transparent;

            bool isSettingsSelected = _settingsTabBtn != null && _settingsTabBtn.Checked;

            btnNavExplorer.Image = TintImage(_iconExplorer, (_targetSidebarPanel == explorerPanel && !mainSplitContainer.Panel1Collapsed) ? accent : defaultColor);
            btnNavSessions.Image = TintImage(_iconSessions, (_targetSidebarPanel == sessionPanel && !mainSplitContainer.Panel1Collapsed) ? accent : defaultColor);
            btnNavSettings.Image = TintImage(_iconSettings, isSettingsSelected ? accent : defaultColor);

            btnNavTheme.Image = TintImage(_iconTheme, defaultColor);
            btnNavClose.Image = TintImage(_iconClose, defaultColor);
        }

        private void SidebarAnimationTimer_Tick(object? sender, EventArgs e)
        {
            int diff = _targetSplitterDistance - mainSplitContainer.SplitterDistance;
            int step = (int)(diff * 0.3);
            if (step == 0) step = Math.Sign(diff);

            int current = mainSplitContainer.SplitterDistance + step;
            if (Math.Abs(current - _targetSplitterDistance) <= 1) current = _targetSplitterDistance;

            mainSplitContainer.SplitterDistance = current;

            if (current == _targetSplitterDistance)
            {
                _sidebarAnimationTimer.Stop();
                if (_targetSplitterDistance == 0)
                {
                    mainSplitContainer.Panel1Collapsed = true;
                    explorerPanel.Visible = false;
                    sessionPanel.Visible = false;
                    UpdateSidebarButtonHighlights();
                }
            }
        }

        private void btnNavClose_Click(object? sender, EventArgs e)
        {
            this.Close();
        }

        private void btnNewSession_Click(object? sender, EventArgs e)
        {
            _sessionService.CreateNewSession();
            if (_chatTabBtn != null)
            {
                _chatTabBtn.Text = _sessionService.CurrentSession.Name;
            }
            if (_persistentEngine != null)
            {
                lock (_persistentEngine.History) { _persistentEngine.History.Clear(); }
            }
            _chatFlow.Controls.Clear();
            _activeStepPanels.Clear();
            _stepGroups.Clear();
            _activeBubbles.Clear();
            AppendChat("System", "Started a new session.");
            RefreshSessionList();
        }

        private void RefreshSessionList()
        {
            var sessions = _sessionService.GetAllSessions();
            sessionList.Controls.Clear();
            foreach (var s in sessions)
            {
                var sessionRow = new Panel
                {
                    Width = sessionList.Width > 20 ? sessionList.Width - 20 : 200,
                    Height = 40,
                    Margin = new Padding(5),
                    BackColor = Color.Transparent
                };

                var btn = new Button
                {
                    Text = s.Name,
                    Width = sessionRow.Width - 40,
                    Height = 40,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Color.White,
                    BackColor = Color.FromArgb(45, 48, 65),
                    Font = new Font("Segoe UI", 10),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Tag = s,
                    Dock = DockStyle.Left
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += (sender, e) =>
                {
                    if (btn.Tag is Session session)
                    {
                        LoadSession(session);
                    }
                };

                var btnDelete = new Button
                {
                    Text = "×",
                    Width = 30,
                    Height = 40,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Color.LightGray,
                    BackColor = Color.FromArgb(45, 48, 65),
                    Font = new Font("Segoe UI", 12, FontStyle.Bold),
                    Dock = DockStyle.Right,
                    Tag = s
                };
                btnDelete.FlatAppearance.BorderSize = 0;
                btnDelete.Click += (sender, e) =>
                {
                    if (btnDelete.Tag is Session sessionToDelete)
                    {
                        var result = MessageBox.Show($"Delete session '{sessionToDelete.Name}'?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (result == DialogResult.Yes)
                        {
                            _sessionService.DeleteSession(sessionToDelete.Id);
                            if (_sessionService.CurrentSession?.Id == sessionToDelete.Id)
                            {
                                _sessionService.CreateNewSession();
                            }
                            RefreshSessionList();
                            if (_chatTabBtn != null) _chatTabBtn.Text = _sessionService.CurrentSession.Name;
                        }
                    }
                };

                sessionRow.Controls.Add(btn);
                sessionRow.Controls.Add(btnDelete);
                sessionList.Controls.Add(sessionRow);
            }
        }

        private void LoadSession(Session s)
        {
            _sessionService.SetCurrentSession(s.Id);
            if (_chatTabBtn != null)
            {
                _chatTabBtn.Text = s.Name;
            }
            if (_persistentEngine != null)
            {
                lock (_persistentEngine.History)
                {
                    _persistentEngine.History.Clear();
                    _persistentEngine.History.AddRange(s.History);
                }
            }
            _chatFlow.Controls.Clear();
            _activeStepPanels.Clear();
            _stepGroups.Clear();
            _activeBubbles.Clear();

            foreach (var msg in s.History)
            {
                AppendChat(msg.Role, msg.Content);
            }
            AppendChat("System", $"Loaded session: {s.Name}");
        }

        private async void btnSaveFile_Click(object? sender, EventArgs e)
        {
            if (btnSaveFile.Tag is string path && !string.IsNullOrEmpty(path))
            {
                var panel = _allPanels.FirstOrDefault(p => (p.Tag as string) == path);
                if (panel != null)
                {
                    var elementHost = panel.Controls.OfType<ElementHost>().FirstOrDefault();
                    if (elementHost != null && elementHost.Child is TextEditor editor)
                    {
                        await System.IO.File.WriteAllTextAsync(path, editor.Text);
                        AppendChat("System", $"File saved: {path}");
                    }
                }
            }
        }


        private class PremiumColorTable : ProfessionalColorTable
        {
            private readonly ThemeColors _c;
            public PremiumColorTable(ThemeColors c) => _c = c;
            public override Color ToolStripGradientBegin => _c.HeaderBackground;
            public override Color ToolStripGradientMiddle => _c.HeaderBackground;
            public override Color ToolStripGradientEnd => _c.HeaderBackground;
            public override Color ToolStripBorder => _c.Border;
            public override Color MenuItemSelected => _c.PanelBackground;
            public override Color MenuItemBorder => _c.Accent;
            public override Color MenuItemSelectedGradientBegin => _c.PanelBackground;
            public override Color MenuItemSelectedGradientEnd => _c.PanelBackground;
        }

        private void AppendShellLog(string message, bool isError)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, bool>(AppendShellLog), message, isError);
                return;
            }
            if (_activeBubbles.TryGetValue(_selectedAgentId ?? "", out var bubble) && bubble != null && !string.IsNullOrWhiteSpace(message))
            {
                bubble.AppendContent(isError ? $"[Error] {message}" : message);
            }
        }

        private async Task RefreshSolutionExplorerAsync()
        {
            if (string.IsNullOrEmpty(_projectContext.ProjectPath)) return;

            var rootDir = _projectContext.ProjectDirectory;
            if (string.IsNullOrEmpty(rootDir)) return;

            var expandedTags = GetExpandedNodeTags(fileTreeView.Nodes);
            var selectedTags = fileTreeView.SelectedNodes.Select(n => n.Tag as string).Where(t => t != null).ToList()!;

            fileTreeView.BeginUpdate();
            try
            {
                fileTreeView.Nodes.Clear();

                // Refresh knowledge index whenever the active folder changes
                _ = _knowledgeService?.RefreshKnowledgeAsync();

                if (_fileWatcher == null || _fileWatcher.Path != rootDir)
                {
                    if (_fileWatcher != null)
                    {
                        _fileWatcher.EnableRaisingEvents = false;
                        _fileWatcher.Dispose();
                    }

                    if (System.IO.Directory.Exists(rootDir))
                    {
                        _fileWatcher = new FileSystemWatcher(rootDir);
                        _fileWatcher.IncludeSubdirectories = true;
                        _fileWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;

                        FileSystemEventHandler handler = (s, e) => {
                            if (e != null)
                            {
                                var path = e.FullPath.Replace('\\', '/');
                                if (path.Contains("/.git/") || path.EndsWith("/.git") ||
                                    path.Contains("/.vs/") || path.EndsWith("/.vs") ||
                                    path.Contains("/bin/") || path.EndsWith("/bin") ||
                                    path.Contains("/obj/") || path.EndsWith("/obj"))
                                {
                                    return;
                                }

                                if (e.ChangeType == WatcherChangeTypes.Changed || e.ChangeType == WatcherChangeTypes.Created)
                                {
                                    this.BeginInvoke(new Action(async () => await ReloadOpenTabForFileAsync(e.FullPath)));
                                }
                            }
                            _lastFileSystemEvent = DateTime.UtcNow;
                            if (this.InvokeRequired)
                            {
                                this.BeginInvoke(new Action(() => { _debounceTimer?.Stop(); _debounceTimer?.Start(); }));
                            }
                            else
                            {
                                _debounceTimer?.Stop(); _debounceTimer?.Start();
                            }
                        };

                        _fileWatcher.Created += handler;
                        _fileWatcher.Changed += handler;
                        _fileWatcher.Deleted += handler;
                        _fileWatcher.Renamed += (s, e) => handler(s, e);
                        _fileWatcher.EnableRaisingEvents = true;
                    }
                }

                var files = await _fileSearchService.EnumerateProjectFilesAsync(rootDir, CancellationToken.None);

                // Get git status for files
                var isGitRepo = _gitService.IsGitRepo();
                var gitStatus = await _gitService.GetStatusAsync();

                var rootText = System.IO.Path.GetFileName(rootDir);
                var rootNode = new TreeNode($"📁 {rootText}") { Name = rootText, Tag = rootDir, ImageKey = "folder", SelectedImageKey = "folder" };
                fileTreeView.Nodes.Add(rootNode);

                foreach (var file in files)
                {
                    var relative = System.IO.Path.GetRelativePath(rootDir, file);
                    var parts = relative.Split(System.IO.Path.DirectorySeparatorChar);
                    var currentNode = rootNode;

                    for (int i = 0; i < parts.Length; i++)
                    {
                        var part = parts[i];
                        bool isFile = (i == parts.Length - 1);

                        var existing = currentNode.Nodes.ContainsKey(part) ? currentNode.Nodes[part] : null;
                        if (existing == null)
                        {
                            var tagStr = currentNode.Tag?.ToString() ?? "";
                            var fullPath = System.IO.Path.Combine(tagStr, part);

                            string displayText;
                            Color foreColor = fileTreeView.ForeColor;

                            if (isFile)
                            {
                                var normalized = relative.Replace(System.IO.Path.DirectorySeparatorChar, '/');
                                if (isGitRepo && gitStatus.TryGetValue(normalized, out var status))
                                {
                                    string icon = status switch
                                    {
                                        "M" => "📝",
                                        "A" => "➕",
                                        "?" => "❓",
                                        "D" => "❌",
                                        _ => "📄"
                                    };
                                    displayText = $"{icon} {part}";
                                    foreColor = status switch
                                    {
                                        "M" => Color.Orange,
                                        "A" => Color.LimeGreen,
                                        "?" => Color.LightCoral,
                                        "D" => Color.Red,
                                        _ => Color.Yellow
                                    };
                                }
                                else
                                {
                                    displayText = $"📄 {part}";
                                    if (!isGitRepo) foreColor = Color.Gray;
                                }
                            }
                            else
                            {
                                displayText = $"📂 {part}";
                            }

                            var newNode = new TreeNode(displayText) { Name = part, Tag = fullPath, ForeColor = foreColor };
                            currentNode.Nodes.Add(newNode);
                            currentNode = newNode;
                        }
                        else
                        {
                            currentNode = existing;
                        }
                    }
                }
                rootNode.Expand();
                RestoreExpandedNodeTags(fileTreeView.Nodes, expandedTags);
                RestoreSelectedNodeTags(fileTreeView, selectedTags);
            }
            finally
            {
                fileTreeView.EndUpdate();
            }
        }

        private static HashSet<string> GetExpandedNodeTags(TreeNodeCollection nodes)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Collect(TreeNodeCollection col)
            {
                foreach (TreeNode n in col)
                {
                    if (n.IsExpanded && n.Tag is string tag)
                    {
                        set.Add(tag);
                    }
                    if (n.Nodes.Count > 0) Collect(n.Nodes);
                }
            }
            Collect(nodes);
            return set;
        }

        private static void RestoreExpandedNodeTags(TreeNodeCollection nodes, HashSet<string> expandedTags)
        {
            if (expandedTags == null || expandedTags.Count == 0) return;
            void Restore(TreeNodeCollection col)
            {
                foreach (TreeNode n in col)
                {
                    if (n.Tag is string tag && expandedTags.Contains(tag))
                    {
                        n.Expand();
                    }
                    if (n.Nodes.Count > 0) Restore(n.Nodes);
                }
            }
            Restore(nodes);
        }

        private static void RestoreSelectedNodeTags(Gravity.UI.MultiSelectTreeView tree, List<string> selectedTags)
        {
            if (selectedTags == null || selectedTags.Count == 0) return;
            var toSelect = new List<TreeNode>();
            void Find(TreeNodeCollection col)
            {
                foreach (TreeNode n in col)
                {
                    if (n.Tag is string tag && selectedTags.Contains(tag))
                    {
                        toSelect.Add(n);
                    }
                    if (n.Nodes.Count > 0) Find(n.Nodes);
                }
            }
            Find(tree.Nodes);
            if (toSelect.Count > 0)
            {
                tree.ClearSelectedNodes();
                foreach (var node in toSelect)
                {
                    tree.SelectNode(node, clearPrevious: false);
                }
            }
        }

        private void UpdateTreeAnimations()
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(UpdateTreeAnimations)); return; }
            if (fileTreeView.Nodes.Count == 0) return;

            bool isDark = _themeService.CurrentMode == ThemeMode.Dark;
            Color defaultForeColor = fileTreeView.ForeColor;
            Color readingColor = Color.Cyan;
            Color writingColor = Color.Orange;

            // Simple pulse effect: toggle between bright and faded color
            bool brightPhase = (DateTime.UtcNow.Millisecond / 200) % 2 == 0;

            void Traverse(TreeNodeCollection nodes)
            {
                foreach (TreeNode node in nodes)
                {
                    var path = node.Tag as string;
                    if (!string.IsNullOrEmpty(path))
                    {
                        bool isReading = false;
                        bool isWriting = false;
                        var now = DateTime.UtcNow;

                        lock (_recentReads)
                        {
                            if (_recentReads.TryGetValue(path, out var t) && (now - t).TotalSeconds < 2.0)
                                isReading = true;
                        }
                        lock (_recentWrites)
                        {
                            if (_recentWrites.TryGetValue(path, out var t) && (now - t).TotalSeconds < 2.0)
                                isWriting = true;
                        }

                        if (isWriting)
                        {
                            node.ForeColor = brightPhase ? writingColor : Color.FromArgb(150, writingColor);
                            node.NodeFont = new Font(fileTreeView.Font, FontStyle.Bold);
                        }
                        else if (isReading)
                        {
                            node.ForeColor = brightPhase ? readingColor : Color.FromArgb(150, readingColor);
                            node.NodeFont = new Font(fileTreeView.Font, FontStyle.Bold);
                        }
                        else
                        {
                            // Try to restore original git status color if any
                            if (node.NodeFont != null) node.NodeFont = null;

                            // Let's rely on the text to infer status if possible, 
                            // though ideally we'd store the original color in a custom property.
                            // For now, if we modified it, we might lose the git color until next refresh.
                            // Since this happens during reasoning, it's an acceptable tradeoff, 
                            // but we can default it back.
                            if (node.ForeColor == readingColor || node.ForeColor == writingColor ||
                                node.ForeColor == Color.FromArgb(150, writingColor) ||
                                node.ForeColor == Color.FromArgb(150, readingColor))
                            {
                                node.ForeColor = defaultForeColor;
                            }
                        }
                    }
                    if (node.Nodes.Count > 0) Traverse(node.Nodes);
                }
            }

            Traverse(fileTreeView.Nodes);
        }

        private ContextMenuStrip? _explorerContextMenu;

        private void SetupSolutionExplorerContextMenu()
        {
            if (_explorerContextMenu != null) return;

            _explorerContextMenu = new ContextMenuStrip();
            var uploadItem = new ToolStripMenuItem("Upload File...");
            var openFolderItem = new ToolStripMenuItem("Open Original Folder 📁");
            var renameItem = new ToolStripMenuItem("Rename");
            var deleteItem = new ToolStripMenuItem("Delete Selected (Del)");

            uploadItem.Click += ExplorerContextMenu_Upload;
            openFolderItem.Click += ExplorerContextMenu_OpenFolder;
            renameItem.Click += ExplorerContextMenu_Rename;
            deleteItem.Click += ExplorerContextMenu_Delete;

            _explorerContextMenu.Items.Add(uploadItem);
            _explorerContextMenu.Items.Add(openFolderItem);
            _explorerContextMenu.Items.Add(new ToolStripSeparator());
            _explorerContextMenu.Items.Add(renameItem);
            _explorerContextMenu.Items.Add(deleteItem);

            fileTreeView.ContextMenuStrip = _explorerContextMenu;
            fileTreeView.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete)
                {
                    ExplorerContextMenu_Delete(s, e);
                }
            };
            fileTreeView.NodeMouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    if (!fileTreeView.SelectedNodes.Contains(e.Node))
                    {
                        fileTreeView.SelectNode(e.Node, clearPrevious: true);
                    }
                }
            };

            // ── ItemDrag: allow dragging selected node(s) out to chat input box ────
            fileTreeView.ItemDrag += (s, e) =>
            {
                var selectedNodes = fileTreeView.SelectedNodes.Count > 0
                    ? fileTreeView.SelectedNodes
                    : (e.Item is TreeNode node ? new List<TreeNode> { node } : new List<TreeNode>());

                if (selectedNodes.Count == 0) return;

                var paths = selectedNodes
                    .Select(n => n.Tag as string)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToArray()!;

                if (paths.Length > 0)
                {
                    var dataObj = new DataObject();
                    dataObj.SetData("GravityFileNode", paths);
                    dataObj.SetData(DataFormats.FileDrop, paths);
                    dataObj.SetData(DataFormats.Text, string.Join(" ", paths.Select(p => System.IO.Path.GetFileName(p))));
                    fileTreeView.DoDragDrop(dataObj, DragDropEffects.Copy);
                }
            };

            // ── Drag & Drop into chat input box (inputBox & inputPanel) ────────────
            var setupInputBoxDrop = (Control targetCtrl) =>
            {
                targetCtrl.AllowDrop = true;
                targetCtrl.DragEnter += (s, e) =>
                {
                    if (e.Data != null && (e.Data.GetDataPresent("GravityFileNode") || e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text)))
                    {
                        e.Effect = DragDropEffects.Copy;
                    }
                    else
                    {
                        e.Effect = DragDropEffects.None;
                    }
                };

                targetCtrl.DragDrop += (s, e) =>
                {
                    if (e.Data == null) return;

                    string[]? filePaths = null;
                    if (e.Data.GetDataPresent("GravityFileNode"))
                    {
                        filePaths = e.Data.GetData("GravityFileNode") as string[];
                    }
                    else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    {
                        filePaths = e.Data.GetData(DataFormats.FileDrop) as string[];
                    }

                    if (filePaths == null || filePaths.Length == 0)
                    {
                        if (e.Data.GetDataPresent(DataFormats.Text))
                        {
                            string textData = e.Data.GetData(DataFormats.Text) as string ?? "";
                            if (!string.IsNullOrEmpty(textData)) filePaths = new[] { textData };
                        }
                    }

                    if (filePaths == null || filePaths.Length == 0) return;

                    // Shift button held during drag or drop pastes full path; normal drag pastes file name
                    bool useFullPath = (Control.ModifierKeys & Keys.Shift) == Keys.Shift
                                    || (e.KeyState & 4) == 4;

                    var insertedTexts = filePaths.Select(p => useFullPath ? p : System.IO.Path.GetFileName(p));
                    string insertion = string.Join(" ", insertedTexts);

                    int caretPos = inputBox.SelectionStart;
                    if (caretPos >= 0 && caretPos <= inputBox.Text.Length)
                    {
                        inputBox.Text = inputBox.Text.Insert(caretPos, insertion);
                        inputBox.SelectionStart = caretPos + insertion.Length;
                    }
                    else
                    {
                        inputBox.Text += (inputBox.Text.Length > 0 && !inputBox.Text.EndsWith(" ") ? " " : "") + insertion;
                        inputBox.SelectionStart = inputBox.Text.Length;
                    }

                    inputBox.Focus();
                };
            };

            setupInputBoxDrop(inputBox);
            setupInputBoxDrop(inputPanel);

            // ── Drag & Drop files from Windows Explorer into Solution Explorer ─────
            fileTreeView.AllowDrop = true;

            fileTreeView.DragEnter += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                }
                else
                {
                    e.Effect = DragDropEffects.None;
                }
            };

            fileTreeView.DragOver += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                    var pt = fileTreeView.PointToClient(new System.Drawing.Point(e.X, e.Y));
                    var targetNode = fileTreeView.GetNodeAt(pt);
                    if (targetNode != null && fileTreeView.SelectedNode != targetNode)
                    {
                        fileTreeView.SelectedNode = targetNode;
                    }
                }
            };

            fileTreeView.DragDrop += async (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (files == null || files.Length == 0) return;

                    var pt = fileTreeView.PointToClient(new System.Drawing.Point(e.X, e.Y));
                    var targetNode = fileTreeView.GetNodeAt(pt);

                    await ProcessUploadedFilesAsync(files, targetNode);
                }
            };
        }

        private async void ExplorerContextMenu_Upload(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Select files to upload",
                Multiselect = true,
                Filter = "All Files (*.*)|*.*|DOCX Documents (*.docx)|*.docx|Code Files (*.cs;*.js;*.ts;*.html;*.css;*.json)|*.cs;*.js;*.ts;*.html;*.css;*.json"
            };

            if (ofd.ShowDialog(this) == DialogResult.OK && ofd.FileNames.Length > 0)
            {
                var targetNode = fileTreeView.SelectedNode;
                await ProcessUploadedFilesAsync(ofd.FileNames, targetNode);
            }
        }

        private async Task ProcessUploadedFilesAsync(string[] files, TreeNode? targetNode)
        {
            string targetDir = _projectContext.ProjectDirectory ?? "";
            if (targetNode != null && targetNode.Tag is string nodePath)
            {
                if (System.IO.Directory.Exists(nodePath))
                {
                    targetDir = nodePath;
                }
                else if (System.IO.File.Exists(nodePath))
                {
                    targetDir = System.IO.Path.GetDirectoryName(nodePath) ?? targetDir;
                }
            }

            if (string.IsNullOrEmpty(targetDir) || !System.IO.Directory.Exists(targetDir)) return;

            var copiedFiles = new List<string>();
            foreach (var srcFile in files)
            {
                try
                {
                    if (System.IO.File.Exists(srcFile))
                    {
                        string fileName = System.IO.Path.GetFileName(srcFile);
                        string destPath = System.IO.Path.Combine(targetDir, fileName);

                        if (!string.Equals(srcFile, destPath, StringComparison.OrdinalIgnoreCase))
                        {
                            System.IO.File.Copy(srcFile, destPath, overwrite: true);
                        }
                        copiedFiles.Add(destPath);
                    }
                    else if (System.IO.Directory.Exists(srcFile))
                    {
                        string dirName = System.IO.Path.GetFileName(srcFile);
                        string destDir = System.IO.Path.Combine(targetDir, dirName);
                        CopyDirectoryRecursive(srcFile, destDir);
                        copiedFiles.Add(destDir);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error uploading '{System.IO.Path.GetFileName(srcFile)}': {ex.Message}",
                        "Upload Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            // Refresh file tree
            await RefreshSolutionExplorerAsync();

            // Auto-open if a single file was uploaded
            if (copiedFiles.Count == 1 && System.IO.File.Exists(copiedFiles[0]))
            {
                await OpenFileInTabAsync(copiedFiles[0]);
            }
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
        {
            var dir = new System.IO.DirectoryInfo(sourceDir);
            if (!dir.Exists) return;

            System.IO.Directory.CreateDirectory(destinationDir);
            foreach (var file in dir.GetFiles())
            {
                file.CopyTo(System.IO.Path.Combine(destinationDir, file.Name), true);
            }
            foreach (var subDir in dir.GetDirectories())
            {
                CopyDirectoryRecursive(subDir.FullName, System.IO.Path.Combine(destinationDir, subDir.Name));
            }
        }

        private void ExplorerContextMenu_Rename(object? sender, EventArgs e)
        {
            var selectedNode = fileTreeView.SelectedNode;
            if (selectedNode == null) return;
            string? filePath = selectedNode.Tag as string;
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return;

            string fileName = System.IO.Path.GetFileName(filePath);
            using var dlg = new InputDialog("Enter new name:", fileName);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                string newName = dlg.GetText();
                if (!string.IsNullOrWhiteSpace(newName) && newName != fileName)
                {
                    string dir = System.IO.Path.GetDirectoryName(filePath) ?? "";
                    string newPath = System.IO.Path.Combine(dir, newName);
                    try
                    {
                        System.IO.File.Move(filePath, newPath);
                        selectedNode.Text = newName;
                        selectedNode.Tag = newPath;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error renaming file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ExplorerContextMenu_OpenFolder(object? sender, EventArgs e)
        {
            var selectedNodes = fileTreeView.SelectedNodes.Count > 0
                ? fileTreeView.SelectedNodes
                : (fileTreeView.SelectedNode != null ? new List<TreeNode> { fileTreeView.SelectedNode } : new List<TreeNode>());

            if (selectedNodes.Count == 0) return;

            foreach (var node in selectedNodes)
            {
                string? path = node.Tag as string;
                if (string.IsNullOrEmpty(path)) continue;

                try
                {
                    if (System.IO.File.Exists(path))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                    }
                    else if (System.IO.Directory.Exists(path))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open folder for '{path}': {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExplorerContextMenu_Delete(object? sender, EventArgs e)
        {
            var selectedNodes = fileTreeView.SelectedNodes.Count > 0
                ? fileTreeView.SelectedNodes.ToList()
                : (fileTreeView.SelectedNode != null ? new List<TreeNode> { fileTreeView.SelectedNode } : new List<TreeNode>());

            if (selectedNodes.Count == 0) return;

            var validTargets = new List<(TreeNode Node, string Path, bool IsDir)>();
            foreach (var node in selectedNodes)
            {
                string? path = node.Tag as string;
                if (!string.IsNullOrEmpty(path))
                {
                    if (System.IO.File.Exists(path))
                        validTargets.Add((node, path, false));
                    else if (System.IO.Directory.Exists(path))
                        validTargets.Add((node, path, true));
                }
            }

            if (validTargets.Count == 0) return;

            string promptMsg = validTargets.Count == 1
                ? $"Are you sure you want to delete '{System.IO.Path.GetFileName(validTargets[0].Path)}'?"
                : $"Are you sure you want to delete these {validTargets.Count} selected items?\n\n" +
                  string.Join("\n", validTargets.Take(5).Select(t => "• " + System.IO.Path.GetFileName(t.Path))) +
                  (validTargets.Count > 5 ? $"\n... and {validTargets.Count - 5} more." : "");

            if (MessageBox.Show(promptMsg, "Confirm Multi-Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                int successCount = 0;
                var errors = new List<string>();

                foreach (var item in validTargets)
                {
                    try
                    {
                        if (item.IsDir)
                        {
                            System.IO.Directory.Delete(item.Path, true);
                        }
                        else
                        {
                            System.IO.File.Delete(item.Path);
                        }
                        item.Node.Remove();
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Failed to delete '{System.IO.Path.GetFileName(item.Path)}': {ex.Message}");
                    }
                }

                fileTreeView.ClearSelectedNodes();

                if (errors.Count > 0)
                {
                    MessageBox.Show($"Deleted {successCount} item(s).\nErrors encountered:\n" + string.Join("\n", errors), "Multi-Delete Warnings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }


        private async void fileTreeView_NodeMouseDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node.Nodes.Count > 0) return; // it's a folder
            var path = e.Node.Tag as string;
            if (!string.IsNullOrEmpty(path)) await OpenFileInTabAsync(path);
        }

        private void toolStripSettings_Click(object? sender, EventArgs e)
        {
            if (_settingsTabBtn != null)
            {
                SelectTab(_settingsTabBtn);
            }
            else
            {
                var settingsHost = _allPanels.FirstOrDefault(p => p.Tag as string == "__settings__");
                if (settingsHost != null) SwitchToPanel(settingsHost);
            }
            UpdateSidebarButtonHighlights();
        }

        private void btnRibbonHelp_Click(object? sender, EventArgs e)
        {
            using var dlg = new HelpDialog();
            dlg.ShowDialog(this);
        }

        private void toolStripTheme_Click(object? sender, EventArgs e)
        {
            _themeService.CurrentMode = _themeService.CurrentMode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
            ApplyTheme();
        }

        private void toolStripToggleAgents_Click(object? sender, EventArgs e)
        {
            mainSplitContainer.Panel1Collapsed = !mainSplitContainer.Panel1Collapsed;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Save session history before shutting down
            if (_settingsService.Current.PersistSession && _persistentEngine != null)
            {
                List<ChatMessage> snapshot;
                lock (_persistentEngine.History) { snapshot = _persistentEngine.History.ToList(); }
                _sessionService.SaveCurrentSession(snapshot);
            }
            else if (!_settingsService.Current.PersistSession)
            {
                // no clear
            }

            _cts?.Cancel();
            _cts?.Dispose();
            base.OnFormClosed(e);
        }

        private void toolStripOpen_Click(object? sender, EventArgs e) => AskForProjectFolder();
        private void toolStripAnalyze_Click(object? sender, EventArgs e) => analyzeButton_Click(sender, e);

        private async void toolStripBuild_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_projectContext.ProjectPath)) return;
            AppendChat("Build", "Starting build...");
            var res = await _buildService.RunDotnetBuildAsync(_projectContext.ProjectPath);
            AppendChat("Build", res.Output);

            diagnosticsList.Items.Clear();
            var lines = res.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines) diagnosticsList.Items.Add(ParseDiagnostic(line));
        }

        private ListViewItem ParseDiagnostic(string d)
        {
            var file = ""; var lineNo = ""; var msg = d;
            try
            {
                var idx = d.IndexOf(": error"); if (idx < 0) idx = d.IndexOf(": warning");
                if (idx > 0)
                {
                    var prefix = d.Substring(0, idx); var paren = prefix.IndexOf('(');
                    if (paren > 0)
                    {
                        file = prefix.Substring(0, paren); var rest = prefix.Substring(paren + 1);
                        var comma = rest.IndexOf(','); var rparen = rest.IndexOf(')');
                        if (comma > 0) lineNo = rest.Substring(0, comma); else if (rparen > 0) lineNo = rest.Substring(0, rparen);
                    }
                }
            }
            catch { }
            return new ListViewItem(new[] { file, lineNo, msg });
        }

        private async void AskForProjectFile()
        {
            try
            {
                using var ofd = new OpenFileDialog { Filter = "C# Project|*.csproj|All files|*.*", Title = "Select project file" };
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    _projectContext.ProjectPath = ofd.FileName;
                    AppendChat("System", "Selected project: " + _projectContext.ProjectPath);

                    // Pre-load workspace in background
                    _ = _roslyn.GetOrLoadProjectAsync(ofd.FileName);

                    await RefreshSolutionExplorerAsync();
                }
            }
            catch (Exception ex) { AppendChat("System", "Error: " + ex.Message); }
        }

        private async void AskForProjectFolder()
        {
            try
            {
                using var fbd = new FolderBrowserDialog { Description = "Select workspace folder", UseDescriptionForTitle = true };
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    var folder = fbd.SelectedPath;
                    var csprojFiles = System.IO.Directory.GetFiles(folder, "*.csproj", System.IO.SearchOption.TopDirectoryOnly);
                    if (csprojFiles.Length > 0)
                    {
                        _projectContext.ProjectPath = csprojFiles[0];
                        AppendChat("System", $"Changed workspace: {System.IO.Path.GetFileName(csprojFiles[0])} in {folder}");
                        _ = _roslyn.GetOrLoadProjectAsync(csprojFiles[0]);
                    }
                    else
                    {
                        _projectContext.ProjectPath = System.IO.Path.Combine(folder, "workspace.csproj");
                        AppendChat("System", $"Changed workspace: {folder} (no .csproj found)");
                    }
                    await RefreshSolutionExplorerAsync();
                }
            }
            catch (Exception ex) { AppendChat("System", "Error: " + ex.Message); }
        }


        private async Task RunPlanWithPreviewAsync(string intent)
        {
            var classification = await _intentRouter.ClassifyAsync(intent, CancellationToken.None);
            var taskPlan = await _taskPlanner.TryPlanAsync(classification, intent, _orchestrator.GetToolDescriptors(), CancellationToken.None);

            if (taskPlan == null) return;
            _lastPlan = taskPlan;

            var previews = await _orchestrator.CollectPreviewStepsAsync(taskPlan, CancellationToken.None);
            if (previews.Count == 0)
            {
                var logProgress = new BufferedProgress(msg => { AppendChat("Orchestrator", msg); });
                var streamProgress = new BufferedProgress(AppendToken);
                var result = await _orchestrator.AgentLoopAsync(intent, taskPlan, logProgress, streamProgress, CancellationToken.None, _settingsService.Current.MaxSteps);
                logProgress.Flush();
                streamProgress.Flush();
                AppendChat("Orchestrator", result);
                return;
            }

            var dlg = new PreviewDialog();
            ApplyTheme(); // Restore Form1's custom backgrounds after the dialog registers with MaterialSkinManager
            dlg.SetItems(previews.Select(p => new PreviewDialog.PreviewItem { Index = p.Index, Title = p.Title, Preview = p.Preview, Selected = true }).ToList());
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                var selected = dlg.GetSelectedIndexes();
                var applied = await _orchestrator.ApplySelectedStepsAsync(taskPlan, selected, CancellationToken.None);
                foreach (var ap in applied)
                {
                    string outStr = ap.Result?.Output ?? "No output";
                    AppendChat("Orchestrator", $"Applied step {ap.Index}: {outStr}");
                }
            }
        }

        private async void toolStripFiles_Click(object? sender, EventArgs e)
        {
            // Now redundant but keeping as a "Global Search" or flat list view
            List<string> files;
            try
            {
                if (!string.IsNullOrEmpty(_projectContext.ProjectPath))
                    files = await _fileSearchService.EnumerateProjectFilesAsync(_projectContext.ProjectPath, CancellationToken.None);
                else
                    files = await _fileSearchService.SearchFilesAsync("", CancellationToken.None);
            }
            catch (Exception ex) { MessageBox.Show("Error: " + ex.Message); return; }

            using var dlg = new Form { Text = "Project Files", Width = 800, Height = 600, StartPosition = FormStartPosition.CenterParent };
            var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
            lv.Columns.Add("Path", 750);
            foreach (var f in files) lv.Items.Add(new ListViewItem(new[] { f }));

            string? selectedPath = null;
            lv.DoubleClick += (s, ev) => {
                if (lv.SelectedItems.Count > 0)
                {
                    selectedPath = lv.SelectedItems[0].Text;
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                }
            };

            dlg.Controls.Add(lv);
            if (dlg.ShowDialog(this) == DialogResult.OK && selectedPath != null)
                await OpenFileInTabAsync(selectedPath);
        }

        private async void toolStripRunPlan_Click(object? sender, EventArgs e)
        {
            var text = inputBox.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            SwitchToPanel(_chatPanel);
            await RunPlanWithPreviewAsync(text);
        }

        // Loading Animation Tracker
        private int _loadingCount = 0;
        private void ShowLoading()
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(ShowLoading)); return; }
            _loadingCount++;
            if (_loadingCount > 0) _spinner.Start();
        }
        private void HideLoading()
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(HideLoading)); return; }
            _loadingCount--;
            if (_loadingCount <= 0) { _loadingCount = 0; _spinner.Stop(); }
        }

        public async Task ReloadOpenTabForFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return;

            var panel = _allPanels.FirstOrDefault(p => string.Equals(p.Tag as string, filePath, StringComparison.OrdinalIgnoreCase));
            if (panel == null) return;

            try
            {
                string newContent = await _fileSearchService.ReadFileAsync(filePath, CancellationToken.None);

                foreach (Control c in panel.Controls)
                {
                    if (c is ElementHost host && host.Child is TextEditor editor)
                    {
                        if (editor.Text != newContent)
                        {
                            int caretOffset = Math.Min(editor.CaretOffset, newContent.Length);
                            editor.Text = newContent;
                            editor.CaretOffset = caretOffset;
                        }
                        break;
                    }
                }
            }
            catch { }
        }

        public Task InvokeOpenFileInTabAsync(string path)
        {
            return OpenFileInTabAsync(path);
        }

        public async Task OpenFileInTabAsync(string path)
        {
            ShowLoading();
            try
            {
                if (!System.IO.File.Exists(path)) { AppendChat("System", "File not found: " + path); return; }

                // ── DOCX: route straight to the rich preview/edit panel ──────────
                if (path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    // Reuse existing DOCX tab for this exact file if open
                    var existingDocx = _allPanels.FirstOrDefault(p => string.Equals(p.Tag as string, path, StringComparison.OrdinalIgnoreCase));
                    if (existingDocx != null) { SwitchToPanel(existingDocx); return; }

                    // Queue conversion — ShowDocxPreview is called by the DocxPreviewReady handler
                    _docxPreviewService.QueuePreview(path);
                    return;
                }

                var isPdf = path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                string content = string.Empty;
                if (!isPdf)
                {
                    content = await _fileSearchService.ReadFileAsync(path, CancellationToken.None);
                }
                var fileName = System.IO.Path.GetFileName(path);
                var existing = _allPanels.FirstOrDefault(p => string.Equals(p.Tag as string, path, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    await ReloadOpenTabForFileAsync(path);
                    SwitchToPanel(existing);
                    return;
                }

                // Outer panel holds editor + minimap side by side
                var outerPanel = new Panel { Dock = DockStyle.Fill, BackColor = _themeService.Colors.Background, Padding = new Padding(0) };

                Gravity.UI.MinimapPanel? minimap = null;
                TextEditor? editor = null;
                SemanticColorizer? colorizer = null;
                Gravity.UI.ErrorUnderlineRenderer? errorRenderer = null;

                if (isPdf)
                {
                    var webView = new Microsoft.Web.WebView2.WinForms.WebView2
                    {
                        Dock = DockStyle.Fill,
                        Source = new Uri(path)
                    };
                    outerPanel.Controls.Add(webView);
                }
                else
                {
                    // Minimap on the right
                    bool isDark = _themeService.CurrentMode == ThemeMode.Dark;
                    minimap = new Gravity.UI.MinimapPanel();
                    minimap.SetColors(_themeService.Colors.Background, isDark);
                    minimap.Dock = DockStyle.Right;
                    outerPanel.Controls.Add(minimap);

                    // AvalonEdit in the center
                    var elementHost = new ElementHost { Dock = DockStyle.Fill };
                    editor = new TextEditor
                    {
                        Text = content,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 13,
                        ShowLineNumbers = true,
                        IsReadOnly = false,
                        WordWrap = false,
                        HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
                    };

                    colorizer = new SemanticColorizer { IsDarkTheme = isDark };
                    editor.TextArea.TextView.LineTransformers.Add(colorizer);
                    errorRenderer = new Gravity.UI.ErrorUnderlineRenderer(editor.Document);
                    editor.TextArea.TextView.BackgroundRenderers.Add(errorRenderer);
                    ApplyEditorTheme(editor, _themeService.Colors);

                    // Load HighlightingManager definitions for .css, .html, .js, .json, .xml, .py, etc.
                    var ext = System.IO.Path.GetExtension(path);
                    if (!string.IsNullOrEmpty(ext))
                    {
                        var syntaxDef = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinitionByExtension(ext);
                        if (syntaxDef != null)
                            editor.SyntaxHighlighting = syntaxDef;
                    }

                    // Live status bar on caret move
                    editor.TextArea.Caret.PositionChanged += (s, ev) =>
                    {
                        if (outerPanel?.Visible == true && (outerPanel?.Tag as string) == path)
                            this.BeginInvoke(new Action(() => UpdateStatusBar(editor)));
                    };

                    elementHost.Child = editor;
                    outerPanel.Controls.Add(elementHost);
                }

                outerPanel.Tag = path; // Important for finding existing
                mainDocumentArea.Controls.Add(outerPanel);

                tabStrip.Visible = true;
                _allPanels.Add(outerPanel);
                var newTab = CreateTabButton(fileName, outerPanel, path);

                this.BeginInvoke(new Action(() => {
                    SelectTab(newTab);
                }));

                if (!isPdf && minimap != null && editor != null)
                {
                    // Defer minimap attachment to next message pump cycle so ElementHost
                    // handle is guaranteed to be created and WPF content fully loaded.
                    var capturedMinimap = minimap;
                    var capturedEditor = editor;
                    this.BeginInvoke(new Action(() =>
                    {
                        try { capturedMinimap.AttachEditor(capturedEditor); }
                        catch { /* best-effort */ }
                    }));

                    // Defer status bar update �?" avoids blocking WinForms thread
                    // before WPF layout has completed.
                    this.BeginInvoke(new Action(() => UpdateStatusBar(editor)));

                    if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && colorizer != null)
                    {
                        // Pass content directly - avoids accessing WPF DependencyProperty from wrong thread
                        var tooltipCtrl = new Gravity.UI.ErrorTooltipController(editor);
                        _ = ApplyRoslynEditorFeaturesAsync(editor, colorizer, errorRenderer, tooltipCtrl, path, content);
                        _ = new Gravity.UI.AvalonCompletionController(editor, _roslyn, () => _projectContext.ProjectPath, path);

                        // ── Live diagnostics on TextChanged (debounced 750ms) ─────────────
                        // Captures so lambdas hold refs without re-evaluating loop vars
                        var liveEditor        = editor;
                        var liveErrorRenderer = errorRenderer;
                        var liveTooltipCtrl   = tooltipCtrl;
                        System.Threading.Timer? liveDebounce = null;

                        liveEditor.TextChanged += (_, _) =>
                        {
                            liveDebounce?.Dispose();
                            liveDebounce = new System.Threading.Timer(async _ =>
                            {
                                try
                                {
                                    // Read text on WPF dispatcher, then leave that thread immediately
                                    string liveCode = string.Empty;
                                    liveEditor.Dispatcher.Invoke(() => liveCode = liveEditor.Text);
                                    if (string.IsNullOrEmpty(liveCode)) return;

                                    var liveDiags = await _roslyn.GetLiveDiagnosticsAsync(_projectContext.ProjectPath, path, liveCode);

                                    liveEditor.Dispatcher.Invoke(() =>
                                    {
                                        liveErrorRenderer?.SetDiagnostics(liveDiags);
                                        liveEditor.TextArea.TextView.Redraw();
                                    });

                                    if (liveTooltipCtrl != null)
                                    {
                                        var spans = liveDiags.Select(d => (
                                            Offset:  d.Location.SourceSpan.Start,
                                            Length:  Math.Max(1, d.Location.SourceSpan.Length),
                                            Message: d.GetMessage(),
                                            IsError: d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
                                        liveTooltipCtrl.UpdateDiagnostics(spans);
                                    }
                                }
                                catch { /* best-effort live check */ }
                            }, null, 750, System.Threading.Timeout.Infinite);
                        };

                        // Breakpoint Margin
                        var capturedEditor2 = editor;
                        var capturedPath2 = path;
                        capturedEditor2.Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                var bpMargin = new Gravity.UI.BreakpointMargin(_debugService, capturedPath2);
                                capturedEditor2.TextArea.LeftMargins.Insert(0, bpMargin);
                            }
                            catch { /* best-effort */ }
                        });
                    }
                }

                UpdateBreadcrumb(path);

            }
            catch (Exception ex) { AppendChat("System", "Error: " + ex.Message); }
            finally { HideLoading(); }
        }

        private async void analyzeButton_Click(object? sender, EventArgs e)
        {
        }

        /// <summary>Opens a file dialog to pick an image and stages it as the pending attachment.</summary>
        private void attachImageButton_Click(object? sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Attach an Image",
                Filter = "Images|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp|All Files|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            var path = dlg.FileName;
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var mime = ext switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".gif" => "image/gif",
                    ".bmp" => "image/bmp",
                    ".webp" => "image/webp",
                    _ => "image/jpeg"
                };

                var bytes = File.ReadAllBytes(path);
                var base64 = Convert.ToBase64String(bytes);
                _pendingImageAttachment = new ImageAttachment
                {
                    MimeType = mime,
                    Base64Data = base64,
                    FilePath = path
                };

                ShowImagePreviewStrip(path);
            }
            catch (Exception ex)
            {
                AppendChat("System", $"Could not attach image: {ex.Message}");
            }
        }

        /// <summary>Shows a compact thumbnail strip above the input box for the staged image.</summary>
        private void ShowImagePreviewStrip(string imagePath)
        {
            ClearImagePreviewStrip();

            bool isDark = inputPanel.Tag is bool b ? b : true;

            // Cache fonts locally so they can be managed/disposed
            var labelFont = new Font("Segoe UI", 9f);
            var buttonFont = new Font("Segoe UI", 9f, FontStyle.Bold);

            _imagePreviewStrip = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = isDark ? Color.FromArgb(10, 18, 50) : Color.FromArgb(230, 235, 250),
                Padding = new Padding(6, 4, 6, 4)
            };

            _imageThumbBox = new PictureBox
            {
                Width = 48,
                Height = 48,
                SizeMode = PictureBoxSizeMode.Zoom,
                Left = 6,
                Top = 4
            };

            try
            {
                // FIX: Read image via a memory stream to avoid locking the physical file on disk
                using (var stream = new System.IO.FileStream(imagePath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                {
                    _imageThumbBox.Image = Image.FromStream(stream);
                }
            }
            catch
            {
                // Fallback or error icon logic here if load fails
            }

            // FIX: Use Docking/Anchoring instead of hardcoded math so it survives window resizing
            var lblName = new Label
            {
                Text = "🖼️ " + Path.GetFileName(imagePath),
                ForeColor = isDark ? Color.FromArgb(190, 200, 255) : Color.FromArgb(40, 60, 140),
                BackColor = Color.Transparent,
                Left = 60,
                Top = 8,
                Height = 20,
                Font = labelFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var btnDismiss = new Button
            {
                Text = "✕", // Clean Unicode "close" character
                Width = 26,
                Height = 26,
                Top = 14,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                ForeColor = isDark ? Color.FromArgb(200, 155, 20) : Color.FromArgb(50, 80, 200),
                BackColor = Color.Transparent,
                Font = buttonFont,
                Anchor = AnchorStyles.Top | AnchorStyles.Right // Sticks to the right edge during resizing
            };

            // Position button relative to the current container state
            btnDismiss.Left = _imagePreviewStrip.Width - btnDismiss.Width - 12;
            btnDismiss.FlatAppearance.BorderSize = 0;
            btnDismiss.Click += (_, __) => ClearImageAttachment();

            // Now safe to dynamic-calculate label width bounding between thumbnail and button
            lblName.Width = btnDismiss.Left - lblName.Left - 6;

            _imagePreviewStrip.Controls.Add(_imageThumbBox);
            _imagePreviewStrip.Controls.Add(lblName);
            _imagePreviewStrip.Controls.Add(btnDismiss);

            // Properly hook up clean-up on disposal to prevent GDI leaks
            _imagePreviewStrip.Disposed += (s, e) =>
            {
                labelFont.Dispose();
                buttonFont.Dispose();
                if (_imageThumbBox.Image != null)
                {
                    _imageThumbBox.Image.Dispose();
                }
            };

            var parent = inputPanel.Parent;
            if (parent != null)
            {
                // Add elements cleanly matching Dock layer priorities
                parent.Controls.Add(_imagePreviewStrip);
                _imagePreviewStrip.BringToFront();
                inputPanel.BringToFront();
            }
        }
        private void ClearImagePreviewStrip()
        {
            if (_imagePreviewStrip == null) return;
            _imageThumbBox?.Image?.Dispose();
            _imageThumbBox?.Dispose();
            _imageThumbBox = null;
            _imagePreviewStrip.Parent?.Controls.Remove(_imagePreviewStrip);
            _imagePreviewStrip.Dispose();
            _imagePreviewStrip = null;
        }

        private void ClearImageAttachment()
        {
            _pendingImageAttachment = null;
            ClearImagePreviewStrip();
        }

        private async void sendButton_Click(object? sender, EventArgs e)
        {
            var userText = inputBox.Text?.Trim();
            if (string.IsNullOrEmpty(userText) && _pendingImageAttachment == null) return;

            var text = string.IsNullOrEmpty(userText) ? "Describe this image." : userText;
            var image = _pendingImageAttachment;

            SwitchToPanel(_chatPanel);
            inputBox.Clear();
            ClearImageAttachment();
            await RunPromptAsync(text, image);
        }

        /// <summary>Programmatically runs a user prompt through the full routing pipeline.</summary>
        public async Task RunPromptAsync(string userText, ImageAttachment? imageAttachment = null)
        {
            if (string.IsNullOrWhiteSpace(userText)) return;

            if (_persistentEngine != null && _pendingPlanApprovals.TryGetValue(_persistentEngine.Id, out var pendingArgs))
            {
                _pendingPlanApprovals.Remove(_persistentEngine.Id);
                pendingArgs.Completion.TrySetResult(false);
                await Task.Delay(200);
            }

            SwitchToPanel(_chatPanel);

            // --- Direct routing paths (no agent spawn) ---
            var agentNames = _router.GetAgentNames().ToList();
            var matchedAgent = agentNames.FirstOrDefault(a => userText.StartsWith(a + ":", StringComparison.OrdinalIgnoreCase));
            if (userText.StartsWith("agent:", StringComparison.OrdinalIgnoreCase) || matchedAgent != null)
            {
                AppendChat("You", userText, imageAttachment);
                var cmd = userText.StartsWith("agent:", StringComparison.OrdinalIgnoreCase) ? userText.Substring(6).Trim() : userText;
                AppendChat("System", $"Routing to {matchedAgent ?? "logic"}...");
                var result = await _router.RouteAsStringAsync(cmd, CancellationToken.None);
                AppendChat("Agent", string.IsNullOrWhiteSpace(result) ? "(Agent completed with no output)" : result);
                return;
            }

            AppendChat("You", userText, imageAttachment);

            ShowLoading();
            try
            {
                ShowThinkingBubble();

                if (_persistentEngine == null)
                {
                    try
                    {
                        _persistentEngine = _orchestrator.SpawnAgent(userText);

                        // Restore persisted session history if enabled
                        if (_settingsService.Current.PersistSession)
                        {
                            var saved = _sessionService.CurrentSession.History;
                            if (saved != null && saved.Count > 0)
                            {
                                lock (_persistentEngine.History)
                                {
                                    _persistentEngine.History.AddRange(saved);
                                }
                                AppendChat("System", $"\u21a9 Restored {saved.Count} messages from previous session.");
                            }
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        HideThinkingBubble();
                        AppendChat("System", $"Cannot spawn engine: {ex.Message}");
                        return;
                    }
                }

                // Select the agent
                _selectedAgentId = _persistentEngine.Id;
                _unreadCounts[_persistentEngine.Id] = 0;
                foreach (Control c in _agentListFlow.Controls)
                {
                    if (c is Panel p)
                    {
                        p.BackColor = (p.Tag as string == _persistentEngine.Id) ? Color.FromArgb(80, 80, 120) : Color.FromArgb(45, 45, 60);
                        if (p.Tag as string == _persistentEngine.Id)
                            foreach (Control child in p.Controls) if (child is Label l && l.BackColor == _themeService.Colors.Accent) l.Visible = false;
                    }
                }

                var group = new Gravity.UI.CollapsibleStepGroup();
                _stepGroups[_persistentEngine] = group;
                _activeStepPanels.Remove(_persistentEngine.Id);
                _activeBubbles.Remove(_persistentEngine.Id);
                _chatFlow.Controls.Add(group);
                if (_thinkingBubblePanel != null)
                {
                    int thinkIdx = _chatFlow.Controls.IndexOf(_thinkingBubblePanel);
                    if (thinkIdx >= 0) _chatFlow.Controls.SetChildIndex(group, thinkIdx);
                }
                _chatFlow.PerformLayout();

                // Execute
                try
                {
                    // Classify intent and generate plan BEFORE execution
                    TaskPlan? taskPlan = null;
                    IntentClassification? classification = null;
                    try
                    {
                        var clarifiedText = await _orchestrator.ClarifyIntentAsync(userText, CancellationToken.None);
                        if (clarifiedText != userText && _persistentEngine != null)
                        {
                            _persistentEngine.ClarifiedIntent = clarifiedText;
                            AppendChat("System", $"\U0001f4a1 Understood: \"{clarifiedText}\"");
                            userText = clarifiedText;
                        }

                        if (userText.StartsWith("Please execute this plan:", StringComparison.OrdinalIgnoreCase))
                        {
                            classification = new IntentClassification { Type = IntentType.CodeEdit, Shape = PlanShape.DirectAnswer, Confidence = 1.0f };
                        }
                        else
                        {
                            classification = await _intentRouter.ClassifyAsync(userText, CancellationToken.None);
                        }
                        AppendChat("System", $"Intent: {classification.Type} ({classification.Shape})");
                        if (_persistentEngine != null)
                        {
                            _persistentEngine.CurrentClassification = classification;
                        }

                        if (classification.Shape == PlanShape.TaskList || classification.Shape == PlanShape.ImplementationPlan)
                        {
                            taskPlan = await _taskPlanner.TryPlanAsync(classification, userText, _orchestrator.GetToolDescriptors(), CancellationToken.None);
                            if (taskPlan != null)
                                AppendChat("System", $"Plan: {taskPlan.Summary} ({taskPlan.Steps.Count} steps)");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendChat("System", $"Planning skipped: {ex.Message}");
                    }

                    if (_settingsService.Current.PersistSession && _persistentEngine != null)
                    {
                        List<ChatMessage> snapshot;
                        lock (_persistentEngine.History)
                        {
                            snapshot = _persistentEngine.History.ToList();
                        }
                        if (!snapshot.Any() || snapshot.Last().Content != userText)
                        {
                            snapshot.Add(new ChatMessage { Role = "user", Content = userText });
                        }
                        _sessionService.SaveCurrentSession(snapshot, userText);
                        this.BeginInvoke(new Action(() => {
                            if (_chatTabBtn != null)
                            {
                                _chatTabBtn.Text = _sessionService.CurrentSession.Name;
                            }
                            RefreshSessionList();
                        }));
                    }

                    // Execute with plan if available, otherwise direct
                    if (taskPlan != null && taskPlan.Steps.Count > 0)
                    {
                        _persistentEngine.SeedPlan(taskPlan);
                        _persistentEngine.TaskState = AgentTaskState.Executing;
                        for (int i = 0; i < taskPlan.Steps.Count; i++)
                        {
                            var step = taskPlan.Steps[i];
                            AppendChat("System", $"Step {i + 1}/{taskPlan.Steps.Count}: {step.Description}");
                            await _persistentEngine.ExecuteStepAsync(step, "gravity_persistent.log", _persistentEngine.Cts?.Token ?? CancellationToken.None, _settingsService.Current.MaxSteps);
                            AppendChat("System", $"Step {i + 1}/{taskPlan.Steps.Count} completed.");
                        }
                    }
                    else
                    {
                        if (_persistentEngine != null)
                        {
                            if (classification != null && classification.Shape == PlanShape.ImplementationPlan)
                            {
                                _persistentEngine.TaskState = AgentTaskState.Planning;
                            }
                            else
                            {
                                _persistentEngine.TaskState = AgentTaskState.Executing;
                            }
                        }
                        await Task.Run(() => _persistentEngine.ExecuteAsync(userText, "gravity_persistent.log", _persistentEngine.Cts?.Token ?? CancellationToken.None, _settingsService.Current.MaxSteps, imageAttachment));
                    }

                    if (_settingsService.Current.PersistSession && _persistentEngine != null)
                    {
                        List<ChatMessage> snapshot;
                        lock (_persistentEngine.History) { snapshot = _persistentEngine.History.ToList(); }
                        _sessionService.SaveCurrentSession(snapshot, userText);
                        this.BeginInvoke(new Action(() => {
                            if (_chatTabBtn != null)
                            {
                                _chatTabBtn.Text = _sessionService.CurrentSession.Name;
                            }
                            RefreshSessionList();
                        }));
                    }
                }
                catch (OperationCanceledException) when (_cts != null && _cts.IsCancellationRequested)
                {
                    AppendChat("System", "[Action Cancelled] Task was stopped by user.");
                }
                catch (OperationCanceledException ex)
                {
                    AppendChat("System", "[Network Timeout] AI provider stream disconnected or timed out mid-reasoning: " + ex.Message);
                }
                catch (Exception ex)
                {
                    AppendChat("System", "Reasoning loop failed: " + ex.Message);
                }
            }
            finally
            {
                HideThinkingBubble();
                HideLoading();
            }
        }




        private void stopButton_Click(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedAgentId))
            {
                _orchestrator.StopAgent(_selectedAgentId);
                AppendChat("System", "[Action Cancelled] The selected agent has been stopped.");
            }
            else if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                AppendChat("System", "[Action Cancelled] The autonomous loop has been stopped.");
            }
        }

        private void inputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                sendButton_Click(this, EventArgs.Empty);
            }
        }

        private ContextMenuStrip _runContextMenu = null!;

        private void SetupRibbonRunButton()
        {
            _runContextMenu = new ContextMenuStrip
            {
                BackColor = System.Drawing.Color.FromArgb(22, 24, 55),
                ForeColor = System.Drawing.Color.FromArgb(210, 220, 255),
                Font = new System.Drawing.Font("Segoe UI", 9f),
                RenderMode = ToolStripRenderMode.System
            };

            var itemProject = new ToolStripMenuItem("▶  Run / Debug Project (F5)")
            {
                ForeColor = System.Drawing.Color.FromArgb(210, 220, 255),
                BackColor = System.Drawing.Color.FromArgb(22, 24, 55)
            };
            itemProject.Click += async (s, e) => await StartDebugSessionAsync();

            var itemFile = new ToolStripMenuItem("📄  Run Current Active File")
            {
                ForeColor = System.Drawing.Color.FromArgb(210, 220, 255),
                BackColor = System.Drawing.Color.FromArgb(22, 24, 55)
            };
            itemFile.Click += async (s, e) =>
            {
                string? activePath = (_allPanels.FirstOrDefault(p => p.Visible && p.Tag is string tag && System.IO.File.Exists(tag))?.Tag as string);
                if (!string.IsNullOrEmpty(activePath))
                {
                    await StartDebugSessionAsync(activePath);
                }
                else
                {
                    await StartDebugSessionAsync();
                }
            };

            var itemPick = new ToolStripMenuItem("📂  Choose Executable / Script...")
            {
                ForeColor = System.Drawing.Color.FromArgb(210, 220, 255),
                BackColor = System.Drawing.Color.FromArgb(22, 24, 55)
            };
            itemPick.Click += (s, e) =>
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "Select File to Run/Debug",
                    Filter = "Executables & Scripts|*.exe;*.csproj;*.sln;*.py;*.js;*.ps1;*.sh|All Files|*.*"
                };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _ = StartDebugSessionAsync(dlg.FileName);
                }
            };

            var itemStop = new ToolStripMenuItem("⏹  Stop Debugging (Shift+F5)")
            {
                ForeColor = System.Drawing.Color.FromArgb(255, 100, 100),
                BackColor = System.Drawing.Color.FromArgb(22, 24, 55)
            };
            itemStop.Click += (s, e) => _debugService.Stop();

            _runContextMenu.Items.Add(itemProject);
            _runContextMenu.Items.Add(itemFile);
            _runContextMenu.Items.Add(itemPick);
            _runContextMenu.Items.Add(new ToolStripSeparator());
            _runContextMenu.Items.Add(itemStop);

            btnRibbonRun.ContextMenuStrip = _runContextMenu;

            // Small companion dropdown arrow button
            var btnRunDrop = new Button
            {
                Text = "▾",
                Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.FromArgb(100, 230, 140),
                BackColor = System.Drawing.Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Height = 48,
                MinimumSize = new System.Drawing.Size(20, 48),
                Margin = new Padding(0, 0, 12, 0),
                Cursor = Cursors.Hand
            };
            btnRunDrop.FlatAppearance.BorderSize = 0;
            btnRunDrop.Click += (s, e) => _runContextMenu.Show(btnRibbonRun, new System.Drawing.Point(0, btnRibbonRun.Height));

            int idx = ribbonFlowLayout.Controls.IndexOf(btnRibbonRun);
            if (idx >= 0)
            {
                ribbonFlowLayout.Controls.Add(btnRunDrop);
                ribbonFlowLayout.Controls.SetChildIndex(btnRunDrop, idx + 1);
            }

            _debugService.OnStateChanged += state => UpdateRibbonRunButton(state);
        }

        private void UpdateRibbonRunButton(DebugSessionState state)
        {
            if (IsDisposed || !IsHandleCreated) return;
            this.BeginInvoke(new Action(() =>
            {
                switch (state)
                {
                    case DebugSessionState.Running:
                        btnRibbonRun.Text = "⏹  Stop";
                        btnRibbonRun.ForeColor = System.Drawing.Color.FromArgb(255, 90, 90);
                        break;
                    case DebugSessionState.Paused:
                        btnRibbonRun.Text = "⏸  Paused";
                        btnRibbonRun.ForeColor = System.Drawing.Color.FromArgb(250, 190, 50);
                        break;
                    default:
                        btnRibbonRun.Text = "▶  Run";
                        btnRibbonRun.ForeColor = System.Drawing.Color.FromArgb(100, 230, 140);
                        break;
                }
            }));
        }

        private async Task StartDebugSessionAsync(string? targetOverride = null)
        {
            if (_debugTabBtn != null)
            {
                SelectTab(_debugTabBtn);
            }
            else
            {
                var debugHost = _allPanels.FirstOrDefault(p => p.Tag as string == "__debug__");
                if (debugHost != null) SwitchToPanel(debugHost);
            }

            string workDir = _projectContext.ProjectDirectory;
            if (string.IsNullOrEmpty(workDir))
                workDir = System.IO.Directory.GetCurrentDirectory();

            string target = targetOverride ?? "";
            if (string.IsNullOrEmpty(target))
            {
                var csprojs = System.IO.Directory.GetFiles(workDir, "*.csproj", System.IO.SearchOption.TopDirectoryOnly);
                var slns = System.IO.Directory.GetFiles(workDir, "*.sln", System.IO.SearchOption.TopDirectoryOnly);
                target = csprojs.Length > 0 ? csprojs[0]
                       : slns.Length > 0 ? slns[0]
                       : workDir;
            }

            await _debugService.StartAsync(target, workDir);
        }

        private void toolStripRun_Click(object? sender, EventArgs e)
        {
            if (_debugService.State == DebugSessionState.Running || _debugService.State == DebugSessionState.Paused)
            {
                _debugService.Stop();
                return;
            }

            _ = StartDebugSessionAsync();
        }

        /// <summary>
        /// Scans <paramref name="root"/> for known project entry-points and returns the
        /// appropriate shell command to execute, or null if none is recognised.
        /// </summary>
        private static string? DetectRunCommand(string root)
        {
            // .NET �?" prefer the first .csproj found
            var csproj = System.IO.Directory.GetFiles(root, "*.csproj", System.IO.SearchOption.TopDirectoryOnly);
            if (csproj.Length > 0)
                return $"dotnet run --project \"{System.IO.Path.GetFileName(csproj[0])}\"";

            // Node.js / JavaScript
            var packageJson = System.IO.Path.Combine(root, "package.json");
            if (System.IO.File.Exists(packageJson))
            {
                try
                {
                    var raw = System.IO.File.ReadAllText(packageJson);
                    // Prefer "start" script if present, otherwise fall back to "dev"
                    if (raw.Contains("\"start\"")) return "npm start";
                    if (raw.Contains("\"dev\"")) return "npm run dev";
                    return "npm start";
                }
                catch { return "npm start"; }
            }

            // Python — look for main.py / app.py / run.py
            foreach (var pyEntry in new[] { "main.py", "app.py", "run.py" })
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(root, pyEntry)))
                    return $"python \"{pyEntry}\"";
            }

            // Go
            if (System.IO.Directory.GetFiles(root, "*.go", System.IO.SearchOption.TopDirectoryOnly).Length > 0)
                return "go run .";

            // Makefile
            if (System.IO.File.Exists(System.IO.Path.Combine(root, "Makefile")))
                return "make";

            return null;
        }

        private async Task ApplyRoslynEditorFeaturesAsync(TextEditor editor, SemanticColorizer colorizer, Gravity.UI.ErrorUnderlineRenderer? errorRenderer, Gravity.UI.ErrorTooltipController? tooltipCtrl, string path, string code)
        {
            // Stage 1: Instant Syntactic pass (~50ms) using a lightweight AdhocWorkspace.
            // 'code' is passed in directly — never read WPF DPs from non-dispatcher threads.
            try
            {
                var fastSpans = await _roslyn.GetSyntacticClassificationsAsync(code);
                if (fastSpans.Any())
                {
                    colorizer.SetSpans(fastSpans);
                    editor.Dispatcher.Invoke(() => editor.TextArea.TextView.Redraw());
                }
            }
            catch { /* syntactic pass best-effort */ }

            // Stage 2: Full semantic pass & Roslyn diagnostics in background (uses MSBuild project).
            _ = Task.Run(async () =>
            {
                try
                {
                    if (string.IsNullOrEmpty(_projectContext.ProjectPath)) return;
                    var fullSpans = await _roslyn.GetClassificationSpansAsync(_projectContext.ProjectPath, path);
                    if (fullSpans.Any())
                    {
                        colorizer.SetSpans(fullSpans);
                    }

                    if (errorRenderer != null)
                    {
                        var diagnostics = await _roslyn.GetFileDiagnosticsAsync(_projectContext.ProjectPath, path);
                        errorRenderer.SetDiagnostics(diagnostics);

                        // Feed tooltip controller with offset-based spans
                        if (tooltipCtrl != null)
                        {
                            var tooltipSpans = diagnostics.Select(d => (
                                Offset:  d.Location.SourceSpan.Start,
                                Length:  d.Location.SourceSpan.Length,
                                Message: d.GetMessage(),
                                IsError: d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                            ));
                            tooltipCtrl.UpdateDiagnostics(tooltipSpans);
                        }
                    }

                    editor.Dispatcher.Invoke(() => editor.TextArea.TextView.Redraw());
                }
                catch { /* semantic pass best-effort */ }
            });
        }

        private Control CreateTextOrLinkControl(string text, Color textColor, Color linkColor, Font font, int maxWidth, bool isUser = false)
        {
            int measuredWidth = Math.Max(100, maxWidth - 16);
            var textBox = new TextBox
            {
                Text = text,
                ForeColor = textColor,
                BackColor = isUser ? Color.FromArgb(40, 50, 70) : Color.FromArgb(40, 40, 48),
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                ShortcutsEnabled = true,
                Font = font,
                Multiline = true,
                WordWrap = true,
                Width = maxWidth,
                Margin = new Padding(0, 2, 0, 2),
                Height = TextRenderer.MeasureText(text, font, new Size(measuredWidth, 0), TextFormatFlags.WordBreak).Height + 15
            };

            var matches = LinkRegex.Matches(text);
            if (matches.Count > 0)
            {
                var filePaths = new List<(int index, int length, string path)>();
                foreach (Match match in matches)
                {
                    string filePath = match.Groups["path"].Value.Replace("file:///", "").Replace("/", "\\");
                    filePaths.Add((match.Index, match.Length, filePath));
                }

                textBox.MouseClick += (s, e) =>
                {
                    int charIndex = textBox.GetCharIndexFromPosition(e.Location);
                    foreach (var fp in filePaths)
                    {
                        if (charIndex >= fp.index && charIndex < fp.index + fp.length)
                        {
                            _ = OpenFileInTabAsync(fp.path);
                            return;
                        }
                    }
                };

                textBox.MouseMove += (s, e) =>
                {
                    int charIndex = textBox.GetCharIndexFromPosition(e.Location);
                    bool overLink = false;
                    foreach (var fp in filePaths)
                    {
                        if (charIndex >= fp.index && charIndex < fp.index + fp.length)
                        {
                            overLink = true;
                            break;
                        }
                    }
                    textBox.Cursor = overLink ? Cursors.Hand : Cursors.IBeam;
                };
            }

            return textBox;
        }

        private Panel CreateCodeBlockControl(string code, string language, int maxWidth)
        {
            // 1. Define standard fonts to ensure we can dispose them if needed, 
            // or rely on a centralized cache. For a local method, we reuse them carefully.
            var headerFont = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            var codeFont = new Font("Consolas", 9.5F);

            var container = new Panel
            {
                Width = maxWidth,
                BackColor = Color.FromArgb(20, 20, 25),
                Padding = new Padding(1),
                Margin = new Padding(0, 8, 0, 8),
                Tag = "CodeBlockContainer"
            };

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.FromArgb(30, 30, 35),
                Padding = new Padding(10, 0, 10, 0)
            };

            var lblLang = new Label
            {
                Text = string.IsNullOrEmpty(language) ? "CODE" : language.ToUpper(),
                Font = headerFont,
                ForeColor = Color.FromArgb(150, 150, 160),
                Dock = DockStyle.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true // Prevents language text clipping
            };
            header.Controls.Add(lblLang);

            // Cleaned up the broken Unicode characters
            var btnCopy = new Label
            {
                Text = "📋 Copy",
                Font = headerFont,
                ForeColor = Color.FromArgb(150, 150, 160),
                Dock = DockStyle.Right,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Width = 70 // Expanded slightly to prevent text clipping across displays
            };

            btnCopy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(code);
                    btnCopy.Text = "✓ Copied!";
                    btnCopy.ForeColor = Color.FromArgb(100, 200, 100);

                    var timer = new System.Windows.Forms.Timer { Interval = 2000 };
                    timer.Tick += (sender, args) =>
                    {
                        btnCopy.Text = "📋 Copy";
                        btnCopy.ForeColor = Color.FromArgb(150, 150, 160);
                        timer.Stop();
                        timer.Dispose();
                    };
                    timer.Start();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to copy text: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            btnCopy.MouseEnter += (s, e) => { if (btnCopy.Text != "✓ Copied!") btnCopy.ForeColor = Color.White; };
            btnCopy.MouseLeave += (s, e) => { if (btnCopy.Text != "✓ Copied!") btnCopy.ForeColor = Color.FromArgb(150, 150, 160); };
            header.Controls.Add(btnCopy);

            var txtCode = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Text = code.TrimEnd(),
                Font = codeFont,
                BackColor = Color.FromArgb(20, 20, 25),
                ForeColor = Color.FromArgb(220, 220, 225),
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Margin = new Padding(0)
            };

            // Fix: Use TextBoxControl flags so the measurement matches how a TextBox actually renders text lines.
            Size size = TextRenderer.MeasureText(
                txtCode.Text,
                codeFont,
                new Size(maxWidth - 20, 10000),
                TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak
            );

            // Generous breathing room for the bottom horizontal scrollbar if long lines exist
            int calculatedHeight = size.Height + 30;
            if (calculatedHeight > 400)
            {
                calculatedHeight = 400;
            }

            container.Height = calculatedHeight + header.Height + 2;

            var textPaddingPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 8, 10, 8),
                BackColor = Color.FromArgb(20, 20, 25)
            };

            textPaddingPanel.Controls.Add(txtCode);

            // Layout addition order: Add the fill panel FIRST, then the top-docked panel,
            // or vice versa, but structure cleanly without relying purely on BringToFront bugs.
            container.Controls.Add(textPaddingPanel);
            container.Controls.Add(header);

            // Ensure fonts are cleaned up if the container itself is ever destroyed
            container.Disposed += (s, e) =>
            {
                headerFont.Dispose();
                codeFont.Dispose();
            };

            return container;
        }

        private void ParseAndAddMessageContent(FlowLayoutPanel parentPanel, string message, Color textColor, Color linkColor, Font font, int maxWidth, bool isUser = false)
        {
            int index = 0;
            while (index < message.Length)
            {
                int startCode = message.IndexOf("```", index);
                if (startCode == -1)
                {
                    string textSegment = message.Substring(index).Trim();
                    if (!string.IsNullOrEmpty(textSegment))
                    {
                        var ctrl = CreateTextOrLinkControl(textSegment, textColor, linkColor, font, maxWidth, isUser);
                        parentPanel.Controls.Add(ctrl);
                    }
                    break;
                }

                if (startCode > index)
                {
                    string textSegment = message.Substring(index, startCode - index).Trim();
                    if (!string.IsNullOrEmpty(textSegment))
                    {
                        var ctrl = CreateTextOrLinkControl(textSegment, textColor, linkColor, font, maxWidth, isUser);
                        parentPanel.Controls.Add(ctrl);
                    }
                }

                int endCode = message.IndexOf("```", startCode + 3);
                if (endCode == -1)
                {
                    string codeContent = message.Substring(startCode + 3);
                    string language = "";

                    int firstNewLine = codeContent.IndexOf('\n');
                    if (firstNewLine > 0 && firstNewLine < 20)
                    {
                        language = codeContent.Substring(0, firstNewLine).Trim();
                        codeContent = codeContent.Substring(firstNewLine + 1);
                    }

                    var codeCtrl = CreateCodeBlockControl(codeContent, language, maxWidth);
                    parentPanel.Controls.Add(codeCtrl);
                    break;
                }
                else
                {
                    string codeBlock = message.Substring(startCode + 3, endCode - (startCode + 3));
                    string language = "";

                    int firstNewLine = codeBlock.IndexOf('\n');
                    if (firstNewLine > 0 && firstNewLine < 20)
                    {
                        language = codeBlock.Substring(0, firstNewLine).Trim();
                        codeBlock = codeBlock.Substring(firstNewLine + 1);
                    }

                    var codeCtrl = CreateCodeBlockControl(codeBlock, language, maxWidth);
                    parentPanel.Controls.Add(codeCtrl);

                    index = endCode + 3;
                }
            }
        }

        private void ShowThinkingBubble()
        {
            if (_chatFlow.InvokeRequired) { _chatFlow.Invoke(new Action(ShowThinkingBubble)); return; }
            if (_thinkingBubblePanel != null) return;

            var c = _themeService.Colors;
            bool isDark = _themeService.CurrentMode == ThemeMode.Dark;

            _thinkingBubblePanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(10, 5, 80, 5),
                Width = Math.Max(300, _chatFlow.ClientSize.Width - 25),
                Margin = new Padding(0, 5, 0, 5),
                Tag = "ThinkingBubble"
            };

            var bubblePanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                BackColor = isDark ? Color.FromArgb(45, 45, 55) : Color.FromArgb(235, 235, 240),
                Padding = new Padding(12),
                Margin = new Padding(0)
            };

            int maxBubbleWidth = Math.Max(200, _chatFlow.ClientSize.Width - 120);
            bubblePanel.MaximumSize = new Size(maxBubbleWidth, 0);

            var lblHeader = new Label
            {
                Text = "Agent �?� Thinking",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = isDark ? Color.FromArgb(170, 170, 180) : Color.FromArgb(100, 100, 110),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };
            bubblePanel.Controls.Add(lblHeader);

            _lblThinkingText = new Label
            {
                Text = "Thinking...",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Italic),
                ForeColor = isDark ? Color.FromArgb(200, 200, 210) : Color.FromArgb(80, 80, 90),
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2)
            };
            bubblePanel.Controls.Add(_lblThinkingText);

            bubblePanel.SizeChanged += (s, e) => {
                if (bubblePanel.Width > 0 && bubblePanel.Height > 0)
                {
                    var path = new System.Drawing.Drawing2D.GraphicsPath();
                    int r = 12;
                    if (bubblePanel.Width > r && bubblePanel.Height > r)
                    {
                        path.AddArc(0, 0, r, r, 180, 90);
                        path.AddArc(bubblePanel.Width - r, 0, r, r, 270, 90);
                        path.AddArc(bubblePanel.Width - r, bubblePanel.Height - r, r, r, 0, 90);
                        path.AddArc(0, bubblePanel.Height - r, r, r, 90, 90);
                        path.CloseFigure();
                        bubblePanel.Region = new Region(path);
                    }
                    else
                    {
                        bubblePanel.Region = null;
                    }
                }
            };

            _thinkingBubblePanel.Controls.Add(bubblePanel);
            _chatFlow.Controls.Add(_thinkingBubblePanel);

            _thinkingFrame = 0;
            _thinkingTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _thinkingTimer.Tick += (s, e) => {
                if (_lblThinkingText == null || _lblThinkingText.IsDisposed) return;
                _thinkingFrame = (_thinkingFrame + 1) % 4;
                string dots = new string('.', _thinkingFrame);
                _lblThinkingText.Text = "Thinking" + dots;
            };
            _thinkingTimer.Start();

            ScrollToBottom();
        }

        private void HideThinkingBubble()
        {
            if (_chatFlow.InvokeRequired) { _chatFlow.Invoke(new Action(HideThinkingBubble)); return; }
            if (_thinkingTimer != null)
            {
                _thinkingTimer.Stop();
                _thinkingTimer.Dispose();
                _thinkingTimer = null;
            }
            if (_thinkingBubblePanel != null)
            {
                _chatFlow.Controls.Remove(_thinkingBubblePanel);
                _thinkingBubblePanel.Dispose();
                _thinkingBubblePanel = null;
            }
            _lblThinkingText = null;
        }

        private void AppendChat(string who, string message, ImageAttachment? imageAttachment = null)
        {
            if (_chatFlow.InvokeRequired) { _chatFlow.Invoke(new Action(() => AppendChat(who, message, imageAttachment))); return; }
            if (_streamingBubblePanel != null)
            {
                var parentPanel = _streamingBubblePanel.Parent as Control;
                if (parentPanel != null && _chatFlow.Controls.Contains(parentPanel))
                {
                    _chatFlow.Controls.Remove(parentPanel);
                    parentPanel.Dispose();
                }
                _streamingBubblePanel = null;
                _streamingContent = "";
            }

            bool isUser = who == "You";
            string time = DateTime.Now.ToString("HH:mm");

            if (who == "Agent" || who == "Orchestrator")
            {
                if (message.StartsWith("[Final Message]"))
                    message = message.Substring("[Final Message]".Length).TrimStart(':', ' ');
                else if (message.StartsWith("[Final]"))
                    message = message.Substring("[Final]".Length).TrimStart(':', ' ');
            }

            var bubble = new Gravity.UI.ChatMessageBubble(who, time, message, isUser, imageAttachment);
            // Width must be set BEFORE adding to the FlowLayoutPanel, otherwise
            // the bubble measures against 0 and collapses.
            int flowW = _chatFlow.ClientSize.Width;
            if (flowW < 50) flowW = Math.Max(300, this.ClientSize.Width - 250);
            bubble.Width = Math.Max(300, flowW - 10);
            _chatFlow.Controls.Add(bubble);
            bubble.AutoSizeBubble();

            if (_thinkingBubblePanel != null)
                _chatFlow.Controls.SetChildIndex(_thinkingBubblePanel, _chatFlow.Controls.Count - 1);

            ScrollToBottom();
        }

        private void ChatScrollContainer_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _chatScrollContainer != null)
            {
                _scrollStartPoint = e.Location;
                _scrollStartOffset = -_chatScrollContainer.AutoScrollPosition.Y;
                _isScrollDragging = false;
            }
        }

        private void ChatScrollContainer_MouseMove(object? sender, MouseEventArgs e)
        {
            if (MouseButtons.Left != Control.MouseButtons || _chatScrollContainer == null) { _isScrollDragging = false; return; }

            int deltaY = _scrollStartPoint.Y - e.Y;
            if (!_isScrollDragging && Math.Abs(deltaY) > 10)
                _isScrollDragging = true;

            if (_isScrollDragging)
                _chatScrollContainer.AutoScrollPosition = new Point(0, _scrollStartOffset + deltaY);
        }

        private void ChatScrollContainer_MouseUp(object? sender, MouseEventArgs e)
        {
            _isScrollDragging = false;
        }

        private void ScrollToBottom()
        {
            if (_chatFlow.InvokeRequired)
            {
                _chatFlow.BeginInvoke(new Action(ScrollToBottom));
                return;
            }

            if (_chatScrollContainer != null)
            {
                int maxScroll = Math.Max(0, _chatFlow.Height - _chatScrollContainer.ClientSize.Height);
                int currentScroll = -_chatScrollContainer.AutoScrollPosition.Y;
                if (maxScroll - currentScroll > 60) return;
                _chatScrollContainer.AutoScrollPosition = new Point(0, maxScroll);
            }
            else if (_chatFlow.Controls.Count > 0)
            {
                _chatFlow.ScrollControlIntoView(_chatFlow.Controls[_chatFlow.Controls.Count - 1]);
            }
        }

        private void ResizeChatControl(Control ctrl, int chatWidth)
        {
            if (ctrl == null) return;

            if (ctrl is Panel cardP && (cardP.Tag as string == "ApprovalCardPanel" || cardP.Tag as string == "ArtifactCardPanel"))
            {
                int targetWidth = Math.Max(300, chatWidth - 30);
                Gravity.UI.CardLayoutHelper.ResizeCardPanel(cardP, targetWidth);
                return;
            }

            // Handle ChatMessageBubble controls
            if (ctrl is Gravity.UI.ChatMessageBubble cmb)
            {
                cmb.Width = Math.Max(300, chatWidth - 10);
                cmb.AutoSizeBubble();
                return;
            }

            // Handle ChatRow controls
            if (ctrl is Gravity.UI.ChatRow chatRow)
            {
                chatRow.Width = Math.Max(300, chatWidth - 10);
                return;
            }

            string tag = ctrl.Tag as string ?? "";

            if (tag == "MsgBlock" || tag == "StreamingBubble")
            {
                ctrl.Width = Math.Max(300, chatWidth - 25);
                int contentWidth = Math.Max(200, ctrl.ClientSize.Width - 120);

                foreach (Control child in ctrl.Controls)
                {
                    if (child is TextBox tb)
                    {
                        tb.Width = contentWidth;
                        int measuredWidth = Math.Max(100, contentWidth - 16);
                        tb.Height = TextRenderer.MeasureText(tb.Text, tb.Font, new Size(measuredWidth, 0), TextFormatFlags.WordBreak).Height + 15;
                    }
                    else if (child is Panel codeContainer && codeContainer.Tag as string == "CodeBlockContainer")
                    {
                        codeContainer.Width = contentWidth;
                        foreach (Control sub in codeContainer.Controls)
                        {
                            if (sub is Panel textPadding && textPadding.Dock == DockStyle.Fill)
                            {
                                foreach (Control txt in textPadding.Controls)
                                {
                                    if (txt is TextBox codeTxt)
                                    {
                                        Size size = TextRenderer.MeasureText(codeTxt.Text, codeTxt.Font, new Size(10000, 0), TextFormatFlags.Default);
                                        int calculatedHeight = size.Height + 25;
                                        if (calculatedHeight > 400)
                                        {
                                            calculatedHeight = 400;
                                        }
                                        codeContainer.Height = calculatedHeight + 28 + 10;
                                    }
                                }
                            }
                        }
                    }
                    else if (child is Panel p && p.Height == 1) // Separator
                    {
                        p.Width = contentWidth;
                    }
                    else if (child is Gravity.UI.CollapsibleStepGroup stepGroup)
                    {
                        stepGroup.Width = contentWidth;
                    }
                }
            }
            else if (tag == "AgentStepGroupOuter" || (ctrl is FlowLayoutPanel flp && flp.Controls.Count > 0 && flp.Controls[0] is FlowLayoutPanel bp && bp.Controls.Count > 1 && bp.Controls[1] is CollapsibleStepGroup))
            {
                ctrl.Width = Math.Max(300, chatWidth - 25);
                int targetContentWidth = Math.Max(200, chatWidth - 145);
                foreach (Control child in ctrl.Controls)
                {
                    if (child is FlowLayoutPanel bubble)
                    {
                        bubble.MaximumSize = new Size(Math.Max(200, chatWidth - 120), 0);
                        foreach (Control sub in bubble.Controls)
                        {
                            if (sub is CollapsibleStepGroup stepGroup)
                            {
                                stepGroup.Width = targetContentWidth;
                            }
                        }
                    }
                }
            }
            else if (tag == "ThinkingBubble")
            {
                ctrl.Width = Math.Max(300, chatWidth - 25);
            }
            else
            {
                ctrl.Width = Math.Max(300, chatWidth - 25);
            }
        }

        private string _streamingContent = "";
        private FlowLayoutPanel? _streamingBubblePanel = null;
        private DateTime _lastStreamUpdate = DateTime.MinValue;

        private void AppendToken(string token)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string>(AppendToken), token);
                return;
            }

            if (!string.IsNullOrEmpty(_selectedAgentId) && _activeBubbles.TryGetValue(_selectedAgentId, out var bubble) && bubble != null)
            {
                bubble.AppendContent(token);
                return;
            }

            _streamingContent += token;

            if (_streamingBubblePanel == null)
            {
                int contentWidth = Math.Max(200, _chatFlow.ClientSize.Width - 120);

                _streamingBubblePanel = new Gravity.UI.DoubleBufferedFlowLayoutPanel
                {
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoSize = true,
                    Width = Math.Max(300, _chatFlow.ClientSize.Width - 25),
                    Margin = new Padding(10, 3, 80, 3),
                    Padding = new Padding(0),
                    Tag = "StreamingBubble"
                };

                var lblHeader = new Label
                {
                    Text = $"Agent �?� {DateTime.Now:HH:mm}",
                    Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(170, 170, 180),
                    AutoSize = true,
                    Margin = new Padding(0, 0, 0, 2)
                };
                _streamingBubblePanel.Controls.Add(lblHeader);

                var tb = new RichTextBox
                {
                    Text = "",
                    ForeColor = Color.White,
                    BackColor = Color.FromArgb(40, 40, 48),
                    BorderStyle = BorderStyle.None,
                    ReadOnly = true,
                    ShortcutsEnabled = true,
                    Font = new Font("Segoe UI", 9.5F),
                    Multiline = true,
                    WordWrap = true,
                    Width = Math.Max(200, _streamingBubblePanel.Width - 10),
                    Margin = new Padding(0, 2, 0, 2),
                    Height = 30,
                    ScrollBars = RichTextBoxScrollBars.None
                };
                tb.ContentsResized += (s, e) => {
                    int newHeight = e.NewRectangle.Height + 5;
                    if (tb.Height != newHeight)
                    {
                        tb.Height = newHeight;
                    }
                };
                _streamingBubblePanel.Controls.Add(tb);

                _chatFlow.Controls.Add(_streamingBubblePanel);
                ScrollToBottom();
            }

            if (_streamingBubblePanel.Controls.Count > 1 && _streamingBubblePanel.Controls[1] is RichTextBox streamingTb)
            {
                streamingTb.AppendText(token);
            }
        }

        private readonly string _debugLogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gravity", "debug_session.log");

        private void AppendDebugLog(string message)
        {
            if (this.IsDisposed || _debugLogBox == null || _debugLogBox.IsDisposed || !_debugLogBox.IsHandleCreated)
                return;

            try
            {
                if (_debugLogBox.InvokeRequired)
                {
                    _debugLogBox.BeginInvoke(new Action<string>(AppendDebugLog), message);
                    return;
                }

                _debugLogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] ");
                _debugLogBox.AppendText(message);
                _debugLogBox.AppendText("\n\n");
                _debugLogBox.ScrollToCaret();

                _ = Task.Run(() => {
                    try { File.AppendAllText(_debugLogFilePath, $"[{DateTime.Now:HH:mm:ss}] {message}\n\n"); } catch { }
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void RegisterMouseWheelRecursive(Control ctrl)
        {
            bool isScrollableTextBox = ctrl is TextBox tb && (tb.ScrollBars == ScrollBars.Vertical || tb.ScrollBars == ScrollBars.Both);

            if (!isScrollableTextBox)
            {
                ctrl.MouseWheel -= Control_MouseWheel;
                ctrl.MouseWheel += Control_MouseWheel;
            }

            ctrl.ControlAdded += (s, e) => {
                if (e.Control != null) RegisterMouseWheelRecursive(e.Control);
            };

            foreach (Control child in ctrl.Controls)
            {
                RegisterMouseWheelRecursive(child);
            }
        }

        private void Control_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (_chatScrollContainer != null && !_chatScrollContainer.IsDisposed && _chatScrollContainer.Visible)
            {
                int currentY = -_chatScrollContainer.AutoScrollPosition.Y;
                int newY = currentY - e.Delta;
                if (newY < 0) newY = 0;
                _chatScrollContainer.AutoScrollPosition = new Point(0, newY);
            }
        }
    }

    public class BufferedProgress : IProgress<string>
    {
        private readonly Action<string> _action;
        private readonly System.Text.StringBuilder _buffer = new System.Text.StringBuilder();
        private readonly object _lock = new object();
        private DateTime _lastUpdate = DateTime.Now;

        public BufferedProgress(Action<string> action)
        {
            _action = action;
        }

        public void Report(string value)
        {
            bool shouldUpdate;
            string toReport = null;
            lock (_lock)
            {
                _buffer.Append(value);
                shouldUpdate = (DateTime.Now - _lastUpdate).TotalMilliseconds > 50;
                if (shouldUpdate)
                {
                    toReport = _buffer.ToString();
                    _buffer.Clear();
                    _lastUpdate = DateTime.Now;
                }
            }
            if (shouldUpdate && !string.IsNullOrEmpty(toReport))
            {
                _action(toReport);
            }
        }

        public void Flush()
        {
            string toReport = null;
            lock (_lock)
            {
                if (_buffer.Length > 0)
                {
                    toReport = _buffer.ToString();
                    _buffer.Clear();
                    _lastUpdate = DateTime.Now;
                }
            }
            if (!string.IsNullOrEmpty(toReport))
            {
                _action(toReport);
            }
        }
    }
}


