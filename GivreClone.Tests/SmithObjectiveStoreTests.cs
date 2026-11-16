extern alias F06App;

using Xunit;
using SmithObjectiveStore = F06App::GivreClone.App.Services.SmithObjectiveStore;
using SmithObjectiveIdentity = F06App::GivreClone.App.Services.SmithObjectiveIdentity;

namespace GivreClone.Tests;

public sealed class SmithObjectiveStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smith-objective-" + Guid.NewGuid().ToString("N"));
    private string Ledger => Path.Combine(_root, "objectives.json");
    private static SmithObjectiveIdentity Identity => new("account-a", 354, 33202504034, 64);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    [Fact]
    public void Restart_and_income_do_not_replenish_settled_allowance()
    {
        var store = new SmithObjectiveStore(Ledger);
        var objective = store.Open(Identity, 60, 200000, 641448);
        var debit = store.Reserve(objective.Id, Guid.NewGuid(), "purchase", 10000, 841448)!;
        store.Settle(objective.Id, debit.Id, 10000);

        var restored = new SmithObjectiveStore(Ledger);
        var resumed = restored.Open(Identity, 60, 200000, 641448);

        Assert.Equal(objective.Id, resumed.Id);
        Assert.Equal(10000, resumed.TotalExpense);
        Assert.Equal(190000, resumed.Available(1000000));
        Assert.Equal(100, resumed.Available(641548));
        Assert.Throws<InvalidOperationException>(() => restored.Open(Identity, 60, 200000, 631448));
    }

    [Fact]
    public void Crash_after_reservation_blocks_another_paid_action_without_inventing_expense()
    {
        var store = new SmithObjectiveStore(Ledger);
        var objective = store.Open(Identity, 60, 200000, 641448);
        var debit = store.Reserve(objective.Id, Guid.NewGuid(), "travel", 500, 841448)!;

        var restored = new SmithObjectiveStore(Ledger);
        var resumed = Assert.Single(restored.Read());

        Assert.True(resumed.NeedsReconciliation);
        Assert.Equal(500, resumed.Reserved);
        Assert.Equal(0, resumed.NewExpense);
        Assert.Null(restored.Reserve(objective.Id, Guid.NewGuid(), "purchase", 1, 841448));
        restored.ConfirmNotSent(objective.Id, debit.Id);
        Assert.Equal(200000, Assert.Single(restored.Read()).Available(841448));
    }

    [Fact]
    public void Reconciled_opening_keeps_original_cap_and_independent_wallet_floor()
    {
        var store = new SmithObjectiveStore(Ledger);
        var objective = store.Open(Identity, 60, 200000, 641448,
            openingExpense: 35423, historicalConfirmedSubtotal: 35423);

        var restored = Assert.Single(new SmithObjectiveStore(Ledger).Read());

        Assert.Equal(objective.Id, restored.Id);
        Assert.Equal(164577, restored.Available(1000000));
        Assert.Equal(154780, restored.Available(796228));
        Assert.Equal(35423, restored.TotalExpense);
        Assert.False(restored.NeedsReconciliation);
    }

    [Fact]
    public void Incomplete_historical_coverage_does_not_turn_a_subtotal_into_a_new_budget()
    {
        var store = new SmithObjectiveStore(Ledger);
        var objective = store.Open(Identity, 60, 200000, 641448,
            openingExpense: null, historicalConfirmedSubtotal: 156);

        Assert.Equal(156, objective.HistoricalConfirmedSubtotal);
        Assert.Null(objective.TotalExpense);
        Assert.Equal(0, objective.Available(796228));
        Assert.Equal(0, objective.Available(1000000));
        Assert.Null(store.Reserve(objective.Id, Guid.NewGuid(), "purchase", 1, 796228));
        Assert.True(Assert.Single(new SmithObjectiveStore(Ledger).Read()).NeedsReconciliation);
    }

    [Fact]
    public void Exact_boundary_is_authorized_but_one_kama_excess_is_not()
    {
        var store = new SmithObjectiveStore();
        var objective = store.Open(Identity, 60, 200000, 641448);

        Assert.Null(store.Reserve(objective.Id, Guid.NewGuid(), "purchase", 200001, 841448));
        Assert.Null(store.Reserve(objective.Id, Guid.NewGuid(), "purchase", 101, 641548));
        Assert.Null(store.Reserve(objective.Id, Guid.NewGuid(), "purchase", 1, -1));
        Assert.NotNull(store.Reserve(objective.Id, Guid.NewGuid(), "purchase", 100, 641548));
    }

    [Fact]
    public void Settlement_is_idempotent_and_separate_accounts_have_separate_objectives()
    {
        var store = new SmithObjectiveStore();
        var first = store.Open(Identity, 60, 200000, 641448);
        var other = store.Open(Identity with { AccountId = "account-b" }, 60, 100, 900);
        var debit = store.Reserve(first.Id, Guid.NewGuid(), "purchase", 20, 841448)!;
        store.Settle(first.Id, debit.Id, 20);
        store.Settle(first.Id, debit.Id, 20);

        Assert.Equal(20, Assert.Single(store.Read("account-a")).TotalExpense);
        Assert.Equal(0, Assert.Single(store.Read("account-b")).TotalExpense);
        Assert.Equal(100, other.Available(1000));
    }

    [Fact]
    public void Unreadable_budget_is_not_overwritten_or_restored_as_spendable()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Ledger, "{broken");

        var store = new SmithObjectiveStore(Ledger);

        Assert.NotNull(store.StorageError);
        Assert.Throws<InvalidOperationException>(() => store.Open(Identity, 60, 200000, 641448));
        Assert.Equal("{broken", File.ReadAllText(Ledger));
    }

    [Fact]
    public void Failed_reservation_commit_does_not_publish_or_authorize_a_send()
    {
        var store = new SmithObjectiveStore(Ledger);
        var objective = store.Open(Identity, 60, 200000, 641448);
        File.Delete(Ledger);
        Directory.CreateDirectory(Ledger);
        int sends = 0;

        var error = Record.Exception(() =>
        {
            if (store.Reserve(objective.Id, Guid.NewGuid(), "purchase", 20, 841448) is not null) sends++;
        });

        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(0, sends);
        Assert.Empty(Assert.Single(store.Read()).Debits);
    }
}
