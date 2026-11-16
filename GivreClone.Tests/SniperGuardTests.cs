using System.Linq;
using GivreClone.App.Services.Eco;
using Xunit;

namespace GivreClone.Tests;

public class SniperGuardTests
{
    private static SnipeLot L(decimal p, long u = 10) => new(p, u);
    private static readonly SniperGuardSettings S = new();

    private static SnipeLot[] Market(params decimal[] prices) => prices.Select(p => L(p)).ToArray();

    [Fact]
    public void A_clear_discount_on_a_healthy_market_is_allowed()
    {
        var cand = L(60);
        var lots = Market(100, 105, 98, 102, 110).Append(cand).ToArray();
        var v = SniperGuard.Evaluate(1, cand, lots, 0, 0, S);
        Assert.True(v.Allowed, string.Join(" | ", v.Reasons));
        Assert.Equal(102m, v.ReferencePrice);
    }

    [Fact]
    public void A_thin_market_is_refused()
    {
        var cand = L(10);
        var v = SniperGuard.Evaluate(1, cand, new[] { cand, L(100), L(110) }, 0, 0, S);
        Assert.False(v.Allowed);
        Assert.Contains(v.Reasons, r => r.StartsWith("marché trop mince"));
    }

    [Fact]
    public void A_mass_item_like_water_is_refused_whatever_the_discount()
    {
        var cand = L(1, 1000);
        var lots = new[] { L(5, 8000), L(5, 9000), L(6, 5000), L(5, 4000), cand };
        var v = SniperGuard.Evaluate(1, cand, lots, 0, 0, S);
        Assert.False(v.Allowed);
        Assert.Contains(v.Reasons, r => r.StartsWith("objet de masse"));
    }

    [Fact]
    public void A_manipulated_reference_is_refused()
    {
        // médiane basse mais quelques énormes lots très chers : la moyenne pondérée s'envole
        var cand = L(50);
        var lots = new[] { L(100, 1), L(100, 1), L(100, 1), L(100, 1), L(900, 500), cand };
        var v = SniperGuard.Evaluate(1, cand, lots, 0, 0, S);
        Assert.False(v.Allowed);
        Assert.Contains(v.Reasons, r => r.StartsWith("prix de référence instable"));
    }

    [Fact]
    public void A_seller_wall_a_small_margin_and_spend_caps_are_refused()
    {
        var cand = L(95, 500);
        var lots = Market(100, 105, 98, 102, 110).Append(cand).ToArray();
        var v = SniperGuard.Evaluate(1, cand, lots, 9_999_000, 6, S);
        Assert.False(v.Allowed);
        Assert.Contains(v.Reasons, r => r.StartsWith("rabais insuffisant"));
        Assert.Contains(v.Reasons, r => r.StartsWith("plafond de dépense par objet"));
        Assert.Contains(v.Reasons, r => r.StartsWith("trop d'achats"));
    }

    [Fact]
    public void Blocked_items_never_pass()
    {
        var cand = L(60);
        var lots = Market(100, 105, 98, 102, 110).Append(cand).ToArray();
        var set = new SniperGuardSettings(); set.BlockedGids.Add(7);
        Assert.False(SniperGuard.Evaluate(7, cand, lots, 0, 0, set).Allowed);
    }
}
