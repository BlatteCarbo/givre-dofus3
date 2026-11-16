// Client protocole Givre/Ikuware (PROTOCOL.md) — porte depuis clients/csharp/LicenseClient.cs.
// Points de securite NON negociables :
//   1. cle publique Ed25519 EMBARQUEE dans le binaire ;
//   2. signature + nonce + server_time verifies sur CHAQUE reponse (succes ET erreur) ;
//   3. arret apres 2 echecs de heartbeat consecutifs (ou immediat sur code terminal).
//
// Adaptations GivreClone vs reference :
//   - verif Ed25519 via Cryptographic.Ed25519 (pur BigInteger, AOT-safe) au lieu de BouncyCastle ;
//   - corps de requete/frames ecrits a la main (Utf8JsonWriter) au lieu de JsonSerializer(reflection) => AOT ;
//   - heartbeat temps-reel par WebSocket (kill serveur INSTANTANE : "fermer la session" cote panel coupe
//     l'app tout de suite) + repli HTTP 60 s si le socket ne tient pas.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.License;

/// <summary>Erreur metier signee du serveur (INVALID_KEY, HWID_MISMATCH, KEY_PAUSED, USER_BANNED…).
/// Pour USER_BANNED/KEY_REVOKED/KEY_PAUSED, <see cref="Reason"/> porte la raison saisie par l'admin
/// (destinee a etre montree a l'utilisateur).</summary>
public sealed class LicenseServerException : Exception
{
    public string Code { get; }
    public string? Reason { get; }
    /// <summary>Delai (ms) aleatoire demande par le serveur avant l'arret, sur erreur terminale. Anti-tamper.</summary>
    public long? TerminateInMs { get; init; }
    /// <summary>SESSION_EXPIRED marqué <c>lapsed: true</c> (booléen JSON STRICT) : la session a juste dépassé sa grâce (coupure,
    /// veille) et PEUT être reprise par un nouveau /auth. Absent = fin voulue ou non reprenable (écran de clé).</summary>
    public bool Lapsed { get; init; }
    /// <summary>SIGNATURE_REJECTED « drift » une 2e fois après recalage : l'horloge du PC est fausse (message dédié).</summary>
    public bool ClockWrong { get; init; }

    public LicenseServerException(string code, string message, string? reason = null)
        : base($"{code}: {message}")
    {
        Code = code;
        Reason = reason;
    }
}

/// <summary>Reponse illisible ou inauthentique (reseau, base64, signature, nonce, horloge).
/// Doit etre traitee comme un echec, jamais comme un succes.</summary>
public sealed class EnvelopeException : Exception
{
    public EnvelopeException(string message) : base(message) { }
}

/// <summary>Un droit et sa date de peremption (epoch ms, ou null s'il suit l'expiration de la licence).</summary>
public sealed record PermissionInfo(string Name, long? ExpiresAtMs);

/// <summary>Push WS "control_kill" (launcher control) : kill global et/ou version imposée + message opérateur.</summary>
public sealed record ControlKillInfo(bool Kill, bool Outdated, string? MinVersion, string? CurrentVersion, string? Message);

/// <summary>Commande serveur poussée sur le socket (ex "DISCONNECT_BOT" avec un pid). Signée + server_time comme
/// toute frame WS, mais SANS champ "status". Une commande inconnue est ignorée (jamais un kill de l'app).</summary>
public sealed record ServerCommand(string Name, int? Pid, string? Target = null, string? Content = null, string? Action = null, string? Via = null,
    string? Message = null, string? Script = null, string? Nonce = null);

/// <summary>Ce que la reponse signee /auth dit de la licence. Timestamps en epoch ms ; ExpiresAtMs = 0
/// pour une licence a vie ; LastAuthAtMs = connexion PRECEDENTE (null a la 1ere activation).</summary>
public sealed record LicenseInfo(
    string Status,
    long? ActivatedAtMs,
    long ExpiresAtMs,
    int DurationDays,
    long? LastAuthAtMs,
    string PackageName,
    IReadOnlyList<PermissionInfo> Permissions,
    int? MaxLevel = null,  // cap de niveau réglé par l'admin sur le pack (signé) : null = non réglé, 0 = aucune limite, n = jusqu'au niveau n
    bool? Free = null);  // licence GRATUITE déclarée par le serveur (signé) ; null = le serveur ne le dit pas (ancien serveur)

/// <summary>Etat du socket de heartbeat temps-reel (surface pour l'UI).</summary>
public enum WsConnectionState { Connecting, Connected, Reconnecting, Closed }

/// <summary>Un battement verifie (welcome puis 1 par beat). ValidUntilMs = TTL glissant echo par le serveur.</summary>
public sealed record HeartbeatTick(int Count, long ValidUntilMs, long? LicenseExpiresAtMs);

/// <summary>Bundle de mapping DÉCHIFFRÉ en mémoire : artefacts par type (confirmed/matching/fields/proto[/positions])
/// -> contenu clair. NotModified=true => rien de neuf (réutiliser le cache). Fallback=true => build exact non publié,
/// le plus récent complet a été servi (lire <see cref="Build"/> pour savoir lequel).</summary>
public sealed record MappingBundle(
    string Build,
    int MappingVersion,
    bool Fallback,
    bool NotModified,
    IReadOnlyDictionary<string, string> Artefacts);

/// <summary>Résultat d'un push de script (POST /scripts) : id serveur du script, nom retenu, et numéro de la révision
/// courante après ce push (inchangé si le contenu était identique = aucune nouvelle révision créée côté serveur).</summary>
public sealed record ScriptPushResult(long Id, string Name, int Revision);

/// <summary>
/// Protocole client Givre. Chaque reponse — succes ou erreur — est verifiee :
/// base64 → signature Ed25519 → echo du nonce → tolerance d'horloge 5 min.
/// Toute reponse non conforme est rejetee.
/// </summary>
public sealed class LicenseClient : IDisposable
{
    private const long MaxClockSkewMs = 300_000;    // 5 min
    // Cloudflare (plan gratuit) : une page d'erreur edge (5xx/429/HTML) = EnvelopeException = JAMAIS un kill.
    // On tolère plus de flaps consécutifs avant de finir la session (un vrai code terminal SIGNÉ coupe, lui, tout de suite).
    private const int MaxConsecutiveFailures = 4;
    private const int DefaultHeartbeatIntervalS = 60;

    // Codes metier signes ou reessayer est inutile : licence/session inutilisable => arret immediat.
    private static readonly HashSet<string> TerminalCodes = new()
    {
        "USER_BANNED", "KEY_PAUSED", "KEY_REVOKED", "KEY_EXPIRED",
        "KEY_LOCKED", "INVALID_KEY", "HWID_MISMATCH", "SESSION_EXPIRED",
    };

    public static bool IsTerminal(string code) => TerminalCodes.Contains(code);

    private readonly HttpClient _http;
    private readonly byte[] _publicKey;   // 32 octets bruts
    private readonly string _clientName;
    private readonly string _clientVersion;
    private readonly string _hwid;

    private string? _token;
    private int _heartbeatIntervalS = DefaultHeartbeatIntervalS;

