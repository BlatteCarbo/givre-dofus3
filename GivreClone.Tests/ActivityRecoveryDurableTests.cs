extern alias F06App;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GivreClone.Protocol;
using Xunit;
using DurableStore = F06App::GivreClone.App.Services.ActivityRecoveryDurableStore;
using DurableScope = F06App::GivreClone.App.Services.ActivityRecoveryScope;
using DurableState = F06App::GivreClone.App.Services.ActivityRecoveryDurableState;
using RecoverySegment = F06App::GivreClone.App.Services.ActivityRecoverySegment;
using RecoveryKind = F06App::GivreClone.App.Services.ActivityRecoveryKind;
using RecoverySnapshot = F06App::GivreClone.App.Services.ActivityRecoverySnapshot;
using RecoveryWorkflow = F06App::GivreClone.App.Services.ActivityRecoveryWorkflow;

namespace GivreClone.Tests;

/// <summary>
/// Disk-only crash/restart durability harness. Every test owns a fresh directory
/// below the process temporary root; no game, app profile, scripts or sockets.
/// </summary>
public sealed class ActivityRecoveryDurableTests
{
    private static readonly DurableScope CharacterA = new(292, 1071683469604);
    private static readonly DurableScope CharacterB = new(292, 1071683469605);
    private static readonly DurableScope OtherServer = new(291, 1071683469604);
    private static readonly RecoverySegment Move = new(RecoveryKind.Cell, 320);
    private static readonly RecoverySegment Gather = new(RecoveryKind.Gather, 127,
        Cell: 241, ElementId: 127);

    private static RecoverySnapshot Observed() =>
        new(new ResumableRouteMap(191105026, 7), 191105026, 191105026,
            240, true, false, false, true, true, true, 2416, 3);

