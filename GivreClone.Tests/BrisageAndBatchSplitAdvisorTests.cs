using System.Collections.Generic;
using GivreClone.App.Services.Eco;
using Xunit;

namespace GivreClone.Tests;

public class BrisageAndBatchSplitAdvisorTestsBatchSplit
{
    // Prix des lots [x1, x10, x100, x1000], -1 = inconnu.
    private static MarketQuote Q(long gid, long[] lots, long? age = 0) => new(gid, "item" + gid, lots, 0, age);

    [Fact]
    public void Profitable_100_to_10_split_is_found_with_exact_amounts()
    {
        var d = Assert.Single(MarketDeals.FindBatchSplits(new[] { Q(1, new long[] { -1, 1000, 8000, -1 }) }, 5, 100));
        Assert.Equal(100, d.BuyLotSize);
        Assert.Equal(10, d.SellLotSize);
        Assert.Equal(10, d.SplitCount);
        Assert.Equal(8000, d.BuyLotCost);
        Assert.Equal(1000, d.SellLotPrice);
        Assert.Equal(10_000, d.GrossRevenue);
        Assert.Equal(200, d.TaxCost);
        Assert.Equal(1800, d.NetProfit);
        Assert.Equal(22.5, d.NetMarginPercent, 6);
    }

    [Fact]
    public void Two_percent_tax_eats_a_fake_margin()
    {
        // Brut 1010 > coût 1000, mais taxe ceil(20,2) = 21 => net -11.
        Assert.Empty(MarketDeals.FindBatchSplits(new[] { Q(1, new long[] { -1, 101, 1000, -1 }) }, 0, 0));
        // Brut 1000 pour un coût de 980 : taxe 20 => net exactement 0, pas un gain.
        Assert.Empty(MarketDeals.FindBatchSplits(new[] { Q(2, new long[] { 100, 980, -1, -1 }) }, 0, 0));
    }

    [Fact]
    public void Tax_is_rounded_up_to_the_next_kama()
    {
        var d = Assert.Single(MarketDeals.FindBatchSplits(new[] { Q(1, new long[] { -1, 101, 800, -1 }) }, 0, 1));
        Assert.Equal(1010, d.GrossRevenue);
        Assert.Equal(21, d.TaxCost);
        Assert.Equal(189, d.NetProfit);
    }

    [Fact]
    public void Custom_tax_rate_changes_the_verdict()
    {
        var quotes = new[] { Q(1, new long[] { -1, 1000, 9900, -1 }) };
        Assert.Empty(MarketDeals.FindBatchSplits(quotes, 0, 1));
        Assert.Single(MarketDeals.FindBatchSplits(quotes, 0, 1, taxRate: 0m));
    }

    [Fact]
    public void Stale_or_unaged_quotes_are_filtered_only_when_a_max_age_is_given()
    {
        var lots = new long[] { -1, 1000, 8000, -1 };
        var quotes = new[] { Q(1, lots, 10_000), Q(2, lots, 7_200_000), Q(3, lots, null) };
        var kept = MarketDeals.FindBatchSplits(quotes, 5, 100, maxAgeMs: 3_600_000);
        Assert.Equal(1, Assert.Single(kept).Gid);
        Assert.Equal(3, MarketDeals.FindBatchSplits(quotes, 5, 100).Count);
    }

    [Fact]
    public void Unknown_prices_never_make_a_split()
    {
        Assert.Empty(MarketDeals.FindBatchSplits(new[] { Q(1, new long[] { -1, -1, 8000, -1 }) }, 0, 0));
        Assert.Empty(MarketDeals.FindBatchSplits(new[] { Q(2, new long[] { 5000, -1, -1, -1 }) }, 0, 0)); // seul le plus petit lot
        Assert.Empty(MarketDeals.FindBatchSplits(new[] { Q(3, new long[] { 0, 1000, 0, -1 }) }, 0, 0));
        Assert.Empty(MarketDeals.FindBatchSplits(new[] { Q(4, new long[] { 100, 1000 }) }, 0, 0)); // liste de prix courte
    }

    [Fact]
    public void Best_profit_split_is_chosen_per_item_and_thresholds_apply()
    {
        // 100 -> 10 x lot 10 : 5000 - 100 - 4000 = +900. 1000 -> 10 x lot 100 : 40000 - 800 - 10000 = +29200.
        // 1000 -> 100 x lot 10 : 50000 - 1000 - 10000 = +39000 (le meilleur).
        var q = Q(1, new long[] { -1, 500, 4000, 10_000 }, 0);
        var d = Assert.Single(MarketDeals.FindBatchSplits(new[] { q }, 0, 0));
        Assert.Equal(1000, d.BuyLotSize);
        Assert.Equal(10, d.SellLotSize);
        Assert.Equal(100, d.SplitCount);
        Assert.Equal(39_000, d.NetProfit);
        Assert.Empty(MarketDeals.FindBatchSplits(new[] { q }, 0, 40_000));
        Assert.Empty(MarketDeals.FindBatchSplits(new[] { q }, 1000, 0)); // marge 390 % < 1000 %
    }

