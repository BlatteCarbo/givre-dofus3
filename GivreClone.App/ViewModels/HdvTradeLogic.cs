using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.ViewModels;

/// <summary>Un lot achetable : taille, prix du lot, autorisé par l'utilisateur, prix maximum (0 = aucun), et seuil : le lot
/// n'est acheté que s'il manque au moins <see cref="MinMissing"/> objets (0 ou &gt;= taille = la taille du lot, jamais de surplus ;
/// plus bas, un surplus est accepté : x1000 dès qu'il en manque 800).</summary>
public readonly record struct LotQuote(int Qty, long Price, bool Allowed, long Max, int MinMissing = 0)
{
    public int Threshold => MinMissing > 0 && MinMissing < Qty ? MinMissing : Qty;
}

/// <summary>Résultat de planification d'un achat : lots à acheter (taille → nombre), coût total, reste non couvert.</summary>
public sealed record BuyPlan(IReadOnlyList<(int Qty, int Count, long LotPrice)> Steps, long Cost, int Uncovered)
{
    public int Covered => Steps.Sum(s => s.Qty * s.Count);
    /// <summary>« 2×x100 + 3×x10 » ; plan vide → chaîne vide.</summary>
    public string Text => string.Join(" + ", Steps.Select(s => $"{s.Count}×x{s.Qty}"));
}

/// <summary>
/// Calculs purs de l'onglet HDV (achat, relist, scan). Noms de fonctions issus des membres de l'original v1.3.1 (MZP23) ;
/// les règles sont des choix documentés ici, à confirmer en jeu — l'original n'est pas lisible à ce niveau.
/// </summary>
public static class HdvTradeLogic
{
    /// <summary>
    /// Glouton : du plus grand lot au plus petit, parmi les lots AUTORISÉS dont le prix connu (&gt; 0) ne dépasse pas le
    /// maximum, jusqu'à couvrir <paramref name="toBuy"/> ; arrête dès que <paramref name="budget"/> (&gt; 0) serait dépassé.
    /// Un lot plus grand que le reste à couvrir n'est acheté que si le reste atteint son seuil (<see cref="LotQuote.MinMissing"/>,
    /// la taille du lot par défaut : pas de sur-achat). Budget ≤ 0 = illimité.
    /// </summary>
    public static BuyPlan Plan(int toBuy, IEnumerable<LotQuote> lots, long budget)
    {
        var steps = new List<(int, int, long)>();
        long cost = 0;
        int remaining = Math.Max(0, toBuy);
        foreach (var lot in lots.Where(l => l.Allowed && l.Qty > 0 && l.Price > 0 && !HdvRowLogic.OverMax(l.Price, l.Max))
                                .OrderByDescending(l => l.Qty))
        {
            int count = remaining >= lot.Threshold ? (remaining - lot.Threshold) / lot.Qty + 1 : 0;   // seuil = taille -> remaining / taille
            if (count <= 0) continue;
            if (budget > 0) count = (int)Math.Min(count, Math.Max(0, (budget - cost) / lot.Price));
            if (count <= 0) continue;
            steps.Add((lot.Qty, count, lot.Price));
            cost += count * lot.Price;
            remaining = Math.Max(0, remaining - count * lot.Qty);   // surplus accepté : rien ne reste à couvrir
        }
        return new BuyPlan(steps, cost, remaining);
    }

    /// <summary>
    /// Même couverture que <see cref="Plan(int, IEnumerable{LotQuote}, long)"/>, mais un lot plus petit remplace le plus gros
    /// lot encore admissible quand son prix unitaire est strictement moins cher de plus de <paramref name="cheaperUnitMarginPct"/>
    /// pour cent. 0 ou moins délègue au plan d'origine (les appelants existants ne changent pas). Marge 20 et écart 20 : le gros lot reste.
    /// </summary>
    public static BuyPlan Plan(int toBuy, IEnumerable<LotQuote> lots, long budget, int cheaperUnitMarginPct)
    {
        if (cheaperUnitMarginPct <= 0) return Plan(toBuy, lots, budget);
        int margin = Math.Min(cheaperUnitMarginPct, 10_000);
        var pool = lots.Where(l => l.Allowed && l.Qty > 0 && l.Price > 0 && !HdvRowLogic.OverMax(l.Price, l.Max)).ToList();
        var steps = new List<(int, int, long)>();
        long cost = 0;
        int remaining = Math.Max(0, toBuy);
        while (remaining > 0)
        {
            if (budget > 0 && cost >= budget) break;
            var eligible = pool.Where(l => remaining >= l.Threshold && (budget <= 0 || l.Price <= budget - cost)).ToList();
            if (eligible.Count == 0) break;
            var largest = eligible.OrderByDescending(l => l.Qty).First();
            LotQuote choice = largest;
            foreach (var lot in eligible)
            {
                if (lot.Qty >= largest.Qty || !CheaperByMoreThan(lot, largest, margin)) continue;
                int cmp = UnitCompare(lot, choice);
                if (cmp < 0 || (cmp == 0 && lot.Qty > choice.Qty)) choice = lot;
            }
            int count = remaining >= choice.Threshold ? (remaining - choice.Threshold) / choice.Qty + 1 : 0;
            if (count <= 0) break;
            if (budget > 0) count = (int)Math.Min(count, Math.Max(0, (budget - cost) / choice.Price));
            if (count <= 0) break;
            steps.Add((choice.Qty, count, choice.Price));
            cost += count * choice.Price;
            remaining = Math.Max(0, remaining - count * choice.Qty);
        }
        return new BuyPlan(steps, cost, remaining);
    }

