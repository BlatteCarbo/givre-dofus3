using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;
using Xunit;
using C = GivreClone.App.Services.Combat.ClassMechanics;
using V = GivreClone.App.Services.Combat.VerifiedTacticalSequence;

namespace GivreClone.Tests;

/// <summary>
/// Pure, synthetic event fixtures. They assert the policy that a real inbound
/// adapter must eventually satisfy; the current FightMark decoder supplies
/// identity/owner but cannot supply cast correlations or action IDs by itself.
/// IDs in accepted examples are pinned in the 3.7.13.17 class export.
/// </summary>
public sealed class VerifiedTacticalSequenceTests
{
    private const long Self = 77;
    private const long Enemy = -9;
    private const long Fight = 990;
    private static readonly int Start = Cell(10, 0);
    private static readonly int Entry = Cell(12, 0);
    private static readonly int Exit = Cell(10, 9);
    private static readonly int Impact = Cell(12, 9);
    private static readonly PlannerCell[] Cells = Enumerable.Range(0, MapPathfinder.CellCount)
        .Select(i => new PlannerCell(i, true, false, ImmutableArray<int>.Empty)).ToArray();

    private static int Cell(int x, int y) => MapPathfinder.CellFromMapPoint(x, y);

    private static SpellCastDescriptor Spell(int id, int action, SpellEffectKind kind,
        int ap = 2, int flags = 4, int? dice = null)
        => new(id, 1, ap, 1, 10, flags, false, 2, 2, 0, 0, "",
            ImmutableArray.Create(new SpellEffectDescriptor(action, kind, 0, 0, 0, 0,
                "a,A", 'P', 1, 0, DiceNum: dice ?? 0)));

    private static readonly SpellCastDescriptor Bomb = Spell(13444, 1008,
        SpellEffectKind.Summon, flags: 12, dice: C.VerifiedExplobombTemplate);
    private static readonly SpellCastDescriptor Portal = Spell(14574, 1181,
        SpellEffectKind.Unknown, flags: 12);
    private static readonly SpellCastDescriptor PortalHit = Spell(14579, 99,
        SpellEffectKind.Damage);
    private static readonly SpellCastDescriptor XelorSwap = Spell(13285, 8,
        SpellEffectKind.Swap, flags: 20);

    private static C.ComboFrame Frame(int breed) => new(Fight, 5, 1, 2, Self, breed);

    private static V.Snapshot Snapshot(int breed, int actorCell = -1,
        int enemyCell = -1, int? ap = 6, int? mp = 3, long order = 0,
        IReadOnlySet<int>? marks = null, IReadOnlyList<C.Portal>? portals = null,
        IReadOnlyList<PlannerCell>? cells = null, IReadOnlyList<PlannerFighter>? fighters = null,
        Func<int, int, bool?>? connection = null)
        => new(Fight, 5, 42, 1, Self, breed, actorCell < 0 ? Start : actorCell,
            ap, mp, order, false, cells ?? Cells, fighters ?? new[]
            {
                new PlannerFighter(Self, actorCell < 0 ? Start : actorCell,
                    false, 100, 100),
                new PlannerFighter(Enemy, enemyCell < 0 ? Impact : enemyCell,
                    true, 100, 100)
            }, marks ?? new HashSet<int>(), portals, connection);

    private static C.ComboEvidence Evidence(int breed, SpellCastDescriptor source,
        SpellCastDescriptor? followup = null, int action = 0, int state = 0,
        int targetCell = -1)
        => new(TargetId: Enemy, SourceSpell: source, FollowupSpell: followup,
            EffectActionId: action, StateId: state,
            FirstCell: Start, SecondCell: Entry, TargetCell: targetCell,
            ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: breed, FollowupBreedId: breed,
            ObservedLearnedGrades: new Dictionary<int, int>
            {
                [source.SpellId] = source.Grade,
                [followup?.SpellId ?? source.SpellId] = followup?.Grade ?? source.Grade
            });

