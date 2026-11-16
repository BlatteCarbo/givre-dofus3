using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>Une interface reseau + l'etat de son proxy d'isolation d'IP (+ port fixe optionnel).</summary>
public partial class AdapterRow : ObservableObject
{
    public NetAdapter Nic { get; }
    public string Id => Nic.Id;
    public string Name => Nic.Name;
    public string Ip => Nic.Ip.ToString();
    public string Description => Nic.Description;

    [ObservableProperty] public partial bool Running { get; set; }
    [ObservableProperty] public partial string Endpoint { get; set; } = "";
    [ObservableProperty] public partial bool FixedPort { get; set; }
    [ObservableProperty] public partial string PortText { get; set; } = "";

    /// <summary>Force la resynchro du ToggleSwitch (lié OneWay à Running) quand un démarrage échoue sans changer Running —
    /// sinon l'interrupteur reste visuellement ALLUMÉ alors que l'adaptateur n'a pas démarré.</summary>
    public void RaiseRunning() => OnPropertyChanged(nameof(Running));

    private readonly bool _ready;

    public AdapterRow(NetAdapter nic)
    {
        Nic = nic;
        var s = AdapterSettingsStore.Get(nic.Id);
        FixedPort = s.FixedPort;
        PortText = s.Port > 0 ? s.Port.ToString() : "";
        _ready = true;
    }

    partial void OnFixedPortChanged(bool value) => Persist();
    partial void OnPortTextChanged(string value) => Persist();

    private void Persist()
    {
        if (!_ready) return;
        int.TryParse(PortText, out var p);
        AdapterSettingsStore.Set(Id, FixedPort, p);
    }
}

/// <summary>
/// Onglet Adaptateurs : interfaces reseau avec auto-refresh (via NetworkWatcher partage), un proxy HTTP par
/// interface (1 IP par interface). Chaque ligne : copier l'IP, definir un port fixe/auto, allumer/eteindre.
/// L'etat "actif" reflete AUSSI un proxy demarre par le lancement d'un compte (source unique InterfaceProxyManager).
/// </summary>
public partial class AdaptersViewModel : ViewModelBase
{
    public ObservableCollection<AdapterRow> Adapters { get; } = new();

    public bool HasAdapters => Adapters.Count > 0;

    /// <summary>Droit serveur "adapter" actif -> démarrer un proxy d'interface est autorisé (grisage UI + gate action).</summary>
    public bool CanUseAdapters => LicenseService.HasPermission(LicenseService.Perm.Adapter);
    public bool ShowLegacyAdapterLicenseNotice => !MZP19LocalClone.Enabled && !CanUseAdapters;

    public AdaptersViewModel()
    {
        // Surveillance centralisee : un seul minuteur partage previent tous les onglets.
        NetworkWatcher.Changed += Sync;
        // Droits serveur changés -> réévaluer le grisage ; si "adapter" retiré, couper les proxies en cours.
        LicenseService.PermissionsChanged += () => Dispatcher.UIThread.Post(OnPermissionsChanged);
        Sync();
    }

    private void OnPermissionsChanged()
    {
        OnPropertyChanged(nameof(CanUseAdapters));
        OnPropertyChanged(nameof(ShowLegacyAdapterLicenseNotice));
        if (!CanUseAdapters && !LicenseService.IsExiting)   // fermeture de l'app : pas une révocation (le processus s'arrête de toute façon)
            foreach (var row in Adapters.Where(r => r.Running).ToList())
            {
                InterfaceProxyManager.Stop(row.Id);
                row.Running = false; row.Endpoint = "";
            }
    }

    // Ajoute les nouvelles interfaces, retire les disparues, et MET A JOUR l'etat des existantes
    // (Running/Endpoint) -> reflete un proxy allume ici OU par le lancement d'un compte.
    private void Sync()
    {
        var live = NetworkInterfaces.List();
        var liveKeys = live.Select(a => a.Id).ToHashSet();

        foreach (var row in Adapters.ToList())
            if (!liveKeys.Contains(row.Id)) { InterfaceProxyManager.Stop(row.Id); Adapters.Remove(row); }

        foreach (var a in live)
        {
            bool running = InterfaceProxyManager.IsRunning(a.Id);
            // AUDIT M6 : le proxy écoute en loopback (l'egress sort quand même par a.Ip). L'endpoint affiché reflète l'écoute réelle.
            string endpoint = running ? $"http://127.0.0.1:{InterfaceProxyManager.Port(a.Id)}" : "";
            var existing = Adapters.FirstOrDefault(r => r.Id == a.Id);
            if (existing is not null)
            {
                existing.Running = running;
                existing.Endpoint = endpoint;
            }
            else
            {
                Adapters.Add(new AdapterRow(a) { Running = running, Endpoint = endpoint });
            }
        }
        OnPropertyChanged(nameof(HasAdapters));
    }

    [RelayCommand]
    private void Refresh()
    {
        NetworkWatcher.Refresh();   // scan immediat -> Changed -> Sync
        Toasts.Show(Loc.Fmt("L.Adapters.Refreshed", Adapters.Count), success: true, sound: false);
    }

    [RelayCommand]
    private async Task CopyIpAsync(AdapterRow row)
    {
        await ClipboardService.CopyAsync(row.Ip).ConfigureAwait(true);
        Toasts.Show(Loc.Fmt("L.Adapters.IpCopied", row.Ip), sound: false);
    }

    [RelayCommand]
    private void Toggle(AdapterRow row)
    {
        if (row.Running)
        {
            InterfaceProxyManager.Stop(row.Id);
            row.Running = false; row.Endpoint = "";
            AudioService.PlayUi(AudioService.Notify);
            Toasts.Show(Loc.Fmt("L.Adapters.Stopped", row.Name), success: false, sound: false);
            return;
        }

        // Droit serveur "adapter" requis pour DÉMARRER (l'arrêt reste toujours permis). Gate au point d'action.
        if (!LicenseService.HasPermission(LicenseService.Perm.Adapter))
        {
            AudioService.PlayError();
            Toasts.Show(Loc.T("L.Perm.AdapterDenied"), success: false);
            row.RaiseRunning();   // le switch a basculé ON au clic -> le resynchroniser sur Running (false)
            return;
        }

        // Port fixe demande : valider avant de demarrer.
        if (row.FixedPort && !(int.TryParse(row.PortText, out var fp) && fp is > 0 and <= 65535))
        {
            AudioService.PlayError();
            Toasts.Show(Loc.T("L.Adapters.InvalidPort"), success: false);
            row.RaiseRunning();
            return;
        }

        try
        {
            int port = InterfaceProxyManager.Ensure(row.Nic);   // port fixe (AdapterSettingsStore) ou auto
            row.Running = true; row.Endpoint = $"http://{row.Ip}:{port}";
            if (!row.FixedPort) row.PortText = port.ToString();  // montre le port auto attribue
            AudioService.PlayUi(AudioService.Success);
            Toasts.Show(Loc.Fmt("L.Adapters.Started", row.Name, port), success: true, sound: false);
        }
        catch (Exception e)
        {
            AudioService.PlayError();
            Toasts.Show(Loc.Fmt("L.Adapters.Error", e.Message), success: false);
            row.RaiseRunning();
        }
    }
}
