using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace WindowsGSM.Functions.Notifications
{
    /// <summary>
    /// Telegram channel via the Bot API (sendMessage). Requires a bot (token via @BotFather) and the
    /// destination chat_id. Token encrypted at rest.
    /// </summary>
    public class TelegramNotifier : INotifier
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = System.TimeSpan.FromSeconds(15) };
        private readonly TelegramConfig _cfg;

        public TelegramNotifier(TelegramConfig cfg) { _cfg = cfg ?? new TelegramConfig(); }

        public string Name => "telegram";

        public bool IsEnabled =>
            _cfg.Enabled &&
            !string.IsNullOrWhiteSpace(_cfg.BotToken) &&
            !string.IsNullOrWhiteSpace(_cfg.ChatId);

        public async Task<bool> SendAsync(string title, string message)
        {
            if (!IsEnabled) { return false; }

            try
            {
                string text = string.IsNullOrWhiteSpace(title) ? message : $"*{title}*\n{message}";
                string url = $"https://api.telegram.org/bot{_cfg.BotToken}/sendMessage";

                var form = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("chat_id", _cfg.ChatId),
                    new KeyValuePair<string, string>("text", text ?? string.Empty),
                    new KeyValuePair<string, string>("parse_mode", "Markdown"),
                });

                var resp = await _http.PostAsync(url, form);
                if (!resp.IsSuccessStatusCode)
                {
                    AppLog.Warn("Notifier/telegram", $"HTTP {(int)resp.StatusCode}");
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                AppLog.Warn("Notifier/telegram", e.Message);
                return false;
            }
        }
    }
}
