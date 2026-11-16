using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public sealed class MovementEchoPolicyTests
{
    // Requested path from the 22:23:10 trace, shortened by the server at its second cell.
    private static readonly int[] Requested = [128,143,171,185,200,214,229,243,258,272,287,301,329,344,358,373,387,402,403,404,390,377,363];

    [Fact]
    public void Server_prefix_must_be_confirmed_even_when_destination_was_not_reached()
        => Assert.True(MovementEchoPolicy.Matches(Requested, new[] { 128, 143 }));

    [Fact]
    public void Same_destination_may_use_a_different_server_route()
        => Assert.True(MovementEchoPolicy.Matches(Requested, new[] { 128, 156, 363 }));

    [Theory]
    [InlineData(128, 156)] // different destination and not a prefix
    [InlineData(100, 363)] // different source
    [InlineData(128, 560)] // invalid cell
    public void Unrelated_or_invalid_echo_does_not_authorize_a_confirmation(int source, int target)
        => Assert.False(MovementEchoPolicy.Matches(Requested, new[] { source, target }));

    [Fact]
    public void Empty_or_stationary_echo_does_not_authorize_a_confirmation()
    {
        Assert.False(MovementEchoPolicy.Matches(Requested, null));
        Assert.False(MovementEchoPolicy.Matches(Requested, new[] { 128 }));
    }
}
