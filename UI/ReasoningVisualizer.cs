using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Gravity.UI
{
    public class ReasoningVisualizer : System.Windows.Controls.UserControl
    {
        private readonly Ellipse _core;
        private readonly Ellipse _glow;

        public ReasoningVisualizer()
        {
            var grid = new Grid();
            
            _glow = new Ellipse
            {
                Width = 40,
                Height = 40,
                Fill = new RadialGradientBrush(System.Windows.Media.Color.FromArgb(100, 88, 166, 255), System.Windows.Media.Colors.Transparent),
                Opacity = 0,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            _core = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(88, 166, 255)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, Color = System.Windows.Media.Color.FromRgb(88, 166, 255), Opacity = 0.5, ShadowDepth = 0 }
            };

            grid.Children.Add(_glow);
            grid.Children.Add(_core);
            this.Content = grid;
        }

        public void StartThinking()
        {
            var pulse = new DoubleAnimation
            {
                From = 0.2,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(1),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            var expand = new DoubleAnimation
            {
                From = 40,
                To = 120,
                Duration = TimeSpan.FromSeconds(1.5),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            _glow.BeginAnimation(OpacityProperty, pulse);
            _glow.BeginAnimation(FrameworkElement.WidthProperty, expand);
            _glow.BeginAnimation(FrameworkElement.HeightProperty, expand);
            _core.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 139, 253));
        }

        public void StopThinking()
        {
            _glow.BeginAnimation(OpacityProperty, null);
            _glow.BeginAnimation(FrameworkElement.WidthProperty, null);
            _glow.BeginAnimation(FrameworkElement.HeightProperty, null);
            _glow.Opacity = 0;
            _core.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(88, 166, 255));
        }
    }
}
