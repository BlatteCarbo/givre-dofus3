extern alias F06App;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvBuyCeilingTests
{
    [Theory]
    [InlineData(3001, 3000, true)]
    [InlineData(3000, 3000, false)]
    [InlineData(2999, 3000, false)]
    [InlineData(999_999, 0, false)]
    public void Lot_above_an_explicit_ceiling_is_refused_and_zero_means_no_ceiling(long lotPrice, long maxPrice, bool refused)
        => Assert.Equal(refused, Launch.ExceedsBuyCeiling(lotPrice, maxPrice));
}
