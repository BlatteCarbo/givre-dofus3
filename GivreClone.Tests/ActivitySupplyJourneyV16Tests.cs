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
/// Real MoonSharp RunLoop -> RunRoute -> GatherOne admissions. The offline
/// boundary records a would-send only AFTER fsynced TryReserve; no gameplay
/// traffic, movement ACK or inventory receipt is fabricated by these tests.
/// </summary>
public sealed class ActivitySupplyJourneyV16Tests
{
    private const uint Pid = 71601;
    private const int Server = 9961;
    private const long Character = 9065400161;
    private const long Map = 90703360;
    private const int SelfCell = 240;
    private const int TargetCell = 320;
    private const int Element = 45161;
    private const int ResourceType = 38;
    private const long Gid = 2416;
    private const int RequiredQuantity = 10;
    private const BindingFlags NonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

    private static FieldInfo Field(Type t, string name) => t.GetField(name, NonPublic)
        ?? throw new InvalidOperationException("missing real host field " + name);

    private static string Script(bool gather = true, long map = Map, int maxPods = 95)
    {
        Assert.True(ViewModel.TryBuildStrictActivityRouteScript(
            map, TargetCell, gather, maxPods, out var script, out var reason), reason);
        Assert.Contains("ACTIVITY_RECOVERY_TABLE_ONLY = true", script);
        return script;
    }

