using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Gravity.UI
{
    public class ModernSidebar : System.Windows.Controls.UserControl
    {
        private readonly StackPanel _itemsPanel;
        private Border? _selectionIndicator;
        private Border? _activeItem;
        public event Action<string>? OnItemSelected;

        public ModernSidebar()
        {
            var grid = new Grid();
            var bgBorder = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(240, 10, 12, 20)), // Very solid Deeper Indigo
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 1, 0)
            };
            grid.Children.Add(bgBorder);

            // Floating selection indicator behind items
            _selectionIndicator = new Border
            {
                Width = 44,
                Height = 44,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(3, -100, 0, 0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 114, 137, 218)),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 114, 137, 218)),
                BorderThickness = new Thickness(1),
                Opacity = 0
            };
            grid.Children.Add(_selectionIndicator);

            _itemsPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            grid.Children.Add(_itemsPanel);

            this.Content = grid;
        }

        public void AddItem(string title, string icon, object tag)
        {
            var border = new Border
            {
                Height = 50,
                Width = 50,
                Margin = new Thickness(0, 5, 0, 0),
                CornerRadius = new CornerRadius(12),
                Background = System.Windows.Media.Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = tag,
                ToolTip = title
            };

            var text = new TextBlock
            {
                Text = icon,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 140, 160)),
                FontSize = 20,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            border.Child = text;
            border.MouseDown += (s, e) => {
                OnItemSelected?.Invoke(title);
                SelectItem(border);
            };

            border.MouseEnter += (s, e) => {
                if (border != _activeItem)
                    text.Foreground = System.Windows.Media.Brushes.White;
            };

            border.MouseLeave += (s, e) => {
                if (border != _activeItem)
                    text.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 140, 160));
            };

            _itemsPanel.Children.Add(border);
        }

        public void SelectItem(string title)
        {
            foreach (Border b in _itemsPanel.Children)
            {
                if (b.ToolTip?.ToString() == title)
                {
                    SelectItem(b);
                    return;
                }
            }
        }

        private void SelectItem(Border selected)
        {
            _activeItem = selected;
            foreach (Border item in _itemsPanel.Children)
            {
                var txt = item.Child as TextBlock;
                if (item == selected)
                {
                    txt.Foreground = System.Windows.Media.Brushes.White;
                    AnimateIndicator(item);
                }
                else
                {
                    txt.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 140, 160));
                }
            }
        }

        private void AnimateIndicator(Border target)
        {
            if (_selectionIndicator == null) return;

            var point = target.TranslatePoint(new System.Windows.Point(0, 0), (UIElement)this.Content);
            
            _selectionIndicator.Opacity = 1;
            var anim = new ThicknessAnimation
            {
                To = new Thickness(3, point.Y + 3, 0, 0),
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            _selectionIndicator.BeginAnimation(Border.MarginProperty, anim);
        }
    }
}
