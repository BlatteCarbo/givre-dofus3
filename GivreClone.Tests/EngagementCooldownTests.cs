using GivreClone.Engine;
using Xunit;

namespace GivreClone.Tests;
public class EngagementCooldownTests
{
    [Fact]
    public void Failed_group_yields_to_another_then_becomes_eligible_after_a_minute()
    {
        var c = new EngagementCooldown();
        c.Failed(11, -20, 100);
        var candidates = new long[] { -20, -21 };
        Assert.Equal(new long[] { -21 }, candidates.Except(c.Excluded(11, 101)));
        Assert.Equal(candidates, candidates.Except(c.Excluded(11, 60_100)));
    }
    [Fact]
    public void Actor_ids_reused_on_another_map_are_not_blocked()
    {
        var c = new EngagementCooldown(); c.Failed(11, -20, 100);
        Assert.Empty(c.Excluded(12, 101));
        Assert.Empty(c.Excluded(11, 102));
    }
    [Fact]
    public void Each_failure_keeps_its_own_expiration()
    {
        var c = new EngagementCooldown(); c.Failed(11, -20, 100); c.Failed(11, -21, 200);
        Assert.Equal(new long[] { -21 }, c.Excluded(11, 60_100));
    }
}
