using System.Collections.Generic;
using System.Threading.Tasks;

namespace WindowsGSM.Functions.PortForward
{
    /// <summary>
    /// Orchestration de l'auto port-forwarding : ouvre les ports d'un serveur au démarrage, les ferme
    /// à l'arrêt, et nettoie tout à la fermeture de l'appli. Respecte l'interrupteur maître global et
    /// l'activation par serveur, et n'ouvre QUE les ports cochés. Best-effort : un échec réseau est
    /// journalisé (AppLog) sans bloquer le serveur de jeu.
    /// </summary>
    public static class PortForwardManager
    {
        // Backend UPnP maison (aucune dépendance externe). Remplaçable si besoin (tests, etc.).
        public static INatBackend Backend { get; set; } = new UpnpNatBackend();

        private static readonly object _lock = new object();
        // serverId -> ports réellement ouverts par nous (pour pouvoir les refermer proprement).
        private static readonly Dictionary<string, List<PortMapping>> _active = new Dictionary<string, List<PortMapping>>();

        public static async Task OpenForServerAsync(string serverId, string gameFullName, string serverPort, string serverQueryPort)
        {
            var cfg = PortForwardConfig.Load();

            // Toujours alimenter la liste suggérée dans portforward.json (rôle "conseiller" : permet de
            // voir/recopier les ports à ouvrir — ex. forward manuel sur OPNsense — même si l'UPnP est off).
            var suggestions = PortResolver.Suggest(gameFullName, serverPort, serverQueryPort);
            var spf = cfg.EnsureServer(serverId, suggestions);

            if (!cfg.Enabled) { return; }  // interrupteur maître : rien ne s'ouvre, mais la liste reste visible
            if (!spf.Enabled) { return; }

            if (!await Backend.IsAvailableAsync())
            {
                AppLog.Warn("PortForward", $"#{serverId} : pas de passerelle UPnP disponible. Active l'UPnP sur la box ou forward les ports manuellement.");
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
                    AppLog.Warn("PortForward", $"#{serverId} ouverture {pm.Key} : {e.Message}");
                }
            }

            if (opened.Count > 0)
            {
                lock (_lock) { _active[serverId] = opened; }
                AppLog.Warn("PortForward", $"#{serverId} : {opened.Count} port(s) ouvert(s) via UPnP.");
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
                catch (System.Exception e) { AppLog.Warn("PortForward", $"#{serverId} fermeture {pm.Key} : {e.Message}"); }
            }
        }

        /// <summary>À appeler à la fermeture de l'appli : referme tous les ports qu'on a ouverts.</summary>
        public static async Task CleanupAllAsync()
        {
            List<string> ids;
            lock (_lock) { ids = new List<string>(_active.Keys); }
            foreach (var id in ids) { await CloseForServerAsync(id); }
        }
    }
}
