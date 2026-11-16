using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GivreClone.App.ViewModels;

// Types de lignes de l'onglet HDV (scan, achat, relist, catalogue, craft) présents dans l'original v1.3.1 et absents du clone
// avant MZP23. Les noms de membres sont ceux de l'original (docs/MZP23_VIEWMODEL_PARITY.md) ; les règles de calcul sont dans
// HdvRowLogic et restent à confirmer en jeu.

/// <summary>Taille de lot proposée à l'achat/relist (1, 10, 100, 1000) : cochable.</summary>
public sealed partial class HdvSizePick : ObservableObject
{
    public int Size { get; init; }
    public string Label => HdvRowLogic.Lot(Size);
    [ObservableProperty] public partial bool Active { get; set; }
}

/// <summary>Objet proposé par la recherche (achat/relist) avec ses tailles de lot.</summary>
public sealed partial class HdvItemPick : ObservableObject
{
    public long Gid { get; init; }
    public string Name { get; init; } = "";
    public Bitmap? Icon { get; init; }
    public bool HasIcon => Icon is not null;
    [ObservableProperty] public partial int Owned { get; set; }
    public string OwnedText => HdvRowLogic.Kamas(Owned);
    partial void OnOwnedChanged(int value) => OnPropertyChanged(nameof(OwnedText));
    public IReadOnlyList<HdvSizePick> Sizes { get; init; } = Array.Empty<HdvSizePick>();
}

/// <summary>Objet du catalogue (suggestion de recette ou d'objet craftable).</summary>
public sealed class HdvCatalogPick
{
    public long Gid { get; init; }
    public string Name { get; init; } = "";
    public Bitmap? Icon { get; init; }
    public bool HasIcon => Icon is not null;
    public int Level { get; init; }
    public string LevelText => HdvRowLogic.Level(Level);
    public bool Craftable { get; init; }
}

/// <summary>Étape de craft d'une recette décomposée : quantité, métier, ingrédient.</summary>
public sealed class HdvCraftStep
{
    public long Gid { get; init; }
    public string Name { get; init; } = "";
    public Bitmap? Icon { get; init; }
    public bool HasIcon => Icon is not null;
    public int Count { get; init; }
    public string CountText => "x" + Count;
    public string Job { get; init; } = "";
    public string JobText => Job;
}

/// <summary>Un lot d'achat (taille + prix) : autorisé ou non, avec un maximum de prix optionnel.</summary>
public sealed partial class HdvBuyLot : ObservableObject
{
    public int Qty { get; init; }
    public string Label => HdvRowLogic.Lot(Qty);
    public string PriceText => HdvRowLogic.KamasOrNone(Price);
    [ObservableProperty] public partial long Price { get; set; }
    [ObservableProperty] public partial bool Allowed { get; set; }
    [ObservableProperty] public partial long Max { get; set; }
    public bool OverMax => HdvRowLogic.OverMax(Price, Max);
    partial void OnMaxChanged(long value) => OnPropertyChanged(nameof(OverMax));
    partial void OnPriceChanged(long value) { OnPropertyChanged(nameof(PriceText)); OnPropertyChanged(nameof(OverMax)); }
}

/// <summary>Une ligne de la liste d'achat : besoin, stock, achats déjà faits, lots autorisés, état.</summary>
public sealed partial class HdvBuyRow : ObservableObject
{
    public int TooltipPid { get; init; }
    public long Gid { get; init; }
    [ObservableProperty] public partial string Name { get; set; } = "";
    public Bitmap? Icon { get; init; }
    public bool HasIcon => Icon is not null;
    public int Level { get; init; }
    public string LevelText => HdvRowLogic.Level(Level);
    public bool Craftable { get; init; }
    public IReadOnlyList<HdvBuyLot> Lots { get; init; } = Array.Empty<HdvBuyLot>();

