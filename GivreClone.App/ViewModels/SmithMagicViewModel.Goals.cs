using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;
using GivreClone.Engine.Recovery;

namespace GivreClone.App.ViewModels;

public sealed record SmithJobChoice(int JobId, string Name, int? Level);

public partial class BagPickRow : ObservableObject
{
    public long ObjectUid { get; init; }
    public long Gid { get; init; }
    public string Name { get; init; } = "";
    public int Level { get; init; }
    public string TypeName { get; init; } = "";
    public int Quantity { get; init; }
    public bool InBag { get; init; }
    public bool IsRune { get; init; }
    public bool IsResource { get; init; }
    public bool HasSlot { get; init; }
    public int? ItemJob { get; init; }
    public int? ItemLevelValue { get; init; }
    public ItemBind Binding { get; set; }
    public string Detail => string.Format(CultureInfo.CurrentCulture, "Niveau {0} · {1} · x{2}", Level, TypeName, Quantity);
    [ObservableProperty] public partial bool IsChecked { get; set; }
    [ObservableProperty] public partial bool Allowed { get; set; } = true;
    [ObservableProperty] public partial string BlockText { get; set; } = "";
    [ObservableProperty] public partial int CrushQuantity { get; set; } = 1;
    [ObservableProperty] public partial int CrushMaximum { get; set; }
    [ObservableProperty] public partial bool ShowCrushQuantity { get; set; }
    [ObservableProperty] public partial string CrushProvenanceText { get; set; } = "";

    public string? ForgeReason(bool queued, int? selectedJob, int? professionLevel)
        => SmithJournal.ForgeBlock(InBag, queued, ItemBindingRules.CanForge(Binding) ? SmithProfessionGate.Admit(ItemJob, selectedJob, ItemLevelValue, professionLevel) : SmithAdmission.Bound, professionLevel, ItemLevelValue);

    public string? CrushReason(bool listed) => SmithJournal.CrushBlock(InBag, IsRune, IsResource, HasSlot, listed, !ItemBindingRules.CanCrush(Binding));
}

