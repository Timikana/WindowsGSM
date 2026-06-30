using System;
using System.Collections.Generic;

namespace WindowsGSM.Functions.Mods
{
    /// <summary>Mécanisme de mods d'un jeu : aucun, dossier (fichiers/sous-dossiers), ou Steam Workshop.</summary>
    public enum ModMechanism { None, Folder, Workshop }

    /// <summary>
    /// Profil de mods d'un jeu : où vivent les mods et comment on les gère. Le manager unifié choisit
    /// l'UI selon Mechanism. Hybride : un profil curaté par jeu connu, sinon null → fallback générique.
    /// </summary>
    public class ModProfile
    {
        public string GameMatch;            // StartsWith sur GameServer.FullName
        public ModMechanism Mechanism = ModMechanism.Folder;
        public string ModFolderRelative;    // dossier des mods sous serverfiles (ex. "mods", "Mods")
        public string[] Extensions;         // extensions de fichiers-mods (ex. .jar, .dll) ; vide si dossiers
        public bool FolderEntries;          // true = chaque sous-dossier est un mod (7DtD, SML)
        public int WorkshopAppId;           // AppID Steam Workshop (download via SteamCMD)
        public string ConfigKey;            // clé de config où lister les mods (ex. ARK "ActiveMods", PZ "WorkshopItems")
        public string ConfigFileRelative;   // fichier de config à câbler (relatif serverfiles)
        public string ConfigSection;        // section INI où écrire la clé (ex. ARK "ServerSettings")
        public string ListSeparator = ",";  // séparateur de la liste d'IDs (ARK ",", PZ ";")
        public bool ServerAutoDownloads;    // true = le serveur télécharge lui-même (PZ, GMod) → pas besoin de SteamCMD
        public string Notes;                // info affichée à l'utilisateur

        public ModProfile() { }
    }

