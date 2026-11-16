extern alias F06App;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Store = F06App::GivreClone.App.Services.Production.GuidedActivityProductionStore;
using Goal = F06App::GivreClone.App.Services.Production.GuidedActivityProductionGoal;
using Stage = F06App::GivreClone.App.Services.Production.GuidedActivityProductionState;
using Recovery = F06App::GivreClone.App.Services.ActivityRecoveryDurableStatus;
using RecoveryScope = F06App::GivreClone.App.Services.ActivityRecoveryScope;
using RecoveryState = F06App::GivreClone.App.Services.ActivityRecoveryDurableState;
using Vm = F06App::GivreClone.App.ViewModels.ItemCraftPlannerViewModel;
using Bots = F06App::GivreClone.App.ViewModels.BotsViewModel;
using CatalogEntry = F06App::GivreClone.App.ViewModels.ItemCraftCatalogEntry;

namespace GivreClone.Tests;

/// <summary>Real fsync disk objective→UI source; no DOFUS, ACK or fabricated stock.</summary>
public sealed class GuidedActivityProductionHandoffV11Tests
{
    private readonly ITestOutputHelper _output;
    public GuidedActivityProductionHandoffV11Tests(ITestOutputHelper output) =>
        _output = output;

    private static Goal ValidGoal(int server = 292, long character = 9710) =>
        new(server, character, 191105026, 12, 320, 1934, 3, 95, true);

    private static Recovery Activity(int server, long character, RecoveryState state) =>
        new(new RecoveryScope(server, character), state, null, 0, 0,
            "offline activity receipt absent");

    private sealed class Fixture : IDisposable
    {
        public readonly string Root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "givre-guided-v11-" +
            Guid.NewGuid().ToString("N"));
        public readonly Store Store;
        public Fixture() => Store = new Store(Root);
        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Persist_restart_then_only_same_character_with_clear_activity_is_ready()
    {
        using var dir = new Fixture();
        var goal = ValidGoal();
        Assert.Equal(Stage.Absent, dir.Store.Read(goal.ServerId, goal.CharacterId).State);
        Assert.True(dir.Store.TrySave(goal));
        var afterRestart = new Store(dir.Root);
        Assert.Equal(goal, afterRestart.Read(goal.ServerId, goal.CharacterId).Goal);
        Assert.True(afterRestart.InspectWithRecovery(goal.ServerId,
            goal.CharacterId, Activity(292, 9710, RecoveryState.Clear)).MayPrepareProduction);
        Assert.Equal(Stage.AwaitingObservation, afterRestart.InspectWithRecovery(
            292, 9710, Activity(292, 9710, RecoveryState.Pending)).State);
        Assert.Equal(Stage.Blocked, afterRestart.InspectWithRecovery(
            292, 9710, Activity(353, 9710, RecoveryState.Clear)).State);
        Assert.Equal(Stage.Absent, afterRestart.Read(353, 9710).State);
    }

    [Theory]
    [InlineData(0, 9710L, 191105026L, 12L, 320, 1934, 3, 95)]
    [InlineData(292, 0L, 191105026L, 12L, 320, 1934, 3, 95)]
    [InlineData(292, 9710L, 0L, 12L, 320, 1934, 3, 95)]
    [InlineData(292, 9710L, 2147483648L, 12L, 320, 1934, 3, 95)]
    [InlineData(292, 9710L, 191105026L, 0L, 320, 1934, 3, 95)]
    [InlineData(292, 9710L, 191105026L, 12L, -1, 1934, 3, 95)]
    [InlineData(292, 9710L, 191105026L, 12L, 560, 1934, 3, 95)]
    [InlineData(292, 9710L, 191105026L, 12L, 320, 0, 3, 95)]
    [InlineData(292, 9710L, 191105026L, 12L, 320, 1934, 0, 95)]
    [InlineData(292, 9710L, 191105026L, 12L, 320, 1934, 100001, 95)]
    [InlineData(292, 9710L, 191105026L, 12L, 320, 1934, 3, 101)]
    public void Invalid_session_map_or_budget_is_never_persisted(
        int server, long character, long map, long generation,
        int cell, int gid, int qty, int pods)
    {
        using var dir = new Fixture();
        Assert.False(dir.Store.TrySave(new Goal(server, character,
            map, generation, cell, gid, qty, pods, true)));
    }

