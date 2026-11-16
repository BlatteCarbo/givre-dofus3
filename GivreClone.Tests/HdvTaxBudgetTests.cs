extern alias F06App;

using HdvTaxBudget = F06App::GivreClone.App.Services.HdvTaxBudget;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvTaxBudgetTests
{
    [Theory]
    [InlineData(10000L, 2f, 200L)]
    [InlineData(101L, 2f, 3L)]
    [InlineData(1L, 2f, 1L)]
    public void Next_listing_fee_is_rounded_up_before_sending(long price, float rate, long expected)
        => Assert.Equal(expected, HdvTaxBudget.EstimateUpperBound(price, rate));

    [Fact]
    public void Unknown_tax_rate_cannot_pass_a_capped_preflight()
    {
        Assert.Null(HdvTaxBudget.EstimateUpperBound(1000, 0));
        Assert.Null(HdvTaxBudget.EstimateUpperBound(1000, float.NaN));
        Assert.Null(HdvTaxBudget.EstimateUpperBound(0, 2));
    }

    [Fact]
    public void Outstanding_tax_is_not_double_counted_after_server_receipt()
    {
        Assert.Equal(40, HdvTaxBudget.UnconfirmedReservation(60, 100, 120));
        Assert.True(HdvTaxBudget.CanReserve(200, 120, 40, 40));
        Assert.False(HdvTaxBudget.CanReserve(200, 120, 40, 41));
        Assert.Equal(0, HdvTaxBudget.UnconfirmedReservation(60, 100, 170));
    }

    [Fact]
    public void Unknown_outcome_still_reserves_its_anticipated_tax()
    {
        Assert.Equal(60, HdvTaxBudget.UnconfirmedReservation(60, 100, 100));
        Assert.False(HdvTaxBudget.CanReserve(200, 100, 60, 50));
        Assert.True(HdvTaxBudget.CanReserve(0, 999999, 100, 500));
    }
}
