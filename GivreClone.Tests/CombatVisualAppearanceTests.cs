using GivreClone.App.Services;
using Xunit;
namespace GivreClone.Tests;
public class CombatVisualAppearanceTests
{
    [Fact]
    public void MissingKeysKeepNormalAppearanceAndUpstreamCategoryDefaults()
    {
        var profile = System.Text.Json.JsonSerializer.Deserialize<CombatProfile>("{}")!;
        Assert.Equal(0, profile.CombatAppearanceMode);
        Assert.Equal(0, profile.RoleplayAppearanceMode);
        Assert.Equal(3, CombatVisualOptions.Mask(profile));
        var selection = CombatVisualOptions.FromProfile(profile);
        Assert.Equal(CombatVisualOptions.DefaultCombatMiniatures, selection.CombatMiniatures);
        Assert.Equal(CombatVisualOptions.DefaultRoleplayMiniatures, selection.RoleplayMiniatures);
        Assert.Equal("111111", selection.CombatMiniatures);
        Assert.Equal("11110", selection.RoleplayMiniatures);
        Assert.True(selection.WantsModule);
        Assert.False(profile.RoleplayMiniatureNpcs);
        Assert.True(profile.MiniatureSelf && profile.RoleplayMiniatureSelf && profile.RoleplayMiniatureMonsters);
    }

    [Fact]
    public void AppearanceAloneLoadsTheModuleWithoutWideningTheMask()
    {
        var profile = new CombatProfile { NoSpellEffects = false, ShortSpellCasts = false, CombatAppearanceMode = 2, RoleplayAppearanceMode = 1 };
        Assert.Equal(0, CombatVisualOptions.Mask(profile));
        Assert.True(CombatVisualOptions.WantsModule(profile));
        profile.CombatAppearanceMode = 0;
        profile.RoleplayAppearanceMode = 0;
        profile.MiniatureEnemies = false;
        profile.RoleplayMiniatureNpcs = true;
        Assert.False(CombatVisualOptions.WantsModule(profile));
        profile.RoleplayAppearanceMode = 1;
        Assert.Equal(0, CombatVisualOptions.Mask(profile));
        Assert.True(CombatVisualOptions.FromProfile(profile).WantsModule);
    }

    [Fact]
    public void EveryMaskCombinationStaysOnSixBitsAndDoesNotImplyAnAppearanceMode()
    {
        for (int bits = 0; bits < 64; bits++)
        {
            var profile = new CombatProfile
            {
                NoSpellEffects = (bits & 1) != 0, ShortSpellCasts = (bits & 2) != 0,
                NoAnim = (bits & 4) != 0, NoDeathAnimations = (bits & 8) != 0,
                InstantMove = (bits & 16) != 0, Collapse = (bits & 32) != 0
            };
            var selection = CombatVisualOptions.FromProfile(profile);
            Assert.Equal(bits, selection.Mask);
            Assert.Equal(0, selection.CombatMode);
            Assert.Equal(0, selection.RoleplayMode);
            Assert.Equal(bits != 0, selection.WantsModule);
            var restored = System.Text.Json.JsonSerializer.Deserialize<CombatProfile>(System.Text.Json.JsonSerializer.Serialize(profile))!;
            Assert.Equal(bits, CombatVisualOptions.Mask(restored));
            Assert.Equal(0, restored.CombatAppearanceMode);
        }
    }

