using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GivreClone.App.Services.ItemCraftPlanner;

namespace GivreClone.App.Services.Production;

/// <summary>
/// Workflow checkpoint, not a new executor. InProgress is a local dispatch
/// intention; Reconciled only means the EXISTING production attempt journal
/// completed this exact local run after its verified craft/listing receipts.
/// </summary>
public enum GuidedProductionWorkflowPhase
{
    Absent,
    Ready,
    InProgress,
    AwaitingObservation,
    Blocked,
    Uncertain,
    Reconciled
}

public sealed record GuidedProductionWorkflowSnapshot(
    GuidedProductionWorkflowPhase Phase, string GoalHash, string RequestHash,
    Guid LocalRunId, string Reason)
{
    public bool CanStart => Phase == GuidedProductionWorkflowPhase.Ready;

    // Advisory V13 supply checkpoint; never an independent gameplay lease.
    // Init defaults preserve decoding of pre-V13 V12 journals.
    public string SupplyPlanHash { get; init; } = "";
    public string SupplySourceKey { get; init; } = "";
    public string SupplySessionKey { get; init; } = "";
    public long SupplyObservationRevision { get; init; }
    public GuidedSupplyPlanPhaseV13? SupplyPhase { get; init; }

    // V18 pods are part of the SAME V13 checkpoint as the inventory revision.
    // Optional on pre-V18 journals; absent evidence cannot grant a new V18
    // handoff until an explicit host-observed refresh binds these values.
    public int? SupplyUsedPodsV18 { get; init; }
    public int? SupplyCapacityPodsV18 { get; init; }

    // V16 operator choice is advisory and one-shot for this exact V13
    // checkpoint. It is never an ItemCraftProductionAttempt lease or ACK.
    public GuidedSupplyExactChoiceV16? SupplyChoiceV16 { get; init; }

    // V17: immutable list of consulted GID/quantity steps for this SAME V13
    // source, session, revision and plan. Each token is an exact operator
    // inspection, not a bank/buyer receipt. Kept compact in the V12 ledger.
    // Canonical delimiter-separated value keeps record equality structural:
    // existing V15/V16 readers compare snapshots before/after I/O.
    public string SupplyConsultationsV17 { get; init; } = "";
}

public sealed record GuidedProductionIngredientObservation(
    long Gid, long Required, int BagObserved, int ChestObserved,
    long MissingFromBag, long MissingAfterAuthorizedBank, bool BuyAllowed);

public sealed record GuidedProductionStockPreflight(
    bool Valid, string Reason,
    IReadOnlyList<GuidedProductionIngredientObservation> Ingredients,
    bool NeedsChest, bool NeedsBuyer, bool NeedsGather,
    bool MayRequestExistingRunner)
{
    public long MissingCount => Ingredients.Sum(i => i.MissingAfterAuthorizedBank);
}