    // Heartbeat temps-reel (WebSocket) : nonces en attente (anti-rejeu) -> n° d'ordre du hello/beat sur le fil, + compteur de battements.
    private readonly Dictionary<string, long> _pendingNonces = new();
    private readonly object _nonceLock = new();
    private int _beatCount;
    // Socket WS courant (uploads hors boucle de session) avec sa GÉNÉRATION (unique dans tout le processus, même d'un client à
    // l'autre) et son compteur d'ordre d'envoi. Le serveur traite les trames d'une socket dans l'ordre, sans parallélisme : la
    // réponse à un beat envoyé APRÈS un upload prouve que l'upload a été reçu (il n'y a pas d'accusé positif d'upload).
    private sealed class WsCtx(ClientWebSocket socket, int gen)
    {
        public ClientWebSocket Socket { get; } = socket;
        public int Gen { get; } = gen;
        public long Seq;   // n° d'ordre sur le fil, incrémenté sous _sendLock (ordre des numéros = ordre d'envoi)
        public volatile bool HelloAcked;
        public volatile bool WatchdogFired;   // socket coupé faute de réponse (hello ou battements), pas une mort par keep-alive
        public long LastReplyTick = Environment.TickCount64;   // dernière réponse VÉRIFIÉE à l'un de nos hello/beat
        // Débit d'envoi de CE socket (le serveur remet son seau à plein à chaque socket) — sous verrou de l'objet.
        public double UploadTokens = UploadBurst;
        public long UploadRefillTick = Environment.TickCount64;
    }
    // Le serveur coupe SANS PRÉVENIR (ni trame ni refus) un socket qui dépasse 10 envois d'avance puis 2/s — et les commandes
    // qu'il venait d'y pousser sont perdues (jamais lues). Le client se bride nettement en dessous ; un envoi retenu n'est pas
    // parti (null) : l'appelant le garde en file (accusés), le retente au tick suivant (bots) ou bascule (pm).
    private const double UploadBurst = 8, UploadRefillPerSec = 1.8;
    private static bool TakeUploadToken(WsCtx ctx)
    {
        lock (ctx)
        {
            long now = Environment.TickCount64;
            ctx.UploadTokens = Math.Min(UploadBurst, ctx.UploadTokens + (now - ctx.UploadRefillTick) / 1000.0 * UploadRefillPerSec);
            ctx.UploadRefillTick = now;
            if (ctx.UploadTokens < 1) return false;
            ctx.UploadTokens -= 1;
            return true;
        }
    }
    private static int _wsGenSeq;
    private volatile WsCtx? _active;
    // Keep-alive avec délai : ping toutes les 15 s, socket déclaré mort sans pong sous 15 s -> une coupure est vue en ≤ 30 s au
    // lieu de 20-80 s (retransmissions TCP), et les envois partis entre-temps dans le vide sont re-tentés plus tôt. Si des sockets
    // tout juste acquittés meurent à répétition sur ce délai (pair qui ne répondrait pas aux pings), on le coupe pour ce client.
    private bool _keepAliveOff;
    private int _keepAliveStrikes;
    private long _lastKaAbortTick;   // fin du socket précédent par keep-alive (0 = il est mort d'autre chose)
    // Envois (uploads) autorisés seulement APRÈS l'acquittement du hello : un upload arrivé avant (course à la (re)connexion)
    // est refusé par le serveur en SESSION_EXPIRED (« upload sans session ») = code terminal -> déconnexion de la licence.
    private volatile bool _uploadsReady;
    // Coupe-circuit : envois refusés à répétition (socket fermé par le serveur juste après un envoi) -> suspendus pour la
    // session, pour ne pas enchaîner les reconnexions. La licence, les kills et les commandes continuent de fonctionner.
    private volatile bool _uploadsSuspended;
    private long _lastUploadTick;
    // Commandes déjà reçues (nonce -> tick) : une commande n'est exécutée et accusée qu'UNE fois (un 2e accusé du même nonce
    // sur le même socket = rejeu pour le serveur). Fenêtre 10 min (= fenêtre d'accusé du serveur).
    private readonly Dictionary<string, long> _seenCommands = new();
    // Mises à jour de licence : lecture-modification-écriture de License sérialisée ; un instantané NON sollicité plus
    // ancien que le dernier appliqué est ignoré (ordre / rejeu d'un vieux push signé).
    private readonly object _licLock = new();
    private long _lastLicenseServerTimeMs;
    private bool _wsConnectedOnce;   // la 1re connexion suit /auth (droits frais) ; les suivantes resynchronisent
    private int _uploadRejects;
    private const int MaxUploadRejects = 3;
    private const long UploadRejectWindowMs = 5_000;
    private readonly SemaphoreSlim _sendLock = new(1, 1);   // ClientWebSocket interdit 2 SendAsync concurrents
    private const int MaxWsReconnects = 6;
    // Délais (propriétés internes : les bancs de test les raccourcissent ; jamais modifiés en production).
    internal static int WsReconnectDelayMs { get; set; } = 2000;   // base du backoff exponentiel (reconnexion WS derrière l'edge Cloudflare)
    // Plafond du backoff : le serveur ne garde une session SANS socket que 15 s (au-delà, le hello répond SESSION_EXPIRED,
    // terminal). Un backoff de 16-30 s faisait expirer la session alors que le réseau était revenu entre-temps.
    internal static int WsReconnectCapMs { get; set; } = 8000;
    internal static TimeSpan WsKeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);
    internal static TimeSpan WsKeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(15);
    // Ouverture du socket (TCP + TLS + upgrade) et réponse au hello BORNÉES : sans délai, un intermédiaire qui accepte la
    // connexion puis se tait figeait toute la boucle de battement (plus de repli HTTP, plus de kick reçu, lien perdu jamais
    // constaté ni levé).
    internal static TimeSpan WsConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);
    internal static TimeSpan WsHelloTimeout { get; set; } = TimeSpan.FromSeconds(20);
    // Battements sans réponse vérifiée depuis heartbeat_interval + ce délai : socket coupé (reconnexion, puis repli HTTP, puis
    // pause des bots). Seule la réponse SIGNÉE à notre nonce prouve le lien : un intermédiaire qui laisse passer le hello puis se
    // tait (ou filtre les trames) gardait sinon la session « vivante » sans contact serveur, kicks jamais reçus.
    internal static TimeSpan WsReplyGrace { get; set; } = TimeSpan.FromSeconds(30);
    // Repli HTTP : on retente le temps réel (une seule tentative) au plus tard toutes les 2 min, et tout de suite quand un
    // battement HTTP repasse après une coupure. Avant, le repli était définitif pour la session.
    internal static TimeSpan FallbackWsRetryAfter { get; set; } = TimeSpan.FromMinutes(2);
    // Refus TRANSITOIRE d'un envoi (not_ready / internal) : le prochain envoi est retardé 1, 2, 4… 30 s (la reconnexion,
    // elle, reste immédiate) -> jamais de boucle serrée reconnexion / hello / envoi refusé si la cause persiste.
    private long _uploadHoldUntil;
    private int _transientRejects;
    // Session ABANDONNÉE (marqueur lapsed) : l'ancien jeton ne sert plus JAMAIS (ni hello, ni bye, ni requête HTTP) et toute
    // réponse qui lui est liée est ignorée. Le client authentifié suivant est un AUTRE LicenseClient (LicenseService).
    private volatile bool _abandoned;
    // Fin de session VOULUE déjà reçue (trame terminale WS / heartbeat HTTP), posée AVANT l'attente terminate_in : pendant ce
    // délai, un marqueur lapsed sur une autre requête ne doit JAMAIS lancer une reprise qui annulerait le kick.
    private LicenseServerException? _terminalError;

    public LicenseInfo? License { get; private set; }
    /// <summary>Cap de niveau de perso porté par la licence (0 = pas de cap serveur). Signé ; consommé par LicenseService.LevelCap.</summary>
    public int? MaxLevel => License?.MaxLevel;
    public int HeartbeatIntervalS => _heartbeatIntervalS;
    public string HardwareId => _hwid;

    /// <summary>true dès que la session HTTP est établie (jeton posé à la FIN d'<see cref="AuthenticateAsync"/>) : les
    /// appels session-gated (ex <see cref="PushScriptAsync"/>) peuvent partir. Permet à LicenseService de DIFFÉRER un
    /// push tant que la session n'est pas prête (course au démarrage) plutôt que de heurter l'InvalidOperationException.</summary>
    public bool IsSessionReady => _token is not null;

    /// <summary>Jeton de la session active (null hors session). Sert d'en-tête Authorization au relais GivreRelay.</summary>
    public string? SessionToken => _token;

    /// <summary>Point d'entrée client du relais GivreRelay : même hôte que l'API, chemin /relay/client (wss en prod).</summary>
    public Uri RelayClientUri
    {
        get
        {
            var b = _http.BaseAddress!;
            return new UriBuilder(b) { Scheme = b.Scheme == "https" ? "wss" : "ws", Path = "/relay/client", Query = "" }.Uri;
        }
    }

    /// <summary>Etat du socket temps-reel a change (thread de fond -> marshaller vers l'UI).</summary>
    public event Action<WsConnectionState>? ConnectionChanged;
    /// <summary>Un battement verifie est arrive (welcome ou beat). Thread de fond.</summary>
    public event Action<HeartbeatTick>? Beat;
    /// <summary>Une frame terminale (kill/revoke/pause/expire/ban) vient d'arriver ; arret imminent (apres terminate_in).</summary>
    public event Action<LicenseServerException>? Terminating;
    /// <summary>La session s'est terminee sur une frame terminale (delai terminate_in deja ecoule). Thread de fond.</summary>
    public event Action<LicenseServerException>? Terminated;
    /// <summary>Réponse à NOTRE requête = SESSION_EXPIRED + lapsed:true (signée, fraîche, nonce vérifié) : session reprenable par
    /// un nouveau /auth. Argument = source (10 WebSocket, sinon n° d'endpoint HTTP Diag.Ep). Thread de fond.</summary>
    public event Action<int>? Lapsed;
    /// <summary>La session est LIÉE à une autre clé cliente que la nôtre (SIGNATURE_REJECTED key_mismatch sur /mapping, ou
    /// UPLOAD_REJECTED key_mismatch sur un envoi WS) : procédure de reprise (nouvel /auth signé). Argument = source (10 WS,
    /// sinon n° d'endpoint HTTP). Thread de fond.</summary>
    public event Action<int>? KeyMismatch;
    /// <summary>Le dernier /auth réussi était SIGNÉ (requêtes signées annoncées par le serveur).</summary>
    public bool SignedAuth { get; private set; }
    /// <summary>session.key_bound du dernier /auth (null = absent : serveur sans la fonction ou /auth en clair).</summary>
    public bool? KeyBound { get; private set; }
    /// <summary>session.key_fingerprint du dernier /auth (empreinte de la clé liée, pour le support), ou null.</summary>
    public string? KeyFingerprint { get; private set; }
    /// <summary>true une fois la session abandonnée (reprise en cours sur un autre client).</summary>
    public bool IsAbandoned => _abandoned;
    /// <summary>Trame terminale déjà reçue sur ce client (fin de session en cours, même pendant terminate_in), sinon null.</summary>
    public LicenseServerException? TerminalError => Volatile.Read(ref _terminalError);
    private void MarkTerminal(LicenseServerException e) => Interlocked.CompareExchange(ref _terminalError, e, null);

    /// <summary>Lâche l'ancien jeton TOUT DE SUITE : ferme son socket, coupe les envois, bloque toute requête et ignore les
    /// réponses qui lui sont liées. Idempotent.</summary>
    public void Abandon()
    {
        _abandoned = true;
        _uploadsReady = false;
        try { _active?.Socket.Abort(); } catch { }
    }

    /// <summary>Génération du socket WS courant (0 = aucun). Unique dans tout le processus : deux sockets (même de deux
    /// clients différents) n'ont jamais la même.</summary>
    public int SocketGen => _active?.Gen ?? 0;

    /// <summary>Un envoi partirait maintenant (socket ouvert, hello acquitté, envois ni suspendus ni retenus).</summary>
    public bool CanUpload => !_abandoned && _active is { } c && c.Socket.State == WebSocketState.Open && _uploadsReady
                             && !_uploadsSuspended && Environment.TickCount64 >= Interlocked.Read(ref _uploadHoldUntil);

    /// <summary>Réponse vérifiée au hello/beat n° <c>seq</c> du socket <c>gen</c> : tout ce qui est parti AVANT sur ce socket a
    /// été traité par le serveur. Thread de fond.</summary>
    public event Action<int, long>? BeatConfirmed;

    // Lien avec le serveur perdu (repli HTTP : ≥ 4 échecs de lien d'affilée) : botting suspendu, licence gardée. Levé dès
    // qu'un battement HTTP repasse ou qu'un hello WS est acquitté. Porté par le client : survit d'un passage du repli à l'autre.
    private volatile bool _linkDown;
    private int _hbLinkFailures, _hbIntegrityFailures;   // échecs d'affilée du repli HTTP (remis à zéro par un battement réussi)
    /// <summary>Lien perdu (true) / revenu (false). Thread de fond.</summary>
    public event Action<bool>? LinkDownChanged;
    public bool IsLinkDown => _linkDown;
    /// <summary>Filet de sécurité (exception imprévue dans la boucle de battement) : botting suspendu jusqu'au prochain contact.</summary>
    internal void MarkLinkDown() => SetLinkDown(true);

    private long _lastServerOkTick = Environment.TickCount64;
    /// <summary>Dernière réponse SIGNÉE et valide du serveur (auth, hello/beat, heartbeat HTTP, autre requête). Sert à
    /// distinguer une coupure totale (tout le serveur muet) d'un blocage ciblé d'une seule route.</summary>
    public long LastServerOkTick => Interlocked.Read(ref _lastServerOkTick);
    private void NoteServerOk() => Interlocked.Exchange(ref _lastServerOkTick, Environment.TickCount64);

    // Battement ANTICIPÉ (un accusé vient de partir) : sa réponse prouve la réception en ~1 s au lieu d'attendre le beat
    // normal (60 s) ; moins d'accusés restent « en vol » si le socket meurt. Au plus un toutes les 5 s.
    private TaskCompletionSource<bool> _beatSoon = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _lastEarlyBeatTick;
    public void RequestEarlyBeat()
    {
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastEarlyBeatTick) < 5_000) return;
        Interlocked.Exchange(ref _lastEarlyBeatTick, now);
        Volatile.Read(ref _beatSoon).TrySetResult(true);
    }
    private void SetLinkDown(bool down)
    {
        if (_linkDown == down) return;
        _linkDown = down;
        LinkDownChanged?.Invoke(down);
    }

    /// <summary>Reprise de session : recopie l'anti-doublon des commandes de l'ancien client (une commande re-livrée après la
    /// reprise n'est ni ré-exécutée ni ré-accusée).</summary>
    internal void SeedSeenCommandsFrom(LicenseClient old)
    {
        KeyValuePair<string, long>[] snap;
        lock (old._seenCommands) snap = old._seenCommands.ToArray();
        lock (_seenCommands) foreach (var kv in snap) _seenCommands.TryAdd(kv.Key, kv.Value);
    }

    /// <summary>Les envois vers le panel (télémétrie, arch, accusés…) ont été refusés à répétition par le serveur (code en
    /// argument) : suspendus jusqu'à la prochaine connexion licence. La session, elle, reste active. Thread de fond.</summary>
    public event Action<string>? UploadsSuspended;
    /// <summary>Les DROITS de la licence ont changé EN DIRECT (push WS license_update lors d'une édition côté panel,
    /// ou lecture des permissions du heartbeat) — SANS re-login. <see cref="License"/> est déjà à jour. Thread de fond.</summary>
    public event Action? PermissionsChanged;

    /// <summary>Push WS "control_kill" (kill switch launcher control activé côté panel) : blocage GLOBAL immédiat. La
    /// frame est déjà VÉRIFIÉE (signature Ed25519 + server_time) par le pipeline WS avant d'arriver ici. Thread de fond.</summary>
    public event Action<ControlKillInfo>? ControlKill;

    /// <summary>Commande serveur poussée (ex "DISCONNECT_BOT"). Frame déjà vérifiée (signature + server_time). Le
    /// consommateur route la commande (ex vers l'arrêt d'un bot par pid). Thread de fond.</summary>
    public event Action<ServerCommand>? Command;

    public LicenseClient(string baseUrl, string publicKeyHex, string clientName, string clientVersion)
    {
        // Cle publique EMBARQUEE : 32 octets en 64 hex (server/keys/ed25519_public.hex). Refus si placeholder.
        if (publicKeyHex.Length != 64 || !publicKeyHex.All(Uri.IsHexDigit))
            throw new ArgumentException("Cle publique invalide : 64 hex attendus.");
        _publicKey = Convert.FromHexString(publicKeyHex);

        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        // User-Agent stable et identifiable (auth/heartbeat/logout/mapping) -> allowlist WAF Cloudflare (Bot Fight Mode).
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GivreClient/" + clientVersion);
        _clientName = clientName;
        _clientVersion = clientVersion;
        _hwid = Hwid.Compute();
    }

    /// <summary>POST /auth : active la licence (1ere fois) ou ouvre une session. Leve LicenseServerException
    /// (erreur metier signee) ou EnvelopeException. Sur succes, un jeton de session est memorise.</summary>
    /// <param name="retryGate">Requête signée refusée pour DÉRIVE d'horloge : autorise-t-on le nouvel essai (après recalage) ?
    /// La reprise silencieuse le compte dans son budget horaire. null = autorisé.</param>
    public async Task AuthenticateAsync(string key, CancellationToken ct = default, Func<bool>? retryGate = null)
    {
        // AUDIT M8 : refuse d'authentifier si le vérificateur Ed25519 a été neutralisé (stub =>true). Déchargé sur un
        // thread (le KAT = ~900 ms de BigInteger, une fois par process car mis en cache) pour ne PAS figer l'UI de login.
        await Task.Run(EnsureVerifierIntact).ConfigureAwait(false);
        var sentSigned = new System.Runtime.CompilerServices.StrongBox<bool>();   // la requête RÉELLEMENT partie était-elle signée ?
        var payload = await PostRequestAsync("auth", w =>
        {
            w.WriteString("key", key);
            w.WriteString("hwid", _hwid);
            w.WriteStartObject("client");
            w.WriteString("name", _clientName);
            w.WriteString("version", _clientVersion);
            w.WriteEndObject();
        }, ct, retryGate, sentSigned).ConfigureAwait(false);
        ThrowIfError(payload);

        try
        {
            var session = payload.GetProperty("session");
            _token = session.GetProperty("token").GetString() ?? throw new EnvelopeException("jeton manquant");
            _heartbeatIntervalS = session.GetProperty("heartbeat_interval").GetInt32();
            // Session LIÉE à notre clé (/auth signé) : chaque /mapping et chaque envoi WS doivent ensuite utiliser la même paire.
            SignedAuth = sentSigned.Value;
            KeyBound = OptBool(session, "key_bound");
            KeyFingerprint = session.TryGetProperty("key_fingerprint", out var kf) && kf.ValueKind == JsonValueKind.String ? kf.GetString() : null;

            var license = payload.GetProperty("license");
            var package = license.GetProperty("package");
            var permissions = package.GetProperty("permissions").EnumerateArray()
                .Select(p => new PermissionInfo(p.GetProperty("name").GetString() ?? "", OptLong(p, "expires_at")))
                .ToArray();
            License = new LicenseInfo(
                license.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString()! : "active",
                OptLong(license, "activated_at"),
                OptLong(license, "expires_at") ?? 0L,
                OptInt(package, "duration_days", 0),
                OptLong(license, "last_auth_at"),
                package.GetProperty("name").GetString() ?? "",
                permissions,
                // Cap serveur : à plat sous license OU dans package ; envoyé SEULEMENT s'il est réglé sur le pack (absent = non réglé
                // -> défaut client : gratuit 30, payant aucune limite ; 0 = aucune limite).
                ReadMaxLevel(license) ?? ReadMaxLevel(package),
                OptBool(license, "free") ?? OptBool(package, "free"));
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            _token = null;
            throw new EnvelopeException("payload d'authentification incomplet");
        }
    }

    /// <summary>Sortie du repli heartbeat HTTP.</summary>
    public enum HttpHeartbeatExit
    {
        /// <summary>Arrêt demandé (CancellationToken).</summary>
        Stopped,
        /// <summary>Session finie (terminal / intégrité : onInvalid appelé) ou reprenable (lapsed : événement Lapsed levé).</summary>
        Ended,
        /// <summary>Retenter le temps réel (WS) : réseau revenu après une coupure, ou repli prolongé.</summary>
        RetryWebSocket,
    }

    /// <summary>Repli POST /heartbeat toutes les heartbeat_interval s (60 s ; 30 s pendant une coupure). Code terminal signé ->
    /// onInvalid (après terminate_in) : l'app DOIT fermer la session. Échecs d'INTÉGRITÉ répétés (signature, horloge, nonce)
    /// -> onInvalid aussi. Échecs de LIEN répétés (réseau, page d'erreur de l'edge, code non terminal comme RATE_LIMITED /
    /// INTERNAL) -> LinkDownChanged(true) : botting suspendu, licence GARDÉE, on continue d'essayer (avant : fin de session après
    /// ~5 min de coupure, alors que le serveur aurait proposé une reprise au retour du réseau).</summary>
    public async Task<HttpHeartbeatExit> HeartbeatLoopAsync(Action<LicenseServerException?, string> onInvalid, CancellationToken ct)
    {
        if (_token is null) throw new InvalidOperationException("Appeler AuthenticateAsync d'abord.");

        // Compteurs d'échecs portés par le client : ils survivent aux passages du repli (tentative WS toutes les 2 min entre deux),
        // sinon le lien perdu ne serait jamais constaté.
        long start = Environment.TickCount64;
        while (!ct.IsCancellationRequested)
        {
            int intervalS = _linkDown ? Math.Max(1, _heartbeatIntervalS / 2) : _heartbeatIntervalS;
            try { await Task.Delay(TimeSpan.FromSeconds(intervalS), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            try
            {
                var payload = await PostSignedAsync("heartbeat", w =>
                {
                    w.WriteString("token", _token!);
                    w.WriteString("hwid", _hwid);
                }, ct).ConfigureAwait(false);
                ThrowIfError(payload);
                bool recovered = _linkDown || _hbLinkFailures > 0;
                _hbLinkFailures = 0;
                _hbIntegrityFailures = 0;
                bool hbExpiry = ApplyExpiry(payload, "license_expires_at");
                // Droits EN DIRECT (repli HTTP ≤60 s) : le heartbeat renvoie license.permissions (recalculé) à chaque beat.
                if (payload.TryGetProperty("license", out var licHb) && licHb.ValueKind == JsonValueKind.Object)
                    ApplyLicenseUpdate(licHb, OptLong(payload, "server_time") ?? 0);
                if (hbExpiry) PermissionsChanged?.Invoke();
                // Lien revenu (session toujours valide) : botting rétabli, et on retente le temps réel tout de suite.
                if (recovered) { SetLinkDown(false); return HttpHeartbeatExit.RetryWebSocket; }
            }
            catch (OperationCanceledException) { break; }
            // Session reprenable (lapsed, réponse à NOTRE heartbeat : nonce vérifié par PostSignedAsync) : l'événement Lapsed est
            // déjà levé ; ni onInvalid (écran de clé), ni terminate_in.
            catch (LicenseServerException e) when (e.Lapsed) { return HttpHeartbeatExit.Ended; }
            catch (LicenseServerException e) when (IsTerminal(e.Code))
            {
                MarkTerminal(e);   // dès maintenant : plus aucune reprise possible pendant terminate_in
                // Anti-tamper : honorer le delai d'arret aleatoire (borne) avant de signaler.
                if (e.TerminateInMs is long ms && ms > 0)
                {
                    try { await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(ms, 600_000)), ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
                if (ct.IsCancellationRequested) return HttpHeartbeatExit.Stopped;   // session remplacée/arrêtée pendant l'attente
                onInvalid(e, e.Message);
                return HttpHeartbeatExit.Ended;
            }
            catch (LicenseServerException)
            {
                // Code signé NON terminal (RATE_LIMITED, INTERNAL, inconnu) : le serveur répond et ne ferme pas la session.
                if (++_hbLinkFailures >= MaxConsecutiveFailures) SetLinkDown(true);
            }
            catch (EnvelopeException e)
            {
                if (_abandoned) return HttpHeartbeatExit.Ended;   // session abandonnée (reprise en cours) : pas un échec du heartbeat
                // Intégrité (signature, horloge du PC, nonce) : la réponse n'est pas digne de confiance -> fin de session comme avant.
                if (Diag.Env(e.Message) != 4)
                {
                    if (++_hbIntegrityFailures >= MaxConsecutiveFailures) { onInvalid(null, e.Message); return HttpHeartbeatExit.Ended; }
                }
                // Lien (réseau, délai, page d'erreur de l'edge) : on suspend, sans jamais fermer la session.
                else if (++_hbLinkFailures >= MaxConsecutiveFailures) SetLinkDown(true);
            }
            // Repli prolongé : une tentative de temps réel (après la requête, pour que chaque passage compte ses échecs).
            if (Environment.TickCount64 - start >= (long)FallbackWsRetryAfter.TotalMilliseconds) return HttpHeartbeatExit.RetryWebSocket;
        }
        return HttpHeartbeatExit.Stopped;
    }

    /// <summary>POST /logout : fermeture propre et idempotente (best-effort).</summary>
    public async Task LogoutAsync()
    {
        if (_token is null) return;
        try
        {
            var payload = await PostSignedAsync("logout", w => w.WriteString("token", _token!), CancellationToken.None).ConfigureAwait(false);
            ThrowIfError(payload);
        }
        catch (Exception e) when (e is EnvelopeException or LicenseServerException) { /* best-effort */ }
        finally { _token = null; }
    }

    /// <summary>Vrai si la licence porte ce droit ET qu'il n'est pas expiré. Chaque droit a SA date de péremption
    /// (peut expirer AVANT la licence, cf page "Packages" du panel) ; un droit sans date (ExpiresAtMs null/0) suit
    /// l'expiration de la licence. On vérifie AUSSI l'expiration globale de la licence. Comparaison insensible à la
    /// casse sur le code. C'est la porte AUTORITAIRE d'un droit — à consommer au point d'action, pas juste dans l'UI.</summary>
    public bool HasPermission(string permission)
    {
        if (License is not { } lic) return false;
        if (IsBlockedStatus(lic.Status)) return false;   // en pause / révoquée / expirée / bannie (license_update sans frame terminale)
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (lic.ExpiresAtMs > 0 && now >= lic.ExpiresAtMs) return false;   // licence globalement expirée
        var p = lic.Permissions.FirstOrDefault(x => string.Equals(x.Name, permission, StringComparison.OrdinalIgnoreCase));
        if (p is null) return false;
        if (p.ExpiresAtMs is long exp && exp > 0 && now >= exp) return false;   // droit expiré avant la licence
        return true;
    }

    /// <summary>Instantané des droits ACTIFS (non expirés) — pour l'onglet "Mon compte".</summary>
    public IReadOnlyList<PermissionInfo> ActivePermissions()
    {
        if (License is not { } lic) return Array.Empty<PermissionInfo>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var list = new List<PermissionInfo>();
        foreach (var p in lic.Permissions)
            if (p.ExpiresAtMs is not { } exp || exp <= 0 || now < exp) list.Add(p);
        return list;
    }

    // ---------------------------------------------------- mapping (streamé + déchiffré en mémoire) --

    /// <summary>
    /// POST /mapping : récupère les artefacts de protocole (confirmed/matching/fields/proto[/positions]) pour un
    /// <paramref name="build"/> Dofus, DÉCHIFFRÉS EN MÉMOIRE (HKDF-SHA256(token) -> AES-256-GCM, sha256 vérifié).
    /// Rien n'est écrit sur disque -> le mapping n'est jamais visible pour l'utilisateur. Requiert AuthenticateAsync.
    /// Passer <paramref name="knownBuild"/>+<paramref name="knownVersion"/> (d'un bundle reçu avec fallback=false)
    /// active le court-circuit not_modified. Lève LicenseServerException / EnvelopeException.
    /// </summary>
    public async Task<MappingBundle> FetchMappingAsync(string build, string? knownBuild = null, int? knownVersion = null, CancellationToken ct = default)
    {
        // Copie locale : un /logout concurrent remet _token à null -> jamais de « "token":null » dans le corps.
        var token = _token ?? throw new InvalidOperationException("Appeler AuthenticateAsync d'abord.");

        var payload = await PostRequestAsync("mapping", w =>
        {
            w.WriteString("token", token);
            w.WriteString("hwid", _hwid);
            w.WriteString("build", build);
            w.WriteNumber("mapping_key_version", 2);   // B.4 : demande le scellement lié au HWID (v2). Serveur v1 -> repli auto au déchiffrement.
            // known_build + known_version vont ENSEMBLE et seulement pour un bundle reçu avec fallback=false.
            if (!string.IsNullOrEmpty(knownBuild) && knownVersion is int kv)
            {
                w.WriteString("known_build", knownBuild);
                w.WriteNumber("known_version", kv);
            }
        }, ct).ConfigureAwait(false);
        ThrowIfError(payload);

        var servedBuild = payload.TryGetProperty("build", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()! : build;
        var mappingVersion = OptInt(payload, "mapping_version", 0);
        var fallback = payload.TryGetProperty("fallback", out var f) && f.ValueKind == JsonValueKind.True;

        if (payload.TryGetProperty("not_modified", out var nm) && nm.ValueKind == JsonValueKind.True)
            return new MappingBundle(servedBuild, mappingVersion, fallback, true, new Dictionary<string, string>());

        if (!payload.TryGetProperty("artefacts", out var arts) || arts.ValueKind != JsonValueKind.Object)
            throw new EnvelopeException("réponse mapping sans artefacts");

        // AUDIT M8 : la dérivation de la clé du JOYAU est GATÉE par l'intégrité du vérificateur Ed25519. Un binaire craqué
        // qui a neutralisé CheckValid (=>true) pour contourner la licence échoue ici -> aucune clé dérivée -> mapping
        // indéchiffrable. Le joyau reste scellé même si l'auth a été frauduleusement "acceptée". Vérif mise en cache.
        EnsureVerifierIntact();
        // B.4 : on tente d'abord la clé v2 (liée au HWID). Si le serveur scelle ENCORE en v1 (déploiement pas
        // encore bascule -> il ignore mapping_key_version et scelle en v1), la clé v2 échoue en bloc (GCM) et on
        // REPLIE proprement sur v1 -> jamais de mapping illisible pendant la fenêtre de bascule. Le repli est
        // LOGGUÉ (détecte un serveur encore en v1 ; B.4 inactif tant qu'il n'est pas déployé).
        var keyV2 = DeriveMappingKeyV2(token, _hwid);
        var result = TryDecryptAll(arts, keyV2);
        CryptographicOperations.ZeroMemory(keyV2);   // hygiène RAM : la clé AES ne survit pas au déchiffrement
        if (result is null)
        {
            Logger.Warn("Mapping : clé v2 non acceptée -> repli v1.");
            var keyV1 = DeriveMappingKey(token);
            result = TryDecryptAll(arts, keyV1);
            CryptographicOperations.ZeroMemory(keyV1);
            if (result is null) throw new EnvelopeException("mapping : déchiffrement échoué (ni v2 ni v1)");
        }
        return new MappingBundle(servedBuild, mappingVersion, fallback, false, result);
    }

    // ---------------------------------------------------- scripts Lua (push au chargement) --

    /// <summary>POST /api/v1/client/scripts : pousse un script Lua chargé par l'utilisateur (il entre dans sa librairie
    /// portail + l'inventaire admin, avec historique de révisions). Session-gated (token+hwid) et enveloppe signée
    /// EXACTEMENT comme /heartbeat et /mapping (nonce + signature Ed25519 + server_time vérifiés sur la réponse).
    /// L'identité serveur d'un script est (licence, name) : même name + contenu différent = nouvelle révision ; même
    /// contenu = aucune révision (juste la date de dernier chargement). Requiert AuthenticateAsync. Lève
    /// LicenseServerException (VALIDATION_ERROR / RATE_LIMITED / SESSION_EXPIRED / HWID_MISMATCH / KEY_*) ou EnvelopeException.</summary>
    public async Task<ScriptPushResult> PushScriptAsync(string name, string content, CancellationToken ct = default)
    {
        if (_token is null) throw new InvalidOperationException("Appeler AuthenticateAsync d'abord.");
        var payload = await PostSignedAsync("scripts", w =>
        {
            w.WriteString("token", _token!);
            w.WriteString("hwid", _hwid);
            w.WriteString("name", name);
            w.WriteString("content", content);
        }, ct).ConfigureAwait(false);
        ThrowIfError(payload);
        return new ScriptPushResult(OptLong(payload, "id") ?? 0, StrOpt(payload, "name") ?? name, OptInt(payload, "revision", 0));
    }

    // Clé AES-256 dérivée du jeton : HKDF-SHA256(IKM=UTF8(token), salt="ikuware-hkdf-salt-v1",
    // info="givre/mapping/session-key/v1", L=32). PIÈGE : IKM = les 64 CARACTÈRES hex en UTF-8, PAS les 32 octets décodés.
    private static byte[] DeriveMappingKey(string token) =>
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(token),
            32,
            Encoding.UTF8.GetBytes("ikuware-hkdf-salt-v1"),
            Encoding.UTF8.GetBytes("givre/mapping/session-key/v1"));

    // B.4 (v2) : clé AES liée au HWID. Contrat serveur (Ikuware) : IKM = UTF8(token + "|" + hwid) — le SÉPARATEUR
    // est un unique octet 0x7C ('|') ; salt INCHANGÉ ; info bumpée en …/v2. hwid = la MÊME chaîne (64 hex minuscule)
    // envoyée sur /auth,/heartbeat,/mapping. Un blob capté + token fuité devient indéchiffrable sans le HWID brut.
    private static byte[] DeriveMappingKeyV2(string token, string hwid) =>
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(token + "|" + hwid),
            32,
            Encoding.UTF8.GetBytes("ikuware-hkdf-salt-v1"),
            Encoding.UTF8.GetBytes("givre/mapping/session-key/v2"));

    // ---- AUDIT M8 : self-test du vérificateur de signature (lie la licence + le mapping à un Ed25519 INTACT) ----
    private static volatile bool _verifierChecked;
    private static readonly object _verifierGate = new();

    /// <summary>Known-answer test du vérificateur Ed25519. On génère un couple (clé, signature) via <c>Signature()</c>
    /// (fonction DISTINCTE de <c>CheckValid()</c> : un stub de CheckValid ne fausse pas la génération), puis on EXIGE :
    /// signature valide ACCEPTÉE **et** signature altérée REJETÉE. Un crack qui neutralise CheckValid (=>true toujours)
    /// échoue le cas négatif -> exception. Défendre l'intégrité de CE test est le rôle de VMProtect (chemin virtualisé) ;
    /// à défaut, le crack devrait réimplémenter la vraie vérif de courbe, ce qui annule tout l'intérêt du contournement.
    /// Mis en cache : le coût (quelques mult. scalaires BigInteger) n'est payé qu'une fois par process.</summary>
    private static void EnsureVerifierIntact()
    {
        if (_verifierChecked) return;
        lock (_verifierGate)
        {
            if (_verifierChecked) return;
            bool good, badRejected;
            try
            {
                var seed = new byte[32];
                for (int i = 0; i < seed.Length; i++) seed[i] = (byte)(0x21 + i);   // graine déterministe (pas de secret ici)
                var pub = Cryptographic.Ed25519.PublicKey(seed);
                var msg = Encoding.ASCII.GetBytes("givre/verifier-self-test/v1");
                var sig = Cryptographic.Ed25519.Signature(msg, seed, pub);
                good = Cryptographic.Ed25519.CheckValid(sig, msg, pub);            // doit être TRUE
                var bad = (byte[])sig.Clone(); bad[7] ^= 0x40;                     // altère R -> doit être REJETÉE
                try { badRejected = !Cryptographic.Ed25519.CheckValid(bad, msg, pub); }
                catch { badRejected = true; }                                     // point hors courbe = rejet légitime
            }
            catch { throw new EnvelopeException("auto-test du vérificateur de signature en échec"); }
            if (!(good && badRejected)) throw new EnvelopeException("vérificateur de signature altéré (intégrité licence)");
            _verifierChecked = true;
        }
    }

    // Déchiffre TOUS les artefacts avec la clé fournie. Retourne null si un artefact refuse la clé (GCM/sha ->
    // EnvelopeException) : signal d'une MAUVAISE clé (mismatch de version) -> l'appelant tente l'autre version.
    // Un artefact structurellement incomplet reste une vraie erreur (throw), indépendante de la clé.
    private static Dictionary<string, string>? TryDecryptAll(JsonElement arts, byte[] key)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var art in arts.EnumerateObject())
        {
            var enc = art.Value.TryGetProperty("enc", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            var sha = art.Value.TryGetProperty("sha256", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            if (enc is null || sha is null) throw new EnvelopeException($"artefact '{art.Name}' incomplet");
            try { result[art.Name] = DecryptArtefact(enc, sha, key); }
            catch (EnvelopeException) { return null; }   // clé refusée -> mismatch de version, on laissera l'appelant replier
        }
        return result;
    }

    // enc = "iv:tag:ciphertext" (hex). AES-256-GCM sans AAD. Vérifie SHA256(clair) == sha256 attendu (hex).
    private static string DecryptArtefact(string enc, string sha256Hex, byte[] key)
    {
        var parts = enc.Split(':');
        if (parts.Length != 3) throw new EnvelopeException("format enc invalide (iv:tag:ct attendu)");
        byte[] iv, tag, ct;
        try { iv = Convert.FromHexString(parts[0]); tag = Convert.FromHexString(parts[1]); ct = Convert.FromHexString(parts[2]); }
        catch (FormatException) { throw new EnvelopeException("enc : hex invalide"); }

        var pt = new byte[ct.Length];
        try
        {
            try
            {
                using var gcm = new AesGcm(key, tag.Length);
                gcm.Decrypt(iv, ct, tag, pt);
            }
            catch (CryptographicException) { throw new EnvelopeException("déchiffrement échoué"); }

            var actual = Convert.ToHexString(SHA256.HashData(pt)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(sha256Hex.ToLowerInvariant())))
                throw new EnvelopeException("sha256 du clair ne correspond pas");
            return Encoding.UTF8.GetString(pt);
        }
        finally
        {
            // Hygiène RAM : efface le CLAIR (octets du joyau) dès qu'il est converti en string -> réduit la fenêtre
            // de dump. (La string reste managée/non-effaçable, mais on ne laisse pas traîner le byte[] du plaintext.)
            CryptographicOperations.ZeroMemory(pt);
        }
    }

    // ---------------------------------------------------- heartbeat temps-reel (WebSocket) --

    private sealed class TerminalSignal : Exception
    {
        public LicenseServerException Error { get; }
        public TerminalSignal(LicenseServerException error) => Error = error;
    }

    // Refus au niveau du SOCKET (VALIDATION_ERROR, RATE_LIMITED, code inconnu…) : le serveur ferme CE socket
    // (clientHub.terminate) sans révoquer la session de licence -> on se reconnecte, on ne déconnecte pas l'utilisateur.
    private sealed class SocketRejectedSignal(string code) : Exception(code);

    // SESSION_EXPIRED + lapsed:true en réponse à NOTRE hello/beat : session reprenable (nouveau /auth piloté par LicenseService).
    private sealed class LapsedSignal(LicenseServerException error) : Exception(error.Code)
    {
        public LicenseServerException Error { get; } = error;
    }

    // Le socket vient d'être refusé : si c'est juste après un envoi, on compte ; au-delà de MaxUploadRejects -> coupe-circuit.
    private void NoteSocketRejection(string code)
    {
        if (Environment.TickCount64 - Interlocked.Read(ref _lastUploadTick) > UploadRejectWindowMs) return;
        if (Interlocked.Increment(ref _uploadRejects) >= MaxUploadRejects && !_uploadsSuspended)
        {
            _uploadsSuspended = true;
            Diag.Log(307, Diag.Err(code));
            UploadsSuspended?.Invoke(code);
        }
    }

    /// <summary>
    /// Heartbeat temps-reel par WebSocket (PROTOCOL.md §8) : ouvre le socket, l'authentifie avec le
    /// jeton, bat toutes les heartbeat_interval s et ECOUTE les push serveur. Un kill pousse par le
    /// serveur (admin "fermer la session", revoke, pause, expiration) est traite A L'INSTANT ou il
    /// arrive, pas au prochain beat -> declenche <see cref="Terminated"/>. Rend la main quand la session
    /// se termine (terminal), que le socket ne se retablit plus, ou que <paramref name="ct"/> est annule.
    /// Requiert AuthenticateAsync au prealable.
    /// </summary>
    /// <param name="singleAttempt">Depuis le repli HTTP : UNE tentative ; si elle aboutit, reconnexions normales ensuite.</param>
    public async Task RunWebSocketAsync(CancellationToken ct, bool singleAttempt = false)
    {
        if (_token is null) throw new InvalidOperationException("Appeler AuthenticateAsync d'abord.");

        var wsUri = BuildWebSocketUri();
        var attempt = 0;
        int limit = singleAttempt ? 0 : MaxWsReconnects;

        while (!ct.IsCancellationRequested && !_abandoned)
        {
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = WsKeepAliveInterval;
            // Avec un délai, le client envoie des PING et déclare le socket mort sans PONG (sans délai : simples PONG spontanés,
            // aucune détection). Un lien coupé en silence était sinon vu seulement quand TCP abandonnait (20-80 s).
            if (!_keepAliveOff) socket.Options.KeepAliveTimeout = WsKeepAliveTimeout;
            socket.Options.SetRequestHeader("User-Agent", "GivreClient/" + _clientVersion);   // handshake GET .../ws visible par le WAF Cloudflare
            var before = _beatCount;
            long opened = Environment.TickCount64;
            bool rejected = false, kaAbort = false, connected = false;
            WsCtx? ctxOut = null;
            try
            {
                ConnectionChanged?.Invoke(attempt == 0 ? WsConnectionState.Connecting : WsConnectionState.Reconnecting);
                Diag.Log(300, attempt);
                using (var cto = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cto.CancelAfter(WsConnectTimeout);
                    await socket.ConnectAsync(wsUri, cto.Token).ConfigureAwait(false);
                }
                connected = true;
                await SessionOverSocketAsync(socket, ct, c => ctxOut = c).ConfigureAwait(false);
                Diag.Log(308, _beatCount - before);   // fermé proprement par le serveur (trame Close)
            }
            // Arrêt demandé ou jeton abandonné. Une OperationCanceledException SANS l'un des deux = keep-alive expiré (socket
            // mort) : reconnexion par le cas général ci-dessous, pas l'abandon du temps réel.
            catch (OperationCanceledException) when (ct.IsCancellationRequested || _abandoned) { break; }
            catch (TerminalSignal t)
            {
                Diag.Log(305, Diag.Err(t.Error.Code));
                ConnectionChanged?.Invoke(WsConnectionState.Closed);
                Terminated?.Invoke(t.Error);
                return;
            }
            catch (LapsedSignal)
            {
                // Grâce serveur dépassée (lapsed) : AUCUN hello de plus avec ce jeton. La reprise (nouveau /auth) est pilotée
                // par LicenseService sur un NOUVEAU client.
                ConnectionChanged?.Invoke(WsConnectionState.Closed);
                Lapsed?.Invoke(10);   // tracé 309 10 par LicenseService.OnLapsed
                return;
            }
            catch (SocketRejectedSignal) { rejected = true; /* déjà tracé (306) : reconnexion IMMÉDIATE (grâce serveur 15 s) */ }
            catch (Exception e)
            {
                Diag.Log(302, Diag.Ex(e), _beatCount - before);   // connexion ratée / socket tombé : on tente une reconnexion
                // OCE après l'ouverture et hors chien de garde = PONG absent (keep-alive). Un délai d'ouverture dépassé ou un
                // hello resté sans réponse est un échec de connexion ordinaire.
                if (e is OperationCanceledException && connected && ctxOut is { WatchdogFired: false })
                {
                    NoteKeepAliveAbort(_beatCount > before, opened);
                    kaAbort = true;
                }
            }
            if (!kaAbort) { _lastKaAbortTick = 0; _keepAliveStrikes = 0; }   // chaîne de morts par keep-alive rompue

            if (_abandoned) break;   // abandonné pendant ce socket (lapsed vu en HTTP) : jamais de reconnexion avec l'ancien jeton
            if (_beatCount > before) { attempt = 0; limit = MaxWsReconnects; } else attempt++;
            if (attempt > limit || ct.IsCancellationRequested)
            {
                // Temps réel abandonné -> repli heartbeat HTTP (pas tracé pour une simple tentative du repli qui échoue).
                if (!ct.IsCancellationRequested && limit > 0) Diag.Log(304, attempt);
                break;
            }
            ConnectionChanged?.Invoke(WsConnectionState.Reconnecting);
            // Backoff exponentiel + jitter (borné 30 s) : évite de marteler l'edge Cloudflare pendant un incident.
            // Envoi refusé (le serveur a fermé CE socket, session intacte) : reconnexion immédiate. Sinon backoff exponentiel
            // plafonné sous la grâce serveur de 15 s.
            var backoff = rejected ? 250 : Math.Min(WsReconnectDelayMs * (1 << Math.Min(attempt, 4)), WsReconnectCapMs);
            backoff += Random.Shared.Next(0, rejected ? 250 : 500);
            Diag.Log(303, attempt, backoff);
            try { await Task.Delay(backoff, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        ConnectionChanged?.Invoke(WsConnectionState.Closed);
    }

    // Socket tué par le keep-alive alors qu'il avait été rouvert AUSSITÔT après une mort par keep-alive (le réseau marchait donc)
    // et acquitté : deux fois de suite = le pair ne répond pas aux PING (proxy intermédiaire) -> délai coupé pour ce client
    // (comportement d'avant), jamais de boucle de reconnexions. Une vraie coupure ne compte pas : la reconnexion échoue tant
    // qu'elle dure, ce qui rompt la chaîne.
    private void NoteKeepAliveAbort(bool helloAcked, long openedTick)
    {
        if (_keepAliveOff) return;
        // Socket COURT (< 60 s) : un pair qui ignore les PING tue chaque socket ~30 s après son ouverture ; un socket qui a vécu
        // des heures puis est tombé sur une coupure ne dit rien du pair (sinon 3 petites coupures dans la journée suffisaient).
        bool shortLived = Environment.TickCount64 - openedTick < 60_000;
        if (helloAcked && shortLived && _lastKaAbortTick != 0 && openedTick - _lastKaAbortTick < 15_000)
        {
            if (++_keepAliveStrikes >= 2) { _keepAliveOff = true; Diag.Log(310); }
        }
        else _keepAliveStrikes = 0;
        _lastKaAbortTick = Environment.TickCount64;
    }

    private async Task SessionOverSocketAsync(ClientWebSocket socket, CancellationToken ct, Action<WsCtx> onCtx)
    {
        var ctx = new WsCtx(socket, Interlocked.Increment(ref _wsGenSeq));
        onCtx(ctx);
        lock (_nonceLock) _pendingNonces.Clear();
        _uploadsReady = false;    // rouvert à l'acquittement du hello (ReceiveLoopAsync)
        _active = ctx;            // expose le socket pour les uploads (télémetrie bots, accusés) hors de cette boucle
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await SendFrameAsync(ctx, "hello", linked.Token).ConfigureAwait(false);
        // Hello sans réponse (serveur ou intermédiaire muet après l'upgrade, keep-alive éventuellement coupé) : socket coupé.
        _ = Task.Delay(WsHelloTimeout, linked.Token).ContinueWith(t =>
        {
            if (t.IsCanceled || ctx.HelloAcked) return;
            ctx.WatchdogFired = true;
            try { socket.Abort(); } catch { }
        }, TaskScheduler.Default);
        var beats = BeatSenderAsync(ctx, linked.Token);
        try { await ReceiveLoopAsync(ctx, linked.Token).ConfigureAwait(false); }
        finally
        {
            _uploadsReady = false;
            _active = null;
            linked.Cancel();
            try { await beats.ConfigureAwait(false); } catch { }
            if (_abandoned) { try { socket.Abort(); } catch { } }   // jeton abandonné : ni « bye » ni fermeture négociée avec lui
            else if (socket.State == WebSocketState.Open)
            {
                // Borné : un pair qui n'acquitte pas le Close (lien à moitié mort) bloquait ici SANS FIN, et avec lui la fin de
                // session (Terminated -> retour à l'écran de clé) après un kick poussé.
                using var closeCts = new CancellationTokenSource(3000);
                try
                {
                    // « bye » = fin de session côté serveur (endSession 'logout'). Seulement sur un arrêt VOULU (ct annulé :
                    // déconnexion, nouveau login). Après un refus d'envoi, une trame terminale ou lapsed, le serveur a déjà fermé
                    // CE socket mais traite encore ses messages : un bye y révoquait la session, et le hello suivant renvoyait
                    // SESSION_EXPIRED -> écran de clé pour un simple refus non terminal.
                    if (ct.IsCancellationRequested) await SendByeAsync(ctx, closeCts.Token).ConfigureAwait(false);
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token).ConfigureAwait(false);
                }
                catch { try { socket.Abort(); } catch { } }
            }
        }
    }

    private async Task BeatSenderAsync(WsCtx ctx, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var soon = Volatile.Read(ref _beatSoon);
            var first = await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(_heartbeatIntervalS), ct), soon.Task).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (first == soon.Task)
            {
                Interlocked.Exchange(ref _beatSoon, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
                await Task.Delay(1000, ct).ConfigureAwait(false);   // regroupe les accusés d'une rafale de commandes
            }
            long silentMs = Environment.TickCount64 - Interlocked.Read(ref ctx.LastReplyTick);
            if (ctx.HelloAcked && silentMs > _heartbeatIntervalS * 1000L + (long)WsReplyGrace.TotalMilliseconds)
            {
                Diag.Log(311, silentMs / 1000);   // battements sans réponse : socket coupé
                ctx.WatchdogFired = true;
                try { ctx.Socket.Abort(); } catch { }
                return;
            }
            await SendFrameAsync(ctx, "beat", ct).ConfigureAwait(false);
        }
    }

    private async Task SendFrameAsync(WsCtx ctx, string type, CancellationToken ct)
    {
        // Abandonné pendant l'ouverture du socket (lapsed vu en HTTP) : aucun hello/beat de plus avec l'ancien jeton.
        if (_abandoned) throw new OperationCanceledException();
        var nonce = MakeNonce();
        var bytes = FrameBytes(type, nonce);
        // Nonce enregistré AVEC son n° d'ordre, sous le verrou d'envoi et avant l'envoi : la réponse ne peut pas le précéder.
        await SendRawAsync(ctx, bytes, ct, seq => { lock (_nonceLock) _pendingNonces[nonce] = seq; }).ConfigureAwait(false);
    }

    private async Task SendByeAsync(WsCtx ctx, CancellationToken ct) =>
        await SendRawAsync(ctx, ByeBytes(), ct).ConfigureAwait(false);

    // Sérialise TOUS les envois WS (frames + uploads) : ClientWebSocket n'autorise pas 2 SendAsync concurrents. Renvoie le n°
    // d'ordre de la trame sur ce socket (attribué sous le verrou : ordre des numéros = ordre sur le fil).
    private async Task<long> SendRawAsync(WsCtx ctx, byte[] bytes, CancellationToken ct, Action<long>? beforeSend = null)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            long seq = ++ctx.Seq;
            beforeSend?.Invoke(seq);
            await ctx.Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
            return seq;
        }
        finally { _sendLock.Release(); }
    }

    /// <summary>Envoie un frame "upload" SIGNÉ CÔTÉ CLIENT sur le socket actif (best-effort ; no-op si pas de session WS).
    /// <paramref name="payloadBytes"/> = le JSON déjà construit (bots + pubkey + nonce + server_time), <paramref name="signature"/>
    /// = Ed25519(payloadBytes) avec la clé cliente. Thread-safe (verrou d'envoi).</summary>
    /// <summary>Pousse un upload signé sur le canal. Renvoie true s'il a réellement été envoyé (WS ouvert), false sinon
    /// (best-effort : un upload raté n'interrompt rien ; le relais MP s'en sert pour savoir s'il peut attendre une réponse).</summary>
    public async Task<bool> PushUploadAsync(string channel, byte[] payloadBytes, byte[] signature) =>
        await PushUploadTrackedAsync(channel, payloadBytes, signature).ConfigureAwait(false) is not null;

    /// <summary>Comme <see cref="PushUploadAsync"/>, mais renvoie la génération du socket et le n° d'ordre de la trame (null si
    /// rien n'est parti). « Parti » = remis au système, PAS reçu : la réception n'est prouvée que par <see cref="BeatConfirmed"/>
    /// pour un n° d'ordre supérieur sur la même génération.</summary>
    public async Task<(int Gen, long Seq)?> PushUploadTrackedAsync(string channel, byte[] payloadBytes, byte[] signature)
    {
        var ctx = _active;
        bool held = Environment.TickCount64 < Interlocked.Read(ref _uploadHoldUntil);
        if (_abandoned || ctx is null || ctx.Socket.State != WebSocketState.Open || !_uploadsReady || _uploadsSuspended || held)
        {
            Diag.LogThrottled(604, Diag.Chan(channel), 60_000, Diag.Chan(channel), _uploadsSuspended ? 1 : held ? 2 : 0);
            return null;
        }
        if (!TakeUploadToken(ctx))
        {
            Diag.LogThrottled(604, Diag.Chan(channel), 60_000, Diag.Chan(channel), 3);   // bridé (débit du serveur)
            return null;
        }
        try
        {
            long seq = await SendRawAsync(ctx, UploadFrameBytes(channel, payloadBytes, signature), CancellationToken.None).ConfigureAwait(false);
            Interlocked.Exchange(ref _lastUploadTick, Environment.TickCount64);
            return (ctx.Gen, seq);
        }
        catch { return null; }
    }

    // { "type":"upload", "channel":<channel>, "envelope":{ "payload":base64(payloadBytes), "signature":base64(signature) } }
    private static byte[] UploadFrameBytes(string channel, byte[] payloadBytes, byte[] signature)
    {
        using var ms = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("type", "upload");
            w.WriteString("channel", channel);
            w.WriteStartObject("envelope");
            w.WriteString("payload", Convert.ToBase64String(payloadBytes));
            w.WriteString("signature", Convert.ToBase64String(signature));
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    private byte[] FrameBytes(string type, string nonce)
    {
        using var ms = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("type", type);
            w.WriteString("token", _token!);
            w.WriteString("hwid", _hwid);
            w.WriteString("nonce", nonce);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    private byte[] ByeBytes()
    {
        using var ms = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("type", "bye");
            w.WriteString("token", _token!);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    private async Task ReceiveLoopAsync(WsCtx ctx, CancellationToken ct)
    {
        var socket = ctx.Socket;
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        var firstTick = true;

        while (!ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            sb.Clear();
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            if (_abandoned) return;   // jeton abandonné (reprise en cours) : plus aucune trame de l'ancien socket n'est traitée
            JsonElement payload;
            try
            {
                payload = ParseVerified(sb.ToString(), out long frameTime);
                if (Math.Abs(frameTime - ServerClock.NowMs()) >= MaxClockSkewMs)
                {
                    // Hors fenêtre : ignorée, SAUF la réponse à NOTRE hello/beat (nonce en attente) quand l'horloge du PC a été
                    // corrigée depuis un recalage (décalage recalculé). Une trame poussée garde la règle stricte.
                    var fn = StrOpt(payload, "nonce");
                    bool ours;
                    lock (_nonceLock) ours = fn is { Length: > 0 } && _pendingNonces.ContainsKey(fn);
                    if (!(ours && TryResyncClock(frameTime))) continue;
                }
            }
            catch (EnvelopeException) { continue; }   // frame non verifiable -> ignoree, socket garde

            // Commande serveur (ex DISCONNECT_BOT) : SIGNÉE mais SANS champ "status" -> à traiter AVANT le test de statut
            // ci-dessous, sinon prise pour une frame TERMINALE (kill de l'app). Commande inconnue -> ignorée (pas de crash).
            if (payload.TryGetProperty("type", out var t0) && t0.ValueKind == JsonValueKind.String && t0.GetString() == "command")
            {
                var cmd = StrOpt(payload, "command");
                bool hasPl = payload.TryGetProperty("payload", out var pl) && pl.ValueKind == JsonValueKind.Object;
                int? pid = hasPl && pl.TryGetProperty("pid", out var pe) && pe.ValueKind == JsonValueKind.Number && pe.TryGetInt32(out var pv) ? pv : null;
                // Commandes relais MP : SEND_PM { pid, target, content, via? } ; PM_ACTION { pid, target, action }.
                // Champs ignorés par les autres commandes (forward-compat).
                string? target = hasPl && pl.TryGetProperty("target", out var te) && te.ValueKind == JsonValueKind.String ? te.GetString() : null;
                string? content = hasPl && pl.TryGetProperty("content", out var ce) && ce.ValueKind == JsonValueKind.String ? ce.GetString() : null;
                string? action = hasPl && pl.TryGetProperty("action", out var ae) && ae.ValueKind == JsonValueKind.String ? ae.GetString() : null;
                string? via = hasPl && pl.TryGetProperty("via", out var ve) && ve.ValueKind == JsonValueKind.String ? ve.GetString() : null;
                // SHOW_NOTIFICATION { message } ; START_SCRIPT { pid, script? }. Ignorés par les autres commandes (forward-compat).
                string? message = hasPl && pl.TryGetProperty("message", out var me) && me.ValueKind == JsonValueKind.String ? me.GetString() : null;
                string? script = hasPl && pl.TryGetProperty("script", out var scE) && scE.ValueKind == JsonValueKind.String ? scE.GetString() : null;
                // nonce de la commande (au NIVEAU RACINE de la frame) : sert à l'accusé command_ack.
                string? cnonce = payload.TryGetProperty("nonce", out var noE) && noE.ValueKind == JsonValueKind.String ? noE.GetString() : null;
                if (cnonce is not null && !FirstSeenCommand(cnonce)) { Diag.Log(605, Diag.Cmd(cmd)); continue; }   // doublon : ni ré-exécutée, ni ré-accusée
                Diag.Log(600, Diag.Cmd(cmd));
                if (!string.IsNullOrEmpty(cmd)) Command?.Invoke(new ServerCommand(cmd!, pid, target, content, action, via, message, script, cnonce));
                continue;
            }

            var status = payload.TryGetProperty("status", out var s) ? s.GetString() : null;
            if (status == "ok")
            {
                // Pushs serveur NON sollicités (aucun nonce à écho) -> à traiter AVANT le contrôle de nonce, sinon
                // rejetés comme "ok non sollicité". La frame est déjà vérifiée (signature + server_time) par VerifyAndParse.
                var frameType = payload.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String ? ty.GetString() : null;
                // "license_update" : droits édités côté panel -> mise à jour INSTANTANÉE.
                if (frameType == "license_update")
                {
                    if (payload.TryGetProperty("license", out var licWs) && licWs.ValueKind == JsonValueKind.Object)
                        ApplyLicenseUpdate(licWs, OptLong(payload, "server_time") ?? 0);
                    continue;
                }
                // "control_kill" : kill switch launcher control activé -> blocage GLOBAL immédiat.
                if (frameType == "control_kill")
                {
                    ControlKill?.Invoke(new ControlKillInfo(
                        payload.TryGetProperty("kill", out var kk) && kk.ValueKind == JsonValueKind.True,
                        payload.TryGetProperty("outdated", out var oo) && oo.ValueKind == JsonValueKind.True,
                        StrOpt(payload, "min_version"), StrOpt(payload, "current_version"), StrOpt(payload, "message")));
                    continue;
                }

                var echoed = payload.TryGetProperty("nonce", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "";
                bool known;
                long beatSeq;
                lock (_nonceLock) known = _pendingNonces.Remove(echoed, out beatSeq);
                if (!known) continue;   // un "ok" non sollicite -> jamais faire confiance

                _beatCount++;
                ctx.HelloAcked = true;
                Interlocked.Exchange(ref ctx.LastReplyTick, Environment.TickCount64);
                NoteServerOk();
                var validUntil = OptLong(payload, "valid_until") ?? 0L;
                long? licExp = OptLong(payload, "license_expires_at");
                bool expiryChanged = ApplyExpiry(payload, "license_expires_at");
                // Le serveur joint l'objet license aux réponses hello/beat (lié au nonce, infalsifiable) : même parseur que
                // license_update / heartbeat HTTP.
                bool beatHasLicense = payload.TryGetProperty("license", out var licBeat) && licBeat.ValueKind == JsonValueKind.Object;
                if (beatHasLicense) ApplyLicenseUpdate(licBeat, OptLong(payload, "server_time") ?? 0);
                if (expiryChanged) PermissionsChanged?.Invoke();
                if (Environment.TickCount64 - Interlocked.Read(ref _lastUploadTick) > UploadRejectWindowMs)
                {
                    Interlocked.Exchange(ref _uploadRejects, 0);
                    Interlocked.Exchange(ref _transientRejects, 0);
                }
                if (firstTick)
                {
                    firstTick = false;
                    _uploadsReady = true;
                    Diag.Log(301, _wsConnectedOnce ? 1 : 0);
                    // RE-connexion : un license_update poussé pendant la coupure (0 socket côté serveur) serait perdu. La
                    // réponse au hello porte désormais l'objet license (appliqué ci-dessus) ; la resynchronisation HTTP ne
                    // part plus que face à un serveur qui ne le joint pas (compatibilité).
                    if (_wsConnectedOnce && !beatHasLicense) _ = RefreshLicenseAsync(ct);
                    _wsConnectedOnce = true;
                    _hbLinkFailures = 0; _hbIntegrityFailures = 0;
                    SetLinkDown(false);   // hello acquitté : le lien est revenu (s'il était perdu en repli HTTP)
                    ConnectionChanged?.Invoke(WsConnectionState.Connected);
                }
                // Trames parties avant ce hello/beat sur ce socket : reçues et traitées (ordre du fil + traitement séquentiel serveur).
                BeatConfirmed?.Invoke(ctx.Gen, beatSeq);
                Beat?.Invoke(new HeartbeatTick(_beatCount, validUntil, licExp));
            }
            else
            {
                // Frame d'erreur (push serveur, ou réponse à un beat / un envoi). Seuls les codes TERMINAUX (ban, clé révoquée/
                // expirée/en pause/verrouillée/invalide, HWID, session expirée) mettent fin à la session. Les autres
                // (VALIDATION_ERROR sur un envoi refusé, RATE_LIMITED…) ne ferment que CE socket côté serveur : on se
                // reconnecte. Avant, TOUTE frame non « ok » tuait la session -> « Clé de licence invalide » dès qu'un envoi
                // (télémétrie au lancement d'un compte) était refusé.
                var error = BuildError(payload) ?? new LicenseServerException("SESSION_EXPIRED", "Session expiree.");
                // SESSION_EXPIRED + lapsed:true : reprenable UNIQUEMENT si c'est la réponse à un hello/beat que NOUS venons
                // d'envoyer (nonce dans _pendingNonces). Une trame poussée (nonce null / inconnu : kick, pause, ban…) garde le
                // chemin terminal. Ni Terminating ni terminate_in ici : c'est la réponse au nouveau /auth qui décidera.
                if (error.Lapsed)
                {
                    var echoedErr = StrOpt(payload, "nonce");
                    bool solicited;
                    lock (_nonceLock) solicited = echoedErr is { Length: > 0 } && _pendingNonces.Remove(echoedErr);
                    if (solicited) { _abandoned = true; throw new LapsedSignal(error); }
                }
                if (!IsTerminal(error.Code))
                {
                    // UPLOAD_REJECTED { reason } (serveur ≥ 747e7c37) : not_ready / internal = transitoires (jamais comptés
                    // pour le coupe-circuit) ; channel/malformed/pubkey/signature/replay/drift… = bug ou horloge (comptés).
                    Diag.Log(306, Diag.Err(error.Code), Diag.Reason(error.Reason));
                    // Session liée à une AUTRE clé : un nouveau socket avec le même jeton n'y change rien -> nouvel /auth
                    // (LicenseService, une seule fois ; le client abandonné sort de la boucle de reconnexion).
                    if (error.Code == "UPLOAD_REJECTED" && error.Reason == "key_mismatch") KeyMismatch?.Invoke(10);
                    if (error.Reason is "not_ready" or "internal")
                    {
                        int n = Interlocked.Increment(ref _transientRejects);
                        long hold = Math.Min(1000L << Math.Min(n - 1, 5), 30_000);   // 1, 2, 4, 8, 16, 30 s
                        Interlocked.Exchange(ref _uploadHoldUntil, Environment.TickCount64 + hold);
                    }
                    else NoteSocketRejection(error.Code);   // toute autre raison (connue ou future) compte
                    throw new SocketRejectedSignal(error.Code);
                }
                MarkTerminal(error);   // dès maintenant : plus aucune reprise possible pendant terminate_in
                Terminating?.Invoke(error);
                if (error.TerminateInMs is long ms && ms > 0)
                {
                    try { await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(ms, 600_000)), ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
                ct.ThrowIfCancellationRequested();   // session remplacée/arrêtée pendant l'attente : pas de fin de session à signaler
                throw new TerminalSignal(error);
            }
        }
    }

    // http(s)://host/api/v1/client/ -> ws(s)://host/api/v1/client/ws
    private Uri BuildWebSocketUri()
    {
        var b = _http.BaseAddress!;
        return new UriBuilder(b)
        {
            Scheme = b.Scheme == "https" ? "wss" : "ws",
            Path = b.AbsolutePath.TrimEnd('/') + "/ws",
        }.Uri;
    }

    public void Dispose() => _http.Dispose();

    // ------------------------------------------------------------- interne --

    /// <summary>Nonce CSPRNG unique PAR requete : 32 octets => 64 hex. Rend chaque reponse non rejouable.</summary>
    private static string MakeNonce() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static long? OptLong(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var v) ? v : null;

    // max_level entier ≥ 0 ; absent / null / illisible = non réglé (null).
    private static int? ReadMaxLevel(JsonElement parent) =>
        parent.TryGetProperty("max_level", out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v) && v >= 0 ? v : null;

    // Booléen JSON strict (true/false) ; tout le reste (absent, null, "true", 1) = inconnu.
    private static bool? OptBool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var e) ? e.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } : null;

    private static int OptInt(JsonElement parent, string name, int fallback) =>
        parent.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v) ? v : fallback;

    private static string? StrOpt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    // Parse un tableau de droits signé [{name, expires_at(ms)}] (identique /auth, /heartbeat, push WS license_update).
    private static IReadOnlyList<PermissionInfo> ParsePermissions(JsonElement permsArray)
    {
        var list = new List<PermissionInfo>();
        if (permsArray.ValueKind == JsonValueKind.Array)
            foreach (var p in permsArray.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object) continue;   // élément malformé : ignoré (jamais une exception)
                var name = p.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new PermissionInfo(name!, OptLong(p, "expires_at")));
            }
        return list;
    }

    private static bool SamePermissions(IReadOnlyList<PermissionInfo> a, IReadOnlyList<PermissionInfo> b)
    {
        if (a.Count != b.Count) return false;
        var setB = b.Select(p => (p.Name, p.ExpiresAtMs)).ToHashSet();
        return a.All(p => setB.Contains((p.Name, p.ExpiresAtMs)));
    }

    /// <summary>Applique une mise à jour de licence reçue EN DIRECT (objet <c>license</c> du heartbeat, ou push WS
    /// <c>license_update</c>) : <c>{ status, expires_at, permissions:[{name,expires_at}] }</c>. Met <see cref="License"/>
    /// à jour et lève <see cref="PermissionsChanged"/> UNIQUEMENT si l'ensemble des droits a réellement changé (évite un
    /// rafraîchissement UI inutile à chaque beat de 60 s). PIÈGE nesting : ici <c>license.permissions</c> est À PLAT
    /// (le /auth, lui, les met sous <c>license.package.permissions</c>).</summary>
    private void ApplyLicenseUpdate(JsonElement license, long serverTimeMs = 0)
    {
        bool changed;
        try
        {
            lock (_licLock)
            {
                if (License is not { } cur) return;
                // Instantané plus ancien que le dernier appliqué (push hors d'ordre, rejeu d'un vieux push signé) : ignoré.
                if (serverTimeMs > 0)
                {
                    if (serverTimeMs < _lastLicenseServerTimeMs) return;
                    _lastLicenseServerTimeMs = serverTimeMs;
                }
                var updated = cur;
                if (license.TryGetProperty("permissions", out var perms) && perms.ValueKind == JsonValueKind.Array)
                {
                    var newPerms = ParsePermissions(perms);
                    if (!SamePermissions(newPerms, cur.Permissions)) updated = updated with { Permissions = newPerms };
                }
                if (TryReadExpiry(license, "expires_at", out var exp)) updated = updated with { ExpiresAtMs = exp };   // null = à vie (0)
                if (license.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String) updated = updated with { Status = st.GetString()! };
                // Licence gratuite / payante live (upgrade / downgrade) : à plat sous license ou dans package.
                if ((OptBool(license, "free") ?? (license.TryGetProperty("package", out var pkFree) && pkFree.ValueKind == JsonValueKind.Object ? OptBool(pkFree, "free") : null)) is bool fr)
                    updated = updated with { Free = fr };
                // Cap de niveau live : à plat sous license ou dans package. Un serveur qui envoie « free » envoie l'objet license
                // COMPLET : max_level ABSENT = non réglé (l'admin l'a vidé) -> retour au défaut. Serveur plus ancien (sans « free ») :
                // absent = inchangé.
                var pkMl = license.TryGetProperty("package", out var pkM) && pkM.ValueKind == JsonValueKind.Object ? pkM : default;
                int? liveMax = ReadMaxLevel(license) ?? (pkMl.ValueKind == JsonValueKind.Object ? ReadMaxLevel(pkMl) : null);
                bool fullObject = OptBool(license, "free") is not null;
                if (liveMax is not null || fullObject) updated = updated with { MaxLevel = liveMax };
                // Pack courant (change après un changement de pack / un upgrade). null = inconnu -> valeur actuelle gardée.
                if (license.TryGetProperty("package", out var pk) && pk.ValueKind == JsonValueKind.Object)
                {
                    if (pk.TryGetProperty("name", out var pn) && pn.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(pn.GetString()))
                        updated = updated with { PackageName = pn.GetString()! };
                    if (pk.TryGetProperty("duration_days", out var pd) && pd.ValueKind == JsonValueKind.Number && pd.TryGetInt32(out var dd))
                        updated = updated with { DurationDays = dd };
                }
                // Rafraîchissement UI/gardes dès que QUOI QUE CE SOIT d'utile change (avant : seulement l'ensemble des droits ->
                // une prolongation/un passage « à vie » ou un changement de cap restaient invisibles jusqu'au redémarrage).
                int flags = (!ReferenceEquals(updated.Permissions, cur.Permissions) ? 1 : 0) | (updated.ExpiresAtMs != cur.ExpiresAtMs ? 2 : 0)
                          | (!string.Equals(updated.Status, cur.Status, StringComparison.Ordinal) ? 4 : 0) | (updated.MaxLevel != cur.MaxLevel || updated.Free != cur.Free ? 8 : 0)
                          | (!string.Equals(updated.PackageName, cur.PackageName, StringComparison.Ordinal) || updated.DurationDays != cur.DurationDays ? 16 : 0);
                changed = flags != 0;
                if (changed) Diag.Log(402, updated.Permissions.Count, flags);   // n droits, quoi a changé (bits)
                License = updated;
            }
        }
        catch (Exception) { return; }   // un instantané malformé ne fait jamais tomber le socket ni le heartbeat
        if (changed) PermissionsChanged?.Invoke();
    }

    // Échéance de licence d'un payload : nombre = ms ; null EXPLICITE = à vie (0) ; absent/illisible = inchangée (false).
    private static bool TryReadExpiry(JsonElement parent, string name, out long expMs)
    {
        expMs = 0;
        if (!parent.TryGetProperty(name, out var e)) return false;
        if (e.ValueKind == JsonValueKind.Null) return true;
        return e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out expMs);
    }

    // Applique license_expires_at (beat WS / heartbeat HTTP). true si l'échéance a réellement changé.
    private bool ApplyExpiry(JsonElement payload, string name)
    {
        if (!TryReadExpiry(payload, name, out var exp)) return false;
        lock (_licLock)
        {
            if (License is not { } cur || cur.ExpiresAtMs == exp) return false;
            License = cur with { ExpiresAtMs = exp };
            return true;
        }
    }

    // Statuts qui coupent tous les droits même sans frame terminale (liste NOIRE : un statut futur inconnu reste permissif).
    private static bool IsBlockedStatus(string? status) =>
        status is not null && (status.Equals("paused", StringComparison.OrdinalIgnoreCase) || status.Equals("revoked", StringComparison.OrdinalIgnoreCase)
                               || status.Equals("expired", StringComparison.OrdinalIgnoreCase) || status.Equals("banned", StringComparison.OrdinalIgnoreCase));

    // Resynchronisation UNIQUE des droits après une reconnexion WS (POST /heartbeat signé). Jamais terminale ici : une erreur
    // est laissée au WS / au heartbeat, qui la traitent déjà.
    private async Task RefreshLicenseAsync(CancellationToken ct)
    {
        try
        {
            if (_token is null) return;
            var payload = await PostSignedAsync("heartbeat", w =>
            {
                w.WriteString("token", _token!);
                w.WriteString("hwid", _hwid);
            }, ct).ConfigureAwait(false);
            if (BuildError(payload) is not null) { Diag.Log(401, 0); return; }
            Diag.Log(401, 1);
            bool expiry = ApplyExpiry(payload, "license_expires_at");
            if (payload.TryGetProperty("license", out var lic) && lic.ValueKind == JsonValueKind.Object)
                ApplyLicenseUpdate(lic, OptLong(payload, "server_time") ?? 0);
            if (expiry) PermissionsChanged?.Invoke();
        }
        catch { /* best-effort : les beats suivants et le repli HTTP restent en place */ }
    }

    // true si ce nonce de commande n'a pas encore été vu (fenêtre 10 min).
    private bool FirstSeenCommand(string nonce)
    {
        long now = Environment.TickCount64;
        lock (_seenCommands)
        {
            if (_seenCommands.Count > 256)
                foreach (var k in _seenCommands.Where(kv => now - kv.Value > 600_000).Select(kv => kv.Key).ToList()) _seenCommands.Remove(k);
            return _seenCommands.TryAdd(nonce, now);
        }
    }

    // /auth et /mapping : SIGNÉS quand le serveur l'annonce (client_signing dans /version), sinon en clair comme 1.1.x.
    // Refus de signature : « replay » -> UN nouvel essai (nouveau nonce) ; « drift » -> recalage de l'horloge (fait à la
    // vérification) puis UN nouvel essai si retryGate l'autorise ; tout autre motif -> aucun nouvel essai, l'erreur remonte
    // (jamais de repli en clair). Chaque essai repasse par PostSignedAsync : nouveau nonce, nouvelle signature.
    private async Task<JsonElement> PostRequestAsync(string path, Action<Utf8JsonWriter> writeBody, CancellationToken ct, Func<bool>? retryGate = null,
                                                     System.Runtime.CompilerServices.StrongBox<bool>? sentSigned = null)
    {
        if (!RequestSigning.Enabled)
        {
            var clear = await PostSignedAsync(path, writeBody, ct).ConfigureAwait(false);
            // Signature EXIGÉE alors qu'on ne l'avait pas encore vue annoncée (réglage Off -> mapping/all en cours de session) :
            // on passe en signé et on refait la requête signée (une session non liée recevra alors key_mismatch -> /auth signé).
            if (!IsSignatureRejected(clear, out var r0) || r0 != "unsigned") return clear;
            RequestSigning.EnableFromRefusal();
        }
        if (sentSigned is not null) sentSigned.Value = true;
        bool replayRetried = false, driftRetried = false;
        long ep = Diag.Ep(path);
        while (true)
        {
            var payload = await PostSignedAsync(path, writeBody, ct, signRequest: true).ConfigureAwait(false);
            if (!IsSignatureRejected(payload, out var reason)) return payload;
            Diag.Log(205, ep, Diag.Reason(reason), (replayRetried ? 1 : 0) + (driftRetried ? 2 : 0));
            if (reason == "replay" && !replayRetried) { replayRetried = true; continue; }
            if (reason == "drift" && !driftRetried && (retryGate?.Invoke() ?? true)) { driftRetried = true; continue; }
            // Session liée à une AUTRE clé (/mapping) : procédure de reprise pilotée par LicenseService (un seul /auth).
            if (reason == "key_mismatch" && path != "auth") KeyMismatch?.Invoke((int)ep);
            // 2e rejeu = traité comme une signature refusée ; 2e dérive = horloge du PC fausse.
            string final = reason == "replay" ? "signature" : reason;
            throw new LicenseServerException("SIGNATURE_REJECTED", "Request signature rejected.", final) { ClockWrong = reason == "drift" && driftRetried };
        }
    }

    // Corps ecrit a la main (AOT-safe) : l'appelant ecrit ses champs, on ajoute le nonce et on scelle.
    // signRequest : payload { champs, nonce, pubkey, server_time } signé par la clé cliente, envoyé SEUL sous
    // { "envelope": { "payload": b64, "signature": b64 } } (aucun autre champ de premier niveau, jamais re-sérialisé).
    private async Task<JsonElement> PostSignedAsync(string path, Action<Utf8JsonWriter> writeBody, CancellationToken ct, bool signRequest = false)
    {
        var nonce = MakeNonce();

        using var ms = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            writeBody(w);
            w.WriteString("nonce", nonce);
            if (signRequest)
            {
                w.WriteString("pubkey", ClientIdentity.PubHex);
                w.WriteNumber("server_time", ServerClock.NowMs());
            }
            w.WriteEndObject();
        }
        byte[] body = ms.ToArray();
        if (signRequest)
        {
            var signature = ClientIdentity.Sign(body);   // octets EXACTS du payload
            using var env = new System.IO.MemoryStream();
            using (var w = new Utf8JsonWriter(env))
            {
                w.WriteStartObject();
                w.WriteStartObject("envelope");
                w.WriteString("payload", Convert.ToBase64String(body));
                w.WriteString("signature", Convert.ToBase64String(signature));
                w.WriteEndObject();
                w.WriteEndObject();
            }
            body = env.ToArray();
        }

        // Jeton abandonné (reprise en cours) : plus aucune requête avec lui. EnvelopeException = échec TRANSITOIRE pour tous
        // les appelants (scripts remis en file, mapping retenté) — jamais un kill. /auth passe (il ne porte pas de jeton).
        if (_abandoned && path != "auth") throw new EnvelopeException("session abandonnee");
        string raw;
        long ep = Diag.Ep(path), t0 = Environment.TickCount64;
        int http = 0;
        try
        {
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            // On lit le corps meme en HTTP 4xx : les erreurs metier arrivent dans la MEME enveloppe signee.
            using var response = await _http.PostAsync(path, content, ct).ConfigureAwait(false);
            http = (int)response.StatusCode;
            raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        // Toute erreur d'envoi / de lecture (réseau, délai, charset inconnu…) = échec RÉSEAU, jamais une exception imprévue qui
        // sortirait des boucles de battement (seul l'arrêt demandé remonte tel quel).
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Diag.Log(203, ep, Diag.Ex(e), Environment.TickCount64 - t0);   // réseau / délai (jamais le message)
            throw new EnvelopeException("reseau : " + e.Message);
        }

        JsonElement verified;
        try { verified = VerifyEnvelope(raw, nonce, signRequest); }
        catch (EnvelopeException e)
        {
            Diag.Log(202, ep, http, Diag.Env(e.Message), Environment.TickCount64 - t0);   // enveloppe rejetée (signature/horloge/nonce/format)
            throw;
        }
        if (_abandoned && path != "auth") throw new EnvelopeException("session abandonnee");   // réponse liée à l'ancien jeton : ignorée
        long ms2 = Environment.TickCount64 - t0;
        if (BuildError(verified) is { } err)
        {
            Diag.Log(201, ep, http, Diag.Err(err.Code), ms2);
            // Réponse à NOTRE requête (nonce vérifié par VerifyEnvelope) marquée lapsed -> reprise. Jamais pour /auth ni /logout.
            // Jamais non plus quand une fin de session voulue est déjà en cours sur ce client (kick en attente de terminate_in).
            if (err.Lapsed && TerminalError is null && path is not ("auth" or "logout")) Lapsed?.Invoke((int)ep);
        }
        else
        {
            NoteServerOk();
            if (ep == 2) Diag.LogThrottled(200, ep, 600_000, ep, http, ms2);   // heartbeat HTTP : 1 ligne « ok » / 10 min
            else Diag.Log(200, ep, http, ms2);
        }
        return verified;
    }

    // Verif d'enveloppe OBLIGATOIRE (signature + server_time), puis echo du nonce (anti-rejeu HTTP).
    // Requête SIGNÉE, deux exceptions (contrat serveur) :
    //  - SIGNATURE_REJECTED « drift » signé ET à nonce en ÉCHO EXACT : accepté même hors ±5 min (un nonce neuf ne peut pas être
    //    un rejeu) -> recalage de l'horloge serveur estimée ;
    //  - nonce null : accepté SEULEMENT pour SIGNATURE_REJECTED « malformed » (après signature + fraîcheur) ; tout autre nonce
    //    null = réponse invalide, traitée comme une erreur RÉSEAU (message sans « nonce/signature/server_time »).
    private JsonElement VerifyEnvelope(string raw, string expectedNonce, bool signedRequest = false)
    {
        var payload = ParseVerified(raw, out long serverTimeMs);
        bool hasNonce = payload.TryGetProperty("nonce", out var n);
        bool echoed = hasNonce && n.ValueKind == JsonValueKind.String
                      && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(n.GetString()!), Encoding.ASCII.GetBytes(expectedNonce));
        if (signedRequest && echoed && IsSignatureRejected(payload, out var why) && why == "drift")
        {
            ServerClock.Calibrate(serverTimeMs);
            Diag.Log(206, ServerClock.OffsetMs / 1000);
            return payload;
        }
        // Réponse à NOTRE nonce, fraîche selon l'horloge BRUTE du PC alors qu'un recalage est actif : l'horloge a été corrigée
        // depuis -> le décalage est recalculé (sinon plus aucune réponse ne passerait et la session finirait « date/heure »).
        if (Math.Abs(serverTimeMs - ServerClock.NowMs()) >= MaxClockSkewMs && !(echoed && TryResyncClock(serverTimeMs)))
            throw new EnvelopeException("server_time hors tolerance (5 min) : horloge locale ou rejeu");
        if (echoed) return payload;
        if (signedRequest && (!hasNonce || n.ValueKind == JsonValueKind.Null))
        {
            if (IsSignatureRejected(payload, out var r2) && r2 == "malformed") { Diag.Log(207, 1); return payload; }
            Diag.Log(207, 0);
            throw new EnvelopeException("reseau : reponse non liee a la requete");
        }
        throw new EnvelopeException("nonce inattendu (rejeu possible)");
    }

    // Recalage périmé : seulement sur une réponse LIÉE à une requête qu'on vient d'envoyer (nonce neuf en écho : ni rejeu ni
    // trame poussée) ET dans ±5 min de l'horloge brute du PC — exactement la règle d'avant tout recalage, rien de plus vieux.
    private static bool TryResyncClock(long serverTimeMs)
    {
        if (ServerClock.OffsetMs == 0) return false;
        if (Math.Abs(serverTimeMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) >= MaxClockSkewMs) return false;
        ServerClock.Calibrate(serverTimeMs);
        Diag.Log(206, ServerClock.OffsetMs / 1000);
        return true;
    }

    // SIGNATURE_REJECTED ? + motif normalisé (motif absent / inconnu -> « malformed », contrat serveur).
    private static bool IsSignatureRejected(JsonElement payload, out string reason)
    {
        reason = "";
        if (StrOpt(payload, "status") == "ok" || StrOpt(payload, "code") != "SIGNATURE_REJECTED") return false;
        reason = StrOpt(payload, "reason") switch
        {
            "unsigned" => "unsigned", "malformed" => "malformed", "pubkey" => "pubkey", "signature" => "signature",
            "key_mismatch" => "key_mismatch", "drift" => "drift", "replay" => "replay", _ => "malformed",
        };
        return true;
    }

    // Enveloppe WS / HTTP non signée : fraîcheur stricte (±5 min autour de l'horloge serveur estimée).
    private JsonElement VerifyAndParse(string raw)
    {
        var payload = ParseVerified(raw, out long serverTimeMs);
        if (Math.Abs(serverTimeMs - ServerClock.NowMs()) >= MaxClockSkewMs)
            throw new EnvelopeException("server_time hors tolerance (5 min) : horloge locale ou rejeu");
        return payload;
    }

    // Signature Ed25519 + JSON objet + server_time entier. Toute anomalie -> EnvelopeException (jamais une exception imprévue).
    private JsonElement ParseVerified(string raw, out long serverTimeMs)
    {
        JsonElement envelope;
        try { using var doc = JsonDocument.Parse(raw); envelope = doc.RootElement.Clone(); }
        catch (JsonException) { throw new EnvelopeException("enveloppe invalide"); }
        // [], null, 1, "x" : TryGetProperty lèverait InvalidOperationException (imprévue -> sortie des boucles de battement).
        if (envelope.ValueKind != JsonValueKind.Object) throw new EnvelopeException("enveloppe invalide");

        if (!envelope.TryGetProperty("payload", out var p) || !envelope.TryGetProperty("signature", out var s) ||
            p.ValueKind != JsonValueKind.String || s.ValueKind != JsonValueKind.String)
            throw new EnvelopeException("enveloppe invalide");

        byte[] payloadBytes, signature;
        try { payloadBytes = Convert.FromBase64String(p.GetString()!); signature = Convert.FromBase64String(s.GetString()!); }
        catch (FormatException) { throw new EnvelopeException("base64 invalide"); }

        if (signature.Length != 64) throw new EnvelopeException("longueur de signature invalide");

        // Signature Ed25519 detachee sur les octets EXACTS du payload, cle publique embarquee.
        bool ok;
        try { ok = Cryptographic.Ed25519.CheckValid(signature, payloadBytes, _publicKey); }
        catch { ok = false; }   // point hors courbe, etc. => rejet
        if (!ok) throw new EnvelopeException("signature invalide");

        JsonElement payload;
        try { using var doc = JsonDocument.Parse(payloadBytes); payload = doc.RootElement.Clone(); }
        catch (JsonException) { throw new EnvelopeException("payload JSON invalide"); }
        if (payload.ValueKind != JsonValueKind.Object) throw new EnvelopeException("payload JSON invalide");

        if (!payload.TryGetProperty("server_time", out var st) || st.ValueKind != JsonValueKind.Number || !st.TryGetInt64(out serverTimeMs))
            throw new EnvelopeException("server_time manquant");

        return payload;
    }

    private static void ThrowIfError(JsonElement payload)
    {
        var error = BuildError(payload);
        if (error is not null) throw error;
    }

    private static LicenseServerException? BuildError(JsonElement payload)
    {
        if (StrOpt(payload, "status") == "ok") return null;

        var code = StrOpt(payload, "code") ?? "ERROR";
        var message = StrOpt(payload, "message") ?? "";
        var reason = StrOpt(payload, "reason");
        long? terminateInMs = OptLong(payload, "terminate_in");
        // lapsed : seul le booléen JSON true compte ("true", 1… -> non), et seulement sur SESSION_EXPIRED.
        bool lapsed = code == "SESSION_EXPIRED" && payload.TryGetProperty("lapsed", out var lp) && lp.ValueKind == JsonValueKind.True;
        return new LicenseServerException(code, message, reason) { TerminateInMs = terminateInMs, Lapsed = lapsed };
    }
}
