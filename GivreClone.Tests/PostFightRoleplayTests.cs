extern alias F06App;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using LuaHost = F06App::GivreClone.App.Services.GivreLuaHost;
using Xunit;

namespace GivreClone.Tests;

public class PostFightRoleplayTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static (LuaHost Host, ConcurrentDictionary<uint, bool> Pending) Host()
    {
        var launch = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        var pending = new ConcurrentDictionary<uint, bool>();
        pending[1] = true;
        typeof(LaunchService).GetField("_postFightMapPending", Private)!.SetValue(launch, pending);
        typeof(LaunchService).GetField("_inFight", Private)!.SetValue(launch, new ConcurrentDictionary<uint, bool>());
        var host = (LuaHost)RuntimeHelpers.GetUninitializedObject(typeof(LuaHost));
        typeof(LuaHost).GetField("_launch", Private)!.SetValue(host, launch);
        typeof(LuaHost).GetField("_pid", Private)!.SetValue(host, (uint)1);
        return (host, pending);
    }
    private static void Wait(LuaHost host, CancellationToken ct) => typeof(LuaHost)
        .GetMethod("WaitForPostFightRoleplay", Private)!.Invoke(host, [ct]);

    [Fact]
    public async Task Fight_return_waits_for_roleplay_snapshot_instead_of_exposing_the_combat_map()
    {
        var (host, pending) = Host();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        // The production wait is deliberately synchronous. Give it its own thread
        // rather than starving the pool while the rest of the suite is running.
        Task continuation = Task.Factory.StartNew(() => { started.SetResult(); Wait(host, timeout.Token); },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(continuation.IsCompleted);
        pending.TryRemove(1, out _);
        await continuation.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Stop_interrupts_pending_map_wait_without_executing_the_next_script_statement()
    {
        var (host, _) = Host();
        bool nextStatement = false;
        var error = Assert.Throws<TargetInvocationException>(() =>
        {
            Wait(host, new CancellationToken(true));
            nextStatement = true;
        });
        Assert.Equal("ScriptStopException", error.InnerException!.GetType().Name);
        Assert.False(nextStatement);
    }
}
