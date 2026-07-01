using System.Collections.Generic;

namespace WindowsGSM.Functions.Localization
{
    /// <summary>
    /// Translation tables for the fork UI. Add entries with E(key, en, fr, es, de).
    /// Keys are namespaced by module (e.g. "WebUsers.Title"). English is the source/fallback.
    /// </summary>
    internal static class LocStrings
    {
        public static readonly Dictionary<string, Dictionary<string, string>> All = Build();

        private static Dictionary<string, Dictionary<string, string>> Build()
        {
            var all = new Dictionary<string, Dictionary<string, string>>();
            foreach (var l in Loc.Supported) { all[l] = new Dictionary<string, string>(); }

            void E(string key, string en, string fr, string es, string de)
            {
                all["en"][key] = en; all["fr"][key] = fr; all["es"][key] = es; all["de"][key] = de;
            }

            // ==== Common ====
            E("Common.Close", "Close", "Fermer", "Cerrar", "Schließen");
            E("Common.Save", "Save", "Enregistrer", "Guardar", "Speichern");
            E("Common.Cancel", "Cancel", "Annuler", "Cancelar", "Abbrechen");
            E("Common.Remove", "Remove", "Supprimer", "Eliminar", "Entfernen");
            E("Common.Generate", "Generate", "Générer", "Generar", "Generieren");

            // ==== Web portal accounts (WebUsersDialog) ====
            E("WebUsers.Title", "Web portal accounts", "Comptes du portail web", "Cuentas del portal web", "Web-Portal-Konten");
            E("WebUsers.RolesHelp",
                "Roles: Viewer (read) · Operator (+ start/stop/restart/backup) · Admin (all). Tick the servers each account may access.",
                "Rôles : Viewer (lecture) · Operator (+ start/stop/restart/backup) · Admin (tout). Cochez les serveurs autorisés pour chaque compte.",
                "Roles: Viewer (lectura) · Operator (+ start/stop/restart/backup) · Admin (todo). Marque los servidores permitidos para cada cuenta.",
                "Rollen: Viewer (Lesen) · Operator (+ Start/Stopp/Neustart/Backup) · Admin (alles). Wählen Sie die für jedes Konto erlaubten Server.");
            E("WebUsers.Username", "Username", "Utilisateur", "Usuario", "Benutzername");
            E("WebUsers.Password", "Password", "Mot de passe", "Contraseña", "Passwort");
            E("WebUsers.Role", "Role", "Rôle", "Rol", "Rolle");
            E("WebUsers.AllowedServers", "Allowed servers", "Serveurs autorisés", "Servidores permitidos", "Erlaubte Server");
            E("WebUsers.AllServers", "All servers", "Tous les serveurs", "Todos los servidores", "Alle Server");
            E("WebUsers.NoServers", "(no servers found)", "(aucun serveur trouvé)", "(no se encontraron servidores)", "(keine Server gefunden)");
            E("WebUsers.AddUpdate", "Add / Update", "Ajouter / Mettre à jour", "Añadir / Actualizar", "Hinzufügen / Aktualisieren");
            E("WebUsers.Removed", "Account removed.", "Compte supprimé.", "Cuenta eliminada.", "Konto entfernt.");
            E("WebUsers.UserRequired", "Username required.", "Nom d'utilisateur requis.", "Se requiere un nombre de usuario.", "Benutzername erforderlich.");
            E("WebUsers.PwRequired", "Password required for a new account.", "Mot de passe requis pour un nouveau compte.", "Se requiere una contraseña para una cuenta nueva.", "Passwort für ein neues Konto erforderlich.");
            E("WebUsers.PickServer",
                "Tick at least one server, or choose \"All servers\".",
                "Cochez au moins un serveur, ou choisissez « Tous les serveurs ».",
                "Marque al menos un servidor o elija «Todos los servidores».",
                "Wählen Sie mindestens einen Server oder „Alle Server“.");
            E("WebUsers.Saved", "Account \"{0}\" saved ({1}).", "Compte « {0} » enregistré ({1}).", "Cuenta «{0}» guardada ({1}).", "Konto „{0}“ gespeichert ({1}).");
            E("WebUsers.ScopeAll", "all servers", "tous les serveurs", "todos los servidores", "alle Server");
            E("WebUsers.ScopeServers", "servers {0}", "serveurs {0}", "servidores {0}", "Server {0}");

            // ==== ApiToken ====
            E("ApiToken.Title", "API Token — #{0} {1}", "Jeton API — #{0} {1}", "Token de API — #{0} {1}", "API-Token — #{0} {1}");
            E("ApiToken.HelpSatisfactory", "Satisfactory: enter the server PASSWORD (Client or Admin) — WGSM obtains a token by itself\nvia the API to read the player count (read-only). An API token (server.GenerateAPIToken) also works.", "Satisfactory : saisis le MOT DE PASSE du serveur (Client ou Admin) — WGSM obtient un token par lui-même\nvia l'API pour lire le nombre de joueurs (lecture seule). Un token API (server.GenerateAPIToken) fonctionne aussi.", "Satisfactory: introduce la CONTRASEÑA del servidor (Cliente o Admin) — WGSM obtiene un token por sí mismo\na través de la API para leer el número de jugadores (solo lectura). Un token de API (server.GenerateAPIToken) también funciona.", "Satisfactory: Gib das SERVER-PASSWORT ein (Client oder Admin) — WGSM ermittelt selbst ein Token\nüber die API, um die Spieleranzahl zu lesen (schreibgeschützt). Ein API-Token (server.GenerateAPIToken) funktioniert ebenfalls.");
            E("ApiToken.HelpGeneric", "API token used to query this server. Paste it below.", "Token API utilisé pour interroger ce serveur. Colle-le ci-dessous.", "Token de API usado para consultar este servidor. Pégalo abajo.", "API-Token zur Abfrage dieses Servers. Füge es unten ein.");
            E("ApiToken.FieldLabel", "API Token", "Jeton API", "Token de API", "API-Token");
            E("ApiToken.Save", "Save", "Enregistrer", "Guardar", "Speichern");
            E("ApiToken.Clear", "Clear", "Effacer", "Borrar", "Leeren");
            E("ApiToken.Close", "Close", "Fermer", "Cerrar", "Schließen");

            // ==== Config ====
            E("Config.Title", "Configuration — #{0} {1}", "Configuration — #{0} {1}", "Configuración — #{0} {1}", "Konfiguration — #{0} {1}");
            E("Config.FileLabel", "File:", "Fichier :", "Archivo:", "Datei:");
            E("Config.GameSettings", "{0} settings", "Paramètres {0}", "Ajustes de {0}", "{0}-Einstellungen");
            E("Config.NoFile", "No configuration file found in serverfiles.\n(The server may not be installed yet.)", "Aucun fichier de configuration trouvé dans serverfiles.\n(Le serveur n'est peut-être pas encore installé.)", "No se encontró ningún archivo de configuración en serverfiles.\n(Puede que el servidor aún no esté instalado.)", "Keine Konfigurationsdatei in serverfiles gefunden.\n(Der Server ist möglicherweise noch nicht installiert.)");
            E("Config.CannotRead", "Cannot read: {0}", "Lecture impossible : {0}", "No se puede leer: {0}", "Kann nicht gelesen werden: {0}");
            E("Config.OtherSettings", "Other settings ({0}) — raw editing", "Autres paramètres ({0}) — édition brute", "Otros ajustes ({0}) — edición sin formato", "Weitere Einstellungen ({0}) — Rohbearbeitung");
            E("Config.NoEntries", "No recognized key/value entry in this file.", "Aucune entrée clé/valeur reconnue dans ce fichier.", "No hay ninguna entrada clave/valor reconocida en este archivo.", "Kein erkannter Schlüssel/Wert-Eintrag in dieser Datei.");
            E("Config.GlobalSection", "(global)", "(global)", "(global)", "(global)");
            E("Config.Saved", "✔ Saved (.wgsmbak backup created). Restart the server to apply.", "✔ Enregistré (sauvegarde .wgsmbak créée). Redémarre le serveur pour appliquer.", "✔ Guardado (copia de seguridad .wgsmbak creada). Reinicia el servidor para aplicar.", "✔ Gespeichert (.wgsmbak-Sicherung erstellt). Starte den Server neu, um zu übernehmen.");
            E("Config.SaveFailed", "Failed: {0}", "Échec : {0}", "Error: {0}", "Fehlgeschlagen: {0}");

            // ==== Doctor ====
            E("Doctor.Title", "Server Doctor", "Docteur du serveur", "Doctor del servidor", "Server-Doktor");
            E("Doctor.ServerLabel", "Server:", "Serveur :", "Servidor:", "Server:");
            E("Doctor.ExternalButton", "External reachability…", "Accessibilité externe…", "Accesibilidad externa…", "Externe Erreichbarkeit…");
            E("Doctor.Retest", "Re-test", "Retester", "Repetir prueba", "Erneut testen");
            E("Doctor.NoServer", "No server to diagnose.", "Aucun serveur à diagnostiquer.", "Ningún servidor que diagnosticar.", "Kein Server zum Diagnostizieren.");
            E("Doctor.ExternalHeader", "External reachability (via check-host.net)", "Accessibilité externe (via check-host.net)", "Accesibilidad externa (vía check-host.net)", "Externe Erreichbarkeit (über check-host.net)");
            E("Doctor.ExternalInProgress", "⏳ Test in progress… (your public IP is sent to check-host.net)", "⏳ Test en cours… (ton IP publique est envoyée à check-host.net)", "⏳ Prueba en curso… (tu IP pública se envía a check-host.net)", "⏳ Test läuft… (deine öffentliche IP wird an check-host.net gesendet)");
            E("Doctor.Error", "Error: {0}", "Erreur : {0}", "Error: {0}", "Fehler: {0}");
            E("Doctor.CheckServerStatus", "Server status", "État du serveur", "Estado del servidor", "Serverstatus");
            E("Doctor.StatusRunning", "Running.", "En cours d'exécution.", "En ejecución.", "Läuft.");
            E("Doctor.StatusStopped", "Stopped (listening-port checks are skipped).", "Arrêté (les vérifications de ports en écoute sont ignorées).", "Detenido (se omiten las comprobaciones de puertos en escucha).", "Gestoppt (Prüfungen der lauschenden Ports werden übersprungen).");
            E("Doctor.CheckInternet", "Internet reachability", "Accessibilité Internet", "Accesibilidad de Internet", "Internet-Erreichbarkeit");
            E("Doctor.InternetDetail", "Depends on the firewall + port-forward on the router (see Tools ▸ Ports / UPnP). This diagnostic checks LOCAL listening, not the opening on the router side.", "Dépend du pare-feu + de la redirection de port sur le routeur (voir Outils ▸ Ports / UPnP). Ce diagnostic vérifie l'écoute LOCALE, pas l'ouverture côté routeur.", "Depende del cortafuegos + la redirección de puertos en el router (ver Herramientas ▸ Puertos / UPnP). Este diagnóstico comprueba la escucha LOCAL, no la apertura en el lado del router.", "Hängt von der Firewall + Portweiterleitung am Router ab (siehe Tools ▸ Ports / UPnP). Diese Diagnose prüft das LOKALE Lauschen, nicht die Öffnung auf der Router-Seite.");
            E("Doctor.CheckPorts", "Ports", "Ports", "Puertos", "Ports");
            E("Doctor.PortsNoneInferred", "No port inferred (incomplete config?).", "Aucun port déduit (configuration incomplète ?).", "No se dedujo ningún puerto (¿configuración incompleta?).", "Kein Port ermittelt (unvollständige Konfiguration?).");
            E("Doctor.PortsListFailed", "Unable to list listening ports: {0}", "Impossible de lister les ports en écoute : {0}", "No se pueden listar los puertos en escucha: {0}", "Lauschende Ports können nicht aufgelistet werden: {0}");
            E("Doctor.PortName", "Port {0} ({1})", "Port {0} ({1})", "Puerto {0} ({1})", "Port {0} ({1})");
            E("Doctor.PortServerStopped", "Server stopped.", "Serveur arrêté.", "Servidor detenido.", "Server gestoppt.");
            E("Doctor.PortListening", "Listening locally.", "En écoute localement.", "En escucha localmente.", "Lauscht lokal.");
            E("Doctor.PortNotListening", "NOT listening while the server is running (wrong port/protocol, or not yet initialized?).", "PAS en écoute alors que le serveur tourne (mauvais port/protocole, ou pas encore initialisé ?).", "NO está en escucha mientras el servidor se ejecuta (¿puerto/protocolo incorrecto, o aún no inicializado?).", "Lauscht NICHT, während der Server läuft (falscher Port/Protokoll oder noch nicht initialisiert?).");
            E("Doctor.CheckDisk", "Disk space", "Espace disque", "Espacio en disco", "Speicherplatz");
            E("Doctor.DiskFree", "{0} GB free on {1}.", "{0} Go libres sur {1}.", "{0} GB libres en {1}.", "{0} GB frei auf {1}.");
            E("Doctor.DiskFreeLow", "{0} GB free on {1} (low).", "{0} Go libres sur {1} (faible).", "{0} GB libres en {1} (bajo).", "{0} GB frei auf {1} (wenig).");
            E("Doctor.PublicIP", "Public IP", "IP publique", "IP pública", "Öffentliche IP");
            E("Doctor.PublicIPFailed", "Unable to obtain it: {0}", "Impossible de l'obtenir : {0}", "No se puede obtener: {0}", "Kann nicht abgerufen werden: {0}");
            E("Doctor.ExternalPortName", "External {0} ({1})", "Externe {0} ({1})", "Externo {0} ({1})", "Extern {0} ({1})");
            E("Doctor.ExternalUdp", "UDP: not reliably verifiable from the outside — test in-game.", "UDP : non vérifiable de manière fiable depuis l'extérieur — teste en jeu.", "UDP: no verificable de forma fiable desde el exterior — pruébalo en el juego.", "UDP: von außen nicht zuverlässig überprüfbar — teste es im Spiel.");
            E("Doctor.CheckHostNoReqId", "check-host.net: no request_id.", "check-host.net : pas de request_id.", "check-host.net: sin request_id.", "check-host.net: keine request_id.");
            E("Doctor.ExtOpen", "Open from the Internet ({0} node(s) were able to connect).", "Ouvert depuis Internet ({0} nœud(s) ont pu se connecter).", "Abierto desde Internet ({0} nodo(s) pudieron conectarse).", "Vom Internet aus offen ({0} Knoten konnten sich verbinden).");
            E("Doctor.ExtNotReachable", "NOT reachable from the outside (connection refused/timeout). Check firewall + port-forward.", "PAS accessible depuis l'extérieur (connexion refusée/expirée). Vérifie le pare-feu + la redirection de port.", "NO accesible desde el exterior (conexión rechazada/tiempo agotado). Comprueba el cortafuegos + la redirección de puertos.", "Von außen NICHT erreichbar (Verbindung abgelehnt/Zeitüberschreitung). Prüfe Firewall + Portweiterleitung.");
            E("Doctor.ExtUndetermined", "Undetermined (no clear response from the probes).", "Indéterminé (aucune réponse claire des sondes).", "Indeterminado (sin respuesta clara de las sondas).", "Unbestimmt (keine eindeutige Antwort von den Sonden).");
            E("Doctor.CheckHostError", "check-host.net: {0}", "check-host.net : {0}", "check-host.net: {0}", "check-host.net: {0}");
            E("Doctor.JavaNone", "No Java detected (required for Minecraft). See adoptium.net.", "Aucun Java détecté (requis pour Minecraft). Voir adoptium.net.", "No se detectó Java (necesario para Minecraft). Ver adoptium.net.", "Kein Java erkannt (für Minecraft erforderlich). Siehe adoptium.net.");
            E("Doctor.JavaDetected", "Java {0} detected. (Recent MC requires 17/21 — adjust if needed.)", "Java {0} détecté. (Les versions récentes de MC requièrent 17/21 — ajuste si besoin.)", "Java {0} detectado. (MC reciente requiere 17/21 — ajústalo si es necesario.)", "Java {0} erkannt. (Aktuelles MC benötigt 17/21 — bei Bedarf anpassen.)");
            E("Doctor.TruckPackagesOk", "server_packages.sii + .dat present (map/DLC OK).", "server_packages.sii + .dat présents (carte/DLC OK).", "server_packages.sii + .dat presentes (mapa/DLC OK).", "server_packages.sii + .dat vorhanden (Karte/DLC OK).");
            E("Doctor.TruckPackagesMissing", "Missing -> the server won't start. In the client (g_console 1), type \"export_server_packages\" with ALL your DLC loaded, then start the server: the plugin copies save\\server_packages.sii + .dat automatically.", "Manquants -> le serveur ne démarrera pas. Dans le client (g_console 1), tape \"export_server_packages\" avec TOUS tes DLC chargés, puis démarre le serveur : le plugin copie save\\server_packages.sii + .dat automatiquement.", "Faltan -> el servidor no arrancará. En el cliente (g_console 1), escribe \"export_server_packages\" con TODOS tus DLC cargados, luego inicia el servidor: el complemento copia save\\server_packages.sii + .dat automáticamente.", "Fehlen -> der Server startet nicht. Gib im Client (g_console 1) \"export_server_packages\" mit ALLEN geladenen DLCs ein, starte dann den Server: das Plugin kopiert save\\server_packages.sii + .dat automatisch.");

            // ==== Donator ====
            E("Donator.Title", "Donor feature", "Fonctionnalité donateur", "Función para donantes", "Spender-Funktion");
            E("Donator.Explain", "Donor-only feature. Become a donor via \"Donor Connect\" in Settings (Patreon), or — if you are the owner — unlock with your passphrase.", "Fonctionnalité réservée aux donateurs. Devenez donateur via « Donor Connect » dans les Paramètres (Patreon) ou, si vous êtes le propriétaire, déverrouillez avec votre phrase secrète.", "Función exclusiva para donantes. Conviértete en donante mediante «Donor Connect» en Ajustes (Patreon) o, si eres el propietario, desbloquéala con tu frase de contraseña.", "Nur für Spender verfügbare Funktion. Werden Sie über „Donor Connect“ in den Einstellungen (Patreon) zum Spender oder – wenn Sie der Eigentümer sind – schalten Sie sie mit Ihrer Passphrase frei.");
            E("Donator.OwnerPassphrase", "Owner passphrase:", "Phrase secrète du propriétaire :", "Frase de contraseña del propietario:", "Passphrase des Eigentümers:");
            E("Donator.Unlock", "Unlock", "Déverrouiller", "Desbloquear", "Freischalten");
            E("Donator.Unlocked", "✔ Unlocked.", "✔ Déverrouillé.", "✔ Desbloqueado.", "✔ Freigeschaltet.");
            E("Donator.BadPassphrase", "Incorrect passphrase (or owner key not yet configured).", "Phrase secrète incorrecte (ou clé propriétaire pas encore configurée).", "Frase de contraseña incorrecta (o clave de propietario aún no configurada).", "Falsche Passphrase (oder Eigentümerschlüssel noch nicht konfiguriert).");

            // ==== GenericSteam ====
            E("GenericSteam.Title", "Add a Steam server (AppID)", "Ajouter un serveur Steam (AppID)", "Añadir un servidor Steam (AppID)", "Steam-Server hinzufügen (AppID)");
            E("GenericSteam.Intro", "Search for a game by name, or directly enter a Steam AppID. WGSM resolves the executable and arguments automatically, installs via SteamCMD, then adds the server.", "Recherche un jeu par son nom, ou saisis directement un AppID Steam. WGSM résout automatiquement l'exécutable et les arguments, installe via SteamCMD, puis ajoute le serveur.", "Busca un juego por su nombre o introduce directamente un AppID de Steam. WGSM resuelve automáticamente el ejecutable y los argumentos, instala mediante SteamCMD y luego añade el servidor.", "Suche ein Spiel nach Namen oder gib direkt eine Steam-AppID ein. WGSM ermittelt automatisch die ausführbare Datei und die Argumente, installiert über SteamCMD und fügt anschließend den Server hinzu.");
            E("GenericSteam.Search", "Search", "Rechercher", "Buscar", "Suchen");
            E("GenericSteam.Install", "Install", "Installer", "Instalar", "Installieren");
            E("GenericSteam.ColGame", "Game", "Jeu", "Juego", "Spiel");
            E("GenericSteam.Searching", "Searching…", "Recherche en cours…", "Buscando…", "Suche läuft…");
            E("GenericSteam.NoResults", "No results (check the name, or enter the AppID directly).", "Aucun résultat (vérifie le nom, ou saisis directement l'AppID).", "Sin resultados (comprueba el nombre o introduce directamente el AppID).", "Keine Ergebnisse (überprüfe den Namen oder gib die AppID direkt ein).");
            E("GenericSteam.ResultCount", "{0} result(s). Select a row, then Install.", "{0} résultat(s). Sélectionne une ligne, puis Installer.", "{0} resultado(s). Selecciona una fila y luego Instalar.", "{0} Ergebnis(se). Wähle eine Zeile aus und klicke dann auf Installieren.");
            E("GenericSteam.SearchError", "Search error: {0}", "Erreur de recherche : {0}", "Error de búsqueda: {0}", "Suchfehler: {0}");
            E("GenericSteam.EnterNumericAppId", "Enter a numeric AppID (or select a game from the list).", "Saisis un AppID numérique (ou sélectionne un jeu dans la liste).", "Introduce un AppID numérico (o selecciona un juego de la lista).", "Gib eine numerische AppID ein (oder wähle ein Spiel aus der Liste).");
            E("GenericSteam.Resolving", "Resolving the executable (AppInfo)…", "Résolution de l'exécutable (AppInfo)…", "Resolviendo el ejecutable (AppInfo)…", "Ausführbare Datei wird ermittelt (AppInfo)…");
            E("GenericSteam.ResolveFailed", "Unable to resolve the Windows executable for AppID {0} (game missing from SteamAppInfo or no Windows launch).", "Impossible de résoudre l'exécutable Windows pour l'AppID {0} (jeu absent de SteamAppInfo ou aucun lancement Windows).", "No se pudo resolver el ejecutable de Windows para el AppID {0} (juego ausente de SteamAppInfo o sin lanzamiento en Windows).", "Die Windows-Programmdatei für AppID {0} konnte nicht ermittelt werden (Spiel fehlt in SteamAppInfo oder kein Windows-Start).");
            E("GenericSteam.ResolveError", "Resolution error: {0}", "Erreur de résolution : {0}", "Error de resolución: {0}", "Auflösungsfehler: {0}");

            // ==== Mods ====
            E("Mods.Title", "Mods — #{0} {1}", "Mods — #{0} {1}", "Mods — #{0} {1}", "Mods — #{0} {1}");
            E("Mods.UnrecognizedGame", "Mods — unrecognized game", "Mods — jeu non reconnu", "Mods — juego no reconocido", "Mods — nicht erkanntes Spiel");
            E("Mods.HeaderFileMods", "File mods — {0}", "Mods fichiers — {0}", "Mods de archivo — {0}", "Datei-Mods — {0}");
            E("Mods.HeaderWorkshop", "Steam Workshop — {0}", "Steam Workshop — {0}", "Steam Workshop — {0}", "Steam Workshop — {0}");
            E("Mods.HeaderGeneric", "Mods — {0}", "Mods — {0}", "Mods — {0}", "Mods — {0}");
            E("Mods.NoProfile", "This game has no known mod profile. You can open the server folder and manage the files manually.", "Ce jeu n'a pas de profil de mods connu. Tu peux ouvrir le dossier du serveur et gérer les fichiers manuellement.", "Este juego no tiene un perfil de mods conocido. Puedes abrir la carpeta del servidor y gestionar los archivos manualmente.", "Für dieses Spiel ist kein Mod-Profil bekannt. Du kannst den Serverordner öffnen und die Dateien manuell verwalten.");
            E("Mods.OpenServerFolder", "Open the server folder", "Ouvrir le dossier du serveur", "Abrir la carpeta del servidor", "Serverordner öffnen");
            E("Mods.NoModSystem", "No server mod system for this game.", "Aucun système de mods serveur pour ce jeu.", "Este juego no tiene sistema de mods de servidor.", "Kein Server-Mod-System für dieses Spiel.");
            E("Mods.AddMod", "Add a mod…", "Ajouter un mod…", "Añadir un mod…", "Mod hinzufügen…");
            E("Mods.OpenFolder", "Open the folder", "Ouvrir le dossier", "Abrir la carpeta", "Ordner öffnen");
            E("Mods.Refresh", "Refresh", "Actualiser", "Actualizar", "Aktualisieren");
            E("Mods.NoModsYet", "No mods yet. Click \"Add a mod…\" or drop files into the folder.", "Aucun mod pour l'instant. Clique sur « Ajouter un mod… » ou dépose des fichiers dans le dossier.", "Todavía no hay mods. Haz clic en «Añadir un mod…» o suelta archivos en la carpeta.", "Noch keine Mods. Klicke auf „Mod hinzufügen…“ oder lege Dateien im Ordner ab.");
            E("Mods.TypeFolder", "folder", "dossier", "carpeta", "Ordner");
            E("Mods.Disabled", "disabled", "désactivé", "desactivado", "deaktiviert");
            E("Mods.EnabledMsg", "\"{0}\" enabled. Restart the server to apply.", "« {0} » activé. Redémarre le serveur pour appliquer.", "«{0}» activado. Reinicia el servidor para aplicar.", "„{0}“ aktiviert. Starte den Server neu, um es anzuwenden.");
            E("Mods.DisabledMsg", "\"{0}\" disabled. Restart the server to apply.", "« {0} » désactivé. Redémarre le serveur pour appliquer.", "«{0}» desactivado. Reinicia el servidor para aplicar.", "„{0}“ deaktiviert. Starte den Server neu, um es anzuwenden.");
            E("Mods.FilterMods", "Mods", "Mods", "Mods", "Mods");
            E("Mods.FilterAllFiles", "All files", "Tous les fichiers", "Todos los archivos", "Alle Dateien");
            E("Mods.ChooseMods", "Choose one or more mods", "Choisir un ou plusieurs mods", "Elegir uno o varios mods", "Einen oder mehrere Mods auswählen");
            E("Mods.ModsAdded", "{0} mod(s) added.", "{0} mod(s) ajouté(s).", "{0} mod(s) añadido(s).", "{0} Mod(s) hinzugefügt.");
            E("Mods.AddLabel", "Add:", "Ajouter :", "Añadir:", "Hinzufügen:");
            E("Mods.WorkshopId", "Workshop ID", "ID Workshop", "ID de Workshop", "Workshop-ID");
            E("Mods.NameOptional", "Name (optional)", "Nom (facultatif)", "Nombre (opcional)", "Name (optional)");
            E("Mods.InvalidWorkshopId", "Invalid Workshop ID (digits only).", "ID Workshop invalide (chiffres uniquement).", "ID de Workshop no válido (solo dígitos).", "Ungültige Workshop-ID (nur Ziffern).");
            E("Mods.DownloadSteamCmd", "Download (SteamCMD)", "Télécharger (SteamCMD)", "Descargar (SteamCMD)", "Herunterladen (SteamCMD)");
            E("Mods.WriteConfigKey", "Write {0} to the config", "Écrire {0} dans la config", "Escribir {0} en la config", "{0} in die Konfiguration schreiben");
            E("Mods.NoWorkshopMods", "No Workshop mods. Paste an ID (the number in the Steam Workshop URL) and click +.", "Aucun mod Workshop. Colle un ID (le numéro dans l'URL du Steam Workshop) et clique sur +.", "No hay mods de Workshop. Pega un ID (el número en la URL del Steam Workshop) y haz clic en +.", "Keine Workshop-Mods. Füge eine ID ein (die Nummer in der Steam-Workshop-URL) und klicke auf +.");
            E("Mods.RemoveFromList", "Remove from the list", "Retirer de la liste", "Quitar de la lista", "Aus der Liste entfernen");
            E("Mods.Downloading", "Downloading {0}…", "Téléchargement de {0}…", "Descargando {0}…", "{0} wird heruntergeladen…");
            E("Mods.DownloadFinished", "Download finished: {0} OK, {1} failure(s). Then \"Write {2}\" if applicable.", "Téléchargement terminé : {0} OK, {1} échec(s). Ensuite « Écrire {2} » si nécessaire.", "Descarga finalizada: {0} OK, {1} fallo(s). Luego «Escribir {2}» si procede.", "Download abgeschlossen: {0} OK, {1} Fehler. Danach „{2} schreiben“, falls zutreffend.");
            E("Mods.ErrorPrefix", "Error: {0}", "Erreur : {0}", "Error: {0}", "Fehler: {0}");

            // ==== Notif ====
            E("Notif.Title", "Notifications", "Notifications", "Notificaciones", "Benachrichtigungen");
            E("Notif.Intro", "Global notification channels. All alerts (crash, RAM, disk, updates...) are broadcast here. Fill in a channel, check \"Enable\", then \"Test\".", "Canaux de notification globaux. Toutes les alertes (plantage, RAM, disque, mises à jour...) sont diffusées ici. Renseigne un canal, coche « Activer », puis « Tester ».", "Canales de notificación globales. Todas las alertas (fallos, RAM, disco, actualizaciones...) se difunden aquí. Rellena un canal, marca «Activar» y luego «Probar».", "Globale Benachrichtigungskanäle. Alle Warnungen (Absturz, RAM, Datenträger, Updates...) werden hier ausgegeben. Fülle einen Kanal aus, aktiviere „Aktivieren“ und dann „Testen“.");
            E("Notif.Enable", "Enable", "Activer", "Activar", "Aktivieren");
            E("Notif.TestChannel", "Test this channel", "Tester ce canal", "Probar este canal", "Diesen Kanal testen");
            E("Notif.Sending", "⏳ Sending a test message…", "⏳ Envoi d'un message de test…", "⏳ Enviando un mensaje de prueba…", "⏳ Testnachricht wird gesendet…");
            E("Notif.TestSubject", "Test WindowsGSM", "Test WindowsGSM", "Prueba WindowsGSM", "Test WindowsGSM");
            E("Notif.TestBody", "✅ This is a test message from WindowsGSM.", "✅ Ceci est un message de test depuis WindowsGSM.", "✅ Este es un mensaje de prueba de WindowsGSM.", "✅ Dies ist eine Testnachricht von WindowsGSM.");
            E("Notif.SentOk", "✅ Sent. Check that it was received.", "✅ Envoyé. Vérifie qu'il a bien été reçu.", "✅ Enviado. Comprueba que se ha recibido.", "✅ Gesendet. Prüfe, ob sie angekommen ist.");
            E("Notif.SentFail", "❌ Failed (channel disabled or incorrect fields?). See the AppLog.", "❌ Échec (canal désactivé ou champs incorrects ?). Consulte l'AppLog.", "❌ Error (¿canal desactivado o campos incorrectos?). Consulta el AppLog.", "❌ Fehlgeschlagen (Kanal deaktiviert oder falsche Felder?). Siehe AppLog.");
            E("Notif.ErrorPrefix", "❌ Error: {0}", "❌ Erreur : {0}", "❌ Error: {0}", "❌ Fehler: {0}");
            E("Notif.NtfyServer", "Server (URL)", "Serveur (URL)", "Servidor (URL)", "Server (URL)");
            E("Notif.NtfyTopic", "Topic", "Sujet (topic)", "Tema (topic)", "Thema (Topic)");
            E("Notif.NtfyToken", "Token (optional)", "Token (facultatif)", "Token (opcional)", "Token (optional)");
            E("Notif.NtfyPriority", "Priority (min/low/default/high/max)", "Priorité (min/low/default/high/max)", "Prioridad (min/low/default/high/max)", "Priorität (min/low/default/high/max)");
            E("Notif.TelegramBotToken", "Bot Token", "Bot Token", "Bot Token", "Bot Token");
            E("Notif.TelegramChatId", "Chat ID", "Chat ID", "Chat ID", "Chat ID");
            E("Notif.EmailTitle", "Email (SMTP)", "E-mail (SMTP)", "Correo (SMTP)", "E-Mail (SMTP)");
            E("Notif.EmailSmtpHost", "SMTP server", "Serveur SMTP", "Servidor SMTP", "SMTP-Server");
            E("Notif.EmailPort", "Port", "Port", "Puerto", "Port");
            E("Notif.EmailSsl", "SSL/TLS", "SSL/TLS", "SSL/TLS", "SSL/TLS");
            E("Notif.EmailUsername", "Username", "Nom d'utilisateur", "Nombre de usuario", "Benutzername");
            E("Notif.EmailPassword", "Password", "Mot de passe", "Contraseña", "Passwort");
            E("Notif.EmailFrom", "Sender (From)", "Expéditeur (From)", "Remitente (From)", "Absender (From)");
            E("Notif.EmailTo", "Recipient(s) (To)", "Destinataire(s) (To)", "Destinatario(s) (To)", "Empfänger (To)");
            E("Notif.WebhookTitle", "Generic webhook", "Webhook générique", "Webhook genérico", "Generischer Webhook");
            E("Notif.WebhookUrl", "URL", "URL", "URL", "URL");
            E("Notif.WebhookBearer", "Bearer (optional)", "Bearer (facultatif)", "Bearer (opcional)", "Bearer (optional)");

            // ==== Ports ====
            E("Ports.Title", "Ports / UPnP", "Ports / UPnP", "Puertos / UPnP", "Ports / UPnP");
            E("Ports.EnableUpnp", "Enable UPnP auto port-forward (opens/closes ports on the router at start/stop)", "Activer la redirection de ports automatique UPnP (ouvre/ferme les ports sur le routeur au démarrage/arrêt)", "Activar el reenvío automático de puertos UPnP (abre/cierra los puertos del router al iniciar/detener)", "Automatische UPnP-Portweiterleitung aktivieren (öffnet/schließt Ports am Router beim Starten/Stoppen)");
            E("Ports.Tip", "Tip: if UPnP is disabled on the router (e.g. OPNsense), leave unchecked and simply copy the ports listed below into your manual forward. RCON is never suggested enabled: only open it if necessary.", "Astuce : si UPnP est désactivé sur le routeur (ex. OPNsense), laisse décoché et recopie simplement les ports listés ci-dessous dans ta redirection manuelle. RCON n'est jamais suggéré activé : ne l'ouvre que si nécessaire.", "Consejo: si UPnP está desactivado en el router (p. ej. OPNsense), déjalo sin marcar y simplemente copia los puertos listados abajo en tu reenvío manual. RCON nunca se sugiere activado: ábrelo solo si es necesario.", "Tipp: Ist UPnP am Router deaktiviert (z. B. OPNsense), lasse das Kontrollkästchen leer und übernimm die unten aufgeführten Ports einfach in deine manuelle Weiterleitung. RCON wird nie standardmäßig aktiviert vorgeschlagen: öffne es nur, wenn nötig.");
            E("Ports.NoServer", "No server.", "Aucun serveur.", "Ningún servidor.", "Kein Server.");
            E("Ports.DeletePortTip", "Delete this port", "Supprimer ce port", "Eliminar este puerto", "Diesen Port löschen");
            E("Ports.AddLabel", "Add:", "Ajouter :", "Añadir:", "Hinzufügen:");
            E("Ports.ManualLabel", "Manual", "Manuel", "Manual", "Manuell");

            // ==== WebApi ====
            E("WebApi.Title", "Web API (remote control)", "API Web (contrôle à distance)", "API web (control remoto)", "Web-API (Fernsteuerung)");
            E("WebApi.Header", "Remote control (web server)", "Contrôle à distance (serveur web)", "Control remoto (servidor web)", "Fernsteuerung (Webserver)");
            E("WebApi.Intro", "Two independent parts you can toggle separately: the token API and the browser portal. The server runs as soon as at least one is enabled — no separate master switch.", "Deux parties indépendantes activables séparément : l'API à jeton et le portail navigateur. Le serveur démarre dès qu'au moins l'une est activée — pas d'interrupteur principal séparé.", "Dos partes independientes que puedes activar por separado: la API con token y el portal del navegador. El servidor se ejecuta en cuanto se activa al menos una — sin interruptor principal aparte.", "Zwei unabhängige Teile, die separat aktivierbar sind: die Token-API und das Browser-Portal. Der Server läuft, sobald mindestens einer aktiviert ist — kein separater Hauptschalter.");
            E("WebApi.EnableApi", "Enable the token API", "Activer l'API à jeton", "Activar la API con token", "Token-API aktivieren");
            E("WebApi.ApiRoutesHelp", "GET /api/servers · POST /api/servers/{id}/{start|stop|restart|backup}. Requires a Bearer token (below).", "GET /api/servers · POST /api/servers/{id}/{start|stop|restart|backup}. Nécessite un jeton Bearer (ci-dessous).", "GET /api/servers · POST /api/servers/{id}/{start|stop|restart|backup}. Requiere un token Bearer (abajo).", "GET /api/servers · POST /api/servers/{id}/{start|stop|restart|backup}. Erfordert ein Bearer-Token (unten).");
            E("WebApi.Port", "Port:", "Port :", "Puerto:", "Port:");
            E("WebApi.ListenIp", "Listen IP:", "IP d'écoute :", "IP de escucha:", "Lausch-IP:");
            E("WebApi.ListenIpHelp", "127.0.0.1 = this machine only (no admin/firewall needed) — use this if the client is local.\n0.0.0.0 / + = all interfaces (LAN/internet): requires running WGSM as administrator (or a netsh urlacl) and an open inbound firewall port.\nYou can also set a specific machine IP. NB: browsers cannot connect TO 0.0.0.0 — reach the server at 127.0.0.1 or the machine's real IP.", "127.0.0.1 = cette machine uniquement (ni admin ni pare-feu nécessaires) — à utiliser si le client est local.\n0.0.0.0 / + = toutes les interfaces (LAN/internet) : nécessite d'exécuter WGSM en administrateur (ou un netsh urlacl) et un port entrant ouvert dans le pare-feu.\nTu peux aussi indiquer une IP machine spécifique. NB : les navigateurs ne peuvent pas se connecter À 0.0.0.0 — joins le serveur via 127.0.0.1 ou l'IP réelle de la machine.", "127.0.0.1 = solo esta máquina (sin admin ni firewall) — úsalo si el cliente es local.\n0.0.0.0 / + = todas las interfaces (LAN/internet): requiere ejecutar WGSM como administrador (o un netsh urlacl) y un puerto entrante abierto en el firewall.\nTambién puedes indicar una IP concreta de la máquina. Nota: los navegadores no pueden conectarse A 0.0.0.0 — accede al servidor por 127.0.0.1 o la IP real de la máquina.", "127.0.0.1 = nur dieser Rechner (kein Admin/keine Firewall nötig) — nutze dies, wenn der Client lokal ist.\n0.0.0.0 / + = alle Schnittstellen (LAN/Internet): erfordert das Ausführen von WGSM als Administrator (oder ein netsh urlacl) und einen offenen eingehenden Firewall-Port.\nDu kannst auch eine bestimmte Rechner-IP angeben. Hinweis: Browser können sich nicht ZU 0.0.0.0 verbinden — erreiche den Server über 127.0.0.1 oder die echte IP des Rechners.");
            E("WebApi.TokenBearer", "Token (Bearer):", "Jeton (Bearer) :", "Token (Bearer):", "Token (Bearer):");
            E("WebApi.PortalHeader", "Web portal (browser interface)", "Portail web (interface navigateur)", "Portal web (interfaz de navegador)", "Web-Portal (Browser-Oberfläche)");
            E("WebApi.PortalHelp", "Login page + dashboard (start/stop/restart/backup) with accounts and roles. Auth via session cookie (HttpOnly, SameSite=Strict).", "Page de connexion + tableau de bord (start/stop/restart/backup) avec comptes et rôles. Authentification via cookie de session (HttpOnly, SameSite=Strict).", "Página de inicio de sesión + panel (start/stop/restart/backup) con cuentas y roles. Autenticación mediante cookie de sesión (HttpOnly, SameSite=Strict).", "Anmeldeseite + Dashboard (start/stop/restart/backup) mit Konten und Rollen. Authentifizierung über Sitzungscookie (HttpOnly, SameSite=Strict).");
            E("WebApi.EnablePortal", "Enable the web portal", "Activer le portail web", "Activar el portal web", "Web-Portal aktivieren");
            E("WebApi.DonatorFeature", "★ Donator feature.", "★ Fonction donateur.", "★ Función para donantes.", "★ Spender-Funktion.");
            E("WebApi.PortalPort", "Portal port:", "Port du portail :", "Puerto del portal:", "Portal-Port:");
            E("WebApi.Ip", "IP:", "IP :", "IP:", "IP:");
            E("WebApi.PortalPortHelp", "Independent of the API above. Same IP:port as the API = single shared listener. Use \"0.0.0.0\" or \"+\" for all interfaces (LAN/internet) — that requires running WGSM as administrator (or a netsh urlacl) and an open firewall port.", "Indépendant de l'API ci-dessus. Même IP:port que l'API = un seul écouteur partagé. Utilise \"0.0.0.0\" ou \"+\" pour toutes les interfaces (LAN/internet) — cela nécessite d'exécuter WGSM en administrateur (ou un netsh urlacl) et un port ouvert dans le pare-feu.", "Independiente de la API anterior. Misma IP:puerto que la API = un único listener compartido. Usa \"0.0.0.0\" o \"+\" para todas las interfaces (LAN/internet) — eso requiere ejecutar WGSM como administrador (o un netsh urlacl) y un puerto abierto en el firewall.", "Unabhängig von der API oben. Gleiche IP:Port wie die API = ein einziger gemeinsamer Listener. Verwende \"0.0.0.0\" oder \"+\" für alle Schnittstellen (LAN/Internet) — das erfordert das Ausführen von WGSM als Administrator (oder ein netsh urlacl) und einen offenen Firewall-Port.");
            E("WebApi.PortalDonatorFeatureName", "Web portal (authentication + roles)", "Portail web (authentification + rôles)", "Portal web (autenticación + roles)", "Web-Portal (Authentifizierung + Rollen)");
            E("WebApi.WebAccounts", "Web accounts…", "Comptes web…", "Cuentas web…", "Web-Konten…");
            E("WebApi.CookieSecure", "\"Secure\" session cookie (behind an HTTPS proxy)", "Cookie de session « Secure » (derrière un proxy HTTPS)", "Cookie de sesión «Secure» (detrás de un proxy HTTPS)", "„Secure“-Sitzungscookie (hinter einem HTTPS-Proxy)");
            E("WebApi.SecurityBestPractices", "🔒 Security best practices (OWASP):\n• The API is plain HTTP → for internet exposure, put it BEHIND AN HTTPS REVERSE-PROXY (nginx/Caddy/Traefik) that handles TLS; ideally keep listening on 127.0.0.1 and let the proxy talk to the LAN/internet. Then enable \"Secure cookie\".\n• Long random token (\"Generate\" button); never share it and rotate it regularly. (The token is NOT accepted in the URL.)\n• Restrict access at the firewall (only open the port to trusted IPs); add rate-limiting/Fail2ban on the proxy side.\n• Built in: PBKDF2 passwords, HttpOnly+SameSite=Strict cookie sessions, anti-CSRF (Origin check), hardened headers (CSP, nosniff, X-Frame-Options DENY, Permissions-Policy, hidden Server header), brute-force throttle (10 fails/5 min), strict action validation, audit log of logins/actions.\n• Listening outside 127.0.0.1 requires WGSM as administrator (or \"netsh http add urlacl\").", "🔒 Bonnes pratiques de sécurité (OWASP) :\n• L'API est en HTTP clair → pour une exposition sur internet, place-la DERRIÈRE UN REVERSE-PROXY HTTPS (nginx/Caddy/Traefik) qui gère le TLS ; idéalement, garde l'écoute sur 127.0.0.1 et laisse le proxy dialoguer avec le LAN/internet. Active ensuite « Secure cookie ».\n• Jeton long et aléatoire (bouton « Generate ») ; ne le partage jamais et fais-le tourner régulièrement. (Le jeton n'est PAS accepté dans l'URL.)\n• Restreins l'accès au pare-feu (n'ouvre le port qu'à des IP de confiance) ; ajoute du rate-limiting/Fail2ban côté proxy.\n• Intégré : mots de passe PBKDF2, sessions par cookie HttpOnly+SameSite=Strict, anti-CSRF (vérification Origin), en-têtes durcis (CSP, nosniff, X-Frame-Options DENY, Permissions-Policy, en-tête Server masqué), limitation anti-brute-force (10 échecs/5 min), validation stricte des actions, journal d'audit des connexions/actions.\n• Écouter en dehors de 127.0.0.1 nécessite WGSM en administrateur (ou « netsh http add urlacl »).", "🔒 Buenas prácticas de seguridad (OWASP):\n• La API es HTTP sin cifrar → para exponerla a internet, colócala DETRÁS DE UN PROXY INVERSO HTTPS (nginx/Caddy/Traefik) que gestione el TLS; lo ideal es seguir escuchando en 127.0.0.1 y dejar que el proxy hable con la LAN/internet. Después activa «Secure cookie».\n• Token largo y aleatorio (botón «Generate»); no lo compartas nunca y rótalo con regularidad. (El token NO se acepta en la URL.)\n• Restringe el acceso en el firewall (abre el puerto solo a IP de confianza); añade rate-limiting/Fail2ban en el lado del proxy.\n• Incorporado: contraseñas PBKDF2, sesiones por cookie HttpOnly+SameSite=Strict, anti-CSRF (comprobación de Origin), cabeceras reforzadas (CSP, nosniff, X-Frame-Options DENY, Permissions-Policy, cabecera Server oculta), limitación anti-fuerza bruta (10 fallos/5 min), validación estricta de acciones, registro de auditoría de inicios de sesión/acciones.\n• Escuchar fuera de 127.0.0.1 requiere WGSM como administrador (o «netsh http add urlacl»).", "🔒 Sicherheits-Best-Practices (OWASP):\n• Die API läuft über einfaches HTTP → für die Internet-Freigabe HINTER EINEN HTTPS-REVERSE-PROXY (nginx/Caddy/Traefik) setzen, der TLS übernimmt; idealerweise weiter auf 127.0.0.1 lauschen und den Proxy mit LAN/Internet kommunizieren lassen. Dann „Secure cookie“ aktivieren.\n• Langes, zufälliges Token (Schaltfläche „Generate“); niemals weitergeben und regelmäßig rotieren. (Das Token wird NICHT in der URL akzeptiert.)\n• Zugriff an der Firewall beschränken (Port nur für vertrauenswürdige IPs öffnen); Rate-Limiting/Fail2ban auf der Proxy-Seite ergänzen.\n• Integriert: PBKDF2-Passwörter, HttpOnly+SameSite=Strict-Cookie-Sitzungen, Anti-CSRF (Origin-Prüfung), gehärtete Header (CSP, nosniff, X-Frame-Options DENY, Permissions-Policy, verborgener Server-Header), Brute-Force-Drosselung (10 Fehlversuche/5 Min.), strenge Aktionsvalidierung, Audit-Protokoll für Anmeldungen/Aktionen.\n• Das Lauschen außerhalb von 127.0.0.1 erfordert WGSM als Administrator (oder „netsh http add urlacl“).");
            E("WebApi.SaveApply", "Save & apply", "Enregistrer et appliquer", "Guardar y aplicar", "Speichern & anwenden");
            E("WebApi.NeedToken", "Set a token (or click \"Generate\") to enable the API.", "Définis un jeton (ou clique sur « Generate ») pour activer l'API.", "Establece un token (o pulsa «Generate») para activar la API.", "Lege ein Token fest (oder klicke auf „Generate“), um die API zu aktivieren.");
            E("WebApi.NeedAccount", "Create at least one account (\"Web accounts…\") before enabling the portal.", "Crée au moins un compte (« Comptes web… ») avant d'activer le portail.", "Crea al menos una cuenta («Cuentas web…») antes de activar el portal.", "Erstelle mindestens ein Konto („Web-Konten…“), bevor du das Portal aktivierst.");
            E("WebApi.InvalidApiPort", "Invalid API port.", "Port d'API invalide.", "Puerto de la API no válido.", "Ungültiger API-Port.");
            E("WebApi.InvalidPortalPort", "Invalid portal port.", "Port du portail invalide.", "Puerto del portal no válido.", "Ungültiger Portal-Port.");
            E("WebApi.StatusNothing", "✔ Nothing enabled (set a token and/or enable the portal).", "✔ Rien d'activé (définis un jeton et/ou active le portail).", "✔ Nada activado (establece un token o activa el portal).", "✔ Nichts aktiviert (lege ein Token fest und/oder aktiviere das Portal).");
            E("WebApi.StatusPortal", "✔ Started. Web portal: http://{0}:{1}/ (sign in with an account).", "✔ Démarré. Portail web : http://{0}:{1}/ (connecte-toi avec un compte).", "✔ Iniciado. Portal web: http://{0}:{1}/ (inicia sesión con una cuenta).", "✔ Gestartet. Web-Portal: http://{0}:{1}/ (mit einem Konto anmelden).");
            E("WebApi.StatusApi", "✔ API started. Test: GET http://{0}:{1}/api/servers (header Authorization: Bearer <token>).", "✔ API démarrée. Test : GET http://{0}:{1}/api/servers (en-tête Authorization: Bearer <token>).", "✔ API iniciada. Prueba: GET http://{0}:{1}/api/servers (cabecera Authorization: Bearer <token>).", "✔ API gestartet. Test: GET http://{0}:{1}/api/servers (Header Authorization: Bearer <token>).");

            return all;
        }
    }
}
