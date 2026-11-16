using GivreClone.App.Services.Eco;
using Xunit;

namespace GivreClone.Tests;

public sealed class ObservedLotBuyTests
{
    [Fact]
    public async Task Missing_limits_buy_nothing_even_if_stop_is_already_requested()
    {
        var market = new FakeMarket();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var noBudget = await ObservedLotBuy.RunAsync(market, new(5, 1, 10, 0), null, cts.Token);
        var negative = await ObservedLotBuy.RunAsync(market, new(5, 1, 10, -5), null, default);
        var noQty = await ObservedLotBuy.RunAsync(market, new(5, 0, 10, 100), null, default);
        var noCap = await ObservedLotBuy.RunAsync(market, new(5, 1, 0, 100), null, default);
        var noGid = await ObservedLotBuy.RunAsync(market, new(0, 1, 10, 100), null, default);

        Assert.Equal("budget non défini", noBudget.Stop);
        Assert.Equal("budget non défini", negative.Stop);
        Assert.Equal("rien à acheter", noQty.Stop);
        Assert.Equal("prix unitaire max absent", noCap.Stop);
        Assert.Equal("prix non observé", noGid.Stop);
        Assert.Equal(0, market.Reads);
        Assert.Empty(market.Purchases);
    }

    [Theory]
    [InlineData("manuel", 0L)]
    [InlineData("moyenne", 0L)]
    [InlineData("market", 0L)]
    [InlineData("", 0L)]
    [InlineData("marché", -1L)]
    public async Task Unfresh_or_foreign_prices_never_buy(string source, long age)
    {
        var market = new FakeMarket { OnRead = _ => new MarketObservation(source, age, new long[] { 10 }) };
        var progress = new List<ObservedLotProgress>();
        var result = await ObservedLotBuy.RunAsync(market, new(7, 1, 100, 1000), progress.Add, default);

        Assert.Equal(0, result.Bought);
        Assert.Equal(0, result.Spent);
        Assert.Equal("prix non observé", result.Stop);
        Assert.Empty(market.Purchases);
        Assert.Empty(progress);
    }

    [Fact]
    public async Task Null_observation_or_missing_age_never_buys()
    {
        var missing = new FakeMarket { OnRead = _ => null };
        var noAge = new FakeMarket { OnRead = _ => new MarketObservation("marché", null, new long[] { 10 }) };
        var unknown = new FakeMarket { OnRead = _ => new MarketObservation("marché", 0, new long[] { -1, 0, -1, -1 }) };
        var a = await ObservedLotBuy.RunAsync(missing, new(7, 1, 100, 1000), null, default);
        var b = await ObservedLotBuy.RunAsync(noAge, new(7, 1, 100, 1000), null, default);
        var c = await ObservedLotBuy.RunAsync(unknown, new(7, 1, 100, 1000), null, default);
        Assert.Equal("prix non observé", a.Stop);
        Assert.Equal("prix non observé", b.Stop);
        Assert.Equal("prix non observé", c.Stop);
        Assert.Empty(missing.Purchases);
        Assert.Empty(noAge.Purchases);
        Assert.Empty(unknown.Purchases);
    }

    [Fact]
    public async Task Age_limit_is_inclusive_and_the_next_millisecond_buys_nothing()
    {
        long limit = PriceAlerts.MaxObservationAgeMs;
        var fresh = new FakeMarket { OnRead = _ => new MarketObservation("marché", limit, new long[] { 10 }) };
        var stale = new FakeMarket { OnRead = _ => new MarketObservation("marché", limit + 1, new long[] { 10 }) };
        var bought = await ObservedLotBuy.RunAsync(fresh, new(3, 1, 10, 10), null, default);
        var skipped = await ObservedLotBuy.RunAsync(stale, new(3, 1, 10, 10), null, default);

        Assert.Equal(1, bought.Bought);
        Assert.Equal(10, bought.Spent);
        Assert.Equal("quantité atteinte", bought.Stop);
        Assert.Equal((3L, 1, 10L), Assert.Single(fresh.Purchases));
        Assert.Equal(0, skipped.Bought);
        Assert.Equal("prix non observé", skipped.Stop);
        Assert.Empty(stale.Purchases);
    }