    [Fact]
    public void Corrupted_or_torn_objective_never_becomes_a_clean_new_job()
    {
        using var dir = new Fixture();
        var goal = ValidGoal();
        Assert.True(dir.Store.TrySave(goal));
        string path = System.IO.Path.Combine(dir.Root, "server-292",
            "character-9710.objective");
        File.AppendAllText(path, "untrusted\n");
        var bytes = File.ReadAllBytes(path);
        var restarted = new Store(dir.Root);
        Assert.Equal(Stage.Uncertain, restarted.Read(292, 9710).State);
        Assert.False(restarted.TrySave(goal with { Quantity = 5 }));
        Assert.Equal(bytes, File.ReadAllBytes(path)); // no repair on read/denial
        Assert.Equal(Stage.Uncertain, restarted.InspectWithRecovery(
            292, 9710, Activity(292, 9710, RecoveryState.Clear)).State);
    }

    [Fact]
    public void Missing_anchor_blocks_after_restart_without_silent_repair()
    {
        using var dir = new Fixture();
        var goal = ValidGoal();
        Assert.True(dir.Store.TrySave(goal));
        string anchor = System.IO.Path.Combine(dir.Root, "server-292",
            "character-9710.objective.anchor");
        File.Delete(anchor); // test fixture only
        Assert.Equal(Stage.Uncertain, new Store(dir.Root).Read(292, 9710).State);
        Assert.False(dir.Store.TrySave(goal));
    }

    [Fact]
    public void Real_viewmodel_and_classic_panel_link_to_existing_production_commands()
    {
        var vm = typeof(Vm);
        Assert.NotNull(vm.GetMethod("TryImportGuidedActivity",
            new[] { typeof(Goal) }));
        Assert.NotNull(vm.GetMethod("TryImportGuidedActivity",
            new[] { typeof(Goal), typeof(int), typeof(long) }));
        Assert.NotNull(vm.GetProperty("GuidedActivityGoal"));
        Assert.NotNull(vm.GetProperty("GuidedActivityStatus"));
        Assert.NotNull(vm.GetProperty("ClearGuidedActivityCommand"));
        Assert.NotNull(typeof(Bots).GetProperty("PrepareStrictActivityRouteCommand"));
        Assert.NotNull(typeof(Bots).GetProperty("OpenGuidedProductionCommand"));
        Assert.NotNull(typeof(Bots).GetProperty("InspectGuidedProductionCommand"));
        string root = FindRoot();
        string main = File.ReadAllText(System.IO.Path.Combine(root,
            "GivreClone.App", "Views", "MainWindow.axaml.cs"));
        string bots = File.ReadAllText(System.IO.Path.Combine(root,
            "GivreClone.App", "Views", "BotsView.axaml"));
        string craft = File.ReadAllText(System.IO.Path.Combine(root,
            "GivreClone.App", "Views", "ItemCraftPlannerPanel.axaml"));
        Assert.Contains("GuidedProductionHandoffRequested += OnGuidedProductionHandoff", main);
        Assert.Contains("new ItemCraftSessionBoundActions(original,", main);
        Assert.Contains("CurrentGuidedProductionAuthority(pid, goal, requireWorkflow: true)", main);
        Assert.Contains("new ItemCraftSessionBoundActions(original,", main);
        Assert.Contains("CurrentManualProductionAuthority(pid)", main);
        Assert.Contains("{Binding OpenGuidedProductionCommand}", bots);
        Assert.Contains("{Binding StrictRouteCraftGid", bots);
        Assert.Contains("{Binding GuidedActivityStatus}", craft);
        Assert.Contains("{Binding RunProductionCommand}", craft);
    }

