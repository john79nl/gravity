using System.Drawing;

namespace Gravity.Core
{
    public enum ThemeMode { Light, Dark }

    public class ThemeColors
    {
        public Color Background { get; set; }
        public Color Foreground { get; set; }
        public Color PanelBackground { get; set; }
        public Color ActivityBarBackground { get; set; }
        public Color HeaderBackground { get; set; }
        public Color Border { get; set; }
        public Color Accent { get; set; }
        public Color Success { get; set; }
        public Color Error { get; set; }
        public Color TabBackground { get; set; }
        public Color TabSelected { get; set; }
        public Color TabAccentLine { get; set; }
        public Color BreadcrumbBackground { get; set; }
        public Color BreadcrumbForeground { get; set; }
        public Color StatusBarBackground { get; set; }
        public Color StatusBarForeground { get; set; }
    }

    public interface IThemeService
    {
        ThemeMode CurrentMode { get; set; }
        ThemeColors Colors { get; }
        Color AccentColor { get; set; }
    }

    public class ThemeService : IThemeService
    {
        private ThemeMode _currentMode = ThemeMode.Light;
        private Color _accentColor = Color.FromArgb(9, 105, 218);

        public ThemeMode CurrentMode
        {
            get => _currentMode;
            set => _currentMode = value;
        }

        public Color AccentColor
        {
            get => _accentColor;
            set => _accentColor = value;
        }

        public ThemeColors Colors => _currentMode == ThemeMode.Dark ? GetDarkTheme() : GetLightTheme();

        private ThemeColors GetDarkTheme() => new ThemeColors
        {
            Background = Color.FromArgb(10, 12, 20),            // Deep Cosmic Indigo
            Foreground = Color.FromArgb(230, 230, 240),         // Clean White
            PanelBackground = Color.FromArgb(24, 26, 38),       // Surface Elev
            ActivityBarBackground = Color.FromArgb(13, 15, 25), // Activity Bar
            HeaderBackground = Color.FromArgb(30, 32, 45),
            Border = Color.FromArgb(45, 48, 65),                // Soft Glow Border
            Accent = Color.FromArgb(114, 137, 218),             // Blurple-ish Accent
            Success = Color.FromArgb(46, 204, 113),             // Emerald
            Error = Color.FromArgb(231, 76, 60),                // Alizarin
            TabBackground = Color.FromArgb(20, 22, 32),
            TabSelected = Color.FromArgb(114, 137, 218),
            TabAccentLine = Color.FromArgb(255, 255, 255),
            BreadcrumbBackground = Color.FromArgb(10, 12, 20),
            BreadcrumbForeground = Color.FromArgb(160, 160, 180),
            StatusBarBackground = Color.FromArgb(12, 14, 22),
            StatusBarForeground = Color.FromArgb(140, 140, 160)
        };

        private ThemeColors GetLightTheme() => new ThemeColors
        {
            Background = Color.FromArgb(255, 255, 255),
            Foreground = Color.FromArgb(27, 31, 36),
            PanelBackground = Color.FromArgb(246, 248, 250),
            ActivityBarBackground = Color.FromArgb(246, 248, 250),
            HeaderBackground = Color.FromArgb(240, 240, 240),
            Border = Color.FromArgb(208, 215, 222),
            Accent = Color.FromArgb(9, 105, 218),
            Success = Color.FromArgb(26, 127, 55),
            Error = Color.FromArgb(207, 34, 46),
            TabBackground = Color.FromArgb(228, 228, 228),
            TabSelected = Color.FromArgb(255, 255, 255),
            TabAccentLine = Color.FromArgb(9, 105, 218),
            BreadcrumbBackground = Color.FromArgb(248, 248, 248),
            BreadcrumbForeground = Color.FromArgb(100, 100, 100),
            StatusBarBackground = Color.FromArgb(9, 105, 218),
            StatusBarForeground = Color.White
        };
    }
}
