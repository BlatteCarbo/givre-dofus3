extern alias F06App;

using System.Collections.Immutable;
using System.Linq;
using GivreClone.App.Services.Combat;
using Xunit;

namespace GivreClone.Tests;

public class EliotropeLearnedRotationPlannerGateTests
{
    private static readonly int[] LearnedAtLevel193 =
        { 14594, 14579, 14619, 14608, 369, 14588, 14617, 14574, 14577, 14580, 14582, 14587, 14589, 14591, 14596, 14611 };

    private static SpellCastDescriptor? Real(F06App::GivreClone.App.Services.GameNameResolver names, int id)
    {
        var actual = names.GetSpellDescriptor(id, 2) ?? names.GetSpellDescriptor(id, 1);
        return actual is null ? null : System.Text.Json.JsonSerializer.Deserialize<SpellCastDescriptor>(
            System.Text.Json.JsonSerializer.Serialize(actual));
    }

    [Fact]
    public void Real_level_193_eliotrope_rotation_reaches_the_tactical_planner()
    {
        var names = new F06App::GivreClone.App.Services.GameNameResolver();
        names.EnsureCombatLoaded();
        var rotation = LearnedAtLevel193.Select(id => Real(names, id)).ToArray();

        Assert.All(rotation, Assert.NotNull);
        Assert.True(AutoSpellRotation.ShouldUsePlanner(rotation));
    }

    [Fact]
    public void Distribution_end_of_turn_damage_is_modeled_but_an_unknown_trigger_still_falls_back()
    {
        var names = new F06App::GivreClone.App.Services.GameNameResolver();
        names.EnsureCombatLoaded();
        var distribution = Real(names, 14594)!;
        Assert.True(AutoSpellRotation.ShouldUsePlanner(new SpellCastDescriptor?[] { distribution }));

        var unknownTrigger = distribution with
        {
            Effects = distribution.Effects.Select(e => e.Triggers == "TE" ? e with { Triggers = "TX" } : e).ToImmutableArray()
        };
        Assert.False(AutoSpellRotation.ShouldUsePlanner(new SpellCastDescriptor?[] { unknownTrigger }));
    }
}
