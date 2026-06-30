using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Adding a Steam dedicated server by AppID: search by name (apps.json) OR direct AppID entry,
    /// then auto-resolution of the executable/arguments (AppInfo). Returns a <see cref="SteamApps.LaunchProfile"/>.
    /// </summary>
    public class GenericSteamDialog : Window
    {
        public SteamApps.LaunchProfile? Result { get; private set; }

        private readonly TextBox _search = new TextBox { Padding = new Thickness(8, 4, 8, 4) };
        private readonly TextBox _appid = new TextBox { Padding = new Thickness(8, 4, 8, 4), FontFamily = new FontFamily("Consolas") };
        private readonly ListView _list = new ListView();
        private readonly TextBlock _status = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0x9a, 0x9a, 0x9a)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        private readonly Wpf.Ui.Controls.Button _install;

        private class Row { public string AppId { get; set; } public string Name { get; set; } }

        public GenericSteamDialog()
        {
            Title = "Add a Steam server (AppID)";
            Width = 620; Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x1f, 0x1f, 0x1f));
            NativeTheme.EnableDarkTitleBar(this);

            var outer = new DockPanel { Margin = new Thickness(14) };

            var intro = new TextBlock
            {
                Text = "Search for a game by name, or directly enter a Steam AppID. WGSM resolves the executable and arguments automatically, installs via SteamCMD, then adds the server.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x9a, 0x9a, 0x9a)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            };
            DockPanel.SetDock(intro, Dock.Top); outer.Children.Add(intro);

            // Search bar
            var searchBar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(searchBar, Dock.Top);
            var btnSearch = new Wpf.Ui.Controls.Button { Content = "Search", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4) };
            btnSearch.Click += async (s, e) => await DoSearch();
            _search.KeyDown += async (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) { await DoSearch(); } };
            DockPanel.SetDock(btnSearch, Dock.Right);
            searchBar.Children.Add(btnSearch);
            searchBar.Children.Add(_search);
            outer.Children.Add(searchBar);

            // AppID row + bottom buttons
            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(bottom, Dock.Bottom);
            _install = new Wpf.Ui.Controls.Button { Content = "Install", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            var cancel = new Wpf.Ui.Controls.Button { Content = "Cancel", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            _install.Click += async (s, e) => await DoInstall();
            cancel.Click += (s, e) => Close();
            bottom.Children.Add(_install); bottom.Children.Add(cancel);
            outer.Children.Add(bottom);

            var appidRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(appidRow, Dock.Bottom);
            var lbl = new TextBlock { Text = "AppID:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(lbl, Dock.Left);
            appidRow.Children.Add(lbl);
            appidRow.Children.Add(_appid);
            outer.Children.Add(appidRow);

            DockPanel.SetDock(_status, Dock.Bottom); outer.Children.Add(_status);

            // Results list
            _list.SelectionChanged += (s, e) =>
            {
                if (_list.SelectedItem is Row r) { _appid.Text = r.AppId; }
            };
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = "Game", DisplayMemberBinding = new System.Windows.Data.Binding("Name"), Width = 430 });
            gv.Columns.Add(new GridViewColumn { Header = "AppID", DisplayMemberBinding = new System.Windows.Data.Binding("AppId"), Width = 110 });
            _list.View = gv;
            outer.Children.Add(_list); // fills the center

            Content = outer;
        }

        private async System.Threading.Tasks.Task DoSearch()
        {
            _status.Text = "Searching…";
            try
            {
                List<SteamApps.AppEntry> res = await SteamApps.SearchAsync(_search.Text, 100);
                var rows = new List<Row>();
                foreach (var a in res) { rows.Add(new Row { AppId = a.AppId, Name = a.Name }); }
                _list.ItemsSource = rows;
                _status.Text = rows.Count == 0 ? "No results (check the name, or enter the AppID directly)." : $"{rows.Count} result(s). Select a row, then Install.";
            }
            catch (Exception ex) { _status.Text = "Search error: " + ex.Message; }
        }

        private async System.Threading.Tasks.Task DoInstall()
        {
            string appid = (_appid.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(appid) || !ulong.TryParse(appid, out _))
            {
                _status.Text = "Enter a numeric AppID (or select a game from the list).";
                return;
            }

            _install.IsEnabled = false;
            _status.Text = "Resolving the executable (AppInfo)…";
            try
            {
                SteamApps.LaunchProfile prof = await SteamApps.ResolveLaunchAsync(appid);
                if (!prof.Found || string.IsNullOrEmpty(prof.Executable))
                {
                    _status.Text = $"Unable to resolve the Windows executable for AppID {appid} (game missing from SteamAppInfo or no Windows launch).";
                    _install.IsEnabled = true;
                    return;
                }
                Result = prof;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                _status.Text = "Resolution error: " + ex.Message;
                _install.IsEnabled = true;
            }
        }
    }
}
