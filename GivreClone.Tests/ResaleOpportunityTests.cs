using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class ResaleOpportunityTests
{
    private static ResaleCandidate C(long unit, long avg, int qty = 10, long age = 1000) => new(1, "Objet", qty, unit, age, avg);

    [Fact]
    public void Flags_only_items_above_threshold_and_value_and_fresh()
    {
        var hits = ResaleOpportunity.Find(new[] { C(1500, 1000), C(1100, 1000), C(1500, 1000, qty: 1), C(1500, 1000, age: 99_999_999), C(0, 1000) }, 30, 10_000, 2);
        var h = Assert.Single(hits);
        Assert.Equal(50, h.GainPercent, 3);
        Assert.Equal(14700m, h.NetTotal);   // 1500 x 10 x 0,98
    }

    [Fact]
    public void Alert_throttle()
    {
        Assert.True(ResaleOpportunity.ShouldAlert(0, 0, 1000, 5));
        Assert.False(ResaleOpportunity.ShouldAlert(1000, 0, 1050, 60_000));
        Assert.True(ResaleOpportunity.ShouldAlert(1000, 0, 1100, 60_000));
        Assert.True(ResaleOpportunity.ShouldAlert(1000, 0, 900, 7 * 3_600_000L));
    }
}
