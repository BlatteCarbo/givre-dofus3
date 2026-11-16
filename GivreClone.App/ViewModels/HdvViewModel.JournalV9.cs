using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>
/// One real durable order on the classic HDV desk. Phase is from the local
/// checksum-validated journal; no entry is a fabricated remote receipt.
/// </summary>
public sealed record HdvJournalOperationRow(
    string Type, string Phase, string Details, bool IsBlocked,
    string Order = "", Guid LocalRunId = default, string Profile = "");

/// <summary>
/// Classic HDV operations pane. The journal is inspected only on explicit
/// operator request, outside the UI thread. Its read-only outcome never
/// becomes authorization to send or a substitute for the durable gate in
/// LaunchService. No negative cache can bypass a newly quarantined order.
/// </summary>
public partial class HdvViewModel
{
    [ObservableProperty] public partial string HdvDurableOperationsStatus { get; set; } =
        "Journal HDV : choisir « Contrôler » pour consulter les opérations du personnage.";
    [ObservableProperty] public partial bool HdvDurableOperationsLoading { get; set; }
    [ObservableProperty] public partial string HdvLastManualReview { get; set; } = "";
    [ObservableProperty] public partial HdvJournalOperationRow? SelectedHdvJournalOperation { get; set; }
    [ObservableProperty] public partial string HdvSelectedEvidenceStatus { get; set; } =
        "Sélectionner un ordre durable puis « Inspecter preuves » : aucune observation locale n'est un ACK.";
    private HdvOperatorEvidenceV12? _selectedHdvEvidence;
    partial void OnSelectedHdvJournalOperationChanged(HdvJournalOperationRow? value)
    {
        _selectedHdvEvidence = null;
        HdvSelectedEvidenceStatus = value is null
            ? "Aucun ordre sélectionné ; aucune preuve attribuable."
            : "Ordre sélectionné ; inspection de source et de stock requise avant rapprochement.";
    }
    public ObservableCollection<HdvJournalOperationRow> HdvDurableOperationRows { get; } = new();
    public bool HdvHasDurableOperationRows => HdvDurableOperationRows.Count > 0;

    private int _hdvJournalInspectionSequence;

    private void NoteHdvManualReview(string kind)
    {
        HdvLastManualReview =
            $"{DateTime.Now:HH:mm:ss} · {kind} rapproché manuellement sur le profil sélectionné. " +
            "Cette déclaration ne constitue pas un ACK serveur.";
        _ = InspectHdvDurableOperations();
    }

