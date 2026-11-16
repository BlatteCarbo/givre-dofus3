extern alias F06App;

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using Launch = F06App::GivreClone.App.Services.LaunchService;

namespace GivreClone.Tests;

/// <summary>
/// The genuine LaunchService.FeedSocketObservedFrame entry point must revoke a
/// source when its owning SocketProbe lease changes at the final post-decode
/// validation boundary. No PID or decoded event may masquerade as an ACK.
/// </summary>
[Collection("Transport capture diagnostic catalog")]
public sealed class SocketSourceAdmissionBoundaryV9Tests
{
    private static readonly Assembly App = typeof(Launch).Assembly;
    private static readonly Type SourceType = App.GetType(
        "GivreClone.App.Services.Transport.Socket.SocketLinkProvenance", true)!;
    private static readonly Type FrameType = App.GetType(
        "GivreClone.App.Services.Transport.Socket.SocketTransportFrameObservation", true)!;
    private static readonly Type LedgerType = App.GetType(
        "GivreClone.App.Services.SocketSourceAuthorityLedger", true)!;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));

    private static Launch Service()
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        typeof(Launch).GetField("_decoder", Hidden)!.SetValue(service, new Decoder(Catalog));
        typeof(Launch).GetField("_decoderInit", Hidden)!.SetValue(service, true);
        typeof(Launch).GetField("_socketSourceAuthority", Hidden)!
            .SetValue(service, Activator.CreateInstance(LedgerType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [true], CultureInfo.InvariantCulture)!);
        return service;
    }

    private static Func<T, bool> Adapt<T>(Func<object, bool> source) => candidate => source(candidate!);
    private static Delegate Validator(Func<object, bool> source)
        => (Delegate)typeof(SocketSourceAdmissionBoundaryV9Tests)
            .GetMethod(nameof(Adapt), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(SourceType).Invoke(null, [source])!;
    private static bool Feed(Launch service, object observation, Func<object, bool> live)
        => (bool)typeof(Launch).GetMethod("FeedSocketObservedFrame", Hidden)!
            .Invoke(service, [observation, Validator(live)])!;
    private static byte[] Wire()
    {
        string code = Catalog.CodeForName("FightStartingEvent")!;
        byte[] payload = EnvelopeBuilder.BuildServerEvent(code,
            [new ProtoField
            {
                Field = Catalog.FieldNumber("FightStartingEvent", "fight_id") ?? 8101,
                Wire = WireType.Varint, Varint = 43
            }]);
        uint count = (uint)payload.Length;
        var prefix = new System.Collections.Generic.List<byte>();
        do
        {
            byte part = (byte)(count & 0x7f);
            count >>= 7;
            prefix.Add(count == 0 ? part : (byte)(part | 0x80));
        } while (count != 0);
        return prefix.Concat(payload).ToArray();
    }
    private static object Origin()
        => Activator.CreateInstance(SourceType, [Guid.NewGuid(), 17L, Guid.NewGuid(), 92021u])!;
    private static object Observation(object source)
        => Activator.CreateInstance(FrameType, [1L, source, false, Wire()])!;

    [Fact]
    public void Reentrant_revocation_during_decoder_callbacks_cannot_report_admitted_frame()
    {
        object source = Origin();
        var baseline = Service();
        int count = 0;
        Assert.True(Feed(baseline, Observation(source),
            candidate => Equals(candidate, source) && ++count > 0));
        Assert.True(count > 0);
        Assert.Equal(1, baseline.SocketSourceAuthorityOf(92021)!.Value.ServerFramesObserved);

        // Reproduce a replacement during source validation while a framed
        // chunk is being decoded. The callback is initially valid and then
        // revokes its own lease. It must not leave a positive admission or
        // a lingering source-ledger snapshot, irrespective of which nested
        // decoder callback happens to observe the replacement first.
        var candidateService = Service();
        int checks = 0;
        bool attributed = Feed(candidateService, Observation(source),
            candidate => Equals(candidate, source) &&
                         ++checks <= Math.Max(1, count - 1));
        Assert.False(attributed);
        Assert.Null(candidateService.SocketSourceAuthorityOf(92021));
    }

    [Fact]
    public void Decoder_only_source_does_not_inject_packet_into_legacy_gameplay_or_certify_ack()
    {
        object source = Origin();
        var service = Service();
        int legacy = 0;
        ((Decoder)typeof(Launch).GetField("_decoder", Hidden)!.GetValue(service)!)
            .Packet += _ => legacy++;
        Assert.True(Feed(service, Observation(source), candidate => Equals(candidate, source)));
        Assert.Equal(0, legacy);
        var status = service.SocketSourceAuthorityOf(92021)!.Value;
        Assert.True(status.CurrentSourceWasObserved);
        Assert.False(status.ServerAckAttributed);
        Assert.False(status.GameplayAuthorizationAvailable);
    }
}