    [ObservableProperty] public partial int Needed { get; set; }
    [ObservableProperty] public partial int Owned { get; set; }
    [ObservableProperty] public partial int Bought { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string StateText { get; set; } = "";
    [ObservableProperty] public partial string PlanText { get; set; } = "";
    [ObservableProperty] public partial long PlanCost { get; set; }

    public string OwnedText => HdvRowLogic.Kamas(Owned);
    public string BoughtText => HdvRowLogic.Kamas(Bought);
    public int ToBuy => HdvRowLogic.ToBuy(Needed, Owned, Bought);
    public string ToBuyText => HdvRowLogic.Kamas(ToBuy);
    public string BalanceText => HdvRowLogic.SignedKamas(HdvRowLogic.Balance(Needed, Owned, Bought));
    public string PlanCostText => HdvRowLogic.KamasOrNone(PlanCost);
    public bool IsMissing => HdvRowLogic.CoverageOf(Needed, Owned, Bought) == HdvRowLogic.Coverage.Missing;
    public bool IsExact => HdvRowLogic.CoverageOf(Needed, Owned, Bought) == HdvRowLogic.Coverage.Exact;
    public bool IsSurplus => HdvRowLogic.CoverageOf(Needed, Owned, Bought) == HdvRowLogic.Coverage.Surplus;
    public bool IsCovered => !IsMissing;
    /// <summary>Cote affichée seulement tant qu'il reste quelque chose à acheter et qu'un plan existe.</summary>
    public bool ShowQuote => IsMissing && !string.IsNullOrEmpty(PlanText);

    partial void OnNeededChanged(int value) => RaiseCoverage();
    partial void OnOwnedChanged(int value) { OnPropertyChanged(nameof(OwnedText)); RaiseCoverage(); }
    partial void OnBoughtChanged(int value) { OnPropertyChanged(nameof(BoughtText)); RaiseCoverage(); }
    partial void OnPlanTextChanged(string value) => OnPropertyChanged(nameof(ShowQuote));
    partial void OnPlanCostChanged(long value) => OnPropertyChanged(nameof(PlanCostText));
    private void RaiseCoverage()
    {
        foreach (var n in new[] { nameof(ToBuy), nameof(ToBuyText), nameof(BalanceText), nameof(IsMissing), nameof(IsExact),
                                  nameof(IsSurplus), nameof(IsCovered), nameof(ShowQuote) })
            OnPropertyChanged(n);
    }
}

/// <summary>Une ligne de la liste de relist : prix minimum à ne pas passer sous, conservation du lot, ligne « à sec ».</summary>
public sealed partial class HdvRelistRow : ObservableObject
{
    public long Gid { get; init; }
    public string Name { get; init; } = "";
    public Bitmap? Icon { get; init; }
    public bool HasIcon => Icon is not null;
    /// <summary>Taille de lot remise en vente (1 / 10 / 100 / 1000), modifiable dans la carte.</summary>
    [ObservableProperty] public partial int Qty { get; set; }
    public string LotText => HdvRowLogic.Lot(Qty);
    partial void OnQtyChanged(int value) => OnPropertyChanged(nameof(LotText));
    /// <summary>Index de la taille dans <see cref="LotSizes"/> (liste déroulante de la carte).</summary>
    public int LotIndex { get => Math.Max(0, Array.IndexOf(LotSizes, Qty)); set { if (value >= 0 && value < LotSizes.Length) Qty = LotSizes[value]; OnPropertyChanged(); } }
    public static readonly int[] LotSizes = { 1, 10, 100, 1000 };
    public string[] LotChoices => new[] { "x1", "x10", "x100", "x1000" };
    /// <summary>Lots de cette taille à garder en vente.</summary>
    [ObservableProperty] public partial int Target { get; set; } = 1;
    [ObservableProperty] public partial long MinPrice { get; set; }
    [ObservableProperty] public partial bool Keep { get; set; }
    /// <summary>Plus rien à relister (aucun lot en vente ni en stock pour cet objet).</summary>
    [ObservableProperty] public partial bool IsDry { get; set; }
    [ObservableProperty] public partial string StateText { get; set; } = "";
    public bool HasState => !string.IsNullOrEmpty(StateText);
    partial void OnStateTextChanged(string value) => OnPropertyChanged(nameof(HasState));
    /// <summary>« 2 en vente · stock : 5 lot(s) » (lots de cette taille dans mes ventes, lots complets dans le sac).</summary>
    [ObservableProperty] public partial string StatsText { get; set; } = "";
    /// <summary>Aucun lot complet en stock : la prochaine vente ne sera pas remplacée.</summary>
    [ObservableProperty] public partial bool StockOut { get; set; }
}

/// <summary>Un serveur proposé au scan de marché.</summary>
public sealed class HdvScanServer
{
    public int Id { get; init; }
    public string Label { get; init; } = "";
}

/// <summary>Un filtre (type ou métier) du scan de marché.</summary>
public sealed class HdvScanFilter
{
    public int Id { get; init; }
    public string Label { get; init; } = "";
}

/// <summary>Un résultat de scan (revente ou craft) : coût, valeur de revente, gain, fiabilité du relevé.</summary>
public sealed partial class HdvScanRow : ObservableObject
{
    public long Gid { get; init; }
    public string Name { get; init; } = "";
    public Bitmap? Icon { get; init; }
    public bool HasIcon => Icon is not null;
    public long Cost { get; init; }
    public long Value { get; init; }
    public string Detail { get; init; } = "";
    /// <summary>Nombre d'objets distincts avec un prix par lot relevé en session (pas un historique de transactions).</summary>
    public int Seen { get; init; }
    /// <summary>Age en millisecondes du plus ancien relevé utilisé pour le calcul.</summary>
    public long OldestPriceAgeMs { get; init; }
    /// <summary>Le détail identifie les tailles de lots et l'origine estimative de la marge.</summary>
    public string PriceEvidenceText => $"Prix observés : {Seen} objet(s) · plus ancien {OldestPriceAgeMs / 1000}s";
    public string ReliabilityText => Suspect ? "À revérifier" : "Prix récents";
    /// <summary>Relevé jugé douteux (prix aberrant ou peu de relevés) : le gain n'est pas fiable.</summary>
    [ObservableProperty] public partial bool Suspect { get; set; }

    public long Gain => Value - Cost;
    public string CostText => HdvRowLogic.KamasOrNone(Cost);
    public string ValueText => HdvRowLogic.KamasOrNone(Value);
    public string GainText => HdvRowLogic.SignedKamas(Gain);
    public string GainPctText => HdvRowLogic.GainPct(Cost, Value);
    public bool IsProfit => Gain > 0;
    public bool IsLoss => Gain < 0;
    public bool IsDoubtfulProfit => IsProfit && Suspect;
    /// <summary>Un résultat douteux peut être revérifié sur le marché.</summary>
    public bool CanVerify => Gid > 0 && Suspect;

    partial void OnSuspectChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDoubtfulProfit));
        OnPropertyChanged(nameof(CanVerify));
        OnPropertyChanged(nameof(ReliabilityText));
    }
}
