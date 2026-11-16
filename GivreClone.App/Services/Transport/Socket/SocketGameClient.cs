using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GivreClone.App.Services;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Proxy;

namespace GivreClone.App.Services.Transport.Socket;

/// <summary>
/// Local evidence of one successfully dialed clientless game TCP stream.
/// SyntheticPid is a routing key, not an OS owner or game authentication proof.
/// This type is deliberately distinct from MITM TransportLinkProvenance.
/// </summary>
internal readonly record struct SocketLinkProvenance(
    Guid ClientInstance, long RunEpoch, Guid ConnectionToken, uint SyntheticPid);

/// <summary>
/// Wire frame observed on that stream after successful local read/write.
/// Sequence is local ordering metadata, never an action UID or server ACK.
/// </summary>
internal sealed record SocketTransportFrameObservation
{
    // V9 compatibility: the public WireFrame shape stays byte[], but each
    // read is independently copied. A callback must not mutate the frame that
    // the following consumer or diagnostic decoder will see.
    private byte[] _wireFrame;

    public SocketTransportFrameObservation(long sequence, SocketLinkProvenance source,
        bool clientToServer, byte[] wireFrame)
    {
        ArgumentNullException.ThrowIfNull(wireFrame);
        Sequence = sequence;
        Source = source;
        ClientToServer = clientToServer;
        _wireFrame = (byte[])wireFrame.Clone();
    }

    public long Sequence { get; init; }
    public SocketLinkProvenance Source { get; init; }
    public bool ClientToServer { get; init; }
    public byte[] WireFrame
    {
        get => (byte[])_wireFrame.Clone();
        init => _wireFrame = (byte[])(value?.Clone() ??
            throw new ArgumentNullException(nameof(value)));
    }
    public int WireLength => _wireFrame.Length;

    // Retain the original positional-record deconstruction API from V9.
    public void Deconstruct(out long sequence, out SocketLinkProvenance source,
        out bool clientToServer, out byte[] wireFrame)
    {
        sequence = Sequence;
        source = Source;
        clientToServer = ClientToServer;
        wireFrame = WireFrame;
    }
}

/// <summary>
/// A bounded queue admission only. It certifies neither an attempted write,
/// nor a completed TCP write, nor any response from the game server.
/// </summary>
internal readonly record struct SocketInjectionAdmissionReceipt(
    SocketLinkProvenance Source, long AdmissionOrdinal, int WireLength, long QueuedAtTick)
{
    // An out parameter set to default after rejection must never look queued.
    public bool Queued => AdmissionOrdinal > 0 && WireLength > 0 &&
        Source.ClientInstance != Guid.Empty && Source.ConnectionToken != Guid.Empty;
    public bool WriteAttempted => false;
    public bool LocalWriteCompleted => false;
    public bool ServerAcknowledged => false;
}

/// <summary>
/// Local, non-durable counters for one SocketGameClient instance. Attempted
/// counts invocation of the outbound write path, while Ambiguous counts an
/// attempt whose WriteAsync/FlushAsync did not both complete. No replay or
/// success may be inferred from a missing post-flush observation.
/// </summary>
internal readonly record struct SocketOutboundJourneyCounters(
    long Queued, long WriteAttempted, long PostFlushWritten,
    long AmbiguousWriteFailure, long DiscardedBeforeWrite);

/// <summary>
/// An engine-injected frame whose local TCP WriteAsync AND FlushAsync completed.
/// This is distinct from Inject() queue admission, from the legacy manual
/// FeedFrame tap, and from any server response/ACK. The byte array is private:
/// callers can obtain only an independent copy.
/// </summary>
internal sealed class SocketTransportWriteObservation
{
    internal SocketTransportWriteObservation(SocketLinkProvenance source, long ordinal,
        byte[] writtenFrame, long writtenAtTick)
        : this(source, 0, 0, ordinal, writtenFrame, writtenAtTick) { }

    internal SocketTransportWriteObservation(SocketLinkProvenance source,
        long admissionOrdinal, long attemptOrdinal, long ordinal,
        byte[] writtenFrame, long writtenAtTick)
    {
        Source = source;
        AdmissionOrdinal = admissionOrdinal;
        AttemptOrdinal = attemptOrdinal;
        Ordinal = ordinal;
        WrittenAtTick = writtenAtTick;
        Wire = ImmutableArray.CreateRange(writtenFrame);
    }
    public SocketLinkProvenance Source { get; }
    /// <summary>Ordinal allocated by the bounded queue; not a game UID.</summary>
    public long AdmissionOrdinal { get; }
    /// <summary>Ordinal allocated immediately before WriteAsync, not an ACK.</summary>
    public long AttemptOrdinal { get; }
    public long Ordinal { get; }
    public long WrittenAtTick { get; }
    public ImmutableArray<byte> Wire { get; }
    public int WireLength => Wire.Length;
    public bool LocalWriteCompleted => true;
    public bool ServerAcknowledged => false; // no causal server receipt is available here
    public byte[] CopyWireFrame() => Wire.ToArray();
}

internal sealed class SocketGameParams
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Token { get; init; }        // jeton renvoyé par SelectServer (serveur de connexion)
    public required string ClientVersion { get; init; }
    public required string DeviceId { get; init; }     // = SHA-256(HWID) hex (device_identifier / HWID de la preuve Schnorr)
    public ProxySettings? Proxy { get; init; }
    /// <summary>Personnage à sélectionner (nom, insensible à la casse) ; null/absent de la liste = le premier.</summary>
    public string? PreferredCharacter { get; init; }

    /// <summary>Optional one-session parking point. Null preserves automatic selection; no mirror or replay is implied.</summary>
    public SocketCharacterSelectionGate? SelectionGate { get; init; }

    /// <summary>Turbo-ack : vrai quand le profil de combat est Turbo -> les accusés de NOS séquences partent tout de suite
    /// (même comportement que le Turbo MITM), sans rejouer la durée d'animation du vrai client.</summary>
    public Func<bool>? TurboAck { get; init; }

    /// <summary>Turbo + « Accuser les actions de tous les combattants » : les séquences des monstres/alliés sont accusées dès leur fin.</summary>
    public Func<bool>? TurboAckAll { get; init; }

    /// <summary>Turbo + « Passage de tour agressif » : « tour prêt » anticipé, réponse immédiate au marqueur, relances de 100 ms.</summary>
    public Func<bool>? TurboEagerReady { get; init; }

    /// <summary>Alimentation du modele de monde : chaque trame WIRE (recue OU envoyee) est passee ici -> meme decodeur
    /// que le MITM (carte/niveau/serveur/pods/interactifs/activite peuples par pid). Cable sur LaunchService.FeedSocketFrame.</summary>
    public Action<uint, bool, byte[]>? FeedFrame { get; init; }
    /// <summary>
    /// Optional source-bound observation for a future trusted consumer. Unlike
    /// FeedFrame, this carries the exact TCP session generation/connection.
    /// No consumer may infer a game ACK or authenticated actor from this alone.
    /// </summary>
    public Action<SocketTransportFrameObservation>? FeedObserved { get; init; }
    /// <summary>
    /// Optional, independently source-checked evidence for ENGINE injections
    /// only. Invoked after the local TCP WriteAsync+FlushAsync completes. Never
    /// forwarded to FeedFrame/FeedObserved: it is not a human/manual request.
    /// </summary>
    public Action<SocketTransportWriteObservation>? OutboundWritten { get; init; }

    /// <summary>PILOTAGE : quand la socket de jeu est prete, on enregistre ici la fonction d'injection du bot (ecriture
    /// d'une trame WIRE sur cette socket) pour son pid -> les primitives de forge (move/cast/harvest/Lua...) la trouvent.</summary>
    public Action<uint, Func<byte[], bool>>? RegisterInjector { get; init; }
    /// <summary>
    /// Prefer this independently scoped registration gate when SocketProbe
    /// can distinguish attempts sharing a synthetic PID. False fails closed.
    /// </summary>
    public Func<uint, Func<byte[], bool>, bool>? RegisterInjectorWithAdmission { get; init; }
    /// <summary>PILOTAGE : a la fin de session, on retire l'injecteur du pid (plus aucune forge routee vers cette socket).</summary>
    public Action<uint>? UnregisterInjector { get; init; }
    /// <summary>Explicit one-movement diagnostic for a SHA-reviewed cross-patch
    /// candidate. Defaults to OFF. A connection/identical .proto alone never
    /// enables general gameplay commands. Caller must check its local receipt.</summary>
    public bool ReviewedCrossPatchPilot { get; init; }
    /// <summary>Reçu SHA valide + Cytrus (lancement) confirme que la version installée est la dernière : codes réseau supposés identiques, pas de restriction « revue ».</summary>
    public bool AliasesVerified { get; init; }
    /// <summary>Création du nouveau personnage à la première liste de personnages (puis sélection de ce personnage). Null = connexion normale.</summary>
    public CharacterCreationSpec? Creation { get; init; }
    /// <summary>Compte de cette session : la page de création affiche le résultat sur la bonne ligne.</summary>
    public string? AccountLogin { get; init; }
    /// <summary>Suppression d'un personnage (nom exact + réponse secrète) à la première liste de personnages ; la session s'arrête ensuite. Null = connexion normale.</summary>
    public CharacterDeletionSpec? Deletion { get; init; }
    /// <summary>Bonus des défis préféré pour ce pid (true = butin) : annoncé à l'entrée en combat, là où le vrai client annonce
    /// le bonus mémorisé de son joueur. null / false = XP (charge utile vide, comme le client par défaut).</summary>
    public Func<uint, bool>? ChallengeLoot { get; init; }
}

