using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

// Onglet Combat : verrous/turbo du profil et import du profil d'un autre personnage (MZP23). Logique pure : CombatImportLogic (testée).
// Réglages Turbo (TurboAckAll, TurboEagerReady) : consommés par le transport Socket (SocketGameClient) et le MITM (LaunchService.Packets) via
// TurboFightPolicy, uniquement en vitesse Turbo. ChallengeAbandonHp : lu en début de tour par le moteur (ChallengesAbandoned).
// Les verrous de début de combat (LockJoin/Party/Spectators) sont pris en compte (FightLocks.Merge).
public partial class BotsViewModel
{
    // ---- Verrous / Turbo / défis ----
    public bool CombatLockJoin { get => _profile.LockJoin; set { if (_profile.LockJoin != value) { _profile.LockJoin = value; OnPropertyChanged(); SaveAndPush(); } } }
    public bool CombatLockParty { get => _profile.LockParty; set { if (_profile.LockParty != value) { _profile.LockParty = value; OnPropertyChanged(); SaveAndPush(); } } }
    public bool CombatLockSpectators { get => _profile.LockSpectators; set { if (_profile.LockSpectators != value) { _profile.LockSpectators = value; OnPropertyChanged(); SaveAndPush(); } } }
    /// <summary>Les verrous spectateur / groupe / fermeture ne partent que du chef. Le suiveur les ignore.</summary>
    public string CombatLockLeaderNote => "Réservé au chef : un suiveur ne pose pas ces verrous.";
    public bool CombatTurboAckAll { get => _profile.TurboAckAll; set { if (_profile.TurboAckAll != value) { _profile.TurboAckAll = value; OnPropertyChanged(); SaveAndPush(); } } }
    public bool CombatTurboEagerReady { get => _profile.TurboEagerReady; set { if (_profile.TurboEagerReady != value) { _profile.TurboEagerReady = value; OnPropertyChanged(); SaveAndPush(); } } }
    public int CombatChallengeAbandonHp
    {
        get => _profile.ChallengeAbandonHp;
        set { value = Math.Clamp(value, 0, 100); if (_profile.ChallengeAbandonHp != value) { _profile.ChallengeAbandonHp = value; OnPropertyChanged(); SaveAndPush(); } }
    }

    /// <summary>Les options Turbo ne s'affichent que si la vitesse choisie est Turbo.</summary>
    public bool CombatIsTurbo => _profile.Speed == CombatSpeed.Turbo;

    public sealed record PlacementModeOption(PlacementMode Mode, string LabelKey)
    {
        public string Label => Loc.T(LabelKey);
    }
    public IReadOnlyList<PlacementModeOption> PlacementModeOptions { get; } =
    [
        new(PlacementMode.LegacyByStyle, "L.Bots.Combat.Placement.Legacy"),
        new(PlacementMode.Keep, "L.Bots.Combat.Placement.None"),
        new(PlacementMode.Near, "L.Bots.Combat.Placement.Near"),
        new(PlacementMode.Far, "L.Bots.Combat.Placement.Far"),
    ];
    public PlacementModeOption CombatPlacementMode
    {
        get => PlacementModeOptions.First(c => c.Mode == _profile.PlacementMode);
        set
        {
            if (value is null || _profile.PlacementMode == value.Mode) return;
            _profile.PlacementMode = value.Mode;
            OnPropertyChanged();
            SaveAndPush();
        }
    }
    public bool CombatNeverApproach
    {
        get => _profile.MovementPolicy == CombatMovementPolicy.NeverApproach;
        set
        {
            var next = value ? CombatMovementPolicy.NeverApproach : CombatMovementPolicy.Standard;
            if (_profile.MovementPolicy == next) return;
            _profile.MovementPolicy = next;
            OnPropertyChanged();
            SaveAndPush();
        }
    }

    private uint? SelectedPid => _profileBot?.Row.Pid is int p and > 0 ? (uint)p : null;
    public bool HasCombatLockScriptNote => SelectedPid is uint pid && Stores.Launch.ScriptLocksFight(pid);
    public string CombatLockScriptNote => HasCombatLockScriptNote ? "Un script en cours pose ses propres verrous ; ils s'ajoutent à ceux du profil." : "";

