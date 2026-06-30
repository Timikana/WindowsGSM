using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace WindowsGSM.Functions.WebApi
{
    /// <summary>
    /// API web de contrôle à distance (#207/#25). HttpListener + token Bearer obligatoire.
    ///   GET  /api/servers                       -> liste + statut (JSON)
    ///   POST /api/servers/{id}/{start|stop|restart|backup}  -> déclenche l'action (202 si accepté)
    /// Les callbacks (fournis par MainWindow) marshalent vers le thread UI.
    /// ⚠️ HTTP en clair : pour internet, mettre derrière un reverse-proxy HTTPS.
    /// </summary>
    public class WebApiServer
    {
        private HttpListener _listener;
        private string _token;
        private readonly Func<string> _getServersJson;
        private readonly Func<string, string, (bool ok, string msg)> _doAction;

        public string LastError = string.Empty;
        public bool IsRunning => _listener != null && _listener.IsListening;

        public WebApiServer(Func<string> getServersJson, Func<string, string, (bool, string)> doAction)
        {
            _getServersJson = getServersJson;
            _doAction = doAction;
        }

        public bool Start()
        {
            Stop();
            var cfg = WebApiConfig.Load();
            if (!cfg.Enabled) { LastError = "API désactivée."; return false; }
            if (string.IsNullOrWhiteSpace(cfg.Token)) { LastError = "Token requis (sécurité) : aucune API exposée sans token."; return false; }
            _token = cfg.Token;
            _listener = new HttpListener();
            string host = cfg.BindAll ? "+" : "localhost";
            _listener.Prefixes.Add($"http://{host}:{cfg.Port}/");
            try { _listener.Start(); }
            catch (Exception e)
            {
                LastError = e.Message + (cfg.BindAll ? " — écoute sur toutes interfaces : lance WGSM en ADMINISTRATEUR, ou réserve l'URL (netsh http add urlacl)." : "");
                _listener = null;
                return false;
            }
            BeginGet();
            AppLog.Info("WebApi", $"API démarrée sur http://{host}:{cfg.Port}/ (bindAll={cfg.BindAll}).");
            return true;
        }

        public void Stop()
        {
            try { _listener?.Stop(); _listener?.Close(); } catch { }
            _listener = null;
        }

        private void BeginGet()
        {
            try { _listener.BeginGetContext(OnContext, null); } catch { }
        }

        private void OnContext(IAsyncResult ar)
        {
            if (_listener == null) { return; }
            HttpListenerContext ctx;
            try { ctx = _listener.EndGetContext(ar); }
            catch { return; }
            BeginGet(); // accepte la requête suivante
            try { Handle(ctx); }
            catch (Exception e) { AppLog.Warn("WebApi/Handle", e.Message); try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { } }
        }

        private void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            res.AddHeader("Access-Control-Allow-Origin", "*");

            // --- Auth : Bearer header ou ?token= ---
            string token = null;
            string auth = req.Headers["Authorization"];
            if (auth != null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) { token = auth.Substring(7).Trim(); }
            if (string.IsNullOrEmpty(token)) { token = req.QueryString["token"]; }
            if (!TokenValid(token)) { Write(res, 401, "{\"error\":\"unauthorized\"}"); return; }

            string path = (req.Url.AbsolutePath ?? "/").TrimEnd('/');

            if (req.HttpMethod == "GET" && path.Equals("/api/servers", StringComparison.OrdinalIgnoreCase))
            {
                Write(res, 200, _getServersJson() ?? "[]");
                return;
            }

            // POST /api/servers/{id}/{action}
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (req.HttpMethod == "POST" && parts.Length == 4 &&
                parts[0].Equals("api", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("servers", StringComparison.OrdinalIgnoreCase))
            {
                var (ok, msg) = _doAction(parts[2], parts[3].ToLowerInvariant());
                Write(res, ok ? 202 : 400, $"{{\"ok\":{(ok ? "true" : "false")},\"message\":{JsonConvert.ToString(msg ?? string.Empty)}}}");
                return;
            }

            Write(res, 404, "{\"error\":\"not found\"}");
        }

        private bool TokenValid(string t)
        {
            if (string.IsNullOrEmpty(t) || string.IsNullOrEmpty(_token)) { return false; }
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(t), Encoding.UTF8.GetBytes(_token));
        }

        private static void Write(HttpListenerResponse res, int code, string json)
        {
            try
            {
                res.StatusCode = code;
                res.ContentType = "application/json";
                byte[] b = Encoding.UTF8.GetBytes(json);
                res.ContentLength64 = b.Length;
                res.OutputStream.Write(b, 0, b.Length);
            }
            catch { }
            finally { try { res.Close(); } catch { } }
        }
    }
}
