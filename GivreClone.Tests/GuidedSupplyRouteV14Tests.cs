extern alias F06App;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MoonSharp.Interpreter;
using GivreClone.Protocol;
using Xunit;
using Xunit.Abstractions;
using Bots = F06App::GivreClone.App.ViewModels.BotsViewModel;
using Accounts = F06App::GivreClone.App.ViewModels.AccountsViewModel;
using Atelier = F06App::GivreClone.App.ViewModels.ItemCraftPlannerViewModel;
using RouteRequest = F06App::GivreClone.App.ViewModels.GuidedSupplyRoutePreparation;
using Goal = F06App::GivreClone.App.Services.Production.GuidedActivityProductionGoal;
using SupplySource = F06App::GivreClone.App.Services.Production.GuidedSupplySourceObservation;
using SupplyPlanner = F06App::GivreClone.App.Services.Production.GuidedSupplyPlannerV13;
using SupplyRead = F06App::GivreClone.App.Services.Production.GuidedProductionReadOnlyActions;
using Workflow = F06App::GivreClone.App.Services.Production.GuidedProductionWorkflowLedger;
using Activity = F06App::GivreClone.App.Services.ActivityRecoveryDurableStatus;
using ActivityScope = F06App::GivreClone.App.Services.ActivityRecoveryScope;
using ActivityPhase = F06App::GivreClone.App.Services.ActivityRecoveryDurableState;
using Attempt = F06App::GivreClone.App.Services.Production.ItemCraftAttemptSnapshot;
using AttemptPhase = F06App::GivreClone.App.Services.Production.ItemCraftAttemptPhase;
using Recipe = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRecipe;
using Ingredient = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionInput;
using Request = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftProductionRequest;
using Receipt = F06App::GivreClone.App.Services.ItemCraftPlanner.ItemCraftWorkshopReceipt;
using Port = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionActions;
using Stock = F06App::GivreClone.App.Services.ItemCraftPlanner.IItemCraftProductionStockObservation;

namespace GivreClone.Tests;

