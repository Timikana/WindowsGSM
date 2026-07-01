using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowsGSM.Functions.Localization;

namespace WindowsGSM.Functions.ConfigEditor
{
    /// <summary>
    /// "Per-game styled" configuration editor: if the game has a curated schema (readable labels,
    /// toggles/sliders/combos, masked secrets) it is displayed grouped by theme; otherwise raw
    /// key/value editing. Any other config file found in serverfiles is editable in raw mode via the
    /// top selector. Faithful save (.wgsmbak backup).
    /// </summary>
    public class ConfigEditorDialog : Window
    {
        private readonly string _serverFiles;
        private readonly string _gameFullName;
        private readonly List<GameConfigSchema> _schemas;

        private readonly ComboBox _fileSelector;
        private readonly StackPanel _body;
        private readonly TextBlock _status;

        private IConfigModel _model;
        private GameConfigSchema _activeSchema;
        private bool _palworld;
        private readonly List<Action> _applies = new List<Action>();

        private static readonly Brush Fg = Brushes.White;
        private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x9a, 0x9a, 0x9a));
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x4c, 0xc2, 0xd6));
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public ConfigEditorDialog(string serverId, string serverName, string gameFullName, string serverFiles)
        {
            _serverFiles = serverFiles;
            _gameFullName = gameFullName;
            _schemas = GameSchemas.All(gameFullName);

            Title = Loc.T("Config.Title", serverId, serverName);
            Width = 720;
            Height = 760;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x1f, 0x1f, 0x1f));
            NativeTheme.EnableDarkTitleBar(this);

            var outer = new DockPanel { Margin = new Thickness(14) };

            // --- Header: file selector ---
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(new TextBlock { Text = Loc.T("Config.FileLabel"), Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _fileSelector = new ComboBox { MinWidth = 420, VerticalAlignment = VerticalAlignment.Center };
            _fileSelector.SelectionChanged += (s, e) => { if (_fileSelector.SelectedItem is FileItem fi) { LoadFile(fi); } };
            header.Children.Add(_fileSelector);
            DockPanel.SetDock(header, Dock.Top);
            outer.Children.Add(header);

            // --- Bottom buttons ---
            var buttons = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            _status = new TextBlock { Foreground = Dim, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(_status, Dock.Left);
            buttons.Children.Add(_status);
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Wpf.Ui.Controls.Button { Content = Loc.T("Common.Save"), Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            var close = new Wpf.Ui.Controls.Button { Content = Loc.T("Common.Close"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            save.Click += (s, e) => SaveCurrent();
            close.Click += (s, e) => Close();
            btnRow.Children.Add(save);
            btnRow.Children.Add(close);
            buttons.Children.Add(btnRow);
            DockPanel.SetDock(buttons, Dock.Bottom);
            outer.Children.Add(buttons);

            // --- Scrolling body ---
            // Right margin = gutter for the Fluent overlay scrollbar (otherwise it covers the values).
            _body = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            outer.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body });

            Content = outer;
            PopulateFiles();
        }

        private class FileItem
        {
            public string FullPath;
            public string Display;
            public GameConfigSchema Schema;   // null = raw editing
            public override string ToString() => Display;
        }

        private void PopulateFiles()
        {
            var items = new List<FileItem>();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Curated primary file(s) (a game may have several: ARK = 2).
            foreach (var schema in _schemas)
            {
                string primary = null;
                foreach (string rel in schema.RelativePaths ?? Array.Empty<string>())
                {
                    string full = Path.Combine(_serverFiles ?? "", rel);
                    if (File.Exists(full)) { primary = full; break; }
                }
                if (primary == null || taken.Contains(primary)) { continue; }
                taken.Add(primary);
                string label = string.IsNullOrEmpty(schema.Label) ? Loc.T("Config.GameSettings", schema.GameMatch) : schema.Label;
                items.Add(new FileItem { FullPath = primary, Display = $"★ {Path.GetFileName(primary)} — {label}", Schema = schema });
            }

            // Other config files discovered (raw mode).
            foreach (string f in ConfigDiscovery.Find(_serverFiles))
            {
                if (taken.Contains(f)) { continue; }
                string rel = (_serverFiles != null && f.StartsWith(_serverFiles, StringComparison.OrdinalIgnoreCase)) ? f.Substring(_serverFiles.Length).TrimStart('\\', '/') : f;
                items.Add(new FileItem { FullPath = f, Display = rel });
            }

            if (items.Count == 0)
            {
                _fileSelector.IsEnabled = false;
                _body.Children.Add(new TextBlock { Text = Loc.T("Config.NoFile"), Foreground = Dim, TextWrapping = TextWrapping.Wrap });
                return;
            }

            foreach (var it in items) { _fileSelector.Items.Add(it); }
            _fileSelector.SelectedIndex = 0; // triggers LoadFile
        }

        private void LoadFile(FileItem fi)
        {
            _applies.Clear();
            _body.Children.Clear();
            _status.Text = "";
            _activeSchema = fi.Schema;
            try
            {
                _palworld = _activeSchema?.Model == "palworld";
                _model = _palworld ? (IConfigModel)PalworldConfig.Load(fi.FullPath) : ConfigFile.Load(fi.FullPath);
            }
            catch (Exception ex)
            {
                _body.Children.Add(new TextBlock { Text = Loc.T("Config.CannotRead", ex.Message), Foreground = Dim, TextWrapping = TextWrapping.Wrap });
                return;
            }

            if (_activeSchema != null) { BuildCurated(); }
            else { BuildRaw(); }
        }

        // ---- Curated view (per-game schema) ----
        private void BuildCurated()
        {
            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string lastGroup = null;

            foreach (var spec in _activeSchema.Fields)
            {
                var entry = _model.Entries.FirstOrDefault(e => string.Equals(e.Key, spec.Key, StringComparison.OrdinalIgnoreCase));
                if (entry == null) { continue; } // key absent from this file -> skip
                handled.Add(spec.Key);

                if (spec.Group != lastGroup) { AddGroupHeader(spec.Group); lastGroup = spec.Group; }
                _body.Children.Add(BuildFieldRow(spec, entry));
            }

            // Fallback: all keys not covered by the schema, editable in raw mode.
            var rest = _model.Entries.Where(e => !handled.Contains(e.Key)).ToList();
            if (rest.Count > 0)
            {
                var exp = new Expander { Header = Loc.T("Config.OtherSettings", rest.Count), Foreground = Dim, Margin = new Thickness(0, 12, 0, 0) };
                var inner = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (var e in rest) { inner.Children.Add(BuildRawRow(e)); }
                exp.Content = inner;
                _body.Children.Add(exp);
            }
        }

        // ---- Raw view (file without a schema) ----
        private void BuildRaw()
        {
            if (_model.Entries.Count == 0)
            {
                _body.Children.Add(new TextBlock { Text = Loc.T("Config.NoEntries"), Foreground = Dim });
                return;
            }
            string lastSection = null;
            foreach (var e in _model.Entries)
            {
                if (e.Section != lastSection) { AddGroupHeader(string.IsNullOrEmpty(e.Section) ? Loc.T("Config.GlobalSection") : e.Section); lastSection = e.Section; }
                _body.Children.Add(BuildRawRow(e));
            }
        }

        private void AddGroupHeader(string title)
        {
            _body.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Accent,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Margin = new Thickness(0, 12, 0, 4)
            });
        }

        // ---- Rows ----
        private FrameworkElement BuildFieldRow(FieldSpec spec, ConfigEntry entry)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };

            var labelPanel = new StackPanel { Width = 230, VerticalAlignment = VerticalAlignment.Center };
            labelPanel.Children.Add(new TextBlock { Text = spec.Label, Foreground = Fg });
            if (!string.IsNullOrEmpty(spec.Description))
            {
                labelPanel.Children.Add(new TextBlock { Text = spec.Description, Foreground = Dim, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            }
            DockPanel.SetDock(labelPanel, Dock.Left);
            dp.Children.Add(labelPanel);

            FrameworkElement control;
            switch (spec.Kind)
            {
                case FieldKind.Bool: control = BoolControl(entry); break;
                case FieldKind.Enum: control = EnumControl(spec, entry); break;
                case FieldKind.Secret: control = SecretControl(entry); break;
                case FieldKind.Int:
                case FieldKind.Float: control = NumberControl(spec, entry); break;
                default: control = TextControl(entry); break;
            }
            control.VerticalAlignment = VerticalAlignment.Center;
            dp.Children.Add(control);
            return dp;
        }

        private FrameworkElement BuildRawRow(ConfigEntry entry)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            dp.Children.Add(new TextBlock { Text = entry.Key, Foreground = Fg, Width = 230, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = entry.Key });
            var tb = new TextBox { Text = entry.Value ?? "", VerticalAlignment = VerticalAlignment.Center };
            var captured = entry;
            _applies.Add(() => _model.Set(captured, tb.Text));
            dp.Children.Add(tb);
            return dp;
        }

        private FrameworkElement BoolControl(ConfigEntry entry)
        {
            var sw = new Wpf.Ui.Controls.ToggleSwitch
            {
                IsChecked = (entry.Value ?? "").Trim().Equals("True", StringComparison.OrdinalIgnoreCase),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var captured = entry;
            string tVal = _activeSchema?.BoolTrue ?? "True";
            string fVal = _activeSchema?.BoolFalse ?? "False";
            _applies.Add(() => _model.Set(captured, sw.IsChecked == true ? tVal : fVal));
            return sw;
        }

        private FrameworkElement EnumControl(FieldSpec spec, ConfigEntry entry)
        {
            var cb = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            string cur = Unquote((entry.Value ?? "").Trim());
            foreach (var v in spec.EnumValues ?? Array.Empty<string>()) { cb.Items.Add(v); }
            if (!cb.Items.Contains(cur) && !string.IsNullOrEmpty(cur)) { cb.Items.Add(cur); }
            cb.SelectedItem = cur;
            var captured = entry;
            _applies.Add(() => { if (cb.SelectedItem is string sel) { _model.Set(captured, sel); } });
            return cb;
        }

        private FrameworkElement SecretControl(ConfigEntry entry)
        {
            var pb = new PasswordBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Password = Unquote(entry.Value ?? "") };
            var captured = entry;
            _applies.Add(() => _model.Set(captured, MaybeQuote(pb.Password)));
            return pb;
        }

        private FrameworkElement NumberControl(FieldSpec spec, ConfigEntry entry)
        {
            bool isFloat = spec.Kind == FieldKind.Float;
            double cur = ParseNum(entry.Value, isFloat);
            bool useSlider = spec.Max > spec.Min && (spec.Max - spec.Min) <= 50.0;

            if (!useSlider)
            {
                var tb = new TextBox { Text = isFloat ? cur.ToString("0.######", Inv) : ((long)cur).ToString(Inv), Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
                var capturedE = entry;
                _applies.Add(() =>
                {
                    if (double.TryParse(tb.Text.Trim(), NumberStyles.Any, Inv, out double v))
                    {
                        _model.Set(capturedE, isFloat ? v.ToString("0.000000", Inv) : ((long)v).ToString(Inv));
                    }
                });
                return tb;
            }

            var panel = new DockPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
            var val = new TextBlock { Foreground = Fg, Width = 56, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            var slider = new Slider
            {
                Minimum = spec.Min,
                Maximum = spec.Max,
                Value = Math.Max(spec.Min, Math.Min(spec.Max, cur)),
                TickFrequency = spec.Step,
                IsSnapToTickEnabled = !isFloat,
                MinWidth = 280,
                VerticalAlignment = VerticalAlignment.Center
            };
            Action refresh = () => val.Text = isFloat ? slider.Value.ToString("0.0", Inv) : ((long)Math.Round(slider.Value)).ToString(Inv);
            slider.ValueChanged += (s, e) => refresh();
            refresh();
            DockPanel.SetDock(val, Dock.Right);
            panel.Children.Add(val);
            panel.Children.Add(slider);
            var capturedEntry = entry;
            _applies.Add(() => _model.Set(capturedEntry, isFloat ? slider.Value.ToString("0.000000", Inv) : ((long)Math.Round(slider.Value)).ToString(Inv)));
            return panel;
        }

        private FrameworkElement TextControl(ConfigEntry entry)
        {
            var tb = new TextBox { Text = Unquote(entry.Value ?? ""), HorizontalAlignment = HorizontalAlignment.Stretch };
            var captured = entry;
            _applies.Add(() => _model.Set(captured, MaybeQuote(tb.Text)));
            return tb;
        }

        private void SaveCurrent()
        {
            if (_model == null) { Close(); return; }
            try
            {
                foreach (var a in _applies) { a(); }
                _model.Save();
                _status.Foreground = Accent;
                _status.Text = Loc.T("Config.Saved");
            }
            catch (Exception ex)
            {
                _status.Foreground = new SolidColorBrush(Color.FromRgb(0xe0, 0x6c, 0x6c));
                _status.Text = Loc.T("Config.SaveFailed", ex.Message);
                Functions.AppLog.Warn("ConfigEditor/Save", ex.Message);
            }
        }

        // ---- Value helpers ----
        private string MaybeQuote(string v) => _palworld ? "\"" + (v ?? "").Replace("\"", "") + "\"" : (v ?? "");

        private static string Unquote(string v)
        {
            if (v != null && v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') { return v.Substring(1, v.Length - 2); }
            return v;
        }

        private static double ParseNum(string v, bool isFloat)
        {
            v = Unquote((v ?? "").Trim());
            return double.TryParse(v, NumberStyles.Any, Inv, out double d) ? d : 0;
        }
    }
}
