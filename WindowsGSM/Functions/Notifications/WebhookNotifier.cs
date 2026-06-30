using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WindowsGSM.Functions.Notifications
{
    /// <summary>
    /// Generic webhook channel: POST JSON {"title","message"} to an arbitrary URL
    /// (Slack/Teams/Gotify/custom...). Optional Authorization: Bearer header (encrypted at rest).
    /// </summary>
    public class WebhookNotifier : INotifier
    {
        private static readonly HttpClient _http = new HttpClient();
        private readonly WebhookConfig _cfg;

        public WebhookNotifier(WebhookConfig cfg) { _cfg = cfg ?? new WebhookConfig(); }

        public string Name => "webhook";

        public bool IsEnabled => _cfg.Enabled && !string.IsNullOrWhiteSpace(_cfg.Url);

        public async Task<bool> SendAsync(string title, string message)
        {
            if (!IsEnabled) { return false; }

            try
            {
                string json = JsonConvert.SerializeObject(new { title = title ?? string.Empty, message = message ?? string.Empty });
                using (var req = new HttpRequestMessage(HttpMethod.Post, _cfg.Url))
                {
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    if (!string.IsNullOrWhiteSpace(_cfg.AuthBearer))
                    {
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cfg.AuthBearer);
                    }

                    var resp = await _http.SendAsync(req);
                    if (!resp.IsSuccessStatusCode)
                    {
                        AppLog.Warn("Notifier/webhook", $"HTTP {(int)resp.StatusCode}");
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception e)
            {
                AppLog.Warn("Notifier/webhook", e.Message);
                return false;
            }
        }
    }
}
