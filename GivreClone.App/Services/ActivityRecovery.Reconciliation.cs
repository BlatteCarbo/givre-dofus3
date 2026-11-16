using System;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>
/// Read-only decision. PendingQuarantine NEVER authorizes a packet, including when the
/// effect appears complete. A real sequence-bound server ACK is not exposed by LuaHost.
/// </summary>
public enum ActivityRecoveryReconciliationDecision
{
    NoPending,
    PendingQuarantine
}

public enum ActivityRecoveryReconciliationEvidence
{
    NoMarker,
    IdentityUnconfirmed,
    MarkerCorrupt,
    MarkerBusy,
    StorageUnavailable,
    CheckpointUnconfirmed,
    GenerationRegressed,
    MapChangedWithoutAttribution,
    SourceMapStillObserved,
    CellTargetObservedWithoutAttribution,
    DestinationMapObservedWithoutAttribution,
    ResourceDataUnconfirmed,
    ResourceAbsentWithoutAttribution,
    ResourceExhaustedWithoutAttribution,
    ResourceStillAvailable,
    InventorySnapshotUnconfirmed,
    InventoryIncreaseWithoutAttribution,
    InventoryUnchanged,
    InteractionStateChangedWithoutAttribution,
    NoCorrelatedAck
}

/// <summary>
/// Immutable diagnostic for Prime/UI and an explicit Lua reconciliation request.
/// Evidence represents an observation; it never constitutes proof that the durable
/// request was accepted or committed, so it never clears/replaces the pending marker.
/// </summary>
public sealed record ActivityRecoveryReconciliationResult(
    ActivityRecoveryDurableStatus DurableStatus,
    ActivityRecoveryReconciliationDecision Decision,
    ActivityRecoveryReconciliationEvidence Evidence,
    string Explanation,
    long ObservedMap,
    long ObservedGeneration)
{
    public bool IsQuarantined => DurableStatus.IsQuarantined;
    public bool MayRedispatch => false;
}

