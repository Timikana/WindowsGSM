using System.Collections.Generic;
using System.Linq;

namespace WindowsGSM.Functions.PortForward
{
    /// <summary>
    /// Construit la liste de ports SUGGÉRÉS pour un serveur. Base = ce que WGSM connaît de fiable
    /// (port de jeu + port de requête) ; affinée par <see cref="GamePortQuirks"/> pour les jeux à
    /// particularités (ex. Satisfactory 1.1 : 7777 TCP+UDP + 8888/TCP, query 15777 abandonné).
    /// Port de jeu et extras suggérés activés ; RCON/ports exotiques restent à ajouter manuellement.
    /// </summary>
    public static class PortResolver
    {
        public static List<PortMapping> Suggest(string gameFullName, string serverPort, string serverQueryPort)
        {
            var list = new List<PortMapping>();
            var quirk = GamePortQuirks.For(gameFullName);

            int gp = -1;
            if (int.TryParse(serverPort, out gp) && gp > 0)
            {
                list.Add(new PortMapping
                {
                    Port = gp,
                    Protocol = quirk?.GamePortProtocol ?? ProtocolTable.GamePort(gameFullName),
                    Label = "Jeu",
                    Enabled = true
                });
            }

            bool includeQuery = quirk?.IncludeQueryPort ?? true;
            if (includeQuery && int.TryParse(serverQueryPort, out int qp) && qp > 0 && qp != gp)
            {
                list.Add(new PortMapping
                {
                    Port = qp,
                    Protocol = ProtocolTable.QueryPort(gameFullName),
                    Label = "Query",
                    Enabled = true
                });
            }

            if (quirk != null)
            {
                foreach (var ex in quirk.ExtraPorts)
                {
                    if (!list.Any(p => p.Port == ex.Port && p.Protocol == ex.Protocol))
                    {
                        list.Add(new PortMapping { Port = ex.Port, Protocol = ex.Protocol, Label = ex.Label, Enabled = ex.Enabled });
                    }
                }
            }

            return list;
        }
    }
}
