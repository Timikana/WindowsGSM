using System.Threading.Tasks;

namespace WindowsGSM.Functions.PortForward
{
    /// <summary>
    /// Default backend as long as no real UPnP engine is plugged in. Does nothing but log,
    /// so the rest (config, resolution, lifecycle) is testable without a network dependency.
    /// Will be replaced by MonoNatBackend (library) or a homemade SSDP/SOAP backend.
    /// </summary>
    public class NullNatBackend : INatBackend
    {
        public Task<bool> IsAvailableAsync()
        {
            AppLog.Warn("PortForward", "No UPnP backend plugged in (NullNatBackend) — no port opened.");
            return Task.FromResult(false);
        }

        public Task<bool> MapAsync(int port, PortProtocol protocol, string description)
        {
            AppLog.Warn("PortForward", $"MapAsync ignored (backend not plugged in): {port}/{protocol} '{description}'.");
            return Task.FromResult(false);
        }

        public Task UnmapAsync(int port, PortProtocol protocol) => Task.CompletedTask;
    }
}
