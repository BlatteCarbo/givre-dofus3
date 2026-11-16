extern alias F06App;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using HdvVm = F06App::GivreClone.App.ViewModels.HdvViewModel;

namespace GivreClone.Tests;

public sealed class HdvMutationScopeTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Set(HdvVm vm, string field, object value)
        => typeof(HdvVm).GetField(field, Hidden)!.SetValue(vm, value);

    private static HdvVm Model(SemaphoreSlim gate)
    {
        var vm = (HdvVm)RuntimeHelpers.GetUninitializedObject(typeof(HdvVm));
        Set(vm, "_pid", 9u);
        Set(vm, "_hdvProfileKey", "alpha-server");
        Set(vm, "_engineGate", gate);
        return vm;
    }

    private static Task<bool> Mutate(HdvVm vm, Func<Task<bool>> operation, CancellationToken cancellation = default)
        => (Task<bool>)typeof(HdvVm).GetMethod("Gated", Hidden)!.Invoke(vm, [operation, cancellation])!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queued_mutation_is_rejected_when_its_account_or_profile_changes(bool profileOnly)
    {
        using var gate = new SemaphoreSlim(0, 1);
        var vm = Model(gate);
        int dispatches = 0;
        var queued = Mutate(vm, () => { dispatches++; return Task.FromResult(true); });
        Assert.False(queued.IsCompleted);

        if (profileOnly) Set(vm, "_hdvProfileKey", "beta-server");
        else Set(vm, "_pid", 10u);
        gate.Release();
        bool result = await queued.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result);
        Assert.Equal(0, dispatches);
        Assert.True(await Mutate(vm, () => Task.FromResult(true)).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Returning_to_the_same_selection_does_not_revive_an_old_mutation()
    {
        using var gate = new SemaphoreSlim(0, 1);
        var vm = Model(gate);
        bool dispatched = false;
        var queued = Mutate(vm, () => { dispatched = true; return Task.FromResult(true); });

        // Two selection changes have returned to the original PID and profile.
        Set(vm, "_mutationEpoch", 2);
        gate.Release();
        bool result = await queued.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result);
        Assert.False(dispatched);
        Assert.True(await Mutate(vm, () => Task.FromResult(true)).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Disconnected_session_does_not_dispatch_a_mutation()
    {
        using var gate = new SemaphoreSlim(1, 1);
        var vm = Model(gate);
        Set(vm, "_pid", 0u);
        bool dispatched = false;

        bool result = await Mutate(vm, () => { dispatched = true; return Task.FromResult(true); });

        Assert.False(result);
        Assert.False(dispatched);
    }

    [Fact]
    public async Task Unchanged_session_preserves_serial_execution()
    {
        using var gate = new SemaphoreSlim(1, 1);
        var vm = Model(gate);
        var firstDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool secondStarted = false;
        var first = Mutate(vm, () => firstDone.Task);
        var second = Mutate(vm, () => { secondStarted = true; return Task.FromResult(true); });
        Assert.False(secondStarted);

        firstDone.SetResult(true);
        bool[] results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(results, result => Assert.True(result));
        Assert.True(secondStarted);
    }

    [Fact]
    public async Task Cancelled_waiter_does_not_dispatch_or_consume_the_gate()
    {
        using var gate = new SemaphoreSlim(0, 1);
        using var stop = new CancellationTokenSource();
        var vm = Model(gate);
        bool dispatched = false;
        var queued = Mutate(vm, () => { dispatched = true; return Task.FromResult(true); }, stop.Token);

        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => queued.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(dispatched);
        gate.Release();
        Assert.True(await Mutate(vm, () => Task.FromResult(true)).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Failed_operation_releases_the_gate_for_the_next_mutation()
    {
        using var gate = new SemaphoreSlim(1, 1);
        var vm = Model(gate);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Mutate(vm, () => throw new InvalidOperationException()).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.True(await Mutate(vm, () => Task.FromResult(true)).WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
