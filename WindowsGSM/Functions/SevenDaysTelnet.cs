using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Persistent Telnet console for 7 Days to Die servers. One long-lived connection per server:
    /// a background reader streams the live server output into the console panel, and typed commands
    /// are sent over the SAME connection (no reconnect-per-command). 7DtD accepts no stdin and its log
    /// goes to a file, so Telnet is the only real console access.
    /// </summary>
    public static class SevenDaysTelnet
    {
        private sealed class Session
        {
            public TcpClient Client;
            public NetworkStream Stream;
            public CancellationTokenSource Cts;
            public string Key;          // host:port — reconnect if the config changes
            public bool LoggedIn;
        }

        private static readonly ConcurrentDictionary<string, Session> _sessions =
            new ConcurrentDictionary<string, Session>();

        // One connect at a time per server: the player-count poll (every 60s) and a user command can
        // both call EnsureAsync concurrently — without this they could each open a socket and clobber
        // the other's session.
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
            new ConcurrentDictionary<string, SemaphoreSlim>();

        // Noise from the background player-count poll (a separate Telnet client running "lp" every minute):
        // 7DtD broadcasts command execution to every connected client, so filter those lines out.
        private static readonly Regex _lpEcho = new Regex(@"Executing command 'lp'", RegexOptions.Compiled);
        private static readonly Regex _lpTotal = new Regex(@"^Total of \d+ in the game", RegexOptions.Compiled);

        public static bool IsConnected(string serverId) =>
            _sessions.TryGetValue(serverId, out var s) && s.Client != null && s.Client.Connected;

        /// <summary>Opens (or reuses) a live session: connect + login + start the streaming reader.</summary>
        public static async Task<bool> EnsureAsync(string serverId, string host, int port, string password, ServerConsole console)
        {
            if (string.IsNullOrWhiteSpace(host) || host == "0.0.0.0") { host = "127.0.0.1"; }
            if (port <= 0) { return false; }
            string key = host + ":" + port;

            var gate = _locks.GetOrAdd(serverId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_sessions.TryGetValue(serverId, out var existing)
                    && existing.Client != null && existing.Client.Connected && existing.Key == key)
                {
                    return true; // already live for this endpoint
                }

                // Build + connect a fresh session BEFORE touching the dictionary, so a failed/timed-out
                // connect disposes its own socket and never leaves a half-session behind.
                var session = new Session { Key = key, Cts = new CancellationTokenSource(), Client = new TcpClient() };
                try
                {
                    var connect = session.Client.ConnectAsync(host, port);
                    if (await Task.WhenAny(connect, Task.Delay(4000)).ConfigureAwait(false) != connect || !session.Client.Connected)
                    {
                        DisposeSession(session);
                        return false;
                    }
                    session.Stream = session.Client.GetStream();
                }
                catch { DisposeSession(session); return false; }

                // Publish: close any previous session for this server, then install ours.
                CloseInternal(serverId);
                _sessions[serverId] = session;

                try
                {
                    await Task.Delay(350).ConfigureAwait(false); // let the banner + password prompt arrive
                    if (!string.IsNullOrEmpty(password))
                    {
                        var pw = Encoding.ASCII.GetBytes(password + "\r\n");
                        await session.Stream.WriteAsync(pw, 0, pw.Length).ConfigureAwait(false);
                        session.LoggedIn = true;
                    }
                    _ = Task.Run(() => ReadLoopAsync(serverId, session, console));
                    return true;
                }
                catch { CloseInternal(serverId, session); return false; }
            }
            finally { gate.Release(); }
        }

        private static async Task ReadLoopAsync(string serverId, Session session, ServerConsole console)
        {
            var enc = Encoding.ASCII;
            var buf = new byte[8192];
            var pending = new StringBuilder();
            try
            {
                while (!session.Cts.IsCancellationRequested && session.Client.Connected)
                {
                    int n = await session.Stream.ReadAsync(buf, 0, buf.Length, session.Cts.Token).ConfigureAwait(false);
                    if (n <= 0) { break; }
                    pending.Append(enc.GetString(buf, 0, n));

                    string all = pending.ToString();
                    int idx;
                    while ((idx = all.IndexOf('\n')) >= 0)
                    {
                        string line = all.Substring(0, idx).Replace("\r", string.Empty).TrimEnd();
                        all = all.Substring(idx + 1);
                        if (ShouldShow(line)) { console.Add(line); }
                    }
                    pending.Clear();
                    pending.Append(all);
                }
            }
            catch { /* connection dropped -> cleaned up below */ }
            finally { CloseInternal(serverId, session); } // identity-checked: never drop a newer session
        }

        private static bool ShouldShow(string line)
        {
            if (line.Length == 0) { return false; }
            if (line.StartsWith("Please enter password", StringComparison.OrdinalIgnoreCase)) { return false; }
            if (line.StartsWith("Logon successful", StringComparison.OrdinalIgnoreCase)) { return false; }
            if (_lpEcho.IsMatch(line) || _lpTotal.IsMatch(line)) { return false; } // background player-count poll
            return true;
        }

        /// <summary>Sends a command over the live session. Returns false if there is no connection.</summary>
        public static async Task<bool> SendAsync(string serverId, string command)
        {
            if (!_sessions.TryGetValue(serverId, out var s) || s.Stream == null || s.Client == null || !s.Client.Connected)
            {
                return false;
            }
            try
            {
                var b = Encoding.ASCII.GetBytes(command + "\r\n");
                await s.Stream.WriteAsync(b, 0, b.Length).ConfigureAwait(false);
                return true;
            }
            catch { CloseInternal(serverId); return false; }
        }

        public static void CloseSession(string serverId) => CloseInternal(serverId);

        private static void CloseInternal(string serverId)
        {
            if (_sessions.TryRemove(serverId, out var s)) { DisposeSession(s); }
        }

        // Remove + dispose ONLY if the stored session is still this one (avoids a dying reader tearing
        // down a session that EnsureAsync just replaced for the same server).
        private static void CloseInternal(string serverId, Session only)
        {
            if (only == null) { return; }
            var pair = new System.Collections.Generic.KeyValuePair<string, Session>(serverId, only);
            if (((System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, Session>>)_sessions).Remove(pair))
            {
                DisposeSession(only);
            }
        }

        private static void DisposeSession(Session s)
        {
            if (s == null) { return; }
            try { s.Cts?.Cancel(); } catch { }
            try { s.Cts?.Dispose(); } catch { }
            try { s.Stream?.Dispose(); } catch { }
            try { s.Client?.Close(); } catch { }
        }
    }
}
