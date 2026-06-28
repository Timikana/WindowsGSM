using System.Collections.Generic;

namespace WindowsGSM.Functions.PortForward
{
    /// <summary>Particularités de ports d'un jeu, non déductibles des champs Port/QueryPort de WGSM.</summary>
    public class PortQuirk
    {
        public PortProtocol? GamePortProtocol;            // force le protocole du port de jeu
        public bool IncludeQueryPort = true;              // false si le jeu n'utilise plus de query port
        public List<PortMapping> ExtraPorts = new List<PortMapping>(); // ports fixes additionnels (ex. reliable)
    }

    /// <summary>
    /// Table des cas particuliers par jeu. WGSM ne modélise que Port + QueryPort ; certains jeux
    /// exigent d'autres ports (ou abandonnent le query). On les encode ici pour que la suggestion
    /// soit juste. L'utilisateur garde la main (cases on/off + ajout manuel).
    /// </summary>
    public static class GamePortQuirks
    {
        public static PortQuirk For(string gameFullName)
        {
            string g = (gameFullName ?? "").ToLowerInvariant();

            // Satisfactory — depuis Update 1.1 : port 7777 (TCP+UDP) + 8888/TCP "Reliable Messaging"
            // OBLIGATOIRE ; les anciens 15000 (beacon) et 15777 (query) ne sont PLUS utilisés.
            if (g.Contains("satisfactory"))
            {
                return new PortQuirk
                {
                    GamePortProtocol = PortProtocol.Both,
                    IncludeQueryPort = false, // 15777 abandonné en 1.1 -> on ne le suggère pas
                    ExtraPorts = new List<PortMapping>
                    {
                        new PortMapping { Port = 8888, Protocol = PortProtocol.Tcp, Label = "Reliable (1.1)", Enabled = true },
                    }
                };
            }

            return null; // pas de particularité -> comportement standard
        }
    }
}
