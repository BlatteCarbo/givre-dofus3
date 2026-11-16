extern alias F06App;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using Xunit;
using AccountsVm = F06App::GivreClone.App.ViewModels.AccountsViewModel;
using Character = F06App::GivreClone.App.ViewModels.SocketCharCard;
using Deletions = F06App::GivreClone.App.Services.Transport.Socket.SocketDeletionRegistry;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Names = F06App::GivreClone.App.Services.GameNameResolver;

namespace GivreClone.Tests;

public sealed class SocketPickerUxTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static (AccountsVm Vm, TaskCompletionSource<(int ServerId, string? Character)?> Pick) Picker()
    {
        var vm = (AccountsVm)RuntimeHelpers.GetUninitializedObject(typeof(AccountsVm));
        var launch = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        var names = new Names();
        var cache = (ConcurrentDictionary<int, string>)typeof(Names).GetField("_cacheServer", Hidden)!.GetValue(names)!;
        cache[42] = "Serveur de test";
        typeof(Launch).GetField("_gameNames", Hidden)!.SetValue(launch, names);
        typeof(AccountsVm).GetField("_launch", Hidden)!.SetValue(vm, launch);
        var pick = new TaskCompletionSource<(int ServerId, string? Character)?>();
        typeof(AccountsVm).GetField("_serverPickTcs", Hidden)!.SetValue(vm, pick);
        typeof(AccountsVm).GetField("_pickServerId", Hidden)!.SetValue(vm, 42);
        vm.ServerPickLogin = Guid.NewGuid().ToString("N");
        vm.ServerPickVisible = true;
        vm.ServerPickStep = 1;
        return (vm, pick);
    }

    [Fact]
    public void Opening_delete_requires_confirmation_and_cannot_select_the_character()
    {
        var (vm, pick) = Picker();
        var character = new Character { Name = "Testeur", ServerId = 42 };
        var server = typeof(AccountsVm).GetField("_pickServerId", Hidden)!;
        server.SetValue(vm, 0);   // pas de LaunchService ici : le sous-titre ne lit pas le nom du serveur
        vm.AskDeleteSocketCharacterCommand.Execute(character);
        server.SetValue(vm, 42);

        Assert.True(vm.ServerPickOnDelete);
        Assert.Same(character, vm.DeleteTarget);
        Assert.False(vm.DeleteConfirmed);
        Assert.Equal("", vm.DeleteName);
        Assert.Equal("", vm.DeleteAnswer);
        vm.PickSocketCharacterCommand.Execute(character);
        vm.ConfirmSocketDeletionCommand.Execute(null);
        Assert.False(pick.Task.IsCompleted);
        Assert.Null(Deletions.Take(vm.ServerPickLogin));
    }

    [Theory]
    [InlineData("", true, "secret", true, 3, false)]
    [InlineData("testeur", true, "secret", true, 3, false)]
    [InlineData("Autre", true, "secret", true, 3, false)]
    [InlineData("Testeur", false, "secret", true, 3, false)]
    [InlineData("Testeur", true, "", true, 3, false)]
    [InlineData("Testeur", true, "secret", false, 3, false)]
    [InlineData("Testeur", true, "secret", true, 1, false)]
    [InlineData("Testeur", true, "secret", true, 3, true)]
    public void Delete_requires_exact_name_secret_confirmation_and_active_delete_step(
        string name, bool confirmed, string answer, bool visible, int step, bool accepted)
    {
        var (vm, pick) = Picker();
        vm.DeleteTarget = new Character { Name = "Testeur", ServerId = 42 };
        vm.DeleteName = name;
        vm.DeleteConfirmed = confirmed;
        vm.DeleteAnswer = answer;
        vm.ServerPickVisible = visible;
        vm.ServerPickStep = step;

        vm.ConfirmSocketDeletionCommand.Execute(null);

        var deletion = Deletions.Take(vm.ServerPickLogin);
        Assert.Equal(accepted, pick.Task.IsCompleted);
        if (!accepted) Assert.Null(deletion);
        else
        {
            Assert.NotNull(deletion);
            Assert.Equal("Testeur", deletion.CharacterName);
            Assert.Equal("secret", deletion.Answer);
            Assert.Equal((42, (string?)null), pick.Task.Result);
            Assert.Equal("", vm.DeleteAnswer);
            Assert.Equal("", vm.DeleteName);
        }
    }
}
