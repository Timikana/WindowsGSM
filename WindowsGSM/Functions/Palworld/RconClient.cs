using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace WindowsGSM.Functions.Palworld
{
    /// <summary>
    /// Minimal Source RCON protocol client (TCP), used by Palworld and most Source-engine servers.
    /// One command per Connect/ExecuteAsync/Dispose cycle is fine; the socket is kept open for the
    /// lifetime of the instance. Best-effort: returns (ok, text/error) instead of throwing.
    /// </summary>
    public sealed class RconClient : IDisposable
    {
        private const int SERVERDATA_AUTH = 3;
        private const int SERVERDATA_EXECCOMMAND = 2;

        private readonly string _host;
        private readonly int _port;
        private TcpClient _tcp;
        private NetworkStream _stream;
        private int _id = 0;

        public RconClient(string host, int port)
        {
            _host = string.IsNullOrWhiteSpace(host) || host == "0.0.0.0" ? "127.0.0.1" : host;
            _port = port;
        }

        /// <summary>Connect + authenticate. Returns (ok, error).</summary>
        public async Task<(bool ok, string err)> ConnectAsync(string password, int timeoutMs = 4000)
        {
            if (_port <= 0) { return (false, "RCON port not set"); }
            try
            {
                _tcp = new TcpClient();
                var connect = _tcp.ConnectAsync(_host, _port);
                if (await Task.WhenAny(connect, Task.Delay(timeoutMs)).ConfigureAwait(false) != connect)
                {
                    return (false, "Connection timed out");
                }
                await connect.ConfigureAwait(false); // surface any connect exception
                _stream = _tcp.GetStream();
                _stream.ReadTimeout = timeoutMs;
                _stream.WriteTimeout = timeoutMs;

                int authId = NextId();
                await SendAsync(authId, SERVERDATA_AUTH, password ?? "").ConfigureAwait(false);
                var (rid, _, _) = await ReadAsync().ConfigureAwait(false);
                if (rid == -1) { return (false, "Authentication failed (wrong admin password?)"); }
                return (true, null);
            }
            catch (Exception e)
            {
                return (false, e.Message);
            }
        }

        /// <summary>Send a command and return its text response.</summary>
        public async Task<(bool ok, string text)> ExecuteAsync(string command)
        {
            try
            {
                int id = NextId();
                await SendAsync(id, SERVERDATA_EXECCOMMAND, command ?? "").ConfigureAwait(false);
                var (_, _, body) = await ReadAsync().ConfigureAwait(false);
                return (true, body ?? "");
            }
            catch (Exception e)
            {
                return (false, e.Message);
            }
        }

        private int NextId() => ++_id;

        private async Task SendAsync(int id, int type, string body)
        {
            byte[] payload = Encoding.UTF8.GetBytes(body ?? "");
            int size = 4 + 4 + payload.Length + 2; // id + type + body + 2 null terminators
            byte[] packet = new byte[4 + size];
            Buffer.BlockCopy(BitConverter.GetBytes(size), 0, packet, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(id), 0, packet, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(type), 0, packet, 8, 4);
            Buffer.BlockCopy(payload, 0, packet, 12, payload.Length);
            // last 2 bytes stay 0 (body null terminator + packet terminator)
            await _stream.WriteAsync(packet, 0, packet.Length).ConfigureAwait(false);
            await _stream.FlushAsync().ConfigureAwait(false);
        }

        private async Task<(int id, int type, string body)> ReadAsync()
        {
            byte[] lenBuf = await ReadExactAsync(4).ConfigureAwait(false);
            int size = BitConverter.ToInt32(lenBuf, 0);
            if (size < 10 || size > 4 * 1024 * 1024) { return (-1, 0, ""); }
            byte[] buf = await ReadExactAsync(size).ConfigureAwait(false);
            int id = BitConverter.ToInt32(buf, 0);
            int type = BitConverter.ToInt32(buf, 4);
            int bodyLen = size - 4 - 4 - 2; // minus id, type, 2 null terminators
            string body = bodyLen > 0 ? Encoding.UTF8.GetString(buf, 8, bodyLen) : "";
            return (id, type, body);
        }

        private async Task<byte[]> ReadExactAsync(int count)
        {
            byte[] buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = await _stream.ReadAsync(buf, read, count - read).ConfigureAwait(false);
                if (n <= 0) { throw new System.IO.IOException("RCON connection closed"); }
                read += n;
            }
            return buf;
        }

        public void Dispose()
        {
            try { _stream?.Dispose(); } catch { }
            try { _tcp?.Close(); } catch { }
        }
    }
}
