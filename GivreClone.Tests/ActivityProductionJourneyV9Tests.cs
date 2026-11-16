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
using RecoverySnapshot = F06App::GivreClone.App.Services.ActivityRecoverySnapshot;
using RecoveryWorkflow = F06App::GivreClone.App.Services.ActivityRecoveryWorkflow;
using RecoveryDecision = F06App::GivreClone.App.Services.ActivityRecoveryDecision;

namespace GivreClone.Tests;

/// <summary>
/// V9 regression journeys through the real MoonSharp RunRoute/GatherOne entry.
/// Only the last gameplay-send boundary is intercepted; filesystem quarantine
/// uses the real durable store and no client/socket/process is started.
/// </summary>
public sealed class ActivityProductionJourneyV9Tests
{
    private const long Map = 90703360;
    private const int Source = 240;
    private const int Waypoint = 320;
    private const int Element = 45678;
    private const int ResourceType = 38;
    private const int ResourceSkill = 45;
    private const int Server = 292;
    private const long Character = 1071683469604;
    private const uint Pid = 1;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static FieldInfo Field(Type owner, string name) =>
        owner.GetField(name, Private) ??
        throw new InvalidOperationException("missing fixture field " + name);

    private sealed class Journey : IDisposable
    {
        private readonly bool _ownsRoot;
        public LuaHostHarness Harness { get; }
        public Host Host => Harness.Host;
        public F06App::GivreClone.App.Services.BotWorldState State => Harness.World.Get(Pid);
        public DurableStore Durable { get; }
        public string Root { get; }
        public List<RecoverySegment> WouldSend { get; } = new();
        public List<string> Warnings { get; } = new();
        public int ResourceCell { get; }
        public int ServerId { get; }
        public long CharacterId { get; }

        public Journey(string? root = null, int serverId = Server, long characterId = Character)
        {
            _ownsRoot = root is null;
            Root = root ?? Path.Combine(Path.GetTempPath(),
                "GivreClone-ActivityJourneyV9", Guid.NewGuid().ToString("N"));
            ServerId = serverId;
            CharacterId = characterId;
            Durable = new DurableStore(Root);
            Harness = LuaHostHarness.Create();
            Host.ActivityRecoveryOfflineStoreForTests = Durable;
            Host.ActivityRecoveryOfflineDispatchForTests = WouldSend.Add;
            Host.ActivityRecoveryOfflineGatherAllowedForTests = true;
            Host.Log += entry => Warnings.Add(entry.Message);

            var launch = Harness.Launch;
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_charId")
                .GetValue(launch)!)[Pid] = characterId;
            ((ConcurrentDictionary<uint, (int Id, string Name)>)Field(typeof(Launch), "_serverInfo")
                .GetValue(launch)!)[Pid] = (serverId, "Test server");
            Harness.SetCurrentMap(Map);

            foreach (string name in new[] { "_linkDown", "_returnWait", "_autoLevelLock",
                "_autoStatSuspended", "_autoStatRequests", "_autoStatCancellation" })
            {
                var field = Field(typeof(Launch), name);
                field.SetValue(launch, Activator.CreateInstance(field.FieldType));
            }
            var decoded = new ConcurrentDictionary<uint, long>();
            decoded[Pid] = Map;
            Field(typeof(Launch), "_interactivesDecodedMap").SetValue(launch, decoded);

            var reader = (MapReader)RuntimeHelpers.GetUninitializedObject(typeof(MapReader));
            ResourceCell = Enumerable.Range(0, MapPathfinder.CellCount)
                .First(c => MapPathfinder.InteractionAdjacent(Source, c));
            var positions = new ConcurrentDictionary<long, IReadOnlyDictionary<int, int>>();
            positions[Map] = new Dictionary<int, int> { [Element] = ResourceCell };
            Field(typeof(MapReader), "_interPosCache").SetValue(reader, positions);
            Field(typeof(Launch), "_cellReader").SetValue(launch, reader);

            State.MapId = Map;
            State.MapGeneration = 12;
            State.MapX = 0;
            State.MapY = 0;
            State.SelfCell = Source;
            State.SelfCellMap = Map;
            State.SelfCellConfirmed = true;
            State.SelfActorId = characterId;
            // Simulated observed pods for a positive offline harvest journey.
            // Production still rejects non-authoritative pods before emission.
            State.PodsAuthoritative = true;
            State.Pods = (10, 100);
            State.Cells = Enumerable.Range(0, MapPathfinder.CellCount)
                .Select(c => new MapCell(c, Mov: true, Los: true, Floor: 0,
                    MoveZone: 1, LinkedZone: 16)).ToArray();
            State.CellsMap = Map;
            State.RebuildWalk();
            State.Actors = new[]
            {
                new MapActor(Source, 0, characterId, "Test persona", MapActorKind.Player)
            };
        }

        public void Resource(int skillUid, bool harvestable = true)
        {
            State.Interactives = new[]
            {
                new MapInteractive(ResourceCell, Element, ResourceType, 0,
                    new[] { new InteractiveSkill(skillUid, ResourceSkill, 9, harvestable) },
                    "Ble", "Recolter")
            };
        }

