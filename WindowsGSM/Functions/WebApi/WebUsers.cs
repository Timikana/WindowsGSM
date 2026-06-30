using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace WindowsGSM.Functions.WebApi
{
    /// <summary>Rôle web : Viewer (lecture), Operator (lecture + contrôle/backup), Admin (tout).</summary>
    public enum WebRole { Viewer = 0, Operator = 1, Admin = 2 }

    /// <summary>Compte web : login, mot de passe hashé (PBKDF2), rôle, et serveurs autorisés ("*" = tous).</summary>
    public class WebUser
    {
        public string Username = string.Empty;
        public string PasswordHash = string.Empty; // "saltB64:hashHex"
        public WebRole Role = WebRole.Viewer;
        public string ServerIds = "*"; // "*" = tous, sinon liste séparée par virgules (ex. "1,3,4")

        [JsonIgnore]
        public bool CanControl => Role >= WebRole.Operator;
        [JsonIgnore]
        public bool IsAdmin => Role == WebRole.Admin;

        public bool AllowsServer(string id)
        {
            if (string.IsNullOrWhiteSpace(ServerIds) || ServerIds.Trim() == "*") { return true; }
            return ServerIds.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Contains(id);
        }
    }

    /// <summary>Store des comptes web (configs/webusers.json). Mots de passe PBKDF2-SHA256 (200k itérations).</summary>
    public class WebUsers
    {
        private const int Iter = 200000;
        public List<WebUser> Users = new List<WebUser>();

        private static string Path => Functions.ServerPath.Get("configs", "webusers.json");

        public static WebUsers Load()
        {
            try { if (File.Exists(Path)) { return JsonConvert.DeserializeObject<WebUsers>(File.ReadAllText(Path)) ?? new WebUsers(); } }
            catch (Exception ex) { AppLog.Warn("WebUsers/Load", ex.Message); }
            return new WebUsers();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.WriteAllText(Path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex) { AppLog.Warn("WebUsers/Save", ex.Message); }
        }

        public static string HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password ?? string.Empty), salt, Iter, HashAlgorithmName.SHA256, 32);
            return Convert.ToBase64String(salt) + ":" + Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>Renvoie l'utilisateur si le couple login/mot de passe est valide, sinon null.</summary>
        // Sel/hash factices : calcul PBKDF2 « à vide » quand le compte n'existe pas, pour égaliser le temps
        // de réponse et empêcher l'énumération des logins par mesure de timing (OWASP A07).
        private static readonly byte[] DummySalt = new byte[16];

        public WebUser Verify(string username, string password)
        {
            var u = Users.FirstOrDefault(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));
            if (u == null || string.IsNullOrEmpty(u.PasswordHash) || !u.PasswordHash.Contains(':'))
            {
                // dérivation factice (même coût CPU) puis échec, pour ne pas trahir l'existence du compte
                try { Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password ?? string.Empty), DummySalt, Iter, HashAlgorithmName.SHA256, 32); } catch { }
                return null;
            }
            try
            {
                string[] parts = u.PasswordHash.Split(':');
                byte[] salt = Convert.FromBase64String(parts[0]);
                byte[] expected = Convert.FromHexString(parts[1]);
                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password ?? string.Empty), salt, Iter, HashAlgorithmName.SHA256, 32);
                return CryptographicOperations.FixedTimeEquals(actual, expected) ? u : null;
            }
            catch { return null; }
        }

        public void Set(string username, string password, WebRole role, string serverIds)
        {
            var u = Users.FirstOrDefault(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));
            if (u == null) { u = new WebUser { Username = username }; Users.Add(u); }
            u.Role = role;
            u.ServerIds = string.IsNullOrWhiteSpace(serverIds) ? "*" : serverIds.Trim();
            if (!string.IsNullOrEmpty(password)) { u.PasswordHash = HashPassword(password); }
        }

        public void Remove(string username) => Users.RemoveAll(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));
    }
}
