using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace WindowsGSM.Functions
{
    class ServerTable : INotifyPropertyChanged
    {
        // Depth of the CPU/RAM history for the dashboard mini-charts.
        private const int HISTORY_LEN = 40;
        public string ID { get; set; }
        private string _pid;
        public string PID { get => _pid; set { if (_pid != value) { _pid = value; OnPropertyChanged(); } } }
        public string Game { get; set; }
        public string Icon { get; set; }
        // Status is notifying AND raises StatusDisplay so the grid cell updates without a full grid refresh.
        private string _status;
        public string Status { get => _status; set { if (_status != value) { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDisplay)); } } }
        // Localized status shown in the grid. Status itself stays the canonical English value
        // (it is compared as a string in the app's logic), so only the DISPLAY is translated.
        public string StatusDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(Status)) { return string.Empty; }
                var t = Localization.Loc.T("Status." + Status);
                return t == "Status." + Status ? Status : t; // fallback to English if no translation
            }
        }
        public string Name { get; set; }
        public string IP { get; set; }
        public string Port { get; set; }
        public string QueryPort { get; set; }
        private string _defaultmap;
        public string Defaultmap { get => _defaultmap; set { if (_defaultmap != value) { _defaultmap = value; OnPropertyChanged(); } } }
        private string _maxplayers;
        public string Maxplayers { get => _maxplayers; set { if (_maxplayers != value) { _maxplayers = value; OnPropertyChanged(); } } }

        // "Update available" badge (P1-1): notifying -> the cell refreshes without reloading the grid.
        private bool _updateAvailable;
        public bool UpdateAvailable
        {
            get => _updateAvailable;
            set { if (_updateAvailable != value) { _updateAvailable = value; OnPropertyChanged(); } }
        }
        private string _updateTooltip;
        public string UpdateTooltip
        {
            get => _updateTooltip;
            set { if (_updateTooltip != value) { _updateTooltip = value; OnPropertyChanged(); } }
        }

        // #15: port conflict (another server uses the same Port or Query Port).
        private bool _portConflict;
        public bool PortConflict
        {
            get => _portConflict;
            set { if (_portConflict != value) { _portConflict = value; OnPropertyChanged(); } }
        }
        private string _portConflictTooltip;
        public string PortConflictTooltip
        {
            get => _portConflictTooltip;
            set { if (_portConflictTooltip != value) { _portConflictTooltip = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // StartTime captured by Sample() (which already holds the process handle) — avoids re-opening the
        // process on every Uptime binding read (was a 2nd GetProcessById per row per tick, + a thrown
        // exception each tick for a dead PID).
        private DateTime _startTime;
        public string Uptime
        {
            get
            {
                if (!Online || _startTime == default(DateTime)) { return string.Empty; }
                try
                {
                    var time = DateTime.Now - _startTime;
                    int numberOfDay = (int)time.TotalDays;
                    return $"{numberOfDay} Day{(numberOfDay > 1 ? "s" : string.Empty)}, {time.Hours:D2}:{time.Minutes:D2}";
                }
                catch { return string.Empty; }
            }
        }

        // ===== P2-3 + Dashboard: live per-server stats =====
        // The values are computed ONCE per tick via Sample() then cached,
        // so that the grid (Items.Refresh) AND the dashboard tiles (notifying binding)
        // read the same measurement without reading the process twice.
        private bool _online;
        public bool Online
        {
            get => _online;
            private set { if (_online != value) { _online = value; OnPropertyChanged(); } }
        }

        private double _cpuPct = -1;
        private long _ramMb = -1;
        public string Cpu => (_cpuPct < 0) ? string.Empty : $"{_cpuPct:0.0}%";
        public string Ram => (_ramMb < 0) ? string.Empty : $"{_ramMb} MB";
        public double CpuPercent => (_cpuPct < 0) ? 0 : _cpuPct;
        public long RamMB => (_ramMb < 0) ? 0 : _ramMb;

        // Online players via A2S (updated by a dedicated loop in MainWindow).
        private string _players = "—";
        public string Players
        {
            get => _players;
            set { if (_players != value) { _players = value; OnPropertyChanged(); } }
        }

        // Histories for the dashboard sparklines (defensive copy on each tick
        // to force the binding to refresh).
        private readonly List<double> _cpuHist = new List<double>();
        private readonly List<double> _ramHist = new List<double>();
        public IReadOnlyList<double> CpuHistory { get; private set; } = new List<double>();
        public IReadOnlyList<double> RamHistory { get; private set; } = new List<double>();

        private TimeSpan _lastCpuTime;
        private DateTime _lastCpuAt;

        /// <summary>
        /// Samples the process CPU/RAM (to be called once per refresh tick) and
        /// notifies the bindings. Called from the grid's refresh loop.
        /// </summary>
        public void Sample()
        {
            double pct = -1;
            long mb = -1;
            try
            {
                if (!string.IsNullOrWhiteSpace(PID) && int.TryParse(PID, out int pid))
                {
                    using var p = Process.GetProcessById(pid);
                    mb = p.WorkingSet64 / (1024 * 1024);
                    _startTime = p.StartTime; // cache for the Uptime getter (avoids reopening the process)

                    var cpuNow = p.TotalProcessorTime;
                    var now = DateTime.Now;
                    if (_lastCpuAt != default(DateTime))
                    {
                        double dtMs = (now - _lastCpuAt).TotalMilliseconds;
                        double dcMs = (cpuNow - _lastCpuTime).TotalMilliseconds;
                        pct = (dtMs > 0) ? (dcMs / (dtMs * Environment.ProcessorCount)) * 100.0 : 0;
                        if (pct < 0) { pct = 0; }
                    }
                    else
                    {
                        pct = 0; // first sample: no delta yet
                    }
                    _lastCpuTime = cpuNow;
                    _lastCpuAt = now;
                }
                else
                {
                    _lastCpuAt = default(DateTime);
                }
            }
            catch
            {
                _lastCpuAt = default(DateTime);
                pct = -1; mb = -1;
            }

            _cpuPct = pct;
            _ramMb = mb;
            Online = pct >= 0;

            // Histories (0 when offline to keep a continuous curve).
            Push(_cpuHist, pct < 0 ? 0 : pct);
            Push(_ramHist, mb < 0 ? 0 : mb);

            OnPropertyChanged(nameof(Cpu));
            OnPropertyChanged(nameof(Ram));
            OnPropertyChanged(nameof(CpuPercent));
            OnPropertyChanged(nameof(RamMB));
            OnPropertyChanged(nameof(Uptime));

            // The sparkline copies (a fresh List per server per tick) are only needed while the dashboard
            // tab is visible — skip the allocation + binding churn when the user is on another tab.
            if (CollectHistory)
            {
                CpuHistory = new List<double>(_cpuHist);
                RamHistory = new List<double>(_ramHist);
                OnPropertyChanged(nameof(CpuHistory));
                OnPropertyChanged(nameof(RamHistory));
            }
        }

        // Set by MainWindow: true only while the Dashboard tab is shown (its tiles host the sparklines).
        public static bool CollectHistory = true;

        private static void Push(List<double> list, double v)
        {
            list.Add(v);
            while (list.Count > HISTORY_LEN) { list.RemoveAt(0); }
        }
    }
}
