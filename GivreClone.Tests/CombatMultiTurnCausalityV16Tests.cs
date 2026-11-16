using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;
using Xunit.Abstractions;
using C = GivreClone.App.Services.Combat.ClassMechanics;
using E = GivreClone.App.Services.Combat.VerifiedTacticalExecutor;
using V = GivreClone.App.Services.Combat.VerifiedTacticalSequence;

namespace GivreClone.Tests;

/// <summary>
/// Offline replay of real PrepareObservedDecision/Inspect/VerifyObservedCombo
/// APIs. All positive receipt/mark/correlation events are synthetic fixtures:
/// the real diagnostic ingress does not supply authenticated cast ACKs.
/// No action can be dispatched by this test or the reviewed APIs.
/// </summary>
public sealed class CombatMultiTurnCausalityV16Tests
{
    private readonly ITestOutputHelper _output;
    public CombatMultiTurnCausalityV16Tests(ITestOutputHelper output) => _output = output;

    private const long Actor = 77, Enemy = -9, Fight = 1616;
    private const int Generation = 8;
    private static readonly int Start = Cell(10, 0);
    private static readonly int Entry = Cell(12, 0);
    private static readonly int Exit = Cell(10, 9);
    private static readonly int Impact = Cell(12, 9);
    private static readonly PlannerCell[] BoardCells = Enumerable.Range(0, MapPathfinder.CellCount)
        .Select(id => new PlannerCell(id, true, false, ImmutableArray<int>.Empty)).ToArray();

    private static int Cell(int x, int y) => MapPathfinder.CellFromMapPoint(x, y);