    /// <summary>Recalcule les propriétés de cette partie (appelé à chaque chargement de profil et changement de vitesse).</summary>
    private void RaiseCombatExtras()
    {
        foreach (var n in new[] { nameof(CombatLockJoin), nameof(CombatLockParty), nameof(CombatLockSpectators), nameof(CombatLockLeaderNote),
                                  nameof(CombatTurboAckAll), nameof(CombatTurboEagerReady), nameof(CombatChallengeAbandonHp), nameof(CombatIsTurbo),
                                  nameof(CombatPlacementMode), nameof(CombatNeverApproach),
                                  nameof(HasCombatLockScriptNote), nameof(CombatLockScriptNote), nameof(ShowSelectHint) })
            OnPropertyChanged(n);
    }

    /// <summary>Aucun bot sélectionné : invite à en choisir un.</summary>
    public bool ShowSelectHint => !HasSelection;

    // ---- Changement de mode (MITM ↔ Socket) du compte du bot ----
    [RelayCommand]
    private void SwitchMode(BotRowViewModel? bot)
    {
        var row = bot?.Row;
        if (row is null) return;
        if (row.Running) { Toasts.Show("Arrête d'abord le compte pour changer de mode.", success: false); return; }
        row.IsSocketMode = !row.IsSocketMode;
        Toasts.Show($"{row.Login} : mode {(row.IsSocketMode ? "Socket" : "MITM")}.");
    }

    // ---- Import du profil de combat d'un autre personnage ----
    [ObservableProperty] public partial bool CombatImportVisible { get; set; }
    [ObservableProperty] public partial CombatImportSource? CombatImportFrom { get; set; }
    [ObservableProperty] public partial bool CombatImportMerge { get; set; } = true;
    [ObservableProperty] public partial bool CombatImportReplace { get; set; }
    public ObservableCollection<CombatImportSource> CombatImportSources { get; } = new();
    public ObservableCollection<CombatImportItem> CombatImportSettings { get; } = new();
    public ObservableCollection<CombatImportItem> CombatImportSpells { get; } = new();
    public ObservableCollection<CombatImportItem> CombatImportRemoved { get; } = new();

    private ImportPlan? _importPlan;
    private CombatProfile? _importSource;

    public bool HasCombatImportSources => CombatImportSources.Count > 0;
    public bool HasCombatImportSource => CombatImportFrom is not null;
    public bool HasCombatImportSpells => CombatImportSpells.Count > 0;
    public bool HasCombatImportRemoved => CombatImportRemoved.Count > 0;
    public bool CombatImportIdentical => _importPlan?.Identical ?? false;
    public string CombatImportSubtitle => CombatImportFrom is { } f ? $"Depuis {f.Name}" + (f.Detail.Length > 0 ? $" · {f.Detail}" : "") : "Choisis un personnage";
    public string CombatImportSummary => _importPlan is { } p ? CombatImportLogic.Summary(p) : "";
    public string CombatImportSpellsNote => CombatImportFrom is { SameClass: false } ? "Autre classe : certains sorts n'existent peut-être pas pour ce personnage." : "";
    public bool CanConfirmCombatImport => HasCombatImportSource && (CombatImportSettings.Concat(CombatImportSpells).Any(i => i.IsSelected) || (CombatImportReplace && HasCombatImportRemoved));

    partial void OnCombatImportMergeChanged(bool value) { if (value && CombatImportReplace) CombatImportReplace = false; else if (!value && !CombatImportReplace) CombatImportReplace = true; }
    partial void OnCombatImportReplaceChanged(bool value)
    {
        if (value && CombatImportMerge) CombatImportMerge = false; else if (!value && !CombatImportMerge) CombatImportMerge = true;
        RebuildCombatImport();
    }
    partial void OnCombatImportFromChanged(CombatImportSource? value) => RebuildCombatImport();

    [RelayCommand]
    private void OpenCombatImport()
    {
        if (_profileBot is null || !CombatProfileReady) { Toasts.Show("Sélectionne un personnage identifié.", success: false); return; }
        CombatImportSources.Clear();
        foreach (var b in Bots.Where(b => !ReferenceEquals(b, _profileBot)))
        {
            var name = b.Row.CharacterName;
            if (!CombatProfileStore.TryGet(name, out _)) continue;   // seulement les personnages qui ont un profil enregistré
            var detail = string.Join(" · ", new[] { b.Row.Breed, b.Row.Level is int l ? $"niv. {l}" : null }.Where(x => !string.IsNullOrEmpty(x)));
            CombatImportSources.Add(new CombatImportSource { Name = name!, Detail = detail, SameClass = b.Row.BreedId > 0 && b.Row.BreedId == _profileBot.Row.BreedId });
        }
        CombatImportFrom = null;
        CombatImportMerge = true;
        RebuildCombatImport();
        CombatImportVisible = true;
        OnPropertyChanged(nameof(HasCombatImportSources));
    }

