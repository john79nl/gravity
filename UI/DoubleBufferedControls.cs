using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace Gravity.UI
{
    public class DoubleBufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public DoubleBufferedFlowLayoutPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }

    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }

    public static class CardLayoutHelper
    {
        public static void ResizeCardPanel(Panel cardPanel, int targetWidth)
        {
            if (cardPanel == null) return;

            cardPanel.SuspendLayout();

            string tag = cardPanel.Tag as string ?? "";
            bool isCompactCard = tag == "ApprovalCardPanel" || tag == "ArtifactCardPanel";

            int minHeight = cardPanel.MinimumSize.Height;
            if (minHeight <= 0)
            {
                minHeight = isCompactCard ? (cardPanel.Height > 0 ? cardPanel.Height : 44) : 120;
            }

            if (isCompactCard)
            {
                int cardH = cardPanel.Height > 0 ? cardPanel.Height : 44;
                cardPanel.MinimumSize = new Size(targetWidth, cardH);
                cardPanel.MaximumSize = new Size(targetWidth, cardH);
                cardPanel.Size = new Size(targetWidth, cardH);
            }
            else
            {
                cardPanel.MinimumSize = new Size(targetWidth, minHeight);
                cardPanel.MaximumSize = new Size(targetWidth, 0);
                cardPanel.Width = targetWidth;
            }

            foreach (Control child in cardPanel.Controls)
            {
                if (child is ElementHost host)
                {
                    host.Width = targetWidth;
                    if (host.Child is System.Windows.FrameworkElement wpfElement)
                    {
                        wpfElement.Width = targetWidth - cardPanel.Padding.Horizontal;
                        wpfElement.UpdateLayout();
                        wpfElement.Measure(new System.Windows.Size(wpfElement.Width, double.PositiveInfinity));

                        int wpfHeight = (int)wpfElement.DesiredSize.Height;
                        host.Height = Math.Max(minHeight, wpfHeight + 10);
                        cardPanel.Height = host.Height + cardPanel.Padding.Vertical;
                    }
                }
            }

            cardPanel.ResumeLayout(true);
        }
    }
}
