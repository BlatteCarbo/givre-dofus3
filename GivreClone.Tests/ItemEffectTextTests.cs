using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class ItemEffectTextTests
{
    private static string F(string? template, int value = 0, int min = 0, int max = 0, int cst = 0, string? text = null, int id = 125)
        => ItemEffectText.Format(id, template, ItemEffectText.Params(value, min, max, cst, text));

    [Fact]
    public void ValeurSimple() => Assert.Equal("143 Vitalité", F("#1{~1~2 à }#2 Vitalité", value: 143));

    [Fact]
    public void Intervalle() => Assert.Equal("10 à 15 Vitalité", F("#1{~1~2 à }#2 Vitalité", min: 10, max: 15));

    [Fact]
    public void IntervalleDegenere() => Assert.Equal("12 Force", F("#1{~1~2 à }#2 Force", min: 12, max: 12));

    [Fact]
    public void Malus() => Assert.Equal("-8% Résistance Air", F("-#1{~1~2 à -}#2% Résistance Air", value: 8));

    [Fact]
    public void Pluriel()
    {
        Assert.Equal("1 PM", F("#1{~1~2 à }#2 PM", value: 1));
        Assert.Equal("2 combats restants", F("#1 combat{~ps} restant{~ps}", value: 2));
        Assert.Equal("1 combat restant", F("#1 combat{~ps} restant{~ps}", value: 1));
    }

    [Fact]
    public void Texte() => Assert.Equal("Fabriqué par : Snow", F("Fabriqué par : #4", text: "Snow", id: 988));

    [Fact]
    public void Balises() => Assert.Equal("10 Dommages Terre", F("<b>#1</b>{~1~2 à }#2 Dommages Terre", value: 10));

    [Fact]
    public void SansGabarit()
    {
        Assert.Equal("Effet 9999 : 5", F(null, value: 5, id: 9999));
        Assert.Equal("Effet 9999", F("", id: 9999));
    }

    [Fact]
    public void Des() => Assert.Equal("11 à 15 (dommages Terre)", F("#1{~1~2 à }#2 (dommages Terre)", min: 11, max: 15));

    [Theory]
    [InlineData(-1, 8, "8 Force", true)]
    [InlineData(1, -8, "-8 Force", true)]
    [InlineData(0, 3, "-3 PA", true)]
    [InlineData(1, 143, "143 Vitalité", false)]
    public void Negatif(int bonus, int value, string text, bool neg) => Assert.Equal(neg, ItemEffectText.IsNegative(bonus, value, text));
}
