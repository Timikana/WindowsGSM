using System;
using System.IO;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Minimal application log, thread-safe and best-effort, to make OBSERVABLE the errors
    /// that were previously swallowed silently (catch {}). Unlike Debug.WriteLine, these writes
    /// persist in the Release build (the build deployed in production). Writes to
    /// &lt;WGSM&gt;\logs\windowsgsm-app.log. The logger never throws: a write failure is ignored.
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
                // A logger must never crash the caller.
            }
        }
    }
}
