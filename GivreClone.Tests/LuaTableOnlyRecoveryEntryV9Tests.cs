extern alias F06App;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using World = F06App::GivreClone.App.Services.BotWorldStore;
using Outcome = F06App::GivreClone.App.Services.LuaRunOutcome;

namespace GivreClone.Tests;

/// <summary>
/// Real Lua host Start → DoString → RunLoopCore admission (no game client).
/// The V9 table-only opt-in bypasses ALL imperative move()/bank()/pods
/// maintenance while server identity is missing. Top-level user Lua remains
/// trusted code; these tests make no broader sandbox claim.
/// </summary>
public sealed class LuaTableOnlyRecoveryEntryV9Tests
{
    [Theory]
    [InlineData("ACTIVITY_RECOVERY_TABLE_ONLY = true; ACTIVITY_RECOVERY = false; ACTIVITY_RECOVERY_ROUTE = {{ map=1234, activityRecovery=true, cell=5 }}")]
    [InlineData("ACTIVITY_RECOVERY_TABLE_ONLY = true; ACTIVITY_RECOVERY = true; ACTIVITY_RECOVERY_ROUTE = nil")]
    [InlineData("ACTIVITY_RECOVERY_TABLE_ONLY = true; ACTIVITY_RECOVERY = true; ACTIVITY_RECOVERY_ROUTE = {}")]
    public async Task Invalid_persisted_strict_route_is_refused_before_any_imperative_loop_action(string setup)
    {
        var launch = new Launch();
        using var host = new Host(717, launch, new World(launch));
        var observed = new List<string>();
        host.Log += item => { lock (observed) observed.Add(item.Message); };
        host.Start(setup + """

            function move() error('UNSAFE_MOVE_CALLED') end
            function bank() error('UNSAFE_BANK_CALLED') end
            """);
        Assert.Equal(Outcome.Failed, await host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10)));
        string all;
        lock (observed) all = string.Join("\n", observed);
        Assert.Contains("ACTIVITY_RECOVERY_TABLE_ONLY", all, StringComparison.Ordinal);
        Assert.DoesNotContain("UNSAFE_MOVE_CALLED", all, StringComparison.Ordinal);
        Assert.DoesNotContain("UNSAFE_BANK_CALLED", all, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Valid_declarative_route_waits_for_world_ready_without_calling_imperative_move_or_bank()
    {
        var launch = new Launch();
        using var host = new Host(718, launch, new World(launch));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<string>();
        host.MainScriptStarting += () => started.TrySetResult();
        host.Log += item => { lock (observed) observed.Add(item.Message); };
        host.Start("""
            ACTIVITY_RECOVERY_TABLE_ONLY = true
            ACTIVITY_RECOVERY = true
            ACTIVITY_RECOVERY_ROUTE = {
                { map = 90703360, activityRecovery = true, cell = 320 }
            }
            MAX_PODS = 1
            function move() error('UNSAFE_MOVE_CALLED') end
            function bank() error('UNSAFE_BANK_CALLED') end
            """);
        var completion = host.RunCompletion;
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            // There is no authenticated map or actor for this PID. The only
            // acceptable behavior is to withhold ALL loop gameplay actions.
            await Task.Delay(110);
            Assert.False(completion.IsCompleted);
        }
        finally { host.Stop("user"); }
        Assert.Equal(Outcome.Stopped, await completion.WaitAsync(TimeSpan.FromSeconds(10)));
        string all;
        lock (observed) all = string.Join("\n", observed);
        Assert.DoesNotContain("UNSAFE_MOVE_CALLED", all, StringComparison.Ordinal);
        Assert.DoesNotContain("UNSAFE_BANK_CALLED", all, StringComparison.Ordinal);
    }
}
