extern alias F06App;

using System.Reflection;
using GivreClone.Protocol;
using Xunit;
using ForgeHistoryPage = F06App::GivreClone.App.Services.ForgeHistoryPage;
using ForgeHistoryStore = F06App::GivreClone.App.Services.ForgeHistoryStore;
using JobProgressSnapshot = F06App::GivreClone.App.Services.JobProgressSnapshot;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using Stores = F06App::GivreClone.App.Services.Stores;
using SmithMagicViewModel = F06App::GivreClone.App.ViewModels.SmithMagicViewModel;
using SmithSessionHistoryRow = F06App::GivreClone.App.ViewModels.SmithSessionHistoryRow;

namespace GivreClone.Tests;

public sealed class SmithHistoryPerformanceTests
{
    [Fact]
    public async Task Changing_bot_cancels_history_wait_even_if_the_original_bot_is_selected_again()
    {
        var vm = Model();
        var pidField = typeof(SmithMagicViewModel).GetField("_pid", BindingFlags.Instance | BindingFlags.NonPublic)!;
        pidField.SetValue(vm, 101);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ForgeHistoryReady = () => ready.Task;
        var pending = (Task<bool>)Method("WaitForForgeHistoryAsync").Invoke(vm, null)!;
        vm.AttachPid(0);
        pidField.SetValue(vm, 101);
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        ready.SetResult();
        Assert.False(vm.Busy);
    }

    [Fact]
    public async Task Stop_while_history_is_loading_refuses_the_pending_start()
    {
        var vm = Model();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ForgeHistoryReady = () => ready.Task;
        var pending = (Task<bool>)Method("WaitForForgeHistoryAsync").Invoke(vm, null)!;
        Assert.False(pending.IsCompleted);
        Assert.True(vm.Starting);
        Assert.True(vm.Busy);
        Assert.False(await (Task<bool>)Method("WaitForForgeHistoryAsync").Invoke(vm, null)!);
        vm.StopCommand.Execute(null);
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(vm.Busy);
        ready.SetResult();
        Assert.True(await (Task<bool>)Method("WaitForForgeHistoryAsync").Invoke(vm, null)!);
    }

    [Fact]
    public async Task Corrupt_history_refuses_the_pending_start_without_faulting_the_command()
    {
        var vm = Model();
        vm.ForgeHistoryReady = () => Task.FromException(new System.IO.InvalidDataException("corrupt archive"));
        Assert.False(await (Task<bool>)Method("WaitForForgeHistoryAsync").Invoke(vm, null)!);
        Assert.False(vm.Running);
    }

