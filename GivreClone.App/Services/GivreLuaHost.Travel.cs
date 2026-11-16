using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Engine.LuaTooling;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ===================== Voyage : changement de carte, voyage multi-cartes (world-graph, zaaps, zaapis, havre-sac), GoToMapId / PlanPath / Reroute =====================
public sealed partial class GivreLuaHost
{
    public (bool Ok, string Reason)? LastTravelResult { get; private set; }
    private int _travelResultDepth;
    private string? _travelServerRefusal;
    private string? _lastTravelFailure;

    internal static DynValue CreateTravelToResult(bool ok, string? failureReason)
        => ok ? DynValue.True : DynValue.NewTuple(DynValue.False, DynValue.NewString(failureReason ?? "action_failed"));

    // Nested reroutes and shortcut retries belong to the same user action.
    private bool ReportTravel(Func<bool> action)
    {
        if (_travelResultDepth++ > 0)
        {
            try { return action(); }
            finally { _travelResultDepth--; }
        }
        LastTravelResult = null;
        _travelServerRefusal = null;
        _lastTravelFailure = null;
        string? failure = null, success = null;
        void Capture(LuaLogEntry entry)
        {
            if (entry.Category is not ("trajet" or "carte" or "hdv")) return;
            if (entry.Level is LuaLogLevel.Warning or LuaLogLevel.Danger)
            {
                if (entry.Category != "hdv" || failure is null) failure = entry.Message;
            }
            else if (entry.Level == LuaLogLevel.Success) success = entry.Message;
        }
        Log += Capture;
        bool ok = false;
        try { return ok = action(); }
        finally
        {
            Log -= Capture;
            _travelResultDepth--;
            LastTravelResult = (ok, ok ? success ?? QuickActions.AlreadyHere
                : _travelServerRefusal ?? failure ?? QuickActions.Interrupted);
        }
    }

    // ------------------------------------------------------------------ verbes
    private bool GoToCell(int cell, CancellationToken ct)
    {
        ThrowIfStopping(ct);
        WaitSelfReady(ct);   // attend la position self confirmee (surtout juste apres un changement de carte)
        var st = _world.Get(_pid);
        long map = _launch.CurrentMap(_pid) ?? 0;
        if (map == 0 || st.SelfCell is not int src) { Warn("deplacement", Loc.Fmt("L.Lua.MoveUnknownCell", cell)); return false; }
        if (src == cell) return true;
        Info("deplacement", Loc.Fmt("L.Lua.MoveGoing", cell));
        var outcome = _launch.SendAndWaitAsync(_pid, () => _launch.MoveTo(_pid, map, src, cell, st.IsWalkableForScript, allowDiag: true, st.StepAllowed), MoveTimeoutMs, ct)
                             .GetAwaiter().GetResult();
        ThrowIfStopping(ct);
        switch (outcome)
        {
            case LaunchService.MoveOutcome.Arrived: Ok("deplacement", Loc.Fmt("L.Lua.MoveArrived", cell)); return true;
            case LaunchService.MoveOutcome.Refused: Warn("deplacement", Loc.Fmt("L.Lua.MoveRefused", cell)); return false;
            case LaunchService.MoveOutcome.NotSent: Warn("deplacement", Loc.Fmt("L.Lua.MoveNoPath", cell)); return false;
            default: Warn("deplacement", Loc.Fmt("L.Lua.MoveTimeout", cell)); return false;
        }
    }

    // follow=true (rejeu de sortie d'une MULE) : FAIL-FAST — 1 seule tentative + timeout court. Une direction bloquée pour la
    // mule (bord infranchissable depuis SA position) ne doit PAS geler ~48 s (3×16 s) : l'appelant retombe vite sur la CHASSE
    // graphe (les cartes de jeu de rôle sont dans le graphe). C'était la cause du « la mule met longtemps à se déplacer ».
    // Un ÉCHANGE HDV encore OUVERT fait REFUSER tout déplacement par le serveur (« marche … non aboutie (Refused) »). On le
    // ferme (DialogLeaveRequest/kla) AVANT de bouger — sinon le bot reste coincé après une opération HDV (ex : MAJ d'un prix
    // puis retour en récolte). Best-effort + courte tempo pour laisser le serveur enregistrer la fermeture. Craft/banque se
    // ferment déjà eux-mêmes (LeaveExchangeAsync) ; ici on couvre l'HDV, laissé ouvert par les verbes sale:/goToHdv.
    private void CloseHdvBeforeMove(CancellationToken ct)
    {
        if (_launch.HdvAnyOpen(_pid)) { _launch.LeaveDialog(_pid); InterruptibleDelay(500, ct); }
    }