/// <summary>
/// V14 operator handoff: a specific missing ingredient reaches the existing
/// strict Lua script, with no fallback movement or synthetic server receipt.
/// </summary>
public sealed class GuidedSupplyRouteV14Tests
{
    private readonly ITestOutputHelper _output;
    public GuidedSupplyRouteV14Tests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Guided_route_local_generation_latency_and_allocations_are_measured_without_live_claim()
    {
        var goal = new Goal(292, 991001, 191105026, 7, 320,
            1934, 3, 95, true);
        var ticket = new RouteRequest(80001, goal, new string('A', 64), 11,
            3, new string('B', 64), new string('C', 64), new string('D', 64), 11);
        long start = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 128; i++)
        {
            Assert.True(Bots.TryBuildGuidedSupplyRouteScript(191105026,
                320, 95, ticket, out var draft, out var refusal), refusal);
            Assert.Contains("GUIDED_SUPPLY_GID = 11", draft);
        }
        clock.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        _output.WriteLine($"V14 128 in-process guided route drafts: " +
            $"{clock.Elapsed.TotalMilliseconds:F3} ms; {allocated} caller-thread bytes; " +
            "OFFLINE, no TCP/gameplay latency inference");
        Assert.InRange(allocated, 0L, 30_000_000L);
    }

    [Theory]
    [InlineData(10L)]
    [InlineData(287L)]
    [InlineData(1934L)]
    public void Exact_ingredient_gid_generates_a_single_real_table_only_gather(long gid)
    {
        Assert.True(Bots.TryBuildStrictActivityRouteScript(
            191105026, 320, gather: true, maxPods: 95, gatherItemGid: gid,
            out var script, out var refusal), refusal);
        Assert.Contains($"ITEMS_TO_GATHER = {{ {gid} }}", script);
        Assert.Contains("ACTIVITY_RECOVERY_TABLE_ONLY = true", script);
        Assert.Contains("map = 191105026, activityRecovery = true, gather = true", script);
        Assert.DoesNotContain("cell = 320", script);
        Assert.DoesNotContain("ELEMENTS_TO_GATHER", script);
        Assert.DoesNotContain("function move", script);
        Assert.DoesNotContain("function bank", script);
        var lua = new Script(CoreModules.Preset_SoftSandbox);
        lua.DoString(script); // actual MoonSharp parser/executor, no transport
        Assert.True(lua.Globals.Get("ACTIVITY_RECOVERY_TABLE_ONLY").Boolean);
        var route = lua.Globals.Get("ACTIVITY_RECOVERY_ROUTE").Table;
        var step = route.Get(1).Table;
        Assert.True(step.Get("gather").Boolean);
        Assert.True(step.Get("cell").IsNil()); // no fallback gameplay send
        Assert.Equal(gid, lua.Globals.Get("ITEMS_TO_GATHER").Table.Get(1).Number);
    }

    [Theory]
    [InlineData(false, 10L)]
    [InlineData(true, 0L)]
    [InlineData(true, -1L)]
    [InlineData(true, 2147483648L)]
    public void Non_gather_or_invalid_item_gid_never_generates_a_script(bool gather, long gid)
    {
        Assert.False(Bots.TryBuildStrictActivityRouteScript(
            191105026, 320, gather, 95, gid, out var script, out var why));
        Assert.Empty(script);
        Assert.Contains("aucun script", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Guided_one_shot_draft_embeds_exact_durable_plan_and_inventory_witness()
    {
        var goal = new Goal(292, 991001, 191105026, 7, 320,
            1934, 3, 95, true);
        var request = new RouteRequest(80001, goal, new string('A', 64),
            GatherItemGid: 11, Required: 3, PlanHash: new string('B', 64),
            SourceKey: new string('C', 64), SessionKey: new string('D', 64),
            InventoryRevision: 45);
        Assert.True(Bots.TryBuildGuidedSupplyRouteScript(191105027, 240, 95,
            request, out string luaText, out string why), why);
        Assert.Contains("ITEMS_TO_GATHER = { 11 }", luaText);
        Assert.DoesNotContain("cell = 240", luaText);
        var interpreter = new Script(CoreModules.Preset_SoftSandbox);
        interpreter.DoString(luaText);
        Assert.Equal(11, interpreter.Globals.Get("GUIDED_SUPPLY_GID").Number);
        Assert.Equal("3", interpreter.Globals.Get("GUIDED_SUPPLY_REQUIRED").String);
        Assert.Equal("45", interpreter.Globals.Get("GUIDED_SUPPLY_OBS_REV").String);
        Assert.Equal(new string('A', 64),
            interpreter.Globals.Get("GUIDED_SUPPLY_REQUEST_HASH").String);
        Assert.Equal(new string('B', 64),
            interpreter.Globals.Get("GUIDED_SUPPLY_PLAN_HASH").String);
        Assert.Equal(new string('C', 64),
            interpreter.Globals.Get("GUIDED_SUPPLY_SOURCE_KEY").String);
        Assert.Equal(new string('D', 64),
            interpreter.Globals.Get("GUIDED_SUPPLY_SESSION_KEY").String);
        Assert.Equal(F06App::GivreClone.App.Services.Production.GuidedProductionWorkflowLedger.Fingerprint(goal),
            interpreter.Globals.Get("GUIDED_SUPPLY_GOAL_HASH").String);
    }

    [Fact]
    public void Traversed_map_is_validated_as_current_source_not_conflated_with_immutable_goal_origin()
    {
        var goal = new Goal(292, 991001, 191105026, 7, 320,
            1934, 3, 95, true);
        var ticket = new RouteRequest(80001, goal, new string('A', 64),
            11, 3, new string('B', 64), new string('C', 64),
            new string('D', 64), 45);
        const long currentMapAfterTravel = 191105027;
        Assert.True(Bots.TryBuildGuidedSupplyRouteScript(
            currentMapAfterTravel, 240, 95, ticket, out var source,
            out var refusal), refusal);
        var lua = new Script(CoreModules.Preset_SoftSandbox);
        lua.DoString(source);
        Assert.Equal(currentMapAfterTravel,
            lua.Globals.Get("ACTIVITY_RECOVERY_ROUTE").Table.Get(1)
                .Table.Get("map").Number);
        Assert.NotEqual(goal.SourceMap, currentMapAfterTravel);
        string runtime = File.ReadAllText(Path.Combine(FindRepoRoot(),
            "GivreClone.App", "Services", "ActivityRecovery.Host.cs"));
        Assert.Contains("source.SourceMap == snapshot.Map.MapId", runtime);
        Assert.Contains("source.SourceMapGeneration == snapshot.Map.Generation", runtime);
        Assert.Contains("sourceHash == GuidedSupplyPlannerV13.SourceKey(source)", runtime);
        Assert.DoesNotContain("goal.SourceMap == snapshot.Map.MapId", runtime);
        Assert.DoesNotContain("goal.SourceMapGeneration == snapshot.Map.Generation", runtime);
    }

    [Theory]
    [InlineData(0L, 10L, true)]
    [InlineData(3L, 0L, true)]
    [InlineData(3L, 10L, false)]
    public void Incomplete_or_forged_guided_draft_is_not_persistable(
        long required, long revision, bool validHash)
    {
        var goal = new Goal(292, 991001, 191105026, 7, 320,
            1934, 3, 95, true);
        var ticket = new RouteRequest(80001, goal, new string('A', 64),
            GatherItemGid: 11, Required: required,
            PlanHash: validHash ? new string('B', 64) : "invalid hash",
            SourceKey: new string('C', 64), SessionKey: new string('D', 64),
            InventoryRevision: revision);
        Assert.False(Bots.TryBuildGuidedSupplyRouteScript(191105026, 320, 95,
            ticket, out string script, out string why));
        Assert.Empty(script);
        Assert.Contains("aucun script", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Real_atelier_and_lua_commands_deny_missing_owner_before_any_mutation()
    {
        var goal = new Goal(292, 991001, 191105026, 7, 320,
            1934, 3, 95, true);
        var ticket = new RouteRequest(80001, goal, new string('A', 64), 10);
        var atelier = new Atelier();
        Assert.False(atelier.GuidedSupplyMayPrepareRoute(ticket));
        var bots = new Bots(new Accounts());
        Assert.False(bots.ArmStrictActivityRouteForGuidedSupply(ticket, () => false));
        Assert.Contains("REFUS", bots.StrictRouteDraftStatus);
        Assert.Equal(0, bots.StrictRouteCraftGid.Length);
    }

    private sealed class LocalStock : Port, Stock
    {
        public int Dispatches;
        public bool SupportsStockObservation => true;
        public bool TryReadBagCount(long gid, out int count)
        {
            count = BagCount(gid);
            return true;
        }
        public int BagCount(long gid) => gid == 10 ? 6 : gid == 11 ? 3 : 0;
        public int ChestCount(long gid) => 0;
        public IReadOnlyList<MapInteractive> Interactives() => Array.Empty<MapInteractive>();
        public Task<bool> OpenChestAsync(CancellationToken ct) { Dispatches++; throw new InvalidOperationException("NO SEND"); }
        public Task<int> WithdrawAsync(long gid, int qty, CancellationToken ct) { Dispatches++; throw new InvalidOperationException("NO SEND"); }
        public Task<bool> CloseChestAsync(CancellationToken ct) { Dispatches++; throw new InvalidOperationException("NO SEND"); }
        public Task<Receipt> CraftAsync(int elementId, int skillUid,
            IReadOnlyList<(long Gid, int Qty)> ingredients, int count, CancellationToken ct)
        { Dispatches++; throw new InvalidOperationException("NO SEND"); }
        public Task<long> LotPriceAsync(long gid, int qty, CancellationToken ct)
        { Dispatches++; throw new InvalidOperationException("NO SEND"); }
        public Task<bool> BuyAsync(long gid, int qty, long price, CancellationToken ct)
        { Dispatches++; throw new InvalidOperationException("NO SEND"); }
        public Task<bool> ListAsync(long gid, int qty, long price, CancellationToken ct)
        { Dispatches++; throw new InvalidOperationException("NO SEND"); }
    }

    [Fact]
    public void Reopening_same_durable_preflight_is_readonly_not_a_second_checkpoint_write()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "givre-guided-v14-" + Guid.NewGuid().ToString("N"));
        try
        {
            var goal = new Goal(292, 991001, 191105026, 7, 320,
                1934, 3, 95, true);
            var request = new Request(new Recipe(1934, 1, 12, 0,
                [new Ingredient(10, 2, false), new Ingredient(11, 1, false)]),
                3, 1000, false, null);
            var data = new LocalStock();
            var source = new SupplySource(292, 991001, 191105026, 7,
                "real-source-token-simulated-offline", 10, 20, 100);
            SupplyRead Safe() => new(data, () => true, () => source);
            Activity state = new(new ActivityScope(292, 991001),
                ActivityPhase.Clear, null, 0, 0, "offline clear");
            var noAttempt = new Attempt(AttemptPhase.None, "none");
            var ledger = new Workflow(root);
            var first = SupplyPlanner.PrepareAndCheckpoint(goal, request, Safe(),
                state, noAttempt, ledger, explicitPreviewCheckpoint: true);
            Assert.True(first.CheckpointWritten);
            Assert.True(first.MayOfferExistingRunner);
            var prior = ledger.Read(292, 991001);
            Assert.True(SupplyPlanner.MatchesExistingReadOnlyCheckpoint(first, prior));
            string file = Path.Combine(root, "server-292",
                "character-991001.workflow");
            byte[] bytes = File.ReadAllBytes(file);
            var afterRestart = new Workflow(root);
            var second = SupplyPlanner.PrepareAndCheckpoint(goal, request, Safe(),
                state, noAttempt, afterRestart, explicitPreviewCheckpoint: true);
            Assert.False(second.CheckpointWritten); // V13 write-once preserved
            Assert.True(second.MayOfferExistingRunner);
            Assert.True(SupplyPlanner.MatchesExistingReadOnlyCheckpoint(
                second, afterRestart.Read(292, 991001)));
            Assert.Equal(bytes, File.ReadAllBytes(file));
            Assert.Equal(0, data.Dispatches);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Classic_handoff_stays_on_the_existing_commands_and_exact_gid_input()
    {
        string root = Path.Combine(FindRepoRoot(), "GivreClone.App");
        string panel = File.ReadAllText(Path.Combine(root, "Views",
            "ItemCraftPlannerPanel.axaml"));
        string main = File.ReadAllText(Path.Combine(root, "Views",
            "MainWindow.axaml.cs"));
        string route = File.ReadAllText(Path.Combine(root, "ViewModels",
            "BotsViewModel.ActivityRecovery.cs"));
        Assert.Contains("GuidedSupplyGatherItemGidText", panel);
        Assert.Contains("PrepareGuidedSupplyRouteCommand", panel);
        Assert.Contains("InspectGuidedHdvListingCommand", panel);
        Assert.Contains("GuidedSupplyListingEvidenceStatus", panel);
        Assert.Contains("InspectGuidedBankSupplyCommand", panel);
        Assert.Contains("ArmStrictActivityRouteForGuidedSupply(request,", main);
        Assert.Contains("PrepareStrictActivityRouteCommand.Execute(null)", main);
        Assert.Contains("ITEMS_TO_GATHER = { ", route);
        Assert.Contains("operation != Interlocked.Read(ref _strictRoutePrepareGeneration)", route);
        Assert.Contains("_guidedProductionStore.Read(ticket.Server, ticket.Character).Goal", route);
        Assert.Contains("GuidedSupplyHdvInspectionRequested +=", main);
        Assert.Contains("ObserveProductionListingAssessment(pid, assessment)", main);
        Assert.Contains("requireGuidedListingEvidence: goal is not null", main);
        Assert.Contains("ExecuteQuickNavigation(\"hdv\")", main);
        Assert.Contains("GuidedSupplyBankInspectionRequested +=", main);
        Assert.Contains("ExecuteQuickNavigation(\"bank\")", main);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir.FullName, "GivreClone.App")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
