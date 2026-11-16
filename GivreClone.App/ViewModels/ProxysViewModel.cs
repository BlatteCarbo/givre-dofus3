using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Relay;
using GivreClone.Engine.Proxies;

namespace GivreClone.App.ViewModels;

/// <summary>Un proxy de la liste : nom + detail (SOCKS5/HTTP host:port, "depuis adaptateur" ou partage GivreRelay) + IP de sortie.</summary>
public partial class ProxyItem : ObservableObject
{
    public ProxyEntry Model { get; }
    public string Name => Model.Name;
    public bool IsRelay => Model.FromRelay;
    public string Detail => IsRelay ? RelayDetail()
        : Model.FromAdapter ? Loc.T("L.Proxys.FromAdapterTag")
        : $"{(Model.Protocol == ProxyProtocol.Socks5 ? "SOCKS5" : "HTTP")}  {Model.Host}:{Model.Port}";

    /// <summary>Partage absent de la liste du relais (connecté) : retiré du portail -> seul cas où on peut le supprimer ici.</summary>
    public bool RelayGone => IsRelay && GivreRelay.Client.State == RelayLinkState.Connected && GivreRelay.Client.GetExit(Model.RelayExitId!) is null;
    public bool CanEdit => !IsRelay;
    public bool CanRemove => !IsRelay || RelayGone;

    [ObservableProperty] public partial string ExitIp { get; set; } = "";
    [ObservableProperty] public partial bool Testing { get; set; }
    [ObservableProperty] public partial bool CanTest { get; set; }

    public ProxyItem(ProxyEntry model, bool canUseProxys)
    {
        Model = model;
        CanTest = IsRelay || canUseProxys;
        if (IsRelay) RefreshRelay();
    }

    // « Relais Givre · FR · en ligne · Dofus uniquement · 1.2 / 10 Go »
    private string RelayDetail()
    {
        string tag = Loc.T("L.Relay.Tag");
        if (GivreRelay.Client.State != RelayLinkState.Connected) return $"{tag}  ·  {Loc.T("L.Relay.Waiting")}";
        if (GivreRelay.Client.GetExit(Model.RelayExitId!) is not { } e) return $"{tag}  ·  {Loc.T("L.Relay.Gone")}";
        var parts = new System.Collections.Generic.List<string> { tag };
        if (!string.IsNullOrEmpty(e.Country)) parts.Add(e.Country);
        parts.Add(Loc.T(e.Online ? "L.Relay.Online" : "L.Relay.OfflineTag"));
        parts.Add(Loc.T(e.AnkamaOnly ? "L.Relay.ScopeAnkama" : "L.Relay.ScopeAll"));
        parts.Add(GivreRelay.Usage(e) + (e.QuotaReset is { Length: > 0 } r ? $" ({Loc.Fmt("L.Relay.ResetOn", r)})" : ""));
        return string.Join("  ·  ", parts);
    }

    /// <summary>État du partage changé (push du relais) : nom, détail, IP publique de l'ami.</summary>
    public void RefreshRelay()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(CanRemove));
        if (!Testing) ExitIp = GivreRelay.Client.GetExit(Model.RelayExitId!)?.PublicIp ?? "";
    }
}

/// <summary>
/// Onglet Proxys : gere la liste des proxys SOCKS5/HTTP assignables aux comptes. Ajout EXTERNE (host:port:user:pass,
/// ex partage 4G du telephone) OU depuis un ADAPTATEUR (reutilise l'onglet Adapters). Bouton Tester -&gt; IP publique
/// de sortie reelle (via ProxyTester). Persiste dans le ProxyStore partage.
/// </summary>
public partial class ProxysViewModel : ViewModelBase
{
    public ObservableCollection<ProxyItem> Proxies { get; } = new();
    public ObservableCollection<NetAdapter> AdapterChoices { get; } = new();

    [ObservableProperty] public partial string NewName { get; set; } = "";
    [ObservableProperty] public partial string Protocol { get; set; } = "SOCKS5";
    [ObservableProperty] public partial string NewHost { get; set; } = "";
    [ObservableProperty] public partial string NewPort { get; set; } = "";
    [ObservableProperty] public partial string NewUser { get; set; } = "";
    [ObservableProperty] public partial string NewPass { get; set; } = "";
    [ObservableProperty] public partial NetAdapter? SelectedAdapter { get; set; }

    // Edition d'un proxy enregistre (modal)
    [ObservableProperty] public partial bool EditVisible { get; set; }
    [ObservableProperty] public partial bool EditIsAdapter { get; set; }
    [ObservableProperty] public partial string EditName { get; set; } = "";
    [ObservableProperty] public partial string EditProtocol { get; set; } = "SOCKS5";
    [ObservableProperty] public partial string EditHost { get; set; } = "";
    [ObservableProperty] public partial string EditPort { get; set; } = "";
    [ObservableProperty] public partial string EditUser { get; set; } = "";
    [ObservableProperty] public partial string EditPass { get; set; } = "";
    private string? _editId;

    public string[] Protocols { get; } = { "SOCKS5", "HTTP" };
    public bool HasProxies => Proxies.Count > 0;

