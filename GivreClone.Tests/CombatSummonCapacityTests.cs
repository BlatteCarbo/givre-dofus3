using System.Collections.Immutable;
using System.Linq;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class CombatSummonCapacityTests
{
    private static int Cell(int x) => MapPathfinder.CellFromMapPoint(8 + x, 8);

    private static CombatTurnSnapshot Board(int owned, int capacity, int? configuredMax = null)
    {
        // Damage makes each admitted summon useful to the real search, so an empty plan
        // cannot accidentally pass because summon-only occupancy has no immediate reward.
        var effects = ImmutableArray.Create(
            new SpellEffectDescriptor(181, SpellEffectKind.Summon, 42, 42, 0, 0, "x", 'P', 0, 0),
            new SpellEffectDescriptor(100, SpellEffectKind.Damage, 10, 10, 0, 0, "A", 'C', 6, 0));
        var spell = new PlannerSpell(new(10, 1, 3, 1, 6, 8, false, 3, 0, 0, 0, "", effects),
            3, 0, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L), MaxSummons: configuredMax);
        var fighters = ImmutableArray.Create(new PlannerFighter(1, Cell(0), false, 100, 100),
            new PlannerFighter(2, Cell(3), true, 1000, 1000, MovePoints: 0,
                ExpectedDamage: 0, AttackRange: 0, ThreatKnown: true));
        for (int i = 0; i < owned; i++)
            fighters = fighters.Add(new PlannerFighter(-1 - i, Cell(4 + i), false, 10, 10, SummonerId: 1));
        return new(Enumerable.Range(0, 7).Select(i => new PlannerCell(Cell(i), true, false,
            ImmutableArray<int>.Empty)).ToImmutableArray(), fighters, 1, 9, 0,
            ImmutableArray.Create(spell), ImmutableDictionary<int, int>.Empty.Add(CharIds.MaxSummonedCreatures, capacity));
    }

    [Theory]
    [InlineData(2, 2, null, 0)]
    [InlineData(1, 2, null, 1)]
    [InlineData(0, 2, null, 2)]
    [InlineData(1, 3, 1, 0)]
    [InlineData(0, 3, 1, 1)]
    [InlineData(0, 3, 0, 1)]
    [InlineData(0, 0, 3, 0)]
    public void Planning_respects_caster_and_profile_capacity_in_every_branch(
        int owned, int capacity, int? configuredMax, int expectedCasts)
    {
        var plan = CombatTurnPlanner.Choose(Board(owned, capacity, configuredMax));

        Assert.Equal(expectedCasts, plan.Actions.Count(a => a.Kind == PlannerActionKind.Cast));
        Assert.Equal(expectedCasts * 10, plan.Score.Damage);
        Assert.False(plan.BudgetExhausted);
    }

    [Fact]
    public void Confirmed_death_removal_frees_one_slot_on_next_plan()
    {
        var snapshot = Board(2, 2);
        Assert.Empty(CombatTurnPlanner.Choose(snapshot).Actions);

        // The live Death handler removes the fighter; the ownership latch remains.
        var afterDeath = snapshot with { Fighters = snapshot.Fighters.RemoveAt(2) };
        var plan = CombatTurnPlanner.Choose(afterDeath);

        Assert.Single(plan.Actions);
        Assert.Equal(10, plan.Score.Damage);
    }

    [Fact]
    public void Allied_and_enemy_owned_summons_do_not_consume_caster_slots()
    {
        var snapshot = Board(2, 2);
        snapshot = snapshot with { Fighters = snapshot.Fighters
            .SetItem(2, snapshot.Fighters[2] with { SummonerId = 3 })
            .SetItem(3, snapshot.Fighters[3] with { SummonerId = 2 }) };

        Assert.Equal(2, CombatTurnPlanner.Choose(snapshot).Actions.Length);
    }

    [Fact]
    public void Missing_caster_characteristic_keeps_configured_summons_available()
    {
        var snapshot = Board(0, 2, 1) with { Characteristics = ImmutableDictionary<int, int>.Empty };

        Assert.Single(CombatTurnPlanner.Choose(snapshot).Actions);
    }

    [Fact]
    public void Different_summon_spells_share_the_same_living_capacity()
    {
        var snapshot = Board(1, 2);
        var other = snapshot.Spells[0] with { Cast = snapshot.Spells[0].Cast with { SpellId = 20 } };
        snapshot = snapshot with { Spells = snapshot.Spells.Add(other) };

        Assert.Single(CombatTurnPlanner.Choose(snapshot).Actions);
    }

    [Fact]
    public void At_summon_cap_regular_damage_remains_available()
    {
        var snapshot = Board(2, 2);
        var attack = snapshot.Spells[0] with
        {
            Cast = snapshot.Spells[0].Cast with
            {
                SpellId = 20, Flags = 0,
                Effects = snapshot.Spells[0].Cast.Effects.RemoveAt(0)
            },
            CastsLeft = 1
        };
        snapshot = snapshot with { Spells = snapshot.Spells.Add(attack) };

        Assert.Equal(20, Assert.Single(CombatTurnPlanner.Choose(snapshot).Actions).SpellId);
    }
}
