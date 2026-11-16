using GivreClone.App.Services.ProtocolUpdates;
using Xunit;

namespace GivreClone.Tests;

public sealed class OfficialDofusReleaseProbeTests
{
    [Fact]
    public void Official_metadata_detects_newer_release_without_certifying_protocol()
    {
        const string json = """
        {"version":6,"name":"production","games":{"dofus":{"platforms":{"windows":{"dofus3":"6.0_3.7.12.12"}}}}}
        """;
        var state = OfficialDofusReleaseProbe.Parse(json);
        Assert.Equal("3.7.12.12", state.LatestVersion);
        Assert.True(state.IsNewerThan("3.7.11.11"));
        Assert.False(state.IsNewerThan("3.7.12.12"));
        Assert.False(state.IsNewerThan(null));
        Assert.False(state.ProtocolCertified);
        Assert.False(state.CanAutomaticallyUpgrade);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{not-json")]
    [InlineData("{\"games\":{\"dofus\":{\"platforms\":{\"windows\":{\"dofus3\":\"6.0_3.7.12.12;bad\"}}}}}")]
    [InlineData("{\"games\":{\"dofus\":{\"platforms\":{\"windows\":{\"dofus3\":\"3.7.12.12\"}}}}}")]
    [InlineData("{\"games\":{\"dofus\":{\"platforms\":{\"linux\":{\"dofus3\":\"6.0_3.7.12.12\"}}}}}")]
    public void Bad_or_non_windows_metadata_remains_unknown(string input)
    {
        var state = OfficialDofusReleaseProbe.Parse(input);
        Assert.Null(state.LatestVersion);
        Assert.False(state.ProtocolCertified);
    }
}
