using System.Text;
using System.Text.Json;
using GivreClone.App.Services.ProfessionPlanner;

namespace GivreClone.App.Services.ProfessionMcp;

/// <summary>
/// Pure, bounded, read-only JSON projection of the actual profession planner.
/// Upstream integration owns evidence verification and snapshot admission.
/// No MCP transport, HTTP, Lua, HDV, inventory mutation or gameplay.
/// The opt-in production path can persist a local advisory CAS checkpoint.
/// </summary>
public static class ProfessionMcpFacade
{
    public const int MaxRequestBytes = 4096;
    public const int MaxResponseBytes = 65536;
    public const int MaxPageSize = 40;
    public const int MaxOffset = 2048;

    public static string QueryJson(string? requestJson, ProfessionMcpSnapshot? snapshot) =>
        QueryJson(requestJson, snapshot, null);

    /// <summary>
    /// Recovery is disabled unless the host injects independently verified
    /// observation evidence AND the caller explicitly activates the preview.
    /// Ordinary plan_job/materials/steps keep their original snapshot behavior.
    /// </summary>
    public static string QueryJson(string? requestJson, ProfessionMcpSnapshot? snapshot,
        ProfessionMcpRecoveryContext? recovery) =>
        QueryJson(requestJson, snapshot, recovery, null);

