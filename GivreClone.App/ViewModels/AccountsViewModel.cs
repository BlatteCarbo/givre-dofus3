using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Transport;
using GivreClone.App.Services.Transport.Socket;
using GivreClone.Engine.Accounts;
using GivreClone.Engine.Proxies;
using GivreClone.Protocol;
using GivreLauncher.Core.Ankama;

namespace GivreClone.App.ViewModels;

/// <summary>Session SOCKET en cours d'un compte, publiée par sa boucle de session : son CTS PROPRE (lié à celui du compte) et son
/// numéro. State (Interlocked) : 0 vivante, 1 coupée à la demande (« Reconnecter »), 2 close. Le passage 0 -> 1 (UI) et
/// 0/1 -> 2 (boucle) est atomique : une coupure demandée APRÈS la fin de la session n'est jamais prise pour la suivante.</summary>
internal sealed class SocketSessionHandle(CancellationTokenSource cts, int seq)
{
    public readonly CancellationTokenSource Cts = cts;
    public readonly int Seq = seq;
    public int State;
}

/// <summary>Suspension du script d'un bot socket : coupure inattendue (reconnexion automatique), coupure demandée
/// (« Reconnecter »), session parquée (déconnexion imposée par le serveur, aucune reconnexion automatique).</summary>
public enum SocketPauseKind { Lost, Manual, Parked }

/// <summary>Une carte de compte : compte + proxy assignable + etat live (IP de sortie, perso, serveur, statut).</summary>
/// <summary>Comment un compte a été routé AU LANCEMENT : fige le droit dont sa connexion dépend, indépendamment
/// du dropdown proxy qui peut changer après coup. Sert à la révocation live (couper si le droit est retiré).</summary>
public enum LaunchRouting { None, Adapter, Proxy, Relay }   // Relay : droit vérifié par le serveur du relais (pas de droit local)

public partial class AccountRow : ObservableObject
{
    public Account Model { get; }
    public ObservableCollection<ProxyEntry> Proxies { get; }
    public string Login => Model.Login;
    public string SourceLabel => Model.Source == AuthSource.Browser ? Loc.T("L.Accounts.SrcBrowser") : Loc.T("L.Accounts.SrcAnkama");
    public string? Label => Model.DisplayName;
    public bool HasLabel => !string.IsNullOrWhiteSpace(Model.DisplayName);

    // Génération du DERNIER lancement de ce compte : la suite d'un lancement dépassé (Stop puis relance pendant son
    // await) ne doit ni adopter son pid ni écrire Running/Pid par-dessus le lancement plus récent.
    internal int LaunchGen;

