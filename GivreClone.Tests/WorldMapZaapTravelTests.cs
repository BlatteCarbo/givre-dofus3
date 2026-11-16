using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class WorldMapZaapTravelTests
{
    private const long RoutesRocailleuses = 164364304; // capture 31/10, acceptée, 210 kamas
    private const long FrigostLocked = 54172489;       // capture 31/10, refusée par le serveur

    [Fact]
    public void Known_payable_zaap_is_sent_from_the_world_map()
    {
        var offer = WorldMapZaapTravel.Evaluate(RoutesRocailleuses, known: true, costKamas: 210, kamas: 210);

        Assert.True(offer.CanSend);
        Assert.Equal(210, offer.CostKamas);
        Assert.Equal(WorldMapZaapTravel.ReadyReason(RoutesRocailleuses, 210), offer.Reason);
        Assert.Equal("210 kamas", WorldMapZaapTravel.FormatCost(offer.CostKamas));
    }

    [Fact]
    public void Unknown_destination_is_refused_with_a_reason()
    {
        var offer = WorldMapZaapTravel.Evaluate(FrigostLocked, known: false, costKamas: 210, kamas: 10_000);

        Assert.False(offer.Known);
        Assert.False(offer.CanSend);
        Assert.Null(offer.CostKamas);
        Assert.Equal(WorldMapZaapTravel.UnknownReason, offer.Reason);
        Assert.Equal("inconnu", WorldMapZaapTravel.FormatCost(null));
    }

    [Theory]
    [InlineData(209)]
    [InlineData(0)]
    public void Kamas_below_the_announced_cost_block_the_send(long kamas)
    {
        var offer = WorldMapZaapTravel.Evaluate(RoutesRocailleuses, true, 210, kamas);

        Assert.True(offer.Known);
        Assert.False(offer.CanSend);
        Assert.Equal(210, offer.CostKamas);
        Assert.Equal(WorldMapZaapTravel.InsufficientReason(kamas, 210), offer.Reason);
    }

    [Fact]
    public void Missing_cost_or_missing_kamas_are_not_treated_as_free()
    {
        var noCost = WorldMapZaapTravel.Evaluate(RoutesRocailleuses, true, null, 10_000);
        var noKamas = WorldMapZaapTravel.Evaluate(RoutesRocailleuses, true, 210, -1);

        Assert.Equal(WorldMapZaapTravel.UnknownCostReason, noCost.Reason);
        Assert.False(noCost.CanSend);
        Assert.Equal(WorldMapZaapTravel.UnknownKamasReason, noKamas.Reason);
        Assert.False(noKamas.CanSend);
    }

    [Fact]
    public void Planner_picks_a_payable_zaap_and_keeps_the_other_routes_available()
    {
        var options = new (long, long?, int)[]
        {
            (100, 5_000, 2),
            (RoutesRocailleuses, 210, 4),
        };

        var pick = WorldMapZaapTravel.ChooseDestination(options, kamas: 300, walkHops: 20, unreachable: false, margin: 2);

        Assert.NotNull(pick);
        Assert.True(pick.Value.CanSend);
        Assert.Equal(RoutesRocailleuses, pick.Value.MapId);
        Assert.Null(WorldMapZaapTravel.ChooseDestination(options, kamas: 300, walkHops: 4, unreachable: false, margin: 2));

        var unknownCostOptions = new (long, long?, int)[]
        {
            (100, null, 2),
            (RoutesRocailleuses, 210, 8),
        };
        var pickDefault = WorldMapZaapTravel.ChooseDestination(unknownCostOptions, kamas: 50_000, walkHops: 20, unreachable: false, margin: 2);
        Assert.NotNull(pickDefault);
        Assert.Equal(RoutesRocailleuses, pickDefault.Value.MapId); // sans allowUnknownCost, le coût connu l'emporte

        var pickUnknown = WorldMapZaapTravel.ChooseDestination(unknownCostOptions, kamas: 50_000, walkHops: 20, unreachable: false, margin: 2, allowUnknownCost: true);
        Assert.NotNull(pickUnknown);
        Assert.True(pickUnknown.Value.CanSend);
        Assert.Equal(100, pickUnknown.Value.MapId); // avec allowUnknownCost, le zaap à 2 sauts est choisi direct
        Assert.Equal(new[] { ZaapRoute.Zaapi, ZaapRoute.WorldMap, ZaapRoute.Ground, ZaapRoute.HavenBag }, WorldMapZaapTravel.Attempts(ZaapRoute.Auto, walkFirst: false));
        Assert.Empty(WorldMapZaapTravel.Attempts(ZaapRoute.Auto, walkFirst: true));
        Assert.Equal(new[] { ZaapRoute.Zaapi }, WorldMapZaapTravel.Attempts(ZaapRoute.Zaapi, walkFirst: true));
        Assert.Equal(new[] { ZaapRoute.Ground }, WorldMapZaapTravel.Attempts(ZaapRoute.Ground, walkFirst: true));
        Assert.Equal(new[] { ZaapRoute.HavenBag }, WorldMapZaapTravel.Attempts(ZaapRoute.HavenBag, walkFirst: false));
        Assert.Equal(new[] { ZaapRoute.WorldMap }, WorldMapZaapTravel.Attempts(ZaapRoute.WorldMap, walkFirst: true));
    }

    [Fact]
    public void ChooseDestination_prefers_cheaper_of_two_equivalent_routes()
    {
        // Deux zaaps équivalents en distance (3 sauts chacun), mais de coûts différents
        const long ZaapCher = 101;
        const long ZaapMoinsCher = 102;
        var options = new (long MapId, long? CostKamas, int HopsAfter)[]
        {
            (ZaapCher, 450, 3),
            (ZaapMoinsCher, 150, 3),
        };

        // Préfère le moins cher (150 < 450)
        var pick = WorldMapZaapTravel.ChooseDestination(options, kamas: 10_000, walkHops: 20, unreachable: false, margin: 2);
        Assert.NotNull(pick);
        Assert.True(pick.Value.CanSend);
        Assert.Equal(ZaapMoinsCher, pick.Value.MapId);
        Assert.Equal(150, pick.Value.CostKamas);

        // Même dans l'ordre inverse des options, le moins cher est choisi
        var optionsInverse = new (long MapId, long? CostKamas, int HopsAfter)[]
        {
            (ZaapMoinsCher, 150, 3),
            (ZaapCher, 450, 3),
        };
        var pickInverse = WorldMapZaapTravel.ChooseDestination(optionsInverse, kamas: 10_000, walkHops: 20, unreachable: false, margin: 2);
        Assert.NotNull(pickInverse);
        Assert.Equal(ZaapMoinsCher, pickInverse.Value.MapId);

        // Entre un zaap à coût connu et un zaap à coût inconnu (null) à sauts égaux : le coût connu l'emporte
        var optionsAvecInconnu = new (long MapId, long? CostKamas, int HopsAfter)[]
        {
            (ZaapCher, null, 3),
            (ZaapMoinsCher, 150, 3),
        };
        var pickConnu = WorldMapZaapTravel.ChooseDestination(optionsAvecInconnu, kamas: 10_000, walkHops: 20, unreachable: false, margin: 2);
        Assert.NotNull(pickConnu);
        Assert.Equal(ZaapMoinsCher, pickConnu.Value.MapId);

        // Si le trajet est strictement plus court en sauts, la distance prime sur le coût
        var optionsSautsDifferents = new (long MapId, long? CostKamas, int HopsAfter)[]
        {
            (ZaapCher, 500, 2),
            (ZaapMoinsCher, 100, 3),
        };
        var pickSauts = WorldMapZaapTravel.ChooseDestination(optionsSautsDifferents, kamas: 10_000, walkHops: 20, unreachable: false, margin: 2);
        Assert.NotNull(pickSauts);
        Assert.Equal(ZaapCher, pickSauts.Value.MapId); // 2 sauts < 3 sauts
    }

    [Fact]
    public void Captured_zaap_payload_parses_real_costs_and_missing_cost_remains_unannounced()
    {
        // Trame réelle capturée (capture_mitm_afk_20261017c, type.ankama.com/hgq)
        // Message TeleportDestinationsEvent : spawn_map_id #1, type #2, destinations #4
        // Sous-messages TeleportDestination : cost #2, map_id #4, level #5, subarea_id #6
        int fDest = F("TeleportDestinationsEvent", "destinations");
        int fCost = F("TeleportDestination", "cost");
        int fMap = F("TeleportDestination", "map_id");
        int fLvl = F("TeleportDestination", "level");
        int fSub = F("TeleportDestination", "subarea_id");

        ProtoField Dest(long map, int? cost, int lvl, int subArea)
        {
            var fields = new List<ProtoField>();
            if (cost is int c) fields.Add(new ProtoField { Field = fCost, Wire = WireType.Varint, Varint = (ulong)c });
            fields.Add(new ProtoField { Field = fMap, Wire = WireType.Varint, Varint = (ulong)map });
            fields.Add(new ProtoField { Field = fLvl, Wire = WireType.Varint, Varint = (ulong)lvl });
            fields.Add(new ProtoField { Field = fSub, Wire = WireType.Varint, Varint = (ulong)subArea });
            return new ProtoField { Field = fDest, Wire = WireType.Len, Message = fields };
        }

        var tree = new List<ProtoField>
        {
            new() { Field = F("TeleportDestinationsEvent", "spawn_map_id"), Wire = WireType.Varint, Varint = 191105026 },
            new() { Field = F("TeleportDestinationsEvent", "type"), Wire = WireType.Varint, Varint = 3 },
            Dest(RoutesRocailleuses, 240, 60, 525), // Routes Rocailleuses (-20,-20) : coût 240 kamas
            Dest(88085249, 310, 40, 490),           // Map 88085249 : coût 310 kamas
            Dest(73400320, 30, 1, 182),             // Map 73400320 : coût 30 kamas
            Dest(FrigostLocked, null, 160, 603),     // FrigostLocked : destination sans champ cost
        };

        var rows = TeleportReader.ReadDestinationDetails(tree);
        Assert.Equal(4, rows.Count);

        var routes = rows.Single(r => r.MapId == RoutesRocailleuses);
        Assert.True(routes.CostKnown);
        Assert.Equal(240, routes.Cost);
        Assert.Equal(60, routes.Level);
        Assert.Equal(525, routes.SubAreaId);

        var map88 = rows.Single(r => r.MapId == 88085249);
        Assert.True(map88.CostKnown);
        Assert.Equal(310, map88.Cost);

        var map73 = rows.Single(r => r.MapId == 73400320);
        Assert.True(map73.CostKnown);
        Assert.Equal(30, map73.Cost);

        var unannounced = rows.Single(r => r.MapId == FrigostLocked);
        Assert.False(unannounced.CostKnown);
        Assert.Equal(0, unannounced.Cost);
        Assert.Equal(160, unannounced.Level);
        Assert.Equal(603, unannounced.SubAreaId);
    }

    [Fact]
    public void Arrival_is_the_observed_map_and_not_the_send()
    {
        Assert.True(WorldMapZaapTravel.Arrived(RoutesRocailleuses, RoutesRocailleuses));
        Assert.False(WorldMapZaapTravel.Arrived(0, RoutesRocailleuses));
        Assert.False(WorldMapZaapTravel.Arrived(FrigostLocked, RoutesRocailleuses));
        Assert.False(WorldMapZaapTravel.Arrived(RoutesRocailleuses, 0));
    }

    [Fact]
    public void Only_zaap_lists_type_0_and_3_teach_a_destination()
    {
        var priced = new (long MapId, long? CostKamas)[] { (RoutesRocailleuses, 210) };
        var book = CharacterZaapBook.From(0, characterId: 7, priced);
        book.Replace(1, 7, new (long, long?)[] { (FrigostLocked, 10) });
        book.Replace(2, 7, new (long, long?)[] { (1, 10) });
        book.Replace(4, 7, new (long, long?)[] { (2, 10) });

        Assert.True(book.TryGet(RoutesRocailleuses, out var cost));
        Assert.Equal(210, cost);
        Assert.False(book.TryGet(FrigostLocked, out _));
        Assert.True(book.Matches(7));
        Assert.True(book.Matches(0));
        Assert.False(book.Matches(8));
        Assert.True(CharacterZaapBook.From(0, 0, priced).Matches(8));

        var seeded = new CharacterZaapBook();
        seeded.SeedUnlocked(7, new long[] { RoutesRocailleuses, FrigostLocked, 0 });
        Assert.Equal(-1, seeded.ListType);
        Assert.Equal(2, seeded.Snapshot().Count);
        Assert.True(seeded.TryGet(RoutesRocailleuses, out var unlocked));
        Assert.Null(unlocked);
        Assert.True(seeded.TryGet(FrigostLocked, out var stillLocked));
        Assert.Null(stillLocked);

        seeded.LearnCosts(1, 7, new (long, long?)[] { (1, 10) });
        seeded.LearnCosts(0, 7, new (long, long?)[] { (RoutesRocailleuses, 210), (FrigostLocked, null) });
        Assert.Equal(0, seeded.ListType);
        Assert.True(seeded.TryGet(RoutesRocailleuses, out var filled));
        Assert.Equal(210, filled);
        Assert.True(seeded.TryGet(FrigostLocked, out var stillUnknown));
        Assert.Null(stillUnknown);
        Assert.False(seeded.TryGet(1, out _));

        seeded.LearnCosts(3, 7, new (long, long?)[] { (RoutesRocailleuses, null) });
        Assert.True(seeded.TryGet(RoutesRocailleuses, out var kept));
        Assert.Equal(210, kept);
        seeded.SeedUnlocked(7, new long[] { RoutesRocailleuses, 42 });
        Assert.True(seeded.TryGet(RoutesRocailleuses, out var keptAgain));
        Assert.Equal(210, keptAgain);
        Assert.True(seeded.TryGet(42, out var added));
        Assert.Null(added);
        Assert.True(seeded.TryGet(FrigostLocked, out _));

        seeded.SeedUnlocked(8, new long[] { 99 });
        Assert.False(seeded.TryGet(RoutesRocailleuses, out _));
        Assert.True(seeded.TryGet(99, out var other));
        Assert.Null(other);
        Assert.Equal(8, seeded.CharacterId);

        book.Replace(3, 9, new (long, long?)[] { (RoutesRocailleuses, null) });
        Assert.Equal(3, book.ListType);
        Assert.True(book.TryGet(RoutesRocailleuses, out var unknown));
        Assert.Null(unknown);
        Assert.Equal(WorldMapZaapTravel.UnknownCostReason, WorldMapZaapTravel.Evaluate(RoutesRocailleuses, true, unknown, 500).Reason);
        Assert.Empty(CharacterZaapBook.From(4, 7, priced).Snapshot());
    }

    [Theory]
    [InlineData("carte", ZaapRoute.WorldMap)]
    [InlineData("Zaap depuis la carte", ZaapRoute.WorldMap)]
    [InlineData("sol", ZaapRoute.Ground)]
    [InlineData("havre-sac", ZaapRoute.HavenBag)]
    [InlineData("auto", ZaapRoute.Auto)]
    [InlineData("zaapi", ZaapRoute.Zaapi)]
    public void French_route_names_select_one_choice(string text, ZaapRoute route)
    {
        Assert.True(WorldMapZaapTravel.TryParseRoute(text, out var parsed));
        Assert.Equal(route, parsed);
        Assert.False(string.IsNullOrEmpty(WorldMapZaapTravel.LabelOf(route)));
    }

    [Fact]
    public void Destination_cost_is_known_only_when_the_field_is_present()
    {
        int destinations = F("TeleportDestinationsEvent", "destinations");
        int mapId = F("TeleportDestination", "map_id");
        int cost = F("TeleportDestination", "cost");
        var tree = new List<ProtoField>
        {
            new()
            {
                Field = destinations, Wire = WireType.Len,
                Message = new List<ProtoField>
                {
                    new() { Field = mapId, Wire = WireType.Varint, Varint = (ulong)RoutesRocailleuses },
                    new() { Field = cost, Wire = WireType.Varint, Varint = 210 },
                },
            },
            new()
            {
                Field = destinations, Wire = WireType.Len,
                Message = new List<ProtoField>
                {
                    new() { Field = mapId, Wire = WireType.Varint, Varint = (ulong)FrigostLocked },
                },
            },
            new() { Field = F("TeleportDestinationsEvent", "type"), Wire = WireType.Varint, Varint = 0 },
        };

        var rows = TeleportReader.ReadDestinationDetails(tree);

        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].CostKnown);
        Assert.Equal(210, rows[0].Cost);
        Assert.False(rows[1].CostKnown);
        Assert.Equal(0, TeleportReader.ReadListType(tree));
        Assert.True(WorldMapZaapTravel.IsCharacterZaapList(0));
        Assert.True(WorldMapZaapTravel.IsCharacterZaapList(3));
        Assert.False(WorldMapZaapTravel.IsCharacterZaapList(1));
        Assert.False(WorldMapZaapTravel.IsCharacterZaapList(4));
    }

    [Fact]
    public void World_map_request_is_map_id_and_source_type_5_only()
    {
        Assert.Equal(1, F("TeleportRequest", "map_id"));
        Assert.Equal(2, F("TeleportRequest", "source_type"));
        Assert.Equal(4, F("TeleportRequest", "destination_type"));

        var (code, payload) = Request(MoveForge.BuildTeleportFramed(Code("TeleportRequest"), RoutesRocailleuses, WorldMapZaapTravel.SourceType));

        Assert.Equal(Code("TeleportRequest"), code);
        Assert.Equal(RoutesRocailleuses, V(payload, F("TeleportRequest", "map_id")));
        Assert.Equal(WorldMapZaapTravel.SourceType, V(payload, F("TeleportRequest", "source_type")));
        Assert.DoesNotContain(payload, field => field.Field == F("TeleportRequest", "destination_type"));
    }

    [Fact]
    public void Ground_and_haven_requests_keep_their_source_types()
    {
        var (groundCode, ground) = Request(MoveForge.BuildTeleportFramed(Code("TeleportRequest"), 73400320, sourceType: 0));
        var (havenCode, haven) = Request(MoveForge.BuildTeleportFramed(Code("TeleportRequest"), 73400320, sourceType: 3));

        Assert.Equal(Code("TeleportRequest"), groundCode);
        Assert.Equal(Code("TeleportRequest"), havenCode);
        Assert.Equal(73400320, V(ground, F("TeleportRequest", "map_id")));
        Assert.DoesNotContain(ground, field => field.Field == F("TeleportRequest", "source_type"));
        Assert.Equal(3, V(haven, F("TeleportRequest", "source_type")));
        Assert.DoesNotContain(haven, field => field.Field == F("TeleportRequest", "destination_type"));
    }

    private static readonly ProtocolCatalog Mapping = ProtocolCatalog.LoadFromDirectory(
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "data"));

    private static string Code(string name) => Mapping.CodeForName(name)
        ?? throw new System.InvalidOperationException($"Mapping missing message {name}");

    private static int F(string name, string property) => Mapping.FieldNumber(name, property)
        ?? throw new System.InvalidOperationException($"Mapping missing field {name}.{property}");

    private static (string Code, IReadOnlyList<ProtoField> Payload) Request(byte[] framed)
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
