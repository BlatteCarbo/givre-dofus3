extern alias F06App;

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using GivreClone.Protocol;
using Xunit;
using World = F06App::GivreClone.App.Services.BotWorldState;
using Lease = F06App::GivreClone.App.Services.MonsterAttackTargetLease;

namespace GivreClone.Tests;

public sealed class MonsterAttackTargetLeaseTests
{
    private static MapActor Monster(long id = -501, int cell = 234) =>
        new(cell, 0, id, "groupe", MapActorKind.Monster);

    private static World WorldWith(MapActor actor) => new()
    {
        MapId = 42,
        MapGeneration = 1,
        SelfCell = 200,
        SelfCellMap = 42,
        SelfCellConfirmed = true,
        Actors = new[] { actor }
    };

    [Fact]
    public void Exact_observed_monster_can_be_selected_and_stays_valid()
    {
        var actor = Monster();
        var world = WorldWith(actor);
        var lease = Assert.IsType<Lease>(Lease.Capture(world, 42, actor));
        Assert.True(lease.IsCurrent(world));
    }

    [Fact]
    public void Disappeared_group_must_be_refused_before_deferred_attack()
    {
        var actor = Monster();
        var world = WorldWith(actor);
        var lease = Assert.IsType<Lease>(Lease.Capture(world, 42, actor));
        world.Actors = Array.Empty<MapActor>();
        Assert.False(lease.IsCurrent(world));
    }

    [Fact]
    public void Replaced_group_with_same_contextual_id_cell_and_name_is_not_the_click()
    {
        var actor = Monster();
        var world = WorldWith(actor);
        var lease = Assert.IsType<Lease>(Lease.Capture(world, 42, actor));
        world.Actors = new[] { Monster() }; // values equal, object/observation different
        Assert.False(lease.IsCurrent(world));
        Assert.Null(Lease.Capture(world, 42, actor));
    }

    [Fact]
    public void Moved_group_or_duplicate_contextual_id_is_not_authorized()
    {
        var actor = Monster();
        var world = WorldWith(actor);
        var lease = Assert.IsType<Lease>(Lease.Capture(world, 42, actor));
        world.Actors = new[] { actor with { CellId = 235 } };
        Assert.False(lease.IsCurrent(world));
        world.Actors = new[] { actor, actor with { Name = "reused" } };
        Assert.False(lease.IsCurrent(world));
    }

    [Fact]
    public void Old_map_generation_or_recycled_map_id_cannot_reauthorize_a_click()
    {
        var actor = Monster();
        var world = WorldWith(actor);
        var lease = Assert.IsType<Lease>(Lease.Capture(world, 42, actor));
        Interlocked.Increment(ref world.MapGeneration);
        Assert.False(lease.IsCurrent(world));
        world.MapGeneration = 1;
        world.MapId = 43;
        Assert.False(lease.IsCurrent(world));
    }

    [Fact]
    public void Unconfirmed_self_position_or_unknown_map_epoch_must_fail_closed()
    {
        var actor = Monster();
        var world = WorldWith(actor);
        var lease = Assert.IsType<Lease>(Lease.Capture(world, 42, actor));
        world.SelfCellConfirmed = false;
        // A move temporarily predicts self before echo. The target lease must
        // survive this transition so the ACK can complete a queued attack.
        Assert.True(lease.IsCurrent(world));
        Assert.Null(Lease.Capture(world, 42, actor));
        world.SelfCellConfirmed = true;
        world.MapGeneration = 0;
        Assert.Null(Lease.Capture(world, 42, actor));
    }

    [Theory]
    [InlineData(501L, MapActorKind.Monster)]
    [InlineData(-501L, MapActorKind.Npc)]
    [InlineData(-501L, MapActorKind.Player)]
    public void Only_negative_contextual_ids_of_monster_actors_are_attackable(long id, MapActorKind kind)
    {
        var actor = Monster(id) with { Kind = kind };
        Assert.Null(Lease.Capture(WorldWith(actor), 42, actor));
    }

    [Fact]
    public void Unrelated_actor_update_preserves_same_group_identity()
    {
        var actor = Monster();
        var world = WorldWith(actor);
        var lease = Assert.IsType<Lease>(Lease.Capture(world, 42, actor));
        world.Actors = new[] { actor, new MapActor(100, 0, 777, "joueur", MapActorKind.Player) };
        Assert.True(lease.IsCurrent(world));
    }

