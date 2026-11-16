using System;
using System.Collections.Concurrent;
using System.Linq;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    // La vue serveur sac+banque n'est PAS le sac : aucun objet distant ne doit devenir
    // utilisable/déposable par les scripts ordinaires simplement en ouvrant le HDV.
    private readonly InventoryStore _hdvMergedInventory = new();
    private readonly ConcurrentDictionary<uint, bool> _hdvMergedRequested = new();
    public bool HdvBankInventoryReady(uint pid) => HdvSellerOpen(pid)
        && _hdvMergedRequested.GetValueOrDefault(pid) && _hdvMergedInventory.IsSeeded(pid);

    private void ClearHdvMergedInventory(uint pid)
    {
        _hdvMergedRequested.TryRemove(pid, out _);
        _hdvMergedInventory.Clear(pid);
    }

    private InventoryStore.Item? HdvSellStack(uint pid, int uid) =>
        HdvBankInventoryReady(pid) ? _hdvMergedInventory.Get(pid, uid) : Inventory.Get(pid, uid);

    public int HdvSellableQuantity(uint pid, int uid) => HdvSellStack(pid, uid) is { Position: 63 } item ? item.Qty : 0;

    private void ObserveHdvMergedInventory(DecodedPacket p)
    {
        if (p.ClientToServer)
        {
            if (p.Name == "InventoryUpdateMergedInventoryTypeRequest" && HdvSellerOpen(p.Pid)
                && FieldOf(p.Name, "context") is int context
                && PayloadReader.Int(p.Payload, context, 0) == 1
                && FieldOf(p.Name, "sources") is int sources)
                _hdvMergedRequested[p.Pid] = InventoryReader.ReadUids(p.Payload, sources).Any(s => s is 2 or 3);
            return;
        }
        if (!HdvBankInventoryReady(p.Pid)) return;
        switch (p.Name)
        {
            case "ObjectAddedEvent":
                if (InventoryReader.ReadAdded(p.Payload) is { } item)
                    _hdvMergedInventory.Upsert(p.Pid, new(item.Uid, item.Gid, item.Qty, item.Position));
                break;
            case "ObjectsAddedEvent":
                foreach (var added in InventoryReader.ReadAddedList(p.Payload))
                    _hdvMergedInventory.Upsert(p.Pid, new(added.Uid, added.Gid, added.Qty, added.Position));
                break;
            case "ObjectQuantityEvent":
                if (InventoryReader.ReadQuantity(p.Payload) is { } q) _hdvMergedInventory.ApplyQuantity(p.Pid, q.Uid, q.Qty);
                break;
            case "ObjectsQuantityEvent":
                foreach (var quantity in InventoryReader.ReadQuantities(p.Payload)) _hdvMergedInventory.ApplyQuantity(p.Pid, quantity.Uid, quantity.Qty);
                break;
            case "ObjectDeletedEvent":
                if (InventoryReader.ReadDeletedUid(p.Payload) is { } uid) _hdvMergedInventory.Remove(p.Pid, uid);
                break;
            case "ObjectsDeletedEvent":
                foreach (var deleted in InventoryReader.ReadDeletedUids(p.Payload)) _hdvMergedInventory.Remove(p.Pid, deleted);
                break;
            case "ObjectMovementEvent":
                if (InventoryReader.ReadMovement(p.Payload) is { } mv) _hdvMergedInventory.SetPosition(p.Pid, mv.Uid, mv.Position);
                break;
        }
    }

    private void SeedHdvOrBagInventory(DecodedPacket p)
    {
        var items = InventoryReader.ReadContent(p.Payload)
            .Select(i => new InventoryStore.Item(i.Uid, i.Gid, i.Qty, i.Position)).ToArray();
        if (HdvSellerOpen(p.Pid) && _hdvMergedRequested.GetValueOrDefault(p.Pid))
        {
            // Le serveur envoie parfois un InventoryContentEvent vide en ouvrant la vue
            // sac+banque, puis rien d'autre. Ce message ne doit pas effacer le sac déjà lu :
            // la forgemagie et l'inventaire resteraient à 0 pile jusqu'à la reconnexion.
            if (items.Length == 0 && Inventory.StackCount(p.Pid) > 0)
            {
                TransportLog.Line($"[inventaire] pid {p.Pid} : snapshot HDV vide ignoré, sac conservé ({Inventory.StackCount(p.Pid)} piles).");
                return;
            }
            // Identité par UID, jamais par GID : deux exemplaires du même objet peuvent
            // provenir l'un du sac, l'autre de la banque. Quantités fraîches uniquement.
            var bagUids = Inventory.Items(p.Pid).Select(i => i.Uid).ToHashSet();
            Inventory.Seed(p.Pid, items.Where(i => bagUids.Contains(i.Uid) || i.Position != 63));
            _hdvMergedInventory.Seed(p.Pid, items);
        }
        else
        {
            Inventory.Seed(p.Pid, items);
            _hdvMergedInventory.Clear(p.Pid);
        }
    }
}
