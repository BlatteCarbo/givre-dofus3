extern alias F06App;
using GivreClone.Protocol;
using Xunit;
using Tracker = F06App::GivreClone.App.Services.Combat.DistributionTriggerTracker;

namespace GivreClone.Tests;

public class DistributionTriggerTrackerTests
{
    [Theory]
    [InlineData(96)]
    [InlineData(2998)]
    public void Captured_Affliction_TB_effect_persists_until_its_server_removal(int action)
    {
        var tracker = new Tracker();
        var buff = new RawTimedDamageObservation(-1, 14, 33273217378, 29098, action, 17, "TB",
            new(-1, -1), new(4, null), 3);
        tracker.ObserveTimed(buff, 3);
        Assert.Equal(buff, Assert.Single(tracker.ActiveTimed(33273217378, 3)));
        tracker.Remove(new(-1, 14));
        Assert.Empty(tracker.ActiveTimed(33273217378, 3));
    }

    [Fact]
    public void Captured_Extinction_pending_damage_preserves_instances_until_removal_or_expiry()
    {
        var tracker = new Tracker();
        var buff = new RawTimedDamageObservation(-2, 20, 33273217378, 14594, 99, 37, "TE",
            new(-1, -1), new(4, null), 2);
        tracker.ObserveTimed(buff, 2);
        tracker.ObserveTimed(buff with { NetworkInstanceId = 21 }, 2);
        Assert.Equal(2, tracker.ActiveTimed(33273217378, 2).Count);
        Assert.Empty(tracker.ActiveTimed(1, 2));
        tracker.Remove(new(-2, 20));
        Assert.Equal(21, Assert.Single(tracker.ActiveTimed(33273217378, 3)).NetworkInstanceId);
        Assert.Empty(tracker.ActiveTimed(33273217378, 4));
        tracker.ObserveTimed(buff with { Trigger = "TB" }, 2);
        Assert.Empty(tracker.ActiveTimed(33273217378, 2));
        tracker.ObserveTimed(buff, 2);
        tracker.Clear();
        Assert.Empty(tracker.ActiveTimed(33273217378, 2));
    }

    [Theory]
    [InlineData(14584, 99, "TE", 2, 4)]
    [InlineData(14594, 96, "TE", 2, 4)]
    [InlineData(14594, 99, "TB", 2, 4)]
    [InlineData(14594, 99, "TE", 0, 4)]
    [InlineData(14594, 99, "TE", 2, 2)]
    public void Unestablished_timed_damage_contract_cannot_replace_an_active_instance(
        int spell, int action, string trigger, int grade, int end)
    {
        var tracker = new Tracker();
        var buff = new RawTimedDamageObservation(-2, 20, 33273217378, 14594, 99, 37, "TE",
            new(-1, -1), new(4, null), 2);
        tracker.ObserveTimed(buff, 2);
        tracker.ObserveTimed(buff with { SourceSpellId = spell, ActionId = action,
            Trigger = trigger, SourceGrade = grade, End = new(end, null) }, 2);
        Assert.Empty(tracker.ActiveTimed(33273217378, 2));
    }

    [Fact]
    public void Captured_Sram_IOFF_persists_until_its_instance_is_removed()
    {
        var tracker = new Tracker();
        var buff = new RawTriggeredSubcastObservation(39467549025, 4, 39467549025, 21978, 21979, 1,
            269632, "IOFF", new(-1, -1), new(-1, null), 792);

        tracker.Observe(buff, 1);

        Assert.Equal(buff, Assert.Single(tracker.Active(39467549025, 1)));
        Assert.Single(tracker.Active(39467549025, 10));
        tracker.Remove(new(39467549025, 4));
        Assert.Empty(tracker.Active(39467549025, 10));
        tracker.Observe(buff with { ActionId = 1160 }, 1);
        tracker.Observe(buff with { SourceId = 99 }, 1);
        Assert.Empty(tracker.Active(39467549025, 1));
    }

    [Fact]
    public void Captured_Resonance_pair_keeps_its_source_until_both_instances_are_removed()
    {
        var tracker = new Tracker();
        var buff = new RawTriggeredSubcastObservation(-2, 29, 33273217378, 14611, 31020, 1,
            404765, "D", new(-1, -1), new(4, null));
        tracker.Observe(buff, 3);
        tracker.Observe(buff with { NetworkInstanceId = 30, Trigger = "XD" }, 3);

        var observed = Assert.Single(tracker.Active(33273217378, 3));
        Assert.Equal(14611, observed.SourceSpellId);
        Assert.Equal(31020, observed.SubSpellId);
        Assert.Empty(tracker.Active(99, 3));
        tracker.Remove(new(-2, 30));
        Assert.Single(tracker.Active(33273217378, 3));
        tracker.Remove(new(-2, 29));
        Assert.Empty(tracker.Active(33273217378, 3));
        tracker.Observe(buff, 3);
        Assert.Empty(tracker.Active(33273217378, 4));
    }

    [Fact]
    public void Paired_instances_form_one_reward_and_expire_at_the_captured_round()
    {
        var tracker = new Tracker();
        RawTriggeredSubcastObservation Buff(int instance, string trigger) =>
            new(-2, instance, 1, 14577, 14578, 2, 226051, trigger, new(-1, -1), new(4, null));
        tracker.Observe(Buff(23, "D"), 3);
        tracker.Observe(Buff(24, "XD"), 3);

        var reward = Assert.Single(tracker.Active(1, 3));
        Assert.Equal(-2L, reward.ActorId);
        tracker.Remove(new RawStateRemoveObservation(-2, 24));
        Assert.Single(tracker.Active(1, 3));
        Assert.Empty(tracker.Active(99, 3));
        Assert.Empty(tracker.Active(1, 4));
        Assert.Empty(tracker.Active(1, 3));
    }

    [Fact]
    public void Removal_and_clear_discard_observations_without_inventing_missing_lifetimes()
    {
        var tracker = new Tracker();
        var buff = new RawTriggeredSubcastObservation(-2, 23, 1, 14577, 14578, 2,
            226051, "D", new(-1, -1), new(4, null));
        tracker.Observe(buff with { End = null }, 3);
        Assert.Empty(tracker.Active(1, 3));
        tracker.Observe(buff, 3);
        tracker.Remove(new RawStateRemoveObservation(-3, 23));
        Assert.Single(tracker.Active(1, 3));
        tracker.Remove(new RawStateRemoveObservation(-2, 23));
        Assert.Empty(tracker.Active(1, 3));
        tracker.Observe(buff, 3);
        tracker.Clear();
        Assert.Empty(tracker.Active(1, 3));
    }
}
