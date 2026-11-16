extern alias F06App;

using System.Collections.Immutable;
using System.Linq;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public sealed class EliotropePortalAreaTests
{
    private static int Cell(int x, int y) => MapPathfinder.CellFromMapPoint(x, y);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_projected_Affliction_values_its_bound_start_turn_effect(bool selfHeal)
    {
        var names = new F06App::GivreClone.App.Services.GameNameResolver();
        names.EnsureLoaded(); names.EnsureCombatLoaded();
        SpellCastDescriptor? Lookup(int id, int grade) =>
            System.Text.Json.JsonSerializer.Deserialize<SpellCastDescriptor>(
                System.Text.Json.JsonSerializer.Serialize(names.GetSpellDescriptor(id, grade)));
        var affliction = Lookup(14584, 2)!;
        var snapshot = new CombatTurnSnapshot(Enumerable.Range(8, 13).Select(x => new PlannerCell(Cell(x, 0),
                true, false, ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(new PlannerFighter(1, Cell(10, 0), false, selfHeal ? 500 : 1000, 1000,
                    States: ImmutableHashSet<int>.Empty),
                new PlannerFighter(2, Cell(18, 0), true, 1000, 1000, MovePoints: 0,
                    ExpectedDamage: 0, ThreatKnown: true)),
            1, affliction.Ap, 0, ImmutableArray.Create(new PlannerSpell(affliction, 1, 1,
                ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(selfHeal ? 1L : 2L))),
            ImmutableDictionary<int, int>.Empty, MaxCasts: 1, SpellLookup: Lookup,
            Portals: ImmutableArray.Create(new ClassMechanics.Portal(1, Cell(12, 0), 1),
                new ClassMechanics.Portal(2, Cell(selfHeal ? 8 : 16, 0), 1)));

        var plan = CombatTurnPlanner.Choose(snapshot);
        var baseline = CombatTurnPlanner.Choose(snapshot with { SpellLookup = (id, grade) =>
            Lookup(id, grade) is { } spell ? spell with { Effects = spell.Effects
                .Where(e => e.Triggers != "TB").ToImmutableArray() } : null });

        Assert.True(Assert.Single(plan.Actions).ViaPortal);
        Assert.Equal(14584, plan.Actions[0].SpellId);
        Assert.Equal(selfHeal ? 1L : 2L, plan.Actions[0].Target);
        if (selfHeal) Assert.True(plan.Score.Healing > baseline.Score.Healing);
        else Assert.True(plan.Score.Damage > baseline.Score.Damage);
        Assert.Equal(0, plan.Score.Finishes);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void Learned_sillage_repositions_for_an_attack_unless_gravity_blocks_it(bool gravity, bool statesKnown)
    {
        var names = new F06App::GivreClone.App.Services.GameNameResolver();
        names.EnsureLoaded(); names.EnsureCombatLoaded();
        var sillage = System.Text.Json.JsonSerializer.Deserialize<SpellCastDescriptor>(
            System.Text.Json.JsonSerializer.Serialize(names.GetSpellDescriptor(14591, 2)))!;
        int caster = Cell(10, 0), enemy = Cell(14, 0);
        var shot = new SpellCastDescriptor(90001, 1, 3, 1, 1, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(100, SpellEffectKind.Damage, 100, 100, 0, 0,
                "A", 'P', 0, 0, DiceNum: 100, DiceSide: 100, Triggers: "I")));
        var states = ImmutableHashSet.Create(3737);
        if (gravity) states = states.Add(7);
        var snapshot = new CombatTurnSnapshot(Enumerable.Range(10, 5).Select(x => new PlannerCell(Cell(x, 0),
                true, false, ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, 1000, 1000, States: states,
                    StatesKnown: statesKnown, AbsentStates: gravity ? ImmutableHashSet<int>.Empty : ImmutableHashSet.Create(7, 3738)),
                new PlannerFighter(2, enemy, true, 1000, 1000, MovePoints: 0, ExpectedDamage: 0, ThreatKnown: true)),
            1, sillage.Ap + shot.Ap, 0, ImmutableArray.Create(
                new PlannerSpell(sillage, 1, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L)),
                new PlannerSpell(shot, 1, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L))),
            ImmutableDictionary<int, int>.Empty, SpellLookup: (id, grade) =>
                System.Text.Json.JsonSerializer.Deserialize<SpellCastDescriptor>(
                    System.Text.Json.JsonSerializer.Serialize(names.GetSpellDescriptor(id, grade))));

        var plan = CombatTurnPlanner.Choose(snapshot);

        if (gravity) Assert.Empty(plan.Actions);
        else
        {
            Assert.Equal(new[] { 14591, 90001 }, plan.Actions.Select(a => a.SpellId));
            Assert.Equal(Cell(13, 0), plan.Actions[0].Cell);
            Assert.Equal(100, plan.Score.Damage);
            Assert.Equal(0, plan.Unmodeled);
        }
    }

    [Fact]
    public void Cabale_returns_through_portals_to_boost_the_following_attack()
    {
        var names = new F06App::GivreClone.App.Services.GameNameResolver();
        names.EnsureLoaded();
        names.EnsureCombatLoaded();
        var cabale = System.Text.Json.JsonSerializer.Deserialize<SpellCastDescriptor>(
            System.Text.Json.JsonSerializer.Serialize(names.GetSpellDescriptor(14617, 3)))!;
        int caster = Cell(10, 0), entry = Cell(12, 0), exit = Cell(8, 0), enemy = Cell(14, 0);
        var shot = new SpellCastDescriptor(90001, 1, 3, 1, 6, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(100, SpellEffectKind.Damage, 100, 100, 0, 0,
                "A", 'P', 0, 0, DiceNum: 100, DiceSide: 100, Triggers: "I")));
        var candidates = ImmutableArray.Create(
            new PlannerSpell(cabale, 1, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L)),
            new PlannerSpell(shot, 1, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L)));
        var snapshot = new CombatTurnSnapshot(Enumerable.Range(8, 7).Select(x => new PlannerCell(Cell(x, 0),
            true, false, ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, 1000, 1000,
                States: ImmutableHashSet<int>.Empty), new PlannerFighter(2, enemy, true, 1000, 1000,
                MovePoints: 0, ExpectedDamage: 0, ThreatKnown: true)),
            1, cabale.Ap + shot.Ap, 0, candidates, ImmutableDictionary<int, int>.Empty,
            EmptyStatBuffInventory: true, Portals: ImmutableArray.Create(new ClassMechanics.Portal(1, entry, 1),
                new ClassMechanics.Portal(2, exit, 1)));

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(new[] { 14617, 90001 }, plan.Actions.Select(a => a.SpellId));
        Assert.True(plan.Actions[0].ViaPortal);
        Assert.Equal(1L, plan.Actions[0].Target);
        Assert.Equal(300, plan.Score.Damage);
        Assert.Equal(0, plan.Unmodeled);
        Assert.Equal((0, 2), CombatTurnPlanner.ExpectedSelfPointGain(snapshot, cabale, entry, viaPortal: true));
        Assert.Equal((0, 1), CombatTurnPlanner.ExpectedSelfPointGain(snapshot, cabale, caster));
    }

    [Fact]
    public void Portal_ray_uses_exit_direction_and_preserves_caster_identity()
    {
        var spell = Ray();
        var board = new SpellBoard(new[]
        {
            new SpellFighter(1, Cell(10, 0), false, 500, 500),
            new SpellFighter(2, Cell(12, 9), true, 500, 500),
            new SpellFighter(3, Cell(13, 9), true, 500, 500),
            new SpellFighter(4, Cell(12, 10), true, 500, 500),
        }, ThroughPortal: true);

        var result = SpellResolver.Resolve(board, 0, Cell(12, 9), spell, (_, _) => null, 167,
            areaOrigin: Cell(10, 9));

        Assert.Equal(new long[] { 2, 3 }, result.Events.OfType<DamageResolved>().Select(e => e.TargetId).Order());
        Assert.Equal(Cell(10, 0), result.Board.Fighters[0].Cell);
        Assert.Equal(60, result.EnemyDamage);
        Assert.Empty(result.Unmodeled);

        var direct = SpellResolver.Resolve(board with { ThroughPortal = false }, 0, Cell(12, 9), spell,
            (_, _) => null, 167);
        Assert.Equal(new long[] { 2, 4 }, direct.Events.OfType<DamageResolved>().Select(e => e.TargetId).Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Planner_routes_area_attack_through_entry_and_hits_exit_axis_targets(bool realRay)
    {
        var spell = Ray();
        if (realRay)
        {
            var names = new F06App::GivreClone.App.Services.GameNameResolver();
            names.EnsureLoaded();
            names.EnsureCombatLoaded();
            var descriptor = names.GetSpellDescriptor(14579, 3);
            Assert.NotNull(descriptor);
            spell = System.Text.Json.JsonSerializer.Deserialize<SpellCastDescriptor>(
                System.Text.Json.JsonSerializer.Serialize(descriptor))!;
            Assert.NotNull(spell);
        }
        var fighters = ImmutableArray.Create(
            new PlannerFighter(1, Cell(10, 0), false, 1000, 1000, Breed: ClassMechanics.EliotropeBreedId,
                States: ImmutableHashSet<int>.Empty),
            new PlannerFighter(2, Cell(12, 9), true, 500, 500, MovePoints: 0, ExpectedDamage: 0,
                ThreatKnown: true, States: ImmutableHashSet<int>.Empty),
            new PlannerFighter(3, Cell(13, 9), true, 500, 500, MovePoints: 0, ExpectedDamage: 0,
                ThreatKnown: true, States: ImmutableHashSet<int>.Empty));
        var tiles = new[] { Cell(10, 0), Cell(11, 0), Cell(12, 0), Cell(10, 9), Cell(11, 9), Cell(12, 9), Cell(13, 9), Cell(14, 9) }
            .Select(c => new PlannerCell(c, true, false, ImmutableArray<int>.Empty)).ToImmutableArray();
        var candidate = new PlannerSpell(spell, 1, 1, ImmutableDictionary<long, int>.Empty,
            ImmutableHashSet.Create(2L, 3L), ZonePolicy: new('L', 2, 0, 2, true, ImmutableHashSet<long>.Empty));
        var snapshot = new CombatTurnSnapshot(tiles, fighters, 1, spell.Ap, 0, ImmutableArray.Create(candidate),
            ImmutableDictionary<int, int>.Empty, UnsafeForcedMovementCells: ImmutableHashSet<int>.Empty,
            Portals: ImmutableArray.Create(new ClassMechanics.Portal(1, Cell(12, 0), 1),
                new ClassMechanics.Portal(2, Cell(10, 9), 1)));

        var plan = CombatTurnPlanner.Choose(snapshot);

        var cast = Assert.Single(plan.Actions, a => a.Kind == PlannerActionKind.Cast);
        Assert.Equal(Cell(12, 0), cast.Cell);
        Assert.Equal(2L, cast.Target);
        Assert.True(plan.Score.Damage > 0);
        Assert.DoesNotContain(CombatTurnPlanner.Choose(snapshot with { Ap = spell.Ap - 1 }).Actions,
            a => a.Kind == PlannerActionKind.Cast);

        var allyInRay = snapshot with { Fighters = fighters.SetItem(2, fighters[2] with { Enemy = false }) };
        Assert.DoesNotContain(CombatTurnPlanner.Choose(allyInRay).Actions, a => a.Kind == PlannerActionKind.Cast);

        var forbiddenTarget = snapshot with { Spells = ImmutableArray.Create(candidate with
        {
            ZonePolicy = new('L', 2, 0, 1, true, ImmutableHashSet.Create(3L))
        }) };
        Assert.DoesNotContain(CombatTurnPlanner.Choose(forbiddenTarget).Actions, a => a.Kind == PlannerActionKind.Cast);

        var wall = snapshot with { Cells = tiles.Select(t => t.Id == Cell(11, 9)
            ? t with { Walkable = false, BlocksLos = true } : t).ToImmutableArray() };
        Assert.DoesNotContain(CombatTurnPlanner.Choose(wall).Actions, a => a.Kind == PlannerActionKind.Cast);
    }

    [Theory]
    [InlineData(200, true, false)]
    [InlineData(1000, false, false)]
    [InlineData(200, true, true)]
    [InlineData(1000, false, true)]
    [InlineData(200, true, false, true)]
    [InlineData(1000, false, false, true)]
    [InlineData(200, true, false, false, true)]
    [InlineData(1000, false, false, false, true)]
    public void Planner_returns_portal_heal_to_injured_caster_only(int life, bool shouldHeal, bool realRay,
        bool realCicatrisation = false, bool emptyImpact = false)
    {
        int caster = Cell(10, 0), entry = Cell(emptyImpact ? 13 : 12, 0), exit = Cell(8, 0);
        var spell = new SpellCastDescriptor(90001, 1, 3, 1, 3, 4, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(108, SpellEffectKind.Heal, 100, 100, 0, 0,
                "a,R", 'P', 0, 0, DiceNum: 100, DiceSide: 100, Triggers: "I")));
        if (emptyImpact)
            spell = spell with { Effects = ImmutableArray.Create(spell.Effects[0] with { Shape = 'C', Size = 1 }) };
        if (realRay || realCicatrisation)
        {
            var names = new F06App::GivreClone.App.Services.GameNameResolver();
            names.EnsureLoaded();
            names.EnsureCombatLoaded();
            var descriptor = names.GetSpellDescriptor(realCicatrisation ? 14587 : 14579, 3);
            Assert.NotNull(descriptor);
            spell = System.Text.Json.JsonSerializer.Deserialize<SpellCastDescriptor>(
                System.Text.Json.JsonSerializer.Serialize(descriptor))!;
        }
        var fighters = ImmutableArray.Create(
            new PlannerFighter(1, caster, false, life, 1000, Breed: ClassMechanics.EliotropeBreedId,
                States: ImmutableHashSet<int>.Empty),
            new PlannerFighter(2, Cell(14, 0), true, 1000, 1000, MovePoints: 0,
                ExpectedDamage: 0, ThreatKnown: true, States: ImmutableHashSet<int>.Empty));
        var cells = Enumerable.Range(8, 7).Select(x => new PlannerCell(Cell(x, 0), true, false,
            ImmutableArray<int>.Empty)).ToImmutableArray();
        var candidate = new PlannerSpell(spell, 1, 1, ImmutableDictionary<long, int>.Empty,
            ImmutableHashSet.Create(1L), AllowedCells: realCicatrisation || emptyImpact ? ImmutableHashSet.Create(entry) : null,
            ZonePolicy: realRay
                ? new PlannerZonePolicy('L', 2, 0, 1, true, ImmutableHashSet<long>.Empty)
                : realCicatrisation || emptyImpact ? new PlannerZonePolicy('C', emptyImpact ? 1 : 2, 0, 1, false, ImmutableHashSet<long>.Empty) : null);
        var snapshot = new CombatTurnSnapshot(cells, fighters, 1, 3, 0, ImmutableArray.Create(candidate),
            ImmutableDictionary<int, int>.Empty,
            Portals: ImmutableArray.Create(new ClassMechanics.Portal(1, entry, 1),
                new ClassMechanics.Portal(2, exit, 1)));

        var plan = CombatTurnPlanner.Choose(snapshot);

        if (shouldHeal)
        {
            var cast = Assert.Single(plan.Actions, a => a.Kind == PlannerActionKind.Cast);
            Assert.Equal(entry, cast.Cell);
            Assert.Equal(emptyImpact ? 0L : 1L, cast.Target);
            Assert.True(cast.ViaPortal);
            Assert.True(plan.Score.Healing > 0);
            var withoutNetwork = CombatTurnPlanner.Choose(snapshot with
            {
                Portals = ImmutableArray<ClassMechanics.Portal>.Empty
            });
            if (realCicatrisation)
            {
                // Direct Cicatrisation is legal too, but the projected heal gains
                // the network multiplier and must be preferred at the same AP cost.
                Assert.False(Assert.Single(withoutNetwork.Actions).ViaPortal);
                Assert.Equal(200, withoutNetwork.Score.Healing);
                Assert.True(plan.Score.Healing > withoutNetwork.Score.Healing);
            }
            else
            {
                Assert.DoesNotContain(withoutNetwork.Actions, a => a.Kind == PlannerActionKind.Cast);
                Assert.Equal(0, withoutNetwork.Score.Healing);
            }
        }
        else Assert.DoesNotContain(plan.Actions, a => a.Kind == PlannerActionKind.Cast);
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(2, false)]
    [InlineData(6, true, false)]
    [InlineData(6, false, false, true)]
    [InlineData(6, false, true, true)]
    [InlineData(6, true, true, false, true)]
    [InlineData(6, false, true, false, false, true)]
    [InlineData(6, false, true, false, false, false, true)]
    [InlineData(6, true, true, false, false, false, false, true)]
    [InlineData(6, false, true, false, false, false, false, false, true)]
    public void Portal_setup_uses_next_turn_ap_only_when_it_opens_an_affordable_shot(int nextAp, bool expected,
        bool active = true, bool exitAlreadyExists = false, bool emptyImpact = false, bool directKillAvailable = false,
        bool redundantIntermediate = false, bool improvedExistingPair = false, bool betterDirectTarget = false)
    {
        int caster = Cell(10, 0), entry = Cell(12, 0), exit = Cell(18, 0), enemy = Cell(20, 0);
        var portal = new SpellCastDescriptor(14574, 3, 1, 1, 8, 4, false, 2, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(1181, SpellEffectKind.Unknown, 2, 2, 0, -1,
                "a,A", 'P', 0, 0, DiceNum: 2, Triggers: "I")));
        var attack = Ray() with { Effects = ImmutableArray.Create(Ray().Effects[0] with { Shape = 'P', Size = 0 }) };
        if (directKillAvailable) attack = attack with { Range = 12 };
        if (emptyImpact)
            attack = attack with { Effects = ImmutableArray.Create(attack.Effects[0] with { Shape = 'C', Size = 1 }) };
        var spells = ImmutableArray.Create(
            new PlannerSpell(portal, 1, 0, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L),
                AllowedCells: ImmutableHashSet.Create(redundantIntermediate ? Cell(14, 0) : exit)),
            new PlannerSpell(attack, 1, 0, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L)));
        if (betterDirectTarget)
            spells = spells.SetItem(1, spells[1] with { AllowedTargets = ImmutableHashSet.Create(2L, 3L) });
        var snapshot = new CombatTurnSnapshot(
            Enumerable.Range(10, emptyImpact ? 12 : 11).Select(x => new PlannerCell(Cell(x, 0), true, false,
                ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, 1000, 1000, TurnAp: nextAp,
                    States: ImmutableHashSet<int>.Empty),
                new PlannerFighter(2, emptyImpact ? Cell(21, 0) : enemy, true, directKillAvailable || betterDirectTarget ? 1 : 500, 500, MovePoints: 0, ExpectedDamage: 0,
                    ThreatKnown: true, States: ImmutableHashSet<int>.Empty)),
            1, 1, 0, spells, ImmutableDictionary<int, int>.Empty,
            Portals: improvedExistingPair
                ? ImmutableArray.Create(new ClassMechanics.Portal(1, entry, 1, true),
                    new ClassMechanics.Portal(2, Cell(16, 0), 1, true))
                : redundantIntermediate
                ? ImmutableArray.Create(new ClassMechanics.Portal(1, entry, 1, true),
                    new ClassMechanics.Portal(2, exit, 1, true))
                : exitAlreadyExists
                ? ImmutableArray.Create(new ClassMechanics.Portal(1, entry, 1, active),
                    new ClassMechanics.Portal(2, exit, 1, false))
                : ImmutableArray.Create(new ClassMechanics.Portal(1, entry, 1, active)));

        if (betterDirectTarget)
            snapshot = snapshot with { Cells = snapshot.Cells.Add(new PlannerCell(Cell(11, 1), true, false,
                    ImmutableArray<int>.Empty)),
                Fighters = snapshot.Fighters.Add(new PlannerFighter(3, Cell(11, 1), true,
                500, 500, MovePoints: 0, ExpectedDamage: 0, ThreatKnown: true,
                States: ImmutableHashSet<int>.Empty)) };
        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(expected, plan.Actions.Any(a => a.Kind == PlannerActionKind.Cast && a.SpellId == 14574));
        if (expected) Assert.Equal(exit, Assert.Single(plan.Actions).Cell);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void First_portal_does_not_prepare_a_shot_on_an_excluded_enemy(bool allowed)
    {
        int caster = Cell(10, 0), exit = Cell(18, 0), enemy = Cell(20, 0);
        var portal = new SpellCastDescriptor(14574, 3, 1, 1, 8, 4, false, 2, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(1181, SpellEffectKind.Unknown, 2, 2, 0, -1,
                "a,A", 'P', 0, 0, DiceNum: 2, Triggers: "I")));
        var attack = Ray() with { Range = 3,
            Effects = ImmutableArray.Create(Ray().Effects[0] with { Shape = 'P', Size = 0 }) };
        var snapshot = new CombatTurnSnapshot(
            Enumerable.Range(10, 11).Select(x => new PlannerCell(Cell(x, 0), true, false,
                ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, 1000, 1000, TurnAp: 6,
                    States: ImmutableHashSet<int>.Empty),
                new PlannerFighter(2, enemy, true, 500, 500, MovePoints: 0, ExpectedDamage: 0,
                    ThreatKnown: true, States: ImmutableHashSet<int>.Empty)),
            1, 1, 0, ImmutableArray.Create(
                new PlannerSpell(portal, 1, 0, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L),
                    AllowedCells: ImmutableHashSet.Create(exit)),
                new PlannerSpell(attack, 1, 0, ImmutableDictionary<long, int>.Empty,
                    allowed ? ImmutableHashSet.Create(2L) : ImmutableHashSet<long>.Empty)),
            ImmutableDictionary<int, int>.Empty);

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(allowed, plan.Actions.Any(a => a.SpellId == 14574));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Portal_self_boost_is_used_only_when_it_improves_an_affordable_attack(bool canFollowUp)
    {
        int caster = Cell(10, 0), entry = Cell(12, 0), exit = Cell(8, 0), enemy = Cell(14, 0);
        var boost = new SpellCastDescriptor(90002, 1, 3, 1, 3, 4, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(138, SpellEffectKind.Buff, 100, 100, 0, 2,
                "a,R", 'P', 0, 0, DiceNum: 100, DiceSide: 100, Triggers: "I")));
        var attack = Ray() with { Effects = ImmutableArray.Create(Ray().Effects[0] with { Shape = 'P', Size = 0 }) };
        var snapshot = new CombatTurnSnapshot(
            Enumerable.Range(8, 7).Select(x => new PlannerCell(Cell(x, 0), true, false,
                ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, 1000, 1000,
                    States: ImmutableHashSet<int>.Empty),
                new PlannerFighter(2, enemy, true, 500, 500, MovePoints: 0, ExpectedDamage: 0,
                    ThreatKnown: true, States: ImmutableHashSet<int>.Empty)),
            1, canFollowUp ? 6 : 3, 0, ImmutableArray.Create(
                new PlannerSpell(boost, 1, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L)),
                new PlannerSpell(attack, 1, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L))),
            ImmutableDictionary<int, int>.Empty,
            Portals: ImmutableArray.Create(new ClassMechanics.Portal(1, entry, 1),
                new ClassMechanics.Portal(2, exit, 1)));

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(canFollowUp, plan.Actions.Any(a => a.SpellId == boost.SpellId));
        if (canFollowUp)
        {
            var cast = Assert.Single(plan.Actions, a => a.SpellId == boost.SpellId);
            Assert.True(cast.ViaPortal);
            Assert.Equal(entry, cast.Cell);
            Assert.Equal(1, cast.Target);
            Assert.Equal(boost.SpellId, plan.Actions[0].SpellId);
            Assert.Equal(60, plan.Score.Damage);
        }
        else Assert.Equal(30, plan.Score.Damage);
    }

    [Theory]
    [InlineData(3738, true)]
    [InlineData(3737, false)]
    public void Learned_transcendance_range_modifier_enables_only_its_matching_spell(int state, bool expected)
    {
        var names = new F06App::GivreClone.App.Services.GameNameResolver();
        names.EnsureLoaded(); names.EnsureCombatLoaded();
        var boost = System.Text.Json.JsonSerializer.Deserialize<SpellCastDescriptor>(
            System.Text.Json.JsonSerializer.Serialize(names.GetSpellDescriptor(14580, 2)))!;
        var portal = new SpellCastDescriptor(14604, 2, 1, 1, 4, 4, false, 2, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(1181, SpellEffectKind.Unknown, 2, 2, 0, -1,
                "a,A", 'P', 0, 0, DiceNum: 2, Triggers: "I")));
        var attack = Ray() with { Effects = ImmutableArray.Create(Ray().Effects[0] with { Shape = 'P', Size = 0 }) };
        var snapshot = new CombatTurnSnapshot(
            Enumerable.Range(10, 9).Select(x => new PlannerCell(Cell(x, 0), true, false,
                ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(new PlannerFighter(1, Cell(10, 0), false, 1000, 1000,
                    States: ImmutableHashSet.Create(state), TurnAp: 6),
                new PlannerFighter(2, Cell(18, 0), true, 500, 500, MovePoints: 0,
                    ExpectedDamage: 0, ThreatKnown: true, States: ImmutableHashSet<int>.Empty)),
            1, boost.Ap + portal.Ap, 0, ImmutableArray.Create(
                new PlannerSpell(boost, 1, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L)),
                new PlannerSpell(portal, 1, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L),
                    AllowedCells: ImmutableHashSet.Create(Cell(16, 0))),
                new PlannerSpell(attack, 1, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L))),
            ImmutableDictionary<int, int>.Empty,
            Portals: ImmutableArray.Create(new ClassMechanics.Portal(1, Cell(12, 0), 1, true)));

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(expected, plan.Actions.Any(a => a.SpellId == portal.SpellId));
        if (expected) Assert.Equal(new[] { 14580, 14604 }, plan.Actions.Select(a => a.SpellId));
    }

    [Theory]
    [InlineData(true, 1, true)]
    [InlineData(false, 1, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, false, 5)]
    [InlineData(true, 1, true, null, 320)]
    [InlineData(false, 1, false, null, 320)]
    public void Simulated_range_bonus_opens_only_boostable_attacks_without_readding_observed_range(
        bool rangeable, int bonus, bool expected, int? rangeCap = null, int action = 117)
    {
        int caster = Cell(10, 0), enemy = Cell(16, 0);
        var boost = new SpellCastDescriptor(90003, 1, 1, 0, 0, 4, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(action, SpellEffectKind.Buff, bonus, bonus, 0, 2,
                action == 320 ? "A" : "C", 'P', 0, 0, DiceNum: bonus, DiceSide: bonus, Triggers: "I")))
            with { Range = action == 320 ? 6 : 0 };
        // Five already includes the observed +2 range, as in the live caller.
        var attack = Ray() with { Range = 5, Rangeable = rangeable,
            Effects = ImmutableArray.Create(Ray().Effects[0] with { Shape = 'P', Size = 0 }) };
        var snapshot = new CombatTurnSnapshot(
            Enumerable.Range(10, 7).Select(x => new PlannerCell(Cell(x, 0), true, false,
                ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, 1000, 1000,
                    States: ImmutableHashSet<int>.Empty),
                new PlannerFighter(2, enemy, true, 500, 500, MovePoints: 0, ExpectedDamage: 0,
                    ThreatKnown: true, States: ImmutableHashSet<int>.Empty)),
            1, 4, 0, ImmutableArray.Create(
                new PlannerSpell(boost, 1, 1, ImmutableDictionary<long, int>.Empty,
                    ImmutableHashSet.Create(action == 320 ? 2L : 1L)),
                new PlannerSpell(attack, 1, 1, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L),
                    RangeCap: rangeCap)),
            ImmutableDictionary<int, int>.Empty.Add(19, 2));

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(expected, plan.Actions.Any(a => a.SpellId == attack.SpellId));
        Assert.Equal(expected ? 30 : 0, plan.Score.Damage);
        if (expected)
        {
            Assert.Equal(2, plan.Actions.Length);
            Assert.Equal(boost.SpellId, plan.Actions[0].SpellId);
            Assert.Equal(attack.SpellId, plan.Actions[1].SpellId);
        }
    }

    [Theory]
    [InlineData(200, 3, true)]
    [InlineData(1000, 3, false)]
    [InlineData(200, 2, false)]
    public void Portal_setup_can_prepare_a_useful_self_heal_for_the_next_turn(int life, int nextAp, bool expected)
    {
        int caster = Cell(10, 0), entry = Cell(12, 0), exit = Cell(8, 0);
        var portal = new SpellCastDescriptor(14574, 3, 1, 1, 8, 4, false, 2, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(1181, SpellEffectKind.Unknown, 2, 2, 0, -1,
                "a,A", 'P', 0, 0, DiceNum: 2, Triggers: "I")));
        var heal = new SpellCastDescriptor(90001, 1, 3, 1, 3, 4, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(108, SpellEffectKind.Heal, 100, 100, 0, 0,
                "a,R", 'P', 0, 0, DiceNum: 100, DiceSide: 100, Triggers: "I")));
        var snapshot = new CombatTurnSnapshot(
            Enumerable.Range(8, 7).Select(x => new PlannerCell(Cell(x, 0), true, false,
                ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, life, 1000, TurnAp: nextAp,
                    States: ImmutableHashSet<int>.Empty),
                new PlannerFighter(2, Cell(14, 0), true, 500, 500, MovePoints: 0, ExpectedDamage: 0,
                    ThreatKnown: true, States: ImmutableHashSet<int>.Empty)),
            1, 1, 0, ImmutableArray.Create(
                new PlannerSpell(portal, 1, 0, ImmutableDictionary<long, int>.Empty,
                    ImmutableHashSet.Create(1L), AllowedCells: ImmutableHashSet.Create(exit)),
                new PlannerSpell(heal, 1, 0, ImmutableDictionary<long, int>.Empty,
                    ImmutableHashSet.Create(1L))), ImmutableDictionary<int, int>.Empty,
            Portals: ImmutableArray.Create(new ClassMechanics.Portal(1, entry, 1)));

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(expected, plan.Actions.Any(a => a.SpellId == portal.SpellId));
        Assert.Equal(0, plan.Score.Healing);
        if (expected)
        {
            Assert.Equal(exit, Assert.Single(plan.Actions).Cell);
            Assert.True(plan.Score.Control > 0);
        }
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(100, false, 2)]
    public void Neutral_closes_an_enemy_portal_shot_only_when_it_reduces_reply_damage(int enemyDamage, bool close,
        int enemyAp = 3)
    {
        int caster = Cell(20, 0), entry = Cell(12, 0), exit = Cell(18, 0), enemy = Cell(10, 0);
        var neutral = new SpellCastDescriptor(14582, 3, 1, 0, 8, 4, false, 2, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(1183, SpellEffectKind.Unknown, 0, 0, 0, 1,
                "a,A", 'P', 0, 0, Triggers: "I"),
                new SpellEffectDescriptor(120, SpellEffectKind.Buff, 1, 1, 0, 0, "C", 'P', 0, 0, Triggers: "I")));
        var snapshot = new CombatTurnSnapshot(
            Enumerable.Range(10, 11).Select(x => new PlannerCell(Cell(x, 0), true, false,
                ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, 1000, 1000,
                    States: ImmutableHashSet<int>.Empty),
                new PlannerFighter(2, enemy, true, 500, 500, MovePoints: 0, AttackRange: 3,
                    ExpectedDamage: enemyDamage, ThreatKnown: true, States: ImmutableHashSet<int>.Empty,
                    TurnAp: enemyAp,
                    OffensiveSpells: ImmutableArray.Create(Ray()))),
            1, 1, 0, ImmutableArray.Create(new PlannerSpell(neutral, 1, 0,
                ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L),
                AllowedCells: ImmutableHashSet.Create(exit))), ImmutableDictionary<int, int>.Empty,
            Portals: ImmutableArray.Create(new ClassMechanics.Portal(1, entry, 1),
                new ClassMechanics.Portal(2, exit, 1)));

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(close, plan.Actions.Any(a => a.Kind == PlannerActionKind.Cast && a.SpellId == 14582));
        if (close) Assert.Equal(exit, Assert.Single(plan.Actions).Cell);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(false, false)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 1, true)]
    public void Empty_network_can_be_built_for_an_immediate_shot_on_a_full_board(bool directKill,
        bool restrictedPlacement = true, int availableAp = 5, bool observedFirst = false)
    {
        int caster = Cell(10, 0), entry = Cell(12, 0), exit = Cell(16, 0), enemy = Cell(directKill ? 13 : 18, 0);
        var portal = new SpellCastDescriptor(14574, 3, 1, 1, 6, 4, false, 2, 0, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(1181, SpellEffectKind.Unknown, 2, 2, 0, -1,
                "a,A", 'P', 0, 0, DiceNum: 2, Triggers: "I")));
        var attack = Ray() with { Range = 3, Effects = ImmutableArray.Create(Ray().Effects[0] with { Shape = 'P', Size = 0 }) };
        var snapshot = new CombatTurnSnapshot(
            Enumerable.Range(0, 560).Select(c => new PlannerCell(c, true, false,
                ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, 1000, 1000,
                    States: ImmutableHashSet<int>.Empty, TurnAp: 5),
                new PlannerFighter(2, enemy, true, directKill ? 20 : 500, 500, MovePoints: 0, ExpectedDamage: 0,
                    ThreatKnown: true, States: ImmutableHashSet<int>.Empty)),
            1, availableAp, 0, ImmutableArray.Create(
                new PlannerSpell(attack, 1, 0, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L)),
                new PlannerSpell(portal, observedFirst ? 1 : 2, 0, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L),
                    AllowedCells: observedFirst ? ImmutableHashSet.Create(exit)
                        : restrictedPlacement ? ImmutableHashSet.Create(entry, exit) : null)),
            ImmutableDictionary<int, int>.Empty,
            Portals: observedFirst ? ImmutableArray.Create(new ClassMechanics.Portal(1, entry, 1, Active: false)) : default);

        var plan = CombatTurnPlanner.Choose(snapshot, 60000);

        Assert.Equal(directKill ? 0 : observedFirst ? 1 : 2, plan.Actions.Count(a => a.SpellId == 14574));
        if (availableAp >= attack.Ap + 2)
        {
            Assert.Contains(plan.Actions, a => a.SpellId == attack.SpellId);
            Assert.True(plan.Score.Damage > 0);
        }
        else
        {
            Assert.DoesNotContain(plan.Actions, a => a.SpellId == attack.SpellId);
            Assert.Equal(0, plan.Score.Damage);
            Assert.True(plan.Score.Control > 0);
        }
        if (directKill) Assert.Single(plan.Actions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Portal_amplification_can_finish_a_target_in_the_simulated_board(bool crowdedRotation)
    {
        int caster = Cell(10, 0), entry = Cell(12, 0), exit = Cell(16, 0), enemy = Cell(18, 0);
        var attack = Ray() with { Range = 3,
            Effects = ImmutableArray.Create(Ray().Effects[0] with { Shape = 'P', Size = 0 }) };
        var candidates = Enumerable.Range(0, crowdedRotation ? 12 : 0).Select(i => new PlannerSpell(
            attack with { SpellId = 91000 + i, Effects = ImmutableArray.Create(
                attack.Effects[0] with { Min = 1, Max = 1, Shape = 'C', Size = 2 }) },
            1, 0, ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L)))
            .Append(new PlannerSpell(attack, 1, 0,
                ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L))).ToImmutableArray();
        var snapshot = new CombatTurnSnapshot(
            (crowdedRotation ? Enumerable.Range(0, 560) : Enumerable.Range(10, 9).Select(x => Cell(x, 0)))
                .Select(x => new PlannerCell(x, true, false,
                ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(new PlannerFighter(1, caster, false, 1000, 1000),
                new PlannerFighter(2, enemy, true, 32, 32, MovePoints: 0, ExpectedDamage: 0, ThreatKnown: true)),
            1, 3, 0, candidates,
            ImmutableDictionary<int, int>.Empty,
            Portals: ImmutableArray.Create(new ClassMechanics.Portal(1, entry, 1),
                new ClassMechanics.Portal(2, exit, 1)));

        var plan = CombatTurnPlanner.Choose(snapshot, crowdedRotation ? 6000 : CombatTurnPlanner.MaxWork);

        Assert.Single(plan.Actions);
        Assert.Equal(32, plan.Score.Damage);
        Assert.True(plan.Score.Finishes > 0);
        Assert.True(Assert.Single(plan.Intent!.Targets).ExpectedKill);
    }

    private static SpellCastDescriptor Ray() => new(14579, 3, 3, 1, 5, 4, false, 1, 1, 0, 0, "",
        ImmutableArray.Create(new SpellEffectDescriptor(99, SpellEffectKind.Damage, 30, 30, 2, 0,
            "A", 'L', 2, 0, DiceNum: 30, DiceSide: 30, Triggers: "I")));
}
