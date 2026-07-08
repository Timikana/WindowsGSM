using System;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Web;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Linq;

namespace WindowsGSM.Functions
{
    class DiscordWebhook 
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _webhookUrl;
        private readonly string _customMessage;
        private readonly string _donorType;

        // ---- Anti-bombing: a crash-looping server must not flood the Discord channel. ----
        // Per-key dedup only (same server+status within the window). No global cap — that could silently
        // drop a genuinely different alert (e.g. a real crash on another server).
        private static readonly object _throttleGate = new object();
        private static readonly System.Collections.Generic.Dictionary<string, DateTime> _lastByKey = new System.Collections.Generic.Dictionary<string, DateTime>();
        private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(5);

        private static bool AllowWebhook(string url, string servername, string status)
        {
            var now = DateTime.UtcNow;
            string key = (url ?? string.Empty) + "" + (servername ?? string.Empty) + "" + (status ?? string.Empty);
            lock (_throttleGate)
            {
                if (_lastByKey.TryGetValue(key, out var last) && now - last < DedupWindow) { return false; }
                _lastByKey[key] = now;
                if (_lastByKey.Count > 256)
                {
                    foreach (var k in _lastByKey.Where(kv => now - kv.Value > DedupWindow).Select(kv => kv.Key).ToList())
                    {
                        _lastByKey.Remove(k);
                    }
                }
                return true;
            }
        }

        public DiscordWebhook(string webhookurl, string customMessage, string donorType = "")
        {
            _webhookUrl = webhookurl ?? string.Empty;
            _customMessage = customMessage ?? string.Empty;
            _donorType = donorType ?? string.Empty;
        }

        // #163: if the configured IP is not routable (0.0.0.0/localhost/empty), display the PUBLIC IP.
        private static string _cachedPublicIp;
        private static DateTime _publicIpAt = DateTime.MinValue;
        private static async Task<string> PublicIpIfNeeded(string ip)
        {
            string s = (ip ?? string.Empty).Trim();
            if (s.Length > 0 && s != "0.0.0.0" && s != "127.0.0.1" && !s.Equals("localhost", StringComparison.OrdinalIgnoreCase)) { return s; }
            try
            {
                if (string.IsNullOrEmpty(_cachedPublicIp) || (DateTime.Now - _publicIpAt).TotalMinutes > 30)
                {
                    _cachedPublicIp = (await _httpClient.GetStringAsync("https://api.ipify.org")).Trim();
                    _publicIpAt = DateTime.Now;
                }
                return string.IsNullOrEmpty(_cachedPublicIp) ? s : _cachedPublicIp;
            }
            catch { return s; }
        }

        public async Task<bool> Send(string serverid, string servergame, string serverstatus, string servername, string serverip, string serverport)
        {
            serverip = await PublicIpIfNeeded(serverip); // #163

            // Multi-channel: also broadcasts to the configured global channels (ntfy, etc.),
            // independently of Discord (works even if no Discord webhook is defined).
            try
            {
                string extra = string.IsNullOrWhiteSpace(_customMessage) ? string.Empty : "\n" + _customMessage;
                await Notifications.Notifier.Broadcast($"{servername} [{servergame}]", $"{serverstatus}\n{serverip}:{serverport}{extra}");
            }
            catch { /* best-effort: never block the Discord alert */ }

            if (string.IsNullOrWhiteSpace(_webhookUrl))
            {
                return false;
            }

            // Anti-bombing: drop a duplicate alert (same server+status) within the window, or when the
            // global cap is hit — a crash loop otherwise spams the Discord channel.
            if (!AllowWebhook(_webhookUrl, servername, serverstatus))
            {
                return false;
            }

            string avatarUrl = GetAvatarUrl();
            string json = @"
            {
                ""username"": ""WindowsGSM"",
                ""avatar_url"": """ + avatarUrl  + @""",
                ""content"": """ + HttpUtility.JavaScriptStringEncode(_customMessage) + @""",
                ""embeds"": [
                {
                    ""type"": ""rich"",
                    ""color"": " + GetColor(serverstatus) + @",
                    ""fields"": [
                    {
                        ""name"": ""Status"",
                        ""value"": """ + GetStatusWithEmoji(serverstatus) + @""",
                        ""inline"": true
                    },
                    {
                        ""name"": ""Game Server"",
                        ""value"": """ + HttpUtility.JavaScriptStringEncode(servergame) + @""",
                        ""inline"": true
                    },
                    {
                        ""name"": ""Server IP:Port"",
                        ""value"": """ + HttpUtility.JavaScriptStringEncode(serverip) + ":" + HttpUtility.JavaScriptStringEncode(serverport) + @""",
                        ""inline"": true
                    }],
                    ""author"": {
                        ""name"": """ + HttpUtility.JavaScriptStringEncode(servername) + @""",
                        ""icon_url"": """ + GetServerGameIcon(servergame) + @"""
                    },
                    ""footer"": {
                        ""text"": """ + MainWindow.WGSM_VERSION + @" - Discord Alert"",
                        ""icon_url"": """ + avatarUrl + @"""
                    },
                    ""timestamp"": """ + DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.mssZ") + @""",
                    ""thumbnail"": {
                        ""url"": """ + GetThumbnail(serverstatus) + @"""
                    }
                }]
            }";

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(_webhookUrl, content);
                if (response.Content != null)
                {
                    return true;
                }
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"Fail to send webhook ({_webhookUrl})");
            }

