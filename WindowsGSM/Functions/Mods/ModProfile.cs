using System;
using System.Collections.Generic;

namespace WindowsGSM.Functions.Mods
{
    /// <summary>Mod mechanism of a game: none, folder (files/subfolders), or Steam Workshop.</summary>
    public enum ModMechanism { None, Folder, Workshop }

    /// <summary>
    /// Mod profile of a game: where mods live and how we manage them. The unified manager picks
    /// the UI based on Mechanism. Hybrid: a curated profile per known game, otherwise null -> generic fallback.
    /// </summary>
    public class ModProfile
    {
        public string GameMatch;            // StartsWith on GameServer.FullName
        public ModMechanism Mechanism = ModMechanism.Folder;
        public string ModFolderRelative;    // mods folder under serverfiles (e.g. "mods", "Mods")
        public string[] Extensions;         // mod file extensions (e.g. .jar, .dll); empty if folders
        public bool FolderEntries;          // true = each subfolder is a mod (7DtD, SML)
        public int WorkshopAppId;           // Steam Workshop AppID (download via SteamCMD)
        public string ConfigKey;            // config key where mods are listed (e.g. ARK "ActiveMods", PZ "WorkshopItems")
        public string ConfigFileRelative;   // config file to wire (relative to serverfiles)
        public string ConfigSection;        // INI section where the key is written (e.g. ARK "ServerSettings")
        public string ListSeparator = ",";  // separator of the ID list (ARK ",", PZ ";")
        public bool ServerAutoDownloads;    // true = the server downloads by itself (PZ, GMod) -> no need for SteamCMD
        public string Notes;                // info shown to the user

        public ModProfile() { }
    }

