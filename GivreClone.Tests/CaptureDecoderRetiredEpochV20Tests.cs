extern alias F06App;

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using Packet = F06App::GivreClone.App.Services.DecodedPacket;
using Launch = F06App::GivreClone.App.Services.LaunchService;

namespace GivreClone.Tests;

/// <summary>
/// Actual CaptureDecoder.Feed/Forget/Packet callback interleavings, entirely
/// offline. A local epoch prevents stale callbacks but does not certify a TCP
/// peer, an effect or any individual gameplay ACK.
/// </summary>
public sealed class CaptureDecoderRetiredEpochV20Tests
{
    private const uint Pid = 96131;
    private static readonly ProtocolCatalog Catalogue = ProtocolCatalog.LoadFromStrings(
        null, """{"type.ankama.com/testepoch":"MessageType.TestEpoch"}""", null, null);

    private static byte[] Framed()
    {
        var raw = RawProtobuf.Encode(
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
                                String = "type.ankama.com/testepoch" }
                        ]
                    }
                ]
            }
        ]);
        return RawProtobuf.EncodeVarint((ulong)raw.Length).Concat(raw).ToArray();
    }

    [Fact]
    public void Reentrant_forget_between_subscribers_drops_remaining_old_epoch_callbacks()
    {
        var decoder = new Decoder(Catalogue);
        Packet? old = null;
        int later = 0;
        decoder.Packet += packet =>
        {
            old = packet;
            Assert.True(decoder.IsCurrentLocalEpoch(packet));
            decoder.Forget(Pid); // reentrant teardown inside real Packet event
        };
        decoder.Packet += _ => later++;

        decoder.Feed(Pid, false, Framed());

        Assert.NotNull(old);
        Assert.False(decoder.IsCurrentLocalEpoch(old));
        Assert.Equal(0, later);
        Assert.Equal(0, decoder.CurrentLocalEpoch(Pid));
    }

    [Fact]
    public async Task Concurrent_forget_waits_for_real_decoder_delivery_before_retiring_epoch()
    {
        var decoder = new Decoder(Catalogue);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Packet? observed = null;
        decoder.Packet += packet =>
        {
            observed = packet;
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(8)));
            Assert.True(decoder.IsCurrentLocalEpoch(packet));
        };
        var incoming = Task.Run(() => decoder.Feed(Pid, false, Framed()));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(8)));
        try
        {
            var retiring = Task.Run(() => decoder.Forget(Pid));
            // In the new contract Forget MUST wait until the in-flight
            // subscriber releases its own PID delivery gate.
            await Task.Delay(30, cancel.Token);
            Assert.False(retiring.IsCompleted);
            release.Set();
            await Task.WhenAll(incoming, retiring).WaitAsync(TimeSpan.FromSeconds(8));
        }
        finally { release.Set(); }
        Assert.NotNull(observed);
        Assert.False(decoder.IsCurrentLocalEpoch(observed));
        Assert.Equal(0, decoder.CurrentLocalEpoch(Pid));
    }

    [Fact]
    public void Legacy_launch_reducer_refuses_a_packet_from_a_real_retired_decoder_epoch()
    {
        var decoder = new Decoder(Catalogue);
        Packet? stale = null;
        decoder.Packet += packet => stale = packet;
        decoder.Feed(Pid, false, Framed());
        Assert.NotNull(stale);
        decoder.Forget(Pid);
        Assert.False(decoder.IsCurrentLocalEpoch(stale));

        var launch = new Launch();
        var field = typeof(Launch).GetField("_decoder",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(launch, decoder);
        int notifications = 0;
        launch.PacketDecoded += _ => notifications++;
        var handler = typeof(Launch).GetMethod("OnDecoded",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        handler.Invoke(launch, [stale]);
        Assert.Equal(0, notifications);
    }
}
