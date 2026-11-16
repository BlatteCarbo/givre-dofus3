extern alias F06App;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using Xunit;
using DurableStore = F06App::GivreClone.App.Services.ActivityRecoveryDurableStore;
using DurableState = F06App::GivreClone.App.Services.ActivityRecoveryDurableState;
using RecoveryEvidence = F06App::GivreClone.App.Services.ActivityRecoveryReconciliationEvidence;
using RecoveryKind = F06App::GivreClone.App.Services.ActivityRecoveryKind;
using RecoverySegment = F06App::GivreClone.App.Services.ActivityRecoverySegment;
using SupplyReview = F06App::GivreClone.App.Services.ActivitySupplyContinuityReview;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using MapReader = F06App::GivreClone.App.Services.MapCellReader;
using WorldStore = F06App::GivreClone.App.Services.BotWorldStore;
using ViewModel = F06App::GivreClone.App.ViewModels.BotsViewModel;

namespace GivreClone.Tests;

/// <summary>
/// Full MoonSharp RunLoop -> RunRoute/GatherOne -> fsynced ActivityRecovery
/// entrypoints. Only the would-send boundary is captured after admission:
/// no fabricated game ACK, packet, full-inventory observation or live client.
/// </summary>
public sealed class ActivityRecoveryCausalV17Tests
{
    private const uint Pid = 71701;
    private const int Server = 9971;
    private const long Character = 9065400171;
    private const long Map = 90703360;
    private const int Self = 240;
    private const int Target = 320;
    private const int Element = 45171;
    private const int ResourceType = 38;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static FieldInfo Field(Type type, string name) =>
        type.GetField(name, Private) ??
        throw new InvalidOperationException("V17 real owner field missing: " + name);

    private static string Generated(bool gather = true, long map = Map)
    {
        Assert.True(ViewModel.TryBuildStrictActivityRouteScript(map,
            Target, gather, 95, out string lua, out string reason), reason);
        Assert.Contains("ACTIVITY_RECOVERY_TABLE_ONLY = true", lua);
        return lua;
    }