    private static SpellCastDescriptor Spell(int id, int action, SpellEffectKind kind,
        int flags, int dice = 0, int ap = 2, string mask = "a,A")
        => new(id, 1, ap, 1, 10, flags, false, 2, 2, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(action, kind,
                0, 0, 0, 0, mask, 'P', 1, 0, DiceNum: dice)));

    private static readonly SpellCastDescriptor Bomb = Spell(13444, 1008,
        SpellEffectKind.Summon, 12, dice: C.VerifiedExplobombTemplate);
    private static readonly SpellCastDescriptor Blast = Spell(13455, 99,
        SpellEffectKind.Damage, 4, mask: "A");
    private static readonly SpellCastDescriptor Portal = Spell(14574, 1181,
        SpellEffectKind.Unknown, 12, ap: 1);
    private static readonly SpellCastDescriptor Ray = Spell(14579, 99,
        SpellEffectKind.Damage, 4, ap: 3, mask: "A");
    private static readonly SpellCastDescriptor Swap = Spell(13285, 8,
        SpellEffectKind.Swap, 20, mask: "A");

    private static C.ComboFrame Frame(int breed, int lastTurn = 10)
        => new(Fight, Generation, 1, lastTurn, Actor, breed);

    private static V.Snapshot Board(int breed, IReadOnlyDictionary<int, int> grades,
        int enemyCell = -1, IReadOnlyList<C.Portal>? portals = null)
        => new(Fight, Generation, 19, 1, Actor, breed, Start, 6, 3, 0,
            false, BoardCells,
            new[]
            {
                new PlannerFighter(Actor, Start, false, 100, 100),
                new PlannerFighter(Enemy, enemyCell < 0 ? Entry : enemyCell,
                    true, 100, 100)
            }, new HashSet<int>(), portals ?? Array.Empty<C.Portal>(),
            KnownSpellGrades: grades);

    private static E.ObservedDecisionFacts Facts(int breed,
        params SpellCastDescriptor[] spells)
    {
        var book = spells.ToDictionary(s => s.SpellId, s => s.Grade);
        return new E.ObservedDecisionFacts(
            new C.ComboFrame(Fight, Generation, 1, 1, Actor, breed),
            Board(breed, book), C.VerifiedClassClientVersion, book, spells);
    }

    private static C.ComboObservation Event(int order, int turn, C.ComboEventKind kind,
        long subject = Enemy, long source = Actor, int spell = 0,
        int from = -1, int to = -1, int mark = 0, int action = 0,
        int template = 0, int state = 0, long correlation = 0,
        bool confirmed = true)
        => new(order, Fight, Generation, turn, source, subject, kind,
            SpellId: spell, Grade: spell > 0 ? 1 : 0,
            FromCell: from, ToCell: to, MarkId: mark, ActionId: action,
            TemplateId: template, StateId: state, Correlation: correlation,
            ServerConfirmed: confirmed);

    private static (C.ComboFrame Frame, C.ComboEvidence Evidence,
        C.ComboObservation[] History) RogueHistory()
    {
        int b1 = Cell(10, 0), b2 = Cell(13, 0);
        int wall = Cell(11, 0), wall2 = Cell(12, 0);
        var evidence = new C.ComboEvidence(TargetId: Enemy,
            SourceSpell: Bomb, FollowupSpell: Blast,
            FirstMarkId: 501, FirstBombId: 101, SecondBombId: 102,
            TargetCell: wall,
            Wall: new C.VerifiedRogueWall(C.VerifiedExplobombWall,
                Actor, 101, 102, ImmutableHashSet.Create(wall, wall2), true),
            CurrentBombs: new[]
            {
                new PlannerFighter(101, b1, false, 100, 100,
                    MonsterTemplate: C.VerifiedExplobombTemplate, SummonerId: Actor),
                new PlannerFighter(102, b2, false, 100, 100,
                    MonsterTemplate: C.VerifiedExplobombTemplate, SummonerId: Actor)
            },
            EffectActionId: 99, FollowupCorrelation: 73,
            ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: C.RogueBreedId, FollowupBreedId: C.RogueBreedId,
            ObservedLearnedGrades: new Dictionary<int, int> { [Bomb.SpellId] = 1 });
        return (Frame(C.RogueBreedId), evidence, new[]
        {
            Event(1, 1, C.ComboEventKind.Cast, spell: 13444, to: b1, correlation: 71),
            Event(2, 1, C.ComboEventKind.Summon, subject: 101,
                spell: 13444, template: C.VerifiedExplobombTemplate, correlation: 71),
            Event(3, 2, C.ComboEventKind.Cast, spell: 13444, to: b2, correlation: 72),
            Event(4, 2, C.ComboEventKind.Summon, subject: 102,
                spell: 13444, template: C.VerifiedExplobombTemplate, correlation: 72),
            Event(5, 2, C.ComboEventKind.WallActivated,
                mark: 501, template: C.VerifiedExplobombWall),
            Event(6, 3, C.ComboEventKind.Move, source: Enemy, to: wall),
            Event(7, 3, C.ComboEventKind.Cast, spell: 13455, to: wall, correlation: 73),
            Event(8, 3, C.ComboEventKind.DetonationConfirmed, subject: 101,
                spell: 13455, template: C.VerifiedExplobombTemplate, correlation: 73),
            Event(9, 3, C.ComboEventKind.Effect, spell: 13455,
                action: 99, to: wall, correlation: 73)
        });
    }

    private static (C.ComboFrame Frame, C.ComboEvidence Evidence,
        C.ComboObservation[] History) EliotropeHistory()
    {
        var evidence = new C.ComboEvidence(TargetId: Enemy,
            SourceSpell: Portal, FollowupSpell: Ray,
            FirstMarkId: 501, SecondMarkId: 502,
            FirstCell: Entry, SecondCell: Exit,
            TargetCell: Impact, CasterCell: Start,
            Portals: new[] { new C.Portal(501, Entry, Actor),
                new C.Portal(502, Exit, Actor) },
            EffectActionId: 99, FollowupCorrelation: 83,
            ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: C.EliotropeBreedId,
            FollowupBreedId: C.EliotropeBreedId,
            ObservedLearnedGrades: new Dictionary<int, int>
            {
                [Portal.SpellId] = 1, [Ray.SpellId] = 1
            });
        return (Frame(C.EliotropeBreedId), evidence, new[]
        {
            Event(1, 1, C.ComboEventKind.Cast, spell: 14574, to: Entry, correlation: 81),
            Event(2, 1, C.ComboEventKind.MarkPlaced, mark: 501,
                action: 1181, to: Entry, correlation: 81),
            Event(3, 2, C.ComboEventKind.Cast, spell: 14574, to: Exit, correlation: 82),
            Event(4, 2, C.ComboEventKind.MarkPlaced, mark: 502,
                action: 1181, to: Exit, correlation: 82),
            Event(5, 2, C.ComboEventKind.MarkActivated, mark: 501, to: Entry),
            Event(6, 2, C.ComboEventKind.MarkActivated, mark: 502, to: Exit),
            Event(7, 3, C.ComboEventKind.Cast, spell: 14579, to: Entry, correlation: 83),
            Event(8, 3, C.ComboEventKind.Effect,
                spell: 14579, action: 99, to: Impact, correlation: 83)
        });
    }

    private static (C.ComboFrame Frame, C.ComboEvidence Evidence,
        C.ComboObservation[] History) XelorHistory()
    {
        var evidence = new C.ComboEvidence(TargetId: Enemy, SourceSpell: Swap,
            FirstCell: Start, SecondCell: Entry, TargetCell: Entry,
            StateId: C.StateTelefrag, TargetIsEnemy: true,
            SourceCorrelation: 91,
            ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: C.XelorBreedId,
            ObservedLearnedGrades: new Dictionary<int, int> { [Swap.SpellId] = 1 });
        return (Frame(C.XelorBreedId), evidence, new[]
        {
            Event(1, 1, C.ComboEventKind.Cast, spell: 13285,
                to: Entry, correlation: 91),
            Event(2, 2, C.ComboEventKind.Move, subject: Actor, source: Actor,
                from: Start, to: Entry, correlation: 91),
            Event(3, 2, C.ComboEventKind.Move, subject: Enemy, source: Actor,
                from: Entry, to: Start, correlation: 91),
            Event(4, 2, C.ComboEventKind.StateAdded,
                state: C.StateTelefrag, correlation: 91)
        });
    }

    [Theory]
    [InlineData(C.RogueBreedId, C.ComboKind.RogueWall)]
    [InlineData(C.EliotropeBreedId, C.ComboKind.EliotropeRedirect)]
    [InlineData(C.XelorBreedId, C.ComboKind.XelorTelefrag)]
    public void Decision_admission_and_Inspect_are_one_turn_advisory_only(
        int breed, C.ComboKind kind)
    {
        var spell = breed == C.RogueBreedId ? Bomb
            : breed == C.EliotropeBreedId ? Portal : Swap;
        var facts = Facts(breed, spell);
        var selected = E.PrepareObservedDecision(facts, enabled: true);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(kind, selected.Kind);
        Assert.False(selected.CanDispatch);
        Assert.Equal(E.Phase.Advisory, selected.Executor!.Inspect(
            facts.Snapshot, Array.Empty<C.ComboObservation>()).State);
        var nextTurn = facts.Snapshot with { Turn = 2 };
        Assert.Equal(E.Phase.Expired, selected.Executor.Inspect(nextTurn,
            Array.Empty<C.ComboObservation>()).State);
        Assert.False(selected.Executor.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).CanDispatch);
    }

    [Fact]
    public void Roublard_multiturn_complete_requires_each_cast_and_detonation_receipt()
    {
        var (frame, evidence, events) = RogueHistory();
        var history = C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, events);
        Assert.True(history.Complete, history.Reason);
        var current = C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, events, observedAsOfTurn: 3);
        Assert.True(current.Complete, current.Reason);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, events, observedAsOfTurn: 6).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, events.Where(e => e.Order != 1).ToArray(),
            observedAsOfTurn: 3).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, events.Select(e => e.Order == 3
                ? e with { ToCell = Start } : e).ToArray(),
            observedAsOfTurn: 3).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, events.Select(e => e.Order == 3
                ? e with { Turn = 1 } : e).ToArray(),
            observedAsOfTurn: 3).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, events.Select(e => e.Order == 7
                ? e with { ServerConfirmed = false } : e).ToArray(),
            observedAsOfTurn: 3).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, events.Append(Event(10, 4, C.ComboEventKind.MarkRemoved,
                mark: 501)).ToArray(), observedAsOfTurn: 4).Complete);
    }

    [Fact]
    public void Completed_Roublard_replay_does_not_hide_a_new_unknown_transport_request()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var selected = E.PrepareObservedDecision(facts, enabled: true);
        Assert.True(selected.Accepted, selected.Reason);
        var cast = Event(1, 1, C.ComboEventKind.Cast, spell: 13444,
            to: selected.Preview!.Cell, correlation: 121);
        var observed = facts.Snapshot with { LastServerOrder = 1 };
        Assert.Equal(E.Phase.Confirmed, selected.Executor!.Inspect(observed,
            new[] { cast }).State);
        var unknown = selected.Executor.Inspect(observed with
        {
            PendingAction = null
        }, new[] { cast });
        Assert.Equal(E.Phase.Pending, unknown.State);
        Assert.Null(unknown.AdvisoryAction);
        Assert.False(unknown.CanDispatch);
    }

    [Fact]
    public void Eliotrope_multiturn_marks_need_distinct_cast_correlation_and_live_projection()
    {
        var (frame, evidence, events) = EliotropeHistory();
        var proof = C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
            frame, evidence, events, observedAsOfTurn: 3);
        Assert.True(proof.Complete, proof.Reason);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
            frame, evidence, events.Select(e => e.Order == 4
                ? e with { Correlation = 0 } : e).ToArray(),
            observedAsOfTurn: 3).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
            frame, evidence, events.Select(e => e.Order == 3
                ? e with { Correlation = 81 } : e).ToArray(),
            observedAsOfTurn: 3).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
            frame, evidence, events.Select(e => e.Order == 8
                ? e with { ToCell = Entry } : e).ToArray(),
            observedAsOfTurn: 3).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
            frame, evidence with { Portals = new[]
            {
                new C.Portal(501, Entry, Actor),
                new C.Portal(502, Exit, Actor, Active: false)
            } }, events, observedAsOfTurn: 3).Complete);
    }

    [Fact]
    public void Xelor_telefrag_observation_is_attributed_but_expires_after_current_turn()
    {
        var (frame, evidence, events) = XelorHistory();
        Assert.True(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            frame, evidence, events, observedAsOfTurn: 2).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            frame, evidence, events, observedAsOfTurn: 5).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            frame, evidence, events.Select(e => e.Order == 2
                ? e with { Source = Enemy } : e).ToArray(),
            observedAsOfTurn: 2).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            frame, evidence, events.Select(e => e.Order == 4
                ? e with { Correlation = 999 } : e).ToArray(),
            observedAsOfTurn: 2).Complete);
    }

    [Theory]
    [InlineData(C.FecaBreedId, C.ComboKind.FecaGlyph)]
    [InlineData(C.EcaflipBreedId, C.ComboKind.EcaflipCard)]
    [InlineData(C.SacrierBreedId, C.ComboKind.SacrierSuffering)]
    public void Feca_Ecaflip_Sacrier_without_grade_source_or_state_join_remain_refused(
        int breed, C.ComboKind kind)
    {
        // N01 cannot pin these class-specific spell/state IDs. The correct
        // assertion is refusal, never fictitious IDs or inferred action ACK.
        var facts = Facts(breed, Bomb);
        var selection = E.PrepareObservedDecision(facts, enabled: true);
        Assert.False(selection.Accepted);
        Assert.False(selection.CanDispatch);
        var evidence = new C.ComboEvidence(TargetId: Enemy,
            SourceSpell: Bomb, FollowupSpell: Blast,
            ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: breed, FollowupBreedId: breed,
            ObservedLearnedGrades: new Dictionary<int, int>
            {
                [Bomb.SpellId] = 1
            });
        Assert.False(C.VerifyObservedCombo(kind, Frame(breed), evidence,
            RogueHistory().History, observedAsOfTurn: 3).Complete);
    }

    [Fact]
    public void Current_turn_cannot_replay_future_events_or_ambiguous_cast_identity()
    {
        var (frame, evidence, events) = RogueHistory();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, events, observedAsOfTurn: 2).Complete);
        var conflicting = events.Append(Event(10, 3, C.ComboEventKind.Cast,
            spell: Ray.SpellId, to: Entry, correlation: 73)).ToArray();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, conflicting, observedAsOfTurn: 3).Complete);
        var wrongEffect = events.Select(e => e.Order == 9
            ? e with { Correlation = 74 } : e).ToArray();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, wrongEffect, observedAsOfTurn: 3).Complete);
    }

    [Fact]
    public void Offline_bounded_review_allocation_sample_does_not_assert_wall_clock()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var selection = E.PrepareObservedDecision(facts, enabled: true,
            executorLimits: new E.Limits(MaxReviews: 64));
        Assert.True(selection.Accepted, selection.Reason);
        var history = RogueHistory();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 64; i++)
        {
            var review = selection.Executor!.Inspect(facts.Snapshot,
                Array.Empty<C.ComboObservation>());
            Assert.Equal(E.Phase.Advisory, review.State);
            Assert.False(review.CanDispatch);
        }
        for (int i = 0; i < 32; i++)
            Assert.True(C.VerifyObservedCombo(C.ComboKind.RogueWall,
                history.Frame, history.Evidence, history.History,
                observedAsOfTurn: 3).Complete);
        sw.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        _output.WriteLine($"V16 offline 64 Inspect / 32 strict VerifyObservedCombo: "
            + $"{bytes} bytes, {sw.ElapsedTicks} Stopwatch ticks (informational)");
        Assert.InRange(bytes, 0, 64L * 1024 * 1024);
        Assert.Equal(E.Phase.Expired, selection.Executor!.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).State);
    }
}
