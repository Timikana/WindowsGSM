using System.Threading.Tasks;

namespace WindowsGSM.Functions.PortForward
{
    /// <summary>
    /// Abstraction du mécanisme d'ouverture de port sur la box (UPnP IGD ou NAT-PMP). Permet de
    /// brancher soit une lib (Mono.Nat), soit une implémentation maison, sans toucher au reste.
    /// </summary>
    public interface INatBackend
    {
        /// <summary>true si une passerelle compatible a été trouvée sur le réseau.</summary>
        Task<bool> IsAvailableAsync();

        Task<bool> MapAsync(int port, PortProtocol protocol, string description);

        Task UnmapAsync(int port, PortProtocol protocol);
    }
}
