using System.Globalization;
using GivreClone.App.Services.Resources;
using GivreClone.App.Services.UnifiedStock;

namespace GivreClone.App.Services.QuantityFarm;

public enum QuantityFarmStatus { Refused, Completed, NeedsBagOnly, Ready }

public sealed record QuantityFarmStack(long Gid, string Uid, long Quantity, int Position);

public sealed record QuantityFarmPending(uint Pid, string Slot, CancellationToken Owner);

public sealed record QuantityFarmPick(bool Accepted, string Message, int MonsterId, long ObjectId)
{
    public static QuantityFarmPick Refuse(string message) => new(false, message, 0, 0);
    public static QuantityFarmPick Resource(int itemId, string name) => new(true, name, 0, itemId);
    public static QuantityFarmPick Drop(int monsterId, long objectId) => new(true, "", monsterId, objectId);
}

public sealed record QuantityFarmDecision(
    QuantityFarmStatus Status,
    string Message,
    long ItemGid,
    long Target,
    long Owned,
    long Remaining,
    long UnknownStorage,
    StockAllocation? Allocation,
    string? Script);

/// <summary>Admission du farm : identifiant exact, sac position 63, stock confirmé, puis script.</summary>
public sealed record QuantityFarmRequest
{
    public QuantityFarmKind Kind { get; init; } = QuantityFarmKind.Resource;
    public long ItemGid { get; init; }
    public long Quantity { get; init; }
    public int MonsterId { get; init; }
    public bool CanGather { get; init; } = true;
    public bool CanFight { get; init; } = true;
    public bool FollowerLocked { get; init; }
    public string? SocketBlock { get; init; }
    public bool AcceptBagOnly { get; init; }
    public bool HasMap { get; init; } = true;
    public bool LevelAllowed { get; init; } = true;
    public bool BottingAuthorized { get; init; } = true;
    public IReadOnlyList<long> RouteMapIds { get; init; } = Array.Empty<long>();
    public IReadOnlyList<QuantityFarmStack> BagStacks { get; init; } = Array.Empty<QuantityFarmStack>();
    public IReadOnlyList<StockObservation>? Observations { get; init; }
    public IReadOnlyList<(int MonsterId, long ObjectId)>? DropCatalog { get; init; }
    public string AccountKey { get; init; } = "";
    public long CharacterId { get; init; }
    public string GameVersion { get; init; } = "";
    public DateTimeOffset Now { get; init; }
    public string DisplayName { get; init; } = "";
    public int MinMonsters { get; init; } = 1;
    public int MaxMonsters { get; init; } = 8;
    public int MaxPods { get; init; } = 90;
}

public static class QuantityFarmPlanner
{
    public const string PendingHarvest = "_pendingHarvest";
    public const string PendingAttack = "_pendingAttack";
    public const string PendingMapChange = "_pendingMapChange";

