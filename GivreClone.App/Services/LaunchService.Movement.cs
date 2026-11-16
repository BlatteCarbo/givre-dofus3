using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Transport;
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

// ===================== Déplacement : MoveTo, changement de carte, world-graph, cellules de transition, confirmation de move =====================
public sealed partial class LaunchService
{
    // Dernier chemin de cases INJECTÉ par le bot (carte, cases) : sur un refus du serveur, le trajet en tire des obstacles
    // temporaires pour chercher un détour (cases réellement bloquées alors que les données de carte les disent libres).
    private readonly ConcurrentDictionary<uint, (long Map, int[] Cells)> _lastBotPath = new();
    public (long Map, int[] Cells)? LastBotPath(uint pid) => _lastBotPath.TryGetValue(pid, out var p) ? p : null;
    /// <summary>Nombre de déplacements refusés par le serveur (MapMovementRefusedEvent) depuis la connexion.</summary>
    public int MoveRefusedCount(uint pid) => _moveRefused.GetValueOrDefault(pid);

    // Les clics concurrents d'un même pid ne doivent jamais franchir la garde
    // avant son armement. Utiliser une serrure stable de service (et non un
    // TryAdd suivi d'un Inject) protège également les injecteurs synchrones de
    // test qui publient l'écho serveur DURANT Inject().
    private readonly object _movementDispatchGate = new();
    private readonly ConcurrentDictionary<uint, (long Seq, int Source, int Target, long MapId)> _moveEchoCorrelation = new();
    // Une arrivée différée ne devient éligible qu'après un écho serveur
    // explicitement corrélé AU mouvement injecté (un jsq seul ne le prouve pas).
    private readonly ConcurrentDictionary<uint, long> _moveServerEchoSeq = new();
    private readonly ConcurrentDictionary<uint, (long Seq, int[] Path)> _requestedMovePaths = new();
    private readonly ConcurrentDictionary<uint, (long Seq, int Cell)> _shortenedMoveEnds = new();
    private readonly ConcurrentDictionary<uint, long> _confirmDispatching = new();
    // Une confirmation mise en file n'est PAS un ACK. Le jsq S->C n'a pas
    // d'identifiant de requête: V21 ne le laisse plus effacer cette entrée ni
    // libérer une continuation de carte/récolte. Un refus explicite, un arrêt
    // contrôlé ou un vrai futur rapprochement individuel clôtureront l'attente.
    private readonly ConcurrentDictionary<uint, long> _moveConfirmSubmitted = new();
    // Seq whose jqi WE queued after its correlated server echo. Only one move is in flight per pid
    // (A/B guard) and a Socket session has no other sender, so the next confirm response answers it.
    private readonly ConcurrentDictionary<uint, long> _moveConfirmInjectedSeq = new();

    /// <summary>Releases the in-flight move answered by our own jqi, then runs its armed arrival action.
    /// A client-sent jqi, a missing jqi or a replaced move never qualifies.</summary>
    private bool TryReleaseOwnConfirmedMove(uint pid)
    {
        if (!_botMoveConfirm.TryGetValue(pid, out var pending)
            || !_moveConfirmInjectedSeq.TryGetValue(pid, out long injected) || injected != pending.Seq)
            return false;
        long seq = pending.Seq;
        if (!_botMoveConfirm.TryRemove(new KeyValuePair<uint, (long Seq, int Dest, int Cells)>(pid, pending)))
            return false;
        _moveConfirmInjectedSeq.TryRemove(new KeyValuePair<uint, long>(pid, seq));
        _moveGuardUntil[pid] = 0;
        _movingUntil.TryRemove(pid, out _);
        RaiseActivity(pid);
        bool shortened = _shortenedMoveEnds.TryRemove(pid, out var shortEnd) && shortEnd.Seq == seq;
        TransportLog.Line($"[move] pid {pid} : accusé de notre confirmation #{seq} - garde libérée");
        lock (ArrivalActionGate)
        {
            bool mapOwned = _mapArrivalIntentV21.TryGetValue(pid, out var mapTicket) && mapTicket.MoveSeq == seq;
            if (mapOwned) _mapArrivalIntentV21.TryRemove(pid, out _);
            if (_pendingMapChange.TryRemove(pid, out var mc) && mapOwned && !shortened
                && !mc.Owner.IsCancellationRequested && Environment.TickCount64 - mc.SetAt <= PendingMapChangeMaxAgeMs)
                SendMapChange(pid, mc.Target);
            bool harvestOwned = _harvestArrivalIntentV21.TryGetValue(pid, out var harvestTicket) && harvestTicket.MoveSeq == seq;
            if (harvestOwned) _harvestArrivalIntentV21.TryRemove(pid, out _);
            if (_pendingHarvest.TryRemove(pid, out var h) && harvestOwned && !shortened && !h.Owner.IsCancellationRequested)
                SendInteractiveUse(pid, h.Skill, h.Element, null, h.Gather);
            if (_pendingAttack.TryGetValue(pid, out var atk) && atk.MoveSeq == seq)
            {
                _pendingAttack.TryRemove(pid, out _);
                if (!shortened && !atk.Owner.IsCancellationRequested
                    && AttackTargetLeaseValid(atk.TargetStillCurrent)
                    && AttackArrivalLeaseValid(atk.ConfirmedArrival, atk.ArrivalCell))
                    SubmitAttackWithLease(atk.SubmitWithLease, atk.ArrivalCell,
                        () => SendAttack(pid, atk.Group, atk.Name));
            }
        }
        SettleMove(pid, shortened ? MoveOutcome.Superseded : MoveOutcome.Arrived);
        return true;
    }

    // V21: local continuation ownership for a movement that will subsequently
    // trigger a map transition or interactive. These facts identify our intent,
    // NOT the MapMovementConfirmResponse: that response has no move/request UID.
    // Keep the legacy pending tuple shape (script cancellation owns it), and
    // store the additional correlation exclusively in this consultative ticket.
    private sealed record ArrivalIntentV21(long MoveSeq, long MapId, int StartCell,
        int ArrivalCell, long DecoderEpoch, CaptureLinkIdentity? SourceLink,
        CancellationToken Owner);

    private readonly ConcurrentDictionary<uint, ArrivalIntentV21> _mapArrivalIntentV21 = new();
    private readonly ConcurrentDictionary<uint, ArrivalIntentV21> _harvestArrivalIntentV21 = new();

    private CaptureLinkIdentity? CurrentArrivalLinkV21(uint pid)
        => IsSocketPid(pid) ? _socketSourceAuthority.CurrentLink(pid)
            : _localMitmSourceAuthority.CurrentLink(pid);

    private ArrivalIntentV21 NewArrivalIntentV21(uint pid, long seq, long mapId,
        int source, int arrival, CancellationToken owner)
        => new(seq, mapId, source, arrival,
            _decoder?.CurrentLocalEpoch(pid) ?? 0, CurrentArrivalLinkV21(pid), owner);

