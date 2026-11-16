extern alias F06App;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using Packet = F06App::GivreClone.App.Services.DecodedPacket;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using Xunit;

namespace GivreClone.Tests;

// Wire fixtures follow the shipped mapping. These are not recorded live packets.
// Exercise the production decoder and SnowObserve reducer without constructing any transport/account.
public sealed class AveragePriceReceptionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static FieldInfo Field(string name) => typeof(LaunchService).GetField(name, Hidden)!;
    private static IDictionary State(LaunchService service, string name) => (IDictionary)Field(name).GetValue(service)!;
    private static LaunchService Service()
    {
        var service = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        foreach (string name in new[] { "_avgPrices", "_serverInfo", "_lastByName", "_burst" })
            Field(name).SetValue(service, Activator.CreateInstance(Field(name).FieldType));
        Field("_catalog").SetValue(service, Catalog);
        return service;
    }

    private static void Observe(LaunchService service, Packet packet)
        => typeof(LaunchService).GetMethod("SnowObserve", Hidden)!.Invoke(service, [packet]);

    private static void Feed(LaunchService service, uint pid, string name, string hex, bool outgoing = false)
    {
        byte[] frame = EnvelopeBuilder.BuildServerEvent(Catalog.CodeForName(name)!, RawProtobuf.Parse(Convert.FromHexString(hex)));
        // All fixture frames fit a one-byte length prefix. Split the input to exercise PID reassembly.
        Assert.InRange(frame.Length, 1, 127);
        byte[] wire = [(byte)frame.Length, .. frame];
        var decoder = new Decoder(Catalog);
        int decoded = 0;
        decoder.Packet += packet => { decoded++; Observe(service, packet); };
        decoder.Feed(pid, outgoing, wire[..4]);
        Assert.Equal(0, decoded);
        decoder.Feed(pid, outgoing, wire[4..]);
        Assert.Equal(1, decoded);
    }

    [Fact]
    public void Server_table_reaches_cache_and_notifies_after_the_value_is_readable()
    {
        var service = Service();
        State(service, "_serverInfo")[9u] = (42, "fixture");
        State(service, "_serverInfo")[10u] = (42, "same server");
        State(service, "_serverInfo")[11u] = (43, "other server");
        int notifications = 0;
        service.AveragePricesChanged += pid =>
        {
            notifications++;
            Assert.Equal(9u, pid);
            Assert.Equal(150, service.AveragePrice(pid, 100));
        };
        // #3 ObjectAveragePrice { #1 object_gid=100; #2 average_price=150 }.
        Feed(service, 9, "ObjectAveragePricesEvent", "1A050864109601");
        Assert.Equal(1, notifications);
        Assert.Equal(150, service.AveragePrice(10, 100));
        Assert.Equal(0, service.AveragePrice(11, 100));
        Assert.Equal(0, service.AveragePrice(9, 999));
    }

    [Fact]
    public void Before_server_identification_table_stays_pid_scoped_and_updates_are_observable()
    {
        var service = Service();
        var seen = new List<long>();
        service.AveragePricesChanged += pid => seen.Add(service.AveragePrice(pid, 100));
        Feed(service, 9, "ObjectAveragePricesEvent", "1A050864109601");
        Feed(service, 9, "ObjectAveragePricesEvent", "1A05086410C801");
        Assert.Equal(new long[] { 150, 200 }, seen);
        Assert.Equal(0, service.AveragePrice(10, 100));
        State(service, "_serverInfo")[9u] = (42, "fixture");
        Assert.Equal(200, service.AveragePrice(9, 100));
    }

    [Fact]
    public void History_outgoing_and_unknown_prices_never_supply_a_substitute_average()
    {
        var service = Service();
        int notifications = 0;
        service.AveragePricesChanged += _ => notifications++;
        Feed(service, 9, "ObjectAveragePricesHistoryEvent", "0A0308E707");
        Feed(service, 9, "ObjectAveragePricesEvent", "1A050864109601", outgoing: true);
        Feed(service, 9, "ObjectAveragePricesEvent", "1A0408641000");
        Assert.Equal(0, notifications);
        Assert.Equal(0, service.AveragePrice(9, 100));
    }
    private static void Identify(LaunchService service, uint pid, int serverId)
        => typeof(LaunchService).GetMethod("RememberServerIdentity", Hidden)!.Invoke(service, [pid, serverId, "fixture"]);

    [Fact]
    public void Identification_refreshes_an_open_view_even_without_a_new_table()
    {
        var service = Service();
        Identify(service, 10, 42);
        Feed(service, 10, "ObjectAveragePricesEvent", "1A050864109601");
        Assert.Equal(0, service.AveragePrice(9, 100));
        var seen = new List<long>();
        service.AveragePricesChanged += pid => { if (pid == 9) seen.Add(service.AveragePrice(9, 100)); };
        Identify(service, 9, 42);
        Assert.Equal(new long[] { 150 }, seen);
    }

    [Fact]
    public void New_pid_table_replaces_older_shared_value_when_identity_arrives()
    {
        var service = Service();
        Identify(service, 10, 42);
        Feed(service, 10, "ObjectAveragePricesEvent", "1A050864109601"); // old shared 150
        Feed(service, 9, "ObjectAveragePricesEvent", "1A05086410C801"); // newer own 200
        Identify(service, 9, 42);
        Assert.Equal(200, service.AveragePrice(9, 100));
        Assert.Equal(200, service.AveragePrice(10, 100));
    }

    [Fact]
    public void Promotion_does_not_overwrite_a_more_recent_shared_table()
    {
        var service = Service();
        Feed(service, 9, "ObjectAveragePricesEvent", "1A050864109601"); // own 150 first
        Identify(service, 10, 42);
        Feed(service, 10, "ObjectAveragePricesEvent", "1A05086410C801"); // shared 200 later
        Identify(service, 9, 42);
        Assert.Equal(200, service.AveragePrice(9, 100));
        Assert.Equal(200, service.AveragePrice(10, 100));
    }


    [Fact]
    public void A_full_server_table_above_the_generic_field_cap_is_still_read()
    {
        var service = Service();
        State(service, "_serverInfo")[9u] = (42, "fixture");
        var entries = new List<ProtoField>();
        for (int gid = 1; gid <= 7000; gid++)
            entries.Add(new ProtoField { Field = 3, Wire = WireType.Len, Bytes = [0x08, .. RawProtobuf.EncodeVarint((ulong)gid), 0x10, .. RawProtobuf.EncodeVarint((ulong)gid * 3)] });
        byte[] frame = EnvelopeBuilder.BuildServerEvent(Catalog.CodeForName("ObjectAveragePricesEvent")!, entries);
        var decoder = new Decoder(Catalog);
        decoder.Packet += packet => Observe(service, packet);
        decoder.Feed(9, false, [.. RawProtobuf.EncodeVarint((ulong)frame.Length), .. frame]);
        Assert.Equal(3, service.AveragePrice(9, 1));
        Assert.Equal(21000, service.AveragePrice(9, 7000));
    }
}
