using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Eco;
using GivreClone.Engine.Recovery;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// FORGEMAGIE : exécute en jeu les décisions de ForgeEngine. Séquence relevée sur le client officiel le 23/10 (Joaillomager, 45 runes) :
// interactif d'atelier -> ExchangeCraftStartedEvent ; objet posé (ExchangeObjectMoveRequest { uid, 1 }) -> ExchangeObjectsAddedEvent ;
// puis pour CHAQUE rune un ExchangeObjectMoveAndSetReadyRequest { uid rune, 1, prêt } -> ExchangeCraftResultEvent { code (1 échec,
// 2 succès), description { reliquat #3, objet #4 avec ses effets } }. L'objet reste dans l'atelier entre deux runes.
public sealed partial class LaunchService
{
    public sealed record ForgeStepReport(int Step, string Rune, int Code, IReadOnlyDictionary<string, int> Stats, int? Pool);
    public sealed record ForgeRunReport(bool Opened, string Stop, IReadOnlyList<ForgeStepReport> Steps);

    private Dictionary<long, (int EffectId, string Tier, string Name)>? _runeItems;

    /// <summary>Runes du jeu par gid (nom « Rune [Pa|Ra] Code » du catalogue retrouvé EXACTEMENT dans la base du jeu) : famille et palier.
    /// Vide tant que la base du jeu n'est pas lisible (non mis en cache dans ce cas).</summary>
    public IReadOnlyDictionary<long, (int EffectId, string Tier, string Name)> RuneItemsByGid()
    {
        if (_runeItems is not null) return _runeItems;
        if (GameData is not { } db) return new Dictionary<long, (int, string, string)>();
        var map = new Dictionary<long, (int, string, string)>();
        foreach (var f in RuneCatalog.Families)
            foreach (var tier in RuneCatalog.Tiers)
            {
                string name = RuneCatalog.ItemName(f, tier);
                // ItemGidByName retombe sur un nom « contenant » : on n'accepte que l'égalité (Rune Fo ≠ Rune Do Feu…).
                if (db.ItemGidByName(name) is { } gid && string.Equals(db.ItemName(gid), name, StringComparison.OrdinalIgnoreCase))
                    map[gid] = (f.EffectId, tier, name);
            }
        if (map.Count > 0) _runeItems = map;
        return map;
    }

    /// <summary>UID d'un objet du SAC donné par son uid ou, à défaut, par son gid (premier exemplaire du sac). Null si absent.</summary>
    public int? BagUidForForge(uint pid, long uidOrGid)
        => Inventory.Items(pid).Any(i => i.Uid == uidOrGid && i.Position == 63) ? (int)uidOrGid : Inventory.FirstBagUidByGid(pid, uidOrGid);

    /// <summary>Forgemage `itemUid` (objet du sac) à l'établi (elementId, skillUid) en suivant `decide` (état de l'objet + puits ->
    /// action). S'arrête sur Terminer/Sauter, rune introuvable ou absente du sac, fermeture de l'établi, délai, `maxSteps` ou annulation.
    /// Une rune ratée ou neutre ne s'arrête pas : le moteur redécide sur l'état suivant. Gaté Perm.Gather (consomme des runes).</summary>
    public async Task<ForgeRunReport> ForgeAsync(uint pid, int elementId, int skillUid, int itemUid,
        Func<IReadOnlyDictionary<string, int>, int?, ForgeAction> decide, int maxSteps, CancellationToken ct = default,
        Action<ForgeStepReport>? onStep = null, Guid? historySessionId = null)
    {
        var steps = new List<ForgeStepReport>();
        ForgeRunReport End(bool opened, string stop) => new(opened, stop, steps);
        if (ct.IsCancellationRequested) return End(false, "annulé");
        if (maxSteps <= 0) return End(false, "limite d'étapes");
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return End(false, "permission");
        var relay = RelayForInject();
        if (relay is null) return End(false, "relay");
        if (!GivreClone.Engine.Recovery.ItemBindingRules.CanForge(ItemBindingOf(pid, itemUid))) return End(false, "objet lié");

        var sessionId = historySessionId ?? BeginForgeHistorySession(pid,
            ScriptAccounts.FirstOrDefault(a => a.Pid == pid)?.Id ?? "", source: "forge");
        string sessionStop = "erreur";
        ForgeRunReport Finish(bool opened, string stop)
        {
            sessionStop = stop;
            ForgeHistory.Record(sessionId, "support-stop", JobProgress(pid), itemUid: itemUid, reason: stop);
            return End(opened, stop);
        }
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var open = false;
        void OnPkt(DecodedPacket p)
        {
            if (p.Pid != pid || p.ClientToServer) return;
            if (p.Name == "ExchangeCraftStartedEvent") open = true;
            else if (p.Name == "ExchangeLeaveEvent" && open) closed.TrySetResult();
        }
        PacketDecoded += OnPkt;
        try
        {
            var started = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildInteractiveUseFramed(skillUid, elementId)),
                new[] { "ExchangeCraftStartedEvent" }, 5000, ct).ConfigureAwait(false);
            if (started is null) return Finish(false, "atelier non ouvert");