    private sealed class Journey : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "GivreClone-ActivitySupplyJourneyV16", Guid.NewGuid().ToString("N"));
        public Launch Launch { get; }
        public WorldStore World { get; }
        public F06App::GivreClone.App.Services.BotWorldState State => World.Get(Pid);
        public Host Host { get; }
        public DurableStore Store { get; }
        public ConcurrentQueue<RecoverySegment> Sent { get; } = new();
        public ConcurrentQueue<string> Logs { get; } = new();
        private readonly TaskCompletionSource<RecoverySegment> _first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _resourceCell;

        public Journey()
        {
            Store = new DurableStore(Root);
            Launch = new Launch();
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_charId")
                .GetValue(Launch)!)[Pid] = Character;
            ((ConcurrentDictionary<uint, (int Id, string Name)>)Field(
                typeof(Launch), "_serverInfo").GetValue(Launch)!)[Pid] = (Server, "offline");
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_lastMap")
                .GetValue(Launch)!)[Pid] = Map;
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_interactivesDecodedMap")
                .GetValue(Launch)!)[Pid] = Map;

            var reader = (MapReader)RuntimeHelpers.GetUninitializedObject(typeof(MapReader));
            _resourceCell = Enumerable.Range(0, MapPathfinder.CellCount)
                .First(c => MapPathfinder.InteractionAdjacent(SelfCell, c));
            Field(typeof(MapReader), "_interPosCache").SetValue(reader,
                new ConcurrentDictionary<long, IReadOnlyDictionary<int, int>>(
                    new[] { new KeyValuePair<long, IReadOnlyDictionary<int, int>>(
                        Map, new Dictionary<int, int> { [Element] = _resourceCell }) }));
            Field(typeof(Launch), "_cellReader").SetValue(Launch, reader);

            World = new WorldStore(Launch);
            State.MapId = Map;
            State.MapGeneration = 16;
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
                new MapActor(SelfCell, 0, Character, "V16 offline actor", MapActorKind.Player)
            };
            State.PodsAuthoritative = true;
            State.Pods = (10, 100);

            Host = new Host(Pid, Launch, World);
            Host.ActivityRecoveryOfflineStoreForTests = Store;
            Host.ActivityRecoveryOfflineGatherAllowedForTests = true;
            Host.ActivityRecoveryOfflineDispatchForTests = Capture;
            Host.Log += log => Logs.Enqueue(log.Message);
        }

        public void Capture(RecoverySegment segment)
        {
            Sent.Enqueue(segment);
            _first.TrySetResult(segment);
        }

        public void Resource(bool available = true) =>
            State.Interactives = new[] {
                new MapInteractive(_resourceCell, Element, ResourceType, 0,
                    new[] { new InteractiveSkill(125, 45, 9, available) }, "Blé", "Récolter")
            };

        public void Absent() => State.Interactives = Array.Empty<MapInteractive>();

        public void ChangedMap()
        {
            const long other = Map + 1;
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_lastMap")
                .GetValue(Launch)!)[Pid] = other;
            State.MapId = other;
            State.MapGeneration = 17;
            State.CellsMap = other;
            State.SelfCellMap = other;
            State.SelfCellConfirmed = true;
            Absent();
        }

        public Task<RecoverySegment> FirstSendAsync() =>
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

    [Theory]
    [InlineData(false, RecoveryEvidence.ResourceExhaustedWithoutAttribution)]
    [InlineData(true, RecoveryEvidence.ResourceAbsentWithoutAttribution)]
    public async Task Real_gather_after_depletion_or_removal_keeps_exact_durable_pending(
        bool absent, RecoveryEvidence expected)
    {
        using var j = new Journey();
        j.Resource();
        j.Host.Start(Script());
        try
        {
            Assert.Equal(RecoveryKind.Gather, (await j.FirstSendAsync()).Kind);
            if (absent) j.Absent(); else j.Resource(available: false);
            var inspection = j.Host.RefreshActivitySupplyContinuityStatus(
                Gid, RequiredQuantity);
            Assert.Equal(expected, inspection.Evidence);
            Assert.Equal(DurableState.Pending, inspection.DurableStatus.State);
            Assert.Equal(SupplyReview.InspectDurableAttempt, inspection.NextReview);
            Assert.False(inspection.MayAutoDispatch);
            Assert.False(inspection.MayAutoClear);
            Assert.True(File.Exists(j.Marker));
        }
        finally { await j.StopAsync(); }
        Assert.Single(j.Sent);
    }

    [Fact]
    public async Task Map_change_after_gather_is_a_quarantined_observation_not_route_permission()
    {
        using var j = new Journey();
        j.Resource();
        j.Host.Start(Script());
        try
        {
            await j.FirstSendAsync();
            j.ChangedMap();
            var status = j.Host.RefreshActivitySupplyContinuityStatus(
                Gid, RequiredQuantity);
            Assert.Equal(RecoveryEvidence.MapChangedWithoutAttribution, status.Evidence);
            Assert.Equal(DurableState.Pending, status.DurableStatus.State);
            Assert.Equal(SupplyReview.InspectDurableAttempt, status.NextReview);
            Assert.True(File.Exists(j.Marker));
        }
        finally { await j.StopAsync(); }
        Assert.Single(j.Sent);
    }

    [Fact]
    public async Task Pending_gather_cannot_replay_through_legacy_Route_or_top_level_gatherAll()
    {
        using var j = new Journey();
        j.Resource();
        j.Host.Start(Script());
        await j.FirstSendAsync();
        await j.StopAsync();
        Assert.Single(j.Sent);
        // This is a real second MoonSharp RunLoop with a different Lua entry:
        // even imperative harvest at load time sees the fsynced prior attempt.
        j.Host.Start("gatherAll()\n" +
            "function move()\n" +
            "  printMessage('V16_LEGACY_CALLBACK_RAN')\n" +
            "  return {{ map = " + Map + ", gather = true, cell = " + TargetCell + " }}\n" +
            "end\n");
        try
        {
            await Task.Delay(250);
            Assert.Single(j.Sent);
            Assert.True(File.Exists(j.Marker));
            Assert.Equal(DurableState.Pending,
                j.Store.ReadStatus(new(Server, Character)).State);
            Assert.DoesNotContain(j.Logs,
                msg => msg.Contains("V16_LEGACY_CALLBACK_RAN", StringComparison.Ordinal));
        }
        finally { await j.StopAsync(); }
    }

    [Fact]
    public async Task Old_lua_would_send_callback_cannot_stop_a_replacement_CTS()
    {
        using var j = new Journey();
        j.Resource();
        int stuck = 0;
        bool replacementSurvived = false;
        j.Host.StuckStopped += () => Interlocked.Increment(ref stuck);
        j.Host.ActivityRecoveryOfflineDispatchForTests = segment =>
        {
            j.Capture(segment);
            var cts = Field(typeof(Host), "_cts");
            var old = (CancellationTokenSource)cts.GetValue(j.Host)!;
            var replacement = new CancellationTokenSource();
            // Deterministically reproduce QueueRun's publication of a new CTS
            // during this ACTUAL RunLoop/RunRoute/GatherOne callback.
            cts.SetValue(j.Host, replacement);
            var runGen = Field(typeof(Host), "_runGen");
            runGen.SetValue(j.Host, (int)runGen.GetValue(j.Host)! + 1);
            typeof(Host).GetMethod("StopRecoveryPlanningOnce", NonPublic)!
                .Invoke(j.Host, new object[] { old.Token });
            replacementSurvived = ReferenceEquals(cts.GetValue(j.Host), replacement) &&
                !replacement.IsCancellationRequested;
            old.Cancel();
        };
        j.Host.Start(Script());
        await j.FirstSendAsync();
        await j.Host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(replacementSurvived);
        Assert.Equal(0, Volatile.Read(ref stuck));
        Assert.Single(j.Sent);
        Assert.Equal(DurableState.Pending,
            j.Store.ReadStatus(new(Server, Character)).State);
    }

    [Fact]
    public async Task Full_pods_and_unmatched_map_cannot_reserve_any_resource_or_cell()
    {
        using var pods = new Journey();
        pods.Resource();
        pods.State.Pods = (97, 100);
        pods.Host.Start(Script());
        await pods.Host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(pods.Sent);
        Assert.False(File.Exists(pods.Marker));

        using var route = new Journey();
        route.Resource();
        int routeStops = 0;
        route.Host.StuckStopped += () => Interlocked.Increment(ref routeStops);
        route.Host.Start(Script(map: Map + 1));
        await route.Host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(route.Sent);
        Assert.False(File.Exists(route.Marker));
        Assert.Equal(1, Volatile.Read(ref routeStops));
    }
}
