using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WindowsGSM.Functions.Controls;
using WindowsGSM.Functions.Localization;

namespace WindowsGSM.Functions.Palworld
{
    /// <summary>
    /// Live admin panel for a Palworld server (REST API + RCON): player table with kick/ban,
    /// in-game announce, manual save, graceful shutdown with countdown, and a free-form RCON
    /// console. Theme-aware and resizable.
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

        private readonly ObservableCollection<PalPlayer> _items = new ObservableCollection<PalPlayer>();
        private readonly DataGrid _grid;
        private readonly TextBlock _info;
        private readonly TextBlock _status;
        private readonly Wpf.Ui.Controls.TextBox _announce;
        private readonly Wpf.Ui.Controls.TextBox _shutdownMsg;
        private readonly Wpf.Ui.Controls.NumberBox _shutdownSecs;
        private readonly Wpf.Ui.Controls.TextBox _rconInput;
        private readonly TextBox _rconOutput;
        private string _version = "";

        public PalworldAdminDialog(string serverName, string host, int restPort, string adminPassword, int rconPort)
        {
            _api = new PalworldAdmin(host, restPort, adminPassword);
            _host = string.IsNullOrWhiteSpace(host) || host == "0.0.0.0" ? "127.0.0.1" : host;
            _rconPort = rconPort;
            _password = adminPassword;

            Title = Loc.T("PalAdmin.Title", serverName ?? "Palworld");
            Width = 780; Height = 780;
            MinWidth = 660; MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            Background = DialogTheme.Bg;
            NativeTheme.EnableDarkTitleBar(this);

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                          // header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });     // players (grows)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                          // server actions
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                          // console
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                          // status + close

            // ---- header ----
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(new TextBlock { Text = Loc.T("PalAdmin.Title", serverName ?? "Palworld"), Foreground = Accent, FontWeight = FontWeights.SemiBold, FontSize = 16 });
            _info = new TextBlock { Text = Loc.T("PalAdmin.Subtitle"), Foreground = Dim, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            header.Children.Add(_info);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ---- players card ----
            var playersCard = Card();
            Grid.SetRow(playersCard, 1);
            var pc = new Grid { Margin = new Thickness(12) };
            pc.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pc.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            pc.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var pcHead = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 8) };
            var pcTitle = new TextBlock { Text = Loc.T("PalAdmin.Players"), Foreground = Fg, FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(pcTitle, Dock.Left);
            pcHead.Children.Add(pcTitle);
            var refreshBtn = Btn(Loc.T("PalAdmin.Refresh"), Wpf.Ui.Controls.ControlAppearance.Secondary);
            refreshBtn.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowClockwise24 };
            refreshBtn.Click += async (s, e) => await RefreshPlayers();
            DockPanel.SetDock(refreshBtn, Dock.Right);
            pcHead.Children.Add(refreshBtn);
            Grid.SetRow(pcHead, 0);
            pc.Children.Add(pcHead);

            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Single,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                CanUserAddRows = false,
                CanUserResizeRows = false,
                RowHeight = 30,
                FontSize = 13,
                Background = DialogTheme.CardBg,
                Foreground = Fg,
                RowBackground = Brushes.Transparent,
                BorderBrush = DialogTheme.CardBorder,
                BorderThickness = new Thickness(1),
                ItemsSource = _items
            };
            _grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("PalAdmin.ColName"), Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("PalAdmin.ColLevel"), Binding = new Binding("Level"), Width = 70 });
            _grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("PalAdmin.ColPing"), Binding = new Binding("Ping"), Width = 80 });
            var idCol = new DataGridTextColumn { Header = "SteamID", Binding = new Binding("SteamId"), Width = 180 };
            var mono = new Style(typeof(TextBlock));
            mono.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Consolas")));
            idCol.ElementStyle = mono;
            _grid.Columns.Add(idCol);
            _grid.MouseDoubleClick += async (s, e) => await KickOrBan(false);
            Grid.SetRow(_grid, 1);
            pc.Children.Add(_grid);

            var pcBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var kickBtn = Btn(Loc.T("PalAdmin.Kick"), Wpf.Ui.Controls.ControlAppearance.Caution);
            kickBtn.Click += async (s, e) => await KickOrBan(false);
            var banBtn = Btn(Loc.T("PalAdmin.Ban"), Wpf.Ui.Controls.ControlAppearance.Danger);
            banBtn.Click += async (s, e) => await KickOrBan(true);
            pcBtns.Children.Add(kickBtn); pcBtns.Children.Add(banBtn);
            Grid.SetRow(pcBtns, 2);
            pc.Children.Add(pcBtns);

            playersCard.Child = pc;
            root.Children.Add(playersCard);

            // ---- server actions card ----
            var actionsCard = Card();
            Grid.SetRow(actionsCard, 2);
            var ac = new StackPanel { Margin = new Thickness(12) };

            ac.Children.Add(SectionTitle(Loc.T("PalAdmin.Broadcast")));
            var annRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) };
            var annBtn = Btn(Loc.T("PalAdmin.Announce"), Wpf.Ui.Controls.ControlAppearance.Primary);
            annBtn.Margin = new Thickness(6, 0, 0, 0);
            annBtn.Click += async (s, e) => await Announce();
            DockPanel.SetDock(annBtn, Dock.Right);
            annRow.Children.Add(annBtn);
            _announce = new Wpf.Ui.Controls.TextBox { PlaceholderText = Loc.T("PalAdmin.AnnouncePlaceholder") };
            annRow.Children.Add(_announce);
            ac.Children.Add(annRow);

            var opsRow = new StackPanel { Orientation = Orientation.Horizontal };
            var saveBtn = Btn(Loc.T("PalAdmin.Save"), Wpf.Ui.Controls.ControlAppearance.Secondary);
            saveBtn.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Save24 };
            saveBtn.Click += async (s, e) => await SaveWorld();
            opsRow.Children.Add(saveBtn);
            _shutdownSecs = new Wpf.Ui.Controls.NumberBox { Value = 30, Minimum = 1, Maximum = 3600, Width = 88, ClearButtonEnabled = false, Margin = new Thickness(16, 0, 6, 0) };
            opsRow.Children.Add(new TextBlock { Text = Loc.T("PalAdmin.ShutdownSeconds"), Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            opsRow.Children.Add(_shutdownSecs);
            _shutdownMsg = new Wpf.Ui.Controls.TextBox { PlaceholderText = Loc.T("PalAdmin.ShutdownMsgPlaceholder"), Width = 220, Margin = new Thickness(0, 0, 6, 0) };
            opsRow.Children.Add(_shutdownMsg);
            var sdBtn = Btn(Loc.T("PalAdmin.ShutdownBtn"), Wpf.Ui.Controls.ControlAppearance.Danger);
            sdBtn.Click += async (s, e) => await Shutdown();
            opsRow.Children.Add(sdBtn);
            ac.Children.Add(opsRow);

            actionsCard.Child = ac;
            root.Children.Add(actionsCard);

            // ---- RCON console card ----
            var consoleCard = Card();
            Grid.SetRow(consoleCard, 3);
            var cc = new StackPanel { Margin = new Thickness(12) };
            cc.Children.Add(SectionTitle(Loc.T("PalAdmin.Console")));
            cc.Children.Add(new TextBlock { Text = Loc.T("PalAdmin.ConsoleHint"), Foreground = Dim, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            var conRow = new DockPanel { LastChildFill = true };
            var sendBtn = Btn(Loc.T("PalAdmin.Send"), Wpf.Ui.Controls.ControlAppearance.Primary);
            sendBtn.Margin = new Thickness(6, 0, 0, 0);
            sendBtn.Click += async (s, e) => await SendRcon();
            DockPanel.SetDock(sendBtn, Dock.Right);
            conRow.Children.Add(sendBtn);
            _rconInput = new Wpf.Ui.Controls.TextBox { PlaceholderText = Loc.T("PalAdmin.ConsolePlaceholder"), FontFamily = new FontFamily("Consolas") };
            _rconInput.KeyDown += async (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) { await SendRcon(); } };
            conRow.Children.Add(_rconInput);
            cc.Children.Add(conRow);
            _rconOutput = new TextBox
            {
                IsReadOnly = true,
                Height = 104,
                Margin = new Thickness(0, 6, 0, 0),
                Background = DialogTheme.Bg,
                Foreground = Fg,
                BorderBrush = DialogTheme.CardBorder,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            cc.Children.Add(_rconOutput);
            consoleCard.Child = cc;
            root.Children.Add(consoleCard);

            // ---- status + close ----
            var bottom = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 10, 0, 0) };
            var closeBtn = Btn(Loc.T("Common.Close"), Wpf.Ui.Controls.ControlAppearance.Secondary);
            closeBtn.IsCancel = true;
            closeBtn.Margin = new Thickness(0);
            closeBtn.Click += (s, e) => Close();
            DockPanel.SetDock(closeBtn, Dock.Right);
            bottom.Children.Add(closeBtn);
            _status = new TextBlock { Foreground = Dim, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            bottom.Children.Add(_status);
            Grid.SetRow(bottom, 4);
            root.Children.Add(bottom);

            Content = root;
            Loaded += async (s, e) => { await LoadInfo(); await RefreshPlayers(); };
        }

        private static Border Card() => new Border
        {
            Background = DialogTheme.CardBg,
            BorderBrush = DialogTheme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 10)
        };

        private static TextBlock SectionTitle(string t) => new TextBlock { Text = t, Foreground = Fg, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };

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

        private async System.Threading.Tasks.Task LoadInfo()
        {
            var (ok, info, _) = await _api.GetInfoAsync();
            if (ok && info != null) { _version = info.Version; }
        }

        private async System.Threading.Tasks.Task RefreshPlayers()
        {
            SetStatus(Loc.T("PalAdmin.Loading"));
            var (ok, players, err) = await _api.GetPlayersAsync();
            _items.Clear();
            if (!ok)
            {
                _info.Text = Loc.T("PalAdmin.Subtitle");
                SetStatus(Loc.T("PalAdmin.Error", err ?? "?"), true);
                return;
            }
            foreach (var p in players) { _items.Add(p); }
            _info.Text = Loc.T("PalAdmin.InfoLine", string.IsNullOrEmpty(_version) ? "?" : _version, players.Count, $"{_host}");
            SetStatus(Loc.T("PalAdmin.PlayerCount", players.Count));
        }

        private PalPlayer Selected() => _grid.SelectedItem as PalPlayer;

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

        // ---- RCON free-form console ----

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
