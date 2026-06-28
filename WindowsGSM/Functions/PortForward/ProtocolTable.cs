namespace WindowsGSM.Functions.PortForward
{
    /// <summary>
    /// Heuristiques de protocole par jeu. WGSM ne stocke pas le protocole des ports → on devine
    /// pour les jeux courants, et on retombe sur "Both" (TCP+UDP) quand on ne sait pas : c'est
    /// fonctionnellement sûr (au pire on ouvre un protocole inutile, jamais on n'en manque un).
    /// L'utilisateur peut corriger le protocole par port dans la config / l'UI.
    /// </summary>
    public static class ProtocolTable
    {
        /// <summary>Protocole du port de jeu principal selon le nom du jeu (FullName).</summary>
        public static PortProtocol GamePort(string gameFullName)
        {
            string g = (gameFullName ?? "").ToLowerInvariant();

            // Minecraft Java : jeu en TCP (query en UDP)
            if (g.Contains("minecraft") && !g.Contains("bedrock") && !g.Contains("pocket")) { return PortProtocol.Tcp; }
            // Minecraft Bedrock / Pocket : UDP
            if (g.Contains("bedrock") || g.Contains("pocket")) { return PortProtocol.Udp; }

            // Inconnu -> les deux (sûr)
            return PortProtocol.Both;
        }

        /// <summary>Le port de requête (query) est presque toujours en UDP.</summary>
        public static PortProtocol QueryPort(string gameFullName) => PortProtocol.Udp;

        /// <summary>RCON est en TCP.</summary>
        public static PortProtocol RconPort(string gameFullName) => PortProtocol.Tcp;
    }
}
