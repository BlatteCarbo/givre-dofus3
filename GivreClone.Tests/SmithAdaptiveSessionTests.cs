extern alias F06App;

using System.Reflection;
using Xunit;
using SmithJobChoice = F06App::GivreClone.App.ViewModels.SmithJobChoice;
using SmithMagicViewModel = F06App::GivreClone.App.ViewModels.SmithMagicViewModel;

namespace GivreClone.Tests;

public sealed class SmithAdaptiveSessionTests
{
    private static SmithMagicViewModel Model()
    {
        var vm = new SmithMagicViewModel
        {
            AdaptiveLeveling = true,
            Leveling = true,
            TargetJobLevel = 60,
            SelectedJob = new SmithJobChoice(64, "Costumage", 1),
        };
        typeof(SmithMagicViewModel).GetField("_pid", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, 101);
        return vm;
    }

    [Fact]
    public void Adaptive_mode_can_start_without_a_queue_or_manual_rune()
    {
        var vm = Model();
        Assert.Empty(vm.Queue);
        Assert.Null(vm.LevelingRune);
        Assert.True(vm.CanStart);
        vm.AdaptiveLeveling = false;
        Assert.False(vm.CanStart);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(60)]
    [InlineData(61)]
    public async Task Unknown_or_completed_profession_never_acquires_or_travels(int? level)
    {
        var vm = Model();
        int calls = 0;
        vm.JobLevelOf = (_, _) => level;
        vm.BuyLevelingSupport = (_, _, _, _, _, _, _) => { calls++; throw new InvalidOperationException(); };
        vm.Travel = (_, _, _) => { calls++; throw new InvalidOperationException(); };
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(0, calls);
        Assert.False(vm.Running);
    }
}
