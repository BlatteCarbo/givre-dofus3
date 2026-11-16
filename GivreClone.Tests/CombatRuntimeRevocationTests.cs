extern alias F06App;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using CombatVisualOptions = F06App::GivreClone.App.Services.CombatVisualOptions;
using Xunit;

namespace GivreClone.Tests;

public class CombatRuntimeRevocationTests
{
    [Fact]
    public void Late_native_teardown_cannot_remove_a_newer_profile_generation()
    {
        var runtime = typeof(LaunchService).Assembly.GetType("GivreClone.App.Services.NativeTurboRuntime")!;
        var state = runtime.GetNestedType("DesiredState", BindingFlags.NonPublic)!;
        object Desired(long generation) => Activator.CreateInstance(state,
            new object[] { false, false, false, false, 0, 0, 0, "", "", generation, 123L, true })!;
        var map = (IDictionary)runtime.GetField("Desired", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var remove = runtime.GetMethod("RemoveDesired", BindingFlags.NonPublic | BindingFlags.Static)!;
        // Negative test PID can never be accepted by RequestState/Forget and causes no native/file action.
        const int pid = -1700624;
        var old = Desired(1);
        var next = Desired(2);
        try
        {
            map[pid] = next;
            Assert.Equal(false, remove.Invoke(null, new[] { (object)pid, old }));
            Assert.Equal(next, map[pid]);
            Assert.Equal(true, remove.Invoke(null, new[] { (object)pid, next }));
            Assert.False(map.Contains(pid));
        }
        finally { map.Remove(pid); }
    }

    [Fact]
    public void Tactical_requires_every_gate_for_both_native_providers()
    {
        for (int bits = 0; bits < 16; bits++)
            Assert.Equal(bits == 7, CombatVisualOptions.TacticalRequested(
                (bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Releasing_control_cancels_companion_wait_without_erasing_cooldowns(bool freeControl)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(LaunchService);
        var service = (LaunchService)RuntimeHelpers.GetUninitializedObject(type);
        FieldInfo Field(string name) => type.GetField(name, flags)!;
        IDictionary Map(string name) => (IDictionary)Field(name).GetValue(service)!;
        foreach (string name in new[] { "_controlledCombat", "_controlledHistory", "_engineControlled",
            "_serverTurnActor", "_controlledTurnStarted", "_automaticTurnStop", "_myTurnOpen", "_turnEpoch",
            "_charId", "_inFight", "_allies" })
            Field(name).SetValue(service, Activator.CreateInstance(Field(name).FieldType));
        Map("_charId")[1u] = 999L;
        Map("_inFight")[1u] = true;
        var observe = type.GetMethod("ObserveControlledFighter", flags)!;
        observe.Invoke(service, new object[] { 1u, new ControlledFighter(999, -20000, new Dictionary<int, int> { [222] = 3 }) });
        var context = Map("_controlledCombat")[1u]!;
        var ledger = (F06App::GivreClone.App.Services.Combat.CastLedger)context.GetType().GetField("Ledger")!.GetValue(context)!;
        ledger.RecordCast(222, 100, 2);
        Map("_serverTurnActor")[1u] = -20000L;
        Map("_myTurnOpen")[1u] = true;
        Map("_turnEpoch")[1u] = 4;
        using var stop = new CancellationTokenSource();
        Map("_automaticTurnStop")[1u] = stop;

        if (freeControl)
            type.GetMethod("ReleaseControlledFighter", flags)!.Invoke(service, new object[] { 1u });
        else
            observe.Invoke(service, new object[] { 1u, new ControlledFighter(999, 999, new Dictionary<int, int>()) });

        Assert.True(stop.IsCancellationRequested);
        Assert.Equal(false, Map("_myTurnOpen")[1u]);
        Assert.Equal(5, Map("_turnEpoch")[1u]);
        Assert.Empty(Map("_controlledCombat"));
        Assert.Single(Map("_controlledHistory"));
        Assert.True(ledger.IsRechargeActive(222, 3, 2, 0, 0));
    }
}
