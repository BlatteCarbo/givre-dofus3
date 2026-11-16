using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Engine;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// ===================== Lien de jeu MITM : session perdue alors que Dofus.exe reste ouvert =====================
// Sans ce suivi, un bot MITM expulsé par le serveur (inactivité, modération, maintenance) restait « En jeu » avec sa dernière
// carte : le process vit, et rien ne purgeait l'état de session (seuls StopCore / la boucle socket appellent Forget). Deux couches :
//  - A (explicite) : SystemMessageDisplayEvent avec hang_up reçu du serveur -> coupure IMPOSÉE, raison connue (message 1 = inactivité).
//  - B (filet) : fermeture de la connexion relayée COURANTE d'un pid qui avait une session de jeu VIVANTE (HandshakeRelay.OnClosed).
//    Fermée d'abord côté serveur -> coupure. Fermée d'abord par le client (changement de perso, retour à la sélection du serveur)
//    ou transfert de serveur -> coupure seulement si aucune nouvelle session ne démarre dans la minute.
// L'identité (perso, serveur, WhoAmI, niveau) est GARDÉE : seule la session (carte, combat, fenêtres, attentes) est oubliée. Les
// bots SOCKET ne passent par les couches A/B que pour noter le hang_up : leur boucle de session gère déjà la coupure (Forget +
// reconnexion / abandon) -> aucun double traitement. Ils n'entrent ici que par la coupure TENUE par l'app (HoldLinkDown).
// Coupure TENUE (Held) : posée par l'app, pas par le réseau — session socket PARQUÉE après une déconnexion imposée par le serveur
// (aucune reconnexion automatique, « Reconnecter » la réveille), ou ancien pid MITM pendant la relance « Reconnecter » en place.
// Tous les consommateurs de IsLinkDown / IsLinkSuspended la voient comme un « Déconnecté » MITM (planning, équipe, injections,
// StuckStop, getStatus) ; ni GameLinkLosing ni GameLinkLost (les scripts du pid sont arrêtés explicitement AVANT). Une trame
// tardive ne la lève jamais (MarkSessionLive) : seuls ReleaseLinkDown et Forget la lèvent.
public sealed partial class LaunchService
{
    /// <summary>Coupure du lien de jeu d'un pid MITM : raison localisée, imposée par le serveur (hang_up), fermée côté serveur
    /// (false = le client a quitté la session sans y revenir à temps), instant UTC. Held : coupure TENUE par l'app
    /// (<see cref="HoldLinkDown"/>) ; Relaunching : ancien pid d'un client MITM en cours de relance (activité « Connexion »).</summary>
    public sealed record GameLinkDown(string Reason, bool Imposed, bool ByServer, DateTime AtUtc, bool Held = false, bool Relaunching = false);

    /// <summary>Session de jeu PERDUE alors que Dofus.exe reste ouvert (MITM) : (pid, raison localisée, imposée par le serveur).
    /// Levé depuis le thread réseau / une minuterie : l'abonné UI marshale. Jamais pour un bot socket.</summary>
    public event Action<uint, string, bool>? GameLinkLost;

    /// <summary>Une nouvelle session de jeu a démarré sur un pid dont le lien était coupé (MITM) : injections réautorisées,
    /// l'activité repart de « Connexion ». Thread réseau.</summary>
    public event Action<uint>? GameLinkRestored;

    /// <summary>Lien de jeu d'un pid MITM en train de tomber : (pid, raison localisée). Levé SYNCHRONEMENT sur le thread qui
    /// détecte la coupure, juste après la pose de la coupure et AVANT l'oubli de session (attentes de déplacement / de paquet
    /// rendues) : un script arrêté ici voit l'annulation, jamais un verbe revenu en échec. Abonnés RAPIDES, thread-safe, jamais
    /// bloquants (appelés sous le verrou de l'état du lien). Jamais pour un bot socket.</summary>
    public event Action<uint, string>? GameLinkLosing;

