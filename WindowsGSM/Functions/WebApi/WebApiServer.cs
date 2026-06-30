using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Newtonsoft.Json;

namespace WindowsGSM.Functions.WebApi
{
    /// <summary>
    /// Serveur embarqué (#207/#25) : API token + PORTAIL WEB (login, sessions, rôles).
    ///   API (token Bearer)  : GET /api/servers, POST /api/servers/{id}/{start|stop|restart|backup}
    ///   Web (cookie session): GET / (login/dashboard), POST /login, GET /logout
    /// Droits : Viewer=lecture, Operator=+contrôle/backup, Admin=tout ; allowlist de serveurs par compte.
    /// Sécu : token chiffré, mots de passe PBKDF2, cookie HttpOnly+SameSite=Strict, en-têtes durcis, throttle anti-brute-force.
    /// ⚠️ HTTP clair -> reverse-proxy HTTPS pour l'exposition internet.
    /// </summary>
    public class WebApiServer
    {
        private HttpListener _listener;
        private string _token;
        private bool _webUi;
        private bool _cookieSecure;
        private static readonly string[] AllowedActions = { "start", "stop", "restart", "backup" };
        private readonly Func<string> _getServersJson;
        private readonly Func<string, string, (bool ok, string msg)> _doAction;

        public string LastError = string.Empty;
        public bool IsRunning => _listener != null && _listener.IsListening;

        private sealed class Session { public string User; public DateTime Expiry; }
        private readonly ConcurrentDictionary<string, Session> _sessions = new ConcurrentDictionary<string, Session>();
        private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(8);

        private sealed class Principal { public bool ViaToken; public WebUser User; }

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
            if (string.IsNullOrWhiteSpace(cfg.Token) && !cfg.WebUiEnabled) { LastError = "Token requis (ou active le portail web avec des comptes)."; return false; }
            _token = cfg.Token;
            _cookieSecure = cfg.CookieSecure;
            // Le portail web (auth + rôles) est une fonction donateur : ne s'active que pour un donateur/propriétaire.
            _webUi = cfg.WebUiEnabled && Donator.DonatorManager.IsDonator;
            if (cfg.WebUiEnabled && !_webUi) { AppLog.Warn("WebApi", "Portail web ignoré : fonction réservée aux donateurs."); }
            if (string.IsNullOrWhiteSpace(_token) && !_webUi) { LastError = "Token requis (le portail web est réservé aux donateurs)."; _listener = null; return false; }
            _listener = new HttpListener();
            string host = string.IsNullOrWhiteSpace(cfg.BindAddress) ? "127.0.0.1" : cfg.BindAddress.Trim();
            _listener.Prefixes.Add($"http://{host}:{cfg.Port}/");
            try { _listener.Start(); }
            catch (Exception e)
            {
                bool nonLocal = host != "127.0.0.1" && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
                LastError = e.Message + (nonLocal ? $" — écoute hors localhost : lance WGSM en ADMINISTRATEUR, ou netsh http add urlacl url=http://{host}:{cfg.Port}/ user=Tout_le_monde" : "");
                _listener = null;
                return false;
            }
            BeginGet();
            AppLog.Info("WebApi", $"Démarré sur http://{host}:{cfg.Port}/ (web={_webUi}).");
            return true;
        }

        public void Stop()
        {
            try { _listener?.Stop(); _listener?.Close(); } catch { }
            _listener = null;
            _sessions.Clear();
        }

        private void BeginGet() { try { _listener.BeginGetContext(OnContext, null); } catch { } }

        private void OnContext(IAsyncResult ar)
        {
            if (_listener == null) { return; }
            HttpListenerContext ctx;
            try { ctx = _listener.EndGetContext(ar); } catch { return; }
            BeginGet();
            try { Handle(ctx); }
            catch (Exception e) { AppLog.Warn("WebApi/Handle", e.Message); try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { } }
        }

        private void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            string ip = req.RemoteEndPoint?.Address?.ToString() ?? "?";
            string path = (req.Url.AbsolutePath ?? "/").TrimEnd('/');
            if (path == string.Empty) { path = "/"; }
            string method = req.HttpMethod.ToUpperInvariant();

            if (IsBlocked(ip)) { WriteJson(res, 429, "{\"error\":\"too many failed attempts\"}"); return; }

            // ---- Portail web (cookie session) ----
            if (_webUi)
            {
                if (path == "/" && method == "GET") { ServePage(res, GetSession(req) != null); return; }
                if (path == "/login" && method == "POST") { HandleLogin(req, res, ip); return; }
                if (path == "/logout") { HandleLogout(req, res); return; }
            }

