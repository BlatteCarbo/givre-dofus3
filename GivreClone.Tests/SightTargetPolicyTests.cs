using GivreClone.Engine;
using Xunit;

namespace GivreClone.Tests;

public sealed class SightTargetPolicyTests
{
    [Fact]
    public void Movement_keeps_target_but_next_turn_reselects_from_current_position()
    {
        var policy = new SightTargetPolicy();
        var enemies = new[] { (Cell: 114, ActorId: -1L), (Cell: 142, ActorId: -2L) };
        Assert.Equal(-1, policy.Select(1, 100, enemies, System.Array.Empty<long>()));
        Assert.Equal(-1, policy.Select(1, 156, enemies, System.Array.Empty<long>()));
        Assert.Equal(-2, policy.Select(2, 156, enemies, System.Array.Empty<long>()));
    }

    [Fact]
    public void Death_reselects_from_current_position_and_server_target_takes_priority()
    {
        var policy = new SightTargetPolicy();
        Assert.Equal(-1, policy.Select(1, 100, new[] { (114, -1L) }, System.Array.Empty<long>()));
        var survivors = new[] { (128, -2L), (156, -3L) };
        Assert.Equal(-3, policy.Select(1, 170, survivors, System.Array.Empty<long>()));
        Assert.Equal(-2, policy.Select(1, 170, survivors, new[] { -2L }));
        Assert.Equal(-2, policy.Select(1, 170, survivors, System.Array.Empty<long>()));
    }

    [Fact]
    public void Equal_distances_are_stable_and_dead_designations_are_ignored()
    {
        var policy = new SightTargetPolicy();
        var enemies = new[] { (114, -1L), (114, -2L) };
        Assert.Equal(-2, policy.Select(1, 100, enemies, new[] { -99L }));
        Assert.Equal(0, policy.Select(2, -1, enemies, System.Array.Empty<long>()));
        Assert.Equal(0, policy.Select(3, 100, System.Array.Empty<(int, long)>(), System.Array.Empty<long>()));
    }
}