    /// <summary>
    /// Read-only diagnosis for the actual S->C confirmation reducer. Even a
    /// same-source own-movement echo proves only THAT movement was announced:
    /// MapMovementConfirmResponse carries no per-request identifier, so it
    /// cannot select this continuation from A/B or authorize a follow-up send.
    /// </summary>
    internal string InspectMapConfirmAttributionV21(uint pid, bool harvest,
        DecodedPacket response)
    {
        var intents = harvest ? _harvestArrivalIntentV21 : _mapArrivalIntentV21;
        if (!intents.TryGetValue(pid, out var ticket)) return "no-local-ticket";
        if (response.Pid != pid || response.ClientToServer ||
            response.Name != "MapMovementConfirmResponse") return "wrong-packet";
        if (ticket.Owner.IsCancellationRequested) return "owner-cancelled";
        if (ticket.DecoderEpoch <= 0 || ticket.SourceLink is null ||
            response.DecoderEpoch <= 0) return "missing-source-or-epoch";
        if (_decoder?.IsCurrentLocalEpoch(response) != true ||
            _decoder.CurrentLocalEpoch(pid) != ticket.DecoderEpoch)
            return "decoder-epoch-changed";
        if (CurrentArrivalLinkV21(pid) != ticket.SourceLink)
            return "source-link-changed";
        if (!_moveEchoCorrelation.TryGetValue(pid, out var move) ||
            move.Seq != ticket.MoveSeq || move.MapId != ticket.MapId ||
            move.Source != ticket.StartCell || move.Target != ticket.ArrivalCell ||
            (CurrentMap(pid) ?? 0) != ticket.MapId)
            return "movement-ticket-changed";
        if (_moveServerEchoSeq.GetValueOrDefault(pid) != ticket.MoveSeq)
            return "own-movement-echo-missing";
        // Strong own-echo association is preserved as diagnostic evidence.
        // No request UID is present in the S->C response, even here.
        return "own-echo-observed-but-confirmation-unattributable";
    }

    /// <summary>
    /// Une annonce serveur ne peut cadencer notre jqi que si elle correspond
    /// au même mouvement, même carte et mêmes extrémités effectivement
    /// demandées. Les points internes peuvent différer si le serveur a choisi
    /// un autre itinéraire. Un chemin vide ou tronqué reste non confirmé.
    /// </summary>
    internal static bool MovementEchoMatches(
        long expectedSequence, long actualSequence, int source, int target,
        IReadOnlyList<int>? serverPath)
        => expectedSequence == actualSequence
           && serverPath is { Count: > 1 }
           && (uint)source < MapPathfinder.CellCount
           && (uint)target < MapPathfinder.CellCount
           && source != target
           && serverPath[0] == source
           && serverPath[^1] == target
           && serverPath.All(cell => (uint)cell < MapPathfinder.CellCount);

    // Le client peut confirmer un déplacement très court AVANT que l'echo
    // MapMovementEvent soit traité : _botMoveConfirm est alors déjà retiré.
    // Reconnaître l'écho directement, sans déduire une réussite d'un jsq
    // isolé. Aucun raccourci d'identité : characterId serveur obligatoire.
    private void NoteObservedOwnMoveEcho(uint pid, long actorId, IReadOnlyList<int> serverPath)
    {
        long selfId = CharacterId(pid);
        if (selfId <= 0 || actorId != selfId
            || !_moveEchoCorrelation.TryGetValue(pid, out var move)
            || !_requestedMovePaths.TryGetValue(pid, out var requested)
            || move.Seq != requested.Seq || move.Seq <= 0
            || serverPath is not { Count: > 1 } || serverPath[^1] != move.Target
            || !MovementEchoPolicy.Matches(requested.Path, serverPath)
            || (_serverMapId.TryGetValue(pid, out var serverMap) && serverMap != 0
                && serverMap != move.MapId))
            return;
        _moveServerEchoSeq[pid] = move.Seq;
    }

    /// <summary>
    /// Clic-pour-deplacer : A* (cellules mov) de source vers target, forge un MapMovementRequest (jrw) et
    /// l'injecte vers le serveur de jeu du pid. Retourne false si pas de relais, pas de map, ou pas de chemin.
    /// </summary>
    // Coupe-circuit des déplacements refusés à répétition (hors combat) : dernier trajet émis par pid, pour attribuer le refus.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, MoveRefusalBreaker> _moveBreakers = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, (long Map, int From, int To)> _lastMoveDispatch = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(uint, long, int, int), byte> _moveBreakerLogged = new();

    // Refus serveur (thread réseau) : compté pour le dernier trajet émis HORS combat ; ouvre le coupe-circuit au seuil.
    private void NoteMoveRefusedForBreaker(uint pid)
    {
        if (_inFight.TryGetValue(pid, out var f) && f) return;
        if (!_lastMoveDispatch.TryRemove(pid, out var last)) return;
        bool opened;
        lock (_movementDispatchGate)
            opened = _moveBreakers.GetOrAdd(pid, static _ => new MoveRefusalBreaker()).NoteRefused(last.Map, last.From, last.To, Environment.TickCount64);
        if (opened) _moveBreakerLogged.TryRemove((pid, last.Map, last.From, last.To), out _);
    }

