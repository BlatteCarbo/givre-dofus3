using System;
using System.Linq;
using GivreClone.App.Services.Eco;
using Xunit;

namespace GivreClone.Tests;

public class JobXpTests
{
    private static XpRecipe R(long gid, int level, double ratio, params (long, string, int)[] ing)
        => new(gid, "R" + gid, level, ratio, ing.Select(i => (i.Item1, i.Item2, i.Item3)).ToArray());

    [Theory]
    [InlineData(0, 1)] [InlineData(19, 1)] [InlineData(20, 2)] [InlineData(60, 3)] [InlineData(200, 5)] [InlineData(398_000, 200)] [InlineData(10_000_000, 200)]
    public void Level_follows_the_game_curve(long xp, int level) => Assert.Equal(level, JobXp.Level(xp));

    [Theory] [InlineData(1, 0)] [InlineData(2, 20)] [InlineData(5, 200)] [InlineData(100, 99_000)] [InlineData(200, 398_000)]
    public void Experience_is_ten_times_level_times_previous(int level, long xp) => Assert.Equal(xp, JobXp.Experience(level));

    [Fact]
    public void Level_and_Experience_are_consistent_for_every_level()
    {
        for (int l = 1; l <= 200; l++) Assert.Equal(l, JobXp.Level(JobXp.Experience(l)));
        for (int l = 2; l <= 200; l++) Assert.Equal(l - 1, JobXp.Level(JobXp.Experience(l) - 1));
    }

    [Fact]
    public void CraftXp_matches_the_published_formula()
    {
        var r = R(1, 5, 1);
        Assert.Equal(100, JobXp.CraftXp(r, 5));                                          // 20 * 5 / 1
        Assert.Equal((int)Math.Floor(20.0 * 5 / (Math.Pow(5, 1.1) / 10 + 1)), JobXp.CraftXp(r, 10));
        Assert.Equal(0, JobXp.CraftXp(r, 4));                                            // recette plus haute que le métier
        Assert.Equal(0, JobXp.CraftXp(r, 106));                                          // plus de 100 niveaux d'écart
        Assert.True(JobXp.CraftXp(r, 5, epic: true) == 300);                             // x3 en craft épique
        Assert.Equal(50, JobXp.CraftXp(R(2, 5, 0.5), 5));                                // ratio de l'objet
        Assert.Equal(150, JobXp.CraftXp(r, 5, coefficientPercent: 150));                 // bonus d'XP
    }

    [Fact]
    public void Plan_reaches_the_target_with_a_single_eligible_recipe()
    {
        var a = R(1, 1, 1, (10, "Blé", 2));
        var plan = JobXp.Plan(new[] { a }, g => 5m, 0, 10);
        Assert.True(plan.Reached);
        Assert.True(plan.FinalLevel >= 10);
        Assert.True(JobXp.Experience(10) <= plan.TotalXp);
        Assert.Single(plan.Steps);
        Assert.Equal(plan.TotalCrafts * 2 * 5m, plan.TotalCost);
        Assert.Equal(plan.TotalCrafts * 2, plan.Shopping.Single().Quantity);
    }

    [Fact]
    public void Plan_cheapest_prefers_the_lowest_cost_per_xp_and_fastest_the_most_xp()
    {
        var cheapSlow = R(1, 1, 1, (10, "Blé", 1));      // 20 xp à bas niveau, coût 1
        var pricey = R(2, 1, 5, (11, "Or", 1));          // 100 xp, coût 1000
        decimal? Price(long g) => g == 10 ? 1m : 1000m;
        var cheap = JobXp.Plan(new[] { cheapSlow, pricey }, Price, 0, 3, cheapest: true);
        var fast = JobXp.Plan(new[] { cheapSlow, pricey }, Price, 0, 3, cheapest: false);
        Assert.All(cheap.Steps, s => Assert.Equal(1, s.Recipe.Gid));
        Assert.All(fast.Steps, s => Assert.Equal(2, s.Recipe.Gid));
        Assert.True(fast.TotalCrafts < cheap.TotalCrafts);
        Assert.True(cheap.TotalCost < fast.TotalCost);
    }

    [Fact]
    public void Plan_switches_recipe_as_the_job_levels_up_and_merges_identical_steps()
    {
        var low = R(1, 1, 1, (10, "A", 1));
        var high = R(2, 20, 1, (11, "B", 1));
        var plan = JobXp.Plan(new[] { low, high }, g => g == 10 ? 1m : 2m, 0, 40, cheapest: false);
        Assert.True(plan.Reached);
        Assert.Equal(new long[] { 1, 2 }, plan.Steps.Select(s => s.Recipe.Gid).ToArray());   // d'abord la basse, puis la haute dès le niveau 20
        Assert.True(plan.Steps[1].FromLevel >= 20);
        Assert.Equal(plan.Steps.Sum(s => s.Xp), plan.TotalXp);
    }

    [Fact]
    public void Plan_without_prices_still_works_and_reports_unknown_cost()
    {
        var plan = JobXp.Plan(new[] { R(1, 1, 1, (10, "A", 1)) }, _ => null, 0, 5);
        Assert.True(plan.Reached);
        Assert.Null(plan.TotalCost);
    }

    [Fact]
    public void Plan_stops_when_no_recipe_can_give_xp()
    {
        var plan = JobXp.Plan(new[] { R(1, 150, 1, (10, "A", 1)) }, _ => 1m, 0, 10);   // recette trop haute pour un métier niveau 1
        Assert.False(plan.Reached);
        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void Plan_from_a_partial_xp_counts_only_the_missing_part()
    {
        var a = R(1, 1, 1, (10, "A", 1));
        long start = JobXp.Experience(5);
        var plan = JobXp.Plan(new[] { a }, _ => 1m, start, 5);
        Assert.True(plan.Reached);
        Assert.Equal(0, plan.TotalCrafts);
    }
}