    public static QuantityFarmPick ResolveResource(ResourceMap map, string? idOrName)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));
        if (string.IsNullOrWhiteSpace(idOrName))
            return QuantityFarmPick.Refuse("Identifiant de ressource requis.");
        string text = idOrName.Trim();
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int id))
        {
            ResourceInfo? hit = null;
            int count = 0;
            foreach (var resource in map.Resources)
            {
                if (resource.ItemId != id) continue;
                hit = resource;
                count++;
            }
            if (count == 1 && hit is not null) return QuantityFarmPick.Resource(hit.ItemId, hit.Name);
            return QuantityFarmPick.Refuse(count == 0 ? "Ressource inconnue." : "Plusieurs ressources portent cet identifiant.");
        }
        var found = map.Find(text);
        if (found.Count > 1)
            return QuantityFarmPick.Refuse("Plusieurs ressources correspondent : identifiant exact requis.");
        return QuantityFarmPick.Refuse(found.Count == 0 ? "Ressource inconnue." : "Identifiant exact requis.");
    }

    public static QuantityFarmPick ResolveDrop(int monsterId, long objectId, IEnumerable<(int MonsterId, long ObjectId)>? drops)
    {
        if (monsterId <= 0 || objectId <= 0)
            return QuantityFarmPick.Refuse("Monstre ou drop invalide.");
        bool onCard = false;
        if (drops is not null)
        {
            foreach (var row in drops)
            {
                if (row.MonsterId == monsterId && row.ObjectId == objectId)
                {
                    onCard = true;
                    break;
                }
            }
        }
        return onCard
            ? QuantityFarmPick.Drop(monsterId, objectId)
            : QuantityFarmPick.Refuse("Ce drop n'est pas sur ce monstre.");
    }

    public static QuantityFarmDecision Evaluate(QuantityFarmRequest request, Func<QuantityFarmScriptSpec, string>? build = null)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.ItemGid <= 0 || request.Quantity < 1 || request.Quantity > 1_000_000_000L)
            throw new ArgumentException("Invalid resource demand.");

        QuantityFarmDecision Stop(QuantityFarmStatus status, string message, long owned = 0, long remaining = 0,
            long unknown = 0, StockAllocation? allocation = null, string? script = null)
            => new(status, message, request.ItemGid, request.Quantity, owned, remaining, unknown, allocation, script);

        if (!string.IsNullOrWhiteSpace(request.SocketBlock))
            return Stop(QuantityFarmStatus.Refused, request.SocketBlock!);
        if (request.FollowerLocked)
            return Stop(QuantityFarmStatus.Refused, "Ce personnage est verrouillé en suiveur.");
        if (!request.BottingAuthorized)
            return Stop(QuantityFarmStatus.Refused, "Botting non autorisé.");
        if (!request.LevelAllowed)
            return Stop(QuantityFarmStatus.Refused, "Niveau supérieur au plafond autorisé.");
        if (!request.HasMap)
            return Stop(QuantityFarmStatus.Refused, "Aucune carte confirmée.");
        if (request.Kind == QuantityFarmKind.Resource && !request.CanGather)
            return Stop(QuantityFarmStatus.Refused, "Droit Récolte absent. Aucun script lancé.");
        if (request.Kind == QuantityFarmKind.Monster && !request.CanFight)
            return Stop(QuantityFarmStatus.Refused, "Droit Combat absent. Aucun script lancé.");
        if (request.Kind == QuantityFarmKind.Monster)
        {
            var drop = ResolveDrop(request.MonsterId, request.ItemGid, request.DropCatalog);
            if (!drop.Accepted) return Stop(QuantityFarmStatus.Refused, drop.Message);
        }
        if (request.RouteMapIds is null || request.RouteMapIds.Count == 0 || request.RouteMapIds.Any(id => id <= 0))
            return Stop(QuantityFarmStatus.Refused, "Aucune carte : pas de farm.");

        long owned = 0;
        foreach (var stack in request.BagStacks ?? Array.Empty<QuantityFarmStack>())
        {
            if (stack.Gid == request.ItemGid && stack.Position == 63 && stack.Quantity > 0)
                owned += stack.Quantity;
        }
        long remaining = request.Quantity > owned ? request.Quantity - owned : 0;
        if (owned >= request.Quantity)
            return Stop(QuantityFarmStatus.Completed, $"Objectif déjà dans le sac : {owned} / {request.Quantity}. Aucun script lancé.", owned, 0);

        var observations = request.Observations ?? SynthesizeBag(request);
        var result = UnifiedStockPlanner.Allocate(request.AccountKey, request.CharacterId, request.GameVersion,
            request.Now, observations, new Dictionary<long, long> { [request.ItemGid] = request.Quantity });
        var allocation = result.Items.Single();
        if (allocation.UnknownStorage > 0 && !request.AcceptBagOnly)
        {
            return Stop(QuantityFarmStatus.NeedsBagOnly,
                $"Sac : {owned}. Reste à prendre dans le sac : {remaining}. Stock non confirmé : {allocation.UnknownStorage}. Confirme « Sac seulement » pour viser le sac, sans déposer cet objet.",
                owned, remaining, allocation.UnknownStorage, allocation);
        }

        var spec = new QuantityFarmScriptSpec(request.Kind, request.ItemGid, request.Quantity, request.MonsterId,
            request.MinMonsters, request.MaxMonsters, request.MaxPods, request.DisplayName, request.RouteMapIds);
        string? script = build?.Invoke(spec);
        return Stop(QuantityFarmStatus.Ready, "Prêt.", owned, remaining, allocation.UnknownStorage, allocation, script);
    }

    /// <summary>Retire les actions du run demandé. Un autre pid, un autre jeton ou une action manuelle restent.</summary>
    public static IReadOnlyList<QuantityFarmPending> Cancel(IReadOnlyList<QuantityFarmPending> pending, uint pid, CancellationToken owner)
    {
        if (pending is null) throw new ArgumentNullException(nameof(pending));
        var kept = new List<QuantityFarmPending>(pending.Count);
        foreach (var item in pending)
        {
            if (item.Pid == pid && item.Owner.Equals(owner)) continue;
            kept.Add(item);
        }
        return kept;
    }

    private static IReadOnlyList<StockObservation> SynthesizeBag(QuantityFarmRequest request)
    {
        var stacks = (request.BagStacks ?? Array.Empty<QuantityFarmStack>())
            .Where(stack => stack.Gid > 0 && stack.Quantity > 0 && !string.IsNullOrWhiteSpace(stack.Uid) && stack.Uid.Length <= 128)
            .Select(stack => new StockStack(stack.Gid, stack.Uid, stack.Quantity, stack.Position != 63))
            .ToArray();
        if (stacks.Length == 0) return Array.Empty<StockObservation>();
        return new StockObservation[]
        {
            new(request.AccountKey, request.CharacterId, StockLocation.Inventory,
                "bag:" + request.CharacterId.ToString(CultureInfo.InvariantCulture),
                "quantity-bag", request.Now, request.GameVersion, StockConfidence.Fresh, true, stacks)
        };
    }

    public static bool TryQuantity(decimal? value, out long quantity)
    {
        quantity = 0;
        if (value is not decimal amount || amount != decimal.Truncate(amount) || amount < 1 || amount > 1_000_000_000m)
            return false;
        quantity = (long)amount;
        return true;
    }
}
