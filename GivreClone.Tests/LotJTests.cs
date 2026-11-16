using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Lot J (coffres &amp; maisons à code) : forges LockableUseCodeRequest / InteractiveUseRequest avec instance,
/// lecture des maisons du MapComplementaryInformationEvent et aides pures LockCode — tout contre le VRAI catalogue (data/),
/// numéros comparés à fields.json (jamais à des littéraux).</summary>
public class LotJTests
{
    private static ProtocolCatalog Cat() => ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));

    private static int F(ProtocolCatalog cat, string msg, string field)
        => cat.FieldNumber(msg, field) ?? throw new Xunit.Sdk.XunitException($"{msg}.{field} absent du mapping");

    private static IReadOnlyList<ProtoField> Payload(byte[] framed, ProtocolCatalog cat, string name)
    {
        Assert.NotEmpty(framed);
        var frames = RawProtobuf.ConsumeFrames(framed, out var rest);
        Assert.Single(frames);
        Assert.Empty(rest);
        var hits = RawProtobuf.FindTypeUrls(RawProtobuf.Parse(frames[0]));
        Assert.Single(hits);
        Assert.Equal(cat.CodeForName(name), hits[0].Code);
        return hits[0].Payload ?? new List<ProtoField>();
    }

    private static ProtoField V(int f, ulong v) => new() { Field = f, Wire = WireType.Varint, Varint = v };
    private static ProtoField M(int f, params ProtoField[] sub) => new() { Field = f, Wire = WireType.Len, Message = sub.ToList() };
    private static ProtoField S(int f, string s) => new() { Field = f, Wire = WireType.Len, String = s };

    [Fact]
    public void LockableUseCode_carries_code_only()
    {
        var cat = Cat();
        Assert.NotNull(cat.CodeForName("LockableUseCodeRequest"));
        var p = Payload(MoveForge.BuildLockableUseCodeFramed("1234"), cat, "LockableUseCodeRequest");
        int codeF = F(cat, "LockableUseCodeRequest", "code");
        var only = Assert.Single(p);
        Assert.Equal(codeF, only.Field);
        Assert.Equal(WireType.Len, only.Wire);
        Assert.Equal("1234", PayloadReader.Str(p, codeF));
        // Le 1er champ (nom obfusqué) est OMIS.
        if (cat.FieldNumber("LockableUseCodeRequest", "fxsy") is int obf) Assert.DoesNotContain(p, f => f.Field == obf);
    }

    [Fact]
    public void InteractiveUse_with_instance_and_identity_without()
    {
        var cat = Cat();
        Assert.NotNull(cat.CodeForName("InteractiveUseRequest"));
        var p = Payload(MoveForge.BuildInteractiveUseFramed(7, 513989, 42), cat, "InteractiveUseRequest");
        int eF = F(cat, "InteractiveUseRequest", "element_id"), iF = F(cat, "InteractiveUseRequest", "specific_instance_id");
        int sF = F(cat, "InteractiveUseRequest", "skill_instance_uid");
        Assert.Equal(new[] { eF, iF, sF }.OrderBy(x => x), p.Select(f => f.Field).OrderBy(x => x));
        Assert.Equal(513989UL, p.Single(f => f.Field == eF).Varint);
        Assert.Equal(42UL, p.Single(f => f.Field == iF).Varint);
        Assert.Equal(7UL, p.Single(f => f.Field == sF).Varint);
        // Instance 0 -> OMISE -> octets identiques à la surcharge historique à 2 arguments.
        Assert.Equal(MoveForge.BuildInteractiveUseFramed(7, 513989), MoveForge.BuildInteractiveUseFramed(7, 513989, 0));
    }

    private static List<ProtoField> JpoWithHouse(ProtocolCatalog cat, ProtoField doorsField, bool inHouse)
    {
        var inst = M(F(cat, "HouseOnMap", "houses_information"),
            V(F(cat, "HouseInstance", "instance_id"), 12),
            V(F(cat, "HouseInstance", "is_locked"), 1),
            M(F(cat, "HouseInstance", "account_tag"),
                S(F(cat, "AccountTag", "tag"), "1234"),
                S(F(cat, "AccountTag", "nickname"), "Bob")));
        var onMap = M(F(cat, "House", "house_on_map"), inst, doorsField);
        var house = M(F(cat, "MapComplementaryInformationEvent", "houses"), onMap, V(F(cat, "House", "house_id"), 777));
        var tree = new List<ProtoField> { V(F(cat, "MapComplementaryInformationEvent", "map_id"), 191104002), house };
        // Une maison SANS variante house_on_map (ex. maison de compte) est ignorée.
        tree.Add(M(F(cat, "MapComplementaryInformationEvent", "houses"), V(F(cat, "House", "house_id"), 9)));
        if (inHouse) tree.Add(new ProtoField { Field = F(cat, "MapComplementaryInformationEvent", "in_house_information"), Wire = WireType.Len, Bytes = Array.Empty<byte>() });
        return tree;
    }

    [Fact]
    public void HouseReader_reads_houses_packed_and_unpacked_doors()
    {
        var cat = Cat();
        int doorsF = F(cat, "HouseOnMap", "doors_on_map");
        byte[] packed = RawProtobuf.EncodeVarint(513001).Concat(RawProtobuf.EncodeVarint(513002)).ToArray();
        var packedField = new ProtoField { Field = doorsF, Wire = WireType.Len, Bytes = packed };

        var tree = JpoWithHouse(cat, packedField, inHouse: false);
        // Round-trip wire -> décodeur brut : vérifie aussi le cas « Bytes toujours conservé ».
        var reparsed = RawProtobuf.Parse(RawProtobuf.Encode(tree));
        foreach (var t in new[] { (IReadOnlyList<ProtoField>)tree, reparsed })
        {
            var h = Assert.Single(HouseReader.Extract(t));
            Assert.Equal(777, h.HouseId);
            Assert.Equal(new[] { 513001, 513002 }, h.DoorElementIds);
            var i = Assert.Single(h.Instances);
            Assert.Equal(12, i.InstanceId);
            Assert.Equal("Bob", i.Nickname);
            Assert.Equal("1234", i.Tag);
            Assert.True(i.IsLocked);
            Assert.False(HouseReader.IsInHouse(t));
        }

        // Une seule porte en varint NON packé.
        var h2 = Assert.Single(HouseReader.Extract(JpoWithHouse(cat, V(doorsF, 513003), inHouse: true)));
        Assert.Equal(new[] { 513003 }, h2.DoorElementIds);

        // in_house_information présent (corps vide) -> dans une maison ; aussi après un aller-retour wire.
        var inside = JpoWithHouse(cat, packedField, inHouse: true);
        Assert.True(HouseReader.IsInHouse(inside));
        Assert.True(HouseReader.IsInHouse(RawProtobuf.Parse(RawProtobuf.Encode(inside))));
    }

    [Fact]
    public void LockCode_helpers()
    {
        Assert.True(LockCode.TryNormalize("-1", out var c0)); Assert.Null(c0);
        Assert.True(LockCode.TryNormalize("", out var c1)); Assert.Null(c1);
        Assert.True(LockCode.TryNormalize(null, out var c2)); Assert.Null(c2);
        Assert.True(LockCode.TryNormalize(" 12 ", out var c3)); Assert.Equal("12", c3);
        Assert.False(LockCode.TryNormalize("123456789", out _));
        Assert.False(LockCode.TryNormalize("12_", out _));
        Assert.False(LockCode.TryNormalize("1 2", out _));

        Assert.Equal(new[] { "1234", "1234____" }, LockCode.Variants("1234", 8, false));
        Assert.Equal(new[] { "1234____", "1234" }, LockCode.Variants("1234", 8, true));
        Assert.Equal(new[] { "12345678" }, LockCode.Variants("12345678", 8, false));
        Assert.Equal(new[] { "1234", "1234____" }, LockCode.Variants("1234", 0, false));   // code_size omis -> 8

        var bob = new HouseInstanceInfo(12, "Bob", "1234", true);
        Assert.True(LockCode.OwnerMatches(bob, "bob"));
        Assert.True(LockCode.OwnerMatches(bob, "Bob#1234"));
        Assert.False(LockCode.OwnerMatches(bob, "Bob#9999"));
        Assert.False(LockCode.OwnerMatches(bob, "Alice"));
        Assert.False(LockCode.OwnerMatches(bob, ""));

        Assert.True(LockCode.TrySplitSpec("300;1234", out var p1)); Assert.Equal(new[] { "300", "1234" }, p1);
        Assert.True(LockCode.TrySplitSpec("300|1234|Bob#1", out var p2)); Assert.Equal(3, p2.Length);
        Assert.False(LockCode.TrySplitSpec("300", out _));
    }

    [Fact]
    public void Lock_messages_resolve_by_name()
    {
        var cat = Cat();
        foreach (var n in new[] { "LockableCodeResultEvent", "LockableShowCodeDialogEvent", "LockableStateUpdateHouseDoorEvent",
                                  "InteractiveUseErrorEvent", "ExchangeStartedWithStorageEvent", "ExchangeStartedWithMultiTabStorageEvent" })
            Assert.NotNull(cat.CodeForName(n));
        F(cat, "LockableCodeResultEvent", "result");
        F(cat, "LockableShowCodeDialogEvent", "code_size");
        F(cat, "LockableShowCodeDialogEvent", "action");
        F(cat, "InteractiveUseErrorEvent", "element_id");
    }
}
