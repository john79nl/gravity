using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Gravity.Core;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Application = System.Windows.Application;

namespace Gravity.UI
{
    public class ArtifactPanel : UserControl
    {
        private readonly IArtifactService _artifactService;
        private readonly StackPanel _artifactList;
        private readonly ScrollViewer _contentViewer;
        private readonly Border _contentArea;
        private Artifact? _currentArtifact;

        public ArtifactPanel(IArtifactService artifactService)
        {
            _artifactService = artifactService ?? throw new ArgumentNullException(nameof(artifactService));

            // Grid Layout
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Sidebar
            var sidebar = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 13, 17, 23)), // Transparent
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 0 } // Base for glass
            };

            var listScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
            _artifactList = new StackPanel { Margin = new Thickness(15) };
            listScroll.Content = _artifactList;
            sidebar.Child = listScroll;
            Grid.SetColumn(sidebar, 0);
            grid.Children.Add(sidebar);

            // Content Area (frosted glass)
            _contentArea = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 10, 12, 16)),
                Padding = new Thickness(30)
            };
            _contentViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _contentArea.Child = _contentViewer;
            Grid.SetColumn(_contentArea, 1);
            grid.Children.Add(_contentArea);

            this.Content = grid;

            // Wire up Service
            _artifactService.OnArtifactCreated += a => this.Dispatcher.Invoke(() => RefreshList());
            _artifactService.OnArtifactUpdated += a => this.Dispatcher.Invoke(() => {
                RefreshList();
                if (_currentArtifact != null && _currentArtifact.Id == a.Id) {
                    DisplayArtifact(a);
                }
            });

            RefreshList();
        }

        private void RefreshList()
        {
            _artifactList.Children.Clear();
            foreach (var artifact in _artifactService.GetArtifacts())
            {
                var btn = CreateArtifactButton(artifact);
                _artifactList.Children.Add(btn);
            }
        }

        private UIElement CreateArtifactButton(Artifact artifact)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 12),
                Cursor = System.Windows.Input.Cursors.Hand,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 15, Opacity = 0.1, Direction = 270, ShadowDepth = 2 }
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = artifact.Title,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(new TextBlock
            {
                Text = artifact.Type.ToString().ToUpper(),
                Foreground = new SolidColorBrush(Color.FromRgb(88, 166, 255)),
                FontWeight = FontWeights.Bold,
                FontSize = 9,
                Opacity = 0.8,
                Margin = new Thickness(0, 6, 0, 0)
            });

            container.Child = stack;
            container.MouseDown += (s, e) => DisplayArtifact(artifact);
            
            // Premium Hover
            container.MouseEnter += (s, e) => {
                container.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
                container.BorderBrush = new SolidColorBrush(Color.FromRgb(88, 166, 255));
            };
            container.MouseLeave += (s, e) => {
                container.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                container.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            };

            return container;
        }

        private void DisplayArtifact(Artifact artifact)
        {
            _currentArtifact = artifact;
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = artifact.Title,
                Foreground = Brushes.White,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var statusBorder = new Border
            {
                Background = new SolidColorBrush(GetStatusColor(artifact.Status)),
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 2, 8, 2),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 0, 20)
            };
            statusBorder.Child = new TextBlock { Text = artifact.Status.ToString(), Foreground = Brushes.White, FontSize = 12 };
            panel.Children.Add(statusBorder);

            if (artifact is TaskArtifact ta && ta.Tasks.Count > 0)
            {
                foreach (var task in ta.Tasks)
                {
                    var taskGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                    taskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                    taskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var check = new Border
                    {
                        Width = 18,
                        Height = 18,
                        BorderThickness = new Thickness(1),
                        BorderBrush = Brushes.Gray,
                        CornerRadius = new CornerRadius(3),
                        Background = task.IsCompleted ? Brushes.SeaGreen : Brushes.Transparent
                    };
                    if (task.IsCompleted) check.Child = new TextBlock { Text = "✓", Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
                    
                    var label = new TextBlock
                    {
                        Text = task.Title,
                        Foreground = task.IsCompleted ? Brushes.Gray : Brushes.White,
                        FontSize = 16,
                        Margin = new Thickness(5, 0, 0, 0),
                        TextDecorations = task.IsCompleted ? TextDecorations.Strikethrough : null
                    };

                    Grid.SetColumn(check, 0);
                    Grid.SetColumn(label, 1);
                    taskGrid.Children.Add(check);
                    taskGrid.Children.Add(label);
                    panel.Children.Add(taskGrid);
                }
            }
            else if (!string.IsNullOrWhiteSpace(artifact.Content))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = artifact.Content,
                    Foreground = Brushes.LightGray,
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 24
                });
            }

            _contentViewer.Content = panel;
        }

        public void SelectArtifact(Artifact artifact)
        {
            if (artifact != null)
            {
                DisplayArtifact(artifact);
            }
        }

        private Color GetStatusColor(ArtifactStatus status) => status switch
        {
            ArtifactStatus.Completed => Color.FromRgb(46, 160, 67),
            ArtifactStatus.InReview => Color.FromRgb(210, 153, 34),
            ArtifactStatus.Failed => Color.FromRgb(248, 81, 73),
            _ => Color.FromRgb(88, 166, 255)
        };
    }
}
