extern alias F06App;

using System.Collections.Immutable;
using System.Linq;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace GivreClone.Tests;

public sealed class EliotropePortalComboTests(ITestOutputHelper output)
{
    private static int Cell(int x, int y) => MapPathfinder.CellFromMapPoint(x, y);

    private static readonly F06App::GivreClone.App.Services.GameNameResolver Names = Load();

    private static F06App::GivreClone.App.Services.GameNameResolver Load()
    {
        var names = new F06App::GivreClone.App.Services.GameNameResolver();
        names.EnsureLoaded(); names.EnsureCombatLoaded();
        return names;
    }

    private static SpellCastDescriptor? Lookup(int id, int grade)
    {
        var actual = Names.GetSpellDescriptor(id, grade) ?? Names.GetSpellDescriptor(id, 1);
        return actual is null ? null : System.Text.Json.JsonSerializer.Deserialize<SpellCastDescriptor>(
            System.Text.Json.JsonSerializer.Serialize(actual));
    }

    private static CombatTurnSnapshot Snapshot(int enemyX, int ap, params int[] spellIds)
    {
        // Live logs show two 14574 casts in one turn (e.g. cells 202 then 228): two casts are legal.
        var spells = spellIds.Select(id => Lookup(id, 2)!).Select(d => new PlannerSpell(d, d.SpellId == 14574 ? 2 : 1,
            d.SpellId is 14574 ? 0 : 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L, 2L)))
            .ToImmutableArray();
        return new CombatTurnSnapshot(
            Enumerable.Range(0, 26).SelectMany(x => Enumerable.Range(-3, 7).Select(y => Cell(x, y)))
                .Where(c => c >= 0).Distinct()
                .Select(c => new PlannerCell(c, true, false, ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(
                new PlannerFighter(1, Cell(4, 0), false, 2000, 2000, Breed: ClassMechanics.EliotropeBreedId,
                    States: ImmutableHashSet<int>.Empty),
                new PlannerFighter(2, Cell(enemyX, 0), true, 6000, 6000, MovePoints: 0,
                    ExpectedDamage: 0, ThreatKnown: true, States: ImmutableHashSet<int>.Empty)),
            1, ap, 0, spells, ImmutableDictionary<int, int>.Empty, MaxCasts: 99, SpellLookup: Lookup, CasterLevel: 193,
            UnsafeForcedMovementCells: ImmutableHashSet<int>.Empty);
    }

    private void Show(string label, CombatTurnPlan plan) => output.WriteLine($"{label}: " +
        string.Join(" > ", plan.Actions.Select(a => a + (a.ViaPortal ? "*" : ""))) +
        $" damage={plan.Score.Damage:0.0} total={plan.Score.Total:0.0}");

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    public void Spare_ap_builds_a_portal_pair_and_shoots_through_it_for_more_damage(int enemyX)
    {
        // 11 AP, one cast each of 14608 (3) and 14588 (4): 4 AP are spare, so two 1 AP portals
        // cost no shot. Shooting through the pair adds +2 % per network cell; direct-only is worse.
        var withPortals = CombatTurnPlanner.Choose(Snapshot(enemyX, 11, 14574, 14608, 14588));
        var directOnly = CombatTurnPlanner.Choose(Snapshot(enemyX, 11, 14608, 14588));
        Show("portals", withPortals); Show("direct", directOnly);

        Assert.Equal(2, withPortals.Actions.Count(a => a.SpellId == 14574));
        Assert.Contains(withPortals.Actions, a => a.ViaPortal);
        Assert.True(withPortals.Score.Damage > directOnly.Score.Damage,
            $"{withPortals.Score.Damage} <= {directOnly.Score.Damage}");
    }

    [Fact]
    public void No_spare_ap_keeps_the_direct_shots_and_lays_no_pair()
    {
        // 7 AP exactly pays 14608 + 14588: any portal would cost a whole shot.
        var plan = CombatTurnPlanner.Choose(Snapshot(10, 7, 14574, 14608, 14588));
        Show("tight", plan);
        Assert.True(plan.Actions.Count(a => a.SpellId == 14574) < 2);
        Assert.Contains(plan.Actions, a => a.SpellId is 14608 or 14588);
    }
}
