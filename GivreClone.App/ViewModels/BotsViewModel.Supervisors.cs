using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Supervisor;

namespace GivreClone.App.ViewModels;

// Section « Superviseurs » de l'onglet Bots (MZP23) : liste, sélection, ajout / suppression, sondage des bots chaque seconde, distribution
// des évènements aux scripts, démarrage automatique. Le moteur (SupervisorRuntime, SupervisorLuaHost) est testé séparément.
public partial class BotsViewModel
{
    private SupervisorRuntime? _supRuntime;
    private AppBotDirectory? _supDir;
    private DispatcherTimer? _supTimer;

    public ObservableCollection<SupervisorViewModel> Supervisors { get; } = new();
    [ObservableProperty] public partial SupervisorViewModel? SelectedSupervisor { get; set; }
    public bool HasSupervisorSelection => SelectedSupervisor is not null;

    partial void OnSelectedSupervisorChanged(SupervisorViewModel? value)
    {
        foreach (var s in Supervisors) s.Selected = ReferenceEquals(s, value);
        OnPropertyChanged(nameof(HasSupervisorSelection));
    }

    private void InitSupervisors()
    {
        InitLocalNotifications();
        InitPhoneAlerts();   // alertes téléphone (page + Discord)
        Stores.Launch.StartLookTracking();   // apparence du perso (inventaire) + journal de discussion
        InitChat();
        // Lecture seule du fichier hosts : une redirection ACTIVE d'un serveur Ankama vers cette machine fait échouer Dofus (« Le serveur ne répond pas ») et les bots Socket.
        if (HostsGuard.ActiveRedirectsOnThisMachine() is { Count: > 0 } hostsLines)
        {
            GivreLauncher.Core.Transport.TransportLog.Line("[hosts] ⚠ redirection active vers cette machine : " + string.Join(" | ", hostsLines) + " — Dofus et les bots Socket ne se connecteront pas tant qu'elle est là (fichier non modifié par Givre)");
            Toasts.Show("Fichier hosts : un serveur Ankama est redirigé vers ce PC. Dofus et les bots Socket ne pourront pas se connecter (commente ces lignes).", success: false, sound: false);
        }
        SupervisorStore.Load();
        var dir = _supDir = new AppBotDirectory(_accounts);
        dir.Refresh();
        var proxies = new ProxiesPort();
        var accountsPort = new AppAccountsPort(_accounts, dir, proxies) { LoginWaiting = label => RaiseLoginWaiting(label) };
        var svc = new SupervisorServices(dir, new AppBotOrders(_accounts, this, dir), accountsPort, proxies, new AppPermissions(), SupervisorHub.Bus)
        {
            Teams = this,
            // Même garde que le webhook d'un bot : droit « relais messagerie » vérifié au point d'action.
            // The supervisor contract is synchronous: return true only after an actual HTTP 2xx,
            // never merely because the notification was queued.
            Webhook = (url, msg) => LicenseService.HasPermission(LicenseService.Perm.PmRelay)
                && WebhookService.PostAsync(url, msg).GetAwaiter().GetResult(),
        };
        var rt = _supRuntime = new SupervisorRuntime(svc);

        // Bus bot ↔ superviseur : lu depuis le thread du script, donc uniquement des sources sûres entre threads.
        SupervisorHub.Bus.BotHasMainScript = pid => BotScripts.Get(pid)?.Running == true;
        SupervisorHub.SnapshotFor = pid => dir.Snapshot().FirstOrDefault(b => b.Pid == pid);

        rt.HostLog += (name, level, text) => Dispatcher.UIThread.Post(() => SupervisorByName(name)?.AddLog(level, text));
        rt.HostEnded += (name, reason) => Dispatcher.UIThread.Post(() => SupervisorByName(name)?.OnEnded(reason));

        foreach (var def in SupervisorStore.All) Supervisors.Add(new SupervisorViewModel(def, rt, SupervisorStore.Save));

        _supTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _supTimer.Tick += (_, _) => SupervisorTick();
        _supTimer.Start();

        // Évènements non déductibles d'un instantané : popup modérateur, crash du client.
        Stores.Launch.ModeratorPopupReceived += (pid, popup) => Dispatcher.UIThread.Post(() =>
            rt.Raise(((int)pid).ToString(), BotEvent.Of(BotEvents.Moderator, ("author", popup.Author), ("content", popup.Content))));
        _accounts.ClientCrashed += row =>
        {
            if (dir.ByAccountId(row.Model.Id) is { } snap) rt.Raise(snap, BotEvent.Of(BotEvents.Crashed, ("reason", "crash")));
        };
        LicenseService.PermissionsChanged += () => Dispatcher.UIThread.Post(StartAutoSupervisors);
        Dispatcher.UIThread.Post(StartAutoSupervisors);
    }

