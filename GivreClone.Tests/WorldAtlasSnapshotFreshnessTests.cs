extern alias F06App;
using System;
using System.Collections.Generic;
using System.Linq;
using Atlas = F06App::GivreClone.App.ViewModels;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Offline regression fixtures inspired by GivreActive's
/// MapViewerSnapshotFreshnessTests.ps1. No client, packets or native module.
/// </summary>
public sealed class WorldAtlasSnapshotFreshnessTests
{
    private static Atlas.WorldAtlasEvidence Proof(string version = "fixture-build-A")
        => new(Atlas.WorldAtlasEvidenceState.Verified, "fixture", version,
            new string('a', 64), "offline-case");

    private static Atlas.WorldAtlasMapRecord Map(long id, string label = "same map",
        string version = "fixture-build-A")
        => new(id, 1, 2, "zone", Atlas.WorldAtlasLayer.Surface, label, Proof(version));

    private static Atlas.WorldAtlasSnapshot Snapshot(long destination, string edgeLabel = "edge",
        string version = "fixture-build-A")
        => new("fixture", Proof(version),
            new[] { Map(100, version: version), Map(destination, version: version) },
            new[] { new Atlas.WorldAtlasTransitionRecord(100, destination, edgeLabel, Proof(version)) });

    private static Atlas.WorldAtlasViewModel SelectedAtlas()
    {
        var vm = new Atlas.WorldAtlasViewModel();
        Assert.True(vm.LoadSnapshot(Snapshot(200)));
        vm.SelectedMap = Assert.Single(vm.Results.Where(m => m.MapId == 100));
        return vm;
    }

    [Fact]
    public void Replacement_rebuilds_transition_rows_even_when_map_records_have_identical_value()
    {
        var vm = SelectedAtlas();
        var oldRow = Assert.Single(vm.SelectedTransitions);
        var initialGeneration = vm.SnapshotGeneration;
        Assert.True(vm.LoadSnapshot(Snapshot(300, "replacement-edge")));
        Assert.Equal(initialGeneration + 1, vm.SnapshotGeneration);
        Assert.Equal(100L, vm.SelectedMap?.MapId);
        var next = Assert.Single(vm.SelectedTransitions);
        Assert.Equal(300L, next.ToMapId);
        Assert.Equal("replacement-edge", next.KindLabel);
        Assert.NotSame(oldRow, next);
        Assert.Equal(vm.SnapshotGeneration, next.SnapshotGeneration);
    }

    [Fact]
    public void Old_transition_is_rejected_after_snapshot_replacement_without_changing_selection()
    {
        var vm = SelectedAtlas();
        var old = Assert.Single(vm.SelectedTransitions);
        Assert.True(vm.LoadSnapshot(Snapshot(300)));
        vm.InspectDestinationCommand.Execute(old);
        Assert.Equal(100L, vm.SelectedMap?.MapId);
        Assert.Equal("", vm.SearchMapId);
        Assert.Contains("périmé", vm.StatusText);
        Assert.Equal(300L, Assert.Single(vm.SelectedTransitions).ToMapId);
    }

    [Fact]
    public void Old_transition_rejected_even_if_replacement_has_identical_map_ids_and_edges()
    {
        var vm = SelectedAtlas();
        var old = Assert.Single(vm.SelectedTransitions);
        Assert.True(vm.LoadSnapshot(Snapshot(200)));
        var replacement = Assert.Single(vm.SelectedTransitions);
        Assert.NotSame(old, replacement);
        vm.InspectDestinationCommand.Execute(old);
        Assert.Equal(100L, vm.SelectedMap?.MapId);
        Assert.Equal("", vm.SearchMapId);
        Assert.Contains("périmé", vm.StatusText);
    }

    [Fact]
    public void Transition_cannot_be_used_after_selection_switch_or_stale_object_injection()
    {
        var vm = SelectedAtlas();
        var fromFirst = Assert.Single(vm.SelectedTransitions);
        vm.SelectedMap = Assert.Single(vm.Results.Where(m => m.MapId == 200));
        vm.InspectDestinationCommand.Execute(fromFirst);
        Assert.Equal(200L, vm.SelectedMap?.MapId);
        Assert.Contains("périmé", vm.StatusText);

        // Simulate a delayed binding event pointing to a prior snapshot record.
        var retiredMap = vm.SelectedMap;
        Assert.True(vm.LoadSnapshot(Snapshot(300)));
        vm.SelectedMap = retiredMap;
        Assert.Null(vm.SelectedMap);
        Assert.Empty(vm.SelectedTransitions);
    }

    [Fact]
    public void Current_transition_is_still_inspectable_offline()
    {
        var vm = SelectedAtlas();
        var current = Assert.Single(vm.SelectedTransitions);
        vm.InspectDestinationCommand.Execute(current);
        Assert.Equal(200L, vm.SelectedMap?.MapId);
        Assert.Equal("200", vm.SearchMapId);
    }

