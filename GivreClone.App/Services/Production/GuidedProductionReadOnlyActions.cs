using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.ItemCraftPlanner;
using GivreClone.Protocol;

namespace GivreClone.App.Services.Production;

/// <summary>
/// Safe preflight adapter for the EXISTING ItemCraftProductionRun.Preflight
/// and GuidedProductionStockPlanner. Only reads already captured local model
/// state; all mutation/query ports are non-emitting even if called by error.
/// Identity is checked before and after every observed read and revoked
/// forever after the first failed check.
/// </summary>
public sealed class GuidedProductionReadOnlyActions :
    IItemCraftProductionActions, IItemCraftProductionStockObservation
{
    private readonly IItemCraftProductionActions _source;
    private readonly Func<bool> _current;
    private readonly Func<GuidedSupplySourceObservation?>? _supplySource;
    private int _revoked;

    public GuidedProductionReadOnlyActions(IItemCraftProductionActions source,
        Func<bool> current)
        : this(source, current, null)
    {
    }

    /// <summary>
    /// The host supplies a real source/map/decoder/inventory-revision witness.
    /// This delegate is read only and is ALWAYS fenced by the same sticky
    /// identity predicate as the existing inventory observations.
    /// </summary>
    public GuidedProductionReadOnlyActions(IItemCraftProductionActions source,
        Func<bool> current, Func<GuidedSupplySourceObservation?>? supplySource)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _supplySource = supplySource;
    }

    public bool IsRevoked => Volatile.Read(ref _revoked) != 0;
    private bool Current()
    {
        if (IsRevoked) return false;
        try
        {
            if (_current()) return !IsRevoked;
        }
        catch (Exception) { }
        Interlocked.Exchange(ref _revoked, 1);
        return false;
    }

    public bool SupportsStockObservation =>
        _source is IItemCraftProductionStockObservation
            { SupportsStockObservation: true };

    public bool TryReadSupplySource(out GuidedSupplySourceObservation snapshot)
    {
        snapshot = default!;
        if (!Current() || _supplySource is null) return false;
        try
        {
            var observed = _supplySource();
            if (!Current() || observed is not { Valid: true })
            {
                Interlocked.Exchange(ref _revoked, 1);
                return false;
            }
            snapshot = observed;
            return true;
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref _revoked, 1);
            return false;
        }
    }

    public bool TryReadChestCount(long gid, out int count)
    {
        count = 0;
        if (!Current() || gid <= 0) return false;
        try
        {
            int observed = _source.ChestCount(gid);
            if (!Current() || observed < 0)
            {
                Interlocked.Exchange(ref _revoked, 1);
                return false;
            }
            count = observed;
            return true;
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref _revoked, 1);
            return false;
        }
    }

    public int BagCount(long gid)
    {
        if (!Current() || gid <= 0) return 0;
        try
        {
            int count = _source.BagCount(gid);
            if (Current() && count >= 0) return count;
        }
        catch (Exception) { }
        Interlocked.Exchange(ref _revoked, 1);
        return 0;
    }

    public int ChestCount(long gid)
    {
        if (!Current() || gid <= 0) return 0;
        try
        {
            int count = _source.ChestCount(gid);
            if (Current() && count >= 0) return count;
        }
        catch (Exception) { }
        Interlocked.Exchange(ref _revoked, 1);
        return 0;
    }

    public bool TryReadBagCount(long gid, out int count)
    {
        count = 0;
        if (!Current() || gid <= 0) return false;
        try
        {
            if (_source is IItemCraftProductionStockObservation
                    { SupportsStockObservation: true } observed &&
                observed.TryReadBagCount(gid, out int now) && now >= 0 &&
                Current())
            {
                count = now;
                return true;
            }
        }
        catch (Exception) { }
        Interlocked.Exchange(ref _revoked, 1);
        return false;
    }

    public IReadOnlyList<MapInteractive> Interactives()
    {
        if (!Current()) return Array.Empty<MapInteractive>();
        try
        {
            var result = _source.Interactives();
            if (Current() && result is not null) return result;
        }
        catch (Exception) { }
        Interlocked.Exchange(ref _revoked, 1);
        return Array.Empty<MapInteractive>();
    }

    // All mutation and remote pricing ports are deliberately disconnected.
    // Returning false/zero cannot authorize the runner to send a request.
    public Task<bool> OpenChestAsync(CancellationToken ct) => Task.FromResult(false);
    public Task<int> WithdrawAsync(long gid, int quantity, CancellationToken ct) =>
        Task.FromResult(0);
    public Task<bool> CloseChestAsync(CancellationToken ct) => Task.FromResult(false);
    public Task<ItemCraftWorkshopReceipt> CraftAsync(int elementId,
        int skillInstanceUid, IReadOnlyList<(long Gid, int Qty)> ingredients,
        int count, CancellationToken ct) =>
        Task.FromResult(new ItemCraftWorkshopReceipt(false, 0, 0));
    public Task<long> LotPriceAsync(long gid, int quantity, CancellationToken ct) =>
        Task.FromResult(0L);
    public Task<bool> BuyAsync(long gid, int quantity, long maxPrice,
        CancellationToken ct) => Task.FromResult(false);
    public Task<bool> ListAsync(long gid, int quantity, long price,
        CancellationToken ct) => Task.FromResult(false);
}
