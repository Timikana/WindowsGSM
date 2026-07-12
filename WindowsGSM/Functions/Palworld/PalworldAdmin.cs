using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WindowsGSM.Functions.Palworld
{
    /// <summary>One connected player, as returned by GET /v1/api/players.</summary>
    public sealed class PalPlayer
    {
        public string Name;
        public string PlayerId;
        public string UserId;   // "steam_0110000..." — the id kick/ban expect
        public string SteamId;
        public int Ping;
        public int Level;
        public string Ip;
    }

    public sealed class PalInfo
    {
        public string Version;
        public string ServerName;
        public string Description;
    }

    /// <summary>
    /// Client for the Palworld dedicated server built-in REST API
    /// (http://host:RESTAPIPort/v1/api, HTTP Basic auth admin:&lt;AdminPassword&gt;).
    /// Every call is best-effort and returns (ok, error) instead of throwing.
    /// The player-count path (metrics) already lives in NativePlayerQuery; this adds the admin actions.
    /// </summary>
    public sealed class PalworldAdmin
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _password;

        public PalworldAdmin(string host, int restPort, string adminPassword)
        {
            _host = string.IsNullOrWhiteSpace(host) || host == "0.0.0.0" ? "127.0.0.1" : host;
            _port = restPort;
            _password = adminPassword;
        }

        public bool IsConfigured => _port > 0 && !string.IsNullOrEmpty(_password);

        private HttpClient Make(int timeoutMs)
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            string token = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:" + _password));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            return http;
        }

        private string Url(string path) => $"http://{_host}:{_port}/v1/api/{path}";

        // ---- reads ----

        public async Task<(bool ok, List<PalPlayer> players, string err)> GetPlayersAsync(int timeoutMs = 3000)
        {
            if (!IsConfigured) { return (false, null, "REST API not configured"); }
            try
            {
                using (var http = Make(timeoutMs))
                {
                    var resp = await http.GetAsync(Url("players")).ConfigureAwait(false);
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) { return (false, null, $"HTTP {(int)resp.StatusCode}"); }

                    var list = new List<PalPlayer>();
                    var arr = JObject.Parse(body)["players"] as JArray;
                    if (arr != null)
                    {
                        foreach (var p in arr)
                        {
                            list.Add(new PalPlayer
                            {
                                Name = p.Value<string>("name") ?? "",
                                PlayerId = p.Value<string>("playerId") ?? "",
                                UserId = p.Value<string>("userId") ?? "",
                                SteamId = p.Value<string>("steamid") ?? p.Value<string>("steamId") ?? "",
                                Ping = (int)Math.Round(p.Value<double?>("ping") ?? 0),
                                Level = p.Value<int?>("level") ?? 0,
                                Ip = p.Value<string>("iP") ?? p.Value<string>("ip") ?? ""
                            });
                        }
                    }
                    return (true, list, null);
                }
            }
            catch (Exception e) { return (false, null, e.Message); }
        }

        public async Task<(bool ok, PalInfo info, string err)> GetInfoAsync(int timeoutMs = 3000)
        {
            if (!IsConfigured) { return (false, null, "REST API not configured"); }
            try
            {
                using (var http = Make(timeoutMs))
                {
                    var resp = await http.GetAsync(Url("info")).ConfigureAwait(false);
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) { return (false, null, $"HTTP {(int)resp.StatusCode}"); }
                    var j = JObject.Parse(body);
                    return (true, new PalInfo
                    {
                        Version = j.Value<string>("version") ?? "",
                        ServerName = j.Value<string>("servername") ?? "",
                        Description = j.Value<string>("description") ?? ""
                    }, null);
                }
            }
            catch (Exception e) { return (false, null, e.Message); }
        }

        // ---- actions ----

        private async Task<(bool ok, string err)> PostAsync(string path, object payload, int timeoutMs = 5000)
        {
            if (!IsConfigured) { return (false, "REST API not configured"); }
            try
            {
                using (var http = Make(timeoutMs))
                {
                    HttpContent content = payload == null
                        ? new StringContent("", Encoding.UTF8, "application/json")
                        : new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    var resp = await http.PostAsync(Url(path), content).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode) { return (true, null); }
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return (false, string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)resp.StatusCode}" : body.Trim());
                }
            }
            catch (Exception e) { return (false, e.Message); }
        }

        public Task<(bool ok, string err)> AnnounceAsync(string message) => PostAsync("announce", new { message = message ?? "" });

        public Task<(bool ok, string err)> KickAsync(string userId, string message) => PostAsync("kick", new { userid = userId, message = message ?? "" });

        public Task<(bool ok, string err)> BanAsync(string userId, string message) => PostAsync("ban", new { userid = userId, message = message ?? "" });

        public Task<(bool ok, string err)> UnbanAsync(string userId) => PostAsync("unban", new { userid = userId });

        public Task<(bool ok, string err)> SaveAsync() => PostAsync("save", null);

        /// <summary>Graceful shutdown after <paramref name="waitSeconds"/> with an in-game notice.</summary>
        public Task<(bool ok, string err)> ShutdownAsync(int waitSeconds, string message) => PostAsync("shutdown", new { waittime = Math.Max(1, waitSeconds), message = message ?? "" });

        public Task<(bool ok, string err)> ForceStopAsync() => PostAsync("stop", null);
    }
}
