using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GivreClone.App.Services.Forgemagie;
using GivreClone.App.Services.ItemCraftPlanner;
using GivreClone.App.Services.Production;
using GivreClone.App.Services.ProfessionPlanner;

namespace GivreClone.App.Services.ProfessionMcp;

/// <summary>
/// A craft completion witness must be attributed to a specific pending guided
/// action by a separately trusted host. The DTO itself is not an ACK.
/// </summary>
public sealed record ProfessionProductionCraftWitness(
    string ActionKey, string SessionKey, long ObservationRevision,
    long RecipeId, long ResultGid, long ConfirmedQuantity, string ReceiptId);

/// <summary>
/// The real IItemCraftProductionActions port only exposes BagCount and an
/// ItemCraftWorkshopReceipt; neither proves an inventory revision or a gather
/// capacity. A certified adapter must provide that evidence independently.
/// The ordinary ItemCraftProductionLaunch does not implement this interface.
/// </summary>
public interface IProfessionProductionCertifiedActions : IItemCraftProductionActions
{
    ProfessionMcpRecoveryContext ReadRecoveryContext();
    ProfessionProductionCraftWitness? ReadCraftWitness(string actionKey);
    bool VerifyCraftWitness(ProfessionProductionCraftWitness witness);
}

/// <summary>
/// Proof of a specifically attributed material acquisition, rather than a bag
/// delta that may come from a player, bank or unrelated run. The verifier must
/// consult an independent source; the record's fields alone certify nothing.
/// </summary>
public sealed record ProfessionProductionAcquisitionReceipt(
    string ActionKey, string SubjectKey, string SessionKey,
    long ObservationRevision, string Kind, long ItemGid,
    long ConfirmedQuantity, string ReceiptId);

/// <summary>
/// Attribution between a guided CRAFT checkpoint and an exact V9 production
/// attempt/confirmed workshop result. The V9 journal alone has no server receipt
/// identity; this link MUST be independently verified by the host.
/// </summary>
public sealed record ProfessionProductionV9CraftReceipt(
    string ActionKey, string SubjectKey, string SessionKey,
    long ObservationRevision, long RecipeId, string ReceiptId,
    string Profile, Guid RunId, uint Pid, string RequestHash,
    ItemCraftWorkshopReceipt WorkshopReceipt,
    // Optional actual V9 order quantity when a certified partial run completed
    // a smaller batch than the profession goal. Never inferred from bag stock.
    int? AttemptQuantity = null);

/// <summary>
/// Opt-in exact V9 recovery port. The host must pin both this action port and
/// the V9 attempt journal to the same server/character/run identity. The real
/// ItemCraftProductionAttemptJournal records only Start/Completed/Uncertain;
/// an external, independently verified receipt link is mandatory as well.
/// Existing adapters that do not implement this contract remain advisory.
/// </summary>
public interface IProfessionProductionV9Evidence : IProfessionProductionCertifiedActions
{
    string V9Profile { get; }
    ItemCraftProductionAttemptJournal V9Journal { get; }
    ProfessionProductionAcquisitionReceipt? ReadAcquisitionReceipt(string actionKey);
    bool VerifyAcquisitionReceipt(ProfessionProductionAcquisitionReceipt receipt);
    ProfessionProductionV9CraftReceipt? ReadV9CraftReceipt(string actionKey);
    bool VerifyV9CraftReceipt(ProfessionProductionV9CraftReceipt receipt);
}

public sealed record ProfessionProductionGuidance(
    string Diagnostic,
    ProfessionPlan? Plan,
    ProfessionProductionPendingAction? NextAction,
    long JournalSequence,
    bool CanSuggest,
    bool Written,
    bool PreviousProposalInvalidated);

/// <summary>
/// Read-only operator gate. V9's Completed phase and a receipt-shaped DTO do
/// not supply a durable, independently bound authorization for a NEW gameplay
/// send; no caller may treat a guided checkpoint as a mutation lease.
/// </summary>
public sealed record ProfessionProductionExecutionAdmission(
    string Diagnostic, bool CanExecute, long JournalSequence,
    string? PendingActionKey);

/// <summary>
/// Exact missing or unverifiable host evidence. A DTO implementing the V9
/// interface is not itself proof that its callbacks are independently trusted.
/// </summary>
public sealed record ProfessionProductionHostProofGap(string Code,
    string ExactHostEvidenceRequired);

public sealed record ProfessionProductionHostProofReport(
    string Diagnostic, string Activity, long JournalSequence,
    ItemCraftAttemptPhase? V9AttemptPhase,
    IReadOnlyList<ProfessionProductionHostProofGap> MissingOrUnverifiableProofs,
    bool CanAdmitMutation);

/// <summary>
/// Status of the actual goal/stock/recovery entry, for operators and the host.
/// A proposed action and a completed V9 journal are observations, not a send
/// lease. This inspection never commits a checkpoint or invokes gameplay.
/// </summary>
public sealed record ProfessionProductionJourneyStatus(
    string Diagnostic, long JournalSequence,
    string? PendingActionKey, string? PendingKind,
    long? CheckpointRevision, long? ObservedRevision,
    ItemCraftAttemptPhase? V9AttemptPhase,
    bool CanPreviewNextAction, bool CanExecute);

/// <summary>Unverified operator-supplied forge inputs yield read-only advice only.</summary>
public sealed record ProfessionProductionForgePreview(
    string Diagnostic, RuneAdvice? Advice, bool CanExecute);

/// <summary>
/// Production inputs are injected by the hosting application, never parsed from
/// MCP JSON. The runner ensures this is the same real bag/recipe port as RunAsync.
/// </summary>
public sealed record ProfessionMcpProductionContext(
    ItemCraftProductionRun Runner,
    ItemCraftProductionRequest Order,
    IItemCraftProductionActions Actions,
    IProfessionProductionJournal Journal);

