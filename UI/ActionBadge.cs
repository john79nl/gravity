using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace Gravity.UI
{
    public class ActionBadge : UserControl
    {
        private readonly StackPanel _mainStack;
        private readonly StackPanel _detailStack;
        private readonly TextBlock _headerText;
        private readonly TextBlock _arrowText;
        private bool _isExpanded = false;
        private int _count = 0;
        private string _type;

        public ActionBadge(string type)
        {
            _type = type;
            _mainStack = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
            
            var header = new Border
            {
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Padding = new Thickness(5, 2, 5, 2)
            };
            header.MouseDown += (s, e) => Toggle();

            var headerContent = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            _headerText = new TextBlock
            {
                Text = $"{_type} 0 files",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            _arrowText = new TextBlock
            {
                Text = " >",
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            };

            headerContent.Children.Add(_headerText);
            headerContent.Children.Add(_arrowText);
            header.Child = headerContent;

            _detailStack = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(25, 5, 0, 10) };

            _mainStack.Children.Add(header);
            _mainStack.Children.Add(_detailStack);

            this.Content = _mainStack;
        }

        public void AddAction(string detail)
        {
            _count++;
            _headerText.Text = $"{_type} {_count} file" + (_count > 1 ? "s" : "");
            
            var item = new TextBlock
            {
                Text = detail,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0)
            };
            // Format "Analyzed C#" with specific colors if needed
            if (detail.Contains("Analyzed C#"))
            {
                 // We could use Run for rich formatting here
            }

            _detailStack.Children.Add(item);
        }

        private void Toggle()
        {
            _isExpanded = !_isExpanded;
            _detailStack.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
            _arrowText.Text = _isExpanded ? " v" : " >";
        }
    }
}
