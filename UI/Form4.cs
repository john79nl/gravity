using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Gravity.UI
{
    public partial class Form4 : Form
    {
        private Image _brainImage;
        private List<AgentNode> _agents = new List<AgentNode>();
        private Random _random = new Random();
        private float _time = 0f;

        // Define agent nodes
        public class AgentNode
        {
            public string Name { get; set; }
            public PointF TargetPosition { get; set; }
            public float SpawnProgress { get; set; } // 0 to 1
            public bool IsActive { get; set; }
            public Color NeonColor { get; set; }
        }

        public Form4()
        {
            InitializeComponent();

            // Try to load brain image
            string imgPath = Path.Combine(Application.StartupPath, "Resources", "icon_agent_brain.png");
            if (File.Exists(imgPath))
            {
                _brainImage = Image.FromFile(imgPath);
            }

            // Initialize agents (positions will be calculated in OnResize or OnPaint)
            _agents.Add(new AgentNode { Name = "CODE_EDITOR", NeonColor = Color.FromArgb(0, 255, 200) });
            _agents.Add(new AgentNode { Name = "SEARCH", NeonColor = Color.FromArgb(255, 100, 200) });
            _agents.Add(new AgentNode { Name = "PLANNER", NeonColor = Color.FromArgb(100, 150, 255) });
            _agents.Add(new AgentNode { Name = "TERMINAL", NeonColor = Color.FromArgb(200, 255, 0) });
        }

        private void Form4_Load(object sender, EventArgs e)
        {
            // Set input box styles
            inputBox.ForeColor = Color.DarkGray;
        }

        private void InputBox_Enter(object sender, EventArgs e)
        {
            if (inputBox.Text == "Ask the brain...")
            {
                inputBox.Text = "";
                inputBox.ForeColor = Color.White;
            }
        }

        private void InputBox_Leave(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(inputBox.Text))
            {
                inputBox.Text = "Ask the brain...";
                inputBox.ForeColor = Color.DarkGray;
            }
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;

                // Randomly summon one agent for demonstration
                foreach (var agent in _agents)
                {
                    agent.IsActive = false;
                }

                var targetAgent = _agents[_random.Next(_agents.Count)];
                targetAgent.IsActive = true;

                inputBox.Text = "";
            }
        }

        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            _time += 0.05f;
            bool needsRedraw = false;

            foreach (var agent in _agents)
            {
                if (agent.IsActive && agent.SpawnProgress < 1f)
                {
                    agent.SpawnProgress += 0.05f;
                    if (agent.SpawnProgress > 1f) agent.SpawnProgress = 1f;
                    needsRedraw = true;
                }
                else if (!agent.IsActive && agent.SpawnProgress > 0f)
                {
                    agent.SpawnProgress -= 0.05f;
                    if (agent.SpawnProgress < 0f) agent.SpawnProgress = 0f;
                    needsRedraw = true;
                }
            }

            // Pulse effect requires constant redraw
            needsRedraw = true;

            if (needsRedraw)
            {
                Invalidate();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Draw glassmorphism background
            using (var brush = new LinearGradientBrush(this.ClientRectangle, Color.FromArgb(10, 10, 20), Color.FromArgb(15, 25, 45), 45f))
            {
                e.Graphics.FillRectangle(brush, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            PointF center = new PointF(this.ClientSize.Width / 2f, this.ClientSize.Height / 2f - 40);

            // Calculate agent positions around a circle
            float radius = 250f;
            for (int i = 0; i < _agents.Count; i++)
            {
                float angle = (float)(i * Math.PI * 2 / _agents.Count) - (float)Math.PI / 2f;
                _agents[i].TargetPosition = new PointF(
                    center.X + (float)Math.Cos(angle) * radius,
                    center.Y + (float)Math.Sin(angle) * radius
                );
            }

            // Draw glowing connections
            foreach (var agent in _agents)
            {
                if (agent.SpawnProgress > 0)
                {
                    float distance = agent.SpawnProgress;
                    PointF endPoint = new PointF(
                        center.X + (agent.TargetPosition.X - center.X) * distance,
                        center.Y + (agent.TargetPosition.Y - center.Y) * distance
                    );

                    using (var pen = new Pen(Color.FromArgb((int)(agent.SpawnProgress * 150), agent.NeonColor), 3))
                    {
                        // Add glow effect to line
                        using (var glowPen = new Pen(Color.FromArgb((int)(agent.SpawnProgress * 50), agent.NeonColor), 10))
                        {
                            g.DrawLine(glowPen, center, endPoint);
                        }
                        g.DrawLine(pen, center, endPoint);
                    }

                    // Draw node if it has reached a certain point
                    if (agent.SpawnProgress > 0.8f)
                    {
                        float alpha = (agent.SpawnProgress - 0.8f) * 5f; // 0 to 1
                        DrawAgentNode(g, agent.TargetPosition, agent.Name, agent.NeonColor, alpha);
                    }
                }
            }

            // Draw brain glow
            float brainPulse = (float)Math.Sin(_time) * 10f + 20f;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(center.X - 80 - brainPulse, center.Y - 80 - brainPulse, 160 + brainPulse * 2, 160 + brainPulse * 2);
                using (var pgb = new PathGradientBrush(path))
                {
                    pgb.CenterPoint = center;
                    pgb.CenterColor = Color.FromArgb(100, 50, 150, 255);
                    pgb.SurroundColors = new[] { Color.Transparent };
                    g.FillPath(pgb, path);
                }
            }

            // Draw brain image
            if (_brainImage != null)
            {
                g.DrawImage(_brainImage, center.X - 64, center.Y - 64, 128, 128);
            }
            else
            {
                using (var brush = new SolidBrush(Color.FromArgb(200, 200, 255)))
                {
                    g.FillEllipse(brush, center.X - 40, center.Y - 40, 80, 80);
                }
            }
        }

        private void DrawAgentNode(Graphics g, PointF pos, string name, Color neonColor, float alpha)
        {
            int a = (int)(alpha * 255);
            if (a < 0) a = 0;
            if (a > 255) a = 255;

            // Draw node background
            using (var brush = new SolidBrush(Color.FromArgb(a, 20, 25, 40)))
            {
                RectangleF rect = new RectangleF(pos.X - 60, pos.Y - 20, 120, 40);

                // Rounded rect
                using (var path = GetRoundedRect(rect, 10))
                {
                    g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb(a, neonColor), 2))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            // Draw text
            using (var font = new Font("Segoe UI", 10, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
            {
                StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(name, font, brush, new RectangleF(pos.X - 60, pos.Y - 20, 120, 40), sf);
            }
        }

        private GraphicsPath GetRoundedRect(RectangleF bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Width + bounds.X - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Width + bounds.X - diameter, bounds.Height + bounds.Y - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Height + bounds.Y - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