/// <summary>
/// Genuine production-entry adapter: reads bag counts from the same action
/// port used by ItemCraftProductionRun, but only a separately verified source
/// can attest whole-stock revisions and remaining gather/drop capacities.
/// This method NEVER calls CraftAsync, BuyAsync or any movement/gameplay action.
/// </summary>
public static class ProfessionProductionWorkflow
{
    /// <summary>
    /// Describes the actual missing admission contract for the existing V9
    /// runner without claiming that local stock, a Completed journal phase,
    /// a wrapper interface or a forgery-shaped DTO is a server ACK. It does
    /// not call TryBegin, CraftAsync, BuyAsync or any gameplay operation.
    /// </summary>
    public static ProfessionProductionHostProofReport InspectHostProofGaps(
        string? requestedActivity, IItemCraftProductionActions? actions,
        IProfessionProductionJournal? journal)
    {
        var gaps = new List<ProfessionProductionHostProofGap>();
        static void Add(List<ProfessionProductionHostProofGap> to, string code, string need) =>
            to.Add(new(code, need));
        ProfessionProductionJournalRead? checkpoint = null;
        try { checkpoint = journal?.Read(); }
        catch (Exception) { }
        if (journal is null || checkpoint is null ||
            checkpoint.Status is not (ProfessionProductionJournalStatus.Empty or
                ProfessionProductionJournalStatus.Current) ||
            checkpoint.Status == ProfessionProductionJournalStatus.Current &&
                checkpoint.Entry is null)
            Add(gaps, "DURABLE_GUIDANCE_JOURNAL",
                "Atomic CAS read and fsynced current checkpoint, bound to one subject/session/goal/revision; corrupt or unavailable journals must be quarantined.");

        string activity = requestedActivity?.Trim().ToUpperInvariant() ??
            checkpoint?.Entry?.Pending?.Kind ?? "CRAFT";
        if (activity is "FM" or "FORGEMAGIE") activity = "FORGE";
        if (activity is not ("GATHER" or "DROP" or "BUY" or "CRAFT" or "FORGE"))
            activity = "UNKNOWN";

        if (activity != "FORGE" && checkpoint?.Entry?.Pending is null)
            Add(gaps, "GUIDED_ACTION_IDENTITY",
                "Existing host-owned ActionKey, exact goal GID/quantity, method/recipe, immutable subject/session and observed checkpoint revision; suggestions alone are not execution leases.");
        if (actions is not IProfessionProductionCertifiedActions)
            Add(gaps, "CERTIFIED_STOCK_PORT",
                "IProfessionProductionCertifiedActions wired to the SAME real ItemCraftProductionRun action port, complete inventory/finite source-cap revisions and BagCount cross-check.");

        // An interface declaration is not an authorization capability. The
        // reader must independently authenticate the receipt and prevent it
        // from being recycled across crashes, sessions and process restarts.
        Add(gaps, "INDEPENDENT_OBSERVATION_AUTHORITY",
            "Host-controlled verifier for subject/session/game version/manifest/revision, exact bag and source-cap digests, non-revoked receipt ID and immutable replay-safe evidence.");

        ItemCraftAttemptPhase? phase = null;
        if (activity is "CRAFT" or "BUY" or "GATHER" or "DROP")
        {
            if (actions is not IProfessionProductionV9Evidence)
                Add(gaps, "V9_EXACT_HOST_BRIDGE",
                    "Concrete IProfessionProductionV9Evidence implementation tied to the actual owner profile, V9 attempt journal and an independently verified action-key receipt ledger (none is supplied by the V9 journal alone).");
            else if (actions is IProfessionProductionV9Evidence v9)
            {
                try
                {
                    if (v9.V9Journal is null || string.IsNullOrWhiteSpace(v9.V9Profile))
                        Add(gaps, "V9_PROFILE_AND_JOURNAL",
                            "Valid server:character V9Profile and readable/fsynced V9 attempt journal for the owning gameplay process.");
                    else
                    {
                        phase = v9.V9Journal.ReadStatus(v9.V9Profile).Phase;
                        if (phase is ItemCraftAttemptPhase.Started or ItemCraftAttemptPhase.Uncertain)
                            Add(gaps, "V9_UNRECONCILED_ATTEMPT",
                                "Independent reconciliation of the existing Started/Uncertain V9 run, without implicit auto-clear or a second TryBegin.");
                    }
                }
                catch (Exception)
                {
                    Add(gaps, "V9_PROFILE_AND_JOURNAL",
                        "V9 attempt journal could not be read; no assumption of Completed or absence is permitted.");
                }
            }
        }

        if (activity == "CRAFT")
        {
            Add(gaps, "V9_PID_RUN_IDENTITY",
                "Independent binding of owning PID/process-start generation, profile, RequestFingerprint, V9 RunId and action key; V9 ReadStatus exposes only phase/run/hash, not PID or a server receipt.");
            Add(gaps, "SERVER_CRAFT_RECEIPT",
                "Host-authenticated workshop outcome tied to ActionKey, recipe ID, result GID, confirmed partial quantity, consumed ingredient deltas, subject/session/revision and exact V9 RunId; a local ItemCraftWorkshopReceipt is insufficient.");
        }
        else if (activity is "GATHER" or "DROP" or "BUY")
            Add(gaps, "SERVER_ACQUISITION_RECEIPT",
                "Host-authenticated per-action acquisition for exact method, GID, partial quantity and subject/session/revision, with matching bag increase and source-cap decrease; HDV/bank/local count changes alone are insufficient.");
        else if (activity == "FORGE")
        {
            Add(gaps, "FORGE_SERVER_OUTCOME",
                "Independent forge result: item UID, rune GID/tier and quantity consumed, before/after item effects and stock revisions, success/failure/reliquat and authoritative receipt correlation.");
            Add(gaps, "FORGE_ATTEMPT_OWNER",
                "Single durable forge attempt lease with owning process/session and crash/uncertain reconciliation; RuneAdvisor is pure advice, not a sender or an ACK.");
        }

        Add(gaps, "SINGLE_HOST_MUTATION_LEASE",
            "Durable host-owned send admission/lease linked to this ActionKey and existing V9 sender, with exclusive writer, process identity, pre/post-I/O revocation and replay protection; this profession API never supplies a mutation lease.");
        return new("HOST_PROOFS_MISSING_OR_UNVERIFIABLE", activity,
            checkpoint?.Sequence ?? 0, phase, gaps.AsReadOnly(), false);
    }

