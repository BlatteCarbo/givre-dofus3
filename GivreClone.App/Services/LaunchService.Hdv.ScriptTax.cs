using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

/// <summary>
/// Lua sale:* commands use the SAME durable journal and profile cap as the
/// desktop HDV. Generic LaunchService methods remain the underlying senders:
/// wrapping them again would double-charge the desktop's own reservation.
/// No successful listing/reprice callback is ever interpreted as a tax receipt.
/// </summary>
public sealed partial class LaunchService
{
    private readonly HdvTaxReservations _scriptTaxReservations = new(
        Path.Combine(MZP19LocalClone.DataRoot, "hdv", "tax-reservations-v1.journal"));

    private bool TryScriptSaleScope(uint pid, out string profile, out HdvProfile settings)
    {
        profile = "";
        settings = null!;
        if (pid == 0) return false;
        string? character = SelfCharacterInfo(pid)?.Name;
        int server = ServerIdOf(pid);
        // Never silently fall back to the unnamed/default cap while a Lua
        // script changes character or the server identity is still unknown.
        if (server <= 0 || string.IsNullOrWhiteSpace(character)) return false;
        profile = HdvProfileKey.For(character, server);
        settings = HdvProfileStore.GetScoped(character, server);
        return settings.MaxTaxSpend >= 0 && settings.MaxTaxPerOperation >= 0 &&
            settings.PriceCeilingMultiplier >= 0;
    }

    private string? HdvScriptSalePriceRefusal(uint pid, long gid, int quantity, long price)
    {
        if (gid <= 0 || gid > int.MaxValue)
            return "Vente script bloquée : GID hors plage du catalogue.";
        if (!TryScriptSaleScope(pid, out _, out HdvProfile settings))
            return "Vente script bloquée : profil HDV serveur/personnage non établi.";
        float tax = HdvTaxPercent(pid);
        if (!float.IsFinite(tax) || tax < 0 || tax > 100)
            return "Vente script bloquée : taux fiscal serveur invalide.";
        long average = AveragePrice(pid, (int)gid);
        if (average <= 0) average = Hdv.AverageFor(pid, gid);
        var prices = Hdv.PriceAgeMs(pid, gid) <= 90_000
            ? Hdv.MarketPricesFor(pid, gid) : Array.Empty<long>();
        // The view pushes limits into an in-memory PID cache, but Lua and the
        // production/relist callers may run without any HDV view attached.
        // The persisted scoped profile is therefore authoritative here.
        return HdvSalePriceGuard.Refusal(quantity, price, average, prices,
            (decimal)tax, settings.PriceCeilingMultiplier, settings.MaxTaxPerOperation);
    }

    private async Task<bool> ExecuteScriptTaxAsync(uint pid, long price,
        string orderKey, Func<CancellationToken, Task<bool>> send,
        CancellationToken ct)
    {
        if (price <= 0 || !TryScriptSaleScope(pid, out string profile, out HdvProfile settings))
        {
            TransportLog.Line($"[hdv] script pid={pid}: no verified character/server tax scope or bad price; no send.");
            return false;
        }
        long cap = settings.MaxTaxSpend;
        long maxPerOrder = settings.MaxTaxPerOperation;
        decimal ceiling = settings.PriceCeilingMultiplier;
        var pinned = new HdvScriptTaxScope(pid, profile, cap, HdvTaxPercent(pid));
        // cap=0 retains unlimited monetary spend, but the common flow still
        // blocks replay against an earlier capped/corrupt on-disk journal.
        var result = await HdvScriptTaxMutationBroker.ExecuteAsync(_scriptTaxReservations,
            pinned, orderKey, price, () =>
                TryScriptSaleScope(pid, out string currentProfile, out HdvProfile currentSettings) &&
                string.Equals(currentProfile, profile, StringComparison.Ordinal) &&
                currentSettings.MaxTaxSpend == cap &&
                currentSettings.MaxTaxPerOperation == maxPerOrder &&
                currentSettings.PriceCeilingMultiplier == ceiling &&
                HdvTaxPercent(pid) == pinned.TaxRate,
            send, ct).ConfigureAwait(false);
        if (!result.Allowed)
            TransportLog.Line($"[hdv] script pid={pid}: order {orderKey} blocked ({result.Decision}); no send.");
        return result.ServerResultObserved;
    }