    // Transitions de l'état « lien de jeu » d'un pid (session vivante, attente de retour en jeu, coupure + oubli de session) :
    // UN verrou, pour qu'une minuterie de retour en jeu ou une fermeture de relais ne coupe jamais une session qui vient de
    // (re)démarrer sur un autre thread. Activité / GameLinkLost / GameLinkRestored levés APRÈS sa libération ; seul
    // GameLinkLosing l'est dedans (abonnés non bloquants : ils annulent un CTS).
    private readonly object _linkGate = new();
    private readonly ConcurrentDictionary<uint, GameLinkDown> _linkDown = new();
    private readonly ConcurrentDictionary<uint, byte> _sessionLive = new();                  // session de jeu VIVANTE (ticket accepté / carte reçue)
    private readonly ConcurrentDictionary<uint, (int MsgId, long At)> _hangUpSeen = new();   // dernier hang_up reçu (socket compris : statut de la ligne)
    private readonly ConcurrentDictionary<uint, long> _serverTransferAt = new();             // demande de transfert vers un autre serveur de jeu
    private readonly ConcurrentDictionary<uint, byte> _expectedRelogin = new();              // ReloginRequest injecté : fermeture, refus de ticket, écran de sélection
    private readonly ConcurrentDictionary<uint, int> _reloginEpoch = new();                  // sessions de jeu acceptées pendant un relogin attendu
    private readonly ConcurrentDictionary<uint, long> _reloginWatch = new();                 // jeton du délai qui ferme la socket de jeu d'origine
    private long _reloginWatchSeq;
    private LocalLaunch.LocalHandshakeRelay? _localRelay;
    private readonly ConcurrentDictionary<uint, long> _returnWait = new();                   // pid -> jeton de l'attente « le client revient-il en jeu ? »
    private long _returnWaitSeq;
    private const long HangUpHandledMs = 5000;          // fermeture < 5 s après un hang_up = la même coupure (déjà traitée)
    private const long ServerTransferGraceMs = 10_000;  // fermeture < 10 s après une demande de transfert = changement de serveur
    private const int ReturnToGameGraceMs = 60_000;     // client qui quitte la session : délai de retour en jeu avant « Déconnecté »

    /// <summary>Lien de jeu coupé (MITM) ? Vrai jusqu'au démarrage d'une nouvelle session de jeu (ou l'arrêt du client).</summary>
    public bool IsLinkDown(uint pid) => _linkDown.ContainsKey(pid);

    /// <summary>Relogin MITM en cours sur ce process. La fermeture du lien n'est pas une déconnexion.</summary>
    public bool IsExpectedRelogin(uint pid) => _expectedRelogin.ContainsKey(pid);

    /// <summary>Nombre de tickets acceptés pendant un relogin attendu. 0 avant le premier.</summary>
    public int ReloginEpoch(uint pid) => _reloginEpoch.TryGetValue(pid, out int n) ? n : 0;

    /// <summary>Marque le lien : le client va fermer sa connexion de jeu et revenir, sans mourir.</summary>
    public void BeginExpectedRelogin(uint pid)
    {
        if (pid == 0 || IsSocketPid(pid)) return;
        NativeTurboRuntime.RevokeTacticalMapReady((int)pid);
        _expectedRelogin[pid] = 0;
        _localRelay?.ArmRelogin(pid);
        TransportLog.Line(pid, $"[jeu] pid {pid} : relogin attendu — Dofus.exe reste ouvert ; une fermeture de connexion, un refus de ticket ou l'écran de sélection ne sont pas une déconnexion");
        ScheduleReloginClose(pid, MitmRelogin.TokenSilenceMs, $"ReloginRequest sans ReloginTokenEvent après {MitmRelogin.TokenSilenceMs} ms");
    }

    /// <summary>Fin du relogin attendu (personnage en jeu, délai dépassé, ou annulation).</summary>
    public void EndExpectedRelogin(uint pid)
    {
        CancelReloginWatch(pid);
        _localRelay?.DisarmRelogin(pid);
        if (!_expectedRelogin.TryRemove(pid, out _)) return;
        lock (_linkGate) _returnWait.TryRemove(pid, out _);
    }

