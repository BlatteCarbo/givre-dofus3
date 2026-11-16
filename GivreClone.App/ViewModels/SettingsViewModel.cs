using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

// Page Réglages ; logique en code-behind, sauf mode d'affichage et mode discret (persistés ici).
public sealed partial class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel()
    {
        AppConfig.UiPreferencesChanged += RaiseUiPreferences;
        LicenseService.PermissionsChanged += () => Dispatcher.UIThread.Post(RaiseUiPreferences);
    }

    public string UiMode => AppConfig.Current.UiMode;
    public string EffectiveUiMode => AppConfig.EffectiveUiMode;
    public bool IsStandardUi => EffectiveUiMode == "standard";
    public bool IsExpertUi => EffectiveUiMode is "expert" or "dev";
    public bool IsDeveloperUi => EffectiveUiMode == "dev";
    /// <summary>Expert strict (sans « dev ») : état actif du bouton « Expert » du sélecteur.</summary>
    public bool IsExpertOnlyUi => EffectiveUiMode == "expert";
    public bool IsDeveloperLicense => LicenseService.IsDeveloper;

    /// <summary>Mode discret (liaison bidirectionnelle du commutateur) : enregistre puis notifie les autres VMs.</summary>
    public bool StreamerPrivacy
    {
        get => AppConfig.Current.StreamerPrivacy;
        set
        {
            if (AppConfig.Current.StreamerPrivacy == value) return;
            AppConfig.Current.StreamerPrivacy = value;
            AppConfig.Save();
            AppConfig.NotifyUiPreferencesChanged();
        }
    }

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
        OnPropertyChanged(nameof(IsExpertOnlyUi));
        OnPropertyChanged(nameof(IsDeveloperLicense));
        OnPropertyChanged(nameof(StreamerPrivacy));
    }
}
