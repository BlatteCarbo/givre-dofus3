using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class ZaapiTravelTests
{
    // Cartes du TeleportDestinationsEvent type 1 du 31/10. Les noms ne sont PAS dans le paquet :
    // ils ne doivent apparaître que si le test fournit l'indice lui-même.
    private const long SortieEst = 54169896;
    private const long Port = 54530067;
    private const long BourgadeZaap = 54172969;
    private const long SansCout = 54171950;

    [Fact]
    public void Type_1_list_keeps_cost_subarea_level_and_destination_kind()
    {
        int destinations = F("TeleportDestinationsEvent", "destinations");
        int listType = F("TeleportDestinationsEvent", "type");
        int mapId = F("TeleportDestination", "map_id");
        int cost = F("TeleportDestination", "cost");
        int sub = F("TeleportDestination", "subarea_id");
        int level = F("TeleportDestination", "level");
        int kind = F("TeleportDestination", "type");
        Assert.Equal(1, kind);
        Assert.Equal(2, cost);
        Assert.Equal(4, mapId);
        Assert.Equal(5, level);
        Assert.Equal(6, sub);

        ProtoField Dest(long map, int? kamas, int subArea, int lvl, int? teleporter)
        {
            var fields = new List<ProtoField>();
            if (teleporter is int t) fields.Add(new ProtoField { Field = kind, Wire = WireType.Varint, Varint = (ulong)t });
            if (kamas is int k) fields.Add(new ProtoField { Field = cost, Wire = WireType.Varint, Varint = (ulong)k });
            fields.Add(new ProtoField { Field = mapId, Wire = WireType.Varint, Varint = (ulong)map });
            if (lvl > 0) fields.Add(new ProtoField { Field = level, Wire = WireType.Varint, Varint = (ulong)lvl });
            if (subArea > 0) fields.Add(new ProtoField { Field = sub, Wire = WireType.Varint, Varint = (ulong)subArea });
            return new ProtoField { Field = destinations, Wire = WireType.Len, Message = fields };
        }

        var tree = new List<ProtoField>
        {
            Dest(SortieEst, 20, 601, 80, 1),
            Dest(Port, 20, 600, 80, 1),
            Dest(BourgadeZaap, 20, 601, 80, 1),
            Dest(SansCout, null, 601, 80, 1),
            Dest(1, 0, 0, 0, null),
            new() { Field = listType, Wire = WireType.Varint, Varint = 1 },
        };

        Assert.Equal(1, TeleportReader.ReadListType(tree));
        var rows = TeleportReader.ReadDestinationDetails(tree);

        Assert.Equal(5, rows.Count);
        Assert.Equal(SortieEst, rows[0].MapId);
        Assert.True(rows[0].CostKnown);
        Assert.Equal(20, rows[0].Cost);
        Assert.Equal(601, rows[0].SubAreaId);
        Assert.Equal(80, rows[0].Level);
        Assert.True(rows[0].TypeKnown);
        Assert.Equal(1, rows[0].Type);
        Assert.Equal(600, rows[1].SubAreaId);
        Assert.False(rows[3].CostKnown);
        Assert.True(rows[3].TypeKnown);
        Assert.True(rows[4].CostKnown);
        Assert.Equal(0, rows[4].Cost);
        Assert.False(rows[4].TypeKnown);
        Assert.False(WorldMapZaapTravel.IsCharacterZaapList(1));
    }

    [Fact]
    public void Labels_come_only_from_the_hints_supplied_for_maps_the_server_sent()
    {
        var sent = new[]
        {
            new TeleportReader.Destination(SortieEst, 20, 601, 80, true, 1, true),
            new TeleportReader.Destination(Port, 20, 600, 80, true, 1, true),
            new TeleportReader.Destination(BourgadeZaap, 20, 601, 80, true, 1, true),
            new TeleportReader.Destination(SansCout, 0, 601, 80, false, 1, true),
        };
        var hints = new[]
        {
            new ZaapiHint(SortieEst, "Sortie Est", 4, "Divers", -72, -40),
            new ZaapiHint(SortieEst, "Atelier des bûcherons", 3, "Ateliers", -72, -40),
            new ZaapiHint(Port, "Hôtel de vente des runes", 2, "Hôtels de vente", -77, -38),
            new ZaapiHint(BourgadeZaap, "Zaap", 9, "Transports", -78, -41),
            new ZaapiHint(999, "Ailleurs", 4, "Divers", 1, 2),
            new ZaapiHint(SansCout, "Sans coût", 4, "Divers", -70, -40),
        };

        var stops = ZaapiTravel.Present(sent, hints, District, map => map == BourgadeZaap ? (-78, -41) : null);

        Assert.Equal(5, stops.Count);
        Assert.Equal(new[] { "Sortie Est", "Atelier des bûcherons" }, stops.Where(s => s.MapId == SortieEst).Select(s => s.Name).ToArray());
        Assert.All(stops.Where(s => s.MapId == SortieEst), s =>
        {
            Assert.Equal("La Bourgade", s.District);
            Assert.Equal((long?)20, s.CostKamas);
            Assert.Equal("-72,-40", ZaapiTravel.Coordinates(s.X, s.Y));
        });
        var port = Assert.Single(stops, s => s.MapId == Port);
        Assert.Equal("Port de givre", port.District);
        Assert.Equal(2, port.CategoryId);
        Assert.Equal("Hôtels de vente", port.Category);
        var unhinted = Assert.Single(stops, s => s.MapId == BourgadeZaap);
        Assert.Equal("", unhinted.Name);
        Assert.Equal("", unhinted.Category);
        Assert.Equal(0, unhinted.CategoryId);
        Assert.Equal((int?)-78, unhinted.X);
        Assert.Equal((int?)-41, unhinted.Y);
        Assert.DoesNotContain(stops, s => s.MapId == 999 || s.Name == "Zaap" || s.Name == "Ailleurs");
        var missing = Assert.Single(stops, s => s.MapId == SansCout);
        Assert.Null(missing.CostKamas);
        Assert.Equal("Sans coût", missing.Name);

        var bare = ZaapiTravel.Present(sent, System.Array.Empty<ZaapiHint>(), _ => null, map => map == BourgadeZaap ? (-78, -41) : null);
        Assert.Equal(4, bare.Count);
        Assert.All(bare, s => Assert.Equal("", s.Name));
        Assert.DoesNotContain(bare, s => s.Name == "Sortie Est");
        Assert.Equal("-78,-41", ZaapiTravel.Coordinates(bare.Single(s => s.MapId == BourgadeZaap).X, bare.Single(s => s.MapId == BourgadeZaap).Y));
    }

    [Fact]
    public void Zaapi_book_keeps_type_1_and_ignores_zaap_prism_and_anomaly_lists()
    {
        var book = ZaapiBook.From(7, new[] { new TeleportReader.Destination(SortieEst, 20, 601, 80, true, 1, true) });
        Assert.True(book.Listed(SortieEst, out var cost));
        Assert.Equal((long?)20, cost);
        Assert.Equal(new[] { SortieEst }, book.MapIds());

        book.Replace(0, 7, new[] { new TeleportReader.Destination(1, 10, 0, 0, true) });
        book.Replace(2, 8, new[] { new TeleportReader.Destination(2, 10, 0, 0, true) });
        book.Replace(4, 8, new[] { new TeleportReader.Destination(3, 10, 0, 0, true) });

        Assert.Equal(ZaapiTravel.ListType, book.ListType);
        Assert.True(book.Listed(SortieEst, out _));
        Assert.False(book.Listed(1, out _));
        Assert.True(book.Matches(7));
        Assert.True(book.Matches(0));
        Assert.False(book.Matches(8));
        Assert.Empty(CharacterZaapBook.From(ZaapiTravel.ListType, 7, new (long, long?)[] { (SortieEst, 20) }).Snapshot());
    }

    [Fact]
    public void Offer_uses_the_announced_cost_and_refuses_an_unknown_one()
    {
        var ready = ZaapiTravel.Evaluate(SortieEst, true, 20, 20);
        Assert.True(ready.CanSend);
        Assert.Equal((long?)20, ready.CostKamas);
        Assert.Equal(ZaapiTravel.ReadyReason(SortieEst, 20), ready.Reason);

        var free = ZaapiTravel.Evaluate(SortieEst, true, 0, 0);
        Assert.True(free.CanSend);
        Assert.Equal((long?)0, free.CostKamas);

        Assert.Equal(ZaapiTravel.UnknownReason, ZaapiTravel.Evaluate(SortieEst, false, 20, 100).Reason);
        Assert.Equal(ZaapiTravel.UnknownCostReason, ZaapiTravel.Evaluate(SortieEst, true, null, 100).Reason);
        Assert.Equal(ZaapiTravel.UnknownKamasReason, ZaapiTravel.Evaluate(SortieEst, true, 20, -1).Reason);
        Assert.Contains("5", ZaapiTravel.Evaluate(SortieEst, true, 20, 5).Reason);
        Assert.False(ZaapiTravel.Evaluate(SortieEst, true, 20, 5).CanSend);
    }

    [Fact]
    public void Choose_picks_the_payable_destination_that_shortens_the_walk()
    {
        var options = new (long MapId, long? CostKamas, int HopsAfter)[] { (Port, 20, 4), (SortieEst, 20, 1) };

        var pick = ZaapiTravel.Choose(options, 100, 10, unreachable: false, margin: 2);

        Assert.NotNull(pick);
        Assert.True(pick.Value.CanSend);
        Assert.Equal(SortieEst, pick.Value.MapId);
        Assert.Equal(1, pick.Value.HopsAfter);
        Assert.Null(ZaapiTravel.Choose(options, 100, 2, unreachable: false, margin: 2));

        var poor = ZaapiTravel.Choose(new (long, long?, int)[] { (SortieEst, 20, 0) }, 5, 0, unreachable: true, margin: 2);
        Assert.NotNull(poor);
        Assert.False(poor.Value.CanSend);
        Assert.Equal(SortieEst, poor.Value.MapId);
    }

    [Fact]
    public void Arrival_is_the_observed_map()
    {
        Assert.True(ZaapiTravel.Arrived(SortieEst, SortieEst));
        Assert.False(ZaapiTravel.Arrived(Port, SortieEst));
        Assert.False(ZaapiTravel.Arrived(0, SortieEst));
        Assert.Contains("20", ZaapiTravel.ArrivedReason(SortieEst, 20));
    }

    [Fact]
    public void Usable_zaapi_is_type_106_or_skill_157_with_an_enabled_skill()
    {
        Assert.True(ZaapiTravel.IsUsable(106, new[] { (1, true) }));
        Assert.True(ZaapiTravel.IsUsable(16, new[] { (157, true) }));
        Assert.False(ZaapiTravel.IsUsable(106, new[] { (157, false) }));
        Assert.False(ZaapiTravel.IsUsable(16, new[] { (114, true) }));
        Assert.True(ZaapiTravel.IsTabCategory(2));
        Assert.True(ZaapiTravel.IsTabCategory(3));
        Assert.True(ZaapiTravel.IsTabCategory(4));
        Assert.False(ZaapiTravel.IsTabCategory(9));
    }

    [Fact]
    public void Teleport_and_use_frames_match_the_captured_field_numbers()
    {
        Assert.Equal(1, F("TeleportRequest", "map_id"));
        Assert.Equal(2, F("TeleportRequest", "source_type"));
        Assert.Equal(4, F("TeleportRequest", "destination_type"));
        Assert.Equal(1, F("InteractiveUseRequest", "skill_instance_uid"));
        Assert.Equal(3, F("InteractiveUseRequest", "element_id"));

        var (_, teleport) = Frame(MoveForge.BuildTeleportFramed(Code("TeleportRequest"), SortieEst, ZaapiTravel.SourceType, ZaapiTravel.DestinationType));
        Assert.Equal(SortieEst, V(teleport, 1));
        Assert.Equal(ZaapiTravel.SourceType, V(teleport, 2));
        Assert.Equal(ZaapiTravel.DestinationType, V(teleport, 4));

        // Capture : champ 1 = 28664 (uid du skill), champ 3 = 524248 (élément). Le journal de carte nomme 524248 l'élément Zaapi.
        var (_, use) = Frame(MoveForge.BuildInteractiveUseFramed(28664, 524248));
        Assert.Equal(28664, V(use, F("InteractiveUseRequest", "skill_instance_uid")));
        Assert.Equal(524248, V(use, F("InteractiveUseRequest", "element_id")));
    }

    private static string? District(int subAreaId) => subAreaId switch
    {
        601 => "La Bourgade",
        600 => "Port de givre",
        _ => null,
    };

    private static readonly ProtocolCatalog Mapping = ProtocolCatalog.LoadFromDirectory(
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "data"));

    private static string Code(string name) => Mapping.CodeForName(name)
        ?? throw new System.InvalidOperationException($"Mapping missing message {name}");

    private static int F(string name, string property) => Mapping.FieldNumber(name, property)
        ?? throw new System.InvalidOperationException($"Mapping missing field {name}.{property}");

    private static (string Code, IReadOnlyList<ProtoField> Payload) Frame(byte[] framed)
    {
        Assert.NotEmpty(framed);
        int pos = 0;
        Assert.True(RawProtobuf.TryReadVarint(framed, ref pos, out ulong len));
        Assert.Equal((int)len, framed.Length - pos);
        var gm = RawProtobuf.Parse(framed[pos..]);
        var req = gm.First(f => f.Field == F("GameMessage", "request") && f.Message is not null).Message!;
        var any = req.First(f => f.Field == F("Request", "content") && f.Message is not null).Message!;
        var url = any.First(f => f.Field == 1).String!;
        var payload = any.FirstOrDefault(f => f.Field == 2 && f.Message is not null)?.Message ?? new List<ProtoField>();
        return (url["type.ankama.com/".Length..], payload);
    }

    private static long V(IReadOnlyList<ProtoField> payload, int field)
        => payload.First(f => f.Field == field && f.Wire == WireType.Varint).AsSignedVarint;
}