    /// <summary>ReloginTokenEvent vu sur le fil. Le jeton n'est pas lu, stocké, ni journalisé.</summary>
    private void NoteReloginToken(uint pid)
    {
        if (pid == 0 || !_expectedRelogin.ContainsKey(pid)) return;
        TransportLog.Line(pid, $"[jeu] pid {pid} : ReloginTokenEvent reçu (jeton non journalisé) — attente de la fermeture client");
        ScheduleReloginClose(pid, MitmRelogin.ClientCloseAfterTokenMs, $"ReloginTokenEvent sans fermeture client après {MitmRelogin.ClientCloseAfterTokenMs} ms");
    }

    private void CancelReloginWatch(uint pid) => _reloginWatch.TryRemove(pid, out _);

    private void ScheduleReloginClose(uint pid, int delayMs, string why)
    {
        long token = Interlocked.Increment(ref _reloginWatchSeq);
        _reloginWatch[pid] = token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
                if (!_reloginWatch.TryGetValue(pid, out var current) || current != token) return;
                if (!MitmReloginWatch.ShouldForceClose(_expectedRelogin.ContainsKey(pid), _localRelay?.GameSocketReplaced(pid) == true))
                    return;
                bool closed = _localRelay?.CloseGameClient(pid) == true;
                TransportLog.Line(pid, closed
                    ? $"[jeu] pid {pid} : {why} — connexion de jeu fermée pour forcer la reconnexion du même process"
                    : $"[jeu] pid {pid} : {why} — pas de connexion de jeu à fermer");
            }
            catch (Exception e) { TransportLog.Line(pid, $"[jeu] pid {pid} : fermeture de relogin : {e.Message}"); }
        });
    }

    // Première socket après l'armement : le même compte suit la reconnexion. L'ancienne carte ne doit pas
    // faire croire que le perso cible est déjà en jeu (sinon l'auto-pick se tait).
    private void OnReloginConnectionOpened(uint pid)
    {
        if (!_expectedRelogin.ContainsKey(pid)) return;
        _clientSelectedAt.TryRemove(pid, out _);
        _characterListAt.TryRemove(pid, out _);
        lock (_linkGate)
        {
            if (!_expectedRelogin.ContainsKey(pid)) return;
            _sessionLive.TryRemove(pid, out _);
            long token = Interlocked.Increment(ref _returnWaitSeq);
            _returnWait[pid] = token;
            ForgetSession(pid);
            _charId.TryRemove(pid, out _);
            _selfInfo.TryRemove(pid, out _);
        }
        TransportLog.Line(pid, $"[jeu] pid {pid} : reconnexion de relogin — la liste de personnages reste sur le même compte");
    }

    /// <summary>Lien de jeu SUSPENDU (MITM) : coupé, OU client sorti de la session de jeu et attendu en jeu (délai de grâce
    /// avant « Déconnecté »). Injections refusées ; un échec d'action n'y est pas un blocage. Jamais vrai pour un bot socket.</summary>
    public bool IsLinkSuspended(uint pid) => _linkDown.ContainsKey(pid) || _returnWait.ContainsKey(pid);

    /// <summary>Détail de la coupure en cours, ou null.</summary>
    public GameLinkDown? LinkDownOf(uint pid) => _linkDown.TryGetValue(pid, out var d) ? d : null;

    /// <summary>Raison localisée d'un hang_up serveur reçu il y a moins de <paramref name="withinMs"/> ms (« inactivité » /
    /// « message système N »), ou null. Vaut aussi pour un bot socket (statut de fin de session) : à lire AVANT Forget.</summary>
    public string? RecentHangUpReason(uint pid, long withinMs = 30_000)
        => _hangUpSeen.TryGetValue(pid, out var h) && Environment.TickCount64 - h.At <= withinMs ? HangUpReason(h.MsgId) : null;

    private static string HangUpReason(int msgId) => msgId == 1 ? Loc.T("L.Link.Inactivity") : Loc.Fmt("L.Link.SystemMessage", msgId);

    // Couche A (thread réseau, via OnLotEPacket) : message système du serveur. Champs lus PAR NOM (repli = numéros du mapping courant).
    private void OnServerSystemMessage(DecodedPacket p)
    {
        bool hang = PayloadReader.Bool(p.Payload, FNum("SystemMessageDisplayEvent", "hang_up"));
        int msgId = (int)PayloadReader.Int(p.Payload, FNum("SystemMessageDisplayEvent", "message_id"), 0);
        bool expectedRelogin = hang && !IsSocketPid(p.Pid) && _expectedRelogin.ContainsKey(p.Pid);
        if (hang && !expectedRelogin) _hangUpSeen[p.Pid] = (msgId, Environment.TickCount64);
        if (IsSocketPid(p.Pid))
        {
            // Socket : SocketGameClient journalise et termine la session lui-même. Coupure TENUE dès le hang_up : d'ici à ce que la
            // boucle de session parque le bot, un échec d'action du script n'est pas un blocage (ni « stuck » ni pause du
            // planning). La boucle la lève (Forget) puis la repose en parquant la session.
            if (hang) HoldLinkDown(p.Pid, HangUpReason(msgId), imposed: true, byServer: true);
            return;
        }
        if (expectedRelogin)
        {
            TransportLog.Line(p.Pid, $"[jeu] pid {p.Pid} : message système {msgId} pendant un relogin attendu — pas une déconnexion (le client peut réessayer)");
            return;
        }
        TransportLog.Line(p.Pid, $"[jeu] pid {p.Pid} : message système {msgId}" + (hang ? " — DÉCONNEXION IMPOSÉE par le serveur" : ""));
        if (hang) MarkGameLinkDown(p.Pid, HangUpReason(msgId), imposed: true, byServer: true);
    }

    // Transfert vers un autre serveur de jeu demandé (thread réseau) : la fermeture de connexion qui suit n'est pas une coupure.
    private void NoteServerTransfer(uint pid)
    {
        NativeTurboRuntime.RevokeTacticalMapReady((int)pid);
        if (!IsSocketPid(pid)) _serverTransferAt[pid] = Environment.TickCount64;
    }

    // Acceptation du ticket par le serveur de jeu (thread réseau) : une NOUVELLE session de jeu démarre sur ce pid.
    private void OnGameSessionStarted(uint pid)
    {
        if (IsSocketPid(pid)) return;
        NativeTurboRuntime.RevokeTacticalMapReady((int)pid);
        if (_expectedRelogin.ContainsKey(pid))
            _reloginEpoch.AddOrUpdate(pid, 1, static (_, n) => n + 1);
        MarkSessionLive(pid, "nouvelle session de jeu");
    }

    // Carte de jeu reçue (thread réseau) : session vivante. Filet si l'acceptation du ticket n'a pas été décodée.
    private void OnGameMapLoaded(uint pid)
    {
        if (IsSocketPid(pid)) return;
        FinishCharacterLoad(pid);
        _firstMapWait.TryRemove(pid, out _);
        MarkSessionLive(pid, "carte de jeu reçue");
    }

    // Client MITM bloqué au chargement : le serveur a confirmé la sélection (fiche, inventaire) mais Dofus.exe
    // n'a pas envoyé ContextCreationRequest / MapInformationRequest. L'horloge part de la sélection, pas du ticket :
    // 2026-11-01 02:51:41 a crié au blocage alors que ContextCreationRequest était déjà parti (02:51:38) et la carte
    // est arrivée à 02:51:45. L'écran de sélection, avant toute sélection, n'est pas un blocage.
    private readonly ConcurrentDictionary<uint, long> _firstMapWait = new();
    private readonly ConcurrentDictionary<uint, long> _contextAt = new();
    private long _firstMapSeq;

    private void NoteCharacterContext(uint pid)
    {
        if (pid == 0 || IsSocketPid(pid)) return;
        _contextAt[pid] = Environment.TickCount64;
    }

    private void WatchCharacterLoad(uint pid)
    {
        long token = Interlocked.Increment(ref _firstMapSeq);
        _firstMapWait[pid] = token;
        long selectedAt = Environment.TickCount64;
        _contextAt.TryRemove(pid, out _);
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                    if (!_firstMapWait.TryGetValue(pid, out var current) || current != token) return;
                    if (_linkDown.ContainsKey(pid) || _expectedRelogin.ContainsKey(pid)) return;
                    if (CurrentMap(pid) is > 0)
                    {
                        _firstMapWait.TryRemove(new KeyValuePair<uint, long>(pid, token));
                        return;
                    }
                    long? context = _contextAt.TryGetValue(pid, out var at) ? at : null;
                    if (!CharacterLoadWatch.DeclareStall(selectedAt, context, mapLoaded: false, Environment.TickCount64))
                        continue;
                    if (!_firstMapWait.TryRemove(new KeyValuePair<uint, long>(pid, token))) return;
                    TransportLog.Line(pid, $"[jeu] pid {pid} : CLIENT DOFUS BLOQUÉ AU CHARGEMENT — sélection confirmée mais le client n'a pas demandé sa carte " +
                                           "(aucune trame à relayer : côté jeu, pas Givre). Relancer le client si l'écran de chargement ne se termine pas.");
                    return;
                }
            }
            catch (Exception e) { TransportLog.Line(pid, $"[jeu] pid {pid} : veille de chargement : {e.Message}"); }
        });
    }

    // Session de jeu VIVANTE (sous _linkGate) : le client est revenu en jeu -> pas de « Déconnecté » (attente de retour
    // annulée), et une coupure en cours est levée (injections réautorisées, activité recalculée : « Connexion » puis « En jeu »).
    // Coupure TENUE par l'app : jamais levée ici — une trame tardive du client tué pendant une relance MITM en place (le relais
    // décode encore après Process.Kill) rendrait sinon la main au planning / aux scripts sur un pid mort.
    private void MarkSessionLive(uint pid, string why)
    {
        bool restored;
        lock (_linkGate)
        {
            if (_linkDown.TryGetValue(pid, out var held) && held.Held) return;
            _returnWait.TryRemove(pid, out _);
            _sessionLive[pid] = 0;
            restored = _linkDown.TryRemove(pid, out _);
        }
        if (!restored) return;
        TransportLog.Line(pid, $"[jeu] pid {pid} : {why} -> lien de jeu rétabli, injections réautorisées");
        RaiseActivity(pid);
        GameLinkRestored?.Invoke(pid);
    }

    // Couche B (thread du relais) : une connexion relayée du pid s'est fermée.
    private void OnRelayClosed(uint pid, bool wasCurrent, bool serverClosedFirst)
    {
        if (IsSocketPid(pid)) return;
        // Plus de connexion courante : un reste de trame à demi reçu ne doit pas se recoller au début de la connexion suivante.
        if (wasCurrent) { _decoder?.Forget(pid); _deviceObserver?.Forget(pid); }   // fin de session : re-observer le device_identifier au prochain login (même pid)
        if (!wasCurrent) return;
        string? downReason = null;
        long token = 0;
        bool transfer = false;
        bool expectedRelogin = false;
        lock (_linkGate)
        {
            NativeTurboRuntime.RevokeTacticalMapReady((int)pid);
            // Seule la connexion COURANTE d'une session de jeu VIVANTE compte : la connexion au serveur de connexion se ferme
            // avant toute session (login), et une ancienne connexion fermée APRÈS l'ouverture d'une nouvelle n'est pas une coupure.
            if (!_sessionLive.TryRemove(pid, out _)) return;
            long now = Environment.TickCount64;
            if (_expectedRelogin.ContainsKey(pid))
            {
                // Relogin injecté : le client ferme lui-même. Pas de « Déconnecté », pas de relance crash.
                expectedRelogin = true;
                token = Interlocked.Increment(ref _returnWaitSeq);
                _returnWait[pid] = token;
                ForgetSession(pid);
                _charId.TryRemove(pid, out _);
                _selfInfo.TryRemove(pid, out _);
                _clientSelectedAt.TryRemove(pid, out _);
                _characterListAt.TryRemove(pid, out _);
            }
            else if (_hangUpSeen.TryGetValue(pid, out var h) && now - h.At <= HangUpHandledMs) return;   // déjà traité par la couche A
            else
            {
                transfer = _serverTransferAt.TryGetValue(pid, out var tr) && now - tr <= ServerTransferGraceMs;
                if (serverClosedFirst && !transfer)
                {
                    string reason = Loc.T("L.Link.Closed");
                    if (SetLinkDownLocked(pid, reason, imposed: false, byServer: true)) downReason = reason;
                }
                else
                {
                    // Le client a fermé (changement de perso, retour à la sélection du serveur) ou transfert de serveur : pas de
                    // coupure tout de suite. « Déconnecté » seulement si aucune nouvelle session ne démarre d'ici ReturnToGameGraceMs.
                    token = Interlocked.Increment(ref _returnWaitSeq);
                    _returnWait[pid] = token;
                }
            }
        }
        if (expectedRelogin)
        {
            CancelReloginWatch(pid);
            _localRelay?.NoteReloginLinkClosed(pid);
            TransportLog.Line(pid, $"[jeu] pid {pid} : relogin attendu — connexion de jeu fermée par le client, même process, pas une déconnexion");
            RaiseActivity(pid);
            return;
        }
        if (downReason is not null) { AnnounceLinkDown(pid, downReason, imposed: false); return; }
        if (token == 0) return;   // déjà coupé
        TransportLog.Line(pid, $"[jeu] pid {pid} : connexion de jeu fermée {(transfer ? "(transfert de serveur)" : "par le client")} — " +
                               $"attente d'une nouvelle session ({ReturnToGameGraceMs / 1000} s) avant de le marquer déconnecté");
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ReturnToGameGraceMs).ConfigureAwait(false);
                string reason = Loc.T("L.Link.NoReturn");
                bool down;
                lock (_linkGate)
                {
                    // Nouvelle session (ticket / carte), client arrêté (Forget), relogin attendu, ou attente plus récente -> rien.
                    if (_expectedRelogin.ContainsKey(pid)) return;
                    if (!_returnWait.TryRemove(new KeyValuePair<uint, long>(pid, token))) return;
                    if (_sessionLive.ContainsKey(pid)) return;   // session (re)démarrée entre-temps : jamais coupée par la minuterie
                    down = SetLinkDownLocked(pid, reason, imposed: false, byServer: false);
                }
                if (down) AnnounceLinkDown(pid, reason, imposed: false);
            }
            catch (Exception e) { TransportLog.Line(pid, $"[jeu] attente de retour en jeu pid {pid} : {e.Message}"); }
        });
    }

    // Pose la coupure (hang_up serveur) puis l'annonce. Jamais pour un bot socket.
    private void MarkGameLinkDown(uint pid, string reason, bool imposed, bool byServer)
    {
        bool down;
        lock (_linkGate) down = SetLinkDownLocked(pid, reason, imposed, byServer);
        if (down) AnnounceLinkDown(pid, reason, imposed);
    }

    // SOUS _linkGate. Pose la coupure (une seule fois par session perdue) : GameLinkLosing (scripts du pid arrêtés AVANT que
    // leurs attentes ne soient rendues -> fin « disconnected », jamais « ended »), puis oubli de la SESSION (pas l'identité) et
    // des tampons du décodeur. false = déjà coupé / bot socket (rien à annoncer).
    private bool SetLinkDownLocked(uint pid, string reason, bool imposed, bool byServer)
    {
        if (IsSocketPid(pid)) return false;
        NativeTurboRuntime.RevokeTacticalMapReady((int)pid);
        if (!_linkDown.TryAdd(pid, new GameLinkDown(reason, imposed, byServer, DateTime.UtcNow))) return false;   // déjà coupé
        _sessionLive.TryRemove(pid, out _);
        _returnWait.TryRemove(pid, out _);
        RaiseLinkLosing(pid, reason);
        ForgetSession(pid);
        _decoder?.Forget(pid);
        _deviceObserver?.Forget(pid);   // coupure du lien de jeu : re-observer le device_identifier à la reconnexion
        return true;
    }

    /// <summary>Pose une coupure TENUE par l'app (thread quelconque) : session socket PARQUÉE après une déconnexion imposée par le
    /// serveur, ou ancien pid MITM pendant une relance « Reconnecter » en place (<paramref name="relaunching"/> : activité
    /// « Connexion »). Remplace une coupure MITM déjà posée. Injections refusées, planning / équipe / scripts en pause comme un
    /// « Déconnecté ». Ni GameLinkLosing ni GameLinkLost : l'appelant a déjà arrêté les scripts du pid. Levée par
    /// <see cref="ReleaseLinkDown"/> ou <see cref="Forget"/>, jamais par une trame tardive.</summary>
    public void HoldLinkDown(uint pid, string reason, bool imposed, bool byServer, bool relaunching = false)
    {
        lock (_linkGate)
        {
            NativeTurboRuntime.RevokeTacticalMapReady((int)pid);
            _sessionLive.TryRemove(pid, out _);
            _returnWait.TryRemove(pid, out _);
            _linkDown[pid] = new GameLinkDown(reason, imposed, byServer, DateTime.UtcNow, Held: true, Relaunching: relaunching);
        }
        TransportLog.Line(pid, $"[jeu] pid {pid} : coupure TENUE par l'app ({reason})" +
                               (relaunching ? " — relance du client en cours" : "") + " : injections suspendues");
        RaiseActivity(pid);
    }

    /// <summary>Lève une coupure TENUE (<see cref="HoldLinkDown"/>) ; une coupure réseau (non tenue) reste en place. Thread
    /// quelconque.</summary>
    public void ReleaseLinkDown(uint pid)
    {
        bool released;
        lock (_linkGate)
            released = _linkDown.TryGetValue(pid, out var d) && d.Held && _linkDown.TryRemove(new KeyValuePair<uint, GameLinkDown>(pid, d));
        if (!released) return;
        TransportLog.Line(pid, $"[jeu] pid {pid} : coupure tenue levée");
        RaiseActivity(pid);
    }

    // GameLinkLosing, abonné par abonné : un abonné qui lève n'empêche ni les autres ni l'oubli de session qui suit.
    private void RaiseLinkLosing(uint pid, string reason)
    {
        if (GameLinkLosing is not { } losing) return;
        foreach (var d in losing.GetInvocationList())
        {
            try { ((Action<uint, string>)d)(pid, reason); }
            catch (Exception e) { TransportLog.Line(pid, $"[jeu] pid {pid} : arrêt sur coupure du lien : {e.Message}"); }
        }
    }

    // Annonce une coupure posée (HORS _linkGate) : journal, activité (« Déconnecté ») puis GameLinkLost.
    private void AnnounceLinkDown(uint pid, string reason, bool imposed)
    {
        TransportLog.Line(pid, $"[jeu] pid {pid} : session de jeu PERDUE ({reason}) — Dofus.exe reste ouvert ; état de session oublié, " +
                               "injections suspendues jusqu'au retour en jeu");
        RaiseActivity(pid);
        GameLinkLost?.Invoke(pid, reason, imposed);
    }

    /// <summary>Oublie l'état de SESSION de jeu d'un pid dont le lien est coupé, en GARDANT son identité (perso, serveur, WhoAmI,
    /// niveau) : ≠ <see cref="Forget"/>, qui efface aussi les infos serveur alors que _whoAmISent/_serverKnown survivent (le
    /// serveur ne serait plus ré-identifié). Oublie : carte, combat, vie, fenêtres d'activité, déplacement en vol, actions armées
    /// « à l'arrivée », attentes de paquet, dialogues ouverts.</summary>
    private void ForgetSession(uint pid)
    {
        // Carte courante (et instance de combat) : repeuplée par la 1re carte de la nouvelle session.
        InvalidateCombatMap(pid);
        _lastMap.TryRemove(pid, out _); _serverMapId.TryRemove(pid, out _); _gatherElements.TryRemove(pid, out _);
        // Combat en cours (aucun FightEndEvent ne viendra) + état de mort : sinon « Combat » / « Mort » resteraient collés.
        _inFight.TryRemove(pid, out _); _fightId.TryRemove(pid, out _); _myTurnOpen.TryRemove(pid, out _);
        PurgeFightState(pid);
        _lifeStatus.TryRemove(pid, out _);
        // Fenêtres d'activité + déplacement en vol : garde, confirmation de secours, attente Lua (réglée « refusé »).
        _gatherUntil.TryRemove(pid, out _); _movingUntil.TryRemove(pid, out _);
        _moveGuardUntil.TryRemove(pid, out _); _botMoveConfirm.TryRemove(pid, out _); _botMoveSentAt.TryRemove(pid, out _);
        SettleMove(pid, MoveOutcome.Refused);
        // Actions armées « à l'arrivée » : ne doivent jamais partir sur la session suivante.
        _pendingMapChange.TryRemove(pid, out _); _pendingHarvest.TryRemove(pid, out _); _pendingAttack.TryRemove(pid, out _);
        // Attente d'un paquet précis (banque/PNJ/HDV…) : rendue tout de suite (null = pas de réponse) au lieu d'expirer.
        if (_codeWait.TryRemove(pid, out var cw)) cw.Tcs.TrySetResult(null!);
        // Dialogues ouverts côté client : fermés par la coupure.
        if (HdvAnyOpen(pid)) ResetHdvState(pid);
        _openTeleportDests.TryRemove(pid, out _);
        if (_lastNpcDialog.TryRemove(pid, out _)) _npcDialogSeq.AddOrUpdate(pid, 1, static (_, v) => v + 1);
        _craftOpen.TryRemove(pid, out _); _tradePending.TryRemove(pid, out _); _pendingDuel.TryRemove(pid, out _);
    }

    // Purge de l'état « lien de jeu » d'un client arrêté (Forget ; pid réutilisable par l'OS).
    // Sous _linkGate : une minuterie de retour en jeu en vol ne recoupe pas un pid purgé.
    private void ForgetGameLink(uint pid)
    {
        NativeTurboRuntime.RevokeTacticalMapReady((int)pid);
        CancelReloginWatch(pid);
        _localRelay?.DisarmRelogin(pid);
        lock (_linkGate)
        {
            _linkDown.TryRemove(pid, out _); _sessionLive.TryRemove(pid, out _); _hangUpSeen.TryRemove(pid, out _);
            _serverTransferAt.TryRemove(pid, out _); _returnWait.TryRemove(pid, out _);
            _expectedRelogin.TryRemove(pid, out _); _reloginEpoch.TryRemove(pid, out _);
        }
        _firstMapWait.TryRemove(pid, out _);
        _contextAt.TryRemove(pid, out _);
        lock (_nativeDeferGate)
        {
            _loadingCharacter.TryRemove(pid, out _);
            _deferredNative.TryRemove(pid, out _);
        }
    }

    private readonly record struct DeferredNative(bool Enabled, bool SpeedHack, bool ItemIds, bool Tactical);
    private readonly object _nativeDeferGate = new();
    private readonly ConcurrentDictionary<uint, DeferredNative> _deferredNative = new();
    private readonly ConcurrentDictionary<uint, byte> _loadingCharacter = new();

    private void MarkCharacterLoading(uint pid)
    {
        if (pid == 0 || IsSocketPid(pid)) return;
        NativeTurboRuntime.RevokeTacticalMapReady((int)pid);
        lock (_nativeDeferGate) _loadingCharacter[pid] = 0;
        WatchCharacterLoad(pid);
    }

    private void FinishCharacterLoad(uint pid)
    {
        if (_nativeDeferGate is null || _deferredNative is null) return;
        lock (_nativeDeferGate)
        {
            _loadingCharacter?.TryRemove(pid, out _);
            if (!_deferredNative.TryRemove(pid, out var pending)) return;
            NativeTurboRuntime.RequestState((int)pid, pending.Enabled, pending.SpeedHack, itemIds: pending.ItemIds, tactical: pending.Tactical, profileVisuals: true);
        }
    }

    private void PublishNativeMapReady(uint pid, long mapId)
    {
        lock (_linkGate)
        {
            if (IsSocketPid(pid) || IsLinkSuspended(pid)) return;
            NativeTurboRuntime.PublishTacticalMapReady((int)pid, mapId);
        }
    }
}
