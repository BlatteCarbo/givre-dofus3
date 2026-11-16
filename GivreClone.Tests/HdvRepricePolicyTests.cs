using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvRepricePolicyTests
{
    [Theory]
    [InlineData(40, 38, 40, -1)]
    [InlineData(400, 190, 400, -1)]
    [InlineData(500, 190, 400, 400)]
    [InlineData(160, 140, 1, 139)]
    [InlineData(140, 140, 1, 139)]
    public void Undercut_respects_minimum(long price, long market, long minimum, long expected) =>
        Assert.Equal(expected, HdvRepricePolicy.Target(price, market, minimum, 1, 100, false));

    [Fact]
    public void Own_cheapest_listing_is_not_undercut()
    {
        Assert.Equal(-1, HdvRepricePolicy.Target(38, 38, 1, 1, 100, true));
        Assert.Equal(38, HdvRepricePolicy.Target(40, 38, 1, 1, 100, true));
    }

    [Fact]
    public void Unknown_ignored_and_failed_quotes_do_not_trigger_a_sale()
    {
        Assert.Equal(-1, HdvRepricePolicy.Target(40, 0, 1, 1, 100, false));
        Assert.Equal(-1, HdvRepricePolicy.Target(40, 38, 1, 1, 100, false, ignored: true));
        Assert.Equal(-1, HdvRepricePolicy.Target(40, 38, 1, 1, 100, false, fetchError: true));
    }

    [Theory]
    [InlineData(1_000, null, 300, true)]
    [InlineData(300_999, 1_000L, 300, false)]
    [InlineData(301_000, 1_000L, 300, true)]
    [InlineData(1_001, 1_000L, 0, true)]
    public void Same_lot_is_not_repriced_again_before_its_cooldown(long now, long? last, int cooldownSec, bool allowed) =>
        Assert.Equal(allowed, HdvRepricePolicy.CooldownElapsed(now, last, cooldownSec));

    [Fact]
    public void Relative_floor_caps_large_undercuts() =>
        Assert.Equal(90, HdvRepricePolicy.Target(150, 100, 1, 50, 10, false));
}
