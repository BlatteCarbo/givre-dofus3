using GivreClone.App.Services;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Craft + inventaire (« Aucune Potion de Souvenir obtenue » alors que le craft avait réussi) :
///  - le résultat de craft est lu PAR NOM (code SUCCESS + objet produit), rejeu de la trame capturée ;
///  - les événements du sac EN LOT (dépôt banque par liste) sont lus et appliqués au modèle, sinon getInventoryItemCount
///    comptait encore les objets déposés et un script « avant / après craft » concluait à tort « rien obtenu ».
/// Lecteurs branchés sur le VRAI catalogue (TestSetup) : ces rejeux valident le chemin fields.json, pas un repli littéral.
/// </summary>
public class CraftInventoryTests
{
    // --- encodeur protobuf minimal (tag + valeur), pour rejouer une trame octet pour octet ---------
    private static byte[] Cat(params byte[][] parts)
    {
        var l = new List<byte>();
        foreach (var p in parts) l.AddRange(p);
        return l.ToArray();
    }

    private static byte[] Var(ulong v)
    {
        var l = new List<byte>();
        do { byte c = (byte)(v & 0x7F); v >>= 7; if (v != 0) c |= 0x80; l.Add(c); } while (v != 0);
        return l.ToArray();
    }

    private static byte[] Tag(int field, int wire) => Var((ulong)((field << 3) | wire));
    private static byte[] I(int field, long v) => Cat(Tag(field, 0), Var(unchecked((ulong)v)));
    private static byte[] M(int field, params byte[][] parts)
    {
        var b = Cat(parts);
        return Cat(Tag(field, 2), Var((ulong)b.Length), b);
    }
    private static byte[] Packed(int field, params long[] values)
        => M(field, values.Select(v => Var(unchecked((ulong)v))).ToArray());

    private static IReadOnlyList<ProtoField> Parse(params byte[][] parts) => RawProtobuf.Parse(Cat(parts));

    private static ProtocolCatalog Catalog() => ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static int F(ProtocolCatalog cat, string msg, string field)
        => cat.FieldNumber(msg, field) ?? throw new Xunit.Sdk.XunitException($"{msg}.{field} absent du mapping");

    private const uint Pid = 1;
    private const long Potion = 7652;   // Potion de Souvenir

    private static IReadOnlyList<ProtoField> ProducedPotionReceipt(int quantity)
    {
        var cat = Catalog();
        return Parse(
            I(F(cat, "ExchangeCraftResultEvent", "result"), InventoryReader.CraftSuccess),
            M(F(cat, "ExchangeCraftResultEvent", "object"),
                M(F(cat, "ExchangeCraftResultWithObjectDescription", "object"),
                    I(F(cat, "ObjectItem", "quantity"), quantity),
                    I(F(cat, "ObjectItem", "gid"), Potion))));
    }

    // =============================================================== résultat de craft
    [Fact]
    public void Craft_result_capture_is_success_with_produced_item()
    {
        // Rejoue la sémantique du reçu de craft avec la numérotation du catalogue livré.
        var r = InventoryReader.ReadCraftResult(ProducedPotionReceipt(100));
        Assert.Equal(InventoryReader.CraftSuccess, r.Code);
        Assert.Equal(Potion, r.Gid);
        Assert.Equal(100, r.Qty);
        Assert.True(r.Made);
    }

    [Fact]
    public void Craft_result_empty_is_impossible_not_made()
    {
        // Résultat VIDE = recette invalide (proto3 omet IMPOSSIBLE = 0) : l'ancien test « paquet non vide » le rejetait déjà.
        var r = InventoryReader.ReadCraftResult(Parse());
        Assert.Equal(InventoryReader.CraftImpossible, r.Code);
        Assert.False(r.Made);
        Assert.Equal(0, r.Gid);
    }

    [Fact]
    public void Craft_result_failed_or_forbidden_is_not_made_even_if_non_empty()
    {
        // L'ancien test « paquet non vide » les prenait pour des réussites.
        var cat = Catalog();
        int res = F(cat, "ExchangeCraftResultEvent", "result");
        Assert.False(InventoryReader.ReadCraftResult(Parse(I(res, InventoryReader.CraftFailed))).Made);
        Assert.False(InventoryReader.ReadCraftResult(Parse(I(res, InventoryReader.CraftForbidden))).Made);
        Assert.False(InventoryReader.ReadCraftResult(Parse(I(res, InventoryReader.CraftNeutral))).Made);
    }