    [RelayCommand]
    private async Task InspectHdvDurableOperations()
    {
        var ticket = CaptureHdvUiMutationTicket();
        int inspection = Interlocked.Increment(ref _hdvJournalInspectionSequence);
        if (!IsCurrentHdvUiMutationTicket(ticket))
        {
            HdvDurableOperationsStatus =
                "Journal BLOQUÉ : identité serveur/personnage introuvable. Ne pas supposer un ACK.";
            return;
        }
        HdvDurableOperationsLoading = true;
        SelectedHdvJournalOperation = null;
        HdvDurableOperationRows.Clear();
        OnPropertyChanged(nameof(HdvHasDurableOperationRows));
        HdvDurableOperationsStatus =
            "Lecture du journal durable du personnage actif, sans émission réseau…";
        try
        {
            // A disk-read failure is NOT the absence of an uncertain order.
            // All four underlying service getters are scoped by the same
            // server+character profile and return fail-closed on corruption.
            var inspectionResult = await Task.Run(() =>
            {
                var snapshot = _launch.HdvReadMutationSnapshot(ticket.Pid);
                if (snapshot.State != HdvDirectMutationReadState.Ready)
                    return (Text: "Journal HDV INDISPONIBLE : " + snapshot.Reason +
                        " ; aucune opération n'est réputée réglée.",
                        Rows: Array.Empty<HdvJournalOperationRow>());
                var blocked = new List<string>(4);
                if (_launch.HdvBuyReviewPending(ticket.Pid))
                    blocked.Add("achat");
                if (_launch.HdvRemoveLastDisposition(ticket.Pid)
                    == HdvSellDisposition.NeedsReview)
                    blocked.Add("retrait");
                if (_launch.HdvSellLastDisposition(ticket.Pid)
                    == HdvSellDisposition.NeedsReview)
                    blocked.Add("vente");
                if (_launch.HdvRepriceReviewPending(ticket.Pid))
                    blocked.Add("repricing");
                string text = blocked.Count == 0 ?
                    "Aucune quarantaine de mutation signalée par les contrôles locaux. " +
                    "Ce constat ne certifie ni la disponibilité du serveur ni un ordre exécuté." :
                    "Mutation(s) HDV BLOQUÉE(S) : " +
                       string.Join(", ", blocked) +
                       ". Vérifier les annonces, l'inventaire, les kamas et la taxe " +
                       "dans le client avant un rapprochement individuel explicite. " +
                       "Ne pas réémettre après un timeout ou une réponse ambiguë.";
                // The original UID/GID/quantity/price and transport-local
                // order key come from the SAME fsynced ledger that gates sends.
                // Showing them does not mark any order confirmed or reviewed.
                var rows = snapshot.Rows.Select(row =>
                    ToHdvJournalOperationRow(row, snapshot.Profile)).ToArray();
                return (Text: text, Rows: rows);
            }).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (inspection != Volatile.Read(ref _hdvJournalInspectionSequence) ||
                    !IsCurrentHdvUiMutationTicket(ticket)) return;
                foreach (var row in inspectionResult.Rows)
                    HdvDurableOperationRows.Add(row);
                OnPropertyChanged(nameof(HdvHasDurableOperationRows));
                HdvDurableOperationsStatus = inspectionResult.Text;
            });
        }
        catch (Exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (inspection == Volatile.Read(ref _hdvJournalInspectionSequence) &&
                    IsCurrentHdvUiMutationTicket(ticket))
                    HdvDurableOperationsStatus =
                        "Journal HDV INDISPONIBLE : impossible de conclure qu'aucune opération n'est en attente.";
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (inspection == Volatile.Read(ref _hdvJournalInspectionSequence))
                    HdvDurableOperationsLoading = false;
            });
        }
    }

    private static HdvJournalOperationRow ToHdvJournalOperationRow(
        HdvDirectMutationHistoryRow operation, string profile)
    {
        string type = operation.Kind switch
        {
            "sell" => "Vente",
            "buy" => "Achat",
            "remove" => "Retrait",
            "reprice" => "Repricing",
            _ => "Ordre inconnu"
        };
        string phase = operation.Phase switch
        {
            HdvDirectMutationPhase.Started => "INTENTION DURABLE · émission non prouvée",
            HdvDirectMutationPhase.Attempted => "TENTATIVE DURABLE · issue inconnue",
            HdvDirectMutationPhase.Uncertain => "INCERTAIN · quarantaine",
            HdvDirectMutationPhase.Reviewed => "RAPPROCHÉ MANUELLEMENT · aucun ACK inféré",
            HdvDirectMutationPhase.Confirmed => "CORRÉLATION MÉTIER · pas un ACK transport",
            _ => "PHASE INCONNUE · bloqué"
        };
        string[] f = operation.Order.Split(':');
        string details = f.Length >= 4
            ? "UID " + f[1] + (f.Length >= 7 ? " · GID " + f[2] : "") +
              " · x" + (f.Length >= 7 ? f[3] : f[2]) +
              " · prix " + (f.Length >= 7 ? f[4] : f[3]) +
              " · PID initial " + operation.OriginalPid
            : "Clé locale partiellement illisible · PID initial " + operation.OriginalPid;
        return new(type, phase, details, operation.NeedsReview,
            operation.Order, operation.LocalRunId, profile);
    }

    /// <summary>
    /// Real UI→LaunchService→durable journal plus current inventory/listing
    /// model. Nothing here confirms a purchase, releases tax or sends data.
    /// A source/epoch change after the background read invalidates the result.
    /// </summary>
    [RelayCommand]
    private async Task InspectSelectedHdvOrderEvidence()
    {
        var selected = SelectedHdvJournalOperation;
        var ticket = CaptureHdvUiMutationTicket();
        int inspection = Volatile.Read(ref _hdvJournalInspectionSequence);
        _selectedHdvEvidence = null;
        if (HdvDurableOperationsLoading || selected is not { IsBlocked: true } ||
            selected.LocalRunId == Guid.Empty ||
            !HdvDurableOperationRows.Contains(selected) ||
            !IsCurrentHdvUiMutationTicket(ticket) ||
            selected.Profile != HdvDirectMutationJournal.Profile(
                ticket.ServerId, ticket.CharacterId))
        {
            HdvSelectedEvidenceStatus = "BLOQUÉ · sélection, profil ou source invalides.";
            return;
        }
        HdvSelectedEvidenceStatus = "Inspection du journal et des observations locales de l'ordre…";
        try
        {
            var evidence = await Task.Run(() =>
                _launch.HdvInspectExactOperatorEvidenceV12(ticket.Pid,
                    selected.Profile, selected.Order, selected.LocalRunId))
                .ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (inspection != Volatile.Read(ref _hdvJournalInspectionSequence) ||
                    !IsCurrentHdvUiMutationTicket(ticket) ||
                    !ReferenceEquals(SelectedHdvJournalOperation, selected) ||
                    !HdvDurableOperationRows.Contains(selected)) return;
                if (evidence is null)
                {
                    HdvSelectedEvidenceStatus = "BLOQUÉ · source, annonce ou journal " +
                        "non vérifiables ; aucune autorisation de rapprochement.";
                    return;
                }
                _selectedHdvEvidence = evidence;
                HdvSelectedEvidenceStatus = evidence.Observations +
                    " Inspection exacte locale, aucun ACK attribuable. " +
                    "Un rapprochement reste une déclaration humaine explicite.";
            });
        }
        catch (Exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (inspection == Volatile.Read(ref _hdvJournalInspectionSequence) &&
                    IsCurrentHdvUiMutationTicket(ticket))
                    HdvSelectedEvidenceStatus =
                        "BLOQUÉ · échec de lecture des observations locales.";
            });
        }
    }

    /// <summary>
    /// Operator chooses a single persisted order. The confirmation and the
    /// actual fsync review both demand the identical profile/order/run,
    /// never a generic type-specific clear or local completion callback.
    /// </summary>
    [RelayCommand]
    private void ReconcileSelectedDurableOrder()
    {
        var selected = SelectedHdvJournalOperation;
        var inspected = _selectedHdvEvidence;
        var ticket = CaptureHdvUiMutationTicket();
        int inspection = Volatile.Read(ref _hdvJournalInspectionSequence);
        if (HdvDurableOperationsLoading || selected is not { IsBlocked: true } ||
            selected.LocalRunId == Guid.Empty ||
            inspected is null ||
            inspected.Pid != ticket.Pid ||
            inspected.Profile != selected.Profile ||
            inspected.Order != selected.Order ||
            inspected.RunId != selected.LocalRunId ||
            !HdvDurableOperationRows.Contains(selected) ||
            !IsCurrentHdvUiMutationTicket(ticket) ||
            selected.Profile != HdvDirectMutationJournal.Profile(
                ticket.ServerId, ticket.CharacterId))
        {
            HdvDurableOperationsStatus =
                "Rapprochement refusé : ordre non inspecté, identité ou journal périmé.";
            return;
        }
        Dialogs.Confirm("Rapprochement HDV d'un ordre précis",
            "Vérifie d'abord dans le client officiel l'annonce, le sac, les " +
            "kamas et les taxes correspondant EXCLUSIVEMENT à cet ordre : " +
            selected.Type + " · " + selected.Details + ". " +
            "Observations disponibles : " + inspected.Observations + ". " +
            "Confirmer marque la demande comme RAPPROCHÉE PAR L'OPÉRATEUR, " +
            "jamais comme un ACK serveur ni un remboursement de taxe.",
            onConfirm: () =>
            {
                if (inspection != Volatile.Read(ref _hdvJournalInspectionSequence) ||
                    !IsCurrentHdvUiMutationTicket(ticket) ||
                    !ReferenceEquals(SelectedHdvJournalOperation, selected) ||
                    !ReferenceEquals(_selectedHdvEvidence, inspected) ||
                    !HdvDurableOperationRows.Contains(selected))
                {
                    HdvDurableOperationsStatus =
                        "Rapprochement annulé : écran ou personnage changé.";
                    return;
                }
                bool done = _launch.HdvReviewFromExactEvidenceV12(inspected);
                if (!done)
                {
                    HdvDurableOperationsStatus =
                        "Refus de rapprochement exact : ordre actif, modifié, " +
                        "session périmée ou journal non attesté. Aucune libération.";
                    return;
                }
                _selectedHdvEvidence = null;
                NoteHdvManualReview(selected.Type + " / run local " +
                    selected.LocalRunId.ToString("N"));
            });
    }

    private void ResetHdvJournalInspection()
    {
        Interlocked.Increment(ref _hdvJournalInspectionSequence);
        SelectedHdvJournalOperation = null;
        _selectedHdvEvidence = null;
        HdvDurableOperationRows.Clear();
        OnPropertyChanged(nameof(HdvHasDurableOperationRows));
        HdvDurableOperationsStatus =
            "Journal HDV : personnage changé. Contrôler le profil sélectionné avant tout rapprochement.";
        HdvLastManualReview = "";
        HdvDurableOperationsLoading = false;
    }
}
