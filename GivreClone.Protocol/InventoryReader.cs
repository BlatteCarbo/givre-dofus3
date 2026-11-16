using System.Collections.Generic;
using System.Linq;
using GivreCore.Protobuf;

namespace GivreClone.Protocol;

/// <summary>
/// Lecture des events d'INVENTAIRE. Numeros de champ SHUFFLE par Ankama, CONFIRMES par capture live (2026-08-20) :
///   InventoryContentEvent (ivx) : kamas #1 ; objets = REPETE #3 (ObjectItemInventory).
///   ObjectItemInventory        : position #1 ; item #5 (ObjectItem).
///   ObjectItem                 : gid #1 ; effects #2 (repete, ignore) ; quantity #3 ; uid #4.
///   ObjectAddedEvent (iua)     : un ObjectItemInventory au champ #3 (meme forme qu'un item du snapshot).
///   ObjectQuantityEvent (ivj)  : #3 { object_uid #2 ; quantity #3 }.
///   ObjectDeletedEvent (ium)   : object_uid #1.
///   Versions EN LOT (dépôt/retrait banque par liste) : ObjectsDeletedEvent / ObjectsQuantityEvent / ObjectsAddedEvent.
///   ⚠ Numéros ci-dessus = historique : la vérité est fields.json (lecture PAR NOM, numéros en repli seulement).
/// Verifie en jeu : Ble gid 289 qty 528 uid 202817400 ; kamas 177242. Numeros propres au patch (re-deriver si MAJ).
/// </summary>
public static class InventoryReader
{
    private static System.Func<string,string,int?>? _fieldNumber;
    public static void UseCatalog(System.Func<string,string,int?> fieldNumber) => _fieldNumber = fieldNumber;
    private static int F(string message, string field) => _fieldNumber?.Invoke(message, field) ?? FieldMiss.Of(message, field);   // mapping streamé seul

    public readonly record struct Item(int Uid, long Gid, int Qty, int Position);

    /// <summary>Snapshot complet (ivx) : tous les ObjectItemInventory (#3) -&gt; (uid, gid, qty, position).</summary>
    public static IReadOnlyList<Item> ReadContent(IReadOnlyList<ProtoField> tree)
    {
        // 3.6.11.12 : les objets sont repetes au champ #2 (avant : #3). Capture 2026-09-08 : isb = { kamas #1,
        // objets #2 x55 }. Le bot annoncait "0 stacks" parce qu il balayait toujours #3, vide depuis le patch.
        var v = ReadContentAt(tree, F("InventoryContentEvent", "objects"));
        return v;
    }

    /// <summary>Contenu au champ répété `objectsField` (ivx=#3 ; StorageInventoryContentEvent iwb=#1).</summary>
    public static IReadOnlyList<Item> ReadContentAt(IReadOnlyList<ProtoField> tree, int objectsField)
    {
        var list = new List<Item>();
        foreach (var f in tree)
            if (f.Field == objectsField && f.Message is { } inv && ReadEntry(inv) is { } it)
                list.Add(it);
        return list;
    }

    /// <summary>Un ObjectItemInventory ajoute (iua = ObjectAddedEvent), au champ #3. null si illisible.</summary>
    public static Item? ReadAdded(IReadOnlyList<ProtoField> tree)
        => Msg(tree, F("ObjectAddedEvent", "object")) is { } inv ? ReadEntry(inv) : null;

    /// <summary>ObjectQuantityEvent : (uid, qty) du sous-message ObjectUidWithQuantity (champ `object`). null si absent.
    /// Quantité ABSENTE = 0 (proto3), pas « inconnue ».</summary>
    public static (int Uid, int Qty)? ReadQuantity(IReadOnlyList<ProtoField> tree)
        => Msg(tree, F("ObjectQuantityEvent", "object")) is { } obj ? ReadUidWithQuantity(obj) : null;

    /// <summary>ObjectsQuantityEvent (version EN LOT, ex. dépôt banque par liste) : un (uid, qty) par ObjectUidWithQuantity
    /// répété au champ `object`. Quantité absente = 0 (proto3) -> la pile est vide.</summary>
    public static IReadOnlyList<(int Uid, int Qty)> ReadQuantities(IReadOnlyList<ProtoField> tree)
    {
        var list = new List<(int Uid, int Qty)>();
        int field = F("ObjectsQuantityEvent", "object");
        foreach (var f in tree)
            if (f.Field == field && f.Message is { } obj && ReadUidWithQuantity(obj) is { } q)
                list.Add(q);
        return list;
    }

