using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;

namespace GivreClone.App.ViewModels;

public sealed record EcoWealthRow(string Name, string QtyText, string UnitText, string TotalText, long Gid)
{
    public Avalonia.Media.Imaging.Bitmap? Icon { get; } = EcoService.IconOf(Gid);
    public bool HasIcon => Icon is not null;
}

public sealed partial class EcoToolsViewModel
{
    public ObservableCollection<EcoWealthRow> BagTop { get; } = new();
    public ObservableCollection<EcoWealthRow> BankTop { get; } = new();
    [ObservableProperty] public partial string KamasText { get; set; } = "—";
    [ObservableProperty] public partial string BagTotalText { get; set; } = "—";
    [ObservableProperty] public partial string BankTotalText { get; set; } = "—";
    [ObservableProperty] public partial string GrandTotalText { get; set; } = "—";
    [ObservableProperty] public partial string WealthNote { get; set; } = "Sélectionne un bot connecté (onglet Bots), puis actualise.";

    [RelayCommand]
    private void RefreshWealth()
    {
        BagTop.Clear(); BankTop.Clear();
        uint pid = CurrentPid;
        if (pid == 0) { KamasText = BagTotalText = BankTotalText = GrandTotalText = "—"; WealthNote = "Sélectionne un bot connecté (onglet Bots), puis actualise."; return; }
        var l = Stores.Launch;
        decimal? Price(long g) => EcoService.PriceFor(pid, g)?.Unit;
        var bag = Wealth.Compute(l.Inventory.Items(pid).Select(i => (i.Gid, NameOf(i.Gid), (long)i.Qty)), Price);
        var bank = Wealth.Compute(l.Bank.Items(pid).Select(i => (i.Gid, NameOf(i.Gid), (long)i.Qty)), Price);
        long kamas = l.Kamas(pid), bankKamas = l.BankKamas(pid);
        KamasText = (kamas >= 0 ? kamas.ToString("N0") : "?") + " (sac)  ·  " + (bankKamas >= 0 ? bankKamas.ToString("N0") : "?") + " (banque)";
        BagTotalText = bag.Total.ToString("N0") + " k";
        BankTotalText = bank.Total.ToString("N0") + " k";
        decimal grand = bag.Total + bank.Total + Math.Max(0, kamas) + Math.Max(0, bankKamas);
        GrandTotalText = grand.ToString("N0") + " k";
        static EcoWealthRow Row(WealthItem w) => new(w.Name, "×" + w.Quantity.ToString("N0"), w.Unit is { } u ? u.ToString("N0") : "—", w.Total is { } t ? t.ToString("N0") : "—", w.Gid);
        foreach (var w in bag.Top) BagTop.Add(Row(w));
        foreach (var w in bank.Top) BankTop.Add(Row(w));
        WealthNote = $"Sac : {bag.Priced} objet(s) valorisé(s), {bag.Unpriced} sans prix. Banque : {bank.Priced} valorisé(s), {bank.Unpriced} sans prix (la banque doit avoir été ouverte). " +
                     "Estimation au prix HDV observé ou saisi : un objet peu échangé peut valoir moins.";
    }
}
