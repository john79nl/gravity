// ────────────────────────────────────────────────────────────────────────────
// ErrorTooltipController.cs
// This file is pure WPF (hosted inside ElementHost). We deliberately avoid
// importing System.Windows.Forms.* or System.Drawing.* to prevent ambiguity.
// ────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Linq;

// WPF aliases — no System.Drawing or System.Windows.Forms imports here
using MediaColor   = System.Windows.Media.Color;
using MediaColors  = System.Windows.Media.Colors;
using MediaBrush   = System.Windows.Media.SolidColorBrush;
using MediaFont    = System.Windows.Media.FontFamily;
using WpfPoint     = System.Windows.Point;
using WpfToolTip   = System.Windows.Controls.ToolTip;
using WpfMouseArgs = System.Windows.Input.MouseEventArgs;

using ICSharpCode.AvalonEdit;

namespace Gravity.UI
{
    /// <summary>
    /// Attaches to an AvalonEdit TextEditor and shows a styled tooltip bubble
    /// whenever the mouse hovers over a Roslyn diagnostic span.
    /// </summary>
    public class ErrorTooltipController
    {
        private readonly TextEditor _editor;
        private readonly WpfToolTip _toolTip;
        private List<(int Offset, int Length, string Message, bool IsError)> _spans = new();

        public ErrorTooltipController(TextEditor editor)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));

            _toolTip = new WpfToolTip
            {
                Placement       = System.Windows.Controls.Primitives.PlacementMode.Mouse,
                PlacementTarget = editor,
                HasDropShadow   = true,
                IsOpen          = false,
                StaysOpen       = true,   // keep open until mouse leaves the TextView
                Background      = new MediaBrush(MediaColor.FromRgb(24, 26, 52)),
                BorderBrush     = new MediaBrush(MediaColor.FromRgb(80, 90, 160)),
                BorderThickness = new System.Windows.Thickness(1),
                Padding         = new System.Windows.Thickness(0)
            };

            editor.TextArea.TextView.MouseHover += OnMouseHover;
            editor.TextArea.TextView.MouseLeave += OnMouseLeave;   // close when cursor exits editor
        }

        /// <summary>Updates diagnostic spans — thread-safe.</summary>
        public void UpdateDiagnostics(
            IEnumerable<(int Offset, int Length, string Message, bool IsError)> diagnostics)
        {
            var snapshot = diagnostics.ToList();
            _editor.Dispatcher.InvokeAsync(() => _spans = snapshot);
        }

        // ── Mouse handlers ─────────────────────────────────────────────────────

        private void OnMouseHover(object? sender, WpfMouseArgs e)
        {
            var pos = e.GetPosition(_editor.TextArea.TextView);
            int offset = GetDocOffset(pos);
            if (offset < 0) { _toolTip.IsOpen = false; return; }

            var hit = _spans.FirstOrDefault(s =>
                offset >= s.Offset && offset <= s.Offset + s.Length);

            if (hit == default)
            {
                _toolTip.IsOpen = false;
                return;
            }

            // Rebuild content only when hovering over a new span
            _toolTip.Content = BuildContent(hit.Message, hit.IsError);
            _toolTip.IsOpen  = true;
            e.Handled = true;
        }

        private void OnMouseLeave(object? sender, WpfMouseArgs e) =>
            _toolTip.IsOpen = false;

        // ── Offset resolution ─────────────────────────────────────────────────

        private int GetDocOffset(WpfPoint visualPos)
        {
            try
            {
                var tv  = _editor.TextArea.TextView;
                var doc = _editor.Document;
                if (doc == null) return -1;

                var lp = tv.GetPosition(new WpfPoint(
                    visualPos.X + tv.ScrollOffset.X,
                    visualPos.Y + tv.ScrollOffset.Y));
                if (lp == null) return -1;

                int line = Math.Max(1, Math.Min(lp.Value.Line, doc.LineCount));
                var docLine = doc.GetLineByNumber(line);
                int col  = Math.Max(0, Math.Min(lp.Value.Column - 1, docLine.Length));
                return docLine.Offset + col;
            }
            catch { return -1; }
        }

        // ── Tooltip content builder ────────────────────────────────────────────

        private static System.Windows.UIElement BuildContent(string message, bool isError)
        {
            MediaColor accentColor = isError
                ? MediaColor.FromRgb(255, 80, 80)
                : MediaColor.FromRgb(255, 190, 50);
            MediaColor kindColor = isError
                ? MediaColor.FromRgb(255, 100, 100)
                : MediaColor.FromRgb(255, 200, 70);
            MediaColor borderAccent = isError
                ? MediaColor.FromRgb(180, 40, 40)
                : MediaColor.FromRgb(180, 140, 30);

            var icon = new System.Windows.Controls.TextBlock
            {
                Text              = isError ? "✖" : "⚠",
                Foreground        = new MediaBrush(accentColor),
                FontSize          = 13,
                FontWeight        = System.Windows.FontWeights.Bold,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin            = new System.Windows.Thickness(0, 0, 8, 0)
            };

            var kind = new System.Windows.Controls.TextBlock
            {
                Text              = isError ? "Error" : "Warning",
                Foreground        = new MediaBrush(kindColor),
                FontSize          = 11,
                FontWeight        = System.Windows.FontWeights.Bold,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            var header = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin      = new System.Windows.Thickness(12, 10, 12, 4)
            };
            header.Children.Add(icon);
            header.Children.Add(kind);

            var sep = new System.Windows.Shapes.Rectangle
            {
                Height = 1,
                Fill   = new MediaBrush(MediaColor.FromRgb(55, 60, 100)),
                Margin = new System.Windows.Thickness(0, 2, 0, 2)
            };

            var body = new System.Windows.Controls.TextBlock
            {
                Text         = message,
                Foreground   = new MediaBrush(MediaColor.FromRgb(210, 215, 255)),
                FontSize     = 11,
                FontFamily   = new MediaFont("Consolas, Segoe UI"),
                TextWrapping = System.Windows.TextWrapping.Wrap,
                MaxWidth     = 460,
                Margin       = new System.Windows.Thickness(12, 4, 12, 10)
            };

            var hint = new System.Windows.Controls.TextBlock
            {
                Text       = "💡 Press Ctrl+. for quick fixes",
                Foreground = new MediaBrush(MediaColor.FromRgb(110, 120, 180)),
                FontSize   = 10,
                Margin     = new System.Windows.Thickness(12, 0, 12, 8)
            };

            var stack = new System.Windows.Controls.StackPanel();
            stack.Children.Add(header);
            stack.Children.Add(sep);
            stack.Children.Add(body);
            stack.Children.Add(hint);

            return new System.Windows.Controls.Border
            {
                Background      = new MediaBrush(MediaColor.FromRgb(18, 20, 46)),
                BorderBrush     = new MediaBrush(borderAccent),
                BorderThickness = new System.Windows.Thickness(0, 0, 0, 2),
                CornerRadius    = new System.Windows.CornerRadius(6),
                MinWidth        = 220,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color       = MediaColors.Black,
                    Opacity     = 0.7,
                    BlurRadius  = 12,
                    ShadowDepth = 3
                },
                Child = stack
            };
        }
    }
}
