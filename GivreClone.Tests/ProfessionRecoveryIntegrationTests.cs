using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GivreClone.App.Services.ProfessionMcp;
using GivreClone.App.Services.ProfessionPlanner;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// In-memory receipt ledger models an independent trusted provider. All recipe,
/// inventory and source data are artificial; no client or transport is accessed.
/// </summary>
public sealed class ProfessionRecoveryIntegrationTests
{
    private const string Version = "3.7.13.17";
    private static readonly string Manifest = new('A', 64);

    private static ProfessionMcpSnapshot Catalog(bool rights = true) =>
        new(new ProfessionPlanRequest(
                new ProfessionGoal(28, "Synthetic", 20, 30, 100, true),
                TrainingRecipeId: 120, RequestedCrafts: null,
                Recipes: new[]
                {
                    new ProfessionRecipe(120, 200, 1,
                        new[] { new RecipeIngredient(10, 2) }, 20,
                        JobId: 28, XpPerCraft: 10, XpVerified: true,
                        SourceVersion: Version, Verified: true,
                        SecondsPerCraft: 1, KamasPerCraft: 0)
                },
                Inventory: Array.Empty<ProfessionInventoryStack>(),
                Sources: Array.Empty<MaterialSource>(),
                XpTable: new XpTable(Version, true, new[]
                {
                    new XpThreshold(20, 100), new XpThreshold(30, 110)
                })),
            new ProfessionMcpProvenance("local-owned", "synthetic-recovery", Version,
                Manifest, rights, rights ? "independent-rights-review" : null));

    private static ProfessionMcpRecoveryEnvelope Evidence(
        long revision, long held, long cap, string session = "session-one",
        bool complete = true)
    {
        var inventory = held > 0
            ? new[] { new ProfessionInventoryStack("bag-observed", 10, held, "bag") }
            : Array.Empty<ProfessionInventoryStack>();
        var sources = new[]
        {
            new MaterialSource(10, AcquisitionMethod.Gather, 2, 1,
                Version, Verified: true, MaxUnits: cap)
        };
        var observed = new ProfessionRecoveryObservation("synthetic-subject",
            revision, Version, complete, complete, inventory, sources);
        var receipt = new ProfessionMcpRecoveryReceipt(observed.SubjectKey, session,
            revision, Version, Manifest,
            ProfessionMcpRecoveryIntegration.InventoryDigest(inventory),
            ProfessionMcpRecoveryIntegration.SourceCapsDigest(sources),
            "receipt-" + revision + "-" + session);
        return new(observed, receipt);
    }

    private sealed class IndependentReceiptLedger
    {
        private readonly Dictionary<string, ProfessionMcpRecoveryReceipt> _approved =
            new(StringComparer.Ordinal);

        public void Admit(ProfessionMcpRecoveryEnvelope envelope) =>
            _approved.Add(envelope.Receipt.ReceiptId, envelope.Receipt);

        public bool Verify(ProfessionMcpRecoveryReceipt receipt) =>
            _approved.TryGetValue(receipt.ReceiptId, out var known) && known == receipt;
    }

    private static ProfessionMcpRecoveryContext Context(
        ProfessionMcpRecoveryEnvelope current, IndependentReceiptLedger ledger,
        ProfessionMcpRecoveryState? previous = null, bool enabled = true) =>
        new(enabled, current, ledger.Verify, previous);

    private static JsonDocument Query(
        ProfessionMcpSnapshot? catalog, ProfessionMcpRecoveryContext? ctx,
        string json) => JsonDocument.Parse(ProfessionMcpFacade.QueryJson(json, catalog, ctx));

    private const string Activate =
        "{\"op\":\"recovery_preview\",\"activateRecovery\":true}";

