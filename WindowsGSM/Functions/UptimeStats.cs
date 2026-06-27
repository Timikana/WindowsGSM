using System;
using System.IO;

namespace WindowsGSM.Functions
{
    // Statistiques de disponibilité par serveur, persistées dans servers/<id>/configs/uptime.cfg
    // (format clé=valeur). Démarrages, crashs, temps en ligne cumulé, date de début de suivi.
    public class UptimeStats
    {
        public int Starts;
        public int Crashes;
        public long OnlineSeconds;
        public DateTime TrackedSince = DateTime.Now;

        private static string PathFor(string serverId) => ServerPath.GetServersConfigs(serverId, "uptime.cfg");

        public static UptimeStats Load(string serverId)
        {
            var s = new UptimeStats();
            try
            {
                string p = PathFor(serverId);
                if (File.Exists(p))
                {
                    foreach (var line in File.ReadAllLines(p))
                    {
                        var kv = line.Split(new[] { '=' }, 2);
                        if (kv.Length != 2) { continue; }
                        switch (kv[0].Trim())
                        {
                            case "starts": int.TryParse(kv[1], out s.Starts); break;
                            case "crashes": int.TryParse(kv[1], out s.Crashes); break;
                            case "onlineseconds": long.TryParse(kv[1], out s.OnlineSeconds); break;
                            case "trackedsince": if (long.TryParse(kv[1], out long t)) { s.TrackedSince = new DateTime(t); } break;
                        }
                    }
                }
                else { s.Save(serverId); } // initialise le suivi
            }
            catch { }
            return s;
        }

        public void Save(string serverId)
        {
            try
            {
                string p = PathFor(serverId);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p,
                    $"starts={Starts}\r\ncrashes={Crashes}\r\nonlineseconds={OnlineSeconds}\r\ntrackedsince={TrackedSince.Ticks}\r\n");
            }
            catch { }
        }

        // % de disponibilité depuis le début du suivi
        public double AvailabilityPercent()
        {
            double tracked = (DateTime.Now - TrackedSince).TotalSeconds;
            if (tracked <= 0) { return 0; }
            double pct = OnlineSeconds / tracked * 100.0;
            return pct > 100 ? 100 : pct;
        }

        public string OnlineTimeString()
        {
            var ts = TimeSpan.FromSeconds(OnlineSeconds);
            return $"{(int)ts.TotalDays}j {ts.Hours:D2}h{ts.Minutes:D2}";
        }
    }
}
