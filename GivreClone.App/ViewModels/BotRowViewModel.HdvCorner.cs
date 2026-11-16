using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>Une ligne du coin HDV de la carte : un objet du sac avec un prix marché connu.</summary>
public sealed record HdvCornerRow(Bitmap? Icon, string Name, string QtyText, string UnitText, string TotalText, long Total);

/// <summary>
/// « Coin HDV » de l'onglet Carte : panneau repliable (ouvrir/fermer) qui valorise le sac aux prix du marché DÉJÀ connus
/// (BidHouseStore : prix min par lot, repli prix moyen) + résume mes lots en vente et mes ventes. Lecture seule : aucun
/// paquet n'est envoyé, les prix viennent des consultations HDV faites par ailleurs (onglet HDV / watcher).
/// </summary>
public partial class BotRowViewModel
{
    private const int HdvCornerMaxRows = 8;
    private string _hdvCornerSig = "";

    [ObservableProperty] public partial bool HdvCornerOpen { get; set; }
    [ObservableProperty] public partial string HdvBagValueText { get; set; } = "—";
    [ObservableProperty] public partial string HdvListingsText { get; set; } = "—";
    [ObservableProperty] public partial string HdvSalesText { get; set; } = "—";
    [ObservableProperty] public partial string HdvUnpricedText { get; set; } = "";
    [ObservableProperty] public partial int HdvListingsCount { get; set; }
    public bool HasHdvListings => HdvListingsCount > 0;
    public ObservableCollection<HdvCornerRow> HdvCornerRows { get; } = new();
    public bool HdvCornerHasRows => HdvCornerRows.Count > 0;

    [RelayCommand]
    private void ToggleHdvCorner()
    {
        HdvCornerOpen = !HdvCornerOpen;
        if (HdvCornerOpen) RefreshHdvCorner();
    }

    /// <summary>Prix unitaire connu d'un gid : prix min du lot ×1, sinon prix moyen ; -1 si aucune consultation HDV.</summary>
    internal static long HdvUnitPrice(long market, long average) => market > 0 ? market : average > 0 ? average : -1;

    [RelayCommand]
    public void RefreshHdvCorner()
    {
        if (Pid <= 0) return;
        uint pid = (uint)Pid;
        var hdv = Stores.Launch.Hdv;
        var items = Stores.Launch.Inventory.Items(pid);
        var priced = new System.Collections.Generic.List<(InventoryItemRow Row, long Unit)>();
        int unpriced = 0;
        foreach (var it in items)
        {
            long px = HdvUnitPrice(hdv.MarketFor(pid, it.Gid, 0), hdv.AverageFor(pid, it.Gid));
            if (px < 0) { unpriced++; continue; }
            priced.Add((new InventoryItemRow(it), px));
        }
        long bag = priced.Sum(p => p.Unit * p.Row.Qty);
        long sales = hdv.SalesTotal(pid);
        int listings = hdv.MyListingCount(pid);
        string sig = string.Join('|', priced.Select(p => $"{p.Row.Gid}:{p.Row.Qty}:{p.Unit}")) + $"/{unpriced}/{listings}/{sales}/{hdv.SalesCount(pid)}";
        if (sig == _hdvCornerSig) return;   // rien n'a changé : pas de reconstruction (évite le clignotement)
        _hdvCornerSig = sig;

        HdvCornerRows.Clear();
        foreach (var p in priced.OrderByDescending(p => p.Unit * p.Row.Qty).Take(HdvCornerMaxRows))
        {
            long total = p.Unit * p.Row.Qty;
            HdvCornerRows.Add(new HdvCornerRow(p.Row.Icon, p.Row.Name, p.Row.QtyText, FormatKamas(p.Unit) + " k/u", FormatKamas(total) + " k", total));
        }
        HdvBagValueText = priced.Count == 0 ? "—" : FormatKamas(bag) + " k";
        HdvListingsCount = listings;
        HdvListingsText = listings.ToString(CultureInfo.InvariantCulture);
        HdvSalesText = hdv.SalesCount(pid) == 0 ? "—" : FormatKamas(sales) + " k (" + hdv.SalesCount(pid).ToString(CultureInfo.InvariantCulture) + ")";
        HdvUnpricedText = unpriced > 0 ? unpriced.ToString(CultureInfo.InvariantCulture) : "";
        OnPropertyChanged(nameof(HdvCornerHasRows));

    }
}
