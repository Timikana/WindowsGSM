using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowsGSM.Functions.Controls;
using WindowsGSM.Functions.Localization;

namespace WindowsGSM.Functions.Palworld
{
    /// <summary>
    /// Live admin panel for a Palworld server, driven by its built-in REST API:
    /// player list + kick/ban, in-game announce, manual save, and graceful shutdown
    /// with a countdown. Theme-aware (works in light/dark).
    /// </summary>
    public sealed class PalworldAdminDialog : Window
    {
        private static Brush Fg => DialogTheme.Fg;
        private static Brush Dim => DialogTheme.Dim;
        private static Brush Accent => DialogTheme.Accent;
        private static Brush Warn => DialogTheme.Warn;

        private readonly PalworldAdmin _api;
        private readonly string _host;
        private readonly int _rconPort;
        private readonly string _password;
        private readonly ListBox _players;
        private readonly List<PalPlayer> _current = new List<PalPlayer>();
        private readonly TextBlock _status;
        private readonly Wpf.Ui.Controls.TextBox _announce;
        private readonly Wpf.Ui.Controls.TextBox _shutdownMsg;
        private readonly Wpf.Ui.Controls.NumberBox _shutdownSecs;
        private readonly Wpf.Ui.Controls.TextBox _rconInput;
        private readonly TextBox _rconOutput;