    /// <summary>Registry of mod profiles per game (covers well beyond ARK).</summary>
    public static class ModProfiles
    {
        private static readonly List<ModProfile> _all = new List<ModProfile>
        {
            // ====== Verified profiles (web research 2025-2026) ======
            // --- File mods with loader ---
            new ModProfile { GameMatch = "Minecraft", Mechanism = ModMechanism.Folder, ModFolderRelative = "mods", Extensions = new[] { ".jar" }, Notes = "Drop .jar files into mods/. Loader MANDATORY (Forge OR Fabric, depending on the server) installed beforehand. Fabric also requires 'Fabric API' (.jar). MC version + loader + each mod must match EXACTLY. Exclude client-only mods (OptiFine...)." },
            new ModProfile { GameMatch = "Valheim", Mechanism = ModMechanism.Folder, ModFolderRelative = @"BepInEx\plugins", Extensions = new[] { ".dll" }, Notes = "Requires BepInEx (BepInExPack Valheim) at the root. ⚠️ If CROSSPLAY is enabled, BepInEx does not load (winhttp.dll conflict) -> disable crossplay. Identical server/client versions." },
            new ModProfile { GameMatch = "V Rising", Mechanism = ModMechanism.Folder, ModFolderRelative = @"BepInEx\plugins", Extensions = new[] { ".dll" }, Notes = "Requires BepInEx (SERVER build, distinct from the client). .dll mods in BepInEx\\plugins. Logs: BepInEx\\logs. Manage dependencies manually." },
            new ModProfile { GameMatch = "Rust", Mechanism = ModMechanism.Folder, ModFolderRelative = @"oxide\plugins", Extensions = new[] { ".cs" }, Notes = "Oxide/uMod (or Carbon) plugins in .cs, compiled on the fly. Requires Oxide/Carbon installed (merged into RustDedicated_Data). MUST BE REINSTALLED after each forced Rust update (monthly wipe)." },
            // --- Folder mods (each subfolder = 1 mod) ---
            new ModProfile { GameMatch = "7 Days to Die", Mechanism = ModMechanism.Folder, ModFolderRelative = "Mods", FolderEntries = true, Notes = "Each mod = a subfolder with ModInfo.xml. Auto-detected at startup. No loader. ⚠️ Check version compatibility (A21 != 1.0)." },
            new ModProfile { GameMatch = "Satisfactory", Mechanism = ModMechanism.Folder, ModFolderRelative = @"FactoryGame\Mods", FolderEntries = true, Notes = "Requires SML (Satisfactory Mod Loader), version TIED to the game version (otherwise the server does not start). Mods from ficsit.app: check the 'Server' flag (many are client-only)." },
            new ModProfile { GameMatch = "Conan Exiles", Mechanism = ModMechanism.Folder, ModFolderRelative = @"ConanSandbox\Mods", Extensions = new[] { ".pak" }, Notes = "Files .pak in ConanSandbox\\Mods + list their paths in modlist.txt (one per line, ORDER matters). Fetched from the Workshop (app 440900) by subscription/SteamCMD — NO server auto-download. Strict client/server sync (mods + order)." },
            new ModProfile { GameMatch = "Left 4 Dead 2", Mechanism = ModMechanism.Folder, ModFolderRelative = @"left4dead2\addons", Extensions = new[] { ".vpk" }, Notes = "Files .vpk in left4dead2\\addons. NO native server Workshop (unlike GMod) -> manual copy. (SourceMod plugins = via addons\\sourcemod\\plugins, requires MetaMod+SourceMod.)" },
            // --- Steam Workshop ---
            new ModProfile { GameMatch = "ARK", Mechanism = ModMechanism.Workshop, WorkshopAppId = 346110, ConfigKey = "ActiveMods", ConfigFileRelative = @"ShooterGame\Saved\Config\WindowsServer\GameUserSettings.ini", ConfigSection = "ServerSettings", ListSeparator = ",", Notes = "Workshop ARK Survival Evolved (app 346110). Download via SteamCMD (or automatically via the -automanagedmods launch flag) then writes ActiveMods= in GameUserSettings.ini. ⚠️ Mods = .z to extract to Content/Mods (extraction not included). NB: ARK Survival ASCENDED uses CurseForge, NOT the Workshop." },
            new ModProfile { GameMatch = "Project Zomboid", Mechanism = ModMechanism.Workshop, WorkshopAppId = 108600, ConfigKey = "WorkshopItems", ListSeparator = ";", ServerAutoDownloads = true, Notes = "Workshop (app 108600). The PZ server downloads the WorkshopItems by itself at startup. ALSO fill in Mods= (the internal mod_ids, != Workshop ID, read from mod.info; the order = load order) in servertest.ini (user Zomboid folder). Separator = ';'." },
            new ModProfile { GameMatch = "Garry's Mod", Mechanism = ModMechanism.Workshop, WorkshopAppId = 4000, ServerAutoDownloads = true, Notes = "Workshop GMod (app 4000): create a Workshop collection (public/unlisted) and pass its ID via +host_workshop_collection (the server downloads+mounts at startup). Force clients: resource.AddWorkshop(\"ID\") in garrysmod\\lua\\autorun\\server\\workshop.lua." },
            new ModProfile { GameMatch = "Palworld", Mechanism = ModMechanism.Workshop, WorkshopAppId = 1623730, Notes = "Workshop Palworld (app 1623730, since \"Home Sweet Home\"). Download via SteamCMD -> the items go to Mods\\Workshop. To enable: in Mods\\PalModSettings.ini -> bGlobalEnableMod=true + one ActiveModList=<PackageName> line per mod (the PackageName comes from the mod's Info.json, NOT the ID or the folder). Restart required. (Automatic config writing not provided: ActiveModList requires the PackageName, not the ID.)" },
        };

        public static ModProfile For(string gameFullName)
        {
            if (string.IsNullOrEmpty(gameFullName)) { return null; }
            foreach (var p in _all)
            {
                if (gameFullName.StartsWith(p.GameMatch, StringComparison.OrdinalIgnoreCase)) { return p; }
            }
            return null; // unknown -> generic fallback in the UI
        }
    }
}
