using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace Gravity.UI
{
    public class ConnectedAgentsControl : Panel
    {
        private readonly List<string> _agentNames;
        private readonly HashSet<string> _activeAgents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Bitmap? _brainImage;
        private readonly System.Windows.Forms.Timer _animationTimer;
        private float _pulsePhase = 0f;
        private readonly FlowLayoutPanel _pillsFlow;
        private readonly Dictionary<string, Panel> _pillControls = new Dictionary<string, Panel>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<string>? AgentClicked;

        public ConnectedAgentsControl(List<string> agentNames)
        {
            _agentNames = agentNames;
            
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            
            bool isDark = MaterialSkin.MaterialSkinManager.Instance?.Theme == MaterialSkin.MaterialSkinManager.Themes.DARK;
            this.BackColor = isDark ? Color.FromArgb(13, 20, 48) : Color.FromArgb(246, 248, 250);
            this.Padding = new Padding(12);
            this.Margin = new Padding(12, 8, 12, 8);
            this.Height = 110;

            try
            {
                if (System.IO.File.Exists("Resources\\icon_agent_brain.png"))
                {
                    _brainImage = new Bitmap("Resources\\icon_agent_brain.png");
                }
            }
            catch
            {
                // Fallback to null, will draw a custom circle/symbol if image is missing
            }

            // Create title label
            var lblTitle = new Label
            {
                Text = "COGNITIVE INTEGRATION ROUTER",
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                ForeColor = isDark ? Color.FromArgb(226, 232, 255) : Color.FromArgb(100, 100, 120),
                Location = new Point(115, 12),
                AutoSize = true
            };
            this.Controls.Add(lblTitle);

            // FlowLayoutPanel for agent badges/pills
            _pillsFlow = new FlowLayoutPanel
            {
                Location = new Point(112, 32),
                Size = new Size(this.Width - 130, 45),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.Transparent
            };
            this.Controls.Add(_pillsFlow);

            // Create pills
            foreach (var name in _agentNames)
            {
                var pill = CreatePill(name, isDark);
                _pillControls[name] = pill;
                _pillsFlow.Controls.Add(pill);
            }

            var lblDesc = new Label
            {
                Text = "Agents automatically engage when task matching coordinates execution.",
                Font = new Font("Segoe UI", 7F, FontStyle.Italic),
                ForeColor = isDark ? Color.FromArgb(138, 153, 199) : Color.FromArgb(120, 120, 140),
                Location = new Point(115, 82),
                AutoSize = true
            };
            this.Controls.Add(lblDesc);

            // Animation Timer
            _animationTimer = new System.Windows.Forms.Timer { Interval = 30 };
            _animationTimer.Tick += (s, e) =>
            {
                _pulsePhase += 0.08f;
                if (_pulsePhase > (float)Math.PI * 2) _pulsePhase -= (float)Math.PI * 2;
                
                // Redraw only the brain area
                this.Invalidate(new Rectangle(12, 12, 85, 85));
            };
            _animationTimer.Start();
        }

        private Panel CreatePill(string name, bool isDark)
        {
            var p = new Panel
            {
                AutoSize = true,
                Padding = new Padding(10, 4, 10, 4),
                Margin = new Padding(4, 4, 4, 4),
                BackColor = isDark ? Color.FromArgb(18, 28, 64) : Color.FromArgb(230, 235, 245),
                Cursor = Cursors.Hand
            };

            var lbl = new Label
            {
                Text = name.ToUpper(),
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                ForeColor = isDark ? Color.FromArgb(226, 232, 255) : Color.FromArgb(80, 90, 110),
                AutoSize = true,
                Dock = DockStyle.Fill
            };
            
            // Round the pill using GraphicsPath
            p.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color borderColor = isDark ? Color.FromArgb(26, 43, 86) : Color.FromArgb(200, 205, 220);
                if (_activeAgents.Contains(name))
                {
                    borderColor = Color.FromArgb(0, 242, 254); // Electric Neon Cyan border when active
                }
                
                // Draw rounded border
                using (var pen = new Pen(borderColor, 1))
                {
                    var rect = new Rectangle(0, 0, p.Width - 1, p.Height - 1);
                    int r = p.Height - 1;
                    using (var path = new GraphicsPath())
                    {
                        path.AddArc(rect.X, rect.Y, r, r, 180, 90);
                        path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
                        path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
                        path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
                        path.CloseFigure();
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            };

            p.Click += (s, e) => AgentClicked?.Invoke(this, name);
            lbl.Click += (s, e) => AgentClicked?.Invoke(this, name);

            p.Controls.Add(lbl);
            return p;
        }

        private string? FindMatchingAgent(string toolAgentName)
        {
            var target = toolAgentName.ToLowerInvariant();
            // Try exact match first
            if (_pillControls.ContainsKey(target)) return target;
            
            // Fuzzy match
            return _agentNames.FirstOrDefault(name => 
                name.Contains(target, StringComparison.OrdinalIgnoreCase) || 
                target.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        public void SetAgentActiveByTool(string toolAgentName, bool isActive)
        {
            var match = FindMatchingAgent(toolAgentName);
            if (match != null)
            {
                SetAgentActive(match, isActive);
            }
        }

        public void ClearAllActive()
        {
            foreach (var name in _activeAgents.ToList())
            {
                SetAgentActive(name, false);
            }
        }

        public void SetAgentActive(string name, bool isActive)
        {
            if (isActive)
                _activeAgents.Add(name);
            else
                _activeAgents.Remove(name);

            if (this.IsDisposed) return;

            try
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke((Action)(() => SetAgentActive(name, isActive)));
                    return;
                }

                bool isDark = MaterialSkin.MaterialSkinManager.Instance?.Theme == MaterialSkin.MaterialSkinManager.Themes.DARK;
                
                if (_pillControls.TryGetValue(name, out var pill))
                {
                    var lbl = pill.Controls.OfType<Label>().FirstOrDefault();
                    if (lbl != null)
                    {
                        if (isActive)
                        {
                            pill.BackColor = Color.FromArgb(46, 204, 113); // Green background when active
                            lbl.ForeColor = Color.White;
                        }
                        else
                        {
                            pill.BackColor = isDark ? Color.FromArgb(35, 38, 55) : Color.FromArgb(230, 235, 245);
                            lbl.ForeColor = isDark ? Color.FromArgb(160, 170, 200) : Color.FromArgb(80, 90, 110);
                        }
                        pill.Invalidate();
                    }
                }
            }
            catch
            {
                // Prevent crashes during application shutdown/dispose
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_pillsFlow != null)
            {
                _pillsFlow.Width = Math.Max(100, this.Width - 130);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            
            bool isDark = MaterialSkin.MaterialSkinManager.Instance?.Theme == MaterialSkin.MaterialSkinManager.Themes.DARK;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Draw card border
            Color borderColor = isDark ? Color.FromArgb(26, 43, 86) : Color.FromArgb(208, 215, 222);
            using (var pen = new Pen(borderColor, 1))
            {
                var rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
                int r = 10;
                using (var path = new GraphicsPath())
                {
                    path.AddArc(rect.X, rect.Y, r, r, 180, 90);
                    path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
                    path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
                    path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
                    path.CloseFigure();
                    e.Graphics.DrawPath(pen, path);
                }
            }

            // Draw glowing background for brain image
            int brainX = 16;
            int brainY = 16;
            int brainSize = 76;
            int centerX = brainX + brainSize / 2;
            int centerY = brainY + brainSize / 2;

            bool anyActive = _activeAgents.Count > 0;

            // Draw connecting glow lines to active agent pills
            foreach (var activeName in _activeAgents)
            {
                if (_pillControls.TryGetValue(activeName, out var pill) && pill.Visible)
                {
                    Point pillCenter = new Point(
                        _pillsFlow.Left + pill.Left + pill.Width / 2,
                        _pillsFlow.Top + pill.Top + pill.Height / 2);

                    using var linePen = new Pen(Color.FromArgb(180, 0, 242, 254), 1.5f)
                    {
                        DashStyle = DashStyle.Dot
                    };

                    Point controlPoint1 = new Point(centerX + 30, centerY);
                    Point controlPoint2 = new Point(pillCenter.X - 20, pillCenter.Y);
                    e.Graphics.DrawBezier(linePen, new Point(centerX, centerY), controlPoint1, controlPoint2, pillCenter);

                    using var nodeBrush = new SolidBrush(Color.FromArgb(0, 242, 254));
                    e.Graphics.FillEllipse(nodeBrush, pillCenter.X - 3, pillCenter.Y - 3, 6, 6);
                }
            }
            
            if (anyActive)
            {
                // Pulsing cyan glow circle
                float pulse = (float)Math.Sin(_pulsePhase);
                int glowRadius = (int)(brainSize / 2 + 10 + 6 * pulse);
                int alpha = (int)(55 + 20 * pulse);
                Color glowColor = Color.FromArgb(alpha, 0, 242, 254); // Pulsing electric cyan glow
                
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(centerX - glowRadius, centerY - glowRadius, glowRadius * 2, glowRadius * 2);
                    using (var pgb = new PathGradientBrush(path))
                    {
                        pgb.CenterColor = glowColor;
                        pgb.SurroundColors = new[] { Color.Transparent };
                        e.Graphics.FillEllipse(pgb, centerX - glowRadius, centerY - glowRadius, glowRadius * 2, glowRadius * 2);
                    }
                }
            }
            else
            {
                // Faint static cyan glow circle
                int glowRadius = brainSize / 2 + 5;
                Color glowColor = isDark ? Color.FromArgb(25, 0, 242, 254) : Color.FromArgb(20, 9, 105, 218);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(centerX - glowRadius, centerY - glowRadius, glowRadius * 2, glowRadius * 2);
                    using (var pgb = new PathGradientBrush(path))
                    {
                        pgb.CenterColor = glowColor;
                        pgb.SurroundColors = new[] { Color.Transparent };
                        e.Graphics.FillEllipse(pgb, centerX - glowRadius, centerY - glowRadius, glowRadius * 2, glowRadius * 2);
                    }
                }
            }

            // Draw brain image
            if (_brainImage != null)
            {
                using (var imageAttributes = new ImageAttributes())
                {
                    ColorMatrix colorMatrix;
                    if (anyActive)
                    {
                        // High energy electric cyan tint
                        float pulseVal = 0.8f + 0.2f * (float)Math.Sin(_pulsePhase);
                        float[][] colorMatrixElements = {
                            new float[] {0.1f * pulseVal, 0, 0, 0, 0},
                            new float[] {0, 0.95f * pulseVal, 0, 0, 0},
                            new float[] {0, 0, 1.0f * pulseVal, 0, 0},
                            new float[] {0, 0, 0, 1, 0},
                            new float[] {0, 0, 0, 0, 1}
                        };
                        colorMatrix = new ColorMatrix(colorMatrixElements);
                    }
                    else
                    {
                        // Dim cool-cyan tint
                        float[][] colorMatrixElements = {
                            new float[] {0.2f, 0, 0, 0, 0},
                            new float[] {0, 0.6f, 0, 0, 0},
                            new float[] {0, 0, 0.8f, 0, 0},
                            new float[] {0, 0, 0, 0.85f, 0},
                            new float[] {0, 0, 0, 0, 1}
                        };
                        colorMatrix = new ColorMatrix(colorMatrixElements);
                    }
                    
                    imageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    e.Graphics.DrawImage(_brainImage, new Rectangle(brainX, brainY, brainSize, brainSize), 0, 0, _brainImage.Width, _brainImage.Height, GraphicsUnit.Pixel, imageAttributes);
                }
            }
            else
            {
                // Draw vector placeholder if image not loaded
                Color brainColor = anyActive ? Color.FromArgb(0, 242, 254) : (isDark ? Color.FromArgb(80, 80, 100) : Color.FromArgb(160, 160, 180));
                using (var pen = new Pen(brainColor, 2))
                {
                    e.Graphics.DrawEllipse(pen, brainX + 15, brainY + 15, brainSize - 30, brainSize - 30);
                    e.Graphics.DrawLine(pen, centerX, brainY + 15, centerX, brainY + brainSize - 15);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animationTimer?.Dispose();
                _brainImage?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