    [Fact]
    public void Results_are_ordered_by_net_profit_then_margin()
    {
        var quotes = new[]
        {
            Q(1, new long[] { -1, 1000, 8000, -1 }),   // +1800, 22,5 %
            Q(2, new long[] { -1, 1000, 6000, -1 }),   // +3800, 63,3 %
            Q(3, new long[] { -1, 100, 800, -1 }),     // +180, 22,5 %
        };
        var r = MarketDeals.FindBatchSplits(quotes, 0, 1);
        Assert.Equal(new long[] { 2, 1, 3 }, r.Select(x => x.Gid).ToArray());
    }

    [Fact]
    public void Ties_on_profit_prefer_the_higher_margin()
    {
        var quotes = new[]
        {
            Q(1, new long[] { -1, 1000, 8000, -1 }),  // 10000 - 200 - 8000 = +1800, 22,5 %
            Q(2, new long[] { 300, 1140, -1, -1 }),   // 3000 - 60 - 1140 = +1800, 157,9 %
        };
        var r = MarketDeals.FindBatchSplits(quotes, 0, 1);
        Assert.Equal(new long[] { 1800, 1800 }, r.Select(x => x.NetProfit).ToArray());
        Assert.Equal(new long[] { 2, 1 }, r.Select(x => x.Gid).ToArray());
    }
}

public class BrisageAndBatchSplitAdvisorTestsFocus
{
    // Niveau 200, coeff 100 : Y_i = Value * UnitWeight. Prix par poids en kamas.
    private static BrisageStatLine Line(string key, int value, double weight, decimal? perWeight) => new(key, value, weight, perWeight);

    [Fact]
    public void Focus_beats_no_focus_when_one_line_is_much_more_valuable()
    {
        var eval = BrisageFocusAdvisor.Evaluate(200, new[] { Line("A", 10, 1, 10m), Line("B", 10, 1, 1m) }, craftCost: 100);
        Assert.True(eval.KnownComplete);
        Assert.Equal(20.0, eval.NoFocusWeight, 6);
        Assert.Equal(107.8m, eval.NoFocusNetKama!.Value, 6);   // (10*10 + 10*1) * 0.98
        Assert.Equal(BrisageChoice.Focus, eval.Choice);
        Assert.Equal("A", eval.FocusStatKey);
        Assert.Equal(147m, eval.BestNetKama!.Value, 6);        // (10 + 0,5*10) * 10 * 0,98
        var b = eval.Candidates.Single(c => c.StatKey == "B");
        Assert.Equal(15.0, b.TotalWeight, 6);
        Assert.Equal(14.7m, b.NetKama!.Value, 6);
        Assert.Equal(47m, eval.NetProfit!.Value, 6);
    }

    [Fact]
    public void No_focus_wins_when_stats_have_equal_high_value()
    {
        var eval = BrisageFocusAdvisor.Evaluate(200, new[] { Line("A", 50, 1, 10m), Line("B", 50, 1, 10m), Line("C", 50, 1, 10m) }, craftCost: 1000);
        Assert.Equal(BrisageChoice.NoFocus, eval.Choice);
        Assert.Null(eval.FocusStatKey);
        Assert.Equal(1470m, eval.BestNetKama!.Value, 6);       // 150 * 10 * 0,98
        Assert.All(eval.Candidates, c => Assert.True(c.NetKama < eval.NoFocusNetKama));
    }

    [Fact]
    public void Item_level_and_server_coefficient_scale_the_yield_linearly()
    {
        var lines = new[] { Line("A", 10, 2, 1m) };
        var half = BrisageFocusAdvisor.Evaluate(100, lines, 0, coeffPercent: 50);
        Assert.Equal(10 * 2 * 0.5 * 0.5, half.NoFocusWeight, 6);
        Assert.Equal(5m * 0.98m, half.NoFocusNetKama!.Value, 6);
        // Un seul poste : Focus = sans Focus, donc pas de Focus conseillé.
        Assert.Equal(BrisageChoice.NoFocus, half.Choice);
    }

