using System.Collections.Immutable;
using System.Linq;
using GivreClone.App.Services;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class CombatTurnPlannerTests
{
    [Theory]
    [InlineData(true, false, 100)]
    [InlineData(false, false, 0)]
    [InlineData(true, true, 0)]
    public void Existing_portals_allow_one_walk_step_then_a_melee_attack_from_the_exit(bool active, bool occupiedExit, int damage)
    {
        // Weapon attacks cannot be projected through portals: reaching melee
        // requires the entity itself to traverse the network.
        var snapshot = Board(Spell(369, SpellEffectKind.Damage, 100, "A", 100, 1)) with
        {
            Mp = 1,
            Portals = ImmutableArray.Create(new ClassMechanics.Portal(1, Cell(1), 1),
                new ClassMechanics.Portal(2, Cell(4), 1, Active: active)),
            UnsafeForcedMovementCells = ImmutableHashSet.Create(Cell(1), Cell(4))
        };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1,
            snapshot.Fighters[1] with { MovePoints = 0, ExpectedDamage = 0 }) };
        if (occupiedExit)
            snapshot = snapshot with { Fighters = snapshot.Fighters.Add(new PlannerFighter(3, Cell(4), false, 100, 100)) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(damage, plan.Score.Damage);
        if (damage == 0)
        {
            Assert.DoesNotContain(plan.Actions, a => a.Kind == PlannerActionKind.Cast);
            return;
        }
        Assert.Equal(PlannerActionKind.Move, plan.Actions[0].Kind);
        Assert.Equal(Cell(1), plan.Actions[0].Cell);
        Assert.Equal(369, plan.Actions[1].SpellId);
        Assert.Equal(Cell(5), plan.Actions[1].Cell);
        Assert.False(plan.Actions[1].ViaPortal);
    }

    [Theory]
    [InlineData(2, 1, 2)]
    [InlineData(1, 1, 1)]
    [InlineData(2, 0, 1)]
    public void Extra_cast_modifier_preserves_the_configured_cast_ceiling(int configuredLimit, int castsLeft, int attacks)
    {
        var boost = Spell(20, SpellEffectKind.Buff, 290, "C", 0, 0);
        boost = boost with { Cast = boost.Cast with { Ap = 1, Effects = ImmutableArray.Create(
            boost.Cast.Effects[0] with { DiceNum = 30, Value = 1, Duration = 1 }) } };
        var shot = Spell(30, SpellEffectKind.Damage, 100, "A", 100, 6) with
        { CastsLeft = castsLeft, TargetCap = 0, CastsLimit = configuredLimit };
        var snapshot = Board(boost, shot) with { Ap = 7, Mp = 0 };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1,
            snapshot.Fighters[1] with { MovePoints = 0, ExpectedDamage = 0 }) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(attacks, plan.Actions.Count(a => a.SpellId == 30));
        Assert.Equal(attacks * 100, plan.Score.Damage);
        Assert.Equal(configuredLimit > castsLeft, plan.Actions.Any(a => a.SpellId == 20));
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(99, false)]
    [InlineData(30, true, 1)]
    [InlineData(30, false, 6)]
    [InlineData(30, true, -2)]
    [InlineData(99, false, -2)]
    public void Spell_specific_range_boost_enables_only_its_targeted_attack(int targetSpell, bool attacks, int minIncrease = 0)
    {
        var boost = Spell(20, SpellEffectKind.Buff, 281, "C", 0, 0);
        boost = boost with { Cast = boost.Cast with { Effects = ImmutableArray.Create(
            boost.Cast.Effects[0] with { DiceNum = targetSpell, Value = 2, Duration = 1 }) } };
        if (minIncrease != 0)
            boost = boost with { Cast = boost.Cast with { Effects = boost.Cast.Effects.Add(
                boost.Cast.Effects[0] with { ActionId = minIncrease > 0 ? 280 : 295, Value = Math.Abs(minIncrease) }) } };
        var shot = Spell(30, SpellEffectKind.Damage, 100, "A", 100, 3);
        if (minIncrease < 0) shot = shot with { Cast = shot.Cast with { MinRange = 6 } };
        var snapshot = Board(boost, shot) with { Ap = 6, Mp = 0 };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1,
            snapshot.Fighters[1] with { MovePoints = 0, ExpectedDamage = 0 }) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        if (attacks)
        {
            Assert.Equal(new[] { 20, 30 }, plan.Actions.Select(a => a.SpellId));
            Assert.Equal(100, plan.Score.Damage);
        }
        else Assert.Empty(plan.Actions);
    }

    [Fact]
    public void Complete_empty_buff_inventory_allows_source_refresh_before_an_attack()
    {
        var buff = Spell(20, SpellEffectKind.Buff, 138, "C", 1000, 0);
        buff = buff with { Cast = buff.Cast with { Effects = buff.Cast.Effects.Insert(0,
            new SpellEffectDescriptor(406, SpellEffectDescriptor.Classify(406), 0, 0, 0, 0, "C", 'P', 0, 0)
                { Value = 20 }) } };
        var snapshot = Board(buff, Spell(30, SpellEffectKind.Damage, 100, "A", 100, 6)) with
            { Ap = 6, Mp = 0, EmptyStatBuffInventory = true };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1,
            snapshot.Fighters[1] with { MovePoints = 0, ExpectedDamage = 0 }) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(new[] { 20, 30 }, plan.Actions.Select(a => a.SpellId));
        Assert.Equal(0, plan.Unmodeled);
        Assert.Equal(500, plan.Score.Damage);
        var unknownInventory = CombatTurnPlanner.Choose(snapshot with { EmptyStatBuffInventory = false });
        Assert.Equal(30, Assert.Single(unknownInventory.Actions).SpellId);
        Assert.Equal(100, unknownInventory.Score.Damage);
        var sourceKnown = CombatTurnPlanner.Choose(snapshot with { EmptyStatBuffInventory = false,
            AbsentStatBuffSources = ImmutableHashSet.Create(20) });
        Assert.Equal(new[] { 20, 30 }, sourceKnown.Actions.Select(a => a.SpellId));
        Assert.Equal(500, sourceKnown.Score.Damage);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void Forced_movement_enables_a_partners_legal_attack_without_a_self_followup(int action)
    {
        var movement = Spell(20, SpellEffectDescriptor.Classify(action), action, "A", action == 6 ? 3 : 0, 6);
        var shot = Spell(30, SpellEffectKind.Damage, 100, "A", 80, 2).Cast with { MinRange = 1 };
        var snapshot = Board(movement) with { Mp = 0, ForcedMovementSafe = true };
        snapshot = snapshot with { Fighters = snapshot.Fighters
            .SetItem(1, snapshot.Fighters[1] with { MovePoints = 0, TurnAp = 0, ExpectedDamage = 0 })
            .Add(new PlannerFighter(3, Cell(action == 6 ? 1 : 2), false, 500, 500,
                TurnAp: 6, OffensiveSpells: ImmutableArray.Create(shot))) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(20, Assert.Single(plan.Actions).SpellId);
        Assert.Equal(0, plan.Score.Damage);
        Assert.True(plan.Score.Position > 0);
    }

    [Fact]
    public void A_partner_without_affordable_attacks_does_not_reward_displacement()
    {
        var snapshot = Board(Spell(20, SpellEffectKind.Swap, 8, "A", 0, 6)) with
            { Mp = 0, ForcedMovementSafe = true };
        snapshot = snapshot with { Fighters = snapshot.Fighters
            .SetItem(1, snapshot.Fighters[1] with { MovePoints = 0, TurnAp = 0, ExpectedDamage = 0 })
            .Add(new PlannerFighter(3, Cell(2), false, 500, 500, TurnAp: 0,
                OffensiveSpells: ImmutableArray.Create(Spell(30, SpellEffectKind.Damage, 100, "A", 80, 2).Cast))) };

        Assert.Empty(CombatTurnPlanner.Choose(snapshot).Actions);
    }

    [Theory]
    [InlineData(6, false, true)]
    [InlineData(0, false, false)]
    [InlineData(6, true, false)]
    public void Replacing_a_tree_is_useful_only_when_the_new_summon_has_a_legal_affordable_attack(
        int summonAp, bool capacityOccupied, bool useful)
    {
        var replacement = Spell(20, SpellEffectKind.Unknown, 405, "a,F5894", 0, 6);
        replacement = replacement with { AllowedTargets = ImmutableHashSet.Create(3L),
            Cast = replacement.Cast with { Effects = ImmutableArray.Create(replacement.Cast.Effects[0]
                with { DiceNum = 5899, DiceSide = 3 }) } };
        var shot = Spell(30, SpellEffectKind.Damage, 100, "A", 80, 2).Cast with { MinRange = 1 };
        var snapshot = Board(replacement) with { Mp = 0, MaxCasts = 1,
            SpellLookup = (id, grade) => id == 30 && grade == 1 ? shot : null,
            SummonLookup = (id, grade, level) => id == 5899 && grade == 3
                ? new GivreClone.App.Services.LotM.SummonGradeCharacteristics(769, summonAp, 5,
                    ImmutableDictionary<int, int>.Empty, new[] { (30, 1) })
                : id == 5894 ? new GivreClone.App.Services.LotM.SummonGradeCharacteristics(513, 0, 0,
                    ImmutableDictionary<int, int>.Empty, SummonCost: 0) : null };
        snapshot = snapshot with { Fighters = snapshot.Fighters
            .SetItem(1, snapshot.Fighters[1] with { MovePoints = 0, TurnAp = 0, ExpectedDamage = 0 })
            .Add(new PlannerFighter(3, Cell(3), false, 513, 513, MonsterTemplate: 5894,
                SummonerId: 1, States: ImmutableHashSet<int>.Empty))
            .Add(new PlannerFighter(5, Cell(1), false, 513, 513, MonsterTemplate: 5894,
                SummonerId: 1, States: ImmutableHashSet<int>.Empty)),
            Characteristics = snapshot.Characteristics.SetItem(CharIds.MaxSummonedCreatures, 1) };
        if (capacityOccupied)
            snapshot = snapshot with { Fighters = snapshot.Fighters.Add(new PlannerFighter(4, Cell(0), false,
                500, 500, MonsterTemplate: 5895, SummonerId: 1, TurnAp: 0)) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        if (useful)
        {
            Assert.Equal(20, Assert.Single(plan.Actions).SpellId);
            Assert.Equal(0, plan.Score.Damage);
            Assert.True(plan.Score.Position > 0);
        }
        else Assert.Empty(plan.Actions);
    }

    [Theory]
    [InlineData(20, 4, true)]
    [InlineData(100, 4, false)]
    [InlineData(20, 1, false)]
    public void Healing_summon_is_prepared_only_for_missing_life_in_its_legal_range(int allyLife, int healRange, bool useful)
    {
        var summon = Spell(20, SpellEffectKind.Summon, 181, "x", 0, 6);
        summon = summon with { AllowedCells = ImmutableHashSet.Create(Cell(3)),
            Cast = summon.Cast with { Effects = ImmutableArray.Create(summon.Cast.Effects[0]
                with { DiceNum = 5897, DiceSide = 3 }) } };
        var heal = Spell(30, SpellEffectKind.Heal, 108, "a", 50, healRange).Cast;
        var snapshot = Board(summon) with { Mp = 0, MaxCasts = 1,
            SpellLookup = (id, grade) => id == 30 ? heal : null,
            SummonLookup = (id, grade, level) => id == 5897
                ? new GivreClone.App.Services.LotM.SummonGradeCharacteristics(500, 6, 0,
                    ImmutableDictionary<int, int>.Empty, new[] { (30, 1) }) : null };
        snapshot = snapshot with { Fighters = snapshot.Fighters
            .SetItem(0, snapshot.Fighters[0] with { Life = allyLife, States = ImmutableHashSet<int>.Empty })
            .SetItem(1, snapshot.Fighters[1] with { MovePoints = 0, TurnAp = 0, ExpectedDamage = 0 }) };
        var plan = CombatTurnPlanner.Choose(snapshot);

        if (useful) Assert.Equal(20, Assert.Single(plan.Actions).SpellId);
        else Assert.Empty(plan.Actions);
        Assert.Equal(useful ? 25 : 0, plan.Score.Position);
        Assert.Equal(0, plan.Score.Healing);
    }

    [Fact]
    public void Transforming_a_summon_requires_capacity_before_the_old_actor_is_removed()
    {
        var replacement = Spell(20, SpellEffectKind.Unknown, 405, "a,E256", 0, 6);
        replacement = replacement with { AllowedTargets = ImmutableHashSet.Create(3L),
            Cast = replacement.Cast with { Effects = ImmutableArray.Create(replacement.Cast.Effects[0]
                with { DiceNum = 5899, DiceSide = 3 }) } };
        var shot = Spell(30, SpellEffectKind.Damage, 100, "A", 80, 2).Cast with { MinRange = 1 };
        var snapshot = Board(replacement) with { Mp = 0, MaxCasts = 1,
            SpellLookup = (id, grade) => id == 30 ? shot : null,
            SummonLookup = (id, grade, level) => id == 5899
                ? new GivreClone.App.Services.LotM.SummonGradeCharacteristics(769, 6, 5,
                    ImmutableDictionary<int, int>.Empty, new[] { (30, 1) }) : null };
        snapshot = snapshot with { Characteristics = snapshot.Characteristics.SetItem(CharIds.MaxSummonedCreatures, 1),
            Fighters = snapshot.Fighters
                .SetItem(1, snapshot.Fighters[1] with { MovePoints = 0, TurnAp = 0, ExpectedDamage = 0 })
                .Add(new PlannerFighter(3, Cell(3), false, 513, 513, MonsterTemplate: 5898,
                    SummonerId: 1, TurnAp: 0, States: ImmutableHashSet.Create(256))) };

        Assert.Empty(CombatTurnPlanner.Choose(snapshot).Actions);
    }

    [Fact]
    public void Zero_cost_summon_can_prepare_an_attack_when_capacity_is_full()
    {
        var summon = Spell(20, SpellEffectKind.Summon, 181, "x", 0, 6);
        summon = summon with { AllowedCells = ImmutableHashSet.Create(Cell(3)),
            Cast = summon.Cast with { Effects = ImmutableArray.Create(summon.Cast.Effects[0]
                with { DiceNum = 5894, DiceSide = 1 }) } };
        var shot = Spell(30, SpellEffectKind.Damage, 100, "A", 80, 2).Cast with { MinRange = 1 };
        var snapshot = Board(summon) with { Mp = 0, MaxCasts = 1,
            SpellLookup = (id, grade) => id == 30 ? shot : null,
            SummonLookup = (id, grade, level) => id == 5894
                ? new GivreClone.App.Services.LotM.SummonGradeCharacteristics(513, 6, 0,
                    ImmutableDictionary<int, int>.Empty, new[] { (30, 1) }, SummonCost: 0) : null };
        snapshot = snapshot with { Characteristics = snapshot.Characteristics.SetItem(CharIds.MaxSummonedCreatures, 1),
            Fighters = snapshot.Fighters
                .SetItem(1, snapshot.Fighters[1] with { MovePoints = 0, TurnAp = 0, ExpectedDamage = 0 })
                .Add(new PlannerFighter(3, Cell(0), false, 500, 500, SummonerId: 1, TurnAp: 0)) };

        Assert.Equal(20, Assert.Single(CombatTurnPlanner.Choose(snapshot).Actions).SpellId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replacing_an_observed_owned_summon_with_the_same_action_does_not_create_potential(bool healer)
    {
        var replacement = Spell(20, SpellEffectKind.Unknown, 405, "a,E256", 0, 6);
        replacement = replacement with { AllowedTargets = ImmutableHashSet.Create(3L),
            Cast = replacement.Cast with { Effects = ImmutableArray.Create(replacement.Cast.Effects[0]
                with { DiceNum = 5899, DiceSide = 3 }) } };
        var shot = healer ? Spell(30, SpellEffectKind.Heal, 108, "a", 50, 4).Cast
            : Spell(30, SpellEffectKind.Damage, 100, "A", 80, 2).Cast with { MinRange = 1 };
        var snapshot = Board(replacement) with { Mp = 0, MaxCasts = 1,
            SpellLookup = (id, grade) => id == 30 && grade == 1 ? shot : null,
            SummonLookup = (id, grade, level) => id == 5899 && grade == 3
                ? new GivreClone.App.Services.LotM.SummonGradeCharacteristics(769, 6, 5,
                    ImmutableDictionary<int, int>.Empty, new[] { (30, 1) }) : null };
        snapshot = snapshot with { Fighters = snapshot.Fighters
            .SetItem(0, snapshot.Fighters[0] with { Life = healer ? 20 : 100, States = ImmutableHashSet<int>.Empty })
            .SetItem(1, snapshot.Fighters[1] with { MovePoints = 0, TurnAp = 0, ExpectedDamage = 0 })
            .Add(new PlannerFighter(3, Cell(3), false, 769, 769, MonsterTemplate: 5899,
                MonsterGrade: 3, SummonerId: 1, TurnAp: 6, States: ImmutableHashSet.Create(256))) };

        Assert.Empty(CombatTurnPlanner.Choose(snapshot).Actions);
    }

    [Fact]
    public void Xelor_does_not_spend_ap_on_a_free_teleport_without_a_productive_follow_up()
    {
        var teleport = Spell(20, SpellEffectKind.Teleport, 4, "C", 0, 4);
        var snapshot = Board(teleport) with { Mp = 0 };
        snapshot = snapshot with { Fighters = snapshot.Fighters
            .SetItem(0, snapshot.Fighters[0] with { Breed = ClassMechanics.XelorBreedId,
                States = ImmutableHashSet<int>.Empty })
            .SetItem(1, snapshot.Fighters[1] with { MovePoints = 0, TurnAp = 0,
                ExpectedDamage = 0, States = ImmutableHashSet<int>.Empty }) };

        Assert.Empty(CombatTurnPlanner.Choose(snapshot).Actions);
    }

    [Fact]
    public void Approach_follows_a_legal_firing_route_even_when_the_first_step_increases_distance()
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 1)) with { Mp = 1 };
        int detour = MapPathfinder.CellFromMapPoint(7, 8);
        snapshot = snapshot with
        {
            Cells = snapshot.Cells.SetItem(0, snapshot.Cells[0] with { Neighbours = ImmutableArray.Create(detour) })
                .Add(new PlannerCell(detour, true, false, ImmutableArray.Create(Cell(0), Cell(1)))),
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with
                { MovePoints = 0, TurnAp = 0, ExpectedDamage = 0 })
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(detour, Assert.Single(plan.Actions).Cell);
        Assert.Equal(PlannerActionKind.Move, plan.Actions[0].Kind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Confirmed_invulnerability_prevents_attacking_or_preparing_a_useless_attack(int mp)
    {
        var board = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 1)) with
        {
            Mp = mp,
            StateDamageRules = ImmutableDictionary<int, GivreClone.App.Services.LotM.SpellStateDamageRule>.Empty
                .Add(9001, new(true, false, false, false))
        };
        board = board with
        {
            Fighters = board.Fighters.SetItem(1, board.Fighters[1] with
            {
                Cell = Cell(mp == 0 ? 1 : 6), ExpectedDamage = 0, AttackRange = 0,
                States = ImmutableHashSet.Create(9001)
            })
        };
        Assert.Empty(CombatTurnPlanner.Choose(board).Actions);
    }

    [Theory]
    [InlineData(30, 9013, 0)]
    [InlineData(1, 1802, 150)]
    public void Finishing_the_last_enemy_beats_saving_overkill_damage_for_next_turn(int life, int maxLife, int expectedDamage)
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 500, 6)) with { Mp = 0 };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1,
            snapshot.Fighters[1] with { Cell = Cell(1), Life = life, MaxLife = maxLife,
                MovePoints = 0, TurnAp = 0, ExpectedDamage = expectedDamage, ThreatKnown = true }) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(10, Assert.Single(plan.Actions).SpellId);
        Assert.Equal(life, plan.Score.Damage);
    }

    [Fact]
    public void Aggressive_prefers_a_nonlethal_hit_over_a_safe_empty_turn()
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 1)) with { TackleImmune = true };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(0, snapshot.Fighters[0] with { Life = 500, MaxLife = 500 })
            .SetItem(1, snapshot.Fighters[1] with { TurnAp = 10 }) };

        var plan = CombatTurnPlanner.Choose(snapshot, 60_000);

        var cast = Assert.Single(plan.Actions, a => a.Kind == PlannerActionKind.Cast);
        Assert.Equal(2, cast.Target);
        Assert.Equal(40, plan.Score.Damage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Aggressive_does_not_take_a_lethal_or_fleeing_hit_over_safety(bool flee)
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 1)) with
        {
            TackleImmune = true, FleeBelowHpPercent = flee ? 90 : 0
        };
        // Lethal: reply 80 >= 60 life. Flee: 60/100 is below the configured 90% threshold.
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(0, snapshot.Fighters[0] with { Life = flee ? 85 : 60 })
            .SetItem(1, snapshot.Fighters[1] with { TurnAp = 10 }) };

        var plan = CombatTurnPlanner.Choose(snapshot, 60_000);

        Assert.DoesNotContain(plan.Actions, a => a.Kind == PlannerActionKind.Cast);
    }

    [Theory]
    [InlineData(FighterVisibility.Invisible, FighterPositionCertainty.LastKnown, null, true)]
    [InlineData(FighterVisibility.Visible, FighterPositionCertainty.Confirmed, null, false)]
    [InlineData(FighterVisibility.Visible, FighterPositionCertainty.Confirmed, 0, true)]
    [InlineData(FighterVisibility.Visible, FighterPositionCertainty.Confirmed, 1, false)]
    public void Last_known_adjacent_enemy_is_not_a_confirmed_tackle_block(FighterVisibility visibility,
        FighterPositionCertainty certainty, int? escapeMp, bool reaches)
    {
        // Only the visible enemy is an authorized target, so the test isolates departure.
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 2)
            with { AllowedTargets = ImmutableHashSet.Create(1L, 2L) }) with { Mp = 2 };
        if (escapeMp is int lost)
            snapshot = snapshot with { EscapeCosts = ImmutableDictionary<int, PlannerEscapeCost>.Empty.Add(Cell(1), new(0, lost)) };
        snapshot = snapshot with { Fighters = snapshot.Fighters
            .SetItem(0, snapshot.Fighters[0] with { Cell = Cell(1), Life = 500, MaxLife = 500 })
            .SetItem(1, snapshot.Fighters[1] with { ExpectedDamage = 0, MovePoints = 0 })
            .Add(new PlannerFighter(3, Cell(0), true, 500, 500, MovePoints: 0, AttackRange: 1, ExpectedDamage: 80,
                ThreatKnown: true, Visibility: visibility, PositionCertainty: certainty, TurnAp: 10)) };

        var plan = CombatTurnPlanner.Choose(snapshot, 60_000);

        var casts = plan.Actions.Where(a => a.Kind == PlannerActionKind.Cast).ToArray();
        Assert.DoesNotContain(casts, a => a.Target == 3);
        if (reaches)
        {
            Assert.Equal(new[] { Cell(2), Cell(3) }, plan.Actions.Where(a => a.Kind == PlannerActionKind.Move).Select(a => a.Cell));
            Assert.Equal(2, Assert.Single(casts).Target);
        }
        else Assert.Empty(casts);
    }

    [Theory]
    [InlineData(FighterVisibility.Visible, 20)]
    [InlineData(FighterVisibility.Invisible, 0)]
    public void Self_invisibility_earns_bounded_defensive_utility_once(FighterVisibility start, double utility)
    {
        var hide = Spell(30, SpellEffectKind.Unknown, 150, "C", 0, 0);
        hide = hide with { Cast = hide.Cast with { Effects = ImmutableArray.Create(new SpellEffectDescriptor(150,
            SpellEffectKind.Unknown, 0, 0, 0, 1, "C", 'P', 0, 0)) } };
        var snapshot = Board(hide) with { Mp = 0 };
        snapshot = snapshot with { Fighters = snapshot.Fighters
            .SetItem(0, snapshot.Fighters[0] with { Visibility = start, States = ImmutableHashSet<int>.Empty,
                PositionCertainty = start == FighterVisibility.Visible ? FighterPositionCertainty.Confirmed : FighterPositionCertainty.LastKnown })
            .SetItem(1, snapshot.Fighters[1] with { Cell = Cell(1), MovePoints = 0, TurnAp = 10 }) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(utility, plan.Score.Shielding);
        Assert.True(plan.Score.Incoming > 0);
        Assert.Equal(utility > 0 ? new[] { 30 } : System.Array.Empty<int>(), plan.Actions.Select(a => a.SpellId));
    }

    [Fact]
    public void Self_invisibility_does_not_displace_a_finishing_attack()
    {
        var hide = Spell(30, SpellEffectKind.Unknown, 150, "C", 0, 0);
        hide = hide with { Cast = hide.Cast with { Effects = ImmutableArray.Create(new SpellEffectDescriptor(150,
            SpellEffectKind.Unknown, 0, 0, 0, 1, "C", 'P', 0, 0)) } };
        var snapshot = Board(hide, Spell(10, SpellEffectKind.Damage, 100, "A", 40, 1)) with { Mp = 0, MaxCasts = 1 };
        snapshot = snapshot with { Fighters = snapshot.Fighters
            .SetItem(0, snapshot.Fighters[0] with { States = ImmutableHashSet<int>.Empty })
            .SetItem(1, snapshot.Fighters[1] with { Cell = Cell(1), Life = 30, MovePoints = 0, TurnAp = 10 }) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(10, Assert.Single(plan.Actions).SpellId);
    }

    private static int Cell(int step) => MapPathfinder.CellFromMapPoint(8 + step, 8);

    private static PlannerSpell Spell(int id, SpellEffectKind kind, int action, string mask,
        int amount, int range = 3, bool approach = true)
    {
        var effect = new SpellEffectDescriptor(action, kind, amount, amount, 0,
            kind == SpellEffectKind.Shield ? 2 : 0, mask, 'P', 0, 0);
        var cast = new SpellCastDescriptor(id, 1, 3, 0, range, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(effect));
        return new(cast, 1, 1, ImmutableDictionary<long, int>.Empty,
            ImmutableHashSet.Create(1L, 2L, 3L), AllowApproach: approach);
    }

    private static CombatTurnSnapshot Board(params PlannerSpell[] spells) => new(
        Enumerable.Range(0, 7).Select(i => new PlannerCell(Cell(i), true, false,
            Enumerable.Range(0, 7).Where(j => System.Math.Abs(i - j) == 1)
                .Select(Cell).ToImmutableArray())).ToImmutableArray(),
        ImmutableArray.Create(new PlannerFighter(1, Cell(0), false, 100, 100),
            new PlannerFighter(2, Cell(5), true, 500, 500, MovePoints: 1,
                AttackRange: 1, ExpectedDamage: 80, ThreatKnown: true)),
        1, 3, 4, spells.ToImmutableArray(), ImmutableDictionary<int, int>.Empty);

    [Theory]
    [InlineData(FighterVisibility.Visible)]
    [InlineData(FighterVisibility.Invisible)]
    public void Invisible_caster_can_still_target_its_own_cell(FighterVisibility visibility)
    {
        var snapshot = Board(Spell(20, SpellEffectKind.Heal, 108, "C", 80, 0)) with { Mp = 0 };
        snapshot = snapshot with { Fighters = snapshot.Fighters
            .SetItem(0, snapshot.Fighters[0] with { Life = 20, Visibility = visibility,
                PositionCertainty = visibility == FighterVisibility.Visible
                    ? FighterPositionCertainty.Confirmed : FighterPositionCertainty.LastKnown })
            .SetItem(1, snapshot.Fighters[1] with { MovePoints = 0, AttackRange = 6 }),
            Characteristics = ImmutableDictionary<int, int>.Empty };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(0,
            snapshot.Fighters[0] with { States = ImmutableHashSet<int>.Empty }) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(20, Assert.Single(plan.Actions).SpellId);
    }

    [Theory]
    [InlineData(true, 8, 0)]
    [InlineData(false, 8, 0)]
    [InlineData(true, 0, 1)]
    [InlineData(false, 0, 1)]
    public void Global_caster_heal_on_empty_cell_requires_an_allowed_beneficiary(bool allowCaster, int flags, int minRange)
    {
        var heal = Spell(20, SpellEffectKind.Heal, 108, "C", 80, 1);
        heal = heal with { Cast = heal.Cast with { Flags = flags, MinRange = minRange } };
        if (!allowCaster) heal = heal with { AllowedTargets = ImmutableHashSet<long>.Empty };
        var snapshot = Board(heal) with { Mp = 0 };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(0,
            snapshot.Fighters[0] with { Life = 20, States = ImmutableHashSet<int>.Empty }) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        if (!allowCaster)
        {
            Assert.Empty(plan.Actions);
            Assert.Equal(0, plan.Score.Healing);
            return;
        }
        var cast = Assert.Single(plan.Actions);
        Assert.Equal(20, cast.SpellId);
        Assert.Equal(Cell(1), cast.Cell);
        Assert.Equal(200, plan.Score.Healing);
    }

    [Theory]
    [InlineData(true, false, 50)]
    [InlineData(false, false, 100)]
    [InlineData(true, true, 50)]
    public void Valid_ally_center_preserves_caster_healing_when_the_ally_is_incurable(
        bool incurable, bool allowCaster, int expectedHealing)
    {
        var heal = Spell(20, SpellEffectKind.Heal, 108, "C", 20, 1);
        heal = heal with
        {
            Cast = heal.Cast with { MinRange = 1, Effects = heal.Cast.Effects.Add(
                new SpellEffectDescriptor(108, SpellEffectKind.Heal, 20, 20, 0, 0, "g", 'P', 0, 0)) },
            AllowedTargets = allowCaster ? ImmutableHashSet.Create(1L, 3L) : ImmutableHashSet.Create(3L)
        };
        var snapshot = Board(heal) with { Mp = 0 };
        snapshot = snapshot with { Fighters = snapshot.Fighters
            .SetItem(0, snapshot.Fighters[0] with { Life = 20, States = ImmutableHashSet<int>.Empty })
            .SetItem(1, snapshot.Fighters[1] with { MovePoints = 0, AttackRange = 0, ExpectedDamage = 0 })
            .Add(new PlannerFighter(3, Cell(1), false, 20, 100,
                States: incurable ? ImmutableHashSet.Create(76) : ImmutableHashSet<int>.Empty)) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        var cast = Assert.Single(plan.Actions);
        Assert.Equal(20, cast.SpellId);
        Assert.Equal(Cell(1), cast.Cell);
        Assert.Equal(expectedHealing, plan.Score.Healing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Target_hidden_by_a_cast_is_not_available_to_a_later_targeted_cast(bool hide)
    {
        // Synthetic setup makes the follow-up legal only after the first cast.
        // It does not claim that live Invisibilite deals damage or applies state42.
        var setup = Spell(10, SpellEffectKind.Damage, 100, "A", 100, 6);
        var effects = setup.Cast.Effects.Add(new SpellEffectDescriptor(950,
            SpellEffectKind.Unknown, 42, 42, 0, 0, "A", 'P', 0, 0));
        if (hide) effects = effects.Add(new SpellEffectDescriptor(150,
            SpellEffectKind.Unknown, 0, 0, 0, 1, "a,A", 'P', 0, 0, Triggers: "I"));
        setup = setup with { Cast = setup.Cast with { Effects = effects } };
        var followup = Spell(20, SpellEffectKind.Damage, 100, "A,E42", 10, 6);
        var snapshot = Board(setup, followup) with { Ap = 6, Mp = 0, MaxCasts = 2 };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1,
            snapshot.Fighters[1] with { States = ImmutableHashSet<int>.Empty,
                MovePoints = 0, AttackRange = 0, ExpectedDamage = 0 }) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(hide ? new[] { 10 } : new[] { 10, 20 },
            plan.Actions.Select(a => a.SpellId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    public void Aggressive_actor_stays_when_walking_cannot_reduce_threat_or_enable_an_attack(int damage)
    {
        var snapshot = Board() with { Ap = 0, Mp = 2 };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(0, snapshot.Fighters[0] with { Cell = Cell(2) })
                .SetItem(1, snapshot.Fighters[1] with { ExpectedDamage = damage, MovePoints = 0 })
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Empty(plan.Actions);
    }

    [Fact]
    public void Zero_damage_cast_does_not_earn_a_follow_up_position_reward()
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 98, "A", 40, 6)) with
        {
            Mp = 0,
            Characteristics = ImmutableDictionary<int, int>.Empty.Add(CharIds.Agility, -400)
        };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with { ExpectedDamage = 0 })
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Empty(plan.Actions);
    }

    [Theory]
    [InlineData(false, null, 70)]
    [InlineData(true, null, 40)]
    [InlineData(true, 2, 30)]
    public void Element_challenge_restricts_the_entire_cast_chain(bool singleElement, int? lockedElement, int damage)
    {
        var neutral = Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6) with { ChallengeElement = 0 };
        var fire = Spell(20, SpellEffectKind.Damage, 99, "A", 30, 6) with { ChallengeElement = 2 };
        var snapshot = Board(neutral, fire) with { Ap = 6, Mp = 0, SingleElement = singleElement, LockedElement = lockedElement };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with { ExpectedDamage = 0 })
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(damage, plan.Score.Damage);
        Assert.Equal(singleElement ? 1 : 2, plan.Actions.Length);
    }

    [Theory]
    [InlineData(2822, 2, 20)]
    [InlineData(2828, 2, 20)]
    [InlineData(2832, 3, 10)]
    [InlineData(2890, 3, 10)]
    [InlineData(2822, 0, 0)]
    [InlineData(2828, 0, 0)]
    [InlineData(2832, 0, 0)]
    [InlineData(2890, 0, 0)]
    public void Element_challenge_uses_dynamic_damage_element_instead_of_raw_summary(int action, int locked, double damage)
    {
        var attack = Spell(10, SpellEffectKind.Damage, action, "A", 10, 6) with { ChallengeElement = 0 };
        var snapshot = Board(attack) with
        {
            Mp = 0, SingleElement = true, LockedElement = locked,
            Characteristics = ImmutableDictionary<int, int>.Empty
                .Add(CharIds.Strength, 50).Add(CharIds.Intelligence, 100)
                .Add(CharIds.Chance, 0).Add(CharIds.Agility, 75)
        };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1,
            snapshot.Fighters[1] with { ExpectedDamage = 0 }) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(damage, plan.Score.Damage);
        Assert.Equal(damage > 0 ? new[] { 10 } : System.Array.Empty<int>(), plan.Actions.Select(a => a.SpellId));
    }

    [Theory]
    [InlineData(1, 30, new[] { 20, 10 })]
    [InlineData(2, 20, new[] { 10 })]
    public void Element_challenge_rechecks_dynamic_element_after_a_stat_boost(int locked, double damage, int[] spells)
    {
        var attack = Spell(10, SpellEffectKind.Damage, 2822, "A", 10, 6) with { ChallengeElement = 2 };
        var boost = Spell(20, SpellEffectKind.Buff, 118, "C", 200, 0);
        var snapshot = Board(attack, boost) with
        {
            Ap = 6, Mp = 0, SingleElement = true, LockedElement = locked,
            Characteristics = ImmutableDictionary<int, int>.Empty.Add(CharIds.Intelligence, 100)
        };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1,
            snapshot.Fighters[1] with { ExpectedDamage = 0 }) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(damage, plan.Score.Damage);
        Assert.Equal(spells, plan.Actions.Select(a => a.SpellId));
    }

    [Theory]
    [InlineData(false, 500, 0, 80, new[] { 10, 20 })]
    [InlineData(true, 500, 0, 40, new[] { 10 })]
    [InlineData(true, 40, 0, 80, new[] { 10, 20 })]
    [InlineData(true, 500, 3, 40, new[] { 20 })]
    [InlineData(true, 40, 2, 80, new[] { 10, 20 })]
    [InlineData(true, 0, 2, 40, new[] { 20 })]
    [InlineData(true, 500, 99, 40, new[] { 10 })]
    public void Focus_challenge_allows_switching_only_after_the_first_enemy_dies(bool focus, int life,
        long initialTarget, double damage, int[] spells)
    {
        var first = Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6) with
        { AllowedTargets = ImmutableHashSet.Create(2L) };
        var second = Spell(20, SpellEffectKind.Damage, 100, "A", 40, 6) with
        { AllowedTargets = ImmutableHashSet.Create(3L) };
        var snapshot = Board(first, second) with
        { Ap = 6, Mp = 0, FocusTargets = focus, FocusTarget = initialTarget };
        snapshot = snapshot with { Fighters = snapshot.Fighters
            .SetItem(1, snapshot.Fighters[1] with { Life = life, ExpectedDamage = 0 })
            .Add(new PlannerFighter(3, Cell(6), true, 500, 500, ExpectedDamage: 0)) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(damage, plan.Score.Damage);
        Assert.Equal(spells, plan.Actions.Select(a => a.SpellId));
    }

    // Live 02/11 20:33 : en duo, la menace d'un monstre était comptée sur CHAQUE allié -> fuite au lieu d'attaquer.
    [Fact]
    public void One_enemy_reply_is_charged_once_even_with_two_exposed_allies()
    {
        var solo = Board();
        var duo = solo with
        {
            Fighters = solo.Fighters.Insert(1, new PlannerFighter(3, Cell(1), false, 100, 100))
        };
        var soloPlan = CombatTurnPlanner.Choose(solo with { MaxCasts = 0 });
        var duoPlan = CombatTurnPlanner.Choose(duo with { MaxCasts = 0 });
        Assert.Equal(soloPlan.Score.Incoming, duoPlan.Score.Incoming, 3);
    }

    [Fact]
    public void Approach_does_not_prepare_an_attack_on_an_excluded_target()
    {
        var attack = Spell(10, SpellEffectKind.Damage, 100, "A", 40, 1) with
        {
            AllowedTargets = ImmutableHashSet.Create(3L)
        };
        var board = Board(attack) with { Mp = 3 };
        board = board with
        {
            Fighters = board.Fighters.SetItem(1, board.Fighters[1] with
            {
                Cell = Cell(6), ExpectedDamage = 0, AttackRange = 0
            })
        };

        var plan = CombatTurnPlanner.Choose(board);

        Assert.Empty(plan.Actions);
    }

    [Theory]
    [InlineData(100, CharIds.ReductionNeutral, 0, true)]
    [InlineData(100, CharIds.ReductionNeutral, 1000, false)]
    [InlineData(99, CharIds.ReductionFire, 1000, false)]
    [InlineData(97, CharIds.ReductionEarth, 1000, false)]
    [InlineData(96, CharIds.ReductionWater, 1000, false)]
    [InlineData(98, CharIds.ReductionAir, 1000, false)]
    [InlineData(89, CharIds.ReductionNeutral, 0, true)]
    [InlineData(89, CharIds.ReductionNeutral, 1000, false)]
    [InlineData(279, CharIds.ReductionNeutral, 0, false)]
    public void Approach_requires_damage_that_survives_observed_flat_reduction(int action, int characteristic, int reduction, bool shouldApproach)
    {
        var board = Board(Spell(10, SpellEffectKind.Damage, action, "A", 40, 1)) with { Mp = 3 };
        board = board with
        {
            Fighters = board.Fighters.SetItem(1, board.Fighters[1] with
            {
                Cell = Cell(6), ExpectedDamage = 0, AttackRange = 0,
                Characteristics = ImmutableDictionary<int, int>.Empty.Add(characteristic, reduction)
            })
        };

        var plan = CombatTurnPlanner.Choose(board);

        if (shouldApproach) Assert.Contains(plan.Actions, action => action.Kind == PlannerActionKind.Move);
        else Assert.Empty(plan.Actions);
    }

    [Fact]
    public void Approach_does_not_prepare_an_attack_outside_its_target_health_gate()
    {
        var attack = Spell(10, SpellEffectKind.Damage, 100, "A", 40, 1) with { TargetHpMax = 50 };
        var board = Board(attack) with { Mp = 3 };
        board = board with
        {
            Fighters = board.Fighters.SetItem(1, board.Fighters[1] with
            {
                Cell = Cell(6), ExpectedDamage = 0, AttackRange = 0
            })
        };

        Assert.Empty(CombatTurnPlanner.Choose(board).Actions);
    }

    [Theory]
    [InlineData("A,E9001", false, false, false)]
    [InlineData("A,E9001", true, false, true)]
    [InlineData("A,e9001", false, false, true)]
    [InlineData("A,e9001", true, false, false)]
    [InlineData("A,*E9001", false, true, false)]
    [InlineData("A,*E9001", true, true, true)]
    [InlineData("A,*e9001", false, true, true)]
    [InlineData("A,*e9001", true, true, false)]
    public void Approach_requires_the_damage_rows_target_state(string mask, bool hasState, bool casterState, bool shouldApproach)
    {
        var board = Board(Spell(10, SpellEffectKind.Damage, 100, mask, 40, 1)) with { Mp = 3 };
        board = board with
        {
            Fighters = board.Fighters.SetItem(1, board.Fighters[1] with
            {
                Cell = Cell(6), ExpectedDamage = 0, AttackRange = 0
            })
        };
        int actor = casterState ? 0 : 1;
        board = board with
        {
            Fighters = board.Fighters.SetItem(actor, board.Fighters[actor] with
            {
                States = hasState ? ImmutableHashSet.Create(9001) : ImmutableHashSet<int>.Empty,
                AbsentStates = hasState ? ImmutableHashSet<int>.Empty : ImmutableHashSet.Create(9001)
            })
        };

        var plan = CombatTurnPlanner.Choose(board);

        if (shouldApproach) Assert.Contains(plan.Actions, action => action.Kind == PlannerActionKind.Move);
        else Assert.Empty(plan.Actions);
    }

    // Live 02/11 20:43 / 21:04 : sans sort à portée ni danger, un agressif reculait au lieu de se placer pour le tour suivant.
    [Theory]
    [InlineData(CombatStyle.Aggressive, CombatMovementPolicy.Standard, true, true)]
    [InlineData(CombatStyle.Fleeing, CombatMovementPolicy.Standard, true, false)]
    [InlineData(CombatStyle.Passive, CombatMovementPolicy.Standard, true, false)]
    [InlineData(CombatStyle.Aggressive, CombatMovementPolicy.NeverApproach, true, false)]
    [InlineData(CombatStyle.Aggressive, CombatMovementPolicy.Standard, false, false)]
    public void Without_reachable_target_or_threat_style_decides_whether_to_close_in(
        CombatStyle style, CombatMovementPolicy policy, bool approach, bool closer)
    {
        // Range 1, enemy 6 cells away with 3 MP : no hit this turn, but a 3-cell walk puts it in reach next turn.
        var board = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 1, approach)) with
        {
            Style = style, MovementPolicy = policy, Mp = 3
        };
        board = board with
        {
            Fighters = board.Fighters.SetItem(1, board.Fighters[1] with { Cell = Cell(6), ExpectedDamage = 0, AttackRange = 0 })
        };
        var plan = CombatTurnPlanner.Choose(board);
        Assert.DoesNotContain(plan.Actions, a => a.Kind == PlannerActionKind.Cast);
        int end = plan.Actions.LastOrDefault(a => a.Kind == PlannerActionKind.Move)?.Cell ?? Cell(0);
        Assert.Equal(closer ? Cell(3) : Cell(0), end);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public void Next_turn_setup_does_not_enter_enemy_range_for_a_self_damage_bonus(int selfDamage)
    {
        var attack = Spell(10, SpellEffectKind.Damage, 100, "A", 40, 1);
        attack = attack with { Cast = attack.Cast with
        {
            Effects = attack.Cast.Effects.Add(new SpellEffectDescriptor(100, SpellEffectKind.Damage,
                selfDamage, selfDamage, 0, 0, "C", 'P', 0, 0))
        } };
        var snapshot = Board(attack) with { Ap = 0, Mp = 1 };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with
            {
                MovePoints = 0, AttackRange = 4, ExpectedDamage = 10
            })
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Empty(plan.Actions);
        Assert.Equal(0, plan.Score.Incoming);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Unknown_max_life_only_allows_unrestricted_hp_conditions(bool self, bool unrestricted)
    {
        var spell = Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6);
        if (!unrestricted)
            spell = self ? spell with { SelfHpMax = 60 } : spell with { TargetHpMax = 60 };
        var snapshot = Board(spell) with { Mp = 0 };
        int actor = self ? 0 : 1;
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(actor, snapshot.Fighters[actor] with
            {
                Life = 1_000_000, MaxLife = 0
            })
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(unrestricted, plan.Actions.Any(a => a.Kind == PlannerActionKind.Cast));
    }

    // 5x5 board around map cell 300: the enemy sits on its point, a wall one column east; it cannot move and hits at range 4.
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void Negative_state_mask_requires_observed_absence(bool known, bool present, bool attacks)
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A,e42", 40, 6)) with { Mp = 0 };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with
            {
                States = known ? (present ? ImmutableHashSet.Create(42) : ImmutableHashSet<int>.Empty) : null
            })
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(attacks, plan.Actions.Any(a => a.Kind == PlannerActionKind.Cast));
    }

    [Theory]
    [InlineData(123, true)]
    [InlineData(124, false)]
    [InlineData(0, false)]
    public void Template_filtered_damage_uses_the_observed_monster_identity(int template, bool attacks)
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A,F123", 40, 6)) with { Mp = 0 };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with { MonsterTemplate = template })
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(attacks, plan.Actions.Any(a => a.Kind == PlannerActionKind.Cast));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, true)]
    [InlineData(0, false)]
    public void Summon_filtered_damage_uses_the_observed_owner(int owner, bool attacks)
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "I,p", 40, 6)) with { Mp = 0 };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with { SummonerId = owner == 0 ? null : owner })
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(attacks, plan.Actions.Any(a => a.Kind == PlannerActionKind.Cast));
    }

    [Fact]
    public void Exposed_caster_ends_its_turn_behind_a_line_of_sight_blocker()
    {
        var (cx, cy) = MapPathfinder.MapPointOf(300);
        int P(int x, int y) => MapPathfinder.CellFromMapPoint(cx + x, cy + y);
        var wall = new[] { P(1, -1), P(1, 0), P(1, 1) }.ToHashSet();
        var pts = (from x in Enumerable.Range(0, 5) from y in Enumerable.Range(-2, 5) select (x, y)).ToArray();
        var cells = pts.Select(p => new PlannerCell(P(p.x, p.y), !wall.Contains(P(p.x, p.y)), wall.Contains(P(p.x, p.y)),
            pts.Where(q => System.Math.Abs(q.x - p.x) + System.Math.Abs(q.y - p.y) == 1).Select(q => P(q.x, q.y)).ToImmutableArray()))
            .ToImmutableArray();
        int enemy = P(0, 0), start = P(0, -2);
        Assert.DoesNotContain(-1, pts.Select(p => P(p.x, p.y)));
        bool Seen(int c) => MapPathfinder.CellDistance(enemy, c) <= 4
            && MapPathfinder.HasLineOfSight(enemy, c, x => wall.Contains(x) || !cells.Any(k => k.Id == x), _ => false);
        Assert.True(Seen(start));
        var snapshot = new CombatTurnSnapshot(cells,
            ImmutableArray.Create(new PlannerFighter(1, start, false, 100, 100),
                new PlannerFighter(2, enemy, true, 500, 500, MovePoints: 0, AttackRange: 4, ExpectedDamage: 60, ThreatKnown: true)),
            1, 0, 4, ImmutableArray<PlannerSpell>.Empty, ImmutableDictionary<int, int>.Empty);

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.False(Seen(plan.Actions.Last(a => a.Kind == PlannerActionKind.Move).Cell), plan.ToString());
        Assert.Equal(0, plan.Score.Incoming);
    }

    [Fact]
    public void Team_protects_exposed_ally_and_publishes_planned_support()
    {
        var heal = Spell(20, SpellEffectKind.Heal, 108, "g", 60, 6);
        var attack = Spell(10, SpellEffectKind.Damage, 100, "A", 80, 6);
        var board = Board(heal, attack) with { Mp = 0 };
        var team = new TeamIntentBoard();
        team.Publish("fight", new TeamIntent(3, 1, System.Array.Empty<TeamTargetIntent>(), Cell(1),
            System.Array.Empty<TeamSupportIntent>(), 1));
        board = board with { Fighters = board.Fighters.Add(new PlannerFighter(3, Cell(1), false, 4, 100,
                States: ImmutableHashSet<int>.Empty)),
            TeamView = team.Read("fight", 1, 1) };
        var plan = CombatTurnPlanner.Choose(board);
        Assert.Contains(plan.Actions, a => a.SpellId == 20 && a.Target == 3);
        Assert.Contains(plan.Intent!.Support, s => s.AllyActorId == 3 && s.Heal > 0);
    }

    [Theory]
    [InlineData(111, 12, "g")]
    [InlineData(120, 12, "g")]
    [InlineData(128, 6, "g")]
    [InlineData(111, 0, "C")]
    [InlineData(120, 0, "C")]
    [InlineData(128, 0, "C")]
    public void Resource_only_rotation_boosts_an_ally_but_not_an_idle_caster(int action, double control, string mask)
    {
        var boost = Spell(20, SpellEffectKind.Buff, action, mask, 2, 6);
        var board = Board(boost) with { Mp = 0, Style = CombatStyle.Passive };
        board = board with { Fighters = board.Fighters.Add(new PlannerFighter(3, Cell(1), false, 100, 100)) };

        Assert.True(AutoSpellRotation.ShouldUsePlanner(new[] { boost.Cast }));
        var plan = CombatTurnPlanner.Choose(board);

        if (mask == "C")
        {
            Assert.Empty(plan.Actions);
            Assert.Equal(0, plan.Score.Control);
            return;
        }
        var cast = Assert.Single(plan.Actions);
        Assert.Equal(PlannerActionKind.Cast, cast.Kind);
        Assert.Equal(20, cast.SpellId);
        Assert.Equal(3, cast.Target);
        Assert.Equal(control, plan.Score.Control);
    }

    [Theory]
    [InlineData(111, 168, 1)]
    [InlineData(111, 168, 3)]
    [InlineData(128, 169, 1)]
    [InlineData(128, 169, 3)]
    [InlineData(111, 168, 1, 1, 6)]
    [InlineData(128, 169, 1, 1, 3)]
    [InlineData(111, 169, 1, 2, 12)]
    [InlineData(128, 168, 1, 2, 6)]
    [InlineData(111, 168, 1, 2, 0, true)]
    [InlineData(128, 169, 3, 2, 0, true)]
    [InlineData(111, 168, 1, 3, 8, true)]
    [InlineData(128, 169, 1, 3, 5, true)]
    public void Point_control_reward_uses_net_change_per_recipient_and_resource(int gainAction, int lossAction, int casts,
        int lossAmount = 2, double expectedControl = 0, bool enemy = false)
    {
        string mask = enemy ? "A" : "g";
        var boost = Spell(20, SpellEffectKind.Buff, gainAction, mask, 2, 6);
        boost = boost with
        {
            CastsLeft = casts, TargetCap = casts,
            Cast = boost.Cast with
            {
                MaxPerTurn = casts, MaxPerTarget = casts,
                Effects = boost.Cast.Effects.Add(new SpellEffectDescriptor(
                    lossAction, SpellEffectKind.Debuff, lossAmount, lossAmount, 0, 0, mask, 'P', 0, 0))
            }
        };
        var board = Board(boost) with { Ap = casts * 3, Mp = 0, MaxCasts = casts, Style = CombatStyle.Passive };
        board = board with
        {
            Fighters = board.Fighters.SetItem(1, board.Fighters[1] with
                { ExpectedDamage = 0, TurnAp = 2, MovePoints = 2 })
                .Add(new PlannerFighter(3, Cell(1), false, 100, 100, MovePoints: 2, TurnAp: 2))
        };

        var plan = CombatTurnPlanner.Choose(board);

        Assert.Equal(expectedControl, plan.Score.Control);
        if (expectedControl == 0) Assert.Empty(plan.Actions);
        else Assert.Equal(20, Assert.Single(plan.Actions).SpellId);
    }

    [Fact]
    public void Team_healer_prioritizes_the_unprotected_ally_using_observed_shield()
    {
        var heal = Spell(20, SpellEffectKind.Heal, 108, "g", 20, 6) with
        {
            AllowedTargets = ImmutableHashSet.Create(3L, 4L)
        };
        var snapshot = Board(heal) with { Mp = 0, TeamView = TeamIntentView.Empty };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with { ExpectedDamage = 0 })
                .Add(new PlannerFighter(3, Cell(1), false, 20, 100, Shield: 80, States: ImmutableHashSet<int>.Empty))
                .Add(new PlannerFighter(4, Cell(2), false, 20, 100, States: ImmutableHashSet<int>.Empty))
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(4, Assert.Single(plan.Actions).Target);
        var support = Assert.Single(plan.Intent!.Support);
        Assert.Equal(4, support.AllyActorId);
        Assert.Equal(20, support.Heal);
        Assert.Equal(20, support.LifeBefore);
        Assert.Equal(0, support.ShieldBefore);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(96, 4)]
    public void Team_healer_spends_its_cast_on_missing_healing_not_another_allies_reserved_heal(int promisedHeal, long expectedAlly)
    {
        var heal = Spell(20, SpellEffectKind.Heal, 108, "g", 40, 6) with
        {
            AllowedTargets = ImmutableHashSet.Create(3L, 4L)
        };
        var team = new TeamIntentBoard();
        team.Publish("fight", new TeamIntent(5, 1, System.Array.Empty<TeamTargetIntent>(), Cell(6),
            new[] { new TeamSupportIntent(3, Heal: promisedHeal, LifeBefore: 4) }, 1));
        var snapshot = Board(heal) with { Mp = 0, TeamView = team.Read("fight", 1, 1) };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with { ExpectedDamage = 0 })
                .Add(new PlannerFighter(3, Cell(1), false, 4, 100, States: ImmutableHashSet<int>.Empty))
                .Add(new PlannerFighter(4, Cell(2), false, 40, 100, States: ImmutableHashSet<int>.Empty))
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(expectedAlly, Assert.Single(plan.Actions).Target);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(80, 0)]
    public void Team_shield_reward_excludes_promised_protection_without_assuming_it_for_survival(int promisedShield, double expectedReward)
    {
        var shield = Spell(20, SpellEffectKind.Shield, 1040, "g", 40, 6) with
        {
            AllowedTargets = ImmutableHashSet.Create(3L)
        };
        var team = new TeamIntentBoard();
        team.Publish("fight", new TeamIntent(5, 1, System.Array.Empty<TeamTargetIntent>(), Cell(6),
            new[] { new TeamSupportIntent(3, Shield: promisedShield, ShieldBefore: 0) }, 1));
        var snapshot = Board(shield) with { Mp = 0, TeamView = team.Read("fight", 1, 1) };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with { MovePoints = 0 })
                .Add(new PlannerFighter(3, Cell(4), false, 40, 100))
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(3, Assert.Single(plan.Actions).Target);
        Assert.Equal(expectedReward, plan.Score.Shielding);
        Assert.Equal(140, plan.Score.Incoming);
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, 100, true)]
    public void Placement_reserves_end_cells_only_for_allies_still_alive_on_the_board(bool present, int life, bool move)
    {
        var team = new TeamIntentBoard();
        team.Publish("fight", new TeamIntent(3, 1, System.Array.Empty<TeamTargetIntent>(), Cell(0),
            System.Array.Empty<TeamSupportIntent>(), 1));
        var snapshot = Board() with { Mp = 1, MaxCasts = 0, TeamView = team.Read("fight", 1, 1) };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with { ExpectedDamage = 0 })
        };
        if (present) snapshot = snapshot with { Fighters = snapshot.Fighters.Add(new PlannerFighter(3, Cell(2), false, life, 100)) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(move ? new[] { Cell(1) } : System.Array.Empty<int>(), plan.Actions.Select(a => a.Cell));
        Assert.Equal(move ? Cell(1) : Cell(0), plan.Intent!.EndCell);
    }

    [Theory]
    [InlineData(111, 2, true)]
    [InlineData(128, 4, true)]
    [InlineData(118, 200, true)]
    [InlineData(115, 7, true)]
    [InlineData(138, 100, true)]
    [InlineData(418, 40, true)]
    [InlineData(118, 200, false)]
    [InlineData(115, 7, false)]
    [InlineData(138, 100, false)]
    [InlineData(418, 40, false)]
    public void Unspent_self_resource_gain_does_not_replace_an_available_attack(int action, int amount, bool reachable)
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 10, reachable ? 6 : 1),
            Spell(20, SpellEffectKind.Buff, action, "C", amount, 0)) with
        {
            Mp = 0, MaxCasts = 1, MovementPolicy = CombatMovementPolicy.NeverApproach
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        if (reachable) Assert.Equal(10, Assert.Single(plan.Actions).SpellId);
        else Assert.Empty(plan.Actions);
        Assert.Equal(0, plan.Score.Control);
    }

