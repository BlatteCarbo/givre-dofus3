using GivreClone.App.Services;
using GivreClone.App.Services.Eco;
using Xunit;

namespace GivreClone.Tests;

public class EcoToolsTests
{
    // ---- Forgemagie ----
    [Fact]
    public void Weight_uses_per_point_rates()
    {
        // 50 Force (1/pt) + 100 Vitalité (0,2/pt) + 1 PA (100/pt) = 50 + 20 + 100
        var stats = new[] { new FmStat(118, "Force", 50, 1m), new FmStat(125, "Vitalité", 100, 0.2m), new FmStat(111, "PA", 1, 100m) };
        Assert.Equal(170m, Forgemagie.TotalWeight(stats));
        Assert.Equal(3, Forgemagie.Positive(stats).Length);
    }

    [Fact]
    public void Malus_has_negative_weight_and_is_not_positive()
    {
        var s = new FmStat(157, "Force", -10, 0.5m);
        Assert.Equal(-5m, s.Weight);
        Assert.Empty(Forgemagie.Positive(new[] { new FmStat(157, "Force", 10, -0.5m) }));
    }

    [Fact]
    public void Rune_table_has_three_tiers_per_family_and_pa_ra_multiples()
    {
        var vi = Forgemagie.Runes.Where(r => r.Stat == "Vi").ToDictionary(r => r.Tier);
        Assert.Equal(5, vi["Ba"].Amount); Assert.Equal(15, vi["Pa"].Amount); Assert.Equal(50, vi["Ra"].Amount);
        Assert.Equal("Rune Ra Fo", Forgemagie.Runes.First(r => r.Stat == "Fo" && r.Tier == "Ra").Name);
        Assert.Equal(1m, Forgemagie.RuneWeight(vi["Ba"], 0.2m));
    }

    [Theory]
    [InlineData(118, 37, 3, 2, 1, 0)]     // 3 Ra (30) + 2 Pa (6) + 1 Ba (1)
    [InlineData(125, 85, 1, 2, 1, 0)]     // 50 + 30 + 5
    [InlineData(158, 25, 0, 0, 2, 5)]     // Pod : base 10, reste 5 non couvert
    public void Decomposes_points_into_runes(int effect, int points, int ra, int pa, int ba, int rest)
    {
        var d = Forgemagie.Decompose(effect, points)!.Value;
        Assert.Equal(new RuneMix(ra, pa, ba), d.Mix);
        Assert.Equal(rest, d.Remainder);
    }

    [Fact]
    public void Mix_cost_needs_every_price()
    {
        var mix = new RuneMix(1, 0, 2);
        Assert.Equal(1000m + 2 * 10m, Forgemagie.MixCost(118, mix, n => n == "Rune Ra Fo" ? 1000m : n == "Rune Fo" ? 10m : null));
        Assert.Null(Forgemagie.MixCost(118, mix, n => n == "Rune Ra Fo" ? 1000m : null));
    }

    [Fact]
    public void Unknown_stat_is_not_decomposed() => Assert.Null(Forgemagie.Decompose(9999, 10));

    // ---- Séance FM ----
    [Fact]
    public void Ledger_summarizes_spend_average_stock_and_cost_per_use()
    {
        var s = new FmSession { Item = "Coiffe" };
        s.Purchases.Add(new FmPurchase { Rune = "Rune Fo", Quantity = 10, UnitPrice = 100 });
        s.Purchases.Add(new FmPurchase { Rune = "Rune Fo", Quantity = 10, UnitPrice = 200 });
        s.Purchases.Add(new FmPurchase { Rune = "Rune Pa Fo", Quantity = 2, UnitPrice = 500 });
        s.Uses.Add(new FmUse { Rune = "Rune Fo", Count = 4 });
        s.Uses.Add(new FmUse { Rune = "Rune Pa Fo", Count = 1 });
        var sum = FmLedger.Summarize(s);
        Assert.Equal(10 * 100 + 10 * 200 + 2 * 500m, sum.TotalSpent);
        var fo = sum.Runes.First(r => r.Rune == "Rune Fo");
        Assert.Equal(150m, fo.AveragePrice);
        Assert.Equal(16, fo.Stock);
        Assert.Equal(5, sum.RunesUsed);
        Assert.Equal((4 * 150m + 500m) / 5, sum.CostPerUse);
    }

