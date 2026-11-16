using System;
using System.Collections.Generic;
using GivreClone.App.Services.QuestProgression;
using Xunit;

namespace GivreClone.Tests;

public sealed class QuestProgressionTrackerTests
{
    private static readonly QuestMilestone[] Plan =
    {
        new(42, 3, 1001, "Parler au PNJ"),
        new(42, 3, 1002, "Choisir la réponse", RequiresManualAction: true),
        new(42, 4, 1003, "Revenir au point de départ")
    };

    private static QuestServerEvidence Snapshot(string session = "session-a", long player = 77, long seq = 1,
        int step = 3, params int[] reached)
        => new(session, player, seq, QuestEvidenceKind.FullSnapshot,
            Snapshot: new QuestObservedSnapshot(
                new[] { new QuestObservedActive(42, step, reached) }, Array.Empty<int>()),
            VerifiedServerEvent: true, VerifiedCharacterIdentity: true);

    private static QuestServerEvidence Event(QuestEvidenceKind kind, long seq, int quest = 42,
        int step = 0, int objective = 0, string session = "session-a", long player = 77)
        => new(session, player, seq, kind, quest, step, objective,
            VerifiedServerEvent: true, VerifiedCharacterIdentity: true);

    [Fact]
    public void Unknown_or_outgoing_packets_never_confirm_milestones()
    {
        var tracker = new QuestProgressionTracker();
        Assert.False(tracker.Apply(Snapshot("session-a", 77, 1) with { VerifiedServerEvent = false }));
        Assert.All(tracker.Evaluate(Plan).Milestones,
            x => Assert.Equal(QuestMilestoneState.WaitingForSnapshot, x.State));
        Assert.True(tracker.Apply(Snapshot()));
        Assert.False(tracker.Apply(Event(QuestEvidenceKind.ObjectiveValidated, 2, objective: 1001)
            with { VerifiedServerEvent = false }));
        Assert.Equal(0, tracker.Evaluate(Plan).ConfirmedCount);
    }

    [Fact]
    public void Server_objective_confirmation_advances_only_matching_step()
    {
        var tracker = new QuestProgressionTracker();
        Assert.True(tracker.Apply(Snapshot()));
        Assert.Equal(QuestMilestoneState.ReadyForReview, tracker.Evaluate(Plan).Milestones[0].State);
        Assert.True(tracker.Apply(Event(QuestEvidenceKind.ObjectiveValidated, 2, objective: 1001)));
        var after = tracker.Evaluate(Plan);
        Assert.Equal(1, after.ConfirmedCount);
        Assert.Equal(QuestMilestoneState.ManualReview, after.Milestones[1].State);
        Assert.Equal(QuestMilestoneState.PrerequisiteUnconfirmed, after.Milestones[2].State);
        Assert.True(tracker.Apply(Event(QuestEvidenceKind.StepStarted, 3, step: 4)));
        Assert.Equal(1, tracker.Evaluate(Plan).ConfirmedCount); // preuve sur l'ancienne étape conservée
        Assert.Equal(QuestMilestoneState.PrerequisiteUnconfirmed, tracker.Evaluate(Plan).Milestones[2].State);
    }

    [Fact]
    public void New_character_session_drops_all_previous_evidence()
    {
        var tracker = new QuestProgressionTracker();
        Assert.True(tracker.Apply(Snapshot(reached: new[] { 1001, 1002 })));
        Assert.Equal(2, tracker.Evaluate(Plan).ConfirmedCount);
        Assert.False(tracker.Apply(Snapshot("session-a", player: 88, seq: 2)));
        Assert.Equal(2, tracker.Evaluate(Plan).ConfirmedCount);
        Assert.True(tracker.Apply(Snapshot("session-b", player: 88, seq: 1)));
        Assert.Equal(0, tracker.Evaluate(Plan).ConfirmedCount);
        Assert.Equal(88, tracker.CharacterId);
    }

    [Fact]
    public void Out_of_order_and_invalid_snapshots_leave_state_intact()
    {
        var tracker = new QuestProgressionTracker();
        Assert.True(tracker.Apply(Snapshot(reached: new[] { 1001 })));
        Assert.False(tracker.Apply(Snapshot(seq: 1)));
        Assert.False(tracker.Apply(Snapshot(seq: 2) with { Snapshot =
            new QuestObservedSnapshot(new[] { new QuestObservedActive(42, -4, Array.Empty<int>()) }, Array.Empty<int>()) }));
        Assert.False(tracker.Apply(Snapshot("session-b", 88, 3) with { Kind = (QuestEvidenceKind)888 }));
        Assert.False(tracker.Apply(Snapshot("session-b", 88, 3) with { Snapshot =
            new QuestObservedSnapshot(null!, Array.Empty<int>()) }));
        Assert.Equal("session-a", tracker.Session);
        Assert.Equal(1, tracker.Evaluate(Plan).ConfirmedCount);
        Assert.Equal(1, tracker.LastSequence);
    }

    [Fact]
    public void Without_step_details_objective_event_is_not_server_confirmation()
    {
        var tracker = new QuestProgressionTracker();
        var missing = Snapshot() with { Snapshot =
            new QuestObservedSnapshot(new[] { new QuestObservedActive(42, 0, Array.Empty<int>()) }, Array.Empty<int>()) };
        Assert.True(tracker.Apply(missing));
        Assert.True(tracker.Apply(Event(QuestEvidenceKind.ObjectiveValidated, 2, objective: 1001)));
        Assert.Equal(QuestMilestoneState.WaitingForDetails, tracker.Evaluate(Plan).Milestones[0].State);
    }

    [Fact]
    public void Explicit_server_quest_completion_is_only_full_completion_event()
    {
        var tracker = new QuestProgressionTracker();
        Assert.True(tracker.Apply(Snapshot()));
        Assert.True(tracker.Apply(Event(QuestEvidenceKind.QuestFinished, 2)));
        Assert.Equal(3, tracker.Evaluate(Plan).ConfirmedCount);
    }

    [Fact]
    public void Duplicate_plan_milestones_are_rejected()
    {
        var tracker = new QuestProgressionTracker();
        Assert.Throws<ArgumentException>(() => tracker.Evaluate(new[] { Plan[0], Plan[0] }));
    }
}
