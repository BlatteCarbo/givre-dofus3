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

// ===================== Établi de craft =====================
public sealed partial class LaunchService
{
    // ================================ ÉTABLI DE CRAFT ================================
    // Séquence CAPTURÉE (2026-09-21) : InteractiveUseRequest(element,skill) -> ExchangeCraftStartedEvent ;
    // pour chaque ingrédient GID->UID puis ExchangeObjectMoveRequest{quantity,uid} -> ExchangeObjectsAdded/Modified ;
    // ExchangeCraftCountRequest{count} -> ExchangeCraftCountModifiedEvent ; ExchangeReadyRequest{ready=1,step} ->
    // ExchangeCraftResultEvent ; DialogLeaveRequest. Tout résolu PAR NOM (fields.json), gaté Perm.Gather au point d'action.

    /// <summary>Issue d'un craft complet (API Lua craftItem) : réussite, quantité PRODUITE et GID de l'objet créé (0 si
    /// inconnus, p.ex. échec ou délai dépassé).</summary>
    public readonly record struct CraftOutcome(bool Ok, int Qty, long Gid);

    /// <summary>CRAFT complet et BLOQUANT (API Lua craftItem) : ouvre l'établi, pose les ingrédients, règle le nombre,
    /// valide, ferme. Retente UNE fois après un court répit si la 1re passe échoue SANS verdict du serveur (établi fermé,
    /// pose non accusée) : juste après un voyage, l'établi/le snapshot
    /// d'inventaire ne sont parfois pas encore prêts — uniquement avant validation. Après validation, une issue inconnue ne doit jamais être retentée. Un verdict
    /// explicite (craft raté / interdit / neutre sans objet) n'est PAS retenté. La réussite est lue PAR NOM dans le résultat
    /// (code SUCCESS), plus « paquet non vide ». Au retour, l'objet créé est déjà compté dans le modèle d'inventaire (attente
    /// bornée), pour qu'un getInventoryItemCount lu juste après soit juste.</summary>
    public async Task<CraftOutcome> CraftAsync(uint pid, int elementId, int skillUid, System.Collections.Generic.IReadOnlyList<(long Gid, int Qty)> recipe, int craftCount, CancellationToken ct = default)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return default;   // consomme l'inventaire -> POINT D'ACTION
        if (recipe is null || recipe.Count == 0 || craftCount < 1
            || recipe.Any(i => i.Gid <= 0 || i.Qty <= 0)) return default;
        var combined = recipe.GroupBy(i => i.Gid).Select(g => (Gid: g.Key, Qty: g.Sum(i => (long)i.Qty))).ToArray();
        if (combined.Sum(i => i.Qty) > int.MaxValue) return default;
        recipe = combined.Select(i => (i.Gid, (int)i.Qty)).ToArray();
        CraftOutcome last = default;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
            {
                try { await Task.Delay(1200, ct).ConfigureAwait(false); } catch { return last; }   // laisse l'établi/l'arrivée se stabiliser
                TransportLog.Line($"[craft] pid {pid} : établi/résultat non prêt -> nouvelle tentative.");
            }
            var (outcome, retry) = await CraftOnceAsync(pid, elementId, skillUid, recipe, craftCount, ct).ConfigureAwait(false);
            last = outcome;
            if (outcome.Ok || !retry || ct.IsCancellationRequested) return outcome;
        }
        return last;
    }

    // Une passe de craft. Retry = true quand l'échec vient d'un établi pas prêt / fermé (rien produit), false sur un verdict
    // explicite du serveur ou un succès.
    private async Task<(CraftOutcome Outcome, bool Retry)> CraftOnceAsync(uint pid, int elementId, int skillUid, System.Collections.Generic.IReadOnlyList<(long Gid, int Qty)> recipe, int craftCount, CancellationToken ct)
    {
        var relay = RelayForInject();
        if (relay is null) return (default, false);

        // ⚠ Le CLIENT RÉEL (ouvert en parallèle) peut renvoyer un InventoryUpdateMergedInventoryTypeRequest pendant qu'on
        // pose les ingrédients : le serveur RE-SYNC alors l'inventaire et RETIRE de l'établi les ingrédients qu'on venait
        // d'y poser (ExchangeObjectRemovedEvent) -> au ready il ne reste qu'un ingrédient -> recette invalide (résultat
        // vide) « ne donne rien ». On PISTE donc les retraits de NOS uid et on les REPOSE avant de valider. object_uid PAR NOM.
        // On piste AUSSI la fermeture de l'établi par le serveur (ExchangeLeaveEvent APRÈS l'ouverture) : sans ça, une passe
        // dont l'établi s'est refermé continuait d'envoyer nombre/validation/fermeture dans le vide (~20 s perdues avant le retry).
        // Abonné AVANT l'ouverture (le gestionnaire passe avant la résolution des attentes) ; une fermeture antérieure à
        // l'ouverture (fin d'un échange précédent) est ignorée.
        var placed = new System.Collections.Generic.List<(int Uid, int Qty)>();
        var removed = new System.Collections.Concurrent.ConcurrentDictionary<int, byte>();
        var openSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCraftPkt(DecodedPacket p)
        {
            if (p.Pid != pid || p.ClientToServer) return;
            switch (p.Name)
            {
                case "ExchangeCraftStartedEvent": openSeen.TrySetResult(); break;
                case "ExchangeLeaveEvent": if (openSeen.Task.IsCompleted) closed.TrySetResult(); break;
                case "ExchangeObjectRemovedEvent":
                    int u = (int)PayloadReader.Int(p.Payload, FNum("ExchangeObjectRemovedEvent", "object_uid"), 0);
                    if (u != 0) removed[u] = 0;
                    break;
            }
        }
        bool Closed(string step)
        {
            if (!closed.Task.IsCompleted) return false;
            TransportLog.Line($"[craft] pid {pid} : établi refermé par le serveur ({step}) -> abandon de la passe.");
            return true;
        }
        PacketDecoded += OnCraftPkt;
        try
        {
            var started = await SendAndWaitForCodeAsync(pid, () => !ct.IsCancellationRequested && relay.Inject(pid, MoveForge.BuildInteractiveUseFramed(skillUid, elementId)),
                new[] { "ExchangeCraftStartedEvent" }, 5000, ct).ConfigureAwait(false);
            if (started is null) { TransportLog.Line($"[craft] pid {pid} : établi non ouvert (element {elementId}, skill {skillUid})."); return (default, true); }
            // Photo du sac À L'OUVERTURE (le client réel a re-seedé l'inventaire juste avant) : base du « avant » pour vérifier
            // que l'objet créé est bien arrivé dans le modèle avant de rendre la main au script.
            var bagBefore = Inventory.Items(pid);

            string[] placeAcks = { "ExchangeObjectsAddedEvent", "ExchangeObjectsModifiedEvent", "ExchangeErrorEvent", "ExchangeLeaveEvent" };
            int stepTotal = 0;
            foreach (var (gid, required) in recipe)
            {
                if (required <= 0) continue;
                if (Closed("pose des ingrédients")) return (default, true);
                var ingredientPlan = Inventory.PlanBagUse(pid, gid, required);
                if (ingredientPlan.Count == 0)
                { TransportLog.Line($"[craft] pid {pid} : ingrédient gid {gid} x{required} insuffisant dans le sac -> abandon."); await LeaveExchangeAsync(pid, ct).ConfigureAwait(false); return (default, false); }
                foreach (var (uid, qty) in ingredientPlan)
                {
                var ack = await SendAndWaitForCodeAsync(pid, () => !ct.IsCancellationRequested && relay.Inject(pid, MoveForge.BuildExchangeObjectMoveFramed(uid, qty)),
                    placeAcks, 3000, p => WorkshopPlacementAck(p, uid, qty), ct).ConfigureAwait(false);
                if (Closed("pose des ingrédients")) return (default, true);
                if (ack is null || ack.Name is "ExchangeErrorEvent" or "ExchangeLeaveEvent")
                {
                    TransportLog.Line($"[craft] pid {pid} : ingrédient gid {gid} x{qty} {(ack is null ? "sans accusé de l'établi" : "refusé par le serveur")} -> abandon de la passe.");
                    await LeaveExchangeAsync(pid, ct).ConfigureAwait(false);
                    return (default, true);
                }
                placed.Add((uid, qty));
                removed.TryRemove(uid, out _);   // fraîchement (re)posé
                stepTotal += qty;
                }
            }

            // Laisse retomber un éventuel re-sync du client réel, puis REPOSE tout ingrédient retiré de l'établi (uid stable
            // après re-sync : la pile revient au sac avec le même uid). En craft NORMAL il n'y a aucun retrait -> aucune
            // repose -> zéro risque de double-dépôt.
            for (int round = 0; round < 3; round++)
            {
                try { await Task.Delay(250, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return (default, false); }
                if (Closed("repose des ingrédients")) return (default, true);
                var toReplace = placed.Where(x => removed.ContainsKey(x.Uid)).ToList();
                if (toReplace.Count == 0) break;
                foreach (var (uid, qty) in toReplace)
                {
                    var replacement = await SendAndWaitForCodeAsync(pid, () => !ct.IsCancellationRequested && relay.Inject(pid, MoveForge.BuildExchangeObjectMoveFramed(uid, qty)),
                        placeAcks, 3000, p => WorkshopPlacementAck(p, uid, qty), ct).ConfigureAwait(false);
                    if (replacement is null || replacement.Name is "ExchangeErrorEvent" or "ExchangeLeaveEvent")
                    {
                        TransportLog.Line($"[craft] pid {pid} : repose uid {uid} x{qty} non confirmée -> abandon avant validation.");
                        await LeaveExchangeAsync(pid, ct).ConfigureAwait(false);
                        return (default, false);
                    }
                    removed.TryRemove(uid, out _);
                }
                TransportLog.Line($"[craft] pid {pid} : {toReplace.Count} ingrédient(s) retiré(s) par un re-sync (client réel ?) -> reposé(s).");
            }
            if (Closed("repose des ingrédients")) return (default, true);
            if (placed.Any(x => removed.ContainsKey(x.Uid)))
            { TransportLog.Line($"[craft] pid {pid} : établi désynchronisé (ingrédients retirés) -> abandon (retry)."); await LeaveExchangeAsync(pid, ct).ConfigureAwait(false); return (default, true); }

            if (craftCount > 1)
            {
                var cnt = await SendAndWaitForCodeAsync(pid, () => !ct.IsCancellationRequested && relay.Inject(pid, MoveForge.BuildCraftCountFramed(craftCount)),
                    new[] { "ExchangeCraftCountModifiedEvent", "ExchangeLeaveEvent" }, 3000,
                    p => p.Name == "ExchangeLeaveEvent" || PayloadReader.Int(p.Payload, FNum("ExchangeCraftCountModifiedEvent", "count"), -1) == craftCount, ct).ConfigureAwait(false);
                if (Closed("nombre de crafts")) return (default, true);
                if (cnt is null)
                {
                    TransportLog.Line($"[craft] pid {pid} : nombre de crafts x{craftCount} non confirmé -> abandon avant validation.");
                    await LeaveExchangeAsync(pid, ct).ConfigureAwait(false);
                    return (default, false);
                }
            }

            var resultPkt = await SendAndWaitForCodeAsync(pid, () => !ct.IsCancellationRequested && relay.Inject(pid, MoveForge.BuildCraftReadyFramed(stepTotal <= 0 ? 1 : stepTotal)),
                new[] { "ExchangeCraftResultEvent", "ExchangeLeaveEvent" }, 12000, ct).ConfigureAwait(false);
            InventoryReader.CraftResult? res = resultPkt?.Name == "ExchangeCraftResultEvent" ? InventoryReader.ReadCraftResult(resultPkt.Payload) : null;
            if (res is null && Closed("validation")) return (default, false);

            await LeaveExchangeAsync(pid, ct).ConfigureAwait(false);
            TransportLog.Line($"[craft] pid {pid} : craft x{craftCount} ({recipe.Count} ingrédient(s)) -> {CraftVerdict(res)}");
            if (res is not { Made: true } r)
                // Après validation, un résultat absent peut cacher un craft effectué : jamais de renvoi automatique.
                return (new CraftOutcome(false, 0, res?.Gid ?? 0), false);

            // L'objet créé arrive dans le modèle d'inventaire APRÈS le résultat (snapshot du client réel à la fermeture, ou
            // ajout/quantité unitaire ou en lot en mode Socket) : attente BORNÉE pour qu'un getInventoryItemCount lu juste
            // après le retour soit à jour. Diagnostic seulement si l'objet n'apparaît pas (le craft reste réussi).
            if (r.Gid > 0 && r.Qty > 0)
            {
                int before = bagBefore.Where(i => i.Gid == r.Gid).Sum(i => i.Qty);
                if (!await WaitBagCountAsync(pid, r.Gid, before + r.Qty, 1500, ct).ConfigureAwait(false))
                    TransportLog.Line($"[craft] pid {pid} : {ItemDisplayName(r.Gid)} pas encore visible dans le sac (compte {Inventory.CountByGid(pid, r.Gid)}, attendu ≥ {before + r.Qty}).");
            }
            return (new CraftOutcome(true, r.Qty, r.Gid), false);
        }
        finally
        {
            PacketDecoded -= OnCraftPkt;
            if (ct.IsCancellationRequested && openSeen.Task.IsCompleted && !closed.Task.IsCompleted)
                await LeaveExchangeAsync(pid, CancellationToken.None).ConfigureAwait(false);
        }
    }

    // An unrelated item update (or the other side of a trade) is not our placement receipt.
    private bool WorkshopPlacementAck(DecodedPacket packet, int uid, int quantity)
    {
        if (packet.Name is "ExchangeErrorEvent" or "ExchangeLeaveEvent") return true;
        bool added = packet.Name == "ExchangeObjectsAddedEvent";
        if (!added && packet.Name != "ExchangeObjectsModifiedEvent") return false;
        int remote = added ? FNum("ExchangeObjectsAddedEvent", "remote") : FNum("ExchangeObjectsModifiedEvent", "remote");
        int objects = added ? FNum("ExchangeObjectsAddedEvent", "objects") : FNum("ExchangeObjectsModifiedEvent", "objects");
        if (remote <= 0 || objects <= 0 || PayloadReader.Bool(packet.Payload, remote)) return false;
        if (InventoryReader.ReadContentAt(packet.Payload, objects).Any(i => i.Uid == uid && i.Qty == quantity)) return true;
        // Accept either decoded object representation, always checking its own UID and quantity.
        int uidField = FNum("ObjectItem", "uid"), quantityField = FNum("ObjectItem", "quantity");
        return uidField > 0 && quantityField > 0 && packet.Payload.Any(f => f.Field == objects && f.Message is { } item
            && PayloadReader.Int(item, uidField, -1) == uid && PayloadReader.Int(item, quantityField, -1) == quantity);
    }

    /// <summary>Texte de journal d'un résultat de craft (aucun code filaire) : « OK : 100 × Potion de Souvenir », « délai
    /// dépassé », « recette invalide (résultat vide) », « craft raté », « craft interdit », « résultat neutre ».</summary>
    private string CraftVerdict(InventoryReader.CraftResult? res) => res switch
    {
        null => "délai dépassé (aucun résultat)",
        { Made: true } r => r.Gid > 0 ? $"OK : {r.Qty} × {ItemDisplayName(r.Gid)}" : "OK",
        { Code: InventoryReader.CraftImpossible } => "recette invalide (résultat vide)",
        { Code: InventoryReader.CraftFailed } => "craft raté",
        { Code: InventoryReader.CraftForbidden } => "craft interdit",
        { Code: InventoryReader.CraftNeutral } => "résultat neutre (aucun objet)",
        { Code: var c } => $"résultat inconnu ({c})",
    };

    /// <summary>Attend (≤ timeoutMs) que le modèle d'inventaire compte au moins `atLeast` exemplaires du gid. false si
    /// l'échéance passe (ou annulation).</summary>
    private async Task<bool> WaitBagCountAsync(uint pid, long gid, int atLeast, int timeoutMs, CancellationToken ct)
    {
        long end = Environment.TickCount64 + timeoutMs;
        while (Inventory.CountByGid(pid, gid) < atLeast)
        {
            if (Environment.TickCount64 >= end) return false;
            try { await Task.Delay(50, ct).ConfigureAwait(false); } catch { return false; }
        }
        return true;
    }

    /// <summary>Ferme un échange ouvert (établi de craft OU coffre banque) : DialogLeaveRequest + attente ExchangeLeaveEvent (best-effort).</summary>
    private async Task LeaveExchangeAsync(uint pid, CancellationToken ct = default)
    {
        var relay = RelayForInject(capExempt: true);   // sortie propre d'un échange, même pour un perso capé
        if (relay is null) return;
        try { await SendAndWaitForCodeAsync(pid, () => !ct.IsCancellationRequested && relay.Inject(pid, MoveForge.BuildDialogLeaveFramed()), new[] { "ExchangeLeaveEvent" }, 2500, ct).ConfigureAwait(false); }
        catch { /* best-effort */ }
    }
}
