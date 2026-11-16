using GivreClone.App.ViewModels;
using Xunit;

namespace GivreClone.Tests;

public class HdvTradeLogicTests
{
    private static LotQuote L(int q, long p, bool a = true, long m = 0) => new(q, p, a, m);

    [Fact]
    public void Plan_takes_largest_allowed_lots_first_without_overbuying()
    {
        var plan = HdvTradeLogic.Plan(123, new[] { L(1, 10), L(10, 90), L(100, 800), L(1000, 7000) }, 0);
        Assert.Equal(new[] { (100, 1, 800L), (10, 2, 90L), (1, 3, 10L) }, plan.Steps);
        Assert.Equal(800 + 180 + 30, plan.Cost);
        Assert.Equal(123, plan.Covered);
        Assert.Equal(0, plan.Uncovered);
        Assert.Equal("1×x100 + 2×x10 + 3×x1", plan.Text);
    }

    [Fact]
    public void Plan_skips_disallowed_unpriced_and_over_max_lots()
    {
        var plan = HdvTradeLogic.Plan(100, new[] { L(100, 800, a: false), L(10, 0), L(1, 20, m: 10) }, 0);
        Assert.Empty(plan.Steps);
        Assert.Equal(100, plan.Uncovered);
        Assert.Equal("", plan.Text);
    }

    [Fact]
    public void Plan_accepts_a_surplus_when_the_missing_quantity_reaches_the_lot_threshold()
    {
        // x1000 dès qu'il en manque 800 : 1 lot pour 800, 2 pour 1 800 ; 1 700 = 1 lot de 1000 puis le reste en x100
        var lots = new[] { new LotQuote(1000, 7000, true, 0, 800), new LotQuote(100, 800, true, 0) };
        Assert.Equal(new[] { (1000, 1, 7000L) }, HdvTradeLogic.Plan(800, lots, 0).Steps);
        Assert.Equal(0, HdvTradeLogic.Plan(800, lots, 0).Uncovered);
        Assert.Equal(new[] { (1000, 2, 7000L) }, HdvTradeLogic.Plan(1800, lots, 0).Steps);
        Assert.Equal(new[] { (1000, 1, 7000L), (100, 7, 800L) }, HdvTradeLogic.Plan(1700, lots, 0).Steps);
        // seuil absent, nul ou au-dessus de la taille : la taille du lot (pas de surplus)
        Assert.Equal(new[] { (100, 7, 800L) }, HdvTradeLogic.Plan(799, lots, 0).Steps);
        Assert.Equal(1000, new LotQuote(1000, 1, true, 0, 5000).Threshold);
        Assert.Equal(1000, new LotQuote(1000, 1, true, 0).Threshold);
    }

    [Fact]
    public void Plan_never_exceeds_the_budget()
    {
        var plan = HdvTradeLogic.Plan(30, new[] { L(10, 100), L(1, 12) }, 250);
        Assert.True(plan.Cost <= 250);
        Assert.Equal(2, plan.Steps[0].Count);   // 2 lots de 10 = 200
        Assert.Equal(4, plan.Steps[1].Count);   // 4 unités = 48 -> total 248
        Assert.Equal(248, plan.Cost);
        Assert.Equal(6, plan.Uncovered);        // 30 - 20 - 4
    }

    [Theory]
    [InlineData(100L, 10, 5, 1050L)]    // 100 x 10 x 1,05
    [InlineData(0L, 10, 5, 0L)]
    [InlineData(100L, 0, 5, 0L)]
    [InlineData(100L, 10, -3, 1000L)]   // tolérance négative ramenée à 0
    public void MaxFromAverage_applies_tolerance(long avg, int qty, int tol, long expected)
        => Assert.Equal(expected, HdvTradeLogic.MaxFromAverage(avg, qty, tol));

    [Fact]
    public void Scale_and_min_from_market_are_clamped()
    {
        Assert.Equal(120, HdvTradeLogic.Scale(100, 120));
        Assert.Equal(0, HdvTradeLogic.Scale(0, 500));
        Assert.Equal(0, HdvTradeLogic.Scale(100, -10));
        Assert.Equal(80, HdvTradeLogic.MinFromMarket(100, 80));
        Assert.Equal(0, HdvTradeLogic.MinFromMarket(-1, 80));
        Assert.Equal(1050, HdvTradeLogic.MaxFromPrice(1000, 5));
        Assert.Equal(0, HdvTradeLogic.MaxFromPrice(0, 5));
    }

    [Theory]
    [InlineData(1000L, 0L, 1L, 999L)]     // marché moins sous-cotation
    [InlineData(1000L, 995L, 10L, 995L)]  // jamais sous le minimum
    [InlineData(1000L, 0L, -5L, 1000L)]   // sous-cotation négative ignorée
    [InlineData(1L, 0L, 5L, 1L)]          // plancher à 1
    [InlineData(0L, 500L, 1L, 0L)]        // marché inconnu : on ne vend pas à l'aveugle
    [InlineData(-1L, 0L, 1L, 0L)]
    public void RelistPrice_follows_market_but_respects_the_minimum(long market, long min, long undercut, long expected)
        => Assert.Equal(expected, HdvTradeLogic.RelistPrice(market, min, undercut));

