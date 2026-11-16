using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Transport;
using GivreClone.App.Services.Transport.Socket;
using GivreClone.Engine.Accounts;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Ankama;
using GivreLauncher.Core.Launch;
using GivreLauncher.Core.Proxy;
using GivreLauncher.Core.Transport;
using GivreLauncher.Core.Zaap;
using CoreAccount = GivreLauncher.Core.Accounts.Account;

namespace GivreClone.App.Services;

public sealed record LaunchRequest(
    string Login,
    AuthSource Source,
    ProxySettings? Proxy,      // proxy SOCKS5/HTTP du compte (auth ET jeu). Null -> lancement REFUSE (anti-fuite).
    string? ApiKey,
    string? RefreshToken,
    int AutoConnectType,
    string? CertId = null,          // certificat Shield persisté (compte Browser sécurisé) : évite un re-mint refusé.
    string? CertEncoded = null,
    string? DeviceSeed = null,      // « Empreinte machine par compte » : graine transmise à GivreKama (GIVRE_HWSEED) qui
                                    // falsifie les MAC dans le client MITM -> device_identifier unique par compte.
    ProxySettings? GameProxy = null,    // proxy du SERVEUR DE JEU (null = Proxy) : connexion via Proxy, session de jeu via celui-ci.
    string? GameProxyLabel = null,       // nom lisible du proxy de jeu (journal de connexion du bot)
    string? CharacterAutoPick = null,   // choix explicite, lié au PID avant injection du hook
    uint HotMirrorSocketPid = 0);       // miroir à chaud : le hook vise l'accepteur local, le Socket reste l'unique amont

public sealed record LaunchOutcome(bool Ok, int Pid, string Message, bool NeedsReauth = false);

/// <summary>
/// Chaine de lancement de GivreClone. Reutilise le MANAGED de GivreLauncher.Core : auth Ankama, faux Zaap,
/// GameLauncher, et le relais 0-leak HandshakeRelay (forward via SOCKS5 / HTTP CONNECT / direct, avec le
/// correctif 443->5555 des hotspots 4G). Le HOOK NATIF est PROPRE a GivreClone (GivreKama.dll) et est
/// COMPATIBLE avec HandshakeRelay (meme handshake 26o, meme port 5658).
///
/// Le relais + le hook demarrent TOUJOURS (le pilotage/capture passe par la) : AVEC proxy, l'amont sort via le proxy
/// (isolation d'IP) ; SANS proxy, l'amont sort en DIRECT (IP reelle, assumee par l'utilisateur) mais le compte reste
/// PILOTABLE. ANTI-FUITE (fail-closed) uniquement AVEC proxy : si GivreKama n'est pas CONFIRME charge, on TUE le client
/// -> jamais de connexion via le proxy sans hook. Sans proxy, un hook absent = jeu brut non pilote (pas de fermeture).
/// </summary>
public sealed partial class LaunchService
{
    // Édition locale : autorités indépendantes (Zaap Thrift local + recette de lancement) sur des ports libres, jamais 26666/5658 d'un
    // GivreClone d'origine. Hors édition locale : constructeur historique, qui refuse de lancer (aucune autorité indépendante).
    private readonly LocalLaunch.LocalZaapAuthority? _localAuth;
    private readonly ZaapEmulator _zaap;
    private bool _zaapStarted;
    private readonly GameLauncher _launcher;

