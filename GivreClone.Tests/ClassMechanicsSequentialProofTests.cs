using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;
using C = GivreClone.App.Services.Combat.ClassMechanics;

namespace GivreClone.Tests;

/// <summary>
/// Offline packet-contract fixtures only. Spell/grade/effect/template IDs used in
/// the positive tests are present in the 3.7.13.17 class export or the pinned
/// bomb-data contract. No synthetic class passive, state or spell ID is treated
/// as live truth. ServerConfirmed is modeled by the fixture, not obtained live.
/// </summary>
public sealed class ClassMechanicsSequentialProofTests
{
    private const long Fight = 889L;
    private const long Actor = 77L;
    private const long Enemy = -8L;

    private static int Cell(int x, int y = 0)
    {
        int c = MapPathfinder.CellFromMapPoint(x, y);
        Assert.InRange(c, 0, 559);
        return c;
    }

    private static SpellEffectDescriptor Row(int action, SpellEffectKind kind,
        int dice = 0, int value = 0)
        => new(action, kind, 0, 0, 0, 0, "a,A", 'P', 1, 0,
            DiceNum: dice, Value: value, Triggers: "I");

    private static SpellCastDescriptor Spell(int id, params SpellEffectDescriptor[] effects)
        => new(id, 1, 3, 1, 6, 0, false, 1, 1, 0, 0, "",
            effects.ToImmutableArray());

    private static C.ComboFrame Frame(int breed, int firstTurn = 1, int lastTurn = 3)
        => new(Fight, 5, firstTurn, lastTurn, Actor, breed);

    private static C.ComboObservation E(long order, C.ComboEventKind kind, long source = Actor,
        long subject = Enemy, int spell = 0, int action = 0, int mark = 0, int template = 0,
        int state = 0, int from = -1, int to = -1, long corr = 0, int turn = 2)
        => new(order, Fight, 5, turn, source, subject, kind, SpellId: spell,
            Grade: spell != 0 ? 1 : 0, ActionId: action, StateId: state,
            TemplateId: template, MarkId: mark, FromCell: from, ToCell: to,
            Correlation: corr, ServerConfirmed: true);

    private static (C.ComboFrame Frame, C.ComboEvidence Evidence, C.ComboObservation[] Events) Rogue()
    {
        var c11 = Cell(11);
        var c12 = Cell(12);
        return (Frame(C.RogueBreedId),
            new(TargetId: Enemy,
                SourceSpell: Spell(13444, Row(1008, SpellEffectKind.Summon, dice: 3112)),
                FollowupSpell: Spell(13455, Row(99, SpellEffectKind.Damage)),
                FirstMarkId: 501, TargetCell: c11, FirstBombId: 101, SecondBombId: 102,
                Wall: new C.VerifiedRogueWall(2, Actor, 101, 102,
                    ImmutableHashSet.Create(c11, c12), Active: true),
                CurrentBombs: new[]
                {
                    new PlannerFighter(101, Cell(10), false, 100, 100,
                        MonsterTemplate: 3112, SummonerId: Actor),
                    new PlannerFighter(102, Cell(13), false, 100, 100,
                        MonsterTemplate: 3112, SummonerId: Actor)
                },
                EffectActionId: 99, FollowupCorrelation: 700,
                ClientVersion: C.VerifiedClassClientVersion,
                SourceBreedId: C.RogueBreedId, FollowupBreedId: C.RogueBreedId,
                ObservedLearnedGrades: new Dictionary<int, int> { [13444] = 1 }),
            new[]
            {
                E(1, C.ComboEventKind.Summon, subject: 101, spell: 13444, template: 3112, turn: 1),
                E(2, C.ComboEventKind.Summon, subject: 102, spell: 13444, template: 3112, turn: 1),
                E(3, C.ComboEventKind.WallActivated, mark: 501, template: 2),
                E(4, C.ComboEventKind.Move, source: -8, to: c11),
                // Synthetic requirement fixture: this witness must later be
                // built from a correlated server packet, never inferred from
                // the mere presence of an explosion damage row.
                E(5, C.ComboEventKind.DetonationConfirmed, subject: 101,
                    spell: 13455, template: 3112, corr: 700),
                E(6, C.ComboEventKind.Effect, spell: 13455, action: 99, corr: 700)
            });
    }

