using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// ===================== HDV Tracker : classement des événements HDV objet par objet (canal "hdv", LicenseService) =====================
// Le serveur de jeu ne dit pas « reprix » : un reprix = RETRAIT de l'ancien lot (ItemRemoved, ancien uid) puis AJOUT au nouveau
// prix (ItemAdded, NOUVEL uid). Une vente HDV ouvert = retrait de l'uid exact + notification de vente. On classe donc par
// appariement dans une courte fenêtre : retrait + ajout même objet/quantité = reprice ; retrait + vente = sold ; retrait seul = remove ;
// ajout seul = list. Le « lot » suivi garde l'uid de la MISE EN VENTE d'origine à travers les reprix -> délai de vente côté tracker.
public sealed partial class LaunchService
{
    private const int HdvPairWindowMs = 2500;   // retrait <-> ajout / vente d'un même lot arrivent en < 1 s ; marge réseau

    private sealed class HdvGone { public int Uid; public long Gid; public int Qty; public long Price; public string Lot = ""; public long Tick; public bool Claimed; }
    private sealed class HdvSale { public long Gid; public int Qty; public long Amount; public string? Lot; public long Tick; }

    private readonly ConcurrentDictionary<(uint Pid, int Uid), string> _hdvLotOf = new();     // uid courant -> lot d'origine
    private readonly ConcurrentDictionary<uint, List<HdvGone>> _hdvGone = new();               // retraits en attente de classement
    private readonly ConcurrentDictionary<uint, List<HdvSale>> _hdvSalePending = new();        // ventes en attente du retrait (lot)
    private readonly ConcurrentDictionary<uint, long> _hdvAutoUntil = new();                   // action lancée par le moteur
    private readonly ConcurrentDictionary<uint, long> _hdvManualUntil = new();                 // action unitaire cliquée dans l'app
    private readonly ConcurrentDictionary<uint, (int Uid, int Qty, long Price, long Gid, long Tick)> _hdvBuyPending = new();
    private readonly ConcurrentDictionary<uint, List<(long Amount, long Tick)>> _hdvDebits = new();   // taxes pas encore rattachées

    // TAXE : HDV vendeur ouvert, la seule dépense possible est la taxe de mise en vente / de reprix. Le serveur traite les
    // requêtes une par une et débite AVANT d'annoncer le lot (capture : KamasUpdate puis InListUpdated). La taxe d'un list ou
    // d'un reprice = les débits reçus depuis l'événement HDV précédent, dans la fenêtre d'appariement. Un reprix sans débit
    // (ex : prix baissé) reçoit 0 sans décaler les suivants. Mesure par écart de kamas : couvre l'app, les scripts ET le client
    // Dofus en MITM (l'onglet HDV ne comptait que ses propres clics).
    private void HdvTrackKamas(uint pid, long before, long after)
    {
        if (before < 0 || after >= before || !HdvSellerOpen(pid)) return;
        var list = _hdvDebits.GetOrAdd(pid, static _ => new());
        lock (list) list.Add((before - after, Environment.TickCount64));
    }

    // Taxe du lot qu'on vient de classer (débits frais consommés). -1 = inconnue : kamas jamais lus pour ce bot.
    private long HdvTakeTax(uint pid)
    {
        if (Kamas(pid) < 0) return -1;
        if (!_hdvDebits.TryGetValue(pid, out var list)) return 0;
        long now = Environment.TickCount64, sum = 0;
        lock (list)
        {
            foreach (var d in list) if (now - d.Tick <= HdvPairWindowMs) sum += d.Amount;
            list.Clear();
        }
        return sum;
    }

    /// <summary>Marque la PROCHAINE action HDV de ce bot comme manuelle (clic unitaire dans l'onglet HDV), même si elle passe
    /// par le moteur. Les automatisations (reprix auto, mise en vente en masse, script) restent auto=true.</summary>
    public void MarkHdvManual(uint pid) => _hdvManualUntil[pid] = Environment.TickCount64 + 8000;

    private void MarkHdvAuto(uint pid) => _hdvAutoUntil[pid] = Environment.TickCount64 + 8000;

    // true = moteur (auto), false = manuel (client Dofus en MITM, ou clic unitaire de l'app).
    private bool HdvIsAuto(uint pid)
    {
        long now = Environment.TickCount64;
        if (_hdvManualUntil.TryGetValue(pid, out long m) && now < m) return false;
        return _hdvAutoUntil.TryGetValue(pid, out long a) && now < a;
    }

    private void EmitHdv(string kind, uint pid, long gid, int qty = 0, long price = -1, long oldPrice = -1, string? lot = null, long avg = 0, long[]? min = null, bool? auto = null, long tax = -1)
    {
        if (gid <= 0) return;
        int type = _gameNames.ItemTypeId((int)gid);
        LicenseService.ReportHdvEvent(kind, (int)pid, gid, _gameNames.ItemName((int)gid) ?? $"#{gid}", type,
                                      type > 0 ? _gameNames.ItemCategory((int)gid) : null, kind is "price" or "sold" ? null : auto ?? HdvIsAuto(pid),
                                      qty, price, oldPrice, lot, avg, min, tax);
    }

