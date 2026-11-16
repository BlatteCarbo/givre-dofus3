using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    private SmithObjectiveStore? _smithObjectives;
    public SmithObjectiveStore SmithObjectives => LazyInitializer.EnsureInitialized(ref _smithObjectives,
        () => new SmithObjectiveStore(Path.Combine(MZP19LocalClone.DataRoot, "forge", "objectives.json")));

    private readonly object _smithSpendGate = new();
    private readonly AsyncLocal<SmithSpendingScope?> _smithSpending = new();
    private readonly ConcurrentDictionary<uint, SmithPendingSpend> _smithPending = new();
    private readonly ConcurrentDictionary<uint, SmithSpendingOperation> _hdvUncertainSpend = new();
    private int _smithReceiptObserver;
    private HashSet<Guid>? _smithHistoryProjected;
    private sealed record SmithPendingSpend(SmithSpendingScope Scope, SmithSpendingOperation Operation, int Uid, long Map);

    private sealed class SmithSpendingScope : IDisposable
    {
        internal readonly uint Pid;
        internal readonly Guid Objective, Attempt;
        internal readonly SmithObjectiveIdentity Identity;
        private readonly Action _restore;
        internal volatile bool Disposed;
        internal SmithSpendingScope(uint pid, Guid objective, Guid attempt, SmithObjectiveIdentity identity, Action restore)
        { Pid = pid; Objective = objective; Attempt = attempt; Identity = identity; _restore = restore; }
        public void Dispose() { Disposed = true; _restore(); }
    }

    public IDisposable BeginSmithSpending(uint pid, Guid objectiveId, Guid attemptId)
    {
        var objective = SmithObjectives.Read().SingleOrDefault(o => o.Id == objectiveId);
        if (attemptId == Guid.Empty || objective is null || !SmithIdentityMatches(pid, objective.Identity)
            || objective.NeedsReconciliation || SmithObjectives.StorageError is not null)
            throw new InvalidOperationException("Budget FM non rapproche ou identite inconnue.");
        // A durable settlement may have preceded a failed history write. Replay cash by operation ID,
        // never by sending again or changing the objective's authorization.
        ReconcileSmithHistory();
        var previous = _smithSpending.Value;
        var scope = new SmithSpendingScope(pid, objectiveId, attemptId, objective.Identity, () => _smithSpending.Value = previous);
        _smithSpending.Value = scope;
        if (Interlocked.Exchange(ref _smithReceiptObserver, 1) == 0) PacketDecoded += ObserveSmithReceipt;
        return scope;
    }

    private bool SmithIdentityMatches(uint pid, SmithObjectiveIdentity identity) =>
        ScriptAccounts.FirstOrDefault(a => pid != 0 && a.Pid == pid)?.Id == identity.AccountId && ServerIdOf(pid) == identity.ServerId
        && CharacterId(pid) == identity.CharacterId;

    private bool InSmithSpending => _smithSpending?.Value is not null;

    public void ReconcileSmithHistory()
    {
        lock (_smithSpendGate)
        {
            var projected = _smithHistoryProjected ??= new HashSet<Guid>();
            var debits = SmithObjectives.Read().SelectMany(o => o.Debits)
                .Where(d => d.Status == SmithDebitStatus.Settled && !projected.Contains(d.Id)).ToArray();
            if (debits.Length == 0) return;
            var sessions = ForgeHistory.ReadSessions().ToDictionary(s => s.Id);
            foreach (var debit in debits)
            {
                if (!sessions.TryGetValue(debit.AttemptId, out var session)) continue;
                ForgeHistory.Record(debit.AttemptId, "cash", session.LatestProgress,
                    cashExpense: debit.ActualExpense!.Value, reason: debit.Kind, operationId: debit.Id);
                if (debit.ItemUid is > 0 && debit.Quantity is > 0)
                    ForgeHistory.Record(debit.AttemptId, "purchase", session.LatestProgress,
                        itemUid: debit.ItemUid, itemGid: debit.ItemGid, quantity: debit.Quantity,
                        unitPrice: debit.Quote % debit.Quantity.Value == 0 ? debit.Quote / debit.Quantity.Value : null,
                        operationId: debit.Id);
                projected.Add(debit.Id);
            }
        }
    }

    private bool SendSmithSpend(uint pid, string kind, long quote, int uid, int quantity, long gid, long map, Func<bool> inject,
        out SmithSpendingOperation? operation)
    {
        operation = null;
        var scope = _smithSpending?.Value;
        if (scope is null) return inject();
        if (scope.Disposed || scope.Pid != pid || !SmithIdentityMatches(pid, scope.Identity) || quote < 0) return false;
        lock (_smithSpendGate)
        {
            if (scope.Disposed || !SmithIdentityMatches(pid, scope.Identity)
                || _smithPending.TryGetValue(pid, out var pending) && !pending.Operation.Finished) return false;
            return SmithSpendingOperation.TrySend(SmithObjectives, scope.Objective, scope.Attempt, scope.Identity,
                kind, quote, Kamas(pid), op =>
                {
                    _smithPending[pid] = new(scope, op, uid, map);
                    op.Settled += expense =>
                    {
                        try
                        {
                            RecordForgeCashExpense(scope.Attempt, expense, kind, op.OperationId);
                            if (uid > 0 && quantity > 0)
                                ForgeHistory.Record(scope.Attempt, "purchase", JobProgress(pid),
                                    itemUid: uid, itemGid: gid > 0 ? gid : null, quantity: quantity,
                                    unitPrice: quote % quantity == 0 ? quote / quantity : null, operationId: op.OperationId);
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                        {
                            TransportLog.Line($"[forge] debit {op.OperationId} confirme dans le budget ; projection historique impossible : {e.Message}");
                        }
                    };
                }, inject, out operation, uid > 0 ? uid : null, gid > 0 ? gid : null, quantity > 0 ? quantity : null);
        }
    }

    private void ObserveSmithReceipt(DecodedPacket packet)
    {
        lock (_smithSpendGate)
        {
            if (packet.ClientToServer || !_smithPending.TryGetValue(packet.Pid, out var pending) || pending.Uid <= 0
                || !SmithIdentityMatches(packet.Pid, pending.Scope.Identity)) return;
            var outcome = MZP26HdvBuyReceipt.Classify(packet.Name, packet.Payload, pending.Uid, FieldOf);
            if (outcome == HdvBuyReceiptOutcome.Confirmed) pending.Operation.Confirmed();
            // A refusal can race a wallet update. Keep sent refusals reserved for reconciliation.
            if (pending.Operation.Finished) RemoveSmithPending(packet.Pid, pending);
        }
    }

    private void NoteSmithWallet(uint pid, long before, long after)
    {
        if (_smithPending is null || !_smithPending.TryGetValue(pid, out var pending)
            || !SmithIdentityMatches(pid, pending.Scope.Identity)) return;
        pending.Operation.Wallet(before, after);
        if (pending.Operation.Finished) RemoveSmithPending(pid, pending);
    }

    private void NoteSmithArrival(uint pid, long map)
    {
        if (_smithPending is null || _smithSpendGate is null) return;
        lock (_smithSpendGate)
        {
            if (!_smithPending.TryGetValue(pid, out var pending) || pending.Map != map
                || !SmithIdentityMatches(pid, pending.Scope.Identity)) return;
            pending.Operation.Confirmed();
            if (pending.Operation.Finished) RemoveSmithPending(pid, pending);
        }
    }

    private void RemoveSmithPending(uint pid, SmithPendingSpend pending) =>
        ((ICollection<KeyValuePair<uint, SmithPendingSpend>>)_smithPending).Remove(new(pid, pending));
}
