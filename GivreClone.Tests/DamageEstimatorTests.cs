using System.Collections.Generic;
using GivreClone.App.Services.Combat;
using Xunit;

namespace GivreClone.Tests;

public class DamageEstimatorTests
{
    private static Dictionary<int, int> Me(params (int, int)[] v) { var d = new Dictionary<int, int>(); foreach (var (k, x) in v) d[k] = x; return d; }

    [Fact]
    public void Element_mapping_follows_the_game_enum()
    {
        Assert.Equal(DamageElement.Earth, DamageEstimator.ElementOf(1));
        Assert.Equal(DamageElement.Fire, DamageEstimator.ElementOf(2));
        Assert.Equal(DamageElement.Neutral, DamageEstimator.ElementOf(0));
        var target = new DamageTarget(new Dictionary<DamageElement, int> { [DamageElement.Earth] = 50 });
        var earth = DamageEstimator.Estimate(100, 100, DamageEstimator.ElementOf(1), Me((CharIds.BonusEarth, 20)), target);
        Assert.Equal(60, earth.Average);
        Assert.Equal(CharIds.Intelligence, DamageEstimator.StatId(DamageElement.Fire));
        Assert.Equal(CharIds.Strength, DamageEstimator.StatId(DamageElement.Neutral));
    }

    [Fact]
    public void Base_formula_scales_with_stat_percent_and_adds_flat_damage()
    {
        // 20-30, force 200, 10 % dommages, 15 dommages fixes + 5 terre : 25 * 3.1 + 20
        var e = DamageEstimator.Estimate(20, 30, DamageElement.Earth, Me((CharIds.Strength, 200), (CharIds.DamagePercent, 10), (CharIds.AllDamageBonus, 15), (CharIds.BonusEarth, 5)));
        Assert.Equal(97.5, e.Average);
        Assert.Equal(82.0, e.Min);   // 20 * 3.1 + 20
        Assert.Equal(113.0, e.Max);  // 30 * 3.1 + 20
        Assert.Equal(310, e.PercentBonus + 100 - 0);   // 200 + 10 de bonus : 100 + 210
    }

    [Fact]
    public void Resistance_then_flat_reduction_apply_to_the_target()
    {
        var target = new DamageTarget(new Dictionary<DamageElement, int> { [DamageElement.Fire] = 50 }, new Dictionary<DamageElement, int> { [DamageElement.Fire] = 10 }, Life: 500);
        var e = DamageEstimator.Estimate(100, 100, DamageElement.Fire, Me(), target);
        Assert.Equal(40.0, e.Average);          // 100 * 0,5 - 10
        Assert.Equal(13.0, e.HitsToKill);       // ceil(500 / 40)
    }

    [Theory]
    [InlineData(100, 0.0)]
    [InlineData(95, 5.0)]
    [InlineData(90, 10.0)]
    public void Observed_full_elemental_resistance_leaves_no_damage(int resist, double expected)
    {
        var target = new DamageTarget(new Dictionary<DamageElement, int> { [DamageElement.Fire] = resist });

        Assert.Equal(expected, DamageEstimator.Estimate(100, 100, DamageElement.Fire, Me(), target).Average);
    }

    [Fact]
    public void Critical_chance_blends_the_expected_damage()
    {
        var e = DamageEstimator.Estimate(100, 100, DamageElement.Neutral, Me((CharIds.CriticalHit, 50), (CharIds.CriticalDamageBonus, 40)));
        Assert.Equal(100.0, e.Average);
        Assert.Equal(120.0, e.AverageWithCrit);   // 50 % de 140 + 50 % de 100
    }

    [Fact]
    public void Damage_never_goes_below_zero_and_unknown_stats_count_as_zero()
    {
        var target = new DamageTarget(null, new Dictionary<DamageElement, int> { [DamageElement.Air] = 500 });
        Assert.Equal(0.0, DamageEstimator.Estimate(10, 20, DamageElement.Air, Me(), target).Average);
        Assert.Equal(15.0, DamageEstimator.Estimate(10, 20, DamageElement.Water, Me()).Average);
    }
}
