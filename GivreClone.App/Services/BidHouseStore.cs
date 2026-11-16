using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services;

/// <summary>
/// État HDV (Hôtel de Vente) par pid, alimenté par les events décodés dans <see cref="LaunchService"/> :
/// - MES lots en vente (ExchangeStartedBidSellerEvent kby / InListUpdated kes / InListRemoved ken) : uid EXCHANGE → lot.
/// - Prix moyen marché par gid (ExchangeBidPriceForSellerEvent kcq).
/// ⚠️ L'uid d'un lot est l'uid EXCHANGE (≠ uid du sac) et CHANGE à chaque reprice (ken retire l'ancien + kes ré-ajoute) :
/// on ré-indexe donc systématiquement sur l'uid renvoyé par kes. Lecture seule côté UI/moteur (observabilité avant action).
/// </summary>
public sealed class BidHouseStore
{
    public sealed record BuyerOffer(int Uid, long Gid, long[] Prices, IReadOnlyList<GivreClone.Protocol.InventoryReader.ItemEffect> Effects);
    private readonly ConcurrentDictionary<(uint Pid, long Gid), BuyerOffer[]> _variants = new();
    public IReadOnlyList<BuyerOffer> BuyerOffers(uint pid, long gid)
        => _variants.TryGetValue((pid, gid), out var rows) ? rows : Array.Empty<BuyerOffer>();
    public void SeedBuyerOffers(uint pid, long gid, IEnumerable<BuyerOffer> rows)
    {
        var snapshot = rows.Where(x => x.Gid == gid && x.Uid > 0).ToArray();
        _variants[(pid, gid)] = snapshot;
        var best = snapshot.Where(x => x.Prices.Length > 0 && x.Prices[0] > 0)
            .OrderBy(x => x.Prices[0]).FirstOrDefault() ?? snapshot.FirstOrDefault();
        if (best is not null) SetOffer(pid, gid, best.Uid, best.Prices);
    }
    /// <summary>Un lot À MOI en vente : uid EXCHANGE, gid catalogue, quantité du lot, prix demandé.</summary>
    public readonly record struct Listing(int Uid, long Gid, int Qty, long Price);

    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, Listing>> _mine = new();   // pid -> (uid exchange -> lot)
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<long, long>> _avg = new();       // pid -> (gid -> prix moyen)
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<long, long[]>> _market = new();   // pid -> (gid -> prix MIN par lot [1,10,100,1000])
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<long, long>> _priceTs = new();    // pid -> (gid -> tick ms du dernier prix reçu) : fraîcheur pour le TTL du watcher
    private readonly ConcurrentDictionary<uint, long> _sales = new();                                  // pid -> total kamas encaissés (ventes lqn)
    // Offres ACHETABLES par gid (mode ACHETEUR) : uid du TYPE d'objet (BidExchangerObject.object_uid) + prix par lot
    // [1,10,100,…] (BidExchangerObject.prices). Alimenté par ExchangeTypesItemsExchangerDescriptionForUserEvent (jzn) au
    // moment d'une recherche en mode acheteur. Sert à l'achat par GID (on a besoin de l'uid du lot pour le kei d'achat).
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<long, (int Uid, long[] Prices)>> _offers = new();

    private ConcurrentDictionary<int, Listing> Mine(uint pid) => _mine.GetOrAdd(pid, static _ => new());
    private ConcurrentDictionary<long, long> Avg(uint pid) => _avg.GetOrAdd(pid, static _ => new());
    private ConcurrentDictionary<long, (int Uid, long[] Prices)> Offers(uint pid) => _offers.GetOrAdd(pid, static _ => new());

    /// <summary>Slot d'un lot: 1→0, 10→1, 100→2, 1000→3 (index dans le tableau des prix marché). ⚠️ 100→2.</summary>
    public static int SlotOf(int quantity) => quantity switch { 1 => 0, 10 => 1, 100 => 2, 1000 => 3, _ => quantity >= 1000 ? 3 : quantity >= 100 ? 2 : quantity >= 10 ? 1 : 0 };