    /// <summary>Vrai si le prix unitaire de <paramref name="smaller"/> est strictement sous celui de <paramref name="larger"/> de plus de <paramref name="marginPct"/> %.</summary>
    private static bool CheaperByMoreThan(LotQuote smaller, LotQuote larger, int marginPct)
    {
        if (marginPct <= 0 || smaller.Qty <= 0 || larger.Qty <= 0 || smaller.Price <= 0 || larger.Price <= 0) return false;
        try
        {
            long left = checked((larger.Price * smaller.Qty - smaller.Price * larger.Qty) * 100L);
            long right = checked((long)marginPct * larger.Price * smaller.Qty);
            return left > right;
        }
        catch (OverflowException) { return false; }
    }

    private static int UnitCompare(LotQuote a, LotQuote b)
    {
        try
        {
            long left = checked(a.Price * b.Qty);
            long right = checked(b.Price * a.Qty);
            return left.CompareTo(right);
        }
        catch (OverflowException) { return 0; }
    }

    /// <summary>Plafond de prix d'un lot déduit du prix moyen unitaire : moyenne × quantité × (100 + tolérance) / 100.</summary>
    public static long MaxFromAverage(long avgUnit, int qty, int tolerancePct)
        => avgUnit <= 0 || qty <= 0 ? 0 : avgUnit * qty * (100L + Math.Max(0, tolerancePct)) / 100;

    /// <summary>Plafond d'un lot déduit de son prix actuel : prix × (100 + marge) / 100 (0 si prix inconnu).</summary>
    public static long MaxFromPrice(long lotPrice, int marginPct)
        => lotPrice <= 0 ? 0 : lotPrice * (100L + Math.Max(0, marginPct)) / 100;

    /// <summary>Applique un pourcentage (100 = inchangé) à un montant ; 0 reste 0 ; jamais négatif.</summary>
    public static long Scale(long value, int percent) => value <= 0 ? 0 : Math.Max(0, value * Math.Max(0, percent) / 100);

    /// <summary>Prix minimum de relist déduit du marché : marché × pourcentage / 100 (0 si marché inconnu).</summary>
    public static long MinFromMarket(long market, int percent) => market <= 0 ? 0 : Math.Max(0, market * Math.Max(0, percent) / 100);

    /// <summary>
    /// Prix de remise en vente : marché − sous-cotation, jamais sous le minimum fixé (si &gt; 0). Marché inconnu (≤ 0) → 0 = ne pas
    /// remettre en vente (on ne vend pas à l'aveugle). Résultat toujours ≥ 1 quand il est non nul.
    /// </summary>
    public static long RelistPrice(long market, long minPrice, long undercut)
    {
        if (market <= 0) return 0;
        long p = market - Math.Max(0, undercut);
        if (minPrice > 0 && p < minPrice) p = minPrice;
        return Math.Max(1, p);
    }

    // ---------------- Scan ----------------
    public enum ScanSort { GainDesc, GainPctDesc, CostAsc, NameAsc }

    public readonly record struct ScanItem(string Name, long Cost, long Value, int Seen);

    public static long Gain(ScanItem s) => s.Value - s.Cost;
    public static double GainPercent(ScanItem s) => s.Cost <= 0 ? 0 : (s.Value - s.Cost) * 100.0 / s.Cost;

