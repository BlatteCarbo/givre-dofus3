using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.Controls;
using GivreClone.App.Services;
using GivreClone.App.Services.Production;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Views;

public partial class MainWindow : Window
{
    private readonly Random _rnd = new();
    private bool _alreadyOpened;
    private VersionVerdict _lastVerdict;   // dernier verdict /version affiché (pour le bouton "Mettre à jour")
    private bool _validating;
    private bool _loginModalDismissable;
    private BlurEffect? _shellBlur;
    private DispatcherTimer? _toastTimer;
    private bool _mzp0DiagnosticsOpen;
    private BotsViewModel? _guidedProductionBoundBots;
    private readonly GuidedActivityProductionStore _guidedActivityStore =
        new(GuidedActivityProductionStore.DefaultRoot());
    private readonly GuidedProductionWorkflowLedger _guidedWorkflowStore =
        new(GuidedProductionWorkflowLedger.DefaultRoot());
    // Lightweight read-only observation only while the optional tools menu is
    // open. No socket/game activity is started, changed or interrupted.
    private readonly DispatcherTimer _socketUiSummaryTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };

    public MainWindow()
    {
        InitializeComponent();
        _socketUiSummaryTimer.Tick += (_, _) =>
        {
            if (DataContext is MainViewModel { AdvancedToolsVisible: true } vm)
                vm.RefreshSocketUiSummary();
        };
        Closed += (_, _) => _socketUiSummaryTimer.Stop();
        WorldAtlasPanelHost.SnapshotProvider = AtlasAccessService.UiSnapshot;
        ProfessionPlannerHost.CatalogProvider = () =>
        {
            var admitted = ProfessionAccessService.GetRecipeCards(ProfessionPlannerHost.ViewModel.SelectedJob);
            return admitted.Count > 0 ? admitted : DofusDbProfessionsUiBridge.RecipeCards(
                ProfessionPlannerHost.ViewModel.SelectedJob);
        };
        ProfessionPlannerHost.PlanProvider = goal =>
            ProfessionAccessService.BuildVisualPlan(goal) ?? DofusDbProfessionsUiBridge.BuildReviewPlan(goal);
        ProfessionPlannerHost.CatalogDirectorySelected += ProfessionAccessService.SelectCatalogDirectory;
        ProfessionPlannerHost.FolderCatalogProvider = folder =>
        {
            ProfessionAccessService.SelectCatalogDirectory(folder);
            return ProfessionPlannerHost.CatalogProvider?.Invoke() ?? Array.Empty<ProfessionCatalogRecipe>();
        };
        ItemCraftAccessService.AttachLiveStock(Stores.Launch);
        ItemCraftPlannerHost.CatalogProvider = () =>
        {
            var local = ItemCraftAccessService.ListCatalog(
                ItemCraftPlannerHost.ViewModel.SearchText, ItemCraftPlannerHost.ViewModel.Mode,
                ItemCraftPlannerHost.ViewModel.Targets.ToArray());
            // The partner API snapshot is a consultative fallback. A locally
            // admitted version-bound catalogue always takes priority.
            return local.Count > 0 ? local : DofusDbPartnerUiBridge.SearchCatalog(
                ItemCraftPlannerHost.ViewModel.SearchText, ItemCraftPlannerHost.ViewModel.Mode,
                ItemCraftPlannerHost.ViewModel.Targets.ToArray());
        };
        ItemCraftPlannerHost.PlanProvider = request =>
        {
            var selected = (DataContext as MainViewModel)?.BotsVM.SelectedBot;
            return ItemCraftAccessService.BuildPlan(request,
                selected is { Pid: > 0 } ? (uint)selected.Pid : null,
                selected?.Row.Model.Id.ToString()) ?? DofusDbPartnerUiBridge.BuildReviewPlan(request);
        };
        // Real entry into the existing bank -> buyer -> workshop -> listing
        // pipeline. Previously ProductionCharacter was never assigned, so
        // clicking "Lancer" always returned "Aucun personnage branché".
        // The selected bot is read fresh at EACH stage by the production
        // session guard, not captured once when the window is constructed.
        ItemCraftPlannerHost.ViewModel.ProductionCharacter = () =>
        {
            var bot = (DataContext as MainViewModel)?.BotsVM.SelectedBot;
            return bot is { Pid: > 0 } && !bot.Row.IsDisconnected
                ? (uint)bot.Pid : null;
        };
        ItemCraftPlannerHost.ViewModel.ProductionPreviewActions = pid =>
        {
            var goal = ItemCraftPlannerHost.ViewModel.GuidedActivityGoal;
            if (goal is null || !CurrentGuidedProductionAuthority(pid, goal))
                throw new InvalidOperationException(
                    "Source du préflight guidé non observée ; aucune lecture autorisée.");
            return new GuidedProductionReadOnlyActions(
                new ItemCraftProductionLaunch(Stores.Launch, pid),
                () => CurrentGuidedProductionAuthority(pid, goal),
                () => CurrentGuidedProductionAuthority(pid, goal)
                    ? Stores.Launch.ReadGuidedSupplySource(pid) : null);
        };
        ItemCraftPlannerHost.ViewModel.ProductionActions = pid =>
        {
            var goal = ItemCraftPlannerHost.ViewModel.GuidedActivityGoal;
            var original = new ItemCraftProductionLaunch(Stores.Launch, pid,
                assessment => ItemCraftPlannerHost.ViewModel
                    .ObserveProductionListingAssessment(pid, assessment),
                requireGuidedListingEvidence: goal is not null);
            if (goal is null)
            {
                if (!CurrentManualProductionAuthority(pid))
                    throw new InvalidOperationException(
                        "Atelier manuel bloqué : identité/connexion non confirmée ou " +
                        "tentative ActivityRecovery encore incertaine.");
                // Detaching a guided goal MUST NOT bypass a durable pending
                // gather/route after crash. Keep manual action UX, but route
                // its real bank/buy/craft/list port through the same sticky
                // source and activity quarantine guards.
                return new ItemCraftSessionBoundActions(original,
                    () => CurrentManualProductionAuthority(pid));
            }
            if (!CurrentGuidedProductionAuthority(pid, goal, requireWorkflow: true))
                throw new InvalidOperationException(
                    "Objectif guidé : identité ou ActivityRecovery non libérée. " +
                    "Aucune action autorisée ; reprendre le contrôle durable.");
            // The existing ItemCraftProductionRun preflight, durable Started
            // and session-bound adapter remain in place. This extra sticky
            // guard revokes EACH real bank/buy/craft/list call if the source
            // changed or a route action became uncertain after admission.
            return new ItemCraftSessionBoundActions(original,
                () => CurrentGuidedProductionAuthority(pid, goal, requireWorkflow: true),
                () => CurrentGuidedProductionAuthority(pid, goal) &&
                      ItemCraftPlannerHost.ViewModel.GuidedWorkflowMayContinue(goal));
        };
        ItemCraftPlannerHost.ViewModel.ProductionRecipe = gid =>
            GivreClone.App.Services.ItemCraftPlanner.WorkshopLocator.ReadRecipe(
                Stores.Launch.GameData, gid);
        ItemCraftPlannerHost.ViewModel.GuidedSupplyRoutePreparationRequested +=
            OnGuidedSupplyRoutePreparationRequested;
        ItemCraftPlannerHost.ViewModel.GuidedSupplyHdvInspectionRequested +=
            OnGuidedSupplyHdvInspectionRequested;
        ItemCraftPlannerHost.ViewModel.GuidedSupplyHdvBuyStageRequested +=
            OnGuidedSupplyHdvBuyStageRequested;
        ItemCraftPlannerHost.ViewModel.GuidedSupplyBankInspectionRequested +=
            OnGuidedSupplyBankInspectionRequested;
        ItemCraftPlannerHost.ViewModel.GuidedSupplyRecoveryInspectionRequested +=
            OnGuidedSupplyRecoveryInspectionRequested;
        ItemCraftPlannerHost.CatalogDirectorySelected += ItemCraftAccessService.SelectFolder;
        ItemCraftPlannerHost.FolderCatalogProvider = folder =>
        {
            ItemCraftAccessService.SelectFolder(folder);
            return ItemCraftPlannerHost.CatalogProvider?.Invoke() ?? Array.Empty<ItemCraftCatalogEntry>();
        };
        // Import pages/SHA on a background worker to keep the desktop responsive.
        // Never contacts Dofus.exe; never upgrades a review source to certified
        // gameplay or silently authorizes AI/MCP disclosure.
        Opened += async (_, _) =>
        {
            try
            {
                await DofusDbPartnerUiBridge.StartLoadAsync();
                if (ItemCraftPlannerHost.CatalogProvider is { } provider)
                    ItemCraftPlannerHost.LoadCatalog(provider());
                await DofusDbProfessionsUiBridge.StartLoadAsync();
                if (ProfessionPlannerHost.CatalogProvider is { } professionProvider)
                    ProfessionPlannerHost.LoadCatalog(professionProvider());
            }
            catch (Exception ex)
            {
                Logger.Warn("DofusDB local preview unavailable: " + ex.GetType().Name);
            }
        };

        // sons d'UI : clic bouton (nav/click) + ouverture ComboBox
        AddHandler(Button.ClickEvent, OnAnyButtonClick, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnQuickNavigationKeyDown, RoutingStrategies.Tunnel);
        // son de bascule sur les toggles
        AddHandler(Avalonia.Controls.Primitives.ToggleButton.IsCheckedChangedEvent, OnAnyToggle, RoutingStrategies.Bubble, handledEventsToo: true);

        Dialogs.ConfirmRequested += OnConfirmRequested;
        Dialogs.FilePickHandler = PickFileAsync;   // sélecteur natif (ex : localiser Dofus.exe) piloté depuis les VM
        Toasts.Requested += ShowToast;

        // Session de licence terminee cote serveur (pause/ban/expiration/HWID/echecs heartbeat) :
        // re-verrouille l'app (retour au LoginOverlay) avec le message localise.
        if (!MZP19LocalClone.Enabled)
            LicenseService.SessionEnded += msg => Dispatcher.UIThread.Post(() => ShowLogin(msg));

        // Kill switch / version RUNTIME : l'operateur coupe (ou impose une MAJ) PENDANT que l'app tourne -> ecran de
        // blocage EN DIRECT, meme si l'app a ete lancee avant le kill switch (fix faille V6 boot-only).
        if (!MZP19LocalClone.Enabled)
            LicenseService.ControlBlocked += v => Dispatcher.UIThread.Post(() => ShowControlBlock(v));
        // MAJ exigée alors que l'app est déjà en cours d'utilisation : PROPOSÉE (maintenant / au prochain lancement), bots maintenus.
        if (!MZP19LocalClone.Enabled)
            LicenseService.UpdateOffered += v => Dispatcher.UIThread.Post(() => ShowUpdateOffer(v));

        Opened += OnOpened;

        // dark = opaque, sinon blur acrylique
        ApplyTransparencyForTheme();
        ThemeService.Changed += ApplyTransparencyForTheme;

        ApplyDensity();
        ApplyStreamMode();

        // l'indicateur de nav suit la page active
        DataContextChanged += (_, _) =>
        {
            if (_mzp0DiagnosticsOpen) SetMZP0Diagnostics(false);
            if (_guidedProductionBoundBots is { } previous)
                previous.GuidedProductionHandoffRequested -= OnGuidedProductionHandoff;
            _guidedProductionBoundBots = null;
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += OnVmPropertyChanged;
                _guidedProductionBoundBots = vm.BotsVM;
                vm.BotsVM.GuidedProductionHandoffRequested += OnGuidedProductionHandoff;
            }
        };

        // restauration depuis la barre des tâches : re-fondu
        PropertyChanged += (_, ev) =>
        {
            if (ev.Property == WindowStateProperty && WindowState != WindowState.Minimized && RootFade.Opacity == 0)
                Dispatcher.UIThread.Post(() => RootFade.Opacity = 1);
        };
    }

    private bool CurrentManualProductionAuthority(uint pid)
    {
        if (pid == 0 ||
            (DataContext as MainViewModel)?.BotsVM.SelectedBot is not
                { Pid: > 0 } bot || (uint)bot.Pid != pid ||
            bot.Row.IsDisconnected || Stores.Launch.IsLinkSuspended(pid) ||
            Stores.Launch.CurrentLocalProductionDecoderEpoch(pid) <= 0)
            return false;
        int server = Stores.Launch.ServerIdOf(pid);
        long character = Stores.Launch.CharacterId(pid);
        if (server <= 0 || character <= 0) return false;
        try
        {
            var recovery = new ActivityRecoveryDurableStore(
                ActivityRecoveryDurableStore.DefaultRoot())
                .ReadStatus(new ActivityRecoveryScope(server, character));
            return GuidedActivityProductionStore.MayUseManualProduction(
                       recovery, server, character) &&
                   GuidedProductionWorkflowLedger.MayBeginIndependentManualProduction(
                       _guidedWorkflowStore.Read(server, character)) &&
                   Stores.Launch.ServerIdOf(pid) == server &&
                   Stores.Launch.CharacterId(pid) == character &&
                   !Stores.Launch.IsLinkSuspended(pid);
        }
        catch (Exception) { return false; }
    }

    private bool CurrentGuidedProductionAuthority(uint pid, GuidedActivityProductionGoal goal,
        bool requireWorkflow = false)
    {
        if (pid == 0 || goal is not { Valid: true } ||
            !Equals(ItemCraftPlannerHost.ViewModel.GuidedActivityGoal, goal) ||
            !ItemCraftPlannerHost.ViewModel.GuidedActivityTargetStillMatches(goal) ||
            (DataContext as MainViewModel)?.BotsVM.SelectedBot is not
                { Pid: > 0 } selected || (uint)selected.Pid != pid ||
            selected.Row.IsDisconnected ||
            Stores.Launch.ServerIdOf(pid) != goal.ServerId ||
            Stores.Launch.CharacterId(pid) != goal.CharacterId)
            return false;
        try
        {
            var scope = new ActivityRecoveryScope(goal.ServerId, goal.CharacterId);
            var recovery = new ActivityRecoveryDurableStore(
                ActivityRecoveryDurableStore.DefaultRoot()).ReadStatus(scope);
            var admission = _guidedActivityStore.InspectWithRecovery(
                goal.ServerId, goal.CharacterId, recovery);
            return admission.MayPrepareProduction &&
                   Equals(admission.Goal, goal) &&
                   (!requireWorkflow ||
                    ItemCraftPlannerHost.ViewModel.GuidedWorkflowMayBind(goal)) &&
                   Stores.Launch.ServerIdOf(pid) == goal.ServerId &&
                   Stores.Launch.CharacterId(pid) == goal.CharacterId;
        }
        catch (Exception) { return false; }
    }

    private void OnGuidedProductionHandoff(GuidedProductionHandoff handoff)
    {
        if (DataContext is not MainViewModel vm ||
            !ReferenceEquals(vm.BotsVM, _guidedProductionBoundBots) ||
            vm.BotsVM.SelectedBot is not { Pid: > 0 } bot ||
            bot.Row.IsDisconnected || handoff.Pid != (uint)bot.Pid ||
            Stores.Launch.ServerIdOf(handoff.Pid) != handoff.Goal.ServerId ||
            Stores.Launch.CharacterId(handoff.Pid) != handoff.Goal.CharacterId)
            return;
        // Import goes through the SAME verified catalog and real
        // AddSelection/RefreshPlan commands as human-selected crafting.
        // No craft, bank, HDV or Lua transport is dispatched here.
        if (!ItemCraftPlannerHost.ViewModel.TryImportGuidedActivity(handoff.Goal))
        {
            Toasts.Show(ItemCraftPlannerHost.ViewModel.GuidedActivityStatus,
                success: false, sound: false);
            return;
        }
        if (!CurrentGuidedProductionAuthority(handoff.Pid, handoff.Goal))
        {
            ItemCraftPlannerHost.ViewModel.ClearGuidedActivityCommand.Execute(null);
            Toasts.Show("Objectif invalidé pendant l'import : aucune fabrication.",
                success: false, sound: false);
            return;
        }
        vm.OpenItemCraft();
    }

    /// <summary>
    /// V13 real atelier→Lua handoff. Reuses the existing strict one-shot route
    /// command (including its human confirmation and script persistence), not
    /// a new navigation engine or an implicit gather dispatch. A stale stock
    /// snapshot, map generation or replaced actor cannot prepare a script.
    /// </summary>
    private void OnGuidedSupplyRoutePreparationRequested(GuidedSupplyRoutePreparation request)
    {
        if (DataContext is not MainViewModel vm ||
            !ReferenceEquals(vm.BotsVM, _guidedProductionBoundBots) ||
            vm.BotsVM.SelectedBot is not { Pid: > 0 } selected ||
            (uint)selected.Pid != request.Pid || selected.Row.IsDisconnected ||
            !CurrentGuidedProductionAuthority(request.Pid, request.Goal))
        {
            Toasts.Show("Route refusée : personnage ou session remplacés ; aucune émission.",
                success: false, sound: false);
            return;
        }
        var flow = _guidedWorkflowStore.Read(request.Goal.ServerId,
            request.Goal.CharacterId);
        long map = Stores.Launch.CurrentMap(request.Pid) ?? 0;
        var world = Stores.World.Get(request.Pid);
        var observed = Stores.Launch.ReadGuidedSupplySource(request.Pid);
        if (!request.Goal.Gather || flow.Phase != GuidedProductionWorkflowPhase.Blocked ||
            flow.GoalHash != GuidedProductionWorkflowLedger.Fingerprint(request.Goal) ||
            flow.RequestHash != request.RequestHash ||
            observed is null ||
            world.MapId != map ||
            world.MapGeneration != observed.SourceMapGeneration ||
            observed.SourceMap != map ||
            !world.SelfReadyFor(map) || !world.CellsReadyFor(map) ||
            !ItemCraftPlannerHost.ViewModel.GuidedSupplyMayPrepareRoute(request))
        {
            Toasts.Show("Route refusée : approvisionnement, carte, génération ou cible modifiés. " +
                "Vérifier les ressources puis le trajet courant.",
                success: false, sound: false);
            return;
        }
        // The same frozen supply GID is rechecked at modal confirmation,
        // with the exact staged plan/revision and current actor/source.
        // It never silently re-authors the V11 objective on a new map.
        bool armed = vm.BotsVM.ArmStrictActivityRouteForGuidedSupply(request,
            () => CurrentGuidedProductionAuthority(request.Pid, request.Goal) &&
                  ItemCraftPlannerHost.ViewModel.GuidedSupplyMayPrepareRoute(request));
        if (!armed)
        {
            Toasts.Show(vm.BotsVM.StrictRouteDraftStatus,
                success: false, sound: false);
            return;
        }
        // Existing command rechecks world/actor/map/editor identity before
        // the modal and again in the modal callback, plus the V14 stock guard.
        vm.BotsVM.PrepareStrictActivityRouteCommand.Execute(null);
        Toasts.Show(vm.BotsVM.StrictRouteDraftStatus,
            success: vm.BotsVM.StrictRouteDraftStatus.StartsWith("Préparation :",
                StringComparison.Ordinal), sound: false);
    }

    /// <summary>
    /// Read-only production→HDV panel handoff; no auto-selection of an order,
    /// repricing, sale, tax release or assumed server receipt. The existing
    /// HDV panel owns its own precise inspect→human review workflow.
    /// </summary>
    private void OnGuidedSupplyHdvBuyStageRequested(GuidedSupplyHdvBuyStage request)
    {
        if (DataContext is not MainViewModel vm) return;
        var hdv = vm.BotsVM.Hdv;
        bool Current()
        {
            try
            {
                return ReferenceEquals(DataContext, vm) &&
                    ReferenceEquals(vm.BotsVM, _guidedProductionBoundBots) &&
                    ReferenceEquals(vm.BotsVM.Hdv, hdv) &&
                    vm.BotsVM.SelectedBot is { Pid: > 0 } bot &&
                    (uint)bot.Pid == request.Pid && !bot.Row.IsDisconnected &&
                    CurrentGuidedProductionAuthority(request.Pid, request.Goal,
                        requireWorkflow: true) &&
                    Equals(_guidedActivityStore.Read(request.Goal.ServerId,
                        request.Goal.CharacterId).Goal, request.Goal) &&
                    ItemCraftPlannerHost.ViewModel.IsCurrentGuidedV17BuyStage(
                        request.Pid, request.Goal, request.Choice);
            }
            catch (Exception) { return false; }
        }

        if (!Current())
        {
            Toasts.Show("Achat guidé refusé : objectif, source ou " +
                "journal V17 non concordants. Aucun achat envoyé.",
                success: false, sound: false);
            return;
        }
        if (!vm.ExecuteQuickNavigation("hdv") || !Current())
        {
            Toasts.Show("Achat guidé refusé : onglet HDV indisponible ou " +
                "source remplacée pendant la navigation. Aucun achat envoyé.",
                success: false, sound: false);
            return;
        }
        if (!hdv.TryStageGuidedSupplyBuy(request.Pid, request.Goal.ServerId,
                request.Goal.CharacterId, request.Choice, Current, out string reason))
        {
            Toasts.Show("Achat guidé refusé : " + reason +
                ". Contrôler le journal ou la règle existante ; aucun achat envoyé.",
                success: false, sound: false);
            return;
        }
        Toasts.Show("HDV : " + reason +
            " Budget, offres et lots restent soumis à votre validation ; aucun achat envoyé.",
            success: true, sound: false);
    }

    private void OnGuidedSupplyHdvInspectionRequested(GuidedSupplyHdvInspection request)
    {
        // A completed V9 run may no longer have a current live source. The
        // listing journal remains inspectable, but never on another selected
        // character or after the navigation itself changes the owner.
        var sourceBefore = Stores.Launch.ReadGuidedSupplySource(request.Pid);
        if (DataContext is not MainViewModel vm ||
            !ReferenceEquals(vm.BotsVM, _guidedProductionBoundBots) ||
            vm.BotsVM.SelectedBot is not { Pid: > 0 } selected ||
            (uint)selected.Pid != request.Pid || selected.Row.IsDisconnected ||
            Stores.Launch.ServerIdOf(request.Pid) != request.Goal.ServerId ||
            Stores.Launch.CharacterId(request.Pid) != request.Goal.CharacterId ||
            !Equals(ItemCraftPlannerHost.ViewModel.GuidedActivityGoal, request.Goal) ||
            !Equals(_guidedActivityStore.Read(request.Goal.ServerId,
                request.Goal.CharacterId).Goal, request.Goal))
        {
            Toasts.Show("HDV : source ou objectif remplacé ; consultation refusée.",
                success: false, sound: false);
            return;
        }
        if (!vm.ExecuteQuickNavigation("hdv"))
        {
            Toasts.Show("HDV : onglet existant non disponible, aucune opération envoyée.",
                success: false, sound: false);
            return;
        }
        if (!ReferenceEquals(DataContext, vm) ||
            !ReferenceEquals(vm.BotsVM, _guidedProductionBoundBots) ||
            vm.BotsVM.SelectedBot is not { Pid: > 0 } after ||
            (uint)after.Pid != request.Pid || after.Row.IsDisconnected ||
            Stores.Launch.ServerIdOf(request.Pid) != request.Goal.ServerId ||
            Stores.Launch.CharacterId(request.Pid) != request.Goal.CharacterId ||
            !Equals(ItemCraftPlannerHost.ViewModel.GuidedActivityGoal, request.Goal) ||
            Stores.Launch.ReadGuidedSupplySource(request.Pid) != sourceBefore)
        {
            Toasts.Show("HDV : personnage/source remplacé pendant la navigation ; " +
                "aucun listing ni reçu attribué.", success: false, sound: false);
            return;
        }
        Toasts.Show("HDV : consultation du journal et des annonces. " +
            "Choisir l'ordre exact puis inspecter ; aucune vente relancée.",
            success: true, sound: false);
    }

    /// <summary>
    /// V15: actual atelier -> existing Lua recovery command. Pending recovery
    /// cannot satisfy CurrentGuidedProductionAuthority (by design), so use
    /// only exact selected owner and persisted goal checks here. NO clear/send.
    /// </summary>
    private async void OnGuidedSupplyRecoveryInspectionRequested(
        GuidedSupplyRecoveryInspection request)
    {
        if (DataContext is not MainViewModel vm ||
            !ReferenceEquals(vm.BotsVM, _guidedProductionBoundBots) ||
            vm.BotsVM.SelectedBot is not { Pid: > 0 } selected ||
            selected.Row.IsDisconnected || (uint)selected.Pid != request.Pid ||
            Stores.Launch.ServerIdOf(request.Pid) != request.Goal.ServerId ||
            Stores.Launch.CharacterId(request.Pid) != request.Goal.CharacterId ||
            !Equals(ItemCraftPlannerHost.ViewModel.GuidedActivityGoal, request.Goal) ||
            !Equals(_guidedActivityStore.Read(request.Goal.ServerId,
                request.Goal.CharacterId).Goal, request.Goal))
        {
            Toasts.Show("Reprise Lua : objectif ou personnage remplacé, inspection refusée.",
                success: false, sound: false);
            return;
        }
        ActivityRecoveryDurableStatus recovery;
        try
        {
            recovery = new ActivityRecoveryDurableStore(
                ActivityRecoveryDurableStore.DefaultRoot()).ReadStatus(
                    new ActivityRecoveryScope(request.Goal.ServerId, request.Goal.CharacterId));
        }
        catch (Exception)
        {
            Toasts.Show("Reprise Lua : journal inaccessible, ne pas redémarrer d'activité.",
                success: false, sound: false);
            return;
        }
        if (recovery.State == ActivityRecoveryDurableState.Clear ||
            !vm.ExecuteQuickNavigation("lua"))
        {
            Toasts.Show("Reprise Lua : état changé ou onglet indisponible ; réinspecter.",
                success: false, sound: false);
            return;
        }
        // Read the real host's durable/source-bound projection. No gameplay
        // send, no release of Pending. The legacy Lua inspection is available
        // even when no ingredient GID can be attributed to the pending marker.
        if (request.IngredientGid <= 0 || request.Required <= 0)
        {
            vm.BotsVM.InspectActivityRecoveryCommand.Execute(null);
            Toasts.Show("Reprise Lua : GID d'ingrédient indisponible ; " +
                "journal durable consultable, aucun replay.", success: false, sound: false);
            return;
        }
        try
        {
            var observed = await vm.BotsVM.InspectGuidedSupplyV16Async(
                request.Pid, request.Goal.ServerId, request.Goal.CharacterId,
                request.IngredientGid, request.Required, request.Goal.MaxPods);
            if (observed is null) return;
            // A selected bot can change during the disk/host observation:
            // neither publish an old actor's result nor show a success toast.
            if (!ReferenceEquals(DataContext, vm) ||
                !ReferenceEquals(vm.BotsVM, _guidedProductionBoundBots) ||
                vm.BotsVM.SelectedBot is not { Pid: > 0 } stillSelected ||
                stillSelected.Row.IsDisconnected ||
                (uint)stillSelected.Pid != request.Pid ||
                Stores.Launch.ServerIdOf(request.Pid) != request.Goal.ServerId ||
                Stores.Launch.CharacterId(request.Pid) != request.Goal.CharacterId ||
                !Equals(ItemCraftPlannerHost.ViewModel.GuidedActivityGoal,
                    request.Goal))
                return;
            ItemCraftPlannerHost.ViewModel.ObserveGuidedSupplyV16Recovery(
                request.Pid, request.Goal,
                vm.BotsVM.GuidedV16ActivityInspectionText);
            Toasts.Show("Reprise Lua : preuve d'activité inspectée. " +
                "Aucune nouvelle admission sans rapprochement indépendant.",
                success: true, sound: false);
        }
        catch (Exception)
        {
            Toasts.Show("Reprise Lua : consultation échouée ; " +
                "marqueur durable conservé, aucun envoi.", success: false, sound: false);
        }
    }

    private void OnGuidedSupplyBankInspectionRequested(GuidedSupplyBankInspection request)
    {
        var sourceBefore = Stores.Launch.ReadGuidedSupplySource(request.Pid);
        if (DataContext is not MainViewModel vm ||
            !ReferenceEquals(vm.BotsVM, _guidedProductionBoundBots) ||
            vm.BotsVM.SelectedBot is not { Pid: > 0 } selected ||
            (uint)selected.Pid != request.Pid || selected.Row.IsDisconnected ||
            Stores.Launch.ServerIdOf(request.Pid) != request.Goal.ServerId ||
            Stores.Launch.CharacterId(request.Pid) != request.Goal.CharacterId ||
            !Equals(ItemCraftPlannerHost.ViewModel.GuidedActivityGoal, request.Goal) ||
            !Equals(_guidedActivityStore.Read(request.Goal.ServerId,
                request.Goal.CharacterId).Goal, request.Goal) ||
            !CurrentGuidedProductionAuthority(request.Pid, request.Goal) ||
            request.Choice is not null &&
                !ItemCraftPlannerHost.ViewModel.IsCurrentGuidedV18BankInspection(
                    request.Pid, request.Goal, request.Choice) ||
            sourceBefore is null)
        {
            Toasts.Show("Banque : source, reprise ou objectif invalidés ; aucun transfert.",
                success: false, sound: false);
            return;
        }
        if (!vm.ExecuteQuickNavigation("bank"))
        {
            Toasts.Show("Banque : onglet non disponible, aucun envoi.",
                success: false, sound: false);
            return;
        }
        if (!ReferenceEquals(DataContext, vm) ||
            !ReferenceEquals(vm.BotsVM, _guidedProductionBoundBots) ||
            vm.BotsVM.SelectedBot is not { Pid: > 0 } after ||
            (uint)after.Pid != request.Pid || after.Row.IsDisconnected ||
            !CurrentGuidedProductionAuthority(request.Pid, request.Goal) ||
            request.Choice is not null &&
                !ItemCraftPlannerHost.ViewModel.IsCurrentGuidedV18BankInspection(
                    request.Pid, request.Goal, request.Choice) ||
            Stores.Launch.ReadGuidedSupplySource(request.Pid) != sourceBefore)
        {
            Toasts.Show("Banque : source remplacée pendant la navigation ; " +
                "revenir inspecter la reprise durable, aucun transfert.",
                success: false, sound: false);
            return;
        }
        Toasts.Show("Banque/pods : consultation manuelle. " +
            "Utiliser « Retour banque / Lua / HDV · réinspecter » après intervention ; aucun retrait émis.",
            success: true, sound: false);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.AdvancedToolsVisible) && sender is MainViewModel toolVm)
        {
            if (toolVm.AdvancedToolsVisible)
            {
                toolVm.RefreshSocketUiSummary();
                _socketUiSummaryTimer.Start();
            }
            else _socketUiSummaryTimer.Stop();
        }
        else if (e.PropertyName is nameof(MainViewModel.ActivePage) or nameof(MainViewModel.CurrentPage))
        {
            // Une vraie navigation du MainViewModel reprend la main sur le contenu.
            if (sender is MainViewModel vm && vm.ProfessionPlannerVisible)
                vm.ProfessionPlannerVisible = false;
            if (sender is MainViewModel craftVm && craftVm.ItemCraftVisible)
                craftVm.ItemCraftVisible = false;
            if (_mzp0DiagnosticsOpen) SetMZP0Diagnostics(false);
            else QueueIndicator();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsClassicSkin) && _mzp0DiagnosticsOpen)
            SetMZP0Diagnostics(true);
    }

    // Toast transitoire (son + visuel), déclenché par Toasts.Show depuis les VMs (ex: interface ajoutée/retirée).
    public void ShowToast(string message, bool success = true, bool sound = true)
    {
        ToastText.Text = message;
        if (this.TryFindResource(success ? "SuccessBrush" : "AmberBrush", out var br) && br is IBrush brush)
            ToastIcon.Foreground = brush;
        if (this.TryFindResource(success ? "IconCheckCircle" : "IconInfo", out var g) && g is Geometry geo)
            ToastIcon.Data = geo;

        if (sound) AudioService.PlayUi(AudioService.Notify);
        // valeurs locales directes : la transition Opacity/RenderTransform du style Border.toast anime l'entrée
        Toast.Opacity = 1;
        Toast.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translateY(0px)");
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer!.Stop();
            Toast.Opacity = 0;
            Toast.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translateY(-12px)");
        };
        _toastTimer.Start();
    }

    // ---- Mode économe : une fenêtre qui n'est plus au premier plan (un bot tourne en arrière-plan) n'a aucune raison de dessiner des animations décoratives ----
    private SeasonalOverlay? _seasonal;
    private DispatcherTimer? _idleEffectsTimer;
    private bool _effectsPaused;

    private void InitPowerSaver()
    {
        _idleEffectsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _idleEffectsTimer.Tick += (_, _) => { _idleEffectsTimer!.Stop(); SetEffectsPaused(true); };
        Deactivated += (_, _) => { _idleEffectsTimer!.Stop(); _idleEffectsTimer.Start(); };
        Activated += (_, _) => { _idleEffectsTimer!.Stop(); SetEffectsPaused(false); };
    }

    private void SetEffectsPaused(bool paused)
    {
        if (_effectsPaused == paused) return;
        _effectsPaused = paused;
        Particles.IsVisible = !paused;        // les 22 points animés ne sont plus dessinés ni animés
        if (_seasonal is not null) _seasonal.Paused = paused;
    }

    // démarrage : fondu + particules + placement de l'indicateur
    private void OnOpened(object? sender, EventArgs e)
    {
        if (_alreadyOpened) return;
        _alreadyOpened = true;

        // Les effets optionnels restent disponibles dans les reglages, sans animation de fond au lancement.
        InitPowerSaver();

        var fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        fade.Tick += (_, _) => { fade.Stop(); RootFade.Opacity = 1; };
        fade.Start();

        // Shell flouté derrière le splash puis le gate, jusqu'à l'entrée dans l'app (comme PyxLauncher).
        ApplyShellBlur();

        // Splash ~1,5 s -> fondu croisé vers : langue (1er lancement) / auto-login / saisie manuelle.
        // Le contenu (login) est monté SOUS le splash opaque -> aucun flash à la sortie du splash.
        // Launcher control Ikuware (kill switch / version requise) : GET /version pré-login, fail-open.
        var versionTask = MZP19LocalClone.Enabled
            ? Task.FromResult(VersionVerdict.Permissive)
            : LicenseService.CheckVersionAsync();
        // Surveillance RUNTIME du kill switch : re-poll pendant toute la vie de l'app (le check boot-only ci-dessus ne
        // couvre PAS un kill activé APRÈS le lancement -> faille V6). Bloque en direct via ControlBlocked.
        if (!MZP19LocalClone.Enabled) LicenseService.StartVersionWatch();
        bool firstRun = !AppConfig.Current.Configured;
        bool autoLogin = !MZP19LocalClone.Enabled && !firstRun && AppConfig.Current.AutoLogin && !string.IsNullOrEmpty(AppConfig.Current.LicenseProtected);
        var splash = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        splash.Tick += async (_, _) =>
        {
            splash.Stop();

            // Kill switch / version : si l'opérateur a coupé (ou exige une MAJ), on BLOQUE avant tout login.
            var verdict = await versionTask;
            if (!verdict.Ok)
            {
                ShowControlBlock(verdict);
                SplashOverlay.Opacity = 0;
                SplashOverlay.IsHitTestVisible = false;
                var hideK = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(480) };
                hideK.Tick += (_, _) => { hideK.Stop(); SplashOverlay.IsVisible = false; };
                hideK.Start();
                return;
            }

            if (firstRun)
            {
                LangOverlay.Classes.Add("modalOpen");
                LangOverlay.IsHitTestVisible = true;
                AudioService.PlayUi(AudioService.ModalIn);
            }
            else if (!autoLogin && !MZP19LocalClone.Enabled)
            {
                ShowLogin();
            }
            // Étape "reveal" : 240 ms pour que l'écran du dessous finisse son fondu AVANT de faire
            // disparaître le splash (comme PyxLauncher -> pas de cross-fade brutal).
            var reveal = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
            reveal.Tick += (_, _) =>
            {
                reveal.Stop();
                SplashOverlay.Opacity = 0;
                SplashOverlay.IsHitTestVisible = false;
                var hide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(480) };
                hide.Tick += (_, _) =>
                {
                    hide.Stop();
                    SplashOverlay.IsVisible = false;
                    if (MZP19LocalClone.Enabled)
                    {
                        if (!firstRun) EnterApp();
                    }
                    else if (autoLogin) _ = DoAutoLogin();
                    else if (!firstRun) LicenseBox.Focus();
                };
                hide.Start();
            };
            reveal.Start();
        };
        splash.Start();

        // Pré-extraction des cartes (cache disque) au 1er lancement / après un patch Dofus : le modal de progression
        // n'apparaît QUE s'il y a du travail (sinon silencieux). Non bloquant, en tâche de fond, sous le gate licence.
        _ = (DataContext as MainViewModel)?.StartMapPrewarmAsync();

        EventHandler? layoutH = null;
        layoutH = (_, _) =>
        {
            if (NavAccountsBtn.Bounds.Height > 0)
            {
                MoveIndicatorToActive();
                Sidebar.LayoutUpdated -= layoutH;
            }
        };
        Sidebar.LayoutUpdated += layoutH;
    }

    private void OnToggleSidebar(object? sender, RoutedEventArgs e)
    {
        if (Sidebar.Classes.Contains("expanded")) Sidebar.Classes.Remove("expanded");
        else Sidebar.Classes.Add("expanded");
        QueueIndicator();
    }

    // Diagnostics MZP0 : navigation purement visuelle. Aucune source, session ou
    // commande Givre n'est raccordée ici ; le panneau reste NON ATTESTÉ par défaut.
    private void OnMZP0ToggleDiagnostics(object? sender, RoutedEventArgs e)
        => SetMZP0Diagnostics(!_mzp0DiagnosticsOpen);

    private void SetMZP0Diagnostics(bool open)
    {
        if (open && DataContext is not MainViewModel) return;
        if (!open && _mzp0DiagnosticsOpen)
            MZP0DiagnosticsPanel.Operator.Unbind(); // abandon explicite de toute preuve antérieure
        _mzp0DiagnosticsOpen = open;
        MZP0OperatorNavigation.Apply(MZP0DiagnosticsHost, MZP0NavDiagnosticsBtn,
            MZP0TitleDiagnosticsBtn, open, (DataContext as MainViewModel)?.IsClassicSkin ?? false);
        QueueIndicator();
    }

    private void QueueIndicator() => Dispatcher.UIThread.Post(MoveIndicatorToActive, DispatcherPriority.Loaded);

    private void MoveIndicatorToActive()
    {
        Control? btn = _mzp0DiagnosticsOpen ? MZP0NavDiagnosticsBtn : ((DataContext as MainViewModel)?.ActivePage switch
        {
            "dashboard" => NavDashboardBtn,
            "accounts" => NavAccountsBtn,
            "proxys" => NavProxysBtn,
            "adapters" => NavAdaptersBtn,
            "settings" => NavSettingsBtn,
            _ => null,
        });
        MoveIndicator(btn);
    }

    private void MoveIndicator(Control? btn)
    {
        if (btn is null) { NavIndicator.Opacity = 0; return; }
        if (NavIndicator.Parent is not Visual parent) return;
        var p = btn.TranslatePoint(new Point(0, 0), parent);
        if (p is null || btn.Bounds.Height <= 0) return;
        double y = p.Value.Y + (btn.Bounds.Height - NavIndicator.Height) / 2;
        NavIndicator.Margin = new Thickness(0, y, 0, 0);
        NavIndicator.Opacity = 1;
    }

    // chrome fenêtre : move / minimize / maximize / close / resize
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var src = e.Source as Visual;
        while (src is not null)
        {
            if (src is Button || src is ComboBox) return;
            src = src.GetVisualParent();
        }
        BeginMoveDrag(e);
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e) => ToggleMaximize();

    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        // Only the chrome button's own click may minimize the shell. Do not
        // leave a delayed window-state change alive across modal transitions.
        if (sender is not Button button || !ReferenceEquals(e.Source, button) || !button.IsEffectivelyVisible) return;
        e.Handled = true;
        WindowState = WindowState.Minimized;
    }

    private void OnMaximize(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        // coins ronds seulement en fenêtré (maximisé à coins ronds = bureau visible)
        RootFade.CornerRadius = new CornerRadius(WindowState == WindowState.Maximized ? 0 : 12);
        // resize natif instantané -> petit zoom 0.97->1 pour adoucir
        RootFade.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("scale(0.97)");
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        t.Tick += (_, _) => { t.Stop(); RootFade.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("scale(1)"); };
        t.Start();
    }

    private bool _closing;
    private void OnClose(object? sender, RoutedEventArgs e)
    {
        if (_closing) return;
        _closing = true;
        RootFade.Opacity = 0;   // fondu de sortie avant fermeture
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        t.Tick += (_, _) => { t.Stop(); Close(); };
        t.Start();
    }

    private void OnResizeGrip(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (sender is Control { Tag: string tag } && Enum.TryParse<WindowEdge>(tag, out var edge))
            BeginResizeDrag(edge, e);
    }

    private void ApplyTransparencyForTheme()
    {
        TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
    }

    // applique l'échelle de densité au contenu (pas la barre de titre)
    public void ApplyDensity()
    {
        if (this.FindControl<LayoutTransformControl>("DensityHost") is not { } host) return;
        double s = AppConfig.Current.DensityScale;
        host.LayoutTransform = new ScaleTransform(s, s);
        if (this.FindControl<LayoutTransformControl>("MZP0DiagnosticsHost") is { } diagnostics)
            diagnostics.LayoutTransform = new ScaleTransform(s, s);
    }

    // Mode Recording/Stream : (dé)pose la classe "stream" sur la fenêtre -> le style "Window.stream *.pii" floute
    // toute PII taguée (identifiant/personnage/serveur/IP/HWID). Purement visuel, réactif au toggle des Réglages.
    public void ApplyStreamMode()
    {
        if (AppConfig.Current.StreamMode) { if (!Classes.Contains("stream")) Classes.Add("stream"); }
        else Classes.Remove("stream");
    }

    private void OnAnyButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_mzp0DiagnosticsOpen && e.Source is Button nav &&
            MZP0OperatorNavigation.IsExistingPageNavigation(nav, NavAccountsBtn, NavBotsBtn,
                NavTeamsBtn, NavProxysBtn, NavAdaptersBtn, NavSettingsBtn, NavMyAccountBtn))
            SetMZP0Diagnostics(false);
        if (e.Source is Avalonia.Controls.Primitives.ToggleButton) return;   // géré par OnAnyToggle
        if (e.Source is Button b && b.Classes.Contains("silent")) return;    // micro-actions (notifications) : pas de son de clic
        if (e.Source is Button nb && nb.Classes.Contains("nav"))
            AudioService.PlayUi(AudioService.Nav);
        else
            AudioService.PlayUi(AudioService.Click);
    }

    // Clic sur le voile du centre de notifications (en dehors du panneau) -> ferme le panneau.
    private void OnNotifScrimPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm) vm.Notifications.IsOpen = false;
    }

    private void OnAnyToggle(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Avalonia.Controls.Primitives.ToggleButton tb)
            AudioService.PlayUi(tb.IsChecked == true ? AudioService.ToggleOn : AudioService.ToggleOff);
    }

    private Action? _confirmAction;

    // Sélecteur de fichier natif demandé par un VM (Dialogs.PickFileAsync) : la fenêtre EST le TopLevel -> StorageProvider.
    // Renvoie le chemin LOCAL choisi, ou null si annulé/indisponible. Utilisé pour localiser Dofus.exe hors emplacement standard.
    private async Task<string?> PickFileAsync(FilePickRequest r)
    {
        var storage = StorageProvider;
        if (storage is null) return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = r.Title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(r.FilterName) { Patterns = r.Patterns },
                FilePickerFileTypes.All,
            },
        });
        return files.Count > 0 ? (files[0].TryGetLocalPath() ?? files[0].Name) : null;
    }

    private void OnConfirmRequested(ConfirmRequest r)
    {
        ConfirmTitle.Text = r.Title;
        ConfirmBody.Text = r.Body;
        ConfirmOkText.Text = r.OkText;
        if (this.TryFindResource(r.Danger ? "IconTrash" : "IconCheck", out var okg) && okg is Geometry og) ConfirmOkIcon.Data = og;
        if (this.TryFindResource(r.Danger ? "IconTrash" : "IconHelp", out var hg) && hg is Geometry hgeo) ConfirmIcon.Data = hgeo;
        _confirmAction = r.OnConfirm;
        ConfirmOverlay.Classes.Add("modalOpen");
        ConfirmOverlay.IsHitTestVisible = true;
        AudioService.PlayUi(AudioService.ModalIn);
    }

    private void CloseConfirm()
    {
        AudioService.PlayUi(AudioService.ModalOut);
        ConfirmOverlay.Classes.Remove("modalOpen");
        ConfirmOverlay.IsHitTestVisible = false;
    }

    private void OnConfirmOk(object? sender, RoutedEventArgs e)
    {
        var a = _confirmAction; _confirmAction = null;
        CloseConfirm();
        a?.Invoke();
    }

    private void OnConfirmCancel(object? sender, RoutedEventArgs e) => CloseConfirm();
    private void OnConfirmBackdrop(object? sender, PointerPressedEventArgs e) => CloseConfirm();

    // choix de la langue (1er lancement)
    private void OnPickLanguage(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string code }) return;
        Loc.Set(code);   // applique + persiste + Configured=true
        AudioService.PlayUi(AudioService.ModalOut);
        LangOverlay.Classes.Remove("modalOpen");
        LangOverlay.IsHitTestVisible = false;
        if (MZP19LocalClone.Enabled) EnterApp();
        else ShowLogin();   // distribution serveur : gate de licence
    }

    // ===================== Gate de licence (clé Ikuware) — fidèle au PyxLauncher =====================

    // Applique le flou sur le shell (derrière le splash/gate).
    private void ApplyShellBlur()
    {
        _shellBlur ??= new BlurEffect { Radius = 14 };
        ShellRoot.Effect = _shellBlur;
    }

    // Retire le flou en douceur à l'entrée dans l'app.
    private void RevealWithBlur()
    {
        var blur = _shellBlur ?? new BlurEffect { Radius = 12 };
        ShellRoot.Effect = blur;
        var anim = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        anim.Tick += (_, _) =>
        {
            blur.Radius -= 0.9;
            if (blur.Radius <= 0) { anim.Stop(); ShellRoot.Effect = null; _shellBlur = null; }
        };
        anim.Start();
    }

    // Affiche le gate (shell flouté + carte animée). Erreur optionnelle -> modal rouge.
    private void ShowLogin(string? error = null)
    {
        if (DataContext is MainViewModel vm) vm.CloseQuickNavigationCommand.Execute(null);
        ApplyShellBlur();
        SetLoginBusy(false);
        LoginOverlay.IsVisible = true;
        LoginOverlay.IsHitTestVisible = true;
        if (!LoginOverlay.Classes.Contains("modalOpen")) LoginOverlay.Classes.Add("modalOpen");
        AudioService.PlayUi(AudioService.ModalIn);
        if (!string.IsNullOrEmpty(error)) ShowLoginErrorModal(error);
        Dispatcher.UIThread.Post(() => LicenseBox.Focus(), DispatcherPriority.Loaded);
    }

    // Masque le gate (fondu de sortie).
    private void HideLogin()
    {
        LoginOverlay.Classes.Remove("modalOpen");
        LoginOverlay.IsHitTestVisible = false;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        t.Tick += (_, _) => { t.Stop(); LoginOverlay.IsVisible = false; };
        t.Start();
    }

    // Succès : ferme le modal + le gate, révèle le shell (dé-floute).
    private void EnterApp()
    {
        CloseLoginModal();
        HideLogin();
        RevealWithBlur();
        AudioService.PlayUi(AudioService.ModalOut);
    }

    // Validation manuelle (bouton / Entrée) : modal connexion -> succès -> entrée ; erreur -> modal rouge.
    private async void OnValidateLicense(object? sender, RoutedEventArgs e)
    {
        if (_validating || !LoginOverlay.IsVisible) return;
        var key = (LicenseBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key)) { ShowLoginErrorModal(Loc.T("L.Login.ErrInvalid")); return; }

        SetLoginBusy(true);
        ShowConnectingModal(Loc.T("L.Login.Connecting"));
        var result = await LicenseService.ValidateAsync(key);

        if (!result.Success)
        {
            SetLoginBusy(false);
            ShowLoginErrorModal(string.IsNullOrEmpty(result.Message) ? Loc.T("L.Login.ErrRejected") : result.Message);
            return;
        }

        LicenseService.SaveLoginPreference(key, RememberCheck.IsChecked == true);
        SetLoginModalSuccess(Loc.T("L.Login.Success"));
        await Task.Delay(900);
        SetLoginBusy(false);
        EnterApp();
    }

    // Reconnexion auto (au démarrage) : modal connexion -> succès / retombe sur la saisie manuelle.
    private async Task DoAutoLogin()
    {
        var key = LicenseService.LoadSavedKey();
        if (string.IsNullOrEmpty(key)) { ShowLogin(); return; }

        ShowLogin();
        RememberCheck.IsChecked = true;
        SetLoginBusy(true);
        ShowConnectingModal(Loc.T("L.Login.Connecting"));
        var result = await LicenseService.ValidateAsync(key);

        if (!result.Success)
        {
            // Clé refusée pour de bon : oubliée. Coupure réseau, erreur de signature ou d'horloge : la clé reste mémorisée
            // (avant, un simple aléa réseau au démarrage obligeait à la ressaisir).
            if (result.KeyRejected)
            {
                LicenseService.ClearSavedKey();
                RememberCheck.IsChecked = false;
            }
            SetLoginBusy(false);
            ShowLoginErrorModal(result.Message);
            return;
        }

        SetLoginModalSuccess(Loc.T("L.Login.Success"));
        await Task.Delay(800);
        SetLoginBusy(false);
        EnterApp();
    }

    // Verrouille la saisie pendant la validation (évite un double-submit).
    private void SetLoginBusy(bool busy)
    {
        _validating = busy;
        ValidateBtn.IsEnabled = !busy;
        LicenseBox.IsEnabled = !busy;
    }

    // ---- Modal de statut du login (spinner ambre / check vert / croix rouge), animé comme les autres ----

    private void OpenLoginModal(bool dismissable)
    {
        _loginModalDismissable = dismissable;
        LoginModal.IsVisible = true;
        LoginModal.IsHitTestVisible = true;
        if (!LoginModal.Classes.Contains("modalOpen")) LoginModal.Classes.Add("modalOpen");
    }

    private void CloseLoginModal()
    {
        LoginModal.Classes.Remove("modalOpen");
        LoginModal.IsHitTestVisible = false;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        t.Tick += (_, _) => { t.Stop(); LoginModal.IsVisible = false; };
        t.Start();
    }

    // Contact serveur : spinner ambre, pas de bouton, non fermable.
    private void ShowConnectingModal(string title)
    {
        LoginModalIcon.IsVisible = false;
        LoginModalOk.IsVisible = false;
        LoginModalSpinner.IsVisible = true;
        LoginModalSpinner.Stroke = GetBrush("AmberBrush");
        LoginModalTitle.Text = title;
        OpenLoginModal(dismissable: false);
    }

    // Succès : check vert, transition automatique (pas de bouton).
    private void SetLoginModalSuccess(string title)
    {
        SetLoginModalIcon("IconCheck", GetBrush("SuccessBrush"));
        LoginModalOk.IsVisible = false;
        LoginModalTitle.Text = title;
        AudioService.PlayUi(AudioService.Success);
    }

    // Erreur : croix rouge + bouton OK, fermable.
    private void ShowLoginErrorModal(string title)
    {
        SetLoginModalIcon("IconClose", GetBrush("DangerBrush"));
        LoginModalTitle.Text = title;
        LoginModalOk.IsVisible = true;
        OpenLoginModal(dismissable: true);
        AudioService.PlayError();
    }

    private void SetLoginModalIcon(string glyphKey, IBrush color)
    {
        LoginModalSpinner.IsVisible = false;
        LoginModalIcon.IsVisible = true;
        LoginModalIcon.Background = color;
        if (this.TryFindResource(glyphKey, out var g) && g is Geometry geo) LoginModalGlyph.Data = geo;
    }

    private IBrush GetBrush(string key) =>
        this.TryFindResource(key, out var v) && v is IBrush b ? b : Brushes.Gray;

    private void OnLoginModalOk(object? sender, RoutedEventArgs e)
    {
        SetLoginBusy(false);
        CloseLoginModal();
        Dispatcher.UIThread.Post(() => LicenseBox.Focus(), DispatcherPriority.Loaded);
    }

    private void OnLoginModalBackdrop(object? sender, PointerPressedEventArgs e)
    {
        if (!_loginModalDismissable) return;
        SetLoginBusy(false);
        CloseLoginModal();
    }

    // Lien d'aide de l'écran de connexion : « Besoin d'aide ? Rejoindre le Discord ».
    private void OnLoginHelp(object? sender, RoutedEventArgs e) => Links.OpenDiscord();

    // Icône Discord de la barre de titre (les deux skins) : rejoindre le Discord GivreClone.
    private void OnOpenDiscord(object? sender, RoutedEventArgs e) => Links.OpenDiscord();

    private bool CanOpenQuickNavigation() =>
        !SplashOverlay.IsVisible && !LoginOverlay.IsVisible && !LoginModal.IsVisible &&
        !ControlOverlay.IsVisible && !ConfirmOverlay.IsHitTestVisible &&
        DataContext is MainViewModel { MapExtractVisible: false, MappingLoadVisible: false } vm &&
        !vm.Notifications.IsOpen && !vm.Notifications.DetailVisible;

    private void OnOpenQuickNavigation(object? sender, RoutedEventArgs e)
    {
        if (!CanOpenQuickNavigation() || DataContext is not MainViewModel vm) return;
        vm.OpenQuickNavigation();
        Dispatcher.UIThread.Post(() => QuickNavSearch.Focus(), DispatcherPriority.Input);
    }

    private void OnQuickNavigationKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (CanOpenQuickNavigation())
            {
                OnOpenQuickNavigation(sender, e);
                e.Handled = true;
            }
            return;
        }
        // Échap ferme seulement la couche du dessus : détail, puis panneau, puis menu Outils.
        if (e.Key == Key.Escape && DataContext is MainViewModel { QuickNavVisible: false } top)
        {
            if (top.Notifications.DetailVisible) top.Notifications.CloseDetailCommand.Execute(null);
            else if (top.Notifications.IsOpen) top.Notifications.CloseOpenCommand.Execute(null);
            else if (top.AdvancedToolsVisible) top.CloseAdvancedToolsCommand.Execute(null);
            else return;
            e.Handled = true;
            return;
        }
        if (DataContext is not MainViewModel { QuickNavVisible: true } vm) return;
        if (!CanOpenQuickNavigation())
        {
            vm.CloseQuickNavigationCommand.Execute(null);
            return;
        }
        if (e.Key == Key.Escape)
        {
            vm.CloseQuickNavigationCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && QuickNavSearch.IsFocused && vm.QuickNavResults.Count > 0)
        {
            vm.ExecuteQuickNavigation(vm.QuickNavResults[0].Key);
            e.Handled = true;
        }
    }

    private async void OnCopyNotificationDetail(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: NotifRow row } || Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(row.CopyText);
        Toasts.Show("Notification copiée.", success: true, sound: false);
    }

    private void OnQuickNavItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key } && DataContext is MainViewModel { QuickNavVisible: true } vm)
            vm.ExecuteQuickNavigation(key);
    }

    private void OnQuickNavBackdrop(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.CloseQuickNavigationCommand.Execute(null);
    }

    // Blocage "launcher control" : kill switch global (rouge) ou version trop ancienne (ambre) -> écran bloquant, PAS de login.
    // Le message est celui de l'opérateur (contenu serveur, affiché tel quel comme texte — jamais exécuté).
    private void ShowControlBlock(VersionVerdict v)
    {
        if (DataContext is MainViewModel vm) vm.CloseQuickNavigationCommand.Execute(null);
        _offerShown = false;                  // un blocage (kill, MAJ acceptée) remplace toute proposition en cours
        ControlLaterBtn.IsVisible = false;
        UpdatePendingBtn.IsVisible = false;
        ControlUpdateBtn.IsEnabled = true;
        _lastVerdict = v;
        ControlTitle.Text = v.Kill ? Loc.T("L.Control.KillTitle") : Loc.T("L.Control.OutdatedTitle");
        ControlMessage.Text = !string.IsNullOrWhiteSpace(v.Message)
            ? v.Message!
            : (v.Kill ? Loc.T("L.Control.KillDefault") : Loc.T("L.Control.OutdatedDefault"));
        ControlIcon.Background = GetBrush(v.Kill ? "DangerBrush" : "AmberBrush");
        // kill = croix rouge (bloqué) ; version = info ambre. PAS de coche (contradictoire pour un blocage).
        if (this.TryFindResource(v.Kill ? "IconClose" : "IconInfo", out var g) && g is Geometry geo) ControlGlyph.Data = geo;
        // "requise" = le PLANCHER que l'utilisateur doit atteindre (min_version), pas la "current version (info)"
        // du serveur. On retombe sur CurrentVersion seulement si aucun plancher n'est défini.
        var target = v.MinVersion ?? v.CurrentVersion;
        if (v.Outdated && !string.IsNullOrEmpty(target))
        {
            ControlVersion.Text = Loc.Fmt("L.Control.VersionFmt", "v" + LicenseService.AppVersion, "v" + target);
            ControlVersion.IsVisible = true;
        }
        else ControlVersion.IsVisible = false;
        // Bouton "Mettre à jour" : uniquement si périmé (pas un kill) ET le verdict porte un pointeur installable
        // ET le swappeur natif est présent. Sinon on reste sur le blocage simple (fermeture manuelle).
        bool canUpdate = v.Outdated && !v.Kill && UpdateService.UpdateAvailable(v) && UpdateService.UpdaterPresent;
        ControlUpdateBtn.IsVisible = false;
        ControlUpdateStatus.IsVisible = canUpdate;
        ControlUpdateStatus.Text = canUpdate ? Loc.T("L.Control.Updating") : "";
        // Release notes de l'opérateur (payload.notes) : affichées sur l'écran de MAJ (le changelog remplace le
        // message générique "périmé"). C'est LE seul endroit où `notes` apparaît côté client.
        if (canUpdate && !string.IsNullOrWhiteSpace(v.Notes))
            ControlMessage.Text = Loc.Fmt("L.Control.WhatsNew", v.Notes);
        ControlOverlay.IsVisible = true;
        ControlOverlay.IsHitTestVisible = true;
        if (!ControlOverlay.Classes.Contains("modalOpen")) ControlOverlay.Classes.Add("modalOpen");   // rend la carte visible (sinon modalCard = Opacity 0)
        AudioService.PlayError();
        // MAJ OBLIGATOIRE : dès que le blocage "périmé" s'affiche avec un pointeur installable, on lance
        // AUTOMATIQUEMENT la mise à jour. Aucun accès à l'app tant qu'elle n'est pas faite (l'overlay bloque tout).
        if (canUpdate) _ = RunControlUpdateAsync();
    }

    // Bouton "Mettre à jour maintenant" (proposition) ou "Réessayer" (après un échec de la MAJ auto).
    private void OnControlUpdate(object? sender, RoutedEventArgs e)
    {
        if (_offerShown) _ = AcceptUpdateOfferAsync();
        else _ = RunControlUpdateAsync();
    }

    // ---- MAJ PROPOSÉE : nouvelle version exigée alors que l'app est DÉJÀ en cours d'utilisation (connexion faite). Même
    // carte que l'écran de MAJ, plus « Au prochain lancement ». Rien n'est imposé : sans réponse (personne devant le PC), les
    // bots continuent. Au prochain lancement, le contrôle du démarrage rend la MAJ obligatoire comme avant.
    private VersionVerdict? _offer;
    private bool _offerShown;

    private void ShowUpdateOffer(VersionVerdict v)
    {
        if (ControlOverlay.IsVisible && !_offerShown) return;   // déjà bloqué (kill, MAJ en cours) : l'écran de blocage prime
        _offer = v;
        _offerShown = true;
        ControlTitle.Text = Loc.T("L.Control.OfferTitle");
        ControlMessage.Text = !string.IsNullOrWhiteSpace(v.Notes) ? Loc.Fmt("L.Control.WhatsNew", v.Notes) : Loc.T("L.Control.OfferBody");
        ControlIcon.Background = GetBrush("AmberBrush");
        if (this.TryFindResource("IconInfo", out var g) && g is Geometry geo) ControlGlyph.Data = geo;
        var target = v.MinVersion ?? v.CurrentVersion;
        ControlVersion.Text = Loc.Fmt("L.Control.VersionFmt", "v" + LicenseService.AppVersion, "v" + target);
        ControlVersion.IsVisible = !string.IsNullOrEmpty(target);
        ControlUpdateBtn.Content = Loc.T("L.Control.UpdateNow");
        ControlUpdateBtn.IsEnabled = true;
        ControlUpdateBtn.IsVisible = true;
        ControlLaterBtn.IsVisible = true;
        ControlUpdateStatus.Text = Loc.T("L.Control.OfferHint");
        ControlUpdateStatus.IsVisible = true;
        UpdatePendingBtn.IsVisible = false;
        ControlOverlay.IsVisible = true;
        ControlOverlay.IsHitTestVisible = true;
        if (!ControlOverlay.Classes.Contains("modalOpen")) ControlOverlay.Classes.Add("modalOpen");
        AudioService.PlayUi(AudioService.ModalIn);
        Toasts.Show(Loc.Fmt("L.Control.OfferToast", "v" + (target ?? "?")), sound: false);   // historique des notifications
    }

    // « Au prochain lancement » : la carte se ferme, les bots continuent ; rappel cliquable dans la barre de titre.
    private void OnControlLater(object? sender, RoutedEventArgs e)
    {
        CloseUpdateOffer();
        UpdatePendingText.Text = Loc.Fmt("L.Control.PendingFmt", "v" + (_offer?.MinVersion ?? _offer?.CurrentVersion ?? "?"));
        UpdatePendingBtn.IsVisible = true;
        Toasts.Show(Loc.T("L.Control.Deferred"));
    }

    private void CloseUpdateOffer()
    {
        _offerShown = false;
        ControlLaterBtn.IsVisible = false;
        ControlOverlay.Classes.Remove("modalOpen");
        ControlOverlay.IsHitTestVisible = false;
        ControlOverlay.IsVisible = false;
    }

    // Rappel de la barre de titre : rouvre le choix.
    private void OnUpdatePending(object? sender, RoutedEventArgs e)
    {
        if (_offer is { } v) ShowUpdateOffer(v);
    }

    // « Mettre à jour maintenant » : verdict RELU (un push control_kill ne porte pas le lien du paquet), puis même chemin
    // qu'une MAJ imposée : arrêt des bots + écran de MAJ automatique (BlockControl -> ShowControlBlock).
    private async Task AcceptUpdateOfferAsync()
    {
        ControlUpdateBtn.IsEnabled = false;
        ControlLaterBtn.IsVisible = false;
        ControlUpdateStatus.Text = Loc.T("L.Control.Updating");
        var fresh = await LicenseService.CheckVersionAsync();
        if (fresh.Verified && fresh.Ok)
        {
            // Plus rien d'exigé (MAJ retirée entre-temps) : on referme, rien à faire.
            CloseUpdateOffer();
            UpdatePendingBtn.IsVisible = false;
            return;
        }
        if (!fresh.Verified)
        {
            AudioService.PlayError();
            ControlUpdateStatus.Text = Loc.T("L.Control.Unreachable");
            ControlUpdateBtn.IsEnabled = true;
            ControlLaterBtn.IsVisible = true;
            return;
        }
        LicenseService.BlockControl(fresh);   // kill ou version exigée, vérifiés : arrêt des bots puis MAJ automatique
    }

    // MAJ OBLIGATOIRE : télécharge le ZIP (vérif SHA-256 signé) puis passe la main au swappeur natif GivreUpdater.exe
    // et ferme l'app (le swappeur attend notre sortie, remplace les fichiers à froid, relance). En cas d'échec :
    // on RESTE bloqué (aucun accès), avec un bouton "Réessayer". Anti-réentrance via _updating.
    private bool _updating;
    private async Task RunControlUpdateAsync()
    {
        if (_updating) return;
        _updating = true;
        ControlUpdateBtn.IsVisible = false;
        ControlUpdateStatus.IsVisible = true;
        ControlUpdateStatus.Text = Loc.T("L.Control.Updating");
        var progress = new Progress<double>(p => Dispatcher.UIThread.Post(() =>
            ControlUpdateStatus.Text = Loc.Fmt("L.Control.Downloading", (int)(p * 100))));
        var err = await UpdateService.DownloadAndHandOffAsync(_lastVerdict, progress,
            s => Dispatcher.UIThread.Post(() => ControlUpdateStatus.Text = s));
        if (err is null)
        {
            ControlUpdateStatus.Text = Loc.T("L.Control.Restarting");
            await Task.Delay(500);
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        else
        {
            AudioService.PlayError();
            ControlUpdateStatus.Text = err;
            ControlUpdateBtn.Content = Loc.T("L.Control.Retry");
            ControlUpdateBtn.IsVisible = true;
            ControlUpdateBtn.IsEnabled = true;
            _updating = false;   // permet le "Réessayer"
        }
    }

    private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var v = e.Source as Visual;
        while (v is not null)
        {
            if (v is ComboBox) { AudioService.PlayUi(AudioService.Click); return; }
            v = v.GetVisualParent();
        }
    }

    // active/désactive les particules à chaud (depuis les Réglages)
    public void ApplyParticles()
    {
        Particles.Children.Clear();
        if (AppConfig.Current.Particles) SpawnParticles();
    }

    private void SpawnParticles()
    {
        double w = Bounds.Width > 0 ? Bounds.Width : 1320;
        double h = Bounds.Height > 0 ? Bounds.Height : 840;
        for (int i = 0; i < 22; i++)
        {
            double size = _rnd.Next(2, 7);
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(Color.FromArgb(170, 0x4C, 0x8D, 0xFF)),
                RenderTransform = new TranslateTransform(),
                Opacity = 0,
            };
            Canvas.SetLeft(dot, _rnd.NextDouble() * w);
            Canvas.SetTop(dot, _rnd.NextDouble() * h);
            Particles.Children.Add(dot);

            double dur = 8 + _rnd.NextDouble() * 10;
            double delay = _rnd.NextDouble() * dur;
            double dist = 80 + _rnd.NextDouble() * 130;
            double maxOp = 0.18 + _rnd.NextDouble() * 0.30;

            var anim = new Animation
            {
                Duration = TimeSpan.FromSeconds(dur),
                Delay = TimeSpan.FromSeconds(delay),
                IterationCount = IterationCount.Infinite,
                Easing = new SineEaseInOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d),   Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(0.5d), Setters = { new Setter(Visual.OpacityProperty, maxOp) } },
                    new KeyFrame { Cue = new Cue(1d),   Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, -dist) } },
                },
            };
            _ = anim.RunAsync(dot);
        }
    }
}
