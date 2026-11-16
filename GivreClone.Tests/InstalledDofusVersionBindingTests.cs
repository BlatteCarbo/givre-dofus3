using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using GivreClone.App.Services;
using Xunit;
using Xunit.Abstractions;

namespace GivreClone.Tests;

public sealed class InstalledDofusVersionBindingTests
{
    private readonly ITestOutputHelper _output;
    public InstalledDofusVersionBindingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Explicit_readonly_installed_game_probe_reports_actual_version()
    {
        string? exe = Environment.GetEnvironmentVariable("GIVRE_TEST_SELECTED_DOFUS_EXE");
        if (string.IsNullOrWhiteSpace(exe)) return; // No implicit user-machine access in CI.
        var found = MZP11CytrusVersionProbe.InspectInstalledDofus(exe);
        _output.WriteLine($"InstalledVersion={found.ProtocolVersion ?? "UNKNOWN"} " +
            $"EvidenceFingerprint={found.ManifestSha256 ?? "NONE"} Source={found.Train ?? "none"} "+
            $"Reason={found.Diagnostic}");
        Assert.True(found.HasLocalVersionEvidence, found.Diagnostic);
    }

    [Fact]
    public void Explicit_local_review_receipt_binds_actual_installation_and_mapping()
    {
        string? exe = Environment.GetEnvironmentVariable("GIVRE_TEST_SELECTED_DOFUS_EXE");
        string? data = Environment.GetEnvironmentVariable("GIVRE_TEST_LOCAL_MAPPING_DIR");
        if (string.IsNullOrWhiteSpace(exe) || string.IsNullOrWhiteSpace(data)) return;
        var installed = MZP11CytrusVersionProbe.InspectInstalledDofus(exe);
        string? schema = MZP12LocalCompatibilityReview.TryAdmit(data,
            "3.7.11.11", "3.7.12.12", installed.ManifestSha256);
        Assert.NotNull(schema);
        var decision = MZP12SocketVersionBinding.Evaluate(
            new("3.7.11.11", installed.ManifestSha256, "3.7.12.12", schema), installed);
        _output.WriteLine($"Current installed={installed.ProtocolVersion}, " +
            $"protoSchema={schema}, admission={decision.Reason}, ACK={decision.ServerAck}");
        Assert.Equal(MZP12SocketVersionBinding.Refusal.None, decision.Reason);
        Assert.False(decision.Authenticated);
        Assert.False(decision.GameplayCertified);
    }