    [Fact]
    public void Scan_filters_by_gain_and_percent_and_sorts()
    {
        var a = new HdvTradeLogic.ScanItem("Alpha", 1000, 1500, 5);   // +500, +50 %
        var b = new HdvTradeLogic.ScanItem("Bravo", 100, 160, 5);     // +60, +60 %
        var c = new HdvTradeLogic.ScanItem("Charlie", 500, 400, 5);   // perte
        Assert.True(HdvTradeLogic.PassesScan(a, 100, 40));
        Assert.False(HdvTradeLogic.PassesScan(b, 100, 40));           // gain 60 < 100
        Assert.False(HdvTradeLogic.PassesScan(c, 0, 0));              // perte : gain négatif < 0
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, HdvTradeLogic.Sort(new[] { c, b, a }, HdvTradeLogic.ScanSort.GainDesc).Select(i => i.Name));
        Assert.Equal(new[] { "Bravo", "Alpha", "Charlie" }, HdvTradeLogic.Sort(new[] { c, b, a }, HdvTradeLogic.ScanSort.GainPctDesc).Select(i => i.Name));
        Assert.Equal(new[] { "Bravo", "Charlie", "Alpha" }, HdvTradeLogic.Sort(new[] { c, b, a }, HdvTradeLogic.ScanSort.CostAsc).Select(i => i.Name));
    }

    [Fact]
    public void Suspect_when_few_samples_or_absurd_gain()
    {
        Assert.True(HdvTradeLogic.IsSuspect(new("x", 100, 150, 1)));
        Assert.True(HdvTradeLogic.IsSuspect(new("x", 100, 1000, 9)));
        Assert.False(HdvTradeLogic.IsSuspect(new("x", 100, 150, 9)));
    }

    // ---------------- Exécuteur d'achat avec un port factice ----------------
    private sealed class FakePort : IHdvBuyPort
    {
        public Dictionary<int, long> Prices = new();
        public int RefuseAfter = int.MaxValue;
        public List<(long Gid, int Qty, long Max)> Buys = new();
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct) => Task.FromResult(Prices.GetValueOrDefault(qty, -1L));
        public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct)
        {
            if (Buys.Count >= RefuseAfter) return Task.FromResult(false);
            Buys.Add((gid, qty, maxPrice));
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task Runner_refuses_without_budget()
    {
        var port = new FakePort { Prices = { [10] = 90 } };
        var r = await HdvBuyRunner.RunAsync(port, new(1, 10, new[] { L(10, 0, m: 100) }), 0, false, default);
        Assert.Equal(0, r.Bought);
        Assert.Empty(port.Buys);
        Assert.Equal("budget non défini", r.Stop);
    }

    [Fact]
    public async Task Runner_rereads_prices_and_buys_with_the_lot_max_as_ceiling()
    {
        var port = new FakePort { Prices = { [10] = 90, [1] = 10 } };
        var r = await HdvBuyRunner.RunAsync(port, new(7, 23, new[] { L(10, 999, m: 100), L(1, 999, m: 20) }), 1000, false, default);
        Assert.Equal(23, r.Bought);
        Assert.Equal(2 * 90 + 3 * 10, r.Spent);
        Assert.Equal(0, r.Uncovered);
        Assert.Equal(new[] { (7L, 10, 100L), (7L, 10, 100L), (7L, 1, 20L), (7L, 1, 20L), (7L, 1, 20L) }, port.Buys);
    }

    [Fact]
    public async Task Runner_skips_priced_lots_with_no_max_unless_price_is_ignored()
    {
        var port = new FakePort { Prices = { [10] = 90 } };
        var blocked = await HdvBuyRunner.RunAsync(port, new(1, 10, new[] { L(10, 0, m: 0) }), 1000, false, default);
        Assert.Equal(0, blocked.Bought);
        Assert.Empty(port.Buys);
        var allowed = await HdvBuyRunner.RunAsync(port, new(1, 10, new[] { L(10, 0, m: 0) }), 1000, true, default);
        Assert.Equal(10, allowed.Bought);
        Assert.Equal(90, port.Buys.Single().Max);   // plafond = prix relu quand le prix est ignoré
    }

    [Fact]
    public async Task Runner_stops_at_the_first_refusal()
    {
        var port = new FakePort { Prices = { [1] = 10 }, RefuseAfter = 2 };
        var r = await HdvBuyRunner.RunAsync(port, new(1, 5, new[] { L(1, 0, m: 50) }), 1000, false, default);
        Assert.Equal(2, r.Bought);
        Assert.Equal(20, r.Spent);
        Assert.Equal(3, r.Uncovered);
        Assert.Equal("achat refusé", r.Stop);
    }

    [Fact]
    public async Task Runner_honours_cancellation()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            HdvBuyRunner.RunAsync(new FakePort { Prices = { [1] = 10 } }, new(1, 3, new[] { L(1, 0, m: 50) }), 1000, false, cts.Token));
    }
    private sealed class CancelAfterFirstBuyPort : IHdvBuyPort
    {
        public CancellationTokenSource Cts { get; } = new();
        public List<int> Buys { get; } = new();
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct) => Task.FromResult(10L);
        public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct)
        {
            Buys.Add(qty);
            Cts.Cancel();
            return Task.FromResult(true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public async Task Stop_between_lots_reports_the_confirmed_lot_and_buys_nothing_more(int marginPct)
    {
        var port = new CancelAfterFirstBuyPort();
        var r = await HdvBuyRunner.RunAsync(port, new(1, 5, new[] { L(1, 0, m: 50) }), 1000, false, port.Cts.Token, marginPct);
        Assert.Equal(new[] { 1 }, port.Buys);
        Assert.Equal(1, r.Bought);
        Assert.Equal(10, r.Spent);
        Assert.Equal(4, r.Uncovered);
        Assert.Equal("achat arrêté", r.Stop);
    }
}
