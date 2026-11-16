extern alias F06App;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using World = F06App::GivreClone.App.Services.BotWorldStore;
using Outcome = F06App::GivreClone.App.Services.LuaRunOutcome;
using Xunit;

namespace GivreClone.Tests;

public sealed class McpLuaCompletionControlTests
{
    [Theory]
    [InlineData("function move() return false end", Outcome.Completed)]
    [InlineData("function move() error('runtime failure') end", Outcome.Failed)]
    [InlineData("error('load failure')", Outcome.Failed)]
    [InlineData("local x = 1", Outcome.Failed)]
    public async Task Main_script_reports_its_actual_outcome(string code, Outcome expected)
    {
        var launch = new Launch();
        using var host = new Host(717, launch, new World(launch));
        host.Start(code);
        Assert.Equal(expected, await host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Fm_preparation_without_a_bag_support_refuses_and_the_template_finishes()
    {
        var launch = new Launch();
        using var host = new Host(717, launch, new World(launch));
        host.Start("""
            function move()
                local result, reason = fm:prepare(0, 64)
                assert(result == nil)
                assert(type(reason) == "string" and #reason > 0)
                return false
            end
            """);
        Assert.Equal(Outcome.Completed, await host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10)));

        host.Start(GivreClone.Engine.LuaTooling.LuaTemplates.Scripts.Single(t => t.Id == "fm-preparation").Body);
        Assert.Equal(Outcome.Completed, await host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Fm_preparation_refuses_a_bag_item_when_required_context_is_missing()
    {
        var launch = new Launch();
        launch.Inventory.Seed(717, new[] { new F06App::GivreClone.App.Services.InventoryStore.Item(991, 1000, 1) });
        using var host = new Host(717, launch, new World(launch));
        host.Start("""
            function move()
                local result, reason = fm:prepare(991, 64)
                assert(result == nil)
                assert(type(reason) == "string" and #reason > 0)
                return false
            end
            """);
        Assert.Equal(Outcome.Completed, await host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, launch.Inventory.Get(717, 991)?.Qty);
    }

    [Fact]
    public async Task User_stop_reports_stopped_instead_of_success()
    {
        var launch = new Launch();
        using var host = new Host(717, launch, new World(launch));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.MainScriptStarting += () => entered.TrySetResult();
        host.Start("function move() while true do end end");
        var completion = host.RunCompletion;
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        host.Stop();

        Assert.Equal(Outcome.Stopped, await completion.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(host.Running);
    }

    [Fact]
    public async Task Replacing_a_run_preserves_its_stopped_result_and_the_new_runs_success()
    {
        var launch = new Launch();
        using var host = new Host(717, launch, new World(launch));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.MainScriptStarting += () => entered.TrySetResult();
        host.Start("function move() while true do end end");
        var first = host.RunCompletion;
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        host.Start("function move() return false end");
        var second = host.RunCompletion;
        Assert.Equal(Outcome.Stopped, await first.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(Outcome.Completed, await second.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
