extern alias F06App;

using System;
using System.Linq;
using System.Threading.Tasks;
using F06App::GivreClone.App.Services.Combat;
using Xunit;

namespace GivreClone.Tests;

public class TeamIntentBoardTests
{
    private static TeamIntent Intent(long actor, int round, params TeamTargetIntent[] targets)
        => new(actor, round, targets, (int)actor, Array.Empty<TeamSupportIntent>(), 1);

    [Fact]
    public void Ally_damage_reserves_doomed_enemy_and_prefers_another_target()
    {
        var board = new TeamIntentBoard();
        board.Publish("fight", Intent(1, 1, new TeamTargetIntent(-10, 60)));
        var view = board.Read("fight", 1, 2);
        Assert.Equal(60, view.ExpectedDamageOn(-10));
        Assert.Equal(new long[] { -20, -10 }, view.OrderTargets(new long[] { -10, -20 }, a => a, _ => 50, 60));
        Assert.Equal(new long[] { -10 }, view.OrderTargets(new long[] { -10 }, a => a, _ => 50, 60));
        Assert.Equal(0, TeamIntentView.EffectiveRemainingLife(50, 60));
    }

    [Fact]
    public void Finish_ally_focused_enemy_before_an_untouched_enemy()
    {
        var board = new TeamIntentBoard();
        board.Publish("fight", Intent(1, 1, new TeamTargetIntent(-10, 40)));
        var view = board.Read("fight", 1, 2);
        Assert.Equal(new long[] { -10, -20 }, view.OrderTargets(new long[] { -20, -10 }, a => a, _ => 100, 60));
        Assert.Equal(60, view.EffectiveRemainingLife(-10, 100));
    }

    [Fact]
    public void Confirmed_damage_is_not_subtracted_twice()
    {
        var board = new TeamIntentBoard();
        board.Publish("fight", Intent(1, 1, new TeamTargetIntent(-10, 40, false, 100)));
        var view = board.Read("fight", 1, 2);
        Assert.Equal(60, view.EffectiveRemainingLife(-10, 60));
        Assert.Equal(60, view.EffectiveRemainingLife(-10, 100));
    }

    [Fact]
    public void Round_change_expires_entries_and_rejects_late_publications()
    {
        var board = new TeamIntentBoard();
        board.Publish("fight", Intent(1, 1, new TeamTargetIntent(-10, 60, true)));
        Assert.Contains(-10, board.Read("fight", 1, 2).ReservedKills);
        Assert.Single(board.Read("fight", 2, 2).Intents);
        Assert.Equal(0, board.Read("fight", 2, 2).ExpectedDamageOn(-10));
        board.Publish("fight", Intent(1, 1, new TeamTargetIntent(-10, 60, true)));
        Assert.Empty(board.Read("fight", 2, 2).ReservedKills);
        Assert.Single(board.Read("fight", 1, 2).Intents);
        Assert.Empty(board.Read("fight", 3, 2).Intents);
    }

    [Fact]
    public void Faster_ally_next_round_does_not_erase_current_round_for_slower_ally()
    {
        var board = new TeamIntentBoard();
        board.Publish("fight", Intent(1, 4, new TeamTargetIntent(-10, 20)));
        board.Publish("fight", Intent(1, 5, new TeamTargetIntent(-20, 30)));
        var slower = board.Read("fight", 4, 2);
        Assert.Equal(20, slower.ExpectedDamageOn(-10));
        Assert.Equal(0, slower.ExpectedDamageOn(-20));
        board.Publish("fight", Intent(2, 4, new TeamTargetIntent(-10, 10)));
        Assert.Single(board.Read("fight", 5, 1).Intents);
        Assert.Equal(0, board.Read("fight", 5, 1).ExpectedDamageOn(-10));
    }

    [Fact]
    public void Fights_are_isolated_and_clear_removes_the_fight()
    {
        var board = new TeamIntentBoard();
        board.Publish("first", Intent(1, 1, new TeamTargetIntent(-10, 60, true)));
        board.Publish("second", Intent(2, 1, new TeamTargetIntent(-20, 30)));
        Assert.Equal(0, board.Read("second", 1).ExpectedDamageOn(-10));
        board.Clear("first");
        Assert.Empty(board.Read("first", 1).Intents);
        Assert.Equal(30, board.Read("second", 1).ExpectedDamageOn(-20));
    }

    [Fact]
    public void Concurrent_publications_preserve_every_allies_intent()
    {
        var board = new TeamIntentBoard();
        Parallel.For(1, 101, actor => board.Publish("fight", Intent(actor, 1, new TeamTargetIntent(-10, 2))));
        var view = board.Read("fight", 1);
        Assert.Equal(100, view.Intents.Count);
        Assert.Equal(200, view.ExpectedDamageOn(-10));
        Assert.Equal(100, view.AllyEndCells.Count);
        Assert.Equal(198, board.Read("fight", 1, 1).ExpectedDamageOn(-10));
    }

    [Fact]
    public void Support_selects_exposed_weakest_ally_and_accounts_for_promised_protection()
    {
        var board = new TeamIntentBoard();
        var allies = new[] { new TeamAllyExposure(2, 20, 100, 10), new TeamAllyExposure(3, 40, 100, 0) };
        Assert.Equal(2, board.Read("fight", 1).MostExposedAlly(allies));
        board.Publish("fight", Intent(1, 1) with { Support = new[] { new TeamSupportIntent(2, 20, 30) } });
        Assert.Equal(3, board.Read("fight", 1).MostExposedAlly(allies));
    }

    [Theory]
    [InlineData(20, 2)]
    [InlineData(40, 2)]
    [InlineData(60, 2)]
    public void Confirmed_healing_is_not_counted_twice_when_selecting_the_next_ally(int observedLife, long expectedAlly)
    {
        var board = new TeamIntentBoard();
        board.Publish("fight", Intent(1, 1) with { Support = new[] { new TeamSupportIntent(2, 40, LifeBefore: 20) } });
        var allies = new[] { new TeamAllyExposure(2, observedLife, 100, 0), new TeamAllyExposure(3, 65, 100, 0) };

        var target = board.Read("fight", 1).MostExposedAlly(allies);

        Assert.Equal(expectedAlly, target);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(20, 2)]
    [InlineData(40, 2)]
    [InlineData(60, 3)]
    public void Shield_priority_uses_observation_without_recounting_promised_shield(int observedShield, long expectedAlly)
    {
        var board = new TeamIntentBoard();
        board.Publish("fight", Intent(1, 1) with
        {
            Support = new[] { new TeamSupportIntent(2, Shield: 40, ShieldBefore: 0) }
        });
        var allies = new[] { new TeamAllyExposure(2, 20, 100, 0, observedShield), new TeamAllyExposure(3, 65, 100, 0) };

        var target = board.Read("fight", 1).MostExposedAlly(allies);

        Assert.Equal(expectedAlly, target);
    }

    [Fact]
    public void Publications_copy_inputs_and_newer_decisions_replace_old_ones()
    {
        var board = new TeamIntentBoard();
        var targets = new[] { new TeamTargetIntent(-10, 20) };
        board.Publish("fight", Intent(1, 1, targets) with { Timestamp = 2 });
        targets[0] = new(-10, 90);
        board.Publish("fight", Intent(1, 1, new TeamTargetIntent(-10, 1)));
        Assert.Equal(20, board.Read("fight", 1).ExpectedDamageOn(-10));
        board.Publish("fight", Intent(1, 1, new TeamTargetIntent(-10, 30)) with { Timestamp = 3 });
        Assert.Equal(30, board.Read("fight", 1).ExpectedDamageOn(-10));
    }
}
