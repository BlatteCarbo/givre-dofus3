using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GivreClone.App.Services.ProtocolUpdates;
using Xunit;

namespace GivreClone.Tests;

public sealed class ProtocolUpdateIndexerTests
{
    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    private static readonly ProtocolArtifactHashes Hashes = new(Hash("exe"), Hash("assembly"), Hash("metadata"), Hash("marker"));
    private static ProtocolUpdateReport Ready => ProtocolUpdateProbe.Compare("3.7.11.11", Hashes, "3.7.11.11");

    [Fact]
    public void Dry_run_plan_has_fixed_allowlisted_argv_without_execute()
    {
        using var fixture = new Fixture();
        var result = ProtocolIndexerPlanner.BuildReviewOnly(Ready, fixture.Workspace);
        Assert.False(result.CanRun);
        Assert.True(result.ReviewOnly);
        Assert.False(result.DownloadsOrUpgrades);
        Assert.Null(result.Executable);
        Assert.Equal(new[] { "-m", "dri", "rework-auto-update" }, result.Arguments.Take(3));
        Assert.DoesNotContain("--execute", result.Arguments);
        Assert.DoesNotContain("upgrade", result.Arguments);
        Assert.Equal("3.7.11.11", result.Arguments[^1]);
        Assert.Equal(Path.Combine(fixture.Workspace, "DofusReverseIndexer", "src"), result.EnvironmentVariables["PYTHONPATH"]);
    }