    private SupervisorViewModel? SupervisorByName(string name) => Supervisors.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void SupervisorTick()
    {
        if (_supDir is null || _supRuntime is null) return;
        _supDir.Refresh();
        DriveLocalNotifications();
        _supRuntime.Tick();
        if (Supervisors.Count == 0) return;
        var snap = _supDir.Snapshot();
        Func<string, string> idOf = alias => _accounts.Accounts.FirstOrDefault(r => _supDir.ByAccountId(r.Model.Id)?.Account == alias)?.Model.Id ?? alias;
        foreach (var s in Supervisors.Where(s => s.Selected || s.Running)) s.UpdateBots(snap, idOf);
    }

    /// <summary>« Démarrer avec GivreClone » : lance les superviseurs marqués, dès que le droit Multi est actif.</summary>
    private void StartAutoSupervisors()
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Multi)) return;
        foreach (var s in Supervisors.Where(s => s.AutoStart && !s.Running).ToList()) s.StartIfIdle();
    }

    /// <summary>Une connexion attend quelqu'un devant la fenêtre Ankama : loginWaiting { reason = "browser" }, bot = { name = libellé, pid = 0 }.</summary>
    private void RaiseLoginWaiting(string label)
        => _supRuntime?.Raise(new BotSnapshot(label, label, 0, "", BotStatus.Connecting), BotEvent.Of(BotEvents.LoginWaiting, ("reason", "browser")));

    [RelayCommand]
    private void AddSupervisor()
    {
        var def = SupervisorStore.Add("Superviseur", SupervisorTemplates.Get(SupervisorTemplates.EmptyId)!.Code);
        if (_supRuntime is null) return;
        var vm = new SupervisorViewModel(def, _supRuntime, SupervisorStore.Save);
        Supervisors.Add(vm);
        SelectedSupervisor = vm;
    }

    [RelayCommand]
    private void RemoveSupervisor(SupervisorViewModel? sup)
    {
        if (sup is null) return;
        Dialogs.Confirm("Supprimer le superviseur", $"Supprimer « {sup.Name} » ? Son script est perdu.", onConfirm: () =>
        {
            _supRuntime?.Stop(sup.Name, "removed");
            Supervisors.Remove(sup);
            SupervisorStore.Remove(sup.Id);
            if (ReferenceEquals(SelectedSupervisor, sup)) SelectedSupervisor = null;
        }, danger: true);
    }

    [RelayCommand] private void SelectSupervisor(SupervisorViewModel? sup) => SelectedSupervisor = sup;

    /// <summary>Bascule MITM ↔ Socket du compte d'une ligne supervisée (refusé s'il est en ligne).</summary>
    [RelayCommand]
    private void ToggleSupervisedMode(SupervisedBotRow? row)
    {
        if (row is null || _accounts.RowByModelId(row.AccountId) is not { } acc) return;
        if (acc.Running) { Toasts.Show("Arrête d'abord le compte pour changer de mode.", success: false); return; }
        acc.IsSocketMode = !acc.IsSocketMode;
    }
}
