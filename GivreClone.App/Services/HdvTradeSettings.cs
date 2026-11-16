using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services;

/// <summary>Réglages HDV achat / relist / reprice / scan d'UN personnage, persistés avec <see cref="HdvProfile"/> (MZP23).
/// Noms = membres de l'original v1.3.1 ; valeurs par défaut choisies ici (non observées).</summary>
public sealed class HdvTradeSettings
{
    public List<HdvBuyRule> BuyRules { get; set; } = new();
    public List<HdvRelistRule> RelistRules { get; set; } = new();
    public long BuyBudget { get; set; } = 0;
    public bool BuyDeductStock { get; set; } = true;
    public int BuyDelayMinMs { get; set; } = 800;
    public int BuyDelayMaxMs { get; set; } = 1800;
    public bool BuyIgnorePrice { get; set; } = false;
    public int BuyModeIndex { get; set; } = 0;
    public int BuyPricePct { get; set; } = 100;
    public int BuyRecipeMult { get; set; } = 1;
    public int BuyRetrySec { get; set; } = 30;
    public int BuyStrategyIndex { get; set; } = 0;
    public int BuySwitchEvery { get; set; } = 0;
    public bool BuyT1 { get; set; } = true;
    public bool BuyT10 { get; set; } = true;
    public bool BuyT100 { get; set; } = true;
    public bool BuyT1000 { get; set; } = false;
    // Seuils « lot de N s'il en manque au moins X » (X = N : jamais de surplus ; plus bas : surplus accepté).
    public int BuyMin1 { get; set; } = 1;
    public int BuyMin10 { get; set; } = 10;
    public int BuyMin100 { get; set; } = 100;
    public int BuyMin1000 { get; set; } = 1000;
    public int BuyTolerancePct { get; set; } = 10;
    /// <summary>0 = plus gros lot d'abord. Au-dessus, un lot plus petit est choisi seulement si son prix unitaire est
    /// strictement moins cher que celui du plus gros lot admissible, de plus de ce pourcentage
    /// (20 % de moins ne gagne pas contre une marge de 20 %).</summary>
    public int BuyCheaperMarginPct { get; set; } = 0;
    public int BulkDelayMinMs { get; set; } = 600;
    public int BulkDelayMaxMs { get; set; } = 1500;
    public int BulkPauseEveryMin { get; set; } = 20;
    public int BulkPauseEveryMax { get; set; } = 40;
    public int BulkPauseMinSec { get; set; } = 5;
    public int BulkPauseMaxSec { get; set; } = 15;
    public bool BulkPauses { get; set; } = true;
    public bool BulkRelist { get; set; } = false;
    public bool NotifyBuy { get; set; } = true;
    public bool NotifyNoKamas { get; set; } = true;
    public bool NotifyPodsFull { get; set; } = true;
    public bool NotifyReprice { get; set; } = false;
    public bool NotifySale { get; set; } = true;
    public int PriceProbeMinMs { get; set; } = 400;
    public int PriceProbeMaxMs { get; set; } = 900;
    public int PriceTtlMinSec { get; set; } = 20;
    public int PriceTtlMaxSec { get; set; } = 60;
    public bool RelistBulkKeep { get; set; } = false;
    public int RelistBulkPct { get; set; } = 95;
    public bool RelistEnabled { get; set; } = false;
    public bool RelistOnSale { get; set; } = true;
    public int RelistRetrySec { get; set; } = 30;
    public int RelistWakeMinMs { get; set; } = 1500;
    public int RelistWakeMaxMs { get; set; } = 4000;
    public bool RepriceEnabled { get; set; } = false;
    public int RepriceScopeIndex { get; set; } = 0;
    public int RepriceScopeCount { get; set; } = 5;
    public int ListUndercut { get; set; } = 1;
    public int LotStrategyIndex { get; set; } = 0;
    public int LotTolerancePct { get; set; } = 0;
    public int NoCompetitorIndex { get; set; } = 0;
    public bool LossPerUnit { get; set; } = false;
    public int SniperPct { get; set; } = 0;
    public int SniperWaitMinSec { get; set; } = 2;
    public int SniperWaitMaxSec { get; set; } = 6;
    public bool ScanDecompose { get; set; } = false;
    public int ScanLevelMax { get; set; } = 200;
    public int ScanLevelMin { get; set; } = 1;
    public long ScanMinGain { get; set; } = 0;
    public int ScanMinPct { get; set; } = 10;
    public int ScanModeIndex { get; set; } = 0;
    public int ScanQty { get; set; } = 1;
    public int ScanSortIndex { get; set; } = 0;

    public HdvTradeSettings Clone()
    {
        var copy = (HdvTradeSettings)MemberwiseClone();
        copy.BuyRules = (BuyRules ?? new()).Select(r => r.Clone()).ToList();
        copy.RelistRules = (RelistRules ?? new()).Select(r => r.Clone()).ToList();
        return copy;
    }
}
