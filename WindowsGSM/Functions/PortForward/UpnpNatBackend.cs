using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace WindowsGSM.Functions.PortForward
{
    /// <summary>
    /// Homemade UPnP IGD backend (no external dependency): SSDP discovery of the gateway,
    /// reading the device description to find the WAN*Connection service, then opening/
    /// closing ports via SOAP (AddPortMapping / DeletePortMapping). Best-effort: any failure
    /// returns false / is logged, no exception ever bubbles up to the caller.
    /// </summary>
    public class UpnpNatBackend : INatBackend
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        private bool _tried;
        private string _serviceType;   // urn:schemas-upnp-org:service:WANIPConnection:1 / :2 / WANPPPConnection:1
        private string _controlUrl;    // absolute URL of the control endpoint
        private string _localIp;       // internal IP of this machine (NewInternalClient)

        public async Task<bool> IsAvailableAsync()
        {
            await EnsureDiscoveredAsync();
            return _controlUrl != null && _localIp != null;
        }

        public async Task<bool> MapAsync(int port, PortProtocol protocol, string description)
        {
            if (!await IsAvailableAsync()) { return false; }

            bool ok = true;
            foreach (var proto in Expand(protocol))
            {
                ok &= await SoapAddAsync(port, proto, description);
            }
            return ok;
        }

        public async Task UnmapAsync(int port, PortProtocol protocol)
        {
            if (!await IsAvailableAsync()) { return; }
            foreach (var proto in Expand(protocol))
            {
                await SoapDeleteAsync(port, proto);
            }
        }

        private static IEnumerable<string> Expand(PortProtocol p)
        {
            switch (p)
            {
                case PortProtocol.Tcp: return new[] { "TCP" };
                case PortProtocol.Udp: return new[] { "UDP" };
                default: return new[] { "TCP", "UDP" };
            }
        }

        // ---- Discovery (once only) ----

        private async Task EnsureDiscoveredAsync()
        {
            if (_tried) { return; }
            await _gate.WaitAsync();
            try
            {
                if (_tried) { return; }
                _tried = true;

                var (location, gatewayHost) = await DiscoverAsync();
                if (location == null) { AppLog.Warn("PortForward", "No UPnP gateway responded (UPnP disabled on the router?)."); return; }

                await ParseDeviceAsync(location);
                if (_controlUrl == null) { AppLog.Warn("PortForward", "Gateway found but WAN*Connection service not found."); return; }

                _localIp = GetLocalIpFor(gatewayHost ?? new Uri(location).Host);
            }
            catch (Exception e)
            {
                AppLog.Warn("PortForward", "UPnP discovery: " + e.Message);
            }
            finally { _gate.Release(); }
        }

        private static async Task<(string location, string gatewayHost)> DiscoverAsync()
        {
            string[] targets =
            {
                "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
                "urn:schemas-upnp-org:service:WANIPConnection:1",
                "urn:schemas-upnp-org:service:WANIPConnection:2",
                "urn:schemas-upnp-org:service:WANPPPConnection:1",
            };

            var multicast = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            using (var udp = new UdpClient())
            {
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                foreach (var st in targets)
                {
                    string msg = "M-SEARCH * HTTP/1.1\r\n" +
                                 "HOST: 239.255.255.250:1900\r\n" +
                                 "MAN: \"ssdp:discover\"\r\n" +
                                 "MX: 2\r\n" +
                                 "ST: " + st + "\r\n\r\n";
                    byte[] bytes = Encoding.ASCII.GetBytes(msg);
                    try { await udp.SendAsync(bytes, bytes.Length, multicast); } catch { }
                }

                var deadline = DateTime.UtcNow.AddSeconds(4);
                while (DateTime.UtcNow < deadline)
                {
                    var recvTask = udp.ReceiveAsync();
                    var delay = Task.Delay(deadline - DateTime.UtcNow);
                    if (await Task.WhenAny(recvTask, delay) != recvTask) { break; }

                    var result = recvTask.Result;
                    string resp = Encoding.ASCII.GetString(result.Buffer);
                    string location = HeaderValue(resp, "LOCATION");
                    if (!string.IsNullOrEmpty(location))
                    {
                        return (location, result.RemoteEndPoint.Address.ToString());
                    }
                }
            }
            return (null, null);
        }

        private static string HeaderValue(string httpText, string header)
        {
            foreach (var line in httpText.Split('\n'))
            {
                int i = line.IndexOf(':');
                if (i <= 0) { continue; }
                if (line.Substring(0, i).Trim().Equals(header, StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(i + 1).Trim();
                }
            }
            return null;
        }

        private async Task ParseDeviceAsync(string location)
        {
            string xml = await _http.GetStringAsync(location);
            var doc = XDocument.Parse(xml);

            // Namespace-agnostic search for a <service> WAN*Connection.
            foreach (var svc in doc.Descendants().Where(e => e.Name.LocalName == "service"))
            {
                string type = svc.Elements().FirstOrDefault(e => e.Name.LocalName == "serviceType")?.Value ?? "";
                if (type.IndexOf("WANIPConnection", StringComparison.OrdinalIgnoreCase) < 0 &&
                    type.IndexOf("WANPPPConnection", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string ctrl = svc.Elements().FirstOrDefault(e => e.Name.LocalName == "controlURL")?.Value;
                if (string.IsNullOrEmpty(ctrl)) { continue; }

                // Resolve the control URL (optional URLBase, otherwise relative to LOCATION).
                string urlBase = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "URLBase")?.Value;
                Uri baseUri = !string.IsNullOrEmpty(urlBase) ? new Uri(urlBase) : new Uri(location);

                _serviceType = type;
                _controlUrl = new Uri(baseUri, ctrl).ToString();
                return;
            }
        }

        private static string GetLocalIpFor(string host)
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect(host, 1900); // sends nothing over UDP, just used to pick the interface
                    return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
                }
            }
            catch { return null; }
        }

        // ---- SOAP ----

        private async Task<bool> SoapAddAsync(int port, string proto, string description)
        {
            string body =
                "<NewRemoteHost></NewRemoteHost>" +
                $"<NewExternalPort>{port}</NewExternalPort>" +
                $"<NewProtocol>{proto}</NewProtocol>" +
                $"<NewInternalPort>{port}</NewInternalPort>" +
                $"<NewInternalClient>{_localIp}</NewInternalClient>" +
                "<NewEnabled>1</NewEnabled>" +
                $"<NewPortMappingDescription>{Escape(description)}</NewPortMappingDescription>" +
                "<NewLeaseDuration>0</NewLeaseDuration>";
            return await SendSoapAsync("AddPortMapping", body);
        }

        private async Task<bool> SoapDeleteAsync(int port, string proto)
        {
            string body =
                "<NewRemoteHost></NewRemoteHost>" +
                $"<NewExternalPort>{port}</NewExternalPort>" +
                $"<NewProtocol>{proto}</NewProtocol>";
            return await SendSoapAsync("DeletePortMapping", body);
        }

        private async Task<bool> SendSoapAsync(string action, string innerBody)
        {
            try
            {
                string envelope =
                    "<?xml version=\"1.0\"?>" +
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                    "<s:Body>" +
                    $"<u:{action} xmlns:u=\"{_serviceType}\">{innerBody}</u:{action}>" +
                    "</s:Body></s:Envelope>";

                using (var req = new HttpRequestMessage(HttpMethod.Post, _controlUrl))
                {
                    req.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");
                    req.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{_serviceType}#{action}\"");
                    var resp = await _http.SendAsync(req);
                    if (!resp.IsSuccessStatusCode)
                    {
                        AppLog.Warn("PortForward", $"{action} HTTP {(int)resp.StatusCode}");
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception e)
            {
                AppLog.Warn("PortForward", $"{action} : {e.Message}");
                return false;
            }
        }

        private static string Escape(string s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
