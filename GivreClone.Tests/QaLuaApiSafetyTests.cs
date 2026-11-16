extern alias F06App;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using MoonSharp.Interpreter;
using GivreClone.Protocol;
using Xunit;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Inventory = F06App::GivreClone.App.Services.InventoryStore;

namespace GivreClone.Tests;

public sealed class QaLuaApiSafetyTests
{
    private const uint Pid = 717;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Set(object target, string name, object value)
        => target.GetType().GetField(name, Private)!.SetValue(target, value);

    private static void Bind(Host host, string name, Script script, CancellationToken ct = default)
    {
        var args = name == "BindFinalLuaContract" ? new object[] { script } : new object[] { script, ct };
        typeof(Host).GetMethod(name, Private)!.Invoke(host, args);
    }

    private static Host HostWithItem(int effect, bool mixed = false)
    {
        var launch = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        var inventory = new Inventory();
        inventory.Seed(Pid, mixed
            ? [new(1, 42, 1), new(2, 42, 10)]
            : [new(1, 42, 1)]);
        Set(launch, "<Inventory>k__BackingField", inventory);
        var effects = new ConcurrentDictionary<(uint Pid, int Uid), IReadOnlyList<InventoryReader.ItemEffect>>();
        effects[(Pid, 1)] = [new(effect, 0, 0, 0, 0, null, 0)];
        Set(launch, "_itemEffects", effects);
        var host = (Host)RuntimeHelpers.GetUninitializedObject(typeof(Host));
        Set(host, "_launch", launch);
        Set(host, "_pid", Pid);
        return host;
    }

    [Theory]
    [InlineData("sale.GetLots({}, 100)", 0)]
    [InlineData("sale.GetLots({})", 0)]
    [InlineData("sale.GetLots()", 0)]
    [InlineData("sale.GetLots(false)", 0)]
    [InlineData("sale.GetLots(-1)", 0)]
    [InlineData("sale.GetLots(100)", 3)]
    [InlineData("sale:GetLots(100)", 3)]
    [InlineData("sale.GetLots('100')", 3)]
    public void Sale_only_skips_its_own_self_table(string expression, int expected)
    {
        var host = (Host)RuntimeHelpers.GetUninitializedObject(typeof(Host));
        var script = new Script(CoreModules.Preset_SoftSandbox);
        Bind(host, "BindSale", script);
        Assert.Equal(expected, script.DoString("return " + expression).Number);
    }

    [Theory]
    [InlineData(981, false)]
    [InlineData(982, false)]
    [InlineData(983, false)]
    [InlineData(981, true)]
    [InlineData(982, true)]
    public void Npc_sell_refuses_bound_gids_before_dispatch(int effect, bool mixed)
    {
        var host = HostWithItem(effect, mixed);
        var script = new Script(CoreModules.Preset_SoftSandbox);
        var npc = new Table(script);
        bool dispatched = false;
        npc["sell"] = DynValue.NewCallback((_, _) => { dispatched = true; return DynValue.True; });
        script.Globals["npc"] = npc;
        Bind(host, "BindFinalLuaContract", script);
        var result = script.DoString("return npc:sell(42, 1)");
        Assert.False(dispatched);
        Assert.False(result.Tuple[0].Boolean);
        Assert.Equal(0, result.Tuple[1].Number);
        Assert.Equal("bound", result.Tuple[2].String);
    }

    [Theory]
    [InlineData(981)]
    [InlineData(982)]
    [InlineData(983)]
    public void Legacy_npc_sale_refuses_bound_items_without_a_transport(int effect)
    {
        var host = HostWithItem(effect);
        var script = new Script(CoreModules.Preset_SoftSandbox);
        Bind(host, "BindSnowSale", script);
        Assert.False(script.DoString("return sale:sellToPnj(42, 1)").Boolean);
    }

    [Fact]
    public void Npc_sell_preserves_unbound_dispatch_and_return_values()
    {
        var host = HostWithItem(111);
        var script = new Script(CoreModules.Preset_SoftSandbox);
        var npc = new Table(script);
        npc["sell"] = DynValue.NewCallback((_, _) =>
            DynValue.NewTuple(DynValue.True, DynValue.NewNumber(1)));
        script.Globals["npc"] = npc;
        Bind(host, "BindFinalLuaContract", script);
        var result = script.DoString("return npc.sell(42, 1)");
        Assert.True(result.Tuple[0].Boolean);
        Assert.Equal(1, result.Tuple[1].Number);
    }

    [Theory]
    [InlineData("putAllItems")]
    [InlineData("getAllItems")]
    public void Cancelled_bulk_exchange_checks_stop_before_reading_session_state(string method)
    {
        var host = (Host)RuntimeHelpers.GetUninitializedObject(typeof(Host));
        var script = new Script(CoreModules.Preset_SoftSandbox);
        using var cts = new CancellationTokenSource();
        Bind(host, "BindSnowExchange", script, cts.Token);
        cts.Cancel();
        var error = Record.Exception(() => script.DoString($"exchange:{method}()"));
        Assert.NotNull(error);
        while (error.InnerException is { } inner) error = inner;
        Assert.Equal("ScriptStopException", error.GetType().Name);
    }
}
