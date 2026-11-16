using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

// Onglet Infos : dépense manuelle des points de caractéristiques (même requête que l'auto level-up, hors combat, droit Fight).
// Deux clics : le premier arme, le second envoie (une dépense ne se reprend qu'avec une potion d'oubli).
public partial class BotsViewModel
{
    public string[] StatChoices { get; } = { "Vitalité", "Sagesse", "Force", "Intelligence", "Chance", "Agilité" };
    [ObservableProperty] public partial int StatChoiceIndex { get; set; }
    [ObservableProperty] public partial decimal? StatAmount { get; set; } = 1;
    [ObservableProperty] public partial string StatPointsText { get; set; } = "—";
    [ObservableProperty] public partial string StatSpendText { get; set; } = "";
    [ObservableProperty] public partial bool StatSpendArmed { get; set; }
    [ObservableProperty] public partial bool StatSpendBusy { get; set; }
    public string StatSpendLabel => StatSpendArmed ? "Confirmer la dépense" : "Dépenser";

    partial void OnStatSpendArmedChanged(bool value) => OnPropertyChanged(nameof(StatSpendLabel));
    partial void OnStatChoiceIndexChanged(int value) => StatSpendArmed = false;
    partial void OnStatAmountChanged(decimal? value) => StatSpendArmed = false;

    private void RefreshStatPoints()
    {
        int left = SelectedBot is { Pid: > 0 } b ? Stores.Launch.StatPointsLeft((uint)b.Pid) : -1;
        StatPointsText = left < 0 ? "inconnu (fiche serveur non reçue)" : left.ToString();
    }

    [RelayCommand]
    private async Task SpendStatPoints()
    {
        if (SelectedBot is not { Pid: > 0 } bot || StatSpendBusy) return;
        int amount = (int)Math.Clamp(StatAmount ?? 1, 1, 1000);
        string stat = StatChoices[Math.Clamp(StatChoiceIndex, 0, StatChoices.Length - 1)];
        if (!StatSpendArmed) { StatSpendArmed = true; StatSpendText = $"Clique encore pour dépenser {amount} point(s) en {stat}."; return; }
        StatSpendArmed = false; StatSpendBusy = true; StatSpendText = "Envoi…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            int? left = await Stores.Launch.UpgradeCharacteristicAsync((uint)bot.Pid, stat, amount, cts.Token);
            StatSpendText = left is { } l ? $"Dépense confirmée en {stat} (budget {amount}) : il reste {l} point(s)."
                : "Refusé : en combat, montée auto en cours, droit manquant ou pas de réponse du serveur.";
        }
        catch (Exception e) { StatSpendText = "Échec : " + e.Message; }
        finally { StatSpendBusy = false; RefreshStatPoints(); }
    }
}
