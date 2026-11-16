using System.Text;
using System.Text.Json;
using GivreClone.App.Services.ProfessionPlanner;

namespace GivreClone.App.Services.ProfessionMcp;

/// <summary>
/// Add-on to ProfessionMcpFacade: bounded visual BOM, proved acquisition
/// routes, and non-executable script planning brief. Never emits gameplay API
/// calls, Lua programs, purchases, client commands or invented game data.
/// </summary>
public static class ProfessionMcpPreviewFacade
{
    public const int MaxRequestBytes = 4096;
    public const int MaxResponseBytes = 65536;
    public const int MaxPageSize = 30;
    public const int MaxOffset = 2048;

    public static string QueryJson(string? requestJson, ProfessionMcpPreviewSnapshot? snapshot)
    {
        if (requestJson is null || Encoding.UTF8.GetByteCount(requestJson) > MaxRequestBytes)
            return Invalid();
        try
        {
            using var doc = JsonDocument.Parse(requestJson, new JsonDocumentOptions
            {
                MaxDepth = 5, AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Invalid();

            string? op = null;
            int? jobId = null, start = null, target = null;
            int offset = 0, limit = 20;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!seen.Add(prop.Name)) return Invalid();
                switch (prop.Name)
                {
                    case "op":
                        if (prop.Value.ValueKind != JsonValueKind.String) return Invalid();
                        op = prop.Value.GetString();
                        break;
                    case "jobId":
                        if (!Integer(prop.Value, 1, int.MaxValue, out var jid)) return Invalid();
                        jobId = jid;
                        break;
                    case "startLevel":
                        if (!Integer(prop.Value, 1, 200, out var from)) return Invalid();
                        start = from;
                        break;
                    case "targetLevel":
                        if (!Integer(prop.Value, 1, 200, out var to)) return Invalid();
                        target = to;
                        break;
                    case "offset":
                        if (!Integer(prop.Value, 0, MaxOffset, out offset)) return Invalid();
                        break;
                    case "limit":
                        if (!Integer(prop.Value, 1, MaxPageSize, out limit)) return Invalid();
                        break;
                    default:
                        return Invalid();
                }
            }
            if (op == "capabilities" && seen.Count == 1)
                return Serialize(new
                {
                    status = "KNOWN", schema = "profession-mcp-preview-v1",
                    operations = new[] { "capabilities", "visual_preview", "farm_routes", "script_outline" },
                    bounds = new { maxRequestBytes = MaxRequestBytes,
                        maxResponseBytes = MaxResponseBytes, maxPageSize = MaxPageSize },
                    readOnly = true, executed = false, acknowledged = false,
                    clientContacted = false, luaExecutable = false,
                    note = "A script-outline is planning data, not executable Lua."
                });
            if (op is not ("visual_preview" or "farm_routes" or "script_outline") ||
                jobId is null || start is null || target is null || target < start ||
                (op == "script_outline" && (seen.Contains("offset") || seen.Contains("limit"))))
                return Invalid();
            if (snapshot is null || !snapshot.CanInspect())
                return Unknown("No locally admitted, authorized profession data.");
            var req = snapshot.Profession.Request;
            if (req.Goal.JobId != jobId || req.Goal.StartLevel != start ||
                req.Goal.TargetLevel != target)
                return Unknown("Requested job/level goal is not in the approved snapshot.");

            ProfessionPlan plan;
            try { plan = ProfessionPlannerEngine.Plan(req); }
            catch (Exception e) when (e is ArgumentException or ArithmeticException or OverflowException)
            { return Unknown("Unable to plan with currently verified recipes and quantities."); }

            return op switch
            {
                "visual_preview" => Graph(plan, snapshot, offset, limit),
                "farm_routes" => Routes(plan, snapshot, offset, limit),
                "script_outline" => Outline(plan, snapshot),
                _ => Invalid()
            };
        }
        catch (JsonException) { return Invalid(); }
        catch (ArgumentException) { return Invalid(); }
    }

    private static string Graph(ProfessionPlan plan, ProfessionMcpPreviewSnapshot snapshot,
        int offset, int limit)
    {
        var steps = plan.Steps.Skip(offset).Take(limit).Select(s => new
        {
            nodeId = s.Id, kind = s.Kind.ToString().ToUpperInvariant(),
            itemGid = s.ItemGid > 0 ? s.ItemGid : (long?)null,
            quantity = s.Quantity, recipeId = s.RecipeId,
            requires = s.Dependencies.Take(64).ToArray(),
            status = s.Verified && s.Kind != PlanStepKind.Unresolved ? "KNOWN" : "UNKNOWN",
            kamas = s.Kamas, seconds = s.Seconds
        }).ToArray();
        return Serialize(new
        {
            status = plan.IsComplete ? "KNOWN" : "UNKNOWN",
            partial = !plan.IsComplete, version = snapshot.Profession.Provenance.GameVersion,
            target = new { plan.Goal.JobId, plan.Goal.StartLevel, plan.Goal.TargetLevel },
            nodes = steps, total = plan.Steps.Count, offset, limit,
            hasMore = (long)offset + steps.Length < plan.Steps.Count,
            warnings = Warnings(plan), fullKamasEstimate = plan.FullKamasEstimate,
            fullSecondsEstimate = plan.FullSecondsEstimate,
            provenance = Provenance(snapshot),
            readOnly = true, executed = false, acknowledged = false, clientContacted = false
        });
    }

