using GivreClone.App.Services.LotM;
using Xunit;

namespace GivreClone.Tests;

public class HdvCraftScannerTests
{
    private static CraftScanRecipe Recipe(long id = 1, string name = "Craft") =>
        new(id, name, 10, 2, new[] { new CraftScanIngredient(2, 3), new CraftScanIngredient(2, 2) });

    private static Dictionary<long, CraftPrice> Prices(long value = 100, long ingredient = 10) => new()
    {
        [1] = HdvCraftScanner.SelectPrice(value, 2500, 999),
        [2] = HdvCraftScanner.SelectPrice(0, long.MaxValue, ingredient),
    };

    [Fact]
    public void Uses_actual_quantities_including_duplicates_and_scales_output_and_cost()
    {
        var result = Assert.Single(HdvCraftScanner.Estimate(new[] { Recipe() }, Prices(), 4));
        Assert.Equal(200m, result.Cost);
        Assert.Equal(400m, result.Value);
        Assert.Equal(200m, result.Gain);
        Assert.Equal(100m, result.GainPercent);
        Assert.Empty(result.UnknownGids);
        Assert.Contains("marché local x1", result.Evidence);
        Assert.Contains("moyenne locale", result.Evidence);
        Assert.Contains("fraîcheur inconnue", result.Evidence);
        Assert.Contains("hors taxes/frais", result.Evidence);
    }

    [Fact]
    public void Market_provenance_keeps_actual_age_and_average_never_invents_freshness()
    {
        var market = HdvCraftScanner.SelectPrice(20, 1234, 15);
        Assert.Equal(20m, market.UnitPrice);
        Assert.Equal(CraftPriceSource.CachedMarketUnit, market.Source);
        Assert.Equal(TimeSpan.FromMilliseconds(1234), market.Age);
        var average = HdvCraftScanner.SelectPrice(-1, 1, 15);
        Assert.Equal(CraftPriceSource.CachedAverage, average.Source);
        Assert.Null(average.Age);
        Assert.Null(HdvCraftScanner.SelectPrice(20, long.MaxValue, 15).Age);
        Assert.False(HdvCraftScanner.SelectPrice(0, 1, -1).Known);
    }

    [Fact]
    public void Missing_ingredient_is_unknown_not_free_and_never_becomes_a_profit()
    {
        var prices = Prices();
        prices.Remove(2);
        var result = Assert.Single(HdvCraftScanner.Estimate(new[] { Recipe() }, prices, 1));
        Assert.Null(result.Cost);
        Assert.Null(result.Gain);
        Assert.Equal(new long[] { 2 }, result.UnknownGids);
        Assert.Contains("prix inconnu", result.Evidence);
        Assert.Empty(HdvCraftScanner.Profits(new[] { result }, 0, 0, 0));
    }

