using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Engine.Recovery;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

// ===================== Effets des objets (caractéristiques affichées dans l'infobulle du jeu) =====================
// Le sac et le coffre ne gardent que (uid, gid, qty, position) ; les EFFETS de chaque exemplaire (jets d'un équipement,
// forgemagie...) sont lus des mêmes paquets et rangés ici par (pid, uid). Un uid est unique par exemplaire : sac et banque
// partagent la table. Lecture par nom (fields.json), aucune requête au serveur.
public sealed partial class LaunchService
{
    private readonly ConcurrentDictionary<(uint Pid, int Uid), IReadOnlyList<InventoryReader.ItemEffect>> _itemEffects = new();

    /// <summary>Effets connus d'un exemplaire (sac ou coffre). Vide si l'objet n'en a pas ou n'a pas encore été vu.</summary>
    public IReadOnlyList<InventoryReader.ItemEffect> ItemEffects(uint pid, int uid)
        => _itemEffects.TryGetValue((pid, uid), out var fx) ? fx : Array.Empty<InventoryReader.ItemEffect>();

    /// <summary>Liaison de cet exemplaire (personnage, compte, date), lue sur ses effets.</summary>
    public ItemBind ItemBindingOf(uint pid, int uid) => ItemBindingRules.Classify(ItemEffects(pid, uid).Select(e => e.ActionId));
    public bool ItemCanTrade(uint pid, int uid) => ItemBindingRules.CanTrade(ItemBindingOf(pid, uid));
    public bool ItemCanCrush(uint pid, int uid) => ItemBindingRules.CanCrush(ItemBindingOf(pid, uid));
    public bool ItemCanBank(uint pid, int uid) => ItemBindingRules.CanBank(ItemBindingOf(pid, uid));

    /// <summary>Lignes d'infobulle d'un exemplaire, dans l'ordre du serveur (celui du jeu) : texte résolu par le gabarit du
    /// client (« 143 Vitalité ») et malus (affiché en rouge). Les effets que le jeu masque (showInTooltip = faux) sont omis.</summary>
    public IReadOnlyList<(string Text, bool Negative)> ItemEffectLines(uint pid, int uid)
    {
        var fx = ItemEffects(pid, uid);
        if (fx.Count == 0) return Array.Empty<(string, bool)>();
        var lines = new List<(string, bool)>(fx.Count);
        foreach (var e in fx)
        {
            var def = _gameNames.Effect(e.ActionId);
            if (def is { ShowOnItem: false } && !ItemBindingRules.IsShownLink(e.ActionId)) continue;
            string? objName = e.ObjectGid > 0 ? ItemDisplayName(e.ObjectGid) : null;
            string text = ItemEffectText.Format(e.ActionId, _gameNames.EffectTemplate(e.ActionId),
                                                ItemEffectText.Params(e.Value, e.Min, e.Max, e.Const, e.Text, objName));
            lines.Add((text, ItemEffectText.IsNegative(def?.BonusType ?? 0, e.Value, text)));
        }
        return lines;
    }

    /// <summary>Texte d'un effet (« 143 Vitalité ») et s'il est masqué par le jeu ; null si les données du client ne sont pas chargées.</summary>
    public (string Text, bool Hidden)? DescribeEffect(InventoryReader.ItemEffect e)
    {
        if (!_gameNames.IsReady) return null;
        var def = _gameNames.Effect(e.ActionId);
        string? objName = e.ObjectGid > 0 ? ItemDisplayName(e.ObjectGid) : null;
        string text = ItemEffectText.Format(e.ActionId, _gameNames.EffectTemplate(e.ActionId),
                                            ItemEffectText.Params(e.Value, e.Min, e.Max, e.Const, e.Text, objName));
        return (text, def is { ShowOnItem: false });
    }

    private readonly ConcurrentDictionary<uint, int> _itemEffectsStamp = new();
    /// <summary>Compteur qui change à chaque mise à jour d'effets du perso : l'onglet Inventaire reconstruit ses lignes.</summary>
    public int ItemEffectsStamp(uint pid) => _itemEffectsStamp.TryGetValue(pid, out var v) ? v : 0;

    /// <summary>Efface les effets d'une session terminée. L'UID ne constitue pas une
    /// identité durable entre deux personnages sur un même PID.</summary>
    private void ForgetItemEffects(uint pid)
    {
        foreach (var key in _itemEffects.Keys)
            if (key.Pid == pid) _itemEffects.TryRemove(key, out _);
        _itemEffectsStamp.TryRemove(pid, out _);
    }

