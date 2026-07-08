using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowsGSM.Functions.Localization;

namespace WindowsGSM.Functions.Doctor
{
    /// <summary>
    /// A server's health report (built in code, no XAML overhaul). Includes a server picker
    /// (useful from the global sidebar, without depending on the grid selection).
    /// </summary>
    public class ServerDoctorDialog : Window
    {
        public class ServerInfo
        {
            public string Id;
            public string Name;
            public string Game;
            public string Port;
            public string Query;
            public bool Running;
            public override string ToString() => $"#{Id}  {Name}";
        }

        private readonly List<ServerInfo> _servers;
        private ServerInfo _current;
        // Once the user has opted into the external probe, keep re-running it on retest / server switch.
        private bool _extRan;
        private bool _closed; // set on close so the ~9s external probe never writes into a disposed dialog

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            base.OnClosed(e);
        }

        private readonly ComboBox _picker;
        private readonly StackPanel _body;
        private readonly StackPanel _extBody;
        private readonly Button _extButton;

        private static Brush Fg => WindowsGSM.Functions.Controls.DialogTheme.Fg;
        private static Brush Dim => WindowsGSM.Functions.Controls.DialogTheme.Dim;

        public ServerDoctorDialog(List<ServerInfo> servers, string selectedId = null)
        {
            _servers = servers ?? new List<ServerInfo>();
            _current = _servers.FirstOrDefault(s => s.Id == selectedId) ?? _servers.FirstOrDefault();

            Title = Loc.T("Doctor.Title");
            Width = 660;
            Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = WindowsGSM.Functions.Controls.DialogTheme.Bg;
            WindowsGSM.Functions.NativeTheme.EnableDarkTitleBar(this);

            var outer = new DockPanel { Margin = new Thickness(14) };

            // --- Header: server picker ---
            var headPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            headPanel.Children.Add(new TextBlock { Text = Loc.T("Doctor.ServerLabel"), Foreground = Fg, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _picker = new ComboBox { MinWidth = 320, VerticalAlignment = VerticalAlignment.Center };
            foreach (var s in _servers) { _picker.Items.Add(s); }
            _picker.SelectedItem = _current;
            _picker.SelectionChanged += async (s, e) =>
            {
                _current = _picker.SelectedItem as ServerInfo;
                _extBody.Children.Clear();
                BuildLocal();
                if (_extRan) { await RunExternal(); } // keep the external report in sync with the selected server
            };
            headPanel.Children.Add(_picker);
            DockPanel.SetDock(headPanel, Dock.Top);
            outer.Children.Add(headPanel);

            // --- Bottom buttons ---
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            _extButton = new Wpf.Ui.Controls.Button { Content = Loc.T("Doctor.ExternalButton"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            var retest = new Wpf.Ui.Controls.Button { Content = Loc.T("Doctor.Retest"), Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            var close = new Wpf.Ui.Controls.Button { Content = Loc.T("Common.Close"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            _extButton.Click += async (s, e) => await RunExternal();
            // "Retest" re-runs everything: local always, external too if it was already run once.
            retest.Click += async (s, e) => { BuildLocal(); if (_extRan) { await RunExternal(); } else { _extBody.Children.Clear(); } };
            close.Click += (s, e) => Close();
            buttons.Children.Add(_extButton);
            buttons.Children.Add(retest);
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            outer.Children.Add(buttons);

            // --- Scrolling body ---
            var content = new StackPanel();
            _body = new StackPanel();
            _extBody = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            content.Children.Add(_body);
            content.Children.Add(_extBody);
            outer.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content });

            Content = outer;
            BuildLocal();
        }

        private void BuildLocal()
        {
            _body.Children.Clear();
            if (_current == null)
            {
                _body.Children.Add(new TextBlock { Text = Loc.T("Doctor.NoServer"), Foreground = Dim });
                return;
            }
            try
            {
                foreach (var r in ServerDoctor.Run(_current.Id, _current.Game, _current.Port, _current.Query, _current.Running))
                {
                    _body.Children.Add(RowFor(r));
                }
            }
            catch (Exception ex)
            {
                // Never let a diagnostic throw out of an async-void handler (would crash the app).
                _body.Children.Add(new TextBlock { Text = Loc.T("Doctor.Error", ex.Message), Foreground = Dim, TextWrapping = TextWrapping.Wrap });
            }
        }

        private async System.Threading.Tasks.Task RunExternal()
        {
            if (_current == null) { return; }
            var target = _current; // capture: the user may switch servers or close during the probe
            _extRan = true;
            _extButton.IsEnabled = false;
            _extBody.Children.Clear();
            _extBody.Children.Add(SectionHeader(Loc.T("Doctor.ExternalHeader")));
            _extBody.Children.Add(new TextBlock { Text = Loc.T("Doctor.ExternalInProgress"), Foreground = Dim, Margin = new Thickness(0, 4, 0, 0) });
            try
            {
                var results = await ServerDoctor.CheckExternalAsync(target.Game, target.Port, target.Query);
                if (_closed || _current != target) { return; } // dialog closed or server changed mid-probe → discard
                _extBody.Children.Clear();
                _extBody.Children.Add(SectionHeader(Loc.T("Doctor.ExternalHeader")));
                foreach (var r in results) { _extBody.Children.Add(RowFor(r)); }
            }
            catch (Exception ex)
            {
                _extBody.Children.Add(new TextBlock { Text = Loc.T("Doctor.Error", ex.Message), Foreground = Dim });
            }
            finally
            {
                _extButton.IsEnabled = true;
            }
        }

        private static TextBlock SectionHeader(string text) => new TextBlock
        {
            Text = text, Foreground = WindowsGSM.Functions.Controls.DialogTheme.Fg, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4)
        };

        private Border RowFor(DiagnosticResult r)
        {
            var row = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 7, 0, 7)
            };
            var line = new DockPanel();
            var badge = new TextBlock { Text = Glyph(r.Status), Foreground = StatusBrush(r.Status), FontWeight = FontWeights.Bold, Width = 30 };
            DockPanel.SetDock(badge, Dock.Left);
            line.Children.Add(badge);
            var texts = new StackPanel();
            texts.Children.Add(new TextBlock { Text = r.Check, Foreground = Fg, FontWeight = FontWeights.SemiBold });
            texts.Children.Add(new TextBlock { Text = r.Detail, Foreground = Dim, TextWrapping = TextWrapping.Wrap });
            line.Children.Add(texts);
            row.Child = line;
            return row;
        }

        private static string Glyph(DiagStatus s)
        {
            switch (s)
            {
                case DiagStatus.Ok: return "OK";
                case DiagStatus.Warn: return "!";
                case DiagStatus.Fail: return "X";
                case DiagStatus.Skip: return "–";
                default: return "i";
            }
        }

        private static Brush StatusBrush(DiagStatus s)
        {
            switch (s)
            {
                case DiagStatus.Ok: return new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50));
                case DiagStatus.Warn: return new SolidColorBrush(Color.FromRgb(0xe0, 0xa0, 0x30));
                case DiagStatus.Fail: return new SolidColorBrush(Color.FromRgb(0xe0, 0x50, 0x50));
                case DiagStatus.Skip: return new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));
                default: return new SolidColorBrush(Color.FromRgb(0x5a, 0x9b, 0xd4));
            }
        }
    }
}
