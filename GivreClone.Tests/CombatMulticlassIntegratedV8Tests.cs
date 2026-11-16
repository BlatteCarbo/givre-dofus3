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
/// The real LaunchService admission path: PrepareObservedDecision -> Executor
/// -> Inspect/InspectDiagnostic, not a detached helper. Positive event rows
/// are synthetic replay fixtures, NEVER live authenticated receipts. Concrete
/// spell IDs 14574/14579, 13444 and 13285 are in the installed N01 3.7.13.17
/// class export. No mark, spell or state identity is inferred from a send.
/// </summary>
public sealed class CombatMulticlassIntegratedV8Tests
{
    private const long Actor = 77, Enemy = -9, Fight = 991;
    private static readonly int Start = Cell(10, 0);
    private static readonly int Entry = Cell(12, 0);
    private static readonly int Exit = Cell(10, 9);
    private static readonly int Impact = Cell(12, 9);
    private static readonly PlannerCell[] Cells = Enumerable.Range(0, MapPathfinder.CellCount)
        .Select(id => new PlannerCell(id, true, false, ImmutableArray<int>.Empty)).ToArray();

    private static int Cell(int x, int y) => MapPathfinder.CellFromMapPoint(x, y);
    private static SpellCastDescriptor Spell(int id, int action, SpellEffectKind kind,
        int ap, int range, int flags, string mask = "a,A", int bomb = 0)
        => new(id, 1, ap, 1, range, flags, false, 2, 2, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(action, kind, 0, 0, 0, 0,
                mask, 'P', 1, 0, DiceNum: bomb)));

    private static readonly SpellCastDescriptor Portal =
        Spell(14574, 1181, SpellEffectKind.Unknown, 1, 4, 12);
    private static readonly SpellCastDescriptor Ray =
        Spell(14579, 99, SpellEffectKind.Damage, 3, 5, 4);
    private static readonly SpellCastDescriptor Bomb =
        Spell(13444, 1008, SpellEffectKind.Summon, 2, 6, 12,
            bomb: C.VerifiedExplobombTemplate);
    private static readonly SpellCastDescriptor Swap =
        Spell(13285, 8, SpellEffectKind.Swap, 4, 2, 20, "A");
    private static readonly SpellCastDescriptor Freeze =
        Spell(13245, 96, SpellEffectKind.Damage, 2, 4, 1, "A");

    private static E.ObservedDecisionFacts Facts(int breed,
        SpellCastDescriptor[] spells, int enemyCell = -1,
        IReadOnlyList<C.Portal>? portals = null, IReadOnlySet<int>? marks = null)
    {
        var grades = spells.ToDictionary(s => s.SpellId, s => s.Grade);
        var board = new V.Snapshot(Fight, 5, 41, 1, Actor, breed, Start, 6, 3, 0,
            false, Cells, new[]
            {
                new PlannerFighter(Actor, Start, false, 100, 100),
                new PlannerFighter(Enemy, enemyCell < 0 ? Impact : enemyCell,
                    true, 100, 100)
            }, marks ?? new HashSet<int>(),
            portals ?? Array.Empty<C.Portal>(), KnownSpellGrades: grades);
        return new(new C.ComboFrame(Fight, 5, 1, 1, Actor, breed),
            board, C.VerifiedClassClientVersion, grades, spells);
    }

    private static (E.ObservedDecisionSelection Selection, E.ObservedDecisionFacts Facts)
        PortalPair()
    {
        var facts = Facts(C.EliotropeBreedId, new[] { Portal, Ray },
            portals: new[] { new C.Portal(502, Exit, Actor) },
            marks: new HashSet<int> { Exit });
        return (E.PrepareObservedDecision(facts, enabled: true), facts);
    }

    private static C.ComboObservation Event(long order, C.ComboEventKind kind,
        int spell = 0, int target = -1, int mark = 0, int action = 0,
        long correlation = 731, bool confirmed = true)
        => new(order, Fight, 5, 1, Actor, Enemy, kind,
            SpellId: spell, Grade: spell > 0 ? 1 : 0,
            ToCell: target, MarkId: mark, ActionId: action,
            Correlation: correlation, ServerConfirmed: confirmed);

    private static (C.ComboObservation Cast, C.ComboObservation Place,
        C.ComboObservation Activate) ProofRows()
        => (Event(1, C.ComboEventKind.Cast, 14574, Entry),
            Event(2, C.ComboEventKind.MarkPlaced, target: Entry,
                mark: 501, action: 1181),
            Event(3, C.ComboEventKind.MarkActivated, mark: 501));

    private static V.Snapshot AfterPortal(V.Snapshot initial, long order = 3)
        => initial with
        {
            Ap = 5, LastServerOrder = order,
            MarkedCells = new HashSet<int> { Entry, Exit },
            Portals = new[] { new C.Portal(501, Entry, Actor),
                new C.Portal(502, Exit, Actor) }
        };

    [Fact]
    public void Integrated_admission_chooses_two_action_portal_ray_and_blocks_dispatch()
    {
        var (selection, facts) = PortalPair();
        Assert.True(selection.Accepted, selection.Reason);
        Assert.Equal(C.ComboKind.EliotropeRedirect, selection.Kind);
        Assert.Equal(2, selection.PlannedActions);
        Assert.Equal(14574, selection.Preview!.SpellId);
        Assert.Equal(Entry, selection.Preview.Cell);
        Assert.Equal(14579, selection.Executor!.AdvisoryPlan.Actions[1].Spell.SpellId);
        Assert.Equal(Entry, selection.Executor.AdvisoryPlan.Actions[1].TargetCell);
        Assert.Equal(Impact, selection.Executor.AdvisoryPlan.Evidence.TargetCell);
        Assert.Equal(Enemy, selection.Executor.AdvisoryPlan.Evidence.TargetId);
        Assert.Contains("transport", selection.DispatchBlockedBy);
        Assert.False(selection.CanDispatch);
        var review = selection.Executor.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Advisory, review.State);
        Assert.Equal(0, review.ConfirmedStages);
        Assert.False(review.CanDispatch);
    }

    [Fact]
    public void Replay_must_observe_correlated_cast_mark_activation_impact_and_followup()
    {
        var (selection, facts) = PortalPair();
        Assert.True(selection.Accepted, selection.Reason);
        var (cast, placed, active) = ProofRows();
        var after = AfterPortal(facts.Snapshot);
        var before = selection.Executor!.Inspect(after with { LastServerOrder = 1 },
            new[] { cast });
        Assert.Equal(E.Phase.Pending, before.State);
        Assert.Equal(0, before.ConfirmedStages);
        Assert.Null(before.AdvisoryAction);
        var followup = selection.Executor.Inspect(after, new[] { cast, placed, active });
        Assert.Equal(E.Phase.Confirmed, followup.State);
        Assert.Equal(1, followup.ConfirmedStages);
        Assert.Equal(PlannerActionKind.Cast, followup.AdvisoryAction!.Kind);
        Assert.Equal(14579, followup.AdvisoryAction.SpellId);
        Assert.Equal(Entry, followup.AdvisoryAction.Cell);
        Assert.Contains("ACK", followup.DispatchBlockedBy);
        Assert.False(followup.CanDispatch);

        var last = Event(4, C.ComboEventKind.Cast, 14579, Entry, correlation: 732);
        var finished = selection.Executor.Inspect(after with { LastServerOrder = 4 },
            new[] { cast, placed, active, last });
        Assert.Equal(E.Phase.Confirmed, finished.State);
        Assert.Equal(2, finished.ConfirmedStages);
        Assert.Null(finished.AdvisoryAction);
        Assert.False(finished.CanDispatch);
    }

    [Fact]
    public void Linked_or_Sent_like_diagnostics_do_not_become_ACK_despite_positive_looking_rows()
    {
        var (selection, facts) = PortalPair();
        var (cast, placed, active) = ProofRows();
        var after = AfterPortal(facts.Snapshot);
        var diagnostic = selection.Executor!.InspectDiagnostic(after,
            new[] { cast with { Correlation = 9876 }, placed, active });
        // V9's portal provenance check expires a plan when diagnostic-only
        // marks pretend that a new portal appeared without a matching
        // transport-bound cast. This is stricter than the V8 Pending state:
        // neither state authorizes a cast, but Expired is now irreversible.
        Assert.Equal(E.Phase.Expired, diagnostic.State);
        Assert.Equal(0, diagnostic.ConfirmedStages);
        Assert.Null(diagnostic.AdvisoryAction);
        Assert.False(diagnostic.CanDispatch);
        var again = E.PrepareObservedDecision(facts, enabled: true);
        var sent = cast with { Correlation = 0, ServerConfirmed = false };
        var pending = again.Executor!.Inspect(after with { LastServerOrder = 1 },
            new[] { sent });
        // A sent-like unconfirmed cast coupled with an already changed
        // portal board also expires permanently under V9. It is not an ACK
        // and must never permit continuation or an automatic retry.
        Assert.Equal(E.Phase.Expired, pending.State);
        Assert.Equal(0, pending.ConfirmedStages);
        Assert.False(pending.CanDispatch);
    }

    [Fact]
    public void Conflicts_in_cast_grade_mark_correlation_order_and_generation_refuse_stage_two()
    {
        var (selection, facts) = PortalPair();
        Assert.True(selection.Accepted, selection.Reason);
        var (cast, placed, active) = ProofRows();
        var after = AfterPortal(facts.Snapshot);
        foreach (var rows in new[]
        {
            new[] { cast with { Correlation = 0 }, placed, active },
            new[] { cast with { Grade = 2 }, placed, active },
            new[] { cast, placed with { Correlation = 0 }, active },
            new[] { cast, placed, active with { Source = 500 } },
            new[] { cast, placed with { Generation = 7 }, active },
            new[] { cast, placed, active with { Order = 2 } }
        })
        {
            var runner = E.PrepareObservedDecision(facts, enabled: true);
            Assert.True(runner.Accepted, runner.Reason);
            var result = runner.Executor!.Inspect(after, rows);
            Assert.Equal(0, result.ConfirmedStages);
            Assert.Null(result.AdvisoryAction);
            Assert.False(result.CanDispatch);
        }
        var confirmed = E.PrepareObservedDecision(facts, enabled: true);
        Assert.Equal(E.Phase.Confirmed, confirmed.Executor!.Inspect(
            after, new[] { cast, placed, active }).State);
        var removed = Event(4, C.ComboEventKind.MarkDeactivated,
            mark: 501, correlation: 0);
        var stale = confirmed.Executor.Inspect(after with
        {
            LastServerOrder = 4,
            MarkedCells = new HashSet<int> { Exit },
            Portals = new[] { new C.Portal(501, Entry, Actor, false),
                new C.Portal(502, Exit, Actor) }
        }, new[] { cast, placed, active, removed });
        Assert.Equal(E.Phase.Expired, stale.State);
        Assert.False(stale.CanDispatch);
    }

    [Fact]
    public void Epoch_grades_resources_LOS_and_action_budget_are_revalidated()
    {
        var (selection, facts) = PortalPair();
        Assert.True(selection.Accepted, selection.Reason);
        foreach (var wrong in new[]
        {
            facts.Snapshot with { Generation = 6 },
            facts.Snapshot with { Turn = 2 },
            facts.Snapshot with { MapGeneration = 42 },
            facts.Snapshot with { FightId = Fight + 1 },
            facts.Snapshot with { Ap = null },
            facts.Snapshot with { Mp = null },
            facts.Snapshot with { KnownSpellGrades =
                new Dictionary<int, int> { [14574] = 2, [14579] = 1 } }
        })
        {
            var runner = E.PrepareObservedDecision(facts, enabled: true);
            Assert.True(runner.Accepted, runner.Reason);
            Assert.Equal(E.Phase.Expired, runner.Executor!.Inspect(wrong,
                Array.Empty<C.ComboObservation>()).State);
        }
        var low = E.PrepareObservedDecision(facts with
        {
            Snapshot = facts.Snapshot with { Ap = 1 }
        }, enabled: true);
        Assert.True(low.Accepted, low.Reason);
        Assert.Equal(1, low.PlannedActions); // 1 PA source, 3 PA followup
        var wall = Cells.Select(c => c.Id == Cell(11, 0)
            ? c with { BlocksLos = true } : c).ToArray();
        var expired = selection.Executor!.Inspect(facts.Snapshot with
        {
            Cells = wall
        }, Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Expired, expired.State);
        Assert.False(expired.CanDispatch);
    }

    [Theory]
    [InlineData(C.RogueBreedId, C.ComboKind.RogueWall)]
    [InlineData(C.XelorBreedId, C.ComboKind.XelorTelefrag)]
    public void Source_spells_without_verified_second_link_remain_one_action(
        int breed, C.ComboKind kind)
    {
        var source = breed == C.RogueBreedId ? Bomb : Swap;
        var facts = Facts(breed, new[] { source },
            enemyCell: Entry);
        var selected = E.PrepareObservedDecision(facts, enabled: true);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(kind, selected.Kind);
        Assert.Equal(1, selected.PlannedActions);
        Assert.False(selected.Executor!.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).CanDispatch);
    }

    [Fact]
    public void Xelor_admits_Permutation_then_Gelure_but_requires_two_moves_and_Telefrag()
    {
        var facts = Facts(C.XelorBreedId, new[] { Swap, Freeze }, enemyCell: Entry);
        var selected = E.PrepareObservedDecision(facts, enabled: true);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(C.ComboKind.XelorTelefrag, selected.Kind);
        Assert.Equal(2, selected.PlannedActions);
        Assert.Equal(13285, selected.Preview!.SpellId);
        Assert.Equal(13245, selected.Executor!.AdvisoryPlan.Actions[1].Spell.SpellId);
        Assert.Equal(Start, selected.Executor.AdvisoryPlan.Actions[1].TargetCell);
        Assert.False(selected.CanDispatch);
        var cast = Event(1, C.ComboEventKind.Cast, 13285, Entry);
        var selfMoved = Event(2, C.ComboEventKind.Move) with
        {
            Subject = Actor, FromCell = Start, ToCell = Entry
        };
        var enemyMoved = Event(3, C.ComboEventKind.Move) with
        {
            Subject = Enemy, FromCell = Entry, ToCell = Start
        };
        var telefrag = Event(4, C.ComboEventKind.StateAdded) with
        {
            Subject = Enemy, StateId = C.StateTelefrag
        };
        var after = facts.Snapshot with
        {
            ActorCell = Entry, Ap = 2, LastServerOrder = 4,
            Fighters = new[]
            {
                new PlannerFighter(Actor, Entry, false, 100, 100),
                new PlannerFighter(Enemy, Start, true, 100, 100,
                    States: ImmutableHashSet.Create(C.StateTelefrag),
                    StatesKnown: true)
            }
        };
        var observed = new[] { cast, selfMoved, enemyMoved, telefrag };
        var ready = selected.Executor.Inspect(after, observed);
        Assert.Equal(E.Phase.Confirmed, ready.State);
        Assert.Equal(1, ready.ConfirmedStages);
        Assert.Equal(13245, ready.AdvisoryAction!.SpellId);
        Assert.Equal(Start, ready.AdvisoryAction.Cell);
        Assert.False(ready.CanDispatch);

        var other = E.PrepareObservedDecision(facts, enabled: true);
        var noState = other.Executor!.Inspect(after with
        {
            Fighters = new[]
            {
                new PlannerFighter(Actor, Entry, false, 100, 100),
                new PlannerFighter(Enemy, Start, true, 100, 100,
                    States: ImmutableHashSet<int>.Empty, StatesKnown: true)
            }
        }, observed);
        Assert.Equal(0, noState.ConfirmedStages);
        Assert.Null(noState.AdvisoryAction);
        Assert.False(noState.CanDispatch);
    }

    [Theory]
    [InlineData(C.FecaBreedId)]
    [InlineData(C.EcaflipBreedId)]
    [InlineData(C.SacrierBreedId)]
    public void Unpinned_Feca_Ecaflip_Sacrier_catalogue_cannot_invent_class_combo(int breed)
    {
        // N01 exports exact Rogue/Elio/Xelor data but no verified class-
        // specific grade/state/trigger join for these three. No fake IDs.
        var facts = Facts(breed, new[] { Bomb });
        var rejected = E.PrepareObservedDecision(facts, enabled: true);
        Assert.False(rejected.Accepted);
        Assert.Equal(0, rejected.PlannedActions);
        Assert.False(rejected.CanDispatch);
        Assert.Contains("ACK", rejected.DispatchBlockedBy);
    }

    [Fact]
    public void Without_observed_owned_portal_or_second_learned_grade_there_is_no_two_step_route()
    {
        var (selected, facts) = PortalPair();
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(2, selected.PlannedActions);
        foreach (var portals in new IReadOnlyList<C.Portal>[]
        {
            Array.Empty<C.Portal>(),
            new[] { new C.Portal(502, Exit, Actor + 1) },
            new[] { new C.Portal(502, Exit, Actor, Active: false) }
        })
        {
            var changed = facts with
            {
                Snapshot = facts.Snapshot with { Portals = portals }
            };
            var admitted = E.PrepareObservedDecision(changed, enabled: true);
            Assert.True(admitted.Accepted, admitted.Reason);
            Assert.Equal(1, admitted.PlannedActions);
            Assert.False(admitted.CanDispatch);
        }
        var missingGrade = facts with
        {
            CatalogSpells = new[] { Portal },
            ObservedLearnedGrades = new Dictionary<int, int>
            {
                [14574] = 1
            },
            Snapshot = facts.Snapshot with
            {
                KnownSpellGrades = new Dictionary<int, int>
                {
                    [14574] = 1
                }
            }
        };
        var one = E.PrepareObservedDecision(missingGrade, enabled: true);
        Assert.True(one.Accepted, one.Reason);
        Assert.Equal(1, one.PlannedActions);
        var bounded = E.PrepareObservedDecision(
            facts with { MaxCandidateChecks = 1 }, enabled: true);
        Assert.False(bounded.Accepted);
        Assert.False(bounded.CanDispatch);
    }

    [Fact]
    public void Determinism_and_allocations_are_measured_without_wall_clock()
    {
        var (selected, facts) = PortalPair();
        Assert.True(selected.Accepted, selected.Reason);
        var reordered = facts with
        {
            CatalogSpells = facts.CatalogSpells!.Reverse().ToArray(),
            ObservedLearnedGrades = new Dictionary<int, int>
            {
                [14579] = 1, [14574] = 1
            }
        };
        reordered = reordered with
        {
            Snapshot = reordered.Snapshot with
            {
                Cells = Cells.Reverse().ToArray(),
                KnownSpellGrades = reordered.ObservedLearnedGrades
            }
        };
        var equal = E.PrepareObservedDecision(reordered, enabled: true);
        Assert.True(equal.Accepted, equal.Reason);
        Assert.Equal(selected.Preview, equal.Preview);
        Assert.Equal(selected.PlannedActions, equal.PlannedActions);
        Assert.Equal(selected.Executor!.AdvisoryPlan.Actions[1],
            equal.Executor!.AdvisoryPlan.Actions[1]);

        static long Allocations(E.ObservedDecisionFacts input, int rounds)
        {
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < rounds; i++)
            {
                var selection = E.PrepareObservedDecision(input, enabled: true);
                if (!selection.Accepted || selection.PlannedActions != 2)
                    throw new InvalidOperationException(selection.Reason);
            }
            return GC.GetAllocatedBytesForCurrentThread() - start;
        }
        Allocations(facts, 2); // warm-up JIT and iterator types
        long three = Allocations(facts, 3);
        long six = Allocations(facts, 6);
        Assert.True(three > 0 && six > three);
        Assert.True(six < three * 3,
            $"allocations not bounded by candidate count: 3={three}; 6={six}");
    }
}
