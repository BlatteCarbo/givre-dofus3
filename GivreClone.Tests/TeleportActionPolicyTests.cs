using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class TeleportActionPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Supported_list_requires_matching_fresh_dialog_and_listed_destination(int type)
    {
        Assert.True(TeleportActionPolicy.CanSend(type, type, 100, 100, 200, true));
        Assert.False(TeleportActionPolicy.CanSend(type, type, 100, 101, 200, true));
        Assert.False(TeleportActionPolicy.CanSend(type, type, 100, 100, 201, false));
        Assert.False(TeleportActionPolicy.CanSend(type, 4, 100, 100, 200, true));
        Assert.False(TeleportActionPolicy.CanSend(type, type, 0, 0, 200, true));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(-1)]
    [InlineData(99)]
    public void Unproved_types_are_never_allowed_even_if_server_advertised_a_map(int type)
        => Assert.False(TeleportActionPolicy.CanSend(type, type, 100, 100, 200, true));
}
