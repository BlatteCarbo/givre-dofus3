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
/// Requirement fixtures for a trustworthy server-event adapter. These packets
/// are SYNTHETIC; Client 3.7.13.17 confirms the spell/action/template joins,
/// not an actual server detonation or Telefrag ordering. No game is started.
/// </summary>
public sealed class ClassMechanicsFalseCertificationTests
{
    private const long Fight = 8001;
    private const long Owner = 75;
    private const long Enemy = 90;
    private const long BombA = 101;
    private const long BombB = 102;
    private const long CastCorrelation = 9001;

    private static int Cell(int x, int y = 0)
    {
        int cell = MapPathfinder.CellFromMapPoint(x, y);
        Assert.InRange(cell, 0, MapPathfinder.CellCount - 1);
        return cell;
    }

    private static SpellCastDescriptor Spell(int id, int action, SpellEffectKind kind, int dice = 0)
        => new(id, 1, 3, 1, 6, 0, false, 2, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(action, kind, 0, 0, 0, 0,
                "a,A", 'P', 1, 0, DiceNum: dice, Triggers: "I")));

    private static C.ComboFrame Frame(int breed)
        => new(Fight, 7, 1, 3, Owner, breed);

    private static C.ComboObservation Event(int order, C.ComboEventKind kind,
        long source = Owner, long subject = Enemy, int spell = 0, int action = 0,
        int mark = 0, int template = 0, int state = 0, int from = -1, int to = -1,
        long correlation = 0, int turn = 2)
        => new(order, Fight, 7, turn, source, subject, kind, SpellId: spell,
            Grade: spell == 0 ? 0 : 1, ActionId: action, StateId: state,
            TemplateId: template, MarkId: mark, FromCell: from, ToCell: to,
            Correlation: correlation, ServerConfirmed: true);

    private static C.ComboObservation[] Insert(C.ComboObservation[] source, int beforeIndex,
        C.ComboObservation extra)
        => source.Take(beforeIndex).Concat(new[] { extra }).Concat(source.Skip(beforeIndex))
            .Select((e, i) => e with { Order = i + 1 }).ToArray();

    private static (C.ComboFrame Frame, C.ComboEvidence Evidence, C.ComboObservation[] Events) Rogue()
    {
        int a = Cell(10), b = Cell(13), inside = Cell(11), inside2 = Cell(12);
        var evidence = new C.ComboEvidence(TargetId: Enemy,
            SourceSpell: Spell(13444, 1008, SpellEffectKind.Summon, dice: 3112),
            FollowupSpell: Spell(13455, 99, SpellEffectKind.Damage),
            FirstMarkId: 501, TargetCell: inside, FirstBombId: BombA, SecondBombId: BombB,
            Wall: new C.VerifiedRogueWall(2, Owner, BombA, BombB,
                ImmutableHashSet.Create(inside, inside2), true),
            CurrentBombs: new[] {
                new PlannerFighter(BombA, a, false, 100, 100, MonsterTemplate: 3112, SummonerId: Owner),
                new PlannerFighter(BombB, b, false, 100, 100, MonsterTemplate: 3112, SummonerId: Owner)
            },
            EffectActionId: 99, FollowupCorrelation: CastCorrelation,
            ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: C.RogueBreedId, FollowupBreedId: C.RogueBreedId,
            ObservedLearnedGrades: new Dictionary<int, int> { [13444] = 1 });
        var events = new[] {
            Event(1, C.ComboEventKind.Summon, subject: BombA, spell: 13444, template: 3112, turn: 1),
            Event(2, C.ComboEventKind.Summon, subject: BombB, spell: 13444, template: 3112, turn: 1),
            Event(3, C.ComboEventKind.WallActivated, mark: 501, template: 2),
            Event(4, C.ComboEventKind.Move, source: Enemy, to: inside),
            Event(5, C.ComboEventKind.DetonationConfirmed, subject: BombA, spell: 13455,
                template: 3112, correlation: CastCorrelation),
            Event(6, C.ComboEventKind.Effect, spell: 13455, action: 99,
                correlation: CastCorrelation)
        };
        return (Frame(C.RogueBreedId), evidence, events);
    }

    private static (C.ComboFrame Frame, C.ComboEvidence Evidence, C.ComboObservation[] Events) Xelor()
    {
        int oldCaster = Cell(10), oldTarget = Cell(11);
        var evidence = new C.ComboEvidence(TargetId: Enemy,
            SourceSpell: Spell(13285, 8, SpellEffectKind.Swap),
            StateId: C.StateTelefrag, FirstCell: oldCaster,
            SecondCell: oldTarget, TargetCell: oldTarget,
            SourceCorrelation: CastCorrelation, TargetIsEnemy: true,
            ClientVersion: C.VerifiedClassClientVersion, SourceBreedId: C.XelorBreedId,
            ObservedLearnedGrades: new Dictionary<int, int> { [13285] = 1 });
        var events = new[] {
            Event(1, C.ComboEventKind.Cast, spell: 13285, to: oldTarget, correlation: CastCorrelation),
            Event(2, C.ComboEventKind.Move, subject: Owner, from: oldCaster, to: oldTarget,
                correlation: CastCorrelation),
            Event(3, C.ComboEventKind.Move, subject: Enemy, from: oldTarget, to: oldCaster,
                correlation: CastCorrelation),
            Event(4, C.ComboEventKind.StateAdded, subject: Enemy, state: C.StateTelefrag,
                correlation: CastCorrelation)
        };
        return (Frame(C.XelorBreedId), evidence, events);
    }

    [Fact]
    public void Rogue_explosion_is_not_certified_from_its_damage_row_without_a_distinct_detonation_witness()
    {
        var (frame, evidence, events) = Rogue();
        Assert.True(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, events).Complete);
        var withoutCause = events.Where(e => e.Kind != C.ComboEventKind.DetonationConfirmed).ToArray();
        var proof = C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, withoutCause);
        Assert.False(proof.Complete);
        Assert.Equal(6, proof.RequiredStages);
        Assert.True(proof.Reason.Contains("out of order", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Rogue_requires_every_distinct_server_step(int deletedIndex)
    {
        var (frame, evidence, events) = Rogue();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, events.Where((_, i) => i != deletedIndex).ToArray()).Complete);
    }

    [Theory]
    [InlineData("foreign-source")]
    [InlineData("foreign-bomb")]
    [InlineData("wrong-template")]
    [InlineData("wrong-grade")]
    [InlineData("wrong-correlation")]
    [InlineData("unconfirmed")]
    [InlineData("other-fight")]
    public void Rogue_detonation_witness_must_match_bomb_owner_spell_grade_and_same_fight(string mutation)
    {
        var (frame, evidence, events) = Rogue();
        var detonation = events[4];
        detonation = mutation switch {
            "foreign-source" => detonation with { Source = 12 },
            "foreign-bomb" => detonation with { Subject = 12 },
            "wrong-template" => detonation with { TemplateId = 2831 },
            "wrong-grade" => detonation with { Grade = 3 },
            "wrong-correlation" => detonation with { Correlation = 9011 },
            "unconfirmed" => detonation with { ServerConfirmed = false },
            "other-fight" => detonation with { FightId = Fight + 1 },
            _ => throw new InvalidOperationException(mutation)
        };
        events[4] = detonation;
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, events).Complete);
    }

    [Theory]
    [InlineData("foreign-source")]
    [InlineData("foreign-target")]
    [InlineData("wrong-spell")]
    [InlineData("wrong-grade")]
    [InlineData("wrong-action")]
    public void Rogue_damage_must_refer_to_the_same_confirmed_explosion_effect(string mutation)
    {
        var (frame, evidence, events) = Rogue();
        var hit = events[^1];
        events[^1] = mutation switch {
            "foreign-source" => hit with { Source = 555 },
            "foreign-target" => hit with { Subject = 555 },
            "wrong-spell" => hit with { SpellId = 13456 },
            "wrong-grade" => hit with { Grade = 2 },
            "wrong-action" => hit with { ActionId = 1009 },
            _ => throw new InvalidOperationException(mutation)
        };
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, events).Complete);
    }

    [Fact]
    public void Rogue_early_confirmed_explosion_or_hit_cannot_be_reinterpreted_after_a_later_trigger()
    {
        var (frame, evidence, events) = Rogue();
        var earlyDetonation = Insert(events, 3, events[4]);
        var earlyHit = Insert(events, 4, events[5]);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, earlyDetonation).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, earlyHit).Complete);
    }

    [Fact]
    public void Rogue_effect_from_wrong_author_cannot_be_skipped_to_cherry_pick_a_later_matching_hit()
    {
        var (frame, evidence, events) = Rogue();
        var foreignHit = events[5] with { Source = Enemy };
        var wrongTarget = events[5] with { Subject = Owner };
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence,
            Insert(events, 5, foreignHit)).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence,
            Insert(events, 5, wrongTarget)).Complete);
    }

    [Fact]
    public void Rogue_rejects_same_bomb_identity_summoned_by_a_foreign_actor_before_correct_ownership()
    {
        var (frame, evidence, events) = Rogue();
        var wrongOwner = events[0] with { Source = Enemy };
        var wrongTemplate = events[0] with { TemplateId = 0 };
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence,
            Insert(events, 0, wrongOwner)).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence,
            Insert(events, 0, wrongTemplate)).Complete);
    }

    [Fact]
    public void Rogue_rejects_wall_activation_or_target_entry_with_conflicting_event_author()
    {
        var (frame, evidence, events) = Rogue();
        var wrongMarkOwner = events[2] with { Source = Enemy };
        var thirdPartyMove = events[3] with { Source = 999 };
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence,
            Insert(events, 2, wrongMarkOwner)).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence,
            Insert(events, 3, thirdPartyMove)).Complete);
    }

    [Fact]
    public void Rogue_removal_followed_by_same_identity_recreation_cannot_erase_authoritative_death()
    {
        var (frame, evidence, events) = Rogue();
        var removed = Insert(events, 4, Event(0, C.ComboEventKind.SummonRemoved, subject: BombA));
        var readded = Insert(removed, 5, events[0] with { Turn = 2 });
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall, frame, evidence, readded).Complete);
    }

    [Fact]
    public void Xelor_requires_two_distinct_actor_movements_confirmed_by_the_same_caster_and_cast()
    {
        var (frame, evidence, events) = Xelor();
        Assert.True(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence, events).Complete);
        var thirdParty = events.ToArray();
        thirdParty[2] = thirdParty[2] with { Source = Enemy };
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence, thirdParty).Complete);
        var unrelatedAction = events.ToArray();
        unrelatedAction[2] = unrelatedAction[2] with { Correlation = CastCorrelation + 1 };
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence, unrelatedAction).Complete);
    }

    [Fact]
    public void Xelor_rejects_a_third_movement_between_cast_and_state_even_if_a_later_pair_matches()
    {
        var (frame, evidence, events) = Xelor();
        var stray = Event(0, C.ComboEventKind.Move, source: Enemy, subject: Owner,
            from: Cell(10), to: Cell(12), correlation: CastCorrelation + 1);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence,
            Insert(events, 1, stray)).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence,
            Insert(events, 2, events[1])).Complete);
    }

    [Fact]
    public void Xelor_cannot_credit_telefrag_state_from_a_different_caster_or_before_the_swap()
    {
        var (frame, evidence, events) = Xelor();
        var wrongOwner = events[3] with { Source = Enemy };
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence,
            Insert(events, 3, wrongOwner)).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence,
            Insert(events, 1, events[3])).Complete);
    }

    [Fact]
    public void Xelor_same_cast_correlation_from_foreign_actor_cannot_be_skipped_for_a_later_valid_cast()
    {
        var (frame, evidence, events) = Xelor();
        var mismatched = events[0] with { Source = Enemy };
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence,
            Insert(events, 0, mismatched)).Complete);
    }

    [Fact]
    public void Xelor_removed_then_reapplied_telefrag_is_a_new_state_generation_not_an_old_proof()
    {
        var (frame, evidence, events) = Xelor();
        var removed = Event(0, C.ComboEventKind.StateRemoved,
            subject: Enemy, state: C.StateTelefrag, correlation: CastCorrelation);
        var reopened = Insert(events, 3, removed);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence, reopened).Complete);
        var postSuccess = events.Concat(new[] {
            removed with { Order = 5 }, events[3] with { Order = 6 }
        }).ToArray();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence, postSuccess).Complete);
    }

    [Fact]
    public void Xelor_rejects_self_swap_and_unidentified_target_side()
    {
        var (frame, evidence, events) = Xelor();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame,
            evidence with { TargetId = Owner }, events).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame,
            evidence with { TargetIsEnemy = null }, events).Complete);
    }

    [Fact]
    public void Xelor_both_real_server_orderings_are_accepted_but_the_event_stream_itself_is_never_sorted()
    {
        var (frame, evidence, events) = Xelor();
        var reverse = events.ToArray();
        (reverse[1], reverse[2]) = (reverse[2] with { Order = 2 }, reverse[1] with { Order = 3 });
        Assert.True(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence, reverse).Complete);

        var malformed = events.ToArray();
        (malformed[1], malformed[2]) = (malformed[2], malformed[1]);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence, malformed).Complete);
    }
}
