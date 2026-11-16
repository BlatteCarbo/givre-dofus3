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
/// Offline continuation and deterministic allocation contracts. Server ACK
/// flags in these fixtures are SYNTHETIC, not live transport proof. The only
/// concrete class spell IDs used here were previously joined to the local
/// 3.7.13.17 client bundle; other classes remain fail-closed without a join.
/// No game sender is available through these classes.
/// </summary>
public sealed class CombatTacticalContinuationTests
{
    private readonly ITestOutputHelper _output;
    public CombatTacticalContinuationTests(ITestOutputHelper output) => _output = output;
    private const long Fight = 901, Self = 77, Enemy = -9;
    private static readonly int Start = Cell(10), EnemyCell = Cell(15);
    private static readonly PlannerCell[] AllCells =
        Enumerable.Range(0, MapPathfinder.CellCount)
            .Select(c => new PlannerCell(c, true, false, ImmutableArray<int>.Empty))
            .ToArray();
    private static readonly SpellCastDescriptor Bomb = new(13444, 1, 2, 1, 8, 12,
        false, 2, 1, 0, 0, "",
        ImmutableArray.Create(new SpellEffectDescriptor(1008,
            SpellEffectKind.Summon, 0, 0, 0, 0, "a,A", 'P', 1, 0,
            DiceNum: C.VerifiedExplobombTemplate)));
    private static readonly SpellCastDescriptor Portal = new(14574, 1, 2, 1, 8, 12,
        false, 2, 1, 0, 0, "",
        ImmutableArray.Create(new SpellEffectDescriptor(1181,
            SpellEffectKind.Unknown, 0, 0, 0, 0, "a,A", 'P', 1, 0)));
    private static readonly SpellCastDescriptor Swap = new(13285, 1, 2, 1, 8, 20,
        false, 2, 1, 0, 0, "",
        ImmutableArray.Create(new SpellEffectDescriptor(8,
            SpellEffectKind.Swap, 0, 0, 0, 0, "A", 'P', 1, 0)));
    private static readonly IReadOnlyDictionary<int, int> BombGrade =
        new Dictionary<int, int> { [13444] = 1 };

    private static int Cell(int x, int y = 0)
        => MapPathfinder.CellFromMapPoint(x, y);

    private static V.Snapshot Board(int breed, IReadOnlyDictionary<int, int> grades,
        int? ap = 6, int? mp = 3, IReadOnlySet<int>? marked = null,
        IReadOnlyList<C.Portal>? portals = null, long order = 0)
        => new(Fight, 5, 31, 1, Self, breed, Start, ap, mp, order,
            false, AllCells,
            new[]
            {
                new PlannerFighter(Self, Start, false, 100, 100),
                new PlannerFighter(Enemy, EnemyCell, true, 100, 100)
            }, marked ?? new HashSet<int>(), portals ?? Array.Empty<C.Portal>(),
            KnownSpellGrades: grades);

    private static E.ObservedDecisionFacts Facts(int breed, SpellCastDescriptor spell)
    {
        var grades = new Dictionary<int, int> { [spell.SpellId] = spell.Grade };
        return new(new C.ComboFrame(Fight, 5, 1, 1, Self, breed),
            Board(breed, grades), C.VerifiedClassClientVersion, grades,
            new[] { spell });
    }

    private static E.ObservedDecisionSelection Select(
        E.ObservedDecisionFacts facts)
        => E.PrepareObservedDecision(facts, enabled: true);

    [Theory]
    [InlineData(C.RogueBreedId)]
    [InlineData(C.EliotropeBreedId)]
    [InlineData(C.XelorBreedId)]
    public void Stable_observations_continue_with_one_action_and_no_send_permission(int breed)
    {
        var spell = breed == C.RogueBreedId ? Bomb
            : breed == C.EliotropeBreedId ? Portal : Swap;
        var facts = Facts(breed, spell);
        var result = Select(facts);
        Assert.True(result.Accepted, result.Reason);
        Assert.False(result.CanDispatch);
        var initial = result.Executor!.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Advisory, initial.State);
        Assert.Equal(result.Preview, initial.AdvisoryAction);
        Assert.False(initial.CanDispatch);

