using System.Threading.Tasks;
using GivreClone.App.Services.DofusDude;
using Xunit;

namespace GivreClone.Tests;

public class DofusDudeClientTests
{
    [Fact]
    public async Task RouteHdvMap_Routes_Correctly_By_Item_Level()
    {
        // GID 969 = Couronne du Wa Wabbit (Level 60) -> Astrub (191104004)
        long mapAstrub = await DofusDudeClient.RouteHdvMapAsync(969);
        Assert.Equal(191104004L, mapAstrub);

        // Objet inconnu ou <= 60 par defaut -> Astrub
        long mapDefault = await DofusDudeClient.RouteHdvMapAsync(0);
        Assert.Equal(191104004L, mapDefault);
    }

    [Fact]
    public async Task GetTodayAlmanax_Returns_Valid_Tribute_Or_Fallback()
    {
        var almanax = await DofusDudeClient.GetTodayAlmanaxAsync();
        if (almanax is not null)
        {
            Assert.NotEmpty(almanax.Date);
            Assert.True(almanax.ItemGid >= 0);
        }
    }
}
