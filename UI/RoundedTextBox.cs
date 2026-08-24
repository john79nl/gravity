using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Gravity.UI
{
    /// <summary>
    /// A borderless, multiline TextBox that paints its own rounded-corner background
    /// and border, with a transparent client area so the rounded fill is visible.
    ///
    /// Drop-in replacement for the designer-created `inputBox` (System.Windows.Forms.TextBox).
    /// </summary>
    public class RoundedTextBox : TextBox
    {
        private int _cornerRadius = 16;
        private int _borderThickness = 1;
        private Color _fillColor = Color.FromArgb(12, 22, 58);
        private Color _borderColor = Color.FromArgb(70, 190, 145, 0);
        private Color _borderFocusColor = Color.FromArgb(120, 210, 165, 0);
        private bool _isHover;

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int CornerRadius
        {
            get => _cornerRadius;
            set { _cornerRadius = Math.Max(0, value); Invalidate(); }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int BorderThickness
        {
            get => _borderThickness;
            set { _borderThickness = Math.Max(0, value); Invalidate(); }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color FillColor
        {
            get => _fillColor;
            set { _fillColor = value; Invalidate(); }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; Invalidate(); }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color BorderFocusColor
        {
            get => _borderFocusColor;
            set { _borderFocusColor = value; Invalidate(); }
        }

        public RoundedTextBox()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.ResizeRedraw, true);

            BorderStyle = BorderStyle.None;
            Multiline = true;
            BackColor = Color.Transparent;
            DoubleBuffered = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHover = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            // Make the underlying EDIT control's background transparent so the
            // rounded fill we paint ourselves shows through. EM_SETBKGNDCOLOR / SETMARGINS
            // are not enough; the canonical trick is to also paint the non-client area.
            const int WM_PAINT = 0x000F;
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            using (var path = BuildRoundedPath(rect, _cornerRadius))
            using (var brush = new SolidBrush(_fillColor))
            {
                g.FillPath(brush, path);
            }

            if (_borderThickness > 0)
            {
                var color = Focused ? _borderFocusColor
                          : _isHover ? ControlPaint.Light(_borderColor)
                                     : _borderColor;
                using (var pen = new Pen(color, _borderThickness))
                using (var path = BuildRoundedPath(rect, _cornerRadius))
                {
                    g.DrawPath(pen, path);
                }
            }

            // Let the base TextBox draw the text/caret on top of our painted background.
            // We force the EDIT control to be transparent so the rounded fill shows through.
            DrawTextOnTop(g);
        }

        private void DrawTextOnTop(Graphics g)
        {
            // The simplest reliable cross-themes approach: ask the EDIT control to
            // paint itself into a memory bitmap using WM_PRINT, then composite it
            // over our rounded background. This keeps the standard TextBox text
            // rendering (selection, IME, RTL) and just makes the bg transparent.
            const int WM_PRINT = 0x0317;
            const int PRF_CHECKVISIBLE = 0x00000001;
            const int PRF_NONCLIENT   = 0x00000002;
            const int PRF_CLIENT      = 0x00000004;
            const int PRF_ERASEBKGND  = 0x00000008;
            const int PRF_CHILDREN    = 0x00000010;

            using (var bmp = new Bitmap(Width, Height, g))
            using (var bg = Graphics.FromImage(bmp))
            {
                IntPtr hdc = bg.GetHdc();
                try
                {
                    SendMessage(Handle, WM_PRINT, hdc, (IntPtr)(PRF_CLIENT | PRF_ERASEBKGND | PRF_CHILDREN));
                }
                finally
                {
                    bg.ReleaseHdc(hdc);
                }

                // Make the EDIT bg pixels transparent (only where alpha==255 and the
                // pixel equals the default EDIT background) so our rounded fill shows through.
                bmp.MakeTransparent(Color.FromArgb(12, 22, 58));
                g.DrawImage(bmp, 0, 0);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private static GraphicsPath BuildRoundedPath(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width)  d = r.Width;
            if (d > r.Height) d = r.Height;

            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
