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
/// Pure, offline grammar and state-machine tests. The three positive spell IDs
/// 13444/14574/13285 are documented in the pinned N01 client-class export.
/// These objects are synthetic board/packet fixtures, NOT real cast receipts.
/// Feca/Ecaflip/Sacrier have no fully pinned 3.7.13.17 effect/grade fixture
/// in N01: tests assert conservative rejection rather than invent such data.
/// </summary>
public sealed class CombatTacticalDecisionWorkflowTests
{
    private const long Actor = 77, Enemy = -9, Fight = 807;
    private static readonly int Start = Cell(10);
    private static readonly int EnemyCell = Cell(15);
    private static readonly PlannerCell[] Cells = Enumerable.Range(0, MapPathfinder.CellCount)
        .Select(id => new PlannerCell(id, true, false, ImmutableArray<int>.Empty)).ToArray();

    private static int Cell(int x, int y = 0) => MapPathfinder.CellFromMapPoint(x, y);

    private static SpellCastDescriptor Spell(int spell, int action, SpellEffectKind kind,
        int flags, string mask = "a,A", int diceNum = 0, int ap = 2, int range = 6)
        => new(spell, 1, ap, 1, range, flags, false, 2, 1, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(action, kind, 0, 0, 0, 0,
                mask, 'P', 1, 0, DiceNum: diceNum)));

    private static readonly SpellCastDescriptor Bomb = Spell(13444, 1008,
        SpellEffectKind.Summon, 12, diceNum: C.VerifiedExplobombTemplate);
    private static readonly SpellCastDescriptor Portal = Spell(14574, 1181,
        SpellEffectKind.Unknown, 12);
    private static readonly SpellCastDescriptor Swap = Spell(13285, 8,
        SpellEffectKind.Swap, 20, "A");

    private static V.Snapshot Board(int breed, IReadOnlyDictionary<int, int> grades,
        int? ap = 6, int? mp = 3, bool? pending = false, long order = 0,
        IReadOnlyList<PlannerCell>? cells = null,
        IReadOnlyList<PlannerFighter>? fighters = null)
        => new(Fight, 5, 31, 1, Actor, breed, Start, ap, mp, order, pending,
            cells ?? Cells,
            fighters ?? new[]
            {
                new PlannerFighter(Actor, Start, false, 100, 100),
                new PlannerFighter(Enemy, EnemyCell, true, 100, 100)
            }, new HashSet<int>(),
            Array.Empty<C.Portal>(), KnownSpellGrades: grades);

    private static E.ObservedDecisionFacts Facts(int breed,
        params SpellCastDescriptor[] spells)
    {
        var grades = spells.ToDictionary(s => s.SpellId, s => s.Grade);
        var snapshot = Board(breed, grades);
        return new(new C.ComboFrame(Fight, 5, 1, 1, Actor, breed),
            snapshot, C.VerifiedClassClientVersion, grades, spells);
    }

    private static E.ObservedDecisionSelection Select(E.ObservedDecisionFacts facts,
        bool enabled = true)
        => E.PrepareObservedDecision(facts, enabled: enabled);

    [Theory]
    [InlineData(C.RogueBreedId, C.ComboKind.RogueWall)]
    [InlineData(C.EliotropeBreedId, C.ComboKind.EliotropeRedirect)]
    [InlineData(C.XelorBreedId, C.ComboKind.XelorTelefrag)]
    public void Pinned_class_source_is_selected_with_real_spell_grade_and_one_bounded_advisory_step(
        int breed, C.ComboKind expected)
    {
        SpellCastDescriptor source = breed switch
        {
            C.RogueBreedId => Bomb,
            C.EliotropeBreedId => Portal,
            _ => Swap
        };
        var facts = Facts(breed, source);
        var result = Select(facts);
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal(expected, result.Kind);
        Assert.Equal(source.SpellId, result.Preview!.SpellId);
        Assert.Equal(source.Ap, result.Preview.Ap);
        Assert.Equal(PlannerActionKind.Cast, result.Preview.Kind);
        Assert.InRange(result.CheckedCandidates, 1, 4096);
        Assert.False(result.CanDispatch);

        var current = result.Executor!.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Advisory, current.State);
        Assert.Equal(result.Preview, current.AdvisoryAction);
        Assert.False(current.CanDispatch);
        Assert.False(result.Executor.InspectDiagnostic(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).CanDispatch);
    }

    [Fact]
    public void Catalogue_dictionary_and_map_enumeration_order_cannot_change_the_selected_action()
    {
        // Actual pinned Rouge bomb source plus an observed but class-ineligible
        // descriptor. The second descriptor does NOT become a Roublard spell
        // merely because the test's grade book contains it.
        var one = Facts(C.RogueBreedId, Bomb, Portal);
        var two = one with
        {
            CatalogSpells = new[] { Portal, Bomb },
            ObservedLearnedGrades = new Dictionary<int, int>
            {
                [Portal.SpellId] = 1, [Bomb.SpellId] = 1
            },
            Snapshot = one.Snapshot with
            {
                KnownSpellGrades = new Dictionary<int, int>
                {
                    [Portal.SpellId] = 1, [Bomb.SpellId] = 1
                },
                Cells = Cells.Reverse().ToArray()
            }
        };
        var selectedOne = Select(one);
        var selectedTwo = Select(two);
        Assert.True(selectedOne.Accepted, selectedOne.Reason);
        Assert.True(selectedTwo.Accepted, selectedTwo.Reason);
        Assert.Equal(selectedOne.Preview, selectedTwo.Preview);
        Assert.Equal(selectedOne.Kind, selectedTwo.Kind);
        Assert.Equal(selectedOne.CheckedCandidates, selectedTwo.CheckedCandidates);
        Assert.False(selectedTwo.CanDispatch);
    }

    [Fact]
    public void Unknown_or_changed_learned_grade_expires_executor_before_any_advisory_action()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var result = Select(facts);
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal(E.Phase.Expired, result.Executor!.Inspect(
            facts.Snapshot with { KnownSpellGrades = null },
            Array.Empty<C.ComboObservation>()).State);
        Assert.Equal(E.Phase.Expired, result.Executor.Inspect(
            facts.Snapshot, Array.Empty<C.ComboObservation>()).State);

        var wrongGrade = Select(facts with
        {
            Snapshot = facts.Snapshot with
            {
                KnownSpellGrades = new Dictionary<int, int> { [13444] = 2 }
            }
        });
        Assert.False(wrongGrade.Accepted);
        var unknown = Select(facts with { ObservedLearnedGrades = null });
        Assert.False(unknown.Accepted);
        Assert.False(unknown.CanDispatch);
    }

    [Fact]
    public void No_pending_action_may_be_invented_from_a_local_send_or_unknown_transport()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        Assert.False(Select(facts with
        {
            Snapshot = facts.Snapshot with { PendingAction = null }
        }).Accepted);
        Assert.False(Select(facts with
        {
            Snapshot = facts.Snapshot with { PendingAction = true }
        }).Accepted);
        var executor = Select(facts).Executor!;
        Assert.Equal(E.Phase.Pending, executor.Inspect(
            facts.Snapshot with { PendingAction = true },
            Array.Empty<C.ComboObservation>()).State);
        Assert.False(executor.Inspect(
            facts.Snapshot with { PendingAction = null },
            Array.Empty<C.ComboObservation>()).CanDispatch);
        Assert.Equal(E.Phase.Advisory, executor.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).State);
    }

    [Fact]
    public void A_sent_or_unconfirmed_cast_is_pending_and_not_a_class_effect_ack()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var selected = Select(facts);
        Assert.True(selected.Accepted);
        int target = selected.Preview!.Cell;
        var attempted = new C.ComboObservation(1, Fight, 5, 1, Actor, Enemy,
            C.ComboEventKind.Cast, SpellId: 13444, Grade: 1, ToCell: target,
            Correlation: 0, ServerConfirmed: false);
        var now = facts.Snapshot with { LastServerOrder = 1 };
        var pending = selected.Executor!.Inspect(now, new[] { attempted });
        Assert.Equal(E.Phase.Pending, pending.State);
        Assert.Equal(0, pending.ConfirmedStages);
        Assert.Null(pending.AdvisoryAction);
        Assert.False(pending.CanDispatch);
        // A forged-looking raw row must be downgraded by the diagnostic
        // channel even when it claims a nonzero correlation and positive flag.
        var diagnostic = selected.Executor.InspectDiagnostic(now,
            new[] { attempted with { Correlation = 100, ServerConfirmed = true } });
        Assert.Equal(E.Phase.Pending, diagnostic.State);
        Assert.Equal(0, diagnostic.ConfirmedStages);
        Assert.False(diagnostic.CanDispatch);
    }

    [Fact]
    public void Same_PID_old_generation_new_map_and_next_turn_revoke_every_observed_decision()
    {
        var facts = Facts(C.EliotropeBreedId, Portal);
        foreach (var stale in new[]
        {
            facts.Snapshot with { FightId = Fight + 1 },
            facts.Snapshot with { Generation = 6 },
            facts.Snapshot with { MapGeneration = 32 },
            facts.Snapshot with { Turn = 2 },
            facts.Snapshot with { ActorId = Actor + 1 },
            facts.Snapshot with { Ap = null },
            facts.Snapshot with { Mp = null }
        })
        {
            var selected = Select(facts);
            Assert.True(selected.Accepted, selected.Reason);
            Assert.Equal(E.Phase.Expired, selected.Executor!.Inspect(stale,
                Array.Empty<C.ComboObservation>()).State);
            Assert.Equal(E.Phase.Expired, selected.Executor.Inspect(
                facts.Snapshot, Array.Empty<C.ComboObservation>()).State);
            Assert.False(selected.Executor.Inspect(facts.Snapshot,
                Array.Empty<C.ComboObservation>()).CanDispatch);
        }
    }

    [Fact]
    public void Missing_provenance_or_illegal_geometry_exhaustion_refuses_preparation()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        Assert.False(Select(facts with { ClientVersion = "3.7.12" }).Accepted);
        Assert.False(Select(facts with { MaxCandidateChecks = 1 }).Accepted);
        Assert.False(Select(facts with
        {
            Snapshot = facts.Snapshot with { MarkedCells = null }
        }).Accepted);
        Assert.False(Select(facts with
        {
            Snapshot = facts.Snapshot with
            {
                Cells = Cells.Where(c => c.Id != Cell(11)).ToArray()
            }
        }).Accepted);
        Assert.False(Select(facts with
        {
            Snapshot = facts.Snapshot with { Ap = 1 }
        }).Accepted);
        Assert.False(Select(facts with
        {
            Snapshot = facts.Snapshot with { Mp = null }
        }).Accepted);
        Assert.False(Select(facts with
        {
            Snapshot = facts.Snapshot with
            {
                Fighters = new[]
                {
                    new PlannerFighter(Actor, Start, false, 100, 100),
                    new PlannerFighter(Enemy, EnemyCell, true, 100, 100,
                        PositionCertainty: FighterPositionCertainty.LastKnown)
                }
            }
        }).Accepted);
    }

    [Fact]
    public void Xelor_must_reject_unknown_LOS_or_target_mask_instead_of_casting_through_a_wall()
    {
        var facts = Facts(C.XelorBreedId, Swap);
        Assert.True(Select(facts).Accepted);
        var blocked = Cells.Select(c => c.Id == Cell(11)
            ? c with { BlocksLos = true } : c).ToArray();
        Assert.False(Select(facts with
        {
            Snapshot = facts.Snapshot with { Cells = blocked }
        }).Accepted);
        Assert.False(Select(facts with
        {
            CatalogSpells = new[] { Swap with
            {
                Effects = ImmutableArray.Create(
                    Swap.Effects[0] with { TargetMask = "a" })
            } }
        }).Accepted);
        Assert.False(Select(facts with
        {
            Snapshot = facts.Snapshot with { Fighters = new[]
            {
                new PlannerFighter(Actor, Start, false, 100, 100),
                new PlannerFighter(Enemy, EnemyCell, true, 100, 100,
                    Visibility: FighterVisibility.Invisible)
            } }
        }).Accepted);
    }

    [Fact]
    public void An_occupied_or_marked_board_cannot_produce_an_imaginary_placement()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        Assert.True(Select(facts).Accepted);
        var marked = Enumerable.Range(0, MapPathfinder.CellCount)
            .Where(c => c != Start && c != EnemyCell).ToHashSet();
        Assert.False(Select(facts with
        {
            Snapshot = facts.Snapshot with { MarkedCells = marked }
        }).Accepted);
        Assert.False(Select(facts with
        {
            CatalogSpells = new[] { Bomb with
            {
                Effects = ImmutableArray.Create(
                    Bomb.Effects[0] with { DiceNum = 0 })
            } }
        }).Accepted);
    }

    [Theory]
    [InlineData(C.FecaBreedId)]
    [InlineData(C.EcaflipBreedId)]
    [InlineData(C.SacrierBreedId)]
    public void Unpinned_other_class_spell_effect_or_unobserved_state_never_creates_a_combo(
        int breed)
    {
        var facts = Facts(breed, Bomb);
        var selected = Select(facts);
        Assert.False(selected.Accepted);
        Assert.Null(selected.Executor);
        Assert.Null(selected.Preview);
        Assert.False(selected.CanDispatch);
    }
}
