extern alias F06App;

using System.IO;
using System.Linq;
using System.Collections.Immutable;
using GivreClone.Engine;
using Combat = F06App::GivreClone.App.Services.Combat;
using SpellConfig = F06App::GivreClone.App.Services.SpellConfig;
using GameNameResolver = F06App::GivreClone.App.Services.GameNameResolver;
using Xunit;

namespace GivreClone.Tests;

// Preuve sur les VRAIES données du client installé (bundles Unity), pas sur un jeu de données écrit à la main.
// Ignoré proprement si le client n'est pas installé.
public class SpellModifierRealDataTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Resonance_entity_traversal_triggers_active_Entraide_once(bool active, bool existingPortal)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver(); names.EnsureCombatLoaded();
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, 261, false, 100, 1000, Ap: 7, States: ImmutableHashSet<int>.Empty),
            new Combat.SpellFighter(2, 245, true, 650, 650, States: ImmutableHashSet<int>.Empty)
        }, Marks: existingPortal ? new[] { new Combat.SpellMark(1181, 14574, 1, new[] { 288 }) }
            : Array.Empty<Combat.SpellMark>());
        var supported = Combat.SpellResolver.Resolve(board, 0, 261,
            names.GetSpellDescriptor(14596, 2)!, names.GetSpellDescriptor, 184);
        if (!active) supported = supported with { Board = supported.Board with
        {
            Fighters = supported.Board.Fighters.Select(f => f.Id == 1
                ? f with { States = ImmutableHashSet<int>.Empty } : f).ToImmutableArray()
        } };
        var marked = Combat.SpellResolver.Resolve(supported.Board, 0, 245,
            names.GetSpellDescriptor(14611, 2)!, names.GetSpellDescriptor, 184);
        var attack = new Combat.SpellCastDescriptor(30, 1, 3, 1, 6, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new Combat.SpellEffectDescriptor(100, Combat.SpellEffectKind.Damage,
                100, 100, 0, 0, "A", 'P', 0, 0)));

        var hit = Combat.SpellResolver.Resolve(marked.Board, 0, 245, attack, names.GetSpellDescriptor, 184);

        bool rewarded = active && existingPortal;
        Assert.Equal(existingPortal ? 288 : 245, hit.Board.Fighters[1].Cell);
        Assert.Equal(550, hit.Board.Fighters[1].Life);
        Assert.Equal(rewarded ? 130 : 100, hit.Board.Fighters[0].Life);
        Assert.Equal(rewarded ? 8 : 7, hit.Board.Fighters[0].Ap);
        Assert.Equal(rewarded ? 1 : 0, hit.Events.OfType<Combat.HealingResolved>().Count());
        Assert.Equal(rewarded ? 1 : 0, hit.Events.OfType<Combat.PointsResolved>().Count());
        Assert.Empty(hit.Unmodeled);
        Assert.Equal(100, marked.Board.Fighters[0].Life);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(true, false)]
    [InlineData(true, false, true)]
    public void Resonance_setup_is_selected_only_when_traversal_enables_a_stronger_followup(bool existingPortal, bool active = true, bool observed = false)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver(); names.EnsureCombatLoaded();
        int Cell(int x) => GivreClone.Protocol.MapPathfinder.CellFromMapPoint(x + 8, 8);
        Combat.SpellCastDescriptor Attack(int id, int ap, int range, int damage) =>
            new(id, 1, ap, 1, range, 0, false, 1, 1, 0, 0, "",
                ImmutableArray.Create(new Combat.SpellEffectDescriptor(100, Combat.SpellEffectKind.Damage,
                    damage, damage, 0, 0, "A", 'P', 0, 0)));
        Combat.PlannerSpell Config(Combat.SpellCastDescriptor cast) => new(cast, 1, 1,
            ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L));
        var snapshot = new Combat.CombatTurnSnapshot(
            Enumerable.Range(0, 560).Select(c => new Combat.PlannerCell(c, true, false,
                ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(new Combat.PlannerFighter(1, Cell(0), false, 1000, 1000,
                    States: ImmutableHashSet<int>.Empty),
                new Combat.PlannerFighter(2, Cell(4), true, 1000, 1000, MovePoints: 0,
                    ExpectedDamage: 0, ThreatKnown: true, States: ImmutableHashSet<int>.Empty)),
            1, observed ? 4 : 6, 0, (observed ? ImmutableArray<Combat.PlannerSpell>.Empty
                : ImmutableArray.Create(Config(names.GetSpellDescriptor(14611, 2)!)))
                .Add(Config(Attack(30, 1, 6, 100))).Add(Config(Attack(31, 3, 1, 400))),
            ImmutableDictionary<int, int>.Empty, ForcedMovementSafe: true,
            SpellLookup: names.GetSpellDescriptor, CasterLevel: 184,
            Portals: existingPortal ? ImmutableArray.Create(new Combat.ClassMechanics.Portal(1, Cell(1), 1, active)) : default,
            ObservedDeferredEffects: observed ? names.GetSpellDescriptor(14611, 2)!.Effects
                .Where(e => e.ActionId == 1160).Select(e => new Combat.DeferredSpellEffect(14611, 1, Cell(4), e, 2))
                .ToImmutableArray() : default);

        var plan = Combat.CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(observed ? new[] { 30, 31 } : existingPortal ? new[] { 14611, 30, 31 } : new[] { 30 }, plan.Actions.Select(a => a.SpellId));
        Assert.Equal(existingPortal ? 500 : 100, plan.Score.Damage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resonance_first_nonlethal_owner_hit_places_a_portal_and_traverses_an_existing_pair(bool existingPortal)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver(); names.EnsureCombatLoaded();
        var resonance = names.GetSpellDescriptor(14611, 2)!;
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, 261, false, 1000, 1000, States: ImmutableHashSet<int>.Empty),
            new Combat.SpellFighter(2, 245, true, 650, 650, States: ImmutableHashSet<int>.Empty)
        }, Marks: existingPortal ? new[] { new Combat.SpellMark(1181, 14574, 1, new[] { 288 }) }
            : Array.Empty<Combat.SpellMark>());
        var marked = Combat.SpellResolver.Resolve(board, 0, 245, resonance, names.GetSpellDescriptor, 184);
        var attack = new Combat.SpellCastDescriptor(30, 1, 3, 1, 6, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new Combat.SpellEffectDescriptor(100, Combat.SpellEffectKind.Damage,
                100, 100, 0, 0, "A", 'P', 0, 0)));

        var hit = Combat.SpellResolver.Resolve(marked.Board, 0, 245, attack, names.GetSpellDescriptor, 184);

        var target = hit.Board.Fighters.Single(f => f.Id == 2);
        Assert.Equal(550, target.Life);
        Assert.Equal(existingPortal ? 288 : 245, target.Cell);
        Assert.DoesNotContain(6251, target.States!);
        Assert.Contains(hit.Board.Marks!, m => m.Cells.Contains(245) && m.ActionId is 1181 or 1183);
        Assert.DoesNotContain(hit.Board.DeferredEffects!, e => e.SpellId == 14611 && e.TargetId == 2);
        Assert.Empty(hit.Unmodeled);
        Assert.Contains(6251, marked.Board.Fighters[1].States!);
        Assert.Equal(245, marked.Board.Fighters[1].Cell);
        Assert.All(marked.Board.DeferredEffects!, pending => Assert.Equal(2L, pending.TargetId));
        Assert.DoesNotContain(hit.Board.DeferredEffects!, e => e.SpellId == 14611);
        var secondHit = Combat.SpellResolver.Resolve(hit.Board, 0, target.Cell, attack, names.GetSpellDescriptor, 184);
        Assert.Equal(450, secondHit.Board.Fighters[1].Life);
        Assert.Equal(target.Cell, secondHit.Board.Fighters[1].Cell);
        Assert.Equal(hit.Board.Marks!.Count, secondHit.Board.Marks!.Count);
    }

    [Theory]
    [InlineData(50, 0, false)]
    [InlineData(650, 30, false)]
    [InlineData(650, 0, true)]
    [InlineData(650, 0, false, true)]
    [InlineData(650, 0, false, false, true)]
    public void Resonance_unverified_damage_boundaries_do_not_invent_traversal(int life, int shield, bool forbidden,
        bool occupiedExit = false, bool thirdParty = false)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver(); names.EnsureCombatLoaded();
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, 261, false, 1000, 1000, States: ImmutableHashSet<int>.Empty),
            new Combat.SpellFighter(2, 245, true, life, 650, Shield: shield,
                States: forbidden ? ImmutableHashSet.Create(678) : ImmutableHashSet<int>.Empty),
            new Combat.SpellFighter(3, occupiedExit ? 288 : 300, false, 1000, 1000,
                States: ImmutableHashSet<int>.Empty)
        }, Marks: new[] { new Combat.SpellMark(1181, 14574, 1, new[] { 288 }) });
        var marked = Combat.SpellResolver.Resolve(board, 0, 245, names.GetSpellDescriptor(14611, 2)!, names.GetSpellDescriptor, 184);
        var attack = new Combat.SpellCastDescriptor(30, 1, 3, 1, 6, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new Combat.SpellEffectDescriptor(100, Combat.SpellEffectKind.Damage,
                100, 100, 0, 0, "A", 'P', 0, 0)));

        var hit = Combat.SpellResolver.Resolve(marked.Board, thirdParty ? 2 : 0, 245, attack, names.GetSpellDescriptor, 184);

        Assert.Equal(245, hit.Board.Fighters[1].Cell);
        Assert.Single(hit.Board.Marks!);
        Assert.Contains(hit.Unmodeled, e => e.SpellId == 14611 && e.ActionId == 1182);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(14574, 100)]
    [InlineData(14582, 100)]
    [InlineData(14591, 100)]
    [InlineData(14596, 100)]
    [InlineData(-1, 100)]
    [InlineData(14574, 1000)]
    [InlineData(-1, 1000)]
    [InlineData(-1, 100, 60000)]
    public void Distribution_combo_remains_searchable_with_learned_portal_utilities_on_a_full_board(int utility, int life, int maxWork = Combat.CombatTurnPlanner.MaxWork)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver(); names.EnsureCombatLoaded();
        int Cell(int x) => GivreClone.Protocol.MapPathfinder.CellFromMapPoint(x + 8, 8);
        var spells = new[] { (14577, 2), (14608, 3), (14574, 3), (14582, 3), (14591, 3), (14596, 2) }
            .Where((pair, index) => index < 2 || utility == -1 || pair.Item1 == utility)
            .Select(pair => names.GetSpellDescriptor(pair.Item1, pair.Item2)!)
            .Select(cast => new Combat.PlannerSpell(cast, 1, 1, ImmutableDictionary<long, int>.Empty,
                ImmutableHashSet.Create(1L, 2L))).ToImmutableArray();
        var snapshot = new Combat.CombatTurnSnapshot(
            Enumerable.Range(0, 560).Select(c => new Combat.PlannerCell(c, true, false,
                ImmutableArray<int>.Empty)).ToImmutableArray(),
            ImmutableArray.Create(new Combat.PlannerFighter(1, Cell(0), false, life, 1000,
                    States: ImmutableHashSet<int>.Empty),
                new Combat.PlannerFighter(2, Cell(1), true, 780, 780, MovePoints: 0,
                    ExpectedDamage: 0, ThreatKnown: true)),
            1, 5, 0, spells, ImmutableDictionary<int, int>.Empty,
            SpellLookup: names.GetSpellDescriptor, CasterLevel: 184);

        var plan = Combat.CombatTurnPlanner.Choose(snapshot, maxWork);

        Assert.True(plan.Actions.Select(a => a.SpellId).SequenceEqual(life < 1000 ? new[] { 14577, 14608 } : new[] { 14608 }),
            $"utility={utility}, plan={plan}");
        Assert.True(plan.Score.Damage > 0);
        Assert.Equal(life < 1000, plan.Score.Healing > 0);
    }

    [Theory]
    [InlineData(100, true, false)]
    [InlineData(1000, false, false)]
    [InlineData(100, true, true)]
    [InlineData(100, true, false, true)]
    [InlineData(1000, false, false, true)]
    public void Distribution_setup_is_selected_only_when_a_following_hit_can_heal(int life, bool useful, bool observed, bool fullBoard = false)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver(); names.EnsureCombatLoaded();
        int Cell(int x) => GivreClone.Protocol.MapPathfinder.CellFromMapPoint(x + 8, 8);
        var distribution = names.GetSpellDescriptor(14577, 2)!;
        var attack = new Combat.SpellCastDescriptor(30, 1, 3, 1, 1, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new Combat.SpellEffectDescriptor(100, Combat.SpellEffectKind.Damage,
                239, 239, 0, 0, "A", 'P', 0, 0)));
        Combat.PlannerSpell Config(Combat.SpellCastDescriptor cast) => new(cast, 1, 1,
            ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L));
        var snapshot = new Combat.CombatTurnSnapshot(
            Enumerable.Range(0, 2).Select(i => new Combat.PlannerCell(Cell(i), true, false,
                ImmutableArray<int>.Empty))
                .Concat(fullBoard ? Enumerable.Range(0, 560).Where(c => c != Cell(0) && c != Cell(1))
                    .Select(c => new Combat.PlannerCell(c, true, false, ImmutableArray<int>.Empty))
                    : Enumerable.Empty<Combat.PlannerCell>()).ToImmutableArray(),
            ImmutableArray.Create(new Combat.PlannerFighter(1, Cell(0), false, life, 1000,
                    States: ImmutableHashSet<int>.Empty),
                new Combat.PlannerFighter(2, Cell(1), true, 780, 780, MovePoints: 0,
                    ExpectedDamage: 0, ThreatKnown: true)),
            1, observed ? 3 : 5, 0, observed ? ImmutableArray.Create(Config(attack))
                : ImmutableArray.Create(Config(distribution), Config(attack)),
            ImmutableDictionary<int, int>.Empty, SpellLookup: names.GetSpellDescriptor, CasterLevel: 184,
            ObservedDeferredEffects: observed ? distribution.Effects.Where(e => e.ActionId == 1160)
                .Select(e => new Combat.DeferredSpellEffect(14577, 1, Cell(1), e, 2)).ToImmutableArray() : default);

        var plan = Combat.CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(useful && !observed ? new[] { 14577, 30 } : new[] { 30 }, plan.Actions.Select(a => a.SpellId));
        Assert.Equal(239, plan.Score.Damage);
        // The planner weights 47 actual PV by its existing 2.5 survival priority at 10% life.
        Assert.Equal(useful ? 117.5 : 0, plan.Score.Healing);
    }

    [Theory]
    [InlineData(false, 47)]
    [InlineData(true, 0)]
    [InlineData(false, 35, 1)]
    [InlineData(false, 59, 3)]
    public void Distribution_heals_around_its_current_target_after_nonlethal_unshielded_damage(bool moveTarget, int healing, int grade = 2)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver(); names.EnsureCombatLoaded();
        int Cell(int x) => GivreClone.Protocol.MapPathfinder.CellFromMapPoint(x + 8, 8);
        var distribution = names.GetSpellDescriptor(14577, grade)!;
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, Cell(0), false, 100, 1000, States: ImmutableHashSet<int>.Empty),
            new Combat.SpellFighter(2, Cell(1), true, 780, 780, States: ImmutableHashSet<int>.Empty)
        });
        var marked = Combat.SpellResolver.Resolve(board, 0, Cell(1), distribution, names.GetSpellDescriptor, 184);
        var moved = marked.Board with { Fighters = marked.Board.Fighters.Select(f => f.Id == 2 && moveTarget
            ? f with { Cell = Cell(4) } : f).ToImmutableArray() };
        var attack = new Combat.SpellCastDescriptor(30, 1, 3, 1, 6, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new Combat.SpellEffectDescriptor(100, Combat.SpellEffectKind.Damage,
                239, 239, 0, 0, "A", 'P', 0, 0)));

        var hit = Combat.SpellResolver.Resolve(moved, 0, Cell(moveTarget ? 4 : 1), attack, names.GetSpellDescriptor, 184);

        Assert.Equal(541, hit.Board.Fighters.Single(f => f.Id == 2).Life);
        Assert.Equal(100 + healing, hit.Board.Fighters.Single(f => f.Id == 1).Life);
        Assert.Equal(healing, hit.Healing);
        Assert.Empty(hit.Unmodeled);
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(780, 30)]
    public void Distribution_does_not_invent_healing_for_unverified_lethal_or_shielded_hits(int targetLife, int shield)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver(); names.EnsureCombatLoaded();
        int Cell(int x) => GivreClone.Protocol.MapPathfinder.CellFromMapPoint(x + 8, 8);
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, Cell(0), false, 100, 1000, States: ImmutableHashSet<int>.Empty),
            new Combat.SpellFighter(2, Cell(1), true, targetLife, 780, Shield: shield,
                States: ImmutableHashSet<int>.Empty)
        });
        var marked = Combat.SpellResolver.Resolve(board, 0, Cell(1),
            names.GetSpellDescriptor(14577, 2)!, names.GetSpellDescriptor, 184);
        var attack = new Combat.SpellCastDescriptor(30, 1, 3, 1, 1, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new Combat.SpellEffectDescriptor(100, Combat.SpellEffectKind.Damage,
                239, 239, 0, 0, "A", 'P', 0, 0)));

        var hit = Combat.SpellResolver.Resolve(marked.Board, 0, Cell(1), attack, names.GetSpellDescriptor, 184);

        Assert.Equal(100, hit.Board.Fighters.Single(f => f.Id == 1).Life);
        Assert.Empty(hit.Events.OfType<Combat.HealingResolved>());
        Assert.Contains(hit.Unmodeled, effect => effect.SpellId == 14577 && effect.TargetId == 2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void Strict_planner_descriptor_never_substitutes_another_spell_grade(int grade)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver();
        names.EnsureCombatLoaded();
        Assert.NotNull(names.GetSpellDescriptor(32443, 1));

        var descriptor = names.GetSpellDescriptor(32443, grade);

       Assert.Null(descriptor);
    }

    [Fact]
    public void Immediate_damage_from_installed_data_is_not_rejected_as_a_conditional_effect()
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver();
        names.EnsureCombatLoaded();

        var descriptor = names.GetSpellDescriptor(32443, 1);

        Assert.NotNull(descriptor);
        var damage = Assert.Single(descriptor!.Effects, e => e.ActionId == 99);
        Assert.False(damage.Conditional);
    }

    [Fact]
    public void Learned_Ivresse_summons_without_inventing_immediate_client_preview_healing()
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver();
        names.EnsureCombatLoaded();
        var descriptor = names.GetSpellDescriptor(12777, 1);
        Assert.NotNull(descriptor);
        Assert.True(descriptor.NeedFreeCell);
        var summon = Assert.Single(descriptor.Effects);
        Assert.Equal(181, summon.ActionId);
        Assert.Equal(5843, summon.DiceNum);
        Assert.Equal(1, summon.DiceSide);

        var rotation = Combat.AutoSpellRotation.Build(System.Array.Empty<SpellConfig>(),
            new[] { new Combat.LearnedSpellInfo(descriptor.SpellId, descriptor.Ap, 0, descriptor) },
            new(RequireDescriptors: true));
        var config = Assert.Single(rotation);
        Assert.Equal(12777, config.SpellId);
        Assert.False(Combat.AutoSpellRotation.ShouldUsePlanner(new[] { descriptor }));

        int caster = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(8, 8);
        int emptyCenter = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(9, 8);
        var snapshot = new Combat.CombatTurnSnapshot(
            ImmutableArray.Create(
                new Combat.PlannerCell(caster, true, false, ImmutableArray.Create(emptyCenter)),
                new Combat.PlannerCell(emptyCenter, true, false, ImmutableArray.Create(caster))),
            ImmutableArray.Create(new Combat.PlannerFighter(1, caster, false, 100, 1000,
                States: ImmutableHashSet<int>.Empty)),
            1, descriptor.Ap, 0,
            ImmutableArray.Create(new Combat.PlannerSpell(descriptor, config.PerTurn, descriptor.MaxPerTarget,
                ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L))),
            ImmutableDictionary<int, int>.Empty, MaxCasts: 1, SpellLookup: names.GetSpellDescriptor);

        // The barrel's future actions are separate from immediate caster healing.
        var plan = Combat.CombatTurnPlanner.Choose(snapshot);

        Assert.Empty(plan.Actions);
        Assert.Equal(0, plan.Score.Healing);

        var resolved = Combat.SpellResolver.Resolve(
            new Combat.SpellBoard(new[] { new Combat.SpellFighter(1, caster, false, 100, 1000,
                States: ImmutableHashSet<int>.Empty) }),
            0, emptyCenter, descriptor, names.GetSpellDescriptor, 1,
            cell => snapshot.Cells.Any(c => c.Id == cell && c.Walkable));
        var summoned = Assert.Single(resolved.Summons);
        Assert.Equal(5843, summoned.Template);
        Assert.Equal(emptyCenter, summoned.Cell);
        Assert.Empty(resolved.Events.OfType<Combat.HealingResolved>());
        Assert.Equal(100, resolved.Board.Fighters.Single(f => f.Id == 1).Life);
        Assert.Empty(resolved.Unmodeled);
    }

    [Theory]
    [InlineData(14596, "CPT", 14598, 1, 2, true)]
    [InlineData(14577, "D|XD", 14578, 2, 1, false)]
    public void Learned_triggered_utilities_defer_rewards_instead_of_granting_preview_AP_and_healing(
        int spellId, string trigger, int subSpell, int subGrade, int triggerDuration, bool initialState)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver();
        names.EnsureCombatLoaded();
        var descriptor = names.GetSpellDescriptor(spellId, 2);
        Assert.NotNull(descriptor);
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, 0, false, 100, 1000, States: ImmutableHashSet<int>.Empty)
        });

        var result = Combat.SpellResolver.Resolve(board, 0, 0, descriptor, names.GetSpellDescriptor, 1);

        Assert.Equal(100, result.Board.Fighters.Single().Life);
        Assert.Empty(result.Events.OfType<Combat.HealingResolved>());
        Assert.Empty(result.Events.OfType<Combat.PointsResolved>());
        if (initialState)
            Assert.Contains(result.Events.OfType<Combat.StateResolved>(), e => e.State == 6252 && e.Applied);
        else
            Assert.Empty(result.Events.OfType<Combat.StateResolved>());
        var deferred = Assert.Single(result.Deferred);
        Assert.Equal(trigger, deferred.Triggers);
        Assert.Equal(subSpell, deferred.Effect.DiceNum);
        Assert.Equal(subGrade, deferred.Effect.DiceSide);
        Assert.Equal(triggerDuration, deferred.Effect.EffectTriggerDuration);
        if (spellId == 14577) Assert.Equal(1L, deferred.TargetId);
        Assert.Equal(deferred, Assert.Single(result.Board.DeferredEffects!));
        var later = Combat.SpellResolver.Resolve(result.Board, 0, 0,
            descriptor with { Effects = ImmutableArray<Combat.SpellEffectDescriptor>.Empty },
            names.GetSpellDescriptor, 1);
        Assert.Equal(deferred, Assert.Single(later.Board.DeferredEffects!));
        Assert.Empty(later.Deferred);
        Assert.Empty(later.Events);
    }

    [Theory]
    [InlineData(100, true, false)]
    [InlineData(1000, false, false)]
    [InlineData(100, true, true)]
    [InlineData(100, true, false, true)]
    [InlineData(100, true, true, true)]
    public void Entraide_then_entity_traversal_funds_a_weapon_attack_and_heals_without_a_projected_shot(int life, bool useful, bool observed, bool fullBoard = false)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver();
        names.EnsureCombatLoaded();
        var entraide = names.GetSpellDescriptor(14596, 2)!;
        int Cell(int x) => GivreClone.Protocol.MapPathfinder.CellFromMapPoint(x + 8, 8);
        var weapon = new Combat.SpellCastDescriptor(369, 1, 3, 1, 1, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new Combat.SpellEffectDescriptor(100, Combat.SpellEffectKind.Damage,
                100, 100, 0, 0, "A", 'P', 0, 0)));
        Combat.PlannerSpell Config(Combat.SpellCastDescriptor cast) => new(cast, 1, 1,
            ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L, 2L));
        var snapshot = new Combat.CombatTurnSnapshot(
            Enumerable.Range(0, 7).Select(i => new Combat.PlannerCell(Cell(i), true, false,
                Enumerable.Range(0, 7).Where(j => Math.Abs(i - j) == 1).Select(Cell).ToImmutableArray()))
                .Concat(fullBoard ? Enumerable.Range(0, 560).Where(c => !Enumerable.Range(0, 7).Select(Cell).Contains(c))
                    .Select(c => new Combat.PlannerCell(c, true, false, ImmutableArray<int>.Empty))
                    : Enumerable.Empty<Combat.PlannerCell>()).ToImmutableArray(),
            ImmutableArray.Create(new Combat.PlannerFighter(1, Cell(0), false, life, 1000,
                    States: observed ? ImmutableHashSet.Create(6252) : ImmutableHashSet<int>.Empty),
                new Combat.PlannerFighter(2, Cell(5), true, 500, 500, MovePoints: 0,
                    ExpectedDamage: 0, ThreatKnown: true)),
            1, observed ? 2 : 4, 1, observed ? ImmutableArray.Create(Config(weapon))
                : ImmutableArray.Create(Config(entraide), Config(weapon)), ImmutableDictionary<int, int>.Empty,
            SpellLookup: names.GetSpellDescriptor, CasterLevel: 183,
            Portals: ImmutableArray.Create(new Combat.ClassMechanics.Portal(1, Cell(1), 1),
                new Combat.ClassMechanics.Portal(2, Cell(4), 1)),
            UnsafeForcedMovementCells: ImmutableHashSet.Create(Cell(1), Cell(4)),
            ObservedDeferredEffects: observed ? entraide.Effects.Where(e => e.Triggers == "CPT")
                .Select(e => new Combat.DeferredSpellEffect(14596, 1, Cell(0), e)).ToImmutableArray() : default);

        var plan = Combat.CombatTurnPlanner.Choose(snapshot);

        var initial = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, Cell(0), false, 100, 1000, Ap: 4, Mp: 1,
                States: ImmutableHashSet<int>.Empty)
        });
        var prepared = Combat.SpellResolver.Resolve(initial, 0, Cell(0), entraide, names.GetSpellDescriptor, 183);
        Assert.Empty(prepared.Unmodeled);
        Assert.Contains(prepared.Events.OfType<Combat.StateResolved>(), e => e.State == 6252);
        Assert.Equal(100, plan.Score.Damage);
        Assert.Equal(useful ? 30 : 0, plan.Score.Healing);
        Assert.Equal(useful && !observed ? new[] { 14596, 0, 369 } : new[] { 0, 369 }, plan.Actions.Select(a => a.SpellId));
        Assert.Equal(Cell(1), plan.Actions[useful && !observed ? 1 : 0].Cell);
        Assert.False(plan.Actions.Last().ViaPortal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Learned_Entraide_rewards_a_confirmed_entity_portal_traversal_after_teleporting_the_caster(bool active)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver();
        names.EnsureCombatLoaded();
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, 329, false, 100, 1000, Ap: 7,
                States: ImmutableHashSet<int>.Empty)
        });
        var setup = Combat.SpellResolver.Resolve(board, 0, 329,
            names.GetSpellDescriptor(14596, 2)!, names.GetSpellDescriptor, 183);
        var traversed = setup.Board with
        {
            Fighters = setup.Board.Fighters.Select(f => f with
            {
                Cell = 302, States = active ? f.States : ImmutableHashSet<int>.Empty
            }).ToArray()
        };

        var result = Combat.SpellResolver.ResolvePortalTraversal(traversed, 0,
            names.GetSpellDescriptor, 183);

        Assert.Equal(302, result.Board.Fighters[0].Cell);
        Assert.Equal(active ? 8 : 7, result.Board.Fighters[0].Ap);
        Assert.Equal(active ? 130 : 100, result.Board.Fighters[0].Life);
        Assert.Equal(100, setup.Board.Fighters[0].Life);
        Assert.Equal(7, setup.Board.Fighters[0].Ap);
        Assert.Equal(active ? 1 : 0, result.Events.OfType<Combat.PointsResolved>().Count());
        Assert.Equal(active ? 1 : 0, result.Events.OfType<Combat.HealingResolved>().Count());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    public void Learned_Sram_invisibility_from_installed_data_enables_walking_then_attacking(int grade, int expectedMp)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver();
        names.EnsureCombatLoaded();
        var descriptor = names.GetSpellDescriptor(12913, grade);
        Assert.NotNull(descriptor);
        var rotation = Combat.AutoSpellRotation.Build(System.Array.Empty<SpellConfig>(),
            new[] { new Combat.LearnedSpellInfo(descriptor.SpellId, descriptor.Ap, 0, descriptor) },
            new(RequireDescriptors: true));
        var config = Assert.Single(rotation);
        Assert.Equal(12913, config.SpellId);
        Assert.True(Combat.AutoSpellRotation.ShouldUsePlanner(new[] { descriptor }));

        int caster = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(8, 8);
        int step = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(9, 8);
        int enemy = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(10, 8);
        // Synthetic corridor and attack isolate the installed spell's immediate MP gain:
        // with zero starting MP the caster must buff, walk, then attack at range one.
        var attack = new Combat.SpellCastDescriptor(42, 1, 3, 1, 1, 0, false, 1, 1, 0, 0, "",
            ImmutableArray.Create(new Combat.SpellEffectDescriptor(100, Combat.SpellEffectKind.Damage,
                10, 10, 0, 0, "A", 'P', 0, 0)));
        var snapshot = new Combat.CombatTurnSnapshot(
            ImmutableArray.Create(
                new Combat.PlannerCell(caster, true, false, ImmutableArray.Create(step)),
                new Combat.PlannerCell(step, true, false, ImmutableArray.Create(caster, enemy)),
                new Combat.PlannerCell(enemy, true, false, ImmutableArray.Create(step))),
            ImmutableArray.Create(
                new Combat.PlannerFighter(1, caster, false, 100, 100, States: ImmutableHashSet<int>.Empty),
                new Combat.PlannerFighter(2, enemy, true, 1000, 1000,
                    MovePoints: 0, AttackRange: 0, ExpectedDamage: 0, ThreatKnown: true,
                    States: ImmutableHashSet<int>.Empty)),
            1, descriptor.Ap + attack.Ap, 0,
            ImmutableArray.Create(
                new Combat.PlannerSpell(descriptor, config.PerTurn, descriptor.MaxPerTarget,
                    ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L)),
                new Combat.PlannerSpell(attack, 1, 1,
                    ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(2L))),
            ImmutableDictionary<int, int>.Empty, MaxCasts: 2, SpellLookup: names.GetSpellDescriptor);

        var plan = Combat.CombatTurnPlanner.Choose(snapshot);

        Assert.Equal(new[] { Combat.PlannerActionKind.Cast, Combat.PlannerActionKind.Move, Combat.PlannerActionKind.Cast },
            plan.Actions.Select(a => a.Kind));
        Assert.Equal(new[] { 12913, 0, 42 }, plan.Actions.Select(a => a.SpellId));
        Assert.Equal(new[] { caster, step, enemy }, plan.Actions.Select(a => a.Cell));
        Assert.Equal(10, plan.Score.Damage);

        var resolved = Combat.SpellResolver.Resolve(
            new Combat.SpellBoard(new[] { new Combat.SpellFighter(1, caster, false, 100, 100,
                States: ImmutableHashSet<int>.Empty, Mp: 0) }),
            0, plan.Actions[0].Cell, descriptor, names.GetSpellDescriptor, 1);
        Assert.Empty(resolved.Unmodeled);
        var fighter = Assert.Single(resolved.Board.Fighters);
        Assert.Equal(expectedMp, fighter.Mp);
        Assert.Equal(GivreClone.Protocol.FighterVisibility.Invisible, fighter.Visibility);
        var points = Assert.Single(resolved.Events.OfType<Combat.PointsResolved>());
        Assert.Equal(128, points.ActionId);
        Assert.Equal(1L, points.TargetId);
        Assert.False(points.IsAp);
        Assert.Equal(expectedMp, points.Delta);
        var hidden = Assert.Single(resolved.Events.OfType<Combat.VisibilityResolved>());
        Assert.Equal(150, hidden.ActionId);
        Assert.Equal(1L, hidden.TargetId);
    }

    [Theory]
    [InlineData(12914, 3, 12928)]
    [InlineData(12921, 2, 12927)]
    public void Installed_Sram_traps_retain_a_resolvable_payload_grade(int spellId, int grade, int payloadId)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver();
        names.EnsureCombatLoaded();
        var descriptor = names.GetSpellDescriptor(spellId, grade);
        Assert.NotNull(descriptor);
        int caster = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(8, 8);
        int center = GivreClone.Protocol.MapPathfinder.CellFromMapPoint(9, 8);

        var resolved = Combat.SpellResolver.Resolve(
            new Combat.SpellBoard(new[] { new Combat.SpellFighter(1, caster, false, 1000, 1000,
                States: ImmutableHashSet<int>.Empty) }),
            0, center, descriptor, names.GetSpellDescriptor, 1);

        Assert.NotNull(resolved.Board.Marks);
        var mark = Assert.Single(resolved.Board.Marks);
        Assert.Equal(payloadId, mark.SpellId);
        Assert.Equal(grade, mark.SpellGrade);
        var payload = names.GetSpellDescriptor(mark.SpellId, mark.SpellGrade);
        Assert.NotNull(payload);
        Assert.Equal(payloadId, payload.SpellId);
        Assert.Equal(grade, payload.Grade);
    }

    private static bool ClientInstalled() =>
        Directory.Exists(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "Ankama", "Dofus-dofus3", "Dofus_Data", "StreamingAssets", "Content", "Data"));

    [Fact]
    public void Cra_shooting_stance_modifies_the_range_window_of_its_spells_in_the_real_game_data()
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver();
        names.EnsureCombatLoaded();

        var effects = names.SpellModifierEffects(32465, 1);   // posture de tir du Crâ
        Assert.NotEmpty(effects);
        // 32443 (Flèche de Barrage, portée 1-5) : +3 portée minimale et +4 portée maximale, 1 tour.
        Assert.Contains(new SpellModifierEffect(SpellModifierActions.MinRangeUp, 32443, 3, 1), effects);
        Assert.Contains(new SpellModifierEffect(SpellModifierActions.MaxRangeUp, 32443, 4, 1), effects);

        var set = new SpellModifierSet();
        set.Apply(effects, turn: 1);
        var baseStats = names.SpellCombat(32443, 1);
        Assert.NotNull(baseStats);
        var s = set.Adjust(32443, new SpellCastStats(baseStats!.Value.Ap, baseStats.Value.MinRange, baseStats.Value.Range,
            baseStats.Value.CastInLine, baseStats.Value.CastInDiagonal, baseStats.Value.CastTestLos, baseStats.Value.Rangeable));
        Assert.Equal(baseStats.Value.MinRange + 3, s.MinRange);   // 1 -> 4 : lancer à 1-3 cases est refusé par le serveur
        Assert.Equal(baseStats.Value.Range + 4, s.Range);          // 5 -> 9

        // Un sort sans effet de modification n'en renvoie aucun (cas courant : pas de surcoût).
        Assert.Empty(names.SpellModifierEffects(32443, 1));
    }
}
