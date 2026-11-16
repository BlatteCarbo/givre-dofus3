using System;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>Affichage « Mode discret » (StreamerPrivacy) de la carte compte : <see cref="Login"/> / <see cref="ExitIp"/>
/// restent bruts (moteur, MCP, copie) ; seules les propriétés Display* sont masquées, et suivent le réglage à chaud.</summary>
public partial class AccountRow
{
    public string DisplayLogin { get { HookPrivacy(); return PrivacyMask.MaskLogin(Login, AppConfig.Current.StreamerPrivacy); } }
    public string DisplayExitIp { get { HookPrivacy(); return PrivacyMask.MaskIp(ExitIp, AppConfig.Current.StreamerPrivacy); } }

    partial void OnExitIpChanged(string value) => OnPropertyChanged(nameof(DisplayExitIp));

    private bool _privacyHooked;
    // Abonnement paresseux (1re lecture par un binding) et FAIBLE : l'événement statique ne retient pas une carte retirée.
    private void HookPrivacy()
    {
        if (_privacyHooked) return;
        _privacyHooked = true;
        var weak = new WeakReference<AccountRow>(this);
        Action? handler = null;
        handler = () =>
        {
            if (weak.TryGetTarget(out var row)) row.RaisePrivacyChanged();
            else AppConfig.UiPreferencesChanged -= handler;
        };
        AppConfig.UiPreferencesChanged += handler;
    }

    private void RaisePrivacyChanged()
    {
        OnPropertyChanged(nameof(DisplayLogin));
        OnPropertyChanged(nameof(DisplayExitIp));
    }
}