    // ObjectUidWithQuantity lu PAR NOM uniquement (fields.json : quantity / object_uid). L'ancienne heuristique de grandeur
    // sur #2/#3 (« le plus grand est l'uid ») est retirée le 03/10 : elle masquait une renumérotation au lieu d'échouer.
    private static (int Uid, int Qty)? ReadUidWithQuantity(IReadOnlyList<ProtoField> obj)
    {
        int uidField = F("ObjectUidWithQuantity", "object_uid"), qtyField = F("ObjectUidWithQuantity", "quantity");
        // Champ non mappé != zéro proto3 : ne jamais vider une pile faute de schéma.
        if (uidField <= 0 || qtyField <= 0 || Var(obj, uidField) is not { } uid || uid == 0 || uid > int.MaxValue) return null;
        ulong qty = Var(obj, qtyField) ?? 0;
        return qty <= int.MaxValue ? ((int)uid, (int)qty) : null;
    }

    /// <summary>ObjectDeletedEvent (ium) : object_uid depuis #1, ou null.</summary>
    public static int? ReadDeletedUid(IReadOnlyList<ProtoField> tree)
        => Var(tree, F("ObjectDeletedEvent", "object_uid")) is { } u ? (int)u : null;

    /// <summary>ObjectsDeletedEvent (version EN LOT) : uids retirés du sac (repeated int32, formes packée et non packée).</summary>
    public static IReadOnlyList<int> ReadDeletedUids(IReadOnlyList<ProtoField> tree)
        => ReadUids(tree, F("ObjectsDeletedEvent", "objects_uid"));

    /// <summary>ObjectsAddedEvent (version EN LOT) : ObjectItemInventory répétés au champ `objects`.</summary>
    public static IReadOnlyList<Item> ReadAddedList(IReadOnlyList<ProtoField> tree)
        => ReadContentAt(tree, F("ObjectsAddedEvent", "objects"));

    // ================================ RÉSULTAT DE CRAFT ================================
    /// <summary>Codes du résultat de craft (enum du proto) : IMPOSSIBLE=0 (absent, proto3), FAILED=1, SUCCESS=2, NEUTRAL=3,
    /// FORBIDDEN=4.</summary>
    public const int CraftImpossible = 0, CraftFailed = 1, CraftSuccess = 2, CraftNeutral = 3, CraftForbidden = 4;

    /// <summary>Résultat de craft décodé : Code (voir constantes Craft*), GID et quantité de l'objet produit (0 si absents).</summary>
    public readonly record struct CraftResult(int Code, long Gid, int Qty)
    {
        /// <summary>Un objet a été produit : SUCCESS, ou NEUTRAL qui rend quand même un objet.</summary>
        public bool Made => Code == CraftSuccess || (Code == CraftNeutral && Qty > 0);
    }

    /// <summary>ExchangeCraftResultEvent lu PAR NOM : { result, oneof { object_gid | object = ExchangeCraftResultWithObjectDescription
    /// { object = ObjectItem { quantity, gid } } } }. Un paquet VIDE = IMPOSSIBLE (recette invalide, proto3 omet le 0).
    /// Capture 3.6.12 : result 2, object.object = { quantity 100, gid 7652 } (uid omis : objet hors conteneur).</summary>
    public static CraftResult ReadCraftResult(IReadOnlyList<ProtoField> tree)
    {
        int code = (int)(Var(tree, F("ExchangeCraftResultEvent", "result")) ?? 0);
        long gid = 0; int qty = 0;
        if (Msg(tree, F("ExchangeCraftResultEvent", "object")) is { } desc
            && Msg(desc, F("ExchangeCraftResultWithObjectDescription", "object")) is { } item)
        {
            gid = (long)(Var(item, F("ObjectItem", "gid")) ?? 0);
            qty = (int)(Var(item, F("ObjectItem", "quantity")) ?? 0);
        }
        // Branche « object_gid » du oneof (résultat sans description d'objet).
        if (gid == 0) gid = (long)(Var(tree, F("ExchangeCraftResultEvent", "object_gid")) ?? 0);
        return new CraftResult(code, gid, qty);
    }

