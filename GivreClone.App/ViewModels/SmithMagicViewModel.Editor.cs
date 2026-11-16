using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;

namespace GivreClone.App.ViewModels;

public partial class SmithMagicViewModel
{
    private int _pid;
    public int Pid => _pid;
    [ObservableProperty] public partial string TargetName { get; set; } = "";
    [ObservableProperty] public partial int TargetCurrent { get; set; }
    [ObservableProperty] public partial int TargetMinimum { get; set; }
    [ObservableProperty] public partial int TargetNaturalMaximum { get; set; }

    /// <summary>Lit le sac du bot affiché (objets à forgemager). Changement de perso : file et objectifs remis à zéro.</summary>
    public void AttachPid(int pid)
    {
        if (_pid != pid)
        {
            if (Busy) Stop();
            ResetAdviceProcessIdentity();
            _pid = pid; Queue.Clear(); Lines.Clear(); BagChoices.Clear(); Runes.Clear(); _linesByItem.Clear(); _loadedUid = 0; BagIndex = -1;
            ResetConditionSteps();
            AttachAdviceIdentity(null);
            ClearCharacterGoals();
            OnPropertyChanged(nameof(CanStart));
        }
        if (pid <= 0) { OnPropertyChanged(nameof(BagHint)); return; }
        BindAdviceIdentity(itemChanged: false);
        var inventory = Stores.Launch.Inventory;
        var rows = inventory.InventoryUids((uint)pid)
            .Select(uid => inventory.Get((uint)pid, uid)).Where(item => item.HasValue)
            .Select(item => item!.Value).Where(item => item.Qty > 0 && item.Position == 63)
            .Where(item => Stores.Launch.ItemEffects((uint)pid, item.Uid).Any(e => RuneCatalog.Of(e.ActionId) is not null))   // objets à lignes runables
            .Select(item => new SmithQueueRow(item.Uid,
                item.Gid is > 0 and <= int.MaxValue ? Stores.Launch.GameNames.ItemName((int)item.Gid) ?? $"Objet #{item.Gid}" : $"Objet #{item.Gid}",
                $"Quantité : {item.Qty}", item.Gid, item.Qty))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.ObjectUid).ToArray();
        if (BagChoices.SequenceEqual(rows)) return;
        long selected = BagIndex >= 0 && BagIndex < BagChoices.Count ? BagChoices[BagIndex].ObjectUid : 0;
        BagChoices.Clear(); foreach (var item in rows) BagChoices.Add(item);
        BagIndex = Array.FindIndex(rows, item => item.ObjectUid == selected);
        OnPropertyChanged(nameof(BagHint));
        RebuildPicks();
    }

    public string BagHint => BagChoices.Count == 0
        ? "Aucun objet du sac n'a de ligne de rune lisible (effets pas encore reçus, ou objet équipé)."
        : $"{BagChoices.Count} objet(s) du sac avec une ligne de rune. Choisis-en un, puis « Ajouter à la file ».";

    /// <summary>Ajoute une ligne à la main (exo : PA, PM, Portée…). Le nom est cherché dans le catalogue de runes (« PA », « Force », « Fo »).</summary>
    [RelayCommand] private void AddTarget()
    {
        var name = TargetName.Trim();
        if (name.Length == 0 || TargetMinimum < 0 || TargetNaturalMaximum < 0)
        { Status = "Saisis une caractéristique et un objectif positif."; return; }
        var family = RuneCatalog.Families.FirstOrDefault(f => string.Equals(f.Label, name, StringComparison.OrdinalIgnoreCase) || string.Equals(f.Code, name, StringComparison.OrdinalIgnoreCase));
        if (family is null) { Status = $"« {name} » : aucune rune connue pour cette caractéristique."; return; }
        if (Lines.Any(line => line.EffectId == family.EffectId)) { Status = "Cette ligne existe déjà."; return; }
        // Maximum naturel 0 = ligne absente de l'objet : c'est un exo.
        Lines.Add(new SmithLineRow { EffectId = family.EffectId, Name = family.Label, Current = TargetCurrent, Min = TargetMinimum,
            Max = Math.Max(TargetMinimum, TargetNaturalMaximum), NaturalMaximum = TargetNaturalMaximum });
        SaveLines();
        Status = TargetNaturalMaximum == 0 ? $"Exo {family.Label} ajouté." : $"Objectif {family.Label} ajouté.";
        TargetName = "";
    }
}