    public bool MoveTo(uint pid, long mapId, int source, int target, Func<int, bool> isWalkable, bool allowDiag = true,
                       Func<int, int, bool>? stepAllowed = null, IReadOnlyList<int>? plannedPath = null,
                       Func<long, bool>? beforeInject = null)
    {
        if (pid == 0 || mapId <= 0 || (uint)source >= MapPathfinder.CellCount ||
            (uint)target >= MapPathfinder.CellCount || source == target || isWalkable is null)
        {
            TransportLog.Line($"[move] pid {pid} : refus paramètres invalides map={mapId} source={source} destination={target}");
            return false;
        }
        if (IsSocketPid(pid) && !_socketInjectors.ContainsKey(pid))
        {
            TransportLog.Line($"[move] pid {pid} : refus Socket, injecteur de jeu non enregistré (aucune trame envoyée)");
            return false;
        }
        if (!(_inFight.TryGetValue(pid, out var fight) && fight) &&
            _serverMapId.TryGetValue(pid, out var confirmedMap) && confirmedMap != 0 && confirmedMap != mapId)
        {
            TransportLog.Line($"[move] pid {pid} : carte demandée {mapId} différente de la carte serveur {confirmedMap}, déplacement non émis");
            return false;
        }
        var relay = RelayForInject();
        if (relay is null) return false;

        lock (_movementDispatchGate)
        {
        // Garde "un seul move a la fois": un jqt de refus ou la fin exacte de session
        // peut retirer le ticket. jsq n'a pas d'identifiant individuel et ne résout
        // donc jamais le mouvement; après expiration de la garde, on refuse toujours B.
        long now = Environment.TickCount64;
        if (_moveGuardUntil.TryGetValue(pid, out var until) && now < until)
        {
            TransportLog.Line($"[move] pid {pid} : IGNORE ({source}->{target}) - deplacement en cours");
            return false;
        }
        if (_botMoveConfirm.ContainsKey(pid))
        {
            TransportLog.Line($"[move] pid {pid} : confirmation du trajet précédent encore non résolue ; nouveau trajet refusé pour éviter une ambiguïté A/B");
            return false;
        }

        bool outOfFight = !(_inFight.TryGetValue(pid, out var fightNow) && fightNow);
        if (outOfFight && !_moveBreakers.GetOrAdd(pid, static _ => new MoveRefusalBreaker()).Allows(mapId, source, target, now))
        {
            // Même trajet déjà refusé plusieurs fois par le serveur : on ne le renvoie pas (journal : une ligne par blocage).
            if (_moveBreakerLogged.TryAdd((pid, mapId, source, target), 0))
                TransportLog.Line($"[move] pid {pid} : {source}->{target} refusé {MoveRefusalBreaker.Threshold} fois par le serveur sur la carte {mapId} -> non renvoyé pendant {MoveRefusalBreaker.CooldownMs / 1000} s (aucune trame émise)");
            return false;
        }
        if (plannedPath is not null && (plannedPath.Count < 2 || plannedPath[0] != source || plannedPath[^1] != target
            || plannedPath.Skip(1).Where((c, i) => !isWalkable(c)
                || MapPathfinder.CellDistance(plannedPath[i], c) != 1
                || !(stepAllowed?.Invoke(plannedPath[i], c) ?? true)).Any())) return false;
        var keyCells = plannedPath is null ? MapPathfinder.KeyMovements(source, target, isWalkable, allowDiag, stepAllowed)
            : MapPathfinder.Compress(plannedPath.ToList());
        if (keyCells.Count == 0) { TransportLog.Line($"[GivreClone] move pid {pid} : aucun chemin {source}->{target}"); return false; }
        _lastMoveDispatch[pid] = (mapId, source, target);
        byte[] jrw = MoveForge.BuildFramed(mapId, keyCells);
        var expanded = MapPathfinder.ExpandPath(keyCells.Select(k => k & 0xFFF).ToList());   // chemin deroule (cellule par cellule)
        if (jrw.Length == 0)
        {
            TransportLog.Line($"[move] pid {pid} : MapMovementRequest NON forgé ({MoveForge.LastForgeError ?? "mapping/codage absent"}), aucun envoi");
            return false;
        }
        if (expanded.Count < 2 || expanded[0] != source || expanded[^1] != target)
        {
            TransportLog.Line($"[move] pid {pid} : chemin construit incohérent ({source}->{target}), aucune injection");
            return false;
        }

        // IMPORTANT : armer AVANT l'injection. Un injecteur de pid synthétique
        // peut déclencher une réception synchrone (harness) ou très rapide :
        // l'ancien code n'armait _botMoveConfirm qu'après Inject() et perdait
        // l'écho MapMovementEvent, donc jamais de jqi ni de jsq.
        long guardUntil = Environment.TickCount64 + MoveGuardMs;
        long moveSeq = 0;
        bool inFight = _inFight.TryGetValue(pid, out var activeFight) && activeFight;
        if (!inFight) moveSeq = Interlocked.Increment(ref _moveSeq);
        if (beforeInject is not null)
        {
            bool armed;
            try { armed = moveSeq > 0 && beforeInject(moveSeq); }
            catch { armed = false; }
            if (!armed) return false;
        }
        _moveGuardUntil[pid] = guardUntil;
        // Comme _botMoveConfirm, la fenêtre d'activité doit exister avant
        // Inject : un jsq/jqt synchrone peut déjà l'avoir refermée quand la
        // fonction retourne. Ne jamais la rouvrir après un ACK/refus reçu.
        long activityUntil = Environment.TickCount64 + MoveWindowMs;
        _movingUntil[pid] = activityUntil;
        if (!inFight)
        {
            _moveServerEchoSeq.TryRemove(pid, out _);
            _moveEchoCorrelation[pid] = (moveSeq, source, target, mapId);
            _requestedMovePaths[pid] = (moveSeq, expanded.ToArray());
            _shortenedMoveEnds.TryRemove(pid, out _);
            _botMoveConfirm[pid] = (moveSeq, target, expanded.Count);
            _botMoveSentAt[pid] = (moveSeq, Environment.TickCount64);
            _moveConfirmSubmitted.TryRemove(pid, out _);
            _moveConfirmInjectedSeq.TryRemove(pid, out _);
        }

        bool ok;
        try { ok = relay.Inject(pid, jrw); }
        catch (Exception e)
        {
            TransportLog.Line($"[move] pid {pid} : dispatch MapMovementRequest impossible ({e.GetType().Name})");
            ok = false;
        }
        if (!ok)
        {
            // Ne pas conserver une garde ou une confirmation pour des octets
            // refusés par le dispatch. CAS : n'efface pas un mouvement plus
            // récent si une réception a été traitée de façon réentrante.
            if (moveSeq != 0)
            {
                if (_botMoveConfirm.TryGetValue(pid, out var pending) && pending.Seq == moveSeq)
                    _botMoveConfirm.TryRemove(
                        new KeyValuePair<uint, (long Seq, int Dest, int Cells)>(pid, pending));
                if (_botMoveSentAt.TryGetValue(pid, out var sentAt) && sentAt.Seq == moveSeq)
                    _botMoveSentAt.TryRemove(new KeyValuePair<uint, (long Seq, long SentAt)>(pid, sentAt));
                _moveEchoCorrelation.TryRemove(new KeyValuePair<uint,
                    (long Seq, int Source, int Target, long MapId)>(
                    pid, (moveSeq, source, target, mapId)));
            }
            if (_moveGuardUntil.TryGetValue(pid, out var currentGuard) && currentGuard == guardUntil)
                _moveGuardUntil[pid] = 0;
            if (_movingUntil.TryGetValue(pid, out var currentActivity) && currentActivity == activityUntil)
                _movingUntil.TryRemove(
                    new KeyValuePair<uint, long>(pid, currentActivity));
            TransportLog.Line($"[move] pid {pid} : MapMovementRequest non accepté par le dispatch (Socket/MITM), aucune arrivée présumée");
            return false;
        }
        ShowInjected(pid, jrw);   // console locale, pas un ACK
        if (IsInFight(pid)) NoteTurnAction(pid);
        _lastBotPath[pid] = (mapId, expanded.ToArray());
        TransportLog.Line($"[GivreClone] move pid {pid} : map {mapId} {source}->{target} ({keyCells.Count} keyCells, packed) dispatch={(ok ? "ACCEPTE" : "REFUSE")} (ACK serveur en attente)");
        if (MoveDiag)
            TransportLog.Line($"[move-dbg] pid {pid} INJECTE {source}->{target} : chemin=[{string.Join(">", expanded)}] ({expanded.Count} cases) ; keyCells=[{string.Join(",", keyCells.Select(k => $"{k & 0xFFF}@{(k >> 12) & 0xF}"))}] ; src.mov={isWalkable(source)} tgt.mov={isWalkable(target)}");
        // Un refus traité durant l'injection aura déjà recalé SelfMoved sur
        // la vraie cellule via OnDecoded. Ne pas écraser ce recalage par
        // notre prédiction tardive.
        if (moveSeq == 0 || (_botMoveConfirm.TryGetValue(pid, out var stillPending) && stillPending.Seq == moveSeq))
            SelfMoved?.Invoke(pid, expanded);   // chemin optimiste, pas position confirmée
        // On N'INJECTE PAS le jqi tout de suite : on laisse le vrai client confirmer s'il le fait (moves courts) ; sinon
        // le jqi de SECOURS part a la reception de l'echo serveur jsj (cf handler MapMovementEvent) -> commit garanti.
        return ok;
        }
    }

