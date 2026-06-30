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
        public string PID { get; set; }
        public string Game { get; set; }
        public string Icon { get; set; }
        public string Status { get; set; }
        public string Name { get; set; }
        public string IP { get; set; }
        public string Port { get; set; }
        public string QueryPort { get; set; }
        public string Defaultmap { get; set; }
        public string Maxplayers { get; set; }

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

        public string Uptime
        {
            get
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(PID) && int.TryParse(PID, out int pid))
                    {
                        var time = DateTime.Now - Process.GetProcessById(pid).StartTime;
                        int numberOfDay = (int)time.TotalDays;
                        return $"{numberOfDay} Day{(numberOfDay > 1 ? "s" : string.Empty)}, {time.Hours:D2}:{time.Minutes:D2}";
                    }
                }
                catch { }

                return string.Empty;
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
                    var p = Process.GetProcessById(pid);
                    mb = p.WorkingSet64 / (1024 * 1024);

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
            CpuHistory = new List<double>(_cpuHist);
            RamHistory = new List<double>(_ramHist);

            OnPropertyChanged(nameof(Cpu));
            OnPropertyChanged(nameof(Ram));
            OnPropertyChanged(nameof(CpuPercent));
            OnPropertyChanged(nameof(RamMB));
            OnPropertyChanged(nameof(Uptime));
            OnPropertyChanged(nameof(CpuHistory));
            OnPropertyChanged(nameof(RamHistory));
        }

        private static void Push(List<double> list, double v)
        {
            list.Add(v);
            while (list.Count > HISTORY_LEN) { list.RemoveAt(0); }
        }
    }
}
