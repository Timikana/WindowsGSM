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
    /// Embedded server (#207/#25): token API + WEB PORTAL (login, sessions, roles).
    ///   API (Bearer token)  : GET /api/servers, POST /api/servers/{id}/{start|stop|restart|backup}
    ///   Web (session cookie): GET / (login/dashboard), POST /login, GET /logout
    /// Rights: Viewer=read, Operator=+control/backup, Admin=all; per-account server allowlist.
    /// Security: encrypted token, PBKDF2 passwords, HttpOnly+SameSite=Strict cookie, hardened headers, brute-force throttle.
    /// ⚠️ Plain HTTP -> put behind an HTTPS reverse-proxy for internet exposure.
    /// </summary>
    public class WebApiServer
    {
        private HttpListener _api;      // token API listener (BindAddress:Port)
        private HttpListener _portal;   // web portal listener (WebUiBindAddress:WebUiPort); == _api when same endpoint
        private string _token;
        private bool _webUi;
        private bool _cookieSecure;
        private static readonly string[] AllowedActions = { "start", "stop", "restart", "backup" };
        private readonly Func<string> _getServersJson;
        private readonly Func<string, string, (bool ok, string msg)> _doAction;

        public string LastError = string.Empty;
        public bool IsRunning => (_api != null && _api.IsListening) || (_portal != null && _portal.IsListening);

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
            if (!cfg.Enabled) { LastError = "API disabled."; return false; }
            if (string.IsNullOrWhiteSpace(cfg.Token) && !cfg.WebUiEnabled) { LastError = "Token required (or enable the web portal with accounts)."; return false; }
            _token = cfg.Token;
            _cookieSecure = cfg.CookieSecure;
            // The web portal (auth + roles) is a donator feature: only enabled for a donator/owner.
            _webUi = cfg.WebUiEnabled && Donator.DonatorManager.IsDonator;
            if (cfg.WebUiEnabled && !_webUi) { AppLog.Warn("WebApi", "Web portal skipped: donator-only feature."); }

            bool haveApi = !string.IsNullOrWhiteSpace(_token);
            if (!haveApi && !_webUi) { LastError = "Token required (the web portal is donator-only)."; return false; }

            string apiHost = Norm(cfg.BindAddress);
            string webHost = Norm(cfg.WebUiBindAddress);
            // The token API and the web portal each listen on their own IP:port. If both are enabled
            // on the SAME endpoint, a single listener serves both (can't bind the same prefix twice).
            bool sameEndpoint = haveApi && _webUi && cfg.Port == cfg.WebUiPort && apiHost.Equals(webHost, StringComparison.OrdinalIgnoreCase);

            try
            {
                if (haveApi)
                {
                    _api = Listen(apiHost, cfg.Port);
                    // If they share the endpoint, this listener also serves the portal routes.
                    BeginGet(_api, isPortal: sameEndpoint);
                    AppLog.Info("WebApi", $"API listening on http://{apiHost}:{cfg.Port}/{(sameEndpoint ? " (+ portal)" : "")}.");
                }
                if (_webUi && !sameEndpoint)
                {
                    _portal = Listen(webHost, cfg.WebUiPort);
                    BeginGet(_portal, isPortal: true);
                    AppLog.Info("WebApi", $"Web portal listening on http://{webHost}:{cfg.WebUiPort}/.");
                }
            }
            catch (HttpListenerException e)
            {
                Stop();
                LastError = e.Message + " — check the port isn't already in use; listening outside 127.0.0.1 requires WGSM as ADMINISTRATOR (or 'netsh http add urlacl').";
                return false;
            }
            catch (Exception e) { Stop(); LastError = e.Message; return false; }
            return true;
        }

        private static string Norm(string host)
        {
            host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            // HttpListener has no notion of "0.0.0.0"/"::" (it treats them as a literal host that never
            // matches). The all-interfaces wildcard is "+". Map the usual "listen everywhere" spellings to it.
            if (host == "0.0.0.0" || host == "::" || host == "[::]" || host == "*") { return "+"; }
            return host;
        }

        private static HttpListener Listen(string host, int port)
        {
            var l = new HttpListener();
            l.Prefixes.Add($"http://{host}:{port}/");
            l.Start();
            return l;
        }

        public void Stop()
        {
            try { _api?.Stop(); _api?.Close(); } catch { }
            try { if (_portal != null && _portal != _api) { _portal.Stop(); _portal.Close(); } } catch { }
            _api = null;
            _portal = null;
            _sessions.Clear();
        }

        private void BeginGet(HttpListener listener, bool isPortal)
        {
            try { listener.BeginGetContext(OnContext, (listener, isPortal)); } catch { }
        }

        private void OnContext(IAsyncResult ar)
        {
            var (listener, isPortal) = ((HttpListener, bool))ar.AsyncState;
            if (listener == null || !listener.IsListening) { return; }
            HttpListenerContext ctx;
            try { ctx = listener.EndGetContext(ar); } catch { return; }
            BeginGet(listener, isPortal);
            try { Handle(ctx, isPortal); }
            catch (Exception e) { AppLog.Warn("WebApi/Handle", e.Message); try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { } }
        }

        private void Handle(HttpListenerContext ctx, bool isPortal)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            string ip = req.RemoteEndPoint?.Address?.ToString() ?? "?";
            string path = (req.Url.AbsolutePath ?? "/").TrimEnd('/');
            if (path == string.Empty) { path = "/"; }
            string method = req.HttpMethod.ToUpperInvariant();

            if (IsBlocked(ip)) { WriteJson(res, 429, "{\"error\":\"too many failed attempts\"}"); return; }

            // ---- Web portal (session cookie) — only on the portal listener ----
            if (_webUi && isPortal)
            {
                if (path == "/" && method == "GET") { ServePage(res, GetSession(req) != null); return; }
                if (path == "/login" && method == "POST") { HandleLogin(req, res, ip); return; }
                if (path == "/logout") { HandleLogout(req, res); return; }
            }

            // ---- API (Bearer token OR session) ----
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
                // A03: id = digits only, action = strict allowlist (nothing else reaches the backend).
                if (!IsValidId(id) || Array.IndexOf(AllowedActions, action) < 0)
                {
                    WriteJson(res, 400, "{\"error\":\"invalid request\"}");
                    return;
                }
                // A01/CSRF: for a session-cookie action, the Origin (if provided) must match the host.
                if (!prin.ViaToken && !SameOrigin(req))
                {
                    AppLog.Warn("WebApi/Audit", $"CSRF denied id={id} action={action} ip={ip} origin={req.Headers["Origin"]}");
                    WriteJson(res, 403, "{\"error\":\"bad origin\"}");
                    return;
                }
                // Rights: token = full access; otherwise Operator+ AND allowed server.
                if (!prin.ViaToken && (!prin.User.CanControl || !prin.User.AllowsServer(id)))
                {
                    AppLog.Warn("WebApi/Audit", $"Action denied (rights) user={prin.User?.Username} id={id} action={action} ip={ip}");
                    WriteJson(res, 403, "{\"error\":\"forbidden\"}");
                    return;
                }
                var (ok, msg) = _doAction(id, action);
                AppLog.Info("WebApi/Audit", $"Action {action} on #{id} by {(prin.ViaToken ? "token" : prin.User?.Username)} ip={ip} -> {(ok ? "OK" : "denied")}");
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
            // NB: the token is NOT accepted as a query-string (?token=) — it would leak into proxy logs/Referer.

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

        /// <summary>A01/CSRF: if an Origin/Referer header is present, its host must match the request host.</summary>
        private static bool SameOrigin(HttpListenerRequest req)
        {
            string origin = req.Headers["Origin"];
            if (string.IsNullOrEmpty(origin)) { origin = req.Headers["Referer"]; }
            if (string.IsNullOrEmpty(origin)) { return true; } // absent (same-origin navigation): covered by SameSite=Strict
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

        private const long MaxLoginBody = 4096; // A04: a urlencoded login fits well within 4 KB

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
                AppLog.Warn("WebApi/Audit", $"Login failed user={user} ip={ip}");
                System.Threading.Thread.Sleep(400); // slows brute-force, mitigates timing
                ServePage(res, false, "Invalid credentials.");
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

        // ---- Brute-force throttle (per IP) ----
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

        // ---- Responses ----
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
            // A05: don't disclose the stack (HttpListener adds "Server: Microsoft-HTTPAPI/2.0").
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
            return @"<!doctype html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>WindowsGSM — Sign in</title><style>
body{background:#1b1b1b;color:#eaeaea;font-family:Segoe UI,Arial,sans-serif;display:flex;min-height:100vh;align-items:center;justify-content:center;margin:0}
.card{background:#252525;padding:28px 32px;border-radius:10px;box-shadow:0 8px 30px #0008;width:300px}
h1{font-size:18px;margin:0 0 16px;color:#4cc2d6}
input{width:100%;box-sizing:border-box;margin:6px 0;padding:10px;border:1px solid #3a3a3a;border-radius:6px;background:#1b1b1b;color:#eaeaea}
button{width:100%;margin-top:12px;padding:10px;border:0;border-radius:6px;background:#4cc2d6;color:#08272d;font-weight:600;cursor:pointer}
.err{color:#e06c6c;font-size:13px}</style></head><body>
<form class='card' method='post' action='/login'><h1>WindowsGSM</h1>" + err + @"
<input name='username' placeholder='Username' autofocus autocomplete='username'>
<input name='password' type='password' placeholder='Password' autocomplete='current-password'>
<button type='submit'>Sign in</button></form></body></html>";
        }

        private static string DashboardHtml()
        {
            return @"<!doctype html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
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
<h1>WindowsGSM</h1><a class='logout' href='/logout'>Sign out</a>
<div id='msg'></div><table><thead><tr><th>ID</th><th>Name</th><th>Game</th><th>Status</th><th>Players</th><th>Actions</th></tr></thead><tbody id='b'></tbody></table>
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
 }catch(e){document.getElementById('msg').textContent='Load error.';}
}
async function act(id,a){
 document.getElementById('msg').textContent='...';
 try{
  var r=await fetch('/api/servers/'+id+'/'+a,{method:'POST',credentials:'same-origin'});
  var j=await r.json();
  document.getElementById('msg').textContent=(r.status==202?'OK: ':'Denied: ')+(j.message||j.error||r.status);
  setTimeout(load,1500);
 }catch(e){document.getElementById('msg').textContent='Error.';}
}
load();setInterval(load,5000);
</script></body></html>";
        }
    }
}
