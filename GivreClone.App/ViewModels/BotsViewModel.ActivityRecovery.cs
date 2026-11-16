using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.Protocol;
using GivreClone.App.Services;
using GivreClone.App.Services.Production;

namespace GivreClone.App.ViewModels;

/// <summary>
/// Read-only operator projection of the actual Lua route/gather recovery lease.
/// A cell/map change or inventory delta is NOT an ACK. Showing the quarantine
/// never re-arms a script, clears a journal, or dispatches anything.
/// </summary>
public partial class BotsViewModel
{
    private string _activityRecoveryStatusText = string.Empty;
    private bool _activityRecoveryStatusVisible;
    private int _activityRecoveryInspectedPid;
    private ActivityRecoveryDurableStatus? _activityRecoveryInspectedDurable;
    private ActivityRecoveryReconciliationResult? _activityRecoveryInspectedReconciliation;
    private ActivityRecoveryCausalNextStepStatus? _activityRecoveryInspectedNextV17;
    private ActivityRecoveryTransitionsV18Status? _activityRecoveryInspectedTransitionsV18;
    private int _activityRecoveryInspectionSequence;
    private int _guidedV16ActivityInspectionSequence;
    [ObservableProperty] public partial string GuidedV16ActivityInspectionText { get; private set; } =
        "V16 · Lire l'activité Lua durable avant toute réévaluation du stock.";

    // This is an explicitly selected, persisted *single* declarative action.
    // Preparing a script NEVER starts the Lua host or resets a durable marker.
    [ObservableProperty] public partial string StrictRouteTargetCell { get; set; } = "";
    [ObservableProperty] public partial string StrictRouteMaxPods { get; set; } = "95";
    [ObservableProperty] public partial bool StrictRouteGather { get; set; }
    [ObservableProperty] public partial string StrictRouteDraftStatus { get; set; } =
        "Choisir une cellule cible sur la carte attestée, puis préparer. Une seule émission pourra être admise après contrôle durable.";

    public string ActivityRecoveryStatusText => _activityRecoveryStatusText;
    public bool ActivityRecoveryStatusVisible => _activityRecoveryStatusVisible;

    /// <summary>
    /// Explicit read-only inspection. Constructing the local Lua host does not
    /// Start a script or dispatch a packet; the durable store is only read on
    /// request, never on the UI heartbeat.
    /// </summary>
    [RelayCommand]
    private async Task InspectActivityRecovery()
    {
        int request = Interlocked.Increment(ref _activityRecoveryInspectionSequence);
        if (SelectedBot is not { Pid: > 0 } bot || bot.Row.IsDisconnected)
        {
            _activityRecoveryInspectedPid = 0;
            _activityRecoveryInspectedDurable = null;
            _activityRecoveryInspectedReconciliation = null;
            _activityRecoveryInspectedNextV17 = null;
            _activityRecoveryInspectedTransitionsV18 = null;
            RefreshActivityRecoveryUi();
            return;
        }
        long selection = Interlocked.Read(ref _mapAttackSelectionGeneration);
        int server = Stores.Launch.ServerIdOf((uint)bot.Pid);
        long character = Stores.Launch.CharacterId((uint)bot.Pid);
        if (server <= 0 || character <= 0) return;
        GivreLuaHost host = GetOrCreateHost(bot.Pid);
        ActivityRecoveryReconciliationResult? reconciliation;
        ActivityRecoveryDurableStatus? durable;
        ActivityRecoveryCausalNextStepStatus? causal;
        ActivityRecoveryTransitionsV18Status? transitions;
        try
        {
            // The SAME real Lua host reads the persisted segment and observes
            // its current server map/cell. Disk IO occurs off the UI thread.
            // A late result after A->B->A, PID reuse or disconnect is ignored.
            transitions = await Task.Run(
                () => host.RefreshActivityRecoveryTransitionsV18Status());
            causal = host.ActivityRecoveryCausalNextStepStatus;
            reconciliation = host.ActivityRecoveryReconciliationStatus;
            durable = transitions.DurableStatus;
        }
        catch (Exception)
        {
            causal = null;
            transitions = null;
            reconciliation = null;
            durable = new ActivityRecoveryDurableStatus(
                new ActivityRecoveryScope(server, character),
                ActivityRecoveryDurableState.StorageUnavailable,
                null, 0, 0, "état durable non consultable");
        }
        if (request != Volatile.Read(ref _activityRecoveryInspectionSequence)
            || !ReferenceEquals(SelectedBot, bot)
            || bot.Row.IsDisconnected
            || Interlocked.Read(ref _mapAttackSelectionGeneration) != selection
            || server != Stores.Launch.ServerIdOf((uint)bot.Pid)
            || character != Stores.Launch.CharacterId((uint)bot.Pid))
            return;
        _activityRecoveryInspectedPid = bot.Pid;
        _activityRecoveryInspectedReconciliation = reconciliation;
        _activityRecoveryInspectedDurable = durable;
        _activityRecoveryInspectedNextV17 = causal;
        _activityRecoveryInspectedTransitionsV18 = transitions;
        RefreshActivityRecoveryUi();
    }

