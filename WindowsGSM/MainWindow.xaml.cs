using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using System.Net;
using System.IO.Compression;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using NCrontab;
using System.Collections.Generic;
using System.Collections;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using System.Management;
using System.Windows.Media.Imaging;
using WindowsGSM.Functions;
using WindowsGSM.Functions.Localization;
using Label = System.Windows.Controls.Label;
using Orientation = System.Windows.Controls.Orientation;
using System.Windows.Documents;
using MessageBox = System.Windows.MessageBox;

namespace WindowsGSM
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, WindowShowStyle nCmdShow);

        [DllImport("user32.dll")]
        private static extern int SetWindowText(IntPtr hWnd, string windowName);

        private static class RegistryKeyName
        {
            public const string HardWareAcceleration = "HardWareAcceleration";
            public const string UIAnimation = "UIAnimation";
            public const string DarkTheme = "DarkTheme";
            public const string StartOnBoot = "StartOnBoot";
            public const string RestartOnCrash = "RestartOnCrash";
            public const string DonorTheme = "DonorTheme";
            public const string DonorColor = "DonorColor";
            public const string DonorAuthKey = "DonorAuthKey";
            public const string SendStatistics = "SendStatistics";
            public const string Height = "Height";
            public const string Width = "Width";
            public const string DiscordBotAutoStart = "DiscordBotAutoStart";
            public const string MemoryWatchdog = "MemoryWatchdog"; // #16
        }

        public class ServerMetadata
        {
            public ServerStatus ServerStatus = ServerStatus.Stopped;
            public Process Process;
            public IntPtr MainWindow;
            public ServerConsole ServerConsole;

            // Basic Game Server Settings
            public bool AutoRestart;
            public bool AutoStart;
            public bool Maintenance; // #69
            public bool AutoUpdate;
            public bool UpdateOnStart;
            public bool BackupOnStart;

            // #16: RAM monitoring (per server)
            public bool MemoryWatchdog;
            public string MemoryLimitMB;
            public bool BackupBeforeUpdate; // #20
            public bool BackupCrontab;          // scheduled backup
            public string BackupCrontabFormat;

            // Discord Alert Settings
            public bool DiscordAlert;
            public string DiscordMessage;
            public string DiscordWebhook;
            public bool AutoRestartAlert;
            public bool AutoStartAlert;
            public bool AutoUpdateAlert;
            public bool RestartCrontabAlert;
            public bool CrashAlert;

            // Restart Crontab Settings
            public bool RestartCrontab;
            public string CrontabFormat;

            // Game Server Start Priority and Affinity
            public string CPUPriority;
            public string CPUAffinity;

            public bool EmbedConsole;
            public bool AutoScroll;
        }

        private enum WindowShowStyle : uint
        {
            Hide = 0,
            ShowNormal = 1,
            Show = 5,
            Minimize = 6,
            ShowMinNoActivate = 7
        }

        public enum ServerStatus
        {
            Started = 0,
            Starting = 1,
            Stopped = 2,
            Stopping = 3,
            Restarted = 4,
            Restarting = 5,
            Updated = 6,
            Updating = 7,
            Backuped = 8,
            Backuping = 9,
            Restored = 10,
            Restoring = 11,
            Deleting = 12
        }

        public static readonly string WGSM_VERSION = "v" + string.Concat(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString().Reverse().Skip(2).Reverse());
        public static readonly int MAX_SERVER = 50;
        public static readonly string WGSM_PATH = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
        public static readonly string DEFAULT_THEME = "Cyan";

        private readonly NotifyIcon notifyIcon;
        private Process Installer;

        public static readonly Dictionary<int, ServerMetadata> _serverMetadata = new Dictionary<int, ServerMetadata>();
        private ServerMetadata GetServerMetadata(object serverId) => _serverMetadata.TryGetValue(int.Parse(serverId.ToString()), out var s) ? s : null;

        public List<PluginMetadata> PluginsList = new List<PluginMetadata>();

        private readonly List<System.Windows.Controls.CheckBox> _checkBoxes = new List<System.Windows.Controls.CheckBox>();

        private string g_DonorType = string.Empty;

        private readonly DiscordBot.Bot g_DiscordBot = new DiscordBot.Bot();

        public MainWindow(bool showCrashHint)
        {
            //Add SplashScreen
            var splashScreen = new SplashScreen("Images/SplashScreen.png");
            splashScreen.Show(false, true);
            DiscordWebhook.SendErrorLog();

            InitializeComponent();
            Title = $"WindowsGSM {WGSM_VERSION}";

            // Plan B .NET 10: MahApps breaks window dragging (NRE get_CriticalHandle).
            // We handle the title bar drag ourselves (native WPF DragMove).
            // FluentWindow handles dragging natively (W11 title bar)
            this.Loaded += (s, e) => { ShowHomeMenu(0); RestoreSidebar(); };

            //Close SplashScreen
            splashScreen.Close(new TimeSpan(0, 0, 1));

            // Add all themes to comboBox_Themes
            new[] { "Cyan", "Blue", "Green", "Purple", "Red", "Orange" }.ToList().ForEach(name => comboBox_Themes.Items.Add(name));

            // Language selector (i18n). Tag carries the language code; label is the native name.
            foreach (var code in Functions.Localization.Loc.Supported)
            {
                var item = new System.Windows.Controls.ComboBoxItem { Content = Functions.Localization.Loc.DisplayNames[code], Tag = code };
                comboBox_Language.Items.Add(item);
                if (code == Functions.Localization.Loc.Lang) { comboBox_Language.SelectedItem = item; }
            }

            // Set up _serverMetadata
            for (int i = 0; i < MAX_SERVER; i++)
            {
                _serverMetadata[i] = new ServerMetadata
                {
                    ServerStatus = ServerStatus.Stopped,
                    ServerConsole = new ServerConsole(i)
                };
            }

            var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM");
            if (key == null)
            {
                key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\WindowsGSM");
                key.SetValue(RegistryKeyName.HardWareAcceleration, "True");
                key.SetValue(RegistryKeyName.UIAnimation, "True");
                key.SetValue(RegistryKeyName.DarkTheme, "False");
                key.SetValue(RegistryKeyName.StartOnBoot, "False");
                key.SetValue(RegistryKeyName.RestartOnCrash, "False");
                key.SetValue(RegistryKeyName.DonorTheme, "False");
                key.SetValue(RegistryKeyName.DonorColor, DEFAULT_THEME);
                key.SetValue(RegistryKeyName.DonorAuthKey, "");
                key.SetValue(RegistryKeyName.SendStatistics, "True");
                key.SetValue(RegistryKeyName.Height, Height);
                key.SetValue(RegistryKeyName.Width, Width);
                key.SetValue(RegistryKeyName.DiscordBotAutoStart, "False");
            }

            MahAppSwitch_HardWareAcceleration.IsChecked = (key.GetValue(RegistryKeyName.HardWareAcceleration) ?? true).ToString() == "True";
            MahAppSwitch_UIAnimation.IsChecked = (key.GetValue(RegistryKeyName.UIAnimation) ?? true).ToString() == "True";
            MahAppSwitch_DarkTheme.IsChecked = (key.GetValue(RegistryKeyName.DarkTheme) ?? true).ToString() == "True"; // dark by default (modern UI)
            MahAppSwitch_StartOnBoot.IsChecked = (key.GetValue(RegistryKeyName.StartOnBoot) ?? false).ToString() == "True";
            MahAppSwitch_RestartOnCrash.IsChecked = (key.GetValue(RegistryKeyName.RestartOnCrash) ?? false).ToString() == "True";
            MahAppSwitch_DonorConnect.Click -= DonorConnect_IsCheckedChanged;
            MahAppSwitch_DonorConnect.IsChecked = (key.GetValue(RegistryKeyName.DonorTheme) ?? false).ToString() == "True";
            MahAppSwitch_DonorConnect.Click += DonorConnect_IsCheckedChanged;
            MahAppSwitch_SendStatistics.IsChecked = (key.GetValue(RegistryKeyName.SendStatistics) ?? true).ToString() == "True";
            MahAppSwitch_DiscordBotAutoStart.IsChecked = (key.GetValue(RegistryKeyName.DiscordBotAutoStart) ?? false).ToString() == "True";
            string color = (key.GetValue(RegistryKeyName.DonorColor) ?? string.Empty).ToString();
            comboBox_Themes.SelectionChanged -= ComboBox_Themes_SelectionChanged;
            comboBox_Themes.SelectedItem = comboBox_Themes.Items.Contains(color) ? color : DEFAULT_THEME;
            comboBox_Themes.SelectionChanged += ComboBox_Themes_SelectionChanged;

            if (MahAppSwitch_DonorConnect.IsChecked == true)
            {
                string authKey = (key.GetValue(RegistryKeyName.DonorAuthKey) == null) ? string.Empty : key.GetValue(RegistryKeyName.DonorAuthKey).ToString();
                if (!string.IsNullOrWhiteSpace(authKey))
                {
#pragma warning disable 4014
                    AuthenticateDonor(authKey);
#pragma warning restore
                }
            }

            Height = (key.GetValue(RegistryKeyName.Height) == null) ? Height : double.Parse(key.GetValue(RegistryKeyName.Height).ToString());
            Width = (key.GetValue(RegistryKeyName.Width) == null) ? Width : double.Parse(key.GetValue(RegistryKeyName.Width).ToString());
            key.Close();

            RenderOptions.ProcessRenderMode = MahAppSwitch_HardWareAcceleration.IsChecked == true ? System.Windows.Interop.RenderMode.SoftwareOnly : System.Windows.Interop.RenderMode.Default;
            // WindowTransitionsEnabled removed (FluentWindow)
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(MahAppSwitch_DarkTheme.IsChecked == true ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
            //Not required - it is set in windows settings
            //SetStartOnBoot(MahAppSwitch_StartOnBoot.IsChecked ?? false);
            if (MahAppSwitch_DiscordBotAutoStart.IsChecked == true)
            {
                // The ToggleSwitch Click event does not fire on a programmatic IsChecked:
                // we start it explicitly (after the window has loaded).
                switch_DiscordBot.IsChecked = true;
                _ = Dispatcher.InvokeAsync(async () => await StartDiscordBotAsync());
            }

            // Add items to Set Affinity Flyout
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                StackPanel stackPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(15, 0, 0, 0)
                };

                _checkBoxes.Add(new System.Windows.Controls.CheckBox());
                _checkBoxes[i].Focusable = false;
                var label = new Label
                {
                    Content = $"CPU {i}",
                    Padding = new Thickness(0, 5, 0, 5)
                };

                stackPanel.Children.Add(_checkBoxes[i]);
                stackPanel.Children.Add(label);
                StackPanel_SetAffinity.Children.Add(stackPanel);
            }

            // Add click listener on each checkBox
            foreach (var checkBox in _checkBoxes)
            {
                checkBox.Click += (sender, e) =>
                {
                    var server = (ServerTable)ServerGrid.SelectedItem;
                    if (server == null) { return; }

                CheckPrioity:
                    string priority = string.Empty;
                    for (int i = _checkBoxes.Count - 1; i >= 0; i--)
                    {
                        priority += (_checkBoxes[i].IsChecked ?? false) ? "1" : "0";
                    }

                    if (!priority.Contains("1"))
                    {
                        checkBox.IsChecked = true;
                        goto CheckPrioity;
                    }

                    textBox_SetAffinity.Text = Functions.CPU.Affinity.GetAffinityValidatedString(priority);

                    _serverMetadata[int.Parse(server.ID)].CPUAffinity = priority;
                    ServerConfig.SetSetting(server.ID, "cpuaffinity", priority);

                    if (GetServerMetadata(server.ID).Process != null && !GetServerMetadata(server.ID).Process.HasExited)
                    {
                        _serverMetadata[int.Parse(server.ID)].Process.ProcessorAffinity = Functions.CPU.Affinity.GetAffinityIntPtr(priority);
                    }
                };
            }

            notifyIcon = new NotifyIcon
            {
                BalloonTipTitle = "WindowsGSM",
                BalloonTipText = "WindowsGSM is running in the background",
                Text = "WindowsGSM",
                BalloonTipIcon = ToolTipIcon.Info,
                Visible = true
            };

            using (var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Images/WindowsGSM-Icon.ico")).Stream)
            {
                notifyIcon.Icon = new System.Drawing.Icon(iconStream);
            }
            notifyIcon.BalloonTipClicked += OnBalloonTipClick;
            notifyIcon.MouseClick += NotifyIcon_MouseClick;

            ServerPath.CreateAndFixDirectories();

            // Compile plugins on background threads (shouldAwait: true -> Task.Run inside the Roslyn
            // emit) so the constructor is not blocked. With shouldAwait: false the emit ran synchronously
            // on the UI thread, so the window did not appear until every plugin had compiled (~seconds).
            // LoadPlugins refreshes the server table itself once plugins are ready (a plugin-backed
            // server is skipped by LoadServerTable while PluginsList is still empty).
            LoadPlugins(shouldAwait: true);
            AddGamesToComboBox();

            LoadServerTable();

            if (ServerGrid.Items.Count > 0)
            {
                ServerGrid.SelectedItem = ServerGrid.Items[0];
            }

            if (showCrashHint)
            {
                string logFile = $"CRASH_{DateTime.Now:yyyyMMdd}.log";
                Log("System", $"WindowsGSM crashed unexpectedly, please view the crash log {logFile}");
            }

            // Re-attach to already-running server processes + auto-start now run in ReattachAndAutoStart(),
            // called once at the end of LoadPlugins. They must run AFTER the plugins have compiled, because
            // plugin-backed servers only appear in the grid once their plugin is loaded (LoadServerTable
            // skips a server whose game class is not yet available). Running it here (grid still empty while
            // plugins compile in the background) would leave every plugin-backed server shown as stopped.

            if (MahAppSwitch_SendStatistics.IsChecked == true)
            {
                SendGoogleAnalytics();
            }

            StartConsoleRefresh();

            StartServerTableRefresh();

            StartPlayerCountRefresh(); // online players (A2S)

            // The dashboard tiles are bound to a dedicated collection (same instances as the grid).
            try { dashboard_tiles.ItemsSource = _dashboardTiles; } catch { }

            StartDashBoardRefresh();

            // P1-1: check "update available" in the background (does not block startup)
            _ = CheckServerUpdatesAsync();

            // P3-7: per-server disk space monitoring
            StartDiskSpaceCheck();

            // #16: memory monitoring (auto-restart if enabled in Settings)
            StartMemoryWatchdog();

            // #19: auto-cleanup of old logs/backups
            StartDiskCleanup();

            // Per-server scheduled backup
            StartBackupCrontabCheck();

            // #207/#25: remote-control web API (opt-in, token required)
            StartWebApi();

            // Per-server uptime tracking
            StartUptimeTracking();
        }

        // ===== Per-server uptime stats =====
        private readonly Dictionary<string, UptimeStats> _uptimeStats = new Dictionary<string, UptimeStats>();
        private readonly Dictionary<string, bool> _uptimeWasOnline = new Dictionary<string, bool>();

        private UptimeStats GetUptime(string id)
        {
            if (!_uptimeStats.TryGetValue(id, out var s)) { s = UptimeStats.Load(id); _uptimeStats[id] = s; }
            return s;
        }

        private async void StartUptimeTracking()
        {
            while (true)
            {
                await Task.Delay(60 * 1000); // accumulate per minute
                try
                {
                    foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
                    {
                        try
                        {
                            var meta = GetServerMetadata(server.ID);
                            bool online = meta != null && meta.ServerStatus == ServerStatus.Started;
                            var st = GetUptime(server.ID);
                            bool was = _uptimeWasOnline.TryGetValue(server.ID, out var w) && w;
                            if (online && !was) { st.Starts++; }   // transition -> new start
                            if (online) { st.OnlineSeconds += 60; }
                            _uptimeWasOnline[server.ID] = online;
                            st.Save(server.ID);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        // ===== Scheduled backup (per server): at the set time, stop -> backup -> restart =====
        private readonly Dictionary<string, DateTime> _lastBackupSchedCheck = new Dictionary<string, DateTime>();
        private async void StartBackupCrontabCheck()
        {
            while (true)
            {
                await Task.Delay(60 * 1000); // check every minute
                try
                {
                    foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
                    {
                        try
                        {
                            var meta = GetServerMetadata(server.ID);
                            if (meta == null || !meta.BackupCrontab) { _lastBackupSchedCheck[server.ID] = DateTime.Now; continue; }
                            var sched = NCrontab.CrontabSchedule.TryParse(meta.BackupCrontabFormat);
                            if (sched == null) { continue; }
                            DateTime last = _lastBackupSchedCheck.TryGetValue(server.ID, out var lc) ? lc : DateTime.Now;
                            DateTime next = sched.GetNextOccurrence(last);
                            _lastBackupSchedCheck[server.ID] = DateTime.Now;
                            if (next <= DateTime.Now)
                            {
                                Log(server.ID, $"[Scheduled backup] #{server.ID} {server.Name}…");
                                bool wasRunning = meta.ServerStatus == ServerStatus.Started;
                                try
                                {
                                    if (wasRunning) { await GameServer_Stop(server); }
                                    await GameServer_Backup(server, " | Scheduled backup");
                                }
                                catch (Exception ex) { Log(server.ID, "[Scheduled backup] Error: " + ex.Message); }
                                finally
                                {
                                    // restart if the server was running before (safeguard: always try to restart)
                                    if (wasRunning && GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped)
                                    {
                                        try
                                        {
                                            var gs = await Server_BeginStart(server);
                                            if (gs != null)
                                            {
                                                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                                                SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());
                                                Log(server.ID, "[Scheduled backup] Server restarted.");
                                            }
                                        }
                                        catch (Exception ex) { Log(server.ID, "[Scheduled backup] Restart failed: " + ex.Message); }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        // ===== #19: disk auto-cleanup (logs + backups older than X days) =====
        private const int CLEANUP_RETENTION_DAYS = 30;
        private async void StartDiskCleanup()
        {
            while (true)
            {
                try
                {
                    var cutoff = DateTime.Now.AddDays(-CLEANUP_RETENTION_DAYS);
                    int removed = 0;

                    void CleanDir(string dir)
                    {
                        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { return; }
                        foreach (var f in Directory.GetFiles(dir))
                        {
                            try { if (File.GetLastWriteTime(f) < cutoff) { File.Delete(f); removed++; } } catch { }
                        }
                    }

                    await Task.Run(() =>
                    {
                        CleanDir(ServerPath.GetLogs());                       // WindowsGSM logs
                        for (int i = 1; i <= MAX_SERVER; i++)
                        {
                            CleanDir(ServerPath.GetBackups(i.ToString()));    // per-server backups
                        }
                    });

                    if (removed > 0) { Log("System", $"[Auto-clean] {removed} file(s) older than {CLEANUP_RETENTION_DAYS} days deleted (logs/backups)."); }
                }
                catch { }
                await Task.Delay(24 * 60 * 60 * 1000); // once/day
            }
        }

        // ===== #16: PER-SERVER RAM monitoring (auto-restart), toggleable + MB threshold in the server panel =====
        private readonly Dictionary<string, int> _ramOverCount = new Dictionary<string, int>();

        private async void StartMemoryWatchdog()
        {
            while (true)
            {
                await Task.Delay(2 * 60 * 1000); // check every 2 min
                try
                {
                    foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
                    {
                        try
                        {
                            var meta = GetServerMetadata(server.ID);
                            if (meta == null || !meta.MemoryWatchdog || meta.ServerStatus != ServerStatus.Started) { _ramOverCount.Remove(server.ID); continue; }
                            var p = meta.Process;
                            if (p == null || p.HasExited) { _ramOverCount.Remove(server.ID); continue; }

                            long limitMb = (long.TryParse(meta.MemoryLimitMB, out long lm) && lm >= 256) ? lm : 8000;
                            long mb = p.WorkingSet64 / (1024 * 1024);
                            if (mb > limitMb)
                            {
                                int c = (_ramOverCount.TryGetValue(server.ID, out var v) ? v : 0) + 1;
                                _ramOverCount[server.ID] = c;
                                if (c >= 3) // ~6 min above the threshold before acting
                                {
                                    _ramOverCount[server.ID] = 0;
                                    Log(server.ID, $"[RAM Watchdog] {server.Name}: {mb} MB (> {limitMb} MB) sustained -> clean restart.");
                                    if (meta.DiscordAlert)
                                    {
                                        try { var wh = new DiscordWebhook(meta.DiscordWebhook, meta.DiscordMessage, g_DonorType); await wh.Send(server.ID, server.Game, $"⚠️ High RAM ({mb} MB) → restart", server.Name, server.IP, server.Port); } catch { }
                                    }
                                    await GameServer_Restart(server);
                                }
                            }
                            else { _ramOverCount.Remove(server.ID); }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        // ===== P3-7: per-server low disk space alert (log + Discord) =====
        private const double DISK_ALERT_THRESHOLD_GB = 5.0; // default threshold (GB) - configurable later
        private readonly HashSet<string> _diskAlerted = new HashSet<string>();

        private async void StartDiskSpaceCheck()
        {
            while (true)
            {
                try
                {
                    foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
                    {
                        try
                        {
                            string root = Path.GetPathRoot(ServerPath.GetServersServerFiles(server.ID));
                            if (string.IsNullOrEmpty(root)) { continue; }
                            var drive = new DriveInfo(root);
                            if (!drive.IsReady) { continue; }
                            double freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                            if (freeGb < DISK_ALERT_THRESHOLD_GB)
                            {
                                if (_diskAlerted.Add(server.ID)) // first detection of this episode -> avoids spam
                                {
                                    Log(server.ID, $"[ALERT] Low disk space on {drive.Name}: {freeGb:0.0} GB free (threshold {DISK_ALERT_THRESHOLD_GB} GB).");
                                    if (GetServerMetadata(server.ID) != null && GetServerMetadata(server.ID).DiscordAlert)
                                    {
                                        var webhook = new DiscordWebhook(GetServerMetadata(server.ID).DiscordWebhook, GetServerMetadata(server.ID).DiscordMessage, g_DonorType);
                                        await webhook.Send(server.ID, server.Game, $"⚠️ Low disk ({freeGb:0.0} GB)", server.Name, server.IP, server.Port);
                                    }
                                }
                            }
                            else
                            {
                                _diskAlerted.Remove(server.ID); // space recovered -> can re-alert later
                            }
                        }
                        catch { }
                    }
                }
                catch { }
                await Task.Delay(15 * 60 * 1000); // every 15 min
            }
        }

        // ===== P1-1: "Update available" badge per Steam server =====
        // Compares the installed build (appmanifest .acf) to the public build (SteamCMD app_info_print).
        // Read-only, in the background, remote build cached ~10 min (1 SteamCMD call/AppID).
        private readonly Dictionary<string, string> _remoteBuildCache = new Dictionary<string, string>();
        // Persists the badge per server (survives periodic LoadServerTable calls that recreate the rows)
        private readonly Dictionary<string, string> _updateTooltips = new Dictionary<string, string>();
        private DateTime _remoteBuildCacheTime = DateTime.MinValue;
        private bool _updateCheckRunning = false;
        // #13: anti-spam for the "update available" alert (1 Discord alert per update episode)
        private readonly HashSet<string> _updateAlerted = new HashSet<string>();

        public async Task CheckServerUpdatesAsync()
        {
            if (_updateCheckRunning) { return; }
            _updateCheckRunning = true;
            try
            {
                // Remote cache expired (>10 min) -> start fresh
                if ((DateTime.Now - _remoteBuildCacheTime).TotalMinutes > 10)
                {
                    _remoteBuildCache.Clear();
                    _remoteBuildCacheTime = DateTime.Now;
                }

                var rows = ServerGrid.Items.Cast<ServerTable>().ToList();
                foreach (var row in rows)
                {
                    bool available = false;
                    string tooltip = null;
                    try
                    {
                        dynamic gameServer = GameServer.Data.Class.Get(row.Game, new ServerConfig(row.ID), PluginsList);
                        if (gameServer != null)
                        {
                            // AppId / GetLocalBuild / GetRemoteBuild only exist on SteamCMD games.
                            string appId = null;
                            try { appId = (string)gameServer.AppId; } catch { appId = null; }
                            if (!string.IsNullOrEmpty(appId))
                            {
                                string local = string.Empty;
                                try { local = gameServer.GetLocalBuild(); } catch { local = string.Empty; }
                                if (!string.IsNullOrEmpty(local)) // server actually installed
                                {
                                    string remote;
                                    if (!_remoteBuildCache.TryGetValue(appId, out remote))
                                    {
                                        try { remote = await gameServer.GetRemoteBuild(); } catch { remote = string.Empty; }
                                        if (!string.IsNullOrEmpty(remote)) { _remoteBuildCache[appId] = remote; }
                                    }
                                    if (!string.IsNullOrEmpty(remote) && local != remote)
                                    {
                                        available = true;
                                        tooltip = $"Update available: {local} → {remote}";
                                        // #13: alert (log + Discord) on first detection only
                                        if (_updateAlerted.Add(row.ID))
                                        {
                                            Log("UpdateCheck", $"#{row.ID} {row.Name}: update available {local} -> {remote}");
                                            if (GetServerMetadata(row.ID) != null && GetServerMetadata(row.ID).DiscordAlert)
                                            {
                                                try
                                                {
                                                    var webhook = new DiscordWebhook(GetServerMetadata(row.ID).DiscordWebhook, GetServerMetadata(row.ID).DiscordMessage, g_DonorType);
                                                    await webhook.Send(row.ID, row.Game, $"🔔 Update available ({local} → {remote})", row.Name, row.IP, row.Port);
                                                }
                                                catch { }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { /* non-Steam game or error -> no badge */ }

                    // Persists (survives LoadServerTable rebuilds) + updates the LIVE row (not the captured instance,
                    // which may have been replaced by a periodic LoadServerTable during the SteamCMD call).
                    string rowId = row.ID;
                    if (available) { _updateTooltips[rowId] = tooltip; } else { _updateTooltips.Remove(rowId); _updateAlerted.Remove(rowId); }
                    bool fAvailable = available; string fTooltip = tooltip;
                    Dispatcher.Invoke(() =>
                    {
                        var live = ServerGrid.Items.Cast<ServerTable>().FirstOrDefault(x => x.ID == rowId);
                        if (live != null) { live.UpdateAvailable = fAvailable; live.UpdateTooltip = fTooltip; }
                    });
                }
            }
            finally { _updateCheckRunning = false; }
        }

        private Process GetConsoleProcess(int processId)
        {
            try
            {
                ManagementObjectSearcher mos = new ManagementObjectSearcher($"Select * From Win32_Process Where ParentProcessID={processId}");
                foreach (ManagementObject mo in mos.Get())
                {
                    Process p = Process.GetProcessById(Convert.ToInt32(mo["ProcessID"]));
                    if (Equals(p, "conhost"))
                    {
                        return p;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        // Read console redirect output - not tested
        private async void ReadConsoleOutput(string serverId, Process p)
        {
            await Task.Run(() =>
            {
                var reader = p.StandardOutput;
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        GetServerMetadata(serverId).ServerConsole.Add(line);
                    });
                }
            });
        }

        public void AddGamesToComboBox()
        {
            comboBox_InstallGameServer.Items.Clear();
            comboBox_ImportGameServer.Items.Clear();

            //Add games to ComboBox
            SortedList sortedList = new SortedList();
            List<DictionaryEntry> gameName = GameServer.Data.Icon.ResourceManager.GetResourceSet(System.Globalization.CultureInfo.CurrentUICulture, true, true).Cast<DictionaryEntry>().ToList();
            // #74/#71: use the indexer (not .Add) to avoid throwing on a duplicate key (a plugin whose FullName
            // collides with a native game or another plugin) -> no more duplicates or list crash.
            gameName.ForEach(delegate (DictionaryEntry entry) { sortedList[entry.Key] = $"/WindowsGSM;component/{entry.Value}"; });
            PluginsList.ForEach(delegate (PluginMetadata plugin)
            {
                if (plugin.IsLoaded)
                {
                    sortedList[plugin.FullName] = plugin.GameImage == PluginManagement.DefaultPluginImage ? plugin.GameImage.Replace("pack://application:,,,", "/WindowsGSM;component") : plugin.GameImage;
                }
            });

            label_GameServerCount.Content = $"{sortedList.Count} game servers supported"; // deduplicated

            for (int i = 0; i < sortedList.Count; i++)
            {
                var row = new Images.Row
                {
                    Image = sortedList.GetByIndex(i).ToString(),
                    Name = sortedList.GetKey(i).ToString()
                };

                comboBox_InstallGameServer.Items.Add(row);
                comboBox_ImportGameServer.Items.Add(row);
            }
        }

        public async void LoadPlugins(bool shouldAwait = true)
        {
            // Show the loading indicator while plugins compile (Roslyn) in the background.
            if (LoadingOverlay_Servers != null) { LoadingOverlay_Servers.Visibility = Visibility.Visible; }
            if (ProgressRing_LoadPlugins != null) { ProgressRing_LoadPlugins.Visibility = Visibility.Visible; }

            var pm = new PluginManagement();
            PluginsList = await pm.LoadPlugins(shouldAwait);

            int loadedCount = 0;
            PluginsList.ForEach(delegate (PluginMetadata plugin)
            {
                if (!plugin.IsLoaded)
                {
                    Directory.CreateDirectory(ServerPath.GetLogs(ServerPath.FolderName.Plugins));
                    string logFile = ServerPath.GetLogs(ServerPath.FolderName.Plugins, $"{plugin.FileName}.log");
                    File.WriteAllText(ServerPath.GetLogs(logFile), plugin.Error);
                    Log("Plugins", $"{plugin.FileName} fail to load. Please view the log: {logFile.Replace(WGSM_PATH, string.Empty)}");
                }
                else
                {
                    loadedCount++;
                    var converter = new BrushConverter();
                    Brush brush;
                    try
                    {
                        brush = (Brush)converter.ConvertFromString(plugin.Plugin.color);
                    }
                    catch
                    {
                        brush = Brushes.DimGray;
                    }

                    var borderBase = new Border
                    {
                        BorderBrush = brush,
                        Background = Brushes.SlateGray,
                        BorderThickness = new Thickness(1.5),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(6),
                        Margin = new Thickness(10, 0, 10, 10)
                    };
                    DockPanel.SetDock(borderBase, Dock.Top);
                    var dockPanelBase = new DockPanel();
                    var gameImage = new Border
                    {
                        BorderBrush = Brushes.White,
                        Background = new ImageBrush
                        {
                            Stretch = Stretch.Fill,
                            ImageSource = plugin.GameImage == PluginManagement.DefaultPluginImage ? PluginManagement.GetDefaultPluginBitmapSource() : new BitmapImage(new Uri(plugin.GameImage))
                        },
                        BorderThickness = new Thickness(0),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(10),
                        Width = 63,
                        Height = 63,
                        MinWidth = 63,
                        MinHeight = 63,
                        Margin = new Thickness(0, 0, 10, 0)
                    };
                    dockPanelBase.Children.Add(gameImage);

                    var dockPanel = new DockPanel { Margin = new Thickness(0, 0, 3, 0) };
                    DockPanel.SetDock(dockPanel, Dock.Top);
                    var label = new Label { Content = $"v{plugin.Plugin.version}", Padding = new Thickness(0) };
                    DockPanel.SetDock(label, Dock.Right);
                    dockPanel.Children.Add(label);
                    label = new Label { Content = plugin.Plugin.name.Split('.')[1], Padding = new Thickness(0), FontSize = 14, FontWeight = FontWeights.Bold };
                    DockPanel.SetDock(label, Dock.Left);
                    dockPanel.Children.Add(label);
                    dockPanelBase.Children.Add(dockPanel);

                    var textBlock = new TextBlock { Text = plugin.Plugin.description };
                    DockPanel.SetDock(textBlock, Dock.Top);
                    dockPanelBase.Children.Add(textBlock);

                    var stackPanelBase = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
                    var authorImage = new Border
                    {
                        Background = new ImageBrush
                        {
                            Stretch = Stretch.Fill,
                            ImageSource = plugin.AuthorImage == PluginManagement.DefaultUserImage ? PluginManagement.GetDefaultUserBitmapSource() : new BitmapImage(new Uri(plugin.AuthorImage))
                        },
                        BorderThickness = new Thickness(0),
                        CornerRadius = new CornerRadius(30),
                        Padding = new Thickness(10),
                        Width = 25,
                        Height = 25,
                        Margin = new Thickness(0, 0, 5, 0)
                    };
                    stackPanelBase.Children.Add(authorImage);
                    var stackPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    label = new Label { Content = plugin.Plugin.author, Padding = new Thickness(0) };
                    DockPanel.SetDock(label, Dock.Top);
                    stackPanel.Children.Add(label);
                    label = new Label { Content = "•", Padding = new Thickness(0), Margin = new Thickness(5, 0, 5, 0) };
                    DockPanel.SetDock(label, Dock.Top);
                    stackPanel.Children.Add(label);
                    textBlock = new TextBlock();
                    var hyperlink = new Hyperlink(new Run(plugin.Plugin.url)) { Foreground = brush };
                    try
                    {
                        hyperlink.NavigateUri = new Uri(plugin.Plugin.url);
                        hyperlink.RequestNavigate += Hyperlink_RequestNavigate;
                    }
                    catch { }

                    textBlock.Inlines.Add(hyperlink);
                    stackPanel.Children.Add(textBlock);
                    stackPanelBase.Children.Add(stackPanel);
                    dockPanelBase.Children.Add(stackPanelBase);

                    borderBase.Child = dockPanelBase;
                    StackPanel_PluginList.Children.Add(borderBase);
                }
            });

            AddGamesToComboBox();

            Label_PluginInstalled.Content = PluginsList.Count.ToString();
            Label_PluginLoaded.Content = loadedCount.ToString();
            Label_PluginFailed.Content = (PluginsList.Count - loadedCount).ToString();

            Log("Plugins", $"Installed: {PluginsList.Count}, Loaded: {loadedCount}, Failed: {PluginsList.Count - loadedCount}");

            // Plugins now compile in the background, so the initial LoadServerTable() may have run
            // before PluginsList was populated (plugin-backed servers get skipped when the plugin is
            // not yet loaded). Refresh the grid now that plugins are ready, and select a row if none is.
            LoadServerTable();
            if (ServerGrid.SelectedItem == null && ServerGrid.Items.Count > 0)
            {
                ServerGrid.SelectedItem = ServerGrid.Items[0];
            }

            // Now that the grid is populated (plugin-backed servers included), re-attach to running
            // processes and honour auto-start. Guarded so it only happens on the first load, not on
            // every manual plugin refresh/import.
            ReattachAndAutoStart();

            // Plugins are ready and the grid is populated -> hide the loading indicator.
            if (LoadingOverlay_Servers != null) { LoadingOverlay_Servers.Visibility = Visibility.Collapsed; }
            if (ProgressRing_LoadPlugins != null) { ProgressRing_LoadPlugins.Visibility = Visibility.Collapsed; }
        }

        private bool _serversInitialized;

        /// <summary>
        /// Re-attach to server processes that are still running from a previous WindowsGSM session
        /// (so their status shows "Started" in the grid AND in the Discord bot / web portal, instead of
        /// a greyed-out "stopped" while the game server is actually up), then run auto-start. Runs once.
        /// </summary>
        private void ReattachAndAutoStart()
        {
            if (_serversInitialized) { return; }
            _serversInitialized = true;

            foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                int pid = ServerCache.GetPID(server.ID);
                if (pid == -1) { continue; }

                Process p = null;
                try { p = Process.GetProcessById(pid); }
                catch { continue; }

                // Guard against PID reuse: the cached process name must still match.
                string pName = ServerCache.GetProcessName(server.ID);
                if (string.IsNullOrWhiteSpace(pName) || p.ProcessName != pName) { continue; }

                _serverMetadata[int.Parse(server.ID)].Process = p;
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                SetServerStatus(server, "Started");
                _serverMetadata[int.Parse(server.ID)].MainWindow = ServerCache.GetWindowsIntPtr(server.ID);
                // A process obtained via GetProcessById does not raise Exited unless we opt in. Without
                // this, a crash of a re-attached server would leave a stale "Started" status in the grid
                // and the Discord bot. Best-effort: may throw for a process started by an elevated session.
                try { p.EnableRaisingEvents = true; } catch { }
                p.Exited += (sender, e) => OnGameServerExited(server);

                StartAutoUpdateCheck(server);
                StartRestartCrontabCheck(server);
                StartSendHeartBeat(server);
                StartQuery(server);
            }

            AutoStartServer();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Shell.Open(e.Uri.AbsoluteUri);
        }

        private async void ImportPlugin_Click(object sender, RoutedEventArgs e)
        {
            // If a server is installing or import => return
            if (progressbar_InstallProgress.IsIndeterminate || progressbar_ImportProgress.IsIndeterminate)
            {
                MessageBox.Show(Loc.T("Msg.InstallingBusy"), Loc.T("Msg.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string pluginsDir = ServerPath.FolderName.Plugins;

            System.Windows.Forms.OpenFileDialog ofd = new System.Windows.Forms.OpenFileDialog();
            ofd.Filter = "zip files (*.zip)|*.zip";
            ofd.InitialDirectory = pluginsDir;

            DialogResult dr = ofd.ShowDialog();
            if (dr == System.Windows.Forms.DialogResult.OK)
            {
                Button_ImportPlugin.IsEnabled = false;
                ProgressRing_LoadPlugins.Visibility = Visibility.Visible;
                Label_PluginInstalled.Content = "-";
                Label_PluginLoaded.Content = "-";
                Label_PluginFailed.Content = "-";
                StackPanel_PluginList.Children.Clear();

                /// This is relying on it keeps the naming shceme of the ZIP files that're downloaed from GitHub releases. Like WindowsGSM.Spigot-1.0.zip,
                /// Just by following WindowsGSM naming of plugins, and this will be fine!
                string zipPath = ofd.FileName;
                string dirName = ofd.SafeFileName.Split('.')[1].Split('-')[0] + ".cs";
                string knownPattern = ".cs";
                // Unziping plugin
                using (ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
                {
                    var result = from entry in zip.Entries
                                 where Path.GetDirectoryName(entry.FullName).Contains(knownPattern)
                                 where !String.IsNullOrEmpty(entry.Name)
                                 select entry;

                    Directory.CreateDirectory(Path.Combine(pluginsDir, dirName));
                    foreach (ZipArchiveEntry entryFile in result)
                    {
                        entryFile.ExtractToFile(Path.Combine(pluginsDir, dirName, entryFile.Name), true);
                    }
                }

                await Task.Delay(500);
                LoadPlugins();
                LoadServerTable();

                Button_ImportPlugin.IsEnabled = true;
                ProgressRing_LoadPlugins.Visibility = Visibility.Collapsed;
            }
        }

        private async void RefreshPlugins_Click(object sender, RoutedEventArgs e)
        {
            // If a server is installing or import => return
            if (progressbar_InstallProgress.IsIndeterminate || progressbar_ImportProgress.IsIndeterminate)
            {
                MessageBox.Show(Loc.T("Msg.InstallingBusy"), Loc.T("Msg.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Button_RefreshPlugins.IsEnabled = false;
            ProgressRing_LoadPlugins.Visibility = Visibility.Visible;
            Label_PluginInstalled.Content = "-";
            Label_PluginLoaded.Content = "-";
            Label_PluginFailed.Content = "-";
            StackPanel_PluginList.Children.Clear();

            await Task.Delay(500);
            LoadPlugins();
            LoadServerTable();

            Button_RefreshPlugins.IsEnabled = true;
            ProgressRing_LoadPlugins.Visibility = Visibility.Collapsed;
        }

        // Plugin creation assistant: generates a Steam plugin .cs, then reloads so it compiles + shows up.
        private void CreatePlugin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Functions.PluginGeneratorDialog(() => { LoadPlugins(); LoadServerTable(); }) { Owner = this };
                dlg.ShowDialog();
            }
            catch (Exception ex) { Functions.AppLog.Warn("PluginGen/UI", ex.Message); }
        }

        // Dedicated source for the dashboard tiles: same ServerTable instances as the grid
        // (so Sample()/Players notifications propagate to the tiles).
        private readonly System.Collections.ObjectModel.ObservableCollection<ServerTable> _dashboardTiles
            = new System.Collections.ObjectModel.ObservableCollection<ServerTable>();

        public void LoadServerTable()
        {
            string[] livePlayerData = new string[MAX_SERVER + 1];
            foreach (ServerTable item in ServerGrid.Items)
            {
                livePlayerData[int.Parse(item.ID)] = item.Maxplayers;
            }

            var selectedrow = (ServerTable)ServerGrid.SelectedItem;
            ServerGrid.Items.Clear();
            _dashboardTiles.Clear();

            //Add server to datagrid
            for (int i = 1; i <= MAX_SERVER; i++)
            {
                string serverid_path = Path.Combine(WGSM_PATH, "servers", i.ToString());
                if (!Directory.Exists(serverid_path)) { continue; }

                string configpath = ServerPath.GetServersConfigs(i.ToString(), "WindowsGSM.cfg");
                if (!File.Exists(configpath)) { continue; }

                var serverConfig = new ServerConfig(i.ToString());

                //If Game server not exist return
                if (GameServer.Data.Class.Get(serverConfig.ServerGame, pluginList: PluginsList) == null) { continue; }

                string status;
                switch (GetServerMetadata(i).ServerStatus)
                {
                    case ServerStatus.Started: status = "Started"; break;
                    case ServerStatus.Starting: status = "Starting"; break;
                    case ServerStatus.Stopped: status = "Stopped"; break;
                    case ServerStatus.Stopping: status = "Stopping"; break;
                    case ServerStatus.Restarted: status = "Restarted"; break;
                    case ServerStatus.Restarting: status = "Restarting"; break;
                    case ServerStatus.Updated: status = "Updated"; break;
                    case ServerStatus.Updating: status = "Updating"; break;
                    case ServerStatus.Backuped: status = "Backuped"; break;
                    case ServerStatus.Backuping: status = "Backuping"; break;
                    case ServerStatus.Restored: status = "Restored"; break;
                    case ServerStatus.Restoring: status = "Restoring"; break;
                    case ServerStatus.Deleting: status = "Deleting"; break;
                    default:
                        {
                            _serverMetadata[i].ServerStatus = ServerStatus.Stopped;
                            status = "Stopped";
                            break;
                        }
                }

                try
                {
                    string icon = GameServer.Data.Icon.ResourceManager.GetString(serverConfig.ServerGame);
                    if (icon == null)
                    {
                        PluginsList.ForEach(delegate (PluginMetadata plugin)
                        {
                            if (plugin.FullName == serverConfig.ServerGame && plugin.IsLoaded)
                            {
                                icon = plugin.GameImage == PluginManagement.DefaultPluginImage
                                    ? plugin.GameImage.Replace("pack://application:,,,", "/WindowsGSM;component")
                                    : plugin.GameImage;
                            }
                        });
                    }
                    if (icon == null)
                    {
                        icon = PluginManagement.DefaultPluginImage.Replace("pack://application:,,,", "/WindowsGSM;component");
                    }

                    string serverId = i.ToString();
                    string pidString = string.Empty;

                    try
                    {
                        using var proc = Process.GetProcessById(ServerCache.GetPID(serverId));
                        pidString = proc.Id.ToString();
                    }
                    catch { }

                    var server = new ServerTable
                    {
                        ID = i.ToString(),
                        PID = pidString,
                        Game = serverConfig.ServerGame,
                        Icon = icon,
                        Status = status,
                        Name = serverConfig.ServerName,
                        IP = serverConfig.ServerIP,
                        Port = serverConfig.ServerPort,
                        QueryPort = serverConfig.ServerQueryPort,
                        Defaultmap = serverConfig.ServerMap,
                        Maxplayers = (GetServerMetadata(i).ServerStatus != ServerStatus.Started) ? serverConfig.ServerMaxPlayer : livePlayerData[i],
                        // P1-1: restore the update badge (computed in the background) after the row is recreated
                        UpdateAvailable = _updateTooltips.ContainsKey(i.ToString()),
                        UpdateTooltip = _updateTooltips.TryGetValue(i.ToString(), out var _tt) ? _tt : null
                    };

                    SaveServerConfigToServerMetadata(i, serverConfig);
                    ServerGrid.Items.Add(server);
                    _dashboardTiles.Add(server);

                    if (selectedrow != null)
                    {
                        if (selectedrow.ID == server.ID)
                        {
                            ServerGrid.SelectedItem = server;
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }

            grid_action.Visibility = (ServerGrid.Items.Count != 0) ? Visibility.Visible : Visibility.Hidden;
            label_select.Visibility = grid_action.Visibility == Visibility.Hidden ? Visibility.Visible : Visibility.Hidden;

            ApplyServerFilter(); // P1-2: re-applies the search filter after the rows are recreated
            DetectPortConflicts(); // #15: flags ports shared between servers
        }

        // #15: flags servers that share the same Port or Query Port.
        private void DetectPortConflicts()
        {
            try
            {
                var rows = ServerGrid.Items.Cast<ServerTable>().ToList();
                var byPort = new Dictionary<string, List<ServerTable>>();
                void Add(string port, ServerTable s)
                {
                    if (string.IsNullOrWhiteSpace(port) || port == "0") { return; }
                    if (!byPort.TryGetValue(port, out var l)) { l = new List<ServerTable>(); byPort[port] = l; }
                    if (!l.Contains(s)) { l.Add(s); }
                }
                foreach (var s in rows) { Add(s.Port, s); Add(s.QueryPort, s); }

                foreach (var s in rows)
                {
                    var conflicts = new List<string>();
                    foreach (var p in new[] { s.Port, s.QueryPort })
                    {
                        if (!string.IsNullOrWhiteSpace(p) && p != "0" && byPort.TryGetValue(p, out var l) && l.Count > 1)
                        {
                            var others = l.Where(x => x != s).Select(x => "#" + x.ID).Distinct();
                            conflicts.Add($"port {p} shared with {string.Join(", ", others)}");
                        }
                    }
                    s.PortConflict = conflicts.Count > 0;
                    s.PortConflictTooltip = conflicts.Count > 0 ? "⚠ Conflict: " + string.Join(" ; ", conflicts.Distinct()) : null;
                }
            }
            catch { }
        }

        private void SaveServerConfigToServerMetadata(object serverId, ServerConfig serverConfig)
        {
            int i = int.Parse(serverId.ToString());

            // Basic Game Server Settings
            _serverMetadata[i].AutoRestart = serverConfig.AutoRestart;
            _serverMetadata[i].Maintenance = serverConfig.Maintenance; // #69
            _serverMetadata[i].AutoStart = serverConfig.AutoStart;
            _serverMetadata[i].AutoUpdate = serverConfig.AutoUpdate;
            _serverMetadata[i].UpdateOnStart = serverConfig.UpdateOnStart;
            _serverMetadata[i].BackupOnStart = serverConfig.BackupOnStart;

            // Discord Alert Settings
            _serverMetadata[i].DiscordAlert = serverConfig.DiscordAlert;
            _serverMetadata[i].DiscordMessage = serverConfig.DiscordMessage;
            _serverMetadata[i].DiscordWebhook = serverConfig.DiscordWebhook;
            _serverMetadata[i].AutoRestartAlert = serverConfig.AutoRestartAlert;
            _serverMetadata[i].AutoStartAlert = serverConfig.AutoStartAlert;
            _serverMetadata[i].AutoUpdateAlert = serverConfig.AutoUpdateAlert;
            _serverMetadata[i].RestartCrontabAlert = serverConfig.RestartCrontabAlert;
            _serverMetadata[i].CrashAlert = serverConfig.CrashAlert;

            // Restart Crontab Settings
            _serverMetadata[i].RestartCrontab = serverConfig.RestartCrontab;
            _serverMetadata[i].CrontabFormat = serverConfig.CrontabFormat;

            // Game Server Start Priority and Affinity
            _serverMetadata[i].CPUPriority = serverConfig.CPUPriority;
            _serverMetadata[i].CPUAffinity = serverConfig.CPUAffinity;

            _serverMetadata[i].EmbedConsole = serverConfig.EmbedConsole;
            _serverMetadata[i].AutoScroll = serverConfig.AutoScroll;
            _serverMetadata[i].MemoryWatchdog = serverConfig.MemoryWatchdog;
            _serverMetadata[i].MemoryLimitMB = serverConfig.MemoryLimitMB;
            _serverMetadata[i].BackupBeforeUpdate = serverConfig.BackupBeforeUpdate;
            _serverMetadata[i].BackupCrontab = serverConfig.BackupCrontab;
            _serverMetadata[i].BackupCrontabFormat = serverConfig.BackupCrontabFormat;
        }

        private async void AutoStartServer()
        {
            foreach (ServerTable server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                int serverId = int.Parse(server.ID);

                if (GetServerMetadata(serverId).Maintenance) { continue; } // #69: do not auto-start a server in maintenance
                if (GetServerMetadata(serverId).AutoStart && GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped)
                {
                    await GameServer_Start(server, " | Auto Start");

                    if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                    {
                        if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).AutoStartAlert)
                        {
                            var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage, g_DonorType);
                            await webhook.Send(server.ID, server.Game, "Started | Auto Start", server.Name, server.IP, server.Port);
                        }
                    }
                }
            }
        }

        private async void StartServerTableRefresh()
        {
            while (true)
            {
                await Task.Delay(5 * 1000); // P2-3: 5s for "live" CPU/RAM/uptime stats
                try
                {
                    // Samples CPU/RAM once per tick (feeds grid + dashboard tiles).
                    foreach (ServerTable s in ServerGrid.Items.Cast<ServerTable>().ToList())
                    {
                        s.Sample();
                    }
                    // No ServerGrid.Items.Refresh() here: Sample() raises INotifyPropertyChanged for every
                    // value it changes (Cpu/Ram/Uptime/histories/Online), so the bound cells update on their
                    // own. Refreshing the whole grid every 5s rebuilt every row container for nothing.

                    // Self-heal the status against reality: a server flagged Started whose process has
                    // actually died (killed externally, or a re-attached process whose Exited event never
                    // fires) is reconciled here. This is what makes the grid, the Discord bot and the web
                    // portal always reflect the real state instead of a stale flag. OnGameServerExited is
                    // gated on ServerStatus == Started, so it is safe (no double-fire if Exited also fires),
                    // and it runs the normal crash path (alert + auto-restart when enabled).
                    foreach (ServerTable s in ServerGrid.Items.Cast<ServerTable>().ToList())
                    {
                        var meta = GetServerMetadata(s.ID);
                        if (meta == null || meta.ServerStatus != ServerStatus.Started) { continue; }

                        bool dead;
                        var p = meta.Process;
                        if (p == null) { dead = true; }
                        else { try { dead = p.HasExited; } catch { dead = false; } } // uncertain -> don't demote

                        if (dead) { OnGameServerExited(s); }
                    }
                }
                catch { }
            }
        }

        // Online players (A2S Steam Query): dedicated loop, slow cadence to avoid
        // hammering the servers. Updates ServerTable.Players (notifying -> grid + tiles).
        private async void StartPlayerCountRefresh()
        {
            while (true)
            {
                try
                {
                    foreach (ServerTable s in ServerGrid.Items.Cast<ServerTable>().ToList())
                    {
                        bool sdtd = s.Game == GameServer.SDTD.FullName;
                        if (!s.Online)
                        {
                            s.Players = "—";
                            if (sdtd) { Functions.SevenDaysTelnet.CloseSession(s.ID); } // drop the persistent console link
                            continue;
                        }

                        // 7DtD: keep a persistent Telnet console session alive while the server runs
                        // (live output stream + command input over one connection).
                        if (sdtd)
                        {
                            var (en, tp, tpw, _) = ReadSevenDaysTelnet(s.ID);
                            var meta = GetServerMetadata(s.ID);
                            if (en && tp > 0 && meta?.ServerConsole != null)
                            {
                                _ = Functions.SevenDaysTelnet.EnsureAsync(s.ID, s.IP, tp, tpw, meta.ServerConsole);
                            }
                        }

                        var info = await QueryServerPlayersAsync(s).ConfigureAwait(true);
                        s.Players = info.HasValue ? $"{info.Value.Players} / {info.Value.MaxPlayers}" : "—";
                    }
                }
                catch { }
                await Task.Delay(60 * 1000); // players refresh: 1 min (slow cadence to avoid hammering)
            }
        }

        // Picks the right counting method per game: A2S by default, native API for
        // games without A2S (Palworld = REST, Satisfactory = HTTPS).
        private async Task<SteamQuery.Info?> QueryServerPlayersAsync(ServerTable s)
        {
            string game = s.Game ?? string.Empty;

            if (game.StartsWith("Palworld", StringComparison.OrdinalIgnoreCase))
            {
                var (enabled, restPort, pwd) = ReadPalworldRest(s.ID);
                if (enabled && restPort > 0)
                {
                    return await Functions.NativePlayerQuery.PalworldAsync(s.IP, restPort, pwd).ConfigureAwait(true);
                }
                return null; // Palworld does not expose A2S: no fallback
            }

            if (game.StartsWith("Satisfactory", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(s.Port, out int sport))
                {
                    string token = Functions.ApiToken.Get(s.ID);
                    return await Functions.NativePlayerQuery.SatisfactoryAsync(s.IP, sport, token).ConfigureAwait(true);
                }
                return null;
            }

            if (game.StartsWith("7 Days to Die", StringComparison.OrdinalIgnoreCase))
            {
                var (en, tport, tpwd, tmax) = ReadSevenDaysTelnet(s.ID);
                if (en && tport > 0)
                {
                    return await Functions.NativePlayerQuery.SevenDaysTelnetAsync(s.IP, tport, tpwd, tmax).ConfigureAwait(true);
                }
                return null; // 7DtD without Telnet enabled: no reliable A2S
            }

            // Default: A2S (QueryPort, otherwise Port).
            string portStr = !string.IsNullOrWhiteSpace(s.QueryPort) && s.QueryPort != "0" ? s.QueryPort : s.Port;
            if (!int.TryParse(portStr, out int port)) { return null; }
            return await SteamQuery.GetInfoAsync(s.IP, port).ConfigureAwait(true);
        }

        // Reads TelnetEnabled / TelnetPort / TelnetPassword / ServerMaxPlayerCount of a 7 Days to Die server.
        // mtime-keyed caches so the 60s player poll doesn't re-parse serverconfig.xml / PalWorldSettings.ini
        // on every tick (these files change rarely). Accessed from the UI-context loop; ConcurrentDictionary for safety.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime mtime, (bool, int, string, int) val)> _sdtdCfgCache = new System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime, (bool, int, string, int))>();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime mtime, (bool, int, string) val)> _palCfgCache = new System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime, (bool, int, string))>();
        private static readonly System.Text.RegularExpressions.Regex _palEnabledRx = new System.Text.RegularExpressions.Regex(@"RESTAPIEnabled=True", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex _palPortRx = new System.Text.RegularExpressions.Regex(@"RESTAPIPort=(\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex _palPwdRx = new System.Text.RegularExpressions.Regex("AdminPassword=\"([^\"]*)\"", System.Text.RegularExpressions.RegexOptions.Compiled);

        private (bool enabled, int port, string password, int max) ReadSevenDaysTelnet(string serverId)
        {
            try
            {
                string cfg = Functions.ServerPath.GetServersServerFiles(serverId, "serverconfig.xml");
                if (!System.IO.File.Exists(cfg)) { return (false, 0, null, 0); }

                var mtime = System.IO.File.GetLastWriteTimeUtc(cfg);
                if (_sdtdCfgCache.TryGetValue(serverId, out var cached) && cached.mtime == mtime) { return cached.val; }

                var doc = System.Xml.Linq.XDocument.Load(cfg);
                string Prop(string name) => doc.Descendants("property")
                    .FirstOrDefault(p => (string)p.Attribute("name") == name)?.Attribute("value")?.Value;

                bool enabled = string.Equals(Prop("TelnetEnabled"), "true", StringComparison.OrdinalIgnoreCase);
                int.TryParse(Prop("TelnetPort"), out int port);
                string pwd = Prop("TelnetPassword");
                int.TryParse(Prop("ServerMaxPlayerCount"), out int max);
                var val = (enabled, port, pwd, max);
                _sdtdCfgCache[serverId] = (mtime, val);
                return val;
            }
            catch
            {
                return (false, 0, null, 0);
            }
        }

        // Reads RESTAPIEnabled / RESTAPIPort / AdminPassword from a Palworld server's PalWorldSettings.ini.
        private (bool enabled, int port, string password) ReadPalworldRest(string serverId)
        {
            try
            {
                string ini = Functions.ServerPath.GetServersServerFiles(serverId, @"Pal\Saved\Config\WindowsServer\PalWorldSettings.ini");
                if (!System.IO.File.Exists(ini)) { return (false, 0, null); }

                var mtime = System.IO.File.GetLastWriteTimeUtc(ini);
                if (_palCfgCache.TryGetValue(serverId, out var cached) && cached.mtime == mtime) { return cached.val; }

                string text = System.IO.File.ReadAllText(ini);
                bool enabled = _palEnabledRx.IsMatch(text);

                int port = 8212;
                var mp = _palPortRx.Match(text);
                if (mp.Success) { int.TryParse(mp.Groups[1].Value, out port); }

                string pwd = null;
                var mpw = _palPwdRx.Match(text);
                if (mpw.Success) { pwd = mpw.Groups[1].Value; }

                var val = (enabled, port, pwd);
                _palCfgCache[serverId] = (mtime, val);
                return val;
            }
            catch
            {
                return (false, 0, null);
            }
        }

        private static readonly System.Text.RegularExpressions.Regex _palRconEnabledRx = new System.Text.RegularExpressions.Regex(@"RCONEnabled=True", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex _palRconPortRx = new System.Text.RegularExpressions.Regex(@"RCONPort=(\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);

        // Returns the RCON port if RCON is enabled on this Palworld server, otherwise 0.
        private int ReadPalworldRconPort(string serverId)
        {
            try
            {
                string ini = Functions.ServerPath.GetServersServerFiles(serverId, @"Pal\Saved\Config\WindowsServer\PalWorldSettings.ini");
                if (!System.IO.File.Exists(ini)) { return 0; }
                string text = System.IO.File.ReadAllText(ini);
                if (!_palRconEnabledRx.IsMatch(text)) { return 0; }
                var m = _palRconPortRx.Match(text);
                return (m.Success && int.TryParse(m.Groups[1].Value, out int p)) ? p : 25575;
            }
            catch { return 0; }
        }

        private async void StartConsoleRefresh()
        {
            string lastId = null;
            int lastVersion = -1;
            while (true)
            {
                await Task.Delay(120); // was 10ms → 100 wakeups/s; 120ms is plenty for a console
                try
                {
                    var row = (ServerTable)ServerGrid.SelectedItem;
                    if (row == null) { continue; }
                    var meta = GetServerMetadata(row.ID);
                    if (meta?.ServerConsole == null) { continue; }

                    // Skip the expensive Get() (joins up to 150 lines) unless the buffer actually changed.
                    int version = meta.ServerConsole.Version;
                    if (row.ID == lastId && version == lastVersion) { continue; }
                    lastId = row.ID;
                    lastVersion = version;

                    string text = meta.ServerConsole.Get();
                    if (text.Length != console.Text.Length || text != console.Text)
                    {
                        console.Text = text;
                        if (meta.AutoScroll) { console.ScrollToEnd(); }
                    }
                }
                catch { /* a transient line must not kill the console refresh */ }
            }
        }

        private async void StartDashBoardRefresh()
        {
            var system = new SystemMetrics();

            // Get CPU info and Set
            await Task.Run(() => system.GetCPUStaticInfo());
            dashboard_cpu_type.Content = system.CPUType;

            // Get RAM info and Set
            await Task.Run(() => system.GetRAMStaticInfo());
            dashboard_ram_type.Content = system.RAMType;

            // Get Disk info and Set
            await Task.Run(() => system.GetDiskStaticInfo());
            dashboard_disk_name.Content = $"({system.DiskName})";
            dashboard_disk_type.Content = system.DiskType;

            while (true)
            {
                // Skip the whole dashboard compute (WMI-ish reads, LINQ, ~18 UI writes) while the Dashboard
                // tab isn't visible — it's invisible work on the common (Servers tab) case.
                if (!_dashboardVisible) { await Task.Delay(2000); continue; }

                dashboard_cpu_bar.Value = await Task.Run(() => system.GetCPUUsage());
                dashboard_cpu_bar.Value = (dashboard_cpu_bar.Value > 100.0) ? 100.0 : dashboard_cpu_bar.Value;
                dashboard_cpu_usage.Content = $"{string.Format("{0:0.00}", dashboard_cpu_bar.Value)}%";

                dashboard_ram_bar.Value = await Task.Run(() => system.GetRAMUsage());
                dashboard_ram_bar.Value = (dashboard_ram_bar.Value > 100.0) ? 100.0 : dashboard_ram_bar.Value;
                dashboard_ram_usage.Content = $"{string.Format("{0:0.00}", dashboard_ram_bar.Value)}%";
                dashboard_ram_ratio.Content = SystemMetrics.GetMemoryRatioString(dashboard_ram_bar.Value, system.RAMTotalSize);

                dashboard_disk_bar.Value = await Task.Run(() => system.GetDiskUsage());
                dashboard_disk_bar.Value = (dashboard_disk_bar.Value > 100.0) ? 100.0 : dashboard_disk_bar.Value;
                dashboard_disk_usage.Content = $"{string.Format("{0:0.00}", dashboard_disk_bar.Value)}%";
                dashboard_disk_ratio.Content = SystemMetrics.GetDiskRatioString(dashboard_disk_bar.Value, system.DiskTotalSize);

                dashboard_servers_bar.Value = ServerGrid.Items.Count * 100.0 / MAX_SERVER;
                dashboard_servers_bar.Value = (dashboard_servers_bar.Value > 100.0) ? 100.0 : dashboard_servers_bar.Value;
                dashboard_servers_usage.Content = $"{string.Format("{0:0.00}", dashboard_servers_bar.Value)}%";
                dashboard_servers_ratio.Content = $"{ServerGrid.Items.Count}/{MAX_SERVER}";

                int startedCount = GetStartedServerCount();
                dashboard_started_bar.Value = ServerGrid.Items.Count == 0 ? 0 : startedCount * 100.0 / ServerGrid.Items.Count;
                dashboard_started_bar.Value = (dashboard_started_bar.Value > 100.0) ? 100.0 : dashboard_started_bar.Value;
                dashboard_started_usage.Content = $"{string.Format("{0:0.00}", dashboard_started_bar.Value)}%";
                dashboard_started_ratio.Content = $"{startedCount}/{ServerGrid.Items.Count}";

                dashboard_players_count.Content = GetActivePlayers().ToString();

                // Dashboard health: summary leveraging the detections (updates, conflicts, alerts)
                try
                {
                    int total = ServerGrid.Items.Count;
                    int online = startedCount; // reuse the count computed above (was a second full enumeration)
                    int updates = _updateTooltips.Count;
                    int conflicts = ServerGrid.Items.Cast<ServerTable>().Count(s => s.PortConflict);
                    int diskAl = _diskAlerted.Count;
                    string ok = (updates == 0 && conflicts == 0 && diskAl == 0) ? "  ✅ nothing to report" : string.Empty;
                    dashboard_health.Text =
                        $"🟢 {online}/{total} server(s) online" +
                        $"     🔔 {updates} update(s) available" +
                        $"     🔴 {conflicts} port conflict(s)" +
                        $"     ⚠️ {diskAl} disk alert(s)" + ok;
                }
                catch { }

                try { Refresh_DashBoard_LiveChart(); } catch { /* do not kill the dashboard loop */ }

                await Task.Delay(2000);
            }
        }

        public int GetStartedServerCount()
        {
            return ServerGrid.Items.Cast<ServerTable>().Where(s => s.Status == "Started").Count();
        }

        public int GetActivePlayers()
        {
            // Live current players come from the A2S loop into ServerTable.Players ("cur / max"),
            // NOT Maxplayers (which is just the configured max unless the legacy query fills it).
            return ServerGrid.Items.Cast<ServerTable>().Where(s => s.Players != null && s.Players.Contains('/')).Sum(s => int.TryParse(s.Players.Split('/')[0].Trim(), out int count) ? count : 0);
        }

        // ===== #207/#25: remote-control web API =====
        private Functions.WebApi.WebApiServer _webApi;

        /// <summary>(Re)starts the web API according to the config (opt-in). Called at launch and after config
        /// changes. Returns null on success (or when intentionally disabled), or the error message if the
        /// listener failed to bind (e.g. port in use / missing urlacl) so the dialog can surface it.</summary>
        public string StartWebApi()
        {
            try
            {
                if (_webApi == null)
                {
                    _webApi = new Functions.WebApi.WebApiServer(
                        () => Dispatcher.Invoke(() => Api_GetServersJson()),
                        (id, action) => Dispatcher.Invoke(() => Api_DoAction(id, action)),
                        (id, action, payload) => Api_PalworldAdmin(id, action, payload)); // pure network I/O -> off the UI thread
                }
                _webApi.Stop();
                if (Functions.WebApi.WebApiConfig.Load().Enabled)
                {
                    if (!_webApi.Start()) { Functions.AppLog.Warn("WebApi", _webApi.LastError); return _webApi.LastError; }
                }
                return null;
            }
            catch (Exception ex) { Functions.AppLog.Warn("WebApi/Start", ex.Message); return ex.Message; }
        }

        /// <summary>JSON of the servers' status (run on the UI thread).</summary>
        public string Api_GetServersJson()
        {
            var list = new System.Collections.Generic.List<object>();
            foreach (ServerTable s in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                list.Add(new { id = s.ID, name = s.Name, game = s.Game, status = GetServerStatus(s.ID).ToString(), players = s.Maxplayers, map = s.Defaultmap, ip = s.IP, port = s.Port });
            }
            return Newtonsoft.Json.JsonConvert.SerializeObject(list);
        }

        /// <summary>Triggers an action on a server (start/stop/restart/backup); fire-and-forget. UI thread.</summary>
        public (bool, string) Api_DoAction(string id, string action)
        {
            var s = ServerGrid.Items.Cast<ServerTable>().FirstOrDefault(x => x.ID == id);
            if (s == null) { return (false, $"server {id} not found"); }
            switch (action)
            {
                case "start": _ = GameServer_Start(s, " | API"); return (true, "start requested");
                case "stop": _ = GameServer_Stop(s); return (true, "stop requested");
                case "restart": _ = GameServer_Restart(s); return (true, "restart requested");
                case "backup": _ = GameServer_Backup(s, " | API"); return (true, "backup requested");
                default: return (false, $"unknown action: {action}");
            }
        }

        private static string JField(string json, string key)
        {
            try { return string.IsNullOrEmpty(json) ? null : Newtonsoft.Json.Linq.JObject.Parse(json).Value<string>(key); }
            catch { return null; }
        }

        /// <summary>Palworld live admin for the web portal (REST + RCON). Runs on the listener thread
        /// (pure network I/O — never touches the UI). Returns (ok, data): a JSON array for "players",
        /// otherwise a status/error message. Host is loopback (portal is co-located with the server).</summary>
        public (bool ok, string data) Api_PalworldAdmin(string id, string action, string payload)
        {
            var (enabled, restPort, pwd) = ReadPalworldRest(id);
            if (!enabled || restPort <= 0 || string.IsNullOrEmpty(pwd)) { return (false, "Palworld REST API not enabled for this server"); }
            var api = new Functions.Palworld.PalworldAdmin("127.0.0.1", restPort, pwd);
            try
            {
                switch (action)
                {
                    case "players":
                    {
                        var (ok, players, err) = api.GetPlayersAsync().GetAwaiter().GetResult();
                        return ok ? (true, Newtonsoft.Json.JsonConvert.SerializeObject(players)) : (false, err);
                    }
                    case "announce":
                    {
                        var (ok, err) = api.AnnounceAsync(JField(payload, "message")).GetAwaiter().GetResult();
                        return (ok, ok ? "announced" : err);
                    }
                    case "kick":
                    case "ban":
                    {
                        string uid = JField(payload, "userid");
                        if (string.IsNullOrEmpty(uid)) { return (false, "userid required"); }
                        var (ok, err) = (action == "ban"
                            ? api.BanAsync(uid, "Banned by an administrator.")
                            : api.KickAsync(uid, "Kicked by an administrator.")).GetAwaiter().GetResult();
                        return (ok, ok ? action + " ok" : err);
                    }
                    case "unban":
                    {
                        var (ok, err) = api.UnbanAsync(JField(payload, "userid")).GetAwaiter().GetResult();
                        return (ok, ok ? "unbanned" : err);
                    }
                    case "save":
                    {
                        var (ok, err) = api.SaveAsync().GetAwaiter().GetResult();
                        return (ok, ok ? "saved" : err);
                    }
                    case "shutdown":
                    {
                        if (!int.TryParse(JField(payload, "waittime"), out int wait) || wait <= 0) { wait = 30; }
                        string msg = JField(payload, "message");
                        if (string.IsNullOrEmpty(msg)) { msg = "Server shutting down."; }
                        var (ok, err) = api.ShutdownAsync(wait, msg).GetAwaiter().GetResult();
                        return (ok, ok ? "shutdown scheduled" : err);
                    }
                    case "rcon":
                    {
                        int rconPort = ReadPalworldRconPort(id);
                        if (rconPort <= 0) { return (false, "RCON not enabled"); }
                        string cmd = JField(payload, "command");
                        if (string.IsNullOrEmpty(cmd)) { return (false, "command required"); }
                        using (var rc = new Functions.Palworld.RconClient("127.0.0.1", rconPort))
                        {
                            var (cok, cerr) = rc.ConnectAsync(pwd).GetAwaiter().GetResult();
                            if (!cok) { return (false, cerr); }
                            var (eok, text) = rc.ExecuteAsync(cmd).GetAwaiter().GetResult();
                            return (eok, text);
                        }
                    }
                    default: return (false, "unknown action");
                }
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        // Custom window dragging (works around the MahApps get_CriticalHandle bug on .NET).
        // We block the MahApps thumb (e.Handled) and call native WPF DragMove().
        private void MainWindow_TitleBarDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            // Ignore clicks on a button/menu (min/max/close, menu bar, etc.)
            for (DependencyObject d = e.OriginalSource as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (d is System.Windows.Controls.Primitives.ButtonBase || d is System.Windows.Controls.MenuItem) return;
            }

            // Only within the title bar area (default MahApps height ~30px)
            const double titleBarHeight = 32;
            if (e.GetPosition(this).Y > titleBarHeight) return;

            if (e.ClickCount == 2)
            {
                this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                e.Handled = true;
                return;
            }

            e.Handled = true; // prevents the faulty MahApps thumb -> no more NRE
            try { this.DragMove(); } catch { }
        }

        // .NET: Process.StartInfo throws InvalidOperationException if the process was RE-ATTACHED
        // (recovered by PID, not started by this instance). We then fall back to the server's
        // EmbedConsole config. Fixes the crash when clicking an already-started server.
        private bool IsEmbeddedConsole(Process p, string serverId)
        {
            if (p == null) return false;
            try { return p.StartInfo.CreateNoWindow || p.StartInfo.RedirectStandardOutput; }
            catch (InvalidOperationException) { return GetServerMetadata(serverId).EmbedConsole; }
        }

        private string _lastLiveChartSig;
        private void Refresh_DashBoard_LiveChart()
        {
            // List<(ServerType, PlayerCount)> Example: ("Ricochet Dedicated Server", 0)
            List<(string, int)> typePlayers = ServerGrid.Items.Cast<ServerTable>()
                .Where(s => s.Status == "Started" && s.Players != null && s.Players.Contains("/"))
                .Select(s => { int.TryParse(s.Players.Split('/')[0].Trim(), out int pc); return (type: s.Game, players: pc); })
                .GroupBy(s => s.type)
                .Select(s => (type: s.Key, players: s.Sum(p => p.players)))
                .ToList();

            // Skip the (expensive) axes+series rebuild when the data is identical to last tick — the chart
            // already shows it. Player counts rarely change between 2s ticks, so this is the common case.
            string sig = string.Join("|", typePlayers.OrderBy(t => t.Item1).Select(t => t.Item1 + "=" + t.Item2));
            if (sig == _lastLiveChartSig) { return; }
            _lastLiveChartSig = sig;

            // LiveCharts2: we rebuild axes + series on every refresh (small chart,
            // updated periodically). Simpler and more robust than mutating in place.
            double maxValue = 10;
            if (typePlayers.Count > 0)
            {
                int m = typePlayers.Select(s => s.Item2).Max() + 5;
                maxValue = (m > 10) ? m : 10;
            }

            livechart_players.YAxes = new[] { new Axis { Name = "Players", MinLimit = 0, MaxLimit = maxValue } };
            livechart_players.XAxes = new[] { new Axis { Labels = new string[0] } };
            livechart_players.Series = typePlayers
                .Select(item => (ISeries)new ColumnSeries<int> { Name = item.Item1, Values = new[] { item.Item2 } })
                .ToArray();
        }

        private async void SendGoogleAnalytics()
        {
            var analytics = new GoogleAnalytics();
            analytics.SendWindowsOS();
            analytics.SendWindowsGSMVersion();
            analytics.SendProcessorName();
            analytics.SendRAM();
            analytics.SendDisk();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Save height and width
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue("Height", Height.ToString());
                key?.SetValue("Width", Width.ToString());
            }

            // Get rid of system tray icon
            notifyIcon.Visible = false;
            notifyIcon.Dispose();

            // Closes all UPnP ports we opened (best-effort, non-blocking).
            try { Functions.PortForward.PortForwardManager.CleanupAllAsync().ConfigureAwait(false); } catch { }

            // Stop Discord Bot
            g_DiscordBot.Stop().ConfigureAwait(false);
        }

        // ===== P1-2: grid search / filter (name / game / status / id) =====
        private void TextBox_Search_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ApplyServerFilter();
        }

        private void ApplyServerFilter()
        {
            try
            {
                string q = textBox_Search?.Text?.Trim();
                if (string.IsNullOrEmpty(q)) { ServerGrid.Items.Filter = null; return; }
                string ql = q.ToLowerInvariant();
                ServerGrid.Items.Filter = o =>
                {
                    if (!(o is ServerTable s)) { return true; }
                    return (s.Name ?? string.Empty).ToLowerInvariant().Contains(ql)
                        || (s.Game ?? string.Empty).ToLowerInvariant().Contains(ql)
                        || (s.Status ?? string.Empty).ToLowerInvariant().Contains(ql)
                        || (s.ID ?? string.Empty).Contains(ql);
                };
            }
            catch { }
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerGrid.SelectedIndex != -1)
            {
                DataGrid_RefreshElements();
            }
        }

        private void DataGrid_RefreshElements()
        {
            var row = (ServerTable)ServerGrid.SelectedItem;

            if (row != null)
            {
                Console.WriteLine("Datagrid Changed");

                if (GetServerMetadata(row.ID).ServerStatus == ServerStatus.Stopped)
                {
                    button_Start.IsEnabled = true;
                    button_Stop.IsEnabled = false;
                    button_Restart.IsEnabled = false;
                    button_Console.IsEnabled = false;
                    button_Update.IsEnabled = true;
                    button_Backup.IsEnabled = true;

                    textbox_servercommand.IsEnabled = false;
                    button_servercommand.IsEnabled = false;
                }
                else if (GetServerMetadata(row.ID).ServerStatus == ServerStatus.Started)
                {
                    button_Start.IsEnabled = false;
                    button_Stop.IsEnabled = true;
                    button_Restart.IsEnabled = true;
                    Process p = GetServerMetadata(row.ID).Process;
                    button_Console.IsEnabled = (p == null || p.HasExited) ? false : !IsEmbeddedConsole(p, row.ID);
                    button_Update.IsEnabled = false;
                    button_Backup.IsEnabled = false;

                    textbox_servercommand.IsEnabled = true;
                    button_servercommand.IsEnabled = true;
                }
                else
                {
                    button_Start.IsEnabled = false;
                    button_Stop.IsEnabled = false;
                    button_Restart.IsEnabled = false;
                    button_Console.IsEnabled = false;
                    button_Update.IsEnabled = false;
                    button_Backup.IsEnabled = false;

                    textbox_servercommand.IsEnabled = false;
                    button_servercommand.IsEnabled = false;
                }

                switch (GetServerMetadata(row.ID).ServerStatus)
                {
                    case ServerStatus.Restarting:
                    case ServerStatus.Restarted:
                    case ServerStatus.Started:
                    case ServerStatus.Starting:
                    case ServerStatus.Stopping:
                        button_Kill.IsEnabled = true;
                        break;
                    default: button_Kill.IsEnabled = false; break;
                }

                button_ManageAddons.IsEnabled = ServerAddon.IsGameSupportManageAddons(row.Game);
                if (GetServerMetadata(row.ID).ServerStatus == ServerStatus.Deleting || GetServerMetadata(row.ID).ServerStatus == ServerStatus.Restoring)
                {
                    button_ManageAddons.IsEnabled = false;
                }

                slider_ProcessPriority.Value = Functions.CPU.Priority.GetPriorityInteger(GetServerMetadata(row.ID).CPUPriority);
                textBox_ProcessPriority.Text = Functions.CPU.Priority.GetPriorityByInteger((int)slider_ProcessPriority.Value);

                textBox_SetAffinity.Text = Functions.CPU.Affinity.GetAffinityValidatedString(GetServerMetadata(row.ID).CPUAffinity);
                string affinity = new string(textBox_SetAffinity.Text.Reverse().ToArray());
                for (int i = 0; i < _checkBoxes.Count; i++)
                {
                    _checkBoxes[i].IsChecked = affinity[i] == '1';
                }

                button_Status.Content = row.StatusDisplay.ToUpper();
                button_Status.Background = (GetServerMetadata(row.ID).ServerStatus == ServerStatus.Started) ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Orange;

                var gameServer = GameServer.Data.Class.Get(row.Game, pluginList: PluginsList);
                switch_embedconsole.IsEnabled = gameServer.AllowsEmbedConsole;
                switch_embedconsole.IsChecked = gameServer.AllowsEmbedConsole ? GetServerMetadata(row.ID).EmbedConsole : false;
                Button_AutoScroll.Content = GetServerMetadata(row.ID).AutoScroll ? "✔️ AUTO SCROLL" : "❌ AUTO SCROLL";

                switch_autorestart.IsChecked = GetServerMetadata(row.ID).AutoRestart;
                switch_restartcrontab.IsChecked = GetServerMetadata(row.ID).RestartCrontab;
                switch_autostart.IsChecked = GetServerMetadata(row.ID).AutoStart;
                switch_autoupdate.IsChecked = GetServerMetadata(row.ID).AutoUpdate;
                switch_updateonstart.IsChecked = GetServerMetadata(row.ID).UpdateOnStart;
                switch_backuponstart.IsChecked = GetServerMetadata(row.ID).BackupOnStart;
                switch_discordalert.IsChecked = GetServerMetadata(row.ID).DiscordAlert;
                button_discordtest.IsEnabled = switch_discordalert.IsChecked == true;
                switch_memwatchdog.IsChecked = GetServerMetadata(row.ID).MemoryWatchdog; // #16
                textBox_memlimit.Text = GetServerMetadata(row.ID).MemoryLimitMB;
                switch_backupbeforeupdate.IsChecked = GetServerMetadata(row.ID).BackupBeforeUpdate; // #20

                textBox_restartcrontab.Text = GetServerMetadata(row.ID).CrontabFormat;
                textBox_nextcrontab.Text = CrontabSchedule.TryParse(textBox_restartcrontab.Text)?.GetNextOccurrence(DateTime.Now).ToString("ddd, MM/dd/yyyy HH:mm:ss");

                MahAppSwitch_AutoStartAlert.IsChecked = GetServerMetadata(row.ID).AutoStartAlert;
                MahAppSwitch_AutoRestartAlert.IsChecked = GetServerMetadata(row.ID).AutoRestartAlert;
                MahAppSwitch_AutoUpdateAlert.IsChecked = GetServerMetadata(row.ID).AutoUpdateAlert;
                MahAppSwitch_RestartCrontabAlert.IsChecked = GetServerMetadata(row.ID).RestartCrontabAlert;
                MahAppSwitch_CrashAlert.IsChecked = GetServerMetadata(row.ID).CrashAlert;
            }
        }

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            if (ServerGrid.Items.Count >= MAX_SERVER)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            MahAppFlyout_InstallGameServer.Visibility = Visibility.Visible;

            if (!progressbar_InstallProgress.IsIndeterminate)
            {
                textbox_InstallServerName.IsEnabled = true;
                comboBox_InstallGameServer.IsEnabled = true;
                progressbar_InstallProgress.IsIndeterminate = false;
                textblock_InstallProgress.Text = string.Empty;
                button_Install.IsEnabled = true;

                ComboBox_InstallGameServer_SelectionChanged(sender, null);

                var newServerConfig = new ServerConfig(null);
                textbox_InstallServerName.Text = $"WindowsGSM - Server #{newServerConfig.ServerID}";
            }
        }

        // Adds a GENERIC dedicated Steam server by AppID (dedicated flow, does not alter the standard install).
        private async void Servers_AddGenericSteam_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Functions.GenericSteamDialog { Owner = this };
            if (dlg.ShowDialog() != true || dlg.Result == null) { return; }
            var prof = dlg.Result.Value;

            var newServerConfig = new ServerConfig(null);
            string installPath = ServerPath.GetServersServerFiles(newServerConfig.ServerID);
            try { if (Directory.Exists(installPath)) { Directory.Delete(installPath, true); } } catch { }
            Directory.CreateDirectory(installPath);
            newServerConfig.CreateServerDirectory();

            // Profile written BEFORE instantiation: the GenericSteam ctor reads configs/wgsm-generic.json.
            GameServer.GenericSteam.SaveProfile(newServerConfig.ServerID, prof.AppId, prof.Name, prof.Executable, prof.Arguments);

            string servergame = GameServer.GenericSteam.FullName;
            string servername = string.IsNullOrWhiteSpace(prof.Name) ? $"Steam #{prof.AppId}" : prof.Name;

            Log(newServerConfig.ServerID, $"Add generic Steam: {servername} (AppID {prof.AppId}) - SteamCMD installation...");

            dynamic gameServer = GameServer.Data.Class.Get(servergame, newServerConfig, PluginsList);
            Process installer = await gameServer.Install();
            if (installer != null)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        var reader = installer.StandardOutput;
                        while (!reader.EndOfStream) { reader.ReadLine(); }
                        installer.WaitForExit();
                    }
                    catch { }
                });
            }

            if (gameServer.IsInstallValid())
            {
                newServerConfig.ServerIP = newServerConfig.GetIPAddress();
                newServerConfig.ServerPort = newServerConfig.GetAvailablePort(gameServer.Port, gameServer.PortIncrements);
                newServerConfig.SetData(servergame, servername, gameServer);
                newServerConfig.CreateWindowsGSMConfig();
                LoadServerTable();
                Log(newServerConfig.ServerID, "Add generic Steam: success.");
                System.Windows.MessageBox.Show(Loc.T("Msg.GenericSteamInstalled", servername, prof.AppId, prof.Executable, prof.Arguments), Loc.T("Msg.ServerAddedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                string err = string.Empty;
                try { err = gameServer.Error ?? string.Empty; } catch { }
                Log(newServerConfig.ServerID, "Add generic Steam: installation failed. " + err);
                System.Windows.MessageBox.Show(Loc.T("Msg.GenericSteamInstallFailed", prof.AppId, err), Loc.T("Msg.FailedTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Button_Install_Click(object sender, RoutedEventArgs e)
        {
            if (Installer != null)
            {
                if (!Installer.HasExited) { Installer.Kill(); }
                Installer = null;
            }

            var selectedgame = (Images.Row)comboBox_InstallGameServer.SelectedItem;
            if (string.IsNullOrWhiteSpace(textbox_InstallServerName.Text) || selectedgame == null) { return; }

            var newServerConfig = new ServerConfig(null);
            string installPath = ServerPath.GetServersServerFiles(newServerConfig.ServerID);
            if (Directory.Exists(installPath))
            {
                try
                {
                    Directory.Delete(installPath, true);
                }
                catch
                {
                    System.Windows.Forms.MessageBox.Show(Loc.T("Msg.PathNotAccessible", installPath), Loc.T("Msg.ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            Directory.CreateDirectory(installPath);

            //Installation start
            textbox_InstallServerName.IsEnabled = false;
            comboBox_InstallGameServer.IsEnabled = false;
            progressbar_InstallProgress.IsIndeterminate = true;
            textblock_InstallProgress.Text = "Installing";
            button_Install.IsEnabled = false;
            textbox_InstallLog.Text = string.Empty;

            string servername = textbox_InstallServerName.Text;
            string servergame = selectedgame.Name;

            newServerConfig.CreateServerDirectory();

            dynamic gameServer = GameServer.Data.Class.Get(servergame, newServerConfig, PluginsList);
            Installer = await gameServer.Install();

            if (Installer != null)
            {
                //Wait installer exit. Example: steamcmd.exe
                await Task.Run(() =>
                {
                    var reader = Installer.StandardOutput;
                    while (!reader.EndOfStream)
                    {
                        var nextLine = reader.ReadLine();
                        if (nextLine.Contains("Logging in user "))
                        {
                            nextLine += Environment.NewLine + "Please send the Login Token:";
                        }

                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            textbox_InstallLog.AppendText(nextLine + Environment.NewLine);
                            textbox_InstallLog.ScrollToEnd();
                        });
                    }

                    Installer?.WaitForExit();
                });
            }

            if (gameServer.IsInstallValid())
            {
                newServerConfig.ServerIP = newServerConfig.GetIPAddress();
                newServerConfig.ServerPort = newServerConfig.GetAvailablePort(gameServer.Port, gameServer.PortIncrements);

                // Create WindowsGSM.cfg
                newServerConfig.SetData(servergame, servername, gameServer);
                newServerConfig.CreateWindowsGSMConfig();

                // Create WindowsGSM.cfg and game server config
                try
                {
                    gameServer = GameServer.Data.Class.Get(servergame, newServerConfig, PluginsList);
                    gameServer.CreateServerCFG();
                }
                catch
                {
                    // ignore
                }

                LoadServerTable();
                Log(newServerConfig.ServerID, "Install: Success");

                MahAppFlyout_InstallGameServer.Visibility = Visibility.Collapsed;
                textbox_InstallServerName.IsEnabled = true;
                comboBox_InstallGameServer.IsEnabled = true;
                progressbar_InstallProgress.IsIndeterminate = false;

                if (MahAppSwitch_SendStatistics.IsChecked == true)
                {
                    var analytics = new GoogleAnalytics();
                    analytics.SendGameServerInstall(newServerConfig.ServerID, servergame);
                }
            }
            else
            {
                textbox_InstallServerName.IsEnabled = true;
                comboBox_InstallGameServer.IsEnabled = true;
                progressbar_InstallProgress.IsIndeterminate = false;
                textblock_InstallProgress.Text = "Install";
                button_Install.IsEnabled = true;

                if (Installer != null)
                {
                    textblock_InstallProgress.Text = "Fail to install [ERROR] Exit code: " + Installer.ExitCode;
                }
                else
                {
                    textblock_InstallProgress.Text = $"Fail to install [ERROR] {gameServer.Error}";
                }
            }
        }

        private void ComboBox_InstallGameServer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Set the elements visibility of Install Server Flyout
            var selectedgame = (Images.Row)comboBox_InstallGameServer.SelectedItem;
            button_InstallSetAccount.IsEnabled = false;
            textBox_InstallToken.Visibility = Visibility.Hidden;
            button_InstallSendToken.Visibility = Visibility.Hidden;
            if (selectedgame == null) { return; }

            try
            {
                dynamic gameServer = GameServer.Data.Class.Get(selectedgame.Name, pluginList: PluginsList);
                if (!gameServer.loginAnonymous)
                {
                    button_InstallSetAccount.IsEnabled = true;
                    textBox_InstallToken.Visibility = Visibility.Visible;
                    button_InstallSendToken.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                // ignore
            }
        }

        private void Button_SetAccount_Click(object sender, RoutedEventArgs e)
        {
            var steamCMD = new Installer.SteamCMD();
            steamCMD.CreateUserDataTxtIfNotExist();

            string userDataPath = ServerPath.GetBin("steamcmd", "userData.txt");
            if (File.Exists(userDataPath))
            {
                Shell.Open(userDataPath);
            }
        }

        private void Button_SendToken_Click(object sender, RoutedEventArgs e)
        {
            if (Installer != null)
            {
                Installer.StandardInput.WriteLine(textBox_InstallToken.Text);
            }

            textBox_InstallToken.Text = string.Empty;
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (ServerGrid.Items.Count >= MAX_SERVER)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            MahAppFlyout_ImportGameServer.Visibility = Visibility.Visible;

            if (!progressbar_ImportProgress.IsIndeterminate)
            {
                textbox_ImportServerName.IsEnabled = true;
                comboBox_ImportGameServer.IsEnabled = true;
                progressbar_ImportProgress.IsIndeterminate = false;
                textblock_ImportProgress.Text = string.Empty;
                button_Import.Content = "Import";

                var newServerConfig = new ServerConfig(null);
                textbox_ImportServerName.Text = $"WindowsGSM - Server #{newServerConfig.ServerID}";
            }
        }

        private async void Button_Import_Click(object sender, RoutedEventArgs e)
        {
            var selectedgame = (Images.Row)comboBox_ImportGameServer.SelectedItem;
            label_ServerDirWarn.Content = Directory.Exists(textbox_ServerDir.Text) ? string.Empty : "Server Dir is invalid";
            if (string.IsNullOrWhiteSpace(textbox_ImportServerName.Text) || selectedgame == null) { return; }

            string servername = textbox_ImportServerName.Text;
            string servergame = selectedgame.Name;

            var newServerConfig = new ServerConfig(null);
            dynamic gameServer = GameServer.Data.Class.Get(servergame, newServerConfig, PluginsList);

            if (!gameServer.IsImportValid(textbox_ServerDir.Text))
            {
                label_ServerDirWarn.Content = gameServer.Error;
                return;
            }

            string importPath = ServerPath.GetServersServerFiles(newServerConfig.ServerID);
            if (Directory.Exists(importPath))
            {
                try
                {
                    Directory.Delete(importPath, true);
                }
                catch
                {
                    System.Windows.Forms.MessageBox.Show(Loc.T("Msg.PathNotAccessible", importPath), Loc.T("Msg.ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            //Import start
            textbox_ImportServerName.IsEnabled = false;
            comboBox_ImportGameServer.IsEnabled = false;
            textbox_ServerDir.IsEnabled = false;
            button_Browse.IsEnabled = false;
            progressbar_ImportProgress.IsIndeterminate = true;
            textblock_ImportProgress.Text = "Importing";

            string sourcePath = textbox_ServerDir.Text;
            string importLog = await Task.Run(() =>
            {
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(sourcePath, importPath);

                    // Scary error while moving the directory, some files may lost - Risky
                    //Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(sourcePath, importPath);

                    // This doesn't work on cross drive - Not working on cross drive
                    //Directory.Move(sourcePath, importPath);

                    return null;
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            });

            if (importLog != null)
            {
                textbox_ImportServerName.IsEnabled = true;
                comboBox_ImportGameServer.IsEnabled = true;
                textbox_ServerDir.IsEnabled = true;
                button_Browse.IsEnabled = true;
                progressbar_ImportProgress.IsIndeterminate = false;
                textblock_ImportProgress.Text = "[ERROR] Fail to import";
                MessageBox.Show(Loc.T("Msg.ImportCopyFailed", textbox_ServerDir.Text, importPath, importLog), Loc.T("Msg.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Create WindowsGSM.cfg
            newServerConfig.SetData(servergame, servername, gameServer);
            newServerConfig.CreateWindowsGSMConfig();

            LoadServerTable();
            Log(newServerConfig.ServerID, "Import: Success");

            MahAppFlyout_ImportGameServer.Visibility = Visibility.Collapsed;
            textbox_ImportServerName.IsEnabled = true;
            comboBox_ImportGameServer.IsEnabled = true;
            textbox_ServerDir.IsEnabled = true;
            button_Browse.IsEnabled = true;
            progressbar_ImportProgress.IsIndeterminate = false;
            textblock_ImportProgress.Text = string.Empty;
        }

        private void Button_Browse_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.ShowDialog();

            if (!string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
            {
                textbox_ServerDir.Text = folderBrowserDialog.SelectedPath;
            }
        }

        // #18: duplicates the selected server's config to a new free ID (incremented ports).
        private void Duplicate_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { System.Windows.MessageBox.Show(Loc.T("Msg.DuplicateSelectFirst"), Loc.T("Msg.DuplicateTitle"), MessageBoxButton.OK, MessageBoxImage.Information); return; }

            // First free ID
            int newId = -1;
            for (int i = 1; i <= MAX_SERVER; i++)
            {
                if (!Directory.Exists(ServerPath.GetServers(i.ToString()))) { newId = i; break; }
            }
            if (newId == -1) { System.Windows.MessageBox.Show(Loc.T("Msg.DuplicateNoFreeId", MAX_SERVER), Loc.T("Msg.DuplicateTitle"), MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            string srcCfg = ServerPath.GetServersConfigs(server.ID, "WindowsGSM.cfg");
            if (!File.Exists(srcCfg)) { return; }

            try
            {
                var dst = new ServerConfig(newId.ToString());
                dst.CreateServerDirectory();
                File.Copy(srcCfg, ServerPath.GetServersConfigs(newId.ToString(), "WindowsGSM.cfg"), true);

                // ports: increment from the source, skipping those already in use
                var used = new HashSet<int>();
                foreach (var s in ServerGrid.Items.Cast<ServerTable>())
                {
                    if (int.TryParse(s.Port, out var pp)) { used.Add(pp); }
                    if (int.TryParse(s.QueryPort, out var qq)) { used.Add(qq); }
                }
                int basePort = int.TryParse(server.Port, out var bp) ? bp : 27015;
                int baseQuery = int.TryParse(server.QueryPort, out var bq) ? bq : basePort + 1;
                int newPort = basePort + 1; while (used.Contains(newPort)) { newPort++; } used.Add(newPort);
                int newQuery = baseQuery + 1; while (used.Contains(newQuery)) { newQuery++; } used.Add(newQuery);

                ServerConfig.SetSetting(newId.ToString(), ServerConfig.SettingName.ServerName, (server.Name ?? "Server") + " (copy)");
                ServerConfig.SetSetting(newId.ToString(), ServerConfig.SettingName.ServerPort, newPort.ToString());
                ServerConfig.SetSetting(newId.ToString(), ServerConfig.SettingName.ServerQueryPort, newQuery.ToString());

                LoadServerTable();
                Log("System", $"Server #{server.ID} duplicated -> #{newId} ({server.Name} (copy), ports {newPort}/{newQuery}). Run Install/Import for the files.");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(Loc.T("Msg.DuplicateFailed", ex.Message), Loc.T("Msg.DuplicateTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            MessageBoxResult result = MessageBox.Show(Loc.T("Msg.DeleteServerConfirm"), Loc.T("Msg.ConfirmationTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await GameServer_Delete(server);
        }

        private async void Button_DiscordEdit_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string webhookUrl = ServerConfig.GetSetting(server.ID, Functions.ServerConfig.SettingName.DiscordWebhook);

            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = Loc.T("Msg.SaveButton"),
                DefaultText = webhookUrl
            };

            webhookUrl = await this.ShowInputAsync(Loc.T("Msg.DiscordWebhookTitle"), Loc.T("Msg.DiscordWebhookPrompt"), settings);
            if (webhookUrl == null) { return; } //If pressed cancel

            _serverMetadata[int.Parse(server.ID)].DiscordWebhook = webhookUrl;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.DiscordWebhook, webhookUrl);
        }

        private async void Button_DiscordSetMessage_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            var message = ServerConfig.GetSetting(server.ID, ServerConfig.SettingName.DiscordMessage);

            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = Loc.T("Msg.SaveButton"),
                DefaultText = message
            };

            message = await this.ShowInputAsync(Loc.T("Msg.DiscordMessageTitle"), Loc.T("Msg.DiscordMessagePrompt"), settings);
            if (message == null) { return; } //If pressed cancel

            _serverMetadata[int.Parse(server.ID)].DiscordMessage = message;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.DiscordMessage, message);
        }

        private async void Button_DiscordWebhookTest_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            int serverId = int.Parse(server.ID);
            if (!GetServerMetadata(serverId).DiscordAlert) { return; }

            var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage, g_DonorType);
            await webhook.Send(server.ID, server.Game, "Webhook Test Alert", server.Name, server.IP, server.Port);
        }

        private void Button_ServerCommand_Click(object sender, RoutedEventArgs e)
        {
            string command = textbox_servercommand.Text;
            textbox_servercommand.Text = string.Empty;

            if (string.IsNullOrWhiteSpace(command)) { return; }

            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            SendCommand(server, command);
        }

        private void Textbox_ServerCommand_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (textbox_servercommand.Text.Length != 0)
                {
                    GetServerMetadata(0).ServerConsole.Add(textbox_servercommand.Text);
                }

                Button_ServerCommand_Click(this, new RoutedEventArgs());
            }
        }

        private void Textbox_ServerCommand_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.IsDown && e.Key == Key.Up)
            {
                e.Handled = true;
                textbox_servercommand.Text = GetServerMetadata(0).ServerConsole.GetPreviousCommand();
            }
            else if (e.IsDown && e.Key == Key.Down)
            {
                e.Handled = true;
                textbox_servercommand.Text = GetServerMetadata(0).ServerConsole.GetNextCommand();
            }
        }

        #region Actions - Button Click
        private void Actions_Crash_Click(object sender, RoutedEventArgs e)
        {
            int test = 0;
            _ = 0 / test; // Crash
        }

        private async void Actions_Start_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            // Reload WindowsGSM.cfg on start
            SaveServerConfigToServerMetadata(server.ID, new ServerConfig(server.ID));

            await GameServer_Start(server);
        }

        private async void Actions_Stop_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            await GameServer_Stop(server);
        }

        private async void Actions_Restart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            await GameServer_Restart(server);
        }

        private async void Actions_Kill_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            switch (GetServerMetadata(server.ID).ServerStatus)
            {
                case ServerStatus.Restarting:
                case ServerStatus.Restarted:
                case ServerStatus.Started:
                case ServerStatus.Starting:
                case ServerStatus.Stopping:
                    Process p = GetServerMetadata(server.ID).Process;
                    if (p != null && !p.HasExited)
                    {
                        Log(server.ID, "Actions: Kill");
                        p.Kill();

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                        Log(server.ID, "Server: Killed");
                        SetServerStatus(server, "Stopped");
                        _serverMetadata[int.Parse(server.ID)].ServerConsole.Clear();
                        _serverMetadata[int.Parse(server.ID)].Process = null;
                    }

                    break;
            }
        }

        private void Actions_ToggleConsole_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            Process p = GetServerMetadata(server.ID).Process;
            if (p == null) { return; }

            //If console is useless, return
            if (IsEmbeddedConsole(p, server.ID)) { return; }

            IntPtr hWnd = GetServerMetadata(server.ID).MainWindow;
            ShowWindow(hWnd, ShowWindow(hWnd, WindowShowStyle.Hide) ? WindowShowStyle.Hide : WindowShowStyle.ShowNormal);
        }

        private async void Actions_StartAllServers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped)
                {
                    await GameServer_Start(server);
                }
            }
        }

        private async void Actions_StartServersWithAutoStartEnabled_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped && GetServerMetadata(server.ID).AutoStart)
                {
                    await GameServer_Start(server);
                }
            }
        }

        private async void Actions_StopAllServers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                {
                    await GameServer_Stop(server);
                }
            }
        }

        private async void Actions_RestartAllServers_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in ServerGrid.Items.Cast<ServerTable>().ToList())
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                {
                    await GameServer_Restart(server);
                }
            }
        }

        private async void Actions_Update_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show(Loc.T("Msg.UpdateServerConfirm"), Loc.T("Msg.ConfirmationTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await GameServer_Update(server);
        }

        // #14: updates at once all Stopped servers flagged "update available" (orange badge).
        private async void Actions_UpdateAllAvailable_Click(object sender, RoutedEventArgs e)
        {
            var targets = ServerGrid.Items.Cast<ServerTable>()
                .Where(s => s.UpdateAvailable && GetServerMetadata(s.ID) != null && GetServerMetadata(s.ID).ServerStatus == ServerStatus.Stopped)
                .ToList();

            if (targets.Count == 0)
            {
                System.Windows.MessageBox.Show(Loc.T("Msg.UpdateAllNone"), Loc.T("Msg.UpdateAllTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var names = string.Join("\n", targets.Select(s => $"#{s.ID} {s.Name}"));
            MessageBoxResult result = System.Windows.MessageBox.Show(Loc.T("Msg.UpdateAllConfirm", targets.Count, names), Loc.T("Msg.UpdateAllTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            foreach (var server in targets)
            {
                Log(server.ID, $"[Update all] Updating #{server.ID} {server.Name}…");
                await GameServer_Update(server);
            }
            Log("System", $"[Update all] Done ({targets.Count} server(s)).");
        }

        // Backup ALL: backs up all stopped servers.
        private async void Actions_BackupAll_Click(object sender, RoutedEventArgs e)
        {
            var targets = ServerGrid.Items.Cast<ServerTable>()
                .Where(s => GetServerMetadata(s.ID) != null && GetServerMetadata(s.ID).ServerStatus == ServerStatus.Stopped)
                .ToList();
            if (targets.Count == 0)
            {
                System.Windows.MessageBox.Show(Loc.T("Msg.BackupAllNone"), Loc.T("Msg.BackupAllTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var result = System.Windows.MessageBox.Show(Loc.T("Msg.BackupAllConfirm", targets.Count), Loc.T("Msg.BackupAllTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }
            foreach (var server in targets)
            {
                Log(server.ID, $"[Backup all] Backing up #{server.ID} {server.Name}…");
                await GameServer_Backup(server, " | Backup all");
            }
            Log("System", $"[Backup all] Done ({targets.Count} server(s)).");
        }

        private async void Actions_UpdateValidate_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show(Loc.T("Msg.ValidateServerConfirm"), Loc.T("Msg.ConfirmationTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await GameServer_Update(server, notes: " | Validate", validate: true);
        }

        private async void Actions_Backup_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            MessageBoxResult result = System.Windows.MessageBox.Show(Loc.T("Msg.BackupServerConfirm"), Loc.T("Msg.ConfirmationTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) { return; }

            await GameServer_Backup(server);
        }

        private async void Actions_RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            listbox_RestoreBackup.Items.Clear();
            var backupConfig = new BackupConfig(server.ID);
            if (Directory.Exists(backupConfig.BackupLocation))
            {
                string zipFileName = $"WGSM-Backup-Server-{server.ID}-";
                foreach (var fi in new DirectoryInfo(backupConfig.BackupLocation).GetFiles("*.zip").Where(x => x.Name.Contains(zipFileName)).OrderByDescending(x => x.LastWriteTime))
                {
                    listbox_RestoreBackup.Items.Add(fi.Name);
                }
            }

            if (listbox_RestoreBackup.Items.Count > 0)
            {
                listbox_RestoreBackup.SelectedIndex = 0;
            }

            label_RestoreBackupServerName.Content = server.Name;
            MahAppFlyout_RestoreBackup.Visibility = Visibility.Visible;
        }

        private async void Button_RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            if (listbox_RestoreBackup.SelectedIndex >= 0)
            {
                MahAppFlyout_RestoreBackup.Visibility = Visibility.Collapsed;
                await GameServer_RestoreBackup(server, listbox_RestoreBackup.SelectedItem.ToString());
            }
        }

        private void Actions_ManageAddons_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            ListBox_ManageAddons_Refresh();
            ToggleMahappFlyout(MahAppFlyout_ManageAddons);
        }
        #endregion

        private void ListBox_ManageAddonsLeft_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (listBox_ManageAddonsLeft.SelectedItem != null)
            {
                var server = (ServerTable)ServerGrid.SelectedItem;
                if (server == null) { return; }

                string item = listBox_ManageAddonsLeft.SelectedItem.ToString();
                listBox_ManageAddonsLeft.Items.Remove(listBox_ManageAddonsLeft.Items[listBox_ManageAddonsLeft.SelectedIndex]);
                listBox_ManageAddonsRight.Items.Add(item);
                var serverAddon = new ServerAddon(server.ID, server.Game);
                serverAddon.AddToRight(listBox_ManageAddonsRight.Items.OfType<string>().ToList(), item);

                ListBox_ManageAddons_Refresh();

                foreach (var selected in listBox_ManageAddonsRight.Items)
                {
                    if (selected.ToString() == item)
                    {
                        listBox_ManageAddonsRight.SelectedItem = selected;
                    }
                }
            }
        }

        private void ListBox_ManageAddonsRight_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (listBox_ManageAddonsRight.SelectedItem != null)
            {
                var server = (ServerTable)ServerGrid.SelectedItem;
                if (server == null) { return; }

                string item = listBox_ManageAddonsRight.SelectedItem.ToString();
                listBox_ManageAddonsRight.Items.Remove(listBox_ManageAddonsRight.Items[listBox_ManageAddonsRight.SelectedIndex]);
                listBox_ManageAddonsLeft.Items.Add(item);
                var serverAddon = new ServerAddon(server.ID, server.Game);
                serverAddon.AddToLeft(listBox_ManageAddonsRight.Items.OfType<string>().ToList(), item);

                ListBox_ManageAddons_Refresh();

                foreach (var selected in listBox_ManageAddonsLeft.Items)
                {
                    if (selected.ToString() == item)
                    {
                        listBox_ManageAddonsLeft.SelectedItem = selected;
                    }
                }
            }
        }

        private void ListBox_ManageAddons_Refresh()
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            var serverAddon = new ServerAddon(server.ID, server.Game);
            label_ManageAddonsName.Content = server.Name;
            label_ManageAddonsGame.Content = server.Game;
            label_ManageAddonsType.Content = serverAddon.GetModsName();

            listBox_ManageAddonsLeft.Items.Clear();
            foreach (string item in serverAddon.GetLeftListBox())
            {
                listBox_ManageAddonsLeft.Items.Add(item);
            }

            listBox_ManageAddonsRight.Items.Clear();
            foreach (string item in serverAddon.GetRightListBox())
            {
                listBox_ManageAddonsRight.Items.Add(item);
            }
        }

        private async Task<dynamic> Server_BeginStart(ServerTable server)
        {
            dynamic gameServer = GameServer.Data.Class.Get(server.Game, new ServerConfig(server.ID), PluginsList);
            if (gameServer == null) { return null; }

            // Euro/American Truck Simulator: the dedicated server CRASHES at startup without
            // save\server_packages.sii + .dat (map/DLC data, exported from the client via
            // export_server_packages). We warn clearly instead of leaving an obscure crash.
            if ((server.Game ?? "").ToLowerInvariant().Contains("truck simulator"))
            {
                try
                {
                    string save = ServerPath.GetServersServerFiles(server.ID, "save");
                    bool ok = File.Exists(Path.Combine(save, "server_packages.sii")) && File.Exists(Path.Combine(save, "server_packages.dat"));
                    if (!ok)
                    {
                        var mb = new Wpf.Ui.Controls.MessageBox
                        {
                            Title = Loc.T("Msg.TruckPackagesTitle"),
                            Content = Loc.T("Msg.TruckPackagesContent"),
                            PrimaryButtonText = Loc.T("Msg.StartAnywayButton"),
                            CloseButtonText = Loc.T("Msg.CancelButton")
                        };
                        var res = await mb.ShowDialogAsync();
                        if (res != Wpf.Ui.Controls.MessageBoxResult.Primary) { return null; }
                    }
                }
                catch (Exception e) { AppLog.Warn("TruckSim", "check server_packages: " + e.Message); }
            }

            //End All Running Process
            await EndAllRunningProcess(server.ID);
            await Task.Delay(500);

            //Add Start File to WindowsFirewall before start
            string startPath = ServerPath.GetServersServerFiles(server.ID, gameServer.StartPath);
            if (!string.IsNullOrWhiteSpace(gameServer.StartPath))
            {
                WindowsFirewall firewall = new WindowsFirewall(Path.GetFileName(startPath), startPath);
                if (!await firewall.IsRuleExist())
                {
                    await firewall.AddRule();
                }
            }

            gameServer.AllowsEmbedConsole = GetServerMetadata(server.ID).EmbedConsole;
            Process p = await gameServer.Start();

            //Fail to start
            if (p == null)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR] " + gameServer.Error);
                SetServerStatus(server, "Stopped");

                return null;
            }

            _serverMetadata[int.Parse(server.ID)].Process = p;
            p.Exited += (sender, e) => OnGameServerExited(server);

            await Task.Run(() =>
            {
                try
                {
                    if (!p.StartInfo.CreateNoWindow)
                    {
                        while (!p.HasExited && !ShowWindow(p.MainWindowHandle, WindowShowStyle.Minimize))
                        {
                            //Debug.WriteLine("Try Setting ShowMinNoActivate Console Window");
                        }

                        Debug.WriteLine("Set ShowMinNoActivate Console Window");

                        //Save MainWindow
                        _serverMetadata[int.Parse(server.ID)].MainWindow = p.MainWindowHandle;
                    }

                    //Fix for Factorio - The WaitForInputIdle never returns my guess is because Factorio is built
                    //  without a message loop seems to work fine with this commented out
                    //p.WaitForInputIdle();

                    if (!p.StartInfo.CreateNoWindow)
                    {
                        ShowWindow(p.MainWindowHandle, WindowShowStyle.Hide);
                    }
                }
                catch
                {
                    Debug.WriteLine("No Window require to hide");
                }
            });

            //An error may occur on ShowWindow if not adding this
            if (p == null || p.HasExited)
            {
                _serverMetadata[int.Parse(server.ID)].Process = null;

                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR] Exit Code: " + p.ExitCode.ToString());
                SetServerStatus(server, "Stopped");

                return null;
            }

            // Set Priority
            p = Functions.CPU.Priority.SetProcessWithPriority(p, Functions.CPU.Priority.GetPriorityInteger(GetServerMetadata(server.ID).CPUPriority));

            // Set Affinity
            try
            {
                p.ProcessorAffinity = Functions.CPU.Affinity.GetAffinityIntPtr(GetServerMetadata(server.ID).CPUAffinity);
            }
            catch (Exception e)
            {
                Log(server.ID, $"[NOTICE] Fail to set affinity. ({e.Message})");
            }

            // Save Cache
            ServerCache.SavePID(server.ID, p.Id);
            ServerCache.SaveProcessName(server.ID, p.ProcessName);
            ServerCache.SaveWindowsIntPtr(server.ID, GetServerMetadata(server.ID).MainWindow);

            SetWindowText(p.MainWindowHandle, server.Name);

            ShowWindow(p.MainWindowHandle, WindowShowStyle.Hide);

            StartAutoUpdateCheck(server);

            StartRestartCrontabCheck(server);

            StartSendHeartBeat(server);

            StartQuery(server);

            // Auto port-forward UPnP (best-effort, opt-in via configs/portforward.json; master OFF by default)
            try
            {
                var portCfg = new Functions.ServerConfig(server.ID);
                _ = Functions.PortForward.PortForwardManager.OpenForServerAsync(server.ID, server.Game, portCfg.ServerPort, portCfg.ServerQueryPort);
            }
            catch { /* never block the server startup */ }

            if (MahAppSwitch_SendStatistics.IsChecked == true)
            {
                var analytics = new GoogleAnalytics();
                analytics.SendGameServerStart(server.ID, server.Game);
            }

            return gameServer;
        }

        private async Task<bool> Server_BeginStop(ServerTable server, Process p)
        {
            _serverMetadata[int.Parse(server.ID)].Process = null;

            dynamic gameServer = GameServer.Data.Class.Get(server.Game, pluginList: PluginsList);
            await gameServer.Stop(p);

            for (int i = 0; i < 10; i++)
            {
                if (p == null || p.HasExited) { break; }
                await Task.Delay(1000);
            }

            _serverMetadata[int.Parse(server.ID)].ServerConsole.Clear();

            // Save Cache
            ServerCache.SavePID(server.ID, -1);
            ServerCache.SaveProcessName(server.ID, string.Empty);
            ServerCache.SaveWindowsIntPtr(server.ID, (IntPtr)0);

            // Closes the UPnP ports opened for this server (best-effort).
            try { _ = Functions.PortForward.PortForwardManager.CloseForServerAsync(server.ID); } catch { }

            if (p != null && !p.HasExited)
            {
                p.Kill();
                return false;
            }

            return true;
        }

        private async Task<(Process, string, dynamic)> Server_BeginUpdate(ServerTable server, bool silenceCheck, bool forceUpdate, bool validate = false, string custum = null)
        {
            dynamic gameServer = GameServer.Data.Class.Get(server.Game, new ServerConfig(server.ID), PluginsList);

            string localVersion = gameServer.GetLocalBuild();
            if (string.IsNullOrWhiteSpace(localVersion) && !silenceCheck)
            {
                Log(server.ID, $"[NOTICE] {gameServer.Error}");
            }

            string remoteVersion = await gameServer.GetRemoteBuild();
            if (string.IsNullOrWhiteSpace(remoteVersion) && !silenceCheck)
            {
                Log(server.ID, $"[NOTICE] {gameServer.Error}");
            }

            if (!silenceCheck)
            {
                Log(server.ID, $"Checking: Version ({localVersion}) => ({remoteVersion})");
            }

            if ((!string.IsNullOrWhiteSpace(localVersion) && !string.IsNullOrWhiteSpace(remoteVersion) && localVersion != remoteVersion) || forceUpdate)
            {
                try
                {
                    return (await gameServer.Update(validate, custum), remoteVersion, gameServer);
                }
                catch
                {
                    return (await gameServer.Update(), remoteVersion, gameServer);
                }
            }

            return (null, remoteVersion, gameServer);
        }

        #region Actions - Game Server
        // Anti double-start: per-server lock covering the whole operation (including the
        // await Backup/Update on start, a window where two clicks slipped past the status guard).
        private readonly HashSet<string> _startingServers = new HashSet<string>();

        private async Task GameServer_Start(ServerTable server, string notes = "")
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }
            if (!_startingServers.Add(server.ID)) { return; }

            try
            {
            string error = string.Empty;
            if (!string.IsNullOrWhiteSpace(server.IP) && !IsValidIPAddress(server.IP))
            {
                error += " IP address is not valid.";
            }

            if (!string.IsNullOrWhiteSpace(server.Port) && !IsValidPort(server.Port))
            {
                error += " Port number is not valid.";
            }

            if (error != string.Empty)
            {
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR]" + error);

                return;
            }

            Process p = GetServerMetadata(server.ID).Process;
            if (p != null) { return; }

            if (GetServerMetadata(server.ID).BackupOnStart)
            {
                await GameServer_Backup(server, " | Backup on Start");
            }

            if (GetServerMetadata(server.ID).UpdateOnStart)
            {
                await GameServer_Update(server, " | Update on Start");
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Starting;
            Log(server.ID, "Action: Start" + notes);
            SetServerStatus(server, "Starting");

            var gameServer = await Server_BeginStart(server);
            if (gameServer == null)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                SetServerStatus(server, "Stopped");
                return;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
            Log(server.ID, "Server: Started");
            if (!string.IsNullOrWhiteSpace(gameServer.Notice))
            {
                Log(server.ID, "[Notice] " + gameServer.Notice);
            }
            SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());
            }
            finally { _startingServers.Remove(server.ID); }
        }

        private async Task GameServer_Stop(ServerTable server)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Started) { return; }

            Process p = GetServerMetadata(server.ID).Process;
            if (p == null) { return; }

            //Begin stop
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopping;
            Log(server.ID, "Action: Stop");
            SetServerStatus(server, "Stopping");

            bool stopGracefully = await Server_BeginStop(server, p);

            Log(server.ID, "Server: Stopped");
            if (!stopGracefully)
            {
                Log(server.ID, "[NOTICE] Server fail to stop gracefully");
            }
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            SetServerStatus(server, "Stopped");
        }

        private async Task GameServer_Restart(ServerTable server)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Started) { return; }

            Process p = GetServerMetadata(server.ID).Process;
            if (p == null) { return; }

            _serverMetadata[int.Parse(server.ID)].Process = null;

            //Begin Restart
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Restarting;
            Log(server.ID, "Action: Restart");
            SetServerStatus(server, "Restarting");

            await Server_BeginStop(server, p);

            await Task.Delay(1000);

            var gameServer = await Server_BeginStart(server);
            if (gameServer == null)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                SetServerStatus(server, "Stopped");
                return;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
            Log(server.ID, "Server: Restarted");
            if (!string.IsNullOrWhiteSpace(gameServer.Notice))
            {
                Log(server.ID, "[Notice] " + gameServer.Notice);
            }
            SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());
        }

        // P1-1b: clears the SteamCMD caches that cause an "Access Denied / Failed to get manifest"
        // leaving the build stuck (known Palworld trap). Reads/deletes cache files only.
        private void ClearSteamUpdateCaches(string serverId, string appId)
        {
            try
            {
                string steamapps = Path.Combine(ServerPath.GetServersServerFiles(serverId), "steamapps");
                string acf = Path.Combine(steamapps, $"appmanifest_{appId}.acf");
                if (File.Exists(acf)) { File.Delete(acf); }
                foreach (var sub in new[] { "downloading", "temp" })
                {
                    string d = Path.Combine(steamapps, sub);
                    if (Directory.Exists(d)) { Directory.Delete(d, true); }
                }
                string vdf = Path.Combine(ServerPath.GetBin("steamcmd"), "appcache", "appinfo.vdf");
                if (File.Exists(vdf)) { File.Delete(vdf); }
                Log(serverId, $"[Recovery] SteamCMD caches cleared (appmanifest_{appId}.acf, downloading, temp, appinfo.vdf).");
            }
            catch (Exception ex) { Log(serverId, "[Recovery] Cache cleanup failed: " + ex.Message); }
        }

        private async Task<bool> GameServer_Update(ServerTable server, string notes = "", bool validate = false, bool isRetry = false)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            // #20: back up before the update if enabled (not on the internal retry)
            if (!isRetry && GetServerMetadata(server.ID).BackupBeforeUpdate)
            {
                Log(server.ID, "Backup before update…");
                await GameServer_Backup(server, " | Before update");
            }

            //Begin Update
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Updating;
            Log(server.ID, "Action: Update" + notes);
            SetServerStatus(server, "Updating");

            var (p, remoteVersion, gameServer) = await Server_BeginUpdate(server, silenceCheck: validate, forceUpdate: true, validate: validate);

            if (p == null && string.IsNullOrEmpty(gameServer.Error)) // Update success (non-steamcmd server)
            {
                Log(server.ID, $"Server: Updated {(validate ? "Validate " : string.Empty)}({remoteVersion})");
            }
            else if (p != null) // p stores process of steamcmd
            {
                await Task.Run(() => { p.WaitForExit(); });

                // Fix: do not rely on the mere fact that steamcmd finished.
                // SteamCMD can fail silently (e.g. "Access Denied" on the manifest) leaving
                // the build unchanged AND an exit code 0. We RE-READ the actually installed
                // build and only declare "Updated" if it matches the remote build.
                string installedVersion = gameServer.GetLocalBuild();
                bool updateOk = !string.IsNullOrWhiteSpace(installedVersion)
                                && (string.IsNullOrWhiteSpace(remoteVersion) || installedVersion == remoteVersion);
                if (updateOk)
                {
                    Log(server.ID, $"Server: Updated {(validate ? "Validate " : string.Empty)}({installedVersion})");
                    // re-check the update badge after a successful update
                    _remoteBuildCache.Clear(); _remoteBuildCacheTime = DateTime.MinValue;
                    _ = CheckServerUpdatesAsync();
                }
                else
                {
                    // P1-1b: auto-recovery from the "Access Denied / stuck manifest" trap -> clear caches + a single retry
                    string appId = null; try { appId = (string)gameServer.AppId; } catch { appId = null; }
                    if (!isRetry && !string.IsNullOrEmpty(appId))
                    {
                        Log(server.ID, "[Recovery] Build unchanged (Access Denied likely) -> clearing SteamCMD caches and retrying...");
                        ClearSteamUpdateCaches(server.ID, appId);
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped; // required before retry
                        return await GameServer_Update(server, notes + " [retry]", validate, isRetry: true);
                    }
                    Log(server.ID, $"Server: Fail to update (installed build '{installedVersion}' != expected '{remoteVersion}')");
                    Log(server.ID, "[ERROR] SteamCMD finished but the build did not change even after clearing the caches. Check the SteamCMD output.");
                }
            }
            else
            {
                Log(server.ID, "Server: Fail to update");
                Log(server.ID, "[ERROR] " + gameServer.Error);
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            SetServerStatus(server, "Stopped");

            return true;
        }

        private async Task<bool> GameServer_Backup(ServerTable server, string notes = "")
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            //Begin backup
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Backuping;
            Log(server.ID, "Action: Backup" + notes);
            SetServerStatus(server, "Backuping");

            //End All Running Process
            await EndAllRunningProcess(server.ID);
            await Task.Delay(1000);

            string backupLocation = ServerPath.GetBackups(server.ID);
            if (!Directory.Exists(backupLocation))
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to backup");
                Log(server.ID, "[ERROR] Backup location not found");
                SetServerStatus(server, "Stopped");
                return false;
            }

            string zipFileName = $"WGSM-Backup-Server-{server.ID}-";

            // Remove the oldest Backup file
            var backupConfig = new BackupConfig(server.ID);
            foreach (var fi in new DirectoryInfo(backupLocation).GetFiles("*.zip").Where(x => x.Name.Contains(zipFileName)).OrderByDescending(x => x.LastWriteTime).Skip(backupConfig.MaximumBackups - 1))
            {
                string ex = string.Empty;
                await Task.Run(() =>
                {
                    try
                    {
                        fi.Delete();
                    }
                    catch (Exception e)
                    {
                        ex = e.Message;
                    }
                });

                if (ex != string.Empty)
                {
                    _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                    Log(server.ID, "Server: Fail to backup");
                    Log(server.ID, $"[ERROR] {ex}");
                    SetServerStatus(server, "Stopped");
                    return false;
                }
            }

            string startPath = ServerPath.GetServers(server.ID);
            string zipFile = Path.Combine(ServerPath.GetBackups(server.ID), $"{zipFileName}{DateTime.Now.ToString("yyyyMMddHHmmss")}.zip");
            string backupFolders = new Functions.BackupConfig(server.ID).BackupFolders; // #179

            string error = string.Empty;
            await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(backupFolders))
                    {
                        ZipFile.CreateFromDirectory(startPath, zipFile); // the whole server (default behavior)
                    }
                    else
                    {
                        // #179: back up ONLY the chosen subfolders (relative to serverfiles), e.g. "savegame".
                        string sf = ServerPath.GetServersServerFiles(server.ID);
                        using (var zip = ZipFile.Open(zipFile, System.IO.Compression.ZipArchiveMode.Create))
                        {
                            foreach (string rel in backupFolders.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                string folder = Path.Combine(sf, rel.Trim());
                                if (!Directory.Exists(folder)) { continue; }
                                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                                {
                                    string entry = Path.GetRelativePath(startPath, file).Replace('\\', '/');
                                    zip.CreateEntryFromFile(file, entry);
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
            });

            if (error != string.Empty)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to backup");
                Log(server.ID, $"[ERROR] {error}");
                SetServerStatus(server, "Stopped");

                return false;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            Log(server.ID, "Server: Backuped");
            SetServerStatus(server, "Stopped");

            return true;
        }

        private async Task<bool> GameServer_RestoreBackup(ServerTable server, string backupFile)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            string backupLocation = ServerPath.GetBackups(server.ID);
            string backupPath = Path.Combine(backupLocation, backupFile);
            if (!File.Exists(backupPath))
            {
                Log(server.ID, "Server: Fail to restore backup");
                Log(server.ID, "[ERROR] Backup not found");
                return false;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Restoring;
            Log(server.ID, "Action: Restore Backup");
            SetServerStatus(server, "Restoring");

            string extractPath = ServerPath.GetServers(server.ID);
            string safetyPath = extractPath + ".old_restore"; // fallback folder for atomic rollback

            string error = string.Empty;
            await Task.Run(() =>
            {
                try
                {
                    // 1) We SET ASIDE the old folder (rename) instead of deleting it -> allows a rollback.
                    if (Directory.Exists(safetyPath)) { Directory.Delete(safetyPath, true); }
                    if (Directory.Exists(extractPath)) { Directory.Move(extractPath, safetyPath); }

                    // 2) Extract the backup.
                    ZipFile.ExtractToDirectory(backupPath, extractPath);

                    // 3) Success -> delete the old set-aside folder.
                    if (Directory.Exists(safetyPath)) { Directory.Delete(safetyPath, true); }
                }
                catch (Exception e)
                {
                    error = e.Message;
                    // ROLLBACK: extraction failed -> restore the original folder.
                    try
                    {
                        if (Directory.Exists(extractPath)) { Directory.Delete(extractPath, true); }
                        if (Directory.Exists(safetyPath)) { Directory.Move(safetyPath, extractPath); }
                    }
                    catch { /* last resort: keep at least .old_restore on disk */ }
                }
            });

            if (error != string.Empty)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to restore backup (original server restored)");
                Log(server.ID, $"[ERROR] {error}");
                SetServerStatus(server, "Stopped");
                return false;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            Log(server.ID, "Server: Restored");
            SetServerStatus(server, "Stopped");

            return true;
        }

        private async Task<bool> GameServer_Delete(ServerTable server)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            //Begin delete
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Deleting;
            Log(server.ID, "Action: Delete");
            SetServerStatus(server, "Deleting");

            //Remove firewall rule
            var firewall = new WindowsFirewall(null, ServerPath.GetServers(server.ID));
            firewall.RemoveRuleEx();

            //End All Running Process
            await EndAllRunningProcess(server.ID);
            await Task.Delay(1000);

            string serverPath = ServerPath.GetServers(server.ID);

            await Task.Run(() =>
            {
                try
                {
                    if (Directory.Exists(serverPath))
                    {
                        Directory.Delete(serverPath, true);
                    }
                }
                catch
                {

                }
            });

            await Task.Delay(1000);

            if (Directory.Exists(serverPath))
            {
                string wgsmCfgPath = ServerPath.GetServersConfigs(server.ID, "WindowsGSM.cfg");
                if (File.Exists(wgsmCfgPath))
                {
                    Log(server.ID, "Server: Fail to delete server");
                    Log(server.ID, "[ERROR] Directory is not accessible");

                    _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                    SetServerStatus(server, "Stopped");

                    return false;
                }
            }

            Log(server.ID, "Server: Deleted server");

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            SetServerStatus(server, "Stopped");

            LoadServerTable();

            return true;
        }
        #endregion

        // #17: anti crash-loop (N crashes close together -> suspend auto-restart + alert)
        private const int CRASH_LOOP_COUNT = 3;
        private const int CRASH_LOOP_WINDOW_MIN = 5;
        private readonly Dictionary<string, List<DateTime>> _crashTimes = new Dictionary<string, List<DateTime>>();

        private async void OnGameServerExited(ServerTable server)
        {
            if (System.Windows.Application.Current == null) { return; }

            await System.Windows.Application.Current.Dispatcher.Invoke(async () =>
            {
                int serverId = int.Parse(server.ID);

                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                {
                    bool autoRestart = GetServerMetadata(serverId).AutoRestart && !GetServerMetadata(serverId).Maintenance; // #69: no restart while in maintenance

                    // #17: if the server crashes in a loop, suspend auto-restart instead of restarting forever
                    if (autoRestart)
                    {
                        if (!_crashTimes.TryGetValue(server.ID, out var times)) { times = new List<DateTime>(); _crashTimes[server.ID] = times; }
                        var now = DateTime.Now;
                        times.Add(now);
                        times.RemoveAll(t => (now - t).TotalMinutes > CRASH_LOOP_WINDOW_MIN);
                        if (times.Count >= CRASH_LOOP_COUNT)
                        {
                            autoRestart = false; // cut off the restart for this episode
                            times.Clear();
                            Log(server.ID, $"[Anti-crash-loop] {CRASH_LOOP_COUNT} crashes in under {CRASH_LOOP_WINDOW_MIN} min -> auto-restart suspended, intervention required.");
                            if (GetServerMetadata(serverId).DiscordAlert)
                            {
                                try { var wh = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage, g_DonorType); await wh.Send(server.ID, server.Game, "⛔ Crash loop → auto-restart suspended", server.Name, server.IP, server.Port); } catch { }
                            }
                        }
                    }

                    _serverMetadata[int.Parse(server.ID)].ServerStatus = autoRestart ? ServerStatus.Restarting : ServerStatus.Stopped;
                    Log(server.ID, "Server: Crashed");
                    try { var ust = GetUptime(server.ID); ust.Crashes++; ust.Save(server.ID); } catch { } // stats dispo
                    SetServerStatus(server, autoRestart ? "Restarting" : "Stopped");

                    if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).CrashAlert)
                    {
                        var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage, g_DonorType);
                        await webhook.Send(server.ID, server.Game, "Crashed", server.Name, server.IP, server.Port);
                    }

                    _serverMetadata[int.Parse(server.ID)].Process = null;

                    if (autoRestart)
                    {
                        if (GetServerMetadata(server.ID).BackupOnStart)
                        {
                            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                            await GameServer_Backup(server, " | Backup on Start");
                        }

                        if (GetServerMetadata(server.ID).UpdateOnStart)
                        {
                            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                            await GameServer_Update(server, " | Update on Start");
                        }

                        var gameServer = await Server_BeginStart(server);
                        if (gameServer == null)
                        {
                            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                            return;
                        }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                        Log(server.ID, "Server: Started | Auto Restart");
                        if (!string.IsNullOrWhiteSpace(gameServer.Notice))
                        {
                            Log(server.ID, "[Notice] " + gameServer.Notice);
                        }
                        SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());

                        if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).AutoRestartAlert)
                        {
                            var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage, g_DonorType);
                            await webhook.Send(server.ID, server.Game, "Restarted | Auto Restart", server.Name, server.IP, server.Port);
                        }
                    }
                }
            });
        }

        const int UPDATE_INTERVAL_MINUTE = 30;
        private async void StartAutoUpdateCheck(ServerTable server)
        {
            int serverId = int.Parse(server.ID);

            //Save the process of game server
            Process p = GetServerMetadata(server.ID).Process;

            dynamic gameServer = GameServer.Data.Class.Get(server.Game, new ServerConfig(server.ID), PluginsList);

            string localVersion = gameServer.GetLocalBuild();

            while (p != null && !p.HasExited)
            {
                await Task.Delay(60000 * UPDATE_INTERVAL_MINUTE);

                if (!GetServerMetadata(server.ID).AutoUpdate || GetServerMetadata(server.ID).ServerStatus == ServerStatus.Updating)
                {
                    continue;
                }

                if (p == null || p.HasExited) { break; }

                //Try to get local build again if not found just now
                if (string.IsNullOrWhiteSpace(localVersion))
                {
                    localVersion = gameServer.GetLocalBuild();
                }

                //Get remote build
                string remoteVersion = await gameServer.GetRemoteBuild();

                //Continue if success to get localVersion and remoteVersion
                if (!string.IsNullOrWhiteSpace(localVersion) && !string.IsNullOrWhiteSpace(remoteVersion))
                {
                    if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Started)
                    {
                        break;
                    }

                    Log(server.ID, $"Checking: Version ({localVersion}) => ({remoteVersion})");

                    if (localVersion != remoteVersion)
                    {
                        _serverMetadata[int.Parse(server.ID)].Process = null;

                        //Begin stop
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopping;
                        SetServerStatus(server, "Stopping");

                        //Stop the server
                        await Server_BeginStop(server, p);

                        if (p != null && !p.HasExited)
                        {
                            p.Kill();
                        }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Updating;
                        SetServerStatus(server, "Updating");

                        //Update the server
                        await gameServer.Update();

                        // Fix: re-read the installed build to confirm real success.
                        // gameServer.Error stays empty on a silent steamcmd failure -> we
                        // compare the effective local build to the remote build before declaring "Updated".
                        string installedVersion = gameServer.GetLocalBuild();
                        bool updateOk = string.IsNullOrWhiteSpace(gameServer.Error)
                                        && !string.IsNullOrWhiteSpace(installedVersion)
                                        && installedVersion == remoteVersion;

                        if (updateOk)
                        {
                            Log(server.ID, $"Server: Updated ({installedVersion})");

                            if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).AutoUpdateAlert)
                            {
                                var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage, g_DonorType);
                                await webhook.Send(server.ID, server.Game, "Updated | Auto Update", server.Name, server.IP, server.Port);
                            }
                        }
                        else
                        {
                            Log(server.ID, $"Server: Fail to update (installed build '{installedVersion}' != expected '{remoteVersion}')");
                            Log(server.ID, "[ERROR] " + (string.IsNullOrWhiteSpace(gameServer.Error) ? "SteamCMD finished but the build did not change (e.g. 'Access Denied' on the manifest)." : gameServer.Error));
                        }

                        //Start the server
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Starting;
                        SetServerStatus(server, "Starting");

                        var gameServerStart = await Server_BeginStart(server);
                        if (gameServerStart == null) { return; }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                        SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());

                        break;
                    }
                }
                else if (string.IsNullOrWhiteSpace(localVersion))
                {
                    Log(server.ID, $"[NOTICE] Fail to get local build.");
                }
                else if (string.IsNullOrWhiteSpace(remoteVersion))
                {
                    Log(server.ID, $"[NOTICE] Fail to get remote build.");
                }
            }
        }

        private async void StartRestartCrontabCheck(ServerTable server)
        {
            int serverId = int.Parse(server.ID);

            //Save the process of game server
            Process p = GetServerMetadata(server.ID).Process;

            while (p != null && !p.HasExited)
            {
                //If not enable return
                if (!GetServerMetadata(serverId).RestartCrontab)
                {
                    await Task.Delay(1000);

                    continue;
                }

                //Try get next DataTime restart
                DateTime? crontabTime = CrontabSchedule.TryParse(GetServerMetadata(serverId).CrontabFormat)?.GetNextOccurrence(DateTime.Now);

                //Delay 1 second for later compare
                await Task.Delay(1000);

                //Return if crontab expression is invalid
                if (crontabTime == null) { continue; }

                //If now >= crontab time
                if (DateTime.Compare(DateTime.Now, crontabTime ?? DateTime.Now) >= 0)
                {
                    //Update the next crontab
                    var currentRow = (ServerTable)ServerGrid.SelectedItem;
                    if (currentRow.ID == server.ID)
                    {
                        textBox_nextcrontab.Text = CrontabSchedule.TryParse(GetServerMetadata(serverId).CrontabFormat)?.GetNextOccurrence(DateTime.Now).ToString("ddd, MM/dd/yyyy HH:mm:ss");
                    }

                    if (p == null || p.HasExited)
                    {
                        break;
                    }

                    //Restart the server
                    if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                    {
                        _serverMetadata[int.Parse(server.ID)].Process = null;

                        //Begin Restart
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Restarting;
                        Log(server.ID, "Action: Restart");
                        SetServerStatus(server, "Restarting");

                        await Server_BeginStop(server, p);
                        var gameServer = await Server_BeginStart(server);
                        if (gameServer == null) { return; }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                        Log(server.ID, "Server: Restarted | Restart Crontab");
                        if (!string.IsNullOrWhiteSpace(gameServer.Notice))
                        {
                            Log(server.ID, "[Notice] " + gameServer.Notice);
                        }
                        SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());

                        if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).RestartCrontabAlert)
                        {
                            var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage, g_DonorType);
                            await webhook.Send(server.ID, server.Game, "Restarted | Restart Crontab", server.Name, server.IP, server.Port);
                        }

                        break;
                    }
                }
            }
        }

        private async void StartSendHeartBeat(ServerTable server)
        {
            //Save the process of game server
            Process p = GetServerMetadata(server.ID).Process;

            while (p != null && !p.HasExited)
            {
                if (MahAppSwitch_SendStatistics.IsChecked == true)
                {
                    var analytics = new GoogleAnalytics();
                    analytics.SendGameServerHeartBeat(server.Game, server.Name);
                }

                await Task.Delay(300000);
            }
        }

        private async void StartQuery(ServerTable server)
        {
            if (string.IsNullOrWhiteSpace(server.IP) || string.IsNullOrWhiteSpace(server.QueryPort)) { return; }

            // Check the server support Query Method
            dynamic gameServer = GameServer.Data.Class.Get(server.Game, pluginList: PluginsList);
            if (gameServer == null) { return; }
            if (gameServer.QueryMethod == null) { return; }

            // Save the process of game server
            Process p = GetServerMetadata(server.ID).Process;

            // Query server every 5 seconds
            while (p != null && !p.HasExited)
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped)
                {
                    break;
                }

                if (!IsValidIPAddress(server.IP) || !IsValidPort(server.QueryPort))
                {
                    await Task.Delay(5000); // avoids a tight loop (CPU 100%) while the address is invalid
                    continue;
                }

                string players = null;
                string liveMap = null;
                try
                {
                    dynamic query = gameServer.QueryMethod;
                    query.SetAddressPort(server.IP, int.Parse(server.QueryPort));
                    players = await query.GetPlayersAndMaxPlayers();
                    // #61: fetch the CURRENT map (best-effort). GetInfo only exists on the native A2S query;
                    // for a plugin query without GetInfo, the dynamic call throws -> caught, no map (harmless).
                    try { var info = await query.GetInfo(); if (info != null) { liveMap = info["Map"] as string; } } catch { }
                }
                catch { players = null; } // third-party plugin code: must never kill the loop/app

                if (players != null)
                {
                    // Maxplayers/Defaultmap are notifying now, so assigning them updates the bound cells
                    // directly — no ServerGrid.Items[i]=server + Items.Refresh() (full grid rebind) needed.
                    server.Maxplayers = players;
                    if (!string.IsNullOrWhiteSpace(liveMap)) { server.Defaultmap = liveMap; } // #61 : map live (TF2/Source)
                }

                await Task.Delay(5000);
            }
        }

        private async Task EndAllRunningProcess(string serverId)
        {
            await Task.Run(() =>
            {
                //LINQ query for windowsgsm old processes
                // Process.GetProcesses() returns a Process (native handle) for EVERY OS process; they must
                // all be Disposed, not just the ones we kill, otherwise each call leaks dozens of handles.
                var all = Process.GetProcesses();
                try
                {
                    string needle = Path.Combine(WGSM_PATH, "servers", serverId) + "\\";
                    foreach (var process in all)
                    {
                        try
                        {
                            if (process.MainModule.FileName.Contains(needle))
                            {
                                try { process.Kill(); } catch { /* ignore */ }
                            }
                        }
                        catch { /* MainModule access denied for system processes */ }
                    }
                }
                finally
                {
                    foreach (var process in all) { try { process.Dispose(); } catch { } }
                }
            });
        }

        private void SetServerStatus(ServerTable server, string status, string pid = null)
        {
            server.Status = status;
            if (pid != null)
            {
                server.PID = pid;
            }
            if (status == "Stopped")
            {
                server.PID = string.Empty;
            }

            if (server.Status != "Started" && server.Maxplayers.Contains('/'))
            {
                var serverConfig = new ServerConfig(server.ID);
                server.Maxplayers = serverConfig.ServerMaxPlayer;
            }

            // Status/PID/Maxplayers are notifying now, so the grid cells (and the StatusDisplay column)
            // update on assignment — no ServerGrid.Items[i]=server + Items.Refresh() full rebind needed.
            DataGrid_RefreshElements();
        }

        public void Log(string serverId, string logText)
        {
            string title = int.TryParse(serverId, out int i) ? $"#{i.ToString()}" : serverId;
            string log = $"[{DateTime.Now.ToString("MM/dd/yyyy-HH:mm:ss")}][{title}] {logText}" + Environment.NewLine;
            string logPath = ServerPath.GetLogs();
            Directory.CreateDirectory(logPath);

            string logFile = Path.Combine(logPath, $"L{DateTime.Now.ToString("yyyyMMdd")}.log");
            File.AppendAllText(logFile, log);

            textBox_wgsmlog.AppendText(log);
            textBox_wgsmlog.Text = RemovedOldLog(textBox_wgsmlog.Text);
            textBox_wgsmlog.ScrollToEnd();
        }

        public void DiscordBotLog(string logText)
        {
            string log = $"[{DateTime.Now.ToString("MM/dd/yyyy-HH:mm:ss")}] {logText}" + Environment.NewLine;
            string logPath = ServerPath.GetLogs();
            Directory.CreateDirectory(logPath);

            string logFile = Path.Combine(logPath, $"L{DateTime.Now.ToString("yyyyMMdd")}-DiscordBot.log");
            File.AppendAllText(logFile, log);

            textBox_DiscordBotLog.AppendText(log);
            textBox_DiscordBotLog.Text = RemovedOldLog(textBox_DiscordBotLog.Text);
            textBox_DiscordBotLog.ScrollToEnd();
        }

        private string RemovedOldLog(string logText)
        {
            const int MAX_LOG_LINE = 50;
            int lineCount = logText.Count(f => f == '\n');
            return (lineCount > MAX_LOG_LINE) ? string.Join("\n", logText.Split('\n').Skip(lineCount - MAX_LOG_LINE).ToArray()) : logText;
        }

        private void Button_ClearServerConsole_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            _serverMetadata[int.Parse(server.ID)].ServerConsole.Clear();
            console.Clear();
        }

        private void Button_ClearWGSMLog_Click(object sender, RoutedEventArgs e)
        {
            textBox_wgsmlog.Clear();
        }

        private void SendCommand(ServerTable server, string command)
        {
            Process p = GetServerMetadata(server.ID).Process;
            if (p == null) { return; }

            // 7 Days to Die: stdin is not redirected and the old SendKeys hack fails on a minimized /
            // output-redirected process, so the console command box never actually reached the server.
            // Route commands through the Telnet console instead (all console commands are available there).
            if (server.Game == GameServer.SDTD.FullName)
            {
                _ = SendSevenDaysTelnetCommand(server, command);
                return;
            }

            textbox_servercommand.Focusable = false;
            _serverMetadata[int.Parse(server.ID)].ServerConsole.Input(p, command, GetServerMetadata(server.ID).MainWindow);
            textbox_servercommand.Focusable = true;
        }

        // Sends a 7DtD console command over the PERSISTENT Telnet session (one connection per server,
        // reused across commands). The response streams back through that session's reader into the panel.
        private async Task SendSevenDaysTelnetCommand(ServerTable server, string command)
        {
            var console = _serverMetadata[int.Parse(server.ID)].ServerConsole;
            var (enabled, tport, tpwd, _) = ReadSevenDaysTelnet(server.ID);
            if (!enabled || tport <= 0)
            {
                console.Add("[WindowsGSM] Telnet is disabled in serverconfig.xml — cannot send commands. Set <property name=\"TelnetEnabled\" value=\"true\"/> (+ a TelnetPassword) and restart the server.");
                return;
            }

            console.Add("> " + command);
            if (!await Functions.SevenDaysTelnet.EnsureAsync(server.ID, server.IP, tport, tpwd, console).ConfigureAwait(true))
            {
                console.Add("[WindowsGSM] Could not reach the Telnet console (check TelnetPort / TelnetPassword).");
                return;
            }
            if (!await Functions.SevenDaysTelnet.SendAsync(server.ID, command).ConfigureAwait(true))
            {
                console.Add("[WindowsGSM] Telnet connection lost — please retry.");
            }
        }

        private static bool IsValidIPAddress(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                return false;
            }

            string[] splitValues = ip.Split('.');
            if (splitValues.Length != 4)
            {
                return false;
            }

            return splitValues.All(r => byte.TryParse(r, out byte tempForParsing));
        }

        private static bool IsValidPort(string port)
        {
            if (!int.TryParse(port, out int portnum))
            {
                return false;
            }

            return portnum > 1 && portnum < 65535;
        }

        #region Menu - Browse
        private void Browse_ServerBackups_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            Shell.Open(Functions.ServerPath.GetBackups(server.ID));
        }

        private void Browse_BackupFiles_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            var backupConfig = new Functions.BackupConfig(server.ID);
            backupConfig.Open();
        }

        // #22: per-server action log (filters the WindowsGSM logs by [#id]).
        private async void Browse_ActionHistory_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { await this.ShowMessageAsync(Loc.T("Msg.ActionHistoryTitle"), Loc.T("Msg.SelectServerFirst")); return; }

            var lines = new System.Collections.Generic.List<string>();
            try
            {
                string logsDir = ServerPath.GetLogs();
                if (Directory.Exists(logsDir))
                {
                    string tag = $"[#{server.ID}]";
                    var files = Directory.GetFiles(logsDir, "L*.log").OrderBy(f => f).ToList();
                    foreach (var f in files.Skip(Math.Max(0, files.Count - 7))) // ~last 7 days
                    {
                        try { foreach (var ln in File.ReadAllLines(f)) { if (ln.Contains(tag)) { lines.Add(ln); } } } catch { }
                    }
                }
            }
            catch { }

            string text = (lines.Count == 0)
                ? Loc.T("Msg.NoActionRecorded")
                : string.Join("\n", lines.Skip(Math.Max(0, lines.Count - 50))); // last 50
            await this.ShowMessageAsync(Loc.T("Msg.HistoryTitle", server.ID, server.Name), text);
        }

        // Uptime stats for the selected server
        private async void Browse_Uptime_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { await this.ShowMessageAsync(Loc.T("Msg.UptimeTitle"), Loc.T("Msg.SelectServerFirst")); return; }
            var st = GetUptime(server.ID);
            string msg = Loc.T("Msg.UptimeBody", st.TrackedSince.ToString("dd/MM/yyyy HH:mm"), st.Starts, st.Crashes, st.OnlineTimeString(), st.AvailabilityPercent().ToString("0.0"));
            await this.ShowMessageAsync(Loc.T("Msg.UptimeStatsTitle", server.ID, server.Name), msg);
        }

        private void Browse_ServerConfigs_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string path = Functions.ServerPath.GetServersConfigs(server.ID);
            if (Directory.Exists(path))
            {
                Shell.Open(path);
            }
        }

        private void Browse_ServerFiles_Click(object sender, RoutedEventArgs e)
        {
            var server = (Functions.ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string path = Functions.ServerPath.GetServersServerFiles(server.ID);
            if (Directory.Exists(path))
            {
                Shell.Open(path);
            }
        }
        #endregion

        #region Top Bar Button
        private void Button_Website_Click(object sender, RoutedEventArgs e)
        {
            Shell.Open("https://windowsgsm.com/");
        }

        private void Button_Discord_Click(object sender, RoutedEventArgs e)
        {
            Shell.Open("https://discord.gg/bGc7t2R");
        }

        private void Button_Patreon_Click(object sender, RoutedEventArgs e)
        {
            Shell.Open("https://www.patreon.com/WindowsGSM/");
        }

        private void Button_Settings_Click(object sender, RoutedEventArgs e)
        {
            ToggleMahappFlyout(MahAppFlyout_Settings);
        }

        private void Button_Hide_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            notifyIcon.Visible = true;
            notifyIcon.ShowBalloonTip(0);
            notifyIcon.Visible = false;
            notifyIcon.Visible = true;
        }
        #endregion

        #region Settings Flyout
        private void HardWareAcceleration_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue(RegistryKeyName.HardWareAcceleration, (MahAppSwitch_HardWareAcceleration.IsChecked == true).ToString());
            }

            RenderOptions.ProcessRenderMode = MahAppSwitch_HardWareAcceleration.IsChecked == true ? System.Windows.Interop.RenderMode.SoftwareOnly : System.Windows.Interop.RenderMode.Default;
        }

        private void UIAnimation_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue(RegistryKeyName.UIAnimation, (MahAppSwitch_UIAnimation.IsChecked == true).ToString());
            }

            // WindowTransitionsEnabled removed (FluentWindow)
        }

        private void DarkTheme_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue(RegistryKeyName.DarkTheme, (MahAppSwitch_DarkTheme.IsChecked == true).ToString());
            }

            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(MahAppSwitch_DarkTheme.IsChecked == true ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
        }

        private void StartOnLogin_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue(RegistryKeyName.StartOnBoot, (MahAppSwitch_StartOnBoot.IsChecked == true).ToString());
            }

            SetStartOnBoot(MahAppSwitch_StartOnBoot.IsChecked == true);
        }

        private void RestartOnCrash_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue(RegistryKeyName.RestartOnCrash, (MahAppSwitch_RestartOnCrash.IsChecked == true).ToString());
            }
        }

        private void SendStatistics_IsCheckedChanged(object sender, EventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue(RegistryKeyName.SendStatistics, (MahAppSwitch_SendStatistics.IsChecked == true).ToString());
            }
        }


        private void SetStartOnBoot(bool enable)
        {
            string taskName = "WindowsGSM";
            string wgsmPath = Process.GetCurrentProcess().MainModule.FileName;

            Process schtasks = new Process
            {
                StartInfo =
                {
                    FileName = "schtasks",
                    Arguments = enable ? $"/create /tn {taskName} /tr \"{wgsmPath}\" /sc onlogon /rl HIGHEST /f" : $"/delete /tn {taskName} /f",
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            };
            schtasks.Start();
        }
        #endregion

        #region Donor Connect
        private async void DonorConnect_IsCheckedChanged(object sender, EventArgs e)
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true);

            //If switch is checked
            if (!MahAppSwitch_DonorConnect.IsChecked == true)
            {
                g_DonorType = string.Empty;
                Functions.Donator.DonatorManager.AuthorDonorActive = false;
                comboBox_Themes.SelectedItem = DEFAULT_THEME;
                comboBox_Themes.IsEnabled = false;

                //Set theme
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(MahAppSwitch_DarkTheme.IsChecked == true ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);

                key.SetValue(RegistryKeyName.DonorTheme, (MahAppSwitch_DonorConnect.IsChecked == true).ToString());
                key.SetValue(RegistryKeyName.DonorColor, DEFAULT_THEME);
                key.Close();
                return;
            }

            //If switch is not checked
            key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true);
            string authKey = (key.GetValue(RegistryKeyName.DonorAuthKey) == null) ? string.Empty : key.GetValue(RegistryKeyName.DonorAuthKey).ToString();

            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = Loc.T("Msg.ActivateButton"),
                DefaultText = authKey
            };

            authKey = await this.ShowInputAsync(Loc.T("Msg.DonorConnectTitle"), Loc.T("Msg.DonorConnectPrompt"), settings);

            //If pressed cancel or key is null or whitespace
            if (string.IsNullOrWhiteSpace(authKey))
            {
                MahAppSwitch_DonorConnect.IsChecked = false;
                key.Close();
                return;
            }

            var controller = await this.ShowProgressAsync("Authenticating...", "Please wait...");
            controller.SetIndeterminate();
            (bool success, string name) = await AuthenticateDonor(authKey);
            await controller.CloseAsync();

            if (success)
            {
                key.SetValue(RegistryKeyName.DonorTheme, "True");
                key.SetValue(RegistryKeyName.DonorAuthKey, authKey);
                await this.ShowMessageAsync(Loc.T("Msg.DonorSuccessTitle"), Loc.T("Msg.DonorSuccessBody", name));
            }
            else
            {
                key.SetValue(RegistryKeyName.DonorTheme, "False");
                key.SetValue(RegistryKeyName.DonorAuthKey, "");
                await this.ShowMessageAsync(Loc.T("Msg.DonorFailTitle"), Loc.T("Msg.DonorFailBody"));

                MahAppSwitch_DonorConnect.IsChecked = false;
            }
            key.Close();
        }

        private async Task<(bool, string)> AuthenticateDonor(string authKey)
        {
            try
            {
                using (WebClient webClient = new WebClient())
                {
                    string json = await webClient.DownloadStringTaskAsync($"https://windowsgsm.com/patreon/patreonAuth.php?auth={authKey}");
                    bool success = JObject.Parse(json)["success"].ToString() == "True";

                    if (success)
                    {
                        string name = JObject.Parse(json)["name"].ToString();
                        string type = JObject.Parse(json)["type"].ToString();

                        g_DonorType = type;
                        g_DiscordBot.SetDonorType(g_DonorType);
                        Functions.Donator.DonatorManager.AuthorDonorActive = true; // donateur Patreon de l'auteur
                        comboBox_Themes.IsEnabled = true;

                        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(MahAppSwitch_DarkTheme.IsChecked == true ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);

                        return (true, name);
                    }

                    MahAppSwitch_DonorConnect.IsChecked = false;

                    //Set theme
                    Wpf.Ui.Appearance.ApplicationThemeManager.Apply(MahAppSwitch_DarkTheme.IsChecked == true ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
                }
            }
            catch
            {
                // ignore
            }

            //Set theme
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(MahAppSwitch_DarkTheme.IsChecked == true ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);

            return (false, string.Empty);
        }

        private void ComboBox_Themes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue(RegistryKeyName.DonorColor, comboBox_Themes.SelectedItem.ToString());
            }

            //Set theme
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(MahAppSwitch_DarkTheme.IsChecked == true ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
        }

        private void ComboBox_Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(comboBox_Language.SelectedItem is System.Windows.Controls.ComboBoxItem item) || !(item.Tag is string code)) { return; }
            if (code == Functions.Localization.Loc.Lang) { return; } // no-op (incl. initial selection)
            Functions.Localization.Loc.SetLang(code);
            // Dialogs read Loc at open time, so newly-opened dialogs are already localized; a restart applies it everywhere.
            try { new Wpf.Ui.Controls.MessageBox { Title = "WindowsGSM", Content = Loc.T("Msg.LanguageChanged") }.ShowDialogAsync(); } catch { }
        }
        #endregion

        #region Menu - Help
        private void Help_OnlineDocumentation_Click(object sender, RoutedEventArgs e)
        {
            Shell.Open("https://docs.windowsgsm.com");
        }

        private void Help_ReportIssue_Click(object sender, RoutedEventArgs e)
        {
            Shell.Open("https://github.com/WindowsGSM/WindowsGSM/issues");
        }

        private async void Help_SoftwareUpdates_Click(object sender, RoutedEventArgs e)
        {
            var controller = await this.ShowProgressAsync("Checking updates...", "Please wait...");
            controller.SetIndeterminate();
            string latestVersion = await GetLatestVersion();
            await controller.CloseAsync();

            if (string.IsNullOrEmpty(latestVersion))
            {
                await this.ShowMessageAsync(Loc.T("Msg.SoftwareUpdatesTitle"), Loc.T("Msg.UpdateCheckFailed"));
                return;
            }

            if (latestVersion == WGSM_VERSION)
            {
                await this.ShowMessageAsync(Loc.T("Msg.SoftwareUpdatesTitle"), Loc.T("Msg.UpToDate"));
                return;
            }

            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = Loc.T("Msg.UpdateButton"),
                DefaultButtonFocus = MessageDialogResult.Affirmative
            };

            var result = await this.ShowMessageAsync(Loc.T("Msg.SoftwareUpdatesTitle"), Loc.T("Msg.UpdateAvailable", latestVersion), MessageDialogStyle.AffirmativeAndNegative, settings);

            if (result.ToString().Equals("Affirmative"))
            {
                string installPath = ServerPath.GetBin();
                Directory.CreateDirectory(installPath);

                string filePath = Path.Combine(installPath, "WindowsGSM-Updater.exe");

                if (!File.Exists(filePath))
                {
                    //Download WindowsGSM-Updater.exe
                    controller = await this.ShowProgressAsync("Downloading WindowsGSM-Updater...", "Please wait...");
                    controller.SetIndeterminate();
                    bool success = await DownloadWindowsGSMUpdater();
                    await controller.CloseAsync();
                }

                if (File.Exists(filePath))
                {
                    //Kill all the server
                    for (int i = 0; i <= MAX_SERVER; i++)
                    {
                        if (GetServerMetadata(i) == null || GetServerMetadata(i).Process == null)
                        {
                            continue;
                        }

                        if (!GetServerMetadata(i).Process.HasExited)
                        {
                            _serverMetadata[i].Process.Kill();
                        }
                    }

                    //Run WindowsGSM-Updater.exe
                    Process updater = new Process
                    {
                        StartInfo =
                        {
                            WorkingDirectory = installPath,
                            FileName = filePath,
                            Arguments = "-autostart -forceupdate"
                        }
                    };
                    updater.Start();

                    Close();
                }
                else
                {
                    await this.ShowMessageAsync(Loc.T("Msg.SoftwareUpdatesTitle"), Loc.T("Msg.UpdaterDownloadFailed"));
                }
            }
        }

        private async Task<string> GetLatestVersion()
        {
            try
            {
                var webRequest = WebRequest.Create("https://api.github.com/repos/WindowsGSM/WindowsGSM/releases/latest") as HttpWebRequest;
                webRequest.Method = "GET";
                webRequest.UserAgent = "Anything";
                webRequest.ServicePoint.Expect100Continue = false;
                var response = await webRequest.GetResponseAsync();
                using (var responseReader = new StreamReader(response.GetResponseStream()))
                    return JObject.Parse(responseReader.ReadToEnd())["tag_name"].ToString();
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> DownloadWindowsGSMUpdater()
        {
            string filePath = ServerPath.GetBin("WindowsGSM-Updater.exe");

            try
            {
                using (WebClient webClient = new WebClient())
                {
                    await webClient.DownloadFileTaskAsync("https://github.com/WindowsGSM/WindowsGSM-Updater/releases/latest/download/WindowsGSM-Updater.exe", filePath);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"Github.WindowsGSM-Updater.exe {e}");
            }

            return File.Exists(filePath);
        }

        private async void Help_AboutWindowsGSM_Click(object sender, RoutedEventArgs e)
        {
            var settings = new MetroDialogSettings
            {
                AffirmativeButtonText = Loc.T("Msg.PatreonButton"),
                NegativeButtonText = Loc.T("Msg.OkButton"),
                DefaultButtonFocus = MessageDialogResult.Negative
            };

            var result = await this.ShowMessageAsync(Loc.T("Msg.AboutTitle"), Loc.T("Msg.AboutBody", WGSM_VERSION.Substring(1)), MessageDialogStyle.AffirmativeAndNegative, settings);

            if (result == MessageDialogResult.Affirmative)
            {
                Shell.Open("https://www.patreon.com/WindowsGSM/");
            }
        }
        #endregion

        #region Menu - Tools
        private void Tools_PortForward_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var servers = new List<Functions.PortForward.PortForwardDialog.ServerInfo>();
                foreach (var row in ServerGrid.Items.Cast<ServerTable>().ToList())
                {
                    try
                    {
                        var sc = new Functions.ServerConfig(row.ID);
                        servers.Add(new Functions.PortForward.PortForwardDialog.ServerInfo
                        {
                            Id = row.ID, Name = row.Name, Game = row.Game, Port = sc.ServerPort, QueryPort = sc.ServerQueryPort
                        });
                    }
                    catch { }
                }
                new Functions.PortForward.PortForwardDialog(servers) { Owner = this }.ShowDialog();
            }
            catch (Exception ex) { Functions.AppLog.Warn("PortForward/UI", ex.Message); }
        }

        private void Tools_ConfigEditor_Click(object sender, RoutedEventArgs e)
        {
            var row = ServerGrid.SelectedItem as ServerTable;
            if (row == null)
            {
                System.Windows.MessageBox.Show(Loc.T("Msg.SelectServerFromList"), Loc.T("Msg.ConfigEditorTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                string serverFiles = Functions.ServerPath.GetServersServerFiles(row.ID);
                new Functions.ConfigEditor.ConfigEditorDialog(row.ID, row.Name, row.Game, serverFiles) { Owner = this }.ShowDialog();
            }
            catch (Exception ex) { Functions.AppLog.Warn("ConfigEditor/UI", ex.Message); }
        }

        private void Tools_PalworldAdmin_Click(object sender, RoutedEventArgs e)
        {
            var row = ServerGrid.SelectedItem as ServerTable;
            if (row == null)
            {
                System.Windows.MessageBox.Show(Loc.T("Msg.SelectServerFromList"), Loc.T("PalAdmin.MenuOpen"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrEmpty(row.Game) || !row.Game.StartsWith("Palworld", StringComparison.OrdinalIgnoreCase))
            {
                System.Windows.MessageBox.Show(Loc.T("PalAdmin.NotEnabled"), Loc.T("PalAdmin.MenuOpen"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var (enabled, restPort, pwd) = ReadPalworldRest(row.ID);
            if (!enabled || restPort <= 0 || string.IsNullOrEmpty(pwd))
            {
                System.Windows.MessageBox.Show(Loc.T("PalAdmin.NotEnabled"), Loc.T("PalAdmin.MenuOpen"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                int rconPort = ReadPalworldRconPort(row.ID);
                new Functions.Palworld.PalworldAdminDialog(row.Name, row.IP, restPort, pwd, rconPort) { Owner = this }.ShowDialog();
            }
            catch (Exception ex) { Functions.AppLog.Warn("PalworldAdmin/UI", ex.Message); }
        }

        private void Tools_Mods_Click(object sender, RoutedEventArgs e)
        {
            var row = ServerGrid.SelectedItem as ServerTable;
            if (row == null)
            {
                System.Windows.MessageBox.Show(Loc.T("Msg.SelectServerFromList"), Loc.T("Msg.ModsTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                string serverFiles = Functions.ServerPath.GetServersServerFiles(row.ID);
                new Functions.Mods.ModsDialog(row.ID, row.Name, row.Game, serverFiles) { Owner = this }.ShowDialog();
            }
            catch (Exception ex) { Functions.AppLog.Warn("Mods/UI", ex.Message); }
        }

        private void Nav_Notifications_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Functions.Donator.DonatorManager.IsDonator)
                {
                    var d = new Functions.Donator.DonatorDialog("Multi-channel notifications") { Owner = this };
                    if (d.ShowDialog() != true) { return; } // not unlocked
                }
                new Functions.Notifications.NotificationsDialog { Owner = this }.ShowDialog();
            }
            catch (Exception ex) { Functions.AppLog.Warn("Notifications/UI", ex.Message); }
        }

        private void Nav_WebApi_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var servers = ServerGrid.Items.Cast<ServerTable>().Select(s => (s.ID, s.Name)).ToList();
                new Functions.WebApi.WebApiDialog(StartWebApi, servers) { Owner = this }.ShowDialog();
            }
            catch (Exception ex) { Functions.AppLog.Warn("WebApi/UI", ex.Message); }
        }

        private void Tools_ServerDoctor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var servers = new List<Functions.Doctor.ServerDoctorDialog.ServerInfo>();
                foreach (var row in ServerGrid.Items.Cast<ServerTable>().ToList())
                {
                    try
                    {
                        var sc = new Functions.ServerConfig(row.ID);
                        var meta = GetServerMetadata(row.ID);
                        bool running = meta != null && meta.ServerStatus == ServerStatus.Started;
                        servers.Add(new Functions.Doctor.ServerDoctorDialog.ServerInfo
                        {
                            Id = row.ID, Name = row.Name, Game = row.Game, Port = sc.ServerPort, Query = sc.ServerQueryPort, Running = running
                        });
                    }
                    catch { }
                }
                // No native MessageBox: the dialog shows the empty state itself (consistent with the theme).
                string sel = (ServerGrid.SelectedItem as ServerTable)?.ID;
                new Functions.Doctor.ServerDoctorDialog(servers, sel) { Owner = this }.ShowDialog();
            }
            catch (Exception ex) { Functions.AppLog.Warn("Doctor/UI", ex.Message); }
        }

        private void Tools_ApiToken_Click(object sender, RoutedEventArgs e)
        {
            var row = (ServerTable)ServerGrid.SelectedItem;
            if (row == null)
            {
                System.Windows.MessageBox.Show(Loc.T("Msg.SelectServerFromList"), Loc.T("Msg.ApiTokenTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool isSatisfactory = (row.Game ?? string.Empty).StartsWith("Satisfactory", StringComparison.OrdinalIgnoreCase);
            var dlg = new Functions.ApiTokenDialog(row.ID, row.Name, isSatisfactory) { Owner = this };
            dlg.ShowDialog();
        }

        private void Tools_GlobalServerListCheck_Click(object sender, RoutedEventArgs e)
        {
            var row = (ServerTable)ServerGrid.SelectedItem;
            if (row == null) { return; }

            if (row.Game == GameServer.MCPE.FullName || row.Game == GameServer.MC.FullName)
            {
                Log(row.ID, $"This feature is not applicable on {row.Game}");
                return;
            }

            string publicIP = GetPublicIP();
            if (publicIP == null)
            {
                Log(row.ID, "Fail to check. Reason: Fail to get the public ip.");
                return;
            }

            string messageText = $"Server Name: {row.Name}\nPublic IP: {publicIP}\nQuery Port: {row.QueryPort}";
            if (GlobalServerList.IsServerOnSteamServerList(publicIP, row.QueryPort))
            {
                MessageBox.Show(Loc.T("Msg.GlobalListOnline", messageText), Loc.T("Msg.GlobalListTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(Loc.T("Msg.GlobalListOffline", messageText), Loc.T("Msg.GlobalListTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Tool_InstallAMXModXMetamodP_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            bool? existed = InstallAddons.IsAMXModXAndMetaModPExists(server);
            if (existed == null)
            {
                await this.ShowMessageAsync(Loc.T("Msg.ToolAmxTitle"), Loc.T("Msg.AddonNotSupported", server.Game, server.ID));
                return;
            }

            if (existed == true)
            {
                await this.ShowMessageAsync(Loc.T("Msg.ToolAmxTitle"), Loc.T("Msg.AddonAlreadyInstalled", server.ID));
                return;
            }

            var result = await this.ShowMessageAsync(Loc.T("Msg.ToolAmxTitle"), Loc.T("Msg.AddonConfirmInstall", server.ID), MessageDialogStyle.AffirmativeAndNegative);
            if (result == MessageDialogResult.Affirmative)
            {
                var controller = await this.ShowProgressAsync("Installing...", "Please wait...");
                controller.SetIndeterminate();
                bool installed = await InstallAddons.AMXModXAndMetaModP(server);
                await controller.CloseAsync();

                string message = installed ? Loc.T("Msg.AddonInstalledOk") : Loc.T("Msg.AddonInstallFail");
                await this.ShowMessageAsync(Loc.T("Msg.ToolAmxTitle"), Loc.T("Msg.AddonResult", message, server.ID));
            }
        }

        private async void Tools_InstallSourcemodMetamod_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            bool? existed = InstallAddons.IsSourceModAndMetaModExists(server);
            if (existed == null)
            {
                await this.ShowMessageAsync(Loc.T("Msg.ToolSourceModTitle"), Loc.T("Msg.AddonNotSupported", server.Game, server.ID));
                return;
            }

            if (existed == true)
            {
                await this.ShowMessageAsync(Loc.T("Msg.ToolSourceModTitle"), Loc.T("Msg.AddonAlreadyInstalled", server.ID));
                return;
            }

            var result = await this.ShowMessageAsync(Loc.T("Msg.ToolSourceModTitle"), Loc.T("Msg.AddonConfirmInstall", server.ID), MessageDialogStyle.AffirmativeAndNegative);
            if (result == MessageDialogResult.Affirmative)
            {
                var controller = await this.ShowProgressAsync("Installing...", "Please wait...");
                controller.SetIndeterminate();
                bool installed = await InstallAddons.SourceModAndMetaMod(server);
                await controller.CloseAsync();

                var message = installed ? Loc.T("Msg.AddonInstalledOk") : Loc.T("Msg.AddonInstallFail");
                await this.ShowMessageAsync(Loc.T("Msg.ToolSourceModTitle"), Loc.T("Msg.AddonResult", message, server.ID));
            }
        }

        private async void Tools_InstallDayZSALModServer_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            bool? existed = InstallAddons.IsDayZSALModServerExists(server);
            if (existed == null)
            {
                await this.ShowMessageAsync(Loc.T("Msg.ToolDayZSALTitle"), Loc.T("Msg.AddonNotSupported", server.Game, server.ID));
                return;
            }

            if (existed == true)
            {
                await this.ShowMessageAsync(Loc.T("Msg.ToolDayZSALTitle"), Loc.T("Msg.AddonAlreadyInstalled", server.ID));
                return;
            }

            var result = await this.ShowMessageAsync(Loc.T("Msg.ToolDayZSALTitle"), Loc.T("Msg.AddonConfirmInstall", server.ID), MessageDialogStyle.AffirmativeAndNegative);
            if (result == MessageDialogResult.Affirmative)
            {
                var controller = await this.ShowProgressAsync("Installing...", "Please wait...");
                controller.SetIndeterminate();
                bool installed = await InstallAddons.DayZSALModServer(server);
                await controller.CloseAsync();

                string message = installed ? Loc.T("Msg.AddonInstalledOk") : Loc.T("Msg.AddonInstallFail");
                await this.ShowMessageAsync(Loc.T("Msg.ToolDayZSALTitle"), Loc.T("Msg.AddonResult", message, server.ID));
            }
        }

        private async void Tools_InstallOxideMod_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            string messageTitle = Loc.T("Msg.ToolOxideTitle");

            bool? existed = InstallAddons.IsOxideModExists(server);
            if (existed == null)
            {
                await this.ShowMessageAsync(messageTitle, Loc.T("Msg.AddonNotSupported", server.Game, server.ID));
                return;
            }

            var result = await this.ShowMessageAsync(messageTitle, Loc.T("Msg.AddonConfirmInstallUpdate", server.ID), MessageDialogStyle.AffirmativeAndNegative);
            if (result == MessageDialogResult.Affirmative)
            {
                var controller = await this.ShowProgressAsync("Installing...", "Please wait...");
                controller.SetIndeterminate();
                bool installed = await InstallAddons.OxideMod(server);
                await controller.CloseAsync();

                string message = installed ? Loc.T("Msg.AddonInstalledOk") : Loc.T("Msg.AddonInstallFail");
                await this.ShowMessageAsync(messageTitle, Loc.T("Msg.AddonResult", message, server.ID));
            }
        }
        #endregion

        private string GetPublicIP()
        {
            try
            {
                using (var webClient = new WebClient())
                {
                    return webClient.DownloadString("https://ipinfo.io/ip").Replace("\n", string.Empty);
                }
            }
            catch
            {
                return null;
            }
        }

        private void OnBalloonTipClick(object sender, EventArgs e)
        {
        }

        private void NotifyIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (IsVisible)
            {
                Hide();
            }
            else
            {
                WindowState = WindowState.Normal;
                Show();
            }
        }

        #region Left Buttom Grid
        private void Slider_ProcessPriority_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            _serverMetadata[int.Parse(server.ID)].CPUPriority = ((int)slider_ProcessPriority.Value).ToString();
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.CPUPriority, GetServerMetadata(server.ID).CPUPriority);
            textBox_ProcessPriority.Text = Functions.CPU.Priority.GetPriorityByInteger((int)slider_ProcessPriority.Value);

            if (GetServerMetadata(server.ID).Process != null && !GetServerMetadata(server.ID).Process.HasExited)
            {
                _serverMetadata[int.Parse(server.ID)].Process = Functions.CPU.Priority.SetProcessWithPriority(GetServerMetadata(server.ID).Process, (int)slider_ProcessPriority.Value);
            }
        }

        private void Button_SetAffinity_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            ToggleMahappFlyout(MahAppFlyout_SetAffinity);
        }

        private void Button_EditConfig_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            if (Refresh_EditConfig_Data(server.ID))
            {
                ToggleMahappFlyout(MahAppFlyout_EditConfig);
            }
            else
            {
                MahAppFlyout_EditConfig.Visibility = Visibility.Collapsed;
            }
        }

        private bool Refresh_EditConfig_Data(string serverId)
        {
            var serverConfig = new ServerConfig(serverId);
            if (string.IsNullOrWhiteSpace(serverConfig.ServerGame)) { return false; }
            var gameServer = GameServer.Data.Class.Get(serverConfig.ServerGame, pluginList: PluginsList);
            if (gameServer == null) { return false; }

            textbox_EC_ServerID.Text = serverConfig.ServerID;
            textbox_EC_ServerGame.Text = serverConfig.ServerGame;
            textbox_EC_ServerName.Text = serverConfig.ServerName;
            textbox_EC_ServerIP.Text = serverConfig.ServerIP;
            numericUpDown_EC_ServerMaxplayer.Value = int.TryParse(serverConfig.ServerMaxPlayer, out var maxplayer) ? maxplayer : int.Parse(gameServer.Maxplayers);
            numericUpDown_EC_ServerPort.Value = int.TryParse(serverConfig.ServerPort, out var port) ? port : int.Parse(gameServer.Port);
            numericUpDown_EC_ServerQueryPort.Value = int.TryParse(serverConfig.ServerQueryPort, out var queryPort) ? queryPort : int.Parse(gameServer.QueryPort);
            textbox_EC_ServerMap.Text = serverConfig.ServerMap;
            revealbox_EC_ServerGSLT.UseMonospace();
            revealbox_EC_ServerGSLT.Password = serverConfig.ServerGSLT;
            textbox_EC_ServerParam.Text = serverConfig.ServerParam;
            return true;
        }

        private void Button_EditConfig_Save_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerGame, textbox_EC_ServerGame.Text.Trim());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerName, textbox_EC_ServerName.Text.Trim());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerIP, textbox_EC_ServerIP.Text.Trim());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerMaxPlayer, numericUpDown_EC_ServerMaxplayer.Value.ToString());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerPort, numericUpDown_EC_ServerPort.Value.ToString());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerQueryPort, numericUpDown_EC_ServerQueryPort.Value.ToString());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerMap, textbox_EC_ServerMap.Text.Trim());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerGSLT, revealbox_EC_ServerGSLT.Password.Trim());
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerParam, textbox_EC_ServerParam.Text.Trim());

            LoadServerTable();
            MahAppFlyout_EditConfig.Visibility = Visibility.Collapsed;
        }

        private void Button_RestartCrontab_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].RestartCrontab = switch_restartcrontab.IsChecked == true;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.RestartCrontab, GetServerMetadata(server.ID).RestartCrontab ? "1" : "0");
        }

        private void Button_EmbedConsole_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].EmbedConsole = switch_embedconsole.IsChecked == true;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.EmbedConsole, GetServerMetadata(server.ID).EmbedConsole ? "1" : "0");
        }

        private void Button_AutoRestart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoRestart = switch_autorestart.IsChecked == true;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoRestart, GetServerMetadata(server.ID).AutoRestart ? "1" : "0");
        }

        private void Button_AutoStart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoStart = switch_autostart.IsChecked == true;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoStart, GetServerMetadata(server.ID).AutoStart ? "1" : "0");
        }

        // #16: toggle RAM monitoring (per server) + MB threshold
        private void Button_MemWatchdog_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].MemoryWatchdog = switch_memwatchdog.IsChecked == true;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.MemoryWatchdog, GetServerMetadata(server.ID).MemoryWatchdog ? "1" : "0");
        }

        // #69: toggles maintenance mode for the selected server (suspends auto-start + auto-restart).
        private void Actions_ToggleMaintenance_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { System.Windows.MessageBox.Show(Loc.T("Msg.SelectServerFromList"), Loc.T("Msg.MaintenanceTitle"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
            bool on = !GetServerMetadata(server.ID).Maintenance;
            _serverMetadata[int.Parse(server.ID)].Maintenance = on;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.Maintenance, on ? "1" : "0");
            Log(server.ID, on
                ? "[Maintenance] ENABLED — auto-start and auto-restart suspended for this server."
                : "[Maintenance] disabled — auto-start/restart re-enabled.");
            System.Windows.MessageBox.Show(on
                ? Loc.T("Msg.MaintenanceEnabled", server.ID, server.Name)
                : Loc.T("Msg.MaintenanceDisabled", server.ID, server.Name),
                Loc.T("Msg.MaintenanceTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // #20: toggle "backup before update" (per server, off by default)
        private void Button_BackupBeforeUpdate_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].BackupBeforeUpdate = switch_backupbeforeupdate.IsChecked == true;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.BackupBeforeUpdate, GetServerMetadata(server.ID).BackupBeforeUpdate ? "1" : "0");
        }

        private void TextBox_MemLimit_LostFocus(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            // keep a valid number (MB); default 8000 if the input is invalid
            string val = new string((textBox_memlimit.Text ?? string.Empty).Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(val) || !long.TryParse(val, out long mb) || mb < 256) { val = "8000"; }
            textBox_memlimit.Text = val;
            _serverMetadata[int.Parse(server.ID)].MemoryLimitMB = val;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.MemoryLimitMB, val);
        }

        private void Button_AutoUpdate_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoUpdate = switch_autoupdate.IsChecked == true;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoUpdate, GetServerMetadata(server.ID).AutoUpdate ? "1" : "0");
        }

        private async void Button_DiscordAlertSettings_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            ToggleMahappFlyout(MahAppFlyout_DiscordAlert);
        }

        private void Button_UpdateOnStart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].UpdateOnStart = switch_updateonstart.IsChecked == true;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.UpdateOnStart, GetServerMetadata(server.ID).UpdateOnStart ? "1" : "0");
        }

        private void Button_BackupOnStart_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].BackupOnStart = switch_backuponstart.IsChecked == true;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.BackupOnStart, GetServerMetadata(server.ID).BackupOnStart ? "1" : "0");
        }

        private void Button_DiscordAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].DiscordAlert = switch_discordalert.IsChecked == true;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.DiscordAlert, GetServerMetadata(server.ID).DiscordAlert ? "1" : "0");
            button_discordtest.IsEnabled = GetServerMetadata(server.ID).DiscordAlert;
        }

        // #21: the Edit button opens the WIZARD (guided cron building). Manual entry optional.
        private void Button_CrontabEdit_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            // fill the hours/minutes lists once
            if (cw_hour.Items.Count == 0)
            {
                for (int h = 0; h < 24; h++) { cw_hour.Items.Add(h.ToString("D2")); }
                for (int m = 0; m < 60; m += 5) { cw_minute.Items.Add(m.ToString("D2")); }
            }
            // pre-fill from the existing cron (M H * * D) if possible
            int hour = 6, min = 0;
            var parts = (ServerConfig.GetSetting(server.ID, ServerConfig.SettingName.CrontabFormat) ?? string.Empty).Split(' ');
            if (parts.Length >= 2 && int.TryParse(parts[0], out int pm) && int.TryParse(parts[1], out int ph)) { min = pm; hour = ph; }
            cw_hour.SelectedItem = hour.ToString("D2");
            cw_minute.SelectedItem = (min - (min % 5)).ToString("D2");
            CrontabWizard_Changed(null, null);
            ToggleMahappFlyout(MahAppFlyout_CrontabWizard);
        }

        private string BuildCronFromWizard()
        {
            int hour = int.TryParse(cw_hour.SelectedItem?.ToString(), out int h) ? h : 6;
            int min = int.TryParse(cw_minute.SelectedItem?.ToString(), out int m) ? m : 0;
            bool weekly = cw_freq.SelectedIndex == 1;
            if (weekly)
            {
                int dow = (cw_day.SelectedIndex + 1) % 7; // Monday=1..Saturday=6, Sunday=0
                return $"{min} {hour} * * {dow}";
            }
            return $"{min} {hour} * * *";
        }

        private void CrontabWizard_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cw_preview == null) { return; }
            cw_day.IsEnabled = cw_freq.SelectedIndex == 1; // day only when weekly
            string cron = BuildCronFromWizard();
            string next = CrontabSchedule.TryParse(cron)?.GetNextOccurrence(DateTime.Now).ToString("ddd, dd/MM/yyyy HH:mm") ?? "?";
            cw_preview.Text = $"cron: {cron}    (next: {next})";
        }

        private void CrontabWizard_Cancel(object sender, RoutedEventArgs e) => ToggleMahappFlyout(MahAppFlyout_CrontabWizard);

        private void CrontabWizard_Apply(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { ToggleMahappFlyout(MahAppFlyout_CrontabWizard); return; }
            string cron = BuildCronFromWizard();
            _serverMetadata[int.Parse(server.ID)].CrontabFormat = cron;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.CrontabFormat, cron);
            textBox_restartcrontab.Text = cron;
            textBox_nextcrontab.Text = CrontabSchedule.TryParse(cron)?.GetNextOccurrence(DateTime.Now).ToString("ddd, MM/dd/yyyy HH:mm:ss") ?? string.Empty;
            ToggleMahappFlyout(MahAppFlyout_CrontabWizard);
        }
        #endregion

        #region Switches
        private void Switch_AutoStartAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoStartAlert = MahAppSwitch_AutoStartAlert.IsChecked == true;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoStartAlert, GetServerMetadata(server.ID).AutoStartAlert ? "1" : "0");
        }

        private void Switch_AutoRestartAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoRestartAlert = MahAppSwitch_AutoRestartAlert.IsChecked == true;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoRestartAlert, GetServerMetadata(server.ID).AutoRestartAlert ? "1" : "0");
        }

        private void Switch_AutoUpdateAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].AutoUpdateAlert = MahAppSwitch_AutoUpdateAlert.IsChecked == true;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoUpdateAlert, GetServerMetadata(server.ID).AutoUpdateAlert ? "1" : "0");
        }

        private void Switch_RestartCrontabAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].RestartCrontabAlert = MahAppSwitch_RestartCrontabAlert.IsChecked == true;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.RestartCrontabAlert, GetServerMetadata(server.ID).RestartCrontabAlert ? "1" : "0");
        }

        private void Switch_CrashAlert_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }
            _serverMetadata[int.Parse(server.ID)].CrashAlert = MahAppSwitch_CrashAlert.IsChecked == true;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.CrashAlert, GetServerMetadata(server.ID).CrashAlert ? "1" : "0");
        }
        #endregion

        private async void Window_Activated(object sender, EventArgs e)
        {
            if (MahAppFlyout_ManageAddons.Visibility == Visibility.Visible)
            {
                ListBox_ManageAddons_Refresh();
            }

            // Fix the windows cannot toggle issue because of LoadServerTable
            await Task.Delay(1);

            if (ShowActivated)
            {
                LoadServerTable();
            }
        }

        #region Discord Bot
        private async void Switch_DiscordBot_Toggled(object sender, RoutedEventArgs e)
        {
            if (!switch_DiscordBot.IsEnabled) { return; }

            if (switch_DiscordBot.IsChecked == true)
            {
                await StartDiscordBotAsync();
            }
            else
            {
                button_DiscordBotInvite.IsEnabled = switch_DiscordBot.IsEnabled = false;
                await g_DiscordBot.Stop();
                DiscordBotLog("Discord Bot stopped.");
                switch_DiscordBot.IsEnabled = true;
            }
        }

        // Bot startup shared by the toggle AND the auto-start (auto-start cannot
        // rely on the Click event, which does not fire on a programmatic IsChecked).
        private async Task StartDiscordBotAsync()
        {
            switch_DiscordBot.IsEnabled = false;
            DiscordBotLog("Starting the Discord Bot…");
            bool botOk = await g_DiscordBot.Start();
            switch_DiscordBot.IsChecked = botOk;
            button_DiscordBotInvite.IsEnabled = botOk;
            // The precise reason for a failure is already logged by Bot.Start() (token / intent / network).
            DiscordBotLog(botOk ? "Discord Bot started." : "Discord Bot: startup failed (see the reason above).");
            switch_DiscordBot.IsEnabled = true;
            // Admin name resolution happens via OnDiscordBotReady() when the bot is actually Ready.
        }

        // Called by Bot when the connection is established (Ready): (re)populates and resolves admin names,
        // regardless of the startup mode (auto or manual) and after a reconnection.
        public void OnDiscordBotReady()
        {
            try
            {
                int sel = listBox_DiscordBotAdminList.SelectedIndex < 0 ? 0 : listBox_DiscordBotAdminList.SelectedIndex;
                Refresh_DiscordBotAdminList(sel);
            }
            catch { /* ignore */ }
        }

        private void Button_DiscordBotPrefixEdit_Click(object sender, RoutedEventArgs e)
        {
            if (button_DiscordBotPrefixEdit.Content.ToString() == "Edit")
            {
                button_DiscordBotPrefixEdit.Content = "Save";
                textBox_DiscordBotPrefix.IsEnabled = true;
                textBox_DiscordBotPrefix.Focus();
                textBox_DiscordBotPrefix.SelectAll();
            }
            else
            {
                button_DiscordBotPrefixEdit.Content = "Edit";
                textBox_DiscordBotPrefix.IsEnabled = false;
                DiscordBot.Configs.SetBotPrefix(textBox_DiscordBotPrefix.Text);
                label_DiscordBotCommands.Content = DiscordBot.Configs.GetCommandsList();
            }
        }

        private void Button_DiscordBotTokenEdit_Click(object sender, RoutedEventArgs e)
        {
            // Toggle on the field state, NOT on the button's (localized) label — comparing to "Edit"
            // broke this in every non-English UI.
            if (!textBox_DiscordBotToken.IsEnabled)
            {
                rectangle_DiscordBotTokenSpoiler.Visibility = Visibility.Hidden;
                button_DiscordBotTokenEdit.Content = Loc.T("MW.Common.Save");
                textBox_DiscordBotToken.IsEnabled = true;
                textBox_DiscordBotToken.Focus();
                textBox_DiscordBotToken.SelectAll();
            }
            else
            {
                rectangle_DiscordBotTokenSpoiler.Visibility = Visibility.Visible;
                button_DiscordBotTokenEdit.Content = Loc.T("MW.Common.Edit");
                textBox_DiscordBotToken.IsEnabled = false;
                DiscordBot.Configs.SetBotToken(textBox_DiscordBotToken.Text);
            }
        }

        // Eye toggle: peek/hide the stored bot token without entering edit mode.
        private void Button_DiscordBotTokenPeek_Click(object sender, RoutedEventArgs e)
        {
            rectangle_DiscordBotTokenSpoiler.Visibility =
                rectangle_DiscordBotTokenSpoiler.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
        }

        private void Button_DiscordBotDashboardEdit_Click(object sender, RoutedEventArgs e)
        {
            if (button_DiscordBotDashboardEdit.Content.ToString() == "Edit")
            {
                button_DiscordBotDashboardEdit.Content = "Save";
                textBox_DiscordBotDashboard.IsEnabled = true;
                textBox_DiscordBotDashboard.Focus();
                textBox_DiscordBotDashboard.SelectAll();
            }
            else
            {
                button_DiscordBotDashboardEdit.Content = "Edit";
                textBox_DiscordBotDashboard.IsEnabled = false;
                DiscordBot.Configs.SetDashboardChannel(textBox_DiscordBotDashboard.Text);
                // Also saves the refresh rate (10–900 s).
                double rate = numericUpDown_DiscordRefreshRate.Value ?? 30;
                DiscordBot.Configs.SetDashboardRefreshRate((int)rate);
            }
        }

        private void Button_DiscordBotAdminPanelEdit_Click(object sender, RoutedEventArgs e)
        {
            if (button_DiscordBotAdminPanelEdit.Content.ToString() == "Edit")
            {
                button_DiscordBotAdminPanelEdit.Content = "Save";
                textBox_DiscordBotAdminPanel.IsEnabled = true;
                textBox_DiscordBotAdminPanel.Focus();
                textBox_DiscordBotAdminPanel.SelectAll();
            }
            else
            {
                button_DiscordBotAdminPanelEdit.Content = "Edit";
                textBox_DiscordBotAdminPanel.IsEnabled = false;
                DiscordBot.Configs.SetAdminPanelChannel(textBox_DiscordBotAdminPanel.Text);
            }
        }

        private void Button_DiscordBotAddID_Click(object sender, RoutedEventArgs e)
        {
            OpenDiscordAdminOverlay(null, "0");
        }

        private void Button_DiscordBotEditServerID_Click(object sender, RoutedEventArgs e)
        {
            var adminListItem = (DiscordBot.AdminListItem)listBox_DiscordBotAdminList.SelectedItem;
            if (adminListItem == null)
            {
                System.Windows.MessageBox.Show(Loc.T("Msg.SelectAdminFirst"), Loc.T("Msg.DiscordAdminTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            OpenDiscordAdminOverlay(adminListItem.AdminId, adminListItem.ServerIds, adminListItem.Username);
        }

        // ===== Discord admin management overlay (ID + per-server permissions via checkboxes) =====
        private sealed class ServerCheckItem
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public bool IsChecked { get; set; }
            public string Display => $"#{Id} — {Name}";
        }

        private string _editingAdminId; // null = add, otherwise edit this ID

        private void OpenDiscordAdminOverlay(string adminId, string serverIds, string knownUsername = null)
        {
            _editingAdminId = adminId;
            da_title.Text = adminId == null ? "Add a Discord admin" : "Edit the Discord admin";
            da_id.Text = adminId ?? string.Empty;
            da_id.IsEnabled = adminId == null; // when editing, the ID is not editable (key)

            // Shows the name of the edited user (value already known, then refreshed via the bot).
            bool hasName = !string.IsNullOrWhiteSpace(knownUsername)
                && knownUsername != "…" && knownUsername != "(start the bot)"
                && knownUsername != "(not found)" && knownUsername != "—";
            da_resolved.Text = hasName ? $"✔ {knownUsername}" : string.Empty;
            if (adminId != null && g_DiscordBot.IsConnected)
            {
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    string name = await g_DiscordBot.ResolveUsername(adminId);
                    if (!string.IsNullOrEmpty(name)) { da_resolved.Text = $"✔ {name}"; }
                });
            }

            var ids = (serverIds ?? string.Empty).Split(',').Select(s => s.Trim()).ToList();
            bool all = ids.Contains("0");
            da_all.IsChecked = all;

            var items = ServerGrid.Items.Cast<ServerTable>()
                .Select(s => new ServerCheckItem { Id = s.ID, Name = s.Name, IsChecked = !all && ids.Contains(s.ID) })
                .ToList();
            da_serverChecks.ItemsSource = items;
            da_serverChecks.IsEnabled = !all;

            ToggleMahappFlyout(MahAppFlyout_DiscordAdmin);
        }

        private void DiscordAdmin_AllToggled(object sender, RoutedEventArgs e)
        {
            da_serverChecks.IsEnabled = da_all.IsChecked != true;
        }

        private async void DiscordAdmin_Resolve_Click(object sender, RoutedEventArgs e)
        {
            string id = da_id.Text.Trim();
            if (!IsValidDiscordId(id)) { da_resolved.Text = "Invalid ID (17–20 digits expected)."; return; }
            if (!g_DiscordBot.IsConnected) { da_resolved.Text = "Start the bot to verify the name."; return; }
            da_resolved.Text = "Searching…";
            string name = await g_DiscordBot.ResolveUsername(id);
            da_resolved.Text = string.IsNullOrEmpty(name) ? "User not found." : $"✔ {name}";
        }

        private void DiscordAdmin_Cancel_Click(object sender, RoutedEventArgs e) => ToggleMahappFlyout(MahAppFlyout_DiscordAdmin);

        private void DiscordAdmin_Save_Click(object sender, RoutedEventArgs e)
        {
            string id = da_id.Text.Trim();
            if (!IsValidDiscordId(id))
            {
                System.Windows.MessageBox.Show(Loc.T("Msg.InvalidDiscordId"), Loc.T("Msg.DiscordAdminTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string serverIds;
            if (da_all.IsChecked == true)
            {
                serverIds = "0";
            }
            else
            {
                var checked_ = (da_serverChecks.ItemsSource as IEnumerable<ServerCheckItem>)?.Where(x => x.IsChecked).Select(x => x.Id).ToList() ?? new List<string>();
                if (checked_.Count == 0)
                {
                    System.Windows.MessageBox.Show(Loc.T("Msg.CheckAtLeastOneServer"), Loc.T("Msg.DiscordAdminTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                serverIds = string.Join(",", checked_);
            }

            var adminList = DiscordBot.Configs.GetBotAdminList();
            string key = _editingAdminId ?? id;
            int idx = adminList.FindIndex(a => a.Item1 == key);
            if (idx >= 0) { adminList[idx] = (id, serverIds); }
            else if (adminList.Any(a => a.Item1 == id)) { adminList[adminList.FindIndex(a => a.Item1 == id)] = (id, serverIds); }
            else { adminList.Add((id, serverIds)); }

            DiscordBot.Configs.SetBotAdminList(adminList);
            ToggleMahappFlyout(MahAppFlyout_DiscordAdmin);
            Refresh_DiscordBotAdminList(Math.Max(0, adminList.FindIndex(a => a.Item1 == id)));
        }

        private static bool IsValidDiscordId(string id)
            => !string.IsNullOrWhiteSpace(id) && id.Length >= 17 && id.Length <= 20 && id.All(char.IsDigit);

        private void Button_DiscordBotRemoveID_Click(object sender, RoutedEventArgs e)
        {
            if (listBox_DiscordBotAdminList.SelectedIndex >= 0)
            {
                var adminList = DiscordBot.Configs.GetBotAdminList();
                try
                {
                    adminList.RemoveAt(listBox_DiscordBotAdminList.SelectedIndex);
                }
                catch
                {
                    Console.WriteLine($"Fail to delete item {listBox_DiscordBotAdminList.SelectedIndex} in adminIDs.txt");
                }
                DiscordBot.Configs.SetBotAdminList(adminList);

                listBox_DiscordBotAdminList.Items.Remove(listBox_DiscordBotAdminList.Items[listBox_DiscordBotAdminList.SelectedIndex]);
            }
        }

        public void Refresh_DiscordBotAdminList(int selectIndex = 0)
        {
            listBox_DiscordBotAdminList.Items.Clear();
            bool botConnected = g_DiscordBot.IsConnected;
            foreach (var (adminID, serverIDs) in DiscordBot.Configs.GetBotAdminList())
            {
                var item = new DiscordBot.AdminListItem
                {
                    AdminId = adminID,
                    ServerIds = serverIDs,
                    Username = botConnected ? "…" : "(start the bot)"
                };
                listBox_DiscordBotAdminList.Items.Add(item);
                if (botConnected) { _ = ResolveAdminUsername(item); }
            }
            listBox_DiscordBotAdminList.SelectedIndex = listBox_DiscordBotAdminList.Items.Count >= 0 ? selectIndex : -1;
        }

        // Resolves an admin's Discord ID to a username (via the bot, REST) and updates the cell.
        private async Task ResolveAdminUsername(DiscordBot.AdminListItem item)
        {
            try
            {
                string name = await g_DiscordBot.ResolveUsername(item.AdminId);
                item.Username = string.IsNullOrEmpty(name) ? "(not found)" : name;
            }
            catch { item.Username = "—"; }
        }

        public int GetServerCount()
        {
            return ServerGrid.Items.Count;
        }

        public List<(string, string, string)> GetServerList()
        {
            var list = new List<(string, string, string)>();

            for (int i = 0; i < ServerGrid.Items.Count; i++)
            {
                var server = (ServerTable)ServerGrid.Items[i];
                list.Add((server.ID, server.Status, server.Name));
            }

            return list;
        }

        public bool IsServerExist(string serverId)
        {
            for (int i = 0; i < ServerGrid.Items.Count; i++)
            {
                var server = (ServerTable)ServerGrid.Items[i];
                if (server.ID == serverId) { return true; }
            }

            return false;
        }

        public ServerStatus GetServerStatus(string serverId)
        {
            return GetServerMetadata(serverId).ServerStatus;
        }

        public string GetServerName(string serverId)
        {
            var server = GetServerTableById(serverId);
            return server?.Name ?? string.Empty;
        }

        private ServerTable GetServerTableById(string serverId)
        {
            for (int i = 0; i < ServerGrid.Items.Count; i++)
            {
                var server = (ServerTable)ServerGrid.Items[i];
                if (server.ID == serverId) { return server; }
            }

            return null;
        }

        public async Task<bool> StartServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive START action | {adminName} ({adminID})");
            await GameServer_Start(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started;
        }

        public async Task<bool> StopServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive STOP action | {adminName} ({adminID})");
            await GameServer_Stop(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped;
        }

        public async Task<bool> RestartServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive RESTART action | {adminName} ({adminID})");
            await GameServer_Restart(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started;
        }

        public async Task<bool> SendCommandById(string serverId, string command, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive SEND action | {adminName} ({adminID}) | {command}");
            SendCommand(server, command);
            return true;
        }

        public async Task<bool> BackupServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive BACKUP action | {adminName} ({adminID})");
            await GameServer_Backup(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped;
        }

        public async Task<bool> UpdateServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive UPDATE action | {adminName} ({adminID})");
            await GameServer_Update(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped;
        }

        // ===== Helpers for the enriched Discord commands =====
        public string GetServerConnectInfo(string serverId)
        {
            var s = GetServerTableById(serverId);
            if (s == null) { return string.Empty; }
            string ip = string.IsNullOrWhiteSpace(s.IP) ? "?" : s.IP;
            string port = string.IsNullOrWhiteSpace(s.Port) ? "?" : s.Port;
            return $"{ip}:{port}";
        }

        public string GetServerGame(string serverId) => GetServerTableById(serverId)?.Game ?? string.Empty;

        // Last lines of a server's console (for the Discord console/log command).
        public string GetServerConsoleTail(string serverId, int lines)
        {
            try
            {
                var meta = GetServerMetadata(serverId);
                string text = meta?.ServerConsole?.Get() ?? string.Empty;
                if (string.IsNullOrEmpty(text)) { return string.Empty; }
                var arr = text.Replace("\r\n", "\n").Split('\n');
                int n = Math.Min(lines <= 0 ? 15 : lines, arr.Length);
                return string.Join("\n", arr.Skip(Math.Max(0, arr.Length - n)));
            }
            catch { return string.Empty; }
        }

        // Online players (A2S) already computed by the StartPlayerCountRefresh loop.
        public string GetServerPlayers(string serverId) => GetServerTableById(serverId)?.Players ?? "—";

        public async Task<bool> KillServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive KILL action | {adminName} ({adminID})");
            try
            {
                Process p = GetServerMetadata(server.ID).Process;
                if (p != null && !p.HasExited)
                {
                    Log(server.ID, "Discord: Kill");
                    p.Kill();
                    _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                    Log(server.ID, "Server: Killed");
                    SetServerStatus(server, "Stopped");
                    _serverMetadata[int.Parse(server.ID)].ServerConsole.Clear();
                    _serverMetadata[int.Parse(server.ID)].Process = null;
                }
                return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped;
            }
            catch
            {
                return false;
            }
        }

        private void Switch_DiscordBotAutoStart_Click(object sender, RoutedEventArgs e)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM", true))
            {
                key?.SetValue("DiscordBotAutoStart", (MahAppSwitch_DiscordBotAutoStart.IsChecked == true).ToString());
            }
        }

        private void Button_DiscordBotInvite_Click(object sender, RoutedEventArgs e)
        {
            string inviteLink = g_DiscordBot.GetInviteLink();
            if (!string.IsNullOrWhiteSpace(inviteLink))
            {
                Shell.Open(g_DiscordBot.GetInviteLink());
            }
        }
        #endregion

        /// <summary>Hide others Flyout and toggle the flyout</summary>
        /// <param name="flyout"></param>
        // === Dialog shims: MahApps ShowXxxAsync -> WPF-UI (call sites unchanged) ===
        public class ProgressStub
        {
            public void SetIndeterminate() { }
            public void SetMessage(string m) { }
            public void SetTitle(string t) { }
            public System.Threading.Tasks.Task CloseAsync() => System.Threading.Tasks.Task.CompletedTask;
        }

        public async System.Threading.Tasks.Task<MessageDialogResult> ShowMessageAsync(string title, string message, MessageDialogStyle style = MessageDialogStyle.Affirmative, MetroDialogSettings settings = null)
        {
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Title = title,
                Content = message,
                PrimaryButtonText = settings?.AffirmativeButtonText ?? "OK",
                CloseButtonText = (style == MessageDialogStyle.AffirmativeAndNegative) ? (settings?.NegativeButtonText ?? "Cancel") : "OK"
            };
            var r = await box.ShowDialogAsync();
            return r == Wpf.Ui.Controls.MessageBoxResult.Primary ? MessageDialogResult.Affirmative : MessageDialogResult.Negative;
        }

        public async System.Threading.Tasks.Task<string> ShowInputAsync(string title, string message, MetroDialogSettings settings = null)
        {
            var tb = new System.Windows.Controls.TextBox { Text = settings?.DefaultText ?? "", MinWidth = 340, Margin = new Thickness(0, 10, 0, 0) };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(tb);
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = settings?.AffirmativeButtonText ?? "OK",
                CloseButtonText = "Cancel"
            };
            var r = await box.ShowDialogAsync();
            return r == Wpf.Ui.Controls.MessageBoxResult.Primary ? tb.Text : null;
        }

        public async System.Threading.Tasks.Task<ProgressStub> ShowProgressAsync(string title, string message, bool isCancelable = false, MetroDialogSettings settings = null)
        {
            await System.Threading.Tasks.Task.CompletedTask;
            return new ProgressStub();
        }

        // Closes a dialog overlay by clicking the dimmed background (outside the card) - W11 pattern
        private void Overlay_BackdropClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, sender) && sender is UIElement el)
            {
                el.Visibility = Visibility.Collapsed;
            }
        }

        // Wizard: help creating a Discord bot token.
        private void TokenHelp_Open_Click(object sender, RoutedEventArgs e) => ToggleMahappFlyout(MahAppFlyout_TokenHelp);
        private void TokenHelp_Close_Click(object sender, RoutedEventArgs e) => ToggleMahappFlyout(MahAppFlyout_TokenHelp);
        private void SetAffinity_Close_Click(object sender, RoutedEventArgs e) => ToggleMahappFlyout(MahAppFlyout_SetAffinity);
        private void DiscordAlert_Close_Click(object sender, RoutedEventArgs e) => ToggleMahappFlyout(MahAppFlyout_DiscordAlert);
        private void TokenHelp_OpenPortal_Click(object sender, RoutedEventArgs e) => Shell.Open("https://discord.com/developers/applications");

        private void ToggleMahappFlyout(FrameworkElement flyout)
        {
            // WPF-UI overlay panels: opens the target if closed, closes all the others.
            foreach (var panel in new FrameworkElement[] {
                MahAppFlyout_DiscordAlert, MahAppFlyout_EditConfig, MahAppFlyout_ImportGameServer,
                MahAppFlyout_InstallGameServer, MahAppFlyout_ManageAddons, MahAppFlyout_RestoreBackup,
                MahAppFlyout_SetAffinity, MahAppFlyout_Settings, MahAppFlyout_ViewPlugins,
                MahAppFlyout_CrontabWizard, MahAppFlyout_TokenHelp, MahAppFlyout_DiscordAdmin })
            {
                bool show = (panel == flyout) && panel.Visibility != Visibility.Visible;
                panel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // === Sidebar W11 : navigation (remplace HamburgerMenu MahApps) ===
        private bool _dashboardVisible; // true only while the Dashboard tab is shown (gates the 2s dashboard loop)
        private void ShowHomeMenu(int index)
        {
            // Only collect the per-server CPU/RAM sparkline history + run the dashboard compute loop while
            // the Dashboard tab is shown.
            Functions.ServerTable.CollectHistory = (index == 1);
            _dashboardVisible = (index == 1);
            // Opening the Dashboard: publish the histories immediately so the sparklines paint at once
            // (the raw buffers keep filling off-tab; without this the copies lag up to one 5s tick).
            if (index == 1)
            {
                try { foreach (var it in ServerGrid.Items) { (it as Functions.ServerTable)?.Sample(); } } catch { }
            }
            hMenu_Home.Visibility = (index == 0) ? Visibility.Visible : Visibility.Hidden;
            hMenu_Dashboard.Visibility = (index == 1) ? Visibility.Visible : Visibility.Hidden;
            hMenu_Discordbot.Visibility = (index == 2) ? Visibility.Visible : Visibility.Hidden;
            hMenu_Backups.Visibility = (index == 3) ? Visibility.Visible : Visibility.Hidden;
            if (NavHome != null) NavHome.Appearance = (index == 0) ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
            if (NavDashboard != null) NavDashboard.Appearance = (index == 1) ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
            if (NavDiscordBot != null) NavDiscordBot.Appearance = (index == 2) ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
            if (NavBackups != null) NavBackups.Appearance = (index == 3) ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
            if (index == 3) { Backups_PopulateServers(); }
            if (index == 2)
            {
                label_DiscordBotCommands.Content = DiscordBot.Configs.GetCommandsList();
                button_DiscordBotPrefixEdit.Content = "Edit";
                textBox_DiscordBotPrefix.IsEnabled = false;
                textBox_DiscordBotPrefix.Text = DiscordBot.Configs.GetBotPrefix();
                button_DiscordBotTokenEdit.Content = "Edit";
                textBox_DiscordBotToken.IsEnabled = false;
                textBox_DiscordBotToken.Text = DiscordBot.Configs.GetBotToken();
                button_DiscordBotDashboardEdit.Content = "Edit";
                textBox_DiscordBotDashboard.IsEnabled = false;
                textBox_DiscordBotDashboard.Text = DiscordBot.Configs.GetDashboardChannel();
                numericUpDown_DiscordRefreshRate.Value = DiscordBot.Configs.GetDashboardRefreshRate();
                button_DiscordBotAdminPanelEdit.Content = "Edit";
                textBox_DiscordBotAdminPanel.IsEnabled = false;
                textBox_DiscordBotAdminPanel.Text = DiscordBot.Configs.GetAdminPanelChannel();
                Refresh_DiscordBotAdminList(listBox_DiscordBotAdminList.SelectedIndex);
                if (listBox_DiscordBotAdminList.Items.Count > 0 && listBox_DiscordBotAdminList.SelectedItem == null)
                    listBox_DiscordBotAdminList.SelectedItem = listBox_DiscordBotAdminList.Items[0];
            }
        }

        private void NavHome_Click(object sender, RoutedEventArgs e) => ShowHomeMenu(0);
        private void NavDashboard_Click(object sender, RoutedEventArgs e) => ShowHomeMenu(1);
        private void NavDiscordBot_Click(object sender, RoutedEventArgs e) => ShowHomeMenu(2);
        private void NavPlugins_Click(object sender, RoutedEventArgs e) => ToggleMahappFlyout(MahAppFlyout_ViewPlugins);
        private void NavSettings_Click(object sender, RoutedEventArgs e) => ToggleMahappFlyout(MahAppFlyout_Settings);
        private void NavBackups_Click(object sender, RoutedEventArgs e) => ShowHomeMenu(3);

        // ===== Backups tab (versioning) =====
        private class BackupItem { public string Name { get; set; } public string Date { get; set; } public string Size { get; set; } public string FullPath { get; set; } }

        private void Backups_PopulateServers()
        {
            var current = (cb_backupServer.SelectedItem as ServerTable)?.ID;
            var list = ServerGrid.Items.Cast<ServerTable>().ToList();
            cb_backupServer.ItemsSource = null;
            cb_backupServer.ItemsSource = list;
            if (list.Count > 0) { cb_backupServer.SelectedItem = list.FirstOrDefault(s => s.ID == current) ?? list[0]; }
            Backups_RefreshList();
            Backups_SyncSchedule();
        }

        private void Backups_RefreshList()
        {
            lv_backups.Items.Clear();
            if (!(cb_backupServer.SelectedItem is ServerTable server)) { textBox_backupLocation.Text = string.Empty; return; }
            try
            {
                var bc = new BackupConfig(server.ID);
                textBox_backupLocation.Text = bc.BackupLocation;
                if (Directory.Exists(bc.BackupLocation))
                {
                    string tag = $"WGSM-Backup-Server-{server.ID}-";
                    foreach (var fi in new DirectoryInfo(bc.BackupLocation).GetFiles("*.zip").Where(x => x.Name.Contains(tag)).OrderByDescending(x => x.LastWriteTime))
                    {
                        lv_backups.Items.Add(new BackupItem { Name = fi.Name, Date = fi.LastWriteTime.ToString("dd/MM/yyyy HH:mm"), Size = (fi.Length / 1024 / 1024) + " Mo", FullPath = fi.FullName });
                    }
                }
            }
            catch { }
        }

        private void Backups_ServerChanged(object sender, SelectionChangedEventArgs e) { Backups_RefreshList(); Backups_SyncSchedule(); }
        private void Backups_Refresh_Click(object sender, RoutedEventArgs e) => Backups_PopulateServers();

        private bool _loadingBackupSchedule = false;
        private void Backups_SyncSchedule()
        {
            _loadingBackupSchedule = true;
            try
            {
                if (cb_backupHour.Items.Count == 0) { for (int h = 0; h < 24; h++) { cb_backupHour.Items.Add(h.ToString("D2") + "h"); } }
                if (cb_backupServer.SelectedItem is ServerTable server && GetServerMetadata(server.ID) != null)
                {
                    switch_backupauto.IsChecked = GetServerMetadata(server.ID).BackupCrontab;
                    int hour = 5;
                    var parts = (GetServerMetadata(server.ID).BackupCrontabFormat ?? "0 5 * * *").Split(' ');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int ph)) { hour = ph; }
                    cb_backupHour.SelectedItem = hour.ToString("D2") + "h";
                }
            }
            catch { }
            finally { _loadingBackupSchedule = false; }
        }

        // scheduled backup: saves the toggle + the time (daily cron "0 H * * *") for the selected server
        private void BackupSchedule_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingBackupSchedule) { return; }
            if (!(cb_backupServer.SelectedItem is ServerTable server)) { return; }
            int hour = 5;
            var sel = cb_backupHour.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(sel) && int.TryParse(sel.Replace("h", ""), out int h)) { hour = h; }
            string cron = $"0 {hour} * * *";
            _serverMetadata[int.Parse(server.ID)].BackupCrontab = switch_backupauto.IsChecked == true;
            _serverMetadata[int.Parse(server.ID)].BackupCrontabFormat = cron;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.BackupCrontab, switch_backupauto.IsChecked == true ? "1" : "0");
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.BackupCrontabFormat, cron);
        }

        private void BackupSchedule_Changed(object sender, SelectionChangedEventArgs e) => BackupSchedule_Changed(sender, (RoutedEventArgs)null);

        private async void Backups_BackupNow_Click(object sender, RoutedEventArgs e)
        {
            if (!(cb_backupServer.SelectedItem is ServerTable server)) { return; }
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            { System.Windows.MessageBox.Show(Loc.T("Msg.MustBeStoppedBackup"), Loc.T("Msg.BackupTitle"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
            await GameServer_Backup(server, " | Manual backup");
            Backups_RefreshList();
        }

        private async void Backups_Restore_Click(object sender, RoutedEventArgs e)
        {
            if (!(cb_backupServer.SelectedItem is ServerTable server) || !(lv_backups.SelectedItem is BackupItem item)) { return; }
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            { System.Windows.MessageBox.Show(Loc.T("Msg.MustBeStoppedRestore"), Loc.T("Msg.RestoreTitle"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var r = System.Windows.MessageBox.Show(Loc.T("Msg.RestoreConfirm", item.Name, server.ID, server.Name), Loc.T("Msg.RestoreTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) { return; }
            await GameServer_RestoreBackup(server, item.Name);
        }

        private void Backups_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!(lv_backups.SelectedItem is BackupItem item)) { return; }
            var r = System.Windows.MessageBox.Show(Loc.T("Msg.DeleteBackupConfirm", item.Name), Loc.T("Msg.DeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) { return; }
            try { File.Delete(item.FullPath); } catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message); }
            Backups_RefreshList();
        }

        private void Backups_OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!(cb_backupServer.SelectedItem is ServerTable server)) { return; }
            var bc = new BackupConfig(server.ID);
            if (Directory.Exists(bc.BackupLocation)) { Shell.Open(bc.BackupLocation); }
        }

        // Change the backup location (path) for the selected server, via a folder picker.
        // #179: choose the subfolders to back up (empty = everything).
        private async void Backups_ChangeFolders_Click(object sender, RoutedEventArgs e)
        {
            if (!(cb_backupServer.SelectedItem is ServerTable server)) { return; }
            var bc = new BackupConfig(server.ID);
            string current = string.IsNullOrWhiteSpace(bc.BackupFolders) ? Loc.T("Msg.BackupFoldersEverything") : bc.BackupFolders;
            string input = await this.ShowInputAsync(Loc.T("Msg.BackupFoldersTitle"),
                Loc.T("Msg.BackupFoldersPrompt", current));
            if (input == null) { return; } // cancelled
            bc.BackupFolders = input.Trim();
            bc.Save();
            Log(server.ID, string.IsNullOrWhiteSpace(bc.BackupFolders)
                ? "[Backup] full backup (all folders)."
                : "[Backup] backup limited to: " + bc.BackupFolders);
        }

        private void Backups_ChangeLocation_Click(object sender, RoutedEventArgs e)
        {
            if (!(cb_backupServer.SelectedItem is ServerTable server)) { return; }
            var bc = new BackupConfig(server.ID);
            string oldPath = bc.BackupLocation;

            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = $"Backup folder for #{server.ID} {server.Name}"
            };
            try { if (Directory.Exists(oldPath)) { dlg.InitialDirectory = oldPath; } } catch { }
            if (dlg.ShowDialog() != true) { return; }

            string newPath = dlg.FolderName;
            if (string.IsNullOrWhiteSpace(newPath) ||
                string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase)) { return; }

            try { Directory.CreateDirectory(newPath); }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(Loc.T("Msg.FolderCreateFailed", ex.Message),
                    Loc.T("Msg.BackupFolderTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Offers to move the existing backups to the new location.
            try
            {
                if (Directory.Exists(oldPath))
                {
                    string tag = $"WGSM-Backup-Server-{server.ID}-";
                    var zips = new DirectoryInfo(oldPath).GetFiles("*.zip").Where(x => x.Name.Contains(tag)).ToList();
                    if (zips.Count > 0)
                    {
                        var move = System.Windows.MessageBox.Show(
                            Loc.T("Msg.MoveBackupsConfirm", zips.Count),
                            Loc.T("Msg.BackupFolderTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (move == MessageBoxResult.Yes)
                        {
                            foreach (var z in zips)
                            {
                                string dest = Path.Combine(newPath, z.Name);
                                try { if (File.Exists(dest)) { File.Delete(dest); } File.Move(z.FullName, dest); } catch { }
                            }
                        }
                    }
                }
            }
            catch { }

            bc.BackupLocation = newPath;
            bc.Save();
            Log(server.ID, $"Backup folder changed: {newPath}");
            Backups_RefreshList();
        }

        // Collapsible sidebar (pin/unpin): the hamburger collapses the menu to icons-only or re-expands it.
        private bool _sidebarExpanded = true;
        private System.Collections.Generic.Dictionary<Wpf.Ui.Controls.Button, object> _navLabels;

        private void NavToggle_Click(object sender, RoutedEventArgs e)
        {
            _sidebarExpanded = !_sidebarExpanded;
            SetSidebarExpanded(_sidebarExpanded);
            // Remember the choice across restarts.
            try { using var rk = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\WindowsGSM"); rk?.SetValue("SidebarExpanded", _sidebarExpanded ? "1" : "0"); } catch { }
        }

        // Restore the saved collapsed/expanded state on startup (called from Loaded, after the localized
        // button labels have been applied by the XAML so the cache captures them).
        private void RestoreSidebar()
        {
            try
            {
                using var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WindowsGSM");
                _sidebarExpanded = rk?.GetValue("SidebarExpanded")?.ToString() != "0";
            }
            catch { _sidebarExpanded = true; }
            SetSidebarExpanded(_sidebarExpanded);
        }

        private void SetSidebarExpanded(bool expanded)
        {
            // Cache each nav button's LOCALIZED label once; collapsing hides it (Content=null) and expanding
            // restores it. (The old code hard-coded English labels and only handled 5 of the 10 buttons, so a
            // collapse+expand un-localized the menu and blanked the rest.)
            if (_navLabels == null)
            {
                _navLabels = new System.Collections.Generic.Dictionary<Wpf.Ui.Controls.Button, object>();
                foreach (var b in new[] { NavHome, NavDashboard, NavDiscordBot, NavBackups, NavPlugins, NavSettings, NavPorts, NavDoctor, NavNotifs, NavWebApi })
                {
                    if (b != null) { _navLabels[b] = b.Content; }
                }
            }

            SidebarBorder.Width = expanded ? 190 : 52;
            foreach (var kv in _navLabels) { kv.Key.Content = expanded ? kv.Value : null; }

            var m = new Thickness(expanded ? 190 : 52, 0, 0, 0);
            hMenu_Home.Margin = m;
            hMenu_Dashboard.Margin = m;
            hMenu_Discordbot.Margin = m;
            hMenu_Backups.Margin = m;
        }

        private void Button_AutoScroll_Click(object sender, RoutedEventArgs e)
        {
            var server = (ServerTable)ServerGrid.SelectedItem;
            if (server == null) { return; }

            Button_AutoScroll.Content = Button_AutoScroll.Content.ToString() == "✔️ AUTO SCROLL" ? "❌ AUTO SCROLL" : "✔️ AUTO SCROLL";
            _serverMetadata[int.Parse(server.ID)].AutoScroll = Button_AutoScroll.Content.ToString().Contains("✔️");
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.AutoScroll, GetServerMetadata(server.ID).AutoScroll ? "1" : "0");
        }
    }
}
