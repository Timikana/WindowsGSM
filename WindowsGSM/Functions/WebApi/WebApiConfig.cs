using System;
using System.IO;
using Newtonsoft.Json;

namespace WindowsGSM.Functions.WebApi
{
    /// <summary>
    /// Config of the remote-control web API (#207/#25). OPT-IN (disabled by default), token REQUIRED.
    /// Stored in configs/webapi.json; the token is encrypted at rest (DPAPI via Secret).
    /// ⚠️ HttpListener = PLAIN HTTP: for internet exposure, put it behind an HTTPS reverse-proxy.
    /// </summary>
    public class WebApiConfig
    {
        public bool Enabled = false;
        public bool WebUiEnabled = false; // web portal (login + dashboard) on top of the token API
        public int Port = 8642;
        // Listen IP/host (HttpListener prefix): "127.0.0.1" = local only (recommended, behind a reverse-proxy),
        // "+" = all interfaces (requires elevated WGSM/urlacl), or a specific machine IP.
        public string BindAddress = "127.0.0.1";
        // The web portal listens on its OWN IP:port (independent of the token API above). If it matches the
        // API endpoint exactly, a single listener serves both.
        public int WebUiPort = 8643;
        public string WebUiBindAddress = "127.0.0.1";
        // Adds the Secure attribute to the session cookie (enable when WGSM is behind an HTTPS reverse-proxy).
        public bool CookieSecure = false;
        public string Token = string.Empty; // plaintext in memory; encrypted on disk

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
                        cfg.Token = Secret.Unprotect(cfg.Token) ?? string.Empty; // decrypt
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
                    WebUiEnabled = WebUiEnabled,
                    Port = Port,
                    BindAddress = string.IsNullOrWhiteSpace(BindAddress) ? "127.0.0.1" : BindAddress.Trim(),
                    WebUiPort = WebUiPort,
                    WebUiBindAddress = string.IsNullOrWhiteSpace(WebUiBindAddress) ? "127.0.0.1" : WebUiBindAddress.Trim(),
                    CookieSecure = CookieSecure,
                    Token = string.IsNullOrEmpty(Token) ? string.Empty : Secret.Protect(Token) // encrypt
                };
                File.WriteAllText(Path, JsonConvert.SerializeObject(onDisk, Formatting.Indented));
            }
            catch (Exception ex) { AppLog.Warn("WebApi/Save", ex.Message); }
        }
    }
}
