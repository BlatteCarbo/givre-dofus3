extern alias F06App;

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
using UiStatus = F06App::GivreClone.App.Services.CombatTacticalAdvisoryStatus;
using UiPhase = F06App::GivreClone.App.Services.Combat.VerifiedTacticalExecutor.Phase;

namespace GivreClone.Tests;

public sealed class VerifiedTacticalWorkflowGuardTests
{
    private const long Actor = 77;
    private const long Fight = 2026;
    private const long Enemy = -1;
    private static readonly int From = MapPathfinder.CellFromMapPoint(10, 0);
    private static readonly int Target = MapPathfinder.CellFromMapPoint(12, 0);
    private static readonly PlannerCell[] Board = Enumerable.Range(0, MapPathfinder.CellCount)
        .Select(c => new PlannerCell(c, true, false, ImmutableArray<int>.Empty)).ToArray();
    private static readonly SpellCastDescriptor Bomb = new(13444, 1, 2, 1, 8, 12,
        false, 2, 2, 0, 0, "",
        ImmutableArray.Create(new SpellEffectDescriptor(1008, SpellEffectKind.Summon,
            0, 0, 0, 0, "a,A", 'P', 1, 0,
            DiceNum: C.VerifiedExplobombTemplate)));

    private static V.Snapshot Snapshot(int? ap = 6, long order = 0)
        => new(Fight, 11, 6, 1, Actor, C.RogueBreedId, From, ap, 3,
            order, null, Board, new[]
            {
                new PlannerFighter(Actor, From, false, 100, 100),
                new PlannerFighter(Enemy, Target, true, 100, 100)
            }, new HashSet<int>());

    private static E Runner()
    {
        var prepared = E.Prepare(C.ComboKind.RogueWall,
            new C.ComboFrame(Fight, 11, 1, 1, Actor, C.RogueBreedId),
            new C.ComboEvidence(TargetId: Enemy, SourceSpell: Bomb,
                ClientVersion: C.VerifiedClassClientVersion,
                SourceBreedId: C.RogueBreedId,
                ObservedLearnedGrades: new Dictionary<int, int> { [Bomb.SpellId] = 1 }),
            Snapshot() with { PendingAction = false },
            new[] { new V.Action(Bomb, MapPathfinder.CellFromMapPoint(11, 0)) },
            enabled: true);
        Assert.NotNull(prepared.Executor);
        return prepared.Executor!;
    }

    private static C.ComboObservation Cast(int order = 1, bool confirmed = true,
        long correlation = 1234)
        => new(order, Fight, 11, 1, Actor, Enemy, C.ComboEventKind.Cast,
            SpellId: 13444, Grade: 1, ToCell: MapPathfinder.CellFromMapPoint(11, 0),
            Correlation: correlation, ServerConfirmed: confirmed);

    [Fact]
    public void Diagnostic_ingress_never_promotes_provided_ServerConfirmed_or_UID_to_ACK()
    {
        var run = Runner();
        var view = run.InspectDiagnostic(Snapshot(order: 1), new[] { Cast() });
        Assert.Equal(E.Phase.Pending, view.State);
        Assert.Equal(0, view.ConfirmedStages);
        Assert.Null(view.AdvisoryAction);
        Assert.False(view.CanDispatch);
        // No transport identity: it is still pending even if an upstream
        // diagnostic claims a correlated positive receipt.
        var repeated = run.InspectDiagnostic(Snapshot(order: 1),
            new[] { Cast(correlation: long.MaxValue) });
        Assert.Equal(E.Phase.Pending, repeated.State);
        Assert.Equal(0, repeated.ConfirmedStages);
        Assert.False(repeated.CanDispatch);
    }

    [Fact]
    public void Diagnostic_pending_expires_on_reconnect_map_generation_or_missing_events()
    {
        var runner = Runner();
        Assert.Equal(E.Phase.Pending, runner.InspectDiagnostic(Snapshot(),
            Array.Empty<C.ComboObservation>()).State);
        Assert.Equal(E.Phase.Expired, runner.InspectDiagnostic(Snapshot() with
        {
            Generation = 12
        }, Array.Empty<C.ComboObservation>()).State);
        Assert.Equal(E.Phase.Expired, runner.InspectDiagnostic(Snapshot(),
            Array.Empty<C.ComboObservation>()).State);

        var other = Runner();
        Assert.Equal(E.Phase.Expired, other.InspectDiagnostic(
            Snapshot(order: 2), new[] { Cast(2) }, streamTruncated: true).State);
        Assert.False(other.InspectDiagnostic(Snapshot(order: 2),
            new[] { Cast(2) }).CanDispatch);
    }

    [Fact]
    public void Changing_tactical_option_never_grants_a_cast_or_reuses_stale_plan()
    {
        var runner = Runner();
        runner.SetEnabled(false);
        Assert.Equal(E.Phase.Disabled, runner.InspectDiagnostic(Snapshot(),
            Array.Empty<C.ComboObservation>()).State);
        runner.SetEnabled(true);
        Assert.Equal(E.Phase.Pending, runner.InspectDiagnostic(Snapshot(),
            Array.Empty<C.ComboObservation>()).State);
        Assert.Equal(E.Phase.Expired, runner.InspectDiagnostic(
            Snapshot() with { Turn = 2 }, Array.Empty<C.ComboObservation>()).State);
        runner.SetEnabled(false);
        runner.SetEnabled(true);
        Assert.Equal(E.Phase.Expired, runner.InspectDiagnostic(Snapshot(),
            Array.Empty<C.ComboObservation>()).State);
    }

    [Fact]
    public void Public_UI_status_is_read_only_and_exposes_no_transport_permission()
    {
        var status = new UiStatus(123, true, UiPhase.Pending, null, null,
            "diagnostic transport provenance missing", Fight, 456, 1, 11);
        Assert.True(status.Enabled);
        Assert.False(status.CanDispatch);
        Assert.False(status.TransportProvenanceEstablished);
        Assert.False(status.IsHypothetical);
        Assert.Equal(UiPhase.Pending, status.Phase);
        var facade = typeof(F06App::GivreClone.App.Services.LaunchService);
        Assert.NotNull(facade.GetMethod("SetCombatTacticalAdvisoryEnabled",
            new[] { typeof(uint), typeof(bool) }));
        Assert.NotNull(facade.GetMethod("ReadCombatTacticalAdvisoryStatus",
            new[] { typeof(uint) }));
    }

    [Fact]
    public void Multi_turn_diagnostic_never_inherits_confirmation_without_new_fight_snapshot()
    {
        var original = Runner();
        var old = original.InspectDiagnostic(Snapshot(order: 1), new[] { Cast() });
        Assert.Equal(0, old.ConfirmedStages);
        var nextTurn = Snapshot(order: 1) with { Turn = 2 };
        Assert.Equal(E.Phase.Expired,
            original.InspectDiagnostic(nextTurn, new[] { Cast() }).State);
        Assert.False(original.InspectDiagnostic(nextTurn, new[] { Cast() }).CanDispatch);
    }
}
