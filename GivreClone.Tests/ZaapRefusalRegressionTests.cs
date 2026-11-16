extern alias F06App;

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Names = F06App::GivreClone.App.Services.GameNameResolver;
using Packet = F06App::GivreClone.App.Services.DecodedPacket;
using WorldStore = F06App::GivreClone.App.Services.BotWorldStore;

namespace GivreClone.Tests;

public sealed class ZaapRefusalRegressionTests
{
    private const uint Pid = 0x40000001;
    private const long HavenBag = 162791424, Frigost = 54172969;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));

    private static void Set(object obj, string name, object value)
        => obj.GetType().GetField(name, Hidden)!.SetValue(obj, value);

    private static IDictionary State(Launch service, string name)
        => (IDictionary)typeof(Launch).GetField(name, Hidden)!.GetValue(service)!;

    private static (Host Host, Launch Service) Offline()
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (var field in typeof(Launch).GetFields(Hidden).Where(f => f.FieldType.IsGenericType
            && f.FieldType.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>)))
            field.SetValue(service, Activator.CreateInstance(field.FieldType));
        Set(service, "_catalog", Catalog);
        // In-memory game i18n/InfoMessage fixture, without reading the installed game or starting a service.
        var names = (Names)RuntimeHelpers.GetUninitializedObject(typeof(Names));
        Set(names, "_initDone", true);
        byte[] text = Encoding.UTF8.GetBytes("Niveau %1 requis");
        Set(names, "_bin", new byte[] { (byte)text.Length }.Concat(text).ToArray());
        Set(names, "_nameIdToOffset", new Dictionary<int, long> { [999] = 0 });
        Set(names, "_infoMessageToTextId", new Dictionary<long, int> { [((long)2 << 32) | 42] = 999 });
        Set(service, "_gameNames", names);
        State(service, "_lastMap")[Pid] = HavenBag;
        var host = (Host)RuntimeHelpers.GetUninitializedObject(typeof(Host));
        Set(host, "_pid", Pid);
        Set(host, "_launch", service);
        Set(host, "_refusedZaaps", new Dictionary<long, string>());
        return (host, service);
    }

    private static void Receive(Launch service, int messageId = 42, int type = 2, uint pid = Pid)
    {
        int F(string field) => Catalog.FieldNumber("TextInformationEvent", field)
            ?? throw new InvalidOperationException("Missing mapped field: " + field);
        var packet = new Packet(pid, false, Catalog.CodeForName("TextInformationEvent")!, "TextInformationEvent",
            new ProtoField[]
            {
                new() { Field = F("message_id"), Wire = WireType.Varint, Varint = (ulong)messageId },
                new() { Field = F("message_type"), Wire = WireType.Varint, Varint = (ulong)type },
                new() { Field = F("parameters"), Wire = WireType.Len, Bytes = Encoding.UTF8.GetBytes("100") }
            }, []);
        typeof(Launch).GetMethod("SnowObserveTreasure", Hidden)!.Invoke(service, [packet, packet.Name]);
        ((Action<Packet>?)typeof(Launch).GetField("PacketDecoded", Hidden)!.GetValue(service))?.Invoke(packet);
    }

    private static (bool Arrived, string? Reason) Confirm(Host host, Func<bool> send, long destination = Frigost)
    {
        object?[] args = [destination, send, CancellationToken.None, null, 0];
        bool arrived = (bool)typeof(Host).GetMethod("ConfirmZaapTeleport", Hidden)!.Invoke(host, args)!;
        return (arrived, (string?)args[3]);
    }

    private static bool Report(Host host, Func<bool> action)
        => (bool)typeof(Host).GetMethod("ReportTravel", Hidden)!.Invoke(host, [action])!;

    private static void Log(Host host, string category, string message, bool success = false)
    {
        var entry = new F06App::GivreClone.App.Services.LuaLogEntry(DateTime.Now,
            success ? F06App::GivreClone.App.Services.LuaLogLevel.Success : F06App::GivreClone.App.Services.LuaLogLevel.Warning,
            category, message);
        ((Action<F06App::GivreClone.App.Services.LuaLogEntry>?)typeof(Host).GetField("Log", Hidden)!.GetValue(host))?.Invoke(entry);
    }

    [Fact]
    public void Action_keeps_server_refusal_across_retries_and_the_generic_hdv_failure()
    {
        var (host, service) = Offline();
        string expected = service.GameNames.InfoMessageText(2, 42, ["100"]);
        Assert.False(Report(host, () =>
        {
            Report(host, () => Confirm(host, () => { Receive(service); return true; }).Arrived);
            Log(host, "trajet", "aucun chemin world-graph");
            Log(host, "hdv", "voyage vers le HDV échoué");
            return false;
        }));
        Assert.Equal((false, expected), host.LastTravelResult);

        // A new action must not inherit that refusal.
        Assert.False(Report(host, () => { Log(host, "trajet", "saut : envoi échoué"); return false; }));
        Assert.Equal((false, "saut : envoi échoué"), host.LastTravelResult);
    }

    [Fact]
    public void Successful_fallback_does_not_report_an_intermediate_failure()
    {
        var (host, _) = Offline();
        Assert.True(Report(host, () =>
        {
            Report(host, () => { Log(host, "trajet", "raccourci refusé"); return false; });
            Log(host, "trajet", "arrivée", success: true);
            return true;
        }));
        Assert.Equal((true, "arrivée"), host.LastTravelResult);
    }

    [Fact]
    public void Mcp_completed_travel_exposes_the_actual_failure_and_does_not_confirm_arrival()
    {
        var (host, _) = Offline();
        Report(host, () => { Log(host, "trajet", "aucun chemin world-graph"); return false; });
        var formatter = typeof(F06App::GivreClone.App.ViewModels.BotsViewModel)
            .GetMethod("ControlTravelResult", BindingFlags.Static | BindingFlags.NonPublic)!;
        var json = (System.Text.Json.Nodes.JsonObject)formatter.Invoke(null, [host.LastTravelResult!.Value])!;
        Assert.False(json["ok"]!.GetValue<bool>());
        Assert.False(json["completionConfirmed"]!.GetValue<bool>());
        Assert.Equal("failed", json["status"]!.GetValue<string>());
        Assert.Equal(host.LastTravelResult.Value.Reason, json["reason"]!.GetValue<string>());
    }

    [Fact]
    public void Synchronous_mapped_refusal_is_resolved_and_the_same_destination_is_not_sent_again()
    {
        var (host, service) = Offline();
        int sends = 0;
        bool Send() { sends++; Receive(service); return true; }

        var first = Confirm(host, Send);
        var repeated = Confirm(host, Send);

        Assert.False(first.Arrived);
        Assert.Equal(service.GameNames.InfoMessageText(2, 42, ["100"]), first.Reason);
        Assert.Equal(first, repeated);
        Assert.Equal(1, sends);
        Assert.Equal(HavenBag, service.CurrentMap(Pid));
        Assert.Null(typeof(Launch).GetField("PacketDecoded", Hidden)!.GetValue(service));
    }

    [Fact]
    public void First_refusal_is_kept_when_another_system_message_follows()
    {
        var (host, service) = Offline();
        var result = Confirm(host, () => { Receive(service); Receive(service, 9999); return true; });
        Assert.Equal(service.GameNames.InfoMessageText(2, 42, ["100"]), result.Reason);
        Assert.Equal(9999, service.LastTextInfo(Pid)!.MessageId);
    }

    [Fact]
    public void Stale_and_other_bot_messages_do_not_turn_a_confirmed_arrival_into_a_refusal()
    {
        var (host, service) = Offline();
        Receive(service);
        var result = Confirm(host, () =>
        {
            Receive(service, pid: Pid + 1);
            State(service, "_lastMap")[Pid] = Frigost;
            return true;
        });
        Assert.True(result.Arrived);
        Assert.Null(result.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Missing_i18n_keeps_the_actual_message_identifiers_and_parameters(int type)
    {
        var (host, service) = Offline();
        var result = Confirm(host, () => { Receive(service, messageId: 9999, type: type); return true; });
        Assert.False(result.Arrived);
        Assert.Contains($"{type}/9999", result.Reason);
        Assert.Contains("100", result.Reason);
    }

    [Fact]
    public void A_system_message_does_not_refuse_a_teleport_whose_map_is_confirmed()
    {
        var (host, service) = Offline();
        var result = Confirm(host, () =>
        {
            Receive(service);
            State(service, "_lastMap")[Pid] = Frigost;
            return true;
        });
        Assert.True(result.Arrived);
        Assert.Null(result.Reason);
        Assert.Empty((IDictionary)typeof(Host).GetField("_refusedZaaps", Hidden)!.GetValue(host)!);
    }

    [Fact]
    public void Failed_dispatch_does_not_blacklist_an_unsent_destination()
    {
        var (host, service) = Offline();
        Assert.False(Confirm(host, () => false).Arrived);
        Assert.True(Confirm(host, () => { State(service, "_lastMap")[Pid] = Frigost; return true; }).Arrived);
    }

    [Fact]
    public void Haven_bag_recovery_closes_the_dialog_then_toggles_once_even_when_return_map_differs()
    {
        var (_, service) = Offline();
        var world = new WorldStore(service);
        using var host = new Host(Pid, service, world);
        Set(host, "_settleMinMs", 0);
        Set(host, "_settleMaxMs", 0);
        State(service, "_charId")[Pid] = 123L;
        var sends = new List<string>();
        void Arrive(long map)
        {
            State(service, "_lastMap")[Pid] = map;
            var state = world.Get(Pid);
            state.MapId = state.CellsMap = map;
            state.SelfCell = 100;
            state.SelfCellMap = map;
            state.SelfCellConfirmed = true;
            state.Cells = [new MapCell(100, true, true, 0)];
        }
        Arrive(HavenBag);
        service.RegisterSocketInjector(Pid, bytes =>
        {
            var frame = RawProtobuf.ConsumeFrames(bytes, out _).Single();
            string name = Catalog.NameForCode(RawProtobuf.FindTypeUrls(RawProtobuf.Parse(frame)).Single().Code)!;
            sends.Add(name);
            if (name == "HavenBagEnterRequest")
            {
                Assert.Equal(new[] { "DialogLeaveRequest", "HavenBagEnterRequest" }, sends);
                Arrive(88081689); // A real return map, not the previously remembered 88081688.
            }
            return true;
        });
        var recover = typeof(Host).GetMethod("ToggleOutOfHavenBag", Hidden)!;
        Assert.True((bool)recover.Invoke(host, [HavenBag, CancellationToken.None])!);
        Assert.Equal(88081689, service.CurrentMap(Pid));
        Assert.Equal(2, sends.Count);
        // A late teleport / already successful exit must never toggle back INTO the haven bag.
        Assert.True((bool)recover.Invoke(host, [HavenBag, CancellationToken.None])!);
        Assert.Equal(2, sends.Count);
    }
}
