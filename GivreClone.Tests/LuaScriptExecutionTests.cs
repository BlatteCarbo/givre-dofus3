using System;
using System.Threading;
using System.Threading.Tasks;
using MoonSharp.Interpreter;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class LuaScriptExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacement_waits_for_previous_cleanup_and_honors_its_own_cancellation(bool cancelPending)
    {
        var previous = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pending = new CancellationTokenSource();
        bool started = false;
        var next = LuaSnowCompatRuntime.RunAfterAsync(previous.Task, pending.Token, () => started = true);
        Assert.False(started);
        Assert.False(next.IsCompleted);
        if (cancelPending) pending.Cancel();
        previous.SetResult(); // fin de l'ancien run ET de son callback stopped
        await next.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(!cancelPending, started);
    }

    [Fact]
    public async Task Restart_is_possible_after_a_failed_run()
    {
        bool started = false;
        await LuaSnowCompatRuntime.RunAfterAsync(Task.FromException(new InvalidOperationException("old run")),
            CancellationToken.None, () => started = true);
        Assert.True(started);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(2147483648d)]
    public void Invalid_delays_raise_a_clear_lua_error(double ms)
        => Assert.Throws<ScriptRuntimeException>(() => LuaSnowCompatRuntime.DelayMilliseconds(DynValue.NewNumber(ms)));

    [Fact]
    public void Delay_bounds_are_representable_without_overflow()
    {
        Assert.Equal(0, LuaSnowCompatRuntime.DelayMilliseconds(DynValue.NewNumber(0)));
        Assert.Equal(int.MaxValue, LuaSnowCompatRuntime.DelayMilliseconds(DynValue.NewNumber(int.MaxValue)));
        Assert.Throws<ScriptRuntimeException>(() => LuaSnowCompatRuntime.DelayMilliseconds(DynValue.Nil));
    }

    [Theory]
    [InlineData("mount:toggleRiding")]
    [InlineData("map:checkMonsterAmountConstraints")]
    [InlineData("developer:unCancelMessageToClient")]
    public void Unavailable_api_raises_a_named_lua_error_and_never_reports_success(string api)
    {
        var s = new Script(CoreModules.Preset_SoftSandbox);
        s.Globals["unavailable"] = LuaSnowCompatRuntime.Unsupported(api);
        var result = s.DoString("return pcall(unavailable)");
        Assert.False(result.Tuple[0].Boolean);
        Assert.Contains(api, result.Tuple[1].String);
        Assert.Contains("Aucune action effectuée", result.Tuple[1].String);
        Assert.Throws<ScriptRuntimeException>(() => s.DoString("unavailable()"));
    }

    [Theory]
    [InlineData("while true do end")]
    [InlineData("while true do pcall(function() while true do end end) end")]
    [InlineData("coroutine.resume(coroutine.create(function() while true do end end))")]
    [InlineData("coroutine.wrap(function() while true do end end)()")]
    public void Pure_lua_loop_is_interrupted_at_an_instruction_checkpoint(string code)
    {
        var s = new Script(CoreModules.Preset_SoftSandbox);
        using var cts = new CancellationTokenSource();
        int checkpoints = 0;
        void Check() { if (++checkpoints == 3) cts.Cancel(); }
        LuaSnowCompatRuntime.ConfigureCoroutines(s, cts.Token, Check);
        Assert.Throws<OperationCanceledException>(() => LuaSnowCompatRuntime.Call(s, s.LoadString(code), cts.Token,
            Check));
        Assert.Equal(3, checkpoints);
    }

    [Fact]
    public void Cancellation_prevents_even_a_short_action_from_starting()
    {
        var s = new Script();
        bool acted = false;
        s.Globals["action"] = DynValue.NewCallback((_, _) => { acted = true; return DynValue.True; });
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => LuaSnowCompatRuntime.Call(s, s.LoadString("action()"), cts.Token));
        Assert.False(acted);
    }

    [Fact]
    public void Nested_hook_and_module_calls_preserve_return_values_and_resume_the_parent()
    {
        var s = new Script(CoreModules.Preset_SoftSandbox);
        s.Globals["hook"] = DynValue.NewCallback((_, _) => LuaSnowCompatRuntime.Call(s,
            s.LoadString("local n=0; for i=1,10000 do n=n+1 end; return n, 'module'"), CancellationToken.None));
        var result = LuaSnowCompatRuntime.Call(s, s.LoadString("local n, name=hook(); return n+1, name"), CancellationToken.None);
        Assert.Equal(10001, result.Tuple[0].Number);
        Assert.Equal("module", result.Tuple[1].String);
    }

    [Fact]
    public void Native_placement_hooks_are_callable_through_the_same_entry_point()
    {
        var s = new Script(CoreModules.Preset_SoftSandbox);
        var hook = DynValue.NewCallback((_, _) => DynValue.NewNumber(7));
        Assert.Equal(7, LuaSnowCompatRuntime.Call(s, hook, CancellationToken.None).Number);
    }

    [Fact]
    public void A_failed_run_does_not_poison_a_new_execution()
    {
        var s = new Script(CoreModules.Preset_SoftSandbox);
        Assert.Throws<ScriptRuntimeException>(() => LuaSnowCompatRuntime.Call(s, s.LoadString("error('failure')"), CancellationToken.None));
        Assert.Equal(42, LuaSnowCompatRuntime.Call(s, s.LoadString("return 42"), CancellationToken.None).Number);
    }

    [Theory]
    [InlineData("local c=coroutine.create(function(x) local y=coroutine.yield(x+1, 'yielded'); return y+2 end); local ok,n,t=coroutine.resume(c, 3); assert(ok and n==4 and t=='yielded'); local ok2,r=coroutine.resume(c, 8); assert(ok2 and r==10); return coroutine.status(c)")]
    [InlineData("local f=coroutine.wrap(function(x) local y=coroutine.yield(x+1); return y+2 end); assert(f(3)==4); assert(f(8)==10); return 'dead'")]
    public void User_coroutines_keep_their_explicit_yields_and_arguments(string code)
    {
        var s = new Script(CoreModules.Preset_SoftSandbox);
        LuaSnowCompatRuntime.ConfigureCoroutines(s, CancellationToken.None);
        Assert.Equal("dead", LuaSnowCompatRuntime.Call(s, s.LoadString(code), CancellationToken.None).String);
    }
}
