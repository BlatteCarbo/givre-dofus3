using System;
using System.Collections.Concurrent;
using System.Linq;

namespace GivreClone.App.Services;

/// <summary>Résumé HDV d'un bot pour la télémétrie du panel web (canal « bots », champ « hdv »).</summary>
/// <param name="State">selling | buying | idle.</param>
/// <param name="Auto">Mise en vente / reprix AUTOMATIQUE en cours (surveillance ou mise en vente en masse).</param>
/// <param name="SlotsMax">Emplacements de vente du compte ; -1 = inconnu (HDV vendeur pas encore ouvert).</param>
/// <param name="LastActionAgoS">Secondes depuis la dernière action ; -1 = aucune.</param>
public sealed record HdvStatus(string State, bool Auto, int Listings, int SlotsMax, long Value, int SoldCount, long SoldKamas,
    string? LastAction, long LastActionAgoS);

public partial class LaunchService
{
    // Dernière action HDV par pid (list | reprice | remove | buy | sold), posée aux points de passage communs à l'onglet HDV et
    // au Lua (sale:) : la télémétrie voit tous les bots, pas seulement celui sélectionné dans l'onglet.
    private readonly ConcurrentDictionary<uint, (string Action, long Tick)> _hdvLastAction = new();
    private readonly ConcurrentDictionary<uint, bool> _hdvAuto = new();

    private void NoteHdvAction(uint pid, string action) => _hdvLastAction[pid] = (action, Environment.TickCount64);

    /// <summary>L'onglet HDV signale une automatisation en cours (surveillance / reprix, mise en vente en masse) pour ce bot.</summary>
    public void SetHdvAuto(uint pid, bool on)
    {
        if (on) _hdvAuto[pid] = true; else _hdvAuto.TryRemove(pid, out _);
    }

    public bool HdvAutoActive(uint pid) => _hdvAuto.ContainsKey(pid);

    // Onglet HDV ouvert : toute variation du sac (ajout, quantité, retrait, équipement) rafraîchit sa liste. Avant, seul
    // l'instantané complet le faisait : une pile vendue restait affichée jusqu'au prochain évènement HDV.
    private void NotifyHdvInventory(uint pid)
    {
        if (HdvAnyOpen(pid)) HdvUpdated?.Invoke(pid);
    }

    private void ForgetHdv(uint pid)
    {
        ClearHdvMergedInventory(pid);
        Hdv.Clear(pid);
        // A PID can be reused by Windows. A new character must never inherit
        // unresolved transactions or an error message from its predecessor.
        _hdvSellNeedsReview.TryRemove(pid, out _);
        _hdvSellError.TryRemove(pid, out _);
        _hdvRepriceNeedsReview.TryRemove(pid, out _);
        _hdvRemoveNeedsReview.TryRemove(pid, out _);
        _hdvRemoveLastDisposition.TryRemove(pid, out _);
        foreach (var key in _hdvSellLinks.Keys.Where(key => key.Pid == pid))
            _hdvSellLinks.TryRemove(key, out _);
        _hdvOpen.TryRemove(pid, out _); _hdvAnyOpen.TryRemove(pid, out _); _hdvBuyerOpen.TryRemove(pid, out _); _hdvBuyerType.TryRemove(pid, out _);
        _hdvTypes.TryRemove(pid, out _); _hdvLotSizes.TryRemove(pid, out _); _hdvTaxPct.TryRemove(pid, out _); _hdvHiddenLots.TryRemove(pid, out _);
        _hdvLastAction.TryRemove(pid, out _); _hdvAuto.TryRemove(pid, out _);
    }

    /// <summary>Résumé HDV du bot, ou null s'il n'a aucune activité HDV (rien à afficher dans le panel).</summary>
    public HdvStatus? HdvStatusOf(uint pid)
    {
        bool open = HdvAnyOpen(pid), auto = HdvAutoActive(pid);
        int listings = Hdv.MyListingCount(pid), sold = Hdv.SalesCount(pid);
        bool hasLast = _hdvLastAction.TryGetValue(pid, out var last);
        if (!open && !auto && listings == 0 && sold == 0 && !hasLast) return null;
        string state = HdvSellerOpen(pid) ? "selling" : HdvBuyerOpen(pid) ? "buying" : "idle";
        long value = Hdv.MyListings(pid).Sum(l => Math.Max(0, l.Price));
        return new HdvStatus(state, auto, listings, HdvMaxSlots(pid), value, sold, Hdv.SalesTotal(pid),
            hasLast ? last.Action : null, hasLast ? (Environment.TickCount64 - last.Tick) / 1000 : -1);
    }
}
