using GivreClone.App.Services.Eco;
using Xunit;

namespace GivreClone.Tests;

public class EconomyOptimizationPolicyTests
{
    private static RuneMarketLot Lot(RuneGrade grade, int size, long price, int count = 1, int server = 99)
        => new(server, 100 + (int)grade, grade, size, price, count);

    [Fact]
    public void Ra_compares_direct_Pa_and_simple_fusion_with_transport()
    {
        var result = RuneAcquisitionPolicy.Compare(99, RuneGrade.Ra, 2, new[]
        {
            Lot(RuneGrade.Ra, 1, 100, 2),
            Lot(RuneGrade.Pa, 1, 25, 6),
            Lot(RuneGrade.Simple, 10, 50, 2)
        }, 0, 20);
        Assert.Equal(3, result.Routes.Count);
        var best = result.Cheapest!;
        Assert.Equal(RuneGrade.Simple, best.SourceGrade);
        Assert.Equal(18, best.RequiredInputs);
        Assert.Equal(20, best.PurchasedInputs);
        Assert.Equal(8, best.FusionOperations);
        Assert.Equal(120, best.TotalCost);
    }

    [Fact]
    public void Rounded_lots_and_transport_can_make_direct_cheaper()
    {
        var result = RuneAcquisitionPolicy.Compare(99, RuneGrade.Pa, 1, new[]
        {
            Lot(RuneGrade.Pa, 1, 20), Lot(RuneGrade.Simple, 10, 10)
        }, 0, 15);
        Assert.Equal(RuneGrade.Pa, result.Cheapest!.SourceGrade);
        Assert.Equal(25, result.Routes.Single(r => r.SourceGrade == RuneGrade.Simple).TotalCost);
    }

    [Fact]
    public void Available_stock_is_bounded_and_mixed_lots_choose_lowest_total()
    {
        var result = RuneAcquisitionPolicy.Compare(99, RuneGrade.Simple, 12, new[]
        {
            Lot(RuneGrade.Simple, 10, 80),
            Lot(RuneGrade.Simple, 1, 10, 2),
            Lot(RuneGrade.Simple, 100, 999)
        }, 7, 0);
        Assert.Equal(107, result.Cheapest!.TotalCost);
        Assert.Equal(12, result.Cheapest.PurchasedInputs);
        Assert.Equal(3, result.Cheapest.Purchases.Sum(p => p.Lots));
    }

    [Fact]
    public void May_buy_oversized_lot_when_it_is_cheaper_than_exact_quantity()
    {
        var result = RuneAcquisitionPolicy.Compare(99, RuneGrade.Simple, 2, new[]
        {
            Lot(RuneGrade.Simple, 1, 10, 2), Lot(RuneGrade.Simple, 1000, 15)
        }, 0, 0);
        Assert.Equal(15, result.Cheapest!.TotalCost);
        Assert.Equal(1000, result.Cheapest.PurchasedInputs);
    }

    [Fact]
    public void Other_server_unknown_price_and_unknown_transport_are_not_free()
    {
        var result = RuneAcquisitionPolicy.Compare(99, RuneGrade.Pa, 1, new[]
        {
            Lot(RuneGrade.Pa, 1, 1, server: 351),
            Lot(RuneGrade.Pa, 1, 0),
            Lot(RuneGrade.Simple, 1, 1, 3)
        }, 0, null);
        Assert.Empty(result.Routes);
        Assert.Null(result.Cheapest);
    }

    [Fact]
    public void Incomplete_stock_does_not_produce_partial_recommendation()
    {
        var result = RuneAcquisitionPolicy.Compare(99, RuneGrade.Ra, 1,
            new[] { Lot(RuneGrade.Simple, 1, 1, 8) }, 0, 0);
        Assert.Empty(result.Routes);
    }

    [Fact]
    public void Equal_total_prefers_direct_over_fusion()
    {
        var result = RuneAcquisitionPolicy.Compare(99, RuneGrade.Pa, 1, new[]
        {
            Lot(RuneGrade.Pa, 1, 3), Lot(RuneGrade.Simple, 1, 1, 3)
        }, 0, 0);
        Assert.Equal(RuneGrade.Pa, result.Cheapest!.SourceGrade);
    }

    [Fact]
    public void First_trial_is_exactly_one_from_selected_stack_and_cannot_release_before_result()
    {
        var policy = new CrushTrialPolicy(new(7, 100, 5));
        Assert.Equal(new CrushSelection(7, 100, 1), policy.Trial);
        Assert.Null(policy.AuthorizeRemaining(new(7, 100, 4), true));
    }

    [Fact]
    public void Measured_yield_is_snapshotted_and_requires_separate_explicit_approval()
    {
        var policy = new CrushTrialPolicy(new(7, 100, 5));
        var runes = new Dictionary<long, int> { [200] = 3 };
        Assert.True(policy.RecordTrial(7, 1, true, runes, _ => 10));
        runes[200] = 999;
        Assert.Equal(3, policy.MeasuredYield!.Runes[200]);
        Assert.Equal(30, policy.MeasuredYield.KnownValue);
        Assert.Null(policy.AuthorizeRemaining(new(7, 100, 4), false));
        Assert.Equal(new CrushSelection(7, 100, 4), policy.AuthorizeRemaining(new(7, 100, 4), true));
        Assert.Null(policy.AuthorizeRemaining(new(7, 100, 4), true));
    }

    [Theory]
    [InlineData(false, 7, 1)]
    [InlineData(true, 8, 1)]
    [InlineData(true, 7, 5)]
    public void Missing_or_mismatched_trial_receipt_cannot_authorize_batch(bool confirmed, int uid, int consumed)
    {
        var policy = new CrushTrialPolicy(new(7, 100, 5));
        Assert.False(policy.RecordTrial(uid, consumed, confirmed, new Dictionary<long, int>(), _ => 10));
        Assert.Null(policy.MeasuredYield);
        Assert.Null(policy.AuthorizeRemaining(new(7, 100, 4), true));
    }

    [Theory]
    [InlineData(8, 100, 4)]
    [InlineData(7, 101, 4)]
    [InlineData(7, 100, 5)]
    [InlineData(7, 100, 3)]
    public void Approval_never_includes_another_item_or_changed_batch(int uid, long gid, int quantity)
    {
        var policy = new CrushTrialPolicy(new(7, 100, 5));
        Assert.True(policy.RecordTrial(7, 1, true, new Dictionary<long, int> { [200] = 1 }, _ => 10));
        Assert.Null(policy.AuthorizeRemaining(new(uid, gid, quantity), true));
    }

    [Fact]
    public void Unknown_yield_price_stays_unknown_and_single_item_has_no_remainder()
    {
        var policy = new CrushTrialPolicy(new(7, 100, 1));
        Assert.True(policy.RecordTrial(7, 1, true, new Dictionary<long, int> { [200] = 2 }, _ => null));
        Assert.Null(policy.MeasuredYield!.KnownValue);
        Assert.Null(policy.AuthorizeRemaining(new(7, 100, 0), true));
        Assert.False(policy.RecordTrial(7, 1, true, new Dictionary<long, int>(), _ => 1));
    }
}
