using GivreClone.App.Services.Eco;
using GivreClone.App.Services.LotM;
using Xunit;

namespace GivreClone.Tests;

public class ResaleFeesTests
{
    [Theory]
    [InlineData(1000, 2, 20)]
    [InlineData(1025, 2, 21)]
    [InlineData(999, 0, 0)]
    public void Tax_is_the_rounded_share_of_the_sale_price(decimal price, decimal pct, decimal tax)
        => Assert.Equal(tax, ResaleFees.Tax(price, pct));

    [Fact]
    public void Known_rate_shows_the_fee_amount_and_the_net_gain_consistent_with_the_advisor()
    {
        CraftScanRecipe? recipe(long g) => g == 1 ? new(1, "Épée", 50, 1, new[] { new CraftScanIngredient(3, 1) }) : null;
        PriceQuote? price(long g) => g switch { 1 => new(1000m, 1000, "marché"), 3 => new(60m, 1000, "marché"), _ => null };
        var a = CraftAdvisor.Analyze(1, 1, recipe, price, g => "#" + g, salePercent: 10m);
        Assert.Equal(1000m * 0.9m - 60m, a.ResaleProfit);
        string text = ResaleFees.Describe(a.BuyCost, a.ResaleProfit, 10m);
        Assert.Contains("100 k", text);
        Assert.Contains("+840", text);
    }

    [Fact]
    public void Unknown_rate_says_the_tax_is_not_included_instead_of_claiming_zero()
        => Assert.Contains("taxe non incluse", ResaleFees.Describe(1000m, 890m, 0m));

    [Fact]
    public void Missing_prices_produce_no_resale_line()
        => Assert.Equal("", ResaleFees.Describe(null, null, 2m));
}
