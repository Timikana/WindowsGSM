using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsGSM.Functions.Notifications
{
    /// <summary>
    /// Notification channels configuration (ntfy / Telegram / email / webhook), with a "Test" button
    /// per channel. Avoids editing notifications.json by hand. Built in code (no XAML).
    /// Secrets are encrypted at rest by <see cref="NotificationConfig.Save"/>.
    /// </summary>
    public class NotificationsDialog : Window
    {
        private readonly NotificationConfig _cfg;

        private static readonly Brush Fg = Brushes.White;
        private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x9a, 0x9a, 0x9a));
        private static readonly Brush CardBorder = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a));

        public NotificationsDialog()
        {
            _cfg = NotificationConfig.Load();

            Title = "Notifications";
            Width = 660;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x1f, 0x1f, 0x1f));
            NativeTheme.EnableDarkTitleBar(this);

            var outer = new DockPanel { Margin = new Thickness(14) };

            var intro = new TextBlock
            {
                Text = "Global notification channels. All alerts (crash, RAM, disk, updates...) are broadcast here. Fill in a channel, check \"Enable\", then \"Test\".",
                Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            };
            DockPanel.SetDock(intro, Dock.Top);
            outer.Children.Add(intro);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var save = new Wpf.Ui.Controls.Button { Content = "Save", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            var close = new Wpf.Ui.Controls.Button { Content = "Close", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, IsCancel = true, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(6, 0, 0, 0) };
            save.Click += (s, e) => { _cfg.Save(); DialogResult = true; Close(); };
            close.Click += (s, e) => Close();
            buttons.Children.Add(save);
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            outer.Children.Add(buttons);

            var body = new StackPanel();
            outer.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body });

            body.Children.Add(BuildNtfyCard());
            body.Children.Add(BuildTelegramCard());
            body.Children.Add(BuildEmailCard());
            body.Children.Add(BuildWebhookCard());

            Content = outer;
        }

        // ===== Per-channel cards =====

        private Border BuildNtfyCard()
        {
            var c = _cfg.Ntfy;
            var sp = NewCard("ntfy", c.Enabled, v => c.Enabled = v, out var resultTb, () => new NtfyNotifier(c));
            sp.Children.Insert(1, TextRow("Server (URL)", c.ServerUrl, v => c.ServerUrl = v));
            sp.Children.Insert(2, TextRow("Topic", c.Topic, v => c.Topic = v));
            sp.Children.Insert(3, TextRow("Token (optional)", c.AuthToken, v => c.AuthToken = v));
            sp.Children.Insert(4, TextRow("Priority (min/low/default/high/max)", c.Priority, v => c.Priority = v));
            return Wrap(sp);
        }

        private Border BuildTelegramCard()
        {
            var c = _cfg.Telegram;
            var sp = NewCard("Telegram", c.Enabled, v => c.Enabled = v, out var resultTb, () => new TelegramNotifier(c));
            sp.Children.Insert(1, TextRow("Bot Token", c.BotToken, v => c.BotToken = v));
            sp.Children.Insert(2, TextRow("Chat ID", c.ChatId, v => c.ChatId = v));
            return Wrap(sp);
        }

        private Border BuildEmailCard()
        {
            var c = _cfg.Email;
            var sp = NewCard("Email (SMTP)", c.Enabled, v => c.Enabled = v, out var resultTb, () => new EmailNotifier(c));
            sp.Children.Insert(1, TextRow("SMTP server", c.SmtpHost, v => c.SmtpHost = v));
            sp.Children.Insert(2, TextRow("Port", c.SmtpPort.ToString(), v => { if (int.TryParse(v, out int p)) { c.SmtpPort = p; } }));
            sp.Children.Insert(3, BoolRow("SSL/TLS", c.UseSsl, v => c.UseSsl = v));
            sp.Children.Insert(4, TextRow("Username", c.Username, v => c.Username = v));
            sp.Children.Insert(5, TextRow("Password", c.Password, v => c.Password = v));
            sp.Children.Insert(6, TextRow("Sender (From)", c.From, v => c.From = v));
            sp.Children.Insert(7, TextRow("Recipient(s) (To)", c.To, v => c.To = v));
            return Wrap(sp);
        }

        private Border BuildWebhookCard()
        {
            var c = _cfg.Webhook;
            var sp = NewCard("Generic webhook", c.Enabled, v => c.Enabled = v, out var resultTb, () => new WebhookNotifier(c));
            sp.Children.Insert(1, TextRow("URL", c.Url, v => c.Url = v));
            sp.Children.Insert(2, TextRow("Bearer (optional)", c.AuthBearer, v => c.AuthBearer = v));
            return Wrap(sp);
        }

        // ===== UI building blocks =====

        // Creates a card StackPanel with: [0]=title+enable line, then (the fields inserted by the caller),
        // then Test button + result area. Returns the panel; resultTb to update; testFactory to test.
        private StackPanel NewCard(string title, bool enabled, Action<bool> onEnable, out TextBlock resultTb, Func<INotifier> testFactory)
        {
            var sp = new StackPanel();

            var titleLine = new DockPanel();
            var enable = new CheckBox { Content = "Enable", Foreground = Fg, IsChecked = enabled, VerticalAlignment = VerticalAlignment.Center };
            enable.Checked += (s, e) => onEnable(true);
            enable.Unchecked += (s, e) => onEnable(false);
            DockPanel.SetDock(enable, Dock.Right);
            titleLine.Children.Add(enable);
            titleLine.Children.Add(new TextBlock { Text = title, Foreground = Fg, FontWeight = FontWeights.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(titleLine); // index 0

            var result = new TextBlock { Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            resultTb = result;

            var testBtn = new Wpf.Ui.Controls.Button { Content = "Test this channel", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            var capturedResult = result;
            testBtn.Click += async (s, e) =>
            {
                testBtn.IsEnabled = false;
                capturedResult.Foreground = Dim;
                capturedResult.Text = "⏳ Sending a test message…";
                try
                {
                    var notifier = testFactory();
                    bool ok = await notifier.SendAsync("Test WindowsGSM", "✅ This is a test message from WindowsGSM.");
                    capturedResult.Foreground = ok ? new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50)) : new SolidColorBrush(Color.FromRgb(0xe0, 0x50, 0x50));
                    capturedResult.Text = ok ? "✅ Sent. Check that it was received." : "❌ Failed (channel disabled or incorrect fields?). See the AppLog.";
                }
                catch (Exception ex)
                {
                    capturedResult.Foreground = new SolidColorBrush(Color.FromRgb(0xe0, 0x50, 0x50));
                    capturedResult.Text = "❌ Error: " + ex.Message;
                }
                finally { testBtn.IsEnabled = true; }
            };

            sp.Children.Add(testBtn); // these 2 stay at the end (fields are inserted before them via Insert)
            sp.Children.Add(result);
            return sp;
        }

        private static DockPanel TextRow(string label, string value, Action<string> onChange)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var lbl = new TextBlock { Text = label, Foreground = Dim, Width = 240, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(lbl, Dock.Left);
            dp.Children.Add(lbl);
            var box = new TextBox { Text = value ?? string.Empty, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 2, 6, 2) };
            box.TextChanged += (s, e) => onChange(box.Text);
            dp.Children.Add(box);
            return dp;
        }

        private static DockPanel BoolRow(string label, bool value, Action<bool> onChange)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var cb = new CheckBox { Content = label, Foreground = Brushes.White, IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
            cb.Checked += (s, e) => onChange(true);
            cb.Unchecked += (s, e) => onChange(false);
            dp.Children.Add(cb);
            return dp;
        }

        private Border Wrap(StackPanel sp) => new Border
        {
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(12),
            Child = sp
        };
    }
}