    // Ouverture vendeur : les lots déjà en vente deviennent leur propre lot d'origine (sans événement).
    private void HdvTrackSeed(uint pid, IEnumerable<BidHouseStore.Listing> listings)
    {
        foreach (var l in listings) _hdvLotOf.TryAdd((pid, l.Uid), l.Uid.ToString());
    }

    // ItemAdded (HDV vendeur ouvert) : reprix si un retrait du même objet/quantité vient d'avoir lieu, sinon mise en vente.
    private void HdvTrackAdded(uint pid, BidHouseStore.Listing added)
    {
        if (_hdvLotOf.ContainsKey((pid, added.Uid))) return;   // même uid ré-annoncé : déjà classé
        HdvGone? gone = null;
        var list = _hdvGone.GetOrAdd(pid, static _ => new());
        lock (list)
        {
            long now = Environment.TickCount64;
            gone = list.FirstOrDefault(g => !g.Claimed && g.Gid == added.Gid && g.Qty == added.Qty && now - g.Tick <= HdvPairWindowMs);
            if (gone is not null) { gone.Claimed = true; list.Remove(gone); }
        }
        long tax = HdvTakeTax(pid);
        if (gone is not null)
        {
            _hdvLotOf[(pid, added.Uid)] = gone.Lot;
            TransportLog.Line($"[hdv] pid {pid} : reprix gid={added.Gid} x{added.Qty} {gone.Price}->{added.Price}, taxe {(tax < 0 ? "?" : tax)}");
            EmitHdv("reprice", pid, added.Gid, added.Qty, added.Price, gone.Price, gone.Lot, tax: tax);   // ignoré si le prix n'a pas changé
            return;
        }
        string lot = added.Uid.ToString();
        _hdvLotOf[(pid, added.Uid)] = lot;
        TransportLog.Line($"[hdv] pid {pid} : mise en vente gid={added.Gid} x{added.Qty} à {added.Price}, taxe {(tax < 0 ? "?" : tax)}");
        EmitHdv("list", pid, added.Gid, added.Qty, added.Price, lot: lot, tax: tax);
    }

    // ItemRemoved (HDV vendeur ouvert) : appelé AVANT le retrait du store (prev = le lot tel qu'il était en vente).
    private void HdvTrackRemoved(uint pid, BidHouseStore.Listing prev)
    {
        if (prev.Uid <= 0 || prev.Gid <= 0) return;
        _hdvLotOf.TryRemove((pid, prev.Uid), out var lot);
        lot ??= prev.Uid.ToString();
        // Vente déjà notifiée (notification avant le retrait) : on lui donne son lot, pas de « remove ».
        var sales = _hdvSalePending.GetOrAdd(pid, static _ => new());
        lock (sales)
        {
            long now = Environment.TickCount64;
            var s = sales.FirstOrDefault(x => x.Lot is null && x.Gid == prev.Gid && x.Qty == prev.Qty && now - x.Tick <= HdvPairWindowMs);
            if (s is not null) { s.Lot = lot; return; }
        }
        var g = new HdvGone { Uid = prev.Uid, Gid = prev.Gid, Qty = prev.Qty, Price = prev.Price, Lot = lot, Tick = Environment.TickCount64 };
        var list = _hdvGone.GetOrAdd(pid, static _ => new());
        lock (list) list.Add(g);
        bool auto = HdvIsAuto(pid);
        _ = Task.Delay(HdvPairWindowMs).ContinueWith(_ =>
        {
            lock (list) { if (g.Claimed) return; g.Claimed = true; list.Remove(g); }
            EmitHdv("remove", pid, g.Gid, g.Qty, g.Price, lot: g.Lot, auto: auto);
        }, TaskScheduler.Default);
    }

    // Vente notifiée. HDV ouvert : le retrait de l'uid exact arrive juste avant ou juste après -> on attend la fenêtre pour
    // récupérer son lot. HDV fermé (ou vente faite hors ligne) : le lot est celui retiré du store.
    private void HdvTrackSold(uint pid, long gid, int qty, long amount, BidHouseStore.Listing? removedClosed)
    {
        if (removedClosed is { } rc)
        {
            _hdvLotOf.TryRemove((pid, rc.Uid), out var lotC);
            EmitHdv("sold", pid, gid, qty, amount, lot: lotC ?? rc.Uid.ToString(), auto: false);
            return;
        }
        HdvGone? gone = null;
        var list = _hdvGone.GetOrAdd(pid, static _ => new());
        lock (list)
        {
            long now = Environment.TickCount64;
            gone = list.FirstOrDefault(g => !g.Claimed && g.Gid == gid && g.Qty == qty && now - g.Tick <= HdvPairWindowMs);
            if (gone is not null) { gone.Claimed = true; list.Remove(gone); }
        }
        if (gone is not null) { EmitHdv("sold", pid, gid, qty, amount, lot: gone.Lot, auto: false); return; }
        if (!HdvSellerOpen(pid)) { EmitHdv("sold", pid, gid, qty, amount, auto: false); return; }
        var s = new HdvSale { Gid = gid, Qty = qty, Amount = amount, Tick = Environment.TickCount64 };
        var sales = _hdvSalePending.GetOrAdd(pid, static _ => new());
        lock (sales) sales.Add(s);
        _ = Task.Delay(HdvPairWindowMs).ContinueWith(_ =>
        {
            lock (sales) sales.Remove(s);
            EmitHdv("sold", pid, s.Gid, s.Qty, s.Amount, lot: s.Lot, auto: false);
        }, TaskScheduler.Default);
    }

