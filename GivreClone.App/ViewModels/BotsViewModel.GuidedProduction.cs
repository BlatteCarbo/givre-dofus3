using System;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.ItemCraftPlanner;
using GivreClone.App.Services.Production;

namespace GivreClone.App.ViewModels;

/// <summary>Strict source-bound operator handoff, not a gameplay dispatch.</summary>
public sealed record GuidedProductionHandoff(uint Pid, GuidedActivityProductionGoal Goal);

public partial class BotsViewModel
{
    private readonly GuidedActivityProductionStore _guidedProductionStore =
        new(GuidedActivityProductionStore.DefaultRoot());
    private readonly GuidedProductionWorkflowLedger _guidedWorkflowLedger =
        new(GuidedProductionWorkflowLedger.DefaultRoot());

    [ObservableProperty] public partial string StrictRouteCraftGid { get; set; } = "";
    [ObservableProperty] public partial string StrictRouteCraftQuantity { get; set; } = "1";
    [ObservableProperty] public partial string GuidedProductionStatusText { get; set; } =
        "Parcours guidé : indique un GID résultat et une quantité pour relier trajet et atelier.";

    public event Action<GuidedProductionHandoff>? GuidedProductionHandoffRequested;

    private bool TryCaptureGuidedProductionGoal(StrictRouteTicket ticket, int cell,
        int pods, bool gather, out GuidedActivityProductionGoal? goal, out string refusal)
    {
        goal = null;
        refusal = "";
        string gidText = (StrictRouteCraftGid ?? "").Trim();
        string qtyText = (StrictRouteCraftQuantity ?? "").Trim();
        if (gidText.Length == 0) return true; // V10 route-only mode stays available.
        if (!int.TryParse(gidText, NumberStyles.None, CultureInfo.InvariantCulture,
                out int gid) || gid <= 0 ||
            !int.TryParse(qtyText, NumberStyles.None, CultureInfo.InvariantCulture,
                out int qty) || qty is < 1 or > 100_000)
        {
            refusal = "Objectif atelier invalide : GID résultat positif et quantité 1 à 100 000 requis.";
            return false;
        }
        try
        {
            var recipe = WorkshopLocator.ReadRecipe(Stores.Launch.GameData, gid);
            if (recipe is null || recipe.ResultGid != gid || recipe.Inputs.Count == 0)
            {
                refusal = "Recette locale non vérifiée pour ce GID : objectif atelier non enregistré.";
                return false;
            }
        }
        catch (Exception)
        {
            refusal = "Recette locale non disponible : objectif atelier refusé.";
            return false;
        }
        goal = new GuidedActivityProductionGoal(
            ticket.Server, ticket.Character, ticket.Map, ticket.Generation,
            cell, gid, qty, pods, gather);
        return true;
    }

    private bool PersistGuidedProductionBeforeScript(GuidedActivityProductionGoal goal)
    {
        try
        {
            var scope = new ActivityRecoveryScope(goal.ServerId, goal.CharacterId);
            var recovery = new ActivityRecoveryDurableStore(
                ActivityRecoveryDurableStore.DefaultRoot()).ReadStatus(scope);
            if (recovery.State != ActivityRecoveryDurableState.Clear)
            {
                GuidedProductionStatusText = "BLOQUÉ · ActivityRecovery " +
                    recovery.State + " : aucun objectif ni script nouveau enregistré. " +
                    "La tentative précédente ne peut pas être réémise.";
                return false;
            }
            var prior = _guidedWorkflowLedger.Read(goal.ServerId, goal.CharacterId);
            if (prior.Phase is GuidedProductionWorkflowPhase.InProgress or
                GuidedProductionWorkflowPhase.Uncertain or
                GuidedProductionWorkflowPhase.AwaitingObservation)
            {
                GuidedProductionStatusText = "BLOQUÉ · ancien objectif V12 " +
                    prior.Phase + " : ne jamais remplacer un cycle incertain " +
                    "par un nouveau trajet/atelier sans reçu exact.";
                return false;
            }
            if (!_guidedProductionStore.TrySave(goal))
            {
                GuidedProductionStatusText =
                    "INCERTAIN · journal d'objectif inaccessible/corrompu ; script non remplacé.";
                return false;
            }
            GuidedProductionStatusText =
                $"PRÊT · objectif durable GID {goal.ResultGid} ×{goal.Quantity} " +
                $"sur carte {goal.SourceMap}. Lancer l'activité manuellement ; " +
                "l'atelier exigera encore les stocks/recettes/reçus exacts.";
            return true;
        }
        catch (Exception ex)
        {
            GuidedProductionStatusText =
                "BLOQUÉ · stockage de l'objectif indisponible (" +
                ex.GetType().Name + ").";
            return false;
        }
    }

