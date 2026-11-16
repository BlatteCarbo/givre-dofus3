extern alias F06App;

using HdvTaxReservations = F06App::GivreClone.App.Services.HdvTaxReservations;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvSharedTaxReservationsTests
{
    [Fact]
    public void The_next_manual_or_reprice_order_cannot_cross_the_global_cap()
    {
        var ledger = new HdvTaxReservations();
        Assert.True(ledger.TryReserve(101, "server:char", 100, 70, 20));
        Assert.Equal(20, ledger.Pending(101, "server:char"));
        Assert.False(ledger.TryReserve(101, "server:char", 100, 70, 11));
        Assert.True(ledger.TryReserve(101, "server:char", 100, 70, 10));
        Assert.False(ledger.TryReserve(101, "server:char", 100, 70, 1));
    }

    [Fact]
    public void Ambiguous_outcome_or_global_tax_delta_never_frees_a_reservation()
    {
        var ledger = new HdvTaxReservations();
        Assert.True(ledger.TryReserve(1, "292:char", 100, 0, 60));
        // A sale may fail ambiguously, or a different operation may debit
        // 30 kamas. Neither outcome is an attributed receipt for this UID.
        Assert.False(ledger.TryReserve(1, "292:char", 100, 30, 41));
        Assert.Equal(60, ledger.Pending(1, "292:char"));
        Assert.False(ledger.TryReserve(1, "292:char", 100, 90, 11));
    }

    [Fact]
    public void Reservations_are_separate_by_process_and_scoped_character()
    {
        var ledger = new HdvTaxReservations();
        Assert.True(ledger.TryReserve(1, "292:char", 10, 0, 10));
        Assert.False(ledger.TryReserve(1, "292:char", 10, 0, 1));
        Assert.True(ledger.TryReserve(1, "353:char", 10, 0, 10));
        Assert.True(ledger.TryReserve(2, "292:char", 10, 0, 10));
        Assert.Equal(10, ledger.Pending(1, "292:char"));
    }

    [Fact]
    public void Missing_scope_invalid_figures_and_extreme_cap_fail_closed()
    {
        var ledger = new HdvTaxReservations();
        Assert.False(ledger.TryReserve(0, "292:char", 10, 0, 1));
        Assert.False(ledger.TryReserve(1, null, 10, 0, 1));
        Assert.False(ledger.TryReserve(1, "?", 10, 0, 1));
        Assert.False(ledger.TryReserve(1, "292:char", -1, 0, 1));
        Assert.False(ledger.TryReserve(1, "292:char", 10, -1, 1));
        Assert.False(ledger.TryReserve(1, "292:char", 10, 0, -1));
        Assert.True(ledger.TryReserve(1, "292:char", long.MaxValue, 0,
            long.MaxValue));
        Assert.False(ledger.TryReserve(1, "292:char", long.MaxValue, 0, 1));
    }

    [Fact]
    public void Zero_cap_preserves_historical_unlimited_setting_without_booking()
    {
        var ledger = new HdvTaxReservations();
        Assert.True(ledger.TryReserve(1, "292:char", 0, 0, long.MaxValue));
        Assert.Equal(0, ledger.Pending(1, "292:char"));
        Assert.False(ledger.TryReserve(1, "292:char", 0, -1, 1));
    }

    [Fact]
    public async Task Concurrent_mutations_cannot_book_more_than_the_remaining_budget()
    {
        var ledger = new HdvTaxReservations();
        var operations = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => ledger.TryReserve(77, "292:char", 100, 40, 10)))
            .ToArray();
        var results = await Task.WhenAll(operations);
        Assert.Equal(6, results.Count(success => success));
        Assert.Equal(60, ledger.Pending(77, "292:char"));
    }
}