    [ObservableProperty] public partial ProxyEntry? SelectedProxy { get; set; }
    [ObservableProperty] public partial string Status { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanReconnect), nameof(ShowStop), nameof(ModeIsSocket), nameof(ShowSocketBtn), nameof(CanCancelResume))] public partial bool Running { get; set; }
    /// <summary>Instant (UTC) de mise en route du bot : base de uptime_s (télémétrie « bots »). Conservé pendant les reconnexions
    /// (le bot reste Running) ; null à l'arrêt.</summary>
    public DateTimeOffset? RunningSince { get; private set; }
    partial void OnRunningChanged(bool value) { RunningSince = value ? DateTimeOffset.UtcNow : null; RefreshUptime(); }

    /// <summary>Durée connectée affichée sur la carte (rafraîchie par le poll) : une reconnexion en place garde Running,
    /// donc la durée continue depuis le lancement initial.</summary>
    [ObservableProperty] public partial string UptimeText { get; set; } = "";
    public void RefreshUptime(DateTimeOffset? now = null) => UptimeText = FormatUptime(RunningSince, now ?? DateTimeOffset.UtcNow);
    public static string FormatUptime(DateTimeOffset? since, DateTimeOffset now)
    {
        if (since is not { } s) return "";
        var d = now - s; if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        return d.TotalHours >= 1 ? $"{(int)d.TotalHours} h {d.Minutes:00}" : $"{d.Minutes} min {d.Seconds:00}";
    }

    /// <summary>Derniers motifs de déconnexion (récent en tête, 10 max), horodatés en heure locale.</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> DisconnectHistory { get; } = new();
    public string DisconnectHistoryText => DisconnectHistory.Count == 0 ? "" : string.Join("\n", DisconnectHistory);
    public bool HasDisconnectHistory => DisconnectHistory.Count > 0;
    public void RecordDisconnect(string reason, DateTimeOffset? at = null)
    {
        string stamp = (at ?? DateTimeOffset.Now).ToLocalTime().ToString("dd/MM HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        DisconnectHistory.Insert(0, $"{stamp} · {reason}");
        while (DisconnectHistory.Count > 10) DisconnectHistory.RemoveAt(DisconnectHistory.Count - 1);
        OnPropertyChanged(nameof(DisconnectHistoryText));
        OnPropertyChanged(nameof(HasDisconnectHistory));
    }
    [ObservableProperty] public partial string ExitIp { get; set; } = "-";
    [ObservableProperty] public partial string Character { get; set; } = "-";   // "nom (niveau)" pour la carte compacte
    [ObservableProperty] public partial string? CharacterName { get; set; }      // nom seul
    /// <summary>Dernier personnage connu de ce compte (jamais effacé par l'arrêt, contrairement à CharacterName) : la
    /// reconnexion (Socket) sait quel perso resélectionner.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanReconnect), nameof(ShowSocketBtn))] public partial string? LastCharacter { get; set; }
    partial void OnCharacterNameChanged(string? value) { if (!string.IsNullOrEmpty(value)) LastCharacter = value; }
    [ObservableProperty] public partial int? Level { get; set; }                 // niveau seul
    [ObservableProperty] public partial string? Breed { get; set; }              // classe (nom)
    [ObservableProperty] public partial int BreedId { get; set; }                // id de classe Dofus (1-20) : sorts du picker Combat
    [ObservableProperty] public partial string? Gender { get; set; }             // sexe (nom)
    [ObservableProperty] public partial Avalonia.Media.Imaging.Bitmap? ClassIcon { get; set; }  // tete de classe (avatar)
    // Index bruts de classe/sexe du perso en jeu : mémorisés dans son planning (icône de la ligne « hors-ligne · planning »).
    public int? BreedIndex { get; set; }
    public int? GenderIndex { get; set; }
    // Badge « Planning » (page Comptes) : un perso de ce compte est en connexion auto. Infobulle sans nom (mode stream).
    [ObservableProperty] public partial bool PlanningManaged { get; set; }
    [ObservableProperty] public partial string PlanningTip { get; set; } = "";
    [ObservableProperty] public partial long? MapId { get; set; }                // map courante
    [ObservableProperty] public partial string? MapCoords { get; set; }          // position monde "x, y" (via table)
    [ObservableProperty] public partial string Server { get; set; } = "-";

    /// <summary>Activité de HAUT NIVEAU du bot (Combat / Récolte / Déplacement / Jeu / Connexion), dérivée de l'état MITM
    /// (LaunchService.ActivityOf) et poussée par AccountsViewModel. Affichée dans l'arbre « » à la place
    /// du compteur de paquets. Offline tant que le bot n'est pas en jeu.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivityText), nameof(ActivityBrush), nameof(ActivityIsMoving), nameof(ActivityIsDot), nameof(IsDisconnected), nameof(CanReconnect), nameof(ShowSocketBtn))]
    public partial BotActivity Activity { get; set; } = BotActivity.Offline;

    /// <summary>En déplacement -> l'arbre un bot tiers montre une flèche (au lieu de la pastille ronde). Sinon pastille.</summary>
    public bool ActivityIsMoving => Activity == BotActivity.Moving;
    public bool ActivityIsDot => Activity != BotActivity.Moving;
    /// <summary>Session de jeu perdue, bot gardé (Running + Pid conservés) : MITM (kick serveur…) alors que Dofus.exe tourne
    /// encore, ou session SOCKET parquée après une déconnexion imposée par le serveur.</summary>
    public bool IsDisconnected => Activity == BotActivity.Disconnected;

    // ---------- « Reconnecter » (onglet Bots / carte compte) ----------
    /// <summary>Une reconnexion MANUELLE de ce compte est en cours (pré-requis, relance du client, session socket coupée ou
    /// réveillée, perso pas encore de retour en jeu) : bouton grisé (anti double-clic), gardé à sa place. Tenu par l'onglet Bots.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowStop), nameof(ShowSocketBtn), nameof(CanReconnect), nameof(CanCancelResume))] public partial bool ReconnectPending { get; set; }
    /// <summary>Bouton « Arrêter » de la carte compte : session en cours, ou reconnexion manuelle en attente (compte hors ligne :
    /// « Arrêter » annule seulement la relance programmée).</summary>
    public bool ShowStop => Running || ReconnectPending;
    /// <summary>Bouton « Socket » de la carte compte : compte hors ligne, sans « Reconnecter » à proposer. « Reconnecter »
    /// (<see cref="CanReconnect"/>) prend SA place (même emplacement de largeur fixe) : en session à côté d'« Arrêter », ou hors
    /// ligne après une déconnexion imposée par le serveur à côté de « Lancer » (toujours proposé).</summary>
    public bool ShowSocketBtn => !ShowStop && !CanReconnect;
    /// <summary>SOCKET : reconnexion automatique programmée (attente du backoff, même pid) -> « Reconnecter » part tout de suite.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanReconnect), nameof(ShowSocketBtn), nameof(CanCancelResume))] public partial bool SocketBackoff { get; set; }
    /// <summary>Bouton « Annuler la reprise » : visible UNIQUEMENT pendant une reprise en attente (attente du backoff de la
    /// reconnexion automatique socket, ou reconnexion manuelle en attente d'un compte hors ligne).</summary>
    public bool CanCancelResume => SocketBackoff || (ReconnectPending && !Running);
    /// <summary>MITM : compte arrêté après une déconnexion IMPOSÉE par le serveur (client « Déconnecté » dont la fenêtre Dofus a
    /// été fermée ensuite), ou dont la relance « Reconnecter » en place a échoué -> « Reconnecter » relance le compte (dernier
    /// perso joué, script gardé). Levé au lancement suivant. Un bot SOCKET déconnecté par le serveur n'est plus arrêté : sa
    /// session est PARQUÉE (Running + même pid, <see cref="IsDisconnected"/>).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanReconnect), nameof(ShowSocketBtn))] public partial bool ServerKickedOffline { get; set; }
    /// <summary>Bouton « Reconnecter » de la carte compte : reconnexion manuelle en cours (grisé, à sa place), MITM « Déconnecté »
    /// (Dofus.exe ouvert), socket parqué après une déconnexion par le serveur (« Déconnecté ») ou en attente de reconnexion, ou
    /// compte MITM hors ligne <see cref="ServerKickedOffline"/> (dernier perso connu requis).</summary>
    public bool CanReconnect => ReconnectPending || (Running ? IsDisconnected || SocketBackoff : ServerKickedOffline && !string.IsNullOrEmpty(LastCharacter));
    // Réveil de l'attente socket (backoff automatique ou session parquée) : publié par la boucle de session (thread de fond) ;
    // son AsyncState = numéro de la session terminée.
    internal TaskCompletionSource<bool>? SocketWake;
    // « Reconnecter » EN PLACE en cours (thread UI) : bot gardé dans l'onglet Bots, bouton grisé jusqu'au retour en jeu (ou
    // ScriptResumeMaxMs après ReconnectInPlaceAt, TickCount64). ReconnectSeq = numéro de la session socket coupée / réveillée.
    internal bool ReconnectInPlace;
    internal long ReconnectInPlaceAt;
    internal int ReconnectSeq;
    // SOCKET : session parquée (déconnexion imposée par le serveur, aucune reconnexion automatique). Thread UI.
    internal bool SocketParked;
    // SOCKET : session EN COURS (publiée par la boucle de session) et numéro de la dernière session ouverte (Interlocked).
    internal SocketSessionHandle? SocketSession;
    internal int SocketSessionSeq;

    /// <summary>Libellé clair de l'activité — sans « En » (« Combat », « Récolte », « Déplacement », « Jeu »…).</summary>
    public string ActivityText => Loc.T(Activity switch
    {
        BotActivity.Disconnected => "L.Activity.Disconnected",
        BotActivity.Dead => "L.Activity.Dead",
        BotActivity.Combat => "L.Activity.Combat",
        BotActivity.Gathering => "L.Activity.Gather",
        BotActivity.Moving => "L.Activity.Move",
        BotActivity.InGame => "L.Activity.InGame",
        BotActivity.Connecting => "L.Activity.Connecting",
        _ => "L.Activity.Offline",
    });

    // Couleurs SÉMANTIQUES de l'activité (pas un accent -> brushes fixes OK) : rouge combat, vert récolte, bleu
    // déplacement, ambre connexion, gris repos. Miroir des pastilles de présence d'équipe.
    private static readonly Avalonia.Media.IBrush _actRed = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#F0533B"));
    private static readonly Avalonia.Media.IBrush _actGreen = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#3BD17E"));
    private static readonly Avalonia.Media.IBrush _actBlue = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#5AA6FF"));
    private static readonly Avalonia.Media.IBrush _actAmber = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#E6B24C"));
    private static readonly Avalonia.Media.IBrush _actGrey = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#9A9A9A"));
    private static readonly Avalonia.Media.IBrush _actPurple = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#B36BE8"));   // mort (violet) — distinct du combat rouge
    /// <summary>Couleur de la pastille/texte d'activité (sémantique).</summary>
    public Avalonia.Media.IBrush ActivityBrush => Activity switch
    {
        BotActivity.Disconnected => DangerBrush(),   // session perdue : rouge « danger » du thème (skin comprise)
        BotActivity.Dead => _actPurple,     // mort / résurrection (violet)
        BotActivity.Combat => _actRed,      // rouge
        BotActivity.Gathering => _actAmber, // doré (récolte) — distinct du vert « en jeu »
        BotActivity.Moving => _actBlue,     // bleu
        BotActivity.InGame => _actGreen,    // vert : en jeu, prêt (pastille « en ligne »)
        _ => _actGrey,                      // Connexion / Hors ligne
    };
    // DangerBrush du thème actif; repli sur le rouge sémantique.
    private static Avalonia.Media.IBrush DangerBrush()
        => Avalonia.Application.Current is { } app && app.TryGetResource("DangerBrush", null, out var r) && r is Avalonia.Media.IBrush b ? b : _actRed;

    /// <summary>Où arriver au lancement : 0 = choix serveur, 1 = choix perso, 2 = direct en jeu. Propre au compte
    /// (SelectedIndex du ComboBox). Consommé tel quel par LaunchRequest -> Zaap + GameLauncher.</summary>
    [ObservableProperty] public partial int AutoConnectType { get; set; }

    /// <summary>Mode de connexion MÉMORISÉ du compte (celui du dernier lancement manuel : bouton Lancer = MITM, bouton
    /// Socket = Socket). C'est lui que suivent les lancements automatiques (planning, Lancer tout, équipe, relance). Affiché
    /// par la pastille MITM / Socket de la carte (les boutons, eux, gardent toujours le même style).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ModeIsSocket))] public partial bool IsSocketMode { get; set; }

    /// <summary>Bannissement Ankama mémorisé (voir <see cref="GivreClone.Engine.Accounts.BanGate"/>) : pastille « Banni » sur la carte.</summary>
    public bool IsBanned => GivreClone.Engine.Accounts.BanGate.IsBanned(Model);
    /// <summary>Heure locale de la reprise automatique programmée après un bannissement temporaire (null = aucune).</summary>
    public DateTime? BanResumeAt { get; private set; }
    public void SetBanResume(DateTime? at) { BanResumeAt = at; RefreshBan(); }
    public string BanLabel => (Model.BanEnd.Length > 0 ? Loc.Fmt("L.Accounts.BannedUntil", Model.BanEnd) : Loc.T("L.Accounts.Banned"))
        + (BanResumeAt is { } r ? Loc.Fmt("L.Accounts.BanResumeAt", r.ToString("HH:mm")) : "");
    public void RefreshBan() { OnPropertyChanged(nameof(IsBanned)); OnPropertyChanged(nameof(BanLabel)); }
    public int? Pid { get; set; }

    // Session Socket (mode clientless) : le compte apparaît en bot avec un pid SYNTHÉTIQUE (aucun Dofus.exe). Ce drapeau
    // garde les chemins spécifiques MITM (kill de process, surveillance crash, télémétrie serveur) de s'appliquer dessus.
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ModeIsSocket))] public partial bool IsSocketSession { get; set; }
    /// <summary>Pastille de mode de la carte : mode de la session en cours si le compte est lancé, sinon mode mémorisé.</summary>
    public bool ModeIsSocket => Running ? IsSocketSession : IsSocketMode;
    internal System.Threading.CancellationTokenSource? SocketCts;
    /// <summary>Routage figé au lancement (voir <see cref="LaunchRouting"/>) — pour la révocation live du droit.</summary>
    public LaunchRouting Routing { get; set; }
    /// <summary>Route du proxy du SERVEUR DE JEU figée au lancement (None = même route que le compte).</summary>
    public LaunchRouting GameRouting { get; set; }

    public Action<AccountRow>? ProxyAssigned;
    /// <summary>Persiste un réglage du compte (autre que le proxy) — sans le toast "proxy assigné".</summary>
    public Action<AccountRow>? SettingsChanged;
    private readonly bool _ready;

    public AccountRow(Account model, ObservableCollection<ProxyEntry> proxies, string status)
    {
        Model = model;
        Proxies = proxies;
        Status = status;
        // Aucun proxy assigne -> on selectionne explicitement "Sans proxy" (au lieu du placeholder, non deselectionnable).
        // Proxy assigné mais supprimé depuis -> AUCUNE sélection (placeholder « Assigner un proxy ») : le compte ne part pas
        // tant qu'un proxy n'est pas choisi (afficher « Sans proxy » laisserait croire à un choix fait).
        SelectedProxy = model.ProxyId is null ? ProxyEntry.None : proxies.FirstOrDefault(p => p.Id == model.ProxyId);
        AutoConnectType = model.AutoConnectType;
        IsSocketMode = model.Mode == ConnectionMode.Socket;
        _ready = true;
    }

    partial void OnIsSocketModeChanged(bool value)
    {
        if (!_ready) return;
        Model.Mode = value ? ConnectionMode.Socket : ConnectionMode.Mitm;
        SettingsChanged?.Invoke(this);
    }

    partial void OnSelectedProxyChanged(ProxyEntry? value)
    {
        if (!_ready || _restoring) return;
        // null = le proxy choisi a disparu de la liste (supprimé, agent retiré du portail) : PAS un choix de l'utilisateur.
        // On garde son identifiant -> lancement refusé tant qu'un autre proxy n'est pas choisi (jamais l'IP réelle en
        // silence) ; s'il revient (même identifiant), le compte le retrouve (RestoreProxy).
        if (value is null) return;
        Model.ProxyId = value.IsNone ? null : value.Id;   // "Sans proxy" -> aucun proxy
        ProxyAssigned?.Invoke(this);
    }

    private bool _restoring;

    /// <summary>Réaffiche le proxy mémorisé revenu dans la liste, sans le traiter comme une nouvelle assignation.</summary>
    public void RestoreProxy(ProxyEntry p)
    {
        _restoring = true;
        try { SelectedProxy = p; } finally { _restoring = false; }
    }

    partial void OnAutoConnectTypeChanged(int value)
    {
        if (!_ready) return;
        Model.AutoConnectType = value;
        SettingsChanged?.Invoke(this);
    }

    /// <summary>Le login/libellé du modèle a changé (édition) -> rafraîchit la carte sans reconstruire la liste.</summary>
    public void RaiseIdentityChanged()
    {
        OnPropertyChanged(nameof(Login));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(HasLabel));
    }
}

