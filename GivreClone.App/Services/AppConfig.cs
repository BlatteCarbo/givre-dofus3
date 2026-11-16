using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GivreClone.App.Services;

/// <summary>Script Lua ouvert récemment (liste « Récents » de l'éditeur).</summary>
public sealed class RecentLuaScript
{
    public string Path { get; set; } = "";
    public DateTime OpenedAt { get; set; }
}

public sealed class AppConfigData
{
    public bool McpEnabled { get; set; } = true;
    public bool McpAllowActions { get; set; } = true;
    // Contrôle à distance (téléphone) : OFF par défaut. Jeton généré au 1er démarrage ; accès : boucle locale + Tailscale (+ réseau local si RemoteAllowLan).
    public bool RemoteEnabled { get; set; }
    public int RemotePort { get; set; } = 8787;
    public bool RemoteAllowLan { get; set; }
    public string RemoteToken { get; set; } = "";
    /// <summary>Comptes (partie locale du login) que l'automatisation MCP/superviseurs ne connecte ni ne pilote jamais.</summary>
    public string[] McpBlockedLogins { get; set; } = GivreClone.App.Services.Mcp.McpGuards.DefaultBlockedLogins;
    public string Language { get; set; } = "fr";   // fr | en | es
    public bool Configured { get; set; }            // false au 1er lancement -> écran de choix de langue
    public string Theme { get; set; } = "amber";   // accent par defaut (les utilisateurs existants gardent leur choix persiste)
    public bool Sfx { get; set; } = true;         // sons d'interface
    public float SfxVolume { get; set; } = 1f;    // volume des sons d'interface (0..1 ; 1 = pleine amplitude, défaut = comportement historique)
    public bool Ambient { get; set; } = false;    // musique d'ambiance — OFF par défaut (activable dans Réglages)
    public double WindowWidth { get; set; }       // 0 = taille par defaut (1460 x 920) ; restaure au demarrage si l'ecran le permet
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public string Density { get; set; } = "normal";  // compact | normal | large
    // Skin de l'interface: "default" = shell GivreClone (sidebar + pages); "classic" = layout
    // (barre de menu haut + arbre chef/mules à gauche + bandeau statut bas). Réglage PUREMENT visuel : mêmes
    // ViewModels/logique dessous, bascule à chaud dans Paramètres, revert instantané en repassant à "default".
    public string UiSkin { get; set; } = "classic";  // default | un bot tiers — un bot tiers est le défaut (nouveaux profils); un choix déjà persisté prime
    public bool Particles { get; set; } = true;
    public bool SeasonalDecor { get; set; } = true;   // décor de fête (Halloween, Noël) hors clics ; coupé dans Réglages > Effets
    public bool TurboAckOwned { get; set; } = true;   // Turbo MITM : le bot accuse lui-même ses séquences dès leur fin (comme en Socket) et retire l'accusé du client visible
    // Carte interactive : mode, filtres, compact multibot et favoris PERSISTÉS (retrouvés au prochain lancement).
    public string MapInteraction { get; set; } = "act";   // act (déplacement, défaut) | inspect
    public bool MapShowMonsters { get; set; } = true;
    public bool MapShowNpcs { get; set; } = true;
    public bool MapShowPlayers { get; set; } = true;
    public bool MapShowResources { get; set; } = true;
    public bool MapCompact { get; set; }
    public List<MapFavorite> MapFavorites { get; set; } = new();
    public string ExpeditionLast { get; set; } = "";   // dernière sélection globale de secours
    public Dictionary<string, string> ExpeditionOf { get; set; } = new(StringComparer.OrdinalIgnoreCase);   // perso -> ExpeditionState.Serialize()
    public int ExpElixirRank { get; set; }             // élixir uchronique de l'expédition : 0 aucun, 1 mineur, 2 amélioré, 3 majeur
    public long ExpElixirMaxPrice { get; set; }        // prix maximum d'un élixir à l'HDV (0 = ne pas acheter)
    public int ExpElixirStock { get; set; } = 2;       // élixirs achetés d'un coup
    // Mule en un clic (onglet Inventaire), par nom de perso : la mule choisie et les objets jamais donnés.
    public Dictionary<string, string> MuleOf { get; set; } = new();
    public Dictionary<string, List<long>> MuleKeep { get; set; } = new();
    public Dictionary<string, List<long>> ZaapFavorites { get; set; } = new();   // id du perso -> cartes des zaaps favoris
    public bool InteractionGrid { get; set; } = true;   // fenêtre d'interaction : objets en grille d'icônes (sinon liste)
    public bool InventoryGrid { get; set; } = true;     // onglet Inventaire : grille d'icônes (sinon cartes)
    // Panoplies par nom de perso : une entrée = GearSetCodec.Format (« nom|position:gid,… »).
    public Dictionary<string, List<string>> GearSets { get; set; } = new();
    public bool ItemIdOverlay { get; set; } = true;   // mod natif : ID de l'objet à droite de son nom dans les bulles d'info (MITM)
    public bool ModsEnabled { get; set; } = true;   // injecte GivreMods.dll (no-anim/tactique) au lancement d'un client ; OFF = client 100% vanilla
    public bool DofusEnhancedEnabled { get; set; }
    // Anti-inactivité : réglage par personnage (BotSettings.AntiAfkEnabled), keepalive d'orientation dans LaunchService.AntiAfk.
    // Relance auto : si un client Dofus lancé par GivreClone MEURT de façon inattendue (crash instantané au lancement, ou
    // fermeture externe) alors qu'on le croyait « en jeu », on le relance tout seul. ON par défaut (le botting attend un
    // fonctionnement non-surveillé). Anti-boucle : au-delà de 3 relances dans une fenêtre de 3 min, on suspend et on prévient.
    public bool AutoRelaunch { get; set; } = true;
    // Archimonstres : recevoir (ou non) le MP Discord perso quand un de MES persos trouve un archimonstre. OFF = le
    // client envoie `no_dm` dans le signalement -> le backend NE me DM PAS, mais la détection remonte QUAND MÊME dans le
    // salon public + la vue admin (le flag ne coupe que le MP au finder). ON par défaut. N'affecte pas l'alerte en jeu.
    public bool ArchDiscordDm { get; set; } = true;
    // Rich Presence Discord (« Joue à GivreClone » : nom + libellé + chrono + boutons Discord/site). Purement cosmétique.
    // ⚠️ Révèle sur Discord que tu utilises GivreClone -> désactivable. Nécessite un Application ID renseigné (sinon no-op).
    public bool RichPresence { get; set; } = true;
    // Mode Recording/Stream : floute toute PII dans l'UI (identifiant, personnage, serveur, IP de sortie, HWID)
    // pour enregistrer une démo/stream sans révéler d'infos de compte. Purement visuel (n'affecte aucune logique).
    public bool StreamMode { get; set; }
    /// <summary>Mode d'affichage (divulgation progressive) : standard | expert | dev. « dev » retombe sur « expert » sans licence développeur (voir AppConfig.EffectiveUiMode).</summary>
    public string UiMode { get; set; } = "standard";
    /// <summary>Mode discret : masque les identifiants sensibles dans l'UI (utile en stream/capture).</summary>
    public bool StreamerPrivacy { get; set; } = false;
    // Capture de découverte (diagnostic) : dumpe dans transport-*.log l'arbre de champs des messages NON identifiés
    // par le mapping (nom inconnu) et des messages d'échange/vente, pour relever les codes + numéros de champ réels
    // quand on reverse une nouvelle interface. OFF par défaut (le corps des messages est écrit en clair, reste local).
    public bool PacketDump { get; set; }
    /// <summary>Au-delà de ce nombre de MES tours dans un combat (combat sans fin où plus personne ne se touche, qui attire les modérateurs), le moteur cesse de jouer : le combat est laissé en pause pour que tu reprennes la main. 0 = jamais.</summary>
    public int PauseFightAfterTurns { get; set; } = 150;
    /// <summary>Abandonne le combat au-delà de ce nombre de tours. 0 = jamais (défaut : l'abandon doit être un choix explicite, un combat abandonné est perdu).</summary>
    public int AbandonFightAfterTurns { get; set; }
    /// <summary>Garde anti-suspension Turbo : au-delà de ce nombre de MES tours dans un même combat, le reste du combat tourne à allure humaine. Les 4 combats Turbo de 30+ tours observés (06-07/11) ont tous fini en suspension de 5 min. 0 = désactivé.</summary>
    public int TurboMaxTurnsPerFight { get; set; } = 25;
    /// <summary>Garde anti-suspension Turbo : au-delà de ce nombre de MES tours sur 10 minutes glissantes (tous combats confondus), allure humaine. 0 = désactivé.</summary>
    public int TurboMaxTurnsPer10Min { get; set; } = 150;
    /// <summary>Garde anti-suspension Turbo : au-delà de ce nombre de MES tours sur 60 secondes glissantes (tous combats confondus), allure humaine jusqu'à ce que la fenêtre redescende. Session coupée 50 ms après une fin de combat avec 34 puis 31 tours/min (07/11) ; p99 sans coupure = 25. 0 = désactivé.</summary>
    public int TurboMaxTurnsPerMinute { get; set; } = 20;   // 09/11 : Eliobot banni 3 fois a 22-40 tours/60 s
    /// <summary>Pause aléatoire (secondes) entre la fin d'un combat et l'engagement suivant. Les 3 suspensions de 5 min observées
    /// (04-05/11) ont toutes suivi 6 à 8 combats très courts enchaînés à 2-6 s d'intervalle. 0/0 = aucune pause.</summary>
    public int FightPauseMinSeconds { get; set; } = 1;
    public int FightPauseMaxSeconds { get; set; } = 2;
    // Capture EXHAUSTIVE : dumpe TOUS les messages, y compris ceux que le mapping nomme deja.
    // Necessaire apres un patch Dofus : le filtre normal masque les messages « connus », or apres une
    // rotation des codes ils sont nommes a TORT — le mauvais pin devient donc invisible dans le journal.
    // Le filtre en combat dependait de _inFight, lui-meme arme par un message dont le pin peut etre
    // casse : la capture dependait du mapping qu'elle sert justement a reparer. Ce drapeau coupe cette
    // boucle. Verbeux (plusieurs Mo/min) : reserve aux sessions de re-liage.
    public bool PacketDumpAll { get; set; }
    // Chemin EXPLICITE de Dofus.exe choisi par l'utilisateur (installation hors emplacement standard). "" = auto-détection
    // (release.json Zaap, sinon %LOCALAPPDATA%\Ankama\Dofus-dofus3). Enregistré une fois via le sélecteur au 1er échec ;
    // auto-réparant : si le chemin sauvegardé n'existe plus, on retombe sur l'auto-détection et le sélecteur reparaît.
    public string DofusExePath { get; set; } = "";
    public string BankDeposit { get; set; } = "all";
    // Éditeur Lua : scripts récemment ouverts (le plus récent en tête, 12 max) + hauteur de l'éditeur dans l'onglet (px).
    public List<RecentLuaScript> RecentLuaScripts { get; set; } = new();
    public double LuaEditorHeight { get; set; } = 460;   // type de dépôt banque : all (tout) | existing (déjà présents) | visible (objets du sac)
    // Onglet Console d'un bot : false = journal lisible (événements, connexion, script), true = flot technique (diagnostic).
    public bool ConsoleTechnical { get; set; }

