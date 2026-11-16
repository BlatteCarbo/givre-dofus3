using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

/// <summary>A single durable send, requiring both its result and an attributable wallet debit.</summary>
public sealed class SmithSpendingOperation
{
    private readonly object _gate = new();
    private readonly SmithObjectiveStore _store;
    private readonly Guid _objectiveId;
    private readonly SmithObjectiveDebit _debit;
    private readonly long _wallet;
    private bool _confirmed, _walletSeen, _ambiguous, _finished;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<bool> Completion => _completion.Task;
    public event Action<long>? Settled;
    public Guid OperationId => _debit.Id;
    public bool Finished { get { lock (_gate) return _finished; } }

    private SmithSpendingOperation(SmithObjectiveStore store, Guid objectiveId, SmithObjectiveDebit debit, long wallet)
    { _store = store; _objectiveId = objectiveId; _debit = debit; _wallet = wallet; }

    // This is the send boundary used by production, not a simulation of its budget check.
    public static bool TrySend(SmithObjectiveStore store, Guid objectiveId, Guid attemptId,
        SmithObjectiveIdentity identity, string kind, long quote, long wallet,
        Action<SmithSpendingOperation> observeBeforeSend, Func<bool> inject, out SmithSpendingOperation? operation,
        int? itemUid = null, long? itemGid = null, int? quantity = null)
    {
        operation = null;
        if (store.StorageError is not null || store.Read().SingleOrDefault(o => o.Id == objectiveId)?.Identity != identity)
            return false;
        try
        {
            var debit = store.Reserve(objectiveId, attemptId, kind, quote, wallet, itemUid, itemGid, quantity);
            if (debit is null) return false;
            operation = new(store, objectiveId, debit, wallet);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return false; }
        observeBeforeSend(operation);
        // An exception during injection is uncertain, never evidence of non-send.
        bool sent = inject();
        if (!sent) operation.Refused();
        return sent;
    }

    public void Wallet(long before, long after)
    {
        lock (_gate)
        {
            if (_finished || before == after || _debit.Quote == 0) return;
            if (!_walletSeen && !_ambiguous && before == _wallet && after == _wallet - _debit.Quote)
                _walletSeen = true;
            else _ambiguous = true;
            Reconcile();
        }
    }

    public void Confirmed()
    {
        lock (_gate) { _confirmed = true; Reconcile(); }
    }

    public void Refused()
    {
        lock (_gate)
        {
            if (_finished || _walletSeen || _ambiguous || _confirmed) return;
            try { _store.ConfirmNotSent(_objectiveId, _debit.Id); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { return; }
            _finished = true;
            _completion.TrySetResult(false);
        }
    }

    private void Reconcile()
    {
        if (_finished || _ambiguous || !_confirmed || !_walletSeen && _debit.Quote != 0) return;
        try { _store.Settle(_objectiveId, _debit.Id, _debit.Quote); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { return; }
        _finished = true;
        try { Settled?.Invoke(_debit.Quote); }
        finally { _completion.TrySetResult(true); }
    }
}