    /// <summary>Garde un résultat si le gain absolu ET le gain relatif atteignent les minimums (un minimum à 0 = pas de filtre).</summary>
    public static bool PassesScan(ScanItem s, long minGain, int minPct)
        => s.Cost > 0 && s.Value > 0 && Gain(s) >= minGain && GainPercent(s) >= minPct;

    public static IReadOnlyList<ScanItem> Sort(IEnumerable<ScanItem> items, ScanSort sort) => sort switch
    {
        ScanSort.GainPctDesc => items.OrderByDescending(GainPercent).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        ScanSort.CostAsc => items.OrderBy(i => i.Cost).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        ScanSort.NameAsc => items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        _ => items.OrderByDescending(Gain).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
    };

    /// <summary>Relevé jugé douteux : trop peu de relevés (&lt; 2) ou gain relatif aberrant (&gt; 300 %).</summary>
    public static bool IsSuspect(ScanItem s) => s.Seen < 2 || GainPercent(s) > 300;
}

/// <summary>Accès marché utilisé par l'exécuteur d'achat : prix du lot et achat. Adapté sur LaunchService côté application.</summary>
public interface IHdvBuyPort
{
    Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct);
    Task<bool> BuyAsync(long gid, int qty, long maxPrice, CancellationToken ct);
}

/// <summary>Une demande d'achat : objet, reste à couvrir, lots autorisés (prix connus ou non).</summary>
public sealed record BuyRequest(long Gid, int ToBuy, IReadOnlyList<LotQuote> Lots);

public sealed record BuyRunResult(int Bought, long Spent, int Uncovered, string Stop);

/// <summary>
/// Exécute un achat : relit le prix de chaque lot autorisé, planifie, puis achète lot par lot. Sécurités : jamais au-delà du budget
/// cumulé (&gt; 0 exigé sauf <c>ignoreBudget</c>), jamais sans plafond de prix sauf <c>ignorePrice</c>, arrêt au premier refus.
/// Le prix relu est de toute façon revérifié par le port à l'achat (plafond transmis).
/// </summary>
public static class HdvBuyRunner
{
    public static async Task<BuyRunResult> RunAsync(IHdvBuyPort port, BuyRequest req, long budget, bool ignorePrice, CancellationToken ct)
    {
        if (req.ToBuy <= 0) return new(0, 0, 0, "rien à acheter");
        if (budget <= 0) return new(0, 0, req.ToBuy, "budget non défini");

        var fresh = new List<LotQuote>();
        foreach (var l in req.Lots.Where(l => l.Allowed))
        {
            ct.ThrowIfCancellationRequested();
            long p = await port.LotPriceAsync(req.Gid, l.Qty, ct).ConfigureAwait(false);
            if (p > 0 && !ignorePrice && l.Max <= 0) continue;   // pas de plafond fixé : on n'achète pas à l'aveugle
            fresh.Add(l with { Price = p });
        }

        var plan = HdvTradeLogic.Plan(req.ToBuy, fresh, budget);
        int bought = 0; long spent = 0; string stop = plan.Steps.Count == 0 ? "aucun lot achetable" : "";
        foreach (var (qty, count, lotPrice) in plan.Steps)
        {
            long max = fresh.First(f => f.Qty == qty).Max;
            for (int i = 0; i < count; i++)
            {
                if (ct.IsCancellationRequested) { if (bought == 0) ct.ThrowIfCancellationRequested(); stop = "achat arrêté"; goto done; }
                if (spent + lotPrice > budget) { stop = "budget atteint"; goto done; }
                if (!await port.BuyAsync(req.Gid, qty, ignorePrice && max <= 0 ? lotPrice : max, ct).ConfigureAwait(false))
                { stop = "achat refusé"; goto done; }
                bought += qty; spent += lotPrice;
            }
        }
    done:
        return new(bought, spent, Math.Max(0, req.ToBuy - bought), stop);
    }

    /// <summary>
    /// Même exécution que <see cref="RunAsync(IHdvBuyPort, BuyRequest, long, bool, CancellationToken)"/>.
    /// <paramref name="cheaperUnitMarginPct"/> ≤ 0 appelle cette méthode d'origine. Au-dessus, le plan frais utilise la marge
    /// et le plafond transmis à l'achat est le prix frais planifié : on ne dépense jamais plus que le montant compté.
    /// <c>BuyAsync</c> vrai signifie un accusé serveur, pas une simple acceptation : le port HDV ne renvoie vrai que sur reçu.
    /// </summary>
    public static Task<BuyRunResult> RunAsync(IHdvBuyPort port, BuyRequest req, long budget, bool ignorePrice, CancellationToken ct, int cheaperUnitMarginPct)
        => cheaperUnitMarginPct <= 0
            ? RunAsync(port, req, budget, ignorePrice, ct)
            : RunWithMarginAsync(port, req, budget, ignorePrice, ct, cheaperUnitMarginPct);

