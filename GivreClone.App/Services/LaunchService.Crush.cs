using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

// BRISAGE : InteractiveUse de la competence 181 (Briser des objets) -> ExchangeRecycleTradeStartedEvent,
// puis ExchangeObjectMoveRequest de toute la pile -> RecycleResultEvent. Aucun autre message n est invente :
// sans l evenement d ouverture, rien n est casse ; sans resultat, la suite s arrete.
public sealed partial class LaunchService
{
    public sealed record CrushRunReport(bool Opened, int Crushed, IReadOnlyList<int> Failed, int Remaining, string Stop);

    public async Task<CrushRunReport> CrushAsync(uint pid, int elementId, int skillUid, IReadOnlyList<(int Uid, int Qty)> items, CancellationToken ct = default)
    {
        var failed = new List<int>();
        CrushRunReport End(bool opened, int crushed, int remaining, string stop) => new(opened, crushed, failed, remaining, stop);
        if (ct.IsCancellationRequested) return End(false, 0, items.Count, "annulé");
        if (items.Count == 0) return End(false, 0, 0, "liste vide");
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return End(false, 0, items.Count, "permission");
        var relay = RelayForInject();
        if (relay is null) return End(false, 0, items.Count, "relay");

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var open = false;
        void OnPkt(DecodedPacket p)
        {
            if (p.Pid != pid || p.ClientToServer) return;
            if (p.Name == "ExchangeRecycleTradeStartedEvent") open = true;
            else if (p.Name == "ExchangeLeaveEvent" && open) closed.TrySetResult();
        }
        PacketDecoded += OnPkt;
        try
        {
            var started = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildInteractiveUseFramed(skillUid, elementId)),
                new[] { "ExchangeRecycleTradeStartedEvent" }, 5000, ct).ConfigureAwait(false);
            if (started is null) return End(false, 0, items.Count, "concasseur non ouvert");

            int crushed = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (ct.IsCancellationRequested) return End(true, crushed, items.Count - i, "annulé");
                if (closed.Task.IsCompleted) return End(true, crushed, items.Count - i, "concasseur fermé");
                var (uid, qty) = items[i];
                if (qty <= 0 || !ItemCanCrush(pid, uid)) { failed.Add(uid); continue; }
                var res = await SendAndWaitForCodeAsync(pid, () => relay.Inject(pid, MoveForge.BuildExchangeObjectMoveFramed(uid, qty)),
                    new[] { "RecycleResultEvent", "ExchangeErrorEvent", "ExchangeLeaveEvent" }, 8000, ct).ConfigureAwait(false);
                if (res is null) { failed.Add(uid); return End(true, crushed, items.Count - i - 1, "délai dépassé");}
                if (res.Name == "ExchangeLeaveEvent") { failed.Add(uid); return End(true, crushed, items.Count - i - 1, "concasseur fermé"); }
                if (res.Name == "ExchangeErrorEvent") { failed.Add(uid); continue; }
                crushed++;
            }
            return End(true, crushed, 0, crushed == items.Count ? "terminé" : "objets refusés");
        }
        finally
        {
            PacketDecoded -= OnPkt;
            if (open) await LeaveExchangeAsync(pid, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
