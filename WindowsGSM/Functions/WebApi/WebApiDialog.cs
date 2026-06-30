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
            Width = 620; Height = 470;
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

            var bindAll = new Wpf.Ui.Controls.ToggleSwitch { Content = "Écouter sur toutes les interfaces (LAN/internet) — sinon localhost seulement", IsChecked = cfg.BindAll, Foreground = Fg, Margin = new Thickness(0, 0, 0, 10) };
            root.Children.Add(bindAll);

            root.Children.Add(new TextBlock { Text = "Token (Bearer) :", Foreground = Fg, Margin = new Thickness(0, 0, 0, 4) });
            var tokenRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var tokenBox = new TextBox { Text = cfg.Token, Width = 420, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
            var gen = new Wpf.Ui.Controls.Button { Content = "Générer", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4) };
            gen.Click += (s, e) => { tokenBox.Text = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('='); };
            tokenRow.Children.Add(tokenBox);
            tokenRow.Children.Add(gen);
            root.Children.Add(tokenRow);

            root.Children.Add(new TextBlock { Text = "⚠️ HTTP en clair : pour une exposition internet, place l'API derrière un reverse-proxy HTTPS (sinon le token circule en clair). Sur « toutes interfaces », lance WGSM en administrateur (ou réserve l'URL via netsh http add urlacl).", Foreground = Warn, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 12) });

            var status = new TextBlock { Foreground = Dim, TextWrapping = TextWrapping.Wrap, MinHeight = 18, Margin = new Thickness(0, 0, 0, 10) };
            root.Children.Add(status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Wpf.Ui.Controls.Button { Content = "Enregistrer & appliquer", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 6, 0) };
            var close = new Wpf.Ui.Controls.Button { Content = "Fermer", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5) };
            save.Click += (s, e) =>
            {
                if (enable.IsChecked == true && string.IsNullOrWhiteSpace(tokenBox.Text))
                {
                    status.Foreground = Warn; status.Text = "Un token est obligatoire pour activer l'API (clique « Générer »).";
                    return;
                }
                if (!int.TryParse(portBox.Text.Trim(), out int port) || port < 1 || port > 65535)
                {
                    status.Foreground = Warn; status.Text = "Port invalide.";
                    return;
                }
                var c = new WebApiConfig { Enabled = enable.IsChecked == true, Port = port, BindAll = bindAll.IsChecked == true, Token = tokenBox.Text.Trim() };
                c.Save();
                try { _onSaved?.Invoke(); } catch { }
                status.Foreground = Accent;
                status.Text = c.Enabled ? $"✔ API démarrée. Test : GET http://<ip>:{port}/api/servers (header Authorization: Bearer <token>)." : "✔ API désactivée.";
            };
            close.Click += (s, e) => Close();
            buttons.Children.Add(save);
            buttons.Children.Add(close);
            root.Children.Add(buttons);

            Content = root;
        }
    }
}