    private sealed class LocalGame : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "GivreVersionFixture-" + Guid.NewGuid().ToString("N"));
        public string Exe => Path.Combine(Root, "Dofus.exe");
        public string Marker => Path.Combine(Root, "Dofus_Data", "StreamingAssets", "version");
        public string Assembly => Path.Combine(Root, "GameAssembly.dll");

        public LocalGame(string marker = "Version=3.7.12.12\nBuildDate=2026-10-19T15:11:45+00:00\n")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Marker)!);
            var metadata = Path.Combine(Root, "Dofus_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(metadata)!);
            File.WriteAllText(Marker, marker, Encoding.UTF8);
            File.WriteAllBytes(Exe, [1, 2, 3, 4]);
            File.WriteAllBytes(Assembly, [5, 6, 7, 8]);
            File.WriteAllBytes(metadata, [9, 10, 11, 12]);
            // This file represents a stale/unreadable Cytrus candidate. With a
            // valid marker, the file must never determine the installed build.
            File.WriteAllText(Path.Combine(Root, "manifest.cytrus"), "stale-not-installed");
        }

        public void Dispose() => Directory.Delete(Root, true);
    }

    [Fact]
    public void Installed_marker_wins_over_stale_launcher_manifest()
    {
        using var game = new LocalGame();
        var current = MZP11CytrusVersionProbe.InspectInstalledDofus(game.Exe);
        Assert.Equal("3.7.12.12", current.ProtocolVersion);
        Assert.True(current.HasLocalVersionEvidence);
        Assert.Equal("installed", current.Train);
        Assert.Equal(64, current.ManifestSha256?.Length);
        Assert.False(current.MayLaunchOrInject);
        Assert.False(current.AuthenticatedGameTraffic);
    }

    [Fact]
    public void Fingerprint_changes_when_executable_or_assembly_changes()
    {
        using var game = new LocalGame();
        var first = MZP11CytrusVersionProbe.InspectInstalledDofus(game.Exe);
        var bytes = File.ReadAllBytes(game.Assembly);
        File.WriteAllBytes(game.Assembly, [.. bytes, 99]);
        var next = MZP11CytrusVersionProbe.InspectInstalledDofus(game.Exe);
        Assert.NotEqual(first.ManifestSha256, next.ManifestSha256);
        var decision = MZP12SocketVersionBinding.Evaluate(
            new(first.ProtocolVersion, first.ManifestSha256), next);
        Assert.True(decision.StructurallyConsistent);
    }

    [Fact]
    public void Malformed_or_duplicate_marker_never_falls_back_to_old_version()
    {
        foreach (var raw in new[]
                 {
                     "Version=3.7.12\n", "Version=3.7.12.12\nVersion=3.7.11.11\n",
                     "Version=4.0.0.0garbage\n"
                 })
        {
            using var game = new LocalGame(raw);
            Assert.False(MZP11CytrusVersionProbe.InspectInstalledDofus(game.Exe)
                .HasLocalVersionEvidence);
        }
    }

    [Fact]
    public void Missing_installed_binary_cannot_publish_a_version_binding()
    {
        using var game = new LocalGame();
        File.Delete(game.Assembly);
        Assert.False(MZP11CytrusVersionProbe.InspectInstalledDofus(game.Exe)
            .HasLocalVersionEvidence);
    }

    [Fact]
    public void Marker_outside_selected_installation_is_rejected()
    {
        using var one = new LocalGame();
        using var other = new LocalGame();
        var result = MZP11CytrusVersionProbe.InspectInstalledMarker(one.Root, other.Marker);
        Assert.False(result.HasLocalVersionEvidence);
    }

    [Fact]
    public void Version_must_share_the_published_mapping_family()
    {
        using var game = new LocalGame();
        var target = MZP11CytrusVersionProbe.InspectInstalledDofus(game.Exe);
        var old = MZP12SocketVersionBinding.Evaluate(
            new("3.7.11.11", target.ManifestSha256), target);
        Assert.True(old.StructurallyConsistent);
        Assert.False(MZP12SocketVersionBinding.Evaluate(
            new("3.8.0.1", target.ManifestSha256), target).StructurallyConsistent);
        var exact = MZP12SocketVersionBinding.Evaluate(
            new(target.ProtocolVersion, target.ManifestSha256), target);
        Assert.True(exact.StructurallyConsistent);
        Assert.False(exact.Authenticated);
        Assert.False(exact.ServerAck);
    }

    [Fact]
    public void Review_receipt_is_optional_for_same_family_protocol_admission()
    {
        using var game = new LocalGame();
        var installed = MZP11CytrusVersionProbe.InspectInstalledDofus(game.Exe);
        string folder = Path.Combine(game.Root, "data");
        Directory.CreateDirectory(folder);
        var matching = Path.Combine(folder, "matching_current.json");
        var fields = Path.Combine(folder, "fields.json");
        File.WriteAllText(matching, "SYNTHETIC MATCHING NOT DOFUS");
        File.WriteAllText(fields, "SYNTHETIC FIELDS NOT DOFUS");
        string hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        string contentsHash(string file) => hash(File.ReadAllBytes(file));
        string protoHash = hash(Encoding.UTF8.GetBytes("synthetic-proto-fixture"));
        void WriteReceipt(string claimedTargetProto) => File.WriteAllText(
            Path.Combine(folder, MZP12LocalCompatibilityReview.ReceiptFile),
            JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["operatorConfirmedSameMappings"] = true,
                ["reviewOnly"] = true,
                ["sourceMappingBuild"] = "3.7.11.11",
                ["installedGameBuild"] = "3.7.12.12",
                ["installedFingerprintSha256"] = installed.ManifestSha256!,
                ["sourceProtoSha256"] = protoHash,
                ["targetProtoSha256"] = claimedTargetProto,
                ["matching_current_jsonSha256"] = contentsHash(matching),
                ["fields_jsonSha256"] = contentsHash(fields),
            }));

        WriteReceipt(protoHash);
        string? admitted = MZP12LocalCompatibilityReview.TryAdmit(folder,
            "3.7.11.11", "3.7.12.12", installed.ManifestSha256);
        Assert.Equal(protoHash, admitted);
        var bound = new MZP12SocketVersionBinding.Binding("3.7.11.11",
            installed.ManifestSha256, "3.7.12.12", admitted);
        var decision = MZP12SocketVersionBinding.Evaluate(bound, installed);
        Assert.True(decision.StructurallyConsistent);
        Assert.False(decision.IsUnverifiedCrossPatchReview);
        Assert.False(decision.Authenticated);
        Assert.False(decision.ServerAck);
        Assert.False(decision.GameplayCertified);
        Assert.True(MZP12SocketVersionBinding.CheckServerRequiredBuild(bound, "3.7.12.12")
            .StructurallyConsistent);
        Assert.True(MZP12SocketVersionBinding.CheckServerRequiredBuild(bound, "3.7.13.13")
            .StructurallyConsistent);
        Assert.False(MZP12SocketVersionBinding.CheckServerRequiredBuild(bound, "3.8.0.1")
            .StructurallyConsistent);

        // An unpinned change invalidates admission (rather than silently
        // continuing to reuse the old mapping).
        File.AppendAllText(fields, "CHANGED");
        Assert.Null(MZP12LocalCompatibilityReview.TryAdmit(folder,
            "3.7.11.11", "3.7.12.12", installed.ManifestSha256));
        File.WriteAllText(fields, "SYNTHETIC FIELDS NOT DOFUS");
        WriteReceipt(hash(Encoding.UTF8.GetBytes("changed-proto")));
        Assert.Null(MZP12LocalCompatibilityReview.TryAdmit(folder,
            "3.7.11.11", "3.7.12.12", installed.ManifestSha256));
    }

    [Fact]
    public void Operator_review_receipt_is_never_granted_to_a_different_installed_binary()
    {
        using var game = new LocalGame();
        var prior = MZP11CytrusVersionProbe.InspectInstalledDofus(game.Exe);
        Assert.Null(MZP12LocalCompatibilityReview.TryAdmit(game.Root,
            "3.7.11.11", "3.7.12.12", prior.ManifestSha256));
        Assert.Null(MZP12LocalCompatibilityReview.TryAdmit(game.Root,
            "3.7.11.11", "3.7.12.12", new string('0', 64)));
    }

    [Fact]
    public void Server_required_version_retry_stays_within_protocol_family()
    {
        var bound = new MZP12SocketVersionBinding.Binding("3.7.11.11",
            new string('A', 64), "3.7.12.12", new string('B', 64));
        Assert.True(MZP12SocketVersionBinding.IsUnrestrictedSameBuildCandidate(
            bound, "3.7.11.11", reviewedCrossPatchSession: false));
        // The server requesting 3.7.11.11 has NOT replaced installed 3.7.12.12.
        Assert.True(MZP12SocketVersionBinding.IsUnrestrictedSameBuildCandidate(
            bound, "3.7.11.11", reviewedCrossPatchSession: true));
        Assert.False(MZP12SocketVersionBinding.IsUnrestrictedSameBuildCandidate(
            bound, "3.7.12.12", reviewedCrossPatchSession: true));
        Assert.False(MZP12SocketVersionBinding.IsUnrestrictedSameBuildCandidate(
            bound, "3.7.13.13", reviewedCrossPatchSession: false));
        Assert.False(MZP12SocketVersionBinding.IsUnrestrictedSameBuildCandidate(
            bound, "3.8.0.1", reviewedCrossPatchSession: true));
    }

    [Fact]
    public void Cross_patch_review_uses_live_socket_control_but_keeps_turbo_disabled()
    {
        var at = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, "GivreClone.App",
            "Services", "Transport", "Socket", "SocketProbe.cs")));
        string? socketSource = at is null ? null : File.ReadAllText(Path.Combine(at.FullName, "GivreClone.App",
            "Services", "Transport", "Socket", "SocketProbe.cs"));
        Assert.NotNull(socketSource);
        Assert.Contains("reviewedMovementOnly: reviewRestricted", socketSource);
        // Registration is now guarded by a session-ownership admission lease;
        // keep checking the real live injector wiring instead of the retired delegate name.
        Assert.Contains("RegisterInjectorWithAdmission = (pid, inject) =>", socketSource);
        Assert.Contains("_sessionOwnership.TryRegister(lease,", socketSource);
        Assert.Contains("Stores.Launch.RegisterSocketInjector(", socketSource);
        Assert.Contains("UnregisterInjector = pid =>", socketSource);
        Assert.Contains("_sessionOwnership.Release(lease, Stores.Launch.UnregisterSocketInjector)", socketSource);
        Assert.Contains("initialAdmission.StructurallyConsistent", socketSource);
        Assert.Contains("initialAdmission.IsUnverifiedCrossPatchReview", socketSource);
        Assert.Contains("TurboAck = reviewRestricted ? null", socketSource);
    }
}
