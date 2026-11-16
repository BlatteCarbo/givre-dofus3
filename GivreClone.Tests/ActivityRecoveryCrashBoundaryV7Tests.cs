extern alias F06App;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using Xunit;
using DurableStore = F06App::GivreClone.App.Services.ActivityRecoveryDurableStore;
using RecoveryScope = F06App::GivreClone.App.Services.ActivityRecoveryScope;
using RecoverySegment = F06App::GivreClone.App.Services.ActivityRecoverySegment;
using RecoveryKind = F06App::GivreClone.App.Services.ActivityRecoveryKind;
using RecoverySnapshot = F06App::GivreClone.App.Services.ActivityRecoverySnapshot;
using DurableState = F06App::GivreClone.App.Services.ActivityRecoveryDurableState;
using ReconciliationDecision = F06App::GivreClone.App.Services.ActivityRecoveryReconciliationDecision;

namespace GivreClone.Tests;

/// <summary>
/// Static-only authoring: these offline tests are for PRIME to execute sequentially.
/// File scopes live under new unique temp roots; nothing touches a bot profile or game.
/// </summary>
public sealed class ActivityRecoveryCrashBoundaryV7Tests
{
    private static readonly RecoveryScope Owner = new(292, 1071683469604);
    private static readonly RecoveryScope OtherCharacter = new(292, 1071683469605);
    private static readonly RecoveryScope OtherServer = new(291, 1071683469604);
    private static readonly RecoverySegment Move = new(RecoveryKind.Cell, 320);
    private static readonly RecoverySegment Gather = new(RecoveryKind.Gather, 2416, Cell: 241, ElementId: 2416);

    private static RecoverySnapshot Snapshot(long gen = 7) => new(
        new ResumableRouteMap(191105026, gen), 191105026, 191105026, 240,
        PositionConfirmed: true, Interrupted: false, Suspended: false,
        ResourcePresent: true, ResourceAvailable: true, AlreadyInReach: true,
        StockGid: 2416, StockCount: 3);

