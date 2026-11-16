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
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using MapReader = F06App::GivreClone.App.Services.MapCellReader;
using DurableStore = F06App::GivreClone.App.Services.ActivityRecoveryDurableStore;
using DurableState = F06App::GivreClone.App.Services.ActivityRecoveryDurableState;
using RecoveryKind = F06App::GivreClone.App.Services.ActivityRecoveryKind;
using RecoverySegment = F06App::GivreClone.App.Services.ActivityRecoverySegment;
using ReconcileEvidence = F06App::GivreClone.App.Services.ActivityRecoveryReconciliationEvidence;

namespace GivreClone.Tests;

/// <summary>
/// Actual MoonSharp move() -> RunRoute -> GatherOne -> durable admission.
/// Sends are intercepted at the existing offline would-send boundary; no game,
/// relay, client, real inventory, or production recovery directory is used.
/// </summary>
public sealed class ActivitySupplyRecoveryV13Tests
{
    private const long Map = 90703360;
    private const int Source = 240;
    private const int Waypoint = 320;
    private const int Element = 45678;
    private const int TypeId = 38;
    private const int Server = 292;
    private const long Character = 1071683469604;
    private const uint Pid = 1;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static FieldInfo Field(Type type, string name) =>
        type.GetField(name, Private) ??
        throw new InvalidOperationException("missing fixture field " + name);

    private sealed class Journey : IDisposable
    {
        private readonly bool _ownsRoot;
        public LuaHostHarness Harness { get; }
        public Host Host => Harness.Host;
        public F06App::GivreClone.App.Services.BotWorldState World => Harness.World.Get(Pid);
        public DurableStore Durable { get; }
        public string Root { get; }
        public List<RecoverySegment> Sent { get; } = new();
        public List<string> Warnings { get; } = new();
        public int ResourceCell { get; }

        public Journey(string? root = null)
        {
            _ownsRoot = root is null;
            Root = root ?? Path.Combine(Path.GetTempPath(),
                "GivreClone-ActivitySupplyV13", Guid.NewGuid().ToString("N"));
            Durable = new DurableStore(Root);
            Harness = LuaHostHarness.Create();
            Host.ActivityRecoveryOfflineStoreForTests = Durable;
            Host.ActivityRecoveryOfflineDispatchForTests = Sent.Add;
            Host.ActivityRecoveryOfflineGatherAllowedForTests = true;
            Host.Log += entry => Warnings.Add(entry.Message);

            var launch = Harness.Launch;
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_charId")
                .GetValue(launch)!)[Pid] = Character;
            ((ConcurrentDictionary<uint, (int Id, string Name)>)Field(
                typeof(Launch), "_serverInfo").GetValue(launch)!)[Pid] =
                    (Server, "Orukam");
            Harness.SetCurrentMap(Map);

            foreach (string name in new[] { "_linkDown", "_returnWait",
                "_autoLevelLock", "_autoStatSuspended", "_autoStatRequests",
                "_autoStatCancellation" })
            {
                var f = Field(typeof(Launch), name);
                f.SetValue(launch, Activator.CreateInstance(f.FieldType));
            }
            Field(typeof(Launch), "_interactivesDecodedMap").SetValue(launch,
                new ConcurrentDictionary<uint, long>(new[] {
                    new KeyValuePair<uint, long>(Pid, Map)
                }));
            var reader = (MapReader)RuntimeHelpers.GetUninitializedObject(typeof(MapReader));
            ResourceCell = Enumerable.Range(0, MapPathfinder.CellCount)
                .First(c => MapPathfinder.InteractionAdjacent(Source, c));
            var positions = new ConcurrentDictionary<long, IReadOnlyDictionary<int, int>>();
            positions[Map] = new Dictionary<int, int> { [Element] = ResourceCell };
            Field(typeof(MapReader), "_interPosCache").SetValue(reader, positions);
            Field(typeof(Launch), "_cellReader").SetValue(launch, reader);

