using System.Collections.Generic;
using System.Threading.Tasks;

namespace WindowsGSM.Functions.PortForward
{
    /// <summary>
    /// Auto port-forwarding orchestration: opens a server's ports at start, closes them
    /// at stop, and cleans everything up when the app closes. Respects the global master switch and
    /// the per-server enable flag, and opens ONLY the checked ports. Best-effort: a network failure is
    /// logged (AppLog) without blocking the game server.
    /// </summary>
    public static class PortForwardManager
    {
        // Homemade UPnP backend (no external dependency). Replaceable if needed (tests, etc.).
        public static INatBackend Backend { get; set; } = new UpnpNatBackend();

        private static readonly object _lock = new object();
        // serverId -> ports actually opened by us (so we can close them cleanly).
        private static readonly Dictionary<string, List<PortMapping>> _active = new Dictionary<string, List<PortMapping>>();

        public static async Task OpenForServerAsync(string serverId, string gameFullName, string serverPort, string serverQueryPort)
        {
            var cfg = PortForwardConfig.Load();

            // Always populate the suggested list in portforward.json ("advisor" role: lets you
            // see/copy the ports to open — e.g. manual forward on OPNsense — even if UPnP is off).
            var suggestions = PortResolver.Suggest(gameFullName, serverPort, serverQueryPort);
            var spf = cfg.EnsureServer(serverId, suggestions);

            if (!cfg.Enabled) { return; }  // master switch: nothing opens, but the list stays visible
            if (!spf.Enabled) { return; }

            if (!await Backend.IsAvailableAsync())
            {
                AppLog.Warn("PortForward", $"#{serverId}: no UPnP gateway available. Enable UPnP on the router or forward the ports manually.");
                return;
            }

            var opened = new List<PortMapping>();
            foreach (var pm in spf.Ports)
            {
                if (!pm.Enabled) { continue; }
                try
                {
                    if (await Backend.MapAsync(pm.Port, pm.Protocol, $"WindowsGSM #{serverId} {pm.Label}".Trim()))
                    {
                        opened.Add(pm);
                    }
                }
                catch (System.Exception e)
                {
                    AppLog.Warn("PortForward", $"#{serverId} opening {pm.Key}: {e.Message}");
                }
            }

            if (opened.Count > 0)
            {
                lock (_lock) { _active[serverId] = opened; }
                AppLog.Warn("PortForward", $"#{serverId}: {opened.Count} port(s) opened via UPnP.");
            }
        }

        public static async Task CloseForServerAsync(string serverId)
        {
            List<PortMapping> toClose;
            lock (_lock)
            {
                if (!_active.TryGetValue(serverId, out toClose)) { return; }
                _active.Remove(serverId);
            }

            foreach (var pm in toClose)
            {
                try { await Backend.UnmapAsync(pm.Port, pm.Protocol); }
                catch (System.Exception e) { AppLog.Warn("PortForward", $"#{serverId} closing {pm.Key}: {e.Message}"); }
            }
        }

        /// <summary>Call when the app closes: re-closes all the ports we opened.</summary>
        public static async Task CleanupAllAsync()
        {
            List<string> ids;
            lock (_lock) { ids = new List<string>(_active.Keys); }
            foreach (var id in ids) { await CloseForServerAsync(id); }
        }
    }
}
