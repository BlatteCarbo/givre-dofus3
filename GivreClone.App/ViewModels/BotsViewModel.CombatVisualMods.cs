using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
namespace GivreClone.App.ViewModels;
public partial class BotsViewModel
{
    private string _combatVisualStatus = "Options visuelles : verifier les modules avant utilisation.";
    public string CombatVisualStatus => _combatVisualStatus;
    private bool? _visualModulesAvailable;
    private bool? _lastVisualOptionsEnabled;
    private bool HasVisualModules => _visualModulesAvailable ??= CombatVisualOptions.Available(Path.Combine(AppContext.BaseDirectory, "NativeTurboRuntime"));
    public bool CanCombatVisualOptions => AppConfig.Current.ModsEnabled && CanNoAnim && !NativeTurboRuntime.EnhancedOwnsHooks(_profileBot?.Pid ?? 0) && HasVisualModules &&
        !CombatVisualOptions.CompatibilityRefused(NativeTurboRuntime.VisualOptionsStatus(_profileBot?.Pid ?? 0)) &&
        !LaunchService.IsSocketPid((uint)Math.Max(0, _profileBot?.Pid ?? 0));
    public bool CanCombatInstantMove => CanCombatVisualOptions;
    public bool CanCombatCollapse => CanCombatVisualOptions;
    public bool CombatNoSpellEffects
    {
        get => _profile.NoSpellEffects;
        set { if (!CanEditVisual(value) || _profile.NoSpellEffects == value) return; _profile.NoSpellEffects = value; OnPropertyChanged(); SaveAndPush(); }
    }
    public bool CombatShortSpellCasts
    {
        get => _profile.ShortSpellCasts;
        set { if (!CanEditVisual(value) || _profile.ShortSpellCasts == value) return; _profile.ShortSpellCasts = value; OnPropertyChanged(); SaveAndPush(); }
    }
    public bool CombatNoDeathAnimations
    {
        get => _profile.NoDeathAnimations;
        set { if (!CanEditVisual(value) || _profile.NoDeathAnimations == value) return; _profile.NoDeathAnimations = value; OnPropertyChanged(); SaveAndPush(); }
    }
    public int CombatAppearanceMode
    {
        get => CombatVisualOptions.NormalizeMode(_profile.CombatAppearanceMode);
        set => SetAppearanceMode(_profile.CombatAppearanceMode, value, mode => _profile.CombatAppearanceMode = mode, nameof(CombatMiniatureCategoriesEnabled));
    }
    public int RoleplayAppearanceMode
    {
        get => CombatVisualOptions.NormalizeMode(_profile.RoleplayAppearanceMode);
        set => SetAppearanceMode(_profile.RoleplayAppearanceMode, value, mode => _profile.RoleplayAppearanceMode = mode, nameof(RoleplayMiniatureCategoriesEnabled));
    }
    public bool CombatMiniatureCategoriesEnabled => CanCombatVisualOptions && CombatAppearanceMode == 2;
    public bool RoleplayMiniatureCategoriesEnabled => CanCombatVisualOptions && RoleplayAppearanceMode == 2;
    public bool CombatMiniatureSelf { get => _profile.MiniatureSelf; set => SetMiniature(_profile.MiniatureSelf, value, v => _profile.MiniatureSelf = v); }
    public bool CombatMiniatureAllies { get => _profile.MiniatureAllies; set => SetMiniature(_profile.MiniatureAllies, value, v => _profile.MiniatureAllies = v); }
    public bool CombatMiniatureAllySummons { get => _profile.MiniatureAllySummons; set => SetMiniature(_profile.MiniatureAllySummons, value, v => _profile.MiniatureAllySummons = v); }
    public bool CombatMiniatureOwnSummons { get => _profile.MiniatureOwnSummons; set => SetMiniature(_profile.MiniatureOwnSummons, value, v => _profile.MiniatureOwnSummons = v); }
    public bool CombatMiniatureEnemies { get => _profile.MiniatureEnemies; set => SetMiniature(_profile.MiniatureEnemies, value, v => _profile.MiniatureEnemies = v); }
    public bool CombatMiniatureEnemySummons { get => _profile.MiniatureEnemySummons; set => SetMiniature(_profile.MiniatureEnemySummons, value, v => _profile.MiniatureEnemySummons = v); }
    public bool RoleplayMiniatureSelf { get => _profile.RoleplayMiniatureSelf; set => SetMiniature(_profile.RoleplayMiniatureSelf, value, v => _profile.RoleplayMiniatureSelf = v); }
    public bool RoleplayMiniatureParty { get => _profile.RoleplayMiniatureParty; set => SetMiniature(_profile.RoleplayMiniatureParty, value, v => _profile.RoleplayMiniatureParty = v); }
    public bool RoleplayMiniaturePlayers { get => _profile.RoleplayMiniaturePlayers; set => SetMiniature(_profile.RoleplayMiniaturePlayers, value, v => _profile.RoleplayMiniaturePlayers = v); }
    public bool RoleplayMiniatureMonsters { get => _profile.RoleplayMiniatureMonsters; set => SetMiniature(_profile.RoleplayMiniatureMonsters, value, v => _profile.RoleplayMiniatureMonsters = v); }
    public bool RoleplayMiniatureNpcs { get => _profile.RoleplayMiniatureNpcs; set => SetMiniature(_profile.RoleplayMiniatureNpcs, value, v => _profile.RoleplayMiniatureNpcs = v); }
    private void SetAppearanceMode(int current, int value, Action<int> assign, string categoriesEnabled, [CallerMemberName] string? name = null)
    {
        int mode = CombatVisualOptions.NormalizeMode(value);
        if (!CanEditVisual(mode != 0)) { OnPropertyChanged(name); return; }
        if (current == mode) return;
        assign(mode);
        OnPropertyChanged(name);
        OnPropertyChanged(categoriesEnabled);
        SaveAndPush();
    }
    private void SetMiniature(bool current, bool value, Action<bool> assign, [CallerMemberName] string? name = null)
    {
        if (!CanEditVisual(value) || current == value) return;
        assign(value);
        OnPropertyChanged(name);
        SaveAndPush();
    }
    private bool CanEditVisual(bool enabling) => CombatProfileReady && (!enabling || CanCombatVisualOptions);
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // Bot selection and permission changes must invalidate the status and
        // derived gates too; otherwise the previous client's READY/Socket state
        // and an expired no-anim permission remain displayed until a manual check.
        if (e.PropertyName is nameof(CombatProfileReady) or nameof(CanNoAnim))
            RefreshCombatVisualStatus();
    }
    private void NotifyVisualProperties()
    {
        OnPropertyChanged(nameof(CombatNoSpellEffects)); OnPropertyChanged(nameof(CombatShortSpellCasts));
        OnPropertyChanged(nameof(CombatNoDeathAnimations)); OnPropertyChanged(nameof(CanCombatVisualOptions));
        OnPropertyChanged(nameof(CanCombatInstantMove)); OnPropertyChanged(nameof(CanCombatCollapse));
        OnPropertyChanged(nameof(CombatAppearanceMode)); OnPropertyChanged(nameof(RoleplayAppearanceMode));
        OnPropertyChanged(nameof(CombatMiniatureCategoriesEnabled)); OnPropertyChanged(nameof(RoleplayMiniatureCategoriesEnabled));
        OnPropertyChanged(nameof(CombatMiniatureSelf)); OnPropertyChanged(nameof(CombatMiniatureAllies));
        OnPropertyChanged(nameof(CombatMiniatureAllySummons)); OnPropertyChanged(nameof(CombatMiniatureOwnSummons));
        OnPropertyChanged(nameof(CombatMiniatureEnemies)); OnPropertyChanged(nameof(CombatMiniatureEnemySummons));
        OnPropertyChanged(nameof(RoleplayMiniatureSelf)); OnPropertyChanged(nameof(RoleplayMiniatureParty));
        OnPropertyChanged(nameof(RoleplayMiniaturePlayers)); OnPropertyChanged(nameof(RoleplayMiniatureMonsters));
        OnPropertyChanged(nameof(RoleplayMiniatureNpcs));
    }
    [RelayCommand]
    private void RefreshCombatVisualStatus()
    {
        _visualModulesAvailable = null;
        RefreshLiveCombatVisualStatus();
    }
    private void RefreshLiveCombatVisualStatus()
    {
        bool socket = LaunchService.IsSocketPid((uint)Math.Max(0, _profileBot?.Pid ?? 0));
        string live = NativeTurboRuntime.VisualOptionsStatus(_profileBot?.Pid ?? 0);
        string status = socket ? "Mode Socket : aucun client graphique."
            : !AppConfig.Current.ModsEnabled ? "Activer les mods natifs dans les reglages."
            : !CanNoAnim ? "Droit no-anim indisponible."
            : NativeTurboRuntime.EnhancedOwnsHooks(_profileBot?.Pid ?? 0) ? "Dofus Enhanced : activation en cours ou module charge dans ce client. Les modules visuels autonomes sont exclus pour eviter les conflits ; effet en jeu a verifier."
            : !HasVisualModules ? "Module visuel absent ou empreinte incorrecte."
            : string.IsNullOrEmpty(live) ? "Modules verifies. Chargement demande par le profil ; effet en combat a verifier."
            : "Module visuel : " + live;
        bool enabled = CanCombatVisualOptions;
        if (_combatVisualStatus == status && _lastVisualOptionsEnabled == enabled) return;
        _combatVisualStatus = status;
        _lastVisualOptionsEnabled = enabled;
        OnPropertyChanged(nameof(CombatVisualStatus)); NotifyVisualProperties();
    }
    [RelayCommand]
    private void EnableCombatNoAnimPreset()
    {
        if (!CombatProfileReady || !AppConfig.Current.ModsEnabled || !CanNoAnim) { RefreshCombatVisualStatus(); return; }
        _profile.NoSpellEffects = CanCombatVisualOptions;
        _profile.ShortSpellCasts = CanCombatVisualOptions;
        _profile.NoAnim = true;
        _profile.NoDeathAnimations = false; _profile.Collapse = false; _profile.InstantMove = false;
        NotifyVisualProperties(); OnPropertyChanged(nameof(CombatNoAnim));
        OnPropertyChanged(nameof(CombatCollapse)); OnPropertyChanged(nameof(CombatInstantMove));
        SaveAndPush(); RefreshCombatVisualStatus();
    }
    [RelayCommand]
    private void DisableCombatVisualPresets()
    {
        if (!CombatProfileReady) return;
        _profile.NoSpellEffects = false; _profile.ShortSpellCasts = false; _profile.NoDeathAnimations = false;
        _profile.NoAnim = false; _profile.Collapse = false; _profile.InstantMove = false;
        _profile.CombatAppearanceMode = 0; _profile.RoleplayAppearanceMode = 0;
        NotifyVisualProperties(); OnPropertyChanged(nameof(CombatNoAnim));
        OnPropertyChanged(nameof(CombatCollapse)); OnPropertyChanged(nameof(CombatInstantMove));
        SaveAndPush(); RefreshCombatVisualStatus();
    }
}
