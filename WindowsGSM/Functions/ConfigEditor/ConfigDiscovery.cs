using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WindowsGSM.Functions.ConfigEditor
{
    /// <summary>
    /// Heuristic discovery of "server" config files in serverfiles (INI / property-XML /
    /// server.properties / cfg), filtering out noise (steamapps, logs, crash, runtimes...).
    /// </summary>
    public static class ConfigDiscovery
    {
        private static readonly HashSet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "serverconfig.xml", "server.properties", "gameusersettings.ini", "game.ini",
            "palworldsettings.ini", "engine.ini"
        };

        private static readonly string[] NoiseFragments =
        {
            "steamapps", "\\logs\\", "/logs/", "crashreportclient", "monobleedingedge",
            "\\jre", "/jre", "\\.git", "\\saved\\logs", "/saved/logs", "\\backup", "_netfx_backup"
        };

        public static List<string> Find(string serverFiles)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(serverFiles) || !Directory.Exists(serverFiles)) { return res; }

            foreach (string pat in new[] { "*.ini", "*.cfg", "*.properties", "*.xml" })
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(serverFiles, pat, SearchOption.AllDirectories); }
                catch { continue; }

                foreach (string f in files)
                {
                    string low = f.ToLowerInvariant();
                    if (NoiseFragments.Any(n => low.Contains(n))) { continue; }

                    string name = Path.GetFileName(f);
                    string ext = Path.GetExtension(low);
                    bool keep =
                        Known.Contains(name)
                        || ext == ".properties"
                        || (ext == ".ini" && (low.Contains("\\config") || low.Contains("/config") || name.IndexOf("server", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("settings", StringComparison.OrdinalIgnoreCase) >= 0))
                        || (ext == ".cfg" && (low.Contains("\\config") || low.Contains("/config") || name.IndexOf("server", StringComparison.OrdinalIgnoreCase) >= 0))
                        || (ext == ".xml" && name.IndexOf("config", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (keep && !res.Contains(f)) { res.Add(f); }
                }
            }

            // Known ones first, then the shallowest paths.
            res.Sort((a, b) =>
            {
                bool ka = Known.Contains(Path.GetFileName(a)), kb = Known.Contains(Path.GetFileName(b));
                if (ka != kb) { return ka ? -1 : 1; }
                return a.Length.CompareTo(b.Length);
            });
            return res;
        }
    }
}
