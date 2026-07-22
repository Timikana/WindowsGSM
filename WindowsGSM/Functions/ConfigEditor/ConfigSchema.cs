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
            Palworld(), SevenDaysToDie(), Minecraft(), ArkGameUserSettings(), ArkGame()
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
