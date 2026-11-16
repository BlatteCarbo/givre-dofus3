using System;
using System.Linq;
using System.Threading;
using MoonSharp.Interpreter;
using GivreClone.App.Services.Production;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>
/// A suggested OPERATOR INSPECTION after a single Lua activity. This enum is
/// not a planning/dispatch state machine and cannot grant another send.
/// </summary>
public enum ActivitySupplyContinuityReview
{
    VerifyIdentityAndSource,
    InspectDurableAttempt,
    InspectMapAndRoute,
    InspectPodsAndBank,
    InspectResourceAvailability,
    InspectRemainingSupply,
    InspectProductionReadiness,
    PrepareNewExplicitStep,
    InspectStoppedRun
}

/// <summary>
/// Immutable, durable-marker-backed, read-only inspection for PRIME/production.
/// The selected GID and required quantity are CALLER REQUESTS, never inferred
/// from a cached inventory delta or treated as server-verified objectives.
/// </summary>
public sealed record ActivitySupplyContinuityStatus(
    ActivityRecoveryDurableStatus DurableStatus,
    ActivityRecoveryReconciliationEvidence Evidence,
    ActivitySupplyContinuityReview NextReview,
    ActivitySupplyContinuityReview ConditionalReviewAfterReconciliation,
    long SelectedGid,
    long RequiredQuantity,
    int? BagObserved,
    long? RemainingInBag,
    bool SourceVerified,
    bool PodsVerified,
    bool ResourceSnapshotVerified,
    bool? MatchingResourceAvailable,
    long ObservedMap,
    long ObservedMapGeneration,
    string Explanation)
{
    public bool RequiresIndependentReconciliation => DurableStatus.IsQuarantined;
    public bool MayAutoDispatch => false;
    public bool MayAutoClear => false;
}

/// <summary>
/// V17: exact pending segment and next OPERATOR INSPECTION, independent of a
/// caller-selected GID. The server-visible map, cell, interactive and inventory
/// events do not carry the original ActivityRecovery dispatch identity, and
/// therefore cannot serve as this segment's receipt. This projection is not
/// a queue, a license to resend, or a workflow transition.
/// </summary>
public sealed record ActivityRecoveryCausalNextStepStatus(
    ActivityRecoveryDurableStatus DurableStatus,
    ActivityRecoveryReconciliationEvidence Observation,
    ActivityRecoverySegment? RecordedSegment,
    ActivitySupplyContinuityReview NextReview,
    ActivitySupplyContinuityReview ConditionalReviewAfterIndependentReconciliation,
    bool SourceProvenanceVerified,
    bool PodsVerified,
    bool? PodsAtThreshold,
    long? SourceInventoryRevision,
    long ObservedMap,
    long ObservedGeneration,
    string Explanation)
{
    public bool IsBlocked => DurableStatus.IsQuarantined;
    public bool HasSegmentAttributedServerReceipt => false;
    public bool MayAutoDispatch => false;
    public bool MayAutoClear => false;
}

/// <summary>
/// V18 reason for a particular Lua route/gather run refusing a step. The last
/// reason is scoped to that run's real CTS and generation; stale callbacks
/// must not become the active run's diagnostic.
/// </summary>
public enum ActivityRecoveryTransitionReasonV18
{
    None,
    DurableAttemptPending,
    DurableStatusUnavailable,
    RunInactive,
    RunReplacedDuringInspection,
    MapUnconfirmed,
    RouteUnmatched,
    PodsUnconfirmed,
    PodsAtThreshold,
    ResourceUnavailable,
    ResourceOutOfReach,
    GuidedPlanIncomplete,
    GuidedSourceUnconfirmed
}

/// <summary>
/// Read-only next inspection for route, gather, map, pods and Stop/Start.
/// The archived segment is taken from the durable marker, never synthesized
/// from a new Lua step or a local server-effect observation.
/// </summary>
public sealed record ActivityRecoveryTransitionsV18Status(
    ActivityRecoveryDurableStatus DurableStatus,
    ActivityRecoveryReconciliationEvidence Evidence,
    ActivityRecoverySegment? RecordedSegment,
    ActivityRecoveryTransitionReasonV18 Reason,
    ActivityRecoveryTransitionReasonV18 LastRunBlock,
    ActivitySupplyContinuityReview NextReview,
    ActivitySupplyContinuityReview ConditionalReviewAfterIndependentReconciliation,
    int RunGeneration,
    bool RunActive,
    bool SourceProvenanceVerified,
    bool PodsVerified,
    bool? PodsAtThreshold,
    long ObservedMap,
    long ObservedGeneration,
    string Explanation)
{
    public bool IsQuarantined => DurableStatus.IsQuarantined;
    public bool HasSegmentAttributedServerReceipt => false;
    public bool MayAutoDispatch => false;
    public bool MayAutoClear => false;
    // Separate local world publication from strict link/decoder provenance.
    // These values can explain a pods stop but never grant an action.
    public bool WorldPodsPublicationObserved { get; init; }
    public bool? WorldPodsAtThreshold { get; init; }
}

public sealed partial class GivreLuaHost
{
    private ActivityRecoveryWorkflow? _activityRecovery;
    private int _activityRecoveryRunGen = -1;
    private string? _activityRecoveryLastWarning;
    private ActivityRecoveryScope _activityRecoveryOwner;
    private int _recoveryStuckNotifiedRunGen = -1;
    private ActivityRecoveryDurableStatus _durableRecoverySnapshot =
        new(default, ActivityRecoveryDurableState.IdentityMissing, null, 0, 0,
            "durable status not yet refreshed");
    private ActivityRecoveryReconciliationResult? _reconciliationSnapshot;
    private ActivitySupplyContinuityStatus? _supplyContinuitySnapshot;
    private ActivityRecoveryCausalNextStepStatus? _causalNextStepSnapshot;
    private ActivityRecoveryTransitionsV18Status? _transitionsV18Snapshot;

    private sealed record ActivityRecoveryRunBlockV18(
        ActivityRecoveryTransitionReasonV18 Reason,
        int RunGeneration, CancellationToken Token, long Map, string Detail);
    private ActivityRecoveryRunBlockV18? _runBlockV18;

    private void NoteActivityRecoveryRunBlockV18(
        ActivityRecoveryTransitionReasonV18 reason, CancellationToken ct, string detail)
    {
        // Route/gather can finish after QueueRun published a new CTS. A late
        // callback may report its own log, but must not overwrite the new
        // owner's transition status.
        var current = Volatile.Read(ref _cts);
        if (!ct.CanBeCanceled || ct.IsCancellationRequested ||
            current is null || current.Token != ct) return;
        int generation = Volatile.Read(ref _runGen);
        if (generation <= 0) return;
        var observation = new ActivityRecoveryRunBlockV18(
            reason, generation, ct, _launch.CurrentMap(_pid) ?? 0, detail);
        if (Volatile.Read(ref _runGen) == generation &&
            ReferenceEquals(current, Volatile.Read(ref _cts)) &&
            !ct.IsCancellationRequested)
            Volatile.Write(ref _runBlockV18, observation);
    }

    // Offline integration harness: the real Lua route/gather admissions run
    // unchanged, while test code can capture the would-send boundary without a
    // relay and keep the durable journal under its own disposable directory.
    // Never configured by production; neither hook fabricates a server ACK.
    internal ActivityRecoveryDurableStore? ActivityRecoveryOfflineStoreForTests { get; set; }
    internal Action<ActivityRecoverySegment>? ActivityRecoveryOfflineDispatchForTests { get; set; }
    // Only used when the would-send boundary is intercepted (no packet leaves).
    // Production never sets this; license admission for real sends is unchanged.
    internal bool ActivityRecoveryOfflineGatherAllowedForTests { get; set; }

    private bool CaptureOfflineRecoveryDispatch(ActivityRecoverySegment segment)
    {
        var capture = ActivityRecoveryOfflineDispatchForTests;
        if (capture is null) return false;
        capture(segment);
        return true;
    }

    /// <summary>Read-only diagnostic. No caller can clear the one-shot pending lease.</summary>
    public ActivityRecoveryWorkflow? ActivityRecoveryStatus => Volatile.Read(ref _activityRecovery);

    /// <summary>
    /// Thread-safe read-only PRIME/UI status backed by the durable on-disk marker.
    /// Unlike ActivityRecoveryStatus this survives process/script restarts and pid reuse.
    /// Unknown identity or unavailable storage is reported as quarantined, never clear.
    /// </summary>
    public ActivityRecoveryDurableStatus DurableActivityRecoveryStatus =>
        Volatile.Read(ref _durableRecoverySnapshot);

    /// <summary>UI/PRIME cache only; no disk I/O and NEVER an unlock signal.</summary>
    public ActivityRecoveryReconciliationResult? ActivityRecoveryReconciliationStatus =>
        Volatile.Read(ref _reconciliationSnapshot);

    /// <summary>Cached read-only projection; call RefreshActivitySupplyContinuityStatus
    /// to consult the actual durable marker and current host source explicitly.</summary>
    public ActivitySupplyContinuityStatus? ActivitySupplyContinuityStatus =>
        Volatile.Read(ref _supplyContinuitySnapshot);

    /// <summary>
    /// Cached result for PRIME/VM. Refresh explicitly off the UI thread to
    /// inspect the current checksummed marker and source checkpoint.
    /// The cached projection never participates in gameplay admission.
    /// </summary>
    public ActivityRecoveryCausalNextStepStatus? ActivityRecoveryCausalNextStepStatus =>
        Volatile.Read(ref _causalNextStepSnapshot);

    /// <summary>
    /// Cached V18 operator inspection. No on-disk reads, gameplay sends or
    /// recovery admissions are performed by this getter.
    /// </summary>
    public ActivityRecoveryTransitionsV18Status? ActivityRecoveryTransitionsV18Status =>
        Volatile.Read(ref _transitionsV18Snapshot);