    /// <summary>
    /// A production workflow may be provided ONLY by the hosting application.
    /// The caller cannot submit its own inventory, order, journal or craft receipt.
    /// </summary>
    public static string QueryJson(string? requestJson, ProfessionMcpSnapshot? snapshot,
        ProfessionMcpRecoveryContext? recovery, ProfessionMcpProductionContext? production)
    {
        if (requestJson is null || Encoding.UTF8.GetByteCount(requestJson) > MaxRequestBytes)
            return Reply("INVALID_REQUEST", "Missing or oversized JSON request.");
        try
        {
            using var parsed = JsonDocument.Parse(requestJson, new JsonDocumentOptions
            {
                AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 5
            });
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                return BadRequest();

            string? op = null, jobName = null;
            bool? activateRecovery = null;
            bool? activateProduction = null;
            int? jobId = null, startLevel = null, targetLevel = null;
            long? trainingRecipeId = null, requestedCrafts = null;
            int offset = 0, limit = 20;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in parsed.RootElement.EnumerateObject())
            {
                if (!keys.Add(field.Name)) return BadRequest();
                switch (field.Name)
                {
                    case "op":
                        if (field.Value.ValueKind != JsonValueKind.String) return BadRequest();
                        op = field.Value.GetString();
                        break;
                    case "jobId":
                        if (!Int(field.Value, 1, int.MaxValue, out var jid)) return BadRequest();
                        jobId = jid;
                        break;
                    case "jobName":
                        if (field.Value.ValueKind != JsonValueKind.String) return BadRequest();
                        jobName = field.Value.GetString();
                        if (!SafeJobName(jobName)) return BadRequest();
                        break;
                    case "startLevel":
                        if (!Int(field.Value, 1, 200, out var from)) return BadRequest();
                        startLevel = from;
                        break;
                    case "targetLevel":
                        if (!Int(field.Value, 1, 200, out var to)) return BadRequest();
                        targetLevel = to;
                        break;
                    case "trainingRecipeId":
                        if (!Long(field.Value, 1, long.MaxValue, out var recipe)) return BadRequest();
                        trainingRecipeId = recipe;
                        break;
                    case "requestedCrafts":
                        if (!Long(field.Value, 0, 1_000_000_000, out var crafts)) return BadRequest();
                        requestedCrafts = crafts;
                        break;
                    case "offset":
                        if (!Int(field.Value, 0, MaxOffset, out offset)) return BadRequest();
                        break;
                    case "limit":
                        if (!Int(field.Value, 1, MaxPageSize, out limit)) return BadRequest();
                        break;
                    case "activateRecovery":
                        if (field.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                            return BadRequest();
                        activateRecovery = field.Value.GetBoolean();
                        break;
                    case "activateProduction":
                        if (field.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                            return BadRequest();
                        activateProduction = field.Value.GetBoolean();
                        break;
                    default:
                        return BadRequest();
                }
            }
            if (op == "capabilities")
            {
                if (keys.Count != 1) return BadRequest();
                return Serialize(new
                {
                    status = "KNOWN", schema = "profession-mcp-query-v1",
                    operations = new[] { "capabilities", "plan_job", "materials", "steps" },
                    recoveryOperations = new[] { "recovery_status", "recovery_preview" },
                    productionOperations = new[] { "production_status", "production_next_action" },
                    recoveryRequiresExplicitActivation = true,
                    productionRequiresHostCertifiedActions = true,
                    recoveryRequiresIndependentObservationReceipt = true,
                    readOnly = true, executed = false, acknowledged = false, clientContacted = false,
                    rightsRequired = true, provenanceRequired = true,
                    bounds = new { maxRequestBytes = MaxRequestBytes, maxResponseBytes = MaxResponseBytes,
                        maxPageSize = MaxPageSize, maxOffset = MaxOffset, maxSteps = 2048 },
                    note = "No automatic gameplay, buying, crafting or route execution."
                });
            }

            if (op is "recovery_status" or "recovery_preview")
            {
                // No caller-supplied inventory, source quota, revision, evidence ID,
                // or arbitrary goal is accepted as recovery evidence via JSON.
                if (keys.Any(k => k is not ("op" or "activateRecovery")) ||
                    op == "recovery_status" && keys.Count != 1)
                    return BadRequest();
                if (op == "recovery_preview" && activateRecovery != true)
                    return RecoveryReply(new("ACTIVATION_REQUIRED", false, false, false,
                        null, null), snapshot);
                var evaluation = ProfessionMcpRecoveryIntegration.Evaluate(snapshot,
                    recovery, op == "recovery_preview");
                return RecoveryReply(evaluation, snapshot);
            }

            if (op is "production_status" or "production_next_action")
            {
                if (keys.Any(k => k is not ("op" or "activateProduction")) ||
                    (op == "production_status" && keys.Count != 1))
                    return BadRequest();
                if (production is null || snapshot is null ||
                    production.Runner is null || production.Order is null ||
                    production.Actions is null || production.Journal is null)
                    return Reply("UNKNOWN", "No host-owned certified production workflow.");
                ProfessionProductionGuidance guidance;
                try
                {
                    guidance = production.Runner.PrepareVerifiedNextAction(snapshot,
                        production.Order, production.Actions, production.Journal,
                        op == "production_next_action" && activateProduction == true);
                }
                catch (Exception)
                { return Reply("UNKNOWN", "Certified production preview unavailable."); }
                return ProductionReply(guidance);
            }

            if (activateRecovery is not null || activateProduction is not null) return BadRequest();

            if (op is not ("plan_job" or "materials" or "steps") ||
                (jobId is null) == (jobName is null) ||
                startLevel is null || targetLevel is null || targetLevel < startLevel ||
                (op == "plan_job" && keys.Overlaps(["offset", "limit"])))
                return BadRequest();
            if (snapshot is null || !snapshot.CanPlan())
                return Reply("UNKNOWN", "No independently admitted local dataset with AI disclosure rights.");

            // Only a goal supplied and evidenced upstream can be planned. MCP callers
            // cannot assert their own verified XP, stock or profession identity.
            var sourceRequest = snapshot.Request;
            var goal = sourceRequest.Goal;
            if (goal.StartLevel != startLevel || goal.TargetLevel != targetLevel ||
                (jobId is { } id && goal.JobId != id) ||
                (jobName is { } name &&
                 !string.Equals(goal.JobName, name, StringComparison.OrdinalIgnoreCase)) ||
                (trainingRecipeId is { } rid && sourceRequest.TrainingRecipeId != rid) ||
                (requestedCrafts is { } qty && sourceRequest.RequestedCrafts != qty))
                return Reply("UNKNOWN", "The requested profession goal is not in the approved snapshot.");

            ProfessionPlan plan;
            try { plan = ProfessionPlannerEngine.Plan(sourceRequest); }
            catch (Exception e) when (e is ArgumentException or OverflowException or ArithmeticException)
            {
                return Reply("UNKNOWN", "Source data or quantity could not be safely planned.");
            }

            return op switch
            {
                "plan_job" => PlanSummary(plan, snapshot),
                "materials" => Materials(plan, snapshot, offset, limit),
                "steps" => Steps(plan, snapshot, offset, limit),
                _ => BadRequest()
            };
        }
        catch (JsonException) { return BadRequest(); }
        catch (ArgumentException) { return BadRequest(); }
    }

    private static string RecoveryReply(ProfessionMcpRecoveryEvaluation evaluation,
        ProfessionMcpSnapshot? snapshot)
    {
        var plan = evaluation.Proposal;
        var state = evaluation.NextState;
        return Serialize(new
        {
            status = evaluation.HasCurrentPlan && plan!.IsComplete ? "KNOWN" : "UNKNOWN",
            diagnostic = evaluation.Diagnostic,
            activated = evaluation.Activated,
            evidenceAdmitted = evaluation.EvidenceAdmitted,
            previousProposalInvalidated = evaluation.PreviousProposalInvalidated,
            revision = state?.Checkpoint.Revision,
            partial = plan is not null && !plan.IsComplete,
            stepCount = plan?.Steps.Count ?? 0,
            steps = plan is null ? Array.Empty<object>() : plan.Steps
                .Take(MaxPageSize).Select(s => (object)new
                {
                    id = s.Id, kind = s.Kind.ToString().ToUpperInvariant(),
                    s.ItemGid, s.Quantity, s.RecipeId,
                    dependencies = s.Dependencies.Take(64).ToArray(),
                    verified = s.Verified && s.Kind != PlanStepKind.Unresolved
                }).ToArray(),
            warnings = plan?.Warnings.Take(20).Select(Short).ToArray() ?? Array.Empty<string>(),
            hasMore = plan is not null && plan.Steps.Count > MaxPageSize,
            gameVersion = evaluation.EvidenceAdmitted ? snapshot?.Provenance.GameVersion : null,
            readOnly = true, executed = false, acknowledged = false, clientContacted = false,
            activationRequired = true,
            note = "A recovery preview is advisory; the host owns receipt verification and persisted state."
        });
    }

    private static string ProductionReply(ProfessionProductionGuidance guidance) =>
        Serialize(new
        {
            status = guidance.CanSuggest && guidance.Plan?.IsComplete == true ? "KNOWN" : "UNKNOWN",
            diagnostic = guidance.Diagnostic,
            partial = guidance.Plan is not null && !guidance.Plan.IsComplete,
            stepCount = guidance.Plan?.Steps.Count ?? 0,
            nextAction = guidance.NextAction is { } step ? new
            {
                stepId = step.StepId, kind = step.Kind,
                itemGid = step.ItemGid, quantity = step.Quantity,
                recipeId = step.RecipeId, observationRevision = step.ObservationRevision,
                manualGuidanceOnly = true
            } : null,
            journalSequence = guidance.JournalSequence,
            checkpointWritten = guidance.Written,
            previousProposalInvalidated = guidance.PreviousProposalInvalidated,
            requiresExplicitActivation = true,
            readOnly = true, executed = false, acknowledged = false, clientContacted = false
        });

    private static string PlanSummary(ProfessionPlan plan, ProfessionMcpSnapshot snapshot) =>
        Serialize(new
        {
            status = Status(plan), partial = !plan.IsComplete, gameVersion = snapshot.Provenance.GameVersion,
            goal = GoalView(plan.Goal), trainingRecipeId = snapshot.Request.TrainingRecipeId,
            progress = new
            {
                plan.Progress.TargetXp, plan.Progress.NeededXp,
                plan.Progress.PlannedCrafts, plan.Progress.PotentialXpEarned,
                plan.Progress.PotentialFinalXp, plan.Progress.TargetReachableFromKnownXp
            },
            stepCount = plan.Steps.Count,
            materialCount = AggregateMaterials(plan).Length,
            knownKamasSubtotal = plan.KnownKamasSubtotal,
            knownSecondsSubtotal = plan.KnownSecondsSubtotal,
            fullKamasEstimate = plan.FullKamasEstimate,
            fullSecondsEstimate = plan.FullSecondsEstimate,
            warnings = plan.Warnings.Take(20).Select(Short).ToArray(),
            warningsTruncated = plan.Warnings.Count > 20,
            provenance = Evidence(snapshot),
            routeStatus = "UNKNOWN", routeLinks = Array.Empty<object>(),
            readOnly = true, executed = false, acknowledged = false, clientContacted = false
        });

    private static string Materials(ProfessionPlan plan, ProfessionMcpSnapshot snapshot, int offset, int limit)
    {
        var materials = AggregateMaterials(plan);
        var selected = materials.Skip(offset).Take(limit).ToArray();
        return Serialize(new
        {
            status = Status(plan), partial = !plan.IsComplete,
            gameVersion = snapshot.Provenance.GameVersion, total = materials.Length,
            offset, limit, returned = selected.Length,
            hasMore = (long)offset + selected.Length < materials.Length,
            materials = selected, provenance = Evidence(snapshot),
            routeStatus = "UNKNOWN", routeLinks = Array.Empty<object>(),
            readOnly = true, executed = false, acknowledged = false, clientContacted = false
        });
    }

    private static object[] AggregateMaterials(ProfessionPlan plan)
    {
        return plan.Steps.Where(s => s.ItemGid > 0 &&
                s.Kind is PlanStepKind.UseInventory or PlanStepKind.Gather or
                    PlanStepKind.Drop or PlanStepKind.Buy or PlanStepKind.Unresolved)
            .GroupBy(s => s.ItemGid).OrderBy(g => g.Key)
            .Select(g => (object)new
            {
                itemGid = g.Key,
                usedStock = SafeSum(g.Where(s => s.Kind == PlanStepKind.UseInventory).Select(s => s.Quantity)),
                toAcquire = SafeSum(g.Where(s => s.Kind != PlanStepKind.UseInventory).Select(s => s.Quantity)),
                methods = g.Where(s => s.Kind != PlanStepKind.UseInventory)
                    .Select(s => s.Kind.ToString().ToUpperInvariant()).Distinct().OrderBy(x => x).ToArray(),
                status = g.All(s => s.Verified && s.Kind != PlanStepKind.Unresolved) ? "KNOWN" : "UNKNOWN",
                alternatives = g.SelectMany(s => s.Alternatives).Where(x => x.Verified)
                    .Select(x => x.Method.ToString().ToUpperInvariant()).Distinct().OrderBy(x => x).ToArray()
            }).ToArray();
    }

    private static string Steps(ProfessionPlan plan, ProfessionMcpSnapshot snapshot, int offset, int limit)
    {
        var page = plan.Steps.Skip(offset).Take(limit).Select(s => new
        {
            id = s.Id, kind = s.Kind.ToString().ToUpperInvariant(), s.ItemGid,
            s.Quantity, s.RecipeId, detail = Short(s.Detail),
            dependencies = s.Dependencies.Take(64).ToArray(),
            status = s.Verified && s.Kind != PlanStepKind.Unresolved ? "KNOWN" : "UNKNOWN",
            kamas = s.Kamas, seconds = s.Seconds,
            alternatives = s.Alternatives.Take(32).Select(a => new
            {
                method = a.Method.ToString().ToUpperInvariant(), a.RecipeId,
                status = a.Verified ? "KNOWN" : "UNKNOWN",
                a.KamasPerUnit, a.SecondsPerUnit,
                sourceVersion = SafeVersion(a.SourceVersion, snapshot.Provenance.GameVersion)
            }).ToArray()
        }).ToArray();
        return Serialize(new
        {
            status = Status(plan), partial = !plan.IsComplete,
            gameVersion = snapshot.Provenance.GameVersion, total = plan.Steps.Count,
            offset, limit, returned = page.Length,
            hasMore = (long)offset + page.Length < plan.Steps.Count,
            steps = page, provenance = Evidence(snapshot),
            routeStatus = "UNKNOWN", routeLinks = Array.Empty<object>(),
            readOnly = true, executed = false, acknowledged = false, clientContacted = false
        });
    }

    private static object GoalView(ProfessionGoal goal) => new
    {
        goal.JobId, jobName = goal.JobName, goal.StartLevel, goal.TargetLevel,
        currentXp = goal.CurrentXpVerified ? goal.CurrentXp : null,
        goal.CurrentXpVerified
    };

    private static object Evidence(ProfessionMcpSnapshot source) => new
    {
        sourceKind = source.Provenance.SourceKind, sourceId = source.Provenance.SourceId,
        gameVersion = source.Provenance.GameVersion,
        manifestSha256 = source.Provenance.ManifestSha256,
        authorizationEvidenceId = source.Provenance.AuthorizationEvidenceId
    };

    private static string SafeVersion(string? sourceVersion, string version) =>
        string.Equals(sourceVersion, version, StringComparison.Ordinal) ? version : "UNKNOWN";

    private static long? SafeSum(IEnumerable<long> quantities)
    {
        long total = 0;
        foreach (long quantity in quantities)
        {
            if (quantity < 0 || total > long.MaxValue - quantity) return null;
            total += quantity;
        }
        return total;
    }

    private static string Status(ProfessionPlan plan) => plan.IsComplete ? "KNOWN" : "UNKNOWN";
    private static string Short(string? value) => value is null ? "" :
        value.Length <= 200 ? value : value[..200];
    private static bool SafeJobName(string? value) =>
        value is { Length: >= 1 and <= 64 } &&
        value.All(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '\'' or '.');
    private static bool Int(JsonElement value, int min, int max, out int number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number) &&
               number >= min && number <= max;
    }
    private static bool Long(JsonElement value, long min, long max, out long number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out number) &&
               number >= min && number <= max;
    }
    private static string BadRequest() => Reply("INVALID_REQUEST", "Invalid or incompatible query fields.");
    private static string Reply(string status, string message) => Serialize(new
    {
        status, message, readOnly = true, executed = false, acknowledged = false,
        clientContacted = false
    });
    private static string Serialize<T>(T payload)
    {
        string json = JsonSerializer.Serialize(payload);
        return Encoding.UTF8.GetByteCount(json) <= MaxResponseBytes ? json :
            "{\"status\":\"UNKNOWN\",\"message\":\"Response size exceeded\",\"readOnly\":true,\"executed\":false,\"acknowledged\":false,\"clientContacted\":false}";
    }
}
