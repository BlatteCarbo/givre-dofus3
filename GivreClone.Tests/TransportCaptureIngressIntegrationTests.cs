extern alias F06App;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using Xunit;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using Linked = F06App::GivreClone.App.Services.LinkScopedDecodedPacket;
using LinkedIdentity = F06App::GivreClone.App.Services.CaptureLinkIdentity;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Packet = F06App::GivreClone.App.Services.DecodedPacket;
using Headless = F06App::GivreClone.App.Services.BotHostService;

namespace GivreClone.Tests;

/// <summary>
/// The real CaptureDecoder.Feed entrypoint (MITM) and the public
/// LaunchService.FeedSocketFrame entrypoint (Socket) receive only
/// (PID,direction,length-prefixed chunks). BotHostService's typed transport
/// event is a separate headless/test TCP source with UNFRAMED bodies.
/// All tests are offline: no host Start, TCP, bot, game or ACK.
/// </summary>
[CollectionDefinition("Transport capture diagnostic catalog", DisableParallelization = true)]
public sealed class TransportCaptureDiagnosticCatalogCollection { }

[Collection("Transport capture diagnostic catalog")]
public sealed class TransportCaptureIngressIntegrationTests
{
    private const uint Pid = 8081;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));

    private static (byte[] Body, byte[] Framed) Event(string name)
    {
        var message = new[]
        {
            new ProtoField
            {
                Field = Catalog.FieldNumber(name, "fight_id") ?? 8101,
                Wire = WireType.Varint,
                Varint = 19
            }
        };
        byte[] body = EnvelopeBuilder.BuildServerEvent(
            Catalog.CodeForName(name) ?? throw new InvalidOperationException(name), message);
        var prefix = new List<byte>();
        uint length = (uint)body.Length;
        do
        {
            byte b = (byte)(length & 0x7f);
            length >>= 7;
            prefix.Add(length == 0 ? b : (byte)(b | 0x80));
        } while (length != 0);
        return (body, prefix.Concat(body).ToArray());
    }

    private static TransportLinkProvenance Link(uint pid = Pid, long token = 19)
        => new(Guid.Parse("49e85202-b5f4-4222-b45a-0610d2578214"),
            RunEpoch: 8, LinkToken: token, ConnectionId: 15, ProcessId: pid);

    private static LinkedIdentity LocalLink(long run = 1, long tunnel = 7,
        Guid? source = null, uint pid = Pid)
        => new(source ?? Guid.Parse("b47d7947-2513-49e9-9f50-48b04c8b43de"),
            pid, run, tunnel);

    private static TransportFrameObservation Observation(string name = "FightStartingEvent",
        long token = 19, long sequence = 13, Direction direction = Direction.ServerToClient)
        => new(sequence, direction, Link(token: token), Event(name).Body);

    private static (Launch Service, Decoder Decoder) UninitializedSocketEntrypoint()
    {
        // LaunchService() constructs actual launcher collaborators and may bind
        // loopback ports. Use the exact public FeedSocketFrame implementation
        // on an inert formatter-style fixture with an injected real decoder.
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        var decoder = new Decoder(Catalog);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Launch).GetField("_decoder", flags)!.SetValue(service, decoder);
        typeof(Launch).GetField("_decoderInit", flags)!.SetValue(service, true);
        return (service, decoder);
    }

    [Fact]
    public void Real_MITM_decoder_feed_and_typed_headless_body_are_distinct_entrypoints()
    {
        var decoder = new Decoder(Catalog);
        var (body, framed) = Event("FightStartingEvent");
        int decoded = 0;
        Packet? packet = null;
        decoder.Packet += p => { decoded++; packet = p; };
        var typed = new TransportFrameObservation(3, Direction.ServerToClient, Link(), body);

        var diagnostic = decoder.InspectTypedTransportFrame(typed, link => link == Link());
        Assert.NotNull(diagnostic);
        Assert.Equal(typed.Provenance, diagnostic!.Link);
        Assert.Equal(typed.FrameSequence, diagnostic.FrameSequence);
        Assert.Equal(typed.Direction, diagnostic.Direction);
        Assert.Equal(body.Length, diagnostic.RawBodyBytes);
        Assert.Contains("FightStartingEvent", diagnostic.MessageNames);
        Assert.Equal(0, decoded);
        Assert.Equal(0, decoder.CurrentLocalEpoch(Pid));
        Assert.False(diagnostic.CanJoinLaunchDecoder);
        Assert.False(diagnostic.ServerConfirmed);
        Assert.False(diagnostic.CanAuthorizeCast);
        Assert.False(diagnostic.IsServerAck);

        // HandshakeRelay.OnData delivers varint-length-prefixed bytes: only
        // the REAL Feed path can produce an existing DecodedPacket.
        decoder.Feed(Pid, false, framed);
        Assert.Equal(1, decoded);
        Assert.NotNull(packet);
        Assert.Equal("FightStartingEvent", packet!.Name);
        Assert.True(decoder.IsCurrentLocalEpoch(packet));
        Assert.True(packet.DecoderOrder > 0);

        // The side-port reinspection does NOT allocate a second DecodedPacket,
        // alter the source framing order or consume an in-game message twice.
        Assert.NotNull(decoder.InspectTypedTransportFrame(typed, link => link == Link()));
        Assert.Equal(1, decoded);
        Assert.True(decoder.IsCurrentLocalEpoch(packet));
    }

    [Fact]
    public void Real_Socket_FeedSocketFrame_has_no_connection_provenance_to_join_headless_frame()
    {
        var (service, decoder) = UninitializedSocketEntrypoint();
        Packet? received = null;
        int packets = 0;
        decoder.Packet += p => { received = p; ++packets; };
        var (body, wire) = Event("FightStartingEvent");
        service.FeedSocketFrame(Pid, false, wire);

        Assert.Equal(1, packets);
        Assert.NotNull(received);
        Assert.True(decoder.IsCurrentLocalEpoch(received!));
        Assert.Equal("FightStartingEvent", received!.Name);
        var fakeRelated = new TransportFrameObservation(
            50, Direction.ServerToClient, Link(), body);

        // An actual (stopped) BotHostService cannot attest an arbitrary frame.
        // Same PID AND identical protobuf bytes confer NO link authority.
        var stoppedSource = new Headless();
        Assert.False(stoppedSource.IsCurrentTransportLink(fakeRelated.Provenance));
        Assert.Null(service.InspectHeadlessTransportDiagnostic(stoppedSource, fakeRelated));
        Assert.Null(service.InspectHeadlessTransportDiagnostic(null, fakeRelated));
        Assert.Null(service.InspectHeadlessTransportDiagnostic(stoppedSource, null));
        Assert.Equal(1, packets);
        Assert.True(decoder.IsCurrentLocalEpoch(received));
    }

    [Fact]
    public void Typed_transport_diagnostic_rechecks_source_before_and_after_parsing()
    {
        var decoder = new Decoder(Catalog);
        var frame = Observation();
        int checks = 0;
        Assert.Null(decoder.InspectTypedTransportFrame(frame,
            provenance => { checks++; return checks == 1 && provenance == Link(); }));
        Assert.Equal(2, checks); // source retired while decoding the frame
        Assert.Null(decoder.InspectTypedTransportFrame(frame, _ => false));
        Assert.Null(decoder.InspectTypedTransportFrame(frame, _ => throw new InvalidOperationException()));
        Assert.Equal(0, decoder.CurrentLocalEpoch(Pid));
    }

    [Fact]
    public void Forged_wrong_connection_direction_and_malformed_body_fail_closed()
    {
        var decoder = new Decoder(Catalog);
        var good = Observation();
        Func<TransportLinkProvenance, bool> current = provenance => provenance == Link();

        Assert.Null(decoder.InspectTypedTransportFrame(
            good with { Provenance = Link(token: 20) }, current));
        Assert.Null(decoder.InspectTypedTransportFrame(
            good with { Provenance = Link(pid: Pid + 1) }, current));
        Assert.Null(decoder.InspectTypedTransportFrame(
            good with { Provenance = default }, _ => true));
        Assert.Null(decoder.InspectTypedTransportFrame(
            good with { FrameSequence = 0 }, _ => true));
        Assert.Null(decoder.InspectTypedTransportFrame(
            good with { RawFrame = [0xff, 0xff, 0xff] }, _ => true));
        Assert.Null(decoder.InspectTypedTransportFrame(
            good with { RawFrame = new byte[1_048_577] }, _ => true));
        Assert.Null(decoder.InspectTypedTransportFrame(
            good with { Direction = (Direction)51 }, _ => true));

        var outbound = decoder.InspectTypedTransportFrame(
            Observation(direction: Direction.ClientToServer), current);
        Assert.NotNull(outbound); // may be inspected as transport metadata
        Assert.Equal(Direction.ClientToServer, outbound!.Direction);
        Assert.False(outbound.IsServerAck);
        Assert.False(outbound.CanJoinLaunchDecoder);
        Assert.False(outbound.CanAuthorizeCast);
        Assert.Equal(0, decoder.CurrentLocalEpoch(Pid));
    }

    [Fact]
    public void Decoder_forget_rejects_stale_packets_without_promoting_typed_source()
    {
        var decoder = new Decoder(Catalog);
        var (_, wire) = Event("FightStartingEvent");
        Packet? old = null;
        decoder.Packet += p => old = p;
        decoder.Feed(Pid, false, wire);
        Assert.NotNull(old);
        long oldEpoch = old!.DecoderEpoch;
        Assert.True(decoder.IsCurrentLocalEpoch(old));
        decoder.Forget(Pid);
        Assert.False(decoder.IsCurrentLocalEpoch(old));
        Assert.Equal(0, decoder.CurrentLocalEpoch(Pid));

        // The headless diagnostic is not an alternative way to reopen local
        // ingress or restore the stale packet's generation.
        var headless = decoder.InspectTypedTransportFrame(
            Observation(), provenance => provenance == Link());
        Assert.NotNull(headless);
        Assert.False(headless!.CanJoinLaunchDecoder);
        Assert.False(headless.ServerConfirmed);
        Assert.Equal(0, decoder.CurrentLocalEpoch(Pid));

        decoder.Feed(Pid, false, wire);
        Assert.NotNull(old);
        Assert.True(old!.DecoderEpoch > oldEpoch);
        Assert.True(decoder.IsCurrentLocalEpoch(old));
        Assert.False(headless.CanAuthorizeCast);
    }

    [Fact]
    public void Same_relay_link_scoped_chunks_reassemble_only_on_typed_channel_and_consume_once()
    {
        var decoder = new Decoder(Catalog);
        var link = LocalLink();
        Func<LinkedIdentity, bool> isCurrent = l => l == link;
        var (_, bytes) = Event("FightStartingEvent");
        var oldPackets = 0;
        var typedPackets = new List<Linked>();
        decoder.Packet += _ => ++oldPackets;
        decoder.LinkedPacket += typedPackets.Add;

        // A TCP chunk is not a complete protobuf message; the decoder must
        // reframe using the exact link+direction, never just a recycled PID.
        Assert.True(decoder.FeedLinkScoped(link, 1, false,
            bytes[..^1], isCurrent));
        Assert.Empty(typedPackets);
        Assert.True(decoder.FeedLinkScoped(link, 2, false,
            bytes[^1..], isCurrent));
        var packet = Assert.Single(typedPackets);
        Assert.Equal("FightStartingEvent", packet.Packet.Name);
        Assert.Equal(link, packet.Link);
        Assert.Equal(2, packet.SourceChunkSequence);
        Assert.True(decoder.IsCurrentLinkedPacket(packet, isCurrent));
        Assert.True(decoder.CurrentLinkedEpoch(link, isCurrent) > 0);
        Assert.False(packet.ServerConfirmed);
        Assert.False(packet.CanAuthorizeCast);
        Assert.False(packet.IsServerAck);
        Assert.Equal(0, oldPackets);
        Assert.Equal(0, decoder.CurrentLocalEpoch(Pid)); // PID-only channel untouched

        Assert.True(decoder.TryConsumeLinkedPacket(packet, isCurrent));
        Assert.False(decoder.TryConsumeLinkedPacket(packet, isCurrent));
        Assert.False(decoder.IsCurrentLinkedPacket(packet with { }, isCurrent));
        Assert.False(decoder.IsCurrentLinkedPacket(
            packet with { Packet = packet.Packet with { } }, isCurrent));
        Assert.False(decoder.FeedLinkScoped(link, 2, false, bytes, isCurrent));
        Assert.Single(typedPackets); // repeated source sequence rejected
    }

    [Fact]
    public void Exact_link_close_and_same_pid_reuse_revoke_only_old_tunnel()
    {
        var decoder = new Decoder(Catalog);
        var first = LocalLink(tunnel: 10);
        var second = LocalLink(tunnel: 11);
        Func<LinkedIdentity, bool> current = l => l == first || l == second;
        var (_, wire) = Event("FightStartingEvent");
        var events = new List<Linked>();
        var closures = new List<(LinkedIdentity Link, long Epoch)>();
        decoder.LinkedPacket += events.Add;
        decoder.LinkedLocalEpochRevoked += (link, epoch) => closures.Add((link, epoch));

        Assert.True(decoder.FeedLinkScoped(first, 1, false, wire, current));
        var old = Assert.Single(events);
        long oldEpoch = decoder.CurrentLinkedEpoch(first, current);
        Assert.True(oldEpoch > 0);
        Assert.True(decoder.FeedLinkScoped(second, 1, false, wire, current));
        Assert.Equal(2, events.Count);
        var fresh = events[1];
        Assert.True(fresh.Packet.DecoderEpoch > oldEpoch);
        Assert.Equal((first, oldEpoch), Assert.Single(closures));
        Assert.False(decoder.IsCurrentLinkedPacket(old, current));
        Assert.True(decoder.IsCurrentLinkedPacket(fresh, current));
        Assert.False(decoder.FeedLinkScoped(first, 2, false, wire, current));

        decoder.RevokeLink(first); // delayed EOF from old tunnel
        Assert.True(decoder.IsCurrentLinkedPacket(fresh, current));
        Assert.Single(closures);
        decoder.RevokeLink(second);
        Assert.False(decoder.IsCurrentLinkedPacket(fresh, current));
        Assert.Equal(0, decoder.CurrentLinkedEpoch(second, current));
        Assert.Equal(2, closures.Count);
        Assert.False(decoder.FeedLinkScoped(second, 2, false, wire, current));
    }

    [Fact]
    public void Linked_feed_rejects_wrong_source_missing_sequence_and_revocation_before_dispatch()
    {
        var decoder = new Decoder(Catalog);
        var link = LocalLink();
        var wrongInstance = LocalLink(source: Guid.NewGuid());
        var (_, wire) = Event("FightStartingEvent");
        var seen = new List<Linked>();
        decoder.LinkedPacket += seen.Add;

        Assert.False(decoder.FeedLinkScoped(default, 1, false, wire, _ => true));
        Assert.False(decoder.FeedLinkScoped(link, 0, false, wire, _ => true));
        Assert.False(decoder.FeedLinkScoped(link, 1, false, [], _ => true));
        Assert.False(decoder.FeedLinkScoped(link, 1, false, wire, _ => false));
        Assert.False(decoder.FeedLinkScoped(wrongInstance, 1, false, wire,
            current => current == link));
        Assert.Empty(seen);

        int validations = 0;
        Func<LinkedIdentity, bool> closeDuringDecode = current =>
            current == link && ++validations == 1;
        Assert.False(decoder.FeedLinkScoped(link, 1, false, wire, closeDuringDecode));
        Assert.Equal(2, validations); // second check fails before typed event
        Assert.Empty(seen);
        Assert.False(decoder.FeedLinkScoped(link, 2, false, wire, _ => true));
        Assert.Equal(0, decoder.CurrentLinkedEpoch(link, _ => true));
    }

    [Fact]
    public void Linked_client_direction_and_legacy_MITM_packet_never_share_provenance()
    {
        var decoder = new Decoder(Catalog);
        var link = LocalLink();
        var (_, wire) = Event("FightStartingEvent");
        var typed = new List<Linked>();
        var legacy = new List<Packet>();
        decoder.LinkedPacket += typed.Add;
        decoder.Packet += legacy.Add;

        Assert.True(decoder.FeedLinkScoped(link, 1, true, wire,
            current => current == link));
        Assert.True(Assert.Single(typed).Packet.ClientToServer);
        Assert.False(typed[0].IsServerAck);
        Assert.False(typed[0].ServerConfirmed);

        decoder.Feed(Pid, false, wire); // legacy MITM tap, no link identity
        Assert.Single(legacy);
        Assert.True(decoder.IsCurrentLocalEpoch(legacy[0]));
        Assert.True(decoder.IsCurrentLinkedPacket(typed[0], current => current == link));
        Assert.Equal(1, typed.Count);
        Assert.False(decoder.IsCurrentLocalEpoch(typed[0].Packet));
        Assert.False(decoder.TryConsumeLinkedPacket(
            new Linked(legacy[0], link, 1), current => current == link));
    }
}
