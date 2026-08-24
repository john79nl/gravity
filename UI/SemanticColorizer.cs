using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.CodeAnalysis.Classification;
using WpfColor = System.Windows.Media.Color;

namespace Gravity.UI
{
    public class SemanticColorizer : DocumentColorizingTransformer
    {
        private List<ClassifiedSpan> _spans = new List<ClassifiedSpan>();
        private readonly object _lock = new object();
        public bool IsDarkTheme { get; set; } = true;

        public void SetSpans(IEnumerable<ClassifiedSpan> spans)
        {
            lock (_lock)
            {
                _spans = spans.ToList();
            }
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            List<ClassifiedSpan> localSpans;
            lock (_lock)
            {
                localSpans = _spans.Where(s => s.TextSpan.OverlapsWith(new Microsoft.CodeAnalysis.Text.TextSpan(line.Offset, line.Length))).ToList();
            }

            foreach (var span in localSpans)
            {
                int start = Math.Max(line.Offset, span.TextSpan.Start);
                int end = Math.Min(line.EndOffset, span.TextSpan.End);

                if (start < end)
                {
                    ChangeLinePart(start, end, (VisualLineElement element) =>
                    {
                        var color = GetColorForClassification(span.ClassificationType);
                        if (color != null)
                        {
                            element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(color.Value));
                        }
                    });
                }
            }
        }

        private WpfColor? GetColorForClassification(string classification)
        {
            if (IsDarkTheme)
            {
                // VS Code "Dark+" Palette
                return classification switch
                {
                    ClassificationTypeNames.Keyword => WpfColor.FromRgb(86, 156, 214),       // Blue
                    ClassificationTypeNames.ControlKeyword => WpfColor.FromRgb(197, 134, 192), // Purple
                    ClassificationTypeNames.ClassName => WpfColor.FromRgb(78, 201, 176),      // Teal
                    ClassificationTypeNames.InterfaceName => WpfColor.FromRgb(78, 201, 176),
                    ClassificationTypeNames.StructName => WpfColor.FromRgb(78, 201, 176),
                    ClassificationTypeNames.EnumName => WpfColor.FromRgb(78, 201, 176),
                    ClassificationTypeNames.DelegateName => WpfColor.FromRgb(78, 201, 176),
                    ClassificationTypeNames.NamespaceName => WpfColor.FromRgb(78, 201, 176),
                    ClassificationTypeNames.MethodName => WpfColor.FromRgb(220, 220, 170),    // Yellow
                    ClassificationTypeNames.ExtensionMethodName => WpfColor.FromRgb(220, 220, 170),
                    ClassificationTypeNames.PropertyName => WpfColor.FromRgb(220, 220, 170),
                    ClassificationTypeNames.LocalName => WpfColor.FromRgb(156, 220, 254),     // Light Blue
                    ClassificationTypeNames.ParameterName => WpfColor.FromRgb(156, 220, 254),
                    ClassificationTypeNames.FieldName => WpfColor.FromRgb(184, 184, 184),      // Grey
                    ClassificationTypeNames.StringLiteral => WpfColor.FromRgb(206, 145, 120),  // Orange
                    ClassificationTypeNames.VerbatimStringLiteral => WpfColor.FromRgb(206, 145, 120),
                    ClassificationTypeNames.Comment => WpfColor.FromRgb(106, 153, 85),        // Green
                    "static symbol" => WpfColor.FromRgb(220, 220, 170),                     // Yellowish
                    _ => null
                };
            }
            else
            {
                // VS Code "Light+" Palette
                return classification switch
                {
                    ClassificationTypeNames.Keyword => WpfColor.FromRgb(0, 0, 255),          // Pure Blue
                    ClassificationTypeNames.ControlKeyword => WpfColor.FromRgb(175, 0, 219), // Purple
                    ClassificationTypeNames.ClassName => WpfColor.FromRgb(38, 127, 153),    // Teal-ish
                    ClassificationTypeNames.InterfaceName => WpfColor.FromRgb(38, 127, 153),
                    ClassificationTypeNames.MethodName => WpfColor.FromRgb(121, 94, 38),     // Brown/Yellow
                    ClassificationTypeNames.LocalName => WpfColor.FromRgb(0, 16, 128),       // Navy
                    ClassificationTypeNames.StringLiteral => WpfColor.FromRgb(163, 21, 21),  // Red
                    ClassificationTypeNames.Comment => WpfColor.FromRgb(0, 128, 0),          // Green
                    _ => null
                };
            }
        }
    }
}
