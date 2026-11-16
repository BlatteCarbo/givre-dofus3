extern alias F06App;

using System.Linq;
using HdvSellPreviewMath = F06App::GivreClone.App.Services.HdvSellPreviewMath;
using HdvSellRow = F06App::GivreClone.App.ViewModels.HdvSellRow;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvSellPreviewOverflowTests
{
    [Theory]
    [InlineData(100L, 50, 5000L)]
    [InlineData(long.MaxValue, 2, long.MaxValue)]
    [InlineData(long.MaxValue, int.MaxValue, long.MaxValue)]
    [InlineData(0L, 10, 0L)]
    [InlineData(-10L, 10, 0L)]
    [InlineData(100L, -3, 0L)]
    public void Indicative_value_does_not_wrap_or_admit_invalid_quantities(long unit, int qty, long expected)
        => Assert.Equal(expected, HdvSellPreviewMath.EstimatedValue(unit, qty));

    [Fact]
    public void Large_estimates_remain_at_top_of_value_sort()
    {
        var rows = new[]
        {
            new HdvSellRow { Gid = 1, AvgUnit = 100, Qty = 10 },
            new HdvSellRow { Gid = 2, AvgUnit = long.MaxValue, Qty = 2 },
            new HdvSellRow { Gid = 3, AvgUnit = 100, Qty = 0 }
        };

        Assert.Equal(new long[] { 2, 1, 3 }, rows.OrderByDescending(r => r.EstValue).Select(r => r.Gid));
        Assert.Equal(long.MaxValue, rows[1].EstValue);
    }

    [Fact]
    public void Aggregated_quantities_and_preview_totals_saturate_instead_of_wrapping()
    {
        Assert.Equal(int.MaxValue, HdvSellPreviewMath.AddQuantity(int.MaxValue - 1, 10));
        Assert.Equal(30, HdvSellPreviewMath.AddQuantity(10, 20));
        Assert.Equal(10, HdvSellPreviewMath.AddQuantity(10, -2));
        Assert.Equal(0, HdvSellPreviewMath.AddQuantity(-1, -2));

        Assert.Equal(long.MaxValue, HdvSellPreviewMath.AddValue(long.MaxValue - 4, 10));
        Assert.Equal(30, HdvSellPreviewMath.AddValue(10, 20));
        Assert.Equal(10, HdvSellPreviewMath.AddValue(10, -1));
        Assert.Equal(long.MaxValue, HdvSellPreviewMath.AddValue(
            HdvSellPreviewMath.EstimatedValue(long.MaxValue, 1000), 1));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(101f)]
    public void Unknown_tax_rate_is_not_silently_replaced_by_a_low_estimate(float rate)
    {
        var hint = HdvSellPreviewMath.TaxCapHint(100, 10, 5000, rate);
        Assert.True(hint.Warn);
        Assert.True(hint.UnknownRate);
    }

    [Fact]
    public void Near_limit_and_extreme_tax_projections_do_not_overflow()
    {
        Assert.False(HdvSellPreviewMath.TaxCapHint(100, 90, 500, 2f).Warn);
        var atLimit = HdvSellPreviewMath.TaxCapHint(100, 90, 501, 2f);
        Assert.True(atLimit.Warn);
        Assert.False(atLimit.UnknownRate);
        Assert.Equal(500, atLimit.RemainingListingValue);

        var large = HdvSellPreviewMath.TaxCapHint(long.MaxValue, long.MaxValue - 1, long.MaxValue, 100f);
        Assert.True(large.Warn);
        Assert.Equal(1, large.RemainingListingValue);

        var tiny = HdvSellPreviewMath.TaxCapHint(long.MaxValue, long.MaxValue - 1, long.MaxValue, float.Epsilon);
        Assert.False(tiny.Warn); // with a subnormal rate, the minimum fee is one kama

        Assert.True(HdvSellPreviewMath.TaxCapHint(100, -1, 100, 2f).UnknownRate);
        Assert.False(HdvSellPreviewMath.TaxCapHint(0, -1, 100, float.NaN).Warn);
    }
}
