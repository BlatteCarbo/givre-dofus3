using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.App.Services.NoCodeLot2;
using Xunit;

namespace GivreClone.Tests;

public class NoCodeLot2PlannerTests
{
    private static RoutineEvidence NoEvidence => new(
        new HashSet<long>(), new HashSet<(long, long)>(), new HashSet<long>(),
        new HashSet<long>(), new HashSet<long>(), new Dictionary<long, decimal>());
    private static RoutineInputs Args(params (string Key, long Value)[] pairs)
        => new(pairs.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));

    [Fact]
    public void Library_covers_five_workflows_with_stable_unique_ids()
    {
        Assert.Equal(5, RoutineLibrary.All.Count);
        Assert.Equal(RoutineLibrary.All.Count, RoutineLibrary.All.Select(t => t.Id).Distinct().Count());
        Assert.Contains(RoutineLibrary.All, t => t.Kind == RoutineKind.MineurGrotte && t.Steps.Any(s => s.Action == RoutineAction.Traverse));
        Assert.Contains(RoutineLibrary.All, t => t.Kind == RoutineKind.Donjon);
        Assert.Contains(RoutineLibrary.All, t => t.Kind == RoutineKind.HdvObservation);
        Assert.Contains(RoutineLibrary.All, t => t.Kind == RoutineKind.Combat);
        Assert.Contains(RoutineLibrary.All, t => t.Kind == RoutineKind.Banque);
    }

    [Fact]
    public void Underground_maps_without_verified_door_are_blocked_even_if_both_maps_are_known()
    {
        var t = RoutineLibrary.Find("mineur-grotte")!;
        var values = Args(("surface", 1), ("entree", 2), ("interieur", 3), ("minerai", 4), ("banque", 5));
        var evidence = NoEvidence with {
            KnownMapIds = new HashSet<long> { 1, 2, 3, 5 },
            KnownTransitions = new HashSet<(long, long)> { (1, 2) },
            KnownResourceTypeIds = new HashSet<long> { 4 }
        };
        var plan = RoutinePlanner.Preview(t, values, evidence);
        Assert.False(plan.Executed);
        Assert.False(plan.Acknowledged);
        Assert.True(plan.HasUnverifiedSteps);
        Assert.Contains(plan.Steps, s => s.Action == RoutineAction.Traverse &&
            s.Reason.Contains("grotte", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Steps, s => s.Action == RoutineAction.Harvest &&
            s.Verdict == EvidenceVerdict.MissingEvidence);
    }

    [Fact]
    public void Even_known_dungeon_key_and_boss_do_not_prove_a_run_or_server_ack()
    {
        var evidence = NoEvidence with
        {
            KnownMapIds = new HashSet<long> { 111, 112 },
            KnownTransitions = new HashSet<(long, long)> { (111, 112) },
            KnownItemGids = new HashSet<long> { 1234 },
            KnownMonsterIds = new HashSet<long> { 4321 }
        };
        var plan = RoutinePlanner.Preview(RoutineLibrary.Find("donjon")!,
            Args(("entree", 111), ("salle", 112), ("cle", 1234), ("monstre", 4321)), evidence);
        Assert.Equal(EvidenceVerdict.Verified, plan.Verdict);
        Assert.False(plan.Executed);
        Assert.False(plan.Acknowledged);
        Assert.All(plan.Steps, s => Assert.Contains("aucune action exécutée", s.Reason));
    }

    [Fact]
    public void Hdv_missing_market_price_remains_unverified_with_no_inferred_quote_or_order()
    {
        var template = RoutineLibrary.Find("hdv")!;
        var inputs = Args(("objet", 56), ("quantite", 10));
        var evidence = NoEvidence with { KnownItemGids = new HashSet<long> { 56 } };
        var missing = RoutinePlanner.Preview(template, inputs, evidence);
        Assert.Equal(EvidenceVerdict.MissingEvidence, missing.Verdict);
        Assert.Contains(missing.Steps, s => s.Reason.Contains("prix HDV", StringComparison.Ordinal));
        var priced = RoutinePlanner.Preview(template, inputs,
            evidence with { ObservedUnitPrices = new Dictionary<long, decimal> { [56] = 188m } });
        Assert.Equal(EvidenceVerdict.Verified, priced.Verdict);
        Assert.False(priced.Executed);
        Assert.False(priced.Acknowledged);
        Assert.DoesNotContain("ACKED", priced.Declaration, StringComparison.Ordinal);
    }

    [Fact]
    public void Bank_and_combat_are_never_simulated_as_successful_actions()
    {
        var bank = RoutinePlanner.Preview(RoutineLibrary.Find("banque")!, Args(("depart", 1), ("banque", 2)),
            NoEvidence with {
                KnownMapIds = new HashSet<long> { 1, 2 },
                KnownTransitions = new HashSet<(long, long)> { (1, 2) }
            });
        var combat = RoutinePlanner.Preview(RoutineLibrary.Find("combat")!, Args(("carte", 5), ("monstre", 6)),
            NoEvidence with { KnownMapIds = new HashSet<long> { 5 }, KnownMonsterIds = new HashSet<long> { 6 } });
        Assert.Contains(bank.Steps, s => s.Action == RoutineAction.Deposit && s.Verdict == EvidenceVerdict.MissingEvidence);
        Assert.Contains(combat.Steps, s => s.Action == RoutineAction.SimulateCombat && s.Verdict == EvidenceVerdict.MissingEvidence);
        Assert.False(bank.Acknowledged || combat.Acknowledged);
    }

    [Fact]
    public void Invalid_ids_missing_parameters_and_forged_template_fail_closed()
    {
        var t = RoutineLibrary.Find("hdv")!;
        var evidence = NoEvidence;
        var missing = RoutinePlanner.Preview(t, Args(("objet", 1)), evidence);
        Assert.Equal(EvidenceVerdict.MissingEvidence, missing.Verdict);
        var invalid = RoutinePlanner.Preview(t, Args(("objet", -1), ("quantite", 0)), evidence);
        Assert.Equal(EvidenceVerdict.InvalidInput, invalid.Verdict);
        var forged = t with { Steps = new[] { new RoutineStep("user-action", RoutineAction.InspectHdv) } };
        Assert.Throws<ArgumentException>(() => RoutinePlanner.Preview(forged,
            Args(("objet", 1), ("quantite", 10)), evidence));
    }

    [Fact]
    public void Same_snapshot_yields_deterministic_non_executable_preview()
    {
        var t = RoutineLibrary.Find("banque")!;
        var inputs = Args(("depart", 1), ("banque", 2));
        var a = RoutinePlanner.Preview(t, inputs, NoEvidence);
        var b = RoutinePlanner.Preview(t, inputs, NoEvidence);
        Assert.Equal(a.Declaration, b.Declaration);
        Assert.Equal(a.Steps, b.Steps);
        Assert.False(a.Executed);
    }
}
