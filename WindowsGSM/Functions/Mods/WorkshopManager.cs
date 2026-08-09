using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WindowsGSM.Functions.Mods
{
    /// <summary>
    /// Downloading Steam Workshop mods via SteamCMD and wiring them into the game config.
    /// Download = best-effort (anonymous SteamCMD); wiring = writes the list of enabled IDs in the
    /// profile's file/key (e.g. ARK ActiveMods= in GameUserSettings.ini [ServerSettings]).
    /// </summary>
    public static class WorkshopManager
    {
        private static string SteamCmdExe() => Path.Combine(Functions.ServerPath.GetBin("steamcmd"), "steamcmd.exe");

        /// <summary>Digits only (anti SteamCMD argument injection).</summary>
        private static string Digits(string s) => new string((s ?? "").Where(char.IsDigit).ToArray());

        /// <summary>Folder where SteamCMD drops the downloaded Workshop content.</summary>
        public static string ContentPath(int appId, string id)
            => Path.Combine(Functions.ServerPath.GetBin("steamcmd"), "steamapps", "workshop", "content", appId.ToString(), Digits(id));

        // Steam account used for Workshop downloads (username only — the password/Steam Guard is entered
        // ONCE by the user via the interactive login, and cached by SteamCMD). Empty = anonymous.
        private static string AccountFile() => Functions.ServerPath.Get("configs", "steam_workshop_account.txt");
        public static string GetSteamAccount() { try { return File.Exists(AccountFile()) ? File.ReadAllText(AccountFile()).Trim() : string.Empty; } catch { return string.Empty; } }
        public static void SetSteamAccount(string user)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(AccountFile())); File.WriteAllText(AccountFile(), (user ?? string.Empty).Trim()); } catch { }
        }

        /// <summary>Opens SteamCMD in a normal console so the user can log in interactively (password +
        /// Steam Guard) ONCE. SteamCMD then caches the session; later downloads reuse it passwordless.</summary>
        public static void LaunchInteractiveLogin(string user)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = SteamCmdExe(),
                    Arguments = $"+login {user} +quit",
                    UseShellExecute = true, // visible window so the user can type the password / Steam Guard code
                    WorkingDirectory = Functions.ServerPath.GetBin("steamcmd")
                });
            }
            catch (Exception ex) { Functions.AppLog.Warn("Workshop/Login", ex.Message); }
        }

        /// <summary>Downloads a Workshop item via SteamCMD (configured account if any, otherwise anonymous). Returns (ok, message).</summary>
        public static async Task<(bool ok, string message)> DownloadAsync(int appId, string id, Action<string> log = null)
        {
            string exe = SteamCmdExe();
            if (!File.Exists(exe)) { return (false, "steamcmd.exe not found (" + exe + ")."); }
            string sid = Digits(id);
            if (string.IsNullOrEmpty(sid) || appId <= 0) { return (false, "Invalid ID/AppID."); }

            string account = GetSteamAccount();
            string login = string.IsNullOrEmpty(account) ? "+login anonymous" : $"+login {account}";

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"{login} +workshop_download_item {appId} {sid} +quit",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Functions.ServerPath.GetBin("steamcmd")
            };

            try
            {
                var sb = new StringBuilder();
                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) { sb.AppendLine(e.Data); log?.Invoke(e.Data); } };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) { sb.AppendLine(e.Data); } };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    // SteamCMD waits on stdin when the cached session is gone (it wants a password and a
                    // Steam Guard code), and stdin is not attached here — so it hangs for ever. That used to
                    // pin a server restart open: the game stayed down until the process was killed by hand.
                    if (!await Task.Run(() => p.WaitForExit(90_000)))
                    {
                        try { p.Kill(true); } catch { }
                        Functions.AppLog.Warn("Workshop/Download", $"app {appId} item {sid}: SteamCMD timed out (90s), killed.");
                        return (false, "SteamCMD did not answer within 90s — the cached Steam session has most likely expired. Log in once from Mods ▸ Steam login.");
                    }
                }
                string content = ContentPath(appId, sid);
                bool ok = Directory.Exists(content);
                Functions.AppLog.Info("Workshop/Download", $"app {appId} item {sid} -> {(ok ? "OK " + content : "failed")}");
                if (ok) { return (true, "Downloaded: " + content); }

                // Diagnose the common failure: anonymous SteamCMD can't decrypt content for games that
                // require ownership (Palworld, ARK…). Give an actionable message instead of a bare "failed".
                string outText = sb.ToString();
                if (outText.IndexOf("Missing decryption key", StringComparison.OrdinalIgnoreCase) >= 0
                    || outText.IndexOf("No subscription", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return (false, "OWNERSHIP: Steam blocks anonymous Workshop downloads for this game — it needs a Steam account that OWNS it. (Games like Project Zomboid / Garry's Mod download mods by themselves at server start; for Palworld/ARK you need an owning Steam login.)");
                }
                if (outText.IndexOf("Rate Limit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return (false, "Steam rate-limited the download. Wait a minute and retry.");
                }
                return (false, "SteamCMD finished but content is missing. See the SteamCMD log.");
            }
            catch (Exception ex)
            {
                Functions.AppLog.Warn("Workshop/Download", ex.Message);
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Palworld activation: copies each ENABLED downloaded item into
        /// &lt;serverfiles&gt;\Pal\Binaries\Win64\Mods\Workshop and (re)writes Mods\PalModSettings.ini
        /// (bGlobalEnableMod + one ActiveModList=&lt;PackageName&gt; per enabled mod; PackageName read
        /// from each mod's Info.json, NOT the id/folder). Disabled mods are simply left out. Restart required.
        /// </summary>
        public static string ApplyPalworld(string serverFiles, IEnumerable<WorkshopEntry> entries)
        {
            try
            {
                // Palworld deploys its own mods. The server reads <server>\Mods\Workshop\<id>\Info.json,
                // enables whatever PalModSettings.ini lists, and on the next start writes
                // Mods\ManagedMods\<PackageName>\InstallManifest.json while copying the files where they
                // belong (~WorkshopMods, LogicMods, Mods\NativeMods\UE4SS). That manifest is also what the
                // server ANNOUNCES to joining clients, so a client rejects a mod the server never staged.
                //
                // This used to stage into Pal\Binaries\Win64\Mods and inject its own UE4SS through a
                // dwmapi.dll proxy. The server never looked there, and that private UE4SS collided with the
                // one Palworld deploys itself: two runtimes in one process, an access violation a few
                // seconds after every start. Stage where the game looks, and let the game do the rest.
                string modsDir = Path.Combine(serverFiles ?? string.Empty, "Mods");
                string workshopDir = Path.Combine(modsDir, "Workshop");
                Directory.CreateDirectory(workshopDir);

                var pkgs = new List<string>();
                int copied = 0, noInfo = 0, notDownloaded = 0;
                foreach (var e in entries.Where(x => x.Enabled))
                {
                    string sid = Digits(e.Id);
                    string src = ContentPath(1623730, sid);
                    if (!Directory.Exists(src)) { notDownloaded++; continue; }

                    string dst = Path.Combine(workshopDir, sid);
                    CopyDir(src, dst);
                    copied++;

                    string info = FindFile(dst, "Info.json");
                    if (info == null) { noInfo++; continue; }
                    try
                    {
                        var j = JObject.Parse(File.ReadAllText(info));
                        string pkg = j.Value<string>("package_name") ?? j.Value<string>("PackageName") ?? j.Value<string>("packageName") ?? j.Value<string>("mod_name");
                        if (!string.IsNullOrWhiteSpace(pkg)) { pkgs.Add(pkg); }
                    }
                    catch { noInfo++; }
                }

                // A disabled mod is dropped from ActiveModList and its staged folder removed, so the server
                // stops redeploying it. Its ManagedMods entry is left alone: the server prunes that itself.
                foreach (var e in entries.Where(x => !x.Enabled))
                {
                    try
                    {
                        string stale = Path.Combine(workshopDir, Digits(e.Id));
                        if (Directory.Exists(stale)) { Directory.Delete(stale, true); }
                    }
                    catch (Exception ex) { Functions.AppLog.Warn("Palworld/Deploy", ex.Message); }
                }

                var sb = new StringBuilder();
                sb.AppendLine("[PalModSettings]");
                sb.AppendLine("bGlobalEnableMod=" + (pkgs.Count > 0 ? "True" : "False"));
                foreach (var p in pkgs.Distinct()) { sb.AppendLine("ActiveModList=" + p); }
                File.WriteAllText(Path.Combine(modsDir, "PalModSettings.ini"), sb.ToString());

                string msg = $"{pkgs.Count} mod(s) staged in Mods\\Workshop — restart the server, it deploys and announces them.";
                if (notDownloaded > 0) { msg += $" WARN {notDownloaded} not downloaded yet (click Download)."; }
                if (noInfo > 0) { msg += $" WARN {noInfo} without a readable Info.json."; }
                return msg;
            }
            catch (Exception ex) { return "Activation error: " + ex.Message; }
        }

        // ---- UE4SS install + per-InstallRule distribution (Palworld) ----

        /// <summary>Distribute one InstallRule of a mod to its runtime target folder.</summary>
        private static void DeployRule(string modDir, string pkg, JToken rule, string win64, string serverFiles, List<string> luaPkgs)
        {
            try
            {
                string type = ((string)rule["Type"] ?? "").Trim();
                string ueDir = Path.Combine(win64, "ue4ss");
                string ueMods = Path.Combine(ueDir, "Mods");
                string logicMods = Path.Combine(serverFiles ?? "", "Pal", "Content", "Paks", "LogicMods");
                string paksMods = Path.Combine(serverFiles ?? "", "Pal", "Content", "Paks", "~mods");
                var targets = (rule["Targets"] as JArray)?.Select(t => (string)t).Where(t => t != null).ToList() ?? new List<string> { "." };

                foreach (var tRaw in targets)
                {
                    string rel = (tRaw ?? ".").Replace('/', '\\').TrimStart('.', '\\');
                    string srcDir = string.IsNullOrEmpty(rel) ? modDir : Path.Combine(modDir, rel);
                    if (!Directory.Exists(srcDir)) { continue; }
                    switch (type)
                    {
                        case "LogicMods": CopyPaks(srcDir, logicMods); break;              // BPModLoaderMod loads these
                        case "Paks": CopyPaks(srcDir, paksMods); break;                    // UE auto-mounts ~mods
                        case "Lua":                                                         // ue4ss\Mods\<pkg>\<Scripts>
                            string leaf = Path.GetFileName(srcDir.TrimEnd('\\'));
                            CopyDir(srcDir, Path.Combine(ueMods, pkg, leaf));
                            try { File.WriteAllText(Path.Combine(ueMods, pkg, "enabled.txt"), ""); } catch { }
                            if (!luaPkgs.Contains(pkg)) { luaPkgs.Add(pkg); }
                            break;
                        case "PalSchema": CopyDir(srcDir, Path.Combine(ueMods, "PalSchema", "mods", pkg)); break;
                        case "UE4SS":                                                       // keep GitHub UE4SS.dll,
                            string mvl = Path.Combine(srcDir, "MemberVariableLayout.ini");  // only overlay Palworld offsets
                            if (File.Exists(mvl)) { File.Copy(mvl, Path.Combine(ueDir, "MemberVariableLayout.ini"), true); }
                            break;
                    }
                }
            }
            catch (Exception ex) { Functions.AppLog.Warn("Palworld/Deploy", ex.Message); }
        }

        /// <summary>Copy only pak-family files (flat) from a source tree into a destination folder.</summary>
        // Mirror of DeployRule: takes back out what a mod had put in place, so unchecking it really stops it
        // from loading. Lua mods lose their enabled.txt (the folder is kept, so re-enabling costs nothing);
        // pak-based mods lose the files they shipped, matched by name so a shared folder is never emptied.
        private static void UndeployRule(string modDir, string pkg, JToken rule, string win64, string serverFiles)
        {
            try
            {
                string type = ((string)rule["Type"] ?? "").Trim();
                string ueMods = Path.Combine(win64, "ue4ss", "Mods");
                string logicMods = Path.Combine(serverFiles ?? "", "Pal", "Content", "Paks", "LogicMods");
                string paksMods = Path.Combine(serverFiles ?? "", "Pal", "Content", "Paks", "~mods");
                var targets = (rule["Targets"] as JArray)?.Select(t => (string)t).Where(t => t != null).ToList() ?? new List<string> { "." };

                foreach (var tRaw in targets)
                {
                    string rel = (tRaw ?? ".").Replace('/', '\\').TrimStart('.', '\\');
                    string srcDir = string.IsNullOrEmpty(rel) ? modDir : Path.Combine(modDir, rel);
                    switch (type)
                    {
                        case "LogicMods": RemovePaks(srcDir, logicMods); break;
                        case "Paks": RemovePaks(srcDir, paksMods); break;
                        case "Lua":
                            try { File.Delete(Path.Combine(ueMods, pkg ?? string.Empty, "enabled.txt")); } catch { }
                            break;
                        case "PalSchema":
                            try
                            {
                                string ps = Path.Combine(ueMods, "PalSchema", "mods", pkg ?? string.Empty);
                                if (Directory.Exists(ps)) { Directory.Delete(ps, true); }
                            }
                            catch { }
                            break;
                    }
                }
            }
            catch (Exception ex) { Functions.AppLog.Warn("Palworld/Undeploy", ex.Message); }
        }

        // Removes from dst only the pak files this mod had shipped (same file names as in src).
        private static void RemovePaks(string src, string dst)
        {
            if (!Directory.Exists(src) || !Directory.Exists(dst)) { return; }
            foreach (var f in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".pak" && ext != ".utoc" && ext != ".ucas" && ext != ".sig") { continue; }
                try
                {
                    string victim = Path.Combine(dst, Path.GetFileName(f));
                    if (File.Exists(victim)) { File.Delete(victim); }
                }
                catch { }
            }
        }

        private static void DisableUe4ssMods(string win64, IEnumerable<string> pkgs)
        {
            try
            {
                var wanted = pkgs.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
                if (wanted.Count == 0) { return; }
                string mt = Path.Combine(win64, "ue4ss", "Mods", "mods.txt");
                if (!File.Exists(mt)) { return; }
                var lines = File.ReadAllLines(mt).ToList();
                bool touched = false;
                foreach (var name in wanted)
                {
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (Regex.IsMatch(lines[i], @"^\s*" + Regex.Escape(name) + @"\s*:", RegexOptions.IgnoreCase))
                        {
                            lines[i] = name + " : 0";
                            touched = true;
                        }
                    }
                    try { File.Delete(Path.Combine(win64, "ue4ss", "Mods", name, "enabled.txt")); } catch { }
                }
                if (touched) { File.WriteAllLines(mt, lines); }
            }
            catch (Exception ex) { Functions.AppLog.Warn("Palworld/UE4SS", ex.Message); }
        }

        private static void CopyPaks(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".pak" || ext == ".utoc" || ext == ".ucas" || ext == ".sig")
                {
                    File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
                }
            }
        }

        /// <summary>Ensure UE4SS is installed as a dwmapi.dll proxy next to the server exe. Downloads the
        /// Palworld-capable experimental release from GitHub on first use; no-op if already present.</summary>
        private static bool EnsureUe4ss(string win64, out string note)
        {
            note = null;
            string proxy = Path.Combine(win64, "dwmapi.dll");
            string ueDll = Path.Combine(win64, "ue4ss", "UE4SS.dll");
            if (File.Exists(proxy) && File.Exists(ueDll)) { note = "UE4SS already installed"; return true; }
            try
            {
                string url = ResolveUe4ssAssetUrl();
                if (url == null) { note = "⚠ UE4SS asset not found on GitHub"; return false; }
                string tmp = Path.Combine(Path.GetTempPath(), "wgsm_ue4ss_" + Guid.NewGuid().ToString("N") + ".zip");
                DownloadFile(url, tmp);
                using (var za = ZipFile.OpenRead(tmp))
                {
                    foreach (var e in za.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) { continue; } // directory entry
                        string entryRel = e.FullName.Replace('/', '\\');
                        string dest = null;
                        if (entryRel.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase)) { dest = proxy; }
                        else if (entryRel.StartsWith("ue4ss\\", StringComparison.OrdinalIgnoreCase)) { dest = Path.Combine(win64, entryRel); }
                        if (dest == null) { continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        e.ExtractToFile(dest, true);
                    }
                }
                try { File.Delete(tmp); } catch { }
                // Headless server: no GUI console window (avoids the SECURE-CRT style crash), keep the file log.
                SetIniKey(Path.Combine(win64, "ue4ss", "UE4SS-settings.ini"), "GuiConsoleEnabled", "0");
                bool ok = File.Exists(proxy) && File.Exists(ueDll);
                note = ok ? "UE4SS installed (GitHub)" : "⚠ UE4SS install incomplete";
                return ok;
            }
            catch (Exception ex) { note = "⚠ UE4SS install failed: " + ex.Message; Functions.AppLog.Warn("Palworld/UE4SS", ex.Message); return false; }
        }

        /// <summary>Enable BPModLoaderMod (+ generic helper) and every deployed Lua mod in ue4ss\Mods\mods.txt.</summary>
        private static void EnableUe4ssMods(string win64, IEnumerable<string> luaPkgs)
        {
            try
            {
                string mt = Path.Combine(win64, "ue4ss", "Mods", "mods.txt");
                if (!File.Exists(mt)) { return; }
                var lines = File.ReadAllLines(mt).ToList();
                void Ensure(string name)
                {
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (Regex.IsMatch(lines[i], @"^\s*" + Regex.Escape(name) + @"\s*:", RegexOptions.IgnoreCase)) { lines[i] = name + " : 1"; return; }
                    }
                    int idx = lines.FindIndex(l => l.TrimStart().StartsWith(";"));
                    if (idx < 0) { idx = lines.Count; }
                    lines.Insert(idx, name + " : 1");
                }
                Ensure("BPModLoaderMod");
                Ensure("BPML_GenericFunctions");
                foreach (var p in luaPkgs.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()) { Ensure(p); }
                File.WriteAllLines(mt, lines);
            }
            catch (Exception ex) { Functions.AppLog.Warn("Palworld/UE4SS", ex.Message); }
        }

        private static void SetIniKey(string path, string key, string val)
        {
            try
            {
                if (!File.Exists(path)) { return; }
                var lines = File.ReadAllLines(path).ToList();
                for (int i = 0; i < lines.Count; i++)
                {
                    var m = Regex.Match(lines[i], @"^(\s*" + Regex.Escape(key) + @"\s*=).*", RegexOptions.IgnoreCase);
                    if (m.Success) { lines[i] = m.Groups[1].Value + " " + val; File.WriteAllLines(path, lines); return; }
                }
            }
            catch { }
        }

        /// <summary>Resolve the download URL of the Palworld-capable UE4SS zip (experimental-latest, the
        /// UE4SS_v*.zip asset — not the zDEV/zCustom/zMap extras).</summary>
        private static string ResolveUe4ssAssetUrl()
        {
            string json = HttpGetString("https://api.github.com/repos/UE4SS-RE/RE-UE4SS/releases/tags/experimental-latest");
            var assets = JObject.Parse(json)["assets"] as JArray;
            if (assets == null) { return null; }
            foreach (var a in assets)
            {
                string name = (string)a["name"] ?? "";
                if (name.StartsWith("UE4SS_v", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    return (string)a["browser_download_url"];
                }
            }
            return null;
        }

        private static string HttpGetString(string url)
        {
            return Task.Run(async () =>
            {
                using (var h = new HttpClient())
                {
                    h.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsGSM");
                    h.Timeout = TimeSpan.FromSeconds(30);
                    return await h.GetStringAsync(url).ConfigureAwait(false);
                }
            }).GetAwaiter().GetResult();
        }

        private static void DownloadFile(string url, string dest)
        {
            Task.Run(async () =>
            {
                using (var h = new HttpClient())
                {
                    h.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsGSM");
                    h.Timeout = TimeSpan.FromMinutes(5);
                    var bytes = await h.GetByteArrayAsync(url).ConfigureAwait(false);
                    File.WriteAllBytes(dest, bytes);
                }
            }).GetAwaiter().GetResult();
        }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) { File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); }
            foreach (var d in Directory.GetDirectories(src)) { CopyDir(d, Path.Combine(dst, Path.GetFileName(d))); }
        }

        private static string FindFile(string root, string name)
        {
            try { foreach (var f in Directory.EnumerateFiles(root, name, SearchOption.AllDirectories)) { return f; } } catch { }
            return null;
        }

        /// <summary>
        /// Writes the list of enabled IDs into the game config (profile.ConfigFileRelative / ConfigKey /
        /// ConfigSection). Returns the summary. No-op if the profile has no config wiring.
        /// </summary>
        public static string ApplyToConfig(string serverFiles, ModProfile profile, IEnumerable<WorkshopEntry> entries)
        {
            if (profile == null || string.IsNullOrEmpty(profile.ConfigFileRelative) || string.IsNullOrEmpty(profile.ConfigKey))
            {
                return "No config wiring for this game (the server manages mods differently).";
            }
            string path = Path.Combine(serverFiles ?? "", profile.ConfigFileRelative);
            if (!File.Exists(path)) { return "Config file not found: " + path; }

            string value = string.Join(profile.ListSeparator, entries.Where(e => e.Enabled).Select(e => e.Id));
            var cf = ConfigEditor.ConfigFile.Load(path);
            cf.SetOrAdd(profile.ConfigSection, profile.ConfigKey, value);
            cf.Save();
            return $"{profile.ConfigKey} = {value}  -> written to {Path.GetFileName(path)} (backup .wgsmbak).";
        }
    }
}
