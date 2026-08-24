using System;
using System.Linq;

// Fully-qualify WPF types to avoid WinForms ambiguity
using WpfColor    = System.Windows.Media.Color;
using WpfBrush    = System.Windows.Media.SolidColorBrush;
using WpfPoint    = System.Windows.Point;
using WpfSize     = System.Windows.Size;
using WpfRect     = System.Windows.Rect;
using WpfMouseArgs = System.Windows.Input.MouseEventArgs;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Gravity.Core;

namespace Gravity.UI
{
    public class BreakpointMargin : AbstractMargin
    {
        private readonly DebugService _debugService;
        private readonly string _filePath;
        private const double MarginWidth = 18;

        public BreakpointMargin(DebugService debugService, string filePath)
        {
            _debugService = debugService ?? throw new ArgumentNullException(nameof(debugService));
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

            _debugService.OnBreakpointToggled += bp =>
            {
                if (string.Equals(System.IO.Path.GetFullPath(bp.FilePath),
                    System.IO.Path.GetFullPath(_filePath),
                    StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.InvokeAsync(() => InvalidateVisual());
                }
            };
        }

        protected override WpfSize MeasureOverride(WpfSize availableSize) =>
            new WpfSize(MarginWidth, 0);

        protected override void OnRender(System.Windows.Media.DrawingContext dc)
        {
            var textView = TextView;
            if (textView == null || !textView.VisualLinesValid) return;

            var bgBrush = new WpfBrush(WpfColor.FromRgb(30, 30, 38));
            bgBrush.Freeze();
            dc.DrawRectangle(bgBrush, null, new WpfRect(0, 0, MarginWidth, RenderSize.Height));

            foreach (var vl in textView.VisualLines)
            {
                int lineNumber = vl.FirstDocumentLine.LineNumber;
                if (!_debugService.HasBreakpoint(_filePath, lineNumber)) continue;

                double y  = vl.VisualTop - textView.ScrollOffset.Y;
                double cx = MarginWidth / 2.0;
                double cy = y + vl.Height / 2.0;
                double r  = MarginWidth * 0.38;

                var redBrush = new WpfBrush(WpfColor.FromRgb(220, 50, 50));
                redBrush.Freeze();
                dc.DrawEllipse(redBrush, null, new WpfPoint(cx, cy), r, r);
            }
        }

        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var textView = TextView;
            if (textView == null) return;

            var pos = e.GetPosition(textView);
            var vl  = textView.GetVisualLineFromVisualTop(pos.Y + textView.ScrollOffset.Y);
            if (vl == null) return;

            _debugService.ToggleBreakpoint(_filePath, vl.FirstDocumentLine.LineNumber);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
        {
            if (oldTextView != null) oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
            if (newTextView != null) newTextView.VisualLinesChanged += OnVisualLinesChanged;
            base.OnTextViewChanged(oldTextView, newTextView);
        }

        private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();
    }
}
