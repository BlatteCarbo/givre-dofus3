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
/// Direct offline tests of the production admission and historical observation
/// APIs. Spell identities are limited to the pinned 3.7.13.17 catalog. The
/// local actor, mark and correlation values are synthetic test data and never
/// a claim of authenticated inbound transport or a dispatch authorization.
/// </summary>
public sealed class CombatDecisionCausalityV15Tests
{
    private readonly ITestOutputHelper _output;
    public CombatDecisionCausalityV15Tests(ITestOutputHelper output) => _output = output;

    private const long Actor = 77, Enemy = -9, Fight = 9771, Correlation = 702;
    private const int Generation = 8;
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

    private static C.ComboFrame Frame(int breed, int lastTurn = 1)
        => new(Fight, Generation, 1, lastTurn, Actor, breed);

    private static V.Snapshot Board(int breed, IReadOnlyDictionary<int, int> grades,
        int enemyCell = -1, IReadOnlyList<C.Portal>? portals = null)
        => new(Fight, Generation, 19, 1, Actor, breed, Start, 6, 3, 0,
            false, Cells,
            new[]
            {
                new PlannerFighter(Actor, Start, false, 100, 100),
                new PlannerFighter(Enemy, enemyCell < 0 ? Entry : enemyCell,
                    true, 100, 100)
            }, new HashSet<int>(), portals ?? Array.Empty<C.Portal>(),
            KnownSpellGrades: grades);

    private static E.ObservedDecisionFacts Facts(int breed, params SpellCastDescriptor[] spells)
    {
        var learned = spells.ToDictionary(s => s.SpellId, s => s.Grade);
        return new E.ObservedDecisionFacts(Frame(breed), Board(breed, learned),
            C.VerifiedClassClientVersion, learned, spells);
    }

    private static C.ComboObservation Event(int order, C.ComboEventKind kind,
        int spell = 0, int cell = -1, int mark = 0, int action = 0,
        long source = Actor, long subject = Enemy, long correlation = Correlation,
        int turn = 1, bool confirmed = true)
        => new(order, Fight, Generation, turn, source, subject, kind,
            SpellId: spell, Grade: spell > 0 ? 1 : 0,
            ToCell: cell, MarkId: mark, ActionId: action,
            Correlation: correlation, ServerConfirmed: confirmed);

    [Theory]
    [InlineData(C.RogueBreedId, C.ComboKind.RogueWall)]
    [InlineData(C.EliotropeBreedId, C.ComboKind.EliotropeRedirect)]
    [InlineData(C.XelorBreedId, C.ComboKind.XelorTelefrag)]
    public void Pinned_class_admission_requires_same_turn_actor_and_version(int breed,
        C.ComboKind kind)
    {
        var source = breed == C.RogueBreedId ? Bomb
            : breed == C.EliotropeBreedId ? Portal : Swap;
        var facts = Facts(breed, source);
        var selected = E.PrepareObservedDecision(facts, enabled: true);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(kind, selected.Kind);
        Assert.False(selected.CanDispatch);
        Assert.Equal(E.Phase.Advisory, selected.Executor!.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).State);

