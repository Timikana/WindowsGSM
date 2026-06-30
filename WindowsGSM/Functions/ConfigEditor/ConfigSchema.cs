using System;
using System.Collections.Generic;

namespace WindowsGSM.Functions.ConfigEditor
{
    /// <summary>Type d'un réglage → pilote le rendu UI (toggle, slider, combo, champ texte, secret masqué).</summary>
    public enum FieldKind { Bool, Int, Float, Enum, Text, Secret }

    /// <summary>Spécification curatée d'un réglage : libellé humain, description, type, bornes/valeurs.</summary>
    public class FieldSpec
    {
        public string Key;            // clé exacte dans le fichier
        public string Label;          // libellé lisible (FR)
        public string Description;    // aide courte
        public FieldKind Kind = FieldKind.Text;
        public string Group = "Général";
        public double Min, Max, Step = 1; // pour Int/Float
        public string[] EnumValues;       // pour Enum

        public FieldSpec() { }
        public FieldSpec(string key, string label, FieldKind kind, string group, string desc = "")
        { Key = key; Label = label; Kind = kind; Group = group; Description = desc; }
    }

    /// <summary>
    /// Schéma curaté d'un jeu : où trouver le fichier (relatif à serverfiles), quel modèle l'ouvre,
    /// et la liste des réglages soignés. Les clés non listées restent éditables en mode brut (fallback).
    /// </summary>
    public class GameConfigSchema
    {
        public string GameMatch;                 // match sur GameServer.FullName (StartsWith)
        public string Label;                     // libellé du fichier dans le sélecteur (optionnel)
        public string[] RelativePaths;           // chemins candidats du fichier sous serverfiles
        public string Model = "universal";        // "universal" (ConfigFile) | "palworld" (PalworldConfig)
        public string BoolTrue = "True";          // littéral écrit pour un booléen vrai (Palworld=True, MC/7DtD=true)
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

    /// <summary>Registre des schémas par jeu (hybride : curaté + fallback brut pour le reste).</summary>
    public static class GameSchemas
    {
        private static readonly List<GameConfigSchema> _all = new List<GameConfigSchema>
        {
            Palworld(), SevenDaysToDie(), Minecraft(), ArkGameUserSettings(), ArkGame()
        };

        /// <summary>Retourne le 1er schéma curaté correspondant au jeu, ou null (→ l'UI tombe en mode brut).</summary>
        public static GameConfigSchema For(string gameFullName)
        {
            if (string.IsNullOrEmpty(gameFullName)) { return null; }
            foreach (var s in _all)
            {
                if (gameFullName.StartsWith(s.GameMatch, StringComparison.OrdinalIgnoreCase)) { return s; }
            }
            return null;
        }

