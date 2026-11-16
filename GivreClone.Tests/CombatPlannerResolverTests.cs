extern alias F06App;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using GivreClone.App.Services;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class CombatPlannerResolverTests
{
    private static int Cell(int x) => MapPathfinder.CellFromMapPoint(8 + x, 8);
    private static SpellEffectDescriptor Effect(int action, int amount, string mask = "A", int size = 0) =>
        new(action, SpellEffectDescriptor.Classify(action), amount, amount, 0, 0, mask, size > 0 ? 'C' : 'P', size, 0);
    private static PlannerSpell Spell(int id, params SpellEffectDescriptor[] effects) => new(
        new(id, 1, 3, 0, 6, 0, false, 1, 1, 0, 0, "", effects.ToImmutableArray()), 1, 1,
        ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L, 2L, 3L, 4L));
    private static CombatTurnSnapshot Board(params PlannerSpell[] spells) => new(
        Enumerable.Range(0, 7).Select(i => new PlannerCell(Cell(i), true, false, ImmutableArray<int>.Empty)).ToImmutableArray(),
        ImmutableArray.Create(new PlannerFighter(1, Cell(0), false, 100, 100),
            new PlannerFighter(2, Cell(3), true, 100, 100, MovePoints: 0, AttackRange: 6, ExpectedDamage: 20, ThreatKnown: true)),
        1, 3, 0, spells.ToImmutableArray(), ImmutableDictionary<int, int>.Empty);

    [Fact]
    public void Zone_never_aims_at_blocked_terrain_to_hit_two_enemies()
    {
        var board = Board(Spell(10, Effect(100, 30, size: 1)));
        board = board with
        {
            Fighters = board.Fighters.Add(board.Fighters[1] with { Id = 3, Cell = Cell(5) }),
            Cells = board.Cells.Select(c => c.Id == Cell(4) ? c with { Walkable = false } : c).ToImmutableArray()
        };
        var plan = CombatTurnPlanner.Choose(board);
        Assert.DoesNotContain(plan.Actions, a => a.Kind == PlannerActionKind.Cast && a.Cell == Cell(4));
        Assert.Equal(30, plan.Score.Damage);
    }

    [Fact]
    public void Refused_center_is_excluded_without_disabling_the_spell()
    {
        var spell = Spell(10, Effect(100, 30, size: 1)) with
        {
            AllowedCells = ImmutableHashSet.Create(Cell(3)),
            ZonePolicy = new('C', 1, 0, 1, true, ImmutableHashSet<long>.Empty)
        };
        var plan = CombatTurnPlanner.Choose(Board(spell));
        Assert.Equal(Cell(3), Assert.Single(plan.Actions).Cell);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 30)]
    public void Zone_safety_uses_the_casting_cell_after_self_walks_out(int mp, int damage)
    {
        var spell = Spell(10, Effect(100, 30, size: 1)) with
        {
            AllowedCells = ImmutableHashSet.Create(Cell(3)),
            ZonePolicy = new('C', 1, 0, 1, true, ImmutableHashSet<long>.Empty)
        };
        var board = Board(spell) with { Mp = mp, TackleImmune = true };
        board = board with
        {
            Fighters = board.Fighters.SetItem(0, board.Fighters[0] with { Cell = Cell(2) }),
            Cells = board.Cells.Select(c => c.Id == Cell(2)
                ? c with { Neighbours = ImmutableArray.Create(Cell(1)) } : c).ToImmutableArray()
        };

        var plan = CombatTurnPlanner.Choose(board);

        Assert.Equal(damage, plan.Score.Damage);
        if (mp == 0) Assert.Empty(plan.Actions);
        else
        {
            Assert.Equal(new[] { PlannerActionKind.Move, PlannerActionKind.Cast }, plan.Actions.Select(a => a.Kind));
            Assert.Equal(new[] { Cell(1), Cell(3) }, plan.Actions.Select(a => a.Cell));
        }
    }

    [Theory]
    [InlineData("ally", 0)]
    [InlineData("minimum-hits", 0)]
    [InlineData("challenge", 0)]
    [InlineData("profile-exclusion", 30)]
    public void Zone_policy_is_geometric_even_when_damage_masks_exclude_the_fighter(string restriction, int damage)
    {
        var spell = Spell(10, Effect(100, 30, "A,B14", 1)) with
        {
            AllowedTargets = ImmutableHashSet.Create(2L),
            AllowedCells = ImmutableHashSet.Create(Cell(3)),
            ZonePolicy = new('C', 1, 0, restriction is "minimum-hits" or "profile-exclusion" ? 2 : 1, true,
                restriction == "challenge" ? ImmutableHashSet.Create(3L) : ImmutableHashSet<long>.Empty)
        };
        var board = Board(spell);
        board = board with { Fighters = board.Fighters.SetItem(1, board.Fighters[1] with { Breed = 14 }) };
        if (restriction != "minimum-hits")
            board = board with { Fighters = board.Fighters.Add(new PlannerFighter(3, Cell(4), restriction != "ally",
                100, 100, Breed: 4, ExpectedDamage: 0, MovePoints: 0)) };

        var plan = CombatTurnPlanner.Choose(board);

        Assert.Equal(damage, plan.Score.Damage);
        Assert.Equal(damage > 0 ? 1 : 0, plan.Actions.Length);
    }

    [Theory]
    [InlineData(0, 24)]
    [InlineData(1, 0)]
    public void Direct_enemy_utility_cast_checks_safety_but_point_zone_skips_aoe_gate(int size, int control)
    {
        var spell = Spell(10, Effect(168, 3)) with
        {
            AllowedCells = ImmutableHashSet.Create(Cell(3)),
            ZonePolicy = new('C', size, 0, 2, false, ImmutableHashSet<long>.Empty)
        };
        var plan = CombatTurnPlanner.Choose(Board(spell));

        Assert.Equal(control, plan.Score.Control);
        Assert.Equal(control > 0 ? 1 : 0, plan.Actions.Length);
    }

    [Fact]
    public void Zone_policy_survives_json_roundtrip_without_callbacks()
    {
        var spell = Spell(10, Effect(100, 30, size: 1)) with
        {
            AllowedCells = ImmutableHashSet.Create(Cell(3)),
            ZonePolicy = new('L', 2, 1, 2, true, ImmutableHashSet.Create(2L, 99L))
        };

        var restored = JsonSerializer.Deserialize<PlannerSpell>(JsonSerializer.Serialize(spell))!;

        Assert.Equal((int)'L', restored.ZonePolicy!.Shape);
        Assert.Equal(2, restored.ZonePolicy.Size);
        Assert.Equal(1, restored.ZonePolicy.MinSize);
        Assert.Equal(2, restored.ZonePolicy.MinEnemyHits);
        Assert.True(restored.ZonePolicy.RequireEnemySafety);
        Assert.True(restored.ZonePolicy.ForbiddenEnemies.SetEquals(new[] { 2L, 99L }));
        Assert.Empty(CombatTurnPlanner.Choose(Board(restored)).Actions);
    }

    [Theory]
    [InlineData(false, 30)]
    [InlineData(true, 0)]
    public void Zone_safety_sees_an_ally_summoned_by_the_previous_cast(bool enforceSafety, int damage)
    {
        var summon = Spell(10, Effect(181, 42), Effect(120, 6, "C")) with
        {
            AllowedCells = ImmutableHashSet.Create(Cell(2))
        };
        var attack = Spell(20, Effect(100, 30, size: 1));
        attack = attack with
        {
            Cast = attack.Cast with { Ap = 6 },
            AllowedCells = ImmutableHashSet.Create(Cell(3)),
            ZonePolicy = enforceSafety ? new('C', 1, 0, 1, true, ImmutableHashSet<long>.Empty) : null
        };
        var board = Board(summon, attack) with { MaxCasts = 2 };

        var plan = CombatTurnPlanner.Choose(board);

        Assert.Equal(damage, plan.Score.Damage);
        Assert.Equal(enforceSafety ? System.Array.Empty<int>() : new[] { 10, 20 }, plan.Actions.Select(a => a.SpellId));
    }

    [Fact]
    public void Zone_aims_at_empty_center_hitting_two_enemies()
    {
        var board = Board(Spell(10, Effect(100, 30, size: 1)));
        board = board with { Fighters = board.Fighters.Add(board.Fighters[1] with { Id = 3, Cell = Cell(5) }) };
        var plan = CombatTurnPlanner.Choose(board);
        Assert.Equal(Cell(4), Assert.Single(plan.Actions).Cell);
        Assert.Equal(60, plan.Score.Damage);
    }

    [Fact]
    public void Equal_zone_alternative_avoids_friendly_fire()
    {
        var board = Board(Spell(10, Effect(100, 30, "a,A", 1)));
        board = board with { Fighters = board.Fighters.Add(new PlannerFighter(3, Cell(2), false, 100, 100)) };
        var plan = CombatTurnPlanner.Choose(board);
        Assert.Equal(Cell(4), Assert.Single(plan.Actions).Cell);
        Assert.Equal(0, plan.Score.FriendlyFire);
    }

    [Fact]
    public void Life_steal_wins_over_plain_damage_at_low_life()
    {
        var board = Board(Spell(10, Effect(100, 40)), Spell(20, Effect(95, 40)));
        board = board with { Fighters = board.Fighters.SetItem(0, board.Fighters[0] with
            { Life = 20, States = ImmutableHashSet<int>.Empty }) };
        var plan = CombatTurnPlanner.Choose(board);
        Assert.Equal(20, Assert.Single(plan.Actions).SpellId);
        Assert.True(plan.Score.Healing > 0);
    }

    [Theory]
    [InlineData(null, 10)]
    [InlineData(76, 10)]
    [InlineData(5920, 20)]
    public void Life_steal_competes_with_damage_only_when_its_healing_is_supported(int? state, int selectedSpell)
    {
        var board = Board(Spell(10, Effect(100, 40)), Spell(20, Effect(95, 36)));
        board = board with { Fighters = board.Fighters.SetItem(0, board.Fighters[0] with
        {
            Life = 20,
            States = state.HasValue ? ImmutableHashSet.Create(state.Value) : null
        }) };

        var plan = CombatTurnPlanner.Choose(board);

        Assert.Equal(selectedSpell, Assert.Single(plan.Actions).SpellId);
        Assert.Equal(selectedSpell == 20 ? 45 : 0, plan.Score.Healing);
        Assert.Equal(selectedSpell == 20 ? 36 : 40, plan.Score.Damage);
    }

    [Fact]
    public void Ap_removal_has_value_and_reduces_expected_reply()
    {
        var board = Board(Spell(10, Effect(100, 30)), Spell(20, Effect(100, 30), Effect(168, 3)));
        var plan = CombatTurnPlanner.Choose(board);
        Assert.Equal(20, Assert.Single(plan.Actions).SpellId);
        Assert.Equal(24, plan.Score.Control);
        Assert.Equal(15, plan.Score.Incoming);
    }

    [Theory]
    [InlineData("presence", false)]
    [InlineData("absence", false)]
    [InlineData("completeness", false)]
    [InlineData("equal-sets", true)]
    [InlineData("explicit-unknown", true)]
    public void Walk_continuation_requires_unchanged_state_knowledge(string change, bool same)
    {
        var before = Board().Fighters[0] with
        {
            States = ImmutableHashSet.Create(42, 76), StatesKnown = false,
            AbsentStates = ImmutableHashSet.Create(43)
        };
        var after = change switch
        {
            "presence" => before with { States = ImmutableHashSet.Create(42) },
            "absence" => before with { AbsentStates = ImmutableHashSet<int>.Empty },
            "completeness" => before with { StatesKnown = true },
            "equal-sets" => before with { States = ImmutableHashSet.Create(76, 42), AbsentStates = ImmutableHashSet.Create(43) },
            "explicit-unknown" => before with { States = null, AbsentStates = null },
            _ => throw new System.ArgumentOutOfRangeException(nameof(change))
        };
        if (change == "explicit-unknown") before = before with { States = null, StatesKnown = null, AbsentStates = null };

        Assert.Equal(same, before.HasSameStateKnowledge(after));
        Assert.Equal(same, after.HasSameStateKnowledge(before));
    }

    [Theory]
    [InlineData(6, 2, 0, 0, 20)]
    [InlineData(6, 2, 1, 16, 13.333333333333334)]
    [InlineData(2, 3, 1, 16, 0)]
    [InlineData(0, 3, 1, 0, 0)]
    [InlineData(null, 3, 1, 24, 15)]
    public void Ap_removal_saturates_at_observed_turn_budget_without_discounting_initial_threat(
        int? turnAp, int removed, int maxCasts, double expectedControl, double expectedIncoming)
    {
        var snapshot = Board(Spell(20, Effect(168, removed))) with { MaxCasts = maxCasts };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1,
            snapshot.Fighters[1] with { TurnAp = turnAp }) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(expectedControl, plan.Score.Control);
        Assert.Equal(expectedIncoming, plan.Score.Incoming, 6);
    }

    [Theory]
    [InlineData(950, "C")]
    [InlineData(950, "A")]
    [InlineData(951, "C")]
    public void State_change_without_a_useful_followup_does_not_spend_ap(int action, string mask)
    {
        var board = Board(Spell(10, Effect(action, 42, mask) with { Value = 42 }));
        board = board with
        {
            Fighters = board.Fighters.Select(f => f with
            {
                States = action == 951 ? ImmutableHashSet.Create(42) : ImmutableHashSet<int>.Empty,
                ExpectedDamage = 0, AttackRange = 0
            }).ToImmutableArray()
        };

        var plan = CombatTurnPlanner.Choose(board);

        Assert.Empty(plan.Actions);
    }

    [Theory]
    [InlineData(true, true, 30)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 30)]
    [InlineData(false, false, 0)]
    public void State_setup_unlocks_a_followup_only_when_the_setup_spell_is_available(
        bool setupAvailable, bool statesKnown, int damage)
    {
        var setup = Spell(10, Effect(950, 42) with { Value = 42 });
        var attack = Spell(20, Effect(100, 30, "A,E42"));
        var board = setupAvailable ? Board(setup, attack) : Board(attack);
        board = board with
        {
            Ap = 6,
            Fighters = board.Fighters.Select(f => f with
            {
                States = statesKnown ? ImmutableHashSet<int>.Empty : null, ExpectedDamage = 0, AttackRange = 0
            }).ToImmutableArray()
        };

        var plan = CombatTurnPlanner.Choose(board);

        Assert.Equal(setupAvailable ? new[] { 10, 20 } : System.Array.Empty<int>(),
            plan.Actions.Select(a => a.SpellId));
        Assert.Equal(damage, plan.Score.Damage);
        if (statesKnown) Assert.Empty(board.Fighters[1].States!);
        else Assert.Null(board.Fighters[1].States);
    }

    [Theory]
    [InlineData("A,E42", false, 30)]
    [InlineData("A,e43", false, 0)]
    [InlineData("A,e43", true, 30)]
    public void Partial_state_observations_prove_only_the_named_presence_or_absence(
        string mask, bool absenceObserved, int damage)
    {
        var board = Board(Spell(20, Effect(100, 30, mask)));
        board = board with { Fighters = board.Fighters.SetItem(1, board.Fighters[1] with
        {
            States = ImmutableHashSet.Create(42), StatesKnown = false,
            AbsentStates = absenceObserved ? ImmutableHashSet.Create(43) : ImmutableHashSet<int>.Empty,
            ExpectedDamage = 0, AttackRange = 0
        }) };

        var plan = CombatTurnPlanner.Choose(board);

        Assert.Equal(damage, plan.Score.Damage);
        Assert.Equal(damage > 0 ? new[] { 20 } : System.Array.Empty<int>(),
            plan.Actions.Select(a => a.SpellId));
    }

    [Theory]
    [InlineData(true, true, 30, 0)]
    [InlineData(false, true, 0, 0)]
    [InlineData(true, false, 0, 0)]
    [InlineData(true, true, 30, 8)]
    public void Explicit_removal_unlocks_absence_followup_from_unknown_states(bool removalAvailable, bool attackAvailable, int damage, int alternatives)
    {
        var removal = Spell(10, Effect(951, 42) with { Value = 42 });
        var attack = Spell(20, Effect(100, 30, "A,e42"));
        var board = Board(new[] { removal, attack }.Where(s =>
            s.Cast.SpellId == 10 ? removalAvailable : attackAvailable)
            .Concat(Enumerable.Range(0, alternatives).Select(i => Spell(100 + i, Effect(100, 1)))).ToArray());
        board = board with
        {
            Ap = 6,
            Fighters = board.Fighters.Select(f => f with
            {
                States = null, ExpectedDamage = 0, AttackRange = 0
            }).ToImmutableArray()
        };

        var plan = CombatTurnPlanner.Choose(board);

        Assert.Equal(removalAvailable && attackAvailable ? new[] { 10, 20 } : System.Array.Empty<int>(),
            plan.Actions.Select(a => a.SpellId));
        Assert.Equal(damage, plan.Score.Damage);
        Assert.Equal(0, plan.Score.Control);
        Assert.Null(board.Fighters[1].States);
    }

    [Fact]
    public void Team_reserved_kill_is_left_for_ally_when_another_enemy_is_reachable()
    {
        var team = new TeamIntentBoard();
        team.Publish("fight", new(4, 1, new[] { new TeamTargetIntent(2, 100, true) }, Cell(6), System.Array.Empty<TeamSupportIntent>(), 1));
        var board = Board(Spell(10, Effect(100, 40)));
        board = board with { TeamView = team.Read("fight", 1, 1),
            Fighters = board.Fighters.Add(board.Fighters[1] with { Id = 3, Cell = Cell(5) }) };
        Assert.Equal(3, Assert.Single(CombatTurnPlanner.Choose(board).Actions).Target);
    }

    [Theory]
    [InlineData(14, "A,B14", 30)]
    [InlineData(4, "A,B14", 0)]
    [InlineData(null, "A,B14", 0)]
    [InlineData(14, "A,b14", 0)]
    [InlineData(4, "A,b14", 30)]
    [InlineData(null, "A,b14", 0)]
    public void Breed_filters_require_observed_identity_in_the_planner(int? breed, string mask, int damage)
    {
        var board = Board(Spell(10, Effect(100, 30, mask)));
        board = board with
        {
            Fighters = board.Fighters.SetItem(1, board.Fighters[1] with
            {
                Breed = breed, AttackRange = 0, ExpectedDamage = 0
            })
        };

        var plan = CombatTurnPlanner.Choose(board);

        Assert.Equal(damage, plan.Score.Damage);
        Assert.Equal(damage == 0 ? 0 : 1, plan.Actions.Length);
    }

    [Fact]
    public void Unknown_side_row_does_not_remove_only_damage_option()
    {
        var spell = Spell(10, Effect(100, 30), Effect(987654, 1));
        Assert.False(spell.Cast.CanPlan);
        var plan = CombatTurnPlanner.Choose(Board(spell));
        Assert.Single(plan.Actions);
        Assert.Equal(30, plan.Score.Damage);
        Assert.Equal(4, plan.Score.Uncertainty);
        Assert.Equal(1, plan.Unmodeled);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, true, false)]
    [InlineData(0, false, true)]
    public void Refund_cannot_enable_an_attack_by_ignoring_an_immediate_unknown_activation(int delay, bool permitted, bool missingSubcast)
    {
        var activation = missingSubcast
            ? Effect(1160, 0, "C") with { DiceNum = 999999, DiceSide = 1 }
            : Effect(1009, 0, "a,P,F3112") with { Delay = delay };
        var refund = Spell(20, Effect(120, 2, "C"), activation);
        refund = refund with { Cast = refund.Cast with { Ap = 1 }, AllowedTargets = ImmutableHashSet.Create(1L) };
        var attack = Spell(10, Effect(100, 50));
        attack = attack with { Cast = attack.Cast with { Ap = 7 } };
        var board = Board(refund, attack) with { Ap = 6, MaxCasts = 2 };

        var plan = CombatTurnPlanner.Choose(board);

        if (!permitted)
        {
            Assert.Empty(plan.Actions);
            Assert.Equal(0, plan.Score.Damage);
        }
        else
        {
            Assert.Equal(new[] { 20, 10 }, plan.Actions.Select(a => a.SpellId));
            Assert.Equal(50, plan.Score.Damage);
        }
    }

    [Fact]
    public void Ally_death_is_forbidden_even_if_enemy_kill_is_profitable()
    {
        var board = Board(Spell(10, Effect(100, 100, "a,A", 1)));
        board = board with { Cells = board.Cells.Where(c => c.Id == Cell(0) || c.Id == Cell(3)).ToImmutableArray(),
            Fighters = board.Fighters.Add(new PlannerFighter(3, Cell(3), false, 1, 100)) };
        Assert.Empty(CombatTurnPlanner.Choose(board).Actions);
    }

    [Fact]
    public void Installed_eniripsa_spells_produce_a_real_damaging_cast()
    {
        if (!System.IO.Directory.Exists(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "Ankama", "Dofus-dofus3", "Dofus_Data", "StreamingAssets", "Content", "Data"))) return;
        var names = new F06App::GivreClone.App.Services.GameNameResolver();
        names.EnsureCombatLoaded();
        var descriptors = new[] { 25870, 25871 }.Select(id => names.GetSpellDescriptor(id, 1)).Where(d => d is not null).ToArray();
        Assert.NotEmpty(descriptors);
        // The runtime assembly owns the descriptors; use its planner types rather than duplicate linked records.
        var cells = Enumerable.Range(0, 560).Select(c => new F06App::GivreClone.App.Services.Combat.PlannerCell(c, true, false, ImmutableArray<int>.Empty)).ToImmutableArray();
        var spells = descriptors.Select(d => new F06App::GivreClone.App.Services.Combat.PlannerSpell(d!, 1, 1,
            ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L, 2L))).ToImmutableArray();
        var fighters = ImmutableArray.Create(new F06App::GivreClone.App.Services.Combat.PlannerFighter(1, Cell(0), false, 100, 100),
            new F06App::GivreClone.App.Services.Combat.PlannerFighter(2, Cell(2), true, 500, 500, MovePoints: 0, ExpectedDamage: 0, ThreatKnown: true));
        var snapshot = new F06App::GivreClone.App.Services.Combat.CombatTurnSnapshot(cells, fighters, 1, 12, 0, spells,
            ImmutableDictionary<int, int>.Empty, SpellLookup: names.GetSpellDescriptor, CasterLevel: 37);
        var plan = F06App::GivreClone.App.Services.Combat.CombatTurnPlanner.Choose(snapshot);
        Assert.Contains(plan.Actions, a => a.Kind == F06App::GivreClone.App.Services.Combat.PlannerActionKind.Cast);
        Assert.True(plan.Score.Damage > 0);
    }
}
