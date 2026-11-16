using GivreClone.App.Services.LocalLaunch;
using Xunit;

namespace GivreClone.Tests;

public class ItemOverlayLaunchTests
{
    [Fact]
    public void Authorized_child_receives_local_overlay_settings_filename()
    {
        using var authority = new LocalZaapAuthority(40001, 40002, "Dofus:127.0.0.1:1")
        { ItemOverlaySettingsPath = @"C:\isolated fixture\settings.tsv" };
        string session = Guid.NewGuid().ToString();
        authority.Expect(session);
        var start = authority.CreateAuthorizedStartInfo(@"C:\fixture\Dofus.exe", new string('a', 64), session, 1, null);
        Assert.NotNull(start);
        Assert.Equal(authority.ItemOverlaySettingsPath, start.Environment["GIVRE_ITEM_OVERLAY_SETTINGS"]);
        Assert.False(start.UseShellExecute);
    }
}
