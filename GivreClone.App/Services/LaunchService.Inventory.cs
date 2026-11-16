using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Transport;
using GivreClone.Engine.Accounts;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Ankama;
using GivreLauncher.Core.Launch;
using GivreLauncher.Core.Proxy;
using GivreLauncher.Core.Transport;
using GivreLauncher.Core.Zaap;
using CoreAccount = GivreLauncher.Core.Accounts.Account;

namespace GivreClone.App.Services;

// ===================== Inventaire : store du sac, usage / jet / suppression d'objets =====================
public sealed partial class LaunchService
{
    public readonly record struct ItemOperationResult(bool Sent, bool ServerConfirmed, bool Rejected, int? ResultUid);

    /// <summary>Contrôle MCP : attend un effet serveur corrélé, y compris un changement d'UID de pile.
    /// position null = utiliser une unité ; 63 = déséquiper ; 0..15 ou 28 = équiper.</summary>
    public async Task<ItemOperationResult> InventoryItemConfirmedAsync(uint pid, int uid, int? position,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Inventory.Get(pid, uid) is not { Qty: > 0 } item || IsInFight(pid)) return default;
        if (position is null && item.Position != 63) return default;
        if (position is { } pos && (pos == item.Position ||
            (pos == 63 ? item.Position == 63 : item.Position != 63 || !(pos is >= 0 and <= 15 or 28) || !EquipPositions(item.Gid).Contains(pos))))
            return default;
        var receipt = new InventoryOperationReceipt(item, position, Inventory.Items(pid), id => ItemEffects(pid, id));
        bool sent = false;
        var response = await SendAndWaitForCodeAsync(pid, () =>
        {
            ct.ThrowIfCancellationRequested();
            return sent = position is { } target ? SendObjectSetPosition(pid, uid, target) : UseItemUid(pid, uid);
        }, new[] { "ObjectMovementEvent", "ObjectAddedEvent", "ObjectsAddedEvent", "ObjectQuantityEvent",
            "ObjectsQuantityEvent", "ObjectDeletedEvent", "ObjectsDeletedEvent", "ObjectErrorEvent" },
            3000, p => p.Name == "ObjectErrorEvent" || receipt.Observe(p.Name!, p.Payload), ct).ConfigureAwait(false);
        bool rejected = response?.Name == "ObjectErrorEvent";
        bool confirmed = sent && response is not null && !rejected;
        TransportLog.Line($"[inventaire] pid {pid} : uid {uid}, cible={position?.ToString() ?? "usage"}, envoyé={sent}, confirmé={confirmed}, refus={rejected}, uid résultat={receipt.ResultUid?.ToString() ?? "?"}");
        return new(sent, confirmed, rejected, receipt.ResultUid);
    }

    /// <summary>Modele d'inventaire (uid-&gt;gid,qty) par pid. Seede/maintenu dans OnDecoded (ivx/iua/ivj/ium).</summary>
    public InventoryStore Inventory { get; } = new();

    /// <summary>Quantite totale d'un objet (par GID) dans le sac. 0 si absent ou inventaire pas encore seede (relog).</summary>
    public int InventoryCount(uint pid, long gid) => Inventory.CountByGid(pid, gid);

    /// <summary>Utilise/ouvre `count` exemplaires d'un objet par GID : resout GID-&gt;UID (le "convertisseur") puis forge
    /// iuu (ObjectUseRequest) autant de fois (ex : ouvrir tous les sacs de ble, 1 sac = 1 iuu). false si absent / pas de
    /// relais / inventaire pas encore recu.</summary>
    public bool UseInventoryItem(uint pid, long gid, int count)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;   // usage d'objet (ouverture des sacs de récolte…) rattaché au workflow récolte -> POINT D'ACTION
        var relay = RelayForInject();
        if (relay is null) return false;
        var plan = Inventory.PlanBagUse(pid, gid, count);
        if (plan.Count == 0)
        {
            TransportLog.Line($"[inventaire] pid {pid} : useInventoryItem gid {gid} -> aucun stack ({(Inventory.StackCount(pid) == 0 ? "inventaire pas encore recu (relog ?)" : "objet absent")})");
            return false;
        }
        foreach (var stack in plan)
        {
            for (int i = 0; i < stack.Qty; i++)
            {
                byte[] pkt = MoveForge.BuildObjectUseFramed(stack.Uid);
                if (pkt.Length == 0 || !relay.Inject(pid, pkt)) return false;
                ShowInjected(pid, pkt);
            }
            TransportLog.Line($"[inventaire] pid {pid} : ObjectUseRequest x{stack.Qty} sur uid {stack.Uid} (gid {gid}), envoi accepté (sans confirmation).");
        }
        return true;
    }

    /// <summary>Lot D : utilise `qty` exemplaires d'UN stack (par UID) en UN paquet (ObjectUseMultipleRequest). false si non
    /// forgeable / pas de relais. L'appelant vérifie l'effet et se replie sur <see cref="UseInventoryItem"/>. Gaté Perm.Gather.</summary>
    public bool UseInventoryItemMultiple(uint pid, int uid, int qty) => UseInventoryItemMultiple(pid, uid, qty, out _);

    /// <summary>Idem ; <paramref name="unforgeable"/> = true si le paquet n'a pas pu être FORGÉ (message absent du mapping :
    /// défaut de protocole durable), false pour un échec passager (permission, relais, injection).</summary>
    public bool UseInventoryItemMultiple(uint pid, int uid, int qty, out bool unforgeable)
    {
        unforgeable = false;
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject();
        if (relay is null || qty < 1 || Inventory.Get(pid, uid) is not { Position: 63, Qty: > 0 } item || qty > item.Qty) return false;
        byte[] pkt = MoveForge.BuildObjectUseMultipleFramed(uid, qty);
        if (pkt.Length == 0) { unforgeable = true; TransportLog.Line($"[inventaire] pid {pid} : ObjectUseMultipleRequest NON forgé ({MoveForge.LastForgeError})."); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[inventaire] pid {pid} : ObjectUseMultipleRequest x{qty} sur uid {uid} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>Jette `count` d'un objet par GID : resout GID-&gt;UID puis forge iur (ObjectDropRequest). false si absent.</summary>
    public bool DropInventoryItem(uint pid, long gid, int count)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;   // jeter le surplus de récolte rattaché au workflow récolte -> POINT D'ACTION (bloque toute session sans droit récolte)
        var relay = RelayForInject();
        if (relay is null) return false;
        if (Inventory.FirstBagUidByGid(pid, gid) is not { } uid) return false;   // jamais l'exemplaire équipé
        byte[] pkt = MoveForge.BuildObjectDropFramed(uid, count < 1 ? 1 : count);
        if (pkt.Length == 0) { TransportLog.Line($"[objet] pid {pid} : ObjectDropRequest pas encore mappé sur ce patch -> objet NON jeté (aucun paquet envoyé)."); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[inventaire] pid {pid} : ObjectDropRequest uid {uid} (gid {gid}) x{count} inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>DÉTRUIT `count` d'un objet par GID (ObjectDeleteRequest ≠ drop : l'objet est SUPPRIMÉ, pas posé au sol).
    /// count&lt;=0 = tout le stack (utile pour un vide-poche AUTO_DELETE piloté par script). GID->UID via l'inventaire.
    /// Gaté Perm.Gather (prolongement du workflow récolte, comme drop/dépôt banque).</summary>
    public bool DeleteInventoryItem(uint pid, long gid, int count)
        => DeleteInventoryItem(pid, gid, count, out _);

    /// <summary>Comme <see cref="DeleteInventoryItem(uint,long,int)"/> ; <paramref name="deleted"/> = quantité envoyée à la
    /// destruction (somme des stacks du SAC injectés avec succès). Ne touche JAMAIS un objet ÉQUIPÉ (position ≠ 63) et
    /// détruit stack par stack avec la quantité propre à chaque stack (un équipement droppé = 1 uid par exemplaire).</summary>
    public bool DeleteInventoryItem(uint pid, long gid, int count, out int deleted)
    {
        deleted = 0;
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return false;
        var relay = RelayForInject();
        if (relay is null) return false;
        var stacks = Inventory.Items(pid).Where(i => i.Gid == gid && i.Position == 63 && i.Qty > 0).ToList();
        if (stacks.Count == 0) return false;
        long wantL = count > 0 ? count : stacks.Sum(i => (long)i.Qty);
        int want = (int)Math.Min(int.MaxValue, wantL);
        bool any = false;
        foreach (var st in stacks)
        {
            if (want <= 0) break;
            int qty = Math.Min(want, st.Qty);
            byte[] pkt = MoveForge.BuildObjectDeleteFramed(st.Uid, qty);
            if (pkt.Length == 0) { TransportLog.Line($"[objet] pid {pid} : ObjectDeleteRequest pas encore mappé sur ce patch -> objet NON détruit (aucun paquet envoyé)."); break; }
            bool ok = relay.Inject(pid, pkt);
            ShowInjected(pid, pkt);
            TransportLog.Line($"[inventaire] pid {pid} : ObjectDeleteRequest uid {st.Uid} (gid {gid}) x{qty} inject={(ok ? "OK" : "ECHEC")}");
            if (!ok) break;
            any = true; deleted += qty; want -= qty;
        }
        return any;
    }
}
