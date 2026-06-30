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
    /// Téléchargement de mods Steam Workshop via SteamCMD et câblage dans la config du jeu.
    /// Download = best-effort (SteamCMD anonyme) ; câblage = écrit la liste d'IDs activés dans le
    /// fichier/clé du profil (ex. ARK ActiveMods= dans GameUserSettings.ini [ServerSettings]).
    /// </summary>
    public static class WorkshopManager
    {
        private static string SteamCmdExe() => Path.Combine(Functions.ServerPath.GetBin("steamcmd"), "steamcmd.exe");

        /// <summary>Chiffres uniquement (anti-injection d'arguments SteamCMD).</summary>
        private static string Digits(string s) => new string((s ?? "").Where(char.IsDigit).ToArray());

        /// <summary>Dossier où SteamCMD dépose le contenu Workshop téléchargé.</summary>
        public static string ContentPath(int appId, string id)
            => Path.Combine(Functions.ServerPath.GetBin("steamcmd"), "steamapps", "workshop", "content", appId.ToString(), Digits(id));

        /// <summary>Télécharge un item Workshop via SteamCMD anonyme. Retourne (ok, message).</summary>
        public static async Task<(bool ok, string message)> DownloadAsync(int appId, string id, Action<string> log = null)
        {
            string exe = SteamCmdExe();
            if (!File.Exists(exe)) { return (false, "steamcmd.exe introuvable (" + exe + ")."); }
            string sid = Digits(id);
            if (string.IsNullOrEmpty(sid) || appId <= 0) { return (false, "ID/AppID invalide."); }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"+login anonymous +workshop_download_item {appId} {sid} +quit",
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
                Functions.AppLog.Info("Workshop/Download", $"app {appId} item {sid} -> {(ok ? "OK " + content : "échec")}");
                return ok ? (true, "Téléchargé : " + content) : (false, "SteamCMD terminé mais contenu absent. Voir log.");
            }
            catch (Exception ex)
            {
                Functions.AppLog.Warn("Workshop/Download", ex.Message);
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Écrit la liste d'IDs activés dans la config du jeu (profil.ConfigFileRelative / ConfigKey /
        /// ConfigSection). Retourne le résumé. No-op si le profil n'a pas de câblage de config.
        /// </summary>
        public static string ApplyToConfig(string serverFiles, ModProfile profile, IEnumerable<WorkshopEntry> entries)
        {
            if (profile == null || string.IsNullOrEmpty(profile.ConfigFileRelative) || string.IsNullOrEmpty(profile.ConfigKey))
            {
                return "Pas de câblage de config pour ce jeu (le serveur gère les mods autrement).";
            }
            string path = Path.Combine(serverFiles ?? "", profile.ConfigFileRelative);
            if (!File.Exists(path)) { return "Fichier de config introuvable : " + path; }

            string value = string.Join(profile.ListSeparator, entries.Where(e => e.Enabled).Select(e => e.Id));
            var cf = ConfigEditor.ConfigFile.Load(path);
            cf.SetOrAdd(profile.ConfigSection, profile.ConfigKey, value);
            cf.Save();
            return $"{profile.ConfigKey} = {value}  → écrit dans {Path.GetFileName(path)} (backup .wgsmbak).";
        }
    }
}