    private static C.ComboObservation Event(long order, C.ComboEventKind kind,
        long correlation = 700, long source = Self, long subject = 0,
        int spell = 0, int action = 0, int mark = 0, int to = -1, int from = -1,
        bool confirmed = true)
        => new(order, Fight, 5, 1, source, subject, kind,
            SpellId: spell, Grade: spell == 0 ? 0 : 1, ActionId: action,
            MarkId: mark, ToCell: to, FromCell: from,
            Correlation: correlation, ServerConfirmed: confirmed);

    private static V.Plan BombPlan(V.Snapshot? snapshot = null,
        V.Action? action = null, V.Options? options = null)
    {
        var board = snapshot ?? Snapshot(C.RogueBreedId);
        var planned = action ?? new V.Action(Bomb, Entry);
        var result = V.Prepare(C.ComboKind.RogueWall, Frame(C.RogueBreedId),
            Evidence(C.RogueBreedId, Bomb), board, new[] { planned }, options);
        Assert.True(result.Accepted, result.Reason);
        return result.Value!;
    }

    [Fact]
    public void Rogue_known_bomb_cast_is_one_action_only_and_never_implies_detonation()
    {
        var plan = BombPlan();
        var ready = V.Next(plan, Snapshot(C.RogueBreedId), Array.Empty<C.ComboObservation>());
        Assert.True(ready.Ready, ready.Reason);
        Assert.Equal(PlannerActionKind.Cast, ready.Instruction!.Kind);
        Assert.Equal(13444, ready.Instruction.SpellId);
        Assert.Equal(Entry, ready.Instruction.Cell);

        var ack = Event(1, C.ComboEventKind.Cast, spell: 13444, to: Entry);
        var completed = V.Next(plan, Snapshot(C.RogueBreedId, order: 1), new[] { ack });
        Assert.False(completed.Ready);
        Assert.Equal(1, completed.Stage);
        Assert.Null(completed.Instruction);
    }

    [Fact]
    public void Unconfirmed_cast_cannot_be_retried_or_unlock_the_followup()
    {
        var plan = BombPlan();
        var unknown = Event(1, C.ComboEventKind.Cast, spell: 13444,
            to: Entry, confirmed: false);
        Assert.False(V.Next(plan, Snapshot(C.RogueBreedId, order: 1),
            new[] { unknown }).Ready);
        Assert.False(V.Next(plan, Snapshot(C.RogueBreedId, order: 1),
            new[] { unknown with { Correlation = 0, ServerConfirmed = true } }).Ready);
    }

    [Fact]
    public void Rogue_rejects_unknown_AP_MP_range_line_of_sight_mark_and_invisible_fighter()
    {
        var evidence = Evidence(C.RogueBreedId, Bomb);
        var frame = Frame(C.RogueBreedId);
        V.PlanResult Try(V.Snapshot b, V.Action? action = null)
            => V.Prepare(C.ComboKind.RogueWall, frame, evidence, b,
                new[] { action ?? new V.Action(Bomb, Entry) });
        Assert.False(Try(Snapshot(C.RogueBreedId, ap: null)).Accepted);
        Assert.False(Try(Snapshot(C.RogueBreedId, mp: null)).Accepted);
        Assert.False(Try(Snapshot(C.RogueBreedId) with { PendingAction = null }).Accepted);
        Assert.False(Try(Snapshot(C.RogueBreedId) with { PendingAction = true }).Accepted);
        Assert.False(Try(Snapshot(C.RogueBreedId, ap: 1)).Accepted);
        Assert.False(Try(Snapshot(C.RogueBreedId), new V.Action(Bomb, Impact)).Accepted);
        Assert.False(Try(Snapshot(C.RogueBreedId,
            cells: Cells.Where(c => c.Id != Cell(11, 0)).ToArray())).Accepted);
        Assert.False(Try(Snapshot(C.RogueBreedId,
            cells: Cells.Select(c => c.Id == Cell(11, 0) ? c with { BlocksLos = true } : c)
                .ToArray())).Accepted);
        Assert.False(Try(Snapshot(C.RogueBreedId,
            marks: new HashSet<int> { Entry })).Accepted);
        Assert.False(Try(Snapshot(C.RogueBreedId,
            fighters: new[] { new PlannerFighter(Self, Start, false, 100, 100),
                new PlannerFighter(Enemy, Impact, true, 100, 100,
                    PositionCertainty: FighterPositionCertainty.LastKnown) })).Accepted);
        Assert.False(Try(Snapshot(C.RogueBreedId), new V.Action(Bomb, -1)).Accepted);
        Assert.False(Try(Snapshot(C.RogueBreedId), new V.Action(Bomb, 560)).Accepted);
    }

