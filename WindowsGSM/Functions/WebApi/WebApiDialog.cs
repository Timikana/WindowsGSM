using System;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsGSM.Functions.WebApi
{
    /// <summary>Config de l'API web : activer, port, exposition, token. Style Fluent (barre titre sombre).</summary>
    public class WebApiDialog : Window
    {
        private static readonly Brush Fg = Brushes.White;
        private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x9a, 0x9a, 0x9a));
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x4c, 0xc2, 0xd6));
        private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xe0, 0xb0, 0x4c));

        private readonly Action _onSaved;

        public WebApiDialog(Action onSaved)
        {
            _onSaved = onSaved;
            var cfg = WebApiConfig.Load();

            Title = "API web (contrôle à distance)";
            Width = 640; Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0x1f, 0x1f, 0x1f));
            NativeTheme.EnableDarkTitleBar(this);

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock { Text = "API web de contrôle à distance", Foreground = Accent, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(new TextBlock { Text = "GET /api/servers (état) · POST /api/servers/{id}/{start|stop|restart|backup}. Token Bearer obligatoire.", Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

            var enable = new Wpf.Ui.Controls.ToggleSwitch { Content = "Activer l'API", IsChecked = cfg.Enabled, Foreground = Fg, Margin = new Thickness(0, 0, 0, 10) };
            root.Children.Add(enable);

            var portRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            portRow.Children.Add(new TextBlock { Text = "Port :", Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, Width = 140 });
            var portBox = new TextBox { Text = cfg.Port.ToString(), Width = 120 };
            portRow.Children.Add(portBox);
            root.Children.Add(portRow);

            var ipRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            ipRow.Children.Add(new TextBlock { Text = "IP d'écoute :", Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, Width = 140 });
            var ipBox = new TextBox { Text = cfg.BindAddress, Width = 180, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
            ipRow.Children.Add(ipBox);
            ipRow.Children.Add(new TextBlock { Text = "127.0.0.1 = local · « + » = toutes interfaces · ou une IP précise", Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontSize = 11 });
            root.Children.Add(ipRow);

            root.Children.Add(new TextBlock { Text = "Token (Bearer) :", Foreground = Fg, Margin = new Thickness(0, 0, 0, 4) });
            var tokenRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var tokenBox = new TextBox { Text = cfg.Token, Width = 420, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
            var gen = new Wpf.Ui.Controls.Button { Content = "Générer", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4) };
            gen.Click += (s, e) => { tokenBox.Text = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('='); };
            tokenRow.Children.Add(tokenBox);
            tokenRow.Children.Add(gen);
            root.Children.Add(tokenRow);

            // ---- Portail web (login + dashboard navigateur) ----
            root.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a)), Margin = new Thickness(0, 4, 0, 10) });
            root.Children.Add(new TextBlock { Text = "Portail web (interface navigateur)", Foreground = Accent, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(new TextBlock { Text = "Page de connexion + tableau de bord (start/stop/restart/backup) avec comptes et rôles. Auth par cookie de session (HttpOnly, SameSite=Strict).", Foreground = Dim, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 8) });
            var webUi = new Wpf.Ui.Controls.ToggleSwitch { Content = "Activer le portail web", IsChecked = cfg.WebUiEnabled, Foreground = Fg, Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(webUi);
            var usersBtn = new Wpf.Ui.Controls.Button { Content = "Comptes web…", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 0, 12) };
            usersBtn.Click += (s, e) => { var d = new WebUsersDialog { Owner = this }; d.ShowDialog(); };
            root.Children.Add(usersBtn);

            root.Children.Add(new TextBlock
            {
                Text = "🔒 Bonnes pratiques sécurité :\n" +
                       "• L'API est en HTTP clair → pour une exposition internet, place-la DERRIÈRE UN REVERSE-PROXY HTTPS (nginx/Caddy/Traefik) qui gère le TLS ; idéalement garde l'écoute sur 127.0.0.1 et laisse le proxy parler au LAN/internet.\n" +
                       "• Token long et aléatoire (bouton « Générer ») ; ne le partage jamais et fais-le tourner régulièrement.\n" +
                       "• Restreins l'accès au pare-feu (n'ouvre le port qu'aux IP de confiance) ; ajoute du rate-limiting/Fail2ban côté proxy.\n" +
                       "• En-têtes de sécurité déjà envoyés par l'API (nosniff, X-Frame-Options DENY, CSP none, no-store) + throttle anti-brute-force intégré (blocage après 10 échecs/5 min).\n" +
                       "• Écouter hors 127.0.0.1 exige WGSM en administrateur (ou « netsh http add urlacl »).",
                Foreground = Warn, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 12)
            });

            var status = new TextBlock { Foreground = Dim, TextWrapping = TextWrapping.Wrap, MinHeight = 18, Margin = new Thickness(0, 0, 0, 10) };
            root.Children.Add(status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Wpf.Ui.Controls.Button { Content = "Enregistrer & appliquer", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 6, 0) };
            var close = new Wpf.Ui.Controls.Button { Content = "Fermer", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5) };
            save.Click += (s, e) =>
            {
                if (enable.IsChecked == true && string.IsNullOrWhiteSpace(tokenBox.Text) && webUi.IsChecked != true)
                {
                    status.Foreground = Warn; status.Text = "Fournis un token (API) ou active le portail web avec des comptes.";
                    return;
                }
                if (webUi.IsChecked == true && WebUsers.Load().Users.Count == 0)
                {
                    status.Foreground = Warn; status.Text = "Crée au moins un compte (« Comptes web… ») avant d'activer le portail.";
                    return;
                }
                if (!int.TryParse(portBox.Text.Trim(), out int port) || port < 1 || port > 65535)
                {
                    status.Foreground = Warn; status.Text = "Port invalide.";
                    return;
                }
                var c = new WebApiConfig { Enabled = enable.IsChecked == true, WebUiEnabled = webUi.IsChecked == true, Port = port, BindAddress = string.IsNullOrWhiteSpace(ipBox.Text) ? "127.0.0.1" : ipBox.Text.Trim(), Token = tokenBox.Text.Trim() };
                c.Save();
                try { _onSaved?.Invoke(); } catch { }
                status.Foreground = Accent;
                status.Text = !c.Enabled ? "✔ API désactivée."
                    : c.WebUiEnabled ? $"✔ Démarrée. Portail web : http://<ip>:{port}/ (connexion par compte)."
                    : $"✔ API démarrée. Test : GET http://<ip>:{port}/api/servers (header Authorization: Bearer <token>).";
            };
            close.Click += (s, e) => Close();
            buttons.Children.Add(save);
            buttons.Children.Add(close);
            root.Children.Add(buttons);

            Content = root;
        }
    }
}