    private bool ChangeMap(string dir, CancellationToken ct, bool follow = false)
    {
        ThrowIfStopping(ct);
        CloseHdvBeforeMove(ct);   // un HDV ouvert bloque le déplacement -> le fermer d'abord
        // "top" OU "top(123)"; forcedCell = -1 sans parenthèses.
        if (!LuaCompat.TryParseDirToken(dir, out var d, out int forcedCell)) { Warn("carte", Loc.Fmt("L.Lua.MapBadDir", dir)); return false; }
        int attempts = follow ? 1 : MapChangeAttempts;
        int attemptTimeout = follow ? 6000 : MapChangeAttemptTimeoutMs;
        // ÉQUIPE : le chef ATTEND que les followers soient sur sa carte AVANT de la quitter (voyage groupé -> ils partent
        // ensemble). Borné par TeamWaitTimeoutMs (jamais de blocage permanent) ; pendant l'attente le TeamPilot les fait venir.
        // No-op si ce bot n'est pas un chef d'équipe avec l'option activée (snapshot vide). Un follower n'est jamais gaté ici.
        if (_launch.TeamMapChangeWaitPending(_pid))
        {
            Info("trajet", Loc.T("L.Lua.TeamWaitMapChange"));
            if (!WaitForTeamDeparture(ct)) return false;
        }
        // SUIVI COLLÉ (SYNC_MOVE) : APRÈS la barrière (les mules sont co-localisées), on diffuse la SORTIE EXACTE (direction)
        // -> elles rejouent ChangeMap(dir) SANS world-graph ni coordonnées : marche en zone OFF-GRAPH (Incarnam/instance) où
        // le pathfinding échoue. L'orchestrateur ne fan que pour un CHEF (pid d'un follower ignoré). C'est le mécanisme un bot tiers.
        if (!WaitForTeamDeparture(ct)) return false;
        _launch.NotifyMapExit(_pid, dir);
        _lastExitDir = d;   // memorise la sortie prise -> anti-retour des sorties multiples (ChoosePath)
        long startMap = _launch.CurrentMap(_pid) ?? 0;

        // Sortie directionnelle NON franchissable par le BORD (marche jusqu'au bord + jqk) : si le world-graph décrit
        // cette direction comme un INTERACTIF/ESCALIER (type 32) ou un SOLEIL (type 8), la carte n'a PAS de cellule
        // mapChangeData de bord (ex. Village de Pandala 206307328 « top » : escalier interactif cellule 50 -> carte
        // 209190912, zéro cellule de bord haut). Seul le voyage type-aware GoToMapId sait franchir (iwo/porte pour un
        // escalier, marche-sur-soleil) -> on délègue, vers la VRAIE carte cible du graphe (pas le voisin géométrique
        // mapData, qui n'est pas atteignable ici). Les vraies sorties de BORD (type 1/2) gardent la voie rapide.
        // Cellule FORCÉE par l'utilisateur -> l'arête est prise littéralement (pas de délégation au voyage type-aware).
        if (forcedCell < 0 && startMap != 0 && _launch.WorldTransitionInDirection(startMap, d) is { Type: 32 or 8 } wtr)
        {
            Info("carte", Loc.Fmt("L.Lua.MapViaInteractive", dir, wtr.Arrival));
            return GoToMapId(wtr.Arrival, ct);
        }
        // Carte à PLUSIEURS SECTEURS : la sortie de bord existe dans le graphe mais pas dans le secteur du perso. Ex 126093080
        // (-2,-53) arrivé par le sud : le bord est (cellule 251) est dans le secteur HAUT, injoignable ; le seul passage vers
        // -1,-53 est la PORTE 302. Sans ce garde, changeMap("right") échouait 3 fois (« pas de sortie right ») alors qu'un
        // `path` NON adjacent passait (le voyage, lui, planifie par secteur). On planifie donc depuis le secteur du perso et,
        // si le plan n'est pas ce bord direct, on délègue au voyage (porte/escalier/détour).
        if (forcedCell < 0 && !follow && startMap != 0 && _launch.WorldSoleZone(startMap) < 0
            && _launch.WorldTransitionInDirection(startMap, d) is { } dtr)
        {
            var plan = PlanPath(startMap, dtr.Arrival, new HashSet<int>(), new HashSet<long>(), ct);
            bool directBorder = plan.Count == 1 && plan[0].Type is 1 or 2 && plan[0].Direction == dtr.Direction;
            if (plan.Count > 0 && !directBorder)
            {
                Info("carte", Loc.Fmt("L.Lua.MapViaSector", dir, dtr.Arrival));
                return GoToMapId(dtr.Arrival, ct);
            }
        }

        // Juste apres un chargement de carte, le 1er MapMovementRequest de transition est PARFOIS ignore par le
        // client/serveur (pas encore prets) : aucun jsq -> le jqk n'est jamais envoye -> la carte ne change pas.
        // Plutot que d'attendre 22 s dans le vide (et de laisser un changement "en attente" parasiter le move
        // suivant), on RE-TENTE : la garde de deplacement (4 s) retombe entre deux essais, et un eventuel refus (jqt)
        // recale la position self reelle -> l'essai suivant repart de la bonne cellule.
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            ThrowIfStopping(ct);
            WaitSelfReady(ct);   // NE PAS calculer sur la cellule d'exit PERIMEE de l'ancienne carte (attend le jss d'entree)
            var st = _world.Get(_pid);
            long map = _launch.CurrentMap(_pid) ?? 0;
            if (map != 0 && map != startMap) { Ok("carte", Loc.Fmt("L.Lua.MapNew", map)); InterruptibleDelay(MapSettle(), ct); return true; }   // deja change (essai precedent abouti tardivement)
            if (map == 0 || st.SelfCell is not int src) { Warn("carte", Loc.T("L.Lua.MapSelfNotConfirmed")); InterruptibleDelay(800, ct); continue; }

            Info("carte", attempt == 1 ? Loc.Fmt("L.Lua.MapChanging", dir)
                                       : Loc.Fmt("L.Lua.MapChangingRetry", dir, attempt, attempts));
            bool sent = forcedCell >= 0
                ? _launch.ChangeMapDirectionViaCell(_pid, map, src, d, forcedCell, st.IsWalkableForScript, st.StepAllowed, st.SelfCellConfirmed)
                : _launch.ChangeMapDirection(_pid, map, src, d, st.IsWalkableForScript, st.StepAllowed, st.SelfCellConfirmed);
            if (!sent)
            {
                Warn("carte", Loc.Fmt("L.Lua.MapNoExit", dir));
                if (follow) return false;   // mule : direction bloquée -> on rend la main tout de suite (chasse graphe en repli)
                InterruptibleDelay(1200, ct);   // laisse la garde de deplacement (4 s) retomber avant de re-tenter
                continue;
            }
            // Bloquant tant que la carte n'a pas change (marche jusqu'au bord + jqk), PAR tentative.
            if (WaitUntil(() => (_launch.CurrentMap(_pid) ?? 0) is var now && now != 0 && now != map, attemptTimeout, ct))
            {
                Ok("carte", Loc.Fmt("L.Lua.MapNew", _launch.CurrentMap(_pid)));
                InterruptibleDelay(MapSettle(), ct);   // le serveur refuse encore les moves un court instant apres une nouvelle carte
                return true;
            }
            Warn("carte", Loc.Fmt("L.Lua.MapChangeUnconfirmed", dir, attempt, attempts));
        }
        Warn("carte", Loc.Fmt("L.Lua.MapChangeFailed", dir, attempts));
        // Dernier recours : le bord direct coince (secteur mal connu, élévation…) alors que le graphe connaît la carte voisine
        // -> voyage world-graph vers CETTE carte (porte/escalier/détour), au lieu de reboucler sur la même direction.
        if (!follow && forcedCell < 0 && startMap != 0 && (_launch.CurrentMap(_pid) ?? 0) == startMap
            && _launch.WorldTransitionInDirection(startMap, d) is { } ftr)
        {
            Info("carte", Loc.Fmt("L.Lua.MapFallbackTravel", dir, ftr.Arrival));
            return GoToMapId(ftr.Arrival, ct);
        }
        InterruptibleDelay(1500, ct);   // cooldown : evite de re-tenter en rafale une direction qui coince
        return false;
    }

    // ============ VOYAGE MULTI-CARTES (Lot 3) : BFS world-graph + enchainement des sauts ============
    // Depuis n'importe ou, va jusqu'a la carte `targetMap` en franchissant les transitions du world-graph : BORD (type 1)
    // = marche jusqu'a la cellule + jqk ; PORTE (type 32) = utilise l'interactif de la cellule (iwo) qui change la carte.
    // Bloquant (fait tout le trajet). false si aucun chemin (carte hors graphe / zaap-bateau requis) ou saut echoue.
    // Zaap connu (parmi KnownZaapMaps) qui MINIMISE la marche restante APRÈS téléport vers la cible, + le nb de sauts
    // restants. null si aucun zaap utile. Métrique = sauts BFS world-graph (le vrai coût de marche).
    // allowGated : le BFS zaap->cible peut emprunter une porte/PNJ CONDITIONNEL dont la quête est DÉJÀ faite (WorldPath avec
    //   _pid, mêmes règles que la marche depuis la carte courante). Réservé au cas « cible injoignable à pied » : sans ça un
    //   zaap parfaitement utile était écarté (« aucun zaap ») alors que le perso pouvait passer. PAS pour la comparaison
    //   « zaap plus court que la marche » (on préférerait sinon zaap + passage conditionnel à une marche LIBRE qui marche).
    // coordFallbackFrom (> 0 = carte COURANTE) : active le REPLI COORDONNÉE (sondage des trajets plafonnés uniquement).
    // viaCoord = le zaap vient du repli coordonnée (et non d'un chemin prouvé) -> mémorisé à l'arrivée (anti-oscillation).
    // ⚠ PAS de normalisation GraphMapForId des ids de zaap : on atterrit sur l'id d'origine, que le voyage (cur non
    //   normalisé) ne saurait pas router -> bot échoué hors graphe (vérif adverse). Un zaap hors graphe est ignoré, comme avant.
    private (long zaapMap, int hops, bool viaCoord)? NearestZaapFor(long targetMap, bool allowGated = false, long coordFallbackFrom = 0)
    {
        long best = 0; int bestHops = int.MaxValue;
        foreach (long z in _launch.KnownZaapMaps(_pid))
        {
            if (z == targetMap) return (z, 0, false);                // zaap SUR la cible -> imbattable
            if (!_launch.WorldGraphKnows((int)z)) continue;
            int hops = _launch.WorldPath((int)z, (int)targetMap, null, allowGated ? _pid : 0u).Count;
            if (hops == 0) continue;                                  // injoignable à pied depuis ce zaap
            if (hops < bestHops) { bestHops = hops; best = z; }
        }
        if (best != 0) return (best, bestHops, false);
        if (coordFallbackFrom > 0 && NearestZaapByCoord(targetMap, coordFallbackFrom) is { } c) return (c.zaapMap, c.hops, true);
        return null;
    }

    private (long target, long zaap) _lastCoordFallbackLog;   // anti-spam du log de repli coordonnée (même paire -> une fois)
    // Zaaps ATTEINTS via le repli coordonnée pendant l'épisode hors-trajet courant : exclus des choix suivants (anti-oscillation
    // A->B->A quand la « carte de route la plus proche » change à chaque tick). Vidé au retour sur la route et au (re)démarrage.
    private readonly HashSet<long> _coordFallbackVisited = new();

    // REPLI COORDONNÉE (autonomie : plus besoin d'un NEAREST_ZAAP écrit dans le script). Quand AUCUN zaap connu ne rejoint
    // la cible À PIED dans le world-graph (trou d'arête autour de la cible, ou zone séparée par l'eau que la marche du graphe
    // ne franchit pas), on choisit le zaap connu dont la COORDONNÉE est la plus proche de la cible — MÊME CALQUE que la cible
    // — et SEULEMENT s'il nous en RAPPROCHE STRICTEMENT par rapport à la carte courante (jamais de téléport à reculons ni sur
    // place). Après le téléport, le trajet se ré-évalue depuis la nouvelle carte (généralement reliée à pied). hops = distance
    // coord (estimation, pas un vrai compte de sauts) : n'est utilisé que dans le cas injoignable (originalWalk == 0), où tout
    // zaap qui rapproche est accepté. Aucun effet sur les trajets qui marchent déjà (branche walk-connectée prioritaire).
    private (long zaapMap, int hops)? NearestZaapByCoord(long targetMap, long fromMap)
    {
        // Réservé aux TROUS entre nœuds CONNUS du graphe : une cible hors graphe (intérieur/instance) partage la coordonnée
        // de l'extérieur -> un téléport « près » d'elle ne la rendrait pas plus joignable (voyage perdu).
        if (!_launch.WorldGraphKnows((int)targetMap)) return null;
        if (_launch.MapCoordOf(targetMap) is not (int tx, int ty)) return null;
        int? tw = _launch.WorldMapOf(targetMap);
        // distance de départ : carte courante -> cible (même calque). Calque différent / coord inconnue = aucune borne
        // (tout zaap du calque de la cible nous rapproche forcément d'un point de vue « bon calque »).
        int curD = _launch.MapCoordOf(fromMap) is (int cx, int cy) && (tw is not int twc || _launch.WorldMapOf(fromMap) is not int fw || fw == twc)
            ? System.Math.Abs(cx - tx) + System.Math.Abs(cy - ty) : int.MaxValue;
        long best = 0; int bestD = int.MaxValue;
        foreach (long z in _launch.KnownZaapMaps(_pid))
        {
            if (z == fromMap) continue;                                                   // pas de téléport sur place
            if (_coordFallbackVisited.Contains(z)) continue;                              // déjà atteint cet épisode (anti-oscillation)
            if (!_launch.WorldGraphKnows((int)z)) continue;                               // on doit pouvoir marcher depuis l'arrivée
            if (_launch.MapCoordOf(z) is not (int zx, int zy)) continue;
            if (tw is int twv && _launch.WorldMapOf(z) is int zw && zw != twv) continue;  // même calque que la cible (Incarnam ≠ Monde)
            int d = System.Math.Abs(zx - tx) + System.Math.Abs(zy - ty);
            if (d >= curD) continue;                                                      // ne nous rapproche pas -> inutile
            if (d < bestD) { bestD = d; best = z; }
        }
        if (best == 0) return null;
        if (_lastCoordFallbackLog != (targetMap, best))
        {
            _lastCoordFallbackLog = (targetMap, best);
            Info("trajet", Loc.Fmt("L.Lua.TravelZaapCoordFallback", targetMap, best, bestD));
        }
        return (best, bestD);
    }

    // Zaap depuis la carte du monde (capture 31/10) : TeleportRequest map_id + source_type 5, sans zaap au sol ni havre-sac.
    // Le raccourci automatique exige un coût annoncé et payable. Le menu peut envoyer un zaap débloqué dont le coût
    // n'est pas encore connu. Succès seulement si la carte observée devient la destination. Sinon, échec en français.
    // Un envoi sans arrivée suspend le raccourci auto 5 min.
    private long _directZaapPausedUntil;
    private (long target, string reason) _lastWorldMapRefusal;
    private (long target, string reason) _lastZaapiRefusal;
    private readonly Dictionary<long, string> _refusedZaaps = new();

    // Subscribe before sending: a Socket refusal can arrive during Inject. Keep the FIRST fresh
    // server message, not a later message from closing the dialog or leaving the haven bag.
    private bool ConfirmZaapTeleport(long destMap, Func<bool> send, CancellationToken ct, out string? reason,
        int timeoutMs = MapChangeAttemptTimeoutMs)
    {
        if (_refusedZaaps.TryGetValue(destMap, out reason)) { _travelServerRefusal = reason; return false; }
        long textSeq = _launch.LastTextInfo(_pid)?.Seq ?? 0;
        LaunchService.TextInfo? refusal = null;
        void Observe(DecodedPacket packet)
        {
            if (packet.Pid == _pid && !packet.ClientToServer && packet.Name == "TextInformationEvent"
                && _launch.LastTextInfo(_pid) is { } text && text.Seq > textSeq)
                Interlocked.CompareExchange(ref refusal, text, null);
        }
        _launch.PacketDecoded += Observe;
        try
        {
            if (!send()) return false;
            // A system message can also accompany a SUCCESSFUL teleport. Only use it as the
            // refusal reason when the map confirmation actually expires.
            WaitUntil(() => (_launch.CurrentMap(_pid) ?? 0) == destMap, timeoutMs, ct);
            if ((_launch.CurrentMap(_pid) ?? 0) == destMap) return true;
            if (Volatile.Read(ref refusal) is { } text)
            {
                reason = _launch.GameNames.InfoMessageText(text.MessageType, text.MessageId, text.Params);
                if (string.IsNullOrWhiteSpace(reason))
                    reason = $"message serveur {text.MessageType}/{text.MessageId}"
                        + (text.Params.Count > 0 ? " : " + string.Join(", ", text.Params) : "");
                _refusedZaaps[destMap] = reason;
                _travelServerRefusal = reason;
            }
            return false;
        }
        finally { _launch.PacketDecoded -= Observe; }
    }

    private void ExplainWorldMap(long target, string reason)
    {
        if (_lastWorldMapRefusal == (target, reason)) return;
        _lastWorldMapRefusal = (target, reason);
        Info("trajet", reason);
    }

    private bool TravelFromWorldMap(long destMap, CancellationToken ct, bool allowUnknownCost = false)
    {
        if (destMap <= 0) return false;
        ThrowIfStopping(ct);
        if (_launch.IsInFight(_pid)) { Warn("trajet", WorldMapZaapTravel.CombatReason); return false; }
        if (WorldMapZaapTravel.Arrived(_launch.CurrentMap(_pid) ?? 0, destMap)) return true;
        return CommitWorldMapZaap(destMap, honorPause: false, ct, allowUnknownCost);
    }

    private bool CommitWorldMapZaap(long destMap, bool honorPause, CancellationToken ct, bool allowUnknownCost = false)
    {
        ThrowIfStopping(ct);
        if (honorPause && Environment.TickCount64 < _directZaapPausedUntil) return false;
        if (_launch.IsInFight(_pid)) return false;
        var offer = _launch.AssessWorldMapZaap(_pid, destMap);
        bool listedWithoutCost = allowUnknownCost && offer.Known && offer.CostKamas is null;
        if (!offer.CanSend && !listedWithoutCost) { Warn("trajet", offer.Reason); return false; }
        CloseHdvBeforeMove(ct);
        Info("trajet", listedWithoutCost ? QuickActions.CostLater : offer.Reason);
        bool sent = false;
        if (!ConfirmZaapTeleport(destMap, () => sent = _launch.TrySendWorldMapZaap(_pid, destMap, allowUnknownCost), ct, out var reason, 8000))
        {
            if (honorPause && sent) _directZaapPausedUntil = Environment.TickCount64 + 5 * 60_000;
            Warn("trajet", (!sent && reason is null ? WorldMapZaapTravel.SendFailedReason : WorldMapZaapTravel.UnconfirmedReason(destMap))
                + (reason is null ? "" : " : " + reason));
            return false;
        }
        InterruptibleDelay(MapSettle(), ct);
        Ok("trajet", WorldMapZaapTravel.ArrivedReason(destMap, offer.CostKamas));
        return true;
    }

    private bool TravelViaWorldMapZaap(long targetMap, int originalWalk, bool unreachable, int margin, bool explainEmpty, bool honorPause, CancellationToken ct)
    {
        // L'échec auto suspend 5 min. zaapcarte / le choix « Zaap depuis la carte » réessaient tout de suite.
        if (honorPause && Environment.TickCount64 < _directZaapPausedUntil) return false;
        var quotes = _launch.WorldMapZaapQuotes(_pid);
        if (quotes.Count == 0)
        {
            if (explainEmpty) ExplainWorldMap(targetMap, WorldMapZaapTravel.UnknownListReason);
            return false;
        }
        long here = _launch.CurrentMap(_pid) ?? 0;
        var options = new List<(long MapId, long? CostKamas, int HopsAfter)>();
        foreach (var quote in quotes)
        {
            if (quote.MapId == here || quote.MapId <= 0 || quote.MapId > int.MaxValue) continue;
            int hops;
            if (quote.MapId == targetMap) hops = 0;
            else if (!_launch.WorldGraphKnows((int)quote.MapId)) continue;
            else
            {
                hops = _launch.WorldPath((int)quote.MapId, (int)targetMap, null, unreachable ? _pid : 0u).Count;
                if (hops == 0) continue;
            }
            options.Add((quote.MapId, quote.CostKamas, hops));
        }
        var pick = WorldMapZaapTravel.ChooseDestination(options, _launch.Kamas(_pid), originalWalk, unreachable, margin);
        if (pick is not { CanSend: true } chosen)
        {
            if (pick is { } refused) ExplainWorldMap(targetMap, refused.Reason);
            else if (explainEmpty) ExplainWorldMap(targetMap, WorldMapZaapTravel.NoShortcutReason);
            return false;
        }
        return CommitWorldMapZaap(chosen.MapId, honorPause, ct);
    }

    // Raccourci de voyage : havre-sac -> ouvre le zaap perso -> téléporte au zaap `zaapMap` (le plus proche de la cible).
    // Renvoie true si on est bien arrivé sur `zaapMap` (l'appelant marche ensuite le reste). À CHAQUE échec d'étape ->
    // false : GoToMapId retombe alors sur la marche directe. Séquence : jbn -> attente carte HS -> zaap type 16 (poll,
    // décodé en tâche de fond) -> ouverture (marche adjacent + iwo, comme une PORTE) -> attente hjj (CanTeleportTo) ->
    // hjc (source HAVEN_BAG=3) -> attente arrivée -> stabilisation.
    // probe : sondage d'un trajet PLAFONNÉ (TravelCapped) -> autorise le repli coordonnée quand la cible est injoignable.
    private bool TravelViaZaap(long targetMap, long startMap, int originalWalk, CancellationToken ct, bool probe = false)
    {
        ThrowIfStopping(ct);
        if (_launch.IsInFight(_pid)) return false;
        if (NearestZaapFor(targetMap, allowGated: originalWalk == 0) is { } known
            && _refusedZaaps.TryGetValue(known.zaapMap, out var refused))
        {
            Warn("trajet", $"téléport vers {known.zaapMap} refusé : {refused}");
            return false;
        }
        Info("trajet", $"raccourci zaap : havre-sac -> ouverture du zaap (cible {targetMap})");
        // Un DIALOGUE resté ouvert (PNJ, banquier, zaap) fait REFUSER l'entrée au havre-sac -> on le ferme d'abord (même
        // précaution qu'ExitHavenBag). C'était la 1re cause d'échec « transitoire » qui désactivait ensuite le raccourci.
        _launch.LeaveDialog(_pid);
        InterruptibleDelay(_launch?.IsTurboAckAll(_pid) == true ? 40 : 300, ct);
        if (!_launch.EnterHavenBag(_pid)) return false;                                                   // 1. entrer au havre-sac
        int layer = _launch.WorldMapOf(startMap) ?? int.MinValue;
        if (!WaitUntil(() => (_launch.CurrentMap(_pid) ?? 0) is var m && m != 0 && m != startMap, MapChangeAttemptTimeoutMs, ct))
        {
            // Le havre-sac N'EXISTE PAS partout (Incarnam / instances / donjons) : l'entrée « réussit » (requête envoyée)
            // mais AUCUN changement de carte -> on l'APPREND pour ce calque et on ne retente PLUS (sinon 3× ~19s de blocage
            // à chaque saut, comme signalé). La marche/le court-circuit adjacent (#3) prennent le relais.
            // Le MONDE DES DOUZE (worldMap 1) a TOUJOURS un havre-sac : un échec y est TRANSITOIRE (dialogue ouvert, lag,
            // combat qui démarre) -> ne JAMAIS le blacklister. Sinon UN hoquet supprimait le raccourci havre-sac EN SILENCE
            // pour toute la vie de l'hôte (plus aucun zaap depuis une carte sans zaap au sol). Seuls Incarnam/instances/donjons.
            // On n'apprend qu'après 2 ÉCHECS CONSÉCUTIFS sur ce calque (un seul hoquet ne doit rien désactiver) :
            //  - calque DÉDIÉ (≥ 2 : Incarnam, dimensions…) -> appris INDISPONIBLE pour la vie de l'hôte ;
            //  - Monde des Douze (1) et -1 (intérieurs/banques MÉLANGÉS aux donjons/instances : 3 649 cartes) -> simple
            //    COOLDOWN (un blacklist permanent y supprimait le retour zaap depuis TOUTES les banques).
            if (layer != int.MinValue && (_havenBagFailStreak[layer] = _havenBagFailStreak.GetValueOrDefault(layer) + 1) >= 2)
            {
                _havenBagFailStreak[layer] = 0;
                if (layer >= 2) _noHavenBagWorld.Add(layer);
                else _havenBagCooldownUntil[layer] = Environment.TickCount64 + HavenBagRetryCooldownMs;
            }
            Warn("trajet", "havre-sac indisponible ici (Incarnam/instance ?) -> marche");
            return false;
        }        // 2. attendre la carte HS
        if (layer != int.MinValue) _havenBagFailStreak[layer] = 0;   // entrée réussie -> série d'échecs remise à zéro
        long hbMap = _launch.CurrentMap(_pid) ?? 0;
        _havenBagMap = hbMap;
        _havenBagEntryMap = startMap;
        InterruptibleDelay(MapSettle(), ct);
        WaitSelfReady(ct);
        MapInteractive? zi = null;                                                                        // 3. zaap perso (type 16)
        if (!WaitUntil(() => (zi = _world.Get(_pid).Interactives.FirstOrDefault(i => i.ElementTypeId == 16 && i.HarvestSkill is not null)) is not null, HavenBagInteractiveTimeoutMs, ct) || zi is null)
            { Warn("trajet", "zaap du havre-sac introuvable -> ressort"); ExitHavenBag(startMap, ct); return false; }
        // 4. ouvrir le zaap (iwo)
        var st = _world.Get(_pid);
        if (st.SelfCell is not int src) { ExitHavenBag(startMap, ct); return false; }
        int? stand = _launch.HarvestStandCell(_pid, hbMap, src, zi, st.IsWalkable, st.StepAllowed, out var standWalk);
        if (stand is null) { ExitHavenBag(startMap, ct); return false; }
        if (stand.Value != src)
        {
            var mv = _launch.SendAndWaitAsync(_pid, () => _launch.MoveTo(_pid, hbMap, src, stand.Value, standWalk, true, st.StepAllowed), MoveTimeoutMs, ct).GetAwaiter().GetResult();
            if (mv != LaunchService.MoveOutcome.Arrived) { ExitHavenBag(startMap, ct); return false; }
        }
        long seq0 = _launch.ZaapListSeq(_pid);                                                            // AVANT l'iwo
        if (!_launch.SendHarvest(_pid, zi)) { ExitHavenBag(startMap, ct); return false; }
        if (!WaitUntil(() => _launch.ZaapListSeq(_pid) > seq0, ZaapDestTimeoutMs, ct))                    // 5. une NOUVELLE liste (hjj) arrive
            { Warn("trajet", "liste des zaaps non reçue -> ressort"); ExitHavenBag(startMap, ct); return false; }
        // 6. MAINTENANT qu'on a la liste FRAÎCHE, on choisit le zaap le plus proche de la cible. On ne téléporte QUE s'il
        // rend le trajet plus court (ou si la cible était injoignable à pied) ; sinon on ressort et on marche.
        bool unreachable = originalWalk == 0;
        if (NearestZaapFor(targetMap, allowGated: unreachable, coordFallbackFrom: probe && unreachable ? startMap : 0) is not { } z
            || !(unreachable || z.hops + ZaapWalkMargin < originalWalk))
            { Info("trajet", "aucun zaap plus proche que la marche -> ressort du havre-sac"); ExitHavenBag(startMap, ct); return false; }
        if (!ConfirmZaapTeleport(z.zaapMap, () => _launch.Teleport(_pid, z.zaapMap, 3), ct, out var reason))
        {
            Warn("trajet", $"téléport vers {z.zaapMap} non confirmé"
                + (reason is null ? "" : " : " + reason) + " -> ressort avant la marche");
            if ((_launch.CurrentMap(_pid) ?? 0) == hbMap) ToggleOutOfHavenBag(hbMap, ct);
            return false;
        }
        if (z.viaCoord) _coordFallbackVisited.Add(z.zaapMap);                                            // anti-oscillation
        InterruptibleDelay(MapSettle(), ct);                                                              // 8. stabilisation
        WaitSelfReady(ct);
        Ok("trajet", $"téléporté au zaap map {z.zaapMap} ({z.hops} carte(s) restantes){ZaapCostSuffix(z.zaapMap)}");
        return true;
    }

    // Ressort du havre-sac. L'entrée (HavenBagEnterRequest/ize) EST un TOGGLE : la renvoyer fait sortir — c'est ce que
    // fait le vrai client (vérifié en capture). MAIS le serveur REFUSE la sortie tant qu'un DIALOGUE est ouvert : ici on
    // vient d'ouvrir le ZAAP du havre-sac -> le toggle renvoyait juste un TextInformationEvent « action impossible » et le
    // perso restait coincé, puis le voyage exécutait un chemin PÉRIMÉ depuis la carte d'entrée => boucle « envoi échoué ».
    // Fix : FERMER le dialogue (DialogLeaveRequest) AVANT de toggler. Best-effort avec une re-tentative. Retour sur `backTo`.
    private void ExitHavenBag(long backTo, CancellationToken ct)
    {
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                _launch.LeaveDialog(_pid);                    // ferme un éventuel zaap/dialogue ouvert (sinon sortie refusée)
                InterruptibleDelay(_launch?.IsTurboAckAll(_pid) == true ? 40 : 350, ct);
                if (!_launch.EnterHavenBag(_pid)) return;     // toggle de sortie
                if (WaitUntil(() => (_launch.CurrentMap(_pid) ?? 0) == backTo, MapChangeAttemptTimeoutMs, ct))
                {
                    InterruptibleDelay(MapSettle(), ct);
                    WaitSelfReady(ct);
                    return;
                }
            }
        }
        catch (Exception ex) when (ct.IsCancellationRequested || Root(ex) is ScriptStopException or HookExpiredException) { throw; }
        catch (Exception ex) { Warn("trajet", Loc.Fmt("L.Lua.TravelCustomError", ex.Message)); }
    }

    // Ouvre un ZAAP PHYSIQUEMENT sur la carte courante (interactif type 16) : marche jusqu'à sa case d'activation + iwo, puis
    // attend la liste des destinations (hjj -> KnownZaapMaps). true si le zaap est ouvert et la liste reçue. NE téléporte PAS.
    private bool OpenGroundZaap(long onMap, CancellationToken ct)
    {
        var st = _world.Get(_pid);
        var zi = st.Interactives.FirstOrDefault(i => i.ElementTypeId == 16 && i.HarvestSkill is not null);
        if (zi is null || st.SelfCell is not int src) return false;
        int? stand = _launch.HarvestStandCell(_pid, onMap, src, zi, st.IsWalkable, st.StepAllowed, out var standWalk);
        if (stand is null) return false;
        if (stand.Value != src)
        {
            var mv = _launch.SendAndWaitAsync(_pid, () => _launch.MoveTo(_pid, onMap, src, stand.Value, standWalk, true, st.StepAllowed), MoveTimeoutMs, ct).GetAwaiter().GetResult();
            if (mv != LaunchService.MoveOutcome.Arrived) return false;
        }
        long seq0 = _launch.ZaapListSeq(_pid);                                               // AVANT l'iwo
        if (!_launch.SendHarvest(_pid, zi)) return false;                                   // ouvre le zaap (iwo)
        return WaitUntil(() => _launch.ZaapListSeq(_pid) > seq0, ZaapDestTimeoutMs, ct);    // une NOUVELLE liste (hjj) arrive = zaap réellement ouvert
    }

    // #1 ZAAP SUR LA CARTE COURANTE: si un zaap est physiquement présent ici, on l'ouvre et on
    // téléporte DIRECTEMENT vers le zaap le plus proche de la cible — SANS le détour havre-sac (plus rapide, plus naturel,
    // moins détectable). false = pas de zaap sol utile ici -> l'appelant retombe sur le havre-sac. Ferme le zaap si on renonce.
    // noUsefulZaap = true UNIQUEMENT si le zaap du sol a été ouvert, la liste FRAÎCHE reçue, et qu'AUCUN zaap connu n'aide :
    // le havre-sac (même liste) serait alors un aller-retour inutile -> l'appelant le saute. Un ÉCHEC de téléport (refus,
    // lag, non confirmé) laisse ce drapeau à false. Un refus serveur explicite interdit de renvoyer la même destination.
    private bool TravelViaGroundZaap(long targetMap, long startMap, int originalWalk, CancellationToken ct, bool probe, out bool noUsefulZaap)
    {
        noUsefulZaap = false;
        ThrowIfStopping(ct);
        if (_launch.IsInFight(_pid)) return false;
        if (!_world.Get(_pid).Interactives.Any(i => i.ElementTypeId == 16 && i.HarvestSkill is not null)) return false;  // pas de zaap ici
        Info("trajet", $"zaap sur la carte -> ouverture directe (cible {targetMap})");
        if (!OpenGroundZaap(startMap, ct)) { Warn("trajet", "zaap du sol : ouverture/liste KO -> repli"); _launch.LeaveDialog(_pid); return false; }
        // On ne téléporte QUE si un zaap rend le trajet plus court (ou cible injoignable à pied) ; sinon on ferme et on marche.
        bool unreachable = originalWalk == 0;
        if (NearestZaapFor(targetMap, allowGated: unreachable, coordFallbackFrom: probe && unreachable ? startMap : 0) is not { } z
            || z.zaapMap == startMap || !(unreachable || z.hops + ZaapWalkMargin < originalWalk))
            { noUsefulZaap = true; Info("trajet", "aucun zaap plus proche que la marche -> ferme le zaap"); _launch.LeaveDialog(_pid); return false; }
        if (!ConfirmZaapTeleport(z.zaapMap, () => _launch.Teleport(_pid, z.zaapMap, 0), ct, out var reason))
        {
            noUsefulZaap = reason is not null;
            Warn("trajet", $"téléport (zaap sol) vers {z.zaapMap} non confirmé"
                + (reason is null ? "" : " : " + reason) + " -> marche");
            _launch.LeaveDialog(_pid);
            if (InHavenBag(startMap)) ToggleOutOfHavenBag(startMap, ct);
            return false;
        }
        if (z.viaCoord) _coordFallbackVisited.Add(z.zaapMap);                                // anti-oscillation
        InterruptibleDelay(MapSettle(), ct);
        WaitSelfReady(ct);
        Ok("trajet", $"téléporté (zaap sol) au zaap map {z.zaapMap} ({z.hops} carte(s) restantes){ZaapCostSuffix(z.zaapMap)}");
        return true;
    }

    // #4 Verbe `zaap:goto(mapId)` : ouvre le zaap PRÉSENT sur la carte courante et téléporte DIRECTEMENT vers `mapId` (doit
    // être une destination de CE zaap). Contrairement à `zaap:travel` (exige un zaap déjà ouvert), celui-ci marche + ouvre.
    private bool ZaapGoto(long targetMap, CancellationToken ct)
        => ReportTravel(() => ZaapGotoCore(targetMap, ct));

    private string ZaapCostSuffix(long map)
        => _launch.AssessWorldMapZaap(_pid, map).CostKamas is long cost ? $" ({cost} kamas)" : "";

    private bool ZaapGotoCore(long targetMap, CancellationToken ct)
    {
        if (targetMap <= 0) return false;
        long cur = _launch.CurrentMap(_pid) ?? 0;
        if (cur == targetMap) return true;
        if (!WaitForTeamDeparture(ct)) return false;
        cur = _launch.CurrentMap(_pid) ?? 0;
        if (!OpenGroundZaap(cur, ct)) { Warn("trajet", "zaap:goto : aucun zaap ouvrable sur la carte courante"); _launch.LeaveDialog(_pid); return false; }
        if (!_launch.CanTeleportTo(_pid, targetMap)) { Warn("trajet", $"zaap:goto : {targetMap} n'est pas une destination de ce zaap"); _launch.LeaveDialog(_pid); return false; }
        // Zaap du HAVRE-SAC (liste de type 3, ou carte havre-sac apprise par l'étape `havenbag`) -> source HAVEN_BAG=3, comme
        // TravelViaZaap. Zaap au sol -> 0 (octets inchangés).
        int srcType = _launch.OpenTeleportType(_pid) == 3 || InHavenBag(cur) ? 3 : 0;
        if (!ConfirmZaapTeleport(targetMap, () => _launch.Teleport(_pid, targetMap, srcType), ct, out var reason))
        {
            Warn("trajet", $"zaap:goto : {targetMap} non confirmé" + (reason is null ? "" : " : " + reason));
            _launch.LeaveDialog(_pid);
            return false;
        }
        InterruptibleDelay(MapSettle(), ct);
        WaitSelfReady(ct);
        Ok("trajet", WorldMapZaapTravel.ArrivedReason(targetMap, _launch.AssessWorldMapZaap(_pid, targetMap).CostKamas));
        return true;
    }

    private void ExplainZaapi(long target, string reason)
    {
        if (_lastZaapiRefusal == (target, reason)) return;
        _lastZaapiRefusal = (target, reason);
        Info("trajet", reason);
    }

    private static bool UsableZaapi(MapInteractive i)
        => ZaapiTravel.IsUsable(i.ElementTypeId, i.Skills.Select(s => (s.SkillId, s.Enabled)));

    // Zaapi posé sur la carte courante, y compris sans cellule connue (le client l'ouvre quand même).
    private MapInteractive? ZaapiOnCurrentMap()
    {
        var placed = _world.Get(_pid).Interactives.FirstOrDefault(UsableZaapi);
        if (placed is not null) return placed;
        long map = _launch.CurrentMap(_pid) ?? 0;
        return _launch.PlacelessInteractives(_pid, map).FirstOrDefault(UsableZaapi);
    }

    // Marche jusqu'au zaapi s'il a une cellule. Sans cellule : ouverture directe, comme le client.
    private bool ApproachZaapi(MapInteractive zi, CancellationToken ct)
    {
        if (zi.CellId < 0) return true;
        long cur = _launch.CurrentMap(_pid) ?? 0;
        var st = _world.Get(_pid);
        if (st.SelfCell is not int src) return false;
        int? stand = _launch.HarvestStandCell(_pid, cur, src, zi, st.IsWalkable, st.StepAllowed, out var standWalk);
        if (stand is null) return false;
        if (stand.Value == src) return true;
        return _launch.SendAndWaitAsync(_pid, () => _launch.MoveTo(_pid, cur, src, stand.Value, standWalk, true, st.StepAllowed), MoveTimeoutMs, ct).GetAwaiter().GetResult() == LaunchService.MoveOutcome.Arrived;
    }

    // InteractiveUseRequest (skill du zaapi + élément), puis attente du TeleportDestinationsEvent type 1.
    private bool OpenZaapiDialog(MapInteractive zi, CancellationToken ct, out string failure)
    {
        failure = "";
        if (_launch.TeleportView(_pid) is not null && _launch.OpenTeleportType(_pid) == ZaapiTravel.ListType) return true;   // dialogue déjà ouvert : sa liste est valable
        if (!ApproachZaapi(zi, ct)) { failure = ZaapiTravel.NotHereReason; return false; }
        long seq0 = _launch.TeleportDialogSeq(_pid);
        if (!_launch.SendInteractiveSkill(_pid, zi, ZaapiTravel.SkillId)) { failure = ZaapiTravel.OpenFailedReason; return false; }
        if (WaitUntil(() => _launch.TeleportDialogSeq(_pid) > seq0 && _launch.OpenTeleportType(_pid) == ZaapiTravel.ListType, ZaapDestTimeoutMs, ct))
            return true;
        _launch.LeaveDialog(_pid);
        failure = ZaapiTravel.ListMissingReason;
        return false;
    }

    // TeleportRequest map_id + source_type 1 + destination_type 1. Succès seulement si la carte observée devient destMap.
    private bool FinishZaapi(long destMap, CancellationToken ct)
    {
        bool arrived = false;
        try
        {
            var offer = _launch.AssessZaapi(_pid, destMap);
            if (!offer.CanSend) { Warn("trajet", offer.Reason); return false; }
            Info("trajet", offer.Reason);
            bool sent = false;
            arrived = ConfirmZaapTeleport(destMap, () => sent = _launch.TrySendZaapi(_pid, destMap), ct, out var refusal, 8000);
            if (!arrived) { Warn("trajet", refusal ?? (sent ? ZaapiTravel.UnconfirmedReason(destMap) : ZaapiTravel.SendFailedReason)); return false; }
            InterruptibleDelay(MapSettle(), ct);
            WaitSelfReady(ct);
            Ok("trajet", ZaapiTravel.ArrivedReason(destMap, offer.CostKamas ?? 0));
            return true;
        }
        finally
        {
            if (!arrived) _launch.LeaveDialog(_pid);
        }
    }

    // Raccourci : seulement si un zaapi est DÉJÀ sur la carte. Ouvre, choisit une destination qui raccourcit, téléporte.
    private bool TravelViaZaapi(long targetMap, int originalWalk, bool unreachable, int margin, bool explain, CancellationToken ct)
    {
        ThrowIfStopping(ct);
        if (_launch.IsInFight(_pid)) { if (explain) Warn("trajet", ZaapiTravel.CombatReason); return false; }
        CloseHdvBeforeMove(ct);
        var zi = ZaapiOnCurrentMap();
        if (zi is null && explain)
            WaitUntil(() => (zi = ZaapiOnCurrentMap()) is not null, 2000, ct);
        if (zi is null)
        {
            if (explain) ExplainZaapi(targetMap, ZaapiTravel.NotHereReason);
            return false;
        }
        if (!OpenZaapiDialog(zi, ct, out var openFailure))
        {
            Warn("trajet", openFailure);
            return false;
        }
        long here = _launch.CurrentMap(_pid) ?? 0;
        var options = new List<(long MapId, long? CostKamas, int HopsAfter)>();
        foreach (var quote in _launch.ZaapiQuotes(_pid))
        {
            if (quote.MapId == here || quote.MapId <= 0 || quote.MapId > int.MaxValue) continue;
            int hops;
            if (quote.MapId == targetMap) hops = 0;
            else if (!_launch.WorldGraphKnows((int)quote.MapId)) continue;
            else
            {
                hops = _launch.WorldPath((int)quote.MapId, (int)targetMap, null, unreachable ? _pid : 0u).Count;
                if (hops == 0) continue;
            }
            options.Add((quote.MapId, quote.CostKamas, hops));
        }
        var pick = ZaapiTravel.Choose(options, _launch.Kamas(_pid), originalWalk, unreachable, margin);
        if (pick is not { CanSend: true } chosen)
        {
            ExplainZaapi(targetMap, pick is { } refused ? refused.Reason : ZaapiTravel.NoShortcutReason);
            _launch.LeaveDialog(_pid);
            return false;
        }
        return FinishZaapi(chosen.MapId, ct);
    }

    // Exécute un saut Zaapi intra-ville calculé par le planificateur de trajet.
    private bool ExecuteCityZaapiHop(ZaapiHop hop, CancellationToken ct, HashSet<int>? avoid, HashSet<long>? avoidEdges)
    {
        ThrowIfStopping(ct);
        if (_launch.IsInFight(_pid)) { Warn("trajet", ZaapiTravel.CombatReason); return false; }
        long cur = _launch.CurrentMap(_pid) ?? 0;
        if (cur != hop.SourceZaapiMap)
        {
            Info("trajet", $"Zaapi: marche vers le zaapi hôte {hop.SourceZaapiMap} ({hop.HopsToSource} carte(s))…");
            if (!GoToMapId(hop.SourceZaapiMap, ct, avoid, 1, avoidEdges))
            {
                Warn("trajet", "Zaapi: impossible de rejoindre le zaapi hôte");
                return false;
            }
            cur = _launch.CurrentMap(_pid) ?? 0;
            if (cur != hop.SourceZaapiMap) return false;
        }

        CloseHdvBeforeMove(ct);
        WaitSelfReady(ct);
        var zi = ZaapiOnCurrentMap();
        if (zi is null)
            WaitUntil(() => (zi = ZaapiOnCurrentMap()) is not null, 2000, ct);
        if (zi is null)
        {
            Warn("trajet", ZaapiTravel.NotHereReason);
            return false;
        }
        if (!OpenZaapiDialog(zi, ct, out var openFailure))
        {
            Warn("trajet", openFailure);
            return false;
        }
        return FinishZaapi(hop.DestZaapiMap, ct);
    }

    // zaapi:go(mapId) : zaapi de la carte courante, puis la destination demandée. Succès = carte observée.
    private bool ZaapiGoto(long targetMap, CancellationToken ct)
        => ReportTravel(() => ZaapiGotoCore(targetMap, ct));

    private bool ZaapiGotoCore(long targetMap, CancellationToken ct)
    {
        if (targetMap <= 0) return false;
        if (ZaapiTravel.Arrived(_launch.CurrentMap(_pid) ?? 0, targetMap)) return true;
        ThrowIfStopping(ct);
        if (_launch.IsInFight(_pid)) { Warn("trajet", ZaapiTravel.CombatReason); return false; }
        if (!WaitForTeamDeparture(ct)) return false;
        CloseHdvBeforeMove(ct);
        WaitSelfReady(ct);
        var zi = ZaapiOnCurrentMap();
        if (zi is null)
            WaitUntil(() => (zi = ZaapiOnCurrentMap()) is not null, 2000, ct);
        if (zi is null)
        {
            long cur = _launch.CurrentMap(_pid) ?? 0;
            var cityZaapis = _launch.ZaapiRegistry.GetZaapisForCityOf(cur, targetMap, m => _launch.SubAreaOf(m));
            if (_launch.NearestZaapiHost(cur, cityZaapis) is { } nearest && nearest.Hops > 0 && nearest.MapId != cur)
            {
                Info("trajet", $"zaapi: marche vers le zaapi hôte {nearest.MapId} ({nearest.Hops} carte(s))…");
                if (GoToMapId(nearest.MapId, ct))
                {
                    WaitSelfReady(ct);
                    zi = ZaapiOnCurrentMap();
                }
            }
        }
        if (zi is null) { Warn("trajet", ZaapiTravel.NotHereReason); return false; }
        if (!OpenZaapiDialog(zi, ct, out var openFailure)) { Warn("trajet", openFailure); return false; }
        return FinishZaapi(targetMap, ct);
    }

    // travelTo(mapId) | travelTo(x, y) | travelTo("x,y") | travelTo("<id>") : la coord est résolue sur le calque COURANT
    // (MapIdAt + CurrentWorldMap, comme getMapIdAt / map:idAt). Puis GoToMapId inchangé (pas de TravelCapped : comme avant).
    private bool TravelToArgs(CallbackArguments a, CancellationToken ct)
    {
        _lastTravelFailure = null;
        return ReportTravel(() => TravelToArgsCore(a, ct));
    }

    private bool TravelToArgsCore(CallbackArguments a, CancellationToken ct)
    {
        int o = a.Count > 0 && a[0].Type == DataType.Table ? 1 : 0;
        DynValue v0 = o < a.Count ? a[o] : DynValue.Nil, v1 = o + 1 < a.Count ? a[o + 1] : DynValue.Nil;
        if (v0.Type == DataType.Number && v1.Type == DataType.Number) return TravelToCoord((int)v0.Number, (int)v1.Number, ct);
        if (v0.Type == DataType.Number) return GoToMapId((long)v0.Number, ct);
        string sv = (v0.CastToString() ?? "").Trim();
        if (long.TryParse(sv, out long id) && id > 0) return GoToMapId(id, ct);
        if (TryParseCoord(sv, out int x, out int y)) return TravelToCoord(x, y, ct);
        Warn("trajet", Loc.Fmt("L.Lua.TravelBadTarget", sv));
        return false;
    }

    private bool TravelToCoord(int x, int y, CancellationToken ct)
    {
        if (_launch.MapIdAt(x, y, _launch.CurrentWorldMap(_pid)) is not long id || id == 0) { Warn("trajet", Loc.Fmt("L.Lua.TravelCoordUnknown", x, y)); return false; }
        return GoToMapId(id, ct);
    }

    // Mots-clés de téléport d'une étape / de changeMap: Un bot tiers Readpath.vb (zaap( / zaapi( / havenbag) + un bot tiers
    // ScriptManager.cs (zaap(id) = y aller via zaap). Repli : voyage multi-cartes (TravelCapped) vers la carte visée.
    private bool RunTeleportKeyword(TeleportKeyword kw, long mapId, CancellationToken ct)
    {
        switch (kw)
        {
            case TeleportKeyword.Zaap:
                Info("trajet", Loc.Fmt("L.Lua.TravelZaapKeyword", mapId));
                // Un bot tiers: zaap PHYSIQUEMENT sur la carte -> téléport direct. Sinon / id pas une destination -> sémantique
                // Un bot tiers: voyage vers cette carte (GoToMapId prend déjà un zaap si utile).
                long zcur = _launch.CurrentMap(_pid) ?? 0;
                if (_world.Get(_pid).Interactives.Any(i => i.ElementTypeId == 16 && i.HarvestSkill is not null) && ZaapGoto(mapId, ct)) { _havenBagZaapFails = 0; return true; }
                if (InHavenBag(zcur))
                {
                    // Depuis le HAVRE-SAC (hors world-graph) : aucun voyage ne peut en partir -> on RESSORT (au lieu de
                    // TravelCapped voué à l'échec puis StuckStop), borné : après N échecs consécutifs on arrête proprement.
                    Warn("havre-sac", Loc.Fmt("L.Lua.HavenBagZaapFail", mapId));
                    bool stuck = !LinkSuspended && ++_havenBagZaapFails >= MaxHavenBagZaapFails;   // lien suspendu : non compté
                    ToggleOutOfHavenBag(zcur, ct);
                    if (stuck) { Err("havre-sac", Loc.Fmt("L.Lua.HavenBagZaapStuck", _havenBagZaapFails)); _havenBagZaapFails = 0; StuckStop(); return false; }
                    InterruptibleDelay(1500, ct);
                    return false;
                }
                Info("trajet", Loc.Fmt("L.Lua.TravelKeywordFallback", "zaap", mapId));
                if (!TravelCapped(mapId, ct)) InterruptibleDelay(1500, ct);
                return (_launch.CurrentMap(_pid) ?? 0) == mapId;
            case TeleportKeyword.Zaapi:
                Info("trajet", Loc.Fmt("L.Lua.TravelZaapiKeyword", mapId));
                if (ZaapiGoto(mapId, ct)) return true;
                Info("trajet", Loc.Fmt("L.Lua.TravelKeywordFallback", "zaapi", mapId));
                if (!TravelCapped(mapId, ct)) InterruptibleDelay(1500, ct);
                return (_launch.CurrentMap(_pid) ?? 0) == mapId;
            case TeleportKeyword.HavenBag:
                return EnterHavenBagStep(ct);
            case TeleportKeyword.WorldMap:
                Info("trajet", $"zaapcarte({mapId}) : {WorldMapZaapTravel.Label}.");
                return TravelFromWorldMap(mapId, ct);
        }
        return false;
    }

    // havenbag : ferme tout dialogue (sinon refus serveur), toggle ize, attend le changement de carte. ⚠ HavenBagEnterRequest
    // est un TOGGLE (fiche havenbag-exit-toggle) : appelé DEPUIS le havre-sac, il en SORT. L'id de la carte havre-sac est
    // APPRIS à l'entrée : RunRoute y exécute alors l'étape map='havenbag' (ou son id), sinon ressort (cf. HavenBagStepOf).
    private bool EnterHavenBagStep(CancellationToken ct)
    {
        ThrowIfStopping(ct);
        if (_launch.IsInFight(_pid)) return false;
        long start = _launch.CurrentMap(_pid) ?? 0;
        bool exiting = InHavenBag(start);
        Info("trajet", Loc.T(exiting ? "L.Lua.TravelHavenBagExit" : "L.Lua.TravelHavenBagKeyword"));
        _launch.LeaveDialog(_pid); InterruptibleDelay(_launch?.IsTurboAckAll(_pid) == true ? 40 : 300, ct);
        if (!_launch.EnterHavenBag(_pid)) { Warn("havre-sac", Loc.T("L.Lua.HavenBagFail")); InterruptibleDelay(1500, ct); return false; }
        if (!WaitUntil(() => (_launch.CurrentMap(_pid) ?? 0) is var m && m != 0 && m != start, MapChangeAttemptTimeoutMs, ct))
            { Warn("havre-sac", Loc.T("L.Lua.TravelHavenBagNoChange")); InterruptibleDelay(1500, ct); return false; }
        if (!exiting) { _havenBagMap = _launch.CurrentMap(_pid) ?? 0; _havenBagEntryMap = start; }
        InterruptibleDelay(MapSettle(), ct); WaitSelfReady(ct);
        Ok("havre-sac", Loc.T(exiting ? "L.Lua.HavenBagExited" : "L.Lua.HavenBagEntered"));
        return true;
    }

    private long _havenBagMap;        // carte havre-sac APPRISE par l'étape/le verbe `havenbag` (0 = inconnue)
    private long _havenBagEntryMap;   // carte extérieure d'où l'on y est entré (diagnostic)
    private int _havenBagZaapFails;   // échecs consécutifs de zaap(id) depuis le havre-sac
    private const int MaxHavenBagZaapFails = 3;

    private bool InHavenBag(long mapId) => _havenBagMap != 0 && mapId == _havenBagMap;

    // Étape d'un trajet pour la carte havre-sac : map='havenbag' (insensible à la casse) ou l'id numérique de cette carte.
    private static Table? HavenBagStepOf(Table route, long hbMap)
    {
        foreach (var v in route.Values)
        {
            if (v.Type != DataType.Table) continue;
            string m = (v.Table.Get("map").CastToString() ?? "").Trim();
            if (m.Equals("havenbag", StringComparison.OrdinalIgnoreCase) || (long.TryParse(m, out long id) && id == hbMap)) return v.Table;
        }
        return null;
    }

    // Ressort du havre-sac SANS supposer la carte de retour (contrairement à ExitHavenBag, qui attend `backTo` et
    // re-toggle -> re-rentrerait si la carte de retour diffère) : on attend seulement de QUITTER `hbMap`. Re-tente UNE fois
    // uniquement si l'on y est toujours (toggle refusé, dialogue ouvert).
    private bool ToggleOutOfHavenBag(long hbMap, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if ((_launch.CurrentMap(_pid) ?? 0) != hbMap) return true;
            _launch.LeaveDialog(_pid);
            InterruptibleDelay(_launch?.IsTurboAckAll(_pid) == true ? 40 : 350, ct);
            if (!_launch.EnterHavenBag(_pid)) return false;
            if (WaitUntil(() => (_launch.CurrentMap(_pid) ?? 0) is var m && m != 0 && m != hbMap, MapChangeAttemptTimeoutMs, ct))
            {
                InterruptibleDelay(MapSettle(), ct);
                WaitSelfReady(ct);
                return true;
            }
        }
        return false;
    }

    private int _offGraphExitTries;   // sondes de sortie de havre-sac depuis une carte hors-graphe non apprise (bornées)
    private const int MaxOffGraphExitTries = 2;

    // Carte HORS world-graph qu'on n'a PAS apprise comme havre-sac (ex : script lancé alors qu'on est DÉJÀ dans son havre-sac,
    // `_havenBagMap` jamais posé). On SONDE une sortie de havre-sac (HavenBagEnterRequest = toggle), AVEC retour arrière : si on
    // atterrit sur une carte du graphe -> c'était bien un havre-sac (on continue) ; si on tombe sur une AUTRE carte hors-graphe
    // (on aurait ouvert le havre-sac depuis une zone non extraite) -> on RE-toggle pour revenir, et on laisse l'escalade agir.
    // Jamais tenté en combat / juste après un combat / dans une vraie maison (in_house_information) / en lien suspendu.
    private bool TryHavenBagExitProbe(long mapId, CancellationToken ct)
    {
        if (LinkSuspended || _launch.IsInFight(_pid) || _launch.InHouse(_pid)) return false;
        if (_launch.MsSinceFightEnd(_pid) < PostFightGraceMs) return false;
        if (_offGraphExitTries >= MaxOffGraphExitTries) return false;
        // Un havre-sac est TOUJOURS en (0,0) ; une vraie zone non extraite garde ses coordonnées réelles. On ne sonde donc que
        // sur une carte en (0,0) ou de coordonnée inconnue -> jamais de toggle depuis une zone légitime (l'instance de combat
        // en (0,0) est déjà écartée par la grâce post-combat ci-dessus).
        var w = _world.Get(_pid);
        if (w.MapX is int mx && w.MapY is int my && (mx != 0 || my != 0)) return false;
        _offGraphExitTries++;
        Info("havre-sac", Loc.Fmt("L.Lua.HavenBagExitProbe", mapId));
        _launch.LeaveDialog(_pid);
        InterruptibleDelay(_launch?.IsTurboAckAll(_pid) == true ? 40 : 350, ct);
        if (!_launch.EnterHavenBag(_pid)) return false;
        if (!WaitUntil(() => (_launch.CurrentMap(_pid) ?? 0) is var m && m != 0 && m != mapId, MapChangeAttemptTimeoutMs, ct))
            return false;   // pas de changement -> pas un havre-sac (ou sortie impossible) : on laisse l'escalade
        long now = _launch.CurrentMap(_pid) ?? 0;
        InterruptibleDelay(MapSettle(), ct);
        if (_launch.WorldGraphKnows((int)now)) { WaitSelfReady(ct); _offGraphExitTries = 0; Ok("havre-sac", Loc.T("L.Lua.HavenBagExited")); return true; }
        // Atterri sur une AUTRE carte hors-graphe : on a probablement OUVERT un havre-sac depuis une zone non extraite -> on revient.
        if (_launch.EnterHavenBag(_pid)) WaitUntil(() => (_launch.CurrentMap(_pid) ?? 0) is var m && m != 0 && m != now, MapChangeAttemptTimeoutMs, ct);
        return false;
    }

    private const int MaxTravelReroutes = 6;   // nb max de re-calculs de chemin quand un saut est bloqué (évite la boucle)
    private readonly Dictionary<long, int> _edgeFails = new();   // #2 : nb d'échecs par ARÊTE ce voyage -> 1er = transitoire (retry), 2e = vrai blocage (ban)
    private readonly HashSet<int> _noHavenBagWorld = new();   // calques (worldMap) où le havre-sac s'est révélé INDISPONIBLE (Incarnam/instance) -> ne plus le tenter
    private const int IncarnamWorldMap = 2;                    // calque d'Incarnam (extraction client ; scripts « givre-worlds: [2,…] ») : jamais de havre-sac ni de zaap
    private readonly Dictionary<int, long> _havenBagCooldownUntil = new();   // calque (1 / -1) -> pas de nouvel essai havre-sac avant cette échéance
    private readonly Dictionary<int, int> _havenBagFailStreak = new();       // calque -> échecs d'entrée CONSÉCUTIFS (apprentissage à partir de 2)
    private const int HavenBagRetryCooldownMs = 180_000;        // 3 min : un refus persistant ne coûte plus 16 s par voyage
    private long _lastZaapDecisionLog;                          // anti-spam du diagnostic de décision zaap (une ligne par cible)

    private const int WalkByCoordsMaxHops = 80;

    private bool WalkByCoordinates(long targetMap, CancellationToken ct)
    {
        // Le serveur a refusé la téléportation : errer sur la carte du monde ne rapprocherait pas de cette destination.
        if (!string.IsNullOrWhiteSpace(_travelServerRefusal)) return false;
        long cur = _launch.CurrentMap(_pid) ?? 0;
        if (cur == 0 || _launch.WorldMapOf(cur) is not int wm || _launch.WorldMapOf(targetMap) is not int wt || wm != wt) return false;
        if (_launch.MapCoordOf(targetMap) is not (int tx, int ty)) return false;
        var seen = new HashSet<long> { cur };
        bool announced = false;
        for (int hop = 0; hop < WalkByCoordsMaxHops; hop++)
        {
            ThrowIfStopping(ct);
            if (_launch.IsInFight(_pid)) return false;
            cur = _launch.CurrentMap(_pid) ?? 0;
            if (cur == targetMap) return true;
            if (cur == 0 || _launch.MapCoordOf(cur) is not (int cx, int cy)) return false;
            if (cx == tx && cy == ty) return true;   // même coordonnée, autre id (carte jumelle) : arrivé
            if (!announced) { Info("trajet", $"marche par coordonnées (graphe incomplet) : [{cx},{cy}] → [{tx},{ty}]"); announced = true; }
            bool moved = false;
            foreach (var dir in Services.WorldMap.CoordWalk.Directions(cx, cy, tx, ty))
            {
                if (!ChangeMap(dir, ct)) continue;
                long now = _launch.CurrentMap(_pid) ?? 0;
                if (now == 0 || now == cur) continue;
                moved = true;
                if (!seen.Add(now)) return false;   // boucle : on abandonne
                break;
            }
            if (!moved) return false;
        }
        return false;
    }

    // probe = sondage zaap quand la marche est impossible : réservé aux trajets PLAFONNÉS (TravelCapped) — cf tryZaap.
    private bool GoToMapId(long targetMap, CancellationToken ct, HashSet<int>? avoid = null, int reroutes = 0, HashSet<long>? avoidEdges = null, bool probe = false)
        => ReportTravel(() => GoToMapIdCore(targetMap, ct, avoid, reroutes, avoidEdges, probe));

    private bool GoToMapIdCore(long targetMap, CancellationToken ct, HashSet<int>? avoid, int reroutes, HashSet<long>? avoidEdges, bool probe)
    {
        ThrowIfStopping(ct);
        if (reroutes == 0) { CloseHdvBeforeMove(ct); _edgeFails.Clear(); }   // ferme un HDV ouvert + réarme les compteurs d'arête (voyage neuf)
        long cur = _launch.CurrentMap(_pid) ?? 0;
        if (cur == 0) return false;
        // Juste après un combat la carte courante est encore celle du combat (pas un nœud du graphe) : le trajet partait en « marche impossible » puis en détour havre-sac
        // (~40 s perdus par combat dans une expédition, relevé sur les logs du 21/10). On attend (borné) le retour sur la vraie carte avant de planifier.
        if (reroutes == 0 && _launch.MsSinceFightEnd(_pid) < 5000 && !_launch.WorldGraphKnows((int)cur))
        {
            WaitUntil(() => _launch.WorldGraphKnows((int)(_launch.CurrentMap(_pid) ?? 0)), 3000, ct);
            cur = _launch.CurrentMap(_pid) ?? cur;
        }
        // Id de carte PÉRIMÉ (script écrit pour un ancien client) : si la cible n'est plus un nœud du world-graph VIVANT
        // mais existe à une coordonnée dont un AUTRE id est le nœud vivant, on vise ce nœud (ex 171443204 -15,39 -> 178257922).
        targetMap = _launch.GraphMapForId(targetMap);
        if (cur == targetMap) return true;
        if (!WaitForTeamDeparture(ct)) return false;
        cur = _launch.CurrentMap(_pid) ?? 0;
        avoid ??= new HashSet<int>();       // cartes dont l'entrée a échoué ce voyage -> exclues du BFS
        avoidEdges ??= new HashSet<long>();  // ARÊTES précises infranchissables (bord bloqué par l'élévation) -> on route autour

        var path = PlanPath(cur, targetMap, avoid, avoidEdges, ct);

        // #3 COURT-CIRCUIT CIBLE ADJACENTE : le world-graph VIVANT est INCOMPLET (le code le note) -> une cible à UNE seule
        // carte (coord adjacente, MÊME calque) peut ressortir « injoignable à pied » (path vide) et déclencher un zaap
        // transcontinental (le vrai symptôme « saut extrême alors qu'on est à 1 carte »). Avant tout raccourci zaap, si la
        // cible est coord-adjacente, on tente UN changement de carte DIRECTIONNEL (marche jusqu'au bord + jqk, primitive
        // éprouvée du pas-à-pas). Échec propre (pas de sortie ce côté) -> on retombe sur la logique zaap/normale existante.
        // ⚠ Les DEUX calques doivent être CONNUS et égaux : `WorldMapOf` rend null pour TOUT sur un cache carte legacy/
        // cold-start (Get() a quand même les coords) -> `null == null` ferait passer une cible d'un AUTRE calque (Incarnam vs
        // Astrub à coord ±1) et lancerait un ChangeMap dans le mauvais sens. On exige donc int==int (calque inconnu -> #3 sauté).
        if (path.Count == 0 && reroutes == 0
            && _launch.MapCoordOf(cur) is (int cxg, int cyg) && _launch.MapCoordOf(targetMap) is (int txg, int tyg)
            && _launch.WorldMapOf(cur) is int wmC && _launch.WorldMapOf(targetMap) is int wmT && wmC == wmT
            && System.Math.Abs(txg - cxg) + System.Math.Abs(tyg - cyg) == 1)
        {
            string dir = txg > cxg ? "right" : txg < cxg ? "left" : tyg > cyg ? "bottom" : "top";
            Info("trajet", Loc.Fmt("L.Lua.TravelAdjacentDirect", targetMap, dir));
            ChangeMap(dir, ct);
            long after = _launch.CurrentMap(_pid) ?? 0;
            if (after == targetMap) return true;
            if (after != cur) { cur = after; path = PlanPath(cur, targetMap, avoid, avoidEdges, ct); }
        }

        // RACCOURCI ZAAP. (a) zaap depuis la carte si le coût annoncé est payable ; (b) sinon zaap au sol, puis havre-sac.
        // La liste de connexion suffit au menu, pas au raccourci automatique : sans coût, on ne dépense pas tout seul.
        // TravelViaZaap fait le choix final une fois la liste connue et ressort du havre-sac si aucun zaap n'aide.
        // Repli total : si le raccourci échoue/renonce, on marche (path direct si dispo).
        bool listKnown = _launch.KnownZaapMaps(_pid).Count > 0;
        // GARDE CALQUE : zaaps ET havre-sac n'existent QUE sur le Monde des Douze principal (worldMap 1). À Incarnam /
        // dans une instance / un donjon (worldMap != 1), tenter le raccourci = « zaap du havre-sac introuvable » en boucle
        // puis abandon (le bug signalé : les maps 2,-1..1,-5 y sont pourtant adjacentes). On NE supprime le raccourci que
        // si le calque courant est CONNU et non-1 (worldMap inconnu = cache legacy/cold start -> on laisse la logique zaap
        // décider, aucune régression sur le monde principal).
        // Calque -1 = INTÉRIEURS (banque, HDV, maisons) — mêlés aux donjons/instances. Le raccourci « zaap plus court » y
        // était INTERDIT : retour de l'HDV (intérieur, -1) vers un circuit à 54 cartes = 54 cartes À PIED (bug signalé
        // 2026-09-23). Il y est désormais permis, avec une marge PLUS PRUDENTE (ZaapWalkMarginInterior) : un aller-retour
        // havre-sac depuis un intérieur ne vaut que pour un vrai long trajet (sortir de banque vers la route voisine = marche).
        // Calques dédiés (≥ 2 : Incarnam, dimensions) : toujours exclus.
        int? curLayer = _launch.WorldMapOf(cur);
        bool zaapLayer = curLayer is not int cw || cw == 1 || cw == -1;
        int zaapMargin = curLayer == -1 ? ZaapWalkMarginInterior : ZaapWalkMargin;
        // CIBLE INJOIGNABLE À PIED (path vide) : on tente le raccourci havre-sac QUEL QUE SOIT le calque courant. C'est
        // indispensable pour REVENIR d'un INTÉRIEUR (banque/maison, worldMap != 1) vers une zone reliée UNIQUEMENT par zaap
        // (bug : après le dépôt banque, le retour marchait en world-graph pur et échouait -> script arrêté). Le garde calque
        // (worldMap==1) ne s'applique donc QU'AU cas « zaap simplement plus court que la marche » (path NON vide) : là on
        // reste sur le Monde des Douze pour ne pas déclencher des allers-retours havre-sac inutiles depuis un intérieur.
        // Si le havre-sac est réellement indispo (Incarnam/donjon), TravelViaZaap échoue vite et on retombe sur la marche
        // (borné par le cap de TravelCapped) : plus de loop, mais plus de blocage silencieux non plus.
        bool unreachableOnFoot = path.Count == 0;
        // Cible HORS world-graph, même après la résolution par coordonnée faite en tête (GraphMapForId) : AUCUN zaap ne peut
        // l'atteindre (depuis l'arrivée du zaap il faudrait encore marcher jusqu'à elle via CE graphe) -> le détour
        // havre-sac/zaap n'y gagne rien et coûte ~19 s par tentative. Même raisonnement que le sondage (probeHere) ci-dessous,
        // étendu à la branche « liste des zaaps inconnue ». N'affecte PAS le retour d'un intérieur : là c'est la SOURCE qui
        // est hors graphe, la cible (carte de route) y est. Cas signalé : id d'étape d'une carte HOMONYME d'une autre zone.
        bool targetOffGraph = !_launch.WorldGraphKnows((int)targetMap);
        // allowGated SEULEMENT si injoignable à pied : pour « zaap plus court que la marche », on compare des chemins LIBRES
        // (sinon zaap + passage PNJ/quête non validé préféré à une marche libre qui marche — vérif adverse).
        var nearZaap = NearestZaapFor(targetMap, allowGated: unreachableOnFoot);
        // Mort (tombe/fantôme) : pas de zaap ni de havre-sac possibles -> jamais tenté (évitait des 16 s de blocage par voyage).
        bool dead = _launch.LifeStatusOf(_pid) != 0;
        // SONDAGE: RÉSERVÉ
        // aux trajets PLAFONNÉS du script (TravelCapped : route/banque/rejoin, bornés par TravelCapped + anti-errance + pause
        // planning). Les autres appelants (suivi d'équipe, Phénix, travelTo, escaliers) bouclent SANS plafond -> ancienne règle
        // (candidat connu ou liste inconnue), sinon allers-retours havre-sac à l'infini. Cible hors graphe : jamais sondée
        // (aucun zaap ne peut l'atteindre -> même échec, en plus lent).
        bool probeHere = probe && unreachableOnFoot && _launch.WorldGraphKnows((int)targetMap);
        // Réglage Déplacement du perso : « marche de préférence » -> zaap / havre-sac seulement si la cible n'est pas joignable
        // à pied (autre île) ou si la marche dépasse la distance max choisie.
        var travelPref = _launch.TravelPreference(_pid);
        bool walkFirst = TravelPolicy.WalkFirst(travelPref.PreferWalk, travelPref.WalkMaxHops, path.Count, unreachableOnFoot);
        var zaapRoute = _launch.ZaapRouteOf(_pid);
        // Pas de raccourci zaap en REROUTE (reroutes>0) : évite des allers-retours havre-sac à chaque tentative.
        // Un choix explicite (carte / sol / havre) ignore « marcher de préférence », mais pas le test « le zaap raccourcit ».
        bool routeAllowed = zaapRoute != ZaapRoute.Auto || !walkFirst;

        // RACCOURCI ZAAPI INTRA-VILLE : si l'origine et la destination sont dans la même ville
        // et que la marche directe dépasse le seuil (défaut 4 cartes), on privilégie un saut Zaapi intra-ville.
        bool cityZaapiAllowed = (zaapRoute == ZaapRoute.Auto || zaapRoute == ZaapRoute.Zaapi) && reroutes == 0 && !dead && !targetOffGraph && routeAllowed;
        ZaapiHop? cityHop = cityZaapiAllowed
            ? _launch.PlanZaapiHop(_pid, cur, targetMap, path.Count, ZaapiRegistry.DefaultCityWalkThreshold)
            : null;

        if (cityHop is { } hop)
        {
            Info("trajet", $"Zaapi ville ({hop.SourceZaapiMap} -> {hop.DestZaapiMap}) : économise {hop.SavedHops} transition(s).");
            if (ExecuteCityZaapiHop(hop, ct, avoid, avoidEdges))
            {
                long now = _launch.CurrentMap(_pid) ?? 0;
                if (now == targetMap) return true;
                if (now != cur)
                {
                    cur = now;
                    path = PlanPath(cur, targetMap, avoid, avoidEdges, ct);
                    if (path.Count == 0 && cur == targetMap) return true;
                }
            }
        }

        bool tryZaap = reroutes == 0 && !dead && !targetOffGraph && routeAllowed && (
               probeHere
            || (unreachableOnFoot && (nearZaap is not null || !listKnown))
            || (zaapLayer && ((nearZaap is { } z && path.Count > z.hops + zaapMargin)
                              || (!listKnown && path.Count > ZaapMinHops))));
        // Zaapi déjà sur la carte : son raccourci ne dépend pas de la liste des zaaps du personnage.
        // Il n'ouvre pas les autres téléports si eux n'étaient pas déjà justifiés.
        bool zaapiOnMap = (zaapRoute == ZaapRoute.Auto || zaapRoute == ZaapRoute.Zaapi) && ZaapiOnCurrentMap() is not null;
        bool zaapiCouldShorten = zaapiOnMap && (unreachableOnFoot || path.Count > zaapMargin);
        bool tryZaapiShortcut = !tryZaap && reroutes == 0 && !dead && !targetOffGraph && routeAllowed
            && (zaapRoute == ZaapRoute.Zaapi || zaapiCouldShorten);
        // Diagnostic : une LONGUE marche choisie sans zaap ne doit plus être muette (1 ligne par cible, avec la raison).
        if (!tryZaap && !tryZaapiShortcut && !unreachableOnFoot && reroutes == 0 && path.Count >= LongWalkLogHops && _lastLongWalkLog != targetMap)
        {
            _lastLongWalkLog = targetMap;
            string why = dead ? "mort"
                : walkFirst ? "marche de préférence (Paramètres > Déplacement)"
                : !zaapLayer ? $"calque {curLayer} sans zaap"
                : nearZaap is { } nz ? $"meilleur zaap à {nz.hops} carte(s) de la cible, pas assez court"
                : listKnown ? "aucun zaap connu ne rejoint la cible à pied" : "liste des zaaps inconnue";
            Info("trajet", Loc.Fmt("L.Lua.TravelLongWalk", path.Count, targetMap, why));
        }
        // Havre-sac disponible sur ce calque ? (Incarnam/instance : appris indisponible ; Monde des Douze : cooldown court après un refus.)
        // Incarnam (calque 2) n'a JAMAIS de havre-sac : exclu D'OFFICE. Avant, il n'était exclu qu'APRÈS 2 échecs consécutifs
        // (apprentissage) -> « havre-sac dispo : True » à Incarnam et 2 × ~19 s perdues par voyage (cas signalé 2026-09-24).
        // Les autres calques dédiés (dimensions…) gardent l'apprentissage : on n'y présume rien.
        bool havenBagHere = _launch.WorldMapOf(cur) is not int wmHb
                            || (wmHb != IncarnamWorldMap && !_noHavenBagWorld.Contains(wmHb)
                                && Environment.TickCount64 >= _havenBagCooldownUntil.GetValueOrDefault(wmHb));
        if (unreachableOnFoot && reroutes == 0 && _lastZaapDecisionLog != targetMap)   // diagnostic : plus JAMAIS d'échec muet (1 fois par cible)
        {
            _lastZaapDecisionLog = targetMap;
            bool groundZaapHere = _world.Get(_pid).Interactives.Any(i => i.ElementTypeId == 16 && i.HarvestSkill is not null);
            Info("trajet", Loc.Fmt("L.Lua.TravelZaapDecision", targetMap, _launch.KnownZaapMaps(_pid).Count, nearZaap?.zaapMap ?? 0, groundZaapHere, havenBagHere));
        }
        if ((tryZaap || tryZaapiShortcut) && zaapRoute != ZaapRoute.Auto)
            Info("trajet", WorldMapZaapTravel.ChosenReason(zaapRoute));
        var zaapAttempts = WorldMapZaapTravel.Attempts(zaapRoute, walkFirst: false);
        if (tryZaap || tryZaapiShortcut)
        {
            // Auto : zaapi si la carte en a un, puis zaap depuis la carte, zaap au sol, havre-sac.
            // Un choix explicite n'enchaîne pas les autres. Succès du zaap carte = carte observée égale à la destination.
            // tryZaapiShortcut : le zaapi seul, sans ouvrir le havre-sac quand la liste des zaaps ne justifiait rien.
            bool explicitZaapi = zaapRoute == ZaapRoute.Zaapi;
            bool zaapiMoved = (tryZaapiShortcut || zaapAttempts.Contains(ZaapRoute.Zaapi))
                && TravelViaZaapi(targetMap, path.Count, unreachableOnFoot, zaapMargin, explicitZaapi, ct);
            bool explicitWorldMap = zaapRoute == ZaapRoute.WorldMap;
            bool movedByWorld = false;
            bool noUsefulZaap = false;
            bool groundMoved = false;
            if (!tryZaapiShortcut)
            {
                movedByWorld = !zaapiMoved && zaapAttempts.Contains(ZaapRoute.WorldMap)
                    && TravelViaWorldMapZaap(targetMap, path.Count, unreachableOnFoot, ZaapWalkMargin, explicitWorldMap, honorPause: !explicitWorldMap, ct);
                if (!zaapiMoved && !movedByWorld && zaapAttempts.Contains(ZaapRoute.Ground))
                    groundMoved = TravelViaGroundZaap(targetMap, cur, path.Count, ct, probeHere, out noUsefulZaap);
                if (!zaapiMoved && !movedByWorld && !groundMoved && zaapAttempts.Contains(ZaapRoute.HavenBag) && havenBagHere && !noUsefulZaap)
                    TravelViaZaap(targetMap, cur, path.Count, ct, probeHere);   // téléporte OU ressort du havre-sac (voir ExitHavenBag)
            }
            // Re-lire la carte SYSTÉMATIQUEMENT : le raccourci a pu nous téléporter (zaap) OU nous laisser ailleurs
            // (havre-sac si la sortie a échoué). NE JAMAIS exécuter le `path` calculé depuis la carte de départ si on a
            // bougé -> re-router depuis la carte réelle (évite « saut X → Y : envoi échoué » en boucle depuis le havre-sac).
            long now = _launch.CurrentMap(_pid) ?? 0;
            if (now == targetMap) return true;
            if (InHavenBag(now))
            {
                Warn("trajet", "sortie du havre-sac non confirmée -> trajet arrêté, aucune marche depuis le havre-sac");
                return false;
            }
            if (now != cur) { cur = now; path = PlanPath(cur, targetMap, avoid, avoidEdges, ct); }
        }

        // Graphe incomplet (source ou cible absente du world-graph vivant, ex. Tainéla -> Astrub) : marche glouton par coordonnées
        // (même calque connu des deux côtés), une sortie de bord à la fois, jusqu'à la cible.
        if (path.Count == 0 && reroutes == 0 && avoid.Count == 0 && avoidEdges.Count == 0 && WalkByCoordinates(targetMap, ct))
            return true;

        if (path.Count == 0)
        {
            if (avoid.Count > 0 || avoidEdges.Count > 0)
            {
                _lastTravelFailure = "no_route";
                Warn("trajet", Loc.Fmt("L.Lua.TravelNoAlt", targetMap, avoid.Count + avoidEdges.Count));
                return false;
            }  // toutes les sorties/arêtes tentées ont échoué
            bool targetKnown = _launch.WorldGraphKnows((int)targetMap);
            bool srcKnown = _launch.WorldGraphKnows((int)cur);
            _lastTravelFailure = "no_route";
            Warn("trajet", Loc.Fmt("L.Lua.TravelNoGraph", cur, targetMap, targetKnown, srcKnown) + " "
                + (targetKnown ? Loc.T("L.Lua.TravelNoGraphCut") : Loc.T("L.Lua.TravelNoGraphInterior")));
            return false;
        }
        Info("trajet", Loc.Fmt("L.Lua.TravelHops", cur, targetMap, path.Count));

        foreach (var tr in path)
        {
            ThrowIfStopping(ct);
            if (_launch.IsInFight(_pid)) { Warn("trajet", Loc.T("L.Lua.TravelCombat")); return false; }
            if (!WaitForTeamDeparture(ct)) return false;
            WaitSelfReady(ct);
            long map = _launch.CurrentMap(_pid) ?? 0;
            // Saut déjà franchi (arrivée tardive) : comparé à l'ARRIVÉE uniquement — jamais à l'id envoyé, qui peut valoir la
            // carte SOURCE elle-même (ScrollAction 107224064 ouest : envoi 107224064, arrivée 107223040).
            if (map == tr.Arrival) continue;
            var st = _world.Get(_pid);
            if (st.SelfCell is not int src) { Warn("trajet", Loc.T("L.Lua.TravelSelfUnknown")); return false; }

            // FRANCHISSEMENT CONDITIONNEL (porte/PNJ gardé par un critère, ex pont « Qf=230 »). Le critère a DÉJÀ été
            // validé par le BFS de repli (sinon cette arête ne serait pas dans le chemin). Exécution dédiée : marcher à
            // la case d'interaction, déclencher l'interactif, piloter le dialogue PNJ jusqu'au changement de carte.
            // CrossGatedDoorAsync confirme lui-même l'arrivée -> on saute le hopSent/WaitUntil partagé.
            // Seul un franchissement INTERACTIF (porte/PNJ, type 32 ou interactif présent sur la case) passe par ici : une
            // ScrollAction (type 2) ou un soleil (type 8) conditionnels n'ont pas d'interactif -> chemin bord/soleil normal
            // plus bas (envoi de l'id, attente de n'importe quel changement de carte). Ex 134659 est -> 195301386 (quête 2041).
            const int DoorType = 32, SunType = 8;
            // A reconnect may deliver the first map before interactive positions are ready.
            // Do not blacklist its only exit on an empty snapshot: ask for one fresh snapshot.
            if (tr.Type == DoorType && st.Interactives is not { Count: > 0 })
            {
                var decoded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnDecoded(uint source, IReadOnlyList<MapInteractive> _)
                {
                    if (source == _pid && _launch.CurrentMap(_pid) == map) decoded.TrySetResult();
                }
                _launch.MapInteractivesDecoded += OnDecoded;
                try
                {
                    if (_launch.SendMapInformationRequest(_pid))
                    {
                        try { decoded.Task.WaitAsync(TimeSpan.FromSeconds(5), ct).GetAwaiter().GetResult(); }
                        catch (TimeoutException) { /* Existing missing-door path below handles the refusal. */ }
                    }
                }
                finally { _launch.MapInteractivesDecoded -= OnDecoded; }
                var refreshedSt = _world.Get(_pid);
                st = refreshedSt;
            }
            // Un critère d'ÉTAT D'INTERACTIF seul (MI : porte au repos) n'a ni PNJ ni dialogue : branche porte normale plus bas
            // (sinon ~4 s d'attente d'un dialogue qui ne vient jamais).
            bool gatedCross = tr.Criterion is not null && !QuestCriteria.IsInteractiveStateOnly(tr.Criterion);
            var gd = gatedCross ? DoorOf(st, tr) : null;
            if (gatedCross && (tr.Type == DoorType || gd?.HarvestSkill is not null))
            {
                if (gd?.HarvestSkill is null) { Warn("trajet", Loc.Fmt("L.Lua.TravelDoorNotFound", tr.CellId, map)); return Reroute(targetMap, ct, avoid, avoidEdges, reroutes, (int)map, tr.Arrival, tr.CellId); }
                int? gstand = _launch.DoorStandCell(_pid, map, src, gd, st.IsWalkable, st.StepAllowed, out var gstandWalk);
                if (gstand is null) { Warn("trajet", Loc.Fmt("L.Lua.TravelDoorUnreachable", tr.CellId)); return Reroute(targetMap, ct, avoid, avoidEdges, reroutes, (int)map, tr.Arrival, tr.CellId); }
                if (gstand.Value != src)
                {
                    var gmv = _launch.SendAndWaitAsync(_pid, () => _launch.MoveTo(_pid, map, src, gstand.Value, gstandWalk, true, st.StepAllowed), MoveTimeoutMs, ct).GetAwaiter().GetResult();
                    if (gmv != LaunchService.MoveOutcome.Arrived) { Warn("trajet", Loc.Fmt("L.Lua.TravelDoorWalkFail", tr.CellId, gmv)); return Reroute(targetMap, ct, avoid, avoidEdges, reroutes, (int)map, tr.Arrival, tr.CellId); }
                }
                Info("trajet", Loc.Fmt("L.Lua.TravelHopGated", map, tr.Arrival, tr.CellId));
                if (!_launch.CrossGatedDoorAsync(_pid, gd, tr.Arrival, ct).GetAwaiter().GetResult())   // attend la carte d'ARRIVÉE
                    return Reroute(targetMap, ct, avoid, avoidEdges, reroutes, (int)map, tr.Arrival, tr.CellId);
                InterruptibleDelay(MapSettle(), ct);
                continue;   // saut franchi (arrivée confirmée par CrossGatedDoorAsync) -> hop suivant
            }

            // Dispatch EXACT d'un bot tiers (InstanceData.MovePlayerAndChangeMap) selon le type de transition:
            int refused0 = _launch.MoveRefusedCount(_pid);
            int[]? obstaclesBeforeDetour = null;
            void UndoDetour()
            {
                if (obstaclesBeforeDetour is not { } before) return;
                var world = _world.Get(_pid);
                world.ClearCustomObstacles();
                if (before.Length > 0) world.AddCustomObstacles(map, before);
                obstaclesBeforeDetour = null;
            }
            bool hopSent;
            if (tr.Type == DoorType)     // PORTE : marche ADJACENT à la cellule + utilise l'interactif (l'iwo change la carte)
            {
                // Porte absente / injoignable -> on REROUTE (interdit CETTE cellule) au lieu d'abandonner : une porte type 32
                // peut doubler un BORD type 1 vers le MÊME voisin (le BFS choisit la porte en premier) ; l'éviter par cellule
                // laisse le BFS reprendre par le bord. Sinon « porte X injoignable » stoppait tout le voyage.
                var door = DoorOf(st, tr);
                if (door?.HarvestSkill is null) { Warn("trajet", Loc.Fmt("L.Lua.TravelDoorNotFound", tr.CellId, map)); return Reroute(targetMap, ct, avoid, avoidEdges, reroutes, (int)map, tr.Arrival, tr.CellId); }
                // Voisine de la porte dans le 8-voisinage (chemin le plus court, à égalité orthogonale puis diagonale ; certaines portes n'ont qu'une voisine
                // marchable en diagonale, ex 352 -> 380, d'où l'ancien « porte injoignable »), cf. DoorStandCell.
                int? stand = _launch.DoorStandCell(_pid, map, src, door, st.IsWalkable, st.StepAllowed, out var standWalk);
                if (stand is null) { Warn("trajet", Loc.Fmt("L.Lua.TravelDoorUnreachable", tr.CellId)); return Reroute(targetMap, ct, avoid, avoidEdges, reroutes, (int)map, tr.Arrival, tr.CellId); }
                if (stand.Value != src)
                {
                    var mv = _launch.SendAndWaitAsync(_pid, () => _launch.MoveTo(_pid, map, src, stand.Value, standWalk, true, st.StepAllowed), MoveTimeoutMs, ct).GetAwaiter().GetResult();
                    if (mv != LaunchService.MoveOutcome.Arrived) { Warn("trajet", Loc.Fmt("L.Lua.TravelDoorWalkFail", tr.CellId, mv)); return Reroute(targetMap, ct, avoid, avoidEdges, reroutes, (int)map, tr.Arrival, tr.CellId); }
                }
                Info("trajet", Loc.Fmt("L.Lua.TravelHopDoor", map, tr.Arrival, tr.Type, tr.CellId));
                // Skill EXACT de la transition (m_skillId, ex 184 « Utiliser ») s'il est actif, sinon le skill par défaut.
                hopSent = tr.SkillId > 0 ? _launch.SendInteractiveSkill(_pid, door, tr.SkillId) : _launch.SendHarvest(_pid, door);
            }
            else if (tr.Type == SunType) // SOLEIL : marcher SUR la cellule -> le serveur change de carte tout seul (ni jqk ni iwo)
            {
                Info("trajet", Loc.Fmt("L.Lua.TravelHopSun", map, tr.Arrival, tr.Type, tr.CellId));
                if (src == tr.CellId) hopSent = true;   // deja sur le soleil -> le changement doit suivre (sinon WaitUntil echoue)
                else
                {
                    var mv = _launch.SendAndWaitAsync(_pid, () => _launch.MoveTo(_pid, map, src, tr.CellId, st.IsWalkable, true, st.StepAllowed), MoveTimeoutMs, ct).GetAwaiter().GetResult();
                    // arrive sur le soleil (jsq) OU changement de carte qui supersede le move ; refus/pas-envoye = echec.
                    hopSent = mv is not (LaunchService.MoveOutcome.NotSent or LaunchService.MoveOutcome.Refused);
                }
            }
            else                         // BORD / scroll (type 1 et autres) : marche jusqu'a la cellule de transition puis jqk
            {
                // Le graphe ne donne qu'UNE cellule de bord ; si elle est sur un etage inaccessible (elevation) le MoveTo
                // echoue -> on prend une AUTRE cellule du meme bord REELLEMENT atteignable (comme ChangeMapDirection). Repli
                // sur tr.CellId si aucune prouvee. La direction etant la meme, on arrive sur le meme voisin.
                int exitCell = _launch.ReachableEdgeCell(map, src, tr, st.IsWalkable, st.StepAllowed);
                Info("trajet", Loc.Fmt("L.Lua.TravelHopEdge", map, tr.Arrival, tr.Type, exitCell));
                // PAYLOAD = id ENVOYÉ (m_transitionMapId), JAMAIS m_to : pour une ScrollAction c'est un voisin théorique que le
                // serveur redirige vers la vraie carte. Envoyer m_to = ignoré (06/09).
                if (tr.Arrival != tr.TransitionMapId)
                    GivreLauncher.Core.Transport.TransportLog.Line($"[trajet] pid {_pid} : scroll {map} -> envoi {tr.TransitionMapId}, arrivée attendue {tr.Arrival} (cellule {exitCell})");
                hopSent = _launch.ChangeMapToCell(_pid, map, src, exitCell, tr.TransitionMapId, st.IsWalkableForScript, st.StepAllowed, st.SelfCellConfirmed);
                // REFUS du serveur (case réellement bloquée que les données de carte disent libre, ex. Pandala 206307842 : 370->538
                // refusé, la case 426 aussi à la main) : au lieu d'attendre 16 s puis replanifier, on écarte les cases du chemin
                // refusé (obstacles de CETTE carte) et on retente un détour, 2 fois au plus.
                for (int retry = 0; hopSent && retry < 2; retry++)
                {
                    bool refused = false;
                    WaitUntil(() => { long m = _launch.CurrentMap(_pid) ?? 0; refused = _launch.MoveRefusedCount(_pid) > refused0; return (m != 0 && m != map) || refused; }, MapChangeAttemptTimeoutMs, ct);
                    if (!refused || (_launch.CurrentMap(_pid) ?? 0) != map) { if (!refused && (_launch.CurrentMap(_pid) ?? 0) == map) hopSent = false; break; }
                    if (_launch.LastBotPath(_pid) is not { } lp || lp.Map != map || lp.Cells.Length < 3) { hopSent = false; break; }
                    // Un premier refus est souvent passager (arrivée récente sur la carte) : même chemin après stabilisation. Bannir tout le
                    // chemin dès le premier refus pouvait enfermer le perso : plus aucun chemin vers aucun bord de la carte.
                    if (retry == 0)
                        Warn("trajet", $"déplacement refusé par le serveur sur la carte {map} : nouvel essai après stabilisation");
                    else
                    {
                        obstaclesBeforeDetour = _world.Get(_pid).CustomObstacleCells.ToArray();
                        _world.Get(_pid).AddCustomObstacles(map, lp.Cells.Skip(1).Take(lp.Cells.Length - 2));
                        Warn("trajet", $"déplacement refusé par le serveur sur la carte {map} : détour en évitant {lp.Cells.Length - 2} case(s)");
                    }
                    InterruptibleDelay(retry == 0 ? Math.Max(1200, MapSettle()) : 400, ct);
                    st = _world.Get(_pid);
                    if (st.SelfCell is not int src2) { hopSent = false; break; }
                    exitCell = _launch.ReachableEdgeCell(map, src2, tr, st.IsWalkableForScript, st.StepAllowed);
                    refused0 = _launch.MoveRefusedCount(_pid);
                    hopSent = _launch.ChangeMapToCell(_pid, map, src2, exitCell, tr.TransitionMapId, st.IsWalkableForScript, st.StepAllowed, st.SelfCellConfirmed);
                }
            }
            if (!hopSent) { UndoDetour(); Warn("trajet", Loc.Fmt("L.Lua.TravelHopSendFail", tr.Arrival)); return Reroute(targetMap, ct, avoid, avoidEdges, reroutes, (int)map, tr.Arrival, tr.CellId); }
            // Attente d'un CHANGEMENT de carte (quelle qu'elle soit) : l'arrivée réelle est m_to — pas l'id envoyé — et une arête
            // mal décrite peut mener ailleurs. Arrivée inattendue = on RE-PLANIFIE depuis la carte réelle (sans bannir l'arête).
            if (!WaitUntil(() => { long m = _launch.CurrentMap(_pid) ?? 0; return (m != 0 && m != map) || _launch.MoveRefusedCount(_pid) > refused0; }, MapChangeAttemptTimeoutMs, ct)
                || ((_launch.CurrentMap(_pid) ?? 0) == map && _launch.MoveRefusedCount(_pid) > refused0))
            { UndoDetour(); Warn("trajet", Loc.Fmt("L.Lua.TravelHopTimeout", tr.Arrival, MapChangeAttemptTimeoutMs / 1000)); return Reroute(targetMap, ct, avoid, avoidEdges, reroutes, (int)map, tr.Arrival, tr.CellId); }
            long landed = _launch.CurrentMap(_pid) ?? 0;
            if (landed != tr.Arrival)
            {
                InterruptibleDelay(MapSettle(), ct);   // même stabilisation qu'un saut normal (le serveur refuse encore les moves un instant)
                if (landed == targetMap) break;        // arrivé quand même à destination
                Warn("trajet", Loc.Fmt("L.Lua.TravelHopUnexpected", landed, tr.Arrival, tr.TransitionMapId));
                // Posé sur l'id ENVOYÉ (carte réelle, redirection dépendant de l'état du perso, ex quête) : la même arête y
                // renverrait à chaque fois -> on l'écarte pour ce trajet (évite le ping-pong jusqu'à MaxTravelReroutes).
                if (landed == tr.TransitionMapId) avoidEdges.Add(WorldGraph.EdgeKey((int)map, tr.CellId));
                if (reroutes >= MaxTravelReroutes) return false;
                return GoToMapId(targetMap, ct, avoid, reroutes + 1, avoidEdges);   // re-planifie depuis la carte réellement atteinte
            }
            InterruptibleDelay(MapSettle(), ct);   // marge de stabilisation avant le saut suivant (le serveur refuse encore les moves un instant)
        }
        bool done = (_launch.CurrentMap(_pid) ?? 0) == targetMap;
        if (done) Ok("trajet", Loc.Fmt("L.Lua.TravelArrived", targetMap));
        else Warn("trajet", Loc.Fmt("L.Lua.TravelWrongMap", targetMap, _launch.CurrentMap(_pid)));
        return done;
    }

    // Un saut a échoué (sortie bloquée / refus serveur) : on interdit CETTE ARÊTE précise (from->failed) et on cherche un
    // AUTRE chemin (BFS), jusqu'à MaxTravelReroutes fois. On n'évite PLUS la carte cible entière : un bord peut être
    // physiquement infranchissable (différence d'ÉLÉVATION : la cellule de bord du graphe est sur un étage inaccessible)
    // alors que la même carte reste joignable par un AUTRE bord. Ex : 171966990 -> 171966478 par l'OUEST (cellule 490) est
    // bloqué, mais l'approche par le NORD (990->989->477->478, cellule 542) passe — exactement le « en haut puis à gauche »
    // fait à la main. Éviter la carte cible entière (ancien comportement) la rendait injoignable dès que le saut raté était
    // le DERNIER (== destination) -> « aucun autre chemin » immédiat. Sinon on renonce (false) et le cap de RunRoute arrête.
    // Chemin depuis la carte COURANTE, par SECTEUR : la zone de départ est celle de la cellule du perso (attendue confirmée,
    // borné), l'état des interactifs est lu en direct (critères MI des portes). Zone inconnue -> niveau carte (historique).
    private IReadOnlyList<MapTransition> PlanPath(long cur, long targetMap, HashSet<int> avoid, HashSet<long> avoidEdges, CancellationToken ct)
    {
        var st = _world.Get(_pid);
        // Carte à UN secteur : il est connu sans la cellule. Hors graphe / ancien cache : 0 (niveau carte). Seule une carte à
        // PLUSIEURS secteurs exige la cellule confirmée du perso — et c'est le seul cas où l'on attend (borné) : jamais sur une
        // instance de combat ou une carte hors graphe (sinon 8-13 s de gel, cf RunRoute).
        int zone = _launch.WorldSoleZone(cur);
        if (zone < 0)
        {
            if (!st.SelfReadyFor(cur) || !st.CellsReadyFor(cur)) { WaitSelfReady(ct); st = _world.Get(_pid); }
            zone = st.RpZoneFor(cur);
        }
        Func<int, int?> state = elem => st.MapId == cur ? st.Interactives.FirstOrDefault(i => i.ElementId == elem)?.State : null;
        return _launch.WorldPath((int)cur, (int)targetMap, avoid, _pid, avoidEdges, zone, state);
    }

    // Porte d'une transition : l'interactif EXACT du world-graph (m_id) s'il est sur la carte, sinon celui posé sur la cellule.
    private static MapInteractive? DoorOf(BotWorldState st, MapTransition tr)
        => (tr.ElementId > 0 ? st.Interactives.FirstOrDefault(i => i.ElementId == tr.ElementId) : null) ?? st.InteractiveAt(tr.CellId);

    private bool Reroute(long targetMap, CancellationToken ct, HashSet<int> avoid, HashSet<long> avoidEdges, int reroutes, int fromMap, int failedMap, int failedCell)
    {
        // #2 : au 1er échec d'une arête, NE PAS la bannir — beaucoup d'échecs sont TRANSITOIRES (position self pas encore
        // confirmée -> hopSent=false, ou timeout de 16 s sur un chargement de carte lent). On la RE-TENTE (le re-BFS
        // reproduira le même chemin). Ce n'est qu'au 2e échec de la MÊME arête qu'on la bannit (bord réellement infranchissable
        // par élévation) et qu'on route autour. Évite qu'un simple hoquet transforme un trajet d'1 carte en long détour.
        bool retry = false;
        if (fromMap > 0 && failedCell >= 0)
        {
            long ek = WorldGraph.EdgeKey(fromMap, failedCell);
            int fails = _edgeFails[ek] = _edgeFails.GetValueOrDefault(ek) + 1;
            if (fails >= 2) avoidEdges.Add(ek);   // 2e échec -> banni (route autour)
            else retry = true;                     // 1er échec -> on re-tente la MÊME arête
        }
        if (reroutes >= MaxTravelReroutes) return false;
        Info("trajet", Loc.Fmt("L.Lua.TravelReroute", failedMap));
        InterruptibleDelay(retry ? 1200 : 600, ct);   // 1er échec : pause un peu plus longue avant de re-tenter la même arête
        return GoToMapId(targetMap, ct, avoid, reroutes + 1, avoidEdges);   // probe sans objet en reroute (tryZaap exige reroutes == 0)
    }

    private bool UseInteractive(int cell, CancellationToken ct, bool verbose)
    {
        var inter = _world.Get(_pid).InteractiveAt(cell);
        if (inter is null) return false;
        if (_launch.IsGatherResource(inter)) return GatherResource(inter, ct, verbose);
        return UseElement(inter.ElementId, 0, ct, verbose)
            is UseOutcome.Used or UseOutcome.Ended or UseOutcome.MapChanged;
    }

    /// <summary>Voyage auto jusqu'à <paramref name="targetMap"/> (BFS world-graph + raccourci zaap + reroute par arête,
    /// exactement comme le verbe Lua travelTo) et renvoie true si arrivé. BLOQUANT -> encapsulé en Task.Run (jamais sur
    /// le thread UI). Point d'entrée PUBLIC pour un pilote NON-Lua : le moteur d'ÉQUIPE (TeamPilot) fait suivre un
    /// follower vers la carte du chef en réutilisant CE chemin (hérite des durcissements de déplacement), sans démarrer
    /// un script MoonSharp. L'annulation (CTS) déroule proprement (ScriptStopException avalée).</summary>
    public Task<bool> TravelToMapAsync(long targetMap, CancellationToken ct)
        => Task.Run(() => RunTravelAction(ct, token => GoToMapId(targetMap, token)), ct);

    private const int MaxFollowHops = 40;   // garde-fou anti-boucle si le chef fuit sans cesse (chaque itération est déjà un voyage multi-sauts)

    /// <summary>SUIVI SIMULTANÉ: chasse la carte VIVANTE du chef, RE-LUE à CHAQUE
    /// itération via <paramref name="leaderMapNow"/> — le follower vise donc toujours là où le chef EST, jamais une carte
    /// périmée capturée au départ. Boucle jusqu'à être sur la carte du chef (ou borne <see cref="MaxFollowHops"/>). BLOQUANT
    /// -> Task.Run. C'est ce qui donne le « déplacement en même temps » : dès que le chef bouge encore, on repart aussitôt.</summary>
    /// <summary>SUIVI COLLÉ: rejoue UN changement de carte DIRECTIONNEL (la sortie exacte prise par le
    /// chef), SANS re-pathfinding — le follower part dans le même sens depuis la même carte. `ChangeMap` gère bord/interactif/
    /// soleil et re-tente ; renvoie false si la sortie est bloquée POUR ce follower (l'appelant retombe alors sur la chasse).</summary>
    public Task<bool> StepMapDirectionAsync(string dir, CancellationToken ct)
        => Task.Run(() => RunTravelAction(ct, token => ChangeMap(dir, token, follow: true)), ct);

    public Task<bool> FollowLeaderMapAsync(Func<long> leaderMapNow, CancellationToken ct)
        => Task.Run(() => RunTravelAction(ct, token =>
        {
                for (int hops = 0; hops < MaxFollowHops; hops++)
                {
                    ThrowIfStopping(token);
                    long lm = leaderMapNow();
                    if (lm == 0) return false;                                   // chef hors jeu / carte inconnue
                    if ((_launch.CurrentMap(_pid) ?? 0) == lm) return true;      // rattrapé : sur la carte du chef
                    if (_launch.IsInFight(_pid)) return false;                   // follower en combat -> l'auto-join prend le relais
                    if (!_launch.WorldGraphKnows((int)lm)) return false;         // le chef est sur une INSTANCE DE COMBAT (off-graph) -> on ne la chasse PAS, l'auto-join s'en charge
                    if (!GoToMapId(lm, token) && leaderMapNow() == lm) return false; // saut échoué ET le chef n'a pas rebougé -> on renonce
                }
                return (_launch.CurrentMap(_pid) ?? 0) == leaderMapNow();
        }), ct);

    private bool RunTravelAction(CancellationToken ct, Func<CancellationToken, bool> action)
    {
        using var owner = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var scope = _launch.ScriptArrivalScope(owner.Token);
        try { return ReportTravel(() => action(owner.Token)); }
        catch (ScriptStopException) { return false; }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { return false; }
        finally { _launch.CancelScriptArrivalActions(_pid, owner); }
    }

    private Task<bool> RunUiTravelAsync(CancellationToken ct, Func<CancellationToken, bool> action)
        => Task.Run(() =>
        {
            bool ok = RunTravelAction(ct, action);
            if (!ok && !ct.IsCancellationRequested)
                Toasts.Post(LastTravelResult?.Reason ?? QuickActions.Interrupted, success: false);
            return ok;
        }, ct);

    /// <summary>Zaap depuis la carte du monde vers <paramref name="destMap"/>. Succès seulement si la carte observée devient cette destination.</summary>
    public Task<(bool Ok, string Reason)> QuickWorldZaapAsync(long destMap, CancellationToken ct)
        => Task.Run(() =>
        {
            string reason = "";
            bool ok = RunTravelAction(ct, token =>
            {
                if (destMap <= 0) { reason = WorldMapZaapTravel.UnknownReason; return false; }
                if (_launch.IsInFight(_pid)) { reason = WorldMapZaapTravel.CombatReason; return false; }
                if (WorldMapZaapTravel.Arrived(_launch.CurrentMap(_pid) ?? 0, destMap))
                {
                    reason = QuickActions.AlreadyHere;
                    return false;
                }
                var offer = _launch.AssessWorldMapZaap(_pid, destMap);
                bool listedWithoutCost = offer.Known && offer.CostKamas is null;
                if (!offer.CanSend && !listedWithoutCost) { reason = offer.Reason; return false; }
                bool arrived = TravelFromWorldMap(destMap, token, listedWithoutCost);
                if (arrived)
                {
                    reason = WorldMapZaapTravel.ArrivedReason(destMap, offer.CostKamas);
                    return true;
                }
                if (token.IsCancellationRequested) { reason = QuickActions.Cancelled; return false; }
                if (_launch.IsInFight(_pid)) { reason = WorldMapZaapTravel.CombatReason; return false; }
                offer = _launch.AssessWorldMapZaap(_pid, destMap);
                listedWithoutCost = offer.Known && offer.CostKamas is null;
                reason = _travelServerRefusal ?? (offer.CanSend || listedWithoutCost
                    ? WorldMapZaapTravel.UnconfirmedReason(destMap) : offer.Reason);
                return false;
            });
            if (reason.Length == 0)
                reason = ct.IsCancellationRequested ? QuickActions.Cancelled
                    : ok ? WorldMapZaapTravel.ArrivedReason(destMap) : WorldMapZaapTravel.SendFailedReason;
            return (ok, reason);
        });

    /// <summary>
    /// Zaapi vers <paramref name="destMap"/>. Si <paramref name="hostMap"/> est une autre carte, le planificateur l'atteint d'abord.
    /// L'ouverture et l'envoi sont ceux du zaapi de la carte (skill 157, puis TeleportRequest source 1 / destination 1).
    /// </summary>
    public Task<(bool Ok, string Reason)> QuickZaapiAsync(long destMap, long hostMap, CancellationToken ct)
        => Task.Run(() =>
        {
            string reason = "";
            bool ok = RunTravelAction(ct, token =>
            {
                if (destMap <= 0) { reason = ZaapiTravel.UnknownReason; return false; }
                if (_launch.IsInFight(_pid)) { reason = ZaapiTravel.CombatReason; return false; }
                if (ZaapiTravel.Arrived(_launch.CurrentMap(_pid) ?? 0, destMap))
                {
                    reason = QuickActions.AlreadyHere;
                    return false;
                }
                long here = _launch.CurrentMap(_pid) ?? 0;
                if (hostMap > 0 && hostMap != destMap && here != hostMap && ZaapiOnCurrentMap() is null)
                {
                    if (!GoToMapId(hostMap, token))
                    {
                        reason = token.IsCancellationRequested ? QuickActions.Cancelled : QuickActions.PlannerMissed;
                        return false;
                    }
                }
                if (token.IsCancellationRequested) { reason = QuickActions.Cancelled; return false; }
                if (_launch.IsInFight(_pid)) { reason = ZaapiTravel.CombatReason; return false; }
                if (ZaapiTravel.Arrived(_launch.CurrentMap(_pid) ?? 0, destMap))
                {
                    reason = QuickActions.AlreadyHere;
                    return false;
                }
                var zi = ZaapiOnCurrentMap();
                if (zi is null)
                    WaitUntil(() => (zi = ZaapiOnCurrentMap()) is not null, 2000, token);
                if (zi is null) { reason = ZaapiTravel.NotHereReason; return false; }
                if (!OpenZaapiDialog(zi, token, out var openFailure)) { reason = openFailure; return false; }
                var offer = _launch.AssessZaapi(_pid, destMap);
                if (!offer.CanSend)
                {
                    reason = offer.Reason;
                    _launch.LeaveDialog(_pid);
                    return false;
                }
                Info("trajet", offer.Reason);
                bool sent = false;
                bool arrived = ConfirmZaapTeleport(destMap, () => sent = _launch.TrySendZaapi(_pid, destMap), token, out var refusal, 8000);
                if (!arrived)
                {
                    reason = token.IsCancellationRequested ? QuickActions.Cancelled
                        : refusal ?? (sent ? ZaapiTravel.UnconfirmedReason(destMap) : ZaapiTravel.SendFailedReason);
                    Warn("trajet", reason);
                    _launch.LeaveDialog(_pid);
                    return false;
                }
                // RÉGLAGE D'ARRIVÉE (settle de carte + position self confirmée).
                // Un ARRÊT COOPÉRATIF (ScriptStopException) N'EST PAS une arrivée réussie : le marqueur
                // n'est plus AVALÉ ici. Il remonte au Task<bool> de cette action (RunTravelAction), qui le
                // convertit en ÉCHEC (« Action annulée. »), jamais en succès. ATTENTION : ce chemin est
                // celui de l'action rapide de l'UI (QuickZaapiAsync) — il ne passe PAS par le runtime Lua.
                // Le réglage Lua-facing (FinishZaapi) propage déjà le marqueur jusqu'au run Lua.
                // Le motif d'arrivée n'est donc posé qu'APRÈS le réglage : un arrêt laisse le motif vide
                // -> la chute « annulé » de la fin s'applique.
                InterruptibleDelay(MapSettle(), token);
                WaitSelfReady(token);
                reason = ZaapiTravel.ArrivedReason(destMap, offer.CostKamas ?? 0);
                Ok("trajet", reason);
                return true;
            });
            if (reason.Length == 0)
                reason = ct.IsCancellationRequested ? QuickActions.Cancelled
                    : ok ? ZaapiTravel.ArrivedReason(destMap, 0) : ZaapiTravel.SendFailedReason;
            if (!ok && reason == QuickActions.PlannerMissed && LastTravelResult is { Ok: false } failed
                && failed.Reason != QuickActions.Interrupted)
                reason = failed.Reason;
            return (ok, reason);
        });
}