    [Fact]
    public void Missing_output_cannot_be_ranked_even_when_ingredients_are_known()
    {
        var prices = Prices();
        prices.Remove(1);
        var result = Assert.Single(HdvCraftScanner.Estimate(new[] { Recipe() }, prices, 1));
        Assert.Equal(50m, result.Cost);
        Assert.Null(result.Value);
        Assert.Equal(new long[] { 1 }, result.UnknownGids);
        Assert.Empty(HdvCraftScanner.Profits(new[] { result }, 0, 0, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_ingredient_quantities_are_not_silently_omitted(int quantity)
    {
        var recipe = Recipe() with { Ingredients = new[] { new CraftScanIngredient(2, quantity) } };
        var result = Assert.Single(HdvCraftScanner.Estimate(new[] { recipe }, Prices(), 1));
        Assert.Null(result.Cost);
        Assert.Contains("recette invalide", result.Evidence);
        Assert.Empty(HdvCraftScanner.Profits(new[] { result }, 0, 0, 0));
    }

    [Fact]
    public void Empty_recipe_is_not_free_crafting()
    {
        var recipe = Recipe() with { Ingredients = Array.Empty<CraftScanIngredient>() };
        Assert.Null(Assert.Single(HdvCraftScanner.Estimate(new[] { recipe }, Prices(), 1)).Cost);
    }

    [Fact]
    public void Filters_require_both_profit_thresholds_and_sort_matches_view_indices()
    {
        var a = new CraftScanEstimate(Recipe(1, "Alpha"), 100m, 200m, Array.Empty<long>(), "");
        var b = new CraftScanEstimate(Recipe(3, "Bravo"), 10m, 40m, Array.Empty<long>(), "");
        var loss = a with { Cost = 300m };
        Assert.Equal(new[] { a, b }, HdvCraftScanner.Profits(new[] { b, loss, a }, 0, 0, 0));
        Assert.Equal(new[] { b, a }, HdvCraftScanner.Profits(new[] { a, b }, 0, 0, 1));
        Assert.Equal(new[] { a, b }, HdvCraftScanner.Profits(new[] { b, a }, 0, 0, 2));
        Assert.Equal(new[] { a }, HdvCraftScanner.Profits(new[] { b, a }, 100, 100, 0));
        Assert.Empty(HdvCraftScanner.Profits(new[] { a }, 100, 101, 0));
    }

    [Fact]
    public void Long_price_products_do_not_wrap_into_false_profit()
    {
        var result = Assert.Single(HdvCraftScanner.Estimate(new[] { Recipe() }, Prices(long.MaxValue, long.MaxValue), 1000));
        Assert.Equal((decimal)long.MaxValue * 5000, result.Cost);
        Assert.True(result.Gain < 0);
        Assert.Empty(HdvCraftScanner.Profits(new[] { result }, 0, 0, 0));
    }

    [Fact]
    public void Cancellation_is_checked_before_enumeration_and_between_recipes()
    {
        using var cts = new CancellationTokenSource();
        IEnumerable<CraftScanRecipe> Input()
        {
            yield return Recipe();
            cts.Cancel();
            yield return Recipe(3);
        }
        Assert.Throws<OperationCanceledException>(() => HdvCraftScanner.Estimate(Input(), Prices(), 1, cts.Token));
        Assert.Throws<OperationCanceledException>(() => HdvCraftScanner.Estimate(Array.Empty<CraftScanRecipe>(), Prices(), 1, cts.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Invalid_scan_quantity_is_rejected(int quantity)
        => Assert.Throws<ArgumentOutOfRangeException>(() => HdvCraftScanner.Estimate(new[] { Recipe() }, Prices(), quantity));

    [Fact]
    public void Decompose_uses_cheaper_of_market_or_crafting_and_ignores_cycles()
    {
        // 5 coûte 100 au marché mais se fabrique 2 x gid 6 (10) = 20 ; 6 a un cycle vers 5.
        var final = new CraftScanRecipe(1, "Final", 10, 2, new[] { new CraftScanIngredient(5, 2) });
        var sub = new Dictionary<long, CraftScanRecipe>
        {
            [5] = new(5, "Intermédiaire", 5, 2, new[] { new CraftScanIngredient(6, 2) }),
            [6] = new(6, "Cyclique", 5, 2, new[] { new CraftScanIngredient(5, 1) }),
        };
        var prices = new Dictionary<long, CraftPrice>
        {
            [1] = HdvCraftScanner.SelectPrice(500, 0, 0),
            [5] = HdvCraftScanner.SelectPrice(100, 0, 0),
            [6] = HdvCraftScanner.SelectPrice(10, 0, 0),
        };
        Assert.Equal(200m, Assert.Single(HdvCraftScanner.Estimate(new[] { final }, prices, 1)).Cost);
        var d = Assert.Single(HdvCraftScanner.Estimate(new[] { final }, prices, 1, subRecipes: sub));
        Assert.Equal(40m, d.Cost);
        Assert.Contains("fabriqué 20", d.Evidence);
    }

    [Fact]
    public void Decompose_makes_unpriced_ingredient_known_when_craftable()
    {
        var final = new CraftScanRecipe(1, "F", 1, 1, new[] { new CraftScanIngredient(5, 1) });
        var sub = new Dictionary<long, CraftScanRecipe> { [5] = new(5, "I", 1, 1, new[] { new CraftScanIngredient(6, 3) }) };
        var prices = new Dictionary<long, CraftPrice> { [1] = HdvCraftScanner.SelectPrice(100, 0, 0), [6] = HdvCraftScanner.SelectPrice(10, 0, 0) };
        Assert.Null(Assert.Single(HdvCraftScanner.Estimate(new[] { final }, prices, 1)).Cost);
        var d = Assert.Single(HdvCraftScanner.Estimate(new[] { final }, prices, 1, subRecipes: sub));
        Assert.Equal(30m, d.Cost);
        Assert.Empty(d.UnknownGids);
    }

    [Fact]
    public void Sale_tax_reduces_value_not_cost()
    {
        var r = Assert.Single(HdvCraftScanner.Estimate(new[] { Recipe() }, Prices(), 1, saleTaxPercent: 2m));
        Assert.Equal(98m, r.Value);
        Assert.Equal(50m, r.Cost);
        Assert.Throws<ArgumentOutOfRangeException>(() => HdvCraftScanner.Estimate(new[] { Recipe() }, Prices(), 1, saleTaxPercent: 100m));
    }
}
