using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GivreClone.App.Services;

/// <summary>Mode du bot HDV : mise en vente ou achat.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HdvMode>))]
public enum HdvMode
{
    Sell, // mettre en vente les objets du sac selon une stratégie de prix
    Buy,  // acheter des objets ciblés sous un prix plafond
}

/// <summary>Stratégie de prix à la VENTE.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HdvSellStrategy>))]
public enum HdvSellStrategy
{
    Undercut, // s'aligne sur le meilleur prix du marché puis passe N kamas en dessous
    Margin,   // prix marché + N %
    Floor,    // ne jamais descendre sous (prix marché - N %)
}

/// <summary>
/// Config du bot HDV d'UN personnage (onglet HDV). Persistée par nom de perso via <see cref="HdvProfileStore"/>
/// (%APPDATA%/GivreClone/hdv.json). Modèle de config uniquement — le moteur HDV (forge + state-machines) est branché
/// séparément une fois le protocole capturé/confirmé.
/// </summary>
public sealed class HdvProfile
{
    public bool Enabled { get; set; }                         // activer le bot HDV pour ce personnage
    public HdvMode Mode { get; set; } = HdvMode.Sell;
    public long HdvMapId { get; set; }                        // HDV cible (0 = non défini)
    public string HdvName { get; set; } = "";                 // libellé du HDV cible (affichage)

    // ---- Vente ----
    public bool SellWholeBag { get; set; }                    // vendre tout ce qui est vendable dans le sac...
    public string SellGids { get; set; } = "";                // ...ou seulement ces gids (liste séparée par virgule)
    public HdvSellStrategy SellStrategy { get; set; } = HdvSellStrategy.Undercut;
    public int UndercutKamas { get; set; } = 1;               // Undercut : passer N kamas sous le meilleur prix
    public int MarginPercent { get; set; } = 10;              // Margin : +N % sur le prix marché
    public int FloorPercent { get; set; } = 10;              // Floor : plancher à prix marché - N %
    public int LotIndex { get; set; } = 3;                   // lot vendu : 0=1, 1=10, 2=100, 3=auto (le + grand possible)
    public bool AutoReprice { get; set; }                    // repricer périodiquement les lots déjà en vente
    public int RepriceIntervalMin { get; set; } = 30;        // intervalle de repricing (minutes)

    // ---- Achat ----
    public string BuyGids { get; set; } = "";                // gids à acheter (liste séparée par virgule)
    public long MaxUnitPrice { get; set; }                   // prix unitaire max (0 = non défini -> achat refusé, sécurité)
    public int BuyLotIndex { get; set; }                     // lot acheté : 0=1, 1=10, 2=100
    public long BudgetMaxSpend { get; set; }                 // plafond de dépense kamas (borne de sécurité)

    // ---- Repricing / watcher (module HDV interactif) ----
    // Délais RANDOMISÉS entre deux bornes (min..max) pour humaniser (jamais deux cycles identiques). Max<=Min -> valeur fixe = Min.
    public int RefreshRateMs { get; set; } = 5000;           // rafraîchissement périodique des prix — borne MIN (>=1000)
    public int RefreshRateMaxMs { get; set; } = 8000;        // ...borne MAX (tirage uniforme entre min et max)
    public int DelayBetweenSalesMs { get; set; } = 1000;     // throttle entre deux reprices — borne MIN (anti-flood serveur)
    public int DelayBetweenSalesMaxMs { get; set; } = 2000;  // ...borne MAX
    public List<long> IgnoreGids { get; set; } = new();      // gids exclus du reprice auto
    public long MaxTaxSpend { get; set; }                    // plafond de taxe de reprice (0 = illimité) : le watcher s'arrête au-delà
    public int RepriceCooldownSec { get; set; } = 300;       // délai minimal entre deux reprix d'un même lot (0 = aucun)
    public decimal PriceCeilingMultiplier { get; set; } = HdvSalePriceGuard.DefaultCeilingMultiplier;   // plafond anti-manipulation (× référence ; 0 = désactivé)
    public long MaxTaxPerOperation { get; set; }             // taxe maximale par mise en vente / reprix (0 = illimité)

    // ---- Mise en vente en masse (onglet HDV) : objets cochés = SellGids ----
    public int BulkLotIndex { get; set; }                     // 0 = auto (le plus grand lot possible), 1..4 = x1, x10, x100, x1000
    public int BulkMaxLotsPerItem { get; set; } = 5;          // lots mis en vente au plus, par objet et par passage

    /// <summary>Réglages achat / relist / reprice / scan (MZP23).</summary>
    public HdvTradeSettings Trade { get; set; } = new();

    /// <summary>Copie profonde (snapshot figé pour le moteur, à l'abri des éditions UI).</summary>
    public HdvProfile Clone() => new()
    {
        Enabled = Enabled, Mode = Mode, HdvMapId = HdvMapId, HdvName = HdvName,
        SellWholeBag = SellWholeBag, SellGids = SellGids, SellStrategy = SellStrategy,
        UndercutKamas = UndercutKamas, MarginPercent = MarginPercent, FloorPercent = FloorPercent,
        LotIndex = LotIndex, AutoReprice = AutoReprice, RepriceIntervalMin = RepriceIntervalMin,
        BuyGids = BuyGids, MaxUnitPrice = MaxUnitPrice, BuyLotIndex = BuyLotIndex, BudgetMaxSpend = BudgetMaxSpend,
        RefreshRateMs = RefreshRateMs, RefreshRateMaxMs = RefreshRateMaxMs,
        DelayBetweenSalesMs = DelayBetweenSalesMs, DelayBetweenSalesMaxMs = DelayBetweenSalesMaxMs,
        IgnoreGids = new List<long>(IgnoreGids), MaxTaxSpend = MaxTaxSpend,
        RepriceCooldownSec = RepriceCooldownSec, PriceCeilingMultiplier = PriceCeilingMultiplier, MaxTaxPerOperation = MaxTaxPerOperation,
        BulkLotIndex = BulkLotIndex, BulkMaxLotsPerItem = BulkMaxLotsPerItem, Trade = Trade.Clone(),
    };
}

/// <summary>Fichier persisté (%APPDATA%/GivreClone/hdv.json) : map nom de perso -> config HDV.</summary>
public sealed class HdvProfilesFile
{
    public Dictionary<string, HdvProfile> Profiles { get; set; } = new();
}
