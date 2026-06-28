using System;
using System.IO;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Journal applicatif minimal, thread-safe et best-effort, pour rendre OBSERVABLES les erreurs
    /// jusque-là avalées silencieusement (catch {}). Contrairement à Debug.WriteLine, ces écritures
    /// subsistent en build Release (le build déployé en prod). Écrit dans
    /// &lt;WGSM&gt;\logs\windowsgsm-app.log. Le logger ne lève jamais : un échec d'écriture est ignoré.
    /// </summary>
    public static class AppLog
    {
        private static readonly object _lock = new object();
        private const string FileName = "windowsgsm-app.log";

        public static void Info(string source, string message) => Write("INFO", source, message);
        public static void Warn(string source, string message) => Write("WARN", source, message);
        public static void Error(string source, string message) => Write("ERROR", source, message);

        private static void Write(string level, string source, string message)
        {
            try
            {
                string dir = ServerPath.GetLogs();
                if (string.IsNullOrEmpty(dir)) { return; }
                Directory.CreateDirectory(dir);
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {source}: {message}{Environment.NewLine}";
                lock (_lock) { File.AppendAllText(Path.Combine(dir, FileName), line); }
            }
            catch
            {
                // Un logger ne doit jamais faire planter l'appelant.
            }
        }
    }
}
