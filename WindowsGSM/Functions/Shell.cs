using System.Diagnostics;

namespace WindowsGSM
{
    // Ouvre une URL ou un fichier/dossier via le shell Windows.
    // Sur .NET (Core/5+), Process.Start(string) a UseShellExecute=false par défaut et
    // ne peut PAS ouvrir une URL/dossier -> Win32Exception. Ce helper remet UseShellExecute=true.
    public static class Shell
    {
        public static void Open(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch { /* lien/chemin invalide -> on ignore plutôt que crasher */ }
        }
    }
}