    /// <summary>Changement de carte dans une direction (bord), facon MovePlayerAndChangeMap d'un bot tiers: lit la
    /// TRANSITION du world-graph du client (cellule de bord exacte + carte cible), MARCHE jusqu'a cette cellule, puis
    /// envoie le MapChangeRequest (jqk) a l'arrivee. Deja au bord -> jqk direct. Retourne false si pas de transition.</summary>
    public bool ChangeMapDirection(uint pid, long mapId, int source, MapDir dir, Func<int, bool> isWalkable,
                                   Func<int, int, bool>? stepAllowed = null, bool sourceConfirmed = true)
    {
        var relay = RelayForInject();
        if (relay is null || mapId == 0) { TransportLog.Line($"[map] pid {pid} : changement {dir} ignore (relais absent ou carte inconnue mapId={mapId})"); return false; }

        // Carte voisine : mapData du client (autoritaire, TOUTES les cartes) en priorite ; world-graph en repli
        // (le world-graph est INCOMPLET - il ne contient pas toutes les cartes, ex 191102978).
        var exits = _cellReader.LoadExits(mapId);
        var wgT = _worldGraph?.InDirection((int)mapId, dir);
        int targetMap = NeighbourMapOf(mapId, dir);
        if (targetMap == 0) { TransportLog.Line($"[map] pid {pid} : pas de voisin {dir} sur la carte {mapId} (mapData {(exits is null ? "absente" : "sans voisin")}, world-graph {(wgT is null ? "sans transition" : "ok")})"); return false; }

        // Cellule de TRANSITION EXACTE: mapChangeData du client (bit de la direction).
        // Replis si absent : cellule du world-graph > fleche de bord mapData.
        // Cellule de sortie : on prefere une cellule de transition REELLEMENT ATTEIGNABLE (chemin non vide, regle de
        // secteur incluse) plutot que la simple plus proche a vol d'oiseau — celle-ci peut etre dans un secteur
        // DECONNECTE, ce qui fait ignorer le move par le serveur (aucun jsq/jqt). Replis conserves si aucune prouvee.
        int exitCell = NearestReachableTransitionCell(mapId, source, dir, isWalkable, stepAllowed);
        bool exitReachable = exitCell >= 0;
        if (exitCell < 0) exitCell = NearestTransitionCell(mapId, source, dir);
        if (exitCell < 0 && wgT is not null) exitCell = wgT.CellId;
        if (exitCell < 0) { var a = exits?.Arrows(dir) ?? System.Array.Empty<int>(); if (a.Count > 0) exitCell = PickNearestWalkable(a, source, isWalkable); }
        if (exitCell < 0) { TransportLog.Line($"[map] pid {pid} : aucune cellule de transition {dir} sur la carte {mapId} (mapChangeData absent ?)"); return false; }

        if (MoveDiag)
        {
            var allCells = _cellReader.LoadCells(mapId);
            int mask = MapChangeMask(dir);
            string cands = allCells is null ? "?" : string.Join(",", allCells.Where(c => (c.MapChangeData & mask) != 0).Select(c => $"{c.CellId}{(c.Mov ? "" : "!")}"));
            TransportLog.Line($"[map-dbg] pid {pid} sortie {dir} : exit={exitCell} (atteignable={exitReachable}) ; world-graph={(wgT is null ? "-" : $"cell {wgT.CellId}->map {wgT.TransitionMapId}")} ; candidats mapChangeData=[{cands}] (! = non marchable) ; source={source}");
        }

        TransportLog.Line($"[map] pid {pid} : changement {dir} -> cellule de transition {exitCell}, carte cible {targetMap} (perso en {source})");

        return ChangeMapToCell(pid, mapId, source, exitCell, targetMap, isWalkable, stepAllowed, sourceConfirmed);
    }

    // Carte voisine dans une direction : mapData du client (autoritaire) en priorité, world-graph en repli. 0 = aucune.
    // (Extrait tel quel de ChangeMapDirection, partagé avec ChangeMapDirectionViaCell.)
    private int NeighbourMapOf(long mapId, MapDir dir)
    {
        var exits = _cellReader.LoadExits(mapId);
        var wgT = _worldGraph?.InDirection((int)mapId, dir);
        return (exits is not null && exits.HasNeighbour(dir)) ? exits.Neighbour(dir) : (wgT?.TransitionMapId ?? 0);
    }

    /// <summary>Changement de carte dans la direction `dir` par une cellule de sortie FORCÉE (un bot tiers « top(123) »): marche
    /// jusqu'à `exitCell` puis jqk (ChangeMapToCell). Aucun choix de cellule la plus proche. Une cellule sans le drapeau de
    /// la direction est journalisée mais tentée quand même (l'utilisateur l'a forcée). false = pas de voisin / cellule
    /// invalide / pas de chemin.</summary>
    public bool ChangeMapDirectionViaCell(uint pid, long mapId, int source, MapDir dir, int exitCell, Func<int, bool> isWalkable,
                                          Func<int, int, bool>? stepAllowed = null, bool sourceConfirmed = true)
    {
        if (RelayForInject() is null || mapId == 0 || (uint)exitCell >= MapPathfinder.CellCount) return false;
        int targetMap = NeighbourMapOf(mapId, dir);
        if (targetMap == 0) { TransportLog.Line($"[map] pid {pid} : pas de voisin {dir} sur la carte {mapId} (cellule forcée {exitCell})"); return false; }
        var cellsV = _cellReader.LoadCells(mapId);
        if (cellsV is not null && !MapExitCells.Candidates(cellsV, dir).Any(x => x.CellId == exitCell))
            TransportLog.Line($"[map] pid {pid} : cellule forcée {exitCell} sans drapeau de sortie {dir} (tentative quand même)");
        TransportLog.Line($"[map] pid {pid} : changement {dir} via cellule forcée {exitCell} -> carte {targetMap}");
        return ChangeMapToCell(pid, mapId, source, exitCell, targetMap, isWalkable, stepAllowed, sourceConfirmed);
    }

    /// <summary>Direction de sortie d'une cellule (bits mapChangeData ; égalité -> la direction qui a une transition
    /// world-graph, puis Droite&gt;Bas&gt;Gauche&gt;Haut). Repli : transition world-graph de type 1/2 sur cette cellule
    /// (tr.Direction 0/2/4/6). null = pas une cellule de sortie. Lecture pure.</summary>
    public MapDir? ExitDirOfCell(long mapId, int cell)
    {
        if (mapId == 0 || (uint)cell >= MapPathfinder.CellCount) return null;
        var c = _cellReader.LoadCells(mapId)?.FirstOrDefault(x => x.CellId == cell);
        if (c is not null && c.MapChangeData != 0)
        {
            var order = new[] { MapDir.Right, MapDir.Bottom, MapDir.Left, MapDir.Top };
            var flagged = order.Where(d => (c.MapChangeData & MapChangeMask(d)) != 0).ToList();
            if (flagged.Count == 1) return flagged[0];
            if (flagged.Count > 1)
            {
                // Case de COIN (plusieurs drapeaux) : d'abord la direction dont une transition inconditionnelle du world-graph
                // part de CETTE case ; seulement ensuite l'ordre fixe (InDirection a un repli géométrique, quasi toujours vrai).
                if (DirOfTransitionAt(mapId, cell) is MapDir onCell && flagged.Contains(onCell)) return onCell;
                return flagged.FirstOrDefault(d => _worldGraph?.InDirection((int)mapId, d) is not null, flagged[0]);
            }
        }
        if (DirOfTransitionAt(mapId, cell) is MapDir wgDir) return wgDir;   // arête world-graph exacte sur cette case
        // Même règle que le choix automatique de sortie (MapExitCells) : bit DIAGONAL sur le bord qui dépasse quand la carte
        // n'a aucune case au bit cardinal (ex zaap de Bonta, case 551) -> un `exitCell = 551` de script est accepté.
        var all = c is not null && c.MapChangeData != 0 ? _cellReader.LoadCells(mapId) : null;
        if (all is not null)
        {
            var diag = new[] { MapDir.Right, MapDir.Bottom, MapDir.Left, MapDir.Top }
                .Where(d => MapExitCells.Candidates(all, d).Any(x => x.CellId == cell)).ToList();
            if (diag.Count > 0) return diag.FirstOrDefault(d => _worldGraph?.InDirection((int)mapId, d) is not null, diag[0]);
        }
        return null;
    }

