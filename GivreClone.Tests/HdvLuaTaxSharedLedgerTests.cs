extern alias F06App;

using Decision = F06App::GivreClone.App.Services.HdvTaxOrderReservation;
using Flow = F06App::GivreClone.App.Services.HdvTaxedMutationFlow;
using Keys = F06App::GivreClone.App.Services.HdvTaxOrderKeys;
using Ledger = F06App::GivreClone.App.Services.HdvTaxReservations;
using Phase = F06App::GivreClone.App.Services.HdvTaxOrderPhase;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// The Lua/desktop entrance share the exact on-disk file and canonical order
/// identity. Fake send delegates stand in for a correlated result, not a live
/// server receipt or an invocation of the Dofus client.
/// </summary>
public sealed class HdvLuaTaxSharedLedgerTests
{
    private const string Profile = "server:292:Char";

    private sealed class PathScope : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(),
            "givre-shared-lua-hdv-" + Guid.NewGuid().ToString("N"));
        public string JournalPath => Path.Combine(_dir, "hdv", "tax-reservations-v1.journal");
        public Ledger NewInstance() => new(JournalPath);
        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task Lua_and_desktop_identical_sell_uid_quantity_price_stock_are_one_order()
    {
        using var path = new PathScope();
        int callbacks = 0;
        string scriptOrder = Keys.Sell(4005, 10, 1500, 30);
        var lua = await Flow.ExecuteAsync(path.NewInstance(), 81, Profile,
            scriptOrder, 200, 0, 1500, 5f, _ =>
            {
                callbacks++;
                return Task.FromResult(true);
            });
        Assert.True(lua.ServerResultObserved);
        Assert.Equal(Phase.Observed, path.NewInstance().ReadOrderPhase(Profile, scriptOrder));

        // The second caller is a fresh ViewModel (or process): no local
        // semaphore can be relied on; durable order identity must win.
        string uiOrder = Keys.Sell(4005, 10, 1500, 30);
        var ui = await Flow.ExecuteAsync(path.NewInstance(), 1234, Profile,
            uiOrder, 200, 0, 1500, 5f, _ =>
            {
                callbacks++;
                return Task.FromResult(true);
            });
        Assert.Equal(Decision.AlreadyReserved, ui.Decision);
        Assert.False(ui.ServerResultObserved);
        Assert.Equal(1, callbacks);
        Assert.Equal(75, path.NewInstance().PendingDurable(Profile));
    }

    [Fact]
    public async Task Lua_undetermined_reprice_blocks_desktop_other_uid_even_after_new_pid()
    {
        using var path = new PathScope();
        int callbacks = 0;
        var first = await Flow.ExecuteAsync(path.NewInstance(), 11, Profile,
            Keys.Reprice(102, 100, 4200), 600, 0, 4200, 5f, _ =>
            {
                callbacks++;
                return Task.FromResult(false); // timeout after possible inject
            });
        Assert.False(first.ServerResultObserved);
        var next = await Flow.ExecuteAsync(path.NewInstance(), 33, Profile,
            Keys.Reprice(103, 100, 4500), 600, 0, 4500, 5f, _ =>
            {
                callbacks++;
                return Task.FromResult(true);
            });
        Assert.Equal(Decision.NeedsReconciliation, next.Decision);
        Assert.Equal(1, callbacks);
        Assert.Equal(Phase.Uncertain, path.NewInstance().ReadOrderPhase(Profile,
            Keys.Reprice(102, 100, 4200)));
        Assert.Equal(210, path.NewInstance().PendingDurable(Profile));
    }

    [Fact]
    public async Task Concurrent_lua_and_desktop_must_not_both_dispatch_same_order()
    {
        using var path = new PathScope();
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int callbacks = 0;
        string key = Keys.Sell(55, 1, 1000, 20);
        Task<Flow.Result> first = Flow.ExecuteAsync(path.NewInstance(), 1, Profile,
            key, 200, 0, 1000, 5f, _ =>
            {
                Interlocked.Increment(ref callbacks);
                firstStarted.SetResult(true);
                return release.Task;
            });
        await firstStarted.Task;
        var second = await Flow.ExecuteAsync(path.NewInstance(), 2, Profile,
            key, 200, 0, 1000, 5f, _ =>
            {
                Interlocked.Increment(ref callbacks);
                return Task.FromResult(true);
            });
        Assert.Equal(Decision.AlreadyReserved, second.Decision);
        release.SetResult(true);
        Assert.True((await first).ServerResultObserved);
        Assert.Equal(1, Volatile.Read(ref callbacks));
    }

    [Fact]
    public async Task Corruption_refuses_lua_then_desktop_without_discarding_record()
    {
        using var path = new PathScope();
        string key = Keys.Sell(5, 10, 200, 50);
        await Flow.ExecuteAsync(path.NewInstance(), 1, Profile, key,
            100, 0, 200, 5f, _ => Task.FromResult(true));
        string bad = File.ReadAllText(path.JournalPath) + "2|garbled\n";
        File.WriteAllText(path.JournalPath, bad);
        int callbacks = 0;
        var next = await Flow.ExecuteAsync(path.NewInstance(),
            2, Profile, Keys.Sell(6, 10, 2000, 50), 200, 0, 2000, 5f,
            _ => { callbacks++; return Task.FromResult(true); });
        Assert.Equal(Decision.Unavailable, next.Decision);
        Assert.Equal(0, callbacks);
        Assert.Equal(bad, File.ReadAllText(path.JournalPath));
        Assert.Equal(long.MaxValue, path.NewInstance().PendingDurable(Profile));
    }
}
