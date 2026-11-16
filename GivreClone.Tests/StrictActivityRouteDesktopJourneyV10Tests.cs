extern alias F06App;

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Vm = F06App::GivreClone.App.ViewModels.BotsViewModel;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using World = F06App::GivreClone.App.Services.BotWorldStore;
using Outcome = F06App::GivreClone.App.Services.LuaRunOutcome;

namespace GivreClone.Tests;

/// <summary>
/// Tests the actual desktop generated script with the real Lua host execution
/// entry. All observations are offline. Generating a script or starting a
/// disconnected host cannot produce a game/transport ACK.
/// </summary>
public sealed class StrictActivityRouteDesktopJourneyV10Tests
{
    [Theory]
    [InlineData(0L, 320, 95)]
    [InlineData(-1L, 320, 95)]
    [InlineData(2147483648L, 320, 95)]
    [InlineData(191105026L, -1, 95)]
    [InlineData(191105026L, 560, 95)]
    [InlineData(191105026L, 320, 0)]
    [InlineData(191105026L, 320, 101)]
    public void Unsafe_ui_values_are_rejected_without_creating_script(
        long map, int cell, int maxPods)
    {
        Assert.False(Vm.TryBuildStrictActivityRouteScript(
            map, cell, false, maxPods, out string script, out string reason));
        Assert.Empty(script);
        Assert.Contains("aucun script", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Desktop_one_shot_script_enters_real_lua_host_without_any_imperative_send(
        bool gather)
    {
        Assert.True(Vm.TryBuildStrictActivityRouteScript(
            191105026, 320, gather, 95, out var script, out var reason), reason);
        Assert.Contains("ACTIVITY_RECOVERY_TABLE_ONLY = true", script);
        Assert.Contains("ACTIVITY_RECOVERY = true", script);
        Assert.Contains("map = 191105026, activityRecovery = true, cell = 320", script);
        Assert.Equal(gather, script.Contains("gather = true", StringComparison.Ordinal));
        Assert.DoesNotContain("function move()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("function bank()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("goToCellId(", script, StringComparison.Ordinal);

        // This is the exact same Start entry as the desktop's RunLua. The
        // disconnected fixture has no server/character/map/actor source proof.
        // The strict table-only loop must remain parked without dispatch.
        var launch = new Launch();
        using var host = new Host(771, launch, new World(launch));
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        host.MainScriptStarting += () => started.TrySetResult();
        host.Start(script);
        Task<Outcome> completed = host.RunCompletion;
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Task.Delay(125);
            Assert.False(completed.IsCompleted);
        }
        finally { host.Stop("user"); }
        Assert.Equal(Outcome.Stopped,
            await completed.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Classic_panel_binds_real_persistent_draft_command_and_read_only_status()
    {
        Assert.NotNull(typeof(Vm).GetProperty("StrictRouteTargetCell"));
        Assert.NotNull(typeof(Vm).GetProperty("StrictRouteMaxPods"));
        Assert.NotNull(typeof(Vm).GetProperty("StrictRouteGather"));
        Assert.NotNull(typeof(Vm).GetProperty("StrictRouteDraftStatus"));
        Assert.NotNull(typeof(Vm).GetProperty("PrepareStrictActivityRouteCommand"));
        var file = Path.Combine(FindRepoRoot(), "GivreClone.App", "Views", "BotsView.axaml");
        string view = File.ReadAllText(file);
        Assert.Contains("{Binding PrepareStrictActivityRouteCommand}", view);
        Assert.Contains("{Binding StrictRouteTargetCell}", view);
        Assert.Contains("{Binding StrictRouteMaxPods}", view);
        Assert.Contains("{Binding InspectActivityRecoveryCommand}", view);
        // A draft must not secretly turn on the existing script scheduler.
        var vmSource = File.ReadAllText(Path.Combine(FindRepoRoot(),
            "GivreClone.App", "ViewModels", "BotsViewModel.ActivityRecovery.cs"));
        Assert.Contains("PlanningStore.Get(characterName).Script = script;", vmSource);
        Assert.Contains("SetLuaTextLoaded(script, resetUndo: true);", vmSource);
        Assert.DoesNotContain("host.Start(script)", vmSource);
        Assert.Contains("CaptureStrictRouteTicket() != ticket", vmSource);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "GivreClone.App")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("GivreClone source unavailable");
    }
}
