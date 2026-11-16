extern alias F06App;

using System.Runtime.CompilerServices;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using Xunit;

namespace GivreClone.Tests;

public sealed class ForgeAdmissionRegressionTests
{
    [Theory]
    [InlineData(false, 0, "limite d'étapes")]
    [InlineData(false, -1, "limite d'étapes")]
    [InlineData(true, 10, "annulé")]
    public async Task No_workshop_or_transport_is_used_when_no_rune_may_be_applied(bool cancel, int limit, string reason)
    {
        var service = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        var report = await service.ForgeAsync(101, 1, 1, 1,
            (_, _) => throw new InvalidOperationException("Decision must not run"),
            limit, new CancellationToken(cancel));
        Assert.False(report.Opened);
        Assert.Empty(report.Steps);
        Assert.Equal(reason, report.Stop);
    }
}