    private static int FreeLoopbackPort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start(); int p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
    }

    /// <summary>Hôtes de connexion donnés au client dans divazaap.json : data/connection-hosts.txt (une valeur) sinon l'hôte de
    /// connexion de production au format de la recette ZaapEmu « Nom:hôte:port ». HYPOTHÈSE de format (le nom est un libellé), à confirmer en jeu.</summary>
    private static string LocalConnectionHosts()
    {
        try
        {
            var f = Path.Combine(AppContext.BaseDirectory, "data", "connection-hosts.txt");
            if (File.Exists(f) && File.ReadAllText(f).Trim() is { Length: > 0 } v) return v;
        }
        catch { }
        return "JMBouftou:dofus2-co-production.ankama-games.com:5555";
    }
    private MitmTransport? _mitm;
    private Func<int>? _localRelayStart;   // édition locale : démarre le relais propre et rend son port
    private bool _relayStarted;

    // MODE SOCKET (clientless) : injecteurs par pid synthetique. Chaque bot socket enregistre ici sa fonction d'ecriture
    // (ecrit une trame WIRE sur SA propre socket de jeu). Le transport de dispatch (RelayForInject) route Inject(pid,..)
    // vers l'injecteur socket si le pid en a un, sinon vers le relais MITM -> TOUTES les primitives de forge (move/cast/
    // harvest/banque/Lua...) pilotent un bot socket EXACTEMENT comme un bot MITM, sans toucher aux ~40 sites d'appel.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, Func<byte[], bool>> _socketInjectors = new();
    // Reviewed cross-patch sockets can run bounded movement diagnostics, not
    // all Lua/combat/expedition commands. Track the scope per synthetic PID.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, byte> _socketReviewedOnly = new();
    private DispatchTransport? _dispatch;

    /// <summary>MODE SOCKET : un bot clientless enregistre sa fonction d'injection (ecriture sur sa socket de jeu) pour
    /// son pid synthetique -> le pilotage (forge) le trouve via le transport de dispatch. Idempotent (remplace).</summary>
    public void RegisterSocketInjector(uint pid, Func<byte[], bool> inject) =>
        RegisterSocketInjector(pid, inject, reviewedMovementOnly: false);

    public void RegisterSocketInjector(uint pid, Func<byte[], bool> inject, bool reviewedMovementOnly)
    {
        if (reviewedMovementOnly) _socketReviewedOnly[pid] = 0;
        else _socketReviewedOnly.TryRemove(pid, out _);
        _socketInjectors[pid] = inject;
    }

    /// <summary>
    /// Diagnostic de disponibilité du canal de commande Socket pour le pid exact.
    /// Le GameClient ne doit publier cet injecteur qu'une fois la sélection
    /// serveur/personnage et le contexte de jeu reçus. Indique un transport de
    /// contrôle raccordé, jamais la réussite ou l'ACK d'une action particulière.
    /// </summary>
    public bool HasSocketInjector(uint pid) =>
        IsSocketPid(pid) && _socketInjectors.ContainsKey(pid);

    /// <summary>Transport callback registered but its cross-patch policy only
    /// permits diagnostic movement packets, not full-script gameplay.</summary>
    public bool IsReviewedSocketMovementOnly(uint pid) =>
        HasSocketInjector(pid) && _socketReviewedOnly.ContainsKey(pid);

    public bool HasFullSocketControl(uint pid) =>
        HasSocketInjector(pid) && !_socketReviewedOnly.ContainsKey(pid);

    /// <summary>MODE SOCKET : le bot clientless se termine -> on retire son injecteur (plus aucune forge routee vers lui).</summary>
    public void UnregisterSocketInjector(uint pid)
    {
        _socketInjectors.TryRemove(pid, out _);
        _socketReviewedOnly.TryRemove(pid, out _);
    }

    /// <summary>MODE SOCKET : renseigne le serveur du pid DIRECTEMENT depuis l'etape A (serveur choisi, deterministe) ->
    /// row.Server apparait tout de suite, sans attendre le WhoAmI/WhoIs (chemin MITM, tardif en clientless). Meme evenement
    /// ServerIdentified que le chemin WhoIsEvent, donc le reste de l'UI/telemetrie se peuple pareil.</summary>
    public void SetSocketServerId(uint pid, int serverId)
    {
        if (serverId == 0) return;
        // Serveur CONNU dès l'étape A, même si son nom n'est pas (encore) résolu : aucun WhoAmI ne doit partir pour un bot
        // socket (le vrai client n'en envoie jamais — 0 sur 4 sessions MITM ; 4 /whois par connexion = signature).
        // L'id est TOUJOURS mémorisé (Lua serverId(), équipe même serveur) ; au 1er lancement de l'app les tables de noms se
        // chargent encore en fond -> le nom est résolu plus tard, à chaque carte chargée (TryResolveSocketServerName).
        MarkServerKnown(pid);
        var name = _gameNames.ServerName(serverId);
        RememberServerIdentity(pid, serverId, name ?? "");
    }

    // Bot socket dont le nom de serveur n'était pas résolvable à l'étape A (tables de noms pas encore chargées) : nouvel essai
    // à chaque carte chargée, sans jamais forger de WhoAmI.
    private void TryResolveSocketServerName(uint pid)
    {
        if (!_serverInfo.TryGetValue(pid, out var s) || s.Id == 0 || !string.IsNullOrEmpty(s.Name)) return;
        if (_gameNames.ServerName(s.Id) is not { } name) return;
        RememberServerIdentity(pid, s.Id, name);
    }

    /// <summary>MODE SOCKET : prépare le décodeur (catalogue branché sur les forges ET l'enveloppe, tables de noms lancées en
    /// fond) AVANT la première trame. Sans ça la 1re IdentificationRequest d'une session était forgée avec les slots
    /// d'enveloppe de repli (catalogue pas encore branché) et les noms de serveur n'étaient pas prêts à l'étape A.</summary>
    public void WarmUpForSocket()
    {
        StartAntiAfk();
        try { EnsureDecoder(); } catch { /* best-effort : SocketGameClient branche aussi l'enveloppe lui-même */ }
        try { _gameNames.EnsureServerNamesLoaded(); } catch { /* ~70 ms : noms de serveur prêts dès l'étape A */ }
    }

    /// <summary>Pid SYNTHÉTIQUE d'une session Socket (≥ 0x40000000, impair) : jamais un vrai process Windows.</summary>
    internal const uint SocketPidBase = 0x4000_0000;
    internal static bool IsSocketPid(uint pid) => pid >= SocketPidBase;

    // Fenêtre d'attente de la confirmation SERVEUR d'un déplacement de combat (SelfCell qui change). En MITM elle n'absorbe
    // que la latence (~35 ms). En SOCKET, la trame peut d'abord être RETENUE par le client socket jusqu'à l'envoi de notre
    // accusé de l'action précédente + l'espacement d'origine (comme le vrai client qui finit son animation) : la fenêtre
    // part de la mise en file, pas de l'envoi réel -> +3 s, sinon un déplacement simplement retenu était pris pour REFUSÉ
    // (case bloquée, replanification depuis une case périmée). Un vrai refus arrive toujours explicitement (jqt) et coupe
    // l'attente tout de suite.
    private static int MoveConfirmWindowMs(uint pid) => IsSocketPid(pid) ? 800 + 3000 : 800;

    // GATE D'INJECTION : le transport n'est rendu aux primitives de FORGE (move/cast/harvest/whoami/banque…) QUE si la
    // session est autorisee. Une session bannie/expiree/killee (LicenseService.IsAuthorized=false) => null => toute
    // injection est refusee (les methodes retournent false sur 'relay is null'). Defense en profondeur avec la PURGE
    // du mapping (decodeur aveugle) : ici on bloque AUSSI les scripts qui n'ont pas besoin de decoder. L'OBSERVATION
    // MITM (tap/decodeur) continue via _mitm directement -> le relais reste actif, seule la FORGE est coupee.
    /// <param name="capExempt">Trames qui ne sont PAS du botting et doivent partir même pour un perso au-delà du cap de niveau
    /// de la licence : confirmation d'un déplacement déjà envoyé, sortie propre d'un dialogue / échange, identification du serveur.</param>
    private IGameTransport? RelayForInject(bool capExempt = false)
    {
        if (!LicenseService.CanBot) return null;   // licence non autorisée OU botting suspendu (reprise de session bloquée)
        // Rien a piloter (ni relais MITM demarre, ni bot socket enregistre) -> null, comme avant (semantique preservee).
        if (_mitm is null && _socketInjectors.IsEmpty) return null;
        return capExempt ? (_dispatchExempt ??= new DispatchTransport(this, capExempt: true)) : (_dispatch ??= new DispatchTransport(this, capExempt: false));
    }
    private DispatchTransport? _dispatchExempt;

    // Cap de niveau licence au POINT DE PASSAGE UNIQUE de toutes les actions forgées (scripts, console, équipe, résurrection,
    // craft, clics de l'app… en MITM comme en Socket) : perso au-delà du cap -> refus. Niveau inconnu toléré ici (les
    // démarrages, eux, l'exigent connu). Le jeu manuel dans Dofus n'est jamais concerné (relayé, pas injecté).
    private bool CapRefuses(uint pid)
    {
        if (!LevelBlocked(pid)) return false;
        long now = Environment.TickCount64;
        if (now - _capRefusedLogTick.GetValueOrDefault(pid) > 30_000)
        {
            _capRefusedLogTick[pid] = now;
            TransportLog.Line($"[licence] pid {pid} : action refusée — niveau {CharacterLevel(pid)} au-delà du cap de la licence ({LicenseService.LevelCap}).");
        }
        return true;
    }
    private readonly ConcurrentDictionary<uint, long> _capRefusedLogTick = new();

    /// <summary>Transport de DISPATCH : Inject(pid,..) part vers l'injecteur SOCKET du pid s'il existe, sinon vers le
    /// relais MITM. Transparent pour les ~40 sites de forge (ils appellent toujours RelayForInject().Inject(pid, trame)).
    /// OnData/Start ne sont utilises que par le cablage MITM (qui passe par _mitm en direct) -> simples passe-plats.</summary>
    private sealed class DispatchTransport : IGameTransport
    {
        private readonly LaunchService _svc;
        private readonly bool _capExempt;
        public DispatchTransport(LaunchService svc, bool capExempt) { _svc = svc; _capExempt = capExempt; }
        public string Name => "dispatch";
        public bool Inject(uint pid, byte[] framedToServer)
        {
            if (!_capExempt && _svc.CapRefuses(pid)) return false;   // cap de niveau licence (offre gratuite)
            if (_svc._socketInjectors.TryGetValue(pid, out var inj)) return inj(framedToServer);
            // Bot socket SANS session vive (coupure / reconnexion en cours) : l'action échoue proprement. Jamais de repli sur
            // le relais MITM, qui n'a aucune connexion pour ce pid (logs « AUCUNE connexion amont » trompeurs).
            if (IsSocketPid(pid)) return false;
            // Lien de jeu MITM SUSPENDU (kick serveur, connexion de jeu fermée, OU client sorti de la session et attendu en jeu
            // pendant le délai de grâce) : la connexion courante du pid n'est plus celle d'une session de jeu (Dofus.exe est à
            // l'écran de connexion / de sélection) -> aucune trame de jeu ne doit y partir. Un pid socket n'y entre jamais.
            if (_svc.IsLinkSuspended(pid)) return false;
            return _svc._mitm?.Inject(pid, framedToServer) ?? false;
        }
        public Action<uint, bool, byte[]>? OnData
        {
            get => _svc._mitm?.OnData;
            set { if (_svc._mitm is not null) _svc._mitm.OnData = value; }
        }
        public Action<uint, bool, bool>? OnClosed
        {
            get => _svc._mitm?.OnClosed;
            set { if (_svc._mitm is not null) _svc._mitm.OnClosed = value; }
        }
        public void Start() => _svc._mitm?.Start();
    }

    public LaunchService()
    {
        if (LocalEdition.Enabled)
        {
            _localAuth = new LocalLaunch.LocalZaapAuthority(FreeLoopbackPort(), FreeLoopbackPort(), LocalConnectionHosts(), m => TransportLog.Line(m));
            _localAuth.ItemOverlaySettingsPath = ItemOverlayPreferences.FilePath;
            _zaap = new ZaapEmulator(_localAuth.ZaapPort);
            _zaap.ConfigureOwnedLoopbackTest(_localAuth);
            _launcher = new GameLauncher(_localAuth);
        }
        else { _zaap = new ZaapEmulator(26666); _launcher = new GameLauncher(); }
        // Purge du mapping (kill/ban/logout) -> on coupe le décodeur EN COURS et on lâche le catalogue.
        MappingCatalog.Cleared += OnMappingCleared;
    }

    /// <summary>Sur PURGE du mapping (session non autorisée) : coupe le feed du décodeur en cours et LÂCHE toutes les
    /// références au catalogue -> le bot devient AVEUGLE et le joyau quitte la RAM (GC). Le relais reste actif (route
    /// le trafic, anti-fuite d'IP) mais n'observe plus. La reprise du décodage après un kill exige un relancement de
    /// l'app (cas rare : un kill affiche déjà l'écran de blocage) -> aucune régression du chemin de login nominal.</summary>
    private void OnMappingCleared()
    {
        try { var r = _mitm; if (r is not null) r.OnData = null; } catch { /* best-effort */ }
        _decoder = null; _catalog = null; _decoderInit = false;
    }
    private readonly ConcurrentDictionary<uint, ProxySettings> _pidProxy = new();
    private readonly ConcurrentDictionary<uint, ProxySettings> _pidGameProxy = new();   // proxy du serveur de jeu, si différent
    private readonly ConcurrentDictionary<uint, string> _pidGameProxyLabel = new();     // son nom (journal de connexion)
    private readonly ConcurrentDictionary<uint, string> _pidLogin = new();   // pid client MITM -> login du compte (pour l'observateur device_identifier)
    private readonly HashSet<uint> _whoAmISent = new();   // pids dont la detection serveur AUTO a deja ete LANCEE
    private readonly HashSet<uint> _serverKnown = new();  // pids dont le serveur est deja identifie (stoppe les retries)
    private volatile byte[]? _whoAmiTemplate;             // trame REELLE (GameMessage lqb) capturee -> rejeu 100% fiable
    // Garde "un seul deplacement a la fois" par pid: ticks jusqu'auxquels un move injecte est
    // considere EN VOL. Un nouveau clic pendant ce laps est IGNORE (sinon il partirait d'une source perimee -> refus).
    private readonly ConcurrentDictionary<uint, long> _moveGuardUntil = new();
    private const long MoveGuardMs = 4000;
    // Confirmation d'arrivee (jqi) de SECOURS (ForgeAndSendMapMovementConfirmRequest) / un bot tiers (952): le
    // deplacement Dofus est un COMMIT EN 2 TEMPS -> jrw (demande) -> jsj (serveur accepte provisoirement) -> jqi (le client
    // confirme = COMMIT) -> jsq (serveur finalise). Le VRAI client (present, MITM) NE confirme PAS un move qu'il n'a pas
    // initie (surtout un long trajet) : sans jqi le serveur laisse le perso a sa case d'ORIGINE et REFUSE le jqk suivant
    // (bug 9->541 : jsj recu, jamais de jsq, puis jqt "tu es en case 9"). On envoie donc le jqi NOUS-MEMES, mais en SECOURS
    // uniquement (si le client/serveur n'a pas resolu avant) pour ne pas doubler le confirm du client (ancien desync).
    // pid -> move injecte en attente de confirm : (num de sequence UNIQUE, case de destination, nb de cases pour cadencer).
    // Le num de sequence est CAPITAL : la route sort presque toujours par la MEME case (541/543) -> sans lui, la tache de
    // confirm retardee d'un move confirmerait prematurement le move SUIVANT vers la meme case (rollback vitesse impossible).
    private readonly ConcurrentDictionary<uint, (long Seq, int Dest, int Cells)> _botMoveConfirm = new();
    private long _moveSeq;   // compteur monotone : identifie CHAQUE move injecte
    // Le serveur ROLLBACK un deplacement dont l'arrivee (jqi) est confirmee TROP TOT : confirmer 24 cases en 685 ms = vitesse
    // impossible -> le perso est renvoye a sa case d'ORIGINE (bug observe). On cadence donc le jqi de secours sur la DUREE
    // d'animation du trajet (~ nb de cases * ms/case), exactement Thread.Sleep(TimeForMove) avant son 952.
    private const int MoveConfirmPerCellMs = 200;    // duree ~ animation de marche par cellule
    // MARGE : le VRAI client marche ~300 ms/case + latence, et sur un move COURT (recolte) il confirme AUSSI. Sans marge,
    // notre secours (200 ms/case) partait AVANT le jqi du client -> DOUBLE confirm -> desync (perso reste a l'origine) et
    // l'iwo de recolte partait sur notre jsq premature -> "hors portee". On decale donc le secours pour que le jqi du client
    // (qui retire _botMoveConfirm et annule notre secours) gagne TOUJOURS la course : le secours redevient un vrai dernier
    // recours (seulement quand le client ne confirme PAS, ex certaines transitions).
    private const int MoveConfirmMarginMs = 700;
    private const int MoveConfirmMinMs = 900;        // plancher : > latence de confirm du client sur un move court
    private const int MoveConfirmMaxMs = 8000;       // plafond (garde le changement de carte sous les timeouts Lua)

    // MODE SOCKET : il n'y a PAS de vrai client pour confirmer -> notre jqi est la SEULE confirmation. La marge MITM ci-dessus
    // (exprès en retard pour que le client gagne la course) y devient un pur handicap : trop LENT sur les longs trajets et,
    // pire, TROP TÔT sur les courts (3 cases : 900-1300 ms). On cadence donc sur la durée d'animation du VRAI client DOFUS 3,
    // MESURÉE en MITM (journaux 2026-09-23/24: trame jrw -> jqi confirmés par Dofus.exe). Les constantes d'un bot tiers (Dofus 2:
    // course 170/255/150 + 256 ms, marche jusqu'à 3 pas) rendaient le socket nettement plus LENT que le MITM :
    // - MARCHE (1-2 pas): 99 trajets, résidu ±25 ms -> 482 lin / 510 horiz / 432 vert + 36 ms;
    //  - COURSE (≥ 3 pas, le vrai client court DÈS 3 pas) : perso À PIED (Persotrois/Maxi niv ≤10, trajets manuels) ->
    // 149 lin / 216 horiz / 135 vert + 27 ms, soit ~12 % plus rapide que un bot tiers. ⚠ Persoun (niv 139, 645 trajets) court
    //    encore ~25 % plus vite (117/170/106) : MONTURE -> NON retenu (un perso à pied confirmerait trop tôt = rollback).
    //    Un perso monté est donc confirmé un peu plus tard que nécessaire : côté SÛR.
    // + marge de sécurité 80 ms (jitter réseau / minuterie), confirmation à durée + 10-20 ms APRÈS L'ENVOI du trajet.
    private const double WalkLinearMs = 482, WalkHorizontalDiagMs = 510, WalkVerticalDiagMs = 432, WalkBaseMs = 36;
    private const double RunLinearMs = 149, RunHorizontalDiagMs = 216, RunVerticalDiagMs = 135, RunBaseMs = 27;
    private const int WalkMaxSteps = 2;          // au-delà, le client Dofus 3 COURT (mesuré : 3 pas = course, ~460 ms)
    private const int ClientMoveSafetyMs = 80;
    private readonly ConcurrentDictionary<uint, (long Seq, long SentAt)> _botMoveSentAt = new();   // pid -> instant d'ENVOI du jrw (base de la cadence socket)

    /// <summary>Durée d'animation d'un trajet côté client Dofus 3 (mesurée en MITM, perso à pied) + marge de sécurité. Pas
    /// selon l'écart de cellId : ±1 = diagonale HORIZONTALE (orientation 0/4), ±28 = diagonale VERTICALE (2/6), sinon
    /// LINÉAIRE (±13/14/15, orientations impaires). 1-2 pas = marche, sinon course.</summary>
    internal static int ClientMoveDurationMs(IReadOnlyList<int> path, bool turbo = false)
    {
        int steps = Math.Max(0, path.Count - 1);
        bool walk = steps <= WalkMaxSteps;
        double lin = walk ? WalkLinearMs : RunLinearMs, hDiag = walk ? WalkHorizontalDiagMs : RunHorizontalDiagMs,
               vDiag = walk ? WalkVerticalDiagMs : RunVerticalDiagMs, total = walk ? WalkBaseMs : RunBaseMs;
        for (int i = 1; i < path.Count; i++)
        {
            int d = Math.Abs(path[i] - path[i - 1]);
            total += d == 1 ? hDiag : d == 28 ? vDiag : lin;
        }
        int safety = turbo ? 20 : ClientMoveSafetyMs;
        return (int)total + safety;
    }

    private const long PendingMapChangeMaxAgeMs = 12000;   // au-dela, un changement de carte "en attente d'arrivee" est PERIME (jsq du move de transition perdu) -> on ne le declenche pas sur un move ulterieur sans rapport
    private const bool MoveDiag = true;   // DIAGNOSTIC deplacement : trace le chemin injecte, le choix de cellule de sortie, et le chemin MANUEL du client (repasser a false une fois le probleme compris)

    /// <summary>Detection serveur AUTO (1x/pid) : forge un WhoAmIRequest (lqb) -&gt; WhoIsEvent (lqe) porteur du serverId.
    /// Ancree sur l'ENTREE EN JEU (1er MapComplementaryInformationEvent) + delai, avec RETRY jusqu'a reponse : le
    /// one-shot 2.5 s tirait souvent AVANT que le perso soit en jeu -&gt; serveur muet.</summary>
    private void TryForgeWhoAmI(uint pid)
    {
        // Bot socket : serveur connu dès l'étape A (SetSocketServerId). Le vrai client n'envoie JAMAIS de WhoAmI.
        if (IsSocketPid(pid)) return;
        lock (_whoAmISent) { if (!_whoAmISent.Add(pid)) return; }
        _ = Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < 4 && !IsServerKnown(pid); i++)
                {
                    await Task.Delay(3000).ConfigureAwait(false);
                    if (IsServerKnown(pid)) break;
                    InjectWhoAmI(pid, $"auto#{i + 1}");
                }
            }
            catch (Exception e) { TransportLog.Line($"[GivreClone] WhoAmI auto pid {pid} ERREUR : {e.Message}"); }
            finally
            {
                // Toujours pas de serveur ? On rearme : le prochain changement de carte relancera un cycle.
                if (!IsServerKnown(pid)) { lock (_whoAmISent) _whoAmISent.Remove(pid); }
            }
        });
    }

    private void MarkServerKnown(uint pid) { lock (_serverKnown) _serverKnown.Add(pid); }
    private bool IsServerKnown(uint pid) { lock (_serverKnown) return _serverKnown.Contains(pid); }

    /// <summary>Detection serveur MANUELLE (bouton UI du bot connecte) : envoie maintenant, sans condition.</summary>
    public void DetectServer(uint pid) => InjectWhoAmI(pid, "manuel");

    /// <summary>
    /// Injecte un WhoAmIRequest vers le serveur de jeu du pid -> reponse WhoIsEvent (lqe) qui porte le serverId.
    /// Prefere la trame REELLE capturee (rejeu exact du /whoami du client); sinon forge.
    /// </summary>
    private void InjectWhoAmI(uint pid, string origin)
    {
        var relay = RelayForInject(capExempt: true);   // identification du serveur (télémétrie, même serveur), pas du botting
        if (relay is null) { TransportLog.Line($"[GivreClone] WhoAmI {origin} pid {pid} : relais absent"); return; }
        var tpl = _whoAmiTemplate;
        // Le code de WhoAmIRequest TOURNE a chaque patch (lqb en 3.6.10.11, lnz en 3.6.11.12) : on le lit dans le
        // mapping au lieu de le figer, sinon la forge injecte un type_url que le serveur ignore en silence.
        // Code ET numéro de champ : mapping streamé UNIQUEMENT (aucun repli figé). Mapping incomplet -> pas de forge.
        byte[]? msg = tpl;
        if (msg is null)
        {
            if (_catalog.CodeForName("WhoAmIRequest") is not { } code || FieldOf("WhoAmIRequest", "verbose") is not int vf)
            { TransportLog.Line($"[GivreClone] WhoAmI {origin} pid {pid} : message absent du mapping -> rien injecté"); return; }
            msg = WhoAmIForge.BuildGameMessage(code, vf);
        }
        byte[] framed = WhoAmIForge.Frame(msg);
        bool ok = relay.Inject(pid, framed);
        string src = tpl is null ? "forge(uid=-1)" : "trame reelle capturee";
        TransportLog.Line($"[GivreClone] WhoAmI {origin} pid {pid} : {src} {framed.Length}o{(LicenseService.IsDeveloper ? $" [{Convert.ToHexString(framed)}]" : "")} inject={(ok ? "OK" : "ECHEC (pas de flux amont)")}");
    }

    // Affiche dans la CONSOLE des paquets une trame qu'on vient d'INJECTER (elle ne passe pas par le tap d'observation).
    // Decodage AUTONOME (pas via le buffer partage) -> aucun risque de corrompre le deframing du flux reel.
    private void ShowInjected(uint pid, byte[] framed)
    {
        if (_catalog is null) return;
        try
        {
            bool meaningful = false;
            foreach (var frame in RawProtobuf.ConsumeFrames(framed, out _))
                foreach (var hit in RawProtobuf.FindTypeUrls(RawProtobuf.Parse(frame)))
                {
                    var name = _catalog.NameForCode(hit.Code);
                    if (name is not ("DialogLeaveRequest" or "PlayerFightFriendlyAnswerRequest" or "MapInformationRequest" or "ContextReadyRequest" or "PlayerStatusUpdateRequest")) meaningful = true;   // anti-stuck : le close-dialog de récupération ne compte PAS comme progrès (cf DriveAntiStuck) ; Lot K : ni un rafraîchissement de carte ni un changement de statut en boucle
                    PacketDecoded?.Invoke(new DecodedPacket(pid, true, hit.Code, name,
                        hit.Payload ?? (IReadOnlyList<ProtoField>)Array.Empty<ProtoField>(), frame));
                }
            if (meaningful) _lastAction[pid] = Environment.TickCount64;   // horodate la dernière ACTION forgée (signal de progrès du watchdog)
        }
        catch { /* affichage best-effort */ }
    }

    // Watchdog anti-stuck : horodatage de la dernière action forgée UTILE par pid (hors DialogLeaveRequest). Alimente
    // LastActionAgeMs, lu par BotsViewModel.DriveAntiStuck pour détecter un script qui tourne mais n'agit plus.
    private readonly ConcurrentDictionary<uint, long> _lastAction = new();

    /// <summary>Ms écoulées depuis la dernière action forgée utile de `pid` (long.MaxValue si jamais agi). Signal du watchdog anti-stuck.</summary>
    public long LastActionAgeMs(uint pid) => _lastAction.TryGetValue(pid, out var t) ? Environment.TickCount64 - t : long.MaxValue;

    /// <summary>Réarme le compteur d'inactivité de `pid` (= « le bot vient de progresser »). Appelé au démarrage d'un script (grâce).</summary>
    public void NoteProgress(uint pid) => _lastAction[pid] = Environment.TickCount64;

    private CaptureDecoder? _decoder;
    private global::GivreClone.App.Services.Transport.Socket.DeviceIdentifierObserver? _deviceObserver;   // observateur PASSIF du login MITM : device_identifier AVANT/APRÈS en Console
    private ProtocolCatalog? _catalog;
    private MapPositions? _mapPositions;
    private readonly MapCellReader _cellReader = new();

    // ---- Pre-extraction des cartes (cache disque) : pilotee par le modal de progression au 1er lancement / apres un
    //      patch Dofus. Decompresse tous les bundles une fois -> chargements de carte instantanes ensuite. ----
    /// <summary>Client Dofus detecte (bundles de cartes accessibles) ? Sinon rien a pre-extraire.</summary>
    public bool MapDataAvailable => _cellReader.Available;
    public (int DeclaredMaps, bool DeclaredComplete) MapCacheManifestSummary() => _cellReader.ManifestSummary();
    /// <summary>Nombre de cartes a (re)extraire : 0 si le cache disque est deja a jour. Construit l'index au passage.</summary>
    public int MapPrewarmPending() => _cellReader.PrewarmPendingCount();
    /// <summary>Pre-extrait toutes les cartes vers le cache disque (tache de fond), en rapportant (fait, total).</summary>
    public async Task PrewarmAllMapsAsync(IProgress<(int done, int total)>? progress, CancellationToken ct = default)
    {
        // Processus enfant en GC serveur (≈ 3x plus rapide) ; s'il échoue ou laisse des cartes manquantes, on termine dans ce processus.
        if (await MapExtractChild.TryRunAsync(progress, ct).ConfigureAwait(false) && _cellReader.PrewarmPendingCount() == 0) return;
        await _cellReader.ExtractAllAsync(progress, ct).ConfigureAwait(false);
    }

    private readonly GameNameResolver _gameNames = new();
    /// <summary>Resolveur de noms/donnees du jeu (sorts par classe, noms de sorts...) pour l'UI (onglet Combat).</summary>
    public GameNameResolver GameNames => _gameNames;
    private readonly ConcurrentDictionary<uint, long> _lastMap = new();
    private volatile WorldGraph? _worldGraph;                                  // graphe des transitions inter-cartes (client)
    private readonly ConcurrentDictionary<uint, (int Target, long SetAt, CancellationToken Owner)> _pendingMapChange = new(); // pid -> (carte cible, instant) a changer une fois arrive a la cellule de transition ; perime si le jsq d'arrivee n'arrive jamais
    private readonly ConcurrentDictionary<uint, (int Skill, int Element, bool Gather, CancellationToken Owner)> _pendingHarvest = new(); // pid -> (skillUid, elementId, vraie ressource ?) a utiliser une fois arrive a la cellule adjacente
    private readonly ConcurrentDictionary<uint, (long Group, string? Name, CancellationToken Owner, long MapId, Func<bool>? TargetStillCurrent, long SetAt, long MoveSeq, int ArrivalCell, Func<int, bool>? ConfirmedArrival, Func<int, Func<bool>, bool>? SubmitWithLease)> _pendingAttack = new(); // pid -> cible/lease + mouvement serveur corrélé
    private readonly ConcurrentDictionary<uint, (int Cur, int Max)> _lastPods = new(); // pid -> dernier poids/pods connu (InventoryWeightEvent)
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, int>> _jobLevels = new(); // pid -> (job_id -> niveau) (JobExperiencesUpdateEvent / JobLevelUpEvent)
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, JobProgressSnapshot>> _jobProgress = new(); // progression métier facultative, sans inventer l'XP
    private readonly ConcurrentDictionary<uint, IReadOnlyList<ProtoField>> _lastNpcDialog = new();   // pid -> dernier NpcDialogQuestionEvent (réponses visibles pour replyNpc)
    private readonly ConcurrentDictionary<uint, bool> _inFight = new(); // pid -> en combat (pour interpreter kmk comme des combattants)
    private readonly ConcurrentDictionary<uint, long> _fightId = new(); // pid -> fight_id du combat EN COURS (0 = aucun) ; sert à l'auto-join coordonné d'équipe (un follower rejoint le combat du chef)
    private readonly ConcurrentDictionary<uint, IReadOnlyList<(int Cell, long ActorId)>> _fighters = new(); // pid -> derniers combattants (kmk)
    private readonly ConcurrentDictionary<uint, long> _serverMapId = new(); // pid -> carte AUTORITAIRE (MapCurrentEvent), utilisée pour la forge ET le relief si connu ; instance inconnue -> relief précédent.

    /// <summary>Oublie l'état par-pid qui alimente ActivityOf (combat / carte / récolte / déplacement), quand un client
    /// est ARRÊTÉ/tué hors du cycle de combat normal (un kill ne déclenche ni FightEndEvent ni carte de jeu de rôle, donc
    /// _inFight resterait à true). Sans ça, un pid RÉUTILISÉ par l'OS pour un nouveau client hériterait d'un état périmé
    /// (ex. « Combat » affiché pendant sa connexion). Libère aussi ces entrées à chaque arrêt (pas d'accumulation).</summary>
    public void Forget(uint pid)
    {
        CancelWorldMirror(pid, null);
        _worldMirrorFrames.Forget(pid);
        InvalidateCombatMap(pid);
        _serverMapId.TryRemove(pid, out _);
        // Inventory and effects are part of the CHARACTER session, not the
        // process. A recycled PID must never see the old character's equipment,
        // bank balance or kamas before fresh server snapshots arrive.
        Inventory.Clear(pid); Bank.Clear(pid); ForgetItemEffects(pid);
        ForgetCharacterSheet(pid);
        _kamas.TryRemove(pid, out _); _bankKamas.TryRemove(pid, out _);
        _charId.TryRemove(pid, out _);
        _charLevel.TryRemove(pid, out _); _characters.TryRemove(pid, out _);   // pid réutilisable par l'OS : jamais le niveau d'un autre perso
        ForgetHdv(pid);   // lots, ventes, état d'ouverture : jamais ceux d'un autre perso (télémétrie du panel)
        ForgetHdvTracking(pid);   // HDV Tracker : lots suivis, retraits/ventes/achats en attente de ce pid
        _inFight.TryRemove(pid, out _);
        _fightId.TryRemove(pid, out _); _myTurnOpen.TryRemove(pid, out _);
        PurgeFightState(pid);   // un client arrete ne fournit pas toujours de fin de combat
        _fightEndAt.TryRemove(pid, out _);
        _lifeStatus.TryRemove(pid, out _);
        _phoenixMap.TryRemove(pid, out _);
        _lastMap.TryRemove(pid, out _);
        _gatherUntil.TryRemove(pid, out _);
        _movingUntil.TryRemove(pid, out _);
        _moveGuardUntil.TryRemove(pid, out _);
        _botMoveConfirm.TryRemove(pid, out _); _botMoveSentAt.TryRemove(pid, out _);   // move en attente de confirm d'un client arrêté
        _gatherElements.TryRemove(pid, out _);
        _archSeen.TryRemove(pid, out _);
        _lastAction.TryRemove(pid, out _);   // anti-stuck : oublie le compteur d'action (pid réutilisable par l'OS)
        ForgetAntiAfk(pid);
        _openTeleportDests.TryRemove(pid, out _);   // dialogue de téléport d'un client arrêté : périmé
        _knownZaaps.TryRemove(pid, out _);
        _directZaaps.TryRemove(pid, out _);
        _worldMapZaaps.TryRemove(pid, out _);
        _zaapRoute.TryRemove(pid, out _);
        _openTeleportType.TryRemove(pid, out _); _teleportDialogSeq.TryRemove(pid, out _); _zaapListSeq.TryRemove(pid, out _);
        _teleportOpen.TryRemove(pid, out _);
        ResetScriptFightState(pid);                  // hooks de combat Lua : état de tour/placement périmé
        _scriptTurnHook.TryRemove(pid, out _); _scriptPlacementHook.TryRemove(pid, out _); _autoFightOverride.TryRemove(pid, out _);
        _manualOwner.TryRemove(pid, out _);
        _serverInfo.TryRemove(pid, out _); _selfInfo.TryRemove(pid, out _);   // Lot B : identité/serveur/points périmés
        // Lot C2 : défis + bilan de combat d'un client arrêté (pid réutilisable par l'OS).
        _challengeProps.TryRemove(pid, out _); _challengeRequested.TryRemove(pid, out _); _challengeChosen.TryRemove(pid, out _);
        _challengeResults.TryRemove(pid, out _); _placementOver.TryRemove(pid, out _);
        ForgetChallengeFight(pid); _challengeBonusKind.TryRemove(pid, out _); _bonusReannounced.TryRemove(pid, out _);
        _fightStartAt.TryRemove(pid, out _); _fightResultStored.TryRemove(pid, out _); _lastFightResult.TryRemove(pid, out _);
        // Lot D : coffre ouvert + état d'échange + mode mule d'un client arrêté.
        _storageOpenMap.TryRemove(pid, out _);
        _tradeOpen.TryRemove(pid, out _); _tradeStep.TryRemove(pid, out _); _tradePending.TryRemove(pid, out _); _tradeAutoTeam.TryRemove(pid, out _);
        _tradeExpected.TryRemove(pid, out _); _tradeStartedPkt.TryRemove(pid, out _); _storageContentPkt.TryRemove(pid, out _);
        _tradePlaced.TryRemove(pid, out _);
        ForgetInteraction(pid);   // fenêtre d'interaction : contenu d'échange des deux côtés
        ForgetLotE(pid);   // Lot E : défi en attente, options de combat, popup, respawn, variante
        ForgetLotF(pid);   // Lot F : PV/régénération/énergie/paliers d'XP/emotes d'un client arrêté
        ForgetLotG(pid);   // Lot G : état du groupe Dofus d'un client arrêté
        ForgetSnow(pid);   // compat tiers: historique de paquets d'un client arrêté
        ForgetLotI(pid);   // Lot I : quêtes (terminées/en cours), sous-zone live, erreurs d'objet d'un client arrêté
        ForgetLotJ(pid);   // Lot J : maisons de la carte, coffre à code ouvert
        ForgetLotK(pid);   // Lot K : compteur de carte, statut joueur, cooldown resetStats, défis actifs
        ForgetGameLink(pid);   // lien de jeu : coupure / session vivante / attente de retour en jeu d'un client arrêté
        _autoPick.Forget(pid);
        _clientSelectedAt.TryRemove(pid, out _);
        _characterListAt.TryRemove(pid, out _);
        _deviceObserver?.Forget(pid); _pidLogin.TryRemove(pid, out _);   // observateur device_identifier : tampon + binding pid->compte périmés (la valeur persistée reste, elle sert d'AVANT)
        NativeTurboRuntime.RequestState((int)pid, enabled: false);
        NativeTurboRuntime.Forget((int)pid);
    }

    public async Task<LaunchOutcome> LaunchAsync(LaunchRequest req, IShieldCodePrompt shield, Action<string>? status = null,
        Action<string, string>? onCertMinted = null)
    {
        // MZP9 admission P0: the historical constructor has no independent TEST
        // launcher/Zaap authority and reserves ports 26666/5658, which could belong
        // to the original GivreClone and its three active sessions. Do NOT contact
        // HAAPI, read account secrets, open Zaap, or create a Dofus process first.
        // Installing a separately owned, verified TEST transport/launcher is a
        // prerequisite, never inferred from an account name or caller-provided PID.
        if (!_launcher.HasIndependentTestAuthority || !_zaap.HasIndependentTestAuthority)
            return new LaunchOutcome(false, 0,
                "MITM TEST indisponible : launcher indépendant et protocole Zaap TEST non admis. Aucun compte ni client touché.");

        // Deux modes :
        //  - AVEC proxy  : isolation garantie fail-closed (relais + hook GivreKama confirme, sinon on tue le client).
        //  - SANS proxy  : lancement direct, IP reelle (choix explicite de l'utilisateur, aucune isolation).
        try
        {
            using var auth = new AnkamaAuth(req.Proxy, shield) { CertificateHasher = LocalLaunch.AnkamaOAuth.HashForStoredCertificate };
            string token;
            CoreAccount? refreshAccount = null;

            if (req.Source == AuthSource.KeyData)
            {
                status?.Invoke(Loc.T("L.Launch.AuthKeydata"));
                var all = await Task.Run(ZaapKeydataReader.ReadInstalledLauncherKeydataAsync).ConfigureAwait(false);
                var kd = all.FirstOrDefault(k => k.Login.Equals(req.Login, StringComparison.OrdinalIgnoreCase));
                if (kd is null)
                    return new LaunchOutcome(false, 0, Loc.T("L.Launch.NoAccount"));
                token = await auth.AuthenticateFromKeyDataAsync(kd).ConfigureAwait(false);
            }
            else
            {
                status?.Invoke(Loc.T("L.Launch.Auth"));
                var coreAcct = new CoreAccount
                {
                    Login = req.Login, Password = "", Proxy = req.Proxy,
                    ApiKey = req.ApiKey, RefreshToken = req.RefreshToken, AutoConnectType = req.AutoConnectType,
                    // Cert Shield persisté (compte déjà sécurisé) : réutilisé tel quel par CreateToken -> pas de re-mint.
                    Certificate = (req.CertId is { Length: > 0 } cid && req.CertEncoded is { Length: > 0 } ce)
                        ? new ShieldCertificate { Id = cid, EncodedCertificate = ce } : null,
                };
                refreshAccount = coreAcct;
                try
                {
                    token = await auth.AuthenticateAsync(coreAcct).ConfigureAwait(false);
                }
                finally
                {
                    // Le cert Shield est l'artefact IRRÉVERSIBLE (Ankama ne redonne jamais l'encodedCertificate). On le
                    // PERSISTE dès qu'il est forgé, en finally -> même si le CreateToken qui suit le mint échoue (proxy 4G
                    // instable), le cert est sauvé. Sinon le compte reste SECURED sans cert = ALREADYSECURED définitif.
                    // MintCertificateAsync pose acct.Certificate AVANT l'appel token faillible, donc il est là même sur throw.
                    if (coreAcct.Certificate is { } minted && minted.Id != req.CertId)
                        onCertMinted?.Invoke(minted.Id, minted.EncodedCertificate);
                }
            }

            var hash = Guid.NewGuid().ToString();
            _zaap.RegisterAccount(hash, token, req.Login, req.AutoConnectType);
            if (_localAuth is not null && req.Source != AuthSource.KeyData && refreshAccount is not null)
                _localAuth.SetTokenRefresher(hash, async ct =>
                {
                    using var again = new AnkamaAuth(req.Proxy, shield) { CertificateHasher = LocalLaunch.AnkamaOAuth.HashForStoredCertificate };
                    return await again.AuthenticateAsync(refreshAccount, ct).ConfigureAwait(false);   // clé API + certificat déjà stockés : un nouveau jeton, sans saisie
                });
            // Compte du launcher Ankama : le client redemande un jeton à chaque (re)connexion (usage unique) -> on relit le keydata
            // (clé + certificat Shield à jour) et on en crée un frais, comme le fait le vrai lanceur à chaque auth_getGameToken.
            if (_localAuth is not null && req.Source == AuthSource.KeyData)
                _localAuth.SetTokenRefresher(hash, async ct =>
                {
                    var fresh = (await Task.Run(ZaapKeydataReader.ReadInstalledLauncherKeydataAsync, ct).ConfigureAwait(false))
                        .FirstOrDefault(k => k.Login.Equals(req.Login, StringComparison.OrdinalIgnoreCase));
                    if (fresh is null) return null;
                    using var again = new AnkamaAuth(req.Proxy, shield);
                    return await again.AuthenticateFromKeyDataAsync(fresh, ct).ConfigureAwait(false);
                });
            if (_localAuth is not null) { _localAuth.Expect(hash); _localAuth.StartConfigServer(); }
            if (!_zaapStarted) { _zaap.Start(); _zaapStarted = true; }

            // Relais + CAPTURE : demarre TOUJOURS (le pilotage passe par le hook+relais, AVEC ou SANS proxy). Sans proxy,
            // le relais connecte l'amont en DIRECT (IP reelle) ; avec proxy, il sort via le proxy (isolation).
            // Un miroir à chaud ne construit pas ce relais : son accepteur local ne compose pas d'amont.
            if (!_relayStarted && req.HotMirrorSocketPid == 0)
            {
                // Seuls les clients lancés PAR CETTE SESSION (pid -> compte, enregistré avant l'injection du hook) sont relayés :
                // un client resté ouvert d'une session précédente est refusé, jamais sorti en direct (anti-fuite d'IP).
                Action<uint, string, bool, bool> onTunnelOpened = (pid, dst, connectionServer, viaGameProxy) =>
                {
                    ConnectionJournal.ForBot(pid, Loc.Fmt(connectionServer ? "L.Journal.ConnServer" : "L.Journal.GameServer", dst));
                    if (connectionServer) OnReloginConnectionOpened(pid);
                    if (viaGameProxy)
                        ConnectionJournal.ForBot(pid, Loc.Fmt("L.Journal.GameProxySwitch",
                            _pidGameProxyLabel.TryGetValue(pid, out var lbl) ? lbl : "?"), LuaLogLevel.Warning);
                };
                var relay = new HandshakeRelay(pid => _pidProxy.TryGetValue(pid, out var p) ? p : null, 5658,
                                               knownPid: pid => _pidLogin.ContainsKey(pid))
                {
                    // Proxy du serveur de jeu (paramètres du compte) : la session de jeu bascule dessus, le reste garde le proxy du compte.
                    GameProxyForPid = pid => _pidGameProxy.TryGetValue(pid, out var gp) ? gp : null,
                    // Journal de connexion du bot : serveur de connexion, serveur de jeu, bascule sur le proxy de jeu.
                    OnGameTunnelOpened = onTunnelOpened,
                };
                if (LocalEdition.Enabled)
                {
                    // Édition locale : relais propre (destination réelle), sur un port libre transmis au hook (GIVRE_RELAY_PORT).
                    var local = new LocalLaunch.LocalHandshakeRelay(0, pid => _pidLogin.ContainsKey(pid), pid => GivreLauncher.Core.Launch.MZP9OwnedClientRegistry.IsOwnedAndLive((int)pid),
                        pid => _pidProxy.TryGetValue(pid, out var pp) ? pp : null, pid => _pidGameProxy.TryGetValue(pid, out var gp) ? gp : null, log: m => TransportLog.Line(m))
                    { OnGameTunnelOpened = onTunnelOpened, DropClientFrame = DropClientAck, DecodeBeforeForward = TurboDecodeFirst };
                    _localRelay = local;
                    _mitm = new MitmTransport(local);
                    _localRelayStart = () => { local.Start(); if (_localAuth is not null) _localAuth.RelayPort = local.Port; return local.Port; };
                }
                else _mitm = new MitmTransport(relay);
                var decoder = EnsureDecoder();
                // Observateur PASSIF du login de connexion : lit device_identifier (empreinte machine) et journalise AVANT/APRÈS
                // dans la Console du bot -> vérifie que l'usurpation de MAC (hook GivreKama, par compte) a changé l'empreinte.
                // Indépendant du décodeur (le login de connexion n'a pas besoin du catalogue) ; résout pid->login via _pidLogin.
                _deviceObserver = new global::GivreClone.App.Services.Transport.Socket.DeviceIdentifierObserver(pid => _pidLogin.TryGetValue(pid, out var l) ? l : null);
                var obs = _deviceObserver;
                _mitm.OnData = decoder is not null
                    ? (pid, c2s, b) =>
                    {
                        try { decoder.Feed(pid, c2s, b); obs.Feed(pid, c2s, b); }
                        catch
                        {
                            // Capture missed: never retain admission after an observer
                            // interrupted this chunk (which may contain another map).
                            NativeTurboRuntime.RevokeTacticalMapReady((int)pid);
                            throw;
                        }
                    }
                    : obs.Feed;   // sans catalogue : au moins l'observateur d'identification tourne (le relais route toujours)
                if (_localRelay is { } linkedGameRelay)
                {
                    // In the LOCAL MITM edition ONLY, the exact relay that
                    // forwards game bytes also issues their connection identity
                    // and monotone chunk sequence. The public PID-only OnData
                    // still processes login-server bytes; game-tunnel bytes
                    // instead enter this source-stamped path. A separate
                    // BotHostService frame cannot be joined to it by PID.
                    var ingressGate = new object();
                    var activeLinks = new Dictionary<uint, LocalLaunch.LocalHandshakeRelay.GameRelayLink>();
                    CaptureLinkIdentity CaptureLink(LocalLaunch.LocalHandshakeRelay.GameRelayLink link)
                        => new(link.RelayInstanceId, link.OwnerPid, link.RunEpoch, link.TunnelId);
                    bool CurrentCaptureLink(CaptureLinkIdentity link)
                        => linkedGameRelay.IsCurrentGameLink(new(
                            link.SourceInstanceId, link.Pid, link.RunEpoch, link.TunnelId));

                    linkedGameRelay.OnGameData = (link, sequence, c2s, bytes) =>
                    {
                        // Link validation must happen at the source, at the
                        // decoder, and again before the legacy game reducer.
                        // Transport identity is NOT a correlated gameplay ACK.
                        if (!linkedGameRelay.IsCurrentGameLink(link)) return;
                        try
                        {
                            if (decoder is not null)
                            {
                                var identity = CaptureLink(link);
                                bool linkedAdmitted;
                                try
                                {
                                    linkedAdmitted = decoder.FeedLinkScoped(identity, sequence,
                                        c2s, bytes, CurrentCaptureLink);
                                }
                                catch
                                {
                                    linkedAdmitted = false;
                                }
                                if (!linkedAdmitted)
                                {
                                    // This optional provenance diagnostic must
                                    // not interrupt the existing PID-only game
                                    // reducer or the TCP relay. It merely
                                    // revokes any possible tactical authority.
                                    NativeTurboRuntime.RevokeTacticalMapReady((int)link.OwnerPid);
                                }
                                else if (linkedGameRelay.IsCurrentGameLink(link))
                                {
                                    // Only this SAME relay actually produced
                                    // the game-tunnel bytes. A matching PID or
                                    // local decoder epoch from another source
                                    // could not establish this observation.
                                    ObserveLocalMitmRelayFrame(link, sequence, c2s,
                                        bytes.Length, linkedGameRelay.IsCurrentGameLink);
                                }
                                lock (ingressGate)
                                {
                                    if (!linkedGameRelay.IsCurrentGameLink(link)) return;
                                    // The old PID-only reducer must not stitch
                                    // a partial frame from an earlier game
                                    // tunnel to a new tunnel with the same PID.
                                    if (activeLinks.TryGetValue(link.OwnerPid, out var old)
                                        && old != link)
                                        decoder.Forget(link.OwnerPid);
                                    activeLinks[link.OwnerPid] = link;
                                    decoder.Feed(link.OwnerPid, c2s, bytes);
                                }
                            }
                            if (linkedGameRelay.IsCurrentGameLink(link))
                                obs.Feed(link.OwnerPid, c2s, bytes);
                        }
                        catch
                        {
                            NativeTurboRuntime.RevokeTacticalMapReady((int)link.OwnerPid);
                            throw;
                        }
                    };
                    linkedGameRelay.OnGameLinkClosed = (link, _) =>
                    {
                        decoder?.RevokeLink(CaptureLink(link));
                        _localMitmSourceAuthority?.RevokeExact(CaptureLink(link));
                        lock (ingressGate)
                            if (activeLinks.TryGetValue(link.OwnerPid, out var active)
                                && active == link) activeLinks.Remove(link.OwnerPid);
                        // Legacy OnClosed owns existing session teardown;
                        // this hook only retires exact-link diagnostics.
                    };
                }
                _mitm.OnClosed = OnRelayClosed;   // fermeture d'une connexion relayée -> session de jeu perdue ? (LaunchService.GameLink)
                try
                {
                    int relayPort = 5658;
                    if (_localRelayStart is not null) relayPort = _localRelayStart(); else _mitm.Start();
                    _relayStarted = true; TransportLog.Line($"[GivreClone] relais {relayPort} DEMARRE");
                    SocketLog.Info(0, $"transport {_mitm.Name} actif (relais {relayPort}) ; mode socket non disponible dans cette version");
                }
                catch (Exception e)
                {
                    // A failed relay is not a playable/pilotable session. Never
                    // continue toward client launch after a failed MITM listener.
                    TransportLog.Line($"[GivreClone] ECHEC demarrage relais 5658 ({e.GetType().Name}: {e.Message})");
                    return new LaunchOutcome(false, 0,
                        "Relais MITM non admis ou indisponible : lancement annulé avant création du client.");
                }
            }

            status?.Invoke(Loc.T("L.Launch.StartClient"));
            // Chemin Dofus.exe : custom persisté (installation hors standard) sinon auto-détection. Le VM a déjà garanti
            // qu'il existe (sélecteur au 1er échec) ; en cas d'absence tardive, GameLauncher lève et l'échec remonte proprement.
            var dofusExe = GameLauncher.ResolveDofusPath(AppConfig.Current.DofusExePath);
            // « Empreinte machine par compte » (MITM, TOUJOURS actif) : on transmet la graine PAR COMPTE à GivreKama.dll
            // via l'env GIVRE_HWSEED -> le hook falsifie les MAC dans Dofus.exe -> device_identifier unique par compte
            // (débloque 2 comptes MITM sur un serveur mono-compte). Graine absente = env non posé. (Socket : forgé côté GivreClone.)
            var hwSeed = string.IsNullOrEmpty(req.DeviceSeed) ? null : req.DeviceSeed;
            System.Diagnostics.Process proc;
            lock (_relayPortGate)
            {
                int previousRelay = 0;
                int mirrorPort = 0;
                bool swappedRelay = false;
                if (req.HotMirrorSocketPid != 0 && _localAuth is not null)
                {
                    previousRelay = _localAuth.RelayPort;
                    mirrorPort = EnsureWorldMirrorAcceptor().Port;
                    _localAuth.RelayPort = mirrorPort;
                    swappedRelay = true;
                }
                try
                {
                    proc = _launcher.Launch(hash, connectionType: req.AutoConnectType, dofusPath: dofusExe, hwSeed: hwSeed);
                }
                finally
                {
                    if (swappedRelay && _localAuth is not null) _localAuth.RelayPort = previousRelay;
                }
                if (req.HotMirrorSocketPid != 0)
                {
                    BindWorldMirror((uint)proc.Id, req.HotMirrorSocketPid);
                    TransportLog.Line(req.HotMirrorSocketPid, WorldMirrorSession.LaunchLog);
                    if (mirrorPort != 0)
                        TransportLog.Line(req.HotMirrorSocketPid, $"[miroir] client {proc.Id} lancé vers l'accepteur 127.0.0.1:{mirrorPort}");
                }
            }

            // pid -> compte : l'observateur d'identification résout le login pour comparer device_identifier AVANT/APRÈS par compte.
            _pidLogin[(uint)proc.Id] = req.Login;
            // Bind before hook injection: no other account can consume this selection.
            _autoPick.Bind((uint)proc.Id, req.CharacterAutoPick);

            // Sortie du pid : via son proxy si assigne (enregistre AVANT la connexion) ; sinon le relais sort en direct.
            if (req.Proxy is not null)
            {
                _pidProxy[(uint)proc.Id] = req.Proxy;
                TransportLog.Line($"[GivreClone] client pid {proc.Id} lance ; sortira via {req.Proxy.Kind} {req.Proxy.Host}:{req.Proxy.Port} (relais 5658)");
            }
            else TransportLog.Line($"[GivreClone] client pid {proc.Id} lance SANS proxy (IP reelle) ; capture via relais {(_localAuth?.RelayPort is > 0 and var rp ? rp : 5658)}");
            if (req.GameProxy is not null)
            {
                _pidGameProxy[(uint)proc.Id] = req.GameProxy;
                _pidGameProxyLabel[(uint)proc.Id] = req.GameProxyLabel ?? $"{req.GameProxy.Kind} {req.GameProxy.Host}:{req.GameProxy.Port}";
                TransportLog.Line($"[GivreClone] client pid {proc.Id} : serveur de jeu via {req.GameProxy.Kind} {req.GameProxy.Host}:{req.GameProxy.Port} (proxy de jeu du compte)");
            }

            // Hook GivreKama : requis pour la CAPTURE/pilotage (redirige le connect du jeu vers le relais). AVEC proxy
            // c'est AUSSI l'anti-fuite (echec -> on tue le client). SANS proxy, l'IP reelle est deja assumee -> on
            // n'EXIGE pas le hook : s'il echoue, le jeu tourne quand meme (brut, non pilote), sans fermeture.
            bool isolate = req.Proxy is not null || req.GameProxy is not null;
            // Sans hook, un client visible ouvrirait le vrai serveur de jeu et pourrait couper le Socket. Le chemin froid
            // sans proxy reste optionnel ; le miroir à chaud échoue fermé.
            bool hotMirror = req.HotMirrorSocketPid != 0;
            var dll = KamaDllPath();
            if (dll is null)
            {
                if (isolate || hotMirror)
                {
                    TryKill(proc.Id);
                    if (hotMirror) CancelWorldMirror(req.HotMirrorSocketPid, WorldMirrorSession.CancelledLog);
                    return new LaunchOutcome(false, proc.Id, hotMirror
                        ? "Le hook de la fenêtre de jeu est absent. La session Socket reste connectée."
                        : Loc.T("L.Launch.NoDll"));
                }
                return new LaunchOutcome(true, proc.Id, Loc.Fmt("L.Launch.HookAbsentNoProxy", proc.Id));
            }

            status?.Invoke(Loc.T("L.Launch.InjectHook"));
            if (!await InjectAndConfirmAsync(proc.Id, dll).ConfigureAwait(false))
            {
                if (isolate || hotMirror)
                {
                    TryKill(proc.Id);
                    if (hotMirror) CancelWorldMirror(req.HotMirrorSocketPid, WorldMirrorSession.CancelledLog);
                    return new LaunchOutcome(false, proc.Id, hotMirror
                        ? "Le hook de la fenêtre de jeu n'est pas confirmé. La session Socket reste connectée."
                        : Loc.T("L.Launch.HookUnconfirmed"));
                }
                return new LaunchOutcome(true, proc.Id, Loc.Fmt("L.Launch.HookUnconfirmedNoProxy", proc.Id));
            }

            // Mods natifs (GivreMods.dll) : no-anim + tactique. Injection BEST-EFFORT (non critique : un echec ne ferme
            // jamais le client). La DLL lit ensuite mods-<pid>.ini (ecrit par ModControl quand un mod est active).
            // Desactivable via Reglages -> "Mods natifs en jeu" : OFF = client 100% vanilla (diagnostic perf / test).
            if (AppConfig.Current.LightenBotClients) LightenClient(proc);
            if (AppConfig.Current.ModsEnabled && !AppConfig.Current.DofusEnhancedEnabled) _ = InjectModsAsync(proc.Id);
            else if (AppConfig.Current.ModsEnabled) _ = InjectModsWhenEnhancedUnavailableAsync(proc.Id);
            else TransportLog.Line($"[GivreClone] mods natifs DESACTIVES (Reglages) : client {proc.Id} lance vanilla (aucune injection GivreMods).");

            return new LaunchOutcome(true, proc.Id, isolate
                ? Loc.Fmt("L.Launch.PilotedProxy", proc.Id, $"{req.Proxy!.Kind} {req.Proxy.Host}:{req.Proxy.Port}")
                : Loc.Fmt("L.Launch.PilotedNoProxy", proc.Id));
        }
        catch (AnkamaAuthException ex)
        {
            TransportLog.Line($"[GivreClone] ECHEC lancement (auth Ankama) : {ex.Message}");
            return new LaunchOutcome(false, 0, ex.Message, NeedsReauth: req.Source == AuthSource.KeyData);
        }
        catch (Exception ex)
        {
            // La chaîne COMPLÈTE des inner exceptions. « The SSL connection could not be established, see inner
            // exception » ne dit RIEN sans son inner (certificat invalide / proxy injoignable / timeout / 407 auth…).
            // Cas fréquent : un proxy 4G/5G (hotspot) dont le tunnel :443 vers HAAPI passe mal (cf HandshakeRelay).
            // On journalise le détail dans la Console (transport-*.log) ET on renvoie un message ACTIONNABLE à l'UI,
            // plutôt que le message générique tronqué qui empêchait tout diagnostic.
            var detail = DescribeException(ex);
            var via = req.Proxy is not null ? $"{req.Proxy.Kind} {req.Proxy.Host}:{req.Proxy.Port}" : "direct (sans proxy)";
            TransportLog.Line($"[GivreClone] ECHEC lancement via {via} : {detail}");
            return new LaunchOutcome(false, 0, detail);
        }
    }

    // Aplati toute la chaîne d'exceptions (message + inner…) : sans ça un HttpRequestException « The SSL connection
    // could not be established, see inner exception » masque la VRAIE cause (certificat, refus/absence proxy, timeout,
    // 407 auth proxy…). Utilisé au lancement pour rendre l'erreur DIAGNOSTIQUABLE côté UI + Console.
    private static string DescribeException(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (sb.Length > 0) sb.Append("  →  ");
            sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
        }
        return sb.ToString();
    }

    // Injecte GivreKama et CONFIRME sa presence dans le client (liste des modules). Reessaie tant qu'il n'est pas
    // vu (le process peut ne pas etre injectable immediatement apres CreateProcess). ~3 s max.
    private static async Task<bool> InjectAndConfirmAsync(int pid, string dll)
    {
        if (!MZP25NativeModOwnership.MayControl(pid)) return false;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (NativeModules.IsLoaded(pid, "GivreKama.dll")) return true;
            // Permis à usage unique requis par DllInjector (sans lui l'injection est toujours refusée) : un par tentative.
            if (!MZP25InjectionPermit.TryRegister(pid, dll))
                TransportLog.Line($"[mod] pid {pid} : permis d'injection refusé (client non possédé/non vivant ou DLL non épinglée)");
            try { GivreCore.Injection.DllInjector.Inject(pid, dll); } catch { /* process pas encore pret */ }
            for (int i = 0; i < 4; i++)
            {
                if (NativeModules.IsLoaded(pid, "GivreKama.dll")) return true;
                await Task.Delay(150).ConfigureAwait(false);
            }
        }
        return NativeModules.IsLoaded(pid, "GivreKama.dll");
    }

    private static void TryKill(int pid)
    {
        // MZP9: the original GivreClone has three active sessions. A PID alone
        // cannot authorize termination: it may have been recycled or refer to
        // another owner's Dofus. Only the exact Process.Start handle enrolled
        // by our independently gated TEST GameLauncher is eligible.
        _ = GivreLauncher.Core.Launch.MZP9OwnedClientRegistry.TryStopOwned(pid);
    }

    private static string? KamaDllPath()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "GivreKama.dll");
        if (MZP19LocalClone.Enabled &&
            !MZP25NativeModCatalog.CheckFile(AppContext.BaseDirectory,
                MZP25NativeModCatalog.OwnedCaptureHook).Pinned)
        {
            TransportLog.Line("[mod] hook captation absent ou empreinte inattendue ; injection refusée pour le clone");
            return null;
        }
        return File.Exists(dll) ? dll : null;
    }

    private static void LightenClient(System.Diagnostics.Process proc)
    {
        try { ClientLightening.Apply(proc); }
        catch (Exception ex) { TransportLog.Line($"[mod] pid {proc.Id} : allègement du client impossible : {ex.Message}"); }
    }

    // Dofus Enhanced est activé : on le laisse posséder les crochets s'il se charge. S'il ne peut pas se charger sur ce client
    // (release.ini d'une autre version, NO_COMPATIBLE_RELEASE, DLL jamais chargée), les mods Givre prennent le relais.
    private static async Task InjectModsWhenEnhancedUnavailableAsync(int pid)
    {
        EnhancedVerdict verdict;
        try { verdict = await DofusEnhanced.ResolveForLaunchAsync(pid, AppConfig.Current.LightenBotClients).ConfigureAwait(false); }
        catch (Exception ex)
        {
            TransportLog.Line($"[mod] pid {pid} : attente de Dofus Enhanced impossible : {ex.Message}");
            return;
        }
        if (verdict != EnhancedVerdict.Incompatible) return;
        TransportLog.Line($"[mod] pid {pid} : Dofus Enhanced incompatible avec ce client -> mods Givre injectés");
        await InjectModsAsync(pid).ConfigureAwait(false);
    }

    // Injecte GivreMods.dll (no-anim + tactique) dans un client, best-effort et CONFIRME (liste des modules). Reessaie
    // le temps que le process soit injectable. N'echoue jamais bruyamment (mod optionnel).
    private static async Task InjectModsAsync(int pid)
    {
        if (!MZP25NativeModOwnership.MayControl(pid)) return;
        // The historically packed GivreMods DLL has no independent source
        // fingerprint in this reconstruction. In local clone mode never inject
        // it as a fallback for the separately built, pinned native host mods.
        if (MZP19LocalClone.Enabled)
        {
            TransportLog.Line("[mod] GivreMods original non admis dans le clone : utiliser modules reconstruits depuis les sources");
            return;
        }
        var dll = Path.Combine(AppContext.BaseDirectory, "GivreMods.dll");
        if (!File.Exists(dll)) return;
        for (int attempt = 0; attempt < 6 && !NativeModules.IsLoaded(pid, "GivreMods.dll"); attempt++)
        {
            try { GivreCore.Injection.DllInjector.Inject(pid, dll); } catch { /* pas encore pret */ }
            for (int i = 0; i < 4 && !NativeModules.IsLoaded(pid, "GivreMods.dll"); i++)
                await Task.Delay(200).ConfigureAwait(false);
        }
        if (NativeModules.IsLoaded(pid, "GivreMods.dll"))
        {
            TransportLog.Line($"[GivreClone] mods natifs (GivreMods.dll) injectes dans le client {pid}.");
            // GivreMods historique est prioritaire : s'il gagne la course contre le fallback autonome, on retire ce dernier
            // pour qu'un seul skipper d'animation reste propriétaire des hooks du client.
            NativeTurboRuntime.LegacyModsTookOwnership(pid);
        }
    }
}
