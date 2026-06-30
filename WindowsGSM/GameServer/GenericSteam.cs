using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WindowsGSM.GameServer
{
    /// <summary>
    /// GENERIC Steam dedicated server: any AppID, without a dedicated plugin. The profile
    /// (appid / executable / arguments / name) is resolved from WindowsGSM/SteamAppInfo at the time
    /// of adding (cf. Functions.SteamApps) and stored in configs/wgsm-generic.json per server.
    /// </summary>
    class GenericSteam
    {
        private readonly Functions.ServerConfig _serverData;

        public string Error;
        public string Notice;

        public const string FullName = "Steam Dedicated Server (Generic)";
        public string StartPath = string.Empty;            // resolved exe (profile)
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public dynamic QueryMethod = new Query.A2S();

        public string Port = "27015";
        public string QueryPort = "27015";
        public string Defaultmap = string.Empty;
        public string Maxplayers = "16";
        public string Additional = string.Empty;

        public string AppId = string.Empty;

        private string _launchArgs = string.Empty;

        public GenericSteam(Functions.ServerConfig serverData)
        {
            _serverData = serverData;
            LoadProfile();
        }

        private void LoadProfile()
        {
            try
            {
                string path = Functions.ServerPath.GetServersConfigs(_serverData.ServerID, "wgsm-generic.json");
                if (!File.Exists(path)) { return; }
                var j = JObject.Parse(File.ReadAllText(path));
                AppId = j.Value<string>("appid") ?? string.Empty;
                StartPath = j.Value<string>("exe") ?? string.Empty;
                _launchArgs = j.Value<string>("args") ?? string.Empty;
            }
            catch { /* profile missing/unreadable -> empty fields */ }
        }

        /// <summary>Writes a server's generic profile (called by the add dialog, BEFORE the install).</summary>
        public static void SaveProfile(string serverId, string appid, string name, string exe, string args)
        {
            var j = new JObject { ["appid"] = appid, ["name"] = name, ["exe"] = exe, ["args"] = args };
            string path = Functions.ServerPath.GetServersConfigs(serverId, "wgsm-generic.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, j.ToString());
        }

        public async void CreateServerCFG()
        {
            await Task.CompletedTask; // no standardized config file for a generic server
        }

        public async Task<Process> Start()
        {
            string workingDir = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID);
            if (string.IsNullOrEmpty(StartPath))
            {
                Error = "Generic profile missing (executable not resolved). Recreate the server via add by AppID.";
                return null;
            }

            string exePath = Path.Combine(workingDir, StartPath);
            if (!File.Exists(exePath))
            {
                Error = $"{StartPath} not found ({exePath})";
                return null;
            }

            string param = $"{_launchArgs} {_serverData.ServerParam}".Trim();

            // .bat/.cmd script -> via cmd.exe; otherwise direct executable.
            bool isBatch = StartPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                        || StartPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            string fileName = isBatch ? "cmd.exe" : exePath;
            string arguments = isBatch ? $"/c \"{exePath}\" {param}" : param;

            WindowsFirewall firewall = new WindowsFirewall(StartPath, exePath);
            if (!await firewall.IsRuleExist())
            {
                await firewall.AddRule();
            }

            Process p;
            if (!AllowsEmbedConsole)
            {
                p = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = workingDir,
                        FileName = fileName,
                        Arguments = arguments,
                        WindowStyle = ProcessWindowStyle.Minimized
                    },
                    EnableRaisingEvents = true
                };
                p.Start();
            }
            else
            {
                p = new Process
                {
                    StartInfo =
                    {
                        WorkingDirectory = workingDir,
                        FileName = fileName,
                        Arguments = arguments,
                        WindowStyle = ProcessWindowStyle.Minimized,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    },
                    EnableRaisingEvents = true
                };
                var serverConsole = new Functions.ServerConsole(_serverData.ServerID);
                p.OutputDataReceived += serverConsole.AddOutput;
                p.ErrorDataReceived += serverConsole.AddOutput;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }

            return p;
        }

        public async Task Stop(Process p)
        {
            await Task.Run(() => { try { p.CloseMainWindow(); } catch { } });
            for (int i = 0; i < 15 && !p.HasExited; i++) { await Task.Delay(1000); }
            if (!p.HasExited) { try { p.Kill(); } catch { } }
        }

        public async Task<Process> Install()
        {
            var steamCMD = new Installer.SteamCMD();
            Process p = await steamCMD.Install(_serverData.ServerID, string.Empty, AppId);
            Error = steamCMD.Error;
            return p;
        }

        public async Task<Process> Update(bool validate = false, string custom = null)
        {
            var (p, error) = await Installer.SteamCMD.UpdateEx(_serverData.ServerID, AppId, validate, custom: custom);
            Error = error;
            return p;
        }

        public bool IsInstallValid()
        {
            return !string.IsNullOrEmpty(StartPath)
                && File.Exists(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath));
        }

        public bool IsImportValid(string path)
        {
            Error = "Import not supported for the generic server: use \"Add a Steam server (AppID)\".";
            return false;
        }

        public string GetLocalBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return steamCMD.GetLocalBuild(_serverData.ServerID, AppId);
        }

        public async Task<string> GetRemoteBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return await steamCMD.GetRemoteBuild(AppId);
        }
    }
}
