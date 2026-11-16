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
using Review = F06App::GivreClone.App.Services.ActivitySupplyContinuityReview;
using Reason = F06App::GivreClone.App.Services.ActivityRecoveryTransitionReasonV18;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using MapReader = F06App::GivreClone.App.Services.MapCellReader;
using WorldStore = F06App::GivreClone.App.Services.BotWorldStore;
using ViewModel = F06App::GivreClone.App.ViewModels.BotsViewModel;

namespace GivreClone.Tests;

/// <summary>
/// Actual MoonSharp RunLoop and its route/gather write-ahead path. Real
/// ActivityRecoveryDurableStore in a disposable directory, with only the
/// final gameplay boundary captured offline. There is no synthetic server
/// acknowledgement or live client.
/// </summary>
public sealed class ActivityRecoveryTransitionsV18Tests
{
    private const uint Pid = 71801;
    private const int Server = 9981;
    private const long Character = 9065400181;
    private const long Map = 90703360;
    private const int SourceCell = 240;
    private const int DestinationCell = 320;
    private const int Element = 45181;
    private const int ResourceType = 38;
    private const BindingFlags NonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

    private static FieldInfo Field(Type type, string name) =>
        type.GetField(name, NonPublic) ??
        throw new InvalidOperationException("V18 fixture field missing: " + name);

    private static string Generated(bool gather = true, long map = Map)
    {
        Assert.True(ViewModel.TryBuildStrictActivityRouteScript(
            map, DestinationCell, gather, 95, out var lua, out var refusal), refusal);
        Assert.Contains("ACTIVITY_RECOVERY_TABLE_ONLY = true", lua);
        return lua;
    }

    private static string GatherOnly() => $$"""
        ACTIVITY_RECOVERY = true
        ACTIVITY_RECOVERY_TABLE_ONLY = true
        ACTIVITY_RECOVERY_ROUTE = {
          { map = {{Map}}, activityRecovery = true, gather = true }
        }
        """;

