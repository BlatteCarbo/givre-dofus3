extern alias F06App;

using Xunit;
using World = F06App::GivreClone.App.Services.BotWorldState;

namespace GivreClone.Tests;

public sealed class WorldReadinessAuthorityTests
{
    [Fact]
    public void Predicted_cell_on_current_map_is_not_server_ready()
    {
        var state = new World { MapId = 191105026, SelfCell = 233, SelfCellMap = 191105026,
            SelfCellConfirmed = false };

        Assert.False(state.SelfReadyFor(191105026));
        Assert.Equal(0, state.RpZoneFor(191105026));

        state.SelfCellConfirmed = true;
        Assert.True(state.SelfReadyFor(191105026));
        Assert.False(state.SelfReadyFor(191105027));
        state.SelfCellConfirmed = false;
        Assert.False(state.SelfReadyFor(191105026));
    }
}
