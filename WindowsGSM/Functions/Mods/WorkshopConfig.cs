using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace WindowsGSM.Functions.Mods
{
    /// <summary>Un mod Workshop suivi pour un serveur (ID Steam publié + nom libre + activé).</summary>
    public class WorkshopEntry
    {
        public string Id;        // publishedfileid
        public string Name;      // libellé libre (nom du mod)
        public bool Enabled = true;
    }

    /// <summary>Liste des mods Workshop d'un serveur, persistée dans configs/wgsm-workshop.json.</summary>
    public class WorkshopConfig
    {
        public List<WorkshopEntry> Items = new List<WorkshopEntry>();

        [JsonIgnore] public string Path;

        public static WorkshopConfig Load(string serverId)
        {
            string path = Functions.ServerPath.GetServersConfigs(serverId, "wgsm-workshop.json");
            WorkshopConfig cfg = null;
            try { if (File.Exists(path)) { cfg = JsonConvert.DeserializeObject<WorkshopConfig>(File.ReadAllText(path)); } }
            catch (Exception ex) { Functions.AppLog.Warn("Workshop/Load", ex.Message); }
            cfg = cfg ?? new WorkshopConfig();
            cfg.Path = path;
            return cfg;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.WriteAllText(Path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex) { Functions.AppLog.Warn("Workshop/Save", ex.Message); }
        }
    }
}