    /// <summary>Explicit read-only inspection of actual durable activity and goal.</summary>
    [RelayCommand]
    private void InspectGuidedProduction()
    {
        if (SelectedBot is not { Pid: > 0 } bot || bot.Row.IsDisconnected)
        {
            GuidedProductionStatusText = "BLOQUÉ · personnage déconnecté.";
            return;
        }
        int server = Stores.Launch.ServerIdOf((uint)bot.Pid);
        long character = Stores.Launch.CharacterId((uint)bot.Pid);
        try
        {
            var store = new ActivityRecoveryDurableStore(
                ActivityRecoveryDurableStore.DefaultRoot());
            var recovery = store.ReadStatus(new ActivityRecoveryScope(server, character));
            var snapshot = _guidedProductionStore.InspectWithRecovery(
                server, character, recovery);
            var goal = snapshot.Goal;
            if (goal is not null)
            {
                var profileName = Stores.Launch.SelfCharacterInfo((uint)bot.Pid)?.Name;
                string profile = string.IsNullOrWhiteSpace(profileName)
                    ? "" : HdvProfileKey.For(profileName, server);
                var attempts = new ItemCraftProductionAttemptJournal(Path.Combine(
                    MZP19LocalClone.DataRoot, "production", "itemcraft-attempts-v1.journal"));
                var workflow = _guidedWorkflowLedger.Inspect(goal, recovery,
                    attempts.ReadStatus(profile));
                GuidedProductionStatusText =
                    $"Objectif {snapshot.State} · GID {goal.ResultGid} ×{goal.Quantity}. " +
                    $"Workflow {workflow.Phase} : {workflow.Reason}. " +
                    "L'état local ne confirme ni récolte ni fabrication.";
                return;
            }
            GuidedProductionStatusText = snapshot.State switch
            {
                GuidedActivityProductionState.Absent =>
                    "Aucun objectif activité→production enregistré pour ce personnage.",
                GuidedActivityProductionState.Ready =>
                    $"PRÊT · GID {goal!.ResultGid} ×{goal.Quantity} ; " +
                    "objectif durable reconnu. Atelier guidé uniquement, aucun craft envoyé.",
                GuidedActivityProductionState.AwaitingObservation =>
                    $"ATTENTE OBSERVATION · {snapshot.Reason} GID {goal!.ResultGid} ×{goal.Quantity}.",
                _ => "BLOQUÉ / INCERTAIN · " + snapshot.Reason +
                    ". Vérification du journal et de la session requise."
            };
        }
        catch (Exception ex)
        {
            GuidedProductionStatusText =
                "BLOQUÉ · inspection objective impossible (" + ex.GetType().Name + ").";
        }
    }

    /// <summary>
    /// Uses the real selected bot. The shell will import the target into the
    /// existing trusted catalog/production VM, never call RunProduction.
    /// </summary>
    [RelayCommand]
    private void OpenGuidedProduction()
    {
        if (SelectedBot is not { Pid: > 0 } bot || bot.Row.IsDisconnected)
        {
            GuidedProductionStatusText = "BLOQUÉ · aucun personnage source confirmé.";
            return;
        }
        var ticket = CaptureStrictRouteTicket();
        if (ticket is not { } owner || owner.Pid != bot.Pid)
        {
            GuidedProductionStatusText = "BLOQUÉ · carte/acteur/source non attestés ou script encore actif.";
            return;
        }
        try
        {
            var scope = new ActivityRecoveryScope(owner.Server, owner.Character);
            var recovery = new ActivityRecoveryDurableStore(
                ActivityRecoveryDurableStore.DefaultRoot()).ReadStatus(scope);
            var snapshot = _guidedProductionStore.InspectWithRecovery(
                owner.Server, owner.Character, recovery);
            if (!snapshot.MayPrepareProduction || snapshot.Goal is not { } goal)
            {
                GuidedProductionStatusText =
                    $"BLOQUÉ · {snapshot.State}: {snapshot.Reason}. Aucun atelier démarré.";
                return;
            }
            if (CaptureStrictRouteTicket() != ticket)
            {
                GuidedProductionStatusText =
                    "BLOQUÉ · session ou carte changée pendant le contrôle.";
                return;
            }
            GuidedProductionStatusText =
                $"TRANSFERT GUIDÉ · GID {goal.ResultGid} ×{goal.Quantity} : " +
                "l'atelier doit revérifier sa recette, le stock et la session. " +
                "Aucune fabrication n'a été lancée.";
            GuidedProductionHandoffRequested?.Invoke(
                new GuidedProductionHandoff((uint)bot.Pid, goal));
        }
        catch (Exception ex)
        {
            GuidedProductionStatusText =
                "BLOQUÉ · inspection d'activité indisponible (" +
                ex.GetType().Name + ").";
        }
    }
}
