extern alias F06App;
using System.Reflection;
using Accounts = F06App::GivreClone.App.ViewModels.AccountsViewModel;
using Picks = F06App::GivreClone.App.Services.Transport.Socket.SocketScriptedPickRegistry;
using Xunit;

namespace GivreClone.Tests;

public sealed class McpLaunchControlTests
{
    private static Task<bool> Launch(string login, string? character, string? server, Func<Task<bool>> launch)
        => (Task<bool>)typeof(Accounts).GetMethod("LaunchWithServerPickAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [login, character, server, launch])!;

    [Theory]
    [InlineData("Mikhal", "Bastionbot")]
    [InlineData("  291  ", " Bastionbot ")]
    [InlineData("Mikhal", null)]
    public async Task Explicit_server_and_character_are_available_to_the_launch(string server, string? character)
    {
        string login = Guid.NewGuid().ToString("N");
        Assert.True(await Launch(login, character, server, () =>
        {
            var pick = Picks.Take(login);
            Assert.NotNull(pick);
            Assert.Equal(server.Trim(), pick.Server);
            Assert.Equal(character?.Trim(), pick.Character);
            return Task.FromResult(true);
        }));
        Assert.False(Picks.Has(login));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refused_or_throwing_launch_cannot_leave_a_pick_for_a_later_launch(bool throws)
    {
        string login = Guid.NewGuid().ToString("N");
        Task<bool> Attempt() => Launch(login, "Bastionbot", "Mikhal", () =>
            throws ? Task.FromException<bool>(new InvalidOperationException()) : Task.FromResult(false));
        if (throws) await Assert.ThrowsAsync<InvalidOperationException>(Attempt);
        else Assert.False(await Attempt());
        Assert.False(Picks.Has(login));
    }
}
