using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Gravity.UI
{
    public class CollapsibleStepGroup : Panel
    {
        private readonly Label _toggleHeader;
        private readonly Panel _stepsContainer;
        private readonly List<CollapsibleStepPanel> _steps = new();
        private bool _expanded = false;
        private int _stepCount = 0;

        private const int HeaderHeight = 32;

        public CollapsibleStepGroup()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.BackColor = Color.Transparent;
            this.Padding = new Padding(0);
            this.Margin = new Padding(0, 0, 0, 0);

            bool isDark = MaterialSkin.MaterialSkinManager.Instance?.Theme == MaterialSkin.MaterialSkinManager.Themes.DARK;
            Color primaryText = isDark ? Color.FromArgb(230, 230, 230) : Color.FromArgb(40, 40, 40);
            Color secondaryText = isDark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(100, 100, 100);

            _toggleHeader = new Label
            {
                Dock = DockStyle.Top,
                Height = HeaderHeight,
                Text = "Reasoning steps (0) >",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = secondaryText,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                Padding = new Padding(8, 0, 10, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };
            
            _toggleHeader.MouseEnter += (s, e) => _toggleHeader.ForeColor = primaryText;
            _toggleHeader.MouseLeave += (s, e) => _toggleHeader.ForeColor = secondaryText;
            _toggleHeader.Click += (s, e) => Toggle();

            _stepsContainer = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Visible = false,
                BackColor = Color.Transparent,
                Padding = new Padding(12, 0, 0, 0), // Left indent for steps
                Margin = new Padding(0)
            };

            this.Controls.Add(_stepsContainer);
            this.Controls.Add(_toggleHeader);
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            SyncWidthFromParent();
        }

        private void SyncWidthFromParent()
        {
            if (this.IsDisposed || this.Parent == null || this.Parent.IsDisposed) return;
            
            int w = this.Parent.ClientSize.Width;
            string parentTag = this.Parent.Tag as string ?? "";
            if (parentTag == "MsgBlock" || parentTag == "StreamingBubble")
            {
                w = Math.Max(200, this.Parent.ClientSize.Width - 120);
            }
            else if (this.Parent is FlowLayoutPanel && this.Parent.Parent != null)
            {
                w = this.Parent.Parent.ClientSize.Width - this.Parent.Padding.Horizontal - this.Parent.Margin.Horizontal;
                if (this.Parent.Parent.Tag as string == "AgentStepGroupOuter")
                {
                    w = Math.Max(200, this.Parent.Parent.ClientSize.Width - 145);
                }
            }

            if (w > 100 && this.Width != w)
            {
                this.MinimumSize = new Size(w, 0);
                this.MaximumSize = new Size(w, 0);
                this.Width = w;
                _stepsContainer.Width = w;
                foreach (Control c in _stepsContainer.Controls)
                {
                    if (c is Panel p && (p.Tag as string == "ApprovalCardPanel" || p.Tag as string == "ArtifactCardPanel"))
                    {
                        Gravity.UI.CardLayoutHelper.ResizeCardPanel(p, w - _stepsContainer.Padding.Horizontal);
                    }
                    else
                    {
                        c.Width = w - _stepsContainer.Padding.Horizontal;
                    }
                }
            }
        }

        public void AddArtifactCard(Control cardPanel)
        {
            cardPanel.Dock = DockStyle.Top;
            int targetWidth = _stepsContainer.Width - _stepsContainer.Padding.Horizontal;
            if (targetWidth > 50)
            {
                if (cardPanel is Panel p && (p.Tag as string == "ApprovalCardPanel" || p.Tag as string == "ArtifactCardPanel"))
                {
                    Gravity.UI.CardLayoutHelper.ResizeCardPanel(p, targetWidth);
                }
                else
                {
                    cardPanel.Width = targetWidth;
                }
            }

            _stepsContainer.Controls.Add(cardPanel);
            cardPanel.SendToBack(); // So it appears at the bottom
        }

        public CollapsibleStepPanel AddStep(int stepNumber, string toolName)
        {
            var step = new CollapsibleStepPanel(stepNumber, toolName);
            step.Dock = DockStyle.Top;
            
            // Sync width immediately to avoid layout jumps
            int targetWidth = _stepsContainer.Width - _stepsContainer.Padding.Horizontal;
            if (targetWidth > 50) step.Width = targetWidth;

            _steps.Add(step);
            // Insert at index 0 so newer steps stack beautifully, or add to bottom.
            // Let's add to bottom by adding to controls list normally:
            _stepsContainer.Controls.Add(step);
            // Send to back so DockStyle.Top stacks them chronologically (first added stays at top)
            step.SendToBack();

            _stepCount++;
            
            UpdateHeaderTitle();
            return step;
        }

        private void UpdateHeaderTitle()
        {
            string chevron = _expanded ? "▲" : "▼";
            _toggleHeader.Text = $"🧠 Reasoning steps ({_stepCount}) {chevron}";
        }

        public void Toggle()
        {
            _expanded = !_expanded;
            _stepsContainer.Visible = _expanded;
            UpdateHeaderTitle();
            
            // Trigger layout updates up the parent chain
            this.Parent?.PerformLayout();
        }

        public void Expand()
        {
            if (!_expanded) Toggle();
        }

        public void Collapse()
        {
            if (_expanded) Toggle();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            SyncWidthFromParent();
        }
    }
}
