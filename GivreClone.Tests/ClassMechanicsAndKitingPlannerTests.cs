using System.Collections.Immutable;
using GivreClone.App.Services;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class ClassMechanicsAndKitingPlannerTests
{
    // Grade-one SpellLevelData rows from the checked-in client 3.7.12.12 export:
    // 40901 (Peur), 40916 (Invisibilite), 40571 (Attirance), 40574 (Transposition), 85234 (Cra push).
    // Retain raw masks, zones, flags, and dice; zero diceSide means a fixed diceNum.
    private static SpellEffectDescriptor Effect(int action, int dice, string mask, int duration = 0,
        int side = 0, int element = -1) => new(action, SpellEffectDescriptor.Classify(action), dice,
            Math.Max(dice, side), element, duration, mask, 'P', 1, 0,
            DiceNum: dice, DiceSide: side, Triggers: "I");
    private static readonly SpellCastDescriptor Hide = new(12913, 1, 2, 0, 4, 512, false,
        0, 0, 3, 0, "", ImmutableArray.Create(Effect(150, 0, "a,A", 1), Effect(128, 1, "a", 2)));
    private static readonly SpellCastDescriptor Pull = new(12735, 1, 2, 2, 8, 533, false,
        2, 1, 0, 0, "", ImmutableArray.Create(Effect(6, 7, "a,A,*e5380"), Effect(6, 11, "a,A,*E5380")));
    private static readonly SpellCastDescriptor Swap = new(12736, 1, 3, 1, 7, 512, false,
        0, 0, 3, 1, "HS!7", ImmutableArray.Create(Effect(8, 0, "a,A")));
    private static readonly SpellCastDescriptor CraPush = new(32426, 1, 3, 1, 6, 580, false,
        3, 2, 0, 0, "", ImmutableArray.Create(Effect(98, 15, "A", side: 17, element: 4), Effect(5, 2, "a,A")));
    private static readonly SpellCastDescriptor Peur = new(12908, 1, 2, 2, 6, 521, false,
        3, 0, 0, 0, "", ImmutableArray.Create(Effect(783, 0, "a,A")));
    private static int Cell(int x) => MapPathfinder.CellFromMapPoint(8 + x, 8);
    private static PlannerSpell Candidate(SpellCastDescriptor descriptor) => new(descriptor, 1, 1,
        ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L, 2L));
    private static CombatTurnSnapshot Board(SpellCastDescriptor descriptor, int self = 2, int enemy = 4) => new(
        Enumerable.Range(0, 9).Select(x => new PlannerCell(Cell(x), true, false,
            new[] { x - 1, x + 1 }.Where(n => n >= 0 && n < 9).Select(Cell).ToImmutableArray())).ToImmutableArray(),
        ImmutableArray.Create(new PlannerFighter(1, Cell(self), false, 500, 500, States: ImmutableHashSet<int>.Empty),
            new PlannerFighter(2, Cell(enemy), true, 500, 500, MovePoints: 0, AttackRange: 1,
                ExpectedDamage: 80, ThreatKnown: true, States: ImmutableHashSet<int>.Empty)),
        1, descriptor.Ap, 2, ImmutableArray.Create(Candidate(descriptor)), ImmutableDictionary<int, int>.Empty,
        Style: CombatStyle.Fleeing, UnsafeForcedMovementCells: ImmutableHashSet<int>.Empty);

    [Theory]
    [InlineData(0, 3, 80)]
    [InlineData(3, 3, 80)]
    [InlineData(0, 0, 0)]
    public void Dodgeable_movement_loss_does_not_guarantee_safety_from_an_enemy_reply(int loss, int observedMp, int incoming)
    {
        var attack = CraPush with { Effects = ImmutableArray.Create(
            Effect(96, 10, "A", element: 3), Effect(1080, loss, "A", duration: 1)) };
        var board = Board(attack, self: 2, enemy: 5) with { Mp = 0, MaxCasts = 1 };
        board = board with { Fighters = board.Fighters.SetItem(1, board.Fighters[1] with { MovePoints = observedMp }) };

        var plan = CombatTurnPlanner.Choose(board);

        Assert.Single(plan.Actions);
        Assert.Equal(incoming, plan.Score.Incoming);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Installed_target_turn_damage_contributes_discounted_future_damage_without_an_immediate_finish(bool endTurn)
    {
        var attack = CraPush with { Effects = ImmutableArray.Create(Effect(96, 10, "A", element: 3)) };
        var pending = new DeferredSpellEffect(endTurn ? 14594 : 29098, 1, Cell(4),
            Effect(endTurn ? 99 : 96, 100, "A", element: 3) with
                { Triggers = endTurn ? "TE" : "TB", EffectTriggerDuration = 1 }, 2);
        var board = Board(attack) with { Mp = 0, ObservedDeferredEffects = ImmutableArray.Create(pending),
            SpellLookup = (_, _) => null };
        var plan = CombatTurnPlanner.Choose(board);
        var baseline = CombatTurnPlanner.Choose(board with { ObservedDeferredEffects = ImmutableArray<DeferredSpellEffect>.Empty });
        Assert.True(plan.Score.Damage > baseline.Score.Damage);
        Assert.Equal(0, plan.Score.Finishes);
        Assert.Equal(baseline.Score.Incoming, plan.Score.Incoming);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(500)]
    public void Installed_Affliction_healing_values_only_missing_life_without_immediate_protection(int life)
    {
        var attack = CraPush with { Effects = ImmutableArray.Create(Effect(96, 10, "A", element: 3)) };
        var board = Board(attack) with { Mp = 0, SpellLookup = (_, _) => null };
        board = board with { Fighters = board.Fighters.SetItem(0, board.Fighters[0] with { Life = life }) };
        var baseline = CombatTurnPlanner.Choose(board);
        var pending = new DeferredSpellEffect(29098, 1, Cell(2),
            Effect(2998, 100, "a", element: 3) with { Triggers = "TB", EffectTriggerDuration = 1 }, 1);
        var plan = CombatTurnPlanner.Choose(board with { ObservedDeferredEffects = ImmutableArray.Create(pending) });

        Assert.Equal(life < 500 ? 50 : 0, plan.Score.Healing - baseline.Score.Healing);
        Assert.Equal(baseline.Score.Incoming, plan.Score.Incoming);
    }

    [Fact]
    public void Affliction_future_damage_can_change_the_selected_cast()
    {
        var immediate = CraPush with { SpellId = 901, Effects = ImmutableArray.Create(Effect(96, 10, "A", element: 3)) };
        var delayed = immediate with { SpellId = 29098, Effects = ImmutableArray.Create(
            Effect(96, 1, "A", element: 3),
            Effect(96, 100, "A", element: 3) with { Triggers = "TB", EffectTriggerDuration = 1 }) };
        var board = Board(immediate) with { Mp = 0, MaxCasts = 1, SpellLookup = (_, _) => null,
            Spells = ImmutableArray.Create(Candidate(immediate), Candidate(delayed)) };

        var plan = CombatTurnPlanner.Choose(board);

        Assert.Equal(29098, Assert.Single(plan.Actions).SpellId);
        Assert.Equal(51, plan.Score.Damage);
        Assert.Equal(0, plan.Score.Finishes);
    }

    [Theory]
    [InlineData(12735)]
    [InlineData(12736)]
    [InlineData(12913)]
    [InlineData(12908)]
    [InlineData(32426)]
    public void Real_learned_utility_descriptors_enter_the_automatic_planner(int id)
    {
        var descriptor = new[] { Pull, Swap, Hide, CraPush, Peur }.Single(d => d.SpellId == id);
        Assert.True(CombatTurnPlanner.IsAdmitted(descriptor));
        Assert.True(AutoSpellRotation.ShouldUsePlanner(new[] { descriptor }));
        var config = Assert.Single(AutoSpellRotation.Build(Array.Empty<SpellConfig>(),
            new[] { new LearnedSpellInfo(id, descriptor.Ap, id == 32426 ? 16 : 0, descriptor) },
            new(RequireDescriptors: true)));
        Assert.Equal(id == 12913, config.SelfBuff);
        Assert.False(config.OnMe); // Real Invisibilite can target an ally as well as self.
        Assert.Equal(id == 12913 ? SpellTargetKind.Ally : SpellTargetKind.Enemy, config.TargetKind);
    }

    [Fact]
    public void Self_only_invisibility_keeps_self_cast_flags()
    {
        var hide = Hide with { Effects = Hide.Effects.Select(e => e with { TargetMask = "C" }).ToImmutableArray() };
        var config = Assert.Single(AutoSpellRotation.Build(Array.Empty<SpellConfig>(),
            new[] { new LearnedSpellInfo(hide.SpellId, hide.Ap, 0, hide) }, new(RequireDescriptors: true)));
        Assert.True(config.OnMe);
        Assert.True(config.SelfBuff);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utility_admission_preserves_unknown_attack_fallback(bool hide)
    {
        var utility = hide ? Hide : Pull;
        Assert.False(AutoSpellRotation.ShouldUsePlanner(new SpellCastDescriptor?[] { utility, null }));
    }

    [Fact]
    public void Push_only_utility_uses_planner_and_can_escape_melee()
    {
        // Isolate the real Cra displacement row from its damage row.
        var push = CraPush with { Effects = ImmutableArray.Create(CraPush.Effects[1]) };
        Assert.True(AutoSpellRotation.ShouldUsePlanner(new[] { push }));
        var plan = CombatTurnPlanner.Choose(Board(push, self: 2, enemy: 3));
        Assert.Equal(PlannerActionKind.Cast, plan.Actions[0].Kind);
        Assert.Contains(plan.Actions.Skip(1), a => a.Kind == PlannerActionKind.Move);
        Assert.Equal(0, plan.Score.Incoming);
    }

    [Fact]
    public void Ranged_attack_then_retreat_spends_remaining_mp_after_cast()
    {
        var attack = CraPush with { Effects = ImmutableArray.Create(CraPush.Effects[0]), Range = 2 };
        var plan = CombatTurnPlanner.Choose(Board(attack));
        Assert.Equal(PlannerActionKind.Cast, plan.Actions[0].Kind);
        Assert.Equal(16, plan.Score.Damage);
        Assert.Equal(2, plan.Actions.Count(a => a.Kind == PlannerActionKind.Move));
        Assert.True(MapPathfinder.CellDistance(plan.Intent!.EndCell, Cell(4)) > 2);
        Assert.Equal(0, plan.Score.Incoming);
    }

    [Fact]
    public void Cra_push_breaks_confirmed_melee_then_retreats_without_invented_tackle_cost()
    {
        var board = Board(CraPush, self: 2, enemy: 3);
        var plan = CombatTurnPlanner.Choose(board);
        Assert.Equal(PlannerActionKind.Cast, plan.Actions[0].Kind);
        Assert.Equal(32426, plan.Actions[0].SpellId);
        Assert.Contains(plan.Actions.Skip(1), a => a.Kind == PlannerActionKind.Move);
        Assert.True(MapPathfinder.CellDistance(plan.Intent!.EndCell, Cell(5)) > 1);
        Assert.Equal(0, plan.Score.Incoming);
    }

    [Fact]
    public void Real_invisibility_resolves_visibility_and_mp_and_is_selected_when_threatened()
    {
        var board = Board(Hide, self: 2, enemy: 3) with { Mp = 0 };
        var resolved = SpellResolver.Resolve(new SpellBoard(new[] {
            new SpellFighter(1, Cell(2), false, 500, 500, Mp: 0) }),
            0, Cell(2), Hide, (_, _) => null, 60);
        Assert.Empty(resolved.Unmodeled);
        Assert.Equal(FighterVisibility.Invisible, resolved.Board.Fighters[0].Visibility);
        Assert.Equal(1, resolved.Board.Fighters[0].Mp);
        Assert.Single(resolved.Events.OfType<VisibilityResolved>());
        var plan = CombatTurnPlanner.Choose(board);
        Assert.Contains(plan.Actions, a => a.SpellId == 12913 && a.Target == 1);
        Assert.True(plan.Score.Shielding > 0);
        Assert.True(plan.Score.Incoming > 0); // Invisibility never guarantees immunity to area damage.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_cast_that_hides_then_reveals_cannot_keep_the_invisibility_reward(bool shield)
    {
        var attack = Hide with
        {
            Effects = (shield ? new[] { Effect(1040, 100, "C", 1) } : Array.Empty<SpellEffectDescriptor>())
                .Concat(new[] { Effect(150, 0, "C", 1), Effect(99, 100, "A", element: 2) }).ToImmutableArray()
        };
        var snapshot = Board(attack, self: 2, enemy: 3) with { Mp = 0 };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Contains(plan.Actions, action => action.Kind == PlannerActionKind.Cast);
        Assert.True(plan.Score.Damage > 0);
        Assert.Equal(shield ? 40 : 0, plan.Score.Shielding);
    }

    [Fact]
    public void Invisibility_mobility_then_direct_attack_keeps_damage_but_loses_hidden_protection()
    {
        var attack = new SpellCastDescriptor(12902, 1, 3, 1, 2, 512, false,
            0, 0, 0, 0, "", ImmutableArray.Create(Effect(97, 100, "A", element: 1)));
        var snapshot = Board(Hide, self: 2, enemy: 5) with
        {
            Ap = 5,
            Mp = 0,
            Spells = ImmutableArray.Create(Candidate(Hide), Candidate(attack)),
            Style = CombatStyle.Aggressive
        };

        var plan = CombatTurnPlanner.Choose(snapshot);

        Assert.Contains(plan.Actions, action => action.SpellId == 12913);
        Assert.Contains(plan.Actions, action => action.Kind == PlannerActionKind.Move);
        Assert.Contains(plan.Actions, action => action.SpellId == 12902);
        Assert.True(plan.Score.Damage > 0);
        Assert.Equal(0, plan.Score.Shielding);
    }

    [Fact]
    public void Retreat_never_walks_through_observed_mark_cells()
    {
        var attack = CraPush with { Effects = ImmutableArray.Create(CraPush.Effects[0]), Range = 2 };
        var board = Board(attack) with { UnsafeForcedMovementCells = ImmutableHashSet.Create(Cell(1)) };
        var plan = CombatTurnPlanner.Choose(board);
        Assert.DoesNotContain(plan.Actions, a => a.Kind == PlannerActionKind.Move && a.Cell == Cell(1));
        Assert.Equal(16, plan.Score.Damage);
    }

    [Fact]
    public void Real_peur_pushes_the_adjacent_fighter_to_empty_destination()
    {
        Assert.Equal(SpellEffectKind.Push, Peur.Effects[0].Kind);
        Assert.True(Peur.Effects[0].IsCertain);
        Assert.True(Peur.CanPlan);
        Assert.False((CraPush with { Effects = ImmutableArray.Create(CraPush.Effects[1]), Flags = 521 }).CanPlan);
        Assert.Empty(SpellResolver.ResolverCoverage(Peur));
        var board = new SpellBoard(new[] {
            new SpellFighter(1, Cell(2), false, 500, 500),
            new SpellFighter(2, Cell(3), true, 500, 500) });
        var resolved = SpellResolver.Resolve(board, 0, Cell(7), Peur, (_, _) => null, 60);
        Assert.Empty(resolved.Unmodeled);
        var movement = Assert.Single(resolved.Movements);
        Assert.Equal(new MovementResolved(783, 1, 2, Cell(3), Cell(7), false), movement);
        Assert.Equal(Cell(7), resolved.Board.Fighters[1].Cell);
        Assert.Equal(Cell(3), board.Fighters[1].Cell);
    }

    [Theory]
    [InlineData(false, "a,A", true)]
    [InlineData(true, "a,A", true)]
    [InlineData(false, "A", false)]
    [InlineData(true, "a", false)]
    public void Peur_applies_masks_to_the_adjacent_fighter(bool enemy, string mask, bool moves)
    {
        var spell = Peur with { Effects = ImmutableArray.Create(Peur.Effects[0] with { TargetMask = mask }) };
        var board = new SpellBoard(new[] { new SpellFighter(1, Cell(2), false, 500, 500),
            new SpellFighter(2, Cell(3), enemy, 500, 500) });
        var resolved = SpellResolver.Resolve(board, 0, Cell(6), spell, (_, _) => null, 60);
        Assert.Empty(resolved.Unmodeled);
        Assert.Equal(moves ? Cell(6) : Cell(3), resolved.Board.Fighters[1].Cell);
        Assert.Equal(moves ? 1 : 0, resolved.Movements.Count);
    }

    [Theory]
    [InlineData(0)] // No adjacent fighter.
    [InlineData(1)] // Dead adjacent fighter.
    [InlineData(2)] // Occupied cast cell.
    [InlineData(3)] // Non-cardinal destination.
    [InlineData(4)] // Destination is only one cell away.
    [InlineData(5)] // Unwalkable cast cell.
    public void Peur_invalid_or_empty_lines_move_nobody(int scenario)
    {
        var fighters = new List<SpellFighter> { new(1, Cell(2), false, 500, 500) };
        if (scenario != 0) fighters.Add(new(2, Cell(3), true, scenario == 1 ? 0 : 500, 500, Alive: scenario != 1));
        if (scenario == 2) fighters.Add(new(3, Cell(6), true, 500, 500));
        int destination = scenario == 3 ? MapPathfinder.CellFromMapPoint(12, 9) : scenario == 4 ? Cell(3) : Cell(6);
        var resolved = SpellResolver.Resolve(new(fighters), 0, destination, Peur, (_, _) => null, 60,
            c => scenario != 5 || c != destination);
        Assert.Empty(resolved.Events);
        Assert.Empty(resolved.Unmodeled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Peur_stops_at_obstructions_and_planner_rejects_collision(bool occupied)
    {
        var board = new SpellBoard(new[] { new SpellFighter(1, Cell(2), false, 500, 500),
            new SpellFighter(2, Cell(3), true, 500, 500) }
            .Concat(occupied ? new[] { new SpellFighter(3, Cell(5), false, 500, 500) } : Array.Empty<SpellFighter>()).ToArray());
        var resolved = SpellResolver.Resolve(board, 0, Cell(7), Peur, (_, _) => null, 60,
            c => occupied || c != Cell(5));
        Assert.Equal(new MovementResolved(783, 1, 2, Cell(3), Cell(4), true), Assert.Single(resolved.Movements));
        Assert.Contains(resolved.Unmodeled, e => e.ActionId == 783 && e.Reason.Contains("collision"));
        var snapshot = Board(Peur, self: 2, enemy: 3);
        snapshot = snapshot with {
            Spells = ImmutableArray.Create(Candidate(Peur) with { AllowedCells = ImmutableHashSet.Create(Cell(7)) }),
            Cells = snapshot.Cells.Select(c => c.Id == Cell(5) && !occupied ? c with { Walkable = false } : c).ToImmutableArray(),
            Fighters = occupied ? snapshot.Fighters.Add(new(3, Cell(5), false, 500, 500)) : snapshot.Fighters };
        Assert.DoesNotContain(CombatTurnPlanner.Choose(snapshot).Actions, a => a.Kind == PlannerActionKind.Cast);
    }

    [Fact]
    public void Sram_peur_breaks_melee_then_kites_without_invented_tackle_cost()
    {
        var board = Board(Peur, self: 2, enemy: 3);
        var plan = CombatTurnPlanner.Choose(board);
        var cast = plan.Actions[0];
        Assert.Equal(PlannerActionKind.Cast, cast.Kind);
        Assert.Equal(12908, cast.SpellId);
        Assert.Equal(0, cast.Target); // The configured cast center is empty.
        Assert.InRange(MapPathfinder.CellDistance(Cell(2), cast.Cell), 2, 6);
        Assert.Contains(plan.Actions.Skip(1), a => a.Kind == PlannerActionKind.Move);
        Assert.True(MapPathfinder.CellDistance(plan.Intent!.EndCell, cast.Cell) > 1);
        Assert.Equal(0, plan.Score.Incoming);
        Assert.Equal(0, plan.Unmodeled);
    }

    [Theory]
    [InlineData(0)] // Unknown movement interactions.
    [InlineData(1)] // Observed trap along the forced path.
    [InlineData(2)] // Adjacent fighter is forbidden by configuration.
    [InlineData(3)] // Adjacent fighter exhausted its target cap.
    public void Peur_preserves_path_safety_and_affected_target_gates(int scenario)
    {
        var board = Board(Peur, self: 2, enemy: 3);
        var candidate = Candidate(Peur) with { AllowedCells = ImmutableHashSet.Create(Cell(7)) };
        if (scenario == 2) candidate = candidate with { AllowedTargets = ImmutableHashSet.Create(1L) };
        if (scenario == 3) candidate = candidate with { TargetCasts = ImmutableDictionary<long, int>.Empty.Add(2, 1) };
        board = board with { Spells = ImmutableArray.Create(candidate),
            UnsafeForcedMovementCells = scenario == 0 ? null : scenario == 1 ? ImmutableHashSet.Create(Cell(5)) : ImmutableHashSet<int>.Empty };
        Assert.DoesNotContain(CombatTurnPlanner.Choose(board).Actions, a => a.Kind == PlannerActionKind.Cast);
    }

    [Fact]
    public void Real_bomb_hooks_remain_explicitly_unsupported()
    {
        // SpellLevelData 41922 (Explobombe), unsupported rows retained.
        var bomb = new SpellCastDescriptor(13444, 1, 2, 1, 6, 516, false, 2, 1, 0, 0, "",
            ImmutableArray.Create(Effect(1008, 3112, "a,A", side: 1),
                Effect(406, 0, "a,P,F3112,F3113,F3114,F5161") with { Value = 13441 },
                Effect(951, 641, "a") with { Value = 641 },
                Effect(951, 665, "A") with { Value = 665 }));
        Assert.False(CombatTurnPlanner.IsAdmitted(bomb));
        Assert.Contains(SpellResolver.ResolverCoverage(bomb), e => e.ActionId == 406);
    }
}