    /// <summary>
    /// Inspect the exact durable marker and last CTS-owned refusal for
    /// map/route/gather/pods/Stop. Refresh off the UI thread. The immediate
    /// review is ALWAYS InspectDurableAttempt when any on-disk marker is
    /// pending/corrupt/busy, even after a successful-looking map transition.
    /// ConditionalReview is only a suggestion after independent reconciliation.
    /// </summary>
    public ActivityRecoveryTransitionsV18Status RefreshActivityRecoveryTransitionsV18Status(
        int maxPods = 95)
    {
        int generation = Volatile.Read(ref _runGen);
        var current = Volatile.Read(ref _cts);
        var causal = RefreshActivityRecoveryCausalNextStepStatus(maxPods);
        var note = Volatile.Read(ref _runBlockV18);
        bool worldPodsObserved = false;
        bool? worldPodsAtThreshold = null;
        if (maxPods is >= 1 and <= 100)
        {
            var state = _world.Get(_pid);
            lock (state.ActorPublicationGate)
            {
                long currentMap = _launch.CurrentMap(_pid) ?? 0;
                if (currentMap > 0 && state.MapId == currentMap &&
                    state.SelfReadyFor(currentMap) &&
                    state.PodsAuthoritative && state.Pods.Max > 0 &&
                    state.Pods.Cur >= 0 && state.Pods.Cur <= state.Pods.Max)
                {
                    worldPodsObserved = true;
                    worldPodsAtThreshold =
                        (long)state.Pods.Cur * 100 >= (long)state.Pods.Max * maxPods;
                }
            }
        }
        bool ownedNote = note is not null && note.RunGeneration == generation &&
            (current is null || current.Token == note.Token);
        var lastBlock = ownedNote ? note!.Reason : ActivityRecoveryTransitionReasonV18.None;
        bool active = current is not null && !current.IsCancellationRequested &&
            Running && generation > 0;
        var conditional = lastBlock switch
        {
            ActivityRecoveryTransitionReasonV18.MapUnconfirmed or
                ActivityRecoveryTransitionReasonV18.RouteUnmatched =>
                ActivitySupplyContinuityReview.InspectMapAndRoute,
            ActivityRecoveryTransitionReasonV18.PodsUnconfirmed or
                ActivityRecoveryTransitionReasonV18.PodsAtThreshold =>
                ActivitySupplyContinuityReview.InspectPodsAndBank,
            ActivityRecoveryTransitionReasonV18.ResourceUnavailable or
                ActivityRecoveryTransitionReasonV18.ResourceOutOfReach =>
                ActivitySupplyContinuityReview.InspectResourceAvailability,
            ActivityRecoveryTransitionReasonV18.GuidedSourceUnconfirmed or
                ActivityRecoveryTransitionReasonV18.GuidedPlanIncomplete =>
                ActivitySupplyContinuityReview.VerifyIdentityAndSource,
            _ when worldPodsAtThreshold == true =>
                ActivitySupplyContinuityReview.InspectPodsAndBank,
            _ when generation > 0 && !worldPodsObserved && !causal.PodsVerified =>
                ActivitySupplyContinuityReview.InspectPodsAndBank,
            _ => causal.ConditionalReviewAfterIndependentReconciliation
        };
        var reason = causal.DurableStatus.State == ActivityRecoveryDurableState.Pending
            ? ActivityRecoveryTransitionReasonV18.DurableAttemptPending
            : causal.DurableStatus.IsQuarantined
                ? ActivityRecoveryTransitionReasonV18.DurableStatusUnavailable
            : lastBlock != ActivityRecoveryTransitionReasonV18.None
                ? lastBlock
                : worldPodsAtThreshold == true
                    ? ActivityRecoveryTransitionReasonV18.PodsAtThreshold
                : generation > 0 && !worldPodsObserved && !causal.PodsVerified
                    ? ActivityRecoveryTransitionReasonV18.PodsUnconfirmed
                : generation > 0 && !active
                    ? ActivityRecoveryTransitionReasonV18.RunInactive
                    : ActivityRecoveryTransitionReasonV18.None;
        var next = causal.DurableStatus.IsQuarantined
            ? causal.NextReview
            : lastBlock != ActivityRecoveryTransitionReasonV18.None
                ? conditional
                : worldPodsAtThreshold == true
                    ? ActivitySupplyContinuityReview.InspectPodsAndBank
                : generation > 0 && !worldPodsObserved && !causal.PodsVerified
                    ? ActivitySupplyContinuityReview.InspectPodsAndBank
                : generation > 0 && !active
                    ? ActivitySupplyContinuityReview.InspectStoppedRun
                    : causal.NextReview;
        var result = new ActivityRecoveryTransitionsV18Status(
            causal.DurableStatus, causal.Observation, causal.RecordedSegment,
            reason, lastBlock, next, conditional, generation, active,
            causal.SourceProvenanceVerified, causal.PodsVerified,
            causal.PodsAtThreshold, causal.ObservedMap,
            causal.ObservedGeneration,
            causal.Explanation + (ownedNote ? " Dernier refus Lua : " + note!.Detail : ""))
        {
            WorldPodsPublicationObserved = worldPodsObserved,
            WorldPodsAtThreshold = worldPodsAtThreshold
        };
        // QueueRun may publish a new CTS before incrementing its generation.
        // A mixed old/new inspection cannot be presented as a valid next step.
        if (Volatile.Read(ref _runGen) != generation ||
            !ReferenceEquals(current, Volatile.Read(ref _cts)))
            result = result with
            {
                Reason = ActivityRecoveryTransitionReasonV18.RunReplacedDuringInspection,
                LastRunBlock = ActivityRecoveryTransitionReasonV18.None,
                NextReview = ActivitySupplyContinuityReview.VerifyIdentityAndSource,
                ConditionalReviewAfterIndependentReconciliation =
                    ActivitySupplyContinuityReview.VerifyIdentityAndSource,
                RunActive = false,
                SourceProvenanceVerified = false,
                PodsVerified = false,
                PodsAtThreshold = null,
                WorldPodsPublicationObserved = false,
                WorldPodsAtThreshold = null,
                Explanation = "Run/CTS remplace pendant lecture : revalider identite et marqueur durable"
            };
        Volatile.Write(ref _transitionsV18Snapshot, result);
        return result;
    }

    /// <summary>
    /// Single read-only operator decision for any activity kind: gather, cell,
    /// door or map transition. The exact segment always comes from the durable
    /// on-disk marker, never a new Lua tick or a caller-supplied target. Even a
    /// stable transport/source observation lacks a segment-correlated server
    /// acknowledgement and cannot release an existing pending record.
    /// </summary>
    public ActivityRecoveryCausalNextStepStatus RefreshActivityRecoveryCausalNextStepStatus(
        int maxPods = 95)
    {
        var reconciliation = RefreshActivityRecoveryReconciliationStatus();
        var durable = reconciliation.DurableStatus;
        var owner = RecoveryIdentity();
        bool validThreshold = maxPods is >= 1 and <= 100;
        bool sourceVerified = false, podsVerified = false;
        bool? podsAtThreshold = null;
        long? inventoryRevision = null;

        if (validThreshold && owner.IsValid && durable.Scope == owner)
        {
            try
            {
                // The linked decoder and inventory revision are rechecked
                // around the inspection. These are provenance witnesses, not
                // request-specific server replies to any archived segment.
                var before = _launch.ReadGuidedSupplySource(_pid);
                var after = _launch.ReadGuidedSupplySource(_pid);
                if (before is { Valid: true } source && before == after &&
                    source.ServerId == owner.ServerId &&
                    source.CharacterId == owner.CharacterId &&
                    source.SourceMap == reconciliation.ObservedMap &&
                    source.SourceMapGeneration == reconciliation.ObservedGeneration &&
                    RecoveryIdentity() == owner)
                {
                    sourceVerified = true;
                    inventoryRevision = source.InventoryRevision;
                    podsVerified = source.UsedPods is >= 0 &&
                        source.CapacityPods is > 0 &&
                        source.UsedPods <= source.CapacityPods;
                    if (podsVerified)
                        podsAtThreshold = (long)source.UsedPods!.Value * 100 >=
                            (long)source.CapacityPods!.Value * maxPods;
                }
            }
            catch (Exception)
            {
                // Failure to inspect a decoder/source is not evidence of an
                // absent durable action, successful harvest or empty inventory.
            }
        }

        var conditional = !validThreshold || !owner.IsValid || durable.Scope != owner
            ? ActivitySupplyContinuityReview.VerifyIdentityAndSource
            : !sourceVerified
                ? ActivitySupplyContinuityReview.VerifyIdentityAndSource
                : !podsVerified || podsAtThreshold == true
                    ? ActivitySupplyContinuityReview.InspectPodsAndBank
                    : reconciliation.Evidence is
                        ActivityRecoveryReconciliationEvidence.MapChangedWithoutAttribution or
                        ActivityRecoveryReconciliationEvidence.GenerationRegressed or
                        ActivityRecoveryReconciliationEvidence.CellTargetObservedWithoutAttribution or
                        ActivityRecoveryReconciliationEvidence.DestinationMapObservedWithoutAttribution
                        ? ActivitySupplyContinuityReview.InspectMapAndRoute
                        : reconciliation.Evidence is
                            ActivityRecoveryReconciliationEvidence.ResourceAbsentWithoutAttribution or
                            ActivityRecoveryReconciliationEvidence.ResourceExhaustedWithoutAttribution or
                            ActivityRecoveryReconciliationEvidence.ResourceStillAvailable or
                            ActivityRecoveryReconciliationEvidence.ResourceDataUnconfirmed
                            ? ActivitySupplyContinuityReview.InspectResourceAvailability
                            : ActivitySupplyContinuityReview.PrepareNewExplicitStep;

        // Pending/corrupt/busy always wins over an apparently completed move,
        // depleted resource, inventory increase, source change or full pods.
        var next = durable.IsQuarantined
            ? owner.IsValid && durable.Scope == owner
                ? ActivitySupplyContinuityReview.InspectDurableAttempt
                : ActivitySupplyContinuityReview.VerifyIdentityAndSource
            : conditional;
        string reason = durable.IsQuarantined
            ? $"Tentative {durable.State}, segment={durable.Segment?.Kind.ToString() ?? "inconnu"} " +
              $"/ cible={durable.Segment?.Target.ToString() ?? "inconnue"}: " +
              $"observation={reconciliation.Evidence}; aucune reponse serveur reliee " +
              "a cette tentative/ce run. Marqueur conserve, aucun renvoi automatique."
            : !validThreshold
                ? "Seuil pods invalide ; aucune recommandation d'action"
                : !sourceVerified
                    ? "Source acteur/session/decoder non attestee pour ce checkpoint"
                    : !podsVerified || podsAtThreshold == true
                        ? "Pods indisponibles ou au seuil ; verifier banque et capacite"
                        : "Aucune tentative durable pour cette identite ; inspection avant toute nouvelle admission";
        var result = new ActivityRecoveryCausalNextStepStatus(durable,
            reconciliation.Evidence, durable.Segment, next, conditional,
            sourceVerified, podsVerified, podsAtThreshold, inventoryRevision,
            reconciliation.ObservedMap, reconciliation.ObservedGeneration, reason);
        // A persona replacement during the final source read cannot publish an
        // apparently clear status for a new owner of the same PID.
        if (RecoveryIdentity() != owner)
            result = new ActivityRecoveryCausalNextStepStatus(
                new(default, ActivityRecoveryDurableState.IdentityMissing, null, 0, 0,
                    "server/character changed during V17 inspection"),
                ActivityRecoveryReconciliationEvidence.IdentityUnconfirmed, null,
                ActivitySupplyContinuityReview.VerifyIdentityAndSource,
                ActivitySupplyContinuityReview.VerifyIdentityAndSource,
                false, false, null, null, 0, 0,
                "Changement de personnage pendant l'inspection ; aucune admission");
        Volatile.Write(ref _causalNextStepSnapshot, result);
        return result;
    }

