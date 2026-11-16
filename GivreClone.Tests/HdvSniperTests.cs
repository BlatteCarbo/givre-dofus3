using System.Text.Json;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;
using GivreClone.App.ViewModels;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Sniper, marge de lot, décomposition et offre choisie. Horloge et tirage injectés : aucun Task.Delay.
/// Le pourcentage sniper est un plafond (« au plus N % du prix moyen »), pas un rabais de N %.
/// </summary>
public class HdvSniperTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 31, 18, 0, 0, TimeSpan.Zero);

    private static HdvSniperLot Lot(int qty, long max, bool allowed = true) => new(qty, max, allowed);
    private static HdvSniperItem Item(long gid, int? cap, params HdvSniperLot[] lots) => new(gid, cap, lots);
    private static HdvSniperConfig Cfg(long budget, int percent = 80, int waitMin = 2, int waitMax = 6)
        => new(percent, waitMin, waitMax, budget);
    private static HdvSniperItem[] One(int? cap = 1, int qty = 1, long max = 1000) => new[] { Item(7, cap, Lot(qty, max)) };

    private static async Task<HdvSniperResult> Run(
        SniperFakePort port, SniperManualClock clock, SniperScriptRandom random, HdvSniperConfig config,
        Func<IReadOnlyList<HdvSniperItem>> items, CancellationToken ct, Action<HdvSniperPurchase>? onConfirmed = null)
    {
        int snaps = 0;
        return await HdvSniper.WatchAsync(port, clock, random, config, () =>
        {
            if (++snaps > 8) throw new InvalidOperationException("boucle sans arrêt");
            return items();
        }, onConfirmed, ct);
    }

    private static (SniperFakePort Port, SniperManualClock Clock, SniperScriptRandom Random, CancellationTokenSource Cts) Rig(params int[] draws)
    {
        var clock = new SniperManualClock();
        var cts = new CancellationTokenSource();
        clock.OnDelay = () => cts.Cancel();
        return (new SniperFakePort(), clock, new SniperScriptRandom(draws), cts);
    }

    [Fact]
    public void Qualifies_at_or_under_the_ceiling_percent_of_the_average_unit_price()
    {
        Assert.True(HdvSniper.Qualifies(80, 1, 1000, 100, 80));
        Assert.False(HdvSniper.Qualifies(81, 1, 1000, 100, 80));
        Assert.True(HdvSniper.Qualifies(800, 10, 1000, 100, 80));
        Assert.False(HdvSniper.Qualifies(801, 10, 1000, 100, 80));
        Assert.True(HdvSniper.Qualifies(80, 1, 80, 100, 80));
        Assert.False(HdvSniper.Qualifies(81, 1, 80, 100, 80));
        Assert.False(HdvSniper.Qualifies(80, 1, 0, 100, 80));
        Assert.False(HdvSniper.Qualifies(80, 1, 1000, 100, 0));
        Assert.False(HdvSniper.Qualifies(80, 1, 1000, 0, 80));
        Assert.False(HdvSniper.Qualifies(0, 1, 1000, 100, 80));
        Assert.False(HdvSniper.Qualifies(80, 0, 1000, 100, 80));
        Assert.True(HdvSniper.Qualifies(100, 1, 1000, 100, 150));
        Assert.False(HdvSniper.Qualifies(101, 1, 1000, 100, 150));
        Assert.False(HdvSniper.Qualifies(long.MaxValue / 2, 1, long.MaxValue, 1, 80));
    }

    [Fact]
    public void HasDemand_requires_an_allowed_lot_with_a_max_and_a_positive_or_open_cap()
    {
        Assert.False(HdvSniper.HasDemand(null));
        Assert.False(HdvSniper.HasDemand(Array.Empty<HdvSniperItem>()));
        Assert.False(HdvSniper.HasDemand(new[] { Item(1, 1, Lot(1, 0)) }));
        Assert.False(HdvSniper.HasDemand(new[] { Item(1, 1, Lot(1, 100, allowed: false)) }));
        Assert.False(HdvSniper.HasDemand(new[] { Item(1, 0, Lot(1, 100)) }));
        Assert.True(HdvSniper.HasDemand(new[] { Item(1, null, Lot(1, 100)) }));
        Assert.True(HdvSniper.HasDemand(new[] { Item(1, 5, Lot(10, 100)) }));
    }

    [Fact]
    public void WaitSeconds_sorts_the_bounds_and_clamps_negatives_before_the_draw()
    {
        var swapped = new SniperScriptRandom(4);
        Assert.Equal(4, HdvSniper.WaitSeconds(6, 2, swapped));
        Assert.Equal((2, 6), swapped.Calls.Single());

        var negative = new SniperScriptRandom(0);
        Assert.Equal(0, HdvSniper.WaitSeconds(-4, -1, negative));
        Assert.Equal((0, 0), negative.Calls.Single());

        var mixed = new SniperScriptRandom(2);
        Assert.Equal(2, HdvSniper.WaitSeconds(-3, 4, mixed));
        Assert.Equal((0, 4), mixed.Calls.Single());
    }

    [Fact]
    public async Task Second_read_that_no_longer_qualifies_does_not_buy_then_waits()
    {
        var (port, clock, random, cts) = Rig(4);
        int n = 0;
        long[] seq = { 50, 90 };
        port.Price = (_, _) => seq[Math.Min(n++, seq.Length - 1)];
        var result = await Run(port, clock, random, Cfg(1000), () => One(), cts.Token);

        Assert.Equal("arrêté", result.Stop);
        Assert.Equal(0, result.Spent);
        Assert.Empty(result.Purchases);
        Assert.Empty(port.Buys);
        Assert.Equal(2, port.Reads);
        Assert.Equal(new[] { TimeSpan.FromSeconds(4) }, clock.Delays);
        Assert.Equal((2, 6), random.Calls.Single());
        Assert.Equal(Start + TimeSpan.FromSeconds(4), clock.UtcNow);
    }

    [Fact]
    public async Task Buy_pays_the_second_price_and_stops_when_that_price_exhausts_the_budget()
    {
        var (port, clock, random, cts) = Rig(4);
        int n = 0;
        long[] seq = { 80, 70 };
        port.Price = (_, _) => seq[Math.Min(n++, seq.Length - 1)];
        var result = await Run(port, clock, random, Cfg(70), () => One(cap: 5), cts.Token);

        Assert.Equal("budget atteint", result.Stop);
        Assert.Equal(70, result.Spent);
        Assert.Equal(1, result.BoughtUnits);
        var buy = Assert.Single(result.Purchases);
        Assert.Equal((7L, 1, 70L), (buy.Gid, buy.Quantity, buy.Price));
        Assert.Equal(Start, buy.At);
        Assert.Equal(new[] { (7L, 1, 70L) }, port.Buys);
        Assert.Equal(2, port.Reads);
        Assert.Empty(clock.Delays);
        Assert.Empty(random.Calls);
    }

    [Fact]
    public async Task Purchase_is_stamped_before_the_injected_wait()
    {
        var (port, clock, random, cts) = Rig(4);
        port.Price = (_, _) => 40;
        int callbacks = 0;
        var result = await Run(port, clock, random, Cfg(1000), () => One(), cts.Token, _ => callbacks++);

        var buy = Assert.Single(result.Purchases);
        Assert.Equal(40, buy.Price);
        Assert.Equal(Start, buy.At);
        Assert.Equal(1, callbacks);
        Assert.Equal(Start + TimeSpan.FromSeconds(4), clock.UtcNow);
        Assert.Equal(new[] { TimeSpan.FromSeconds(4) }, clock.Delays);
        Assert.Equal((2, 6), random.Calls.Single());
        Assert.Equal("arrêté", result.Stop);
        Assert.Equal(2, port.Reads);
    }

    [Fact]
    public async Task Swapped_and_negative_wait_settings_still_draw_inside_the_normalized_range()
    {
        var highLow = Rig(4);
        highLow.Port.Price = (_, _) => 90;
        var high = await Run(highLow.Port, highLow.Clock, highLow.Random, Cfg(1000, waitMin: 6, waitMax: 2), () => One(), highLow.Cts.Token);
        Assert.Equal("arrêté", high.Stop);
        Assert.Empty(highLow.Port.Buys);
        Assert.Equal(1, highLow.Port.Reads);
        Assert.Equal((2, 6), highLow.Random.Calls.Single());
        Assert.Equal(new[] { TimeSpan.FromSeconds(4) }, highLow.Clock.Delays);

        var negative = Rig(2);
        negative.Port.Price = (_, _) => 90;
        await Run(negative.Port, negative.Clock, negative.Random, Cfg(1000, waitMin: -3, waitMax: 4), () => One(), negative.Cts.Token);
        Assert.Equal((0, 4), negative.Random.Calls.Single());
        Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, negative.Clock.Delays);
    }

    [Fact]
    public async Task Unconfirmed_buy_spends_nothing_and_does_not_wait()
    {
        var (port, clock, random, cts) = Rig(4);
        port.Price = (_, _) => 40;
        port.Receipt = false;
        int callbacks = 0;
        var result = await Run(port, clock, random, Cfg(1000), () => One(), cts.Token, _ => callbacks++);

        Assert.Equal("achat non confirmé", result.Stop);
        Assert.Equal(0, result.Spent);
        Assert.Empty(result.Purchases);
        Assert.Equal(0, callbacks);
        Assert.Equal(new[] { (7L, 1, 40L) }, port.Buys);
        Assert.Empty(clock.Delays);
        Assert.Empty(random.Calls);
    }

    [Fact]
    public async Task Cancelled_buy_is_not_a_receipt()
    {
        var (port, clock, random, cts) = Rig(4);
        port.Price = (_, _) => 40;
        port.CancelBuy = true;
        int callbacks = 0;
        var result = await Run(port, clock, random, Cfg(1000), () => One(), cts.Token, _ => callbacks++);

        Assert.Equal("arrêté", result.Stop);
        Assert.Equal(0, result.Spent);
        Assert.Empty(result.Purchases);
        Assert.Equal(0, callbacks);
        Assert.Single(port.Buys);
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task Exact_budget_buys_once_and_does_not_read_again()
    {
        var (port, clock, random, cts) = Rig(4);
        port.Price = (_, _) => 60;
        var result = await Run(port, clock, random, Cfg(60), () => One(cap: null), cts.Token);

        Assert.Equal("budget atteint", result.Stop);
        Assert.Equal(60, result.Spent);
        Assert.Equal(new[] { (7L, 1, 60L) }, port.Buys);
        Assert.Equal(2, port.Reads);
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task Lot_above_the_remaining_budget_is_skipped_and_the_watch_continues()
    {
        var (port, clock, random, cts) = Rig(4);
        port.Price = (_, _) => 60;
        var result = await Run(port, clock, random, Cfg(50), () => One(), cts.Token);

        Assert.Equal("arrêté", result.Stop);
        Assert.Equal(0, result.Spent);
        Assert.Empty(port.Buys);
        Assert.Equal(2, port.Reads);
        Assert.Equal(new[] { TimeSpan.FromSeconds(4) }, clock.Delays);
    }

    [Fact]
    public async Task A_dropped_reference_on_the_second_read_blocks_the_buy()
    {
        var (port, clock, random, cts) = Rig(4);
        int refs = 0;
        port.ReferenceOf = _ => ++refs == 1 ? 100 : 10;
        port.Price = (_, _) => 80;
        var result = await Run(port, clock, random, Cfg(1000), () => One(), cts.Token);

        Assert.Empty(port.Buys);
        Assert.Equal(0, result.Spent);
        Assert.Equal(2, port.Reads);
        Assert.Equal(2, refs);
        Assert.Equal(new[] { TimeSpan.FromSeconds(4) }, clock.Delays);
    }

    [Fact]
    public async Task Already_cancelled_token_does_not_read_or_wait()
    {
        var (port, clock, random, cts) = Rig(4);
        port.Price = (_, _) => throw new InvalidOperationException("lecture interdite");
        cts.Cancel();
        var result = await Run(port, clock, random, Cfg(1000), () => One(), cts.Token);

        Assert.Equal("arrêté", result.Stop);
        Assert.Equal(0, result.Spent);
        Assert.Equal(0, port.Reads);
        Assert.Empty(clock.Delays);
        Assert.Empty(random.Calls);
    }

    [Fact]
    public async Task Zero_percent_or_budget_and_a_list_without_a_max_do_not_read()
    {
        var closed = new SniperFakePort { Price = (_, _) => throw new InvalidOperationException("lecture interdite") };
        var clock = new SniperManualClock();
        var random = new SniperScriptRandom(4);
        var rows = One();

        Assert.Equal("pourcentage sniper nul", (await Run(closed, clock, random, Cfg(1000, percent: 0), () => rows, CancellationToken.None)).Stop);
        Assert.Equal("budget non défini", (await Run(closed, clock, random, Cfg(0), () => rows, CancellationToken.None)).Stop);
        Assert.Equal("rien à surveiller", (await Run(closed, clock, random, Cfg(1000), () => new[] { Item(7, 1, Lot(1, 0)) }, CancellationToken.None)).Stop);
        Assert.Equal("rien à surveiller", (await Run(closed, clock, random, Cfg(1000), () => new[] { Item(7, 1, Lot(1, 100, false)) }, CancellationToken.None)).Stop);
        Assert.Equal(0, closed.Reads);
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task Known_kamas_below_the_lot_stop_without_waiting_unknown_kamas_do_not()
    {
        var shortWallet = Rig(4);
        shortWallet.Port.Price = (_, _) => 50;
        shortWallet.Port.Wallet = 10;
        var denied = await Run(shortWallet.Port, shortWallet.Clock, shortWallet.Random, Cfg(1000), () => One(), shortWallet.Cts.Token);
        Assert.Equal("pas assez de kamas", denied.Stop);
        Assert.Equal(0, denied.Spent);
        Assert.Empty(shortWallet.Port.Buys);
        Assert.Empty(shortWallet.Clock.Delays);
        Assert.Equal(2, shortWallet.Port.Reads);

        var unknown = Rig(4);
        unknown.Port.Price = (_, _) => 40;
        unknown.Port.Wallet = -1;
        var bought = await Run(unknown.Port, unknown.Clock, unknown.Random, Cfg(40), () => One(), unknown.Cts.Token);
        Assert.Equal("budget atteint", bought.Stop);
        Assert.Equal(40, bought.Spent);
        Assert.Equal(new[] { (7L, 1, 40L) }, unknown.Port.Buys);
    }

    [Fact]
    public async Task A_larger_lot_the_wallet_cannot_pay_does_not_block_a_smaller_affordable_lot()
    {
        var (port, clock, random, cts) = Rig(4);
        port.Wallet = 50;
        port.Price = (_, qty) => qty == 10 ? 100 : 40;
        var result = await Run(port, clock, random, Cfg(50), () => new[] { Item(7, 10, Lot(1, 1000), Lot(10, 1000)) }, cts.Token);

        Assert.Equal(new[] { (7L, 1, 40L) }, port.Buys);
        Assert.Equal(40, result.Spent);
        Assert.Equal("arrêté", result.Stop);
        Assert.Equal(new[] { TimeSpan.FromSeconds(4) }, clock.Delays);
    }

    [Fact]
    public async Task Largest_allowed_lot_is_bought_before_a_smaller_one()
    {
        var (port, clock, random, cts) = Rig(4);
        port.Price = (_, qty) => qty * 40L;
        var result = await Run(port, clock, random, Cfg(400), () => new[] { Item(7, 10, Lot(1, 1000), Lot(10, 1000)) }, cts.Token);

        Assert.Equal("budget atteint", result.Stop);
        Assert.Equal(new[] { (7L, 10, 400L) }, port.Buys);
        Assert.Equal(400, result.Spent);
        Assert.Equal(2, port.Reads);
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task Quantity_cap_stops_after_the_confirmed_units_and_never_overshoots()
    {
        var capped = Rig(4);
        int left = 10;
        int callbacks = 0;
        capped.Port.Price = (_, _) => 50;
        var done = await Run(capped.Port, capped.Clock, capped.Random, Cfg(500), () => new[] { Item(7, left, Lot(10, 1000)) }, capped.Cts.Token,
            purchase => { callbacks++; left -= purchase.Quantity; });
        Assert.Equal("quantité atteinte", done.Stop);
        Assert.Equal(1, callbacks);
        Assert.Equal(new[] { (7L, 10, 50L) }, capped.Port.Buys);
        Assert.Equal(50, done.Spent);
        Assert.Empty(capped.Clock.Delays);

        var tooBig = Rig(4);
        tooBig.Port.Price = (_, _) => throw new InvalidOperationException("lecture interdite");
        var skipped = await Run(tooBig.Port, tooBig.Clock, tooBig.Random, Cfg(1000), () => new[] { Item(7, 5, Lot(10, 1000)) }, tooBig.Cts.Token);
        Assert.Equal("arrêté", skipped.Stop);
        Assert.Empty(tooBig.Port.Buys);
        Assert.Equal(0, tooBig.Port.Reads);
        Assert.Equal(new[] { TimeSpan.FromSeconds(4) }, tooBig.Clock.Delays);
    }

    [Fact]
    public async Task One_pass_buys_every_qualifying_row_that_fits()
    {
        var (port, clock, random, cts) = Rig(4);
        port.Price = (gid, _) => gid == 7 ? 30 : 40;
        var rows = new[] { Item(7, 1, Lot(1, 1000)), Item(8, 1, Lot(1, 1000)) };
        var result = await Run(port, clock, random, Cfg(70), () => rows, cts.Token);

        Assert.Equal("budget atteint", result.Stop);
        Assert.Equal(new[] { (7L, 1, 30L), (8L, 1, 40L) }, port.Buys);
        Assert.Equal(70, result.Spent);
        Assert.Equal(4, port.Reads);
        Assert.Empty(clock.Delays);
    }

    private sealed class SniperFakePort : IHdvSniperPort
    {
        public Func<long, int, long> Price = (_, _) => 0;
        public Func<long, long> ReferenceOf = _ => 100;
        public long Wallet = 1_000_000;
        public bool Receipt = true;
        public bool CancelBuy;
        public int Reads;
        public List<(long Gid, int Qty, long Price)> Buys { get; } = new();
        public Task<long> FreshLotPriceAsync(long gid, int quantity, CancellationToken ct)
        {
            if (++Reads > 12) throw new InvalidOperationException("trop de lectures");
            return Task.FromResult(Price(gid, quantity));
        }
        public long ReferenceUnit(long gid) => ReferenceOf(gid);
        public long Kamas() => Wallet;
        public Task<bool> ConfirmedBuyAsync(long gid, int quantity, long price, CancellationToken ct)
        {
            if (Buys.Count >= 12) throw new InvalidOperationException("trop d'achats");
            Buys.Add((gid, quantity, price));
            if (CancelBuy) throw new OperationCanceledException();
            return Task.FromResult(Receipt);
        }
    }

    private sealed class SniperManualClock : IHdvSniperClock
    {
        public DateTimeOffset UtcNow { get; private set; } = Start;
        public List<TimeSpan> Delays { get; } = new();
        public Action? OnDelay;
        public Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            Delays.Add(delay);
            UtcNow += delay;
            OnDelay?.Invoke();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class SniperScriptRandom : IHdvSniperRandom
    {
        public List<(int Min, int Max)> Calls { get; } = new();
        private readonly int[] _values;
        private int _index;
        public SniperScriptRandom(params int[] values) => _values = values;
        public int NextInclusive(int minInclusive, int maxInclusive)
        {
            Calls.Add((minInclusive, maxInclusive));
            if (_index >= _values.Length) throw new InvalidOperationException("tirage en trop");
            int value = _values[_index++];
            if (value < minInclusive || value > maxInclusive)
                throw new InvalidOperationException($"tirage {value} hors {minInclusive}..{maxInclusive}");
            return value;
        }
    }
}

public class HdvCheaperLotTests
{
    private static LotQuote Q(int qty, long price, long max = 9_000_000, bool allowed = true, int minMissing = 0)
        => new(qty, price, allowed, max, minMissing);

    private static LotQuote[] DocLots() => new[] { Q(1000, 2_000_000), Q(100, 160_000) };

    [Fact]
    public void Original_plan_still_takes_the_largest_lot_on_the_doc_prices()
    {
        Assert.Equal(new[] { (1000, 1, 2_000_000L) }, HdvTradeLogic.Plan(1000, DocLots(), 0).Steps);
        Assert.Equal(HdvTradeLogic.Plan(1000, DocLots(), 5_000_000).Steps, HdvTradeLogic.Plan(1000, DocLots(), 5_000_000, 0).Steps);
        Assert.Equal(HdvTradeLogic.Plan(1000, DocLots(), 5_000_000).Cost, HdvTradeLogic.Plan(1000, DocLots(), 5_000_000, -5).Cost);
    }

    [Fact]
    public void Margin_above_the_gap_buys_the_smaller_lot_and_an_equal_gap_does_not()
    {
        var under = HdvTradeLogic.Plan(1000, DocLots(), 5_000_000, 10);
        Assert.Equal(new[] { (100, 10, 160_000L) }, under.Steps);
        Assert.Equal(1_600_000, under.Cost);
        Assert.Equal(0, under.Uncovered);

        Assert.Equal(new[] { (100, 10, 160_000L) }, HdvTradeLogic.Plan(1000, DocLots(), 0, 10).Steps);
        Assert.Equal(new[] { (1000, 1, 2_000_000L) }, HdvTradeLogic.Plan(1000, DocLots(), 5_000_000, 20).Steps);

        var exact = new[] { Q(1000, 2_000_000), Q(100, 180_000) };
        Assert.Equal(new[] { (1000, 1, 2_000_000L) }, HdvTradeLogic.Plan(1000, exact, 5_000_000, 10).Steps);
    }

    [Fact]
    public void Among_lots_that_beat_the_margin_the_lowest_unit_wins_and_a_tie_keeps_the_larger_qty()
    {
        var cheapest = new[] { Q(1000, 100_000), Q(100, 7_000), Q(10, 600) };
        var plan = HdvTradeLogic.Plan(1000, cheapest, 0, 10);
        Assert.Equal(new[] { (10, 100, 600L) }, plan.Steps);
        Assert.Equal(60_000, plan.Cost);

        var tie = new[] { Q(1000, 100_000), Q(10, 700), Q(100, 7_000) };
        Assert.Equal(new[] { (100, 10, 7_000L) }, HdvTradeLogic.Plan(1000, tie, 0, 10).Steps);
    }

    [Fact]
    public void Margin_keeps_the_surplus_threshold_and_the_budget()
    {
        var surplus = new[] { new LotQuote(1000, 7000, true, 0, 800), new LotQuote(100, 800, true, 0) };
        Assert.Equal(new[] { (1000, 1, 7000L) }, HdvTradeLogic.Plan(800, surplus, 0, 10).Steps);
        Assert.Equal(new[] { (100, 7, 800L) }, HdvTradeLogic.Plan(799, surplus, 0, 50).Steps);

        var tight = HdvTradeLogic.Plan(1000, DocLots(), 320_000, 10);
        Assert.Equal(new[] { (100, 2, 160_000L) }, tight.Steps);
        Assert.Equal(320_000, tight.Cost);
        Assert.Equal(800, tight.Uncovered);

        var filtered = new[] { Q(1000, 2_000_000, max: 1_000_000), Q(100, 160_000, allowed: false), Q(10, 1_000) };
        Assert.Equal(new[] { (10, 100, 1_000L) }, HdvTradeLogic.Plan(1000, filtered, 5_000_000, 10).Steps);
    }

    [Fact]
    public async Task Runner_uses_the_fresh_prices_for_the_margin_and_delegates_when_the_margin_is_not_positive()
    {
        var original = new BuyFakePort();
        var plain = await HdvBuyRunner.RunAsync(original, StaleDoc(), 5_000_000, false, CancellationToken.None);
        Assert.Equal(new[] { 1000 }, original.Qtys);
        Assert.Equal(new[] { 9_000_000L }, original.Ceilings);
        Assert.Equal(2_000_000, plain.Spent);
        Assert.Equal(1000, plain.Bought);
        Assert.Equal("", plain.Stop);

        var zero = new BuyFakePort();
        var delegated = await HdvBuyRunner.RunAsync(zero, StaleDoc(), 5_000_000, false, CancellationToken.None, 0);
        Assert.Equal(new[] { 1000 }, zero.Qtys);
        Assert.Equal(2_000_000, delegated.Spent);

        var negative = new BuyFakePort();
        await HdvBuyRunner.RunAsync(negative, StaleDoc(), 5_000_000, false, CancellationToken.None, -3);
        Assert.Equal(new[] { 1000 }, negative.Qtys);

        var cheap = new BuyFakePort();
        var margin = await HdvBuyRunner.RunAsync(cheap, StaleDoc(), 5_000_000, false, CancellationToken.None, 10);
        Assert.Equal(Enumerable.Repeat(100, 10), cheap.Qtys);
        Assert.All(cheap.Ceilings, ceiling => Assert.Equal(160_000L, ceiling));
        Assert.Equal(1_600_000, margin.Spent);
        Assert.Equal(1000, margin.Bought);
        Assert.Equal(0, margin.Uncovered);
        Assert.DoesNotContain(1000, cheap.Qtys);

        var equal = new BuyFakePort();
        var kept = await HdvBuyRunner.RunAsync(equal, StaleDoc(), 5_000_000, false, CancellationToken.None, 20);
        Assert.Equal(new[] { 1000 }, equal.Qtys);
        Assert.Equal(2_000_000, kept.Spent);

        var tight = new BuyFakePort();
        var partial = await HdvBuyRunner.RunAsync(tight, StaleDoc(), 320_000, false, CancellationToken.None, 10);
        Assert.Equal(new[] { 100, 100 }, tight.Qtys);
        Assert.Equal(320_000, partial.Spent);
        Assert.Equal(800, partial.Uncovered);

        var refused = new BuyFakePort { Receipt = false };
        var failed = await HdvBuyRunner.RunAsync(refused, StaleDoc(), 5_000_000, false, CancellationToken.None, 10);
        Assert.Equal(new[] { 100 }, refused.Qtys);
        Assert.Equal(0, failed.Bought);
        Assert.Equal(0, failed.Spent);
        Assert.Equal("achat refusé", failed.Stop);
    }

    [Fact]
    public async Task Refused_purchase_leaves_the_remaining_budget_untouched_and_a_receipt_reduces_it_by_the_price_paid()
    {
        const long budget = 1_000_000;
        var refused = await HdvBuyRunner.RunAsync(new BuyFakePort { Receipt = false }, StaleDoc(), budget, false, CancellationToken.None, 10);
        Assert.Equal(budget, budget - refused.Spent);

        var tight = await HdvBuyRunner.RunAsync(new BuyFakePort(), StaleDoc(), 320_000, false, CancellationToken.None, 10);
        Assert.Equal(0, 320_000 - tight.Spent);
        Assert.Equal(800, tight.Uncovered);
    }

    private static BuyRequest StaleDoc() => new(7, 1000, new[]
    {
        new LotQuote(1000, 160_000, true, 9_000_000),
        new LotQuote(100, 2_000_000, true, 9_000_000),
    });

    private sealed class BuyFakePort : IHdvBuyPort
    {
        public bool Receipt = true;
        public List<int> Qtys { get; } = new();
        public List<long> Ceilings { get; } = new();
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct)
            => Task.FromResult(qty == 1000 ? 2_000_000L : qty == 100 ? 160_000L : 0L);
        public Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct)
        {
            Qtys.Add(qty);
            Ceilings.Add(maxPrice);
            return Task.FromResult(Receipt);
        }
    }
}

public class HdvRecipeBreakdownTests
{
    private static HdvComponent C(long gid, int qty, string name) => new(gid, qty, name);

    [Fact]
    public void One_call_expands_every_level_down_to_the_raw_material()
    {
        var result = HdvRecipeBreakdown.DecomposeToRaw(new[] { C(1, 2, "Épée") }, gid => gid switch
        {
            1 => new[] { C(2, 3, "Lame") },
            2 => new[] { C(3, 4, "Minerai") },
            _ => null,
        });

        Assert.False(result.Overflow);
        Assert.False(result.CycleStopped);
        Assert.Equal(2, result.Expanded);
        Assert.Equal(new[] { C(3, 24, "Minerai") }, result.Rows);
    }

    [Fact]
    public void Shared_raw_material_is_summed_and_keeps_the_first_name()
    {
        var result = HdvRecipeBreakdown.DecomposeToRaw(new[] { C(1, 1, "A"), C(2, 1, "B") }, gid => gid switch
        {
            1 => new[] { C(3, 2, "Cuivre") },
            2 => new[] { C(3, 5, "Autre") },
            _ => null,
        });
        Assert.Equal(2, result.Expanded);
        Assert.Equal(new[] { C(3, 7, "Cuivre") }, result.Rows);
    }

    [Fact]
    public void Item_without_a_recipe_stays_and_a_non_positive_quantity_is_ignored()
    {
        var raw = HdvRecipeBreakdown.DecomposeToRaw(new[] { C(5, 4, "Bois") }, _ => null);
        Assert.Equal(0, raw.Expanded);
        Assert.Equal(new[] { C(5, 4, "Bois") }, raw.Rows);

        var none = HdvRecipeBreakdown.DecomposeToRaw(new[] { C(1, 0, "A"), C(2, -3, "B") }, _ => new[] { C(9, 1, "Minerai") });
        Assert.Equal(0, none.Expanded);
        Assert.Empty(none.Rows);

        var missing = HdvRecipeBreakdown.DecomposeToRaw(null!, _ => null);
        Assert.Empty(missing.Rows);
        Assert.Throws<ArgumentNullException>(() => HdvRecipeBreakdown.DecomposeToRaw(Array.Empty<HdvComponent>(), null!));
    }

    [Fact]
    public void A_cycle_stops_and_does_not_multiply_without_bound()
    {
        var loop = HdvRecipeBreakdown.DecomposeToRaw(new[] { C(1, 1, "A") }, gid => gid switch
        {
            1 => new[] { C(2, 2, "B") },
            2 => new[] { C(1, 1, "A") },
            _ => null,
        });
        Assert.True(loop.CycleStopped);
        Assert.False(loop.Overflow);
        Assert.Equal(2, loop.Expanded);
        Assert.Equal(new[] { C(1, 2, "A") }, loop.Rows);

        var self = HdvRecipeBreakdown.DecomposeToRaw(new[] { C(1, 1, "A") },
            gid => gid == 1 ? new[] { C(1, 1, "A"), C(9, 2, "Minerai") } : null);
        Assert.True(self.CycleStopped);
        Assert.Equal(1, self.Expanded);
        Assert.Equal(new[] { C(1, 1, "A"), C(9, 2, "Minerai") }, self.Rows);
    }

    [Fact]
    public void Overflow_discards_rows_already_accumulated()
    {
        var result = HdvRecipeBreakdown.DecomposeToRaw(new[] { C(1, int.MaxValue, "A") },
            gid => gid == 1 ? new[] { C(9, 1, "Minerai"), C(8, 2, "Gemme") } : null);
        Assert.True(result.Overflow);
        Assert.Empty(result.Rows);
    }
}

public class HdvChosenListingTests
{
    private static HdvListedEffect Fx(int id, int value) => new(id, value, 0, 0, 0, null, 0);
    private static HdvListedEffect[] Mark() => new[] { Fx(111, 10) };

    private static HdvChosenBuyRequest Req(long price = 100, long budget = 500, int qty = 1, int uid = 9, long gid = 5,
        IReadOnlyList<HdvListedEffect>? effects = null, bool nullEffects = false)
        => new(gid, uid, qty, price, nullEffects ? null! : effects ?? Mark(), budget);

    private static ChosenFakePort Open(long price = 100, long wallet = -1, bool receipt = true, IReadOnlyList<HdvListedEffect>? effects = null)
        => new()
        {
            Fresh = new HdvFreshListing(9, 5, price, effects ?? Mark()),
            Wallet = wallet,
            Receipt = receipt,
        };

    [Fact]
    public async Task Structural_refusals_do_not_touch_the_port()
    {
        var closed = new ChosenClosedPort();
        Assert.Equal("budget atteint", (await HdvChosenListing.BuyAsync(closed, Req(budget: 0), CancellationToken.None)).Reason);
        Assert.Equal("uid invalide", (await HdvChosenListing.BuyAsync(closed, Req(uid: 0), CancellationToken.None)).Reason);
        Assert.Equal("uid invalide", (await HdvChosenListing.BuyAsync(closed, Req(gid: 0), CancellationToken.None)).Reason);
        Assert.Equal("quantité de lot invalide", (await HdvChosenListing.BuyAsync(closed, Req(qty: 2), CancellationToken.None)).Reason);
        Assert.Equal("prix affiché nul", (await HdvChosenListing.BuyAsync(closed, Req(price: 0, budget: 500), CancellationToken.None)).Reason);
        Assert.Equal("budget insuffisant", (await HdvChosenListing.BuyAsync(closed, Req(price: 200, budget: 100), CancellationToken.None)).Reason);
    }

    [Fact]
    public async Task Fresh_price_effects_and_kamas_must_match_before_any_buy()
    {
        var price = Open(price: 101);
        var priced = await HdvChosenListing.BuyAsync(price, Req(), CancellationToken.None);
        Assert.Equal("le prix frais diffère du prix affiché", priced.Reason);
        Assert.Equal(0, priced.Spent);
        Assert.False(priced.Confirmed);
        Assert.Equal(0, price.BuyCalls);

        var order = Open(effects: new[] { Fx(222, 1), Fx(111, 10) });
        var changed = await HdvChosenListing.BuyAsync(order, Req(effects: new[] { Fx(111, 10), Fx(222, 1) }), CancellationToken.None);
        Assert.Equal("les effets ont changé", changed.Reason);
        Assert.Equal(0, order.BuyCalls);

        var moved = Open();
        moved.Fresh = new HdvFreshListing(8, 5, 100, Mark());
        Assert.Equal("offre introuvable", (await HdvChosenListing.BuyAsync(moved, Req(), CancellationToken.None)).Reason);
        Assert.Equal(0, moved.BuyCalls);

        var poor = Open(wallet: 99);
        Assert.Equal("pas assez de kamas", (await HdvChosenListing.BuyAsync(poor, Req(), CancellationToken.None)).Reason);
        Assert.Equal(0, poor.BuyCalls);

        var exact = Open(wallet: 100);
        var paid = await HdvChosenListing.BuyAsync(exact, Req(), CancellationToken.None);
        Assert.True(paid.Confirmed);
        Assert.Equal(100, paid.Spent);
        Assert.Equal(1, exact.BuyCalls);
        Assert.Equal(100, exact.LastPrice);

        var unknown = Open(wallet: -1);
        Assert.True((await HdvChosenListing.BuyAsync(unknown, Req(), CancellationToken.None)).Confirmed);
        Assert.Equal(1, unknown.BuyCalls);
    }

    [Fact]
    public async Task Empty_effects_match_and_a_false_receipt_spends_zero()
    {
        var bare = Open(effects: Array.Empty<HdvListedEffect>());
        var bought = await HdvChosenListing.BuyAsync(bare, Req(effects: Array.Empty<HdvListedEffect>()), CancellationToken.None);
        Assert.True(bought.Confirmed);
        Assert.Equal(100, bought.Spent);
        Assert.Equal(1, bare.BuyCalls);

        var none = Open(effects: Array.Empty<HdvListedEffect>());
        var missing = await HdvChosenListing.BuyAsync(none, Req(nullEffects: true), CancellationToken.None);
        Assert.True(missing.Confirmed);

        var refused = Open(receipt: false);
        var result = await HdvChosenListing.BuyAsync(refused, Req(), CancellationToken.None);
        Assert.False(result.Confirmed);
        Assert.Equal(0, result.Spent);
        Assert.Equal("achat non confirmé", result.Reason);
        Assert.Equal(1, refused.BuyCalls);
        Assert.Equal(100, refused.LastPrice);
    }

    [Fact]
    public async Task A_cancelled_token_does_not_send_the_buy()
    {
        var port = Open();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HdvChosenListing.BuyAsync(port, Req(), cts.Token));
        Assert.Equal(1, port.FreshCalls);
        Assert.Equal(0, port.BuyCalls);
    }

    private sealed class ChosenFakePort : IHdvChosenListingPort
    {
        public HdvFreshListing? Fresh;
        public long Wallet = -1;
        public bool Receipt = true;
        public int FreshCalls;
        public int BuyCalls;
        public long LastPrice;
        public Task<HdvFreshListing?> FreshAsync(long gid, int uid, int quantity, CancellationToken ct)
        {
            FreshCalls++;
            return Task.FromResult(Fresh);
        }
        public long Kamas() => Wallet;
        public Task<bool> ConfirmedBuyAsync(long gid, int uid, int quantity, long price, CancellationToken ct)
        {
            BuyCalls++;
            LastPrice = price;
            return Task.FromResult(Receipt);
        }
    }

    private sealed class ChosenClosedPort : IHdvChosenListingPort
    {
        public Task<HdvFreshListing?> FreshAsync(long gid, int uid, int quantity, CancellationToken ct)
            => throw new InvalidOperationException("recherche interdite");
        public long Kamas() => throw new InvalidOperationException("kamas interdits");
        public Task<bool> ConfirmedBuyAsync(long gid, int uid, int quantity, long price, CancellationToken ct)
            => throw new InvalidOperationException("achat interdit");
    }
}

public class HdvCheaperMarginSettingsTests
{
    [Fact]
    public void Margin_defaults_to_zero_and_round_trips_in_json()
    {
        Assert.Equal(0, new HdvTradeSettings().BuyCheaperMarginPct);
        var missing = JsonSerializer.Deserialize<HdvTradeSettings>("{\"BuyBudget\":1}")!;
        Assert.Equal(0, missing.BuyCheaperMarginPct);

        var origin = new HdvTradeSettings { BuyCheaperMarginPct = 12 };
        var clone = origin.Clone();
        Assert.Equal(12, clone.BuyCheaperMarginPct);
        clone.BuyCheaperMarginPct = 4;
        Assert.Equal(12, origin.BuyCheaperMarginPct);

        var json = JsonSerializer.Serialize(origin);
        Assert.Contains("\"BuyCheaperMarginPct\":12", json);
        Assert.Equal(12, JsonSerializer.Deserialize<HdvTradeSettings>(json)!.BuyCheaperMarginPct);
    }
}
