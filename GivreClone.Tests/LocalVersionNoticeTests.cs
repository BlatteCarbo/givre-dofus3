using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class LocalVersionNoticeTests
{
    [Theory]
    [InlineData("3.7.11.11", "3.7.11.11", false)]
    [InlineData("3.7.12.12", "3.7.11.11", false)]
    [InlineData("3.7.99.999", "3.7.11.11", false)]
    [InlineData("3.8.0.1", "3.7.11.11", true)]
    [InlineData(null, "3.7.11.11", true)]
    [InlineData("3.7.11.11", null, true)]
    [InlineData("3.7.11.11", "garbage", true)]
    public void Attention_depends_on_protocol_family(string? installed, string? declared, bool attention)
        => Assert.Equal(attention, LocalVersionNotice.Compare(installed, declared).RequiresAttention);

    [Fact]
    public void Reviewed_socket_candidate_clears_false_update_banner_but_does_not_certify_gameplay()
    {
        var reviewed = new MZP12SocketVersionBinding.Decision(
            MZP12SocketVersionBinding.Refusal.ReviewedCrossPatchCandidate,
            "Revue locale, pas une preuve d'ACK.");
        var notice = LocalVersionNotice.Compare("3.7.12.12", "3.7.11.11", reviewed);
        Assert.False(notice.RequiresAttention);
        Assert.False(LocalVersionNotice.Compare("3.7.13.13", "3.7.11.11").RequiresAttention);
        Assert.False(LocalVersionNotice.Compare("3.7.12.12", "3.7.11.11",
            new(MZP12SocketVersionBinding.Refusal.MappingBuildMismatch, "refus"))
            .RequiresAttention);
    }

    [Fact]
    public void Reviewed_candidate_does_not_require_binary_hash_review_for_protocol()
    {
        var reviewed = new MZP12SocketVersionBinding.Decision(
            MZP12SocketVersionBinding.Refusal.ReviewedCrossPatchCandidate,
            "Revue protocole candidate.");
        var drift = LocalVersionNotice.Compare("3.7.12.12", "3.7.11.11",
            reviewed, installedHashesChanged: true);
        Assert.False(drift.RequiresAttention);
    }

    [Theory]
    [InlineData("3.7.12.12", "3.7.12.12")]
    [InlineData("3.7.13.13", "3.7.11.11")]
    public void Binary_hash_drift_does_not_change_protocol_family(
        string installed, string bundle)
        => Assert.False(LocalVersionNotice.Compare(installed, bundle,
            installedHashesChanged: true).RequiresAttention);

    [Fact]
    public void Previous_patch_review_does_not_restrict_other_patches_in_the_family()
    {
        var reviewed = new MZP12SocketVersionBinding.Decision(
            MZP12SocketVersionBinding.Refusal.ReviewedCrossPatchCandidate,
            "Cible initiale vérifiée.");
        var refusal = new MZP12SocketVersionBinding.Decision(
            MZP12SocketVersionBinding.Refusal.MappingBuildMismatch, "Autre version.");
        Assert.False(LocalVersionNotice.Compare("3.7.12.12", "3.7.11.11", reviewed).RequiresAttention);
        Assert.False(LocalVersionNotice.Compare("3.7.13.13", "3.7.11.11", refusal).RequiresAttention);
    }

    [Fact]
    public void Historic_observation_is_superseded_only_by_a_current_SHA_review_not_a_trusted_hash_error()
    {
        var reviewed = new MZP12SocketVersionBinding.Decision(
            MZP12SocketVersionBinding.Refusal.ReviewedCrossPatchCandidate, "Hash épinglé du client courant.");
        Assert.False(LocalVersionNotice.HasUnresolvedHashDrift(
            historicalChanged: true, trustedHashChanged: false, currentAdmission: reviewed));
        Assert.True(LocalVersionNotice.HasUnresolvedHashDrift(
            historicalChanged: true, trustedHashChanged: true, currentAdmission: reviewed));
        Assert.True(LocalVersionNotice.HasUnresolvedHashDrift(
            historicalChanged: true, trustedHashChanged: false, currentAdmission: null));
        Assert.False(LocalVersionNotice.HasUnresolvedHashDrift(
            historicalChanged: false, trustedHashChanged: false, currentAdmission: null));
    }

    [Theory]
    [InlineData("{\"build\":\"3.7.11.11\"}", "3.7.11.11")]
    [InlineData("{\"build\":12}", null)]
    [InlineData("[]", null)]
    [InlineData("{", null)]
    public void Declaration_requires_string_property(string json, string? expected)
    {
        var path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, json);
            Assert.Equal(expected, LocalVersionNotice.ReadDeclaredBuild(path));
        }
        finally { System.IO.File.Delete(path); }
    }
}
