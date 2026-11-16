extern alias F06App;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GivreClone.Protocol;
using Xunit;
using DurableStore = F06App::GivreClone.App.Services.ActivityRecoveryDurableStore;
using RecoveryScope = F06App::GivreClone.App.Services.ActivityRecoveryScope;
using RecoveryKind = F06App::GivreClone.App.Services.ActivityRecoveryKind;
using RecoverySegment = F06App::GivreClone.App.Services.ActivityRecoverySegment;
using RecoverySnapshot = F06App::GivreClone.App.Services.ActivityRecoverySnapshot;
using DurableState = F06App::GivreClone.App.Services.ActivityRecoveryDurableState;
using ReconcileDecision = F06App::GivreClone.App.Services.ActivityRecoveryReconciliationDecision;
using ReconcileEvidence = F06App::GivreClone.App.Services.ActivityRecoveryReconciliationEvidence;

namespace GivreClone.Tests;

/// <summary>
/// Offline read-only reconciliation: all fixtures use private temp roots,
/// never an active client, network, user profile or other worker's files.
/// </summary>
public sealed class ActivityRecoveryReconciliationTests
{
    private static readonly RecoveryScope Identity = new(292, 1071683469604);
    private static readonly RecoverySegment Move = new(RecoveryKind.Cell, 320);
    private static readonly RecoverySegment Gather = new(RecoveryKind.Gather, 2416, Cell: 241, ElementId: 2416);
    private static readonly RecoverySegment Door = new(RecoveryKind.Interaction, 101, Cell: 241, ElementId: 101);
    private static readonly RecoverySegment Transition = new(RecoveryKind.MapTransition, 191105027);

