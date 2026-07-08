using System.Threading.Tasks;

namespace WindowsGSM.Functions.Notifications
{
    /// <summary>
    /// Generic notification channel (ntfy, Telegram, email, webhook...). Lets you add new channels
    /// without touching the code that emits alerts.
    /// </summary>
    public interface INotifier
    {
        string Name { get; }
        bool IsEnabled { get; }
        Task<bool> SendAsync(string title, string message);
    }
}
