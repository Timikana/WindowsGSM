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
            // — Server —
            s.Fields.Add(new FieldSpec("ServerName", "Server name", FieldKind.Text, "Server"));
            s.Fields.Add(new FieldSpec("ServerDescription", "Description", FieldKind.Text, "Server"));
            s.Fields.Add(new FieldSpec("ServerPassword", "Password", FieldKind.Secret, "Server", "Empty = open server."));
            s.Fields.Add(new FieldSpec("Region", "Region", FieldKind.Enum, "Server") { EnumValues = new[] { "NorthAmericaEast", "NorthAmericaWest", "CentralAmerica", "SouthAmerica", "Europe", "Russia", "Asia", "MiddleEast", "Africa", "Oceania" } });
            s.Fields.Add(new FieldSpec("Language", "Language", FieldKind.Text, "Server"));
            s.Fields.Add(new FieldSpec("ServerPort", "Port", FieldKind.Int, "Server") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("ServerVisibility", "Visibility", FieldKind.Int, "Server", "0 = hidden, 1 = friends, 2 = public.") { Min = 0, Max = 2, Step = 1 });
            s.Fields.Add(new FieldSpec("ServerMaxPlayerCount", "Max players", FieldKind.Int, "Server") { Min = 1, Max = 64, Step = 1 });
            // — World —
            s.Fields.Add(new FieldSpec("GameWorld", "World", FieldKind.Text, "World", "RWG or the name of an existing world (Navezgane...)."));
            s.Fields.Add(new FieldSpec("WorldGenSeed", "Seed", FieldKind.Text, "World"));
            s.Fields.Add(new FieldSpec("WorldGenSize", "World size", FieldKind.Int, "World", "Multiple of 2048, between 6144 and 10240.") { Min = 6144, Max = 10240, Step = 2048 });
            s.Fields.Add(new FieldSpec("GameName", "Game name", FieldKind.Text, "World"));
            // — Difficulty —
            s.Fields.Add(new FieldSpec("GameDifficulty", "Difficulty", FieldKind.Int, "Difficulty", "0 = easy ... 5 = hard.") { Min = 0, Max = 5, Step = 1 });
            s.Fields.Add(new FieldSpec("DayNightLength", "Day length (real minutes)", FieldKind.Int, "Difficulty") { Min = 10, Max = 120, Step = 5 });
            s.Fields.Add(new FieldSpec("DeathPenalty", "Death penalty", FieldKind.Int, "Difficulty", "0 = none, 1 = XP, 2 = injured, 3 = permanent death.") { Min = 0, Max = 3, Step = 1 });
            s.Fields.Add(new FieldSpec("DropOnDeath", "Drop on death", FieldKind.Int, "Difficulty", "0 = nothing, 1 = everything, 2 = toolbelt, 3 = backpack, 4 = delete.") { Min = 0, Max = 4, Step = 1 });
            s.Fields.Add(new FieldSpec("XPMultiplier", "XP multiplier (%)", FieldKind.Int, "Difficulty") { Min = 50, Max = 300, Step = 5 });
            s.Fields.Add(new FieldSpec("BlockDamagePlayer", "Player block damage (%)", FieldKind.Int, "Difficulty") { Min = 0, Max = 500, Step = 10 });
            s.Fields.Add(new FieldSpec("LootAbundance", "Loot abundance (%)", FieldKind.Int, "Difficulty") { Min = 25, Max = 600, Step = 25 });
            s.Fields.Add(new FieldSpec("LootRespawnDays", "Loot respawn (days)", FieldKind.Int, "Difficulty") { Min = 0, Max = 30, Step = 1 });
            // — Zombies —
            s.Fields.Add(new FieldSpec("EnemySpawnMode", "Enemy spawning", FieldKind.Bool, "Zombies"));
            s.Fields.Add(new FieldSpec("EnemyDifficulty", "Enemy difficulty", FieldKind.Int, "Zombies", "0 = normal, 1 = feral.") { Min = 0, Max = 1, Step = 1 });
            s.Fields.Add(new FieldSpec("MaxSpawnedZombies", "Max zombies (map)", FieldKind.Int, "Zombies") { Min = 0, Max = 200, Step = 10 });
            s.Fields.Add(new FieldSpec("BloodMoonFrequency", "Blood moon frequency (days)", FieldKind.Int, "Zombies", "0 = never.") { Min = 0, Max = 30, Step = 1 });
            s.Fields.Add(new FieldSpec("BloodMoonEnemyCount", "Zombies per player (blood moon)", FieldKind.Int, "Zombies") { Min = 0, Max = 60, Step = 1 });
            s.Fields.Add(new FieldSpec("ZombieMove", "Zombie speed (day)", FieldKind.Int, "Zombies", "0 = walk ... 4 = nightmare.") { Min = 0, Max = 4, Step = 1 });
            s.Fields.Add(new FieldSpec("ZombieMoveNight", "Zombie speed (night)", FieldKind.Int, "Zombies", "0 = walk ... 4 = nightmare.") { Min = 0, Max = 4, Step = 1 });
            // — Multiplayer —
            s.Fields.Add(new FieldSpec("PlayerKillingMode", "PvP mode", FieldKind.Int, "Multiplayer", "0 = none, 1 = allies, 2 = strangers, 3 = everyone.") { Min = 0, Max = 3, Step = 1 });
            s.Fields.Add(new FieldSpec("LandClaimCount", "Max land claims / player", FieldKind.Int, "Multiplayer") { Min = 0, Max = 10, Step = 1 });
            s.Fields.Add(new FieldSpec("LandClaimSize", "Land claim size (blocks)", FieldKind.Int, "Multiplayer") { Min = 1, Max = 100, Step = 1 });
            // — Advanced —
            s.Fields.Add(new FieldSpec("EACEnabled", "EasyAntiCheat", FieldKind.Bool, "Advanced"));
            s.Fields.Add(new FieldSpec("ServerMaxAllowedViewDistance", "Max view distance", FieldKind.Int, "Advanced") { Min = 6, Max = 12, Step = 1 });
            s.Fields.Add(new FieldSpec("PersistentPlayerProfiles", "Persistent profiles", FieldKind.Bool, "Advanced"));
            s.Fields.Add(new FieldSpec("WebDashboardEnabled", "Web dashboard", FieldKind.Bool, "Advanced"));
            s.Fields.Add(new FieldSpec("WebDashboardPort", "Web dashboard port", FieldKind.Int, "Advanced") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("TelnetEnabled", "Telnet enabled", FieldKind.Bool, "Advanced"));
            s.Fields.Add(new FieldSpec("TelnetPort", "Telnet port", FieldKind.Int, "Advanced") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("TelnetPassword", "Telnet password", FieldKind.Secret, "Advanced"));
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
