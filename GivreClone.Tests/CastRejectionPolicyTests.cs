using GivreClone.App.Services.CombatParity;
using Xunit;
namespace GivreClone.Tests;
public class CastRejectionPolicyTests
{
    [Theory]
    [InlineData(170, "2", "3", true, 2)]
    [InlineData(170, "0", "3", true, 0)]
    [InlineData(65, "2", "3", false, 0)]
    [InlineData(170, "3", "3", false, 3)]
    [InlineData(170, "4", "3", false, 4)]
    [InlineData(170, "-1", "3", false, 0)]
    [InlineData(170, "x", "3", false, 0)]
    [InlineData(170, "2", "999999999999999999999", false, 2)]
    public void Only_an_explicit_insufficient_ap_response_resolves_a_cast(int id, string ap, string cost, bool rejected, int available)
    {
        Assert.Equal(rejected, CastRejectionPolicy.TryRead(id, new[] { ap, cost }, out int result));
        if (rejected) Assert.Equal(available, result);
    }
    [Fact]
    public void Incomplete_messages_do_not_release_the_barrier()
    {
        Assert.False(CastRejectionPolicy.TryRead(170, new[] { "2" }, out _));
        Assert.False(CastRejectionPolicy.TryRead(170, new[] { "2", "3", "other" }, out _));
    }
}
