using System.Collections.Generic;

namespace WindowsGSM.Functions.PortForward
{
    /// <summary>Port specifics of a game, not derivable from WGSM's Port/QueryPort fields.</summary>
    public class PortQuirk
    {
        public PortProtocol? GamePortProtocol;            // forces the protocol of the game port
        public bool IncludeQueryPort = true;              // false if the game no longer uses a query port
        public List<PortMapping> ExtraPorts = new List<PortMapping>(); // additional fixed ports (e.g. reliable)
    }

    /// <summary>
    /// Table of special cases per game. WGSM only models Port + QueryPort; some games
    /// require other ports (or drop the query). We encode them here so the suggestion
    /// is correct. The user keeps control (on/off checkboxes + manual add).
    /// </summary>
    public static class GamePortQuirks
    {
        public static PortQuirk For(string gameFullName)
        {
            string g = (gameFullName ?? "").ToLowerInvariant();

            // Satisfactory — since Update 1.1: port 7777 (TCP+UDP) + 8888/TCP "Reliable Messaging"
            // MANDATORY; the old 15000 (beacon) and 15777 (query) are NO LONGER used.
            if (g.Contains("satisfactory"))
            {
                return new PortQuirk
                {
                    GamePortProtocol = PortProtocol.Both,
                    IncludeQueryPort = false, // 15777 dropped in 1.1 -> we do not suggest it
                    ExtraPorts = new List<PortMapping>
                    {
                        new PortMapping { Port = 8888, Protocol = PortProtocol.Tcp, Label = "Reliable (1.1)", Enabled = true },
                    }
                };
            }

            return null; // no specifics -> standard behavior
        }
    }
}
