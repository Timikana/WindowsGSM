using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowsGSM.Functions.Localization;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Plugin creation assistant: search a Steam game by name (or type an AppID), auto-resolve the
    /// executable/arguments from AppInfo, tweak the fields, then generate a ready-to-compile WindowsGSM
    /// plugin (.cs) into plugins\. Reuses <see cref="SteamApps"/> + <see cref="PluginGenerator"/>.
    /// </summary>
    public class PluginGeneratorDialog : Window
    {
        private static Brush Fg => WindowsGSM.Functions.Controls.DialogTheme.Fg;
        private static Brush Dim => WindowsGSM.Functions.Controls.DialogTheme.Dim;
        private static Brush Accent => WindowsGSM.Functions.Controls.DialogTheme.Accent;

        private readonly Action _onGenerated;
        private readonly TextBox _search = new TextBox { Padding = new Thickness(8, 4, 8, 4) };
        private readonly ListView _list = new ListView { Height = 130 };
        private readonly TextBox _appid = new TextBox { Width = 140, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _game = new TextBox { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _author = new TextBox { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _exe = new TextBox { FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _args = new TextBox { FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _port = new TextBox { Width = 90, Text = "27015", VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _qport = new TextBox { Width = 90, Text = "27015", VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _max = new TextBox { Width = 60, Text = "16", VerticalAlignment = VerticalAlignment.Center };
        private readonly CheckBox _a2s = new CheckBox { Content = "A2S", Foreground = Fg, IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        private readonly CheckBox _overwrite = new CheckBox { Foreground = Fg, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _status = new TextBlock { Foreground = Dim, TextWrapping = TextWrapping.Wrap, MinHeight = 18, Margin = new Thickness(0, 8, 0, 0) };
        private readonly Wpf.Ui.Controls.Button _generate;

        private class Row { public string AppId { get; set; } public string Name { get; set; } }

        public PluginGeneratorDialog(Action onGenerated)
        {
            _onGenerated = onGenerated;
            _overwrite.Content = Loc.T("PluginGen.Overwrite");

            Title = Loc.T("PluginGen.Title");
            Width = 660; Height = 680; MinWidth = 560; MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = WindowsGSM.Functions.Controls.DialogTheme.Bg;
            NativeTheme.EnableDarkTitleBar(this);

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(new TextBlock { Text = Loc.T("PluginGen.Title"), Foreground = Accent, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(new TextBlock { Text = Loc.T("PluginGen.Intro"), Foreground = Dim, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 10) });

            // Search bar
            var searchBar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var btnSearch = new Wpf.Ui.Controls.Button { Content = Loc.T("PluginGen.Search"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4) };
            btnSearch.Click += async (s, e) => await DoSearch();
            _search.KeyDown += async (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) { await DoSearch(); } };
            DockPanel.SetDock(btnSearch, Dock.Right);
            searchBar.Children.Add(btnSearch);
            searchBar.Children.Add(_search);
            root.Children.Add(searchBar);

            _list.SelectionChanged += async (s, e) =>
            {
                if (_list.SelectedItem is Row r) { _appid.Text = r.AppId; if (string.IsNullOrWhiteSpace(_game.Text)) { _game.Text = r.Name; } await Resolve(); }
            };
            var gv = new GridView();
            gv.Columns.Add(new GridViewColumn { Header = Loc.T("PluginGen.ColGame"), DisplayMemberBinding = new System.Windows.Data.Binding("Name"), Width = 470 });
            gv.Columns.Add(new GridViewColumn { Header = "AppID", DisplayMemberBinding = new System.Windows.Data.Binding("AppId"), Width = 110 });
            _list.View = gv;
            root.Children.Add(_list);

            // AppID + Resolve
            var appidRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 6) };
            appidRow.Children.Add(Label("PluginGen.AppId"));
            appidRow.Children.Add(_appid);
            var btnResolve = new Wpf.Ui.Controls.Button { Content = Loc.T("PluginGen.Resolve"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
            btnResolve.Click += async (s, e) => await Resolve();
            appidRow.Children.Add(btnResolve);
            root.Children.Add(appidRow);

            root.Children.Add(FieldRow("PluginGen.GameName", _game));
            root.Children.Add(FieldRow("PluginGen.Author", _author));
            root.Children.Add(FieldRow("PluginGen.Executable", _exe));
            root.Children.Add(FieldRow("PluginGen.Arguments", _args));

            // ports + max + a2s
            var portsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            portsRow.Children.Add(Label("PluginGen.Port"));
            portsRow.Children.Add(_port);
            portsRow.Children.Add(new TextBlock { Text = Loc.T("PluginGen.QueryPort"), Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) });
            portsRow.Children.Add(_qport);
            portsRow.Children.Add(new TextBlock { Text = Loc.T("PluginGen.MaxPlayers"), Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) });
            portsRow.Children.Add(_max);
            portsRow.Children.Add(_a2s);
            root.Children.Add(portsRow);

            _overwrite.Margin = new Thickness(0, 0, 0, 4);
            root.Children.Add(_overwrite);

            root.Children.Add(_status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            _generate = new Wpf.Ui.Controls.Button { Content = Loc.T("PluginGen.Generate"), Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 6, 0) };
            var close = new Wpf.Ui.Controls.Button { Content = Loc.T("Common.Close"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5) };
            _generate.Click += (s, e) => DoGenerate();
            close.Click += (s, e) => Close();
            buttons.Children.Add(_generate); buttons.Children.Add(close);
            root.Children.Add(buttons);

            Content = root;
        }

        private static TextBlock Label(string key) =>
            new TextBlock { Text = Loc.T(key), Foreground = Fg, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };

        private static FrameworkElement FieldRow(string key, TextBox box)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var lbl = Label(key);
            DockPanel.SetDock(lbl, Dock.Left);
            row.Children.Add(lbl);
            row.Children.Add(box); // fills
            return row;
        }

        private async System.Threading.Tasks.Task DoSearch()
        {
            _status.Foreground = Dim; _status.Text = Loc.T("PluginGen.Searching");
            try
            {
                List<SteamApps.AppEntry> res = await SteamApps.SearchAsync(_search.Text, 100);
                var rows = new List<Row>();
                foreach (var a in res) { rows.Add(new Row { AppId = a.AppId, Name = a.Name }); }
                _list.ItemsSource = rows;
                _status.Text = rows.Count == 0 ? Loc.T("PluginGen.NoResults") : Loc.T("PluginGen.ResultCount", rows.Count);
            }
            catch (Exception ex) { _status.Foreground = Brushes.OrangeRed; _status.Text = ex.Message; }
        }

        private async System.Threading.Tasks.Task Resolve()
        {
            string appid = (_appid.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(appid) || !ulong.TryParse(appid, out _)) { _status.Foreground = Brushes.OrangeRed; _status.Text = Loc.T("PluginGen.NeedAppId"); return; }
            _status.Foreground = Dim; _status.Text = Loc.T("PluginGen.Resolving");
            try
            {
                SteamApps.LaunchProfile prof = await SteamApps.ResolveLaunchAsync(appid);
                if (!string.IsNullOrEmpty(prof.Name) && string.IsNullOrWhiteSpace(_game.Text)) { _game.Text = prof.Name; }
                if (!prof.Found || string.IsNullOrEmpty(prof.Executable))
                {
                    _status.Foreground = Brushes.OrangeRed;
                    _status.Text = Loc.T("PluginGen.ResolveFailed", appid);
                    return;
                }
                _exe.Text = prof.Executable;
                _args.Text = prof.Arguments ?? string.Empty;
                _status.Foreground = Accent;
                _status.Text = Loc.T("PluginGen.Resolved");
            }
            catch (Exception ex) { _status.Foreground = Brushes.OrangeRed; _status.Text = ex.Message; }
        }

        private void DoGenerate()
        {
            var (ok, msg) = PluginGenerator.Generate(
                _game.Text, _author.Text, (_appid.Text ?? string.Empty).Trim(), _exe.Text, _args.Text,
                _port.Text, _qport.Text, _max.Text, _a2s.IsChecked == true, _overwrite.IsChecked == true);

            if (ok)
            {
                _status.Foreground = Accent;
                _status.Text = Loc.T("PluginGen.Done", System.IO.Path.GetFileName(msg));
                try { _onGenerated?.Invoke(); } catch { }
            }
            else
            {
                _status.Foreground = Brushes.OrangeRed;
                _status.Text = msg;
            }
        }
    }
}
