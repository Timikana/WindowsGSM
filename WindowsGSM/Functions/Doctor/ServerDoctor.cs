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
    /// "Server Doctor": a server's health report at a glance. Reuses the port model
    /// (incl. quirks like Satisfactory 7777+8888), disk state and Java detection. All local,
    /// no external probe: reports what is actually listening on the machine + reminds that
    /// reachability from the Internet depends on the firewall/port-forward.
    /// </summary>
    public static class ServerDoctor
    {
        public static List<DiagnosticResult> Run(string serverId, string gameFullName, string serverPort, string serverQueryPort, bool isRunning)
        {
            var results = new List<DiagnosticResult>();

            // 1) Server state
            results.Add(new DiagnosticResult("Server status",
                isRunning ? DiagStatus.Ok : DiagStatus.Info,
                isRunning ? "Running." : "Stopped (listening-port checks are skipped)."));

            // 2) Required ports: are they listening locally?
            CheckPorts(results, gameFullName, serverPort, serverQueryPort, isRunning);

            // 3) Server disk space
            CheckDisk(results, serverId);

            // 4) Java (Minecraft games)
            CheckJava(results, gameFullName);

            // 5) Truck Simulator: server_packages.* required (otherwise the server won't start)
            CheckTruckPackages(results, serverId, gameFullName);

            // Reminder
            results.Add(new DiagnosticResult("Internet reachability", DiagStatus.Info,
                "Depends on the firewall + port-forward on the router (see Tools ▸ Ports / UPnP). This diagnostic checks LOCAL listening, not the opening on the router side."));

            return results;
        }

        private static void CheckPorts(List<DiagnosticResult> results, string gameFullName, string serverPort, string serverQueryPort, bool isRunning)
        {
            List<PortMapping> ports;
            try { ports = PortResolver.Suggest(gameFullName, serverPort, serverQueryPort); }
            catch { ports = new List<PortMapping>(); }

            if (ports.Count == 0)
            {
                results.Add(new DiagnosticResult("Ports", DiagStatus.Warn, "No port inferred (incomplete config?)."));
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
                results.Add(new DiagnosticResult("Ports", DiagStatus.Warn, "Unable to list listening ports: " + e.Message));
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
                    results.Add(new DiagnosticResult(name, DiagStatus.Skip, "Server stopped."));
                }
                else if (listening)
                {
                    results.Add(new DiagnosticResult(name, DiagStatus.Ok, "Listening locally."));
                }
                else
                {
                    results.Add(new DiagnosticResult(name, DiagStatus.Fail, "NOT listening while the server is running (wrong port/protocol, or not yet initialized?)."));
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
                results.Add(new DiagnosticResult("Disk space",
                    freeGb < 5 ? DiagStatus.Warn : DiagStatus.Ok,
                    $"{freeGb:0.0} GB free on {drive.Name}" + (freeGb < 5 ? " (low)." : ".")));
            }
            catch (Exception e)
            {
                results.Add(new DiagnosticResult("Disk space", DiagStatus.Warn, e.Message));
            }
        }

        // ===== EXTERNAL reachability check (opt-in: sends the public IP to check-host.net) =====
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public static async Task<List<DiagnosticResult>> CheckExternalAsync(string gameFullName, string serverPort, string serverQueryPort)
        {
            var results = new List<DiagnosticResult>();

            string ip;
            try { ip = (await _http.GetStringAsync("https://api.ipify.org")).Trim(); }
            catch (Exception e)
            {
                results.Add(new DiagnosticResult("Public IP", DiagStatus.Warn, "Unable to obtain it: " + e.Message));
                return results;
            }
            results.Add(new DiagnosticResult("Public IP", DiagStatus.Info, ip));

            List<PortMapping> ports;
            try { ports = PortResolver.Suggest(gameFullName, serverPort, serverQueryPort); }
            catch { ports = new List<PortMapping>(); }

            foreach (var pm in ports)
            {
                bool tcp = pm.Protocol == PortProtocol.Tcp || pm.Protocol == PortProtocol.Both;
                if (!tcp)
                {
                    results.Add(new DiagnosticResult($"External {pm.Port}/UDP ({pm.Label})", DiagStatus.Info,
                        "UDP: not reliably verifiable from the outside — test in-game."));
                    continue;
                }

                var r = await CheckHostTcpAsync(ip, pm.Port);
                r.Check = $"External {pm.Port}/TCP ({pm.Label})";
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
                if (string.IsNullOrEmpty(reqId)) { return new DiagnosticResult("", DiagStatus.Warn, "check-host.net: no request_id."); }

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
                        if (val == null || val.Type == JTokenType.Null) { continue; } // node still pending
                        var first = val.First;
                        if (first == null) { continue; }
                        if (first["address"] != null) { open++; }
                        else if (first["error"] != null) { closed++; }
                    }

                    if (open > 0) { return new DiagnosticResult("", DiagStatus.Ok, $"Open from the Internet ({open} node(s) were able to connect)."); }
                    if (closed >= 2) { break; } // enough nodes failed -> conclude
                }

                if (closed > 0) { return new DiagnosticResult("", DiagStatus.Fail, "NOT reachable from the outside (connection refused/timeout). Check firewall + port-forward."); }
                return new DiagnosticResult("", DiagStatus.Warn, "Undetermined (no clear response from the probes).");
            }
            catch (Exception e)
            {
                return new DiagnosticResult("", DiagStatus.Warn, "check-host.net: " + e.Message);
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
                    results.Add(new DiagnosticResult("Java", DiagStatus.Fail, "No Java detected (required for Minecraft). See adoptium.net."));
                }
                else
                {
                    results.Add(new DiagnosticResult("Java", DiagStatus.Ok, $"Java {major} detected. (Recent MC requires 17/21 — adjust if needed.)"));
                }
            }
            catch (Exception e)
            {
                results.Add(new DiagnosticResult("Java", DiagStatus.Warn, e.Message));
            }
        }

        // Euro/American Truck Simulator: the dedicated server REFUSES to start without
        // save\server_packages.sii + .dat (map/DLC data). These files are exported
        // from the client (console: export_server_packages) then the plugin copies them.
        // We anticipate by alerting if they're missing.
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
                        "server_packages.sii + .dat present (map/DLC OK)."));
                }
                else
                {
                    results.Add(new DiagnosticResult("server_packages", DiagStatus.Fail,
                        "Missing -> the server won't start. In the client (g_console 1), type \"export_server_packages\" with ALL your DLC loaded, then start the server: the plugin copies save\\server_packages.sii + .dat automatically."));
                }
            }
            catch (Exception e)
            {
                results.Add(new DiagnosticResult("server_packages", DiagStatus.Warn, e.Message));
            }
        }
    }
}