        Assert.False(E.PrepareObservedDecision(facts with
        {
            ClientVersion = "unverified"
        }, enabled: true).Accepted);
        Assert.False(E.PrepareObservedDecision(facts with
        {
            Frame = facts.Frame with { FirstTurn = facts.Frame.FirstTurn + 1 }
        }, enabled: true).Accepted);
        Assert.Equal(E.Phase.Expired, selected.Executor.Inspect(facts.Snapshot with
        {
            Generation = Generation + 1
        }, Array.Empty<C.ComboObservation>()).State);
        Assert.False(selected.Executor.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).CanDispatch);
    }

    [Fact]
    public void Opt_out_after_attributed_Roublard_receipt_revokes_historical_sequence()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var selected = E.PrepareObservedDecision(facts, enabled: true);
        Assert.True(selected.Accepted, selected.Reason);
        var cast = Event(1, C.ComboEventKind.Cast,
            spell: Bomb.SpellId, cell: selected.Preview!.Cell);
        var reviewed = selected.Executor!.Inspect(facts.Snapshot with
        {
            LastServerOrder = 1
        }, new[] { cast });
        Assert.Equal(E.Phase.Confirmed, reviewed.State);
        Assert.Null(reviewed.AdvisoryAction);
        Assert.False(reviewed.CanDispatch);

        selected.Executor.SetEnabled(false);
        selected.Executor.SetEnabled(true);
        var stale = selected.Executor.Inspect(facts.Snapshot with
        {
            LastServerOrder = 1
        }, new[] { cast });
        Assert.Equal(E.Phase.Expired, stale.State);
        Assert.False(stale.CanDispatch);

        // A fresh observed decision has no inherited ACK or class effect.
        var fresh = E.PrepareObservedDecision(facts, enabled: true);
        Assert.True(fresh.Accepted, fresh.Reason);
        Assert.Equal(E.Phase.Advisory, fresh.Executor!.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).State);
    }

    [Fact]
    public void Uncorrelated_cast_diagnostic_cannot_be_upgraded_by_opt_in()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var selected = E.PrepareObservedDecision(facts, enabled: true);
        Assert.True(selected.Accepted, selected.Reason);
        var alleged = Event(1, C.ComboEventKind.Cast, spell: Bomb.SpellId,
            cell: selected.Preview!.Cell);
        var status = selected.Executor!.InspectDiagnostic(facts.Snapshot with
        {
            LastServerOrder = 1
        }, new[] { alleged });
        Assert.Equal(0, status.ConfirmedStages);
        Assert.Null(status.AdvisoryAction);
        Assert.NotEqual(E.Phase.Confirmed, status.State);
        Assert.False(status.CanDispatch);
    }

    [Fact]
    public void Duplicated_observed_fighter_cell_cannot_retain_a_selected_advice()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var selected = E.PrepareObservedDecision(facts, enabled: true);
        Assert.True(selected.Accepted, selected.Reason);
        var duplicateOccupancy = facts.Snapshot with
        {
            Fighters = new[]
            {
                new PlannerFighter(Actor, Start, false, 100, 100),
                new PlannerFighter(Enemy, Start, true, 100, 100)
            }
        };
        var invalid = selected.Executor!.Inspect(duplicateOccupancy,
            Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Expired, invalid.State);
        Assert.False(invalid.CanDispatch);
    }

    private static (E.ObservedDecisionSelection Selected, V.Snapshot After,
        C.ComboObservation[] History) EliotropeAdvisory()
    {
        var known = new[] { new C.Portal(502, Exit, Actor) };
        var grades = new Dictionary<int, int> { [Portal.SpellId] = 1, [Ray.SpellId] = 1 };
        var before = Board(C.EliotropeBreedId, grades, Impact, known);
        var facts = new E.ObservedDecisionFacts(Frame(C.EliotropeBreedId), before,
            C.VerifiedClassClientVersion, grades, new[] { Portal, Ray });
        var selected = E.PrepareObservedDecision(facts, enabled: true);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(2, selected.PlannedActions);
        var entry = selected.Preview!.Cell;
        var after = before with
        {
            Ap = 5, LastServerOrder = 3,
            MarkedCells = new HashSet<int> { entry, Exit },
            Portals = new[] { new C.Portal(501, entry, Actor), known[0] }
        };
        return (selected, after, new[]
        {
            Event(1, C.ComboEventKind.Cast, spell: Portal.SpellId, cell: entry),
            Event(2, C.ComboEventKind.MarkPlaced,
                mark: 501, cell: entry, action: 1181),
            Event(3, C.ComboEventKind.MarkActivated, mark: 501, cell: entry)
        });
    }

    [Fact]
    public void Eliotrope_two_action_replay_blocks_wrong_source_mark_and_missing_ACK()
    {
        var journey = EliotropeAdvisory();
        var admitted = journey.Selected.Executor!.Inspect(journey.After, journey.History);
        Assert.Equal(E.Phase.Confirmed, admitted.State);
        Assert.Equal(1, admitted.ConfirmedStages);
        Assert.Equal(Ray.SpellId, admitted.AdvisoryAction!.SpellId);
        Assert.False(admitted.CanDispatch);

        var second = Event(4, C.ComboEventKind.Cast, spell: Ray.SpellId,
            cell: journey.Selected.Executor.AdvisoryPlan.Actions[1].TargetCell,
            correlation: Correlation + 1, confirmed: false);
        var pending = journey.Selected.Executor.Inspect(journey.After with
        {
            LastServerOrder = 4
        }, journey.History.Append(second).ToArray());
        Assert.Equal(E.Phase.Pending, pending.State);
        Assert.False(pending.CanDispatch);

        // The second action is a replay-only confirmation, never a send.
        var complete = journey.Selected.Executor.Inspect(journey.After with
        {
            LastServerOrder = 4
        }, journey.History.Append(second with { ServerConfirmed = true }).ToArray());
        Assert.Equal(E.Phase.Confirmed, complete.State);
        Assert.Equal(2, complete.ConfirmedStages);
        Assert.False(complete.CanDispatch);
        Assert.Equal(E.Phase.Expired, journey.Selected.Executor.Inspect(journey.After with
        {
            LastServerOrder = 4
        }, journey.History.Append(second with
        {
            ServerConfirmed = true, Source = Enemy
        }).ToArray()).State);
    }

    private static (C.ComboFrame Frame, C.ComboEvidence Evidence,
        C.ComboObservation[] History) XelorHistorical(int lastTurn = 12)
    {
        var evidence = new C.ComboEvidence(TargetId: Enemy, SourceSpell: Swap,
            StateId: C.StateTelefrag, FirstCell: Start, SecondCell: Entry,
            TargetCell: Entry, SourceCorrelation: Correlation,
            TargetIsEnemy: true, ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: C.XelorBreedId,
            ObservedLearnedGrades: new Dictionary<int, int> { [Swap.SpellId] = 1 });
        return (Frame(C.XelorBreedId, lastTurn), evidence, new[]
        {
            Event(1, C.ComboEventKind.Cast, spell: Swap.SpellId, cell: Entry),
            Event(2, C.ComboEventKind.Move, subject: Actor,
                cell: Entry) with { FromCell = Start },
            Event(3, C.ComboEventKind.Move, subject: Enemy,
                cell: Start) with { FromCell = Entry },
            Event(4, C.ComboEventKind.StateAdded,
                subject: Enemy) with { StateId = C.StateTelefrag }
        });
    }

    [Fact]
    public void Xelor_multiturn_evidence_requires_bounded_stage_gaps_and_actual_target_moves()
    {
        var (frame, evidence, history) = XelorHistorical();
        var positive = C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            frame, evidence, history);
        Assert.True(positive.Complete, positive.Reason);
        Assert.Equal(4, positive.ConfirmedStages);

        var turnTwo = history.Select((e, i) => i == 0 ? e : e with { Turn = 2 }).ToArray();
        Assert.True(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            frame, evidence, turnTwo).Complete);

        var tooLate = history.Select((e, i) => i == 0 ? e : e with { Turn = 11 }).ToArray();
        var stale = C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            frame, evidence, tooLate);
        Assert.False(stale.Complete);
        Assert.Contains("turn", stale.Reason, StringComparison.OrdinalIgnoreCase);

        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            frame, evidence, history.Select((e, i) => i == 2
                ? e with { FromCell = Start } : e).ToArray()).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            frame, evidence, history.Select((e, i) => i == 3
                ? e with { Source = Enemy } : e).ToArray()).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            frame, evidence, history.Select((e, i) => i == 0
                ? e with { ServerConfirmed = false } : e).ToArray()).Complete);
    }

    [Theory]
    [InlineData(C.FecaBreedId, C.ComboKind.FecaGlyph)]
    [InlineData(C.EcaflipBreedId, C.ComboKind.EcaflipCard)]
    [InlineData(C.SacrierBreedId, C.ComboKind.SacrierSuffering)]
    public void Missing_verified_class_grade_and_effect_chain_cannot_produce_a_combo(
        int breed, C.ComboKind kind)
    {
        // Pinned N01 lacks class-specific grade/effect joins for these classes.
        // Do not fabricate a glyph spell, card-state ID or suffering source.
        var facts = Facts(breed, Bomb);
        var admission = E.PrepareObservedDecision(facts, enabled: true);
        Assert.False(admission.Accepted);
        Assert.False(admission.CanDispatch);
        var evidence = new C.ComboEvidence(TargetId: Enemy, SourceSpell: Bomb,
            ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: breed, FollowupBreedId: breed,
            ObservedLearnedGrades: new Dictionary<int, int> { [Bomb.SpellId] = 1 });
        Assert.False(C.VerifyObservedCombo(kind, Frame(breed), evidence,
            new[] { Event(1, C.ComboEventKind.Cast,
                spell: Bomb.SpellId, cell: Entry) }).Complete);
    }

    [Fact]
    public void Deterministic_offline_review_cost_and_expiration_do_not_depend_on_wall_time()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var selection = E.PrepareObservedDecision(facts, enabled: true,
            executorLimits: new E.Limits(MaxReviews: 128));
        Assert.True(selection.Accepted, selection.Reason);
        var historical = XelorHistorical();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (int index = 0; index < 128; index++)
        {
            var review = selection.Executor!.Inspect(facts.Snapshot,
                Array.Empty<C.ComboObservation>());
            Assert.Equal(E.Phase.Advisory, review.State);
            Assert.False(review.CanDispatch);
        }
        for (int index = 0; index < 64; index++)
        {
            var proof = C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
                historical.Frame, historical.Evidence, historical.History);
            Assert.True(proof.Complete, proof.Reason);
        }
        watch.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        _output.WriteLine($"V15 offline: 128 Inspect + 64 VerifyObservedCombo; "
            + $"thread allocations {bytes} bytes; Stopwatch ticks {watch.ElapsedTicks} (informative)");
        Assert.InRange(bytes, 0, 64L * 1024 * 1024);
        Assert.Equal(E.Phase.Expired, selection.Executor!.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).State);
    }
}
