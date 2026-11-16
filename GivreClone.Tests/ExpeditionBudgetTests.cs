using System;
using GivreClone.App.Services.Expedition;
using Xunit;

namespace GivreClone.Tests;

public sealed class ExpeditionBudgetTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Multiple_runs_subtract_owned_stacks_once_even_for_duplicate_keys()
    {
        var items = new[] {
            new ExpeditionMaterial(42, "Clé", 1, 2, 1, 500m, At),
            new ExpeditionMaterial(42, "Clé", 1, 2, 1, 500m, At),
        };
        var result = ExpeditionBudget.Estimate(items, 3, At);
        var line = Assert.Single(result.Lines);
        Assert.Equal(6, line.Required);
        Assert.Equal(3, line.Available);
        Assert.Equal(3, line.Missing);
        Assert.Equal(1500m, line.MissingCost);
        Assert.Equal(1500m, result.FullEstimate);
    }

    [Fact]
    public void Absent_or_stale_prices_make_full_total_unknown()
    {
        var items = new[] {
            new ExpeditionMaterial(42, "Clé", 2, 0, 0, 500m, At.AddDays(-5)),
            new ExpeditionMaterial(55, "Potion", 1, 0, 0, 200m, At),
        };
        var result = ExpeditionBudget.Estimate(items, 2, At);
        Assert.Equal(400m, result.KnownSubtotal);
        Assert.Null(result.FullEstimate);
        Assert.True(result.RequiresPriceObservations);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(24 * 60, true)]
    [InlineData(24 * 60 + 1, false)]
    [InlineData(-1, false)]
    public void Quote_expires_strictly_after_its_age_limit_and_a_future_quote_is_not_trusted(int ageMinutes, bool priced)
    {
        var item = new ExpeditionMaterial(42, "Clé", 1, 0, 0, 500m, At.AddMinutes(-ageMinutes));
        var line = Assert.Single(ExpeditionBudget.Estimate(new[] { item }, 1, At).Lines);
        Assert.Equal(priced ? 500m : null, line.MissingCost);
    }

    [Fact]
    public void Custom_quote_age_limit_is_honoured_and_must_be_positive()
    {
        var item = new ExpeditionMaterial(42, "Clé", 1, 0, 0, 500m, At.AddHours(-2));
        Assert.Null(Assert.Single(ExpeditionBudget.Estimate(new[] { item }, 1, At, TimeSpan.FromHours(1)).Lines).MissingCost);
        Assert.Equal(500m, Assert.Single(ExpeditionBudget.Estimate(new[] { item }, 1, At, TimeSpan.FromHours(3)).Lines).MissingCost);
        Assert.Throws<ArgumentOutOfRangeException>(() => ExpeditionBudget.Estimate(new[] { item }, 1, At, TimeSpan.Zero));
    }

    [Fact]
    public void Already_owned_materials_need_no_price_and_unlimited_is_not_totalled()
    {
        var item = new ExpeditionMaterial(42, "Clé", 2, 9, 0);
        var known = ExpeditionBudget.Estimate(new[] { item }, 4, At);
        Assert.Equal(0, Assert.Single(known.Lines).Missing);
        Assert.Equal(0m, known.FullEstimate);
        var unlimited = ExpeditionBudget.Estimate(new[] { item }, 0, At);
        Assert.True(unlimited.Unlimited);
        Assert.Equal(2, Assert.Single(unlimited.Lines).Required);
        Assert.Null(unlimited.FullEstimate);
    }

    [Fact]
    public void Contradictory_stocks_or_negative_inputs_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ExpeditionBudget.Estimate(new[] {
            new ExpeditionMaterial(42, "Clé", 1, 2, 1),
            new ExpeditionMaterial(42, "Clé", 1, 3, 1)
        }, 3, At));
        Assert.Throws<ArgumentException>(() => ExpeditionBudget.Estimate(new[] {
            new ExpeditionMaterial(42, "Clé", -1, 0, 0)
        }, 1, At));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExpeditionBudget.Estimate(
            Array.Empty<ExpeditionMaterial>(), -1, At));
    }
}