    [Fact]
    public void Median_and_outlier()
    {
        Assert.Equal(20m, FmLedger.Median(new[] { 10m, 30m, 20m }));
        Assert.Equal(25m, FmLedger.Median(new[] { 10m, 20m, 30m, 40m }));
        Assert.Null(FmLedger.Median(Array.Empty<decimal>()));
        Assert.True(FmLedger.IsOutlier(130, 100)); Assert.False(FmLedger.IsOutlier(120, 100)); Assert.False(FmLedger.IsOutlier(5, null));
    }

    // ---- Prix manuels / carnet ----
    [Fact]
    public void Manual_price_is_per_server_and_zero_removes()
    {
        var b = new ManualPriceBook();
        b.Set(292, 1519, 120, new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
        var q = b.Get(292, 1519, new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc))!;
        Assert.Equal(120m, q.Unit); Assert.Equal("manuel", q.Source); Assert.Equal(24L * 3_600_000, q.AgeMs);
        Assert.Null(b.Get(351, 1519));
        b.Set(292, 1519, 0);
        Assert.Null(b.Get(292, 1519));
    }

    [Theory]
    [InlineData("[3,-4]", true, 3, -4)]
    [InlineData("(12; 7)", true, 12, 7)]
    [InlineData("1 2", true, 1, 2)]
    [InlineData("999,1", false, 0, 0)]
    [InlineData("abc", false, 0, 0)]
    public void Parses_coordinates(string text, bool ok, int x, int y)
    {
        Assert.Equal(ok, Notebook.TryParseCoordinates(text, out int px, out int py));
        if (ok) { Assert.Equal(x, px); Assert.Equal(y, py); }
    }

    [Fact]
    public void Json_roundtrip_and_corrupt_file_gives_empty_object()
    {
        string dir = Path.Combine(Path.GetTempPath(), "eco-" + Guid.NewGuid().ToString("N")); string f = Path.Combine(dir, "nb.json");
        try
        {
            var nb = new Notebook(); nb.Positions.Add(new Notebook.Position { Name = "Zaap", X = 1, Y = 2 }); nb.TrackedItems.Add(1519);
            EcoJson.Save(f, nb);
            var back = EcoJson.Load<Notebook>(f);
            Assert.Equal("Zaap", back.Positions[0].Name); Assert.Equal(1519L, back.TrackedItems[0]);
            File.WriteAllText(f, "{ pas du json");
            Assert.Empty(EcoJson.Load<Notebook>(f).Positions);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

public class SubscriptionTrackerTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(10, SubscriptionLevel.Ok)]
    [InlineData(6, SubscriptionLevel.Soon)]
    [InlineData(2, SubscriptionLevel.Critical)]
    [InlineData(-1, SubscriptionLevel.Expired)]
    public void Level_by_days_left(double days, SubscriptionLevel expected)
        => Assert.Equal(expected, SubscriptionTracker.Level(Now.AddDays(days), Now));

    [Fact] public void Unknown_when_missing() => Assert.Equal(SubscriptionLevel.Unknown, SubscriptionTracker.Level(null, Now));

    [Fact]
    public void Describes_remaining_time()
    {
        Assert.Equal("4 j 12 h", SubscriptionTracker.Describe(Now.AddDays(4.5), Now));
        Assert.Equal("5 h", SubscriptionTracker.Describe(Now.AddHours(5), Now));
        Assert.Equal("expiré", SubscriptionTracker.Describe(Now.AddMinutes(-1), Now));
    }

    [Fact]
    public void Alerts_once_per_threshold_and_resets_on_new_end_date()
    {
        var book = new SubscriptionBook();
        var e = SubscriptionTracker.Observe(book, "a@b.c", "Perso", Now.AddDays(6.5), Now);
        Assert.Equal(7, SubscriptionTracker.AlertDue(e, Now));
        Assert.Null(SubscriptionTracker.AlertDue(e, Now.AddHours(1)));                 // déjà signalé
        Assert.Equal(3, SubscriptionTracker.AlertDue(e, Now.AddDays(3.8)));            // 2,7 j restants
        Assert.Null(SubscriptionTracker.AlertDue(e, Now.AddDays(3.9)));
        Assert.Equal(0, SubscriptionTracker.AlertDue(e, Now.AddDays(7)));              // expiré
        // réabonnement : nouvelle date, tout repart
        SubscriptionTracker.Observe(book, "A@B.C", "Perso", Now.AddDays(37), Now.AddDays(7));
        Assert.Single(book.Entries);
        Assert.Empty(e.SentThresholds);
    }

    [Fact]
    public void Same_end_date_with_rounding_does_not_reset_sent_thresholds()
    {
        var book = new SubscriptionBook();
        var e = SubscriptionTracker.Observe(book, "x", "", Now.AddDays(2), Now);
        SubscriptionTracker.AlertDue(e, Now);
        SubscriptionTracker.Observe(book, "x", "", Now.AddDays(2).AddSeconds(30), Now.AddMinutes(5));
        Assert.NotEmpty(e.SentThresholds);
    }

    [Fact]
    public void Manual_date_is_flagged_and_replaced_by_a_server_value()
    {
        var book = new SubscriptionBook();
        var e = SubscriptionTracker.SetManual(book, "a", "P", Now.AddDays(20), Now);
        Assert.True(e.Manual);
        SubscriptionTracker.Observe(book, "a", "P", Now.AddDays(21), Now);
        Assert.False(e.Manual);
        Assert.Single(book.Entries);
    }

    [Fact]
    public void Alert_text_mentions_who_and_time()
    {
        Assert.Contains("expiré", SubscriptionTracker.AlertText("Perso", 0, Now, Now));
        Assert.Contains("Perso", SubscriptionTracker.AlertText("Perso", 3, Now.AddDays(2), Now));
    }
}

public class MarketDealsTests
{
    private static MarketQuote Q(long gid, long[] lots, long avg, long? age = 1000) => new(gid, "obj" + gid, lots, avg, age);

