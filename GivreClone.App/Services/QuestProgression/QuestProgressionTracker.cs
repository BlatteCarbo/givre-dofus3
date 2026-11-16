using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.QuestProgression;

/// <summary>
/// Projection volontairement passive d'une progression de quête. Aucun envoi réseau.
/// Le producteur DOIT distinguer un événement serveur décodé et une requête simplement émise.
/// </summary>
public sealed class QuestProgressionTracker
{
    private readonly Dictionary<int, int> _stepByQuest = new();
    private readonly HashSet<(int Quest, int Step, int Objective)> _reached = new();
    private readonly HashSet<int> _finished = new();
    private string? _session;
    private long _character;
    private long _lastSequence;
    private bool _hasSnapshot;

    public string? Session => _session;
    public long CharacterId => _character;
    public bool HasAuthoritativeSnapshot => _hasSnapshot;
    public long LastSequence => _lastSequence;

    /// <summary>
    /// Rejette les identités non authentifiées, les événements non serveur, les événements
    /// périmés et les payloads malformés sans modifier l'état existant.
    /// Un changement de session explicite invalide les confirmations précédentes.
    /// </summary>
    public bool Apply(QuestServerEvidence evidence)
    {
        if (evidence is null || !evidence.VerifiedServerEvent ||
            !evidence.VerifiedCharacterIdentity || evidence.CharacterId <= 0 ||
            string.IsNullOrWhiteSpace(evidence.SessionKey) ||
            evidence.SessionKey.Length > 128 || evidence.Sequence <= 0 ||
            !Enum.IsDefined(evidence.Kind))
            return false;

        bool newSession = _session != evidence.SessionKey;
        if (!newSession && _character != evidence.CharacterId)
            return false;
        if (!newSession && evidence.Sequence <= _lastSequence)
            return false;

        if (evidence.Kind == QuestEvidenceKind.FullSnapshot)
        {
            if (evidence.Snapshot is null ||
                evidence.Snapshot.Active is null ||
                evidence.Snapshot.Finished is null ||
                evidence.Snapshot.Active.Count > 1000 ||
                evidence.Snapshot.Finished.Count > 10000 ||
                evidence.Snapshot.Active.Any(q => q is null || q.QuestId <= 0 || q.StepId < 0 ||
                    q.ReachedObjectiveIds is null ||
                    q.ReachedObjectiveIds.Count > 2000 ||
                    q.ReachedObjectiveIds.Any(id => id <= 0) ||
                    q.ReachedObjectiveIds.Distinct().Count() != q.ReachedObjectiveIds.Count) ||
                evidence.Snapshot.Finished.Any(id => id <= 0) ||
                evidence.Snapshot.Active.Select(q => q.QuestId).Distinct().Count() != evidence.Snapshot.Active.Count ||
                evidence.Snapshot.Finished.Distinct().Count() != evidence.Snapshot.Finished.Count ||
                evidence.Snapshot.Active.Any(q => evidence.Snapshot.Finished.Contains(q.QuestId)))
                return false;
        }
        else if (evidence.QuestId <= 0 ||
                 (evidence.Kind == QuestEvidenceKind.StepStarted && evidence.StepId <= 0) ||
                 (evidence.Kind == QuestEvidenceKind.ObjectiveValidated && evidence.ObjectiveId <= 0))
            return false;

        if (newSession)
        {
            _stepByQuest.Clear();
            _reached.Clear();
            _finished.Clear();
            _hasSnapshot = false;
            _session = evidence.SessionKey;
            _character = evidence.CharacterId;
        }

        _lastSequence = evidence.Sequence;
        switch (evidence.Kind)
        {
            case QuestEvidenceKind.FullSnapshot:
                _stepByQuest.Clear();
                _reached.Clear();
                _finished.Clear();
                foreach (int finished in evidence.Snapshot!.Finished)
                    _finished.Add(finished);
                foreach (var quest in evidence.Snapshot.Active)
                {
                    if (quest.StepId <= 0) continue; // sans details : objectifs inconnus
                    _stepByQuest.Add(quest.QuestId, quest.StepId);
                    foreach (int objective in quest.ReachedObjectiveIds)
                        _reached.Add((quest.QuestId, quest.StepId, objective));
                }
                _hasSnapshot = true;
                break;
            case QuestEvidenceKind.StepStarted:
                _stepByQuest[evidence.QuestId] = evidence.StepId;
                // Les objectifs validés par le serveur sur une étape antérieure
                // restent prouvés dans CETTE session. La clé contient l'étape :
                // une validation ancienne ne peut confirmer le nouvel objectif.
                _finished.Remove(evidence.QuestId);
                break;
            case QuestEvidenceKind.ObjectiveValidated:
                // Le message ObjectiveValidated ne porte pas le stepId :
                // sans étape reçue du serveur, refuser toute confirmation.
                if (_stepByQuest.TryGetValue(evidence.QuestId, out int step))
                    _reached.Add((evidence.QuestId, step, evidence.ObjectiveId));
                break;
            case QuestEvidenceKind.QuestFinished:
                _finished.Add(evidence.QuestId);
                _stepByQuest.Remove(evidence.QuestId);
                _reached.RemoveWhere(o => o.Quest == evidence.QuestId);
                break;
            default:
                return false;
        }
        return true;
    }

