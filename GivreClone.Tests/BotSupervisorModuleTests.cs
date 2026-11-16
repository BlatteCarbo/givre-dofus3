using GivreClone.App.Services.Supervisor;
using MoonSharp.Interpreter;
using Xunit;

namespace GivreClone.Tests;

public class BotSupervisorModuleTests
{
    private sealed class StopEx : Exception { }

    private sealed class Bot : IDisposable
    {
        public readonly SupervisorBus Bus = new();
        public readonly Script S = new(CoreModules.Preset_SoftSandbox);
        public readonly List<string> Out = new(), Warn = new();
        public readonly BotSupervisorModule Mod;
        public readonly CancellationTokenSource Cts = new();
        public const int Pid = 11;

        public Bot()
        {
            S.Globals["print"] = DynValue.NewCallback((_, a) =>
            {
                var parts = new List<string>();
                for (int i = 0; i < a.Count; i++) parts.Add(a[i].ToPrintString());
                Out.Add(string.Join("\t", parts));
                return DynValue.Nil;
            });
            Mod = new BotSupervisorModule(Pid, S, Cts.Token, Bus, pid => Sup.Bot("Banquier", "Sereneth", pid),
                e => { for (var x = e; x is not null; x = x.InnerException) if (x is StopEx) return true; return false; },
                () => new StopEx(), Warn.Add);
            Mod.Bind();
        }

        public void Run(string code) => S.DoString(code);
        public void Dispose() => Cts.Dispose();
    }

    [Fact]
    public void IsRunning_follows_the_registered_supervisors()
    {
        using var b = new Bot();
        b.Run("print(tostring(supervisor:isRunning()))");
        b.Bus.RegisterSupervisor("S");
        b.Run("print(tostring(supervisor:isRunning()))");
        Assert.Equal(new[] { "false", "true" }, b.Out);
    }

    [Fact]
    public void Send_reaches_running_supervisors_with_the_bot_as_sender_and_returns_the_count()
    {
        using var b = new Bot();
        b.Run("print(supervisor:send('kamas', { besoin = 200000 }))");   // aucun superviseur : 0
        var box = b.Bus.RegisterSupervisor("S");
        b.Bus.RegisterSupervisor("T");
        b.Run("print(supervisor:send('kamas', { besoin = 200000, tags = { 'a', 'b' } }))");
        Assert.Equal(new[] { "0", "2" }, b.Out);
        var m = box.TryTake("kamas")!;
        Assert.Equal("Sereneth", m.SenderBot!.Name);
        var d = (Dictionary<string, object?>)m.Data!;
        Assert.Equal(200000.0, d["besoin"]);
        Assert.Equal(new object?[] { "a", "b" }, ((List<object?>)d["tags"]!).ToArray());
    }

    [Fact]
    public void Send_without_data_or_with_a_function_behaves()
    {
        using var b = new Bot();
        var box = b.Bus.RegisterSupervisor("S");
        b.Run("print(supervisor:send('ping'))");
        Assert.Null(box.TryTake("ping")!.Data);
        b.Run("print(tostring(pcall(function() supervisor:send('x', { f = print }) end)))");
        Assert.Equal("false", b.Out[^1]);
        b.Run("print(supervisor:send(42))");   // sujet non texte : rien envoyé
        Assert.Equal("0", b.Out[^1]);
    }

    [Fact]
    public void Wait_returns_data_and_supervisor_name_or_nil_on_timeout()
    {
        using var b = new Bot();
        var t = Bg.Run(() => { Thread.Sleep(150); b.Bus.SendToBot(Bot.Pid, "Veille", "go", new Dictionary<string, object?> { ["carte"] = 5.0 }); });
        b.Run("local d, who = supervisor:wait('go', 5000) print(d.carte, who)  print(tostring(supervisor:wait('rien', 100)))");
        t.Wait();
        Assert.Equal(new[] { "5\tVeille", "nil" }, b.Out);
    }

    [Fact]
    public void Pump_serves_messages_to_onSupervisorMessage_in_order()
    {
        using var b = new Bot();
        b.Run("function onSupervisorMessage(sujet, d, sup) print(sup, sujet, d and d.n or '-') end");
        b.Bus.SendToBot(Bot.Pid, "S", "a", new Dictionary<string, object?> { ["n"] = 1.0 });
        b.Bus.SendToBot(Bot.Pid, "S", "b", null);
        b.Mod.Pump();
        Assert.Equal(new[] { "S\ta\t1", "S\tb\t-" }, b.Out);
        Assert.Equal(0, b.Bus.BotBox(Bot.Pid).PendingCount);
    }

    [Fact]
    public void Without_a_callback_messages_are_kept_for_a_later_wait_not_lost()
    {
        using var b = new Bot();
        b.Bus.SendToBot(Bot.Pid, "S", "plus-tard", 9.0);
        b.Mod.Pump();   // pas de onSupervisorMessage : gardé
        b.Run("local d = supervisor:wait('plus-tard', 500) print(d)");
        Assert.Equal(new[] { "9" }, b.Out);
    }

    [Fact]
    public void Wait_serves_other_subjects_to_the_callback_meanwhile()
    {
        using var b = new Bot();
        b.Run("function onSupervisorMessage(sujet) print('cb:' .. sujet) end");
        b.Bus.SendToBot(Bot.Pid, "S", "autre", null);
        var t = Bg.Run(() => { Thread.Sleep(100); b.Bus.SendToBot(Bot.Pid, "S", "cible", 1.0); });
        b.Run("print(supervisor:wait('cible', 3000))");
        t.Wait();
        Assert.Equal(new[] { "cb:autre", "1	S" }, b.Out);
    }

    [Fact]
    public void An_error_in_the_callback_is_reported_and_the_next_message_is_still_served()
    {
        using var b = new Bot();
        b.Run("function onSupervisorMessage(sujet) if sujet == 'boom' then error('oups') end print(sujet) end");
        b.Bus.SendToBot(Bot.Pid, "S", "boom", null);
        b.Bus.SendToBot(Bot.Pid, "S", "ok", null);
        b.Mod.Pump();
        Assert.Contains(b.Warn, w => w.Contains("onSupervisorMessage") && w.Contains("oups"));
        Assert.Equal(new[] { "ok" }, b.Out);
    }

    [Fact]
    public void A_stop_requested_during_a_wait_unwinds_the_script()
    {
        using var b = new Bot();
        var t = Bg.Run(() => { Thread.Sleep(100); b.Cts.Cancel(); });
        var ex = Assert.ThrowsAny<Exception>(() => b.Run("supervisor:wait('jamais', 60000)"));
        t.Wait();
        Assert.True(ex is StopEx || ex.InnerException is StopEx);
    }

    [Fact]
    public void Names_are_tolerant_and_dot_calls_work()
    {
        using var b = new Bot();
        b.Bus.RegisterSupervisor("S");
        b.Run("print(supervisor.is_running(), tostring(supervisor:IsRunning()), supervisor.send('x'))");
        Assert.Equal("true\ttrue\t1", b.Out.Single());
    }
}
