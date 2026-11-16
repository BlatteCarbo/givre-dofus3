extern alias F06App;

using HdvSalePriceMath = F06App::GivreClone.App.Services.HdvSalePriceMath;
using HdvRepricePolicy = F06App::GivreClone.App.Services.HdvRepricePolicy;
using HdvSalePriceGuard = F06App::GivreClone.App.Services.HdvSalePriceGuard;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvRealSalePriceOverflowTests
{
    [Theory]
    [InlineData(100L, 10, 500L)]
    [InlineData(101L, 1, 51L)]
    [InlineData(0L, 100, 1L)]
    [InlineData(long.MaxValue, 1, 4611686018427387904L)]
    public void Valid_half_average_floor_is_exact(long average, int lotSize, long expected)
        => Assert.Equal(expected, HdvSalePriceMath.HalfAverageFloor(average, lotSize));

    [Fact]
    public void Nonrepresentable_or_invalid_sale_floor_refuses_order()
    {
        Assert.Null(HdvSalePriceMath.HalfAverageFloor(long.MaxValue, 10));
        Assert.Null(HdvSalePriceMath.HalfAverageFloor(long.MaxValue, 1000));
        Assert.Null(HdvSalePriceMath.HalfAverageFloor(100, 0));
        Assert.Null(HdvSalePriceMath.HalfAverageFloor(-1, 1));
    }

    [Fact]
    public void Derived_price_preserves_precision_above_double_integer_limit()
    {
        Assert.Equal(90071992547409930L,
            HdvSalePriceMath.DerivedPrice(9007199254740993m, 10));
        Assert.Equal(2L, HdvSalePriceMath.DerivedPrice(1.5m, 1));
        Assert.Null(HdvSalePriceMath.DerivedPrice(long.MaxValue, 10));
        Assert.Null(HdvSalePriceMath.DerivedPrice(decimal.MaxValue, int.MaxValue));
        Assert.Null(HdvSalePriceMath.DerivedPrice(-1m, 10));
    }

    [Fact]
    public void Maximum_market_cannot_wrap_to_one_kama_with_extreme_step()
    {
        Assert.Equal(long.MaxValue,
            HdvSalePriceMath.MarketPercentageFloor(long.MaxValue, 0));
        Assert.Equal(-1,
            HdvRepricePolicy.Target(long.MaxValue, long.MaxValue,
                minimum: 1, step: long.MaxValue, floorPercent: 0, ownAtMarket: false));
        Assert.Equal(99,
            HdvRepricePolicy.Target(100, 100, 1, 1, 10, false));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Invalid_reprice_percent_never_produces_a_real_order(double rate)
    {
        Assert.Null(HdvSalePriceMath.MarketPercentageFloor(100, rate));
        Assert.Equal(-1, HdvRepricePolicy.Target(100, 90, 1, 1, rate, false));
    }

    [Fact]
    public void Nonrepresentable_reference_ceiling_does_not_throw_or_authorize_a_sale()
    {
        string? refusal = HdvSalePriceGuard.Refusal(
            int.MaxValue, 100, long.MaxValue, Array.Empty<long>(), 2m,
            ceilingMultiplier: decimal.MaxValue);
        Assert.NotNull(refusal);
        Assert.Contains("non représentable", refusal);
    }
}