public sealed partial class ActivityRecoveryDurableStore
{
    /// <summary>
    /// Compare a genuinely observed server checkpoint with the fsynced pre-send snapshot.
    /// Pure read: opens the marker with read sharing, verifies checksum/identity, and
    /// neither creates/deletes/renames/clears it nor invents a sequence ACK.
    ///
    /// interactivesDecodedForMap must come from InteractivesDecodedMap(pid) == current
    /// map. An absent element on an un-decoded map is NOT evidence of exhaustion.
    /// inventorySnapshotAuthoritative requires a separately confirmed full inventory
    /// snapshot; LuaHost currently lacks that signal and passes false.
    /// </summary>
    public ActivityRecoveryReconciliationResult Reconcile(
        ActivityRecoveryScope scope,
        ActivityRecoverySnapshot observed,
        bool interactivesDecodedForMap,
        bool inventorySnapshotAuthoritative = false)
    {
        var (status, record) = ReadMarker(scope);
        ActivityRecoveryReconciliationResult Result(ActivityRecoveryReconciliationEvidence evidence,
            string explanation) => new(
                status,
                status.IsQuarantined
                    ? ActivityRecoveryReconciliationDecision.PendingQuarantine
                    : ActivityRecoveryReconciliationDecision.NoPending,
                evidence, explanation, observed.Map.MapId, observed.Map.Generation);

        if (status.State != ActivityRecoveryDurableState.Pending || record is null)
        {
            var evidence = status.State switch
            {
                ActivityRecoveryDurableState.Clear => ActivityRecoveryReconciliationEvidence.NoMarker,
                ActivityRecoveryDurableState.IdentityMissing => ActivityRecoveryReconciliationEvidence.IdentityUnconfirmed,
                ActivityRecoveryDurableState.Corrupt => ActivityRecoveryReconciliationEvidence.MarkerCorrupt,
                ActivityRecoveryDurableState.Busy => ActivityRecoveryReconciliationEvidence.MarkerBusy,
                _ => ActivityRecoveryReconciliationEvidence.StorageUnavailable
            };
            return Result(evidence, status.Detail);
        }

        // Identity/epoch are from a persisted marker scoped by server and character,
        // never by the process PID. Do not trust any current actor without an actual
        // server-confirmed cell/map generation supplied by the caller.
        if (!observed.Map.IsValid || observed.Interrupted || observed.Suspended ||
            !observed.PositionConfirmed || observed.PositionMap != observed.Map.MapId ||
            observed.GeometryMap != observed.Map.MapId ||
            observed.ServerCell is not int serverCell ||
            (uint)serverCell >= MapPathfinder.CellCount)
            return Result(ActivityRecoveryReconciliationEvidence.CheckpointUnconfirmed,
                "authoritative map/generation/cell unavailable; marker remains pending");

        var before = record.Observation;
        var segment = record.Segment;
        bool sameMap = observed.Map.MapId == before.Map.MapId;
        if (sameMap && observed.Map.Generation < before.Map.Generation)
            return Result(ActivityRecoveryReconciliationEvidence.GenerationRegressed,
                "older map generation than durable pre-send checkpoint");

        if (segment.Kind == ActivityRecoveryKind.MapTransition)
        {
            if (observed.Map.MapId == segment.Target &&
                observed.Map.Generation > before.Map.Generation)
                return Result(ActivityRecoveryReconciliationEvidence.DestinationMapObservedWithoutAttribution,
                    "destination map seen on newer generation, but no ACK bound to dispatch");
            return Result(ActivityRecoveryReconciliationEvidence.MapChangedWithoutAttribution,
                "map transition not attributable to durable action");
        }

        if (!sameMap)
            return Result(ActivityRecoveryReconciliationEvidence.MapChangedWithoutAttribution,
                "source map no longer active; pending source action cannot be replayed");

        if (segment.Kind == ActivityRecoveryKind.Cell)
            return observed.ServerCell == segment.Target
                ? Result(ActivityRecoveryReconciliationEvidence.CellTargetObservedWithoutAttribution,
                    "target cell observed on source map, but no correlated movement ACK")
                : Result(ActivityRecoveryReconciliationEvidence.SourceMapStillObserved,
                    "same map; destination not confirmed, original send remains ambiguous");

        if (!interactivesDecodedForMap)
            return Result(ActivityRecoveryReconciliationEvidence.ResourceDataUnconfirmed,
                "interactive state is not decoded for this map");

        if (!observed.ResourcePresent)
            return Result(ActivityRecoveryReconciliationEvidence.ResourceAbsentWithoutAttribution,
                "element absent from decoded map; may be taken by another actor");

        if (!observed.ResourceAvailable)
            return Result(segment.Kind == ActivityRecoveryKind.Gather
                    ? ActivityRecoveryReconciliationEvidence.ResourceExhaustedWithoutAttribution
                    : ActivityRecoveryReconciliationEvidence.InteractionStateChangedWithoutAttribution,
                "resource/interactive unavailable, but action was not attributed to this sender");

        if (segment.Kind == ActivityRecoveryKind.Gather &&
            before.StockGid is > 0 && before.StockCount is int initial &&
            observed.StockGid == before.StockGid && observed.StockCount is int now)
        {
            if (!inventorySnapshotAuthoritative)
                return Result(ActivityRecoveryReconciliationEvidence.InventorySnapshotUnconfirmed,
                    "inventory count cached without an authoritative full snapshot for this epoch");
            return now > initial
                ? Result(ActivityRecoveryReconciliationEvidence.InventoryIncreaseWithoutAttribution,
                    "inventory grew, but no element/request-specific server attribution")
                : Result(ActivityRecoveryReconciliationEvidence.InventoryUnchanged,
                    "no attributable inventory gain for this durable action");
        }

        return Result(segment.Kind == ActivityRecoveryKind.Gather
                ? ActivityRecoveryReconciliationEvidence.ResourceStillAvailable
                : ActivityRecoveryReconciliationEvidence.NoCorrelatedAck,
            "resource/interactive remains available; no sequence-bound ACK");
    }
}
