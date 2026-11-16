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
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using MapReader = F06App::GivreClone.App.Services.MapCellReader;
using RecoveryKind = F06App::GivreClone.App.Services.ActivityRecoveryKind;
using RecoverySegment = F06App::GivreClone.App.Services.ActivityRecoverySegment;
using ViewModel = F06App::GivreClone.App.ViewModels.BotsViewModel;
using WorldStore = F06App::GivreClone.App.Services.BotWorldStore;
using GameNames = F06App::GivreClone.App.Services.GameNameResolver;
using Inventory = F06App::GivreClone.App.Services.InventoryStore;
using GuidedGoal = F06App::GivreClone.App.Services.Production.GuidedActivityProductionGoal;
using GuidedTicket = F06App::GivreClone.App.ViewModels.GuidedSupplyRoutePreparation;

namespace GivreClone.Tests;

/// <summary>
/// Production-entry regression: the actual desktop script factory is fed to
/// GivreLuaHost.Start -> RunLoopCore table-only -> RunRoute -> GatherOne.
/// The REAL durable store is used in a disposable test directory; only the
/// final would-send point is intercepted. No relay, Dofus process or server ACK.
/// </summary>
public sealed class ActivityRouteProductionV14Tests
{
    private const uint Pid = 71431;
    private const int Server = 9931;
    private const long Character = 9043219876;
    private const long Map = 90703360;
    private const int Source = 240;
    private const int Destination = 320;
    private const int Element = 45678;
    private const int ResourceType = 38;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static FieldInfo Field(Type t, string name) =>
        t.GetField(name, PrivateInstance) ??
        throw new InvalidOperationException("offline fixture field missing: " + name);

    private static string Generated(bool gather, int maxPods = 95)
    {
        Assert.True(ViewModel.TryBuildStrictActivityRouteScript(
            Map, Destination, gather, maxPods, out var script, out var refusal), refusal);
        Assert.Contains("ACTIVITY_RECOVERY_TABLE_ONLY = true", script);
        Assert.Contains("ACTIVITY_RECOVERY = true", script);
        Assert.Contains("ACTIVITY_RECOVERY_ROUTE = {", script);
        Assert.DoesNotContain("function move()", script);
        Assert.DoesNotContain("function bank()", script);
        return script;
    }

    private static string GeneratedGuided(long gid = 2416, long required = 10)
    {
        var goal = new GuidedGoal(Server, Character, Map, 12,
            Destination, 1934, 1, 95, true);
        var ticket = new GuidedTicket(Pid, goal, new string('A', 64),
            gid, required, new string('B', 64), new string('C', 64),
            new string('D', 64), 1);
        Assert.True(ViewModel.TryBuildGuidedSupplyRouteScript(
            Map, Destination, 95, ticket, out var script, out var refusal), refusal);
        Assert.Contains("ITEMS_TO_GATHER = { " + gid + " }", script);
        Assert.Contains("GUIDED_SUPPLY_REQUIRED = \"" + required + "\"", script);
        Assert.Contains("GUIDED_SUPPLY_OBS_REV = \"1\"", script);
        Assert.DoesNotContain("cell = ", script);
        return script;
    }

