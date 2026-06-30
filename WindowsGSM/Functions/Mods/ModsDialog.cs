using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
        private readonly StackPanel _body;
        private readonly TextBlock _status;
        private WorkshopConfig _ws;

        private static readonly Brush Fg = Brushes.White;
        private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x9a, 0x9a, 0x9a));
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x4c, 0xc2, 0xd6));
        private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xe0, 0x6c, 0x6c));

        public ModsDialog(string serverId, string serverName, string game, string serverFiles)
        {
            _serverId = serverId;
            _serverFiles = serverFiles;
            _game = game;
            _profile = ModProfiles.For(game);

            Title = $"Mods — #{serverId} {serverName}";
            Width = 720;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x1f, 0x1f, 0x1f));
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
            var close = new Wpf.Ui.Controls.Button { Content = "Close", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5), HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (s, e) => Close();
            DockPanel.SetDock(close, Dock.Right);
            bottom.Children.Add(close);
            DockPanel.SetDock(bottom, Dock.Bottom);
            outer.Children.Add(bottom);

            // Right margin = gutter for the Fluent overlay scrollbar (otherwise it covers the content).
            _body = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            outer.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body });

            Content = outer;
            BuildBody();
        }

        private string MechanismLabel()
        {
            if (_profile == null) { return "Mods — unrecognized game"; }
            switch (_profile.Mechanism)
            {
                case ModMechanism.Folder: return $"File mods — {_game}";
                case ModMechanism.Workshop: return $"Steam Workshop — {_game}";
                default: return $"Mods — {_game}";
            }
        }

        private void BuildBody()
        {
            _body.Children.Clear();

            if (_profile == null)
            {
                _body.Children.Add(Info("This game has no known mod profile. You can open the server folder and manage the files manually."));
                _body.Children.Add(OpenButton("Open the server folder", _serverFiles));
                return;
            }

            switch (_profile.Mechanism)
            {
                case ModMechanism.None:
                    _body.Children.Add(Info("No server mod system for this game."));
                    break;
                case ModMechanism.Workshop:
                    BuildWorkshopView();
                    break;
                default:
                    BuildFolderView();
                    break;
            }
        }

        // ---- File mods view ----
        private void BuildFolderView()
        {
            string modDir = ModFolder.ModDirPath(_serverFiles, _profile);

            // Action bar
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var addBtn = new Wpf.Ui.Controls.Button { Content = "Add a mod…", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 6, 0) };
            addBtn.Click += (s, e) => AddMod();
            var openBtn = new Wpf.Ui.Controls.Button { Content = "Open the folder", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 6, 0) };
            openBtn.Click += (s, e) => { try { Directory.CreateDirectory(modDir); WindowsGSM.Shell.Open(modDir); } catch (Exception ex) { Fail(ex.Message); } };
            var refreshBtn = new Wpf.Ui.Controls.Button { Content = "Refresh", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(14, 5, 14, 5) };
            refreshBtn.Click += (s, e) => BuildBody();
            actions.Children.Add(addBtn);
            actions.Children.Add(openBtn);
            actions.Children.Add(refreshBtn);
            _body.Children.Add(actions);

            _body.Children.Add(new TextBlock { Text = modDir, Foreground = Dim, FontSize = 11, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });

            var mods = ModFolder.List(_serverFiles, _profile);
            if (mods.Count == 0)
            {
                _body.Children.Add(Info("No mods yet. Click \"Add a mod…\" or drop files into the folder."));
                return;
            }

            foreach (var m in mods)
            {
                var captured = m;
                var card = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a)),
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
                string sub = (m.IsDirectory ? "folder" : SizeStr(m.SizeBytes)) + (m.Enabled ? "" : " · disabled");
                meta.Children.Add(new TextBlock { Text = sub, Foreground = Dim, FontSize = 11 });
                dp.Children.Add(meta);

                card.Child = dp;
                _body.Children.Add(card);
            }
        }

        private void DoToggle(ModFolder.ModItem item)
        {
            try
            {
                ModFolder.Toggle(_serverFiles, _profile, item);
                _status.Foreground = Accent;
                _status.Text = $"\"{item.Name}\" {(item.Enabled ? "enabled" : "disabled")}. Restart the server to apply.";
                BuildBody();
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        private void AddMod()
        {
            try
            {
                string filter = _profile.Extensions != null && _profile.Extensions.Length > 0
                    ? "Mods|" + string.Join(";", Array.ConvertAll(_profile.Extensions, x => "*" + x)) + "|All files|*.*"
                    : "All files|*.*";
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Choose one or more mods", Multiselect = true, Filter = filter };
                if (dlg.ShowDialog(this) == true)
                {
                    foreach (string f in dlg.FileNames) { ModFolder.AddFile(_serverFiles, _profile, f); }
                    _status.Foreground = Accent;
                    _status.Text = $"{dlg.FileNames.Length} mod(s) added.";
                    BuildBody();
                }
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        // ---- Steam Workshop view ----
        private void BuildWorkshopView()
        {
            if (_ws == null) { _ws = WorkshopConfig.Load(_serverId); }

            // Add row
            var add = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            add.Children.Add(new TextBlock { Text = "Add:", Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var idBox = new TextBox { Width = 150, VerticalAlignment = VerticalAlignment.Center };
            ApplyPlaceholder(idBox, "Workshop ID");
            var nameBox = new TextBox { Width = 220, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            ApplyPlaceholder(nameBox, "Name (optional)");
            var addBtn = new Wpf.Ui.Controls.Button { Content = "+", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(12, 2, 12, 2) };
            addBtn.Click += (s, e) =>
            {
                string id = new string((idBox.Text ?? "").Trim().Where(char.IsDigit).ToArray());
                if (id.Length == 0) { Fail("Invalid Workshop ID (digits only)."); return; }
                _ws.Items.Add(new WorkshopEntry { Id = id, Name = (nameBox.Text ?? "").Trim(), Enabled = true });
                _ws.Save();
                BuildBody();
            };
            add.Children.Add(idBox);
            add.Children.Add(nameBox);
            add.Children.Add(addBtn);
            _body.Children.Add(add);

            // Global actions
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            if (!_profile.ServerAutoDownloads)
            {
                var dl = new Wpf.Ui.Controls.Button { Content = "Download (SteamCMD)", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 6, 0) };
                dl.Click += async (s, e) => await DownloadAll(dl);
                actions.Children.Add(dl);
            }
            if (!string.IsNullOrEmpty(_profile.ConfigKey) && !string.IsNullOrEmpty(_profile.ConfigFileRelative))
            {
                var apply = new Wpf.Ui.Controls.Button { Content = $"Write {_profile.ConfigKey} to the config", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(14, 5, 14, 5) };
                apply.Click += (s, e) =>
                {
                    try { _status.Foreground = Accent; _status.Text = WorkshopManager.ApplyToConfig(_serverFiles, _profile, _ws.Items); }
                    catch (Exception ex) { Fail(ex.Message); }
                };
                actions.Children.Add(apply);
            }
            if (actions.Children.Count > 0) { _body.Children.Add(actions); }

            if (_ws.Items.Count == 0)
            {
                _body.Children.Add(Info("No Workshop mods. Paste an ID (the number in the Steam Workshop URL) and click +."));
                return;
            }

            foreach (var entry in _ws.Items)
            {
                var captured = entry;
                var card = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 0, 0, 6),
                    Padding = new Thickness(10)
                };
                var dp = new DockPanel();

                var toggle = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = entry.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                toggle.Checked += (s, e) => { captured.Enabled = true; _ws.Save(); };
                toggle.Unchecked += (s, e) => { captured.Enabled = false; _ws.Save(); };
                DockPanel.SetDock(toggle, Dock.Left);
                dp.Children.Add(toggle);

                var del = new Wpf.Ui.Controls.Button { Content = "✕", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Width = 32, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Remove from the list" };
                del.Click += (s, e) => { _ws.Items.Remove(captured); _ws.Save(); BuildBody(); };
                DockPanel.SetDock(del, Dock.Right);
                dp.Children.Add(del);

                var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                meta.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(entry.Name) ? entry.Id : $"{entry.Name}", Foreground = Fg, FontWeight = FontWeights.SemiBold });
                meta.Children.Add(new TextBlock { Text = "ID " + entry.Id + (entry.Enabled ? "" : " · disabled"), Foreground = Dim, FontSize = 11 });
                dp.Children.Add(meta);

                card.Child = dp;
                _body.Children.Add(card);
            }
        }

        private async System.Threading.Tasks.Task DownloadAll(Wpf.Ui.Controls.Button btn)
        {
            btn.IsEnabled = false;
            try
            {
                int ok = 0, fail = 0;
                foreach (var e in _ws.Items.Where(x => x.Enabled).ToList())
                {
                    _status.Foreground = Dim;
                    _status.Text = $"Downloading {e.Id}…";
                    var (success, msg) = await WorkshopManager.DownloadAsync(_profile.WorkshopAppId, e.Id);
                    if (success) { ok++; } else { fail++; }
                }
                _status.Foreground = fail == 0 ? Accent : Warn;
                _status.Text = $"Download finished: {ok} OK, {fail} failure(s). Then \"Write {_profile.ConfigKey}\" if applicable.";
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
            _status.Text = "Error: " + msg;
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
