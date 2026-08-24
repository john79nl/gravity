using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ToolTip = System.Windows.Controls.ToolTip;
using Orientation = System.Windows.Controls.Orientation;
using Size = System.Windows.Size;
using FontFamily = System.Windows.Media.FontFamily;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Gravity.UI
{
    public class ApprovalCard : UserControl
    {
        public event Action? OnAllow;
        public event Action? OnDeny;
        public event Action? OnReview;

        public ApprovalCard(string tool, string verb, string command, int index, int total)
        {
            var mainStack = new StackPanel { Orientation = Orientation.Vertical };

            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 26, 38)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(45, 48, 65)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 4, 0, 4),
                Effect = new DropShadowEffect { BlurRadius = 10, Opacity = 0.25, ShadowDepth = 3 }
            };

            var bar = new Grid();
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left Section: Icon + Action text + Command details button with tooltip
            var leftStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            
            var iconLabel = new TextBlock
            {
                Text = "⚡",
                FontSize = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            leftStack.Children.Add(iconLabel);

            var titleLabel = new TextBlock
            {
                Text = $"Action Required: {verb}",
                Foreground = new SolidColorBrush(Color.FromRgb(230, 230, 240)),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            leftStack.Children.Add(titleLabel);

            // View Command Pill Button with Tooltip
            var viewCmdBtn = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(36, 39, 56)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 64, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = new ToolTip
                {
                    Content = new TextBlock
                    {
                        Text = command,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        Foreground = Brushes.White,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 450
                    },
                    Background = new SolidColorBrush(Color.FromRgb(15, 16, 25)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(80, 85, 110)),
                    Padding = new Thickness(10)
                }
            };

            var viewCmdStack = new StackPanel { Orientation = Orientation.Horizontal };
            viewCmdStack.Children.Add(new TextBlock { Text = "🔍 ", FontSize = 11 });
            viewCmdStack.Children.Add(new TextBlock { Text = "View Command", Foreground = new SolidColorBrush(Color.FromRgb(170, 175, 200)), FontSize = 11, FontWeight = FontWeights.Medium });
            viewCmdBtn.Child = viewCmdStack;
            leftStack.Children.Add(viewCmdBtn);

            Grid.SetColumn(leftStack, 0);
            bar.Children.Add(leftStack);

            // Expandable inline preview box when clicking "View Command"
            var previewBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 16, 25)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(45, 48, 65)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed
            };

            var previewText = new TextBox
            {
                Text = command,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 225, 245)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 120
            };
            previewBorder.Child = new ScrollViewer { Content = previewText, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            viewCmdBtn.MouseDown += (s, e) =>
            {
                previewBorder.Visibility = previewBorder.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            };

            // Right Section: 2 Compact Icon Action Buttons (Red X for Deny, Green Check for Allow)
            var actionStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // Deny Icon Button (Red ✖)
            var denyIconBtn = CreateIconButton("✖", Color.FromRgb(218, 54, 51), "Decline / Deny Command");
            denyIconBtn.Click += (s, e) => OnDeny?.Invoke();
            actionStack.Children.Add(denyIconBtn);

            // Allow Icon Button (Green ✔)
            var allowIconBtn = CreateIconButton("✔", Color.FromRgb(46, 160, 67), "Allow / Execute Command");
            allowIconBtn.Margin = new Thickness(8, 0, 0, 0);
            allowIconBtn.Click += (s, e) => OnAllow?.Invoke();
            actionStack.Children.Add(allowIconBtn);

            Grid.SetColumn(actionStack, 1);
            bar.Children.Add(actionStack);

            mainBorder.Child = bar;
            mainStack.Children.Add(mainBorder);
            mainStack.Children.Add(previewBorder);

            this.Content = mainStack;
            this.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        private Button CreateIconButton(string icon, Color bg, string tooltipText)
        {
            return new Button
            {
                Content = new TextBlock
                {
                    Text = icon,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Background = new SolidColorBrush(bg),
                BorderThickness = new Thickness(0),
                Width = 32,
                Height = 32,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = tooltipText,
                Template = CreateIconButtonTemplate()
            };
        }

        private ControlTemplate CreateIconButtonTemplate()
        {
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
                <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
                    <Border Background='{TemplateBinding Background}' CornerRadius='16'>
                        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                    </Border>
                </ControlTemplate>");
        }
    }
}
