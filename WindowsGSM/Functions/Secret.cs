using System;
using System.Security.Cryptography;
using System.Text;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Chiffrement au repos de chaînes sensibles (tokens, mots de passe) via DPAPI, lié au compte
    /// Windows courant. Réutilisable par toute la base (notifications, etc.). Préfixe "enc:v1:" pour
    /// distinguer une valeur chiffrée d'une valeur en clair (rétro-compat / migration douce).
    /// </summary>
    public static class Secret
    {
        private const string EncPrefix = "enc:v1:";
        private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("WindowsGSM.Secret");

        /// <summary>Chiffre une valeur (renvoie "enc:v1:..."). Vide -> vide.</summary>
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) { return string.Empty; }
            byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), _entropy, DataProtectionScope.CurrentUser);
            return EncPrefix + Convert.ToBase64String(enc);
        }

        /// <summary>Déchiffre une valeur. Si elle n'est pas préfixée -> considérée en clair et renvoyée telle quelle.</summary>
        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) { return string.Empty; }
            if (!stored.StartsWith(EncPrefix)) { return stored; } // ancienne valeur en clair
            try
            {
                byte[] enc = Convert.FromBase64String(stored.Substring(EncPrefix.Length));
                byte[] data = ProtectedData.Unprotect(enc, _entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return string.Empty; // chiffré par un autre compte Windows / corrompu
            }
        }

        public static bool IsProtected(string value) => !string.IsNullOrEmpty(value) && value.StartsWith(EncPrefix);
    }
}
