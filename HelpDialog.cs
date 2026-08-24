using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Gravity
{
    /// <summary>
    /// Premium Help window — master/detail two-column layout.
    /// Left: topic list with yellow highlight stripe.
    /// Right: scrollable detail cards with rounded corners.
    /// </summary>
    public sealed class HelpDialog : Form
    {
        // ── Palette ───────────────────────────────────────────────────────────
        private static readonly Color BgDeep    = Color.FromArgb(5,   7,  28);
        private static readonly Color BgPanel   = Color.FromArgb(10,  15,  45);
        private static readonly Color BgHeader  = Color.FromArgb(8,   12,  40);
        private static readonly Color BgItem    = Color.FromArgb(14,  20,  55);
        private static readonly Color BgItemHov = Color.FromArgb(22,  32,  80);
        private static readonly Color BgItemSel = Color.FromArgb(28,  42, 100);
        private static readonly Color FgMain    = Color.FromArgb(210, 220, 255);
        private static readonly Color FgSoft    = Color.FromArgb(140, 160, 220);
        private static readonly Color Accent    = Color.FromArgb(245, 200,  50);

        // ── Topic data ────────────────────────────────────────────────────────
        private readonly List<HelpTopic> _topics = new()
        {
            new("🚀  Getting Started", "Welcome to Gravity", new[]
            {
                ("Open a Workspace",
                 "Click 📁 Open Folder in the top bar to choose the root folder of the project you want to work with. "
                 + "Gravity will scan your workspace and populate the Solution Explorer on the left with all recognised files."),

                ("Start a Conversation",
                 "Type any question, request, or instruction into the input bar at the bottom of the screen and press Enter "
                 + "or the Send ▶ button. Gravity will begin working on your request immediately."),

                ("Switching Themes",
                 "Use the 🌙 / ☀️ button in the left activity bar to toggle between dark and light mode at any time."),
            }),

            new("💬  Chat & Tasks", "Working with AI Assistance", new[]
            {
                ("Describing Tasks",
                 "Be as specific or as high-level as you like. You can say \"explain this file\", \"add error handling\", "
                 + "or \"refactor the data layer\" and Gravity will interpret your intent and act accordingly."),

                ("Multi-step Work",
                 "Complex requests are automatically broken into steps. You can follow progress in real time "
                 + "as each stage is completed and logged in the chat area."),

                ("Stopping a Task",
                 "Click the Stop ◼ button at any time to immediately cancel the current operation. "
                 + "Partial results already produced are preserved in the chat."),

                ("Agent Cards",
                 "Each task you start spawns an Agent card in the top agent bar. "
                 + "Click a card to switch to that agent's conversation and see its full history."),
            }),

            new("📁  Solution Explorer", "Navigating Your Project", new[]
            {
                ("Opening Files",
                 "Double-click any file in the Solution Explorer to open it in the editor. "
                 + "Multiple files can be open at once — each appears as a tab above the editor area."),

                ("Git Indicators",
                 "If your workspace is a Git repository, each file shows a status icon: "
                 + "📝 modified · ➕ added · ❓ untracked · ❌ deleted."),

                ("Live Refresh",
                 "The explorer updates automatically when files are created, deleted or renamed — "
                 + "no manual refresh needed."),

                ("Collapsing the Panel",
                 "Click the 📁 icon in the activity bar to toggle the Solution Explorer open or closed "
                 + "and give more room to the editor or chat."),
            }),

            new("✏️  The Editor", "Editing Files", new[]
            {
                ("Syntax Highlighting",
                 "The built-in editor provides full syntax highlighting for C# and common web formats. "
                 + "Colours automatically adjust when you switch themes."),

                ("Breadcrumb Trail",
                 "A breadcrumb bar above the editor shows the path of the currently open file, "
                 + "making it easy to orient yourself inside deep directory trees."),

                ("Status Bar",
                 "The status bar at the very bottom shows the current cursor position (line and column), "
                 + "file encoding, and language mode."),

                ("Closing Tabs",
                 "Middle-click any editor tab to close it. Your chat tab cannot be closed "
                 + "and is always accessible."),
            }),

            new("⚙️  Settings", "Customising Gravity", new[]
            {
                ("Opening Settings",
                 "Click ⚙ Settings in the ribbon bar or the ⚙ icon in the activity bar to open the Settings panel."),

                ("Development Mode",
                 "Use the Development Mode selector in the agent bar to switch between different working styles. "
                 + "Each mode shapes how Gravity approaches and structures its responses."),

                ("Model Selection",
                 "You can choose which AI model powers Gravity from the Settings panel. "
                 + "Different models offer different trade-offs between speed and depth of reasoning."),
            }),

            new("💡  Tips & Shortcuts", "Getting the Most from Gravity", new[]
            {
                ("Enter to Send",
                 "Press Enter to send a message. To insert a line break inside your message, "
                 + "press Shift+Enter instead."),

                ("Reference Open Files",
                 "When you have a file open in the editor, Gravity is aware of its path and can "
                 + "include it in the context of your request automatically."),

                ("Be Conversational",
                 "You don't need to use special syntax or commands. Talk to Gravity the way you would talk "
                 + "to a colleague — ask follow-up questions, request revisions, or change direction freely."),

                ("Use the Artifacts Panel",
                 "When Gravity creates a structured output (document, report, plan…), it will appear as an "
                 + "Artifact card in the chat. Click it to open a dedicated viewer with formatting and export options."),

                ("Approval Prompts",
                 "Certain operations that affect your project will pause and ask for your confirmation before proceeding. "
                 + "Review the proposed action and click Allow or Deny as you see fit."),
            }),

            new("🌐  GitHub Repository", "Open Source & Community", new[]
            {
                ("Source Code & Repository",
                 "Gravity AI is an open-source project hosted on GitHub. "
                 + "View the full source code, report issues, or contribute to development:\n\n"
                 + "https://github.com/john79nl/gravity"),

                ("Built by Giovanni D'Arienzo",
                 "Created by solo developer Giovanni D'Arienzo, Gravity is built on the vision of unifying agentic software engineering into one ultimate open-source tool."),

                ("Star & Contribute",
                 "Check out the GitHub repository to star the project, submit pull requests, or join discussions with the community."),
            }),
        };

        // ── State ─────────────────────────────────────────────────────────────
        private readonly Panel             _detailPanel;
        private readonly FlowLayoutPanel   _topicsFlow;   // direct reference — fixes highlight bug
        private int                        _selectedIndex = -1;

        // ─────────────────────────────────────────────────────────────────────
        public HelpDialog()
        {
            Text            = "Gravity — Help";
            Size            = new Size(1000, 700);
            MinimumSize     = new Size(720, 500);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            BackColor       = BgDeep;
            ForeColor       = FgMain;
            Font            = new Font("Segoe UI", 10.5f);
            DoubleBuffered  = true;

            // ── Header ────────────────────────────────────────────────────────
            var header = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = BgHeader };
            header.Paint += (_, e) =>
            {
                using var pen = new Pen(Color.FromArgb(90, Accent), 2f);
                e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            };
            var lblTitle = new Label
            {
                Text      = "✦  Help & Guide",
                Dock      = DockStyle.Left,
                Width     = 320,
                Font      = new Font("Segoe UI Light", 20f),
                ForeColor = FgMain,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(20, 0, 0, 0),
            };
            var lblSub = new Label
            {
                Text      = "Everything you need to know about Gravity",
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 9.5f),
                ForeColor = FgSoft,
                TextAlign = ContentAlignment.MiddleRight,
                Padding   = new Padding(0, 0, 20, 0),
            };
            header.Controls.Add(lblSub);
            header.Controls.Add(lblTitle);

            // ── Footer ────────────────────────────────────────────────────────
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = BgHeader };
            footer.Paint += (_, e) =>
            {
                using var pen = new Pen(Color.FromArgb(60, Accent), 1f);
                e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };
            var btnGitHub = new Button
            {
                Text      = "🌐 GitHub",
                Dock      = DockStyle.Right,
                Width     = 110,
                Height    = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(20, 35, 80),
                ForeColor = Accent,
                Font      = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor    = Cursors.Hand,
                Margin    = new Padding(0, 8, 12, 8),
            };
            btnGitHub.FlatAppearance.BorderColor        = Color.FromArgb(245, 200, 50);
            btnGitHub.FlatAppearance.BorderSize         = 1;
            btnGitHub.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 60, 130);
            btnGitHub.Click += (_, _) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/john79nl/gravity") { UseShellExecute = true });
                }
                catch { }
            };

            var btnClose = new Button
            {
                Text      = "Close",
                Dock      = DockStyle.Right,
                Width     = 110,
                Height    = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(28, 42, 100),
                ForeColor = FgMain,
                Font      = new Font("Segoe UI", 10f),
                Cursor    = Cursors.Hand,
                Margin    = new Padding(0, 8, 16, 8),
            };
            btnClose.FlatAppearance.BorderColor           = Color.FromArgb(60, 80, 160);
            btnClose.FlatAppearance.BorderSize            = 1;
            btnClose.FlatAppearance.MouseOverBackColor    = Color.FromArgb(40, 60, 130);
            btnClose.Click += (_, _) => Close();
            footer.Controls.Add(btnClose);
            footer.Controls.Add(btnGitHub);

            // ── SplitContainer ────────────────────────────────────────────────
            var splitter = new SplitContainer
            {
                Dock            = DockStyle.Fill,
                SplitterWidth   = 2,
                FixedPanel      = FixedPanel.Panel1,
                BackColor       = Color.FromArgb(25, 40, 100),
            };
            splitter.Panel1.BackColor = BgPanel;
            splitter.Panel2.BackColor = BgDeep;

            // ── Master panel (left) ───────────────────────────────────────────
            var masterPanel = new Panel { Dock = DockStyle.Fill, BackColor = BgPanel };

            var masterHeader = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = BgHeader };
            var lblTopics = new Label
            {
                Text      = "TOPICS",
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = FgSoft,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(18, 0, 0, 0),
            };
            masterHeader.Controls.Add(lblTopics);

            // Store the flow as a field so SelectTopic can reach it directly
            _topicsFlow = new FlowLayoutPanel
            {
                Dock          = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents  = false,
                BackColor     = BgPanel,
                Padding       = new Padding(0, 6, 0, 6),
                AutoScroll    = true,
            };

            for (int i = 0; i < _topics.Count; i++)
                _topicsFlow.Controls.Add(BuildTopicItem(i));

            masterPanel.Controls.Add(_topicsFlow);
            masterPanel.Controls.Add(masterHeader);   // added last → docks to Top correctly

            // ── Detail panel (right) ──────────────────────────────────────────
            // AutoScroll = true on the outer panel; inner stack panel holds the cards.
            _detailPanel = new Panel
            {
                Dock       = DockStyle.Fill,
                BackColor  = BgDeep,
                AutoScroll = true,
                Padding    = new Padding(30, 26, 30, 26),
            };

            splitter.Panel1.Controls.Add(masterPanel);
            splitter.Panel2.Controls.Add(_detailPanel);

            Controls.Add(splitter);
            // Set Splitter properties after adding to Controls so the container has inherited the form's valid Size
            splitter.Panel1MinSize    = 200;
            splitter.Panel2MinSize    = 350;
            splitter.SplitterDistance = 265;

            Controls.Add(footer);
            Controls.Add(header);

            // Select first topic after the form is assembled
            SelectTopic(0);
        }

        // ── Build one topic row ───────────────────────────────────────────────
        private Panel BuildTopicItem(int index)
        {
            var topic = _topics[index];

            var indicator = new Panel
            {
                Dock      = DockStyle.Left,
                Width     = 4,
                BackColor = Color.Transparent,
            };

            var lbl = new Label
            {
                Text      = topic.Title,
                Dock      = DockStyle.Fill,
                ForeColor = FgMain,
                Font      = new Font("Segoe UI", 10.5f),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(14, 0, 0, 0),
            };

            var item = new Panel
            {
                Width     = 261,
                Height    = 48,
                Margin    = new Padding(0),
                BackColor = BgItem,
                Cursor    = Cursors.Hand,
                Tag       = index,
            };
            item.Controls.Add(lbl);
            item.Controls.Add(indicator);

            void Select() => SelectTopic(index);
            void Hover(bool on)
            {
                if (index != _selectedIndex)
                    item.BackColor = on ? BgItemHov : BgItem;
            }

            item.Click      += (_, _) => Select();
            lbl.Click       += (_, _) => Select();
            item.MouseEnter += (_, _) => Hover(true);
            item.MouseLeave += (_, _) => Hover(false);
            lbl.MouseEnter  += (_, _) => Hover(true);
            lbl.MouseLeave  += (_, _) => Hover(false);

            return item;
        }

        // ── Select + highlight a topic ────────────────────────────────────────
        private void SelectTopic(int index)
        {
            _selectedIndex = index;

            // Update highlight on all topic rows via the stored field reference
            foreach (Control ctrl in _topicsFlow.Controls)
            {
                if (ctrl is not Panel item) continue;
                bool sel = item.Tag is int i && i == index;

                item.BackColor = sel ? BgItemSel : BgItem;

                // indicator stripe = Controls[1] (added second, so index 1 in the Controls collection)
                if (item.Controls.Count >= 2 && item.Controls[1] is Panel ind)
                    ind.BackColor = sel ? Accent : Color.Transparent;
            }

            BuildDetailView(_topics[index]);
        }

        // ── Render detail cards ───────────────────────────────────────────────
        private void BuildDetailView(HelpTopic topic)
        {
            _detailPanel.SuspendLayout();
            _detailPanel.Controls.Clear();

            // A Panel that will grow tall enough to hold all cards.
            // It is NOT docked — we set its width on resize and position at (0,0).
            var stack = new Panel
            {
                Location  = new Point(0, 0),
                BackColor = Color.Transparent,
            };

            int cardWidth = Math.Max(300, _detailPanel.ClientSize.Width
                                          - _detailPanel.Padding.Horizontal - 20);

            int y = 0;

            // ── Heading ──────────────────────────────────────────────────────
            var heading = new Label
            {
                Text      = topic.Heading,
                Location  = new Point(0, y),
                Size      = new Size(cardWidth, 50),
                Font      = new Font("Segoe UI Light", 22f),
                ForeColor = FgMain,
                BackColor = Color.Transparent,
            };
            stack.Controls.Add(heading);
            y += 50;

            // Yellow underline
            var sep = new Panel
            {
                Location  = new Point(0, y),
                Size      = new Size(52, 3),
                BackColor = Accent,
            };
            stack.Controls.Add(sep);
            y += 22;

            // ── Section cards ─────────────────────────────────────────────────
            foreach (var (subTitle, body) in topic.Sections)
            {
                var card = BuildSectionCard(subTitle, body, cardWidth);
                card.Location = new Point(0, y);
                stack.Controls.Add(card);
                y += card.Height + 14;
            }

            stack.Size = new Size(cardWidth, y + 10);

            _detailPanel.Controls.Add(stack);

            // Re-anchor stack width when the panel resizes
            _detailPanel.Resize += (_, _) =>
            {
                int w = Math.Max(300, _detailPanel.ClientSize.Width
                                      - _detailPanel.Padding.Horizontal - 20);
                stack.Width = w;
                foreach (Control c in stack.Controls)
                    if (c != sep) c.Width = w;
            };

            _detailPanel.ResumeLayout(true);
        }

        // ── One rounded card ──────────────────────────────────────────────────
        private Panel BuildSectionCard(string title, string body, int width)
        {
            const int PadH = 20;   // horizontal inner padding
            const int PadT = 16;   // top inner padding
            const int PadB = 16;   // bottom inner padding
            const int TitleH = 30;

            int innerWidth = width - PadH * 2;
            int bodyH      = MeasureTextHeight(body, new Font("Segoe UI", 10.5f), innerWidth);
            int totalH     = PadT + TitleH + 6 + bodyH + PadB;

            var card = new Panel
            {
                Size      = new Size(width, totalH),
                BackColor = BgPanel,
            };

            // Rounded card paint
            card.Paint += (_, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                using var path = RoundedRect(r, 10f);
                using var fill = new SolidBrush(BgPanel);
                g.FillPath(fill, path);
                using var border = new Pen(Color.FromArgb(35, 55, 140), 1f);
                g.DrawPath(border, path);
                // left accent stripe
                using var stripe = new SolidBrush(Accent);
                g.FillRectangle(stripe, 0, 14, 4, card.Height - 28);
            };

            var lblTitle = new Label
            {
                Text      = title,
                Location  = new Point(PadH, PadT),
                Size      = new Size(innerWidth, TitleH),
                Font      = new Font("Segoe UI Semibold", 11f),
                ForeColor = Accent,
                BackColor = Color.Transparent,
            };

            var lblBody = new Label
            {
                Text      = body,
                Location  = new Point(PadH, PadT + TitleH + 6),
                Size      = new Size(innerWidth, bodyH),
                Font      = new Font("Segoe UI", 10.5f),
                ForeColor = FgMain,
                BackColor = Color.Transparent,
            };

            card.Controls.Add(lblTitle);
            card.Controls.Add(lblBody);
            return card;
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static int MeasureTextHeight(string text, Font font, int width)
        {
            using var bmp = new Bitmap(1, 1);
            using var g   = Graphics.FromImage(bmp);
            using (font)   // dispose the passed-in font
            {
                var sz = g.MeasureString(text, font, width);
                return (int)Math.Ceiling(sz.Height);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, float rad)
        {
            var p = new GraphicsPath();
            p.AddArc(r.Left,             r.Top,              rad * 2, rad * 2, 180, 90);
            p.AddArc(r.Right - rad * 2,  r.Top,              rad * 2, rad * 2, 270, 90);
            p.AddArc(r.Right - rad * 2,  r.Bottom - rad * 2, rad * 2, rad * 2,   0, 90);
            p.AddArc(r.Left,             r.Bottom - rad * 2, rad * 2, rad * 2,  90, 90);
            p.CloseFigure();
            return p;
        }

        // ── Data model ────────────────────────────────────────────────────────
        private sealed class HelpTopic
        {
            public string                      Title    { get; }
            public string                      Heading  { get; }
            public (string SubTitle, string Body)[] Sections { get; }

            public HelpTopic(string title, string heading, (string, string)[] sections)
            {
                Title    = title;
                Heading  = heading;
                Sections = sections;
            }
        }
    }
}