    /// <summary>Lua GID sale with fresh UID/stock resolution before durable reservation.</summary>
    public async Task<bool> HdvScriptSellByGidAsync(uint pid, long gid,
        int quantity, long price, CancellationToken ct = default)
    {
        if (gid <= 0 || quantity <= 0 || price <= 0) return false;
        var picked = HdvMergedItems(pid)
            .Where(x => x.Item.Gid == gid && x.Item.Position == 63 &&
                x.Item.Qty >= quantity && ItemCanTrade(pid, x.Item.Uid))
            .OrderByDescending(x => x.Item.Qty)
            .Select(x => (Uid: (int?)x.Item.Uid, x.Item.Qty))
            .FirstOrDefault();
        if (picked.Uid is not int uid || HdvSellableQuantity(pid, uid) < quantity ||
            HdvScriptSalePriceRefusal(pid, gid, quantity, price) is not null) return false;
        int stock = HdvSellableQuantity(pid, uid);
        return await ExecuteScriptTaxAsync(pid, price,
            HdvTaxOrderKeys.Sell(uid, quantity, price, stock), async token =>
            {
                if (HdvScriptSalePriceRefusal(pid, gid, quantity, price) is not null ||
                    HdvSellableQuantity(pid, uid) < quantity ||
                    !HdvMergedItems(pid).Any(x => x.Item.Uid == uid &&
                        x.Item.Gid == gid && x.Item.Position == 63 &&
                        x.Item.Qty >= quantity && ItemCanTrade(pid, uid))) return false;
                return await HdvSellAsync(pid, uid, quantity, price, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
    }

    /// <summary>Lua exchange-UID repricing with server-observed seller lot preflight.</summary>
    public async Task<bool> HdvScriptRepriceAsync(uint pid, int uid,
        int quantity, long price, CancellationToken ct = default)
    {
        if (uid <= 0 || quantity <= 0 || price <= 0) return false;
        var listing = Hdv.MyListings(pid).FirstOrDefault(x => x.Uid == uid && x.Qty == quantity);
        if (listing is not { Gid: > 0 } ||
            HdvScriptSalePriceRefusal(pid, listing.Gid, quantity, price) is not null) return false;
        return await ExecuteScriptTaxAsync(pid, price,
            HdvTaxOrderKeys.Reprice(uid, quantity, price), async token =>
            {
                if (HdvScriptSalePriceRefusal(pid, listing.Gid, quantity, price) is not null ||
                    !Hdv.MyListings(pid).Any(x => x.Uid == uid &&
                    x.Gid == listing.Gid && x.Qty == quantity)) return false;
                return await HdvRepriceAsync(pid, uid, quantity, price, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
    }

    public async Task<(int Matched, int Attempted, int Succeeded)> HdvScriptRepriceEveryByGidAsync(
        uint pid, long gid, int quantity, long price, CancellationToken ct = default)
    {
        if (gid <= 0 || quantity <= 0 || price <= 0) return (0, 0, 0);
        var mine = Hdv.MyListings(pid).Where(x => x.Gid == gid && x.Qty == quantity).ToArray();
        var todo = mine.Where(x => x.Price != price).Select(x => x.Uid).ToArray();
        int success = 0;
        for (int i = 0; i < todo.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0)
            {
                int delay = ResolveRepriceDelay(pid);
                if (HdvRepricePacing.ShouldWait(delay)) await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            // A failed/uncertain order must stop the batch. No different UID
            // may proceed across a pending tax reconciliation.
            if (!await HdvScriptRepriceAsync(pid, todo[i], quantity, price, ct).ConfigureAwait(false))
                return (mine.Length, todo.Length, success);
            success++;
        }
        return (mine.Length, todo.Length, success);
    }

    /// <summary>Lua UpdateAllItems: same rules as normal repricing, shared fee ledger.</summary>
    public Task<int> HdvScriptRepriceAllAsync(uint pid, long undercut = 1,
        CancellationToken ct = default)
        => HdvRepriceAllCoreAsync(pid, undercut, ct, HdvScriptRepriceAsync,
            failureAsNegative: true);
}
