using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Client A2S minimal (protocole Steam "Source Engine Query") pour récupérer
    /// le nombre de joueurs en ligne d'un serveur. UDP, gère le challenge 0x41.
    /// Tout est best-effort : en cas d'échec on renvoie null, jamais d'exception.
    /// </summary>
    internal static class SteamQuery
    {
        private static readonly byte[] A2S_INFO_HEADER =
            { 0xFF, 0xFF, 0xFF, 0xFF, 0x54 };
        private static readonly byte[] A2S_INFO_PAYLOAD =
            Encoding.ASCII.GetBytes("Source Engine Query\0");

        public struct Info
        {
            public int Players;
            public int MaxPlayers;
        }

        /// <summary>
        /// Interroge un serveur en A2S_INFO. Renvoie (joueurs, max) ou null si injoignable.
        /// </summary>
        public static async Task<Info?> GetInfoAsync(string host, int port, int timeoutMs = 1200)
        {
            if (string.IsNullOrWhiteSpace(host) || host == "0.0.0.0") { host = "127.0.0.1"; }
            if (port <= 0 || port > 65535) { return null; }

            using (var udp = new UdpClient())
            {
                try
                {
                    udp.Client.ReceiveTimeout = timeoutMs;
                    udp.Client.SendTimeout = timeoutMs;
                    udp.Connect(host, port);

                    byte[] response = await QueryAsync(udp, BuildRequest(null), timeoutMs).ConfigureAwait(false);
                    if (response == null) { return null; }

                    // Réponse de challenge : 0x41 suivi d'un challenge 4 octets -> on rejoue.
                    if (response.Length >= 9 && response[4] == 0x41)
                    {
                        byte[] challenge = new byte[4];
                        Array.Copy(response, 5, challenge, 0, 4);
                        response = await QueryAsync(udp, BuildRequest(challenge), timeoutMs).ConfigureAwait(false);
                        if (response == null) { return null; }
                    }

                    return Parse(response);
                }
                catch
                {
                    return null;
                }
            }
        }

        private static byte[] BuildRequest(byte[] challenge)
        {
            int len = A2S_INFO_HEADER.Length + A2S_INFO_PAYLOAD.Length + (challenge?.Length ?? 0);
            byte[] req = new byte[len];
            int o = 0;
            Buffer.BlockCopy(A2S_INFO_HEADER, 0, req, o, A2S_INFO_HEADER.Length); o += A2S_INFO_HEADER.Length;
            Buffer.BlockCopy(A2S_INFO_PAYLOAD, 0, req, o, A2S_INFO_PAYLOAD.Length); o += A2S_INFO_PAYLOAD.Length;
            if (challenge != null) { Buffer.BlockCopy(challenge, 0, req, o, challenge.Length); }
            return req;
        }

        private static async Task<byte[]> QueryAsync(UdpClient udp, byte[] request, int timeoutMs)
        {
            await udp.SendAsync(request, request.Length).ConfigureAwait(false);

            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                Task<UdpReceiveResult> recv = udp.ReceiveAsync();
                Task done = await Task.WhenAny(recv, Task.Delay(timeoutMs, cts.Token)).ConfigureAwait(false);
                if (done != recv)
                {
                    // Timeout : on observe l'exception du ReceiveAsync abandonné (le socket sera fermé)
                    // pour éviter une "unobserved task exception".
                    _ = recv.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
                    return null;
                }
                cts.Cancel();
                return recv.Result.Buffer;
            }
        }

        private static Info? Parse(byte[] data)
        {
            // En-tête 0xFFFFFFFF puis 0x49 (A2S_INFO réponse Source).
            if (data == null || data.Length < 6 || data[4] != 0x49) { return null; }

            int i = 5;
            try
            {
                i++;                       // protocol (byte)
                SkipString(data, ref i);   // name
                SkipString(data, ref i);   // map
                SkipString(data, ref i);   // folder
                SkipString(data, ref i);   // game
                i += 2;                    // app id (short)

                if (i + 1 >= data.Length) { return null; }
                int players = data[i];
                int max = data[i + 1];
                return new Info { Players = players, MaxPlayers = max };
            }
            catch
            {
                return null;
            }
        }

        private static void SkipString(byte[] data, ref int i)
        {
            while (i < data.Length && data[i] != 0x00) { i++; }
            i++; // saute le 0 terminal
        }
    }
}
