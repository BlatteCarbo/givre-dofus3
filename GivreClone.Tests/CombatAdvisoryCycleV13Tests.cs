using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;
using C = GivreClone.App.Services.Combat.ClassMechanics;
using E = GivreClone.App.Services.Combat.VerifiedTacticalExecutor;
using V = GivreClone.App.Services.Combat.VerifiedTacticalSequence;

namespace GivreClone.Tests;

/// <summary>
/// Offline historical replay through production PrepareObservedDecision,
/// Inspect/InspectDiagnostic and VerifyObservedCombo. Only class spell/effect
/// identities already pinned to DOFUS 3.7.13.17 are used. Synthetic actor,
/// mark and correlation numbers make TEST EVENTS, never real server receipts.
/// No assertion here establishes a live transport-generation-bound ACK.
/// </summary>
public sealed class CombatAdvisoryCycleV13Tests
{
    private const long Actor = 77, Enemy = -9, Fight = 1313;
    private const int Generation = 7;
    private const long Correlation = 731;
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
    private static readonly SpellCastDescriptor Explosion = Spell(13455, 99,
        SpellEffectKind.Damage, 4, mask: "A");
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

    private static E.ObservedDecisionFacts Facts(int breed,
        params SpellCastDescriptor[] descriptors)
    {
        var grades = descriptors.ToDictionary(s => s.SpellId, s => s.Grade);
        return new E.ObservedDecisionFacts(Frame(breed),
            Board(breed, grades), C.VerifiedClassClientVersion, grades, descriptors);
    }

    private static E.ObservedDecisionSelection Select(E.ObservedDecisionFacts facts,
        E.Limits? limits = null)
        => E.PrepareObservedDecision(facts, executorLimits: limits, enabled: true);

    private static C.ComboObservation Event(long order, C.ComboEventKind kind,
        long source = Actor, long subject = Enemy, int spell = 0, int cell = -1,
        int mark = 0, int action = 0, int state = 0, long correlation = Correlation,
        bool confirmed = true)
        => new(order, Fight, Generation, 1, source, subject, kind,
            SpellId: spell, Grade: spell > 0 ? 1 : 0,
            ActionId: action, StateId: state, MarkId: mark, ToCell: cell,
            Correlation: correlation, ServerConfirmed: confirmed);

    [Theory]
    [InlineData(C.RogueBreedId, C.ComboKind.RogueWall)]
    [InlineData(C.EliotropeBreedId, C.ComboKind.EliotropeRedirect)]
    [InlineData(C.XelorBreedId, C.ComboKind.XelorTelefrag)]
    public void Pinned_class_decision_expires_after_session_or_turn_change(
        int breed, C.ComboKind expected)
    {
        var source = breed == C.RogueBreedId ? Bomb
            : breed == C.EliotropeBreedId ? Portal : Swap;
        var facts = Facts(breed, source);
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(expected, selected.Kind);
        Assert.Equal(E.Phase.Advisory, selected.Executor!.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).State);
        Assert.False(selected.CanDispatch);

