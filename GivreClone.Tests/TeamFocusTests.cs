using System.Collections.Generic;
using GivreClone.App.Services.Combat;
using Xunit;

namespace GivreClone.Tests;

public class TeamFocusTests
{
    [Fact]
    public void Leader_target_moves_to_the_front_and_the_rest_keeps_its_order()
    {
        var ordered = new List<(int Cell, long ActorId)> { (1, -10), (2, -20), (3, -30) };
        Assert.Equal(new long[] { -30, -10, -20 }, TeamFocus.LeaderTargetFirst(ordered, e => e.ActorId, -30).ConvertAll(e => e.ActorId));
    }

    [Fact]
    public void Missing_or_absent_leader_target_leaves_the_order_unchanged()
    {
        var ordered = new List<(int Cell, long ActorId)> { (1, -10), (2, -20) };
        Assert.Same(ordered, TeamFocus.LeaderTargetFirst(ordered, e => e.ActorId, null));
        Assert.Same(ordered, TeamFocus.LeaderTargetFirst(ordered, e => e.ActorId, -99));
        Assert.Same(ordered, TeamFocus.LeaderTargetFirst(ordered, e => e.ActorId, -10));   // déjà en tête
    }
}
