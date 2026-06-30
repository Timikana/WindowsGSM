using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Small native-appearance utility: applies the DARK title bar (DWM immersive dark
    /// mode) to a WPF window, so that dialogs built in code match the app's dark theme
    /// instead of showing a white title bar. Best-effort: silent if not supported.
    /// </summary>
    public static class NativeTheme
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20; // Win10 2004+/Win11/recent Server

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
                catch { /* not supported -> we ignore */ }
            }

            if (new WindowInteropHelper(window).Handle != IntPtr.Zero) { Apply(); }
            else { window.SourceInitialized += (s, e) => Apply(); }
        }
    }
}
