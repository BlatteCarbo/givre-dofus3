using System.Text;
using System.Text.Json;

namespace GivreClone.App.Services.ItemCraftMcp;

/// <summary>
/// Pure JSON view over independently admitted, already computed ItemCraftPlanner
/// snapshots. No runtime reads, file access, HTTP, route inference or commands.
/// Planning an uncached goal returns UNKNOWN instead of guessing a recipe.
/// </summary>
public static class ItemCraftMcpFacade
{
    public const int MaxRequestBytes = 4096;
    public const int MaxResponseBytes = 65536;
    public const int MaxPageSize = 32;
    public const int MaxOffset = 2048;

    public static string QueryJson(string? requestJson, ItemCraftMcpSnapshot? snapshot)
    {
        if (requestJson is null || Encoding.UTF8.GetByteCount(requestJson) > MaxRequestBytes)
            return Reply("INVALID_REQUEST", "Missing or oversized request.");
        try
        {
            using var doc = JsonDocument.Parse(requestJson, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 6
            });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Bad();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? op = null, kindText = null, query = null;
            long? itemGid = null, setId = null;
            long quantity = 1;
            int offset = 0, limit = 20;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name)) return Bad();
                switch (property.Name)
                {
                    case "op":
                        if (property.Value.ValueKind != JsonValueKind.String) return Bad();
                        op = property.Value.GetString();
                        break;
                    case "kind":
                        if (property.Value.ValueKind != JsonValueKind.String) return Bad();
                        kindText = property.Value.GetString();
                        if (kindText is not ("item" or "set")) return Bad();
                        break;
                    case "query":
                        if (property.Value.ValueKind != JsonValueKind.String) return Bad();
                        query = property.Value.GetString();
                        if (query is null || query.Length > 80 || query.Any(char.IsControl))
                            return Bad();
                        break;
                    case "itemGid":
                        if (!Long(property.Value, 1, long.MaxValue, out var id)) return Bad();
                        itemGid = id;
                        break;
                    case "setId":
                        if (!Long(property.Value, 1, long.MaxValue, out var sid)) return Bad();
                        setId = sid;
                        break;
                    case "quantity":
                        if (!Long(property.Value, 1, ItemCraftMcpSnapshot.MaxQuantity, out quantity))
                            return Bad();
                        break;
                    case "offset":
                        if (!Int(property.Value, 0, MaxOffset, out offset)) return Bad();
                        break;
                    case "limit":
                        if (!Int(property.Value, 1, MaxPageSize, out limit)) return Bad();
                        break;
                    default:
                        return Bad();
                }
            }

            if (op == "capabilities")
            {
                if (seen.Count != 1) return Bad();
                return Serialize(new
                {
                    status = "KNOWN", schema = "item-craft-mcp-query-v1",
                    operations = new[] { "capabilities", "list_targets", "plan_item", "plan_set", "materials", "steps" },
                    bounds = new { maxRequestBytes = MaxRequestBytes, maxResponseBytes = MaxResponseBytes,
                        maxPageSize = MaxPageSize, maxOffset = MaxOffset,
                        maxTargets = ItemCraftMcpSnapshot.MaxTargets,
                        maxCachedPlans = ItemCraftMcpSnapshot.MaxPlans,
                        maxQuantity = ItemCraftMcpSnapshot.MaxQuantity },
                    provenanceRequired = true, independentAiRightsRequired = true,
                    readOnly = true, executed = false, acknowledged = false, clientContacted = false,
                    runtimeCertified = false
                });
            }

            if (op == "list_targets")
            {
                if (seen.Overlaps(["itemGid", "setId", "quantity"])) return Bad();
            }
            else
            {
                if (op is not ("plan_item" or "plan_set" or "materials" or "steps") ||
                    seen.Contains("query")) return Bad();
                if (op == "plan_item" &&
                    (itemGid is null || setId is not null || kindText is not null ||
                     seen.Overlaps(["offset", "limit"])))
                    return Bad();
                if (op == "plan_set" &&
                    (setId is null || itemGid is not null || kindText is not null ||
                     seen.Overlaps(["offset", "limit"])))
                    return Bad();
                if (op is "materials" or "steps" &&
                    (kindText is null ||
                     (kindText == "item" && (itemGid is null || setId is not null)) ||
                     (kindText == "set" && (setId is null || itemGid is not null))))
                    return Bad();
            }

            if (snapshot is null || !snapshot.CanDisclose())
                return Reply("UNKNOWN", "No independently authorized local item/set dataset.");

            if (op == "list_targets")
                return ListTargets(snapshot, kindText, query, offset, limit);

            var kind = op switch
            {
                "plan_item" => ItemCraftMcpTargetKind.Item,
                "plan_set" => ItemCraftMcpTargetKind.Set,
                _ => kindText == "set" ? ItemCraftMcpTargetKind.Set : ItemCraftMcpTargetKind.Item
            };
            long targetId = kind == ItemCraftMcpTargetKind.Item ? itemGid!.Value : setId!.Value;
            var target = snapshot.Find(kind, targetId);
            if (target is null)
                return Reply("UNKNOWN", "Target absent from admitted local item/set index.");
            var plan = snapshot.FindPlan(kind, targetId, quantity);
            if (plan is null && (snapshot.Context is not null || snapshot.RecipeCatalog is not null))
            {
                try { plan = ItemCraftMcpEngineAdapter.Compute(snapshot, target, quantity); }
                catch (Exception ex) when (ex is ArgumentException or OverflowException or
                    ArithmeticException or InvalidOperationException)
                {
                    return Reply("UNKNOWN", "Unable to safely evaluate the approved recipe graph.");
                }
            }
            if (plan is null)
                return Reply("UNKNOWN", "No cached ItemCraftPlanner computation for exact target and quantity.");

            return op switch
            {
                "plan_item" or "plan_set" => Summary(snapshot, target, plan),
                "materials" => Materials(snapshot, target, plan, offset, limit),
                "steps" => Steps(snapshot, target, plan, offset, limit),
                _ => Bad()
            };
        }
        catch (JsonException) { return Bad(); }
        catch (ArgumentException) { return Bad(); }
    }

    private static string ListTargets(ItemCraftMcpSnapshot snapshot, string? filter,
        string? search, int offset, int limit)
    {
        var rows = snapshot.Items.Values.Concat(snapshot.Sets.Values)
            .Where(t => filter is null ||
                        t.Kind == (filter == "set" ? ItemCraftMcpTargetKind.Set : ItemCraftMcpTargetKind.Item))
            .Where(t => search is null || t.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Kind).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Id).ToArray();
        var page = rows.Skip(offset).Take(limit).Select(t => TargetView(snapshot, t)).ToArray();
        return Serialize(new
        {
            status = rows.Length == 0 ? "UNKNOWN" : "KNOWN", gameVersion = snapshot.Evidence.GameVersion,
            total = rows.Length, offset, limit, returned = page.Length,
            hasMore = (long)offset + page.Length < rows.Length,
            targets = page, provenance = EvidenceView(snapshot.Evidence),
            readOnly = true, executed = false, acknowledged = false, clientContacted = false,
            runtimeCertified = false
        });
    }

    private static string Summary(ItemCraftMcpSnapshot snapshot, ItemCraftMcpTarget target,
        ItemCraftMcpPlanResult plan) => Serialize(new
        {
            status = Status(snapshot, target, plan), partial = Status(snapshot, target, plan) != "KNOWN",
            target = TargetView(snapshot, target), quantity = plan.Quantity,
            gameVersion = snapshot.Evidence.GameVersion,
            materialCount = plan.Materials.Count, stepCount = plan.Steps.Count,
            inventoryStatus = plan.InventoryVerified ? "VERIFIED" : "UNKNOWN",
            warnings = plan.Warnings.Take(32).Select(Short).ToArray(),
            warningsTruncated = plan.Warnings.Count > 32,
            provenance = EvidenceView(plan.Evidence),
            routeStatus = "UNKNOWN", routeLinks = Array.Empty<object>(),
            readOnly = true, executed = false, acknowledged = false, clientContacted = false,
            runtimeCertified = false
        });

    private static string Materials(ItemCraftMcpSnapshot snapshot, ItemCraftMcpTarget target,
        ItemCraftMcpPlanResult plan, int offset, int limit)
    {
        var page = plan.Materials.OrderBy(m => m.ItemGid).Skip(offset).Take(limit).Select(m => new
        {
            itemGid = m.ItemGid,
            status = m.Verified ? "KNOWN" : "UNKNOWN",
            required = m.Verified ? m.Required : null,
            owned = m.Verified && plan.InventoryVerified ? m.Owned : null,
            missing = m.Verified && plan.InventoryVerified ? m.Missing : null,
            method = m.Verified ? m.Method : "UNKNOWN",
            sourceId = m.Verified ? m.SourceId : null
        }).ToArray();
        return Serialize(new
        {
            status = Status(snapshot, target, plan), partial = Status(snapshot, target, plan) != "KNOWN",
            gameVersion = snapshot.Evidence.GameVersion,
            total = plan.Materials.Count, offset, limit, returned = page.Length,
            hasMore = (long)offset + page.Length < plan.Materials.Count,
            materials = page, provenance = EvidenceView(plan.Evidence),
            routeStatus = "UNKNOWN", routeLinks = Array.Empty<object>(),
            readOnly = true, executed = false, acknowledged = false, clientContacted = false,
            runtimeCertified = false
        });
    }

    private static string Steps(ItemCraftMcpSnapshot snapshot, ItemCraftMcpTarget target,
        ItemCraftMcpPlanResult plan, int offset, int limit)
    {
        var page = plan.Steps.Skip(offset).Take(limit).Select(s => new
        {
            stepId = s.StepId, itemGid = s.ItemGid,
            status = s.Verified ? "KNOWN" : "UNKNOWN",
            quantity = s.Verified ? s.Quantity : null,
            method = s.Verified ? s.Method : "UNKNOWN",
            recipeId = s.Verified ? s.RecipeId : null,
            localRecipeKey = s.Verified ? s.LocalRecipeKey : null,
            dependencies = s.Dependencies.ToArray(),
            sourceId = s.Verified ? s.SourceId : null
        }).ToArray();
        return Serialize(new
        {
            status = Status(snapshot, target, plan), partial = Status(snapshot, target, plan) != "KNOWN",
            gameVersion = snapshot.Evidence.GameVersion,
            total = plan.Steps.Count, offset, limit, returned = page.Length,
            hasMore = (long)offset + page.Length < plan.Steps.Count,
            steps = page, provenance = EvidenceView(plan.Evidence),
            routeStatus = "UNKNOWN", routeLinks = Array.Empty<object>(),
            readOnly = true, executed = false, acknowledged = false, clientContacted = false,
            runtimeCertified = false
        });
    }

    private static string Status(ItemCraftMcpSnapshot snapshot, ItemCraftMcpTarget target,
        ItemCraftMcpPlanResult plan)
    {
        bool membersVerified = target.Kind != ItemCraftMcpTargetKind.Set ||
            target.PieceGids.Count > 0 &&
            target.PieceGids.All(gid => snapshot.Items.TryGetValue(gid, out var item) && item.Verified);
        return target.Verified && membersVerified && plan.IsComplete && plan.InventoryVerified &&
               plan.Materials.All(m => m.Verified && m.Required is not null &&
                   m.Owned is not null && m.Missing is not null) &&
               plan.Steps.All(s => s.Verified && s.Quantity is not null)
            ? "KNOWN" : "UNKNOWN";
    }

    private static object TargetView(ItemCraftMcpSnapshot snapshot, ItemCraftMcpTarget target)
    {
        bool allMembers = target.Kind != ItemCraftMcpTargetKind.Set ||
            target.PieceGids.Count > 0 &&
            target.PieceGids.All(gid => snapshot.Items.TryGetValue(gid, out var piece) && piece.Verified);
        return new
        {
            kind = target.Kind == ItemCraftMcpTargetKind.Item ? "item" : "set",
            id = target.Id, name = target.Name,
            status = target.Verified && allMembers ? "VERIFIED" : "UNKNOWN",
            pieceGids = target.Kind == ItemCraftMcpTargetKind.Set ? target.PieceGids : Array.Empty<long>(),
            provenance = EvidenceView(target.Evidence)
        };
    }

    private static object EvidenceView(ItemCraftMcpEvidence p) => new
    {
        sourceKind = p.SourceKind, sourceId = p.SourceId,
        gameVersion = p.GameVersion, manifestSha256 = p.ManifestSha256,
        authorizationEvidenceId = p.AuthorizationEvidenceId
    };

    private static string Short(string s) => s.Length <= 200 ? s : s[..200];
    private static bool Long(JsonElement el, long min, long max, out long result)
    {
        result = 0;
        return el.ValueKind == JsonValueKind.Number &&
               el.TryGetInt64(out result) && result >= min && result <= max;
    }
    private static bool Int(JsonElement el, int min, int max, out int result)
    {
        result = 0;
        return el.ValueKind == JsonValueKind.Number &&
               el.TryGetInt32(out result) && result >= min && result <= max;
    }
    private static string Bad() => Reply("INVALID_REQUEST", "Invalid or incompatible JSON query.");
    private static string Reply(string status, string message) => Serialize(new
    {
        status, message, readOnly = true, executed = false, acknowledged = false,
        clientContacted = false, runtimeCertified = false
    });
    private static string Serialize<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return Encoding.UTF8.GetByteCount(json) <= MaxResponseBytes ? json :
            "{\"status\":\"UNKNOWN\",\"message\":\"Response exceeded output cap\",\"readOnly\":true,\"executed\":false,\"acknowledged\":false,\"clientContacted\":false,\"runtimeCertified\":false}";
    }
}
