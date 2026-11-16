using GivreClone.App.Services.Eco;
using Xunit;

namespace GivreClone.Tests;

public class ForgemagieProvenanceTests
{
    [Fact]
    public void Missing_game_data_is_reported_instead_of_a_silent_zero_weight()
        => Assert.StartsWith("Poids indisponibles", Forgemagie.WeightProvenance(0, 4));

    [Theory]
    [InlineData(0, "")]
    [InlineData(2, " 2 caractéristique(s) sans poids connu, non comptées.")]
    public void Loaded_rates_name_their_source_and_count_unweighted_lines(int unknown, string tail)
        => Assert.Equal("Poids par point lus dans les données du jeu (EffectData.effectPowerRate)." + tail, Forgemagie.WeightProvenance(120, unknown));
}
