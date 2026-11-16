using GivreClone.App.Services;
using Xunit;
namespace GivreClone.Tests;
public class CombatVisualOptionsTests
{
    [Theory]
    [InlineData("ERROR Dofus 3.7.12.12 fingerprints differ; visual hooks refused", true)]
    [InlineData("READY", false)]
    [InlineData("", false)]
    [InlineData("ERROR DNM_Load returned -1", false)]
    public void Native_fingerprint_refusal_disables_the_advanced_provider(string status, bool refused) =>
        Assert.Equal(refused, CombatVisualOptions.CompatibilityRefused(status));

    [Fact] public void DefaultsAndClonePreserveTheSixIndependentOptions()
    {
        var p = new CombatProfile(); Assert.Equal(3, CombatVisualOptions.Mask(p));
        p.NoDeathAnimations = true; p.NoAnim = true; p.InstantMove = true; p.Collapse = true;
        var clone = p.Clone(); Assert.Equal(63, CombatVisualOptions.Mask(clone));
        p.ShortSpellCasts = false; Assert.True(clone.ShortSpellCasts);
        clone.NoSpellEffects = false; clone.ShortSpellCasts = false; clone.NoDeathAnimations = false;
        clone.NoAnim = false; clone.InstantMove = false; clone.Collapse = false;
        Assert.Equal(0, CombatVisualOptions.Mask(clone));
    }
    [Theory] [InlineData("SetCombatProfile", true)] [InlineData("RemoveCombatProfile", false)]
    [InlineData("Forget", false)] [InlineData("", false)]
    public void TeardownDoesNotReenableDefaultVisualOptions(string caller, bool expected) =>
        Assert.Equal(expected, CombatVisualOptions.IsProfilePush(caller));
    [Fact] public void EveryCombinationSurvivesPersistenceAndMissingLegacyKeysUseTheDefaults()
    {
        Assert.Equal(3, CombatVisualOptions.Mask(System.Text.Json.JsonSerializer.Deserialize<CombatProfile>("{}")!));
        for (int bits = 0; bits < 64; bits++)
        {
            var p = new CombatProfile { NoSpellEffects = (bits & 1) != 0, ShortSpellCasts = (bits & 2) != 0,
                NoAnim = (bits & 4) != 0, NoDeathAnimations = (bits & 8) != 0,
                InstantMove = (bits & 16) != 0, Collapse = (bits & 32) != 0 };
            var restored = System.Text.Json.JsonSerializer.Deserialize<CombatProfile>(System.Text.Json.JsonSerializer.Serialize(p))!;
            Assert.Equal(bits, CombatVisualOptions.Mask(restored));
        }
    }
}
