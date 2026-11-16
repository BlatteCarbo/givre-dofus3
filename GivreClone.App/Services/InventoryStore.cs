using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services;

/// <summary>
/// Modele d'inventaire par pid : uid -&gt; (gid, quantite). Seede par InventoryContentEvent (ivx) au login, maintenu
/// par ObjectAddedEvent (iua) / ObjectQuantityEvent (ivj) / ObjectDeletedEvent (ium) et leurs versions EN LOT
/// (ObjectsAdded/ObjectsQuantity/ObjectsDeleted : dépôt/retrait banque par liste). Reproduit le "convertisseur"
/// d'un bot tiers (RawInventory.GetObjectByGid): le protocole ne transporte JAMAIS le GID (id catalogue) dans les
/// requetes d'usage/drop, UNIQUEMENT l'UID (id d'INSTANCE du stack) -&gt; il faut resoudre GID-&gt;UID via ce store
/// avant de forger iuu/iur. Thread-safe (acces depuis le thread du relais ET les verbes Lua).
/// </summary>
public sealed class InventoryStore
{
    public readonly record struct Item(int Uid, long Gid, int Qty, int Position = 63);   // position 63 = sac (non équipé)

    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<int, Item>> _byPid = new();
    private readonly ConcurrentDictionary<uint, byte> _seeded = new();
    private readonly ConcurrentDictionary<uint, int> _seedSeq = new();
    // V13: a source-scoped, monotonic model revision for read-only supply
    // planning. Every actual store mutation holds the same per-PID gate, so
    // Revision cannot describe a half-written bag snapshot. It is NOT a
    // server receipt, an action ACK or a globally unique transport sequence.
    private readonly ConcurrentDictionary<uint, object> _revisionGates = new();
    private readonly ConcurrentDictionary<uint, long> _mutationVersions = new();
    private object MutationGate(uint pid) => _revisionGates.GetOrAdd(pid, static _ => new object());
    private void Bump(uint pid) => _mutationVersions.AddOrUpdate(pid, 1,
        static (_, old) => old == long.MaxValue ? 0 : old + 1);
    public long ObservationRevision(uint pid)
    {
        lock (MutationGate(pid))
            return _seeded.ContainsKey(pid) ? _mutationVersions.GetValueOrDefault(pid) : 0;
    }
    public int SeedVersion(uint pid) => _seedSeq.GetValueOrDefault(pid);
    public bool IsSeeded(uint pid) => _seeded.ContainsKey(pid);

    private ConcurrentDictionary<int, Item> Bag(uint pid) => _byPid.GetOrAdd(pid, static _ => new());

    /// <summary>Remplace TOUT l'inventaire du pid (snapshot InventoryContentEvent).</summary>
    public void Seed(uint pid, IEnumerable<Item> items)
    {
        lock (MutationGate(pid))
        {
            var bag = new ConcurrentDictionary<int, Item>();
            foreach (var it in items)
                if (it.Uid > 0 && it.Gid > 0 && it.Qty > 0) bag[it.Uid] = it;
            _byPid[pid] = bag;
            _seeded[pid] = 0;
            _seedSeq.AddOrUpdate(pid, 1, static (_, v) => v + 1);
            Bump(pid);
        }
    }

    /// <summary>Ajoute/met a jour un stack (ObjectAddedEvent).</summary>
    public void Upsert(uint pid, Item it)
    {
        if (it.Uid <= 0 || it.Gid <= 0) return;
        if (it.Qty <= 0) Remove(pid, it.Uid);
        else lock (MutationGate(pid)) { Bag(pid)[it.Uid] = it; Bump(pid); }
    }

    /// <summary>Met a jour la quantite d'un stack existant (ObjectQuantityEvent). No-op si l'uid est inconnu.</summary>
    public void SetQuantity(uint pid, int uid, int qty)
    {
        if (qty <= 0) { Remove(pid, uid); return; }
        lock (MutationGate(pid))
        {
            var bag = Bag(pid);
            while (bag.TryGetValue(uid, out var it))
                if (bag.TryUpdate(uid, it with { Qty = qty }, it))
                { Bump(pid); break; }
        }
    }

    /// <summary>Quantité reçue du serveur (ObjectQuantity / ObjectsQuantity) : &gt; 0 -&gt; SetQuantity ; 0 (champ absent, proto3)
    /// = pile vidée -&gt; Remove (sinon l'uid resterait dans la liste d'un prochain dépôt banque).</summary>
    public void ApplyQuantity(uint pid, int uid, int qty)
    {
        if (qty > 0) SetQuantity(pid, uid, qty); else Remove(pid, uid);
    }

    /// <summary>Met a jour la position d'equipement d'un stack existant (ObjectMovementEvent : 63 = sac). No-op si l'uid est inconnu.</summary>
    public void SetPosition(uint pid, int uid, int position)
    {
        lock (MutationGate(pid))
        {
            var bag = Bag(pid);
            while (bag.TryGetValue(uid, out var it))
                if (bag.TryUpdate(uid, it with { Position = position }, it))
                { Bump(pid); break; }
        }
    }

