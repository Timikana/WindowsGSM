using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Saisie du jeton d'API d'un serveur (ex. Satisfactory). Champ masqué, stocké chiffré (DPAPI) via
    /// <see cref="ApiToken"/>. Le jeton n'est JAMAIS saisi via le chat : uniquement ici.
    /// </summary>
    public class ApiTokenDialog : Window
    {
        private readonly string _serverId;
        private readonly PasswordBox _box;

        public ApiTokenDialog(string serverId, string serverName, bool isSatisfactory)
        {
            _serverId = serverId;

            Title = $"API Token — #{serverId} {serverName}";
            Width = 560;
            Height = 280;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x1f, 0x1f, 0x1f));
            NativeTheme.EnableDarkTitleBar(this);

            var outer = new DockPanel { Margin = new Thickness(14) };

            string help = isSatisfactory
                ? "Satisfactory : saisis le MOT DE PASSE du serveur (Client ou Admin) — WGSM obtient un jeton tout seul\nvia l'API pour lire le nombre de joueurs (lecture seule). Un token d'API (server.GenerateAPIToken) marche aussi."
                : "Jeton d'API utilisé pour interroger ce serveur. Colle-le ci-dessous.";

            var intro = new TextBlock
            {
                Text = help,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9a, 0x9a, 0x9a)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            DockPanel.SetDock(intro, Dock.Top);
            outer.Children.Add(intro);

            var label = new TextBlock { Text = "API Token", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(label, Dock.Top);
            outer.Children.Add(label);

            _box = new PasswordBox { Padding = new Thickness(8, 4, 8, 4), FontFamily = new FontFamily("Consolas") };
            string existing = ApiToken.Get(serverId);
            if (!string.IsNullOrEmpty(existing)) { _box.Password = existing; }
            DockPanel.SetDock(_box, Dock.Top);
            outer.Children.Add(_box);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var save = new Wpf.Ui.Controls.Button { Content = "Enregistrer", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            var clear = new Wpf.Ui.Controls.Button { Content = "Effacer", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            var close = new Wpf.Ui.Controls.Button { Content = "Fermer", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            save.Click += (s, e) => { ApiToken.Set(_serverId, _box.Password); DialogResult = true; Close(); };
            clear.Click += (s, e) => { _box.Password = string.Empty; };
            close.Click += (s, e) => Close();
            buttons.Children.Add(save);
            buttons.Children.Add(clear);
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            outer.Children.Add(buttons);

            Content = outer;
        }
    }
}