    private static string Routes(ProfessionPlan plan, ProfessionMcpPreviewSnapshot snapshot,
        int offset, int limit)
    {
        var required = plan.Steps
            .Where(s => s.Verified && s.Kind is PlanStepKind.Gather or PlanStepKind.Drop or PlanStepKind.Buy)
            .ToArray();
        var routes = snapshot.Routes
            .Where(r => snapshot.IsRouteAdmitted(r) && required.Any(s =>
                s.ItemGid == r.ItemGid && s.Kind.ToString().Equals(
                    r.Method.ToString(), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(r => r.ItemGid).ThenBy(r => r.RouteId, StringComparer.Ordinal).ToArray();
        var page = routes.Skip(offset).Take(limit).Select(r => new
        {
            routeId = r.RouteId, itemGid = r.ItemGid,
            method = r.Method.ToString().ToUpperInvariant(),
            mapIds = r.MapIds, edgeIds = r.EdgeIds,
            hopCount = r.EdgeIds.Count, status = "KNOWN",
            provenance = new
            {
                sourceKind = r.SourceKind, sourceId = r.SourceId,
                sourceVersion = r.GameVersion, sha256 = r.SourceSha256,
                authorizationEvidenceId = r.AuthorizationEvidenceId
            }
        }).ToArray();
        return Serialize(new
        {
            status = routes.Length > 0 ? "KNOWN" : "UNKNOWN",
            version = snapshot.Profession.Provenance.GameVersion, total = routes.Length,
            offset, limit, hasMore = (long)offset + page.Length < routes.Length,
            routes = page, partial = !plan.IsComplete,
            warnings = Warnings(plan), provenance = Provenance(snapshot),
            readOnly = true, executed = false, acknowledged = false, clientContacted = false,
            note = "Evidence-backed static paths only; never proof of travel/ACK."
        });
    }

    private static string Outline(ProfessionPlan plan, ProfessionMcpPreviewSnapshot snapshot)
    {
        // A tool may use this outline to draft its own Lua, but none of these
        // fields are gameplay script/API commands or authorizations.
        var actions = plan.Steps.Take(128).Select(s => new
        {
            action = s.Kind.ToString().ToUpperInvariant(),
            itemGid = s.ItemGid > 0 ? s.ItemGid : (long?)null,
            quantity = s.Quantity, recipeId = s.RecipeId,
            dependsOn = s.Dependencies.Take(32).ToArray(),
            status = s.Verified && s.Kind != PlanStepKind.Unresolved ? "KNOWN" : "UNKNOWN",
        }).ToArray();
        // Comment-only Lua scaffold: nothing can launch, move, buy or craft.
        // Numeric identifiers and enum labels are exclusively engine-derived.
        var lua = new StringBuilder();
        lua.AppendLine("-- GivreWork profession plan - review-only scaffold");
        lua.Append("-- Job ").Append(plan.Goal.JobId?.ToString() ?? "UNKNOWN")
            .Append("; levels ").Append(plan.Goal.StartLevel)
            .Append(" -> ").Append(plan.Goal.TargetLevel).AppendLine();
        foreach (var step in plan.Steps.Take(50))
            lua.Append("-- Step ").Append(step.Id).Append(": ")
                .Append(step.Kind.ToString().ToUpperInvariant())
                .Append(" item=").Append(step.ItemGid)
                .Append(" count=").Append(step.Quantity)
                .Append(" source=").Append(step.Verified ? "REVIEWED" : "UNKNOWN")
                .AppendLine();
        if (plan.Steps.Count > 50) lua.AppendLine("-- Further steps omitted from Lua scaffold");
        lua.AppendLine("-- No gameplay API calls. Review recipe/XP, navigation and permissions separately.");
        return Serialize(new
        {
            status = plan.IsComplete ? "KNOWN" : "UNKNOWN",
            version = snapshot.Profession.Provenance.GameVersion,
            actions, truncated = plan.Steps.Count > actions.Length,
            trainingRecipeId = snapshot.Profession.Request.TrainingRecipeId,
            plannedCrafts = plan.Progress.PlannedCrafts,
            neededXp = plan.Progress.NeededXp,
            warnings = Warnings(plan), routeStatus = "UNKNOWN",
            executableLua = (string?)null, requiresExplicitScriptGeneration = true,
            luaDraft = lua.ToString(), luaDraftIsExecutableGameplay = false,
            requiresSeparateGameplayPermission = true,
            provenance = Provenance(snapshot),
            readOnly = true, executed = false, acknowledged = false, clientContacted = false
        });
    }

    private static object Provenance(ProfessionMcpPreviewSnapshot s) => new
    {
        sourceKind = s.Profession.Provenance.SourceKind,
        sourceId = s.Profession.Provenance.SourceId,
        gameVersion = s.Profession.Provenance.GameVersion,
        manifestSha256 = s.Profession.Provenance.ManifestSha256,
        authorizationEvidenceId = s.Profession.Provenance.AuthorizationEvidenceId,
        operatorAttested = s.PartnerGrant?.Allows(s.Profession.Provenance) == true,
        independentlyRightsVerified = false
    };

    private static string[] Warnings(ProfessionPlan plan) =>
        plan.Warnings.Take(20).Select(w => w.Length > 160 ? w[..160] : w).ToArray();
    private static bool Integer(JsonElement el, int min, int max, out int v)
    {
        v = 0;
        return el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out v) &&
               v >= min && v <= max;
    }
    private static string Invalid() => Reply("INVALID_REQUEST", "Invalid read-only profession query.");
    private static string Unknown(string reason) => Reply("UNKNOWN", reason);
    private static string Reply(string status, string reason) => Serialize(new
    {
        status, message = reason, readOnly = true, executed = false,
        acknowledged = false, clientContacted = false
    });
    private static string Serialize<T>(T result)
    {
        string json = JsonSerializer.Serialize(result);
        return Encoding.UTF8.GetByteCount(json) <= MaxResponseBytes ? json :
            "{\"status\":\"UNKNOWN\",\"readOnly\":true,\"executed\":false,\"acknowledged\":false,\"clientContacted\":false}";
    }
}
