using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WindowsGSM.DiscordBot
{
    class AdminListItem : INotifyPropertyChanged
    {
        public string AdminId { get; set; }
        public string ServerIds { get; set; }

        // Résolu en async via le bot (REST /users/{id}) -> notifiant pour rafraîchir la cellule.
        private string _username = "…";
        public string Username
        {
            get => _username;
            set { if (_username != value) { _username = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
