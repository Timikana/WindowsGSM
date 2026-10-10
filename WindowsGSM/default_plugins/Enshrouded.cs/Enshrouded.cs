using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;
using WindowsGSM.GameServer.Query;

namespace WindowsGSM.Plugins
{
    public class Enshrouded : SteamCMDAgent
    {
        #region Win32 - Ctrl+C sent straight to the server's own console
        private const uint CTRL_C_EVENT = 0;
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint dwProcessId);
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)] private static extern bool FreeConsole();
        [DllImport("kernel32.dll")] private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);
        [DllImport("kernel32.dll")] private static extern bool SetConsoleCtrlHandler(IntPtr handlerRoutine, bool add);
        // AttachConsole is process-wide: never let two stops share the WindowsGSM process' console slot.
        private static readonly object ConsoleLock = new object();
        #endregion

        // - Plugin Details
        public Plugin Plugin = new Plugin
        {
            name = "WindowsGSM.Enshrouded", // WindowsGSM.XXXX
            author = "ohmcodes",
            description = "WindowsGSM plugin for supporting Enshrouded Dedicated Server",
            version = "1.2.0",
            url = "https://github.com/ohmcodes/WindowsGSM.Enshrouded", // Github repository link (Best practice)
            color = "#34c9eb" // Color Hex
        };

        // - Settings properties for SteamCMD installer
        public override bool loginAnonymous => true;
        public override string AppId => "2278520"; // Game server appId Steam

        // - Standard Constructor and properties
        public Enshrouded(ServerConfig serverData) : base(serverData) => base.serverData = _serverData = serverData;
        private readonly ServerConfig _serverData;
        public string Error, Notice;

        // - Game server Fixed variables
        public override string StartPath => "enshrouded_server.exe"; // Game server start path
        public string FullName = "Enshrouded Dedicated Server"; // Game server FullName
        public bool AllowsEmbedConsole = true;  // Does this server support output redirect?
        public int PortIncrements = 1; // This tells WindowsGSM how many ports should skip after installation
        public object QueryMethod = new A2S(); // Steam query answers on the query port

        // - Game server default values
        // The server only listens on its queryPort now (gamePort was removed from the game). The "game"
        // port below is kept so WindowsGSM's port allocation stays the same as before.
        public string Port = "15636";
        public string QueryPort = "15637"; // The port players use to join and the Steam query port.
        public string Defaultmap = "Dedicated"; // Not used by the game
        public string Maxplayers = "16"; // The game caps slotCount at 16
        public string Additional = ""; // The server reads no command-line options: everything is in enshrouded_server.json

        private const string ConfigFile = "enshrouded_server.json";
        private const int MaxSlots = 16;
        // A clean shutdown saves and compresses the world. Usually a few seconds, but it was measured at
        // 45+ s on a server pinned to one core: killing it while it closes the save container risks the
        // world, so the grace is generous.
        private const int StopGraceMs = 120000;

        // - Create a default cfg for the game server after installation
        // Only the keys WindowsGSM owns are written: on its first start the server completes the file with
        // its own defaults (game settings, user groups with random passwords, bans). That keeps new servers
        // in line with whatever the installed build expects (1.0 included) instead of a hand-made copy.
        public async void CreateServerCFG()
        {
            try { SyncConfig(); }
            catch (Exception e) { Error = $"Fail to write {ConfigFile}: {e.Message}"; }
        }

        /// <summary>
        /// Writes the settings WindowsGSM owns (name, ip, query port, slots) into enshrouded_server.json and
        /// keeps every other key exactly as it is. The game ignores command-line options, so this is the
        /// only way a port or name change made in WindowsGSM reaches the server.
        /// </summary>
        private void SyncConfig()
        {
            string path = ServerPath.GetServersServerFiles(_serverData.ServerID, ConfigFile);
            JObject cfg = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();

            cfg["name"] = _serverData.ServerName ?? "Enshrouded Server";
            if (cfg["saveDirectory"] == null) { cfg["saveDirectory"] = "./savegame"; }
            if (cfg["logDirectory"] == null) { cfg["logDirectory"] = "./logs"; }
            // WindowsGSM owns the address: its player counter queries ServerIP, so the server must listen
            // there. An address set by hand in the file while WindowsGSM says 0.0.0.0 makes the server
            // deaf to that query (it then binds one card only, not the local address WindowsGSM asks).
            string ip = (_serverData.ServerIP ?? string.Empty).Trim();
            cfg["ip"] = ip.Length > 0 ? ip : "0.0.0.0";
            if (int.TryParse(_serverData.ServerQueryPort, out int qp) && qp > 0) { cfg["queryPort"] = qp; }
            if (int.TryParse(_serverData.ServerMaxPlayer, out int slots)) { cfg["slotCount"] = Math.Max(1, Math.Min(MaxSlots, slots)); }
            // gamePort no longer exists in the game; drop a stale one left by older plugin versions.
            cfg.Remove("gamePort");

            string json = cfg.ToString(Formatting.Indented);
            if (File.Exists(path) && File.ReadAllText(path) == json) { return; }
            if (File.Exists(path)) { File.Copy(path, path + ".wgsmbak", true); }
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        // - Start server function, return its Process to WindowsGSM
        public async Task<Process> Start()
        {
            string shipExePath = ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            if (!File.Exists(shipExePath))
            {
                Error = $"{Path.GetFileName(shipExePath)} not found ({shipExePath})";
                return null;
            }

            try { SyncConfig(); }
            catch (Exception e) { Notice = $"{ConfigFile} not updated ({e.Message}); starting with the file as it is."; }

            var p = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = ServerPath.GetServersServerFiles(_serverData.ServerID),
                    FileName = shipExePath,
                    Arguments = (_serverData.ServerParam ?? string.Empty).Trim(),
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    // Mandatory: with a hidden console window MainWindowHandle stays 0, and WindowsGSM's start
                    // loop (wait for the window, then minimise it) spins forever - status stuck on "Starting",
                    // one CPU core burnt. CREATE_NO_WINDOW still gives the server a console, so Ctrl+C on stop
                    // keeps working.
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            // Set up Redirect Input and Output to WindowsGSM Console if EmbedConsole is on
            if (AllowsEmbedConsole)
            {
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                var serverConsole = new ServerConsole(_serverData.ServerID);
                p.OutputDataReceived += serverConsole.AddOutput;
                p.ErrorDataReceived += serverConsole.AddOutput;
            }

            try
            {
                p.Start();
                if (AllowsEmbedConsole)
                {
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                }
                return p;
            }
            catch (Exception e)
            {
                Error = e.Message;
                return null;
            }
        }

        // - Stop server function
        // Ctrl+C goes to the server's console directly (the old SendKeys path typed Ctrl+C into whatever
        // window had the focus), then we give the save time to finish before falling back to a kill.
        public async Task Stop(Process p)
        {
            await Task.Run(() =>
            {
                if (p == null || p.HasExited) { return; }
                if (!SendCtrlC(p, StopGraceMs) && !p.HasExited)
                {
                    try { p.Kill(); } catch { }
                }
            });
        }

        private static bool SendCtrlC(Process p, int graceMs)
        {
            lock (ConsoleLock)
            {
                FreeConsole(); // WindowsGSM has no console of its own; make sure the slot is free
                if (!AttachConsole((uint)p.Id)) { return false; }
                SetConsoleCtrlHandler(IntPtr.Zero, true); // ignore the event ourselves
                try
                {
                    if (!GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)) { return false; }
                    return p.WaitForExit(graceMs);
                }
                finally
                {
                    FreeConsole();
                    SetConsoleCtrlHandler(IntPtr.Zero, false);
                }
            }
        }

        public async Task<Process> Install()
        {
            var steamCMD = new Installer.SteamCMD();
            Process p = await steamCMD.Install(_serverData.ServerID, string.Empty, AppId, true, loginAnonymous);
            Error = steamCMD.Error;
            return p;
        }

        public async Task<Process> Update(bool validate = false, string custom = null)
        {
            var (p, error) = await Installer.SteamCMD.UpdateEx(serverData.ServerID, AppId, validate, custom: custom, loginAnonymous: loginAnonymous);
            Error = error;
            await Task.Run(() => { p.WaitForExit(); });
            return p;
        }

        public bool IsInstallValid()
        {
            return File.Exists(ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath));
        }

        public bool IsImportValid(string path)
        {
            string importPath = Path.Combine(path, StartPath);
            Error = $"Invalid Path! Fail to find {Path.GetFileName(StartPath)}";
            return File.Exists(importPath);
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
