extern alias F06App;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using MoonSharp.Interpreter;
using GivreClone.Protocol;
using Xunit;
using DurableStore = F06App::GivreClone.App.Services.ActivityRecoveryDurableStore;
using DurableState = F06App::GivreClone.App.Services.ActivityRecoveryDurableState;
using RecoveryKind = F06App::GivreClone.App.Services.ActivityRecoveryKind;
using RecoverySegment = F06App::GivreClone.App.Services.ActivityRecoverySegment;
using RecoveryDecision = F06App::GivreClone.App.Services.ActivityRecoveryReconciliationDecision;
using RecoveryEvidence = F06App::GivreClone.App.Services.ActivityRecoveryReconciliationEvidence;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using MapReader = F06App::GivreClone.App.Services.MapCellReader;

namespace GivreClone.Tests;

/// <summary>
/// REAL route/gather LuaHost journey tests, offline. Each test invokes RunRoute on
/// a MoonSharp route returned by a script from TestInitScript/Bind, with a
/// server-shaped BotWorldState and LaunchService current-map/identity dictionaries.
/// Only the final gameplay send is intercepted via the internal test-only hook:
/// no relay, socket, game process, UI, or user profile is touched.
/// The durable file is the actual production ActivityRecoveryDurableStore under a
/// unique private test directory, reopened across host instances for crash tests.
/// </summary>
public sealed class ActivityRouteGatherJourneyV8Tests
{
    private const long Map = 90703360;
    private const int Source = 240;
    private const int Target = 320;
    private const int Element = 45678;
    private const int ResourceType = 38;
    private const int Skill = 45;
    private const long Character = 1071683469604;
    private const int Server = 292;
    private const uint Pid = 1;
    private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    private sealed class Journey : IDisposable
    {
        private readonly bool _ownsRoot;
        public LuaHostHarness Harness { get; }
        public Host Host => Harness.Host;
        public F06App::GivreClone.App.Services.BotWorldState World => Harness.World.Get(Pid);
        public string Root { get; }
        public DurableStore Store { get; }
        public List<RecoverySegment> WouldSend { get; } = new();
        public List<string> Warnings { get; } = new();
        public int ResourceCell { get; }

        public Journey(string? root = null)
        {
            _ownsRoot = root is null;
            Root = root ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "GivreClone-ActivityRouteJourney-V8", Guid.NewGuid().ToString("N"));
            Store = new DurableStore(Root);
            Harness = LuaHostHarness.Create();
            Host.ActivityRecoveryOfflineStoreForTests = Store;
            Host.ActivityRecoveryOfflineDispatchForTests = WouldSend.Add;
            Host.ActivityRecoveryOfflineGatherAllowedForTests = true;
            Host.Log += entry => Warnings.Add(entry.Message);

            var launch = Harness.Launch;
            var own = (ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_charId").GetValue(launch)!;
            own[Pid] = Character;
            var servers = (ConcurrentDictionary<uint, (int Id, string Name)>)Field(
                typeof(Launch), "_serverInfo").GetValue(launch)!;
            servers[Pid] = (Server, "Orukam");
            Harness.SetCurrentMap(Map);

            // The existing binding harness makes a no-network LaunchService via
            // GetUninitializedObject. Add exactly the pieces read by real recovery
            // guards, with no real relay injected.
            Field(typeof(Launch), "_linkDown").SetValue(launch,
                Activator.CreateInstance(Field(typeof(Launch), "_linkDown").FieldType));
            Field(typeof(Launch), "_returnWait").SetValue(launch,
                Activator.CreateInstance(Field(typeof(Launch), "_returnWait").FieldType));
            // This offline harness constructs LaunchService without running
            // its ctor. The true Stop("user") path always calls the already
            // existing automatic-stat cancellation gate, so initialize those
            // unrelated per-process fields rather than altering production
            // Stop or avoiding the Stop/Start lifecycle under test.
            foreach (string name in new[]
            {
                "_autoLevelLock", "_autoStatSuspended", "_autoStatRequests",
                "_autoStatCancellation"
            })
            {
                var field = Field(typeof(Launch), name);
                field.SetValue(launch, Activator.CreateInstance(field.FieldType));
            }
            var decoded = new ConcurrentDictionary<uint, long>();
            decoded[Pid] = Map;
            Field(typeof(Launch), "_interactivesDecodedMap").SetValue(launch, decoded);

            // The map reader uses an in-memory position cache here. No map bundle,
            // user mapcache, or background game extraction is accessed.
            var reader = (MapReader)RuntimeHelpers.GetUninitializedObject(typeof(MapReader));
            var positions = new ConcurrentDictionary<long, IReadOnlyDictionary<int, int>>();
            ResourceCell = Enumerable.Range(0, MapPathfinder.CellCount)
                .First(c => MapPathfinder.InteractionAdjacent(Source, c));
            positions[Map] = new Dictionary<int, int> { [Element] = ResourceCell };
            Field(typeof(MapReader), "_interPosCache").SetValue(reader, positions);
            Field(typeof(Launch), "_cellReader").SetValue(launch, reader);

            var state = World;
            state.MapId = Map;
            state.MapGeneration = 7;
            state.MapX = 0; state.MapY = 0;
            state.SelfCell = Source; state.SelfCellMap = Map;
            state.SelfCellConfirmed = true; state.SelfActorId = Character;
            // V15 strict recovery rejects missing server pods. The offline
            // positive fixture supplies a simulated authoritative reading;
            // tests for missing pods must separately assert NO SEND.
            state.PodsAuthoritative = true;
            state.Pods = (10, 100);
            state.Cells = Enumerable.Range(0, MapPathfinder.CellCount)
                .Select(c => new MapCell(c, Mov: true, Los: true, Floor: 0,
                    MoveZone: 1, LinkedZone: 16)).ToArray();
            state.CellsMap = Map;
            state.RebuildWalk();
            state.Actors = new[]
            {
                new MapActor(Source, 0, Character, "Test-Character", MapActorKind.Player)
            };
        }

        private static FieldInfo Field(Type type, string field) =>
            type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Test fixture cannot locate " + field);

        public void SetResource(bool enabled, bool present = true)
        {
            World.Interactives = present
                ? new[] { new MapInteractive(ResourceCell, Element, ResourceType, 0,
                    new[] { new InteractiveSkill(125, Skill, 9, enabled) }, "Blé", "Récolter") }
                : Array.Empty<MapInteractive>();
        }

        public void Script(string source) => Harness.Run(source);

        public void Route(string route)
        {
            // Execute the actual declarative move() function through MoonSharp,
            // as RunLoopCore does, then enter its real RunRoute handler.
            Harness.Run("function move() return " + route + " end");
            DynValue parsed = Harness.Run("return move()");
            Assert.Equal(DataType.Table, parsed.Type);
            var run = typeof(Host).GetMethod("RunRoute", NonPublicInstance)
                ?? throw new InvalidOperationException("Real RunRoute entry missing");
            try { run.Invoke(Host, new object[] { parsed.Table, CancellationToken.None, 0 }); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        public string Marker => System.IO.Path.Combine(Root, "server-" + Server,
            "character-" + Character + ".pending");

        public void Dispose()
        {
            Harness.Dispose();
            if (_ownsRoot && Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public void Real_lua_route_to_cell_writes_before_would_send_and_quarantines_next_tick()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "GivreClone-ActivityRouteJourney-V8", Guid.NewGuid().ToString("N"));
        try
        {
            using var journey = new Journey(root);
            string route = $"{{ {{ map = {Map}, activityRecovery = true, cell = {Target} }} }}";
            journey.Route(route);
            Assert.Single(journey.WouldSend);
            Assert.Equal(RecoveryKind.Cell, journey.WouldSend[0].Kind);
            Assert.True(File.Exists(journey.Marker)); // actual fsynced marker before fake dispatch
            Assert.Equal(DurableState.Pending, journey.Store.ReadStatus(new(Server, Character)).State);
            Assert.True(journey.Host.ActivityRecoveryStatus?.IsQuarantined);
            journey.Route(route); // same run, same route, new tick
            Assert.Single(journey.WouldSend);
            Assert.Equal(DurableState.Pending, journey.Store.ReadStatus(new(Server, Character)).State);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Real_route_stop_then_new_host_same_identity_cannot_send_after_crash()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "GivreClone-ActivityRouteJourney-V8", Guid.NewGuid().ToString("N"));
        try
        {
            string route = $"{{ {{ map = {Map}, activityRecovery = true, cell = {Target} }} }}";
            using (var first = new Journey(root))
            {
                first.Route(route);
                Assert.Single(first.WouldSend);
                first.Host.Stop("user");
            }
            using (var restarted = new Journey(root))
            {
                restarted.World.SelfCell = Target; // waypoint observed after reconnect
                restarted.World.Actors = new[]
                {
                    new MapActor(Target, 0, Character, "Test-Character", MapActorKind.Player)
                };
                restarted.Route(route);
                Assert.Empty(restarted.WouldSend);
                Assert.Equal(DurableState.Pending, restarted.Host.DurableActivityRecoveryStatus.State);
                Assert.True(restarted.Host.ActivityRecoveryStatus?.IsQuarantined);
                var result = restarted.Host.RefreshActivityRecoveryReconciliationStatus();
                Assert.Equal(RecoveryDecision.PendingQuarantine, result.Decision);
                Assert.Equal(RecoveryEvidence.CellTargetObservedWithoutAttribution, result.Evidence);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Same_host_script_generation_restarted_cannot_rearm_an_ambiguous_waypoint()
    {
        using var journey = new Journey();
        string route = $"{{ {{ map = {Map}, activityRecovery = true, cell = {Target} }} }}";
        journey.Route(route);
        Assert.Single(journey.WouldSend);
        var generation = typeof(Host).GetField("_runGen", NonPublicInstance)!;
        generation.SetValue(journey.Host, (int)generation.GetValue(journey.Host)! + 1);
        journey.Route(route);
        Assert.Single(journey.WouldSend);
        Assert.Equal(DurableState.Pending,
            journey.Store.ReadStatus(new(Server, Character)).State);
    }

    [Fact]
    public void Per_step_lua_reconciliation_remains_readonly_when_marker_is_already_pending()
    {
        using var journey = new Journey();
        journey.Route($"{{ {{ map = {Map}, activityRecovery = true, cell = {Target} }} }}");
        Assert.Single(journey.WouldSend);
        journey.Route($"{{ {{ map = {Map}, activityRecoveryReconcile = true }} }}");
        Assert.Single(journey.WouldSend);
        var inspection = journey.Host.ActivityRecoveryReconciliationStatus;
        Assert.NotNull(inspection);
        Assert.Equal(RecoveryDecision.PendingQuarantine, inspection!.Decision);
        Assert.False(inspection.MayRedispatch);
        Assert.Equal(DurableState.Pending, journey.Store.ReadStatus(new(Server, Character)).State);
    }

    [Fact]
    public void Real_route_mixed_opt_in_must_not_enter_legacy_off_graph_rejoin()
    {
        using var journey = new Journey();
        // Neither the first unmatched waypoint nor the current map has an opted-in
        // step. Before V8, the legacy branch tried world-graph rejoin *before*
        // reaching the later per-step durable gate.
        string route = $"{{ {{ map = {Map + 99}, activityRecovery = true, cell = {Target} }} }}";
        journey.Route(route);
        Assert.Empty(journey.WouldSend);
        Assert.Empty(Directory.Exists(journey.Root)
            ? Directory.GetFiles(journey.Root, "*.pending", SearchOption.AllDirectories)
            : Array.Empty<string>());
    }

    [Fact]
    public void Mixed_recovery_route_refuses_a_current_legacy_step_before_any_send()
    {
        using var journey = new Journey();
        // This current-map step would be handled by the historical route engine,
        // but a later opted step makes the whole journey require explicit admission.
        string route = $"{{ {{ map = {Map}, cell = {Target} }}, " +
            $"{{ map = {Map + 1}, activityRecovery = true, cell = {Target} }} }}";
        journey.Route(route);
        Assert.Empty(journey.WouldSend);
        Assert.False(File.Exists(journey.Marker));
        Assert.Contains(journey.Warnings,
            w => w.Contains("etape non opt-in dans une route recovery mixte"));
    }

    [Fact]
    public void Actual_lua_waypoint_already_observed_skips_dispatch_and_disk_marker()
    {
        using var journey = new Journey();
        string route = $"{{ {{ map = {Map}, activityRecovery = true, cell = {Source} }} }}";
        journey.Route(route);
        Assert.Empty(journey.WouldSend);
        Assert.Equal(DurableState.Clear, journey.Store.ReadStatus(new(Server, Character)).State);
        Assert.False(File.Exists(journey.Marker));
    }

    [Fact]
    public void Actual_route_gather_then_cell_reserves_harvest_not_followup_movement()
    {
        using var journey = new Journey();
        journey.SetResource(enabled: true);
        journey.Script($"ELEMENTS_TO_GATHER = {{{ResourceType}}}");
        string route = $"{{ {{ map = {Map}, activityRecovery = true, gather = true, cell = {Target} }} }}";
        journey.Route(route);
        Assert.Single(journey.WouldSend);
        Assert.Equal(RecoveryKind.Gather, journey.WouldSend[0].Kind);
        Assert.Equal(Element, journey.WouldSend[0].ElementId);
        var durable = journey.Store.ReadStatus(new(Server, Character));
        Assert.Equal(DurableState.Pending, durable.State);
        Assert.Equal(RecoveryKind.Gather, durable.Segment?.Kind);
        var observation = journey.Host.RefreshActivityRecoveryReconciliationStatus();
        Assert.Equal(RecoveryDecision.PendingQuarantine, observation.Decision);
        Assert.False(observation.MayRedispatch);
        // No inventory snapshot/element-specific receipt was injected by this
        // offline journey: do not invent a stocked-item ACK.
        Assert.NotEqual(RecoveryEvidence.InventoryIncreaseWithoutAttribution, observation.Evidence);
        journey.Route(route);
        Assert.Single(journey.WouldSend); // no second gather and no movement
    }

    [Fact]
    public void Exhausted_resource_can_be_skipped_to_one_admitted_cell_waypoint()
    {
        using var journey = new Journey();
        journey.SetResource(enabled: false);
        journey.Script($"ELEMENTS_TO_GATHER = {{{ResourceType}}}");
        string route = $"{{ {{ map = {Map}, activityRecovery = true, gather = true, cell = {Target} }} }}";
        journey.Route(route);
        Assert.Single(journey.WouldSend);
        Assert.Equal(RecoveryKind.Cell, journey.WouldSend[0].Kind);
        Assert.Equal(RecoveryKind.Cell, journey.Store.ReadStatus(new(Server, Character)).Segment?.Kind);
        journey.Route(route);
        Assert.Single(journey.WouldSend);
    }

    [Fact]
    public void Gather_only_exhausted_resource_has_no_dispatch_and_no_busy_retry_marker()
    {
        using var journey = new Journey();
        journey.SetResource(enabled: false);
        string route = $"{{ {{ map = {Map}, activityRecovery = true, gather = true }} }}";
        for (int i = 0; i < 3; i++) journey.Route(route);
        Assert.Empty(journey.WouldSend);
        Assert.Equal(DurableState.Clear, journey.Store.ReadStatus(new(Server, Character)).State);
        Assert.False(File.Exists(journey.Marker));
        Assert.Single(journey.Warnings.Where(w => w.Contains("etape sans sortie")));
    }

    [Fact]
    public void Corrupted_marker_after_crash_blocks_even_a_new_eligible_resource()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "GivreClone-ActivityRouteJourney-V8", Guid.NewGuid().ToString("N"));
        try
        {
            using (var first = new Journey(root))
                first.Route($"{{ {{ map = {Map}, activityRecovery = true, cell = {Target} }} }}");
            string marker = System.IO.Path.Combine(root, "server-" + Server,
                "character-" + Character + ".pending");
            byte[] bad = File.ReadAllBytes(marker);
            bad[bad.Length / 2] ^= 0x51;
            File.WriteAllBytes(marker, bad);
            using var reopened = new Journey(root);
            reopened.SetResource(enabled: true);
            reopened.Route($"{{ {{ map = {Map}, activityRecovery = true, gather = true }} }}");
            Assert.Empty(reopened.WouldSend);
            Assert.Equal(DurableState.Corrupt, reopened.Host.DurableActivityRecoveryStatus.State);
            Assert.Equal(bad, File.ReadAllBytes(marker));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Invalid_identity_on_restart_blocks_before_admission_no_pid_inference()
    {
        using var journey = new Journey();
        var charId = (ConcurrentDictionary<uint, long>)typeof(Launch)
            .GetField("_charId", NonPublicInstance)!.GetValue(journey.Harness.Launch)!;
        charId.TryRemove(Pid, out _);
        journey.Route($"{{ {{ map = {Map}, activityRecovery = true, cell = {Target} }} }}");
        Assert.Empty(journey.WouldSend);
        Assert.Equal(DurableState.IdentityMissing, journey.Host.DurableActivityRecoveryStatus.State);
        Assert.False(File.Exists(journey.Marker));
    }

    [Fact]
    public void Position_only_predicted_after_reconnect_refuses_admission()
    {
        using var journey = new Journey();
        journey.World.SelfCellConfirmed = false;
        journey.Route($"{{ {{ map = {Map}, activityRecovery = true, cell = {Target} }} }}");
        Assert.Empty(journey.WouldSend);
        Assert.False(File.Exists(journey.Marker));
        Assert.True(journey.Host.DurableActivityRecoveryStatus.IsQuarantined);
    }

    [Fact]
    public void Global_opt_in_generated_route_uses_same_durable_barrier()
    {
        using var journey = new Journey();
        journey.Script("ACTIVITY_RECOVERY = true");
        journey.Route($"{{ {{ map = {Map}, cell = {Target} }} }}");
        Assert.Single(journey.WouldSend);
        Assert.Equal(DurableState.Pending, journey.Host.DurableActivityRecoveryStatus.State);
        journey.Route($"{{ {{ map = {Map}, cell = {Target} }} }}");
        Assert.Single(journey.WouldSend);
    }
}