            return false;
        }

        private static string GetColor(string serverStatus)
        {
            // Test "Restarted" BEFORE "Started": "Restarted".Contains("Started") is true, so the order matters.
            if (serverStatus.Contains("Restarted"))
            {
                return "65535"; //Cyan
            }
            else if (serverStatus.Contains("Started"))
            {
                return "65280"; //Green
            }
            else if (serverStatus.Contains("Crashed"))
            {
                return "16711680"; //Red
            }
            else if (serverStatus.Contains("Updated"))
            {
                return "16564292"; //Gold
            }

            return "16711679";
        }

        private static string GetStatusWithEmoji(string serverStatus)
        {
            if (serverStatus.Contains("Restarted"))
            {
                return ":blue_circle: " + serverStatus;
            }
            if (serverStatus.Contains("Started"))
            {
                return ":green_circle: " + serverStatus;
            }
            if (serverStatus.Contains("Crashed"))
            {
                return ":red_circle: " + serverStatus;
            }
            if (serverStatus.Contains("Updated"))
            {
                return ":orange_circle: " + serverStatus;
            }

            return serverStatus;
        }

        private static string GetThumbnail(string serverStatus)
        {
            string url = "https://github.com/WindowsGSM/Discord-Alert-Icons/raw/master/";
            if (serverStatus.Contains("Restarted"))
            {
                return $"{url}Restarted.png";
            }
            if (serverStatus.Contains("Started"))
            {
                return $"{url}Started.png";
            }
            if (serverStatus.Contains("Crashed"))
            {
                return $"{url}Crashed.png";
            }
            if (serverStatus.Contains("Updated"))
            {
                return $"{url}Updated.png";
            }

            return $"{url}Test.png";
        }

        private static string GetServerGameIcon(string serverGame)
        {
            try
            {
                return @"https://github.com/WindowsGSM/WindowsGSM/raw/master/WindowsGSM/" + GameServer.Data.Icon.ResourceManager.GetString(serverGame);
            }
            catch
            {
                return @"https://github.com/WindowsGSM/WindowsGSM/raw/master/WindowsGSM/Images/WindowsGSM.png";
            }
        }

        private string GetAvatarUrl()
        {
            return "https://github.com/WindowsGSM/WindowsGSM/raw/master/WindowsGSM/Images/WindowsGSM" + (string.IsNullOrWhiteSpace(_donorType) ? string.Empty : $"-{_donorType}") + ".png";
        }

        // Privacy: the upstream WindowsGSM code shipped the full crash log to a hidden, obfuscated
        // third-party Discord webhook. Removed. We only tidy up the local temp crash file; nothing is
        // sent anywhere. (The crash log itself is still written locally by App.xaml.cs for the operator.)
        public static void SendErrorLog()
        {
            try
            {
                string latestLogFile = Path.Combine(MainWindow.WGSM_PATH, "logs", "latest_crash_wgsm_temp.log");
                if (File.Exists(latestLogFile)) { File.Delete(latestLogFile); }
            }
            catch { }
        }
    }
}