    private static RecoverySnapshot Before() => new(
        new ResumableRouteMap(191105026, 7), 191105026, 191105026, 240,
        PositionConfirmed: true, Interrupted: false, Suspended: false,
        ResourcePresent: true, ResourceAvailable: true, AlreadyInReach: true,
        StockGid: 2416, StockCount: 3);

    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "GivreClone-ActivityRecovery-Reconciliation", Guid.NewGuid().ToString("N"));

        public DurableStore Store() => new(Path);
        public string Marker(RecoveryScope scope) => System.IO.Path.Combine(Path,
            "server-" + scope.ServerId, "character-" + scope.CharacterId + ".pending");

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private static void Pending(
        F06App::GivreClone.App.Services.ActivityRecoveryReconciliationResult result,
        ReconcileEvidence expected)
    {
        Assert.Equal(ReconcileDecision.PendingQuarantine, result.Decision);
        Assert.Equal(expected, result.Evidence);
        Assert.Equal(DurableState.Pending, result.DurableStatus.State);
        Assert.True(result.IsQuarantined);
        Assert.False(result.MayRedispatch);
    }

    [Fact]
    public void No_marker_is_readonly_not_an_admission_token()
    {
        using var root = new TempRoot();
        var store = root.Store();
        var result = store.Reconcile(Identity, Before() with { ServerCell = 320 }, true);
        Assert.Equal(ReconcileDecision.NoPending, result.Decision);
        Assert.Equal(ReconcileEvidence.NoMarker, result.Evidence);
        Assert.Equal(DurableState.Clear, result.DurableStatus.State);
        Assert.False(result.MayRedispatch);
        Assert.False(File.Exists(root.Marker(Identity)));
    }

    [Fact]
    public void Confirmed_target_cell_same_generation_does_not_release_crash_marker()
    {
        using var root = new TempRoot();
        var store = root.Store();
        Assert.True(store.TryReserve(Identity, Move, Before()).MayDispatch);
        byte[] marker = File.ReadAllBytes(root.Marker(Identity));
        var result = root.Store().Reconcile(Identity, Before() with { ServerCell = 320 }, true);
        Pending(result, ReconcileEvidence.CellTargetObservedWithoutAttribution);
        Assert.Equal(320L, result.DurableStatus.Segment?.Target);
        Assert.Equal(191105026L, result.DurableStatus.MapId);
        Assert.Equal(7L, result.DurableStatus.MapGeneration);
        Assert.Equal(marker, File.ReadAllBytes(root.Marker(Identity)));
        Assert.False(store.TryReserve(Identity, Move, Before()).MayDispatch);
    }

    [Fact]
    public void Newer_same_map_generation_does_not_turn_observed_move_into_ack()
    {
        using var root = new TempRoot();
        var store = root.Store();
        Assert.True(store.TryReserve(Identity, Move, Before()).MayDispatch);
        var newer = Before() with { Map = new(191105026, 8), ServerCell = 320 };
        Pending(store.Reconcile(Identity, newer, true),
            ReconcileEvidence.CellTargetObservedWithoutAttribution);
        Assert.False(store.TryReserve(Identity, Move, newer).MayDispatch);
    }

    [Fact]
    public void Wrong_cell_or_stale_generation_preserves_unattributed_quarantine()
    {
        using var root = new TempRoot();
        var store = root.Store();
        Assert.True(store.TryReserve(Identity, Move, Before()).MayDispatch);
        Pending(store.Reconcile(Identity, Before(), true),
            ReconcileEvidence.SourceMapStillObserved);
        var old = Before() with { Map = new(191105026, 6), ServerCell = 320 };
        Pending(store.Reconcile(Identity, old, true),
            ReconcileEvidence.GenerationRegressed);
        Pending(store.Reconcile(Identity, Before() with { PositionConfirmed = false }, true),
            ReconcileEvidence.CheckpointUnconfirmed);
        Assert.False(store.TryReserve(Identity, Move, Before()).MayDispatch);
    }

    [Fact]
    public void Map_transition_target_on_newer_epoch_is_not_attributable()
    {
        using var root = new TempRoot();
        var store = root.Store();
        Assert.True(store.TryReserve(Identity, Transition, Before()).MayDispatch);
        var moved = Before() with
        {
            Map = new(191105027, 8), GeometryMap = 191105027,
            PositionMap = 191105027, ServerCell = 241
        };
        Pending(store.Reconcile(Identity, moved, false),
            ReconcileEvidence.DestinationMapObservedWithoutAttribution);
        Pending(store.Reconcile(Identity, moved with { Map = new(191105027, 7) }, false),
            ReconcileEvidence.MapChangedWithoutAttribution);
        Pending(store.Reconcile(Identity, moved with
        {
            Map = new(191105028, 8), GeometryMap = 191105028, PositionMap = 191105028
        }, false), ReconcileEvidence.MapChangedWithoutAttribution);
    }

    [Fact]
    public void Source_map_exit_after_cell_send_never_suggests_new_dispatch()
    {
        using var root = new TempRoot();
        var store = root.Store();
        Assert.True(store.TryReserve(Identity, Move, Before()).MayDispatch);
        var other = Before() with
        {
            Map = new(191105028, 9), GeometryMap = 191105028,
            PositionMap = 191105028, ServerCell = 211
        };
        Pending(store.Reconcile(Identity, other, true),
            ReconcileEvidence.MapChangedWithoutAttribution);
    }

    [Fact]
    public void Gather_resource_exhaustion_is_not_an_ack_from_this_sender()
    {
        using var root = new TempRoot();
        var store = root.Store();
        Assert.True(store.TryReserve(Identity, Gather, Before()).MayDispatch);
        var exhausted = Before() with { ResourceAvailable = false, StockCount = 5 };
        Pending(store.Reconcile(Identity, exhausted, true, inventorySnapshotAuthoritative: true),
            ReconcileEvidence.ResourceExhaustedWithoutAttribution);
        // A different actor may exhaust the same element while our request times out.
        Pending(store.Reconcile(Identity, exhausted with { Map = new(191105026, 10) }, true),
            ReconcileEvidence.ResourceExhaustedWithoutAttribution);
        Assert.False(store.TryReserve(Identity, Gather, exhausted).MayDispatch);
    }

    [Fact]
    public void Missing_resource_requires_current_map_decoded_not_a_stale_list()
    {
        using var root = new TempRoot();
        var store = root.Store();
        Assert.True(store.TryReserve(Identity, Gather, Before()).MayDispatch);
        var absent = Before() with { ResourcePresent = false, ResourceAvailable = false, StockGid = null };
        Pending(store.Reconcile(Identity, absent, false),
            ReconcileEvidence.ResourceDataUnconfirmed);
        Pending(store.Reconcile(Identity, absent, true),
            ReconcileEvidence.ResourceAbsentWithoutAttribution);
    }

    [Fact]
    public void Inventory_delta_is_not_proof_without_authoritative_inventory_epoch_or_attribution()
    {
        using var root = new TempRoot();
        var store = root.Store();
        Assert.True(store.TryReserve(Identity, Gather, Before()).MayDispatch);
        var grew = Before() with { StockCount = 5 };
        Pending(store.Reconcile(Identity, grew, true, inventorySnapshotAuthoritative: false),
            ReconcileEvidence.InventorySnapshotUnconfirmed);
        Pending(store.Reconcile(Identity, grew, true, inventorySnapshotAuthoritative: true),
            ReconcileEvidence.InventoryIncreaseWithoutAttribution);
        Pending(store.Reconcile(Identity, Before(), true, inventorySnapshotAuthoritative: true),
            ReconcileEvidence.InventoryUnchanged);
        Pending(store.Reconcile(Identity, grew with { StockGid = 98765 }, true,
            inventorySnapshotAuthoritative: true), ReconcileEvidence.ResourceStillAvailable);
        Assert.Equal(DurableState.Pending, store.ReadStatus(Identity).State);
    }

    [Fact]
    public void Interactive_state_change_is_not_a_sequence_bound_confirmation()
    {
        using var root = new TempRoot();
        var store = root.Store();
        Assert.True(store.TryReserve(Identity, Door, Before()).MayDispatch);
        Pending(store.Reconcile(Identity, Before() with { ResourceAvailable = false }, true),
            ReconcileEvidence.InteractionStateChangedWithoutAttribution);
        Pending(store.Reconcile(Identity, Before(), true),
            ReconcileEvidence.NoCorrelatedAck);
    }

    [Fact]
    public void Corrupted_and_zero_byte_markers_are_pending_quarantine_even_with_target_observed()
    {
        using var root = new TempRoot();
        string path = root.Marker(Identity);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Array.Empty<byte>());
        var store = root.Store();
        var result = store.Reconcile(Identity, Before() with { ServerCell = 320 }, true);
        Assert.Equal(ReconcileDecision.PendingQuarantine, result.Decision);
        Assert.Equal(ReconcileEvidence.MarkerCorrupt, result.Evidence);
        Assert.Equal(DurableState.Corrupt, result.DurableStatus.State);
        Assert.False(result.MayRedispatch);
        Assert.Equal(0, new FileInfo(path).Length);
        Assert.False(store.TryReserve(Identity, Move, Before()).MayDispatch);
    }

    [Fact]
    public void Exclusive_writer_window_is_busy_and_never_a_clear_reconciliation()
    {
        using var root = new TempRoot();
        var store = root.Store();
        Assert.True(store.TryReserve(Identity, Move, Before()).MayDispatch);
        using (var locked = new FileStream(root.Marker(Identity),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = store.Reconcile(Identity, Before() with { ServerCell = 320 }, true);
            Assert.Equal(ReconcileDecision.PendingQuarantine, result.Decision);
            Assert.Equal(ReconcileEvidence.MarkerBusy, result.Evidence);
            Assert.False(result.MayRedispatch);
        }
        Pending(store.Reconcile(Identity, Before() with { ServerCell = 320 }, true),
            ReconcileEvidence.CellTargetObservedWithoutAttribution);
    }

    [Fact]
    public void Invalid_identity_cannot_inspect_or_release_a_different_persons_marker()
    {
        using var root = new TempRoot();
        var store = root.Store();
        Assert.True(store.TryReserve(Identity, Move, Before()).MayDispatch);
        var missing = store.Reconcile(new RecoveryScope(0, Identity.CharacterId), Before(), true);
        Assert.Equal(ReconcileDecision.PendingQuarantine, missing.Decision);
        Assert.Equal(ReconcileEvidence.IdentityUnconfirmed, missing.Evidence);
        var other = store.Reconcile(new RecoveryScope(291, Identity.CharacterId), Before(), true);
        Assert.Equal(ReconcileDecision.NoPending, other.Decision);
        Assert.Equal(ReconcileEvidence.NoMarker, other.Evidence);
        Assert.Equal(DurableState.Pending, store.ReadStatus(Identity).State);
    }

    [Fact]
    public async Task Concurrent_inspection_and_admission_can_never_unlock_after_a_crash()
    {
        using var root = new TempRoot();
        Assert.True(root.Store().TryReserve(Identity, Gather, Before()).MayDispatch);
        byte[] original = File.ReadAllBytes(root.Marker(Identity));
        var tasks = Enumerable.Range(0, 64).Select(i => Task.Run(() =>
        {
            var store = root.Store();
            var observed = Before() with
            {
                ResourceAvailable = i % 2 == 0,
                StockCount = 3 + i,
                Map = new(191105026, 7 + i % 4)
            };
            var result = store.Reconcile(Identity, observed, true, i % 3 == 0);
            return (result, admission: store.TryReserve(Identity, Gather, observed));
        })).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, value =>
        {
            Assert.Equal(ReconcileDecision.PendingQuarantine, value.result.Decision);
            Assert.False(value.result.MayRedispatch);
            Assert.True(value.result.IsQuarantined);
            Assert.False(value.admission.MayDispatch);
        });
        Assert.Equal(original, File.ReadAllBytes(root.Marker(Identity)));
    }

    [Fact]
    public void Explicit_inspection_is_reproducible_without_mutating_fsynced_marker()
    {
        using var root = new TempRoot();
        var store = root.Store();
        Assert.True(store.TryReserve(Identity, Transition, Before()).MayDispatch);
        byte[] initial = File.ReadAllBytes(root.Marker(Identity));
        var snapshot = Before() with { Map = new(191105027, 8), GeometryMap = 191105027,
            PositionMap = 191105027, ServerCell = 320 };
        var first = store.Reconcile(Identity, snapshot, true);
        var second = root.Store().Reconcile(Identity, snapshot, true);
        Assert.Equal(first, second);
        Assert.Equal(initial, File.ReadAllBytes(root.Marker(Identity)));
        Assert.False(store.TryReserve(Identity, Transition, Before()).MayDispatch);
    }
}
