using System;
using System.Collections.Generic;

namespace WindowsGSM.Functions.ConfigEditor
{
    /// <summary>
    /// French overlay for the (English) config schema: labels, descriptions and group names.
    /// Applied only when the app language is French; otherwise the schema's English text is used
    /// (so EN/ES/DE fall back cleanly). Keyed by field Key so it doesn't duplicate the schema itself.
    /// </summary>
    internal static class ConfigSchemaFr
    {
        private static bool Fr => string.Equals(Localization.Loc.Lang, "fr", StringComparison.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> Groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Server", "Serveur" }, { "Gameplay", "Gameplay" }, { "Rates", "Taux" }, { "Combat", "Combat" },
            { "Guild & base", "Guilde & base" }, { "Build & gather", "Construction & récolte" },
            { "Drops & items", "Objets au sol" }, { "Death & PvP", "Mort & PvP" }, { "Stat leveling", "Montée de stats" },
            { "Advanced", "Avancé" }, { "World", "Monde" }, { "Difficulty", "Difficulté" }, { "Zombies", "Zombies" }, { "Multiplayer", "Multijoueur" }
        };

        private static readonly Dictionary<string, (string label, string desc)> Palworld = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            { "ServerName", ("Nom du serveur", "Nom affiché dans la liste des serveurs.") },
            { "ServerDescription", ("Description", "Texte court affiché dans le navigateur de serveurs.") },
            { "ServerPassword", ("Mot de passe serveur", "Mot de passe pour rejoindre. Vide = serveur public.") },
            { "AdminPassword", ("Mot de passe admin", "Donne l'accès admin RCON/REST (contrôle total). À garder secret.") },
            { "ServerPlayerMaxNum", ("Joueurs max", "Nombre max de joueurs simultanés (Palworld plafonne à 32).") },
            { "PublicPort", ("Port public", "Port UDP de jeu (défaut 8211). Doit être ouvert/redirigé.") },
            { "Difficulty", ("Difficulté", "Préréglage global. « None » = tout régler à la main.") },
            { "DeathPenalty", ("Pénalité de mort", "Ce que tu perds en mourant : rien / objets / objets+équip / tout (Pals inclus).") },
            { "bIsPvP", ("PvP activé", "Autorise les joueurs à se combattre.") },
            { "bEnablePlayerToPlayerDamage", ("Dégâts joueur↔joueur", "Les joueurs peuvent réellement se blesser (requis pour un vrai PvP).") },
            { "bEnableFriendlyFire", ("Tir allié", "Les membres d'une même guilde peuvent se blesser.") },
            { "bHardcore", ("Hardcore", "Mort permanente du personnage (pas de réapparition).") },
            { "DayTimeSpeedRate", ("Vitesse du jour", "Vitesse d'écoulement du jour (plus haut = jours plus courts).") },
            { "NightTimeSpeedRate", ("Vitesse de la nuit", "Vitesse d'écoulement de la nuit.") },
            { "ExpRate", ("Taux d'XP", "Multiplicateur d'XP joueurs et Pals (plus haut = level up plus vite).") },
            { "PalCaptureRate", ("Taux de capture", "Chance d'attraper les Pals (plus haut = plus facile).") },
            { "PalSpawnNumRate", ("Densité de Pals", "Quantité de Pals sauvages qui apparaissent.") },
            { "CollectionDropRate", ("Taux de récolte", "Quantité récoltée sur les nœuds (bois, pierre, minerai…).") },
            { "EnemyDropItemRate", ("Butin ennemis", "Loot lâché par les ennemis vaincus.") },
            { "WorkSpeedRate", ("Vitesse de travail", "Vitesse des Pals aux tâches de la base (craft, construction…).") },
            { "PalDamageRateAttack", ("Dégâts Pals (attaque)", "Dégâts infligés PAR les Pals (plus haut = Pals plus forts).") },
            { "PalDamageRateDefense", ("Dégâts Pals (défense)", "Dégâts subis PAR les Pals (plus haut = ils meurent plus vite).") },
            { "PlayerDamageRateAttack", ("Dégâts joueur (attaque)", "Dégâts infligés PAR les joueurs.") },
            { "PlayerDamageRateDefense", ("Dégâts joueur (défense)", "Dégâts subis PAR les joueurs (plus haut = plus fragile).") },
            { "GuildPlayerMaxNum", ("Joueurs max / guilde", "Nombre max de membres par guilde.") },
            { "CoopPlayerMaxNum", ("Joueurs coop max", "Max de joueurs partageant un même monde en coop.") },
            { "BaseCampMaxNum", ("Camps de base max", "Nombre max de camps de base sur tout le serveur.") },
            { "PalEggDefaultHatchingTime", ("Éclosion des œufs (h)", "Heures pour faire éclore un œuf de Pal (0 = instantané).") },
            { "AutoSaveSpan", ("Sauvegarde auto (s)", "Secondes entre deux sauvegardes automatiques du monde.") },
            { "RESTAPIEnabled", ("API REST activée", "API HTTP intégrée. Utilisée par le panneau admin & le compteur de joueurs de WindowsGSM.") },
            { "RESTAPIPort", ("Port API REST", "Port de l'API REST (défaut 8212).") },
            { "RCONEnabled", ("RCON activé", "Console distante. Utilisée par la console RCON de WindowsGSM (Broadcast, ShowPlayers…).") },
            { "RCONPort", ("Port RCON", "Port du RCON (défaut 25575).") },
            { "PublicIP", ("IP publique", "IP publique annoncée aux joueurs. Vide = détection auto.") },
            { "Region", ("Région", "Code de région du serveur. Vide = détection auto.") },
            { "bUseAuth", ("Authentification requise", "Exige l'authentification Steam pour rejoindre (recommandé activé).") },
            { "BanListURL", ("URL liste de bans", "URL de la liste de bannis (défaut = liste officielle Palworld).") },
            { "bShowPlayerList", ("Liste joueurs publique", "Expose publiquement la liste des joueurs connectés.") },
            { "ChatPostLimitPerMinute", ("Limite chat / min", "Anti-spam : messages de chat max par joueur et par minute.") },
            { "CrossplayPlatforms", ("Plateformes crossplay", "Plateformes autorisées, format (Steam,Xbox,PS5,Mac).") },
            { "LogFormatType", ("Format des logs", "Format du fichier de log du serveur.") },
            { "bIsShowJoinLeftMessage", ("Messages arrivée/départ", "Affiche les messages d'arrivée/départ dans le chat.") },
            { "bAllowClientMod", ("Autoriser mods client", "Autorise les joueurs ayant des mods client à se connecter.") },
            { "bIsMultiplay", ("Drapeau multijoueur", "Drapeau interne multijoueur (laisser tel quel pour un dédié).") },
            { "bIsUseBackupSaveData", ("Sauvegardes de secours", "Conserve des copies de secours de la sauvegarde.") },
            { "bEnableInvaderEnemy", ("Raids de base", "Active les ennemis envahisseurs qui attaquent les bases.") },
            { "EnablePredatorBossPal", ("Pals prédateurs/boss", "Active les Pals prédateurs (boss) itinérants.") },
            { "bEnableFastTravel", ("Voyage rapide", "Autorise le voyage rapide entre les statues.") },
            { "bEnableFastTravelOnlyBaseCamp", ("Voyage rapide : camps seulement", "Limite le voyage rapide aux camps de base.") },
            { "bIsStartLocationSelectByMap", ("Choix du départ sur carte", "Laisse choisir le point de départ sur la carte.") },
            { "bExistPlayerAfterLogout", ("Corps après déconnexion", "Le corps du joueur reste dans le monde après déconnexion.") },
            { "bEnableNonLoginPenalty", ("Pénalité de non-connexion", "Applique une pénalité aux joueurs absents un moment.") },
            { "SupplyDropSpan", ("Largages (min)", "Minutes entre deux largages de ravitaillement aériens.") },
            { "bActiveUNKO", ("Fonction UNKO", "Active la fonction « UNKO » (engrais).") },
            { "bEnableAimAssistPad", ("Visée assistée (manette)", "Visée assistée pour les joueurs manette.") },
            { "bEnableAimAssistKeyboard", ("Visée assistée (clavier)", "Visée assistée pour clavier/souris.") },
            { "RandomizerType", ("Randomizer", "Mode randomizer de Pals (None / Region / All).") },
            { "RandomizerSeed", ("Graine randomizer", "Graine du randomizer (vide = aléatoire).") },
            { "bIsRandomizerPalLevelRandom", ("Niveaux de Pals aléatoires", "Rend les niveaux des Pals aléatoires si le randomizer est actif.") },
            { "PlayerStomachDecreaceRate", ("Faim joueur", "Vitesse à laquelle la faim du joueur baisse (plus bas = mange moins).") },
            { "PlayerStaminaDecreaceRate", ("Endurance joueur", "Vitesse à laquelle l'endurance du joueur baisse.") },
            { "PlayerAutoHPRegeneRate", ("Régén PV joueur", "Vitesse de régénération des PV du joueur.") },
            { "PlayerAutoHpRegeneRateInSleep", ("Régén PV joueur (sommeil)", "Régén des PV du joueur pendant le sommeil.") },
            { "PalStomachDecreaceRate", ("Faim Pals", "Vitesse à laquelle la faim des Pals baisse.") },
            { "PalStaminaDecreaceRate", ("Endurance Pals", "Vitesse à laquelle l'endurance des Pals baisse.") },
            { "PalAutoHPRegeneRate", ("Régén PV Pals", "Vitesse de régénération des PV des Pals.") },
            { "PalAutoHpRegeneRateInSleep", ("Régén PV Pals (Palbox)", "Régén des PV des Pals dans la Palbox.") },
            { "ItemWeightRate", ("Poids des objets", "Multiplicateur de poids des objets (plus bas = porte plus).") },
            { "EquipmentDurabilityDamageRate", ("Usure de l'équipement", "Vitesse d'usure de la durabilité de l'équipement.") },
            { "ItemCorruptionMultiplier", ("Corruption des objets", "Vitesse de corruption/péremption des objets.") },
            { "BuildObjectHpRate", ("PV des structures", "Multiplicateur de PV des structures.") },
            { "BuildObjectDamageRate", ("Dégâts aux structures", "Dégâts infligés aux structures.") },
            { "BuildObjectDeteriorationDamageRate", ("Dégradation des structures", "Vitesse de dégradation des structures (0 = pas de dégradation).") },
            { "CollectionObjectHpRate", ("PV des nœuds", "PV des nœuds récoltables (arbres, rochers…).") },
            { "CollectionObjectRespawnSpeedRate", ("Respawn des nœuds", "Vitesse de réapparition des nœuds récoltables.") },
            { "MaxBuildingLimitNum", ("Constructions max", "Nombre max de constructions sur le serveur (0 = illimité).") },
            { "bBuildAreaLimit", ("Limite zone de construction", "Restreint la construction à la zone autour d'une base.") },
            { "DropItemMaxNum", ("Objets au sol max", "Nombre max d'objets au sol dans le monde.") },
            { "DropItemMaxNum_UNKO", ("Objets UNKO au sol max", "Nombre max d'objets « UNKO » au sol.") },
            { "DropItemAliveMaxHours", ("Durée objets au sol (h)", "Heures avant qu'un objet au sol disparaisse.") },
            { "ItemContainerForceMarkDirtyInterval", ("Sauvegarde des coffres", "Avancé : secondes entre sauvegardes forcées des coffres.") },
            { "BaseCampWorkerMaxNum", ("Ouvriers / base", "Nombre max de Pals travailleurs par camp de base.") },
            { "BaseCampMaxNumInGuild", ("Bases / guilde", "Nombre max de camps de base par guilde.") },
            { "bAutoResetGuildNoOnlinePlayers", ("Dissolution auto des guildes", "Dissout les guildes dont tous les membres sont hors ligne trop longtemps.") },
            { "AutoResetGuildTimeNoOnlinePlayers", ("Dissolution après (h)", "Heures hors ligne avant dissolution d'une guilde.") },
            { "GuildRejoinCooldownMinutes", ("Délai réintégration guilde (min)", "Délai avant qu'un joueur puisse rejoindre une guilde.") },
            { "bEnableDefenseOtherGuildPlayer", ("Défense vs autres guildes", "Autorise les défenses de base à viser les joueurs d'autres guildes.") },
            { "bInvisibleOtherGuildBaseCampAreaFX", ("Cacher zone autres bases", "Masque l'effet de zone des camps des autres guildes.") },
            { "bCanPickupOtherGuildDeathPenaltyDrop", ("Ramasser drops autres guildes", "Autorise à ramasser les objets lâchés à la mort d'autres guildes.") },
            { "bPalLost", ("Perdre ses Pals à la mort", "Le joueur perd les Pals de son équipe à la mort (façon hardcore).") },
            { "bCharacterRecreateInHardcore", ("Recréer perso (hardcore)", "En hardcore, recrée le personnage à la mort.") },
            { "BlockRespawnTime", ("Blocage réapparition (s)", "Temps avant de pouvoir réapparaître.") },
            { "RespawnPenaltyDurationThreshold", ("Seuil pénalité réapparition", "Avancé : seuil avant application de la pénalité de réapparition.") },
            { "RespawnPenaltyTimeScale", ("Échelle pénalité réapparition", "Avancé : multiplicateur de durée de la pénalité de réapparition.") },
            { "bDisplayPvPItemNumOnWorldMap_BaseCamp", ("Objets PvP bases sur carte", "Affiche le nombre d'objets PvP des camps sur la carte du monde.") },
            { "bDisplayPvPItemNumOnWorldMap_Player", ("Objets PvP joueurs sur carte", "Affiche le nombre d'objets PvP des joueurs sur la carte du monde.") },
            { "bAdditionalDropItemWhenPlayerKillingInPvPMode", ("Drop supplémentaire kill PvP", "Lâche un objet supplémentaire en tuant un joueur en PvP.") },
            { "AdditionalDropItemWhenPlayerKillingInPvPMode", ("Objet lâché au kill PvP", "Quel objet est lâché lors d'un kill PvP.") },
            { "AdditionalDropItemNumWhenPlayerKillingInPvPMode", ("Quantité drop kill PvP", "Combien d'objets sont lâchés lors d'un kill PvP.") },
            { "bAllowEnhanceStat_Health", ("Autoriser montée : Vie", "Les joueurs peuvent mettre des points en Vie.") },
            { "bAllowEnhanceStat_Attack", ("Autoriser montée : Attaque", "Les joueurs peuvent mettre des points en Attaque.") },
            { "bAllowEnhanceStat_Stamina", ("Autoriser montée : Endurance", "Les joueurs peuvent mettre des points en Endurance.") },
            { "bAllowEnhanceStat_Weight", ("Autoriser montée : Charge", "Les joueurs peuvent mettre des points en Charge portée.") },
            { "bAllowEnhanceStat_WorkSpeed", ("Autoriser montée : Vitesse travail", "Les joueurs peuvent mettre des points en Vitesse de travail.") },
            { "bAllowGlobalPalboxExport", ("Export Palbox globale", "Autorise l'export de Pals vers la Palbox globale (inter-serveurs).") },
            { "bAllowGlobalPalboxImport", ("Import Palbox globale", "Autorise l'import de Pals depuis la Palbox globale (inter-serveurs).") },
            { "DenyTechnologyList", ("Technologies désactivées", "Liste (séparée par virgules) des technologies à désactiver (vide = toutes autorisées).") },
            { "ServerReplicatePawnCullDistance", ("Distance de rendu réseau", "Distance réseau d'affichage des pnj/Pals (plus bas = meilleures perfs, portée visible réduite).") },
        };

        private static Dictionary<string, (string label, string desc)> Map(string game)
        {
            if (!string.IsNullOrEmpty(game) && game.StartsWith("Palworld", StringComparison.OrdinalIgnoreCase)) { return Palworld; }
            return null;
        }

        public static string Group(string game, string group)
        {
            if (!Fr || string.IsNullOrEmpty(group)) { return group; }
            return Groups.TryGetValue(group, out var g) ? g : group;
        }

        public static string Label(string game, FieldSpec spec)
        {
            if (Fr) { var m = Map(game); if (m != null && m.TryGetValue(spec.Key, out var t) && !string.IsNullOrEmpty(t.label)) { return t.label; } }
            return spec.Label;
        }

        public static string Desc(string game, FieldSpec spec)
        {
            if (Fr) { var m = Map(game); if (m != null && m.TryGetValue(spec.Key, out var t)) { return t.desc; } }
            return spec.Description;
        }
    }
}
