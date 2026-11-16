using GivreClone.App.Services.UiNavigation;
using Xunit;

namespace GivreClone.Tests;

public class WindowSizePolicyTests
{
    [Fact]
    public void Nothing_saved_keeps_the_default_size()
        => Assert.Equal((0d, 0d), WindowSizePolicy.Restore(0, 0, 960, 600, 1920, 1040));

    [Fact]
    public void Saved_size_that_fits_is_restored_exactly()
        => Assert.Equal((1200d, 800d), WindowSizePolicy.Restore(1200, 800, 960, 600, 1920, 1040));

    [Fact]
    public void Saved_size_larger_than_the_screen_is_shrunk_to_the_screen()
        => Assert.Equal((1366d, 728d), WindowSizePolicy.Restore(1920, 1040, 960, 600, 1366, 728));

    [Fact]
    public void Saved_size_never_goes_below_the_window_minimum()
        => Assert.Equal((960d, 600d), WindowSizePolicy.Restore(500, 300, 960, 600, 1920, 1040));
}