    [Fact]
    public void Finds_lots_well_below_average_and_picks_the_best_lot_size()
    {
        // moyenne 1000/unité. Lot x1 = 900 (10 %), lot x10 = 6000 (40 % ; gain 4000), lot x100 inconnu.
        var d = Assert.Single(MarketDeals.Find(new[] { Q(1, new long[] { 900, 6000, -1, -1 }, 1000) }, 20, 100));
        Assert.Equal(10, d.LotSize);
        Assert.Equal(600, d.UnitPrice);
        Assert.Equal(40, d.DiscountPercent, 3);
        Assert.Equal(4000, d.LotGain);
    }

    [Fact]
    public void Filters_by_discount_gain_and_age_and_sorts_by_gain()
    {
        var quotes = new[]
        {
            Q(1, new long[] { 700, -1, -1, -1 }, 1000),                 // 30 %, gain 300
            Q(2, new long[] { 100, -1, -1, -1 }, 120),                  // 17 % -> trop faible
            Q(3, new long[] { 5000, -1, -1, -1 }, 9000),                // 44 %, gain 4000
            Q(4, new long[] { 100, -1, -1, -1 }, 1000, age: 9_999_999), // trop ancien
            Q(5, new long[] { 100, -1, -1, -1 }, 0),                    // pas de moyenne
        };
        var r = MarketDeals.Find(quotes, 25, 200, maxAgeMs: 3_600_000);
        Assert.Equal(new long[] { 3, 1 }, r.Select(x => x.Gid).ToArray());
    }

    [Fact] public void Nothing_when_prices_are_at_or_above_average() => Assert.Empty(MarketDeals.Find(new[] { Q(1, new long[] { 1000, 11000, -1, -1 }, 1000) }, 1, 1));

    [Fact]
    public void Fractional_unit_price_keeps_exact_lot_figures_and_never_rounds_a_gain_upward()
    {
        // x10 = 1001 -> 100.1/unit vs average 120 : unit gain 19.9 shown as 19, lot gain exactly 1200 - 1001 = 199.
        var d = Assert.Single(MarketDeals.Find(new[] { Q(1, new long[] { -1, 1001, -1, -1 }, 120) }, 1, 1));
        Assert.Equal(100.1m, d.UnitPrice);
        Assert.Equal(19, d.GainPerUnit);
        Assert.Equal(199, d.LotGain);
        Assert.Equal(1001, d.LotCost);
    }

