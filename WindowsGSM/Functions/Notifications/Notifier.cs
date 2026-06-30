using System.Collections.Generic;
using System.Threading.Tasks;

namespace WindowsGSM.Functions.Notifications
{
    /// <summary>
    /// Point d'entrée unique des canaux de notification globaux (en plus des webhooks Discord
    /// par-serveur). Diffuse un message texte vers tous les canaux activés. Best-effort : l'échec
    /// d'un canal n'empêche pas les autres, et n'interrompt jamais l'appelant.
    /// Palier 1 : ntfy. Brancher ici Telegram / e-mail / webhook au palier 2.
    /// </summary>
    public static class Notifier
    {
        public static async Task Broadcast(string title, string message)
        {
            // Fonction réservée aux donateurs (ou au propriétaire). Verrou aussi côté envoi, pas seulement l'UI.
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