    /// <summary>
    /// Invoked by the EXISTING atelier→Lua event after the operator chooses
    /// "Inspecter suite activité". Reads the real host's durable marker and
    /// authenticated inventory/pods/resource projection, not another planner.
    /// Never starts Lua, releases a pending marker or transmits a request.
    /// </summary>
    public async Task<ActivitySupplyContinuityStatus?> InspectGuidedSupplyV16Async(
        uint pid, int server, long character, long ingredientGid,
        long required, int maxPods)
    {
        int ticket = Interlocked.Increment(ref _guidedV16ActivityInspectionSequence);
        GuidedV16ActivityInspectionText = "V16 · Consultation de la reprise durable en cours.";
        if (SelectedBot is not { Pid: > 0 } bot ||
            bot.Row.IsDisconnected || (uint)bot.Pid != pid ||
            server <= 0 || character <= 0 ||
            Stores.Launch.ServerIdOf(pid) != server ||
            Stores.Launch.CharacterId(pid) != character ||
            ingredientGid is <= 0 or > int.MaxValue || required <= 0 ||
            maxPods is < 1 or > 100)
        {
            GuidedV16ActivityInspectionText =
                "V16 · Source ou GID exact absent : inspecter d'abord le journal Lua, aucune émission.";
            return null;
        }
        long selection = Interlocked.Read(ref _mapAttackSelectionGeneration);
        GivreLuaHost host = GetOrCreateHost(bot.Pid);
        var sourceBefore = Stores.Launch.ReadGuidedSupplySource(pid);
        ActivitySupplyContinuityStatus? observed;
        ActivityRecoveryCausalNextStepStatus? causal;
        try
        {
            (observed, causal) = await Task.Run(() =>
            {
                var continuity = host.RefreshActivitySupplyContinuityStatus(
                    ingredientGid, required, maxPods);
                var next = host.RefreshActivityRecoveryCausalNextStepStatus(maxPods);
                return (continuity, next);
            });
        }
        catch (Exception ex)
        {
            if (ticket == Volatile.Read(ref _guidedV16ActivityInspectionSequence))
                GuidedV16ActivityInspectionText =
                    "V16 · Consultation du journal indisponible (" +
                    ex.GetType().Name + ") ; aucune nouvelle action autorisée.";
            return null;
        }
        var sourceAfter = Stores.Launch.ReadGuidedSupplySource(pid);
        if (ticket != Volatile.Read(ref _guidedV16ActivityInspectionSequence) ||
            !ReferenceEquals(SelectedBot, bot) || bot.Row.IsDisconnected ||
            Interlocked.Read(ref _mapAttackSelectionGeneration) != selection ||
            Stores.Launch.ServerIdOf(pid) != server ||
            Stores.Launch.CharacterId(pid) != character ||
            sourceBefore != sourceAfter ||
            observed.DurableStatus.Scope != new ActivityRecoveryScope(server, character) ||
            !Equals(observed.DurableStatus, causal.DurableStatus))
        {
            GuidedV16ActivityInspectionText =
                "V16 · Identité/source/génération changée pendant la lecture : résultat ignoré, aucun envoi.";
            return null;
        }
        _activityRecoveryInspectedPid = bot.Pid;
        _activityRecoveryInspectedDurable = observed.DurableStatus;
        _activityRecoveryInspectedNextV17 = causal;
        _activityRecoveryInspectedReconciliation =
            host.ActivityRecoveryReconciliationStatus;
        RefreshActivityRecoveryUi();
        GuidedV16ActivityInspectionText =
            "V16 · GID " + ingredientGid + " ×" + required +
            " · reprise " + observed.DurableStatus.State +
            " · examen " + observed.NextReview +
            (observed.DurableStatus.IsQuarantined
                ? " · après rapprochement indépendant : " +
                  observed.ConditionalReviewAfterReconciliation
                : "") +
            " · source " + (observed.SourceVerified ? "observée" : "non attestée") +
            " · sac " + (observed.BagObserved?.ToString() ?? "inconnu") +
            " · reste " + (observed.RemainingInBag?.ToString() ?? "inconnu") +
            " · " + observed.Explanation +
            " · V17 étape suivante " + causal.NextReview +
            " · après rapprochement indépendant " +
            causal.ConditionalReviewAfterIndependentReconciliation +
            " · pods " + (causal.PodsVerified
                ? causal.PodsAtThreshold == true ? "au seuil" : "sous seuil"
                : "non attestés") +
            " · " + causal.Explanation +
            ". Aucun ACK individuel ni droit de rejouer n'est déduit de ce constat.";
        return observed;
    }

