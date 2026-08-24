using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Gravity.UI
{
    /// <summary>
    /// A small spinning circle animation to indicate background processing.
    /// </summary>
    public class LoadingSpinner : Control
    {
        private System.Windows.Forms.Timer _timer;
        private int _angle = 0;
        private Color _accentColor = Color.FromArgb(245, 200, 50);

        public LoadingSpinner()
        {
            this.DoubleBuffered = true;
            this.Size = new Size(80, 48);
            this.Visible = false;

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 20; // ~50fps for smoother math.sin bounce
            _timer.Tick += (s, e) =>
            {
                _angle = (_angle + 12) % 360;
                this.Invalidate();
            };
        }

        public void SetColors(Color background, Color accent)
        {
            this.BackColor = background;
            _accentColor = accent;
            this.Invalidate();
        }

        public void Start()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(Start));
                return;
            }
            this.Visible = true;
            _angle = 0;
            _timer.Start();
        }

        public void Stop()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(Stop));
                return;
            }
            _timer.Stop();
            this.Visible = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!this.Visible) return;
            
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            
            int dotSize = 8;
            int gap = 12;
            int startX = (this.Width - (dotSize * 3 + gap * 2)) / 2;
            int cy = this.Height / 2;
            
            for (int i = 0; i < 3; i++)
            {
                // Offset each dot's phase
                double phase = (_angle - (i * 45)) * Math.PI / 180.0;
                
                // Y bounce offset
                int dy = (int)(Math.Sin(phase) * 6);
                
                // Opacity pulse
                int alpha = 80 + (int)(175 * ((Math.Sin(phase) + 1) / 2));
                // clamp alpha
                alpha = Math.Max(0, Math.Min(255, alpha));
                
                using (var brush = new SolidBrush(Color.FromArgb(alpha, _accentColor)))
                {
                    e.Graphics.FillEllipse(brush, startX + i * (dotSize + gap), cy + dy - dotSize / 2, dotSize, dotSize);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer?.Dispose();
            base.Dispose(disposing);
        }
    }
}
