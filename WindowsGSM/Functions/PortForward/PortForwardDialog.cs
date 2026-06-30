using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsGSM.Functions.PortForward
{
    /// <summary>
    /// Auto port-forward configuration window, built in code (no rework of the main XAML).
    /// Global master switch + per server the list of suggested ports (on/off checkboxes)
    /// + manual add. Also acts as an "advisor": the displayed list = exactly the ports to open.
    /// </summary>
    public class PortForwardDialog : Window
    {
        public class ServerInfo
        {
            public string Id;
            public string Name;
            public string Game;
            public string Port;
            public string QueryPort;
        }

        private readonly PortForwardConfig _cfg;
        private readonly List<ServerInfo> _servers;
        private readonly StackPanel _body;

        private static readonly Brush Fg = Brushes.White;
        private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x9a, 0x9a, 0x9a));

        public PortForwardDialog(List<ServerInfo> servers)
        {
            _servers = servers ?? new List<ServerInfo>();
            _cfg = PortForwardConfig.Load();

            Title = "Ports / UPnP";
            Width = 660;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x1f, 0x1f, 0x1f));
            NativeTheme.EnableDarkTitleBar(this);

            var outer = new DockPanel { Margin = new Thickness(14) };

            // --- Header: master switch + tip ---
            var header = new StackPanel();
            var master = new CheckBox
            {
                Content = "Enable UPnP auto port-forward (opens/closes ports on the router at start/stop)",
                Foreground = Fg,
                IsChecked = _cfg.Enabled,
                Margin = new Thickness(0, 0, 0, 6)
            };
            master.Checked += (s, e) => _cfg.Enabled = true;
            master.Unchecked += (s, e) => _cfg.Enabled = false;
            header.Children.Add(master);
            header.Children.Add(new TextBlock
            {
                Text = "Tip: if UPnP is disabled on the router (e.g. OPNsense), leave unchecked and simply copy the ports listed below into your manual forward. RCON is never suggested enabled: only open it if necessary.",
                Foreground = Dim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
            DockPanel.SetDock(header, Dock.Top);
            outer.Children.Add(header);

            // --- Bottom buttons ---
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var save = new Wpf.Ui.Controls.Button { Content = "Save", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            var close = new Wpf.Ui.Controls.Button { Content = "Close", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            save.Click += (s, e) => { _cfg.Save(); DialogResult = true; Close(); };
            close.Click += (s, e) => Close();
            buttons.Children.Add(save);
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            outer.Children.Add(buttons);

            // --- Scrolling body ---
            _body = new StackPanel();
            outer.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body });

            Content = outer;
            BuildBody();
        }

        private void BuildBody()
        {
            _body.Children.Clear();

            if (_servers.Count == 0)
            {
                _body.Children.Add(new TextBlock { Text = "No server.", Foreground = Dim });
                return;
            }

            foreach (var sv in _servers)
            {
                var suggestions = PortResolver.Suggest(sv.Game, sv.Port, sv.QueryPort);
                var spf = _cfg.EnsureServer(sv.Id, suggestions);

                var card = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(10)
                };
                var sp = new StackPanel();

                var title = new CheckBox
                {
                    Content = $"#{sv.Id}  {sv.Name}   ({sv.Game})",
                    Foreground = Fg,
                    FontWeight = FontWeights.Bold,
                    IsChecked = spf.Enabled
                };
                title.Checked += (s, e) => spf.Enabled = true;
                title.Unchecked += (s, e) => spf.Enabled = false;
                sp.Children.Add(title);

                var cardSpf = spf;
                foreach (var pm in spf.Ports)
                {
                    var captured = pm;
                    var rowPanel = new DockPanel { Margin = new Thickness(22, 3, 0, 0) };

                    // Delete (on the right)
                    var del = new Wpf.Ui.Controls.Button { Content = "✕", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Width = 32, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Delete this port", VerticalAlignment = VerticalAlignment.Center };
                    del.Click += (s, e) => { cardSpf.Ports.Remove(captured); BuildBody(); };
                    DockPanel.SetDock(del, Dock.Right);
                    rowPanel.Children.Add(del);

                    // Editable protocol (on the right)
                    var rproto = new ComboBox { Width = 80, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    rproto.Items.Add("Both"); rproto.Items.Add("TCP"); rproto.Items.Add("UDP");
                    rproto.SelectedItem = captured.Protocol == PortProtocol.Tcp ? "TCP" : captured.Protocol == PortProtocol.Udp ? "UDP" : "Both";
                    rproto.SelectionChanged += (s, e) =>
                    {
                        captured.Protocol = (string)rproto.SelectedItem == "TCP" ? PortProtocol.Tcp
                                          : (string)rproto.SelectedItem == "UDP" ? PortProtocol.Udp
                                          : PortProtocol.Both;
                    };
                    DockPanel.SetDock(rproto, Dock.Right);
                    rowPanel.Children.Add(rproto);

                    // Enabled checkbox + label (fills)
                    var cb = new CheckBox { Content = $"{captured.Port}  —  {captured.Label}", Foreground = Fg, IsChecked = captured.Enabled, VerticalAlignment = VerticalAlignment.Center };
                    cb.Checked += (s, e) => captured.Enabled = true;
                    cb.Unchecked += (s, e) => captured.Enabled = false;
                    rowPanel.Children.Add(cb);

                    sp.Children.Add(rowPanel);
                }

                // Manual add row
                var add = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 8, 0, 0) };
                add.Children.Add(new TextBlock { Text = "Add:", Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                var portBox = new TextBox { Width = 70, VerticalAlignment = VerticalAlignment.Center };
                var proto = new ComboBox { Width = 80, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                proto.Items.Add("Both");
                proto.Items.Add("TCP");
                proto.Items.Add("UDP");
                proto.SelectedIndex = 0;
                var addBtn = new Wpf.Ui.Controls.Button { Content = "+", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(10, 2, 10, 2) };
                var capturedSpf = spf;
                addBtn.Click += (s, e) =>
                {
                    if (int.TryParse(portBox.Text, out int pn) && pn > 0 && pn <= 65535)
                    {
                        capturedSpf.Ports.Add(new PortMapping
                        {
                            Port = pn,
                            Protocol = (string)proto.SelectedItem == "TCP" ? PortProtocol.Tcp
                                     : (string)proto.SelectedItem == "UDP" ? PortProtocol.Udp
                                     : PortProtocol.Both,
                            Label = "Manual",
                            Enabled = true
                        });
                        BuildBody();
                    }
                };
                add.Children.Add(portBox);
                add.Children.Add(proto);
                add.Children.Add(addBtn);
                sp.Children.Add(add);

                card.Child = sp;
                _body.Children.Add(card);
            }
        }
    }
}