/// <summary>
/// Onglet Comptes : cartes de compte (une par compte), chacune assignant un PROXY (depuis l'onglet Proxys) et
/// affichant l'IP de sortie / perso / serveur / statut. Lancement individuel + Lancer tout / Arreter tout.
/// Auth keydata Ankama OU navigateur ; le transport (GivreKama + HandshakeRelay) est fail-closed (anti-fuite).
/// </summary>
public partial class AccountsViewModel : ViewModelBase, IShieldCodePrompt
{
    private readonly LaunchService _launch = Stores.Launch;   // instance PARTAGEE (l'onglet Bots consomme le meme flux)
    private readonly DispatcherTimer _poll;

    public ObservableCollection<AccountRow> Accounts { get; } = new();
    public ObservableCollection<ProxyEntry> Proxies { get; } = new();

    [ObservableProperty] public partial string NewLogin { get; set; } = "";
    [ObservableProperty] public partial string NewPassword { get; set; } = "";
    [ObservableProperty] public partial string NewAccountLabel { get; set; } = "";
    // Proxy du formulaire d'ajout : le login NAVIGATEUR (WebAuth) passe PAR ce proxy -> pas de fuite d'IP réelle
    // pendant l'auth Ankama. "Sans proxy" (ProxyEntry.None) = IP réelle exposée, assumée.
    [ObservableProperty] public partial ProxyEntry? SelectedNewProxy { get; set; } = ProxyEntry.None;

    [ObservableProperty] public partial bool ShieldVisible { get; set; }
    [ObservableProperty] public partial string ShieldLogin { get; set; } = "";
    [ObservableProperty] public partial string ShieldCode { get; set; } = "";
    private TaskCompletionSource<string?>? _shieldTcs;

