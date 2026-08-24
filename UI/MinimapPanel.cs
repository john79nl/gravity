using System;
using System.Drawing;
using System.Windows.Forms;
using ICSharpCode.AvalonEdit;

namespace Gravity.UI
{
    /// <summary>
    /// VS Code-style minimap panel that renders a downscaled snapshot of the editor content
    /// and provides a draggable viewport indicator.
    /// </summary>
    public class MinimapPanel : Panel
    {
        private TextEditor? _editor;
        private string _text = "";
        private float _scrollRatio = 0f;
        private float _viewRatio = 1f;

        private bool _isDragging = false;
        private int _dragStartY = 0;
        private float _dragStartScrollRatio = 0f;

        private static readonly Font MinimapFont = new Font("Consolas", 2.0f);

        private Color _bg = Color.FromArgb(37, 37, 38);
        private Color _textColor = Color.FromArgb(90, 90, 90);
        private Color _viewportColor = Color.FromArgb(60, 60, 60);

        public MinimapPanel()
        {
            this.Width = 80;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Default;
        }

        public void SetColors(Color background, bool isDark)
        {
            _bg = background;
            _textColor = isDark
                ? Color.FromArgb(80, 80, 80)
                : Color.FromArgb(190, 190, 190);
            _viewportColor = isDark
                ? Color.FromArgb(80, 80, 80)
                : Color.FromArgb(190, 210, 235);
            Invalidate();
        }

        public void AttachEditor(TextEditor editor)
        {
            if (_editor != null) return; // prevent double-attach
            _editor = editor;
            _text = editor.Text;

            editor.TextChanged += (s, e) =>
            {
                _text = editor.Text;
                UpdateScrollInfo();
                SafeInvalidate();
            };

            editor.TextArea.TextView.ScrollOffsetChanged += (s, e) =>
            {
                UpdateScrollInfo();
                SafeInvalidate();
            };

            UpdateScrollInfo();
            SafeInvalidate();
        }

        private RichTextBox? _rtb;
        public void AttachRichTextBox(RichTextBox rtb)
        {
            if (_rtb != null || _editor != null) return;
            _rtb = rtb;
            _text = rtb.Text;

            rtb.TextChanged += (s, e) =>
            {
                _text = rtb.Text;
                UpdateScrollInfoRtb();
                SafeInvalidate();
            };

            rtb.VScroll += (s, e) =>
            {
                UpdateScrollInfoRtb();
                SafeInvalidate();
            };

            UpdateScrollInfoRtb();
            SafeInvalidate();
        }

        private void UpdateScrollInfoRtb()
        {
            if (_rtb == null) return;
            if (_rtb.InvokeRequired)
            {
                _rtb.BeginInvoke(new Action(UpdateScrollInfoRtb));
                return;
            }

            int firstChar = _rtb.GetCharIndexFromPosition(new Point(1, 1));
            int lastChar = _rtb.GetCharIndexFromPosition(new Point(1, _rtb.Height - 1));
            int total = _rtb.TextLength;
            if (total == 0)
            {
                _scrollRatio = 0f;
                _viewRatio = 1f;
            }
            else
            {
                _scrollRatio = Math.Max(0f, Math.Min(1f, (float)firstChar / total));
                _viewRatio = Math.Max(0f, Math.Min(1f, (float)(lastChar - firstChar) / total));
            }
        }

        private void SafeInvalidate()
        {
            if (this.IsHandleCreated)
                this.BeginInvoke(new Action(Invalidate));
        }

        private void UpdateScrollInfo()
        {
            if (_editor == null) return;
            _editor.Dispatcher.Invoke(() =>
            {
                var tv = _editor.TextArea.TextView;
                double total = tv.DocumentHeight;
                double view = tv.ActualHeight;
                double offset = tv.VerticalOffset;
                _scrollRatio = total > 0 ? (float)(offset / total) : 0f;
                _viewRatio = total > 0 ? (float)(view / total) : 1f;
            });
        }

        private void ScrollEditorToRatio(float ratio)
        {
            ratio = Math.Max(0f, Math.Min(1f, ratio));
            
            if (_editor != null)
            {
                _editor.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var tv = _editor.TextArea.TextView;
                    _editor.ScrollToVerticalOffset(ratio * tv.DocumentHeight);
                }));
            }
            else if (_rtb != null)
            {
                _rtb.BeginInvoke(new Action(() =>
                {
                    int targetChar = (int)(ratio * _rtb.TextLength);
                    if (targetChar < 0) targetChar = 0;
                    if (targetChar > _rtb.TextLength) targetChar = _rtb.TextLength;
                    _rtb.SelectionStart = targetChar;
                    _rtb.SelectionLength = 0;
                    _rtb.ScrollToCaret();
                }));
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _dragStartY = e.Y;
                _dragStartScrollRatio = _scrollRatio;
                this.Capture = true;

                // Jump immediately to clicked position
                float ratio = (float)e.Y / this.Height;
                ScrollEditorToRatio(ratio - _viewRatio / 2f);
                _dragStartScrollRatio = ratio - _viewRatio / 2f;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            // Change cursor when hovering over viewport indicator
            float vpTop = _scrollRatio * this.Height;
            float vpH = Math.Max(_viewRatio * this.Height, 20f);
            bool overViewport = e.Y >= vpTop && e.Y <= vpTop + vpH;
            this.Cursor = overViewport ? Cursors.SizeNS : Cursors.Default;

            if (_isDragging && e.Button == MouseButtons.Left)
            {
                int delta = e.Y - _dragStartY;
                float deltaRatio = (float)delta / this.Height;
                ScrollEditorToRatio(_dragStartScrollRatio + deltaRatio);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isDragging = false;
            this.Capture = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(_bg);

            if (string.IsNullOrEmpty(_text))
            {
                base.OnPaint(e);
                return;
            }

            var lines = _text.Split('\n');
            float lineH = 3.5f;
            float panelH = this.Height;
            float totalH = lines.Length * lineH;
            float scale = totalH > panelH ? panelH / totalH : 1f;
            float y = 0;
            int maxLines = (int)(panelH / (lineH * scale)) + 1;

            using (var brush = new SolidBrush(_textColor))
            {
                for (int i = 0; i < Math.Min(lines.Length, maxLines); i++)
                {
                    var line = lines[i].TrimEnd();
                    if (line.Length > 0)
                    {
                        string display = line.Length > 60 ? line.Substring(0, 60) : line;
                        float indent = (line.Length - line.TrimStart().Length) * 1.2f;
                        g.DrawString(display, MinimapFont, brush, indent, y);
                    }
                    y += lineH * scale;
                    if (y > panelH) break;
                }
            }

            // Viewport indicator
            float vpTop = _scrollRatio * panelH;
            float vpH = Math.Max(_viewRatio * panelH, 20f);

            using (var brush = new SolidBrush(Color.FromArgb(50, _viewportColor)))
                g.FillRectangle(brush, 0, vpTop, this.Width, vpH);

            using (var pen = new System.Drawing.Pen(Color.FromArgb(120, _viewportColor), 1))
                g.DrawRectangle(pen, 0, vpTop, this.Width - 1, vpH);

            base.OnPaint(e);
        }
    }
}
