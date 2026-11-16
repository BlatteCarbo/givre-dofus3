using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GivreClone.App.Services.ProtocolUpdates;
using Xunit;

namespace GivreClone.Tests;

public sealed class ProtocolUpdateProbeTests
{
    private static readonly ProtocolArtifactHashes Original = new(
        Sha("exe"), Sha("assembly"), Sha("metadata"), Sha("version"));
    private static readonly ProtocolArtifactHashes Changed = new(
        Sha("exe changed"), Sha("assembly"), Sha("metadata"), Sha("version"));
    private static readonly ProtocolTrustedBaseline Trusted =
        new("3.7.11.11", Original, "receipt-r1", true);

    [Fact]
    public void Identical_version_and_four_hashes_need_no_drift_review()
    {
        var result = ProtocolUpdateProbe.Compare("3.7.11.11", Original, "3.7.11.11", Trusted);
        Assert.Equal(ProtocolUpdateStatus.UNCHANGED, result.Status);
        Assert.False(result.NeedsReview);
        Assert.False(result.CanRun);
        Assert.False(result.ProtoChangedProven);
    }

    [Fact]
    public void Version_drift_takes_priority_over_binary_hashes()
    {
        var result = ProtocolUpdateProbe.Compare("3.7.12.12", Changed, "3.7.11.11", Trusted);
        Assert.Equal(ProtocolUpdateStatus.VERSION_CHANGED, result.Status);
        Assert.True(result.NeedsReview);
        Assert.True(result.CanRun);
        Assert.False(result.ProtoChangedProven);
    }

    [Fact]
    public void Same_version_with_changed_binary_is_hash_changed_not_proven_wire_change()
    {
        var result = ProtocolUpdateProbe.Compare("3.7.11.11", Changed, "3.7.11.11", Trusted);
        Assert.Equal(ProtocolUpdateStatus.HASH_CHANGED, result.Status);
        Assert.True(result.NeedsReview);
        Assert.True(result.CanRun);
        Assert.False(result.ProtoChangedProven);
    }

    [Fact]
    public void Version_equality_without_trusted_binary_baseline_remains_unknown_but_reviewable()
    {
        var result = ProtocolUpdateProbe.Compare("3.7.11.11", Changed, "3.7.11.11", null);
        Assert.Equal(ProtocolUpdateStatus.UNKNOWN, result.Status);
        Assert.True(result.CanRun);
        Assert.True(result.NeedsReview);
        Assert.False(result.ProtoChangedProven);
    }

    [Fact]
    public void Untrusted_receipt_is_ignored_even_if_hashes_match()
    {
        var result = ProtocolUpdateProbe.Compare("3.7.11.11", Original, "3.7.11.11",
            Trusted with { IsTrusted = false });
        Assert.Equal(ProtocolUpdateStatus.UNKNOWN, result.Status);
    }

    [Fact]
    public void Missing_or_invalid_bundle_version_is_unknown_even_with_matching_baseline()
    {
        var result = ProtocolUpdateProbe.Compare("3.7.11.11", Original, null, Trusted);
        Assert.Equal(ProtocolUpdateStatus.UNKNOWN, result.Status);
        Assert.True(result.CanRun);
    }

    [Theory]
    [InlineData("3.7.11")]
    [InlineData("3.7.11.11.4")]
    [InlineData("3.7.abc.11")]
    [InlineData("3.7.011.11")]
    [InlineData("6.0_3.7.11.11")]
    public void Invalid_version_is_unknown_without_a_runnable_plan(string version)
    {
        var result = ProtocolUpdateProbe.Compare(version, Original, "3.7.11.11", Trusted);
        Assert.Equal(ProtocolUpdateStatus.UNKNOWN, result.Status);
        Assert.False(result.CanRun);
    }

    [Fact]
    public void Scanner_reads_real_multiline_version_marker_without_running_client()
    {
        using var client = new TestClient("Version=3.7.11.11\r\nBuildDate=2026-10-16\r\nConfigUrl=https://example.invalid\r\n");
        var observed = ProtocolUpdateProbe.Inspect(client.Executable, "3.7.11.11");

        Assert.Equal(ProtocolUpdateStatus.UNKNOWN, observed.Status);
        Assert.Equal("3.7.11.11", observed.InstalledVersion);
        Assert.Equal(ShaBytes(File.ReadAllBytes(client.Executable)), observed.InstalledSnapshot!.DofusExeSha256);
        Assert.Equal(ShaBytes(File.ReadAllBytes(client.Marker)), observed.InstalledSnapshot.VersionMarkerSha256);
        Assert.True(observed.CanRun);
        Assert.False(observed.ProtoChangedProven);
    }

