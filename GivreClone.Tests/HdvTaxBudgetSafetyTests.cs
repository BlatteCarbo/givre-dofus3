extern alias F06App;

using HdvTaxBudget = F06App::GivreClone.App.Services.HdvTaxBudget;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvTaxBudgetSafetyTests
{
    [Fact]
    public void Unattributed_external_tax_receipt_never_releases_a_bulk_reservation()
    {
        const long projectedBulkFee = 60;
        const long observedTaxIncludingExternal = 130;
        const long cap = 200;
        Assert.Equal(60, HdvTaxBudget.ReservationWithoutAttributedReceipts(projectedBulkFee));
        Assert.False(HdvTaxBudget.CanReserve(cap, observedTaxIncludingExternal,
            HdvTaxBudget.ReservationWithoutAttributedReceipts(projectedBulkFee), 11));
        // A global-ledger subtraction would misleadingly free 30 of this
        // reservation even though that receipt could be another HDV action.
        Assert.Equal(30, HdvTaxBudget.UnconfirmedReservation(60, 100, 130));
    }

    [Theory]
    [InlineData(-1L, 0L, 0L, 0L, false)]
    [InlineData(0L, 1L, 1L, 1L, true)]
    [InlineData(0L, -1L, 0L, 1L, false)]
    [InlineData(100L, 0L, 60L, 40L, true)]
    [InlineData(100L, 0L, 60L, 41L, false)]
    [InlineData(long.MaxValue, long.MaxValue - 1, 1L, 1L, false)]
    [InlineData(long.MaxValue, 0L, long.MaxValue, 1L, false)]
    public void Invalid_or_overflowing_tax_budgets_fail_closed(long cap,
        long observed, long reserved, long next, bool accepted)
        => Assert.Equal(accepted, HdvTaxBudget.CanReserve(cap, observed, reserved, next));

    [Fact]
    public void Rate_and_price_limits_never_underestimate_by_wrapping()
    {
        Assert.Equal(long.MaxValue, HdvTaxBudget.EstimateUpperBound(long.MaxValue, 100f));
        Assert.Null(HdvTaxBudget.EstimateUpperBound(long.MaxValue, 100.1f));
        Assert.Null(HdvTaxBudget.EstimateUpperBound(-1, 2f));
        Assert.Null(HdvTaxBudget.EstimateUpperBound(10, float.PositiveInfinity));
    }
}