    private static async Task<BuyRunResult> RunWithMarginAsync(IHdvBuyPort port, BuyRequest req, long budget, bool ignorePrice, CancellationToken ct, int cheaperUnitMarginPct)
    {
        if (req.ToBuy <= 0) return new(0, 0, 0, "rien à acheter");
        if (budget <= 0) return new(0, 0, req.ToBuy, "budget non défini");

        var fresh = new List<LotQuote>();
        foreach (var l in req.Lots.Where(l => l.Allowed))
        {
            ct.ThrowIfCancellationRequested();
            long p = await port.LotPriceAsync(req.Gid, l.Qty, ct).ConfigureAwait(false);
            if (p > 0 && !ignorePrice && l.Max <= 0) continue;
            fresh.Add(l with { Price = p });
        }

        var plan = HdvTradeLogic.Plan(req.ToBuy, fresh, budget, cheaperUnitMarginPct);
        int bought = 0; long spent = 0; string stop = plan.Steps.Count == 0 ? "aucun lot achetable" : "";
        foreach (var (qty, count, lotPrice) in plan.Steps)
        {
            long max = fresh.First(f => f.Qty == qty).Max;
            for (int i = 0; i < count; i++)
            {
                if (ct.IsCancellationRequested) { if (bought == 0) ct.ThrowIfCancellationRequested(); stop = "achat arrêté"; goto done; }
                if (spent + lotPrice > budget) { stop = "budget atteint"; goto done; }
                long ceiling = ignorePrice && max <= 0 ? lotPrice : max;
                if (lotPrice > 0 && ceiling > lotPrice) ceiling = lotPrice;
                if (!await port.BuyAsync(req.Gid, qty, ceiling, ct).ConfigureAwait(false))
                { stop = "achat refusé"; goto done; }
                bought += qty; spent += lotPrice;
            }
        }
    done:
        return new(bought, spent, Math.Max(0, req.ToBuy - bought), stop);
    }
}

/// <summary>Ingrédient ou ligne d'achat : gid, quantité, nom affiché.</summary>
public readonly record struct HdvComponent(long Gid, int Quantity, string Name);

/// <summary>Décomposition jusqu'aux objets sans recette. <see cref="HdvDecomposeResult.Rows"/> est vide si <see cref="HdvDecomposeResult.Overflow"/>.</summary>
public sealed record HdvDecomposeResult(IReadOnlyList<HdvComponent> Rows, int Expanded, bool CycleStopped, bool Overflow);

/// <summary>Effet déjà lu sur une offre (mêmes champs que l'effet d'objet connu). Sert à comparer l'offre affichée et l'offre fraîche.</summary>
public readonly record struct HdvListedEffect(int ActionId, int Value, int Min, int Max, int Const, string? Text, long ObjectGid);

public sealed record HdvFreshListing(int Uid, long Gid, long Price, IReadOnlyList<HdvListedEffect> Effects);

public sealed record HdvChosenBuyRequest(long Gid, int Uid, int Quantity, long ShownPrice, IReadOnlyList<HdvListedEffect> Effects, long BudgetRemaining);

public sealed record HdvChosenAdmit(bool Allowed, string Reason);

/// <summary><see cref="Confirmed"/> n'est vrai que si le port a renvoyé un accusé serveur. <see cref="Spent"/> reste 0 sinon.</summary>
public sealed record HdvChosenBuyResult(bool Confirmed, long Spent, string Reason);

public interface IHdvChosenListingPort
{
    Task<HdvFreshListing?> FreshAsync(long gid, int uid, int quantity, CancellationToken ct);
    long Kamas();
    Task<bool> ConfirmedBuyAsync(long gid, int uid, int quantity, long price, CancellationToken ct);
}

