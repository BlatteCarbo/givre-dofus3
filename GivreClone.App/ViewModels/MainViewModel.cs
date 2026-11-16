using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public AccountsViewModel AccountsVM { get; }
    public BotsViewModel BotsVM { get; }
    /// <summary>Vue globale locale, fondée sur les sessions et superviseurs déjà observés.</summary>
    public DashboardViewModel DashboardVM { get; }
    public Services.Mcp.LocalControlBridge? LocalControl { get; private set; }
    public TeamsViewModel TeamsVM { get; }
    public ProxysViewModel ProxysVM { get; }
    public AdaptersViewModel AdaptersVM { get; }
    public SettingsViewModel SettingsVM { get; }
    public MyAccountViewModel MyAccountVM { get; }
    /// <summary>Centre de notifications (cloche barre de titre + historique persistant + modal détail).</summary>
    public NotificationCenterViewModel Notifications { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultPage))]
    [NotifyPropertyChangedFor(nameof(SnowPage))]
    public partial ViewModelBase CurrentPage { get; set; }

    // ===== Skin de l'interface =====
    // Bascule à chaud, PUREMENT visuelle : on ne swappe que le CORPS de la fenêtre (Grid.Row=1). Les deux corps
    // partagent le MÊME MainViewModel -> zéro changement de logique. Pour éviter que la page courante ne soit
    // instanciée DEUX fois (un PageHost par corps), chaque corps ne reçoit CurrentPage que quand il est actif :
    // DefaultPage (corps GivreClone) et SnowPage sont l'un ou l'autre null selon la skin.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultPage))]
    [NotifyPropertyChangedFor(nameof(SnowPage))]
    public partial bool IsClassicSkin { get; set; }

    public ViewModelBase? DefaultPage => IsClassicSkin ? null : CurrentPage;
    public ViewModelBase? SnowPage => IsClassicSkin ? CurrentPage : null;

    /// <summary>Persiste le choix de skin. Le thème un bot tiers COMPLET (palette gris/violet + layout) s'applique au
    /// REDÉMARRAGE (la palette de base est en StaticResource figée au chargement) : on ne bascule donc PAS
    /// IsClassicSkin à chaud (ça ne donnerait qu'un demi-skin). Le prompt de redémarrage vit dans SettingsView.</summary>
    public void SetSkin(bool snow)
    {
        var code = snow ? "classic" : "default";
        if (AppConfig.Current.UiSkin == code) return;
        AppConfig.Current.UiSkin = code;
        AppConfig.Save();
    }

    /// <summary>Clic sur un bot dans l'arbre de la skin un bot tiers: sélectionne ce bot (câblage complet via
    /// BotsViewModel.OnSelectedBotChanged) et bascule la vue centrale sur la page Bots.</summary>
    [RelayCommand]
    private void PickBot(BotRowViewModel? bot)
    {
        if (bot is null) return;
        BotsVM.SelectedBot = bot;
        NavigateTo(BotsVM, "bots");
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardActive))]
    [NotifyPropertyChangedFor(nameof(IsAccountsActive))]
    [NotifyPropertyChangedFor(nameof(IsBotsActive))]
    [NotifyPropertyChangedFor(nameof(IsTeamsActive))]
    [NotifyPropertyChangedFor(nameof(IsProxysActive))]
    [NotifyPropertyChangedFor(nameof(IsAdaptersActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive))]
    [NotifyPropertyChangedFor(nameof(IsMyAccountActive))]
    public partial string ActivePage { get; set; } = "dashboard";

    public bool IsDashboardActive => ActivePage == "dashboard";
    public bool IsAccountsActive => ActivePage == "accounts";
    public bool IsBotsActive => ActivePage == "bots";
    public bool IsTeamsActive => ActivePage == "teams";
    public bool IsProxysActive => ActivePage == "proxys";
    public bool IsAdaptersActive => ActivePage == "adapters";
    public bool IsSettingsActive => ActivePage == "settings";
    public bool IsMyAccountActive => ActivePage == "myaccount";

    /// <summary>Version affichee (titre + gate + splash) pour identifier le build : "v1.0.0".</summary>
    public string AppVersionLabel => $"v{LicenseService.AppVersion} · clone local";

    /// <summary>Statut d'abonnement (barre de statut) : « Abonnement jusqu'au JJ/MM/AAAA » (date d'expiration de la licence)
    /// ou « Abonnement illimité » (aucune expiration). Rafraîchi sur PermissionsChanged (login / MAJ temps réel) + langue.</summary>
    public string SubscriptionText =>
        MZP19LocalClone.Enabled ? "Mode local · fonctions Dofus non certifiées" :
        LicenseService.License is { ExpiresAtMs: > 0 } lic
            ? Loc.Fmt("L.Sub.EndsOn", DateTimeOffset.FromUnixTimeMilliseconds(lic.ExpiresAtMs).LocalDateTime.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture))
            : Loc.T("L.Sub.Unlimited");

    // ---- Modal de pre-extraction des cartes (1er lancement / apres un patch Dofus) ----
    [ObservableProperty] public partial bool MapExtractVisible { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MapExtractCounter))]
    public partial int MapExtractDone { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MapExtractCounter))]
    public partial int MapExtractTotal { get; set; }
    [ObservableProperty] public partial bool MapExtractBusy { get; set; }   // spinner pendant l'extraction
    [ObservableProperty] public partial bool MapExtractDoneOk { get; set; } // coche verte a la fin

    public string MapExtractCounter => $"{MapExtractDone} / {MapExtractTotal}";

    private bool _mapExtractBackgrounded;   // l'utilisateur a cliqué « Continuer en arrière-plan » -> notifier à la fin

    // « Continuer en arrière-plan » : on ferme le modal mais l'extraction continue. On pousse un statut dans le centre de
    // notifications (la cloche) et on mémorise pour envoyer un toast + son quand ce sera terminé (cf StartMapPrewarmAsync).
    [RelayCommand] private void CloseMapExtract()
    {
        MapExtractVisible = false;
        if (MapExtractBusy && !_mapExtractBackgrounded)   // seulement si vraiment en cours (pas déjà fini)
        {
            _mapExtractBackgrounded = true;
            Notifier.Push(NotifKind.Info, Loc.T("L.MapExtract.Title"), Loc.T("L.MapExtract.BgStarted"));
        }
    }

    // ---- Modal de chargement du PROTOCOLE (mapping streamé du serveur) ----
    // Referme la course « je me logge et lance un bot trop vite -> mapping manquant » : le stream arrive après le login
    // (async) ; avant tout lancement on s'assure qu'il est bien là, avec un modal de chargement.
    [ObservableProperty] public partial bool MappingLoadVisible { get; set; }

    // Pastille « lien licence » (barre de titre) : reprise de session après une coupure longue / une veille. Le cas normal (reprise
    // en quelques secondes) reste INVISIBLE ; la pastille n'apparaît que si la reprise dure > 3 s, ou si elle bloque (bouton).
    [ObservableProperty] public partial bool LicenseLinkVisible { get; set; }
    [ObservableProperty] public partial bool LicenseLinkBusy { get; set; }
    [ObservableProperty] public partial bool LicenseLinkCanRetry { get; set; }
    [ObservableProperty] public partial string LicenseLinkText { get; set; } = "";
    private Avalonia.Threading.DispatcherTimer? _linkDebounce;
    private LicenseLinkState _linkPrev = LicenseLinkState.Online;

    [RelayCommand]
    private void RetryLicenseLink()
    {
        LicenseLinkCanRetry = false;
        LicenseService.RetryResumeNow();
    }

    private void OnLicenseLinkState(LicenseLinkState s)
    {
        _linkDebounce?.Stop();
        bool entered = s != _linkPrev;   // Stalled est ré-émis après chaque échec : le toast, lui, ne part qu'à l'entrée
        _linkPrev = s;
        switch (s)
        {
            case LicenseLinkState.Online:
                LicenseLinkVisible = false; LicenseLinkBusy = false; LicenseLinkCanRetry = false;
                return;
            case LicenseLinkState.Resuming:
            case LicenseLinkState.WaitingNetwork:
                LicenseLinkText = Loc.T(s == LicenseLinkState.WaitingNetwork ? "L.LicLink.WaitingNetwork" : "L.LicLink.Resuming");
                LicenseLinkBusy = true; LicenseLinkCanRetry = false;
                if (!LicenseLinkVisible)
                {
                    // 6 s : une reprise normale (0-5 s d'aléa anti-troupeau + /auth) reste invisible.
                    _linkDebounce ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                    _linkDebounce.Tick -= OnLicenseLinkDebounce;
                    _linkDebounce.Tick += OnLicenseLinkDebounce;
                    _linkDebounce.Start();
                }
                return;
            case LicenseLinkState.Stalled:
                LicenseLinkText = Loc.T("L.LicLink.Stalled"); LicenseLinkBusy = true; LicenseLinkCanRetry = true;
                break;
            case LicenseLinkState.NeedsUser:
                LicenseLinkText = Loc.T("L.LicLink.NeedsUser"); LicenseLinkBusy = false; LicenseLinkCanRetry = true;
                break;
            case LicenseLinkState.Offline:   // coupure prolongée : réessai automatique, rien à cliquer
                LicenseLinkText = Loc.T("L.LicLink.Offline"); LicenseLinkBusy = true; LicenseLinkCanRetry = false;
                break;
        }
        LicenseLinkVisible = true;
        if (entered) Toasts.Show(LicenseLinkText, success: false);   // une seule fois, à l'entrée dans l'état bloqué (-> centre de notifications)
    }

    private void OnLicenseLinkDebounce(object? sender, EventArgs e)
    {
        _linkDebounce?.Stop();
        if (LicenseService.LinkState is LicenseLinkState.Resuming or LicenseLinkState.WaitingNetwork) LicenseLinkVisible = true;
    }
    [ObservableProperty] public partial bool MappingLoadBusy { get; set; }

    /// <summary>Instance courante (une seule fenêtre) : permet aux VMs enfants (AccountsViewModel) de piloter les modals
    /// niveau fenêtre hébergés ici (chargement du protocole).</summary>
    public static MainViewModel? Instance { get; private set; }

    /// <summary>Garantit que le mapping streamé est PRÊT avant de lancer un bot. Affiche un modal de chargement, (re)lance
    /// le stream et attend (retries bornés). true = prêt (ou repli data/ local en DEV). On est SÛR que le protocole est là
    /// avant d'agir -> plus de « mapping manquant » sur un lancement juste après le login.</summary>
    public async Task<bool> EnsureMappingReadyAsync()
    {
        if (MappingCatalog.IsReady || MappingCatalog.MissingEssentials().Count == 0) return true;   // déjà prêt (ou repli local dev)
        // Requête signée refusée (client à mettre à jour / horloge) : inutile de re-demander, on dit pourquoi.
        if (LicenseService.SigningBlocked) { Toasts.Show(LicenseService.SigningBlockedMessage, success: false); return false; }
        MappingLoadBusy = true;
        MappingLoadVisible = true;
        try
        {
            for (int attempt = 0; attempt < 5 && !MappingCatalog.IsReady; attempt++)
            {
                var fetch = MappingCatalog.EnsureFetchedAsync();
                await Task.WhenAny(fetch, Task.Delay(8000));
                if (MappingCatalog.IsReady) break;
                await Task.Delay(600);
            }
            return MappingCatalog.IsReady || MappingCatalog.MissingEssentials().Count == 0;
        }
        finally { MappingLoadVisible = false; MappingLoadBusy = false; }
    }

    private bool _mapPrewarmStarted;

    /// <summary>Lance la pre-extraction des cartes si necessaire (1er lancement / apres patch). Ne montre le modal que
    /// s'il y a reellement du travail ; sinon (cache deja a jour, ou client Dofus absent) c'est silencieux.</summary>
    public async Task StartMapPrewarmAsync()
    {
        if (_mapPrewarmStarted) return;
        _mapPrewarmStarted = true;

        var launch = Stores.Launch;
        if (!launch.MapDataAvailable) return;   // client Dofus non detecte -> rien a extraire

        int pending;
        try { pending = await Task.Run(launch.MapPrewarmPending); }   // scan de l'index (~3 s) hors thread UI
        catch { return; }
        if (pending <= 0) return;               // cache deja a jour -> pas de modal

        MapExtractTotal = pending;
        MapExtractDone = 0;
        MapExtractBusy = true;
        MapExtractDoneOk = false;
        MapExtractVisible = true;
        _mapExtractBackgrounded = false;

        // Progress cree sur le thread UI -> les callbacks reviennent sur l'UI (maj directe des proprietes liees).
        var progress = new Progress<(int done, int total)>(p =>
        {
            MapExtractDone = p.done;
            if (p.total > 0) MapExtractTotal = p.total;
        });
        try { await launch.PrewarmAllMapsAsync(progress); }
        catch { /* best-effort */ }

        MapExtractDone = MapExtractTotal;
        MapExtractBusy = false;
        MapExtractDoneOk = true;

        if (_mapExtractBackgrounded)
        {
            // Modal fermé (« en arrière-plan ») : l'utilisateur ne voit pas la coche -> on le prévient (toast + son +
            // centre de notifications via Toasts.Requested). Le toast n'apparaît QUE dans ce cas (sinon la coche suffit).
            _mapExtractBackgrounded = false;
            AudioService.PlayUi(AudioService.Notify);
            Toasts.Show(Loc.Fmt("L.MapExtract.Done", MapExtractTotal), success: true, sound: false);
        }
        else
        {
            await Task.Delay(1400);             // modal encore visible : laisse voir la coche verte
        }
        MapExtractVisible = false;
    }

    public MainViewModel()
    {
        Instance = this;
        // Bundle de mapping reçu incomplet pour CE build (champs lus/forgés absents) : avertissement NON bloquant, une fois par
        // réception. Le détail (noms des champs) est dans la console. Le 04/10, ce cas cassait le combat sans aucun signal.
        MappingCatalog.Ready += () =>
        {
            int n = MappingCatalog.MissingRequiredFields.Count;
            if (n > 0) Avalonia.Threading.Dispatcher.UIThread.Post(() => Toasts.Show(Loc.Fmt("L.Mapping.MissingFieldsFmt", n), success: false));
        };
        // Detection automatisee des interfaces reseau + notification a chaque nouvelle interface.
        NetworkWatcher.Ensure();
        NetworkWatcher.AdapterAppeared += OnAdapterAppeared;

        AccountsVM = new AccountsViewModel();
        BotsVM = new BotsViewModel(AccountsVM);   // consomme le flux partage + la liste des comptes en jeu
        DashboardVM = new DashboardViewModel(BotsVM, Notifications, PickBot,
            sup => PickSupervisor(sup), () =>
            {
                if (!Notifications.IsOpen) Notifications.ToggleOpenCommand.Execute(null);
            });
        if (AppConfig.Current.McpEnabled && Environment.GetEnvironmentVariable("GIVRE_DISABLE_MCP") != "1")
        {
            LocalControl = new Services.Mcp.LocalControlBridge(BotsVM);
            LocalControl.Start();
        }
        StartRemoteIfEnabled();   // contrôle à distance (téléphone), OFF par défaut
        TeamsVM = new TeamsViewModel(BotsVM);     // page Équipe : même orchestrateur, autre vue (grouper les comptes)
        ProxysVM = new ProxysViewModel();
        AdaptersVM = new AdaptersViewModel();
        SettingsVM = new SettingsViewModel();
        MyAccountVM = new MyAccountViewModel();
        IsClassicSkin = SkinService.FeatureEnabled && AppConfig.Current.UiSkin == "classic";   // choisi AVANT CurrentPage -> Default/SnowPage corrects d'entrée ; forcé false si skin en pause
        CurrentPage = DashboardVM;
        DashboardVM.Activate();
        _ = RefreshProtocolAuditAsync(); // empreintes du client installé : lecture locale, sans indexation réseau
        // Statut d'abonnement: refléter l'expiration dès le login et à chaque MAJ de droits.
        LicenseService.PermissionsChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SubscriptionText)));
        Loc.Changed += () => OnPropertyChanged(nameof(SubscriptionText));
        AppConfig.UiPreferencesChanged += RaiseUiPreferences;
        LicenseService.PermissionsChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(RaiseUiPreferences);
        // Reprise silencieuse de la session de licence (coupure longue / veille) -> pastille de la barre de titre.
        LicenseService.LinkStateChanged += st => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnLicenseLinkState(st));
        DiscordRichPresence.ApplyFromConfig();   // « Joue à GivreClone » sur Discord (si activé + Application ID renseigné)
        // Notification poussée par l'admin/portail (commande SHOW_NOTIFICATION) : toast + centre de notifications + son.
        LicenseService.ShowNotificationRequested += msg => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            AudioService.PlayUi(AudioService.Notify);
            Toasts.Show(msg, success: true, sound: false);
        });
    }

    // ===== Mode d'affichage (standard / expert / dev) + mode discret : persistés dans AppConfig =====
    public string UiMode => AppConfig.Current.UiMode;
    public string EffectiveUiMode => AppConfig.EffectiveUiMode;
    public bool IsStandardUi => EffectiveUiMode == "standard";
    public bool IsExpertUi => EffectiveUiMode is "expert" or "dev";
    public bool IsDeveloperUi => EffectiveUiMode == "dev";
    public bool IsDeveloperLicense => LicenseService.IsDeveloper;
    public bool StreamerPrivacy => AppConfig.Current.StreamerPrivacy;

    [RelayCommand]
    private void SetUiMode(string mode)
    {
        if (mode is not ("standard" or "expert" or "dev")) return;
        if (mode == "dev" && !LicenseService.IsDeveloper) return;
        if (AppConfig.Current.UiMode == mode) return;
        AppConfig.Current.UiMode = mode;
        AppConfig.Save();
        AppConfig.NotifyUiPreferencesChanged();
    }

    private void RaiseUiPreferences()
    {
        OnPropertyChanged(nameof(UiMode));
        OnPropertyChanged(nameof(EffectiveUiMode));
        OnPropertyChanged(nameof(IsStandardUi));
        OnPropertyChanged(nameof(IsExpertUi));
        OnPropertyChanged(nameof(IsDeveloperUi));
        OnPropertyChanged(nameof(IsDeveloperLicense));
        OnPropertyChanged(nameof(StreamerPrivacy));
    }

    private static void OnAdapterAppeared(NetAdapter nic)
    {
        AudioService.PlayUi(AudioService.Notify);
        Toasts.Show(Loc.Fmt("L.Adapters.Appeared", nic.Name), success: true, sound: false);
    }

    /// <summary>Navigation visuelle uniquement. Le rafraîchissement du Dashboard ne tourne que s'il est affiché.</summary>
    private void NavigateTo(ViewModelBase page, string key)
    {
        MarketInsightsVisible = false;
        EcoToolsVisible = false;
        if (!ReferenceEquals(page, DashboardVM)) DashboardVM.Deactivate();
        RecordNavigation(key);
        CurrentPage = page;
        ActivePage = key;
        if (ReferenceEquals(page, DashboardVM)) DashboardVM.Activate();
    }

    [RelayCommand] private void ShowDashboard() => NavigateTo(DashboardVM, "dashboard");
    [RelayCommand] private void ShowAccounts() => NavigateTo(AccountsVM, "accounts");
    [RelayCommand] private void ShowBots() => NavigateTo(BotsVM, "bots");

    /// <summary>Le thème « Givre » est actif (chargé au démarrage).</summary>
    public bool IsGivreSkin => Services.SkinService.IsSnow;

    /// <summary>Ouvre l'onglet Bots sur le superviseur choisi (liste latérale).</summary>
    [RelayCommand]
    private void PickSupervisor(SupervisorViewModel? sup)
    {
        if (sup is null) return;
        BotsVM.SelectSupervisorCommand.Execute(sup);
        ShowBots();
    }
    [RelayCommand] private void ShowTeams() => NavigateTo(TeamsVM, "teams");
    [RelayCommand] private void ShowProxys() => NavigateTo(ProxysVM, "proxys");
    [RelayCommand] private void ShowAdapters() => NavigateTo(AdaptersVM, "adapters");
    [RelayCommand] private void ShowSettings() => NavigateTo(SettingsVM, "settings");
    [RelayCommand] private void ShowMyAccount() => NavigateTo(MyAccountVM, "myaccount");
}