public partial class SmithMagicViewModel
{
    public ObservableCollection<SmithJobChoice> JobChoices { get; } = [];
    public ObservableCollection<SmithQueueRow> CrushQueue { get; } = [];
    public ObservableCollection<BagPickRow> Picks { get; } = [];
    public ObservableCollection<BagPickRow> FilteredPicks { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanStart), nameof(CanApply), nameof(ModeText), nameof(LevelingHint))] public partial bool Leveling { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanStart), nameof(LevelingHint))] public partial int TargetJobLevel { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanStart), nameof(LevelingHint))] public partial SmithRune? LevelingRune { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanStart), nameof(LevelingHint))] public partial SmithJobChoice? SelectedJob { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanStart), nameof(LevelingHint))] public partial bool AdaptiveLeveling { get; set; } = true;
    [ObservableProperty] public partial bool PickerForCrush { get; set; }
    [ObservableProperty] public partial bool IncludeOtherCrushItems { get; set; }
    [ObservableProperty] public partial string CrushProvenanceNote { get; set; } =
        "Par défaut : un exemplaire prouvé par support de session. Provenance ancienne ou après reprise inconnue : sélection explicite requise.";
    [ObservableProperty] public partial string BagSearch { get; set; } = "";
    [ObservableProperty] public partial string BankRuneNote { get; set; } = "";
    public string ModeText => Leveling ? "Mode : montée de métier" : "Mode : objectifs";
    public string LevelingHint => !Leveling ? "" : AdaptiveLeveling
        ? "Supports compatibles et runes économiques automatiques ; dépenses réelles limitées au budget total."
        : LevelingReady ? "La montée passe uniquement la rune choisie jusqu'au niveau cible." : "Choisis le métier, le niveau cible et une rune.";
    public bool LevelingReady => (AdaptiveLeveling || LevelingRune is { ItemId: > 0 }) && TargetJobLevel >= 1 && SelectedJob is { JobId: > 0 };
    public Func<uint, int, int, long, IReadOnlySet<long>?, IReadOnlySet<long>?, CancellationToken,
        Task<(long Uid, string Report)>>? BuyLevelingSupport { get; set; }
    public bool CanCrush => !Busy && _pid > 0 && CrushQueue.Count > 0;

    public Func<uint, long, ItemBind> BindingOf { get; set; } = (pid, uid) => Stores.Launch.ItemBindingOf(pid, (int)uid);
    public Func<long, int?> ItemLevelOf { get; set; } = gid => ItemLevel(Stores.Launch.GameData, gid);
    public Func<long, int?> JobForItem { get; set; } = gid => SkillForItem(Stores.Launch.GameData, gid) is int skill ? JobForSkill(Stores.Launch.GameData, skill) : null;
    public Func<uint, int, int?> JobLevelOf { get; set; } = (pid, job) => Stores.Launch.JobLevels(pid).TryGetValue(job, out var level) ? level : null;
    public Func<uint, long, bool> StillInBag { get; set; } = (pid, uid) => Stores.Launch.BagUidForForge(pid, uid) is not null;
    public Func<uint, int, (int Bag, int Bank, bool Known)> RuneQuantities { get; set; } = (pid, gid) =>
    {
        int bag = Stores.Launch.Inventory.Items(pid).Where(i => i.Position == 63 && i.Gid == gid && i.Qty > 0).Sum(i => i.Qty);
        bool known = Stores.Launch.Bank.IsSeeded(pid);
        int bank = known ? Stores.Launch.Bank.Items(pid).Where(i => i.Gid == gid && i.Qty > 0).Sum(i => i.Qty) : 0;
        return (bag, bank, known);
    };
    public Func<int, string?> LevelingRuneName { get; set; } = gid =>
    {
        var map = Stores.Launch.RuneItemsByGid();
        return map.TryGetValue(gid, out var rune) && rune.Name.Length > 0 ? rune.Name : null;
    };
    public Func<int, string> JobTitle { get; set; } = id =>
    {
        var named = Stores.Launch.GameData?.Jobs().FirstOrDefault(job => job.Id == id);
        return named is { Name.Length: > 0 } ? named.Name : "Métier " + id.ToString(CultureInfo.InvariantCulture);
    };
    public Func<uint, int, int, IReadOnlyList<(int Uid, int Qty)>, CancellationToken, Task<LaunchService.CrushRunReport>>? CrushItems { get; set; }

    private string _pickSignature = "";
    private string _jobSignature = "";
    private bool _bankNoteSent;
    private readonly HashSet<long> _loggedSkips = [];

    partial void OnBagSearchChanged(string value) => ApplyPickFilter();
    partial void OnPickerForCrushChanged(bool value) => ApplyPickFilter();
    partial void OnIncludeOtherCrushItemsChanged(bool value)
    {
        foreach (var row in Picks) { row.IsChecked = false; row.CrushQuantity = 1; }
        if (!value) ClearCrushList();
        ApplyPickFilter();
    }
    partial void OnSelectedJobChanged(SmithJobChoice? value) => RefreshPickBlocks();

    [RelayCommand] private void UseObjectives() => Leveling = false;
    [RelayCommand] private void UseLevelingMode() => Leveling = true;
    [RelayCommand] private void PickForge() { PickerForCrush = false; }
    [RelayCommand] private void PickCrush() { PickerForCrush = true; }
    [RelayCommand] private void SelectVisiblePicks()
    {
        var ids = BagPicker.VisibleAllowed(VisibleProjection()).ToHashSet();
        foreach (var row in FilteredPicks) if (row.Allowed) row.IsChecked = ids.Contains(row.ObjectUid);
    }
    [RelayCommand] private void AddCheckedToForge() => AddUids(BagPicker.AddOrder(VisibleProjection(), CheckedIds()), false);
    [RelayCommand] private void AddCheckedToCrush() => AddUids(BagPicker.AddOrder(VisibleProjection(), CheckedIds()), true);
    [RelayCommand] private void AddSearchHit()
    {
        if (BagPicker.Enter(VisibleProjection()) is not long uid) return;
        AddUids(new[] { uid }, PickerForCrush);
    }

    public void SelectPickRange(long anchor, long current)
    {
        var ids = BagPicker.ShiftRange(VisibleProjection(), anchor, current).ToHashSet();
        foreach (var row in FilteredPicks) row.IsChecked = row.Allowed && ids.Contains(row.ObjectUid);
    }

    public void TogglePick(long uid)
    {
        if (FilteredPicks.FirstOrDefault(row => row.ObjectUid == uid) is not { Allowed: true } row) return;
        row.IsChecked = !row.IsChecked;
    }

    [RelayCommand] private void RemoveCrushItem(SmithQueueRow? item)
    {
        if (item is null) return;
        CrushQueue.Remove(item);
        _crushChoices.Remove(item.ObjectUid);
        RefreshPickBlocks();
        OnPropertyChanged(nameof(CanCrush));
    }

    [RelayCommand] private void ClearCrushList()
    {
        CrushQueue.Clear();
        _crushChoices.Clear();
        RefreshPickBlocks();
        OnPropertyChanged(nameof(CanCrush));
    }

    private void ClearCharacterGoals()
    {
        CrushQueue.Clear();
        _crushChoices.Clear();
        IncludeOtherCrushItems = false;
        if (_crushOwner is { } previous) CrushProvenance.Reconcile(previous, _ => (null, 0));
        _crushOwner = null;
        Picks.Clear();
        FilteredPicks.Clear();
        JobChoices.Clear();
        SelectedJob = null;
        _pickSignature = "";
        _jobSignature = "";
        _loggedSkips.Clear();
        _bankNoteSent = false;
        OnPropertyChanged(nameof(CanCrush));
        OnPropertyChanged(nameof(CanStart));
    }
}
