using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Gravity.Core;

namespace Gravity.UI
{
    public class DebugPanel : UserControl
    {
        private readonly DebugService _debugService;
        private readonly IThemeService? _themeService;

        private bool _isDarkMode = true;

        // Callback delegate to trigger debug session from Form1
        public Func<System.Threading.Tasks.Task>? OnStartRequested;

        // Toolbar controls
        private readonly Button _btnStartStop;
        private readonly Button _btnPause;
        private readonly Button _btnClear;
        private readonly Button _btnThemeToggle;
        private readonly Label _lblStatus;

        // Containers & section headers
        private readonly Panel _mainBorderPanel;
        private readonly Panel _mainInnerPanel;
        private readonly Panel _toolbar;
        private readonly Panel _outputBorderPanel;
        private readonly Panel _outputPanel;
        private readonly Panel _bpBorderPanel;
        private readonly Panel _bpPanel;
        private readonly Label _consoleHeader;
        private readonly Label _bpHeader;

        // Terminal controls
        private readonly RichTextBox _outputBox;
        private readonly ListView _breakpointList;
        private readonly SplitContainer _splitContainer;

        public DebugPanel(DebugService debugService, IThemeService? themeService = null)
        {
            _debugService = debugService ?? throw new ArgumentNullException(nameof(debugService));
            _themeService = themeService;

            if (_themeService != null)
            {
                _isDarkMode = _themeService.CurrentMode == ThemeMode.Dark;
            }

            Padding = new Padding(6);

            // ── Outer Bordered Panel ──────────────────────────────────────────
            _mainBorderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1)
            };

            _mainInnerPanel = new Panel
            {
                Dock = DockStyle.Fill
            };