    /// <summary>Droit serveur "proxy" actif -> l'onglet Proxys est utilisable (tester + router un compte).
    /// Sinon : bandeau d'info + bouton Tester grisé + refus au lancement (voir ProxyResolver / AccountsViewModel).</summary>
    public bool CanUseProxys => LicenseService.HasPermission(LicenseService.Perm.Proxy);
    public bool ShowLegacyProxyLicenseNotice => !MZP19LocalClone.Enabled && !CanUseProxys;

    // GivreRelay : état du lien + nombre de partages (en-tête de la section Relais).
    public string RelayStateText => Loc.T(GivreRelay.Client.State switch
    {
        RelayLinkState.Connecting => "L.Relay.State.Connecting",
        RelayLinkState.Connected => "L.Relay.State.Connected",
        RelayLinkState.Reconnecting => "L.Relay.State.Reconnecting",
        RelayLinkState.Refused => "L.Relay.State.Refused",
        _ => "L.Relay.State.Off",
    }) + (GivreRelay.Client.State == RelayLinkState.Connected
            ? "  ·  " + Loc.Fmt("L.Relay.Count", GivreRelay.Client.Exits.Count)
            : GivreRelay.Client.LastError is { Length: > 0 } err ? "  ·  " + err : "");
    public bool RelayConnected => GivreRelay.Client.State == RelayLinkState.Connected;
    public bool RelayEmpty => RelayConnected && GivreRelay.Client.Exits.Count == 0;

    public ProxysViewModel()
    {
        // Auto-refresh centralise : une interface branchee apres le demarrage (ex : partage 4G d'un
        // telephone) apparait aussi dans ce menu, pas seulement dans l'onglet Adaptateurs.
        NetworkWatcher.Changed += RefreshAdapters;
        // Droits serveur changés -> réévaluer le grisage (bandeau + bouton Tester) en direct.
        LicenseService.PermissionsChanged += () => Dispatcher.UIThread.Post(OnPermissionsChanged);
        // Partages GivreRelay : liste, état et consommation poussés par le relais -> mis à jour en place.
        GivreRelay.Changed += RefreshRelay;   // déjà sur le thread UI, après la synchro de la liste des proxys
        GivreRelay.Client.StateChanged += () => Dispatcher.UIThread.Post(RefreshRelay);
        RefreshAdapters();
        Reload();
    }

    private void OnPermissionsChanged()
    {
        OnPropertyChanged(nameof(CanUseProxys));
        OnPropertyChanged(nameof(ShowLegacyProxyLicenseNotice));
        foreach (var it in Proxies) it.CanTest = it.IsRelay || CanUseProxys;
    }

    private void Reload()
    {
        Proxies.Clear();
        foreach (var p in Stores.Proxies.Proxies) Proxies.Add(new ProxyItem(p, CanUseProxys));
        OnPropertyChanged(nameof(HasProxies));
    }

    // Sans vider la liste (les résultats de test des autres proxys restent affichés) : nouveaux partages ajoutés, détail rafraîchi.
    private void RefreshRelay()
    {
        var stored = Stores.Proxies.Proxies.Select(p => p.Id).ToHashSet();
        foreach (var it in Proxies.Where(it => it.IsRelay && !stored.Contains(it.Model.Id)).ToList()) Proxies.Remove(it);   // agent supprimé
        var shown = Proxies.Select(p => p.Model.Id).ToHashSet();
        foreach (var p in Stores.Proxies.Proxies)
            if (!shown.Contains(p.Id)) Proxies.Add(new ProxyItem(p, CanUseProxys));
        foreach (var it in Proxies) if (it.IsRelay) it.RefreshRelay();
        OnPropertyChanged(nameof(HasProxies));
        OnPropertyChanged(nameof(RelayStateText));
        OnPropertyChanged(nameof(RelayConnected));
        OnPropertyChanged(nameof(RelayEmpty));
    }

    // Sync NON destructif : ajoute les nouvelles interfaces, retire les disparues, SANS vider la liste
    // (sinon la selection du ComboBox se reset). Preserve les instances -> SelectedAdapter reste valide.
    private void RefreshAdapters()
    {
        var live = NetworkInterfaces.List();
        var liveKeys = live.Select(a => a.Id).ToHashSet();
        foreach (var cur in AdapterChoices.ToList())
            if (!liveKeys.Contains(cur.Id)) AdapterChoices.Remove(cur);
        var shown = AdapterChoices.Select(a => a.Id).ToHashSet();
        foreach (var a in live)
            if (!shown.Contains(a.Id)) AdapterChoices.Add(a);
    }

