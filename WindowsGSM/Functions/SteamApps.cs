using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Découverte de serveurs dédiés Steam via le dépôt de données WindowsGSM/SteamAppInfo.
    /// - apps.json : liste {appid, name} (recherche par nom).
    /// - AppInfo/&lt;appid&gt;.json : métadonnées Steam, dont config.launch (exécutable + arguments par OS).
    /// Permet d'ajouter "n'importe quel" serveur Steam à WGSM par son AppID, sans coder un plugin.
    /// </summary>
    public static class SteamApps
    {
        private const string AppsUrl = "https://raw.githubusercontent.com/WindowsGSM/SteamAppInfo/main/apps.json";
        private const string AppInfoUrlFmt = "https://raw.githubusercontent.com/WindowsGSM/SteamAppInfo/main/AppInfo/{0}.json";

        public struct AppEntry
        {
            public string AppId;
            public string Name;
        }

        /// <summary>Profil de lancement résolu depuis AppInfo (exécutable Windows + arguments).</summary>
        public struct LaunchProfile
        {
            public string AppId;
            public string Name;
            public string Executable; // ex. "FactoryServer.exe" ou "startdedicated.bat"
            public string Arguments;  // ex. "-log -unattended"
            public bool Found;
        }

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

        /// <summary>Charge apps.json (liste complète des serveurs dédiés). Vide si indisponible.</summary>
        public static async Task<List<AppEntry>> GetAppListAsync()
        {
            var list = new List<AppEntry>();
            try
            {
                string body = await _http.GetStringAsync(AppsUrl).ConfigureAwait(false);
                var arr = JArray.Parse(body);
                foreach (var item in arr)
                {
                    string id = item.Value<object>("appid")?.ToString();
                    string name = item.Value<string>("name");
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                    {
                        list.Add(new AppEntry { AppId = id, Name = name });
                    }
                }
            }
            catch { /* best-effort */ }
            return list;
        }

        /// <summary>Recherche par sous-chaîne (insensible à la casse) dans les noms. Limite le nombre de résultats.</summary>
        public static async Task<List<AppEntry>> SearchAsync(string query, int max = 50)
        {
            var all = await GetAppListAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(query)) { return all.Take(max).ToList(); }
            string q = query.Trim().ToLowerInvariant();
            return all.Where(a => a.Name.ToLowerInvariant().Contains(q) || a.AppId == q).Take(max).ToList();
        }

        /// <summary>Résout le profil de lancement Windows (exe + args + nom) pour un AppID via AppInfo/&lt;appid&gt;.json.</summary>
        public static async Task<LaunchProfile> ResolveLaunchAsync(string appId)
        {
            var prof = new LaunchProfile { AppId = appId, Found = false };
            if (string.IsNullOrWhiteSpace(appId)) { return prof; }
            try
            {
                string body = await _http.GetStringAsync(string.Format(AppInfoUrlFmt, appId)).ConfigureAwait(false);
                var root = JObject.Parse(body);
                prof.Name = root["common"]?.Value<string>("name") ?? $"AppID {appId}";

                var launch = root["config"]?["launch"] as JObject;
                if (launch != null)
                {
                    JObject best = null;
                    int bestScore = -1;
                    foreach (var entry in launch.Properties())
                    {
                        if (!(entry.Value is JObject e)) { continue; }
                        var cfg = e["config"] as JObject;
                        string oslist = cfg?.Value<string>("oslist") ?? string.Empty;
                        string osarch = cfg?.Value<string>("osarch") ?? string.Empty;

                        // On ne veut que Windows (oslist vide = tous OS, accepté en dernier recours).
                        bool isWin = oslist.IndexOf("windows", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool anyOs = string.IsNullOrEmpty(oslist);
                        if (!isWin && !anyOs) { continue; }

                        // Score : windows 64 > windows > tous-OS.
                        int score = isWin ? (osarch == "64" ? 3 : 2) : 1;
                        if (score > bestScore && !string.IsNullOrEmpty(e.Value<string>("executable")))
                        {
                            bestScore = score;
                            best = e;
                        }
                    }

                    if (best != null)
                    {
                        prof.Executable = best.Value<string>("executable");
                        prof.Arguments = best.Value<string>("arguments") ?? string.Empty;
                        prof.Found = !string.IsNullOrEmpty(prof.Executable);
                    }
                }
            }
            catch { /* best-effort */ }
            return prof;
        }
    }
}