/// <summary>Décomposition locale d'une liste d'achat jusqu'aux objets sans recette. Une boucle de recette s'arrête et garde l'objet.</summary>
public static class HdvRecipeBreakdown
{
    public static HdvDecomposeResult DecomposeToRaw(IReadOnlyList<HdvComponent> rows, Func<long, IReadOnlyList<HdvComponent>?> recipe)
    {
        if (recipe is null) throw new ArgumentNullException(nameof(recipe));
        var totals = new Dictionary<long, (int Qty, string Name)>();
        var order = new List<long>();
        int expanded = 0;
        bool cycle = false, overflow = false;

        void Add(long gid, int qty, string name)
        {
            if (overflow || gid <= 0 || qty <= 0) return;
            if (totals.TryGetValue(gid, out var cur))
            {
                long sum = (long)cur.Qty + qty;
                if (sum > int.MaxValue) { overflow = true; return; }
                totals[gid] = ((int)sum, cur.Name.Length > 0 ? cur.Name : name ?? "");
            }
            else
            {
                totals[gid] = (qty, name ?? "");
                order.Add(gid);
            }
        }

        void Expand(long gid, int qty, string name, HashSet<long> path)
        {
            if (overflow || gid <= 0 || qty <= 0) return;
            if (!path.Add(gid)) { cycle = true; Add(gid, qty, name); return; }
            try
            {
                var ingredients = recipe(gid);
                var usable = ingredients?.Where(i => i.Gid > 0 && i.Quantity > 0).ToList();
                if (usable is null || usable.Count == 0) { Add(gid, qty, name); return; }
                expanded++;
                foreach (var ingredient in usable)
                {
                    long next;
                    try { next = checked((long)qty * ingredient.Quantity); }
                    catch (OverflowException) { overflow = true; return; }
                    if (next <= 0 || next > int.MaxValue) { overflow = true; return; }
                    Expand(ingredient.Gid, (int)next, string.IsNullOrEmpty(ingredient.Name) ? name : ingredient.Name, path);
                }
            }
            finally { path.Remove(gid); }
        }

        foreach (var row in rows ?? Array.Empty<HdvComponent>())
            Expand(row.Gid, row.Quantity, row.Name ?? "", new HashSet<long>());

        if (overflow) return new(Array.Empty<HdvComponent>(), expanded, cycle, true);
        var list = order.Select(gid => new HdvComponent(gid, totals[gid].Qty, totals[gid].Name)).ToList();
        return new(list, expanded, cycle, false);
    }
}

/// <summary>Achat d'une offre précise (uid + effets). Le prix frais doit être celui affiché, et seul un accusé serveur dépense le budget.</summary>
public static class HdvChosenListing
{
    public static HdvChosenAdmit Admit(HdvChosenBuyRequest request, HdvFreshListing? fresh, long kamas)
    {
        if (request.BudgetRemaining <= 0) return new(false, "budget atteint");
        if (request.Uid <= 0 || request.Gid <= 0) return new(false, "uid invalide");
        if (request.Quantity is not (1 or 10 or 100 or 1000)) return new(false, "quantité de lot invalide");
        if (request.ShownPrice <= 0) return new(false, "prix affiché nul");
        if (request.ShownPrice > request.BudgetRemaining) return new(false, "budget insuffisant");
        if (fresh is null || fresh.Uid != request.Uid || fresh.Gid != request.Gid) return new(false, "offre introuvable");
        if (fresh.Price != request.ShownPrice) return new(false, "le prix frais diffère du prix affiché");
        var shown = request.Effects ?? Array.Empty<HdvListedEffect>();
        var again = fresh.Effects ?? Array.Empty<HdvListedEffect>();
        if (!shown.SequenceEqual(again)) return new(false, "les effets ont changé");
        if (kamas >= 0 && kamas < request.ShownPrice) return new(false, "pas assez de kamas");
        return new(true, "");
    }

    public static async Task<HdvChosenBuyResult> BuyAsync(IHdvChosenListingPort port, HdvChosenBuyRequest request, CancellationToken ct)
    {
        if (port is null) throw new ArgumentNullException(nameof(port));
        var blocked = Admit(request, fresh: null, kamas: -1);
        if (blocked.Reason is "budget atteint" or "uid invalide" or "quantité de lot invalide" or "prix affiché nul" or "budget insuffisant")
            return new(false, 0, blocked.Reason);

        var fresh = await port.FreshAsync(request.Gid, request.Uid, request.Quantity, ct).ConfigureAwait(false);
        var admit = Admit(request, fresh, port.Kamas());
        if (!admit.Allowed) return new(false, 0, admit.Reason);

        ct.ThrowIfCancellationRequested();
        bool confirmed = await port.ConfirmedBuyAsync(request.Gid, request.Uid, request.Quantity, request.ShownPrice, ct).ConfigureAwait(false);
        return confirmed ? new(true, request.ShownPrice, "") : new(false, 0, "achat non confirmé");
    }
}