            World.MapId = Map;
            World.MapGeneration = 12;
            World.MapX = 0;
            World.MapY = 0;
            World.SelfCell = Source;
            World.SelfCellMap = Map;
            World.SelfCellConfirmed = true;
            World.SelfActorId = Character;
            // Positive offline server-shaped fixture: V15 strict gather now
            // requires an observed pods boundary, not an optimistic default.
            // This is synthetic TEST evidence; no Live observation is claimed.
            World.PodsAuthoritative = true;
            World.Pods = (10, 100);
            World.Cells = Enumerable.Range(0, MapPathfinder.CellCount)
                .Select(c => new MapCell(c, Mov: true, Los: true, Floor: 0,
                    MoveZone: 1, LinkedZone: 16)).ToArray();
            World.CellsMap = Map;
            World.RebuildWalk();
            World.Actors = new[] {
                new MapActor(Source, 0, Character, "Test persona", MapActorKind.Player)
            };
        }

        public void Resource(bool ready = true)
        {
            World.Interactives = new[] {
                new MapInteractive(ResourceCell, Element, TypeId, 0,
                    new[] { new InteractiveSkill(125, 45, 9, ready) },
                    "Ble", "Recolter")
            };
        }

        public string Marker => Path.Combine(Root, "server-" + Server,
            "character-" + Character + ".pending");