    // Direction de la 1re transition cardinale (type 1/2) INCONDITIONNELLE du world-graph partant de `cell`, sinon null.
    private MapDir? DirOfTransitionAt(long mapId, int cell)
    {
        if (_worldGraph is null) return null;
        foreach (var tr in _worldGraph.Transitions((int)mapId))
        {
            if (tr.CellId != cell || tr.Type is not (1 or 2) || tr.Criterion is not null) continue;
            MapDir? d = tr.Direction switch { 0 => MapDir.Right, 2 => MapDir.Bottom, 4 => MapDir.Left, 6 => MapDir.Top, _ => (MapDir?)null };
            if (d is not null) return d;
        }
        return null;
    }

    /// <summary>Marche jusqu'a la cellule de transition <paramref name="exitCell"/> puis change vers <paramref name="targetMap"/>
    /// (jqk envoye a l'arrivee, handler jsq). Deja sur la cellule -> jqk direct. Coeur commun a ChangeMapDirection et au
    /// voyage multi-cartes (BFS). Retourne false si aucun chemin (ou deplacement deja en cours).</summary>
    public bool ChangeMapToCell(uint pid, long mapId, int source, int exitCell, int targetMap, Func<int, bool> isWalkable, Func<int, int, bool>? stepAllowed, bool sourceConfirmed = true)
    {
        if (source == exitCell)
        {
            // Deja SUR la cellule de transition -> jqk direct... MAIS seulement si la position est une VERITE SERVEUR.
            // Si elle n'est que PREDITE (move precedent ignore en silence -> SelfCell reste la cellule d'exit optimiste
            // alors que le perso n'a pas bouge), un jqk direct est REFUSE par le serveur (perso pas reellement au bord)
            // et le jqt de refus d'un jqk ne recale pas la position (semantique x/y differente) -> desync en boucle.
            // Un bot tiers ne change JAMAIS de carte sans une cellule de bord confirmee: on echoue proprement et on laisse un
            // evenement serveur (jss/jsj/jqt) recaler la vraie cellule avant de re-tenter.
            if (!sourceConfirmed)
            {
                TransportLog.Line($"[map] pid {pid} : source==exit {exitCell} mais position NON CONFIRMEE (predite) -> changement de carte direct ANNULÉ (on attend un recalage serveur)");
                return false;
            }
            return SendMapChange(pid, targetMap);                               // deja sur une cellule de transition (confirmee) -> envoi direct
        }
        long armedSeq = 0;
        bool moved = MoveTo(pid, mapId, source, exitCell, isWalkable,
            allowDiag: true, stepAllowed: stepAllowed, beforeInject: seq =>
            {
                lock (ArrivalActionGate)
                {
                    var owner = ScriptActionOwner.Value;
                    if (owner.IsCancellationRequested || _pendingMapChange.ContainsKey(pid) ||
                        _pendingHarvest.ContainsKey(pid)) return false;
                    armedSeq = seq;
                    _mapArrivalIntentV21[pid] = NewArrivalIntentV21(pid, seq,
                        mapId, source, exitCell, owner);
                    _pendingMapChange[pid] = (targetMap, Environment.TickCount64, owner);
                    return true;
                }
            });
        if (moved)
        {
            TransportLog.Line($"[map] pid {pid} : marche {source}->{exitCell}, transition vers {targetMap} en attente d'une confirmation INDIVIDUELLEMENT attribuable (V21)");
        }
        else
        {
            if (armedSeq != 0)
                lock (ArrivalActionGate)
                    if (_mapArrivalIntentV21.TryGetValue(pid, out var ticket) && ticket.MoveSeq == armedSeq)
                    {
                        _mapArrivalIntentV21.TryRemove(pid, out _);
                        _pendingMapChange.TryRemove(pid, out _);
                    }
            TransportLog.Line($"[map] pid {pid} : impossible de marcher vers la cellule de transition {exitCell} (pas de chemin, ou deplacement deja en cours)");
        }
        return moved;
    }

    /// <summary>Un changement de carte est en attente (marche vers le bord en cours, jqk pas encore envoyé).</summary>
    public bool HasPendingMapChange(uint pid) => _pendingMapChange.ContainsKey(pid);

    /// <summary>Annule le changement de carte en attente : le perso finit sa marche mais ne quitte pas la carte.</summary>
    public void CancelPendingMapChange(uint pid)
    {
        lock (ArrivalActionGate)
        {
            _pendingMapChange.TryRemove(pid, out _);
            _mapArrivalIntentV21.TryRemove(pid, out _);
        }
    }

    /// <summary>Une marche est encore en cours (garde « un seul déplacement à la fois » active).</summary>
    public bool MoveInProgress(uint pid) => _moveGuardUntil.TryGetValue(pid, out var until) && Environment.TickCount64 < until;

    /// <summary>Plus court chemin de transitions (world-graph BFS) de startMap a targetMap, ou liste vide. Voyage multi-cartes.
    /// Si <paramref name="pid"/> est fourni (!=0), le BFS peut, EN REPLI (aucun chemin libre), emprunter une porte/PNJ
    /// CONDITIONNEL dont le critere est prouve satisfait pour ce perso (ex pont gardé par une quête déjà faite).
    /// <para><paramref name="startZone"/> : secteur (zone du world-graph) de la cellule du perso sur <paramref name="startMap"/>,
    /// 0 = inconnu -> planification au niveau carte. <paramref name="interactiveState"/> : état LIVE des interactifs de la carte
    /// courante (critères MI des portes), null = inconnu.</para></summary>
    /// <summary>Secteur unique des sorties de la carte (&gt; 0), -1 si plusieurs (la cellule du perso tranche), 0 si inconnu.</summary>
    public int WorldSoleZone(long mapId) => _worldGraph?.SoleZone((int)mapId) ?? 0;

    public IReadOnlyList<MapTransition> WorldPath(int startMap, int targetMap, IReadOnlySet<int>? avoid = null, uint pid = 0, IReadOnlySet<long>? avoidEdges = null,
                                                  int startZone = 0, Func<int, int?>? interactiveState = null)
        => _worldGraph?.PathToMap(startMap, targetMap, avoid, 80, pid != 0 ? (c => CriterionMet(pid, c, interactiveState)) : null, avoidEdges, startZone)
           ?? System.Array.Empty<MapTransition>();