    public QuestProgressView Evaluate(IReadOnlyList<QuestMilestone> milestones)
    {
        if (milestones is null || milestones.Count > 10000 ||
            milestones.Any(m => m.QuestId <= 0 || m.StepId <= 0 || m.ObjectiveId <= 0 ||
                string.IsNullOrWhiteSpace(m.Title)) ||
            milestones.Select(m => (m.QuestId, m.StepId, m.ObjectiveId)).Distinct().Count() != milestones.Count)
            throw new ArgumentException("Plan de quête invalide, dupliqué ou trop volumineux.", nameof(milestones));

        var entries = new List<QuestMilestoneView>(milestones.Count);
        bool priorBlocked = false;
        foreach (var milestone in milestones)
        {
            QuestMilestoneState state;
            string why;
            if (_finished.Contains(milestone.QuestId) ||
                _reached.Contains((milestone.QuestId, milestone.StepId, milestone.ObjectiveId)))
            {
                state = QuestMilestoneState.ServerConfirmed;
                why = "Progression confirmée par événement serveur / instantané autoritatif.";
            }
            else if (!_hasSnapshot)
            {
                state = QuestMilestoneState.WaitingForSnapshot;
                why = "Attente d'un instantané de quêtes associé au personnage.";
            }
            else if (priorBlocked)
            {
                state = QuestMilestoneState.PrerequisiteUnconfirmed;
                why = "L'objectif précédent n'a pas été confirmé.";
            }
            else if (!_stepByQuest.TryGetValue(milestone.QuestId, out var serverStep))
            {
                state = QuestMilestoneState.WaitingForDetails;
                why = "Étape ou quête absente des détails serveur; pas de validation présumée.";
            }
            else if (serverStep != milestone.StepId)
            {
                state = QuestMilestoneState.PrerequisiteUnconfirmed;
                why = "Étape du serveur différente de celle du plan.";
            }
            else
            {
                state = milestone.RequiresManualAction
                    ? QuestMilestoneState.ManualReview
                    : QuestMilestoneState.ReadyForReview;
                why = milestone.RequiresManualAction
                    ? "Action manuelle requise; aucune validation automatique."
                    : "Objectif présent dans l'étape serveur; action scriptée non certifiée.";
            }
            if (state != QuestMilestoneState.ServerConfirmed) priorBlocked = true;
            entries.Add(new QuestMilestoneView(milestone, state, why));
        }

        return new QuestProgressView(_session, _character, _hasSnapshot, entries,
            entries.Count(v => v.State == QuestMilestoneState.ServerConfirmed));
    }
}

public enum QuestEvidenceKind { FullSnapshot, StepStarted, ObjectiveValidated, QuestFinished }

public sealed record QuestObservedSnapshot(
    IReadOnlyList<QuestObservedActive> Active,
    IReadOnlyList<int> Finished);

public sealed record QuestObservedActive(int QuestId, int StepId, IReadOnlyList<int> ReachedObjectiveIds);

public sealed record QuestServerEvidence(
    string SessionKey, long CharacterId, long Sequence,
    QuestEvidenceKind Kind, int QuestId = 0, int StepId = 0, int ObjectiveId = 0,
    QuestObservedSnapshot? Snapshot = null,
    bool VerifiedServerEvent = false, bool VerifiedCharacterIdentity = false);

public sealed record QuestMilestone(
    int QuestId, int StepId, int ObjectiveId, string Title,
    bool RequiresManualAction = false);
public enum QuestMilestoneState
{
    WaitingForSnapshot, WaitingForDetails, PrerequisiteUnconfirmed,
    ManualReview, ReadyForReview, ServerConfirmed
}
public sealed record QuestMilestoneView(QuestMilestone Milestone, QuestMilestoneState State, string Reason);
public sealed record QuestProgressView(
    string? SessionKey, long CharacterId, bool HasAuthoritativeSnapshot,
    IReadOnlyList<QuestMilestoneView> Milestones, int ConfirmedCount);