    private sealed class Journey : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "GivreClone-ActivityRecoveryCausalV17",
            Guid.NewGuid().ToString("N"));
        public Launch Launch { get; }
        public WorldStore World { get; }
        public F06App::GivreClone.App.Services.BotWorldState State => World.Get(Pid);
        public Host Host { get; }
        public DurableStore Store { get; }
        public ConcurrentQueue<RecoverySegment> Attempts { get; } = new();
        public ConcurrentQueue<string> Logs { get; } = new();
        public int ResourceCell { get; }
        private readonly TaskCompletionSource<RecoverySegment> _first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Journey()
        {
            Launch = new Launch();
            Store = new DurableStore(Root);
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_charId")
                .GetValue(Launch)!)[Pid] = Character;
            ((ConcurrentDictionary<uint, (int Id, string Name)>)Field(
                typeof(Launch), "_serverInfo").GetValue(Launch)!)[Pid] =
                    (Server, "offline");
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_lastMap")
                .GetValue(Launch)!)[Pid] = Map;
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_interactivesDecodedMap")
                .GetValue(Launch)!)[Pid] = Map;

            var reader = (MapReader)RuntimeHelpers.GetUninitializedObject(typeof(MapReader));
            ResourceCell = Enumerable.Range(0, MapPathfinder.CellCount)
                .First(c => MapPathfinder.InteractionAdjacent(Self, c));
            Field(typeof(MapReader), "_interPosCache").SetValue(reader,
                new ConcurrentDictionary<long, IReadOnlyDictionary<int, int>>(
                    new[] { new KeyValuePair<long, IReadOnlyDictionary<int, int>>(
                        Map, new Dictionary<int, int> { [Element] = ResourceCell }) }));
            Field(typeof(Launch), "_cellReader").SetValue(Launch, reader);

            World = new WorldStore(Launch);
            State.MapId = Map;
            State.MapGeneration = 17;
            State.MapX = 0;
            State.MapY = 0;
            State.SelfCell = Self;
            State.SelfCellMap = Map;
            State.SelfCellConfirmed = true;
            State.SelfActorId = Character;
            State.Cells = Enumerable.Range(0, MapPathfinder.CellCount)
                .Select(c => new MapCell(c, Mov: true, Los: true, Floor: 0,
                    MoveZone: 1, LinkedZone: 16)).ToArray();
            State.CellsMap = Map;
            State.RebuildWalk();
            State.Actors = new[] {
                new MapActor(Self, 0, Character, "V17 offline actor", MapActorKind.Player)
            };
            State.PodsAuthoritative = true;
            State.Pods = (10, 100);

            Host = new Host(Pid, Launch, World);
            Host.ActivityRecoveryOfflineStoreForTests = Store;
            Host.ActivityRecoveryOfflineGatherAllowedForTests = true;
            Host.ActivityRecoveryOfflineDispatchForTests = Capture;
            Host.Log += entry => Logs.Enqueue(entry.Message);
        }

        public void Capture(RecoverySegment segment)
        {
            Attempts.Enqueue(segment);
            _first.TrySetResult(segment);
        }

        public void Resource(bool available = true) =>
            State.Interactives = new[] {
                new MapInteractive(ResourceCell, Element, ResourceType, 0,
                    new[] { new InteractiveSkill(125, 45, 9, available) }, "Blé", "Récolter")
            };

        public void Absent() => State.Interactives = Array.Empty<MapInteractive>();

        public void MoveToAnotherMap()
        {
            const long next = Map + 1;
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_lastMap")
                .GetValue(Launch)!)[Pid] = next;
            State.MapId = next;
            State.MapGeneration = 18;
            State.SelfCellMap = next;
            State.CellsMap = next;
            State.SelfCellConfirmed = true;
            Absent();
        }

        public Task<RecoverySegment> FirstAsync() =>
            _first.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public string Marker => Path.Combine(Root, "server-" + Server,
            "character-" + Character + ".pending");

        public async Task StopAsync()
        {
            Host.Stop("user");
            await Host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public void Dispose()
        {
            Host.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public async Task Actual_lua_harvest_owns_exact_segment_and_read_only_next_step()
    {
        using var j = new Journey();
        j.Resource();
        j.Host.Start(Generated());
        try
        {
            Assert.Equal(RecoveryKind.Gather, (await j.FirstAsync()).Kind);
            byte[] marker = File.ReadAllBytes(j.Marker);
            var status = j.Host.RefreshActivityRecoveryCausalNextStepStatus();
            Assert.Equal(DurableState.Pending, status.DurableStatus.State);
            Assert.Equal(RecoveryKind.Gather, status.RecordedSegment?.Kind);
            Assert.Equal(Element, status.RecordedSegment?.ElementId);
            Assert.Equal(Map, status.DurableStatus.MapId);
            Assert.Equal(SupplyReview.InspectDurableAttempt, status.NextReview);
            Assert.False(status.SourceProvenanceVerified);
            Assert.False(status.HasSegmentAttributedServerReceipt);
            Assert.False(status.MayAutoClear);
            Assert.False(status.MayAutoDispatch);
            Assert.Equal(status, j.Host.ActivityRecoveryCausalNextStepStatus);
            Assert.Equal(marker, File.ReadAllBytes(j.Marker));
        }
        finally { await j.StopAsync(); }
        Assert.Single(j.Attempts);
    }

    [Fact]
    public async Task Actual_lua_waypoint_cell_preserves_exact_durable_target_without_a_move_ack()
    {
        using var j = new Journey();
        j.Absent();
        j.Host.Start(Generated(gather: false));
        try
        {
            var attempted = await j.FirstAsync();
            Assert.Equal(RecoveryKind.Cell, attempted.Kind);
            Assert.Equal(Target, attempted.Target);
            var status = j.Host.RefreshActivityRecoveryCausalNextStepStatus();
            Assert.Equal(DurableState.Pending, status.DurableStatus.State);
            Assert.Equal(RecoveryKind.Cell, status.RecordedSegment?.Kind);
            Assert.Equal(Target, status.RecordedSegment?.Target);
            Assert.Equal(RecoveryEvidence.SourceMapStillObserved, status.Observation);
            Assert.Equal(SupplyReview.InspectDurableAttempt, status.NextReview);
            Assert.False(status.HasSegmentAttributedServerReceipt);
            Assert.True(File.Exists(j.Marker));
        }
        finally { await j.StopAsync(); }
        Assert.Single(j.Attempts);
    }

    [Theory]
    [InlineData(0, RecoveryEvidence.ResourceExhaustedWithoutAttribution)]
    [InlineData(1, RecoveryEvidence.ResourceAbsentWithoutAttribution)]
    [InlineData(2, RecoveryEvidence.MapChangedWithoutAttribution)]
    public async Task Depleted_absent_or_changed_map_never_attributes_prior_segment(
        int observed, RecoveryEvidence expected)
    {
        using var j = new Journey();
        j.Resource();
        j.Host.Start(Generated());
        try
        {
            await j.FirstAsync();
            if (observed == 0) j.Resource(available: false);
            else if (observed == 1) j.Absent();
            else j.MoveToAnotherMap();
            byte[] before = File.ReadAllBytes(j.Marker);
            var status = j.Host.RefreshActivityRecoveryCausalNextStepStatus();
            Assert.Equal(expected, status.Observation);
            Assert.Equal(DurableState.Pending, status.DurableStatus.State);
            Assert.Equal(RecoveryKind.Gather, status.RecordedSegment?.Kind);
            Assert.Equal(SupplyReview.InspectDurableAttempt, status.NextReview);
            Assert.False(status.HasSegmentAttributedServerReceipt);
            Assert.Equal(before, File.ReadAllBytes(j.Marker));
        }
        finally { await j.StopAsync(); }
        Assert.Single(j.Attempts);
    }

    [Fact]
    public async Task Removed_opt_in_on_restart_cannot_replay_through_legacy_route()
    {
        using var j = new Journey();
        j.Resource();
        j.Host.Start(Generated());
        await j.FirstAsync();
        await j.StopAsync();
        var originalMarker = File.ReadAllBytes(j.Marker);
        j.Host.Start("function move()\n" +
            "  printMessage('V17_LEGACY_MOVE_CALLED')\n" +
            "  return {{ map = " + Map + ", cell = " + Target + " }}\n" +
            "end\n");
        try
        {
            await j.Host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Single(j.Attempts);
            Assert.DoesNotContain(j.Logs,
                msg => msg.Contains("V17_LEGACY_MOVE_CALLED", StringComparison.Ordinal));
            Assert.Equal(originalMarker, File.ReadAllBytes(j.Marker));
            Assert.Equal(DurableState.Pending,
                j.Host.RefreshActivityRecoveryCausalNextStepStatus().DurableStatus.State);
        }
        finally { await j.StopAsync(); }
    }

    [Theory]
    [InlineData(false, 10, 100)]
    [InlineData(true, 96, 100)]
    public async Task Missing_or_full_pods_block_real_Lua_route_before_durable_reservation(
        bool authoritative, int used, int capacity)
    {
        using var j = new Journey();
        j.Resource();
        j.State.PodsAuthoritative = authoritative;
        j.State.Pods = (used, capacity);
        j.Host.Start(Generated());
        await j.Host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(j.Attempts);
        Assert.False(File.Exists(j.Marker));
        var inspect = j.Host.RefreshActivityRecoveryCausalNextStepStatus();
        Assert.False(inspect.HasSegmentAttributedServerReceipt);
        Assert.False(inspect.MayAutoDispatch);
    }

    [Fact]
    public async Task Route_map_not_matched_does_not_teleport_or_create_durable_attempt()
    {
        using var j = new Journey();
        j.Resource();
        j.Host.Start(Generated(map: Map + 1));
        await j.Host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(j.Attempts);
        Assert.False(File.Exists(j.Marker));
    }

    [Fact]
    public async Task Corrupted_marker_after_harvest_remains_quarantined_with_no_inferred_target()
    {
        using var j = new Journey();
        j.Resource();
        j.Host.Start(Generated());
        await j.FirstAsync();
        await j.StopAsync();
        byte[] corrupt = File.ReadAllBytes(j.Marker);
        corrupt[corrupt.Length / 2] ^= 0x44;
        File.WriteAllBytes(j.Marker, corrupt);

        var observed = j.Host.RefreshActivityRecoveryCausalNextStepStatus();
        Assert.Equal(DurableState.Corrupt, observed.DurableStatus.State);
        Assert.Equal(RecoveryEvidence.MarkerCorrupt, observed.Observation);
        Assert.Null(observed.RecordedSegment);
        Assert.Equal(SupplyReview.InspectDurableAttempt, observed.NextReview);
        Assert.True(observed.IsBlocked);
        Assert.False(observed.MayAutoClear);
        Assert.Equal(corrupt, File.ReadAllBytes(j.Marker));
        Assert.Single(j.Attempts);
    }

    [Fact]
    public async Task Replacement_actor_cannot_inherit_previous_pendings_next_step()
    {
        using var j = new Journey();
        j.Resource();
        j.Host.Start(Generated());
        await j.FirstAsync();
        await j.StopAsync();
        j.State.SelfActorId = Character + 1;

        var observed = j.Host.RefreshActivityRecoveryCausalNextStepStatus();
        Assert.Equal(DurableState.IdentityMissing, observed.DurableStatus.State);
        Assert.Equal(RecoveryEvidence.IdentityUnconfirmed, observed.Observation);
        Assert.Null(observed.RecordedSegment);
        Assert.Equal(SupplyReview.VerifyIdentityAndSource, observed.NextReview);
        Assert.False(observed.SourceProvenanceVerified);
        Assert.False(observed.MayAutoDispatch);
        // Previous actor's fsynced marker remains on disk for independent review.
        Assert.True(File.Exists(j.Marker));
        Assert.Equal(DurableState.Pending,
            j.Store.ReadStatus(new(Server, Character)).State);
    }

    [Fact]
    public async Task Late_old_gather_callback_must_not_cancel_replacement_run()
    {
        using var j = new Journey();
        j.Resource();
        int stuckNotifications = 0;
        bool replacementAlive = false;
        var replacement = new CancellationTokenSource();
        j.Host.StuckStopped += () => Interlocked.Increment(ref stuckNotifications);
        j.Host.ActivityRecoveryOfflineDispatchForTests = segment =>
        {
            j.Capture(segment);
            var ctsField = Field(typeof(Host), "_cts");
            var old = (CancellationTokenSource)ctsField.GetValue(j.Host)!;
            var genField = Field(typeof(Host), "_runGen");
            ctsField.SetValue(j.Host, replacement);
            genField.SetValue(j.Host, (int)genField.GetValue(j.Host)! + 1);
            typeof(Host).GetMethod("StopRecoveryPlanningOnce", Private)!
                .Invoke(j.Host, new object[] { old.Token });
            replacementAlive = ReferenceEquals(ctsField.GetValue(j.Host), replacement) &&
                !replacement.IsCancellationRequested;
            old.Cancel();
        };
        j.Host.Start(Generated());
        await j.FirstAsync();
        await j.Host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(replacementAlive);
        Assert.Equal(0, Volatile.Read(ref stuckNotifications));
        Assert.Single(j.Attempts);
        Assert.Equal(DurableState.Pending,
            j.Store.ReadStatus(new(Server, Character)).State);
        j.Host.Stop("user");
        replacement.Dispose();
    }
}
