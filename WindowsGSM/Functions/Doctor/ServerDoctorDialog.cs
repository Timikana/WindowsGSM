using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsGSM.Functions.Doctor
{
    /// <summary>
    /// Bulletin de santé d'un serveur (construit en code, pas de refonte XAML). Inclut un sélecteur
    /// de serveur (utile depuis la sidebar globale, sans dépendre de la sélection de la grille).
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

        private readonly ComboBox _picker;
        private readonly StackPanel _body;
        private readonly StackPanel _extBody;
        private readonly Button _extButton;

        private static readonly Brush Fg = Brushes.White;
        private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x9a, 0x9a, 0x9a));

        public ServerDoctorDialog(List<ServerInfo> servers, string selectedId = null)
        {
            _servers = servers ?? new List<ServerInfo>();
            _current = _servers.FirstOrDefault(s => s.Id == selectedId) ?? _servers.FirstOrDefault();

            Title = "Server Doctor";
            Width = 660;
            Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x1f, 0x1f, 0x1f));
            WindowsGSM.Functions.NativeTheme.EnableDarkTitleBar(this);

            var outer = new DockPanel { Margin = new Thickness(14) };

            // --- En-tête : sélecteur de serveur ---
            var headPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            headPanel.Children.Add(new TextBlock { Text = "Serveur :", Foreground = Fg, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _picker = new ComboBox { MinWidth = 320, VerticalAlignment = VerticalAlignment.Center };
            foreach (var s in _servers) { _picker.Items.Add(s); }
            _picker.SelectedItem = _current;
            _picker.SelectionChanged += (s, e) =>
            {
                _current = _picker.SelectedItem as ServerInfo;
                _extBody.Children.Clear();
                BuildLocal();
            };
            headPanel.Children.Add(_picker);
            DockPanel.SetDock(headPanel, Dock.Top);
            outer.Children.Add(headPanel);

            // --- Boutons bas ---
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            _extButton = new Wpf.Ui.Controls.Button { Content = "Joignabilité externe…", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            var retest = new Wpf.Ui.Controls.Button { Content = "Re-tester", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            var close = new Wpf.Ui.Controls.Button { Content = "Fermer", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            _extButton.Click += async (s, e) => await RunExternal();
            retest.Click += (s, e) => { _extBody.Children.Clear(); BuildLocal(); };
            close.Click += (s, e) => Close();
            buttons.Children.Add(_extButton);
            buttons.Children.Add(retest);
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            outer.Children.Add(buttons);

            // --- Corps défilant ---
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
                _body.Children.Add(new TextBlock { Text = "Aucun serveur à diagnostiquer.", Foreground = Dim });
                return;
            }
            foreach (var r in ServerDoctor.Run(_current.Id, _current.Game, _current.Port, _current.Query, _current.Running))
            {
                _body.Children.Add(RowFor(r));
            }
        }

        private async System.Threading.Tasks.Task RunExternal()
        {
            if (_current == null) { return; }
            _extButton.IsEnabled = false;
            _extBody.Children.Clear();
            _extBody.Children.Add(SectionHeader("Joignabilité externe (via check-host.net)"));
            _extBody.Children.Add(new TextBlock { Text = "⏳ Test en cours… (ton IP publique est envoyée à check-host.net)", Foreground = Dim, Margin = new Thickness(0, 4, 0, 0) });
            try
            {
                var results = await ServerDoctor.CheckExternalAsync(_current.Game, _current.Port, _current.Query);
                _extBody.Children.Clear();
                _extBody.Children.Add(SectionHeader("Joignabilité externe (via check-host.net)"));
                foreach (var r in results) { _extBody.Children.Add(RowFor(r)); }
            }
            catch (Exception ex)
            {
                _extBody.Children.Add(new TextBlock { Text = "Erreur : " + ex.Message, Foreground = Dim });
            }
            finally
            {
                _extButton.IsEnabled = true;
            }
        }

        private static TextBlock SectionHeader(string text) => new TextBlock
        {
            Text = text, Foreground = Brushes.White, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4)
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
