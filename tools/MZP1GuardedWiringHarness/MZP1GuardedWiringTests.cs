using GivreClone.Engine;
using GivreClone.Engine.Recovery;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using Xunit;

namespace MZP1GuardedWiringHarness;

public sealed class MZP1GuardedWiringTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        "mzp1-guard-wiring-" + Guid.NewGuid().ToString("N"));
    private const int ConnectionId = 51;
    private const int ProcessId = 24680;
    private const string Version = "3.7.10.10";
    private static readonly MZP0SessionIdentity Identity = new(ProcessId, 638960000000000000,
        ConnectionId, Version);

    public MZP1GuardedWiringTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); }
        catch (IOException) { }
    }

    private MZP0RecoveryJournal Journal(string name = "journal.json")
        => new(Path.Combine(_directory, name));

    private static ProtocolCatalog Catalog()
    {
        const string schema = """
            message FightTurnFinishRequest {
              bool is_afk = 1;
            }
            message MapMovementRequest {
              int64 map_id = 1;
              bool cautious = 2;
              repeated int32 key_cells = 3;
            }
            """;
        const string names = """
            {"type.ankama.com/finish":"MessageType.FightTurnFinishRequest",
             "type.ankama.com/move":"MessageType.MapMovementRequest",
             "type.ankama.com/positive":"MessageType.PositiveResponse"}
            """;
        const string fields = """
            {"GameMessage":{"request":1,"event":2,"response":3},
             "Request":{"content":1,"uid":2},
             "Event":{"content":3},
             "Response":{"content":1,"uid":2}}
            """;
        var catalog = ProtocolCatalog.LoadFromStrings(schema, names, null, fields);
        EnvelopeBuilder.UseCatalog(catalog.FieldNumber);
        return catalog;
    }

    private sealed class Authority : IMZP0AuthenticatedSessionSource
    {
        public bool Trusted { get; set; } = true;
        public bool Throws { get; set; }
        public MZP0SessionIdentity Value { get; set; } = Identity;
        public bool TryGetAuthenticatedIdentity(out MZP0SessionIdentity identity)
        {
            if (Throws) throw new InvalidOperationException("private external identity error");
            identity = Value;
            return Trusted;
        }
    }

    private sealed class Verifier : IMZP0PositiveResponseVerifier
    {
        public bool Trusted { get; set; }
        public Action? OnVerify { get; set; }
        public int Calls { get; private set; }
        public bool IsVerifiedPositiveResponse(BotMessage inbound,
            MZP0SessionGeneration expectedGeneration, int expectedUid)
        {
            Calls++;
            OnVerify?.Invoke();
            return Trusted && expectedGeneration.Identity == Identity &&
                inbound.IsFromServer && inbound.ConnectionId == ConnectionId &&
                inbound.Name == "PositiveResponse" &&
                EnvelopeBuilder.ReadEnvelopeUid(inbound.RawFrame) == expectedUid;
        }
    }

    private sealed class Sink : IFrameSink
    {
        public int Calls { get; private set; }
        public bool Send(Direction direction, byte[] gameMessageBytes)
        {
            Calls++;
            Assert.Equal(Direction.ClientToServer, direction);
            Assert.NotEmpty(gameMessageBytes);
            return true;
        }
    }

    private sealed class Both : IMZP0AuthenticatedSessionSource, IMZP0PositiveResponseVerifier
    {
        public bool TryGetAuthenticatedIdentity(out MZP0SessionIdentity identity)
        { identity = Identity; return true; }
        public bool IsVerifiedPositiveResponse(BotMessage inbound,
            MZP0SessionGeneration expectedGeneration, int expectedUid) => true;
    }

    private static MZP1GuardedEngineWiring Build(MZP0RecoveryJournal? journal, Sink sink,
        IMZP0AuthenticatedSessionSource? source, IMZP0PositiveResponseVerifier? verifier,
        int conn = ConnectionId, string version = Version)
        => MZP1GuardedEngineWiringFactory.Create(conn, version, Catalog(),
            sink, new NoPacer(), journal, source, verifier);

    private static CapturedMessage Capture(int uid = 1, int conn = ConnectionId,
        uint pid = ProcessId, Direction direction = Direction.ServerToClient,
        bool response = true)
    {
        byte[] raw = RawProtobuf.Encode(new[]
        {
            new ProtoField { Field = response ? 3 : 2, Wire = WireType.Len,
                Message = new List<ProtoField>
                { new() { Field = 2, Wire = WireType.Varint, Varint = (ulong)uid } } }
        });
        return new CapturedMessage(uid, direction, conn, pid, "positive",
            Array.Empty<ProtoField>(), raw);
    }

    [Theory]
    [InlineData("journal", MZP1Admission.MissingJournal)]
    [InlineData("source", MZP1Admission.MissingIdentityAuthority)]
    [InlineData("response", MZP1Admission.MissingResponseAuthority)]
    public async Task Missing_dependency_creates_real_BotContext_with_denied_IActionSender(
        string missing, MZP1Admission expected)
    {
        using var journal = Journal();
        var sink = new Sink();
        var source = new Authority();
        var verifier = new Verifier();
        var wiring = Build(missing == "journal" ? null : journal, sink,
            missing == "source" ? null : source,
            missing == "response" ? null : verifier);

        Assert.Equal(expected, wiring.Admission);
        Assert.IsType<BotContext>(wiring.Context);
        Assert.Same(wiring.Actions, wiring.Context.Actions);
        Assert.Same(wiring.State, wiring.Context.State);
        var error = await Assert.ThrowsAsync<MZP1AdmissionException>(
            () => wiring.Context.Actions.EndTurnAsync());
        Assert.Equal(expected, error.Reason);
        Assert.Equal(0, sink.Calls);
        Assert.Null(journal.ActiveGeneration);
        Assert.Empty(journal.Records);
        Assert.Equal(MZP1CaptureResult.Blocked, wiring.TryDispatchCaptured(Capture()));
    }

    [Fact]
    public async Task Single_object_claiming_both_authorities_rejected_without_journal_activation()
    {
        using var journal = Journal();
        var both = new Both();
        var sink = new Sink();
        var wiring = Build(journal, sink, both, both);
        Assert.Equal(MZP1Admission.SameAuthority, wiring.Admission);
        await Assert.ThrowsAsync<MZP1AdmissionException>(() => wiring.Actions.EndTurnAsync());
        Assert.Null(journal.ActiveGeneration);
        Assert.Equal(0, sink.Calls);
    }

    [Theory]
    [InlineData("untrusted", MZP1Admission.IdentityUnverified)]
    [InlineData("throws", MZP1Admission.IdentityUnverified)]
    [InlineData("birth", MZP1Admission.IdentityMismatch)]
    [InlineData("pid", MZP1Admission.IdentityMismatch)]
    [InlineData("connection", MZP1Admission.IdentityMismatch)]
    [InlineData("protocol", MZP1Admission.IdentityMismatch)]
    public async Task Invalid_or_unverified_origin_never_sends_or_activates(string defect,
        MZP1Admission expected)
    {
        using var journal = Journal();
        var sink = new Sink();
        var source = new Authority();
        switch (defect)
        {
            case "untrusted": source.Trusted = false; break;
            case "throws": source.Throws = true; break;
            case "birth": source.Value = Identity with { ProcessStartUtcTicks = 0 }; break;
            case "pid": source.Value = Identity with { ProcessId = 0 }; break;
            case "connection": source.Value = Identity with { ConnectionId = 52 }; break;
            case "protocol": source.Value = Identity with { ProtocolVersion = "wrong" }; break;
        }

        var wiring = Build(journal, sink, source, new Verifier());
        Assert.Equal(expected, wiring.Admission);
        await Assert.ThrowsAsync<MZP1AdmissionException>(() => wiring.Actions.EndTurnAsync());
        Assert.Empty(journal.Records);
        Assert.Equal(0, sink.Calls);
        Assert.Null(journal.ActiveGeneration);
    }

    [Fact]
    public async Task Authenticated_factory_uses_real_IActionSender_behind_BotContext_and_BehaviorHost()
    {
        using var journal = Journal();
        var sink = new Sink();
        var source = new Authority();
        var wiring = Build(journal, sink, source, new Verifier());

        Assert.Equal(MZP1Admission.Ready, wiring.Admission);
        Assert.IsType<MZP0GuardedActionSender>(wiring.Context.Actions);
        Assert.Same(wiring.State, wiring.Context.State);
        Assert.Same(wiring.Actions, wiring.Context.Actions);
        Assert.Same(wiring.Bus, wiring.Bus);
        Assert.NotNull(wiring.Behaviors);
        Assert.Equal(1, journal.Revision); // one activated generation, no operation yet

        // This is the actual helper used by IBehavior implementations.
        var result = await wiring.Context.Actions.EndTurnAsync();
        Assert.True(result.Sent);
        Assert.Null(result.Frame);
        Assert.Equal(1, sink.Calls);
        Assert.Equal(MZP0DeliveryState.DispatchWindowOpened, Assert.Single(journal.Records).State);
        Assert.Equal(3, journal.Revision);
    }

    [Fact]
    public async Task Real_BehaviorHost_executes_behaviors_through_the_guarded_context()
    {
        using var journal = Journal();
        var sink = new Sink();
        var wiring = Build(journal, sink, new Authority(), new Verifier());
        var behavior = new OneShotBehavior();
        wiring.Behaviors.Start(behavior);
        var action = await behavior.Result.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.True(action.Sent);
        Assert.Null(action.Frame);
        Assert.Equal(1, sink.Calls);
        Assert.Single(journal.Records);
        wiring.Behaviors.Stop();
    }

    private sealed class OneShotBehavior : IBehavior
    {
        private readonly TaskCompletionSource<ActionResult> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ActionResult> Result => _result.Task;
        public string Name => "offline-protected-action";
        public async Task RunAsync(IBotContext context, CancellationToken ct)
        {
            try { _result.TrySetResult(await context.Actions.EndTurnAsync(ct)); }
            catch (Exception error) { _result.TrySetException(error); }
        }
    }

    [Fact]
    public async Task Correct_bound_capture_is_fanned_out_and_acks_only_with_positive_response_proof()
    {
        using var journal = Journal();
        var sink = new Sink();
        var verifier = new Verifier();
        var wiring = Build(journal, sink, new Authority(), verifier);
        var action = await wiring.Actions.EndTurnAsync();

        var incoming = wiring.Bus.WaitForAsync(m => m.Is("PositiveResponse"), TimeSpan.FromSeconds(2));
        Assert.Equal(MZP1CaptureResult.Accepted, wiring.TryDispatchCaptured(Capture(action.Uid)));
        var published = await incoming;
        Assert.Equal("PositiveResponse", published.Name);
        Assert.Empty(published.RawFrame);
        Assert.Empty(published.Payload);
        Assert.Equal(MZP0DeliveryState.DispatchWindowOpened, Assert.Single(journal.Records).State);
        Assert.Equal(1, verifier.Calls); // consulted but responded negative

        verifier.Trusted = true;
        Assert.Equal(MZP1CaptureResult.Accepted, wiring.TryDispatchCaptured(Capture(action.Uid)));
        Assert.Equal(MZP0DeliveryState.Acknowledged, Assert.Single(journal.Records).State);
    }

    [Fact]
    public async Task Identity_lost_inside_response_verifier_does_not_ack_or_fan_out()
    {
        using var journal = Journal();
        var sink = new Sink();
        var source = new Authority();
        var verifier = new Verifier { Trusted = true, OnVerify = () => source.Trusted = false };
        var wiring = Build(journal, sink, source, verifier);
        var action = await wiring.Actions.EndTurnAsync();
        int forwarded = 0;
        using var sub = wiring.Bus.Subscribe(_ => forwarded++);
        Assert.Equal(MZP1CaptureResult.Blocked,
            wiring.TryDispatchCaptured(Capture(action.Uid)));
        Assert.Equal(0, forwarded);
        Assert.Equal(MZP0DeliveryState.DispatchWindowOpened, Assert.Single(journal.Records).State);
        Assert.Equal(MZP1Admission.Revoked, wiring.Admission);
    }

    [Theory]
    [InlineData(0u, 51, MZP1CaptureResult.MissingProcessProof)]
    [InlineData(24681u, 51, MZP1CaptureResult.MismatchedSession)]
    [InlineData(24680u, 52, MZP1CaptureResult.MismatchedSession)]
    public async Task Unknown_or_wrong_capture_identity_cannot_reach_packet_bus_or_ack(
        uint pid, int connection, MZP1CaptureResult expected)
    {
        using var journal = Journal();
        var verifier = new Verifier { Trusted = true };
        var wiring = Build(journal, new Sink(), new Authority(), verifier);
        var action = await wiring.Actions.EndTurnAsync();
        int captured = 0;
        using var subscribed = wiring.Bus.Subscribe(_ => captured++);
        Assert.Equal(expected, wiring.TryDispatchCaptured(Capture(action.Uid, connection, pid)));
        Assert.Equal(0, captured);
        Assert.Equal(0, verifier.Calls);
        Assert.Equal(MZP0DeliveryState.DispatchWindowOpened, Assert.Single(journal.Records).State);
    }

    [Fact]
    public async Task Identity_drift_revokes_wiring_and_stops_all_followup_actions_or_captures()
    {
        using var journal = Journal();
        var sink = new Sink();
        var source = new Authority();
        var wiring = Build(journal, sink, source, new Verifier());
        Assert.Equal(MZP1Admission.Ready, wiring.Admission);
        source.Value = Identity with { ProcessStartUtcTicks = Identity.ProcessStartUtcTicks + 7 };
        Assert.Equal(MZP1Admission.Revoked, wiring.Admission);
        source.Value = Identity;
        Assert.Equal(MZP1CaptureResult.Blocked, wiring.TryDispatchCaptured(Capture()));
        await Assert.ThrowsAsync<MZP0GuardedActionException>(() => wiring.Actions.EndTurnAsync());
        Assert.Equal(0, sink.Calls);
    }

    [Fact]
    public async Task Explicit_Revoke_disables_healthy_instance_irreversibly()
    {
        using var journal = Journal();
        var sink = new Sink();
        var wiring = Build(journal, sink, new Authority(), new Verifier());
        wiring.Revoke();
        Assert.Equal(MZP1Admission.Revoked, wiring.Admission);
        await Assert.ThrowsAsync<MZP0GuardedActionException>(() => wiring.Actions.EndTurnAsync());
        Assert.Equal(0, sink.Calls);
        Assert.Empty(journal.Records);
    }

    [Fact]
    public async Task Journal_already_owned_by_another_generation_blocks_second_wiring()
    {
        using var journal = Journal();
        var first = Build(journal, new Sink(), new Authority(), new Verifier());
        var secondSink = new Sink();
        var second = Build(journal, secondSink, new Authority(), new Verifier());
        Assert.Equal(MZP1Admission.Ready, first.Admission);
        Assert.Equal(MZP1Admission.JournalRejected, second.Admission);
        await Assert.ThrowsAsync<MZP1AdmissionException>(() => second.Actions.EndTurnAsync());
        Assert.Equal(0, secondSink.Calls);
    }

    [Fact]
    public void Legacy_BotSession_is_sealed_and_hardcodes_unprotected_ActionSender()
    {
        var original = typeof(BotSession);
        Assert.True(original.IsSealed);
        Assert.Null(original.GetConstructor(new[] { typeof(int), typeof(ProtocolCatalog),
            typeof(IFrameSink), typeof(IPacer), typeof(IActionSender) }));
        var legacy = new BotSession(ConnectionId, Catalog(), new Sink(), new NoPacer());
        Assert.IsType<ActionSender>(legacy.Actions);
        Assert.Same(legacy.Actions, legacy.Context.Actions);
    }

    [Fact]
    public void Factory_fails_fast_for_invalid_constructor_configuration()
    {
        using var journal = Journal();
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(journal, new Sink(),
            new Authority(), new Verifier(), conn: 0));
        Assert.Throws<ArgumentException>(() => Build(journal, new Sink(),
            new Authority(), new Verifier(), version: ""));
    }
}
