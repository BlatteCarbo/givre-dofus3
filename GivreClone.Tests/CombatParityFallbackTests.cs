using GivreClone.App.Services;
using GivreClone.App.Services.CombatParity;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Exerce les sources de production liées par le csproj sans charger l'UI Avalonia,
/// aucun binaire App précompilé, aucun client et aucune donnée de compte.
/// </summary>
public sealed class CombatParityFallbackTests
{
    [Fact]
    public void Enemy_spell_config_remains_eligible_for_offensive_recast()
    {
        var spell = new SpellConfig { SpellId = 101, PerTurn = 2, TargetKind = SpellTargetKind.Enemy };
        Assert.True(spell.CanUseOffensiveFallback());
        Assert.True(OffensiveFallbackPolicy.IsEligible(true, 101, 2, false, false, SpellTargetKind.Enemy));
    }

    [Theory]
    [InlineData(false, 101, 1, false, false, SpellTargetKind.Enemy)]
    [InlineData(true, 0, 1, false, false, SpellTargetKind.Enemy)]
    [InlineData(true, -20, 1, false, false, SpellTargetKind.Enemy)]
    [InlineData(true, 101, 0, false, false, SpellTargetKind.Enemy)]
    [InlineData(true, 101, -1, false, false, SpellTargetKind.Enemy)]
    [InlineData(true, 101, 1, true, false, SpellTargetKind.Enemy)]
    [InlineData(true, 101, 1, false, true, SpellTargetKind.Enemy)]
    [InlineData(true, 101, 1, false, false, SpellTargetKind.Ally)]
    [InlineData(true, 101, 1, false, false, (SpellTargetKind)99)]
    public void Unsafe_spells_are_excluded_by_both_policy_and_actual_profile_method(
        bool enabled, int id, int perTurn, bool onMe, bool summon, SpellTargetKind target)
    {
        var spell = new SpellConfig
        {
            Enabled = enabled, SpellId = id, PerTurn = perTurn,
            OnMe = onMe, IsSummon = summon, TargetKind = target
        };
        Assert.False(OffensiveFallbackPolicy.IsEligible(enabled, id, perTurn, onMe, summon, target));
        Assert.False(spell.CanUseOffensiveFallback());
    }

    [Fact]
    public void Cloning_profile_preserves_summon_guard_and_is_independent()
    {
        var source = new CombatProfile
        {
            Spells = [new SpellConfig { SpellId = 101, PerTurn = 1, IsSummon = true, TargetKind = SpellTargetKind.Enemy }]
        };
        var copy = source.Clone();
        Assert.NotSame(source.Spells[0], copy.Spells[0]);
        Assert.False(copy.Spells[0].CanUseOffensiveFallback());
        copy.Spells[0].IsSummon = false;
        Assert.True(copy.Spells[0].CanUseOffensiveFallback());
        Assert.True(source.Spells[0].IsSummon);
        Assert.False(source.Spells[0].CanUseOffensiveFallback());
    }
}
