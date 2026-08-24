using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Gravity.Core;

namespace Gravity.UI
{
    public class ArtifactCard : UserControl
    {
        public event Action<Artifact>? OnOpenArtifact;
        public event Action<Artifact>? OnExecuteArtifact;

        private readonly Artifact _artifact;

        public ArtifactCard(Artifact artifact, bool showExecuteButton = false)
        {
            _artifact = artifact;
            
            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 31, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(88, 166, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 10, 0, 10),
                Effect = new DropShadowEffect { BlurRadius = 15, Opacity = 0.3, ShadowDepth = 5 }
            };

            var layout = new DockPanel { LastChildFill = true };

            var headerText = new TextBlock
            {
                Text = $"New Artifact Created: {artifact.Type}",
                Foreground = Brushes.LightGray,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(headerText, Dock.Top);
            layout.Children.Add(headerText);

            var titleText = new TextBlock
            {
                Text = artifact.Title,
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            DockPanel.SetDock(titleText, Dock.Top);
            layout.Children.Add(titleText);

            // Content preview — truncated so the card stays compact
            if (!string.IsNullOrWhiteSpace(artifact.Content))
            {
                var preview = artifact.Content.Length > 300
                    ? artifact.Content.Substring(0, 300) + "..."
                    : artifact.Content;

                var contentText = new TextBlock
                {
                    Text = preview,
                    Foreground = Brushes.LightGray,
                    FontSize = 12,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 120,
                    Margin = new Thickness(0, 0, 0, 10),
                    Opacity = 0.8
                };
                DockPanel.SetDock(contentText, Dock.Top);
                layout.Children.Add(contentText);
            }
            else if (artifact is TaskArtifact ta && ta.Tasks.Count > 0)
            {
                // Show task checklist preview
                var taskPreview = string.Join("\n", ta.Tasks.Take(5).Select(t => $"  {(t.IsCompleted ? "[x]" : "[ ]")} {t.Title}"));
                if (ta.Tasks.Count > 5)
                    taskPreview += $"\n  ... and {ta.Tasks.Count - 5} more";

                var taskText = new TextBlock
                {
                    Text = taskPreview,
                    Foreground = Brushes.LightGray,
                    FontSize = 12,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 120,
                    Margin = new Thickness(0, 0, 0, 10),
                    Opacity = 0.8
                };
                DockPanel.SetDock(taskText, Dock.Top);
                layout.Children.Add(taskText);
            }

            // Footer with buttons — docked Bottom BEFORE fill child
            var btnStack = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 5, 0, 0)
            };



            var openBtn = new System.Windows.Controls.Button
            {
                Content = "View Plan",
                Background = new SolidColorBrush(Color.FromRgb(46, 160, 67)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(20, 8, 20, 8),
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            openBtn.Template = CreateButtonTemplate(Color.FromRgb(46, 160, 67));
            openBtn.Click += (s, e) => OnOpenArtifact?.Invoke(_artifact);

            btnStack.Children.Add(openBtn);

            if (_artifact.Type == ArtifactType.ImplementationPlan && showExecuteButton)
            {
                var execBtn = new System.Windows.Controls.Button
                {
                    Content = "Execute Plan",
                    Background = new SolidColorBrush(Color.FromRgb(130, 80, 220)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(20, 8, 20, 8),
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(10, 0, 0, 0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                execBtn.Template = CreateButtonTemplate(Color.FromRgb(130, 80, 220));
                execBtn.Click += (s, e) => {
                    execBtn.IsEnabled = false;
                    execBtn.Content = "Executing...";
                    OnExecuteArtifact?.Invoke(_artifact);
                };
                btnStack.Children.Add(execBtn);
            }

            var footerPanel = new System.Windows.Controls.StackPanel();
            footerPanel.Children.Add(btnStack);
            DockPanel.SetDock(footerPanel, Dock.Bottom);
            layout.Children.Add(footerPanel);

            mainBorder.Child = layout;
            this.Content = mainBorder;
        }

        private ControlTemplate CreateButtonTemplate(Color baseColor)
        {
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse($@"
                <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
                    <Border Background='{{TemplateBinding Background}}' CornerRadius='6' Padding='{{TemplateBinding Padding}}'>
                        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                    </Border>
                </ControlTemplate>");
        }
    }
}
