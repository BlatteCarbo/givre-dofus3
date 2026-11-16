using System.Linq;
using GivreClone.App.Services.Equipment;
using Xunit;

namespace GivreClone.Tests;

public class BuildStatsTests
{
    [Fact]
    public void Sums_same_labels_across_items_and_subtracts_maluses()
    {
        var t = BuildStats.Aggregate(new (string, bool)[]
        {
            ("143 Vitalité", false), ("30 Sagesse", false), ("80 Vitalité", false),
            ("10 Sagesse", true),                    // malus rouge
            ("5% Résistance Air", false), ("-8% Résistance Air", true), ("2 PA", false),
        });
        Assert.Equal(new[] { "+223 Vitalité", "+20 Sagesse", "-3% Résistance Air", "+2 PA" }, t.Select(x => x.Text).ToArray());
    }

    [Fact]
    public void Ranges_count_their_high_value_and_text_without_value_is_ignored()
    {
        var t = BuildStats.Aggregate(new (string, bool)[] { ("10 à 15 Force", false), ("Échangeable le 25/10/2026", false), ("(Pas de bonus)", false) });
        Assert.Equal(new[] { "+15 Force" }, t.Select(x => x.Text).ToArray());
    }

    [Fact]
    public void Zero_totals_disappear()
    {
        Assert.Empty(BuildStats.Aggregate(new (string, bool)[] { ("10 Chance", false), ("10 Chance", true) }));
    }
}
