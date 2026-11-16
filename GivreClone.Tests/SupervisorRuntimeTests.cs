using GivreClone.App.Services.Supervisor;
using Xunit;

namespace GivreClone.Tests;

public class SupervisorRuntimeTests
{
    private const string Watcher = """
        function onBotEvent(bot, ev, infos)
          print(bot.account .. ":" .. ev .. ":" .. tostring(infos and (infos.reason or infos.level or infos.script) or "-"))
        end
        function move() delay(20) end
        """;

    private static (SupervisorRuntime Rt, Harness H, List<string> Out) Make(bool multi = true)
    {
        var h = new Harness();
        h.Perm.HasMulti = multi;
        h.Host.Dispose();   // le Harness crée son propre hôte : on n'en veut pas ici
        var rt = new SupervisorRuntime(new SupervisorServices(h.Dir, h.Orders, h.Accounts, h.Proxies, h.Perm, h.Bus));
        var outp = new List<string>();
        rt.HostLog += (n, level, text) => { if (level == "info") lock (outp) outp.Add(text); };
        return (rt, h, outp);
    }

    private static void SpinUntil(Func<bool> cond, int ms = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!cond() && DateTime.UtcNow < until) Thread.Sleep(20);
    }

    [Fact]
    public void Start_is_refused_without_the_multi_permission_and_for_an_empty_script()
    {
        var (rt, _, _) = Make(multi: false);
        Assert.Equal((false, "permission Multi requise"), rt.Start("S", Watcher));
        var (rt2, _, _) = Make();
        Assert.Equal((false, "script vide"), rt2.Start("S", "  "));
        Assert.Empty(rt.RunningNames);
        rt.Dispose(); rt2.Dispose();
    }

    [Fact]
    public void Tick_without_a_running_supervisor_does_nothing()
    {
        var (rt, _, _) = Make();
        Assert.Equal(0, rt.Tick());
        rt.Dispose();
    }

    [Fact]
    public void Existing_bots_are_a_baseline_and_only_later_changes_become_events()
    {
        var (rt, h, o) = Make();
        Assert.True(rt.Start("S", Watcher).Ok);
        Assert.Equal(0, rt.Tick());   // première vue : base, aucun « connected » pour les bots déjà en jeu

        h.Dir.Bots[2] = Sup.Bot("Off", "Zed", 12, "ingame", level: 12);   // Off arrive en jeu
        h.Dir.Bots[0] = Sup.Bot("Farmeur", "Njalla", 10, level: 51);       // Farmeur monte de niveau
        Assert.Equal(2, rt.Tick());
        SpinUntil(() => { lock (o) return o.Count >= 2; });
        lock (o) Assert.Equal(new[] { "Farmeur:levelUp:51", "Off:connected:-" }, o.OrderBy(x => x).ToArray());
        Assert.Equal(0, rt.Tick());   // plus rien à signaler
        rt.Dispose();
    }

    [Fact]
    public void Events_reach_every_running_supervisor()
    {
        var (rt, h, _) = Make();
        var seen = new System.Collections.Concurrent.ConcurrentBag<string>();
        rt.HostLog += (n, level, text) => { if (level == "info") seen.Add(n + "|" + text); };
        Assert.True(rt.Start("A", Watcher).Ok);
        Assert.True(rt.Start("B", Watcher).Ok);
        rt.Tick();
        h.Dir.Bots[1] = Sup.Bot("Banquier", "Sereneth", 11, "dead", script: true);   // son script tourne toujours : seul « death » change
        rt.Tick();
        SpinUntil(() => seen.Count >= 2);
        Assert.Equal(new[] { "A|Banquier:death:-", "B|Banquier:death:-" }, seen.OrderBy(x => x).ToArray());
        rt.Dispose();
    }

    [Fact]
    public void Explicit_events_target_the_bot_by_alias_character_or_pid()
    {
        var (rt, _, o) = Make();
        rt.Start("S", Watcher);
        Assert.True(rt.Raise("banquier", BotEvent.Of("moderator", ("author", "Modo"), ("content", "salut"))));
        Assert.True(rt.Raise("Njalla", BotEvent.Of("crashed", ("reason", "crash"))));
        Assert.False(rt.Raise("inconnu", BotEvent.Of("x")));
        SpinUntil(() => { lock (o) return o.Count >= 2; });
        lock (o) Assert.Equal(new[] { "Banquier:moderator:-", "Farmeur:crashed:crash" }, o.OrderBy(x => x).ToArray());
        rt.Dispose();
    }

    [Fact]
    public void LoginWaiting_can_be_raised_for_a_bot_that_is_not_in_the_directory()
    {
        var (rt, _, o) = Make();
        rt.Start("S", Watcher);
        rt.Raise(new BotSnapshot("Farmeur 4", "Farmeur 4", 0, "socket", BotStatus.Connecting), BotEvent.Of("loginWaiting", ("reason", "browser")));
        SpinUntil(() => { lock (o) return o.Count >= 1; });
        lock (o) Assert.Equal("Farmeur 4:loginWaiting:browser", o.Single());
        rt.Dispose();
    }

    [Fact]
    public void Stop_ends_the_host_reports_the_reason_and_allows_a_restart()
    {
        var (rt, _, _) = Make();
        string? ended = null;
        rt.HostEnded += (n, r) => ended = r;
        rt.Start("S", Watcher);
        Assert.True(rt.IsRunning("S"));
        Assert.Equal((false, "déjà en cours"), rt.Start("S", Watcher));
        rt.Stop("S", "user");
        SpinUntil(() => !rt.IsRunning("S"));
        Assert.False(rt.IsRunning("S"));
        Assert.Equal("user", ended);
        Assert.True(rt.Start("S", Watcher).Ok);   // relance après arrêt
        rt.StopAll("emergency");
        SpinUntil(() => !rt.IsRunning("S"));
        Assert.Equal("emergency", ended);
        rt.Dispose();
    }

    [Fact]
    public void A_restarted_supervisor_starts_from_a_fresh_baseline()
    {
        var (rt, h, o) = Make();
        rt.Start("S", Watcher);
        rt.Tick();
        rt.Stop("S"); SpinUntil(() => !rt.IsRunning("S"));
        h.Dir.Bots[2] = Sup.Bot("Off", "Zed", 12, "ingame");   // changement PENDANT l'arrêt
        rt.Start("S", Watcher);
        Assert.Equal(0, rt.Tick());                              // pas de « connected » rétroactif
        lock (o) Assert.Empty(o);
        rt.Dispose();
    }

    [Fact]
    public void A_supervisor_whose_script_ends_by_itself_is_no_longer_running()
    {
        var (rt, _, _) = Make();
        string? ended = null;
        rt.HostEnded += (n, r) => ended = r;
        rt.Start("Once", "function move() return false end");
        SpinUntil(() => !rt.IsRunning("Once"));
        Assert.False(rt.IsRunning("Once"));
        Assert.Equal("ended", ended);
        Assert.Equal(0, rt.Tick());
        rt.Dispose();
    }

    [Fact]
    public void Doc_example_watcher_reconnects_a_crashed_bot_and_stops_everything_on_a_moderator()
    {
        var (rt, h, _) = Make();
        rt.Start("Veille", """
            function onBotEvent(bot, evenement, infos)
                if evenement == "moderator" then
                    bots:stopAllScripts()
                elseif (evenement == "disconnected" or evenement == "crashed") and infos.reason ~= "stopped" then
                    bots:reconnect(bot.account)
                end
            end
            function move() delay(5000) end
            """);
        rt.Raise("Farmeur", BotEvent.Of("crashed", ("reason", "crash")));
        rt.Raise("Farmeur", BotEvent.Of("disconnected", ("reason", "stopped")));   // arrêt voulu : pas de reconnexion
        rt.Raise("Banquier", BotEvent.Of("moderator", ("author", "Modo")));
        SpinUntil(() => h.Orders.Calls.Count >= 2);
        Assert.Equal(new[] { "reconnect Farmeur", "stopAll" }, h.Orders.Calls);
        rt.Dispose();
    }
}
