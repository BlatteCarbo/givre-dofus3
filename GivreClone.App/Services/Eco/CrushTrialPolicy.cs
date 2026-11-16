using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GivreClone.App.Services.Eco;

public sealed record CrushSelection(int Uid, long Gid, int Quantity);
public sealed record CrushTrialYield(IReadOnlyDictionary<long, int> Runes, long? KnownValue);

/// <summary>One explicitly selected stack only. A confirmed one-item trial must be displayed before
/// a separate user approval can release the remaining original quantity. No inventory-wide selection.</summary>
public sealed class CrushTrialPolicy
{
    private readonly CrushSelection _selected;
    private bool _trialRecorded;
    private bool _released;

    public CrushTrialPolicy(CrushSelection selected)
    {
        if (selected.Uid <= 0 || selected.Gid <= 0 || selected.Quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(selected));
        _selected = selected;
    }

    public CrushSelection Trial => _selected with { Quantity = 1 };
    public CrushTrialYield? MeasuredYield { get; private set; }

    // consumedQuantity must come from a confirmed inventory delta, not CrushRunReport.Crushed
    // (that report counts successful stacks, not consumed items).
    public bool RecordTrial(int uid, int consumedQuantity, bool resultConfirmed,
        IReadOnlyDictionary<long, int> runes, Func<long, long?> currentServerUnitPrice)
    {
        if (_trialRecorded) return false;
        _trialRecorded = true;
        if (!resultConfirmed || uid != _selected.Uid || consumedQuantity != 1 ||
            runes.Any(r => r.Key <= 0 || r.Value < 0)) return false;
        var snapshot = new Dictionary<long, int>(runes);
        long? value = 0;
        foreach (var (gid, quantity) in snapshot.Where(r => r.Value > 0))
        {
            long? price = currentServerUnitPrice(gid);
            if (price is null or <= 0) { value = null; break; }
            value = checked(value + checked(price * quantity));
        }
        MeasuredYield = new(new ReadOnlyDictionary<long, int>(snapshot), value);
        return true;
    }

    public CrushSelection? AuthorizeRemaining(CrushSelection current, bool userApprovedAfterYieldShown)
    {
        if (_released || MeasuredYield is null || !userApprovedAfterYieldShown ||
            current.Uid != _selected.Uid || current.Gid != _selected.Gid ||
            current.Quantity != _selected.Quantity - 1 || current.Quantity <= 0) return null;
        _released = true;
        return current;
    }
}
