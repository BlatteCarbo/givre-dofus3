using System.Collections.Generic;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class EntityLookReaderTests
{
    [Fact]
    public void Map_appearance_reads_only_the_known_self_actor()
    {
        var catalog = ProtocolCatalog.LoadFromDirectory(System.IO.Path.Combine(System.AppContext.BaseDirectory, "data"));
        var payload = new[] { M(3, V(1, 33202504034), M(3, M(2, V(1, 639), V(4, 3)))) };
        Assert.Equal(639, EntityLookReader.ReadMapSelf(payload, 3, 33202504034, catalog.FieldNumber)!.BonesId);
        Assert.Null(EntityLookReader.ReadMapSelf(payload, 3, 46848999778, catalog.FieldNumber));
        Assert.Null(EntityLookReader.ReadMapSelf(payload, 3, 0, catalog.FieldNumber));
        Assert.Null(EntityLookReader.ReadMapSelf(payload, 3, 33202504034, (_, _) => null));
    }

    [Fact]
    public void Observed_mounted_self_look_preserves_colors_and_rider_from_wire()
    {
        var catalog = ProtocolCatalog.LoadFromDirectory(System.IO.Path.Combine(System.AppContext.BaseDirectory, "data"));
        var fields = new List<ProtoField>
        {
            V(1, 639), V(4, 3),
            new() { Field = 5, Wire = WireType.Len, Bytes = System.Convert.FromHexString("8281AC0AF0FFFF17F0FFFF1FC3C8D92394A090488080DC56") },
            new() { Field = 6, Wire = WireType.Len, Bytes = new byte[] { 120 } },
            M(2, V(4, 2), M(3, V(1, 2), V(4, 24),
                new ProtoField { Field = 5, Wire = WireType.Len, Bytes = System.Convert.FromHexString("AAE7D60F9BD1D517D3D7E21F9DD1CE27D2D5DE2FF3E5CF3794A090488080DC56") },
                new ProtoField { Field = 6, Wire = WireType.Len, Bytes = new byte[] { 53 } })),
        };
        var look = EntityLookReader.Parse(fields, catalog.FieldNumber)!;
        Assert.Equal(639, look.BonesId);
        Assert.Contains((1, 4915330), look.Colors);
        Assert.Contains((2, 16777200), look.Colors);
        Assert.Equal(120, Assert.Single(look.Scales));
        var rider = Assert.Single(look.SubEntities!);
        Assert.Equal(2, rider.BindingPointCategory);
        Assert.Equal(0, rider.BindingPointIndex);
        Assert.Contains((1, 16102314), rider.Look.Colors);
        Assert.Equal(53, Assert.Single(rider.Look.Scales));
    }

    // Numéros de champ de test (le vrai passe par le catalogue streamé).
    private static int? Fn(string msg, string field) => (msg, field) switch
    {
        ("EntityLook", "indexed_colors") => 1, ("EntityLook", "skins") => 4, ("EntityLook", "bones_id") => 5, ("EntityLook", "scales") => 6,
        ("EntityLook", "sub_entities") => 2,
        ("SubEntityInformation", "binding_point_category") => 4,
        ("SubEntityInformation", "binding_point_index") => 1,
        ("SubEntityInformation", "sub_entity_look") => 3,
        ("ContextRefreshEntityLookEvent", "entity_id") => 1, ("ContextRefreshEntityLookEvent", "look") => 2,
        ("CharacterListEvent", "characters") => 1, ("Character", "character_basic_information") => 2, ("Character", "id") => 3,
        ("CharacterBasicInformation", "character_look") => 5, ("CharacterLook", "look") => 6,
        _ => null,
    };

    private static ProtoField V(int f, ulong v) => new() { Field = f, Wire = WireType.Varint, Varint = v };
    private static ProtoField M(int f, params ProtoField[] inner) => new() { Field = f, Wire = WireType.Len, Message = new List<ProtoField>(inner) };

    private static byte[] Packed(params ulong[] vs)
    {
        var l = new List<byte>();
        foreach (var v0 in vs) { var v = v0; while (v >= 0x80) { l.Add((byte)(v | 0x80)); v >>= 7; } l.Add((byte)v); }
        return l.ToArray();
    }

    private static List<ProtoField> LookFields() => new()
    {
        V(5, 1),
        new ProtoField { Field = 4, Wire = WireType.Len, Bytes = Packed(1245, 4234) },   // skins en « packed »
        V(1, (1UL << 24) | 0xFF8800), V(1, (2UL << 24) | 0x112233),
        V(6, 100),
    };

    [Fact]
    public void Parse_builds_the_historic_look_string_from_packed_skins_and_indexed_colors()
    {
        var look = EntityLookReader.Parse(LookFields(), Fn)!;
        Assert.Equal(1, look.BonesId);
        Assert.Equal(new[] { 1245, 4234 }, look.Skins);
        Assert.Equal("{1|1245,4234|1=16746496,2=1122867|100}", look.ToLookString());
    }

    [Fact]
    public void Mounted_character_keeps_the_rider_in_the_renderer_look()
    {
        var fields = new List<ProtoField>
        {
            V(5, 69), V(6, 100),
            M(2, V(4, 2), V(1, 0), M(3, LookFields().ToArray())),
        };

        var look = EntityLookReader.Parse(fields, Fn)!;

        Assert.Equal("{69|||100|2@0={1|1245,4234|1=16746496,2=1122867|100}}", look.ToLookString());
    }

    [Fact]
    public void Parse_returns_null_without_bones_or_skins()
    {
        Assert.Null(EntityLookReader.Parse(new List<ProtoField> { V(6, 100) }, Fn));
    }

    [Fact]
    public void ReadRefresh_returns_entity_and_look()
    {
        var payload = new List<ProtoField> { V(1, 41234567890), M(2, LookFields().ToArray()) };
        var r = EntityLookReader.ReadRefresh(payload, Fn)!.Value;
        Assert.Equal(41234567890, r.EntityId);
        Assert.Equal(1, r.Look.BonesId);
    }

    [Fact]
    public void ReadCharacterList_maps_each_character_id_to_its_look()
    {
        var one = M(1, V(3, 111), M(2, M(5, M(6, LookFields().ToArray()))));
        var tree = new List<ProtoField> { one };
        var res = EntityLookReader.ReadCharacterList(tree, Fn);
        Assert.Equal(new[] { 111L }, res.Keys);
    }

    [Fact]
    public void Unknown_catalog_gives_nothing_instead_of_throwing()
    {
        Assert.Null(EntityLookReader.Parse(LookFields(), (_, _) => null));
        Assert.Empty(EntityLookReader.ReadCharacterList(new List<ProtoField>(), (_, _) => null));
    }
}
