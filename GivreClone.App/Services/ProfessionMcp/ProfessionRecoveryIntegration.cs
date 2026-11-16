using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using GivreClone.App.Services.ProfessionPlanner;

namespace GivreClone.App.Services.ProfessionMcp;

/// <summary>
/// Evidence metadata supplied by an independently controlled observation reader.
/// ReceiptId identifies an external audit receipt: the local SHA-256 values alone
/// prove neither a game event nor authorization to read it.
/// </summary>
public sealed record ProfessionMcpRecoveryReceipt(
    string SubjectKey, string SessionKey, long Revision, string CatalogVersion,
    string CatalogManifestSha256, string InventorySha256, string SourceCapsSha256,
    string ReceiptId);

public sealed record ProfessionMcpRecoveryEnvelope(
    ProfessionRecoveryObservation Observation, ProfessionMcpRecoveryReceipt Receipt);

/// <summary>
/// Persist this complete value after an admitted preview; load it on process restart.
/// No file I/O occurs in this module. Recovered state is reverified on every resume.
/// </summary>
public sealed record ProfessionMcpRecoveryState(
    ProfessionMcpRecoveryEnvelope LastEvidence,
    ProfessionRecoveryCheckpoint Checkpoint,
    ProfessionPlan Proposal);

/// <summary>
/// Provided by the hosting profession workflow, never constructed from MCP JSON.
/// Both the host opt-in and the request's explicit recovery activation are required.
/// The verifier MUST check an independent receipt bound to the inventory/cap hashes,
/// subject, session, revision, catalog version and manifest. A self-reported bool or
/// recalculated local hash does not count as independent evidence.
/// </summary>
public sealed record ProfessionMcpRecoveryContext(
    bool EnabledByHost,
    ProfessionMcpRecoveryEnvelope? CurrentEvidence,
    Func<ProfessionMcpRecoveryReceipt, bool>? VerifyIndependentReceipt,
    ProfessionMcpRecoveryState? PersistedState = null,
    long? TargetItemGid = null,
    long? TargetQuantity = null);

public sealed record ProfessionMcpRecoveryEvaluation(
    string Diagnostic,
    bool EvidenceAdmitted,
    bool Activated,
    bool PreviousProposalInvalidated,
    ProfessionPlan? Proposal,
    ProfessionMcpRecoveryState? NextState)
{
    public bool HasCurrentPlan => Activated && Proposal is not null && NextState is not null;
}