    [Fact]
    public void Lot_sizes_are_compared_per_unit_not_per_lot()
    {
        // x1 = 95 (5 %), x100 = 8000 (80/unit, 20 %) : the bigger lot is the better deal despite its larger price.
        var d = Assert.Single(MarketDeals.Find(new[] { Q(1, new long[] { 95, -1, 8000, -1 }, 100) }, 1, 1));
        Assert.Equal(100, d.LotSize);
        Assert.Equal(20, d.DiscountPercent, 3);
    }
}

public class PriceImportTests
{
    [Theory]
    [InlineData("12 500", 12500)]
    [InlineData("12.500", 12500)]
    [InlineData("12,5k", 12500)]
    [InlineData("1,2m", 1200000)]
    [InlineData("150", 150)]
    [InlineData("1 234 567", 1234567)]
    [InlineData("12,5", 12.5)]
    public void Parses_numbers(string text, double expected)
    {
        Assert.True(PriceImport.TryNumber(text, out var v));
        Assert.Equal((decimal)expected, v);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("12 abc")]
    public void Rejects_non_numbers(string text) => Assert.False(PriceImport.TryNumber(text, out _));

    [Fact]
    public void Parses_lines_with_various_separators_and_skips_junk()
    {
        var text = "Objet\tPrix\nLaine de Bouftou\t120\nSang de Bouftou : 45\nPeau de Bouftou;1 200\r\nAmulette du Bouftou 12 500\n\nx 3\nRune Fo 2k";
        var r = PriceImport.Parse(text);
        Assert.Equal(new[] { "Laine de Bouftou", "Sang de Bouftou", "Peau de Bouftou", "Amulette du Bouftou", "Rune Fo" }, r.Select(l => l.Name).ToArray());
        Assert.Equal(new decimal[] { 120, 45, 1200, 12500, 2000 }, r.Select(l => l.Price).ToArray());
    }
}

public class SeasonalCalendarTests
{
    [Theory]
    [InlineData(2026, 10, 14, Season.None)]
    [InlineData(2026, 10, 15, Season.Halloween)]
    [InlineData(2026, 11, 2, Season.Halloween)]
    [InlineData(2026, 11, 3, Season.None)]
    [InlineData(2026, 12, 15, Season.Christmas)]
    [InlineData(2027, 1, 2, Season.Christmas)]
    [InlineData(2027, 1, 3, Season.None)]
    [InlineData(2026, 7, 14, Season.None)]
    public void Season_by_date(int y, int m, int d, Season expected) => Assert.Equal(expected, SeasonalCalendar.Of(new DateTime(y, m, d)));
}

public class WealthTests
{
    [Fact]
    public void Groups_stacks_prices_known_items_and_ranks_by_total()
    {
        var items = new (long, string, long)[] { (1, "Bois", 100), (1, "Bois", 50), (2, "Épée", 1), (3, "Truc", 5), (4, "Vide", 0) };
        var r = Wealth.Compute(items, g => g switch { 1 => 10m, 2 => 5000m, _ => null }, top: 5);
        Assert.Equal(150 * 10m + 5000m, r.Total);
        Assert.Equal(2, r.Priced);
        Assert.Equal(1, r.Unpriced);          // « Truc » sans prix ; « Vide » (quantité 0) ignoré
        Assert.Equal(4, r.Stacks);
        Assert.Equal(new[] { "Épée", "Bois" }, r.Top.Select(t => t.Name).ToArray());
        Assert.Equal(150, r.Top[1].Quantity);
    }

    [Fact] public void Top_is_bounded_and_empty_input_is_zero()
    {
        Assert.Equal(0m, Wealth.Compute(Array.Empty<(long, string, long)>(), _ => 1m).Total);
        Assert.Single(Wealth.Compute(new (long, string, long)[] { (1, "a", 1), (2, "b", 1) }, _ => 1m, top: 1).Top);
    }
}

public class PriceAlertsTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    private static PriceAlert A(bool below = true, decimal target = 100) => new() { Gid = 1, Name = "Objet", Below = below, Target = target };

    [Theory]
    [InlineData(true, 100, 90, true)]
    [InlineData(true, 100, 100, true)]
    [InlineData(true, 100, 101, false)]
    [InlineData(false, 100, 120, true)]
    [InlineData(false, 100, 99, false)]
    [InlineData(true, 100, 0, false)]
    public void Hit_rules(bool below, double target, double price, bool expected)
        => Assert.Equal(expected, PriceAlerts.IsHit(A(below, (decimal)target), (decimal)price));

    [Fact]
    public void Anti_spam_cooldown_and_further_move()
    {
        var a = A();
        Assert.True(PriceAlerts.ShouldNotify(a, 90, Now));
        PriceAlerts.MarkNotified(a, 90, Now);
        Assert.False(PriceAlerts.ShouldNotify(a, 89, Now.AddHours(1)));      // pas assez bas
        Assert.True(PriceAlerts.ShouldNotify(a, 80, Now.AddHours(1)));       // -11 % depuis la dernière alerte
        Assert.True(PriceAlerts.ShouldNotify(a, 95, Now.AddHours(7)));       // délai écoulé
        Assert.False(PriceAlerts.ShouldNotify(a, 150, Now.AddHours(7)));     // plus sous la cible
    }