// Régression live 02/11 18:52 : budget épuisé par l'exploration des déplacements -> aucun lancer (défaite en 11 tours).
    [Fact]
    public void Tiny_budget_still_casts_an_attack_already_in_range()
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6));
        var plan = CombatTurnPlanner.Choose(snapshot, maxWork: 40);
        Assert.Contains(plan.Actions, a => a.Kind == PlannerActionKind.Cast && a.SpellId == 10);
    }

    [Fact]
    public void Bounded_search_reaches_a_firing_origin_before_spending_the_budget_on_empty_area_casts()
    {
        var spells = Enumerable.Range(10, 8).Select(id =>
        {
            var spell = Spell(id, SpellEffectKind.Damage, 100, "A", 40, 1);
            return spell with { Cast = spell.Cast with
            {
                Effects = ImmutableArray.Create(spell.Cast.Effects[0] with { Shape = 'C', Size = 1 })
            } };
        }).ToArray();
        var snapshot = Board(spells) with { Style = CombatStyle.Fleeing };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1,
            snapshot.Fighters[1] with { ExpectedDamage = 0 }) };

        var plan = CombatTurnPlanner.Choose(snapshot, maxWork: 300);

        Assert.Contains(plan.Actions, a => a.Kind == PlannerActionKind.Cast);
        Assert.Equal(40, plan.Score.Damage);
        Assert.InRange(plan.Work, 1, 300);
    }

    [Fact]
    public void Constrained_end_cell_is_reached_before_cast_search_exhausts_the_budget()
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6)) with
        {
            Ap = 11, Mp = 6, EndCells = ImmutableHashSet.Create(Cell(1))
        };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with
                { MovePoints = 0, AttackRange = 0, ExpectedDamage = 0 })
        };

        var plan = CombatTurnPlanner.Choose(snapshot, maxWork: 8);

        Assert.Contains(plan.Actions, a => a.Kind == PlannerActionKind.Move);
        Assert.Equal(Cell(1), plan.Intent!.EndCell);
        Assert.True(plan.BudgetExhausted);
    }

    [Fact]
    public void Unreachable_challenge_end_cells_do_not_cancel_available_attacks()
    {
        // Live 04/11 23:30 (Collant): no compliant end cell was reachable, the planner
        // returned [] with 13 AP beside enemies and the turn ended without a cast.
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6)) with
        {
            Ap = 6, Mp = 0, EndCells = ImmutableHashSet.Create(Cell(3))
        };

        var plan = CombatTurnPlanner.Choose(snapshot, maxWork: 60_000);

        Assert.Contains(plan.Actions, a => a.Kind == PlannerActionKind.Cast && a.SpellId == 10);
        Assert.True(plan.Score.Damage > 0);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public void Reachable_challenge_end_cell_does_not_cancel_available_attacks(int ap)
    {
        // The escape consumes 3 AP: with 3 AP, walking is compliant but no
        // casting endpoint is; with 6 AP, casting then escaping preserves both.
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6)) with
        {
            Ap = ap, Mp = 1, EndCells = ImmutableHashSet.Create(Cell(1)),
            EscapeCosts = ImmutableDictionary<int, PlannerEscapeCost>.Empty
                .Add(Cell(0), new(3, 0))
        };
        snapshot = snapshot with
        {
            Cells = snapshot.Cells.Add(new PlannerCell(MapPathfinder.CellFromMapPoint(8, 7), true, false, ImmutableArray<int>.Empty)),
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with
                { Cell = MapPathfinder.CellFromMapPoint(8, 7), MovePoints = 0, AttackRange = 0, ExpectedDamage = 0 })
        };
        Assert.InRange(snapshot.Fighters[1].Cell, 0, MapPathfinder.CellCount - 1);

        var plan = CombatTurnPlanner.Choose(snapshot, maxWork: 60_000);

        Assert.Contains(plan.Actions, a => a.Kind == PlannerActionKind.Cast && a.SpellId == 10);
        Assert.Equal(40, plan.Score.Damage);
        Assert.Equal(ap == 6 ? Cell(1) : Cell(0), plan.Intent!.EndCell);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Turn_cast_limit_applies_to_the_initial_in_range_search(int limit)
    {
        var attack = Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6) with
        { CastsLeft = 3, TargetCap = 3 };
        var snapshot = Board(attack) with { Ap = 9, Mp = 0, MaxCasts = limit };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(limit, plan.Actions.Count(a => a.Kind == PlannerActionKind.Cast));
        Assert.Equal(limit * 40, plan.Score.Damage);
    }

    [Fact]
    public void Out_of_range_attack_approaches_casts_then_retreats()
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40));

        var plan = CombatTurnPlanner.Choose(snapshot);

        int cast = System.Array.FindIndex(plan.Actions.ToArray(), a => a.Kind == PlannerActionKind.Cast);
        Assert.True(cast > 0);
        Assert.Contains(plan.Actions.Take(cast), a => a.Kind == PlannerActionKind.Move);
        Assert.Contains(plan.Actions.Skip(cast + 1), a => a.Kind == PlannerActionKind.Move);
        Assert.Equal(Cell(0), plan.Actions.Last().Cell);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Attack_from_an_illegal_end_cell_returns_to_the_required_safe_cell(bool approach)
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6, approach)) with
        {
            Mp = 1, EndCells = ImmutableHashSet.Create(Cell(1))
        };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with
                { MovePoints = 0, AttackRange = 0, ExpectedDamage = 0 })
        };

        var plan = CombatTurnPlanner.Choose(snapshot, maxWork: 60_000);

        Assert.Equal(40, plan.Score.Damage);
        Assert.Contains(plan.Actions, a => a.Kind == PlannerActionKind.Cast && a.SpellId == 10);
        Assert.Equal(Cell(1), plan.Intent!.EndCell);
        if (!approach) Assert.Equal(PlannerActionKind.Cast, plan.Actions[0].Kind);
    }

    [Fact]
    public void Never_approach_can_retreat_to_clear_a_firing_line_before_casting()
    {
        var (cx, cy) = MapPathfinder.MapPointOf(300);
        int P(int x, int y) => MapPathfinder.CellFromMapPoint(cx + x, cy + y);
        int start = P(0, 0), enemy = P(4, 0), wall = P(2, 0);
        var points = (from x in Enumerable.Range(0, 5)
                      from y in Enumerable.Range(0, 3) select (x, y)).ToArray();
        var cells = points.Select(p => new PlannerCell(P(p.x, p.y),
            P(p.x, p.y) != wall, P(p.x, p.y) == wall,
            points.Where(q => System.Math.Abs(q.x - p.x) + System.Math.Abs(q.y - p.y) == 1)
                .Select(q => P(q.x, q.y)).ToImmutableArray())).ToImmutableArray();
        var attack = Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6);
        attack = attack with { Cast = attack.Cast with { Flags = 4 } };
        var snapshot = new CombatTurnSnapshot(cells,
            ImmutableArray.Create(new PlannerFighter(1, start, false, 100, 100),
                new PlannerFighter(2, enemy, true, 100, 100,
                    MovePoints: 0, AttackRange: 0, ExpectedDamage: 0, ThreatKnown: true)),
            1, 3, 2, ImmutableArray.Create(attack), ImmutableDictionary<int, int>.Empty,
            MovementPolicy: CombatMovementPolicy.NeverApproach);
        Assert.False(MapPathfinder.HasLineOfSight(start, enemy, c => c == wall, _ => false));
        Assert.True(MapPathfinder.HasLineOfSight(P(0, 2), enemy, c => c == wall, _ => false));

        var plan = CombatTurnPlanner.Choose(snapshot);

        int castIndex = System.Array.FindIndex(plan.Actions.ToArray(), a => a.Kind == PlannerActionKind.Cast);
        Assert.True(castIndex > 0);
        Assert.Equal(40, plan.Score.Damage);
        int previous = start;
        foreach (var move in plan.Actions.Where(a => a.Kind == PlannerActionKind.Move))
        {
            Assert.True(MapPathfinder.CellDistance(move.Cell, enemy) >= MapPathfinder.CellDistance(previous, enemy));
            previous = move.Cell;
        }
    }

    [Theory]
    [InlineData(FighterVisibility.Unknown, FighterPositionCertainty.Unknown)]
    [InlineData(FighterVisibility.Invisible, FighterPositionCertainty.LastKnown)]
    [InlineData(FighterVisibility.Detected, FighterPositionCertainty.LastKnown)]
    [InlineData(FighterVisibility.Visible, FighterPositionCertainty.LastKnown)]
    public void Unconfirmed_enemy_cell_never_authorizes_an_attack(FighterVisibility visibility, FighterPositionCertainty certainty)
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6)) with { Mp = 0 };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1,
            snapshot.Fighters[1] with { Visibility = visibility, PositionCertainty = certainty }) };
        Assert.DoesNotContain(CombatTurnPlanner.Choose(snapshot).Actions, a => a.Kind == PlannerActionKind.Cast);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(987, false)]
    public void Observed_visibility_controls_actual_attack_expansion(int raw, bool attack)
    {
        var observed = new FightVisibilityReducer();
        observed.Observe(new(2, raw), positionConfirmed: true);
        var state = observed.Get(2);
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6)) with { Mp = 0 };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with
            {
                Visibility = state.Visibility, PositionCertainty = state.PositionCertainty
            })
        };

        var casts = CombatTurnPlanner.Choose(snapshot).Actions.Where(a => a.Kind == PlannerActionKind.Cast);

        Assert.Equal(attack ? new[] { 10 } : System.Array.Empty<int>(), casts.Select(a => a.SpellId));
    }

    [Theory]
    [InlineData(SpellEffectKind.Heal, 108)]
    [InlineData(SpellEffectKind.Shield, 1040)]
    public void Low_life_under_threat_prefers_survival_over_small_attack(SpellEffectKind kind, int action)
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 10, 6),
            Spell(20, kind, action, "C", 80, 0));
        snapshot = snapshot with
        {
            Mp = 0,
            Fighters = snapshot.Fighters.SetItem(0, snapshot.Fighters[0] with
                { Life = 20, States = ImmutableHashSet<int>.Empty })
                .SetItem(1, snapshot.Fighters[1] with { MovePoints = 0, AttackRange = 6 })
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(20, Assert.Single(plan.Actions).SpellId);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(50, true)]
    [InlineData(70, false)]
    public void Flee_threshold_turns_a_profitable_exposed_attack_into_retreat(int threshold, bool attack)
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 2)) with
        {
            Mp = 2, FleeBelowHpPercent = threshold,
        };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(0, snapshot.Fighters[0] with { Cell = Cell(1), Life = 60 })
                .SetItem(1, snapshot.Fighters[1] with { ExpectedDamage = 30 })
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(attack, plan.Actions.Any(a => a.Kind == PlannerActionKind.Cast));
        if (!attack) Assert.Equal(Cell(0), plan.Actions.Last().Cell);
    }

    [Theory]
    [InlineData(40, true)]
    [InlineData(500, false)]
    public void Teleport_attack_is_chosen_only_when_its_reward_outweighs_the_exposure(int enemyLife, bool attack)
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 1),
            Spell(20, SpellEffectKind.Teleport, 4, "C", 0, 4)) with { Ap = 6, Mp = 0 };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with { Life = enemyLife })
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(attack ? new[] { 20, 10 } : System.Array.Empty<int>(),
            plan.Actions.Select(a => a.SpellId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Approach_prohibitions_prevent_an_out_of_range_attack(bool global)
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, approach: global));
        if (global) snapshot = snapshot with { MovementPolicy = CombatMovementPolicy.NeverApproach };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.DoesNotContain(plan.Actions, a => a.Kind == PlannerActionKind.Cast);
    }

    [Fact]
    public void Finishing_a_target_is_preferred_to_equal_damage_on_a_healthy_target()
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6));
        snapshot = snapshot with
        {
            Mp = 0,
            Fighters = snapshot.Fighters.Add(new PlannerFighter(3, Cell(6), true, 40, 500,
                MovePoints: 0, AttackRange: 0, ThreatKnown: true))
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(3L, Assert.Single(plan.Actions).Target);
    }

    [Fact]
    public void Duplicate_spell_configurations_keep_the_selected_targeting_row()
    {
        var healthyTarget = Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6) with
        {
            AllowedTargets = ImmutableHashSet.Create(2L), ConfigurationIndex = 4
        };
        var finishingTarget = healthyTarget with
        {
            AllowedTargets = ImmutableHashSet.Create(3L), ConfigurationIndex = 9
        };
        var snapshot = Board(healthyTarget, finishingTarget);
        snapshot = snapshot with
        {
            Mp = 0,
            Fighters = snapshot.Fighters.Add(new PlannerFighter(3, Cell(6), true, 40, 500,
                MovePoints: 0, AttackRange: 0, ThreatKnown: true))
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(9, Assert.Single(plan.Actions).ConfigurationIndex);
    }

    [Fact]
    public void Mobility_cast_can_be_followed_by_walking_before_the_attack()
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 2),
            Spell(20, SpellEffectKind.Teleport, 4, "C", 0, 2));
        snapshot = snapshot with
        {
            Ap = 6, Mp = 1,
            Cells = snapshot.Cells.SetItem(1, snapshot.Cells[1] with { Walkable = false }),
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with { Life = 40 })
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(new[] { PlannerActionKind.Cast, PlannerActionKind.Move, PlannerActionKind.Cast },
            plan.Actions.Select(a => a.Kind));
        Assert.Equal(new[] { 20, 0, 10 }, plan.Actions.Select(a => a.SpellId));
        Assert.Equal(Cell(3), plan.Actions[1].Cell);
    }

    [Theory]
    [InlineData(CharIds.ResistNeutral, 90)]
    [InlineData(CharIds.ReductionNeutral, 35)]
    public void Observed_resistance_changes_which_enemy_can_be_finished(int characteristic, int value)
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6)) with { Mp = 0 };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with
            {
                Life = 40, MovePoints = 0, AttackRange = 0, ExpectedDamage = 0,
                Characteristics = ImmutableDictionary<int, int>.Empty.Add(characteristic, value)
            }).Add(new PlannerFighter(3, Cell(6), true, 40, 500,
                MovePoints: 0, AttackRange: 0, ExpectedDamage: 0, ThreatKnown: true))
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(3L, Assert.Single(plan.Actions).Target);
        Assert.Equal(40, plan.Score.Damage);
    }

    [Fact]
    public void Percentage_healing_uses_target_maximum_life_not_the_casters_intelligence()
    {
        var snapshot = Board(Spell(20, SpellEffectKind.Heal, 1109, "C", 20, 0)) with
        {
            Mp = 0,
            Characteristics = ImmutableDictionary<int, int>.Empty.Add(CharIds.Intelligence, 500)
        };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(0, snapshot.Fighters[0] with
                { Life = 50, MaxLife = 1000, States = ImmutableHashSet<int>.Empty })
                .SetItem(1, snapshot.Fighters[1] with { MovePoints = 0, AttackRange = 6 })
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(20, Assert.Single(plan.Actions).SpellId);
        Assert.Equal(500, plan.Score.Healing);
    }

    [Theory]
    [InlineData(SpellEffectKind.Pull, 6, 5, 3, 0, 2, 2)]
    [InlineData(SpellEffectKind.Push, 5, 2, 2, 4, 4, 4)]
    public void Displacement_sets_up_an_attack_at_the_changed_enemy_cell(SpellEffectKind kind,
        int action, int start, int amount, int minRange, int range, int landing)
    {
        var attack = Spell(10, SpellEffectKind.Damage, 100, "A", 40, range);
        attack = attack with { Cast = attack.Cast with { MinRange = minRange } };
        var snapshot = Board(attack, Spell(20, kind, action, "A", amount, 6)) with
        { Ap = 6, Mp = 0, ForcedMovementSafe = true };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1,
            snapshot.Fighters[1] with { Cell = Cell(start), MovePoints = 0, AttackRange = 0, ExpectedDamage = 0 }) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(new[] { 20, 10 }, plan.Actions.Select(a => a.SpellId));
        Assert.Equal(Cell(start), plan.Actions[0].Cell);
        Assert.Equal(Cell(landing), plan.Actions[1].Cell);
        Assert.Equal(40, plan.Score.Damage);
        var repeat = CombatTurnPlanner.Choose(snapshot);
        Assert.Equal(plan.Actions.ToArray(), repeat.Actions.ToArray());
        Assert.Equal(plan.Score, repeat.Score);
        Assert.Equal(plan.Work, repeat.Work);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(5, false)]
    [InlineData(4, false)]
    [InlineData(6, true)]
    [InlineData(1, true)]
    public void Live_pull_is_admitted_only_when_its_path_avoids_observed_mark_cells(int markedStep, bool pulled)
    {
        var attack = Spell(10, SpellEffectKind.Damage, 100, "A", 40, 2);
        var snapshot = Board(attack, Spell(20, SpellEffectKind.Pull, 6, "A", 3, 6)) with
        {
            Ap = 6, Mp = 0,
            UnsafeForcedMovementCells = markedStep < 0 ? ImmutableHashSet<int>.Empty : ImmutableHashSet.Create(Cell(markedStep))
        };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1,
            snapshot.Fighters[1] with { MovePoints = 0, AttackRange = 0, ExpectedDamage = 0 }) };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(pulled, plan.Actions.Select(a => a.SpellId).SequenceEqual(new[] { 20, 10 }));
        Assert.Equal(pulled ? 40 : 0, plan.Score.Damage);
    }

    [Fact]
    public void Unknown_mark_coverage_still_forbids_live_forced_movement()
    {
        var attack = Spell(10, SpellEffectKind.Damage, 100, "A", 40, 2);
        var snapshot = Board(attack, Spell(20, SpellEffectKind.Pull, 6, "A", 3, 6)) with { Ap = 6, Mp = 0 };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1,
            snapshot.Fighters[1] with { MovePoints = 0, AttackRange = 0, ExpectedDamage = 0 }) };

        Assert.Empty(CombatTurnPlanner.Choose(snapshot).Actions.Where(a => a.SpellId == 20));
    }

    [Fact]
    public void Swap_reopens_a_blocked_firing_line_and_updates_both_occupants()
    {
        var attack = Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6);
        attack = attack with { Cast = attack.Cast with { Flags = 4 } };
        var snapshot = Board(attack, Spell(20, SpellEffectKind.Swap, 8, "g", 0, 2)) with
        { Ap = 6, Mp = 0, ForcedMovementSafe = true };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with
                { MovePoints = 0, AttackRange = 0, ExpectedDamage = 0 })
                .Add(new PlannerFighter(3, Cell(2), false, 100, 100))
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(new[] { 20, 10 }, plan.Actions.Select(a => a.SpellId));
        Assert.Equal(3, plan.Actions[0].Target);
        Assert.Equal(40, plan.Score.Damage);
    }

    [Fact]
    public void Delegated_push_checks_alignment_from_the_actual_subspell_caster()
    {
        int At(int x, int y) => MapPathfinder.CellFromMapPoint(8 + x, y);
        var parent = Spell(20, SpellEffectKind.Unknown, 792, "g", 42, 2);
        parent = parent with { Cast = parent.Cast with
        {
            MinRange = 2,
            Effects = parent.Cast.Effects.SetItem(0, parent.Cast.Effects[0] with { DiceNum = 42, DiceSide = 1 })
        } };
        var child = Spell(42, SpellEffectKind.Damage, 100, "A", 40).Cast;
        child = child with { Effects = ImmutableArray.Create(
            child.Effects[0] with { Shape = 'C', Size = 2 },
            new SpellEffectDescriptor(5, SpellEffectKind.Push, 1, 1, 0, 0, "A", 'C', 2, 0)) };
        var snapshot = Board(parent) with
        {
            Ap = 3, Mp = 0, MaxCasts = 1, ForcedMovementSafe = true,
            SpellLookup = (id, grade) => id == 42 && grade == 1 ? child : null,
            Cells = new[] { At(0, 0), At(2, 0), At(2, 2), At(2, 3) }
                .Select(c => new PlannerCell(c, true, false, ImmutableArray<int>.Empty)).ToImmutableArray(),
            Fighters = ImmutableArray.Create(new PlannerFighter(1, At(0, 0), false, 100, 100),
                new PlannerFighter(2, At(2, 2), true, 500, 500, MovePoints: 0,
                    AttackRange: 0, ExpectedDamage: 0, ThreatKnown: true),
                new PlannerFighter(3, At(2, 0), false, 100, 100))
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        var cast = Assert.Single(plan.Actions);
        Assert.Equal(20, cast.SpellId);
        Assert.Equal(At(2, 0), cast.Cell);
        Assert.Equal(40, plan.Score.Damage);
    }

    [Fact]
    public void Compound_swap_then_pull_uses_the_casters_updated_position()
    {
        int At(int x, int y) => MapPathfinder.CellFromMapPoint(8 + x, y);
        var attack = Spell(10, SpellEffectKind.Damage, 100, "A", 40, 1);
        attack = attack with { Cast = attack.Cast with { MinRange = 1 } };
        var setup = Spell(20, SpellEffectKind.Swap, 8, "g", 0, 2);
        setup = setup with { Cast = setup.Cast with
        {
            MinRange = 2,
            Effects = setup.Cast.Effects.Add(new SpellEffectDescriptor(6, SpellEffectKind.Pull,
                2, 2, 0, 0, "A", 'C', 3, 0))
        } };
        var snapshot = Board(attack, setup) with
        {
            Ap = 6, Mp = 0, MaxCasts = 2, ForcedMovementSafe = true,
            Cells = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 4)
                .Select(y => new PlannerCell(At(x, y), true, false, ImmutableArray<int>.Empty)))
                .ToImmutableArray(),
            Fighters = ImmutableArray.Create(new PlannerFighter(1, At(0, 0), false, 100, 100),
                new PlannerFighter(2, At(2, 3), true, 500, 500, MovePoints: 0,
                    AttackRange: 0, ExpectedDamage: 0, ThreatKnown: true),
                new PlannerFighter(3, At(2, 0), false, 100, 100))
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(new[] { 20, 10 }, plan.Actions.Select(a => a.SpellId));
        Assert.Equal(new[] { At(2, 0), At(2, 1) }, plan.Actions.Select(a => a.Cell));
        Assert.Equal(40, plan.Score.Damage);
    }

    [Fact]
    public void Swap_then_pull_opens_a_melee_attack_without_walking()
    {
        var attack = Spell(10, SpellEffectKind.Damage, 100, "A", 40, 1);
        attack = attack with { Cast = attack.Cast with { MinRange = 1 } };
        var snapshot = Board(attack, Spell(20, SpellEffectKind.Swap, 8, "g", 0, 2),
            Spell(30, SpellEffectKind.Pull, 6, "A", 2, 3)) with
        {
            Ap = 9, Mp = 0, ForcedMovementSafe = true,
            MaxCasts = 3
        };
        snapshot = snapshot with
        {
            Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with
                { MovePoints = 0, AttackRange = 0, ExpectedDamage = 0 })
                .Add(new PlannerFighter(3, Cell(2), false, 100, 100))
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(new[] { 20, 30, 10 }, plan.Actions.Select(a => a.SpellId));
        Assert.Equal(new[] { Cell(2), Cell(5), Cell(3) }, plan.Actions.Select(a => a.Cell));
        Assert.Equal(40, plan.Score.Damage);
        Assert.Equal(Cell(0), snapshot.Fighters[0].Cell);
        Assert.Equal(Cell(5), snapshot.Fighters[1].Cell);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Obstruction_rejects_the_whole_displacement_not_a_fictitious_landing(bool fighter)
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 2),
            Spell(20, SpellEffectKind.Pull, 6, "A", 3, 6)) with
        { Ap = 6, Mp = 0, ForcedMovementSafe = true };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with
            { MovePoints = 0, AttackRange = 0, ExpectedDamage = 0 }) };
        snapshot = fighter
            ? snapshot with { Fighters = snapshot.Fighters.Add(new PlannerFighter(3, Cell(3), false, 100, 100)) }
            : snapshot with { Cells = snapshot.Cells.SetItem(3, snapshot.Cells[3] with { Walkable = false }) };

        Assert.Empty(CombatTurnPlanner.Choose(snapshot).Actions);
    }

    [Fact]
    public void Moved_enemy_threat_does_not_reuse_its_old_safe_range()
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 2),
            Spell(20, SpellEffectKind.Pull, 6, "A", 3, 6)) with
        { Ap = 6, Mp = 0, ForcedMovementSafe = true };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with
            { MovePoints = 0, AttackRange = 2, ExpectedDamage = 80 }) };

        Assert.Empty(CombatTurnPlanner.Choose(snapshot).Actions);
    }

    [Fact]
    public void Unknown_movement_interactions_are_not_assumed_safe()
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 2),
            Spell(20, SpellEffectKind.Pull, 6, "A", 3, 6)) with { Ap = 6, Mp = 0 };

        Assert.Empty(CombatTurnPlanner.Choose(snapshot).Actions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Displacement_preserves_target_history_and_state_criteria_gates(bool history)
    {
        var pull = Spell(20, SpellEffectKind.Pull, 6, "A", 3, 6);
        pull = history
            ? pull with { TargetCasts = ImmutableDictionary<long, int>.Empty.Add(2, 1) }
            : pull with { Cast = pull.Cast with { StatesCriterion = "HS!97" } };
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 2), pull) with
        { Ap = 6, Mp = 0, ForcedMovementSafe = true };

        Assert.Empty(CombatTurnPlanner.Choose(snapshot).Actions);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Swap_preserves_end_cell_and_mobility_gates(bool mobility, bool approach)
    {
        var attack = Spell(10, SpellEffectKind.Damage, 100, "A", 40, 6);
        attack = attack with { Cast = attack.Cast with { Flags = 4 } };
        var snapshot = Board(attack, Spell(20, SpellEffectKind.Swap, 8, "g", 0, 2, approach)) with
        {
            Ap = 6, Mp = 0, ForcedMovementSafe = true, MobilityAllowed = mobility,
            EndCells = mobility && approach ? ImmutableHashSet.Create(Cell(0)) : null
        };
        snapshot = snapshot with { Fighters = snapshot.Fighters.SetItem(1, snapshot.Fighters[1] with
            { MovePoints = 0, AttackRange = 0, ExpectedDamage = 0 })
            .Add(new PlannerFighter(3, Cell(2), false, 100, 100)) };

        Assert.Empty(CombatTurnPlanner.Choose(snapshot).Actions);
    }

    [Theory]
    [InlineData(5, "A,E97", 0, false)]
    [InlineData(6, "A", 1, false)]
    [InlineData(8, "g", 0, true)]
    [InlineData(1021, "A", 0, false)]
    [InlineData(1103, "A", 0, false)]
    [InlineData(1023, "g", 0, false)]
    public void Unsupported_displacement_data_stays_out_of_the_planner(int action, string mask, int duration, bool conditional)
    {
        var spell = Spell(20, SpellEffectDescriptor.Classify(action), action, mask, action == 8 ? 0 : 1);
        spell = spell with { Cast = spell.Cast with { Effects = spell.Cast.Effects.SetItem(0,
            spell.Cast.Effects[0] with { Duration = duration, Conditional = conditional }) } };

        Assert.False(spell.Cast.CanPlan);
        Assert.False((spell.Cast with { StatesCriterion = "HS!97" }).CanPlan);
    }

    [Fact]
    public void Threat_search_leaves_budget_for_an_immediately_available_finishing_cast()
    {
        var snapshot = Board(Spell(10, SpellEffectKind.Damage, 100, "A", 40, 50)) with
        {
            Mp = 0,
            Cells = Enumerable.Range(0, MapPathfinder.CellCount)
                .Select(c => new PlannerCell(c, true, false,
                    MapPathfinder.InteractionNeighbours(c)
                        .Where(n => MapPathfinder.CellDistance(c, n) == 1).ToImmutableArray()))
                .ToImmutableArray(),
            Fighters = Enumerable.Range(0, 33)
                .Select(i => new PlannerFighter(i + 1L, i, i != 0, i == 0 ? 100 : 1, 100,
                    MovePoints: 12, AttackRange: 0, ExpectedDamage: 0, ThreatKnown: true))
                .ToImmutableArray()
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(10, Assert.Single(plan.Actions).SpellId);
        Assert.InRange(plan.Work, 1, CombatTurnPlanner.MaxWork);
    }
}
