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
/// End-to-end admission -> review contracts for the SAME executor used by the
/// opt-in desktop advisor. Observed-cast fixtures are synthetic replay ONLY:
/// CombatComboIngressBuffer has no authenticated transport generation or ACK.
/// Unpinned Feca/Ecaflip/Sacrier descriptors below test typed class predicates,
/// not an assertion that these artificial IDs exist in the installed client.
/// </summary>
public sealed class CombatConfirmedIngressJourneyV9Tests
{
    private readonly ITestOutputHelper _output;
    public CombatConfirmedIngressJourneyV9Tests(ITestOutputHelper output) => _output = output;

    private const long Actor = 77, Enemy = -9, Fight = 8709;
    private const int Generation = 6;
    private static readonly int Start = Cell(10, 0);
    private static readonly int EnemyCell = Cell(15, 0);
    private static readonly int Exit = Cell(10, 9);
    private static readonly int Impact = Cell(11, 9);
    private static readonly PlannerCell[] Cells = Enumerable.Range(0, MapPathfinder.CellCount)
        .Select(id => new PlannerCell(id, true, false, ImmutableArray<int>.Empty))
        .ToArray();

    private static int Cell(int x, int y) => MapPathfinder.CellFromMapPoint(x, y);

    private static SpellCastDescriptor Spell(int id, int action, SpellEffectKind kind,
        int flags, string mask = "a,A", int dice = 0, int state = 0,
        int minRange = 1)
        => new(id, 1, 2, minRange, 8, flags, false, 2, 2, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(action, kind,
                0, 0, 0, 0, mask, 'P', 1, 0, DiceNum: dice, Value: state)));

    // The following three spell identities are pinned by the 3.7.13.17
    // exported descriptors; they still do not establish a live cast receipt.
    private static readonly SpellCastDescriptor Bomb = Spell(13444, 1008,
        SpellEffectKind.Summon, 12, dice: C.VerifiedExplobombTemplate);
    private static readonly SpellCastDescriptor Portal = Spell(14574, 1181,
        SpellEffectKind.Unknown, 12);
    private static readonly SpellCastDescriptor Ray = Spell(14579, 99,
        SpellEffectKind.Damage, 4, mask: "A");
    private static readonly SpellCastDescriptor Swap = Spell(13285, 8,
        SpellEffectKind.Swap, 20, mask: "A");

    private static V.Snapshot Board(int breed, IReadOnlyDictionary<int, int> grades,
        int enemyCell = -1, IReadOnlyList<C.Portal>? portals = null,
        ImmutableHashSet<int>? states = null, long order = 0)
        => new(Fight, Generation, 19, 1, Actor, breed, Start, 6, 3, order,
            false, Cells,
            new[]
            {
                new PlannerFighter(Actor, Start, false, 100, 100,
                    States: states ?? ImmutableHashSet<int>.Empty, StatesKnown: true),
                new PlannerFighter(Enemy, enemyCell < 0 ? EnemyCell : enemyCell,
                    true, 100, 100)
            }, new HashSet<int>(), portals ?? Array.Empty<C.Portal>(),
            KnownSpellGrades: grades);

    private static E.ObservedDecisionFacts Facts(int breed,
        IReadOnlyList<SpellCastDescriptor> descriptors,
        int enemyCell = -1, IReadOnlyList<C.Portal>? portals = null,
        ImmutableHashSet<int>? states = null,
        IReadOnlySet<int>? verifiedStates = null)
    {
        var grades = descriptors.ToDictionary(s => s.SpellId, s => s.Grade);
        return new E.ObservedDecisionFacts(
            new C.ComboFrame(Fight, Generation, 1, 1, Actor, breed),
            Board(breed, grades, enemyCell, portals, states),
            C.VerifiedClassClientVersion, grades, descriptors,
            VerifiedClassStateIds: verifiedStates);
    }

    private static E.ObservedDecisionSelection Select(E.ObservedDecisionFacts facts,
        E.Limits? limits = null)
        => E.PrepareObservedDecision(facts, executorLimits: limits, enabled: true);

    [Theory]
    [InlineData(C.RogueBreedId, C.ComboKind.RogueWall)]
    [InlineData(C.EliotropeBreedId, C.ComboKind.EliotropeRedirect)]
    [InlineData(C.XelorBreedId, C.ComboKind.XelorTelefrag)]
    public void Pinned_spell_admission_revalidates_one_cast_without_transport_permission(
        int breed, C.ComboKind kind)
    {
        var source = breed == C.RogueBreedId ? Bomb
            : breed == C.EliotropeBreedId ? Portal : Swap;
        var facts = Facts(breed, new[] { source });
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(kind, selected.Kind);
        Assert.False(selected.CanDispatch);
        Assert.Equal(E.Phase.Advisory, selected.Executor!.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).State);

        var modified = breed switch
        {
            C.RogueBreedId => facts.Snapshot with
            {
                MarkedCells = new HashSet<int> { selected.Preview!.Cell }
            },
            C.XelorBreedId => facts.Snapshot with
            {
                Fighters = new[]
                {
                    new PlannerFighter(Actor, Start, false, 100, 100),
                    new PlannerFighter(Enemy, Cell(16, 0), true, 100, 100)
                }
            },
            _ => facts.Snapshot with { Ap = 1 }
        };
        Assert.Equal(E.Phase.Expired, selected.Executor.Inspect(modified,
            Array.Empty<C.ComboObservation>()).State);
        Assert.Equal(E.Phase.Expired, selected.Executor.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).State);
        Assert.False(selected.Executor.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).CanDispatch);
    }

    [Fact]
    public void Eliotrope_existing_exit_deactivation_revokes_selected_two_cast_route()
    {
        // From (10,0) an entry at (11,0) into the existing exit (10,9)
        // projects at (11,9). All points are actual board coordinates.
        var baseline = new[] { new C.Portal(501, Exit, Actor) };
        var facts = Facts(C.EliotropeBreedId, new[] { Portal, Ray },
            enemyCell: Impact, portals: baseline);
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(2, selected.PlannedActions);
        Assert.Equal(Portal.SpellId, selected.Preview!.SpellId);
        Assert.Equal(E.Phase.Advisory, selected.Executor!.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).State);
        var deactivated = facts.Snapshot with
        {
            Portals = new[] { baseline[0] with { Active = false } }
        };
        var expired = selected.Executor.Inspect(deactivated,
            Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Expired, expired.State);
        Assert.Contains("portal", expired.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(expired.AdvisoryAction);
        Assert.False(expired.CanDispatch);

        // A replacement mark using the same cell cannot resurrect the old exit.
        Assert.Equal(E.Phase.Expired, selected.Executor.Inspect(facts.Snapshot with
        {
            Portals = new[] { new C.Portal(502, Exit, Actor) }
        }, Array.Empty<C.ComboObservation>()).State);
    }

    [Fact]
    public void Eliotrope_portal_addition_without_selected_cast_ack_cannot_extend_network()
    {
        var facts = Facts(C.EliotropeBreedId, new[] { Portal }, portals: new[]
        {
            new C.Portal(501, Exit, Actor)
        });
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(E.Phase.Expired, selected.Executor!.Inspect(facts.Snapshot with
        {
            Portals = new[]
            {
                new C.Portal(501, Exit, Actor),
                new C.Portal(503, Cell(11, 0), Actor)
            }
        }, Array.Empty<C.ComboObservation>()).State);
    }

    [Fact]
    public void Ecaflip_observed_state_from_exact_cast_advances_replay_then_removal_expires()
    {
        const int classState = 801; // Synthetic state for grammar, not client identity.
        var card = Spell(61001, 950, SpellEffectKind.Buff, 16,
            mask: "a", state: classState, minRange: 0);
        var facts = Facts(C.EcaflipBreedId, new[] { card },
            verifiedStates: new HashSet<int> { classState });
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(C.ComboKind.EcaflipCard, selected.Kind);
        Assert.Equal(E.Phase.Advisory, selected.Executor!.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).State);

        var cast = new C.ComboObservation(1, Fight, Generation, 1, Actor, Actor,
            C.ComboEventKind.Cast, SpellId: card.SpellId, Grade: card.Grade,
            ToCell: Start, Correlation: 919, ServerConfirmed: true);
        var granted = new C.ComboObservation(2, Fight, Generation, 1, Actor, Actor,
            C.ComboEventKind.StateAdded, StateId: classState,
            Correlation: 919, ServerConfirmed: true);
        var after = facts.Snapshot with
        {
            LastServerOrder = 2,
            Fighters = new[]
            {
                new PlannerFighter(Actor, Start, false, 100, 100,
                    States: ImmutableHashSet.Create(classState), StatesKnown: true),
                new PlannerFighter(Enemy, EnemyCell, true, 100, 100)
            }
        };
        var replay = selected.Executor.Inspect(after, new[] { cast, granted });
        Assert.Equal(E.Phase.Confirmed, replay.State);
        Assert.Equal(1, replay.ConfirmedStages);
        Assert.False(replay.CanDispatch);

        var revoked = new C.ComboObservation(3, Fight, Generation, 1, Actor, Actor,
            C.ComboEventKind.StateRemoved, StateId: classState, ServerConfirmed: true);
        Assert.Equal(E.Phase.Expired, selected.Executor.Inspect(facts.Snapshot with
        {
            LastServerOrder = 3
        }, new[] { cast, granted, revoked }).State);
    }

    [Fact]
    public void Uncorrelated_card_state_and_diagnostic_fake_ack_are_never_promoted()
    {
        const int classState = 801;
        var card = Spell(61001, 950, SpellEffectKind.Buff, 16,
            mask: "a", state: classState, minRange: 0);
        var facts = Facts(C.EcaflipBreedId, new[] { card },
            verifiedStates: new HashSet<int> { classState });
        var forged = new C.ComboObservation(1, Fight, Generation, 1, Actor, Actor,
            C.ComboEventKind.Cast, SpellId: card.SpellId, Grade: 1,
            ToCell: Start, Correlation: 0, ServerConfirmed: true);
        var added = new C.ComboObservation(2, Fight, Generation, 1, Actor, Actor,
            C.ComboEventKind.StateAdded, StateId: classState,
            Correlation: 999, ServerConfirmed: true);
        var after = facts.Snapshot with
        {
            LastServerOrder = 2,
            Fighters = new[]
            {
                new PlannerFighter(Actor, Start, false, 100, 100,
                    States: ImmutableHashSet.Create(classState), StatesKnown: true),
                new PlannerFighter(Enemy, EnemyCell, true, 100, 100)
            }
        };
        var uncorrelated = Select(facts);
        Assert.True(uncorrelated.Accepted);
        Assert.Equal(E.Phase.Expired, uncorrelated.Executor!.Inspect(after,
            new[] { forged, added }).State);

        var diagnostic = Select(facts);
        Assert.True(diagnostic.Accepted);
        var downgraded = diagnostic.Executor!.InspectDiagnostic(after,
            new[] { forged with { Correlation = 999 }, added });
        Assert.Equal(E.Phase.Expired, downgraded.State);
        Assert.Equal(0, downgraded.ConfirmedStages);
        Assert.False(downgraded.CanDispatch);
    }

    [Theory]
    [InlineData(C.FecaBreedId, C.ComboKind.FecaGlyph)]
    [InlineData(C.SacrierBreedId, C.ComboKind.SacrierSuffering)]
    public void Other_class_predicate_requires_observed_grade_and_current_state(
        int breed, C.ComboKind expected)
    {
        const int classState = 902;
        var glyph = Spell(62001, 401, SpellEffectKind.Unknown, 8);
        var pull = Spell(63001, 6, SpellEffectKind.Pull, 20, mask: "A");
        var stateSource = Spell(63002, 950, SpellEffectKind.Buff, 16,
            mask: "a", state: classState);
        var catalog = breed == C.FecaBreedId ? new[] { glyph }
            : new[] { pull, stateSource };
        var facts = Facts(breed, catalog,
            states: ImmutableHashSet.Create(classState),
            verifiedStates: new HashSet<int> { classState });
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(expected, selected.Kind);
        Assert.False(selected.CanDispatch);
        Assert.Equal(E.Phase.Advisory, selected.Executor!.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).State);
        Assert.Equal(E.Phase.Expired, selected.Executor.Inspect(facts.Snapshot with
        {
            KnownSpellGrades = new Dictionary<int, int>()
        }, Array.Empty<C.ComboObservation>()).State);

        var changed = Select(facts);
        Assert.True(changed.Accepted, changed.Reason);
        var invalidated = breed == C.FecaBreedId
            ? facts.Snapshot with
            {
                MarkedCells = new HashSet<int> { changed.Preview!.Cell }
            }
            : facts.Snapshot with
            {
                Fighters = new[]
                {
                    new PlannerFighter(Actor, Start, false, 100, 100,
                        States: ImmutableHashSet.Create(classState), StatesKnown: true),
                    new PlannerFighter(Enemy, Cell(16, 0), true, 100, 100)
                }
            };
        Assert.Equal(E.Phase.Expired, changed.Executor!.Inspect(invalidated,
            Array.Empty<C.ComboObservation>()).State);

        if (breed == C.SacrierBreedId)
        {
            var missingState = Facts(breed, catalog,
                verifiedStates: new HashSet<int> { classState });
            Assert.False(Select(missingState).Accepted);
        }
    }

    [Fact]
    public void Pinned_xelor_prohibits_stale_line_of_sight_and_old_actor_turn()
    {
        var facts = Facts(C.XelorBreedId, new[] { Swap });
        var rayCell = MapPathfinder.LineCells(Start, EnemyCell)
            .Skip(1).SkipLast(1).First();
        var blocked = Cells.Select(c => c.Id == rayCell
            ? c with { BlocksLos = true } : c).ToArray();
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(E.Phase.Expired, selected.Executor!.Inspect(facts.Snapshot with
        {
            Cells = blocked
        }, Array.Empty<C.ComboObservation>()).State);

        foreach (var invalid in new[]
        {
            facts.Snapshot with { Generation = Generation + 1 },
            facts.Snapshot with { Turn = 2 },
            facts.Snapshot with { MapGeneration = 20 },
            facts.Snapshot with { ActorId = Actor + 1 }
        })
        {
            var runner = Select(facts);
            Assert.True(runner.Accepted, runner.Reason);
            Assert.Equal(E.Phase.Expired, runner.Executor!.Inspect(invalid,
                Array.Empty<C.ComboObservation>()).State);
            Assert.False(runner.Executor.Inspect(facts.Snapshot,
                Array.Empty<C.ComboObservation>()).CanDispatch);
        }
    }

    [Fact]
    public void Direct_review_rejects_null_gapped_and_cross_generation_observations()
    {
        var facts = Facts(C.RogueBreedId, new[] { Bomb });
        foreach (var observations in new IReadOnlyList<C.ComboObservation>[]
        {
            new C.ComboObservation[] { null! },
            new[] { new C.ComboObservation(1, Fight + 1, Generation, 1,
                Actor, Enemy, C.ComboEventKind.Effect) },
            new[] { new C.ComboObservation(1, Fight, Generation + 1, 1,
                Actor, Enemy, C.ComboEventKind.Effect) }
        })
        {
            var selected = Select(facts);
            Assert.True(selected.Accepted);
            var review = selected.Executor!.Inspect(facts.Snapshot with
            {
                LastServerOrder = 1
            }, observations);
            Assert.Equal(E.Phase.Expired, review.State);
            Assert.False(review.CanDispatch);
        }
    }

    [Fact]
    public void Bounded_stable_reviews_record_offline_cost_and_never_dispatch()
    {
        var facts = Facts(C.RogueBreedId, new[] { Bomb });
        var selected = Select(facts, new E.Limits(MaxReviews: 128));
        Assert.True(selected.Accepted, selected.Reason);
        for (int i = 0; i < 3; i++)
            Assert.Equal(E.Phase.Advisory, selected.Executor!.Inspect(facts.Snapshot,
                Array.Empty<C.ComboObservation>()).State);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 48; i++)
        {
            var review = selected.Executor!.Inspect(facts.Snapshot,
                Array.Empty<C.ComboObservation>());
            Assert.Equal(E.Phase.Advisory, review.State);
            Assert.Equal(selected.Preview, review.AdvisoryAction);
            Assert.False(review.CanDispatch);
        }
        clock.Stop();
        long allocations = GC.GetAllocatedBytesForCurrentThread() - before;
        _output.WriteLine($"V9 offline PrepareObservedDecision->Inspect: "
            + $"48 stable reviews, {clock.Elapsed.TotalMilliseconds:F3} ms, "
            + $"{allocations} thread-local allocated bytes; no wall-clock threshold");
        Assert.InRange(allocations, 0, 20_000_000);
    }
}