    private sealed class Root : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "GivreClone-ActivityRecoveryV7",
            Guid.NewGuid().ToString("N"));

        public DurableStore Store() => new(Path);
        public string Marker(RecoveryScope scope) => System.IO.Path.Combine(
            Path, "server-" + scope.ServerId, "character-" + scope.CharacterId + ".pending");

        public void Dispose()
        {
            // Only this test's newly generated private root may be removed.
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    [Fact]
    public void Missing_marker_on_healthy_root_is_clear_but_not_dispatch_authorization()
    {
        using var root = new Root();
        Directory.CreateDirectory(root.Path);
        Assert.Equal(DurableState.Clear, root.Store().ReadStatus(Owner).State);
        Assert.False(root.Store().Reconcile(Owner, Snapshot(), true).MayRedispatch);
        Assert.True(root.Store().TryReserve(Owner, Move, Snapshot()).MayDispatch);
        Assert.Equal(DurableState.Pending, root.Store().ReadStatus(Owner).State);
    }

    [Fact]
    public void Missing_marker_when_root_was_never_created_can_create_one_once()
    {
        using var root = new Root();
        Assert.False(Directory.Exists(root.Path));
        Assert.Equal(DurableState.Clear, root.Store().ReadStatus(Owner).State);
        Assert.True(root.Store().TryReserve(Owner, Move, Snapshot()).MayDispatch);
        Assert.False(root.Store().TryReserve(Owner, Move, Snapshot()).MayDispatch);
    }

    [Fact]
    public void Parent_path_is_file_not_directory_cannot_be_reported_clear()
    {
        using var root = new Root();
        Directory.CreateDirectory(root.Path);
        string occupied = System.IO.Path.Combine(root.Path, "file-instead-of-recovery-root");
        File.WriteAllText(occupied, "foreign sentinel");
        var store = new DurableStore(occupied);
        var status = store.ReadStatus(Owner);
        // Prior code used File.Exists(marker) -> false, then Directory.Exists
        // -> false, incorrectly reporting Clear despite malformed root.
        Assert.NotEqual(DurableState.Clear, status.State);
        Assert.True(status.IsQuarantined);
        Assert.False(store.TryReserve(Owner, Move, Snapshot()).MayDispatch);
        Assert.Equal("foreign sentinel", File.ReadAllText(occupied));
    }

    [Fact]
    public void Marker_path_is_directory_and_must_not_be_mistaken_for_absence()
    {
        using var root = new Root();
        string marker = root.Marker(Owner);
        Directory.CreateDirectory(marker);
        var status = root.Store().ReadStatus(Owner);
        Assert.NotEqual(DurableState.Clear, status.State);
        Assert.True(status.IsQuarantined);
        Assert.False(root.Store().TryReserve(Owner, Move, Snapshot()).MayDispatch);
    }

    [Fact]
    public void Write_ahead_then_crash_before_send_survives_stop_start_same_scope()
    {
        using var root = new Root();
        var first = root.Store().TryReserve(Owner, Gather, Snapshot());
        Assert.True(first.MayDispatch);
        byte[] originallyFlushed = File.ReadAllBytes(root.Marker(Owner));
        // New store instance stands in for a fresh process and new PID.
        var restarted = root.Store();
        Assert.Equal(DurableState.Pending, restarted.ReadStatus(Owner).State);
        Assert.False(restarted.TryReserve(Owner, Gather, Snapshot()).MayDispatch);
        Assert.False(restarted.TryReserve(Owner, Move, Snapshot(8)).MayDispatch);
        Assert.Equal(originallyFlushed, File.ReadAllBytes(root.Marker(Owner)));
    }

    [Fact]
    public async Task Concurrent_same_scope_writers_yield_exactly_one_durable_admission()
    {
        using var root = new Root();
        using var start = new ManualResetEventSlim(false);
        var requests = Enumerable.Range(0, 48).Select(i => Task.Run(() =>
        {
            start.Wait();
            var s = i % 2 == 0 ? Move : Gather;
            return root.Store().TryReserve(Owner, s, Snapshot());
        })).ToArray();
        start.Set();
        var results = await Task.WhenAll(requests);
        Assert.Single(results.Where(result => result.MayDispatch));
        Assert.All(results.Where(result => !result.MayDispatch),
            result => Assert.NotEqual(DurableState.Clear, result.Status.State));
        Assert.Equal(DurableState.Pending, root.Store().ReadStatus(Owner).State);
    }

    [Fact]
    public void Reused_pid_distinct_character_and_server_cannot_clear_original_marker()
    {
        using var root = new Root();
        Assert.True(root.Store().TryReserve(Owner, Move, Snapshot()).MayDispatch);
        Assert.Equal(DurableState.Pending, root.Store().ReadStatus(Owner).State);
        Assert.Equal(DurableState.Clear, root.Store().ReadStatus(OtherCharacter).State);
        Assert.Equal(DurableState.Clear, root.Store().ReadStatus(OtherServer).State);
        Assert.True(root.Store().TryReserve(OtherCharacter, Move, Snapshot()).MayDispatch);
        Assert.True(root.Store().TryReserve(OtherServer, Gather, Snapshot()).MayDispatch);
        Assert.Equal(DurableState.Pending, root.Store().ReadStatus(Owner).State);
        Assert.Equal(3, new[] { Owner, OtherCharacter, OtherServer }.Count(
            s => root.Store().ReadStatus(s).State == DurableState.Pending));
    }

    [Fact]
    public void Exclusive_writer_window_is_busy_not_clear_and_never_grants_second_send()
    {
        using var root = new Root();
        string marker = root.Marker(Owner);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(marker)!);
        using (var writing = new FileStream(marker, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 4096, FileOptions.WriteThrough))
        {
            writing.Write(Encoding.UTF8.GetBytes("ARC-DURABLE-1\n"));
            var status = root.Store().ReadStatus(Owner);
            Assert.NotEqual(DurableState.Clear, status.State);
            Assert.False(root.Store().TryReserve(Owner, Gather, Snapshot()).MayDispatch);
            writing.Flush(flushToDisk: true);
        }
        Assert.Equal(DurableState.Corrupt, root.Store().ReadStatus(Owner).State);
        Assert.False(root.Store().TryReserve(Owner, Move, Snapshot()).MayDispatch);
    }

    [Fact]
    public void Zero_byte_marker_from_crash_during_create_is_never_rearmed()
    {
        using var root = new Root();
        string marker = root.Marker(Owner);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(marker)!);
        using (new FileStream(marker, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 4096, FileOptions.WriteThrough)) { }
        Assert.Equal(DurableState.Corrupt, root.Store().ReadStatus(Owner).State);
        Assert.False(root.Store().TryReserve(Owner, Move, Snapshot()).MayDispatch);
        Assert.Equal(0, new FileInfo(marker).Length);
    }

    [Fact]
    public void Truncated_marker_after_stop_start_preserves_original_corrupt_bytes()
    {
        using var root = new Root();
        Assert.True(root.Store().TryReserve(Owner, Move, Snapshot()).MayDispatch);
        string marker = root.Marker(Owner);
        byte[] first = File.ReadAllBytes(marker);
        byte[] truncated = first[..(first.Length / 2)];
        File.WriteAllBytes(marker, truncated);
        Assert.Equal(DurableState.Corrupt, root.Store().ReadStatus(Owner).State);
        Assert.False(root.Store().TryReserve(Owner, Move, Snapshot()).MayDispatch);
        Assert.Equal(truncated, File.ReadAllBytes(marker));
    }

    [Fact]
    public void Non_utf8_marker_with_valid_framing_must_be_corrupt_not_normalized()
    {
        using var root = new Root();
        string marker = root.Marker(Owner);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(marker)!);
        // UTF8Encoding.GetString() with replacement fallback can erase
        // information from malformed storage bytes. Strict decoder rejects it.
        var payload = Encoding.UTF8.GetBytes(
            "ARC-DURABLE-1\n" + new string('0', 64) + "\n{\"note\":\"");
        File.WriteAllBytes(marker, payload.Concat(new byte[] { 0xFF, 0xFE, 0x7D, 0x0A }).ToArray());
        byte[] original = File.ReadAllBytes(marker);
        Assert.Equal(DurableState.Corrupt, root.Store().ReadStatus(Owner).State);
        Assert.False(root.Store().TryReserve(Owner, Gather, Snapshot()).MayDispatch);
        Assert.Equal(original, File.ReadAllBytes(marker));
    }

    [Fact]
    public async Task Concurrent_reconciliation_of_pending_and_corrupt_marker_never_resets_it()
    {
        using var root = new Root();
        Assert.True(root.Store().TryReserve(Owner, Gather, Snapshot()).MayDispatch);
        byte[] original = File.ReadAllBytes(root.Marker(Owner));
        var inspectors = Enumerable.Range(0, 64).Select(i => Task.Run(() =>
            root.Store().Reconcile(Owner, Snapshot(7 + i % 3)
                with { ServerCell = i % 2 == 0 ? 320 : 240 }, true))).ToArray();
        var observations = await Task.WhenAll(inspectors);
        Assert.All(observations, result =>
        {
            Assert.Equal(ReconciliationDecision.PendingQuarantine, result.Decision);
            Assert.False(result.MayRedispatch);
        });
        Assert.Equal(original, File.ReadAllBytes(root.Marker(Owner)));
        // An observer must not erase the marker before another writer races in.
        Assert.False(root.Store().TryReserve(Owner, Move, Snapshot()).MayDispatch);
    }

    [Fact]
    public void Invalid_server_character_and_stale_checkpoint_fail_closed_before_marker()
    {
        using var root = new Root();
        var store = root.Store();
        Assert.False(store.TryReserve(new RecoveryScope(0, Owner.CharacterId),
            Move, Snapshot()).MayDispatch);
        Assert.False(store.TryReserve(new RecoveryScope(Owner.ServerId, 0),
            Move, Snapshot()).MayDispatch);
        Assert.False(store.TryReserve(Owner, Move,
            Snapshot() with { PositionConfirmed = false }).MayDispatch);
        Assert.False(store.TryReserve(Owner, Move,
            Snapshot() with { GeometryMap = 0 }).MayDispatch);
        Assert.False(File.Exists(root.Marker(Owner)));
    }
}
