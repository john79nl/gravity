using System;
using System.Linq;
#if MAUI
using Microsoft.Maui.Controls;
using Gravity.ViewModels;
using Gravity.Models;

namespace Gravity.UI
{
    public partial class ChatPage : ContentPage
    {
        private ChatViewModel _vm = new ChatViewModel();
        private CollectionView _messagesView;
        private TerminalPanel _terminalPanel;

        public ChatPage()
        {
            BindingContext = _vm;
            Title = "Gravity Chat";
            BackgroundColor = Colors.FromArgb("FF101217");

            _messagesView = new CollectionView
            {
                ItemsSource = _vm.Messages,
                ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 8 },
                ItemTemplate = new DataTemplate(() =>
                {
                    var view = new MauiChatMessageView();
                    view.SetBinding(MauiChatMessageView.SenderProperty, "Sender");
                    view.SetBinding(MauiChatMessageView.TimeProperty, "Time");
                    view.SetBinding(MauiChatMessageView.ContentTextProperty, "Content");
                    view.SetBinding(MauiChatMessageView.DetailsTextProperty, "Details");
                    view.SetBinding(MauiChatMessageView.IsUserProperty, "IsUser");
                    return view;
                }),
                SelectionMode = SelectionMode.None,
                Margin = new Thickness(8)
            };

            var input = new Entry { Placeholder = "Ask Gravity...", TextColor = Colors.White };
            input.SetBinding(Entry.TextProperty, "InputText", BindingMode.TwoWay);

            var sendBtn = new Button { Text = "Send" };
            sendBtn.Clicked += OnSendClicked;

            var terminalToggle = new Button { Text = "Toggle Terminal" };
            terminalToggle.Clicked += (s, e) => { _terminalPanel.IsVisible = !_terminalPanel.IsVisible; };

            _terminalPanel = new TerminalPanel { IsVisible = false, HeightRequest = 220 };

            var bottomBar = new HorizontalStackLayout { Spacing = 6, Padding = new Thickness(8) };
            bottomBar.Add(input);
            bottomBar.Add(sendBtn);
            bottomBar.Add(terminalToggle);

            var main = new Grid();
            main.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            main.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            main.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            main.Add(_messagesView);
            main.Add(bottomBar, 0, 1);
            main.Add(_terminalPanel, 0, 2);

            Content = main;
        }

        private void OnSendClicked(object? sender, EventArgs e)
        {
            var txt = _vm.InputText?.Trim();
            if (string.IsNullOrEmpty(txt)) return;

            var userMsg = _vm.AddUserMessage(txt);
            _vm.InputText = string.Empty;

            var assistant = _vm.AddAssistantMessage(" ");
            _ = SimulateStreamingResponseAsync(assistant, "Here is a simulated improved response with a code block:\n```csharp\nConsole.WriteLine(\"Hello MAUI Styled\");\n```\nAnd a link: https://example.com");

            ScrollToEnd();
        }

        private async System.Threading.Tasks.Task SimulateStreamingResponseAsync(ChatMessageModel msg, string full)
        {
            msg.Content = string.Empty;
            for (int i = 0; i < full.Length; i += 14)
            {
                var chunk = full.Substring(i, Math.Min(14, full.Length - i));
                _vm.AppendToMessage(msg, chunk);
                await System.Threading.Tasks.Task.Delay(90);
                ScrollToEnd();
            }
        }

        private void ScrollToEnd()
        {
            if (_messagesView.ItemsSource is System.Collections.IList list && list.Count > 0)
            {
                var last = list[list.Count - 1];
                _messagesView.ScrollTo(last, position: ScrollToPosition.End, animate: true);
            }
        }
    }
}
#endif
