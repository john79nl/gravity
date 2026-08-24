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

namespace Gravity.UI
{
    public class ImpactPanel : UserControl
    {
        private readonly StackPanel _impactList;
        private readonly TextBlock _headerTitle;
        private readonly TextBlock _scoreLabel;
        private readonly Border _severityIndicator;

        public ImpactPanel()
        {
            // Main Layout
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header Section
            var header = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Padding = new Thickness(20),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var headerStack = new StackPanel();
            _headerTitle = new TextBlock
            {
                Text = "Impact Analysis",
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.Bold
            };
            var subhead = new TextBlock
            {
                Text = "Semantic Blast Radius (Roslyn)",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 15)
            };

            var scoreGrid = new Grid();
            scoreGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            scoreGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _severityIndicator = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            _scoreLabel = new TextBlock
            {
                Text = "No data analyzed yet.",
                Foreground = Brushes.White,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(_severityIndicator, 0);
            Grid.SetColumn(_scoreLabel, 1);
            scoreGrid.Children.Add(_severityIndicator);
            scoreGrid.Children.Add(_scoreLabel);

            headerStack.Children.Add(_headerTitle);
            headerStack.Children.Add(subhead);
            headerStack.Children.Add(scoreGrid);
            header.Child = headerStack;
            
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            // List Section
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
            _impactList = new StackPanel();
            scroll.Content = _impactList;

            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);

            this.Content = grid;
            this.Background = new SolidColorBrush(Color.FromRgb(10, 12, 16));
        }

        public void UpdateImpact(List<ImpactInfo> impacts)
        {
            this.Dispatcher.Invoke(() =>
            {
                _impactList.Children.Clear();

                if (impacts == null || !impacts.Any())
                {
                    _scoreLabel.Text = "Zero external impact detected.";
                    _severityIndicator.Background = new SolidColorBrush(Color.FromRgb(46, 160, 67)); // Green
                    return;
                }

                // Severity Logic
                int uniqueFiles = impacts.Select(i => i.DependentPath).Distinct().Count();
                _scoreLabel.Text = $"{impacts.Count} references across {uniqueFiles} files.";
                
                if (uniqueFiles > 5) _severityIndicator.Background = Brushes.Crimson;
                else if (uniqueFiles > 2) _severityIndicator.Background = Brushes.Orange;
                else _severityIndicator.Background = Brushes.DodgerBlue;

                // Group by File
                var groups = impacts.GroupBy(i => i.DependentFile);
                foreach (var group in groups)
                {
                    var groupItem = CreateImpactGroup(group.Key, group.ToList());
                    _impactList.Children.Add(groupItem);
                }
            });
        }

        private UIElement CreateImpactGroup(string fileName, List<ImpactInfo> references)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 15)
            };

            var stack = new StackPanel();
            var titleStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            
            titleStack.Children.Add(new TextBlock 
            { 
                Text = "🔗 " + fileName, 
                Foreground = Brushes.White, 
                FontWeight = FontWeights.Bold, 
                FontSize = 14 
            });

            stack.Children.Add(titleStack);

            foreach (var refItem in references)
            {
                var refBlock = new Grid { Margin = new Thickness(20, 2, 0, 2) };
                refBlock.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                refBlock.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var symbolText = new TextBlock
                {
                    Text = "• " + refItem.SymbolName,
                    Foreground = Brushes.LightGray,
                    FontSize = 12
                };
                var lineText = new TextBlock
                {
                    Text = "Line " + refItem.Line,
                    Foreground = Brushes.DimGray,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center
                };

                Grid.SetColumn(symbolText, 0);
                Grid.SetColumn(lineText, 1);
                refBlock.Children.Add(symbolText);
                refBlock.Children.Add(lineText);
                stack.Children.Add(refBlock);
            }

            border.Child = stack;
            return border;
        }
    }
}
