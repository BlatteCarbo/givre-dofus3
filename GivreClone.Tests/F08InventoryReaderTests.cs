using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

// Le catalogue d'InventoryReader est statique : ces tests de schéma manquant s'exécutent seuls.
[CollectionDefinition("F08InventoryMapping", DisableParallelization = true)]
public sealed class F08InventoryMappingCollection { }

[Collection("F08InventoryMapping")]
public sealed class F08InventoryReaderTests
{
    private static readonly ProtocolCatalog Mapping = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static ProtoField V(int field, ulong value) => new() { Field = field, Wire = WireType.Varint, Varint = value };

    [Fact]
    public void Buyer_exemplar_effects_are_read_even_when_raw_decoder_guessed_text()
    {
        try
        {
            InventoryReader.UseCatalog(Mapping.FieldNumber);
            int field = Mapping.FieldNumber("BidExchangerObject", "effects")!.Value;
            var raw = new ProtoField { Field = field, Wire = WireType.Len, Bytes = new byte[] {0x28, 65, 0x50, 125} };
            var effect = Assert.Single(InventoryReader.ReadEffects(new[] {raw}, "BidExchangerObject"));
            Assert.Equal(125, effect.ActionId); Assert.Equal(65, effect.Value);
        }
        finally { InventoryReader.UseCatalog(Mapping.FieldNumber); }
    }

    [Fact]
    public void Missing_quantity_mapping_never_becomes_a_zero_quantity_receipt()
    {
        int outer = Mapping.FieldNumber("ObjectQuantityEvent", "object")!.Value;
        int uid = Mapping.FieldNumber("ObjectUidWithQuantity", "object_uid")!.Value;
        var payload = new[] { new ProtoField { Field = outer, Wire = WireType.Len, Message = new List<ProtoField> { V(uid, 10) } } };
        try
        {
            InventoryReader.UseCatalog((message, field) => (message, field) == ("ObjectUidWithQuantity", "quantity") ? null : Mapping.FieldNumber(message, field));
            Assert.Null(InventoryReader.ReadQuantity(payload));
        }
        finally { InventoryReader.UseCatalog(Mapping.FieldNumber); }
        Assert.Equal((10, 0), InventoryReader.ReadQuantity(payload)); // zéro réellement omis : valide avec schéma connu
    }

    [Fact]
    public void Missing_position_mapping_never_confirms_an_amulet()
    {
        var payload = new[] { V(Mapping.FieldNumber("ObjectMovementEvent", "object_uid")!.Value, 10) };
        try
        {
            InventoryReader.UseCatalog((message, field) => (message, field) == ("ObjectMovementEvent", "position") ? null : Mapping.FieldNumber(message, field));
            Assert.Null(InventoryReader.ReadMovement(payload));
        }
        finally { InventoryReader.UseCatalog(Mapping.FieldNumber); }
        Assert.Equal((10, 0), InventoryReader.ReadMovement(payload));
    }

    [Fact]
    public void Invalid_server_quantity_cannot_wrap_and_delete_a_valid_uid()
    {
        int outer = Mapping.FieldNumber("ObjectQuantityEvent", "object")!.Value;
        int uid = Mapping.FieldNumber("ObjectUidWithQuantity", "object_uid")!.Value;
        int quantity = Mapping.FieldNumber("ObjectUidWithQuantity", "quantity")!.Value;
        var payload = new[] { new ProtoField { Field = outer, Wire = WireType.Len,
            Message = new List<ProtoField> { V(uid, 10), V(quantity, ulong.MaxValue) } } };
        Assert.Null(InventoryReader.ReadQuantity(payload));
        Assert.Null(InventoryReader.ReadMovement(new[] { V(Mapping.FieldNumber("ObjectMovementEvent", "object_uid")!.Value, (ulong)uint.MaxValue + 11) }));
    }
}
