using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GivreClone.App.Services;
using GivreClone.App.Services.License;

namespace GivreClone.App.ViewModels;

/// <summary>Une ligne de droit serveur (permission) affichée dans l'onglet "Mon compte".</summary>
public partial class PermissionRowVM : ObservableObject
{
    public string Code { get; }
    public string DisplayName { get; }

    /// <summary>Droit accordé par la licence (indépendamment de son expiration propre).</summary>
    [ObservableProperty] public partial bool Granted { get; set; }
    /// <summary>Droit ACTIF maintenant (accordé + non expiré + licence valide) = ce que <c>HasPermission</c> renvoie.</summary>
    [ObservableProperty] public partial bool Active { get; set; }
    /// <summary>Libellé d'expiration du droit ("suit la licence", une date, "expiré le…", ou "non inclus").</summary>
    [ObservableProperty] public partial string ExpiryLabel { get; set; } = "";

    public PermissionRowVM(string code, string displayName) { Code = code; DisplayName = displayName; }
}

/// <summary>Onglet "Mon compte" : récapitulatif de la licence connectée (package, statut, expiration, activation,
/// dernière connexion, HWID) et de ses DROITS serveur (chacun avec sa propre expiration). Rafraîchi en direct via
/// <see cref="LicenseService.PermissionsChanged"/> (login/logout/kill/expiration) et au changement de langue.</summary>
public partial class MyAccountViewModel : ViewModelBase
{
    public ObservableCollection<PermissionRowVM> Permissions { get; } = new();

    [ObservableProperty] public partial bool Connected { get; set; }
    [ObservableProperty] public partial string PackageName { get; set; } = "";
    [ObservableProperty] public partial string StatusLabel { get; set; } = "";
    [ObservableProperty] public partial string ExpirationLabel { get; set; } = "";
    [ObservableProperty] public partial string ActivatedLabel { get; set; } = "";
    [ObservableProperty] public partial string LastAuthLabel { get; set; } = "";
    [ObservableProperty] public partial string RemainingLabel { get; set; } = "";

    /// <summary>HWID de la machine (identique à celui lié à la licence côté serveur).</summary>
    public string HardwareId => LicenseService.HardwareId;
    /// <summary>Empreinte de la clé client (même calcul que l'admin) : le support la compare quand des bots n'apparaissent pas.</summary>
    public string ClientKeyFingerprint => LicenseService.ClientKeyFingerprint();

    public MyAccountViewModel()
    {
        // PermissionsChanged peut venir d'un thread de fond (heartbeat/WS) -> marshaller vers l'UI.
        LicenseService.PermissionsChanged += () => Dispatcher.UIThread.Post(Refresh);
        // Notification (toast) quand un droit est ajouté / retiré EN DIRECT (édition côté panel).
        LicenseService.PermissionsDelta += OnPermissionsDelta;
        Loc.Changed += Refresh;   // re-localise les libellés au changement de langue
        Refresh();
    }

    private void OnPermissionsDelta(IReadOnlyList<string> added, IReadOnlyList<string> removed) =>
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var code in added)   Toasts.Show(Loc.Fmt("L.Perm.Added",   PermName(code)), success: true,  sound: true);
            foreach (var code in removed) Toasts.Show(Loc.Fmt("L.Perm.Removed", PermName(code)), success: false, sound: true);
        });

    private static string FmtDate(long ms) =>
        ms <= 0 ? Loc.T("L.Account.Lifetime")
                : DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);

    public void Refresh()
    {
        var lic = LicenseService.License;
        Connected = lic is not null;
        OnPropertyChanged(nameof(HardwareId));
        OnPropertyChanged(nameof(ClientKeyFingerprint));

        if (lic is null)
        {
            PackageName = StatusLabel = ExpirationLabel = ActivatedLabel = LastAuthLabel = RemainingLabel = "";
            Permissions.Clear();
            return;
        }

        PackageName = string.IsNullOrWhiteSpace(lic.PackageName) ? "—" : lic.PackageName;
        StatusLabel = lic.Status;
        ExpirationLabel = FmtDate(lic.ExpiresAtMs);
        ActivatedLabel = lic.ActivatedAtMs is long a ? FmtDate(a) : "—";
        LastAuthLabel = lic.LastAuthAtMs is long la ? FmtDate(la) : Loc.T("L.Account.FirstAuth");

        var rem = LicenseService.RemainingTime;
        RemainingLabel = rem is null ? Loc.T("L.Account.Lifetime")
                       : rem.Value.TotalSeconds <= 0 ? Loc.T("L.Account.Expired")
                       : Loc.Fmt("L.Account.RemainingFmt", (int)rem.Value.TotalDays, rem.Value.Hours);

        RebuildPermissions(lic);
    }

    private void RebuildPermissions(LicenseInfo lic)
    {
        Permissions.Clear();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var code in LicenseService.Perm.All)
        {
            var p = lic.Permissions.FirstOrDefault(x => string.Equals(x.Name, code, StringComparison.OrdinalIgnoreCase));
            var row = new PermissionRowVM(code, PermName(code))
            {
                Granted = p is not null,
                Active = LicenseService.HasPermission(code),
                ExpiryLabel = p is null
                    ? Loc.T("L.Account.PermNone")
                    : p.ExpiresAtMs is long e && e > 0
                        ? (e <= now ? Loc.Fmt("L.Account.PermExpiredFmt", FmtDate(e)) : FmtDate(e))
                        : Loc.T("L.Account.PermFollowsLicense"),
            };
            Permissions.Add(row);
        }
    }

    // Libellé LOCALISÉ d'un droit : le serveur n'envoie que la clé brute ("socket", "pm-relay"…), le nom affiché vient des
    // fichiers de langue (liste « Mon compte » + toasts « Permission … activée / retirée »).
    private static string PermName(string code) => code switch
    {
        "gather"   => Loc.T("L.Perm.gather"),
        "fight"    => Loc.T("L.Perm.fight"),
        "adapter"  => Loc.T("L.Perm.adapter"),
        "multi"    => Loc.T("L.Perm.multi"),
        "tactical" => Loc.T("L.Perm.tactical"),
        "no-anim"  => Loc.T("L.Perm.noanim"),
        "proxy"    => Loc.T("L.Perm.proxy"),
        "pm-relay" => Loc.T("L.Perm.pmrelay"),
        "hdv"      => Loc.T("L.Perm.hdv"),
        "socket"   => Loc.T("L.Perm.socket"),   // « Mode Socket » (oublié à l'ajout du droit : la ligne affichait « socket »)
        _          => PermNameFallback(code),
    };

    // Droit sans cas explicite : clé « L.Perm.<code sans tirets> » si le fichier de langue la définit, sinon le nom serveur
    // (Loc.T renvoie la clé quand la ressource manque) -> un nouveau droit n'apparaît plus en clé brute s'il a sa traduction.
    private static string PermNameFallback(string code)
    {
        var key = "L.Perm." + code.Replace("-", "", StringComparison.Ordinal);
        var label = Loc.T(key);
        return label == key ? code : label;
    }
}
