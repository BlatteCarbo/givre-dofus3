extern alias F06App;

using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using F06App::GivreClone.App.Services.ForgeAdvice;
using SmithMagicViewModel = F06App::GivreClone.App.ViewModels.SmithMagicViewModel;
using SmithQueueRow = F06App::GivreClone.App.ViewModels.SmithQueueRow;
using Xunit;

namespace GivreClone.Tests;

public sealed class ForgeAdviceTests
{
    private static readonly ForgeAdviceIdentity Identity = new(101, 123456, "session-a", "character-a", "server-a");
    private static readonly ForgeAdviceRune[] Catalog =
    [
        new(1001, 118, "Ba", "Rune Fo"),
        new(1002, 125, "Ba", "Rune Vi"),
        new(1557, 111, "Ba", "Rune Ga Pa")
    ];

    private static ForgeAdviceController Controller(IEnumerable<ForgeAdviceRepair>? repairs = null, int? naturalPa = 0)
    {
        var controller = new ForgeAdviceController();
        controller.Attach(Identity, 42, 900);
        controller.Configure(new(1, repairs ?? [], naturalPa), Catalog);
        return controller;
    }

    private static ForgeAdviceObservation Observation(long revision = 1, IReadOnlyDictionary<int, int>? effects = null,
        IReadOnlyDictionary<long, int>? stock = null, int? pool = null) =>
        new(Identity, 42, 900, revision, effects, stock, pool);

    [Fact]
    public void Exo_repairs_follow_explicit_priority_then_exact_catalog_ga_pa_and_stop_on_success()
    {
        var controller = Controller([new(118, 20, 2, 1001), new(125, 50, 1, 1002)]);
        var stock = new Dictionary<long, int> { [1001] = 5, [1002] = 5, [1557] = 5 };
        Assert.True(controller.Observe(Observation(effects: new Dictionary<int, int>(), stock: stock)));
        Assert.Equal(1002, controller.Current.RuneGid);
        controller.Observe(Observation(2, new Dictionary<int, int> { [125] = 50 }, stock));
        Assert.Equal(1001, controller.Current.RuneGid);
        controller.Observe(Observation(3, new Dictionary<int, int> { [125] = 50, [118] = 20 }, stock));
        var ready = controller.Current;
        Assert.Equal(ForgeAdviceStatus.Ready, ready.Status);
        Assert.Equal(1557, ready.RuneGid);
        Assert.Equal("Rune Ga Pa", ready.RuneName);
        Assert.Null(ready.Pool);
        Assert.Equal(Identity, ready.Identity);
        Assert.Equal(42, ready.ItemUid);
        Assert.Equal(900, ready.ItemGid);
        Assert.Equal(3, ready.ItemRevision);
        Assert.Equal(1, ready.ObjectiveRevision);
        controller.Observe(Observation(4, new Dictionary<int, int> { [111] = 1 }, null));
        Assert.Equal(ForgeAdviceStatus.Done, controller.Current.Status);
        Assert.Null(controller.Current.RuneGid);
    }

    [Fact]
    public void Exo_target_is_relative_to_explicit_natural_pa_not_a_fixed_one()
    {
        var controller = Controller(naturalPa: 1);
        var stock = new Dictionary<long, int> { [1557] = 1 };
        controller.Observe(Observation(effects: new Dictionary<int, int> { [111] = 1 }, stock: stock, pool: 0));
        Assert.Equal(ForgeAdviceStatus.Ready, controller.Current.Status);
        Assert.Equal(0, controller.Current.Pool);
        controller.Observe(Observation(2, new Dictionary<int, int> { [111] = 2 }, stock));
        Assert.Equal(ForgeAdviceStatus.Done, controller.Current.Status);
    }

    [Fact]
    public void Unknown_effects_and_stock_are_not_received_empty_and_unknown_pool_is_not_zero()
    {
        var controller = Controller();
        controller.Observe(Observation());
        Assert.Equal(ForgeAdviceStatus.UnknownState, controller.Current.Status);
        controller.Observe(Observation(2, new Dictionary<int, int>(), null));
        Assert.Equal(ForgeAdviceStatus.UnknownState, controller.Current.Status);
        controller.Observe(Observation(3, new Dictionary<int, int>(), new Dictionary<long, int>()));
        Assert.Equal(ForgeAdviceStatus.MissingRune, controller.Current.Status);
        Assert.Null(controller.Current.RuneGid);
        Assert.Null(controller.Current.Pool);
        controller.Observe(Observation(4, new Dictionary<int, int>(), new Dictionary<long, int> { [1557] = 1 }));
        Assert.Equal(ForgeAdviceStatus.Ready, controller.Current.Status);
        controller.Observe(Observation(5));
        Assert.Equal(ForgeAdviceStatus.UnknownState, controller.Current.Status);
        Assert.Null(controller.Current.RuneGid);
    }

    [Theory]
    [InlineData("Pa", "Rune Pa Ga Pa")]
    [InlineData("Ra", "Rune Ra Ga Pa")]
    [InlineData("Ba", "Wrong name")]
    public void Exo_never_fabricates_a_tier_or_accepts_a_non_exact_catalog_name(string tier, string name)
    {
        var controller = Controller();
        controller.Configure(new(2, [], 0), [new(9999, 111, tier, name)]);
        controller.Observe(Observation(effects: new Dictionary<int, int>(), stock: new Dictionary<long, int> { [9999] = 1 }));
        Assert.Equal(ForgeAdviceStatus.UnknownState, controller.Current.Status);
        Assert.Null(controller.Current.RuneGid);
    }

