using System;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace Gravity.UI
{
    public class RoslynCompletionData : ICompletionData
    {
        public RoslynCompletionData(string text, string? description = null, double priority = 0)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Description = string.IsNullOrEmpty(description) ? $"[Symbol] {text}" : description;
            Priority = priority;
        }

        public ImageSource? Image => null;

        public string Text { get; }

        public object Content => Text;

        public object Description { get; }

        public double Priority { get; }

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            if (textArea?.Document == null || completionSegment == null) return;
            textArea.Document.Replace(completionSegment, Text);
        }
    }
}
