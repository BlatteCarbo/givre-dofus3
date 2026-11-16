using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

/// <summary>Issue d'un transfert vers la mule : conclu ou non, piles et kamas posés, raison lisible en cas d'échec.</summary>
public sealed record MuleOutcome(bool Done, int Stacks, long Kamas, string Reason);

// ===================== Mule en un clic : un bot donne son sac à un autre bot de l'app =====================
// Les deux côtés sont pilotés ici (aucun script requis) : le donneur demande l'échange, la mule accepte, le donneur pose ses piles
// (sauf objets gardés et équipement porté), la mule se déclare prête dès que le donneur l'est, échange conclu (ExchangeLeaveEvent
// success). Un seul verbe BLOQUANT par pid (le donneur : demande puis « prêt ») ; la mule n'utilise que des envois non bloquants,
// jamais SendAndWaitForCodeAsync -> aucune attente de script écrasée chez elle. Mêmes chemins en MITM et en Socket.
public sealed partial class LaunchService
{
    /// <summary>Donne le sac de <paramref name="giver"/> à <paramref name="mule"/> (même carte requise). <paramref name="keep"/> : gids
    /// jamais donnés. <paramref name="kamas"/> : kamas à poser (0 = aucun). Gaté Perm.Gather + Perm.Multi (deux comptes pilotés).</summary>
    public async Task<MuleOutcome> MuleTransferAsync(uint giver, uint mule, IReadOnlyCollection<long> keep, long kamas, CancellationToken ct)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather) || !LicenseService.HasPermission(LicenseService.Perm.Multi))
            return new(false, 0, 0, "noperm");
        if (giver == mule) return new(false, 0, 0, "same");
        long giverId = CharacterId(giver), muleId = CharacterId(mule);
        if (giverId == 0 || muleId == 0) return new(false, 0, 0, "identity");
        if ((CurrentMap(giver) ?? 0) == 0 || CurrentMap(giver) != CurrentMap(mule)) return new(false, 0, 0, "map");
        if (IsInFight(giver) || IsInFight(mule)) return new(false, 0, 0, "fight");
        if (OpenInteraction(giver) is not null) LeaveDialog(giver);
        if (OpenInteraction(mule) is not null) LeaveDialog(mule);

        var stacks = Inventory.Items(giver).Where(i => i.Position == 63 && i.Qty > 0 && !keep.Contains(i.Gid) && ItemCanTrade(giver, i.Uid)).ToList();
        long giveKamas = Math.Clamp(kamas, 0, Math.Max(0, Kamas(giver)));
        if (stacks.Count == 0 && giveKamas == 0) return new(false, 0, 0, "empty");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // MULE : accepte la demande du donneur dès qu'elle arrive (non bloquant, comme le mode mule d'équipe).
        var accept = Task.Run(async () =>
        {
            long until = Environment.TickCount64 + 15_000;
            while (Environment.TickCount64 < until && !cts.IsCancellationRequested)
            {
                if (TradePending(mule) is { } p && p.SourceId == giverId)
                {
                    await Task.Delay(Random.Shared.Next(500, 1100), cts.Token).ConfigureAwait(false);
                    TradeAccept(mule);
                    return;
                }
                await Task.Delay(120, cts.Token).ConfigureAwait(false);
            }
        }, cts.Token);

        Task muleReady = Task.CompletedTask;
        try
        {
            TransportLog.Line($"[mule] {giver} -> {mule} : {stacks.Count} pile(s), {giveKamas} kamas");
            var opened = await TradeWithPlayerAsync(giver, muleId, 15_000, cts.Token).ConfigureAwait(false);
            if (opened != true) return new(false, 0, 0, opened is null ? "noanswer" : "refused");

            int put = 0;
            foreach (var st in stacks)
            {
                cts.Token.ThrowIfCancellationRequested();
                if (!TradeOpen(giver)) return new(false, put, 0, "closed");
                if (TradePutUid(giver, st.Uid, 0)) put++;
                await Task.Delay(Random.Shared.Next(180, 380), cts.Token).ConfigureAwait(false);   // rythme humain
            }
            if (giveKamas > 0 && TradeOpen(giver)) { TradePutKamas(giver, giveKamas); await Task.Delay(400, cts.Token).ConfigureAwait(false); }

            // MULE : prête dès que le donneur l'est (le contenu ne bouge plus) — non bloquant.
            muleReady = Task.Run(async () =>
            {
                long until = Environment.TickCount64 + 20_000;
                while (Environment.TickCount64 < until && !cts.IsCancellationRequested && TradeOpen(mule))
                {
                    if (TradeView(mule) is { TheyReady: true, MeReady: false })
                    {
                        await Task.Delay(Random.Shared.Next(500, 1000), cts.Token).ConfigureAwait(false);
                        TradeSetReady(mule, true);
                        return;
                    }
                    await Task.Delay(120, cts.Token).ConfigureAwait(false);
                }
            }, cts.Token);

            var done = await TradeReadyAndWaitAsync(giver, 20_000, cts.Token).ConfigureAwait(false);
            TransportLog.Line($"[mule] {giver} -> {mule} : {(done == true ? "échange conclu" : done is null ? "pas de conclusion (délai)" : "échange annulé")}");
            if (done is null && TradeOpen(giver)) TradeCancel(giver);
            return done == true ? new(true, put, giveKamas, "") : new(false, put, 0, done is null ? "timeout" : "cancelled");
        }
        catch (OperationCanceledException) { if (TradeOpen(giver)) TradeCancel(giver); return new(false, 0, 0, "cancelled"); }
        finally
        {
            try { cts.Cancel(); } catch { }
            try { await accept.ConfigureAwait(false); } catch { }
            try { await muleReady.ConfigureAwait(false); } catch { }
        }
    }
}