    /// <summary>
    /// Inspect the REAL fsynced attempt plus the current session-bound
    /// inventory/interactive observations. This is safe after Stop/Start,
    /// map transitions and depleted/absent resources; it NEVER mutates the
    /// durable marker and is never an admission to a second gameplay action.
    /// An optional suggested next review only helps the operator decide what
    /// to inspect AFTER independently resolving any pending attempt.
    /// </summary>
    public ActivitySupplyContinuityStatus RefreshActivitySupplyContinuityStatus(
        long selectedGid, long requiredQuantity, int maxPods = 95)
    {
        var reconciliation = RefreshActivityRecoveryReconciliationStatus();
        var durable = reconciliation.DurableStatus;
        var owner = RecoveryIdentity();
        bool validRequest = selectedGid is > 0 and <= int.MaxValue &&
            requiredQuantity > 0 && maxPods is >= 1 and <= 100;
        int? bag = null;
        long? remaining = null;
        bool sourceVerified = false, podsVerified = false, resourcesVerified = false;
        bool? resourceAvailable = null;
        bool podsFull = false;
        long map = reconciliation.ObservedMap;
        long generation = reconciliation.ObservedGeneration;

        // A guessed count (including zero before an InventoryContentEvent)
        // is NOT an inventory observation. Require one stable host-issued
        // link+decoder+inventory revision around the entire set of reads.
        if (validRequest && owner.IsValid && durable.Scope == owner)
        {
            try
            {
                var before = _launch.ReadGuidedSupplySource(_pid);
                if (before is { Valid: true } source &&
                    source.ServerId == owner.ServerId &&
                    source.CharacterId == owner.CharacterId)
                {
                    int observed = _launch.InventoryCount(_pid, selectedGid);
                    bool decoded = false, available = false;
                    var state = _world.Get(_pid);
                    lock (state.ActorPublicationGate)
                    {
                        decoded = state.MapId == source.SourceMap &&
                            state.MapGeneration == source.SourceMapGeneration &&
                            state.SelfActorId == owner.CharacterId &&
                            state.SelfReadyFor(source.SourceMap) &&
                            _launch.InteractivesDecodedMap(_pid) == source.SourceMap &&
                            _launch.SkillDataLoaded;
                        if (decoded)
                            available = state.Interactives.Any(i => i.Harvestable &&
                                _launch.GatheredItemGid(i) == selectedGid);
                    }
                    var after = _launch.ReadGuidedSupplySource(_pid);
                    if (after == before && RecoveryIdentity() == owner &&
                        observed >= 0)
                    {
                        sourceVerified = true;
                        bag = observed;
                        remaining = Math.Max(0L, requiredQuantity - observed);
                        map = source.SourceMap;
                        generation = source.SourceMapGeneration;
                        podsVerified = source.UsedPods is >= 0 &&
                            source.CapacityPods is > 0;
                        podsFull = podsVerified &&
                            (long)source.UsedPods!.Value * 100 >=
                            (long)source.CapacityPods!.Value * maxPods;
                        resourcesVerified = decoded;
                        if (decoded) resourceAvailable = available;
                    }
                }
            }
            catch (Exception)
            {
                // Read-only source unavailable: withhold the observation.
                // A storage/decoder failure never means the bag is empty.
            }
        }

        // The conditional review is only a read-only preview of what may need
        // inspection once a separate, independently authorized reconciliation
        // is complete. It is not a next gameplay step or proof of success.
        var conditional = !validRequest || !owner.IsValid ||
                          durable.Scope != owner
            ? ActivitySupplyContinuityReview.VerifyIdentityAndSource
            : !sourceVerified
                ? ActivitySupplyContinuityReview.VerifyIdentityAndSource
                : !podsVerified || podsFull
                    ? ActivitySupplyContinuityReview.InspectPodsAndBank
                    : remaining == 0
                        ? ActivitySupplyContinuityReview.InspectProductionReadiness
                        : !resourcesVerified
                            ? ActivitySupplyContinuityReview.InspectResourceAvailability
                            : resourceAvailable == false
                                ? ActivitySupplyContinuityReview.InspectMapAndRoute
                                : ActivitySupplyContinuityReview.PrepareNewExplicitStep;
        // A pending/corrupt/busy marker wins over every later observation.
        // Even apparent inventory sufficiency or a depleted resource cannot
        // prove this exact request was acknowledged.
        var next = durable.IsQuarantined && owner.IsValid &&
                   durable.Scope == owner
            ? ActivitySupplyContinuityReview.InspectDurableAttempt
            : conditional;

        string explanation = durable.IsQuarantined
            ? "Tentative durable non rapprochee : aucun nouveau mouvement, depot ou harvest autorise; " +
              reconciliation.Evidence + " n'est pas un ACK"
            : !validRequest ? "GID, quantite ou seuil de pods invalides"
            : !sourceVerified ? "Source session/decoder/inventaire non attestee"
            : !podsVerified || podsFull ? "Pods ou depot banque a inspecter"
            : remaining == 0 ? "Quantite demandee observee; verifier production independamment"
            : !resourcesVerified ? "Ressources non decodees ou catalogue skill non disponible"
            : resourceAvailable == false ? "Ressource GID non disponible sur la carte attestee"
            : "Deficit observe; une nouvelle admission reste bastionbot a son propre preflight";

        var result = new ActivitySupplyContinuityStatus(durable,
            reconciliation.Evidence, next, conditional, selectedGid, requiredQuantity, bag,
            remaining, sourceVerified, podsVerified, resourcesVerified,
            resourceAvailable, map, generation, explanation);
        Volatile.Write(ref _supplyContinuitySnapshot, result);
        return result;
    }

    /// <summary>
    /// Explicit read-only inspection of the durable pre-send marker against currently
    /// observed server map/cell/interactives. It never clears/replaces the marker.
    /// External callers may inspect after a restart without triggering any gameplay.
    /// </summary>
    public ActivityRecoveryReconciliationResult RefreshActivityRecoveryReconciliationStatus()
    {
        var identity = RecoveryIdentity();
        var store = RecoveryStore();
        var current = _launch.CurrentMap(_pid) ?? 0;
        ActivityRecoveryReconciliationResult result;
        if (!identity.IsValid || store is null)
        {
            var state = !identity.IsValid
                ? ActivityRecoveryDurableState.IdentityMissing
                : ActivityRecoveryDurableState.StorageUnavailable;
            var durable = new ActivityRecoveryDurableStatus(identity, state, null, 0, 0,
                "no verified server/character identity or storage");
            result = new ActivityRecoveryReconciliationResult(durable,
                ActivityRecoveryReconciliationDecision.PendingQuarantine,
                !identity.IsValid
                    ? ActivityRecoveryReconciliationEvidence.IdentityUnconfirmed
                    : ActivityRecoveryReconciliationEvidence.StorageUnavailable,
                durable.Detail, current, 0);
        }
        else
        {
            var recorded = store.ReadStatus(identity);
            var segment = recorded.Segment;
            // The ACTUAL archived target is read from the checksum-verified disk marker,
            // never guessed from this tick's next Lua step.
            var observed = ObserveActivity(segment?.ElementId ?? 0,
                segment?.Cell ?? -1, inReach: false, ct: CancellationToken.None);
            bool decoded = observed.Map.IsValid &&
                _launch.InteractivesDecodedMap(_pid) == observed.Map.MapId;
            result = store.Reconcile(identity, observed, decoded,
                inventorySnapshotAuthoritative: false);
            // Identity may have changed while reading; no cached stale result
            // may be interpreted as an unquarantined state for another character.
            if (RecoveryIdentity() != identity)
            {
                var stale = new ActivityRecoveryDurableStatus(default,
                    ActivityRecoveryDurableState.IdentityMissing, null, 0, 0,
                    "server/character changed during reconciliation");
                result = new ActivityRecoveryReconciliationResult(stale,
                    ActivityRecoveryReconciliationDecision.PendingQuarantine,
                    ActivityRecoveryReconciliationEvidence.IdentityUnconfirmed,
                    stale.Detail, observed.Map.MapId, observed.Map.Generation);
            }
        }
        Volatile.Write(ref _durableRecoverySnapshot, result.DurableStatus);
        Volatile.Write(ref _reconciliationSnapshot, result);
        // Preserve the existing PRIME UI API on process restart: cached
        // ActivityRecoveryStatus must describe the durable quarantine even before
        // this run has attempted a segment. It is NEVER an admission authority.
        if (result.IsQuarantined && identity.IsValid && identity == RecoveryIdentity())
        {
            var live = Volatile.Read(ref _activityRecovery);
            // Reconciliation is read-only and runs again on the next scheduler
            // tick. Do not throw away an in-run observed-effect diagnostic if
            // the SAME immutable durable attempt still owns that workflow.
            if (_activityRecoveryOwner != identity || live is null ||
                !live.IsQuarantined ||
                live.PendingSegment != result.DurableStatus.Segment ||
                live.SourceMap != result.DurableStatus.MapId)
            {
                _activityRecoveryOwner = identity;
                Volatile.Write(ref _activityRecovery,
                    ActivityRecoveryWorkflow.FromDurableStatus(result.DurableStatus));
            }
        }
        return result;
    }