        foreach (var invalid in new[]
        {
            facts.Snapshot with { Generation = Generation + 1 },
            facts.Snapshot with { FightId = Fight + 1 },
            facts.Snapshot with { MapGeneration = 20 },
            facts.Snapshot with { Turn = 2 },
            facts.Snapshot with { ActorId = Actor + 1 }
        })
        {
            var next = Select(facts);
            Assert.True(next.Accepted, next.Reason);
            Assert.Equal(E.Phase.Expired, next.Executor!.Inspect(invalid,
                Array.Empty<C.ComboObservation>()).State);
            Assert.Equal(E.Phase.Expired, next.Executor.Inspect(facts.Snapshot,
                Array.Empty<C.ComboObservation>()).State);
            Assert.False(next.Executor.Inspect(facts.Snapshot,
                Array.Empty<C.ComboObservation>()).CanDispatch);
        }
    }

    [Fact]
    public void Completed_roublard_source_cast_cannot_reconfirm_after_a_repeat_or_replacement()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        var first = Event(1, C.ComboEventKind.Cast, spell: Bomb.SpellId,
            cell: selected.Preview!.Cell);
        var complete = selected.Executor!.Inspect(facts.Snapshot with
        {
            LastServerOrder = 1
        }, new[] { first });
        Assert.Equal(E.Phase.Confirmed, complete.State);
        Assert.Equal(1, complete.ConfirmedStages);
        Assert.False(complete.CanDispatch);

        var repeated = selected.Executor.Inspect(facts.Snapshot with
        {
            LastServerOrder = 2
        }, new[] { first, first with { Order = 2, Correlation = Correlation + 1 } });
        Assert.Equal(E.Phase.Expired, repeated.State);
        Assert.Null(repeated.AdvisoryAction);
        Assert.False(repeated.CanDispatch);

        var changed = Select(facts);
        Assert.True(changed.Accepted, changed.Reason);
        Assert.Equal(E.Phase.Confirmed, changed.Executor!.Inspect(facts.Snapshot with
        {
            LastServerOrder = 1
        }, new[] { first }).State);
        var replaced = changed.Executor.Inspect(facts.Snapshot with
        {
            LastServerOrder = 1
        }, new[] { first with { Source = Enemy } });
        Assert.Equal(E.Phase.Expired, replaced.State);
    }

    [Fact]
    public void Eliotrope_plan_binds_new_portal_actor_cell_mark_and_unique_cast()
    {
        var initialPortals = new[] { new C.Portal(502, Exit, Actor) };
        var grades = new Dictionary<int, int> { [Portal.SpellId] = 1, [Ray.SpellId] = 1 };
        var initial = Board(C.EliotropeBreedId, grades, Impact, initialPortals);
        var facts = new E.ObservedDecisionFacts(Frame(C.EliotropeBreedId), initial,
            C.VerifiedClassClientVersion, grades, new[] { Portal, Ray });
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(2, selected.PlannedActions);
        Assert.Equal(Portal.SpellId, selected.Preview!.SpellId);
        var entry = selected.Preview.Cell;
        var cast = Event(1, C.ComboEventKind.Cast, spell: Portal.SpellId,
            cell: entry);
        var placed = Event(2, C.ComboEventKind.MarkPlaced,
            mark: 501, cell: entry, action: 1181);
        var active = Event(3, C.ComboEventKind.MarkActivated,
            mark: 501, cell: entry);
        var after = initial with
        {
            Ap = 5, LastServerOrder = 3,
            MarkedCells = new HashSet<int> { entry, Exit },
            Portals = new[] { new C.Portal(501, entry, Actor), initialPortals[0] }
        };
        Assert.Equal(E.Phase.Advisory, selected.Executor!.Inspect(initial,
            Array.Empty<C.ComboObservation>()).State);
        var first = selected.Executor.Inspect(after with { LastServerOrder = 1 },
            new[] { cast });
        Assert.Equal(E.Phase.Pending, first.State);
        Assert.False(first.CanDispatch);
        var proven = selected.Executor.Inspect(after,
            new[] { cast, placed, active });
        Assert.Equal(E.Phase.Confirmed, proven.State);
        Assert.Equal(1, proven.ConfirmedStages);
        Assert.Equal(Ray.SpellId, proven.AdvisoryAction!.SpellId);
        Assert.False(proven.CanDispatch);

        var second = Event(4, C.ComboEventKind.Cast,
            spell: Ray.SpellId, cell: entry, correlation: Correlation + 1);
        var finished = selected.Executor.Inspect(after with
        {
            LastServerOrder = 4
        }, new[] { cast, placed, active, second });
        Assert.Equal(E.Phase.Confirmed, finished.State);
        Assert.Equal(2, finished.ConfirmedStages);
        Assert.False(finished.CanDispatch);
        // Historical follow-up cannot survive an author substitution even
        // if earlier portal/impact proof is unchanged.
        Assert.Equal(E.Phase.Expired, selected.Executor.Inspect(after with
        {
            LastServerOrder = 4
        }, new[] { cast, placed, active,
            second with { Source = Enemy } }).State);

        foreach (var changed in new[]
        {
            new[] { new C.Portal(501, Entry, Enemy), initialPortals[0] },
            new[] { new C.Portal(501, Impact, Actor), initialPortals[0] },
            new[] { new C.Portal(501, entry, Actor, false), initialPortals[0] },
            new[] { new C.Portal(501, entry, Actor),
                initialPortals[0] with { Active = false } }
        })
        {
            var runner = Select(facts);
            Assert.True(runner.Accepted, runner.Reason);
            Assert.Equal(E.Phase.Expired, runner.Executor!.Inspect(after with
            {
                Portals = changed
            }, new[] { cast, placed, active }).State);
        }

        foreach (var conflicting in new[]
        {
            placed with { Source = Enemy },
            placed with { ToCell = Impact },
            placed with { ActionId = 0 },
            placed with { Order = 1 },
            placed with { Correlation = Correlation + 1 }
        })
        {
            var runner = Select(facts);
            Assert.True(runner.Accepted, runner.Reason);
            var res = runner.Executor!.Inspect(after,
                new[] { cast, conflicting, active });
            Assert.Equal(E.Phase.Expired, res.State);
            Assert.False(res.CanDispatch);
        }
    }

    [Fact]
    public void Real_diagnostic_entry_remains_pending_even_with_fake_positive_cast()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        var forged = Event(1, C.ComboEventKind.Cast,
            spell: Bomb.SpellId, cell: selected.Preview!.Cell);
        var review = selected.Executor!.InspectDiagnostic(facts.Snapshot with
        {
            LastServerOrder = 1
        }, new[] { forged });
        Assert.Equal(E.Phase.Pending, review.State);
        Assert.Equal(0, review.ConfirmedStages);
        Assert.False(review.CanDispatch);
    }

    [Fact]
    public void Xelor_observed_permutation_Telefrag_remains_historical_and_bounded()
    {
        // Pinned spell 13285/action8; state 251 is an existing verified class
        // constant. Two independent server moves and a state grant are needed.
        var target = Entry;
        var evidence = new C.ComboEvidence(TargetId: Enemy, SourceSpell: Swap,
            StateId: C.StateTelefrag, FirstCell: Start, SecondCell: target,
            TargetCell: target, SourceCorrelation: Correlation,
            TargetIsEnemy: true, ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: C.XelorBreedId,
            ObservedLearnedGrades: new Dictionary<int, int> { [Swap.SpellId] = 1 });
        var cast = Event(1, C.ComboEventKind.Cast, spell: Swap.SpellId,
            cell: target);
        var mover = Event(2, C.ComboEventKind.Move, subject: Actor,
            cell: target) with { FromCell = Start };
        var movedTarget = Event(3, C.ComboEventKind.Move, subject: Enemy,
            cell: Start) with { FromCell = target };
        var state = Event(4, C.ComboEventKind.StateAdded,
            subject: Enemy, state: C.StateTelefrag);
        var observations = new[] { cast, mover, movedTarget, state };
        var positive = C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            Frame(C.XelorBreedId), evidence, observations);
        Assert.True(positive.Complete, positive.Reason);
        Assert.Equal(4, positive.ConfirmedStages);

        var repeated = observations.Append(cast with { Order = 5 }).ToArray();
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            Frame(C.XelorBreedId), evidence, repeated).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            Frame(C.XelorBreedId), evidence, new[] { cast, mover,
                movedTarget with { Source = Enemy }, state }).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            Frame(C.XelorBreedId), evidence, observations.Select(e => e.Order == 4
                ? e with { StateId = 0 } : e).ToArray()).Complete);
    }

    [Fact]
    public void Eliotrope_mark_activation_cell_and_duplicate_ID_revoke_historical_proof()
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
        var events = new[]
        {
            Event(1, C.ComboEventKind.MarkPlaced, mark: 501,
                cell: Entry, action: 1181),
            Event(2, C.ComboEventKind.MarkPlaced, mark: 502,
                cell: Exit, action: 1181),
            Event(3, C.ComboEventKind.MarkActivated, mark: 501, cell: Entry),
            Event(4, C.ComboEventKind.MarkActivated, mark: 502, cell: Exit),
            Event(5, C.ComboEventKind.Cast, spell: Ray.SpellId, cell: Entry),
            Event(6, C.ComboEventKind.Effect, spell: Ray.SpellId,
                action: 99, subject: Enemy)
        };
        var proof = C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
            Frame(C.EliotropeBreedId), evidence, events);
        Assert.True(proof.Complete, proof.Reason);
        Assert.Equal(6, proof.ConfirmedStages);

        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
            Frame(C.EliotropeBreedId), evidence,
            events.Select((e, i) => i == 2
                ? e with { ToCell = Start } : e).ToArray()).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
            Frame(C.EliotropeBreedId), evidence,
            events.Append(events[0] with { Order = 7 }).ToArray()).Complete);
    }

    [Theory]
    [InlineData(C.FecaBreedId, C.ComboKind.FecaGlyph)]
    [InlineData(C.EcaflipBreedId, C.ComboKind.EcaflipCard)]
    [InlineData(C.SacrierBreedId, C.ComboKind.SacrierSuffering)]
    public void Unjoined_class_catalog_cannot_infer_glyph_card_or_suffering(
        int breed, C.ComboKind kind)
    {
        // Bomb grade from an unrelated class proves neither Feca glyph IDs,
        // Ecaflip card-state IDs nor Sacrier suffering IDs. Do not invent a
        // 3.7.13.17 spell mapping just to make a positive path in tests.
        var facts = Facts(breed, Bomb);
        var selection = Select(facts);
        Assert.False(selection.Accepted);
        Assert.False(selection.CanDispatch);
        Assert.Null(selection.Executor);

        var evidence = new C.ComboEvidence(TargetId: Enemy,
            SourceSpell: Bomb, FollowupSpell: Explosion,
            ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: breed, FollowupBreedId: breed,
            ObservedLearnedGrades: new Dictionary<int, int> { [Bomb.SpellId] = 1 });
        var fabricated = new[]
        {
            Event(1, C.ComboEventKind.Cast, spell: Bomb.SpellId, cell: Entry),
            Event(2, C.ComboEventKind.MarkPlaced, mark: 501,
                cell: Entry, action: 1181)
        };
        Assert.False(C.VerifyObservedCombo(kind, Frame(breed),
            evidence, fabricated).Complete);
    }

    [Fact]
    public void VerifyObservedCombo_refuses_unbounded_turns_orders_and_observation_count()
    {
        var evidence = new C.ComboEvidence(TargetId: Enemy, SourceSpell: Swap,
            StateId: C.StateTelefrag, FirstCell: Start, SecondCell: Entry,
            TargetCell: Entry, SourceCorrelation: Correlation,
            TargetIsEnemy: true, ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: C.XelorBreedId,
            ObservedLearnedGrades: new Dictionary<int, int> { [Swap.SpellId] = 1 });
        var events = new[]
        {
            Event(1, C.ComboEventKind.Cast, spell: Swap.SpellId, cell: Entry),
            Event(2, C.ComboEventKind.Move, subject: Actor,
                cell: Entry) with { FromCell = Start },
            Event(3, C.ComboEventKind.Move, subject: Enemy,
                cell: Start) with { FromCell = Entry },
            Event(4, C.ComboEventKind.StateAdded, subject: Enemy,
                state: C.StateTelefrag)
        };
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            Frame(C.XelorBreedId, lastTurn: 100), evidence, events).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            Frame(C.XelorBreedId), evidence,
            events.Select((e, i) => i == 3
                ? e with { Order = 100_000 } : e).ToArray()).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            Frame(C.XelorBreedId), evidence,
            Enumerable.Range(1, 513).Select(i => Event(i,
                C.ComboEventKind.HpChanged, confirmed: false)).ToArray()).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            Frame(C.XelorBreedId), evidence,
            new C.ComboObservation[] { null! }).Complete);
    }

    [Fact]
    public void Bounded_executor_reviews_reject_gapped_or_stale_event_high_water()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var selected = Select(facts, new E.Limits(MaxReviews: 8,
            MaxObservations: 8, MaxOrderAdvance: 8));
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(E.Phase.Expired, selected.Executor!.Inspect(facts.Snapshot with
        {
            LastServerOrder = 9
        }, Array.Empty<C.ComboObservation>()).State);
        var noEvent = Select(facts);
        Assert.True(noEvent.Accepted, noEvent.Reason);
        Assert.Equal(E.Phase.Expired, noEvent.Executor!.Inspect(facts.Snapshot with
        {
            LastServerOrder = 1
        }, Array.Empty<C.ComboObservation>()).State);
    }
}
