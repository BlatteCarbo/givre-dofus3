using GivreClone.App.Services.CombatParity;
using Xunit;
namespace GivreClone.Tests;

public sealed class SpellZoneSelectionTests
{
    [Fact]
    public void Dechainement_retient_le_carre_de_degats_pas_le_point_sur_le_lanceur()
    {
        var effects = new[]
        {
            new SpellZoneCandidate('P', 1, 0, Damaging: false, TargetMask: "C"),
            new SpellZoneCandidate('G', 1, 0, Damaging: true, TargetMask: "g,A"),
        };
        Assert.Equal(((int)'G', 1, 0), SpellZoneSelection.Pick(effects));
    }

    [Fact]
    public void Degats_prioritaires_puis_plus_grand_rayon_puis_vraie_forme()
    {
        // Une poussée sans dégâts plus large ne remplace pas la zone des dégâts.
        Assert.Equal(((int)'C', 2, 0), SpellZoneSelection.Pick(new[]
        {
            new SpellZoneCandidate('X', 4, 0, false, "A"),
            new SpellZoneCandidate('C', 2, 0, true, "A"),
            new SpellZoneCandidate('P', 3, 0, false, "C"),
        }));
        Assert.Equal(((int)'X', 2, 1), SpellZoneSelection.Pick(new[]
        {
            new SpellZoneCandidate('P', 2, 0, true, "A"),
            new SpellZoneCandidate('X', 2, 1, true, "A"),
            new SpellZoneCandidate('C', 1, 0, true, "A"),
        }));
    }

    [Fact]
    public void Sans_degats_les_effets_hors_lanceur_passent_avant_le_lanceur_et_rien_sans_zone()
    {
        Assert.Equal(((int)'C', 1, 0), SpellZoneSelection.Pick(new[]
        {
            new SpellZoneCandidate('C', 3, 0, false, "C"),
            new SpellZoneCandidate('C', 1, 0, false, "a,A"),
        }));
        Assert.Null(SpellZoneSelection.Pick(new[] { new SpellZoneCandidate(0, 3, 0, true, "A") }));
        Assert.True(SpellZoneSelection.CasterOnly("C"));
        Assert.True(SpellZoneSelection.CasterOnly(" c , C "));
        Assert.False(SpellZoneSelection.CasterOnly("C,A"));
        Assert.False(SpellZoneSelection.CasterOnly(null));
    }
}