    [Fact]
    public void Item_identity_disconnect_and_objective_revisions_clear_advice_and_reject_stale_events()
    {
        var controller = Controller();
        var effects = new Dictionary<int, int>();
        var stock = new Dictionary<long, int> { [1557] = 2 };
        controller.Observe(Observation(5, effects, stock));
        Assert.False(controller.Observe(Observation(4, new Dictionary<int, int> { [111] = 1 }, stock)));
        Assert.Equal(ForgeAdviceStatus.Ready, controller.Current.Status);
        controller.Configure(new(2, [], 0), Catalog);
        Assert.Equal(ForgeAdviceStatus.UnknownState, controller.Current.Status);
        Assert.Null(controller.Current.RuneGid);
        Assert.False(controller.Observe(Observation(5, effects, stock)));
        Assert.True(controller.Observe(Observation(6, effects, stock)));
        Assert.Equal(2, controller.Current.ObjectiveRevision);
        controller.Attach(Identity, 43, 900);
        Assert.Null(controller.Current.RuneGid);
        Assert.False(controller.Observe(Observation(7, effects, stock)));
        controller.Attach(Identity with { }, 42, 900);
        Assert.True(controller.Observe(Observation(8, effects, stock)));
        Assert.Equal(ForgeAdviceStatus.UnknownState, controller.Current.Status); // item changes require objective configuration
        var replacement = new ForgeAdviceIdentity(101, 123457, "session-b", "character-b", "server-a");
        controller.Attach(replacement, 42, 900);
        controller.Configure(new(3, [], 0), Catalog);
        Assert.False(controller.Observe(Observation(9, effects, stock)));
        controller.Attach(null);
        Assert.Null(controller.Current.Identity);
        Assert.Null(controller.Current.RuneGid);
    }

    [Fact]
    public void Observation_and_objective_copy_mutable_input_and_published_record_stays_immutable()
    {
        var repairs = new List<ForgeAdviceRepair> { new(118, 10, 1, 1001) };
        var objective = new ForgeAdviceObjective(1, repairs);
        repairs.Clear();
        var controller = new ForgeAdviceController();
        controller.Attach(Identity, 42, 900);
        controller.Configure(objective, Catalog);
        var effects = new Dictionary<int, int>();
        var stock = new Dictionary<long, int> { [1001] = 1 };
        var observed = Observation(effects: effects, stock: stock);
        effects[118] = 10;
        stock.Clear();
        controller.Observe(observed);
        var published = controller.Current;
        Assert.Equal(1001, published.RuneGid);
        controller.Invalidate();
        Assert.Equal(ForgeAdviceStatus.Ready, published.Status);
        Assert.Null(controller.Current.RuneGid);
        Assert.True(controller.Current.PublicationRevision > published.PublicationRevision);
        Assert.Throws<ArgumentException>(() => new ForgeAdviceObjective(1,
            Enumerable.Range(1, 65).Select(i => new ForgeAdviceRepair(118, i, i, i))));
        Assert.Throws<ArgumentException>(() => new ForgeAdviceIdentity(1, 1, new string('x', 129), "c", "s"));
    }

    private static SmithMagicViewModel CommandOnlyViewModel(ForgeMode mode)
    {
        // No constructor: the normal constructor installs a global history timer. Commands must not need it in Advice.
        var vm = (SmithMagicViewModel)RuntimeHelpers.GetUninitializedObject(typeof(SmithMagicViewModel));
        Set(vm, "_advice", Controller());
        Set(vm, "_adviceConfigured", true);
        Set(vm, "_forgeMode", mode);
        Set(vm, "_pid", 101);
        Set(vm, "_loadedUid", 42L);
        Set(vm, "<Queue>k__BackingField", new ObservableCollection<SmithQueueRow> { new(42, "item", "", 900) });
        return vm;
    }

    private static void Set(SmithMagicViewModel vm, string name, object value) =>
        typeof(SmithMagicViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);

    [Fact]
    public async Task Advice_start_and_apply_have_no_history_workshop_or_launch_dependency_and_stop_removes_advice()
    {
        var vm = CommandOnlyViewModel(ForgeMode.Advice);
        vm.ForgeHistoryReady = () => throw new InvalidOperationException("No automatic admission in advice.");
        await vm.StartCommand.ExecuteAsync(null);
        await vm.ApplyOneCommand.ExecuteAsync(null);
        Assert.True(vm.AdviceActive);
        Assert.False(vm.Busy);
        Assert.False(vm.CanApply);
        Assert.True(vm.ObserveAdvice(Observation(effects: new Dictionary<int, int>(), stock: new Dictionary<long, int> { [1557] = 1 })));
        Assert.Equal(1557, vm.Advice.RuneGid);
        vm.StopCommand.Execute(null);
        Assert.False(vm.AdviceActive);
        Assert.Null(vm.Advice.RuneGid);
        Assert.False(vm.ObserveAdvice(Observation(2, new Dictionary<int, int>(), new Dictionary<long, int> { [1557] = 1 })));
    }

    [Fact]
    public async Task Automatic_start_uses_existing_starting_busy_and_history_cancellation_gate()
    {
        var vm = CommandOnlyViewModel(ForgeMode.Automatic);
        var history = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = false;
        vm.ForgeHistoryReady = () => { entered = true; return history.Task; };
        var start = vm.StartCommand.ExecuteAsync(null);
        Assert.True(entered);
        Assert.True(vm.Starting);
        Assert.True(vm.Busy);
        Assert.False(vm.SetForgeMode(ForgeMode.Advice));
        vm.StopCommand.Execute(null);
        await start.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.Busy);
        Assert.Equal(ForgeMode.Automatic, vm.Mode);
    }
}