/// <summary>
/// Opt-in bridge to the existing profession MCP snapshot and the pure recovery
/// planner. All proposals remain read-only, with Executed/Acknowledged=false.
/// </summary>
public static class ProfessionMcpRecoveryIntegration
{
    public static ProfessionMcpRecoveryEvaluation Evaluate(
        ProfessionMcpSnapshot? catalog, ProfessionMcpRecoveryContext? context,
        bool activate)
    {
        if (catalog is null || !catalog.CanPlan())
            return Refuse("CATALOG_UNAVAILABLE", context?.PersistedState is not null);
        if (context?.EnabledByHost != true)
            return Refuse("HOST_DISABLED", context?.PersistedState is not null);
        if (context.VerifyIndependentReceipt is null)
            return Refuse("INDEPENDENT_VERIFIER_MISSING",
                context.PersistedState is not null);
        // The observation port is external and its IReadOnlyList fields may be
        // backed by mutable inventory/source collections. Freeze *before*
        // verifying and planning, so a late host callback cannot change the
        // already verified data between Admit, Plan and durable persistence.
        ProfessionMcpRecoveryEnvelope? current;
        try
        {
            current = context.CurrentEvidence is { Observation: not null, Receipt: not null } evidence
                ? Freeze(evidence) : null;
        }
        catch (Exception)
        {
            return Refuse("CURRENT_OBSERVATION_UNVERIFIED",
                context.PersistedState is not null);
        }
        if (!Admit(catalog, current, context.VerifyIndependentReceipt))
            return Refuse("CURRENT_OBSERVATION_UNVERIFIED",
                context.PersistedState is not null);

        var verifiedCurrent = current!;
        ProfessionMcpRecoveryState? verifiedPrevious = null;
        if (context.PersistedState is { } previous)
        {
            ProfessionMcpRecoveryEnvelope priorEvidence;
            try { priorEvidence = Freeze(previous.LastEvidence); }
            catch (Exception)
            { return Refuse("PRIOR_OBSERVATION_UNVERIFIED", true); }
            previous = previous with { LastEvidence = priorEvidence };
            verifiedPrevious = previous;
            if (context.TargetItemGid is { } target &&
                (previous.Checkpoint.ItemGid != target ||
                 previous.Checkpoint.ItemQuantity != context.TargetQuantity ||
                 previous.Checkpoint.Mode != ProfessionRecoveryMode.Item))
                return Refuse("CHECKPOINT_TARGET_CHANGED", true, evidenceAdmitted: true);
            if (!Admit(catalog, previous.LastEvidence, context.VerifyIndependentReceipt))
                return Refuse("PRIOR_OBSERVATION_UNVERIFIED", true);
            if (!string.Equals(previous.LastEvidence.Receipt.SessionKey,
                    verifiedCurrent.Receipt.SessionKey, StringComparison.Ordinal) ||
                !string.Equals(previous.LastEvidence.Observation.SubjectKey,
                    verifiedCurrent.Observation.SubjectKey, StringComparison.Ordinal))
                return Refuse("SESSION_CHANGED", true, evidenceAdmitted: true);
            if (verifiedCurrent.Observation.Revision <= previous.LastEvidence.Observation.Revision)
                return Refuse("STALE_REVISION", true, evidenceAdmitted: true);
        }

        if (!activate)
            return new("READY_FOR_EXPLICIT_ACTIVATION", true, false,
                context.PersistedState is not null, null, null);

        if ((context.TargetItemGid is null) != (context.TargetQuantity is null) ||
            context.TargetItemGid is <= 0 ||
            context.TargetQuantity is <= 0 or > 1_000_000_000L)
            return Refuse("INVALID_TARGET", context.PersistedState is not null,
                evidenceAdmitted: true);

        ProfessionRecoveryResult result;
        try
        {
            result = verifiedPrevious is { } prior
                ? ProfessionPlannerEngine.ResumeObserved(
                    catalog.Request, prior.Checkpoint, prior.Proposal,
                    prior.LastEvidence.Observation, verifiedCurrent.Observation)
                : context.TargetItemGid is { } target && context.TargetQuantity is { } quantity
                    ? ProfessionPlannerEngine.BeginObservedItem(catalog.Request,
                        verifiedCurrent.Observation, target, quantity)
                : ProfessionPlannerEngine.BeginObservedTraining(catalog.Request,
                        verifiedCurrent.Observation);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            return Refuse("RECOVERY_RECALCULATION_FAILED",
                context.PersistedState is not null, evidenceAdmitted: true);
        }

        if (!result.Accepted || result.Plan is null || result.Checkpoint is null)
            return Refuse("RECOVERY_" + result.Issue.ToString().ToUpperInvariant(),
                context.PersistedState is not null, evidenceAdmitted: true);
        if (result.Plan.Executed || result.Plan.Acknowledged)
            return Refuse("RECOVERY_NON_ADVISORY_PROPOSAL", context.PersistedState is not null,
                evidenceAdmitted: true);

        // Never carry forward the old proposal or a suggested action as an
        // observation; persist only the new, source-verified snapshot and cursor.
        var next = new ProfessionMcpRecoveryState(verifiedCurrent, result.Checkpoint,
            result.Plan);
        return new(result.Plan.IsComplete ? "PLAN_READY" : "PLAN_PARTIAL", true, true,
            result.PreviousProposalInvalidated, result.Plan, next);
    }

    private static ProfessionMcpRecoveryEvaluation Refuse(
        string diagnostic, bool invalidateOld, bool evidenceAdmitted = false) =>
        new(diagnostic, evidenceAdmitted, false, invalidateOld, null, null);

