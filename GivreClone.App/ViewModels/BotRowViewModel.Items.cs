using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>Un emplacement d'équipement de l'onglet Inventaire (vide ou occupé). Clic droit : Déséquiper.</summary>
public sealed class EquipSlotRow
{
    public BotRowViewModel Owner { get; }
    public int Position { get; }
    public string Label { get; }
    public int Uid { get; }
    public long Gid { get; }
    public string Name { get; }
    public Bitmap? Icon { get; }
    public bool HasItem => Uid != 0;
    public bool HasIcon => Icon is not null;
    public string Tip => HasItem ? $"{Label} — {Name}" : Label;
    /// <summary>L'objet porté vu comme une ligne d'inventaire (infobulle riche avec ses effets) ; null si vide.</summary>
    public InventoryItemRow? Item { get; }

    public EquipSlotRow(BotRowViewModel owner, int position, InventoryStore.Item? item)
    {
        Owner = owner; Position = position;
        Label = InteractionLogic.SlotKey(position) is { } k ? Loc.T(k) : Loc.Fmt("L.Inv.Slot.Other", position);
        if (item is { } it)
        {
            Uid = it.Uid; Gid = it.Gid;
            Name = Stores.Launch.ItemDisplayName(it.Gid);
            Icon = Stores.InteractiveIcons.GetByIcon(Stores.Launch.ItemIconId(it.Gid));
            Item = new InventoryItemRow(it, owner);
        }
        else Name = "";
    }
}

// ===================== Onglet Inventaire : équipement + menu clic droit (utiliser, équiper, jeter, détruire, déposer, échanger, vendre) =====================
// Toutes les actions visent la PILE cliquée (uid) et passent par LaunchService (gates de licence, refus en combat, MITM comme Socket).
// Le résultat affiché est l'envoi ; la vérité (pile retirée, objet équipé) arrive par les décodeurs au tick suivant.
public partial class BotRowViewModel
{
    public ObservableCollection<EquipSlotRow> EquipLeft { get; } = new();
    public ObservableCollection<EquipSlotRow> EquipRight { get; } = new();
    public ObservableCollection<EquipSlotRow> EquipDofus { get; } = new();
    /// <summary>Objets portés hors des 16 emplacements de base (monture, familier secondaire, cosmétiques…).</summary>
    public ObservableCollection<EquipSlotRow> EquipOther { get; } = new();
    public bool HasEquipOther => EquipOther.Count > 0;

    private void RefreshEquipment(IReadOnlyList<InventoryStore.Item> items)
    {
        var worn = items.Where(i => i.Position != 63).GroupBy(i => i.Position).ToDictionary(g => g.Key, g => g.First());
        void FillSlots(ObservableCollection<EquipSlotRow> target, IEnumerable<int> positions)
        {
            target.Clear();
            foreach (int p in positions) target.Add(new EquipSlotRow(this, p, worn.TryGetValue(p, out var it) ? it : null));
        }
        FillSlots(EquipLeft, InteractionLogic.LeftSlots);
        FillSlots(EquipRight, InteractionLogic.RightSlots);
        FillSlots(EquipDofus, InteractionLogic.DofusSlots);
        FillSlots(EquipOther, worn.Keys.Where(p => p > 15).OrderBy(p => p));
        OnPropertyChanged(nameof(HasEquipOther));
    }

    /// <summary>Quantité utilisée par Jeter / Détruire / Déposer / Échanger depuis le menu (0 ou « Tout » = pile entière).</summary>
    [ObservableProperty] public partial int ItemActionQty { get; set; } = 1;
    [ObservableProperty] public partial bool ItemActionAll { get; set; }
    private int ItemQty => ItemActionAll ? 0 : Math.Max(1, ItemActionQty);

    // Contexte du menu clic droit : actions visibles seulement quand l'échange correspondant est ouvert.
    [ObservableProperty] public partial bool CtxBankOpen { get; set; }
    [ObservableProperty] public partial bool CtxTradeOpen { get; set; }
    [ObservableProperty] public partial bool CtxShopOpen { get; set; }

    private void RefreshItemContext()
    {
        var k = Pid > 0 ? Stores.Launch.CurrentExchange((uint)Pid) : ExchangeKind.None;
        CtxBankOpen = k == ExchangeKind.Bank; CtxTradeOpen = k == ExchangeKind.Trade; CtxShopOpen = k == ExchangeKind.Shop;
    }

