using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace WindowsGSM.Functions.Donator
{
    /// <summary>
    /// Statut « donateur » qui débloque les fonctions premium du fork (ex. Notifications multi-canaux).
    /// Deux voies :
    ///  (1) DONATEUR PATREON de l'auteur original — on GARDE le « Donor Connect » existant
    ///      (validation windowsgsm.com). MainWindow renseigne <see cref="AuthorDonorActive"/> après auth.
    ///  (2) PROPRIÉTAIRE (toi) — clé perso dérivée d'une PASSPHRASE + sel (PBKDF2-SHA256), définie en local
    ///      via l'outil `donator setowner` (la passphrase ne quitte jamais la machine ; seuls le hash + le sel
    ///      sont embarqués). Bonus : la machine de l'auteur est aussi reconnue par empreinte anonyme (auto).
    /// NB : verrou « honor-system » (app open-source MIT) — dissuasif, pas du DRM.
    /// </summary>
    public static class DonatorManager
    {
        // --- Propriétaire : empreinte machine anonyme (auto, aucune saisie) ---
        private const string OwnerHash = "d45a1aa40850aa7d0397ba86270d7e44f179c28159e9cce95288ed902f91108a";
        private const string OwnerSalt = "WGSM-Donator-v1";

        // --- Propriétaire : passphrase + sel (PBKDF2). Défini par `donator setowner` (passphrase locale). ---
        private const string OwnerPbkdf2SaltB64 = "v3qBMqLwNXdOT2SnI6d+tw==";
        private const string OwnerPbkdf2Hash = "a1f110d8c8dd469ff827e97c9ec360a1555c19218879de9d4a035cbb1cb78495";
        private const int OwnerPbkdf2Iter = 200000;

        private const string RegPath = @"SOFTWARE\WindowsGSM";
        private const string RegOwnerUnlocked = "OwnerUnlocked";

        private static bool? _ownerMachineCache;

        /// <summary>Positionné par MainWindow d'après le « Donor Connect » Patreon de l'auteur (g_DonorType).</summary>
        public static bool AuthorDonorActive { get; set; }

        /// <summary>Accès aux fonctions donateur : donateur Patreon de l'auteur OU propriétaire.</summary>
        public static bool IsDonator => IsOwner() || AuthorDonorActive;

        /// <summary>Propriétaire = machine de l'auteur (auto) OU passphrase propriétaire déjà déverrouillée.</summary>
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

        /// <summary>Déverrouille le mode propriétaire avec la passphrase perso (PBKDF2). Persiste si OK.</summary>
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

        /// <summary>Retire le déverrouillage propriétaire local.</summary>
        public static void LockOwner()
        {
            try { using var k = Registry.CurrentUser.CreateSubKey(RegPath); k.DeleteValue(RegOwnerUnlocked, false); } catch { }
        }
    }
}
