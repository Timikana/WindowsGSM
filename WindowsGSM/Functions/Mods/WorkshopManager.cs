using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
                    await Task.Run(() => p.WaitForExit());
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
                string modsDir = Path.Combine(serverFiles ?? string.Empty, "Pal", "Binaries", "Win64", "Mods");
                string workshopDir = Path.Combine(modsDir, "Workshop");
                Directory.CreateDirectory(workshopDir);

                var pkgs = new List<string>();
                int copied = 0, noInfo = 0, notServer = 0, notDownloaded = 0;
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
                        var j = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(info));
                        string pkg = j.Value<string>("package_name") ?? j.Value<string>("PackageName") ?? j.Value<string>("packageName") ?? j.Value<string>("mod_name");
                        // InstallRule may be an object OR an array of rules; the mod is server-usable if any
                        // rule carries "IsServer": true (assume ok when the field is absent entirely).
                        bool isServer = true;
                        var ruleTok = j["InstallRule"];
                        if (ruleTok is Newtonsoft.Json.Linq.JArray arr)
                        {
                            isServer = !arr.Any(x => x["IsServer"] != null) || arr.Any(x => (bool?)x["IsServer"] == true);
                        }
                        else if (ruleTok is Newtonsoft.Json.Linq.JObject o && o["IsServer"] != null)
                        {
                            isServer = o.Value<bool?>("IsServer") == true;
                        }
                        if (!isServer) { notServer++; }
                        if (!string.IsNullOrWhiteSpace(pkg)) { pkgs.Add(pkg); }
                        else { noInfo++; }
                    }
                    catch { noInfo++; }
                }

                var sb = new StringBuilder();
                sb.AppendLine("[PalModSettings]");
                sb.AppendLine("bGlobalEnableMod=" + (pkgs.Count > 0 ? "true" : "false"));
                foreach (var p in pkgs.Distinct()) { sb.AppendLine("ActiveModList=" + p); }
                File.WriteAllText(Path.Combine(modsDir, "PalModSettings.ini"), sb.ToString());

                string msg = $"{pkgs.Count} mod(s) activated — restart the server to apply.";
                if (notDownloaded > 0) { msg += $" ⚠ {notDownloaded} not downloaded yet (click Download)."; }
                if (noInfo > 0) { msg += $" ⚠ {noInfo} without a readable PackageName."; }
                if (notServer > 0) { msg += $" ⚠ {notServer} flagged client-only."; }
                return msg;
            }
            catch (Exception ex) { return "Activation error: " + ex.Message; }
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
