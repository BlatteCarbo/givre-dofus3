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
/// Real tactical API entry points and bounded replay, with OFFLINE fixtures.
/// Fixture ServerConfirmed flags are NOT real network ACKs. Live ingress must
/// use InspectDiagnostic and never promote these diagnostic rows to sends.
/// Feca/Ecaflip/Sacrier lack an independently pinned 3.7.13.17 spell/state
/// join in N01: negative tests must not manufacture such spell identities.
/// </summary>
public sealed class CombatMultiTurnCausalityV17Tests
{
    private readonly ITestOutputHelper _output;
    public CombatMultiTurnCausalityV17Tests(ITestOutputHelper output) => _output = output;

    private const long Actor = 77, Enemy = -9, Fight = 1717;
    private const int Generation = 9;
    private static readonly int Start = Cell(10, 0);
    private static readonly int Entry = Cell(12, 0);
    private static readonly int Exit = Cell(10, 9);
    private static readonly int Impact = Cell(12, 9);
    private static readonly PlannerCell[] Cells = Enumerable.Range(0, MapPathfinder.CellCount)
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
            false, Cells,
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

    private static C.ComboObservation Row(int order, int turn, C.ComboEventKind kind,
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
        C.ComboObservation[] Rows) Rogue()
    {
        int b1 = Cell(10, 0), b2 = Cell(13, 0);
        int wall = Cell(11, 0), wall2 = Cell(12, 0);
        var evidence = new C.ComboEvidence(TargetId: Enemy,
            SourceSpell: Bomb, FollowupSpell: Blast, FirstMarkId: 501,
            FirstBombId: 101, SecondBombId: 102, TargetCell: wall,
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
            Row(1, 1, C.ComboEventKind.Cast, spell: 13444, to: b1, correlation: 71),
            Row(2, 1, C.ComboEventKind.Summon, subject: 101, spell: 13444,
                template: C.VerifiedExplobombTemplate, correlation: 71),
            Row(3, 2, C.ComboEventKind.Cast, spell: 13444, to: b2, correlation: 72),
            Row(4, 2, C.ComboEventKind.Summon, subject: 102, spell: 13444,
                template: C.VerifiedExplobombTemplate, correlation: 72),
            Row(5, 2, C.ComboEventKind.WallActivated,
                mark: 501, template: C.VerifiedExplobombWall),
            Row(6, 3, C.ComboEventKind.Move, source: Enemy, to: wall),
            Row(7, 3, C.ComboEventKind.Cast, spell: 13455,
                to: wall, correlation: 73),
            Row(8, 3, C.ComboEventKind.DetonationConfirmed, subject: 101,
                spell: 13455, template: C.VerifiedExplobombTemplate, correlation: 73),
            Row(9, 3, C.ComboEventKind.Effect, spell: 13455,
                action: 99, to: wall, correlation: 73)
        });
    }

    private static (C.ComboFrame Frame, C.ComboEvidence Evidence,
        C.ComboObservation[] Rows) Eliotrope()
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
            Row(1, 1, C.ComboEventKind.Cast, spell: 14574,
                to: Entry, correlation: 81),
            Row(2, 1, C.ComboEventKind.MarkPlaced, mark: 501,
                action: 1181, to: Entry, correlation: 81),
            Row(3, 2, C.ComboEventKind.Cast, spell: 14574,
                to: Exit, correlation: 82),
            Row(4, 2, C.ComboEventKind.MarkPlaced, mark: 502,
                action: 1181, to: Exit, correlation: 82),
            Row(5, 2, C.ComboEventKind.MarkActivated, mark: 501, to: Entry),
            Row(6, 2, C.ComboEventKind.MarkActivated, mark: 502, to: Exit),
            Row(7, 3, C.ComboEventKind.Cast, spell: 14579,
                to: Entry, correlation: 83),
            Row(8, 3, C.ComboEventKind.Effect, spell: 14579,
                action: 99, to: Impact, correlation: 83)
        });
    }

    private static (C.ComboFrame Frame, C.ComboEvidence Evidence,
        C.ComboObservation[] Rows) Xelor()
    {
        var evidence = new C.ComboEvidence(TargetId: Enemy,
            SourceSpell: Swap, FirstCell: Start, SecondCell: Entry,
            TargetCell: Entry, StateId: C.StateTelefrag,
            TargetIsEnemy: true, SourceCorrelation: 91,
            ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: C.XelorBreedId,
            ObservedLearnedGrades: new Dictionary<int, int> { [Swap.SpellId] = 1 });
        return (Frame(C.XelorBreedId), evidence, new[]
        {
            Row(1, 1, C.ComboEventKind.Cast,
                spell: 13285, to: Entry, correlation: 91),
            Row(2, 2, C.ComboEventKind.Move, subject: Actor,
                from: Start, to: Entry, correlation: 91),
            Row(3, 2, C.ComboEventKind.Move, subject: Enemy,
                from: Entry, to: Start, correlation: 91),
            Row(4, 2, C.ComboEventKind.StateAdded,
                state: C.StateTelefrag, correlation: 91)
        });
    }

    [Theory]
    [InlineData(C.RogueBreedId, C.ComboKind.RogueWall)]
    [InlineData(C.EliotropeBreedId, C.ComboKind.EliotropeRedirect)]
    [InlineData(C.XelorBreedId, C.ComboKind.XelorTelefrag)]
    public void True_decision_to_diagnostic_ingress_never_confirms_a_forged_cast(
        int breed, C.ComboKind kind)
    {
        var spell = breed == C.RogueBreedId ? Bomb
            : breed == C.EliotropeBreedId ? Portal : Swap;
        var facts = Facts(breed, spell);
        var selected = E.PrepareObservedDecision(facts, enabled: true);
        Assert.True(selected.Accepted, selected.Reason);
        Assert.Equal(kind, selected.Kind);
        Assert.False(selected.CanDispatch);
        var pending = selected.Executor!.InspectDiagnostic(facts.Snapshot,
            Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Pending, pending.State);
        Assert.False(pending.CanDispatch);
        Assert.Null(pending.AdvisoryAction);

        var alleged = Row(1, 1, C.ComboEventKind.Cast,
            spell: spell.SpellId, to: selected.Preview!.Cell, correlation: 71);
        var fakeAck = selected.Executor.InspectDiagnostic(facts.Snapshot with
        {
            LastServerOrder = 1
        }, new[] { alleged });
        Assert.Equal(0, fakeAck.ConfirmedStages);
        Assert.Equal(E.Phase.Pending, fakeAck.State);
        Assert.False(fakeAck.CanDispatch);
        Assert.Null(fakeAck.AdvisoryAction);
    }

    [Theory]
    [InlineData(C.RogueBreedId)]
    [InlineData(C.EliotropeBreedId)]
    [InlineData(C.XelorBreedId)]
    public void Diagnostic_rechecks_AP_MP_turn_map_actor_and_unknown_status(int breed)
    {
        var spell = breed == C.RogueBreedId ? Bomb
            : breed == C.EliotropeBreedId ? Portal : Swap;
        var facts = Facts(breed, spell);
        foreach (var invalid in new[]
        {
            facts.Snapshot with { Ap = 5 },
            facts.Snapshot with { Mp = 2 },
            facts.Snapshot with { MapGeneration = 20 },
            facts.Snapshot with { Generation = Generation + 1 },
            facts.Snapshot with { ActorId = Actor + 1 },
            facts.Snapshot with { Turn = 2 },
            facts.Snapshot with { ActorCell = Start + 1 }
        })
        {
            var selected = E.PrepareObservedDecision(facts, enabled: true);
            Assert.True(selected.Accepted, selected.Reason);
            var review = selected.Executor!.InspectDiagnostic(invalid,
                Array.Empty<C.ComboObservation>());
            Assert.Equal(E.Phase.Expired, review.State);
            Assert.False(review.CanDispatch);
            Assert.Null(review.AdvisoryAction);
        }
    }

    [Fact]
    public void Diagnostic_blocks_new_line_of_sight_obstacle_without_a_transport_receipt()
    {
        // MinRange is only synthetic board geometry for this test; spell ID,
        // grade and action are the pinned Rogue class catalog join.
        var facts = Facts(C.RogueBreedId, Bomb with { MinRange = 3 });
        var selection = E.PrepareObservedDecision(facts, enabled: true);
        Assert.True(selection.Accepted, selection.Reason);
        var line = MapPathfinder.LineCells(Start, selection.Preview!.Cell).ToArray();
        Assert.True(line.Length > 2);
        var blocker = line[1];
        var changed = Cells.Select(c => c.Id == blocker
            ? c with { BlocksLos = true } : c).ToArray();
        var review = selection.Executor!.InspectDiagnostic(facts.Snapshot with
        {
            Cells = changed
        }, Array.Empty<C.ComboObservation>());
        Assert.Equal(E.Phase.Expired, review.State);
        Assert.False(review.CanDispatch);
    }

    [Fact]
    public void Trusted_multiturn_Roublard_requires_unique_target_effect_and_revokes_removal()
    {
        var (frame, evidence, rows) = Rogue();
        Assert.True(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, rows, observedAsOfTurn: 3).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, rows.Append(rows[^1] with { Order = 10 }).ToArray(),
            observedAsOfTurn: 3).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, rows.Append(Row(10, 4,
                C.ComboEventKind.MarkRemoved, mark: evidence.FirstMarkId,
                confirmed: false)).ToArray(), observedAsOfTurn: 4).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.RogueWall,
            frame, evidence, rows.Select(e => e.Order == 9
                ? e with { ToCell = Start } : e).ToArray(),
            observedAsOfTurn: 3).Complete);
    }

    [Fact]
    public void Trusted_multiturn_Eliotrope_refuses_lost_portal_or_different_effect()
    {
        var (frame, evidence, rows) = Eliotrope();
        Assert.True(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
            frame, evidence, rows, observedAsOfTurn: 3).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
            frame, evidence, rows.Append(Row(9, 4,
                C.ComboEventKind.PortalDeactivated, mark: 502)).ToArray(),
            observedAsOfTurn: 4).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.EliotropeRedirect,
            frame, evidence, rows.Select(e => e.Order == 8
                ? e with { Correlation = 1 } : e).ToArray(),
            observedAsOfTurn: 3).Complete);
    }

    [Fact]
    public void Trusted_Xelor_state_and_both_moves_need_same_attributed_effect_turn()
    {
        var (frame, evidence, rows) = Xelor();
        Assert.True(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            frame, evidence, rows, observedAsOfTurn: 2).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            frame, evidence, rows.Select(e => e.Order == 3
                ? e with { Turn = 1 } : e).ToArray(),
            observedAsOfTurn: 2).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            frame, evidence, rows.Select(e => e.Order == 4
                ? e with { Correlation = 12 } : e).ToArray(),
            observedAsOfTurn: 2).Complete);
        Assert.False(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
            frame, evidence, rows.Append(Row(5, 3, C.ComboEventKind.StateRemoved,
                state: evidence.StateId, confirmed: false)).ToArray(),
            observedAsOfTurn: 3).Complete);
    }

    [Theory]
    [InlineData(C.FecaBreedId, C.ComboKind.FecaGlyph)]
    [InlineData(C.EcaflipBreedId, C.ComboKind.EcaflipCard)]
    [InlineData(C.SacrierBreedId, C.ComboKind.SacrierSuffering)]
    public void Feca_Ecaflip_Sacrier_cannot_borrow_another_class_catalog_or_ACK(
        int breed, C.ComboKind kind)
    {
        var facts = Facts(breed, Bomb);
        var selection = E.PrepareObservedDecision(facts, enabled: true);
        Assert.False(selection.Accepted);
        Assert.False(selection.CanDispatch);
        var spoof = new C.ComboEvidence(TargetId: Enemy,
            SourceSpell: Bomb, FollowupSpell: Blast,
            ClientVersion: C.VerifiedClassClientVersion,
            SourceBreedId: breed, FollowupBreedId: breed,
            ObservedLearnedGrades: new Dictionary<int, int> { [Bomb.SpellId] = 1 });
        Assert.False(C.VerifyObservedCombo(kind, Frame(breed), spoof,
            Rogue().Rows, observedAsOfTurn: 3).Complete);
    }

    [Fact]
    public void Bounded_GC_measurement_on_diagnostic_and_strict_replay_is_offline_only()
    {
        var facts = Facts(C.RogueBreedId, Bomb);
        var selection = E.PrepareObservedDecision(facts, enabled: true,
            executorLimits: new E.Limits(MaxReviews: 64));
        Assert.True(selection.Accepted, selection.Reason);
        var (frame, evidence, rows) = Xelor();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 64; i++)
        {
            var review = selection.Executor!.InspectDiagnostic(facts.Snapshot,
                Array.Empty<C.ComboObservation>());
            Assert.Equal(E.Phase.Pending, review.State);
            Assert.False(review.CanDispatch);
        }
        for (int i = 0; i < 32; i++)
            Assert.True(C.VerifyObservedCombo(C.ComboKind.XelorTelefrag,
                frame, evidence, rows, observedAsOfTurn: 2).Complete);
        sw.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        _output.WriteLine($"V17 offline 64 InspectDiagnostic + 32 strict proofs: "
            + $"{bytes} bytes, {sw.ElapsedTicks} ticks (diagnostic only)");
        Assert.InRange(bytes, 0, 64L * 1024 * 1024);
        Assert.Equal(E.Phase.Expired, selection.Executor!.InspectDiagnostic(
            facts.Snapshot, Array.Empty<C.ComboObservation>()).State);
    }
}
