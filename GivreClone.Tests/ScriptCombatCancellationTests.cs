extern alias F06App;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using Xunit;

namespace GivreClone.Tests;

public sealed class ScriptCombatCancellationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Fact]
    public async Task Stopped_script_cannot_delegate_finish_or_ready_a_turn()
    {
        // An uninitialized service fails on any state/transport access: cancellation must win before that.
        var service = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        var ct = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FinishScriptTurnAsync(1, 1, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PlayTurnForScriptAsync(1, 1, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.EngineTakeoverAfterHookErrorAsync(1, 1, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ScriptFinishPlacementAsync(1, ct));
    }

    [Fact]
    public async Task Stop_during_end_turn_pacing_does_not_reach_transport_or_mark_the_turn_ended()
    {
        var service = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        foreach (string name in new[] { "_combat", "_scriptTurn", "_turnEpoch", "_myTurnOpen" })
        {
            var field = typeof(LaunchService).GetField(name, Private)!;
            field.SetValue(service, Activator.CreateInstance(field.FieldType));
        }
        IDictionary Field(string name) => (IDictionary)typeof(LaunchService).GetField(name, Private)!.GetValue(service)!;
        var type = typeof(LaunchService).GetNestedType("ScriptTurnState", BindingFlags.NonPublic)!;
        var turn = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetField("Epoch")!.SetValue(turn, 7);
        Field("_scriptTurn")[(uint)1] = turn;
        Field("_turnEpoch")[(uint)1] = 7;
        Field("_myTurnOpen")[(uint)1] = true;
        using var stop = new CancellationTokenSource();
        Task finish = service.FinishScriptTurnAsync(1, 7, stop.Token);
        Assert.False(finish.IsCompleted); // currently waiting in the real profile pacing delay
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => finish);
        Assert.Equal(0, type.GetField("Ended")!.GetValue(turn));
    }
}