    private sealed class TestRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "GivreClone-DurableRecovery-Tests", Guid.NewGuid().ToString("N"));

        public DurableStore Open() => new(Path);

        public string Marker(DurableScope scope) => System.IO.Path.Combine(Path,
            "server-" + scope.ServerId, "character-" + scope.CharacterId + ".pending");

        public void Dispose()
        {
            // Only the explicitly created private test directory may be cleaned.
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    [Fact]
    public void Durable_write_before_dispatch_survives_new_store_and_crash_before_send()
    {
        using var root = new TestRoot();
        var scope = CharacterA;
        var first = root.Open().TryReserve(scope, Gather, Observed());
        Assert.True(first.MayDispatch);
        Assert.Equal(DurableState.Pending, first.Status.State);
        Assert.Equal(scope, first.Status.Scope);
        Assert.True(first.Status.IsQuarantined);
        Assert.Equal(Gather, first.Status.Segment);
        Assert.True(File.Exists(root.Marker(scope)));
        Assert.True(new FileInfo(root.Marker(scope)).Length > 0);

        // Pretend the process terminated here, before any request was injected.
        // Reservation is intentionally NOT refunded; send outcome is unknowable.
        var restarted = root.Open();
        var status = restarted.ReadStatus(scope);
        Assert.Equal(DurableState.Pending, status.State);
        Assert.Equal(Gather, status.Segment);
        Assert.Equal(Observed().Map.MapId, status.MapId);
        Assert.Equal(Observed().Map.Generation, status.MapGeneration);
        Assert.False(restarted.TryReserve(scope, Gather, Observed()).MayDispatch);
        Assert.False(restarted.TryReserve(scope, Move, Observed()).MayDispatch);
    }

    [Fact]
    public void Character_and_server_scope_survive_pid_reuse_without_shared_pid_key()
    {
        using var root = new TestRoot();
        var store = root.Open();
        Assert.True(store.TryReserve(CharacterA, Move, Observed()).MayDispatch);
        // A new pid running the SAME identity must see the exact existing marker.
        Assert.Equal(DurableState.Pending, root.Open().ReadStatus(CharacterA).State);
        Assert.False(root.Open().TryReserve(CharacterA, Gather, Observed()).MayDispatch);
        // PID reuse for a genuinely distinct server/character must not inherit this marker.
        Assert.Equal(DurableState.Clear, root.Open().ReadStatus(CharacterB).State);
        Assert.Equal(DurableState.Clear, root.Open().ReadStatus(OtherServer).State);
        Assert.True(root.Open().TryReserve(CharacterB, Gather, Observed()).MayDispatch);
        Assert.True(root.Open().TryReserve(OtherServer, Move, Observed()).MayDispatch);
        Assert.Equal(3, new[] { CharacterA, CharacterB, OtherServer }.Count(s =>
            root.Open().ReadStatus(s).State == DurableState.Pending));
    }

    [Fact]
    public async Task Exclusive_cross_instance_create_new_admits_only_one_concurrent_sender()
    {
        using var root = new TestRoot();
        var attempts = Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            root.Open().TryReserve(CharacterA, Move, Observed()))).ToArray();
        var results = await Task.WhenAll(attempts);
        Assert.Single(results.Where(r => r.MayDispatch));
        Assert.All(results.Where(r => !r.MayDispatch), r => Assert.True(r.Status.IsQuarantined));
        Assert.Equal(DurableState.Pending, root.Open().ReadStatus(CharacterA).State);
    }

    [Fact]
    public void Empty_file_from_crash_during_marker_create_remains_a_quarantine()
    {
        using var root = new TestRoot();
        string marker = root.Marker(CharacterA);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(marker)!);
        using (new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough)) { }

        var restarted = root.Open();
        Assert.Equal(DurableState.Corrupt, restarted.ReadStatus(CharacterA).State);
        Assert.False(restarted.TryReserve(CharacterA, Move, Observed()).MayDispatch);
        Assert.Equal(0, new FileInfo(marker).Length); // unreadable original preserved byte-for-byte
    }

    [Fact]
    public void Partial_fsynced_marker_is_not_considered_a_clear_start()
    {
        using var root = new TestRoot();
        string marker = root.Marker(CharacterA);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(marker)!);
        byte[] prefix = { 0x41, 0x52, 0x43, 0x2D };
        using (var fs = new FileStream(marker, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Write(prefix);
            fs.Flush(flushToDisk: true);
        }
        Assert.Equal(DurableState.Corrupt, root.Open().ReadStatus(CharacterA).State);
        Assert.False(root.Open().TryReserve(CharacterA, Gather, Observed()).MayDispatch);
        Assert.Equal(prefix, File.ReadAllBytes(marker)); // never rewrite or delete corrupt evidence
    }

    [Fact]
    public void Corrupted_digest_after_restart_blocks_send_and_preserves_bad_bytes()
    {
        using var root = new TestRoot();
        Assert.True(root.Open().TryReserve(CharacterA, Move, Observed()).MayDispatch);
        string marker = root.Marker(CharacterA);
        byte[] damaged = File.ReadAllBytes(marker);
        damaged[damaged.Length / 2] ^= 0x7f;
        File.WriteAllBytes(marker, damaged);
        var reopened = root.Open();
        Assert.Equal(DurableState.Corrupt, reopened.ReadStatus(CharacterA).State);
        Assert.False(reopened.TryReserve(CharacterA, Move, Observed()).MayDispatch);
        Assert.Equal(damaged, File.ReadAllBytes(marker));
    }

    [Fact]
    public void Exclusive_open_in_another_process_window_is_busy_and_blocks()
    {
        using var root = new TestRoot();
        Assert.True(root.Open().TryReserve(CharacterA, Move, Observed()).MayDispatch);
        using (var locked = new FileStream(root.Marker(CharacterA), FileMode.Open,
            FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(DurableState.Busy, root.Open().ReadStatus(CharacterA).State);
            Assert.False(root.Open().TryReserve(CharacterA, Move, Observed()).MayDispatch);
        }
        Assert.Equal(DurableState.Pending, root.Open().ReadStatus(CharacterA).State);
    }

    [Fact]
    public void Unwritable_data_root_must_not_fall_back_to_an_in_memory_send()
    {
        using var root = new TestRoot();
        Directory.CreateDirectory(root.Path);
        string occupiedFile = System.IO.Path.Combine(root.Path, "not-a-directory");
        File.WriteAllText(occupiedFile, "keep");
        var store = new DurableStore(occupiedFile);
        var admission = store.TryReserve(CharacterA, Move, Observed());
        Assert.False(admission.MayDispatch);
        Assert.Equal(DurableState.StorageUnavailable, admission.Status.State);
        Assert.Equal("keep", File.ReadAllText(occupiedFile));
    }

    [Fact]
    public void Missing_identity_or_unconfirmed_position_denies_without_creating_marker()
    {
        using var root = new TestRoot();
        var store = root.Open();
        foreach (var identity in new[] { new DurableScope(0, 11), new DurableScope(292, 0) })
        {
            Assert.Equal(DurableState.IdentityMissing, store.ReadStatus(identity).State);
            Assert.False(store.TryReserve(identity, Move, Observed()).MayDispatch);
        }
        Assert.False(store.TryReserve(CharacterA, Move,
            Observed() with { PositionConfirmed = false }).MayDispatch);
        Assert.False(store.TryReserve(CharacterA, Gather,
            Observed() with { GeometryMap = 0 }).MayDispatch);
        Assert.Equal(DurableState.Clear, store.ReadStatus(CharacterA).State);
    }

    [Fact]
    public void Invalid_target_or_element_identity_never_creates_admission_marker()
    {
        using var root = new TestRoot();
        var store = root.Open();
        var invalid = new[]
        {
            new RecoverySegment(RecoveryKind.Cell, -1),
            new RecoverySegment(RecoveryKind.Cell, 560),
            new RecoverySegment(RecoveryKind.Interaction, 100, Cell: 241, ElementId: 101),
            new RecoverySegment(RecoveryKind.Gather, 127, Cell: -1, ElementId: 127),
            new RecoverySegment(RecoveryKind.MapTransition, 0),
            new RecoverySegment((RecoveryKind)99, 100)
        };
        foreach (var segment in invalid)
            Assert.False(store.TryReserve(CharacterA, segment, Observed()).MayDispatch);
        Assert.False(File.Exists(root.Marker(CharacterA)));
        Assert.Equal(DurableState.Clear, store.ReadStatus(CharacterA).State);
    }

    [Fact]
    public void Exact_data_root_is_isolated_and_does_not_modify_unrelated_files()
    {
        using var rootA = new TestRoot();
        using var rootB = new TestRoot();
        Assert.True(rootA.Open().TryReserve(CharacterA, Move, Observed()).MayDispatch);
        Assert.Equal(DurableState.Pending, rootA.Open().ReadStatus(CharacterA).State);
        Assert.Equal(DurableState.Clear, rootB.Open().ReadStatus(CharacterA).State);
        Assert.True(rootB.Open().TryReserve(CharacterA, Move, Observed()).MayDispatch);
        Assert.True(File.Exists(rootA.Marker(CharacterA)));
        Assert.True(File.Exists(rootB.Marker(CharacterA)));
    }

    [Fact]
    public void Sequence_confirmed_in_memory_never_clears_persisted_quarantine()
    {
        using var root = new TestRoot();
        var snapshot = Observed();
        Assert.True(root.Open().TryReserve(CharacterA, Move, snapshot).MayDispatch);
        var wf = RecoveryWorkflow.Start(snapshot.Map.MapId);
        var probe = wf.Evaluate(Move, snapshot, _ => true);
        var pending = wf.Begin(probe, snapshot, realSequence: 1234, walkable: _ => true);
        Assert.True(pending.IsQuarantined);
        Assert.False(pending.ConfirmCorrelatedAck(1234,
            snapshot with { ServerCell = 320 }).IsQuarantined);
        // There is deliberately NO durable release API: UI should still show pending.
        Assert.Equal(DurableState.Pending, root.Open().ReadStatus(CharacterA).State);
        Assert.False(root.Open().TryReserve(CharacterA, Move, snapshot).MayDispatch);
    }

    [Fact]
    public void Current_readonly_status_is_safe_for_simultaneous_ui_readers()
    {
        using var root = new TestRoot();
        Assert.True(root.Open().TryReserve(CharacterA, Move, Observed()).MayDispatch);
        Parallel.For(0, 64, _ =>
        {
            var snapshot = root.Open().ReadStatus(CharacterA);
            Assert.Equal(DurableState.Pending, snapshot.State);
            Assert.True(snapshot.IsQuarantined);
        });
    }
}