/// <summary>
/// Le rôle du CLIENT DOFUS 3 sur la connexion au serveur de jeu, en mode clientless : login, vérification Schnorr, sélection
/// du personnage, entrée en jeu, et TOUT ce que Dofus.exe envoie de lui-même en MITM (chargement de carte, séquence, latence,
/// accusés et « tour prêt » de combat, messages d'initialisation sociale/quêtes). Chaque réaction est calée sur les trames
/// SORTANTES du vrai client capturées en MITM (journaux [pkt-dbg] 2026-09-23/24: ordre, délais, payloads) — pas sur un bot tiers,
/// dont plusieurs valeurs étaient inventées (latence 500-650 ms, ping/date toutes les 30 s, accusés de TOUS les combattants).
/// Codes et numéros de champ résolus PAR NOM via le mapping streamé (repli = numéro du patch courant), jamais figés.
/// </summary>
internal sealed class SocketGameClient
{
    private readonly uint _logPid;
    private static long _nextStreamEpoch;
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly object _sourceGate = new();
    private SocketLinkProvenance? _currentSource;
    // Bound before dial by SocketProbe; a superseded synthetic-PID lease is
    // invalidated immediately, even before its old Stop callback executes.
    private Func<bool>? _sessionLeaseCurrent;
    private CancellationTokenSource? _sessionCancellation;
    private long _observationSequence;
    private long _outboundAdmissionOrdinal;
    private long _outboundAttemptOrdinal;
    private long _outboundWriteOrdinal;
    private long _outboundAmbiguousWriteFailures;
    private long _outboundDiscardedBeforeWrite;
    private const int MaxObservedFrames = 256;
    private readonly object _observationGate = new();
    private readonly Queue<SocketTransportFrameObservation> _observations = new();
    private bool _observationDraining;
    private int _runEntered;
    private int _superseded;
    private readonly SemaphoreSlim _writeLock = new(1, 1);   // NetworkStream : jamais 2 écritures concurrentes
    // File d'injection PILOTAGE : bornée, sans perte silencieuse. Une file illimitée pouvait accepter
    // des milliers d'actions derrière une socket coupée et annoncer à tort « injection réussie ».
    private readonly Channel<Outgoing> _outbound =
        Channel.CreateBounded<Outgoing>(new BoundedChannelOptions(256)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait,
        });
    // Visible depuis les threads Lua/UI : aucune injection avant la sélection de personnage,
    // après un échec de login, ni pendant/après la fermeture. L'envoi TCP reste non garanti
    // tant que la boucle de drainage n'a pas réellement écrit les octets.
    private int _injectionReady;
    // Registration is a public dispatch capability, NOT a synonym for TCP
    // connection. Publish only after server-selected character + game context.
    // A cross-patch review is connection/decoder evidence, not a gameplay ACK.
    private int _injectorPublished;
    private enum ReviewCommand { None, Move, Confirm }
    private Func<byte[], ReviewCommand>? _reviewClassifier;
    private int _reviewDiagnosticMode;
    // 0=idle, 1=movement queued, 2=self echo, 3=confirm queued,
    // 4=server response observed (next move can be tried), -1=refused/time-out.
    private int _reviewMoveState;
    private int _reviewMoveGeneration;
    private int _reviewMoveResponses;
    private int _reviewDeniedFrames;
    public string ReviewMovementDiagnosticStatus => Volatile.Read(ref _reviewMoveState) switch
    {
        1 => "MOVEMENT_PENDING",
        2 => "SELF_ECHO_OBSERVED_NOT_ACK",
        3 => "CONFIRM_PENDING",
        4 => "SERVER_RESPONSE_OBSERVED_REVIEW_ONLY",
        -1 => "REFUSED_OR_TIMED_OUT",
        _ => "NOT_STARTED"
    };
    // Trame du moteur + instant de MISE EN FILE : en combat, l'écart d'origine entre deux actions du moteur est conservé
    // à l'envoi, même quand l'une d'elles a été retenue (jamais deux actions collées dans la même milliseconde).
    private readonly record struct Outgoing(byte[] Frame, long EnqTick,
        SocketLinkProvenance Source, long AdmissionOrdinal, int ReviewMoveGeneration = 0);
    // File ORDONNÉE des réactions de COMBAT du client (accusés de séquence, tour prêt), chacune à son heure : un accusé
    // part toujours avant le « tour prêt » qui le suit, comme chez le vrai client qui finit ses animations dans l'ordre.
    private readonly Channel<Reaction> _reactions =
        Channel.CreateUnbounded<Reaction>(new UnboundedChannelOptions { SingleReader = true });

    // Échéance MODIFIABLE : l'accusé du coup qui TERMINE le combat n'est reconnu qu'après sa planification (cf ScheduleTurnReady).
    private sealed class Reaction
    {
        public required string Name { get; init; }
        public required List<ProtoField> Payload { get; init; }
        public long DueTick;
        public required int Epoch { get; init; }
        public required bool IsAck { get; init; }
        public required string Label { get; set; }
        public volatile bool Sent;
    }

    // ---- état de session (une instance = une connexion au serveur de jeu) ----
    private long _controlledActor;        // server-granted FightControlEvent context, never guessed from a turn
    private long _selfId;                 // personnage sélectionné (auteur des séquences qu'on accuse)
    private volatile bool _inFight;       // entre FightStartingEvent/FightJoinEvent et FightEndEvent
    private int _seqOpen;                 // séquences serveur OUVERTES (imbrication SequenceStart/SequenceEnd) — thread lecture
    private bool _turnReadyPending;       // FightIsTurnReadyEvent reçu pendant une séquence ouverte -> répondre à sa fermeture
    private long _turnReadyFor;           // character_id de ce tour prêt différé
    private bool _noTurnYet;              // combat démarré, aucun FightTurnEvent encore : « tour prêt » quasi immédiat
    private bool _challengePending;       // entrée en combat : envoyer le choix de bonus/mode au prochain ChallengeNumberEvent
    private int _fightEpoch;              // change à chaque début/fin de combat : les réactions d'un combat fini sont jetées
    private int _pendingAcks;             // accusés planifiés pas encore envoyés : retiennent les actions du moteur
    private int _selfSeqEnds;             // séquences EXTERNES de NOTRE perso closes (l'action précédente est revenue du serveur)
    private long _ackChainDue;            // échéance du dernier accusé planifié : les animations du client s'enchaînent
    private int _seqMoveSteps;            // pas de NOTRE déplacement dans la séquence externe en cours (animation ~139 ms/pas)
    private Reaction? _lastSelfAck;       // dernier accusé planifié d'une de NOS séquences (recalable : coup de fin de combat)
    private long _lastSelfSeqEnd;         // arrivée de la fin de cette séquence
    private long _lastSelfSeqType = -1;   // son type (0 sort, 1 arme…)
    private bool _turnEndSeen;            // une de NOS séquences « fin de tour » (type 7) close depuis le dernier FightTurnEvent
    private bool _afterFightMap;          // 1re carte de jeu de rôle après un combat : chargement plus long (~0,43-0,57 s)
    private bool _firstFightMapDone;      // 1re carte de combat de la session : chargement plus long (~0,46-0,7 s)
    private bool _firstFightDone;         // 1er combat de la session : le client envoie DEUX fois le choix de défi
    private int _selfLevel;               // niveau du perso sélectionné (portes d'initialisation du client)
    private long _lastSelfAckBase;        // début de l'animation du dernier accusé (max(fin de séquence, accusé précédent))
    private long _outerAuthor;            // auteur de la séquence EXTERNE en cours (animation d'un AUTRE combattant)
    private long _turnOwner;              // combattant dont c'est le tour (FightTurnEvent.character_id)
    private bool _earlyReadySent;         // Turbo agressif : « tour prêt » anticipé déjà envoyé dans ce combat
    private int _readyGen;                // Turbo agressif : change à chaque nouveau tour/combat -> les relances « tour prêt » en cours s'arrêtent
    private readonly AdaptiveReadyPolicy _adaptiveReady = new();
    private bool _otherSeqSeen;           // une séquence d'un AUTRE combattant a été vue depuis son FightTurnEvent
    private readonly List<string> _otherShape = new();   // forme du tour d'un autre combattant (diagnostic du « tour prêt »)
    private int _charListSent;            // CharacterListRequest envoyé (après ServerSessionReady, repli après l'acceptation)
    private bool TakeCharList() => Interlocked.Exchange(ref _charListSent, 1) == 0;
    // Champ « corps » (BodyData.id) de CharacterRemodelingInformation : son nom est OBFUSQUÉ et propre à ce build. Volontairement hors du
    // manifeste des champs requis (RequiredMapping) : un renommage au prochain patch ne doit bloquer que la création, pas toute l'app.
    private const string CreationBodyField = CharacterCreationTab.BodyField;
    private int _deleteState;              // 0 aucune, 1 « préparation » envoyée, 2 demande de suppression envoyée
    private long _deleteId;
    private volatile bool _deleteDone;      // la session a déjà conclu (succès ou refus) : le contrôle de 15 s ne fait rien
    private int _createState;              // 0 aucune/non démarrée, 1 démarrée (nom demandé ou envoi), 2 demande envoyée, 3 créé
    private string? _createdName;
    private bool _firstSelection;         // le personnage vient d être créé : CharacterFirstSelectionRequest
    private long _pendingCharId;          // perso choisi dans la liste, sélection différée (comme le client : après la liste d'actions)
    private bool _forced;                 // sélection IMPOSÉE par le serveur (reconnexion en plein combat)
    private volatile bool _selSuccess;    // CharacterSelectionEvent { success } reçu
    private int _selStarted;              // sélection lancée (une seule, quel que soit le déclencheur : liste d'actions / repli / imposée)
    private bool StartSelection() => Interlocked.CompareExchange(ref _selStarted, 1, 0) == 0;
    private bool _bidActivitiesSent;      // BidActivitiesRequest (ventes HDV hors ligne) déjà envoyé cette session
    private long _thinkUntil;             // une fenêtre vient de s'ouvrir (zaap, HDV, atelier) : 1re action du moteur pas avant
    private long _thinkSetAt;             // instant d'ouverture de cette fenêtre

    /// <summary>Le serveur a imposé la déconnexion (SystemMessageDisplayEvent hang_up : modération, maintenance…) : ne pas se
    /// reconnecter automatiquement. Texte = identifiant du message + paramètres.</summary>
    public string? ServerHangUp { get; private set; }
    private long _loginSentAt;            // instant d'envoi de l'IdentificationRequest de jeu
    private volatile bool _closedBeforeAccept;   // serveur a fermé APRÈS notre login et AVANT de l'accepter (session précédente éjectée)
    private bool _ctxFight;               // dernier ContextCreationEvent = contexte COMBAT
    private bool _loginBurstDone;         // messages d'initialisation du 1er contexte envoyés
    private bool _firstMapDone;           // 1re carte après le login (demande d'anomalie de sous-zone en plus)
    private bool _afterFight;             // FightEndEvent vu : le prochain contexte jeu de rôle redemande les étapes de quête
    private readonly List<int> _questIds = new();
    private int _seq;                     // compteur de SequenceNumberRequest (1, 2, …)
    private long _pingSentAt;             // instant d'envoi du ping unique (0 = pas encore)
    private volatile int _rttMs;          // aller-retour Ping -> Pong mesuré (0 = inconnu)
    private long _lastRx = Environment.TickCount64;
    private volatile string? _endReason;  // cause d'une fin de session décidée par nous (inactivité…)

    // Au-delà, la connexion est tenue pour MORTE : même inactif, le serveur sonde toutes les 4 min (SequenceNumberEvent +
    // BasicLatencyStatsEvent, constaté en MITM). Le vrai client ne pingue pas : c'est la seule détection applicative.
    private const long RxIdleLimitMs = 330_000;
    // Garde-fou : une action du moteur n'attend jamais plus longtemps un accusé en cours (évite tout blocage).
    private const int AckGateMaxMs = 2500;

    public SocketGameClient(uint logPid) => _logPid = logPid;

    /// <summary>
    /// Bind a lightweight, nonblocking lease check once before RunAsync.
    /// It must not acquire SocketProbe's registry lock (prevents lock inversion
    /// with _sourceGate); the real Probe passes Lease.IsActive (volatile).
    /// </summary>
    public void BindSessionOwnership(Func<bool> isCurrent)
    {
        ArgumentNullException.ThrowIfNull(isCurrent);
        lock (_sourceGate)
        {
            if (Volatile.Read(ref _runEntered) != 0 || _sessionLeaseCurrent is not null)
                throw new InvalidOperationException("Socket session lease already bound or started.");
            _sessionLeaseCurrent = isCurrent;
        }
    }

    private bool IsLeaseCurrentUnsafe()
    {
        try { return _sessionLeaseCurrent?.Invoke() != false; }
        catch { return false; } // an unavailable ownership authority cannot grant admission
    }

    private bool IsValidSource(SocketLinkProvenance source) =>
        source.ClientInstance != Guid.Empty &&
        source.ConnectionToken != Guid.Empty &&
        source.RunEpoch > 0 && source.SyntheticPid != 0 &&
        source.SyntheticPid == _logPid;

    /// <summary>
    /// Bounded in-memory diagnostics. Queued includes frames not yet drained;
    /// Attempted may exceed PostFlushWritten after partial I/O or cancellation.
    /// These counts are NOT a source of server acknowledgement or retry rights.
    /// </summary>
    public SocketOutboundJourneyCounters ReadOutboundJourneyCounters() => new(
        Interlocked.Read(ref _outboundAdmissionOrdinal),
        Interlocked.Read(ref _outboundAttemptOrdinal),
        Interlocked.Read(ref _outboundWriteOrdinal),
        Interlocked.Read(ref _outboundAmbiguousWriteFailures),
        Interlocked.Read(ref _outboundDiscardedBeforeWrite));

    /// <summary>
    /// Read-only exact connection check; an earlier successful check cannot
    /// keep a lease alive across EOF, Stop, retry, or PID reuse.
    /// </summary>
    public bool IsCurrentSocketLink(SocketLinkProvenance source)
    {
        lock (_sourceGate)
            return Volatile.Read(ref _superseded) == 0 &&
                IsLeaseCurrentUnsafe() &&
                IsValidSource(source) &&
                _currentSource is { } current && current == source &&
                _sessionCancellation is { IsCancellationRequested: false };
    }

    public bool TryGetCurrentSocketLink(out SocketLinkProvenance source)
    {
        lock (_sourceGate)
        {
            if (Volatile.Read(ref _superseded) == 0 &&
                IsLeaseCurrentUnsafe() &&
                _currentSource is { } current && IsValidSource(current) &&
                _sessionCancellation is { IsCancellationRequested: false })
            {
                source = current;
                return true;
            }
        }
        source = default;
        return false;
    }

    /// <summary>
    /// A newer session for this synthetic PID has taken ownership. Revoke a
    /// previously retained Inject delegate before cancelling the old stream.
    /// Only SocketProbe's owned session coordinator invokes this method.
    /// </summary>
    public void RevokeForReplacement()
    {
        Volatile.Write(ref _superseded, 1);
        Volatile.Write(ref _injectionReady, 0);
        Volatile.Write(ref _injectorPublished, 0);
        CancellationTokenSource? cancel;
        lock (_sourceGate)
        {
            _currentSource = null;
            cancel = _sessionCancellation;
        }
        lock (_observationGate) _observations.Clear();
        try { cancel?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void PublishObserved(SocketGameParams parameters, bool clientToServer, byte[] wire)
    {
        var subscribers = parameters.FeedObserved;
        if (subscribers is null) return;
        if (!TryGetCurrentSocketLink(out var source)) return;
        bool drain = false, overflow = false;
        // Both read and write pumps may publish concurrently. Serialize the
        // actual consumer callbacks so local sequence labels are delivered in
        // order; reentrant publications enqueue behind the current callback.
        lock (_observationGate)
        {
            if (_observations.Count >= MaxObservedFrames) overflow = true;
            else
            {
                _observations.Enqueue(new SocketTransportFrameObservation(
                    ++_observationSequence, source, clientToServer, wire));
                if (!_observationDraining)
                    drain = _observationDraining = true;
            }
        }
        if (overflow)
        {
            // Never silently drop/reorder a potentially authoritative frame.
            // The optional observer may not starve the owning socket.
            RevokeForReplacement();
            return;
        }
        if (!drain) return;
        while (true)
        {
            SocketTransportFrameObservation observation;
            lock (_observationGate)
            {
                if (_observations.Count == 0)
                {
                    _observationDraining = false;
                    return;
                }
                observation = _observations.Dequeue();
            }
            foreach (Action<SocketTransportFrameObservation> subscriber
                     in subscribers.GetInvocationList())
            {
                if (!IsCurrentSocketLink(observation.Source))
                {
                    lock (_observationGate)
                    {
                        _observations.Clear();
                        _observationDraining = false;
                    }
                    return;
                }
                try { subscriber(observation); }
                catch { /* An observer cannot interrupt the game transport. */ }
            }
        }
    }

    /// <summary>
    /// Actual engine-output drain boundary. A queued frame cannot get an
    /// ordinal or a written witness. The outbound write lock is released
    /// BEFORE invoking arbitrary callbacks, so an observer may Stop/revoke
    /// without trapping the transport. No server ACK is implied.
    /// </summary>
    private async Task<bool> WriteQueuedEngineActionAsync(Stream target, Outgoing item,
        SocketGameParams parameters, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !IsCurrentSocketLink(item.Source))
        {
            Interlocked.Increment(ref _outboundDiscardedBeforeWrite);
            return false;
        }
        long writeOrdinal;
        long attemptOrdinal;
        long completedAt;
        try { await _writeLock.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _outboundDiscardedBeforeWrite);
            throw;
        }
        try
        {
            // Another session or Stop can win while the write gate is held.
            if (ct.IsCancellationRequested || !IsCurrentSocketLink(item.Source))
            {
                Interlocked.Increment(ref _outboundDiscardedBeforeWrite);
                return false;
            }
            // This counts an I/O attempt, not its result. Exceptions or
            // cancellation after a partial WriteAsync leave outcome unknown.
            attemptOrdinal = Interlocked.Increment(ref _outboundAttemptOrdinal);
            try
            {
                await target.WriteAsync(item.Frame, ct).ConfigureAwait(false);
                await target.FlushAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                Interlocked.Increment(ref _outboundAmbiguousWriteFailures);
                throw; // never retry, and never manufacture post-flush proof
            }
            // Some Stream implementations can complete FlushAsync despite a
            // cancellation/revocation that happened while I/O was pending.
            // Bytes MAY have escaped, but the retired link cannot acquire a
            // successful local-write witness. Preserve uncertainty, no replay.
            if (ct.IsCancellationRequested || !IsCurrentSocketLink(item.Source))
            {
                Interlocked.Increment(ref _outboundAmbiguousWriteFailures);
                return false;
            }
            // Assign strictly after both I/O operations AND the current-link
            // check, never at Inject() or merely after a stale post-flush.
            writeOrdinal = Interlocked.Increment(ref _outboundWriteOrdinal);
            completedAt = Environment.TickCount64;
        }
        finally { _writeLock.Release(); }

        var handlers = parameters.OutboundWritten;
        if (handlers is not null && !ct.IsCancellationRequested &&
            IsCurrentSocketLink(item.Source))
        {
            var witness = new SocketTransportWriteObservation(item.Source,
                item.AdmissionOrdinal, attemptOrdinal, writeOrdinal, item.Frame, completedAt);
            foreach (Action<SocketTransportWriteObservation> handler in handlers.GetInvocationList())
            {
                if (ct.IsCancellationRequested || !IsCurrentSocketLink(item.Source)) break;
                try { handler(witness); }
                catch { /* A diagnostic observer cannot reclassify or break TCP. */ }
            }
        }
        return true; // local write+flush only; this is NOT a server response
    }

    /// <summary>Le serveur de jeu a ACCEPTÉ notre login (AuthenticationTicketAcceptedEvent). Faux en fin de session =
    /// fermé juste après l'IdentificationRequest : la session précédente du compte était encore tenue par le serveur, qu'il
    /// vient d'éjecter -> SocketProbe réessaie une fois.</summary>
    public bool LoginAccepted { get; private set; }

    /// <summary>Le serveur de jeu a FERMÉ la connexion après notre IdentificationRequest et AVANT de l'accepter : c'est le
    /// signe d'une session précédente encore tenue qu'il vient d'éjecter (seul cas où SocketProbe retente tout de suite).
    /// Faux pour un échec de DNS / d'ouverture TCP / de proxy (échecs ordinaires, gérés par la boucle de reconnexion).</summary>
    public bool ClosedBeforeAccept => _closedBeforeAccept;

    /// <summary>PILOTAGE : injecte une trame WIRE (enveloppe forgée, length-prefixed) vers le serveur de jeu de CE bot. Non
    /// bloquant (file interne) -> appelable depuis l'UI/Lua/combat. true = mise en file bornée,
    /// PAS accusé serveur. Une socket encore en authentification ou déjà fermée refuse l'action.</summary>
    public bool Inject(byte[] framedToServer) => TryInject(framedToServer, out _);

    /// <summary>
    /// Explicitly reports a successful queue admission without conflating it
    /// with the later write attempt or post-flush observation. No disk state,
    /// action ID, server ACK or retry promise is created.
    /// </summary>
    public bool TryInject(byte[] framedToServer, out SocketInjectionAdmissionReceipt receipt)
    {
        receipt = default;
        SocketLinkProvenance source;
        lock (_sourceGate)
        {
            if (_currentSource is not { } current) return false;
            source = current;
        }
        if (!IsCurrentSocketLink(source)) return false;
        if (Volatile.Read(ref _injectionReady) != 1 ||
            Volatile.Read(ref _injectorPublished) != 1 ||
            !(framedToServer is { Length: > 1 and <= 2_097_152 }))
            return false;
        // An engine can reuse/change its buffer after Inject() returns.
        // The old queue kept the CALLER's array until the delayed TCP drain:
        // the bytes actually transmitted could differ from the accepted command.
        // Own a stable snapshot before queuing; true still means queued, NOT ACK.
        var ownedFrame = (byte[])framedToServer.Clone();
        ReviewCommand kind = ReviewCommand.None;
        int moveGeneration = 0;
        int movePrevious = 0;
        if (Volatile.Read(ref _reviewDiagnosticMode) == 1)
        {
            // Never route combat/HDV/Lua/other arbitrary forged frames in review.
            // A single verified-form movement may be tried. Even an observed
            // response does not promote the rest of the wire protocol.
            kind = _reviewClassifier?.Invoke(ownedFrame) ?? ReviewCommand.None;
            if (kind == ReviewCommand.Move)
            {
                if (_inFight) return false; // combat movement needs a separate, proven ACK protocol
                if (Interlocked.CompareExchange(ref _reviewMoveState, 1, 0) != 0)
                {
                    if (Interlocked.CompareExchange(ref _reviewMoveState, 1, 4) != 4)
                        return false; // one in flight; rearm only after observed response
                    movePrevious = 4;
                }
                moveGeneration = Interlocked.Increment(ref _reviewMoveGeneration);
            }
            else if (kind == ReviewCommand.Confirm)
            {
                if (Interlocked.CompareExchange(ref _reviewMoveState, 3, 2) != 2)
                    return false;
            }
            else
            {
                // Bounded, redacted diagnostic (never dump the possibly sensitive
                // frame or request payload into logs).
                int denied = Interlocked.Increment(ref _reviewDeniedFrames);
                if (denied == 1 || denied == 10 || denied == 100)
                    SocketLog.Info(_logPid, "[jeu] revue inter-patch : action hors liste de diagnostic refusée " +
                        "(sort/combat/HDV/script non certifié), occurrences=" + denied);
                return false;
            }
        }
        bool queued = false;
        // This is the actual admission linearization point. The old
        // IsCurrentSocketLink -> TryWrite gap could return true AFTER Stop or
        // RevokeForReplacement had retired the source on another thread.
        lock (_sourceGate)
        {
            if (Volatile.Read(ref _superseded) == 0 &&
                IsLeaseCurrentUnsafe() &&
                IsValidSource(source) &&
                _currentSource is { } current && current == source &&
                _sessionCancellation is { IsCancellationRequested: false } &&
                Volatile.Read(ref _injectionReady) == 1 &&
                Volatile.Read(ref _injectorPublished) == 1)
            {
                long ordinal = _outboundAdmissionOrdinal + 1;
                if (ordinal > 0)
                {
                    long now = Environment.TickCount64;
                    queued = _outbound.Writer.TryWrite(new Outgoing(
                        ownedFrame, now, source, ordinal, moveGeneration));
                    if (queued)
                    {
                        _outboundAdmissionOrdinal = ordinal;
                        receipt = new SocketInjectionAdmissionReceipt(
                            source, ordinal, ownedFrame.Length, now);
                    }
                }
            }
        }
        if (!queued && kind == ReviewCommand.Move)
            Interlocked.CompareExchange(ref _reviewMoveState, movePrevious, 1);
        if (!queued && kind == ReviewCommand.Confirm)
            Interlocked.CompareExchange(ref _reviewMoveState, 2, 3);
        return queued;
    }

    public async Task RunAsync(SocketGameParams p, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _runEntered, 1) != 0)
            throw new InvalidOperationException("SocketGameClient permits only one TCP session.");
        // Session de JEU PERSISTANTE : vit jusqu'au Stop (row.SocketCts annule ct) ou fermeture serveur. Aucun timeout global.
        // Jeton PROPRE à la session (lié au Stop) et annulé en fin de session QUELLE QU'EN SOIT LA CAUSE : sinon, après une
        // coupure serveur (ct intact), les tâches différées de l'ancienne session survivaient.
        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tk = session.Token;
        bool wasSuperseded;
        lock (_sourceGate)
        {
            wasSuperseded = Volatile.Read(ref _superseded) != 0 ||
                !IsLeaseCurrentUnsafe();
            _sessionCancellation = session;
        }
        if (wasSuperseded) session.Cancel();
        var cat = MappingCatalog.GetEffective();
        // PURGE du mapping (licence tuée / bannie / déconnectée) : la session de jeu se ferme IMMÉDIATEMENT. Sinon nos
        // réponses automatiques (séquence, latence, accusés de combat) garderaient le perso en ligne sur un catalogue que le
        // kill-switch veut justement retirer de la RAM (cf LaunchService.OnMappingCleared).
        Action onPurge = () =>
        {
            _endReason = "mapping purgé (licence non autorisée) → fermeture de la session";
            try { session.Cancel(); } catch { }
        };
        MappingCatalog.Cleared += onPurge;
        Stream? stream = null;
        try
        {
            // Sans catalogue, aucun message ne peut être forgé correctement (codes/champs renumérotés à chaque patch) :
            // jamais de code figé en repli -> on n'ouvre même pas la connexion.
            if (cat is null) { SocketLog.Info(_logPid, "[jeu] mapping ABSENT (pas encore reçu du serveur ?) — connexion au jeu annulée"); return; }
            SocketLog.Info(_logPid, $"[jeu] résolution DNS de {p.Host}…");
            var addrs = await Dns.GetHostAddressesAsync(p.Host, tk).ConfigureAwait(false);
            var ip = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ip is null) { SocketLog.Info(_logPid, "[jeu] aucune IPv4 — abandon"); return; }
            var ipv4 = ip.GetAddressBytes();
            var via = p.Proxy is null ? "DIRECT" : $"{p.Proxy.Kind} {p.Proxy.Host}:{p.Proxy.Port}";
            SocketLog.Info(_logPid, $"[jeu] cible {ip}:{p.Port} via {via}");

            // Enveloppe (slots GameMessage/Request) branchée sur CE catalogue AVANT la 1re trame forgée : sinon la 1re
            // IdentificationRequest d'une session partait avec les numéros de repli (décodeur pas encore initialisé).
            EnvelopeBuilder.UseCatalog(cat.FieldNumber);
            stream = await SocketDialer.DialAsync(p.Proxy, ipv4, p.Port, _logPid, "[jeu]", tk).ConfigureAwait(false);
            // Issue this proof only AFTER the actual TCP dial succeeds. A PID,
            // DNS result, retry intent or prior connection cannot mint it.
            lock (_sourceGate)
            {
                if (Volatile.Read(ref _superseded) == 0 &&
                    IsLeaseCurrentUnsafe() && !tk.IsCancellationRequested)
                {
                    long epoch = Interlocked.Increment(ref _nextStreamEpoch);
                    if (epoch > 0)
                        _currentSource = new SocketLinkProvenance(
                            _instanceId, epoch, Guid.NewGuid(), _logPid);
                }
            }
            if (!TryGetCurrentSocketLink(out _))
                return;
            SocketLog.Info(_logPid, "[jeu] connexion TCP établie");
            SocketStats.SessionStarted(_logPid);

            var schnorr = new SocketSchnorr();

            // Numéro de champ PAR NOM via le catalogue (les patchs renumérotent : ne JAMAIS figer), repli = patch courant.
            int FN(string msg, string field) => cat.FieldNumber(msg, field) ?? FieldMiss.Of(msg, field);   // mapping streamé seul
            static ProtoField VF(int field, long v) => new ProtoField { Field = field, Wire = WireType.Varint, Varint = unchecked((ulong)v) };
            static long PV(List<ProtoField>? pl, int field) =>
                pl?.FirstOrDefault(f => f.Field == field && f.Wire == WireType.Varint) is { } f ? unchecked((long)f.Varint) : 0;
            List<ProtoField> VN(string msg, string field, long v) => FN(msg, field) is int vn and > 0 ? new() { VF(vn, v) } : new();   // champ inconnu : omis
            List<ProtoField> SN(string msg, string field, string v) => FN(msg, field) is not (int sn and > 0) ? new() :
                new() { new ProtoField { Field = sn, Wire = WireType.Len, String = v } };
            static List<ProtoField> None() => new();

            // Registering a callback exposes bot actions through DispatchTransport.
            // A valid TCP connection or even a CharacterSelectionEvent alone does
            // NOT prove an active game context or safe cross-patch wire commands.
            // An exact mapping build is required for unrestricted gameplay;
            // cross-patch mappings can only enable the explicit reviewed move
            // subset below. Never escalate a family match to gameplay access.
            bool exactGameplayMapping = MZP12SocketVersionBinding.IsUnrestrictedSameBuildCandidate(
                MappingCatalog.SessionVersionBinding, p.ClientVersion, p.ReviewedCrossPatchPilot);
            exactGameplayMapping |= ProtocolUpdates.SocketPilotPolicy.GrantsReviewedGameplay(p.ReviewedCrossPatchPilot, p.AliasesVerified);
            var reviewed = MappingCatalog.SessionVersionBinding;
            bool reviewedMoveOnly = !exactGameplayMapping &&
                MZP10MappingVersionAdmission.IsSameProtocolFamily(reviewed?.MappingBuild, p.ClientVersion) &&
                p.ReviewedCrossPatchPilot &&
                string.Equals(reviewed?.OperatorReviewedTarget, p.ClientVersion, StringComparison.Ordinal) &&
                reviewed?.IdenticalProtoSha256 is { Length: 64 } &&
                cat.CodeForName("MapMovementRequest") is not null &&
                cat.CodeForName("MapMovementConfirmRequest") is not null &&
                cat.FieldNumber("GameMessage", "request") is > 0 &&
                cat.FieldNumber("MapMovementRequest", "key_cells") is > 0 &&
                cat.FieldNumber("MapMovementRequest", "map_id") is > 0;
            if (!exactGameplayMapping && !reviewedMoveOnly)
                SocketLog.Info(_logPid, "[jeu] injection désactivée : " +
                    "mapping inter-patch sans diagnostic opérateur conforme; lecture seule");
            if (reviewedMoveOnly)
            {
                var moveCode = cat.CodeForName("MapMovementRequest");
                var confirmCode = cat.CodeForName("MapMovementConfirmRequest");
                int requestSlot = cat.FieldNumber("GameMessage", "request")!.Value;
                int pathField = cat.FieldNumber("MapMovementRequest", "key_cells")!.Value;
                int mapField = cat.FieldNumber("MapMovementRequest", "map_id")!.Value;
                _reviewClassifier = framed =>
                {
                    // Full, single, bounded request frame; reject extra trailing
                    // bytes and server Event/Response envelope masquerading as a
                    // client request. Never use a guessed message code.
                    if (framed.Length is < 3 or > 4096) return ReviewCommand.None;
                    int prefixLen = 0, shift = 0;
                    ulong declared = 0;
                    while (prefixLen < framed.Length && prefixLen < 5)
                    {
                        byte b = framed[prefixLen++];
                        declared |= (ulong)(b & 0x7F) << shift;
                        if ((b & 0x80) == 0) break;
                        shift += 7;
                    }
                    if (prefixLen > 5 || (framed[prefixLen - 1] & 0x80) != 0 ||
                        declared != (ulong)(framed.Length - prefixLen))
                        return ReviewCommand.None;
                    try
                    {
                        var tree = RawProtobuf.Parse(framed.AsSpan(prefixLen).ToArray());
                        if (tree.Count != 1 || tree[0].Field != requestSlot ||
                            tree[0].Message is not { Count: > 0 })
                            return ReviewCommand.None;
                        var hits = RawProtobuf.FindTypeUrls(tree);
                        if (hits.Count != 1) return ReviewCommand.None;
                        var hit = hits[0];
                        if (hit.Code == confirmCode &&
                            hit.Payload is null or { Count: 0 })
                            return ReviewCommand.Confirm;
                        if (hit.Code != moveCode || hit.Payload is not { Count: > 0 } payload)
                            return ReviewCommand.None;
                        bool knownMap = payload.Any(f => f.Field == mapField &&
                            f.Wire == WireType.Varint && f.Varint > 0);
                        bool pathPacked = payload.Any(f => f.Field == pathField &&
                            f.Wire == WireType.Len && f.Bytes is { Length: > 0 and <= 256 });
                        return knownMap && pathPacked ? ReviewCommand.Move : ReviewCommand.None;
                    }
                    catch { return ReviewCommand.None; }
                };
                Volatile.Write(ref _reviewDiagnosticMode, 1);
                SocketLog.Info(_logPid, "[jeu] revue inter-patch : mouvements UN par UN après écho SELF + confirmation; " +
                    "AttackMonsterRequest interdit tant que placement/sort/fin de tour ne sont pas jouables et ACK corrélés");
            }
            bool gameContextSeen = false;

            // Envoi d'un message jeu dans le RÔLE DU CLIENT (code résolu par le mapping streamé). uid = -1 : convention du
            // vrai client pour ~tous ses messages (confirmé par la taille de ses trames). Ces trames sont montrées au décodeur
            // (parité MITM : le tap voit les trames de Dofus.exe), contrairement aux injections du moteur.
            async Task<bool> SendGame(string name, List<ProtoField>? payload = null, int uid = -1)
            {
                if (!_inFight && (name.StartsWith("FightTurn", StringComparison.Ordinal) || name == "FightTurnReadyRequest"))
                {
                    SocketLog.Info(_logPid, $"[jeu] ⚔ {name} ignoré : combat déjà terminé");
                    return false;
                }
                var c = cat.CodeForName(name);
                if (c is null) { SocketLog.Info(_logPid, $"[jeu] code introuvable pour {name} — envoi impossible"); return false; }
                var body = EnvelopeBuilder.BuildClientRequest(c, payload ?? None(), uid);
                var fr = Frame(body);
                SocketLog.Tx(_logPid, $"[jeu] → {ProtoLog.Named(name, c)} {fr.Length}o" + (uid != -1 ? $" uid={uid}" : ""));
                await _writeLock.WaitAsync(tk).ConfigureAwait(false);
                try { await stream!.WriteAsync(fr, tk).ConfigureAwait(false); await stream.FlushAsync(tk).ConfigureAwait(false); }
                finally { _writeLock.Release(); }
                SocketStats.Tx(_logPid, fr.Length);
                PublishObserved(p, true, fr);
                if (tk.IsCancellationRequested) return false;
                p.FeedFrame?.Invoke(_logPid, true, fr);   // trame SORTANTE du « client » -> modele de monde (comme le tap MITM)
                return true;
            }

            // Réaction différée hors combat (carte, initialisation) : exécutée après `ms` sauf si la session se termine.
            void Later(int ms, string what, Func<Task> act) => _ = Task.Run(async () =>
            {
                try { await Task.Delay(ms, tk).ConfigureAwait(false); await act().ConfigureAwait(false); }
                catch (OperationCanceledException) { /* fin de session */ }
                catch (Exception e) { SocketLog.Info(_logPid, $"[jeu] {what} NON envoyé : {e.GetType().Name}"); }
            });

            // Une fenêtre vient de s'ouvrir : la 1re action du moteur mise en file ensuite attend `ms` (file d'injection).
            void SetThink(int ms, string what)
            {
                if (p.TurboAckAll?.Invoke() == true) return;
                long now = Environment.TickCount64;
                Volatile.Write(ref _thinkSetAt, now);
                Volatile.Write(ref _thinkUntil, now + ms);
                SocketLog.Info(_logPid, $"[jeu] {what} ouverte → 1re action du moteur pas avant +{ms} ms (temps de lecture humain)");
            }

            // Réaction de COMBAT planifiée dans la file ordonnée (accusé ou tour prêt).
            Reaction? React(string name, List<ProtoField> payload, int delayMs, bool isAck, string label)
            {
                var r = new Reaction
                {
                    Name = name, Payload = payload, DueTick = Environment.TickCount64 + delayMs,
                    Epoch = Volatile.Read(ref _fightEpoch), IsAck = isAck, Label = label,
                };
                if (isAck) Interlocked.Increment(ref _pendingAcks);
                if (_reactions.Writer.TryWrite(r)) return r;
                if (isAck) Interlocked.Decrement(ref _pendingAcks);
                return null;
            }

            // Accusé d'une séquence qui NOUS appartient. Le vrai client joue ses animations À LA SUITE : l'accusé part à
            // max(maintenant, échéance de l'accusé précédent) + durée de CETTE animation (règle vérifiée sur 180/180 accusés).
            void ScheduleOwnAck(long actionId, long seqType)
            {
                long now = Environment.TickCount64;
                bool turboAck = p.TurboAck?.Invoke() == true;
                // Sécurité anti-burst : en Turbo, insérer une micro-gigue humaine (15-35 ms, adaptative sous backoff) au lieu de 0 ms absolu
                // En Turbo l'accuse prend un creneau de la cadence PARTAGEE (nos accuses + ceux des autres) : deux
                // compteurs separes laissaient passer 57-60 accuses/s malgre la gigue, d'ou les fermetures serveur.
                if (turboAck)
                {
                    long turboDue = now + _adaptiveReady.NextOwnAckDelayMs(now);
                    _lastSelfAckBase = Math.Max(now, _ackChainDue);
                    _ackChainDue = Math.Max(turboDue, _lastSelfAckBase);
                    string turboWhat = seqType == 4 && _seqMoveSteps > 0 ? $"deplacement {_seqMoveSteps} pas" : $"type {seqType}";
                    _lastSelfAck = React("GameActionAcknowledgementRequest", new List<ProtoField>
                    {
                        VF(FN("GameActionAcknowledgementRequest", "valid"), 1),
                        VF(FN("GameActionAcknowledgementRequest", "action_id"), actionId),
                    }, (int)(_ackChainDue - now), isAck: true,
                       $"accusé de NOTRE séquence (action {actionId}, {turboWhat}, +{_ackChainDue - now} ms, cadence partagée)");
                    _lastSelfSeqEnd = now; _lastSelfSeqType = seqType;
                    return;
                }
                int anim = AnimMs(seqType, _seqMoveSteps);
                _lastSelfAckBase = Math.Max(now, _ackChainDue);
                long due = _lastSelfAckBase + anim;
                _ackChainDue = due;
                string what = seqType == 4 && _seqMoveSteps > 0 ? $"déplacement {_seqMoveSteps} pas" : $"type {seqType}";
                _lastSelfAck = React("GameActionAcknowledgementRequest", new List<ProtoField>
                {
                    VF(FN("GameActionAcknowledgementRequest", "valid"), 1),
                    VF(FN("GameActionAcknowledgementRequest", "action_id"), actionId),
                }, (int)(due - now), isAck: true, $"accusé de NOTRE séquence (action {actionId}, {what}, +{due - now} ms)");
                _lastSelfSeqEnd = now; _lastSelfSeqType = seqType;
            }

            // « Tour prêt » (FightTurnReadyRequest{is_ready}) : exactement UNE réponse par demande (58/58 et 36/36 en MITM).
            // Après NOTRE tour : 0-3 ms après notre dernier accusé (animations finies). Après le tour d'un AUTRE combattant :
            // bimodal ~1,62-1,86 s (1/3) / ~2,37-2,44 s (2/3). Avant le 1er tour (lancement du combat) : quelques ms.
            void ScheduleTurnReady(long who, string why)
            {
                long now = Environment.TickCount64;
                bool eager = p.TurboEagerReady?.Invoke() == true;

                // SI LE COMBAT EST TERMINÉ (FightEndEvent / _fightActive == false / etc.) :
                // NE JAMAIS envoyer de FightTurnReadyRequest ! Le serveur a déjà clos le contexte de combat.
                if (!_inFight)
                {
                    SocketLog.Info(_logPid, "[jeu] ⚔ FightTurnReadyRequest ignoré : le combat est déjà terminé");
                    return;
                }

                // COUP QUI TERMINE LE COMBAT : « tour prêt » demandé pour NOUS juste après notre sort, SANS séquence de fin de
                // tour (6/6 en MITM, 0 sur 23 tours normaux). Le vrai client y joue l'animation de fin (~1,45-1,6 s) avant
                // d'accuser ce sort — contrairement à un kill intermédiaire (accusé normal, ~250 ms) : l'accusé déjà planifié
                // est RECALÉ, puis le « tour prêt » le suit de 0-3 ms.
                if (!eager && (who == _selfId || (who != 0 && who == _controlledActor)) && !_noTurnYet && !_turnEndSeen && _lastSelfSeqType is 0 or 1
                    && _lastSelfAck is { Sent: false } last && last.Epoch == Volatile.Read(ref _fightEpoch))
                {
                    // Enchaîné comme les autres animations : max(fin de séquence, accusé précédent) + 1447-1575 ms (10/10).
                    long nd = _lastSelfAckBase + Random.Shared.Next(1447, 1576);
                    if (nd > Volatile.Read(ref last.DueTick))
                    {
                        Volatile.Write(ref last.DueTick, nd);
                        _ackChainDue = Math.Max(_ackChainDue, nd);
                        last.Label += " — COUP DE FIN DE COMBAT, recalé à ~1,5 s";
                    }
                }
                // Avant le 1er tour : 2-12 ms (après notre accusé de début de combat s'il est encore en attente). Après NOTRE
                // tour : 0-3 ms après notre dernier accusé. Après un AUTRE combattant : quand le client a fini de jouer SES
                // animations (file commune : sort ~530-560 ms, déplacement ≥ ~500 ms, reste ~0) ; repli bimodal réel
                // (~1,6-1,9 s / ~2,37-2,44 s) si aucune de ses séquences n'a été vue. Constantes calées sur UNE espèce de monstre
                // (captures 1v1) : la forme du tour est journalisée pour recaler sur d'autres combats.
                string shape = "";
                int d;
                if (_noTurnYet) d = (int)Math.Max(0, _ackChainDue - now) + Random.Shared.Next(2, 13);
                else if ((who == _selfId || (who != 0 && who == _controlledActor))) d = (int)Math.Max(0, _ackChainDue - now) + Random.Shared.Next(0, 4);
                else if (_otherSeqSeen)
                {
                    d = (int)Math.Clamp(_ackChainDue - now, 0, 8000) + Random.Shared.Next(0, 9);
                    shape = $", forme=[{string.Join(",", _otherShape)}]";
                }
                else d = Random.Shared.Next(3) == 0 ? Random.Shared.Next(1617, 1859) : Random.Shared.Next(2366, 2445);
                if (eager) { d = _adaptiveReady.GetInitialDelayMs(); shape = $", turbo agressif (lvl={_adaptiveReady.BackoffLevel})"; }
                _adaptiveReady.NoteReadySent(now + d);
                React("FightTurnReadyRequest", VN("FightTurnReadyRequest", "is_ready", 1), d, isAck: false,
                      $"tour prêt (+{d} ms, {((who == _selfId || (who != 0 && who == _controlledActor)) ? "notre tour" : $"tour de {who}")}{shape}) — {why}");
                if (eager) ReadyBurst();
            }

            // Turbo agressif : relances « tour prêt » adaptatives tant qu'aucun nouveau tour ni nouvelle demande n'est arrivé.
            void ReadyBurst()
            {
                int gen = Interlocked.Increment(ref _readyGen);
                int epoch = Volatile.Read(ref _fightEpoch);
                int maxRetries = _adaptiveReady.GetBurstRetries();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        for (int i = 1; i <= maxRetries; i++)
                        {
                            int spacing = _adaptiveReady.GetBurstSpacingMs(i);
                            await Task.Delay(spacing, tk).ConfigureAwait(false);
                            if (!_inFight || Volatile.Read(ref _fightEpoch) != epoch || Volatile.Read(ref _readyGen) != gen) return;
                            await SendGame("FightTurnReadyRequest", VN("FightTurnReadyRequest", "is_ready", 1)).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { /* fin de session */ }
                    catch (Exception e) { SocketLog.Info(_logPid, $"[jeu] relances tour prêt interrompues : {e.GetType().Name}"); }
                });
            }

            // Sélection du perso choisi (_pendingCharId). twice : le vrai client en CONNEXION AUTO envoie la requête DEUX fois à
            // 0-2 ms d'écart (3/3 sessions ; le serveur refuse le doublon puis confirme). Sans succès en 5 s : session abandonnée.
            async Task SelectCharacter(bool twice)
            {
                if (p.SelectionGate is { } gate)
                    await gate.WaitBeforeSelectionAsync(tk).ConfigureAwait(false);
                if (_firstSelection)
                {
                    // Comme le vrai client après une création : CharacterFirstSelectionRequest { character_id, do_tutorial }, UNE seule fois.
                    var first = VN("CharacterFirstSelectionRequest", "character_id", _pendingCharId);
                    if (p.Creation?.DoTutorial == true) first.AddRange(VN("CharacterFirstSelectionRequest", "do_tutorial", 1));
                    await SendGame("CharacterFirstSelectionRequest", first).ConfigureAwait(false);
                    twice = false;
                }
                else
                {
                await SendGame("CharacterSelectionRequest", VN("CharacterSelectionRequest", "character_id", _pendingCharId)).ConfigureAwait(false);
                }
                if (twice)
                {
                    await Task.Delay(Random.Shared.Next(0, 3), tk).ConfigureAwait(false);
                    await SendGame("CharacterSelectionRequest", VN("CharacterSelectionRequest", "character_id", _pendingCharId)).ConfigureAwait(false);
                }
                Later(5000, "contrôle de la sélection", () =>
                {
                    if (!_selSuccess)
                    {
                        _endReason = "sélection du personnage sans confirmation du serveur (5 s) → session abandonnée";
                        try { session.Cancel(); } catch { }
                    }
                    return Task.CompletedTask;
                });
            }

            // Création de personnage (demandée depuis l'interface) : nom (suggestion du serveur si vide) -> CharacterCreationRequest.
            // Forme relevée sur une vraie création du client 3.7.12.12 ; champs résolus PAR NOM (le corps = champ « gccm », nom obfusqué
            // propre à ce build : s'il change au prochain patch, la création est refusée au lieu d'envoyer un octet deviné).
            async Task SendCreation(string charName)
            {
                var spec = p.Creation! with { Name = charName };
                int body = cat.FieldNumber("CharacterRemodelingInformation", CreationBodyField) ?? 0;
                var fields = new CreationFieldNumbers(
                    cat.FieldNumber("CharacterRemodelingInformation", "name") ?? 0, cat.FieldNumber("CharacterRemodelingInformation", "gender") ?? 0,
                    cat.FieldNumber("CharacterRemodelingInformation", "breed_id") ?? 0, cat.FieldNumber("CharacterRemodelingInformation", "cosmetic_id") ?? 0,
                    body, cat.FieldNumber("CharacterRemodelingInformation", "colors") ?? 0);
                int slot = cat.FieldNumber("CharacterCreationRequest", "modeling_information") ?? 0;
                byte[] inner;
                try { inner = CharacterCreationEncoder.ModelingInfo(spec, fields); }
                catch (ArgumentException e) when (slot > 0)
                {
                    _endReason = "création refusée avant envoi : " + e.Message + " (mapping de ce build ?)";
                    try { session.Cancel(); } catch { }
                    return;
                }
                if (slot <= 0) { _endReason = "création impossible : CharacterCreationRequest absent du mapping"; try { session.Cancel(); } catch { } return; }
                _createdName = charName;
                Volatile.Write(ref _createState, 2);
                SocketLog.Info(_logPid, $"[jeu] création du personnage « {charName} » (classe {spec.BreedId}, sexe {(spec.Gender == 0 ? "M" : "F")})");
                await SendGame("CharacterCreationRequest", new List<ProtoField> { new ProtoField { Field = slot, Wire = WireType.Len, Bytes = inner } }).ConfigureAwait(false);
                Later(10000, "contrôle de la création", () =>
                {
                    if (Volatile.Read(ref _createState) != 3) { _endReason = "création du personnage sans réponse du serveur (10 s)"; try { session.Cancel(); } catch { } }
                    return Task.CompletedTask;
                });
            }

            // Login jeu : IdentificationRequest { ticket_key(1)=jeton SelectServer, language_code(2)="fr" }.
            if (cat.CodeForName("IdentificationRequest") is not { } loginCode)
            { SocketLog.Info(_logPid, "[jeu] IdentificationRequest absent du mapping — connexion au jeu impossible"); return; }
            SocketLog.Info(_logPid, $"[jeu] IdentificationRequest {(LicenseService.IsDeveloper ? "= " + loginCode : "prêt")}");   // code masqué hors licence développeur
            var loginPayload = new List<ProtoField>
            {
                new ProtoField { Field = FN("IdentificationRequest", "ticket_key"), Wire = WireType.Len, String = p.Token },
                new ProtoField { Field = FN("IdentificationRequest", "language_code"), Wire = WireType.Len, String = "fr" },
            };
            var loginFramed = Frame(EnvelopeBuilder.BuildClientRequest(loginCode, loginPayload, uid: -1));
            SocketLog.Tx(_logPid, $"[jeu] → IdentificationRequest {loginFramed.Length}o");   // jamais le contenu : ticket de jeu du compte
            await stream.WriteAsync(loginFramed, tk).ConfigureAwait(false);
            await stream.FlushAsync(tk).ConfigureAwait(false);
            _loginSentAt = Environment.TickCount64;
            SocketStats.Tx(_logPid, loginFramed.Length);
            PublishObserved(p, true, loginFramed);
            if (tk.IsCancellationRequested) return;
            p.FeedFrame?.Invoke(_logPid, true, loginFramed);

            // PING UNIQUE puis surveillance d'inactivité. Le vrai client n'a AUCUN keep-alive : 0 DateRequest et au plus UN
            // PingRequest{quiet} par session, 5,0-6,5 s après l'IdentificationRequest (4 sessions MITM complètes). Le Ping/Date
            // toutes les 30 s d'un bot tiers (~120 requêtes/h qu'aucun client n'émet) est supprimé. Le Pong donne l'aller-retour
            // réel, seule valeur que le client déclare ensuite en BasicLatencyStats. Uid du ping : petit entier (le seul
            // message du client qui n'est pas en -1 dans ses trames de login).
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(Random.Shared.Next(5000, 6501), tk).ConfigureAwait(false);
                    Volatile.Write(ref _pingSentAt, Environment.TickCount64);
                    await SendGame("PingRequest", VN("PingRequest", "quiet", 1), uid: 1).ConfigureAwait(false);
                    while (true)
                    {
                        await Task.Delay(15_000, tk).ConfigureAwait(false);
                        // Login de jeu resté SANS réponse (connexion acceptée puis muette, proxy qui bloque) : on n'attend pas
                        // les 330 s d'inactivité, la boucle de reconnexion retentera.
                        if (!LoginAccepted && Environment.TickCount64 - _loginSentAt > 30_000)
                        {
                            _endReason = "login de jeu sans réponse depuis 30 s → session abandonnée";
                            try { session.Cancel(); } catch { }
                            return;
                        }
                        long idle = Environment.TickCount64 - Volatile.Read(ref _lastRx);
                        if (idle > RxIdleLimitMs)
                        {
                            _endReason = $"aucune trame du serveur depuis {idle / 1000} s (il sonde toutes les 4 min) → connexion tenue pour MORTE";
                            try { session.Cancel(); } catch { }
                            return;
                        }
                    }
                }
                catch (OperationCanceledException) { /* fin de session */ }
                catch (Exception e)
                {
                    // Écriture impossible = socket morte : on termine la session (la boucle de reconnexion prend le relais).
                    _endReason = $"écriture impossible ({e.GetType().Name}) → session terminée";
                    try { session.Cancel(); } catch { }
                }
            });

            // Réactions de COMBAT, dans l'ordre, chacune à son heure. Une réaction d'un combat terminé est jetée.
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var r in _reactions.Reader.ReadAllAsync(tk).ConfigureAwait(false))
                    {
                        try
                        {
                            // Échéance relue en boucle : elle peut être RECALÉE plus tard pendant l'attente (coup de fin de combat).
                            while (true)
                            {
                                long wait = Volatile.Read(ref r.DueTick) - Environment.TickCount64;
                                if (wait <= 0) break;
                                await Task.Delay((int)Math.Min(wait, 25), tk).ConfigureAwait(false);
                            }
                            r.Sent = true;
                            if (r.Epoch != Volatile.Read(ref _fightEpoch) || !_inFight)
                                SocketLog.Info(_logPid, $"[jeu] ⚔ {r.Label} — ANNULÉ (combat terminé entre-temps)");
                            else if (await SendGame(r.Name, r.Payload).ConfigureAwait(false))
                                SocketLog.Info(_logPid, $"[jeu] ⚔ {r.Label}");
                        }
                        finally { if (r.IsAck) Interlocked.Decrement(ref _pendingAcks); }
                    }
                }
                catch (OperationCanceledException) { /* fin de session */ }
                catch (Exception e)
                {
                    // Écriture impossible : sans cette file, plus aucun accusé ni « tour prêt » ne partirait (timeouts de 15 s
                    // à chaque tour, actions du moteur bloquées) alors que la lecture continue -> on termine la session.
                    _endReason = $"file de réactions de combat arrêtée ({e.GetType().Name}) → session terminée";
                    try { session.Cancel(); } catch { }
                }
            });

            // TCP et login ne publient aucun injecteur. Seuls CharacterSelectionEvent
            // confirmé + contexte serveur authentique pourront l'enregistrer (voir
            // PublishGameplayInjectorIfReady). La file reste inactive en revue
            // inter-patch sans preuve wire/ACK, même si SocketProbe passe un callback.
            _ = Task.Run(async () =>
            {
                long lastWrite = 0, lastEnq = 0, echoUntil = 0;
                int echoBase = 0;
                try
                {
                    await foreach (var o in _outbound.Reader.ReadAllAsync(tk).ConfigureAwait(false))
                    {
                        // Une FENÊTRE vient de s'ouvrir (liste de zaaps, HDV, atelier) : un humain lit avant de cliquer. Le vrai
                        // client choisit sa destination ~1,5 s après la liste de zaaps ; le moteur le faisait en ~65 ms (42/42),
                        // et agissait dans le HDV / l'atelier en 0-1 ms. Seule la 1re action mise en file APRÈS l'ouverture attend.
                        long thinkUntil = Volatile.Read(ref _thinkUntil);
                        if (thinkUntil > 0 && o.EnqTick >= Volatile.Read(ref _thinkSetAt))
                        {
                            if (p.TurboAckAll?.Invoke() != true)
                            {
                                long w = thinkUntil - Environment.TickCount64;
                                if (w > 0) await Task.Delay((int)w, tk).ConfigureAwait(false);
                            }
                            Volatile.Write(ref _thinkUntil, 0);
                        }
                        // EN COMBAT, le vrai client n'enchaîne une action qu'après avoir VU revenir la précédente et fini son
                        // animation. Trois retenues, toutes bornées :
                        if (_inFight)
                        {
                            // 1) l'écart d'origine voulu par le moteur (cadence de son profil) est conservé à l'envoi quand cette
                            //    action a été mise en file PENDANT que la précédente était retenue : jamais deux actions collées
                            //    dans la même milliseconde (mise en file après l'envoi précédent = écart déjà réel, rien à faire) ;
                            if (lastWrite > 0 && o.EnqTick <= lastWrite)
                            {
                                long due = lastWrite + Math.Max(0, o.EnqTick - lastEnq);
                                long w = due - Environment.TickCount64;
                                if (w > 0) await Task.Delay((int)Math.Min(w, AckGateMaxMs), tk).ConfigureAwait(false);
                            }
                            // 2) la séquence serveur de l'action précédente doit être revenue (NOTRE séquence externe close) ;
                            // Turbo / placement : inutile (le moteur attend déjà la fin de séquence ; défis, validation et « prêt » n'ouvrent aucune
                            // séquence et subissaient 300-800 ms de retenue CHACUN avant le combat).
                            bool skipEcho = _noTurnYet || p.TurboAck?.Invoke() == true;
                            int pollMs = skipEcho ? 1 : 10;   // Turbo / placement : minuteur 1 ms (Program.EnableHighResolutionTimer)
                            while (!skipEcho && Environment.TickCount64 < echoUntil && Volatile.Read(ref _selfSeqEnds) == echoBase)
                                await Task.Delay(pollMs, tk).ConfigureAwait(false);
                            // 3) et son accusé (après l'animation) doit être parti : jamais d'action sur une séquence non accusée.
                            if (Volatile.Read(ref _pendingAcks) > 0)
                            {
                                long until = Environment.TickCount64 + AckGateMaxMs;
                                while (Volatile.Read(ref _pendingAcks) > 0 && Environment.TickCount64 < until)
                                    await Task.Delay(pollMs, tk).ConfigureAwait(false);
                            }
                        }
                        if (!await WriteQueuedEngineActionAsync(stream!, o, p, tk)
                                .ConfigureAwait(false)) return;
                        SocketStats.Tx(_logPid, o.Frame.Length);
                        if (tk.IsCancellationRequested || !IsCurrentSocketLink(o.Source))
                            return; // old session must not schedule review/log actions
                        // Timebox the single cross-patch experiment from the
                        // actual TCP write, not from the queuing time.
                        if (Volatile.Read(ref _reviewDiagnosticMode) == 1 && o.ReviewMoveGeneration > 0)
                        {
                            int generation = o.ReviewMoveGeneration;
                            Later(20_000, "diagnostic mouvement sans réponse", () =>
                            {
                                if (Volatile.Read(ref _reviewMoveGeneration) == generation &&
                                    (Interlocked.CompareExchange(ref _reviewMoveState, -1, 1) == 1 ||
                                     Interlocked.CompareExchange(ref _reviewMoveState, -1, 2) == 2 ||
                                     Interlocked.CompareExchange(ref _reviewMoveState, -1, 3) == 3))
                                {
                                    Volatile.Write(ref _injectionReady, 0);
                                    SocketLog.Info(_logPid, "[jeu] ⚠ essai de déplacement : " +
                                        "pas de réponse de confirmation corrélable dans 20 s ; pilotage verrouillé");
                                }
                                return Task.CompletedTask;
                            });
                        }
                        lastWrite = Environment.TickCount64; lastEnq = o.EnqTick;
                        // Attente de l'écho serveur de CETTE action (bornée : une trame qui n'ouvre pas de séquence, ex.
                        // placement ou chat, ne retient la suivante que ~3 allers-retours).
                        echoBase = Volatile.Read(ref _selfSeqEnds);
                        echoUntil = lastWrite + Math.Clamp(_rttMs * 3, 300, 800);
                        // PAS de retour au décodeur : parité MITM, où une injection du moteur ne passe JAMAIS par le tap (contrat
                        // IGameTransport). Sinon le moteur prend SES propres trames pour des actions MANUELLES du client : jrw ->
                        // SelfMoved en double (followers d'équipe rejouent 2x le trajet), jqi « du client », InteractiveUse/
                        // DialogLeave manuels, hooks Lua sortants qui voient les forges. Le moteur affiche déjà ses injections.
                        SocketLog.Tx(_logPid, $"[jeu] ⇒ injection pilotage {o.Frame.Length}o" +
                                              (lastWrite - o.EnqTick > 20 ? $" (retenue {lastWrite - o.EnqTick} ms : animation/accusé en cours)" : ""));
                    }
                }
                catch (OperationCanceledException) { /* Stop */ }
                catch (Exception e)
                {
                    // Écriture impossible = socket morte : on termine la session (la boucle de reconnexion prend le relais).
                    _endReason = $"file d'injection arrêtée ({e.GetType().Name}) → session terminée";
                    try { session.Cancel(); } catch { }
                }
            });

            var acc = new List<byte>(8192);
            var read = new byte[16384];

            // Début / fin de combat : tout ce qui était planifié pour le combat précédent devient caduc.
            void FightBoundary(bool start)
            {
                Interlocked.Increment(ref _fightEpoch);
                _controlledActor = 0; _inFight = start; _seqOpen = 0; _turnReadyPending = false;
                _ackChainDue = 0; _seqMoveSteps = 0; _lastSelfAck = null; _lastSelfSeqType = -1; _turnEndSeen = false;
                _earlyReadySent = false; Interlocked.Increment(ref _readyGen);
                if (start) { _noTurnYet = true; _challengePending = true; }
                else { _afterFight = true; _afterFightMap = true; _adaptiveReady.Reset(); }
            }

            while (!tk.IsCancellationRequested)
            {
                int n;
                try { n = await stream.ReadAsync(read, tk).ConfigureAwait(false); }
                catch (IOException) when (!LoginAccepted && !tk.IsCancellationRequested) { n = 0; }   // RST juste après le login = éjection
                if (n <= 0)
                {
                    // Fermé APRÈS notre login et AVANT son acceptation : session précédente du compte tenue puis éjectée.
                    if (!LoginAccepted) _closedBeforeAccept = true;
                    SocketLog.Info(_logPid, "[jeu] le serveur a fermé la connexion" + (LoginAccepted ? "" : " (avant d'accepter le login)"));
                    return;
                }
                Volatile.Write(ref _lastRx, Environment.TickCount64);
                acc.AddRange(read.AsSpan(0, n).ToArray());
                while (TryDeframe(acc, out var frame))
                {
                    SocketStats.Rx(_logPid, frame.Length);
                    // ORDRE DU VRAI CLIENT, trame par trame : NOTRE réaction protocolaire d'abord (planification d'accusé, tour
                    // prêt…), PUIS le moteur voit la trame. Les accusés planifiés retiennent les actions du moteur (file
                    // d'injection) jusqu'à leur envoi : jamais d'action sur une séquence que le serveur attend qu'on accuse.
                    var hits = new List<RawProtobuf.TypeUrlHit>();
                    try
                    {
                        var tree = RawProtobuf.Parse(frame);
                        hits = RawProtobuf.FindTypeUrls(tree);
                        if (hits.Count == 0) SocketLog.Rx(_logPid, $"[jeu] trame {frame.Length}o (aucun type_url)");
                    }
                    catch { SocketLog.Rx(_logPid, $"[jeu] trame {frame.Length}o (illisible) : {SocketLog.Hex(frame, 48)}"); }
                    foreach (var hit in hits)
                    {
                        var name = cat.NameForCode(hit.Code) ?? "?";
                        SocketLog.Rx(_logPid, $"[jeu] {ProtoLog.Named(name, hit.Code)} ({frame.Length}o)");   // code obfusqué masqué hors licence développeur
                        // Structure du message (numéros de champ) : pendant la CONNEXION (recalage sélection de perso, etc.) et
                        // pour les codes inconnus du mapping. Pas pour tout le flux en jeu : ToJson de CHAQUE trame (combat :
                        // ~2 500 lignes/min pour UN compte) coûtait sur le fil de lecture de 30 comptes pour rien.
                        // Arbre brut (numéros de champ, messages imbriqués) : outil de recalage -> licence développeur uniquement.
                        if (LicenseService.IsDeveloper && hit.Payload is { Count: > 0 } dbg && (!_loginBurstDone || name == "?"))
                        {
                            var j = RawProtobuf.ToJson(dbg).Replace("\r", "").Replace("\n", " ");
                            SocketLog.Rx(_logPid, "[jeu]   " + (j.Length > 500 ? j[..500] + "…" : j));
                        }
                        switch (name)
                        {
                            // ================= CONNEXION : le client enchaîne liste persos -> sélection -> contexte =================
                            case "AuthenticationTicketAcceptedEvent":
                                // Le vrai client demande la liste des persos APRÈS la DERNIÈRE trame du lot de connexion
                                // (ServerSessionReadyEvent, +3-7 ms), juste avant son ClientIdRequest (4/4) -> envoi là-bas ; repli
                                // ici si ServerSessionReady n'arrive pas.
                                LoginAccepted = true;
                                SocketLog.Info(_logPid, "[jeu] login accepté");
                                Later(80, "liste des personnages (repli)", async () =>
                                {
                                    if (!TakeCharList()) return;
                                    await SendGame("CharacterListRequest").ConfigureAwait(false);
                                });
                                break;
                            case "AuthenticationTicketRefusedEvent":
                                SocketLog.Info(_logPid, "✘ [jeu] jeton de jeu REFUSÉ par le serveur (AuthenticationTicketRefusedEvent)");
                                break;
                            case "CharacterListEvent":     // liste des persos (nom v5 ; l'ancien « CharacterInformation » venait du pont confirmed.json)
                                if (p.Deletion is { } del)
                                {
                                    // Suppression : jamais de sélection. 1re liste -> demande de préparation ; liste suivante (après la suppression) -> contrôle.
                                    long wantedId = FindCharacterId(hit.Payload, del.CharacterName, FN);
                                    if (Volatile.Read(ref _deleteState) == 2)
                                    {
                                        _deleteDone = true;
                                        if (wantedId == 0) { _endReason = $"✔ personnage « {del.CharacterName} » supprimé"; SocketLog.Info(_logPid, "✔ [jeu] " + _endReason); }
                                        else _endReason = $"suppression sans effet : « {del.CharacterName} » est toujours dans la liste";
                                        try { session.Cancel(); } catch { }
                                    }
                                    else if (Interlocked.CompareExchange(ref _deleteState, 1, 0) == 0)
                                    {
                                        if (wantedId == 0) { _endReason = $"suppression annulée : personnage « {del.CharacterName} » introuvable sur ce serveur"; try { session.Cancel(); } catch { } }
                                        else
                                        {
                                            _deleteId = wantedId;
                                            SocketLog.Info(_logPid, $"[jeu] suppression du personnage « {del.CharacterName} » : préparation");
                                            Later(Random.Shared.Next(300, 700), "préparation de la suppression", () => SendGame("PrepareCharacterDeletionRequest", VN("PrepareCharacterDeletionRequest", "character_id", wantedId)));
                                            Later(15000, "contrôle de la suppression", () =>
                                            {
                                                if (!_deleteDone) { _endReason = "suppression sans réponse du serveur (15 s)"; try { session.Cancel(); } catch { } }
                                                return Task.CompletedTask;
                                            });
                                        }
                                    }
                                    break;
                                }
                                if (Volatile.Read(ref _selStarted) != 0 || _pendingCharId > 0) break;   // une seule sélection
                                if (p.Creation is { } wanted && Volatile.Read(ref _createState) < 3)
                                {
                                    // 1re liste : on crée (jamais de sélection d'un personnage existant). Liste suivante (après la création) : on
                                    // sélectionne le nouveau, plus bas.
                                    if (Interlocked.CompareExchange(ref _createState, 1, 0) == 0)
                                    {
                                        if (string.IsNullOrWhiteSpace(wanted.Name))
                                            Later(Random.Shared.Next(300, 700), "nom suggéré", () => SendGame("CharacterNameSuggestionRequest"));
                                        else Later(Random.Shared.Next(300, 700), "création du personnage", () => SendCreation(wanted.Name));
                                    }
                                    break;
                                }
                                var (charId, charName, charLevel) = PickCharacter(hit.Payload, _createdName ?? p.PreferredCharacter, FN);
                                if (_createdName is not null) _firstSelection = true;
                                if (charId > 0)
                                {
                                    // Le vrai client (connexion auto du launcher, 3/3) sélectionne 13-95 ms APRÈS la 1re liste
                                    // d'actions (GameActionItemListEvent) qui suit la liste des persos -> sélection différée. Repli :
                                    // 100-150 ms après la liste si cette trame n'arrive pas.
                                    _pendingCharId = charId; _selfId = charId; _selfLevel = charLevel;
                                    SocketLog.Info(_logPid, $"[jeu] personnage « {charName ?? "?"} » id={charId} niv {charLevel} → sélection");
                                    Later(Random.Shared.Next(100, 151), "sélection du personnage (repli)", async () =>
                                    {
                                        if (_forced || !StartSelection()) return;
                                        await SelectCharacter(twice: false).ConfigureAwait(false);
                                    });
                                }
                                else SocketLog.Info(_logPid, "[jeu] personnage demandé introuvable ou id absent : aucune sélection (voir payload ci-dessus)");
                                break;
                            case "CharacterDeletionPrepareEvent":
                            {
                                if (p.Deletion is not { } delSpec || _deleteId <= 0 || Interlocked.CompareExchange(ref _deleteState, 2, 1) != 1) break;
                                bool needsAnswer = PV(hit.Payload, FN("CharacterDeletionPrepareEvent", "need_secret_answer")) == 1;
                                if (!needsAnswer)
                                {
                                    // Sans code secret, le client envoie un hash que l'on ne sait pas encore reproduire (3 captures sans code : aucune formule trouvée).
                                    _deleteDone = true;
                                    _endReason = "suppression non effectuée : ce compte n'a pas de code secret et ce cas n'est pas encore pris en charge";
                                    try { session.Cancel(); } catch { }
                                    break;
                                }
                                if (string.IsNullOrEmpty(delSpec.Answer)) { _deleteDone = true; _endReason = "suppression non effectuée : réponse secrète vide"; try { session.Cancel(); } catch { } break; }
                                // Forme relevée sur 3 suppressions réelles (client 3.7.12.12) : #1 = MD5(id~réponse) en hexadécimal minuscule, #2 = id du personnage.
                                // Numéros pris sur la capture (le mapping annonce 2 et 3 : décalé) — jamais devinés.
                                var req = new List<ProtoField>
                                {
                                    new ProtoField { Field = 1, Wire = WireType.Len, String = CharacterDeletionEncoder.AnswerHash(_deleteId, delSpec.Answer) },
                                    VF(2, _deleteId),
                                };
                                SocketLog.Info(_logPid, "[jeu] suppression : envoi de la confirmation");
                                Later(Random.Shared.Next(900, 1800), "confirmation de la suppression", () => SendGame("CharacterDeletionRequest", req));
                                break;
                            }
                            case "CharacterDeletionErrorEvent":
                                if (p.Deletion is not null)
                                {
                                    _deleteDone = true;
                                    long why = PV(hit.Payload, FN("CharacterDeletionErrorEvent", "reason"));
                                    _endReason = $"suppression refusée par le serveur (raison {why}" + (why == 2 ? " : réponse secrète incorrecte probable)" : ")");
                                    try { session.Cancel(); } catch { }
                                }
                                break;
                            case "CharacterNameSuggestionEvent":
                                if (p.Creation is not null && Volatile.Read(ref _createState) == 1)
                                {
                                    string? suggested = CharacterCreationTab.SuggestionName(hit.Payload, cat.FieldNumber);
                                    if (!string.IsNullOrWhiteSpace(suggested))
                                        Later(Random.Shared.Next(400, 900), "création du personnage", () => SendCreation(suggested));
                                    else
                                    {
                                        _endReason = CharacterCreationTab.SuggestionFailed(null);
                                        CharacterCreationReports.Publish(p.AccountLogin, _endReason);
                                        try { session.Cancel(); } catch { }
                                    }
                                }
                                break;
                            case "CharacterNameGenerationFailedEvent":
                                if (p.Creation is not null && Volatile.Read(ref _createState) == 1)
                                {
                                    _endReason = CharacterCreationTab.SuggestionFailed(CharacterCreationTab.SuggestionFailureReason(hit.Payload, cat.FieldNumber));
                                    CharacterCreationReports.Publish(p.AccountLogin, _endReason);
                                    try { session.Cancel(); } catch { }
                                }
                                break;
                            case "CharacterCreationResultEvent":
                            {
                                // result absent = 0 = CREATE_OK (valeur par défaut omise, comme observé). Le numéro de champ vient du catalogue.
                                long res = hit.Payload?.FirstOrDefault(f => f.Field == FN("CharacterCreationResultEvent", "result") && f.Wire == WireType.Varint) is { } rf ? (long)rf.Varint : 0;
                                if (res == 0)
                                {
                                    Volatile.Write(ref _createState, 3);
                                    SocketLog.Info(_logPid, $"✔ [jeu] personnage « {_createdName} » créé — liste mise à jour attendue, puis première sélection");
                                    CharacterCreationReports.Publish(p.AccountLogin, CharacterCreationTab.CreatedText, _createdName);
                                }
                                else
                                {
                                    int? reasonField = cat.FieldNumber("CharacterCreationResultEvent", "reason");
                                    long? why = reasonField is > 0 && hit.Payload?.FirstOrDefault(f => f.Field == reasonField && f.Wire == WireType.Varint) is { } rr ? (long)rr.Varint : null;
                                    _endReason = CharacterCreationTab.RefusalText(res, why) ?? $"Le serveur a refusé la création (résultat {res}).";
                                    CharacterCreationReports.Publish(p.AccountLogin, _endReason);
                                    try { session.Cancel(); } catch { }
                                }
                                break;
                            }
                            case "GameActionItemListEvent":
                                if (_pendingCharId > 0 && !_forced && StartSelection())
                                {
                                    Later(Random.Shared.Next(13, 96), "sélection du personnage", () => SelectCharacter(twice: true));
                                }
                                break;
                            case "CharacterForceSelectionEvent":
                                // Le serveur IMPOSE le perso (reconnexion d'un compte dont le perso est encore en combat). Constaté 2×
                                // en socket (16:57:49 / 16:58:46) : la liste arrive AVANT, UNE sélection suffit et le serveur enchaîne
                                // sur FightJoinRunningEvent sans attendre de « prêt » -> pas de CharacterForceSelectionReadyRequest
                                //. Pas de liste d'actions ici:
                                // sélection tout de suite.
                                SocketLog.Info(_logPid, "[jeu] sélection de personnage IMPOSÉE par le serveur (perso encore en combat ?)");
                                _forced = true;
                                if (_pendingCharId > 0 && StartSelection())
                                {
                                    Later(Random.Shared.Next(1, 6), "sélection imposée", () => SelectCharacter(twice: false));
                                }
                                break;
                            case "CharacterSelectionEvent":
                                // Réponse { success #1 } ou { error #3 } : la 2e sélection (doublon du client en connexion auto) reçoit
                                // TOUJOURS un refus avant le succès — attendu, pas une erreur.
                                bool okSel = hit.Payload?.Any(f => f.Field == FN("CharacterSelectionEvent", "success")) == true;
                                if (!okSel) { SocketLog.Info(_logPid, "[jeu] sélection : refus du doublon (attendu, comme le vrai client)"); break; }
                                _selSuccess = true;
                                // Un personnage sélectionné n'est pas encore un contexte
                                // de jeu jouable. L'injecteur attend AUSSI ContextCreationEvent
                                // (ou FightJoinRunningEvent en reconnexion de combat).
                                PublishGameplayInjectorIfReady(name);
                                SocketLog.Info(_logPid, "✔ [jeu] PERSONNAGE SÉLECTIONNÉ");
                                // Filet (sélection imposée par le serveur sans passer par notre liste) : l'id du perso est aussi
                                // dans success { <Character> { id } } (2 niveaux, cf capture) — indispensable pour savoir QUELLES
                                // séquences accuser. Le type imbriqué Success n'a pas d'entrée propre dans fields.json (son nom court
                                // est pris par un autre message) : on prend le sous-message qui porte un Character.id, par nom.
                                int charIdF = FN("Character", "id");
                                if (_selfId == 0
                                    && hit.Payload?.FirstOrDefault(f => f.Field == FN("CharacterSelectionEvent", "success"))?.Message is { } okc
                                    && okc.Select(f => f.Message).FirstOrDefault(m => m is not null && PV(m, charIdF) != 0) is { } och)
                                {
                                    _selfId = PV(och, charIdF);
                                    if (_selfId != 0) SocketLog.Info(_logPid, $"[jeu] identité du perso reprise de la sélection : id={_selfId}");
                                }
                                break;
                            case "QuestsFollowedEvent":
                                // Quêtes suivies : le client redemande leurs étapes à l'entrée en jeu (2x) et après chaque combat.
                                _questIds.Clear();
                                int qf = FN("QuestsFollowedEvent", "quests"), qid = FN("QuestActive", "quest_id");
                                foreach (var q in hit.Payload ?? new())
                                    if (q.Field == qf && q.Message is { } qm && PV(qm, qid) is long id and > 0) _questIds.Add((int)id);
                                SocketLog.Info(_logPid, $"[jeu] {_questIds.Count} quête(s) suivie(s)");
                                break;
                            case "CharacterLoadingCompleteEvent":
                                // Vrai client : ~1 s après, 8 requêtes d'initialisation (contacts/guilde, payloads par défaut),
                                // puis ~0,33 s plus tard la création du contexte + l'abonnement au livre des métiers.
                                SocketLog.Info(_logPid, "[jeu] chargement du personnage terminé → initialisation client puis création du contexte");
                                // Mesuré (4/4) : les 5 réglages à +972-1018 ms, les 3 requêtes de guilde 0-65 ms après, la création
                                // du contexte 345-435 ms après les réglages.
                                int d1 = Random.Shared.Next(970, 1021), d2 = d1 + Random.Shared.Next(345, 436);
                                Later(d1, "initialisation contacts/guilde", async () =>
                                {
                                    foreach (var m in new[] { "ContactWarnOnAchievementCompleteSetRequest", "FriendSetWarnOnConnectionRequest",
                                                              "FriendSetWarnOnLevelGainRequest", "ContactWarnOnPermanentDeathSetRequest",
                                                              "FriendSetStatusShareRequest" })
                                        await SendGame(m).ConfigureAwait(false);
                                    await Task.Delay(Random.Shared.Next(0, 66), tk).ConfigureAwait(false);
                                    await SendGame("GuildInformationRequest", VN("GuildInformationRequest", "information_type", 3)).ConfigureAwait(false);
                                    await SendGame("GuildInformationRequest").ConfigureAwait(false);
                                    await SendGame("GuildWeeklyNextResetRequest").ConfigureAwait(false);
                                });
                                Later(d2, "création du contexte", async () =>
                                {
                                    await SendGame("ContextCreationRequest").ConfigureAwait(false);
                                    // Livre des artisans : envoyé 3/3 par un perso niv 139 (métiers niv 200), JAMAIS par un perso
                                    // niv 8 (métiers ≤ 2). Condition exacte du client inconnue -> seuil de niveau prudent, qui
                                    // reproduit les deux cas observés (à affiner avec d'autres captures).
                                    if (_selfLevel >= 100)
                                    {
                                        await Task.Delay(Random.Shared.Next(5, 10), tk).ConfigureAwait(false);
                                        await SendGame("JobBookSubscribeRequest").ConfigureAwait(false);
                                    }
                                });
                                break;
                            case "ContextCreationEvent":
                                _ctxFight = PV(hit.Payload, FN("ContextCreationEvent", "context")) == 1;   // 0 = jeu de rôle, 1 = combat
                                gameContextSeen = true;
                                PublishGameplayInjectorIfReady(name);
                                if (!_loginBurstDone && !_ctxFight)
                                {
                                    _loginBurstDone = true;
                                    SocketLog.Info(_logPid, "✅✅ [jeu] EN JEU — contexte créé. Connexion CLIENTLESS COMPLÈTE (sans Dofus.exe) !");
                                    LoginContextBurst();
                                    AntiAfkLoop();
                                }
                                else if (!_ctxFight && _afterFight)
                                {
                                    // Retour de combat : le client redemande l'étape de chaque quête suivie (~10 ms après).
                                    _afterFight = false;
                                    Later(Random.Shared.Next(8, 16), "étapes de quêtes (après combat)", async () =>
                                    {
                                        foreach (var q in _questIds.ToArray())
                                            await SendGame("QuestStepInformationRequest", VN("QuestStepInformationRequest", "quest_id", q)).ConfigureAwait(false);
                                    });
                                }
                                break;

                            // ================= CARTE : le client réclame le contenu (sinon aucune carte, UI vide) =================
                            // Vrai client, 412/412 cartes : ContextReadyRequest{map} PUIS MapInformationRequest{map}, ~300 ms après
                            // MapCurrentEvent (1re carte : ~1,6 s, précédée d'AnomalySubareaInformationRequest). En COMBAT :
                            // FightPingsRequest + ContextReadyRequest seulement, jamais de MapInformationRequest : 1er combat de la
                            // session 484/657 ms, les suivants 293-317 ms (10 combats) ; le serveur répond +33 ms après.
                            case "MapCurrentEvent":
                            case "MapCurrentInstanceEvent":   // carte INSTANCIÉE (donjon / Kolizéum) : même besoin de contenu
                                long mapId = PV(hit.Payload, FN(name, "map_id"));
                                if (mapId == 0) break;
                                if (_ctxFight)
                                {
                                    bool isTurboFight = p.TurboAckAll?.Invoke() == true;
                                    int df = isTurboFight ? Random.Shared.Next(10, 25)
                                           : !_firstFightMapDone ? Random.Shared.Next(460, 701) : Random.Shared.Next(285, 326);
                                    _firstFightMapDone = true;
                                    SocketLog.Info(_logPid, $"[jeu] carte de combat {mapId} → prêt (+{df} ms)");
                                    Later(df, "carte de combat", async () =>
                                    {
                                        await SendGame("FightPingsRequest").ConfigureAwait(false);
                                        await SendGame("ContextReadyRequest", VN("ContextReadyRequest", "map_id", mapId)).ConfigureAwait(false);
                                    });
                                }
                                else
                                {
                                    bool first = !_firstMapDone;
                                    _firstMapDone = true;
                                    // Distributions RÉELLES (jpw -> kkg) : 1re carte de la session 820-1641 ms (plus long avec des quêtes
                                    // suivies, 1300-1650) ; 1re carte après un combat 422-760 ms (10/10, médiane ~450) ; sinon médiane
                                    // 300, p90 ~325, queue jusqu'à ~680 ms (n=701).
                                    var rnd = Random.Shared;
                                    bool isTurbo = p.TurboAckAll?.Invoke() == true;
                                    int dm = isTurbo ? rnd.Next(8, 20)
                                           : first ? (_questIds.Count > 0 ? rnd.Next(1300, 1651) : rnd.Next(820, 1101))
                                           : _afterFightMap ? (rnd.Next(10) == 0 ? rnd.Next(570, 761) : rnd.Next(422, 571))
                                           : rnd.Next(100) switch { < 90 => rnd.Next(280, 331), < 97 => rnd.Next(331, 400), _ => rnd.Next(400, 681) };
                                    _afterFightMap = false;
                                    SocketLog.Info(_logPid, $"[jeu] carte {mapId} annoncée → demande du contenu (+{dm} ms)");
                                    Later(dm, "chargement de carte", async () =>
                                    {
                                        if (first) await SendGame("AnomalySubareaInformationRequest").ConfigureAwait(false);
                                        await SendGame("ContextReadyRequest", VN("ContextReadyRequest", "map_id", mapId)).ConfigureAwait(false);
                                        await SendGame("MapInformationRequest", VN("MapInformationRequest", "map_id", mapId)).ConfigureAwait(false);
                                    });
                                }
                                break;

                            // ================= MÉNAGE : séquence, latence, ping =================
                            case "SequenceNumberEvent":
                                // Réponse immédiate {number = 1, 2, …}. Sans réponse: désync + détection.
                                await SendGame("SequenceNumberRequest", VN("SequenceNumberRequest", "number", ++_seq)).ConfigureAwait(false);
                                break;
                            case "BasicLatencyStatsEvent":
                                // Le vrai client déclare l'aller-retour MESURÉ de son ping (33 / 87 ms constatés), constant sur la
                                // session, et un payload VIDE s'il n'a pas pingué. Jamais une valeur inventée.
                                // Uid d'enveloppe : le serveur n'émet jamais d'enveloppe REQUEST (rien à recopier, contrairement à
                                // Un bot tiers) -> uid 0 = omis. ⚠ À CONFIRMER à l'octet près: la capture de découverte MITM journalise
                                // désormais l'hex des trames sortantes du vrai client (ping / latence / guilde).
                                int rtt = _rttMs;
                                await SendGame("BasicLatencyStatsRequest",
                                               rtt > 0 ? VN("BasicLatencyStatsRequest", "latency", rtt) : None(), uid: 0).ConfigureAwait(false);
                                break;
                            case "PongEvent":
                                long sent = Volatile.Read(ref _pingSentAt);
                                if (sent > 0 && _rttMs == 0)
                                {
                                    _rttMs = (int)Math.Clamp(Environment.TickCount64 - sent, 1, 5000);
                                    SocketLog.Info(_logPid, $"[jeu] aller-retour mesuré : {_rttMs} ms (déclaré en BasicLatencyStats)");
                                }
                                break;

                            // ================= COMBAT : le rôle du CLIENT (calé sur les trames réelles en MITM) =================
                            case "FightStartingEvent":
                            case "FightJoinEvent":
                                if (!_inFight) SocketLog.Info(_logPid, $"[jeu] ⚔ combat : début ({name}) — accusés de NOS séquences + tour prêt actifs");
                                if (!_inFight || name == "FightStartingEvent") FightBoundary(start: true);
                                break;
                            case "FightJoinRunningEvent":
                                // Reconnexion dans un combat EN COURS : on reprend le rôle de combat sans remettre à zéro une
                                // séquence ouverte (sinon un accusé partirait sur une sous-séquence).
                                SocketLog.Info(_logPid, "[jeu] ⚔ reprise d'un combat en cours (FightJoinRunningEvent)");
                                if (!_inFight) { Interlocked.Increment(ref _fightEpoch); _inFight = true; }
                                // Server-confirmed combat context can precede an ordinary
                                // ContextCreationEvent when resuming an existing fight.
                                // Still require CharacterSelectionEvent success.
                                gameContextSeen = true;
                                PublishGameplayInjectorIfReady(name);
                                break;
                            case "FightSynchronizeEvent":
                                // Arrive dans CHAQUE combat, à l'intérieur de la séquence FIGHT_START : ne touche à rien d'autre.
                                if (!_inFight) { Interlocked.Increment(ref _fightEpoch); _inFight = true; }
                                break;
                            case "ChallengeNumberEvent":
                                // Entrée en combat : le client envoie son choix de bonus et de mode de défi (payloads par défaut)
                                // après l'annonce des défis qui suit FightStarting/Join. 1er combat de la session : DEUX paires
                                // collées, 37-95 ms après (2/2 sessions) ; combats suivants : UNE paire, 17-20 ms après (8/8). Le
                                // moteur ne les envoie pas (en MITM c'est le client).
                                if (_inFight && _challengePending)
                                {
                                    _challengePending = false;
                                    bool firstFight = !_firstFightDone;
                                    _firstFightDone = true;
                                    Later(firstFight ? Random.Shared.Next(35, 96) : Random.Shared.Next(16, 21), "choix de défi", async () =>
                                    {
                                        // Bonus du profil (butin = 1 ; XP = champ omis, proto3) : le vrai client envoie ici le
                                        // bonus MÉMORISÉ de son joueur.
                                        bool loot = p.ChallengeLoot?.Invoke(_logPid) == true;
                                        for (int pair = 0; pair < (firstFight ? 2 : 1); pair++)
                                        {
                                            await SendGame("ChallengeBonusChoiceRequest", loot ? VN("ChallengeBonusChoiceRequest", "challenge_bonus", 1) : None()).ConfigureAwait(false);
                                            await SendGame("ChallengeModSelectRequest").ConfigureAwait(false);
                                        }
                                    });
                                }
                                break;
                            case "FightEndEvent":
                                SocketLog.Info(_logPid, "[jeu] ⚔ combat : fin (FightEndEvent)");
                                FightBoundary(start: false);
                                break;
                            case "MapComplementaryInformationEvent":
                                // Filet : une carte de JEU DE RÔLE n'arrive jamais en combat -> si on se croit encore en combat
                                // (FightEndEvent manqué), on le clôt ici (même logique que LaunchService).
                                if (_inFight) { SocketLog.Info(_logPid, "[jeu] ⚔ combat clos (retour carte de jeu de rôle)"); FightBoundary(start: false); }
                                break;
                            case "FightTurnEvent":
                                _adaptiveReady.NoteTurnStart(Environment.TickCount64);
                                // Début de tour : aucune séquence ne doit rester ouverte -> re-synchronise le compteur si une
                                // Start/End a été manquée. Un tour prêt encore différé de l'ancien tour est caduc.
                                if (_seqOpen != 0 || _turnReadyPending)
                                    SocketLog.Info(_logPid, $"[jeu] ⚔ nouveau tour : resynchro (séquences ouvertes {_seqOpen} -> 0{(_turnReadyPending ? ", tour prêt en attente abandonné" : "")})");
                                _seqOpen = 0; _turnReadyPending = false; _noTurnYet = false; _turnEndSeen = false;
                                Interlocked.Increment(ref _readyGen);
                                _turnOwner = PV(hit.Payload, FN("FightTurnEvent", "character_id"));
                                _otherSeqSeen = false; _otherShape.Clear();
                                break;
                            case "FightControlEvent":
                                if (_inFight && FightControlReader.Read(hit.Payload, cat.FieldNumber) is { } control
                                    && control.MasterId == _selfId)
                                    _controlledActor = control.ActorId == _selfId ? 0 : control.ActorId;
                                break;
                            case "FightFreeControlEvent":
                                if (hit.Payload.Any(f => f.Wire == WireType.Varint && f.AsSignedVarint == _selfId)
                                    && hit.Payload.Any(f => f.Wire == WireType.Varint && f.AsSignedVarint == _controlledActor))
                                    _controlledActor = 0;
                                break;
                            case "ChallengeProposalEvent":
                                // Le vrai client sélectionne LUI-MÊME le 1er défi proposé, 1-47 ms après la proposition (10/10 combats,
                                // médiane 3 ms) ; le moteur ne fait plus que valider (ou choisir un autre défi 60 ms plus tard, script).
                                if (_inFight)
                                {
                                    long chId = 0;
                                    int propF = FN("ChallengeProposalEvent", "challenge_proposals"), chIdF = FN("Challenge", "challenge_id");
                                    foreach (var f in hit.Payload ?? new())
                                        if (f.Field == propF && f.Message is { } chm) { chId = PV(chm, chIdF); break; }
                                    if (chId > 0)
                                        Later(Random.Shared.Next(100) < 70 ? Random.Shared.Next(1, 6) : Random.Shared.Next(10, 48), "sélection du 1er défi",
                                              () => SendGame("ChallengeSelectionRequest", VN("ChallengeSelectionRequest", "challenge_id", chId)));
                                }
                                break;

                            // Séquences d'action : le serveur les encadre SequenceStart/SequenceEnd (imbriquées : un sort ouvre
                            // une séquence qui contient des sous-séquences). Quand la plus EXTERNE se ferme, le vrai client accuse
                            // GameActionAcknowledgementRequest{valid, action_id} — mais UNIQUEMENT les SIENNES (297/297 accusées,
                            // TRIGGERED compris ; 0/242 pour les monstres ; la simulation de cette règle sur la capture reproduit
                            // 180/180 accusés réels dans l'ordre), APRÈS l'animation (cf AnimMs).
                            case "SequenceStartEvent":
                                if (!_inFight) break;
                                if (_seqOpen == 0)   // nouvelle séquence EXTERNE
                                {
                                    _seqMoveSteps = 0;
                                    _outerAuthor = PV(hit.Payload, FN("SequenceStartEvent", "author_id"));
                                }
                                _seqOpen++;
                                break;
                            case "MapMovementEvent":
                                if (Volatile.Read(ref _reviewDiagnosticMode) == 1 &&
                                    Volatile.Read(ref _reviewMoveState) == 1 && _selfId > 0 &&
                                    MapMovementReader.Read(hit.Payload,
                                        FN("MapMovementEvent", "cells"),
                                        FN("MapMovementEvent", "character_id")) is { } ownMove &&
                                    ownMove.ActorId == _selfId)
                                {
                                    Interlocked.CompareExchange(ref _reviewMoveState, 2, 1);
                                    SocketLog.Info(_logPid, "[jeu] essai déplacement : écho de NOTRE acteur, " +
                                        "confirmation d'arrivée encore ABSENTE (pas ACK)");
                                }
                                // Déplacement de l'AUTEUR de la séquence en cours (nous ou un autre) : sa longueur fixe la durée
                                // d'animation que le client joue (nous : ~139 ms par pas, 1 pas 139-150, 2 pas ~280, 4 pas 552-565 ms).
                                if (_inFight && _seqOpen > 0
                                    && MapMovementReader.Read(hit.Payload, FN("MapMovementEvent", "cells"), FN("MapMovementEvent", "character_id")) is { } mv
                                    && mv.ActorId == _outerAuthor)
                                    _seqMoveSteps = Math.Max(0, MapPathfinder.ExpandPath(mv.Cells).Count - 1);
                                break;
                            case "MapMovementConfirmResponse":
                                if (Volatile.Read(ref _reviewDiagnosticMode) == 1 &&
                                    Interlocked.CompareExchange(ref _reviewMoveState, 4, 3) == 3)
                                {
                                    Interlocked.Increment(ref _reviewMoveResponses);
                                    SocketLog.Info(_logPid, "[jeu] essai déplacement : réponse de confirmation vue " +
                                        "(corrélation UID non prouvée ; un autre mouvement diagnostique autorisé)");
                                }
                                break;
                            case "MapMovementRefusedEvent":
                                if (Volatile.Read(ref _reviewDiagnosticMode) == 1 &&
                                    Volatile.Read(ref _reviewMoveState) is 1 or 2 or 3)
                                {
                                    Interlocked.Exchange(ref _reviewMoveState, -1);
                                    Volatile.Write(ref _injectionReady, 0);
                                    SocketLog.Info(_logPid, "[jeu] ⚠ essai déplacement REFUSÉ par le serveur ; pilotage verrouillé");
                                }
                                break;
                            case "SequenceEndEvent":
                                if (!_inFight) break;
                                _seqOpen = Math.Max(0, _seqOpen - 1);
                                if (_seqOpen > 0) break;
                                long actionId = PV(hit.Payload, FN("SequenceEndEvent", "action_id"));
                                long author = PV(hit.Payload, FN("SequenceEndEvent", "author_id"));
                                long seqType = PV(hit.Payload, FN("SequenceEndEvent", "sequence_type"));
                                if (_selfId != 0 && (author == _selfId || (author == _controlledActor && _turnOwner == _controlledActor)))
                                {
                                    if (actionId != 0) ScheduleOwnAck(actionId, seqType);
                                    // Turbo agressif : « tour prêt » anticipé juste après l'accusé de notre séquence de début de combat (type 8).
                                    if (TurboFightPolicy.ShouldSendEarlyReady(p.TurboEagerReady?.Invoke() == true, author, _selfId, seqType, _noTurnYet, _earlyReadySent))
                                    {
                                        _earlyReadySent = true;
                                        int earlyDelay = _adaptiveReady.GetInitialDelayMs();
                                        _adaptiveReady.NoteReadySent(Environment.TickCount64 + earlyDelay);
                                        React("FightTurnReadyRequest", VN("FightTurnReadyRequest", "is_ready", 1), earlyDelay, isAck: false,
                                              "tour prêt ANTICIPÉ (turbo agressif, après l'accusé de début de combat) — avant la demande du serveur");
                                    }
                                    if (seqType == 7) _turnEndSeen = true;   // fin de NOTRE tour (≠ coup de fin de combat)
                                    // APRÈS la planification de l'accusé : l'action suivante du moteur voit d'abord « accusé en
                                    // attente » puis « écho reçu » (jamais l'inverse, sinon elle passerait entre les deux).
                                    Interlocked.Increment(ref _selfSeqEnds);
                                }
                                else if (author != 0)
                                {
                                    // Séquence d'un AUTRE combattant : jamais accusée (0/242 chez le vrai client), mais le client JOUE son
                                    // animation -> elle repousse le « tour prêt » qui suivra (file d'animations commune).
                                    long nowO = Environment.TickCount64;
                                    if (TurboFightPolicy.ShouldAckForeignSequence(p.TurboAckAll?.Invoke() == true, author, _selfId, actionId))
                                    {
                                        // « Accuser les actions de tous les combattants » : accusé régulé avec gigue pour éviter le burst serveur
                                        int delayForeign = _adaptiveReady.NextForeignAckDelayMs(nowO);
                                        // Rafale au-dela de la cadence soutenue : l'accuse est abandonne (le vrai client
                                        // n'en envoie aucun) plutot que de repartir en burst et declencher le ban.
                                        if (delayForeign < 0)
                                            _ackChainDue = Math.Max(nowO, _ackChainDue) + AnimOtherMs(seqType, _seqMoveSteps, true);
                                        else
                                        React("GameActionAcknowledgementRequest", new List<ProtoField>
                                        {
                                            VF(FN("GameActionAcknowledgementRequest", "valid"), 1),
                                            VF(FN("GameActionAcknowledgementRequest", "action_id"), actionId),
                                        }, delayForeign, isAck: true, $"accusé de la séquence de {author} (turbo tous combattants, action {actionId}, type {seqType}, +{delayForeign} ms)");
                                    }
                                    else _ackChainDue = Math.Max(nowO, _ackChainDue) + AnimOtherMs(seqType, _seqMoveSteps, p.TurboAckAll?.Invoke() == true);
                                    _otherSeqSeen = true;
                                    _otherShape.Add(seqType == 4 && _seqMoveSteps > 0 ? $"4:{_seqMoveSteps}p" : seqType.ToString());
                                }
                                _seqMoveSteps = 0;
                                if (_turnReadyPending) { _turnReadyPending = false; ScheduleTurnReady(_turnReadyFor, "demande différée, séquences closes"); }
                                break;

                            // « Tu as fini tes animations ? » : le serveur attend notre FightTurnReadyRequest avant de donner la main
                            // (sinon TIMEOUT ~15 s avant CHACUN de nos tours). Séquence encore ouverte -> réponse à sa fermeture.
                            case "FightIsTurnReadyEvent":
                                if (!_inFight)
                                {
                                    SocketLog.Info(_logPid, "[jeu] ⚔ FightIsTurnReadyEvent ignoré : combat déjà terminé");
                                    break;
                                }
                                long who = PV(hit.Payload, FN("FightIsTurnReadyEvent", "character_id"));
                                if (_seqOpen > 0) { _turnReadyPending = true; _turnReadyFor = who; SocketLog.Info(_logPid, $"[jeu] ⚔ tour prêt demandé — différé ({_seqOpen} séquence(s) ouverte(s))"); }
                                else ScheduleTurnReady(who, "demande serveur");
                                break;

                            case "TextInformationEvent":
                            {
                                long msgId = PV(hit.Payload, FN("TextInformationEvent", "message_id"));
                                if (TurboFightPolicy.IsRejectionCode((int)msgId))
                                {
                                    _adaptiveReady.NoteRejection((int)msgId, Environment.TickCount64);
                                    SocketLog.Info(_logPid, $"[jeu] ⚠ Rejet serveur reçu (message={msgId}) -> backoff ready niveau={_adaptiveReady.BackoffLevel}");
                                }
                                break;
                            }

                            // ================= FENÊTRES : temps de lecture humain avant la 1re action du moteur =================
                            case "TeleportDestinationsEvent":
                                // Liste de zaaps / havre-sac : le vrai client choisit ~1,5 s après (le moteur : ~65 ms, 42/42).
                                if (p.TurboAckAll?.Invoke() != true)
                                    SetThink(Math.Clamp((int)(1500 * Math.Exp(0.25 * NextGaussian())), 900, 2500), "liste de destinations");
                                break;
                            case "ExchangeBidBuyerStartedEvent":
                            case "ExchangeBidSellerStartedEvent":
                            case "ExchangeCraftStartedEvent":
                                SetThink(Random.Shared.Next(400, 1201), "fenêtre d'échange");   // le moteur agissait en 0-1 ms (7/7)
                                break;
                            // Banque, échange joueur, boutique PNJ : mêmes fenêtres côté client (lecture du contenu avant le 1er
                            // dépôt / achat). Fenêtre d'interaction de l'app ou script : la 1re action attend comme un joueur.
                            case "StorageInventoryContentEvent":
                            case "ExchangeStartedWithStorageEvent":
                            case "ExchangeStartedWithMultiTabStorageEvent":
                            case "ExchangeStartedWithPodsEvent":
                            case "ExchangeNpcShopStartedEvent":
                                SetThink(Random.Shared.Next(500, 1301), "fenêtre de banque / d'échange / de boutique");
                                break;

                            // ================= MESSAGES SERVEUR RARES (jamais capturés : prudence, journalisation) =================
                            case "ExchangeBidHouseOfflineSoldItemsEvent":
                                // Ventes HDV faites hors ligne : le vrai client consulte ses activités HDV ~5,9 s après (n=1, INFÉRÉ).
                                if (!_bidActivitiesSent && hit.Payload?.Any(f => f.Field == FN("ExchangeBidHouseOfflineSoldItemsEvent", "bid_house_items")) == true)
                                {
                                    _bidActivitiesSent = true;
                                    Later(Random.Shared.Next(5700, 6101), "activités HDV", () => SendGame("BidActivitiesRequest"));
                                }
                                break;
                            case "SystemMessageDisplayEvent":
                            {
                                // Message système (kick de modération, maintenance…). hang_up : le client se DÉCONNECTE et ne revient pas
                                // tout seul -> fin de session sans reconnexion automatique.
                                long msgId = PV(hit.Payload, FN("SystemMessageDisplayEvent", "message_id"));
                                var pars = string.Join(" | ", (hit.Payload ?? new()).Where(f => f.Field == FN("SystemMessageDisplayEvent", "parameters") && f.String is not null).Select(f => f.String));
                                bool hang = PV(hit.Payload, FN("SystemMessageDisplayEvent", "hang_up")) != 0;
                                SocketLog.Info(_logPid, $"⚠ [jeu] MESSAGE SYSTÈME id={msgId} [{pars}]{(hang ? " — DÉCONNEXION IMPOSÉE" : "")}");
                                if (hang)
                                {
                                    ServerHangUp = $"message système {msgId}" + (pars.Length > 0 ? $" ({pars})" : "");
                                    _endReason = "déconnexion imposée par le serveur (" + ServerHangUp + ")";
                                    Later(Random.Shared.Next(100, 401), "déconnexion imposée", () => { try { session.Cancel(); } catch { } return Task.CompletedTask; });
                                }
                                break;
                            }
                            case "ReconnectToGameServerEvent":
                                // Le serveur demande de passer sur un autre serveur de jeu : on ne sait pas suivre ce transfert (jamais
                                // capturé) -> fin de session, la boucle de reconnexion refait une connexion complète.
                                // Contenu NON journalisé : il porte le jeton de connexion au nouveau serveur (identifiant de session).
                                SocketLog.Info(_logPid, "⚠ [jeu] redirection vers un autre serveur de jeu demandée");
                                _endReason = "redirection serveur demandée → reconnexion complète";
                                try { session.Cancel(); } catch { }
                                break;
                            case "ReloginTokenEvent":
                                SocketLog.Info(_logPid, "[jeu] jeton de reconnexion reçu (non utilisé)");
                                break;
                            case "PopupWarningEvent":
                                // Popup de MODÉRATEUR : le vrai client l'affiche et ne répond rien tant que le joueur ne la ferme pas.
                                // Aucune fermeture automatique (charge jamais capturée) ; le moteur alerte l'utilisateur.
                                SocketLog.Info(_logPid, "⚠⚠ [jeu] POPUP DE MODÉRATEUR : " +
                                                        PayloadText(hit.Payload));
                                break;
                            case "FileCheckEvent":
                                // Contrôle d'intégrité des fichiers du client : réponse du vrai client jamais capturée -> AUCUNE réponse
                                // inventée (une empreinte fausse serait pire que le silence), alerte bien visible.
                                SocketLog.Info(_logPid, "⚠⚠ [jeu] CONTRÔLE DE FICHIERS DU CLIENT demandé par le serveur (non répondu) : " +
                                                        PayloadText(hit.Payload));
                                break;

                            // ================= VÉRIFICATION SCHNORR (en parallèle du pilotage de session) =================
                            case "ServerSessionReadyEvent":
                                // Vrai client (4/4) : CharacterListRequest 3-7 ms après cette trame, PUIS ClientIdRequest 1-4 ms plus tard.
                                SocketLog.Schnorr(_logPid, "session prête → liste des persos puis ClientIdRequest (engagement G^x mod P)");
                                string clientId = schnorr.ClientId();
                                Later(Random.Shared.Next(3, 8), "liste des persos + ClientId", async () =>
                                {
                                    if (TakeCharList())
                                    {
                                        await SendGame("CharacterListRequest").ConfigureAwait(false);
                                        await Task.Delay(Random.Shared.Next(1, 5), tk).ConfigureAwait(false);
                                    }
                                    await SendGame("ClientIdRequest", SN("ClientIdRequest", "id", clientId)).ConfigureAwait(false);
                                });
                                break;
                            case "ServerVerificationEvent":
                                // Re-vérification périodique (toutes les ~11-24 min observées). Capture passive du trafic RÉEL
                                // de l'original (2026-10-17, tools/MZP23CaptureReplay sur capture_long_20261017.pcapng) : le
                                // client répond en relançant EXACTEMENT le même cycle Schnorr qu'à la connexion -> un NOUVEL
                                // engagement ClientChallengeInitRequest{challenge_key = G^cvlh mod P} (231 octets observés,
                                // cohérent avec SocketSchnorr.ChallengeInit), puis ServerChallengeEvent -> ClientChallengeProofRequest
                                // comme déjà géré ci-dessous. ServerVerificationEvent lui-même est vide (aucun champ observé) :
                                // aucune preuve à en extraire, donc rien à deviner ici.
                                SocketLog.Schnorr(_logPid, "re-vérification serveur → nouveau ClientChallengeInitRequest (G^r mod P)");
                                await SendGame("ClientChallengeInitRequest", SN("ClientChallengeInitRequest", "challenge_key", schnorr.ChallengeInit())).ConfigureAwait(false);
                                break;
                            case "ServerChallengeEvent":
                                int valF = FN("ServerChallengeEvent", "value");
                                var val = hit.Payload?.FirstOrDefault(f => f.Field == valF)?.String;
                                var shown = string.IsNullOrEmpty(val) ? "vide → Fiat-Shamir" : (val.Length > 14 ? val[..14] + "…" : val);
                                SocketLog.Schnorr(_logPid, $"défi reçu (value={shown}) → ClientChallengeProofRequest");
                                await SendGame("ClientChallengeProofRequest", SN("ClientChallengeProofRequest", "proof", schnorr.ChallengeProof(val, p.DeviceId))).ConfigureAwait(false);
                                SocketLog.Info(_logPid, "✔ [jeu] preuve Schnorr envoyée.");
                                break;
                        }
                    }
                    // Le moteur (même décodeur que le MITM) voit la trame APRÈS notre réaction protocolaire.
                    PublishObserved(p, false, Frame(frame));
                    if (tk.IsCancellationRequested) return;
                    p.FeedFrame?.Invoke(_logPid, false, Frame(frame));
                }
            }

            // Deliberately located after the server event handler: no readiness
            // exists just because a TCP transport was established. Only explicit
            // CharacterSelectionEvent + gameplay context can call this helper.
            void PublishGameplayInjectorIfReady(string eventName)
            {
                if (!_selSuccess || !gameContextSeen || !(exactGameplayMapping || reviewedMoveOnly) ||
                    (p.RegisterInjector is null && p.RegisterInjectorWithAdmission is null) ||
                    Volatile.Read(ref _injectorPublished) != 0)
                    return;
                if (p.RegisterInjectorWithAdmission is { } register)
                {
                    if (!register(_logPid, Inject)) return;
                }
                else p.RegisterInjector!(_logPid, Inject);
                lock (_sourceGate)
                {
                    if (_currentSource is null || _sessionCancellation is not
                        { IsCancellationRequested: false } ||
                        Volatile.Read(ref _superseded) != 0) return;
                }
                Volatile.Write(ref _injectorPublished, 1);
                Volatile.Write(ref _injectionReady, 1);
                SocketLog.Info(_logPid, "[jeu] pilotage : injecteur publié après sélection + contexte serveur (" +
                    eventName + "); mise en file ≠ accusé serveur");
            }

            // Anti-AFK (mode Socket SEULEMENT) : en MITM l'original s'en remet à GivreMods, un service d'inactivité CÔTÉ
            // CLIENT (AppConfig.cs : « aucun keep-alive emote côté GivreClone, choix user »=délégué au mod) — rien d'équivalent
            // ici puisqu'il n'y a pas de vrai client Dofus.exe à instrumenter. Capture passive propre (2026-10-17,
            // tools/MZP23CaptureReplay sur capture_long_20261017.pcapng) : la ré-vérification Schnorr périodique
            // (ServerVerificationEvent) est gérée et RÉUSSIT deux fois d'affilée avant la coupure -> ce n'est PAS la cause.
            // Le personnage reste alors totalement immobile (aucune action envoyée) ~35 min, puis le serveur ferme lui-même
            // la session (SystemMessageDisplayEvent{hang_up=true, message_id=1} puis fermeture TCP 12 ms après, sans délai
            // de grâce côté serveur) : coupure anti-AFK du jeu. Demande explicite de l'utilisateur : AUCUNE action visible
            // (ni émote, ni mouvement) — on répète seulement le PingRequest{quiet} déjà envoyé UNE fois au login (ligne
            // ~353 ci-dessus), un pur ping réseau sans effet en jeu. Test MITM 2026-10-17 (capture_mitm_afk_20261017b.pcapng,
            // compte Xyla-Taz) : session restée vivante 55+ min sans aucune coupure ni SystemMessageDisplayEvent.
            void AntiAfkLoop() => _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(10), tk).ConfigureAwait(false);
                        await SendGame("PingRequest", VN("PingRequest", "quiet", 1), uid: 1).ConfigureAwait(false);
                        SocketLog.Info(_logPid, "[jeu] anti-AFK : PingRequest répété (aucune action visible)");
                    }
                }
                catch (OperationCanceledException) { /* fin de session */ }
                catch (Exception e) { SocketLog.Info(_logPid, $"[jeu] anti-AFK NON envoyé : {e.GetType().Name}"); }
            });

            // Initialisation du 1er contexte de jeu (vrai client, 4/4 sessions MITM) : 2-65 ms après ContextCreationEvent,
            // jeton BAK + alertes de guilde, canal communautaire, étapes des quêtes suivies (2x), prix moyens, listes sociales.
            // SubscribeMultipleChannelRequest n'est PAS reproduit : son contenu (canaux) dépend de la config locale du perso et
            // n'est pas lisible dans les captures -> mieux vaut l'absence qu'un contenu faux (la capture de découverte MITM
            // journalise désormais l'hex des trames sortantes pour le caler).
            void LoginContextBurst() => Later(Random.Shared.Next(2, 66), "initialisation du contexte", async () =>
            {
                await SendGame("BakApiTokenRequest").ConfigureAwait(false);
                await SendGame("GuildMemberWarnOnConnectionStartRequest").ConfigureAwait(false);
                await Task.Delay(Random.Shared.Next(27, 39), tk).ConfigureAwait(false);
                await SendGame("SetCommunityChannelRequest").ConfigureAwait(false);
                var quests = _questIds.ToArray();
                if (quests.Length > 0)
                {
                    await Task.Delay(Random.Shared.Next(140, 171), tk).ConfigureAwait(false);
                    for (int pass = 0; pass < 2; pass++)
                        foreach (var q in quests)
                            await SendGame("QuestStepInformationRequest", VN("QuestStepInformationRequest", "quest_id", q)).ConfigureAwait(false);
                    await Task.Delay(Random.Shared.Next(20, 31), tk).ConfigureAwait(false);
                }
                else await Task.Delay(Random.Shared.Next(55, 71), tk).ConfigureAwait(false);   // sans quête suivie : ~63 ms (n=1)
                await SendGame("ObjectAveragePricesRequest").ConfigureAwait(false);
                await Task.Delay(quests.Length > 0 ? Random.Shared.Next(55, 71) : Random.Shared.Next(0, 3), tk).ConfigureAwait(false);
                await SendGame("FriendListRequest").ConfigureAwait(false);
                await SendGame("AcquaintanceListRequest").ConfigureAwait(false);
                await SendGame("SpouseInformationRequest").ConfigureAwait(false);
                await SendGame("GuildInformationRequest", VN("GuildInformationRequest", "information_type", 3)).ConfigureAwait(false);
            });
        }
        catch (OperationCanceledException) { SocketLog.Info(_logPid, "[jeu] " + (_endReason ?? "session arrêtée (Stop)")); }
        catch (Exception e) { SocketLog.Info(_logPid, $"[jeu] ERREUR {e.GetType().Name} : {e.Message}"); }
        finally
        {
            // Revoke the exact socket link before notifying any cleanup
            // consumer; a delayed callback/old PID can no longer appear live.
            lock (_sourceGate)
            {
                _currentSource = null;
                _sessionCancellation = null;
            }
            lock (_observationGate) _observations.Clear();
            Volatile.Write(ref _injectionReady, 0);
            Volatile.Write(ref _injectorPublished, 0);
            Volatile.Write(ref _reviewDiagnosticMode, 0);
            p.SelectionGate?.EndSession();
            MappingCatalog.Cleared -= onPurge;
            try { session.Cancel(); } catch { }        // arrête ping / réactions / file d'injection de CETTE session
            try { p.UnregisterInjector?.Invoke(_logPid); }
            catch (Exception e)
            {
                // A reentrant/throwing observer must not prevent the queues
                // from completing or the actual TCP stream from closing.
                SocketLog.Info(_logPid, "[jeu] désinscription injecteur impossible (" +
                    e.GetType().Name + ") ; fermeture locale maintenue");
            }
            _outbound.Writer.TryComplete();          // arrête la tâche de drainage
            _reactions.Writer.TryComplete();
            if (stream is not null)
            {
                // Fermeture = FIN TCP, EXACTEMENT comme le vrai client et un bot tiers (Client.Disconnect): il n'existe AUCUN
                // message de déconnexion dans le protocole D3. ⚠ JAMAIS ContextQuitRequest ici : c'est l'ABANDON DE COMBAT
                // -> une Déconnexion en plein combat le faisait PERDRE. Un FIN qui ATTEINT le serveur
                // retire le perso en ~90 ms (constaté : GameContextRemoveElement vu par un autre compte) ; le « fantôme »
                // (perso resté en jeu, login suivant rejeté) venait du chemin réseau (port 443 via le saut proxy loopback),
                // d'où le bind-direct + le port 5555 du vrai client (SocketDialer / SocketProbe). En plein COMBAT, le serveur
                // garde le perso jusqu'à la fin du combat quoi qu'il arrive.
                var how = SocketDialer.Close(stream);
                SocketLog.Info(_logPid, $"[jeu] connexion fermée : {how}");
            }
            // _writeLock n'est PAS disposé : une tâche de la session encore en vol l'aurait touché après coup
            // (ObjectDisposedException constatée sur l'ancien keep-alive) ; un SemaphoreSlim sans handle n'a rien à libérer.
        }
    }

    // Durée d'animation que le vrai client joue avant d'accuser une séquence qui LUI appartient, mesurée à partir de
    // max(arrivée de la séquence, accusé précédent) sur la capture MITM (le client enchaîne ses animations ; 180/180 accusés)
    // — enum SequenceType : Spell0 Weapon1 Glyph2 Triggered3 Move4 Death5 TurnStart6 TurnEnd7 FightStart8 :
    //  - sort / arme : 243-292 ms (80 %), 327-443 ms (20 %) — y compris un kill intermédiaire (~250-265 ms) ; seul le coup
    //    qui TERMINE le combat attend ~1,5 s, recalé après coup (cf ScheduleTurnReady) ;
    //  - déplacement : ~139 ms par pas ; fin de tour : 0-2 ms ; déclenché : 0-11 ms ; début de combat : 14-18 ms ;
    //    le reste : 0-20 ms.
    private static int AnimMs(long sequenceType, int moveSteps)
    {
        var r = Random.Shared;
        return sequenceType switch
        {
            0 or 1 => r.Next(100) < 80 ? r.Next(243, 293) : r.Next(327, 444),
            4 => moveSteps > 0 ? moveSteps * 139 + r.Next(0, 12) : r.Next(139, 151),
            7 => r.Next(0, 3),
            3 => r.Next(0, 12),
            8 => r.Next(14, 36),
            _ => r.Next(0, 21),
        };
    }

    // Animation d'une séquence d'un AUTRE combattant, que le client joue avant de se dire prêt pour le tour suivant (INFÉRÉ,
    // calé sur UNE espèce de monstre en 1v1 : formes [sort,sort,sort,déplacement,fin] 2366-2444 ms et
    // [début,sort,sort,déplacement,fin] 1821-1858 ms) : sort ~530-560 ms, déplacement ≥ ~500 ms (~140 ms/pas au-delà), le reste ~0.
    private static int AnimOtherMs(long sequenceType, int moveSteps, bool turbo = false)
    {
        if (turbo) return Random.Shared.Next(0, 15);
        var r = Random.Shared;
        return sequenceType switch
        {
            0 or 1 => r.Next(530, 561),
            4 => Math.Max(r.Next(490, 531), moveSteps * 140 + r.Next(25, 46)),
            _ => r.Next(0, 6),
        };
    }

    // Tirage gaussien (Box-Muller) pour les temps de réaction humains (log-normaux).
    private static double NextGaussian()
    {
        double u1 = 1.0 - Random.Shared.NextDouble(), u2 = Random.Shared.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    // Liste des persos (CharacterListEvent, code ksw — « CharacterInformation » dans le mapping GivreClone) :
    // { characters: [ Character { character_basic_information { level, name… }, id } ] }, champs résolus PAR NOM. Rend le
    // perso demandé (nom) s'il est présent, sinon le premier.
    /// <summary>Id du personnage portant EXACTEMENT ce nom (insensible à la casse) ; 0 s'il n'y en a pas — jamais de repli sur un autre personnage.</summary>
    private static long FindCharacterId(List<ProtoField>? payload, string name, Func<string, string, int> fn)
    {
        if (payload is null || string.IsNullOrWhiteSpace(name)) return 0;
        int chars = fn("CharacterListEvent", "characters"), idF = fn("Character", "id"),
            basicF = fn("Character", "character_basic_information"), nameF = fn("CharacterBasicInformation", "name");
        foreach (var f in payload)
        {
            if (f.Field != chars || f.Message is not { } ch) continue;
            string? nm = ch.FirstOrDefault(g => g.Field == basicF)?.Message?.FirstOrDefault(g => g.Field == nameF)?.String;
            if (string.Equals(nm, name, StringComparison.OrdinalIgnoreCase) && ch.FirstOrDefault(g => g.Field == idF && g.Wire == WireType.Varint) is { } gi) return (long)gi.Varint;
        }
        return 0;
    }

    private static (long Id, string? Name, int Level) PickCharacter(List<ProtoField>? payload, string? preferred, Func<string, string, int> fn)
    {
        if (payload is null) return (0, null, 0);
        int chars = fn("CharacterListEvent", "characters"), idF = fn("Character", "id"),
            basicF = fn("Character", "character_basic_information"), nameF = fn("CharacterBasicInformation", "name"),
            levelF = fn("CharacterBasicInformation", "level");
        (long, string?, int) first = (0, null, 0);
        foreach (var f in payload)
        {
            if (f.Field != chars || f.Message is not { } ch) continue;
            long id = ch.FirstOrDefault(g => g.Field == idF && g.Wire == WireType.Varint) is { } gi ? (long)gi.Varint : 0;
            if (id <= 0) continue;
            var basic = ch.FirstOrDefault(g => g.Field == basicF)?.Message;
            string? nm = basic?.FirstOrDefault(g => g.Field == nameF)?.String;
            int lv = basic?.FirstOrDefault(g => g.Field == levelF && g.Wire == WireType.Varint) is { } gl ? (int)gl.Varint : 0;
            if (first.Item1 == 0) first = (id, nm, lv);
            if (!string.IsNullOrEmpty(preferred) && string.Equals(nm, preferred, StringComparison.OrdinalIgnoreCase)) return (id, nm, lv);
        }
        return string.IsNullOrEmpty(preferred) ? first : (0, null, 0);   // nom demandé absent : jamais un autre personnage
    }

    // Contenu d'un message serveur pour le journal : arbre brut (numéros de champ) en licence DÉVELOPPEUR ; sinon seulement
    // les TEXTES décodés (ex. le message d'une popup de modérateur) — « (vide) » uniquement si le message est réellement vide.
    private static string PayloadText(List<ProtoField>? payload)
    {
        if (payload is not { Count: > 0 }) return "(vide)";
        if (LicenseService.IsDeveloper) return RawProtobuf.ToJson(payload).Replace("\r", "").Replace("\n", " ");
        var texts = new List<string>();
        void Walk(IReadOnlyList<ProtoField> fields)
        {
            foreach (var f in fields)
            {
                if (f.Wire != WireType.Len) continue;
                // Le décodeur brut ne reconnaît que l'ASCII comme texte : un texte accentué (« Vous avez été averti… ») reste en
                // octets, et un texte court peut même être pris pour un sous-message. UTF-8 strict imprimable = texte.
                if (!string.IsNullOrWhiteSpace(f.String)) texts.Add(f.String!.Trim());
                else if (Utf8Text(f.Bytes) is { } s) texts.Add(s);
                else if (f.Message is { Count: > 0 } m) Walk(m);
            }
        }
        Walk(payload);
        return texts.Count > 0 ? "« " + string.Join(" · ", texts) + " »" : "(détail réservé au diagnostic)";
    }

    private static readonly System.Text.UTF8Encoding StrictUtf8 = new(false, true);
    private static string? Utf8Text(byte[]? b)
    {
        if (b is not { Length: > 0 }) return null;
        string s;
        try { s = StrictUtf8.GetString(b); } catch (ArgumentException) { return null; }
        foreach (char c in s) if (char.IsControl(c) && c is not ('\n' or '\r' or '\t')) return null;
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static byte[] Frame(byte[] body)
    {
        var len = RawProtobuf.EncodeVarint((ulong)body.Length);
        var fr = new byte[len.Length + body.Length];
        Buffer.BlockCopy(len, 0, fr, 0, len.Length);
        Buffer.BlockCopy(body, 0, fr, len.Length, body.Length);
        return fr;
    }

    private static bool TryDeframe(List<byte> acc, out byte[] body)
    {
        body = Array.Empty<byte>();
        int len = 0, shift = 0, i = 0;
        while (true)
        {
            if (i >= acc.Count) return false;
            byte b = acc[i++];
            len |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift > 35) { acc.Clear(); return false; }
        }
        if (acc.Count - i < len) return false;
        body = new byte[len];
        acc.CopyTo(i, body, 0, len);
        acc.RemoveRange(0, i + len);
        return true;
    }
}