    /// <summary>
    /// Validates the exact one-shot Lua route that the existing
    /// GivreLuaHost.RunLoopCore actually consumes. Generating this text is
    /// not a transport admission, a game request or a server acknowledgment.
    /// Do not silently turn this into an imperatively looping move() script.
    /// </summary>
    public static bool TryBuildStrictActivityRouteScript(
        long map, int targetCell, bool gather, int maxPods,
        out string script, out string refusal) =>
        TryBuildStrictActivityRouteScript(map, targetCell, gather, maxPods,
            null, out script, out refusal);

    /// <summary>
    /// V14 guided specialization: an INGREDIENT item GID is not an interactive
    /// ElementTypeId. Strict recovery uses ITEMS_TO_GATHER to resolve the
    /// gathered item from authenticated skill data; a missing match sends
    /// nothing. Only the legacy manual route may retain an unfiltered gather.
    /// </summary>
    public static bool TryBuildStrictActivityRouteScript(
        long map, int targetCell, bool gather, int maxPods, long? gatherItemGid,
        out string script, out string refusal)
    {
        script = "";
        refusal = "";
        if (map <= 0 || map > int.MaxValue ||
            (uint)targetCell >= MapPathfinder.CellCount ||
            maxPods is < 1 or > 100 ||
            gatherItemGid is not null && (!gather || gatherItemGid is <= 0 or > int.MaxValue))
        {
            refusal = "Carte, cellule ou seuil de pods hors limites ; aucun script enregistré.";
            return false;
        }
        script = "-- GivreClone · parcours durable ONE-SHOT sur carte attestée\n" +
                 "-- Génération par le panneau Lua ; édition et lancement manuels.\n" +
                 "-- Après un timeout/Stop/crash, la tentative reste en quarantaine.\n" +
                 "-- Aucun ACK n'est déduit d'une position ou d'un delta de stock.\n" +
                 "ACTIVITY_RECOVERY = true\n" +
                 "ACTIVITY_RECOVERY_TABLE_ONLY = true\n" +
                 "MAX_PODS = " + maxPods.ToString(CultureInfo.InvariantCulture) + "\n" +
                 (gatherItemGid is long gid ?
                     "ITEMS_TO_GATHER = { " + gid.ToString(CultureInfo.InvariantCulture) +
                     " }   -- GID de l'ingrédient, PAS ElementTypeId\n" : "") +
                 "ACTIVITY_RECOVERY_ROUTE = {\n" +
                 "  { map = " + map.ToString(CultureInfo.InvariantCulture) +
                 ", activityRecovery = true" +
                 // Guided supply is ONE verified harvest only; a failed/no-
                 // match gather must never degrade into a surprise cell move.
                 (gatherItemGid is null ? ", cell = " +
                     targetCell.ToString(CultureInfo.InvariantCulture) : "") +
                 (gather ? ", gather = true" : "") + " }\n" +
                 "}\n";
        return true;
    }

