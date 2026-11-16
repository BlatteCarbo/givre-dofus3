extern alias F06App;

using Broker = F06App::GivreClone.App.Services.HdvScriptTaxMutationBroker;
using Scope = F06App::GivreClone.App.Services.HdvScriptTaxScope;
using Keys = F06App::GivreClone.App.Services.HdvTaxOrderKeys;
using Ledger = F06App::GivreClone.App.Services.HdvTaxReservations;
using Phase = F06App::GivreClone.App.Services.HdvTaxOrderPhase;
using Decision = F06App::GivreClone.App.Services.HdvTaxOrderReservation;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvScriptTaxMutationBrokerTests
{
    private sealed class Sandbox : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "givre-script-mutation-broker-" + Guid.NewGuid().ToString("N"));
        public Ledger NewLedger() => new(Path.Combine(_root, "tax-reservations.journal"));
        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static Scope Scoped(uint pid = 19, long cap = 150)
        => new(pid, "server:292:Character", cap, 5f);

    [Fact]
    public async Task Policy_mismatch_at_dispatch_quarantines_attempt_and_never_calls_network_sender()
    {
        using var files = new Sandbox();
        int sends = 0;
        string key = Keys.Sell(99, 10, 200, 50);
        var outcome = await Broker.ExecuteAsync(files.NewLedger(), Scoped(), key, 200,
            isStillCurrent: () => false, sendAndVerify: _ =>
            {
                sends++;
                return Task.FromResult(true);
            });
        Assert.Equal(Decision.Reserved, outcome.Decision);
        Assert.False(outcome.ServerResultObserved);
        Assert.Equal(0, sends);
        Assert.Equal(Phase.Uncertain, files.NewLedger().ReadOrderPhase(Scoped().Profile, key));
        Assert.Equal(10, files.NewLedger().PendingDurable(Scoped().Profile));
        // A new PID with the same character cannot use a new UID to jump over
        // the unresolved attempted order.
        var blocked = await Broker.ExecuteAsync(files.NewLedger(), Scoped(pid: 20),
            Keys.Sell(100, 10, 200, 50), 200, () => true,
            _ => { sends++; return Task.FromResult(true); });
        Assert.Equal(Decision.NeedsReconciliation, blocked.Decision);
        Assert.Equal(0, sends);
    }

    [Fact]
    public async Task Shared_broker_writes_attempt_before_real_sender_and_observed_after_confirmed_listing()
    {
        using var files = new Sandbox();
        const string key = "reprice:202:100:5400";
        var scope = Scoped(cap: 500);
        int sends = 0;
        var result = await Broker.ExecuteAsync(files.NewLedger(), scope, key, 5400,
            () => true, _ =>
            {
                sends++;
                Assert.Equal(Phase.Attempted,
                    files.NewLedger().ReadOrderPhase(scope.Profile, key));
                return Task.FromResult(true);
            });
        Assert.True(result.ServerResultObserved);
        Assert.Equal(1, sends);
        Assert.Equal(Phase.Observed, files.NewLedger().ReadOrderPhase(scope.Profile, key));
        Assert.Equal(270, files.NewLedger().PendingDurable(scope.Profile));
        var duplicate = await Broker.ExecuteAsync(files.NewLedger(), Scoped(pid: 999, cap: 500),
            key, 5400, () => true,
            _ => { sends++; return Task.FromResult(true); });
        Assert.Equal(Decision.AlreadyReserved, duplicate.Decision);
        Assert.Equal(1, sends);
    }

    [Fact]
    public async Task Unknown_identity_or_negative_cap_blocks_even_before_durable_intent()
    {
        using var files = new Sandbox();
        int sends = 0;
        foreach (var scope in new[] {
            new Scope(0, Scoped().Profile, 100, 5),
            new Scope(19, "?", 100, 5),
            new Scope(19, "server:292:Character", -1, 5),
        })
        {
            var result = await Broker.ExecuteAsync(files.NewLedger(), scope,
                Keys.Sell(5, 1, 500, 3), 500, () => true,
                _ => { sends++; return Task.FromResult(true); });
            Assert.Equal(Decision.Unavailable, result.Decision);
        }
        Assert.Equal(0, sends);
    }

    [Fact]
    public async Task Unlimited_mode_still_checks_scope_and_prior_uncertainty()
    {
        using var files = new Sandbox();
        int sends = 0;
        var uncapped = await Broker.ExecuteAsync(files.NewLedger(), Scoped(cap: 0),
            Keys.Sell(1, 1, 100, 3), 100, () => true,
            _ => { sends++; return Task.FromResult(true); });
        Assert.True(uncapped.ServerResultObserved);
        Assert.Equal(1, sends);

        await Broker.ExecuteAsync(files.NewLedger(), Scoped(cap: 100),
            Keys.Sell(2, 1, 100, 3), 100, () => true,
            _ => Task.FromResult(false));
        var bypass = await Broker.ExecuteAsync(files.NewLedger(), Scoped(cap: 0),
            Keys.Sell(3, 1, 100, 3), 100, () => true,
            _ => { sends++; return Task.FromResult(true); });
        Assert.Equal(Decision.NeedsReconciliation, bypass.Decision);
        Assert.Equal(1, sends);
    }
}
