using System;
using System.IO;
using Newtonsoft.Json;

namespace WindowsGSM.Functions.Notifications
{
    public class NtfyConfig
    {
        public bool Enabled { get; set; } = false;
        public string ServerUrl { get; set; } = "https://ntfy.sh";
        public string Topic { get; set; } = "";
        public string AuthToken { get; set; } = ""; // secret (encrypted on disk)
        public string Priority { get; set; } = "default"; // min|low|default|high|max
    }

    public class TelegramConfig
    {
        public bool Enabled { get; set; } = false;
        public string BotToken { get; set; } = ""; // secret
        public string ChatId { get; set; } = "";
    }

    public class EmailConfig
    {
        public bool Enabled { get; set; } = false;
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 587;
        public bool UseSsl { get; set; } = true;
        public string Username { get; set; } = "";
        public string Password { get; set; } = ""; // secret
        public string From { get; set; } = "";
        public string To { get; set; } = "";
    }

    public class WebhookConfig
    {
        public bool Enabled { get; set; } = false;
        public string Url { get; set; } = "";
        public string AuthBearer { get; set; } = ""; // optional secret (Authorization: Bearer header)
    }

    /// <summary>
    /// Global notification channels configuration. Stored in &lt;WGSM&gt;\configs\notifications.json.
    /// Secrets (tokens, passwords) are encrypted at rest via <see cref="Secret"/>.
    /// Channels: ntfy, Telegram, email (SMTP), generic webhook.
    /// </summary>
    public class NotificationConfig
    {
        public NtfyConfig Ntfy { get; set; } = new NtfyConfig();
        public TelegramConfig Telegram { get; set; } = new TelegramConfig();
        public EmailConfig Email { get; set; } = new EmailConfig();
        public WebhookConfig Webhook { get; set; } = new WebhookConfig();

        private static string FilePath => ServerPath.Get(ServerPath.FolderName.Configs, "notifications.json");

        public static NotificationConfig Load()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path))
                {
                    var def = new NotificationConfig();
                    def.Save(); // create a disabled template the user can edit
                    return def;
                }

                var cfg = JsonConvert.DeserializeObject<NotificationConfig>(File.ReadAllText(path)) ?? new NotificationConfig();
                if (cfg.Ntfy == null) { cfg.Ntfy = new NtfyConfig(); }
                if (cfg.Telegram == null) { cfg.Telegram = new TelegramConfig(); }
                if (cfg.Email == null) { cfg.Email = new EmailConfig(); }
                if (cfg.Webhook == null) { cfg.Webhook = new WebhookConfig(); }

                // Decrypt secrets for in-memory use.
                cfg.Ntfy.AuthToken = Secret.Unprotect(cfg.Ntfy.AuthToken);
                cfg.Telegram.BotToken = Secret.Unprotect(cfg.Telegram.BotToken);
                cfg.Email.Password = Secret.Unprotect(cfg.Email.Password);
                cfg.Webhook.AuthBearer = Secret.Unprotect(cfg.Webhook.AuthBearer);
                return cfg;
            }
            catch (Exception e)
            {
                AppLog.Warn("NotificationConfig/Load", e.Message);
                return new NotificationConfig();
            }
        }

        public void Save()
        {
            try
            {
                string Enc(string v) => Secret.IsProtected(v) ? v : Secret.Protect(v);

                // "Disk" copy with encrypted secrets, without altering the in-memory instance.
                var onDisk = new NotificationConfig
                {
                    Ntfy = new NtfyConfig
                    {
                        Enabled = Ntfy.Enabled, ServerUrl = Ntfy.ServerUrl, Topic = Ntfy.Topic,
                        AuthToken = Enc(Ntfy.AuthToken), Priority = Ntfy.Priority
                    },
                    Telegram = new TelegramConfig
                    {
                        Enabled = Telegram.Enabled, BotToken = Enc(Telegram.BotToken), ChatId = Telegram.ChatId
                    },
                    Email = new EmailConfig
                    {
                        Enabled = Email.Enabled, SmtpHost = Email.SmtpHost, SmtpPort = Email.SmtpPort,
                        UseSsl = Email.UseSsl, Username = Email.Username, Password = Enc(Email.Password),
                        From = Email.From, To = Email.To
                    },
                    Webhook = new WebhookConfig
                    {
                        Enabled = Webhook.Enabled, Url = Webhook.Url, AuthBearer = Enc(Webhook.AuthBearer)
                    }
                };

                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(onDisk, Formatting.Indented));
            }
            catch (Exception e)
            {
                AppLog.Warn("NotificationConfig/Save", e.Message);
            }
        }
    }
}