    /// <summary>
    /// V14: a genuinely bound harvest draft carries the checksum-verified
    /// V11/V13 goal, request, plan, current source and inventory revision.
    /// The Lua host MUST independently compare these to its durable stores at
    /// dispatch time. Strings for long counters avoid MoonSharp double loss.
    /// This text remains an operator-editable draft, never an authority itself.
    /// </summary>
    public static bool TryBuildGuidedSupplyRouteScript(
        long map, int targetCell, int maxPods,
        GuidedSupplyRoutePreparation request,
        out string script, out string refusal)
    {
        script = "";
        refusal = "";
        static bool Sha64(string? value) => value is { Length: 64 } &&
            value.All(Uri.IsHexDigit);
        if (request is null || !request.Goal.Valid || !request.Goal.Gather ||
            request.GatherItemGid is <= 0 or > int.MaxValue ||
            request.Required <= 0 || request.InventoryRevision <= 0 ||
            !Sha64(request.RequestHash) || !Sha64(request.PlanHash) ||
            !Sha64(request.SourceKey) || !Sha64(request.SessionKey))
        {
            refusal = "Checkpoint V14 incomplet ou altéré ; aucun script enregistré.";
            return false;
        }
        if (!TryBuildStrictActivityRouteScript(map, targetCell,
                gather: true, maxPods, request.GatherItemGid,
                out script, out refusal)) return false;
        string metadata =
            "GUIDED_SUPPLY_GID = " + request.GatherItemGid.ToString(CultureInfo.InvariantCulture) + "\n" +
            "GUIDED_SUPPLY_REQUIRED = \"" + request.Required.ToString(CultureInfo.InvariantCulture) + "\"\n" +
            "GUIDED_SUPPLY_GOAL_HASH = \"" + GuidedProductionWorkflowLedger.Fingerprint(request.Goal) + "\"\n" +
            "GUIDED_SUPPLY_REQUEST_HASH = \"" + request.RequestHash + "\"\n" +
            "GUIDED_SUPPLY_PLAN_HASH = \"" + request.PlanHash + "\"\n" +
            "GUIDED_SUPPLY_SOURCE_KEY = \"" + request.SourceKey + "\"\n" +
            "GUIDED_SUPPLY_SESSION_KEY = \"" + request.SessionKey + "\"\n" +
            "GUIDED_SUPPLY_OBS_REV = \"" + request.InventoryRevision.ToString(CultureInfo.InvariantCulture) + "\"\n";
        const string marker = "ACTIVITY_RECOVERY_ROUTE = {";
        if (!script.Contains(marker, StringComparison.Ordinal))
        {
            script = "";
            refusal = "La route Lua stricte ne possède pas son point d'entrée ; aucun script enregistré.";
            return false;
        }
        script = script.Replace(marker, metadata + marker, StringComparison.Ordinal);
        return true;
    }

    private readonly record struct StrictRouteTicket(
        int Pid, int Server, long Character, long Map,
        long Generation, long Selection, int SelfCell);

    private sealed record GuidedSupplyScriptAuthorization(
        GuidedSupplyRoutePreparation Request, Func<bool> Current);

    private GuidedSupplyScriptAuthorization? _nextGuidedSupplyScript;
    private long _strictRoutePrepareGeneration;

    /// <summary>
    /// Actual atelier→Lua command adapter; does not start or replace a script
    /// without the EXISTING modal. The guard is checked on entry and again
    /// after the user confirms; no objective is re-authored by this handoff.
    /// </summary>
    public bool ArmStrictActivityRouteForGuidedSupply(
        GuidedSupplyRoutePreparation request, Func<bool> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        _nextGuidedSupplyScript = null;
        if (request is null || request.GatherItemGid is <= 0 or > int.MaxValue ||
            request.Required <= 0 || request.InventoryRevision <= 0 ||
            !request.Goal.Gather || !current())
        {
            StrictRouteDraftStatus =
                "REFUS : déficit GID, personnage ou checkpoint d'approvisionnement non confirmé.";
            return false;
        }
        _nextGuidedSupplyScript = new(request, current);
        StrictRouteTargetCell = request.Goal.Cell.ToString(CultureInfo.InvariantCulture);
        StrictRouteMaxPods = request.Goal.MaxPods.ToString(CultureInfo.InvariantCulture);
        StrictRouteGather = true;
        StrictRouteCraftGid = request.Goal.ResultGid.ToString(CultureInfo.InvariantCulture);
        StrictRouteCraftQuantity = request.Goal.Quantity.ToString(CultureInfo.InvariantCulture);
        return true; // caller now executes the existing UI command/modal
    }

