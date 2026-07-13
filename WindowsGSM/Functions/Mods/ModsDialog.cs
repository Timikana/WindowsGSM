using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WindowsGSM.Functions.Localization;

namespace WindowsGSM.Functions.Mods
{
    /// <summary>
    /// UNIFIED mod manager (all games): detects the game's mechanism (file mods folder,
    /// subfolders, Steam Workshop, or none) and shows the matching UI. Fluent style, dark title bar.
    /// </summary>
    public class ModsDialog : Window
    {
        private readonly string _serverId;
        private readonly string _serverFiles;
        private readonly string _game;
        private readonly ModProfile _profile;
        private Grid _host;          // fills the middle; holds either a scroll (folder view) or the workshop tabs
        private StackPanel _body;    // folder/none/fallback view container (re-created per BuildBody)
        private readonly TextBlock _status;
        private bool _closed; // stop a multi-item download loop from writing into a closed dialog
        private string _filter = string.Empty; // mods list search filter (persists across rebuilds)

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            base.OnClosed(e);
        }
        private WorkshopConfig _ws;

        private static Brush Fg => WindowsGSM.Functions.Controls.DialogTheme.Fg;
        private static Brush Dim => WindowsGSM.Functions.Controls.DialogTheme.Dim;
        private static Brush Accent => WindowsGSM.Functions.Controls.DialogTheme.Accent;
        private static Brush Warn => WindowsGSM.Functions.Controls.DialogTheme.Warn;

