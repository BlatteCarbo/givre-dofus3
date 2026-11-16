extern alias F06App;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using Xunit.Abstractions;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using CaptureIdentity = F06App::GivreClone.App.Services.CaptureLinkIdentity;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using LinkedPacket = F06App::GivreClone.App.Services.LinkScopedDecodedPacket;

namespace GivreClone.Tests;

/// <summary>
/// The real app entry and source-linked decoder are tested with inert framed
/// fixtures. SocketGameClient/SocketProbe types are internal by design; only
/// their record constructors and the actual LaunchService ingress are invoked
/// by reflection. No dial, game, login, account or transport write occurs.
/// </summary>
[Collection("Transport capture diagnostic catalog")]
public sealed class SocketSourceLinkedIngressTests
{
    private readonly ITestOutputHelper _output;

    public SocketSourceLinkedIngressTests(ITestOutputHelper output) => _output = output;

    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));
    private static readonly Assembly App = typeof(Launch).Assembly;
    private static readonly Type SourceType = App.GetType(
        "GivreClone.App.Services.Transport.Socket.SocketLinkProvenance", true)!;
    private static readonly Type ObservationType = App.GetType(
        "GivreClone.App.Services.Transport.Socket.SocketTransportFrameObservation", true)!;
    private static readonly BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

    private static object Source(uint pid, long run, Guid client, Guid connection) =>
        Activator.CreateInstance(SourceType, [client, run, connection, pid])!;

    private static object Observation(long seq, object source, bool outbound, byte[] wire) =>
        Activator.CreateInstance(ObservationType, [seq, source, outbound, wire])!;

    private static Func<T, bool> Adapt<T>(Func<object, bool> predicate)
        => value => predicate(value!);

    private static Delegate Validator(Func<object, bool> predicate) =>
        (Delegate)typeof(SocketSourceLinkedIngressTests).GetMethod(nameof(Adapt),
            BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(SourceType)
            .Invoke(null, [predicate])!;

    private static bool Feed(Launch launch, object observation, Func<object, bool> current) =>
        (bool)typeof(Launch).GetMethod("FeedSocketObservedFrame", Hidden)!
            .Invoke(launch, [observation, Validator(current)])!;

    private static void Revoke(Launch launch, object source) =>
        typeof(Launch).GetMethod("RevokeSocketObservedLink", Hidden)!
            .Invoke(launch, [source]);

    private static (Launch Service, Decoder Decoder) InertLaunch()
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        var decoder = new Decoder(Catalog);
        typeof(Launch).GetField("_decoder", Hidden)!.SetValue(service, decoder);
        typeof(Launch).GetField("_decoderInit", Hidden)!.SetValue(service, true);
        return (service, decoder);
    }

    private static byte[] Wire(string name)
    {
        byte[] body = EnvelopeBuilder.BuildServerEvent(
            Catalog.CodeForName(name) ?? throw new InvalidOperationException(name),
            [new ProtoField
            {
                Field = Catalog.FieldNumber(name, "fight_id") ?? 8101,
                Wire = WireType.Varint, Varint = 19
            }]);
        var prefix = new List<byte>();
        uint length = (uint)body.Length;
        do
        {
            byte b = (byte)(length & 127);
            length >>= 7;
            prefix.Add(length == 0 ? b : (byte)(b | 128));
        } while (length > 0);
        return prefix.Concat(body).ToArray();
    }

    [Fact]
    public void Actual_socket_observation_entry_emits_only_a_source_linked_advisory_packet()
    {
        var (launch, decoder) = InertLaunch();
        var client = Guid.NewGuid();
        var token = Guid.NewGuid();
        object source = Source(70001, 12, client, token);
        var linked = new List<LinkedPacket>();
        int legacyPackets = 0;
        decoder.LinkedPacket += linked.Add;
        decoder.Packet += _ => legacyPackets++;
        object frame = Observation(1, source, false, Wire("FightStartingEvent"));

        Assert.True(Feed(launch, frame, s => Equals(s, source)));
        var packet = Assert.Single(linked);
        Assert.Equal(new CaptureIdentity(client, 70001, 12, 12, token), packet.Link);
        Assert.False(packet.Packet.ClientToServer);
        Assert.Equal("FightStartingEvent", packet.Packet.Name);
        Assert.False(packet.ServerConfirmed);
        Assert.False(packet.CanAuthorizeCast);
        Assert.False(packet.IsServerAck);
        Assert.Equal(0, legacyPackets);
        Assert.Equal(0, decoder.CurrentLocalEpoch(70001));

        bool Current(CaptureIdentity link) => link == packet.Link;
        Assert.True(decoder.TryConsumeLinkedPacket(packet, Current));
        Assert.False(decoder.TryConsumeLinkedPacket(packet, Current));
        Assert.False(decoder.TryConsumeLinkedPacket(packet with { }, Current));
    }

    [Fact]
    public void Revoke_and_pid_replacement_cannot_replay_the_old_connection_token()
    {
        var (launch, decoder) = InertLaunch();
        var firstClient = Guid.NewGuid();
        var firstToken = Guid.NewGuid();
        object oldSource = Source(70002, 16, firstClient, firstToken);
        var observed = new List<LinkedPacket>();
        decoder.LinkedPacket += observed.Add;
        var wire = Wire("FightStartingEvent");

        Assert.True(Feed(launch, Observation(1, oldSource, false, wire), s => Equals(s, oldSource)));
        var old = Assert.Single(observed);
        Revoke(launch, oldSource);
        Assert.False(decoder.IsCurrentLinkedPacket(old, _ => true));
        Assert.False(Feed(launch, Observation(2, oldSource, false, wire), _ => true));

        object newer = Source(70002, 17, Guid.NewGuid(), Guid.NewGuid());
        Assert.True(Feed(launch, Observation(1, newer, false, wire), s => Equals(s, newer)));
        var latest = observed.Last();
        Assert.Equal(2, observed.Count);
        Assert.NotEqual(old.Link, latest.Link);
        Revoke(launch, oldSource); // late close of old socket, exact token only
        Assert.True(decoder.IsCurrentLinkedPacket(latest, id => id == latest.Link));
        Assert.False(Feed(launch, Observation(2, oldSource, false, wire), _ => true));
        Assert.True(decoder.IsCurrentLinkedPacket(latest, id => id == latest.Link));
    }

    [Fact]
    public void Validator_must_reconfirm_exact_source_after_parsing_and_reentrant_revocation()
    {
        var (launch, decoder) = InertLaunch();
        object source = Source(70003, 44, Guid.NewGuid(), Guid.NewGuid());
        int checks = 0, linked = 0;
        decoder.LinkedPacket += _ => linked++;
        var frame = Observation(1, source, false, Wire("FightStartingEvent"));
        Assert.False(Feed(launch, frame, s => Equals(s, source) && ++checks < 2));
        Assert.Equal(0, linked);
        Assert.False(Feed(launch, Observation(2, source, false,
            Wire("FightStartingEvent")), _ => true)); // revoked, not rearmed
    }

    [Fact]
    public void Wrong_identity_malformed_wire_and_wrong_local_direction_never_grant_server_proof()
    {
        var (launch, decoder) = InertLaunch();
        object source = Source(70004, 77, Guid.NewGuid(), Guid.NewGuid());
        var observed = new List<LinkedPacket>();
        decoder.LinkedPacket += observed.Add;
        var wire = Wire("FightStartingEvent");
        Assert.False(Feed(launch, Observation(1, source, false, wire), _ => false));
        Assert.True(Feed(launch, Observation(1, source, true, wire), s => Equals(s, source)));
        var packet = Assert.Single(observed);
        Assert.True(packet.Packet.ClientToServer);
        Assert.False(packet.CanAuthorizeCast);
        Assert.False(packet.ServerConfirmed);
        Assert.False(packet.IsServerAck);

        Revoke(launch, source);
        object other = Source(70004, 78, Guid.NewGuid(), Guid.NewGuid());
        // Three continuation bytes are an INCOMPLETE TCP varint, not a
        // malformed complete packet: retaining them is legitimate, but no
        // application event may be emitted before a complete frame arrives.
        Assert.True(Feed(launch, Observation(1, other, false, new byte[] { 255, 255, 255 }),
            s => Equals(s, other)));
        Assert.Single(observed);
        Assert.False(Feed(launch, Observation(1, other, false, wire),
            s => Equals(s, other))); // duplicate sequence revokes the source
        object third = Source(70004, 79, Guid.NewGuid(), Guid.NewGuid());
        Assert.False(Feed(launch, Observation(1, third, false, new byte[1_048_577]),
            s => Equals(s, third))); // beyond bounded frame payload
        Assert.Single(observed);
    }

    [Fact]
    public void Real_socket_linked_ingress_measures_decode_cost_without_granting_gameplay_authority()
    {
        var (launch, decoder) = InertLaunch();
        object source = Source(70005, 88, Guid.NewGuid(), Guid.NewGuid());
        var wire = Wire("FightStartingEvent");
        int observed = 0;
        decoder.LinkedPacket += packet =>
        {
            observed++;
            Assert.False(packet.ServerConfirmed);
            Assert.False(packet.CanAuthorizeCast);
        };
        Func<object, bool> current = candidate => Equals(source, candidate);
        for (int i = 1; i <= 16; ++i)
            Assert.True(Feed(launch, Observation(i, source, false, wire), current));

        const int count = 256;
        long before = GC.GetAllocatedBytesForCurrentThread();
        var elapsed = Stopwatch.StartNew();
        for (int i = 17; i < 17 + count; ++i)
            Assert.True(Feed(launch, Observation(i, source, false, wire), current));
        elapsed.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        _output.WriteLine($"V6 offline actual linked Socket ingress: {count} framed " +
            $"observations decoded in {elapsed.Elapsed.TotalMilliseconds:F3} ms; " +
            $"{bytes} bytes allocated on caller thread; synthetic frames, " +
            "not Socket TCP latency, remote ACK, or game throughput.");
        Assert.Equal(16 + count, observed);
        Assert.Equal(0, decoder.CurrentLocalEpoch(70005));
        Revoke(launch, source);
        Assert.False(Feed(launch, Observation(17 + count, source, false, wire), current));
    }
}
