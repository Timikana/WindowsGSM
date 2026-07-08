using System.Windows.Media;

namespace WindowsGSM.Functions.Controls
{
    /// <summary>
    /// Theme-aware brushes for the code-behind dialogs. They used to hard-code a dark palette
    /// (white text on #1f1f1f), which is unreadable when the app is switched to the light theme.
    /// We pick an explicit light or dark palette based on the active WPF-UI theme (resolved fresh on
    /// each access — dialogs are created per open, so they match the current theme). Explicit palettes
    /// are used rather than TryFindResource because the WPF-UI theme brushes aren't reliably resolvable
    /// from Application scope for a plain (non-Fluent) Window.
    /// </summary>
    public static class DialogTheme
    {
        public static bool IsLightTheme
        {
            get
            {
                try { return Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Light; }
                catch { return false; }
            }
        }

        private static Brush B(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

        public static Brush Fg => IsLightTheme ? B(0x1a, 0x1a, 0x1a) : B(0xf0, 0xf0, 0xf0);
        public static Brush Dim => IsLightTheme ? B(0x5a, 0x5a, 0x5a) : B(0x9a, 0x9a, 0x9a);
        public static Brush Bg => IsLightTheme ? B(0xf3, 0xf3, 0xf3) : B(0x1f, 0x1f, 0x1f);
        public static Brush CardBg => IsLightTheme ? B(0xfb, 0xfb, 0xfb) : B(0x2b, 0x2b, 0x2b);
        public static Brush CardBorder => IsLightTheme ? B(0xd0, 0xd0, 0xd0) : B(0x3a, 0x3a, 0x3a);

        // Accent + warning read acceptably on both light and dark, so they stay fixed.
        public static Brush Accent => B(0x2f, 0x9e, 0xb0);
        public static Brush Warn => B(0xd0, 0x5a, 0x5a);
    }
}
