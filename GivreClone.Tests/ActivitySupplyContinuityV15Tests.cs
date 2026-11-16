extern alias F06App;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using GivreClone.Protocol;
using Xunit;
using DurableStore = F06App::GivreClone.App.Services.ActivityRecoveryDurableStore;
using DurableState = F06App::GivreClone.App.Services.ActivityRecoveryDurableState;
using RecoveryKind = F06App::GivreClone.App.Services.ActivityRecoveryKind;
using RecoverySegment = F06App::GivreClone.App.Services.ActivityRecoverySegment;
using RecoveryEvidence = F06App::GivreClone.App.Services.ActivityRecoveryReconciliationEvidence;
using SupplyReview = F06App::GivreClone.App.Services.ActivitySupplyContinuityReview;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using MapReader = F06App::GivreClone.App.Services.MapCellReader;
using WorldStore = F06App::GivreClone.App.Services.BotWorldStore;
using ViewModel = F06App::GivreClone.App.ViewModels.BotsViewModel;

namespace GivreClone.Tests;

/// <summary>
/// MoonSharp through the actual desktop generator → GivreLuaHost.Start →
/// RunLoop table-only → RunRoute/GatherOne → fsynced durable admission.
/// Only the final would-send boundary is intercepted; never launch DOFUS.
/// </summary>
public sealed class ActivitySupplyContinuityV15Tests
{
    private const uint Pid = 71501;
    private const int Server = 9945;
    private const long Character = 9065400031;
    private const long Map = 90703360;
    private const int SelfCell = 240;
    private const int TargetCell = 320;
    private const int Element = 45151;
    private const int ResourceType = 38;
    private const long Gid = 2416;
    private const int Required = 10;
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    private static FieldInfo Field(Type type, string name) =>
        type.GetField(name, Fields) ??
        throw new InvalidOperationException("V15 offline fixture missing " + name);

    private static string Generated(bool gather = true, long map = Map, int maxPods = 95)
    {
        Assert.True(ViewModel.TryBuildStrictActivityRouteScript(
            map, TargetCell, gather, maxPods, out var script, out var reason), reason);
        Assert.Contains("ACTIVITY_RECOVERY_TABLE_ONLY = true", script);
        Assert.Contains("ACTIVITY_RECOVERY_ROUTE", script);
        Assert.DoesNotContain("function move()", script);
        Assert.DoesNotContain("function bank()", script);
        return script;
    }

    private sealed class Journey : IDisposable
    {
        private readonly bool _ownedRoot;
        public string Root { get; }
        public Launch Launch { get; }
        public WorldStore World { get; }
        public F06App::GivreClone.App.Services.BotWorldState State => World.Get(Pid);
        public Host Host { get; }
        public DurableStore Durable { get; }
        public ConcurrentQueue<RecoverySegment> Sent { get; } = new();
        public ConcurrentQueue<string> Logs { get; } = new();
        private readonly TaskCompletionSource<RecoverySegment> _sent =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _resourceCell;

        public Journey(string? root = null)
        {
            _ownedRoot = root is null;
            Root = root ?? Path.Combine(Path.GetTempPath(),
                "GivreClone-ActivitySupplyContinuityV15",
                Guid.NewGuid().ToString("N"));
            Durable = new DurableStore(Root);
            Launch = new Launch();
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
            _resourceCell = Enumerable.Range(0, MapPathfinder.CellCount)
                .First(c => MapPathfinder.InteractionAdjacent(SelfCell, c));
            Field(typeof(MapReader), "_interPosCache").SetValue(reader,
                new ConcurrentDictionary<long, IReadOnlyDictionary<int, int>>(
                    new[] {
                        new KeyValuePair<long, IReadOnlyDictionary<int, int>>(
                            Map, new Dictionary<int, int> { [Element] = _resourceCell })
                    }));
            Field(typeof(Launch), "_cellReader").SetValue(Launch, reader);

            World = new WorldStore(Launch);
            State.MapId = Map;
            State.MapGeneration = 12;
            State.MapX = 0;
            State.MapY = 0;
            State.SelfCell = SelfCell;
            State.SelfCellMap = Map;
            State.SelfCellConfirmed = true;
            State.SelfActorId = Character;
            State.Cells = Enumerable.Range(0, MapPathfinder.CellCount)
                .Select(c => new MapCell(c, Mov: true, Los: true, Floor: 0,
                    MoveZone: 1, LinkedZone: 16)).ToArray();
            State.CellsMap = Map;
            State.RebuildWalk();
            State.Actors = new[] {
                new MapActor(SelfCell, 0, Character, "V15 offline actor", MapActorKind.Player)
            };
            State.PodsAuthoritative = true;
            State.Pods = (10, 100);

            Host = new Host(Pid, Launch, World);
            Host.ActivityRecoveryOfflineStoreForTests = Durable;
            Host.ActivityRecoveryOfflineGatherAllowedForTests = true;
            Host.ActivityRecoveryOfflineDispatchForTests = segment =>
            {
                Sent.Enqueue(segment);
                _sent.TrySetResult(segment);
            };
            Host.Log += log => Logs.Enqueue(log.Message);
        }

