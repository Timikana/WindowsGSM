using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowsGSM.Functions.Localization;

namespace WindowsGSM.Functions.WebApi
{
    /// <summary>Web API config: enable, port, exposure, token. Fluent style (dark title bar).</summary>
    public class WebApiDialog : Window
    {
        private static Brush Fg => WindowsGSM.Functions.Controls.DialogTheme.Fg;
        private static Brush Dim => WindowsGSM.Functions.Controls.DialogTheme.Dim;
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x4c, 0xc2, 0xd6));
        private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xe0, 0xb0, 0x4c));

        private readonly Func<string> _onSaved; // returns null on success, or the bind error to show
        private readonly List<(string Id, string Name)> _servers;

        public WebApiDialog(Func<string> onSaved, IEnumerable<(string Id, string Name)> servers = null)
        {
            _onSaved = onSaved;
            _servers = (servers ?? Enumerable.Empty<(string, string)>()).ToList();
            var cfg = WebApiConfig.Load();

            Title = Loc.T("WebApi.Title");
            Width = 640; Height = 820;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = WindowsGSM.Functions.Controls.DialogTheme.Bg;
            NativeTheme.EnableDarkTitleBar(this);

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock { Text = Loc.T("WebApi.Header"), Foreground = Accent, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(new TextBlock { Text = Loc.T("WebApi.Intro"), Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

            var apiEnable = new Wpf.Ui.Controls.ToggleSwitch { Content = Loc.T("WebApi.EnableApi"), IsChecked = cfg.ApiEnabled, Foreground = Fg, Margin = new Thickness(0, 0, 0, 6) };
            root.Children.Add(apiEnable);
            root.Children.Add(new TextBlock { Text = Loc.T("WebApi.ApiRoutesHelp"), Foreground = Dim, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 8) });

            var portRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            portRow.Children.Add(new TextBlock { Text = Loc.T("WebApi.Port"), Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, MinWidth = 140, Margin = new Thickness(0, 0, 8, 0) });
            var portBox = new TextBox { Text = cfg.Port.ToString(), Width = 120 };
            portRow.Children.Add(portBox);
            root.Children.Add(portRow);

            var ipRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            ipRow.Children.Add(new TextBlock { Text = Loc.T("WebApi.ListenIp"), Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, MinWidth = 140, Margin = new Thickness(0, 0, 8, 0) });
            var ipBox = new TextBox { Text = cfg.BindAddress, Width = 180, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
            ipRow.Children.Add(ipBox);
            root.Children.Add(ipRow);
            root.Children.Add(new TextBlock
            {
                Text = Loc.T("WebApi.ListenIpHelp"),
                Foreground = Dim, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 10)
            });

            root.Children.Add(new TextBlock { Text = Loc.T("WebApi.TokenBearer"), Foreground = Fg, Margin = new Thickness(0, 0, 0, 4) });
            var tokenRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var tokenBox = new TextBox { Text = cfg.Token, Width = 420, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
            var gen = new Wpf.Ui.Controls.Button { Content = Loc.T("Common.Generate"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4) };
            gen.Click += (s, e) => { tokenBox.Text = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('='); };
            tokenRow.Children.Add(tokenBox);
            tokenRow.Children.Add(gen);
            root.Children.Add(tokenRow);

            // ---- Web portal (browser login + dashboard) ----
            root.Children.Add(new Border { Height = 1, Background = WindowsGSM.Functions.Controls.DialogTheme.CardBorder, Margin = new Thickness(0, 4, 0, 10) });
            root.Children.Add(new TextBlock { Text = Loc.T("WebApi.PortalHeader"), Foreground = Accent, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(new TextBlock { Text = Loc.T("WebApi.PortalHelp"), Foreground = Dim, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 8) });
            var webUi = new Wpf.Ui.Controls.ToggleSwitch { Content = Loc.T("WebApi.EnablePortal"), IsChecked = cfg.WebUiEnabled, Foreground = Fg, Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(webUi);
            root.Children.Add(new TextBlock { Text = Loc.T("WebApi.DonatorFeature"), Foreground = Accent, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });

            var webPortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            webPortRow.Children.Add(new TextBlock { Text = Loc.T("WebApi.PortalPort"), Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) });
            var webPortBox = new TextBox { Text = cfg.WebUiPort.ToString(), Width = 90, VerticalAlignment = VerticalAlignment.Center };
            webPortRow.Children.Add(webPortBox);
            webPortRow.Children.Add(new TextBlock { Text = Loc.T("WebApi.Ip"), Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, MinWidth = 30, Margin = new Thickness(14, 0, 8, 0) });
            var webIpBox = new TextBox { Text = cfg.WebUiBindAddress, Width = 150, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
            webPortRow.Children.Add(webIpBox);
            root.Children.Add(webPortRow);
            root.Children.Add(new TextBlock { Text = Loc.T("WebApi.PortalPortHelp"), Foreground = Dim, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
            // The web portal (auth + roles) is donator-only (like multi-channel notifications).
            webUi.Checked += (s, e) =>
            {
                if (!Donator.DonatorManager.IsDonator)
                {
                    var d = new Donator.DonatorDialog(Loc.T("WebApi.PortalDonatorFeatureName")) { Owner = this };
                    if (d.ShowDialog() != true) { webUi.IsChecked = false; }
                }
            };
            var usersBtn = new Wpf.Ui.Controls.Button { Content = Loc.T("WebApi.WebAccounts"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 0, 12) };
            usersBtn.Click += (s, e) =>
            {
                if (!Donator.DonatorManager.IsDonator)
                {
                    var dd = new Donator.DonatorDialog(Loc.T("WebApi.PortalDonatorFeatureName")) { Owner = this };
                    if (dd.ShowDialog() != true) { return; }
                }
                var d = new WebUsersDialog(_servers) { Owner = this }; d.ShowDialog();
            };
            root.Children.Add(usersBtn);
            var cookieSecure = new Wpf.Ui.Controls.ToggleSwitch { Content = Loc.T("WebApi.CookieSecure"), IsChecked = cfg.CookieSecure, Foreground = Fg, Margin = new Thickness(0, 0, 0, 12) };
            root.Children.Add(cookieSecure);

            root.Children.Add(new TextBlock
            {
                Text = Loc.T("WebApi.SecurityBestPractices"),
                Foreground = Warn, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 12)
            });

            var status = new TextBlock { Foreground = Dim, TextWrapping = TextWrapping.Wrap, MinHeight = 18, Margin = new Thickness(0, 0, 0, 10) };
            root.Children.Add(status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Wpf.Ui.Controls.Button { Content = Loc.T("WebApi.SaveApply"), Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 6, 0) };
            var close = new Wpf.Ui.Controls.Button { Content = Loc.T("Common.Close"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5) };
            save.Click += (s, e) =>
            {
                bool wantPortal = webUi.IsChecked == true;
                bool wantApi = apiEnable.IsChecked == true;
                if (wantApi && string.IsNullOrWhiteSpace(tokenBox.Text))
                {
                    status.Foreground = Warn; status.Text = Loc.T("WebApi.NeedToken");
                    return;
                }
                if (wantPortal && WebUsers.Load().Users.Count == 0)
                {
                    status.Foreground = Warn; status.Text = Loc.T("WebApi.NeedAccount");
                    return;
                }
                if (!int.TryParse(portBox.Text.Trim(), out int port) || port < 1 || port > 65535)
                {
                    status.Foreground = Warn; status.Text = Loc.T("WebApi.InvalidApiPort");
                    return;
                }
                if (!int.TryParse(webPortBox.Text.Trim(), out int webPort) || webPort < 1 || webPort > 65535)
                {
                    status.Foreground = Warn; status.Text = Loc.T("WebApi.InvalidPortalPort");
                    return;
                }
                // No master switch: the server runs whenever the API and/or the portal is enabled.
                var c = new WebApiConfig { Enabled = wantApi || wantPortal, ApiEnabled = wantApi, WebUiEnabled = wantPortal, CookieSecure = cookieSecure.IsChecked == true, Port = port, BindAddress = string.IsNullOrWhiteSpace(ipBox.Text) ? "127.0.0.1" : ipBox.Text.Trim(), WebUiPort = webPort, WebUiBindAddress = string.IsNullOrWhiteSpace(webIpBox.Text) ? "127.0.0.1" : webIpBox.Text.Trim(), Token = tokenBox.Text.Trim() };
                c.Save();
                string startError = null;
                try { startError = _onSaved?.Invoke(); } catch (Exception ex) { startError = ex.Message; }
                if (!string.IsNullOrEmpty(startError))
                {
                    // The listener failed to bind (port in use, missing urlacl…) — don't show a false success.
                    status.Foreground = Warn;
                    status.Text = Loc.T("WebApi.StartFailed", startError);
                    return;
                }
                status.Foreground = Accent;
                status.Text = !c.Enabled ? Loc.T("WebApi.StatusNothing")
                    : c.WebUiEnabled ? Loc.T("WebApi.StatusPortal", c.WebUiBindAddress, webPort)
                    : Loc.T("WebApi.StatusApi", c.BindAddress, port);
            };
            close.Click += (s, e) => Close();
            buttons.Children.Add(save);
            buttons.Children.Add(close);
            root.Children.Add(buttons);

            Content = root;
        }
    }
}