        var unchanged = result.Executor.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Advisory, unchanged.State);
        Assert.Equal(initial.AdvisoryAction, unchanged.AdvisoryAction);
        Assert.False(unchanged.CanDispatch);

        // V17 requires an individually attributed transition for any MP
        // delta. A same-order snapshot with changed MP is never 'stable'.
        var changed = result.Executor.Inspect(facts.Snapshot with { Mp = 2 },
            Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Expired, changed.State);
        Assert.Null(changed.AdvisoryAction);
        Assert.False(changed.CanDispatch);
    }

    [Fact]
    public void Existing_auto_candidate_expires_when_mark_prevents_cast_and_cannot_be_recovered()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var result = Select(facts);
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal(E.Phase.Advisory, result.Executor!.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).State);
        int target = result.Preview!.Cell;
        Assert.Equal(E.Phase.Expired, result.Executor.Inspect(facts.Snapshot with
        {
            MarkedCells = new HashSet<int> { target }
        }, Array.Empty<C.ComboObservation>()).State);
        Assert.Equal(E.Phase.Expired, result.Executor.Inspect(
            facts.Snapshot, Array.Empty<C.ComboObservation>()).State);
    }

    [Fact]
    public void Pending_transport_stays_pending_until_context_can_be_revalidated()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var result = Select(facts);
        Assert.True(result.Accepted);
        int target = result.Preview!.Cell;
        // Unknown external action by itself remains Pending, never a grant.
        var unknown = result.Executor!.Inspect(facts.Snapshot with
        {
            PendingAction = null
        }, Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Pending, unknown.State);
        Assert.Null(unknown.AdvisoryAction);
        Assert.False(unknown.CanDispatch);

        var changedBoard = facts.Snapshot with
        {
            MarkedCells = new HashSet<int> { target },
            PendingAction = null
        };
        var pending = result.Executor!.Inspect(changedBoard,
            Array.Empty<C.ComboObservation>());
        // The previously legal cell is now marked without an observed server
        // transition. Revocation outranks the unknown pending transport.
        Assert.Equal(E.Phase.Expired, pending.State);
        Assert.Equal(0, pending.ConfirmedStages);
        Assert.Null(pending.AdvisoryAction);
        Assert.False(pending.CanDispatch);

        // A client-side Sent / unknown ACK cannot clear the changed legality.
        var revalidated = result.Executor.Inspect(changedBoard with
        {
            PendingAction = false
        }, Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Expired, revalidated.State);
        Assert.False(revalidated.CanDispatch);
    }

    [Fact]
    public void Resource_and_fighter_changes_revoke_automatic_candidate_but_not_legacy_replay_semantics()
    {
        var facts = Facts(C.XelorBreedId, Swap);
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(E.Phase.Expired, selected.Executor!.Inspect(facts.Snapshot with
        {
            Ap = 1
        }, Array.Empty<C.ComboObservation>()).State);

        var second = Select(facts);
        Assert.True(second.Accepted);
        var relocated = facts.Snapshot with
        {
            Fighters = new[]
            {
                new PlannerFighter(Self, Start, false, 100, 100),
                new PlannerFighter(Enemy, Cell(16), true, 100, 100)
            }
        };
        Assert.Equal(E.Phase.Expired, second.Executor!.Inspect(relocated,
            Array.Empty<C.ComboObservation>()).State);
        Assert.Equal(E.Phase.Expired, second.Executor.Inspect(
            facts.Snapshot, Array.Empty<C.ComboObservation>()).State);
    }

    [Fact]
    public void Eliotrope_owner_network_becoming_full_or_unknown_expires_previous_candidate()
    {
        var facts = Facts(C.EliotropeBreedId, Portal);
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(E.Phase.Advisory, selected.Executor!.Inspect(
            facts.Snapshot, Array.Empty<C.ComboObservation>()).State);
        var full = facts.Snapshot with { Portals = new[]
        {
            new C.Portal(501, Cell(11, 1), Self),
            new C.Portal(502, Cell(12, 1), Self),
            new C.Portal(503, Cell(13, 1), Self),
            new C.Portal(504, Cell(14, 1), Self)
        } };
        Assert.Equal(E.Phase.Expired, selected.Executor.Inspect(full,
            Array.Empty<C.ComboObservation>()).State);
        var unknown = Select(facts);
        Assert.True(unknown.Accepted);
        Assert.Equal(E.Phase.Expired, unknown.Executor!.Inspect(
            facts.Snapshot with { Portals = null },
            Array.Empty<C.ComboObservation>()).State);
        Assert.False(unknown.Executor.Inspect(facts.Snapshot,
            Array.Empty<C.ComboObservation>()).CanDispatch);
    }

    [Fact]
    public void Reconnection_turn_map_and_reordered_history_never_continue_old_plan()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        foreach (var invalid in new[]
        {
            facts.Snapshot with { Generation = 6 },
            facts.Snapshot with { Turn = 2 },
            facts.Snapshot with { MapGeneration = 32 },
            facts.Snapshot with { FightId = Fight + 1 },
            facts.Snapshot with { ActorId = Self + 1 }
        })
        {
            var selected = Select(facts);
            Assert.True(selected.Accepted, selected.Reason);
            Assert.Equal(E.Phase.Expired, selected.Executor!.Inspect(invalid,
                Array.Empty<C.ComboObservation>()).State);
        }

        var badHistory = Select(facts);
        var duplicate = new[]
        {
            new C.ComboObservation(1, Fight, 5, 1, Self, Enemy,
                C.ComboEventKind.Cast, SpellId: Bomb.SpellId,
                ToCell: badHistory.Preview!.Cell),
            new C.ComboObservation(1, Fight, 5, 1, Self, Enemy,
                C.ComboEventKind.Cast, SpellId: Bomb.SpellId,
                ToCell: badHistory.Preview.Cell)
        };
        Assert.Equal(E.Phase.Expired, badHistory.Executor!.Inspect(
            facts.Snapshot with { LastServerOrder = 1 }, duplicate).State);
    }

    [Fact]
    public void Switching_from_synthetic_replay_to_diagnostic_ingress_retracts_its_confirmation()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var selected = Select(facts);
        Assert.True(selected.Accepted, selected.Reason);
        var cast = new C.ComboObservation(1, Fight, 5, 1, Self, Enemy,
            C.ComboEventKind.Cast, SpellId: 13444, Grade: 1,
            ToCell: selected.Preview!.Cell, Correlation: 100,
            ServerConfirmed: true);
        var observed = facts.Snapshot with { LastServerOrder = 1 };
        // The historical replay API deliberately accepts SYNTHETIC positive
        // rows to verify its transition logic, but this is not live authority.
        var replay = selected.Executor!.Inspect(observed, new[] { cast });
        Assert.Equal(E.Phase.Confirmed, replay.State);
        Assert.False(replay.CanDispatch);
        // Diagnostic-only source has no verified ACK/transport epoch, so the
        // older replay phase must be revoked rather than carried forward.
        var diagnostic = selected.Executor.InspectDiagnostic(observed,
            new[] { cast });
        Assert.Equal(E.Phase.Expired, diagnostic.State);
        Assert.Null(diagnostic.AdvisoryAction);
        Assert.False(diagnostic.CanDispatch);
        Assert.Equal(E.Phase.Expired, selected.Executor.Inspect(observed,
            new[] { cast }).State);
    }

    [Fact]
    public void Indexed_sequence_agrees_with_legacy_sequence_and_refuses_a_stale_index()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var boardIndex = V.IndexCastBoard(facts.Snapshot);
        Assert.NotNull(boardIndex);
        var evidence = new C.ComboEvidence(TargetId: Enemy, SourceSpell: Bomb,
            ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: C.RogueBreedId,
            ObservedLearnedGrades: BombGrade);
        foreach (int cell in Enumerable.Range(0, MapPathfinder.CellCount)
            .Where(c => c != Start && c != EnemyCell).Take(80))
        {
            var actions = new[] { new V.Action(Bomb, cell) };
            var legacy = V.Prepare(C.ComboKind.RogueWall,
                facts.Frame, evidence, facts.Snapshot, actions);
            var indexed = V.PrepareIndexed(C.ComboKind.RogueWall,
                facts.Frame, evidence, facts.Snapshot, actions, boardIndex!);
            Assert.Equal(legacy.Accepted, indexed.Accepted);
            Assert.Equal(legacy.Reason, indexed.Reason);
            if (!legacy.Accepted) continue;
            var empty = Array.Empty<C.ComboObservation>();
            var a = V.Next(legacy.Value!, facts.Snapshot, empty);
            var b = V.NextIndexed(indexed.Value!, facts.Snapshot, empty,
                boardIndex!);
            Assert.Equal(a.Ready, b.Ready);
            Assert.Equal(a.Instruction, b.Instruction);
        }
        var altered = facts.Snapshot with
        {
            MarkedCells = new HashSet<int> { Cell(12) }
        };
        var one = new[] { new V.Action(Bomb, Cell(12)) };
        Assert.Equal(V.Prepare(C.ComboKind.RogueWall, facts.Frame,
            evidence, altered, one).Accepted,
            V.PrepareIndexed(C.ComboKind.RogueWall, facts.Frame,
                evidence, altered, one, boardIndex!).Accepted);
    }

    [Fact]
    public void Indexed_legal_checks_allocate_less_than_repeated_560_cell_legacy_checks()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var index = V.IndexCastBoard(facts.Snapshot);
        Assert.NotNull(index);
        var evidence = new C.ComboEvidence(TargetId: Enemy, SourceSpell: Bomb,
            ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: C.RogueBreedId,
            ObservedLearnedGrades: BombGrade);
        var targets = Enumerable.Range(0, MapPathfinder.CellCount)
            .Where(c => c != Start && c != EnemyCell).Take(64).ToArray();
        long Measure(bool reuseIndex)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int repeat = 0; repeat < 3; repeat++)
            foreach (int target in targets)
            {
                var action = new[] { new V.Action(Bomb, target) };
                var result = reuseIndex
                    ? V.PrepareIndexed(C.ComboKind.RogueWall, facts.Frame,
                        evidence, facts.Snapshot, action, index!)
                    : V.Prepare(C.ComboKind.RogueWall, facts.Frame,
                        evidence, facts.Snapshot, action);
                if (result.Value is not { } plan) continue;
                if (reuseIndex)
                    V.NextIndexed(plan, facts.Snapshot,
                        Array.Empty<C.ComboObservation>(), index!);
                else
                    V.Next(plan, facts.Snapshot,
                        Array.Empty<C.ComboObservation>());
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        // Warm both paths before counting only thread-local allocations;
        // no wall clock or machine-dependent performance threshold is used.
        Measure(false);
        Measure(true);
        var legacyWatch = Stopwatch.StartNew();
        long legacyBytes = Measure(false);
        legacyWatch.Stop();
        var indexedWatch = Stopwatch.StartNew();
        long indexedBytes = Measure(true);
        indexedWatch.Stop();
        _output.WriteLine($"V5 offline tactical actual cost: 192 candidate checks " +
            $"legacy={legacyWatch.Elapsed.TotalMilliseconds:F3} ms/{legacyBytes} B; " +
            $"indexed={indexedWatch.Elapsed.TotalMilliseconds:F3} ms/{indexedBytes} B; " +
            "GC allocated bytes are current-thread only; wall-clock is environment-sensitive, not live.");
        Assert.True(indexedBytes < legacyBytes,
            $"index must save allocations, indexed={indexedBytes}, legacy={legacyBytes}");
    }
}