    [Fact]
    public void Map_generation_fight_turn_and_server_order_cannot_be_reused()
    {
        var board = Snapshot(C.RogueBreedId);
        var plan = BombPlan(board);
        var stream = Array.Empty<C.ComboObservation>();
        Assert.False(V.Next(plan, board with { MapGeneration = 43 }, stream).Ready);
        Assert.False(V.Next(plan, board with { Generation = 6 }, stream).Ready);
        Assert.False(V.Next(plan, board with { FightId = 991 }, stream).Ready);
        Assert.False(V.Next(plan, board with { Turn = 2 }, stream).Ready);
        Assert.False(V.Next(plan, board with { Ap = null }, stream).Ready);
        Assert.False(V.Next(plan, board with { Mp = null }, stream).Ready);
        Assert.False(V.Next(plan, board with { PendingAction = null }, stream).Ready);
        Assert.False(V.Next(plan, board with { PendingAction = true }, stream).Ready);
        Assert.False(V.Next(plan, board with { LastServerOrder = -1 }, stream).Ready);
        Assert.False(V.Next(plan, board with { LastServerOrder = 2 }, stream).Ready);
    }

    [Fact]
    public void Xelor_swap_checks_known_target_identity_and_does_not_assume_telefrag_on_cast()
    {
        var board = Snapshot(C.XelorBreedId, enemyCell: Entry);
        var evidence = Evidence(C.XelorBreedId, XelorSwap, state: C.StateTelefrag);
        var candidate = new V.Action(XelorSwap, Entry, Enemy);
        var result = V.Prepare(C.ComboKind.XelorTelefrag, Frame(C.XelorBreedId),
            evidence, board, new[] { candidate });
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal(PlannerActionKind.Cast,
            V.Next(result.Value!, board, Array.Empty<C.ComboObservation>()).Instruction!.Kind);
        // A cast ACK alone does not establish either movement or a state.
        var ack = Event(1, C.ComboEventKind.Cast, spell: 13285, to: Entry);
        Assert.False(V.Next(result.Value!, board with { LastServerOrder = 1 },
            new[] { ack }).Ready);
        Assert.False(V.Prepare(C.ComboKind.XelorTelefrag, Frame(C.XelorBreedId),
            evidence, board, new[] { candidate with { TargetActorId = Self } }).Accepted);
        Assert.False(V.Prepare(C.ComboKind.XelorTelefrag, Frame(C.XelorBreedId),
            evidence, board with { Fighters = new[]
            {
                new PlannerFighter(Self, Start, false, 100, 100),
                new PlannerFighter(Enemy, Impact, true, 100, 100)
            } }, new[] { candidate }).Accepted);
    }

    [Fact]
    public void Movement_returns_separate_instruction_and_requires_server_move_event_before_cast()
    {
        var adjacent = MapPathfinder.OrthoReachable(Start, 1, _ => true)
            .Keys.First(c => c != Start && c != Entry);
        var initial = Snapshot(C.RogueBreedId, connection: (_, _) => true);
        var plan = BombPlan(initial, new V.Action(Bomb, Entry, StandCell: adjacent),
            new V.Options(AllowMovement: true));
        var move = V.Next(plan, initial, Array.Empty<C.ComboObservation>());
        Assert.True(move.Ready, move.Reason);
        Assert.Equal(PlannerActionKind.Move, move.Instruction!.Kind);
        Assert.Equal(1, move.Instruction.Mp);
        var after = Snapshot(C.RogueBreedId, actorCell: adjacent, order: 1);
        Assert.False(V.Next(plan, after, Array.Empty<C.ComboObservation>()).Ready);
        var proven = Event(1, C.ComboEventKind.Move, subject: Self,
            from: Start, to: adjacent);
        var next = V.Next(plan, after, new[] { proven });
        Assert.True(next.Ready, next.Reason);
        Assert.Equal(PlannerActionKind.Cast, next.Instruction!.Kind);
        Assert.False(V.Prepare(C.ComboKind.RogueWall, Frame(C.RogueBreedId),
            Evidence(C.RogueBreedId, Bomb), initial with { Connection = (_, _) => null },
            new[] { new V.Action(Bomb, Entry, StandCell: adjacent) },
            new V.Options(AllowMovement: true)).Accepted);
    }