    /// <summary>Registre des profils de mods par jeu (couvre bien au-delà d'ARK).</summary>
    public static class ModProfiles
    {
        private static readonly List<ModProfile> _all = new List<ModProfile>
        {
            // ====== Profils vérifiés (recherche web 2025-2026) ======
            // --- Mods-fichiers avec loader ---
            new ModProfile { GameMatch = "Minecraft", Mechanism = ModMechanism.Folder, ModFolderRelative = "mods", Extensions = new[] { ".jar" }, Notes = "Dépose des .jar dans mods/. Loader OBLIGATOIRE (Forge OU Fabric, selon le serveur) installé avant. Fabric exige aussi 'Fabric API' (.jar). Version MC + loader + chaque mod doivent matcher EXACTEMENT. Exclure les mods client-only (OptiFine…)." },
            new ModProfile { GameMatch = "Valheim", Mechanism = ModMechanism.Folder, ModFolderRelative = @"BepInEx\plugins", Extensions = new[] { ".dll" }, Notes = "Nécessite BepInEx (BepInExPack Valheim) à la racine. ⚠️ Si le CROSSPLAY est activé, BepInEx ne se charge pas (conflit winhttp.dll) → désactiver le crossplay. Versions serveur/client identiques." },
            new ModProfile { GameMatch = "V Rising", Mechanism = ModMechanism.Folder, ModFolderRelative = @"BepInEx\plugins", Extensions = new[] { ".dll" }, Notes = "Nécessite BepInEx (build SERVEUR, distinct du client). Mods .dll dans BepInEx\\plugins. Logs : BepInEx\\logs. Gérer les dépendances manuellement." },
            new ModProfile { GameMatch = "Rust", Mechanism = ModMechanism.Folder, ModFolderRelative = @"oxide\plugins", Extensions = new[] { ".cs" }, Notes = "Plugins Oxide/uMod (ou Carbon) en .cs, compilés à chaud. Nécessite Oxide/Carbon installé (merge dans RustDedicated_Data). À RÉINSTALLER après chaque update forcé de Rust (wipe mensuel)." },
            // --- Mods-dossiers (chaque sous-dossier = 1 mod) ---
            new ModProfile { GameMatch = "7 Days to Die", Mechanism = ModMechanism.Folder, ModFolderRelative = "Mods", FolderEntries = true, Notes = "Chaque mod = un sous-dossier avec ModInfo.xml. Auto-détecté au démarrage. Pas de loader. ⚠️ Vérifier la compatibilité de version (A21 ≠ 1.0)." },
            new ModProfile { GameMatch = "Satisfactory", Mechanism = ModMechanism.Folder, ModFolderRelative = @"FactoryGame\Mods", FolderEntries = true, Notes = "Nécessite SML (Satisfactory Mod Loader), version LIÉE à la version du jeu (sinon le serveur ne démarre pas). Mods depuis ficsit.app : vérifier le flag 'Server' (beaucoup sont client-only)." },
            new ModProfile { GameMatch = "Conan Exiles", Mechanism = ModMechanism.Folder, ModFolderRelative = @"ConanSandbox\Mods", Extensions = new[] { ".pak" }, Notes = "Fichiers .pak dans ConanSandbox\\Mods + lister leurs chemins dans modlist.txt (un par ligne, l'ORDRE compte). Récupérés du Workshop (app 440900) par abonnement/SteamCMD — PAS d'auto-download serveur. Sync stricte client/serveur (mods + ordre)." },
            new ModProfile { GameMatch = "Left 4 Dead 2", Mechanism = ModMechanism.Folder, ModFolderRelative = @"left4dead2\addons", Extensions = new[] { ".vpk" }, Notes = "Fichiers .vpk dans left4dead2\\addons. PAS de Workshop serveur natif (≠ GMod) → copie manuelle. (Plugins SourceMod = via addons\\sourcemod\\plugins, requiert MetaMod+SourceMod.)" },
            // --- Steam Workshop ---
            new ModProfile { GameMatch = "ARK", Mechanism = ModMechanism.Workshop, WorkshopAppId = 346110, ConfigKey = "ActiveMods", ConfigFileRelative = @"ShooterGame\Saved\Config\WindowsServer\GameUserSettings.ini", ConfigSection = "ServerSettings", ListSeparator = ",", Notes = "Workshop ARK Survival Evolved (app 346110). Télécharge via SteamCMD (ou auto via le flag de lancement -automanagedmods) puis écrit ActiveMods= dans GameUserSettings.ini. ⚠️ Mods = .z à extraire vers Content/Mods (extraction non incluse). NB : ARK Survival ASCENDED utilise CurseForge, PAS le Workshop." },
            new ModProfile { GameMatch = "Project Zomboid", Mechanism = ModMechanism.Workshop, WorkshopAppId = 108600, ConfigKey = "WorkshopItems", ListSeparator = ";", ServerAutoDownloads = true, Notes = "Workshop (app 108600). Le serveur PZ télécharge lui-même les WorkshopItems au démarrage. Renseigne AUSSI Mods= (les mod_id internes, ≠ ID Workshop, lus dans mod.info ; l'ordre = ordre de chargement) dans servertest.ini (dossier Zomboid utilisateur). Séparateur = ';'." },
            new ModProfile { GameMatch = "Garry's Mod", Mechanism = ModMechanism.Workshop, WorkshopAppId = 4000, ServerAutoDownloads = true, Notes = "Workshop GMod (app 4000) : crée une collection Workshop (publique/unlisted) et passe son ID via +host_workshop_collection (le serveur télécharge+monte au démarrage). Forcer les clients : resource.AddWorkshop(\"ID\") dans garrysmod\\lua\\autorun\\server\\workshop.lua." },
            new ModProfile { GameMatch = "Palworld", Mechanism = ModMechanism.Workshop, WorkshopAppId = 1623730, Notes = "Workshop Palworld (app 1623730, depuis « Home Sweet Home »). Télécharge via SteamCMD → les items vont dans Mods\\Workshop. Pour activer : dans Mods\\PalModSettings.ini → bGlobalEnableMod=true + une ligne ActiveModList=<PackageName> par mod (le PackageName vient du Info.json du mod, PAS l'ID ni le dossier). Redémarrage requis. (Écriture auto de la config non fournie : ActiveModList exige le PackageName, pas l'ID.)" },
        };

        public static ModProfile For(string gameFullName)
        {
            if (string.IsNullOrEmpty(gameFullName)) { return null; }
            foreach (var p in _all)
            {
                if (gameFullName.StartsWith(p.GameMatch, StringComparison.OrdinalIgnoreCase)) { return p; }
            }
            return null; // inconnu → fallback générique dans l'UI
        }
    }
}
