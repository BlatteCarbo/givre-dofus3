extern alias F06App;

using GivreClone.Protocol;
using Q = F06App::GivreClone.App.Services.QuantityFarm;
using Xunit;

namespace GivreClone.Tests;

public class QuantityFarmScriptTests
{
    [Fact]
    public void Resource_script_checks_the_count_then_gathers_one_step()
    {
        string lua = Q.QuantityFarmScript.Build(new Q.QuantityFarmScriptSpec(
            Q.QuantityFarmKind.Resource, 303, 10, 0, 1, 8, 95, "Frêne", new long[] { 188745730 }));

        Assert.Contains("ITEMS_TO_GATHER = { 303 }", lua);
        Assert.Contains("local TARGET = 10", lua);
        Assert.Contains("printMessage(\"[quantity-farm] gid=\" .. GID .. \" owned=\" .. owned .. \" target=\" .. TARGET .. \" remaining=\" .. remaining)", lua);
        Assert.Contains("if owned >= TARGET then return false end", lua);
        Assert.Contains("gather = true", lua);
        Assert.Contains("map = \"188745730\"", lua);
        Assert.DoesNotContain("gatherAll", lua);
        Assert.DoesNotContain("fight = true", lua);
        Assert.DoesNotContain("FORCE_MONSTERS", lua);
        Assert.DoesNotContain("npcBank", lua);
        Assert.DoesNotContain("giveUp", lua);
        Assert.DoesNotContain("bank()", lua);
    }

    [Fact]
    public void Resource_display_name_cannot_open_a_new_lua_line()
    {
        string lua = Q.QuantityFarmScript.Build(new Q.QuantityFarmScriptSpec(
            Q.QuantityFarmKind.Resource, 303, 10, 0, 1, 8, 95, "bois\ngatherAll()", new long[] { 188745730 }));
        Assert.DoesNotContain("\ngatherAll", lua);
        Assert.Contains("ITEMS_TO_GATHER = { 303 }", lua);
    }

    [Fact]
    public void Monster_script_forces_that_monster_and_swaps_a_reversed_group()
    {
        string lua = Q.QuantityFarmScript.Build(new Q.QuantityFarmScriptSpec(
            Q.QuantityFarmKind.Monster, 885, 10, 149, 3, 6, 95, "Bouftou", new long[] { 42 }));
        Assert.Contains("FORCE_MONSTERS = { 149 }", lua);
        Assert.Contains("MIN_MONSTERS = 3", lua);
        Assert.Contains("MAX_MONSTERS = 6", lua);
        Assert.Contains("fight = true", lua);
        Assert.Contains("map = \"42\"", lua);
        Assert.Contains("printMessage(\"[quantity-farm] gid=\" .. GID .. \" owned=\" .. owned .. \" target=\" .. TARGET .. \" remaining=\" .. remaining)", lua);
        Assert.Contains("return false", lua);
        Assert.DoesNotContain("gatherAll", lua);
        Assert.DoesNotContain("ITEMS_TO_GATHER", lua);
        Assert.DoesNotContain("gather = true", lua);

        string swapped = Q.QuantityFarmScript.Build(new Q.QuantityFarmScriptSpec(
            Q.QuantityFarmKind.Monster, 885, 10, 149, 5, 2, 95, "Bouftou", new long[] { 42 }));
        Assert.Contains("MIN_MONSTERS = 2", swapped);
        Assert.Contains("MAX_MONSTERS = 5", swapped);
    }

    [Fact]
    public void Fight_loot_of_the_target_gid_can_overshoot_then_the_next_move_stops()
    {
        const long baseline = 5;
        var session = new Q.QuantityFarmSession(885, baseline, baseline + 2);
        session.ApplyLoot(new FightEndSummary(false, 0, 0, null, null, new[] { (885, 9) }));
        Assert.Equal(baseline, session.Owned);

        Assert.True(session.Move(Q.QuantityFarmStep.Fight));
        session.ApplyLoot(new FightEndSummary(true, 2, 0, null, null, new[] { (885, 1), (999, 4) }));
        Assert.Equal(baseline + 1, session.Owned);

        Assert.True(session.Move(Q.QuantityFarmStep.Fight));
        session.ApplyLoot(new FightEndSummary(true, 2, 0, null, null, new[] { (885, 2), (111, 7) }));
        Assert.Equal(baseline + 3, session.Owned);
        Assert.False(session.Move(Q.QuantityFarmStep.Fight));
        Assert.Equal(new[] { Q.QuantityFarmSession.AttackAction, Q.QuantityFarmSession.AttackAction }, session.Trace);
        Assert.Equal("AttackMonsterRequest", Q.QuantityFarmSession.AttackAction);

        Assert.True(Q.QuantityFarmProgress.TryParse(session.Line, out var progress));
        Assert.Equal(0, progress.Remaining);
        Assert.Equal(baseline + 3, progress.Owned);
    }

    [Fact]
    public void Two_harvests_cover_the_remaining_and_a_third_click_is_not_traced()
    {
        var session = new Q.QuantityFarmSession(303, 0, 6);
        Assert.True(session.Move(Q.QuantityFarmStep.Gather));
        session.ApplyHarvest(4);
        Assert.True(session.Move(Q.QuantityFarmStep.Gather));
        session.ApplyHarvest(3);
        Assert.Equal(7, session.Owned);
        Assert.False(session.Move(Q.QuantityFarmStep.Gather));
        Assert.Equal(2, session.Trace.Count);
        Assert.All(session.Trace, action => Assert.Equal(Q.QuantityFarmSession.HarvestAction, action));
        Assert.Equal("InteractiveUseRequest", Q.QuantityFarmSession.HarvestAction);
        Assert.True(Q.QuantityFarmProgress.TryParse(session.Line, out var progress));
        Assert.Equal(0, progress.Remaining);
        Assert.Equal(7, progress.Owned);
    }
}
