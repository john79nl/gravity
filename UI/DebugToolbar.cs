using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Gravity.Core;

namespace Gravity.UI
{
    /// <summary>
    /// A slim always-visible debug toolbar that sits above the tab strip.
    /// Contains a split-button Start (with dropdown), Pause, Stop, and a status LED.
    /// </summary>
    public class DebugToolbar : UserControl
    {
        private readonly DebugService _debugService;

        // Public event so Form1 can intercept the "show debug panel" request
        public event Action? OnShowDebugPanel;
        // Fired when the user picks a run target from the dropdown
        public event Func<string, System.Threading.Tasks.Task>? OnStartRequested;

        private readonly Label _ledStatus;
        private readonly Button _btnStart;
        private readonly Button _btnDropArrow;
        private readonly Button _btnPause;
        private readonly Button _btnStop;
        private readonly ContextMenuStrip _startMenu;

        // Last-chosen project path (persisted across sessions within the process)
        private string? _lastProjectPath;

        public DebugToolbar(DebugService debugService)
        {
            _debugService = debugService ?? throw new ArgumentNullException(nameof(debugService));

            Height      = 38;
            BackColor   = Color.FromArgb(14, 16, 40);
            Dock        = DockStyle.Top;
            Padding     = new Padding(4, 3, 4, 3);

            // ── LED status ─────────────────────────────────────────────────────
            _ledStatus = new Label
            {
                Text      = "●  Idle",
                ForeColor = Color.FromArgb(110, 120, 180),
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                AutoSize  = true,
                Dock      = DockStyle.Right,
                TextAlign = ContentAlignment.MiddleRight,
                Padding   = new Padding(0, 0, 10, 0)
            };

            // ── Start split-button ─────────────────────────────────────────────
            _startMenu = BuildStartMenu();

            _btnStart = MakeBtn("▶  Start", Color.FromArgb(50, 200, 90), 76);
            _btnStart.Click += async (s, e) => await LaunchLastOrDefault();

            // Small arrow button next to Start to open the dropdown
            _btnDropArrow = new Button
            {
                Text      = "▾",
                Width     = 20,
                Height    = 28,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(38, 170, 70),
                Font      = new Font("Segoe UI", 8f),
                Cursor    = Cursors.Hand,
                Margin    = new Padding(0, 0, 6, 0)
            };
            _btnDropArrow.FlatAppearance.BorderSize  = 0;
            _btnDropArrow.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 200, 90);
            _btnDropArrow.Click += (s, e) =>
                _startMenu.Show(_btnStart, new Point(0, _btnStart.Height));

            // ── Pause / Stop ───────────────────────────────────────────────────
            _btnPause = MakeBtn("⏸", Color.FromArgb(230, 180, 40), 38);
            _btnPause.Click += (s, e) => { _debugService.Pause(); };

            _btnStop = MakeBtn("⏹", Color.FromArgb(210, 50, 50), 38);
            _btnStop.Click += (s, e) => { _debugService.Stop(); };

            // ── Separator label ────────────────────────────────────────────────
            var sep = new Label
            {
                Text      = "DEBUG",
                ForeColor = Color.FromArgb(70, 80, 130),
                Font      = new Font("Segoe UI", 7f, FontStyle.Bold),
                AutoSize  = true,
                Dock      = DockStyle.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(8, 0, 10, 0)
            };

            // ── Flow layout ────────────────────────────────────────────────────
            var flow = new FlowLayoutPanel
            {
                Dock          = DockStyle.Left,
                AutoSize      = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                BackColor     = Color.Transparent,
                Padding       = new Padding(0)
            };
            flow.Controls.Add(sep);
            flow.Controls.Add(_btnStart);
            flow.Controls.Add(_btnDropArrow);
            flow.Controls.Add(_btnPause);
            flow.Controls.Add(_btnStop);

            Controls.Add(_ledStatus);
            Controls.Add(flow);

            // ── Wire state changes ─────────────────────────────────────────────
            _debugService.OnStateChanged += state =>
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(new Action(() => UpdateState(state)));
            };