        public void Resource(bool available = true)
        {
            State.Interactives = new[] {
                new MapInteractive(_resourceCell, Element, ResourceType, 0,
                    new[] { new InteractiveSkill(125, 45, 9, available) },
                    "Blé", "Récolter")
            };
        }

        public void Absent() => State.Interactives = Array.Empty<MapInteractive>();

        public void ChangedMap()
        {
            const long next = Map + 1;
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_lastMap")
                .GetValue(Launch)!)[Pid] = next;
            State.MapId = next;
            State.MapGeneration = 13;
            State.CellsMap = next;
            State.SelfCellMap = next;
            State.SelfCellConfirmed = true;
            Absent();
        }

        public Task<RecoverySegment> FirstSendAsync() =>
            _sent.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public F06App::GivreClone.App.Services.ActivitySupplyContinuityStatus Inspect() =>
            Host.RefreshActivitySupplyContinuityStatus(Gid, Required);

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
            if (_ownedRoot && Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public async Task Harvest_from_generated_Lua_has_durable_pending_and_only_inspection_next()
    {
        using var j = new Journey();
        j.Resource();
        j.Host.Start(Generated());
        try
        {
            Assert.Equal(RecoveryKind.Gather, (await j.FirstSendAsync()).Kind);
            var continuity = j.Inspect();
            Assert.Equal(DurableState.Pending, continuity.DurableStatus.State);
            Assert.True(continuity.RequiresIndependentReconciliation);
            Assert.Equal(SupplyReview.InspectDurableAttempt, continuity.NextReview);
            Assert.False(continuity.MayAutoDispatch);
            Assert.False(continuity.MayAutoClear);
            Assert.Null(continuity.BagObserved); // no authenticated link/decoder
            Assert.True(File.Exists(j.Marker));
        }
        finally { await j.StopAsync(); }
        Assert.Single(j.Sent);
    }

    [Theory]
    [InlineData(false, RecoveryEvidence.ResourceExhaustedWithoutAttribution)]
    [InlineData(true, RecoveryEvidence.ResourceAbsentWithoutAttribution)]
    public async Task Depleted_or_absent_resource_stays_quarantined_without_implicit_next_step(
        bool absent, RecoveryEvidence evidence)
    {
        using var j = new Journey();
        j.Resource();
        j.Host.Start(Generated());
        try
        {
            await j.FirstSendAsync();
            if (absent) j.Absent(); else j.Resource(available: false);
            var continuity = j.Inspect();
            Assert.Equal(evidence, continuity.Evidence);
            Assert.Equal(SupplyReview.InspectDurableAttempt, continuity.NextReview);
            Assert.True(continuity.RequiresIndependentReconciliation);
            Assert.False(continuity.MayAutoDispatch);
            Assert.True(File.Exists(j.Marker));
        }
        finally { await j.StopAsync(); }
        Assert.Single(j.Sent);
    }

    [Fact]
    public async Task Map_change_after_durable_harvest_preserves_marker_and_prevents_rejoin()
    {
        using var j = new Journey();
        j.Resource();
        j.Host.Start(Generated());
        try
        {
            await j.FirstSendAsync();
            j.ChangedMap();
            var status = j.Inspect();
            Assert.Equal(RecoveryEvidence.MapChangedWithoutAttribution, status.Evidence);
            Assert.Equal(DurableState.Pending, status.DurableStatus.State);
            Assert.Equal(SupplyReview.InspectDurableAttempt, status.NextReview);
            Assert.True(File.Exists(j.Marker));
        }
        finally { await j.StopAsync(); }
        Assert.Single(j.Sent);
    }

    [Fact]
    public async Task Generated_route_without_resources_uses_only_one_durable_cell()
    {
        using var j = new Journey();
        j.Absent();
        j.Host.Start(Generated());
        try
        {
            var segment = await j.FirstSendAsync();
            Assert.Equal(RecoveryKind.Cell, segment.Kind);
            Assert.Equal(TargetCell, segment.Target);
            Assert.Equal(DurableState.Pending, j.Inspect().DurableStatus.State);
        }
        finally { await j.StopAsync(); }
        Assert.Single(j.Sent);
    }

    [Fact]
    public async Task Unmatched_route_map_is_not_a_legacy_travel_fallback()
    {
        using var j = new Journey();
        j.Resource();
        j.Host.Start(Generated(map: Map + 1));
        await j.Host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(j.Sent);
        Assert.False(File.Exists(j.Marker));
        Assert.False(j.Inspect().MayAutoDispatch);
    }

    [Fact]
    public async Task Full_pods_park_generated_route_without_bank_or_durable_reservation()
    {
        using var j = new Journey();
        j.Resource();
        j.State.Pods = (95, 100);
        j.Host.Start(Generated() + """

            function move() error('V15_UNSAFE_MOVE') end
            function bank() error('V15_UNSAFE_BANK') end
            """);
        await j.Host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(j.Sent);
        Assert.False(File.Exists(j.Marker));
        Assert.DoesNotContain(j.Logs, msg => msg.Contains("V15_UNSAFE_"));
        var observed = j.Inspect();
        Assert.False(observed.MayAutoDispatch);
        Assert.False(observed.SourceVerified);
    }

    [Fact]
    public async Task Stopped_then_restarted_Lua_cannot_replay_first_ambiguous_gather()
    {
        using var j = new Journey();
        j.Resource();
        j.Host.Start(Generated());
        await j.FirstSendAsync();
        await j.StopAsync();
        Assert.True(File.Exists(j.Marker));
        j.Host.Start(Generated());
        await j.StopAsync();
        Assert.Single(j.Sent);
        Assert.Equal(DurableState.Pending, j.Inspect().DurableStatus.State);
        Assert.Equal(false, j.Host.ActivitySupplyContinuityStatus?.MayAutoClear);
    }

    [Fact]
    public async Task Foreign_actor_before_first_admission_is_not_a_source_identity()
    {
        using var j = new Journey();
        j.Resource();
        j.State.SelfActorId = Character + 1;
        j.Host.Start(Generated());
        try
        {
            await Task.Delay(280);
            Assert.Empty(j.Sent);
            Assert.False(File.Exists(j.Marker));
            var continuity = j.Inspect();
            Assert.Equal(SupplyReview.VerifyIdentityAndSource, continuity.NextReview);
            Assert.False(continuity.MayAutoDispatch);
            Assert.Null(continuity.BagObserved);
        }
        finally { await j.StopAsync(); }
    }

    [Fact]
    public async Task Replacement_actor_and_late_completion_cannot_turn_old_marker_clear()
    {
        using var j = new Journey();
        j.Resource();
        j.Host.ActivityRecoveryOfflineDispatchForTests = segment =>
        {
            j.Sent.Enqueue(segment);
            j.State.SelfActorId = Character + 1;
            j.Absent();
        };
        j.Host.Start(Generated());
        try
        {
            await Task.Delay(350);
            Assert.Single(j.Sent);
            var unconfirmed = j.Inspect();
            Assert.False(unconfirmed.MayAutoDispatch);
            Assert.Equal(SupplyReview.VerifyIdentityAndSource, unconfirmed.NextReview);
            Assert.True(File.Exists(j.Marker));
            Assert.Equal(DurableState.Pending,
                j.Durable.ReadStatus(new(Server, Character)).State);
        }
        finally { await j.StopAsync(); }
    }

    [Fact]
    public async Task Old_generation_finally_cannot_publish_an_effect_for_new_run()
    {
        using var j = new Journey();
        j.Resource();
        var generation = Field(typeof(Host), "_runGen");
        j.Host.ActivityRecoveryOfflineDispatchForTests = segment =>
        {
            j.Sent.Enqueue(segment);
            // The actual Lua RunLoop was entered. Emulate a newer generation
            // arriving during the old run's would-send callback. No ACK exists.
            generation.SetValue(j.Host,
                (int)generation.GetValue(j.Host)! + 1);
            j.Resource(available: false);
        };
        j.Host.Start(Generated());
        try
        {
            await Task.Delay(350);
            Assert.Single(j.Sent);
            Assert.True(File.Exists(j.Marker));
            Assert.Equal(DurableState.Pending,
                j.Durable.ReadStatus(new(Server, Character)).State);
            Assert.NotEqual(true,
                j.Host.ActivityRecoveryStatus?.ObservedEffectWithoutAck);
            Assert.False(j.Inspect().MayAutoDispatch);
        }
        finally { await j.StopAsync(); }
    }
}