    [Fact]
    public void Choice_does_not_depend_on_coefficient_but_break_even_stays_at_100_percent_baseline()
    {
        var lines = new[] { Line("A", 10, 1, 10m), Line("B", 10, 1, 1m) };
        var at100 = BrisageFocusAdvisor.Evaluate(200, lines, 100);
        var at300 = BrisageFocusAdvisor.Evaluate(200, lines, 100, coeffPercent: 300);
        Assert.Equal(at100.Choice, at300.Choice);
        Assert.Equal(at100.BreakEvenCoeffPercent!.Value, at300.BreakEvenCoeffPercent!.Value, 6);
        Assert.Equal(441m, at300.BestNetKama!.Value, 6);
    }

    [Fact]
    public void Break_even_at_or_under_60_percent_is_an_anchor()
    {
        // Valeur nette sans Focus = 200 * 0,98 = 196 ; craft 100 => 51,02 %.
        var eval = BrisageFocusAdvisor.Evaluate(200, new[] { Line("A", 100, 1, 1m), Line("B", 100, 1, 1m) }, craftCost: 100);
        Assert.Equal(100.0 / 196.0 * 100.0, eval.BreakEvenCoeffPercent!.Value, 6);
        Assert.Equal(BrisageClass.Anchor, eval.Classification);
    }

    [Theory]
    [InlineData(60, BrisageClass.Anchor)]
    [InlineData(60.01, BrisageClass.Balanced)]
    [InlineData(150, BrisageClass.Balanced)]
    [InlineData(150.01, BrisageClass.Speculative)]
    public void Classification_thresholds(double be, BrisageClass expected) => Assert.Equal(expected, BrisageFocusAdvisor.Classify(be));

    [Fact]
    public void Break_even_classifies_balanced_and_speculative_from_craft_cost()
    {
        var lines = new[] { Line("A", 100, 1, 1m), Line("B", 100, 1, 1m) }; // valeur nette 196
        Assert.Equal(BrisageClass.Balanced, BrisageFocusAdvisor.Evaluate(200, lines, 250).Classification);   // 127,6 %
        Assert.Equal(BrisageClass.Speculative, BrisageFocusAdvisor.Evaluate(200, lines, 400).Classification); // 204 %
    }

    [Fact]
    public void Missing_rune_price_is_reported_not_invented_as_zero()
    {
        var eval = BrisageFocusAdvisor.Evaluate(200, new[] { Line("A", 10, 1, 10m), Line("B", 10, 1, null) }, craftCost: 50);
        Assert.False(eval.KnownComplete);
        Assert.Equal(new[] { "B" }, eval.MissingPriceStats);
        Assert.Null(eval.NoFocusNetKama);
        Assert.Equal(BrisageChoice.Undetermined, eval.Choice);
        Assert.Null(eval.BestNetKama);
        Assert.Null(eval.NetProfit);
        Assert.Null(eval.BreakEvenCoeffPercent);
        Assert.Equal(BrisageClass.Unknown, eval.Classification);
        // La valeur Focus sur A est calculable ; celle sur B (prix inconnu) ne l'est pas.
        Assert.NotNull(eval.Candidates.Single(c => c.StatKey == "A").NetKama);
        Assert.Null(eval.Candidates.Single(c => c.StatKey == "B").NetKama);
    }

    [Fact]
    public void Zero_price_counts_as_unknown_and_zero_weight_lines_are_ignored()
    {
        var zeroPrice = BrisageFocusAdvisor.Evaluate(200, new[] { Line("A", 10, 1, 0m) }, 10);
        Assert.False(zeroPrice.KnownComplete);
        // Ligne sans valeur ou sans poids : n'entre pas dans le calcul, donc n'exige aucun prix.
        var ignored = BrisageFocusAdvisor.Evaluate(200, new[] { Line("A", 10, 1, 1m), Line("Z", 0, 1, null), Line("W", 5, 0, null) }, 1);
        Assert.True(ignored.KnownComplete);
        Assert.Equal(9.8m, ignored.NoFocusNetKama!.Value, 6);
    }

