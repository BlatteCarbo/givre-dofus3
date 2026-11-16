extern alias F06App;

using Xunit;
using MZP19LocalClone = F06App::GivreClone.App.Services.MZP19LocalClone;

namespace GivreClone.Tests;

public sealed class TestStorageIsolationTests
{
    [Fact]
    public void App_userdata_is_isolated_to_this_testhost()
    {
        string runName = Assert.IsType<string>(Environment.GetEnvironmentVariable("GIVRE_CLONE_RUN_NAME"));
        string prefix = $"givre-tests-{Environment.ProcessId}-";
        Assert.StartsWith(prefix, runName);
        Assert.True(Guid.TryParseExact(runName[prefix.Length..], "N", out _));
        Assert.Equal(runName, MZP19LocalClone.ValidateRunName(runName));
        Assert.Equal(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GivreReplica", "TestRuns", runName, "userdata"), MZP19LocalClone.DataRoot);
    }
}
