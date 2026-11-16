using System.Linq;
using GivreClone.App.Services.Eco;
using Xunit;

namespace GivreClone.Tests;

public class RunePurchasePlanTests
{
    // Observed live 2026-10-31: Fo average 48, offers 100@4386, 10@818, 1@67, ceiling 120 %.
    private static long FoLot(long gid, int lot) => gid != 1519 ? 0 : lot switch { 100 => 4386, 10 => 818, _ => 67 };

    [Fact]
    public void Exploration_falls_back_to_an_affordable_smaller_lot_after_each_family()
    {
        long Lot(long gid, int lot) => (gid, lot) switch
        {
            (1, 100) => 4000, (2, 100) => 4500, (2, 10) => 500, _ => 0
        };
        var plan = RunePurchasePlan.PlanExploration([1, 2], _ => 100, Lot, 100, 5000);
        Assert.Equal(new[] { new RunePurchase(1, 100, 1, 4000), new RunePurchase(2, 10, 1, 500) }, plan);
        Assert.Equal(new RunePurchase(2, 10, 1, 500), Assert.Single(
            RunePurchasePlan.PlanExploration([2], _ => 100, Lot, 100, 500)));
    }

    [Fact]
    public void Unit_lots_fund_a_batch_instead_of_repeating_a_trip_for_one_rune()
    {
        var plan = RunePurchasePlan.PlanExploration([1], _ => 10, (_, lot) => lot == 1 ? 10 : 0, 100, 50);
        Assert.Equal(new RunePurchase(1, 1, 5, 10), Assert.Single(plan));
        Assert.Empty(RunePurchasePlan.PlanExploration([1], _ => 10, (_, lot) => lot == 1 ? 10 : 0, 100, 49));
        Assert.Equal(2, Assert.Single(RunePurchasePlan.PlanExploration(
            [1], _ => 10, (_, lot) => lot == 1 ? 10 : 0, 100, 20, minimumSample: 2)).Count);
    }

    [Fact]
    public void Exploration_handles_large_reference_prices_without_overflow()
        => Assert.Single(RunePurchasePlan.PlanExploration([1], _ => long.MaxValue,
            (_, lot) => lot == 10 ? 10 : 0, int.MaxValue, 10));

    [Fact]
    public void Exploration_buys_the_real_hundred_lot_even_when_its_percent_ceiling_exceeds_the_budget()
    {
        var plan = RunePurchasePlan.PlanExploration(new[] { 1519L }, _ => 48, FoLot, 120, 100000);
        Assert.Equal(new RunePurchase(1519, 100, 1, 4386), Assert.Single(plan));
    }

    [Fact]
    public void Exploration_ranks_real_unit_cost_and_buys_two_families_within_five_thousand()
    {
        long Lot(long gid, int lot) => (gid, lot) switch
        {
            (1, 100) => 4000, (2, 10) => 300, (3, 10) => 310, (4, 1) => 50, _ => 0
        };
        var plan = RunePurchasePlan.PlanExploration(new[] { 1L, 2L, 3L, 4L }, _ => 100, Lot, 100, 100000);
        Assert.Equal(new[] { 2L, 3L }, plan.Select(p => p.Gid).ToArray());
        Assert.True(plan.Sum(p => p.Count * p.MaxLotPrice) <= 5000);
    }

    [Fact]
    public void Exploration_uses_a_live_reference_when_the_average_is_unknown()
    {
        long Reference(long gid) => RunePurchasePlan.Reference(0, gid == 7 ? 30 : -1);
        var plan = RunePurchasePlan.PlanExploration(new[] { 7L, 8L }, Reference, (_, lot) => lot == 10 ? 280 : 0, 100, 5000);
        Assert.Equal(new RunePurchase(7, 10, 1, 280), Assert.Single(plan));
    }

    [Fact]
    public void Exploration_sends_nothing_without_an_admissible_offer_or_budget()
    {
        Assert.Empty(RunePurchasePlan.PlanExploration(new[] { 1L }, _ => 100, (_, _) => -1, 100, 5000));
        Assert.Empty(RunePurchasePlan.PlanExploration(new[] { 1519L }, _ => 40, FoLot, 100, 5000));
        Assert.Empty(RunePurchasePlan.PlanExploration(new[] { 1519L }, _ => 48, FoLot, 120, 60));
    }

    [Fact]
    public void Need_is_covered_by_lots_capped_at_a_percent_of_the_average()
    {
        var plan = RunePurchasePlan.Plan(new[] { (100L, 23) }, _ => 50, 120, 1_000_000);
        Assert.Equal(new[] { (10, 2), (1, 3) }, plan.Select(p => (p.Lot, p.Count)).ToArray());
        Assert.Equal(600, plan[0].MaxLotPrice);   // 50 × 10 × 120 %
        Assert.Equal(60, plan[1].MaxLotPrice);
    }

    [Fact]
    public void Budget_limits_the_lots_and_unknown_prices_are_skipped()
    {
        var plan = RunePurchasePlan.Plan(new[] { (1L, 30), (2L, 5) }, g => g == 1 ? 100 : 0, 100, 1_500);
        // 1 lot de 10 (1 000) puis 5 runes à l'unité (500) : budget épuisé ; la rune 2 n'a pas de prix moyen.
        Assert.Equal(new[] { (1L, 10, 1), (1L, 1, 5) }, plan.Select(p => (p.Gid, p.Lot, p.Count)).ToArray());
    }

    [Fact]
    public void Needed_doubles_the_passes_and_nearest_market_uses_grid_distance()
    {
        Assert.Equal(6, RunePurchasePlan.Needed(25, 10));
        Assert.Equal(0, RunePurchasePlan.Needed(0, 10));
        long? m = RunePurchasePlan.Nearest(new[] { 1L, 2L, 3L }, 0, 0, id => id switch { 1 => (10, 10), 2 => (-2, 1), _ => null });
        Assert.Equal(2, m);
    }

    [Theory]
    [InlineData(80, 120, 80)]
    [InlineData(0, 120, 120)]
    [InlineData(0, -1, 0)]
    public void Reference_uses_the_server_average_then_the_live_unit_price(long average, long live, long expected)
        => Assert.Equal(expected, RunePurchasePlan.Reference(average, live));
}
