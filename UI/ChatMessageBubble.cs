using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Gravity.Core;

namespace Gravity.UI
{
    /// <summary>
    /// A self-contained chat message control.
    /// All message types (User, Agent, System) render with the same flat layout:
    ///   [avatar] Sender · HH:mm
    ///   ─────────────────────────────────
    ///   Full message text (inline, auto-sized, no collapse)
    ///
    /// The old "Show reasoning" toggle panel has been removed.
    /// </summary>
    public class ChatMessageBubble : Panel
    {
        private readonly Label   _headerLabel;
        private readonly RichTextBox _contentBox;
        private readonly List<PictureBox> _imageBoxes = new List<PictureBox>();

        private bool _isUser;
        private bool _sizingInProgress;

        private static readonly Regex LinkRegex = new Regex(
            @"\[(?<label>[^\]\r\n]+)\]\((?:file:\/\/\/)?((?<path>[A-Za-z]:[^)\r\n]+))\)|(?<!(\(|file:\/\/\/))(?<path>[A-Za-z]:[\\/][a-zA-Z0-9_\-\.\/\\ \(\)]+)(?:\.[a-zA-Z0-9]+)?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MarkdownImageRegex = new Regex(
            @"!\[(?<alt>[^\]]*)\]\((?<src>[^)]+)\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HttpClient ImageHttpClient = new HttpClient();

        public string Sender { get; }
        public string Time   { get; }

        public ChatMessageBubble(string sender, string time, string content, bool isUser, ImageAttachment? imageAttachment = null)
        {
            Sender  = sender;
            Time    = time;
            _isUser = isUser;

            bool isDark = MaterialSkin.MaterialSkinManager.Instance?.Theme == MaterialSkin.MaterialSkinManager.Themes.DARK;

            // ── Colours ──────────────────────────────────────────────────────
            Color bg, fg, headerFg, contentBg;

            if (isDark)
            {
                bg        = Color.Transparent;
                fg        = Color.FromArgb(226, 232, 255);
                headerFg  = isUser
                    ? Color.FromArgb(0, 242, 254)   // Electric Neon Cyan for user
                    : Color.FromArgb(0, 242, 254);  // Electric Neon Cyan for agent
                contentBg = isUser
                    ? Color.FromArgb(22, 37, 72)    // Translucent dark cyan-blue card (#162548)
                    : Color.FromArgb(13, 20, 48);   // Dark navy surface (#0d1430)
            }
            else
            {
                bg        = Color.Transparent;
                fg        = Color.FromArgb(30, 30, 30);
                headerFg  = isUser
                    ? Color.FromArgb(40, 80, 200)
                    : Color.FromArgb(30, 130, 80);
                contentBg = isUser
                    ? Color.FromArgb(240, 245, 255)
                    : Color.FromArgb(250, 252, 255);
            }

            // ── Panel self ───────────────────────────────────────────────────
            this.BackColor    = bg;
            this.Padding      = new Padding(0, 4, 0, 8);
            this.Margin       = new Padding(0, 2, 0, 2);
            this.SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint  |
                ControlStyles.SupportsTransparentBackColor, true);
            this.DoubleBuffered = true;

            // ── Avatar + sender label ────────────────────────────────────────
            string avatar = isUser ? "🧑" : sender switch
            {
                "Agent"       => "🤖",
                "System"      => "⚙️",
                "Orchestrator"=> "🤖",
                _             => "💬"
            };

            _headerLabel = new Label
            {
                Text      = $"{avatar}  {sender}  ·  {time}",
                Font      = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = headerFg,
                BackColor = Color.Transparent,
                AutoSize  = false,
                Height    = 22,
            };
            this.Controls.Add(_headerLabel);

            // ── Content text box ─────────────────────────────────────────────
            string displayText = isUser
                ? content
                : Gravity.Core.MarkdownStripper.ToPlainText(content);

            _contentBox = new RichTextBox
            {
                Text          = displayText,
                ReadOnly      = true,
                BorderStyle   = BorderStyle.None,
                BackColor     = contentBg,
                ForeColor     = fg,
                Font          = new Font("Segoe UI", 9.5F),
                WordWrap      = true,
                ScrollBars    = RichTextBoxScrollBars.None,
                DetectUrls    = false,
                ShortcutsEnabled = true,
            };
            _contentBox.ContentsResized += (s, e) =>
            {
                int h = e.NewRectangle.Height + 6;
                if (_contentBox.Height != h)
                {
                    _contentBox.Height = h;
                    AutoSizeBubble();
                }
            };
            this.Controls.Add(_contentBox);

            // Wire link-clicking
            if (!string.IsNullOrEmpty(content))
                SetupLinks(_contentBox, content);

            // Process attached image
            if (imageAttachment != null)
                AddImageFromAttachment(imageAttachment);

            // Extract images from content
            if (!string.IsNullOrEmpty(content))
                ProcessImageLinks(content);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────

        public void AppendContent(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(AppendContent), text); return; }
            string plain = _isUser ? text : Gravity.Core.MarkdownStripper.ToPlainText(text);
            _contentBox.AppendText(plain);
            SetupLinks(_contentBox, _contentBox.Text);
            AutoSizeBubble();
        }