    private static (C.ComboFrame Frame, C.ComboEvidence Evidence, C.ComboObservation[] Events) Eliotrope()
    {
        var caster = Cell(10, 0);
        var entry = Cell(12, 0);
        var exit = Cell(10, 9);
        var enemy = Cell(12, 9);
        return (Frame(C.EliotropeBreedId),
            new(TargetId: Enemy,
                SourceSpell: Spell(14574, Row(1181, SpellEffectKind.Unknown, dice: 2)),
                FollowupSpell: Spell(14579, Row(99, SpellEffectKind.Damage)),
                FirstMarkId: 501, SecondMarkId: 502,
                FirstCell: entry, SecondCell: exit, TargetCell: enemy, CasterCell: caster,
                Portals: new[] { new C.Portal(501, entry, Actor), new C.Portal(502, exit, Actor) },
                EffectActionId: 99, FollowupCorrelation: 700,
                ClientVersion: C.VerifiedClassClientVersion,
                SourceBreedId: C.EliotropeBreedId, FollowupBreedId: C.EliotropeBreedId,
                ObservedLearnedGrades: new Dictionary<int, int> { [14574] = 1, [14579] = 1 }),
            new[]
            {
                E(1, C.ComboEventKind.MarkPlaced, mark: 501, action: 1181, to: entry, turn: 1),
                E(2, C.ComboEventKind.MarkPlaced, mark: 502, action: 1181, to: exit, turn: 1),
                E(3, C.ComboEventKind.MarkActivated, mark: 501),
                E(4, C.ComboEventKind.MarkActivated, mark: 502),
                E(5, C.ComboEventKind.Cast, spell: 14579, to: entry, corr: 700),
                E(6, C.ComboEventKind.Effect, spell: 14579, action: 99, corr: 700)
            });
    }

    private static (C.ComboFrame Frame, C.ComboEvidence Evidence, C.ComboObservation[] Events) Xelor()
    {
        var initialSelf = Cell(10);
        var initialTarget = Cell(11);
        return (Frame(C.XelorBreedId),
            new(TargetId: Enemy, SourceSpell: Spell(13285, Row(8, SpellEffectKind.Swap)),
                StateId: C.StateTelefrag, FirstCell: initialSelf,
                SecondCell: initialTarget, TargetCell: initialTarget, SourceCorrelation: 700,
                TargetIsEnemy: true,
                ClientVersion: C.VerifiedClassClientVersion,
                SourceBreedId: C.XelorBreedId,
                ObservedLearnedGrades: new Dictionary<int, int> { [13285] = 1 }),
            new[]
            {
                E(1, C.ComboEventKind.Cast, spell: 13285, to: initialTarget, corr: 700),
                E(2, C.ComboEventKind.Move, source: Actor, subject: Actor,
                    from: initialSelf, to: initialTarget, corr: 700),
                E(3, C.ComboEventKind.Move, source: Actor, subject: Enemy,
                    from: initialTarget, to: initialSelf, corr: 700),
                E(4, C.ComboEventKind.StateAdded, state: C.StateTelefrag, corr: 700)
            });
    }

    [Fact]
    public void Rogue_requires_two_summons_wall_move_confirmed_detonation_and_effect()
    {
        var (frame, evidence, events) = Rogue();
        var proof = C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, events);
        Assert.True(proof.Complete, proof.Reason);
        Assert.Equal(6, proof.RequiredStages);
        Assert.Equal(6, proof.ConfirmedStages);