        public void Route(string luaSteps, CancellationToken ct = default)
        {
            Harness.Run("ELEMENTS_TO_GATHER = {" + TypeId + "}");
            Harness.Run("function move() return " + luaSteps + " end");
            DynValue result = Harness.Run("return move()");
            Assert.Equal(DataType.Table, result.Type);
            var route = typeof(Host).GetMethod("RunRoute", Private)!;
            try { route.Invoke(Host, new object[] { result.Table, ct, 0 }); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        public void Dispose()
        {
            Harness.Dispose();
            if (_ownsRoot && Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public void Harvest_then_exhausted_reconcile_stays_pending_and_no_second_send()
    {
        using var j = new Journey();
        j.Resource();
        string route = $"{{ {{ map = {Map}, activityRecovery = true, gather = true }} }}";
        j.Route(route);
        Assert.Single(j.Sent);
        Assert.Equal(RecoveryKind.Gather, j.Sent[0].Kind);
        Assert.Equal(DurableState.Pending,
            j.Durable.ReadStatus(new(Server, Character)).State);

        j.Resource(ready: false);
        j.Route(route);
        Assert.Single(j.Sent);
        var reconcile = j.Host.RefreshActivityRecoveryReconciliationStatus();
        Assert.Equal(ReconcileEvidence.ResourceExhaustedWithoutAttribution,
            reconcile.Evidence);
        Assert.False(reconcile.MayRedispatch);
        Assert.True(j.Host.ActivityRecoveryStatus?.IsQuarantined);
        Assert.True(File.Exists(j.Marker));
    }

    [Fact]
    public void Crash_restart_with_same_persona_cannot_replay_even_if_resource_respawns()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "GivreClone-ActivitySupplyV13", Guid.NewGuid().ToString("N"));
        string route = $"{{ {{ map = {Map}, activityRecovery = true, gather = true }} }}";
        try
        {
            using (var first = new Journey(root))
            {
                first.Resource();
                first.Route(route);
                Assert.Single(first.Sent);
                Assert.Equal(DurableState.Pending,
                    first.Durable.ReadStatus(new(Server, Character)).State);
                first.Host.Stop("user");
            }

            using (var restarted = new Journey(root))
            {
                restarted.Resource(); // the same element is available again
                restarted.Route(route);
                Assert.Empty(restarted.Sent);
                Assert.True(restarted.Host.ActivityRecoveryStatus?.IsQuarantined);
                Assert.Equal(DurableState.Pending,
                    restarted.Host.DurableActivityRecoveryStatus.State);
                Assert.False(restarted.Host.RefreshActivityRecoveryReconciliationStatus()
                    .MayRedispatch);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Old_world_map_cannot_authorize_gather_when_transport_map_has_advanced()
    {
        using var j = new Journey();
        j.Resource();
        j.Harness.SetCurrentMap(Map + 1);
        j.Route($"{{ {{ map = {Map + 1}, activityRecovery = true, gather = true }} }}");
        Assert.Empty(j.Sent);
        Assert.False(File.Exists(j.Marker));
        Assert.True(j.Host.DurableActivityRecoveryStatus.IsQuarantined);
    }

    [Fact]
    public void Full_authoritative_pods_block_route_before_gather_or_bank_composite()
    {
        using var j = new Journey();
        j.Resource();
        j.World.PodsAuthoritative = true;
        j.World.Pods = (95, 100);
        j.Harness.Run("MAX_PODS = 95");
        j.Route($"{{ {{ map = {Map}, activityRecovery = true, " +
            "gather = true, npcBank = true } }");
        Assert.Empty(j.Sent);
        Assert.False(File.Exists(j.Marker));
        Assert.Contains(j.Warnings, w => w.Contains("pods au seuil"));
    }

    [Fact]
    public void Missing_authoritative_pods_never_escape_strict_gather_even_with_available_resource()
    {
        using var j = new Journey();
        j.Resource();
        j.World.PodsAuthoritative = false;
        j.Route($"{{ {{ map = {Map}, activityRecovery = true, gather = true }} }}");
        Assert.Empty(j.Sent);
        Assert.False(File.Exists(j.Marker));
        Assert.Equal(DurableState.Clear,
            j.Durable.ReadStatus(new(Server, Character)).State);
        Assert.Contains(j.Warnings, w =>
            w.Contains("pods non confirmes", StringComparison.Ordinal));
    }

    [Fact]
    public void Late_completion_from_old_lua_generation_does_not_mutate_new_status()
    {
        using var j = new Journey();
        j.Resource();
        var generation = Field(typeof(Host), "_runGen");
        j.Host.ActivityRecoveryOfflineDispatchForTests = segment =>
        {
            j.Sent.Add(segment);
            // The interception stands in for the last send boundary. A
            // replacement script starts before the previous caller's finally.
            generation.SetValue(j.Host, (int)generation.GetValue(j.Host)! + 1);
            j.Resource(ready: false);
        };
        string route = $"{{ {{ map = {Map}, activityRecovery = true, gather = true }} }}";
        j.Route(route);
        Assert.Single(j.Sent);
        Assert.True(j.Host.ActivityRecoveryStatus?.IsQuarantined);
        Assert.False(j.Host.ActivityRecoveryStatus?.ObservedEffectWithoutAck);
        j.Route(route);
        Assert.Single(j.Sent);
        Assert.Equal(DurableState.Pending,
            j.Host.RefreshDurableActivityRecoveryStatus().State);
    }

    [Fact]
    public void Disabled_resource_without_exit_is_not_interpreted_as_a_completed_harvest()
    {
        using var j = new Journey();
        j.Resource(ready: false);
        j.Route($"{{ {{ map = {Map}, activityRecovery = true, gather = true }} }}");
        Assert.Empty(j.Sent);
        Assert.False(File.Exists(j.Marker));
        Assert.Equal(DurableState.Clear, j.Durable.ReadStatus(new(Server, Character)).State);
        Assert.Contains(j.Warnings, w => w.Contains("ressource epuisee"));
    }

    [Fact]
    public void Cancelled_lua_route_cannot_reenter_durable_admission()
    {
        using var j = new Journey();
        j.Resource();
        using var source = new CancellationTokenSource();
        source.Cancel();
        var stopped = Record.Exception(() =>
            j.Route($"{{ {{ map = {Map}, activityRecovery = true, gather = true }} }}",
                source.Token));
        Assert.NotNull(stopped);
        Assert.Equal("ScriptStopException", stopped.GetType().Name);
        Assert.Empty(j.Sent);
        Assert.False(File.Exists(j.Marker));
    }
}