    [Fact]
    public void Recovery_is_opt_in_with_explicit_status_diagnostics_before_activation()
    {
        var catalog = Catalog();
        var evidence = Evidence(1, held: 0, cap: 2);
        var ledger = new IndependentReceiptLedger();
        ledger.Admit(evidence);
        var ctx = Context(evidence, ledger);

        using var status = Query(catalog, ctx, "{\"op\":\"recovery_status\"}");
        Assert.Equal("UNKNOWN", status.RootElement.GetProperty("status").GetString());
        Assert.Equal("READY_FOR_EXPLICIT_ACTIVATION",
            status.RootElement.GetProperty("diagnostic").GetString());
        Assert.True(status.RootElement.GetProperty("evidenceAdmitted").GetBoolean());
        Assert.False(status.RootElement.GetProperty("activated").GetBoolean());
        Assert.Equal(0, status.RootElement.GetProperty("stepCount").GetInt32());

        using var unrequested = Query(catalog, ctx, "{\"op\":\"recovery_preview\"}");
        Assert.Equal("ACTIVATION_REQUIRED",
            unrequested.RootElement.GetProperty("diagnostic").GetString());
        Assert.Equal("UNKNOWN", unrequested.RootElement.GetProperty("status").GetString());

        using var activated = Query(catalog, ctx, Activate);
        Assert.Equal("KNOWN", activated.RootElement.GetProperty("status").GetString());
        Assert.Equal("PLAN_READY", activated.RootElement.GetProperty("diagnostic").GetString());
        Assert.True(activated.RootElement.GetProperty("activated").GetBoolean());
        Assert.Equal(1, activated.RootElement.GetProperty("revision").GetInt64());
        Assert.False(activated.RootElement.GetProperty("executed").GetBoolean());
        Assert.False(activated.RootElement.GetProperty("acknowledged").GetBoolean());
        Assert.False(activated.RootElement.GetProperty("clientContacted").GetBoolean());
    }

    [Fact]
    public void Crash_restart_with_persisted_state_replans_against_later_stock_and_remaining_cap()
    {
        var catalog = Catalog();
        var ledger = new IndependentReceiptLedger();
        var initialEvidence = Evidence(41, held: 0, cap: 2);
        ledger.Admit(initialEvidence);
        var first = ProfessionMcpRecoveryIntegration.Evaluate(catalog,
            Context(initialEvidence, ledger), activate: true);
        Assert.True(first.HasCurrentPlan);
        Assert.Equal(2, first.Proposal!.Steps.Single(s => s.Kind == PlanStepKind.Gather).Quantity);
        Assert.False(first.Proposal.Executed);

        // Simulate process crash by serializing and restoring the externally
        // persisted cursor, proposal and original observation; no in-memory ledger survives.
        string stored = JsonSerializer.Serialize(first.NextState);
        var restored = JsonSerializer.Deserialize<ProfessionMcpRecoveryState>(stored);
        Assert.NotNull(restored);
        var afterCrash = Evidence(42, held: 1, cap: 1);
        ledger.Admit(afterCrash);

        var resumed = ProfessionMcpRecoveryIntegration.Evaluate(catalog,
            Context(afterCrash, ledger, restored), activate: true);
        Assert.True(resumed.HasCurrentPlan);
        Assert.True(resumed.PreviousProposalInvalidated);
        Assert.Equal(42, resumed.NextState!.Checkpoint.Revision);
        Assert.Equal(1, resumed.Proposal!.UsedStock[10]);
        Assert.Single(resumed.Proposal.Steps, s => s.Kind == PlanStepKind.Gather && s.Quantity == 1);
        Assert.False(resumed.Proposal.Acknowledged);
        Assert.False(resumed.Proposal.Executed);

        using var mcp = Query(catalog, Context(afterCrash, ledger, restored), Activate);
        Assert.Equal("KNOWN", mcp.RootElement.GetProperty("status").GetString());
        Assert.True(mcp.RootElement.GetProperty("previousProposalInvalidated").GetBoolean());
    }

    [Fact]
    public void Shrinking_or_exhausted_caps_after_restart_never_inherit_previous_gather_reservation()
    {
        var catalog = Catalog();
        var ledger = new IndependentReceiptLedger();
        var initial = Evidence(1, 0, 2);
        var exhausted = Evidence(2, 0, 0);
        ledger.Admit(initial);
        ledger.Admit(exhausted);
        var before = ProfessionMcpRecoveryIntegration.Evaluate(catalog,
            Context(initial, ledger), true);

        var after = ProfessionMcpRecoveryIntegration.Evaluate(catalog,
            Context(exhausted, ledger, before.NextState), true);
        Assert.True(after.HasCurrentPlan);
        Assert.Equal("PLAN_PARTIAL", after.Diagnostic);
        Assert.False(after.Proposal!.IsComplete);
        Assert.Contains(after.Proposal.Steps, s => s.Kind == PlanStepKind.Unresolved);
        Assert.DoesNotContain(after.Proposal.Steps, s => s.Kind == PlanStepKind.Gather);
        Assert.Null(after.Proposal.FullKamasEstimate);
    }

