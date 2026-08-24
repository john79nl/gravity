using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Gravity.UI
{
    /// <summary>
    /// The type/role of a chat row entry, controlling icon, accent colour, and style.
    /// </summary>
    public enum ChatRowType
    {
        User,    // 🧑 blue accent
        Agent,   // 🤖 green accent
        System,  // ⚙️  muted/secondary
        Step,    // 🔧 purple accent
        Log,     // 📋 amber accent
    }

    /// <summary>
    /// A unified, compact chat row control.
    ///
    /// Layout (28 px tall when collapsed):
    ///   [icon] [sender · timestamp]  [── preview text ──────────]  [🔍 View]
    ///   ↕ expandable content box (hidden until View clicked)
    /// </summary>
    public class ChatRow : Panel
    {
        // ── Layout constants ────────────────────────────────────────────────
        private const int RowHeight    = 28;
        private const int IconWidth    = 24;
        private const int SenderWidth  = 110;  // sender + time label
        private const int ViewBtnWidth = 66;
        private const int PreviewChars = 85;

        // ── Child controls ──────────────────────────────────────────────────
        private readonly Label       _iconLabel;
        private readonly Label       _senderLabel;
        private readonly Label       _previewLabel;
        private readonly Label       _viewBtn;
        private readonly RichTextBox _contentBox;

        // ── State ───────────────────────────────────────────────────────────
        private bool _expanded     = false;
        private bool _sizingInProg = false;
        private bool _hasContent   = false;

        // Token batching (for streaming step rows)
        private readonly StringBuilder _pending = new();
        private readonly System.Windows.Forms.Timer _flushTimer;

        // ── Public ──────────────────────────────────────────────────────────
        public ChatRowType RowType { get; }

        // ────────────────────────────────────────────────────────────────────
        // Constructor
        // ────────────────────────────────────────────────────────────────────
        public ChatRow(ChatRowType type, string sender, string content = "")
        {
            RowType = type;

            bool isDark = MaterialSkin.MaterialSkinManager.Instance?.Theme ==
                          MaterialSkin.MaterialSkinManager.Themes.DARK;

            // ── Palette ────────────────────────────────────────────────────
            Color fgPrimary, fgSecondary, fgAccent, fgViewBtn, bgViewBtn, contentBg;

            if (isDark)
            {
                fgPrimary   = Color.FromArgb(218, 222, 255);
                fgSecondary = Color.FromArgb(130, 135, 170);
                fgAccent    = type switch
                {
                    ChatRowType.User   => Color.FromArgb(120, 160, 255),
                    ChatRowType.Agent  => Color.FromArgb(140, 220, 160),
                    ChatRowType.Step   => Color.FromArgb(190, 170, 255),
                    ChatRowType.Log    => Color.FromArgb(200, 160, 100),
                    _                  => Color.FromArgb(130, 135, 170),
                };
                fgViewBtn   = Color.FromArgb(170, 190, 240);
                bgViewBtn   = Color.FromArgb(30, 40, 80);
                contentBg   = Color.FromArgb(18, 22, 54);
            }
            else
            {
                fgPrimary   = Color.FromArgb(30, 30, 30);
                fgSecondary = Color.FromArgb(100, 100, 110);
                fgAccent    = type switch
                {
                    ChatRowType.User   => Color.FromArgb(40, 80, 200),
                    ChatRowType.Agent  => Color.FromArgb(30, 130, 80),
                    ChatRowType.Step   => Color.FromArgb(100, 60, 200),
                    ChatRowType.Log    => Color.FromArgb(160, 100, 20),
                    _                  => Color.FromArgb(80, 80, 100),
                };
                fgViewBtn   = Color.FromArgb(40, 80, 180);
                bgViewBtn   = Color.FromArgb(230, 235, 250);
                contentBg   = Color.FromArgb(248, 249, 255);
            }

            string icon = type switch
            {
                ChatRowType.User   => "🧑",
                ChatRowType.Agent  => "🤖",
                ChatRowType.Step   => "🔧",
                ChatRowType.Log    => "📋",
                _                  => "⚙️",
            };

            // ── Panel self ─────────────────────────────────────────────────
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.AllPaintingInWmPaint  |
                          ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
            this.Height    = RowHeight;
            this.Margin    = new Padding(0, 1, 0, 1);
            this.Padding   = new Padding(0);

            // ── Icon ───────────────────────────────────────────────────────
            _iconLabel = new Label
            {
                Text      = icon,
                Font      = new Font("Segoe UI Emoji", 10F),
                ForeColor = fgAccent,
                BackColor = Color.Transparent,
                AutoSize  = false,
                Width     = IconWidth,
                Height    = RowHeight,
                TextAlign = ContentAlignment.MiddleCenter,
                Location  = new Point(4, 0),
            };

            // ── Sender · time ──────────────────────────────────────────────
            string time = DateTime.Now.ToString("HH:mm");
            _senderLabel = new Label
            {
                Text      = $"{sender} · {time}",
                Font      = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = fgAccent,
                BackColor = Color.Transparent,
                AutoSize  = false,
                Width     = SenderWidth,
                Height    = RowHeight,
                TextAlign = ContentAlignment.MiddleLeft,
                Location  = new Point(IconWidth + 6, 0),
            };

            // ── Preview text (truncated inline) ───────────────────────────
            _previewLabel = new Label
            {
                Text      = "",
                Font      = new Font("Segoe UI", 9F),
                ForeColor = fgPrimary,
                BackColor = Color.Transparent,
                AutoSize  = false,
                Height    = RowHeight,
                TextAlign = ContentAlignment.MiddleLeft,
                Location  = new Point(IconWidth + SenderWidth + 10, 0),
            };

            // ── [🔍 View] button ───────────────────────────────────────────
            _viewBtn = new Label
            {
                Text      = "🔍 View",
                Font      = new Font("Segoe UI", 8.5F),
                ForeColor = fgViewBtn,
                BackColor = bgViewBtn,
                AutoSize  = false,
                Width     = ViewBtnWidth,
                Height    = 20,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor    = Cursors.Hand,
                Visible   = false,
                Padding   = new Padding(2),
            };
            _viewBtn.Click      += (_, __) => ToggleExpand();
            _viewBtn.MouseEnter += (_, __) => _viewBtn.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            _viewBtn.MouseLeave += (_, __) => _viewBtn.Font = new Font("Segoe UI", 8.5F);

            // ── Expandable content box ─────────────────────────────────────
            _contentBox = new RichTextBox
            {
                ReadOnly         = true,
                BackColor        = contentBg,
                ForeColor        = fgPrimary,
                BorderStyle      = BorderStyle.None,
                Font             = new Font("Consolas", 9F),
                ScrollBars       = RichTextBoxScrollBars.None,
                WordWrap         = true,
                DetectUrls       = false,
                ShortcutsEnabled = true,
                Visible          = false,
                Height           = 0,
                Location         = new Point(IconWidth + SenderWidth + 10, RowHeight + 4),
            };
            _contentBox.ContentsResized += (s, e) =>
            {
                if (_expanded) ResizeContent();
            };

            // ── Flush timer for streaming ──────────────────────────────────
            _flushTimer = new System.Windows.Forms.Timer { Interval = 80 };
            _flushTimer.Tick += FlushPending;

            // ── Add controls ───────────────────────────────────────────────
            this.Controls.Add(_iconLabel);
            this.Controls.Add(_senderLabel);
            this.Controls.Add(_previewLabel);
            this.Controls.Add(_viewBtn);
            this.Controls.Add(_contentBox);

            _contentBox.MouseClick += ContentBox_MouseClick;

            // ── Initial content ────────────────────────────────────────────
            if (!string.IsNullOrEmpty(content))
                SetContent(content);

            LayoutRow();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _flushTimer?.Dispose();
            base.Dispose(disposing);
        }

        // ────────────────────────────────────────────────────────────────────
        // Public API
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Thread-safe: appends streaming text; flushed via 80ms timer.</summary>
        public void AppendContent(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(AppendContent), text); return; }
            _pending.Append(text);
            if (!_flushTimer.Enabled) _flushTimer.Start();
        }

        /// <summary>Thread-safe: replaces full content.</summary>
        public void SetContent(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(SetContent), text); return; }

            string plain = RowType == ChatRowType.User ? text
                         : Gravity.Core.MarkdownStripper.ToPlainText(text);

            _contentBox.Text   = plain;
            _previewLabel.Text = BuildPreview(plain);
            _hasContent = !string.IsNullOrWhiteSpace(plain);
            UpdateViewButtonVisibility();

            if (_expanded) ResizeContent();
        }

        /// <summary>Updates the preview label (e.g. "Reasoning..." → "📖 Reading Form1.cs").</summary>
        public void UpdateLabel(string newLabel)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(UpdateLabel), newLabel); return; }
            _previewLabel.Text = BuildPreview(newLabel);
            if (!_hasContent)
            {
                _contentBox.Text = newLabel;
                _hasContent = !string.IsNullOrWhiteSpace(newLabel);
                UpdateViewButtonVisibility();
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Resize / layout
        // ────────────────────────────────────────────────────────────────────

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutRow();
        }

        private void LayoutRow()
        {
            if (_sizingInProg || _iconLabel == null || _senderLabel == null ||
                _previewLabel == null || _viewBtn == null || _contentBox == null) return;

            _sizingInProg = true;
            try
            {
                int w = this.Width;
                _viewBtn.Location  = new Point(Math.Max(10, w - ViewBtnWidth - 8), (RowHeight - 20) / 2);

                int previewLeft  = IconWidth + SenderWidth + 10;
                int previewRight = _viewBtn.Visible ? (_viewBtn.Left - 8) : (w - 8);
                _previewLabel.Location = new Point(previewLeft, 0);
                _previewLabel.Width    = Math.Max(20, previewRight - previewLeft);

                _contentBox.Location = new Point(previewLeft, RowHeight + 4);
                _contentBox.Width    = Math.Max(100, w - previewLeft - 8);

                if (_expanded) ResizeContent();
            }
            finally { _sizingInProg = false; }
        }

        private void ToggleExpand()
        {
            _expanded           = !_expanded;
            _viewBtn.Text       = _expanded ? "▲ Hide" : "🔍 View";
            _contentBox.Visible = _expanded;

            this.SuspendLayout();
            try
            {
                if (_expanded)
                    ResizeContent();
                else
                {
                    _contentBox.Height = 0;
                    this.Height        = RowHeight;
                }
            }
            finally { this.ResumeLayout(true); }

            this.Parent?.PerformLayout();
        }

        private void ResizeContent()
        {
            if (!_expanded || _contentBox == null) return;

            int lineCount = _contentBox.Lines.Length > 0 ? _contentBox.Lines.Length : 1;
            int contentH = Math.Max(40, (lineCount * _contentBox.Font.Height) + 16);

            if (_contentBox.Height != contentH) _contentBox.Height = contentH;

            int total = RowHeight + 4 + contentH + 8;
            if (this.Height != total) this.Height = total;
        }

        private void UpdateViewButtonVisibility()
        {
            if (_contentBox == null || _viewBtn == null) return;
            bool show = _hasContent && NeedsExpand(_contentBox.Text);
            if (!show && _expanded) ToggleExpand();
            _viewBtn.Visible = show;
            LayoutRow();
        }

        // ────────────────────────────────────────────────────────────────────
        // Streaming flush
        // ────────────────────────────────────────────────────────────────────

        private void FlushPending(object? sender, EventArgs e)
        {
            _flushTimer.Stop();
            if (_pending.Length == 0) return;

            string text  = _pending.ToString();
            _pending.Clear();

            string plain = RowType == ChatRowType.User ? text
                         : Gravity.Core.MarkdownStripper.ToPlainText(text);

            _contentBox.AppendText(plain);
            _hasContent = true;
            _previewLabel.Text = BuildPreview(_contentBox.Text);
            UpdateViewButtonVisibility();
            if (_expanded) ResizeContent();
        }

        // ────────────────────────────────────────────────────────────────────
        // Link detection
        // ────────────────────────────────────────────────────────────────────

        private static readonly System.Text.RegularExpressions.Regex _linkRegex =
            new(@"\[(?<label>[^\]\r\n]+)\]\((?:file:///)?(?<path>[A-Za-z]:[^)\r\n]+)\)|(?<path>[A-Za-z]:[\\/][a-zA-Z0-9_\-\./\\ \(\)]+(?:\.[a-zA-Z0-9]+)?)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private void ContentBox_MouseClick(object? sender, MouseEventArgs e)
        {
            if (sender is not RichTextBox tb) return;
            int ci = tb.GetCharIndexFromPosition(e.Location);
            var matches = _linkRegex.Matches(tb.Text);
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (ci >= m.Index && ci < m.Index + m.Length)
                {
                    string fp = m.Groups["path"].Value.Replace("file:///", "").Replace("/", "\\");
                    var form = this.FindForm() as Form1;
                    if (form != null) _ = form.InvokeOpenFileInTabAsync(fp);
                    return;
                }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────────

        private static bool NeedsExpand(string text) =>
            !string.IsNullOrEmpty(text) && (text.Length > PreviewChars || text.Contains('\n'));

        private static string BuildPreview(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string first = text.Split('\n')[0].Trim();
            return first.Length > PreviewChars ? first[..PreviewChars] + "…" : first;
        }
    }
}