    private static (V.Plan Plan, V.Snapshot Board) PortalPlan()
    {
        var board = Snapshot(C.EliotropeBreedId, enemyCell: Impact);
        var e = Evidence(C.EliotropeBreedId, Portal, PortalHit, action: 99,
            targetCell: Impact);
        var result = V.Prepare(C.ComboKind.EliotropeRedirect, Frame(C.EliotropeBreedId),
            e, board, new[] { new V.Action(Portal, Entry), new V.Action(PortalHit, Entry) });
        Assert.True(result.Accepted, result.Reason);
        return (result.Value!, board);
    }

    [Fact]
    public void Eliotrope_followup_requires_cast_ack_then_owned_active_portal_marks_and_projected_impact()
    {
        var (plan, initial) = PortalPlan();
        var first = V.Next(plan, initial, Array.Empty<C.ComboObservation>());
        Assert.True(first.Ready, first.Reason);
        Assert.Equal(PlannerActionKind.Cast, first.Instruction!.Kind);
        var stream = new[]
        {
            Event(1, C.ComboEventKind.Cast, spell: 14574, to: Entry),
            Event(2, C.ComboEventKind.MarkPlaced, action: 1181, mark: 501, to: Entry),
            Event(3, C.ComboEventKind.MarkActivated, mark: 501)
        };
        var observed = initial with
        {
            Ap = 4, LastServerOrder = 3,
            MarkedCells = new HashSet<int> { Entry, Exit },
            Portals = new[] { new C.Portal(501, Entry, Self), new C.Portal(502, Exit, Self) }
        };
        var ready = V.Next(plan, observed, stream);
        Assert.True(ready.Ready, ready.Reason);
        Assert.Equal(1, ready.Stage);
        Assert.Equal(PlannerActionKind.Cast, ready.Instruction!.Kind);
        Assert.Equal(14579, ready.Instruction.SpellId);
        Assert.Equal(Entry, ready.Instruction.Cell);

        Assert.False(V.Next(plan, observed, stream[..^1]).Ready);
        Assert.False(V.Next(plan, observed,
            stream.Select((x, i) => i == 0 ? x with { Correlation = 0 } : x).ToArray()).Ready);
        Assert.False(V.Next(plan, observed,
            stream.Select((x, i) => i == 0 ? x with { Generation = 4 } : x).ToArray()).Ready);
        Assert.False(V.Next(plan, observed,
            stream.Select((x, i) => i == 1 ? x with { Correlation = 0 } : x).ToArray()).Ready);
        Assert.False(V.Next(plan, observed,
            stream.Select((x, i) => i == 1 ? x with { Source = 999 } : x).ToArray()).Ready);
        Assert.False(V.Next(plan, observed,
            stream.Select((x, i) => i == 1 ? x with { ActionId = 0 } : x).ToArray()).Ready);
        Assert.False(V.Next(plan, observed,
            stream.Select((x, i) => i == 2 ? x with { Order = 1 } : x).ToArray()).Ready);
        Assert.False(V.Next(plan, observed,
            new[] { stream[1] with { Order = 1 }, stream[0] with { Order = 2 },
                stream[2] }).Ready);
        Assert.False(V.Next(plan, observed with
        {
            Portals = new[] { new C.Portal(501, Entry, 99), new C.Portal(502, Exit, Self) }
        }, stream).Ready);
        Assert.False(V.Next(plan, observed with
        {
            Portals = new[] { new C.Portal(501, Entry, Self), new C.Portal(502, Exit, Self, false) }
        }, stream).Ready);
        Assert.False(V.Next(plan, observed with { MarkedCells = null }, stream).Ready);
        Assert.False(V.Next(plan, observed with { MapGeneration = 43 }, stream).Ready);
        Assert.False(V.Next(plan, observed with { Ap = 1 }, stream).Ready);
        var deactivated = stream.Append(Event(4, C.ComboEventKind.MarkDeactivated,
            mark: 501)).ToArray();
        Assert.False(V.Next(plan, observed with { LastServerOrder = 4 }, deactivated).Ready);
        var alreadyCast = stream.Append(Event(4, C.ComboEventKind.Cast,
            spell: 14579, to: Entry)).ToArray();
        Assert.False(V.Next(plan, observed with { LastServerOrder = 4 }, alreadyCast).Ready);
    }

