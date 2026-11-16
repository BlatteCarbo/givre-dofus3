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

/// <summary>Offline synthetic observations, never a claimed live ACK. The
/// current CombatComboIngressBuffer marks all observed mark rows unconfirmed
/// until a genuine transport generation is integrated.</summary>
public sealed class VerifiedTacticalExecutorLifecycleTests
{
    private const long Self = 77;
    private const long Enemy = -9;
    private const long Fight = 991;
    private static readonly int Start = Cell(10, 0);
    private static readonly int Entry = Cell(12, 0);
    private static readonly int Exit = Cell(10, 9);
    private static readonly int Impact = Cell(12, 9);
    private static readonly PlannerCell[] AllCells = Enumerable.Range(0, MapPathfinder.CellCount)
        .Select(id => new PlannerCell(id, true, false, ImmutableArray<int>.Empty)).ToArray();

    private static int Cell(int x, int y) => MapPathfinder.CellFromMapPoint(x, y);

    private static SpellCastDescriptor Spell(int id, int effect, SpellEffectKind kind, int flags,
        int dice = 0)
        => new(id, 1, 2, 1, 10, flags, false, 2, 2, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(effect, kind, 0, 0, 0, 0,
                "a,A", 'P', 1, 0, DiceNum: dice)));

    private static readonly SpellCastDescriptor Explobomb = Spell(13444, 1008,
        SpellEffectKind.Summon, 12, dice: C.VerifiedExplobombTemplate);
    private static readonly SpellCastDescriptor Portal = Spell(14574, 1181,
        SpellEffectKind.Unknown, 12);
    private static readonly SpellCastDescriptor PortalHit = Spell(14579, 99,
        SpellEffectKind.Damage, 4);

    private static V.Snapshot Board(int breed, long order = 0, int? ap = 6,
        IReadOnlySet<int>? marks = null, IReadOnlyList<C.Portal>? portals = null)
        => new(Fight, 5, 41, 1, Self, breed, Start, ap, 3, order, false,
            AllCells,
            new[]
            {
                new PlannerFighter(Self, Start, false, 100, 100),
                new PlannerFighter(Enemy, Impact, true, 100, 100)
            }, marks ?? new HashSet<int>(), portals);

    private static C.ComboFrame Frame(int breed) => new(Fight, 5, 1, 2, Self, breed);

    private static C.ComboEvidence Evidence(int breed, SpellCastDescriptor source,
        SpellCastDescriptor? followup = null, int effect = 0)
        => new(TargetId: Enemy, SourceSpell: source, FollowupSpell: followup,
            EffectActionId: effect, TargetCell: Impact,
            ClientVersion: C.VerifiedClassClientVersion, SourceBreedId: breed,
            FollowupBreedId: breed,
            ObservedLearnedGrades: new Dictionary<int, int>
            {
                [source.SpellId] = 1,
                [followup?.SpellId ?? source.SpellId] = 1
            });

    private static C.ComboObservation Event(long order, C.ComboEventKind kind,
        int spell = 0, int cell = -1, int markId = 0, int action = 0,
        bool confirmed = true, long corr = 700, long source = Self)
        => new(order, Fight, 5, 1, source, Enemy, kind,
            SpellId: spell, Grade: spell > 0 ? 1 : 0, MarkId: markId,
            ActionId: action, ToCell: cell, Correlation: corr, ServerConfirmed: confirmed);

    private static E NewRogue(bool enabled = true, E.Limits? limits = null)
    {
        var result = E.Prepare(C.ComboKind.RogueWall, Frame(C.RogueBreedId),
            Evidence(C.RogueBreedId, Explobomb), Board(C.RogueBreedId),
            new[] { new V.Action(Explobomb, Entry) },
            executorLimits: limits, enabled: enabled);
        Assert.NotNull(result.Executor);
        return result.Executor!;
    }

    private static (E Executor, V.Snapshot Initial) NewPortal()
    {
        var initial = Board(C.EliotropeBreedId);
        var prepared = E.Prepare(C.ComboKind.EliotropeRedirect, Frame(C.EliotropeBreedId),
            Evidence(C.EliotropeBreedId, Portal, PortalHit, effect: 99), initial,
            new[] { new V.Action(Portal, Entry), new V.Action(PortalHit, Entry) },
            enabled: true);
        Assert.NotNull(prepared.Executor);
        return (prepared.Executor!, initial);
    }

    [Fact]
    public void Disabled_by_default_and_runtime_toggle_never_enable_dispatch()
    {
        var closed = NewRogue(enabled: false);
        var idle = closed.Inspect(Board(C.RogueBreedId), Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Disabled, idle.State);
        Assert.Null(idle.AdvisoryAction);
        Assert.False(idle.CanDispatch);
        closed.SetEnabled(true);
        var candidate = closed.Inspect(Board(C.RogueBreedId), Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Advisory, candidate.State);
        Assert.Equal(PlannerActionKind.Cast, candidate.AdvisoryAction!.Kind);
        Assert.False(candidate.CanDispatch);
        closed.SetEnabled(false);
        Assert.Equal(E.Phase.Disabled, closed.Inspect(Board(C.RogueBreedId),
            Array.Empty<C.ComboObservation>()).State);
    }

    [Fact]
    public void Single_action_observed_confirmation_is_not_a_send_or_a_combo_effect_proof()
    {
        var execution = NewRogue();
        Assert.Equal(E.Phase.Advisory, execution.Inspect(Board(C.RogueBreedId),
            Array.Empty<C.ComboObservation>()).State);
        var uncertain = Event(1, C.ComboEventKind.Cast, 13444, Entry, confirmed: false);
        var pending = execution.Inspect(Board(C.RogueBreedId, order: 1),
            new[] { uncertain });
        Assert.Equal(E.Phase.Pending, pending.State);
        Assert.Equal(0, pending.ConfirmedStages);
        Assert.Null(pending.AdvisoryAction);
        Assert.False(pending.CanDispatch);

        var seen = execution.Inspect(Board(C.RogueBreedId, order: 1),
            new[] { uncertain with { ServerConfirmed = true } });
        Assert.Equal(E.Phase.Confirmed, seen.State);
        Assert.Equal(1, seen.ConfirmedStages);
        Assert.False(seen.CanDispatch);
        Assert.Null(seen.AdvisoryAction);
        var replay = execution.Inspect(Board(C.RogueBreedId, order: 1),
            new[] { uncertain with { ServerConfirmed = true } });
        Assert.Equal(E.Phase.Confirmed, replay.State);
        Assert.False(replay.CanDispatch);
    }

    [Fact]
    public void Two_step_portal_sequence_requires_ordered_correlated_mark_and_followup_observations()
    {
        var (execution, initial) = NewPortal();
        var advice = execution.Inspect(initial, Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Advisory, advice.State);
        Assert.Equal(14574, advice.AdvisoryAction!.SpellId);

        var cast = Event(1, C.ComboEventKind.Cast, 14574, Entry);
        var mark = Event(2, C.ComboEventKind.MarkPlaced, cell: Entry,
            markId: 501, action: 1181);
        var active = Event(3, C.ComboEventKind.MarkActivated, markId: 501);
        var observed = initial with
        {
            Ap = 4, LastServerOrder = 3,
            MarkedCells = new HashSet<int> { Entry, Exit },
            Portals = new[]
            {
                new C.Portal(501, Entry, Self),
                new C.Portal(502, Exit, Self)
            }
        };
        Assert.Equal(E.Phase.Pending, execution.Inspect(
            observed with { LastServerOrder = 1 }, new[] { cast }).State);
        var ready = execution.Inspect(observed, new[] { cast, mark, active });
        Assert.Equal(E.Phase.Confirmed, ready.State);
        Assert.Equal(1, ready.ConfirmedStages);
        Assert.Equal(14579, ready.AdvisoryAction!.SpellId);
        Assert.Equal(Entry, ready.AdvisoryAction.Cell);
        Assert.False(ready.CanDispatch);

        var second = Event(4, C.ComboEventKind.Cast, 14579, Entry, confirmed: false);
        var pending = execution.Inspect(observed with { LastServerOrder = 4 },
            new[] { cast, mark, active, second });
        Assert.Equal(E.Phase.Pending, pending.State);
        Assert.Equal(1, pending.ConfirmedStages);
        Assert.False(pending.CanDispatch);
        var completed = execution.Inspect(observed with { LastServerOrder = 4 },
            new[] { cast, mark, active, second with { ServerConfirmed = true } });
        Assert.Equal(E.Phase.Confirmed, completed.State);
        Assert.Equal(2, completed.ConfirmedStages);
        Assert.Null(completed.AdvisoryAction);
        Assert.False(completed.CanDispatch);
    }

    [Fact]
    public void A_second_cast_cannot_retroactively_certify_a_portal_removed_before_or_after_it()
    {
        var (runner, start) = NewPortal();
        var cast = Event(1, C.ComboEventKind.Cast, 14574, Entry);
        var placed = Event(2, C.ComboEventKind.MarkPlaced, cell: Entry,
            markId: 501, action: 1181);
        var activated = Event(3, C.ComboEventKind.MarkActivated, markId: 501);
        var board = start with
        {
            Ap = 4, LastServerOrder = 3,
            MarkedCells = new HashSet<int> { Entry, Exit },
            Portals = new[]
            {
                new C.Portal(501, Entry, Self),
                new C.Portal(502, Exit, Self)
            }
        };
        Assert.Equal(E.Phase.Confirmed, runner.Inspect(board,
            new[] { cast, placed, activated }).State);
        var second = Event(5, C.ComboEventKind.Cast, 14579, Entry);
        var deactivated = Event(4, C.ComboEventKind.MarkDeactivated, markId: 501);
        Assert.Equal(E.Phase.Expired, runner.Inspect(board with { LastServerOrder = 5 },
            new[] { cast, placed, activated, deactivated, second }).State);
        Assert.Equal(E.Phase.Expired, runner.Inspect(board,
            new[] { cast, placed, activated }).State);

        var (other, initial) = NewPortal();
        Assert.Equal(E.Phase.Confirmed, other.Inspect(board,
            new[] { cast, placed, activated }).State);
        var laterOff = Event(5, C.ComboEventKind.MarkDeactivated, markId: 501);
        var earlierSecond = second with { Order = 4 };
        Assert.Equal(E.Phase.Expired, other.Inspect(initial with
        {
            Ap = 2, LastServerOrder = 5, MarkedCells = new HashSet<int> { Exit },
            Portals = new[]
            {
                new C.Portal(501, Entry, Self, false),
                new C.Portal(502, Exit, Self)
            }
        }, new[] { cast, placed, activated, earlierSecond, laterOff }).State);
    }

    [Fact]
    public void Refuses_reconnect_PID_reuse_new_map_turn_and_negative_or_missing_resource()
    {
        var board = Board(C.RogueBreedId);
        var (kind, frame, evidence) = (C.ComboKind.RogueWall,
            Frame(C.RogueBreedId), Evidence(C.RogueBreedId, Explobomb));
        foreach (var invalid in new[]
        {
            board with { Generation = 6 },
            board with { MapGeneration = 42 },
            board with { FightId = Fight + 1 },
            board with { Turn = 2 },
            board with { ActorId = Self + 1 },
            board with { Ap = null },
            board with { Mp = null },
            board with { LastServerOrder = -1 }
        })
        {
            var runner = NewRogue();
            Assert.Equal(E.Phase.Expired, runner.Inspect(invalid,
                Array.Empty<C.ComboObservation>()).State);
            // Once expired, returning to the old PID/epoch cannot revive it.
            Assert.Equal(E.Phase.Expired, runner.Inspect(board,
                Array.Empty<C.ComboObservation>()).State);
        }
        Assert.Null(E.Prepare(kind, frame, evidence, board with { Ap = null },
            new[] { new V.Action(Explobomb, Entry) }).Executor);
        Assert.Null(E.Prepare(kind, frame, evidence, board with { PendingAction = null },
            new[] { new V.Action(Explobomb, Entry) }).Executor);
    }

    [Fact]
    public void Missing_ACK_malformed_order_foreign_owner_truncated_history_are_never_promoted()
    {
        var badFirst = Event(1, C.ComboEventKind.Cast, 13444, Entry, confirmed: false);
        foreach (var malformed in new[]
        {
            new[] { badFirst with { Source = 900 } },
            new[] { badFirst with { Grade = 2 } },
            new[] { badFirst with { ToCell = Start } },
            new[] { badFirst, badFirst with { Order = 1 } },
            new[] { badFirst with { Generation = 4 } }
        })
        {
            var worker = NewRogue();
            var review = worker.Inspect(Board(C.RogueBreedId, order: malformed.Length),
                malformed);
            Assert.NotEqual(E.Phase.Advisory, review.State);
            Assert.False(review.CanDispatch);
        }
        var expired = NewRogue();
        Assert.Equal(E.Phase.Expired, expired.Inspect(Board(C.RogueBreedId),
            Array.Empty<C.ComboObservation>(), streamTruncated: true).State);
        var noAck = NewRogue();
        var waiting = noAck.Inspect(Board(C.RogueBreedId) with { PendingAction = true },
            Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Pending, waiting.State);
        Assert.Null(waiting.AdvisoryAction);
    }

    [Fact]
    public void Observation_and_review_budgets_expire_without_invoking_transport()
    {
        var one = NewRogue(limits: new E.Limits(MaxReviews: 1));
        Assert.Equal(E.Phase.Advisory, one.Inspect(Board(C.RogueBreedId),
            Array.Empty<C.ComboObservation>()).State);
        Assert.Equal(E.Phase.Expired, one.Inspect(Board(C.RogueBreedId),
            Array.Empty<C.ComboObservation>()).State);
        var order = NewRogue(limits: new E.Limits(MaxOrderAdvance: 1));
        Assert.Equal(E.Phase.Expired, order.Inspect(Board(C.RogueBreedId, order: 2),
            new[] { Event(2, C.ComboEventKind.Effect) }).State);
        Assert.Null(E.Prepare(C.ComboKind.RogueWall, Frame(C.RogueBreedId),
            Evidence(C.RogueBreedId, Explobomb), Board(C.RogueBreedId),
            new[] { new V.Action(Explobomb, Entry) },
            executorLimits: new E.Limits(MaxReviews: 0)).Executor);
    }

    [Fact]
    public void Multi_turn_continuation_requires_new_observed_plan_and_cannot_reuse_old_turn_receipts()
    {
        var execution = NewRogue();
        var turnTwo = Board(C.RogueBreedId) with { Turn = 2 };
        var expired = execution.Inspect(turnTwo, Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Expired, expired.State);
        Assert.False(expired.CanDispatch);

        // A fresh plan can be proposed in turn 2 after a fresh board/grades
        // observation, but it has no inherited class effect/cast ACK.
        var next = E.Prepare(C.ComboKind.RogueWall, Frame(C.RogueBreedId),
            Evidence(C.RogueBreedId, Explobomb), turnTwo,
            new[] { new V.Action(Explobomb, Entry) }, enabled: true);
        Assert.NotNull(next.Executor);
        var advice = next.Executor!.Inspect(turnTwo, Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Advisory, advice.State);
        Assert.Equal(0, advice.ConfirmedStages);
        Assert.False(advice.CanDispatch);
        // A stale turn-1 event cannot count as the turn-2 confirmation.
        var stale = Event(1, C.ComboEventKind.Cast, 13444, Entry);
        Assert.Equal(E.Phase.Expired, next.Executor.Inspect(
            turnTwo with { LastServerOrder = 1 }, new[] { stale }).State);
    }
}
