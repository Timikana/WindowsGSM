namespace WindowsGSM.Functions.PortForward
{
    /// <summary>Protocole d'un mapping de port. "Both" ouvre TCP et UDP (défaut sûr si inconnu).</summary>
    public enum PortProtocol { Tcp, Udp, Both }

    /// <summary>
    /// Un port à (éventuellement) ouvrir via UPnP pour un serveur : numéro, protocole, libellé
    /// lisible, et activé ou non. Sérialisé tel quel dans portforward.json.
    /// </summary>
    public class PortMapping
    {
        public int Port { get; set; }
        public PortProtocol Protocol { get; set; } = PortProtocol.Both;
        public string Label { get; set; } = "";
        public bool Enabled { get; set; } = false;

        public string Key => $"{Port}/{Protocol}";
    }
}
