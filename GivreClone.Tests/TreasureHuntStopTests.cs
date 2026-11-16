extern alias F06App;

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Engine.Accounts;
using AccountRow = F06App::GivreClone.App.ViewModels.AccountRow;
using BotRow = F06App::GivreClone.App.ViewModels.BotRowViewModel;
using BotScripts = F06App::GivreClone.App.Services.BotScripts;
using BotsVm = F06App::GivreClone.App.ViewModels.BotsViewModel;
using TreasureViewModel = F06App::GivreClone.App.ViewModels.TreasureViewModel;
using Xunit;

namespace GivreClone.Tests;

public sealed class TreasureHuntStopTests
{
    private const int Pid = 910221;

    [Fact]
    public void Stop_hits_only_the_hunt_host_and_never_asks_it_to_give_up()
    {
        BotScripts.Forget(Pid);
        var calls = new List<(int Pid, string Reason)>();
        bool accept = false;
        var vm = Blank();
        try
        {
            var paused = new HashSet<int>();
            var linkLost = new HashSet<int> { Pid };
            var socket = new HashSet<int> { Pid };
            Set(vm, "_planningPaused", paused);
            Set(vm, "_linkLostResume", linkLost);
            Set(vm, "_socketResume", socket);
            var bot = Bot("hunt-acc", Pid);
            var byPid = new System.Collections.Concurrent.ConcurrentDictionary<int, BotRow>();
            byPid[Pid] = bot;
            Set(vm, "_byPid", byPid);
            var reconnect = ArmInPlaceResume(vm, "hunt-acc", "-- chasse en cours");
            SetAuto(vm, "SelectedBot", bot);
            vm.LuaRunning = true;
            Set(vm, "_huntHostStop", new Func<int, string, bool>((pid, reason) =>
            {
                calls.Add((pid, reason));
                return accept;
            }));
            BotScripts.Set(Pid, "mine.lua", true);

            Assert.False(Stop(vm, TreasureViewModel.AutoHuntScriptFile));
            Assert.Empty(calls);
            Assert.Contains(Pid, linkLost);
            Assert.Contains(Pid, socket);
            Assert.DoesNotContain(Pid, paused);
            Assert.Equal("-- chasse en cours", Field(reconnect, "ScriptBody"));
            Assert.True(vm.LuaRunning);
            Assert.True(BotScripts.Get(Pid)!.Value.Running);

            BotScripts.Set(Pid, TreasureViewModel.AutoHuntScriptFile, true);
            Assert.False(Stop(vm, TreasureViewModel.AutoHuntScriptFile));
            Assert.Equal(Pid, Assert.Single(calls).Pid);
            Assert.Equal("user", calls[0].Reason);
            Assert.Contains(Pid, linkLost);
            Assert.Contains(Pid, socket);
            Assert.DoesNotContain(Pid, paused);
            Assert.Equal("-- chasse en cours", Field(reconnect, "ScriptBody"));
            Assert.True(vm.LuaRunning);

            SetAuto(vm, "SelectedBot", Bot("other-acc", 3));
            accept = true;
            Assert.True(Stop(vm, TreasureViewModel.AutoHuntScriptFile));
            Assert.Equal(2, calls.Count);
            Assert.All(calls, call => Assert.Equal("user", call.Reason));
            Assert.DoesNotContain(Pid, linkLost);
            Assert.DoesNotContain(Pid, socket);
            Assert.Contains(Pid, paused);
            Assert.Null(Field(reconnect, "ScriptBody"));
            Assert.Null(Field(reconnect, "ScriptName"));
            Assert.False(BotScripts.Get(Pid)!.Value.Running);
            Assert.Equal(TreasureViewModel.AutoHuntScriptFile, BotScripts.Get(Pid)!.Value.Name);
            Assert.True(vm.LuaRunning);

            linkLost.Add(Pid);
            socket.Add(Pid);
            paused.Remove(Pid);
            ArmAgain(reconnect, "-- fouduglen");
            BotScripts.Set(Pid, TreasureViewModel.FouduglenScriptFile, true);
            Assert.False(Stop(vm, TreasureViewModel.AutoHuntScriptFile));
            Assert.Equal(2, calls.Count);
            Assert.Equal("-- fouduglen", Field(reconnect, "ScriptBody"));
            SetAuto(vm, "SelectedBot", bot);
            Assert.True(Stop(vm, TreasureViewModel.FouduglenScriptFile));
            Assert.Equal((Pid, "user"), calls[^1]);
            Assert.Equal(3, calls.Count);
            Assert.All(calls, call => Assert.Equal("user", call.Reason));
            Assert.False(vm.LuaRunning);
            Assert.Null(Field(reconnect, "ScriptBody"));
            Assert.False(BotScripts.Get(Pid)!.Value.Running);
            Assert.DoesNotContain(Pid, linkLost);
            Assert.DoesNotContain(Pid, socket);
            Assert.Contains(Pid, paused);
        }
        finally { BotScripts.Forget(Pid); }
    }

    private static bool Stop(BotsVm vm, string file)
        => (bool)typeof(BotsVm).GetMethod("StopNamedHuntScript", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, [(uint)Pid, file])!;

    private static BotsVm Blank()
        => (BotsVm)RuntimeHelpers.GetUninitializedObject(typeof(BotsVm));

    private static BotRow Bot(string accountId, int pid)
    {
        var row = (AccountRow)RuntimeHelpers.GetUninitializedObject(typeof(AccountRow));
        SetAuto(row, "Model", new Account { Id = accountId });
        var bot = (BotRow)RuntimeHelpers.GetUninitializedObject(typeof(BotRow));
        SetAuto(bot, "Row", row);
        SetAuto(bot, "Pid", pid);
        return bot;
    }

    private static object ArmInPlaceResume(BotsVm vm, string accountId, string body)
    {
        var reconnectType = typeof(BotsVm).GetNestedType("ScriptReconnect", BindingFlags.NonPublic)!;
        var reconnect = Activator.CreateInstance(reconnectType, nonPublic: true)!;
        ArmAgain(reconnect, body);
        reconnectType.GetField("AccountId")!.SetValue(reconnect, accountId);
        reconnectType.GetField("InPlace")!.SetValue(reconnect, true);
        var list = Activator.CreateInstance(typeof(List<>).MakeGenericType(reconnectType))!;
        ((IList)list).Add(reconnect);
        Set(vm, "_scriptReconnects", list);
        return reconnect;
    }

    private static void ArmAgain(object reconnect, string body)
    {
        var type = reconnect.GetType();
        type.GetField("ScriptBody")!.SetValue(reconnect, body);
        type.GetField("ScriptName")!.SetValue(reconnect, "Chasse au trésor.lua");
        type.GetField("InPlace")!.SetValue(reconnect, true);
    }

    private static object? Field(object target, string name)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target);

    private static void Set(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static void SetAuto(object target, string property, object? value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var field = target.GetType().GetField("<" + property + ">k__BackingField", flags)
            ?? target.GetType().GetField(char.ToLowerInvariant(property[0]) + property[1..], flags)
            ?? target.GetType().GetField("_" + char.ToLowerInvariant(property[0]) + property[1..], flags);
        if (field is null)
            throw new InvalidOperationException(property + " : " + string.Join(", ", target.GetType().GetFields(flags).Select(f => f.Name)));
        field.SetValue(target, value);
    }
}