    private static bool Admit(ProfessionMcpSnapshot catalog, ProfessionMcpRecoveryEnvelope? envelope,
        Func<ProfessionMcpRecoveryReceipt, bool> verifier)
    {
        if (envelope?.Observation is not { } o || envelope.Receipt is not { } receipt ||
            o.Inventory is null || o.Sources is null ||
            !o.InventoryComplete || !o.SourceCapsComplete ||
            o.Revision <= 0 || o.Inventory.Count > ProfessionMcpSnapshot.MaxInventoryStacks ||
            o.Sources.Count > ProfessionMcpSnapshot.MaxAcquisitionSources ||
            !SafeLabel(o.SubjectKey, 128) || !SafeLabel(receipt.SessionKey, 128) ||
            !SafeLabel(receipt.ReceiptId, 128) ||
            !string.Equals(o.SubjectKey, receipt.SubjectKey, StringComparison.Ordinal) ||
            receipt.Revision != o.Revision ||
            !string.Equals(o.CatalogVersion, catalog.Provenance.GameVersion, StringComparison.Ordinal) ||
            !string.Equals(receipt.CatalogVersion, o.CatalogVersion, StringComparison.Ordinal) ||
            !string.Equals(receipt.CatalogManifestSha256,
                catalog.Provenance.ManifestSha256, StringComparison.OrdinalIgnoreCase) ||
            !IsDigest(receipt.InventorySha256) || !IsDigest(receipt.SourceCapsSha256))
            return false;

        // Unknown/unbounded supply is not independently attestable remaining
        // capacity. Zero means a verified exhausted source, never unlimited.
        if (o.Inventory.Any(s => s is null || !SafeLabel(s.StackId, 128) ||
                !SafeLabel(s.Container, 128) || s.ItemGid <= 0 ||
                s.Quantity is < 0 or > 1_000_000_000L) ||
            o.Inventory.GroupBy(s => s.StackId, StringComparer.Ordinal).Any(g => g.Count() > 1) ||
            o.Sources.Any(s => s is null || s.ItemGid <= 0 || !s.Verified ||
                s.MaxUnits is null or < 0 or > 1_000_000_000L ||
                s.Method == AcquisitionMethod.Craft || !Enum.IsDefined(s.Method) ||
                s.KamasPerUnit is < 0 || s.SecondsPerUnit is < 0 ||
                !string.Equals(s.SourceVersion, o.CatalogVersion, StringComparison.Ordinal)) ||
            o.Sources.GroupBy(s => (s.ItemGid, s.Method, s.SourceVersion))
                .Any(g => g.Count() > 1))
            return false;

        try
        {
            if (!string.Equals(receipt.InventorySha256, InventoryDigest(o.Inventory),
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(receipt.SourceCapsSha256, SourceCapsDigest(o.Sources),
                    StringComparison.OrdinalIgnoreCase))
                return false;
            return verifier(receipt);
        }
        catch (Exception)
        {
            // An unreachable/revoked verifier cannot authorize a preview.
            return false;
        }
    }

    /// <summary>Canonical local digest for an external receipt verifier to bind.</summary>
    public static string InventoryDigest(IReadOnlyList<ProfessionInventoryStack> stacks) =>
        Hash(stacks.OrderBy(s => s.StackId, StringComparer.Ordinal)
            .Select(s => new { s.StackId, s.ItemGid, s.Quantity, s.Container }).ToArray());

    /// <summary>Canonical local digest including caps, price, time and provenance.</summary>
    public static string SourceCapsDigest(IReadOnlyList<MaterialSource> sources) =>
        Hash(sources.OrderBy(s => s.ItemGid).ThenBy(s => s.Method)
            .ThenBy(s => s.SourceVersion, StringComparer.Ordinal)
            .Select(s => new
            {
                s.ItemGid, s.Method, s.SecondsPerUnit, s.KamasPerUnit,
                s.SourceVersion, s.Verified, s.MaxUnits
            }).ToArray());

    /// <summary>
    /// Copies the externally owned stock and capacity collections. In
    /// particular, a callback that mutates an IReadOnlyList-backed List after
    /// this return cannot change a checked proposal or its action identity.
    /// This does not itself attest the source; Admit still verifies the host
    /// receipt against both frozen digests.
    /// </summary>
    internal static ProfessionMcpRecoveryEnvelope Freeze(ProfessionMcpRecoveryEnvelope current) =>
        new(current.Observation with
        {
            Inventory = Array.AsReadOnly(current.Observation.Inventory.ToArray()),
            Sources = Array.AsReadOnly(current.Observation.Sources.ToArray())
        }, current.Receipt);

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static bool IsDigest(string? digest) =>
        digest is { Length: 64 } && digest.All(Uri.IsHexDigit);

    private static bool SafeLabel(string? value, int length) =>
        value is { Length: > 0 } && value.Length <= length &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':');
}
