using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;

namespace GivreClone.App.Services;

/// <summary>Projection privée des événements serveur d'une opération : aucune mutation optimiste du sac.
/// Une séparation/fusion n'est confirmée qu'avec les deux côtés du transfert et les mêmes effets.</summary>
public sealed class InventoryOperationReceipt
{
    private readonly InventoryStore.Item _source;
    private readonly int? _position;
    private readonly Dictionary<int, InventoryStore.Item> _before;
    private readonly Dictionary<int, InventoryStore.Item> _after;
    private readonly Dictionary<int, IReadOnlyList<InventoryReader.ItemEffect>> _effects;
    private readonly Dictionary<int, IReadOnlyList<InventoryReader.ItemEffect>> _beforeEffects;
    public int? ResultUid { get; private set; }

    public InventoryOperationReceipt(InventoryStore.Item source, int? position,
        IReadOnlyList<InventoryStore.Item> snapshot, Func<int, IReadOnlyList<InventoryReader.ItemEffect>> effects)
    {
        _source = source;
        _position = position;
        _before = snapshot.ToDictionary(i => i.Uid);
        _after = new(_before);
        _effects = snapshot.ToDictionary(i => i.Uid, i => effects(i.Uid));
        _beforeEffects = new(_effects);
    }

    public bool Observe(string name, IReadOnlyList<ProtoField> payload)
    {
        void Put(InventoryReader.Item i) => _after[i.Uid] = new(i.Uid, i.Gid, i.Qty, i.Position);
        void Quantity(int uid, int qty)
        {
            if (qty <= 0) _after.Remove(uid);
            else if (_after.TryGetValue(uid, out var it)) _after[uid] = it with { Qty = qty };
        }
        switch (name)
        {
            case "ObjectMovementEvent":
                if (InventoryReader.ReadMovement(payload) is { } mv && _after.TryGetValue(mv.Uid, out var item))
                    _after[mv.Uid] = item with { Position = mv.Position };
                break;
            case "ObjectAddedEvent":
                if (InventoryReader.ReadAdded(payload) is { } added) Put(added);
                if (InventoryReader.ReadAddedEffects(payload) is { } fx) _effects[fx.Uid] = fx.Effects;
                break;
            case "ObjectsAddedEvent":
                foreach (var i in InventoryReader.ReadAddedList(payload)) Put(i);
                foreach (var e in InventoryReader.ReadAddedListEffects(payload)) _effects[e.Uid] = e.Effects;
                break;
            case "ObjectQuantityEvent":
                if (InventoryReader.ReadQuantity(payload) is { } q) Quantity(q.Uid, q.Qty);
                break;
            case "ObjectsQuantityEvent":
                foreach (var batchQuantity in InventoryReader.ReadQuantities(payload)) Quantity(batchQuantity.Uid, batchQuantity.Qty);
                break;
            case "ObjectDeletedEvent":
                if (InventoryReader.ReadDeletedUid(payload) is { } uid) _after.Remove(uid);
                break;
            case "ObjectsDeletedEvent":
                foreach (int deletedUid in InventoryReader.ReadDeletedUids(payload)) _after.Remove(deletedUid);
                break;
            default: return false;
        }

        int remaining = _after.TryGetValue(_source.Uid, out var current) ? current.Qty : 0;
        if (_position is null) return remaining < _source.Qty; // usage : consommation de CET UID uniquement
        if (remaining > 0 && current.Position == _position && current.Position != _source.Position)
        {
            ResultUid = current.Uid;
            return true;
        }

        // Équiper une unité d'une pile crée un UID ; déséquiper peut fusionner avec une pile du sac.
        int moved = _position == 63 ? _source.Qty : 1;
        if (_source.Qty - remaining != moved || (_position == 63 && remaining != 0)) return false;
        var sourceEffects = _beforeEffects.GetValueOrDefault(_source.Uid) ?? Array.Empty<InventoryReader.ItemEffect>();
        var candidates = _after.Values.Where(i => i.Uid != _source.Uid && i.Gid == _source.Gid &&
            i.Position == _position && i.Qty > 0 &&
            _effects.TryGetValue(i.Uid, out var fx) && fx.SequenceEqual(sourceEffects) &&
            (!_before.TryGetValue(i.Uid, out var old)
                ? i.Qty == moved
                : _position == 63 && old.Position == 63 && old.Gid == i.Gid &&
                    _beforeEffects[i.Uid].SequenceEqual(sourceEffects) && (long)i.Qty - old.Qty == moved)).ToList();
        if (candidates.Count != 1) return false;
        ResultUid = candidates[0].Uid;
        return true;
    }
}
