using System.Xml.Linq;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class FightSummaryTextTests
{
    [Theory]
    [InlineData("fr", 14, 5, "Combat gagné en 14 secondes, 5 tours")]
    [InlineData("fr", 1, 1, "Combat gagné en 1 seconde, 1 tour")]
    [InlineData("en", 14, 5, "Fight won in 14 seconds, 5 turns")]
    [InlineData("en", 1, 1, "Fight won in 1 second, 1 turn")]
    [InlineData("es", 14, 5, "Combate ganado en 14 segundos, 5 turnos")]
    [InlineData("fr", 14, null, "Combat gagné en 14 secondes")]
    [InlineData("en", 0, 0, "Fight won in 0 seconds")]
    public void Summary_uses_localized_units_and_only_observed_turns(string language, long seconds, int? turns, string expected)
    {
        string directory = MappingScan.WalkUp(d => Directory.Exists(Path.Combine(d.FullName, "GivreClone.App")))?.FullName
            ?? throw new DirectoryNotFoundException();
        var doc = XDocument.Load(Path.Combine(directory, "GivreClone.App", "Lang", language + ".axaml"));
        var resources = doc.Root!.Elements().Where(e => e.Name.LocalName == "String")
            .ToDictionary(e => e.Attributes().Single(a => a.Name.LocalName == "Key").Value, e => e.Value);

        string actual = FightSummaryText.Format(2, true, seconds, turns, key => resources[key]);

        Assert.Equal(expected, actual);
    }
}
