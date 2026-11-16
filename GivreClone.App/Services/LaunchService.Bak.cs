using System;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Bak;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    public async Task<(bool Success, string Message, long KamasSpent)> ExecuteBakPurchaseAsync(
        uint pid,
        int ogrinesAmount,
        int maxAcceptableRate = 0,
        CancellationToken ct = default)
    {
        if (pid == 0 || ogrinesAmount <= 0)
            return (false, "Aucun bot connecté sélectionné.", 0);
        if (IsInFight(pid))
            return (false, "Impossible d'acheter des Ogrines pendant un combat.", 0);
        var relay = RelayForInject();
        if (relay is null)
            return (false, "Relais réseau indisponible pour ce personnage.", 0);

        int serverId = ServerIdOf(pid);
        int rate = BakSubscriptionService.GetRateForServer(serverId);
        if (maxAcceptableRate > 0 && rate > maxAcceptableRate)
            return (false, $"Taux actuel ({rate} k/u) supérieur au plafond autorisé ({maxAcceptableRate} k/u).", 0);

        long expectedKamas = (long)rate * ogrinesAmount;
        long bagKamas = Kamas(pid);
        long bankKamas = BankKamas(pid);
        long availableKamas = (bagKamas >= 0 ? bagKamas : 0) + (bankKamas >= 0 ? bankKamas : 0);
        if (bagKamas >= 0 && availableKamas < expectedKamas)
            return (false, $"Kamas insuffisants : {availableKamas:N0} k disponibles pour {expectedKamas:N0} k requis.", 0);

        string? listCode = _catalog?.CodeForName("BakBufferListRequest");
        string? actionCode = _catalog?.CodeForName("BakActionRequest");
        string? validCode = _catalog?.CodeForName("BakTransactionValidationRequest");
        if (string.IsNullOrEmpty(actionCode) || string.IsNullOrEmpty(validCode))
            return (false, "Codes protocolaires BAK absents du catalogue actif.", 0);

        if (!string.IsNullOrEmpty(listCode))
        {
            byte[] listFrame = BakSubscriptionService.BuildBakBufferListFrame(listCode);
            if (listFrame.Length > 0) relay.Inject(pid, listFrame);
        }

        var actionTcs = new TaskCompletionSource<(long LockedKamas, string Uuid, int ServerRate)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var buyConfirmedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnPacket(DecodedPacket p)
        {
            if (p.Pid != pid || p.ClientToServer || p.Name is null) return;
            if (p.Name == "BakActionEvent")
            {
                long lockedKamas = PayloadReader.Int(p.Payload, 1, 0);
                string uuid = PayloadReader.Str(p.Payload, 2) ?? "";
                int srvRate = (int)PayloadReader.Int(p.Payload, 6, rate);
                if (!string.IsNullOrWhiteSpace(uuid))
                    actionTcs.TrySetResult((lockedKamas > 0 ? lockedKamas : expectedKamas, uuid, srvRate > 0 ? srvRate : rate));
            }
            else if (p.Name is "BakBuyValidationEvent" or "AccountInformationUpdateEvent")
            {
                if (p.Name == "AccountInformationUpdateEvent")
                {
                    long expMs = PayloadReader.Int(p.Payload, 1, 0);
                    string? charName = SelfCharacterInfo(pid)?.Name;
                    if (expMs > 0 && !string.IsNullOrEmpty(charName))
                        BakSubscriptionService.UpdateExpiration(charName, expMs);
                }
                buyConfirmedTcs.TrySetResult(true);
            }
        }

        PacketDecoded += OnPacket;
        try
        {
            byte[] actionFrame = BakSubscriptionService.BuildBakActionFrame(actionCode, rate, ogrinesAmount);
            if (actionFrame.Length == 0 || !relay.Inject(pid, actionFrame))
                return (false, "Échec d'injection de BakActionRequest.", 0);

            ShowInjected(pid, actionFrame);
            TransportLog.Line($"[bak] pid {pid} : BakActionRequest envoyé ({ogrinesAmount} Ogrines @ {rate} k = {expectedKamas:N0} k)");

            var completedAction = await Task.WhenAny(actionTcs.Task, Task.Delay(6000, ct)).ConfigureAwait(false);
            if (completedAction != actionTcs.Task)
                return (false, "Délai dépassé en attente de BakActionEvent du serveur.", 0);

            var (lockedKamas, uuid, serverRate) = await actionTcs.Task.ConfigureAwait(false);
            if (serverId > 0 && serverRate > 0)
                BakSubscriptionService.SetServerRate(serverId, serverRate);

            if (maxAcceptableRate > 0 && serverRate > maxAcceptableRate)
                return (false, $"Offre serveur refusée : taux {serverRate} k/u > plafond {maxAcceptableRate} k/u.", 0);

            byte[] validFrame = BakSubscriptionService.BuildBakValidationFrame(validCode, uuid);
            if (validFrame.Length == 0 || !relay.Inject(pid, validFrame))
                return (false, "Échec d'injection de BakTransactionValidationRequest.", 0);

            ShowInjected(pid, validFrame);
            TransportLog.Line($"[bak] pid {pid} : BakTransactionValidationRequest envoyé (uuid={uuid}, kamas={lockedKamas:N0})");

            var completedBuy = await Task.WhenAny(buyConfirmedTcs.Task, Task.Delay(8000, ct)).ConfigureAwait(false);
            if (completedBuy != buyConfirmedTcs.Task)
                return (false, "Validation envoyée mais BakBuyValidationEvent non confirmé dans les 8 s.", 0);

            return (true, $"Achat confirmé par le serveur : {ogrinesAmount} Ogrines pour {lockedKamas:N0} kamas ({serverRate} k/u).", lockedKamas);
        }
        finally
        {
            PacketDecoded -= OnPacket;
        }
    }
}
