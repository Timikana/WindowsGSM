using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace WindowsGSM.Functions
{
    class ServerTable : INotifyPropertyChanged
    {
        // Profondeur de l'historique CPU/RAM pour les mini-graphes du dashboard.
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

        // Badge "MAJ dispo" (P1-1) : notifiant -> la cellule se rafraîchit sans recharger la grille.
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

        // #15 : conflit de port (un autre serveur utilise le même Port ou Query Port).
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

        // ===== P2-3 + Dashboard : stats live par serveur =====
        // Les valeurs sont calculées UNE fois par tick via Sample() puis mises en cache,
        // pour que la grille (Items.Refresh) ET les tuiles du dashboard (binding notifiant)
        // lisent la même mesure sans double lecture du process.
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

        // Joueurs en ligne via A2S (mis à jour par une boucle dédiée dans MainWindow).
        private string _players = "—";
        public string Players
        {
            get => _players;
            set { if (_players != value) { _players = value; OnPropertyChanged(); } }
        }

        // Historiques pour les sparklines du dashboard (copie défensive à chaque tick
        // pour forcer le binding à se rafraîchir).
        private readonly List<double> _cpuHist = new List<double>();
        private readonly List<double> _ramHist = new List<double>();
        public IReadOnlyList<double> CpuHistory { get; private set; } = new List<double>();
        public IReadOnlyList<double> RamHistory { get; private set; } = new List<double>();

        private TimeSpan _lastCpuTime;
        private DateTime _lastCpuAt;

        /// <summary>
        /// Échantillonne CPU/RAM du process (à appeler 1× par tick de refresh) et
        /// notifie les bindings. Appelé depuis la boucle de rafraîchissement de la grille.
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
                        pct = 0; // premier échantillon : pas encore de delta
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

            // Historiques (0 quand hors-ligne pour garder une courbe continue).
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