    /// <summary>Transition du world-graph dans une direction cardinale (cellule + carte cible + TYPE : 1/2 bord, 8 soleil,
    /// 32 interactif/escalier), ou null. Sert au pas-a-pas directionnel a detecter une sortie NON franchissable par le
    /// bord (escalier : aucune cellule mapChangeData) pour deleguer au voyage type-aware.</summary>
    public MapTransition? WorldTransitionInDirection(long mapId, MapDir dir) => _worldGraph?.InDirection((int)mapId, dir);

    /// <summary>Carte RÉELLEMENT atteinte par changeMap(dir) depuis <paramref name="mapId"/> (lecture pure, map:neighbourId) :
    /// sortie escalier/soleil (world-graph type 32/8) -> sa carte d'arrivée (changeMap délègue alors à GoToMapId) ; sortie
    /// ScrollAction dont l'arrivée (m_to) diffère de l'id envoyé -> m_to (le voisin mapData est alors un id THÉORIQUE sans
    /// carte réelle, ex 212600324 au sud du zaap de Bonta, où l'on arrive en 212600837) ; sinon voisin mapData (autoritaire) ;
    /// sinon transition world-graph. 0 = aucune.</summary>
    public long NeighbourMapId(long mapId, MapDir dir)
    {
        if (mapId == 0) return 0;
        var wg = _worldGraph?.InDirection((int)mapId, dir);
        if (wg is { Type: 32 or 8 }) return wg.Arrival;
        if (wg is { Type: 2 } && wg.Arrival != wg.TransitionMapId) return wg.Arrival;
        var exits = _cellReader.LoadExits(mapId);
        if (exits is not null && exits.HasNeighbour(dir)) return exits.Neighbour(dir);
        return wg?.Arrival ?? 0;
    }

    /// <summary>La carte est-elle connue du world-graph (diagnostic voyage : cible atteignable en principe) ?</summary>
    public bool WorldGraphKnows(int mapId) => _worldGraph?.IsKnownMap(mapId) ?? false;

    /// <summary>Appartenance au graphe pour le catalogue Expédition ; null tant que le graphe n'est pas disponible.</summary>
    public bool? ExpeditionNavigationMap(long mapId)
        => _worldGraph is { MapCount: > 0 } graph ? graph.IsKnownMap((int)mapId) : null;

    /// <summary>Départage les doublons d'Expédition : sorties de bord (marche/scroll), puis connectivité totale.
    /// Ce score n'atteste ni l'accès du personnage ni la présence de monstres. Sans graphe : égalité historique.</summary>
    public int ExpeditionWalkScore(long mapId)
    {
        var edges = _worldGraph?.Transitions((int)mapId);
        if (edges is null) return 0;
        int borders = edges.Count(e => e.Type is 1 or 2 && e.Direction is >= 0 and <= 7);
        return borders * 10_000 + Math.Min(edges.Count, 9_999);
    }

    /// <summary>Id de carte a la coordonnee (x,y). Plusieurs cartes peuvent partager un coord de DEUX facons :
    /// (a) meme calque, EXTERIEUR + INTERIEUR (ex 4,-22 : champ 189792777 + interieur 119015430) — on prend le PLUS
    /// connecte = l'exterieur ; (b) calques DIFFERENTS, meme coord sur des MONDES distincts (ex "2,-1" existe a Incarnam
    /// ET a Astrub) — la desambiguisation par nombre d'aretes est alors AVEUGLE et peut renvoyer la carte d'un AUTRE monde
    /// (bug havre-sac a Incarnam). <paramref name="preferWorldMap"/> (le calque courant du perso, cf <see cref="CurrentWorldMap"/>)
    /// verrouille le bon monde : un candidat dont le worldMap == preferWorldMap gagne un ENORME bonus, dominant le score
    /// d'aretes. (GetMapIdFromCoords: worldMap==current => +100000) et un bot tiers (match par presence physique).
    /// preferWorldMap null (ou worldMap candidat inconnu = cache legacy) => on retombe sur le pur nombre d'aretes. Null si aucune carte connue.</summary>
    public long? MapIdAt(int x, int y, int? preferWorldMap = null)
    {
        if (_mapPositions is null) return null;
        long? best = null; long bestScore = long.MinValue;
        foreach (var m in _mapPositions.MapsAt(x, y))
        {
            int edges = _worldGraph?.Transitions((int)m).Count ?? 0;
            long score = edges;
            if (preferWorldMap is int want && _mapPositions.WorldMapOf(m) is int wm && wm == want)
                score += 1_000_000;                                                   // verrou de calque : ce monde d'abord
            if (best is null || score > bestScore) { best = m; bestScore = score; }
        }
        return best;
    }

    /// <summary>Calque (worldMap) d'une carte donnee, ou null si inconnu (carte absente / cache legacy). 1 = Monde des Douze
    /// principal (seul calque avec zaaps/havre-sac accessibles), Incarnam/instances = autre valeur, -1 = donjon.</summary>
    public int? WorldMapOf(long mapId) => _mapPositions?.WorldMapOf(mapId) ?? GameMapPosition(mapId)?.World;

    // Repli « données du jeu » (MapInformationData = les relevés de DofusDB) : une carte absente de la table extraite du client, ou dont le calque est inconnu, n'est plus « inconnue » —
    // sinon le raccourci « carte voisine » est sauté et le bot part au havre-sac (≈ 40 s perdues par changement de carte).
    private Lazy<IReadOnlyDictionary<long, (int X, int Y, int World)>>? _gameMapPositions;
    private (int X, int Y, int World)? GameMapPosition(long mapId)
    {
        var db = GameData;
        if (db is null) return null;
        var lazy = _gameMapPositions ??= new Lazy<IReadOnlyDictionary<long, (int X, int Y, int World)>>(() =>
        {
            try { return db.AllMapPositions(); }
            catch (Exception e) when (e is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException) { return new Dictionary<long, (int X, int Y, int World)>(); }
        });
        return lazy.Value.TryGetValue(mapId, out var p) ? p : null;
    }

    /// <summary>Coordonnée MONDE (x,y) d'une carte, ou null si inconnue. Sert au suivi COLLÉ (SYNC_MOVE) : dériver la
    /// direction de sortie du chef à partir du delta de coord entre sa carte de départ et sa carte d'arrivée.</summary>
    public (int X, int Y)? MapCoordOf(long mapId) => _mapPositions?.Get(mapId) is (int x, int y) ? (x, y) : GameMapPosition(mapId) is { } g ? (g.X, g.Y) : null;

    /// <summary>Calque (worldMap) OU se trouve actuellement le perso, ou null si inconnu. Sert a verrouiller la resolution
    /// coord->carte sur le bon monde et a savoir si les zaaps/havre-sac sont disponibles (uniquement worldMap 1).</summary>
    public int? CurrentWorldMap(uint pid) => CurrentMap(pid) is long m ? WorldMapOf(m) : null;

