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
/// Exact production API admission/replay checks. Spell identities are pinned
/// in the existing 3.7.13.17 class catalogue; actors, correlations and marks
/// below are test fixtures, NEVER inferred server ACKs or dispatch grants.
/// </summary>
public sealed class CombatSequenceAdmissionV14Tests
{
    private readonly ITestOutputHelper _output;
    public CombatSequenceAdmissionV14Tests(ITestOutputHelper output) => _output = output;

    private const long Actor = 77, Enemy = -9, Fight = 6144, Correlation = 920;
    private const int Generation = 6;
    private static readonly int Start = Cell(10, 0);
    private static readonly int Entry = Cell(12, 0);
    private static readonly int Exit = Cell(10, 9);
    private static readonly int Impact = Cell(12, 9);
    private static readonly PlannerCell[] Cells = Enumerable.Range(0, MapPathfinder.CellCount)
        .Select(id => new PlannerCell(id, true, false, ImmutableArray<int>.Empty)).ToArray();

    private static int Cell(int x, int y) => MapPathfinder.CellFromMapPoint(x, y);

    private static SpellCastDescriptor Spell(int id, int action, SpellEffectKind kind,
        int flags, int ap = 2, int dice = 0, string mask = "a,A")
        => new(id, 1, ap, 1, 10, flags, false, 2, 2, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(action, kind,
                0, 0, 0, 0, mask, 'P', 1, 0, DiceNum: dice)));

    private static readonly SpellCastDescriptor Bomb = Spell(13444, 1008,
        SpellEffectKind.Summon, 12, dice: C.VerifiedExplobombTemplate);
    private static readonly SpellCastDescriptor Portal = Spell(14574, 1181,
        SpellEffectKind.Unknown, 12, ap: 1);
    private static readonly SpellCastDescriptor Ray = Spell(14579, 99,
        SpellEffectKind.Damage, 4, ap: 3, mask: "A");
    private static readonly SpellCastDescriptor Swap = Spell(13285, 8,
        SpellEffectKind.Swap, 20, mask: "A");

    private static C.ComboFrame Frame(int breed) => new(Fight, Generation, 1, 1, Actor, breed);

    private static V.Snapshot Board(int breed, IReadOnlyDictionary<int, int> grades,
        int target = -1, IReadOnlyList<C.Portal>? portals = null)
        => new(Fight, Generation, 19, 1, Actor, breed, Start, 6, 3, 0,
            false, Cells,
            new[]
            {
                new PlannerFighter(Actor, Start, false, 100, 100),
                new PlannerFighter(Enemy, target < 0 ? Entry : target, true, 100, 100)
            }, new HashSet<int>(), portals ?? Array.Empty<C.Portal>(),
            KnownSpellGrades: grades);

    private static E.ObservedDecisionFacts Facts(int breed, params SpellCastDescriptor[] spells)
    {
        var learned = spells.ToDictionary(s => s.SpellId, s => s.Grade);
        return new E.ObservedDecisionFacts(Frame(breed), Board(breed, learned),
            C.VerifiedClassClientVersion, learned, spells);
    }

    private static E.ObservedDecisionSelection Select(E.ObservedDecisionFacts facts,
        E.Limits? limits = null)
        => E.PrepareObservedDecision(facts, enabled: true, executorLimits: limits);

    private static C.ComboObservation Event(int order, C.ComboEventKind kind,
        int spell = 0, int cell = -1, int mark = 0, int action = 0,
        long source = Actor, long subject = Enemy, long correlation = Correlation)
        => new(order, Fight, Generation, 1, source, subject, kind,
            SpellId: spell, Grade: spell > 0 ? 1 : 0, ToCell: cell,
            MarkId: mark, ActionId: action, Correlation: correlation,
            ServerConfirmed: true);

    [Fact]
    public void Completed_Roublard_cast_is_pinned_to_one_order_and_one_correlation()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        var cast = Event(1, C.ComboEventKind.Cast, spell: Bomb.SpellId,
            cell: selected.Preview!.Cell);
        var executor = selected.Executor!;
        Assert.Equal(E.Phase.Confirmed, executor.Inspect(facts.Snapshot with
        {
            LastServerOrder = 1
        }, new[] { cast }).State);
        Assert.False(selected.CanDispatch);

        // Same order, same spell, new positive-looking correlation. This used
        // to retain Phase.Confirmed because the old cast was never pinned.
        Assert.Equal(E.Phase.Expired, executor.Inspect(facts.Snapshot with
        {
            LastServerOrder = 1
        }, new[] { cast with { Correlation = Correlation + 1 } }).State);
        Assert.False(executor.Inspect(facts.Snapshot with
        {
            LastServerOrder = 1
        }, new[] { cast }).CanDispatch);

        var second = Select(facts);
        Assert.True(second.Accepted, second.Reason);
        Assert.Equal(E.Phase.Confirmed, second.Executor!.Inspect(facts.Snapshot with
        {
            LastServerOrder = 1
        }, new[] { cast }).State);
        Assert.Equal(E.Phase.Expired, second.Executor.Inspect(facts.Snapshot with
        {
            LastServerOrder = 2
        }, new[] { cast with { Order = 2 } }).State);
    }

    private static (E.ObservedDecisionFacts Facts, E.ObservedDecisionSelection Selected,
        V.Snapshot After, C.ComboObservation Cast, C.ComboObservation Placed,
        C.ComboObservation Activated) EliotropeSequence()
    {
        var basePortals = new[] { new C.Portal(502, Exit, Actor) };
        var grades = new Dictionary<int, int> { [Portal.SpellId] = 1, [Ray.SpellId] = 1 };
        var before = Board(C.EliotropeBreedId, grades, Impact, basePortals);
        var facts = new E.ObservedDecisionFacts(Frame(C.EliotropeBreedId), before,
            C.VerifiedClassClientVersion, grades, new[] { Portal, Ray });
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(2, selected.PlannedActions);
        var entry = selected.Preview!.Cell;
        var after = before with
        {
            LastServerOrder = 3, Ap = 5,
            MarkedCells = new HashSet<int> { entry, Exit },
            Portals = new[] { new C.Portal(501, entry, Actor), basePortals[0] }
        };
        return (facts, selected, after,
            Event(1, C.ComboEventKind.Cast, spell: Portal.SpellId, cell: entry),
            Event(2, C.ComboEventKind.MarkPlaced, cell: entry,
                mark: 501, action: 1181),
            Event(3, C.ComboEventKind.MarkActivated, cell: entry, mark: 501));
    }

    [Fact]
    public void Eliotrope_stage_one_cannot_be_rewritten_by_matching_forged_later_evidence()
    {
        var journey = EliotropeSequence();
        var executor = journey.Selected.Executor!;
        var seen = new[] { journey.Cast, journey.Placed, journey.Activated };
        var review = executor.Inspect(journey.After, seen);
        Assert.Equal(E.Phase.Confirmed, review.State);
        Assert.Equal(1, review.ConfirmedStages);
        Assert.Equal(Ray.SpellId, review.AdvisoryAction!.SpellId);
        Assert.False(review.CanDispatch);

        // Even a consistent *replacement* of the three positive-looking
        // historical correlations cannot supersede an already admitted cast.
        var replaced = seen.Select(e => e with
        {
            Correlation = e.Correlation + 31
        }).ToArray();
        var expired = executor.Inspect(journey.After, replaced);
        Assert.Equal(E.Phase.Expired, expired.State);
        Assert.Null(expired.AdvisoryAction);
        Assert.False(expired.CanDispatch);
    }

    [Fact]
    public void Eliotrope_stage_two_is_frozen_to_final_ack_order_and_correlation()
    {
        var journey = EliotropeSequence();
        var executor = journey.Selected.Executor!;
        var source = new[] { journey.Cast, journey.Placed, journey.Activated };
        Assert.Equal(E.Phase.Confirmed, executor.Inspect(journey.After, source).State);
        var finish = Event(4, C.ComboEventKind.Cast, spell: Ray.SpellId,
            cell: journey.Selected.Executor.AdvisoryPlan.Actions[1].TargetCell,
            correlation: Correlation + 1);
        Assert.Equal(E.Phase.Confirmed, executor.Inspect(journey.After with
        {
            LastServerOrder = 4
        }, source.Append(finish).ToArray()).State);
        var wrong = executor.Inspect(journey.After with { LastServerOrder = 4 },
            source.Append(finish with { Correlation = Correlation + 2 }).ToArray());
        Assert.Equal(E.Phase.Expired, wrong.State);
        Assert.False(wrong.CanDispatch);
    }

    [Fact]
    public void Diagnostic_Elio_ingress_never_promotes_even_positive_looking_server_flags()
    {
        var journey = EliotropeSequence();
        var review = journey.Selected.Executor!.InspectDiagnostic(journey.After,
            new[] { journey.Cast, journey.Placed, journey.Activated });
        Assert.Equal(0, review.ConfirmedStages);
        Assert.Null(review.AdvisoryAction);
        Assert.False(review.CanDispatch);
        Assert.NotEqual(E.Phase.Confirmed, review.State);
    }

    [Fact]
    public void Xelor_Telefrag_positive_replay_revoked_by_late_target_or_cast_conflict()
    {
        var evidence = new C.ComboEvidence(TargetId: Enemy, SourceSpell: Swap,
            StateId: C.StateTelefrag, FirstCell: Start, SecondCell: Entry,
            TargetCell: Entry, SourceCorrelation: Correlation,
            TargetIsEnemy: true, ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: C.XelorBreedId,
            ObservedLearnedGrades: new Dictionary<int, int> { [Swap.SpellId] = 1 });
        var rows = new[]
        {
            Event(1, C.ComboEventKind.Cast, spell: Swap.SpellId, cell: Entry),
            Event(2, C.ComboEventKind.Move, cell: Entry, subject: Actor)
                with { FromCell = Start },
            Event(3, C.ComboEventKind.Move, cell: Start, subject: Enemy)
                with { FromCell = Entry },
            Event(4, C.ComboEventKind.StateAdded, subject: Enemy)
                with { StateId = C.StateTelefrag }
        };
        var frame = Frame(C.XelorBreedId);
        var valid = C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence, rows);
        Assert.True(valid.Complete, valid.Reason);
        Assert.Equal(4, valid.ConfirmedStages);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence,
            rows.Append(rows[0] with { Order = 5, Source = Enemy }).ToArray()).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence,
            rows.Append(rows[2] with { Order = 5 }).ToArray()).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag, frame, evidence,
            rows.Append(rows[3] with { Order = 5, Source = Enemy }).ToArray()).Complete);
    }

    [Fact]
    public void Eliotrope_historical_effect_cannot_change_owner_after_proof_completion()
    {
        var evidence = new C.ComboEvidence(TargetId: Enemy,
            SourceSpell: Portal, FollowupSpell: Ray,
            FirstMarkId: 501, SecondMarkId: 502,
            FirstCell: Entry, SecondCell: Exit, TargetCell: Impact,
            CasterCell: Start,
            Portals: new[] { new C.Portal(501, Entry, Actor),
                new C.Portal(502, Exit, Actor) },
            EffectActionId: 99, FollowupCorrelation: Correlation,
            ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: C.EliotropeBreedId,
            FollowupBreedId: C.EliotropeBreedId,
            ObservedLearnedGrades: new Dictionary<int, int>
            {
                [Portal.SpellId] = 1, [Ray.SpellId] = 1
            });
        var frame = Frame(C.EliotropeBreedId);
        var rows = new[]
        {
            Event(1, C.ComboEventKind.MarkPlaced, cell: Entry,
                mark: 501, action: 1181),
            Event(2, C.ComboEventKind.MarkPlaced, cell: Exit,
                mark: 502, action: 1181),
            Event(3, C.ComboEventKind.MarkActivated, mark: 501, cell: Entry),
            Event(4, C.ComboEventKind.MarkActivated, mark: 502, cell: Exit),
            Event(5, C.ComboEventKind.Cast, spell: Ray.SpellId, cell: Entry),
            Event(6, C.ComboEventKind.Effect, spell: Ray.SpellId, action: 99)
        };
        var valid = C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
            frame, evidence, rows);
        Assert.True(valid.Complete, valid.Reason);
        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 64; i++)
            Assert.True(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
                frame, evidence, rows).Complete);
        watch.Stop();
        long replayBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        _output.WriteLine($"V14 offline 64 VerifyObservedCombo replays: "
            + $"{replayBytes} bytes, {watch.ElapsedTicks} Stopwatch ticks (diagnostic only)");
        Assert.InRange(replayBytes, 0, 32L * 1024 * 1024);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect, frame,
            evidence, rows.Append(rows[5] with { Order = 7, Source = Enemy })
                .ToArray()).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect, frame,
            evidence, rows.Append(rows[3] with { Order = 7, Source = Enemy })
                .ToArray()).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect, frame,
            evidence, rows.Append(rows[3] with { Order = 7 })
                .ToArray()).Complete);
    }

    [Theory]
    [InlineData(C.FecaBreedId, C.ComboKind.FecaGlyph)]
    [InlineData(C.EcaflipBreedId, C.ComboKind.EcaflipCard)]
    [InlineData(C.SacrierBreedId, C.ComboKind.SacrierSuffering)]
    public void Unverified_class_spell_or_state_never_creates_a_positive_advisory(
        int breed, C.ComboKind kind)
    {
        // A grade for a different class cannot identify glyph, card or
        // suffering mechanics. Never invent 3.7.13.17 spell/state IDs.
        var facts = Facts(breed, Bomb);
        var selected = Select(facts);
        Assert.False(selected.Accepted);
        Assert.Null(selected.Executor);
        Assert.False(selected.CanDispatch);
        var spoof = new C.ComboEvidence(TargetId: Enemy, SourceSpell: Bomb,
            ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: breed, FollowupBreedId: breed,
            ObservedLearnedGrades: new Dictionary<int, int> { [Bomb.SpellId] = 1 });
        Assert.False(C.VerifyObservedCombo(kind, Frame(breed), spoof,
            new[] { Event(1, C.ComboEventKind.Cast, spell: Bomb.SpellId,
                cell: Entry) }).Complete);
    }

    [Fact]
    public void Null_board_entries_expire_instead_of_trusting_incomplete_observation()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var invalid = facts.Snapshot with
        {
            Fighters = new PlannerFighter[]
            {
                null!, new PlannerFighter(Enemy, Entry, true, 100, 100)
            }
        };
        Assert.False(Select(facts with { Snapshot = invalid }).Accepted);
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        var expired = selected.Executor!.Inspect(invalid,
            Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Expired, expired.State);
        Assert.False(expired.CanDispatch);
    }

    [Fact]
    public void Bounded_local_review_measurement_is_reproducible_without_timing_claims()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var limits = new E.Limits(MaxReviews: 128,
            MaxObservations: 128, MaxOrderAdvance: 128);
        var selected = Select(facts, limits);
        Assert.True(selected.Accepted, selected.Reason);
        var executor = selected.Executor!;
        var empty = Array.Empty<C.ComboObservation>();
        long startBytes = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (int iteration = 0; iteration < 128; iteration++)
        {
            var review = executor.Inspect(facts.Snapshot, empty);
            Assert.Equal(E.Phase.Advisory, review.State);
            Assert.False(review.CanDispatch);
        }
        watch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - startBytes;
        _output.WriteLine($"V14 offline 128 Inspect reviews: {allocated} bytes; "
            + $"{watch.ElapsedTicks} Stopwatch ticks (diagnostic only)");
        // Generous allocation ceiling catches an accidentally unbounded loop
        // without making scheduler-dependent wall time a pass/fail assertion.
        Assert.InRange(allocated, 0, 64L * 1024 * 1024);
        Assert.Equal(E.Phase.Expired, executor.Inspect(facts.Snapshot, empty).State);
    }
}