    /// <summary>Résultat d'une passe de FORGEMAGIE : le même ExchangeCraftResultEvent que le craft, mais son
    /// ExchangeCraftResultWithObjectDescription porte l'objet avec ses effets courants (<c>object</c>), la puissance de la
    /// rune (<c>fm_power</c>) et l'état du puits (<c>magic_pool_status</c>, lu en entier : sa sémantique exacte n'est pas
    /// établie hors ligne). Pool/FmPower sont null s'ils sont absents (proto3 omet le 0).</summary>
    public readonly record struct MagicResult(int Code, long Gid, IReadOnlyList<ItemEffect> Effects, int? Pool, int? FmPower);

    public static MagicResult ReadMagicResult(IReadOnlyList<ProtoField> tree)
    {
        int code = (int)(Var(tree, F("ExchangeCraftResultEvent", "result")) ?? 0);
        long gid = 0; int? pool = null, power = null;
        IReadOnlyList<ItemEffect> effects = System.Array.Empty<ItemEffect>();
        if (Msg(tree, F("ExchangeCraftResultEvent", "object")) is { } desc)
        {
            if (Var(desc, F("ExchangeCraftResultWithObjectDescription", "magic_pool_status")) is { } mp) pool = unchecked((int)(long)mp);
            if (Var(desc, F("ExchangeCraftResultWithObjectDescription", "fm_power")) is { } fp) power = unchecked((int)(long)fp);
            if (Msg(desc, F("ExchangeCraftResultWithObjectDescription", "object")) is { } item)
            {
                gid = (long)(Var(item, F("ObjectItem", "gid")) ?? 0);
                effects = ReadEffects(item);
            }
        }
        return new MagicResult(code, gid, effects, pool, power);
    }

    /// <summary>ObjectMovementEvent { position, object_uid } : position ABSENTE = 0 = AMULETTE (proto3), pas « inconnu ».</summary>
    public static (int Uid, int Position)? ReadMovement(IReadOnlyList<ProtoField> tree)
    {
        int uidField = F("ObjectMovementEvent", "object_uid"), posField = F("ObjectMovementEvent", "position");
        if (uidField <= 0 || posField <= 0 || Var(tree, uidField) is not { } uid || uid == 0 || uid > int.MaxValue) return null;
        ulong position = Var(tree, posField) ?? 0;
        return position <= int.MaxValue ? ((int)uid, (int)position) : null;
    }

    /// <summary>Kamas du snapshot InventoryContentEvent (ivx) : champ #1 (int64). Confirme capture : 177242. Null si absent.</summary>
    public static long? ReadKamas(IReadOnlyList<ProtoField> tree) => ReadKamasAt(tree, F("InventoryContentEvent", "kamas"));

    /// <summary>Kamas au champ `field` (ivx = #1 ; StorageInventoryContentEvent iwb = #2, le total du COFFRE). Null si absent.</summary>
    public static long? ReadKamasAt(IReadOnlyList<ProtoField> tree, int field)
        => Var(tree, field) is { } k ? (long)k : null;

    /// <summary>UN ObjectItemInventory porté au champ `field` (ex StorageObjectUpdateEvent.object). null si absent/illisible.</summary>
    public static Item? ReadEntryAt(IReadOnlyList<ProtoField> tree, int field)
        => Msg(tree, field) is { } inv ? ReadEntry(inv) : null;

    /// <summary>repeated int32 au champ `field` (ex StorageObjectsRemovedEvent.objects_uid). Accepte la forme PACKÉE (un champ
    /// Len : varints concaténés, lus depuis Bytes — le décodeur brut remplit Bytes pour TOUT champ Len, même quand il l'a
    /// aussi interprété comme sous-message) ET la forme non packée (un Varint par valeur).</summary>
    public static IReadOnlyList<int> ReadUids(IReadOnlyList<ProtoField> tree, int field)
    {
        var list = new List<int>();
        foreach (var f in tree)
        {
            if (f.Field != field) continue;
            if (f.Wire == WireType.Varint) list.Add(unchecked((int)(long)f.Varint));
            else if (f.Wire == WireType.Len && f.Bytes is { } b)
            {
                int pos = 0;
                while (pos < b.Length && RawProtobuf.TryReadVarint(b, ref pos, out var v)) list.Add(unchecked((int)(long)v));
            }
        }
        return list;
    }

