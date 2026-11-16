using System;
using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.App.Services;

/// <summary>
/// Tracks only server-observed full-stack removals for the exact bag UIDs
/// selected before the operation. An inject/timeout cannot mark completion.
/// </summary>
public sealed class MZP27BankDepositTracker
{
    private readonly HashSet<int> _expected;
    private readonly HashSet<int> _confirmed = new();
    private readonly object _sync = new();

    public MZP27BankDepositTracker(IEnumerable<int> expected)
        => _expected = expected.Where(x => x > 0).ToHashSet();

    public int ExpectedCount => _expected.Count;
    public int ConfirmedCount { get { lock (_sync) return _confirmed.Count; } }
    public bool IsComplete { get { lock (_sync) return _expected.Count > 0 && _confirmed.Count == _expected.Count; } }

    public bool Observe(string? name, IReadOnlyList<ProtoField> payload,
        Func<string, string, int?> fieldLookup)
    {
        var uids = MZP25BankReceipt.DepositedBagUids(name, payload, fieldLookup);
        lock (_sync)
        {
            foreach (int uid in uids)
                if (_expected.Contains(uid)) _confirmed.Add(uid);
            return _expected.Count > 0 && _confirmed.Count == _expected.Count;
        }
    }
}