        public PalworldAdminDialog(string serverName, string host, int restPort, string adminPassword, int rconPort)
        {
            _api = new PalworldAdmin(host, restPort, adminPassword);
            _host = string.IsNullOrWhiteSpace(host) || host == "0.0.0.0" ? "127.0.0.1" : host;
            _rconPort = rconPort;
            _password = adminPassword;

            Title = Loc.T("PalAdmin.Title", serverName ?? "Palworld");
            Width = 620; Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = DialogTheme.Bg;
            NativeTheme.EnableDarkTitleBar(this);

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // players
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // actions
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // status + close

            // ---- header ----
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(new TextBlock { Text = Loc.T("PalAdmin.Title", serverName ?? "Palworld"), Foreground = Accent, FontWeight = FontWeights.SemiBold, FontSize = 15 });
            header.Children.Add(new TextBlock { Text = Loc.T("PalAdmin.Subtitle"), Foreground = Dim, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ---- players card ----
            var playersCard = Card();
            Grid.SetRow(playersCard, 1);
            var pcContent = new DockPanel { Margin = new Thickness(12) };

            var pcHead = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 6) };
            var pcTitle = new TextBlock { Text = Loc.T("PalAdmin.Players"), Foreground = Fg, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(pcTitle, Dock.Left);
            pcHead.Children.Add(pcTitle);
            var refreshBtn = Btn(Loc.T("PalAdmin.Refresh"), Wpf.Ui.Controls.ControlAppearance.Secondary);
            refreshBtn.Click += async (s, e) => await RefreshPlayers();
            DockPanel.SetDock(refreshBtn, Dock.Right);
            pcHead.Children.Add(refreshBtn);
            DockPanel.SetDock(pcHead, Dock.Top);
            pcContent.Children.Add(pcHead);

            var rowBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var kickBtn = Btn(Loc.T("PalAdmin.Kick"), Wpf.Ui.Controls.ControlAppearance.Caution);
            kickBtn.Click += async (s, e) => await KickOrBan(false);
            var banBtn = Btn(Loc.T("PalAdmin.Ban"), Wpf.Ui.Controls.ControlAppearance.Danger);
            banBtn.Click += async (s, e) => await KickOrBan(true);
            var tpMeBtn = Btn(Loc.T("PalAdmin.TpToMe"), Wpf.Ui.Controls.ControlAppearance.Secondary);
            tpMeBtn.ToolTip = Loc.T("PalAdmin.TpToMeTip");
            tpMeBtn.Click += async (s, e) => await Teleport(toMe: true);
            var tpPlayerBtn = Btn(Loc.T("PalAdmin.TpToPlayer"), Wpf.Ui.Controls.ControlAppearance.Secondary);
            tpPlayerBtn.ToolTip = Loc.T("PalAdmin.TpToPlayerTip");
            tpPlayerBtn.Click += async (s, e) => await Teleport(toMe: false);
            rowBtns.Children.Add(kickBtn); rowBtns.Children.Add(banBtn);
            rowBtns.Children.Add(tpMeBtn); rowBtns.Children.Add(tpPlayerBtn);
            DockPanel.SetDock(rowBtns, Dock.Bottom);
            pcContent.Children.Add(rowBtns);

            _players = new ListBox { Background = DialogTheme.CardBg, Foreground = Fg, BorderBrush = DialogTheme.CardBorder, Margin = new Thickness(0, 0, 0, 0) };
            pcContent.Children.Add(_players);
            playersCard.Child = pcContent;
            root.Children.Add(playersCard);

            // ---- actions card ----
            var actionsCard = Card();
            Grid.SetRow(actionsCard, 2);
            var ac = new StackPanel { Margin = new Thickness(12) };

            ac.Children.Add(new TextBlock { Text = Loc.T("PalAdmin.Broadcast"), Foreground = Fg, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var annRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) };
            var annBtn = Btn(Loc.T("PalAdmin.Announce"), Wpf.Ui.Controls.ControlAppearance.Primary);
            annBtn.Click += async (s, e) => await Announce();
            DockPanel.SetDock(annBtn, Dock.Right);
            annBtn.Margin = new Thickness(6, 0, 0, 0);
            annRow.Children.Add(annBtn);
            _announce = new Wpf.Ui.Controls.TextBox { PlaceholderText = Loc.T("PalAdmin.AnnouncePlaceholder") };
            annRow.Children.Add(_announce);
            ac.Children.Add(annRow);

            var opsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var saveBtn = Btn(Loc.T("PalAdmin.Save"), Wpf.Ui.Controls.ControlAppearance.Secondary);
            saveBtn.Click += async (s, e) => await SaveWorld();
            opsRow.Children.Add(saveBtn);
            ac.Children.Add(opsRow);

            ac.Children.Add(new TextBlock { Text = Loc.T("PalAdmin.Shutdown"), Foreground = Fg, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var sdRow = new StackPanel { Orientation = Orientation.Horizontal };
            _shutdownSecs = new Wpf.Ui.Controls.NumberBox { Value = 30, Minimum = 1, Maximum = 3600, Width = 90, ClearButtonEnabled = false };
            sdRow.Children.Add(new TextBlock { Text = Loc.T("PalAdmin.ShutdownSeconds"), Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            sdRow.Children.Add(_shutdownSecs);
            _shutdownMsg = new Wpf.Ui.Controls.TextBox { PlaceholderText = Loc.T("PalAdmin.ShutdownMsgPlaceholder"), Width = 250, Margin = new Thickness(8, 0, 6, 0) };
            sdRow.Children.Add(_shutdownMsg);
            var sdBtn = Btn(Loc.T("PalAdmin.ShutdownBtn"), Wpf.Ui.Controls.ControlAppearance.Danger);
            sdBtn.Click += async (s, e) => await Shutdown();
            sdRow.Children.Add(sdBtn);
            ac.Children.Add(sdRow);

            // ---- free-form RCON console ----
            ac.Children.Add(new TextBlock { Text = Loc.T("PalAdmin.Console"), Foreground = Fg, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 2) });
            ac.Children.Add(new TextBlock { Text = Loc.T("PalAdmin.ConsoleHint"), Foreground = Dim, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
            var conRow = new DockPanel { LastChildFill = true };
            var sendBtn = Btn(Loc.T("PalAdmin.Send"), Wpf.Ui.Controls.ControlAppearance.Primary);
            sendBtn.Margin = new Thickness(6, 0, 0, 0);
            sendBtn.Click += async (s, e) => await SendRcon();
            DockPanel.SetDock(sendBtn, Dock.Right);
            conRow.Children.Add(sendBtn);
            _rconInput = new Wpf.Ui.Controls.TextBox { PlaceholderText = Loc.T("PalAdmin.ConsolePlaceholder"), FontFamily = new FontFamily("Consolas") };
            _rconInput.KeyDown += async (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) { await SendRcon(); } };
            conRow.Children.Add(_rconInput);
            ac.Children.Add(conRow);
            _rconOutput = new TextBox
            {
                IsReadOnly = true,
                Height = 88,
                Margin = new Thickness(0, 6, 0, 0),
                Background = DialogTheme.CardBg,
                Foreground = Fg,
                BorderBrush = DialogTheme.CardBorder,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            ac.Children.Add(_rconOutput);

            actionsCard.Child = ac;
            root.Children.Add(actionsCard);

            // ---- status + close ----
            var bottom = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 10, 0, 0) };
            var closeBtn = Btn(Loc.T("Common.Close"), Wpf.Ui.Controls.ControlAppearance.Secondary);
            closeBtn.IsCancel = true;
            closeBtn.Click += (s, e) => Close();
            DockPanel.SetDock(closeBtn, Dock.Right);
            bottom.Children.Add(closeBtn);
            _status = new TextBlock { Foreground = Dim, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            bottom.Children.Add(_status);
            Grid.SetRow(bottom, 3);
            root.Children.Add(bottom);

            Content = root;
            Loaded += async (s, e) => await RefreshPlayers();
        }

        private static Border Card() => new Border
        {
            Background = DialogTheme.CardBg,
            BorderBrush = DialogTheme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 10)
        };

        private static Wpf.Ui.Controls.Button Btn(string text, Wpf.Ui.Controls.ControlAppearance appearance) => new Wpf.Ui.Controls.Button
        {
            Content = text,
            Appearance = appearance,
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 6, 0)
        };

        private void SetStatus(string text, bool error = false)
        {
            _status.Foreground = error ? Warn : Accent;
            _status.Text = text;
        }

        private async System.Threading.Tasks.Task RefreshPlayers()
        {
            SetStatus(Loc.T("PalAdmin.Loading"));
            var (ok, players, err) = await _api.GetPlayersAsync();
            _players.Items.Clear();
            _current.Clear();
            if (!ok)
            {
                SetStatus(Loc.T("PalAdmin.Error", err ?? "?"), true);
                return;
            }
            _current.AddRange(players);
            foreach (var p in players)
            {
                _players.Items.Add($"{p.Name}   —   Lv {p.Level}   —   {p.Ping} ms   —   {p.UserId}");
            }
            SetStatus(Loc.T("PalAdmin.PlayerCount", players.Count));
        }

        private PalPlayer Selected()
        {
            int i = _players.SelectedIndex;
            return (i >= 0 && i < _current.Count) ? _current[i] : null;
        }

        private async System.Threading.Tasks.Task KickOrBan(bool ban)
        {
            var p = Selected();
            if (p == null) { SetStatus(Loc.T("PalAdmin.SelectPlayer"), true); return; }
            string confirmKey = ban ? "PalAdmin.ConfirmBan" : "PalAdmin.ConfirmKick";
            if (MessageBox.Show(Loc.T(confirmKey, p.Name), Loc.T("Common.ConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { return; }

            var (ok, err) = ban
                ? await _api.BanAsync(p.UserId, Loc.T("PalAdmin.BanReason"))
                : await _api.KickAsync(p.UserId, Loc.T("PalAdmin.KickReason"));
            if (ok) { SetStatus(Loc.T(ban ? "PalAdmin.Banned" : "PalAdmin.Kicked", p.Name)); await RefreshPlayers(); }
            else { SetStatus(Loc.T("PalAdmin.Error", err ?? "?"), true); }
        }

        private async System.Threading.Tasks.Task Announce()
        {
            string msg = _announce.Text?.Trim();
            if (string.IsNullOrEmpty(msg)) { SetStatus(Loc.T("PalAdmin.EmptyMessage"), true); return; }
            var (ok, err) = await _api.AnnounceAsync(msg);
            if (ok) { SetStatus(Loc.T("PalAdmin.Announced")); _announce.Text = ""; }
            else { SetStatus(Loc.T("PalAdmin.Error", err ?? "?"), true); }
        }

        private async System.Threading.Tasks.Task SaveWorld()
        {
            SetStatus(Loc.T("PalAdmin.Saving"));
            var (ok, err) = await _api.SaveAsync();
            SetStatus(ok ? Loc.T("PalAdmin.Saved") : Loc.T("PalAdmin.Error", err ?? "?"), !ok);
        }

        private async System.Threading.Tasks.Task Shutdown()
        {
            int secs = (int)Math.Max(1, _shutdownSecs.Value ?? 30);
            string msg = _shutdownMsg.Text?.Trim();
            if (string.IsNullOrEmpty(msg)) { msg = Loc.T("PalAdmin.DefaultShutdownMsg"); }
            if (MessageBox.Show(Loc.T("PalAdmin.ConfirmShutdown", secs), Loc.T("Common.ConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { return; }
            var (ok, err) = await _api.ShutdownAsync(secs, msg);
            SetStatus(ok ? Loc.T("PalAdmin.ShutdownScheduled", secs) : Loc.T("PalAdmin.Error", err ?? "?"), !ok);
        }

        // ---- RCON (teleport + free-form console) ----

        private async System.Threading.Tasks.Task<(bool ok, string text)> RconExecAsync(string command)
        {
            if (_rconPort <= 0 || string.IsNullOrEmpty(_password)) { return (false, Loc.T("PalAdmin.RconOff")); }
            using (var rc = new RconClient(_host, _rconPort))
            {
                var (ok, err) = await rc.ConnectAsync(_password);
                if (!ok) { return (false, err); }
                return await rc.ExecuteAsync(command);
            }
        }

        private static string SteamIdOf(PalPlayer p)
        {
            if (p == null) { return null; }
            if (!string.IsNullOrEmpty(p.SteamId)) { return p.SteamId; }
            if (!string.IsNullOrEmpty(p.UserId) && p.UserId.StartsWith("steam_", StringComparison.OrdinalIgnoreCase)) { return p.UserId.Substring(6); }
            return p.UserId;
        }

        private async System.Threading.Tasks.Task Teleport(bool toMe)
        {
            var p = Selected();
            if (p == null) { SetStatus(Loc.T("PalAdmin.SelectPlayer"), true); return; }
            string steamId = SteamIdOf(p);
            string cmd = (toMe ? "TeleportToMe " : "TeleportToPlayer ") + steamId;
            var (ok, text) = await RconExecAsync(cmd);
            AppendConsole("> " + cmd + "\r\n" + text);
            SetStatus(ok ? Loc.T("PalAdmin.TpDone", p.Name) : Loc.T("PalAdmin.Error", text), !ok);
        }

        private async System.Threading.Tasks.Task SendRcon()
        {
            string cmd = _rconInput.Text?.Trim();
            if (string.IsNullOrEmpty(cmd)) { return; }
            var (ok, text) = await RconExecAsync(cmd);
            AppendConsole("> " + cmd + "\r\n" + (ok ? text : Loc.T("PalAdmin.Error", text)));
            if (ok) { _rconInput.Text = ""; }
        }

        private void AppendConsole(string s)
        {
            _rconOutput.AppendText(((s ?? "").TrimEnd()) + "\r\n");
            _rconOutput.ScrollToEnd();
        }
    }
}