        public void Route(string body)
        {
            Harness.Run("ELEMENTS_TO_GATHER = {" + ResourceType + "}");
            Harness.Run("function move() return " + body + " end");
            var lua = Harness.Run("return move()");
            Assert.Equal(DataType.Table, lua.Type);
            var entry = typeof(Host).GetMethod("RunRoute", Private)!;
            try { entry.Invoke(Host, new object[] { lua.Table, CancellationToken.None, 0 }); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        public RecoverySnapshot Snapshot(bool inReach = true)
        {
            var observe = typeof(Host).GetMethod("ObserveActivity", Private)!;
            return (RecoverySnapshot)observe.Invoke(Host,
                new object[] { Element, ResourceCell, inReach, CancellationToken.None })!;
        }

        public string Marker => Path.Combine(Root, "server-" + ServerId,
            "character-" + CharacterId + ".pending");

        public void Dispose()
        {
            Harness.Dispose();
            if (_ownsRoot && Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public void Lua_gather_persists_exact_skill_uid_and_cannot_replay_after_skill_republication()
    {
        using var journey = new Journey();
        journey.Resource(skillUid: 125);
        string route = $"{{ {{ map = {Map}, activityRecovery = true, gather = true }} }}";
        journey.Route(route);
        Assert.Single(journey.WouldSend);
        Assert.Equal(RecoveryKind.Gather, journey.WouldSend[0].Kind);
        Assert.Contains("\"SkillInstanceUid\":125", File.ReadAllText(journey.Marker));

        // The element ID and cell are unchanged, but a new server skill
        // instance has replaced the original target after a possible send.
        journey.Resource(skillUid: 126);
        journey.Route(route);
        Assert.Single(journey.WouldSend);
        Assert.Equal(DurableState.Pending,
            journey.Durable.ReadStatus(new(Server, Character)).State);
        Assert.False(journey.Host.RefreshActivityRecoveryReconciliationStatus().MayRedispatch);
    }

    [Fact]
    public void Republished_skill_invalidates_preflight_before_a_durable_reservation()
    {
        using var journey = new Journey();
        journey.Resource(skillUid: 125);
        var before = journey.Snapshot();
        var workflow = RecoveryWorkflow.Start(Map);
        var segment = new RecoverySegment(RecoveryKind.Gather, Element,
            journey.ResourceCell, Element);
        var evaluation = workflow.Evaluate(segment, before);
        Assert.Equal(RecoveryDecision.Ready, evaluation.Decision);

        journey.Resource(skillUid: 126);
        var after = journey.Snapshot();
        Assert.NotEqual(before, after);
        Assert.Equal(125, before.SkillInstanceUid);
        Assert.Equal(126, after.SkillInstanceUid);
        Assert.Same(workflow, workflow.Begin(evaluation, after));
        Assert.False(File.Exists(journey.Marker));
    }

    [Fact]
    public void Blacklisted_but_still_harvestable_resource_blocks_waypoint_progress()
    {
        using var journey = new Journey();
        journey.Resource(skillUid: 125);
        // In a real route this blacklist is set by GatherOne after a refused
        // or unreachable harvest. Re-enter the actual Lua route with that state.
        ((HashSet<int>)Field(typeof(Host), "_gatherBlocked").GetValue(journey.Host)!)
            .Add(Element);
        Field(typeof(Host), "_gatherBlockedMap").SetValue(journey.Host, Map);

        journey.Route($"{{ {{ map = {Map}, activityRecovery = true, " +
            $"gather = true, cell = {Waypoint} }} }}");
        Assert.Empty(journey.WouldSend);
        Assert.False(File.Exists(journey.Marker));
        Assert.Contains(journey.Warnings,
            warning => warning.Contains("ressource encore disponible mais bloquee"));
    }

    [Fact]
    public void Same_pid_new_server_character_is_independent_from_old_durable_marker()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "GivreClone-ActivityJourneyV9", Guid.NewGuid().ToString("N"));
        try
        {
            string route = $"{{ {{ map = {Map}, activityRecovery = true, cell = {Waypoint} }} }}";
            using (var previous = new Journey(root))
            {
                previous.Route(route);
                Assert.Single(previous.WouldSend);
                Assert.True(File.Exists(previous.Marker));
            }
            using (var next = new Journey(root, serverId: Server + 1,
                       characterId: Character + 1))
            {
                next.Route(route);
                Assert.Single(next.WouldSend);
                Assert.True(File.Exists(next.Marker));
                Assert.Equal(DurableState.Pending,
                    next.Durable.ReadStatus(new(next.ServerId, next.CharacterId)).State);
                Assert.Equal(DurableState.Pending,
                    next.Durable.ReadStatus(new(Server, Character)).State);
            }
            using (var formerReturns = new Journey(root))
            {
                formerReturns.Route(route);
                Assert.Empty(formerReturns.WouldSend);
                Assert.Equal(DurableState.Pending,
                    formerReturns.Host.DurableActivityRecoveryStatus.State);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Bank_deposit_step_refuses_composite_operation_without_a_durable_send()
    {
        using var journey = new Journey();
        journey.Route($"{{ {{ map = {Map}, activityRecovery = true, npcBank = true }} }}");
        Assert.Empty(journey.WouldSend);
        Assert.False(File.Exists(journey.Marker));
        Assert.Contains(journey.Warnings,
            warning => warning.Contains("etape Lua composite non prise en charge"));
    }
}