    private sealed class Journey : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "GivreClone-ActivityRecoveryTransitionsV18",
            Guid.NewGuid().ToString("N"));
        public Launch Launch { get; }
        public WorldStore World { get; }
        public F06App::GivreClone.App.Services.BotWorldState State => World.Get(Pid);
        public Host Host { get; }
        public DurableStore Store { get; }
        public ConcurrentQueue<RecoverySegment> Attempts { get; } = new();
        public ConcurrentQueue<string> Logs { get; } = new();
        public int ResourceCell { get; }
        private readonly ConcurrentDictionary<long, IReadOnlyDictionary<int, int>>
            _positions = new();
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
                    (Server, "V18 offline");
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_lastMap")
                .GetValue(Launch)!)[Pid] = Map;
            ((ConcurrentDictionary<uint, long>)Field(typeof(Launch), "_interactivesDecodedMap")
                .GetValue(Launch)!)[Pid] = Map;

            var mapReader = (MapReader)RuntimeHelpers.GetUninitializedObject(typeof(MapReader));
            ResourceCell = Enumerable.Range(0, MapPathfinder.CellCount)
                .First(c => MapPathfinder.InteractionAdjacent(SourceCell, c));
            _positions[Map] = new Dictionary<int, int> { [Element] = ResourceCell };
            Field(typeof(MapReader), "_interPosCache").SetValue(mapReader, _positions);
            Field(typeof(Launch), "_cellReader").SetValue(Launch, mapReader);

            World = new WorldStore(Launch);
            State.MapId = Map;
            State.MapGeneration = 18;
            State.MapX = 0;
            State.MapY = 0;
            State.SelfCell = SourceCell;
            State.SelfCellMap = Map;
            State.SelfCellConfirmed = true;
            State.SelfActorId = Character;
            State.Cells = Enumerable.Range(0, MapPathfinder.CellCount)
                .Select(c => new MapCell(c, Mov: true, Los: true, Floor: 0,
                    MoveZone: 1, LinkedZone: 16)).ToArray();
            State.CellsMap = Map;
            State.RebuildWalk();
            State.Actors = new[] {
                new MapActor(SourceCell, 0, Character, "V18 offline actor", MapActorKind.Player)
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

        public void Resource(bool harvestable = true) =>
            State.Interactives = new[] {
                new MapInteractive(ResourceCell, Element, ResourceType, 0,
                    new[] { new InteractiveSkill(125, 45, 9, harvestable) },
                    "Blé", "Récolter")
            };

        public void ResourceFar()
        {
            int far = Enumerable.Range(0, MapPathfinder.CellCount)
                .First(c => MapPathfinder.CellDistance(c, SourceCell) > 8);
            _positions[Map] = new Dictionary<int, int> { [Element] = far };
            State.Interactives = new[] {
                new MapInteractive(far, Element, ResourceType, 0,
                    new[] { new InteractiveSkill(125, 45, 9, true) },
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
            State.MapGeneration = 19;
            State.CellsMap = next;
            State.SelfCellMap = next;
            State.SelfCellConfirmed = true;
            Absent();
        }

        public string Marker => Path.Combine(Root, "server-" + Server,
            "character-" + Character + ".pending");

        public Task<RecoverySegment> FirstAsync() =>
            _first.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public async Task CompleteAsync() =>
            await Host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10));

        public async Task StopAsync()
        {
            Host.Stop("user");
            await CompleteAsync();
        }

        public void Dispose()
        {
            Host.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public async Task Unmatched_strict_route_reports_exact_read_only_map_inspection()
    {
        using var journey = new Journey();
        journey.Resource();
        journey.Host.Start(Generated(map: Map + 1));
        await journey.CompleteAsync();
        var status = journey.Host.RefreshActivityRecoveryTransitionsV18Status();
        Assert.Empty(journey.Attempts);
        Assert.False(File.Exists(journey.Marker));
        Assert.Equal(DurableState.Clear, status.DurableStatus.State);
        Assert.Equal(Reason.RouteUnmatched, status.Reason);
        Assert.Equal(Review.InspectMapAndRoute, status.NextReview);
        Assert.False(status.RunActive);
        Assert.Null(status.RecordedSegment);
        Assert.False(status.HasSegmentAttributedServerReceipt);
        Assert.False(status.MayAutoDispatch);
        Assert.False(status.MayAutoClear);
        Assert.Equal(status, journey.Host.ActivityRecoveryTransitionsV18Status);
    }

    [Theory]
    [InlineData(false, 10, Reason.PodsUnconfirmed)]
    [InlineData(true, 96, Reason.PodsAtThreshold)]
    public async Task Missing_or_full_pods_produce_an_operator_step_without_a_marker(
        bool authoritative, int used, Reason expected)
    {
        using var journey = new Journey();
        journey.Resource();
        journey.State.PodsAuthoritative = authoritative;
        journey.State.Pods = (used, 100);
        journey.Host.Start(Generated());
        await journey.CompleteAsync();
        var status = journey.Host.RefreshActivityRecoveryTransitionsV18Status();
        Assert.Empty(journey.Attempts);
        Assert.False(File.Exists(journey.Marker));
        Assert.Equal(expected, status.Reason);
        Assert.Equal(Review.InspectPodsAndBank, status.NextReview);
        Assert.Equal(authoritative, status.WorldPodsPublicationObserved);
        Assert.Equal(authoritative ? (bool?)true : null, status.WorldPodsAtThreshold);
        Assert.False(status.PodsVerified); // no authenticated link/decoder in offline fixture
        Assert.False(status.MayAutoDispatch);
    }

    [Theory]
    [InlineData(false, Reason.ResourceUnavailable)]
    [InlineData(true, Reason.ResourceOutOfReach)]
    public async Task Gather_only_missing_or_distant_resource_reports_distinct_denial(
        bool distant, Reason expected)
    {
        using var journey = new Journey();
        if (distant) journey.ResourceFar(); else journey.Resource(harvestable: false);
        journey.Host.Start(GatherOnly());
        await journey.CompleteAsync();
        var status = journey.Host.RefreshActivityRecoveryTransitionsV18Status();
        Assert.Empty(journey.Attempts);
        Assert.False(File.Exists(journey.Marker));
        Assert.Equal(expected, status.Reason);
        Assert.Equal(Review.InspectResourceAvailability, status.NextReview);
        Assert.False(status.HasSegmentAttributedServerReceipt);
    }

    [Fact]
    public async Task One_pending_gather_wins_over_stop_resource_depletion_and_changed_map()
    {
        using var journey = new Journey();
        journey.Resource();
        journey.Host.Start(Generated());
        Assert.Equal(RecoveryKind.Gather, (await journey.FirstAsync()).Kind);
        await journey.StopAsync();
        byte[] marker = File.ReadAllBytes(journey.Marker);
        journey.Resource(harvestable: false);
        var depleted = journey.Host.RefreshActivityRecoveryTransitionsV18Status();
        Assert.Equal(DurableState.Pending, depleted.DurableStatus.State);
        Assert.Equal(RecoveryEvidence.ResourceExhaustedWithoutAttribution, depleted.Evidence);
        Assert.Equal(Reason.DurableAttemptPending, depleted.Reason);
        Assert.Equal(Review.InspectDurableAttempt, depleted.NextReview);
        Assert.Equal(Element, depleted.RecordedSegment?.ElementId);
        Assert.False(depleted.RunActive);

        journey.ChangedMap();
        var changed = journey.Host.RefreshActivityRecoveryTransitionsV18Status();
        Assert.Equal(RecoveryEvidence.MapChangedWithoutAttribution, changed.Evidence);
        Assert.Equal(Reason.DurableAttemptPending, changed.Reason);
        Assert.Equal(Review.InspectDurableAttempt, changed.NextReview);
        Assert.Equal(marker, File.ReadAllBytes(journey.Marker));
        Assert.Single(journey.Attempts);
    }

    [Fact]
    public async Task Stopped_without_attempt_is_inspection_only_and_has_no_fake_segment()
    {
        using var journey = new Journey();
        journey.Host.Start("function move() return false end");
        await journey.CompleteAsync();
        var status = journey.Host.RefreshActivityRecoveryTransitionsV18Status();
        Assert.Equal(DurableState.Clear, status.DurableStatus.State);
        Assert.Equal(Reason.RunInactive, status.Reason);
        Assert.Equal(Review.InspectStoppedRun, status.NextReview);
        Assert.Null(status.RecordedSegment);
        Assert.Empty(journey.Attempts);
        Assert.False(status.MayAutoClear);
    }

    [Fact]
    public async Task Old_run_callback_cannot_replace_new_CTS_transition_status()
    {
        using var journey = new Journey();
        journey.Resource();
        var replacement = new CancellationTokenSource();
        bool preserved = false;
        journey.Host.ActivityRecoveryOfflineDispatchForTests = segment =>
        {
            journey.Capture(segment);
            var field = Field(typeof(Host), "_cts");
            var old = (CancellationTokenSource)field.GetValue(journey.Host)!;
            var generation = Field(typeof(Host), "_runGen");
            field.SetValue(journey.Host, replacement);
            generation.SetValue(journey.Host, (int)generation.GetValue(journey.Host)! + 1);
            typeof(Host).GetMethod("NoteActivityRecoveryRunBlockV18", NonPublic)!
                .Invoke(journey.Host, new object[]
                {
                    Reason.RouteUnmatched, old.Token, "old callback cannot own new run"
                });
            preserved = ReferenceEquals(field.GetValue(journey.Host), replacement) &&
                !replacement.IsCancellationRequested;
            old.Cancel();
        };
        journey.Host.Start(Generated());
        await journey.FirstAsync();
        await journey.CompleteAsync();
        var status = journey.Host.RefreshActivityRecoveryTransitionsV18Status();
        Assert.True(preserved);
        Assert.Equal(Reason.None, status.LastRunBlock);
        Assert.Equal(Reason.DurableAttemptPending, status.Reason);
        Assert.Equal(Review.InspectDurableAttempt, status.NextReview);
        Assert.Single(journey.Attempts);
        Assert.Equal(DurableState.Pending,
            journey.Store.ReadStatus(new(Server, Character)).State);
        journey.Host.Stop("user");
        replacement.Dispose();
    }
}
