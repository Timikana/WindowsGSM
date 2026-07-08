using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WindowsGSM.Functions.Localization;

namespace WindowsGSM.Functions.Controls
{
    /// <summary>
    /// A password entry with a reveal (eye) toggle. Masked by default; clicking the eye swaps to a plain
    /// TextBox so a forgotten password can be read back. Exposes <see cref="Password"/> (get/set) so it is a
    /// drop-in replacement for a <see cref="PasswordBox"/>.
    /// </summary>
    public class RevealPasswordBox : Grid
    {
        private readonly PasswordBox _pwd = new PasswordBox { Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center };
        private readonly TextBox _txt = new TextBox { Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        private readonly ToggleButton _eye;
        private bool _sync;

        /// <summary>Raised on user edits (not when Password is set programmatically).</summary>
        public event System.EventHandler PasswordChanged;

        public RevealPasswordBox()
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Keep the masked box and the revealed box in sync (guard against re-entrancy).
            _pwd.PasswordChanged += (s, e) => { if (!_sync) { _sync = true; _txt.Text = _pwd.Password; _sync = false; PasswordChanged?.Invoke(this, System.EventArgs.Empty); } };
            _txt.TextChanged += (s, e) => { if (!_sync) { _sync = true; _pwd.Password = _txt.Text; _sync = false; PasswordChanged?.Invoke(this, System.EventArgs.Empty); } };

            SetColumn(_pwd, 0);
            SetColumn(_txt, 0);
            Children.Add(_pwd);
            Children.Add(_txt);

            _eye = new ToggleButton
            {
                Width = 32,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = Loc.T("Common.ShowPassword"),
                Content = Icon(Wpf.Ui.Controls.SymbolRegular.Eye24)
            };
            _eye.Checked += (s, e) =>
            {
                _txt.Visibility = Visibility.Visible;
                _pwd.Visibility = Visibility.Collapsed;
                _eye.Content = Icon(Wpf.Ui.Controls.SymbolRegular.EyeOff24);
                _txt.Focus();
                _txt.CaretIndex = _txt.Text.Length;
            };
            _eye.Unchecked += (s, e) =>
            {
                _txt.Visibility = Visibility.Collapsed;
                _pwd.Visibility = Visibility.Visible;
                _eye.Content = Icon(Wpf.Ui.Controls.SymbolRegular.Eye24);
            };
            SetColumn(_eye, 1);
            Children.Add(_eye);
        }

        private static Wpf.Ui.Controls.SymbolIcon Icon(Wpf.Ui.Controls.SymbolRegular s) =>
            new Wpf.Ui.Controls.SymbolIcon { Symbol = s, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)) };

        /// <summary>The current password (works whether it is masked or revealed).</summary>
        public string Password
        {
            get => _pwd.Password;
            set { _sync = true; _pwd.Password = value ?? string.Empty; _txt.Text = value ?? string.Empty; _sync = false; }
        }

        /// <summary>Use a monospaced font (for tokens/keys).</summary>
        public void UseMonospace()
        {
            var f = new FontFamily("Consolas");
            _pwd.FontFamily = f;
            _txt.FontFamily = f;
        }
    }
}
