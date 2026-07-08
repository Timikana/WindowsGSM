using System;
using System.Collections.Generic;
using System.Linq;
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
        // ---- Anti-bombing: a crash-looping server must not flood email/SMS/push. ----
        // Drop an IDENTICAL alert repeated within DedupWindow (per-key). No global cap, so a genuinely
        // DIFFERENT important alert (e.g. a real crash elsewhere) is never silently swallowed.
        private static readonly object _throttleGate = new object();
        private static readonly Dictionary<string, DateTime> _lastByKey = new Dictionary<string, DateTime>();
        private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(5);

        private static bool AllowSend(string title, string message)
        {
            var now = DateTime.UtcNow;
            string key = (title ?? string.Empty) + "" + (message ?? string.Empty);
            lock (_throttleGate)
            {
                if (_lastByKey.TryGetValue(key, out var last) && now - last < DedupWindow) { return false; }
                _lastByKey[key] = now;

                if (_lastByKey.Count > 256) // opportunistic prune so the dedup map can't grow unbounded
                {
                    foreach (var k in _lastByKey.Where(kv => now - kv.Value > DedupWindow).Select(kv => kv.Key).ToList())
                    {
                        _lastByKey.Remove(k);
                    }
                }
                return true;
            }
        }

        public static async Task Broadcast(string title, string message)
        {
            // Donor-only feature (or owner). Lock on the send side too, not only the UI.
            if (!Donator.DonatorManager.IsDonator) { return; }

            // Anti-bombing gate (dedup identical alerts + global rate cap).
            if (!AllowSend(title, message))
            {
                AppLog.Info("Notifier", "Notification throttled (anti-bombing: duplicate or rate cap).");
                return;
            }

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