    /// <summary>
    /// Read-only, host-provenance-sensitive inspection of the existing real
    /// Prepare path. A second read detects concurrent CAS/double-click changes.
    /// On any missing/retired evidence it returns a useful blocking diagnostic.
    /// The actual V9 sender owns separate mutation admission and cannot be
    /// authorized by a profession checkpoint.
    /// </summary>
    public static ProfessionProductionJourneyStatus InspectJourney(
        ProfessionMcpSnapshot? catalog, ItemCraftProductionRequest? order,
        IItemCraftProductionActions? actions, IProfessionProductionJournal? journal)
    {
        static ProfessionProductionJourneyStatus Deny(string code,
            long sequence = 0, ProfessionProductionJournalEntry? entry = null,
            long? observedRevision = null, ItemCraftAttemptPhase? phase = null) =>
            new(code, sequence, entry?.Pending?.ActionKey, entry?.Pending?.Kind,
                entry?.RecoveryState?.Checkpoint?.Revision, observedRevision,
                phase, false, false);

        if (journal is null) return Deny("DURABLE_CAS_JOURNAL_MISSING");
        ProfessionProductionJournalRead first;
        try { first = journal.Read(); }
        catch (Exception) { return Deny("JOURNAL_UNAVAILABLE"); }
        if (first.Status == ProfessionProductionJournalStatus.Corrupt ||
            first.Status == ProfessionProductionJournalStatus.Unavailable)
            return Deny("JOURNAL_" + first.Status.ToString().ToUpperInvariant());
        if (first.Status == ProfessionProductionJournalStatus.Current && first.Entry is null)
            return Deny("JOURNAL_CORRUPT", first.Sequence);
        if (catalog is null || order is null || actions is null)
            return Deny("JOURNEY_INPUTS_MISSING", first.Sequence, first.Entry);

        ProfessionProductionGuidance guidance;
        try { guidance = Prepare(catalog, order, actions, journal, false); }
        catch (Exception) { return Deny("JOURNEY_OBSERVATION_UNAVAILABLE", first.Sequence, first.Entry); }

        ProfessionProductionJournalRead second;
        try { second = journal.Read(); }
        catch (Exception) { return Deny("JOURNAL_UNAVAILABLE", first.Sequence, first.Entry); }
        if (second.Status != first.Status || second.Sequence != first.Sequence ||
            second.Entry?.Pending?.ActionKey != first.Entry?.Pending?.ActionKey)
            return Deny("JOURNAL_CHANGED_DURING_INSPECTION", second.Sequence, second.Entry);

        long? revision = null;
        if (actions is IProfessionProductionCertifiedActions certified)
        {
            try { revision = certified.ReadRecoveryContext().CurrentEvidence?.Observation?.Revision; }
            catch (Exception)
            { return Deny("CERTIFIED_OBSERVATION_UNAVAILABLE", first.Sequence, first.Entry); }
        }

        ItemCraftAttemptPhase? phase = null;
        if (actions is IProfessionProductionV9Evidence v9 &&
            (first.Entry is null ||
             first.Entry.Pending is { EvidenceContract: "V9_EXACT", Kind: "CRAFT" }))
        {
            try
            {
                if (v9.V9Journal is null || string.IsNullOrWhiteSpace(v9.V9Profile))
                    return Deny("V9_JOURNAL_UNAVAILABLE", first.Sequence, first.Entry, revision);
                phase = v9.V9Journal.ReadStatus(v9.V9Profile).Phase;
            }
            catch (Exception)
            { return Deny("V9_JOURNAL_UNAVAILABLE", first.Sequence, first.Entry, revision); }
        }

        string code = guidance.Diagnostic;
        if (first.Entry?.Pending is { Kind: "CRAFT", EvidenceContract: "V9_EXACT" } &&
            code is "V9_CRAFT_RECEIPT_OR_ATTEMPT_UNVERIFIED" or
                "AWAITING_INDEPENDENT_OBSERVATION_OR_CRAFT_RECEIPT" or
                "SESSION_OR_REVISION_STALE")
        {
            code = phase switch
            {
                ItemCraftAttemptPhase.Started => "V9_CRAFT_STARTED_RECONCILIATION_REQUIRED",
                ItemCraftAttemptPhase.Uncertain => "V9_CRAFT_UNCERTAIN_RECONCILIATION_REQUIRED",
                ItemCraftAttemptPhase.Completed when
                    code == "AWAITING_INDEPENDENT_OBSERVATION_OR_CRAFT_RECEIPT" =>
                    "V9_CRAFT_RECEIPT_PENDING",
                ItemCraftAttemptPhase.Completed when
                    code == "V9_CRAFT_RECEIPT_OR_ATTEMPT_UNVERIFIED" =>
                    "V9_CRAFT_RECEIPT_ATTRIBUTION_UNVERIFIED",
                ItemCraftAttemptPhase.None when
                    code == "V9_CRAFT_RECEIPT_OR_ATTEMPT_UNVERIFIED" =>
                    "V9_CRAFT_ATTEMPT_MISSING",
                _ => code
            };
        }
        return new(code, first.Sequence, first.Entry?.Pending?.ActionKey,
            first.Entry?.Pending?.Kind,
            first.Entry?.RecoveryState?.Checkpoint?.Revision, revision, phase,
            guidance.CanSuggest, false);
    }

    public static ProfessionProductionExecutionAdmission InspectExecutionAdmission(
        ProfessionMcpSnapshot? catalog, ItemCraftProductionRequest? order,
        IItemCraftProductionActions? actions, IProfessionProductionJournal? journal)
    {
        var status = InspectJourney(catalog, order, actions, journal);
        return new(status.Diagnostic, false, status.JournalSequence,
            status.PendingActionKey);
    }

