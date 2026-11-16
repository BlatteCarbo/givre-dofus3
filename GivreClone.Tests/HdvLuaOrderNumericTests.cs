extern alias F06App;

using Numeric = F06App::GivreClone.App.Services.HdvLuaOrderNumeric;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvLuaOrderNumericTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(13.25)]
    [InlineData(9007199254740992d)]
    [InlineData(double.MaxValue)]
    public void Unsafe_Lua_money_arguments_cannot_narrow_to_a_real_order(double candidate)
    {
        Assert.False(Numeric.TryPositiveLong(candidate, out _));
    }

    [Theory]
    [InlineData(1d, 1L)]
    [InlineData(1000d, 1000L)]
    [InlineData(9007199254740991d, 9007199254740991L)]
    public void Exact_positive_prices_remain_intact(double input, long expected)
    {
        Assert.True(Numeric.TryPositiveLong(input, out long result));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Uid_or_quantity_must_fit_positive_int_without_wrap()
    {
        Assert.True(Numeric.TryPositiveInt(1000, out int qty));
        Assert.Equal(1000, qty);
        Assert.False(Numeric.TryPositiveInt(2147483648d, out _));
        Assert.False(Numeric.TryPositiveInt(double.NaN, out _));
        Assert.False(Numeric.TryPositiveInt(null, out _));
        Assert.False(Numeric.TryPositiveLong(null, out _));
        Assert.True(Numeric.TryNonNegativeLong(0, out long noLimit));
        Assert.Equal(0, noLimit);
        Assert.False(Numeric.TryNonNegativeLong(double.NaN, out _));
        Assert.False(Numeric.TryNonNegativeLong(12.5, out _));
        Assert.False(Numeric.TryNonNegativeLong(double.MaxValue, out _));
    }
}
