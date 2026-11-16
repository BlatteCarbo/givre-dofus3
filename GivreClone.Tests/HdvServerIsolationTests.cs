extern alias F06App;
using System.Reflection;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvServerIsolationTests
{
    private static void Identify(Launch launch, uint pid, int serverId, string name) =>
        typeof(Launch).GetMethod("RememberServerIdentity", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(launch, [pid, serverId, name]);

    [Fact]
    public void Market_quote_seen_on_one_server_is_not_offered_after_the_session_moves_to_another()
    {
        var launch = new Launch();
        Identify(launch, 900u, 291, "Draconiros");
        launch.Hdv.SetMarketPrices(900u, 1519, new long[] { 120, 1100, 10500 });
        Assert.Equal(120, launch.Hdv.MarketFor(900u, 1519, 0));

        Identify(launch, 900u, 292, "Imagiro");

        Assert.Equal(-1, launch.Hdv.MarketFor(900u, 1519, 0));
    }

    [Fact]
    public void Same_server_reidentification_keeps_the_observed_quote()
    {
        var launch = new Launch();
        Identify(launch, 901u, 291, "Draconiros");
        launch.Hdv.SetMarketPrices(901u, 1519, new long[] { 120, 1100, 10500 });

        Identify(launch, 901u, 291, "Draconiros");

        Assert.Equal(120, launch.Hdv.MarketFor(901u, 1519, 0));
    }
}