    /// <summary>
    /// Uses only the existing pure RuneAdvisor. There is no forge receipt,
    /// mutation lease, or independently authenticated item/rune stock in this
    /// port, so the result cannot authorize any forge action.
    /// </summary>
    public static ProfessionProductionForgePreview PreviewForge(
        IReadOnlyList<ForgeLine>? lines,
        IReadOnlyDictionary<(string Stat, string Tier), int>? stock,
        AdvisorSettings? settings = null)
    {
        if (lines is null || stock is null || lines.Count > 64 || stock.Count > 256 ||
            lines.Any(line => line is null ||
                string.IsNullOrWhiteSpace(line.Stat) || line.Stat.Length > 64 ||
                line.Min > line.Max || line.Cap < 0 ||
                Math.Abs((long)line.Current) > 1_000_000 ||
                Math.Abs((long)line.Target) > 1_000_000 ||
                Math.Abs((long)line.Min) > 1_000_000 ||
                Math.Abs((long)line.Max) > 1_000_000) ||
            stock.Any(entry => string.IsNullOrWhiteSpace(entry.Key.Stat) ||
                string.IsNullOrWhiteSpace(entry.Key.Tier) ||
                entry.Key.Stat.Length > 64 || entry.Key.Tier.Length > 8 ||
                entry.Value < 0 || entry.Value > 1_000_000))
            return new("FORGE_INPUTS_UNVERIFIED", null, false);
        try
        {
            return new("FORGE_ADVISORY_ONLY",
                RuneAdvisor.Advise(lines, stock, settings), false);
        }
        catch (Exception)
        { return new("FORGE_ADVISOR_UNAVAILABLE", null, false); }
    }

    /// <summary>
    /// Host-facing fail-closed status for production/forgemagie entry points.
    /// No existing host adapter binds a profession action to a live V9 mutation
    /// lease and an independent receipt source. This method never authorizes a
    /// send, even when the guidance journal was correctly checkpointed.
    /// </summary>
    public static ProfessionProductionExecutionAdmission InspectExecutionAdmission(
        IItemCraftProductionActions? actions, IProfessionProductionJournal? journal)
    {
        if (journal is null)
            return new("DURABLE_CAS_JOURNAL_MISSING", false, 0, null);
        ProfessionProductionJournalRead read;
        try { read = journal.Read(); }
        catch (Exception) { return new("JOURNAL_UNAVAILABLE", false, 0, null); }
        if (read.Status == ProfessionProductionJournalStatus.Current && read.Entry is null)
            return new("JOURNAL_CORRUPT", false, read.Sequence, null);
        if (read.Status != ProfessionProductionJournalStatus.Current || read.Entry is null)
            return new("JOURNAL_" + read.Status.ToString().ToUpperInvariant(),
                false, read.Sequence, null);
        if (read.Entry.Pending is null)
            return new("NO_PENDING_GUIDED_ACTION", false, read.Sequence, null);
        if (actions is not IProfessionProductionV9Evidence ||
            read.Entry.Pending.EvidenceContract != "V9_EXACT")
            return new("V9_HOST_EVIDENCE_UNAVAILABLE", false, read.Sequence,
                read.Entry.Pending.ActionKey);
        return new("V9_INDEPENDENT_MUTATION_LEASE_UNAVAILABLE", false,
            read.Sequence, read.Entry.Pending.ActionKey);
    }