    [ObservableProperty] public partial string ItemStatus { get; set; } = "";
    public bool HasItemStatus => ItemStatus.Length > 0;
    partial void OnItemStatusChanged(string value) => OnPropertyChanged(nameof(HasItemStatus));

    private void ItemReport(bool ok, string verb, string name)
    {
        ItemStatus = ok ? Loc.Fmt("L.Iw.Sent", $"{verb} {name}") : Loc.Fmt("L.Iw.Refused", $"{verb} {name}");
        _invSig = -1;   // relit le sac au prochain tick (accusé serveur)
    }

    private uint P => (uint)Math.Max(0, Pid);

    [RelayCommand] private void UseItem(InventoryItemRow? r) { if (r is not null) ItemReport(Stores.Launch.UseItemUid(P, r.Uid), Loc.T("L.Inv.Use"), r.Name); }

    [RelayCommand]
    private async Task OpenAllBagsAsync()
    {
        var bags = Stores.Launch.Inventory.Items(P)
            .Where(it => it.Position == 63 && (Stores.Launch.ItemTypeId(it.Gid) == 100
                || (Stores.Launch.ItemDisplayName(it.Gid) is { } n && (n.StartsWith("Sac de ", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Sac d'", StringComparison.OrdinalIgnoreCase)))))
            .ToList();
        if (bags.Count == 0) { ItemStatus = "Aucun sac de ressources dans le sac."; return; }
        int count = 0;
        foreach (var it in bags)
        {
            if (it.Qty > 0 && Stores.Launch.UseInventoryItemMultiple(P, it.Uid, it.Qty))
            {
                count += it.Qty;
                await Task.Delay(150).ConfigureAwait(true);
            }
        }
        ItemStatus = count > 0 ? $"{count} sac(s) de ressources ouvert(s)." : "Ouverture des sacs terminée.";
        _invSig = -1;
    }

    [RelayCommand]
    private void EquipItem(InventoryItemRow? r)
    {
        if (r is null) return;
        if (r.IsEquipped) { ItemReport(Stores.Launch.UnequipItemUid(P, r.Uid), Loc.T("L.Inv.Unequip"), r.Name); return; }
        if (Stores.Launch.EquipTarget(P, r.Gid) < 0) { ItemStatus = Loc.Fmt("L.Inv.NotEquipment", r.Name); return; }
        ItemReport(Stores.Launch.EquipItemUid(P, r.Uid), Loc.T("L.Inv.Equip"), r.Name);
    }

    [RelayCommand] private void UnequipSlot(EquipSlotRow? s) { if (s is { HasItem: true }) ItemReport(Stores.Launch.UnequipItemUid(P, s.Uid), Loc.T("L.Inv.Unequip"), s.Name); }

    // ---- Sélecteur « Équiper » (clic sur un emplacement, comme dans le jeu) : objets du sac compatibles avec CET emplacement ----
    public ObservableCollection<InventoryItemRow> SlotPickerItems { get; } = new();
    [ObservableProperty] public partial bool SlotPickerVisible { get; set; }
    [ObservableProperty] public partial string SlotPickerTitle { get; set; } = "";
    public bool SlotPickerEmpty => SlotPickerItems.Count == 0;
    private int _slotPickerPosition = -1;

    [RelayCommand]
    private void OpenSlotPicker(EquipSlotRow? s)
    {
        if (s is null) return;
        if (SlotPickerVisible && _slotPickerPosition == s.Position) { CloseSlotPicker(); return; }   // 2e clic : referme
        _slotPickerPosition = s.Position;
        SlotPickerTitle = s.HasItem ? Loc.Fmt("L.Inv.PickerTitleWorn", s.Label, s.Name) : Loc.Fmt("L.Inv.PickerTitle", s.Label);
        FillSlotPicker();
        SlotPickerVisible = true;
    }

    [RelayCommand] private void CloseSlotPicker() { SlotPickerVisible = false; _slotPickerPosition = -1; SlotPickerItems.Clear(); }

    private void FillSlotPicker()
    {
        SlotPickerItems.Clear();
        int pos = _slotPickerPosition;
        if (pos < 0) return;
        var l = Stores.Launch;
        foreach (var it in l.Inventory.Items(P).Where(i => i.Position == 63 && i.Qty > 0 && l.EquipPositions(i.Gid).Contains(pos))
                                               .OrderByDescending(i => l.ItemLevel((int)i.Gid)).ThenBy(i => l.ItemDisplayName(i.Gid)))
            SlotPickerItems.Add(new InventoryItemRow(it, this));
        OnPropertyChanged(nameof(SlotPickerEmpty));
    }

    /// <summary>Clic sur un objet du sélecteur : équipé À CET emplacement (l'objet porté retourne au sac).</summary>
    [RelayCommand]
    private void PickForSlot(InventoryItemRow? r)
    {
        if (r is null || _slotPickerPosition < 0) return;
        ItemReport(Stores.Launch.EquipItemUidAt(P, r.Uid, _slotPickerPosition), Loc.T("L.Inv.Equip"), r.Name);
        CloseSlotPicker();
    }

    // ---- Détails d'un objet (clic sur une case de la grille) : nom, niveau, catégorie, effets, actions rapides ----
    [ObservableProperty] public partial InventoryItemRow? DetailItem { get; set; }
    public bool HasDetailItem => DetailItem is not null;
    partial void OnDetailItemChanged(InventoryItemRow? value) => OnPropertyChanged(nameof(HasDetailItem));

    /// <summary>Clic sur une case : affiche ses détails (un 2e clic sur la même case referme).</summary>
    [RelayCommand]
    private void ShowItemDetails(InventoryItemRow? r)
        => DetailItem = r is null || (DetailItem is { } d && d.Uid == r.Uid) ? null : r;

    [RelayCommand] private void CloseItemDetails() => DetailItem = null;

    /// <summary>Après reconstruction des lignes : le panneau suit le même exemplaire (quantité, effets à jour), ou se ferme
    /// s'il a quitté le sac.</summary>
    private void RefreshDetailItem()
    {
        if (DetailItem is not { } d) return;
        DetailItem = _invFull.FirstOrDefault(r => r.Uid == d.Uid);
    }

    /// <summary>Inventaire en grille d'icônes (comme le jeu) ou en cartes ; choix mémorisé.</summary>
    [ObservableProperty] public partial bool InventoryGrid { get; set; } = AppConfig.Current.InventoryGrid;
    public bool InventoryCards => !InventoryGrid;
    partial void OnInventoryGridChanged(bool value) { OnPropertyChanged(nameof(InventoryCards)); AppConfig.Current.InventoryGrid = value; AppConfig.Save(); }
    [RelayCommand] private void ToggleInventoryGrid() => InventoryGrid = !InventoryGrid;

    [RelayCommand] private void DropItem(InventoryItemRow? r) { if (r is not null) ItemReport(Stores.Launch.DropItemUid(P, r.Uid, ItemQty), Loc.T("L.Inv.Drop"), r.Name); }

    [RelayCommand] private void DepositItem(InventoryItemRow? r) { if (r is not null) ItemReport(Stores.Launch.BankDepositUid(P, r.Uid, ItemQty), Loc.T("L.Iw.Deposit"), r.Name); }

    [RelayCommand] private void TradeItem(InventoryItemRow? r) { if (r is not null) ItemReport(Stores.Launch.TradePutUid(P, r.Uid, ItemQty), Loc.T("L.Iw.Put"), r.Name); }

    [RelayCommand]
    private async System.Threading.Tasks.Task SellItem(InventoryItemRow? r)
    {
        if (r is null) return;
        int want = InteractionLogic.ClampQty(ItemQty, r.Qty);
        var res = await Stores.Launch.NpcSellAsync(P, (int)r.Gid, want, default).ConfigureAwait(true);
        ItemReport(res.Ok, Loc.T("L.Iw.Sell"), $"{res.Done}× {r.Name}");
    }

    // Destruction : irréversible -> confirmation explicite dans un bandeau (rien n'est envoyé avant « Confirmer »).
    [ObservableProperty] public partial InventoryItemRow? PendingDestroy { get; set; }
    public bool HasPendingDestroy => PendingDestroy is not null;
    public string PendingDestroyText => PendingDestroy is { } r
        ? Loc.Fmt("L.Inv.DestroyConfirmText", InteractionLogic.ClampQty(ItemQty, r.Qty), r.Name) : "";
    partial void OnPendingDestroyChanged(InventoryItemRow? value) { OnPropertyChanged(nameof(HasPendingDestroy)); OnPropertyChanged(nameof(PendingDestroyText)); }
    partial void OnItemActionQtyChanged(int value) => OnPropertyChanged(nameof(PendingDestroyText));
    partial void OnItemActionAllChanged(bool value) => OnPropertyChanged(nameof(PendingDestroyText));

    [RelayCommand] private void AskDestroy(InventoryItemRow? r) { if (r is { IsEquipped: false }) PendingDestroy = r; }
    [RelayCommand] private void CancelDestroy() => PendingDestroy = null;
    [RelayCommand]
    private void ConfirmDestroy()
    {
        if (PendingDestroy is not { } r) return;
        PendingDestroy = null;
        ItemReport(Stores.Launch.DeleteItemUid(P, r.Uid, ItemQty), Loc.T("L.Inv.Destroy"), r.Name);
    }
}

// ===================== Mule (objets gardés) et panoplies, par perso =====================
public partial class BotRowViewModel
{
    private string CharKey => string.IsNullOrWhiteSpace(Row.CharacterName) ? "?" : Row.CharacterName;

    /// <summary>Gids jamais donnés à la mule pour ce perso (réglages persistés).</summary>
    public IReadOnlyCollection<long> MuleKeep => AppConfig.Current.MuleKeep.TryGetValue(CharKey, out var l) ? l : Array.Empty<long>();
    public bool IsMuleKept(long gid) => MuleKeep.Contains(gid);

    [RelayCommand]
    private void ToggleMuleKeep(InventoryItemRow? r)
    {
        if (r is null) return;
        var map = AppConfig.Current.MuleKeep;
        if (!map.TryGetValue(CharKey, out var list)) map[CharKey] = list = new List<long>();
        if (!list.Remove(r.Gid)) list.Add(r.Gid);
        AppConfig.Save();
        ItemStatus = Loc.Fmt(list.Contains(r.Gid) ? "L.Mule.KeptOn" : "L.Mule.KeptOff", r.Name);
        _invSig = -1;   // cadenas à jour au prochain tick
    }

    // ---- panoplies ----
    public ObservableCollection<string> GearSetNames { get; } = new();
    [ObservableProperty] public partial string? SelectedGearSet { get; set; }
    [ObservableProperty] public partial string GearPreviewText { get; set; } = "";
    partial void OnSelectedGearSetChanged(string? value) => GearPreviewText = "";
    [ObservableProperty] public partial string NewGearSetName { get; set; } = "";
    [ObservableProperty] public partial bool GearBusy { get; set; }
    public bool HasGearSets => GearSetNames.Count > 0;

    private List<string> GearStore()
    {
        var map = AppConfig.Current.GearSets;
        if (!map.TryGetValue(CharKey, out var list)) map[CharKey] = list = new List<string>();
        return list;
    }

    private GearSet? FindGearSet(string? name)
        => name is null ? null : GearStore().Select(GearSetCodec.Parse).FirstOrDefault(g => g is not null && g.Name == name);

    /// <summary>Prévisualise les différences de GID et les objets manquants, sans envoyer de commande.
    /// Les presets ne conservent pas les jets des exemplaires, donc les gains de statistiques restent inconnus.</summary>
    [RelayCommand]
    private void PreviewGearSet()
    {
        if (Pid <= 0 || FindGearSet(SelectedGearSet) is not { } set)
        {
            GearPreviewText = "Connecte un personnage et sélectionne une panoplie pour la comparer.";
            return;
        }
        uint pid = (uint)Pid;
        var inventory = Stores.Launch.Inventory;
        var before = new List<GivreClone.App.Services.Equipment.GearItemSnapshot>();
        foreach (var (position, _) in set.Items)
        {
            if (inventory.ItemAt(pid, position) is { Gid: > 0 } worn)
                before.Add(new(position, worn.Gid, worn.Uid));
        }
        var after = set.Items.Select(i => new GivreClone.App.Services.Equipment.GearItemSnapshot(i.Position, i.Gid)).ToArray();
        var result = GivreClone.App.Services.Equipment.GearComparison.Compare(
            new("Portée", before), new(set.Name, after));
        int changes = result.Slots.Count(s => s.After is not null && s.Before?.ItemGid != s.After.ItemGid);
        var missing = set.Items.Where(i => inventory.ItemAt(pid, i.Position)?.Gid != i.Gid &&
            inventory.FirstBagUidByGid(pid, i.Gid) is null)
            .Select(i => Stores.Launch.ItemDisplayName(i.Gid)).Distinct().ToArray();
        GearPreviewText = $"{changes} emplacement(s) à modifier. " +
            (missing.Length == 0 ? "Objets nécessaires présents dans le sac." :
                $"Objets manquants : {string.Join(", ", missing)}. ") +
            "Les jets/statistiques du preset ne sont pas enregistrés : aucun gain chiffré n'est inféré.";
    }

    /// <summary>Relit les panoplies du perso (sélection du bot, nom résolu).</summary>
    public void RefreshGearSets()
    {
        var names = GearStore().Select(GearSetCodec.Parse).Where(g => g is not null).Select(g => g!.Name).ToList();
        if (names.SequenceEqual(GearSetNames)) return;
        var keep = SelectedGearSet;
        GearSetNames.Clear();
        foreach (var n in names) GearSetNames.Add(n);
        SelectedGearSet = keep is not null && names.Contains(keep) ? keep : names.FirstOrDefault();
        OnPropertyChanged(nameof(HasGearSets));
    }

    /// <summary>Enregistre l'équipement porté sous un nom (remplace une panoplie du même nom).</summary>
    [RelayCommand]
    private void SaveGearSet()
    {
        if (Pid <= 0) return;
        string name = string.IsNullOrWhiteSpace(NewGearSetName) ? Loc.Fmt("L.Gear.DefaultName", GearStore().Count + 1) : NewGearSetName.Trim().Replace("|", "/");
        var worn = Stores.Launch.Inventory.Equipped((uint)Pid).Select(i => (i.Position, i.Gid)).ToList();
        if (worn.Count == 0) { ItemStatus = Loc.T("L.Gear.NothingWorn"); return; }
        var list = GearStore();
        list.RemoveAll(e => GearSetCodec.Parse(e)?.Name == name);
        list.Add(GearSetCodec.Format(new GearSet(name, worn)));
        AppConfig.Save();
        RefreshGearSets();
        SelectedGearSet = name;
        NewGearSetName = "";
        ItemStatus = Loc.Fmt("L.Gear.Saved", name, worn.Count);
    }

    [RelayCommand]
    private void DeleteGearSet()
    {
        if (SelectedGearSet is not { } name) return;
        GearStore().RemoveAll(e => GearSetCodec.Parse(e)?.Name == name);
        AppConfig.Save();
        RefreshGearSets();
        ItemStatus = Loc.Fmt("L.Gear.Deleted", name);
    }

    /// <summary>Équipe la panoplie choisie, emplacement par emplacement (le jeu échange avec l'objet déjà porté). Les objets absents
    /// du sac sont signalés ; chaque pose attend la confirmation du serveur (ou 2 s) avant la suivante.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task EquipGearSet()
    {
        if (GearBusy || Pid <= 0 || FindGearSet(SelectedGearSet) is not { } set) return;
        uint pid = (uint)Pid;
        var l = Stores.Launch;
        if (l.IsInFight(pid)) { ItemStatus = Loc.T("L.Gear.InFight"); return; }
        GearBusy = true;
        int done = 0;
        bool confirmationUnknown = false;
        var missing = new List<string>();
        try
        {
            foreach (var (pos, gid) in set.Items.OrderBy(i => i.Position))
            {
                if (l.Inventory.ItemAt(pid, pos) is { } there && there.Gid == gid) { done++; continue; }
                if (l.Inventory.FirstBagUidByGid(pid, gid) is not int uid) { missing.Add(l.ItemDisplayName(gid)); continue; }
                if (!l.SendObjectSetPosition(pid, uid, pos)) { missing.Add(l.ItemDisplayName(gid)); continue; }
                long until = Environment.TickCount64 + 2000;
                while (Environment.TickCount64 < until && l.Inventory.ItemAt(pid, pos)?.Gid != gid)
                    await System.Threading.Tasks.Task.Delay(100);
                if (l.Inventory.ItemAt(pid, pos)?.Gid == gid) done++;
                else
                {
                    // The server may still apply this equip operation after the
                    // local timeout. Do not chain further equipment changes
                    // against an unconfirmed inventory snapshot.
                    missing.Add(l.ItemDisplayName(gid));
                    confirmationUnknown = true;
                    break;
                }
                await System.Threading.Tasks.Task.Delay(Random.Shared.Next(250, 500));   // rythme humain
            }
        }
        finally { GearBusy = false; _invSig = -1; }
        ItemStatus = confirmationUnknown
            ? "Équipement interrompu : une pose envoyée n'a pas été confirmée. Vérifiez la panoplie dans le jeu avant de reprendre."
            : missing.Count == 0 ? Loc.Fmt("L.Gear.Equipped", set.Name, done)
                                 : Loc.Fmt("L.Gear.Partial", set.Name, done, set.Items.Count, string.Join(", ", missing.Distinct()));
    }
}