    [Fact]
    public void Missing_installed_3_7_13_17_specific_grade_never_creates_Feca_Ecaflip_Sacrier_combo()
    {
        foreach (var (kind, breed) in new[]
        {
            (C.ComboKind.FecaGlyph, C.FecaBreedId),
            (C.ComboKind.EcaflipCard, C.EcaflipBreedId),
            (C.ComboKind.SacrierSuffering, C.SacrierBreedId)
        })
        {
            var board = Snapshot(breed);
            var missing = new C.ComboEvidence(TargetId: Enemy,
                ClientVersion: C.VerifiedClassClientVersion, SourceBreedId: breed,
                ObservedLearnedGrades: new Dictionary<int, int>());
            // These three class combos have no pinned specific spell + state
            // join in the current N01 evidence. Zero is unknown, not a spell.
            var unknown = Spell(0, 950, SpellEffectKind.Buff);
            var proposal = V.Prepare(kind, Frame(breed), missing with
            {
                SourceSpell = unknown, StateId = C.StateTelefrag
            }, board, new[] { new V.Action(unknown, Entry) });
            Assert.False(proposal.Accepted);
        }
    }

    [Fact]
    public void Source_grade_breed_client_version_action_and_budget_are_not_guessed()
    {
        var (plan, board) = PortalPlan();
        var source = plan.Actions[0].Spell;
        var evidence = plan.Evidence;
        V.PlanResult Try(C.ComboEvidence e, IReadOnlyList<V.Action> actions)
            => V.Prepare(C.ComboKind.EliotropeRedirect, plan.Frame, e, board, actions);
        Assert.False(Try(evidence with { ClientVersion = "3.7.12" }, plan.Actions).Accepted);
        Assert.False(Try(evidence with { SourceBreedId = C.XelorBreedId }, plan.Actions).Accepted);
        Assert.False(Try(evidence with { ObservedLearnedGrades = null }, plan.Actions).Accepted);
        Assert.False(Try(evidence with { ObservedLearnedGrades = new Dictionary<int, int>
        {
            [14574] = 2, [14579] = 1
        } }, plan.Actions).Accepted);
        Assert.False(Try(evidence, new[] { new V.Action(source with { Uncertain = true }, Entry) }).Accepted);
        Assert.False(Try(evidence, new[] { new V.Action(PortalHit, Entry) }).Accepted);
        Assert.False(V.Prepare(C.ComboKind.EliotropeRedirect, plan.Frame, evidence,
            board with { Ap = 3 }, plan.Actions).Accepted);
        Assert.False(V.Prepare(C.ComboKind.EliotropeRedirect, plan.Frame, evidence,
            board, plan.Actions, new V.Options(MaxSteps: 1)).Accepted);
        Assert.False(V.Prepare(C.ComboKind.EliotropeRedirect, plan.Frame, evidence,
            board with { BreedId = C.FecaBreedId }, plan.Actions).Accepted);
        Assert.False(V.Prepare(C.ComboKind.EliotropeRedirect, plan.Frame, evidence,
            board with { Generation = 6 }, plan.Actions).Accepted);
    }
}
