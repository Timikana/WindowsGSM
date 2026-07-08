using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Discovery of Steam dedicated servers via the WindowsGSM/SteamAppInfo data repository.
    /// - apps.json: list of {appid, name} (search by name).
    /// - AppInfo/&lt;appid&gt;.json: Steam metadata, including config.launch (executable + arguments per OS).
    /// Lets you add "any" Steam server to WGSM by its AppID, without coding a plugin.
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

        /// <summary>Launch profile resolved from AppInfo (Windows executable + arguments).</summary>
        public struct LaunchProfile
        {
            public string AppId;
            public string Name;
            public string Executable; // e.g. "FactoryServer.exe" or "startdedicated.bat"
            public string Arguments;  // e.g. "-log -unattended"
            public bool Found;
        }

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

        /// <summary>Loads apps.json (full list of dedicated servers). Empty if unavailable.</summary>
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

        /// <summary>Substring search (case-insensitive) in names. Limits the number of results.</summary>
        public static async Task<List<AppEntry>> SearchAsync(string query, int max = 50)
        {
            var all = await GetAppListAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(query)) { return all.Take(max).ToList(); }
            string q = query.Trim().ToLowerInvariant();
            return all.Where(a => a.Name.ToLowerInvariant().Contains(q) || a.AppId == q).Take(max).ToList();
        }

        /// <summary>Resolves the Windows launch profile (exe + args + name) for an AppID via AppInfo/&lt;appid&gt;.json.</summary>
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

                        // We only want Windows (empty oslist = all OS, accepted as a last resort).
                        bool isWin = oslist.IndexOf("windows", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool anyOs = string.IsNullOrEmpty(oslist);
                        if (!isWin && !anyOs) { continue; }

                        // Score: windows 64 > windows > all-OS.
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
