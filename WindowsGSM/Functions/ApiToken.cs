using System.IO;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Per-server API token (e.g. Satisfactory: server.GenerateAPIToken), encrypted at rest via
    /// DPAPI (Secret). Stored in servers/&lt;id&gt;/configs/apitoken.txt. Never entered via chat.
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