    /// <summary>Après modification du sac/coffre, ne conserve que les effets d'UID
    /// encore observés dans l'un des deux stores. Un dépôt banque ne perd donc
    /// pas les effets d'un objet qui garde son UID dans le coffre.</summary>
    private void PruneItemEffects(uint pid)
    {
        bool changed = false;
        var visible = Hdv.MyListings(pid).Select(i => i.Uid).ToHashSet();
        if (TradeView(pid) is { } trade) visible.UnionWith(trade.Mine.Concat(trade.Theirs).Select(i => i.Uid));
        foreach (var key in _itemEffects.Keys)
            if (key.Pid == pid && !visible.Contains(key.Uid) && Inventory.Get(pid, key.Uid) is null && Bank.Get(pid, key.Uid) is null
                && _hdvMergedInventory.Get(pid, key.Uid) is null)
                changed |= _itemEffects.TryRemove(key, out _);
        if (changed) _itemEffectsStamp.AddOrUpdate(pid, 1, (_, old) => unchecked(old + 1));
    }

    private void NoteItemEffects(DecodedPacket p)
    {
        if (p.ClientToServer || p.Name is null) return;
        // Storage* messages are also emitted by a separately opened locked
        // house chest. Its UID/effect snapshot must not overwrite the player's
        // persistent bank/bag cache (the bank reducer already ignores it).
        if (p.Name.StartsWith("Storage", StringComparison.Ordinal) && _chestSession.ContainsKey(p.Pid)) return;
        try
        {
            switch (p.Name)
            {
                case "ExchangeObjectsAddedEvent":
                case "ExchangeObjectsModifiedEvent":
                    StoreEffects(p.Pid, InventoryReader.ReadEffectsAt(p.Payload, FNum(p.Name, "objects")));
                    break;
                case "ExchangeBidSellerStartedEvent":
                    foreach (var entry in p.Payload.Where(f => f.Field == FNum(p.Name, "objects")))
                        if (entry.Message?.FirstOrDefault(f => f.Field == FNum("ItemToSellInBid", "object"))?.Message is { } item)
                            StoreBidEffects(p.Pid, item);
                    break;
                case "ExchangeBidHouseItemAddedEvent":
                    if (p.Payload.FirstOrDefault(f => f.Field == FNum(p.Name, "item"))?.Message is { } bidItem)
                        StoreBidEffects(p.Pid, bidItem);
                    break;
                case "InventoryContentEvent": StoreEffects(p.Pid, InventoryReader.ReadContentEffects(p.Payload)); break;
                case "ObjectAddedEvent": if (InventoryReader.ReadAddedEffects(p.Payload) is { } a) StoreEffects(p.Pid, a); break;
                case "ObjectsAddedEvent": StoreEffects(p.Pid, InventoryReader.ReadAddedListEffects(p.Payload)); break;
                case "ObjectModifiedEvent":
                    if (InventoryReader.ReadModifiedEffects(p.Payload) is { } m) StoreEffects(p.Pid, m);
                    break;
                case "StorageInventoryContentEvent": StoreEffects(p.Pid, InventoryReader.ReadEffectsAt(p.Payload, FNum("StorageInventoryContentEvent", "objects"))); break;
                case "StorageObjectUpdateEvent":
                    if (InventoryReader.ReadEntryEffectsAt(p.Payload, FNum("StorageObjectUpdateEvent", "object")) is { } u) StoreEffects(p.Pid, u);
                    break;
                case "StorageObjectsUpdateEvent": StoreEffects(p.Pid, InventoryReader.ReadEffectsAt(p.Payload, FNum("StorageObjectsUpdateEvent", "objects"))); break;
            }
        }
        catch { /* best-effort : un effet illisible ne doit pas casser le suivi du sac */ }
    }

    private void StoreEffects(uint pid, IReadOnlyList<(int Uid, IReadOnlyList<InventoryReader.ItemEffect> Effects)> list)
    {
        foreach (var x in list) StoreEffects(pid, x);
    }

    private void StoreBidEffects(uint pid, IReadOnlyList<GivreCore.Protobuf.ProtoField> item)
    {
        int uid = (int)PayloadReader.Int(item, FNum("BidItem", "uid"), 0);
        if (uid > 0) StoreEffects(pid, (uid, InventoryReader.ReadEffects(item, "BidItem")));
    }

    private void StoreEffects(uint pid, (int Uid, IReadOnlyList<InventoryReader.ItemEffect> Effects) x)
    {
        _itemEffectsStamp.AddOrUpdate(pid, 1, (_, v) => unchecked(v + 1));
        if (x.Effects.Count == 0) _itemEffects.TryRemove((pid, x.Uid), out _);
        else _itemEffects[(pid, x.Uid)] = x.Effects;
    }
}
