extern alias F06App;

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Store = F06App::GivreClone.App.Services.SmithObjectiveStore;
using Identity = F06App::GivreClone.App.Services.SmithObjectiveIdentity;
using Spend = F06App::GivreClone.App.Services.SmithSpendingOperation;
using History = F06App::GivreClone.App.Services.ForgeHistoryStore;

namespace GivreClone.Tests;

public sealed class SmithSpendingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smith-spend-" + Guid.NewGuid().ToString("N"));
    private static Identity Account => new("account-a", 354, 33202504034, 64);
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Theory]
    [InlineData(100, 1000, true)]
    [InlineData(101, 1000, false)]
    [InlineData(100, 999, false)]
    [InlineData(1, -1, false)]
    public void Production_send_boundary_reserves_before_injection_and_checks_exact_floor(long quote, long wallet, bool allowed)
    {
        var store = new Store();
        var objective = store.Open(Account, 60, 100, 900);
        int injections = 0;
        bool sent = Spend.TrySend(store, objective.Id, Guid.NewGuid(), Account, "purchase", quote, wallet,
            _ => { }, () =>
            {
                Assert.True(Assert.Single(store.Read()).NeedsReconciliation);
                Assert.Equal(quote, Assert.Single(store.Read()).Reserved);
                injections++;
                return true;
            }, out var operation);
        Assert.Equal(allowed, sent);
        Assert.Equal(allowed ? 1 : 0, injections);
        Assert.Equal(allowed, operation is not null);
    }

    [Fact]
    public void Failed_durable_commit_calls_neither_observer_nor_injector()
    {
        string path = Path.Combine(_root, "objectives.json");
        var store = new Store(path);
        var objective = store.Open(Account, 60, 100, 900);
        File.Delete(path);
        Directory.CreateDirectory(path);
        int injections = 0, observers = 0;
        Assert.False(Spend.TrySend(store, objective.Id, Guid.NewGuid(), Account, "purchase", 100, 1000,
            _ => observers++, () => { injections++; return true; }, out _));
        Assert.Equal(0, injections);
        Assert.Equal(0, observers);
        Assert.Empty(Assert.Single(store.Read()).Debits);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Receipt_and_post_cache_wallet_are_both_required_in_either_order(bool receiptFirst)
    {
        var store = new Store();
        var objective = store.Open(Account, 60, 100, 900);
        int cashEntries = 0;
        Assert.True(Spend.TrySend(store, objective.Id, Guid.NewGuid(), Account, "purchase", 100, 1000,
            op => op.Settled += amount => { Assert.Equal(100, amount); cashEntries++; }, () => true, out var operation));
        if (receiptFirst) operation!.Confirmed(); else operation!.Wallet(1000, 900);
        Assert.False(operation.Completion.IsCompleted);
        Assert.Equal(0, Assert.Single(store.Read()).NewExpense);
        if (receiptFirst) operation.Wallet(1000, 900); else operation.Confirmed();
        Assert.True(await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        operation.Confirmed();
        operation.Wallet(900, 1000);
        Assert.Equal(1, cashEntries);
        Assert.Equal(100, Assert.Single(store.Read()).NewExpense);
        Assert.Equal(0, Assert.Single(store.Read()).Available(2000));
    }

    [Theory]
    [InlineData(1000, 1100, 1100, 1000)] // income followed by the quoted debit
    [InlineData(1000, 950, 950, 900)] // two unrelated debits cannot be combined into an invoice
    [InlineData(1000, 1100, 1100, 900)] // ambiguous net movement
    public void Ambiguous_wallet_activity_never_settles_or_releases(long before, long after, long nextBefore, long nextAfter)
    {
        var store = new Store();
        var objective = store.Open(Account, 60, 100, 900);
        Assert.True(Spend.TrySend(store, objective.Id, Guid.NewGuid(), Account, "travel", 100, 1000,
            _ => { }, () => true, out var operation));
        operation!.Wallet(before, after);
        operation.Wallet(nextBefore, nextAfter);
        operation.Confirmed();
        operation.Refused();
        Assert.False(operation.Completion.IsCompleted);
        Assert.True(Assert.Single(store.Read()).NeedsReconciliation);
        Assert.Equal(0, Assert.Single(store.Read()).NewExpense);
    }

    [Fact]
    public async Task Caller_timeout_or_cancellation_does_not_cancel_reconciliation_or_allow_resend()
    {
        var store = new Store();
        var objective = store.Open(Account, 60, 100, 900);
        int injections = 0;
        Assert.True(Spend.TrySend(store, objective.Id, Guid.NewGuid(), Account, "purchase", 100, 1000,
            _ => { }, () => { injections++; return true; }, out var operation));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation!.Completion.WaitAsync(cancellation.Token));
        await Assert.ThrowsAsync<TimeoutException>(() => operation!.Completion.WaitAsync(TimeSpan.Zero));
        Assert.False(Spend.TrySend(store, objective.Id, Guid.NewGuid(), Account, "purchase", 1, 1000,
            _ => { }, () => { injections++; return true; }, out _));
        Assert.Equal(1, injections);
        operation!.Wallet(1000, 900);
        operation.Confirmed();
        Assert.True(await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Definite_non_send_releases_only_without_contradictory_debit()
    {
        var store = new Store();
        var objective = store.Open(Account, 60, 100, 900);
        Assert.False(Spend.TrySend(store, objective.Id, Guid.NewGuid(), Account, "purchase", 100, 1000,
            _ => { }, () => false, out var released));
        Assert.True(released!.Finished);
        Assert.False(Assert.Single(store.Read()).NeedsReconciliation);
        Spend? pending = null;
        Assert.False(Spend.TrySend(store, objective.Id, Guid.NewGuid(), Account, "purchase", 100, 1000,
            op => pending = op, () => { pending!.Wallet(1000, 900); return false; }, out _));
        Assert.True(Assert.Single(store.Read()).NeedsReconciliation);
    }

    [Fact]
    public void Unknown_identity_or_corrupt_ledger_never_injects()
    {
        var store = new Store();
        var objective = store.Open(Account, 60, 100, 900);
        int injections = 0;
        bool Inject() { injections++; return true; }
        Assert.False(Spend.TrySend(store, objective.Id, Guid.NewGuid(), Account with { CharacterId = 22 },
            "purchase", 1, 1000, _ => { }, Inject, out _));
        Assert.False(Spend.TrySend(store, Guid.NewGuid(), Guid.NewGuid(), Account,
            "purchase", 1, 1000, _ => { }, Inject, out _));
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "broken.json");
        File.WriteAllText(path, "{broken");
        Assert.False(Spend.TrySend(new Store(path), objective.Id, Guid.NewGuid(), Account,
            "purchase", 1, 1000, _ => { }, Inject, out _));
        Assert.Equal(0, injections);
    }

    private static FieldInfo Field(string name) => typeof(Launch).GetField(name, Hidden)!;
    private static Launch Service(Store store)
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (string name in new[] { "_smithSpending", "_smithPending", "_smithSpendGate", "_charId", "_serverInfo", "_kamas", "_jobProgress" })
            Field(name).SetValue(service, Activator.CreateInstance(Field(name).FieldType, nonPublic: true));
        Field("_smithObjectives").SetValue(service, store);
        Field("<ForgeHistory>k__BackingField").SetValue(service, new History());
        service.SetAccountsSnapshot([new("account-a", "alias", "character", 9, true, "login")]);
        ((IDictionary)Field("_charId").GetValue(service)!)[9u] = Account.CharacterId;
        ((IDictionary)Field("_serverInfo").GetValue(service)!)[9u] = (Account.ServerId, "server");
        ((IDictionary)Field("_kamas").GetValue(service)!)[9u] = 1000L;
        return service;
    }

    private static (bool Sent, Spend? Operation) Send(Launch service, uint pid, Func<bool> inject)
    {
        object?[] args = [pid, "purchase", 100L, 71, 1, 0L, 0L, inject, null];
        bool sent = (bool)typeof(Launch).GetMethod("SendSmithSpend", Hidden)!.Invoke(service, args)!;
        return (sent, (Spend?)args[8]);
    }

    [Fact]
    public async Task Flow_scope_inherits_into_task_run_but_does_not_authorize_other_flows_or_pids()
    {
        var store = new Store();
        var objective = store.Open(Account, 60, 100, 900);
        var service = Service(store);
        int injections = 0;
        bool Inject() { Interlocked.Increment(ref injections); return true; }
        using (service.BeginSmithSpending(9, objective.Id, Guid.NewGuid()))
        {
            Assert.False((await Task.Run(() => Send(service, 10, Inject))).Sent);
            Assert.False(service.TeleportDirect(9, 42)); // no unpriced paid request even reaches dialog/transport state
            Assert.True((await Task.Run(() => Send(service, 9, Inject))).Sent);
            Assert.False((await Task.Run(() => Send(service, 9, Inject))).Sent);
        }
        Assert.Equal(1, injections);
        Assert.True(Send(service, 9, Inject).Sent); // ordinary non-FM behavior remains outside the flow
        Assert.Equal(2, injections);
        Assert.True(Assert.Single(store.Read()).NeedsReconciliation);
    }

    [Fact]
    public async Task Disposed_scope_blocks_an_inherited_child_before_it_reaches_the_injector()
    {
        var store = new Store();
        var objective = store.Open(Account, 60, 100, 900);
        var service = Service(store);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int injections = 0;
        Task<bool> child;
        using (service.BeginSmithSpending(9, objective.Id, Guid.NewGuid()))
        {
            child = Task.Run(async () =>
            {
                await resume.Task;
                return Send(service, 9, () => { injections++; return true; }).Sent;
            });
        }
        resume.SetResult();
        Assert.False(await child.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, injections);
        Assert.Empty(Assert.Single(store.Read()).Debits);
    }

    [Fact]
    public void Another_profession_cannot_replace_a_pending_operation_for_the_same_character()
    {
        var store = new Store();
        var first = store.Open(Account, 60, 100, 900);
        var second = store.Open(Account with { JobId = 63 }, 60, 100, 900);
        var service = Service(store);
        int injections = 0;
        bool Inject() { injections++; return true; }
        using (service.BeginSmithSpending(9, first.Id, Guid.NewGuid()))
            Assert.True(Send(service, 9, Inject).Sent);
        using (service.BeginSmithSpending(9, second.Id, Guid.NewGuid()))
            Assert.False(Send(service, 9, Inject).Sent);

        Assert.Equal(1, injections);
        Assert.Empty(store.Read().Single(o => o.Id == second.Id).Debits);
        Assert.True(store.Read().Single(o => o.Id == first.Id).NeedsReconciliation);
    }

    [Fact]
    public async Task Free_travel_definitely_not_sent_releases_its_reservation()
    {
        var store = new Store();
        var objective = store.Open(Account, 60, 100, 900);
        Assert.False(Spend.TrySend(store, objective.Id, Guid.NewGuid(), Account, "travel", 0, 1000,
            _ => { }, () => false, out var operation));
        Assert.False(await operation!.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(Assert.Single(store.Read()).NeedsReconciliation);
    }

    [Fact]
    public async Task Unchanged_wallet_snapshot_does_not_poison_free_arrival()
    {
        var store = new Store();
        var objective = store.Open(Account, 60, 100, 900);
        Assert.True(Spend.TrySend(store, objective.Id, Guid.NewGuid(), Account, "travel", 0, 1000,
            _ => { }, () => true, out var operation));
        operation!.Wallet(1000, 1000);
        operation.Confirmed();
        Assert.True(await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(Assert.Single(store.Read()).NeedsReconciliation);
    }

    [Fact]
    public void Reopening_settled_budget_replays_cash_and_purchase_without_starting_a_spending_scope()
    {
        string path = Path.Combine(_root, "objectives.json");
        var store = new Store(path);
        var objective = store.Open(Account, 60, 100, 900);
        var service = Service(store);
        Guid attempt = service.BeginForgeHistorySession(9, Account.AccountId, Account.JobId);
        service.EndForgeHistorySession(attempt, "done");
        var debit = store.Reserve(objective.Id, attempt, "purchase", 100, 1000, 71, 1519, 2)!;
        store.Settle(objective.Id, debit.Id, 100);
        Field("_smithObjectives").SetValue(service, new Store(path));

        service.ReconcileSmithHistory();
        service.ReconcileSmithHistory();

        var history = service.ForgeHistory.Read();
        Assert.Equal(100, Assert.Single(history.Sessions).ActualCashExpense);
        Assert.Equal("done", Assert.Single(history.Sessions).Stop);
        Assert.Equal(debit.Id, Assert.Single(history.Entries, e => e.Kind == "cash").OperationId);
        var purchase = Assert.Single(history.Entries, e => e.Kind == "purchase");
        Assert.Equal(71, purchase.ItemUid);
        Assert.Equal(1519, purchase.ItemGid);
        Assert.Equal(2, purchase.Quantity);
        Assert.Equal(50, purchase.UnitPrice);
    }

    [Fact]
    public async Task Confirmed_free_travel_does_not_require_a_nonexistent_wallet_debit()
    {
        var store = new Store();
        var objective = store.Open(Account, 60, 100, 900);
        Assert.True(Spend.TrySend(store, objective.Id, Guid.NewGuid(), Account, "travel", 0, 1000,
            _ => { }, () => true, out var operation));

        operation!.Confirmed();

        Assert.True(await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, Assert.Single(store.Read()).NewExpense);
        Assert.False(Assert.Single(store.Read()).NeedsReconciliation);
    }

    [Fact]
    public async Task Post_cache_wallet_observer_survives_scope_exit_and_projects_purchase_without_double_cash()
    {
        var store = new Store();
        var objective = store.Open(Account, 60, 100, 900);
        var service = Service(store);
        Guid attempt = service.BeginForgeHistorySession(9, Account.AccountId, Account.JobId);
        Spend? operation;
        using (service.BeginSmithSpending(9, objective.Id, attempt))
        {
            var sent = Send(service, 9, () => true);
            Assert.True(sent.Sent);
            operation = sent.Operation;
        }
        service.EndForgeHistorySession(attempt, "annule");
        operation!.Confirmed();
        ((IDictionary)Field("_kamas").GetValue(service)!)[9u] = 900L;
        typeof(Launch).GetMethod("NoteSmithWallet", Hidden)!.Invoke(service, [9u, 1000L, 900L]);
        Assert.True(await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        operation.Confirmed();
        var page = service.ForgeHistory.Read();
        Assert.Equal(100, Assert.Single(page.Sessions).ActualCashExpense);
        Assert.NotNull(Assert.Single(page.Sessions).Ended);
        Assert.Equal("annule", Assert.Single(page.Sessions).Stop);
        var cash = Assert.Single(page.Entries, e => e.Kind == "cash");
        Assert.Equal(attempt, cash.SessionId);
        Assert.Equal(100, cash.CashExpense);
        var purchase = Assert.Single(page.Entries, e => e.Kind == "purchase");
        Assert.Equal(71, purchase.ItemUid);
        Assert.Equal(1, purchase.Quantity);
        Assert.Equal(100, purchase.UnitPrice);
        Assert.Equal(operation.OperationId, purchase.OperationId);
        Assert.Null(purchase.CashExpense);
    }

    [Fact]
    public void Injection_exception_keeps_a_durable_reservation_and_never_retries()
    {
        var store = new Store();
        var objective = store.Open(Account, 60, 100, 900);
        int injections = 0;
        Assert.Throws<IOException>(() => Spend.TrySend(store, objective.Id, Guid.NewGuid(), Account, "purchase", 100, 1000,
            _ => { }, () => { injections++; throw new IOException("transport"); }, out _));
        Assert.True(Assert.Single(store.Read()).NeedsReconciliation);
        Assert.Equal(1, injections);
        Assert.False(Spend.TrySend(store, objective.Id, Guid.NewGuid(), Account, "purchase", 1, 1000,
            _ => { }, () => { injections++; return true; }, out _));
        Assert.Equal(1, injections);
    }

    [Fact]
    public async Task Late_purchase_settles_smith_budget_without_auto_clearing_direct_buy_quarantine()
    {
        var store = new Store();
        var objective = store.Open(Account, 60, 100, 900);
        int injections = 0;
        Assert.True(Spend.TrySend(store, objective.Id, Guid.NewGuid(), Account, "purchase", 100, 1000,
            _ => { }, () => { injections++; return true; }, out var operation));
        var service = Service(store);
        foreach (string name in new[] { "_hdvUncertainSpend", "_hdvUncertainBuy" })
            Field(name).SetValue(service, Activator.CreateInstance(Field(name).FieldType));
        var spends = (IDictionary)Field("_hdvUncertainSpend").GetValue(service)!;
        var buys = (IDictionary)Field("_hdvUncertainBuy").GetValue(service)!;
        spends[9u] = operation;
        buys[9u] = (123, 100, 100L, 1L);
        operation!.Confirmed();
        service.ReconcileSmithHistory();
        Assert.Single(buys);
        operation.Wallet(1000, 900);
        Assert.True(await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        service.ReconcileSmithHistory();

        // Exact wallet debit settles only the Smith spending ledger; the
        // server buy event lacks quantity and price correlation. The direct
        // HDV anti-replay quarantine is NOT auto-cleared by this fact.
        Assert.Single(buys);
        Assert.Single(spends);
        Assert.Null(typeof(Launch).GetMethod("ClearSettledHdvBuy", Hidden));
        Assert.Equal(1, injections);
        Assert.Equal(100, Assert.Single(store.Read()).NewExpense);
    }

    [Fact]
    public void Non_FM_send_does_not_require_new_fields_in_uninitialized_service_fixtures()
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        int injections = 0;
        Assert.True(Send(service, 9, () => { injections++; return true; }).Sent);
        Assert.Equal(1, injections);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Teleport_boundary_requires_target_arrival_and_matching_wallet_in_either_order(bool arrivalFirst)
    {
        var store = new Store();
        var objective = store.Open(Account, 60, 100, 900);
        var service = Service(store);
        Guid attempt = service.BeginForgeHistorySession(9, Account.AccountId, Account.JobId);
        object?[] args = [9u, "travel", 100L, 0, 0, 0L, 42L, (Func<bool>)(() => true), null];
        using (service.BeginSmithSpending(9, objective.Id, attempt))
            Assert.True((bool)typeof(Launch).GetMethod("SendSmithSpend", Hidden)!.Invoke(service, args)!);
        var operation = (Spend)args[8]!;
        void Arrive(long target) => typeof(Launch).GetMethod("NoteSmithArrival", Hidden)!.Invoke(service, [9u, target]);
        void Wallet()
        {
            ((IDictionary)Field("_kamas").GetValue(service)!)[9u] = 900L;
            typeof(Launch).GetMethod("NoteSmithWallet", Hidden)!.Invoke(service, [9u, 1000L, 900L]);
        }
        Arrive(41);
        if (arrivalFirst) Arrive(42); else Wallet();
        Assert.False(operation.Completion.IsCompleted);
        Assert.Equal(0, Assert.Single(store.Read()).NewExpense);
        if (arrivalFirst) Wallet(); else Arrive(42);
        Assert.True(await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(100, Assert.Single(store.Read()).NewExpense);
        Assert.Equal(100, Assert.Single(service.ForgeHistory.Read().Sessions).ActualCashExpense);
        Assert.DoesNotContain(service.ForgeHistory.Read().Entries, e => e.Kind == "purchase");
    }
}