            UpdateState(DebugSessionState.Idle);
        }

        // ── Context menu ───────────────────────────────────────────────────────

        private ContextMenuStrip BuildStartMenu()
        {
            var menu = new ContextMenuStrip
            {
                BackColor  = Color.FromArgb(22, 24, 55),
                ForeColor  = Color.FromArgb(210, 220, 255),
                Font       = new Font("Segoe UI", 9f),
                RenderMode = ToolStripRenderMode.System
            };

            AddMenuItem(menu, "▶  Run Project  (F5)",
                "Run the .csproj or .sln in the current project folder",
                async () => await StartWithMode("project"));

            AddMenuItem(menu, "📄  Run Current File",
                "Run only the currently open script file",
                async () => await StartWithMode("file"));

            menu.Items.Add(new ToolStripSeparator());

            AddMenuItem(menu, "📂  Choose executable…",
                "Browse for an .exe, .py or .js to run",
                async () => await PickAndStart());

            return menu;
        }

        private static void AddMenuItem(ContextMenuStrip menu, string text, string tip,
            Func<System.Threading.Tasks.Task> action)
        {
            var item = new ToolStripMenuItem(text)
            {
                ToolTipText = tip,
                ForeColor   = Color.FromArgb(210, 220, 255),
                BackColor   = Color.FromArgb(22, 24, 55)
            };
            item.Click += async (s, e) => await action();
            menu.Items.Add(item);
        }

        // ── Launch logic ───────────────────────────────────────────────────────

        private async System.Threading.Tasks.Task LaunchLastOrDefault() =>
            await StartWithMode("project");

        private async System.Threading.Tasks.Task StartWithMode(string mode)
        {
            OnShowDebugPanel?.Invoke();

            string workDir  = DetectWorkDir();
            string target;

            if (mode == "file")
            {
                // Ask form1 for current file via event or default to first file found
                target = FindCurrentOrFirstFile(workDir);
            }
            else
            {
                // Project mode: prefer .csproj, then .sln, then directory
                var csprojs = Directory.GetFiles(workDir, "*.csproj", SearchOption.TopDirectoryOnly);
                var slns    = Directory.GetFiles(workDir, "*.sln",   SearchOption.TopDirectoryOnly);
                target = csprojs.Length > 0 ? csprojs[0]
                       : slns.Length    > 0 ? slns[0]
                       : workDir;
            }

            _lastProjectPath = target;
            if (OnStartRequested != null) await OnStartRequested(target);
            else await _debugService.StartAsync(target, workDir);
        }

        private async System.Threading.Tasks.Task PickAndStart()
        {
            string? picked = null;
            // Must run on STA thread
            BeginInvoke(new Action(async () =>
            {
                using var dlg = new OpenFileDialog
                {
                    Title  = "Choose file to run",
                    Filter = "Executables|*.exe;*.csproj;*.sln;*.py;*.js;*.ts|All Files|*.*"
                };
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    picked = dlg.FileName;
                    OnShowDebugPanel?.Invoke();
                    string workDir = Path.GetDirectoryName(picked) ?? Directory.GetCurrentDirectory();
                    if (OnStartRequested != null) await OnStartRequested(picked);
                    else await _debugService.StartAsync(picked, workDir);
                }
            }));
            await System.Threading.Tasks.Task.CompletedTask;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string DetectWorkDir()
        {
            // Walk up from current directory to find a .csproj
            var dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 4; i++)
            {
                if (Directory.GetFiles(dir, "*.csproj").Length > 0) return dir;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return Directory.GetCurrentDirectory();
        }

        private static string FindCurrentOrFirstFile(string workDir)
        {
            var cs = Directory.GetFiles(workDir, "*.cs", SearchOption.TopDirectoryOnly);
            return cs.Length > 0 ? cs[0] : workDir;
        }

        private void UpdateState(DebugSessionState state)
        {
            (string text, Color color) = state switch
            {
                DebugSessionState.Running => ("●  Running", Color.FromArgb(55, 200, 90)),
                DebugSessionState.Paused  => ("⏸  Paused",  Color.FromArgb(240, 185, 50)),
                DebugSessionState.Stopped => ("■  Stopped", Color.FromArgb(210, 60, 60)),
                _                         => ("●  Idle",    Color.FromArgb(110, 120, 180))
            };
            _ledStatus.Text      = text;
            _ledStatus.ForeColor = color;

            _btnStart.Enabled     = state != DebugSessionState.Running;
            _btnDropArrow.Enabled = state != DebugSessionState.Running;
            _btnPause.Enabled     = state == DebugSessionState.Running;
            _btnStop.Enabled      = state is DebugSessionState.Running or DebugSessionState.Paused;
        }

        private static Button MakeBtn(string text, Color accent, int width)
        {
            var btn = new Button
            {
                Text      = text,
                Width     = width,
                Height    = 28,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(28, 30, 60),
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Margin    = new Padding(2, 0, 2, 0),
                Cursor    = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor         = accent;
            btn.FlatAppearance.BorderSize          = 1;
            btn.FlatAppearance.MouseOverBackColor  =
                Color.FromArgb(accent.R / 5 + 25, accent.G / 5 + 25, accent.B / 5 + 50);
            return btn;
        }

        /// <summary>Called by Form1 to feed output into the debug panel when toolbar is used standalone.</summary>
        public void AttachOutputTarget(Action<string, bool> outputSink) =>
            _debugService.OnDebugOutput += outputSink;
    }
}
