using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;

namespace WindowsGSM.GameServer
{
    // Myth of Empires — AppID serveur privé 1794810 (SteamDB). UE (base type ARK), query A2S.
    // ⚠ Lancer d'abord PrivateServerTool.exe pour générer la config avant le 1er démarrage.
    class MythOfEmpires : Engine.UnrealEngine
    {
        private readonly Functions.ServerConfig _serverData;

        public string Error;
        public string Notice = "Lance d'abord PrivateServerTool.exe (dossier PrivateServerTool) pour générer la config avant de démarrer.";

        public const string FullName = "Myth of Empires Dedicated Server";
        public string StartPath = @"WindowsPrivateServer\MOE\Binaries\Win64\MOEServer.exe";
        public bool AllowsEmbedConsole = false;
        public int PortIncrements = 2;
        public dynamic QueryMethod = new Query.A2S();

        public string Port = "1188";
        public string QueryPort = "1288";
        public string Defaultmap = "";
        public string Maxplayers = "50";
        public string Additional = string.Empty;

        public string AppId = "1794810";

        public MythOfEmpires(Functions.ServerConfig serverData)
        {
            _serverData = serverData;
        }

        public async void CreateServerCFG() { /* via PrivateServerTool.exe au 1er lancement */ }

        public async Task<Process> Start()
        {
            string exe = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            if (!File.Exists(exe)) { Error = $"{Path.GetFileName(exe)} introuvable ({exe}). Lance PrivateServerTool.exe d'abord."; return null; }

            string param = string.Empty;
            param += string.IsNullOrWhiteSpace(_serverData.ServerPort) ? string.Empty : $" -Port={_serverData.ServerPort}";
            param += string.IsNullOrWhiteSpace(_serverData.ServerQueryPort) ? string.Empty : $" -QueryPort={_serverData.ServerQueryPort}";
            param += $" {_serverData.ServerParam} -log";

            Process p = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID),
                    FileName = exe,
                    Arguments = param.Trim(),
                    WindowStyle = ProcessWindowStyle.Minimized,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };
            p.Start();
            return p;
        }

        public async Task Stop(Process p) { await Task.Run(() => { p.Kill(); }); }

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

        public bool IsInstallValid() => File.Exists(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath));

        public bool IsImportValid(string path)
        {
            Error = $"Invalid Path! Fail to find {Path.GetFileName(StartPath)}";
            return File.Exists(Path.Combine(path, StartPath));
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