    /// <summary>Si <paramref name="mapId"/> n'est PLUS un nœud du world-graph du client ACTUEL (carte retirée/renumérotée
    /// par un patch Dofus mais toujours présente dans map-positions), renvoie la carte à la MÊME coordonnée (x,y) qui EST
    /// le nœud vivant du graphe (le mieux connecté) ; sinon renvoie <paramref name="mapId"/> inchangé (coût nul si déjà un
    /// nœud). Rend le voyage robuste aux ids de carte périmés d'un vieux script — ex 171443204 (-15,39, orphelin du graphe
    /// actuel) → 178257922 (même coord, nœud vivant). Un bot tiers « marche » car il embarque un worldgraph.json GELÉ; nous
    /// extrayons le graphe VIVANT, donc on résout l'équivalence par coordonnée.</summary>
    public long GraphMapForId(long mapId)
    {
        if (mapId <= 0 || WorldGraphKnows((int)mapId)) return mapId;                 // déjà un nœud -> inchangé
        if (_mapPositions?.Get(mapId) is not (int x, int y) || (x == 0 && y == 0)) return mapId;   // coord inconnue OU sentinelle (0,0 = coord non déterminée à l'extraction) -> NE PAS résoudre : MapIdAt(0,0) renverrait la carte du monde en (0,0) et un follower irait errer là-bas
        return MapIdAt(x, y, WorldMapOf(mapId)) ?? mapId;                            // nœud le mieux connecté à (x,y), MÊME calque
    }

    // ============ ÉTAT DES QUÊTES : voir LaunchService.LotI.cs (CriterionMet, snapshot QuestsEvent, quêtes en cours) ============

    // Cellule de TRANSITION (mapChangeData porte le bit de la direction) marchable la plus proche de source, ou -1.
    private int NearestTransitionCell(long mapId, int source, MapDir dir)
    {
        var cells = _cellReader.LoadCells(mapId);
        if (cells is null || MapChangeMask(dir) == 0) return -1;
        int best = -1, bestD = int.MaxValue;
        foreach (var c in MapExitCells.Candidates(cells, dir))   // bit cardinal ; sinon bits diagonaux du bord (ex zaap de Bonta)
        {
            int d = MapPathfinder.Distance(source, c.CellId);
            if (d < bestD) { bestD = d; best = c.CellId; }
        }
        return best;
    }

    // Cellule de TRANSITION marchable la plus proche de source ET REELLEMENT ATTEIGNABLE (KeyMovements non vide, avec la
    // regle de secteur). -1 si aucune candidate atteignable. Evite de marcher vers une transition d'un secteur deconnecte
    // (cause du move ignore par le serveur). A* n'est evalue que pour les candidates plus proches que la meilleure courante.
    private int NearestReachableTransitionCell(long mapId, int source, MapDir dir, Func<int, bool> isWalkable, Func<int, int, bool>? stepAllowed)
    {
        var cells = _cellReader.LoadCells(mapId);
        if (cells is null || MapChangeMask(dir) == 0) return -1;
        int best = -1, bestD = int.MaxValue;
        foreach (var c in MapExitCells.Candidates(cells, dir))   // bit cardinal ; sinon bits diagonaux du bord (ex zaap de Bonta)
        {
            if (c.CellId == source) return source;                       // deja sur une transition -> atteignable
            int d = MapPathfinder.Distance(source, c.CellId);
            if (d >= bestD) continue;                                     // deja une meilleure candidate atteignable
            if (MapPathfinder.KeyMovements(source, c.CellId, isWalkable, allowDiag: true, stepAllowed).Count == 0) continue;
            bestD = d; best = c.CellId;
        }
        return best;
    }

    /// <summary>Cellule de bord REELLEMENT ATTEIGNABLE pour franchir la transition world-graph <paramref name="tr"/> (bord
    /// type 1/2) depuis <paramref name="source"/>. Le graphe ne fournit qu'UNE cellule representative par arete, souvent sur
    /// un etage inaccessible (elevation) -> on cherche une AUTRE cellule du MEME bord (meme direction) qui soit atteignable
    /// a pied. Repli sur <c>tr.CellId</c> si aucune prouvee (comportement historique). Le voyage multi-cartes (BFS) l'utilise
    /// pour ne pas echouer sur la cellule figee quand une autre cellule du bord passe — comme ChangeMapDirection le fait deja
    /// pour le pas-a-pas directionnel.</summary>
    public int ReachableEdgeCell(long mapId, int source, MapTransition tr, Func<int, bool> isWalkable, Func<int, int, bool>? stepAllowed)
    {
        // Carte d'ARRIVÉE à PLUSIEURS secteurs (cache v5) : la cellule de sortie décide du secteur d'arrivée (miroir du bord),
        // et le trajet par secteur a choisi CELLE-CI pour sa zone d'arrivée -> on la suit si elle est atteignable. Une voisine
        // plus proche du même bord peut déposer dans un autre secteur (172753414 sud : 542 -> secteur 1, 533 -> secteur 2) et
        // rendre le saut suivant impossible (aller-retour + arête bannie à tort).
        if (tr.ArrivalZone > 0 && (_worldGraph?.SoleZone(tr.Arrival) ?? 0) == -1
            && (uint)tr.CellId < MapPathfinder.CellCount && isWalkable(tr.CellId)
            && (source == tr.CellId || MapPathfinder.KeyMovements(source, tr.CellId, isWalkable, allowDiag: true, stepAllowed).Count > 0))
            return tr.CellId;
        MapDir? d = tr.Direction switch { 0 => MapDir.Right, 2 => MapDir.Bottom, 4 => MapDir.Left, 6 => MapDir.Top, _ => (MapDir?)null };
        if (d is MapDir dir)
        {
            int rc = NearestReachableTransitionCell(mapId, source, dir, isWalkable, stepAllowed);
            if (rc >= 0) return rc;
        }
        return tr.CellId;
    }

    // Bit mapChangeData d'une direction cardinale (Dofus) : Est/Droite=1, Sud/Bas=4, Ouest/Gauche=16, Nord/Haut=64.
    public static int MapChangeMask(MapDir d) => MapExitCells.CardinalMask(d);

    // Cellule candidate (fleche de bord) la plus proche de source : marchable en priorite, sinon la + proche.
    private static int PickNearestWalkable(IEnumerable<int> cands, int source, Func<int, bool> isWalkable)
    {
        int best = -1, bestD = int.MaxValue, any = -1, anyD = int.MaxValue;
        foreach (var c in cands)
        {
            if ((uint)c >= MapPathfinder.CellCount) continue;
            int d = MapPathfinder.Distance(source, c);
            if (d < anyD) { anyD = d; any = c; }
            if (isWalkable(c) && d < bestD) { bestD = d; best = c; }
        }
        return best >= 0 ? best : any;
    }

