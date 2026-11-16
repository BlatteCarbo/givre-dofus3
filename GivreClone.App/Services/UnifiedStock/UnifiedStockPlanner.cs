using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.UnifiedStock;

/// <summary>Source of a STOCK OBSERVATION, not a claim that the container can be opened.</summary>
public enum StockLocation { Inventory, Bank, HavenBag }
public enum StockConfidence { Fresh, Stale, Unverified, Unknown }

/// <summary>
/// A bank and a haven-bag chest may refer to the same underlying storage:
/// storageIdentity is stable across aliases and MUST be independently identified.
/// Blank identities are never aggregatable for non-bag sources.
/// </summary>
public sealed record StockObservation(
    string AccountKey, long CharacterId, StockLocation Location, string StorageIdentity,
    string ObservationId, DateTimeOffset ObservedAt, string GameVersion,
    StockConfidence Confidence, bool CompleteSnapshot,
    IReadOnlyList<StockStack> Stacks);

public sealed record StockStack(long ItemGid, string StackUid, long Quantity, bool Reserved = false);
public sealed record StockAllocation(long ItemGid, long Needed, long FromInventory,
    long FromBank, long FromHavenBag, long Missing, long UnknownStorage,
    IReadOnlyList<string> Warnings);
public sealed record UnifiedStockResult(IReadOnlyList<StockAllocation> Items,
    bool AllSourcesFresh, IReadOnlyList<string> Warnings);

/// <summary>
/// Deterministic read-only allocation shared by all item/set targets.
/// Never treats a missed bank/Havre-Sac packet as an empty store; never
/// trusts a manual location label over an observed, complete snapshot.
/// </summary>
public static class UnifiedStockPlanner
{
    public static UnifiedStockResult Allocate(
        string accountKey, long characterId, string gameVersion,
        DateTimeOffset now, IReadOnlyList<StockObservation> observations,
        IReadOnlyDictionary<long, long> required, TimeSpan? maxAge = null)
    {
        if (string.IsNullOrWhiteSpace(accountKey) || accountKey.Length > 100
            || characterId <= 0 || string.IsNullOrWhiteSpace(gameVersion)
            || observations is null || required is null
            || observations.Count > 200 || required.Count > 10000)
            throw new ArgumentException("Invalid stock inputs.");
        TimeSpan age = maxAge ?? TimeSpan.FromMinutes(5);
        if (age <= TimeSpan.Zero || age > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(maxAge));
        foreach (var r in required)
            if (r.Key <= 0 || r.Value < 0 || r.Value > 1_000_000_000L)
                throw new ArgumentException("Invalid resource demand.");

        var warnings = new List<string>();
        var accepted = new List<StockObservation>();
        var groups = observations
            .Where(x => string.Equals(x.AccountKey, accountKey, StringComparison.Ordinal)
                && x.CharacterId == characterId
                && string.Equals(x.GameVersion, gameVersion, StringComparison.Ordinal))
            .ToArray();
        if (observations.Count > groups.Length)
            warnings.Add("Des stocks d'autres personnages/comptes/versions ont été ignorés.");
        foreach (var x in groups)
        {
            if (x.Stacks is null || x.Stacks.Count > 10000 || string.IsNullOrWhiteSpace(x.ObservationId)
                || x.ObservationId.Length > 128 || x.StorageIdentity.Length > 128
                || x.Stacks.Any(i => i.ItemGid <= 0 || i.Quantity < 0 || i.Quantity > 1_000_000_000L
                    || string.IsNullOrWhiteSpace(i.StackUid) || i.StackUid.Length > 128))
                throw new ArgumentException("Malformed stock observation.");
            if (x.Location != StockLocation.Inventory && string.IsNullOrWhiteSpace(x.StorageIdentity))
            {
                warnings.Add($"{x.Location}: identité du coffre absente ; quantité inconnue.");
                continue;
            }
            if (x.Confidence != StockConfidence.Fresh || !x.CompleteSnapshot
                || x.ObservedAt > now || now - x.ObservedAt > age)
            {
                warnings.Add($"{x.Location}: snapshot périmé, partiel ou sans confirmation ; quantité inconnue.");
                continue;
            }
            accepted.Add(x);
        }
        // Common storageIdentity => underlying storage is shared, not 2 containers
        // to sum. In a conflicting alias situation fail closed for the whole store.
        var byStore = accepted.GroupBy(x => x.StorageIdentity, StringComparer.Ordinal).ToArray();
        var stocks = new Dictionary<long, Dictionary<StockLocation, long>>();
        var known = new HashSet<StockLocation>();
        // Runtime HDV merges bag and bank and prefers the bag for matching
        // item instance UIDs. Do not count the same UID in two containers.
        var seenCrossStore = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in byStore.OrderBy(g => g.Min(x => x.Location)))
        {
            if (group.Key.Length == 0)
            {
                warnings.Add("Un snapshot sans identité de stockage a été ignoré.");
                continue;
            }
            var observationsOfStore = group.ToArray();
            var stable = observationsOfStore
                .Select(x => string.Join("|", x.Stacks.Where(s => !s.Reserved).OrderBy(s => s.StackUid,
                    StringComparer.Ordinal).Select(s => $"{s.ItemGid}:{s.StackUid}:{s.Quantity}")))
                .Distinct(StringComparer.Ordinal).ToArray();
            if (stable.Length != 1)
            {
                warnings.Add($"Contenu contradictoire des alias du coffre {group.Key} ; ignoré.");
                continue;
            }
            var chosen = observationsOfStore.OrderBy(x => x.Location).First();
            var stackUids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in chosen.Stacks)
            {
                if (!stackUids.Add(s.StackUid))
                    throw new ArgumentException("Duplicate stack UID in one storage.");
                if (s.Reserved || s.Quantity == 0) continue;
                if (!seenCrossStore.Add(s.StackUid))
                {
                    warnings.Add("Même UID vu dans plusieurs stockages : pile dédupliquée.");
                    continue;
                }
                if (!stocks.TryGetValue(s.ItemGid, out var amounts))
                    stocks[s.ItemGid] = amounts = new Dictionary<StockLocation, long>();
                amounts.TryGetValue(chosen.Location, out long prev);
                amounts[chosen.Location] = checked(prev + s.Quantity);
            }
            known.Add(chosen.Location);
        }
        var items = new List<StockAllocation>();
        foreach (var entry in required.OrderBy(x => x.Key))
        {
            var per = stocks.GetValueOrDefault(entry.Key)
                      ?? new Dictionary<StockLocation, long>();
            long pending = entry.Value;
            long take(StockLocation location)
            {
                long amt = Math.Min(pending, per.GetValueOrDefault(location));
                pending -= amt;
                return amt;
            }
            long bag = take(StockLocation.Inventory);
            long bank = take(StockLocation.Bank);
            long haven = take(StockLocation.HavenBag);
            long unknown = pending > 0 && (!known.Contains(StockLocation.Bank)
                         || !known.Contains(StockLocation.HavenBag)) ? pending : 0;
            items.Add(new StockAllocation(entry.Key, entry.Value, bag, bank, haven, pending, unknown,
                unknown > 0 ? ["Stock en banque/Havre-Sac non entièrement confirmé : reste indicatif."] : []));
        }
        // Never infer an empty HavenBag from a travel/zaap event.
        bool complete = known.Contains(StockLocation.Inventory)
            && known.Contains(StockLocation.Bank) && known.Contains(StockLocation.HavenBag);
        return new UnifiedStockResult(items, complete, warnings);
    }
}