    [Fact]
    public void Session_switch_or_replayed_revision_invalidates_persisted_proposal()
    {
        var catalog = Catalog();
        var ledger = new IndependentReceiptLedger();
        var initial = Evidence(8, 0, 2);
        ledger.Admit(initial);
        var first = ProfessionMcpRecoveryIntegration.Evaluate(catalog,
            Context(initial, ledger), true);
        var differentSession = Evidence(9, 0, 2, "session-two");
        ledger.Admit(differentSession);
        var swapped = ProfessionMcpRecoveryIntegration.Evaluate(catalog,
            Context(differentSession, ledger, first.NextState), true);
        Assert.Equal("SESSION_CHANGED", swapped.Diagnostic);
        Assert.True(swapped.PreviousProposalInvalidated);
        Assert.Null(swapped.Proposal);

        var replay = ProfessionMcpRecoveryIntegration.Evaluate(catalog,
            Context(initial, ledger, first.NextState), true);
        Assert.Equal("STALE_REVISION", replay.Diagnostic);
        Assert.Null(replay.NextState);
    }

    [Fact]
    public void Host_disabled_or_missing_trusted_verifier_only_exposes_unknown_diagnostics()
    {
        var catalog = Catalog();
        var ledger = new IndependentReceiptLedger();
        var observation = Evidence(1, 0, 2);
        ledger.Admit(observation);
        using var disabled = Query(catalog, Context(observation, ledger, enabled: false), Activate);
        Assert.Equal("HOST_DISABLED", disabled.RootElement.GetProperty("diagnostic").GetString());
        Assert.Equal("UNKNOWN", disabled.RootElement.GetProperty("status").GetString());
        using var noVerifier = Query(catalog,
            new ProfessionMcpRecoveryContext(true, observation, null), Activate);
        Assert.Equal("INDEPENDENT_VERIFIER_MISSING",
            noVerifier.RootElement.GetProperty("diagnostic").GetString());
        Assert.False(noVerifier.RootElement.GetProperty("activated").GetBoolean());

        using var noEvidence = Query(catalog,
            new ProfessionMcpRecoveryContext(true, observation, _ => false), Activate);
        Assert.Equal("CURRENT_OBSERVATION_UNVERIFIED",
            noEvidence.RootElement.GetProperty("diagnostic").GetString());
        Assert.Null(noEvidence.RootElement.GetProperty("revision").GetString());
    }

    [Fact]
    public void New_revision_without_activation_or_with_revoked_receipt_invalidates_old_preview()
    {
        var catalog = Catalog();
        var ledger = new IndependentReceiptLedger();
        var original = Evidence(5, 0, 2);
        var later = Evidence(6, 1, 1);
        ledger.Admit(original);
        ledger.Admit(later);
        var first = ProfessionMcpRecoveryIntegration.Evaluate(
            catalog, Context(original, ledger), true);
        Assert.True(first.HasCurrentPlan);

        using var readyButInactive = Query(catalog,
            Context(later, ledger, first.NextState), "{\"op\":\"recovery_status\"}");
        Assert.Equal("READY_FOR_EXPLICIT_ACTIVATION",
            readyButInactive.RootElement.GetProperty("diagnostic").GetString());
        Assert.True(readyButInactive.RootElement
            .GetProperty("previousProposalInvalidated").GetBoolean());
        Assert.Equal(0, readyButInactive.RootElement.GetProperty("stepCount").GetInt32());

        var unavailable = ProfessionMcpRecoveryIntegration.Evaluate(catalog,
            new ProfessionMcpRecoveryContext(true, later, _ => false, first.NextState), true);
        Assert.Equal("CURRENT_OBSERVATION_UNVERIFIED", unavailable.Diagnostic);
        Assert.True(unavailable.PreviousProposalInvalidated);
        Assert.Null(unavailable.NextState);
    }

    [Fact]
    public void Permission_or_provenance_failure_prevents_preview_even_with_receipt()
    {
        var ledger = new IndependentReceiptLedger();
        var observation = Evidence(1, 0, 2);
        ledger.Admit(observation);
        using var denied = Query(Catalog(rights: false), Context(observation, ledger), Activate);
        Assert.Equal("CATALOG_UNAVAILABLE", denied.RootElement.GetProperty("diagnostic").GetString());
        Assert.Equal("UNKNOWN", denied.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, denied.RootElement.GetProperty("gameVersion").ValueKind);
    }

