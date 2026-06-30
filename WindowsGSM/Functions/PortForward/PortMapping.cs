namespace WindowsGSM.Functions.PortForward
{
    /// <summary>Protocol of a port mapping. "Both" opens TCP and UDP (safe default when unknown).</summary>
    public enum PortProtocol { Tcp, Udp, Both }

    /// <summary>
    /// A port to (possibly) open via UPnP for a server: number, protocol, readable
    /// label, and enabled or not. Serialized as-is in portforward.json.
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