    // Modal de choix SERVEUR -> PERSONNAGE (mode Socket « écran de choix… »), en 2 étapes comme le vrai client. Données =
    // liste de l'étape A de la connexion (serveurs + leurs persos : nom, classe, sexe, niveau, dernière connexion).
    [ObservableProperty] public partial bool ServerPickVisible { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ServerPickOnServers), nameof(ServerPickOnChars))]
    public partial int ServerPickStep { get; set; }   // 0 = serveurs, 1 = personnages du serveur _pickServerId
    public bool ServerPickOnServers => ServerPickStep == 0;
    public bool ServerPickOnChars => ServerPickStep == 1;
    [ObservableProperty] public partial string ServerPickTitle { get; set; } = "";
    [ObservableProperty] public partial string ServerPickSubtitle { get; set; } = "";
    [ObservableProperty] public partial string ServerPickLogin { get; set; } = "";
    [ObservableProperty] public partial string ServerPickSearch { get; set; } = "";
    [ObservableProperty] public partial bool ServerPickShowSearch { get; set; }   // recherche seulement quand la liste est longue
    public ObservableCollection<SocketServerCard> ServerPickServers { get; } = new();
    public ObservableCollection<SocketCharCard> ServerPickChars { get; } = new();
    private IReadOnlyList<ConnServer> _pickSource = Array.Empty<ConnServer>();
    private int _pickServerId;          // serveur affiché à l'étape « personnages »
    private bool _pickSearchMute;       // remise à zéro de la recherche SANS reconstruction en double
    private TaskCompletionSource<(int ServerId, string? Character)?>? _serverPickTcs;
    private readonly SemaphoreSlim _serverPickGate = new(1, 1);   // un seul modal de choix à la fois (Lancer tout en Socket)

    // Modal de lancement (montre la progression : passage par le proxy, auth, injection...)
    [ObservableProperty] public partial bool LaunchModalVisible { get; set; }
    [ObservableProperty] public partial string LaunchModalLogin { get; set; } = "";
    [ObservableProperty] public partial string LaunchModalProxy { get; set; } = "";
    [ObservableProperty] public partial string LaunchModalStep { get; set; } = "";
    [ObservableProperty] public partial bool LaunchModalBusy { get; set; }
    [ObservableProperty] public partial bool LaunchModalOk { get; set; }
    [ObservableProperty] public partial bool LaunchModalFailed { get; set; }

    public bool HasAccounts => Accounts.Count > 0;

    /// <summary>Droit serveur "multi" actif -> lancer plus d'un compte à la fois est autorisé. Sinon : bandeau d'info +
    /// refus au point d'action + coupe les comptes en trop si le droit est retiré en cours de session.</summary>
    public bool CanMulti => LicenseService.HasPermission(LicenseService.Perm.Multi);
    /// <summary>La licence du produit historique ne s'applique pas au clone local.
    /// Le contrôle indépendant de session TEST demeure obligatoire au lancement.</summary>
    public bool ShowLegacyMultiLicenseNotice => !MZP19LocalClone.Enabled && !CanMulti;
    /// <summary>Droit serveur « socket » : connexion sans Dofus.exe. Le MITM reste inclus de base.</summary>
    public bool CanSocket => LicenseService.HasPermission(LicenseService.Perm.Socket);
    /// <summary>Infobulle du bouton Socket (visible même grisé) : explique le mode, ou POURQUOI il est indisponible (licence non
    /// connectée ≠ droit « socket » absent).</summary>
    public string SocketButtonTip => Loc.T(!LicenseService.IsAuthorized ? "L.Perm.SocketNoSessionTip"
                                         : CanSocket ? "L.Perm.SocketTip" : "L.Perm.SocketLockedTip");

    // Stats de capture MITM par PID (alimentees depuis les threads du relais -&gt; ConcurrentDictionary + Interlocked).
    private sealed class PidStats { public long Total; public long Recognized; public string? Character; public int? Level; public string? Breed; public int BreedId; public int? BreedIndex; public int? GenderIndex; public string? Gender; public long MapId; public int? MapX; public int? MapY; public string? Server; }
    private readonly ConcurrentDictionary<uint, PidStats> _pidStats = new();

    // Surveillance de LIVENESS des clients lancés : pid -> Process observé. Process.Exited détecte un crash instantané
    // (le jeu se lance puis meurt) OU une fermeture externe (hors app) -> OnClientExited. Retiré+disposé sur arrêt
    // manuel (StopCore) et à la sortie, pour distinguer une mort ATTENDUE (Stop) d'une INATTENDUE (crash).
    private readonly ConcurrentDictionary<int, Process> _watched = new();
    // « Reconnecter » MITM EN PLACE : anciens pid dont le client est en cours de relance (tué au point de validation, le
    // nouveau client pas encore adopté). Thread UI. Leurs trames tardives / leur Process.Exited sont ignorés.
    private readonly Dictionary<int, AccountRow> _relaunching = new();   // pid de l'ANCIEN client -> ligne qui le relance
    /// <summary>Relance « Reconnecter » en place en cours sur la session de ce compte (thread UI).</summary>
    internal bool IsRelaunching(AccountRow r) => r.Pid is int p && RelaunchOwnedBy(p, r);
    // Ce pid est-il l'ancien client de CETTE ligne en relance en place ? (jamais vrai pour un autre compte qui aurait hérité du pid)
    private bool RelaunchOwnedBy(int pid, AccountRow row) => _relaunching.TryGetValue(pid, out var o) && ReferenceEquals(o, row);
    // Budget anti-boucle de relance auto, PAR COMPTE (id) : (nb dans la fenêtre, début de fenêtre). Au-delà de
    // RelaunchMax relances en RelaunchWindowMs, on suspend la relance auto de ce compte et on prévient (crash-loop).
    private readonly ConcurrentDictionary<string, (int Count, long WindowStart)> _relaunchBudget = new();
    private const int RelaunchMax = 3;
    private const long RelaunchWindowMs = 180_000;

    public AccountsViewModel()
    {
        WireAccountFilters();
        SyncProxies();
        Reload();
        WireMulti();
        CharacterCreation = new CharacterCreationTabViewModel(this);
        _launch.PacketDecoded += OnPacketDecoded;
        _launch.CharacterIdentified += OnCharacterIdentified;
        _launch.CharacterLeveledUp += OnCharacterLeveledUp;
        _launch.MapChanged += OnMapChanged;
        _launch.ServerIdentified += OnServerIdentified;
        // Activité (Combat / Récolte / Déplacement / Jeu) : MAJ IMMÉDIATE sur les fronts que le moteur émet déjà (le poll
        // 2,5 s ci-dessous fait le reste : début de récolte, connexion->jeu, expiration du déplacement).
        _launch.FightStarted += ApplyActivity;
        _launch.FightEnded += ApplyActivity;
        _launch.SelfMoved += (pid, _) => ApplyActivity(pid);
        _launch.InteractiveUseEnded += (pid, _) => ApplyActivity(pid);
        _launch.InteractiveUseFailed += (pid, _) => ApplyActivity(pid);
        _launch.ActivityChanged += ApplyActivity;   // fronts fin-de-déplacement (jsq) / début-de-récolte -> MAJ immédiate
        _launch.ArchMonsterSighted += OnArchMonsterSighted;   // archimonstre repéré -> alerte + journal + relais Discord
        _launch.ModeratorPopupReceived += OnModeratorPopup;   // Lot E : popup de modération -> son + toast (centre de notifications)
        _launch.GameLinkLost += OnGameLinkLost;               // MITM : kick serveur / connexion de jeu fermée, Dofus.exe ouvert -> « Déconnecté »
        // Déconnexion à distance (panel "Sessions" -> DISCONNECT_BOT) : arrête le bot du pid ciblé, comme "Arrêter".
        LicenseService.DisconnectBotRequested += (pid, nonce, name) => Dispatcher.UIThread.Post(() =>
        {
            var row = Accounts.FirstOrDefault(a => a.Pid == pid);
            bool found = row is { Running: true };
            if (found) Stop(row!);
            LicenseService.PulseBotTelemetry();   // le panel voit le bot disparaître tout de suite
            _ = LicenseService.AckScriptControlAsync(nonce, name, found, found ? null : "bot introuvable ou déjà arrêté");
        });
        // Droits serveur changés (expiration / retrait / kill) -> réévalue le bandeau multi ET coupe les comptes déjà
        // en ligne dont le droit de routage/multi vient de disparaître (le client ne reste pas connecté sans droit).
        LicenseService.PermissionsChanged += () => Dispatcher.UIThread.Post(OnPermissionsChanged);
        Loc.Changed += () => OnPropertyChanged(nameof(SocketButtonTip));   // infobulle du bouton Socket re-localisée
        // Fournit l'état des bots (télémetrie serveur "bots") : lu périodiquement par LicenseService, thread de fond.
        LicenseService.SetBotProvider(CollectBotTelemetry);
        // Conso RAM/CPU par bot toutes les 60 s ([socket-perf]) : lit l'instantané déjà tenu par le poll, sans saut UI.
        TransportPerf.Start(() => _botSnapshot
            .Where(b => !LaunchService.IsSocketPid((uint)b.Pid))   // socket : aucun process à mesurer (pid synthétique)
            .Select(b => new PerfTarget((uint)b.Pid, string.IsNullOrEmpty(b.Character) ? "bot" : b.Character))
            .ToList(),
            // Comptes SOCKET : trafic par compte + estimation mémoire par compte (ws GivreClone.exe − base à vide) / N.
            () => _botSnapshot
            .Where(b => LaunchService.IsSocketPid((uint)b.Pid))
            .Select(b => new PerfTarget((uint)b.Pid, string.IsNullOrEmpty(b.Character) ? "bot" : b.Character))
            .ToList());
        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _poll.Tick += (_, _) => { SyncProxies(); UpdateCapture(); UpdateActivity(); RefreshBotSnapshot(); };
        _poll.Start();
    }

    // Recalcule l'activité de HAUT NIVEAU de chaque bot en jeu (poll 2,5 s) : couvre ce que les fronts d'événements ne
    // donnent pas (début de récolte, passage Connexion->Jeu, expiration de la fenêtre de déplacement/récolte).
    private void UpdateActivity()
    {
        foreach (var row in Accounts)
        {
            if (row.Running && row.Pid is int pid && pid > 0) row.Activity = _launch.ActivityOf((uint)pid);
            row.RefreshUptime();
        }
    }

    // MAJ IMMÉDIATE de l'activité d'un pid, depuis un front d'événement du moteur (threads du relais -> marshalé UI).
    private void ApplyActivity(uint pid) => ApplyToRow(pid, r => { if (r.Running) r.Activity = _launch.ActivityOf(pid); });

    // Lot E : popup de MODÉRATION reçu par un bot (thread réseau) -> son d'erreur + toast (alimente le centre de notifications).
    private void OnModeratorPopup(uint pid, LaunchService.ModeratorPopup mp)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var row = Accounts.FirstOrDefault(a => a.Pid == (int)pid);
            string character = !string.IsNullOrEmpty(row?.CharacterName) ? row!.CharacterName! : pid.ToString(System.Globalization.CultureInfo.InvariantCulture);
            AudioService.PlayUi(AudioService.Error);
            Toasts.Show(Loc.Fmt("L.Mod.Toast", character, mp.Content), success: false, sound: false);
        });
    }

    // MITM : la session de jeu est perdue (kick serveur — inactivité, modération… — ou connexion de jeu fermée) alors que
    // Dofus.exe reste ouvert (thread réseau / minuterie). La ligne passe « Déconnecté » avec la raison ; le process vit : Running
    // + Pid, l'entrée Bots et sa Console sont CONSERVÉS. Carte périmée effacée (y compris le cache du poll, sinon UpdateCapture
    // la restaurait). La ligne revient d'elle-même à « Connexion » puis « En jeu » quand une nouvelle session démarre.
    private void OnGameLinkLost(uint pid, string reason, bool imposed)
    {
        if (_pidStats.TryGetValue(pid, out var s)) { s.MapId = 0; s.MapX = null; s.MapY = null; }
        bool byServer = _launch.LinkDownOf(pid)?.ByServer ?? true;
        Dispatcher.UIThread.Post(() =>
        {
            var row = Accounts.FirstOrDefault(a => a.Pid == (int)pid);
            if (row is null || !row.Running || row.IsSocketSession) return;
            row.Activity = BotActivity.Disconnected;
            row.Status = Loc.Fmt(byServer ? "L.Accounts.ServerKicked" : "L.Accounts.LinkLost", reason);
            row.MapId = null; row.MapCoords = null;
            RefreshBotSnapshot(); LicenseService.PulseBotTelemetry();   // panel : le bot n'est plus « en jeu » sur sa carte
            AudioService.PlayError();
            Toasts.Show(Loc.Fmt(byServer ? "L.Accounts.ServerKickedToast" : "L.Accounts.LinkLostToast", row.Login, reason), success: false, sound: false);
        });
    }

    // Threads du relais : on accumule seulement (aucune MAJ UI ici -> pas de spam a 100+ paquets/s).
    private void OnPacketDecoded(DecodedPacket p)
    {
        var s = _pidStats.GetOrAdd(p.Pid, static _ => new PidStats());
        Interlocked.Increment(ref s.Total);
        if (p.Name is not null) Interlocked.Increment(ref s.Recognized);
    }

    private void OnCharacterIdentified(uint pid, CharacterInfo ci)
    {
        var s = _pidStats.GetOrAdd(pid, static _ => new PidStats());
        s.Character = ci.Name; s.Level = ci.Level; s.Breed = ci.Breed; s.Gender = ci.Gender;
        s.BreedIndex = ci.BreedIndex; s.GenderIndex = ci.GenderIndex;   // bruts : re-resolution de l'icone dans UpdateCapture
        int breedId = ci.BreedIndex is int bix ? ClassIconProvider.BreedId(bix) : 0;
        s.BreedId = breedId;
        // Evenement DISCRET (pas un flux) : MAJ immediate de la ligne, sans attendre le timer 2,5 s.
        // Icone de classe (tete) depuis l'index d'enum + sexe -> remplace l'avatar generique. null si non resolue.
        var classIcon = ci.BreedIndex is int bi ? Stores.ClassIcons.Get(bi, ci.GenderIndex ?? 0) : null;
        // Diagnostic : distingue les 2 causes d'icone absente. breedIndex null = champ de classe deplace par un patch
        // (protocole) ; breedIndex OK mais icone nulle = bundle pas encore charge (re-resolu par UpdateCapture) ou renomme.
        GivreLauncher.Core.Transport.TransportLog.Line(
            $"[classe] {ci.Name} : breedIndex={(ci.BreedIndex?.ToString() ?? "null")} classe={ci.Breed ?? "?"} icone={(classIcon is not null ? "OK" : "absente")}");
        ApplyToRow(pid, r =>
        {
            r.CharacterName = ci.Name; r.Level = ci.Level; r.Breed = ci.Breed; r.Gender = ci.Gender;
            r.BreedId = breedId; r.BreedIndex = ci.BreedIndex; r.GenderIndex = ci.GenderIndex;
            r.Character = ci.Level is int lv ? $"{ci.Name} ({lv})" : ci.Name!;
            if (classIcon is not null) r.ClassIcon = classIcon;
        });
    }

    // Changement de niveau (CharacterLevelUpEvent, resolu par nom) : maj immediate du niveau + "nom (niveau)" sur la fiche.
    private void OnCharacterLeveledUp(uint pid, int level)
    {
        var s = _pidStats.GetOrAdd(pid, static _ => new PidStats());
        s.Level = level;
        ApplyToRow(pid, r =>
        {
            r.Level = level;
            if (!string.IsNullOrEmpty(r.CharacterName)) r.Character = $"{r.CharacterName} ({level})";
        });
    }

    private void OnServerIdentified(uint pid, string server)
    {
        var s = _pidStats.GetOrAdd(pid, static _ => new PidStats());
        s.Server = server;
        ApplyToRow(pid, r => r.Server = server);   // MAJ immediate (evenement discret)
        Dispatcher.UIThread.Post(() => FlushArchPending(pid));   // archimonstres vus avant l'identification du serveur
    }

    private void OnMapChanged(uint pid, long mapId, int? x, int? y)
    {
        if (_launch.IsMeaninglessPosition(pid, mapId, x, y)) return;   // garde la dernière vraie position (plus de « 0,0 » en combat)
        var s = _pidStats.GetOrAdd(pid, static _ => new PidStats());
        s.MapId = mapId; s.MapX = x; s.MapY = y;
        var coords = x is int xx && y is int yy ? $"{xx}, {yy}" : null;
        // Changement de carte = evenement discret : MAJ immediate (le paquet arrive instantanement, plus de lag 2,5 s).
        ApplyToRow(pid, r => { r.MapId = mapId; r.MapCoords = coords; });
    }

    // Applique une MAJ a la ligne du PID sur le thread UI, TOUT DE SUITE (ces handlers sont appeles depuis les
    // threads du relais). Reserve aux evenements DISCRETS (perso, carte) ; les COMPTEURS de paquets restent
    // agreges par le timer 2,5 s (UpdateCapture) pour ne pas spammer l'UI a 100+ paquets/s.
    private void ApplyToRow(uint pid, Action<AccountRow> apply)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var row in Accounts)
                if (row.Pid == (int)pid)
                {
                    if (RelaunchOwnedBy((int)pid, row)) continue;   // trame tardive du client tué par « Reconnecter » en place
                    apply(row); break;
                }
        });
    }

    // Timer UI (2,5 s) : reflete l'activite de capture sur chaque carte de compte lancee.
    private void UpdateCapture()
    {
        foreach (var row in Accounts)
        {
            if (row.Pid is not int pid || !_pidStats.TryGetValue((uint)pid, out var s)) continue;
            if (RelaunchOwnedBy(pid, row)) continue;   // client tué par « Reconnecter » en place : paquets tardifs, statut « Reconnexion » gardé
            long total = Interlocked.Read(ref s.Total);
            if (total == 0) continue;
            // Statut de la carte = ACTIVITÉ claire (Jeu / Combat / Récolte / Déplacement), plus le compteur de paquets
            // (reconnus/total) : ce détail de diagnostic vit dans l'onglet Console du bot (SelectedBot.PacketSummary).
            row.Activity = _launch.ActivityOf((uint)pid);
            if (!row.IsDisconnected) row.Status = row.ActivityText;   // « Déconnecté par le serveur (raison) » reste affiché
            if (!string.IsNullOrEmpty(s.Character))
            {
                row.CharacterName = s.Character;
                row.Level = s.Level;
                row.Breed = s.Breed;
                row.BreedId = s.BreedId;
                row.BreedIndex = s.BreedIndex; row.GenderIndex = s.GenderIndex;
                row.Gender = s.Gender;
                row.Character = s.Level is int lv ? $"{s.Character} ({lv})" : s.Character!;
            }
            if (s.MapId != 0)
            {
                row.MapId = s.MapId;
                row.MapCoords = s.MapX is int xx && s.MapY is int yy ? $"{xx}, {yy}" : row.MapCoords;
            }
            if (s.Server is not null) row.Server = s.Server;
            // Auto-guerison de l'icone de classe : elle se resout a l'identification, mais si le bundle des symboles
            // n'etait pas encore charge (tache de fond), elle etait nulle sans nouvelle tentative. On la re-resout ici
            // tant qu'elle manque -> l'avatar apparait au plus tard 2,5 s apres que le bundle est pret.
            if (row.ClassIcon is null && s.BreedIndex is int bidx)
            {
                var icon = Stores.ClassIcons.Get(bidx, s.GenderIndex ?? 0);
                if (icon is not null) row.ClassIcon = icon;
            }
        }
    }

    private readonly Dictionary<string, string> _shownNames = new();

    // Recharge la liste de proxys partagee (les references du store sont stables -> les selections tiennent).
    private void SyncProxies()
    {
        // Sentinelle "Sans proxy" toujours en tete : permet de choisir explicitement AUCUN proxy (IP reelle).
        if (!Proxies.Contains(ProxyEntry.None)) Proxies.Insert(0, ProxyEntry.None);

        var live = Stores.Proxies.Proxies;
        var liveIds = live.Select(p => p.Id).ToHashSet();
        foreach (var cur in Proxies.ToList())
            if (!cur.IsNone && !liveIds.Contains(cur.Id)) Proxies.Remove(cur);
        var shown = Proxies.Select(p => p.Id).ToHashSet();
        foreach (var p in live)
            if (!shown.Contains(p.Id)) Proxies.Add(p);
        // Proxy renommé (agent renommé sur le portail) : ProxyEntry ne notifie pas -> l'élément est réinséré pour que les
        // listes déroulantes affichent le nouveau nom ; les cartes qui le portaient le retrouvent juste en dessous.
        foreach (var p in live)
        {
            if (_shownNames.TryGetValue(p.Id, out var was) && was != p.Name && Proxies.IndexOf(p) is int at and >= 0)
            {
                bool chosenForNew = ReferenceEquals(SelectedNewProxy, p);
                Proxies.RemoveAt(at);
                Proxies.Insert(at, p);
                if (chosenForNew) SelectedNewProxy = p;   // formulaire d'ajout : même choix, nouveau nom
            }
            _shownNames[p.Id] = p.Name;
        }
        // Proxy mémorisé de retour dans la liste (agent revenu avec le même identifiant, renommé) : réaffiché sur la carte du compte.
        foreach (var row in Accounts)
            if (row.SelectedProxy is null && row.Model.ProxyId is { } pid && Proxies.FirstOrDefault(p => p.Id == pid) is { } back)
                row.RestoreProxy(back);
    }

    private void Reload()
    {
        // ⚠ Réconciliation EN PLACE (clé = Model.Id) — on ne VIDE JAMAIS la collection. Recréer des AccountRow NEUFS
        // effaçait l'état TRANSITOIRE (Running/Pid/Character/carte/serveur/ExitIp/proxy live) des comptes ENCORE
        // lancés : BotsViewModel.Resync (abonné à Accounts.CollectionChanged) recalcule alors « aucun bot ne tourne »
        // et retire TOUS les bots de l'onglet Bots — en disposant leur hôte Lua — alors que leurs process tournent
        // toujours (TryKill n'a visé que le compte supprimé), laissant un client Dofus ORPHELIN (sans ligne ni moyen
        // de l'arrêter). Reproduit sur Remove ET AddAccount/DetectKeydata/AddBrowser. On réutilise donc la ligne
        // existante (AccountManager.Remove garde les mêmes instances Account survivantes -> .Model reste valide) et on
        // ne crée une ligne neuve que pour un compte réellement nouveau. Un Clear()+re-add marcherait aussi MAIS sa
        // justesse dépendrait du Post différé de Resync (collection vide transitoire) : la réconciliation en place
        // supprime ce couplage caché et évite le flicker de l'onglet Comptes.
        var existing = new Dictionary<string, AccountRow>();
        foreach (var r in Accounts) existing[r.Model.Id] = r;   // indexer = dédup sûr (jamais de throw)
        var desired = Stores.Accounts.Accounts;                 // ordre du store = source de vérité

        // 1) Retire les lignes dont le compte n'existe plus (vraie suppression).
        var keep = new HashSet<string>(desired.Select(a => a.Id));
        for (int i = Accounts.Count - 1; i >= 0; i--)
            if (!keep.Contains(Accounts[i].Model.Id)) Accounts.RemoveAt(i);

        // 2) Aligne sur l'ordre du store : réutilise la ligne existante (état live préservé) ou en crée une neuve
        //    pour un compte réellement nouveau, à la bonne position.
        for (int i = 0; i < desired.Count; i++)
        {
            var a = desired[i];
            if (existing.TryGetValue(a.Id, out var kept))
            {
                int cur = Accounts.IndexOf(kept);
                if (cur != i) Accounts.Move(cur, i);
            }
            else
            {
                var row = new AccountRow(a, Proxies, Loc.T("L.Accounts.StatusIdle"))
                {
                    ProxyAssigned = r => { Stores.Accounts.Persist(); Toasts.Show(Loc.Fmt("L.Accounts.ProxySet", r.Login), sound: false); },
                    SettingsChanged = _ => Stores.Accounts.Persist()
                };
                Accounts.Insert(i, row);
            }
        }
        OnPropertyChanged(nameof(HasAccounts));
    }

    [RelayCommand]
    private void AddAccount()
    {
        if (string.IsNullOrWhiteSpace(NewLogin)) { Toasts.Show(Loc.T("L.Accounts.LoginRequired"), success: false); return; }
        var label = string.IsNullOrWhiteSpace(NewAccountLabel) ? null : NewAccountLabel.Trim();
        var acc = Stores.Accounts.Add(NewLogin.Trim(), NewPassword, label);
        if (SelectedNewProxy is { IsNone: false } p) { acc.ProxyId = p.Id; Stores.Accounts.Persist(); }   // proxy choisi -> assigné au compte
        NewLogin = ""; NewPassword = ""; NewAccountLabel = ""; SelectedNewProxy = ProxyEntry.None; Reload();
        Toasts.Show(Loc.Fmt("L.Accounts.Added", acc.Login));
    }

    [RelayCommand]
    private async Task DetectKeydataAsync()
    {
        try
        {
            var all = await LauncherAccountReader.ReadInstalledAsync().ConfigureAwait(true);
            int added = 0;
            foreach (var kd in all)
            {
                if (Stores.Accounts.Accounts.Any(a => a.Login.Equals(kd.Login, StringComparison.OrdinalIgnoreCase))) continue;
                Stores.Accounts.Add(kd.Login, displayName: kd.Nickname);
                added++;
            }
            Reload();
            Toasts.Show($"{added} compte(s) importé(s), {all.Count - added} déjà présent(s). Les autorisations de connexion ne sont pas importées.");
        }
        catch (Exception e) { Toasts.Show(Loc.Fmt("L.Accounts.LaunchError", e.Message), success: false); }
    }

    // Résout le proxy choisi dans le formulaire d'ajout. Renvoie false (et prévient) si un proxy est sélectionné
    // mais injoignable -> on BLOQUE l'auth navigateur plutôt que d'exposer l'IP réelle en silence.
    private bool TryResolveNewProxy(out GivreLauncher.Core.Proxy.ProxySettings? ps, out ProxyEntry? entry)
    {
        ps = null;
        entry = SelectedNewProxy;
        // Aucune sélection (le proxy choisi vient d'être supprimé) : jamais pris pour « Sans proxy ».
        if (entry is null) { AudioService.PlayError(); Toasts.Show(Loc.Fmt("L.Accounts.ProxyRemoved", NewLogin.Trim()), success: false); return false; }
        if (entry.IsNone) entry = null;   // "Sans proxy" -> IP réelle assumée
        if (entry is null) return true;
        if (entry.FromRelay) return TryResolveRelay(entry, out ps);
        // Droit requis pour router : "adapter" (interface) ou "proxy" (externe). Message précis plutôt que le
        // générique "injoignable" (sinon l'utilisateur ne comprend pas que c'est la licence qui bloque).
        if (entry.FromAdapter && !LicenseService.HasPermission(LicenseService.Perm.Adapter))
        { AudioService.PlayError(); Toasts.Show(Loc.T("L.Perm.AdapterDenied"), success: false); return false; }
        if (!entry.FromAdapter && !LicenseService.HasPermission(LicenseService.Perm.Proxy))
        { AudioService.PlayError(); Toasts.Show(Loc.T("L.Perm.ProxyDenied"), success: false); return false; }
        ps = ProxyResolver.Resolve(entry, Stores.Proxies);
        if (ps is null) { Toasts.Show(Loc.T("L.Accounts.ProxyUnresolved"), success: false); return false; }
        return true;
    }

    /// <summary>Connexion Ankama par OAuth (édition locale) : navigateur de l'utilisateur, puis code OTP saisi dans la fenêtre Bouclier. Jamais d'identifiant dans l'application.</summary>
    [RelayCommand]
    private async Task AddOAuthAsync()
    {
        if (!TryResolveNewProxy(out var ps, out var entry)) return;   // proxy choisi injoignable -> on bloque (pas de fuite d'IP)
        try
        {
            using var http = GivreClone.App.Services.LocalLaunch.AnkamaOAuth.CreateClient(ps);
            var verifier = GivreClone.App.Services.LocalLaunch.AnkamaOAuth.NewCodeVerifier();
            var url = GivreClone.App.Services.LocalLaunch.AnkamaOAuth.LoginUrl(verifier);
            var waiting = GivreClone.App.Services.LocalLaunch.AnkamaOAuth.WaitForCodeAsync(TimeSpan.FromMinutes(5), CancellationToken.None);
            Toasts.Show("Connexion Ankama : une page s'ouvre dans votre navigateur. Connectez-vous (5 min max).");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            var code = await waiting.ConfigureAwait(true);
            if (string.IsNullOrEmpty(code)) { Toasts.Show("Connexion Ankama annulée ou délai dépassé.", success: false); return; }

            var (access, refresh) = await GivreClone.App.Services.LocalLaunch.AnkamaOAuth.RedeemCodeAsync(http, code, verifier, CancellationToken.None).ConfigureAwait(true);
            var info = await GivreClone.App.Services.LocalLaunch.AnkamaOAuth.GetAccountAsync(http, access, CancellationToken.None).ConfigureAwait(true);
            string login = !string.IsNullOrWhiteSpace(info.Login) ? info.Login! : NewLogin.Trim();
            if (string.IsNullOrWhiteSpace(login)) { Toasts.Show("Identifiant du compte illisible : saisissez-le dans le champ Identifiant puis recommencez.", success: false); return; }

            GivreClone.App.Services.LocalLaunch.AnkamaOAuth.Certificate? cert = null;
            if (info.ShieldActive)
            {
                // Comme le lanceur officiel : on DEMANDE d'abord l'envoi du code (e-mail), ou on apprend que le compte utilise une application.
                var mode = await GivreClone.App.Services.LocalLaunch.AnkamaOAuth.RequestSecurityCodeAsync(http, access, sms: false, CancellationToken.None).ConfigureAwait(true);
                if (mode != GivreClone.App.Services.LocalLaunch.AnkamaOAuth.SecurityCodeMode.NotNeeded)
                {
                    Toasts.Show(mode == GivreClone.App.Services.LocalLaunch.AnkamaOAuth.SecurityCodeMode.EmailSent
                        ? "Un code vient d'être envoyé par e-mail (vérifiez aussi les indésirables)."
                        : "Saisissez le code de votre application d'authentification.");
                    var code2 = await RequestSecurityCodeAsync(login).ConfigureAwait(true);   // fenêtre Bouclier
                    if (string.IsNullOrWhiteSpace(code2)) { Toasts.Show("Code non saisi : connexion annulée.", success: false); return; }
                    cert = await GivreClone.App.Services.LocalLaunch.AnkamaOAuth.ValidateSecurityCodeAsync(http, access, code2,
                        otp: mode == GivreClone.App.Services.LocalLaunch.AnkamaOAuth.SecurityCodeMode.OtpRequired, CancellationToken.None).ConfigureAwait(true);
                }
            }
            var apiKey = await GivreClone.App.Services.LocalLaunch.AnkamaOAuth.RefreshApiKeyAsync(http, access, refresh, CancellationToken.None).ConfigureAwait(true);

            // Un compte du même identifiant existe déjà (ex. importé du Launcher sans jetons) : on le CONVERTIT en compte navigateur au lieu de créer un doublon.
            var acc = Stores.Accounts.Accounts.FirstOrDefault(x => string.Equals(x.Login, login, StringComparison.OrdinalIgnoreCase));
            if (acc is null) acc = Stores.Accounts.AddBrowser(login, apiKey, refresh);
            else Stores.Accounts.SetBrowserTokens(acc.Id, apiKey, refresh);
            if (cert is not null) Stores.Accounts.SetCertificate(acc.Id, cert.Id, cert.Encoded);
            if (entry is not null) acc.ProxyId = entry.Id;
            if (!string.IsNullOrWhiteSpace(NewAccountLabel)) acc.DisplayName = NewAccountLabel.Trim();
            Stores.Accounts.Persist();
            NewLogin = ""; NewPassword = ""; NewAccountLabel = ""; SelectedNewProxy = ProxyEntry.None; Reload();
            Toasts.Show(Loc.Fmt("L.Accounts.Added", acc.Login));
        }
        catch (Exception ex)
        {
            AudioService.PlayError();
            Toasts.Show("Connexion Ankama : " + ex.Message, success: false);   // message sans secret (seul le code HTTP est relayé)
        }
    }

    [RelayCommand]
    private async Task AddBrowserAsync()
    {
        if (string.IsNullOrWhiteSpace(NewLogin)) { Toasts.Show(Loc.T("L.Accounts.LoginRequired"), success: false); return; }
        if (!TryResolveNewProxy(out var ps, out var entry)) return;   // proxy injoignable -> on bloque (pas de fuite)
        var res = await WebAuthRunner.RunAsync(ps, NewLogin.Trim(), NewPassword).ConfigureAwait(true);
        switch (res.Outcome)
        {
            case WebAuthRunner.Outcome.Success when res.Tokens is not null:
                var acc = Stores.Accounts.AddBrowser(NewLogin.Trim(), res.Tokens.AccessToken, res.Tokens.RefreshToken);
                if (entry is not null) acc.ProxyId = entry.Id;   // assigne le proxy choisi au compte créé
                if (!string.IsNullOrWhiteSpace(NewAccountLabel)) acc.DisplayName = NewAccountLabel.Trim();
                Stores.Accounts.Persist();
                NewLogin = ""; NewPassword = ""; NewAccountLabel = ""; SelectedNewProxy = ProxyEntry.None; Reload();
                Toasts.Show(Loc.Fmt("L.Accounts.Added", acc.Login));
                break;
            case WebAuthRunner.Outcome.Unavailable:
                Toasts.Show(Loc.T("L.Accounts.WebAuthUnavailable"), success: false); break;
            case WebAuthRunner.Outcome.Cancelled: break;
            default:
                Toasts.Show(Loc.Fmt("L.Accounts.LaunchError", res.Error ?? "auth"), success: false); break;
        }
    }

    [RelayCommand]
    private void Remove(AccountRow row)
    {
        Dialogs.Confirm(Loc.T("L.Accounts.RemoveTitle"), Loc.Fmt("L.Accounts.RemoveBody", row.Login),
            // Teardown VOULU via StopCore (retire la surveillance de crash + reset Running) AVANT de tuer/supprimer :
            // sinon le Process.Exited du client tué serait pris pour un crash (toast fantôme, voire relance auto d'un
            // compte supprimé). Compte non lancé (Pid déjà nul) -> rien à couper. Pid d'un client déjà tué par une relance en
            // place (réattribuable par l'OS) : jamais tué (cf StopCore).
            onConfirm: () =>
            {
                if (row.Running) StopCore(row);
                else if (row.Pid is int pid && !RelaunchOwnedBy(pid, row)) TryKill(pid);
                Stores.Accounts.Remove(row.Model.Id); Reload(); Toasts.Show(Loc.Fmt("L.Accounts.Removed", row.Login));
            },
            danger: true);
    }

    // ---- Édition d'un compte après création (login / libellé / mot de passe) ----
    [ObservableProperty] public partial bool EditVisible { get; set; }
    [ObservableProperty] public partial string EditLogin { get; set; } = "";
    [ObservableProperty] public partial string EditLabel { get; set; } = "";
    [ObservableProperty] public partial string EditPassword { get; set; } = "";
    /// <summary>Compte Ankama/keydata (mot de passe éditable). Faux = compte navigateur (jetons, pas de mot de passe ici).</summary>
    [ObservableProperty] public partial bool EditIsKeyData { get; set; }
    /// <summary>Paramètres du compte : démarrer le script du perso dès qu'il est en jeu.</summary>
    [ObservableProperty] public partial bool EditAutoStartScript { get; set; }
    /// <summary>Proxy du serveur de jeu choisi (<see cref="SameProxyChoice"/> = le même que le compte ; null = l'ancien a été supprimé).</summary>
    [ObservableProperty] public partial ProxyEntry? EditGameProxy { get; set; }
    /// <summary>Choix du proxy du serveur de jeu : « Même proxy que la connexion » puis les proxys enregistrés.</summary>
    public ObservableCollection<ProxyEntry> GameProxyChoices { get; } = new();
    /// <summary>Sentinelle « Même proxy que la connexion » (aucun proxy de jeu distinct).</summary>
    public static ProxyEntry SameProxyChoice { get; } = new() { Id = "same", Name = "" };
    [ObservableProperty] public partial string EditTitleLogin { get; set; } = "";
    private AccountRow? _editRow;

    // Engrenage de la carte : paramètres du compte (identité, proxy du serveur de jeu, démarrage automatique du script).
    [RelayCommand]
    private void Edit(AccountRow row)
    {
        _editRow = row;
        EditLogin = row.Model.Login;
        EditLabel = row.Model.DisplayName ?? "";
        EditPassword = "";   // vide = on ne change PAS le mot de passe existant
        EditIsKeyData = row.Model.Source == AuthSource.KeyData;
        EditTitleLogin = row.HasLabel ? row.Label! : row.Login;
        EditAutoStartScript = row.Model.AutoStartScript;
        SameProxyChoice.Name = Loc.T("L.AccountSettings.SameProxy");
        GameProxyChoices.Clear();
        GameProxyChoices.Add(SameProxyChoice);
        foreach (var p in Stores.Proxies.Proxies) GameProxyChoices.Add(p);
        // Proxy de jeu mémorisé mais supprimé depuis : aucune sélection (le lancement est refusé tant qu'un choix n'est pas fait).
        EditGameProxy = row.Model.GameProxyId is { Length: > 0 } gid ? GameProxyChoices.FirstOrDefault(p => p.Id == gid) : SameProxyChoice;
        EditVisible = true;
    }

    [RelayCommand]
    private void CancelEdit() { EditVisible = false; _editRow = null; }

    [RelayCommand]
    private void ConfirmEdit()
    {
        if (_editRow is null) { EditVisible = false; return; }
        if (string.IsNullOrWhiteSpace(EditLogin)) { Toasts.Show(Loc.T("L.Accounts.LoginRequired"), success: false); return; }
        _editRow.Model.Login = EditLogin.Trim();
        _editRow.Model.DisplayName = string.IsNullOrWhiteSpace(EditLabel) ? null : EditLabel.Trim();
        _editRow.Model.AutoStartScript = EditAutoStartScript;
        // Proxy du serveur de jeu : « même proxy » = aucun ; pas de sélection (ancien supprimé) = inchangé. Pris en compte au
        // prochain lancement (une session en cours garde sa route).
        if (EditGameProxy is { } gp) _editRow.Model.GameProxyId = ReferenceEquals(gp, SameProxyChoice) ? null : gp.Id;
        // Mot de passe : uniquement compte keydata ET champ non vide (sinon on garde le blob DPAPI existant).
        if (EditIsKeyData && !string.IsNullOrEmpty(EditPassword))
            Stores.Accounts.SetPassword(_editRow.Model.Id, EditPassword);   // SetPassword persiste tout le compte (login+libellé inclus)
        else
            Stores.Accounts.Persist();
        _editRow.RaiseIdentityChanged();   // rafraîchit la carte SANS Reload (ne casse pas une session en cours)
        EditPassword = "";
        var login = _editRow.Model.Login;
        _editRow = null;
        EditVisible = false;
        Toasts.Show(Loc.Fmt("L.Accounts.Edited", login));
    }

    private static void TryKill(int pid)
        => _ = GivreLauncher.Core.Launch.MZP9OwnedClientRegistry.TryStopOwned(pid);

    // ---------- IShieldCodePrompt (2FA) ----------
    public async Task<string?> RequestSecurityCodeAsync(string login, CancellationToken ct = default)
    {
        // Une seule fenêtre Bouclier à la fois : des lancements concurrents (Lancer tout en Socket, plusieurs comptes) se
        // passaient la MÊME TCS -> la 1re demande n'était jamais complétée (compte bloqué « connexion… » à vie, e-mail de code
        // jamais demandé). Chaque demande attend son tour et a sa propre TCS ; sans réponse en 10 min -> abandon propre.
        try { await _shieldGate.WaitAsync(ct).ConfigureAwait(true); }
        catch (OperationCanceledException) { return null; }
        try
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            await Dispatcher.UIThread.InvokeAsync(() => { ShieldLogin = login; ShieldCode = ""; _shieldTcs = tcs; ShieldVisible = true; });
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            await using (timeout.Token.Register(() => tcs.TrySetResult(null)))
            {
                var code = await tcs.Task.ConfigureAwait(true);
                if (timeout.IsCancellationRequested) await Dispatcher.UIThread.InvokeAsync(() => { if (ReferenceEquals(_shieldTcs, tcs)) ShieldVisible = false; });
                return code;
            }
        }
        finally { _shieldGate.Release(); }
    }
    private readonly SemaphoreSlim _shieldGate = new(1, 1);

    [RelayCommand]
    private void ConfirmShield() { ShieldVisible = false; _shieldTcs?.TrySetResult(string.IsNullOrWhiteSpace(ShieldCode) ? null : ShieldCode.Trim()); }

    [RelayCommand]
    private void CancelShield() { ShieldVisible = false; _shieldTcs?.TrySetResult(null); }
}