        var incomplete = C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, events[..^1]);
        Assert.False(incomplete.Complete);
        Assert.Equal(5, incomplete.ConfirmedStages);
        Assert.Equal(6, incomplete.RequiredStages);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Rogue_never_accepts_a_missing_confirmed_stage(int removed)
    {
        var (frame, evidence, events) = Rogue();
        var without = events.Where((_, i) => i != removed).ToArray();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, without).Complete);
    }

    [Fact]
    public void Rogue_refuses_removed_bomb_or_wall_and_unverified_wall_identity()
    {
        var (frame, evidence, events) = Rogue();
        var bombKilled = events.Take(3).Concat(new[]
            { E(35, C.ComboEventKind.SummonRemoved, subject: 101) }).Concat(events.Skip(3)
                .Select(e => e with { Order = e.Order + 35 })).ToArray();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, bombKilled).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame,
            evidence with { Wall = evidence.Wall! with { OwnerId = 999 } }, events).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame,
            evidence with { SourceSpell = Spell(13444, Row(1008, SpellEffectKind.Summon, dice: 0)) }, events).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame,
            evidence with { CurrentBombs = evidence.CurrentBombs!.Select(b => b.Id == 101
                ? b with { Life = 0 } : b).ToArray() }, events).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame,
            evidence with { CurrentBombs = evidence.CurrentBombs!.Select(b => b.Id == 102
                ? b with { SummonerId = 99 } : b).ToArray() }, events).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence,
            events.Select((e, i) => i == 2 ? e with { TemplateId = 0 } : e).ToArray()).Complete);
    }

    [Fact]
    public void Eliotrope_requires_both_active_owned_portals_and_the_confirmed_entry_cast()
    {
        var (frame, evidence, events) = Eliotrope();
        var proof = C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect, frame, evidence, events);
        Assert.True(proof.Complete, proof.Reason);
        Assert.Equal(6, proof.ConfirmedStages);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect, frame,
            evidence with { Portals = new[] { new C.Portal(501, evidence.FirstCell, Actor),
                new C.Portal(502, evidence.SecondCell, Actor, Active: false) } }, events).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect, frame,
            evidence with { TargetCell = Cell(13, 9) }, events).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect, frame, evidence,
            events.Select((e, i) => i == 4 ? e with { ToCell = evidence.SecondCell } : e).ToArray()).Complete);
    }

    [Fact]
    public void Eliotrope_refuses_same_mark_deactivation_wrong_author_and_uncorrelated_hit()
    {
        var (frame, evidence, events) = Eliotrope();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect, frame,
            evidence with { SecondMarkId = evidence.FirstMarkId }, events).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect, frame, evidence,
            events.Select((e, i) => i == 1 ? e with { Source = 500 } : e).ToArray()).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect, frame, evidence,
            events.Select((e, i) => i == 5 ? e with { Correlation = 0 } : e).ToArray()).Complete);
        var deactivated = events.Take(4).Concat(new[]
            { E(5, C.ComboEventKind.PortalDeactivated, mark: 502) })
            .Concat(events.Skip(4).Select(e => e with { Order = e.Order + 1 })).ToArray();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect, frame, evidence, deactivated).Complete);
    }

    [Fact]
    public void Eliotrope_push_requires_confirmed_target_displacement_after_observed_portal_effect()
    {
        // Installed 3.7.13.17 Eliotrope grade-1 Affront 14575 contains push action 5.
        // The destination is a *packet observation*, never extrapolated from dice.
        var (frame, evidence, events) = Eliotrope();
        int arrived = Cell(13, 9);
        var push = evidence with
        {
            FollowupSpell = Spell(14575, Row(5, SpellEffectKind.Push, dice: 2)),
            EffectActionId = 5,
            DisplacedToCell = arrived,
            ObservedLearnedGrades = new Dictionary<int, int> { [14574] = 1, [14575] = 1 }
        };
        var pushed = events.Take(4)
            .Concat(new[]
            {
                E(5, C.ComboEventKind.Cast, spell: 14575, to: evidence.FirstCell, corr: 700),
                E(6, C.ComboEventKind.Effect, spell: 14575, action: 5, corr: 700),
                E(7, C.ComboEventKind.Move, source: Actor, subject: Enemy,
                    from: evidence.TargetCell, to: arrived, corr: 700)
            }).ToArray();
        var result = C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect, frame, push, pushed);
        Assert.True(result.Complete, result.Reason);
        Assert.Equal(7, result.RequiredStages);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
            frame, push, pushed[..^1]).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
            frame, push with { DisplacedToCell = -1 }, pushed).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
            frame, push, pushed.Select((e, i) => i == 6 ? e with { Correlation = 0 } : e).ToArray()).Complete);
    }

    [Fact]
    public void Xelor_requires_same_cast_two_positions_and_the_observed_telefrag_state()
    {
        var (frame, evidence, events) = Xelor();
        Assert.True(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence, events).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence,
            events.Take(3).ToArray()).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence,
            events.Select((e, i) => i == 2 ? e with { FromCell = evidence.FirstCell } : e).ToArray()).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence,
            events.Select((e, i) => i == 3 ? e with { StateId = 0 } : e).ToArray()).Complete);
    }

    [Fact]
    public void Xelor_server_can_report_target_movement_before_caster_movement()
    {
        var (frame, evidence, events) = Xelor();
        var reversed = events.ToArray();
        (reversed[1], reversed[2]) = (reversed[2] with { Order = 2 }, reversed[1] with { Order = 3 });
        Assert.True(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence, reversed).Complete);
    }

    [Fact]
    public void Xelor_refuses_unattributed_or_expired_telefrag_claims()
    {
        var (frame, evidence, events) = Xelor();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence,
            events.Select((e, i) => i == 1 ? e with { Correlation = 701 } : e).ToArray()).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame,
            evidence with { StateId = C.StateAllyTelefrag }, events).Complete);
        var removed = events.Concat(new[] { E(5, C.ComboEventKind.StateRemoved, state: C.StateTelefrag) }).ToArray();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence, removed).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame,
            evidence with { TargetIsEnemy = null }, events).Complete);
    }

    [Theory]
    [InlineData(C.ComboKind.FecaGlyph, C.FecaBreedId)]
    [InlineData(C.ComboKind.EcaflipCard, C.EcaflipBreedId)]
    [InlineData(C.ComboKind.SacrierSuffering, C.SacrierBreedId)]
    public void Classes_without_pinned_state_or_mark_rows_fail_closed_instead_of_guessing(
        C.ComboKind kind, int breed)
    {
        // N01 3.7.13.17 class-effect export contains only Sram/Rogue/Xelor/Elio.
        // No card state, glyph trigger or suffering effect ID is fabricated.
        var result = C.VerifyObservedCombo(kind, Frame(breed), new(TargetId: Enemy), Array.Empty<C.ComboObservation>());
        Assert.False(result.Complete);
        Assert.Equal(0, result.ConfirmedStages);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory]
    [InlineData(C.ComboKind.FecaGlyph, C.FecaBreedId)]
    [InlineData(C.ComboKind.EcaflipCard, C.EcaflipBreedId)]
    [InlineData(C.ComboKind.SacrierSuffering, C.SacrierBreedId)]
    public void Generic_damage_or_a_mark_is_not_a_class_specific_combo_without_a_real_grade(
        C.ComboKind kind, int breed)
    {
        var events = new[]
        {
            E(1, C.ComboEventKind.Cast, spell: 14579, corr: 700),
            E(2, C.ComboEventKind.MarkPlaced, mark: 501),
            E(3, C.ComboEventKind.StateAdded, state: C.StateTelefrag),
            E(4, C.ComboEventKind.Effect, spell: 14579, action: 99, corr: 700)
        };
        var spoof = new C.ComboEvidence(TargetId: Enemy,
            SourceSpell: Spell(14579, Row(99, SpellEffectKind.Damage)),
            FollowupSpell: Spell(14579, Row(99, SpellEffectKind.Damage)),
            StateId: 0, FirstMarkId: 501, EffectActionId: 99,
            SourceCorrelation: 700, FollowupCorrelation: 700);
        Assert.False(C.VerifyObservedCombo(kind, Frame(breed), spoof, events).Complete);
    }

    [Fact]
    public void Wrong_generation_wrong_breed_or_unconfirmed_event_cannot_complete_a_sequence()
    {
        var (frame, evidence, events) = Rogue();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame with { BreedId = C.EliotropeBreedId }, evidence, events).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame with { Generation = 7 }, evidence, events).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, events.Select((e, i) => i == 2 ? e with { ServerConfirmed = false } : e).ToArray()).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence,
            events.Select((e, i) => i == 4 ? e with { FightId = Fight + 1 } : e).ToArray()).Complete);
    }

    [Fact]
    public void Client_version_breed_join_and_learned_grade_are_mandatory_even_for_complete_packets()
    {
        var (frame, evidence, events) = Rogue();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame,
            evidence with { ClientVersion = "3.7.12.12" }, events).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame,
            evidence with { SourceBreedId = C.XelorBreedId }, events).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame,
            evidence with { ObservedLearnedGrades = new Dictionary<int, int> { [13444] = 2 } }, events).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame,
            evidence with { ObservedLearnedGrades = null }, events).Complete);
        Assert.True(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, events).Complete);
    }

    [Fact]
    public void Irrelevant_events_from_other_generations_are_ignored_without_granting_an_unconfirmed_stage()
    {
        var (frame, evidence, events) = Rogue();
        var unrelated = E(0, C.ComboEventKind.WallActivated, mark: 501) with
        { Order = 20, FightId = Fight + 1, Generation = frame.Generation - 1 };
        var harmless = E(3, C.ComboEventKind.Cast, spell: 14579, corr: 701);
        var stream = events.Take(2).Concat(new[] { harmless with { Order = 3 } })
            .Concat(events.Skip(2).Select(e => e with { Order = e.Order + 1 }))
            .Concat(new[] { unrelated }).ToArray();
        Assert.True(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, stream).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence,
            stream.Where(e => e.Kind != C.ComboEventKind.WallActivated).ToArray()).Complete);
    }

    [Fact]
    public void Observed_wall_removal_blocks_completion_even_if_a_later_effect_matches()
    {
        var (frame, evidence, events) = Rogue();
        var revoked = events.Take(3)
            .Concat(new[] { E(4, C.ComboEventKind.MarkRemoved, mark: evidence.FirstMarkId) })
            .Concat(events.Skip(3).Select(e => e with { Order = e.Order + 1 })).ToArray();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, revoked).Complete);
    }

    [Fact]
    public void Duplicate_or_reversed_event_order_is_not_sorted_into_a_fictional_combo()
    {
        var (frame, evidence, events) = Rogue();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence,
            events.Select((e, i) => i == 4 ? e with { Order = 4 } : e).ToArray()).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence,
            new[] { events[1], events[0], events[2], events[3], events[4] }).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence,
            events.Select((e, i) => i == 3 ? e with { Turn = 1 } : e).ToArray()).Complete);
    }
}