            // ---- API (token Bearer OU session) ----
            Principal prin = ResolvePrincipal(req);
            if (prin == null) { RecordFail(ip); WriteJson(res, 401, "{\"error\":\"unauthorized\"}"); return; }
            ResetFails(ip);

            if (method == "GET" && path.Equals("/api/servers", StringComparison.OrdinalIgnoreCase))
            {
                WriteJson(res, 200, _getServersJson() ?? "[]");
                return;
            }

            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (method == "POST" && parts.Length == 4 &&
                parts[0].Equals("api", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("servers", StringComparison.OrdinalIgnoreCase))
            {
                string id = parts[2]; string action = parts[3].ToLowerInvariant();
                // A03 : id = chiffres uniquement, action = liste blanche stricte (rien d'autre n'atteint le backend).
                if (!IsValidId(id) || Array.IndexOf(AllowedActions, action) < 0)
                {
                    WriteJson(res, 400, "{\"error\":\"invalid request\"}");
                    return;
                }
                // A01/CSRF : pour une action via cookie de session, l'Origin (si fourni) doit correspondre à l'hôte.
                if (!prin.ViaToken && !SameOrigin(req))
                {
                    AppLog.Warn("WebApi/Audit", $"CSRF refusé id={id} action={action} ip={ip} origin={req.Headers["Origin"]}");
                    WriteJson(res, 403, "{\"error\":\"bad origin\"}");
                    return;
                }
                // Droits : token = plein accès ; sinon Operator+ ET serveur autorisé.
                if (!prin.ViaToken && (!prin.User.CanControl || !prin.User.AllowsServer(id)))
                {
                    AppLog.Warn("WebApi/Audit", $"Action refusée (droits) user={prin.User?.Username} id={id} action={action} ip={ip}");
                    WriteJson(res, 403, "{\"error\":\"forbidden\"}");
                    return;
                }
                var (ok, msg) = _doAction(id, action);
                AppLog.Info("WebApi/Audit", $"Action {action} sur #{id} par {(prin.ViaToken ? "token" : prin.User?.Username)} ip={ip} -> {(ok ? "OK" : "refus")}");
                WriteJson(res, ok ? 202 : 400, $"{{\"ok\":{(ok ? "true" : "false")},\"message\":{JsonConvert.ToString(msg ?? string.Empty)}}}");
                return;
            }

            WriteJson(res, 404, "{\"error\":\"not found\"}");
        }

        // ---- Auth ----
        private Principal ResolvePrincipal(HttpListenerRequest req)
        {
            string auth = req.Headers["Authorization"];
            if (auth != null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                string t = auth.Substring(7).Trim();
                if (TokenValid(t)) { return new Principal { ViaToken = true }; }
            }
            // NB : on n'accepte PAS le token en query-string (?token=) — il fuiterait dans les logs proxy/Referer.

            var s = GetSession(req);
            if (s != null)
            {
                var u = WebUsers.Load().Users.Find(x => string.Equals(x.Username, s.User, StringComparison.OrdinalIgnoreCase));
                if (u != null) { return new Principal { User = u }; }
            }
            return null;
        }

