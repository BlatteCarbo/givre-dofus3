using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class LocalCloneRunNameTests
{
    [Fact]
    public void Default_storage_keeps_its_existing_name()
    {
        Assert.Equal("givre-app-offline", MZP19LocalClone.ValidateRunName(null));
        Assert.Equal("givre-app-offline", MZP19LocalClone.ValidateRunName(""));
        Assert.Equal("dakal-validation_20261024", MZP19LocalClone.ValidateRunName("dakal-validation_20261024"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../other")]
    [InlineData("C:\\other")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(" name")]
    [InlineData("name.")]
    public void Run_name_cannot_redirect_storage_outside_test_runs(string name)
        => Assert.Throws<ArgumentException>(() => MZP19LocalClone.ValidateRunName(name));
}