    /// <summary>
    /// Explicit disk refresh for PRIME/UI initialization (not for a frequent UI tick).
    /// The getter above and ActivityRecoveryStatus only perform volatile reads.
    /// Actual gameplay admission never relies on these caches.
    /// </summary>
    public ActivityRecoveryDurableStatus RefreshDurableActivityRecoveryStatus()
    {
        var status = ReadDurableRecoveryStatus();
        Volatile.Write(ref _durableRecoverySnapshot, status);
        if (status.IsQuarantined)
        {
            var identity = status.Scope;
            if (identity == RecoveryIdentity())
            {
                var live = Volatile.Read(ref _activityRecovery);
                if (_activityRecoveryOwner != identity || live is null ||
                    !live.IsQuarantined || live.PendingSegment != status.Segment ||
                    live.SourceMap != status.MapId)
                {
                    _activityRecoveryOwner = identity;
                    Volatile.Write(ref _activityRecovery,
                        ActivityRecoveryWorkflow.FromDurableStatus(status));
                }
            }
        }
        return status;
    }

    private ActivityRecoveryScope RecoveryIdentity()
    {
        int server = _launch.ServerIdOf(_pid);
        long character = _launch.CharacterId(_pid);
        if (server <= 0 || character <= 0) return default;
        var st = _world.Get(_pid);
        lock (st.ActorPublicationGate)
        {
            // A reused PID or a disconnected map can retain an old characterId.
            // Require the authoritative self actor to be present on THIS confirmed map.
            if (st.MapId <= 0 || st.MapId != _launch.CurrentMap(_pid) ||
                !st.SelfReadyFor(st.MapId) ||
                st.SelfActorId != character ||
                !st.Actors.Any(a => a.Kind == MapActorKind.Player &&
                                    a.ActorId == character && a.CellId == st.SelfCell))
                return default;
        }
        if (server != _launch.ServerIdOf(_pid) || character != _launch.CharacterId(_pid))
            return default;
        return new ActivityRecoveryScope(server, character);
    }

    private ActivityRecoveryDurableStore? RecoveryStore()
    {
        if (ActivityRecoveryOfflineStoreForTests is { } offline) return offline;
        try { return new ActivityRecoveryDurableStore(ActivityRecoveryDurableStore.DefaultRoot()); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
            System.IO.IOException or UnauthorizedAccessException)
        {
            return null; // missing/unusable root -> no admission
        }
    }

    private ActivityRecoveryDurableStatus ReadDurableRecoveryStatus()
    {
        var identity = RecoveryIdentity();
        if (!identity.IsValid)
            return new(identity, ActivityRecoveryDurableState.IdentityMissing, null, 0, 0,
                "no authoritative server/character identity");
        var store = RecoveryStore();
        return store?.ReadStatus(identity) ??
            new ActivityRecoveryDurableStatus(identity, ActivityRecoveryDurableState.StorageUnavailable,
                null, 0, 0, "durable recovery root unavailable");
    }

    // Lua opt-in: ACTIVITY_RECOVERY = true (for imperative gather) or
    // move(): return { { map = ..., activityRecovery = true, cell = 240, gather = true, door = 100 } }
    private bool ActivityRecoveryGlobalEnabled =>
        _script?.Globals.Get("ACTIVITY_RECOVERY").CastToBool() == true;

    // Read-only Lua opt-in: report the archived marker versus current server
    // checkpoint; never issue movement, gather or interact on this tick.
    private bool ActivityRecoveryReconcileGlobalEnabled =>
        _script?.Globals.Get("ACTIVITY_RECOVERY_RECONCILE").CastToBool() == true;

    // V14 guided supply opt-in from the persisted script: every field is a
    // check against independently stored, read-only production/source data,
    // never an authority to clear ActivityRecovery or to approve another send.
    // A table-only ITEMS_TO_GATHER script with no matching metadata is refused
    // rather than being allowed to harvest an arbitrary stale objective.
    private bool GuidedSupplyIntent
    {
        get
        {
            if (_script is not { } script) return false;
            if (script.Globals.Get("GUIDED_SUPPLY_GID").Type != DataType.Nil ||
                script.Globals.Get("GUIDED_SUPPLY_REQUIRED").Type != DataType.Nil ||
                script.Globals.Get("GUIDED_SUPPLY_GOAL_HASH").Type != DataType.Nil ||
                script.Globals.Get("GUIDED_SUPPLY_REQUEST_HASH").Type != DataType.Nil ||
                script.Globals.Get("GUIDED_SUPPLY_PLAN_HASH").Type != DataType.Nil ||
                script.Globals.Get("GUIDED_SUPPLY_SOURCE_KEY").Type != DataType.Nil ||
                script.Globals.Get("GUIDED_SUPPLY_SESSION_KEY").Type != DataType.Nil ||
                script.Globals.Get("GUIDED_SUPPLY_OBS_REV").Type != DataType.Nil)
                return true;
            return script.Globals.Get("ACTIVITY_RECOVERY_TABLE_ONLY").CastToBool() &&
                script.Globals.Get("ITEMS_TO_GATHER") is { Type: DataType.Table } items &&
                items.Table.Values.Any();
        }
    }

    private static bool GuidedSupplyInteger(DynValue value, out long integer)
    {
        integer = 0;
        if (value.Type != DataType.Number || !double.IsFinite(value.Number) ||
            value.Number < 1 || value.Number > 9007199254740991d ||
            value.Number != Math.Truncate(value.Number)) return false;
        integer = (long)value.Number;
        return integer > 0;
    }

    private static bool GuidedSupplyHash(DynValue value, out string hash)
    {
        hash = "";
        if (value.Type != DataType.String || value.String is not { Length: 64 } text ||
            text.Any(c => !Uri.IsHexDigit(c)))
            return false;
        hash = text.ToUpperInvariant();
        return true;
    }