    private sealed class RunningJourney : IDisposable
    {
        private readonly bool _ownsRoot;
        public string Root { get; }
        public Launch Launch { get; }
        public WorldStore WorldStore { get; }
        public F06App::GivreClone.App.Services.BotWorldState State => WorldStore.Get(Pid);
        public Host Host { get; }
        public DurableStore Store { get; }
        public ConcurrentQueue<RecoverySegment> Sent { get; } = new();
        public ConcurrentQueue<string> Logs { get; } = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>>
            _logSignals = new(StringComparer.Ordinal);
        public int ResourceCell { get; }
        private readonly ConcurrentDictionary<long, IReadOnlyDictionary<int, int>> _positions = new();
        private readonly TaskCompletionSource<RecoverySegment> _first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _mainScriptStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RunningJourney(string? root = null)
        {
            _ownsRoot = root is null;
            Root = root ?? Path.Combine(Path.GetTempPath(),
                "GivreClone-ActivityRouteProductionV14", Guid.NewGuid().ToString("N"));
            Store = new DurableStore(Root);
            Launch = new Launch();
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_charId")
                .GetValue(Launch)!)[Pid] = Character;
            ((ConcurrentDictionary<uint, (int Id, string Name)>)Field(
                typeof(Launch), "_serverInfo").GetValue(Launch)!)[Pid] = (Server, "Orukam");
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_lastMap")
                .GetValue(Launch)!)[Pid] = Map;
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_interactivesDecodedMap")
                .GetValue(Launch)!)[Pid] = Map;

            // Self-contained map fixture: do not read installed bundles or
            // modify the user's map cache during these offline tests.
            var mapReader = (MapReader)RuntimeHelpers.GetUninitializedObject(typeof(MapReader));
            ResourceCell = Enumerable.Range(0, MapPathfinder.CellCount)
                .First(c => MapPathfinder.InteractionAdjacent(Source, c));
            _positions[Map] = new Dictionary<int, int> { [Element] = ResourceCell };
            Field(typeof(MapReader), "_interPosCache").SetValue(mapReader, _positions);
            Field(typeof(Launch), "_cellReader").SetValue(Launch, mapReader);

            WorldStore = new WorldStore(Launch);
            State.MapId = Map;
            State.MapGeneration = 12;
            State.MapX = 0;
            State.MapY = 0;
            State.SelfCell = Source;
            State.SelfCellMap = Map;
            State.SelfCellConfirmed = true;
            State.SelfActorId = Character;
            State.Cells = Enumerable.Range(0, MapPathfinder.CellCount)
                .Select(c => new MapCell(c, Mov: true, Los: true, Floor: 0,
                    MoveZone: 1, LinkedZone: 16)).ToArray();
            State.CellsMap = Map;
            State.RebuildWalk();
            State.Actors = new[] {
                new MapActor(Source, 0, Character, "V14 offline persona", MapActorKind.Player)
            };
            State.PodsAuthoritative = true;
            State.Pods = (10, 100);

            Host = new Host(Pid, Launch, WorldStore);
            Host.MainScriptStarting += () => _mainScriptStarted.TrySetResult();
            Host.ActivityRecoveryOfflineStoreForTests = Store;
            Host.ActivityRecoveryOfflineGatherAllowedForTests = true;
            Host.ActivityRecoveryOfflineDispatchForTests = segment =>
            {
                Sent.Enqueue(segment);
                _first.TrySetResult(segment);
            };
            Host.Log += entry =>
            {
                Logs.Enqueue(entry.Message);
                foreach (var signal in _logSignals)
                    if (entry.Message.Contains(signal.Key, StringComparison.Ordinal))
                        signal.Value.TrySetResult(entry.Message);
            };
        }

        public void Resource(bool harvestable = true, int uid = 125)
        {
            State.Interactives = new[] {
                new MapInteractive(ResourceCell, Element, ResourceType, 0,
                    new[] { new InteractiveSkill(uid, 45, 9, harvestable) },
                    "Ble", "Recolter")
            };
        }

        public void GuidedIngredient(long gid, int bagQty = 1)
        {
            Field(typeof(GameNames), "_skillToItem").SetValue(Launch.GameNames,
                new Dictionary<int, int> { [45] = checked((int)gid) });
            Launch.Inventory.Seed(Pid,
                new[] { new Inventory.Item(10001, gid, bagQty) });
        }

        public void ResourceFar()
        {
            int far = Enumerable.Range(0, MapPathfinder.CellCount)
                .First(c => MapPathfinder.CellDistance(c, Source) > 8);
            _positions[Map] = new Dictionary<int, int> { [Element] = far };
            State.Interactives = new[] {
                new MapInteractive(far, Element, ResourceType, 0,
                    new[] { new InteractiveSkill(125, 45, 9, true) },
                    "Ble", "Recolter")
            };
        }

        public string Marker => Path.Combine(Root,
            "server-" + Server, "character-" + Character + ".pending");

        public Task<RecoverySegment> FirstSendAsync() =>
            _first.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Observe the actual RunLoop log without polling task scheduling and
        // without discarding a message emitted just before registration.
        public Task<string> ObserveLogAsync(string text)
        {
            var signal = _logSignals.GetOrAdd(text, _ =>
                new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));
            var alreadySeen = Logs.FirstOrDefault(s =>
                s.Contains(text, StringComparison.Ordinal));
            if (alreadySeen is not null) signal.TrySetResult(alreadySeen);
            return signal.Task;
        }

        public async Task AwaitRequiredRunLogAsync(string text)
        {
            var diagnostic = ObserveLogAsync(text);
            var completion = Host.RunCompletion;
            // QueueRun schedules the real RunLoop on a thread-pool worker.
            // In a loaded full suite its Task may still be WaitingForActivation
            // when a fixed post-Start wall-clock timer expires. Only start the
            // diagnostic bound after observing the actual run-start callback.
            var start = await Task.WhenAny(_mainScriptStarted.Task, completion,
                Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(_mainScriptStarted.Task.IsCompletedSuccessfully,
                "RunLoop never activated; winning task=" + start.Status +
                "; completion=" + completion.Status +
                "; logs=" + string.Join(" | ", Logs.Take(20)));
            // A script can legitimately terminate before RunRoute because of
            // its top-level guard, a syntax error, or invalid map/actor state.
            // Treat that as a deterministic assertion failure, with evidence,
            // instead of waiting five seconds for an impossible later log.
            await Task.WhenAny(diagnostic, completion, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.True(diagnostic.IsCompletedSuccessfully,
                "RunLoop ended or did not reach required diagnostic '" + text +
                "'; completion=" + completion.Status +
                "; logs=" + string.Join(" | ", Logs.Take(20)));
        }

        public async Task StopAsync()
        {
            Host.Stop("user");
            await Host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public void Dispose()
        {
            Host.Dispose();
            if (_ownsRoot && Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public async Task Generated_table_only_gather_flows_through_real_RunLoop_and_durable_marker()
    {
        using var journey = new RunningJourney();
        journey.Resource();
        journey.Host.Start(Generated(gather: true));
        try
        {
            var segment = await journey.FirstSendAsync();
            Assert.Equal(RecoveryKind.Gather, segment.Kind);
            Assert.Equal(Element, segment.ElementId);
            Assert.True(File.Exists(journey.Marker));
            Assert.Equal(DurableState.Pending,
                journey.Store.ReadStatus(new(Server, Character)).State);
            Assert.False(journey.Host.RefreshActivityRecoveryReconciliationStatus()
                .MayRedispatch);
        }
        finally { await journey.StopAsync(); }
        Assert.Single(journey.Sent);
    }

    [Fact]
    public async Task Generated_guided_gid_requires_real_link_decoder_checkpoint_even_with_cached_deficit()
    {
        using var journey = new RunningJourney();
        journey.Resource();
        journey.GuidedIngredient(2416, bagQty: 1);
        var expectedLog = journey.ObserveLogAsync("approvisionnement");
        journey.Host.Start(GeneratedGuided());
        try
        {
            await journey.AwaitRequiredRunLogAsync("approvisionnement");
            Assert.True(expectedLog.IsCompletedSuccessfully);
            // The local bag count is 1 < required 10, but the offline host
            // has no trusted link/decoder source and no durable goal ledger.
            // These are mandatory; a cache shortage is never a send lease.
            Assert.Empty(journey.Sent);
            Assert.False(File.Exists(journey.Marker));
            Assert.Contains(journey.Logs, s => s.Contains("approvisionnement"));
            Assert.Contains(journey.Logs,
                s => s.Contains("source lien/decoder/revision non attestee"));
        }
        finally { await journey.StopAsync(); }
    }

    [Fact]
    public async Task Filtered_guided_script_missing_revision_stops_without_a_packet()
    {
        using var journey = new RunningJourney();
        journey.Resource();
        journey.GuidedIngredient(2416);
        string invalid = GeneratedGuided()
            .Replace("GUIDED_SUPPLY_OBS_REV = \"1\"", "GUIDED_SUPPLY_OBS_REV = nil",
                StringComparison.Ordinal);
        var expectedLog = journey.ObserveLogAsync("plan incomplet");
        journey.Host.Start(invalid);
        try
        {
            await journey.AwaitRequiredRunLogAsync("plan incomplet");
            Assert.True(expectedLog.IsCompletedSuccessfully);
            Assert.Empty(journey.Sent);
            Assert.False(File.Exists(journey.Marker));
            Assert.Contains(journey.Logs, s => s.Contains("plan incomplet"));
        }
        finally { await journey.StopAsync(); }
    }

    [Fact]
    public async Task Guided_depleted_resource_must_not_fall_back_to_waypoint_movement()
    {
        using var journey = new RunningJourney();
        journey.Resource(harvestable: false);
        journey.GuidedIngredient(2416);
        journey.Host.Start(GeneratedGuided());
        try
        {
            // Even with no harvest candidate, the route must diagnose the
            // missing authenticated link/decoder before resource selection.
            await journey.AwaitRequiredRunLogAsync("source lien/decoder");
            Assert.Empty(journey.Sent);
            Assert.False(File.Exists(journey.Marker));
        }
        finally { await journey.StopAsync(); }
    }

    [Fact]
    public async Task Generated_gather_with_exhausted_resource_chooses_one_durable_cell_action()
    {
        using var journey = new RunningJourney();
        journey.Resource(harvestable: false);
        journey.Host.Start(Generated(gather: true));
        try
        {
            Assert.Equal(RecoveryKind.Cell, (await journey.FirstSendAsync()).Kind);
            Assert.Equal(DurableState.Pending,
                journey.Store.ReadStatus(new(Server, Character)).State);
        }
        finally { await journey.StopAsync(); }
        Assert.Single(journey.Sent);
    }

    [Fact]
    public async Task Generated_gather_with_only_distant_resource_chooses_one_cell_action()
    {
        using var journey = new RunningJourney();
        journey.ResourceFar();
        journey.Host.Start(Generated(gather: true));
        try
        {
            var segment = await journey.FirstSendAsync();
            Assert.Equal(RecoveryKind.Cell, segment.Kind);
            Assert.Equal(Destination, segment.Target);
        }
        finally { await journey.StopAsync(); }
        Assert.Single(journey.Sent);
    }

    [Fact]
    public async Task Table_only_full_pods_never_calls_bank_or_move_and_never_reserves()
    {
        using var journey = new RunningJourney();
        journey.Resource();
        journey.State.Pods = (95, 100);
        string script = Generated(gather: true) + """

            function move() error('V14_UNSAFE_MOVE') end
            function bank() error('V14_UNSAFE_BANK') end
            """;
        journey.Host.Start(script);
        await journey.Host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(journey.Sent);
        Assert.False(File.Exists(journey.Marker));
        Assert.DoesNotContain(journey.Logs, s => s.Contains("V14_UNSAFE_"));
        Assert.Contains(journey.Logs, s => s.Contains("pods au seuil"));
    }

    [Fact]
    public async Task Table_only_unknown_pods_cannot_approve_a_harvest()
    {
        using var journey = new RunningJourney();
        journey.Resource();
        journey.State.PodsAuthoritative = false; // cached 10/100 is not server evidence
        journey.Host.Start(Generated(gather: true));
        try
        {
            await Task.Delay(260);
            Assert.Empty(journey.Sent);
            Assert.False(File.Exists(journey.Marker));
        }
        finally { await journey.StopAsync(); }
    }

    [Fact]
    public async Task Invalid_future_waypoint_cannot_consume_a_harvest_lease_first()
    {
        using var journey = new RunningJourney();
        journey.Resource();
        string invalid = Generated(gather: true)
            .Replace("cell = 320", "cell = 'invalid'", StringComparison.Ordinal);
        journey.Host.Start(invalid);
        await journey.Host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(journey.Sent);
        Assert.False(File.Exists(journey.Marker));
        Assert.Contains(journey.Logs, s => s.Contains("cell invalide"));
    }

    [Fact]
    public async Task Table_only_stale_map_and_actor_never_create_a_durable_marker()
    {
        using var journey = new RunningJourney();
        journey.Resource();
        journey.State.MapId = Map + 1; // transport still reports Map
        journey.State.SelfActorId = Character + 1;
        journey.Host.Start(Generated(gather: true));
        try
        {
            await Task.Delay(250);
            Assert.Empty(journey.Sent);
            Assert.False(File.Exists(journey.Marker));
        }
        finally { await journey.StopAsync(); }
    }

    [Fact]
    public async Task Correct_map_but_foreign_self_actor_cannot_dispatch()
    {
        using var journey = new RunningJourney();
        journey.Resource();
        journey.State.SelfActorId = Character + 1;
        journey.Host.Start(Generated(gather: true));
        try
        {
            await Task.Delay(260);
            Assert.Empty(journey.Sent);
            Assert.False(File.Exists(journey.Marker));
        }
        finally { await journey.StopAsync(); }
    }

    [Fact]
    public async Task Restart_of_generated_route_cannot_replay_a_persisted_gather()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "GivreClone-ActivityRouteProductionV14", Guid.NewGuid().ToString("N"));
        try
        {
            using (var original = new RunningJourney(root))
            {
                original.Resource();
                original.Host.Start(Generated(gather: true));
                try { Assert.Equal(RecoveryKind.Gather,
                    (await original.FirstSendAsync()).Kind); }
                finally { await original.StopAsync(); }
                Assert.Single(original.Sent);
                Assert.True(File.Exists(original.Marker));
            }
            using (var resumed = new RunningJourney(root))
            {
                resumed.Resource(); // the target has apparently respawned
                resumed.Host.Start(Generated(gather: true));
                try
                {
                    await Task.Delay(300);
                    Assert.Empty(resumed.Sent);
                    Assert.True(resumed.Host.RefreshActivityRecoveryReconciliationStatus()
                        .IsQuarantined);
                    Assert.Equal(DurableState.Pending,
                        resumed.Store.ReadStatus(new(Server, Character)).State);
                }
                finally { await resumed.StopAsync(); }
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Pending_harvest_from_crash_blocks_imperative_bank_on_relaunch()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "GivreClone-ActivityRouteProductionV14", Guid.NewGuid().ToString("N"));
        try
        {
            using (var first = new RunningJourney(root))
            {
                first.Resource();
                first.Host.Start(Generated(gather: true));
                try { await first.FirstSendAsync(); }
                finally { await first.StopAsync(); }
                Assert.True(File.Exists(first.Marker));
            }
            using (var restart = new RunningJourney(root))
            {
                restart.Resource();
                restart.State.Pods = (100, 100);
                restart.Host.Start(Generated(gather: true) + """

                    function bank() error('V14_UNSAFE_BANK') end
                    function move() error('V14_UNSAFE_MOVE') end
                    """);
                try
                {
                    await Task.Delay(260);
                    Assert.Empty(restart.Sent);
                    Assert.Equal(DurableState.Pending,
                        restart.Store.ReadStatus(new(Server, Character)).State);
                    Assert.DoesNotContain(restart.Logs,
                        s => s.Contains("V14_UNSAFE_"));
                }
                finally { await restart.StopAsync(); }
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Late_actor_republication_after_would_send_does_not_clear_the_lease()
    {
        using var journey = new RunningJourney();
        journey.Resource();
        var wouldSend = new TaskCompletionSource<RecoverySegment>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        journey.Host.ActivityRecoveryOfflineDispatchForTests = segment =>
        {
            journey.Sent.Enqueue(segment);
            journey.State.SelfActorId = Character + 1;
            journey.State.Interactives = Array.Empty<MapInteractive>();
            wouldSend.TrySetResult(segment);
        };
        journey.Host.Start(Generated(gather: true));
        try
        {
            // Wait for the actual would-send callback, not an arbitrary delay
            // from Start that can expire before RunLoop is scheduled.
            Assert.Equal(RecoveryKind.Gather,
                (await wouldSend.Task.WaitAsync(TimeSpan.FromSeconds(10))).Kind);
            Assert.Single(journey.Sent);
            Assert.True(File.Exists(journey.Marker));
            Assert.Equal(DurableState.Pending,
                journey.Store.ReadStatus(new(Server, Character)).State);
            Assert.True(journey.Host.DurableActivityRecoveryStatus.IsQuarantined);
        }
        finally { await journey.StopAsync(); }
    }
}
