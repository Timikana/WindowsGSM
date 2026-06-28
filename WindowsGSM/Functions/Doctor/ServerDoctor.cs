using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions.PortForward;

namespace WindowsGSM.Functions.Doctor
{
    /// <summary>
    /// « Server Doctor » : bulletin de santé d'un serveur en un coup d'œil. Réutilise le modèle de
    /// ports (incl. quirks type Satisfactory 7777+8888), l'état disque et la détection Java. Tout en
    /// local, sans sonde externe : indique ce qui écoute réellement sur la machine + rappelle que la
    /// joignabilité depuis Internet dépend du firewall/port-forward.
    /// </summary>
    public static class ServerDoctor
    {
        public static List<DiagnosticResult> Run(string serverId, string gameFullName, string serverPort, string serverQueryPort, bool isRunning)
        {
            var results = new List<DiagnosticResult>();

            // 1) État du serveur
            results.Add(new DiagnosticResult("Statut du serveur",
                isRunning ? DiagStatus.Ok : DiagStatus.Info,
                isRunning ? "En cours d'exécution." : "Arrêté (les contrôles de ports en écoute sont ignorés)."));

            // 2) Ports requis : écoutent-ils localement ?
            CheckPorts(results, gameFullName, serverPort, serverQueryPort, isRunning);

            // 3) Espace disque du serveur
            CheckDisk(results, serverId);

            // 4) Java (jeux Minecraft)
            CheckJava(results, gameFullName);

            // 5) Truck Simulator : server_packages.* requis (sinon le serveur ne démarre pas)
            CheckTruckPackages(results, serverId, gameFullName);

            // Rappel
            results.Add(new DiagnosticResult("Joignabilité Internet", DiagStatus.Info,
                "Dépend du firewall + du port-forward sur la box (voir Tools ▸ Ports / UPnP). Ce diagnostic vérifie l'écoute LOCALE, pas l'ouverture côté box."));

            return results;
        }

        private static void CheckPorts(List<DiagnosticResult> results, string gameFullName, string serverPort, string serverQueryPort, bool isRunning)
        {
            List<PortMapping> ports;
            try { ports = PortResolver.Suggest(gameFullName, serverPort, serverQueryPort); }
            catch { ports = new List<PortMapping>(); }

            if (ports.Count == 0)
            {
                results.Add(new DiagnosticResult("Ports", DiagStatus.Warn, "Aucun port déduit (config incomplète ?)."));
                return;
            }

            HashSet<int> tcp = new HashSet<int>(), udp = new HashSet<int>();
            try
            {
                var props = IPGlobalProperties.GetIPGlobalProperties();
                tcp = new HashSet<int>(props.GetActiveTcpListeners().Select(e => e.Port));
                udp = new HashSet<int>(props.GetActiveUdpListeners().Select(e => e.Port));
            }
            catch (Exception e)
            {
                results.Add(new DiagnosticResult("Ports", DiagStatus.Warn, "Impossible de lister les ports en écoute : " + e.Message));
                return;
            }

            foreach (var pm in ports)
            {
                bool listening =
                    (pm.Protocol == PortProtocol.Tcp && tcp.Contains(pm.Port)) ||
                    (pm.Protocol == PortProtocol.Udp && udp.Contains(pm.Port)) ||
                    (pm.Protocol == PortProtocol.Both && (tcp.Contains(pm.Port) || udp.Contains(pm.Port)));

                string name = $"Port {pm.Port}/{pm.Protocol} ({pm.Label})";
                if (!isRunning)
                {
                    results.Add(new DiagnosticResult(name, DiagStatus.Skip, "Serveur arrêté."));
                }
                else if (listening)
                {
                    results.Add(new DiagnosticResult(name, DiagStatus.Ok, "En écoute localement."));
                }
                else
                {
                    results.Add(new DiagnosticResult(name, DiagStatus.Fail, "PAS en écoute alors que le serveur tourne (mauvais port/protocole, ou non encore initialisé ?)."));
                }
            }
        }

        private static void CheckDisk(List<DiagnosticResult> results, string serverId)
        {
            try
            {
                string root = Path.GetPathRoot(ServerPath.GetServersServerFiles(serverId));
                if (string.IsNullOrEmpty(root)) { return; }
                var drive = new DriveInfo(root);
                if (!drive.IsReady) { return; }
                double freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                results.Add(new DiagnosticResult("Espace disque",
                    freeGb < 5 ? DiagStatus.Warn : DiagStatus.Ok,
                    $"{freeGb:0.0} Go libres sur {drive.Name}" + (freeGb < 5 ? " (faible)." : ".")));
            }
            catch (Exception e)
            {
                results.Add(new DiagnosticResult("Espace disque", DiagStatus.Warn, e.Message));
            }
        }