    [Fact]
    public void Launcher_requires_explicit_approval_and_exact_python_sha()
    {
        using var fixture = new Fixture();
        string fakePython = Path.Combine(fixture.Root, "python.exe");
        File.WriteAllText(fakePython, "fixture only; no process launches");
        string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fakePython)));

        var denied = ProtocolIndexerPlanner.BuildReviewOnly(Ready, fixture.Workspace, fakePython, sha);
        Assert.False(denied.CanRun);
        var tampered = ProtocolIndexerPlanner.BuildReviewOnly(Ready, fixture.Workspace, fakePython,
            new string('F', 64), true);
        Assert.False(tampered.CanRun);
        var permitted = ProtocolIndexerPlanner.BuildReviewOnly(Ready, fixture.Workspace, fakePython, sha, true);
        Assert.True(permitted.CanRun);
        Assert.Equal(Path.GetFullPath(fakePython), permitted.Executable);
        Assert.Equal(sha, permitted.ApprovedExecutableSha256);
        Assert.False(permitted.DownloadsOrUpgrades);
    }

    [Fact]
    public void Planner_rejects_arbitrary_destination_and_unchanged_client()
    {
        using var fixture = new Fixture();
        Assert.False(ProtocolIndexerPlanner.BuildReviewOnly(Ready, fixture.Workspace,
            destinationAppDirectory: "GivreActive").CanRun);
        var baseline = new ProtocolTrustedBaseline("3.7.11.11", Hashes, "signed-provenance", true);
        var unchanged = ProtocolUpdateProbe.Compare("3.7.11.11", Hashes, "3.7.11.11", baseline);
        Assert.False(ProtocolIndexerPlanner.BuildReviewOnly(unchanged, fixture.Workspace).CanRun);
    }

    [Fact]
    public void Runner_receipt_gate_rejects_execute_or_runtime_certification()
    {
        const string safe = "{\"status\":\"planned\",\"mode\":\"plan\",\"review_only\":true,\"runtime_certified\":false,\"auto_promote\":false,\"givre_runtime_modified\":false}";
        Assert.True(ProtocolIndexerRunner.IsReviewOnlyReceipt(safe));
        Assert.False(ProtocolIndexerRunner.IsReviewOnlyReceipt(safe.Replace("\"plan\"", "\"execute\"")));
        Assert.False(ProtocolIndexerRunner.IsReviewOnlyReceipt(safe.Replace("\"runtime_certified\":false", "\"runtime_certified\":true")));
        Assert.False(ProtocolIndexerRunner.IsReviewOnlyReceipt(safe.Replace("\"auto_promote\":false", "\"auto_promote\":true")));
        Assert.False(ProtocolIndexerRunner.IsReviewOnlyReceipt("{not-json"));
    }

    [Fact]
    public void Redirected_output_directory_is_not_allowed()
    {
        using var fixture = new Fixture();
        string link = Path.Combine(fixture.Workspace, "GivreWork-Integration-20261020", "artifacts");
        try { Directory.CreateSymbolicLink(link, Path.Combine(fixture.Workspace, "GivreActive")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { return; }

        Assert.False(ProtocolIndexerPlanner.BuildReviewOnly(Ready, fixture.Workspace).CanRun);
    }

    [Fact]
    public void Proto_candidate_sha_equality_is_observed_only_after_hash_verification()
    {
        using var fixture = new Fixture();
        fixture.WriteNative("3.7.10.10", "message Identical { optional int32 id = 1; }", Hashes);
        fixture.WriteNative("3.7.11.11", "message Identical { optional int32 id = 1; }", Hashes);
        var report = Ready.WithLocalSchemaEvidence(Path.Combine(fixture.Workspace, "DofusReverseIndexer"), "3.7.10.10");
        Assert.True(report.SchemaUnchangedProven);
        Assert.True(report.SchemaEvidence!.CurrentClientBound);
        Assert.False(report.SchemaEvidence.ProtoChangedProven);
        Assert.False(report.ProtoChangedProven);
    }

    [Fact]
    public void Changed_proto_provides_a_candidate_only_not_gameplay_proof()
    {
        using var fixture = new Fixture();
        fixture.WriteNative("3.7.10.10", "message Before { optional int32 id = 1; }", Hashes);
        fixture.WriteNative("3.7.11.11", "message After { optional int64 id = 1; }", Hashes);
        var evidence = ProtocolSchemaEvidenceProbe.Inspect(Path.Combine(fixture.Workspace, "DofusReverseIndexer"),
            "3.7.10.10", "3.7.11.11");
        Assert.False(evidence.SchemaUnchangedProven);
        Assert.True(evidence.SchemaChangedCandidate);
        Assert.False(evidence.ProtoChangedProven);
    }

    [Fact]
    public void Missing_or_modified_proto_never_claims_equality()
    {
        using var fixture = new Fixture();
        fixture.WriteNative("3.7.10.10", "message Old { optional int32 id = 1; }", Hashes);
        fixture.WriteNative("3.7.11.11", "message Old { optional int32 id = 1; }", Hashes);
        string target = Path.Combine(fixture.Workspace, "DofusReverseIndexer", "local-output",
            "historical-extractions-v3", "3.7.11.11", "dofus-3.7.11.11.proto");
        File.AppendAllText(target, "corruption");
        var evidence = ProtocolSchemaEvidenceProbe.Inspect(Path.Combine(fixture.Workspace, "DofusReverseIndexer"),
            "3.7.10.10", "3.7.11.11");
        Assert.False(evidence.SchemaUnchangedProven);
        Assert.False(evidence.CurrentClientBound);
        Assert.False(evidence.SchemaChangedCandidate);
        Assert.Null(evidence.TargetProtoSha256);
    }

    [Fact]
    public void Equal_archives_are_not_automatically_bound_to_the_installed_client()
    {
        using var fixture = new Fixture();
        fixture.WriteNative("3.7.10.10", "message Identical { optional int32 id = 1; }", Hashes);
        fixture.WriteNative("3.7.11.11", "message Identical { optional int32 id = 1; }", Hashes);
        var mismatched = Hashes with { DofusExeSha256 = Hash("not current") };
        var evidence = ProtocolSchemaEvidenceProbe.Inspect(Path.Combine(fixture.Workspace, "DofusReverseIndexer"),
            "3.7.10.10", "3.7.11.11", mismatched);
        Assert.True(evidence.SchemaUnchangedProven);
        Assert.False(evidence.CurrentClientBound);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "protocol-planner-" + Guid.NewGuid().ToString("N"));
        public string Workspace => Path.Combine(Root, "GIVRE");

        public Fixture()
        {
            string indexer = Path.Combine(Workspace, "DofusReverseIndexer", "src", "dri");
            Directory.CreateDirectory(indexer);
            File.WriteAllText(Path.Combine(indexer, "cli.py"), "fixture");
            File.WriteAllText(Path.Combine(indexer, "autopipeline.py"), "fixture");
            Directory.CreateDirectory(Path.Combine(Workspace, "GivreActive"));
            Directory.CreateDirectory(Path.Combine(Workspace, "GivreWork-Integration-20261020"));
        }

        public void WriteNative(string version, string proto, ProtocolArtifactHashes hashes)
        {
            proto = proto.PadRight(80, ' '); // Native candidate manifests require >= 50 bytes.
            string dir = Path.Combine(Workspace, "DofusReverseIndexer", "local-output", "historical-extractions-v3", version);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "dofus-" + version + ".proto");
            File.WriteAllText(path, proto);
            string hash = Hash(proto);
            var manifest = new
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
                        dofusExeSha256 = hashes.DofusExeSha256,
                        gameAssemblySha256 = hashes.GameAssemblySha256,
                        globalMetadataSha256 = hashes.GlobalMetadataSha256
                    }
                }
            };
            File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
