using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowsGSM.Functions.Localization;

namespace WindowsGSM.Functions.WebApi
{
    /// <summary>Web portal account management (login, role, allowed servers). PBKDF2-hashed passwords.</summary>
    public class WebUsersDialog : Window
    {
        private static Brush Fg => WindowsGSM.Functions.Controls.DialogTheme.Fg;
        private static Brush Dim => WindowsGSM.Functions.Controls.DialogTheme.Dim;
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x4c, 0xc2, 0xd6));

        private readonly WebUsers _store;
        private readonly List<(string Id, string Name)> _servers;
        private readonly ListBox _list;
        private readonly TextBox _user;
        private readonly WindowsGSM.Functions.Controls.RevealPasswordBox _pass;
        private readonly ComboBox _role;
        private readonly CheckBox _allServers;
        private readonly StackPanel _serverPanel;
        private readonly List<CheckBox> _serverChecks = new List<CheckBox>();
        private readonly TextBlock _status;

        public WebUsersDialog(IEnumerable<(string Id, string Name)> servers)
        {
            _store = WebUsers.Load();
            _servers = (servers ?? Enumerable.Empty<(string, string)>()).ToList();

            Title = Loc.T("WebUsers.Title");
            Width = 560; Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = WindowsGSM.Functions.Controls.DialogTheme.Bg;
            NativeTheme.EnableDarkTitleBar(this);

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(new TextBlock { Text = Loc.T("WebUsers.Title"), Foreground = Accent, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(new TextBlock { Text = Loc.T("WebUsers.RolesHelp"), Foreground = Dim, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 10) });

            _list = new ListBox { Height = 150, Background = WindowsGSM.Functions.Controls.DialogTheme.CardBg, Foreground = Fg, BorderBrush = WindowsGSM.Functions.Controls.DialogTheme.CardBorder };
            _list.SelectionChanged += (s, e) =>
            {
                if (_list.SelectedItem is string line)
                {
                    var u = _store.Users.FirstOrDefault(x => line.StartsWith(x.Username + "  "));
                    if (u != null) { _user.Text = u.Username; _role.SelectedIndex = (int)u.Role; _pass.Password = string.Empty; ApplySelection(u.ServerIds); }
                }
            };
            root.Children.Add(_list);

            var f1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 6) };
            f1.Children.Add(new TextBlock { Text = Loc.T("WebUsers.Username"), Foreground = Fg, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            _user = new TextBox { Width = 160 };
            f1.Children.Add(_user);
            f1.Children.Add(new TextBlock { Text = Loc.T("WebUsers.Password"), Foreground = Fg, MinWidth = 80, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            _pass = new WindowsGSM.Functions.Controls.RevealPasswordBox { Width = 190 };
            f1.Children.Add(_pass);
            root.Children.Add(f1);

            var f2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            f2.Children.Add(new TextBlock { Text = Loc.T("WebUsers.Role"), Foreground = Fg, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            _role = new ComboBox { Width = 160 };
            _role.Items.Add("Viewer"); _role.Items.Add("Operator"); _role.Items.Add("Admin"); _role.SelectedIndex = 0;
            f2.Children.Add(_role);
            root.Children.Add(f2);

            // ---- Server allowlist (checkboxes) ----
            root.Children.Add(new TextBlock { Text = Loc.T("WebUsers.AllowedServers"), Foreground = Fg, Margin = new Thickness(0, 4, 0, 2) });
            _allServers = new CheckBox { Content = Loc.T("WebUsers.AllServers"), Foreground = Fg, IsChecked = true, Margin = new Thickness(0, 0, 0, 4) };
            _allServers.Checked += (s, e) => SetServerPanelEnabled(false);
            _allServers.Unchecked += (s, e) => SetServerPanelEnabled(true);
            root.Children.Add(_allServers);

            _serverPanel = new StackPanel();
            foreach (var srv in _servers)
            {
                var cb = new CheckBox { Content = $"#{srv.Id} — {srv.Name}", Foreground = Fg, Tag = srv.Id, Margin = new Thickness(2, 1, 0, 1) };
                _serverChecks.Add(cb);
                _serverPanel.Children.Add(cb);
            }
            if (_servers.Count == 0)
            {
                _serverPanel.Children.Add(new TextBlock { Text = Loc.T("WebUsers.NoServers"), Foreground = Dim, FontSize = 11 });
            }
            var scroll = new ScrollViewer { Content = _serverPanel, Height = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = WindowsGSM.Functions.Controls.DialogTheme.CardBg, BorderBrush = WindowsGSM.Functions.Controls.DialogTheme.CardBorder, BorderThickness = new Thickness(1), Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(scroll);
            SetServerPanelEnabled(false); // "All servers" is checked by default

            _status = new TextBlock { Foreground = Dim, MinHeight = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(_status);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var addBtn = new Wpf.Ui.Controls.Button { Content = Loc.T("WebUsers.AddUpdate"), Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 6, 0) };
            var delBtn = new Wpf.Ui.Controls.Button { Content = Loc.T("Common.Remove"), Appearance = Wpf.Ui.Controls.ControlAppearance.Danger, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 6, 0) };
            var closeBtn = new Wpf.Ui.Controls.Button { Content = Loc.T("Common.Close"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(14, 5, 14, 5) };
            addBtn.Click += (s, e) => AddOrUpdate();
            delBtn.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_user.Text)) { return; }
                if (System.Windows.MessageBox.Show(Loc.T("Common.ConfirmRemove"), Loc.T("Common.ConfirmTitle"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes) { return; }
                _store.Remove(_user.Text.Trim()); _store.Save(); Refresh(); _status.Text = Loc.T("WebUsers.Removed");
            };
            closeBtn.Click += (s, e) => Close();
            btns.Children.Add(addBtn); btns.Children.Add(delBtn); btns.Children.Add(closeBtn);
            root.Children.Add(btns);

            Content = root;
            Refresh();
        }

        private void SetServerPanelEnabled(bool enabled)
        {
            _serverPanel.IsEnabled = enabled;
            _serverPanel.Opacity = enabled ? 1.0 : 0.4;
        }

        /// <summary>Reflect a stored ServerIds value ("*", "" or "1,3,4") onto the checkboxes.</summary>
        private void ApplySelection(string serverIds)
        {
            bool all = string.IsNullOrWhiteSpace(serverIds) || serverIds.Trim() == "*";
            _allServers.IsChecked = all;
            var ids = all ? new HashSet<string>() : new HashSet<string>(serverIds.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
            foreach (var cb in _serverChecks) { cb.IsChecked = ids.Contains((string)cb.Tag); }
        }

        private string ComputeServerIds()
        {
            if (_allServers.IsChecked == true) { return "*"; }
            return string.Join(",", _serverChecks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag));
        }

        private void AddOrUpdate()
        {
            string user = _user.Text.Trim();
            if (string.IsNullOrWhiteSpace(user)) { _status.Foreground = Brushes.OrangeRed; _status.Text = Loc.T("WebUsers.UserRequired"); return; }
            bool exists = _store.Users.Any(x => string.Equals(x.Username, user, StringComparison.OrdinalIgnoreCase));
            if (!exists && string.IsNullOrEmpty(_pass.Password)) { _status.Foreground = Brushes.OrangeRed; _status.Text = Loc.T("WebUsers.PwRequired"); return; }
            string serverIds = ComputeServerIds();
            // Guard: not "all" but nothing ticked would (via AllowsServer) mean "all" — force an explicit choice.
            if (_allServers.IsChecked != true && serverIds.Length == 0)
            {
                _status.Foreground = Brushes.OrangeRed; _status.Text = Loc.T("WebUsers.PickServer");
                return;
            }
            var role = (WebRole)Math.Max(0, _role.SelectedIndex);
            _store.Set(user, _pass.Password, role, serverIds);
            _store.Save();
            _pass.Password = string.Empty;
            Refresh();
            _status.Foreground = Accent;
            _status.Text = Loc.T("WebUsers.Saved", user, role);
        }

        private void Refresh()
        {
            _list.Items.Clear();
            foreach (var u in _store.Users.OrderBy(x => x.Username))
            {
                string scope = string.IsNullOrWhiteSpace(u.ServerIds) || u.ServerIds == "*" ? Loc.T("WebUsers.ScopeAll") : Loc.T("WebUsers.ScopeServers", u.ServerIds);
                _list.Items.Add($"{u.Username}  —  {u.Role}  —  {scope}");
            }
        }
    }
}
