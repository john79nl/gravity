using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.CodeAnalysis;
using TextDocument = ICSharpCode.AvalonEdit.Document.TextDocument;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace Gravity.UI
{
    public class DiagnosticItem
    {
        public int StartOffset { get; set; }
        public int Length { get; set; }
        public DiagnosticSeverity Severity { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class ErrorUnderlineRenderer : IBackgroundRenderer
    {
        private readonly TextDocument _document;
        private List<DiagnosticItem> _diagnostics = new List<DiagnosticItem>();
        private readonly object _lock = new object();

        public ErrorUnderlineRenderer(TextDocument document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public KnownLayer Layer => KnownLayer.Selection;

        public void SetDiagnostics(IEnumerable<Diagnostic> diagnostics)
        {
            lock (_lock)
            {
                _diagnostics = diagnostics
                    .Where(d => d.Location.IsInSource)
                    .Select(d => new DiagnosticItem
                    {
                        StartOffset = d.Location.SourceSpan.Start,
                        Length = Math.Max(1, d.Location.SourceSpan.Length),
                        Severity = d.Severity,
                        Message = d.GetMessage()
                    })
                    .ToList();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _diagnostics.Clear();
            }
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (textView == null || drawingContext == null) return;

            List<DiagnosticItem> items;
            lock (_lock)
            {
                items = _diagnostics.ToList();
            }

            if (!items.Any() || _document.TextLength == 0) return;

            var errorPen = new Pen(Brushes.Red, 1.25);
            errorPen.Freeze();

            var warningPen = new Pen(Brushes.Orange, 1.25);
            warningPen.Freeze();

            int docLength = _document.TextLength;

            foreach (var item in items)
            {
                int start = Math.Clamp(item.StartOffset, 0, docLength);
                int length = Math.Min(item.Length, docLength - start);

                if (length <= 0) continue;

                var segment = new TextSegment { StartOffset = start, Length = length };
                var pen = item.Severity == DiagnosticSeverity.Error ? errorPen : warningPen;

                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    Point startPoint = rect.BottomLeft;
                    Point endPoint = rect.BottomRight;

                    if (endPoint.X <= startPoint.X) continue;

                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        double waveHeight = 2.0;
                        double waveLength = 3.5;
                        bool up = true;

                        ctx.BeginFigure(startPoint, false, false);
                        for (double x = startPoint.X; x < endPoint.X; x += waveLength)
                        {
                            double y = startPoint.Y + (up ? -waveHeight : 0);
                            ctx.LineTo(new Point(Math.Min(x, endPoint.X), y), true, false);
                            up = !up;
                        }
                    }
                    geometry.Freeze();
                    drawingContext.DrawGeometry(null, pen, geometry);
                }
            }
        }
    }
}
