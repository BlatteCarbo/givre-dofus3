using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>
/// Read-only UI facade. The live diagnostic ingress has no proved transport
/// generation/ACK correlation: StageConfirmed is never promoted here, and the
/// candidate is hypothetical while another command may still be in flight.
/// This facade does not call SendCast, Inject, IActionSender or TryMove.
/// </summary>
public sealed record CombatTacticalAdvisoryStatus(
    uint Pid, bool Enabled, VerifiedTacticalExecutor.Phase Phase,
    ClassMechanics.ComboKind? ClassTactic, PlannerAction? Candidate,
    string Reason, long FightId = 0, long MapId = 0, int Turn = 0,
    int Generation = 0, int ConfirmedStages = 0)
{
    public bool CanDispatch => false;
    public bool IsHypothetical => Candidate is not null;
    public bool TransportProvenanceEstablished => false;
}

public sealed partial class LaunchService
{
    private sealed class TacticalAdvisorWindow
    {
        public readonly object Gate = new();
        public bool Enabled;
        public long FightId;
        public long MapId;
        public int Generation;
        public int Turn;
        public int TurnEpoch;
        public object? MapTicket;
        public VerifiedTacticalExecutor? Executor;
        public VerifiedTacticalSequence.Plan? Plan;
        public CombatTacticalAdvisoryStatus? Last;
        public bool Expired;
        public bool SourceRevoked;
    }

    private readonly ConcurrentDictionary<uint, TacticalAdvisorWindow> _tacticalAdvisorWindows = new();
    private readonly ConcurrentDictionary<uint, byte> _tacticalAdvisorRefreshQueued = new();
    private readonly object _tacticalAdvisorSubscriptionGate = new();
    private bool _tacticalAdvisorSubscribed;

    /// <summary>
    /// REAL current Socket source close/replacement invalidates any candidate
    /// pending under the same PID. This is a one-way advisory revocation, not
    /// proof of an acknowledged cast or permission to send one. The operator
    /// must opt-in again after re-authenticating the replacement source.
    /// </summary>
    private void RevokeTacticalAdvisorOnSocketSourceLoss(uint pid, string reason)
    {
        if (_tacticalAdvisorWindows is null ||
            !_tacticalAdvisorWindows.TryGetValue(pid, out var window)) return;
        CombatTacticalAdvisoryStatus? revoked = null;
        lock (window.Gate)
        {
            if (!window.Enabled || window.SourceRevoked) return;
            window.SourceRevoked = true;
            window.Executor?.SetEnabled(false);
            window.Executor = null;
            window.Plan = null;
            window.Expired = true;
            revoked = window.Last = new(pid, true,
                VerifiedTacticalExecutor.Phase.Expired, null, null,
                "Socket source revoked: " + reason +
                "; new explicit opt-in required; no cast ACK inferred");
        }
        if (revoked is not null)
        {
            try { CombatTacticalAdvisoryUpdated?.Invoke(pid, revoked); }
            catch (Exception)
            {
                // A desktop subscriber cannot fault SocketGameClient's
                // source-validation callback or force packet replay.
            }
        }
    }

    /// <summary>
    /// Raised after an opt-in live combat observation has been projected to a
    /// new read-only status. Listeners may always call ReadCombatTacticalAdvisoryStatus
    /// for the latest snapshot; there is no action/send callback.
    /// </summary>
    public event Action<uint, CombatTacticalAdvisoryStatus>? CombatTacticalAdvisoryUpdated;

    private void EnsureTacticalAdvisorObservationHooks()
    {
        lock (_tacticalAdvisorSubscriptionGate)
        {
            if (_tacticalAdvisorSubscribed) return;
            _tacticalAdvisorSubscribed = true;
            FightersUpdated += (pid, _) => QueueCombatTacticalAdvisoryRefresh(pid);
            FighterStatsUpdated += QueueCombatTacticalAdvisoryRefresh;
            MapCellsLoaded += (pid, _, _) => QueueCombatTacticalAdvisoryRefresh(pid);
        }
    }

