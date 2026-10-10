using System;
using System.Collections.Generic;

namespace WindowsGSM.Functions.ConfigEditor
{
    /// <summary>Type of a setting -> drives the UI rendering (toggle, slider, combo, text field, masked secret).</summary>
    public enum FieldKind { Bool, Int, Float, Enum, Text, Secret }

    /// <summary>Curated specification of a setting: human label, description, type, bounds/values.</summary>
    public class FieldSpec
    {
        public string Key;            // exact key in the file
        public string Label;          // human-readable label
        public string Description;    // short help text
        public FieldKind Kind = FieldKind.Text;
        public string Group = "General";
        public double Min, Max, Step = 1; // for Int/Float
        public string[] EnumValues;       // for Enum

        public FieldSpec() { }
        public FieldSpec(string key, string label, FieldKind kind, string group, string desc = "")
        { Key = key; Label = label; Kind = kind; Group = group; Description = desc; }
    }

    /// <summary>
    /// Curated schema of a game: where to find the file (relative to serverfiles), which model opens it,
    /// and the list of polished settings. Unlisted keys stay editable in raw mode (fallback).
    /// </summary>
    public class GameConfigSchema
    {
        public string GameMatch;                 // match on GameServer.FullName (StartsWith)
        public string Label;                     // file label in the selector (optional)
        public string[] RelativePaths;           // candidate file paths under serverfiles

        /// <summary>
        /// Resolves one RelativePaths entry under serverFiles, allowing '*' as a path segment: Windrose
        /// keeps its world file under RocksDB_v2\&lt;game version&gt;\Worlds\&lt;world id&gt;\, both of which
        /// change on their own (a game update bumps the version, a new world adds a folder). Returns the
        /// most recently written match, or null when nothing matches.
        /// </summary>
        public static string ResolveUnder(string serverFiles, string relative)
        {
            if (string.IsNullOrEmpty(relative)) { return null; }
            string full = System.IO.Path.Combine(serverFiles ?? string.Empty, relative);
            if (relative.IndexOf('*') < 0) { return System.IO.File.Exists(full) ? full : null; }

            var current = new System.Collections.Generic.List<string> { serverFiles ?? string.Empty };
            var parts = relative.Split(new[] { '\\', '/' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                bool last = i == parts.Length - 1;
                var next = new System.Collections.Generic.List<string>();
                foreach (string baseDir in current)
                {
                    try
                    {
                        if (parts[i] == "..")
                        {
                            var up = System.IO.Directory.GetParent(baseDir);
                            if (up != null) { next.Add(up.FullName); }
                        }
                        else if (last)
                        {
                            next.AddRange(System.IO.Directory.GetFiles(baseDir, parts[i]));
                        }
                        else
                        {
                            next.AddRange(System.IO.Directory.GetDirectories(baseDir, parts[i]));
                        }
                    }
                    catch { }
                }
                current = next;
                if (current.Count == 0) { return null; }
            }
            return System.Linq.Enumerable.FirstOrDefault(
                System.Linq.Enumerable.OrderByDescending(current,
                    f => { try { return System.IO.File.GetLastWriteTimeUtc(f); } catch { return System.DateTime.MinValue; } }));
        }
        public string Model = "universal";        // "universal" (ConfigFile) | "palworld" (PalworldConfig)
        public string BoolTrue = "True";          // literal written for a true boolean (Palworld=True, MC/7DtD=true)
        public string BoolFalse = "False";
        public List<FieldSpec> Fields = new List<FieldSpec>();

        public FieldSpec FieldFor(string key)
        {
            foreach (var f in Fields)
            {
                if (string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)) { return f; }
            }
            return null;
        }
    }

    /// <summary>Registry of per-game schemas (hybrid: curated + raw fallback for the rest).</summary>
    public static class GameSchemas
    {
        private static readonly List<GameConfigSchema> _all = new List<GameConfigSchema>
        {
            Palworld(), SevenDaysToDie(), ProjectZomboid(), ProjectZomboidSandbox(), Windrose(), WindroseWorld(), Enshrouded(), Minecraft(), ArkGameUserSettings(), ArkGame()
        };

        /// <summary>Returns the first curated schema matching the game, or null (-> the UI falls back to raw mode).</summary>
        public static GameConfigSchema For(string gameFullName)
        {
            if (string.IsNullOrEmpty(gameFullName)) { return null; }
            foreach (var s in _all)
            {
                if (gameFullName.StartsWith(s.GameMatch, StringComparison.OrdinalIgnoreCase)) { return s; }
            }
            return null;
        }

        /// <summary>ALL curated schemas for the game (a game may have several files: ARK = GameUserSettings.ini + Game.ini).</summary>
        public static List<GameConfigSchema> All(string gameFullName)
        {
            var res = new List<GameConfigSchema>();
            if (string.IsNullOrEmpty(gameFullName)) { return res; }
            foreach (var s in _all)
            {
                if (gameFullName.StartsWith(s.GameMatch, StringComparison.OrdinalIgnoreCase)) { res.Add(s); }
            }
            return res;
        }