    /// <summary>Retire un stack (ObjectDeletedEvent).</summary>
    public void Remove(uint pid, int uid)
    {
        lock (MutationGate(pid))
            if (Bag(pid).TryRemove(uid, out _)) Bump(pid);
    }

    public void Clear(uint pid)
    {
        lock (MutationGate(pid))
        {
            _byPid.TryRemove(pid, out _);
            _seeded.TryRemove(pid, out _);
            Bump(pid);
        }
    }

    /// <summary>Stack par uid, ou null.</summary>
    public Item? Get(uint pid, int uid) => _byPid.TryGetValue(pid, out var bag) && bag.TryGetValue(uid, out var it) ? it : null;

    /// <summary>Mise à jour OPTIMISTE et idempotente : applique <paramref name="newQty"/> (≤ 0 = retire) SEULEMENT si le stack
    /// est encore à <paramref name="expected"/>. Une mise à jour du serveur arrivée entre-temps fait foi (jamais de double retrait).</summary>
    public bool TryUpdateQuantity(uint pid, int uid, int expected, int newQty)
    {
        lock (MutationGate(pid))
        {
            if (!_byPid.TryGetValue(pid, out var bag) || !bag.TryGetValue(uid, out var it) || it.Qty != expected) return false;
            bool applied = newQty > 0 ? bag.TryUpdate(uid, it with { Qty = newQty }, it)
                            : ((ICollection<KeyValuePair<int, Item>>)bag).Remove(new KeyValuePair<int, Item>(uid, it));
            if (applied) Bump(pid);
            return applied;
        }
    }

    /// <summary>Quantite TOTALE d'un objet par son GID (somme des stacks). 0 si absent. = getInventoryItemCount(gid).</summary>
    public int CountByGid(uint pid, long gid)
        => _byPid.TryGetValue(pid, out var bag) ? bag.Values.Where(i => i.Gid == gid).Sum(i => i.Qty) : 0;

    /// <summary>UIDs des objets NON équipés (position 63 = sac) — pour un dépôt banque « objets visibles » (kdd).</summary>
    public IReadOnlyList<int> InventoryUids(uint pid)
        => _byPid.TryGetValue(pid, out var bag)
           ? bag.Values.Where(i => i.Position == 63).Select(i => i.Uid).ToList()
           : System.Array.Empty<int>();

    /// <summary>1er UID (instance de stack) portant ce GID, ou null si absent. = le "convertisseur" GID-&gt;UID.</summary>
    public int? FirstUidByGid(uint pid, long gid)
    {
        if (_byPid.TryGetValue(pid, out var bag))
            foreach (var it in bag.Values)
                if (it.Gid == gid) return it.Uid;
        return null;
    }

    /// <summary>Lot I : 1er objet porté à cette position d'équipement (null si l'emplacement est libre ou inconnu).</summary>
    public Item? ItemAt(uint pid, int position)
    {
        if (_byPid.TryGetValue(pid, out var bag))
            foreach (var it in bag.Values)
                if (it.Position == position) return it;
        return null;
    }

    /// <summary>Lot I : 1er UID de ce GID DANS LE SAC (position 63) — ne re-sélectionne JAMAIS l'exemplaire déjà équipé.</summary>
    public int? FirstBagUidByGid(uint pid, long gid)
    {
        if (_byPid.TryGetValue(pid, out var bag))
            foreach (var it in bag.Values)
                if (it.Gid == gid && it.Position == 63 && it.Qty > 0) return it.Uid;
        return null;
    }

    /// <summary>Plan d'usage borné par les quantités serveur, sac uniquement. Vide si le stock demandé manque.</summary>
    public IReadOnlyList<(int Uid, int Qty)> PlanBagUse(uint pid, long gid, int count)
    {
        int remaining = count < 1 ? 1 : count;
        var plan = new List<(int Uid, int Qty)>();
        foreach (var item in Items(pid).Where(i => i.Gid == gid && i.Position == 63 && i.Qty > 0)
                     .OrderByDescending(i => i.Qty).ThenBy(i => i.Uid))
        {
            int take = System.Math.Min(remaining, item.Qty);
            plan.Add((item.Uid, take));
            remaining -= take;
            if (remaining == 0) return plan;
        }
        return System.Array.Empty<(int, int)>();
    }

    /// <summary>Lot I : objets équipés (position != 63), triés par position.</summary>
    public IReadOnlyList<Item> Equipped(uint pid)
        => _byPid.TryGetValue(pid, out var bag)
           ? bag.Values.Where(i => i.Position != 63).OrderBy(i => i.Position).ThenBy(i => i.Uid).ToList()
           : System.Array.Empty<Item>();

    /// <summary>Nombre de stacks distincts connus pour le pid (diagnostic : 0 = inventaire pas encore seede).</summary>
    public int StackCount(uint pid) => _byPid.TryGetValue(pid, out var bag) ? bag.Count : 0;

    /// <summary>Snapshot des objets du pid (copie), trié par gid. Pour l'onglet Inventaire.</summary>
    public IReadOnlyList<Item> Items(uint pid)
        => _byPid.TryGetValue(pid, out var bag)
           ? bag.Values.OrderBy(i => i.Gid).ThenBy(i => i.Uid).ToList()
           : System.Array.Empty<Item>();
}