    [Fact]
    public void Nothing_to_break_is_a_known_zero_with_no_break_even()
    {
        var eval = BrisageFocusAdvisor.Evaluate(200, System.Array.Empty<BrisageStatLine>(), 100);
        Assert.True(eval.KnownComplete);
        Assert.Equal(0m, eval.BestNetKama);
        Assert.Null(eval.BreakEvenCoeffPercent);
        Assert.Equal(BrisageClass.Unknown, eval.Classification);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void Item_level_must_be_in_1_200(int level)
        => Assert.Throws<System.ArgumentOutOfRangeException>(() => BrisageFocusAdvisor.Evaluate(level, System.Array.Empty<BrisageStatLine>(), 0));

    [Fact]
    public void FromRune_uses_the_catalog_base_amount_and_leaves_unknowns_null()
    {
        // Force : Ba = 1 point => 10 kamas / (1 x 1). Vitalité : Ba = 5 points de 0,2 => 10 / (5 x 0,2) = 10.
        Assert.Equal(10m, BrisageStatLine.FromRune(118, 30, 1, 10m).RuneValuePerWeight);
        var vi = BrisageStatLine.FromRune(125, 200, 0.2, 10m);
        Assert.Equal("Vitalité", vi.StatKey);
        Assert.Equal(10m, vi.RuneValuePerWeight!.Value, 6);
        Assert.Null(BrisageStatLine.FromRune(118, 30, 1, null).RuneValuePerWeight);
        Assert.Null(BrisageStatLine.FromRune(118, 30, 1, 0m).RuneValuePerWeight);
        Assert.Null(BrisageStatLine.FromRune(118, 30, 0, 10m).RuneValuePerWeight);
        Assert.Null(BrisageStatLine.FromRune(999_999, 30, 1, 10m).RuneValuePerWeight);
    }

    [Fact]
    public void Summarize_labels_focus_nofocus_break_even_and_class()
    {
        var focus = BrisageFocusAdvisor.Summarize(BrisageFocusAdvisor.Evaluate(200, new[] { Line("Force", 10, 1, 10m), Line("Sagesse", 10, 1, 1m) }, 100), costKnown: true);
        Assert.Equal("Focus recommandé : Force", focus.Verdict);
        Assert.Equal("Seuil C_be 68 %", focus.BreakEven);
        Assert.Equal("Équilibré", focus.ClassLabel);
        Assert.True(focus.Complete);

        var anchor = BrisageFocusAdvisor.Summarize(BrisageFocusAdvisor.Evaluate(200, new[] { Line("A", 50, 1, 10m), Line("B", 50, 1, 10m) }, 50), true);
        Assert.Equal("Sans focus", anchor.Verdict);
        Assert.Equal("Ancre", anchor.ClassLabel);

        var spec = BrisageFocusAdvisor.Summarize(BrisageFocusAdvisor.Evaluate(200, new[] { Line("A", 100, 1, 1m), Line("B", 100, 1, 1m) }, 400), true);
        Assert.Equal("Spéculatif", spec.ClassLabel);
    }

    [Fact]
    public void Summarize_without_cost_keeps_the_choice_but_shows_no_threshold_or_class()
    {
        var s = BrisageFocusAdvisor.Summarize(BrisageFocusAdvisor.Evaluate(200, new[] { Line("Force", 10, 1, 10m), Line("Sagesse", 10, 1, 1m) }, 0), costKnown: false);
        Assert.Equal("Focus recommandé : Force", s.Verdict);
        Assert.DoesNotContain("%", s.BreakEven);
        Assert.Equal("—", s.ClassLabel);
        Assert.Equal("", s.Profit);
    }

    [Fact]
    public void Summarize_reports_missing_rune_price_instead_of_a_verdict()
    {
        var s = BrisageFocusAdvisor.Summarize(BrisageFocusAdvisor.Evaluate(200, new[] { Line("Force", 10, 1, 10m), Line("Sagesse", 10, 1, null) }, 100), true);
        Assert.Equal("Prix rune manquant : Sagesse", s.Verdict);
        Assert.False(s.Complete);
        Assert.Equal("—", s.ClassLabel);
        Assert.DoesNotContain("%", s.BreakEven);
        Assert.Equal("", s.Profit);
    }

    [Fact]
    public void BuildLines_uses_game_weights_and_catalog_runes_and_keeps_unknown_prices_null()
    {
        var rates = new Dictionary<int, decimal> { [118] = 1m, [125] = 0.2m, [124] = 3m, [999_998] = 5m };
        var asked = new List<string>();
        var lines = BrisageFocusAdvisor.BuildLines(
            new[] { (118, 30), (125, 200), (124, 0), (118, 99), (999_998, 4), (111, 1), (126, 10) }, rates,
            name => { asked.Add(name); return name == "Rune Fo" ? 10m : name == "Rune Vi" ? 10m : null; });
        // Sagesse à 0 point, Force en double, PA/Intelligence sans poids : ignorés. L'effet 999998 a un poids mais aucune rune au catalogue.
        Assert.Equal(new[] { "Force", "Vitalité", "effet 999998" }, lines.Select(l => l.StatKey).ToArray());
        Assert.Equal(10m, lines[0].RuneValuePerWeight);
        Assert.Equal(10m, lines[1].RuneValuePerWeight!.Value, 6);
        Assert.Null(lines[2].RuneValuePerWeight);
        Assert.Equal(new[] { "Rune Fo", "Rune Vi" }, asked);
    }
}
