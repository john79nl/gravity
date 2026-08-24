using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Gravity.UI
{
    public class CollapsibleStepPanel : Panel
    {
        private readonly Label _headerLabel;
        private readonly Label _collapseIcon;
        private readonly Panel _timelineDot;
        private readonly TextBox _contentBox;
        private bool _expanded = false;

        // Token batching — accumulates streaming tokens and flushes every 80ms.
        // Prevents a full layout/measure cycle on every individual character.
        private readonly StringBuilder _pendingTokens = new StringBuilder();
        private readonly System.Windows.Forms.Timer _flushTimer;

        public CollapsibleStepPanel(int stepNumber, string toolName)
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            this.Dock = DockStyle.Top;
            this.BackColor = Color.Transparent;
            this.Padding = new Padding(16, 2, 4, 2); // left padding for indent
            this.Margin = new Padding(0);

            _flushTimer = new System.Windows.Forms.Timer { Interval = 80 };
            _flushTimer.Tick += FlushPendingTokens;

            bool isDark = MaterialSkin.MaterialSkinManager.Instance?.Theme == MaterialSkin.MaterialSkinManager.Themes.DARK;
            Color primaryText = isDark ? Color.FromArgb(230, 230, 230) : Color.FromArgb(40, 40, 40);
            Color secondaryText = isDark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(100, 100, 100);

            string shortTitle = BuildShortTitle(stepNumber, toolName);
            _headerLabel = new Label
            {
                Text = shortTitle,
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                ForeColor = secondaryText,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Margin = new Padding(0)
            };

            _collapseIcon = new Label
            {
                Text = "🔍 View",
                ForeColor = isDark ? Color.FromArgb(170, 190, 240) : Color.FromArgb(40, 80, 180),
                BackColor = isDark ? Color.FromArgb(40, 45, 65) : Color.FromArgb(230, 235, 250),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                AutoSize = true,
                Cursor = Cursors.Hand,
                Padding = new Padding(6, 2, 6, 2),
                Margin = new Padding(8, 0, 0, 0)
            };

            var clickHandler = (EventHandler)((s, e) => ToggleExpand());
            _collapseIcon.Click += clickHandler;
            _headerLabel.Click += clickHandler;

            var headerPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                Dock = DockStyle.Top,
                BackColor = Color.Transparent,
                Padding = new Padding(0),
                Margin = new Padding(0, 0, 0, 4)
            };
            headerPanel.Controls.Add(_headerLabel);
            headerPanel.Controls.Add(_collapseIcon); // View button next to title

            _contentBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BackColor = isDark ? Color.FromArgb(35, 35, 35) : Color.FromArgb(250, 250, 250),
                ForeColor = secondaryText,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9),
                Dock = DockStyle.Top,
                Height = 0,
                Visible = false,
                ScrollBars = ScrollBars.None,
                ShortcutsEnabled = true,
                Padding = new Padding(6),
                Margin = new Padding(0, 4, 0, 4)
            };

            this.Controls.Add(_contentBox);
            this.Controls.Add(headerPanel);

            if (!string.IsNullOrWhiteSpace(toolName))
            {
                _contentBox.Text = toolName;
            }
            this.Height = 26; // Always start collapsed as a compact single line
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _flushTimer?.Dispose();
            base.Dispose(disposing);
        }

        private static string BuildShortTitle(int stepNumber, string toolName)
        {
            if (stepNumber == 0 && !string.IsNullOrWhiteSpace(toolName))
                return toolName;

            if (string.IsNullOrWhiteSpace(toolName))
                return $"Step {stepNumber}";

            if (toolName.Length > 60)
                return $"Step {stepNumber}: Prompt Context";

            return $"Step {stepNumber}: {toolName}";
        }

        // P/Invoke to suppress WM_PAINT on a control without causing layout events.
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SendMessage(IntPtr hWnd, int msg, bool wParam, int lParam);
        private const int WM_SETREDRAW = 0x000B;

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessage")]
        private static extern int SendMessageInt(IntPtr hWnd, int msg, int wParam, int lParam);
        private const int EM_GETLINECOUNT = 0x00BA;

        /// <summary>
        /// Thread-safe: accumulates text in a buffer and flushes to the TextBox
        /// via a timer (max ~12 times/sec) to prevent per-token layout thrash.
        /// </summary>
        public void AppendContent(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendContent), text);
                return;
            }
            _pendingTokens.Append(text);
            if (!_flushTimer.Enabled)
                _flushTimer.Start();
        }

        /// <summary>
        /// Updates the header label text in real-time (e.g. from "Reasoning..." to
        /// "Reading Form1.cs") without touching the expand icon or content box.
        /// </summary>
        public void UpdateTitle(string newTitle)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateTitle), newTitle);
                return;
            }
            _headerLabel.Text = newTitle;
        }

        private void FlushPendingTokens(object? sender, EventArgs e)
        {
            _flushTimer.Stop();
            if (_pendingTokens.Length == 0) return;

            var text = _pendingTokens.ToString();
            _pendingTokens.Clear();

            bool wasEmpty = _contentBox.Text.Length == 0;

            _contentBox.AppendText(text);

            if (_expanded)
                AutoSizeContent();
        }

        public void SetContent(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SetContent), text);
                return;
            }
            _contentBox.Text = text;
            if (_expanded)
            {
                AutoSizeContent();
            }
            else
            {
                SetHeight(26);
            }
        }

        private void ToggleExpand()
        {
            _expanded = !_expanded;
            _collapseIcon.Text = _expanded ? "▲ Hide" : "🔍 View";
            _contentBox.Visible = _expanded;

            this.SuspendLayout();
            try
            {
                if (_expanded)
                    AutoSizeContent();
                else
                {
                    _contentBox.Height = 0;
                    SetHeight(26);
                }
            }
            finally
            {
                this.ResumeLayout(true);
            }
            this.Parent?.PerformLayout();
        }

        // Re-entrancy guard: prevents OnResize → AutoSizeContent → Height= → OnResize loops.
        private bool _sizingInProgress = false;

        private void AutoSizeContent()
        {
            if (!_expanded) return;

            int contentHeight;
            if (_contentBox.TextLength > 0)
            {
                int lineCount = SendMessageInt(_contentBox.Handle, EM_GETLINECOUNT, 0, 0);
                // Let the textbox expand fully to prevent nested scrolling!
                contentHeight = Math.Max(40, (lineCount * _contentBox.Font.Height) + 16);
            }
            else
            {
                contentHeight = 40;
            }
            
            // Only update heights if they actually changed — avoids cascading resize events.
            if (_contentBox.Height != contentHeight)
                _contentBox.Height = contentHeight;
            
            SetHeight(26 + contentHeight + 12);
        }

        /// <summary>Sets this.Height only when the value changes, preventing redundant resize cascades.</summary>
        private void SetHeight(int h)
        {
            if (this.Height != h)
                this.Height = h;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Guard against re-entrancy: our own SetHeight call fires OnResize,
            // which would otherwise recurse into AutoSizeContent indefinitely.
            if (_sizingInProgress) return;
            _sizingInProgress = true;
            try
            {
                if (_expanded) AutoSizeContent();
            }
            finally
            {
                _sizingInProgress = false;
            }
        }
    }
}
