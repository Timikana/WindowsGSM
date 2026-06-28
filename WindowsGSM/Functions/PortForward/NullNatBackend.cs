using System.Threading.Tasks;

namespace WindowsGSM.Functions.PortForward
{
    /// <summary>
    /// Backend par défaut tant qu'aucun moteur UPnP réel n'est branché. Ne fait rien sauf journaliser,
    /// pour que le reste (config, résolution, cycle de vie) soit testable sans dépendance réseau.
    /// Sera remplacé par MonoNatBackend (lib) ou un backend SSDP/SOAP maison.
    /// </summary>
    public class NullNatBackend : INatBackend
    {
        public Task<bool> IsAvailableAsync()
        {
            AppLog.Warn("PortForward", "Aucun backend UPnP branché (NullNatBackend) — aucun port ouvert.");
            return Task.FromResult(false);
        }

        public Task<bool> MapAsync(int port, PortProtocol protocol, string description)
        {
            AppLog.Warn("PortForward", $"MapAsync ignoré (backend non branché) : {port}/{protocol} '{description}'.");
            return Task.FromResult(false);
        }

        public Task UnmapAsync(int port, PortProtocol protocol) => Task.CompletedTask;
    }
}