    public static ProfessionProductionGuidance Prepare(
        ProfessionMcpSnapshot? catalog,
        ItemCraftProductionRequest? order,
        IItemCraftProductionActions? actions,
        IProfessionProductionJournal? journal,
        bool explicitActivation)
    {
        if (catalog is null || !catalog.CanPlan() || order?.Recipe is null ||
            !MatchesApprovedRecipe(catalog, order))
            return Block("RECIPE_OR_PROVENANCE_UNAVAILABLE");
        if (actions is not IProfessionProductionCertifiedActions certified)
            return Block("CERTIFIED_OBSERVATION_PORT_MISSING");
        if (journal is null)
            return Block("DURABLE_CAS_JOURNAL_MISSING");

        ProfessionProductionJournalRead journalRead;
        ProfessionMcpRecoveryContext? ctx;
        try
        {
            journalRead = journal.Read();
            if (journalRead.Status is not (ProfessionProductionJournalStatus.Empty or
                ProfessionProductionJournalStatus.Current))
                return Block("JOURNAL_" + journalRead.Status.ToString().ToUpperInvariant());
            ctx = certified.ReadRecoveryContext();
            if (ctx?.CurrentEvidence is { } evidence)
                ctx = ctx with { CurrentEvidence = ProfessionMcpRecoveryIntegration.Freeze(evidence) };
        }
        catch (Exception)
        {
            return Block("CERTIFIED_OBSERVATION_UNAVAILABLE");
        }
        if (ctx?.CurrentEvidence?.Observation is not { } current ||
            !ctx.EnabledByHost || ctx.VerifyIndependentReceipt is null)
            return Block("CERTIFIED_OBSERVATION_UNAVAILABLE");
        if (journalRead.Status == ProfessionProductionJournalStatus.Current &&
            journalRead.Entry is null)
            return Block("JOURNAL_CORRUPT");

        var previous = journalRead.Entry;
        string? confirmedReceiptId = null;
        Guid confirmedV9RunId = Guid.Empty;
        if (!MatchesRealBag(actions, order, current))
            return Block("BAG_SNAPSHOT_MISMATCH", previous?.Sequence ?? 0, previous is not null);
        if (previous is not null)
        {
            if (previous.Pending is null)
                return Block("CHECKPOINT_NO_PENDING_ACTION", previous.Sequence, true);
            // A strict checkpoint cannot silently fall back to advisory stock
            // deltas after crash, adapter replacement or reconnection.
            if (previous.Pending.EvidenceContract == "V9_EXACT" &&
                certified is not IProfessionProductionV9Evidence)
                return Block("V9_CERTIFIED_PORT_MISSING", previous.Sequence, true);
            if (certified is IProfessionProductionV9Evidence &&
                previous.Pending.EvidenceContract != "V9_EXACT")
                return Block("CHECKPOINT_EVIDENCE_CONTRACT_CHANGED", previous.Sequence, true);
            if (!string.Equals(previous.Pending.ActionKey,
                    ActionKey(previous.RecoveryState.Checkpoint.ProposalFingerprint,
                        previous.Pending.ObservationRevision, previous.Pending.StepId),
                    StringComparison.Ordinal))
                return Block("CHECKPOINT_ACTION_IDENTITY_MISMATCH", previous.Sequence, true);
            if (previous.RecoveryState.Checkpoint.Mode != ProfessionRecoveryMode.Item ||
                previous.RecoveryState.Checkpoint.ItemGid != order.Recipe.ResultGid ||
                previous.RecoveryState.Checkpoint.ItemQuantity != order.Quantity)
                return Block("CHECKPOINT_TARGET_CHANGED", previous.Sequence, true);
            if (!SameSession(previous, ctx) ||
                current.Revision <= previous.RecoveryState.LastEvidence.Observation.Revision)
                return Block("SESSION_OR_REVISION_STALE", previous.Sequence, true);
            var reconciliation = PendingReconciliationFailure(previous, current,
                order!, certified, out confirmedReceiptId, out confirmedV9RunId);
            if (reconciliation is not null)
                return Block(reconciliation, previous.Sequence, true);
            if (confirmedReceiptId is not null &&
                previous.ConsumedReceiptIds?.Contains(confirmedReceiptId, StringComparer.Ordinal) == true)
                return Block("CERTIFIED_RECEIPT_REPLAY", previous.Sequence, true);
            if (confirmedV9RunId != Guid.Empty &&
                previous.ConsumedV9RunIds?.Contains(confirmedV9RunId) == true)
                return Block("V9_ATTEMPT_REPLAY", previous.Sequence, true);
        }

        if (!ExtendEvidenceHistory(previous, confirmedReceiptId, confirmedV9RunId,
                out var usedReceipts, out var usedRuns))
            return Block("RECEIPT_HISTORY_EXHAUSTED", previous?.Sequence ?? 0,
                previous is not null);

        // Full source approval and replay of the original plan are performed by
        // ProfessionMcpRecoveryIntegration, before any new checkpoint is offered.
        var scoped = ctx with
        {
            PersistedState = previous?.RecoveryState,
            TargetItemGid = order!.Recipe.ResultGid,
            TargetQuantity = order.Quantity
        };
        ProfessionMcpRecoveryEvaluation evaluated;
        try { evaluated = ProfessionMcpRecoveryIntegration.Evaluate(catalog, scoped, activate: true); }
        catch (Exception)
        { return Block("RECOVERY_EVALUATION_FAILED", previous?.Sequence ?? 0, previous is not null); }
        if (!evaluated.HasCurrentPlan)
            return Block(evaluated.Diagnostic, previous?.Sequence ?? 0, previous is not null);

        var plan = evaluated.Proposal!;
        if (!plan.IsComplete || plan.Steps.Any(s => !s.Verified || s.Kind == PlanStepKind.Unresolved))
            return new("PARTIAL_OR_UNPROVEN_PLAN", plan, null, previous?.Sequence ?? 0,
                false, false, previous is not null);
        var step = plan.Steps.FirstOrDefault(s => s.Kind is
            PlanStepKind.Gather or PlanStepKind.Drop or PlanStepKind.Buy or PlanStepKind.Craft);
        // The existing V9 ItemCraftProductionRun is bound to the specific
        // approved output order. A recursive profession plan can put a craft
        // for an ingredient *before* the final craft. We have no certified
        // secondary workshop order or receipt binding for that sub-recipe.
        // Display the plan but never checkpoint an action the host cannot
        // identify against the real V9 request.
        if (step is { Kind: PlanStepKind.Craft } &&
            certified is IProfessionProductionV9Evidence &&
            (step.ItemGid != order.Recipe.ResultGid ||
             !catalog.Request.Recipes.Any(r => r.RecipeId == step.RecipeId &&
                 r.ResultGid == order.Recipe.ResultGid &&
                 r.ResultQty == order.Recipe.ResultPerCraft)))
            return new("NESTED_CRAFT_EXACT_V9_ORDER_MISSING", plan, null,
                previous?.Sequence ?? 0, false, false, previous is not null);
        // The genuine V9 TryBegin(profile, pid, request) will refuse an
        // unreconciled Started/Uncertain run. V17 could nevertheless checkpoint
        // a new guided action while that old lease was unresolved, misleading
        // operators and later consumers. Check real V9 ReadStatus before ANY
        // new guidance, including an acquisition queued before a craft.
        if (step is not null && certified is IProfessionProductionV9Evidence v9 &&
            InspectV9LeaseBlocker(v9) is { } v9Block)
            return Block(v9Block, previous?.Sequence ?? 0, previous is not null);
        if (step is null)
        {
            if (previous is null || !explicitActivation)
                return new("TARGET_ALREADY_IN_OBSERVED_STOCK", plan, null,
                    previous?.Sequence ?? 0, false, false, previous is not null);
            if (previous.Sequence == long.MaxValue)
                return Block("JOURNAL_SEQUENCE_EXHAUSTED", previous.Sequence, true);
            if (!EvidenceStillCurrent(certified, actions, order, previous,
                    evaluated.NextState!, confirmedReceiptId, confirmedV9RunId))
                return Block("CERTIFIED_EVIDENCE_CHANGED_BEFORE_COMMIT", previous.Sequence, true);
            var completed = new ProfessionProductionJournalEntry(previous.Sequence + 1,
                evaluated.NextState!, null, usedReceipts, usedRuns);
            ProfessionProductionJournalStatus completedStatus;
            try { completedStatus = journal.CompareExchange(previous.Sequence, completed); }
            catch (Exception)
            { return Block("JOURNAL_COMMIT_UNAVAILABLE", previous.Sequence, true); }
            return completedStatus == ProfessionProductionJournalStatus.Current
                ? new("TARGET_OBSERVED_CHECKPOINTED", plan, null, completed.Sequence,
                    false, true, true)
                : Block("JOURNAL_CAS_" + completedStatus.ToString().ToUpperInvariant(),
                    previous.Sequence, true);
        }

        // Only suggest an action, never imply it was applied. The journal's
        // checkpoint blocks a second suggestion until new independent evidence.
        var verifiedEvidence = evaluated.NextState!.LastEvidence;
        var pending = new ProfessionProductionPendingAction(
            ActionKey(evaluated.NextState.Checkpoint.ProposalFingerprint,
                verifiedEvidence.Observation.Revision, step.Id),
            verifiedEvidence.Receipt.SessionKey, verifiedEvidence.Observation.Revision,
            step.Id, step.Kind.ToString().ToUpperInvariant(), step.ItemGid,
            step.Quantity, step.RecipeId,
            certified is IProfessionProductionV9Evidence ? "V9_EXACT" : null);
        if (!explicitActivation)
            return new("NEXT_ACTION_REQUIRES_EXPLICIT_OPT_IN", plan, pending,
                previous?.Sequence ?? 0, true, false, previous is not null);

        long expected = previous?.Sequence ?? 0;
        if (expected == long.MaxValue)
            return Block("JOURNAL_SEQUENCE_EXHAUSTED", expected, true);
        if (!EvidenceStillCurrent(certified, actions, order, previous,
                evaluated.NextState!, confirmedReceiptId, confirmedV9RunId))
            return Block("CERTIFIED_EVIDENCE_CHANGED_BEFORE_COMMIT", expected,
                previous is not null);
        if (certified is IProfessionProductionV9Evidence finalV9 &&
            InspectV9LeaseBlocker(finalV9) is { } latestV9Block)
            return Block(latestV9Block, expected, previous is not null);
        var next = new ProfessionProductionJournalEntry(expected + 1,
            evaluated.NextState!, pending, usedReceipts, usedRuns);
        ProfessionProductionJournalStatus committed;
        try { committed = journal.CompareExchange(expected, next); }
        catch (Exception)
        { return Block("JOURNAL_COMMIT_UNAVAILABLE", expected, previous is not null); }
        if (committed != ProfessionProductionJournalStatus.Current)
            return Block("JOURNAL_CAS_" + committed.ToString().ToUpperInvariant(),
                expected, previous is not null);
        return new("GUIDED_ACTION_CHECKPOINTED", plan, pending,
            next.Sequence, true, true, previous is not null);
    }

