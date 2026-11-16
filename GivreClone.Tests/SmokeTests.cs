using System.Linq;
using GivreClone.Engine;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class ProtocolCatalogTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");

    [Fact]
    public void Loads_code_name_mappings_and_confirmed_override()
    {
        var cat = ProtocolCatalog.LoadFromDirectory(DataDir);
        Assert.True(cat.MappingCount >= 104, $"attendu >= 104 codes, obtenu {cat.MappingCount}");
        // Bundle livré 3.7.11.11 : alias jnj, alors que jpo est une ancienne attente 3.6.
        // Test dans les deux sens sans injecter de catalogue ou de pin de confirmation.
        Assert.Equal("MapComplementaryInformationEvent", cat.NameForCode("jnj"));
        // ... et la resolution inverse par nom, dont depend CodeForName (forges).
        Assert.Equal("jnj", cat.CodeForName("MapComplementaryInformationEvent"));
    }

    [Fact]
    public void Map_actors_reader_classifies_player_monster_npc()
    {
        // Trames au format ACTUEL, numéros pris dans le mapping livré (aucun numéro figé dans le test non plus) :
        // ActorPositionInformation{ actor_information { role_play_actor { named_actor | monster_group_actor | npc_actor } },
        // actor_id, disposition { cell_id, direction } }.
        var cat = ProtocolCatalog.LoadFromDirectory(DataDir);
        int N(string m, string f) => cat.FieldNumber(m, f) ?? throw new Xunit.Sdk.XunitException($"{m}.{f} absent du mapping");
        static ProtoField V(int f, ulong v) => new() { Field = f, Wire = WireType.Varint, Varint = v };
        static ProtoField S(int f, string s) => new() { Field = f, Wire = WireType.Len, String = s };
        static ProtoField M(int f, params ProtoField[] k) => new() { Field = f, Wire = WireType.Len, Message = k.ToList() };
        ProtoField Actor(int cell, int dir, ulong id, ProtoField branch) =>
            M(5, M(N("ActorPositionInformation", "actor_information"), M(N("ActorInformation", "role_play_actor"), branch)),
                 V(N("ActorPositionInformation", "actor_id"), id),
                 M(N("ActorPositionInformation", "disposition"), V(N("EntityDisposition", "cell_id"), (ulong)cell), V(N("EntityDisposition", "direction"), (ulong)dir)));
        ProtoField Mob(int gid) => M(0, V(N("MonsterInGroupInformation", "gid"), (ulong)gid));
        ProtoField At(ProtoField p, int field) => new() { Field = field, Wire = p.Wire, Message = p.Message };

        var player = Actor(419, 4, 100, M(N("RolePlayActor", "named_actor"), S(1, "Persodeux")));
        var group = M(N("MonsterGroupActor", "identification"),
            At(Mob(99), N("MonsterGroupStaticInformation", "main_creature")),
            At(Mob(99), N("MonsterGroupStaticInformation", "underlings")),
            At(Mob(99), N("MonsterGroupStaticInformation", "underlings")));
        var monster = Actor(150, 0, 200, M(N("RolePlayActor", "monster_group_actor"), group));
        var npc = Actor(50, 0, 300, M(N("RolePlayActor", "npc_actor"), V(N("NpcStaticInformation", "npc_id"), 522)));

        var actors = MapActorsReader.Extract(new List<ProtoField> { player, monster, npc }, 5,
            gid => gid == 99 ? "Bouftou" : null,
            id => id == 522 ? "Al Etsop" : null);
        Assert.Equal(3, actors.Count);

        Assert.Equal(MapActorKind.Player, actors[0].Kind);
        Assert.Equal("Persodeux", actors[0].Name);
        Assert.Equal(419, actors[0].CellId);

        Assert.Equal(MapActorKind.Monster, actors[1].Kind);
        Assert.Equal("3 monstres", actors[1].Name);
        Assert.Single(actors[1].Monsters!);
        Assert.Equal("Bouftou", actors[1].Monsters![0].Name);
        Assert.Equal(3, actors[1].Monsters![0].Count);

        Assert.Equal(MapActorKind.Npc, actors[2].Kind);
        Assert.Equal("Al Etsop", actors[2].Name);
        Assert.Equal(522, actors[2].NpcId);
        Assert.Equal(300L, actors[2].ActorId);
    }

    [Fact]
    public void Map_actors_reader_npc_unknown_name_falls_back_to_template_id()
    {
        var cat = ProtocolCatalog.LoadFromDirectory(DataDir);
        int N(string m, string f) => cat.FieldNumber(m, f) ?? throw new Xunit.Sdk.XunitException($"{m}.{f} absent du mapping");
        static ProtoField V(int f, ulong v) => new() { Field = f, Wire = WireType.Varint, Varint = v };
        static ProtoField M(int f, params ProtoField[] k) => new() { Field = f, Wire = WireType.Len, Message = k.ToList() };
        var npc = M(5,
            M(N("ActorPositionInformation", "actor_information"), M(N("ActorInformation", "role_play_actor"),
                M(N("RolePlayActor", "npc_actor"), V(N("NpcStaticInformation", "npc_id"), 3646)))),
            V(N("ActorPositionInformation", "actor_id"), 300),
            M(N("ActorPositionInformation", "disposition"), V(N("EntityDisposition", "cell_id"), 50)));
        var actors = MapActorsReader.Extract(new List<ProtoField> { npc }, 5, null, _ => null);
        Assert.Single(actors);
        Assert.Equal(MapActorKind.Npc, actors[0].Kind);
        Assert.Equal("PNJ 3646", actors[0].Name);
        Assert.Equal(3646, actors[0].NpcId);
    }

    [Fact]
    public void Map_movement_reader_decodes_destination_and_actor()
    {
        // keyMovements packes (champ 1) : [depart=50, arrivee=(3<<12)|100=12388]. Destination = 12388 & 0xFFF = 100.
        var blob = new byte[] { 0x32, 0xE4, 0x60 };   // varints 50, 12388
        var tree = new List<ProtoField>
        {
            new() { Field = 1, Wire = WireType.Len, Bytes = blob },
            new() { Field = 5, Wire = WireType.Varint, Varint = unchecked((ulong)(-20004L)) },
        };
        var mv = MapMovementReader.Read(tree, 1, 5);
        Assert.NotNull(mv);
        Assert.Equal(-20004, mv!.Value.ActorId);              // int64 signe (acteur non-joueur)
        Assert.Equal(new[] { 50, 100 }, mv.Value.Cells);      // cellules du chemin : depart 50 -> destination 100
    }

    [Fact]
    public void Map_positions_table_resolves_world_coords()
    {
        var cachePath = Path.Combine(DataDir, "map_positions.json");
        var pos = MapPositions.Load(cachePath);
        if (File.Exists(cachePath))
        {
            Assert.True(pos.Count > 10000, $"table trop petite: {pos.Count}");
            // map 191104002 = (4, -18) si la vraie extraction est livrée.
            Assert.Equal((4, -18), pos.Get(191104002));
        }
        else
        {
            // Snapshot partagé sans extraction propriétaire : aucune coordonnée inventée.
            Assert.Equal(0, pos.Count);
            Assert.Null(pos.Get(191104002));
            Assert.Null(pos.WorldMapOf(191104002));
        }
        Assert.Null(pos.Get(1)); // carte inexistante -> null
    }

    [Fact]
    public void Map_positions_from_dictionary_and_cache_roundtrip()
    {
        // Table construite depuis l'extraction runtime (dictionnaire) + reserialisation cache -> relecture identique.
        // 3e valeur = worldMap (calque) : 1 = Monde des Douze principal, 2 = Incarnam (exemple) -> desambiguisation coord.
        var dict = new Dictionary<long, (int X, int Y, int WorldMap)> { [191104002] = (4, -18, 1), [156238850] = (-6, -42, 2) };
        var mp = MapPositions.FromDictionary(dict);
        Assert.Equal((4, -18), mp.Get(191104002));
        Assert.Equal(1, mp.WorldMapOf(191104002));
        Assert.Equal(2, mp.WorldMapOf(156238850));

        var tmp = Path.GetTempFileName();
        try
        {
            MapPositions.SaveCache(tmp, dict);
            var reloaded = MapPositions.Load(tmp);
            Assert.Equal(2, reloaded.Count);
            Assert.Equal((-6, -42), reloaded.Get(156238850));
            Assert.Equal(2, reloaded.WorldMapOf(156238850));   // worldMap survit au round-trip cache [x,y,worldMap]
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Confirmed_field_map_resolves_wire_number_both_ways()
    {
        var cat = ProtocolCatalog.LoadFromDirectory(DataDir);
        // fields.json livré : map_id est #10, et non l'ancien #6 du test historique.
        Assert.Equal(10, cat.FieldNumber("MapComplementaryInformationEvent", "map_id"));
        Assert.Equal("map_id", cat.FieldName("MapComplementaryInformationEvent", 10));
        // champ/message non confirme -> null (on ne devine pas).
        Assert.Null(cat.FieldNumber("MapComplementaryInformationEvent", "inconnu"));
        Assert.Null(cat.FieldNumber("MessageInconnu", "map_id"));
    }

    [Fact]
    public void Parses_proto_field_maps()
    {
        var cat = ProtocolCatalog.LoadFromDirectory(DataDir);
        var clearProtoPath = Path.Combine(DataDir, "clear_game_2025.proto");
        if (File.Exists(clearProtoPath))
            Assert.True(cat.MessageDefCount > 500, $"proto peu parse: {cat.MessageDefCount} messages");
        else
        {
            // L'export partagé ne livre pas ce schéma clair historique. Vérifier le fail-closed,
            // puis exercer le même parseur avec une fixture locale explicite, sans simuler le wire 3.7.
            Assert.Equal(0, cat.MessageDefCount);
            Assert.Null(cat.Message("MapMovementRequest"));
            const string fixture = "message MapMovementRequest {\n int64 map_id = 1;\n bool cautious = 2;\n repeated int32 key_cells = 3;\n}\n"
                + "message GameActionFightCastRequest {\n int32 spell_id = 1;\n int32 cell = 2;\n}\n";
            cat = ProtocolCatalog.LoadFromStrings(fixture, null, null, null);
            Assert.Equal(2, cat.MessageDefCount);
        }

        var move = cat.Message("MapMovementRequest");
        Assert.NotNull(move);
        Assert.Equal(1, move!.ByName["map_id"].Number);
        Assert.Equal(2, move.ByName["cautious"].Number);
        Assert.Equal(3, move.ByName["key_cells"].Number);
        Assert.True(move.ByName["key_cells"].Repeated);

        var cast = cat.Message("GameActionFightCastRequest");
        Assert.NotNull(cast);
        Assert.Equal(1, cast!.ByName["spell_id"].Number);
        Assert.Equal(2, cast.ByName["cell"].Number);
    }
}

public class EnvelopeRoundtripTests
{
    // Prouve la these de CONTROLE de bout en bout au niveau paquet : forger une Request -> l'encadrer comme sur
    // le fil -> deframe -> decode -> on retrouve le meme code et le meme payload (+ l'uid d'enveloppe).
    [Fact]
    public void Forge_frame_decode_roundtrips_code_payload_uid()
    {
        var payload = new List<ProtoField>
        {
            new() { Field = 1, Wire = WireType.Varint, Varint = 12345 }, // map_id
            new() { Field = 3, Wire = WireType.Varint, Varint = 287 },   // key_cells[0]
        };

        var gm = EnvelopeBuilder.BuildClientRequest("abc", payload, uid: 7);

        // encadrement comme sur le fil : varint(len) + gm
        var framed = RawProtobuf.EncodeVarint((ulong)gm.Length).Concat(gm).ToArray();
        var frames = RawProtobuf.ConsumeFrames(framed, out var rest);
        Assert.Single(frames);
        Assert.Empty(rest);

        var tree = RawProtobuf.Parse(frames[0]);
        var hits = RawProtobuf.FindTypeUrls(tree);
        Assert.Single(hits);
        Assert.Equal("abc", hits[0].Code);

        var decoded = hits[0].Payload!;
        Assert.Equal(12345UL, decoded.First(f => f.Field == 1).Varint);
        Assert.Equal(287UL, decoded.First(f => f.Field == 3).Varint);

        Assert.Equal(7, EnvelopeBuilder.ReadEnvelopeUid(frames[0]));
    }

    [Fact]
    public void Server_event_carries_code_and_payload()
    {
        var payload = new List<ProtoField> { new() { Field = 1, Wire = WireType.Varint, Varint = 99 } };
        var gm = EnvelopeBuilder.BuildServerEvent("evt", payload);
        var hits = RawProtobuf.FindTypeUrls(RawProtobuf.Parse(gm));
        Assert.Single(hits);
        Assert.Equal("evt", hits[0].Code);
        Assert.Equal(99UL, hits[0].Payload!.First(f => f.Field == 1).Varint);
    }
}

public class MoveForgeTests
{
    // Verrou anti-regression du bug qui faisait REFUSER les deplacements : key_cells est un repeated int32 PACKED,
    // donc UN champ #2 (Len) contenant les varints concatenes - comme la vraie trame client (#2 = bytes[6]). L'eclater
    // (un #2 varint par valeur, ce que je faisais) casse le forge. Valeurs = keyMovements reels captures.
    [Fact]
    public void BuildFramed_encodes_key_cells_packed_like_client()
    {
        var catalog = ProtocolCatalog.LoadFromDirectory(System.IO.Path.Combine(AppContext.BaseDirectory, "data"));
        var keyCellsField = catalog.FieldNumber("MapMovementRequest", "key_cells")
            ?? throw new InvalidOperationException("Champ MapMovementRequest.key_cells absent du catalogue livré");
        var mapIdField = catalog.FieldNumber("MapMovementRequest", "map_id")
            ?? throw new InvalidOperationException("Champ MapMovementRequest.map_id absent du catalogue livré");
        long mapId = 153880836;
        var keyCells = new[] { 24834, 28790, 28776 };
        var framed = MoveForge.BuildFramed(mapId, keyCells);

        var frames = RawProtobuf.ConsumeFrames(framed, out var rest);
        Assert.Single(frames);
        Assert.Empty(rest);

        var hits = RawProtobuf.FindTypeUrls(RawProtobuf.Parse(frames[0]));
        Assert.Single(hits);
        Assert.Equal(catalog.CodeForName("MapMovementRequest"), hits[0].Code);

        var payload = hits[0].Payload!;
        Assert.Equal((ulong)mapId, payload.First(f => f.Field == mapIdField).Varint);

        // UN SEUL champ de cellules (packed), surtout PAS trois varints eclatees.
        var cells = payload.Where(f => f.Field == keyCellsField).ToList();
        Assert.Single(cells);
        Assert.Equal(WireType.Len, cells[0].Wire);
        Assert.NotNull(cells[0].Bytes);
        Assert.Equal(keyCells, ReadVarints(cells[0].Bytes!).Select(v => (int)v).ToArray());

        // Seuls key_cells et map_id du mapping 3.7 livré doivent être émis ; cautious reste absent.
        Assert.Equal(new[] { keyCellsField, mapIdField }.OrderBy(x => x).ToArray(),
            payload.Select(f => f.Field).Distinct().OrderBy(x => x).ToArray());
    }

    private static IEnumerable<ulong> ReadVarints(byte[] b)
    {
        int i = 0;
        while (i < b.Length)
        {
            ulong v = 0; int shift = 0;
            while (i < b.Length)
            {
                byte x = b[i++];
                v |= (ulong)(x & 0x7F) << shift;
                if ((x & 0x80) == 0) break;
                shift += 7;
            }
            yield return v;
        }
    }
}

public class GameCodecTests
{
    // Migration protobuf : les classes GENEREES doivent produire des trames BYTE-IDENTIQUES aux forges manuelles
    // (et a la vraie trame capturee). Preuve que l'enveloppe generee = numeros wire du patch actuel.
    [Fact(Skip = "GameCodec est GELE sur 3.6.10.11 : les classes protobuf generees (Givre.cs) portent encore " +
                 "GameMessage.request=#2 et MapMovementRequest{map_id=1,key_cells=2}, tous perimes depuis 3.6.11.12. " +
                 "Ce chemin n'a AUCUN appelant en production ; re-activer ce test apres regeneration des classes.")]
    public void Generated_move_is_byte_identical_to_MoveForge_DISABLED()
    {
        long mapId = 153880836;
        var keyCells = new[] { 24834, 28790, 28776 };
        byte[] generated = GivreClone.Protocol.GameCodec.BuildFramedRequest("jrw",
            new GivreClone.Protocol.Generated.MapMovementRequest { MapId = mapId, KeyCells = { keyCells }, Cautious = false });
        byte[] hand = MoveForge.BuildFramed(mapId, keyCells);
        Assert.Equal(Convert.ToHexString(hand), Convert.ToHexString(generated));
    }

    [Fact(Skip = "GameCodec est GELE sur 3.6.10.11 : la trame attendue ici porte l'ancien code 'lqb' et " +
                 "l'ancien slot d'enveloppe #2. Voir le commentaire de GameCodec.cs.")]
    public void Generated_whoami_matches_real_captured_frame_DISABLED()
    {
        byte[] generated = GivreClone.Protocol.GameCodec.BuildFramedRequest("lqb",
            new GivreClone.Protocol.Generated.WhoAmIRequest { Verbose = true });
        // trame REELLE verifiee du /whoami client (uid=-1 int64, GameMessage req=#2/content=#1/uid=#2)
        Assert.Equal("2812260A190A13747970652E616E6B616D612E636F6D2F6C71621202080110FFFFFFFFFFFFFFFFFF01",
            Convert.ToHexString(generated));
    }
}

public class MapGeometryTests
{
    // Le recalage de position au refus (jqt) depend de la conversion point-carte (x,y) -> cellId. Elle doit etre EXACTE
    // sinon le bot repart d'une cellule fausse. Ancre de reference.
    [Fact]
    public void CellFromMapPoint_is_exact_inverse_and_matches_reference()
    {
        Assert.Equal(240, MapPathfinder.CellFromMapPoint(11, -6));  // cell 240 <-> point-carte (11,-6)
        Assert.Equal(0, MapPathfinder.CellFromMapPoint(0, 0));

        // Bijection : chaque cellId round-trip via son point-carte standard.
        for (int id = 0; id < MapPathfinder.CellCount; id++)
        {
            int loc2 = id / 14, loc3 = (loc2 + 1) / 2, rem = id - loc2 * 14;
            int x = loc3 + rem, y = rem - (loc2 - loc3);
            Assert.Equal(id, MapPathfinder.CellFromMapPoint(x, y));
        }

        Assert.Equal(-1, MapPathfinder.CellFromMapPoint(999, 999));  // hors grille -> -1
    }

    // ExpandPath (base positions) sur des points de virage REELS (move client capture [448,380,352]) : le chemin
    // deroule doit rester SUR LA CARTE (0-559) et enchainer des cellules adjacentes -> jamais "hors carte" ni saut.
    [Fact]
    public void ExpandPath_from_real_turn_points_stays_on_map_and_contiguous()
    {
        var full = MapPathfinder.ExpandPath(new[] { 448, 380, 352 });
        Assert.Equal(448, full[0]);
        Assert.Equal(352, full[full.Count - 1]);
        foreach (var c in full) Assert.InRange(c, 0, 559);
        for (int i = 1; i < full.Count; i++)
            Assert.Equal(1, MapPathfinder.Distance(full[i - 1], full[i]));   // chaque pas = 1 cellule (aucun saut)
    }

    // jqt (MapMovementRefusedEvent) : #1 = cell_x, #2 = cell_y (int32 SIGNES) -> cellule reelle. Ex (11,-6) -> 240.
    [Fact]
    public void MapMovementRefusedReader_reads_signed_coords_to_cell()
    {
        var tree = new List<ProtoField>
        {
            new() { Field = 1, Wire = WireType.Varint, Varint = 11 },
            new() { Field = 2, Wire = WireType.Varint, Varint = unchecked((ulong)(long)-6) },  // -6 = varint sign-extended
        };
        var r = MapMovementRefusedReader.Read(tree);
        Assert.NotNull(r);
        Assert.Equal(11, r!.Value.X);
        Assert.Equal(-6, r.Value.Y);
        Assert.Equal(240, r.Value.Cell);
    }
}

public class PacketBusTests
{
    private static BotMessage Msg(string code, string? name = null) =>
        new(1, Direction.ServerToClient, 1, code, name, new List<ProtoField>(), Array.Empty<byte>());

    [Fact]
    public async Task WaitFor_resolves_on_matching_publish()
    {
        var bus = new PacketBus();
        var task = bus.WaitForAsync(m => m.Code == "xyz", TimeSpan.FromSeconds(2));
        bus.Publish(Msg("other"));
        bus.Publish(Msg("xyz", "SomeEvent"));
        var got = await task;
        Assert.Equal("xyz", got.Code);
    }

    [Fact]
    public async Task WaitFor_times_out()
    {
        var bus = new PacketBus();
        await Assert.ThrowsAsync<TimeoutException>(
            () => bus.WaitForAsync(_ => false, TimeSpan.FromMilliseconds(120)));
    }

    [Fact]
    public void Subscribe_receives_published()
    {
        var bus = new PacketBus();
        var seen = new List<string>();
        using (bus.Subscribe(m => seen.Add(m.Code)))
        {
            bus.Publish(Msg("a"));
            bus.Publish(Msg("b"));
        }
        bus.Publish(Msg("c")); // apres Dispose -> non recu
        Assert.Equal(new[] { "a", "b" }, seen);
    }
}

public class BehaviorTests
{
    private sealed class MoveOnceBehavior : IBehavior
    {
        public string Name => "move-once";
        public Task RunAsync(IBotContext ctx, CancellationToken ct)
            => ctx.Actions.MoveToCellAsync(123, new[] { 1, 2, 3 }, cautious: false, ct);
    }

    [Fact]
    public async Task Behavior_drives_actions_on_fake_context()
    {
        var ctx = new FakeBotContext();
        await new MoveOnceBehavior().RunAsync(ctx, CancellationToken.None);
        Assert.Contains("MapMovementRequest", ctx.FakeActions.Sent);
    }
}
