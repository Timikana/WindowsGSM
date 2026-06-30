using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace WindowsGSM.Functions.PortForward
{
    public class ServerPortForward
    {
        public bool Enabled { get; set; } = true;       // opening for this server (subject to the global master)
        public List<PortMapping> Ports { get; set; } = new List<PortMapping>();
    }

    /// <summary>
    /// UPnP auto port-forwarding configuration. Stored in &lt;WGSM&gt;\configs\portforward.json.
    /// - <see cref="Enabled"/> = MASTER switch: if false, nothing is opened.
    /// - Per server: enable flag + list of ports with on/off checkbox (RCON suggested but off).
    /// </summary>
    public class PortForwardConfig
    {
        public bool Enabled { get; set; } = false;                  // master OFF by default (opt-in)
        public Dictionary<string, ServerPortForward> Servers { get; set; } = new Dictionary<string, ServerPortForward>();

        private static string FilePath => ServerPath.Get(ServerPath.FolderName.Configs, "portforward.json");

        public static PortForwardConfig Load()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) { var def = new PortForwardConfig(); def.Save(); return def; }
                var cfg = JsonConvert.DeserializeObject<PortForwardConfig>(File.ReadAllText(path)) ?? new PortForwardConfig();
                if (cfg.Servers == null) { cfg.Servers = new Dictionary<string, ServerPortForward>(); }
                return cfg;
            }
            catch (Exception e)
            {
                AppLog.Warn("PortForwardConfig/Load", e.Message);
                return new PortForwardConfig();
            }
        }

        public void Save()
        {
            try
            {
                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception e)
            {
                AppLog.Warn("PortForwardConfig/Save", e.Message);
            }
        }

        /// <summary>
        /// Gets a server's config, creating/completing it from the suggestions: adds the
        /// missing suggested ports WITHOUT overwriting the on/off choices already made by the user.
        /// </summary>
        public ServerPortForward EnsureServer(string serverId, List<PortMapping> suggestions)
        {
            if (!Servers.TryGetValue(serverId, out var spf))
            {
                spf = new ServerPortForward { Enabled = true, Ports = new List<PortMapping>() };
                Servers[serverId] = spf;
            }

            bool changed = false;
            foreach (var sug in suggestions)
            {
                if (!spf.Ports.Any(p => p.Port == sug.Port && p.Protocol == sug.Protocol))
                {
                    spf.Ports.Add(sug);
                    changed = true;
                }
            }
            if (changed) { Save(); }
            return spf;
        }
    }
}
