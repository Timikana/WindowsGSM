using System;
using System.Security.Cryptography;
using System.Text;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// Encryption at rest of sensitive strings (tokens, passwords) via DPAPI, bound to the current
    /// Windows account. Reusable across the whole codebase (notifications, etc.). Prefix "enc:v1:" to
    /// distinguish an encrypted value from a plaintext value (backward-compat / soft migration).
    /// </summary>
    public static class Secret
    {
        private const string EncPrefix = "enc:v1:";
        private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("WindowsGSM.Secret");

        /// <summary>Encrypts a value (returns "enc:v1:..."). Empty -> empty.</summary>
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) { return string.Empty; }
            byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), _entropy, DataProtectionScope.CurrentUser);
            return EncPrefix + Convert.ToBase64String(enc);
        }

        /// <summary>Decrypts a value. If it is not prefixed -> considered plaintext and returned as-is.</summary>
        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) { return string.Empty; }
            if (!stored.StartsWith(EncPrefix)) { return stored; } // old plaintext value
            try
            {
                byte[] enc = Convert.FromBase64String(stored.Substring(EncPrefix.Length));
                byte[] data = ProtectedData.Unprotect(enc, _entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return string.Empty; // encrypted by another Windows account / corrupted
            }
        }

        public static bool IsProtected(string value) => !string.IsNullOrEmpty(value) && value.StartsWith(EncPrefix);
    }
}
