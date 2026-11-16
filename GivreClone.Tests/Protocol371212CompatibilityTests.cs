using System.Security.Cryptography;
using System.Text;
using GivreClone.App.Services.ProtocolUpdates;
using Xunit;

namespace GivreClone.Tests;

public sealed class Protocol371212CompatibilityTests
{
    private static readonly Protocol371212Evidence FullyVerifiedFixture = new(
        TargetVersion: "3.7.12.12", InstalledVersion: "3.7.12.12",
        BundleVersion: "3.7.12.12", ReferenceVersion: "3.7.11.11",
        OfficialVersionAdvertised: true,
        TargetProtoManifestAndShaVerified: true,
        BaselineProtoManifestAndShaVerified: true,
        TargetClientBinariesMatchManifest: true,
        ExactTrustedTargetBinaryBaselineMatch: true,
        IndependentWireDispatchVerified: true,
        IndependentSerializationRoundTripVerified: true,
        IndependentServerAckVerified: true,
        IndependentEvidenceAdmittedByTrustedGate: true);

    [Fact]
    public void Published_metadata_does_not_make_uninstalled_target_compatible()
    {
        var decision = Protocol371212Compatibility.Evaluate(FullyVerifiedFixture with
        {
            InstalledVersion = "3.7.11.11",
            BundleVersion = "3.7.11.11",
            TargetProtoManifestAndShaVerified = false
        });

        Assert.Equal(Protocol371212Status.BLOCK, decision.Status);
        Assert.True(decision.CanInspectOffline);
        Assert.False(decision.CanUseRuntime);
        Assert.False(decision.ProtoChangedProven);
        Assert.False(decision.MayUpgradeAutomatically);
        Assert.False(decision.MayPromoteCodecAutomatically);
    }

    [Fact]
    public void Missing_official_metadata_blocks_claims_of_safe_compatibility()
    {
        var decision = Protocol371212Compatibility.Evaluate(FullyVerifiedFixture with
        { OfficialVersionAdvertised = false });
        Assert.Equal(Protocol371212Status.BLOCK, decision.Status);
        Assert.False(decision.CanInspectOffline);
        Assert.False(decision.CanUseRuntime);
    }

    [Theory]
    [InlineData("3.7.11.11")]
    [InlineData("3.7.10.10")]
    [InlineData("3.7.12.11")]
    [InlineData("3.7.12.13")]
    public void Same_37_family_or_adjacent_point_release_is_not_accepted(string installed)
    {
        Assert.Equal(Protocol371212Status.BLOCK,
            Protocol371212Compatibility.Evaluate(FullyVerifiedFixture with { InstalledVersion = installed }).Status);
    }

    [Fact]
    public void Stale_bundle_does_not_become_safe_if_target_installed()
    {
        Assert.Equal(Protocol371212Status.BLOCK,
            Protocol371212Compatibility.Evaluate(FullyVerifiedFixture with { BundleVersion = "3.7.11.11" }).Status);
    }

    [Fact]
    public void Wrong_reference_version_is_rejected_even_if_same_family()
    {
        Assert.Equal(Protocol371212Status.BLOCK,
            Protocol371212Compatibility.Evaluate(FullyVerifiedFixture with { ReferenceVersion = "3.7.10.10" }).Status);
    }

    [Theory]
    [InlineData("target-manifest")]
    [InlineData("baseline-manifest")]
    [InlineData("client-sha")]
    public void Candidate_without_verified_manifest_or_binary_identity_is_blocked(string missing)
    {
        var evidence = missing switch
        {
            "target-manifest" => FullyVerifiedFixture with { TargetProtoManifestAndShaVerified = false },
            "baseline-manifest" => FullyVerifiedFixture with { BaselineProtoManifestAndShaVerified = false },
            "client-sha" => FullyVerifiedFixture with { TargetClientBinariesMatchManifest = false },
            _ => throw new InvalidOperationException()
        };
        var decision = Protocol371212Compatibility.Evaluate(evidence);
        Assert.Equal(Protocol371212Status.BLOCK, decision.Status);
        Assert.False(decision.CanUseRuntime);
    }

