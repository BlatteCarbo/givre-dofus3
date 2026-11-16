extern alias F06App;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Packet = F06App::GivreClone.App.Services.DecodedPacket;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using Link = F06App::GivreClone.App.Services.CaptureLinkIdentity;

namespace GivreClone.Tests;

/// <summary>
/// Actual OnDecoded boundary, with synthetic wire and real decoder epoch
/// where required. No game, TCP or local-write acknowledgement is fabricated.
/// An exact self movement echo remains useful evidence, but the currently
/// mapped MapMovementConfirmResponse contains no individual request identity.
/// </summary>
public sealed class MapConfirmAttributionV21Tests
{
    private const uint Pid = 0x4000_2211;
    private const uint OtherPid = 0x4000_2212;
    private const long MapId = 191_104_008;
    private const int Source = 100;
    private const int Destination = 101;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly ProtocolCatalog WireCatalog =
        ProtocolCatalog.LoadFromStrings(null,
            """{"type.ankama.com/confirmv21":"MapMovementConfirmResponse"}""",
            null, null);

    private static FieldInfo Field(string name) =>
        typeof(Launch).GetField(name, Hidden)
        ?? throw new InvalidOperationException("Missing " + name);

    private static IDictionary Table(Launch service, string name)
        => (IDictionary)(Field(name).GetValue(service)
            ?? throw new InvalidOperationException("Missing table " + name));

    private static Launch Service(out ConcurrentQueue<byte[]> sent)
    {
        var service = new Launch();
        sent = new ConcurrentQueue<byte[]>();
        var emitted = sent;
        service.RegisterSocketInjector(Pid, bytes =>
        {
            emitted.Enqueue(bytes.ToArray());
            return true;
        });
        service.RegisterSocketInjector(OtherPid, bytes =>
        {
            emitted.Enqueue(bytes.ToArray());
            return true;
        });
        Table(service, "_lastMap")[Pid] = MapId;
        Table(service, "_serverMapId")[Pid] = MapId;
        Table(service, "_lastMap")[OtherPid] = MapId;
        Table(service, "_serverMapId")[OtherPid] = MapId;
        return service;
    }

    private static Packet Response(uint pid = Pid) => new(pid,
        ClientToServer: false, Code: "confirmv21",
        Name: "MapMovementConfirmResponse",
        Payload: Array.Empty<ProtoField>(), RawFrame: Array.Empty<byte>());

    private static void Receive(Launch service, Packet packet)
        => typeof(Launch).GetMethod("OnDecoded", Hidden)!
            .Invoke(service, [packet]);

    private static void PendingMap(Launch service, uint pid, long moveSequence = 1)
    {
        Table(service, "_pendingMapChange")[pid] =
            (191_104_009, Environment.TickCount64, default(CancellationToken));
        Table(service, "_moveEchoCorrelation")[pid] =
            (moveSequence, Source, Destination, MapId);
        Table(service, "_moveServerEchoSeq")[pid] = moveSequence;
        Table(service, "_moveGuardUntil")[pid] = long.MaxValue;
        Table(service, "_botMoveConfirm")[pid] = (moveSequence, Destination, 2);
    }

    private static void PendingHarvest(Launch service, uint pid, long moveSequence = 1)
    {
        Table(service, "_pendingHarvest")[pid] =
            (42, 99, true, default(CancellationToken));
        Table(service, "_moveEchoCorrelation")[pid] =
            (moveSequence, Source, Destination, MapId);
        Table(service, "_moveServerEchoSeq")[pid] = moveSequence;
        Table(service, "_moveGuardUntil")[pid] = long.MaxValue;
        Table(service, "_botMoveConfirm")[pid] = (moveSequence, Destination, 2);
    }

    private static object Ticket(long seq, Link? link, long epoch)
    {
        var nested = typeof(Launch).GetNestedType("ArrivalIntentV21",
            BindingFlags.NonPublic)!;
        return Activator.CreateInstance(nested,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            args: [seq, MapId, Source, Destination, epoch, link,
                default(CancellationToken)],
            culture: null)!;
    }

    private static void Attach(Launch service, string name,
        uint pid, long seq, Link? link = null, long epoch = 0)
        => Table(service, name)[pid] = Ticket(seq, link, epoch);

    private static string Inspect(Launch service, bool harvest, Packet response)
        => (string)(typeof(Launch).GetMethod("InspectMapConfirmAttributionV21",
            Hidden)!.Invoke(service, [response.Pid, harvest, response])!);