    [Theory]
    [InlineData("Version=3.7.11.11\nBuildDate=test\nVersion=3.7.12.12")]
    [InlineData("BuildDate=test\nVersion=3.7.11.11")]
    [InlineData("Version=hello\nBuildDate=test")]
    public void Ambiguous_or_invalid_markers_are_rejected(string marker)
    {
        using var client = new TestClient(marker);
        var observed = ProtocolUpdateProbe.Inspect(client.Executable, "3.7.11.11");
        Assert.Equal(ProtocolUpdateStatus.UNKNOWN, observed.Status);
        Assert.Null(observed.InstalledSnapshot);
        Assert.False(observed.CanRun);
    }

    [Fact]
    public void Deleting_metadata_or_changing_executable_filename_is_unknown()
    {
        using var client = new TestClient("Version=3.7.11.11");
        Assert.Equal(ProtocolUpdateStatus.UNKNOWN, ProtocolUpdateProbe.Inspect(
            Path.Combine(client.Root, "Wrong.exe"), "3.7.11.11").Status);
        File.Delete(client.Metadata);
        var result = ProtocolUpdateProbe.Inspect(client.Executable, "3.7.11.11");
        Assert.Equal(ProtocolUpdateStatus.UNKNOWN, result.Status);
        Assert.False(result.CanRun);
    }

    [Fact]
    public void Comparing_a_fixture_against_its_own_trusted_hashes_succeeds()
    {
        using var client = new TestClient("Version=3.7.11.11\nBuildDate=test");
        var initial = ProtocolUpdateProbe.Inspect(client.Executable, "3.7.11.11");
        var trusted = new ProtocolTrustedBaseline("3.7.11.11", initial.InstalledSnapshot!, "fixture-proof", true);
        Assert.Equal(ProtocolUpdateStatus.UNCHANGED, ProtocolUpdateProbe.Inspect(
            client.Executable, "3.7.11.11", trusted).Status);
        File.WriteAllText(client.Metadata, "changed");
        Assert.Equal(ProtocolUpdateStatus.HASH_CHANGED, ProtocolUpdateProbe.Inspect(
            client.Executable, "3.7.11.11", trusted).Status);
    }

    [Fact]
    public void BuildDate_change_at_same_version_changes_marker_sha_without_proving_proto_change()
    {
        using var client = new TestClient("Version=3.7.11.11\nBuildDate=2026-10-16");
        var baselineHashes = ProtocolUpdateProbe.Inspect(client.Executable, "3.7.11.11").InstalledSnapshot!;
        File.WriteAllText(client.Marker, "Version=3.7.11.11\nBuildDate=2026-10-20");
        var result = ProtocolUpdateProbe.Inspect(client.Executable, "3.7.11.11",
            new ProtocolTrustedBaseline("3.7.11.11", baselineHashes, "test-only", true));
        Assert.Equal(ProtocolUpdateStatus.HASH_CHANGED, result.Status);
        Assert.False(result.ProtoChangedProven);
    }

    private static string Sha(string value) => ShaBytes(Encoding.UTF8.GetBytes(value));
    internal static string ShaBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class TestClient : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Givre-ProtocolUpdate-" + Guid.NewGuid().ToString("N"));
        public string Executable => Path.Combine(Root, "Dofus.exe");
        public string Metadata => Path.Combine(Root, "Dofus_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
        public string Marker => Path.Combine(Root, "Dofus_Data", "StreamingAssets", "version");

        public TestClient(string marker)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Metadata)!);
            Directory.CreateDirectory(Path.GetDirectoryName(Marker)!);
            File.WriteAllBytes(Executable, Encoding.UTF8.GetBytes("fake exe, no process"));
            File.WriteAllBytes(Path.Combine(Root, "GameAssembly.dll"), Encoding.UTF8.GetBytes("fake assembly"));
            File.WriteAllBytes(Metadata, Encoding.UTF8.GetBytes("fake metadata"));
            File.WriteAllText(Marker, marker);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