    [Theory]
    [InlineData("trusted-baseline")]
    [InlineData("dispatch")]
    [InlineData("roundtrip")]
    [InlineData("server-ack")]
    [InlineData("admission")]
    public void Verified_candidate_without_independent_runtime_receipt_needs_review(string missing)
    {
        var evidence = missing switch
        {
            "trusted-baseline" => FullyVerifiedFixture with { ExactTrustedTargetBinaryBaselineMatch = false },
            "dispatch" => FullyVerifiedFixture with { IndependentWireDispatchVerified = false },
            "roundtrip" => FullyVerifiedFixture with { IndependentSerializationRoundTripVerified = false },
            "server-ack" => FullyVerifiedFixture with { IndependentServerAckVerified = false },
            "admission" => FullyVerifiedFixture with { IndependentEvidenceAdmittedByTrustedGate = false },
            _ => throw new InvalidOperationException()
        };
        var decision = Protocol371212Compatibility.Evaluate(evidence);
        Assert.Equal(Protocol371212Status.REVIEW, decision.Status);
        Assert.False(decision.CanUseRuntime);
        Assert.False(decision.ProtoChangedProven);
        Assert.True(decision.CanInspectOffline);
    }

    [Fact]
    public void Fully_independently_admitted_fixture_is_safe_only_for_exact_target()
    {
        var decision = Protocol371212Compatibility.Evaluate(FullyVerifiedFixture);
        Assert.Equal(Protocol371212Status.SAFE, decision.Status);
        Assert.True(decision.CanUseRuntime);
        Assert.False(decision.MayUpgradeAutomatically);
        Assert.False(decision.MayPromoteCodecAutomatically);
        Assert.False(decision.ProtoChangedProven);
        Assert.Equal(Protocol371212Status.BLOCK,
            Protocol371212Compatibility.Evaluate(FullyVerifiedFixture with { TargetVersion = "3.7.13.13" }).Status);
    }

    [Fact]
    public void Missing_target_snapshot_prevents_claiming_schema_equality()
    {
        using var fixture = new NativeFixture();
        fixture.WriteReference();
        var evidence = ProtocolSchemaEvidenceProbe.Inspect(fixture.IndexerRoot, "3.7.11.11", "3.7.12.12");
        Assert.False(evidence.SchemaUnchangedProven);
        Assert.False(evidence.SchemaChangedCandidate);
        Assert.False(evidence.CurrentClientBound);
        Assert.False(evidence.ProtoChangedProven);
        Assert.Null(evidence.TargetProtoSha256);
    }

    [Fact]
    public void Matching_existing_reference_cannot_be_used_as_a_substitute_for_missing_target()
    {
        using var fixture = new NativeFixture();
        fixture.WriteReference();
        var evidence = ProtocolSchemaEvidenceProbe.Inspect(fixture.IndexerRoot, "3.7.11.11", "3.7.11.11");
        Assert.True(evidence.SchemaUnchangedProven);
        Assert.False(evidence.ProtoChangedProven);
        // The policy rejects the wrong target even if a caller supplies all proof flags.
        Assert.Equal(Protocol371212Status.BLOCK,
            Protocol371212Compatibility.Evaluate(FullyVerifiedFixture with { TargetVersion = "3.7.11.11" }).Status);
    }

    private sealed class NativeFixture : IDisposable
    {
        public string IndexerRoot { get; } = Path.Combine(Path.GetTempPath(), "protocol-371212-" + Guid.NewGuid().ToString("N"));

        public void WriteReference()
        {
            const string version = "3.7.11.11";
            const string proto = "message Typed { optional int32 id = 1; }                              ";
            string directory = Path.Combine(IndexerRoot, "local-output", "historical-extractions-v3", version);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "dofus-" + version + ".proto"), proto);
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proto)));
            string binarySha = new('A', 64);
            string json = System.Text.Json.JsonSerializer.Serialize(new
            {
                format = ProtocolSchemaEvidenceProbe.NativeCandidateFormat,
                version,
                certified = false,
                review_only = true,
                protocol_promotion = false,
                proto_size = Encoding.UTF8.GetByteCount(proto),
                proto_sha256 = hash,
                counts = new { messages = 1, fields = 1, nonempty_messages = 1 },
                source = new
                {
                    candidate_sha256 = hash,
                    client_sha256 = new
                    {
                        dofusExeSha256 = binarySha,
                        gameAssemblySha256 = binarySha,
                        globalMetadataSha256 = binarySha
                    }
                }
            });
            File.WriteAllText(Path.Combine(directory, "manifest.json"), json);
        }

        public void Dispose()
        {
            if (Directory.Exists(IndexerRoot)) Directory.Delete(IndexerRoot, true);
        }
    }
}
