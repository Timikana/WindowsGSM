using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowsGSM.Functions.Localization;

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
        private static Brush Fg => WindowsGSM.Functions.Controls.DialogTheme.Fg;
        private static Brush Dim => WindowsGSM.Functions.Controls.DialogTheme.Dim;
        private static Brush Accent => WindowsGSM.Functions.Controls.DialogTheme.Accent;
        private static Brush Warn => WindowsGSM.Functions.Controls.DialogTheme.Warn;

        public DonatorDialog(string featureName)
        {
            Title = Loc.T("Donator.Title");
            Width = 560;
            Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = WindowsGSM.Functions.Controls.DialogTheme.Bg;
            NativeTheme.EnableDarkTitleBar(this);

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock { Text = "★ " + featureName, Foreground = Accent, FontWeight = FontWeights.SemiBold, FontSize = 16, Margin = new Thickness(0, 0, 0, 6) });
            root.Children.Add(new TextBlock
            {
                Text = Loc.T("Donator.Explain"),
                Foreground = Dim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            });

            root.Children.Add(new TextBlock { Text = Loc.T("Donator.OwnerPassphrase"), Foreground = Fg, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            var passBox = new WindowsGSM.Functions.Controls.RevealPasswordBox { MinWidth = 460, Margin = new Thickness(0, 0, 0, 6) };
            root.Children.Add(passBox);

            var status = new TextBlock { Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 12), MinHeight = 18 };
            root.Children.Add(status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var unlock = new Wpf.Ui.Controls.Button { Content = Loc.T("Donator.Unlock"), Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 6, 0) };
            var close = new Wpf.Ui.Controls.Button { Content = Loc.T("Common.Close"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5) };
            unlock.Click += (s, e) =>
            {
                if (DonatorManager.UnlockOwner(passBox.Password))
                {
                    status.Foreground = Accent;
                    status.Text = Loc.T("Donator.Unlocked");
                    DialogResult = true;
                    Close();
                }
                else
                {
                    status.Foreground = Warn;
                    status.Text = Loc.T("Donator.BadPassphrase");
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