    [Theory]
    [InlineData(LotPurchaseEvidence.None)]
    [InlineData(LotPurchaseEvidence.Accepted)]
    public async Task Acceptance_without_a_receipt_spends_nothing(LotPurchaseEvidence evidence)
    {
        var market = new FakeMarket
        {
            OnRead = _ => Fresh(10),
            Evidence = evidence,
        };
        var progress = new List<ObservedLotProgress>();
        var result = await ObservedLotBuy.RunAsync(market, new(9, 1, 100, 1000), progress.Add, default);

        Assert.Equal(0, result.Bought);
        Assert.Equal(0, result.Spent);
        Assert.Equal(1, result.Uncovered);
        Assert.Equal("achat refusé", result.Stop);
        Assert.Equal((9L, 1, 10L), Assert.Single(market.Purchases));
        Assert.Empty(progress);
    }

    [Fact]
    public async Task Buys_largest_observed_lots_one_by_one_and_reports_each_receipt()
    {
        // x1 = 10, x10 = 90. Quantité 23 : deux x10 puis trois x1. Le plafond unitaire est plus haut que le prix observé.
        var market = new FakeMarket { OnRead = _ => new MarketObservation("marché", 0, new long[] { 10, 90, -1, -1 }) };
        var progress = new List<ObservedLotProgress>();
        var result = await ObservedLotBuy.RunAsync(market, new(7, 23, 100, 10_000), progress.Add, default);

        Assert.Equal(23, result.Bought);
        Assert.Equal(2 * 90 + 3 * 10, result.Spent);
        Assert.Equal(0, result.Uncovered);
        Assert.Equal("quantité atteinte", result.Stop);
        Assert.Equal(5, market.Reads);
        Assert.Equal(new[] { (7L, 10, 90L), (7L, 10, 90L), (7L, 1, 10L), (7L, 1, 10L), (7L, 1, 10L) }, market.Purchases);
        Assert.Equal(new[] { 10, 20, 21, 22, 23 }, progress.Select(p => p.Bought));
        Assert.Equal(new[] { 90L, 180L, 190L, 200L, 210L }, progress.Select(p => p.Spent));
        Assert.Equal(0, progress[^1].Remaining);
        Assert.Equal(10_000 - 210, progress[^1].BudgetLeft);
    }

    [Fact]
    public async Task Second_lot_uses_the_new_observation_and_stops_when_it_exceeds_the_unit_cap()
    {
        var market = new FakeMarket
        {
            OnRead = n => n == 1 ? Fresh(10) : Fresh(25),
        };
        var result = await ObservedLotBuy.RunAsync(market, new(4, 2, 20, 100), null, default);

        Assert.Equal(1, result.Bought);
        Assert.Equal(10, result.Spent);
        Assert.Equal(1, result.Uncovered);
        Assert.Equal("aucun lot achetable", result.Stop);
        Assert.Equal(2, market.Reads);
        Assert.Equal((4L, 1, 10L), Assert.Single(market.Purchases));
    }

    [Fact]
    public async Task Budget_stops_before_a_lot_that_would_exceed_it()
    {
        var market = new FakeMarket { OnRead = _ => Fresh(40) };
        var progress = new List<ObservedLotProgress>();
        var result = await ObservedLotBuy.RunAsync(market, new(4, 3, 40, 50), progress.Add, default);

        Assert.Equal(1, result.Bought);
        Assert.Equal(40, result.Spent);
        Assert.Equal(2, result.Uncovered);
        Assert.Equal("budget atteint", result.Stop);
        Assert.Equal(2, market.Reads);
        Assert.Equal((4L, 1, 40L), Assert.Single(market.Purchases));
        Assert.Equal(10, Assert.Single(progress).BudgetLeft);
    }

    [Fact]
    public async Task Exact_budget_does_not_open_another_observation()
    {
        var market = new FakeMarket { OnRead = _ => Fresh(40) };
        var result = await ObservedLotBuy.RunAsync(market, new(4, 2, 40, 40), null, default);
        Assert.Equal(1, result.Bought);
        Assert.Equal(40, result.Spent);
        Assert.Equal("budget atteint", result.Stop);
        Assert.Equal(1, market.Reads);
        Assert.Single(market.Purchases);
    }

    [Fact]
    public async Task Refusal_keeps_only_the_receipts_already_counted()
    {
        var market = new FakeMarket { OnRead = _ => Fresh(15), RefuseAfter = 1 };
        var progress = new List<ObservedLotProgress>();
        var result = await ObservedLotBuy.RunAsync(market, new(8, 3, 15, 100), progress.Add, default);

        Assert.Equal(1, result.Bought);
        Assert.Equal(15, result.Spent);
        Assert.Equal("achat refusé", result.Stop);
        Assert.Equal(2, market.Purchases.Count);
        Assert.Equal(new ObservedLotProgress(1, 15, 2, 85), Assert.Single(progress));
    }