/// <summary>
/// Reads the same per-ingredient, per-session ports as the actual production
/// runner. The counts are LOCAL MODEL OBSERVATIONS, not server inventory
/// snapshots or an action receipt. No bank, move, gather, buy or craft send.
/// The runner remains the only sender and independently checks the recipe,
/// stock, workshop, budget and receipts before every mutation.
/// </summary>
public static class GuidedProductionStockPlanner
{
    public static GuidedProductionStockPreflight Evaluate(
        ItemCraftProductionRequest request, IItemCraftProductionActions actions)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actions);
        var r = request.Recipe;
        if (r is null || r.ResultGid <= 0 || r.ResultPerCraft is not int output ||
            output <= 0 || request.Quantity <= 0 || r.Inputs is not { Count: > 0 and <= 128 } ||
            actions is not IItemCraftProductionStockObservation
                { SupportsStockObservation: true } stock)
            return Deny("recette, rendement ou observation de stock manquants");
        try
        {
            long batches = checked(((long)request.Quantity + output - 1) / output);
            var rows = new List<GuidedProductionIngredientObservation>(r.Inputs.Count);
            foreach (var ingredient in r.Inputs)
            {
                if (ingredient.Gid <= 0 || ingredient.PerCraft <= 0 ||
                    !stock.TryReadBagCount(ingredient.Gid, out int bag) || bag < 0)
                    return Deny("compte inventaire non attribuable à la session");
                int chest = request.BankAllowed ? actions.ChestCount(ingredient.Gid) : 0;
                if (chest < 0) return Deny("stock coffre non observé");
                long needed = checked(batches * ingredient.PerCraft);
                long missingBag = Math.Max(0L, needed - bag);
                long missingAfterBank = Math.Max(0L, missingBag - chest);
                rows.Add(new(ingredient.Gid, needed, bag, chest, missingBag,
                    missingAfterBank, ingredient.BuyAllowed));
            }
            bool needsChest = rows.Any(row => row.MissingFromBag > 0 && row.ChestObserved > 0);
            bool needsBuyer = rows.Any(row => row.MissingAfterAuthorizedBank > 0 && row.BuyAllowed);
            bool needsGather = rows.Any(row => row.MissingAfterAuthorizedBank > 0 && !row.BuyAllowed);
            return new(true, "comptes issus du modèle local ; aucun reçu de récolte ou de craft",
                rows, needsChest, needsBuyer, needsGather, !needsGather);
        }
        catch (Exception e) when (e is OverflowException or ArgumentOutOfRangeException)
        {
            return Deny("quantité d'ingrédients hors limites arithmétiques");
        }
    }

    private static GuidedProductionStockPreflight Deny(string reason) =>
        new(false, reason, Array.Empty<GuidedProductionIngredientObservation>(),
            false, false, false, false);
}

/// <summary>
/// Per-character durable V12 stage machine. It NEVER dispatches gameplay
/// and cannot authorize an old action. The V9 production journal remains the
/// sole owner of an actual bank/buy/craft/list attempt.
///
/// A torn document/anchor replacement is permanently quarantined, not
/// silently reconstructed from a cached UI. A writer lock serializes
/// cross-process transitions; no automatic retry of InProgress/Uncertain.
/// </summary>
public sealed class GuidedProductionWorkflowLedger
{
    private const string Header = "GIVRE-GUIDED-FLOW-v12\n";
    private const string AnchorHeader = "GIVRE-GUIDED-FLOW-ANCHOR-v12|";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _root;

    public GuidedProductionWorkflowLedger(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("Workflow durable root required", nameof(root));
        _root = Path.GetFullPath(root);
    }

    public static string DefaultRoot() =>
        Path.Combine(MZP19LocalClone.DataRoot, "production", "guided-workflow-v12");

    /// <summary>
    /// An independent manual recipe must NOT bypass an unresolved old guided
    /// mutation after the old VM was detached or a process restarted.
    /// Ready/Blocked are only non-emitting preflight states; Reconciled
    /// requires the exact existing V9 receipt-bound completion.
    /// </summary>
    public static bool MayBeginIndependentManualProduction(
        GuidedProductionWorkflowSnapshot snapshot)
        => snapshot.Phase is GuidedProductionWorkflowPhase.Absent or
            GuidedProductionWorkflowPhase.Ready or
            GuidedProductionWorkflowPhase.Blocked or
            GuidedProductionWorkflowPhase.Reconciled;

    private string PathFor(int server, long character) =>
        Path.Combine(_root, "server-" + server.ToString(CultureInfo.InvariantCulture),
            "character-" + character.ToString(CultureInfo.InvariantCulture) + ".workflow");

    private static bool StorageError(Exception e) =>
        e is IOException or UnauthorizedAccessException or DecoderFallbackException
            or NotSupportedException or ArgumentException or JsonException or FormatException;

