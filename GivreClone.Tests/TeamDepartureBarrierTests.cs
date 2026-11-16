extern alias F06App;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using LuaHost = F06App::GivreClone.App.Services.GivreLuaHost;
using Xunit;

namespace GivreClone.Tests;

public class TeamDepartureBarrierTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static (LuaHost Host, LaunchService Launch, IDictionary Maps, IDictionary Fights) Setup()
    {
        var launch = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        foreach (string name in new[] { "_lastMap", "_inFight" })
        {
            var field = typeof(LaunchService).GetField(name, Private)!;
            field.SetValue(launch, Activator.CreateInstance(field.FieldType));
        }
        IDictionary Get(string name) => (IDictionary)typeof(LaunchService).GetField(name, Private)!.GetValue(launch)!;
        var maps = Get("_lastMap");
        maps[1u] = 10L; maps[2u] = 20L;
        launch.SetTeamMapChangeWait(new Dictionary<uint, uint[]> { [1] = [2] });
        var host = (LuaHost)RuntimeHelpers.GetUninitializedObject(typeof(LuaHost));
        typeof(LuaHost).GetField("_launch", Private)!.SetValue(host, launch);
        typeof(LuaHost).GetField("_pid", Private)!.SetValue(host, 1u);
        return (host, launch, maps, Get("_inFight"));
    }
    private static bool Wait(LuaHost host, int timeout = 0, CancellationToken ct = default) =>
        (bool)typeof(LuaHost).GetMethod("WaitForTeamDeparture", Private)!.Invoke(host, [ct, timeout])!;

    [Fact]
    public void Expired_wait_cannot_authorize_graph_travel_or_teleport()
    {
        var (host, _, _, _) = Setup();
        Assert.False(Wait(host));
    }

    [Fact]
    public void Member_joining_the_map_releases_the_departure()
    {
        var (host, _, maps, _) = Setup();
        maps[2u] = 10L;
        Assert.True(Wait(host));
    }

    [Fact]
    public void Member_in_another_fight_blocks_departure_even_on_the_same_map()
    {
        var (host, _, maps, fights) = Setup();
        maps[2u] = 10L; fights[2u] = true;
        Assert.False(Wait(host));
    }

    [Fact]
    public async Task Changed_map_invalidates_the_waiting_callers_snapshot()
    {
        var (host, _, maps, _) = Setup();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> wait = Task.Factory.StartNew(() => { entered.SetResult(); return Wait(host, 3000); },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        maps[1u] = 30L;
        Assert.False(await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Cancellation_prevents_departure_even_when_all_members_are_present()
    {
        var (host, _, maps, _) = Setup();
        maps[2u] = 10L;
        var error = Assert.Throws<TargetInvocationException>(() => Wait(host, ct: new CancellationToken(true)));
        Assert.Equal("ScriptStopException", error.InnerException!.GetType().Name);
    }

    [Fact]
    public void Removing_the_expected_team_does_not_leave_a_stale_barrier()
    {
        var (host, launch, _, _) = Setup();
        launch.SetTeamMapChangeWait(new Dictionary<uint, uint[]>());
        Assert.True(Wait(host));
    }
}