    [Fact]
    public void Only_fresh_market_observations_count()
    {
        Assert.True(PriceAlerts.IsFreshObservation("marché", 60_000));
        Assert.False(PriceAlerts.IsFreshObservation("marché", 4 * 3_600_000L));
        Assert.False(PriceAlerts.IsFreshObservation("manuel", 1000));
        Assert.False(PriceAlerts.IsFreshObservation("marché", null));
        Assert.Contains("Objet", PriceAlerts.Text("Perso", A(), 90));
    }
}

public class SetAdvisorTests
{
    // 10 = Chapeau (craft 100 / achat 500) ; 11 = Cape (craft 900 / achat 300) ; 12 = Anneau (aucun prix)
    private static CraftAdvice Advice(long gid)
    {
        var node = new CraftNode(gid, "n" + gid, 1, null, null, CraftDecision.Craft, null, false, "", new[]
        {
            new CraftNode(900 + gid, "ingr" + gid, 2, 5, null, CraftDecision.Buy, 5, false, "marché", Array.Empty<CraftNode>()),
        });
        return gid switch
        {
            10 => new CraftAdvice(node, 100, 500, 400, 80, null, false, Array.Empty<long>(), ""),
            11 => new CraftAdvice(node, 900, 300, -600, -200, null, false, Array.Empty<long>(), ""),
            _ => new CraftAdvice(node, null, null, null, null, null, false, new long[] { 3 }, ""),
        };
    }

    [Fact]
    public void Picks_cheaper_per_piece_totals_and_merges_shopping()
    {
        var r = SetAdvisor.Analyze(new long[] { 10, 11 }, Advice, g => "p" + g);
        Assert.Equal(new[] { SetVerdict.Craft, SetVerdict.Buy }, r.Items.Select(i => i.Verdict).ToArray());
        Assert.Equal(100 + 300m, r.TotalChosen);
        Assert.Equal(800m, r.TotalBuy);
        Assert.Equal(1000m, r.TotalCraft);
        Assert.Equal(0, r.Unknown);
        // chapeau fabriqué -> ingrédient 910 x2 ; cape achetée -> la cape elle-même
        Assert.Contains(r.Shopping, s => s.Gid == 910 && s.Quantity == 2);
        Assert.Contains(r.Shopping, s => s.Gid == 11 && s.Quantity == 1 && s.Cost == 300m);
    }

    [Fact]
    public void Unknown_piece_makes_the_total_unknown()
    {
        var r = SetAdvisor.Analyze(new long[] { 10, 12 }, Advice, g => "p" + g);
        Assert.Equal(1, r.Unknown);
        Assert.Null(r.TotalChosen);
        Assert.Null(r.TotalBuy);
    }

    [Fact] public void Duplicates_are_counted_once() => Assert.Equal(1, SetAdvisor.Analyze(new long[] { 10, 10 }, Advice, g => "p").Items.Count);
}

public class CharacterDeletionEncoderTests
{
    // Captures réelles (MITM 3.7.12.12) : id 788196229413, réponse « Dofus » (acceptée) / « dofus » (refusée).
    [Fact]
    public void Hash_et_corps_reproduisent_la_capture()
    {
        Assert.Equal("0d3d3de733481348a343ea3b98f08497", GivreClone.App.Services.CharacterDeletionEncoder.AnswerHash(788196229413, "Dofus"));
        Assert.Equal("98c79de6c5c4a38e3abad4dbd774a166", GivreClone.App.Services.CharacterDeletionEncoder.AnswerHash(788196229413, "dofus"));
        Assert.Equal("0A20" + Hex(System.Text.Encoding.ASCII.GetBytes("0d3d3de733481348a343ea3b98f08497")) + "10A582A0A1F816",
            Hex(GivreClone.App.Services.CharacterDeletionEncoder.DeleteBody(788196229413, "Dofus")));
        Assert.Equal("08A582A0A1F816", Convert.ToHexString(GivreClone.App.Services.CharacterDeletionEncoder.PrepareBody(788196229413)));
    }

    private static string Hex(byte[] b) => Convert.ToHexString(b);
}