    private static bool MatchesApprovedRecipe(ProfessionMcpSnapshot catalog,
        ItemCraftProductionRequest order)
    {
        var input = order.Recipe;
        if (catalog.Request.Goal.JobId is not > 0 ||
            order.Quantity is <= 0 or > 1_000_000_000 ||
            input.ResultGid <= 0 || input.ResultPerCraft is not > 0 ||
            input.SkillId is not > 0 || input.Inputs is null ||
            input.KamasPerCraft is null or < 0 || input.Inputs.Count > 64 ||
            order.MaxKamas < 0 || order.SaleFloor is not null)
            return false;
        var approved = catalog.Request.Recipes.Where(r =>
            r.ResultGid == input.ResultGid && r.ResultQty == input.ResultPerCraft &&
            r.Verified && r.JobId == catalog.Request.Goal.JobId &&
            r.SourceVersion == catalog.Provenance.GameVersion).ToArray();
        if (approved.Length != 1 || catalog.Request.Recipes.Count(r => r.ResultGid == input.ResultGid) != 1)
            return false;
        var chosen = approved[0];
        if (catalog.Request.TrainingRecipeId is { } id && id != chosen.RecipeId)
            return false;
        // The production runner spends the explicitly supplied workshop fee.
        // A recipe from the same GID/ingredient catalog with a different or
        // unknown fee is not the exact approved production request.
        if (chosen.KamasPerCraft is not decimal fee ||
            fee != input.KamasPerCraft.Value ||
            chosen.RequiredLevel > catalog.Request.Goal.StartLevel)
            return false;
        if (chosen.Ingredients.Count != input.Inputs.Count ||
            chosen.Ingredients.Any(i => input.Inputs.Count(x =>
                x.Gid == i.ItemGid && x.PerCraft == i.Quantity) != 1))
            return false;
        if (input.Inputs.Any(i => i.Gid <= 0 || i.PerCraft <= 0 || !chosen.Ingredients.Any(
                c => c.ItemGid == i.Gid && c.Quantity == i.PerCraft)))
            return false;
        return true;
    }

    private static bool MatchesRealBag(IItemCraftProductionActions actions,
        ItemCraftProductionRequest order, ProfessionRecoveryObservation observation)
    {
        if (!observation.InventoryComplete || !observation.SourceCapsComplete ||
            observation.Inventory is null || observation.Sources is null ||
            observation.Inventory.Count > 10000 ||
            observation.Inventory.Any(s => s is null ||
                !string.Equals(s.Container, "bag", StringComparison.OrdinalIgnoreCase)))
            return false;
        try
        {
            var counts = observation.Inventory.GroupBy(s => s.ItemGid)
                .ToDictionary(g => g.Key, g => checked(g.Sum(x => x.Quantity)));
            var ids = order.Recipe.Inputs.Select(i => i.Gid)
                .Append(order.Recipe.ResultGid).Concat(counts.Keys).Distinct().ToArray();
            foreach (long gid in ids)
            {
                int live = actions.BagCount(gid);
                if (live < 0 || live != counts.GetValueOrDefault(gid)) return false;
            }
            return true;
        }
        catch (Exception)
        { return false; }
    }

    private static bool SameSession(ProfessionProductionJournalEntry previous,
        ProfessionMcpRecoveryContext context) =>
        string.Equals(previous.Pending?.SessionKey,
            context.CurrentEvidence?.Receipt.SessionKey, StringComparison.Ordinal) &&
        string.Equals(previous.RecoveryState.LastEvidence.Observation.SubjectKey,
            context.CurrentEvidence?.Observation.SubjectKey, StringComparison.Ordinal);

