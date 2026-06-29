using System.IO;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Jeton d'API par serveur (ex. Satisfactory : server.GenerateAPIToken), chiffré au repos via
    /// DPAPI (Secret). Stocké dans servers/&lt;id&gt;/configs/apitoken.txt. Jamais saisi via le chat.
    /// </summary>
    public static class ApiToken
    {
        private static string PathFor(string serverId) => ServerPath.GetServersConfigs(serverId, "apitoken.txt");

        public static string Get(string serverId)
        {
            try { return Secret.Unprotect(File.ReadAllText(PathFor(serverId)).Trim()); }
            catch { return string.Empty; }
        }

        public static void Set(string serverId, string token)
        {
            string path = PathFor(serverId);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Secret.Protect((token ?? string.Empty).Trim()));
        }
    }
}