        private static GameConfigSchema Palworld()
        {
            var s = new GameConfigSchema
            {
                GameMatch = "Palworld",
                Model = "palworld",
                RelativePaths = new[] { @"Pal\Saved\Config\WindowsServer\PalWorldSettings.ini" }
            };
            // — Server —
            s.Fields.Add(new FieldSpec("ServerName", "Server name", FieldKind.Text, "Server", "Name shown in the server list."));
            s.Fields.Add(new FieldSpec("ServerDescription", "Description", FieldKind.Text, "Server", "Short text shown in the server browser."));
            s.Fields.Add(new FieldSpec("ServerPassword", "Server password", FieldKind.Secret, "Server", "Password required to join. Leave empty for a public server."));
            s.Fields.Add(new FieldSpec("AdminPassword", "Admin password", FieldKind.Secret, "Server", "Grants RCON / REST admin access (full control). Keep it secret."));
            s.Fields.Add(new FieldSpec("ServerPlayerMaxNum", "Max players", FieldKind.Int, "Server", "Maximum simultaneous players (Palworld caps at 32).") { Min = 1, Max = 32, Step = 1 });
            s.Fields.Add(new FieldSpec("PublicPort", "Public port", FieldKind.Int, "Server", "UDP game port players connect to (default 8211). Must be open/forwarded.") { Min = 1, Max = 65535, Step = 1 });
            // — Gameplay —
            s.Fields.Add(new FieldSpec("Difficulty", "Difficulty", FieldKind.Enum, "Gameplay", "Overall preset. 'None' lets you tune every rate manually.") { EnumValues = new[] { "None", "Casual", "Normal", "Hard" } });
            s.Fields.Add(new FieldSpec("DeathPenalty", "Death penalty", FieldKind.Enum, "Gameplay", "What you lose on death: nothing / items / items+gear / all (incl. Pals).") { EnumValues = new[] { "None", "Item", "ItemAndEquipment", "All" } });
            s.Fields.Add(new FieldSpec("bIsPvP", "PvP enabled", FieldKind.Bool, "Gameplay", "Allow players to fight each other."));
            s.Fields.Add(new FieldSpec("bEnablePlayerToPlayerDamage", "Player-to-player damage", FieldKind.Bool, "Gameplay", "Players can actually damage each other (needed for real PvP)."));
            s.Fields.Add(new FieldSpec("bEnableFriendlyFire", "Friendly fire", FieldKind.Bool, "Gameplay", "Guild members can damage each other."));
            s.Fields.Add(new FieldSpec("bHardcore", "Hardcore", FieldKind.Bool, "Gameplay", "Permanent character death (no respawn)."));
            // — Rates —
            s.Fields.Add(new FieldSpec("DayTimeSpeedRate", "Daytime speed", FieldKind.Float, "Rates", "How fast daytime passes (higher = shorter days).") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("NightTimeSpeedRate", "Nighttime speed", FieldKind.Float, "Rates", "How fast nighttime passes (higher = shorter nights).") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("ExpRate", "XP rate", FieldKind.Float, "Rates", "XP multiplier for players and Pals (higher = faster leveling).") { Min = 0.1, Max = 20, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PalCaptureRate", "Capture rate", FieldKind.Float, "Rates", "Chance of catching Pals (higher = easier captures).") { Min = 0.5, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PalSpawnNumRate", "Pal density", FieldKind.Float, "Rates", "How many wild Pals spawn in the world.") { Min = 0.1, Max = 3, Step = 0.1 });
            s.Fields.Add(new FieldSpec("CollectionDropRate", "Gathering rate", FieldKind.Float, "Rates", "Amount harvested from nodes (wood, stone, ore…).") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("EnemyDropItemRate", "Enemy loot", FieldKind.Float, "Rates", "Loot dropped by defeated enemies.") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("WorkSpeedRate", "Work speed", FieldKind.Float, "Rates", "How fast Pals perform base tasks (crafting, building…).") { Min = 0.1, Max = 5, Step = 0.1 });
            // — Combat —
            s.Fields.Add(new FieldSpec("PalDamageRateAttack", "Pal damage (attack)", FieldKind.Float, "Combat", "Damage dealt BY Pals (higher = stronger Pals).") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PalDamageRateDefense", "Pal damage (defense)", FieldKind.Float, "Combat", "Damage taken BY Pals (higher = they die faster).") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PlayerDamageRateAttack", "Player damage (attack)", FieldKind.Float, "Combat", "Damage dealt BY players.") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PlayerDamageRateDefense", "Player damage (defense)", FieldKind.Float, "Combat", "Damage taken BY players (higher = you're squishier).") { Min = 0.1, Max = 5, Step = 0.1 });
            // — Guild / Base —
            s.Fields.Add(new FieldSpec("GuildPlayerMaxNum", "Max players / guild", FieldKind.Int, "Guild & base", "Maximum members per guild.") { Min = 1, Max = 100, Step = 1 });
            s.Fields.Add(new FieldSpec("CoopPlayerMaxNum", "Max co-op players", FieldKind.Int, "Guild & base", "Max players sharing a single world in co-op.") { Min = 1, Max = 4, Step = 1 });
            s.Fields.Add(new FieldSpec("BaseCampMaxNum", "Max base camps", FieldKind.Int, "Guild & base", "Maximum base camps on the whole server.") { Min = 1, Max = 256, Step = 1 });
            s.Fields.Add(new FieldSpec("PalEggDefaultHatchingTime", "Egg hatching (h)", FieldKind.Float, "Guild & base", "Hours for a Pal egg to hatch (0 = instant).") { Min = 0, Max = 240, Step = 1 });
            // — Advanced —
            s.Fields.Add(new FieldSpec("AutoSaveSpan", "Auto-save (s)", FieldKind.Float, "Advanced", "Seconds between automatic world saves.") { Min = 30, Max = 3600, Step = 10 });
            s.Fields.Add(new FieldSpec("RESTAPIEnabled", "REST API enabled", FieldKind.Bool, "Advanced", "Built-in HTTP API. Used by WindowsGSM's admin panel & live player count."));
            s.Fields.Add(new FieldSpec("RESTAPIPort", "REST API port", FieldKind.Int, "Advanced", "Port of the REST API (default 8212).") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("RCONEnabled", "RCON enabled", FieldKind.Bool, "Advanced", "Remote console. Used by WindowsGSM's RCON console (Broadcast, ShowPlayers…)."));
            s.Fields.Add(new FieldSpec("RCONPort", "RCON port", FieldKind.Int, "Advanced", "Port of RCON (default 25575).") { Min = 1, Max = 65535, Step = 1 });

            // — Server (network / access) —
            s.Fields.Add(new FieldSpec("PublicIP", "Public IP", FieldKind.Text, "Server", "Public IP announced to players. Leave empty to auto-detect."));
            s.Fields.Add(new FieldSpec("Region", "Region", FieldKind.Text, "Server", "Server region code. Leave empty to auto-detect."));
            s.Fields.Add(new FieldSpec("bUseAuth", "Require authentication", FieldKind.Bool, "Server", "Require Steam auth to join (recommended ON)."));
            s.Fields.Add(new FieldSpec("BanListURL", "Ban list URL", FieldKind.Text, "Server", "URL of the ban list (default = official Palworld list)."));
            s.Fields.Add(new FieldSpec("bShowPlayerList", "Public player list", FieldKind.Bool, "Server", "Expose the connected-player list publicly."));
            s.Fields.Add(new FieldSpec("ChatPostLimitPerMinute", "Chat limit / min", FieldKind.Int, "Server", "Anti-spam: max chat messages per player per minute.") { Min = 1, Max = 600, Step = 1 });
            s.Fields.Add(new FieldSpec("CrossplayPlatforms", "Crossplay platforms", FieldKind.Text, "Server", "Allowed platforms, format (Steam,Xbox,PS5,Mac)."));
            s.Fields.Add(new FieldSpec("LogFormatType", "Log format", FieldKind.Enum, "Server", "Server log file format.") { EnumValues = new[] { "Text", "Json" } });
            s.Fields.Add(new FieldSpec("bIsShowJoinLeftMessage", "Join/leave messages", FieldKind.Bool, "Server", "Show join/leave messages in chat."));
            s.Fields.Add(new FieldSpec("bAllowClientMod", "Allow client mods", FieldKind.Bool, "Server", "Allow players with client-side mods to connect."));
            s.Fields.Add(new FieldSpec("bIsMultiplay", "Multiplayer flag", FieldKind.Bool, "Server", "Internal multiplayer flag (leave as is for a dedicated server)."));
            s.Fields.Add(new FieldSpec("bIsUseBackupSaveData", "Backup save data", FieldKind.Bool, "Server", "Keep backup copies of the save file."));

            // — Gameplay (world / features) —
            s.Fields.Add(new FieldSpec("bEnableInvaderEnemy", "Base raids", FieldKind.Bool, "Gameplay", "Enable invader/raid enemies attacking bases."));
            s.Fields.Add(new FieldSpec("EnablePredatorBossPal", "Predator/boss Pals", FieldKind.Bool, "Gameplay", "Enable roaming predator (boss) Pals."));
            s.Fields.Add(new FieldSpec("bEnableFastTravel", "Fast travel", FieldKind.Bool, "Gameplay", "Allow fast travel between statues."));
            s.Fields.Add(new FieldSpec("bEnableFastTravelOnlyBaseCamp", "Fast travel: base camps only", FieldKind.Bool, "Gameplay", "Restrict fast travel to base camps only."));
            s.Fields.Add(new FieldSpec("bIsStartLocationSelectByMap", "Choose start on map", FieldKind.Bool, "Gameplay", "Let players pick their starting location on the map."));
            s.Fields.Add(new FieldSpec("bExistPlayerAfterLogout", "Body stays after logout", FieldKind.Bool, "Gameplay", "The player's body remains in the world after logging out."));
            s.Fields.Add(new FieldSpec("bEnableNonLoginPenalty", "Non-login penalty", FieldKind.Bool, "Gameplay", "Apply a penalty for players who don't log in for a while."));
            s.Fields.Add(new FieldSpec("SupplyDropSpan", "Supply drop interval (min)", FieldKind.Int, "Gameplay", "Minutes between air supply drops.") { Min = 0, Max = 1440, Step = 5 });
            s.Fields.Add(new FieldSpec("bActiveUNKO", "UNKO feature", FieldKind.Bool, "Gameplay", "Enable the 'UNKO' (fertilizer) feature."));
            s.Fields.Add(new FieldSpec("bEnableAimAssistPad", "Aim assist (controller)", FieldKind.Bool, "Gameplay", "Aim assist for gamepad players."));
            s.Fields.Add(new FieldSpec("bEnableAimAssistKeyboard", "Aim assist (keyboard)", FieldKind.Bool, "Gameplay", "Aim assist for keyboard/mouse players."));
            s.Fields.Add(new FieldSpec("RandomizerType", "Randomizer", FieldKind.Text, "Gameplay", "Pal randomizer mode (None / Region / All)."));
            s.Fields.Add(new FieldSpec("RandomizerSeed", "Randomizer seed", FieldKind.Text, "Gameplay", "Seed for the randomizer (empty = random)."));
            s.Fields.Add(new FieldSpec("bIsRandomizerPalLevelRandom", "Random Pal levels", FieldKind.Bool, "Gameplay", "Randomize Pal levels when the randomizer is on."));

            // — Rates (survival / regen) —
            s.Fields.Add(new FieldSpec("PlayerStomachDecreaceRate", "Player hunger drain", FieldKind.Float, "Rates", "How fast the player's hunger drops (lower = eat less).") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PlayerStaminaDecreaceRate", "Player stamina drain", FieldKind.Float, "Rates", "How fast the player's stamina drops.") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PlayerAutoHPRegeneRate", "Player HP regen", FieldKind.Float, "Rates", "Player health regeneration speed.") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PlayerAutoHpRegeneRateInSleep", "Player HP regen (sleep)", FieldKind.Float, "Rates", "Player health regen while sleeping.") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PalStomachDecreaceRate", "Pal hunger drain", FieldKind.Float, "Rates", "How fast Pals' hunger drops.") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PalStaminaDecreaceRate", "Pal stamina drain", FieldKind.Float, "Rates", "How fast Pals' stamina drops.") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PalAutoHPRegeneRate", "Pal HP regen", FieldKind.Float, "Rates", "Pal health regeneration speed.") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PalAutoHpRegeneRateInSleep", "Pal HP regen (Palbox)", FieldKind.Float, "Rates", "Pal health regen while in the Palbox.") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("ItemWeightRate", "Item weight", FieldKind.Float, "Rates", "Item weight multiplier (lower = carry more).") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("EquipmentDurabilityDamageRate", "Equip. durability loss", FieldKind.Float, "Rates", "How fast equipment durability drops.") { Min = 0, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("ItemCorruptionMultiplier", "Item corruption", FieldKind.Float, "Rates", "Rate at which items corrupt/spoil.") { Min = 0, Max = 5, Step = 0.1 });

            // — Build & gathering —
            s.Fields.Add(new FieldSpec("BuildObjectHpRate", "Structure HP", FieldKind.Float, "Build & gather", "Structure health multiplier.") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("BuildObjectDamageRate", "Structure damage", FieldKind.Float, "Build & gather", "Damage dealt to structures.") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("BuildObjectDeteriorationDamageRate", "Structure decay", FieldKind.Float, "Build & gather", "Structure deterioration speed (0 = no decay).") { Min = 0, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("CollectionObjectHpRate", "Node HP", FieldKind.Float, "Build & gather", "HP of gatherable nodes (trees, rocks…).") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("CollectionObjectRespawnSpeedRate", "Node respawn", FieldKind.Float, "Build & gather", "How fast gatherable nodes respawn.") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("MaxBuildingLimitNum", "Max buildings", FieldKind.Int, "Build & gather", "Max buildings on the server (0 = unlimited).") { Min = 0, Max = 100000, Step = 100 });
            s.Fields.Add(new FieldSpec("bBuildAreaLimit", "Build area limit", FieldKind.Bool, "Build & gather", "Restrict building to the area around a base."));

            // — Drops & items —
            s.Fields.Add(new FieldSpec("DropItemMaxNum", "Max dropped items", FieldKind.Int, "Drops & items", "Max items lying on the ground worldwide.") { Min = 0, Max = 20000, Step = 100 });
            s.Fields.Add(new FieldSpec("DropItemMaxNum_UNKO", "Max dropped UNKO", FieldKind.Int, "Drops & items", "Max dropped 'UNKO' items.") { Min = 0, Max = 5000, Step = 50 });
            s.Fields.Add(new FieldSpec("DropItemAliveMaxHours", "Dropped item lifetime (h)", FieldKind.Float, "Drops & items", "Hours a dropped item stays before despawning.") { Min = 0, Max = 240, Step = 0.5 });
            s.Fields.Add(new FieldSpec("ItemContainerForceMarkDirtyInterval", "Container save interval", FieldKind.Float, "Drops & items", "Advanced: seconds between forced container saves.") { Min = 0, Max = 60, Step = 0.5 });

            // — Guild & base (extra) —
            s.Fields.Add(new FieldSpec("BaseCampWorkerMaxNum", "Workers / base", FieldKind.Int, "Guild & base", "Max working Pals per base camp.") { Min = 1, Max = 50, Step = 1 });
            s.Fields.Add(new FieldSpec("BaseCampMaxNumInGuild", "Bases / guild", FieldKind.Int, "Guild & base", "Max base camps per guild.") { Min = 1, Max = 20, Step = 1 });
            s.Fields.Add(new FieldSpec("bAutoResetGuildNoOnlinePlayers", "Auto-disband guilds", FieldKind.Bool, "Guild & base", "Disband guilds whose members are all offline for too long."));
            s.Fields.Add(new FieldSpec("AutoResetGuildTimeNoOnlinePlayers", "Auto-disband after (h)", FieldKind.Float, "Guild & base", "Hours offline before a guild is disbanded.") { Min = 1, Max = 720, Step = 1 });
            s.Fields.Add(new FieldSpec("GuildRejoinCooldownMinutes", "Guild rejoin cooldown (min)", FieldKind.Int, "Guild & base", "Cooldown before a player can rejoin a guild.") { Min = 0, Max = 1440, Step = 1 });
            s.Fields.Add(new FieldSpec("bEnableDefenseOtherGuildPlayer", "Defend vs other guilds", FieldKind.Bool, "Guild & base", "Allow base defenses to target other-guild players."));
            s.Fields.Add(new FieldSpec("bInvisibleOtherGuildBaseCampAreaFX", "Hide other base FX", FieldKind.Bool, "Guild & base", "Hide the area effect of other guilds' base camps."));
            s.Fields.Add(new FieldSpec("bCanPickupOtherGuildDeathPenaltyDrop", "Loot other guild drops", FieldKind.Bool, "Guild & base", "Allow looting death-penalty drops of other guilds."));

            // — Death / respawn / PvP —
            s.Fields.Add(new FieldSpec("bPalLost", "Lose Pals on death", FieldKind.Bool, "Death & PvP", "Player loses Pals in their party on death (hardcore-like)."));
            s.Fields.Add(new FieldSpec("bCharacterRecreateInHardcore", "Recreate char (hardcore)", FieldKind.Bool, "Death & PvP", "In hardcore, recreate the character on death."));
            s.Fields.Add(new FieldSpec("BlockRespawnTime", "Respawn block (s)", FieldKind.Float, "Death & PvP", "Time before you can respawn.") { Min = 0, Max = 120, Step = 1 });
            s.Fields.Add(new FieldSpec("RespawnPenaltyDurationThreshold", "Respawn penalty threshold", FieldKind.Float, "Death & PvP", "Advanced: threshold before the respawn penalty applies.") { Min = 0, Max = 3600, Step = 1 });
            s.Fields.Add(new FieldSpec("RespawnPenaltyTimeScale", "Respawn penalty scale", FieldKind.Float, "Death & PvP", "Advanced: multiplier for the respawn penalty duration.") { Min = 0, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("bDisplayPvPItemNumOnWorldMap_BaseCamp", "Show base PvP items on map", FieldKind.Bool, "Death & PvP", "Display PvP item counts for base camps on the world map."));
            s.Fields.Add(new FieldSpec("bDisplayPvPItemNumOnWorldMap_Player", "Show player PvP items on map", FieldKind.Bool, "Death & PvP", "Display PvP item counts for players on the world map."));
            s.Fields.Add(new FieldSpec("bAdditionalDropItemWhenPlayerKillingInPvPMode", "Extra PvP-kill drop", FieldKind.Bool, "Death & PvP", "Drop an additional item when killing a player in PvP."));
            s.Fields.Add(new FieldSpec("AdditionalDropItemWhenPlayerKillingInPvPMode", "PvP-kill drop item", FieldKind.Text, "Death & PvP", "Which item is dropped on a PvP kill."));
            s.Fields.Add(new FieldSpec("AdditionalDropItemNumWhenPlayerKillingInPvPMode", "PvP-kill drop count", FieldKind.Int, "Death & PvP", "How many items drop on a PvP kill.") { Min = 0, Max = 100, Step = 1 });

            // — Stat leveling —
            s.Fields.Add(new FieldSpec("bAllowEnhanceStat_Health", "Allow leveling: Health", FieldKind.Bool, "Stat leveling", "Players can spend points into Health."));
            s.Fields.Add(new FieldSpec("bAllowEnhanceStat_Attack", "Allow leveling: Attack", FieldKind.Bool, "Stat leveling", "Players can spend points into Attack."));
            s.Fields.Add(new FieldSpec("bAllowEnhanceStat_Stamina", "Allow leveling: Stamina", FieldKind.Bool, "Stat leveling", "Players can spend points into Stamina."));
            s.Fields.Add(new FieldSpec("bAllowEnhanceStat_Weight", "Allow leveling: Weight", FieldKind.Bool, "Stat leveling", "Players can spend points into carry Weight."));
            s.Fields.Add(new FieldSpec("bAllowEnhanceStat_WorkSpeed", "Allow leveling: Work speed", FieldKind.Bool, "Stat leveling", "Players can spend points into Work Speed."));

            // — Palbox / advanced —
            s.Fields.Add(new FieldSpec("bAllowGlobalPalboxExport", "Global Palbox export", FieldKind.Bool, "Advanced", "Allow exporting Pals to the global Palbox (cross-server)."));
            s.Fields.Add(new FieldSpec("bAllowGlobalPalboxImport", "Global Palbox import", FieldKind.Bool, "Advanced", "Allow importing Pals from the global Palbox (cross-server)."));
            s.Fields.Add(new FieldSpec("DenyTechnologyList", "Disabled technologies", FieldKind.Text, "Advanced", "Comma-separated list of technologies to disable (empty = all allowed)."));
            s.Fields.Add(new FieldSpec("ServerReplicatePawnCullDistance", "Pawn cull distance", FieldKind.Float, "Advanced", "Network draw distance for pawns (lower = better perf, less visible range).") { Min = 5000, Max = 30000, Step = 1000 });
            return s;
        }

        private static GameConfigSchema SevenDaysToDie()
        {
            var s = new GameConfigSchema
            {
                GameMatch = "7 Days to Die",
                Model = "universal",
                BoolTrue = "true",
                BoolFalse = "false",
                RelativePaths = new[] { "serverconfig.xml" }
            };
            s.Fields.Add(new FieldSpec("ServerName", "Server name", FieldKind.Text, "Server", "Name shown in the server browser."));
            s.Fields.Add(new FieldSpec("ServerDescription", "Description", FieldKind.Text, "Server", "Short description shown in the server browser."));
            s.Fields.Add(new FieldSpec("ServerWebsiteURL", "Website URL", FieldKind.Text, "Server", "Clickable link shown in the server browser."));
            s.Fields.Add(new FieldSpec("ServerPassword", "Password", FieldKind.Secret, "Server", "Password to join. Empty = open server."));
            s.Fields.Add(new FieldSpec("ServerLoginConfirmationText", "Login confirmation text", FieldKind.Text, "Server", "If set, players must confirm this message before joining."));
            s.Fields.Add(new FieldSpec("Region", "Region", FieldKind.Enum, "Server", "Region the server is in.") { EnumValues = new[] { "NorthAmericaEast", "NorthAmericaWest", "CentralAmerica", "SouthAmerica", "Europe", "Russia", "Asia", "MiddleEast", "Africa", "Oceania" } });
            s.Fields.Add(new FieldSpec("Language", "Language", FieldKind.Text, "Server", "Primary language (English name, e.g. French)."));
            s.Fields.Add(new FieldSpec("ServerVisibility", "Visibility", FieldKind.Int, "Server", "0 = not listed, 1 = friends only, 2 = public.") { Min = 0, Max = 2, Step = 1 });
            s.Fields.Add(new FieldSpec("ServerMaxPlayerCount", "Max players", FieldKind.Int, "Server", "Maximum concurrent players.") { Min = 1, Max = 64, Step = 1 });
            s.Fields.Add(new FieldSpec("ServerReservedSlots", "Reserved slots", FieldKind.Int, "Server", "Slots usable only by players with the permission level below.") { Min = 0, Max = 64, Step = 1 });
            s.Fields.Add(new FieldSpec("ServerReservedSlotsPermission", "Reserved slots permission", FieldKind.Int, "Server", "Permission level required to use reserved slots.") { Min = 0, Max = 1000, Step = 1 });
            s.Fields.Add(new FieldSpec("ServerAdminSlots", "Admin slots", FieldKind.Int, "Server", "Admins that can join even when the server is full.") { Min = 0, Max = 64, Step = 1 });
            s.Fields.Add(new FieldSpec("ServerAdminSlotsPermission", "Admin slots permission", FieldKind.Int, "Server", "Permission level required to use admin slots.") { Min = 0, Max = 1000, Step = 1 });
            s.Fields.Add(new FieldSpec("ServerPort", "Port", FieldKind.Int, "Network", "Game port (26900-26905 or 27015-27020 for LAN discovery).") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("ServerDisabledNetworkProtocols", "Disabled protocols", FieldKind.Text, "Network", "Comma-separated: LiteNetLib, SteamNetworking. Disable SteamNetworking when port-forwarding is set up."));
            s.Fields.Add(new FieldSpec("ServerMaxWorldTransferSpeedKiBs", "Max world transfer (KiB/s)", FieldKind.Int, "Network", "Speed the world is sent to new clients. Max ~1300.") { Min = 64, Max = 1300, Step = 64 });
            s.Fields.Add(new FieldSpec("EACEnabled", "EasyAntiCheat", FieldKind.Bool, "Network", "Enable/disable EasyAntiCheat."));
            s.Fields.Add(new FieldSpec("WebDashboardEnabled", "Web dashboard", FieldKind.Bool, "Web & Telnet", "Enable the web dashboard."));
            s.Fields.Add(new FieldSpec("WebDashboardPort", "Web dashboard port", FieldKind.Int, "Web & Telnet") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("WebDashboardUrl", "Web dashboard URL", FieldKind.Text, "Web & Telnet", "External URL if behind a reverse proxy. Empty = use public IP."));
            s.Fields.Add(new FieldSpec("EnableMapRendering", "Map rendering", FieldKind.Bool, "Web & Telnet", "Render the map to tiles (used by the web dashboard)."));
            s.Fields.Add(new FieldSpec("TelnetEnabled", "Telnet enabled", FieldKind.Bool, "Web & Telnet"));
            s.Fields.Add(new FieldSpec("TelnetPort", "Telnet port", FieldKind.Int, "Web & Telnet") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("TelnetPassword", "Telnet password", FieldKind.Secret, "Web & Telnet", "Empty = telnet listens on loopback only."));
            s.Fields.Add(new FieldSpec("TelnetFailedLoginLimit", "Telnet failed-login limit", FieldKind.Int, "Web & Telnet", "Wrong passwords before a client is blocked.") { Min = 1, Max = 100, Step = 1 });
            s.Fields.Add(new FieldSpec("TelnetFailedLoginsBlocktime", "Telnet block time (s)", FieldKind.Int, "Web & Telnet") { Min = 1, Max = 3600, Step = 1 });
            s.Fields.Add(new FieldSpec("TerminalWindowEnabled", "Terminal window", FieldKind.Bool, "Web & Telnet", "Show a console window (Windows only)."));
            s.Fields.Add(new FieldSpec("AdminFileName", "Admin file name", FieldKind.Text, "Web & Telnet", "serveradmin.xml path, relative to the save folder."));
            s.Fields.Add(new FieldSpec("HideCommandExecutionLog", "Hide command log", FieldKind.Int, "Web & Telnet", "0 = show all ... 3 = hide everything.") { Min = 0, Max = 3, Step = 1 });
            s.Fields.Add(new FieldSpec("GameWorld", "World", FieldKind.Text, "World", "RWG or an existing world name (Navezgane, PREGEN01...)."));
            s.Fields.Add(new FieldSpec("WorldGenSeed", "Seed", FieldKind.Text, "World", "Seed for RWG world generation."));
            s.Fields.Add(new FieldSpec("WorldGenSize", "World size", FieldKind.Int, "World", "Multiple of 2048, between 6144 and 10240.") { Min = 6144, Max = 10240, Step = 2048 });
            s.Fields.Add(new FieldSpec("GameName", "Game name", FieldKind.Text, "World", "Save game name and decoration seed."));
            s.Fields.Add(new FieldSpec("GameMode", "Game mode", FieldKind.Enum, "World") { EnumValues = new[] { "GameModeSurvival" } });
            s.Fields.Add(new FieldSpec("PersistentPlayerProfiles", "Persistent profiles", FieldKind.Bool, "World", "If on, players keep the profile they first joined with."));
            s.Fields.Add(new FieldSpec("MaxUncoveredMapChunksPerPlayer", "Max uncovered map chunks / player", FieldKind.Int, "World", "Map exploration limit. Default 131072 is about 32 km2.") { Min = 0, Max = 262144, Step = 1024 });
            s.Fields.Add(new FieldSpec("GameDifficulty", "Difficulty", FieldKind.Int, "Difficulty", "0 = easiest ... 5 = hardest.") { Min = 0, Max = 5, Step = 1 });
            s.Fields.Add(new FieldSpec("BlockDamagePlayer", "Player block damage (%)", FieldKind.Int, "Difficulty", "Damage players deal to blocks.") { Min = 0, Max = 500, Step = 10 });
            s.Fields.Add(new FieldSpec("BlockDamageAI", "AI block damage (%)", FieldKind.Int, "Difficulty", "Damage zombies deal to blocks.") { Min = 0, Max = 500, Step = 10 });
            s.Fields.Add(new FieldSpec("BlockDamageAIBM", "AI block damage - blood moon (%)", FieldKind.Int, "Difficulty", "Damage zombies deal to blocks during blood moons.") { Min = 0, Max = 500, Step = 10 });
            s.Fields.Add(new FieldSpec("XPMultiplier", "XP multiplier (%)", FieldKind.Int, "Difficulty") { Min = 0, Max = 1000, Step = 5 });
            s.Fields.Add(new FieldSpec("PlayerSafeZoneLevel", "Safe zone level", FieldKind.Int, "Difficulty", "New players at or below this level get a no-enemy safe zone on spawn.") { Min = 0, Max = 100, Step = 1 });
            s.Fields.Add(new FieldSpec("PlayerSafeZoneHours", "Safe zone duration (h)", FieldKind.Int, "Difficulty", "How long the spawn safe zone lasts.") { Min = 0, Max = 100, Step = 1 });
            s.Fields.Add(new FieldSpec("BuildCreate", "Creative mode", FieldKind.Bool, "Difficulty", "Cheat/creative mode."));
            s.Fields.Add(new FieldSpec("DayNightLength", "Day length (real minutes)", FieldKind.Int, "Difficulty", "Real minutes per in-game day.") { Min = 10, Max = 120, Step = 5 });
            s.Fields.Add(new FieldSpec("DayLightLength", "Daylight hours", FieldKind.Int, "Difficulty", "In-game hours of daylight per day.") { Min = 1, Max = 24, Step = 1 });
            s.Fields.Add(new FieldSpec("DeathPenalty", "Death penalty", FieldKind.Int, "Difficulty", "0 = none, 1 = XP, 2 = injured, 3 = permanent death.") { Min = 0, Max = 3, Step = 1 });
            s.Fields.Add(new FieldSpec("DropOnDeath", "Drop on death", FieldKind.Int, "Difficulty", "0 = nothing, 1 = all, 2 = toolbelt, 3 = backpack, 4 = delete all.") { Min = 0, Max = 4, Step = 1 });
            s.Fields.Add(new FieldSpec("DropOnQuit", "Drop on quit", FieldKind.Int, "Difficulty", "0 = nothing, 1 = all, 2 = toolbelt, 3 = backpack.") { Min = 0, Max = 3, Step = 1 });
            s.Fields.Add(new FieldSpec("BedrollDeadZoneSize", "Bedroll dead-zone size", FieldKind.Int, "Difficulty", "No zombies spawn within this box radius of a bedroll.") { Min = 0, Max = 100, Step = 1 });
            s.Fields.Add(new FieldSpec("BedrollExpiryTime", "Bedroll expiry (days)", FieldKind.Int, "Difficulty", "Real days a bedroll stays active after the owner was last online.") { Min = 0, Max = 365, Step = 1 });
            s.Fields.Add(new FieldSpec("MaxSpawnedZombies", "Max zombies (map)", FieldKind.Int, "Zombies", "Total zombies allowed on the whole map. Big performance impact.") { Min = 0, Max = 200, Step = 10 });
            s.Fields.Add(new FieldSpec("MaxSpawnedAnimals", "Max animals (map)", FieldKind.Int, "Zombies", "Total wildlife allowed on the whole map.") { Min = 0, Max = 200, Step = 10 });
            s.Fields.Add(new FieldSpec("EnemySpawnMode", "Enemy spawning", FieldKind.Bool, "Zombies", "Enable/disable enemy spawning."));
            s.Fields.Add(new FieldSpec("EnemyDifficulty", "Enemy difficulty", FieldKind.Int, "Zombies", "0 = normal, 1 = feral.") { Min = 0, Max = 1, Step = 1 });
            s.Fields.Add(new FieldSpec("ZombieFeralSense", "Feral sense", FieldKind.Int, "Zombies", "0 = off, 1 = day, 2 = night, 3 = all.") { Min = 0, Max = 3, Step = 1 });
            s.Fields.Add(new FieldSpec("ZombieMove", "Zombie speed (day)", FieldKind.Int, "Zombies", "0 = walk, 1 = jog, 2 = run, 3 = sprint, 4 = nightmare.") { Min = 0, Max = 4, Step = 1 });
            s.Fields.Add(new FieldSpec("ZombieMoveNight", "Zombie speed (night)", FieldKind.Int, "Zombies", "0 = walk ... 4 = nightmare.") { Min = 0, Max = 4, Step = 1 });
            s.Fields.Add(new FieldSpec("ZombieFeralMove", "Feral zombie speed", FieldKind.Int, "Zombies", "0 = walk ... 4 = nightmare.") { Min = 0, Max = 4, Step = 1 });
            s.Fields.Add(new FieldSpec("ZombieBMMove", "Blood moon zombie speed", FieldKind.Int, "Zombies", "0 = walk ... 4 = nightmare.") { Min = 0, Max = 4, Step = 1 });
            s.Fields.Add(new FieldSpec("BloodMoonFrequency", "Frequency (days)", FieldKind.Int, "Blood moon", "Days between blood moons. 0 = never.") { Min = 0, Max = 30, Step = 1 });
            s.Fields.Add(new FieldSpec("BloodMoonRange", "Random range (days)", FieldKind.Int, "Blood moon", "Random deviation from the frequency. 0 = exact.") { Min = 0, Max = 15, Step = 1 });
            s.Fields.Add(new FieldSpec("BloodMoonWarning", "Warning hour", FieldKind.Int, "Blood moon", "Hour the day counter turns red. -1 = never.") { Min = -1, Max = 24, Step = 1 });
            s.Fields.Add(new FieldSpec("BloodMoonEnemyCount", "Zombies per player", FieldKind.Int, "Blood moon", "Zombies alive per player during a horde (capped by max zombies).") { Min = 0, Max = 60, Step = 1 });
            s.Fields.Add(new FieldSpec("LootAbundance", "Loot abundance (%)", FieldKind.Int, "Loot") { Min = 25, Max = 600, Step = 25 });
            s.Fields.Add(new FieldSpec("LootRespawnDays", "Loot respawn (days)", FieldKind.Int, "Loot") { Min = 0, Max = 30, Step = 1 });
            s.Fields.Add(new FieldSpec("AirDropFrequency", "Air drop frequency (h)", FieldKind.Int, "Loot", "Game-hours between air drops. 0 = never.") { Min = 0, Max = 168, Step = 1 });
            s.Fields.Add(new FieldSpec("AirDropMarker", "Air drop marker", FieldKind.Bool, "Loot", "Show a map/compass marker for air drops."));
            s.Fields.Add(new FieldSpec("PartySharedKillRange", "Shared kill XP range", FieldKind.Int, "Multiplayer", "Distance to receive party shared kill XP and quest credit.") { Min = 0, Max = 100000, Step = 100 });
            s.Fields.Add(new FieldSpec("PlayerKillingMode", "PvP mode", FieldKind.Int, "Multiplayer", "0 = none, 1 = allies only, 2 = strangers only, 3 = everyone.") { Min = 0, Max = 3, Step = 1 });
            s.Fields.Add(new FieldSpec("LandClaimCount", "Max claims / player", FieldKind.Int, "Land claims") { Min = 0, Max = 50, Step = 1 });
            s.Fields.Add(new FieldSpec("LandClaimSize", "Claim size (blocks)", FieldKind.Int, "Land claims", "Area protected by a keystone.") { Min = 1, Max = 100, Step = 1 });
            s.Fields.Add(new FieldSpec("LandClaimDeadZone", "Claim dead zone (blocks)", FieldKind.Int, "Land claims", "Minimum distance between non-friend keystones.") { Min = 0, Max = 100, Step = 1 });
            s.Fields.Add(new FieldSpec("LandClaimExpiryTime", "Claim expiry (days)", FieldKind.Int, "Land claims", "Offline days before a claim expires.") { Min = 0, Max = 365, Step = 1 });
            s.Fields.Add(new FieldSpec("LandClaimDecayMode", "Claim decay mode", FieldKind.Int, "Land claims", "0 = slow (linear), 1 = fast (exponential), 2 = none.") { Min = 0, Max = 2, Step = 1 });
            s.Fields.Add(new FieldSpec("LandClaimOnlineDurabilityModifier", "Online durability x", FieldKind.Int, "Land claims", "Block hardness multiplier while the owner is online. 0 = invincible.") { Min = 0, Max = 64, Step = 1 });
            s.Fields.Add(new FieldSpec("LandClaimOfflineDurabilityModifier", "Offline durability x", FieldKind.Int, "Land claims", "Block hardness multiplier while the owner is offline. 0 = invincible.") { Min = 0, Max = 64, Step = 1 });
            s.Fields.Add(new FieldSpec("LandClaimOfflineDelay", "Offline delay (min)", FieldKind.Int, "Land claims", "Minutes after logout before a claim switches to offline hardness.") { Min = 0, Max = 1440, Step = 1 });
            s.Fields.Add(new FieldSpec("ServerMaxAllowedViewDistance", "Max view distance", FieldKind.Int, "Performance", "6-12. High impact on memory/performance.") { Min = 6, Max = 12, Step = 1 });
            s.Fields.Add(new FieldSpec("MaxQueuedMeshLayers", "Max queued mesh layers", FieldKind.Int, "Performance", "Lower = less RAM but slower chunk generation.") { Min = 100, Max = 10000, Step = 100 });
            s.Fields.Add(new FieldSpec("MaxChunkAge", "Max chunk age (days)", FieldKind.Int, "Performance", "In-game days before an unvisited chunk resets. -1 = never.") { Min = -1, Max = 9999, Step = 1 });
            s.Fields.Add(new FieldSpec("SaveDataLimit", "Save data limit (MB)", FieldKind.Int, "Performance", "Max disk per save in MB. -1 = no limit.") { Min = -1, Max = 100000, Step = 100 });
            s.Fields.Add(new FieldSpec("DynamicMeshEnabled", "Dynamic mesh", FieldKind.Bool, "Performance", "Enable the dynamic mesh system."));
            s.Fields.Add(new FieldSpec("DynamicMeshLandClaimOnly", "Dynamic mesh: claims only", FieldKind.Bool, "Performance", "Restrict dynamic mesh to land-claimed areas."));
            s.Fields.Add(new FieldSpec("DynamicMeshLandClaimBuffer", "Dynamic mesh buffer (chunks)", FieldKind.Int, "Performance") { Min = 1, Max = 32, Step = 1 });
            s.Fields.Add(new FieldSpec("DynamicMeshMaxItemCache", "Dynamic mesh item cache", FieldKind.Int, "Performance", "Concurrent items processed; higher = more RAM.") { Min = 1, Max = 32, Step = 1 });
            s.Fields.Add(new FieldSpec("TwitchServerPermission", "Twitch permission level", FieldKind.Int, "Twitch", "Permission level required to use Twitch integration.") { Min = 0, Max = 1000, Step = 1 });
            s.Fields.Add(new FieldSpec("TwitchBloodMoonAllowed", "Twitch during blood moon", FieldKind.Bool, "Twitch", "Allow Twitch actions during blood moons (can cause lag)."));
            return s;
        }

        private static GameConfigSchema ProjectZomboid()
        {
            var s = new GameConfigSchema
            {
                GameMatch = "Project Zomboid",
                Model = "universal",
                BoolTrue = "true",
                BoolFalse = "false",
                RelativePaths = new[] { @"..\Zomboid\Server\servertest.ini" }
            };
            s.Fields.Add(new FieldSpec("PVP", "PvP", FieldKind.Bool, "PvP"));
            s.Fields.Add(new FieldSpec("PauseEmpty", "Pause when empty", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("GlobalChat", "Global chat", FieldKind.Bool, "Chat"));
            s.Fields.Add(new FieldSpec("ChatStreams", "Chat streams", FieldKind.Text, "Chat"));
            s.Fields.Add(new FieldSpec("Open", "Open server", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("ServerWelcomeMessage", "Welcome message", FieldKind.Text, "Server"));
            s.Fields.Add(new FieldSpec("AutoCreateUserInWhiteList", "Auto-whitelist new users", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("DisplayUserName", "Display username", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("ShowFirstAndLastName", "Show first/last name", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("SpawnPoint", "Spawn point", FieldKind.Text, "World"));
            s.Fields.Add(new FieldSpec("SafetySystem", "Safety system", FieldKind.Bool, "PvP"));
            s.Fields.Add(new FieldSpec("ShowSafety", "Show safety", FieldKind.Bool, "PvP"));
            s.Fields.Add(new FieldSpec("SafetyToggleTimer", "Safety toggle timer", FieldKind.Int, "PvP"));
            s.Fields.Add(new FieldSpec("SafetyCooldownTimer", "Safety cooldown", FieldKind.Int, "PvP"));
            s.Fields.Add(new FieldSpec("SpawnItems", "Spawn items", FieldKind.Text, "World"));
            s.Fields.Add(new FieldSpec("DefaultPort", "Game port", FieldKind.Int, "Network"));
            s.Fields.Add(new FieldSpec("UDPPort", "UDP port", FieldKind.Int, "Network"));
            s.Fields.Add(new FieldSpec("ResetID", "Reset ID", FieldKind.Int, "Server"));
            s.Fields.Add(new FieldSpec("Mods", "Mods", FieldKind.Text, "World"));
            s.Fields.Add(new FieldSpec("Map", "Maps", FieldKind.Text, "World"));
            s.Fields.Add(new FieldSpec("DoLuaChecksum", "Lua checksum (anti-cheat)", FieldKind.Bool, "World"));
            s.Fields.Add(new FieldSpec("DenyLoginOnOverloadedServer", "Deny login if overloaded", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("Public", "Public (server browser)", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("PublicName", "Public name", FieldKind.Text, "Server"));
            s.Fields.Add(new FieldSpec("PublicDescription", "Public description", FieldKind.Text, "Server"));
            s.Fields.Add(new FieldSpec("MaxPlayers", "Max players", FieldKind.Int, "Server"));
            s.Fields.Add(new FieldSpec("PingLimit", "Ping limit (ms)", FieldKind.Int, "Server"));
            s.Fields.Add(new FieldSpec("HoursForLootRespawn", "Loot respawn (hours)", FieldKind.Int, "Loot"));
            s.Fields.Add(new FieldSpec("MaxItemsForLootRespawn", "Max items for respawn", FieldKind.Int, "Loot"));
            s.Fields.Add(new FieldSpec("ConstructionPreventsLootRespawn", "Construction blocks respawn", FieldKind.Bool, "Loot"));
            s.Fields.Add(new FieldSpec("DropOffWhiteListAfterDeath", "Drop from whitelist on death", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("NoFire", "No fire spread", FieldKind.Bool, "World"));
            s.Fields.Add(new FieldSpec("AnnounceDeath", "Announce deaths", FieldKind.Bool, "World"));
            s.Fields.Add(new FieldSpec("MinutesPerPage", "Minutes per book page", FieldKind.Float, "World"));
            s.Fields.Add(new FieldSpec("SaveWorldEveryMinutes", "Autosave (min)", FieldKind.Int, "World"));
            s.Fields.Add(new FieldSpec("PlayerSafehouse", "Player safehouses", FieldKind.Bool, "Safehouse"));
            s.Fields.Add(new FieldSpec("AdminSafehouse", "Admin safehouses", FieldKind.Bool, "Safehouse"));
            s.Fields.Add(new FieldSpec("SafehouseAllowTrepass", "Allow trespass", FieldKind.Bool, "Safehouse"));
            s.Fields.Add(new FieldSpec("SafehouseAllowFire", "Allow fire", FieldKind.Bool, "Safehouse"));
            s.Fields.Add(new FieldSpec("SafehouseAllowLoot", "Allow loot", FieldKind.Bool, "Safehouse"));
            s.Fields.Add(new FieldSpec("SafehouseAllowRespawn", "Allow respawn", FieldKind.Bool, "Safehouse"));
            s.Fields.Add(new FieldSpec("SafehouseDaySurvivedToClaim", "Days survived to claim", FieldKind.Int, "Safehouse"));
            s.Fields.Add(new FieldSpec("SafeHouseRemovalTime", "Removal time (h)", FieldKind.Int, "Safehouse"));
            s.Fields.Add(new FieldSpec("SafehouseAllowNonResidential", "Allow non-residential", FieldKind.Bool, "Safehouse"));
            s.Fields.Add(new FieldSpec("AllowDestructionBySledgehammer", "Sledgehammer destruction", FieldKind.Bool, "Safehouse"));
            s.Fields.Add(new FieldSpec("SledgehammerOnlyInSafehouse", "Sledgehammer only in safehouse", FieldKind.Bool, "Safehouse"));
            s.Fields.Add(new FieldSpec("KickFastPlayers", "Kick fast players", FieldKind.Bool, "World"));
            s.Fields.Add(new FieldSpec("ServerPlayerID", "Server player ID", FieldKind.Int, "Server"));
            s.Fields.Add(new FieldSpec("RCONPort", "RCON port", FieldKind.Int, "RCON & Discord"));
            s.Fields.Add(new FieldSpec("RCONPassword", "RCON password", FieldKind.Secret, "RCON & Discord"));
            s.Fields.Add(new FieldSpec("DiscordEnable", "Discord relay", FieldKind.Bool, "RCON & Discord"));
            s.Fields.Add(new FieldSpec("DiscordToken", "Discord token", FieldKind.Secret, "RCON & Discord"));
            s.Fields.Add(new FieldSpec("DiscordChannel", "Discord channel", FieldKind.Int, "RCON & Discord"));
            s.Fields.Add(new FieldSpec("DiscordChannelID", "Discord channel ID", FieldKind.Int, "RCON & Discord"));
            s.Fields.Add(new FieldSpec("Password", "Password", FieldKind.Secret, "Server"));
            s.Fields.Add(new FieldSpec("MaxAccountsPerUser", "Max accounts / user", FieldKind.Int, "Server"));
            s.Fields.Add(new FieldSpec("AllowCoop", "Allow split-screen coop", FieldKind.Bool, "World"));
            s.Fields.Add(new FieldSpec("SleepAllowed", "Sleep allowed", FieldKind.Bool, "World"));
            s.Fields.Add(new FieldSpec("SleepNeeded", "Sleep needed", FieldKind.Bool, "World"));
            s.Fields.Add(new FieldSpec("KnockedDownAllowed", "Knockdown allowed", FieldKind.Bool, "World"));
            s.Fields.Add(new FieldSpec("SneakModeHideFromOtherPlayers", "Sneak hides from players", FieldKind.Bool, "World"));
            s.Fields.Add(new FieldSpec("WorkshopItems", "Workshop items", FieldKind.Text, "World"));
            s.Fields.Add(new FieldSpec("SteamScoreboard", "Steam scoreboard", FieldKind.Bool, "Steam"));
            s.Fields.Add(new FieldSpec("SteamVAC", "Steam VAC", FieldKind.Bool, "Steam"));
            s.Fields.Add(new FieldSpec("UPnP", "UPnP", FieldKind.Bool, "Network"));
            s.Fields.Add(new FieldSpec("VoiceEnable", "Voice chat", FieldKind.Bool, "Voice"));
            s.Fields.Add(new FieldSpec("VoiceMinDistance", "Voice min distance", FieldKind.Float, "Voice"));
            s.Fields.Add(new FieldSpec("VoiceMaxDistance", "Voice max distance", FieldKind.Float, "Voice"));
            s.Fields.Add(new FieldSpec("Voice3D", "3D voice", FieldKind.Bool, "Voice"));
            s.Fields.Add(new FieldSpec("SpeedLimit", "Speed limit", FieldKind.Float, "Network"));
            s.Fields.Add(new FieldSpec("LoginQueueEnabled", "Login queue", FieldKind.Bool, "Network"));
            s.Fields.Add(new FieldSpec("LoginQueueConnectTimeout", "Queue timeout (s)", FieldKind.Int, "Network"));
            s.Fields.Add(new FieldSpec("server_browser_announced_ip", "Announced IP", FieldKind.Text, "Server"));
            s.Fields.Add(new FieldSpec("PlayerRespawnWithSelf", "Respawn at own death spot", FieldKind.Bool, "PvP"));
            s.Fields.Add(new FieldSpec("PlayerRespawnWithOther", "Respawn at other's spot", FieldKind.Bool, "PvP"));
            s.Fields.Add(new FieldSpec("FastForwardMultiplier", "Fast-forward multiplier", FieldKind.Float, "World"));
            s.Fields.Add(new FieldSpec("DisableSafehouseWhenPlayerConnected", "Disable when owner online", FieldKind.Bool, "Safehouse"));
            s.Fields.Add(new FieldSpec("Faction", "Factions", FieldKind.Bool, "Factions"));
            s.Fields.Add(new FieldSpec("FactionDaySurvivedToCreate", "Days to create", FieldKind.Int, "Factions"));
            s.Fields.Add(new FieldSpec("FactionPlayersRequiredForTag", "Players for tag", FieldKind.Int, "Factions"));
            s.Fields.Add(new FieldSpec("DisableRadioStaff", "Radio: staff", FieldKind.Bool, "Chat"));
            s.Fields.Add(new FieldSpec("DisableRadioAdmin", "Radio: admin", FieldKind.Bool, "Chat"));
            s.Fields.Add(new FieldSpec("DisableRadioGM", "Radio: GM", FieldKind.Bool, "Chat"));
            s.Fields.Add(new FieldSpec("DisableRadioOverseer", "Radio: overseer", FieldKind.Bool, "Chat"));
            s.Fields.Add(new FieldSpec("DisableRadioModerator", "Radio: moderator", FieldKind.Bool, "Chat"));
            s.Fields.Add(new FieldSpec("DisableRadioInvisible", "Radio: invisible", FieldKind.Bool, "Chat"));
            s.Fields.Add(new FieldSpec("ClientCommandFilter", "Client command filter", FieldKind.Text, "Logs"));
            s.Fields.Add(new FieldSpec("ClientActionLogs", "Client action logs", FieldKind.Text, "Logs"));
            s.Fields.Add(new FieldSpec("PerkLogs", "Perk logs", FieldKind.Bool, "Logs"));
            s.Fields.Add(new FieldSpec("ItemNumbersLimitPerContainer", "Item limit / container", FieldKind.Int, "Loot"));
            s.Fields.Add(new FieldSpec("BloodSplatLifespanDays", "Blood splat lifespan (days)", FieldKind.Int, "Loot"));
            s.Fields.Add(new FieldSpec("AllowNonAsciiUsername", "Allow non-ASCII usernames", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("BanKickGlobalSound", "Ban/kick global sound", FieldKind.Bool, "Logs"));
            s.Fields.Add(new FieldSpec("RemovePlayerCorpsesOnCorpseRemoval", "Remove player corpses", FieldKind.Bool, "Loot"));
            s.Fields.Add(new FieldSpec("TrashDeleteAll", "Trash deletes all", FieldKind.Bool, "Loot"));
            s.Fields.Add(new FieldSpec("PVPMeleeWhileHitReaction", "Melee while hit reaction", FieldKind.Bool, "PvP"));
            s.Fields.Add(new FieldSpec("MouseOverToSeeDisplayName", "Mouse over to see name", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("HidePlayersBehindYou", "Hide players behind you", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("PVPMeleeDamageModifier", "Melee damage (%)", FieldKind.Float, "PvP"));
            s.Fields.Add(new FieldSpec("PVPFirearmDamageModifier", "Firearm damage (%)", FieldKind.Float, "PvP"));
            s.Fields.Add(new FieldSpec("CarEngineAttractionModifier", "Car engine noise (zombies)", FieldKind.Float, "World"));
            s.Fields.Add(new FieldSpec("PlayerBumpPlayer", "Player bump player", FieldKind.Bool, "PvP"));
            s.Fields.Add(new FieldSpec("MapRemotePlayerVisibility", "Map player visibility", FieldKind.Int, "World"));
            s.Fields.Add(new FieldSpec("BackupsCount", "Backups count", FieldKind.Int, "Backups"));
            s.Fields.Add(new FieldSpec("BackupsOnStart", "Backup on start", FieldKind.Bool, "Backups"));
            s.Fields.Add(new FieldSpec("BackupsOnVersionChange", "Backup on version change", FieldKind.Bool, "Backups"));
            s.Fields.Add(new FieldSpec("BackupsPeriod", "Backup period (min)", FieldKind.Int, "Backups"));
                        s.Fields.Add(new FieldSpec("AnnounceAnimalDeath", "Announce animal death", FieldKind.Bool, "World"));
            s.Fields.Add(new FieldSpec("AntiCheatChecksum", "Anti-cheat: Checksum", FieldKind.Int, "Anti-cheat"));
            s.Fields.Add(new FieldSpec("AntiCheatHit", "Anti-cheat: Hit", FieldKind.Int, "Anti-cheat"));
            s.Fields.Add(new FieldSpec("AntiCheatNoClip", "Anti-cheat: NoClip", FieldKind.Int, "Anti-cheat"));
            s.Fields.Add(new FieldSpec("AntiCheatPacketException", "Anti-cheat: PacketException", FieldKind.Int, "Anti-cheat"));
            s.Fields.Add(new FieldSpec("AntiCheatPermission", "Anti-cheat: Permission", FieldKind.Int, "Anti-cheat"));
            s.Fields.Add(new FieldSpec("AntiCheatPlayer", "Anti-cheat: Player", FieldKind.Int, "Anti-cheat"));
            s.Fields.Add(new FieldSpec("AntiCheatSafeHouse", "Anti-cheat: SafeHouse", FieldKind.Int, "Anti-cheat"));
            s.Fields.Add(new FieldSpec("AntiCheatSafety", "Anti-cheat: Safety", FieldKind.Int, "Anti-cheat"));
            s.Fields.Add(new FieldSpec("AntiCheatSpeed", "Anti-cheat: Speed", FieldKind.Int, "Anti-cheat"));
            s.Fields.Add(new FieldSpec("AntiCheatXP", "Anti-cheat: XP", FieldKind.Int, "Anti-cheat"));
            s.Fields.Add(new FieldSpec("BadWordListFile", "Bad word list file", FieldKind.Text, "Chat"));
            s.Fields.Add(new FieldSpec("BadWordPolicy", "Bad word policy", FieldKind.Int, "Chat"));
            s.Fields.Add(new FieldSpec("BadWordReplacement", "Bad word replacement", FieldKind.Text, "Chat"));
            s.Fields.Add(new FieldSpec("ChatMessageCharacterLimit", "Chat character limit", FieldKind.Int, "Chat"));
            s.Fields.Add(new FieldSpec("ChatMessageSlowModeTime", "Chat slow mode", FieldKind.Int, "Chat"));
            s.Fields.Add(new FieldSpec("DisableBurntTowing", "Disable burnt vehicle towing", FieldKind.Bool, "Vehicles"));
            s.Fields.Add(new FieldSpec("DisableSafehouseWhenOwnerConnected", "Disable safehouse when owner online", FieldKind.Bool, "Safehouse"));
            s.Fields.Add(new FieldSpec("DisableScoreboard", "Disable scoreboard", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("DisableTrailerTowing", "Disable trailer towing", FieldKind.Bool, "Vehicles"));
            s.Fields.Add(new FieldSpec("DisableVehicleTowing", "Disable vehicle towing", FieldKind.Bool, "Vehicles"));
            s.Fields.Add(new FieldSpec("DiscordChatChannel", "Discord chat channel", FieldKind.Text, "RCON & Discord"));
            s.Fields.Add(new FieldSpec("DiscordCommandChannel", "Discord command channel", FieldKind.Text, "RCON & Discord"));
            s.Fields.Add(new FieldSpec("DiscordLogChannel", "Discord log channel", FieldKind.Text, "RCON & Discord"));
            s.Fields.Add(new FieldSpec("GoodWordListFile", "Allowed word list file", FieldKind.Text, "Chat"));
            s.Fields.Add(new FieldSpec("HideAdminsInPlayerList", "Hide admins in player list", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("HideDisguisedUserName", "Hide disguised username", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("MaxPacketsPerSecond", "Max packets per second", FieldKind.Int, "Network"));
            s.Fields.Add(new FieldSpec("MaxSafezoneSize", "Max safezone size", FieldKind.Int, "PvP"));
            s.Fields.Add(new FieldSpec("MultiplayerStatisticsPeriod", "Statistics period", FieldKind.Int, "Performance"));
            s.Fields.Add(new FieldSpec("PVPLogToolChat", "PvP log in chat", FieldKind.Bool, "Logs"));
            s.Fields.Add(new FieldSpec("PVPLogToolFile", "PvP log to file", FieldKind.Bool, "Logs"));
            s.Fields.Add(new FieldSpec("SafehouseDisableDisguises", "Safehouse disables disguises", FieldKind.Bool, "Safehouse"));
            s.Fields.Add(new FieldSpec("SafehousePreventsLootRespawn", "Safehouse prevents loot respawn", FieldKind.Bool, "Safehouse"));
            s.Fields.Add(new FieldSpec("SafetyDisconnectDelay", "Safety disconnect delay", FieldKind.Int, "PvP"));
            s.Fields.Add(new FieldSpec("Seed", "Map seed", FieldKind.Text, "World"));
            s.Fields.Add(new FieldSpec("ShowCoordinates", "Show coordinates", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("SwitchZombiesOwnershipEachUpdate", "Switch zombie ownership each update", FieldKind.Bool, "Performance"));
            s.Fields.Add(new FieldSpec("UltraSpeedDoesnotAffectToAnimals", "Ultra speed spares animals", FieldKind.Bool, "World"));
            s.Fields.Add(new FieldSpec("UsePhysicsHitReaction", "Physics hit reaction", FieldKind.Bool, "Advanced"));

            s.Fields.Add(new FieldSpec("UsernameDisguises", "Username disguises", FieldKind.Bool, "Safehouse"));
            s.Fields.Add(new FieldSpec("War", "Faction war", FieldKind.Bool, "Factions"));
            s.Fields.Add(new FieldSpec("WarStartDelay", "War start delay", FieldKind.Int, "Factions"));
            s.Fields.Add(new FieldSpec("WarDuration", "War duration", FieldKind.Int, "Factions"));
            s.Fields.Add(new FieldSpec("WarSafehouseHitPoints", "War safehouse hit points", FieldKind.Int, "Factions"));
            s.Fields.Add(new FieldSpec("WebhookAddress", "Webhook address", FieldKind.Text, "RCON & Discord"));

return s;
        }

        private static GameConfigSchema ProjectZomboidSandbox()
        {
            var s = new GameConfigSchema
            {
                GameMatch = "Project Zomboid",
                Label = "Sandbox (gameplay)",
                Model = "zomboidsandbox",
                BoolTrue = "true",
                BoolFalse = "false",
                RelativePaths = new[] { @"..\Zomboid\Server\servertest_SandboxVars.lua" }
            };
            s.Fields.Add(new FieldSpec("Zombies", "Zombies", FieldKind.Int, "Population"));
            s.Fields.Add(new FieldSpec("Distribution", "Distribution", FieldKind.Int, "Population"));
            s.Fields.Add(new FieldSpec("DayLength", "Day Length", FieldKind.Int, "Time & weather"));
            s.Fields.Add(new FieldSpec("StartYear", "Start Year", FieldKind.Int, "Time & weather"));
            s.Fields.Add(new FieldSpec("StartMonth", "Start Month", FieldKind.Int, "Time & weather"));
            s.Fields.Add(new FieldSpec("StartDay", "Start Day", FieldKind.Int, "Time & weather"));
            s.Fields.Add(new FieldSpec("StartTime", "Start Time", FieldKind.Int, "Time & weather"));
            s.Fields.Add(new FieldSpec("WaterShut", "Water Shut", FieldKind.Int, "Survival"));
            s.Fields.Add(new FieldSpec("ElecShut", "Elec Shut", FieldKind.Int, "Survival"));
            s.Fields.Add(new FieldSpec("WaterShutModifier", "Water Shut Modifier", FieldKind.Int, "Survival"));
            s.Fields.Add(new FieldSpec("ElecShutModifier", "Elec Shut Modifier", FieldKind.Int, "Survival"));
            s.Fields.Add(new FieldSpec("Temperature", "Temperature", FieldKind.Int, "Time & weather"));
            s.Fields.Add(new FieldSpec("Rain", "Rain", FieldKind.Int, "Time & weather"));
            s.Fields.Add(new FieldSpec("ErosionSpeed", "Erosion Speed", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("ErosionDays", "Erosion Days", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("ZombieAttractionMultiplier", "Zombie Attraction Multiplier", FieldKind.Float, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("VehicleEasyUse", "Vehicle Easy Use", FieldKind.Bool, "Vehicles"));
            s.Fields.Add(new FieldSpec("Farming", "Farming", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("CompostTime", "Compost Time", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("StatsDecrease", "Stats Decrease", FieldKind.Int, "Character"));
            s.Fields.Add(new FieldSpec("NatureAbundance", "Nature Abundance", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("Alarm", "Alarm", FieldKind.Int, "Events"));
            s.Fields.Add(new FieldSpec("LockedHouses", "Locked Houses", FieldKind.Int, "Events"));
            s.Fields.Add(new FieldSpec("StarterKit", "Starter Kit", FieldKind.Bool, "Character"));
            s.Fields.Add(new FieldSpec("Nutrition", "Nutrition", FieldKind.Bool, "Character"));
            s.Fields.Add(new FieldSpec("FoodRotSpeed", "Food Rot Speed", FieldKind.Int, "Survival"));
            s.Fields.Add(new FieldSpec("FridgeFactor", "Fridge Factor", FieldKind.Int, "Survival"));
            s.Fields.Add(new FieldSpec("SeenHoursPreventLootRespawn", "Seen Hours Prevent Loot Respawn", FieldKind.Int, "Loot"));
            s.Fields.Add(new FieldSpec("HoursForWorldItemRemoval", "Hours For World Item Removal", FieldKind.Float, "Advanced"));
            s.Fields.Add(new FieldSpec("ItemRemovalListBlacklistToggle", "Item Removal List Blacklist Toggle", FieldKind.Bool, "Advanced"));
            s.Fields.Add(new FieldSpec("TimeSinceApo", "Time Since Apo", FieldKind.Int, "Time & weather"));
            s.Fields.Add(new FieldSpec("PlantResilience", "Plant Resilience", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("PlantAbundance", "Plant Abundance", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("EndRegen", "End Regen", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("Helicopter", "Helicopter", FieldKind.Int, "Events"));
            s.Fields.Add(new FieldSpec("MetaEvent", "Meta Event", FieldKind.Int, "Events"));
            s.Fields.Add(new FieldSpec("SleepingEvent", "Sleeping Event", FieldKind.Int, "Events"));
            s.Fields.Add(new FieldSpec("GeneratorSpawning", "Generator Spawning", FieldKind.Int, "Survival"));
            s.Fields.Add(new FieldSpec("GeneratorFuelConsumption", "Generator Fuel Consumption", FieldKind.Float, "Survival"));
            s.Fields.Add(new FieldSpec("SurvivorHouseChance", "Survivor House Chance", FieldKind.Int, "Events"));
            s.Fields.Add(new FieldSpec("VehicleStoryChance", "Vehicle Story Chance", FieldKind.Int, "Events"));
            s.Fields.Add(new FieldSpec("ZoneStoryChance", "Zone Story Chance", FieldKind.Int, "Events"));
            s.Fields.Add(new FieldSpec("AnnotatedMapChance", "Annotated Map Chance", FieldKind.Int, "Events"));
            s.Fields.Add(new FieldSpec("CharacterFreePoints", "Character Free Points", FieldKind.Int, "Character"));
            s.Fields.Add(new FieldSpec("ConstructionBonusPoints", "Construction Bonus Points", FieldKind.Int, "Character"));
            s.Fields.Add(new FieldSpec("NightDarkness", "Night Darkness", FieldKind.Int, "Time & weather"));
            s.Fields.Add(new FieldSpec("NightLength", "Night Length", FieldKind.Int, "Time & weather"));
            s.Fields.Add(new FieldSpec("InjurySeverity", "Injury Severity", FieldKind.Int, "Character"));
            s.Fields.Add(new FieldSpec("BoneFracture", "Bone Fracture", FieldKind.Bool, "Character"));
            s.Fields.Add(new FieldSpec("HoursForCorpseRemoval", "Hours For Corpse Removal", FieldKind.Float, "Advanced"));
            s.Fields.Add(new FieldSpec("DecayingCorpseHealthImpact", "Decaying Corpse Health Impact", FieldKind.Int, "Survival"));
            s.Fields.Add(new FieldSpec("BloodLevel", "Blood Level", FieldKind.Int, "Advanced"));
            s.Fields.Add(new FieldSpec("ClothingDegradation", "Clothing Degradation", FieldKind.Int, "Character"));
            s.Fields.Add(new FieldSpec("FireSpread", "Fire Spread", FieldKind.Bool, "Advanced"));
            s.Fields.Add(new FieldSpec("DaysForRottenFoodRemoval", "Days For Rotten Food Removal", FieldKind.Int, "Advanced"));
            s.Fields.Add(new FieldSpec("AllowExteriorGenerator", "Allow Exterior Generator", FieldKind.Bool, "Survival"));
            s.Fields.Add(new FieldSpec("MaxFogIntensity", "Max Fog Intensity", FieldKind.Int, "Time & weather"));
            s.Fields.Add(new FieldSpec("MaxRainFxIntensity", "Max Rain Fx Intensity", FieldKind.Int, "Time & weather"));
            s.Fields.Add(new FieldSpec("EnableSnowOnGround", "Enable Snow On Ground", FieldKind.Bool, "Time & weather"));
            s.Fields.Add(new FieldSpec("MultiHitZombies", "Multi Hit Zombies", FieldKind.Bool, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("RearVulnerability", "Rear Vulnerability", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("AttackBlockMovements", "Attack Block Movements", FieldKind.Bool, "Advanced"));
            s.Fields.Add(new FieldSpec("AllClothesUnlocked", "All Clothes Unlocked", FieldKind.Bool, "Character"));
            s.Fields.Add(new FieldSpec("EnableTaintedWaterText", "Enable Tainted Water Text", FieldKind.Bool, "Advanced"));
            s.Fields.Add(new FieldSpec("CarSpawnRate", "Car Spawn Rate", FieldKind.Int, "Vehicles"));
            s.Fields.Add(new FieldSpec("ChanceHasGas", "Chance Has Gas", FieldKind.Int, "Vehicles"));
            s.Fields.Add(new FieldSpec("InitialGas", "Initial Gas", FieldKind.Int, "Vehicles"));
            s.Fields.Add(new FieldSpec("CarGasConsumption", "Car Gas Consumption", FieldKind.Float, "Vehicles"));
            s.Fields.Add(new FieldSpec("LockedCar", "Locked Car", FieldKind.Int, "Vehicles"));
            s.Fields.Add(new FieldSpec("CarGeneralCondition", "Car General Condition", FieldKind.Int, "Vehicles"));
            s.Fields.Add(new FieldSpec("CarDamageOnImpact", "Car Damage On Impact", FieldKind.Int, "Vehicles"));
            s.Fields.Add(new FieldSpec("DamageToPlayerFromHitByACar", "Damage To Player From Hit By A Car", FieldKind.Int, "Vehicles"));
            s.Fields.Add(new FieldSpec("TrafficJam", "Traffic Jam", FieldKind.Bool, "Vehicles"));
            s.Fields.Add(new FieldSpec("CarAlarm", "Car Alarm", FieldKind.Int, "Vehicles"));
            s.Fields.Add(new FieldSpec("PlayerDamageFromCrash", "Player Damage From Crash", FieldKind.Bool, "Vehicles"));
            s.Fields.Add(new FieldSpec("SirenShutoffHours", "Siren Shutoff Hours", FieldKind.Float, "Vehicles"));
            s.Fields.Add(new FieldSpec("RecentlySurvivorVehicles", "Recently Survivor Vehicles", FieldKind.Int, "Vehicles"));
            s.Fields.Add(new FieldSpec("EnableVehicles", "Enable Vehicles", FieldKind.Bool, "Vehicles"));
            s.Fields.Add(new FieldSpec("EnablePoisoning", "Enable Poisoning", FieldKind.Int, "Survival"));
            s.Fields.Add(new FieldSpec("MaggotSpawn", "Maggot Spawn", FieldKind.Int, "Advanced"));
            s.Fields.Add(new FieldSpec("LightBulbLifespan", "Light Bulb Lifespan", FieldKind.Float, "Advanced"));
            s.Fields.Add(new FieldSpec("Map.AllowMiniMap", "Allow Mini Map", FieldKind.Bool, "Map"));
            s.Fields.Add(new FieldSpec("Map.AllowWorldMap", "Allow World Map", FieldKind.Bool, "Map"));
            s.Fields.Add(new FieldSpec("Map.MapAllKnown", "Map All Known", FieldKind.Bool, "Map"));
            s.Fields.Add(new FieldSpec("ZombieLore.Speed", "Speed", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.Strength", "Strength", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.Toughness", "Toughness", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.Transmission", "Transmission", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.Mortality", "Mortality", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.Reanimate", "Reanimate", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.Cognition", "Cognition", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.CrawlUnderVehicle", "Crawl Under Vehicle", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.Memory", "Memory", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.Sight", "Sight", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.Hearing", "Hearing", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.ThumpNoChasing", "Thump No Chasing", FieldKind.Bool, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.ThumpOnConstruction", "Thump On Construction", FieldKind.Bool, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.ActiveOnly", "Active Only", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.TriggerHouseAlarm", "Trigger House Alarm", FieldKind.Bool, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.ZombiesDragDown", "Zombies Drag Down", FieldKind.Bool, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.ZombiesFenceLunge", "Zombies Fence Lunge", FieldKind.Bool, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.DisableFakeDead", "Disable Fake Dead", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieConfig.PopulationMultiplier", "Population Multiplier", FieldKind.Float, "Population"));
            s.Fields.Add(new FieldSpec("ZombieConfig.PopulationStartMultiplier", "Population Start Multiplier", FieldKind.Float, "Population"));
            s.Fields.Add(new FieldSpec("ZombieConfig.PopulationPeakMultiplier", "Population Peak Multiplier", FieldKind.Float, "Population"));
            s.Fields.Add(new FieldSpec("ZombieConfig.PopulationPeakDay", "Population Peak Day", FieldKind.Int, "Population"));
            s.Fields.Add(new FieldSpec("ZombieConfig.RespawnHours", "Respawn Hours", FieldKind.Float, "Population"));
            s.Fields.Add(new FieldSpec("ZombieConfig.RespawnUnseenHours", "Respawn Unseen Hours", FieldKind.Float, "Population"));
            s.Fields.Add(new FieldSpec("ZombieConfig.RespawnMultiplier", "Respawn Multiplier", FieldKind.Float, "Population"));
            s.Fields.Add(new FieldSpec("ZombieConfig.RedistributeHours", "Redistribute Hours", FieldKind.Float, "Population"));
            s.Fields.Add(new FieldSpec("ZombieConfig.FollowSoundDistance", "Follow Sound Distance", FieldKind.Int, "Population"));
            s.Fields.Add(new FieldSpec("ZombieConfig.RallyGroupSize", "Rally Group Size", FieldKind.Int, "Population"));
            s.Fields.Add(new FieldSpec("ZombieConfig.RallyTravelDistance", "Rally Travel Distance", FieldKind.Int, "Population"));
            s.Fields.Add(new FieldSpec("ZombieConfig.RallyGroupSeparation", "Rally Group Separation", FieldKind.Int, "Population"));
            s.Fields.Add(new FieldSpec("ZombieConfig.RallyGroupRadius", "Rally Group Radius", FieldKind.Int, "Population"));
                        s.Fields.Add(new FieldSpec("AbundantLootFactor", "Abundant loot factor", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("AlarmDecay", "Alarm decay", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("AlarmDecayModifier", "Alarm decay modifier", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("AmmoLootNew", "Loot: Ammo", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("AnimalAgeModifier", "Animal age modifier", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("AnimalEggHatch", "Egg hatch", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("AnimalGrassRegrowTime", "Grass regrow time", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("AnimalMatingSeason", "Mating season", FieldKind.Bool, "Nature & farming"));
            s.Fields.Add(new FieldSpec("AnimalMetaPredator", "Meta predators", FieldKind.Bool, "Nature & farming"));
            s.Fields.Add(new FieldSpec("AnimalMetaStatsModifier", "Meta stats modifier", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("AnimalMilkIncModifier", "Milk yield", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("AnimalPathChance", "Animal path chance", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("AnimalPregnancyTime", "Pregnancy time", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("AnimalRanchChance", "Ranch chance", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("AnimalSoundAttractZombies", "Animal noise attracts zombies", FieldKind.Bool, "Nature & farming"));
            s.Fields.Add(new FieldSpec("AnimalStatsModifier", "Animal stats modifier", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("AnimalTrackChance", "Track chance", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("AnimalWoolIncModifier", "Wool yield", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("Basement.SpawnFrequency", "Basement frequency", FieldKind.Int, "Map"));
            s.Fields.Add(new FieldSpec("BloodSplatLifespanDays", "Blood splat lifespan", FieldKind.Int, "World"));
            s.Fields.Add(new FieldSpec("CannedFoodLootNew", "Loot: CannedFood", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("ClayLakeChance", "Clay near lakes", FieldKind.Float, "Nature & farming"));
            s.Fields.Add(new FieldSpec("ClayRiverChance", "Clay near rivers", FieldKind.Float, "Nature & farming"));
            s.Fields.Add(new FieldSpec("ClimateCycle", "Climate cycle", FieldKind.Int, "Time & weather"));
            s.Fields.Add(new FieldSpec("ClothingLootNew", "Loot: Clothing", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("CommonLootFactor", "Common loot factor", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("ConstructionPreventsLootRespawn", "Construction blocks respawn", FieldKind.Bool, "Loot"));
            s.Fields.Add(new FieldSpec("ContainerLootNew", "Loot: Container", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("CookwareLootNew", "Loot: Cookware", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("DayNightCycle", "Day/night cycle", FieldKind.Int, "Time & weather"));
            s.Fields.Add(new FieldSpec("DaysUntilMaximumDiminishedLoot", "Days until diminished loot", FieldKind.Int, "Loot"));
            s.Fields.Add(new FieldSpec("DaysUntilMaximumLooted", "Days until maximum looted", FieldKind.Int, "Loot"));
            s.Fields.Add(new FieldSpec("DaysUntilMaximumRatIndex", "Days until max rat index", FieldKind.Int, "Loot"));
            s.Fields.Add(new FieldSpec("DiscomfortFactor", "Discomfort factor", FieldKind.Float, "Character"));
            s.Fields.Add(new FieldSpec("EasyClimbing", "Easy climbing", FieldKind.Bool, "Character"));
            s.Fields.Add(new FieldSpec("ExtremeLootFactor", "Extreme loot factor", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("FarmingAmountNew", "Farming yield", FieldKind.Float, "Nature & farming"));
            s.Fields.Add(new FieldSpec("FarmingLootNew", "Loot: Farming", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("FarmingSpeedNew", "Farming speed", FieldKind.Float, "Nature & farming"));
            s.Fields.Add(new FieldSpec("FirearmHeadGearEffect", "Headgear affects hearing", FieldKind.Bool, "Combat"));
            s.Fields.Add(new FieldSpec("FirearmJamMultiplier", "Jam multiplier", FieldKind.Float, "Combat"));
            s.Fields.Add(new FieldSpec("FirearmMoodleMultiplier", "Moodle effect", FieldKind.Float, "Combat"));
            s.Fields.Add(new FieldSpec("FirearmNoiseMultiplier", "Noise multiplier", FieldKind.Float, "Combat"));
            s.Fields.Add(new FieldSpec("FirearmUseDamageChance", "Wear on use", FieldKind.Int, "Combat"));
            s.Fields.Add(new FieldSpec("FirearmWeatherMultiplier", "Weather effect", FieldKind.Float, "Combat"));
            s.Fields.Add(new FieldSpec("FishAbundance", "Fish abundance", FieldKind.Int, "Nature & farming"));
            s.Fields.Add(new FieldSpec("FogCycle", "Fog cycle", FieldKind.Int, "Time & weather"));
            s.Fields.Add(new FieldSpec("FoodLootNew", "Loot: Food", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("FuelStationGasEmptyChance", "Empty pump chance", FieldKind.Int, "Survival"));
            s.Fields.Add(new FieldSpec("FuelStationGasInfinite", "Infinite fuel", FieldKind.Bool, "Survival"));
            s.Fields.Add(new FieldSpec("FuelStationGasMax", "Max station fuel", FieldKind.Float, "Survival"));
            s.Fields.Add(new FieldSpec("FuelStationGasMin", "Min station fuel", FieldKind.Float, "Survival"));
            s.Fields.Add(new FieldSpec("GeneratorTileRange", "Generator range", FieldKind.Int, "Survival"));
            s.Fields.Add(new FieldSpec("GeneratorVerticalPowerRange", "Generator vertical range", FieldKind.Int, "Survival"));
            s.Fields.Add(new FieldSpec("HoursForLootRespawn", "Hours for loot respawn", FieldKind.Int, "Loot"));
            s.Fields.Add(new FieldSpec("InsaneLootFactor", "Insane loot factor", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("KeyLootNew", "Loot: Key", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("KillInsideCrops", "Kill indoor crops", FieldKind.Bool, "Nature & farming"));
            s.Fields.Add(new FieldSpec("LevelForDismantleXPCutoff", "Dismantle XP cutoff", FieldKind.Int, "Stat leveling"));
            s.Fields.Add(new FieldSpec("LevelForMediaXPCutoff", "Media XP cutoff", FieldKind.Int, "Stat leveling"));
            s.Fields.Add(new FieldSpec("LiteratureCooldown", "Literature cooldown", FieldKind.Int, "Character"));
            s.Fields.Add(new FieldSpec("LiteratureLootNew", "Loot: Literature", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("LootItemRemovalList", "Loot item removal list", FieldKind.Text, "Loot"));
            s.Fields.Add(new FieldSpec("Map.MapNeedsLight", "Map needs light", FieldKind.Bool, "Map"));
            s.Fields.Add(new FieldSpec("MaterialLootNew", "Loot: Material", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("MaxItemsForLootRespawn", "Max items for respawn", FieldKind.Int, "Loot"));
            s.Fields.Add(new FieldSpec("MaximumDiminishedLoot", "Maximum diminished loot", FieldKind.Int, "Loot"));
            s.Fields.Add(new FieldSpec("MaximumFireFuelHours", "Max fire fuel hours", FieldKind.Int, "Survival"));
            s.Fields.Add(new FieldSpec("MaximumLooted", "Maximum looted", FieldKind.Int, "Loot"));
            s.Fields.Add(new FieldSpec("MaximumLootedBuildingRooms", "Max looted rooms", FieldKind.Int, "Loot"));
            s.Fields.Add(new FieldSpec("MaximumRatIndex", "Maximum rat index", FieldKind.Int, "Loot"));
            s.Fields.Add(new FieldSpec("MechanicsLootNew", "Loot: Mechanics", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("MediaLootNew", "Loot: Media", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("MedicalLootNew", "Loot: Medical", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("MementoLootNew", "Loot: Memento", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("MetaKnowledge", "Meta knowledge", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("MinutesPerPage", "Minutes per page", FieldKind.Float, "Character"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Aiming", "XP multiplier: Aiming", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Axe", "XP multiplier: Axe", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Blacksmith", "XP multiplier: Blacksmith", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Blunt", "XP multiplier: Blunt", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Butchering", "XP multiplier: Butchering", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Carving", "XP multiplier: Carving", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Cooking", "XP multiplier: Cooking", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Doctor", "XP multiplier: Doctor", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Electricity", "XP multiplier: Electricity", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Farming", "XP multiplier: Farming", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Fishing", "XP multiplier: Fishing", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Fitness", "XP multiplier: Fitness", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.FlintKnapping", "XP multiplier: FlintKnapping", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Glassmaking", "XP multiplier: Glassmaking", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Global", "XP multiplier: global", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.GlobalToggle", "Use global XP multiplier", FieldKind.Bool, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Husbandry", "XP multiplier: Husbandry", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Lightfoot", "XP multiplier: Lightfoot", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.LongBlade", "XP multiplier: LongBlade", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Maintenance", "XP multiplier: Maintenance", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Masonry", "XP multiplier: Masonry", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Mechanics", "XP multiplier: Mechanics", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.MetalWelding", "XP multiplier: MetalWelding", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Nimble", "XP multiplier: Nimble", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.PlantScavenging", "XP multiplier: PlantScavenging", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Pottery", "XP multiplier: Pottery", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Reloading", "XP multiplier: Reloading", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.SmallBlade", "XP multiplier: SmallBlade", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.SmallBlunt", "XP multiplier: SmallBlunt", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Sneak", "XP multiplier: Sneak", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Spear", "XP multiplier: Spear", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Sprinting", "XP multiplier: Sprinting", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Strength", "XP multiplier: Strength", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Tailoring", "XP multiplier: Tailoring", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Tracking", "XP multiplier: Tracking", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Trapping", "XP multiplier: Trapping", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MultiplierConfig.Woodwork", "XP multiplier: Woodwork", FieldKind.Float, "Stat leveling"));
            s.Fields.Add(new FieldSpec("MuscleStrainFactor", "Muscle strain", FieldKind.Float, "Character"));
            s.Fields.Add(new FieldSpec("NegativeTraitsPenalty", "Negative traits penalty", FieldKind.Int, "Character"));
            s.Fields.Add(new FieldSpec("NoBlackClothes", "No black clothes", FieldKind.Bool, "Character"));
            s.Fields.Add(new FieldSpec("NormalLootFactor", "Normal loot factor", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("OtherLootNew", "Loot: Other", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("PlaceDirtAboveground", "Dirt aboveground", FieldKind.Bool, "Nature & farming"));
            s.Fields.Add(new FieldSpec("PlantGrowingSeasons", "Plant growing seasons", FieldKind.Bool, "Nature & farming"));
            s.Fields.Add(new FieldSpec("RangedWeaponLootNew", "Loot: RangedWeapon", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("RareLootFactor", "Rare loot factor", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("RecipeResourceLoot", "Recipe resource loot", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("RemoveStoryLoot", "Remove story loot", FieldKind.Bool, "Loot"));
            s.Fields.Add(new FieldSpec("RemoveZombieLoot", "Remove zombie loot", FieldKind.Bool, "Loot"));
            s.Fields.Add(new FieldSpec("RollsMultiplier", "Loot rolls multiplier", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("RuralLooted", "Rural looted", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("SeeNotLearntRecipe", "See unlearnt recipes", FieldKind.Bool, "Character"));
            s.Fields.Add(new FieldSpec("SirenEffectsZombies", "Sirens attract zombies", FieldKind.Bool, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("SkillBookLoot", "Skill book loot", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("SurvivalGearsLootNew", "Loot: SurvivalGears", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("ToolLootNew", "Loot: Tool", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("VERSION", "Sandbox version", FieldKind.Int, "Advanced"));
            s.Fields.Add(new FieldSpec("WeaponLootNew", "Loot: Weapon", FieldKind.Float, "Loot"));
            s.Fields.Add(new FieldSpec("WorldItemRemovalList", "World item removal list", FieldKind.Text, "Loot"));
            s.Fields.Add(new FieldSpec("WoundInfectionFactor", "Wound infection", FieldKind.Float, "Character"));
            s.Fields.Add(new FieldSpec("ZombieConfig.RallyGroupSizeVariance", "Rally group variance", FieldKind.Int, "Population"));
            s.Fields.Add(new FieldSpec("ZombieConfig.ZombiesCountBeforeDelete", "Zombies before delete", FieldKind.Int, "Population"));
            s.Fields.Add(new FieldSpec("ZombieHealthImpact", "Health impact", FieldKind.Bool, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.ChanceOfAttachedWeapon", "Attached weapon chance", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.DoorOpeningPercentage", "Door opening", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.FenceDamageMultiplier", "Fence damage", FieldKind.Float, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.FenceThumpersRequired", "Fence thumpers required", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.PlayerSpawnZombieRemoval", "Spawn zombie removal", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.SpottedLogic", "Spotting logic", FieldKind.Bool, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.SprinterPercentage", "Sprinter percentage", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.ZombiesArmorFactor", "Zombie armor", FieldKind.Float, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.ZombiesCrawlersDragDown", "Crawlers drag down", FieldKind.Bool, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.ZombiesFallDamage", "Zombie fall damage", FieldKind.Float, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieLore.ZombiesMaxDefense", "Zombie max defense", FieldKind.Int, "Zombie behaviour"));
            s.Fields.Add(new FieldSpec("ZombieMigrate", "Zombie migration", FieldKind.Bool, "Population"));
            s.Fields.Add(new FieldSpec("ZombiePopLootEffect", "Population affects loot", FieldKind.Int, "Population"));
            s.Fields.Add(new FieldSpec("ZombieRespawn", "Zombie respawn", FieldKind.Int, "Population"));
            s.Fields.Add(new FieldSpec("ZombieVoronoiNoise", "Voronoi distribution", FieldKind.Bool, "Population"));

return s;
        }

        private static GameConfigSchema Windrose()
        {
            var s = new GameConfigSchema
            {
                GameMatch = "Windrose",
                Model = "windrosejson",
                BoolTrue = "true",
                BoolFalse = "false",
                RelativePaths = new[] { @"R5\ServerDescription.json" }
            };
            // — Server —
            s.Fields.Add(new FieldSpec("ServerName", "Server name", FieldKind.Text, "Server", "Name shown to players (invite codes can look alike)."));
            s.Fields.Add(new FieldSpec("InviteCode", "Invite code", FieldKind.Text, "Server", "Players join with this code (Play > Connect to Server). 6+ letters/digits, case-sensitive."));
            s.Fields.Add(new FieldSpec("Password", "Password", FieldKind.Secret, "Server", "Server password. Also set 'Password protected' accordingly."));
            s.Fields.Add(new FieldSpec("IsPasswordProtected", "Password protected", FieldKind.Bool, "Server", "Must be ON when a password is set, OFF when empty."));
            s.Fields.Add(new FieldSpec("MaxPlayerCount", "Max players", FieldKind.Int, "Server", "Maximum simultaneous players.") { Min = 1, Max = 16, Step = 1 });
            s.Fields.Add(new FieldSpec("UserSelectedRegion", "Region", FieldKind.Enum, "Server", "Connection Service region. Empty = auto by latency. EU also covers NA.") { EnumValues = new[] { "", "EU", "SEA", "CIS" } });
            // — Network —
            s.Fields.Add(new FieldSpec("UseDirectConnection", "Direct connection", FieldKind.Bool, "Network", "ON = direct IP sockets (needs port forwarding). OFF = ICE P2P via the Connection Service (no port forwarding, join by invite code)."));
            s.Fields.Add(new FieldSpec("DirectConnectionServerPort", "Direct connection port", FieldKind.Int, "Network", "TCP+UDP port for direct connections (-1 = unset, default 7777).") { Min = -1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("P2pProxyAddress", "P2P listen address", FieldKind.Text, "Network", "IP address for listening sockets."));
            s.Fields.Add(new FieldSpec("DirectConnectionProxyAddress", "Direct listen address", FieldKind.Text, "Network", "Bind address for direct connections."));
            s.Fields.Add(new FieldSpec("DirectConnectionServerAddress", "Direct server address", FieldKind.Text, "Network", "Reserved for future use (not used currently)."));
            // — Advanced —
            s.Fields.Add(new FieldSpec("WorldIslandId", "World island ID", FieldKind.Text, "Advanced", "ID of the world loaded at startup. Must match a WorldDescription.json. Change only to switch worlds."));
            s.Fields.Add(new FieldSpec("AutoLoadLatestBackupIfHasBroken", "Auto-load backup if broken", FieldKind.Bool, "Advanced", "Load the latest backup automatically if the save is corrupted."));
            s.Fields.Add(new FieldSpec("CanLaunchMultipleServerInstances", "Allow multiple instances", FieldKind.Bool, "Advanced", "Allow several server instances on the same machine."));
            return s;
        }

        private static GameConfigSchema WindroseWorld()
        {
            // Per-world gameplay settings. The file lives under a version- and world-id-keyed path that
            // both move on their own, so RelativePaths uses '*' segments (resolved by GameConfigSchema.ResolveUnder).
            var s = new GameConfigSchema
            {
                GameMatch = "Windrose",
                Model = "windroseworld",
                Label = "Réglages du monde",
                BoolTrue = "true",
                BoolFalse = "false",
                RelativePaths = new[] { @"R5\Saved\SaveProfiles\Default\RocksDB_v2\*\Worlds\*\WorldDescription.json" }
            };
            // — World —
            s.Fields.Add(new FieldSpec("WorldName", "World name", FieldKind.Text, "World", "Display name of this world."));
            s.Fields.Add(new FieldSpec("WorldPresetType", "Difficulty preset", FieldKind.Enum, "World", "Gameplay preset. Changing any parameter below forces this to Custom.") { EnumValues = new[] { "Easy", "Medium", "Hard", "Custom" } });
            // — Combat — (values only apply under the Custom preset)
            s.Fields.Add(new FieldSpec("CombatDifficulty", "Combat difficulty", FieldKind.Enum, "Combat", "How difficult boss encounters are and how aggressive enemies are.") { EnumValues = new[] { "Easy", "Normal", "Hard" } });
            s.Fields.Add(new FieldSpec("MobHealthMultiplier", "Enemy health", FieldKind.Float, "Combat", "How much health enemies have.") { Min = 0.2, Max = 5.0, Step = 0.1 });
            s.Fields.Add(new FieldSpec("MobDamageMultiplier", "Enemy damage", FieldKind.Float, "Combat", "How hard enemies hit.") { Min = 0.2, Max = 5.0, Step = 0.1 });
            s.Fields.Add(new FieldSpec("ShipsHealthMultiplier", "Enemy ship health", FieldKind.Float, "Combat", "How much health enemy ships have.") { Min = 0.4, Max = 5.0, Step = 0.1 });
            s.Fields.Add(new FieldSpec("ShipsDamageMultiplier", "Enemy ship damage", FieldKind.Float, "Combat", "How much damage enemy ships deal.") { Min = 0.2, Max = 2.5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("BoardingDifficultyMultiplier", "Boarding difficulty", FieldKind.Float, "Combat", "How many enemy sailors must be defeated to win a boarding.") { Min = 0.2, Max = 5.0, Step = 0.1 });
            // — Co-op —
            s.Fields.Add(new FieldSpec("Coop.SharedQuests", "Shared quests", FieldKind.Bool, "Co-op", "A co-op quest completed by one player auto-completes for the others who have it active."));
            s.Fields.Add(new FieldSpec("Coop.StatsCorrectionModifier", "Enemy scaling (players)", FieldKind.Float, "Co-op", "Scales enemy health / posture loss with the number of players.") { Min = 0.0, Max = 2.0, Step = 0.1 });
            s.Fields.Add(new FieldSpec("Coop.ShipStatsCorrectionModifier", "Enemy ship scaling (players)", FieldKind.Float, "Co-op", "Scales enemy ship health with the number of players.") { Min = 0.0, Max = 2.0, Step = 0.1 });
            // — Exploration —
            s.Fields.Add(new FieldSpec("EasyExplore", "Immersive exploration", FieldKind.Bool, "Exploration", "ON hides points-of-interest map markers (harder exploration). Note: the name is inverted — true makes it harder."));
            return s;
        }

        // Enshrouded user groups created by the game (current builds: Admin/Friend/Guest/Visitor; older
        // worlds have a single "Default"). Fields only show for the groups present in the file.
        // A property, not a static field: the schema registry above is built during static init, before
        // any field declared below it would be assigned.
        internal static string[] EnshroudedGroups => new[] { "Admin", "Friend", "Guest", "Visitor", "Default" };

        private static GameConfigSchema Enshrouded()
        {
            // Values and ranges: enum values read from enshrouded_server.exe, ranges from the in-game sliders.
            var s = new GameConfigSchema
            {
                GameMatch = "Enshrouded",
                Model = "enshroudedjson",
                BoolTrue = "true",
                BoolFalse = "false",
                RelativePaths = new[] { "enshrouded_server.json" }
            };
            FieldSpec F(string key, string label, FieldKind kind, string group, string desc, double min = 0, double max = 0, double step = 0.05)
                => new FieldSpec(key, label, kind, group, desc) { Min = min, Max = max, Step = step };
            FieldSpec E(string key, string label, string group, string desc, params string[] values)
                => new FieldSpec(key, label, FieldKind.Enum, group, desc) { EnumValues = values };
            const string G = "gameSettings.";

            // — Server —
            s.Fields.Add(E("gameSettingsPreset", "Difficulty preset", "Server", "Default / Relaxed / Hard / Survival use the game's own values. The gameplay settings below only apply with Custom.", "Default", "Relaxed", "Hard", "Survival", "Custom"));
            s.Fields.Add(F("useAlternativeP2P", "Alternative P2P connection", FieldKind.Bool, "Network", "Alternative connection method. Leave off unless players cannot join."));
            // — Communication —
            s.Fields.Add(F("enableTextChat", "Text chat", FieldKind.Bool, "Communication", "Allow the in-game text chat."));
            s.Fields.Add(F("enableVoiceChat", "Voice chat", FieldKind.Bool, "Communication", "Allow the in-game voice chat."));
            s.Fields.Add(E("voiceChatMode", "Voice chat mode", "Communication", "Proximity = heard by nearby players only; Global = heard by everyone.", "Proximity", "Global"));
            // — Player —
            s.Fields.Add(F(G + "playerHealthFactor", "Player health", FieldKind.Float, "Player", "Player health multiplier.", 0.25, 4));
            s.Fields.Add(F(G + "playerManaFactor", "Player mana", FieldKind.Float, "Player", "Player mana multiplier.", 0.25, 4));
            s.Fields.Add(F(G + "playerStaminaFactor", "Player stamina", FieldKind.Float, "Player", "Player stamina multiplier.", 0.25, 4));
            s.Fields.Add(F(G + "playerBodyHeatFactor", "Body heat", FieldKind.Float, "Player", "How fast cold affects the player (higher = more resistant).", 0.5, 2));
            s.Fields.Add(F(G + "playerDivingTimeFactor", "Diving time", FieldKind.Float, "Player", "How long players can stay under water.", 0.5, 2));
            // — Survival —
            s.Fields.Add(F(G + "enableDurability", "Equipment durability", FieldKind.Bool, "Survival", "Weapons and tools wear out."));
            s.Fields.Add(F(G + "enableStarvingDebuff", "Starvation debuff", FieldKind.Bool, "Survival", "Players get a debuff when they go without food."));
            s.Fields.Add(F(G + "fromHungerToStarving", "Hunger to starving (min)", FieldKind.Float, "Survival", "Minutes between being hungry and starving.", 5, 20, 1));
            s.Fields.Add(F(G + "foodBuffDurationFactor", "Food buff duration", FieldKind.Float, "Survival", "How long food buffs last.", 0.5, 2));
            s.Fields.Add(F(G + "shroudTimeFactor", "Time in the Shroud", FieldKind.Float, "Survival", "How long players can stay in the Shroud.", 0.5, 2));
            s.Fields.Add(E(G + "tombstoneMode", "On death", "Survival", "AddBackpackMaterials = materials only drop; Everything = the whole inventory drops; NoTombstone = keep everything.", "AddBackpackMaterials", "Everything", "NoTombstone"));
            s.Fields.Add(F(G + "enableGliderTurbulences", "Glider turbulence", FieldKind.Bool, "Survival", "The glider wobbles in turbulence."));
            // — World —
            s.Fields.Add(F(G + "dayTimeDuration", "Day length (min)", FieldKind.Float, "World", "Length of the day, in minutes.", 2, 60, 1));
            s.Fields.Add(F(G + "nightTimeDuration", "Night length (min)", FieldKind.Float, "World", "Length of the night, in minutes.", 2, 60, 1));
            s.Fields.Add(E(G + "weatherFrequency", "Weather", "World", "How often weather events happen.", "Disabled", "Rare", "Normal", "Often"));
            s.Fields.Add(E(G + "fishingDifficulty", "Fishing difficulty", "World", "Difficulty of the fishing mini-game.", "VeryEasy", "Easy", "Normal", "Hard", "VeryHard"));
            s.Fields.Add(E(G + "curseModifier", "Shroud curse", "World", "Chance of getting cursed by the Shroud.", "Easy", "Normal", "Hard"));
            // — Resources —
            s.Fields.Add(F(G + "miningDamageFactor", "Mining speed", FieldKind.Float, "Resources", "Damage dealt to resource nodes.", 0.5, 2));
            s.Fields.Add(F(G + "resourceDropStackAmountFactor", "Resource drops", FieldKind.Float, "Resources", "Quantity of resources gathered.", 0.25, 2));
            s.Fields.Add(F(G + "plantGrowthSpeedFactor", "Plant growth", FieldKind.Float, "Resources", "Speed at which plants grow.", 0.25, 2));
            s.Fields.Add(F(G + "factoryProductionSpeedFactor", "Production speed", FieldKind.Float, "Resources", "Speed of crafting stations and production.", 0.25, 2));
            // — Progression —
            s.Fields.Add(F(G + "experienceCombatFactor", "Combat XP", FieldKind.Float, "Progression", "Experience from combat.", 0.25, 2));
            s.Fields.Add(F(G + "experienceMiningFactor", "Mining XP", FieldKind.Float, "Progression", "Experience from mining.", 0, 2));
            s.Fields.Add(F(G + "experienceExplorationQuestsFactor", "Exploration & quests XP", FieldKind.Float, "Progression", "Experience from exploration and quests.", 0.25, 2));
            s.Fields.Add(F(G + "perkCostFactor", "Perk cost", FieldKind.Float, "Progression", "Price of perk upgrades.", 0.25, 2));
            s.Fields.Add(F(G + "perkUpgradeRecyclingFactor", "Perk refund", FieldKind.Float, "Progression", "Share refunded when a perk upgrade is recycled.", 0, 1));
            // — Enemies —
            s.Fields.Add(E(G + "randomSpawnerAmount", "Enemy amount", "Enemies", "Number of enemies spawned.", "Few", "Normal", "Many", "Extreme"));
            s.Fields.Add(E(G + "aggroPoolAmount", "Simultaneous attackers", "Enemies", "How many enemies can attack at the same time.", "Few", "Normal", "Many", "Extreme"));
            s.Fields.Add(F(G + "enemyDamageFactor", "Enemy damage", FieldKind.Float, "Enemies", "Damage dealt by enemies (bosses excluded).", 0.25, 5));
            s.Fields.Add(F(G + "enemyHealthFactor", "Enemy health", FieldKind.Float, "Enemies", "Enemy health (bosses excluded).", 0.25, 4));
            s.Fields.Add(F(G + "enemyStaminaFactor", "Enemy stamina", FieldKind.Float, "Enemies", "Enemy stamina: how fast they get staggered.", 0.5, 2));
            s.Fields.Add(F(G + "enemyPerceptionRangeFactor", "Enemy perception", FieldKind.Float, "Enemies", "Distance at which enemies notice players.", 0.5, 2));
            s.Fields.Add(F(G + "threatBonus", "Enemy aggressiveness", FieldKind.Float, "Enemies", "How aggressive enemies are.", 0.25, 4));
            s.Fields.Add(F(G + "bossDamageFactor", "Boss damage", FieldKind.Float, "Enemies", "Damage dealt by bosses.", 0.2, 5));
            s.Fields.Add(F(G + "bossHealthFactor", "Boss health", FieldKind.Float, "Enemies", "Boss health.", 0.2, 5));
            s.Fields.Add(F(G + "pacifyAllEnemies", "Peaceful enemies", FieldKind.Bool, "Enemies", "Enemies never attack first."));
            s.Fields.Add(E(G + "tamingStartleRepercussion", "Startled taming", "Enemies", "What happens to taming progress when the animal is startled.", "KeepProgress", "LoseSomeProgress", "LoseAllProgress"));
            // — User groups — (the game rejects duplicate passwords and more than one group without one)
            foreach (string g in EnshroudedGroups)
            {
                string k = "userGroups." + g + ".";
                s.Fields.Add(F(k + "password", g + ": password", FieldKind.Secret, "User groups", "Password players type to join with this role. Must be different for every group; only one group may have none."));
                s.Fields.Add(F(k + "canKickBan", g + ": kick & ban", FieldKind.Bool, "User groups", "Can kick and ban players."));
                s.Fields.Add(F(k + "canAccessInventories", g + ": open chests", FieldKind.Bool, "User groups", "Can open other players' chests and storage."));
                s.Fields.Add(F(k + "canEditWorld", g + ": edit the world", FieldKind.Bool, "User groups", "Can build and dig outside bases."));
                s.Fields.Add(F(k + "canEditBase", g + ": edit bases", FieldKind.Bool, "User groups", "Can build inside bases (requires 'edit the world')."));
                s.Fields.Add(F(k + "canExtendBase", g + ": extend bases", FieldKind.Bool, "User groups", "Can place and extend Flame Altars."));
                s.Fields.Add(F(k + "reservedSlots", g + ": reserved slots", FieldKind.Int, "User groups", "Slots kept free for this group.", 0, 16, 1));
            }
            return s;
        }

        private static GameConfigSchema Minecraft()
        {
            var s = new GameConfigSchema
            {
                GameMatch = "Minecraft",
                Model = "universal",
                BoolTrue = "true",
                BoolFalse = "false",
                RelativePaths = new[] { "server.properties" }
            };
            // — Server —
            s.Fields.Add(new FieldSpec("motd", "Welcome message (MOTD)", FieldKind.Text, "Server"));
            s.Fields.Add(new FieldSpec("max-players", "Max players", FieldKind.Int, "Server") { Min = 1, Max = 100, Step = 1 });
            s.Fields.Add(new FieldSpec("server-port", "Port", FieldKind.Int, "Server") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("server-ip", "Server IP", FieldKind.Text, "Server", "Empty = all interfaces."));
            s.Fields.Add(new FieldSpec("white-list", "Whitelist", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("enforce-whitelist", "Enforce whitelist", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("online-mode", "Online mode (Mojang auth)", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("player-idle-timeout", "Idle kick (min)", FieldKind.Int, "Server", "0 = never.") { Min = 0, Max = 60, Step = 1 });
            // — World —
            s.Fields.Add(new FieldSpec("level-name", "World name", FieldKind.Text, "World"));
            s.Fields.Add(new FieldSpec("level-seed", "Seed", FieldKind.Text, "World"));
            s.Fields.Add(new FieldSpec("level-type", "World type", FieldKind.Enum, "World") { EnumValues = new[] { "minecraft:normal", "minecraft:flat", "minecraft:large_biomes", "minecraft:amplified", "default", "flat", "large_biomes", "amplified" } });
            s.Fields.Add(new FieldSpec("generate-structures", "Generate structures", FieldKind.Bool, "World"));
            s.Fields.Add(new FieldSpec("spawn-protection", "Spawn protection radius", FieldKind.Int, "World") { Min = 0, Max = 64, Step = 1 });
            // — Gameplay —
            s.Fields.Add(new FieldSpec("gamemode", "Game mode", FieldKind.Enum, "Gameplay") { EnumValues = new[] { "survival", "creative", "adventure", "spectator" } });
            s.Fields.Add(new FieldSpec("difficulty", "Difficulty", FieldKind.Enum, "Gameplay") { EnumValues = new[] { "peaceful", "easy", "normal", "hard" } });
            s.Fields.Add(new FieldSpec("hardcore", "Hardcore", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("pvp", "PvP", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("force-gamemode", "Force game mode", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("allow-flight", "Allow flight", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("allow-nether", "Allow the Nether", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("spawn-monsters", "Spawn monsters", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("spawn-animals", "Spawn animals", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("spawn-npcs", "Spawn NPCs", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("enable-command-block", "Command blocks", FieldKind.Bool, "Gameplay"));
            // — Network / RCON —
            s.Fields.Add(new FieldSpec("enable-rcon", "RCON enabled", FieldKind.Bool, "Network & RCON"));
            s.Fields.Add(new FieldSpec("rcon.port", "RCON port", FieldKind.Int, "Network & RCON") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("rcon.password", "RCON password", FieldKind.Secret, "Network & RCON"));
            s.Fields.Add(new FieldSpec("enable-query", "Query enabled", FieldKind.Bool, "Network & RCON"));
            s.Fields.Add(new FieldSpec("query.port", "Query port", FieldKind.Int, "Network & RCON") { Min = 1, Max = 65535, Step = 1 });
            // — Advanced —
            s.Fields.Add(new FieldSpec("view-distance", "View distance", FieldKind.Int, "Advanced") { Min = 3, Max = 32, Step = 1 });
            s.Fields.Add(new FieldSpec("simulation-distance", "Simulation distance", FieldKind.Int, "Advanced") { Min = 3, Max = 32, Step = 1 });
            s.Fields.Add(new FieldSpec("op-permission-level", "OP permission level", FieldKind.Int, "Advanced") { Min = 0, Max = 4, Step = 1 });
            s.Fields.Add(new FieldSpec("resource-pack", "Resource pack (URL)", FieldKind.Text, "Advanced"));
            return s;
        }

        // ARK: bools as True/False (UE). Server settings = GameUserSettings.ini; advanced multipliers = Game.ini.
        private static GameConfigSchema ArkGameUserSettings()
        {
            var s = new GameConfigSchema
            {
                GameMatch = "ARK",
                Label = "Server settings (GameUserSettings.ini)",
                Model = "universal",
                BoolTrue = "True",
                BoolFalse = "False",
                RelativePaths = new[] { @"ShooterGame\Saved\Config\WindowsServer\GameUserSettings.ini" }
            };
            // — Server —
            s.Fields.Add(new FieldSpec("SessionName", "Server name", FieldKind.Text, "Server"));
            s.Fields.Add(new FieldSpec("ServerPassword", "Password", FieldKind.Secret, "Server", "Empty = open server."));
            s.Fields.Add(new FieldSpec("ServerAdminPassword", "Admin password", FieldKind.Secret, "Server", "Required for RCON/admin."));
            s.Fields.Add(new FieldSpec("SpectatorPassword", "Spectator password", FieldKind.Secret, "Server"));
            s.Fields.Add(new FieldSpec("MaxPlayers", "Max players", FieldKind.Int, "Server") { Min = 1, Max = 127, Step = 1 });
            s.Fields.Add(new FieldSpec("RCONEnabled", "RCON enabled", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("RCONPort", "RCON port", FieldKind.Int, "Server") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("ServerPVE", "PvE mode (no PvP)", FieldKind.Bool, "Server"));
            s.Fields.Add(new FieldSpec("ServerHardcore", "Hardcore", FieldKind.Bool, "Server"));
            // — Difficulty / progression —
            s.Fields.Add(new FieldSpec("DifficultyOffset", "Difficulty offset", FieldKind.Float, "Difficulty", "0 to 1.") { Min = 0, Max = 1, Step = 0.01 });
            s.Fields.Add(new FieldSpec("OverrideOfficialDifficulty", "Official difficulty (max level)", FieldKind.Float, "Difficulty", "5.0 = level 150 creatures.") { Min = 1, Max = 10, Step = 0.5 });
            s.Fields.Add(new FieldSpec("XPMultiplier", "XP multiplier", FieldKind.Float, "Difficulty") { Min = 0.1, Max = 50, Step = 0.5 });
            s.Fields.Add(new FieldSpec("TamingSpeedMultiplier", "Taming speed", FieldKind.Float, "Difficulty") { Min = 0.1, Max = 50, Step = 0.5 });
            s.Fields.Add(new FieldSpec("HarvestAmountMultiplier", "Harvest amount", FieldKind.Float, "Difficulty") { Min = 0.1, Max = 50, Step = 0.5 });
            s.Fields.Add(new FieldSpec("HarvestHealthMultiplier", "Resource health", FieldKind.Float, "Difficulty") { Min = 0.1, Max = 10, Step = 0.5 });
            s.Fields.Add(new FieldSpec("ResourcesRespawnPeriodMultiplier", "Resource respawn delay", FieldKind.Float, "Difficulty") { Min = 0.1, Max = 5, Step = 0.1 });
            // — Day/night cycle —
            s.Fields.Add(new FieldSpec("DayCycleSpeedScale", "Cycle speed", FieldKind.Float, "Time") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("DayTimeSpeedScale", "Daytime speed", FieldKind.Float, "Time") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("NightTimeSpeedScale", "Nighttime speed", FieldKind.Float, "Time") { Min = 0.1, Max = 5, Step = 0.1 });
            // — Combat (multipliers) —
            s.Fields.Add(new FieldSpec("PlayerDamageMultiplier", "Player damage", FieldKind.Float, "Combat") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PlayerResistanceMultiplier", "Player resistance", FieldKind.Float, "Combat") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("DinoDamageMultiplier", "Wild dino damage", FieldKind.Float, "Combat") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("TamedDinoDamageMultiplier", "Tamed dino damage", FieldKind.Float, "Combat") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("TamedDinoResistanceMultiplier", "Tamed dino resistance", FieldKind.Float, "Combat") { Min = 0.1, Max = 10, Step = 0.1 });
            // — Structures —
            s.Fields.Add(new FieldSpec("StructureDamageMultiplier", "Structure damage", FieldKind.Float, "Structures") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("StructureResistanceMultiplier", "Structure resistance", FieldKind.Float, "Structures") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("TheMaxStructuresInRange", "Max structures (area)", FieldKind.Int, "Structures") { Min = 1000, Max = 30000, Step = 500 });
            s.Fields.Add(new FieldSpec("DisableStructureDecayPvE", "Disable decay (PvE)", FieldKind.Bool, "Structures"));
            // — Survival (needs) —
            s.Fields.Add(new FieldSpec("PlayerCharacterFoodDrainMultiplier", "Player hunger", FieldKind.Float, "Survival") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PlayerCharacterWaterDrainMultiplier", "Player thirst", FieldKind.Float, "Survival") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PlayerCharacterStaminaDrainMultiplier", "Player stamina", FieldKind.Float, "Survival") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("DinoCharacterFoodDrainMultiplier", "Dino hunger", FieldKind.Float, "Survival") { Min = 0.1, Max = 5, Step = 0.1 });
            // — Game options —
            s.Fields.Add(new FieldSpec("AllowThirdPersonPlayer", "Third-person view", FieldKind.Bool, "Options"));
            s.Fields.Add(new FieldSpec("ShowMapPlayerLocation", "Player location on map", FieldKind.Bool, "Options"));
            s.Fields.Add(new FieldSpec("ServerCrosshair", "Aiming crosshair", FieldKind.Bool, "Options"));
            s.Fields.Add(new FieldSpec("EnablePVPGamma", "Gamma allowed (PvP)", FieldKind.Bool, "Options"));
            s.Fields.Add(new FieldSpec("globalVoiceChat", "Global voice chat", FieldKind.Bool, "Options"));
            s.Fields.Add(new FieldSpec("AllowFlyerCarryPvE", "Flyers can carry (PvE)", FieldKind.Bool, "Options"));
            s.Fields.Add(new FieldSpec("KickIdlePlayersPeriod", "Idle kick (s)", FieldKind.Int, "Options", "0 = never.") { Min = 0, Max = 7200, Step = 60 });
            return s;
        }

        private static GameConfigSchema ArkGame()
        {
            var s = new GameConfigSchema
            {
                GameMatch = "ARK",
                Label = "Advanced multipliers (Game.ini)",
                Model = "universal",
                BoolTrue = "True",
                BoolFalse = "False",
                RelativePaths = new[] { @"ShooterGame\Saved\Config\WindowsServer\Game.ini" }
            };
            // — Breeding —
            s.Fields.Add(new FieldSpec("MatingIntervalMultiplier", "Mating interval", FieldKind.Float, "Breeding", "<1 = faster.") { Min = 0.01, Max = 5, Step = 0.05 });
            s.Fields.Add(new FieldSpec("EggHatchSpeedMultiplier", "Hatch speed", FieldKind.Float, "Breeding") { Min = 0.1, Max = 50, Step = 0.5 });
            s.Fields.Add(new FieldSpec("BabyMatureSpeedMultiplier", "Baby maturation speed", FieldKind.Float, "Breeding") { Min = 0.1, Max = 50, Step = 0.5 });
            s.Fields.Add(new FieldSpec("BabyImprintingStatScaleMultiplier", "Imprinting bonus", FieldKind.Float, "Breeding") { Min = 0, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("BabyFoodConsumptionSpeedMultiplier", "Baby food consumption", FieldKind.Float, "Breeding") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("BabyCuddleIntervalMultiplier", "Cuddle interval", FieldKind.Float, "Breeding") { Min = 0.1, Max = 5, Step = 0.1 });
            // — Tribes / rules —
            s.Fields.Add(new FieldSpec("MaxNumberOfPlayersInTribe", "Max players per tribe", FieldKind.Int, "Tribes", "0 = unlimited.") { Min = 0, Max = 50, Step = 1 });
            s.Fields.Add(new FieldSpec("bDisableFriendlyFire", "Disable friendly fire", FieldKind.Bool, "Tribes"));
            s.Fields.Add(new FieldSpec("bPvEAllowTribeWar", "Tribe wars (PvE)", FieldKind.Bool, "Tribes"));
            s.Fields.Add(new FieldSpec("bUseSingleplayerSettings", "Singleplayer settings (boost)", FieldKind.Bool, "Tribes"));
            // — Harvest / crafting —
            s.Fields.Add(new FieldSpec("CropGrowthSpeedMultiplier", "Crop growth speed", FieldKind.Float, "Harvest") { Min = 0.1, Max = 10, Step = 0.5 });
            s.Fields.Add(new FieldSpec("HarvestAmountMultiplier", "Harvest amount (Game.ini)", FieldKind.Float, "Harvest") { Min = 0.1, Max = 50, Step = 0.5 });
            s.Fields.Add(new FieldSpec("CustomRecipeEffectivenessMultiplier", "Custom recipe effectiveness", FieldKind.Float, "Harvest") { Min = 0.1, Max = 10, Step = 0.1 });
            return s;
        }
    }
}
