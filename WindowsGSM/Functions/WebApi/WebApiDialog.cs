using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsGSM.Functions.WebApi
{
    /// <summary>Web API config: enable, port, exposure, token. Fluent style (dark title bar).</summary>
    public class WebApiDialog : Window
    {
        private static readonly Brush Fg = Brushes.White;
        private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x9a, 0x9a, 0x9a));
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x4c, 0xc2, 0xd6));
        private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xe0, 0xb0, 0x4c));

        private readonly Action _onSaved;
        private readonly List<(string Id, string Name)> _servers;

        public WebApiDialog(Action onSaved, IEnumerable<(string Id, string Name)> servers = null)
        {
            _onSaved = onSaved;
            _servers = (servers ?? Enumerable.Empty<(string, string)>()).ToList();
            var cfg = WebApiConfig.Load();

            Title = "Web API (remote control)";
            Width = 640; Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0x1f, 0x1f, 0x1f));
            NativeTheme.EnableDarkTitleBar(this);

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock { Text = "Remote control (web server)", Foreground = Accent, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(new TextBlock { Text = "Two independent parts you can toggle separately: the token API and the browser portal. The server runs as soon as at least one is enabled — no separate master switch.", Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

            var apiEnable = new Wpf.Ui.Controls.ToggleSwitch { Content = "Enable the token API", IsChecked = cfg.ApiEnabled, Foreground = Fg, Margin = new Thickness(0, 0, 0, 6) };
            root.Children.Add(apiEnable);
            root.Children.Add(new TextBlock { Text = "GET /api/servers · POST /api/servers/{id}/{start|stop|restart|backup}. Requires a Bearer token (below).", Foreground = Dim, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 8) });

            var portRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            portRow.Children.Add(new TextBlock { Text = "Port:", Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, Width = 140 });
            var portBox = new TextBox { Text = cfg.Port.ToString(), Width = 120 };
            portRow.Children.Add(portBox);
            root.Children.Add(portRow);

            var ipRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            ipRow.Children.Add(new TextBlock { Text = "Listen IP:", Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, Width = 140 });
            var ipBox = new TextBox { Text = cfg.BindAddress, Width = 180, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
            ipRow.Children.Add(ipBox);
            root.Children.Add(ipRow);
            root.Children.Add(new TextBlock
            {
                Text = "127.0.0.1 = this machine only (no admin/firewall needed) — use this if the client is local.\n" +
                       "0.0.0.0 / + = all interfaces (LAN/internet): requires running WGSM as administrator (or a netsh urlacl) and an open inbound firewall port.\n" +
                       "You can also set a specific machine IP. NB: browsers cannot connect TO 0.0.0.0 — reach the server at 127.0.0.1 or the machine's real IP.",
                Foreground = Dim, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 10)
            });

            root.Children.Add(new TextBlock { Text = "Token (Bearer):", Foreground = Fg, Margin = new Thickness(0, 0, 0, 4) });
            var tokenRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var tokenBox = new TextBox { Text = cfg.Token, Width = 420, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
            var gen = new Wpf.Ui.Controls.Button { Content = "Generate", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4) };
            gen.Click += (s, e) => { tokenBox.Text = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('='); };
            tokenRow.Children.Add(tokenBox);
            tokenRow.Children.Add(gen);
            root.Children.Add(tokenRow);

            // ---- Web portal (browser login + dashboard) ----
            root.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a)), Margin = new Thickness(0, 4, 0, 10) });
            root.Children.Add(new TextBlock { Text = "Web portal (browser interface)", Foreground = Accent, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(new TextBlock { Text = "Login page + dashboard (start/stop/restart/backup) with accounts and roles. Auth via session cookie (HttpOnly, SameSite=Strict).", Foreground = Dim, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 8) });
            var webUi = new Wpf.Ui.Controls.ToggleSwitch { Content = "Enable the web portal", IsChecked = cfg.WebUiEnabled, Foreground = Fg, Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(webUi);
            root.Children.Add(new TextBlock { Text = "★ Donator feature.", Foreground = Accent, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });

            var webPortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            webPortRow.Children.Add(new TextBlock { Text = "Portal port:", Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, Width = 90 });
            var webPortBox = new TextBox { Text = cfg.WebUiPort.ToString(), Width = 90, VerticalAlignment = VerticalAlignment.Center };
            webPortRow.Children.Add(webPortBox);
            webPortRow.Children.Add(new TextBlock { Text = "IP:", Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, Width = 30, Margin = new Thickness(14, 0, 0, 0) });
            var webIpBox = new TextBox { Text = cfg.WebUiBindAddress, Width = 150, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
            webPortRow.Children.Add(webIpBox);
            root.Children.Add(webPortRow);
            root.Children.Add(new TextBlock { Text = "Independent of the API above. Same IP:port as the API = single shared listener. Use \"0.0.0.0\" or \"+\" for all interfaces (LAN/internet) — that requires running WGSM as administrator (or a netsh urlacl) and an open firewall port.", Foreground = Dim, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
            // The web portal (auth + roles) is donator-only (like multi-channel notifications).
            webUi.Checked += (s, e) =>
            {
                if (!Donator.DonatorManager.IsDonator)
                {
                    var d = new Donator.DonatorDialog("Web portal (authentication + roles)") { Owner = this };
                    if (d.ShowDialog() != true) { webUi.IsChecked = false; }
                }
            };
            var usersBtn = new Wpf.Ui.Controls.Button { Content = "Web accounts…", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 0, 12) };
            usersBtn.Click += (s, e) =>
            {
                if (!Donator.DonatorManager.IsDonator)
                {
                    var dd = new Donator.DonatorDialog("Web portal (authentication + roles)") { Owner = this };
                    if (dd.ShowDialog() != true) { return; }
                }
                var d = new WebUsersDialog(_servers) { Owner = this }; d.ShowDialog();
            };
            root.Children.Add(usersBtn);
            var cookieSecure = new Wpf.Ui.Controls.ToggleSwitch { Content = "\"Secure\" session cookie (behind an HTTPS proxy)", IsChecked = cfg.CookieSecure, Foreground = Fg, Margin = new Thickness(0, 0, 0, 12) };
            root.Children.Add(cookieSecure);

            root.Children.Add(new TextBlock
            {
                Text = "🔒 Security best practices (OWASP):\n" +
                       "• The API is plain HTTP → for internet exposure, put it BEHIND AN HTTPS REVERSE-PROXY (nginx/Caddy/Traefik) that handles TLS; ideally keep listening on 127.0.0.1 and let the proxy talk to the LAN/internet. Then enable \"Secure cookie\".\n" +
                       "• Long random token (\"Generate\" button); never share it and rotate it regularly. (The token is NOT accepted in the URL.)\n" +
                       "• Restrict access at the firewall (only open the port to trusted IPs); add rate-limiting/Fail2ban on the proxy side.\n" +
                       "• Built in: PBKDF2 passwords, HttpOnly+SameSite=Strict cookie sessions, anti-CSRF (Origin check), hardened headers (CSP, nosniff, X-Frame-Options DENY, Permissions-Policy, hidden Server header), brute-force throttle (10 fails/5 min), strict action validation, audit log of logins/actions.\n" +
                       "• Listening outside 127.0.0.1 requires WGSM as administrator (or \"netsh http add urlacl\").",
                Foreground = Warn, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 12)
            });

            var status = new TextBlock { Foreground = Dim, TextWrapping = TextWrapping.Wrap, MinHeight = 18, Margin = new Thickness(0, 0, 0, 10) };
            root.Children.Add(status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Wpf.Ui.Controls.Button { Content = "Save & apply", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 6, 0) };
            var close = new Wpf.Ui.Controls.Button { Content = "Close", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5) };
            save.Click += (s, e) =>
            {
                bool wantPortal = webUi.IsChecked == true;
                bool wantApi = apiEnable.IsChecked == true;
                if (wantApi && string.IsNullOrWhiteSpace(tokenBox.Text))
                {
                    status.Foreground = Warn; status.Text = "Set a token (or click \"Generate\") to enable the API.";
                    return;
                }
                if (wantPortal && WebUsers.Load().Users.Count == 0)
                {
                    status.Foreground = Warn; status.Text = "Create at least one account (\"Web accounts…\") before enabling the portal.";
                    return;
                }
                if (!int.TryParse(portBox.Text.Trim(), out int port) || port < 1 || port > 65535)
                {
                    status.Foreground = Warn; status.Text = "Invalid API port.";
                    return;
                }
                if (!int.TryParse(webPortBox.Text.Trim(), out int webPort) || webPort < 1 || webPort > 65535)
                {
                    status.Foreground = Warn; status.Text = "Invalid portal port.";
                    return;
                }
                // No master switch: the server runs whenever the API and/or the portal is enabled.
                var c = new WebApiConfig { Enabled = wantApi || wantPortal, ApiEnabled = wantApi, WebUiEnabled = wantPortal, CookieSecure = cookieSecure.IsChecked == true, Port = port, BindAddress = string.IsNullOrWhiteSpace(ipBox.Text) ? "127.0.0.1" : ipBox.Text.Trim(), WebUiPort = webPort, WebUiBindAddress = string.IsNullOrWhiteSpace(webIpBox.Text) ? "127.0.0.1" : webIpBox.Text.Trim(), Token = tokenBox.Text.Trim() };
                c.Save();
                try { _onSaved?.Invoke(); } catch { }
                status.Foreground = Accent;
                status.Text = !c.Enabled ? "✔ Nothing enabled (set a token and/or enable the portal)."
                    : c.WebUiEnabled ? $"✔ Started. Web portal: http://{c.WebUiBindAddress}:{webPort}/ (sign in with an account)."
                    : $"✔ API started. Test: GET http://{c.BindAddress}:{port}/api/servers (header Authorization: Bearer <token>).";
            };
            close.Click += (s, e) => Close();
            buttons.Children.Add(save);
            buttons.Children.Add(close);
            root.Children.Add(buttons);

            Content = root;
        }
    }
}