        // ===== Vérification de joignabilité EXTERNE (opt-in : envoie l'IP publique à check-host.net) =====
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public static async Task<List<DiagnosticResult>> CheckExternalAsync(string gameFullName, string serverPort, string serverQueryPort)
        {
            var results = new List<DiagnosticResult>();

            string ip;
            try { ip = (await _http.GetStringAsync("https://api.ipify.org")).Trim(); }
            catch (Exception e)
            {
                results.Add(new DiagnosticResult("IP publique", DiagStatus.Warn, "Impossible de l'obtenir : " + e.Message));
                return results;
            }
            results.Add(new DiagnosticResult("IP publique", DiagStatus.Info, ip));

            List<PortMapping> ports;
            try { ports = PortResolver.Suggest(gameFullName, serverPort, serverQueryPort); }
            catch { ports = new List<PortMapping>(); }

            foreach (var pm in ports)
            {
                bool tcp = pm.Protocol == PortProtocol.Tcp || pm.Protocol == PortProtocol.Both;
                if (!tcp)
                {
                    results.Add(new DiagnosticResult($"Externe {pm.Port}/UDP ({pm.Label})", DiagStatus.Info,
                        "UDP : non vérifiable de façon fiable depuis l'extérieur — teste en jeu."));
                    continue;
                }

                var r = await CheckHostTcpAsync(ip, pm.Port);
                r.Check = $"Externe {pm.Port}/TCP ({pm.Label})";
                results.Add(r);
            }

            return results;
        }

        private static async Task<DiagnosticResult> CheckHostTcpAsync(string ip, int port)
        {
            try
            {
                var init = new HttpRequestMessage(HttpMethod.Get, $"https://check-host.net/check-tcp?host={ip}:{port}&max_nodes=3");
                init.Headers.TryAddWithoutValidation("Accept", "application/json");
                var initResp = await _http.SendAsync(init);
                var initObj = JObject.Parse(await initResp.Content.ReadAsStringAsync());
                string reqId = initObj["request_id"]?.ToString();
                if (string.IsNullOrEmpty(reqId)) { return new DiagnosticResult("", DiagStatus.Warn, "check-host.net : pas de request_id."); }

                int open = 0, closed = 0;
                for (int i = 0; i < 7; i++)
                {
                    await Task.Delay(1300);
                    var resReq = new HttpRequestMessage(HttpMethod.Get, $"https://check-host.net/check-result/{reqId}");
                    resReq.Headers.TryAddWithoutValidation("Accept", "application/json");
                    var resResp = await _http.SendAsync(resReq);
                    var res = JObject.Parse(await resResp.Content.ReadAsStringAsync());

                    open = 0; closed = 0;
                    foreach (var prop in res.Properties())
                    {
                        var val = prop.Value;
                        if (val == null || val.Type == JTokenType.Null) { continue; } // nœud en attente
                        var first = val.First;
                        if (first == null) { continue; }
                        if (first["address"] != null) { open++; }
                        else if (first["error"] != null) { closed++; }
                    }

                    if (open > 0) { return new DiagnosticResult("", DiagStatus.Ok, $"Ouvert depuis Internet ({open} nœud(s) ont pu se connecter)."); }
                    if (closed >= 2) { break; } // assez de nœuds ont échoué -> conclure
                }

                if (closed > 0) { return new DiagnosticResult("", DiagStatus.Fail, "NON joignable depuis l'extérieur (connexion refusée/timeout). Vérifie firewall + port-forward."); }
                return new DiagnosticResult("", DiagStatus.Warn, "Indéterminé (pas de réponse claire des sondes).");
            }
            catch (Exception e)
            {
                return new DiagnosticResult("", DiagStatus.Warn, "check-host.net : " + e.Message);
            }
        }

        private static void CheckJava(List<DiagnosticResult> results, string gameFullName)
        {
            string g = (gameFullName ?? "").ToLowerInvariant();
            bool isMinecraft = g.Contains("minecraft") || g.Contains("fabric") || g.Contains("forge");
            if (!isMinecraft) { return; }

            try
            {
                int major = JavaHelper.GetNewestJavaMajorVersion();
                if (major <= 0)
                {
                    results.Add(new DiagnosticResult("Java", DiagStatus.Fail, "Aucun Java détecté (requis pour Minecraft). Voir adoptium.net."));
                }
                else
                {
                    results.Add(new DiagnosticResult("Java", DiagStatus.Ok, $"Java {major} détecté. (MC récent demande 17/21 — adapte si besoin.)"));
                }
            }
            catch (Exception e)
            {
                results.Add(new DiagnosticResult("Java", DiagStatus.Warn, e.Message));
            }
        }

        // Euro/American Truck Simulator : le serveur dédié REFUSE de démarrer sans
        // save\server_packages.sii + .dat (données de map/DLC). Ces fichiers s'exportent
        // depuis le client (console : export_server_packages) puis le plugin les copie.
        // On anticipe en alertant si manquants.
        private static void CheckTruckPackages(List<DiagnosticResult> results, string serverId, string gameFullName)
        {
            string g = (gameFullName ?? "").ToLowerInvariant();
            if (!g.Contains("truck simulator")) { return; }

            try
            {
                string save = ServerPath.GetServersServerFiles(serverId, "save");
                bool sii = File.Exists(Path.Combine(save, "server_packages.sii"));
                bool dat = File.Exists(Path.Combine(save, "server_packages.dat"));
                if (sii && dat)
                {
                    results.Add(new DiagnosticResult("server_packages", DiagStatus.Ok,
                        "server_packages.sii + .dat présents (map/DLC OK)."));
                }
                else
                {
                    results.Add(new DiagnosticResult("server_packages", DiagStatus.Fail,
                        "Manquant(s) -> le serveur ne démarrera pas. Dans le client (g_console 1), tape « export_server_packages » avec TOUS tes DLC chargés, puis démarre le serveur : le plugin copie save\\server_packages.sii + .dat automatiquement."));
                }
            }
            catch (Exception e)
            {
                results.Add(new DiagnosticResult("server_packages", DiagStatus.Warn, e.Message));
            }
        }
    }
}