        private bool TokenValid(string t)
        {
            if (string.IsNullOrEmpty(t) || string.IsNullOrEmpty(_token)) { return false; }
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(t), Encoding.UTF8.GetBytes(_token));
        }

        private static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 6) { return false; }
            foreach (char c in id) { if (c < '0' || c > '9') { return false; } }
            return true;
        }

        /// <summary>A01/CSRF : si un en-tête Origin/Referer est présent, son hôte doit correspondre à celui de la requête.</summary>
        private static bool SameOrigin(HttpListenerRequest req)
        {
            string origin = req.Headers["Origin"];
            if (string.IsNullOrEmpty(origin)) { origin = req.Headers["Referer"]; }
            if (string.IsNullOrEmpty(origin)) { return true; } // absent (navigation same-origin) : SameSite=Strict couvre le cas
            return Uri.TryCreate(origin, UriKind.Absolute, out var o) && req.Url != null &&
                   string.Equals(o.Host, req.Url.Host, StringComparison.OrdinalIgnoreCase) && o.Port == req.Url.Port;
        }

        private Session GetSession(HttpListenerRequest req)
        {
            try
            {
                var c = req.Cookies["wgsm_session"];
                if (c == null || string.IsNullOrEmpty(c.Value)) { return null; }
                if (_sessions.TryGetValue(c.Value, out var s))
                {
                    if (DateTime.UtcNow > s.Expiry) { _sessions.TryRemove(c.Value, out _); return null; }
                    return s;
                }
            }
            catch { }
            return null;
        }

        private const long MaxLoginBody = 4096; // A04 : un login urlencodé tient largement dans 4 Ko

        private void HandleLogin(HttpListenerRequest req, HttpListenerResponse res, string ip)
        {
            if (req.ContentLength64 > MaxLoginBody) { WriteJson(res, 413, "{\"error\":\"payload too large\"}"); return; }
            string body;
            using (var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8)) { body = sr.ReadToEnd(); }
            if (body.Length > MaxLoginBody) { WriteJson(res, 413, "{\"error\":\"payload too large\"}"); return; }
            var form = HttpUtility.ParseQueryString(body);
            string user = form["username"]; string pass = form["password"];
            var u = WebUsers.Load().Verify(user, pass);
            if (u == null)
            {
                RecordFail(ip);
                AppLog.Warn("WebApi/Audit", $"Login échoué user={user} ip={ip}");
                System.Threading.Thread.Sleep(400); // ralentit le bruteforce, atténue le timing
                ServePage(res, false, "Identifiants invalides.");
                return;
            }
            ResetFails(ip);
            AppLog.Info("WebApi/Audit", $"Login OK user={u.Username} ({u.Role}) ip={ip}");
            string sid = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            _sessions[sid] = new Session { User = u.Username, Expiry = DateTime.UtcNow + SessionTtl };
            res.AddHeader("Set-Cookie", $"wgsm_session={sid}; HttpOnly; SameSite=Strict; Path=/{(_cookieSecure ? "; Secure" : string.Empty)}");
            Redirect(res, "/");
        }

        private void HandleLogout(HttpListenerRequest req, HttpListenerResponse res)
        {
            try { var c = req.Cookies["wgsm_session"]; if (c != null) { _sessions.TryRemove(c.Value, out _); } } catch { }
            res.AddHeader("Set-Cookie", $"wgsm_session=; HttpOnly; SameSite=Strict; Path=/; Max-Age=0{(_cookieSecure ? "; Secure" : string.Empty)}");
            Redirect(res, "/");
        }

        // ---- Anti-brute-force (par IP) ----
        private const int MaxFails = 10;
        private static readonly TimeSpan FailWindow = TimeSpan.FromMinutes(5);
        private readonly ConcurrentDictionary<string, (int fails, DateTime since)> _failsByIp = new ConcurrentDictionary<string, (int, DateTime)>();
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

        // ---- Réponses ----
        private static void SecurityHeaders(HttpListenerResponse res, bool html)
        {
            res.AddHeader("X-Content-Type-Options", "nosniff");
            res.AddHeader("X-Frame-Options", "DENY");
            res.AddHeader("Referrer-Policy", "no-referrer");
            res.AddHeader("Cache-Control", "no-store");
            res.AddHeader("X-Robots-Tag", "noindex, nofollow");
            res.AddHeader("X-Permitted-Cross-Domain-Policies", "none");
            res.AddHeader("Permissions-Policy", "geolocation=(), microphone=(), camera=()");
            res.AddHeader("Content-Security-Policy", html
                ? "default-src 'none'; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'"
                : "default-src 'none'; base-uri 'none'; frame-ancestors 'none'");
            // A05 : ne pas divulguer la stack (HttpListener ajoute « Server: Microsoft-HTTPAPI/2.0 »).
            try { res.Headers.Remove("Server"); res.AddHeader("Server", ""); } catch { }
        }

        private static void WriteJson(HttpListenerResponse res, int code, string json)
        {
            try { res.StatusCode = code; res.ContentType = "application/json"; SecurityHeaders(res, false); var b = Encoding.UTF8.GetBytes(json); res.ContentLength64 = b.Length; res.OutputStream.Write(b, 0, b.Length); }
            catch { } finally { try { res.Close(); } catch { } }
        }

        private static void WriteHtml(HttpListenerResponse res, int code, string html)
        {
            try { res.StatusCode = code; res.ContentType = "text/html; charset=utf-8"; SecurityHeaders(res, true); var b = Encoding.UTF8.GetBytes(html); res.ContentLength64 = b.Length; res.OutputStream.Write(b, 0, b.Length); }
            catch { } finally { try { res.Close(); } catch { } }
        }

        private static void Redirect(HttpListenerResponse res, string location)
        {
            try { res.StatusCode = 302; res.AddHeader("Location", location); SecurityHeaders(res, true); }
            catch { } finally { try { res.Close(); } catch { } }
        }

        private void ServePage(HttpListenerResponse res, bool loggedIn, string error = null)
        {
            WriteHtml(res, 200, loggedIn ? DashboardHtml() : LoginHtml(error));
        }

        private static string LoginHtml(string error)
        {
            string err = string.IsNullOrEmpty(error) ? string.Empty : $"<p class='err'>{HttpUtility.HtmlEncode(error)}</p>";
            return @"<!doctype html><html lang='fr'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>WindowsGSM — Connexion</title><style>
body{background:#1b1b1b;color:#eaeaea;font-family:Segoe UI,Arial,sans-serif;display:flex;min-height:100vh;align-items:center;justify-content:center;margin:0}
.card{background:#252525;padding:28px 32px;border-radius:10px;box-shadow:0 8px 30px #0008;width:300px}
h1{font-size:18px;margin:0 0 16px;color:#4cc2d6}
input{width:100%;box-sizing:border-box;margin:6px 0;padding:10px;border:1px solid #3a3a3a;border-radius:6px;background:#1b1b1b;color:#eaeaea}
button{width:100%;margin-top:12px;padding:10px;border:0;border-radius:6px;background:#4cc2d6;color:#08272d;font-weight:600;cursor:pointer}
.err{color:#e06c6c;font-size:13px}</style></head><body>
<form class='card' method='post' action='/login'><h1>WindowsGSM</h1>" + err + @"
<input name='username' placeholder='Utilisateur' autofocus autocomplete='username'>
<input name='password' type='password' placeholder='Mot de passe' autocomplete='current-password'>
<button type='submit'>Se connecter</button></form></body></html>";
        }

        private static string DashboardHtml()
        {
            return @"<!doctype html><html lang='fr'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>WindowsGSM</title><style>
body{background:#1b1b1b;color:#eaeaea;font-family:Segoe UI,Arial,sans-serif;margin:0;padding:20px}
h1{color:#4cc2d6;font-size:20px;display:inline-block}
a.logout{float:right;color:#9a9a9a;text-decoration:none;margin-top:8px}
table{width:100%;border-collapse:collapse;margin-top:14px}
th,td{text-align:left;padding:8px 10px;border-bottom:1px solid #2f2f2f;font-size:14px}
th{color:#9a9a9a;font-weight:600}
.on{color:#6ad06a}.off{color:#9a9a9a}
button{margin:0 2px;padding:5px 9px;border:0;border-radius:5px;cursor:pointer;font-size:12px;color:#fff}
.start{background:#2e9e44}.stop{background:#c97a00}.restart{background:#2b8fd0}.backup{background:#555}
#msg{margin-top:10px;color:#4cc2d6;min-height:18px;font-size:13px}</style></head><body>
<h1>WindowsGSM</h1><a class='logout' href='/logout'>Déconnexion</a>
<div id='msg'></div><table><thead><tr><th>ID</th><th>Nom</th><th>Jeu</th><th>État</th><th>Joueurs</th><th>Actions</th></tr></thead><tbody id='b'></tbody></table>
<script>
async function load(){
 try{
  var r=await fetch('/api/servers',{credentials:'same-origin'});
  var a=await r.json();var b=document.getElementById('b');b.innerHTML='';
  a.forEach(function(s){
   var on=(s.status||'').toLowerCase().indexOf('start')>=0;
   var tr=document.createElement('tr');
   ['id','name','game'].forEach(function(k){var td=document.createElement('td');td.textContent=s[k]||'';tr.appendChild(td);});
   var st=document.createElement('td');st.textContent=s.status||'';st.className=on?'on':'off';tr.appendChild(st);
   var pl=document.createElement('td');pl.textContent=s.players||'';tr.appendChild(pl);
   var ac=document.createElement('td');
   [['start','Start'],['stop','Stop'],['restart','Restart'],['backup','Backup']].forEach(function(x){
    var btn=document.createElement('button');btn.textContent=x[1];btn.className=x[0];
    btn.addEventListener('click',function(){act(s.id,x[0]);});ac.appendChild(btn);});
   tr.appendChild(ac);b.appendChild(tr);
  });
 }catch(e){document.getElementById('msg').textContent='Erreur de chargement.';}
}
async function act(id,a){
 document.getElementById('msg').textContent='...';
 try{
  var r=await fetch('/api/servers/'+id+'/'+a,{method:'POST',credentials:'same-origin'});
  var j=await r.json();
  document.getElementById('msg').textContent=(r.status==202?'OK : ':'Refusé : ')+(j.message||j.error||r.status);
  setTimeout(load,1500);
 }catch(e){document.getElementById('msg').textContent='Erreur.';}
}
load();setInterval(load,5000);
</script></body></html>";
        }
    }
}