    private void QueueCombatTacticalAdvisoryRefresh(uint pid)
    {
        // FormatterServices-based decoder regression fixtures deliberately
        // construct LaunchService without running field initializers. In that
        // context the optional advisor is absent, not a reason to crash the
        // game's OnDecoded path or to promote any diagnostic into authority.
        if (_tacticalAdvisorWindows is null || _tacticalAdvisorRefreshQueued is null
            || !_tacticalAdvisorWindows.TryGetValue(pid, out var window) || !window.Enabled
            || !_tacticalAdvisorRefreshQueued.TryAdd(pid, 0)) return;
        // At most one queued projection per PID. The engine packet callback
        // never waits on disk map loading or tactical candidate evaluation.
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var status = ReadCombatTacticalAdvisoryStatus(pid);
                if (status.Enabled)
                    CombatTacticalAdvisoryUpdated?.Invoke(pid, status);
            }
            catch (Exception)
            {
                // Read-only optional diagnostics must not fault the game's
                // packet-processing thread or affect combat execution.
            }
            finally { _tacticalAdvisorRefreshQueued.TryRemove(pid, out byte ignored); }
        });
    }

    /// <summary>
    /// Opt-in, per PID and in-memory. Disabling clears the advisory session but
    /// does not alter the automated combat profile or inject any game message.
    /// </summary>
    public CombatTacticalAdvisoryStatus SetCombatTacticalAdvisoryEnabled(uint pid, bool enabled)
    {
        if (pid == 0)
            return new(pid, false, VerifiedTacticalExecutor.Phase.Disabled, null,
                null, "invalid PID");
        var window = _tacticalAdvisorWindows.GetOrAdd(pid, static _ => new TacticalAdvisorWindow());
        if (enabled) EnsureTacticalAdvisorObservationHooks();
        lock (window.Gate)
        {
            if (window.Enabled != enabled)
            {
                window.Enabled = enabled;
                window.Executor?.SetEnabled(false);
                window.Executor = null;
                window.Plan = null;
                window.Last = null;
                window.Expired = false;
                window.SourceRevoked = false;
                window.MapTicket = null;
                window.FightId = 0;
                window.MapId = 0;
                window.Generation = 0;
                window.Turn = 0;
                window.TurnEpoch = 0;
            }
        }
        return ReadCombatTacticalAdvisoryStatus(pid);
    }

    /// <summary>
    /// Pull facade suitable for a classic desktop status row. It reads the
    /// current fight state on every call, rechecks map ticket, visible targets,
    /// learned grades and resource/turn epochs, then returns only a diagnostic
    /// advisory. It never causes the combat actor to perform an action.
    /// </summary>
    public CombatTacticalAdvisoryStatus ReadCombatTacticalAdvisoryStatus(uint pid)
    {
        if (!_tacticalAdvisorWindows.TryGetValue(pid, out var window))
            return new(pid, false, VerifiedTacticalExecutor.Phase.Disabled, null,
                null, "tactical advisory off");
        lock (window.Gate)
        {
            if (!window.Enabled)
                return new(pid, false, VerifiedTacticalExecutor.Phase.Disabled,
                    null, null, "tactical advisory off");
            if (window.SourceRevoked)
                return window.Last ?? new(pid, true,
                    VerifiedTacticalExecutor.Phase.Expired, null, null,
                    "source transport previous fight expired, opt-in required");
            if (!IsInFight(pid) || CurrentFightId(pid) <= 0)
            {
                // The next fight has its own epoch; do not keep stale advice.
                window.Executor?.SetEnabled(false);
                window.Executor = null;
                window.Plan = null;
                window.Expired = false;
                window.FightId = 0;
                return window.Last = new(pid, true,
                    VerifiedTacticalExecutor.Phase.Pending, null, null, "outside active fight");
            }

            var ingress = ReadCombatComboDiagnosticSnapshot(pid);
            if (ingress is null || ingress.Generation <= 0
                || ingress.Turn <= 0 || ingress.MapId <= 0)
                return window.FightId > 0
                    ? ExpireTacticalWindow(pid, window,
                        "previous fight/turn evidence is no longer available")
                    : window.Last = new(pid, true,
                        VerifiedTacticalExecutor.Phase.Pending, null, null,
                        "waiting for a complete diagnostic fight/turn epoch");
            if (ingress.Truncated)
                return window.FightId > 0
                    ? ExpireTacticalWindow(pid, window,
                        "diagnostic observation stream truncated")
                    : window.Last = new(pid, true,
                        VerifiedTacticalExecutor.Phase.Pending, null, null,
                        "diagnostic history truncated; waiting for new verified fight epoch");
            int turnEpoch = _turnEpoch.GetValueOrDefault(pid);
            var ticket = _combatMapTickets.GetValueOrDefault(pid);
            if (ticket is null)
                return window.FightId > 0
                    ? ExpireTacticalWindow(pid, window, "combat map context missing")
                    : window.Last = new(pid, true,
                        VerifiedTacticalExecutor.Phase.Pending, null, null,
                        "waiting for combat map context");
            bool changed = window.FightId != ingress.FightId
                || window.MapId != ingress.MapId
                || window.Generation != ingress.Generation
                || window.Turn != ingress.Turn
                || window.TurnEpoch != turnEpoch
                || !ReferenceEquals(window.MapTicket, ticket);
            if (window.FightId > 0 && changed)
            {
                // Never reinterpret an old proposed action in a new fight,
                // turn or map. UI gets a visible expired transition first;
                // a later refresh may plan a wholly new, independent candidate.
                window.Executor?.SetEnabled(false);
                window.Executor = null;
                window.Plan = null;
                window.FightId = ingress.FightId;
                window.MapId = ingress.MapId;
                window.Generation = ingress.Generation;
                window.Turn = ingress.Turn;
                window.TurnEpoch = turnEpoch;
                window.MapTicket = ticket;
                window.Expired = false;
                return window.Last = new(pid, true,
                    VerifiedTacticalExecutor.Phase.Expired, null, null,
                    "fight, map or turn epoch changed; previous advice invalidated",
                    ingress.FightId, ingress.MapId, ingress.Turn, ingress.Generation);
            }
            if (window.FightId == 0)
            {
                window.FightId = ingress.FightId;
                window.MapId = ingress.MapId;
                window.Generation = ingress.Generation;
                window.Turn = ingress.Turn;
                window.TurnEpoch = turnEpoch;
                window.MapTicket = ticket;
            }
            if (window.Expired)
                return window.Last ?? new(pid, true,
                    VerifiedTacticalExecutor.Phase.Expired, null, null,
                    "previous advisor context expired", ingress.FightId,
                    ingress.MapId, ingress.Turn, ingress.Generation);

            if (!TryBuildTacticalSnapshot(pid, ingress, ticket, turnEpoch, out var snapshot,
                out var grades, out var reason))
            {
                if (window.Executor is not null)
                    return ExpireTacticalWindow(pid, window,
                        "previous tactical snapshot invalidated: " + reason);
                return window.Last = new(pid, true,
                    VerifiedTacticalExecutor.Phase.Pending, null, null,
                    reason, ingress.FightId, ingress.MapId, ingress.Turn, ingress.Generation);
            }
            var currentTurnStream = ingress.Observations
                .Where(e => e.Turn == ingress.Turn).ToArray();
            var afterCapture = ReadCombatComboDiagnosticSnapshot(pid);
            if (afterCapture is null || afterCapture.FightId != ingress.FightId
                || afterCapture.MapId != ingress.MapId
                || afterCapture.Generation != ingress.Generation
                || afterCapture.Turn != ingress.Turn
                || afterCapture.Truncated
                || afterCapture.Observations.Count != ingress.Observations.Count
                || !ReferenceEquals(ticket, _combatMapTickets.GetValueOrDefault(pid))
                || _turnEpoch.GetValueOrDefault(pid) != turnEpoch)
                return window.Executor is null
                    ? window.Last = new(pid, true, VerifiedTacticalExecutor.Phase.Pending,
                        null, null, "combat changed during snapshot revalidation",
                        ingress.FightId, ingress.MapId, ingress.Turn, ingress.Generation)
                    : ExpireTacticalWindow(pid, window,
                        "combat changed during tactical snapshot revalidation");

            // The live ingress is NOT certified. No false->true conversion:
            // even if a future diagnostic accidentally sets ServerConfirmed,
            // InspectDiagnostic strips it again. No local send is an ACK.
            if (window.Executor is { } current)
            {
                // The ingress cannot prove the absence of other in-flight
                // sends. Use the hypothetical idle flag ONLY for geometric
                // revalidation of an existing UI candidate, never to dispatch.
                if (window.Plan is not { } existingPlan)
                    return ExpireTacticalWindow(pid, window,
                        "tactical plan identity missing");
                var currentGeometry = VerifiedTacticalSequence.Next(
                    existingPlan, snapshot! with { PendingAction = false },
                    currentTurnStream);
                if (!currentGeometry.Ready
                    || currentGeometry.Instruction != window.Last?.Candidate
                    || currentGeometry.Stage != 0)
                    return ExpireTacticalWindow(pid, window,
                        "observed fighter, AP/MP, line of sight or class tactic changed");
                var reviewed = current.InspectDiagnostic(snapshot!,
                    currentTurnStream, ingress.Truncated);
                if (reviewed.State is VerifiedTacticalExecutor.Phase.Expired)
                {
                    window.Expired = true;
                    return window.Last = new(pid, true,
                        reviewed.State, window.Last?.ClassTactic, null,
                        reviewed.Reason, ingress.FightId, ingress.MapId,
                        ingress.Turn, ingress.Generation, reviewed.ConfirmedStages);
                }
                return window.Last = new(pid, true, reviewed.State,
                    window.Last?.ClassTactic, window.Last?.Candidate,
                    "diagnostic ingress lacks transport generation / ACK; "
                    + reviewed.Reason, ingress.FightId, ingress.MapId,
                    ingress.Turn, ingress.Generation, reviewed.ConfirmedStages);
            }

            // New deterministic bounded multiclasse decision path shares the
            // EXACT executor inspected on subsequent observations. The only
            // locally assumed fact is that a hypothetical candidate could be
            // evaluated while idle: the real PendingAction remains unknown.
            // Therefore no call here is a cast/move authorization.
            var projection = snapshot! with { PendingAction = false };
            var frame = new ClassMechanics.ComboFrame(ingress.FightId,
                ingress.Generation, ingress.Turn, ingress.Turn,
                projection.ActorId, projection.BreedId);
            var catalog = grades!.OrderBy(x => x.Key)
                .Select(x => _gameNames.GetSpellDescriptor(x.Key, x.Value))
                .Where(x => x is { Uncertain: false } && !x.Effects.IsDefaultOrEmpty)
                .Select(x => x!)
                .Take(65).ToArray();
            var selection = VerifiedTacticalExecutor.PrepareObservedDecision(
                new VerifiedTacticalExecutor.ObservedDecisionFacts(
                    frame, projection, ClassMechanics.VerifiedClassClientVersion,
                    grades, catalog, MaxCandidateChecks: 4096),
                enabled: true);
            if (selection.Accepted && selection.Executor is { } executor
                && selection.Kind is { } classTactic
                && selection.Preview is { } action)
            {
                window.Executor = executor;
                window.Plan = executor.AdvisoryPlan;
                var reviewed = executor.InspectDiagnostic(snapshot,
                    currentTurnStream, ingress.Truncated);
                return window.Last = new(pid, true, reviewed.State,
                    classTactic, action,
                    "deterministic grade-bound advisory (" + selection.CheckedCandidates
                    + " checks); pending transport and correlated ACK unknown; no dispatch",
                    ingress.FightId, ingress.MapId, ingress.Turn, ingress.Generation);
            }
            return window.Last = new(pid, true,
                VerifiedTacticalExecutor.Phase.Pending, null, null,
                "no admissible observed multiclasse decision: " + selection.Reason,
                ingress.FightId, ingress.MapId, ingress.Turn, ingress.Generation);
        }
    }

    private CombatTacticalAdvisoryStatus ExpireTacticalWindow(uint pid,
        TacticalAdvisorWindow window, string reason)
    {
        window.Executor?.SetEnabled(false);
        window.Executor = null;
        window.Plan = null;
        window.Expired = true;
        return window.Last = new(pid, true, VerifiedTacticalExecutor.Phase.Expired,
            null, null, reason, CurrentFightId(pid));
    }

    private bool TryBuildTacticalSnapshot(uint pid, CombatComboIngressSnapshot ingress,
        object ticket, int turnEpoch,
        out VerifiedTacticalSequence.Snapshot? snapshot,
        out IReadOnlyDictionary<int, int>? learnedGrades, out string reason)
    {
        snapshot = null;
        learnedGrades = null;
        reason = "live combat snapshot incomplete";
        long selfId = CharacterId(pid), fight = CurrentFightId(pid);
        if (!_myTurnOpen.GetValueOrDefault(pid)
            || selfId <= 0 || selfId != SelfActorId(pid)
            || fight != ingress.FightId || _serverMapId.GetValueOrDefault(pid) != ingress.MapId
            || SelfCharacterInfo(pid)?.BreedIndex is not int breedIndex
            // All sequence signatures are pinned to this installed catalog
            // generation; a mutable or unknown local game-data version cannot
            // certify spell ownership/effects for 3.7.13.17.
            || LotMGameVersion != ClassMechanics.VerifiedClassClientVersion
            || !_charSpells.TryGetValue(pid, out var learned)
            || !_charSpellLevels.TryGetValue(pid, out var levels)
            || learned.Count == 0 || levels.Count == 0
            || SelfStats(pid) is not { } stats || stats.Pa < 0 || stats.Pm < 0)
        {
            reason = "turn, authenticated actor, pinned client/catalog version, "
                + "learned grades or AP/MP unavailable";
            return false;
        }
        int breed = ClassIconProvider.BreedId(breedIndex);
        if (breed is not (ClassMechanics.RogueBreedId or ClassMechanics.EliotropeBreedId
            or ClassMechanics.XelorBreedId or ClassMechanics.FecaBreedId
            or ClassMechanics.EcaflipBreedId or ClassMechanics.SacrierBreedId))
        {
            reason = "class has no verified tactical signature";
            return false;
        }

        var (mapId, cells) = LoadFightMap(pid);
        if (mapId != ingress.MapId || cells is null || cells.Length != MapPathfinder.CellCount
            || cells.Any(c => (uint)c.CellId >= MapPathfinder.CellCount)
            || cells.Select(c => c.CellId).Distinct().Count() != MapPathfinder.CellCount)
        {
            reason = "complete fight map and LoS not available";
            return false;
        }
        var mapByCell = cells.ToDictionary(c => c.CellId);
        var floors = new int[MapPathfinder.CellCount];
        var zones = new int[MapPathfinder.CellCount];
        var zoneMoves = new bool[MapPathfinder.CellCount];
        var board = new List<PlannerCell>(MapPathfinder.CellCount);
        foreach (var cell in cells)
        {
            bool walk = cell.Mov && !cell.NonWalkableDuringFight;
            floors[cell.CellId] = walk ? cell.Floor : -1;
            zones[cell.CellId] = walk ? (cell.LinkedZone & 0xF8) >> 4 : -1;
            zoneMoves[cell.CellId] = cell.MoveZone == 1;
            board.Add(new PlannerCell(cell.CellId, walk, !cell.Los,
                ImmutableArray<int>.Empty));
        }
        IReadOnlyList<(int Cell, long ActorId)> roster;
        lock (_fightersLock)
            roster = _fighters.TryGetValue(pid, out var current)
                ? current.ToArray() : Array.Empty<(int Cell, long ActorId)>();
        if (roster.Count == 0 || roster.Count > 64
            || roster.Any(f => f.ActorId == 0 || (uint)f.Cell >= MapPathfinder.CellCount)
            || roster.Select(f => f.ActorId).Distinct().Count() != roster.Count
            || roster.Select(f => f.Cell).Distinct().Count() != roster.Count
            || !roster.Any(f => f.ActorId == selfId))
        {
            reason = "fighter identity or position conflicts";
            return false;
        }
        int selfCell = roster.Single(f => f.ActorId == selfId).Cell;
        var fighterBoard = new List<PlannerFighter>(roster.Count);
        foreach (var f in roster)
        {
            FighterStats? actorStats = _fighterStats.TryGetValue(pid, out var byId)
                && byId.TryGetValue(f.ActorId, out var live) ? live
                : f.ActorId == selfId ? stats : null;
            var vis = _fighterVisibility.GetValueOrDefault(pid)?.Get(f.ActorId);
            fighterBoard.Add(new PlannerFighter(f.ActorId, f.Cell,
                f.ActorId != selfId && IsEnemy(pid, f.ActorId),
                actorStats is { Life: > 0 } ? actorStats.Value.Life : 1,
                actorStats?.MaxLife ?? 0,
                Visibility: f.ActorId == selfId ? FighterVisibility.Visible
                    : vis?.Visibility ?? FighterVisibility.Unknown,
                PositionCertainty: f.ActorId == selfId
                    ? FighterPositionCertainty.Confirmed
                    : vis?.PositionCertainty ?? FighterPositionCertainty.Unknown,
                States: FighterActiveStates(pid, f.ActorId).ToImmutableHashSet(),
                // A stream of state additions is NOT a full positive/negative
                // state snapshot, so do not grant states-known confidence.
                StatesKnown: false));
        }
        if (fighterBoard.Any(f => f.Id != selfId && !f.CanTarget))
        {
            reason = "at least one fighter has unknown visibility or position";
            return false;
        }
        var marks = ForcedMovementUnsafeCells(pid);
        if (marks is null)
        {
            reason = "ground-mark coverage unknown";
            return false;
        }
        if (!ReferenceEquals(ticket, _combatMapTickets.GetValueOrDefault(pid))
            || !IsInFight(pid) || CurrentFightId(pid) != fight
            || _turnEpoch.GetValueOrDefault(pid) != turnEpoch
            || _serverMapId.GetValueOrDefault(pid) != mapId)
        {
            reason = "map or fight changed during snapshot capture";
            return false;
        }
        var learnedSet = learned.ToHashSet();
        learnedGrades = levels.Where(x => x.Value > 0 && learnedSet.Contains(x.Key))
            .ToImmutableDictionary();
        if (learnedGrades.Count == 0)
        {
            reason = "no server-observed learned grades";
            return false;
        }
        snapshot = new VerifiedTacticalSequence.Snapshot(fight,
            ingress.Generation, ingress.Generation, ingress.Turn, selfId, breed,
            selfCell, stats.Pa, stats.Pm,
            ingress.Observations.Count > 0 ? ingress.Observations[^1].Order : 0,
            null, board, fighterBoard, marks,
            Portals(pid, includeInactive: true),
            (a, b) => mapByCell.ContainsKey(a) && mapByCell.ContainsKey(b)
                ? MapPathfinder.Communicates(a, b, floors, zones, zoneMoves) : null)
        { KnownSpellGrades = learnedGrades };
        return true;
    }
}
