using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Comptage des joueurs pour les jeux qui n'exposent PAS l'A2S Steam (Palworld, Satisfactory).
    /// On interroge leur API native. Best-effort : renvoie null en cas d'échec (jamais d'exception).
    /// Réutilise SteamQuery.Info (Players / MaxPlayers).
    /// </summary>
    internal static class NativePlayerQuery
    {
        // ===== Palworld : API REST GET /v1/api/metrics (Basic auth admin:<AdminPassword>) =====
        public static async Task<SteamQuery.Info?> PalworldAsync(string host, int restPort, string adminPassword, int timeoutMs = 1500)
        {
            if (string.IsNullOrWhiteSpace(host) || host == "0.0.0.0") { host = "127.0.0.1"; }
            if (restPort <= 0 || string.IsNullOrEmpty(adminPassword)) { return null; }

            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) })
                {
                    string token = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:" + adminPassword));
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);

                    var resp = await http.GetAsync($"http://{host}:{restPort}/v1/api/metrics").ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) { return null; }

                    var j = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                    return new SteamQuery.Info
                    {
                        Players = j.Value<int?>("currentplayernum") ?? 0,
                        MaxPlayers = j.Value<int?>("maxplayernum") ?? 0
                    };
                }
            }
            catch
            {
                return null;
            }
        }

        // ===== Satisfactory : API HTTPS (certif auto-signé) =====
        // Si apiToken fourni (server.GenerateAPIToken) on l'utilise directement ; sinon on tente
        // PasswordlessLogin (ne marche que sur un serveur NON claimé). Puis QueryServerState.
        // secret = token API (server.GenerateAPIToken) OU mot de passe (Client/Admin). On tente, dans l'ordre :
        // le secret comme token Bearer ; sinon PasswordLogin (secret = mot de passe) ; sinon PasswordlessLogin.
        public static async Task<SteamQuery.Info?> SatisfactoryAsync(string host, int apiPort, string secret = null, int timeoutMs = 2500)
        {
            if (string.IsNullOrWhiteSpace(host) || host == "0.0.0.0") { host = "127.0.0.1"; }
            if (apiPort <= 0) { return null; }

            try
            {
                var handler = new HttpClientHandler
                {
                    // Certificat auto-signé du serveur Satisfactory : on ne valide pas (loopback local).
                    ServerCertificateCustomValidationCallback = (m, c, ch, e) => true
                };
                using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) })
                {
                    string url = $"https://{host}:{apiPort}/api/v1";
                    secret = string.IsNullOrWhiteSpace(secret) ? null : secret.Trim();

                    // 1) secret comme token Bearer direct.
                    if (secret != null)
                    {
                        var direct = await SatisQueryState(http, url, secret).ConfigureAwait(false);
                        if (direct != null) { return direct; }
                    }

                    // 2) secret comme mot de passe (PasswordLogin Administrator) ; sinon PasswordlessLogin.
                    string token = secret != null
                        ? await SatisLogin(http, url, "PasswordLogin", "Administrator", secret).ConfigureAwait(false)
                        : await SatisLogin(http, url, "PasswordlessLogin", "Client", null).ConfigureAwait(false);

                    if (string.IsNullOrEmpty(token)) { return null; }
                    return await SatisQueryState(http, url, token).ConfigureAwait(false);
                }
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string> SatisLogin(HttpClient http, string url, string function, string level, string password)
        {
            try
            {
                string body = password == null
                    ? $"{{\"function\":\"{function}\",\"data\":{{\"minimumPrivilegeLevel\":\"{level}\"}}}}"
                    : $"{{\"function\":\"{function}\",\"data\":{{\"minimumPrivilegeLevel\":\"{level}\",\"password\":{JsonConvert.ToString(password)}}}}}";
                var resp = await http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) { return null; }
                var j = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                return j["data"]?.Value<string>("authenticationToken");
            }
            catch { return null; }
        }

        // ===== 7 Days to Die : via Telnet (pas d'A2S fiable). Login -> "lp" -> "Total of N in the game". =====
        public static async Task<SteamQuery.Info?> SevenDaysTelnetAsync(string host, int telnetPort, string password, int maxPlayers, int timeoutMs = 3500)
        {
            if (string.IsNullOrWhiteSpace(host) || host == "0.0.0.0") { host = "127.0.0.1"; }
            if (telnetPort <= 0) { return null; }

            return await Task.Run(() =>
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        if (!client.ConnectAsync(host, telnetPort).Wait(timeoutMs)) { return (SteamQuery.Info?)null; }
                        using (var stream = client.GetStream())
                        {
                            stream.ReadTimeout = 900;
                            var enc = Encoding.ASCII;
                            var buf = new byte[8192];

                            string ReadFor(int totalMs)
                            {
                                var sb = new StringBuilder();
                                var sw = Stopwatch.StartNew();
                                while (sw.ElapsedMilliseconds < totalMs)
                                {
                                    try
                                    {
                                        int n = stream.Read(buf, 0, buf.Length);
                                        if (n <= 0) { break; }
                                        sb.Append(enc.GetString(buf, 0, n));
                                    }
                                    catch (IOException) { break; } // gap (read timeout) -> on s'arrête
                                }
                                return sb.ToString();
                            }
                            void Send(string s) { var b = enc.GetBytes(s + "\r\n"); stream.Write(b, 0, b.Length); }

                            ReadFor(1200); // bannière + "Please enter password:"
                            if (!string.IsNullOrEmpty(password)) { Send(password); ReadFor(1000); }
                            Send("lp");
                            string outp = ReadFor(1500);

                            var m = Regex.Match(outp, @"Total of (\d+) in the game");
                            if (!m.Success) { return (SteamQuery.Info?)null; }
                            return (SteamQuery.Info?)new SteamQuery.Info
                            {
                                Players = int.Parse(m.Groups[1].Value),
                                MaxPlayers = maxPlayers
                            };
                        }
                    }
                }
                catch { return (SteamQuery.Info?)null; }
            }).ConfigureAwait(false);
        }

        private static async Task<SteamQuery.Info?> SatisQueryState(HttpClient http, string url, string token)
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    req.Content = new StringContent("{\"function\":\"QueryServerState\"}", Encoding.UTF8, "application/json");
                    if (!string.IsNullOrEmpty(token)) { req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); }

                    var qr = await http.SendAsync(req).ConfigureAwait(false);
                    if (!qr.IsSuccessStatusCode) { return null; }

                    var qj = JObject.Parse(await qr.Content.ReadAsStringAsync().ConfigureAwait(false));
                    var state = qj["data"]?["serverGameState"];
                    if (state == null) { return null; }

                    return new SteamQuery.Info
                    {
                        Players = state.Value<int?>("numConnectedPlayers") ?? 0,
                        MaxPlayers = state.Value<int?>("playerLimit") ?? 0
                    };
                }
            }
            catch { return null; }
        }
    }
}
