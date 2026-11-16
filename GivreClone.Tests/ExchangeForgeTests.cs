using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Lot D (banque &amp; échanges) : forges et lecteurs vérifiés contre le VRAI catalogue livré (data/). Codes et numéros de
/// champ comparés à ceux du catalogue (jamais à des littéraux) -> un patch qui renumérote reste couvert par fields.json.
/// </summary>
public class ExchangeForgeTests
{
    private static ProtocolCatalog Cat() => ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));

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

    private static int F(ProtocolCatalog cat, string msg, string field)
        => cat.FieldNumber(msg, field) ?? throw new Xunit.Sdk.XunitException($"{msg}.{field} absent du mapping");

    private static IEnumerable<ulong> ReadVarints(byte[] b)
    {
        int pos = 0;
        while (pos < b.Length && RawProtobuf.TryReadVarint(b, ref pos, out var v)) yield return v;
    }

    [Fact]
    public void ObjectUseMultiple_carries_quantity_and_uid_only()
    {
        var cat = Cat();
        var p = Payload(MoveForge.BuildObjectUseMultipleFramed(18299710, 7), cat, "ObjectUseMultipleRequest");
        int qF = F(cat, "ObjectUseMultipleRequest", "quantity"), uF = F(cat, "ObjectUseMultipleRequest", "object_uid");
        Assert.Equal(new[] { qF, uF }.OrderBy(x => x), p.Select(f => f.Field).OrderBy(x => x));
        Assert.Equal(7UL, p.Single(f => f.Field == qF).Varint);
        Assert.Equal(18299710UL, p.Single(f => f.Field == uF).Varint);
        // Le 1er champ (nom obfusqué) est OMIS.
        if (cat.FieldNumber("ObjectUseMultipleRequest", "frpk") is int obf) Assert.DoesNotContain(p, f => f.Field == obf);
    }

    [Fact]
    public void ExchangeObjectMoveSigned_encodes_negative_quantity_as_int64()
    {
        var cat = Cat();
        var p = Payload(MoveForge.BuildExchangeObjectMoveSignedFramed(123, -5), cat, "ExchangeObjectMoveRequest");
        Assert.Equal(unchecked((ulong)(long)-5), p.Single(f => f.Field == F(cat, "ExchangeObjectMoveRequest", "quantity")).Varint);
        Assert.Equal(123UL, p.Single(f => f.Field == F(cat, "ExchangeObjectMoveRequest", "object_uid")).Varint);
    }

    [Fact]
    public void ExchangeMoveKama_has_only_signed_quantity()
    {
        var cat = Cat();
        var p = Payload(MoveForge.BuildExchangeMoveKamaFramed(-250), cat, "ExchangeMoveKamaRequest");
        var only = Assert.Single(p);
        Assert.Equal(F(cat, "ExchangeMoveKamaRequest", "quantity"), only.Field);
        Assert.Equal(unchecked((ulong)(long)-250), only.Varint);
    }

    [Fact]
    public void BankDepositList_packs_uids_in_one_field()
    {
        var cat = Cat();
        var p = Payload(MoveForge.BuildBankDepositListFramed(new[] { 1, 2, 300 }), cat, "ExchangeObjectTransferListFromInventoryRequest");
        int f = F(cat, "ExchangeObjectTransferListFromInventoryRequest", "objects_uid");
        var field = Assert.Single(p, x => x.Field == f);
        Assert.Equal(WireType.Len, field.Wire);
        Assert.Equal(new ulong[] { 1, 2, 300 }, ReadVarints(field.Bytes!).ToArray());
    }

    [Fact]
    public void ReadEntryAt_decodes_storage_object_update()
    {
        var cat = Cat();
        static ProtoField V(int f, ulong v) => new() { Field = f, Wire = WireType.Varint, Varint = v };
        static ProtoField M(int f, List<ProtoField> m) => new() { Field = f, Wire = WireType.Len, Message = m };
        var item = new List<ProtoField>
        {
            V(F(cat, "ObjectItem", "uid"), 202817400),
            V(F(cat, "ObjectItem", "quantity"), 42),
            V(F(cat, "ObjectItem", "gid"), 289),
        };
        var inv = new List<ProtoField> { V(F(cat, "ObjectItemInventory", "position"), 63), M(F(cat, "ObjectItemInventory", "item"), item) };
        var tree = new List<ProtoField> { M(F(cat, "StorageObjectUpdateEvent", "object"), inv) };

        var it = InventoryReader.ReadEntryAt(tree, F(cat, "StorageObjectUpdateEvent", "object"));
        Assert.NotNull(it);
        Assert.Equal(202817400, it!.Value.Uid);
        Assert.Equal(289L, it.Value.Gid);
        Assert.Equal(42, it.Value.Qty);
        Assert.Equal(63, it.Value.Position);
    }

    [Fact]
    public void ReadUids_reads_packed_and_unpacked()
    {
        var cat = Cat();
        int f = F(cat, "StorageObjectsRemovedEvent", "objects_uid");
        var packed = new List<byte>();
        foreach (var u in new[] { 5, 70000, 202817400 }) packed.AddRange(RawProtobuf.EncodeVarint((ulong)u));
        // Forme PACKÉE telle que le décodeur brut la produit (Bytes toujours rempli pour un Len).
        var parsed = RawProtobuf.Parse(RawProtobuf.Encode(new[] { new ProtoField { Field = f, Wire = WireType.Len, Bytes = packed.ToArray() } }));
        Assert.Equal(new[] { 5, 70000, 202817400 }, InventoryReader.ReadUids(parsed, f));

        var unpacked = new List<ProtoField>
        {
            new() { Field = f, Wire = WireType.Varint, Varint = 11 },
            new() { Field = f, Wire = WireType.Varint, Varint = 22 },
            new() { Field = f + 1, Wire = WireType.Varint, Varint = 99 },   // autre champ ignoré
        };
        Assert.Equal(new[] { 11, 22 }, InventoryReader.ReadUids(unpacked, f));
    }
}
