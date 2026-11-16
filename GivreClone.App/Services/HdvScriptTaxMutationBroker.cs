using System;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

/// <summary>
/// Immutable script admission captured from the observed character/server and
/// its configured HDV cap. This is a local admission identity, not a transport
/// generation or server-issued tax receipt.
/// </summary>
public readonly record struct HdvScriptTaxScope(uint Pid, string Profile,
    long MaxTaxSpend, float TaxRate);

/// <summary>
/// The actual Lua-to-LaunchService mutation boundary. Pins the policy before
/// persisting intent; checks it again after durable Attempted and directly
/// before the sender. If the session/policy changed, the result stays
/// Uncertain; no new order is guessed or resent.
/// </summary>
public static class HdvScriptTaxMutationBroker
{
    public static Task<HdvTaxedMutationFlow.Result> ExecuteAsync(
        HdvTaxReservations ledger, HdvScriptTaxScope scope, string orderKey,
        long price, Func<bool> isStillCurrent,
        Func<CancellationToken, Task<bool>> sendAndVerify, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(isStillCurrent);
        ArgumentNullException.ThrowIfNull(sendAndVerify);
        if (scope.Pid == 0 || scope.MaxTaxSpend < 0 ||
            string.IsNullOrWhiteSpace(scope.Profile) ||
            !scope.Profile.StartsWith("server:", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(orderKey) || price <= 0)
            return Task.FromResult(new HdvTaxedMutationFlow.Result(
                HdvTaxOrderReservation.Unavailable, false));
        return HdvTaxedMutationFlow.ExecuteAsync(ledger, scope.Pid, scope.Profile,
            orderKey, scope.MaxTaxSpend, 0, price, scope.TaxRate,
            async token =>
            {
                token.ThrowIfCancellationRequested();
                if (!isStillCurrent()) return false;
                return await sendAndVerify(token).ConfigureAwait(false);
            }, ct);
    }
}
