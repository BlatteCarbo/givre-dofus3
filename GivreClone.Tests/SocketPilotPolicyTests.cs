using GivreClone.App.Services.ProtocolUpdates;
using Xunit;

namespace GivreClone.Tests;

public sealed class SocketPilotPolicyTests
{
    [Theory]
    [InlineData("3.7.12.12", "3.7.12.12", true)]
    [InlineData("3.7.13.1", "3.7.12.12", false)]   // nouveau patch publié : retour en revue
    [InlineData(null, "3.7.12.12", false)]         // Cytrus muet : fermé
    [InlineData("", "", false)]
    [InlineData("3.7.12.12", null, false)]
    public void Verified_only_when_latest_equals_installed(string? latest, string? installed, bool expected)
        => Assert.Equal(expected, SocketPilotPolicy.AliasesVerified(latest, installed));

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Reviewed_gameplay_needs_both_operator_review_and_matching_cytrus_version(
        bool reviewed, bool aliasesVerified, bool expected)
        => Assert.Equal(expected, SocketPilotPolicy.GrantsReviewedGameplay(reviewed, aliasesVerified));

    [Theory]
    [InlineData(false, true, "3.7.11.11", "3.7.13.17", true)]
    [InlineData(true, true, "3.7.11.11", "3.7.12.12", true)]
    [InlineData(false, true, "3.7.13.17", "3.7.13.17", false)]
    [InlineData(false, false, "3.7.11.11", "3.8.0.1", false)]
    [InlineData(false, true, "3.7.11.11", null, false)]
    public void Admitted_install_on_another_build_is_a_cross_patch_session(
        bool reviewedCandidate, bool admitted, string? mapping, string? installed, bool expected)
        => Assert.Equal(expected, SocketPilotPolicy.IsCrossPatchSession(reviewedCandidate, admitted, mapping, installed));
}