/// <summary>Carte SERVEUR du modal de choix (mode Socket) : nom, statut, mono-compte, restriction d'accès, aperçu des persos.</summary>
public sealed class SocketServerCard
{
    public int ServerId { get; init; }
    public string Name { get; init; } = "";
    public string Initial { get; init; } = "";
    public bool Online { get; init; }
    public bool Maintenance => !Online;
    public bool MonoAccount { get; init; }
    public string? Restriction { get; init; }
    public bool HasRestriction => Restriction is not null;
    public string CharCountText { get; init; } = "";
    public string CharNames { get; init; } = "";
    public IReadOnlyList<SocketCharCard> Preview { get; init; } = Array.Empty<SocketCharCard>();
    public string LastPlayedText { get; init; } = "";
    public bool HasLastPlayed => LastPlayedText.Length > 0;
    internal DateTimeOffset LastPlayed { get; init; }
}

/// <summary>Carte PERSONNAGE du modal de choix (mode Socket) : symbole de classe, nom, niveau, classe · sexe, dernière connexion.</summary>
public sealed class SocketCharCard
{
    public int ServerId { get; init; }
    public string Name { get; init; } = "";
    public string DeleteTooltip => Loc.Fmt("L.Socket.Delete.Tooltip", Name);
    public string LevelText { get; init; } = "";
    public string ClassName { get; init; } = "";
    public Avalonia.Media.Imaging.Bitmap? Icon { get; init; }
    public bool HasIcon => Icon is not null;
    public bool NoIcon => Icon is null;
    public string LastPlayedText { get; init; } = "";
    public bool HasLastPlayed => LastPlayedText.Length > 0;
    internal DateTimeOffset LastPlayed { get; init; }
}
