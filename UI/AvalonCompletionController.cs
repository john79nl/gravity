using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Gravity.Core;

namespace Gravity.UI
{
    public class AvalonCompletionController
    {
        private readonly TextEditor _editor;
        private readonly RoslynService _roslyn;
        private readonly Func<string?> _getProjectPath;
        private readonly string _filePath;
        private CompletionWindow? _completionWindow;

        public AvalonCompletionController(TextEditor editor, RoslynService roslyn, Func<string?> getProjectPath, string filePath)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _roslyn = roslyn ?? throw new ArgumentNullException(nameof(roslyn));
            _getProjectPath = getProjectPath ?? throw new ArgumentNullException(nameof(getProjectPath));
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

            _editor.TextArea.TextEntering += TextArea_TextEntering;
            _editor.TextArea.TextEntered += TextArea_TextEntered;
            _editor.TextArea.KeyDown += TextArea_KeyDown;
        }

        private void TextArea_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                _ = TriggerCompletionAsync();
            }
        }

        private void TextArea_TextEntering(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length > 0 && _completionWindow != null)
            {
                if (!char.IsLetterOrDigit(e.Text[0]) && e.Text[0] != '_')
                {
                    // Whenever a non-letter/digit/underscore is typed, confirm current completion
                    _completionWindow.CompletionList.RequestInsertion(e);
                }
            }
        }

        private async void TextArea_TextEntered(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length > 0)
            {
                char c = e.Text[0];
                if (c == '.' || char.IsLetter(c) || c == '_')
                {
                    await TriggerCompletionAsync();
                }
            }
        }

        public async Task TriggerCompletionAsync()
        {
            try
            {
                int caretOffset = _editor.CaretOffset;
                string? projectPath = _getProjectPath();

                List<CompletionItem> items;
                if (!string.IsNullOrEmpty(projectPath))
                {
                    items = await _roslyn.GetCompletionsAsync(projectPath, _filePath, caretOffset);
                }
                else
                {
                    items = await _roslyn.GetDefaultCompletionsAsync(_editor.Text, caretOffset);
                }

                if (items == null || !items.Any()) return;

                _editor.Dispatcher.Invoke(() =>
                {
                    if (_completionWindow != null)
                    {
                        _completionWindow.Close();
                        _completionWindow = null;
                    }

                    _completionWindow = new CompletionWindow(_editor.TextArea);
                    var dataList = _completionWindow.CompletionList.CompletionData;

                    foreach (var item in items)
                    {
                        dataList.Add(new RoslynCompletionData(item.Text, item.Description));
                    }

                    _completionWindow.Show();
                    _completionWindow.Closed += (s, e) => { _completionWindow = null; };
                });
            }
            catch
            {
                /* best-effort completion */
            }
        }
    }
}
