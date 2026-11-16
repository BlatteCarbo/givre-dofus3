using GivreClone.Engine;
using GivreClone.Engine.Recovery;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using System.Reflection;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Integration tests compile the production ActionSender, FrameSink, EnvelopeBuilder,
/// ProtocolCatalog, PacketBus, MZP0RecoveryJournal and guarded wrapper, with offline
/// GivreCore-only wire stand-ins. These test contracts, NOT a live authenticated source.
/// </summary>
[Collection("MZP0-EnvelopeBuilder-StaticCatalog")]
public sealed class MZP0GuardedActionSenderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mzp0-guard-" + Guid.NewGuid().ToString("N"));
    private string JournalPath => Path.Combine(_directory, "send.json");
    private static readonly MZP0SessionIdentity Identity = new(3141, 638960000000000000L, 27, "3.7.10.10");

    public MZP0GuardedActionSenderTests() => Directory.CreateDirectory(_directory);
    public void Dispose()
    {
        // This test temporarily installs a synthetic 3.6 envelope in a process-wide static
        // singleton; do not leak it into other tests that consume the 3.7.11.11 catalogue.
        TestSetup.Init();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static ProtocolCatalog Catalog()
    {
        const string proto = """
            message MapMovementRequest {
              int64 map_id = 1;
              repeated int32 key_cells = 2;
            }
            message FightTurnFinishRequest {
              bool is_afk = 1;
            }
            """;
        const string matching = """
            {"type.ankama.com/mov":"MessageType.MapMovementRequest", "type.ankama.com/ftf":"MessageType.FightTurnFinishRequest"}
            """;
        const string fields = """
            {"GameMessage":{"request":1,"event":2,"response":3},
             "Request":{"content":1,"uid":2},"Response":{"content":1,"uid":2},
             "Event":{"content":3}}
            """;
        var catalog = ProtocolCatalog.LoadFromStrings(proto, matching, null, fields);
        EnvelopeBuilder.UseCatalog(catalog.FieldNumber);
        return catalog;
    }

    private sealed class AuthenticatedSource : IMZP0AuthenticatedSessionSource
    {
        public bool Authorized { get; set; }
        public MZP0SessionIdentity Session { get; set; } = Identity;
        public int Verifications { get; private set; }
        public bool TryGetAuthenticatedIdentity(out MZP0SessionIdentity identity)
        {
            Verifications++;
            identity = Session;
            return Authorized;
        }
    }

    private sealed class PositiveResponseVerifier : IMZP0PositiveResponseVerifier
    {
        public bool TrustedResponse { get; set; }
        public int Calls { get; private set; }
        public bool IsVerifiedPositiveResponse(BotMessage inbound, MZP0SessionGeneration expectedGeneration, int expectedUid)
        {
            Calls++;
            if (!TrustedResponse || expectedGeneration.Identity != Identity ||
                expectedGeneration.Nonce == Guid.Empty || inbound.Name != "VerifiedPositive") return false;
            // Test only: exercise positive response branch separately from server events.
            var fields = RawProtobuf.Parse(inbound.RawFrame);
            return fields.Any(f => f.Field == 3) &&
                EnvelopeBuilder.ReadEnvelopeUid(inbound.RawFrame) == expectedUid;
        }
    }

    private sealed class CapturingSink : IFrameSink
    {
        public List<(Direction Direction, byte[] Bytes)> Sends { get; } = new();
        public bool Writable { get; set; } = true;
        public bool Throws { get; set; }
        public Action? BeforeSend { get; set; }
        public bool Send(Direction direction, byte[] gameMessageBytes)
        {
            BeforeSend?.Invoke();
            Sends.Add((direction, gameMessageBytes.ToArray()));
            if (Throws) throw new IOException("transport sensitive exception contents");
            return Writable;
        }
    }

    private sealed class TestRig : IDisposable
    {
        public MZP0RecoveryJournal Journal { get; }
        public CapturingSink Sink { get; } = new();
        public AuthenticatedSource Source { get; } = new();
        public PositiveResponseVerifier Responses { get; } = new();
        public PacketBus Bus { get; } = new();
        public MZP0GuardedActionSender Guard { get; }

        public TestRig(string path, bool attested = true, RequestCorrelator? correlator = null, IPacer? pacer = null)
        {
            Journal = new MZP0RecoveryJournal(path);
            Source.Authorized = attested;
            Guard = new MZP0GuardedActionSender(Catalog(), Sink, Bus, pacer ?? new NoPacer(),
                correlator ?? new RequestCorrelator(), Journal, Source, Responses);
        }

        public void Dispose() => Journal.Dispose();
    }

    private static Task<ActionResult> Send(IActionSender sender, CancellationToken ct = default)
        => sender.SendRequestAsync("MapMovementRequest", p => p.Int("map_id", 100).Ints("key_cells", new[] { 30, 44 }), ct);

    private static BotMessage Response(int uid, int connection = 27, Direction direction = Direction.ServerToClient,
        string? name = "VerifiedPositive", bool responseBranch = true)
    {
        // Real EnvelopeBuilder reads current mapping fields, not synthetic acknowledgment objects.
        var raw = RawProtobuf.Encode(new[]
        {
            new ProtoField
            {
                Field = responseBranch ? 3 : 2, Wire = WireType.Len,
                Message = new List<ProtoField> { new() { Field = 2, Wire = WireType.Varint, Varint = (ulong)uid } }
            }
        });
        return new BotMessage(1, direction, connection, "rsp", name,
            new List<ProtoField> { new() { Field = 1, Wire = WireType.Len, String = "private account field" } }, raw);
    }

    [Fact]
    public async Task Interface_implemented_and_real_action_sender_sends_only_after_two_durable_journal_commits()
    {
        using var rig = new TestRig(JournalPath);
        IActionSender interfaceSender = rig.Guard;
        rig.Guard.ActivateAuthenticatedSession();
        long revisionAtTransport = -1;
        rig.Sink.BeforeSend = () =>
        {
            revisionAtTransport = rig.Journal.Revision;
            Assert.Equal(MZP0DeliveryState.DispatchWindowOpened, Assert.Single(rig.Journal.Records).State);
        };

        ActionResult result = await Send(interfaceSender);
        Assert.True(result.Sent);
        Assert.Null(result.Frame);
        Assert.Equal(1, result.Uid);
        Assert.Equal(3, revisionAtTransport); // Activate, BeginIntent, OpenDispatchWindow
        Assert.Single(rig.Sink.Sends);
        Assert.Equal(Direction.ClientToServer, rig.Sink.Sends[0].Direction);
        Assert.Equal(result.Uid, EnvelopeBuilder.ReadEnvelopeUid(rig.Sink.Sends[0].Bytes));
        Assert.Equal(MZP0DeliverySemantics.NonIdempotent, Assert.Single(rig.Journal.Records).Semantics);
        Assert.Equal(MZP0RecoveryDisposition.AwaitingVerifiedAcknowledgment,
            rig.Journal.Assess(Assert.Single(rig.Journal.Records).OperationId));
    }

    [Fact]
    public async Task Unauthenticated_source_prevents_send_and_creates_no_journal_or_fake_generation()
    {
        using var rig = new TestRig(JournalPath, attested: false);
        Assert.Throws<MZP0GuardedActionException>(() => rig.Guard.ActivateAuthenticatedSession());
        var ex = await Assert.ThrowsAsync<MZP0GuardedActionException>(() => Send(rig.Guard));
        Assert.Equal(MZP0GuardFailure.SessionNotAuthenticated, ex.Failure);
        Assert.Empty(rig.Sink.Sends);
        Assert.Empty(rig.Journal.Records);
        Assert.Null(rig.Journal.ActiveGeneration);
    }

    [Fact]
    public async Task Withdrawn_or_changed_origin_blocks_dispatch_and_permanently_revokes_this_wrapper()
    {
        using var rig = new TestRig(JournalPath);
        rig.Guard.ActivateAuthenticatedSession();
        rig.Source.Session = Identity with { ProcessStartUtcTicks = Identity.ProcessStartUtcTicks + 1 };

        var error = await Assert.ThrowsAsync<MZP0GuardedActionException>(() => Send(rig.Guard));
        Assert.Equal(MZP0GuardFailure.SessionChanged, error.Failure);
        Assert.Empty(rig.Sink.Sends);
        rig.Source.Session = Identity;
        Assert.False(rig.Guard.IsAuthenticated);
        await Assert.ThrowsAsync<MZP0GuardedActionException>(() => Send(rig.Guard));
    }

    private sealed class BlockedPacer : IPacer
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public void Release() => _released.TrySetResult();
        public async Task BeforeActionAsync(CancellationToken ct = default)
        {
            _entered.TrySetResult();
            await _released.Task.WaitAsync(ct);
        }
    }

    [Fact]
    public async Task Authentication_lost_during_pacing_refuses_transport_and_journaling()
    {
        var pacer = new BlockedPacer();
        using var rig = new TestRig(JournalPath, pacer: pacer);
        rig.Guard.ActivateAuthenticatedSession();
        var pending = Send(rig.Guard);
        await pacer.Entered.WaitAsync(TimeSpan.FromSeconds(3));
        rig.Source.Authorized = false;
        pacer.Release();
        var error = await Assert.ThrowsAsync<MZP0GuardedActionException>(() => pending);
        Assert.Equal(MZP0GuardFailure.SessionChanged, error.Failure);
        Assert.Empty(rig.Journal.Records);
        Assert.Empty(rig.Sink.Sends);
    }

    [Fact]
    public async Task Cancel_during_pacing_does_not_leave_an_intent_or_dispatch_window()
    {
        var pacer = new BlockedPacer();
        using var rig = new TestRig(JournalPath, pacer: pacer);
        rig.Guard.ActivateAuthenticatedSession();
        using var cts = new CancellationTokenSource();
        var pending = Send(rig.Guard, cts.Token);
        await pacer.Entered.WaitAsync(TimeSpan.FromSeconds(3));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(rig.Journal.Records);
        Assert.Empty(rig.Sink.Sends);
        pacer.Release();
    }

    [Fact]
    public async Task Cancellation_during_payload_build_blocks_write_even_if_pacer_ignores_token()
    {
        using var rig = new TestRig(JournalPath);
        rig.Guard.ActivateAuthenticatedSession();
        using var cts = new CancellationTokenSource();
        IActionSender actions = rig.Guard;
        var pending = actions.SendRequestAsync("MapMovementRequest", p =>
        {
            p.Int("map_id", 100);
            cts.Cancel();
        }, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(rig.Journal.Records);
        Assert.Empty(rig.Sink.Sends);
    }

    [Fact]
    public async Task Generic_event_or_sent_action_cannot_certify_positive_ACK()
    {
        using var rig = new TestRig(JournalPath);
        rig.Guard.ActivateAuthenticatedSession();
        ActionResult result = await Send(rig.Guard);
        rig.Responses.TrustedResponse = false;
        Assert.False(rig.Guard.TryAcknowledgeVerifiedInbound(Response(result.Uid)));
        Assert.False(rig.Guard.TryAcknowledgeVerifiedInbound(Response(result.Uid, name: "Event")));
        Assert.False(rig.Guard.TryAcknowledgeVerifiedInbound(Response(result.Uid, responseBranch: false)));
        Assert.Equal(MZP0DeliveryState.DispatchWindowOpened, Assert.Single(rig.Journal.Records).State);
    }

    [Fact]
    public async Task ACK_requires_distinct_verifier_correct_UID_server_direction_and_connection()
    {
        using var rig = new TestRig(JournalPath);
        rig.Guard.ActivateAuthenticatedSession();
        var action = await Send(rig.Guard);
        rig.Responses.TrustedResponse = true;
        Assert.False(rig.Guard.TryAcknowledgeVerifiedInbound(Response(action.Uid + 1)));
        Assert.False(rig.Guard.TryAcknowledgeVerifiedInbound(Response(action.Uid, connection: 555)));
        Assert.False(rig.Guard.TryAcknowledgeVerifiedInbound(Response(action.Uid, direction: Direction.ClientToServer)));
        Assert.False(rig.Guard.TryAcknowledgeVerifiedInbound(Response(action.Uid, name: "Event")));
        Assert.False(rig.Guard.TryAcknowledgeVerifiedInbound(Response(action.Uid, responseBranch: false)));
        Assert.Equal(MZP0DeliveryState.DispatchWindowOpened, Assert.Single(rig.Journal.Records).State);

        Assert.True(rig.Guard.TryAcknowledgeVerifiedInbound(Response(action.Uid)));
        Assert.Equal(MZP0DeliveryState.Acknowledged, Assert.Single(rig.Journal.Records).State);
        Assert.False(rig.Guard.TryAcknowledgeVerifiedInbound(Response(action.Uid)));
    }

    [Fact]
    public async Task Even_a_permissive_response_adapter_cannot_ack_an_event_or_request_envelope()
    {
        using var journal = new MZP0RecoveryJournal(JournalPath);
        var source = new AuthenticatedSource { Authorized = true };
        var wrongVerifier = new UnconditionallyPositiveVerifier();
        var sink = new CapturingSink();
        var guard = new MZP0GuardedActionSender(Catalog(), sink, new PacketBus(),
            new NoPacer(), new RequestCorrelator(), journal, source, wrongVerifier);
        guard.ActivateAuthenticatedSession();
        var action = await Send(guard);

        Assert.False(guard.TryAcknowledgeVerifiedInbound(Response(action.Uid, responseBranch: false)));
        var requestBytes = RawProtobuf.Encode(new[]
        {
            new ProtoField { Field = 1, Wire = WireType.Len, Message = new List<ProtoField>
            {
                new() { Field = 2, Wire = WireType.Varint, Varint = (ulong)action.Uid }
            } }
        });
        Assert.False(guard.TryAcknowledgeVerifiedInbound(Response(action.Uid) with { RawFrame = requestBytes }));
        var ambiguous = RawProtobuf.Encode(new[]
        {
            new ProtoField { Field = 1, Wire = WireType.Len, Message = new List<ProtoField>
            {
                new() { Field = 2, Wire = WireType.Varint, Varint = (ulong)action.Uid }
            } },
            new ProtoField { Field = 3, Wire = WireType.Len, Message = new List<ProtoField>
            {
                new() { Field = 2, Wire = WireType.Varint, Varint = (ulong)action.Uid }
            } }
        });
        Assert.False(guard.TryAcknowledgeVerifiedInbound(Response(action.Uid) with { RawFrame = ambiguous }));
        Assert.Equal(0, wrongVerifier.Calls);
        Assert.Equal(MZP0DeliveryState.DispatchWindowOpened, Assert.Single(journal.Records).State);
    }

    private sealed class UnconditionallyPositiveVerifier : IMZP0PositiveResponseVerifier
    {
        public int Calls { get; private set; }
        public bool IsVerifiedPositiveResponse(BotMessage inbound, MZP0SessionGeneration expectedGeneration, int expectedUid)
        {
            Calls++;
            return true;
        }
    }

    [Fact]
    public async Task Returned_WaitForEvent_message_is_scrubbed_and_does_not_ack_journal()
    {
        using var rig = new TestRig(JournalPath);
        rig.Guard.ActivateAuthenticatedSession();
        var action = await Send(rig.Guard);
        var nextEvent = rig.Guard.WaitForEventAsync("VerifiedPositive", TimeSpan.FromSeconds(2));
        rig.Bus.Publish(Response(action.Uid));
        var returned = await nextEvent;
        Assert.Equal("VerifiedPositive", returned.Name);
        Assert.Empty(returned.RawFrame);
        Assert.Empty(returned.Payload);
        Assert.Equal(MZP0DeliveryState.DispatchWindowOpened, Assert.Single(rig.Journal.Records).State);
    }

    [Fact]
    public async Task Failed_transport_false_is_uncertain_not_confirmed_and_does_not_leak_frame()
    {
        using var rig = new TestRig(JournalPath);
        rig.Guard.ActivateAuthenticatedSession();
        rig.Sink.Writable = false;
        var result = await Send(rig.Guard);
        Assert.Equal(SendOutcome.ConnectionClosed, result.Outcome);
        Assert.Null(result.Frame);
        Assert.Equal(MZP0DeliveryState.Uncertain, Assert.Single(rig.Journal.Records).State);
    }

    [Fact]
    public async Task Throw_after_possible_transport_send_is_uncertain_and_sanitizes_error()
    {
        using var rig = new TestRig(JournalPath);
        rig.Guard.ActivateAuthenticatedSession();
        rig.Sink.Throws = true;
        var error = await Assert.ThrowsAsync<MZP0GuardedActionException>(() => Send(rig.Guard));
        Assert.Equal(MZP0GuardFailure.TransportUncertain, error.Failure);
        Assert.DoesNotContain("transport sensitive", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Single(rig.Sink.Sends);
        Assert.Equal(MZP0DeliveryState.Uncertain, Assert.Single(rig.Journal.Records).State);
    }

    [Fact]
    public async Task UID_reuse_is_rejected_before_transport_send_even_after_prune()
    {
        var correlator = new RequestCorrelator();
        using var rig = new TestRig(JournalPath, correlator: correlator);
        var generation = rig.Guard.ActivateAuthenticatedSession();
        var first = await Send(rig.Guard);
        rig.Responses.TrustedResponse = true;
        Assert.True(rig.Guard.TryAcknowledgeVerifiedInbound(Response(first.Uid)));
        Assert.Equal(1, rig.Journal.PruneTerminal(generation));
        Assert.Empty(rig.Journal.Records);

        // Inject a corrupted correlator watermark for this negative regression:
        // ActionSender still generates UID=1, but the durable journal forbids reuse.
        typeof(RequestCorrelator).GetField("_uid", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(correlator, 0);
        var error = await Assert.ThrowsAsync<MZP0GuardedActionException>(() => Send(rig.Guard));
        Assert.Equal(MZP0GuardFailure.JournalRejected, error.Failure);
        Assert.Single(rig.Sink.Sends);
        Assert.Empty(rig.Journal.Records);

        var second = await Send(rig.Guard);
        Assert.True(second.Sent);
        Assert.Equal(2, second.Uid);
        Assert.Equal(2, rig.Sink.Sends.Count);
    }

    [Fact]
    public async Task Concurrent_actions_still_use_strictly_increasing_UID_and_durable_commit_order()
    {
        using var rig = new TestRig(JournalPath);
        rig.Guard.ActivateAuthenticatedSession();
        var tasks = Enumerable.Range(0, 12).Select(_ => Send(rig.Guard)).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Equal(Enumerable.Range(1, 12), results.Select(r => r.Uid));
        Assert.Equal(12, rig.Journal.Records.Count);
        Assert.Equal(12, rig.Sink.Sends.Count);
        Assert.All(results, r => Assert.Null(r.Frame));
    }

    [Fact]
    public async Task Unknown_mapping_and_schema_fail_without_journaling_or_transport()
    {
        using var rig = new TestRig(JournalPath);
        rig.Guard.ActivateAuthenticatedSession();
        var outcome = await rig.Guard.SendRequestAsync("NoSuchRequest", _ => { });
        Assert.Equal(SendOutcome.NotMapped, outcome.Outcome);
        Assert.Equal(0, outcome.Uid);
        Assert.Null(outcome.Frame);
        Assert.Empty(rig.Journal.Records);
        Assert.Empty(rig.Sink.Sends);
    }

    [Fact]
    public async Task Reopen_after_crash_never_replays_or_auto_acks_an_open_dispatch_window()
    {
        int uid;
        using (var rig = new TestRig(JournalPath))
        {
            rig.Guard.ActivateAuthenticatedSession();
            uid = (await Send(rig.Guard)).Uid;
        }
        using var recovered = new TestRig(JournalPath);
        Assert.Empty(recovered.Sink.Sends);
        Assert.False(recovered.Guard.TryAcknowledgeVerifiedInbound(Response(uid)));
        recovered.Guard.ActivateAuthenticatedSession();
        Assert.Equal(MZP0DeliveryState.Uncertain, Assert.Single(recovered.Journal.Records).State);
        recovered.Responses.TrustedResponse = true;
        Assert.False(recovered.Guard.TryAcknowledgeVerifiedInbound(Response(uid)));
        Assert.Empty(recovered.Sink.Sends);
    }

    [Fact]
    public void Authenticator_and_responder_cannot_be_the_same_object()
    {
        using var journal = new MZP0RecoveryJournal(JournalPath);
        var combined = new CombinedAuthority();
        Assert.Throws<ArgumentException>(() => new MZP0GuardedActionSender(Catalog(), new CapturingSink(),
            new PacketBus(), new NoPacer(), new RequestCorrelator(), journal, combined, combined));
    }

    private sealed class CombinedAuthority : IMZP0AuthenticatedSessionSource, IMZP0PositiveResponseVerifier
    {
        public bool TryGetAuthenticatedIdentity(out MZP0SessionIdentity identity) { identity = Identity; return true; }
        public bool IsVerifiedPositiveResponse(BotMessage inbound, MZP0SessionGeneration expectedGeneration, int expectedUid) => true;
    }
}

[CollectionDefinition("MZP0-EnvelopeBuilder-StaticCatalog", DisableParallelization = true)]
public sealed class MZP0EnvelopeCatalogueCollection { }
