using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services.Eco;
using Xunit;

namespace GivreClone.Tests;

public class ForgeEngineTests
{
    private static Dictionary<string, int> S(params (string, int)[] v) => v.ToDictionary(x => x.Item1, x => x.Item2);

    [Fact]
    public void Targets_pick_the_best_priority_then_the_largest_gap_and_finish_when_all_reached()
    {
        var t = new[] { new ForgeTarget("agi", 10, 1, "Ra Agi"), new ForgeTarget("sag", 30, 2, "Ra Sa"), new ForgeTarget("for", 20, 3, "Ra Fo"), new ForgeTarget("vi", 100, 3, "Ra Vi"), new ForgeTarget("cha", 15, 3, "Ra Cha") };
        Assert.Equal("Ra Agi", ForgeEngine.DecideTargets(S(("agi", 5)), t).Rune);                                    // priorité 1 d'abord
        Assert.Equal("Ra Sa", ForgeEngine.DecideTargets(S(("agi", 10), ("sag", 0)), t).Rune);
        // priorité égale (3) : Force 17→20 (3), Vitalité 90→100 (10)... on prend le plus grand écart
        Assert.Equal("Ra Vi", ForgeEngine.DecideTargets(S(("agi", 10), ("sag", 30), ("for", 17), ("vi", 90), ("cha", 11)), t).Rune);
        Assert.Equal(ForgeActionKind.Finish, ForgeEngine.DecideTargets(S(("agi", 10), ("sag", 30), ("for", 20), ("vi", 100), ("cha", 15)), t).Kind);
    }

    [Fact]
    public void Conditions_use_the_first_matching_rule_and_skip_otherwise()
    {
        var rules = new[]
        {
            new ForgeCondition("for", 61, 999, ForgeAction.Use("Ini")),
            new ForgeCondition("for", 0, 20, ForgeAction.Use("Fo")),
            new ForgeCondition("for", 21, 47, ForgeAction.Use("Pa Fo")),
            new ForgeCondition("for", 48, 57, ForgeAction.Use("Ra Fo")),
        };
        Assert.Equal("Ra Fo", ForgeEngine.DecideConditions(S(("for", 54)), null, rules).Rune);
        Assert.Equal("Ini", ForgeEngine.DecideConditions(S(("for", 64)), null, rules).Rune);
        Assert.Equal(ForgeActionKind.Skip, ForgeEngine.DecideConditions(S(("for", 59)), null, rules).Kind);
    }

    [Fact]
    public void Pit_conditions_combine_with_and_or()
    {
        var and = new ForgeCondition("res", 0, 4, ForgeAction.Use("Re"), 6, 12, ForgeCombine.And);
        var or = and with { Combine = ForgeCombine.Or };
        Assert.True(ForgeEngine.Matches(and, S(("res", 3)), 8));
        Assert.False(ForgeEngine.Matches(and, S(("res", 3)), 20));
        Assert.True(ForgeEngine.Matches(or, S(("res", 3)), 20));
        Assert.False(ForgeEngine.Matches(or, S(("res", 9)), 20));
        Assert.True(ForgeEngine.Matches(new ForgeCondition(null, 0, 0, ForgeAction.Skip), S(), null));   // aucune case cochée : toujours
    }
}

public class LotPlannerTests
{
    private static readonly LotOffer[] Offers = { new(1, 2000), new(10, 1900), new(100, 1600), new(1000, 2000) };

    [Fact]
    public void Largest_first_decomposes_into_1000_100_10_1()
    {
        var (lots, left) = LotPlanner.Decompose(1234, Offers, LotPolicy.LargestFirst, 0, buying: true);
        Assert.Equal(new[] { 1000, 100, 100, 10, 10, 10, 1, 1, 1, 1 }, lots.Select(l => l.Size).ToArray());
        Assert.Equal(0, left);
    }

    [Fact]
    public void Buying_prefers_a_smaller_cheaper_lot_only_beyond_the_margin()
    {
        // lot 100 à 1600 contre lot 1000 à 2000 : 20 % moins cher
        Assert.Equal(100, LotPlanner.Next(1000, Offers, LotPolicy.Smart, 10, buying: true)!.Value.Size);
        Assert.Equal(1000, LotPlanner.Next(1000, Offers, LotPolicy.Smart, 25, buying: true)!.Value.Size);   // marge trop haute : on garde le plus grand
    }

    [Fact]
    public void Selling_prefers_the_dearer_smaller_lot_beyond_the_margin()
    {
        var offers = new LotOffer[] { new(100, 2000), new(1000, 1600) };
        Assert.Equal(100, LotPlanner.Next(1000, offers, LotPolicy.Smart, 10, buying: false)!.Value.Size);   // +25 %
        Assert.Equal(1000, LotPlanner.Next(1000, offers, LotPolicy.Smart, 30, buying: false)!.Value.Size);
    }

    [Fact]
    public void A_missing_small_size_leaves_the_remainder_uncovered()
    {
        var (lots, left) = LotPlanner.Decompose(15, new LotOffer[] { new(10, 5) }, LotPolicy.LargestFirst, 0, true);
        Assert.Single(lots); Assert.Equal(5, left);
    }
}
