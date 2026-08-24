using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Windows.Forms.Integration;
using System.Linq;

namespace Gravity.UI
{
    public class StepPanel : Panel
    {
        private readonly Label _headerLabel;
        private readonly TextBox _contentBox;
        private readonly Panel _contextPanel;
        private readonly TextBox _contextBox;
        private readonly Label _toolLabel;
        private readonly Button _toggleContextBtn;
        private readonly ElementHost _badgeHost;
        private readonly System.Windows.Controls.StackPanel _badgeContainer;
        private readonly Dictionary<string, ActionBadge> _badges = new();
        private bool _contextExpanded = false;

        public StepPanel(int stepNumber)
        {
            this.Dock = DockStyle.Top;
            this.AutoSize = true;
            this.MinimumSize = new Size(0, 100);
            this.Padding = new Padding(10);
            this.BackColor = Color.FromArgb(24, 26, 38);
            this.Margin = new Padding(15, 0, 15, 12);
            this.Region = CreateRoundedRegion(this.Width, this.Height, 12);

            _headerLabel = new Label
            {
                Text = $"STEP {stepNumber}",
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 150, 255),
                Height = 25
            };

            _toggleContextBtn = new Button
            {
                Text = "Show Context [+] ",
                Dock = DockStyle.Top,
                FlatStyle = FlatStyle.Flat,
                Height = 25,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 7),
                Cursor = Cursors.Hand
            };
            _toggleContextBtn.FlatAppearance.BorderSize = 0;
            _toggleContextBtn.Click += (s, e) => ToggleContext();

            _contextPanel = new Panel { Dock = DockStyle.Top, Height = 0, Visible = false, Padding = new Padding(5) };
            _contextBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(25, 25, 30),
                ForeColor = Color.FromArgb(150, 150, 150),
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 8),
                ScrollBars = ScrollBars.Vertical
            };
            _contextPanel.Controls.Add(_contextBox);

            _contentBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(35, 35, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 10),
                Height = 20 // Will auto-grow
            };
            _contentBox.TextChanged += (s, e) => AutoGrow(_contentBox);

            _toolLabel = new Label
            {
                Text = "",
                Dock = DockStyle.Bottom,
                Font = new Font("Segoe UI", 8, FontStyle.Italic),
                ForeColor = Color.FromArgb(0, 200, 150),
                Height = 25,
                Visible = false
            };

            _badgeContainer = new System.Windows.Controls.StackPanel();
            _badgeHost = new ElementHost
            {
                Dock = DockStyle.Top,
                Child = _badgeContainer,
                BackColorTransparent = true,
                Height = 0,
                Margin = new Padding(0, 5, 0, 5)
            };

            this.Controls.Add(_toolLabel);
            this.Controls.Add(_contentBox);
            this.Controls.Add(_badgeHost);
            this.Controls.Add(_contextPanel);
            this.Controls.Add(_toggleContextBtn);
            this.Controls.Add(_headerLabel);
        }

        public void SetContext(string context)
        {
            _contextBox.Text = context;
        }

        public void AppendThought(string token)
        {
            _contentBox.AppendText(token);
        }

        public void SetToolAction(string toolAction)
        {
            _toolLabel.Text = $">> {toolAction}";
            _toolLabel.Visible = true;
        }

        public void AddAction(Gravity.Core.Agents.ActionTelemetry telemetry)
        {
            if (!_badges.TryGetValue(telemetry.Type, out var badge))
            {
                badge = new ActionBadge(telemetry.Type);
                _badges[telemetry.Type] = badge;
                _badgeContainer.Children.Add(badge);
                
                // Adjust height based on badges (rough estimate for WinForms measure)
                _badgeHost.Height += 30; 
            }

            badge.AddAction(telemetry.Detail);
            _badgeHost.Height += 20; // Grow for each detail item added
        }

        public void ApplyTheme(Core.ThemeColors c, bool isDark)
        {
            this.BackColor = isDark ? Color.FromArgb(35, 35, 45) : c.PanelBackground;
            _headerLabel.ForeColor = c.Accent;
            _toggleContextBtn.ForeColor = Color.Gray;
            
            _contentBox.BackColor = this.BackColor;
            _contentBox.ForeColor = isDark ? Color.White : c.Foreground;
            
            _contextBox.BackColor = isDark ? Color.FromArgb(25, 25, 30) : Color.FromArgb(230, 230, 230);
            _contextBox.ForeColor = isDark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(80, 80, 80);
            
            _toolLabel.ForeColor = c.Success;
            this.Invalidate();
        }

        private void ToggleContext()
        {
            _contextExpanded = !_contextExpanded;
            _contextPanel.Height = _contextExpanded ? 200 : 0;
            _contextPanel.Visible = _contextExpanded;
            _toggleContextBtn.Text = _contextExpanded ? "Hide Context [-] " : "Show Context [+] ";
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        private const int EM_GETLINECOUNT = 0x00BA;

        private void AutoGrow(TextBox tb)
        {
            if (tb.TextLength == 0)
            {
                tb.Height = 20;
                return;
            }
            int lineCount = SendMessage(tb.Handle, EM_GETLINECOUNT, 0, 0);
            tb.Height = Math.Max(20, (lineCount * tb.Font.Height) + 10);
        }

        private Region CreateRoundedRegion(int width, int height, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddArc(0, 0, radius, radius, 180, 90);
            path.AddArc(width - radius, 0, radius, radius, 270, 90);
            path.AddArc(width - radius, height - radius, radius, radius, 0, 90);
            path.AddArc(0, height - radius, radius, radius, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Region = CreateRoundedRegion(this.Width, this.Height, 12);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // Subtle glow border
            using var pen = new Pen(Color.FromArgb(60, 114, 137, 218), 2);
            e.Graphics.DrawPath(pen, GetRoundedPath(new Rectangle(1, 1, this.Width - 3, this.Height - 3), 12));
        }

        private GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.X + rect.Width - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.X + rect.Width - d, rect.Y + rect.Height - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Y + rect.Height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
