using System.Threading.Tasks;

namespace WindowsGSM.Functions.PortForward
{
    /// <summary>
    /// Abstraction of the port-opening mechanism on the router (UPnP IGD or NAT-PMP). Lets you
    /// plug in either a library (Mono.Nat) or a homemade implementation, without touching the rest.
    /// </summary>
    public interface INatBackend
    {
        /// <summary>true if a compatible gateway was found on the network.</summary>
        Task<bool> IsAvailableAsync();

        Task<bool> MapAsync(int port, PortProtocol protocol, string description);

        Task UnmapAsync(int port, PortProtocol protocol);
    }
}
