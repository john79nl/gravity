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
            Background = Color.FromArgb(7, 11, 25),            // Deep Cosmic Navy (#070b19)
            Foreground = Color.FromArgb(226, 232, 255),        // Lavender White (#e2e8ff)
            PanelBackground = Color.FromArgb(13, 20, 48),      // Surface Container (#0d1430)
            ActivityBarBackground = Color.FromArgb(6, 9, 21),   // Activity Bar (#060915)
            HeaderBackground = Color.FromArgb(10, 16, 38),      // Header Bar (#0a1026)
            Border = Color.FromArgb(26, 43, 86),               // Cyan/Navy Border (#1a2b56)
            Accent = Color.FromArgb(0, 242, 254),              // Neon Electric Cyan (#00f2fe)
            Success = Color.FromArgb(0, 242, 254),             // Neon Cyan
            Error = Color.FromArgb(255, 65, 108),              // Neon Crimson (#ff416c)
            TabBackground = Color.FromArgb(10, 16, 38),
            TabSelected = Color.FromArgb(0, 242, 254),
            TabAccentLine = Color.FromArgb(0, 242, 254),
            BreadcrumbBackground = Color.FromArgb(7, 11, 25),
            BreadcrumbForeground = Color.FromArgb(138, 153, 199), // Muted Blue-Grey (#8a99c7)
            StatusBarBackground = Color.FromArgb(6, 9, 21),
            StatusBarForeground = Color.FromArgb(138, 153, 199)
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
