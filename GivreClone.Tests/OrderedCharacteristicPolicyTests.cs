using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class OrderedCharacteristicPolicyTests
{
    // Format BreedData : [base minimale, coût, gain facultatif].
    private static Dictionary<string, string> Costs() => CharacteristicUpgradePolicy.Stats.ToDictionary(s => s.Name,
        s => s.Name == "vitality" ? "[[0,1]]" : s.Name == "wisdom" ? "[[0,3]]" : "[[0,1],[100,2],[200,3],[300,4]]");
    private static Dictionary<int, int> Bases() => CharacteristicUpgradePolicy.Stats.ToDictionary(s => s.Id, _ => 0);

    [Fact]
    public void Ordered_caps_then_remainder_preserve_all_other_investments()
    {
        var bases = Bases();
        bases[10] = 99;
        bases[12] = 2;
        var targets = new[] { new CharacteristicTarget("strength", 101), new CharacteristicTarget("intelligence", 2), new CharacteristicTarget("wisdom", null) };
        int budget = 12;
        foreach (var (stat, basis, cost) in new[] { ("strength", 101, 3), ("intelligence", 2, 2), ("wisdom", 4, 6) })
        {
            var spend = OrderedCharacteristicPolicy.Next(bases, budget, targets, Costs())!;
            Assert.Equal(stat, spend.Stat);
            Assert.Equal(basis, spend.Target);
            Assert.Equal(cost, spend.Cost);
            Assert.Equal(6, spend.Allocation.Count);
            Assert.Equal(CharacteristicUpgradePolicy.Invested(stat, basis), spend.Allocation[stat]);
            foreach (var other in CharacteristicUpgradePolicy.Stats.Where(s => s.Name != stat))
                Assert.Equal(CharacteristicUpgradePolicy.Invested(other.Name, bases[other.Id]), spend.Allocation[other.Name]);
            bases[CharacteristicUpgradePolicy.Stats.Single(s => s.Name == stat).Id] = basis;
            budget -= cost;
        }
        Assert.Equal(1, budget);
        Assert.Null(OrderedCharacteristicPolicy.Next(bases, budget, targets, Costs()));
    }

    [Theory]
    [InlineData(99, 4, 100, 1)]
    [InlineData(199, 8, 201, 5)]
    [InlineData(299, 8, 301, 7)]
    [InlineData(300, 3, 300, 0)]
    public void Budget_crosses_real_cost_tiers_without_exceeding_cap(int basis, int budget, int expected, int cost)
    {
        var bases = Bases(); bases[10] = basis;
        var spend = OrderedCharacteristicPolicy.Next(bases, budget, new[] { new CharacteristicTarget("strength", expected) }, Costs());
        if (cost == 0) Assert.Null(spend);
        else { Assert.Equal(expected, spend!.Target); Assert.Equal(cost, spend.Cost); }
    }

    [Fact]
    public void Unaffordable_priority_does_not_spend_on_later_target()
    {
        Assert.Null(OrderedCharacteristicPolicy.Next(Bases(), 2,
            new[] { new CharacteristicTarget("wisdom", 1), new CharacteristicTarget("vitality", null) }, Costs()));
    }

    [Fact]
    public void Reads_supplied_breed_costs_and_optional_gain_instead_of_hardcoded_formula()
    {
        var costs = Costs(); costs["vitality"] = "[[0,1,2],[100,2,2]]";
        var bases = Bases(); bases[11] = 98;
        var spend = OrderedCharacteristicPolicy.Next(bases, 5, new[] { new CharacteristicTarget("vitality", 102) }, costs)!;
        Assert.Equal(102, spend.Target);
        Assert.Equal(3, spend.Cost);
        Assert.Equal(52, spend.Allocation["vitality"]);
        Assert.Null(OrderedCharacteristicPolicy.Next(bases, 5, new[] { new CharacteristicTarget("vitality", 99) }, costs));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[[100,1]]")]
    [InlineData("[[0,0]]")]
    [InlineData("[[0,1],[0,2]]")]
    [InlineData("broken")]
    public void Missing_or_malformed_data_prevents_any_allocation(string bad)
    {
        var costs = Costs(); costs["chance"] = bad;
        Assert.Null(OrderedCharacteristicPolicy.Next(Bases(), 5, new[] { new CharacteristicTarget("strength") }, costs));
        costs = Costs(); costs.Remove("chance");
        Assert.Null(OrderedCharacteristicPolicy.Next(Bases(), 5, new[] { new CharacteristicTarget("strength") }, costs));
        var bases = Bases(); bases.Remove(13);
        Assert.Null(OrderedCharacteristicPolicy.Next(bases, 5, new[] { new CharacteristicTarget("strength") }, Costs()));
    }

    [Fact]
    public void Empty_completed_invalid_and_zero_budget_plans_do_not_spend()
    {
        Assert.Null(OrderedCharacteristicPolicy.Next(Bases(), 5, Array.Empty<CharacteristicTarget>(), Costs()));
        Assert.Null(OrderedCharacteristicPolicy.Next(Bases(), 0, new[] { new CharacteristicTarget() }, Costs()));
        foreach (var target in new[] { new CharacteristicTarget("bad"), new CharacteristicTarget("strength", -1), new CharacteristicTarget("strength", 0) })
            Assert.Null(OrderedCharacteristicPolicy.Next(Bases(), 5, new[] { target }, Costs()));
    }
}
