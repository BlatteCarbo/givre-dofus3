extern alias F06App;

using System.Reflection;
using System.Runtime.CompilerServices;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using TreasureViewModel = F06App::GivreClone.App.ViewModels.TreasureViewModel;
using Xunit;

namespace GivreClone.Tests;

public sealed class TreasureNavigationRegressionTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public async Task Navigation_yields_to_replacement_or_script_control(bool changeCharacter, bool duringTravel, bool scriptTakesOver)
    {
        // No service constructor: no session, transport or account is opened.
        var service = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        foreach (string name in new[] { "_treasure", "_treasureFinished" })
        {
            var field = typeof(LaunchService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(service, Activator.CreateInstance(field.FieldType));
        }
        var hunts = (System.Collections.IDictionary)typeof(LaunchService)
            .GetField("_treasure", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        hunts[(uint)101] = new LaunchService.TreasureHunt(0, 100, 1, 0, 1, 3,
            Array.Empty<LaunchService.TreasureFlag>(),
            new[] { new LaunchService.TreasureStep("poi", 0, 0, 1, 0) }, 1);
        var vm = new TreasureViewModel(service);
        bool scriptRunning = false;
        vm.ScriptRunning = _ => scriptRunning;
        vm.AttachPid(101);
        var resolved = new TaskCompletionSource<(long?, long, string?)>();
        var arrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        vm.ResolveStepMap = (_, _, ct) => { token = ct; return resolved.Task; };
        int travels = 0;
        vm.Travel = (_, _, _) => { travels++; return arrived.Task; };
        if (duringTravel) resolved.SetResult((200, 100, null));
        Task going = vm.GoToStepCommand.ExecuteAsync(null);
        if (changeCharacter) vm.AttachPid(102);
        else if (scriptTakesOver)
        {
            scriptRunning = true;
            vm.Refresh();
        }
        else
        {
            hunts[(uint)101] = new LaunchService.TreasureHunt(0, 100, 2, 0, 1, 3,
                Array.Empty<LaunchService.TreasureFlag>(),
                new[] { new LaunchService.TreasureStep("direction", 4, 0, 0, 2) }, 2);
            vm.Refresh();
        }
        string status = vm.Status;
        Assert.True(token.IsCancellationRequested);
        if (duringTravel) arrived.SetResult(true); // traveler deliberately ignores cancellation
        else resolved.SetResult((200, 100, null)); // resolver deliberately ignores cancellation
        await going;
        Assert.Equal(duringTravel ? 1 : 0, travels);
        Assert.Equal(status, vm.Status);
        Assert.False(vm.Going);
    }
}
