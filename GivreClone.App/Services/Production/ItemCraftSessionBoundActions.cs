using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.ItemCraftPlanner;
using GivreClone.Protocol;

namespace GivreClone.App.Services.Production;

/// <summary>
/// Wraps the production runner's ACTUAL bank, HDV and workshop ports. A
/// character/server identity mismatch blocks each request, including the
/// second action of a multi-step craft, and discards a stale result on return.
/// It is not an authenticating TCP link token or a server ACK.
/// </summary>
public sealed class ItemCraftSessionBoundActions :
    IItemCraftProductionActions, IItemCraftProductionStockObservation
{
    private readonly IItemCraftProductionActions _inner;
    private readonly Func<bool> _current;
    private readonly Func<bool> _mayMutate;
    // A lost identity observed by this adapter invalidates the whole attempt,
    // not only the particular receipt. A later matching map/character snapshot
    // cannot authorize the old Started lease or another mutation.
    private int _revoked;

    public bool IsRevoked => Volatile.Read(ref _revoked) != 0;
    public bool SupportsStockObservation =>
        _inner is IItemCraftProductionStockObservation { SupportsStockObservation: true };

    public ItemCraftSessionBoundActions(IItemCraftProductionActions inner,
        Func<bool> current, Func<bool>? mayMutate = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        // Read-only preflight runs before V9 Started. The optional distinct
        // mutation lease is checked at the *actual* action boundary, so an
        // adapter created while Ready cannot be used to bypass durable Begin.
        _mayMutate = mayMutate ?? current;
    }

    public int BagCount(long gid)
    {
        if (!Current()) return 0;
        int count = _inner.BagCount(gid);
        return Current() ? count : 0;
    }

    public int ChestCount(long gid)
    {
        if (!Current()) return 0;
        int count = _inner.ChestCount(gid);
        return Current() ? count : 0;
    }

    public bool TryReadBagCount(long gid, out int count)
    {
        count = 0;
        if (!Current() || _inner is not IItemCraftProductionStockObservation
            { SupportsStockObservation: true } observed)
            return false;
        bool ok = observed.TryReadBagCount(gid, out int result);
        if (!Current() || !ok || result < 0) return false;
        count = result;
        return true;
    }

    public IReadOnlyList<MapInteractive> Interactives()
    {
        if (!Current()) return Array.Empty<MapInteractive>();
        var result = _inner.Interactives();
        return Current() ? result : Array.Empty<MapInteractive>();
    }

    public async Task<bool> OpenChestAsync(CancellationToken ct)
        => await IfCurrent(_inner.OpenChestAsync, false, ct).ConfigureAwait(false);
    public async Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct)
        => await IfCurrent(token => _inner.WithdrawAsync(gid, quantity, token), 0, ct).ConfigureAwait(false);
    public async Task<bool> CloseChestAsync(CancellationToken ct)
        => await IfCurrent(_inner.CloseChestAsync, false, ct).ConfigureAwait(false);

    public async Task<ItemCraftWorkshopReceipt> CraftAsync(int elementId, int skillInstanceUid,
        IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        => await IfCurrent(token => _inner.CraftAsync(elementId, skillInstanceUid, ingredients, count, token),
            new ItemCraftWorkshopReceipt(false, 0, 0), ct).ConfigureAwait(false);

    public async Task<long> LotPriceAsync(long gid, int quantity, CancellationToken ct)
        => await IfCurrent(token => _inner.LotPriceAsync(gid, quantity, token), 0L, ct).ConfigureAwait(false);
    public async Task<bool> BuyAsync(long gid, int quantity, long maxPrice, CancellationToken ct)
        => await IfCurrent(token => _inner.BuyAsync(gid, quantity, maxPrice, token), false, ct).ConfigureAwait(false);
    public async Task<bool> ListAsync(long gid, int quantity, long price, CancellationToken ct)
        => await IfCurrent(token => _inner.ListAsync(gid, quantity, price, token), false, ct).ConfigureAwait(false);

    private async Task<T> IfCurrent<T>(Func<CancellationToken, Task<T>> send, T denied,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!CurrentMutation()) return denied;
        T result = await send(ct).ConfigureAwait(false);
        // A sender may ignore cancellation and return a positive receipt late.
        ct.ThrowIfCancellationRequested();
        return CurrentMutation() ? result : denied;
    }

    private bool CurrentMutation()
    {
        if (!Current()) return false;
        bool admitted;
        try { admitted = _mayMutate(); }
        catch (Exception) { admitted = false; }
        if (!admitted)
        {
            Interlocked.Exchange(ref _revoked, 1);
            return false;
        }
        return Volatile.Read(ref _revoked) == 0;
    }

    private bool Current()
    {
        if (Volatile.Read(ref _revoked) != 0) return false;
        bool sourceCurrent;
        try { sourceCurrent = _current(); }
        catch (Exception) { sourceCurrent = false; }
        if (!sourceCurrent)
        {
            Interlocked.Exchange(ref _revoked, 1);
            return false;
        }
        return Volatile.Read(ref _revoked) == 0;
    }
}