    private static byte[] FramedConfirmation()
    {
        var message = RawProtobuf.Encode(
        [
            new ProtoField
            {
                Field = 3, Wire = WireType.Len,
                Message =
                [
                    new ProtoField
                    {
                        Field = 3, Wire = WireType.Len,
                        Message =
                        [
                            new ProtoField { Field = 1, Wire = WireType.Len,
                                String = "type.ankama.com/confirmv21" }
                        ]
                    }
                ]
            }
        ]);
        return RawProtobuf.EncodeVarint((ulong)message.Length)
            .Concat(message).ToArray();
    }

    private static Packet LinkedResponse(Launch service, uint pid,
        out Decoder decoder)
    {
        decoder = new Decoder(WireCatalog);
        Field("_decoder").SetValue(service, decoder);
        Packet? result = null;
        decoder.Packet += packet => result = packet;
        decoder.Feed(pid, clientToServer: false, FramedConfirmation());
        Assert.NotNull(result);
        Assert.Equal("MapMovementConfirmResponse", result!.Name);
        Assert.True(decoder.IsCurrentLocalEpoch(result));
        return result;
    }

    private static void ObserveSource(Launch service, Link link)
    {
        var ledger = Field("_socketSourceAuthority").GetValue(service)!;
        var observer = ledger.GetType().GetMethod("ObserveFramedChunk")!;
        Assert.True((bool)observer.Invoke(ledger,
            [link, 1L, false, FramedConfirmation().Length])!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_OnDecoded_unattributed_confirmation_never_releases_arrival_or_wait(
        bool harvest)
    {
        var service = Service(out var sent);
        if (harvest) PendingHarvest(service, Pid); else PendingMap(service, Pid);
        using var owner = new CancellationTokenSource();
        var awaitingMove = service.SendAndWaitAsync(Pid, () => true, 30_000, owner.Token);
        int seen = 0;
        service.PacketDecoded += _ => seen++;

        Receive(service, Response());

        Assert.Equal(1, seen); // actual OnDecoded path, not a duplicate policy.
        Assert.Empty(sent);
        Assert.Equal(long.MaxValue, Table(service, "_moveGuardUntil")[Pid]);
        Assert.True(Table(service, "_botMoveConfirm").Contains(Pid));
        Assert.True(Table(service, harvest ? "_pendingHarvest" : "_pendingMapChange")
            .Contains(Pid));
        Assert.False(awaitingMove.IsCompleted);
        owner.Cancel();
    }

    [Fact]
    public void A_response_cannot_consume_B_with_same_pid_map_actor_and_target()
    {
        var service = Service(out var sent);
        PendingMap(service, Pid, moveSequence: 21);
        Attach(service, "_mapArrivalIntentV21", Pid, seq: 21);
        // B replaces A's in-flight local intent; A's delayed response has no
        // wire field that can distinguish it from a B response.
        PendingMap(service, Pid, moveSequence: 22);
        Attach(service, "_mapArrivalIntentV21", Pid, seq: 22);
        Assert.Equal("missing-source-or-epoch", Inspect(service, false, Response()));

        Receive(service, Response());
        Receive(service, Response()); // duplicate/replay also grants nothing.

        Assert.Empty(sent);
        Assert.True(Table(service, "_pendingMapChange").Contains(Pid));
        Assert.Equal(22L, ((ValueTuple<long, int, int>)Table(service,
            "_botMoveConfirm")[Pid]!).Item1);
        Assert.Equal(long.MaxValue, Table(service, "_moveGuardUntil")[Pid]);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Confirmation_after_our_own_injected_jqi_releases_that_move_and_sends_its_map_change(
        bool ownJqi, bool released)
    {
        var service = Service(out var sent);
        PendingMap(service, Pid, moveSequence: 55);
        Attach(service, "_mapArrivalIntentV21", Pid, seq: 55);
        if (ownJqi) Table(service, "_moveConfirmInjectedSeq")[Pid] = 55L;
        using var owner = new CancellationTokenSource();
        var awaitingMove = service.SendAndWaitAsync(Pid, () => true, 30_000, owner.Token);

        Receive(service, Response());

        Assert.True(released == !Table(service, "_botMoveConfirm").Contains(Pid), "move ticket");
        Assert.True(released == !Table(service, "_pendingMapChange").Contains(Pid), "pending map change");
        Assert.True(released == (sent.Count == 1), $"map change sent ({sent.Count})");
        if (released)
            Assert.Equal(Launch.MoveOutcome.Arrived, await awaitingMove.WaitAsync(TimeSpan.FromSeconds(5)));
        else
        {
            Assert.False(awaitingMove.IsCompleted, "script wait");
            owner.Cancel();
            await awaitingMove.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void Our_jqi_for_an_older_move_never_releases_the_current_one()
    {
        var service = Service(out var sent);
        PendingMap(service, Pid, moveSequence: 61);
        Table(service, "_moveConfirmInjectedSeq")[Pid] = 60L;

        Receive(service, Response());

        Assert.Empty(sent);
        Assert.True(Table(service, "_botMoveConfirm").Contains(Pid));
        Assert.True(Table(service, "_pendingMapChange").Contains(Pid));
    }

    [Fact]
    public void Other_pid_confirmation_never_consumes_pending_same_map()
    {
        var service = Service(out var sent);
        PendingHarvest(service, Pid);
        Receive(service, Response(OtherPid));
        Assert.Empty(sent);
        Assert.True(Table(service, "_pendingHarvest").Contains(Pid));
        Assert.Equal(long.MaxValue, Table(service, "_moveGuardUntil")[Pid]);
    }

    [Fact]
    public async Task Unattributed_response_without_map_or_harvest_intent_never_releases_basic_move_or_attack()
    {
        var service = Service(out var sent);
        const long moveSequence = 77;
        Table(service, "_moveEchoCorrelation")[Pid] =
            (moveSequence, Source, Destination, MapId);
        Table(service, "_moveServerEchoSeq")[Pid] = moveSequence;
        Table(service, "_botMoveConfirm")[Pid] = (moveSequence, Destination, 2);
        Table(service, "_moveGuardUntil")[Pid] = 0L; // even after the timed guard expires
        Table(service, "_movingUntil")[Pid] = long.MaxValue;
        Table(service, "_pendingAttack")[Pid] =
            (7L, "Bouftou", default(CancellationToken), MapId,
                (Func<bool>)(() => true), Environment.TickCount64, moveSequence,
                Destination, (Func<int, bool>)(_ => true),
                (Func<int, Func<bool>, bool>)((_, submit) => submit()));
        using var owner = new CancellationTokenSource();
        var awaitingMove = service.SendAndWaitAsync(Pid, () => true, 30_000, owner.Token);

        Receive(service, new Packet(Pid, ClientToServer: true, Code: "jny",
            Name: "MapMovementConfirmRequest", Payload: Array.Empty<ProtoField>(),
            RawFrame: Array.Empty<byte>()));
        Assert.True(Table(service, "_botMoveConfirm").Contains(Pid));
        Assert.Equal(moveSequence, Table(service, "_moveConfirmSubmitted")[Pid]);
        Assert.False(service.MoveTo(Pid, MapId, Source, Destination, _ => true));

        Receive(service, Response());

        Assert.Empty(sent);
        Assert.True(Table(service, "_pendingAttack").Contains(Pid));
        Assert.True(Table(service, "_botMoveConfirm").Contains(Pid));
        Assert.Equal(0L, Table(service, "_moveGuardUntil")[Pid]);
        Assert.Equal(long.MaxValue, Table(service, "_movingUntil")[Pid]);
        Assert.False(awaitingMove.IsCompleted); // V9 caller remains Pending/Uncertain, never Arrived
        Assert.False(service.MoveTo(Pid, MapId, Source, Destination, _ => true));
        Assert.Empty(sent);

        owner.Cancel();
        await awaitingMove.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Exact_source_and_own_echo_remain_diagnostic_without_individual_ack()
    {
        var service = Service(out var sent);
        var packet = LinkedResponse(service, Pid, out var decoder);
        var link = new Link(Guid.NewGuid(), Pid, 5L, 5L, Guid.NewGuid());
        ObserveSource(service, link);
        PendingMap(service, Pid, moveSequence: 33);
        Attach(service, "_mapArrivalIntentV21", Pid, seq: 33,
            link: link, epoch: decoder.CurrentLocalEpoch(Pid));

        Assert.Equal("own-echo-observed-but-confirmation-unattributable",
            Inspect(service, false, packet));
        Receive(service, packet); // actual decoder-stamped OnDecoded.

        Assert.Empty(sent);
        Assert.True(Table(service, "_pendingMapChange").Contains(Pid));
        Assert.Equal(long.MaxValue, Table(service, "_moveGuardUntil")[Pid]);
        Assert.True(Table(service, "_botMoveConfirm").Contains(Pid));
    }

    [Fact]
    public void New_source_and_old_decoder_epoch_revoke_local_diagnostic()
    {
        var service = Service(out var sent);
        var packet = LinkedResponse(service, Pid, out var decoder);
        var old = new Link(Guid.NewGuid(), Pid, 5L, 5L, Guid.NewGuid());
        ObserveSource(service, old);
        PendingHarvest(service, Pid, moveSequence: 42);
        Attach(service, "_harvestArrivalIntentV21", Pid, seq: 42,
            link: old, epoch: decoder.CurrentLocalEpoch(Pid));
        Assert.Equal("own-echo-observed-but-confirmation-unattributable",
            Inspect(service, true, packet));

        var ledger = Field("_socketSourceAuthority").GetValue(service)!;
        ledger.GetType().GetMethod("RevokeExact")!.Invoke(ledger, [old]);
        var newer = new Link(Guid.NewGuid(), Pid, 6L, 6L, Guid.NewGuid());
        ObserveSource(service, newer);
        Assert.Equal("source-link-changed", Inspect(service, true, packet));
        Receive(service, packet);
        Assert.Empty(sent);
        Assert.True(Table(service, "_pendingHarvest").Contains(Pid));

        decoder.Forget(Pid);
        Assert.Equal("decoder-epoch-changed", Inspect(service, true, packet));
        Receive(service, packet); // retired epoch must not enter OnDecoded.
        Assert.Empty(sent);
        Assert.True(Table(service, "_pendingHarvest").Contains(Pid));
    }

    [Fact]
    public void Feed_forget_new_link_then_old_OnDecoded_rejects_retired_packet()
    {
        var service = Service(out var sent);
        var old = LinkedResponse(service, Pid, out var decoder);
        PendingMap(service, Pid, moveSequence: 73);
        int delivered = 0;
        service.PacketDecoded += _ => delivered++;

        // The actual decoder packet was captured before the retirement.
        // This manually delayed subscriber delivery models a consumer which
        // retained that exact object while the connection was replaced.
        decoder.Forget(Pid);
        Packet? fresh = null;
        decoder.Packet += packet => fresh = packet;
        decoder.Feed(Pid, clientToServer: false, FramedConfirmation());
        Assert.NotNull(fresh);
        Assert.NotEqual(old.DecoderEpoch, fresh!.DecoderEpoch);

        Receive(service, old);
        Assert.Equal(0, delivered);
        Assert.True(Table(service, "_pendingMapChange").Contains(Pid));
        Assert.Equal(long.MaxValue, Table(service, "_moveGuardUntil")[Pid]);

        // The new session's genuine stamped response is still ambiguous:
        // only its current epoch, not individual move A/B, is established.
        Receive(service, fresh);
        Assert.Equal(1, delivered);
        Assert.Empty(sent);
        Assert.True(Table(service, "_pendingMapChange").Contains(Pid));
    }

    [Fact]
    public async Task Actual_Feed_delivery_gate_serializes_Forget_and_relink_without_deadlock()
    {
        var service = Service(out var sent);
        PendingMap(service, Pid, moveSequence: 91);
        var decoder = new Decoder(WireCatalog);
        Field("_decoder").SetValue(service, decoder);
        using var entered = new ManualResetEventSlim();
        using var forgetStarted = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Packet? retired = null;
        Packet? fresh = null;
        int delivered = 0;
        decoder.Packet += packet =>
        {
            if (Interlocked.Increment(ref delivered) == 1)
            {
                retired = packet;
                entered.Set(); // Feed holds DeliveryGate, not _gate.
                Assert.True(release.Wait(TimeSpan.FromSeconds(8)));
            }
            else fresh = packet;
        };

        // Use dedicated threads for the two opposing sides of the real
        // DeliveryGate. A blocked xUnit worker should not starve a scheduled
        // Task.Run worker when the full suite saturates the thread pool.
        var oldFeed = Task.Factory.StartNew(
            () => decoder.Feed(Pid, false, FramedConfirmation()),
            CancellationToken.None, TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Task? forgetting = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(8)));
            forgetting = Task.Factory.StartNew(() =>
                {
                    forgetStarted.Set();
                    decoder.Forget(Pid);
                }, CancellationToken.None, TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Assert.True(forgetStarted.Wait(TimeSpan.FromSeconds(8)));
            // The old callback cannot be overtaken by a completed Forget.
            // No task is waiting on _gate while holding the test barrier.
            Assert.False(forgetting.IsCompleted);
        }
        finally { release.Set(); }

        await Task.WhenAll(oldFeed, forgetting!).WaitAsync(TimeSpan.FromSeconds(8));
        decoder.Feed(Pid, false, FramedConfirmation());
        Assert.NotNull(retired);
        Assert.NotNull(fresh);
        Assert.NotEqual(retired!.DecoderEpoch, fresh!.DecoderEpoch);

        // Deliberately deliver the old object to the REAL reducer after the
        // replacement (the worst possible delayed external subscriber).
        Receive(service, retired);
        Assert.Empty(sent);
        Assert.True(Table(service, "_pendingMapChange").Contains(Pid));
        Assert.Equal(long.MaxValue, Table(service, "_moveGuardUntil")[Pid]);
    }
}