    private static string Sha(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    public static string Fingerprint(GuidedActivityProductionGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return Sha(JsonSerializer.SerializeToUtf8Bytes(goal));
    }

    private static GuidedProductionWorkflowSnapshot Uncertain(string message) =>
        new(GuidedProductionWorkflowPhase.Uncertain, "", "", Guid.Empty, message);

    private static GuidedProductionWorkflowSnapshot Empty =>
        new(GuidedProductionWorkflowPhase.Absent, "", "", Guid.Empty,
            "aucune étape de production guidée consignée");

    /// <summary>Failure to acquire a writer lock never looks like Ready.</summary>
    public GuidedProductionWorkflowSnapshot Read(int server, long character)
    {
        if (server <= 0 || character <= 0) return Uncertain("identité absente");
        string path = PathFor(server, character);
        try
        {
            // File.OpenCreate is not used by readers: merely inspecting the
            // record must not create a new workflow or alter evidence.
            using var lockFile = new FileStream(path + ".lock",
                FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return ReadUnsafe(path);
        }
        catch (FileNotFoundException)
        {
            // No lock yet is normal for a new profile, but not if a workflow
            // or anchor already exists. If storage is unreadable, opening a
            // known root fails closed rather than inventing absence.
            if (File.Exists(path) || File.Exists(path + ".anchor"))
                return Uncertain("workflow existant sans verrou lisible");
            // A second character under the same server may legitimately
            // have no workflow at all. Enumerate the actual parent instead
            // of using Directory.Exists(false), which hides ACL errors.
            try
            {
                return Directory.EnumerateFileSystemEntries(
                    Path.GetDirectoryName(path)!,
                    Path.GetFileName(path) + "*").Any()
                    ? Uncertain("fichiers ou staging workflow sans verrou")
                    : Empty;
            }
            catch (DirectoryNotFoundException) { return Empty; }
            catch (Exception e) when (StorageError(e))
            { return Uncertain("répertoire workflow inaccessible"); }
        }
        catch (DirectoryNotFoundException) { return Empty; }
        catch (Exception e) when (StorageError(e))
        { return Uncertain("lecture workflow impossible (" + e.GetType().Name + ")"); }
    }

    private static GuidedProductionWorkflowSnapshot ReadUnsafe(string path)
    {
        string text;
        try { text = File.ReadAllText(path, StrictUtf8); }
        catch (FileNotFoundException)
        {
            return File.Exists(path + ".anchor")
                ? Uncertain("ancre sans workflow ; perte de preuve possible") : Empty;
        }
        if (text.Length > 16_384 || !text.StartsWith(Header, StringComparison.Ordinal) ||
            !text.EndsWith('\n')) return Uncertain("document workflow tronqué");
        string[] parts = text[Header.Length..].TrimEnd('\n').Split('\n');
        if (parts.Length != 2 || parts[1].Length != 64)
            return Uncertain("format durable inconnu");
        byte[] data = Convert.FromBase64String(parts[0]);
        if (data.Length is <= 0 or > 8192 || Sha(data) != parts[1])
            return Uncertain("checksum workflow invalide");
        string anchor = File.ReadAllText(path + ".anchor", StrictUtf8);
        if (anchor != AnchorHeader + parts[1] + "\n")
            return Uncertain("ancre workflow déchirée ou divergente");
        var parsed = JsonSerializer.Deserialize<GuidedProductionWorkflowSnapshot>(data);
        if (parsed is null || parsed.GoalHash is null ||
            parsed.RequestHash is null || parsed.Reason is null ||
            !Enum.IsDefined(parsed.Phase) ||
            parsed.Phase == GuidedProductionWorkflowPhase.Absent ||
            parsed.GoalHash.Length != 64 || !parsed.GoalHash.All(Uri.IsHexDigit) ||
            parsed.RequestHash.Length != 64 || !parsed.RequestHash.All(Uri.IsHexDigit) ||
            parsed.Reason.Length > 1024 ||
            ((parsed.Phase is GuidedProductionWorkflowPhase.InProgress or
                GuidedProductionWorkflowPhase.Uncertain or GuidedProductionWorkflowPhase.Reconciled) &&
             parsed.LocalRunId == Guid.Empty))
            return Uncertain("état métier durable invalide");
        if (parsed.SupplyPlanHash is null || parsed.SupplySourceKey is null ||
            parsed.SupplySessionKey is null || parsed.SupplyConsultationsV17 is null)
            return Uncertain("checkpoint V13 incomplet");
        if ((parsed.SupplyPlanHash.Length > 0 &&
             (!ValidHash(parsed.SupplyPlanHash) ||
              !ValidHash(parsed.SupplySourceKey) ||
              !ValidHash(parsed.SupplySessionKey) ||
              parsed.SupplyObservationRevision <= 0 ||
              parsed.SupplyPhase is null ||
              !Enum.IsDefined(parsed.SupplyPhase.Value))) ||
            (parsed.SupplyPlanHash.Length == 0 &&
             (parsed.SupplySourceKey.Length != 0 ||
              parsed.SupplySessionKey.Length != 0 ||
              parsed.SupplyObservationRevision != 0 ||
              parsed.SupplyPhase is not null)) ||
            (parsed.SupplyChoiceV16 is { } choice &&
             (!choice.Valid || choice.PlanHash != parsed.SupplyPlanHash ||
              choice.SourceKey != parsed.SupplySourceKey ||
              choice.SessionKey != parsed.SupplySessionKey ||
              choice.ObservationRevision != parsed.SupplyObservationRevision ||
              (choice.HasExactPodsEvidenceV18 &&
               (choice.ObservedUsedPodsV18 != parsed.SupplyUsedPodsV18 ||
                choice.ObservedCapacityPodsV18 != parsed.SupplyCapacityPodsV18)))) ||
            !(parsed.SupplyUsedPodsV18 is null &&
              parsed.SupplyCapacityPodsV18 is null ||
              parsed.SupplyUsedPodsV18 is >= 0 &&
              parsed.SupplyCapacityPodsV18 is > 0 &&
              parsed.SupplyUsedPodsV18 <= parsed.SupplyCapacityPodsV18) ||
            (parsed.SupplyPlanHash.Length == 0 &&
             parsed.SupplyUsedPodsV18 is not null) ||
            !ValidConsultationHistory(parsed.SupplyConsultationsV17) ||
            (parsed.SupplyConsultationsV17.Length != 0 &&
             (parsed.SupplyChoiceV16 is null ||
              parsed.SupplyPlanHash.Length == 0 ||
              (IsConsultation(parsed.SupplyChoiceV16.Kind) &&
               !SplitConsultations(parsed.SupplyConsultationsV17).Contains(
                   ConsultationToken(parsed.SupplyChoiceV16),
                   StringComparer.Ordinal)))))
            return Uncertain("checkpoint d'approvisionnement V13 incohérent");
        return parsed;
    }

    public bool TryNotePreflight(GuidedActivityProductionGoal goal, string requestHash,
        bool ready, string reason)
    {
        if (goal is not { Valid: true } || !ValidHash(requestHash) ||
            string.IsNullOrWhiteSpace(reason)) return false;
        return Transition(goal.ServerId, goal.CharacterId, prior =>
        {
            // Never turn an in-flight or ambiguous request into Ready after a
            // simple inventory delta or a human re-open of the planner.
            if (prior.Phase is GuidedProductionWorkflowPhase.InProgress or
                GuidedProductionWorkflowPhase.Uncertain or
                GuidedProductionWorkflowPhase.AwaitingObservation or
                GuidedProductionWorkflowPhase.Reconciled) return null;
            // Legacy TryNotePreflight has no source/revision/recovery witness.
            // It cannot rewrite a source-bound preview after any V16 choice.
            // Explicit V13 checkpoint refreshes may supersede *inspection*
            // choices after a newer host inventory revision.
            if (prior.SupplyChoiceV16 is not null) return null;
            if (prior.Phase != GuidedProductionWorkflowPhase.Absent &&
                (prior.GoalHash != Fingerprint(goal) ||
                 prior.RequestHash != requestHash)) return null;
            if (prior.SupplyPlanHash.Length != 0 &&
                ready && (prior.SupplyPhase !=
                    GuidedSupplyPlanPhaseV13.ReadyForExistingRunner ||
                    prior.Phase != GuidedProductionWorkflowPhase.Ready))
                return null;
            if (prior.SupplyPlanHash.Length != 0)
                return prior with
                {
                    Phase = ready ? GuidedProductionWorkflowPhase.Ready :
                        GuidedProductionWorkflowPhase.Blocked,
                    Reason = reason[..Math.Min(reason.Length, 1000)]
                };
            return new(ready ? GuidedProductionWorkflowPhase.Ready :
                GuidedProductionWorkflowPhase.Blocked, Fingerprint(goal),
                requestHash, Guid.Empty, reason[..Math.Min(reason.Length, 1000)]);
        });
    }

    /// <summary>
    /// Explicit PREVIEW checkpoint only. The existing V9 production journal
    /// remains the only live mutation admission; this ledger cannot clear an
    /// ambiguous run. A stale source revision or cross-session cached plan
    /// never silently becomes Ready, even after app restart.
    /// </summary>
    public bool TryNoteSupplyPlan(GuidedActivityProductionGoal goal,
        GuidedSupplyPlanV13 plan)
    {
        if (goal is not { Valid: true } ||
            plan is not { EvidenceValid: true, ObservationRevision: > 0 } ||
            (plan.PodsObserved &&
             (plan.ObservedUsedPodsV18 is not >= 0 ||
              plan.ObservedCapacityPodsV18 is not > 0 ||
              plan.ObservedUsedPodsV18 > plan.ObservedCapacityPodsV18 ||
              (long)plan.ObservedUsedPodsV18.Value * 100 >=
                  (long)plan.ObservedCapacityPodsV18.Value * goal.MaxPods)) ||
            (!plan.PodsObserved &&
             (plan.ObservedUsedPodsV18 is not null ||
              plan.ObservedCapacityPodsV18 is not null)) ||
            !ValidHash(plan.GoalHash) || !ValidHash(plan.RequestHash) ||
            !ValidHash(plan.SourceKey) || !ValidHash(plan.SessionKey) ||
            !ValidHash(plan.PlanHash) ||
            plan.Reason is not { Length: > 0 and <= 1024 } ||
            plan.GoalHash != Fingerprint(goal) ||
            plan.Rows is not { Count: > 0 and <= 128 } ||
            plan.Rows.Any(r => r is null || r.Gid <= 0 || r.PerCraft <= 0 ||
                r.Required <= 0 || r.BagObserved < 0 || r.ChestObserved < 0 ||
                r.TakeFromBank < 0 || r.BuyCandidate < 0 ||
                r.GatherDeficit < 0 ||
                r.TakeFromBank + r.BuyCandidate + r.GatherDeficit !=
                    Math.Max(0L, r.Required - r.BagObserved)) ||
            plan.Phase is not (GuidedSupplyPlanPhaseV13.Blocked or
                GuidedSupplyPlanPhaseV13.AwaitingObservation or
                GuidedSupplyPlanPhaseV13.ReadyForExistingRunner) ||
            plan.PlanHash != GuidedSupplyPlannerV13.Fingerprint(plan.GoalHash,
                plan.RequestHash, plan.SourceKey, plan.SessionKey,
                plan.ObservationRevision, plan.Phase, plan.Reason, plan.Rows,
                plan.BankOptIn, plan.BuyerNeeded, plan.GatherNeeded,
                plan.GatherAdmissionPending, plan.PodsObserved))
            return false;
        return Transition(goal.ServerId, goal.CharacterId, prior =>
        {
            if (prior.Phase is GuidedProductionWorkflowPhase.InProgress or
                GuidedProductionWorkflowPhase.Uncertain or
                GuidedProductionWorkflowPhase.AwaitingObservation)
                return null;
            // A prior operator may already have left this planner for a
            // gameplay-capable route or the V9 runner. An inventory revision
            // alone cannot prove that the external admission did NOT send.
            // Pure inspection choices (bank/buy) may be superseded by an
            // explicit new V13 checkpoint because they never dispatch.
            if (prior.SupplyChoiceV16?.Kind is
                GuidedSupplyResumeStepV16.PrepareExactGatherRoute or
                GuidedSupplyResumeStepV16.UseExistingV9Runner)
                return null;
            if (prior.SupplyPlanHash.Length != 0 &&
                prior.GoalHash == plan.GoalHash &&
                (prior.SupplySessionKey != plan.SessionKey ||
                 prior.SupplyObservationRevision > plan.ObservationRevision ||
                 prior.SupplyObservationRevision == plan.ObservationRevision &&
                 // Read-only migration of an old V13 checkpoint is allowed
                 // once, only when it had NO operator choice, all prior plan
                 // bytes agree, and pods were not previously bound. This is
                 // never a replay or a new production lease.
                 !(prior.SupplyUsedPodsV18 is null &&
                   prior.SupplyCapacityPodsV18 is null &&
                   prior.SupplyChoiceV16 is null &&
                   prior.SupplyPlanHash == plan.PlanHash &&
                   prior.SupplySourceKey == plan.SourceKey &&
                   prior.SupplyPhase == plan.Phase &&
                   prior.Phase == (plan.MayOfferExistingRunner
                       ? GuidedProductionWorkflowPhase.Ready
                       : GuidedProductionWorkflowPhase.Blocked) &&
                   plan.PodsObserved)))
                return null;
            // Never rebind a non-reconciled old goal to another objective by
            // merely presenting a different stock cache.
            if (prior.Phase is (GuidedProductionWorkflowPhase.Ready or
                    GuidedProductionWorkflowPhase.Blocked) &&
                (prior.GoalHash != plan.GoalHash ||
                 prior.RequestHash != plan.RequestHash))
                return null;
            return new GuidedProductionWorkflowSnapshot(
                plan.MayOfferExistingRunner
                    ? GuidedProductionWorkflowPhase.Ready
                    : GuidedProductionWorkflowPhase.Blocked,
                plan.GoalHash, plan.RequestHash, Guid.Empty,
                ("approvisionnement V13 · " + plan.Reason)[..
                    Math.Min(1000, ("approvisionnement V13 · " + plan.Reason).Length)])
            {
                SupplyPlanHash = plan.PlanHash,
                SupplySourceKey = plan.SourceKey,
                SupplySessionKey = plan.SessionKey,
                SupplyObservationRevision = plan.ObservationRevision,
                SupplyPhase = plan.Phase,
                SupplyUsedPodsV18 = plan.ObservedUsedPodsV18,
                SupplyCapacityPodsV18 = plan.ObservedCapacityPodsV18
            };
        });
    }

    /// <summary>
    /// Atomically consumes one operator selection for the currently persisted
    /// preview. This stores an instruction only; it never reserves or releases
    /// a V9, HDV, bank, route, gather, or craft attempt. The caller must first
    /// reproduce the exact V13 candidate from host-bound observations.
    /// </summary>
    internal bool TryNoteExactSupplyChoice(GuidedActivityProductionGoal goal,
        string requestHash, GuidedSupplyExactChoiceV16 choice)
    {
        if (goal is not { Valid: true } || !ValidHash(requestHash) ||
            choice is not { Valid: true }) return false;
        return Transition(goal.ServerId, goal.CharacterId, prior =>
        {
            if (prior.Phase is not (GuidedProductionWorkflowPhase.Ready or
                    GuidedProductionWorkflowPhase.Blocked) ||
                prior.GoalHash != Fingerprint(goal) ||
                prior.RequestHash != requestHash ||
                prior.SupplyPlanHash != choice.PlanHash ||
                prior.SupplySourceKey != choice.SourceKey ||
                prior.SupplySessionKey != choice.SessionKey ||
                prior.SupplyObservationRevision != choice.ObservationRevision ||
                prior.SupplyUsedPodsV18 != choice.ObservedUsedPodsV18 ||
                prior.SupplyCapacityPodsV18 != choice.ObservedCapacityPodsV18 ||
                (choice.Kind == GuidedSupplyResumeStepV16.UseExistingV9Runner &&
                 prior.Phase != GuidedProductionWorkflowPhase.Ready) ||
                (prior.SupplyChoiceV16 is { } previous &&
                 !IsConsultation(previous.Kind)))
                return null;

            // A later choice is only possible after a purely consultative
            // one. Never re-offer that exact GID/quantity during this plan.
            if (!ValidConsultationHistory(prior.SupplyConsultationsV17))
                return null;
            var consulted = SplitConsultations(prior.SupplyConsultationsV17);
            if (prior.SupplyChoiceV16 is { } old &&
                IsConsultation(old.Kind) &&
                !consulted.Contains(ConsultationToken(old),
                    StringComparer.Ordinal))
                consulted = consulted.Append(ConsultationToken(old)).ToArray();
            string token = ConsultationToken(choice);
            if (IsConsultation(choice.Kind) &&
                consulted.Contains(token, StringComparer.Ordinal))
                return null;
            if (IsConsultation(choice.Kind))
            {
                if (consulted.Length >= 128) return null;
                consulted = consulted.Append(token).ToArray();
            }
            string history = string.Join("|", consulted);
            if (history.Length > 4096) return null;
            return prior with
            {
                SupplyChoiceV16 = choice,
                SupplyConsultationsV17 = history
            };
        });
    }

    internal static bool IsConsultation(GuidedSupplyResumeStepV16 kind) =>
        kind is GuidedSupplyResumeStepV16.InspectBank or
            GuidedSupplyResumeStepV16.InspectBuy;

    internal static string ConsultationToken(GuidedSupplyExactChoiceV16 choice) =>
        ((int)choice.Kind).ToString(CultureInfo.InvariantCulture) + ":" +
        choice.Gid.ToString(CultureInfo.InvariantCulture) + ":" +
        choice.Quantity.ToString(CultureInfo.InvariantCulture);

    internal static string[] SplitConsultations(string? encoded) =>
        string.IsNullOrEmpty(encoded)
            ? Array.Empty<string>()
            : encoded.Split('|');

    private static bool ValidConsultationHistory(string? encoded)
    {
        if (encoded is null || encoded.Length > 4096) return false;
        var tokens = SplitConsultations(encoded);
        return tokens.Length <= 128 &&
            tokens.All(ValidConsultationToken) &&
            tokens.Distinct(StringComparer.Ordinal).Count() == tokens.Length;
    }

    private static bool ValidConsultationToken(string? token)
    {
        if (token is not { Length: > 4 and <= 64 }) return false;
        var fields = token.Split(':');
        if (fields.Length != 3 ||
            !int.TryParse(fields[0], NumberStyles.None,
                CultureInfo.InvariantCulture, out int code) ||
            !Enum.IsDefined(typeof(GuidedSupplyResumeStepV16), code) ||
            !IsConsultation((GuidedSupplyResumeStepV16)code) ||
            !long.TryParse(fields[1], NumberStyles.None,
                CultureInfo.InvariantCulture, out long gid) || gid <= 0 ||
            !long.TryParse(fields[2], NumberStyles.None,
                CultureInfo.InvariantCulture, out long qty) || qty <= 0)
            return false;
        return token == code.ToString(CultureInfo.InvariantCulture) + ":" +
            gid.ToString(CultureInfo.InvariantCulture) + ":" +
            qty.ToString(CultureInfo.InvariantCulture);
    }

    public bool TryBegin(GuidedActivityProductionGoal goal, string requestHash,
        Guid actualProductionAttemptRun)
    {
        if (goal is not { Valid: true } || !ValidHash(requestHash) ||
            actualProductionAttemptRun == Guid.Empty) return false;
        return Transition(goal.ServerId, goal.CharacterId, prior =>
            prior.Phase == GuidedProductionWorkflowPhase.Ready &&
            prior.GoalHash == Fingerprint(goal) && prior.RequestHash == requestHash
                ? prior with
                {
                    Phase = GuidedProductionWorkflowPhase.InProgress,
                    LocalRunId = actualProductionAttemptRun,
                    Reason = "essai V9 commencé ; reçu exact encore attendu"
                } : null);
    }

    public bool TryCompleteFromActualProductionJournal(GuidedActivityProductionGoal goal,
        ItemCraftAttemptSnapshot attempt)
    {
        if (goal is not { Valid: true } || attempt.LocalRunId == Guid.Empty ||
            !ValidHash(attempt.RequestHash)) return false;
        return Transition(goal.ServerId, goal.CharacterId, prior =>
        {
            if (prior.Phase is not (GuidedProductionWorkflowPhase.InProgress or
                GuidedProductionWorkflowPhase.Uncertain) ||
                prior.GoalHash != Fingerprint(goal) ||
                prior.RequestHash != attempt.RequestHash ||
                prior.LocalRunId != attempt.LocalRunId) return null;
            return attempt.Phase switch
            {
                ItemCraftAttemptPhase.Completed => prior with
                {
                    Phase = GuidedProductionWorkflowPhase.Reconciled,
                    Reason = "run exact clôturé par le journal V9 après reçus vérifiés"
                },
                ItemCraftAttemptPhase.Uncertain => prior with
                {
                    Phase = GuidedProductionWorkflowPhase.Uncertain,
                    Reason = "reçus incomplets ou résultat indéterminé ; reprise interdite"
                },
                _ => null
            };
        });
    }

    public GuidedProductionWorkflowSnapshot Inspect(GuidedActivityProductionGoal goal,
        ActivityRecoveryDurableStatus activity, ItemCraftAttemptSnapshot actual)
    {
        if (goal is not { Valid: true }) return Uncertain("objectif non attesté");
        var stored = Read(goal.ServerId, goal.CharacterId);
        if (stored.Phase == GuidedProductionWorkflowPhase.Uncertain) return stored;
        if (stored.Phase != GuidedProductionWorkflowPhase.Absent &&
            stored.GoalHash != Fingerprint(goal))
            return Uncertain("objectif local remplacé sans clôture de l'ancien workflow");
        if (activity.Scope.ServerId != goal.ServerId ||
            activity.Scope.CharacterId != goal.CharacterId)
            return Uncertain("source ActivityRecovery différente");
        if (activity.State != ActivityRecoveryDurableState.Clear)
            return stored with
            {
                Phase = activity.State == ActivityRecoveryDurableState.Pending
                    ? GuidedProductionWorkflowPhase.AwaitingObservation
                    : GuidedProductionWorkflowPhase.Blocked,
                Reason = "activité " + activity.State +
                    " ; aucun ACK de route/récolte causal et aucune reprise automatique"
            };
        if (stored.Phase == GuidedProductionWorkflowPhase.InProgress)
        {
            if (actual.LocalRunId != stored.LocalRunId ||
                actual.RequestHash != stored.RequestHash ||
                actual.Phase == ItemCraftAttemptPhase.Uncertain)
                return stored with
                {
                    Phase = GuidedProductionWorkflowPhase.Uncertain,
                    Reason = "production interrompue/remplacée ; rapprochement exact requis"
                };
            if (actual.Phase == ItemCraftAttemptPhase.Completed)
                return stored with
                {
                    Phase = GuidedProductionWorkflowPhase.AwaitingObservation,
                    Reason = "reçu du run exact dans V9 ; confirmer la clôture du workflow"
                };
        }
        return stored;
    }

    private static bool ValidHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    private bool Transition(int server, long character,
        Func<GuidedProductionWorkflowSnapshot, GuidedProductionWorkflowSnapshot?> next)
    {
        if (server <= 0 || character <= 0) return false;
        try
        {
            string path = PathFor(server, character);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var ownership = new FileStream(path + ".lock",
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var current = ReadUnsafe(path);
            if (current.Phase == GuidedProductionWorkflowPhase.Uncertain) return false;
            var proposed = next(current);
            if (proposed is null || proposed.Phase == GuidedProductionWorkflowPhase.Absent ||
                proposed.GoalHash.Length != 64 || proposed.RequestHash.Length != 64)
                return false;
            byte[] content = JsonSerializer.SerializeToUtf8Bytes(proposed);
            if (content.Length > 8192) return false;
            string hash = Sha(content);
            string tmp = path + ".pending-" + Guid.NewGuid().ToString("N");
            string anchorTmp = path + ".anchor.pending-" + Guid.NewGuid().ToString("N");
            static void WriteDurable(string filename, byte[] bytes)
            {
                using var file = new FileStream(filename, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            WriteDurable(tmp, StrictUtf8.GetBytes(
                Header + Convert.ToBase64String(content) + "\n" + hash + "\n"));
            WriteDurable(anchorTmp, StrictUtf8.GetBytes(AnchorHeader + hash + "\n"));
            File.Move(tmp, path, overwrite: true);
            File.Move(anchorTmp, path + ".anchor", overwrite: true);
            return true;
        }
        catch (Exception e) when (StorageError(e)) { return false; }
    }
}
