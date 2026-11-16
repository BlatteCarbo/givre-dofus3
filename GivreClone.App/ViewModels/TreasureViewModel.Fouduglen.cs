using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GivreClone.App.ViewModels;

public partial class TreasureViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartFouduglenCommand))]
    public partial int FouduglenCount { get; set; } = 2;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartFouduglenCommand))]
    public partial int FouduglenBudget { get; set; } = 40000;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartFouduglenCommand))]
    public partial int FouduglenMaxUnitPrice { get; set; } = 10000;

    /// <summary>Lance Fouduglen sur ce pid (chasses, budget fragments, prix maximum par fragment) ; false si refusé.</summary>
    public Func<uint, int, int, int, bool>? StartFouduglenScript { get; set; }

    public bool CanStartFouduglen => _pid > 0 && _launch.CurrentMap(_pid) is not null
        && StartFouduglenScript is not null && ScriptRunning?.Invoke(_pid) != true && !Going && !Running
        && FouduglenCount is >= 1 and <= 100 && FouduglenBudget >= 0 && FouduglenMaxUnitPrice >= 0;

    partial void RefreshFouduglenActions()
    {
        OnPropertyChanged(nameof(CanStartFouduglen));
        StartFouduglenCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStartFouduglen))]
    private void StartFouduglen()
    {
        if (!CanStartFouduglen || StartFouduglenScript is not { } start) return;
        if (!start(_pid, FouduglenCount, FouduglenBudget, FouduglenMaxUnitPrice))
        {
            Status = "Fouduglen refusé (droits, niveau ou personnage indisponible).";
            RefreshFouduglenActions();
            return;
        }
        RememberLaunchedHunt(fouduglen: true);
        Status = $"Fouduglen lancé : {FouduglenCount} chasse(s), achat des fragments manquants, assemblage et combat. L'étape en cours s'affiche ici ; arrêt au premier échec.";
        RefreshFouduglenActions();
        NotifyHuntRun();
    }
}
