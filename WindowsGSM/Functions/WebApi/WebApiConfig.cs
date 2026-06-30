using System;
using System.IO;
using Newtonsoft.Json;

namespace WindowsGSM.Functions.WebApi
{
    /// <summary>
    /// Config de l'API web de contrôle à distance (#207/#25). OPT-IN (désactivée par défaut), token OBLIGATOIRE.
    /// Stockée dans configs/webapi.json ; le token est chiffré au repos (DPAPI via Secret).
    /// ⚠️ HttpListener = HTTP EN CLAIR : pour une exposition internet, placer derrière un reverse-proxy HTTPS.
    /// </summary>
    public class WebApiConfig
    {
        public bool Enabled = false;
        public int Port = 8642;
        public bool BindAll = false;     // false = localhost seulement ; true = toutes interfaces (internet/LAN)
        public string Token = string.Empty; // en clair en mémoire ; chiffré sur disque

        private static string Path => Functions.ServerPath.Get("configs", "webapi.json");

        public static WebApiConfig Load()
        {
            var cfg = new WebApiConfig();
            try
            {
                if (File.Exists(Path))
                {
                    var disk = JsonConvert.DeserializeObject<WebApiConfig>(File.ReadAllText(Path));
                    if (disk != null)
                    {
                        cfg = disk;
                        cfg.Token = Secret.Unprotect(cfg.Token) ?? string.Empty; // déchiffre
                    }
                }
            }
            catch (Exception ex) { AppLog.Warn("WebApi/Load", ex.Message); }
            return cfg;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                var onDisk = new WebApiConfig
                {
                    Enabled = Enabled,
                    Port = Port,
                    BindAll = BindAll,
                    Token = string.IsNullOrEmpty(Token) ? string.Empty : Secret.Protect(Token) // chiffre
                };
                File.WriteAllText(Path, JsonConvert.SerializeObject(onDisk, Formatting.Indented));
            }
            catch (Exception ex) { AppLog.Warn("WebApi/Save", ex.Message); }
        }
    }
}
