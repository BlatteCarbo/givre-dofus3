using System;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Bak;

namespace GivreClone.App.ViewModels;

/// <summary>
/// ViewModel pour l'interface de la Bourse aux Kamas (BAK), du suivi des taux d'Ogrines
/// et de l'auto-abonnement autonome (1 Clic ou automatique sous seuil de rentabilité).
/// </summary>
public partial class BakViewModel : ViewModelBase
{
    private string _currentChar = "";
    private uint _pid;
    private int _serverId;
    private string _serverName = "";

    [ObservableProperty] public partial int CurrentRate { get; set; } = BakSubscriptionService.DefaultRate;
    [ObservableProperty] public partial int AverageRate { get; set; } = BakSubscriptionService.DefaultRate;
    [ObservableProperty] public partial long Kamas7Days { get; set; } = BakSubscriptionService.DefaultRate * 1400;
    [ObservableProperty] public partial long Kamas30Days { get; set; } = BakSubscriptionService.DefaultRate * 4000;
    [ObservableProperty] public partial string ProfitabilityText { get; set; } = "🔥 TRÈS RENTABLE (Acheter maintenant)";
    [ObservableProperty] public partial IBrush ProfitabilityBrush { get; set; } = Brushes.MediumSeaGreen;
    [ObservableProperty] public partial string SubscriptionStatusText { get; set; } = "En attente du serveur";
    [ObservableProperty] public partial string ExpirationDateText { get; set; } = "—";
    [ObservableProperty] public partial bool AutoRenewEnabled { get; set; } = true;
    [ObservableProperty] public partial int MaxAcceptableRate { get; set; } = 650;
    [ObservableProperty] public partial string LastActionMessage { get; set; } = "Prêt. Taux de l'Ogrine synchronisé.";

    public BakViewModel()
    {
        RefreshRates();
    }

    public void AttachCharacter(string characterName, uint pid = 0, int serverId = 0, string serverName = "")
    {
        _currentChar = characterName ?? "";
        _pid = pid;
        _serverId = serverId;
        _serverName = serverName ?? "";
        RefreshRates();
        RefreshSubscription();
    }

    public void RefreshRates()
    {
        CurrentRate = BakSubscriptionService.GetRateForServer(_serverId);
        AverageRate = BakSubscriptionService.GetAverageHistoricalRate(_serverId);
        Kamas7Days = (long)CurrentRate * BakSubscriptionService.Ogrines7Days;
        Kamas30Days = (long)CurrentRate * BakSubscriptionService.Ogrines30Days;

        var prof = BakSubscriptionService.EvaluateProfitability(CurrentRate, _serverId);
        switch (prof)
        {
            case BakProfitability.VeryProfitable:
                ProfitabilityText = "🔥 TRÈS RENTABLE — Cours bas, excellente opportunité !";
                ProfitabilityBrush = Brushes.MediumSeaGreen;
                break;
            case BakProfitability.Normal:
                ProfitabilityText = "⚖️ TAUX NORMAL — Cours équilibré du marché";
                ProfitabilityBrush = Brushes.CornflowerBlue;
                break;
            case BakProfitability.Expensive:
                ProfitabilityText = "⚠️ TAUX ÉLEVÉ — Cher en kamas, attendre si possible";
                ProfitabilityBrush = Brushes.OrangeRed;
                break;
        }
    }

    public void RefreshSubscription()
    {
        DateTime? subEnd = _pid > 0 ? Stores.Launch.SubscriptionEndUtc(_pid) : null;
        if (subEnd is { } exp && exp > DateTime.UtcNow)
        {
            var rem = exp - DateTime.UtcNow;
            SubscriptionStatusText = $"Abonné ({rem.Days}j {rem.Hours}h {rem.Minutes}m restantes)";
            ExpirationDateText = exp.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        }
        else if (subEnd is { } expPast && expPast <= DateTime.UtcNow)
        {
            SubscriptionStatusText = "Abonnement Expiré";
            ExpirationDateText = expPast.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        }
        else
        {
            var (rem, expFallback, active) = BakSubscriptionService.GetSubscriptionStatus(_currentChar);
            if (active)
            {
                SubscriptionStatusText = $"Abonné ({rem.Days}j {rem.Hours}h {rem.Minutes}m restantes)";
                ExpirationDateText = expFallback.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            }
            else
            {
                SubscriptionStatusText = "Abonnement non détecté / Expiré";
                ExpirationDateText = "En attente du serveur";
            }
        }
    }

    [RelayCommand]
    public async Task Buy7DaysAsync()
    {
        if (_pid == 0)
        {
            LastActionMessage = "Sélectionne d'abord un bot connecté en jeu.";
            return;
        }
        LastActionMessage = $"Souscription de 7 jours ({BakSubscriptionService.Ogrines7Days} Ogrines à {CurrentRate} k/u = {Kamas7Days:N0} k)...";
        var (_, msg, _) = await Stores.Launch.ExecuteBakPurchaseAsync(_pid, BakSubscriptionService.Ogrines7Days, MaxAcceptableRate).ConfigureAwait(true);
        LastActionMessage = msg;
        RefreshRates();
        RefreshSubscription();
    }

    [RelayCommand]
    public async Task Buy30DaysAsync()
    {
        if (_pid == 0)
        {
            LastActionMessage = "Sélectionne d'abord un bot connecté en jeu.";
            return;
        }
        LastActionMessage = $"Souscription de 30 jours ({BakSubscriptionService.Ogrines30Days} Ogrines à {CurrentRate} k/u = {Kamas30Days:N0} k)...";
        var (_, msg, _) = await Stores.Launch.ExecuteBakPurchaseAsync(_pid, BakSubscriptionService.Ogrines30Days, MaxAcceptableRate).ConfigureAwait(true);
        LastActionMessage = msg;
        RefreshRates();
        RefreshSubscription();
    }

    [RelayCommand]
    public void RefreshRate()
    {
        RefreshRates();
        RefreshSubscription();
        LastActionMessage = $"Taux actualisé : {CurrentRate} kamas / Ogrine.";
    }
}