    [Fact]
    public void IniLinesKeepModesBesideTheMaskAndRejectValuesOutsideTheRange()
    {
        var profile = new CombatProfile
        {
            NoSpellEffects = false, ShortSpellCasts = false, NoAnim = true,
            CombatAppearanceMode = 2, RoleplayAppearanceMode = 1,
            MiniatureEnemies = false, RoleplayMiniatureNpcs = true
        };
        var selection = CombatVisualOptions.FromProfile(profile);
        Assert.Equal("111101", selection.CombatMiniatures);
        Assert.Equal("11111", selection.RoleplayMiniatures);
        Assert.Equal("mask=4\ncombat-mode=2\nroleplay-mode=1\ncombat-miniatures=111101\nroleplay-miniatures=11111\n",
            CombatVisualOptions.FormatIni(selection));
        var invalid = new CombatVisualSelection(100, 9, -1, "10", "11111");
        Assert.Equal("mask=36\ncombat-mode=0\nroleplay-mode=0\ncombat-miniatures=111111\nroleplay-miniatures=11111\n",
            CombatVisualOptions.FormatIni(invalid));
        Assert.Equal(0, CombatVisualOptions.NormalizeMode(9));
        Assert.Equal(2, CombatVisualOptions.NormalizeMode(2));
    }

    [Fact]
    public void TeardownPersistsZeroModesAndDoesNotReloadTheModule()
    {
        var profile = new CombatProfile
        {
            NoSpellEffects = false, ShortSpellCasts = false, NoAnim = true,
            CombatAppearanceMode = 2, RoleplayAppearanceMode = 1,
            MiniatureEnemies = false, RoleplayMiniatureNpcs = true
        };
        var pushed = CombatVisualOptions.Resolve(profilePush: true, profile);
        var teardown = CombatVisualOptions.Resolve(profilePush: false, profile);
        Assert.Equal(2, pushed.CombatMode);
        Assert.Equal(4, pushed.Mask);
        Assert.Equal("111101", pushed.CombatMiniatures);
        Assert.Equal("11111", pushed.RoleplayMiniatures);
        Assert.True(pushed.WantsModule);
        Assert.Equal(CombatVisualSelection.Teardown, teardown);
        Assert.False(teardown.WantsModule);
        Assert.Equal(0, teardown.Mask);
        Assert.Equal(0, teardown.CombatMode);
        Assert.Equal(0, teardown.RoleplayMode);
        Assert.Equal(CombatVisualOptions.DefaultCombatMiniatures, teardown.CombatMiniatures);
        Assert.Equal(CombatVisualOptions.DefaultRoleplayMiniatures, teardown.RoleplayMiniatures);
        string ini = CombatVisualOptions.FormatIni(teardown);
        Assert.Contains("mask=0\n", ini);
        Assert.Contains("combat-mode=0\n", ini);
        Assert.Contains("roleplay-mode=0\n", ini);
        Assert.DoesNotContain("combat-mode=2", ini);
        Assert.DoesNotContain("roleplay-mode=1", ini);
    }

    [Fact]
    public void CloneAndJsonKeepBothModesAndTheElevenCategories()
    {
        var profile = new CombatProfile
        {
            CombatAppearanceMode = 2, RoleplayAppearanceMode = 1,
            MiniatureSelf = false, MiniatureAllies = true, MiniatureAllySummons = false,
            MiniatureOwnSummons = true, MiniatureEnemies = false, MiniatureEnemySummons = true,
            RoleplayMiniatureSelf = false, RoleplayMiniatureParty = true, RoleplayMiniaturePlayers = false,
            RoleplayMiniatureMonsters = true, RoleplayMiniatureNpcs = true
        };
        var clone = profile.Clone();
        profile.CombatAppearanceMode = 0;
        profile.MiniatureSelf = true;
        profile.RoleplayMiniatureNpcs = false;
        Assert.Equal(2, clone.CombatAppearanceMode);
        Assert.Equal(1, clone.RoleplayAppearanceMode);
        Assert.False(clone.MiniatureSelf);
        Assert.True(clone.MiniatureEnemySummons);
        Assert.True(clone.RoleplayMiniatureNpcs);
        Assert.False(clone.RoleplayMiniaturePlayers);
        var restored = System.Text.Json.JsonSerializer.Deserialize<CombatProfile>(System.Text.Json.JsonSerializer.Serialize(clone))!;
        Assert.Equal(CombatVisualOptions.FromProfile(clone), CombatVisualOptions.FromProfile(restored));
    }
}
