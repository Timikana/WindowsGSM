using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsGSM.Functions.Donator
{
    /// <summary>
    /// "Donor feature" window shown when a non-donor opens a premium feature.
    /// - Points to the Patreon "Donor Connect" (Settings) for the author's donors.
    /// - Lets the OWNER unlock with their personal passphrase (PBKDF2).
    /// Returns DialogResult=true if access is unlocked.
    /// </summary>
    public class DonatorDialog : Window
    {
        private static readonly Brush Fg = Brushes.White;
        private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x9a, 0x9a, 0x9a));
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x4c, 0xc2, 0xd6));
        private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xe0, 0x6c, 0x6c));

        public DonatorDialog(string featureName)
        {
            Title = "Donor feature";
            Width = 560;
            Height = 330;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0x1f, 0x1f, 0x1f));
            NativeTheme.EnableDarkTitleBar(this);

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock { Text = "★ " + featureName, Foreground = Accent, FontWeight = FontWeights.SemiBold, FontSize = 16, Margin = new Thickness(0, 0, 0, 6) });
            root.Children.Add(new TextBlock
            {
                Text = "Donor-only feature. Become a donor via \"Donor Connect\" in Settings (Patreon), or — if you are the owner — unlock with your passphrase.",
                Foreground = Dim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            });

            root.Children.Add(new TextBlock { Text = "Owner passphrase:", Foreground = Fg, Margin = new Thickness(0, 0, 0, 4) });
            var passBox = new PasswordBox { MinWidth = 460, Margin = new Thickness(0, 0, 0, 6) };
            root.Children.Add(passBox);

            var status = new TextBlock { Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 12), MinHeight = 18 };
            root.Children.Add(status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var unlock = new Wpf.Ui.Controls.Button { Content = "Unlock", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 6, 0) };
            var close = new Wpf.Ui.Controls.Button { Content = "Close", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5) };
            unlock.Click += (s, e) =>
            {
                if (DonatorManager.UnlockOwner(passBox.Password))
                {
                    status.Foreground = Accent;
                    status.Text = "✔ Unlocked.";
                    DialogResult = true;
                    Close();
                }
                else
                {
                    status.Foreground = Warn;
                    status.Text = "Incorrect passphrase (or owner key not yet configured).";
                }
            };
            close.Click += (s, e) => Close();
            buttons.Children.Add(unlock);
            buttons.Children.Add(close);
            root.Children.Add(buttons);

            Content = root;
        }
    }
}
