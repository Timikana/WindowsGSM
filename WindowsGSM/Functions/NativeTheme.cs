using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Petit utilitaire d'apparence native : applique la barre de titre SOMBRE (DWM immersive dark
    /// mode) à une fenêtre WPF, pour que les dialogues construits en code s'accordent au thème sombre
    /// de l'app au lieu d'afficher une barre de titre blanche. Best-effort : silencieux si non supporté.
    /// </summary>
    public static class NativeTheme
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20; // Win10 2004+/Win11/Server récents

        public static void EnableDarkTitleBar(Window window)
        {
            if (window == null) { return; }

            void Apply()
            {
                try
                {
                    IntPtr hwnd = new WindowInteropHelper(window).Handle;
                    if (hwnd == IntPtr.Zero) { return; }
                    int on = 1;
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
                }
                catch { /* non supporté -> on ignore */ }
            }

            if (new WindowInteropHelper(window).Handle != IntPtr.Zero) { Apply(); }
            else { window.SourceInitialized += (s, e) => Apply(); }
        }
    }
}