            // ── Toolbar ───────────────────────────────────────────────────────
            _toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 42,
                Padding = new Padding(8, 5, 8, 5)
            };

            _lblStatus = new Label
            {
                Text = "● Idle",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                AutoSize = true,
                Dock = DockStyle.Right,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 4, 8, 0)
            };

            _btnStartStop = MakeToolBtn("▶  Start Debug", Color.FromArgb(50, 205, 100));
            _btnPause     = MakeToolBtn("⏸ Pause",  Color.FromArgb(240, 190, 60));
            _btnClear     = MakeToolBtn("🧹 Clear", Color.FromArgb(140, 150, 190));
            _btnThemeToggle = MakeToolBtn(_isDarkMode ? "☀️ Light Mode" : "🌙 Dark Mode", Color.FromArgb(120, 130, 170));

            _btnStartStop.Click += async (s, e) =>
            {
                if (_debugService.State == DebugSessionState.Running || _debugService.State == DebugSessionState.Paused)
                    _debugService.Stop();
                else
                    await OnStartAsync();
            };

            _btnPause.Click += (s, e) => OnPause();
            _btnClear.Click += (s, e) => ClearTerminal();
            _btnThemeToggle.Click += (s, e) => ToggleTheme();

            var toolFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0)
            };
            toolFlow.Controls.Add(_btnStartStop);
            toolFlow.Controls.Add(_btnPause);
            toolFlow.Controls.Add(_btnClear);
            toolFlow.Controls.Add(_btnThemeToggle);

            _toolbar.Controls.Add(_lblStatus);
            _toolbar.Controls.Add(toolFlow);

            // ── Split: Terminal Console (Left) + Breakpoints (Right) ──────────
            _splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical
            };

            // ── Terminal Console Screen ───────────────────────────────────────
            _consoleHeader = BuildSectionHeader(">_ TERMINAL CONSOLE OUTPUT", Color.FromArgb(0, 180, 220));
            
            _outputBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10f, FontStyle.Regular),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Padding = new Padding(8)
            };

            _outputBorderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1)
            };
            _outputPanel = new Panel { Dock = DockStyle.Fill };
            _outputPanel.Controls.Add(_outputBox);
            _outputPanel.Controls.Add(_consoleHeader);
            _outputBorderPanel.Controls.Add(_outputPanel);
            _splitContainer.Panel1.Controls.Add(_outputBorderPanel);

            // ── Breakpoints List ──────────────────────────────────────────────
            _bpHeader = BuildSectionHeader("BREAKPOINTS", Color.FromArgb(220, 80, 80));
            _breakpointList = new ListView
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9f),
                View = View.Details,
                FullRowSelect = true,
                BorderStyle = BorderStyle.None,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _breakpointList.Columns.Add("File", 140);
            _breakpointList.Columns.Add("Line", 55);
            _breakpointList.Columns.Add("Enabled", 60);

            _bpBorderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1)
            };
            _bpPanel = new Panel { Dock = DockStyle.Fill };
            _bpPanel.Controls.Add(_breakpointList);
            _bpPanel.Controls.Add(_bpHeader);
            _bpBorderPanel.Controls.Add(_bpPanel);
            _splitContainer.Panel2.Controls.Add(_bpBorderPanel);

            // Assemble main inner panel
            _mainInnerPanel.Controls.Add(_splitContainer);
            _mainInnerPanel.Controls.Add(_toolbar);
            _splitContainer.BringToFront();

            _mainBorderPanel.Controls.Add(_mainInnerPanel);
            Controls.Add(_mainBorderPanel);

            // Apply initial theme scheme
            ApplyThemeColors();

            // Initial proportional splitter allocation
            this.Load += (s, e) => AdjustSplitter();
            this.SizeChanged += (s, e) => AdjustSplitter();

            // ── Wire events ───────────────────────────────────────────────────
            _debugService.OnDebugOutput += (msg, isError) =>
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(new Action(() => AppendOutput(msg, isError)));
            };
            _debugService.OnStateChanged += state =>
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(new Action(() => UpdateState(state)));
            };
            _debugService.OnBreakpointToggled += bp =>
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(new Action(RefreshBreakpointList));
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyThemeColors();
            if (_outputBox.TextLength == 0)
            {
                AppendOutput("[System] Terminal Ready. Press ▶ Start Debug to launch session.", isError: false, isSystem: true);
            }
        }

        public void SetThemeMode(ThemeMode mode)
        {
            _isDarkMode = mode == ThemeMode.Dark;
            ApplyThemeColors();
        }

        private void ToggleTheme()
        {
            _isDarkMode = !_isDarkMode;
            ApplyThemeColors();
        }

        private void ApplyThemeColors()
        {
            _btnThemeToggle.Text = _isDarkMode ? "☀️ Light Mode" : "🌙 Dark Mode";

            if (_isDarkMode)
            {
                // Dark Mode: Background Black, Text White
                BackColor = Color.Black;
                _mainBorderPanel.BackColor = Color.FromArgb(50, 55, 75);
                _mainInnerPanel.BackColor = Color.Black;
                _toolbar.BackColor = Color.FromArgb(20, 22, 32);

                _splitContainer.BackColor = Color.FromArgb(50, 55, 75);

                _outputBorderPanel.BackColor = Color.FromArgb(50, 55, 75);
                _outputPanel.BackColor = Color.Black;
                _outputBox.BackColor = Color.Black;
                _outputBox.ForeColor = Color.White;

                _bpBorderPanel.BackColor = Color.FromArgb(50, 55, 75);
                _bpPanel.BackColor = Color.FromArgb(16, 18, 28);
                _breakpointList.BackColor = Color.FromArgb(16, 18, 28);
                _breakpointList.ForeColor = Color.White;

                _consoleHeader.BackColor = Color.FromArgb(24, 28, 44);
                _consoleHeader.ForeColor = Color.FromArgb(0, 229, 255);

                _bpHeader.BackColor = Color.FromArgb(24, 28, 44);
                _bpHeader.ForeColor = Color.FromArgb(255, 120, 120);

                _lblStatus.ForeColor = Color.FromArgb(160, 175, 220);
            }
            else
            {
                // Light Mode: Background White, Text Black
                BackColor = Color.White;
                _mainBorderPanel.BackColor = Color.FromArgb(210, 215, 230);
                _mainInnerPanel.BackColor = Color.White;
                _toolbar.BackColor = Color.FromArgb(242, 244, 250);

                _splitContainer.BackColor = Color.FromArgb(210, 215, 230);

                _outputBorderPanel.BackColor = Color.FromArgb(210, 215, 230);
                _outputPanel.BackColor = Color.White;
                _outputBox.BackColor = Color.White;
                _outputBox.ForeColor = Color.Black;

                _bpBorderPanel.BackColor = Color.FromArgb(210, 215, 230);
                _bpPanel.BackColor = Color.FromArgb(250, 252, 255);
                _breakpointList.BackColor = Color.FromArgb(250, 252, 255);
                _breakpointList.ForeColor = Color.Black;

                _consoleHeader.BackColor = Color.FromArgb(230, 235, 248);
                _consoleHeader.ForeColor = Color.FromArgb(0, 110, 180);

                _bpHeader.BackColor = Color.FromArgb(230, 235, 248);
                _bpHeader.ForeColor = Color.FromArgb(190, 40, 40);

                _lblStatus.ForeColor = Color.FromArgb(50, 60, 100);
            }

            // Update existing button backgrounds
            Color btnBg = _isDarkMode ? Color.FromArgb(36, 42, 66) : Color.FromArgb(225, 230, 244);
            Color btnFg = _isDarkMode ? Color.White : Color.Black;

            foreach (Control ctrl in new Control[] { _btnStartStop, _btnPause, _btnClear, _btnThemeToggle })
            {
                if (ctrl is Button btn)
                {
                    btn.BackColor = btnBg;
                    btn.ForeColor = btnFg;
                }
            }

            // Re-format existing output box text if any
            if (_outputBox.IsHandleCreated && _outputBox.TextLength > 0)
            {
                _outputBox.SelectAll();
                _outputBox.SelectionColor = _isDarkMode ? Color.White : Color.Black;
                _outputBox.DeselectAll();
            }

            RefreshBreakpointList();
        }

        private void AdjustSplitter()
        {
            try
            {
                if (_splitContainer.Width > 100)
                {
                    int min1 = _splitContainer.Panel1MinSize;
                    int min2 = _splitContainer.Panel2MinSize;
                    int maxAllowed = _splitContainer.Width - min2;

                    if (maxAllowed > min1)
                    {
                        int target = (int)(_splitContainer.Width * 0.72);
                        if (target >= min1 && target <= maxAllowed)
                        {
                            _splitContainer.SplitterDistance = target;
                        }
                    }
                }
            }
            catch { }
        }

        private void ClearTerminal()
        {
            _outputBox.Clear();
            _outputBox.SelectionColor = _isDarkMode ? Color.White : Color.Black;
            AppendOutput("[System] Console output cleared.", isError: false, isSystem: true);
        }

        private async System.Threading.Tasks.Task OnStartAsync()
        {
            if (OnStartRequested != null)
            {
                await OnStartRequested.Invoke();
                return;
            }

            string workDir = System.IO.Directory.GetCurrentDirectory();
            string searchDir = workDir;
            while (!string.IsNullOrEmpty(searchDir) && (searchDir.EndsWith("bin", StringComparison.OrdinalIgnoreCase) || searchDir.Contains("bin\\", StringComparison.OrdinalIgnoreCase) || searchDir.Contains("obj\\", StringComparison.OrdinalIgnoreCase)))
            {
                var parent = System.IO.Directory.GetParent(searchDir);
                if (parent == null) break;
                searchDir = parent.FullName;
            }

            var csprojs = System.IO.Directory.GetFiles(searchDir, "*.csproj", System.IO.SearchOption.TopDirectoryOnly);
            string projectPath = csprojs.Length > 0 ? csprojs[0] : workDir;
            AppendOutput($"[Debug] Launching target: {projectPath}", isError: false, isSystem: true);
            await _debugService.StartAsync(projectPath, searchDir);
        }

        private void OnPause() => _debugService.Pause();

        private void AppendOutput(string message, bool isError, bool isSystem = false)
        {
            if (_outputBox.IsDisposed) return;

            if (!_outputBox.IsHandleCreated)
            {
                this.HandleCreated += (s, e) => AppendOutput(message, isError, isSystem);
                return;
            }

            _outputBox.SuspendLayout();

            Color textColor;
            if (isError)
                textColor = _isDarkMode ? Color.FromArgb(255, 110, 110) : Color.FromArgb(200, 20, 20);
            else if (isSystem)
                textColor = _isDarkMode ? Color.Cyan : Color.FromArgb(0, 120, 215);
            else
                textColor = _isDarkMode ? Color.White : Color.Black;

            int start = _outputBox.TextLength;
            _outputBox.SelectionStart = start;
            _outputBox.SelectionLength = 0;
            _outputBox.SelectionColor = textColor;

            _outputBox.AppendText(message + "\n");

            _outputBox.Select(start, message.Length + 1);
            _outputBox.SelectionColor = textColor;
            _outputBox.SelectionLength = 0;
            _outputBox.SelectionStart = _outputBox.TextLength;
            _outputBox.SelectionColor = textColor;

            _outputBox.ScrollToCaret();
            _outputBox.ResumeLayout();
        }

        private void UpdateState(DebugSessionState state)
        {
            string icon = state switch
            {
                DebugSessionState.Running => "● Running",
                DebugSessionState.Paused  => "⏸ Paused",
                DebugSessionState.Stopped => "■ Stopped",
                _                         => "● Idle"
            };
            Color color = state switch
            {
                DebugSessionState.Running => Color.FromArgb(50, 205, 100),
                DebugSessionState.Paused  => Color.FromArgb(240, 190, 60),
                DebugSessionState.Stopped => Color.FromArgb(235, 75, 75),
                _                         => _isDarkMode ? Color.FromArgb(160, 175, 220) : Color.FromArgb(60, 75, 120)
            };
            _lblStatus.Text = icon;
            _lblStatus.ForeColor = color;

            if (state == DebugSessionState.Running || state == DebugSessionState.Paused)
            {
                _btnStartStop.Text = "⏹  Stop Debug";
                _btnStartStop.FlatAppearance.BorderColor = Color.FromArgb(235, 75, 75);
            }
            else
            {
                _btnStartStop.Text = "▶  Start Debug";
                _btnStartStop.FlatAppearance.BorderColor = Color.FromArgb(50, 205, 100);
            }

            _btnPause.Enabled = state == DebugSessionState.Running;
        }

        private void RefreshBreakpointList()
        {
            _breakpointList.Items.Clear();
            foreach (var bp in _debugService.GetAllBreakpoints())
            {
                var item = new ListViewItem(System.IO.Path.GetFileName(bp.FilePath));
                item.SubItems.Add(bp.LineNumber.ToString());
                item.SubItems.Add(bp.IsEnabled ? "✓" : "—");
                item.ForeColor = bp.IsEnabled
                    ? (_isDarkMode ? Color.FromArgb(255, 110, 110) : Color.FromArgb(190, 40, 40))
                    : (_isDarkMode ? Color.FromArgb(180, 190, 220) : Color.FromArgb(100, 110, 130));
                _breakpointList.Items.Add(item);
            }
        }

        private static Button MakeToolBtn(string text, Color accent)
        {
            var btn = new Button
            {
                Text = text,
                Height = 30,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Margin = new Padding(3, 1, 3, 1),
                Padding = new Padding(8, 0, 8, 0),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = accent;
            btn.FlatAppearance.BorderSize = 1;
            return btn;
        }

        private static Label BuildSectionHeader(string title, Color accentColor)
        {
            return new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 28,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0)
            };
        }

        public async System.Threading.Tasks.Task HandleF5Async() => await OnStartAsync();
        public void HandleShiftF5() => _debugService.Stop();
    }
}