    [Fact]
    public async Task Stop_after_a_receipt_does_not_buy_the_next_lot()
    {
        using var cts = new CancellationTokenSource();
        var market = new FakeMarket { OnRead = _ => Fresh(10), AfterPurchase = () => cts.Cancel() };
        var result = await ObservedLotBuy.RunAsync(market, new(2, 4, 10, 100), null, cts.Token);

        Assert.Equal(1, result.Bought);
        Assert.Equal(10, result.Spent);
        Assert.Equal(3, result.Uncovered);
        Assert.Equal("arrêté", result.Stop);
        Assert.Equal(1, market.Reads);
        Assert.Single(market.Purchases);
    }

    [Fact]
    public async Task Stop_before_the_first_read_buys_nothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var market = new FakeMarket { OnRead = _ => Fresh(10) };
        var result = await ObservedLotBuy.RunAsync(market, new(2, 4, 10, 100), null, cts.Token);
        Assert.Equal(0, result.Bought);
        Assert.Equal("arrêté", result.Stop);
        Assert.Equal(0, market.Reads);
        Assert.Empty(market.Purchases);
    }

    [Fact]
    public async Task Does_not_overbuy_a_larger_lot_and_rejects_a_fractional_unit_above_the_cap()
    {
        var tooBig = new FakeMarket { OnRead = _ => new MarketObservation("marché", 0, new long[] { -1, 10, -1, -1 }) };
        var over = await ObservedLotBuy.RunAsync(tooBig, new(1, 5, 100, 1000), null, default);
        Assert.Equal(0, over.Bought);
        Assert.Equal("aucun lot achetable", over.Stop);
        Assert.Empty(tooBig.Purchases);

        // 95 kamas pour x10 = 9,5 / unité. Plafond 9 : 95 > 90, on n'achète pas. Plafond 10 : 95 <= 100.
        var nine = new FakeMarket { OnRead = _ => new MarketObservation("marché", 0, new long[] { -1, 95, -1, -1 }) };
        var blocked = await ObservedLotBuy.RunAsync(nine, new(1, 10, 9, 1000), null, default);
        Assert.Equal(0, blocked.Bought);
        Assert.Empty(nine.Purchases);

        var ten = new FakeMarket { OnRead = _ => new MarketObservation("marché", 0, new long[] { -1, 95, -1, -1 }) };
        var allowed = await ObservedLotBuy.RunAsync(ten, new(1, 10, 10, 95), null, default);
        Assert.Equal(10, allowed.Bought);
        Assert.Equal(95, allowed.Spent);
        Assert.Equal((1L, 10, 95L), Assert.Single(ten.Purchases));
    }

    [Fact]
    public async Task Huge_unit_cap_still_pays_the_observed_price()
    {
        var market = new FakeMarket { OnRead = _ => new MarketObservation("marché", 0, new long[] { -1, -1, -1, 100 }) };
        var result = await ObservedLotBuy.RunAsync(market, new(6, 1000, long.MaxValue, 100), null, default);
        Assert.Equal(1000, result.Bought);
        Assert.Equal(100, result.Spent);
        Assert.Equal((6L, 1000, 100L), Assert.Single(market.Purchases));
    }

    private static MarketObservation Fresh(params long[] prices) => new("marché", 0, prices);

    private sealed class FakeMarket : IObservedLotMarket
    {
        public int Reads;
        public List<(long Gid, int Qty, long Max)> Purchases = new();
        public Func<int, MarketObservation?>? OnRead;
        public LotPurchaseEvidence Evidence = LotPurchaseEvidence.Receipt;
        public int RefuseAfter = int.MaxValue;
        public Action? AfterPurchase;

        public Task<MarketObservation?> ReadAsync(long gid, CancellationToken ct)
        {
            Reads++;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<MarketObservation?>(OnRead?.Invoke(Reads));
        }

        public Task<LotPurchaseEvidence> PurchaseAsync(long gid, int quantity, long maxPrice, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Purchases.Add((gid, quantity, maxPrice));
            var evidence = Purchases.Count > RefuseAfter ? LotPurchaseEvidence.Accepted : Evidence;
            AfterPurchase?.Invoke();
            return Task.FromResult(evidence);
        }
    }
}