    private static string? PendingReconciliationFailure(
        ProfessionProductionJournalEntry previous,
        ProfessionRecoveryObservation now,
        ItemCraftProductionRequest order,
        IProfessionProductionCertifiedActions certified,
        out string? confirmedReceiptId,
        out Guid confirmedV9RunId)
    {
        confirmedReceiptId = null;
        confirmedV9RunId = Guid.Empty;
        var before = previous.RecoveryState.LastEvidence.Observation;
        var pending = previous.Pending!;
        static long Count(ProfessionRecoveryObservation o, long gid) =>
            checked(o.Inventory.Where(s => s.ItemGid == gid).Sum(s => s.Quantity));
        try
        {
            long delta = checked(Count(now, pending.ItemGid) - Count(before, pending.ItemGid));
            if (pending.Kind == "CRAFT")
            {
                var witness = certified.ReadCraftWitness(pending.ActionKey);
                if (witness is not { ConfirmedQuantity: > 0, RecipeId: > 0 } ||
                    witness.ActionKey != pending.ActionKey ||
                    witness.SessionKey != pending.SessionKey ||
                    witness.ObservationRevision != now.Revision ||
                    witness.RecipeId != pending.RecipeId ||
                    witness.ResultGid != pending.ItemGid ||
                    witness.ReceiptId is not { Length: > 0 and <= 128 } ||
                    witness.ConfirmedQuantity > pending.Quantity ||
                    delta < witness.ConfirmedQuantity ||
                    !certified.VerifyCraftWitness(witness))
                    return "AWAITING_INDEPENDENT_OBSERVATION_OR_CRAFT_RECEIPT";
                if (certified is IProfessionProductionV9Evidence v9)
                {
                    if (!ExactV9Craft(v9, pending, before, now, order, witness,
                            delta, out var runId))
                        return "V9_CRAFT_RECEIPT_OR_ATTEMPT_UNVERIFIED";
                    confirmedReceiptId = witness.ReceiptId;
                    confirmedV9RunId = runId;
                }
                return null;
            }
            // Goods actually appeared in the fresh inventory snapshot. An
            // acquisition proposal alone does not satisfy a previous checkpoint.
            if (pending.Kind is not ("GATHER" or "DROP" or "BUY") || delta <= 0)
                return "AWAITING_INDEPENDENT_OBSERVATION_OR_CRAFT_RECEIPT";
            if (certified is IProfessionProductionV9Evidence exact)
            {
                if (!ExactAcquisition(exact, pending, before, now, delta,
                        out confirmedReceiptId))
                    return "V9_ACQUISITION_RECEIPT_UNVERIFIED";
            }
            return null;
        }
        catch (Exception)
        { return "AWAITING_INDEPENDENT_OBSERVATION_OR_CRAFT_RECEIPT"; }
    }

    private static bool ExactAcquisition(IProfessionProductionV9Evidence source,
        ProfessionProductionPendingAction pending,
        ProfessionRecoveryObservation before, ProfessionRecoveryObservation now, long delta,
        out string? confirmedReceiptId)
    {
        confirmedReceiptId = null;
        var receipt = source.ReadAcquisitionReceipt(pending.ActionKey);
        if (receipt is null || receipt.ActionKey != pending.ActionKey ||
            receipt.SubjectKey != now.SubjectKey || receipt.SessionKey != pending.SessionKey ||
            receipt.ObservationRevision != now.Revision ||
            receipt.Kind != pending.Kind || receipt.ItemGid != pending.ItemGid ||
            receipt.ConfirmedQuantity <= 0 || receipt.ConfirmedQuantity > pending.Quantity ||
            receipt.ConfirmedQuantity != delta || !SafeReceiptId(receipt.ReceiptId) ||
            !source.VerifyAcquisitionReceipt(receipt))
            return false;

        AcquisitionMethod method = pending.Kind switch
        {
            "GATHER" => AcquisitionMethod.Gather,
            "DROP" => AcquisitionMethod.Drop,
            "BUY" => AcquisitionMethod.Buy,
            _ => AcquisitionMethod.Craft
        };
        var priorCap = before.Sources.SingleOrDefault(s =>
            s.ItemGid == pending.ItemGid && s.Method == method);
        var currentCap = now.Sources.SingleOrDefault(s =>
            s.ItemGid == pending.ItemGid && s.Method == method &&
            s.SourceVersion == priorCap?.SourceVersion);
        if (priorCap?.MaxUnits is not long prior || currentCap?.MaxUnits is not long current ||
            prior < current || prior - current != receipt.ConfirmedQuantity)
            return false;
        confirmedReceiptId = receipt.ReceiptId;
        return true;
    }

