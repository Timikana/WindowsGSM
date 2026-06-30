using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace WindowsGSM.Functions.Notifications
{
    /// <summary>
    /// Email channel via SMTP (System.Net.Mail). Password encrypted at rest. Supports multiple
    /// recipients separated by comma or semicolon.
    /// </summary>
    public class EmailNotifier : INotifier
    {
        private readonly EmailConfig _cfg;

        public EmailNotifier(EmailConfig cfg) { _cfg = cfg ?? new EmailConfig(); }

        public string Name => "email";

        public bool IsEnabled =>
            _cfg.Enabled &&
            !string.IsNullOrWhiteSpace(_cfg.SmtpHost) &&
            !string.IsNullOrWhiteSpace(_cfg.From) &&
            !string.IsNullOrWhiteSpace(_cfg.To);

        public async Task<bool> SendAsync(string title, string message)
        {
            if (!IsEnabled) { return false; }

            try
            {
                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress(_cfg.From);
                    foreach (var addr in _cfg.To.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        mail.To.Add(addr.Trim());
                    }
                    mail.Subject = string.IsNullOrWhiteSpace(title) ? "WindowsGSM" : title;
                    mail.Body = message ?? string.Empty;

                    using (var smtp = new SmtpClient(_cfg.SmtpHost, _cfg.SmtpPort))
                    {
                        smtp.EnableSsl = _cfg.UseSsl;
                        if (!string.IsNullOrWhiteSpace(_cfg.Username))
                        {
                            smtp.Credentials = new NetworkCredential(_cfg.Username, _cfg.Password);
                        }
                        await smtp.SendMailAsync(mail);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                AppLog.Warn("Notifier/email", e.Message);
                return false;
            }
        }
    }
}
