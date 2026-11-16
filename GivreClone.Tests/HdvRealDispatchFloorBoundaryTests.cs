extern alias F06App;

using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Exercise the actual LaunchService pre-send guard shared by manual/Bulk,
/// Lua and the production listing adapter. No game or network is used.
/// </summary>
public sealed class HdvRealDispatchFloorBoundaryTests
{
    [Fact]
    public void Shared_launch_boundary_refuses_manual_or_script_price_below_observed_average_floor()
    {
        var launch = new LaunchService();
        launch.Hdv.SetAverage(9, 7342, 100);

        Assert.Contains("plancher", launch.HdvSalePriceRefusal(9, 7342, 10, 499)!);
        Assert.Null(launch.HdvSalePriceRefusal(9, 7342, 10, 500));
        Assert.Null(launch.HdvSalePriceRefusal(9, 7342, 10, 1000));
        // Unknown average retains the historical manual-price policy.
        Assert.Null(launch.HdvSalePriceRefusal(9, 7343, 10, 1));
    }

    [Fact]
    public void Nonrepresentable_average_lot_floor_refuses_at_real_boundary()
    {
        var launch = new LaunchService();
        launch.Hdv.SetAverage(9, 7342, long.MaxValue);
        Assert.Contains("non représentable",
            launch.HdvSalePriceRefusal(9, 7342, 1000, long.MaxValue)!);
    }

    [Fact]
    public void Cross_character_and_gid_data_cannot_supply_a_foreign_floor()
    {
        var launch = new LaunchService();
        launch.Hdv.SetAverage(9, 7342, 100);
        Assert.Contains("plancher", launch.HdvSalePriceRefusal(9, 7342, 10, 499)!);
        Assert.Null(launch.HdvSalePriceRefusal(10, 7342, 10, 1));
        Assert.Null(launch.HdvSalePriceRefusal(9, 7343, 10, 1));
        Assert.Contains("GID", launch.HdvSalePriceRefusal(9, (long)int.MaxValue + 1, 1, 100)!);
    }
}