        /// <summary>TOUS les schémas curatés du jeu (un jeu peut avoir plusieurs fichiers : ARK = GameUserSettings.ini + Game.ini).</summary>
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
            // — Serveur —
            s.Fields.Add(new FieldSpec("ServerName", "Nom du serveur", FieldKind.Text, "Serveur"));
            s.Fields.Add(new FieldSpec("ServerDescription", "Description", FieldKind.Text, "Serveur"));
            s.Fields.Add(new FieldSpec("ServerPassword", "Mot de passe serveur", FieldKind.Secret, "Serveur", "Vide = serveur public."));
            s.Fields.Add(new FieldSpec("AdminPassword", "Mot de passe admin", FieldKind.Secret, "Serveur", "Requis pour RCON/REST."));
            s.Fields.Add(new FieldSpec("ServerPlayerMaxNum", "Joueurs max", FieldKind.Int, "Serveur") { Min = 1, Max = 32, Step = 1 });
            s.Fields.Add(new FieldSpec("PublicPort", "Port public", FieldKind.Int, "Serveur") { Min = 1, Max = 65535, Step = 1 });
            // — Gameplay —
            s.Fields.Add(new FieldSpec("Difficulty", "Difficulté", FieldKind.Enum, "Gameplay") { EnumValues = new[] { "None", "Casual", "Normal", "Hard" } });
            s.Fields.Add(new FieldSpec("DeathPenalty", "Pénalité de mort", FieldKind.Enum, "Gameplay") { EnumValues = new[] { "None", "Item", "ItemAndEquipment", "All" } });
            s.Fields.Add(new FieldSpec("bIsPvP", "PvP activé", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("bEnablePlayerToPlayerDamage", "Dégâts entre joueurs", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("bEnableFriendlyFire", "Tir allié", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("bHardcore", "Hardcore", FieldKind.Bool, "Gameplay"));
            // — Taux —
            s.Fields.Add(new FieldSpec("DayTimeSpeedRate", "Vitesse du jour", FieldKind.Float, "Taux") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("NightTimeSpeedRate", "Vitesse de la nuit", FieldKind.Float, "Taux") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("ExpRate", "Taux d'XP", FieldKind.Float, "Taux") { Min = 0.1, Max = 20, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PalCaptureRate", "Taux de capture", FieldKind.Float, "Taux") { Min = 0.5, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PalSpawnNumRate", "Densité de Pals", FieldKind.Float, "Taux") { Min = 0.1, Max = 3, Step = 0.1 });
            s.Fields.Add(new FieldSpec("CollectionDropRate", "Taux de récolte", FieldKind.Float, "Taux") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("EnemyDropItemRate", "Butin ennemis", FieldKind.Float, "Taux") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("WorkSpeedRate", "Vitesse de travail", FieldKind.Float, "Taux") { Min = 0.1, Max = 5, Step = 0.1 });
            // — Combat —
            s.Fields.Add(new FieldSpec("PalDamageRateAttack", "Dégâts Pal (attaque)", FieldKind.Float, "Combat") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PalDamageRateDefense", "Dégâts Pal (défense)", FieldKind.Float, "Combat") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PlayerDamageRateAttack", "Dégâts joueur (attaque)", FieldKind.Float, "Combat") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PlayerDamageRateDefense", "Dégâts joueur (défense)", FieldKind.Float, "Combat") { Min = 0.1, Max = 5, Step = 0.1 });
            // — Guilde / Base —
            s.Fields.Add(new FieldSpec("GuildPlayerMaxNum", "Joueurs max / guilde", FieldKind.Int, "Guilde & base") { Min = 1, Max = 100, Step = 1 });
            s.Fields.Add(new FieldSpec("CoopPlayerMaxNum", "Joueurs coop max", FieldKind.Int, "Guilde & base") { Min = 1, Max = 4, Step = 1 });
            s.Fields.Add(new FieldSpec("BaseCampMaxNum", "Camps de base max", FieldKind.Int, "Guilde & base") { Min = 1, Max = 256, Step = 1 });
            s.Fields.Add(new FieldSpec("PalEggDefaultHatchingTime", "Éclosion œuf (h)", FieldKind.Float, "Guilde & base") { Min = 0, Max = 240, Step = 1 });
            // — Avancé —
            s.Fields.Add(new FieldSpec("AutoSaveSpan", "Sauvegarde auto (s)", FieldKind.Float, "Avancé") { Min = 30, Max = 3600, Step = 10 });
            s.Fields.Add(new FieldSpec("RESTAPIEnabled", "API REST activée", FieldKind.Bool, "Avancé"));
            s.Fields.Add(new FieldSpec("RESTAPIPort", "Port API REST", FieldKind.Int, "Avancé") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("RCONEnabled", "RCON activé", FieldKind.Bool, "Avancé"));
            s.Fields.Add(new FieldSpec("RCONPort", "Port RCON", FieldKind.Int, "Avancé") { Min = 1, Max = 65535, Step = 1 });
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
            // — Serveur —
            s.Fields.Add(new FieldSpec("ServerName", "Nom du serveur", FieldKind.Text, "Serveur"));
            s.Fields.Add(new FieldSpec("ServerDescription", "Description", FieldKind.Text, "Serveur"));
            s.Fields.Add(new FieldSpec("ServerPassword", "Mot de passe", FieldKind.Secret, "Serveur", "Vide = serveur ouvert."));
            s.Fields.Add(new FieldSpec("Region", "Région", FieldKind.Enum, "Serveur") { EnumValues = new[] { "NorthAmericaEast", "NorthAmericaWest", "CentralAmerica", "SouthAmerica", "Europe", "Russia", "Asia", "MiddleEast", "Africa", "Oceania" } });
            s.Fields.Add(new FieldSpec("Language", "Langue", FieldKind.Text, "Serveur"));
            s.Fields.Add(new FieldSpec("ServerPort", "Port", FieldKind.Int, "Serveur") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("ServerVisibility", "Visibilité", FieldKind.Int, "Serveur", "0 = caché, 1 = amis, 2 = public.") { Min = 0, Max = 2, Step = 1 });
            s.Fields.Add(new FieldSpec("ServerMaxPlayerCount", "Joueurs max", FieldKind.Int, "Serveur") { Min = 1, Max = 64, Step = 1 });
            // — Monde —
            s.Fields.Add(new FieldSpec("GameWorld", "Monde", FieldKind.Text, "Monde", "RWG ou nom d'un monde existant (Navezgane…)."));
            s.Fields.Add(new FieldSpec("WorldGenSeed", "Graine (seed)", FieldKind.Text, "Monde"));
            s.Fields.Add(new FieldSpec("WorldGenSize", "Taille du monde", FieldKind.Int, "Monde", "Multiple de 2048, entre 6144 et 10240.") { Min = 6144, Max = 10240, Step = 2048 });
            s.Fields.Add(new FieldSpec("GameName", "Nom de la partie", FieldKind.Text, "Monde"));
            // — Difficulté —
            s.Fields.Add(new FieldSpec("GameDifficulty", "Difficulté", FieldKind.Int, "Difficulté", "0 = facile … 5 = difficile.") { Min = 0, Max = 5, Step = 1 });
            s.Fields.Add(new FieldSpec("DayNightLength", "Durée d'un jour (min réelles)", FieldKind.Int, "Difficulté") { Min = 10, Max = 120, Step = 5 });
            s.Fields.Add(new FieldSpec("DeathPenalty", "Pénalité de mort", FieldKind.Int, "Difficulté", "0 = aucune, 1 = XP, 2 = blessé, 3 = mort permanente.") { Min = 0, Max = 3, Step = 1 });
            s.Fields.Add(new FieldSpec("DropOnDeath", "Drop à la mort", FieldKind.Int, "Difficulté", "0 = rien, 1 = tout, 2 = ceinture, 3 = sac, 4 = supprime.") { Min = 0, Max = 4, Step = 1 });
            s.Fields.Add(new FieldSpec("XPMultiplier", "Multiplicateur d'XP (%)", FieldKind.Int, "Difficulté") { Min = 50, Max = 300, Step = 5 });
            s.Fields.Add(new FieldSpec("BlockDamagePlayer", "Dégâts aux blocs joueur (%)", FieldKind.Int, "Difficulté") { Min = 0, Max = 500, Step = 10 });
            s.Fields.Add(new FieldSpec("LootAbundance", "Abondance du loot (%)", FieldKind.Int, "Difficulté") { Min = 25, Max = 600, Step = 25 });
            s.Fields.Add(new FieldSpec("LootRespawnDays", "Réapparition du loot (jours)", FieldKind.Int, "Difficulté") { Min = 0, Max = 30, Step = 1 });
            // — Zombies —
            s.Fields.Add(new FieldSpec("EnemySpawnMode", "Apparition des ennemis", FieldKind.Bool, "Zombies"));
            s.Fields.Add(new FieldSpec("EnemyDifficulty", "Difficulté ennemis", FieldKind.Int, "Zombies", "0 = normal, 1 = féral.") { Min = 0, Max = 1, Step = 1 });
            s.Fields.Add(new FieldSpec("MaxSpawnedZombies", "Zombies max (carte)", FieldKind.Int, "Zombies") { Min = 0, Max = 200, Step = 10 });
            s.Fields.Add(new FieldSpec("BloodMoonFrequency", "Fréquence lune de sang (jours)", FieldKind.Int, "Zombies", "0 = jamais.") { Min = 0, Max = 30, Step = 1 });
            s.Fields.Add(new FieldSpec("BloodMoonEnemyCount", "Zombies par joueur (lune de sang)", FieldKind.Int, "Zombies") { Min = 0, Max = 60, Step = 1 });
            s.Fields.Add(new FieldSpec("ZombieMove", "Vitesse zombies (jour)", FieldKind.Int, "Zombies", "0 = marche … 4 = cauchemar.") { Min = 0, Max = 4, Step = 1 });
            s.Fields.Add(new FieldSpec("ZombieMoveNight", "Vitesse zombies (nuit)", FieldKind.Int, "Zombies", "0 = marche … 4 = cauchemar.") { Min = 0, Max = 4, Step = 1 });
            // — Multijoueur —
            s.Fields.Add(new FieldSpec("PlayerKillingMode", "Mode PvP", FieldKind.Int, "Multijoueur", "0 = aucun, 1 = alliés, 2 = inconnus, 3 = tous.") { Min = 0, Max = 3, Step = 1 });
            s.Fields.Add(new FieldSpec("LandClaimCount", "Revendications max / joueur", FieldKind.Int, "Multijoueur") { Min = 0, Max = 10, Step = 1 });
            s.Fields.Add(new FieldSpec("LandClaimSize", "Taille de revendication (blocs)", FieldKind.Int, "Multijoueur") { Min = 1, Max = 100, Step = 1 });
            // — Avancé —
            s.Fields.Add(new FieldSpec("EACEnabled", "EasyAntiCheat", FieldKind.Bool, "Avancé"));
            s.Fields.Add(new FieldSpec("ServerMaxAllowedViewDistance", "Distance de vue max", FieldKind.Int, "Avancé") { Min = 6, Max = 12, Step = 1 });
            s.Fields.Add(new FieldSpec("PersistentPlayerProfiles", "Profils persistants", FieldKind.Bool, "Avancé"));
            s.Fields.Add(new FieldSpec("WebDashboardEnabled", "Tableau de bord web", FieldKind.Bool, "Avancé"));
            s.Fields.Add(new FieldSpec("WebDashboardPort", "Port du dashboard web", FieldKind.Int, "Avancé") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("TelnetEnabled", "Telnet activé", FieldKind.Bool, "Avancé"));
            s.Fields.Add(new FieldSpec("TelnetPort", "Port Telnet", FieldKind.Int, "Avancé") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("TelnetPassword", "Mot de passe Telnet", FieldKind.Secret, "Avancé"));
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
            // — Serveur —
            s.Fields.Add(new FieldSpec("motd", "Message d'accueil (MOTD)", FieldKind.Text, "Serveur"));
            s.Fields.Add(new FieldSpec("max-players", "Joueurs max", FieldKind.Int, "Serveur") { Min = 1, Max = 100, Step = 1 });
            s.Fields.Add(new FieldSpec("server-port", "Port", FieldKind.Int, "Serveur") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("server-ip", "IP du serveur", FieldKind.Text, "Serveur", "Vide = toutes les interfaces."));
            s.Fields.Add(new FieldSpec("white-list", "Liste blanche", FieldKind.Bool, "Serveur"));
            s.Fields.Add(new FieldSpec("enforce-whitelist", "Forcer la liste blanche", FieldKind.Bool, "Serveur"));
            s.Fields.Add(new FieldSpec("online-mode", "Mode en ligne (auth Mojang)", FieldKind.Bool, "Serveur"));
            s.Fields.Add(new FieldSpec("player-idle-timeout", "Expulsion inactivité (min)", FieldKind.Int, "Serveur", "0 = jamais.") { Min = 0, Max = 60, Step = 1 });
            // — Monde —
            s.Fields.Add(new FieldSpec("level-name", "Nom du monde", FieldKind.Text, "Monde"));
            s.Fields.Add(new FieldSpec("level-seed", "Graine (seed)", FieldKind.Text, "Monde"));
            s.Fields.Add(new FieldSpec("level-type", "Type de monde", FieldKind.Enum, "Monde") { EnumValues = new[] { "minecraft:normal", "minecraft:flat", "minecraft:large_biomes", "minecraft:amplified", "default", "flat", "large_biomes", "amplified" } });
            s.Fields.Add(new FieldSpec("generate-structures", "Générer les structures", FieldKind.Bool, "Monde"));
            s.Fields.Add(new FieldSpec("spawn-protection", "Rayon de protection du spawn", FieldKind.Int, "Monde") { Min = 0, Max = 64, Step = 1 });
            // — Gameplay —
            s.Fields.Add(new FieldSpec("gamemode", "Mode de jeu", FieldKind.Enum, "Gameplay") { EnumValues = new[] { "survival", "creative", "adventure", "spectator" } });
            s.Fields.Add(new FieldSpec("difficulty", "Difficulté", FieldKind.Enum, "Gameplay") { EnumValues = new[] { "peaceful", "easy", "normal", "hard" } });
            s.Fields.Add(new FieldSpec("hardcore", "Hardcore", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("pvp", "PvP", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("force-gamemode", "Forcer le mode de jeu", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("allow-flight", "Autoriser le vol", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("allow-nether", "Autoriser le Nether", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("spawn-monsters", "Apparition des monstres", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("spawn-animals", "Apparition des animaux", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("spawn-npcs", "Apparition des PNJ", FieldKind.Bool, "Gameplay"));
            s.Fields.Add(new FieldSpec("enable-command-block", "Blocs de commande", FieldKind.Bool, "Gameplay"));
            // — Réseau / RCON —
            s.Fields.Add(new FieldSpec("enable-rcon", "RCON activé", FieldKind.Bool, "Réseau & RCON"));
            s.Fields.Add(new FieldSpec("rcon.port", "Port RCON", FieldKind.Int, "Réseau & RCON") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("rcon.password", "Mot de passe RCON", FieldKind.Secret, "Réseau & RCON"));
            s.Fields.Add(new FieldSpec("enable-query", "Query activé", FieldKind.Bool, "Réseau & RCON"));
            s.Fields.Add(new FieldSpec("query.port", "Port Query", FieldKind.Int, "Réseau & RCON") { Min = 1, Max = 65535, Step = 1 });
            // — Avancé —
            s.Fields.Add(new FieldSpec("view-distance", "Distance de vue", FieldKind.Int, "Avancé") { Min = 3, Max = 32, Step = 1 });
            s.Fields.Add(new FieldSpec("simulation-distance", "Distance de simulation", FieldKind.Int, "Avancé") { Min = 3, Max = 32, Step = 1 });
            s.Fields.Add(new FieldSpec("op-permission-level", "Niveau de permission OP", FieldKind.Int, "Avancé") { Min = 0, Max = 4, Step = 1 });
            s.Fields.Add(new FieldSpec("resource-pack", "Pack de ressources (URL)", FieldKind.Text, "Avancé"));
            return s;
        }

        // ARK : bools en True/False (UE). Réglages serveur = GameUserSettings.ini ; multiplicateurs avancés = Game.ini.
        private static GameConfigSchema ArkGameUserSettings()
        {
            var s = new GameConfigSchema
            {
                GameMatch = "ARK",
                Label = "Réglages serveur (GameUserSettings.ini)",
                Model = "universal",
                BoolTrue = "True",
                BoolFalse = "False",
                RelativePaths = new[] { @"ShooterGame\Saved\Config\WindowsServer\GameUserSettings.ini" }
            };
            // — Serveur —
            s.Fields.Add(new FieldSpec("SessionName", "Nom du serveur", FieldKind.Text, "Serveur"));
            s.Fields.Add(new FieldSpec("ServerPassword", "Mot de passe", FieldKind.Secret, "Serveur", "Vide = serveur ouvert."));
            s.Fields.Add(new FieldSpec("ServerAdminPassword", "Mot de passe admin", FieldKind.Secret, "Serveur", "Requis pour RCON/admin."));
            s.Fields.Add(new FieldSpec("SpectatorPassword", "Mot de passe spectateur", FieldKind.Secret, "Serveur"));
            s.Fields.Add(new FieldSpec("MaxPlayers", "Joueurs max", FieldKind.Int, "Serveur") { Min = 1, Max = 127, Step = 1 });
            s.Fields.Add(new FieldSpec("RCONEnabled", "RCON activé", FieldKind.Bool, "Serveur"));
            s.Fields.Add(new FieldSpec("RCONPort", "Port RCON", FieldKind.Int, "Serveur") { Min = 1, Max = 65535, Step = 1 });
            s.Fields.Add(new FieldSpec("ServerPVE", "Mode PvE (pas de PvP)", FieldKind.Bool, "Serveur"));
            s.Fields.Add(new FieldSpec("ServerHardcore", "Hardcore", FieldKind.Bool, "Serveur"));
            // — Difficulté / progression —
            s.Fields.Add(new FieldSpec("DifficultyOffset", "Décalage de difficulté", FieldKind.Float, "Difficulté", "0 à 1.") { Min = 0, Max = 1, Step = 0.01 });
            s.Fields.Add(new FieldSpec("OverrideOfficialDifficulty", "Difficulté officielle (niveau max)", FieldKind.Float, "Difficulté", "5.0 = créatures niv. 150.") { Min = 1, Max = 10, Step = 0.5 });
            s.Fields.Add(new FieldSpec("XPMultiplier", "Multiplicateur d'XP", FieldKind.Float, "Difficulté") { Min = 0.1, Max = 50, Step = 0.5 });
            s.Fields.Add(new FieldSpec("TamingSpeedMultiplier", "Vitesse d'apprivoisement", FieldKind.Float, "Difficulté") { Min = 0.1, Max = 50, Step = 0.5 });
            s.Fields.Add(new FieldSpec("HarvestAmountMultiplier", "Quantité de récolte", FieldKind.Float, "Difficulté") { Min = 0.1, Max = 50, Step = 0.5 });
            s.Fields.Add(new FieldSpec("HarvestHealthMultiplier", "Résistance des ressources", FieldKind.Float, "Difficulté") { Min = 0.1, Max = 10, Step = 0.5 });
            s.Fields.Add(new FieldSpec("ResourcesRespawnPeriodMultiplier", "Délai de réapparition ressources", FieldKind.Float, "Difficulté") { Min = 0.1, Max = 5, Step = 0.1 });
            // — Cycle jour/nuit —
            s.Fields.Add(new FieldSpec("DayCycleSpeedScale", "Vitesse du cycle", FieldKind.Float, "Temps") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("DayTimeSpeedScale", "Vitesse du jour", FieldKind.Float, "Temps") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("NightTimeSpeedScale", "Vitesse de la nuit", FieldKind.Float, "Temps") { Min = 0.1, Max = 5, Step = 0.1 });
            // — Combat (multiplicateurs) —
            s.Fields.Add(new FieldSpec("PlayerDamageMultiplier", "Dégâts joueurs", FieldKind.Float, "Combat") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PlayerResistanceMultiplier", "Résistance joueurs", FieldKind.Float, "Combat") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("DinoDamageMultiplier", "Dégâts dinos sauvages", FieldKind.Float, "Combat") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("TamedDinoDamageMultiplier", "Dégâts dinos apprivoisés", FieldKind.Float, "Combat") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("TamedDinoResistanceMultiplier", "Résistance dinos apprivoisés", FieldKind.Float, "Combat") { Min = 0.1, Max = 10, Step = 0.1 });
            // — Structures —
            s.Fields.Add(new FieldSpec("StructureDamageMultiplier", "Dégâts aux structures", FieldKind.Float, "Structures") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("StructureResistanceMultiplier", "Résistance des structures", FieldKind.Float, "Structures") { Min = 0.1, Max = 10, Step = 0.1 });
            s.Fields.Add(new FieldSpec("TheMaxStructuresInRange", "Structures max (zone)", FieldKind.Int, "Structures") { Min = 1000, Max = 30000, Step = 500 });
            s.Fields.Add(new FieldSpec("DisableStructureDecayPvE", "Désactiver dégradation (PvE)", FieldKind.Bool, "Structures"));
            // — Survie (besoins) —
            s.Fields.Add(new FieldSpec("PlayerCharacterFoodDrainMultiplier", "Faim joueurs", FieldKind.Float, "Survie") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PlayerCharacterWaterDrainMultiplier", "Soif joueurs", FieldKind.Float, "Survie") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("PlayerCharacterStaminaDrainMultiplier", "Endurance joueurs", FieldKind.Float, "Survie") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("DinoCharacterFoodDrainMultiplier", "Faim dinos", FieldKind.Float, "Survie") { Min = 0.1, Max = 5, Step = 0.1 });
            // — Options de jeu —
            s.Fields.Add(new FieldSpec("AllowThirdPersonPlayer", "Vue à la 3e personne", FieldKind.Bool, "Options"));
            s.Fields.Add(new FieldSpec("ShowMapPlayerLocation", "Position du joueur sur la carte", FieldKind.Bool, "Options"));
            s.Fields.Add(new FieldSpec("ServerCrosshair", "Réticule de visée", FieldKind.Bool, "Options"));
            s.Fields.Add(new FieldSpec("EnablePVPGamma", "Gamma autorisé (PvP)", FieldKind.Bool, "Options"));
            s.Fields.Add(new FieldSpec("globalVoiceChat", "Chat vocal global", FieldKind.Bool, "Options"));
            s.Fields.Add(new FieldSpec("AllowFlyerCarryPvE", "Volants peuvent porter (PvE)", FieldKind.Bool, "Options"));
            s.Fields.Add(new FieldSpec("KickIdlePlayersPeriod", "Expulsion inactivité (s)", FieldKind.Int, "Options", "0 = jamais.") { Min = 0, Max = 7200, Step = 60 });
            return s;
        }

        private static GameConfigSchema ArkGame()
        {
            var s = new GameConfigSchema
            {
                GameMatch = "ARK",
                Label = "Multiplicateurs avancés (Game.ini)",
                Model = "universal",
                BoolTrue = "True",
                BoolFalse = "False",
                RelativePaths = new[] { @"ShooterGame\Saved\Config\WindowsServer\Game.ini" }
            };
            // — Élevage —
            s.Fields.Add(new FieldSpec("MatingIntervalMultiplier", "Intervalle d'accouplement", FieldKind.Float, "Élevage", "<1 = plus rapide.") { Min = 0.01, Max = 5, Step = 0.05 });
            s.Fields.Add(new FieldSpec("EggHatchSpeedMultiplier", "Vitesse d'éclosion", FieldKind.Float, "Élevage") { Min = 0.1, Max = 50, Step = 0.5 });
            s.Fields.Add(new FieldSpec("BabyMatureSpeedMultiplier", "Vitesse de croissance des bébés", FieldKind.Float, "Élevage") { Min = 0.1, Max = 50, Step = 0.5 });
            s.Fields.Add(new FieldSpec("BabyImprintingStatScaleMultiplier", "Bonus d'empreinte", FieldKind.Float, "Élevage") { Min = 0, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("BabyFoodConsumptionSpeedMultiplier", "Conso. nourriture bébés", FieldKind.Float, "Élevage") { Min = 0.1, Max = 5, Step = 0.1 });
            s.Fields.Add(new FieldSpec("BabyCuddleIntervalMultiplier", "Intervalle de câlin", FieldKind.Float, "Élevage") { Min = 0.1, Max = 5, Step = 0.1 });
            // — Tribus / règles —
            s.Fields.Add(new FieldSpec("MaxNumberOfPlayersInTribe", "Joueurs max par tribu", FieldKind.Int, "Tribus", "0 = illimité.") { Min = 0, Max = 50, Step = 1 });
            s.Fields.Add(new FieldSpec("bDisableFriendlyFire", "Désactiver tir allié", FieldKind.Bool, "Tribus"));
            s.Fields.Add(new FieldSpec("bPvEAllowTribeWar", "Guerres de tribu (PvE)", FieldKind.Bool, "Tribus"));
            s.Fields.Add(new FieldSpec("bUseSingleplayerSettings", "Réglages solo (boost)", FieldKind.Bool, "Tribus"));
            // — Récolte / craft —
            s.Fields.Add(new FieldSpec("CropGrowthSpeedMultiplier", "Vitesse de pousse des cultures", FieldKind.Float, "Récolte") { Min = 0.1, Max = 10, Step = 0.5 });
            s.Fields.Add(new FieldSpec("HarvestAmountMultiplier", "Quantité de récolte (Game.ini)", FieldKind.Float, "Récolte") { Min = 0.1, Max = 50, Step = 0.5 });
            s.Fields.Add(new FieldSpec("CustomRecipeEffectivenessMultiplier", "Efficacité recettes perso", FieldKind.Float, "Récolte") { Min = 0.1, Max = 10, Step = 0.1 });
            return s;
        }
    }
}
