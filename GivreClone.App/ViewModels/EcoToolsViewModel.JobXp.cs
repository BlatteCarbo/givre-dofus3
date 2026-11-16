using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;
using GivreClone.App.Services.LotM;

namespace GivreClone.App.ViewModels;

public sealed record EcoXpRow(long Gid, string Recipe, string Levels, string Crafts, string Xp, string Cost)
{
    public Avalonia.Media.Imaging.Bitmap? Icon { get; } = EcoService.IconOf(Gid);
    public bool HasIcon => Icon is not null;
}

// Planificateur d'XP des métiers : hors ligne, formules du jeu, recette la moins chère par XP (ou la plus rapide) à chaque niveau.
public sealed partial class EcoToolsViewModel
{
    public ObservableCollection<JobInfo> JobOptions { get; } = new();
    public ObservableCollection<EcoXpRow> XpRows { get; } = new();
    public string[] JobModes { get; } = { "La moins chère par XP", "La plus rapide" };
    [ObservableProperty] public partial JobInfo? SelectedJob { get; set; }
    [ObservableProperty] public partial decimal? JobFrom { get; set; } = 1;
    [ObservableProperty] public partial decimal? JobTo { get; set; } = 100;
    [ObservableProperty] public partial decimal? JobBonus { get; set; } = 100;
    [ObservableProperty] public partial int JobModeIndex { get; set; }
    [ObservableProperty] public partial string JobSummary { get; set; } = "—";
    [ObservableProperty] public partial string JobCostText { get; set; } = "—";
    [ObservableProperty] public partial string JobNote { get; set; } = "Choisis un métier et les niveaux de départ et d'arrivée.";
    [ObservableProperty] public partial string JobShopping { get; set; } = "";

    private void LoadJobs()
    {
        if (JobOptions.Count > 0 || Stores.Launch.GameData is not { } db) return;
        try { foreach (var j in db.Jobs().Where(j => !string.IsNullOrWhiteSpace(j.Name) && j.Id > 1)) JobOptions.Add(j); } catch { /* données du jeu illisibles */ }
    }

    [RelayCommand]
    private void ComputeJobPlan()
    {
        XpRows.Clear(); JobShopping = ""; JobSummary = JobCostText = "—";
        if (SelectedJob is not { } job) { JobNote = "Choisis d'abord un métier."; return; }
        int from = (int)(JobFrom ?? 1), to = (int)(JobTo ?? 100);
        if (from < 1 || from > 199 || to <= from || to > 200) { JobNote = "Niveaux : de 1 à 199, et le niveau visé doit dépasser le niveau actuel (200 maximum)."; return; }
        var plan = EcoService.JobPlan(CurrentPid, job.Id, from, to, JobModeIndex == 0, (int)(JobBonus ?? 100));
        if (plan is null || plan.Steps.Count == 0) { JobNote = "Aucune recette ne permet de progresser sur cette plage."; return; }
        foreach (var s in plan.Steps)
            XpRows.Add(new EcoXpRow(s.Recipe.Gid, s.Recipe.Name, $"niv. {s.FromLevel} → {s.ToLevel}", $"{s.Crafts:N0} ×", $"{s.Xp:N0} XP", s.Cost is { } c ? c.ToString("N0") + " k" : "prix ?"));
        JobSummary = $"{plan.TotalCrafts:N0} crafts · {plan.TotalXp:N0} XP · niveau {plan.FinalLevel}";
        JobCostText = plan.TotalCost is { } t ? t.ToString("N0") + " k" : "prix incomplets";
        JobNote = plan.Reached
            ? (plan.TotalCost is null ? "Estimation : certains ingrédients n'ont pas de prix (saisis-les dans « Craft ou acheter » ou ouvre l'HDV avec un bot)." : "Estimation : ingrédients achetés au prix HDV observé ou saisi à la main.")
            : "Le niveau visé n'est pas atteignable avec les recettes connues : le plan s'arrête au niveau " + plan.FinalLevel + ".";
        JobShopping = string.Join("\n", plan.Shopping.Select(x => $"{x.Quantity:N0} × {x.Name}" + (x.Cost is { } cc ? $"  ({cc:N0} k)" : "  (prix ?)")));
    }

    [RelayCommand] private void CopyJobShopping() { if (JobShopping.Length > 0) { CopyRequested?.Invoke(JobShopping); JobNote = "Liste de courses copiée."; } }
}