    [Fact]
    public void Spoofed_receipt_digest_edited_stock_or_unknown_cap_is_rejected()
    {
        var ledger = new IndependentReceiptLedger();
        var original = Evidence(1, 0, 2);
        ledger.Admit(original);
        var alteredStock = original with
        {
            Observation = original.Observation with
            {
                Inventory = new[] { new ProfessionInventoryStack("fake-bag", 10, 999, "bag") }
            }
        };
        var forgedReceipt = original with
        {
            Receipt = original.Receipt with { SourceCapsSha256 = new string('B', 64) }
        };
        var unknownCap = original with
        {
            Observation = original.Observation with
            {
                Sources = new[] { original.Observation.Sources[0] with { MaxUnits = null } }
            }
        };
        foreach (var bad in new[] { alteredStock, forgedReceipt, unknownCap })
        {
            var result = ProfessionMcpRecoveryIntegration.Evaluate(Catalog(), Context(bad, ledger), true);
            Assert.Equal("CURRENT_OBSERVATION_UNVERIFIED", result.Diagnostic);
            Assert.Null(result.Proposal);
        }
    }

    [Fact]
    public void Recovery_query_forbids_client_supplied_stock_receipts_and_generic_activation()
    {
        var catalog = Catalog();
        foreach (string json in new[]
        {
            "{\"op\":\"recovery_preview\",\"activateRecovery\":true,\"revision\":999}",
            "{\"op\":\"recovery_preview\",\"activateRecovery\":true,\"inventory\":[]}",
            "{\"op\":\"recovery_preview\",\"activateRecovery\":true,\"sessionKey\":\"fake\"}",
            "{\"op\":\"recovery_status\",\"activateRecovery\":true}",
            "{\"op\":\"plan_job\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30,\"activateRecovery\":true}",
            "{\"op\":\"recovery_preview\",\"activateRecovery\":1}",
            "{\"op\":\"recovery_preview\",\"activateRecovery\":true,\"activateRecovery\":true}"
        })
        {
            using var denied = Query(catalog, null, json);
            Assert.Equal("INVALID_REQUEST", denied.RootElement.GetProperty("status").GetString());
        }

        using var oldCapabilities = Query(null, null, "{\"op\":\"capabilities\"}");
        Assert.Equal(4, oldCapabilities.RootElement.GetProperty("operations").GetArrayLength());
        Assert.Equal(2, oldCapabilities.RootElement.GetProperty("recoveryOperations").GetArrayLength());
        using var ordinaryPlan = Query(catalog, null,
            "{\"op\":\"plan_job\",\"jobId\":28,\"startLevel\":20,\"targetLevel\":30}");
        // The original MCP path still uses its original empty-source snapshot:
        // it must not borrow the separate recovery observation or source caps.
        Assert.Equal("UNKNOWN", ordinaryPlan.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void Tampered_persisted_state_requires_prior_receipt_and_plan_reverification()
    {
        var catalog = Catalog();
        var ledger = new IndependentReceiptLedger();
        var prior = Evidence(2, 0, 2);
        var next = Evidence(3, 1, 1);
        ledger.Admit(prior);
        ledger.Admit(next);
        var first = ProfessionMcpRecoveryIntegration.Evaluate(catalog, Context(prior, ledger), true);
        var modifiedOldStock = first.NextState! with
        {
            LastEvidence = first.NextState.LastEvidence with
            {
                Observation = first.NextState.LastEvidence.Observation with
                {
                    Inventory = new[] { new ProfessionInventoryStack("injected", 10, 2, "bag") }
                }
            }
        };
        var refusedStock = ProfessionMcpRecoveryIntegration.Evaluate(catalog,
            Context(next, ledger, modifiedOldStock), true);
        Assert.Equal("PRIOR_OBSERVATION_UNVERIFIED", refusedStock.Diagnostic);
        Assert.True(refusedStock.PreviousProposalInvalidated);

        var modifiedPlan = first.NextState with
        {
            Proposal = first.Proposal! with { Executed = true }
        };
        var refusedPlan = ProfessionMcpRecoveryIntegration.Evaluate(catalog,
            Context(next, ledger, modifiedPlan), true);
        Assert.Equal("RECOVERY_PREVIOUSPROPOSALCHANGED", refusedPlan.Diagnostic);
        Assert.Null(refusedPlan.Proposal);
    }
}