    [Fact]
    public void Rejected_invalid_snapshot_does_not_revoke_current_generation()
    {
        var vm = SelectedAtlas();
        var current = Assert.Single(vm.SelectedTransitions);
        var generation = vm.SnapshotGeneration;
        var invalid = new Atlas.WorldAtlasSnapshot("invalid", Proof(),
            new[] { Map(100), Map(100) }, Array.Empty<Atlas.WorldAtlasTransitionRecord>());
        Assert.False(vm.LoadSnapshot(invalid));
        Assert.Equal(generation, vm.SnapshotGeneration);
        vm.InspectDestinationCommand.Execute(current);
        Assert.Equal(200L, vm.SelectedMap?.MapId);
    }

    [Fact]
    public void Published_snapshot_is_frozen_against_caller_list_mutation()
    {
        var maps = new List<Atlas.WorldAtlasMapRecord> { Map(100), Map(200) };
        var transitions = new List<Atlas.WorldAtlasTransitionRecord>
        {
            new(100, 200, "original-edge", Proof())
        };
        var source = new Atlas.WorldAtlasSnapshot("mutable-fixture", Proof(), maps, transitions);
        var vm = new Atlas.WorldAtlasViewModel();
        Assert.True(vm.LoadSnapshot(source));
        vm.SelectedMap = Assert.Single(vm.Results.Where(m => m.MapId == 100));
        Assert.Equal(200L, Assert.Single(vm.SelectedTransitions).ToMapId);

        maps.Clear();
        maps.Add(Map(900));
        transitions.Clear();
        transitions.Add(new Atlas.WorldAtlasTransitionRecord(100, 900, "injected", Proof()));

        vm.ClearSearchCommand.Execute(null);
        Assert.Equal(new long[] { 100, 200 }, vm.Results.Select(m => m.MapId).ToArray());
        vm.SelectedMap = Assert.Single(vm.Results.Where(m => m.MapId == 100));
        Assert.Equal(200L, Assert.Single(vm.SelectedTransitions).ToMapId);
        Assert.Equal("original-edge", Assert.Single(vm.SelectedTransitions).KindLabel);
    }

    [Fact]
    public void Old_manual_entry_must_not_remove_new_entry_with_same_map_id()
    {
        var vm = new Atlas.WorldAtlasViewModel { ManualMapId = "900" };
        vm.AddManualCommand.Execute(null);
        var old = Assert.Single(vm.Results);
        vm.RemoveManualCommand.Execute(old);
        Assert.Empty(vm.Results);
        vm.AddManualCommand.Execute(null);
        var successor = Assert.Single(vm.Results);
        Assert.NotSame(old, successor);
        vm.RemoveManualCommand.Execute(old);
        Assert.Same(successor, Assert.Single(vm.Results));
        vm.RemoveManualCommand.Execute(successor);
        Assert.Empty(vm.Results);
    }

    private static Atlas.WorldAtlasMonsterSnapshot Actors(
        Atlas.WorldAtlasMonsterGroup[]? groups = null,
        string sessionId = "session-A", string clientVersion = "fixture-build-A",
        long sessionGeneration = 8, long mapGeneration = 12, long actorGeneration = 55,
        long mapId = 100, string mapSnapshotId = "map-object-A",
        string actorSnapshotId = "actor-object-A")
        => new(sessionId, clientVersion, sessionGeneration, mapGeneration, actorGeneration,
            mapSnapshotId, actorSnapshotId, mapId,
            groups ?? [new Atlas.WorldAtlasMonsterGroup(41, 501, 78)]);

    private static Atlas.WorldAtlasMonsterSelection Capture(Atlas.WorldAtlasMonsterSnapshot? snapshot = null)
    {
        Assert.True(Atlas.WorldAtlasMonsterActionGuard.TryCapture(snapshot ?? Actors(), 41, out var selection));
        return Assert.IsType<Atlas.WorldAtlasMonsterSelection>(selection);
    }

    private static void Refused(Atlas.WorldAtlasMonsterSelection selection,
        Atlas.WorldAtlasMonsterSnapshot snapshot)
    {
        Assert.False(Atlas.WorldAtlasMonsterActionGuard.TryResolveAction(selection, snapshot, out var resolved));
        Assert.Null(resolved);
    }

    [Fact]
    public void Exact_group_session_version_and_generations_are_required_for_action()
    {
        var selection = Capture();
        Assert.True(Atlas.WorldAtlasMonsterActionGuard.TryResolveAction(selection, Actors(), out var group));
        Assert.Equal(41L, group!.ContextualId);
        Assert.Equal(501L, group.InstanceGeneration);
        Assert.Equal(78, group.CellId);
    }

