using GivreClone.App.Services.Supervisor;
using Xunit;

namespace GivreClone.Tests;

public sealed class TeamSupervisorTests
{
    private sealed class TeamsPort : ITeamSupervisorPort
    {
        public string? Action;
        public System.Text.Json.JsonElement Args;
        public Task<System.Text.Json.Nodes.JsonNode> ControlTeamAsync(string action, System.Text.Json.JsonElement args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Action = action; Args = args.Clone();
            return Task.FromResult<System.Text.Json.Nodes.JsonNode>(new System.Text.Json.Nodes.JsonObject
            { ["ok"] = true, ["status"] = "requested", ["reason"] = "unconfirmed" });
        }
    }

    [Fact]
    public void Lua_teams_preserves_requested_status_and_options()
    {
        var port = new TeamsPort();
        using var host = new SupervisorLuaHost("teams-test", new SupervisorServices(new FakeDirectory(), new FakeOrders(),
            new FakeAccounts(), new FakeProxies(), new Perms(), new SupervisorBus()) { Teams = port });
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        host.Log += (level, text) => { if (level == "error") errors.Enqueue(text); };
        host.Start("""
            function move()
                local r = teams:form({name="Alpha", leader="a1", enabled=true, timeout=250})
                assert(r.ok and r.status == "requested" and r.reason == "unconfirmed")
                return false
            end
            """);
        Assert.True(host.WaitEnded(TimeSpan.FromSeconds(5)));
        Assert.Empty(errors);
        Assert.Equal("form", port.Action);
        Assert.Equal("Alpha", port.Args.GetProperty("name").GetString());
        Assert.Equal(250, port.Args.GetProperty("timeout").GetInt32());
    }

    [Fact]
    public void Lua_teams_checks_permission_before_port_dispatch()
    {
        var port = new TeamsPort();
        using var host = new SupervisorLuaHost("teams-denied", new SupervisorServices(new FakeDirectory(), new FakeOrders(),
            new FakeAccounts(), new FakeProxies(), new Perms { HasMulti = false }, new SupervisorBus()) { Teams = port });
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        host.Log += (level, text) => { if (level == "error") errors.Enqueue(text); };
        host.Start("function move() local r = teams:list(); assert(not r.ok and r.status == 'error'); return false end");
        Assert.True(host.WaitEnded(TimeSpan.FromSeconds(5)));
        Assert.Empty(errors);
        Assert.Null(port.Action);
    }

    [Fact]
    public async Task Gather_returns_observed_success()
    {
        var result = Assert.IsType<TeamControlResult>(await TeamSupervisorWait.GatherAsync(
            _ => Task.FromResult(new TeamControlResult(true, "gathered")), 1000, CancellationToken.None));
        Assert.True(result.Ok);
        Assert.Equal("gathered", result.Status);
    }

    [Fact]
    public async Task Gather_zero_timeout_observes_once_without_claiming_success()
    {
        int calls = 0;
        var result = Assert.IsType<TeamControlResult>(await TeamSupervisorWait.GatherAsync(
            _ => { calls++; return Task.FromResult(new TeamControlResult(true, "pending")); }, 0, CancellationToken.None));
        Assert.Equal(1, calls);
        Assert.False(result.Ok);
        Assert.Equal("timeout", result.Status);
    }

    [Fact]
    public async Task Gather_preserves_operation_failure()
    {
        var result = Assert.IsType<TeamControlResult>(await TeamSupervisorWait.GatherAsync(
            _ => Task.FromResult(TeamControlResult.Error("unknown teamId")), 1000, CancellationToken.None));
        Assert.False(result.Ok);
        Assert.Equal("unknown teamId", result.Reason);
    }

    [Fact]
    public async Task Gather_propagates_cancellation_before_observation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TeamSupervisorWait.GatherAsync(
            _ => throw new InvalidOperationException("must not observe"), 1000, cts.Token));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(120001)]
    public async Task Gather_rejects_unbounded_timeout(int ms)
    {
        var result = Assert.IsType<TeamControlResult>(await TeamSupervisorWait.GatherAsync(
            _ => throw new InvalidOperationException("must not observe"), ms, CancellationToken.None));
        Assert.False(result.Ok);
        Assert.Equal("error", result.Status);
    }

    [Fact]
    public async Task Gather_deadline_cancels_blocked_observer()
    {
        var result = Assert.IsType<TeamControlResult>(await TeamSupervisorWait.GatherAsync(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new TeamControlResult(true, "gathered");
        }, 10, CancellationToken.None));
        Assert.False(result.Ok);
        Assert.Equal("timeout", result.Status);
    }

    [Fact]
    public async Task Gather_deadline_bounds_observer_that_ignores_cancellation()
    {
        var blocked = new TaskCompletionSource<TeamControlResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = Assert.IsType<TeamControlResult>(await TeamSupervisorWait.GatherAsync(
            _ => blocked.Task, 10, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(result.Ok);
        Assert.Equal("timeout", result.Status);
        Assert.False(blocked.Task.IsCompleted);
    }
}