    [Theory]
    [InlineData(100L, 100L, true)]
    [InlineData(100L, 45_100L, true)]
    [InlineData(100L, 45_101L, false)]
    [InlineData(100L, 99L, false)]
    [InlineData(-1L, 200L, false)]
    public void Deferred_attack_has_bounded_monotonic_age(long requestedAt, long now, bool expected)
        => Assert.Equal(expected, Lease.IsPendingFresh(requestedAt, now));

    [Theory]
    [InlineData(42, 42, 42, 200, 200, 10, 11, true)]
    [InlineData(42, 0, 42, 200, 200, 10, 11, false)] // ACK with no server echo
    [InlineData(42, 41, 42, 200, 200, 10, 11, false)] // echo of previous move
    [InlineData(42, 42, 43, 200, 200, 10, 11, false)] // a new move replaced the pending one
    [InlineData(42, 42, 42, 200, 201, 10, 11, false)] // changed map
    [InlineData(42, 42, 42, 200, 200, 10, 45011, false)] // expired
    public void Ack_does_not_dispatch_an_unrelated_movement(long requestedSeq, long echoedSeq,
        long trackedSeq, long requestedMap, long currentMap, long sentAt, long now, bool expected)
        => Assert.Equal(expected, Lease.MatchesConfirmedMovement(requestedSeq, echoedSeq,
            trackedSeq, requestedMap, currentMap, sentAt, now));

    [Fact]
    public void Attack_after_move_requires_server_confirmed_self_cell_and_adjacency()
    {
        var actor = Monster();
        var world = WorldWith(actor);
        var lease = Assert.IsType<Lease>(Lease.Capture(world, 42, actor));
        int adjacent = Enumerable.Range(0, MapPathfinder.CellCount)
            .First(c => MapPathfinder.CellDistance(c, actor.CellId) == 1);
        world.SelfCell = adjacent;
        world.SelfCellConfirmed = false;
        Assert.False(lease.IsAtConfirmedArrival(world, adjacent)); // injected prediction
        world.SelfCellConfirmed = true;
        Assert.True(lease.IsAtConfirmedArrival(world, adjacent)); // server echo
        Assert.False(lease.IsAtConfirmedArrival(world, world.SelfCell.Value + 1)); // different destination
        world.SelfCell = 0;
        Assert.False(lease.IsAtConfirmedArrival(world, adjacent));
    }

    [Fact]
    public void In_flight_actor_publication_cannot_be_seen_as_a_stable_snapshot()
    {
        var actor = Monster();
        var world = WorldWith(actor);
        var lease = Assert.IsType<Lease>(Lease.Capture(world, 42, actor));
        Interlocked.Increment(ref world.ActorPublicationSequence); // odd: update not finished
        Assert.False(lease.IsCurrent(world));
        Assert.Null(Lease.Capture(world, 42, actor));
        Interlocked.Increment(ref world.ActorPublicationSequence); // even: update finished
        Assert.True(lease.IsCurrent(world));
    }

    [Fact]
    public void Target_invalidation_prevents_dispatch_in_the_final_critical_section()
    {
        var actor = Monster();
        var world = WorldWith(actor);
        int adjacent = Enumerable.Range(0, MapPathfinder.CellCount)
            .First(c => MapPathfinder.CellDistance(c, actor.CellId) == 1);
        world.SelfCell = adjacent;
        var lease = Assert.IsType<Lease>(Lease.Capture(world, 42, actor));
        bool submitted = false;
        Assert.True(lease.TrySubmitFromConfirmedCell(world, adjacent, () => { submitted = true; return true; }));
        Assert.True(submitted);

        submitted = false;
        world.Actors = new[] { Monster() };
        Assert.False(lease.TrySubmitFromConfirmedCell(world, adjacent, () => { submitted = true; return true; }));
        Assert.False(submitted);
    }

    [Fact]
    public void Dispatch_is_held_under_same_gate_as_actor_publication()
    {
        var actor = Monster();
        var world = WorldWith(actor);
        int adjacent = Enumerable.Range(0, MapPathfinder.CellCount)
            .First(c => MapPathfinder.CellDistance(c, actor.CellId) == 1);
        world.SelfCell = adjacent;
        var lease = Assert.IsType<Lease>(Lease.Capture(world, 42, actor));
        var gate = typeof(World).GetField("ActorPublicationGate",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!;
        Assert.True(lease.TrySubmitFromConfirmedCell(world, adjacent, () =>
        {
            Assert.True(Monitor.IsEntered(gate));
            Assert.True(lease.IsAtConfirmedArrival(world, adjacent));
            return true;
        }));
    }
}
