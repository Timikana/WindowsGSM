using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace WindowsGSM.Functions.Notifications
{
    /// <summary>
    /// ntfy channel (https://ntfy.sh or self-hosted instance): POST text to a topic.
    /// Ideal for push notifications on a phone without a bot. Optional Bearer auth (private topic).
    /// </summary>
    public class NtfyNotifier : INotifier
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = System.TimeSpan.FromSeconds(15) };
        private readonly NtfyConfig _cfg;

        public NtfyNotifier(NtfyConfig cfg) { _cfg = cfg ?? new NtfyConfig(); }

        public string Name => "ntfy";

        public bool IsEnabled =>
            _cfg.Enabled &&
            !string.IsNullOrWhiteSpace(_cfg.ServerUrl) &&
            !string.IsNullOrWhiteSpace(_cfg.Topic);

        public async Task<bool> SendAsync(string title, string message)
        {
            if (!IsEnabled) { return false; }

            try
            {
                string url = _cfg.ServerUrl.TrimEnd('/') + "/" + _cfg.Topic.Trim();
                using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    req.Content = new StringContent(message ?? string.Empty, Encoding.UTF8);

                    // HTTP headers don't accept line breaks -> flatten the title.
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        req.Headers.TryAddWithoutValidation("Title", title.Replace("\r", " ").Replace("\n", " "));
                    }
                    if (!string.IsNullOrWhiteSpace(_cfg.Priority))
                    {
                        req.Headers.TryAddWithoutValidation("Priority", _cfg.Priority);
                    }
                    if (!string.IsNullOrWhiteSpace(_cfg.AuthToken))
                    {
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cfg.AuthToken);
                    }

                    var resp = await _http.SendAsync(req);
                    if (!resp.IsSuccessStatusCode)
                    {
                        AppLog.Warn("Notifier/ntfy", $"HTTP {(int)resp.StatusCode} on {url}");
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception e)
            {
                AppLog.Warn("Notifier/ntfy", e.Message);
                return false;
            }
        }
    }
}
