using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WindowsGSM.Functions.PortForward;
using WindowsGSM.Functions.Localization;

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
            results.Add(new DiagnosticResult(Loc.T("Doctor.CheckServerStatus"),
                isRunning ? DiagStatus.Ok : DiagStatus.Info,
                isRunning ? Loc.T("Doctor.StatusRunning") : Loc.T("Doctor.StatusStopped")));

            // 2) Required ports: are they listening locally?
            CheckPorts(results, gameFullName, serverPort, serverQueryPort, isRunning);

            // 3) Server disk space
            CheckDisk(results, serverId);

            // 4) Java (Minecraft games)
            CheckJava(results, gameFullName);

            // 5) Truck Simulator: server_packages.* required (otherwise the server won't start)
            CheckTruckPackages(results, serverId, gameFullName);

            // Reminder
            results.Add(new DiagnosticResult(Loc.T("Doctor.CheckInternet"), DiagStatus.Info,
                Loc.T("Doctor.InternetDetail")));

            return results;
        }

        private static void CheckPorts(List<DiagnosticResult> results, string gameFullName, string serverPort, string serverQueryPort, bool isRunning)
        {
            List<PortMapping> ports;
            try { ports = PortResolver.Suggest(gameFullName, serverPort, serverQueryPort); }
            catch { ports = new List<PortMapping>(); }

            if (ports.Count == 0)
            {
                results.Add(new DiagnosticResult(Loc.T("Doctor.CheckPorts"), DiagStatus.Warn, Loc.T("Doctor.PortsNoneInferred")));
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
                results.Add(new DiagnosticResult(Loc.T("Doctor.CheckPorts"), DiagStatus.Warn, Loc.T("Doctor.PortsListFailed", e.Message)));
                return;
            }

            foreach (var pm in ports)
            {
                bool listening =
                    (pm.Protocol == PortProtocol.Tcp && tcp.Contains(pm.Port)) ||
                    (pm.Protocol == PortProtocol.Udp && udp.Contains(pm.Port)) ||
                    (pm.Protocol == PortProtocol.Both && (tcp.Contains(pm.Port) || udp.Contains(pm.Port)));

                string name = Loc.T("Doctor.PortName", $"{pm.Port}/{pm.Protocol}", pm.Label);
                if (!isRunning)
                {
                    results.Add(new DiagnosticResult(name, DiagStatus.Skip, Loc.T("Doctor.PortServerStopped")));
                }
                else if (listening)
                {
                    results.Add(new DiagnosticResult(name, DiagStatus.Ok, Loc.T("Doctor.PortListening")));
                }
                else
                {
                    results.Add(new DiagnosticResult(name, DiagStatus.Fail, Loc.T("Doctor.PortNotListening")));
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
                results.Add(new DiagnosticResult(Loc.T("Doctor.CheckDisk"),
                    freeGb < 5 ? DiagStatus.Warn : DiagStatus.Ok,
                    Loc.T(freeGb < 5 ? "Doctor.DiskFreeLow" : "Doctor.DiskFree", $"{freeGb:0.0}", drive.Name)));
            }
            catch (Exception e)
            {
                results.Add(new DiagnosticResult(Loc.T("Doctor.CheckDisk"), DiagStatus.Warn, e.Message));
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
                results.Add(new DiagnosticResult(Loc.T("Doctor.PublicIP"), DiagStatus.Warn, Loc.T("Doctor.PublicIPFailed", e.Message)));
                return results;
            }
            results.Add(new DiagnosticResult(Loc.T("Doctor.PublicIP"), DiagStatus.Info, ip));

            List<PortMapping> ports;
            try { ports = PortResolver.Suggest(gameFullName, serverPort, serverQueryPort); }
            catch { ports = new List<PortMapping>(); }

            foreach (var pm in ports)
            {
                bool tcp = pm.Protocol == PortProtocol.Tcp || pm.Protocol == PortProtocol.Both;
                if (!tcp)
                {
                    results.Add(new DiagnosticResult(Loc.T("Doctor.ExternalPortName", $"{pm.Port}/UDP", pm.Label), DiagStatus.Info,
                        Loc.T("Doctor.ExternalUdp")));
                    continue;
                }

                var r = await CheckHostTcpAsync(ip, pm.Port);
                r.Check = Loc.T("Doctor.ExternalPortName", $"{pm.Port}/TCP", pm.Label);
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
                if (string.IsNullOrEmpty(reqId)) { return new DiagnosticResult("", DiagStatus.Warn, Loc.T("Doctor.CheckHostNoReqId")); }

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

                    if (open > 0) { return new DiagnosticResult("", DiagStatus.Ok, Loc.T("Doctor.ExtOpen", open)); }
                    if (closed >= 2) { break; } // enough nodes failed -> conclude
                }

                if (closed > 0) { return new DiagnosticResult("", DiagStatus.Fail, Loc.T("Doctor.ExtNotReachable")); }
                return new DiagnosticResult("", DiagStatus.Warn, Loc.T("Doctor.ExtUndetermined"));
            }
            catch (Exception e)
            {
                return new DiagnosticResult("", DiagStatus.Warn, Loc.T("Doctor.CheckHostError", e.Message));
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
                    results.Add(new DiagnosticResult("Java", DiagStatus.Fail, Loc.T("Doctor.JavaNone")));
                }
                else
                {
                    results.Add(new DiagnosticResult("Java", DiagStatus.Ok, Loc.T("Doctor.JavaDetected", major)));
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
                        Loc.T("Doctor.TruckPackagesOk")));
                }
                else
                {
                    results.Add(new DiagnosticResult("server_packages", DiagStatus.Fail,
                        Loc.T("Doctor.TruckPackagesMissing")));
                }
            }
            catch (Exception e)
            {
                results.Add(new DiagnosticResult("server_packages", DiagStatus.Warn, e.Message));
            }
        }
    }
}