    [Theory]
    [InlineData(RecoveryState.Clear, 292, 9710L, true)]
    [InlineData(RecoveryState.Pending, 292, 9710L, false)]
    [InlineData(RecoveryState.Corrupt, 292, 9710L, false)]
    [InlineData(RecoveryState.StorageUnavailable, 292, 9710L, false)]
    [InlineData(RecoveryState.Clear, 353, 9710L, false)]
    [InlineData(RecoveryState.Clear, 292, 9720L, false)]
    public void Manual_atelier_cannot_bypass_durable_activity_after_detaching_guided_goal(
        RecoveryState phase, int requestedServer, long requestedChar, bool allowed)
    {
        var status = Activity(292, 9710, phase);
        Assert.Equal(allowed,
            Store.MayUseManualProduction(status, requestedServer, requestedChar));
    }

    [Fact]
    public void Actual_atelier_vm_imports_verified_goal_through_real_selection_without_starting_a_craft()
    {
        var goal = ValidGoal();
        var vm = new Vm
        {
            CatalogProvider = () =>
            [
                new CatalogEntry("1934", "Recette vérifiée", "Objet",
                    "hash:offline-fixture-1934", true),
                new CatalogEntry("1935", "Autre recette", "Objet",
                    "hash:offline-fixture-1935", true)
            ]
        };
        Assert.True(vm.TryImportGuidedActivity(goal, 292, 9710));
        Assert.Equal(goal, vm.GuidedActivityGoal);
        var target = Assert.Single(vm.Targets);
        Assert.Equal("1934", target.ItemId);
        Assert.Equal(3, target.Quantity);
        Assert.True(vm.GuidedActivityTargetStillMatches(goal));
        Assert.False(vm.GuidedActivityTargetStillMatches(goal with { Quantity = 4 }));
        Assert.False(vm.IsProductionRunning);
        Assert.Equal(0, vm.ProductionProduced);
        Assert.Equal(0, vm.ProductionListed);
        Assert.False(vm.TryImportGuidedActivity(goal, 353, 9710));
        Assert.Equal(goal, vm.GuidedActivityGoal);
        Assert.Single(vm.Targets); // wrong source cannot change it
        vm.ClearGuidedActivityCommand.Execute(null);
        Assert.False(vm.GuidedActivityTargetStillMatches(goal));
        Assert.Null(vm.GuidedActivityGoal);
        Assert.Single(vm.Targets); // manual target remains, durable marker untouched
    }

    [Fact]
    public void Unverified_catalog_cannot_be_imported_into_real_production()
    {
        var vm = new Vm
        {
            CatalogProvider = () =>
            [
                new CatalogEntry("1934", "Faux item", "Objet",
                    "FICTIF", false)
            ]
        };
        Assert.False(vm.TryImportGuidedActivity(ValidGoal(), 292, 9710));
        Assert.Empty(vm.Targets);
        Assert.Null(vm.GuidedActivityGoal);
    }

    [Fact]
    public void Offline_real_verification_cost_is_bounded_per_small_persisted_objective()
    {
        using var dir = new Fixture();
        var goal = ValidGoal();
        Assert.True(dir.Store.TrySave(goal));
        long startBytes = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 48; i++)
            Assert.Equal(Stage.Ready, dir.Store.Read(292, 9710).State);
        clock.Stop();
        long allocation = GC.GetAllocatedBytesForCurrentThread() - startBytes;
        _output.WriteLine($"V11 exact persisted goal: 48 checksum+anchor reads, " +
            $"{clock.Elapsed.TotalMilliseconds:F3} ms, {allocation} caller-thread bytes; offline only");
        Assert.InRange(allocation, 0, 80_000_000);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(System.IO.Path.Combine(
                    dir.FullName, "GivreClone.App"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Source project absent");
    }
}