    private StrictRouteTicket? CaptureStrictRouteTicket()
    {
        if (SelectedBot is not { Pid: > 0 } bot || bot.Row.IsDisconnected ||
            bot.FollowerLocked || _luaHosts.TryGetValue(bot.Pid, out var host) && host.Running)
            return null;
        uint pid = (uint)bot.Pid;
        int server = Stores.Launch.ServerIdOf(pid);
        long actor = Stores.Launch.CharacterId(pid);
        var world = Stores.World.Get(pid);
        lock (world.ActorPublicationGate)
        {
            long map = Stores.Launch.CurrentMap(pid) ?? 0;
            if (server <= 0 || actor <= 0 || map <= 0 ||
                world.MapId != map || world.MapGeneration <= 0 ||
                !world.SelfReadyFor(map) || !world.CellsReadyFor(map) ||
                world.SelfActorId != actor || world.SelfCell is not int cell ||
                !world.Actors.Any(a => a.Kind == MapActorKind.Player &&
                    a.ActorId == actor && a.CellId == cell))
                return null;
            return new(bot.Pid, server, actor, map,
                world.MapGeneration,
                Interlocked.Read(ref _mapAttackSelectionGeneration), cell);
        }
    }

    [RelayCommand]
    private void PrepareStrictActivityRoute()
    {
        var supply = _nextGuidedSupplyScript;
        _nextGuidedSupplyScript = null;
        long operation = Interlocked.Increment(ref _strictRoutePrepareGeneration);
        var owner = CaptureStrictRouteTicket();
        if (owner is not { } ticket)
        {
            StrictRouteDraftStatus =
                "REFUS : aucun personnage, carte, position et géométrie attestés, ou script déjà actif.";
            return;
        }
        string script = "", refusal = "";
        int cell = 0, pods = 0;
        bool validInput = int.TryParse(StrictRouteTargetCell, NumberStyles.None,
                CultureInfo.InvariantCulture, out cell) &&
            int.TryParse(StrictRouteMaxPods, NumberStyles.None,
                CultureInfo.InvariantCulture, out pods);
        bool built = validInput && (supply is { } boundDraft
            ? TryBuildGuidedSupplyRouteScript(ticket.Map, cell, pods,
                boundDraft.Request, out script, out refusal)
            : TryBuildStrictActivityRouteScript(ticket.Map, cell, StrictRouteGather,
                pods, out script, out refusal));
        if (!built)
        {
            StrictRouteDraftStatus =
                string.IsNullOrWhiteSpace(refusal) ?
                    "REFUS : cellule et seuil de pods entiers requis." : refusal;
            return;
        }
        GuidedActivityProductionGoal? guidedGoal;
        string guidedRefusal;
        if (supply is { } authorized)
        {
            if (!authorized.Current() ||
                authorized.Request.Pid != (uint)ticket.Pid ||
                authorized.Request.Goal.ServerId != ticket.Server ||
                authorized.Request.Goal.CharacterId != ticket.Character ||
                !StrictRouteGather || pods != authorized.Request.Goal.MaxPods)
            {
                StrictRouteDraftStatus =
                    "REFUS : source, plan, ressource ou session de l'atelier invalidés.";
                return;
            }
            guidedGoal = authorized.Request.Goal;
            guidedRefusal = "";
        }
        else if (!TryCaptureGuidedProductionGoal(ticket, cell, pods, StrictRouteGather,
                     out guidedGoal, out guidedRefusal))
        {
            StrictRouteDraftStatus = "REFUS : " + guidedRefusal;
            return;
        }
        string goalGidText = StrictRouteCraftGid;
        string goalQtyText = StrictRouteCraftQuantity;
        var state = Stores.World.Get((uint)ticket.Pid);
        if ((supply is null && !state.IsWalkable(cell)) ||
            (cell == ticket.SelfCell && !StrictRouteGather))
        {
            StrictRouteDraftStatus =
                "REFUS : cellule inaccessible ou déjà occupée par le personnage sans récolte demandée.";
            return;
        }
        var selected = SelectedBot;
        var characterName = selected?.Row.CharacterName;
        if (string.IsNullOrWhiteSpace(characterName) ||
            !string.Equals(_luaLoadedFor, characterName, StringComparison.Ordinal))
        {
            StrictRouteDraftStatus =
                "REFUS : l'éditeur doit afficher le script du personnage sélectionné.";
            return;
        }
        // No direct script write until the operator confirms replacement of
        // this character's existing persisted editor contents. Revalidate
        // map/actor/generation both before and AFTER the modal, including ABA.
        StrictRouteDraftStatus =
            $"Préparation : carte {ticket.Map}, cible {cell}, pods {pods} %, " +
            (supply is not null
                ? "récolte filtrée GID " + supply.Request.GatherItemGid + " (sans déplacement implicite). "
                : StrictRouteGather ? "récolte optionnelle. " : "déplacement seul. ") +
            "Confirmation nécessaire ; aucun envoi de jeu.";
        Dialogs.Confirm("Préparer un trajet à reprise stricte",
            "Remplacer le script Lua enregistré de CE personnage par un trajet " +
            "déclaratif sur la carte actuellement attestée ? L'ancien script " +
            "sera remplacé, mais aucun nouveau script ne démarrera. Une action " +
            "incertaine reste en quarantaine après Stop/crash.",
            onConfirm: () =>
            {
                if (operation != Interlocked.Read(ref _strictRoutePrepareGeneration) ||
                    supply is not null && !supply.Current() ||
                    !ReferenceEquals(SelectedBot, selected) ||
                    CaptureStrictRouteTicket() != ticket ||
                    !string.Equals(StrictRouteCraftGid, goalGidText, StringComparison.Ordinal) ||
                    !string.Equals(StrictRouteCraftQuantity, goalQtyText, StringComparison.Ordinal) ||
                    !string.Equals(_luaLoadedFor, characterName, StringComparison.Ordinal) ||
                    supply is null && !Stores.World.Get((uint)ticket.Pid).IsWalkable(cell))
                {
                    StrictRouteDraftStatus =
                        "REFUS : personnage, carte, position ou géométrie modifiés pendant la confirmation.";
                    return;
                }
                // Persist user-selected production goal only after a real
                // ActivityRecovery Clear check. A dirty/pending one-shot
                // marker cannot be replaced by another script or goal.
                if (guidedGoal is not null &&
                    (supply is { } bound
                        ? !Equals(bound.Request.Goal,
                            _guidedProductionStore.Read(ticket.Server, ticket.Character).Goal)
                        : !PersistGuidedProductionBeforeScript(guidedGoal)))
                {
                    StrictRouteDraftStatus =
                        "REFUS : objectif guidé non admis ; script existant préservé.";
                    return;
                }
                SetLuaTextLoaded(script, resetUndo: true);
                LoadedScriptPath = null;
                _luaFileBaseline = null;
                LuaFileModified = false;
                _luaDirty = false;
                PlanningStore.Get(characterName).Script = script;
                PlanningStore.Save();
                StrictRouteDraftStatus =
                    "Script one-shot enregistré pour le personnage actif, non démarré. " +
                    "Vérifier « État reprise », puis lancer manuellement. " +
                    "Une tentative ambiguë bloque le redémarrage.";
            });
    }