    /// <summary>Prix MIN du marché par lot pour un gid (le lot le moins cher actuellement en vente, nos lots inclus) : [p1,p10,p100,p1000].</summary>
    public void SetMarketPrices(uint pid, long gid, long[] prices)
    {
        _market.GetOrAdd(pid, static _ => new())[gid] = prices;
        _priceTs.GetOrAdd(pid, static _ => new())[gid] = Environment.TickCount64;   // horodate la fraîcheur -> le watcher évite de re-sonder un prix récent
    }

    /// <summary>Age (ms) du dernier prix marché reçu pour un gid, ou long.MaxValue si jamais reçu. Sert au TTL du watcher (anti re-sonde).</summary>
    public long PriceAgeMs(uint pid, long gid) => _priceTs.TryGetValue(pid, out var d) && d.TryGetValue(gid, out var t) ? Environment.TickCount64 - t : long.MaxValue;

    /// <summary>Tous les gid dont un prix (moyen ou lot) est connu pour ce pid.</summary>
    public IReadOnlyCollection<long> KnownGids(uint pid)
    {
        var set = new HashSet<long>();
        if (_avg.TryGetValue(pid, out var a)) foreach (var g in a.Keys) set.Add(g);
        if (_market.TryGetValue(pid, out var m)) foreach (var g in m.Keys) set.Add(g);
        return set;
    }

    /// <summary>Tableau des prix min par lot d'un gid (vide si inconnu).</summary>
    public long[] MarketPricesFor(uint pid, long gid) => _market.TryGetValue(pid, out var d) && d.TryGetValue(gid, out var a) ? a : System.Array.Empty<long>();

    /// <summary>Prix marché MIN pour un lot précis (slot 0=1,1=10,2=100,3=1000), ou -1 si inconnu.</summary>
    public long MarketFor(uint pid, long gid, int slot) { var a = MarketPricesFor(pid, gid); return slot >= 0 && slot < a.Length ? a[slot] : -1; }

    /// <summary>Mémorise l'offre ACHETABLE d'un gid (jzn) : uid du lot + prix par lot [1,10,100,…]. Sert à l'achat par GID.</summary>
    public void SetOffer(uint pid, long gid, int uid, long[] pricesByLot) => Offers(pid)[gid] = (uid, pricesByLot ?? System.Array.Empty<long>());

    /// <summary>Offre achetable mémorisée pour un gid (uid du lot + prix par lot), ou null si aucune recherche acheteur reçue.</summary>
    public (int Uid, long[] Prices)? OfferFor(uint pid, long gid) => Offers(pid).TryGetValue(gid, out var v) ? v : null;

    /// <summary>Prix achat pour un lot précis d'un gid (slot 0=1,1=10,2=100,…) depuis l'offre jzn, ou -1 si inconnu.</summary>
    public long OfferPriceFor(uint pid, long gid, int slot)
    { var o = OfferFor(pid, gid); return o is { } v && slot >= 0 && slot < v.Prices.Length ? v.Prices[slot] : -1; }

    /// <summary>Remplace la liste de MES lots (snapshot ExchangeStartedBidSellerEvent).</summary>
    public void SeedMine(uint pid, IEnumerable<Listing> listings)
    {
        var d = new ConcurrentDictionary<int, Listing>();
        foreach (var l in listings) d[l.Uid] = l;
        _mine[pid] = d;
    }

    /// <summary>Ajoute/met à jour un lot (InListUpdated kes) — clé = uid exchange COURANT.</summary>
    public void UpsertMine(uint pid, Listing l) => Mine(pid)[l.Uid] = l;

    /// <summary>Retire un lot (InListRemoved ken).</summary>
    public void RemoveMine(uint pid, int uid) => Mine(pid).TryRemove(uid, out _);

    /// <summary>Prix moyen marché mémorisé pour un gid (ExchangeBidPriceForSellerEvent).</summary>
    public void SetAverage(uint pid, long gid, long price) => Avg(pid)[gid] = price;

    /// <summary>Prix moyen marché d'un gid, ou -1 si inconnu.</summary>
    public long AverageFor(uint pid, long gid) => Avg(pid).TryGetValue(gid, out var p) ? p : -1;

