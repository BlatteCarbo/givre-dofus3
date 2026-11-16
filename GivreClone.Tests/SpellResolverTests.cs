extern alias F06App;

using System.Collections.Immutable;
using GivreClone.Protocol;
using Xunit;
using Xunit.Abstractions;
using Combat = F06App::GivreClone.App.Services.Combat;
using GameNameResolver = F06App::GivreClone.App.Services.GameNameResolver;
using StateRule = F06App::GivreClone.App.Services.LotM.SpellStateDamageRule;

namespace GivreClone.Tests;

public class SpellResolverTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void Next_turn_expires_only_simulated_states_whose_duration_is_consumed(int duration, bool remains)
    {
        var board = new Combat.SpellBoard(new[] {
            new Combat.SpellFighter(1, 200, false, 100, 100,
                States: ImmutableHashSet.Create(263, 713))
        }, TimedStates: new[] { new Combat.SimulatedState(1, 713, duration) });

        var next = Combat.SpellResolver.AdvanceStatBuffs(board);

        Assert.Equal(remains, next.Fighters[0].States!.Contains(713));
        Assert.Contains(263, next.Fighters[0].States!);
        Assert.Equal(!remains, next.Fighters[0].AbsentStates!.Contains(713));
        if (remains) Assert.Equal(duration - 1, Assert.Single(next.TimedStates!).Duration);
        else Assert.Empty(next.TimedStates!);
        Assert.Contains(713, board.Fighters[0].States!);
        Assert.Equal(duration, Assert.Single(board.TimedStates!).Duration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_Persiflage_subcast_steals_range_only_when_projected(bool projected)
    {
        var names = new GameNameResolver();
        names.EnsureLoaded(); names.EnsureCombatLoaded();
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, 200, false, 100, 100,
                Characteristics: ImmutableDictionary<int, int>.Empty.Add(19, 2)),
            new Combat.SpellFighter(2, 230, true, 1000, 1000,
                Characteristics: ImmutableDictionary<int, int>.Empty.Add(19, 5))
        }, ThroughPortal: projected);
        var result = Combat.SpellResolver.Resolve(board, 0, 230, names.GetSpellDescriptor(14620, 3)!,
            (id, grade) => names.GetSpellDescriptor(id, grade), 187);

        Assert.DoesNotContain(result.Unmodeled, e => e.ActionId == 320);
        Assert.Equal(projected ? 5 : 2, result.Board.Fighters[0].Characteristics![19]);
        Assert.Equal(2, result.Board.Fighters[1].Characteristics![19]);
        var expired = Combat.SpellResolver.AdvanceStatBuffs(result.Board);
        Assert.Equal(2, expired.Fighters[0].Characteristics![19]);
        Assert.Equal(5, expired.Fighters[1].Characteristics![19]);
        Assert.Equal(2, board.Fighters[0].Characteristics![19]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Extinction_triggers_only_at_its_bound_targets_end_turn_and_expires(bool projected)
    {
        var names = new GameNameResolver();
        names.EnsureLoaded(); names.EnsureCombatLoaded();
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, 200, false, 100, 100),
            new Combat.SpellFighter(2, 230, true, 1000, 1000)
        }, ThroughPortal: projected);
        var installed = Combat.SpellResolver.Resolve(board, 0, 230, names.GetSpellDescriptor(14594, 2)!,
            (id, grade) => names.GetSpellDescriptor(id, grade), 180);
        var later = installed.Board with { ThroughPortal = false };
        var start = Combat.SpellResolver.ResolveStartTurn(installed.Board, 2,
            (id, grade) => names.GetSpellDescriptor(id, grade), 180);
        Assert.Equal(installed.Board.Fighters[1].Life, start.Board.Fighters[1].Life);
        var other = Combat.SpellResolver.ResolveEndTurn(installed.Board, 1,
            (id, grade) => names.GetSpellDescriptor(id, grade), 180);
        Assert.Equal(installed.Board.Fighters[1].Life, other.Board.Fighters[1].Life);
        var end = Combat.SpellResolver.ResolveEndTurn(later, 2,
            (id, grade) => names.GetSpellDescriptor(id, grade), 180);
        Assert.Equal(projected ? 33.5 : 0, installed.Board.Fighters[1].Life - end.Board.Fighters[1].Life);
        var repeated = Combat.SpellResolver.ResolveEndTurn(end.Board, 2,
            (id, grade) => names.GetSpellDescriptor(id, grade), 180);
        Assert.Equal(end.Board.Fighters[1].Life, repeated.Board.Fighters[1].Life);
        Assert.Empty(end.Board.DeferredEffects!);
    }

    [Fact]
    public void Affliction_start_turn_heals_only_its_bound_ally_and_expires()
    {
        var names = new GameNameResolver();
        names.EnsureLoaded(); names.EnsureCombatLoaded();
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, 200, false, 50, 100),
            new Combat.SpellFighter(2, 230, false, 50, 100)
        }, ThroughPortal: true);
        var installed = Combat.SpellResolver.Resolve(board, 0, 230, names.GetSpellDescriptor(14584, 2)!,
            (id, grade) => names.GetSpellDescriptor(id, grade), 180);
        var triggered = Combat.SpellResolver.ResolveStartTurn(installed.Board, 2,
            (id, grade) => names.GetSpellDescriptor(id, grade), 180);

        Assert.True(triggered.Board.Fighters[1].Life > installed.Board.Fighters[1].Life);
        Assert.Equal(installed.Board.Fighters[0].Life, triggered.Board.Fighters[0].Life);
        Assert.Contains(triggered.Events, e => e is Combat.HealingResolved { TargetId: 2, ActionId: 2998 });
        Assert.DoesNotContain(triggered.Board.DeferredEffects!, e => e.TargetId == 2 && e.SpellId == 29098);
    }

    [Fact]
    public void Affliction_start_turn_damage_follows_its_bound_target_after_movement()
    {
        var names = new GameNameResolver();
        names.EnsureLoaded();
        names.EnsureCombatLoaded();
        var affliction = names.GetSpellDescriptor(14584, 2)!;
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, 200, false, 100, 100),
            new Combat.SpellFighter(2, 230, true, 1000, 1000),
            new Combat.SpellFighter(3, 240, true, 1000, 1000)
        }, ThroughPortal: true);
        var installed = Combat.SpellResolver.Resolve(board, 0, 230, affliction,
            (id, grade) => names.GetSpellDescriptor(id, grade), 180);
        Assert.Contains(installed.Deferred, effect => effect.SpellId == 29098 && effect.Effect.ActionId == 96);
        var moved = installed.Board with { Fighters = installed.Board.Fighters.Select(f =>
            f.Id == 2 ? f with { Cell = 260 } : f.Id == 1
                ? f with { Visibility = FighterVisibility.Invisible } : f).ToImmutableArray() };

        var unrelated = Combat.SpellResolver.ResolveStartTurn(moved, 3,
            (id, grade) => names.GetSpellDescriptor(id, grade), 180);
        Assert.Equal(moved.Fighters[1].Life, unrelated.Board.Fighters[1].Life);
        var triggered = Combat.SpellResolver.ResolveStartTurn(moved, 2,
            (id, grade) => names.GetSpellDescriptor(id, grade), 180);
        Assert.True(triggered.Board.Fighters[1].Life < moved.Fighters[1].Life);
        Assert.Equal(moved.Fighters[2].Life, triggered.Board.Fighters[2].Life);
        Assert.Equal(FighterVisibility.Invisible, triggered.Board.Fighters[0].Visibility);
        Assert.Equal(230, installed.Board.Fighters[1].Cell);
        Assert.Contains(triggered.Events, e => e is Combat.DamageResolved { TargetId: 2 });
        var repeated = Combat.SpellResolver.ResolveStartTurn(triggered.Board, 2,
            (id, grade) => names.GetSpellDescriptor(id, grade), 180);
        Assert.Equal(triggered.Board.Fighters[1].Life, repeated.Board.Fighters[1].Life);
        Assert.DoesNotContain(triggered.Board.DeferredEffects!, e => e.SpellId == 29098 && e.TargetId == 2);
        var prolonged = moved with { DeferredEffects = moved.DeferredEffects!.Select(e =>
            e with { Effect = e.Effect with { EffectTriggerDuration = 2 } }).ToImmutableArray() };
        var first = Combat.SpellResolver.ResolveStartTurn(prolonged, 2,
            (id, grade) => names.GetSpellDescriptor(id, grade), 180);
        Assert.All(first.Board.DeferredEffects!, e => Assert.Equal(1, e.Effect.EffectTriggerDuration));
        var last = Combat.SpellResolver.ResolveStartTurn(first.Board, 2,
            (id, grade) => names.GetSpellDescriptor(id, grade), 180);
        Assert.True(last.Board.Fighters[1].Life < first.Board.Fighters[1].Life);
        Assert.Empty(last.Board.DeferredEffects!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sram_reveal_applies_critical_reward_only_from_its_installed_passive(bool installed)
    {
        var names = new GameNameResolver();
        names.EnsureLoaded();
        names.EnsureCombatLoaded();
        var attack = names.GetSpellDescriptor(12902, 3)!;
        var passive = names.GetSpellDescriptor(21978, 1)!;
        var trigger = Assert.Single(passive.Effects, effect => effect.Triggers == "IOFF");
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, 200, false, 100, 100, Visibility: FighterVisibility.Invisible,
                Characteristics: ImmutableDictionary<int, int>.Empty.Add(18, 20)),
            new Combat.SpellFighter(2, 230, true, 1000, 1000)
        }, DeferredEffects: installed ? new[] { new Combat.DeferredSpellEffect(21978, 1, 200, trigger, 1) }
            : Array.Empty<Combat.DeferredSpellEffect>());

        var result = Combat.SpellResolver.Resolve(board, 0, 230, attack,
            (id, grade) => names.GetSpellDescriptor(id, grade), 187);

        Assert.Equal(installed ? 35 : 20, result.Board.Fighters[0].Characteristics![18]);
        Assert.Equal(FighterVisibility.Visible, result.Board.Fighters[0].Visibility);
        Assert.Equal(20, board.Fighters[0].Characteristics![18]);
        if (installed)
        {
            Assert.Contains(result.Board.DeferredEffects!, effect => effect.SpellId == 21979 && effect.Triggers == "CC");
            Assert.Contains(result.Board.StatBuffs!, buff => buff.SpellId == 21979 && buff.Delta == 15 && buff.Duration == 1);
            var second = Combat.SpellResolver.Resolve(result.Board, 0, 230, attack,
                (id, grade) => names.GetSpellDescriptor(id, grade), 187);
            Assert.Equal(35, second.Board.Fighters[0].Characteristics![18]);
            Assert.Single(second.Board.StatBuffs!, buff => buff.SpellId == 21979);
            var expired = Combat.SpellResolver.AdvanceStatBuffs(result.Board);
            Assert.Equal(20, expired.Fighters[0].Characteristics![18]);
            Assert.DoesNotContain(expired.DeferredEffects!, effect => effect.SpellId == 21979);
            Assert.Contains(expired.DeferredEffects!, effect => effect.SpellId == 21978);
            Assert.Equal(35, Combat.SpellResolver.ConsumeCriticalTriggers(result.Board, 2)
                .Fighters[0].Characteristics![18]);
            var consumed = Combat.SpellResolver.ConsumeCriticalTriggers(result.Board, 1);
            Assert.Equal(20, consumed.Fighters[0].Characteristics![18]);
            Assert.DoesNotContain(consumed.StatBuffs!, buff => buff.SpellId == 21979);
            Assert.DoesNotContain(consumed.DeferredEffects!, effect => effect.SpellId == 21979);
            Assert.Contains(consumed.DeferredEffects!, effect => effect.SpellId == 21978);
            Assert.Equal(35, result.Board.Fighters[0].Characteristics![18]);
            var revealingCritical = Combat.SpellResolver.Resolve(board, 0, 230, attack,
                (id, grade) => names.GetSpellDescriptor(id, grade), 187, critical: true);
            Assert.Equal(20, revealingCritical.Board.Fighters[0].Characteristics![18]);
            Assert.DoesNotContain(revealingCritical.Board.DeferredEffects!, effect => effect.SpellId == 21979);
            Assert.Contains(revealingCritical.Board.DeferredEffects!, effect => effect.SpellId == 21978);
        }
    }

    [Theory]
    [InlineData(FighterVisibility.Invisible, 0)]
    [InlineData(FighterVisibility.Detected, 0)]
    [InlineData(FighterVisibility.Invisible, 1000)]
    public void Actual_Sram_direct_attack_reveals_its_hidden_caster(FighterVisibility visibility, int shield)
    {
        var names = new GameNameResolver();
        names.EnsureLoaded();
        names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(12902, 3)
            ?? throw new InvalidOperationException("Missing Sram spell 12902 grade 3");
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, 200, false, 100, 100, Visibility: visibility),
            new Combat.SpellFighter(2, 230, true, 1000, 1000, Shield: shield)
        });

        var result = Combat.SpellResolver.Resolve(board, 0, 230, spell,
            (id, grade) => names.GetSpellDescriptor(id, grade), 187);

        Assert.True(result.EnemyDamage > 0);
        Assert.Equal(FighterVisibility.Visible, result.Board.Fighters[0].Visibility);
        Assert.Contains(result.Events, effect => effect is Combat.VisibilityResolved
            { TargetId: 1, Visibility: FighterVisibility.Visible });
        Assert.Equal(visibility, board.Fighters[0].Visibility);
    }

    [Theory]
    [InlineData(12921, 2)]
    [InlineData(29098, 3)]
    [InlineData(12908, 3)]
    public void Trap_placement_pending_poison_and_push_do_not_reveal_the_caster(int id, int grade)
    {
        var names = new GameNameResolver();
        names.EnsureLoaded();
        names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(id, grade)
            ?? throw new InvalidOperationException($"Missing spell {id} grade {grade}");
        var board = Board();
        board = board with { Fighters = board.Fighters.Select((fighter, index) => index == 0
            ? fighter with { Visibility = FighterVisibility.Invisible } : fighter).ToImmutableArray() };

        var result = Combat.SpellResolver.Resolve(board, 0, 230, spell,
            (child, rank) => names.GetSpellDescriptor(child, rank), 187);

        Assert.Equal(FighterVisibility.Invisible, result.Board.Fighters[0].Visibility);
        Assert.DoesNotContain(result.Events, effect => effect is Combat.VisibilityResolved
            { Visibility: FighterVisibility.Visible });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Affliction_binds_projected_start_turn_effects_to_the_affected_fighter(bool projected, bool ally)
    {
        var names = new GameNameResolver();
        names.EnsureLoaded();
        names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(14584, 2)
            ?? throw new InvalidOperationException("Missing Affliction grade 2");
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, 200, false, 100, 100),
            new Combat.SpellFighter(2, 230, !ally, 1000, 1000)
        }, ThroughPortal: projected);

        var result = Combat.SpellResolver.Resolve(board, 0, 230, spell,
            (id, grade) => names.GetSpellDescriptor(id, grade), 186);
        var pending = result.Deferred.Where(effect => effect.SpellId == 29098 && effect.Triggers == "TB").ToArray();

        Assert.Equal(projected ? 2 : 0, pending.Length);
        Assert.All(pending, effect =>
        {
            Assert.Equal(2L, effect.TargetId);
            Assert.Contains(effect.Effect.ActionId, new[] { 3793, ally ? 2998 : 96 });
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Extinction_keeps_its_end_turn_effects_only_when_cast_through_portals(bool projected)
    {
        var names = new GameNameResolver();
        names.EnsureLoaded();
        names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(14594, 2)
            ?? throw new InvalidOperationException("Missing Extinction grade 2");

        var result = Combat.SpellResolver.Resolve(Board() with { ThroughPortal = projected },
            0, 230, spell, (_, _) => null, 186);

        Assert.Equal(projected ? 2 : 0, result.Deferred.Count);
        Assert.All(result.Deferred, effect =>
        {
            Assert.Equal("TE", effect.Triggers);
            Assert.Equal(2L, effect.TargetId);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_Outrage_range_bonus_requires_projection_and_expires_without_mutating_input(bool projected)
    {
        var names = new GameNameResolver();
        names.EnsureLoaded();
        names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(14608, 3)
            ?? throw new InvalidOperationException("Missing Outrage grade 3");
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, 200, false, 100, 100,
                Characteristics: ImmutableDictionary<int, int>.Empty.Add(19, 4)),
            new Combat.SpellFighter(2, 230, true, 1000, 1000)
        }, ThroughPortal: projected);

        var result = Combat.SpellResolver.Resolve(board, 0, 230, spell, (_, _) => null, 180);

        Assert.Empty(result.Unmodeled);
        Assert.Equal(projected ? 5 : 4, result.Board.Fighters[0].Characteristics![19]);
        Assert.Equal(4, board.Fighters[0].Characteristics![19]);
        var expired = Combat.SpellResolver.AdvanceStatBuffs(Combat.SpellResolver.AdvanceStatBuffs(result.Board));
        Assert.Equal(4, expired.Fighters[0].Characteristics![19]);
        Assert.Equal(projected ? 5 : 4, result.Board.Fighters[0].Characteristics![19]);
    }

    [Fact]
    public void Portal_damage_multiplier_precedes_resistance_and_shield_and_life_steal_uses_life_lost()
    {
        var board = Board();
        board = board with { Fighters = board.Fighters.Select((f, i) => i == 1 ? f with
        {
            Shield = 20,
            Resistances = ImmutableDictionary<Combat.DamageElement, int>.Empty.Add(Combat.DamageElement.Water, 50),
            Reductions = ImmutableDictionary<Combat.DamageElement, int>.Empty.Add(Combat.DamageElement.Water, 10)
        } : f).ToArray() };

        var result = Combat.SpellResolver.Resolve(board, 0, 230, Spell(Effect(91, 100)),
            (_, _) => null, 178, damageMultiplier: 1.5);

        Assert.Equal(65, result.EnemyDamage);
        Assert.Equal(955, result.Board.Fighters[1].Life);
        Assert.Equal(0, result.Board.Fighters[1].Shield);
        Assert.Equal(22, result.Healing);
        Assert.Equal(72, result.Board.Fighters[0].Life);
        Assert.Equal(1000, board.Fighters[1].Life);
        Assert.Equal(20, board.Fighters[1].Shield);
    }

    [Fact]
    public void Neutral_disables_only_the_portal_on_its_cell_and_refunds_its_ap()
    {
        var board = new Combat.SpellBoard(new[] { new Combat.SpellFighter(1, 200, false, 100, 100, Ap: 5) },
            Marks: new[]
            {
                new Combat.SpellMark(1181, 2, 1, new[] { 201 }),
                new Combat.SpellMark(1181, 2, 1, new[] { 220 })
            });
        var spell = Spell(Effect(1183, 0, "a,A") with { Duration = 1 }, Effect(120, 1, "C"));
        var result = Combat.SpellResolver.Resolve(board, 0, 201, spell, (_, _) => null, 177);

        Assert.Empty(result.Unmodeled);
        Assert.Single(result.Board.Marks!, m => m.ActionId == 1181 && m.Cells.Contains(220));
        Assert.DoesNotContain(result.Board.Marks!, m => m.ActionId == 1181 && m.Cells.Contains(201));
        Assert.Equal(6, result.Board.Fighters[0].Ap);
        Assert.Equal(2, board.Marks!.Count);
    }

    [Theory]
    [InlineData(414, 70)]
    [InlineData(415, -30)]
    public void Push_damage_bonus_changes_only_its_characteristic_and_expires(int action, int expected)
    {
        var board = new Combat.SpellBoard(new[] { new Combat.SpellFighter(1, 200, false, 100, 100,
            Characteristics: ImmutableDictionary<int, int>.Empty.Add(84, 20).Add(25, 35)) });
        var spell = Spell(Effect(action, 50, "C") with { Duration = 1 });

        var result = Combat.SpellResolver.Resolve(board, 0, 200, spell, (_, _) => null, 161);

        Assert.Empty(result.Unmodeled);
        Assert.Equal(expected, result.Board.Fighters[0].Characteristics![84]);
        Assert.Equal(35, result.Board.Fighters[0].Characteristics![25]);
        var expired = Combat.SpellResolver.AdvanceStatBuffs(result.Board);
        Assert.Equal(20, expired.Fighters[0].Characteristics![84]);
        Assert.Empty(expired.StatBuffs!);
        Assert.Equal(20, board.Fighters[0].Characteristics![84]);
    }

    [Fact]
    public void Expiring_stat_buffs_removes_only_the_ending_bonus_and_preserves_input()
    {
        var board = new Combat.SpellBoard(new[] { new Combat.SpellFighter(1, 200, false, 100, 100,
            Characteristics: ImmutableDictionary<int, int>.Empty.Add(Combat.CharIds.DamagePercent, 175)) },
            StatBuffs: new[]
            {
                new Combat.SpellStatBuff(10, 1, Combat.CharIds.DamagePercent, 100, 1),
                new Combat.SpellStatBuff(11, 1, Combat.CharIds.DamagePercent, 50, 2)
            });

        var next = Combat.SpellResolver.AdvanceStatBuffs(board);

        Assert.Equal(75, next.Fighters[0].Characteristics![Combat.CharIds.DamagePercent]);
        Assert.Equal(1, Assert.Single(next.StatBuffs!).Duration);
        Assert.Equal(175, board.Fighters[0].Characteristics![Combat.CharIds.DamagePercent]);
    }

    [Fact]
    public void Source_refresh_after_new_stat_rows_keeps_this_cast_and_removes_previous_cast()
    {
        var board = new Combat.SpellBoard(new[] { new Combat.SpellFighter(1, 200, false, 100, 100) },
            KnownStatBuffSources: ImmutableHashSet.Create(14617));
        var spell = Spell(Effect(138, 100, "C") with { Duration = 2 },
            Effect(406, 0, "C") with { Value = 14617 }) with { SpellId = 14617 };
        var first = Combat.SpellResolver.Resolve(board, 0, 200, spell, (_, _) => null, 177);
        var second = Combat.SpellResolver.Resolve(first.Board, 0, 200, spell, (_, _) => null, 177);

        Assert.Empty(second.Unmodeled);
        Assert.Equal(100, second.Board.Fighters[0].Characteristics![Combat.CharIds.DamagePercent]);
        Assert.Single(second.Board.StatBuffs!);
    }

    [Theory]
    [InlineData(false, 100, 1)]
    [InlineData(true, 200, 2)]
    public void Cabale_recast_refreshes_actual_power_and_movement_bonus(bool throughPortal, int power, int movement)
    {
        var names = new GameNameResolver();
        names.EnsureLoaded();
        names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(14617, 3)
            ?? throw new InvalidOperationException("Missing Cabale grade 3");
        var board = new Combat.SpellBoard(new[] { new Combat.SpellFighter(1, 200, false, 100, 100, Mp: 3) },
            ThroughPortal: throughPortal, KnownStatBuffSources: ImmutableHashSet.Create(14617));
        var first = Combat.SpellResolver.Resolve(board, 0, 200, spell, (_, _) => null, 177);
        var second = Combat.SpellResolver.Resolve(first.Board, 0, 200, spell, (_, _) => null, 177);

        Assert.Empty(first.Unmodeled);
        Assert.Empty(second.Unmodeled);
        Assert.Equal(power, second.Board.Fighters[0].Characteristics![Combat.CharIds.DamagePercent]);
        Assert.Equal(3 + movement, second.Board.Fighters[0].Mp);
        Assert.Equal(3, board.Fighters[0].Mp);
    }

    [Fact]
    public void Removing_source_buffs_without_observed_inventory_remains_unmodeled()
    {
        var board = new Combat.SpellBoard(new[] { new Combat.SpellFighter(1, 200, false, 100, 100) });
        var result = Combat.SpellResolver.Resolve(board, 0, 200,
            Spell(Effect(406, 0, "C") with { Value = 14617 }), (_, _) => null, 100);

        Assert.Contains(result.Unmodeled, e => e.ActionId == 406);
        Assert.Empty(result.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cabale_can_refresh_from_a_complete_initially_empty_inventory(bool throughPortal)
    {
        var names = new GameNameResolver();
        names.EnsureLoaded();
        names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(14617, 3)!;
        var board = new Combat.SpellBoard(new[] { new Combat.SpellFighter(1, 200, false, 100, 100, Mp: 3) },
            ThroughPortal: throughPortal, AllStatBuffSourcesKnown: true);

        var first = Combat.SpellResolver.Resolve(board, 0, 200, spell, (_, _) => null, 182);
        var second = Combat.SpellResolver.Resolve(first.Board, 0, 200, spell, (_, _) => null, 182);

        Assert.Empty(first.Unmodeled);
        Assert.Empty(second.Unmodeled);
        Assert.Equal(throughPortal ? 200 : 100, second.Board.Fighters[0].Characteristics![Combat.CharIds.DamagePercent]);
        Assert.Equal(throughPortal ? 5 : 4, second.Board.Fighters[0].Mp);
        Assert.True(second.Board.AllStatBuffSourcesKnown);
    }

    [Fact]
    public void Recasting_a_source_spell_replaces_its_power_instead_of_stacking_it()
    {
        var board = new Combat.SpellBoard(new[] { new Combat.SpellFighter(1, 200, false, 100, 100) },
            KnownStatBuffSources: ImmutableHashSet.Create(14617));
        var buff = Spell(Effect(406, 0, "C") with { Value = 14617 },
            Effect(138, 100, "C") with { Duration = 2 }) with { SpellId = 14617 };
        var first = Combat.SpellResolver.Resolve(board, 0, 200, buff, (_, _) => null, 100);
        var second = Combat.SpellResolver.Resolve(first.Board, 0, 200, buff, (_, _) => null, 100);

        Assert.Empty(first.Unmodeled);
        Assert.Empty(second.Unmodeled);
        Assert.Equal(100, second.Board.Fighters[0].Characteristics![Combat.CharIds.DamagePercent]);
        Assert.Null(board.Fighters[0].Characteristics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Portal_buff_changes_the_next_damage_or_heal_without_changing_the_input_board(bool healing,
        bool observedBaseline = false, bool projected = false)
    {
        var board = new Combat.SpellBoard(new[]
        {
            new Combat.SpellFighter(1, 200, false, 100, healing ? 1000 : 100,
                Characteristics: observedBaseline ? ImmutableDictionary<int, int>.Empty
                    .Add(healing ? Combat.CharIds.DealtHealMultiplier : Combat.CharIds.DealtDamageMultiplier, 100) : null),
            new Combat.SpellFighter(2, 201, true, 1000, 1000)
        });
        var buff = Spell(Effect(healing ? 2971 : 1171, 2, "C") with { Duration = 3 });
        var boosted = Combat.SpellResolver.Resolve(board, 0, 200, buff, (_, _) => null, 100);
        var attack = Spell(Effect(healing ? 108 : 100, 100, healing ? "C" : "A"));
        var result = Combat.SpellResolver.Resolve(boosted.Board, 0, healing ? 200 : 201, attack, (_, _) => null, 100,
            damageMultiplier: projected ? 1.5 : 1);

        Assert.Empty(boosted.Unmodeled);
        var observedBuff = Assert.Single(boosted.Board.StatBuffs!);
        Assert.Equal(3, observedBuff.Duration);
        Assert.Equal(healing ? 143 : Combat.CharIds.DealtDamageMultiplier, observedBuff.Characteristic);
        Assert.Equal(2, observedBuff.Delta);
        Assert.Equal(projected ? 153 : 102, healing ? result.Healing : result.EnemyDamage);
        Assert.Equal(100, board.Fighters[0].Life);
        Assert.Equal(1000, board.Fighters[1].Life);
    }

    [Theory]
    [InlineData(950, true)]
    [InlineData(951, false)]
    [InlineData(952, false)]
    public void Summon_accepts_state_changes_later_in_the_same_cast(int action, bool present)
    {
        var board = new Combat.SpellBoard(new[] { new Combat.SpellFighter(1, 200, false, 100, 100) });
        var spell = Spell(Effect(1008, 0, "a,A") with { DiceNum = 3112, DiceSide = 1 },
            Effect(action, 0, "a") with { Value = 42 });

        var result = Resolve(spell, board);

        var summon = Assert.Single(result.Summons);
        var fighter = Assert.Single(result.Board.Fighters, f => f.Id == summon.TargetId);
        Assert.Equal(present, fighter.States!.Contains(42));
        Assert.Equal(!present, fighter.AbsentStates!.Contains(42));
        Assert.Empty(result.Unmodeled);
        Assert.Single(board.Fighters);
    }

    [Theory]
    [InlineData(1008, 3112, 1, 1)]
    [InlineData(1008, 3112, 2, 2)]
    [InlineData(1008, 3112, 3, 3)]
    [InlineData(1008, 3112, 0, 0)]
    [InlineData(181, 3958, 1, 1)]
    [InlineData(181, 3958, 2, 2)]
    [InlineData(181, 3960, 3, 3)]
    [InlineData(181, 3960, 0, 0)]
    [InlineData(1011, 3112, 3, 0)]
    public void Summon_retains_effect_monster_grade_in_event_and_subsequent_board(
        int action, int template, int diceSide, int expectedGrade)
    {
        var board = new Combat.SpellBoard(new[] { new Combat.SpellFighter(1, 200, false, 100, 100) });
        var spell = Spell(Effect(action, 0, "a,A") with { DiceNum = template, DiceSide = diceSide });

        var result = Resolve(spell, board);
        var next = Resolve(Spell(Effect(950, 0, "C") with { Value = 42 }), result.Board);

        var summon = Assert.Single(result.Summons);
        Assert.Equal((template, expectedGrade, 230, 1L), (summon.Template, summon.Grade, summon.Cell, summon.CasterId));
        var fighter = Assert.Single(next.Board.Fighters, f => f.Id == summon.TargetId);
        Assert.Equal((template, expectedGrade, 230, (long?)1),
            (fighter.MonsterTemplate, fighter.MonsterGrade, fighter.Cell, fighter.SummonerId));
        Assert.Single(board.Fighters);
        Assert.Equal(0, board.Fighters[0].MonsterGrade);
    }

    [Theory]
    [InlineData(true, false, 3)]
    [InlineData(true, true, 4)]
    [InlineData(false, false, 3)]
    public void Learned_superpowerful_replaces_tree_with_new_identity_and_observed_scaled_stats(
        bool hasCatalogue, bool leafy, int expectedRank)
    {
        var names = new GameNameResolver();
        names.EnsureLoaded(); names.EnsureCombatLoaded();
        using var db = F06App::GivreClone.App.Services.LotM.GameDb.TryOpen(RealGameData.Path!)!;
        var board = new Combat.SpellBoard(new[] {
            new Combat.SpellFighter(1, 229, false, 2149, 2149),
            new Combat.SpellFighter(-2, 130, false, 513, 513, MonsterTemplate: 5894,
                SummonerId: 1, MonsterGrade: 1, States: leafy ? ImmutableHashSet.Create(256) : ImmutableHashSet<int>.Empty)
        }, SummonLookup: hasCatalogue ? db.SummonGradeStats : null);

        var result = Combat.SpellResolver.Resolve(board, 0, 130, names.GetSpellDescriptor(13578, 2)!,
            names.GetSpellDescriptor, 161);

        if (!hasCatalogue)
        {
            Assert.Contains(result.Unmodeled, e => e.ActionId == 405);
            Assert.True(result.Board.Fighters[1].Alive);
            Assert.Empty(result.Summons);
            return;
        }
        Assert.Empty(result.Unmodeled);
        Assert.False(result.Board.Fighters[1].Alive);
        var doll = Assert.Single(result.Board.Fighters, f => f.Alive && f.MonsterTemplate == 5899);
        Assert.NotEqual(-2, doll.Id);
        Assert.Equal((130, 769d, 769d, 6d, 5d, expectedRank, (long?)1),
            (doll.Cell, doll.Life, doll.MaxLife, doll.Ap, doll.Mp, doll.MonsterGrade, doll.SummonerId));
        Assert.Equal(652, doll.Characteristics![10]);
        Assert.True(board.Fighters[1].Alive);
    }

    [Fact]
    public void Learned_tree_uses_catalogue_life_before_superpowerful_replaces_it()
    {
        var names = new GameNameResolver();
        names.EnsureLoaded(); names.EnsureCombatLoaded();
        using var db = F06App::GivreClone.App.Services.LotM.GameDb.TryOpen(RealGameData.Path!)!;
        var board = new Combat.SpellBoard(new[] {
            new Combat.SpellFighter(1, 229, false, 2149, 2149)
        }, SummonLookup: db.SummonGradeStats);

        var treeCast = Combat.SpellResolver.Resolve(board, 0, 130, names.GetSpellDescriptor(13519, 3)!,
            names.GetSpellDescriptor, 161);

        var tree = Assert.Single(treeCast.Board.Fighters, f => f.MonsterTemplate == 5894);
        Assert.Equal((513d, 513d, 0d, 0d), (tree.Life, tree.MaxLife, tree.Ap, tree.Mp));
        var transformed = Combat.SpellResolver.Resolve(treeCast.Board, 0, 130, names.GetSpellDescriptor(13578, 2)!,
            names.GetSpellDescriptor, 161);
        Assert.Empty(transformed.Unmodeled);
        Assert.Single(transformed.Board.Fighters, f => f.Alive && f.MonsterTemplate == 5899);
        Assert.Single(board.Fighters);
    }

    [Theory]
    [InlineData(true, true, 100, true)]
    [InlineData(false, true, 100, true)]
    [InlineData(false, false, 100, false)]
    [InlineData(false, true, 90, false)]
    [InlineData(true, true, 100, true, true, 1000)]
    [InlineData(true, true, 100, false, true, 0)]
    [InlineData(false, true, 100, true, true, 1000)]
    public void Learned_doll_is_selected_for_its_actual_legal_action_from_an_existing_or_new_tree(
        bool existingTree, bool allowNewAllies, int targetHpMax, bool useful, bool healer = false, int missingLife = 0)
    {
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        using var db = F06App::GivreClone.App.Services.LotM.GameDb.TryOpen(RealGameData.Path!)!;
        int Cell(int x) => MapPathfinder.CellFromMapPoint(8 + x, 8);
        int dollSpell = healer ? 13573 : 13578;
        var spell = names.GetSpellDescriptor(dollSpell, 2)!;
        var tree = names.GetSpellDescriptor(13519, 3)!;
        var fighters = ImmutableArray.Create(
            new Combat.PlannerFighter(1, Cell(0), false, 2149 - missingLife, 2149, States: ImmutableHashSet<int>.Empty),
            new Combat.PlannerFighter(2, Cell(5), true, 1000, 1000, MovePoints: 0, TurnAp: 0,
                ExpectedDamage: 0, ThreatKnown: true, States: ImmutableHashSet<int>.Empty));
        if (existingTree) fighters = fighters.Add(new Combat.PlannerFighter(3, Cell(2), false, 513, 513,
            MonsterTemplate: 5894, MonsterGrade: 1, SummonerId: 1, States: ImmutableHashSet<int>.Empty));
        var spells = ImmutableArray.Create(new Combat.PlannerSpell(spell, 1, 1,
            ImmutableDictionary<long, int>.Empty, existingTree ? ImmutableHashSet.Create(3L) : ImmutableHashSet<long>.Empty,
            AllowNewAlliedSummons: allowNewAllies, TargetHpMax: targetHpMax));
        if (!existingTree) spells = spells.Add(new Combat.PlannerSpell(tree, 1, 1,
            ImmutableDictionary<long, int>.Empty, ImmutableHashSet<long>.Empty,
            AllowedCells: ImmutableHashSet.Create(Cell(2))));
        var snapshot = new Combat.CombatTurnSnapshot(
            Enumerable.Range(0, 7).Select(x => new Combat.PlannerCell(Cell(x), true, false,
                ImmutableArray<int>.Empty)).ToImmutableArray(),
            fighters, 1, spell.Ap + (existingTree ? 0 : tree.Ap), 0, spells,
            ImmutableDictionary<int, int>.Empty.Add(26, 1), MaxCasts: existingTree ? 1 : 2, SpellLookup: names.GetSpellDescriptor,
            CasterLevel: 161, SummonLookup: db.SummonGradeStats);

        var plan = Combat.CombatTurnPlanner.Choose(snapshot);

        if (!useful)
        {
            Assert.Empty(plan.Actions);
            return;
        }
        Assert.Equal(existingTree ? new[] { dollSpell } : new[] { 13519, dollSpell }, plan.Actions.Select(a => a.SpellId));
        Assert.All(plan.Actions, action => Assert.Equal(Cell(2), action.Cell));
        Assert.Equal(0, plan.Score.Damage);
        Assert.True(plan.Score.Position > 0);
        Assert.Equal(0, plan.Unmodeled);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 40)]
    public void Catalog_invulnerability_changes_apply_in_effect_order(bool apply, int expectedDamage)
    {
        var board = Board();
        board = board with
        {
            StateDamageRules = ImmutableDictionary<int, StateRule>.Empty.Add(9001, new(true, false, false, false)),
            Fighters = board.Fighters.Select((f, i) => i == 1 ? f with
            {
                States = apply ? ImmutableHashSet<int>.Empty : ImmutableHashSet.Create(9001)
            } : f).ToArray()
        };

        var result = Resolve(Spell(Effect(apply ? 950 : 951) with { Value = 9001 }, Effect(100, 40)), board);

        Assert.Equal(expectedDamage, result.EnemyDamage);
        Assert.Equal(1000 - expectedDamage, result.Board.Fighters[1].Life);
        Assert.Equal(apply, result.Board.Fighters[1].States!.Contains(9001));
        Assert.Equal(!apply, board.Fighters[1].States!.Contains(9001));
    }

    [Theory]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 40)]
    [InlineData(false, false, 40)]
    public void Confirmed_catalog_state_blocks_damage_without_consuming_shield(bool targetState, bool present, int expected)
    {
        var board = Board();
        board = board with
        {
            StateDamageRules = ImmutableDictionary<int, StateRule>.Empty.Add(9001,
                new StateRule(targetState, false, false, !targetState)),
            Fighters = board.Fighters.Select((f, i) => f with
            {
                Shield = i == 1 ? 10 : f.Shield,
                States = present && i == (targetState ? 1 : 0) ? ImmutableHashSet.Create(9001) : ImmutableHashSet<int>.Empty
            }).ToArray()
        };
        var result = Resolve(Spell(Effect(91, 40)), board);
        Assert.Equal(expected, result.EnemyDamage);
        Assert.Equal(expected == 0 ? 10 : 0, result.Board.Fighters[1].Shield);
        Assert.Equal(expected == 0 ? 0 : 15, result.Healing);
        Assert.Same(board.StateDamageRules, result.Board.StateDamageRules);
        Assert.Equal(1000, board.Fighters[1].Life);
    }

    internal static Combat.SpellEffectDescriptor Effect(int action, int amount = 20, string mask = "A", char shape = 'P', int radius = 0) =>
        new(action, Combat.SpellEffectDescriptor.Classify(action), amount, amount, 3, 0, mask, shape, radius, 0);
    internal static Combat.SpellCastDescriptor Spell(params Combat.SpellEffectDescriptor[] effects) =>
        new(1, 1, 3, 1, 4, 0, false, 0, 0, 0, 0, "", effects.ToImmutableArray());
    internal static Combat.SpellBoard Board() => new(new Combat.SpellFighter[]
    {
        new(1, 200, false, 50, 100), new(2, 230, true, 1000, 1000),
        new(3, Neighbor(230, 1, 0), true, 1000, 1000),
        new(4, Neighbor(230, 0, 1), false, 1000, 1000)
    });
    private static int Neighbor(int cell, int dx, int dy)
    {
        var (x, y) = MapPathfinder.MapPointOf(cell);
        return MapPathfinder.CellFromMapPoint(x + dx, y + dy);
    }
    internal static Combat.ResolvedCast Resolve(Combat.SpellCastDescriptor spell, Combat.SpellBoard? board = null,
        Func<int, int, Combat.SpellCastDescriptor?>? lookup = null, Func<int, bool>? passable = null) =>
        Combat.SpellResolver.Resolve(board ?? Board(), 0, 230, spell, lookup ?? ((_, _) => null), 200, passable);

    [Fact]
    public void Sram_invisibility_immediate_row_is_modeled_separately_from_ally_mp()
    {
        var spell = Spell(Effect(150, 0, "a,A") with { Duration = 1, Triggers = "I" },
            Effect(128, 1, "a") with { Duration = 2, Triggers = "I" }) with { SpellId = 12913 };

        var result = Resolve(spell);

        Assert.Empty(result.Unmodeled);
        Assert.Equal(FighterVisibility.Invisible, result.Board.Fighters[1].Visibility);
        Assert.All(result.Board.Fighters.Where(f => f.Id != 2),
            f => Assert.Equal(FighterVisibility.Visible, f.Visibility));
        var hidden = Assert.Single(result.Events.OfType<Combat.VisibilityResolved>());
        Assert.Equal(2, hidden.TargetId);
        Assert.Equal(6, result.Board.Fighters[1].Mp);
        Assert.Equal(6, result.Board.Fighters[0].Mp);
    }

    [Fact]
    public void Circle_damage_counts_each_enemy_and_friendly_fire_without_mutating_input()
    {
        var board = Board();
        var result = Resolve(Spell(Effect(96, mask: "a,A", shape: 'C', radius: 2)), board);
        Assert.Equal(40, result.EnemyDamage);
        Assert.Equal(20, result.FriendlyFire);
        Assert.Equal(1000, board.Fighters[1].Life);
        Assert.Equal(980, result.Board.Fighters[1].Life);
        Assert.Empty(result.Unmodeled);
    }

    [Theory]
    [InlineData("A,V60", false)]
    [InlineData("A,v60", false)]
    [InlineData("A,*V60", true)]
    [InlineData("A,*v60", true)]
    public void Life_percentage_masks_report_unknown_maximum_instead_of_accepting_sentinel_life(string mask, bool caster)
    {
        var board = Board();
        board = board with
        {
            Fighters = board.Fighters.Select((f, i) =>
                i == (caster ? 0 : 1) ? f with { Life = 1_000_000, MaxLife = 0 } : f).ToArray()
        };

        var result = Resolve(Spell(Effect(96, mask: mask)), board);

        Assert.Equal(0, result.EnemyDamage);
        Assert.NotEmpty(result.Unmodeled);
    }

    [Theory]
    [InlineData(279, true)]
    [InlineData(1109, false)]
    [InlineData(1039, false)]
    public void Maximum_life_dependent_effects_report_missing_observation(int action, bool caster)
    {
        var board = Board();
        board = board with
        {
            Fighters = board.Fighters.Select((f, i) =>
                i == (caster ? 0 : 1) ? f with { MaxLife = 0 } : f).ToArray()
        };

        var result = Resolve(Spell(Effect(action)), board);

        Assert.Empty(result.Events);
        Assert.NotEmpty(result.Unmodeled);
    }

    [Theory]
    [InlineData(279, 490, 0)]
    [InlineData(1109, 700, 0)]
    [InlineData(1039, 500, 200)]
    public void Maximum_life_dependent_effects_still_resolve_with_observed_values(int action, int life, int shield)
    {
        var board = Board();
        board = board with
        {
            Fighters = board.Fighters.Select((f, i) => i == 1 ? f with { Life = 500 } : f).ToArray()
        };

        var result = Resolve(Spell(Effect(action)), board);

        Assert.Equal(life, result.Board.Fighters[1].Life);
        Assert.Equal(shield, result.Board.Fighters[1].Shield);
        Assert.Empty(result.Unmodeled);
    }

    [Fact]
    public void Life_steal_heals_half_actual_life_damage_capped_at_maximum()
    {
        var result = Resolve(Spell(Effect(91, 40)));
        Assert.Equal(40, result.EnemyDamage);
        Assert.Equal(20, result.Healing);
        Assert.Equal(70, result.Board.Fighters[0].Life);
        var capped = Resolve(Spell(Effect(91, 400)));
        Assert.Equal(50, capped.Healing);
        var shielded = Board();
        shielded = shielded with { Fighters = shielded.Fighters.Select((f, i) => i == 1 ? f with { Shield = 40 } : f).ToArray() };
        Assert.Equal(0, Resolve(Spell(Effect(91, 40)), shielded).Healing);
    }

    [Fact]
    public void Subcast_uses_raw_spell_id_grade_and_parent_caster()
    {
        var child = Spell(Effect(91)) with { SpellId = 42, Grade = 2 };
        var result = Resolve(Spell(Effect(1160) with { DiceNum = 42, DiceSide = 2 }),
            lookup: (id, grade) => id == 42 && grade == 2 ? child : null);
        Assert.Equal(20, result.EnemyDamage);
        Assert.Equal(10, result.Healing);
        Assert.Single(result.Events.OfType<Combat.SubCastResolved>());
        Assert.Empty(result.Unmodeled);
    }

    [Theory]
    [InlineData(76, 0)]
    [InlineData(5920, 20)]
    public void Healing_uses_the_incurable_state_not_its_shared_icon(int state, int healing)
    {
        var board = Board();
        board = board with { Fighters = board.Fighters.Select((f, i) =>
            i == 0 ? f with { States = ImmutableHashSet.Create(state) } : f).ToArray() };

        var result = Resolve(Spell(Effect(108, 20, "C")), board);

        Assert.Equal(healing, result.Healing);
        Assert.Equal(50 + healing, result.Board.Fighters[0].Life);
        Assert.Empty(result.Unmodeled);
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 20)]
    public void Incurable_blocks_life_steal_on_the_recipient_without_cancelling_damage(
        bool casterIncurable, bool targetIncurable, int healing)
    {
        var board = Board();
        board = board with { Fighters = board.Fighters.Select((f, i) =>
            f with { States = (i == 0 && casterIncurable || i == 1 && targetIncurable)
                ? ImmutableHashSet.Create(76) : ImmutableHashSet<int>.Empty }).ToArray() };

        var result = Resolve(Spell(Effect(91, 40)), board);

        Assert.Equal(40, result.EnemyDamage);
        Assert.Equal(healing, result.Healing);
        Assert.Equal(50 + healing, result.Board.Fighters[0].Life);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 20)]
    public void Incurable_changes_take_effect_in_spell_order_without_blocking_shield(bool apply, int healing)
    {
        var board = Board();
        board = board with { Fighters = board.Fighters.Select((f, i) =>
            i == 0 ? f with { States = apply ? ImmutableHashSet<int>.Empty : ImmutableHashSet.Create(76) } : f).ToArray() };

        var result = Resolve(Spell(Effect(apply ? 950 : 951, mask: "C") with { Value = 76 },
            Effect(108, 20, "C"), Effect(1040, 30, "C")), board);

        Assert.Equal(healing, result.Healing);
        Assert.Equal(30, result.Board.Fighters[0].Shield);
        Assert.Equal(50 + healing, result.Board.Fighters[0].Life);
    }

    [Theory]
    [InlineData(false, false, 0, true)]
    [InlineData(true, false, 20, false)]
    [InlineData(false, true, 20, false)]
    public void Healing_requires_observed_absence_of_incurable_when_states_are_incomplete(
        bool absent, bool removeFirst, int healing, bool unknown)
    {
        var board = Board();
        board = board with { Fighters = board.Fighters.Select((f, i) => i == 0 ? f with
        {
            StatesKnown = false,
            AbsentStates = absent ? ImmutableHashSet.Create(76) : ImmutableHashSet<int>.Empty
        } : f).ToArray() };
        var heal = Effect(108, 20, "C");
        var spell = removeFirst ? Spell(Effect(951, mask: "C") with { Value = 76 }, heal) : Spell(heal);

        var result = Resolve(spell, board);

        Assert.Equal(healing, result.Healing);
        Assert.Equal(50 + healing, result.Board.Fighters[0].Life);
        Assert.Equal(unknown ? 1 : 0, result.Unmodeled.Count);
        if (unknown) Assert.Equal(1, Assert.Single(result.Unmodeled).TargetId);
    }

    [Fact]
    public void Recursive_cycle_and_missing_subcast_are_reported()
    {
        var spell = Spell(Effect(1160) with { DiceNum = 1, DiceSide = 1 });
        Assert.Contains(Resolve(spell, lookup: (_, _) => spell).Unmodeled, e => e.Reason.Contains("cycle"));
        Assert.Contains(Resolve(spell).Unmodeled, e => e.ActionId == 1160 && e.Reason.Contains("Missing"));
    }

    [Fact]
    public void Triggered_effect_remains_on_the_branch_after_another_cast_without_granting_its_reward()
    {
        var setup = Resolve(Spell(Effect(1160, mask: "C") with
        {
            DiceNum = 14598, DiceSide = 1, Triggers = "CPT", EffectTriggerDuration = 2
        }));
        var next = Resolve(Spell(Effect(96)), setup.Board);

        var pending = Assert.Single(next.Board.DeferredEffects!);
        Assert.Equal("CPT", pending.Triggers);
        Assert.Equal(2, pending.Effect.EffectTriggerDuration);
        Assert.Empty(next.Deferred);
        Assert.Equal(setup.Board.Fighters[0].Ap, next.Board.Fighters[0].Ap);
        Assert.Equal(0, next.Healing);
    }

    [Fact]
    public void Trigger_and_delay_are_deferred_and_unknown_rows_do_not_block_damage()
    {
        var result = Resolve(Spell(Effect(96) with { Triggers = "D", Conditional = true },
            Effect(96) with { Delay = 1 }, Effect(987654), Effect(96)));
        Assert.Equal(20, result.EnemyDamage);
        Assert.Equal(2, result.Deferred.Count);
        Assert.Equal("D", result.Deferred[0].Triggers);
        Assert.Equal(987654, Assert.Single(result.Unmodeled).ActionId);
    }

    [Fact]
    public void State_requirements_use_effect_order_and_caster_conditions()
    {
        var result = Resolve(Spell(Effect(950) with { Value = 42 }, Effect(96, mask: "A,E42"),
            Effect(951) with { Value = 42 }, Effect(96, mask: "A,E42"), Effect(96, mask: "A,*E42")));
        Assert.Equal(20, result.EnemyDamage);
        Assert.Equal(1, result.StatesApplied);
        Assert.Equal(1, result.StatesRemoved);
        Assert.Empty(result.Board.Fighters[1].States!);
    }

    [Fact]
    public void Incomplete_states_preserve_unknown_absence_except_for_explicitly_changed_states()
    {
        var board = Board();
        board = board with
        {
            Fighters = board.Fighters.Select((f, i) => i == 1 ? f with { StatesKnown = false } : f).ToArray()
        };
        var spell = Spell(
            Effect(96, mask: "A,e42"),
            Effect(950) with { Value = 42 },
            Effect(96, mask: "A,E42"),
            Effect(951) with { Value = 42 },
            Effect(96, mask: "A,e42"));

        var result = Resolve(spell, board);

        Assert.Equal(40, result.EnemyDamage);
        Assert.Equal(2, result.Events.OfType<Combat.DamageResolved>().Count());
        Assert.Single(result.Unmodeled.Where(e => e.TargetId == 2));
        Assert.False(result.Board.Fighters[1].StatesKnown);
    }

    [Fact]
    public void Removed_state_absence_survives_between_casts_without_revealing_other_states()
    {
        var board = Board();
        board = board with { Fighters = board.Fighters.Select((f, i) => i == 1
            ? f with { StatesKnown = false } : f).ToArray() };
        var removed = Resolve(Spell(Effect(951) with { Value = 42 }), board);

        var result = Resolve(Spell(Effect(96, mask: "A,e42"), Effect(96, mask: "A,e43")), removed.Board);

        Assert.Equal(20, result.EnemyDamage);
        Assert.Single(result.Unmodeled.Where(e => e.TargetId == 2));
        Assert.False(result.Board.Fighters[1].StatesKnown);
        Assert.Null(board.Fighters[1].States);
    }

    [Fact]
    public void Reapplying_a_removed_state_invalidates_its_absence_for_later_casts()
    {
        var board = Board();
        board = board with { Fighters = board.Fighters.Select((f, i) => i == 1
            ? f with { StatesKnown = false } : f).ToArray() };
        var removed = Resolve(Spell(Effect(951) with { Value = 42 }), board);
        var reapplied = Resolve(Spell(Effect(950) with { Value = 42 }), removed.Board);

        var result = Resolve(Spell(Effect(96, mask: "A,e42"), Effect(96, mask: "A,E42")), reapplied.Board);

        Assert.Equal(20, result.EnemyDamage);
        Assert.Empty(result.Unmodeled);
        Assert.Contains(42, result.Board.Fighters[1].States!);
        Assert.DoesNotContain(42, result.Board.Fighters[1].AbsentStates!);
        Assert.Contains(42, removed.Board.Fighters[1].AbsentStates!);
    }

    [Fact]
    public void Caster_mask_is_global_lowercase_c_is_zone_local_and_templates_are_alternatives()
    {
        var result = Resolve(Spell(Effect(108, 10, "C"), Effect(108, 10, "c")));
        Assert.Equal(10, result.Healing);
        var board = Board();
        board = board with { Fighters = board.Fighters.Select((f, i) => i == 1 ? f with { MonsterTemplate = 123 } : f).ToArray() };
        Assert.Equal(20, Resolve(Spell(Effect(96, mask: "A,F999,F123")), board).EnemyDamage);
        Assert.Equal(0, Resolve(Spell(Effect(96, mask: "A,f123")), board).EnemyDamage);
    }

    [Fact]
    public void Stats_shields_and_points_are_applied_in_sequence()
    {
        var result = Resolve(Spell(Effect(138, 100, "C"), Effect(96), Effect(1020, 50, "C"),
            Effect(1079, 2), Effect(1080, 3), Effect(111, 2, "C"), Effect(128, 1, "C")));
        Assert.Equal(40, result.EnemyDamage);
        Assert.Equal(100, result.ShieldGained);
        Assert.Equal(2, result.EnemyApRemoved);
        Assert.Equal(3, result.EnemyMpRemoved);
        Assert.Equal(2, result.AllyApGiven);
        Assert.Equal(1, result.AllyMpGiven);
    }

    [Theory]
    [InlineData(1160, 100)]
    [InlineData(792, 0)]
    public void Recursive_level_shield_does_not_borrow_the_original_caster_level(int action, int expectedShield)
    {
        var shield = Spell(Effect(1020, 50, "C")) with { SpellId = 2 };
        var parent = Spell(Effect(action, 2) with { DiceNum = 2, DiceSide = 1 });

        var result = Resolve(parent, lookup: (id, grade) => id == 2 && grade == 1 ? shield : null);

        Assert.Equal(expectedShield, result.Board.Fighters.Sum(f => f.Shield));
        if (action == 792)
            Assert.Contains(result.Unmodeled, e => e.SpellId == 2 && e.ActionId == 1020);
        else
            Assert.Empty(result.Unmodeled);
    }

    [Fact]
    public void Unknown_mask_and_random_choice_are_explicit_uncertainty()
    {
        var spell = Spell(Effect(96, mask: "A,unknown"), Effect(96) with { Random = 50 }, Effect(96));
        var result = Resolve(spell);
        Assert.Equal(20, result.EnemyDamage);
        Assert.Equal(2, result.Unmodeled.Count);
        Assert.Equal(2, Combat.SpellResolver.ResolverCoverage(spell).Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Summons_occupy_ground_and_marks_record_their_cells(int payloadGrade)
    {
        var spell = Spell(Effect(181, 123, "x"), Effect(181, 124, "x"),
            Effect(400, shape: 'C', radius: 2) with { DiceNum = 456, DiceSide = payloadGrade, Duration = 3 })
            with { SpellId = 1234, Grade = 2 };
        var result = Combat.SpellResolver.Resolve(Board(), 0, 300, spell, (_, _) => null, 200);
        Assert.Single(result.Summons);
        Assert.Equal(5, result.Board.Fighters.Count);
        Assert.Equal(300, result.Board.Fighters[^1].Cell);
        Assert.Contains(300, Assert.Single(result.Marks).Mark.Cells);
        var mark = Assert.Single(result.Board.Marks!);
        Assert.Equal((400, 456, 1L, 1234, 2, 3),
            (mark.ActionId, mark.SpellId, mark.OwnerId, mark.SourceSpellId, mark.SourceGrade, mark.Duration));
        Assert.Equal(payloadGrade, mark.SpellGrade);
    }

    [Fact]
    public void Later_cast_preserves_existing_mark_provenance_without_expiring_it()
    {
        var mark = new Combat.SpellMark(400, 456, 1, new[] { 300 }, 1234, 2, 3, SpellGrade: 1);
        var board = Board() with { Marks = new[] { mark } };

        var result = Resolve(Spell(Effect(96)), board);

        var retained = Assert.Single(result.Board.Marks!);
        Assert.Equal((400, 456, 1L, 1234, 2, 3),
            (retained.ActionId, retained.SpellId, retained.OwnerId,
                retained.SourceSpellId, retained.SourceGrade, retained.Duration));
        Assert.Equal(new[] { 300 }, retained.Cells);
        Assert.Equal(1, retained.SpellGrade);
        Assert.Empty(result.Marks);
    }

    [Fact]
    public void Teleport_wall_and_occupancy_checks_and_swap_are_observable()
    {
        var teleport = Resolve(Spell(Effect(4, 0, "C")));
        Assert.True(Assert.Single(teleport.Movements).Blocked);
        var swap = Resolve(Spell(Effect(8, 0)));
        Assert.Equal(230, swap.Board.Fighters[0].Cell);
        Assert.Equal(200, swap.Board.Fighters[1].Cell);
        var wall = Combat.SpellResolver.Resolve(Board(), 0, 300, Spell(Effect(4, 0, "C")), (_, _) => null, 200, c => c != 300);
        Assert.Equal(200, wall.Board.Fighters[0].Cell);
    }

    [Fact]
    public void Telefrag_mask_is_local_to_each_cast_and_restored_after_a_child()
    {
        var child = Spell(Effect(950, 0, "a,A,T", 'A') with { Value = 701 }) with { SpellId = 2 };
        var parent = Spell(Effect(8, 0),
            Effect(1160, 0, "C") with { DiceNum = 2, DiceSide = 1 },
            Effect(950, 0, "a,A,T", 'A') with { Value = 702 });

        var result = Resolve(parent, lookup: (id, _) => id == 2 ? child : null);

        Assert.Empty(result.Unmodeled);
        foreach (int index in new[] { 0, 1 })
        {
            Assert.Contains(702, result.Board.Fighters[index].States!);
            Assert.DoesNotContain(701, result.Board.Fighters[index].States!);
        }
        Assert.DoesNotContain(702, result.Board.Fighters[2].States!);
        var later = Resolve(child, result.Board);
        Assert.Empty(later.Unmodeled);
        Assert.All(later.Board.Fighters, fighter => Assert.DoesNotContain(701, fighter.States!));
    }
}

public class SpellResolverRealDataTests(ITestOutputHelper output)
{
    [Fact]
    public void Learned_cra_power_boost_keeps_its_gameplay_effects_without_simulating_its_graphic_script()
    {
        Assert.True(ClientInstalled(), "This integration scenario requires the installed official client catalog.");
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var initial = new Combat.SpellBoard(new[] {
            new Combat.SpellFighter(1, 200, false, 1000, 1000, States: ImmutableHashSet<int>.Empty)
        });

        var result = Combat.SpellResolver.Resolve(initial, 0, 200,
            names.GetSpellDescriptor(32466, 1)!, names.GetSpellDescriptor, 83);

        var boosted = result.Board.Fighters[0].Characteristics!;
        Assert.Equal(150, boosted.GetValueOrDefault(Combat.CharIds.DamagePercent));
        Assert.Equal(50, boosted.GetValueOrDefault(Combat.CharIds.PushDamageBonus));
        Assert.Equal(10, boosted.GetValueOrDefault(Combat.CharIds.CriticalHit));
        // The catalogue's range penalty carries delay 1: it is scheduled, not applied by this cast.
        Assert.Equal(0, boosted.GetValueOrDefault(Combat.CharIds.Range));
        Assert.DoesNotContain(result.Events, e => e is Combat.SummonResolved);
        // Every delayed row of this boost waits, including its graphic script: none is applied now,
        // and the script is never turned into damage, a characteristic or a summon.
        Assert.Equal(new[] { 3793, 116, 295, 280, 280, 280, 280 },
            result.Deferred.Select(p => p.Effect.ActionId));
        Assert.All(result.Deferred, p => Assert.Equal(1, p.Effect.Delay));
        Assert.Empty(result.Unmodeled);
        var next = Combat.SpellResolver.AdvanceStatBuffs(result.Board);
        Assert.Equal(0, next.Fighters[0].Characteristics!.GetValueOrDefault(Combat.CharIds.DamagePercent));
        Assert.Empty(next.SpellModifiers!);
    }

    [Theory]
    [InlineData(240, 298, true, 226, 284)]
    [InlineData(397, 368, false, 382, 353)]
    public void Learned_odyssey_reproduces_observed_pull_after_caster_retreat(
        int origin, int target, bool enemy, int casterAfter, int targetAfter)
    {
        Assert.True(ClientInstalled(), "This integration scenario requires the installed official client catalog.");
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(14589, 3)!;
        var initial = new Combat.SpellBoard(new[] {
            new Combat.SpellFighter(1, origin, false, 1000, 1000, States: ImmutableHashSet<int>.Empty),
            new Combat.SpellFighter(2, target, enemy, 1000, 1000, States: ImmutableHashSet<int>.Empty)
        });

        var result = Combat.SpellResolver.Resolve(initial, 0, target, spell, names.GetSpellDescriptor, 187);

        Assert.Equal(casterAfter, result.Board.Fighters[0].Cell);
        Assert.Equal(targetAfter, result.Board.Fighters[1].Cell);
        Assert.Empty(result.Unmodeled);
        Assert.Equal(origin, initial.Fighters[0].Cell);
        Assert.Equal(target, initial.Fighters[1].Cell);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Learned_therapy_heals_an_ally_without_stealing_its_life(bool projected)
    {
        Assert.True(ClientInstalled(), "This integration scenario requires the installed official client catalog.");
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(14581, 2)!;
        var initial = new Combat.SpellBoard(new[] {
            new Combat.SpellFighter(1, 200, false, 500, 1000, States: ImmutableHashSet<int>.Empty),
            new Combat.SpellFighter(2, 230, false, 500, 1000, States: ImmutableHashSet<int>.Empty)
        }, ThroughPortal: projected);

        var result = Combat.SpellResolver.Resolve(initial, 0, 230, spell, names.GetSpellDescriptor, 187);

        Assert.Equal(521, result.Board.Fighters[1].Life);
        Assert.Equal(500, result.Board.Fighters[0].Life);
        Assert.Empty(result.Events.OfType<Combat.DamageResolved>());
        Assert.Empty(result.Unmodeled);
    }

    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, 2)]
    public void Learned_raillerie_removes_movement_points_only_through_portals(bool projected, int expectedMp)
    {
        Assert.True(ClientInstalled(), "This integration scenario requires the installed official client catalog.");
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(14595, 2)!;
        var initial = new Combat.SpellBoard(new[] {
            new Combat.SpellFighter(1, 200, false, 1000, 1000, States: ImmutableHashSet<int>.Empty),
            new Combat.SpellFighter(2, 230, true, 1000, 1000, Mp: 5, States: ImmutableHashSet<int>.Empty)
        }, ThroughPortal: projected);

        var result = Combat.SpellResolver.Resolve(initial, 0, 230, spell, names.GetSpellDescriptor, 187);

        Assert.Equal(expectedMp, result.Board.Fighters[1].Mp);
        Assert.Equal(5, initial.Fighters[1].Mp);
        Assert.Empty(result.Unmodeled);
    }

    [Theory]
    [InlineData(false, 8)]
    [InlineData(true, 10)]
    public void Learned_insolence_retreats_only_on_a_direct_cast(bool projected, int expectedCasterX)
    {
        Assert.True(ClientInstalled(), "This integration scenario requires the installed official client catalog.");
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(14637, 3)!;
        int origin = MapPathfinder.CellFromMapPoint(10, 0);
        int target = MapPathfinder.CellFromMapPoint(14, 0);
        var initial = new Combat.SpellBoard(new[] {
            new Combat.SpellFighter(1, origin, false, 1000, 1000, States: ImmutableHashSet<int>.Empty),
            new Combat.SpellFighter(2, target, true, 1000, 1000, States: ImmutableHashSet<int>.Empty)
        }, ThroughPortal: projected);

        var result = Combat.SpellResolver.Resolve(initial, 0, target, spell, names.GetSpellDescriptor, 187);

        Assert.Equal(MapPathfinder.CellFromMapPoint(expectedCasterX, 0), result.Board.Fighters[0].Cell);
        Assert.Equal(target, result.Board.Fighters[1].Cell);
        Assert.True(result.Board.Fighters[1].Life < 1000);
        Assert.Empty(result.Unmodeled);
        Assert.Equal(origin, initial.Fighters[0].Cell);
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 1, 0)]
    [InlineData(2, 6, 80)]
    public void Learned_cra_retreat_arrow_then_walk_preserves_the_remaining_enemy_threat(
        int grade, int enemyRange, double incoming)
    {
        Assert.True(ClientInstalled(), "This integration scenario requires the installed official client catalog.");
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var push = names.GetSpellDescriptor(32426, grade)!;
        int Cell(int x) => MapPathfinder.CellFromMapPoint(8 + x, 8);
        var board = new Combat.CombatTurnSnapshot(
            Enumerable.Range(0, 9).Select(x => new Combat.PlannerCell(Cell(x), true, false,
                new[] { x - 1, x + 1 }.Where(n => n >= 0 && n < 9).Select(Cell).ToImmutableArray())).ToImmutableArray(),
            ImmutableArray.Create(
                new Combat.PlannerFighter(1, Cell(2), false, 500, 500, States: ImmutableHashSet<int>.Empty),
                new Combat.PlannerFighter(2, Cell(3), true, 500, 500, MovePoints: 0, AttackRange: enemyRange,
                    ExpectedDamage: 80, ThreatKnown: true, States: ImmutableHashSet<int>.Empty)),
            1, push.Ap, 2, ImmutableArray.Create(new Combat.PlannerSpell(push, 1, 1,
                ImmutableDictionary<long, int>.Empty, ImmutableHashSet.Create(1L, 2L))),
            ImmutableDictionary<int, int>.Empty, Style: F06App::GivreClone.App.Services.CombatStyle.Fleeing,
            SpellLookup: names.GetSpellDescriptor, UnsafeForcedMovementCells: ImmutableHashSet<int>.Empty);

        var plan = Combat.CombatTurnPlanner.Choose(board);

        Assert.Equal(Combat.PlannerActionKind.Cast, plan.Actions[0].Kind);
        Assert.Equal(32426, plan.Actions[0].SpellId);
        Assert.Contains(plan.Actions.Skip(1), a => a.Kind == Combat.PlannerActionKind.Move);
        Assert.True(MapPathfinder.CellDistance(plan.Intent!.EndCell, Cell(5)) > 1);
        Assert.Equal(incoming, plan.Score.Incoming);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Learned_cra_retreat_arrow_pushes_two_cells_with_its_actual_rank_damage(bool captured)
    {
        Assert.True(ClientInstalled(), "This integration scenario requires the installed official client catalog.");
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        int origin = captured ? 146 : MapPathfinder.CellFromMapPoint(8, 8);
        int target = captured ? 173 : MapPathfinder.CellFromMapPoint(9, 8);
        var board = new Combat.SpellBoard(new[] {
            new Combat.SpellFighter(1, origin, false, 1000, 1000),
            new Combat.SpellFighter(2, target, true, 1000, 1000)
        });

        var result = Combat.SpellResolver.Resolve(board, 0, target,
            names.GetSpellDescriptor(32426, 2)!, names.GetSpellDescriptor, 82);

        Assert.Equal(captured ? 200 : MapPathfinder.CellFromMapPoint(11, 8), result.Board.Fighters[1].Cell);
        Assert.Equal(979, result.Board.Fighters[1].Life);
        Assert.Empty(result.Unmodeled);
        Assert.Equal(target, board.Fighters[1].Cell);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Learned_xelor_symmetry_grants_telefrag_states_only_to_the_exchanged_pair(bool occupied)
    {
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        int Cell(int x) => MapPathfinder.CellFromMapPoint(x, 8);
        var fighters = new System.Collections.Generic.List<Combat.SpellFighter> {
            new(1, Cell(8), false, 1000, 1000, States: ImmutableHashSet<int>.Empty),
            new(2, Cell(9), true, 1000, 1000, States: ImmutableHashSet<int>.Empty)
        };
        if (occupied) fighters.Add(new(3, Cell(10), true, 1000, 1000, States: ImmutableHashSet<int>.Empty));

        var result = Combat.SpellResolver.Resolve(new Combat.SpellBoard(fighters), 0, Cell(9),
            names.GetSpellDescriptor(13249, 1)!, names.GetSpellDescriptor, 100);

        Assert.Equal(Cell(10), result.Board.Fighters[0].Cell);
        Assert.Equal(occupied, result.Board.Fighters[0].States!.Contains(244));
        Assert.DoesNotContain(251, result.Board.Fighters[1].States!);
        if (occupied)
        {
            Assert.Equal(Cell(8), result.Board.Fighters[2].Cell);
            Assert.Contains(251, result.Board.Fighters[2].States!);
            var unsupported = Assert.Single(result.Unmodeled);
            Assert.Equal(13272, unsupported.SpellId);
            Assert.Equal(2792, unsupported.ActionId);
            Assert.Equal("Unsupported action", unsupported.Reason);
        }
        else Assert.Empty(result.Unmodeled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Learned_xelor_attack_routes_only_targets_with_their_observed_telefrag_state(bool telefrag, bool enemy = true)
    {
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var initial = new Combat.SpellBoard(new[] {
            new Combat.SpellFighter(1, 200, false, 1000, 1000, States: ImmutableHashSet<int>.Empty),
            new Combat.SpellFighter(2, 203, enemy, 1000, 1000,
                States: telefrag ? ImmutableHashSet.Create(enemy ? 251 : 244) : ImmutableHashSet<int>.Empty),
            new Combat.SpellFighter(3, 204, true, 1000, 1000, States: ImmutableHashSet<int>.Empty)
        });

        var result = Combat.SpellResolver.Resolve(initial, 0, 203,
            names.GetSpellDescriptor(13286, 1)!, names.GetSpellDescriptor, 100);

        Assert.Equal(telefrag, result.Events.OfType<Combat.SubCastResolved>().Any(e => e.SpellId == 13303));
        Assert.DoesNotContain(251, result.Board.Fighters[1].States!);
        Assert.DoesNotContain(244, result.Board.Fighters[1].States!);
        Assert.Equal(telefrag, result.Board.Fighters[2].States!.Contains(251));
        Assert.DoesNotContain(3622, result.Board.Fighters[1].States!);
        Assert.Equal(enemy ? (telefrag ? 22 : 11) : (telefrag ? 11 : 0), result.EnemyDamage);
        Assert.Equal(0, result.FriendlyFire);
        Assert.Empty(result.Unmodeled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Learned_soothing_bramble_resolves_enemy_mp_loss_without_leaking_infection_markers(bool infected)
    {
        Assert.True(ClientInstalled(), "This integration scenario requires the installed official client catalog.");
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(13525, 3)!;
        var initial = new Combat.SpellBoard(new[] {
            new Combat.SpellFighter(1, 200, false, 1000, 1000, States: ImmutableHashSet<int>.Empty),
            new Combat.SpellFighter(2, 203, true, 1000, 1000, Mp: 6,
                States: infected ? ImmutableHashSet.Create(263) : ImmutableHashSet<int>.Empty),
            new Combat.SpellFighter(3, 206, true, 1000, 1000, Mp: 6, States: ImmutableHashSet.Create(263)),
            new Combat.SpellFighter(4, 209, true, 1000, 1000, Mp: 6, States: ImmutableHashSet<int>.Empty)
        });

        var result = Combat.SpellResolver.Resolve(initial, 0, 203, spell, names.GetSpellDescriptor, 161);

        // Parent preview row 1080 is client-only; only the server sub-cast
        // removes two MP. Counting both would double the loss.
        Assert.Equal(4, result.Board.Fighters[1].Mp);
        Assert.Single(result.Events.OfType<Combat.SubCastResolved>(), e => e.SpellId == 13546 && e.Grade == 10);
        Assert.DoesNotContain(6, result.Board.Fighters[1].States!);
        Assert.DoesNotContain(5409, result.Board.Fighters[1].States!);
        Assert.DoesNotContain(6244, result.Board.Fighters[1].States!);
        Assert.DoesNotContain(5409, result.Board.Fighters[0].States!);
        Assert.Equal(infected ? 4 : 6, result.Board.Fighters[2].Mp);
        Assert.Equal(6, result.Board.Fighters[3].Mp);
        Assert.Equal(infected, result.Board.Fighters[2].States!.Contains(713));
        Assert.DoesNotContain(5409, result.Board.Fighters[2].States!);
        Assert.DoesNotContain(6244, result.Board.Fighters[2].States!);
        Assert.Equal(1000, result.Board.Fighters[1].Life);
        Assert.Equal(6, initial.Fighters[1].Mp);
        Assert.Empty(result.Unmodeled);

        var nextTurn = Combat.SpellResolver.AdvanceStatBuffs(result.Board);
        Assert.DoesNotContain(713, nextTurn.Fighters[2].States!);
        Assert.Contains(263, nextTurn.Fighters[2].States!);
    }

    [Theory]
    [InlineData(3737, 290, 14574, 1)]
    [InlineData(3738, 281, 14604, 2)]
    public void Learned_transcendance_selects_only_its_observed_portal_variant(int state, int action, int targetSpell, int value)
    {
        Assert.True(ClientInstalled(), "This integration scenario requires the installed official client catalog.");
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(14580, 2)!;
        var caster = new Combat.SpellFighter(1, 200, false, 1000, 1000, States: ImmutableHashSet.Create(state));
        var initial = new Combat.SpellBoard(new[] { caster });

        var result = Combat.SpellResolver.Resolve(initial, 0, 200, spell, names.GetSpellDescriptor, 182);

        var modifier = Assert.Single(result.Board.SpellModifiers!);
        Assert.Equal(action, modifier.ActionId);
        Assert.Equal(targetSpell, modifier.SpellId);
        Assert.Equal(value, modifier.Value);
        Assert.Equal(1L, modifier.TargetId);
        Assert.Contains(96, result.Board.Fighters[0].States!);
        Assert.Empty(result.Unmodeled);
        Assert.Null(initial.SpellModifiers);
        Assert.Empty(Combat.SpellResolver.AdvanceStatBuffs(result.Board).SpellModifiers!);
    }

    [Theory]
    [InlineData(3737, 2)]
    [InlineData(3738, 2)]
    [InlineData(3737, 3)]
    [InlineData(3738, 3)]
    public void Eliotrope_learned_sillage_leaves_a_portal_at_origin_then_teleports(int portalState, int grade)
    {
        Assert.True(ClientInstalled(), "This integration scenario requires the installed official client catalog.");
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(14591, grade);
        Assert.NotNull(spell);
        output.WriteLine($"Sillage learned grade{grade} statesCriterion={spell.StatesCriterion}");
        int origin = MapPathfinder.CellFromMapPoint(10, 0);
        int destination = MapPathfinder.CellFromMapPoint(12, 0);
        var caster = new Combat.SpellFighter(1, origin, false, 1000, 1000,
            States: ImmutableHashSet.Create(portalState));
        var result = Combat.SpellResolver.Resolve(new Combat.SpellBoard(new[] { caster }), 0, destination,
            spell!, names.GetSpellDescriptor, 182);
        foreach (var row in result.Events) output.WriteLine(row.ToString());
        foreach (var row in result.Unmodeled) output.WriteLine(row.ToString());
        Assert.Equal(destination, result.Board.Fighters[0].Cell);
        Assert.Single(result.Marks);
        Assert.Contains(result.Marks, mark => mark.Mark.ActionId == 1181 && mark.Mark.Cells.SequenceEqual(new[] { origin }));
        Assert.DoesNotContain(result.Marks, mark => mark.Mark.Cells.Contains(destination));
        Assert.Empty(result.Unmodeled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sillage_does_not_invent_portals_when_its_caster_state_is_absent_or_unknown(bool statesKnown)
    {
        Assert.True(ClientInstalled(), "This integration scenario requires the installed official client catalog.");
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(14591, 2)!;
        int origin = MapPathfinder.CellFromMapPoint(10, 0), destination = MapPathfinder.CellFromMapPoint(12, 0);
        var caster = new Combat.SpellFighter(1, origin, false, 1000, 1000,
            States: ImmutableHashSet<int>.Empty, StatesKnown: statesKnown);

        var result = Combat.SpellResolver.Resolve(new Combat.SpellBoard(new[] { caster }), 0, destination,
            spell, names.GetSpellDescriptor, 182);

        Assert.Empty(result.Marks);
        if (statesKnown) Assert.Empty(result.Unmodeled);
        else Assert.Contains(result.Unmodeled, row => row.Reason.Contains("state presence or absence is unobserved"));
    }

    private static bool ClientInstalled() => Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ankama", "Dofus-dofus3", "Dofus_Data", "StreamingAssets", "Content", "Data"));

    [Theory]
    [InlineData(14574, 3)]
    [InlineData(14579, 3)]
    [InlineData(14608, 3)]
    [InlineData(14594, 2)]
    [InlineData(14619, 1)]
    public void Eliotrope_current_fire_and_portal_grades_are_admitted_with_real_effects(int id, int grade)
    {
        Assert.True(ClientInstalled(), "This integration scenario requires the installed official client catalog.");
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(id, grade);
        Assert.NotNull(spell);
        foreach (var effect in spell!.Effects)
            output.WriteLine($"{id} action={effect.ActionId} mask={effect.TargetMask} shape={effect.Shape} size={effect.Size} delay={effect.Delay} trigger={effect.Triggers} condition={effect.Condition} modeled={Combat.SpellResolver.IsImmediateModeled(effect)}");
        Assert.True(Combat.CombatTurnPlanner.IsAdmitted(spell), string.Join("; ",
            Combat.SpellResolver.ResolverCoverage(spell).Select(e => e.ToString())));
        if (id == 14574) Assert.True(spell.PlacesGroundMark);
        else Assert.True(Combat.CombatTurnPlanner.HasModeledEnemyDamage(spell));
    }

    [Fact]
    public void Eliotrope_current_cicatrisation_heals_actual_missing_life_without_overheal()
    {
        Assert.True(ClientInstalled(), "This integration scenario requires the installed official client catalog.");
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(14587, 3);
        Assert.NotNull(spell);
        Assert.True(Combat.CombatTurnPlanner.IsAdmitted(spell!));
        var caster = new Combat.SpellFighter(1, 200, false, 950, 1000);
        var result = Combat.SpellResolver.Resolve(new Combat.SpellBoard(new[] { caster }), 0, 200,
            spell!, names.GetSpellDescriptor, 168);
        Assert.Equal(1000, result.Board.Fighters[0].Life);
        Assert.Equal(50, result.Healing);
        Assert.Empty(result.Unmodeled);
        var full = Combat.SpellResolver.Resolve(new Combat.SpellBoard(new[] { caster with { Life = 1000 } }),
            0, 200, spell!, names.GetSpellDescriptor, 168);
        Assert.Equal(0, full.Healing);
    }

    [Theory]
    [InlineData(25870)]
    [InlineData(25871)]
    public void Eniripsa_real_spells_damage_and_steal_without_rejecting_side_effects(int id)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(id, 1);
        Assert.NotNull(spell);
        var result = SpellResolverTests.Resolve(spell!, lookup: names.GetSpellDescriptor);
        Assert.True(result.EnemyDamage > 0);
        Assert.True(result.Events.OfType<Combat.HealingResolved>().Any(e => e.ActionId is >= 91 and <= 95 && e.Amount > 0));
        Assert.True(result.Board.Fighters[0].Life > 50);
        // 25870's sub-casts (1160) target 'a,P,F7370,F7371' = only the Eniripsa words (summon templates 7370/7371):
        // with none on the board, no sub-cast is the correct outcome, not an omission. 25871 reaches its own sub-casts.
        if (id == 25871) Assert.True(result.Events.OfType<Combat.SubCastResolved>().Any() || result.Deferred.Count > 0);
        else Assert.DoesNotContain(result.Events.OfType<Combat.SubCastResolved>(), e => e.SpellId is 25797 or 25889);
    }

    [Theory]
    [InlineData(14574)]
    [InlineData(14604)]
    public void Eliotrope_portal_spells_are_admitted_by_the_planner(int id)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var spell = names.GetSpellDescriptor(id, 1);
        Assert.NotNull(spell);
        Assert.True(Combat.CombatTurnPlanner.IsAdmitted(spell!));
    }

    [Theory]
    [InlineData(32435)]
    [InlineData(32436)]
    public void Cra_arrows_with_a_caster_buff_are_not_cast_on_the_caster_cell(int id)
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var arrow = names.GetSpellDescriptor(id, 1);
        Assert.NotNull(arrow);
        var rotation = Combat.AutoSpellRotation.Build(System.Array.Empty<F06App::GivreClone.App.Services.SpellConfig>(),
            new[] { new Combat.LearnedSpellInfo(id, arrow!.Ap, 0, arrow) },
            new Combat.AutoSpellOptions(RequireDescriptors: true));
        Assert.False(Assert.Single(rotation).OnMe);
    }

    [Fact]
    public void Eliotrope_entraide_rotation_entry_respects_its_cast_interval()
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        var entraide = names.GetSpellDescriptor(14596, 2);
        Assert.NotNull(entraide);
        var rotation = Combat.AutoSpellRotation.Build(System.Array.Empty<F06App::GivreClone.App.Services.SpellConfig>(),
            new[] { new Combat.LearnedSpellInfo(14596, entraide!.Ap, 0, entraide) },
            new Combat.AutoSpellOptions(RequireDescriptors: true));
        var cfg = Assert.Single(rotation);
        Assert.Equal(1, cfg.PerTurn);
        Assert.Equal(entraide.MinCastInterval - 1, cfg.Cooldown);
    }

    [Fact]
    public void Installed_class_spell_coverage_never_throws_and_models_immediate_damage()
    {
        if (!ClientInstalled()) return;
        var names = new GameNameResolver(); names.EnsureLoaded(); names.EnsureCombatLoaded();
        int total = 0, modeled = 0;
        var unknown = new Dictionary<int, int>();
        for (int breed = 1; breed <= 20; breed++)
        {
            int classTotal = 0, classModeled = 0, damageSpells = 0;
            foreach (var (id, _) in names.SpellsForBreed(breed))
            {
                var spell = names.GetSpellDescriptor(id, 1);
                if (spell is null) continue;
                var result = SpellResolverTests.Resolve(spell, lookup: names.GetSpellDescriptor);
                classTotal++;
                // Coverage is immediate damage-row support, not a claim that every utility row is implemented.
                // Include damage in reached sub-casts; unknown sub-cast descriptors/cycles remain uncertain.
                bool complete = !result.Unmodeled.Any(e => Combat.SpellResolver.IsDamageAction(e.ActionId)
                    || e.ActionId is 1160 or 2960 or 2794 or 792 or 1017 or 1019 or 2160 or 0);
                if (complete) classModeled++;
                if (result.EnemyDamage > 0) damageSpells++;
                foreach (var e in result.Unmodeled) unknown[e.ActionId] = unknown.GetValueOrDefault(e.ActionId) + 1;
            }
            output.WriteLine($"Class {breed}: immediate damage modeled {classModeled}/{classTotal}; positive enemy damage {damageSpells}");
            total += classTotal; modeled += classModeled;
        }
        output.WriteLine($"TOTAL {modeled}/{total} ({100.0 * modeled / total:F2}%)");
        output.WriteLine("Unmodeled action occurrences: " + string.Join(", ", unknown.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}")));
        Assert.True(total > 0);
        Assert.True((double)modeled / total >= 0.60, $"Immediate damage coverage {modeled}/{total}");
    }
}