    // Licence Ikuware : cle chiffree DPAPI (jamais en clair) + reconnexion auto (case "rester connecte")
    public string LicenseProtected { get; set; } = "";
    public bool AutoLogin { get; set; }
    /// <summary>Clé de signature Ed25519 CLIENTE (semence 32 o base64, protégée DPAPI) pour les uploads télémetrie
    /// (TOFU côté serveur). Générée une fois, doit rester stable. Voir LicenseService.ClientKey().</summary>
    public string ClientSigningKey { get; set; } = "";

    // échelle UI selon la densité
    public double DensityScale => Density switch
    {
        "compact" => 0.88,
        "large" => 1.15,
        _ => 1.0,
    };

    /// <summary>Clients MITM allégés sans injection : EcoQoS, priorité sous la normale, purge du working set hors premier plan (ClientLightening). Les réglages « client léger » de Better Dofus suivent aussi ce drapeau.</summary>
    public bool LightenBotClients { get; set; } = true;

    /// <summary>Seuil de Working Set (Mo) au-delà duquel un client Dofus est éligible à la purge EmptyWorkingSet. 0 = désactivé.</summary>
    public int MemoryTrimClientMb { get; set; } = 1536;

    /// <summary>Seuil de Working Set (Mo) au-delà duquel l'application GivreClone est éligible à la purge EmptyWorkingSet. 0 = désactivé.</summary>
    public int MemoryTrimAppMb { get; set; } = 600;
}