    [RelayCommand]
    private void AddExternal()
    {
        if (string.IsNullOrWhiteSpace(NewHost) || !int.TryParse(NewPort, out var port) || port is <= 0 or > 65535)
        {
            Toasts.Show(Loc.T("L.Proxys.InvalidHost"), success: false);
            return;
        }
        var name = string.IsNullOrWhiteSpace(NewName) ? $"{NewHost.Trim()}:{port}" : NewName.Trim();
        var proto = Protocol == "HTTP" ? ProxyProtocol.Http : ProxyProtocol.Socks5;
        Stores.Proxies.AddExternal(name, proto, NewHost.Trim(), port,
            string.IsNullOrWhiteSpace(NewUser) ? null : NewUser.Trim(), NewPass);
        NewName = NewHost = NewPort = NewUser = NewPass = "";
        Reload();
        Toasts.Show(Loc.Fmt("L.Proxys.Added", name));
    }

    [RelayCommand]
    private void AddFromAdapter()
    {
        // Lire la selection AVANT tout refresh (sinon on la perd).
        if (SelectedAdapter is not { } nic) { Toasts.Show(Loc.T("L.Proxys.PickAdapter"), success: false); return; }
        var p = Stores.Proxies.AddFromAdapter(nic.Name, nic.Id);
        SelectedAdapter = null;
        Reload();
        Toasts.Show(Loc.Fmt("L.Proxys.Added", p.Name));
    }

    [RelayCommand]
    private void BeginEdit(ProxyItem item)
    {
        if (!item.CanEdit) return;   // partage GivreRelay : nom et réglages viennent du portail
        var e = item.Model;
        _editId = e.Id;
        EditIsAdapter = e.FromAdapter;
        EditName = e.Name;
        EditProtocol = e.Protocol == ProxyProtocol.Socks5 ? "SOCKS5" : "HTTP";
        EditHost = e.Host ?? "";
        EditPort = e.Port > 0 ? e.Port.ToString() : "";
        EditUser = e.Username ?? "";
        EditPass = "";   // jamais reafficher le mot de passe stocke ; vide = inchange
        EditVisible = true;
    }

    [RelayCommand]
    private void CancelEdit() { EditVisible = false; _editId = null; }

    [RelayCommand]
    private void SaveEdit()
    {
        if (_editId is null) { EditVisible = false; return; }
        var e = Stores.Proxies.Get(_editId);
        if (e is null) { EditVisible = false; _editId = null; return; }
        if (string.IsNullOrWhiteSpace(EditName)) { Toasts.Show(Loc.T("L.Proxys.NameRequired"), success: false); return; }

        e.Name = EditName.Trim();
        if (e.FromAdapter)
        {
            Stores.Proxies.Update(e);   // proxy d'adaptateur : seul le nom est modifiable
        }
        else
        {
            if (string.IsNullOrWhiteSpace(EditHost) || !int.TryParse(EditPort, out var port) || port is <= 0 or > 65535)
            { Toasts.Show(Loc.T("L.Proxys.InvalidHost"), success: false); return; }
            e.Protocol = EditProtocol == "HTTP" ? ProxyProtocol.Http : ProxyProtocol.Socks5;
            e.Host = EditHost.Trim();
            e.Port = port;
            e.Username = string.IsNullOrWhiteSpace(EditUser) ? null : EditUser.Trim();
            // mot de passe vide -> inchange ; sinon remplace.
            Stores.Proxies.Update(e, newPassword: string.IsNullOrEmpty(EditPass) ? null : EditPass);
        }

        EditVisible = false; _editId = null;
        Reload();
        Toasts.Show(Loc.Fmt("L.Proxys.Updated", e.Name));
    }

    [RelayCommand]
    private void Remove(ProxyItem item)
    {
        if (!item.CanRemove) return;   // partage encore listé par le relais : il reviendrait au prochain push
        Dialogs.Confirm(Loc.T("L.Proxys.RemoveTitle"), Loc.Fmt("L.Proxys.RemoveBody", item.Name),
            onConfirm: () => { Stores.Proxies.Remove(item.Model.Id); Reload(); Toasts.Show(Loc.Fmt("L.Proxys.Removed", item.Name)); },
            danger: true);
    }

    [RelayCommand]
    private async Task TestAsync(ProxyItem item)
    {
        if (item.Testing) return;   // déjà en cours -> pas de test concurrent (anti-spam du bouton)
        // Droit serveur "proxy" requis pour activer/sonder un proxy (le bouton est aussi grisé côté vue). Partage GivreRelay :
        // droit vérifié par le serveur du relais.
        if (!item.IsRelay && !CanUseProxys) { AudioService.PlayError(); Toasts.Show(Loc.T("L.Perm.ProxyDenied"), success: false); return; }
        item.Testing = true;
        item.ExitIp = Loc.T("L.Proxys.Testing");
        var ps = ProxyResolver.Resolve(item.Model, Stores.Proxies);
        if (ps is null)
        {
            item.ExitIp = item.IsRelay ? GivreRelay.Unavailable(item.Model.RelayExitId!) ?? Loc.T("L.Proxys.Unresolved") : Loc.T("L.Proxys.Unresolved");
            item.Testing = false;
            return;
        }
        var (ok, ip, ms) = await ProxyTester.TestAsync(ps).ConfigureAwait(true);
        item.ExitIp = ok ? Loc.Fmt("L.Proxys.ExitIp", ip, ms) : Loc.Fmt("L.Proxys.TestFailed", ip);
        item.Testing = false;
    }
}