    [RelayCommand] private void CloseCombatImport() => CombatImportVisible = false;

    private void RebuildCombatImport()
    {
        CombatImportSettings.Clear(); CombatImportSpells.Clear(); CombatImportRemoved.Clear();
        _importPlan = null; _importSource = null;
        if (CombatImportFrom is { } f && CombatProfileStore.TryGet(f.Name, out var src))
        {
            _importSource = src;
            var plan = _importPlan = CombatImportLogic.Diff(_profile, src, CombatImportReplace);
            bool first = true;
            foreach (var s in plan.Settings)
            {
                CombatImportSettings.Add(new CombatImportItem
                {
                    Key = s.Key, Label = s.Label, Current = s.Current, Imported = s.Incoming, Differs = s.Differs,
                    Header = first ? "Réglages" : "", IsSelected = s.Differs,
                });
                first = false;
            }
            first = true;
            foreach (var sp in plan.Spells)
            {
                CombatImportSpells.Add(new CombatImportItem
                {
                    Key = sp.SpellId.ToString(), SpellId = sp.SpellId, Label = SpellDisplayName(sp.SpellId), Icon = SpellIcon(sp.SpellId),
                    Current = sp.Current is null ? "—" : CombatImportLogic.Describe(sp.Current, sp.Incoming), Imported = CombatImportLogic.Describe(sp.Incoming, sp.Current),
                    IsNew = sp.IsNew, Differs = sp.Differs, Header = first ? "Sorts" : "", IsSelected = sp.Differs,
                });
                first = false;
            }
            foreach (var r in plan.Removed)
                CombatImportRemoved.Add(new CombatImportItem { Key = r.SpellId.ToString(), SpellId = r.SpellId, Label = SpellDisplayName(r.SpellId), Icon = SpellIcon(r.SpellId), Current = CombatImportLogic.Describe(r), Imported = "retiré", Differs = true });
        }
        foreach (var item in CombatImportSettings.Concat(CombatImportSpells))   // cocher / décocher -> bouton Importer
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CombatImportItem.IsSelected)) OnPropertyChanged(nameof(CanConfirmCombatImport)); };
        foreach (var n in new[] { nameof(HasCombatImportSource), nameof(HasCombatImportSpells), nameof(HasCombatImportRemoved), nameof(CombatImportIdentical),
                                  nameof(CombatImportSubtitle), nameof(CombatImportSummary), nameof(CombatImportSpellsNote), nameof(CanConfirmCombatImport) })
            OnPropertyChanged(n);
    }

    [RelayCommand] private void CombatImportCheckAll() { foreach (var i in CombatImportSettings.Concat(CombatImportSpells).Where(i => i.CanSelect)) i.IsSelected = true; OnPropertyChanged(nameof(CanConfirmCombatImport)); }
    [RelayCommand] private void CombatImportCheckNone() { foreach (var i in CombatImportSettings.Concat(CombatImportSpells)) i.IsSelected = false; OnPropertyChanged(nameof(CanConfirmCombatImport)); }

    [RelayCommand]
    private void ConfirmCombatImport()
    {
        if (_importSource is null || !CanConfirmCombatImport) return;
        var keys = CombatImportSettings.Where(i => i.IsSelected).Select(i => i.Key).ToHashSet();
        var ids = CombatImportSpells.Where(i => i.IsSelected).Select(i => i.SpellId).ToHashSet();
        var r = CombatImportLogic.Apply(_profile, _importSource, keys, ids, CombatImportReplace);
        SaveAndPush();
        LoadCombatProfile(_profileBot);   // recharge les propriétés liées et la liste de sorts
        CombatImportVisible = false;
        Toasts.Show($"Importé : {r.Settings} réglage(s), {r.SpellsAdded} sort(s) ajouté(s), {r.SpellsReplaced} remplacé(s), {r.SpellsRemoved} retiré(s).");
    }
}