// réglages persistés dans %APPDATA%/GivreClone/settings.json
public static class AppConfig
{
    private static readonly string Dir =
        MZP19LocalClone.DataRoot;
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static AppConfigData Current { get; private set; } = new();

    /// <summary>Levé (thread appelant, normalement l'UI) après un changement de UiMode / StreamerPrivacy enregistré.</summary>
    public static event Action? UiPreferencesChanged;
    public static void NotifyUiPreferencesChanged() => UiPreferencesChanged?.Invoke();

    /// <summary>Mode d'affichage effectif : valeur inconnue -> standard ; « dev » sans licence développeur -> expert.</summary>
    public static string EffectiveUiMode => Current.UiMode switch
    {
        "expert" => "expert",
        "dev" => LicenseService.IsDeveloper ? "dev" : "expert",
        _ => "standard",
    };

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = (AppConfigData?)JsonSerializer.Deserialize(File.ReadAllText(FilePath), typeof(AppConfigData), AppJsonContext.Default) ?? new();
        }
        catch { Current = new(); }
    }

    public static void Save()
    {
        try
        {
            AtomicJson.Write(FilePath, JsonSerializer.Serialize(Current, typeof(AppConfigData), AppJsonContext.Default));
        }
        catch { /* best-effort */ }
    }
}
