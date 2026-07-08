using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace WindowsGSM.Functions.Donator
{
    /// <summary>
    /// "Donor" status that unlocks the fork's premium features (e.g. multi-channel Notifications).
    /// Two paths:
    ///  (1) Original author's PATREON DONOR — we KEEP the existing "Donor Connect"
    ///      (windowsgsm.com validation). MainWindow sets <see cref="AuthorDonorActive"/> after auth.
    ///  (2) OWNER (you) — personal key derived from a PASSPHRASE + salt (PBKDF2-SHA256), set locally
    ///      via the `donator setowner` tool (the passphrase never leaves the machine; only the hash + salt
    ///      are embedded). Bonus: the author's machine is also recognized by anonymous fingerprint (auto).
    /// NB: "honor-system" lock (open-source MIT app) — a deterrent, not DRM.
    /// </summary>
    public static class DonatorManager
    {
        // --- Owner: anonymous machine fingerprint (auto, no input) ---
        private const string OwnerHash = "d45a1aa40850aa7d0397ba86270d7e44f179c28159e9cce95288ed902f91108a";
        private const string OwnerSalt = "WGSM-Donator-v1";

        // --- Owner: passphrase + salt (PBKDF2). Set by `donator setowner` (local passphrase). ---
        private const string OwnerPbkdf2SaltB64 = "v3qBMqLwNXdOT2SnI6d+tw==";
        private const string OwnerPbkdf2Hash = "a1f110d8c8dd469ff827e97c9ec360a1555c19218879de9d4a035cbb1cb78495";
        private const int OwnerPbkdf2Iter = 200000;

        private const string RegPath = @"SOFTWARE\WindowsGSM";
        private const string RegOwnerUnlocked = "OwnerUnlocked";

        private static bool? _ownerMachineCache;

        /// <summary>Set by MainWindow based on the author's Patreon "Donor Connect" (g_DonorType).</summary>
        public static bool AuthorDonorActive { get; set; }

        /// <summary>Access to donor features: author's Patreon donor OR owner.</summary>
        public static bool IsDonator => IsOwner() || AuthorDonorActive;

        /// <summary>Owner = author's machine (auto) OR owner passphrase already unlocked.</summary>
        public static bool IsOwner() => IsOwnerMachine() || IsOwnerUnlockedStored();

        private static bool IsOwnerMachine()
        {
            if (_ownerMachineCache.HasValue) { return _ownerMachineCache.Value; }
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                string guid = k?.GetValue("MachineGuid")?.ToString() ?? "";
                using var sha = SHA256.Create();
                string h = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(OwnerSalt + "|" + guid))).ToLowerInvariant();
                _ownerMachineCache = string.Equals(h, OwnerHash, StringComparison.Ordinal);
            }
            catch { _ownerMachineCache = false; }
            return _ownerMachineCache.Value;
        }

        private static bool IsOwnerUnlockedStored()
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(RegPath); return (k?.GetValue(RegOwnerUnlocked)?.ToString() == "1"); }
            catch { return false; }
        }

        /// <summary>Unlocks owner mode with the personal passphrase (PBKDF2). Persists if OK.</summary>
        public static bool UnlockOwner(string passphrase)
        {
            if (string.IsNullOrEmpty(OwnerPbkdf2Hash) || string.IsNullOrEmpty(OwnerPbkdf2SaltB64) || string.IsNullOrEmpty(passphrase)) { return false; }
            try
            {
                byte[] salt = Convert.FromBase64String(OwnerPbkdf2SaltB64);
                byte[] derived = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, OwnerPbkdf2Iter, HashAlgorithmName.SHA256, 32);
                string h = Convert.ToHexString(derived).ToLowerInvariant();
                if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(h), Encoding.ASCII.GetBytes(OwnerPbkdf2Hash))) { return false; }
                using var k = Registry.CurrentUser.CreateSubKey(RegPath);
                k.SetValue(RegOwnerUnlocked, "1");
                return true;
            }
            catch { return false; }
        }

        /// <summary>Removes the local owner unlock.</summary>
        public static void LockOwner()
        {
            try { using var k = Registry.CurrentUser.CreateSubKey(RegPath); k.DeleteValue(RegOwnerUnlocked, false); } catch { }
        }
    }
}