            string[] placeAcks = { "ExchangeObjectsAddedEvent", "ExchangeObjectsModifiedEvent", "ExchangeErrorEvent", "ExchangeLeaveEvent" };
            var itemAck = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildExchangeObjectMoveFramed(itemUid, 1)), placeAcks, 3000, p => WorkshopPlacementAck(p, itemUid, 1), ct).ConfigureAwait(false);
            if (itemAck is null || itemAck.Name is "ExchangeErrorEvent" or "ExchangeLeaveEvent") return Finish(true, "objet refusé");

            // Dernier état connu des effets : celui de l'objet posé, relu après chaque rune depuis le résultat.
            var stats = ForgeState.Stats(ItemEffects(pid, itemUid));
            ForgeHistory.Record(sessionId, "support", JobProgress(pid), itemUid: itemUid,
                itemGid: Inventory.Get(pid, itemUid)?.Gid, stats: stats);
            int? pool = null;
            for (int step = 1; step <= maxSteps; step++)
            {
                if (ct.IsCancellationRequested) return Finish(true, "annulé");
                if (closed.Task.IsCompleted) return Finish(true, "atelier fermé");
                var action = decide(stats, pool);
                if (action.Kind == ForgeActionKind.Finish) return Finish(true, "terminé");
                if (action.Kind == ForgeActionKind.Skip) return Finish(true, string.IsNullOrEmpty(action.Reason) ? "étape sautée" : "étape sautée : " + action.Reason);

                long? runeGid = RuneItemsByGid().Where(r => ForgeState.RuneItemNames(action.Rune)
                    .Contains(r.Value.Name, StringComparer.OrdinalIgnoreCase)).Select(r => (long?)r.Key).FirstOrDefault();
                if (runeGid is null) return Finish(true, $"rune inconnue : {action.Rune}");
                int? bagUid = Inventory.FirstBagUidByGid(pid, runeGid.Value);
                int? bankUid = Bank.IsSeeded(pid) ? Bank.Items(pid).Where(i => i.Gid == runeGid.Value && i.Qty > 0).Select(i => (int?)i.Uid).FirstOrDefault() : null;
                if (RuneStock.Uid(bagUid, bankUid) is not { } runeUid) return Finish(true, $"rune absente du sac : {action.Rune}");

                // Une rune = UN message, comme le client officiel (capture 23/10, 45 runes) : ExchangeObjectMoveAndSetReadyRequest
                // { uid de la rune, quantité 1, prêt }. Le serveur enchaîne ajout / quantité / retrait de la rune puis le résultat.
                var res = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildExchangeObjectMoveAndSetReadyFramed(runeUid, 1)),
                    new[] { "ExchangeCraftResultEvent", "ExchangeErrorEvent", "ExchangeLeaveEvent" }, 8000, ct).ConfigureAwait(false);
                if (res is null) return Finish(true, "délai dépassé");
                if (res.Name == "ExchangeErrorEvent") return Finish(true, "rune refusée");
                if (res.Name == "ExchangeLeaveEvent") return Finish(true, "atelier fermé");

                var m = InventoryReader.ReadMagicResult(res.Payload);
                if (m.Code is not (InventoryReader.CraftFailed or InventoryReader.CraftSuccess or InventoryReader.CraftNeutral))
                    return Finish(true, $"résultat refusé ou inconnu : {m.Code}");
                // Gid identifies the returned object even when it has lost every effect.
                // Without an object snapshot we cannot safely decide the next rune.
                if (!ConfirmForgeProgress(m, () => NoteProgress(pid))) return Finish(true, "résultat sans état d'objet");
                stats = ForgeState.Stats(m.Effects);
                pool = m.Pool;
                var report = new ForgeStepReport(step, action.Rune, m.Code, stats, pool);
                steps.Add(report);
                ForgeHistory.Record(sessionId, "rune", JobProgress(pid), itemUid: itemUid, itemGid: m.Gid,
                    rune: action.Rune, outcome: m.Code, stats: stats, pool: pool,
                    runeGid: runeGid.Value, quantity: 1,
                    stockUnitValue: AveragePrice(pid, (int)runeGid.Value) is > 0 and var value ? value : null);
                try { onStep?.Invoke(report); } catch { /* observateur d'interface : jamais bloquant */ }
                TransportLog.Line($"[forge] pid {pid} : étape {step} {action.Rune} -> code {m.Code}, puits {(pool?.ToString() ?? "?")}.");
            }
            return Finish(true, "limite d'étapes");
        }
        catch (OperationCanceledException) { sessionStop = "annulé"; throw; }
        finally
        {
            PacketDecoded -= OnPkt;
            if (historySessionId is null) EndForgeHistorySession(sessionId, sessionStop);
            if (open) await LeaveExchangeAsync(pid, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
