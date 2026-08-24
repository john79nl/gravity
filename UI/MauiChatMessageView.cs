using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
#if MAUI
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;

namespace Gravity.UI
{
    // MAUI ContentView equivalent of ChatMessageBubble (lightweight, bindable, streaming-friendly)
    public class MauiChatMessageView : ContentView
    {
        private readonly Label _headerLabel;
        private readonly Label _contentLabel;
        private readonly Button _toggleButton;
        private readonly ScrollView _detailsScroll;
        private readonly Label _detailsLabel;
        private readonly Button _copyCodeButton;

        private bool _isExpanded;

        private static readonly Regex CodeBlockRegex = new(@"```(?<lang>\w+)?\n(?<code>[\s\S]*?)```", RegexOptions.Compiled);
        private static readonly Regex LinkRegex = new(@"\bhttps?://\S+\b|file:\/\/\/\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly BindableProperty SenderProperty = BindableProperty.Create(nameof(Sender), typeof(string), typeof(MauiChatMessageView), string.Empty);
        public static readonly BindableProperty TimeProperty = BindableProperty.Create(nameof(Time), typeof(string), typeof(MauiChatMessageView), string.Empty);
        public static readonly BindableProperty ContentTextProperty = BindableProperty.Create(nameof(ContentText), typeof(string), typeof(MauiChatMessageView), string.Empty, propertyChanged: OnContentChanged);
        public static readonly BindableProperty DetailsTextProperty = BindableProperty.Create(nameof(DetailsText), typeof(string), typeof(MauiChatMessageView), string.Empty, propertyChanged: OnDetailsChanged);
        public static readonly BindableProperty IsUserProperty = BindableProperty.Create(nameof(IsUser), typeof(bool), typeof(MauiChatMessageView), false, propertyChanged: OnIsUserChanged);

        public string Sender {
            get => (string)GetValue(SenderProperty);
            set => SetValue(SenderProperty, value);
        }
        public string Time {
            get => (string)GetValue(TimeProperty);
            set => SetValue(TimeProperty, value);
        }
        public string ContentText {
            get => (string)GetValue(ContentTextProperty);
            set => SetValue(ContentTextProperty, value);
        }
        public string DetailsText {
            get => (string)GetValue(DetailsTextProperty);
            set => SetValue(DetailsTextProperty, value);
        }
        public bool IsUser {
            get => (bool)GetValue(IsUserProperty);
            set => SetValue(IsUserProperty, value);
        }

        public MauiChatMessageView()
        {
            Padding = new Thickness(8);
            Margin = new Thickness(0, 6);

            // Visual polish: rounded frame with subtle shadow-like border
            this.Background = Colors.Transparent;

            _headerLabel = new Label { FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Colors.FromArgb("FF9FB6FF") };
            _contentLabel = new Label { LineBreakMode = LineBreakMode.WordWrap, FontSize = 14, TextColor = Colors.FromArgb("FFF0F0F5") };
            _toggleButton = new Button { Text = "Show reasoning", FontSize = 12, IsVisible = false, BackgroundColor = Colors.Transparent, TextColor = Colors.FromArgb("FF8FBFFF") };
            _detailsLabel = new Label { LineBreakMode = LineBreakMode.WordWrap, FontFamily = "Consolas", FontSize = 12, TextColor = Colors.FromArgb("FFD6DCE7") };
            _detailsScroll = new ScrollView { Content = _detailsLabel, IsVisible = false, HeightRequest = 140 }; _detailsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Never;
            _copyCodeButton = new Button { Text = "Copy code", FontSize = 12, IsVisible = false, HorizontalOptions = LayoutOptions.End, BackgroundColor = Colors.Transparent, TextColor = Colors.FromArgb("FFBFE1FF") };

            _toggleButton.Clicked += (s, e) => ToggleDetails();
            _copyCodeButton.Clicked += async (s, e) => await CopyCodeBlocksAsync();

            var bubble = new Frame
            {
                CornerRadius = 10,
                Padding = new Thickness(10),
                HasShadow = false,
                BackgroundColor = Colors.FromArgb(IsUser ? "FF203040" : "FF202028"),
                BorderColor = Colors.FromArgb("FF2C3A52")
            };

            var stack = new VerticalStackLayout { Spacing = 6 };
            stack.Add(_headerLabel);
            stack.Add(_contentLabel);

            var codeRow = new Grid { ColumnDefinitions = new ColumnDefinitions { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            codeRow.Add(_toggleButton);
            codeRow.Add(_copyCodeButton);
            Grid.SetColumn(_toggleButton, 0);
            Grid.SetColumn(_copyCodeButton, 1);

            stack.Add(codeRow);
            stack.Add(_detailsScroll);

            bubble.Content = stack;
            Content = bubble;

            // Tap links inside content
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => HandleLinkTap(_contentLabel.Text);
            _contentLabel.GestureRecognizers.Add(tapGesture);
        }

        private static void OnContentChanged(BindableObject bindable, object oldVal, object newVal)
        {
            var view = (MauiChatMessageView)bindable;
            view.UpdateContent((newVal ?? string.Empty).ToString());
        }

        private static void OnDetailsChanged(BindableObject bindable, object oldVal, object newVal)
        {
            var view = (MauiChatMessageView)bindable;
            view._detailsLabel.Text = (newVal ?? string.Empty).ToString();
            view._toggleButton.IsVisible = !string.IsNullOrWhiteSpace(view._detailsLabel.Text);
        }

        private static void OnIsUserChanged(BindableObject bindable, object oldVal, object newVal)
        {
            var view = (MauiChatMessageView)bindable;
            view.BackgroundColor = (bool)newVal ? Colors.FromArgb("FF283A4A") : Colors.FromArgb("FF282830");
            view._contentLabel.TextColor = (bool)newVal ? Colors.White : Colors.FromArgb("FFE6E7EB");
        }

        private void UpdateContent(string text)
        {
            _headerLabel.Text = $"{Sender}  •  {Time}";
            _contentLabel.Text = text;

            // Detect code blocks
            var m = CodeBlockRegex.Match(text);
            if (m.Success)
            {
                _copyCodeButton.IsVisible = true;
                _toggleButton.IsVisible = true;
            }
            else
            {
                _copyCodeButton.IsVisible = false;
                // leave toggle visibility to details content
            }
        }

        private void ToggleDetails()
        {
            _isExpanded = !_isExpanded;
            _detailsScroll.IsVisible = _isExpanded;
            _toggleButton.Text = _isExpanded ? "Hide reasoning" : "Show reasoning";
        }

        public void AppendContent(string text)
        {
            if (Dispatcher == null) { ContentText += text; return; }
            Dispatcher.Dispatch(() => { ContentText += text; });
        }

        public void AppendDetails(string details)
        {
            if (Dispatcher == null) { DetailsText += details; return; }
            Dispatcher.Dispatch(() => { DetailsText += details; _toggleButton.IsVisible = true; });
        }

        private async Task CopyCodeBlocksAsync()
        {
            var matches = CodeBlockRegex.Matches(ContentText ?? string.Empty);
            if (matches.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            foreach (Match m in matches)
            {
                var code = m.Groups["code"].Value;
                sb.AppendLine(code);
                sb.AppendLine();
            }

            var toCopy = sb.ToString();
            try
            {
                await Clipboard.SetTextAsync(toCopy);
            }
            catch { /* Best-effort copy */ }
        }

        private void HandleLinkTap(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var m = LinkRegex.Match(text);
            if (!m.Success) return;
            var link = m.Value;

            // Prefer file:/// links to be handled by parent (expose event) — fallback to opening in system browser
            if (link.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                // Raise a routed event via MessagingCenter so host can open file in tab
                try { MessagingCenter.Send(this, "OpenFile", link.Replace("file://", string.Empty)); } catch { }
            }
            else
            {
                try { _ = Launcher.OpenAsync(new Uri(link)); } catch { }
            }
        }
    }
}
#endif
