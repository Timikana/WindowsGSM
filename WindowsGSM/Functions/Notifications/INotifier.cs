using System.Threading.Tasks;

namespace WindowsGSM.Functions.Notifications
{
    /// <summary>
    /// Canal de notification générique (ntfy, Telegram, e-mail, webhook…). Permet d'ajouter de
    /// nouveaux canaux sans toucher au code qui émet les alertes.
    /// </summary>
    public interface INotifier
    {
        string Name { get; }
        bool IsEnabled { get; }
        Task<bool> SendAsync(string title, string message);
    }
}
