using System.Collections.Generic;
using System.Threading.Tasks;

namespace WindowsGSM.Functions.Notifications
{
    /// <summary>
    /// Single entry point for the global notification channels (in addition to the per-server
    /// Discord webhooks). Broadcasts a text message to all enabled channels. Best-effort: one
    /// channel's failure does not prevent the others, and never interrupts the caller.
    /// Tier 1: ntfy. Wire up Telegram / email / webhook here at tier 2.
    /// </summary>
    public static class Notifier
    {
        public static async Task Broadcast(string title, string message)
        {
            // Donor-only feature (or owner). Lock on the send side too, not only the UI.
            if (!Donator.DonatorManager.IsDonator) { return; }

            var cfg = NotificationConfig.Load();

            var notifiers = new List<INotifier>
            {
                new NtfyNotifier(cfg.Ntfy),
                new TelegramNotifier(cfg.Telegram),
                new EmailNotifier(cfg.Email),
                new WebhookNotifier(cfg.Webhook),
            };

            foreach (var n in notifiers)
            {
                if (!n.IsEnabled) { continue; }
                try { await n.SendAsync(title, message); } catch { }
            }
        }
    }
}