    [Fact]
    public void Disappeared_group_does_not_transfer_action_to_group_in_same_cell()
    {
        var selection = Capture();
        Refused(selection, Actors([]));
        Refused(selection, Actors([new Atlas.WorldAtlasMonsterGroup(72, 999, 78)]));
        Refused(selection, Actors([new Atlas.WorldAtlasMonsterGroup(41, 502, 78)]));
        Refused(selection, Actors([new Atlas.WorldAtlasMonsterGroup(41, 501, 79)]));
    }

    [Fact]
    public void Reconnect_or_different_session_revokes_monster_target()
    {
        var selection = Capture();
        Refused(selection, Actors(sessionId: "session-B"));
        Refused(selection, Actors(sessionGeneration: 9));
        Refused(selection, Actors(sessionGeneration: 7));
        Refused(selection, Actors(clientVersion: "fixture-build-B"));
        Refused(selection, Actors(clientVersion: ""));
    }

    [Fact]
    public void Replaced_map_and_new_actor_snapshot_revoke_monster_target()
    {
        var selection = Capture();
        Refused(selection, Actors(mapId: 200));
        Refused(selection, Actors(mapGeneration: 13));
        Refused(selection, Actors(mapGeneration: 11));
        Refused(selection, Actors(mapSnapshotId: "map-object-replaced"));
        Refused(selection, Actors(actorGeneration: 56));
        Refused(selection, Actors(actorGeneration: 54));
        Refused(selection, Actors(actorSnapshotId: "actor-object-replaced"));
    }

    [Fact]
    public void Malformed_or_duplicate_group_evidence_is_never_an_action_authorization()
    {
        Assert.False(Atlas.WorldAtlasMonsterActionGuard.TryCapture(null, 41, out _));
        Assert.False(Atlas.WorldAtlasMonsterActionGuard.TryCapture(Actors(sessionId: ""), 41, out _));
        Assert.False(Atlas.WorldAtlasMonsterActionGuard.TryCapture(Actors(sessionGeneration: 0), 41, out _));
        Assert.False(Atlas.WorldAtlasMonsterActionGuard.TryCapture(Actors(actorGeneration: 0), 41, out _));
        Assert.False(Atlas.WorldAtlasMonsterActionGuard.TryCapture(Actors(mapGeneration: 0), 41, out _));
        Assert.False(Atlas.WorldAtlasMonsterActionGuard.TryCapture(Actors(mapId: 0), 41, out _));
        Assert.False(Atlas.WorldAtlasMonsterActionGuard.TryCapture(Actors(mapSnapshotId: ""), 41, out _));
        Assert.False(Atlas.WorldAtlasMonsterActionGuard.TryCapture(Actors(actorSnapshotId: ""), 41, out _));
        Assert.False(Atlas.WorldAtlasMonsterActionGuard.TryCapture(Actors([]), 41, out _));
        Assert.False(Atlas.WorldAtlasMonsterActionGuard.TryCapture(Actors(), 72, out _));
        Assert.False(Atlas.WorldAtlasMonsterActionGuard.TryCapture(Actors(
            [new Atlas.WorldAtlasMonsterGroup(41, 501, 78),
             new Atlas.WorldAtlasMonsterGroup(41, 502, 78)]), 41, out _));
        Assert.False(Atlas.WorldAtlasMonsterActionGuard.TryCapture(Actors(
            [new Atlas.WorldAtlasMonsterGroup(41, 0, 78)]), 41, out _));
        Assert.False(Atlas.WorldAtlasMonsterActionGuard.TryCapture(Actors(
            [new Atlas.WorldAtlasMonsterGroup(41, 501, -1)]), 41, out _));

        var selection = Capture();
        Refused(selection, Actors([new Atlas.WorldAtlasMonsterGroup(41, 501, 78),
            new Atlas.WorldAtlasMonsterGroup(41, 501, 78)]));
        Assert.False(Atlas.WorldAtlasMonsterActionGuard.TryResolveAction(null, Actors(), out _));
    }

    [Fact]
    public void Caller_cannot_forge_missing_generation_or_version_in_selection()
    {
        var valid = Capture();
        Refused(valid with { SessionGeneration = 0 }, Actors());
        Refused(valid with { MapGeneration = 0 }, Actors());
        Refused(valid with { ActorGeneration = 0 }, Actors());
        Refused(valid with { ClientVersion = "" }, Actors());
        Refused(valid with { SessionId = "" }, Actors());
        Refused(valid with { MapSnapshotId = "" }, Actors());
        Refused(valid with { ActorSnapshotId = "" }, Actors());
        Refused(valid with { MapId = 0 }, Actors());
        Refused(valid with { InstanceGeneration = 0 }, Actors());
        Refused(valid with { ContextualId = 0 }, Actors());
        Refused(valid with { CellId = -1 }, Actors());
    }
}
