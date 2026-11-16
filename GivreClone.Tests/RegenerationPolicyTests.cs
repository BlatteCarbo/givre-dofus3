using GivreClone.Engine;
using Xunit;

namespace GivreClone.Tests;

public class RegenerationPolicyTests
{
    [Fact]
    public void Defaults_are_off_and_do_not_gate_unknown_life()
    {
        var options = new RegenerationOptions();
        Assert.False(options.Enabled);
        Assert.False(options.WaitForTeam);
        Assert.Equal(RegenerationDecision.Ready, RegenerationPolicy.Decide(options, null));
    }

    [Theory]
    [InlineData(null, false, RegenerationDecision.WaitForLife)]
    [InlineData(-1, false, RegenerationDecision.WaitForLife)]
    [InlineData(101, true, RegenerationDecision.WaitForLife)]
    [InlineData(0, false, RegenerationDecision.Regenerate)]
    [InlineData(59, false, RegenerationDecision.Regenerate)]
    [InlineData(60, false, RegenerationDecision.Ready)]
    [InlineData(60, true, RegenerationDecision.Regenerate)]
    [InlineData(89, true, RegenerationDecision.Regenerate)]
    [InlineData(90, true, RegenerationDecision.Ready)]
    public void Threshold_and_recovery_hysteresis_use_only_the_supplied_observation(
        int? life, bool recovering, RegenerationDecision expected)
    {
        Assert.Equal(expected, RegenerationPolicy.Decide(new(true, 60, 90), life, recovering));
    }

    [Theory]
    [InlineData(false, false, false, false, null, true)]
    [InlineData(true, true, false, true, null, false)]
    [InlineData(true, true, false, true, 59, false)]
    [InlineData(true, true, false, true, 70, true)]
    [InlineData(true, true, false, true, 89, true)]
    [InlineData(true, true, false, true, 90, true)]
    [InlineData(true, false, false, true, 100, false)]
    [InlineData(true, true, true, true, 100, false)]
    [InlineData(true, true, false, false, 100, false)]
    public void Team_readiness_excludes_offline_but_requires_online_life_and_availability(
        bool online, bool sameMap, bool inFight, bool alive, int? life, bool expected)
    {
        Assert.Equal(expected, RegenerationPolicy.MemberReady(new(true, 60, 90, 123, true),
            online, sameMap, inFight, alive, life));
    }

    [Fact]
    public void Team_wait_is_independently_opt_in()
    {
        Assert.True(RegenerationPolicy.MemberReady(new(true), true, false, true, false, null));
        Assert.True(RegenerationPolicy.MemberReady(new(false, WaitForTeam: true),
            true, false, true, false, null));
    }

    [Fact]
    public void Recovering_member_must_reach_stop_but_idle_member_only_needs_start()
    {
        var options = new RegenerationOptions(true, 60, 90, WaitForTeam: true);
        Assert.True(RegenerationPolicy.MemberReady(options, true, true, false, true, 70));
        Assert.False(RegenerationPolicy.MemberReady(options, true, true, false, true, 70, recovering: true));
        Assert.True(RegenerationPolicy.MemberReady(options, true, true, false, true, 90, recovering: true));
    }

    [Fact]
    public void Invalid_saved_thresholds_are_normalized_without_lowering_stop_below_start()
    {
        Assert.Equal(1, new RegenerationOptions(true, -50, -10).Start);
        Assert.Equal(1, new RegenerationOptions(true, -50, -10).Stop);
        Assert.Equal(100, new RegenerationOptions(true, 150, 20).Stop);
        Assert.Equal(75, new RegenerationOptions(true, 75, 20).Stop);
    }
}