    private bool SendMapChange(uint pid, int targetMap)
    {
        if (targetMap <= 0) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        byte[] pkt = MoveForge.BuildMapChangeFramed(targetMap);   // MapChangeRequest, map_id de destination (code et champ resolus par nom)
        if (pkt.Length == 0)
        {
            TransportLog.Line($"[map] pid {pid} : MapChangeRequest non forgé ({MoveForge.LastForgeError ?? "mapping absent"}), aucun envoi");
            return false;
        }
        bool ok;
        try { ok = relay.Inject(pid, pkt); }
        catch (Exception e)
        {
            TransportLog.Line($"[map] pid {pid} : dispatch MapChangeRequest impossible ({e.GetType().Name})");
            ok = false;
        }
        if (ok) ShowInjected(pid, pkt);
        TransportLog.Line($"[map] pid {pid} : changement de carte -> {targetMap} (MapChangeRequest) inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    // Confirmation d'arrivee de SECOURS (jqi) : appelee a la reception de l'echo serveur (jsj) d'un move injecte. On attend
    // un court instant pour laisser le VRAI client confirmer de lui-meme (moves courts) ; s'il ne l'a pas fait (et que le
    // serveur n'a ni finalise jsq ni refuse jqt), on injecte le jqi NOUS-MEMES -> le serveur COMMIT le deplacement, puis
    // renvoie jsq -> le jqk/iwo en attente peut enfin partir. (ForgeAndSendMapMovementConfirmRequest, fallback).
    private async Task ConfirmMoveFallbackAsync(uint pid, long seq, int dest, int cells, IReadOnlyList<int>? serverPath = null)
    {
        // Ne jamais accuser l'écho d'un AUTRE acteur arrivant par hasard sur
        // la même cellule, ni un écho d'une ancienne carte. Le handler filtre
        // par ActorId si connu ; ici nous exigeons aussi les extrémités du
        // trajet réellement accepté (et la séquence encore en vol).
        if (!_moveEchoCorrelation.TryGetValue(pid, out var move) ||
            move.Seq != seq ||
            !_requestedMovePaths.TryGetValue(pid, out var request) || request.Seq != seq ||
            !MovementEchoPolicy.Matches(request.Path, serverPath) ||
            move.Target != dest ||
            (_serverMapId.TryGetValue(pid, out var serverMap) && serverMap != 0 &&
             serverMap != move.MapId))
        {
            TransportLog.Line($"[move] pid {pid} : écho serveur non corrélé au trajet #{seq}, jqi non programmé");
            return;
        }
        if (serverPath![^1] != dest)
        {
            _shortenedMoveEnds[pid] = (seq, serverPath[^1]);
            TransportLog.Line($"[move] pid {pid} : chemin serveur raccourci #{seq}, destination demandée {dest}, arrêt accepté {serverPath[^1]} ; confirmation du trajet reçu, aucune action d'arrivée présumée");
            _pendingMapChange.TryRemove(pid, out _);
            _pendingHarvest.TryRemove(pid, out _);
            _pendingAttack.TryRemove(pid, out _);
        }
        int delay;
        if (serverPath is { Count: > 1 } && (IsSocketPid(pid) || IsTurboAckAll(pid)))
        {
            // Socket OU MITM Turbo : confirmation calée sur la durée d'animation client (avec speedhack natif)
            int anim = ClientMoveDurationMs(serverPath, IsTurboAckAll(pid));
            int jitter = IsTurboAckAll(pid) ? System.Random.Shared.Next(4, 11) : System.Random.Shared.Next(10, 21);
            delay = anim + jitter;
            TransportLog.Line($"[move] pid {pid} : confirmation calée client ({serverPath.Count - 1} pas, {(serverPath.Count - 1 <= WalkMaxSteps ? "marche" : "course")}) : " +
                              $"animation {anim} ms depuis écho serveur (enfilement Socket/MITM Turbo) -> jqi dans {delay} ms");
        }
        else
        {
            // MITM non-turbo : attend ~ la duree d'animation du trajet AVANT de confirmer l'arrivee
            delay = Math.Clamp(cells * MoveConfirmPerCellMs + MoveConfirmMarginMs, MoveConfirmMinMs, MoveConfirmMaxMs);
        }
        await Task.Delay(delay).ConfigureAwait(false);
        // On ne confirme que si CE move (meme num de sequence) est TOUJOURS l'unique en attente. Retrait ATOMIQUE de la paire
        // exacte : si un move plus recent l'a remplacee (meme case mais autre sequence), TryRemove(kvp) echoue -> pas de
        // confirm fantome (c'etait la cause des rollbacks intermittents : la route sort toujours par la meme case 541/543).
        if (_moveConfirmSubmitted.TryGetValue(pid, out var submittedSeq) && submittedSeq == seq) return;
        if (!_confirmDispatching.TryAdd(pid, seq)) return;
        try
        {
            // Un jqi REFUSÉ au dispatch n'a pas d'ACK et le serveur n'est pas
            // tenu de renvoyer l'écho jsj : une unique tâche qui se termine
            // sur false laisserait le bot coincé jusqu'au reconnect. Deux
            // réessais bornés sont autorisés seulement pour le même seq avec
            // écho déjà validé, sans jamais simuler un jsq.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0) await Task.Delay(250 * attempt).ConfigureAwait(false);
                if (!_botMoveConfirm.TryGetValue(pid, out var cur) || cur.Seq != seq ||
                    !_moveEchoCorrelation.TryGetValue(pid, out var tracked) || tracked.Seq != seq ||
                    (_moveConfirmSubmitted.TryGetValue(pid, out submittedSeq) && submittedSeq == seq))
                    break;
                if (IsSocketPid(pid) && !_socketInjectors.ContainsKey(pid)) break;
                if (!InjectConfirm(pid))
                {
                    TransportLog.Line($"[move] pid {pid} : jqi seq {seq} non enfilé, tentative {attempt + 1}/3 (aucun ACK supposé)");
                    continue;
                }
                // Une injection seulement MISE EN FILE ne certifie pas la
                // réception. Ne retirer _botMoveConfirm qu'à l'ACK/refus réel
                // (handler OnDecoded), et empêcher un second jqi sur ce seq.
                _moveConfirmSubmitted[pid] = seq;
                _moveConfirmInjectedSeq[pid] = seq;
                break;
            }
        }
        finally
        {
            _confirmDispatching.TryRemove(new KeyValuePair<uint, long>(pid, seq));
        }
    }

    // Injecte le MapMovementConfirmRequest (jqi, trame VIDE) : COMMIT du deplacement precedent cote serveur.
    private bool InjectConfirm(uint pid)
    {
        var relay = RelayForInject(capExempt: true);   // commit d'un déplacement déjà parti (rôle client en Socket)
        if (relay is null) return false;
        byte[] jqi = MoveForge.BuildConfirmFramed();
        if (jqi.Length == 0)
        {
            TransportLog.Line($"[move] pid {pid} : MapMovementConfirmRequest NON forgé ({MoveForge.LastForgeError ?? "code absent"}), aucun envoi");
            return false;
        }
        bool ok;
        try { ok = relay.Inject(pid, jqi); }
        catch (Exception e)
        {
            TransportLog.Line($"[move] pid {pid} : dispatch confirmation impossible ({e.GetType().Name})");
            ok = false;
        }
        if (ok) ShowInjected(pid, jqi);
        TransportLog.Line(_socketInjectors.ContainsKey(pid)
            ? $"[move] pid {pid} : confirmation jqi Socket dispatch={(ok ? "ACCEPTE" : "REFUSE")} (ACK jsq encore requis)"
            : $"[move] pid {pid} : confirmation jqi de secours dispatch={(ok ? "ACCEPTE" : "REFUSE")} (ACK jsq encore requis)");
        return ok;
    }
}