    private static MethodInfo Method(string name) =>
        typeof(SmithMagicViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!;

    private static IEnumerable<SmithSessionHistoryRow> Project(ForgeHistoryPage page) =>
        (IEnumerable<SmithSessionHistoryRow>)Method("ProjectForgeHistory").Invoke(null, [page])!;

    // Drive the timer boundary explicitly; no dispatcher pump, clock delays or live forge.
    private static SmithMagicViewModel Model()
    {
        var vm = new SmithMagicViewModel();
        Stores.Launch.ForgeHistory.Ready.GetAwaiter().GetResult();
        object timer = typeof(SmithMagicViewModel)
            .GetField("_forgeHistoryTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!;
        Assert.Equal(TimeSpan.FromSeconds(1), timer.GetType().GetProperty("Interval")!.GetValue(timer));
        timer.GetType().GetMethod("Stop")!.Invoke(timer, null);
        return vm;
    }

    [Fact]
    public void Refresh_requests_coalesce_until_one_tick_and_idle_ticks_do_nothing()
    {
        var vm = Model();
        int notifications = 0;
        vm.SessionHistory.CollectionChanged += (_, _) => notifications++;
        Method("FlushForgeHistory").Invoke(vm, null);
        notifications = 0;
        var store = new ForgeHistoryStore();
        store.Changed += Method("RefreshForgeHistory").CreateDelegate<Action>(vm);
        var jobs = new Dictionary<int, JobProgressSnapshot>();
        var id = store.Begin(101, "a", null, "ui", jobs);
        Parallel.For(0, 10000, _ => store.Record(id, "rune", jobs, outcome: InventoryReader.CraftSuccess));
        Assert.Equal(0, notifications);

        Method("FlushForgeHistory").Invoke(vm, null);
        Assert.Equal(1, notifications);
        for (int i = 0; i < 100; i++) Method("FlushForgeHistory").Invoke(vm, null);
        Assert.Equal(1, notifications);

        vm.RefreshForgeHistoryCommand.Execute(null);
        Method("FlushForgeHistory").Invoke(vm, null);
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void CanStart_changes_for_the_same_character_do_not_refresh_history()
    {
        var vm = Model();
        Method("FlushForgeHistory").Invoke(vm, null);
        int notifications = 0;
        vm.SessionHistory.CollectionChanged += (_, _) => notifications++;
        vm.Running = true;
        vm.Running = false;
        Method("FlushForgeHistory").Invoke(vm, null);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void Refresh_requested_during_a_flush_is_preserved_for_the_next_tick()
    {
        var vm = Model();
        int notifications = 0;
        vm.SessionHistory.CollectionChanged += (_, _) =>
        {
            if (++notifications == 1) vm.RefreshForgeHistoryCommand.Execute(null);
        };
        Method("FlushForgeHistory").Invoke(vm, null);
        Method("FlushForgeHistory").Invoke(vm, null);
        Method("FlushForgeHistory").Invoke(vm, null);
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void Rune_evidence_projects_one_session_row_without_removing_measured_data()
    {
        var store = new ForgeHistoryStore();
        var before = new Dictionary<int, JobProgressSnapshot> { [44] = new(10, 1200) };
        var after = new Dictionary<int, JobProgressSnapshot> { [44] = new(11, 1350) };
        var id = store.Begin(101, "a", 44, "ui", before);
        for (int i = 0; i < 2000; i++)
            store.Record(id, "rune", after, rune: "Fo", outcome: InventoryReader.CraftSuccess, pool: i);
        store.Record(id, "cash", after, cashExpense: 180);
        var page = store.Read(limit: 2000);

        Assert.Single(Project(page));
        Assert.Equal(2000, page.Entries.Count);
        Assert.True(page.Truncated);
        var session = Assert.Single(page.Sessions);
        Assert.Equal(2000, session.RuneCount);
        Assert.Equal(180, session.ActualCashExpense);
        Assert.Equal(1200, session.InitialProgress[44].Experience);
        Assert.Equal(1350, session.LatestProgress[44].Experience);
        Assert.Equal(InventoryReader.CraftSuccess, page.Entries.First(e => e.Kind == "rune").Outcome);
        Assert.Equal(1999, page.Entries.Last(e => e.Kind == "rune").Pool);
        var retained = store.Read(limit: 2000);
        Assert.Equal(page.Entries.ToArray(), retained.Entries.ToArray());
        Assert.Equal(page.Sessions.ToArray(), retained.Sessions.ToArray());
    }

    [Fact]
    public void Session_projection_has_an_explicit_row_bound()
    {
        var store = new ForgeHistoryStore(sessionLimit: 256);
        for (int i = 0; i < 150; i++) store.Begin(101, "a", null, "ui", new Dictionary<int, JobProgressSnapshot>());
        var page = store.Read(limit: 2000);
        Assert.Equal(150, page.Sessions.Count);
        Assert.Equal(128, Project(page).Count());
    }

    [Fact]
    public void Adaptive_batches_keep_exact_outcomes_and_a_compact_bounded_feed()
    {
        var vm = Model();
        LaunchService.ForgeStepReport[] steps =
        [
            new(1, "Fo", InventoryReader.CraftSuccess, new Dictionary<string, int>(), 3),
            new(2, "Fo", InventoryReader.CraftNeutral, new Dictionary<string, int>(), 4),
            new(3, "Fo", InventoryReader.CraftFailed, new Dictionary<string, int>(), 5),
            new(4, "Fo", InventoryReader.CraftSuccess, new Dictionary<string, int>(), 6)
        ];
        Method("AddAdaptiveHistoryBatch").Invoke(vm, [steps]);
        Assert.Single(vm.History);
        Assert.Equal(2, vm.SuccessCount);
        Assert.Equal(1, vm.NeutralCount);
        Assert.Equal(1, vm.FailCount);
        Assert.Equal(steps[^1].Pool!.Value.ToString(), vm.PoolText);
        Method("AddAdaptiveHistoryBatch").Invoke(vm, [Array.Empty<LaunchService.ForgeStepReport>()]);
        Assert.Single(vm.History);

        for (int i = 1; i < 75; i++) Method("AddAdaptiveHistoryBatch").Invoke(vm, [steps]);
        Assert.Equal(50, vm.History.Count);
        Assert.Equal(150, vm.SuccessCount);
        Assert.Equal(75, vm.NeutralCount);
        Assert.Equal(75, vm.FailCount);
    }
}
