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
            string host = string.IsNullOrWhiteSpace(cfg.BindAddress) ? "127.0.0.1" : cfg.BindAddress.Trim();
            _listener.Prefixes.Add($"http://{host}:{cfg.Port}/");
            try { _listener.Start(); }
            catch (Exception e)
            {
                bool nonLocal = host != "127.0.0.1" && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
                LastError = e.Message + (nonLocal ? $" — écoute hors localhost : lance WGSM en ADMINISTRATEUR, ou réserve l'URL : netsh http add urlacl url=http://{host}:{cfg.Port}/ user=Tout_le_monde" : "");
                _listener = null;
                return false;
            }
            BeginGet();
            AppLog.Info("WebApi", $"API démarrée sur http://{host}:{cfg.Port}/.");
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
            string ip = req.RemoteEndPoint?.Address?.ToString() ?? "?";

            // Anti-brute-force : bloque une IP après trop d'échecs d'authentification.
            if (IsBlocked(ip)) { Write(res, 429, "{\"error\":\"too many failed attempts\"}"); return; }

            // --- Auth : Bearer header ou ?token= ---
            string token = null;
            string auth = req.Headers["Authorization"];
            if (auth != null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) { token = auth.Substring(7).Trim(); }
            if (string.IsNullOrEmpty(token)) { token = req.QueryString["token"]; }
            if (!TokenValid(token)) { RecordFail(ip); Write(res, 401, "{\"error\":\"unauthorized\"}"); return; }
            ResetFails(ip);

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

        // --- Anti-brute-force du token (en mémoire, par IP) ---
        private const int MaxFails = 10;
        private static readonly TimeSpan FailWindow = TimeSpan.FromMinutes(5);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int fails, DateTime since)> _failsByIp
            = new System.Collections.Concurrent.ConcurrentDictionary<string, (int, DateTime)>();

        private bool IsBlocked(string ip)
        {
            if (_failsByIp.TryGetValue(ip, out var v))
            {
                if (DateTime.UtcNow - v.since > FailWindow) { _failsByIp.TryRemove(ip, out _); return false; }
                return v.fails >= MaxFails;
            }
            return false;
        }
        private void RecordFail(string ip) =>
            _failsByIp.AddOrUpdate(ip, (1, DateTime.UtcNow), (k, v) => (DateTime.UtcNow - v.since > FailWindow) ? (1, DateTime.UtcNow) : (v.fails + 1, v.since));
        private void ResetFails(string ip) { _failsByIp.TryRemove(ip, out _); }

        private static void Write(HttpListenerResponse res, int code, string json)
        {
            try
            {
                res.StatusCode = code;
                res.ContentType = "application/json";
                // En-têtes HTTP de sécurité (durcissement réponse).
                res.AddHeader("X-Content-Type-Options", "nosniff");
                res.AddHeader("X-Frame-Options", "DENY");
                res.AddHeader("Referrer-Policy", "no-referrer");
                res.AddHeader("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
                res.AddHeader("Cache-Control", "no-store");
                res.AddHeader("X-Robots-Tag", "noindex, nofollow");
                byte[] b = Encoding.UTF8.GetBytes(json);
                res.ContentLength64 = b.Length;
                res.OutputStream.Write(b, 0, b.Length);
            }
            catch { }
            finally { try { res.Close(); } catch { } }
        }
    }
}
