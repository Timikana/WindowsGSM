using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;

namespace WindowsGSM.GameServer
{
    // Black Mesa Dedicated Server — AppID 346680 (≠ client 362890). Moteur Source (srcds, -game bms). Query A2S.
    class BlackMesa : Engine.Source
    {
        private readonly Functions.ServerConfig _serverData;

        public string Error;
        public string Notice;

        public const string FullName = "Black Mesa Dedicated Server";
        public string StartPath = "srcds.exe";
        public bool AllowsEmbedConsole = false;
        public int PortIncrements = 1;
        public dynamic QueryMethod = new Query.A2S();

        public string Port = "27015";
        public string QueryPort = "27015";   // Source: A2S query on the game port
        public string Defaultmap = "dm_crossfire";
        public string Maxplayers = "32";
        public string Additional = string.Empty;

        public string AppId = "346680";

        public BlackMesa(Functions.ServerConfig serverData) : base(serverData)
        {
            _serverData = serverData;
        }

        public async void CreateServerCFG() { /* cfg in bms\cfg\server.cfg (optional) */ }

        public async Task<Process> Start()
        {
            string exe = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            if (!File.Exists(exe)) { Error = $"{Path.GetFileName(exe)} not found ({exe})"; return null; }

            string map = string.IsNullOrWhiteSpace(_serverData.ServerMap) ? Defaultmap : _serverData.ServerMap;
            string param = "-console -game bms";
            param += string.IsNullOrWhiteSpace(_serverData.ServerPort) ? string.Empty : $" -port {_serverData.ServerPort}";
            param += string.IsNullOrWhiteSpace(_serverData.ServerMaxPlayer) ? string.Empty : $" +maxplayers {_serverData.ServerMaxPlayer}";
            param += string.IsNullOrWhiteSpace(map) ? string.Empty : $" +map {map}";
            param += $" {_serverData.ServerParam}";

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

        public async Task Stop(Process p) { await Task.Run(() => { p.CloseMainWindow(); }); }

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