    private void RefreshActivityRecoveryUi()
    {
        string text = string.Empty;
        bool visible = false;
        if (SelectedBot is { Pid: > 0 } bot &&
            _luaHosts.TryGetValue(bot.Pid, out var host) &&
            host.ActivityRecoveryStatus is { } recovery)
        {
            var pending = recovery.PendingSegment;
            if (pending is not null)
            {
                visible = true;
                string target = pending.Kind == ActivityRecoveryKind.Cell
                    ? $"cellule {pending.Target}" :
                    pending.Kind == ActivityRecoveryKind.MapTransition
                        ? $"carte {pending.Target}" :
                        $"élément {pending.ElementId} / cellule {pending.Cell}";
                text = $"Reprise stricte BLOQUÉE · {pending.Kind} ({target}), " +
                       (recovery.ObservedEffectWithoutAck
                           ? "effet observé mais ACK non attribuable."
                           : "ACK de séquence non attribuable.") +
                       " Aucun second envoi automatique.";
            }
            else if (recovery.Attempts != 0 || recovery.CorrelatedCompletions != 0)
            {
                visible = true;
                text = $"Reprise stricte : {recovery.Attempts} tentative(s), " +
                       $"{recovery.CorrelatedCompletions} clôture(s) attribuée(s).";
            }
        }
        if (!visible && SelectedBot is { Pid: > 0 } selected &&
            selected.Pid == _activityRecoveryInspectedPid &&
            _activityRecoveryInspectedDurable is { } durable &&
            durable.Scope.ServerId == Stores.Launch.ServerIdOf((uint)selected.Pid) &&
            durable.Scope.CharacterId == Stores.Launch.CharacterId((uint)selected.Pid))
        {
            visible = true;
            text = durable.State switch
            {
                ActivityRecoveryDurableState.Clear =>
                    "Reprise stricte : aucune tentative durable en attente (diagnostic local uniquement).",
                ActivityRecoveryDurableState.Pending =>
                    $"Reprise stricte BLOQUÉE après redémarrage : {durable.Segment?.Kind} " +
                    $"cible {durable.Segment?.Target}, carte {durable.MapId}. " +
                    "Aucun second envoi sans rapprochement attesté.",
                _ => $"Reprise stricte BLOQUÉE · {durable.State} : " +
                    "identité ou journal non vérifiable, aucun envoi automatique."
            };
        }
        // Reconciliation is a read-only explanation of WHY a persisted
        // attempt remains quarantined. It is not an unlock, a transport ACK,
        // or a reason to emit the next route/gather request. Only display the
        // explicitly inspected profile, never stale evidence of another bot.
        if (SelectedBot is { Pid: > 0 } inspected &&
            inspected.Pid == _activityRecoveryInspectedPid &&
            _activityRecoveryInspectedReconciliation is { } reconciliation &&
            reconciliation.DurableStatus.Scope.ServerId == Stores.Launch.ServerIdOf((uint)inspected.Pid) &&
            reconciliation.DurableStatus.Scope.CharacterId == Stores.Launch.CharacterId((uint)inspected.Pid))
        {
            visible = true;
            if (reconciliation.Decision == ActivityRecoveryReconciliationDecision.PendingQuarantine)
                text += $" · Rapprochement : {reconciliation.Evidence} " +
                        $"(carte {reconciliation.ObservedMap}, génération {reconciliation.ObservedGeneration}). " +
                        "Preuve non attribuable ; quarantaine maintenue.";
            else if (string.IsNullOrWhiteSpace(text))
                text = "Rapprochement consulté : aucune tentative durable en attente ; aucun nouvel envoi demandé.";
        }
        if (SelectedBot is { Pid: > 0 } current &&
            current.Pid == _activityRecoveryInspectedPid &&
            _activityRecoveryInspectedNextV17 is { } next &&
            Equals(next.DurableStatus, _activityRecoveryInspectedDurable) &&
            next.DurableStatus.Scope.ServerId == Stores.Launch.ServerIdOf((uint)current.Pid) &&
            next.DurableStatus.Scope.CharacterId == Stores.Launch.CharacterId((uint)current.Pid))
        {
            visible = true;
            text += " · V17 prochain examen : " + next.NextReview +
                " ; après rapprochement indépendant : " +
                next.ConditionalReviewAfterIndependentReconciliation +
                ". " + next.Explanation +
                " Aucun reçu serveur individuel ni reprise automatique.";
        }
        if (SelectedBot is { Pid: > 0 } v18Bot &&
            v18Bot.Pid == _activityRecoveryInspectedPid &&
            _activityRecoveryInspectedTransitionsV18 is { } transition &&
            Equals(transition.DurableStatus, _activityRecoveryInspectedDurable) &&
            transition.DurableStatus.Scope.ServerId == Stores.Launch.ServerIdOf((uint)v18Bot.Pid) &&
            transition.DurableStatus.Scope.CharacterId == Stores.Launch.CharacterId((uint)v18Bot.Pid))
        {
            visible = true;
            text += " · V18 activité : " + transition.Reason +
                ", refus Lua " + transition.LastRunBlock +
                ", prochaine inspection " + transition.NextReview +
                ", run " + transition.RunGeneration +
                ", pods vérifiés " + transition.PodsVerified +
                ". Aucune émission ni levée de quarantaine.";
        }
        if (!string.Equals(_activityRecoveryStatusText, text, StringComparison.Ordinal))
        {
            _activityRecoveryStatusText = text;
            OnPropertyChanged(nameof(ActivityRecoveryStatusText));
        }
        if (_activityRecoveryStatusVisible != visible)
        {
            _activityRecoveryStatusVisible = visible;
            OnPropertyChanged(nameof(ActivityRecoveryStatusVisible));
        }
    }
}