    /// <summary>MES lots en vente (copie triée par gid).</summary>
    public IReadOnlyList<Listing> MyListings(uint pid)
        => _mine.TryGetValue(pid, out var d) ? d.Values.OrderBy(l => l.Gid).ThenBy(l => l.Qty).ToList() : Array.Empty<Listing>();

    /// <summary>Nombre de lots que J'AI en vente (= emplacements HDV occupés). Sert à l'API Lua `sale:ItemsOnSale()`.</summary>
    public int MyListingCount(uint pid) => _mine.TryGetValue(pid, out var d) ? d.Count : 0;

    /// <summary>Un de mes lots par index (liste triée), ou null. Sert aux accesseurs Lua `sale:GetItem*(Index)`.</summary>
    public Listing? MyListingAt(uint pid, int index)
    {
        var l = MyListings(pid);
        return index >= 0 && index < l.Count ? l[index] : null;
    }

    /// <summary>Mon lot d'un gid+lot précis (le 1er qui matche), ou null. Sert à `sale:EditPriceByGID`.</summary>
    public Listing? MyListingByGid(uint pid, long gid, int qty)
    {
        if (!_mine.TryGetValue(pid, out var d)) return null;
        foreach (var l in d.Values) if (l.Gid == gid && l.Qty == qty) return l;
        return null;
    }

    // ---- Total des ventes encaissées (event lqn) : gains cumulés par pid ----
    /// <summary>Ajoute une vente encaissée (montant net en kamas) au cumul du pid.</summary>
    public void AddSale(uint pid, long amount)
    {
        _sales.AddOrUpdate(pid, Math.Max(0, amount), (_, cur) => cur + Math.Max(0, amount));
        _saleCount.AddOrUpdate(pid, 1, (_, n) => n + 1);
    }

    private readonly ConcurrentDictionary<uint, int> _saleCount = new();
    /// <summary>Nombre de lots vendus depuis le lancement du perso (session).</summary>
    public int SalesCount(uint pid) => _saleCount.TryGetValue(pid, out var n) ? n : 0;

    /// <summary>Un de MES lots (gid, quantité) s'est vendu : on le retire (sinon le décompte et la valeur en vente restent faux
    /// tant que l'HDV est fermé). Le premier lot correspondant ; no-op si aucun.</summary>
    public Listing? RemoveOneMineSold(uint pid, long gid, int qty)
    {
        if (!_mine.TryGetValue(pid, out var d)) return null;
        foreach (var kv in d)
            if (kv.Value.Gid == gid && kv.Value.Qty == qty && d.TryRemove(kv.Key, out var removed)) return removed;
        return null;
    }

    /// <summary>Gid de l'offre ACHETEUR mémorisée pour cet uid de lot (0 si inconnu) : un achat ne connaît que l'uid.</summary>
    public long GidForOfferUid(uint pid, int uid)
    {
        if (uid <= 0) return 0;
        foreach (var pair in _variants) if (pair.Key.Pid == pid && pair.Value.Any(x => x.Uid == uid)) return pair.Key.Gid;
        if (!_offers.TryGetValue(pid, out var d)) return 0;
        foreach (var kv in d) if (kv.Value.Uid == uid) return kv.Key;
        return 0;
    }

    /// <summary>Total kamas encaissés en vente HDV pour ce pid (session courante).</summary>
    public long SalesTotal(uint pid) => _sales.TryGetValue(pid, out var v) ? v : 0;

    /// <summary>Invalide l'offre achetable mémorisée d'un gid (à appeler AVANT une nouvelle recherche : interdit d'acheter
    /// sur un uid/prix périmé si le jzn frais n'arrive pas).</summary>
    public void ClearOffer(uint pid, long gid) { _variants.TryRemove((pid, gid), out _); if (_offers.TryGetValue(pid, out var d)) d.TryRemove(gid, out _); }

    public void Clear(uint pid) { foreach (var key in _variants.Keys) if (key.Pid == pid) _variants.TryRemove(key, out _); _mine.TryRemove(pid, out _); _avg.TryRemove(pid, out _); _market.TryRemove(pid, out _); _priceTs.TryRemove(pid, out _); _sales.TryRemove(pid, out _); _saleCount.TryRemove(pid, out _); _offers.TryRemove(pid, out _); }
}
