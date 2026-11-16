using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.ViewModels;

namespace GivreClone.App.Services.Eco;

/// <summary>Limites saisies sur une ligne. Le prix payé n'est pas ici : il vient uniquement d'une observation de marché fraîche.</summary>
public sealed record ObservedLotOrder(long Gid, int Quantity, long MaxUnitPrice, long Budget);

/// <summary>Prix de lots déjà lus. L'index suit <see cref="MarketDeals.LotSizes"/> : 1, 10, 100, 1000. -1 ou 0 = taille non cotée.</summary>
public sealed record MarketObservation(string Source, long? AgeMs, IReadOnlyList<long> LotPrices);

/// <summary>Aucun achat, demande acceptée sans reçu, ou reçu serveur. Seul le reçu est une dépense.</summary>
public enum LotPurchaseEvidence
{
    None = 0,
    Accepted = 1,
    Receipt = 2,
}

/// <summary>Lecture du marché et achat d'un lot. L'orchestrateur ignore tout prix qui ne sort pas de <see cref="ReadAsync"/>.</summary>
public interface IObservedLotMarket
{
    Task<MarketObservation?> ReadAsync(long gid, CancellationToken ct);
    Task<LotPurchaseEvidence> PurchaseAsync(long gid, int quantity, long maxPrice, CancellationToken ct);
}

/// <summary>Avancement après un lot dont le reçu serveur a été reçu. Les montants sont ceux de l'observation, pas une saisie.</summary>
public readonly record struct ObservedLotProgress(int Bought, long Spent, int Remaining, long BudgetLeft);

public sealed record ObservedLotBuyResult(int Bought, long Spent, int Uncovered, string Stop);

/// <summary>
/// Achète lot par lot via <see cref="HdvBuyRunner"/> jusqu'à la quantité, le budget, un refus ou l'arrêt.
/// Chaque lot relit le marché. Un prix saisi, une moyenne ou une observation trop vieille n'achète rien.
/// <see cref="LotPurchaseEvidence.Accepted"/> n'est pas un achat : seul un reçu augmente le dépensé.
/// </summary>
public static class ObservedLotBuy
{
    public static async Task<ObservedLotBuyResult> RunAsync(IObservedLotMarket market, ObservedLotOrder order,
        Action<ObservedLotProgress>? progress, CancellationToken ct)
    {
        if (market is null) throw new ArgumentNullException(nameof(market));
        if (order.Quantity <= 0) return new(0, 0, 0, "rien à acheter");
        if (order.Budget <= 0) return new(0, 0, order.Quantity, "budget non défini");
        if (order.MaxUnitPrice <= 0) return new(0, 0, order.Quantity, "prix unitaire max absent");
        if (order.Gid <= 0) return new(0, 0, order.Quantity, "prix non observé");

        int bought = 0;
        long spent = 0;
        try
        {
            while (bought < order.Quantity)
            {
                ct.ThrowIfCancellationRequested();
                long budgetLeft = BudgetLeft(order.Budget, spent);
                if (budgetLeft <= 0) return Done(order, bought, spent, "budget atteint");

                var observation = await market.ReadAsync(order.Gid, ct).ConfigureAwait(false);
                if (!IsFresh(observation)) return Done(order, bought, spent, "prix non observé");

                var quotes = Quotes(observation!, order.MaxUnitPrice);
                if (quotes.Count == 0)
                    return Done(order, bought, spent, observation!.LotPrices.Any(p => p > 0) ? "aucun lot achetable" : "prix non observé");

                int remaining = order.Quantity - bought;
                var plan = HdvTradeLogic.Plan(remaining, quotes, budgetLeft);
                if (plan.Steps.Count == 0)
                    return Done(order, bought, spent, quotes.Any(q => q.Price <= budgetLeft) ? "aucun lot achetable" : "budget atteint");

                var quote = quotes.First(q => q.Qty == plan.Steps[0].Qty);
                var step = await HdvBuyRunner.RunAsync(new PricedPort(market, quote),
                    new BuyRequest(order.Gid, quote.Qty, new[] { quote }), budgetLeft, false, ct).ConfigureAwait(false);
                if (step.Bought <= 0 || step.Spent <= 0)
                    return Done(order, bought, spent, string.IsNullOrEmpty(step.Stop) ? "achat refusé" : step.Stop);

                bought += step.Bought;
                spent += step.Spent;
                progress?.Invoke(new ObservedLotProgress(bought, spent, Math.Max(0, order.Quantity - bought), BudgetLeft(order.Budget, spent)));
                if (bought >= order.Quantity) return Done(order, bought, spent, "quantité atteinte");
                if (spent > order.Budget || step.Stop == "budget atteint") return Done(order, bought, spent, "budget atteint");
                if (!string.IsNullOrEmpty(step.Stop)) return Done(order, bought, spent, step.Stop);
            }
            return Done(order, bought, spent, "quantité atteinte");
        }
        catch (OperationCanceledException)
        {
            return Done(order, bought, spent, "arrêté");
        }
    }

    private static bool IsFresh(MarketObservation? observation)
        => observation is { AgeMs: >= 0, LotPrices: not null }
           && PriceAlerts.IsFreshObservation(observation.Source, observation.AgeMs);

    /// <summary>Un lot est retenu seulement si son prix observé ne dépasse pas le plafond unitaire. Le plafond transmis à l'achat est ce prix observé.</summary>
    private static List<LotQuote> Quotes(MarketObservation observation, long maxUnit)
    {
        var quotes = new List<LotQuote>();
        int n = Math.Min(MarketDeals.LotSizes.Length, observation.LotPrices.Count);
        for (int i = 0; i < n; i++)
        {
            int size = MarketDeals.LotSizes[i];
            long price = observation.LotPrices[i];
            if (!WithinCeiling(price, size, maxUnit)) continue;
            quotes.Add(new LotQuote(size, price, true, price));
        }
        return quotes;
    }

    private static bool WithinCeiling(long price, int size, long maxUnit)
    {
        if (price <= 0 || size <= 0 || maxUnit <= 0) return false;
        if (maxUnit > long.MaxValue / size) return true;
        return price <= maxUnit * size;
    }

    private static long BudgetLeft(long budget, long spent) => spent >= budget ? 0 : budget - spent;

    private static ObservedLotBuyResult Done(ObservedLotOrder order, int bought, long spent, string stop)
        => new(bought, spent, Math.Max(0, order.Quantity - bought), stop);

    /// <summary>Le runner relit le prix ici : on ne lui donne que le prix de l'observation, et on ne confirme que le reçu.</summary>
    private sealed class PricedPort : IHdvBuyPort
    {
        private readonly IObservedLotMarket _market;
        private readonly LotQuote _quote;
        public PricedPort(IObservedLotMarket market, LotQuote quote) { _market = market; _quote = quote; }

        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct)
            => Task.FromResult(qty == _quote.Qty ? _quote.Price : 0L);

        public async Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct)
        {
            if (qty != _quote.Qty || maxPrice != _quote.Price || _quote.Price <= 0) return false;
            var evidence = await _market.PurchaseAsync(gid, qty, maxPrice, ct).ConfigureAwait(false);
            return evidence == LotPurchaseEvidence.Receipt;
        }
    }
}
