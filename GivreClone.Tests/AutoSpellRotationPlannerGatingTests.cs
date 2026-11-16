using System.Collections.Immutable;
using GivreClone.App.Services.Combat;
using Xunit;

namespace GivreClone.Tests;

public sealed class AutoSpellRotationPlannerGatingTests
{
    private static SpellCastDescriptor Attack(int id, string stateCriterion = "") =>
        new(id, 1, 3, 0, 6, 0, false, 1, 1, 0, 0, stateCriterion,
            ImmutableArray.Create(new SpellEffectDescriptor(99, SpellEffectKind.Damage,
                20, 20, 2, 0, "A", 'P', 0, 0, DiceNum: 20, DiceSide: 20, Triggers: "I")));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unknown_attack_keeps_fallback_regardless_of_rotation_order(bool unknownFirst)
    {
        SpellCastDescriptor modeled = Attack(70);
        SpellCastDescriptor conditional = Attack(71, "HS!97");
        Assert.True(CombatTurnPlanner.HasModeledEnemyDamage(modeled));
        var rotation = unknownFirst
            ? new SpellCastDescriptor?[] { conditional, modeled }
            : new SpellCastDescriptor?[] { modeled, conditional };

        Assert.False(AutoSpellRotation.ShouldUsePlanner(rotation));
        Assert.True(AutoSpellRotation.ShouldUsePlanner(new[] { modeled }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_descriptor_does_not_disappear_behind_modeled_attack(bool unknownFirst)
    {
        SpellCastDescriptor modeled = Attack(72);
        var rotation = unknownFirst
            ? new SpellCastDescriptor?[] { null, modeled }
            : new SpellCastDescriptor?[] { modeled, null };

        Assert.False(AutoSpellRotation.ShouldUsePlanner(rotation));
    }
}
