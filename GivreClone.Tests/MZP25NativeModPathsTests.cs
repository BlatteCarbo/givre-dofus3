using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class MZP25NativeModPathsTests
{
    [Fact]
    public void Clone_paths_are_private_and_not_in_the_original_product_root()
    {
        var clone = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "test-owned-run"));
        var local = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "local-appdata"));
        var controls = MZP25NativeModPaths.Controls(true, clone, local);
        var turbo = MZP25NativeModPaths.Turbo(12345, true, clone, local);
        Assert.Equal(Path.Combine(clone, "ModsControl"), controls);
        Assert.Equal(Path.Combine(clone, "NativeTurbo", "12345"), turbo);
        Assert.False(controls.StartsWith(local + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.False(turbo.StartsWith(local + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Historical_layout_is_unchanged_for_non_clone()
    {
        var clone = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "clone"));
        var local = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "local"));
        Assert.Equal(Path.Combine(local, "GivreLauncher"), MZP25NativeModPaths.Controls(false, clone, local));
        Assert.Equal(Path.Combine(local, "GivreClone", "NativeTurbo", "71"),
            MZP25NativeModPaths.Turbo(71, false, clone, local));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-91)]
    public void Invalid_pid_is_rejected(int pid)
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            MZP25NativeModPaths.Turbo(pid, true, Path.GetTempPath(), Path.GetTempPath()));

    [Fact]
    public void Relative_root_is_not_accepted()
        => Assert.Throws<ArgumentException>(() =>
            MZP25NativeModPaths.Controls(true, "../relative", Path.GetTempPath()));
}
