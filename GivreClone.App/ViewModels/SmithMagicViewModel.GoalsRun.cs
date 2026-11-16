using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.LotM;
using GivreClone.App.Services.Eco;
using GivreClone.Engine.Recovery;

namespace GivreClone.App.ViewModels;

public partial class SmithMagicViewModel
{
    private BagPick[] VisibleProjection()
        => FilteredPicks.Select(row => new BagPick(row.ObjectUid, row.Name, row.Level, row.TypeName, row.Allowed ? null : row.BlockText)).ToArray();

    private HashSet<long> CheckedIds() => Picks.Where(row => row.IsChecked).Select(row => row.ObjectUid).ToHashSet();

    private void AddUids(IReadOnlyList<long> uids, bool crush)
    {
        foreach (var uid in uids)
        {
            var row = Picks.FirstOrDefault(item => item.ObjectUid == uid);
            if (row is null) continue;
            if (crush)
            {
                if (row.CrushReason(CrushQueue.Any(item => item.ObjectUid == uid)) is not null) continue;
                RefreshCrushProvenance();
                if (_crushOwner is not { } owner || CrushProvenance.StorageError is not null) continue;
                int available = IncludeOtherCrushItems ? row.Quantity
                    : CrushProvenance.Available(owner, uid, row.Gid, CrushQuantityOf((uint)_pid, uid));
                if (row.CrushQuantity < 1 || row.CrushQuantity > available) continue;
                _crushChoices[uid] = new(owner, row.Gid, row.CrushQuantity, IncludeOtherCrushItems);
                CrushQueue.Add(new SmithQueueRow(uid, row.Name,
                    $"Quantité sélectionnée : {row.CrushQuantity} / {row.Quantity} dans le sac"
                    + (IncludeOtherCrushItems ? " · choix explicite" : " · session"), row.Gid, row.CrushQuantity));
            }
            else
            {
                int? selected = Leveling ? SelectedJob?.JobId : null;
                int? level = row.ItemJob is int job ? JobLevelOf((uint)_pid, job) : null;
                if (row.ForgeReason(Queue.Any(item => item.ObjectUid == uid), selected, level) is not null) continue;
                var queued = new SmithQueueRow(uid, row.Name, "Quantité : " + row.Quantity.ToString(CultureInfo.CurrentCulture), row.Gid, row.Quantity);
                Queue.Add(queued);
                if (_loadedUid == 0) LoadItem(queued);
            }
        }
        foreach (var row in Picks) row.IsChecked = false;
        RefreshJobChoices();
        RefreshPickBlocks();
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanCrush));
    }

    private void ApplyPickFilter()
    {
        RefreshPickBlocks();
        var visible = BagPicker.Filter(Picks.Select(row => new BagPick(row.ObjectUid, row.Name, row.Level, row.TypeName, row.Allowed ? null : row.BlockText)).ToArray(), BagSearch)
            .Select(row => row.Uid).ToHashSet();
        var filtered = Picks.Where(row => visible.Contains(row.ObjectUid)
            && (!PickerForCrush || row.InBag && row.HasSlot && !row.IsRune && !row.IsResource
                && (IncludeOtherCrushItems || row.CrushMaximum > 0))).ToArray();
        if (filtered.SequenceEqual(FilteredPicks)) return;
        FilteredPicks.Clear();
        foreach (var row in filtered) FilteredPicks.Add(row);
    }

    private void RefreshPickBlocks()
    {
        RefreshCrushProvenance();
        int? selected = Leveling ? SelectedJob?.JobId : null;
        foreach (var row in Picks)
        {
            row.Binding = BindingOf((uint)_pid, row.ObjectUid);
            int? level = row.ItemJob is int job ? JobLevelOf((uint)_pid, job) : null;
            string? reason = PickerForCrush
                ? row.CrushReason(CrushQueue.Any(item => item.ObjectUid == row.ObjectUid))
                : row.ForgeReason(Queue.Any(item => item.ObjectUid == row.ObjectUid), selected, level);
            row.ShowCrushQuantity = PickerForCrush;
            int sessionQuantity = _crushOwner is { } owner
                ? CrushProvenance.Available(owner, row.ObjectUid, row.Gid, CrushQuantityOf((uint)_pid, row.ObjectUid)) : 0;
            row.CrushMaximum = IncludeOtherCrushItems ? row.Quantity : sessionQuantity;
            row.CrushProvenanceText = sessionQuantity > 0 ? "Session : 1 exemplaire prouvé."
                : "Provenance de session inconnue ou déjà consommée. Choix explicite uniquement.";
            if (PickerForCrush && reason is null && (row.CrushMaximum == 0 || _crushOwner is null
                || CrushProvenance.StorageError is not null))
                reason = "Provenance inconnue : sélection de session indisponible.";
            row.BlockText = reason ?? "";
            row.Allowed = reason is null;
            if (!row.Allowed) row.IsChecked = false;
        }
    }

    public void RebuildPicks()
    {
        if (_pid <= 0)
        {
            if (Picks.Count > 0) { Picks.Clear(); FilteredPicks.Clear(); _pickSignature = ""; }
            return;
        }
        var db = Stores.Launch.GameData;
        var runes = Stores.Launch.RuneItemsByGid().Keys.ToHashSet();
        var rows = Stores.Launch.Inventory.Items((uint)_pid).Where(item => item.Qty > 0)
            .OrderBy(item => item.Position == 63 ? 0 : 1).ThenBy(item => item.Uid).ToList();
        string signature = string.Join("|", rows.Select(item => item.Uid.ToString(CultureInfo.InvariantCulture) + ":" + item.Qty.ToString(CultureInfo.InvariantCulture) + ":" + item.Position.ToString(CultureInfo.InvariantCulture)));
        if (signature == _pickSignature) { ApplyPickFilter(); RefreshJobChoices(); return; }
        var checks = Picks.Where(row => row.IsChecked).ToDictionary(row => row.ObjectUid, row => row.CrushQuantity);
        _pickSignature = signature;
        Picks.Clear();
        foreach (var item in rows)
        {
            string name = item.Gid is > 0 and <= int.MaxValue ? Stores.Launch.GameNames.ItemName((int)item.Gid) ?? ("Objet #" + item.Gid.ToString(CultureInfo.InvariantCulture)) : "Objet #" + item.Gid.ToString(CultureInfo.InvariantCulture);
            int? itemLevel = ItemLevel(db, item.Gid);
            var row = new BagPickRow
            {
                ObjectUid = item.Uid, Gid = item.Gid, Name = name, Level = itemLevel ?? 0, TypeName = ItemTypeName(db, item.Gid),
                Quantity = item.Qty, InBag = item.Position == 63, IsRune = runes.Contains(item.Gid), IsResource = IsGatheredResource(db, item.Gid),
                HasSlot = HasEquipmentSlot(db, item.Gid), ItemJob = JobForItem(item.Gid), ItemLevelValue = itemLevel,
            };
            row.IsChecked = checks.ContainsKey(item.Uid);
            row.CrushQuantity = checks.GetValueOrDefault(item.Uid, 1);
            Picks.Add(row);
        }
        ApplyPickFilter();
        RefreshJobChoices();
    }

    private static string ItemTypeName(GameDb? db, long gid)
    {
        if (db?.Row("ItemData", gid.ToString(CultureInfo.InvariantCulture)) is not { } item || Positive(item, "typeId") is not int type) return "";
        if (db.Row("ItemTypeData", type.ToString(CultureInfo.InvariantCulture)) is not { } kind || !kind.TryGetValue("nameId", out var name) || name is null) return "";
        return db.Text(Convert.ToInt64(name, CultureInfo.InvariantCulture)) ?? "";
    }

    private void RefreshJobChoices()
    {
        int? selected = SelectedJob?.JobId;
        var ids = Queue.Select(row => JobForItem(row.Gid)).Where(id => id is > 0).Select(id => id!.Value)
            .Concat(Stores.Launch.JobLevels((uint)_pid).Keys.Where(id => id is 44 or 48 or 62 or 63 or 64 or 74))
            .Distinct().OrderBy(id => id).ToArray();
        string signature = string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture) + ":" + (JobLevelOf((uint)_pid, id)?.ToString(CultureInfo.InvariantCulture) ?? "?")));
        if (signature == _jobSignature && JobChoices.Count == ids.Length) return;
        _jobSignature = signature;
        JobChoices.Clear();
        foreach (var id in ids)
        {
            int? level = _pid > 0 ? JobLevelOf((uint)_pid, id) : null;
            string title = JobTitle(id);
            JobChoices.Add(new SmithJobChoice(id, level is int known ? title + " (" + known.ToString(CultureInfo.CurrentCulture) + ")" : title, level));
        }
        SelectedJob = JobChoices.FirstOrDefault(job => job.JobId == selected) ?? (JobChoices.Count == 1 ? JobChoices[0] : null);
    }

    private SmithAdmission AdmitItem(long uid, long gid, int? selectedJob)
    {
        if (!ItemBindingRules.CanForge(BindingOf((uint)_pid, uid))) return SmithAdmission.Bound;
        int? job = JobForItem(gid);
        int? level = job is int id ? JobLevelOf((uint)_pid, id) : null;
        return SmithProfessionGate.Admit(job, selectedJob, ItemLevelOf(gid), level);
    }
}
