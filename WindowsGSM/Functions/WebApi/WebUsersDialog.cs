using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsGSM.Functions.WebApi
{
    /// <summary>Gestion des comptes du portail web (login, rôle, serveurs autorisés). Mots de passe hashés PBKDF2.</summary>
    public class WebUsersDialog : Window
    {
        private static readonly Brush Fg = Brushes.White;
        private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x9a, 0x9a, 0x9a));
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x4c, 0xc2, 0xd6));

        private readonly WebUsers _store;
        private readonly ListBox _list;
        private readonly TextBox _user;
        private readonly PasswordBox _pass;
        private readonly ComboBox _role;
        private readonly TextBox _servers;
        private readonly TextBlock _status;

        public WebUsersDialog()
        {
            _store = WebUsers.Load();

            Title = "Comptes du portail web";
            Width = 560; Height = 540;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0x1f, 0x1f, 0x1f));
            NativeTheme.EnableDarkTitleBar(this);

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(new TextBlock { Text = "Comptes du portail web", Foreground = Accent, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(new TextBlock { Text = "Rôles : Viewer (lecture) · Operator (+ start/stop/restart/backup) · Admin (tout). Serveurs : « * » = tous, ou « 1,3,4 ».", Foreground = Dim, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 10) });

            _list = new ListBox { Height = 180, Background = new SolidColorBrush(Color.FromRgb(0x1b, 0x1b, 0x1b)), Foreground = Fg, BorderBrush = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a)) };
            _list.SelectionChanged += (s, e) => { if (_list.SelectedItem is string line) { var u = _store.Users.FirstOrDefault(x => line.StartsWith(x.Username + "  ")); if (u != null) { _user.Text = u.Username; _role.SelectedIndex = (int)u.Role; _servers.Text = u.ServerIds; _pass.Password = string.Empty; } } };
            root.Children.Add(_list);

            var f1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 6) };
            f1.Children.Add(new TextBlock { Text = "Utilisateur", Foreground = Fg, Width = 90, VerticalAlignment = VerticalAlignment.Center });
            _user = new TextBox { Width = 160 };
            f1.Children.Add(_user);
            f1.Children.Add(new TextBlock { Text = "Mot de passe", Foreground = Fg, Width = 100, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            _pass = new PasswordBox { Width = 150 };
            f1.Children.Add(_pass);
            root.Children.Add(f1);

            var f2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            f2.Children.Add(new TextBlock { Text = "Rôle", Foreground = Fg, Width = 90, VerticalAlignment = VerticalAlignment.Center });
            _role = new ComboBox { Width = 160 };
            _role.Items.Add("Viewer"); _role.Items.Add("Operator"); _role.Items.Add("Admin"); _role.SelectedIndex = 0;
            f2.Children.Add(_role);
            f2.Children.Add(new TextBlock { Text = "Serveurs", Foreground = Fg, Width = 100, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            _servers = new TextBox { Width = 150, Text = "*" };
            f2.Children.Add(_servers);
            root.Children.Add(f2);

            _status = new TextBlock { Foreground = Dim, MinHeight = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(_status);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var addBtn = new Wpf.Ui.Controls.Button { Content = "Ajouter / Mettre à jour", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 6, 0) };
            var delBtn = new Wpf.Ui.Controls.Button { Content = "Supprimer", Appearance = Wpf.Ui.Controls.ControlAppearance.Danger, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 6, 0) };
            var closeBtn = new Wpf.Ui.Controls.Button { Content = "Fermer", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(14, 5, 14, 5) };
            addBtn.Click += (s, e) => AddOrUpdate();
            delBtn.Click += (s, e) => { if (!string.IsNullOrWhiteSpace(_user.Text)) { _store.Remove(_user.Text.Trim()); _store.Save(); Refresh(); _status.Text = "Compte supprimé."; } };
            closeBtn.Click += (s, e) => Close();
            btns.Children.Add(addBtn); btns.Children.Add(delBtn); btns.Children.Add(closeBtn);
            root.Children.Add(btns);

            Content = root;
            Refresh();
        }

        private void AddOrUpdate()
        {
            string user = _user.Text.Trim();
            if (string.IsNullOrWhiteSpace(user)) { _status.Foreground = Brushes.OrangeRed; _status.Text = "Nom d'utilisateur requis."; return; }
            bool exists = _store.Users.Any(x => string.Equals(x.Username, user, StringComparison.OrdinalIgnoreCase));
            if (!exists && string.IsNullOrEmpty(_pass.Password)) { _status.Foreground = Brushes.OrangeRed; _status.Text = "Mot de passe requis pour un nouveau compte."; return; }
            var role = (WebRole)Math.Max(0, _role.SelectedIndex);
            _store.Set(user, _pass.Password, role, string.IsNullOrWhiteSpace(_servers.Text) ? "*" : _servers.Text.Trim());
            _store.Save();
            _pass.Password = string.Empty;
            Refresh();
            _status.Foreground = Accent;
            _status.Text = $"Compte « {user} » enregistré ({role}).";
        }

        private void Refresh()
        {
            _list.Items.Clear();
            foreach (var u in _store.Users.OrderBy(x => x.Username))
            {
                _list.Items.Add($"{u.Username}  —  {u.Role}  —  serveurs: {u.ServerIds}");
            }
        }
    }
}