        /// <summary>
        /// Previously used for reasoning details — now forwards to AppendContent
        /// so callers that still call SetDetails/AppendDetails don't break.
        /// </summary>
        public void SetDetails(string details)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(SetDetails), details); return; }
            // Details are appended inline after main content
            if (!string.IsNullOrWhiteSpace(details))
            {
                if (!string.IsNullOrWhiteSpace(_contentBox.Text))
                    _contentBox.AppendText("\n");
                _contentBox.AppendText(Gravity.Core.MarkdownStripper.ToPlainText(details));
            }
            AutoSizeBubble();
        }

        public void AppendDetails(string detailText)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(AppendDetails), detailText); return; }
            AppendContent(detailText);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Layout
        // ─────────────────────────────────────────────────────────────────────

        public void AutoSizeBubble()
        {
            if (_sizingInProgress || _headerLabel == null || _contentBox == null) return;
            _sizingInProgress = true;
            try
            {
                int padL   = this.Padding.Left;
                int padT   = this.Padding.Top;
                int padR   = this.Padding.Right;
                int padB   = this.Padding.Bottom;
                int innerW = Math.Max(this.Width - padL - padR, 100);
                int y      = padT;

                // Header
                _headerLabel.Location = new Point(padL, y);
                _headerLabel.Width    = innerW;
                y += _headerLabel.Height + 4;

                // Content box
                _contentBox.Location = new Point(padL, y);
                _contentBox.Width    = innerW;
                // Height is driven by ContentsResized; measure now for initial pass
                if (_contentBox.Height < 20)
                {
                    var sz = TextRenderer.MeasureText(
                        string.IsNullOrEmpty(_contentBox.Text) ? " " : _contentBox.Text,
                        _contentBox.Font,
                        new Size(innerW, 0),
                        TextFormatFlags.WordBreak);
                    _contentBox.Height = Math.Max(20, sz.Height + 8);
                }
                y += _contentBox.Height + 4;

                // Images
                foreach (var pb in _imageBoxes)
                {
                    int imgH = 150;
                    if (pb.Image != null && pb.Image.Width > 0)
                    {
                        double aspect = pb.Image.Height / (double)pb.Image.Width;
                        imgH = Math.Min(350, Math.Max(60, (int)(innerW * aspect)));
                    }
                    pb.Location = new Point(padL, y);
                    pb.Width    = innerW;
                    pb.Height   = imgH;
                    y += imgH + 8;
                }

                this.Height = y + padB;
            }
            finally
            {
                _sizingInProgress = false;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            AutoSizeBubble();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Image helpers
        // ─────────────────────────────────────────────────────────────────────

        private void AddImageFromAttachment(ImageAttachment attachment)
        {
            try
            {
                Image? img = null;
                if (!string.IsNullOrEmpty(attachment.FilePath) && File.Exists(attachment.FilePath))
                    img = Image.FromFile(attachment.FilePath);
                else if (!string.IsNullOrEmpty(attachment.Base64Data))
                {
                    byte[] bytes = Convert.FromBase64String(attachment.Base64Data);
                    using var ms = new MemoryStream(bytes);
                    img = Image.FromStream(ms);
                }
                if (img != null) AddImageControl(img, attachment.FilePath, "Attached Image");
            }
            catch { }
        }

        private void ProcessImageLinks(string rawContent)
        {
            var matches = MarkdownImageRegex.Matches(rawContent);
            foreach (Match match in matches)
            {
                string alt = match.Groups["alt"].Value;
                string src = match.Groups["src"].Value.Trim();

                if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    var base64Idx = src.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
                    if (base64Idx >= 0)
                    {
                        try
                        {
                            var b64   = src.Substring(base64Idx + 7);
                            byte[] bytes = Convert.FromBase64String(b64);
                            using var ms = new MemoryStream(bytes);
                            var img = Image.FromStream(ms);
                            AddImageControl(img, null, alt);
                        }
                        catch { }
                    }
                }
                else if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    AddImageFromUrlAsync(src, alt);
                }
                else
                {
                    string path = src.Replace("file:///", "").Replace("/", "\\");
                    if (File.Exists(path))
                    {
                        try { AddImageControl(Image.FromFile(path), path, alt); } catch { }
                    }
                }
            }
        }

        private void AddImageControl(Image? img, string? sourcePathOrUrl, string? altText)
        {
            var pb = new PictureBox
            {
                Image       = img,
                SizeMode    = PictureBoxSizeMode.Zoom,
                Cursor      = Cursors.Hand,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor   = Color.FromArgb(20, 20, 20)
            };
            var tt = new ToolTip();
            tt.SetToolTip(pb, !string.IsNullOrEmpty(altText) ? altText : (sourcePathOrUrl ?? "Click to view image"));
            pb.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(sourcePathOrUrl))
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sourcePathOrUrl) { UseShellExecute = true }); }
                    catch { }
                }
            };
            _imageBoxes.Add(pb);
            this.Controls.Add(pb);
        }

        private async void AddImageFromUrlAsync(string url, string altText)
        {
            var pb = new PictureBox
            {
                SizeMode    = PictureBoxSizeMode.Zoom,
                Cursor      = Cursors.Hand,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor   = Color.FromArgb(20, 20, 20)
            };
            var tt = new ToolTip();
            tt.SetToolTip(pb, !string.IsNullOrEmpty(altText) ? altText : url);
            pb.Click += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { }
            };
            _imageBoxes.Add(pb);
            this.Controls.Add(pb);
            AutoSizeBubble();

            try
            {
                byte[] bytes = await ImageHttpClient.GetByteArrayAsync(url);
                using var ms = new MemoryStream(bytes);
                var img = Image.FromStream(ms);
                if (this.IsHandleCreated)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        pb.Image = img;
                        AutoSizeBubble();
                        this.Parent?.PerformLayout();
                    }));
                }
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Link detection
        // ─────────────────────────────────────────────────────────────────────

        private void SetupLinks(RichTextBox textBox, string text)
        {
            var matches = LinkRegex.Matches(text);
            if (matches.Count == 0) return;

            var filePaths = new List<(int index, int length, string path)>();
            foreach (Match match in matches)
            {
                string fp = match.Groups["path"].Value.Replace("file:///", "").Replace("/", "\\");
                filePaths.Add((match.Index, match.Length, fp));
            }

            textBox.MouseClick += (s, e) =>
            {
                int ci = textBox.GetCharIndexFromPosition(e.Location);
                foreach (var fp in filePaths)
                {
                    if (ci >= fp.index && ci < fp.index + fp.length)
                    {
                        var form = this.FindForm() as Form1;
                        if (form != null) _ = form.InvokeOpenFileInTabAsync(fp.path);
                        return;
                    }
                }
            };

            textBox.MouseMove += (s, e) =>
            {
                int ci = textBox.GetCharIndexFromPosition(e.Location);
                bool over = false;
                foreach (var fp in filePaths)
                    if (ci >= fp.index && ci < fp.index + fp.length) { over = true; break; }
                textBox.Cursor = over ? Cursors.Hand : Cursors.IBeam;
            };
        }
    }
}