    // ================================ EFFETS D'OBJET (caractéristiques) ================================
    /// <summary>Un effet d'objet (ObjectEffect) : `ActionId` = id d'effet (effectsdataroot), valeurs selon la branche du
    /// oneof : value_int -&gt; Value ; min_max -&gt; Min/Max ; dice -&gt; Min = num, Max = side, Const = const ; value_string -&gt; Text ;
    /// object_gid -&gt; ObjectGid. Les branches non lues (date, monture, jauge...) laissent tout à 0.</summary>
    public readonly record struct ItemEffect(int ActionId, int Value, int Min, int Max, int Const, string? Text, long ObjectGid);

    /// <summary>Effets d'un ObjectItem (champ répété `effects`), lus PAR NOM. Vide si aucun.</summary>
    public static IReadOnlyList<ItemEffect> ReadEffects(IReadOnlyList<ProtoField> objectItem, string objectType = "ObjectItem")
    {
        var list = new List<ItemEffect>();
        int field = objectType switch { "BidItem" => F("BidItem", "effects"), "BidExchangerObject" => F("BidExchangerObject", "effects"), _ => F("ObjectItem", "effects") };
        foreach (var f in objectItem)
        {
            if (f.Field != field) continue;
            var e = f.Message;
            // A valid encoded effect can consist entirely of printable bytes.
            // Its schema is a message even if the raw decoder guessed text.
            if (e is null && f.Bytes is { Length: > 0 } bytes)
                try { e = RawProtobuf.Parse(bytes); } catch (System.FormatException) { }
            if (e is not null && ReadEffect(e) is { } fx) list.Add(fx);
        }
        return list;
    }

    private static ItemEffect? ReadEffect(IReadOnlyList<ProtoField> e)
    {
        if (Var(e, F("ObjectEffect", "action")) is not { } action || action == 0) return null;
        int value = Var(e, F("ObjectEffect", "value_int")) is { } v ? unchecked((int)(long)v) : 0;   // int32 : négatif sur 10 octets
        int min = 0, max = 0, cst = 0;
        if (Msg(e, F("ObjectEffect", "min_max")) is { } mm)
        {
            min = I32(mm, F("ObjectEffectMinMaxValue", "min"));
            max = I32(mm, F("ObjectEffectMinMaxValue", "max"));
        }
        else if (Msg(e, F("ObjectEffect", "dice")) is { } d)
        {
            min = I32(d, F("ObjectEffectDiceValue", "num"));
            max = I32(d, F("ObjectEffectDiceValue", "side"));
            cst = I32(d, F("ObjectEffectDiceValue", "const"));
        }
        string? text = PayloadReader.Str(e, F("ObjectEffect", "value_string"));
        long gid = (long)(Var(e, F("ObjectEffect", "object_gid")) ?? 0);
        return new ItemEffect((int)action, value, min, max, cst, text, gid);
    }

    /// <summary>SetUpdateEvent : panoplie dont l'état change — (id de panoplie, uid des pièces portées, bonus ACTUELS de la panoplie). Sans pièce portée, les bonus sont vides.</summary>
    public static (int SetId, IReadOnlyList<int> Uids, IReadOnlyList<ItemEffect> Effects)? ReadSetUpdate(IReadOnlyList<ProtoField> tree)
    {
        int setF = F("SetUpdateEvent", "set_id"), uidsF = F("SetUpdateEvent", "objects_uid"), fxF = F("SetUpdateEvent", "effects");
        if (Var(tree, setF) is not { } id) return null;
        var fx = new List<ItemEffect>();
        foreach (var f in tree)
            if (f.Field == fxF && f.Message is { } e && ReadEffect(e) is { } x) fx.Add(x);
        return (unchecked((int)(long)id), ReadUids(tree, uidsF), fx);
    }

    private static int I32(IReadOnlyList<ProtoField> node, int field) => Var(node, field) is { } v ? unchecked((int)(long)v) : 0;

    /// <summary>(uid, effets) de chaque ObjectItemInventory répété au champ `objectsField` (snapshot sac ou coffre).</summary>
    public static IReadOnlyList<(int Uid, IReadOnlyList<ItemEffect> Effects)> ReadEffectsAt(IReadOnlyList<ProtoField> tree, int objectsField)
    {
        var list = new List<(int, IReadOnlyList<ItemEffect>)>();
        foreach (var f in tree)
            if (f.Field == objectsField && f.Message is { } inv && EntryEffects(inv) is { } x) list.Add(x);
        return list;
    }