    private static bool GuidedSupplyDecimal(DynValue value, out long integer)
    {
        integer = 0;
        return value.Type == DataType.String &&
            value.String is { Length: > 0 and <= 19 } number &&
            long.TryParse(number, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out integer) &&
            integer > 0 &&
            number == integer.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private readonly record struct GuidedSupplyClaim(
        long Gid, long Required, long Revision, string GoalHash,
        string RequestHash, string PlanHash, string SourceHash, string SessionHash);

    // Reuse this exact parser both at route entry (before resource filtering)
    // and at the final single-segment admission. A script missing its revision
    // must be refused as an incomplete plan, even on an empty/depleted map.
    private static bool TryReadGuidedSupplyClaim(Script script, out GuidedSupplyClaim claim)
    {
        claim = default;
        if (!script.Globals.Get("ACTIVITY_RECOVERY_TABLE_ONLY").CastToBool() ||
            !GuidedSupplyInteger(script.Globals.Get("GUIDED_SUPPLY_GID"), out long gid) ||
            gid > int.MaxValue ||
            !GuidedSupplyDecimal(script.Globals.Get("GUIDED_SUPPLY_REQUIRED"), out long required) ||
            !GuidedSupplyDecimal(script.Globals.Get("GUIDED_SUPPLY_OBS_REV"), out long revision) ||
            !GuidedSupplyHash(script.Globals.Get("GUIDED_SUPPLY_GOAL_HASH"), out string goalHash) ||
            !GuidedSupplyHash(script.Globals.Get("GUIDED_SUPPLY_REQUEST_HASH"), out string requestHash) ||
            !GuidedSupplyHash(script.Globals.Get("GUIDED_SUPPLY_PLAN_HASH"), out string planHash) ||
            !GuidedSupplyHash(script.Globals.Get("GUIDED_SUPPLY_SOURCE_KEY"), out string sourceHash) ||
            !GuidedSupplyHash(script.Globals.Get("GUIDED_SUPPLY_SESSION_KEY"), out string sessionHash) ||
            script.Globals.Get("ITEMS_TO_GATHER") is not { Type: DataType.Table } items ||
            items.Table.Values.Count() != 1 ||
            !GuidedSupplyInteger(items.Table.Values.Single(), out long requestedGid) ||
            requestedGid != gid)
            return false;
        claim = new(gid, required, revision, goalHash, requestHash,
            planHash, sourceHash, sessionHash);
        return true;
    }

    // Route-level refusal before GatherOne filters candidates. Without this
    // check a guided script with a cached bag deficit but missing link/decoder
    // could simply see no candidate on the current map, stop, and never
    // report WHY the required source checkpoint was unavailable. Final
    // GuidedSupplyCanDispatch still validates the full persisted goal/ledger.
    private bool GuidedSupplyRoutePreflight(long map, long mapGeneration,
        CancellationToken ct)
    {
        if (!GuidedSupplyIntent) return true;
        if (_script is not { } script ||
            !TryReadGuidedSupplyClaim(script, out var claim))
        {
            NoteActivityRecoveryRunBlockV18(
                ActivityRecoveryTransitionReasonV18.GuidedPlanIncomplete, ct,
                "metadonnees de l'approvisionnement guide incomplètes");
            RecoveryWarning("approvisionnement GID/objectif/quantite/plan incomplet ou non confirme ; aucun envoi");
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        try
        {
            var owner = RecoveryIdentity();
            var source = _launch.ReadGuidedSupplySource(_pid);
            if (owner.IsValid && source is { Valid: true } observed &&
                observed.ServerId == owner.ServerId &&
                observed.CharacterId == owner.CharacterId &&
                observed.SourceMap == map &&
                observed.SourceMapGeneration == mapGeneration &&
                observed.InventoryRevision == claim.Revision &&
                GuidedSupplyPlannerV13.SourceKey(observed) == claim.SourceHash &&
                GuidedSupplyPlannerV13.SessionKey(observed) == claim.SessionHash &&
                RecoveryIdentity() == owner && !ct.IsCancellationRequested)
                return true;
        }
        catch (Exception)
        {
            // Missing/locked source is a refusal, never a recovered receipt.
        }
        NoteActivityRecoveryRunBlockV18(
            ActivityRecoveryTransitionReasonV18.GuidedSourceUnconfirmed, ct,
            "source de lien/decoder et revision non attestees");
        RecoveryWarning("approvisionnement : source lien/decoder/revision non attestee ; " +
            "deficit de sac local insuffisant pour toute admission");
        StopRecoveryPlanningOnce(ct);
        return false;
    }

    private bool GuidedSupplyCanDispatch(ActivityRecoverySegment segment,
        ActivityRecoverySnapshot snapshot, CancellationToken ct)
    {
        if (!GuidedSupplyIntent) return true;
        if (_script is not { } script ||
            segment.Kind != ActivityRecoveryKind.Gather || ct.IsCancellationRequested ||
            !TryReadGuidedSupplyClaim(script, out var claim) ||
            snapshot.StockGid != claim.Gid ||
            !snapshot.ResourcePresent || !snapshot.ResourceAvailable ||
            snapshot.Map.MapId <= 0 || snapshot.Map.Generation <= 0)
        {
            RecoveryWarning("approvisionnement GID/objectif/quantite/plan incomplet ou non confirme ; aucun envoi");
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        var identity = RecoveryIdentity();
        if (!identity.IsValid)
        {
            RecoveryWarning("approvisionnement : identite non attestee ; aucun envoi");
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        try
        {
            long gid = claim.Gid, required = claim.Required, revision = claim.Revision;
            string goalHash = claim.GoalHash, requestHash = claim.RequestHash;
            string planHash = claim.PlanHash, sourceHash = claim.SourceHash;
            string sessionHash = claim.SessionHash;
            var before = _launch.ReadGuidedSupplySource(_pid);
            var objective = new GuidedActivityProductionStore(
                GuidedActivityProductionStore.DefaultRoot())
                .Read(identity.ServerId, identity.CharacterId);
            var workflow = new GuidedProductionWorkflowLedger(
                GuidedProductionWorkflowLedger.DefaultRoot())
                .Read(identity.ServerId, identity.CharacterId);
            int bag = _launch.InventoryCount(_pid, gid);
            var after = _launch.ReadGuidedSupplySource(_pid);
            bool valid = before is { Valid: true } source && source == after &&
                source.ServerId == identity.ServerId &&
                source.CharacterId == identity.CharacterId &&
                source.SourceMap == snapshot.Map.MapId &&
                source.SourceMapGeneration == snapshot.Map.Generation &&
                source.InventoryRevision == revision &&
                source.UsedPods is int used && source.CapacityPods is int max &&
                (long)used * 100 < (long)max * Math.Clamp(
                    ReadInt(script, "MAX_PODS", 95), 1, 100) &&
                objective is { State: GuidedActivityProductionState.Ready,
                    Goal: { Gather: true } goal } &&
                GuidedProductionWorkflowLedger.Fingerprint(goal) == goalHash &&
                // Goal.SourceMap/SourceMapGeneration identify the IMMUTABLE
                // map where the operator created the objective. A legitimate
                // route may have reached another map before gathering. The
                // CURRENT map/generation are authenticated by source and
                // snapshot above, and by the persisted V13 source/session
                // checkpoint below; never conflate them with the old origin.
                goal.MaxPods == ReadInt(script, "MAX_PODS", 95) &&
                workflow.Phase == GuidedProductionWorkflowPhase.Blocked &&
                workflow.SupplyPhase == GuidedSupplyPlanPhaseV13.Blocked &&
                workflow.GoalHash == goalHash &&
                workflow.RequestHash == requestHash &&
                workflow.SupplyPlanHash == planHash &&
                workflow.SupplyObservationRevision == revision &&
                workflow.SupplySourceKey == sourceHash &&
                workflow.SupplySessionKey == sessionHash &&
                sourceHash == GuidedSupplyPlannerV13.SourceKey(source) &&
                sessionHash == GuidedSupplyPlannerV13.SessionKey(source) &&
                bag >= 0 && bag < required && snapshot.StockCount == bag &&
                RecoveryIdentity() == identity;
            if (valid) return true;
        }
        catch (Exception)
        {
            // A missing/inaccessible source, goal, session or disk ledger can
            // only withhold admission. No receipt is inferred from exceptions.
        }
        RecoveryWarning("approvisionnement : deficit GID, revision, session ou checkpoint perime ; aucun envoi");
        StopRecoveryPlanningOnce(ct);
        return false;
    }

    private void InspectActivityRecoveryFromLua(CancellationToken ownerToken)
    {
        var result = RefreshActivityRecoveryReconciliationStatus();
        RecoveryWarning($"reconciliation {result.Decision} / {result.Evidence}: " +
                        $"{result.Explanation}; aucun deblocage ni ACK fabrique");
        if (result.IsQuarantined) StopRecoveryPlanningOnce(ownerToken);
    }

    private ActivityRecoveryWorkflow RecoveryForMap(long map)
    {
        var owner = RecoveryIdentity();
        // In-memory quarantine must not be erased just because Start() advanced _runGen.
        // The durable marker below is authoritative even after a full process restart.
        if (_activityRecovery is { IsQuarantined: true } && owner == _activityRecoveryOwner &&
            owner.IsValid)
            return _activityRecovery;
        if (_activityRecovery is null || owner != _activityRecoveryOwner ||
            _activityRecoveryRunGen != _runGen ||
            (!_activityRecovery.IsQuarantined && _activityRecovery.SourceMap != map))
        {
            _activityRecovery = ActivityRecoveryWorkflow.Start(map);
            _activityRecoveryRunGen = _runGen;
            _activityRecoveryOwner = owner;
            _activityRecoveryLastWarning = null;
        }
        return _activityRecovery;
    }

    private void RecoveryWarning(string message)
    {
        if (_activityRecoveryLastWarning == message) return;
        _activityRecoveryLastWarning = message;
        Warn("trajet", "ActivityRecovery : " + message);
    }

    /// <summary>
    /// A pending one-shot recovery lease blocks ALL future route steps, including steps not
    /// opted in. Stopping and manually restarting the script is the only safe re-arm while
    /// the existing Lua transport does not expose a sequence-correlated completion.
    /// </summary>
    private bool RecoveryBlocksRoute(bool checkDurable = false,
        CancellationToken ownerToken = default)
    {
        bool optIn = checkDurable || ActivityRecoveryGlobalEnabled ||
            (_activityRecoveryRunGen == _runGen && _activityRecovery is not null);
        if (optIn)
        {
            var reconciliation = RefreshActivityRecoveryReconciliationStatus();
            var durable = reconciliation.DurableStatus;
            if (durable.IsQuarantined)
            {
                RecoveryWarning($"admission durable bloquee ({durable.State}, " +
                    $"reconciliation={reconciliation.Evidence}); aucune action");
                StopRecoveryPlanningOnce(ownerToken);
                return true;
            }
        }
        // Legacy callers may skip the disk probe by default; all Lua route,
        // gather and RunLoop send entrypoints request it explicitly.
        if (!optIn && _activityRecoveryRunGen != _runGen) return false;
        if (_activityRecoveryOwner != RecoveryIdentity() ||
            _activityRecovery?.IsQuarantined != true) return false;
        RecoveryWarning("segment deja declenche ; sequence ACK non attribuable. " +
                        "Aucun second envoi, trajet suspendu (reprise explicite necessaire).");
        StopRecoveryPlanningOnce(ownerToken);
        return true;
    }

    // Scheduler observes StuckStopped. A durable denied admission must PAUSE the
    // planning instead of polling/restarting the same ambiguous request indefinitely.
    // StuckStop already skips disconnected sessions, canceled CTS and foreign runs.
    private void StopRecoveryPlanningOnce(CancellationToken ownerToken = default)
    {
        if (LinkSuspended) return;
        var currentRun = Volatile.Read(ref _cts);
        if (currentRun is null || currentRun.IsCancellationRequested) return;
        // QueueRun publishes a NEW CTS before the prior Lua task has unwound.
        // A caller without its own token may read that NEW CTS and cancel it:
        // the resulting quarantine is still read-only, but scheduling must
        // await an exact owner (the RunLoop caller supplies its ct).
        if (!ownerToken.CanBeCanceled || currentRun.Token != ownerToken)
            return;
        if (CurrentRuntime is { } runtime && runtime.Token != ownerToken)
            return;
        int generation = Volatile.Read(ref _runGen);
        if (!ReferenceEquals(currentRun, Volatile.Read(ref _cts))) return;
        // Multiple read-only reconcilers may discover the same pending marker
        // concurrently. Claim the notification ONCE for this exact generation;
        // a plain read/assignment allowed two observers to both pause planning.
        int prior;
        do
        {
            prior = Volatile.Read(ref _recoveryStuckNotifiedRunGen);
            if (prior == generation) return;
        }
        while (Interlocked.CompareExchange(ref _recoveryStuckNotifiedRunGen,
                   generation, prior) != prior);
        // StopRun, unlike Stop, is scoped to this exact CTS. A late callback
        // can no longer cancel an unrelated Start even across the final race.
        StopRun(currentRun.Token, "stuck");
        if (Volatile.Read(ref _runGen) == generation &&
            Volatile.Read(ref _cts) is null)
            StuckStopped?.Invoke();
    }

    /// <summary>
    /// Best-effort atomic snapshot for map/cell/actor fields. Resource and inventory
    /// information is an observation, never proof that a particular dispatch was ACKed.
    /// </summary>
    private ActivityRecoverySnapshot ObserveActivity(int elementId, int cell, bool inReach,
        CancellationToken ct)
    {
        var st = _world.Get(_pid);
        lock (st.ActorPublicationGate)
        {
            long map = _launch.CurrentMap(_pid) ?? 0;
            var resource = elementId > 0
                ? st.Interactives.FirstOrDefault(i => i.ElementId == elementId && i.CellId == cell)
                : null;
            int gid = resource is not null ? _launch.GatheredItemGid(resource) : 0;
            int? stock = gid > 0 ? _launch.InventoryCount(_pid, gid) : null;
            var skill = resource?.HarvestSkill;
            // The transport may have advanced to a new map while the world
            // reducer still publishes the previous map's self/interactives.
            // A valid-looking previous cell must never authorize a new send.
            bool mapCoherent = map > 0 && st.MapId == map;
            return new ActivityRecoverySnapshot(
                new ResumableRouteMap(map, Interlocked.Read(ref st.MapGeneration)),
                st.CellsMap, st.SelfCellMap, st.SelfCell, st.SelfReadyFor(map),
                ct.IsCancellationRequested, !mapCoherent || LinkSuspended || _launch.IsInFight(_pid),
                mapCoherent && resource is not null,
                mapCoherent && resource?.Harvestable == true, inReach && mapCoherent,
                gid > 0 ? gid : null, stock, resource?.ElementTypeId,
                skill?.SkillInstanceUid, skill?.SkillId);
        }
    }

    // Pods were previously checked only on entry to RunRouteActivityRecovery.
    // A change during resource decoding, path selection or disk admission could
    // otherwise lead to a subsequent one-shot cell/map send with full pods.
    // This validates a fresh server-derived pods publication at every boundary.
    // It remains a refusal check, never a receipt or replay authorization.
    private bool RecoveryPodsSafe(CancellationToken ct)
    {
        if (ct.IsCancellationRequested || _script is not { } script) return false;
        int threshold = ReadInt(script, "MAX_PODS", 95);
        if (threshold is < 1 or > 100) return false;
        var state = _world.Get(_pid);
        lock (state.ActorPublicationGate)
        {
            long map = _launch.CurrentMap(_pid) ?? 0;
            return map > 0 && state.MapId == map && state.SelfReadyFor(map) &&
                state.PodsAuthoritative && state.Pods.Max > 0 &&
                state.Pods.Cur >= 0 && state.Pods.Cur <= state.Pods.Max &&
                (long)state.Pods.Cur * 100 < (long)state.Pods.Max * threshold;
        }
    }

    // Denial-only diagnostic: the caller has ALREADY refused the one-shot
    // admission. A second read cannot make that denied send admissible, but
    // distinguishes a genuinely observed full bag from missing authority.
    private string RecoveryPodsRefusalReason()
    {
        if (_script is not { } script)
            return "pods non confirmes : script interrompu";
        int threshold = ReadInt(script, "MAX_PODS", 95);
        if (threshold is < 1 or > 100)
            return "seuil pods invalide ; banque automatique non admise";
        var state = _world.Get(_pid);
        lock (state.ActorPublicationGate)
        {
            long map = _launch.CurrentMap(_pid) ?? 0;
            if (map <= 0 || state.MapId != map || !state.SelfReadyFor(map))
                return "source carte/personnage changee : pods non confirmes";
            if (!state.PodsAuthoritative || state.Pods.Max <= 0 ||
                state.Pods.Cur < 0 || state.Pods.Cur > state.Pods.Max)
                return "pods non confirmes : publication serveur absente ou incoherente";
            if ((long)state.Pods.Cur * 100 >= (long)state.Pods.Max * threshold)
                return "pods au seuil observe ; banque automatique non admise dans cette etape";
        }
        return "pods/source remplaces pendant admission ; aucun envoi";
    }

    // The range decision made while choosing a harvest/door target can expire
    // while we wait for map/self data. Check it again at the durable admission
    // boundary, using the exact interactive skill and authoritative source cell.
    // This is a refusal gate; it never turns movement into a gather or an ACK.
    private bool RecoveryInteractiveInReach(ActivityRecoverySegment segment,
        ActivityRecoverySnapshot snapshot)
    {
        if (segment.Kind is not (ActivityRecoveryKind.Gather or ActivityRecoveryKind.Interaction))
            return true;
        long map = snapshot.Map.MapId;
        if (map <= 0 || _launch.CurrentMap(_pid) != map ||
            _launch.InteractivesDecodedMap(_pid) != map ||
            snapshot.ServerCell is not int source || !snapshot.PositionConfirmed ||
            snapshot.SkillInstanceUid is not > 0 || snapshot.SkillId is not > 0)
            return false;
        var st = _world.Get(_pid);
        var interactive = st.Interactives.FirstOrDefault(i =>
            i.ElementId == segment.ElementId && i.CellId == segment.Cell);
        var skill = interactive?.HarvestSkill;
        if (interactive is null || !interactive.Harvestable || skill is null ||
            interactive.ElementTypeId != snapshot.ResourceTypeId ||
            skill.SkillInstanceUid != snapshot.SkillInstanceUid ||
            skill.SkillId != snapshot.SkillId || !st.SelfReadyFor(map) ||
            st.SelfCell != source)
            return false;
        int? stand;
        if (segment.Kind == ActivityRecoveryKind.Gather)
            stand = _launch.HarvestStandCell(_pid, map, source, interactive,
                st.IsWalkableForScript, st.StepAllowed, out _);
        else
            stand = _launch.InteractiveStandCell(_pid, map, source, interactive, skill,
                st.IsWalkableForScript, st.StepAllowed, out _);
        return stand == source;
    }

    // A lease is owned by the Lua generation, actual cancellation token,
    // confirmed server persona and exact workflow reference. Late callbacks
    // from a prior script must not rewrite a replacement run's UI status.
    private readonly record struct RecoveryDispatchLease(
        int RunGeneration, CancellationToken Token, ActivityRecoveryScope Owner,
        ActivityRecoveryWorkflow Workflow, ActivityRecoverySegment Segment,
        ActivityRecoverySnapshot PreSend);

    private bool RecoveryMaySend(RecoveryDispatchLease lease, CancellationToken ct)
    {
        if (ct != lease.Token || ct.IsCancellationRequested ||
            Volatile.Read(ref _runGen) != lease.RunGeneration ||
            (ct != CancellationToken.None && Volatile.Read(ref _cts)?.Token != ct) ||
            _activityRecoveryOwner != lease.Owner ||
            !ReferenceEquals(Volatile.Read(ref _activityRecovery), lease.Workflow) ||
            RecoveryIdentity() != lease.Owner ||
            !RecoveryPodsSafe(ct) ||
            ObserveActivity(lease.Segment.ElementId, lease.Segment.Cell,
                lease.PreSend.AlreadyInReach, ct) != lease.PreSend)
            return false;
        return RecoveryInteractiveInReach(lease.Segment, lease.PreSend) &&
            GuidedSupplyCanDispatch(lease.Segment, lease.PreSend, ct);
    }

    private bool ReserveRecovery(ActivityRecoverySegment segment, CancellationToken ct,
        out RecoveryDispatchLease lease,
        bool inReach = false, int? requiredSource = null,
        Func<int, bool>? walkable = null, Func<int, int, bool>? stepAllowed = null)
    {
        lease = default;
        int generation = Volatile.Read(ref _runGen);
        if (ct.IsCancellationRequested ||
            (ct != CancellationToken.None && Volatile.Read(ref _cts)?.Token != ct))
        {
            RecoveryWarning("script arrete/remplace avant admission ; aucun envoi");
            return false;
        }
        var before = ObserveActivity(segment.ElementId, segment.Cell, inReach, ct);
        if (requiredSource is int original && before.ServerCell != original)
        {
            RecoveryWarning("position changee depuis calcul de portee : aucun envoi");
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        if (RecoveryBlocksRoute(checkDurable: true, ownerToken: ct)) return false;
        if (!RecoveryPodsSafe(ct))
        {
            RecoveryWarning("pods non confirmes, invalides ou au seuil avant admission durable");
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        if (!GuidedSupplyCanDispatch(segment, before, ct)) return false;
        var identity = RecoveryIdentity();
        if (!identity.IsValid)
        {
            RecoveryWarning("identite serveur/personnage non confirmee ; aucun envoi");
            return false;
        }
        var workflow = RecoveryForMap(before.Map.MapId);
        var probe = workflow.Evaluate(segment, before, walkable, stepAllowed);
        if (!probe.CanAttempt)
        {
            RecoveryWarning($"segment {segment.Kind} cible={segment.Target} refuse : {probe.Decision}");
            // In strict table-only mode a failed path/geometry/target is not a
            // retryable Lua loop. Repeated admission probes cannot make a
            // blocked waypoint valid and must not spin or auto-restart it.
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        // The lease is committed BEFORE calling any existing send primitive.
        // Fresh publication or changed obstacles invalidate the preflight rather than
        // reusing a stale map/position. No artificial ACK sequence is ever supplied.
        var immediatelyBefore = ObserveActivity(segment.ElementId, segment.Cell, inReach, ct);
        if (requiredSource is int source && immediatelyBefore.ServerCell != source)
        {
            RecoveryWarning("position modifiee avant envoi : aucun envoi");
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        if (!RecoveryInteractiveInReach(segment, immediatelyBefore) ||
            !GuidedSupplyCanDispatch(segment, immediatelyBefore, ct))
        {
            RecoveryWarning("ressource/porte ou skill plus attestes a portee : aucun envoi");
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        var reserved = workflow.Begin(probe, immediatelyBefore, walkable: walkable,
            stepAllowed: stepAllowed);
        if (ReferenceEquals(reserved, workflow))
        {
            RecoveryWarning($"segment {segment.Kind} invalide juste avant envoi (snapshot change)");
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        // Durable write-ahead admission is the LAST gate before invoking gameplay.
        // If fsync, exclusive CreateNew, or identity verification fails, no dispatch.
        if (RecoveryIdentity() != identity)
        {
            RecoveryWarning("identite changee avant reservation durable ; aucun envoi");
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        var store = RecoveryStore();
        if (store is null)
        {
            RecoveryWarning("dossier durable indisponible ; aucun envoi");
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        var admission = store.TryReserve(identity, segment, immediatelyBefore);
        Volatile.Write(ref _durableRecoverySnapshot, admission.Status);
        Volatile.Write(ref _reconciliationSnapshot, null);
        if (!admission.MayDispatch)
        {
            if (admission.Status.IsQuarantined)
                Volatile.Write(ref _activityRecovery,
                    ActivityRecoveryWorkflow.FromDurableStatus(admission.Status));
            var recon = RefreshActivityRecoveryReconciliationStatus();
            RecoveryWarning($"reservation durable refusee : {admission.Status.State} " +
                $"({recon.Evidence}; {recon.Explanation})");
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        _activityRecovery = reserved;
        _activityRecoveryOwner = identity;
        _activityRecoveryLastWarning = null;
        // A crash or identity/map race AFTER fsync leaves a persistent quarantine, not
        // an accidentally cleared lease. Conservatively refuse even a first send.
        if (RecoveryIdentity() != identity ||
            ObserveActivity(segment.ElementId, segment.Cell, inReach, ct) != immediatelyBefore ||
            !RecoveryPodsSafe(ct) ||
            !RecoveryInteractiveInReach(segment, immediatelyBefore) ||
            !GuidedSupplyCanDispatch(segment, immediatelyBefore, ct) ||
            ct.IsCancellationRequested ||
            Volatile.Read(ref _runGen) != generation ||
            (ct != CancellationToken.None && Volatile.Read(ref _cts)?.Token != ct))
        {
            RecoveryWarning("contexte modifie apres fsync ; reservation preservee, envoi annule");
            StopRecoveryPlanningOnce(ct);
            return false;
        }
        lease = new RecoveryDispatchLease(generation, ct, identity,
            reserved, segment, immediatelyBefore);
        return true;
    }

    private void RecordRecoveryEffect(int elementId, int cell, CancellationToken ct,
        RecoveryDispatchLease lease)
    {
        // An old finally/late completion must never scribble over a new run.
        // It may still have produced a genuine effect; durable quarantine
        // remains authoritative and is intentionally never cleared here.
        if (ct.IsCancellationRequested ||
            (ct != CancellationToken.None && Volatile.Read(ref _cts)?.Token != ct) ||
            Volatile.Read(ref _runGen) != lease.RunGeneration ||
            _activityRecoveryOwner != lease.Owner ||
            !ReferenceEquals(Volatile.Read(ref _activityRecovery), lease.Workflow) ||
            RecoveryIdentity() != lease.Owner) return;
        var observed = lease.Workflow.ObserveEffect(ObserveActivity(elementId, cell,
            inReach: false, ct: ct));
        // A new run can replace the workflow while the observation is being
        // computed. Never let this late finally overwrite its new owner/status.
        if (!ReferenceEquals(Interlocked.CompareExchange(
                ref _activityRecovery, observed, lease.Workflow), lease.Workflow) ||
            Volatile.Read(ref _runGen) != lease.RunGeneration ||
            ct.IsCancellationRequested)
            return;
        RecoveryWarning(observed.ObservedEffectWithoutAck
            ? "effet observable, mais aucun ACK correlable a la sequence ; nouveau dispatch interdit"
            : "issue non corroboree par une sequence ACK ; nouveau dispatch interdit");
    }

    /// <summary>
    /// Opt-in Lua route activity. Only validated single segments are supported:
    /// gather in range (no walk), cell, door already in range (no walk), or direct
    /// cardinal map exit from a server-confirmed edge cell. Unsupported
    /// complex operations fail closed, not silently switched to legacy retries.
    /// </summary>
    private void RunRouteActivityRecovery(Table step, CancellationToken ct)
    {
        ThrowIfStopping(ct);
        if (RecoveryBlocksRoute(checkDurable: true, ownerToken: ct)) return;
        long map = _launch.CurrentMap(_pid) ?? 0;
        var initial = _world.Get(_pid);
        if (initial.MapId != map || !initial.SelfReadyFor(map) ||
            !initial.CellsReadyFor(map))
        {
            NoteActivityRecoveryRunBlockV18(
                ActivityRecoveryTransitionReasonV18.MapUnconfirmed, ct,
                "carte ou acteur non confirmes pour cette etape");
            RecoveryWarning("carte de jeu et publication locale incoherentes ; aucune action");
            if (initial.MapId > 0 && initial.MapId != map)
                StopRecoveryPlanningOnce(ct);
            return;
        }
        if (!RecoveryPodsSafe(ct))
        {
            string podsReason = RecoveryPodsRefusalReason();
            NoteActivityRecoveryRunBlockV18(
                podsReason.Contains("au seuil", StringComparison.Ordinal)
                    ? ActivityRecoveryTransitionReasonV18.PodsAtThreshold
                    : ActivityRecoveryTransitionReasonV18.PodsUnconfirmed,
                ct, podsReason);
            RecoveryWarning(podsReason);
            StopRecoveryPlanningOnce(ct);
            return;
        }
        var workflow = RecoveryForMap(map);
        if (workflow.IsQuarantined) { RecoveryBlocksRoute(ownerToken: ct); return; }

        if (step.Get("fight").CastToBool() || step.Get("forcefight").CastToBool() ||
            step.Get("forceFight").CastToBool() || step.Get("forcegather").CastToBool() ||
            step.Get("forceGather").CastToBool() || step.Get("npcBank").CastToBool() ||
            !step.Get("custom").IsNil() || !step.Get("lockedCustom").IsNil() ||
            !step.Get("lockedStorage").IsNil() || !step.Get("lockedHouse").IsNil())
        {
            RecoveryWarning("etape Lua composite non prise en charge ; aucune action executee");
            StopRecoveryPlanningOnce(ct);
            return;
        }
        if (GuidedSupplyIntent &&
            (!step.Get("gather").CastToBool() ||
             !step.Get("cell").IsNil() ||
             !step.Get("door").IsNil() || !step.Get("path").IsNil() ||
             !step.Get("paths").IsNil() || !step.Get("changeMap").IsNil() ||
             !step.Get("exitCell").IsNil()))
        {
            RecoveryWarning("approvisionnement guide : seule une recolte GID attestee est admissible");
            StopRecoveryPlanningOnce(ct);
            return;
        }

        // Validate the full generated waypoint shape BEFORE even an optional
        // gather. A malformed cell value must not silently spend this run's
        // one-shot harvest lease and only fail when it reaches the cell branch.
        var requestedCell = step.Get("cell");
        if (!requestedCell.IsNil() &&
            (requestedCell.Type != DataType.Number ||
             !double.IsFinite(requestedCell.Number) ||
             requestedCell.Number != Math.Truncate(requestedCell.Number) ||
             requestedCell.Number < 0 ||
             requestedCell.Number >= MapPathfinder.CellCount))
        {
            RecoveryWarning("cell invalide dans route recovery ; aucune action");
            StopRecoveryPlanningOnce(ct);
            return;
        }
        // Check provenance while the current map/actor publication is still
        // attached to this exact route. A filtered-out or depleted resource
        // must not mask a missing guided source/ledger prerequisite.
        if (!GuidedSupplyRoutePreflight(map, initial.MapGeneration, ct))
            return;
        bool gatherRequested = step.Get("gather").CastToBool();
        if (gatherRequested)
        {
            if (GatherOne(ct, strictRecovery: true)) return;
            if (RecoveryBlocksRoute(ownerToken: ct)) return;
            if (_launch.InteractivesDecodedMap(_pid) != map)
            {
                RecoveryWarning("stock de ressources de carte non decode ; arret sans sortie");
                StopRecoveryPlanningOnce(ct);
                return;
            }
            var state = _world.Get(_pid);
            if (_launch.CurrentMap(_pid) != map || !state.SelfReadyFor(map) ||
                !state.CellsReadyFor(map))
            {
                RecoveryWarning("carte ou position a change durant le balayage des ressources ; aucune sortie");
                StopRecoveryPlanningOnce(ct);
                return;
            }
            // A failed harvest remains available on the server, but GatherOne
            // blacklists it for the current map. Do not mistake that blacklist
            // for an exhausted map and silently advance a bank/farm waypoint.
            var filter = GatherFilter();
            if (state.Interactives.Any(i => i.Harvestable &&
                _gatherBlocked.Contains(i.ElementId) && IsWantedResource(i, map, filter)))
            {
                NoteActivityRecoveryRunBlockV18(
                    ActivityRecoveryTransitionReasonV18.ResourceOutOfReach, ct,
                    "ressource toujours disponible mais tentative bloquee");
                RecoveryWarning("ressource encore disponible mais bloquee/injoignable ; " +
                    "sortie reportee, planning suspendu");
                StopRecoveryPlanningOnce(ct);
                return;
            }
        }
        if (requestedCell.Type == DataType.Number)
        {
            int cell = (int)requestedCell.Number;
            var snapshot = ObserveActivity(0, -1, false, ct);
            var probe = RecoveryForMap(map).Evaluate(
                new ActivityRecoverySegment(ActivityRecoveryKind.Cell, cell), snapshot,
                _world.Get(_pid).IsWalkableForScript, _world.Get(_pid).StepAllowed);
            if (probe.Decision != ActivityRecoveryDecision.AlreadyObserved)
            {
                if (!ReserveRecovery(probe.Segment, ct, out var lease,
                    walkable: _world.Get(_pid).IsWalkableForScript,
                    stepAllowed: _world.Get(_pid).StepAllowed)) return;
                if (!RecoveryMaySend(lease, ct))
                {
                    RecoveryWarning("session/carte alteree apres reservation ; envoi de cellule annule");
                    StopRecoveryPlanningOnce(ct);
                    return;
                }
                // Already reserved: the legacy GoToCell is allowed to send once, then
                // the entire declarative route is quarantined regardless of its bool result.
                if (CaptureOfflineRecoveryDispatch(probe.Segment))
                {
                    RecordRecoveryEffect(0, -1, ct, lease);
                    return;
                }
                try { GoToCell(cell, ct); }
                finally { RecordRecoveryEffect(0, -1, ct, lease); }
                return;
            }
        }

        if (step.Get("door").Type is DataType.String or DataType.Number)
        {
            string? raw = step.Get("door").CastToString();
            if (raw is null || raw.Contains('|') || !int.TryParse(raw, out int cell) ||
                (uint)cell >= MapPathfinder.CellCount)
            {
                RecoveryWarning("door multiple ou invalide ; aucune interaction");
                StopRecoveryPlanningOnce(ct);
                return;
            }
            var st = _world.Get(_pid);
            if (_launch.InteractivesDecodedMap(_pid) != map)
            {
                RecoveryWarning("interactifs de carte non attestes ; aucun usage de porte");
                return;
            }
            var inter = st.InteractiveAt(cell);
            var skill = inter?.PickSkill(0);
            if (inter is null || skill is null || _launch.IsGatherResource(inter) ||
                st.SelfCell is not int source || !st.SelfReadyFor(map) ||
                !MapPathfinder.InteractionAdjacent(source, cell))
            {
                RecoveryWarning("porte/skill non attestes ; aucun envoi");
                StopRecoveryPlanningOnce(ct);
                return;
            }
            int? stand = _launch.InteractiveStandCell(_pid, map, source, inter, skill,
                st.IsWalkableForScript, st.StepAllowed, out _);
            if (stand != source)
            {
                RecoveryWarning("porte hors de portee : mouvement implicite interdit");
                StopRecoveryPlanningOnce(ct);
                return;
            }
            var interactionSegment = new ActivityRecoverySegment(ActivityRecoveryKind.Interaction,
                inter.ElementId, cell, inter.ElementId);
            if (!ReserveRecovery(interactionSegment, ct, out var lease,
                    inReach: true,
                    requiredSource: source)) return;
            if (!RecoveryMaySend(lease, ct))
            {
                RecoveryWarning("session/porte alteree apres reservation ; envoi annule");
                StopRecoveryPlanningOnce(ct);
                return;
            }
            if (CaptureOfflineRecoveryDispatch(interactionSegment))
            {
                RecordRecoveryEffect(inter.ElementId, cell, ct, lease);
                return;
            }
            // UseInteractive reselects the map target and can implicitly
            // move before using it. Recovery already proved the exact element,
            // skill instance and reach; send that single existing primitive.
            try
            {
                if (!_launch.UseInteractiveElement(_pid, inter, skill))
                    RecoveryWarning("porte refusee/non envoyee ; reservation conservee");
            }
            finally { RecordRecoveryEffect(inter.ElementId, cell, ct, lease); }
            return;
        }

        string? directionToken = null;
        var exit = step.Get("exitCell");
        if (!exit.IsNil())
        {
            if (!int.TryParse(exit.CastToString(), out int forced) ||
                (uint)forced >= MapPathfinder.CellCount ||
                _launch.ExitDirOfCell(map, forced) is not MapDir exitDirection)
            {
                RecoveryWarning("exitCell non attestee ; aucun changement de carte");
                StopRecoveryPlanningOnce(ct);
                return;
            }
            directionToken = $"{DirName(exitDirection)}({forced})";
        }
        else
        {
            var raw = step.Get("changeMap");
            if (raw.IsNil()) raw = step.Get("path");
            if (raw.IsNil()) raw = step.Get("paths");
            if (!raw.IsNil())
            {
                if (raw.Type != DataType.String)
                {
                    RecoveryWarning("path doit etre une direction unique (pas de liste ou waypoint)");
                    StopRecoveryPlanningOnce(ct);
                    return;
                }
                directionToken = raw.String;
            }
        }
        if (directionToken is null)
        {
            // A gather-only route with no presently eligible resources otherwise
            // revisits this same Lua step every tick. In strict recovery, absence
            // or exhaustion cannot prove our action completed; pause instead of
            // spinning or treating an empty list as an ACK.
            if (gatherRequested)
            {
                // GatherOne already recorded whether the map is genuinely
                // empty or its still-present resources are out of reach.
                // Preserve that more precise reason for PRIME/VM.
                RecoveryWarning("aucune ressource admissible / ressource epuisee ; etape sans sortie, planning suspendu");
                StopRecoveryPlanningOnce(ct);
            }
            return;
        }
        if (!LuaCompat.TryParseDirToken(directionToken, out MapDir dir, out int forcedCell) ||
            forcedCell >= 0 && _launch.ExitDirOfCell(map, forcedCell) != dir ||
            _launch.WorldTransitionInDirection(map, dir) is not { Type: 1 or 2 } transition ||
            transition.Criterion is not null ||
            transition.Arrival <= 0 ||
            _launch.NeighbourMapId(map, dir) != transition.Arrival)
        {
            NoteActivityRecoveryRunBlockV18(
                ActivityRecoveryTransitionReasonV18.RouteUnmatched, ct,
                "sortie de carte non prouvee par une transition simple");
            RecoveryWarning("sortie non prouvee comme bord direct : trajet multi-envois interdit");
            StopRecoveryPlanningOnce(ct);
            return;
        }
        // ChangeMapDirection* can dispatch MoveTo first, then queue a second
        // MapChangeRequest on arrival. One durable lease is not an admission for
        // that pair. Only a player ALREADY on the exact server-attested edge
        // cell can make a one-message map change here.
        var atExit = _world.Get(_pid);
        int exactExit = forcedCell >= 0 ? forcedCell : transition.CellId;
        if ((uint)exactExit >= MapPathfinder.CellCount ||
            _launch.ExitDirOfCell(map, exactExit) != dir ||
            !atExit.SelfReadyFor(map) || atExit.SelfCell != exactExit)
        {
            RecoveryWarning("sortie exige position serveur sur la cellule de bord ; aucun trajet composite");
            StopRecoveryPlanningOnce(ct);
            return;
        }
        if (_launch.MoveInProgress(_pid) || _launch.HasPendingMapChange(_pid))
        {
            RecoveryWarning("mouvement ou changement de carte deja en vol ; aucune nouvelle sortie");
            StopRecoveryPlanningOnce(ct);
            return;
        }
        var mapSegment = new ActivityRecoverySegment(ActivityRecoveryKind.MapTransition,
            transition.Arrival);
        if (!ReserveRecovery(mapSegment, ct, out var mapLease)) return;
        if (!RecoveryMaySend(mapLease, ct))
        {
            RecoveryWarning("session/carte alteree apres reservation ; sortie annulee");
            StopRecoveryPlanningOnce(ct);
            return;
        }
        if (CaptureOfflineRecoveryDispatch(mapSegment))
        {
            RecordRecoveryEffect(0, -1, ct, mapLease);
            return;
        }
        try
        {
            // The generic ChangeMap wrapper closes HDV, broadcasts a leader
            // exit to followers, and can fall back to unrelated travel paths.
            // None of those extra effects is covered by this one-shot marker.
            // Invoke exactly one direct jqk from the already confirmed exit cell.
            // Since source == exactExit, ChangeMapToCell cannot send MoveTo or
            // enqueue a later jqk, regardless of which branch its caller takes.
            var state = _world.Get(_pid);
            if (!state.SelfReadyFor(map) || state.SelfCell != exactExit ||
                !state.CellsReadyFor(map) ||
                _launch.ExitDirOfCell(map, exactExit) != dir)
            {
                RecoveryWarning("position de bord devenue incertaine ; sortie annulee");
                return;
            }
            bool sent = _launch.ChangeMapDirectionViaCell(_pid, map, exactExit,
                dir, exactExit, state.IsWalkableForScript,
                state.StepAllowed, state.SelfCellConfirmed);
            if (!sent)
            {
                RecoveryWarning("sortie directe non admise ; reservation conservee");
                return;
            }
            // CurrentMap observation is useful for status only. No position
            // change can release the marker without sequence-bound ACK.
            _ = WaitUntil(() => (_launch.CurrentMap(_pid) ?? 0) != map,
                6000, ct);
        }
        finally { RecordRecoveryEffect(0, -1, ct, mapLease); }
    }
}