    // Ventes HORS LIGNE (liste reçue à la connexion) : la liste arrive pendant le login, souvent avant que le perso soit identifié
    // côté app -> on attend son nom (90 s max) avant d'envoyer les « sold » au portail et de les inscrire au journal HDV local.
    // Les deux sont dédupliqués (id / clé déterministes) : une même liste renvoyée à une connexion suivante n'est jamais recomptée.
    private void HdvTrackOfflineSales(uint pid, List<(long Gid, int Qty, long Price, long SoldAt)> sales)
    {
        var dated = sales.Where(s => s.SoldAt > 0).ToList();
        if (dated.Count < sales.Count) TransportLog.Line($"[hdv] pid {pid} : {sales.Count - dated.Count} vente(s) hors ligne sans date ignorée(s).");
        if (dated.Count == 0) return;
        _ = Task.Run(async () =>
        {
            var rows = dated.Select(s =>
            {
                int type = _gameNames.ItemTypeId((int)s.Gid);
                return (s.Gid, Name: _gameNames.ItemName((int)s.Gid) ?? $"#{s.Gid}", TypeId: type,
                        TypeName: type > 0 ? _gameNames.ItemCategory((int)s.Gid) : null, s.Qty, s.Price, s.SoldAt);
            }).ToList();
            string? character = null;
            bool reported = false;
            for (int i = 0; i < 45 && (!reported || character is null); i++)
            {
                if (!reported) reported = LicenseService.ReportHdvOfflineSales((int)pid, rows);
                character ??= LicenseService.BotCharacterName((int)pid);
                if (!reported || character is null) await Task.Delay(2000).ConfigureAwait(false);
            }
            if (!reported) TransportLog.Line($"[hdv] pid {pid} : ventes hors ligne non transmises (perso jamais identifié).");
            if (character is null) return;
            var seen = new Dictionary<string, int>();
            var entries = rows.Select(r =>
            {
                string key = $"off:{r.Gid}:{r.Qty}:{r.Price}:{r.SoldAt}";
                int rank = seen.TryGetValue(key, out int k) ? k + 1 : 0;
                seen[key] = rank;
                return new HdvStatEntry { Ts = r.SoldAt, IsTax = false, Gid = r.Gid, Qty = r.Qty, Amount = r.Price, Name = r.Name, Key = key + ":" + rank };
            }).ToList();
            var (added, total) = HdvStatsStore.RecordOfflineSales(character, entries);
            TransportLog.Line($"[hdv] pid {pid} : ventes hors ligne -> {added}/{entries.Count} nouvelle(s) au journal HDV.");
            if (added > 0) HdvOfflineSold?.Invoke(pid, added, total);
        });
    }

    // Achat : on retient le lot visé (uid, quantité, prix) à la requête, l'événement part sur le résultat « acheté ».
    private void HdvTrackBuyRequest(uint pid, int uid, int qty, long price)
    {
        if (uid <= 0 || qty <= 0 || price <= 0) return;
        _hdvBuyPending[pid] = (uid, qty, price, Hdv.GidForOfferUid(pid, uid), Environment.TickCount64);
    }

    private void HdvTrackBuyResult(uint pid, int uid, bool bought)
    {
        if (!_hdvBuyPending.TryGetValue(pid, out var b) || Environment.TickCount64 - b.Tick > 15_000) return;
        if (uid != 0 && b.Uid != uid) return;
        _hdvBuyPending.TryRemove(pid, out _);
        if (bought && b.Gid > 0) EmitHdv("buy", pid, b.Gid, b.Qty, b.Price);
    }

    private void ForgetHdvTracking(uint pid)
    {
        foreach (var k in _hdvLotOf.Keys.Where(k => k.Pid == pid).ToList()) _hdvLotOf.TryRemove(k, out _);
        _hdvGone.TryRemove(pid, out _); _hdvSalePending.TryRemove(pid, out _); _hdvBuyPending.TryRemove(pid, out _);
        _hdvAutoUntil.TryRemove(pid, out _); _hdvManualUntil.TryRemove(pid, out _); _hdvDebits.TryRemove(pid, out _);
    }
}