    private static bool ExactV9Craft(IProfessionProductionV9Evidence source,
        ProfessionProductionPendingAction pending, ProfessionRecoveryObservation before,
        ProfessionRecoveryObservation now, ItemCraftProductionRequest order,
        ProfessionProductionCraftWitness witness, long delta, out Guid confirmedRunId)
    {
        confirmedRunId = Guid.Empty;
        var link = source.ReadV9CraftReceipt(pending.ActionKey);
        if (link is null || source.V9Journal is null ||
            link.ActionKey != pending.ActionKey || link.SubjectKey != now.SubjectKey ||
            link.SessionKey != pending.SessionKey || link.ObservationRevision != now.Revision ||
            link.RecipeId != pending.RecipeId || link.ReceiptId != witness.ReceiptId ||
            !SafeReceiptId(link.ReceiptId) ||
            !string.Equals(link.Profile, source.V9Profile, StringComparison.Ordinal) ||
            link.RunId == Guid.Empty || link.Pid == 0 ||
            !link.WorkshopReceipt.Confirmed ||
            link.WorkshopReceipt.ResultGid != pending.ItemGid ||
            link.WorkshopReceipt.Quantity != witness.ConfirmedQuantity ||
            delta != witness.ConfirmedQuantity ||
            !source.VerifyV9CraftReceipt(link))
            return false;

        int attemptQuantity = link.AttemptQuantity ?? order.Quantity;
        if (attemptQuantity <= 0 || attemptQuantity > order.Quantity ||
            !string.Equals(link.RequestHash,
                ItemCraftProductionAttemptJournal.RequestFingerprint(
                    order with { Quantity = attemptQuantity }), StringComparison.Ordinal))
            return false;

        var attempt = source.V9Journal.ReadStatus(source.V9Profile);
        if (attempt.Phase != ItemCraftAttemptPhase.Completed ||
            attempt.LocalRunId != link.RunId ||
            !string.Equals(attempt.RequestHash, link.RequestHash, StringComparison.Ordinal))
            return false;

        // Check the inputs genuinely disappeared from the next certified bag
        // revision as a corroboration of the exact confirmed workshop quantity.
        if (order.Recipe.ResultPerCraft is not int yield || yield <= 0 ||
            witness.ConfirmedQuantity % yield != 0 ||
            witness.ConfirmedQuantity < attemptQuantity ||
            witness.ConfirmedQuantity - attemptQuantity >= yield)
            return false;
        long crafts = witness.ConfirmedQuantity / yield;
        foreach (var input in order.Recipe.Inputs)
        {
            long oldCount = checked(before.Inventory.Where(s => s.ItemGid == input.Gid)
                .Sum(s => s.Quantity));
            long newCount = checked(now.Inventory.Where(s => s.ItemGid == input.Gid)
                .Sum(s => s.Quantity));
            if (checked(oldCount - newCount) != checked(input.PerCraft * crafts))
                return false;
        }
        confirmedRunId = link.RunId;
        return true;
    }

    private static bool ExtendEvidenceHistory(ProfessionProductionJournalEntry? previous,
        string? receiptId, Guid runId,
        out IReadOnlyList<string>? receipts, out IReadOnlyList<Guid>? runs)
    {
        receipts = previous?.ConsumedReceiptIds;
        runs = previous?.ConsumedV9RunIds;
        if ((receiptId is not null && !SafeReceiptId(receiptId)) ||
            receipts is { Count: >= 2048 } && receiptId is not null ||
            runs is { Count: >= 2048 } && runId != Guid.Empty)
            return false;
        if (receiptId is not null)
            receipts = (receipts ?? Array.Empty<string>()).Append(receiptId).ToArray();
        if (runId != Guid.Empty)
            runs = (runs ?? Array.Empty<Guid>()).Append(runId).ToArray();
        return true;
    }

    private static string? InspectV9LeaseBlocker(IProfessionProductionV9Evidence v9)
    {
        try
        {
            if (v9.V9Journal is null || string.IsNullOrWhiteSpace(v9.V9Profile))
                return "V9_ATTEMPT_JOURNAL_UNAVAILABLE";
            var snapshot = v9.V9Journal.ReadStatus(v9.V9Profile);
            return snapshot.Phase switch
            {
                ItemCraftAttemptPhase.Started => "V9_PREEXISTING_ATTEMPT_STARTED",
                ItemCraftAttemptPhase.Uncertain => "V9_PREEXISTING_ATTEMPT_UNCERTAIN",
                ItemCraftAttemptPhase.None or ItemCraftAttemptPhase.Completed => null,
                _ => "V9_ATTEMPT_JOURNAL_UNAVAILABLE"
            };
        }
        catch (Exception) { return "V9_ATTEMPT_JOURNAL_UNAVAILABLE"; }
    }

    private static bool EvidenceStillCurrent(
        IProfessionProductionCertifiedActions source,
        IItemCraftProductionActions actions,
        ItemCraftProductionRequest order,
        ProfessionProductionJournalEntry? previous,
        ProfessionMcpRecoveryState proposed,
        string? confirmedReceiptId,
        Guid confirmedRunId)
    {
        try
        {
            // Independent observations may be revoked or replaced between the
            // initial plan and durable CAS. Revalidate against the frozen plan
            // snapshot and the actual bag immediately before writing.
            var latest = source.ReadRecoveryContext();
            var observed = latest.CurrentEvidence is { } sourceEvidence
                ? ProfessionMcpRecoveryIntegration.Freeze(sourceEvidence) : null;
            var frozen = proposed.LastEvidence;
            if (!latest.EnabledByHost || latest.VerifyIndependentReceipt is null ||
                observed?.Observation is null || observed.Receipt != frozen.Receipt ||
                !observed.Observation.InventoryComplete ||
                !observed.Observation.SourceCapsComplete ||
                observed.Observation.SubjectKey != frozen.Observation.SubjectKey ||
                observed.Observation.Revision != frozen.Observation.Revision ||
                observed.Observation.CatalogVersion != frozen.Observation.CatalogVersion ||
                !string.Equals(ProfessionMcpRecoveryIntegration.InventoryDigest(
                        observed.Observation.Inventory), frozen.Receipt.InventorySha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(ProfessionMcpRecoveryIntegration.SourceCapsDigest(
                        observed.Observation.Sources), frozen.Receipt.SourceCapsSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !latest.VerifyIndependentReceipt(frozen.Receipt) ||
                previous is not null &&
                    !latest.VerifyIndependentReceipt(previous.RecoveryState.LastEvidence.Receipt) ||
                !MatchesRealBag(actions, order, frozen.Observation))
                return false;

            if (previous is null) return true;
            return PendingReconciliationFailure(previous, frozen.Observation, order,
                    source, out var repeatedReceiptId, out var repeatedRunId) is null &&
                string.Equals(repeatedReceiptId, confirmedReceiptId, StringComparison.Ordinal) &&
                repeatedRunId == confirmedRunId;
        }
        catch (Exception) { return false; }
    }

    private static bool SafeReceiptId(string? value) =>
        value is { Length: > 0 and <= 128 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':');

    private static string ActionKey(string proposalFingerprint, long revision, int stepId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            proposalFingerprint + "|" + revision + "|" + stepId)));

    private static ProfessionProductionGuidance Block(
        string code, long sequence = 0, bool invalidate = false) =>
        new(code, null, null, sequence, false, false, invalidate);
}
