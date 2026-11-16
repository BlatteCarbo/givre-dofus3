using System;
using System.IO;
using GivreClone.App.Services.Eco;
using Xunit;

namespace GivreClone.Tests;

public sealed class F09F10RegressionTests
{
    [Theory]
    [InlineData(10, 1, 0.1)]
    [InlineData(100, 123, 1.23)]
    [InlineData(1000, 1234, 1.234)]
    public void Deal_preserves_fractional_unit_price(int size, long cost, double expected)
    {
        var prices = new long[] { -1, -1, -1, -1 };
        prices[Array.IndexOf(MarketDeals.LotSizes, size)] = cost;
        var deal = Assert.Single(MarketDeals.Find(new[] { new MarketQuote(1, "item", prices, 10, 0) }, 1, 0));
        Assert.Equal((decimal)expected, deal.UnitPrice);
        Assert.Equal(cost, deal.UnitPrice * size);
    }

}