    /// <summary>Effets de l'UN ObjectItemInventory porté au champ `field` (ex StorageObjectUpdateEvent.object).</summary>
    public static (int Uid, IReadOnlyList<ItemEffect> Effects)? ReadEntryEffectsAt(IReadOnlyList<ProtoField> tree, int field)
        => Msg(tree, field) is { } inv ? EntryEffects(inv) : null;

    /// <summary>Effets du snapshot InventoryContentEvent.</summary>
    public static IReadOnlyList<(int Uid, IReadOnlyList<ItemEffect> Effects)> ReadContentEffects(IReadOnlyList<ProtoField> tree)
        => ReadEffectsAt(tree, F("InventoryContentEvent", "objects"));

    /// <summary>Effets de l'objet porté par ObjectAddedEvent.</summary>
    public static (int Uid, IReadOnlyList<ItemEffect> Effects)? ReadAddedEffects(IReadOnlyList<ProtoField> tree)
        => Msg(tree, F("ObjectAddedEvent", "object")) is { } inv ? EntryEffects(inv) : null;

    /// <summary>Effets des objets d'ObjectsAddedEvent (version en lot).</summary>
    public static IReadOnlyList<(int Uid, IReadOnlyList<ItemEffect> Effects)> ReadAddedListEffects(IReadOnlyList<ProtoField> tree)
        => ReadEffectsAt(tree, F("ObjectsAddedEvent", "objects"));

    /// <summary>ObjectModifiedEvent (forgemagie, mimibiote, objet vivant...) : l'objet modifié et ses nouveaux effets. Le champ
    /// `object` porte un ObjectItemInventory (sous-message `item`) comme ObjectAddedEvent ; s'il porte directement un
    /// ObjectItem (pas de sous-message `item`), on le lit tel quel. null si aucun uid lisible.</summary>
    public static (int Uid, IReadOnlyList<ItemEffect> Effects)? ReadModifiedEffects(IReadOnlyList<ProtoField> tree)
    {
        if (Msg(tree, F("ObjectModifiedEvent", "object")) is not { } obj) return null;
        if (Msg(obj, F("ObjectItemInventory", "item")) is not null) return EntryEffects(obj);
        return Var(obj, F("ObjectItem", "uid")) is { } uid && uid != 0 ? ((int)uid, ReadEffects(obj)) : null;
    }

    private static (int Uid, IReadOnlyList<ItemEffect> Effects)? EntryEffects(IReadOnlyList<ProtoField> inv)
        => Msg(inv, F("ObjectItemInventory", "item")) is { } item && Var(item, F("ObjectItem", "uid")) is { } uid && uid != 0
           ? ((int)uid, ReadEffects(item)) : null;

    // ObjectItemInventory { position #1 ; item #5 = ObjectItem { gid #1 ; quantity #3 ; uid #4 } }
    private static Item? ReadEntry(IReadOnlyList<ProtoField> inv)
    {
        if (Msg(inv, F("ObjectItemInventory", "item")) is not { } item) return null;
        // 3.6.11.12 : ObjectItemInventory { position #3, item #5 } ; ObjectItem { uid #1, quantite #2, effets #3, gid #5 }.
        // Preuve croisee : ObjectAddedEvent du butin porte uid 18299708 / gid 31518, et le bilan de fin de combat
        // (jwe) annonce le meme gid 31518 en quantite 1. Avant le patch : position #1, gid #1, quantite #3, uid #4.
        // Nom résolu par le mapping : position ABSENTE = 0 = AMULETTE (proto3).
        int position = (int)(Var(inv, F("ObjectItemInventory", "position")) ?? 0);
        if (Var(item, F("ObjectItem", "gid")) is { } gid && Var(item, F("ObjectItem", "uid")) is { } uid)
            return new Item((int)uid, (long)gid, (int)(Var(item, F("ObjectItem", "quantity")) ?? 0), position);
        return null;
    }

    private static IReadOnlyList<ProtoField>? Msg(IReadOnlyList<ProtoField> node, int field)
        => node.FirstOrDefault(x => x.Field == field && x.Message is not null)?.Message;

    private static ulong? Var(IReadOnlyList<ProtoField> node, int field)
        => node.FirstOrDefault(x => x.Field == field && x.Wire == WireType.Varint)?.Varint;
}
