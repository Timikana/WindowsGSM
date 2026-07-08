using System.Diagnostics;

namespace WindowsGSM
{
    // Opens a URL or a file/folder via the Windows shell.
    // On .NET (Core/5+), Process.Start(string) has UseShellExecute=false by default and
    // CANNOT open a URL/folder -> Win32Exception. This helper sets UseShellExecute=true again.
    public static class Shell
    {
        public static void Open(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch { /* invalid link/path -> we ignore rather than crash */ }
        }
    }
}
