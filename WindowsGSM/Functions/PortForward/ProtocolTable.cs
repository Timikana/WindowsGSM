namespace WindowsGSM.Functions.PortForward
{
    /// <summary>
    /// Per-game protocol heuristics. WGSM does not store the protocol of ports -> we guess
    /// for common games, and fall back to "Both" (TCP+UDP) when we do not know: this is
    /// functionally safe (at worst we open a useless protocol, never miss one).
    /// The user can fix the protocol per port in the config / UI.
    /// </summary>
    public static class ProtocolTable
    {
        /// <summary>Protocol of the main game port based on the game name (FullName).</summary>
        public static PortProtocol GamePort(string gameFullName)
        {
            string g = (gameFullName ?? "").ToLowerInvariant();

            // Minecraft Java: game over TCP (query over UDP)
            if (g.Contains("minecraft") && !g.Contains("bedrock") && !g.Contains("pocket")) { return PortProtocol.Tcp; }
            // Minecraft Bedrock / Pocket: UDP
            if (g.Contains("bedrock") || g.Contains("pocket")) { return PortProtocol.Udp; }

            // Unknown -> both (safe)
            return PortProtocol.Both;
        }

        /// <summary>The query port is almost always UDP.</summary>
        public static PortProtocol QueryPort(string gameFullName) => PortProtocol.Udp;

        /// <summary>RCON is over TCP.</summary>
        public static PortProtocol RconPort(string gameFullName) => PortProtocol.Tcp;
    }
}
