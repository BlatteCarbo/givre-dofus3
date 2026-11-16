using GivreClone.Engine;
using Xunit;

namespace GivreClone.Tests;

public class ClientAutoPassPolicyTests
{
    [Fact]
    public void Visible_clients_automatic_finish_cannot_preempt_an_owned_turn() =>
        Assert.True(ClientAutoPassPolicy.Suppress(false, true, true, true, true, true));

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void Manual_or_unknown_finish_is_always_forwarded(bool? afk) =>
        Assert.False(ClientAutoPassPolicy.Suppress(false, true, true, true, true, afk));

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, false, true, true, true)]
    [InlineData(false, true, false, true, true)]
    [InlineData(false, true, true, false, true)]
    [InlineData(false, true, true, true, false)]
    public void Socket_unowned_closed_or_revoked_turns_are_untouched(
        bool socket, bool fight, bool open, bool owned, bool permitted) =>
        Assert.False(ClientAutoPassPolicy.Suppress(socket, fight, open, owned, permitted, true));
}
