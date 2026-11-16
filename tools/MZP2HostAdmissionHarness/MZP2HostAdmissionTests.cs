using GivreClone.App.Services;
using GivreClone.Engine;
using GivreClone.Engine.Recovery;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using Xunit;

namespace MZP2HostAdmissionHarness;

public sealed class MZP2HostAdmissionTests : IDisposable
{
    private const int Pid = 34567;
    private const int Connection = 74;
    private const long Birth = 638960000000000000L;
    private const string Version = "3.7.10.10";
    private const string Digest = "1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF";
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mzp2-host-admission-" + Guid.NewGuid().ToString("N"));
    private readonly string _isolatedAccountStore = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GivreReplica", "TestRuns", "harness-" + Guid.NewGuid().ToString("N"), "accounts.json");
    private const int TestPort = 15659;

    public MZP2HostAdmissionTests() => Directory.CreateDirectory(_temp);
    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch (IOException) { }
    }

    private sealed class FakeHost : IMZP2OwnedHostPort
    {
        public bool IsRunning { get; set; }
        public bool BindingVerified { get; set; } = true;
        public bool BindingThrows { get; set; }
        public bool CanBindProtectedEngine(MZP1OwnedClientPermit ownedClient,
            MZP0SessionIdentity identity, MZP2TrustedEnginePorts enginePorts,
            MZP2IsolatedTestHostConfiguration testConfiguration)
        {
            if (BindingThrows) throw new IOException("fake binding error");
            return BindingVerified && ownedClient.Pid == identity.ProcessId &&
                enginePorts is { ConnectionBoundSink: not null, Catalog: not null } &&
                testConfiguration.TestRelayPort == TestPort;
        }
        public bool ThrowOnStart { get; set; }
        public bool ThrowAfterRunning { get; set; }
        public bool ThrowOnStop { get; set; }
        public bool StartWithoutRunning { get; set; }
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public MZP1OwnedClientPermit? ReceivedPermit { get; private set; }
        public MZP2IsolatedTestHostConfiguration? ReceivedTestConfiguration { get; private set; }
        public Func<CapturedMessage, MZP1GuardedEngineWiring>? ProtectedFactory { get; private set; }

        public void Start(MZP1OwnedClientPermit permit,
            Func<CapturedMessage, MZP1GuardedEngineWiring> factory,
            MZP2IsolatedTestHostConfiguration testConfiguration)
        {
            Starts++;
            ReceivedPermit = permit;
            ReceivedTestConfiguration = testConfiguration;
            ProtectedFactory = factory;
            if (ThrowOnStart) throw new InvalidOperationException("fake sensitive host error");
            IsRunning = !StartWithoutRunning;
            if (ThrowAfterRunning) throw new IOException("fake failure after launch");
        }

        public void Stop()
        {
            Stops++;
            if (ThrowOnStop) throw new InvalidOperationException("fake sensitive stop failure");
            IsRunning = false;
        }

        public MZP1GuardedEngineWiring Deliver(CapturedMessage message)
            => (ProtectedFactory ?? throw new InvalidOperationException("No protected capture delegate"))(message);
    }

    private sealed class AuthenticatedOrigin : IMZP0AuthenticatedSessionSource
    {
        public bool Authenticated { get; set; } = true;
        public bool Throws { get; set; }
        public MZP0SessionIdentity Identity { get; set; } = new(Pid, Birth, Connection, Version);
        public bool TryGetAuthenticatedIdentity(out MZP0SessionIdentity identity)
        {
            if (Throws) throw new IOException("account secret should not leak");
            identity = Identity;
            return Authenticated;
        }
    }

    private sealed class PositiveResponseAuthority : IMZP0PositiveResponseVerifier
    {
        public bool Trusted { get; set; }
        public bool IsVerifiedPositiveResponse(BotMessage inbound, MZP0SessionGeneration generation, int uid)
            => Trusted && generation.Identity.ConnectionId == Connection &&
               inbound.Name == "ServerPositive" && inbound.IsFromServer &&
               EnvelopeBuilder.ReadEnvelopeUid(inbound.RawFrame) == uid;
    }

    private sealed class CombinedAuthority : IMZP0AuthenticatedSessionSource, IMZP0PositiveResponseVerifier
    {
        public bool TryGetAuthenticatedIdentity(out MZP0SessionIdentity identity)
        { identity = new(Pid, Birth, Connection, Version); return true; }
        public bool IsVerifiedPositiveResponse(BotMessage inbound, MZP0SessionGeneration generation, int uid) => true;
    }

    private sealed class SafeSink : IFrameSink
    {
        public int Calls { get; private set; }
        public bool Send(Direction direction, byte[] bytes)
        {
            Calls++;
            Assert.Equal(Direction.ClientToServer, direction);
            Assert.NotEmpty(bytes);
            return true;
        }
    }

    private static ProtocolCatalog Catalog()
    {
        var proto = """
            message FightTurnFinishRequest {
              bool is_afk = 1;
            }
            """;
        var matching = """
            {"type.ankama.com/end":"MessageType.FightTurnFinishRequest",
             "type.ankama.com/ok":"MessageType.ServerPositive"}
            """;
        var fields = """
            {"GameMessage":{"request":1,"event":2,"response":3},
             "Request":{"content":1,"uid":2},"Response":{"content":1,"uid":2},
             "Event":{"content":3}}
            """;
        var catalog = ProtocolCatalog.LoadFromStrings(proto, matching, null, fields);
        EnvelopeBuilder.UseCatalog(catalog.FieldNumber);
        return catalog;
    }

    private static MZP1OwnedClientPermit Permit()
        => new(Pid, Birth, Version, Path.Combine(Path.GetTempPath(), "owned-Dofus.exe"), Digest);

    private MZP2TestClientAdmissionRequest Request(AuthenticatedOrigin? origin = null,
        IMZP0PositiveResponseVerifier? verifier = null, SafeSink? sink = null,
        bool explicitApproval = true, MZP1OwnedClientPermit? permit = null,
        string? directory = null)
        => new(explicitApproval, permit ?? Permit(),
            new MZP2TrustedEnginePorts(Catalog(), sink ?? new SafeSink(), new NoPacer(),
                origin ?? new AuthenticatedOrigin(), verifier ?? new PositiveResponseAuthority()),
            directory ?? _temp,
            new MZP2IsolatedTestHostConfiguration(TestPort, _isolatedAccountStore, true));

    private static CapturedMessage Capture(uint pid = Pid, int connection = Connection,
        int uid = 1)
    {
        var frame = RawProtobuf.Encode(new[]
        {
            new ProtoField { Field = 3, Wire = WireType.Len, Message = new List<ProtoField>
            { new() { Field = 2, Wire = WireType.Varint, Varint = (ulong)uid } } }
        });
        return new CapturedMessage(1, Direction.ServerToClient, connection,
            pid, "ok", Array.Empty<ProtoField>(), frame);
    }

    [Fact]
    public void Default_dedicated_adapter_wraps_real_BotHostService_but_never_starts_implicitly()
    {
        var service = new BotHostService(); // real source, but offline BotHost stub is never constructed
        Assert.False(service.IsRunning);
        service.Start(null, null); // proven fail closed before BotHost ctor
        service.StartBound(null, null, TestPort, _isolatedAccountStore);
        Assert.False(service.IsRunning);
        var adapter = new MZP2DedicatedBotHostPort();
        Assert.False(adapter.IsRunning);
        using var coordinator = new MZP2OwnedHostAdmission(adapter);
        Assert.Equal(MZP2AdmissionState.Dormant, coordinator.Diagnostics.State);
        Assert.False(coordinator.Diagnostics.HostOwned);
        // An explicit permit still cannot bypass the missing real host/sink
        // identity seam: the concrete production adapter ALWAYS refuses it.
        var attempted = coordinator.StartOwnedTestHost(Request());
        Assert.Equal(MZP2AdmissionReason.HostTransportBindingUnavailable, attempted.Reason);
        Assert.False(attempted.HostOwned);
        Assert.False(adapter.IsRunning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_or_throwing_host_transport_binding_fails_before_journal_or_host_start(bool throws)
    {
        var host = new FakeHost { BindingVerified = false, BindingThrows = throws };
        using var coordinator = new MZP2OwnedHostAdmission(host);
        var result = coordinator.StartOwnedTestHost(Request());
        Assert.Equal(MZP2AdmissionReason.HostTransportBindingUnavailable, result.Reason);
        Assert.Equal(0, host.Starts);
        Assert.Equal(0, host.Stops);
        Assert.Empty(Directory.GetFiles(_temp, "*.journal.json", SearchOption.TopDirectoryOnly));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1023)]
    [InlineData(5657)]
    [InlineData(5658)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void Invalid_or_active_Givre_relay_port_is_rejected_before_host_binding(int port)
    {
        var host = new FakeHost();
        using var coordinator = new MZP2OwnedHostAdmission(host);
        var request = Request() with { IsolatedTestHost = new(port, _isolatedAccountStore, true) };
        var status = coordinator.StartOwnedTestHost(request);
        Assert.Equal(MZP2AdmissionReason.IsolatedTestRelayPortInvalid, status.Reason);
        Assert.Equal(0, host.Starts);
        Assert.Equal(0, host.Stops);
        Assert.False(status.IsolatedAccountStoreSelected);
    }

    [Fact]
    public void Isolated_account_store_requires_its_own_explicit_operator_approval()
    {
        var host = new FakeHost();
        using var coordinator = new MZP2OwnedHostAdmission(host);
        var request = Request() with
        {
            IsolatedTestHost = new(TestPort, _isolatedAccountStore, false)
        };
        var status = coordinator.StartOwnedTestHost(request);
        Assert.Equal(MZP2AdmissionReason.IsolatedTestHostApprovalMissing, status.Reason);
        Assert.Equal(0, host.Starts);
    }

    [Fact]
    public void Missing_isolated_config_never_uses_default_service_port_or_accounts()
    {
        var host = new FakeHost();
        using var coordinator = new MZP2OwnedHostAdmission(host);
        var status = coordinator.StartOwnedTestHost(Request() with { IsolatedTestHost = null });
        Assert.Equal(MZP2AdmissionReason.IsolatedTestHostApprovalMissing, status.Reason);
        Assert.Equal(0, host.Starts);
        Assert.Equal(0, host.Stops);
    }

    [Fact]
    public void Unsafe_store_paths_are_rejected_without_reading_or_creating_accounts()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string parent = Path.Combine(local, "GivreReplica");
        string testRoot = Path.Combine(parent, "TestRuns");
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] disallowed =
        {
            "", "accounts.json", ".\\accounts.json", "C:accounts.json",
            Path.Combine(appData, "GivreClone", "accounts.json"),
            Path.Combine(parent, "accounts.json"),
            Path.Combine(local, "GivreReplica", "TestRuns-other", "accounts.json"),
            Path.Combine(testRoot, "owned", "accounts.txt"),
            Path.Combine(testRoot, "..", "GivreClone", "accounts.json"),
            testRoot
        };
        foreach (string path in disallowed)
        {
            var host = new FakeHost();
            using var coordinator = new MZP2OwnedHostAdmission(host);
            var status = coordinator.StartOwnedTestHost(Request() with
                { IsolatedTestHost = new(TestPort, path, true) });
            Assert.Equal(MZP2AdmissionReason.IsolatedTestAccountStoreInvalid, status.Reason);
            Assert.Equal(0, host.Starts);
            Assert.Equal(0, host.Stops);
            Assert.Empty(Directory.GetFiles(_temp, "*.journal.json"));
        }
    }

    [Fact]
    public void Isolated_selection_is_retained_only_while_owned_and_never_exposes_store_path()
    {
        var host = new FakeHost();
        using var coordinator = new MZP2OwnedHostAdmission(host);
        var request = Request();
        var status = coordinator.StartOwnedTestHost(request);
        Assert.Equal(TestPort, status.SelectedTestRelayPort);
        Assert.True(status.IsolatedAccountStoreSelected);
        Assert.Equal(_isolatedAccountStore, host.ReceivedTestConfiguration!.TestAccountStorePath);
        Assert.DoesNotContain(_isolatedAccountStore, status.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(_isolatedAccountStore));
        var stopped = coordinator.StopOwnedTestHost();
        Assert.Null(stopped.SelectedTestRelayPort);
        Assert.False(stopped.IsolatedAccountStoreSelected);
    }

    [Fact]
    public void Bare_constructor_does_not_start_process_or_proxy()
    {
        using var coordinator = new MZP2OwnedHostAdmission();
        var status = coordinator.Diagnostics;
        Assert.Equal(MZP2AdmissionState.Dormant, status.State);
        Assert.False(status.HostOwned);
        Assert.False(status.ProtectedSessionReady);
    }

    [Fact]
    public void Prepared_host_bound_delegate_matches_real_StartBound_but_does_not_self_attest()
    {
        // Compile-time evidence of the new genuine host-originated signature.
        MZP2HostBoundSessionFactory prepared =
            (capture, catalog, sink, pacer) => throw new InvalidOperationException(
                "Host-only ports are not available in this test");
        Func<CapturedMessage, ProtocolCatalog, IFrameSink, IPacer, MZP1GuardedEngineWiring>
            matchingRealHostSignature = prepared.Invoke;
        var service = new BotHostService();
        service.StartBound(null, matchingRealHostSignature, TestPort, _isolatedAccountStore);
        Assert.False(service.IsRunning);
        Assert.Equal(0, service.Port);
        // The real adapter deliberately blocks even with externally supplied
        // fake authorities: a callback alone never authenticates a session.
        using var coordinator = new MZP2OwnedHostAdmission();
        var denied = coordinator.StartOwnedTestHost(Request());
        Assert.Equal(MZP2AdmissionReason.HostTransportBindingUnavailable, denied.Reason);
    }

    [Theory]
    [InlineData("approval", MZP2AdmissionReason.ExplicitApprovalMissing)]
    [InlineData("malformed", MZP2AdmissionReason.PermitMalformed)]
    [InlineData("ports", MZP2AdmissionReason.EnginePortsMissing)]
    [InlineData("unauthenticated", MZP2AdmissionReason.IdentityUnverified)]
    [InlineData("throws", MZP2AdmissionReason.IdentityUnverified)]
    [InlineData("birth", MZP2AdmissionReason.IdentityMismatch)]
    [InlineData("pid", MZP2AdmissionReason.IdentityMismatch)]
    [InlineData("version", MZP2AdmissionReason.IdentityMismatch)]
    [InlineData("relative", MZP2AdmissionReason.JournalLocationInvalid)]
    public void Preflight_denies_without_creating_or_stopping_any_host(
        string defect, MZP2AdmissionReason expected)
    {
        var host = new FakeHost();
        using var coordinator = new MZP2OwnedHostAdmission(host);
        var origin = new AuthenticatedOrigin();
        var request = Request(origin);
        switch (defect)
        {
            case "approval": request = request with { ExplicitOperatorApproval = false }; break;
            case "malformed": request = request with
                { OwnedClient = Permit() with { ClientBuild = "3.6.12.16" } }; break;
            case "ports": request = request with { TrustedPorts = null }; break;
            case "unauthenticated": origin.Authenticated = false; break;
            case "throws": origin.Throws = true; break;
            case "birth": origin.Identity = origin.Identity with { ProcessStartUtcTicks = Birth + 1 }; break;
            case "pid": origin.Identity = origin.Identity with { ProcessId = Pid + 1 }; break;
            case "version": origin.Identity = origin.Identity with { ProtocolVersion = "not-target" }; break;
            case "relative": request = request with { JournalDirectory = "relative/journal" }; break;
        }
        var status = coordinator.StartOwnedTestHost(request);
        Assert.Equal(MZP2AdmissionState.Denied, status.State);
        Assert.Equal(expected, status.Reason);
        Assert.Equal(0, host.Starts);
        Assert.Equal(0, host.Stops);
        Assert.False(status.HostOwned);
    }

    [Fact]
    public void Same_reference_cannot_claim_independent_authorities()
    {
        var host = new FakeHost();
        using var coordinator = new MZP2OwnedHostAdmission(host);
        var both = new CombinedAuthority();
        var request = Request() with { TrustedPorts = new(Catalog(), new SafeSink(),
            new NoPacer(), both, both) };
        var status = coordinator.StartOwnedTestHost(request);
        Assert.Equal(MZP2AdmissionReason.AuthoritiesNotIndependent, status.Reason);
        Assert.Equal(0, host.Starts);
    }

    [Fact]
    public async Task Trusted_admission_passes_explicit_permit_to_real_host_signature_and_engine_factory()
    {
        var host = new FakeHost();
        using var coordinator = new MZP2OwnedHostAdmission(host);
        var sink = new SafeSink();
        var origin = new AuthenticatedOrigin();
        var verifier = new PositiveResponseAuthority();
        var request = Request(origin, verifier, sink);

        var started = coordinator.StartOwnedTestHost(request);
        Assert.Equal(MZP2AdmissionState.AwaitingOwnedCapture, started.State);
        Assert.True(started.HostOwned);
        Assert.False(started.ProtectedSessionReady);
        Assert.Equal(1, host.Starts);
        Assert.Same(request.OwnedClient, host.ReceivedPermit);
        Assert.Same(request.IsolatedTestHost, host.ReceivedTestConfiguration);
        Assert.Equal(TestPort, started.SelectedTestRelayPort);
        Assert.True(started.IsolatedAccountStoreSelected);
        Assert.False(File.Exists(_isolatedAccountStore));
        Assert.NotNull(host.ProtectedFactory);

        var wiring = host.Deliver(Capture());
        Assert.Equal(MZP1Admission.Ready, wiring.Admission);
        Assert.Equal(MZP2AdmissionState.SessionAdmitted, coordinator.Diagnostics.State);
        Assert.Same(wiring.Context.Actions, wiring.Actions);
        var action = await wiring.Context.Actions.EndTurnAsync();
        Assert.True(action.Sent);
        Assert.Null(action.Frame);
        Assert.Equal(1, sink.Calls);
        Assert.Equal(1, coordinator.Diagnostics.UnresolvedOperations);
        Assert.True(coordinator.Diagnostics.JournalRevision >= 3);
        Assert.Equal(MZP0DeliveryState.DispatchWindowOpened,
            Assert.Single(((MZP0GuardedActionSender)wiring.Actions).JournalStatus).State);

        var stopped = coordinator.StopOwnedTestHost();
        Assert.Equal(MZP2AdmissionState.Stopped, stopped.State);
        Assert.False(stopped.HostOwned);
        Assert.Equal(1, host.Stops);
        await Assert.ThrowsAsync<MZP0GuardedActionException>(() => wiring.Actions.EndTurnAsync());
    }

    [Theory]
    [InlineData(0u, 74)]
    [InlineData(34568u, 74)]
    [InlineData(34567u, 75)]
    public void Capture_not_bound_to_owner_is_rejected_without_journal_activation(uint pid, int connection)
    {
        var host = new FakeHost();
        using var coordinator = new MZP2OwnedHostAdmission(host);
        Assert.Equal(MZP2AdmissionState.AwaitingOwnedCapture,
            coordinator.StartOwnedTestHost(Request()).State);
        var error = Assert.Throws<MZP2AdmissionException>(() => host.Deliver(Capture(pid, connection)));
        Assert.Equal(MZP2AdmissionReason.CaptureMismatch, error.Reason);
        Assert.Equal(MZP2AdmissionState.Faulted, coordinator.Diagnostics.State);
        Assert.False(coordinator.Diagnostics.ProtectedSessionReady);
        coordinator.StopOwnedTestHost();
        Assert.Equal(1, host.Stops);
    }

    [Fact]
    public void Authority_revoked_after_start_but_before_first_capture_blocks_guarded_factory()
    {
        var host = new FakeHost();
        using var coordinator = new MZP2OwnedHostAdmission(host);
        var origin = new AuthenticatedOrigin();
        coordinator.StartOwnedTestHost(Request(origin));
        origin.Authenticated = false;
        var error = Assert.Throws<MZP2AdmissionException>(() => host.Deliver(Capture()));
        Assert.Equal(MZP2AdmissionReason.CaptureMismatch, error.Reason);
        Assert.Equal(MZP2AdmissionState.Faulted, coordinator.Diagnostics.State);
    }

    [Fact]
    public void Host_start_failure_remains_fail_closed_and_does_not_call_stop_on_other_host()
    {
        var host = new FakeHost { ThrowOnStart = true };
        using var coordinator = new MZP2OwnedHostAdmission(host);
        var result = coordinator.StartOwnedTestHost(Request());
        Assert.Equal(MZP2AdmissionReason.HostFailed, result.Reason);
        Assert.Equal(1, host.Starts);
        Assert.Equal(0, host.Stops);
        Assert.False(result.HostOwned);
        Assert.DoesNotContain("sensitive", result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Partly_started_host_failure_keeps_journal_leased_until_targeted_stop_succeeds()
    {
        var host = new FakeHost { ThrowAfterRunning = true, ThrowOnStop = true };
        using var coordinator = new MZP2OwnedHostAdmission(host);
        var status = coordinator.StartOwnedTestHost(Request());
        Assert.Equal(MZP2AdmissionState.Faulted, status.State);
        Assert.True(status.HostOwned);
        Assert.Equal(1, host.Starts);
        Assert.Equal(1, host.Stops);
        using var second = new MZP2OwnedHostAdmission(new FakeHost());
        Assert.Equal(MZP2AdmissionReason.JournalUnavailable,
            second.StartOwnedTestHost(Request()).Reason);

        host.ThrowOnStop = false;
        var ended = coordinator.StopOwnedTestHost();
        Assert.Equal(MZP2AdmissionState.Stopped, ended.State);
        Assert.False(ended.HostOwned);
        Assert.Equal(2, host.Stops);
    }

    [Fact]
    public void Host_that_refuses_to_start_cannot_be_claimed_as_owned()
    {
        var host = new FakeHost { StartWithoutRunning = true };
        using var coordinator = new MZP2OwnedHostAdmission(host);
        var result = coordinator.StartOwnedTestHost(Request());
        Assert.Equal(MZP2AdmissionReason.HostFailed, result.Reason);
        Assert.Equal(0, host.Stops);
        Assert.False(result.HostOwned);
    }

    [Fact]
    public void Already_running_host_must_not_be_stopped_by_new_coordinator()
    {
        var externalHost = new FakeHost { IsRunning = true };
        using var coordinator = new MZP2OwnedHostAdmission(externalHost);
        var result = coordinator.StartOwnedTestHost(Request());
        Assert.Equal(MZP2AdmissionReason.AlreadyRunning, result.Reason);
        Assert.Equal(MZP2AdmissionState.Denied, result.State);
        Assert.Equal(0, externalHost.Starts);
        coordinator.StopOwnedTestHost();
        Assert.Equal(0, externalHost.Stops);
        Assert.True(externalHost.IsRunning);
    }

    [Fact]
    public async Task Restart_with_unresolved_prior_intent_never_replays_or_starts_host()
    {
        var firstHost = new FakeHost();
        using (var first = new MZP2OwnedHostAdmission(firstHost))
        {
            first.StartOwnedTestHost(Request());
            var wiring = firstHost.Deliver(Capture());
            await wiring.Actions.EndTurnAsync();
            first.StopOwnedTestHost();
        }
        var nextHost = new FakeHost();
        using var next = new MZP2OwnedHostAdmission(nextHost);
        var restarted = next.StartOwnedTestHost(Request());
        Assert.Equal(MZP2AdmissionState.ReconciliationRequired, restarted.State);
        Assert.Equal(MZP2AdmissionReason.UnresolvedEarlierOperations, restarted.Reason);
        Assert.Equal(1, restarted.UnresolvedOperations);
        Assert.False(restarted.HostOwned);
        Assert.Equal(0, nextHost.Starts);
        Assert.Equal(0, nextHost.Stops);
    }

    [Fact]
    public void Already_open_exclusive_journal_lease_blocks_another_owner_before_host_start()
    {
        var owner1 = new FakeHost();
        using var first = new MZP2OwnedHostAdmission(owner1);
        first.StartOwnedTestHost(Request());
        var owner2 = new FakeHost();
        using var second = new MZP2OwnedHostAdmission(owner2);
        var result = second.StartOwnedTestHost(Request());
        Assert.Equal(MZP2AdmissionReason.JournalUnavailable, result.Reason);
        Assert.Equal(0, owner2.Starts);
        Assert.Equal(0, owner2.Stops);
    }

    [Fact]
    public void Stop_not_owned_is_idempotent_and_never_calls_host_stop()
    {
        var host = new FakeHost();
        using var coordinator = new MZP2OwnedHostAdmission(host);
        Assert.Equal(MZP2AdmissionState.Stopped, coordinator.StopOwnedTestHost().State);
        Assert.Equal(MZP2AdmissionState.Stopped, coordinator.StopOwnedTestHost().State);
        Assert.Equal(0, host.Stops);
    }

    [Fact]
    public void Duplicate_start_never_replaces_an_active_owned_journal_or_factory()
    {
        var host = new FakeHost();
        using var coordinator = new MZP2OwnedHostAdmission(host);
        Assert.Equal(MZP2AdmissionState.AwaitingOwnedCapture,
            coordinator.StartOwnedTestHost(Request()).State);
        var existing = host.ProtectedFactory;
        var duplicate = coordinator.StartOwnedTestHost(Request());
        Assert.Equal(MZP2AdmissionReason.AlreadyRunning, duplicate.Reason);
        Assert.Equal(MZP2AdmissionState.AwaitingOwnedCapture, duplicate.State);
        Assert.Same(existing, host.ProtectedFactory);
        Assert.Equal(1, host.Starts);
        Assert.Equal(0, host.Stops);
    }

    [Fact]
    public void Failed_stop_keeps_owned_journal_and_allows_retry_without_touching_other_hosts()
    {
        var host = new FakeHost { ThrowOnStop = true };
        using var coordinator = new MZP2OwnedHostAdmission(host);
        coordinator.StartOwnedTestHost(Request());
        var stopped = coordinator.StopOwnedTestHost();
        Assert.Equal(MZP2AdmissionState.Faulted, stopped.State);
        Assert.True(stopped.HostOwned);
        Assert.Equal(1, host.Stops);
        host.ThrowOnStop = false;
        var retried = coordinator.StopOwnedTestHost();
        Assert.Equal(MZP2AdmissionState.Stopped, retried.State);
        Assert.Equal(2, host.Stops);
    }

    [Fact]
    public void Diagnostics_never_expose_account_hash_executable_path_or_PID()
    {
        var host = new FakeHost();
        using var coordinator = new MZP2OwnedHostAdmission(host);
        coordinator.StartOwnedTestHost(Request());
        var text = coordinator.Diagnostics.ToString();
        Assert.DoesNotContain(Digest, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("owned-Dofus.exe", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Pid.ToString(), text, StringComparison.OrdinalIgnoreCase);
    }
}