    [Fact]
    public void Craft_result_gid_only_branch_is_read()
    {
        // Branche « object_gid » du oneof (pas de description d'objet) : gid lu, quantité inconnue (0).
        var cat = Catalog();
        var r = InventoryReader.ReadCraftResult(Parse(
            I(F(cat, "ExchangeCraftResultEvent", "result"), InventoryReader.CraftSuccess),
            I(F(cat, "ExchangeCraftResultEvent", "object_gid"), Potion)));
        Assert.True(r.Made);
        Assert.Equal(Potion, r.Gid);
        Assert.Equal(0, r.Qty);
    }

    // =============================================================== événements du sac EN LOT
    [Fact]
    public void Objects_deleted_reads_packed_and_unpacked_uids()
    {
        var cat = Catalog();
        int f = F(cat, "ObjectsDeletedEvent", "objects_uid");
        Assert.Equal(new[] { 18299710, 18299711, 5 }, InventoryReader.ReadDeletedUids(Parse(Packed(f, 18299710, 18299711, 5))));
        Assert.Equal(new[] { 18299710, 5 }, InventoryReader.ReadDeletedUids(Parse(I(f, 18299710), I(f, 5))));
    }

    [Fact]
    public void Objects_quantity_reads_each_entry_and_absent_quantity_is_zero()
    {
        var cat = Catalog();
        int obj = F(cat, "ObjectsQuantityEvent", "object");
        int uid = F(cat, "ObjectUidWithQuantity", "object_uid"), qty = F(cat, "ObjectUidWithQuantity", "quantity");
        var list = InventoryReader.ReadQuantities(Parse(
            M(obj, I(uid, 18299710), I(qty, 37)),
            M(obj, I(uid, 18299711))));   // quantité omise (proto3) = 0 = pile vidée
        Assert.Equal(new[] { (18299710, 37), (18299711, 0) }, list);
    }

    [Fact]
    public void Objects_added_reads_inventory_items()
    {
        var cat = Catalog();
        var list = InventoryReader.ReadAddedList(Parse(M(F(cat, "ObjectsAddedEvent", "objects"),
            I(F(cat, "ObjectItemInventory", "position"), 63),
            M(F(cat, "ObjectItemInventory", "item"),
                I(F(cat, "ObjectItem", "uid"), 18299800), I(F(cat, "ObjectItem", "quantity"), 100), I(F(cat, "ObjectItem", "gid"), Potion)))));
        var it = Assert.Single(list);
        Assert.Equal(18299800, it.Uid);
        Assert.Equal(Potion, it.Gid);
        Assert.Equal(100, it.Qty);
        Assert.Equal(63, it.Position);
    }

    // =============================================================== scénario du user
    [Fact]
    public void Bank_deposit_by_list_then_craft_keeps_the_bag_count_right()
    {
        // Sac : 100 potions (1 pile) + 50 orties dont 30 déposées. Dépôt banque PAR LISTE -> événements du sac EN LOT.
        var cat = Catalog();
        var inv = new InventoryStore();
        inv.Seed(Pid, new[]
        {
            new InventoryStore.Item(18299710, Potion, 100),
            new InventoryStore.Item(18299711, 421, 50),
        });

        int delF = F(cat, "ObjectsDeletedEvent", "objects_uid");
        foreach (var u in InventoryReader.ReadDeletedUids(Parse(Packed(delF, 18299710)))) inv.Remove(Pid, u);
        int obj = F(cat, "ObjectsQuantityEvent", "object");
        int uid = F(cat, "ObjectUidWithQuantity", "object_uid"), qty = F(cat, "ObjectUidWithQuantity", "quantity");
        foreach (var q in InventoryReader.ReadQuantities(Parse(M(obj, I(uid, 18299711), I(qty, 20))))) inv.ApplyQuantity(Pid, q.Uid, q.Qty);

        // « avant » lu par le script : 0 potion (l'ancien modèle disait encore 100 -> « après <= avant » -> arrêt).
        int before = inv.CountByGid(Pid, Potion);
        Assert.Equal(0, before);
        Assert.Equal(20, inv.CountByGid(Pid, 421));

        // Le craft produit 100 potions, reçues par le protocole de la version livrée.
        var r = InventoryReader.ReadCraftResult(ProducedPotionReceipt(100));
        Assert.True(r.Made);
        Assert.Equal(Potion, r.Gid);
        Assert.Equal(100, r.Qty);
        inv.Upsert(Pid, new InventoryStore.Item(18299900, r.Gid, r.Qty));
        Assert.Equal(before + r.Qty, inv.CountByGid(Pid, Potion));
    }

    [Fact]
    public void Quantity_zero_removes_the_stack()
    {
        var inv = new InventoryStore();
        inv.Seed(Pid, new[] { new InventoryStore.Item(7, Potion, 3) });
        inv.ApplyQuantity(Pid, 7, 0);
        Assert.Equal(0, inv.StackCount(Pid));
        Assert.Empty(inv.InventoryUids(Pid));   // plus dans la liste d'un prochain dépôt banque
    }
}
