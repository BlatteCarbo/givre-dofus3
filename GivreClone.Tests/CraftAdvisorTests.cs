using GivreClone.App.Services.Eco;
using GivreClone.App.Services.LotM;
using Xunit;

namespace GivreClone.Tests;

public class CraftAdvisorTests
{
    // 1 = Épée (2 x Planche[2] + 1 x Fer[3]) ; Planche[2] = 3 x Bois[4] ; Bois et Fer n'ont pas de recette.
    private static CraftScanRecipe? Recipe(long g) => g switch
    {
        1 => new(1, "Épée", 50, 1, new[] { new CraftScanIngredient(2, 2), new CraftScanIngredient(3, 1) }),
        2 => new(2, "Planche", 10, 1, new[] { new CraftScanIngredient(4, 3) }),
        _ => null,
    };

    private static string Name(long g) => g switch { 1 => "Épée", 2 => "Planche", 3 => "Fer", 4 => "Bois", _ => "#" + g };

    private static Func<long, PriceQuote?> Prices(decimal sword = 1000, decimal plank = 100, decimal iron = 50, decimal wood = 10, long? age = 1000) => g => g switch
    {
        1 => sword > 0 ? new(sword, age, "marché") : null,
        2 => plank > 0 ? new(plank, age, "marché") : null,
        3 => iron > 0 ? new(iron, age, "marché") : null,
        4 => wood > 0 ? new(wood, age, "moyenne") : null,
        _ => null,
    };

    [Fact]
    public void Intermediate_is_crafted_when_cheaper_than_buying_and_total_matches()
    {
        // Planche : acheter 100, fabriquer 3 x 10 = 30 -> fabriquer. Épée : 2 x 30 + 50 = 110 contre 1000 à l'HDV.
        var a = CraftAdvisor.Analyze(1, 1, Recipe, Prices(), Name);
        Assert.Equal(110m, a.CraftCost);
        Assert.Equal(1000m, a.BuyCost);
        Assert.Equal(890m, a.Saving);
        Assert.Contains("Fabriquer", a.Recommendation);
        var plank = a.Root.Children.First(c => c.Gid == 2);
        Assert.Equal(CraftDecision.Craft, plank.Decision);
        Assert.Equal(30m, plank.UnitCost);
    }

    [Fact]
    public void Force_buy_overrides_the_cheaper_craft()
    {
        var a = CraftAdvisor.Analyze(1, 1, Recipe, Prices(), Name, new Dictionary<long, CraftForce> { [2] = CraftForce.Buy });
        Assert.Equal(250m, a.CraftCost);   // 2 x 100 + 50
        Assert.Equal(CraftDecision.Buy, a.Root.Children.First(c => c.Gid == 2).Decision);
    }

    [Fact]
    public void Buy_is_recommended_when_crafting_costs_more()
    {
        var a = CraftAdvisor.Analyze(1, 1, Recipe, Prices(sword: 100), Name);
        Assert.Contains("Acheter", a.Recommendation);
        Assert.True(a.Saving < 0);
    }

    [Fact]
    public void Missing_price_makes_the_advice_incomplete_and_is_listed()
    {
        var a = CraftAdvisor.Analyze(1, 1, Recipe, Prices(iron: 0), Name);
        Assert.Null(a.CraftCost);
        Assert.Contains(3L, a.MissingPrices);
        Assert.Contains("incomplètes", a.Recommendation);
    }

    [Fact]
    public void Stale_prices_are_flagged_only_for_bought_leaves()
    {
        var a = CraftAdvisor.Analyze(1, 1, Recipe, Prices(age: CraftAdvisor.DefaultStaleAfterMs + 1), Name);
        Assert.True(a.AnyStale);
    }

    [Fact]
    public void A_saving_built_on_stale_prices_is_announced_as_not_guaranteed()
    {
        Assert.Contains("Non garanti", CraftAdvisor.Analyze(1, 1, Recipe, Prices(age: CraftAdvisor.DefaultStaleAfterMs + 1), Name).Recommendation);
        Assert.DoesNotContain("Non garanti", CraftAdvisor.Analyze(1, 1, Recipe, Prices(), Name).Recommendation);
    }

    [Fact]
    public void Unknown_result_price_gives_no_saving_and_no_resale_figure()
    {
        var a = CraftAdvisor.Analyze(1, 1, Recipe, Prices(sword: 0), Name);
        Assert.Null(a.Saving);
        Assert.Null(a.ResaleProfit);
        Assert.Contains("impossible de comparer", a.Recommendation);
    }

    [Fact]
    public void Quantity_scales_and_shopping_list_groups_leaves()
    {
        var a = CraftAdvisor.Analyze(1, 3, Recipe, Prices(), Name);
        Assert.Equal(330m, a.CraftCost);
        var list = CraftAdvisor.ShoppingList(a.Root);
        Assert.Equal(18L, list.First(x => x.Gid == 4).Quantity);   // 3 x 2 x 3 bois
        Assert.Equal(3L, list.First(x => x.Gid == 3).Quantity);
    }

    [Fact]
    public void Resale_profit_deducts_the_sale_tax()
    {
        var a = CraftAdvisor.Analyze(1, 1, Recipe, Prices(), Name, salePercent: 10m);
        Assert.Equal(1000m * 0.9m - 110m, a.ResaleProfit);
    }

    [Fact]
    public void Cycles_do_not_loop()
    {
        CraftScanRecipe? Loop(long g) => g == 1 ? new(1, "A", 1, 1, new[] { new CraftScanIngredient(2, 1) }) : g == 2 ? new(2, "B", 1, 1, new[] { new CraftScanIngredient(1, 1) }) : null;
        var a = CraftAdvisor.Analyze(1, 1, Loop, g => new PriceQuote(10, null, "manuel"), g => "#" + g);
        Assert.NotNull(a.Root);
    }

    [Fact]
    public void Unknown_prices_keep_the_tree_so_missing_ones_can_be_entered()
    {
        var a = CraftAdvisor.Analyze(1, 1, Recipe, g => null, Name);
        Assert.Null(a.CraftCost);
        Assert.True(a.Root.Children.Count > 0);                 // le noeud racine garde ses ingrédients
        Assert.Contains(3L, a.MissingPrices);
    }
}
