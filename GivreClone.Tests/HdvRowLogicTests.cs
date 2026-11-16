using System;
using System.Globalization;
using GivreClone.App.ViewModels;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Règles d'affichage des lignes HDV (MZP23). Culture figée : les séparateurs de milliers dépendent de la culture.</summary>
public class HdvRowLogicTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture, _ui = CultureInfo.CurrentUICulture;

    public HdvRowLogicTests()
    {
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = new CultureInfo("en-US");
    }

    // Restaure la culture du thread : sinon elle fuit vers les tests suivants exécutés sur le même thread.
    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _ui;
    }

    [Fact]
    public void Kamas_use_thousands_separator_and_zero_is_none_for_prices()
    {
        Assert.Equal("1,234,567", HdvRowLogic.Kamas(1234567));
        Assert.Equal("—", HdvRowLogic.KamasOrNone(0));
        Assert.Equal("5", HdvRowLogic.KamasOrNone(5));
    }

    [Theory]
    [InlineData(1200L, "+1,200")]
    [InlineData(-300L, "-300")]
    [InlineData(0L, "0")]
    public void Gain_is_signed(long gain, string expected) => Assert.Equal(expected, HdvRowLogic.SignedKamas(gain));

    [Theory]
    [InlineData(100L, 112L, "+12.0 %")]
    [InlineData(200L, 150L, "-25.0 %")]
    [InlineData(100L, 100L, "0.0 %")]
    [InlineData(0L, 500L, "—")]
    public void Gain_percent_is_relative_to_cost(long cost, long value, string expected)
        => Assert.Equal(expected, HdvRowLogic.GainPct(cost, value));

    [Theory]
    [InlineData(10, 4, 0, HdvRowLogic.Coverage.Missing)]
    [InlineData(10, 4, 6, HdvRowLogic.Coverage.Exact)]
    [InlineData(10, 4, 9, HdvRowLogic.Coverage.Surplus)]
    [InlineData(0, 0, 0, HdvRowLogic.Coverage.Exact)]
    public void Coverage_compares_stock_plus_purchases_to_need(int needed, int owned, int bought, HdvRowLogic.Coverage expected)
        => Assert.Equal(expected, HdvRowLogic.CoverageOf(needed, owned, bought));

    [Fact]
    public void ToBuy_is_never_negative_and_balance_is_signed()
    {
        Assert.Equal(6, HdvRowLogic.ToBuy(10, 4, 0));
        Assert.Equal(0, HdvRowLogic.ToBuy(10, 4, 20));
        Assert.Equal(-6, HdvRowLogic.Balance(10, 4, 0));
        Assert.Equal(14, HdvRowLogic.Balance(10, 4, 20));
    }

    [Theory]
    [InlineData(500L, 0L, false)]    // pas de maximum fixé
    [InlineData(500L, 500L, false)]  // égal au maximum : autorisé
    [InlineData(501L, 500L, true)]
    public void Lot_is_over_max_only_when_a_max_is_set_and_exceeded(long price, long max, bool expected)
        => Assert.Equal(expected, HdvRowLogic.OverMax(price, max));

    [Fact]
    public void Level_and_lot_text()
    {
        Assert.Equal("Niv. 120", HdvRowLogic.Level(120));
        Assert.Equal("", HdvRowLogic.Level(0));
        Assert.Equal("x100", HdvRowLogic.Lot(100));
    }
}
