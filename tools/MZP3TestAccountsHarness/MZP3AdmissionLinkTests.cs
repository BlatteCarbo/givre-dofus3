using GivreClone.App.Services;
using GivreClone.Engine;
using GivreClone.Engine.Recovery;
using GivreClone.Protocol;
using GivreCore.Mitm;
using Xunit;

namespace MZP3TestAccountsHarness;

public sealed class MZP3AdmissionLinkTests : IDisposable
{
    private readonly string _run = "mzp3-" + Guid.NewGuid().ToString("N");
    private readonly string _folder;
    public MZP3AdmissionLinkTests()
    {
        _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GivreReplica", "TestRuns", _run);
    }

    private sealed class OfflineSource : IMZP0AuthenticatedSessionSource
    {
        public bool TryGetAuthenticatedIdentity(out MZP0SessionIdentity identity)
        { identity = default; return false; }
    }
    private sealed class OfflineVerifier : IMZP0PositiveResponseVerifier
    {
        public bool IsVerifiedPositiveResponse(BotMessage inbound,
            MZP0SessionGeneration expectedGeneration, int expectedUid) => false;
    }
    private sealed class DenySink : IFrameSink
    {
        public bool Send(Direction direction, byte[] bytes)
            => throw new InvalidOperationException("No network transport in offline tests.");
    }

    private static MZP1OwnedClientPermit ExternalPermit()
        => new(11111, 638960000000000000L, "3.7.10.10",
            Path.Combine(Path.GetTempPath(), "offline-proving-only.exe"),
            new string('A', 64));

    private static MZP2TrustedEnginePorts ExternalPorts()
    {
        var catalog = ProtocolCatalog.LoadFromStrings("", "{}", null, "{}");
        return new MZP2TrustedEnginePorts(catalog, new DenySink(), new NoPacer(),
            new OfflineSource(), new OfflineVerifier());
    }

    private MZP3TestAccountService Open() => MZP3TestAccountService.Open(_run, true);

    public void Dispose()
    {
        if (!Directory.Exists(_folder)) return;
        string root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "GivreReplica", "TestRuns"));
        if (!Path.GetFullPath(_folder).StartsWith(root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) || _run.Length != 37 ||
            !_run.StartsWith("mzp3-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unsafe test cleanup target.");
        if (File.GetAttributes(_folder).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("Unsafe reparse-point test cleanup.");
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Prepared_MZP2_request_contains_only_explicit_test_paths_and_external_authorities()
    {
        using var store = Open();
        var profile = store.CreateProfile("Standalone UI");
        store.SelectProfile(profile.Id);
        store.SetOfflineCandidate(profile.Id, "Offline Server", "Offline Character", true);
        store.Save();
        var permit = ExternalPermit();
        var ports = ExternalPorts();
        var request = store.PrepareMZP2Request(permit, ports, 15677, true);
        Assert.Same(permit, request.OwnedClient);
        Assert.Same(ports, request.TrustedPorts);
        Assert.True(request.ExplicitOperatorApproval);
        Assert.Equal(15677, request.IsolatedTestHost!.TestRelayPort);
        Assert.True(request.IsolatedTestHost.ExplicitOperatorApproval);
        Assert.Equal(Path.Combine(_folder, "accounts.json"), request.IsolatedTestHost.TestAccountStorePath);
        Assert.Equal(Path.Combine(_folder, "recovery"), request.JournalDirectory);
        Assert.False(File.Exists(request.IsolatedTestHost.TestAccountStorePath));
        // MZP2 itself remains gated: fabricated test fixtures cannot authorize
        // either the proxy binding or the real identity/server response.
        using var admission = new MZP2OwnedHostAdmission();
        Assert.Equal(MZP2AdmissionReason.IdentityUnverified,
            admission.StartOwnedTestHost(request).Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5657)]
    [InlineData(5658)]
    [InlineData(65536)]
    public void Unsafe_external_relay_ports_refused_without_credential_file(int port)
    {
        using var store = Open();
        var id = store.CreateProfile("Ready Label").Id;
        store.SelectProfile(id);
        store.SetOfflineCandidate(id, "Server", "Character", true);
        store.Save();
        Assert.Equal(MZP3Failure.InvalidPort,
            Assert.Throws<MZP3StoreException>(() =>
                store.PrepareMZP2Request(ExternalPermit(), ExternalPorts(), port, true)).Failure);
        Assert.False(File.Exists(Path.Combine(_folder, "accounts.json")));
    }

    [Fact]
    public void Unsaved_or_missing_character_candidate_blocks_preparation()
    {
        using var store = Open();
        var id = store.CreateProfile("Local Only").Id;
        store.SelectProfile(id);
        Assert.Equal(MZP3Failure.UnsavedChanges,
            Assert.Throws<MZP3StoreException>(() =>
                store.PrepareMZP2Request(ExternalPermit(), ExternalPorts(), 15677, true)).Failure);
        store.Save();
        Assert.Equal(MZP3Failure.NoSelection,
            Assert.Throws<MZP3StoreException>(() =>
                store.PrepareMZP2Request(ExternalPermit(), ExternalPorts(), 15677, true)).Failure);
    }

    [Fact]
    public void Explicit_test_run_operator_approval_and_external_ports_are_both_required()
    {
        using var store = Open();
        var id = store.CreateProfile("Local Test").Id;
        store.SelectProfile(id);
        store.SetOfflineCandidate(id, "Server", "Character", true);
        store.Save();
        Assert.Equal(MZP3Failure.ApprovalRequired,
            Assert.Throws<MZP3StoreException>(() =>
                store.PrepareMZP2Request(ExternalPermit(), ExternalPorts(), 15677, false)).Failure);
        Assert.Equal(MZP3Failure.CandidateNotExplicit,
            Assert.Throws<MZP3StoreException>(() =>
                store.PrepareMZP2Request(ExternalPermit(), null!, 15677, true)).Failure);
    }
}