        public ModsDialog(string serverId, string serverName, string game, string serverFiles)
        {
            _serverId = serverId;
            _serverFiles = serverFiles;
            _game = game;
            _profile = ModProfiles.For(game);

            Title = Loc.T("Mods.Title", serverId, serverName);
            Width = 720;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = WindowsGSM.Functions.Controls.DialogTheme.Bg;
            NativeTheme.EnableDarkTitleBar(this);

            var outer = new DockPanel { Margin = new Thickness(14) };

            // Header
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(new TextBlock { Text = MechanismLabel(), Foreground = Accent, FontWeight = FontWeights.SemiBold, FontSize = 15 });
            if (!string.IsNullOrEmpty(_profile?.Notes))
            {
                header.Children.Add(new TextBlock { Text = _profile.Notes, Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            }
            DockPanel.SetDock(header, Dock.Top);
            outer.Children.Add(header);

            // Bottom: status + close
            var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            _status = new TextBlock { Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(_status, Dock.Left);
            bottom.Children.Add(_status);
            var close = new Wpf.Ui.Controls.Button { Content = Loc.T("Common.Close"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5), HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (s, e) => Close();
            DockPanel.SetDock(close, Dock.Right);
            bottom.Children.Add(close);
            DockPanel.SetDock(bottom, Dock.Bottom);
            outer.Children.Add(bottom);

            _host = new Grid(); // BuildBody fills it (scroll for folder view, tabs for workshop)
            outer.Children.Add(_host);

            Content = outer;
            BuildBody();
        }

        private string MechanismLabel()
        {
            if (_profile == null) { return Loc.T("Mods.UnrecognizedGame"); }
            switch (_profile.Mechanism)
            {
                case ModMechanism.Folder: return Loc.T("Mods.HeaderFileMods", _game);
                case ModMechanism.Workshop: return Loc.T("Mods.HeaderWorkshop", _game);
                default: return Loc.T("Mods.HeaderGeneric", _game);
            }
        }

        private void BuildBody()
        {
            _host.Children.Clear();

            // Steam Workshop games get a two-tab UI (Browse / Activate).
            if (_profile != null && _profile.Mechanism == ModMechanism.Workshop)
            {
                BuildWorkshopTabs();
                return;
            }

            // Everything else: a single scrollable body.
            _body = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            _host.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body });

            if (_profile == null)
            {
                _body.Children.Add(Info(Loc.T("Mods.NoProfile")));
                _body.Children.Add(OpenButton(Loc.T("Mods.OpenServerFolder"), _serverFiles));
                return;
            }
            if (_profile.Mechanism == ModMechanism.None)
            {
                _body.Children.Add(Info(Loc.T("Mods.NoModSystem")));
                return;
            }
            BuildFolderView();
        }

        // ---- File mods view ----
        private void BuildFolderView()
        {
            string modDir = ModFolder.ModDirPath(_serverFiles, _profile);

            // Action bar
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var addBtn = new Wpf.Ui.Controls.Button { Content = Loc.T("Mods.AddMod"), Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 6, 0) };
            addBtn.Click += (s, e) => AddMod();
            var openBtn = new Wpf.Ui.Controls.Button { Content = Loc.T("Mods.OpenFolder"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 6, 0) };
            openBtn.Click += (s, e) => { try { Directory.CreateDirectory(modDir); WindowsGSM.Shell.Open(modDir); } catch (Exception ex) { Fail(ex.Message); } };
            var refreshBtn = new Wpf.Ui.Controls.Button { Content = Loc.T("Mods.Refresh"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(14, 5, 14, 5) };
            refreshBtn.Click += (s, e) => BuildBody();
            actions.Children.Add(addBtn);
            actions.Children.Add(openBtn);
            actions.Children.Add(refreshBtn);
            _body.Children.Add(actions);

            _body.Children.Add(new TextBlock { Text = modDir, Foreground = Dim, FontSize = 11, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });

            var mods = ModFolder.List(_serverFiles, _profile);
            if (mods.Count == 0)
            {
                _body.Children.Add(Info(Loc.T("Mods.NoModsYet")));
                return;
            }

            var count = new TextBlock { Foreground = Dim, FontSize = 11, Margin = new Thickness(2, 0, 0, 6) };
            var host = new StackPanel();
            var search = SearchBox(host, filter =>
            {
                var list = string.IsNullOrWhiteSpace(filter) ? mods
                    : mods.Where(m => (m.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                count.Text = Loc.T("Mods.Count", list.Count, mods.Count);
                if (list.Count == 0) { host.Children.Add(Info(Loc.T("Mods.NoMatch"))); return; }
                foreach (var m in list) { host.Children.Add(FolderCard(m)); }
            });
            _body.Children.Add(search);
            _body.Children.Add(count);
            _body.Children.Add(host);
        }

        private Border FolderCard(ModFolder.ModItem m)
        {
            var captured = m;
            var card = new Border
            {
                BorderBrush = WindowsGSM.Functions.Controls.DialogTheme.CardBorder,
                Background = WindowsGSM.Functions.Controls.DialogTheme.CardBg,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10)
            };
            var dp = new DockPanel();

            var toggle = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = m.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            toggle.Checked += (s, e) => DoToggle(captured);
            toggle.Unchecked += (s, e) => DoToggle(captured);
            DockPanel.SetDock(toggle, Dock.Left);
            dp.Children.Add(toggle);

            var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            meta.Children.Add(new TextBlock { Text = m.Name, Foreground = m.Enabled ? Fg : Dim, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            string sub = (m.IsDirectory ? Loc.T("Mods.TypeFolder") : SizeStr(m.SizeBytes)) + (m.Enabled ? "" : " · " + Loc.T("Mods.Disabled"));
            meta.Children.Add(new TextBlock { Text = sub, Foreground = Dim, FontSize = 11 });
            dp.Children.Add(meta);

            card.Child = dp;
            return card;
        }

        /// <summary>A reusable filter box: repopulates <paramref name="host"/> via <paramref name="render"/>
        /// on every keystroke. The filter text persists across rebuilds (BuildBody) via _filter.</summary>
        private Wpf.Ui.Controls.TextBox SearchBox(StackPanel host, Action<string> render)
        {
            var box = new Wpf.Ui.Controls.TextBox
            {
                PlaceholderText = Loc.T("Mods.SearchPlaceholder"),
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Search24 },
                Margin = new Thickness(0, 0, 0, 6),
                Text = _filter
            };
            void Run() { host.Children.Clear(); render(_filter ?? string.Empty); }
            box.TextChanged += (s, e) => { _filter = box.Text ?? string.Empty; Run(); };
            Run(); // initial population
            return box;
        }

        private void DoToggle(ModFolder.ModItem item)
        {
            try
            {
                ModFolder.Toggle(_serverFiles, _profile, item);
                _status.Foreground = Accent;
                _status.Text = item.Enabled ? Loc.T("Mods.EnabledMsg", item.Name) : Loc.T("Mods.DisabledMsg", item.Name);
                BuildBody();
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        private void AddMod()
        {
            try
            {
                string filter = _profile.Extensions != null && _profile.Extensions.Length > 0
                    ? Loc.T("Mods.FilterMods") + "|" + string.Join(";", Array.ConvertAll(_profile.Extensions, x => "*" + x)) + "|" + Loc.T("Mods.FilterAllFiles") + "|*.*"
                    : Loc.T("Mods.FilterAllFiles") + "|*.*";
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("Mods.ChooseMods"), Multiselect = true, Filter = filter };
                if (dlg.ShowDialog(this) == true)
                {
                    foreach (string f in dlg.FileNames) { ModFolder.AddFile(_serverFiles, _profile, f); }
                    _status.Foreground = Accent;
                    _status.Text = Loc.T("Mods.ModsAdded", dlg.FileNames.Length);
                    BuildBody();
                }
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        // ---- Steam Workshop view: two tabs (Browse / Activate) ----
        private void BuildWorkshopTabs()
        {
            if (_ws == null) { _ws = WorkshopConfig.Load(_serverId); }
            var tabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            var browse = new StackPanel { Margin = new Thickness(0, 10, 14, 0) };
            var activate = new StackPanel { Margin = new Thickness(0, 10, 14, 0) };
            var tActivate = new TabItem { Header = Loc.T("Mods.TabActivate"), Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = activate } };
            tabs.Items.Add(new TabItem { Header = Loc.T("Mods.TabBrowse"), Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = browse } });
            tabs.Items.Add(tActivate);
            // Rebuild the Activate tab each time it's shown so mods added in the Browse tab appear.
            tabs.SelectionChanged += (s, e) => { if (ReferenceEquals(tabs.SelectedItem, tActivate)) { activate.Children.Clear(); BuildActivateTab(activate); } };
            _host.Children.Add(tabs);
            BuildBrowseTab(browse);
            BuildActivateTab(activate);
        }

        // Tab 1: discover & add mods from the Steam Workshop catalogue.
        private void BuildBrowseTab(StackPanel body)
        {
            var browseRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
            var searchBtn = new Wpf.Ui.Controls.Button { Content = Loc.T("Mods.BrowseSearchBtn"), Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(searchBtn, Dock.Right);
            browseRow.Children.Add(searchBtn);
            var qBox = new Wpf.Ui.Controls.TextBox { PlaceholderText = Loc.T("Mods.BrowsePlaceholder"), Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Search24 } };
            browseRow.Children.Add(qBox);
            body.Children.Add(browseRow);

            var browseHost = new StackPanel();
            body.Children.Add(browseHost);

            async System.Threading.Tasks.Task RunBrowse()
            {
                browseHost.Children.Clear();
                browseHost.Children.Add(Info(Loc.T("Mods.BrowseLoading")));
                var (ok, items, err) = await WorkshopBrowser.SearchAsync(_profile.WorkshopAppId.ToString(), qBox.Text);
                if (_closed) { return; }
                browseHost.Children.Clear();
                if (!ok) { browseHost.Children.Add(Info(Loc.T("Mods.ErrorPrefix", err ?? "?"))); return; }
                if (items.Count == 0) { browseHost.Children.Add(Info(Loc.T("Mods.BrowseNoResult"))); return; }
                foreach (var it in items) { browseHost.Children.Add(BrowseCard(it)); }
            }
            searchBtn.Click += async (s, e) => await RunBrowse();
            qBox.KeyDown += async (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) { await RunBrowse(); } };
            _ = RunBrowse();

            body.Children.Add(new TextBlock { Text = Loc.T("Mods.AddLabel"), Foreground = Dim, FontSize = 11, Margin = new Thickness(0, 12, 0, 4) });
            var add = new StackPanel { Orientation = Orientation.Horizontal };
            var idBox = new Wpf.Ui.Controls.TextBox { MinWidth = 190, VerticalAlignment = VerticalAlignment.Center, PlaceholderText = Loc.T("Mods.WorkshopIdHint") };
            var nameBox = new Wpf.Ui.Controls.TextBox { MinWidth = 170, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, PlaceholderText = Loc.T("Mods.NameOptional") };
            var addBtn = new Wpf.Ui.Controls.Button { Content = Loc.T("Mods.AddBtn"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(14, 5, 14, 5), ToolTip = Loc.T("Common.AddEntry") };
            addBtn.Click += (s, e) =>
            {
                string id = new string((idBox.Text ?? "").Trim().Where(char.IsDigit).ToArray());
                if (id.Length == 0) { Fail(Loc.T("Mods.InvalidWorkshopId")); return; }
                if (!_ws.Items.Any(x => x.Id == id)) { _ws.Items.Add(new WorkshopEntry { Id = id, Name = (nameBox.Text ?? "").Trim(), Enabled = true }); _ws.Save(); }
                idBox.Text = string.Empty; nameBox.Text = string.Empty;
                _status.Foreground = Accent; _status.Text = Loc.T("Mods.BrowseAddedMsg", id);
            };
            add.Children.Add(idBox); add.Children.Add(nameBox); add.Children.Add(addBtn);
            body.Children.Add(add);
        }

        // Tab 2: enable/disable your mods, download them, and apply to the server.
        private void BuildActivateTab(StackPanel body)
        {
            body.Children.Add(new TextBlock { Text = Loc.T("Mods.ActivateNote"), Foreground = Dim, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

            if (!_profile.ServerAutoDownloads)
            {
                var acct = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                acct.Children.Add(new TextBlock { Text = Loc.T("Mods.SteamAccount"), Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                var acctBox = new Wpf.Ui.Controls.TextBox { MinWidth = 150, Text = WorkshopManager.GetSteamAccount(), PlaceholderText = Loc.T("Mods.SteamAccountHint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                acct.Children.Add(acctBox);
                var loginBtn = new Wpf.Ui.Controls.Button { Content = Loc.T("Mods.SteamLogin"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(14, 5, 14, 5) };
                loginBtn.Click += (s, e) =>
                {
                    string u = (acctBox.Text ?? string.Empty).Trim();
                    if (u.Length == 0) { Fail(Loc.T("Mods.SteamAccountHint")); return; }
                    WorkshopManager.SetSteamAccount(u);
                    WorkshopManager.LaunchInteractiveLogin(u);
                    _status.Foreground = Accent; _status.Text = Loc.T("Mods.SteamLoginOpened");
                };
                acct.Children.Add(loginBtn);
                body.Children.Add(acct);
                body.Children.Add(new TextBlock { Text = Loc.T("Mods.SteamAccountNote"), Foreground = Dim, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            }

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            if (!_profile.ServerAutoDownloads)
            {
                var dl = new Wpf.Ui.Controls.Button { Content = Loc.T("Mods.DownloadSteamCmd"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 6, 0) };
                dl.Click += async (s, e) => await DownloadAll(dl);
                actions.Children.Add(dl);
            }
            var applyBtn = new Wpf.Ui.Controls.Button { Content = Loc.T("Mods.ApplyActivate"), Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(14, 5, 14, 5) };
            applyBtn.Click += (s, e) => ApplyConfigNow();
            actions.Children.Add(applyBtn);
            body.Children.Add(actions);

            if (_ws.Items.Count == 0)
            {
                body.Children.Add(Info(Loc.T("Mods.NoWorkshopMods")));
                return;
            }

            var count = new TextBlock { Foreground = Dim, FontSize = 11, Margin = new Thickness(2, 0, 0, 6) };
            var host = new StackPanel();
            var search = SearchBox(host, filter =>
            {
                var list = string.IsNullOrWhiteSpace(filter) ? _ws.Items.ToList()
                    : _ws.Items.Where(x => (x.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                                        || (x.Id ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                count.Text = Loc.T("Mods.Count", list.Count, _ws.Items.Count);
                if (list.Count == 0) { host.Children.Add(Info(Loc.T("Mods.NoMatch"))); return; }
                foreach (var entry in list) { host.Children.Add(WorkshopCard(entry)); }
            });
            body.Children.Add(search);
            body.Children.Add(count);
            body.Children.Add(host);
        }

        // Writes the enabled mods into the game (Palworld: copy + PalModSettings.ini; ARK/PZ: config key).
        private void ApplyConfigNow()
        {
            try
            {
                string msg;
                if (string.Equals(_profile.GameMatch, "Palworld", StringComparison.OrdinalIgnoreCase))
                {
                    msg = WorkshopManager.ApplyPalworld(_serverFiles, _ws.Items);
                }
                else if (!string.IsNullOrEmpty(_profile.ConfigKey) && !string.IsNullOrEmpty(_profile.ConfigFileRelative))
                {
                    msg = WorkshopManager.ApplyToConfig(_serverFiles, _profile, _ws.Items);
                }
                else
                {
                    msg = Loc.T("Mods.ActivateServerAuto");
                }
                _status.Foreground = Accent; _status.Text = msg;
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        // A browse result from the Steam Workshop catalogue: thumbnail + title + "Add" (adds to the tracked list).
        private Border BrowseCard(WorkshopBrowserItem it)
        {
            var card = new Border
            {
                BorderBrush = WindowsGSM.Functions.Controls.DialogTheme.CardBorder,
                Background = WindowsGSM.Functions.Controls.DialogTheme.CardBg,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(8)
            };
            var dp = new DockPanel();

            try
            {
                var img = new Image { Width = 44, Height = 44, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Stretch = Stretch.UniformToFill };
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(it.PreviewUrl);
                bmp.DecodePixelWidth = 44;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.EndInit();
                img.Source = bmp;
                DockPanel.SetDock(img, Dock.Left);
                dp.Children.Add(img);
            }
            catch { }

            bool already = _ws.Items.Any(x => x.Id == it.Id);
            var addB = new Wpf.Ui.Controls.Button
            {
                Content = already ? Loc.T("Mods.BrowseAdded") : Loc.T("Mods.AddBtn"),
                Appearance = already ? Wpf.Ui.Controls.ControlAppearance.Secondary : Wpf.Ui.Controls.ControlAppearance.Primary,
                IsEnabled = !already,
                Padding = new Thickness(14, 5, 14, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
            addB.Click += (s, e) =>
            {
                if (_ws.Items.Any(x => x.Id == it.Id)) { return; }
                _ws.Items.Add(new WorkshopEntry { Id = it.Id, Name = it.Title, Enabled = true });
                _ws.Save();
                addB.Content = Loc.T("Mods.BrowseAdded");
                addB.IsEnabled = false;
                addB.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
                _status.Foreground = Accent;
                _status.Text = Loc.T("Mods.BrowseAddedMsg", it.Title);
            };
            DockPanel.SetDock(addB, Dock.Right);
            dp.Children.Add(addB);

            var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            meta.Children.Add(new TextBlock { Text = it.Title, Foreground = Fg, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            meta.Children.Add(new TextBlock { Text = "ID " + it.Id, Foreground = Dim, FontSize = 11 });
            dp.Children.Add(meta);

            card.Child = dp;
            return card;
        }

        private Border WorkshopCard(WorkshopEntry entry)
        {
            var captured = entry;
            var card = new Border
            {
                BorderBrush = WindowsGSM.Functions.Controls.DialogTheme.CardBorder,
                Background = WindowsGSM.Functions.Controls.DialogTheme.CardBg,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10)
            };
            var dp = new DockPanel();

            var toggle = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = entry.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            toggle.Checked += (s, e) => { captured.Enabled = true; _ws.Save(); ApplyConfigNow(); };
            toggle.Unchecked += (s, e) => { captured.Enabled = false; _ws.Save(); ApplyConfigNow(); };
            DockPanel.SetDock(toggle, Dock.Left);
            dp.Children.Add(toggle);

            var del = new Wpf.Ui.Controls.Button { Content = "✕", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Width = 32, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.T("Mods.RemoveFromList") };
            del.Click += (s, e) =>
            {
                if (System.Windows.MessageBox.Show(Loc.T("Common.ConfirmRemove"), Loc.T("Common.ConfirmTitle"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes) { return; }
                _ws.Items.Remove(captured); _ws.Save(); BuildBody();
            };
            DockPanel.SetDock(del, Dock.Right);
            dp.Children.Add(del);

            var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            meta.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(entry.Name) ? entry.Id : $"{entry.Name}", Foreground = Fg, FontWeight = FontWeights.SemiBold });
            meta.Children.Add(new TextBlock { Text = "ID " + entry.Id + (entry.Enabled ? "" : " · " + Loc.T("Mods.Disabled")), Foreground = Dim, FontSize = 11 });
            dp.Children.Add(meta);

            card.Child = dp;
            return card;
        }

        private async System.Threading.Tasks.Task DownloadAll(Wpf.Ui.Controls.Button btn)
        {
            btn.IsEnabled = false;
            try
            {
                int ok = 0, fail = 0; string lastErr = null;
                foreach (var e in _ws.Items.Where(x => x.Enabled).ToList())
                {
                    if (_closed) { return; } // dialog closed → stop launching further downloads
                    _status.Foreground = Dim;
                    _status.Text = Loc.T("Mods.Downloading", e.Id);
                    var (success, msg) = await WorkshopManager.DownloadAsync(_profile.WorkshopAppId, e.Id);
                    if (success) { ok++; } else { fail++; lastErr = msg; }
                }
                if (_closed) { return; }
                _status.Foreground = fail == 0 ? Accent : Warn;
                _status.Text = Loc.T("Mods.DownloadFinished", ok, fail, _profile.ConfigKey)
                    + (fail > 0 && !string.IsNullOrEmpty(lastErr) ? " — " + lastErr : "");
                // Auto-activate what was just downloaded (Palworld: PalModSettings.ini; ARK/PZ: config key).
                if (ok > 0) { ApplyConfigNow(); }
            }
            catch (Exception ex) { Fail(ex.Message); }
            finally { btn.IsEnabled = true; }
        }

        private static void ApplyPlaceholder(TextBox box, string hint)
        {
            // PlaceholderText via Wpf.Ui if available; otherwise left empty (cosmetic).
            box.ToolTip = hint;
        }

        // ---- UI helpers ----
        private void Fail(string msg)
        {
            _status.Foreground = Warn;
            _status.Text = Loc.T("Mods.ErrorPrefix", msg);
            Functions.AppLog.Warn("Mods/UI", msg);
        }

        private TextBlock Info(string text) => new TextBlock { Text = text, Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };

        private Wpf.Ui.Controls.Button OpenButton(string label, string path)
        {
            var b = new Wpf.Ui.Controls.Button { Content = label, Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(14, 5, 14, 5), HorizontalAlignment = HorizontalAlignment.Left };
            b.Click += (s, e) => { try { if (!string.IsNullOrEmpty(path)) { WindowsGSM.Shell.Open(path); } } catch (Exception ex) { Fail(ex.Message); } };
            return b;
        }

        private static string SizeStr(long bytes)
        {
            if (bytes >= 1024 * 1024) { return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB"; }
            if (bytes >= 1024) { return (bytes / 1024.0).ToString("0") + " KB"; }
            return bytes + " B";
        }
    }
}
