using GivreClone.App.Services;
using GivreClone.App.Services.Supervisor;
using MoonSharp.Interpreter;
using Xunit;

namespace GivreClone.Tests;

public class BotStatusMapTests
{
    [Theory]
    [InlineData(false, false, BotActivity.Offline, "offline")]
    [InlineData(false, true, BotActivity.Offline, "disconnected")]
    [InlineData(true, false, BotActivity.Offline, "connecting")]
    [InlineData(true, false, BotActivity.Connecting, "connecting")]
    [InlineData(true, false, BotActivity.InGame, "ingame")]
    [InlineData(true, false, BotActivity.Moving, "moving")]
    [InlineData(true, false, BotActivity.Gathering, "gathering")]
    [InlineData(true, false, BotActivity.Combat, "fight")]
    [InlineData(true, false, BotActivity.Dead, "dead")]
    [InlineData(true, false, BotActivity.Disconnected, "disconnected")]
    public void Internal_state_maps_to_the_documented_statuses(bool running, bool hint, BotActivity act, string expected)
        => Assert.Equal(expected, BotStatusMap.From(running, hint, act));

    [Fact]
    public void Every_documented_status_is_reachable_and_valid()
    {
        var produced = Enum.GetValues<BotActivity>().Select(a => BotStatusMap.From(true, false, a)).Append(BotStatusMap.From(false, false, BotActivity.Offline)).Append(BotStatusMap.From(false, true, BotActivity.Offline)).ToHashSet();
        Assert.All(produced, p => Assert.Contains(p, BotStatus.All));
        Assert.Equal(BotStatus.All.OrderBy(x => x), produced.OrderBy(x => x));
    }

    [Fact]
    public void The_alias_never_falls_back_to_a_login()
    {
        Assert.Equal("Farmeur 3", BotStatusMap.Alias(" Farmeur 3 ", "Njalla", "Old", 0));
        Assert.Equal("Njalla", BotStatusMap.Alias(null, "Njalla", "Old", 0));
        Assert.Equal("Old", BotStatusMap.Alias("  ", null, "Old", 0));
        Assert.Equal("compte-3", BotStatusMap.Alias(null, null, null, 2));
    }
}

public class SupervisorTemplatesTests
{
    [Fact]
    public void Templates_have_unique_ids_and_non_empty_code()
    {
        Assert.Equal(SupervisorTemplates.All.Count, SupervisorTemplates.All.Select(t => t.Id).Distinct().Count());
        Assert.All(SupervisorTemplates.All, t => { Assert.NotEmpty(t.Title); Assert.False(string.IsNullOrWhiteSpace(t.Code)); });
        Assert.NotNull(SupervisorTemplates.Get("veille"));
        Assert.Null(SupervisorTemplates.Get("nope"));
    }

    [Fact]
    public void Every_template_loads_in_the_sandbox_and_defines_move_or_a_callback()
    {
        foreach (var t in SupervisorTemplates.All)
        {
            var s = new Script(CoreModules.Preset_SoftSandbox);
            foreach (var g in new[] { "bots", "accounts", "proxies", "sendWebhook", "delay" }) s.Globals[g] = DynValue.NewTable(new Table(s));
            s.DoString(t.Code);   // une erreur de syntaxe ou de top-level lèverait ici
            Assert.True(s.Globals.Get("move").Type == DataType.Function || s.Globals.Get("onBotEvent").Type == DataType.Function, t.Id);
        }
    }

    [Fact]
    public void The_watch_template_reconnects_a_crashed_bot_and_stops_everything_on_a_moderator()
    {
        var h = new Harness();
        h.Host.Dispose();
        var rt = new SupervisorRuntime(new SupervisorServices(h.Dir, h.Orders, h.Accounts, h.Proxies, h.Perm, h.Bus));
        Assert.True(rt.Start("Veille", SupervisorTemplates.Get("veille")!.Code).Ok);
        rt.Raise("Farmeur", BotEvent.Of("crashed", ("reason", "crash")));
        rt.Raise("Banquier", BotEvent.Of("moderator", ("author", "Modo"), ("content", "salut")));
        var until = DateTime.UtcNow.AddSeconds(5);
        while (h.Orders.Calls.Count < 2 && DateTime.UtcNow < until) Thread.Sleep(20);
        Assert.Equal(new[] { "reconnect Farmeur", "stopAll" }, h.Orders.Calls);
        rt.Dispose();
    }
}

public class SupervisorNamingTests
{
    [Fact]
    public void Unique_names_never_collide_ignoring_case_and_keep_the_typed_case()
    {
        Assert.Equal("Superviseur", SupervisorNaming.Unique(null, Array.Empty<string>()));
        Assert.Equal("Superviseur 2", SupervisorNaming.Unique("", new[] { "Superviseur" }));
        Assert.Equal("veille 3", SupervisorNaming.Unique("veille", new[] { "VEILLE", "Veille 2" }));
        Assert.Equal("Autre", SupervisorNaming.Unique("  Autre ", new[] { "Veille" }));
    }
}

public class SupervisorFormatTests
{
    [Fact]
    public void Every_status_has_a_label_and_a_tone()
    {
        foreach (var s in BotStatus.All)
        {
            Assert.NotEqual(s, SupervisorFormat.StatusText(s));   // traduit
            Assert.Contains(SupervisorFormat.StatusTone(s), new[] { "ok", "warn", "bad", "off" });
        }
        Assert.Equal("ok", SupervisorFormat.StatusTone("fight"));
        Assert.Equal("warn", SupervisorFormat.StatusTone("disconnected"));
        Assert.Equal("bad", SupervisorFormat.StatusTone("dead"));
        Assert.Equal("off", SupervisorFormat.StatusTone("offline"));
        Assert.Equal("inconnu", SupervisorFormat.StatusText("inconnu"));
    }

    [Fact]
    public void Row_texts()
    {
        var b = Sup.Bot(pods: 45, podsMax: 90, script: true);
        Assert.Equal("50 %", SupervisorFormat.PodsText(b));
        Assert.Equal("—", SupervisorFormat.PodsText(Sup.Bot(podsMax: 0)));
        Assert.Equal("88213269", SupervisorFormat.MapText(b));
        Assert.Equal("4,-17", SupervisorFormat.MapText(b with { Coords = "4,-17" }));
        Assert.Equal("—", SupervisorFormat.MapText(Sup.Bot(map: 0)));
        Assert.Equal("farm", SupervisorFormat.ScriptText(b));
        Assert.Equal("en cours", SupervisorFormat.ScriptText(b with { Script = "" }));
        Assert.Equal("—", SupervisorFormat.ScriptText(Sup.Bot()));
        Assert.Equal("Socket", SupervisorFormat.ModeText("socket"));
        Assert.Equal("MITM", SupervisorFormat.ModeText("mitm"));
        Assert.Contains("MITM", SupervisorFormat.ModeTip("socket"));
        Assert.Contains("Socket", SupervisorFormat.ModeTip("mitm"));
    }

    [Fact]
    public void Overview_and_problem()
    {
        Assert.Equal("Aucun bot", SupervisorFormat.Overview(Array.Empty<BotSnapshot>()));
        Assert.Equal("1 bot · 1 en jeu", SupervisorFormat.Overview(new[] { Sup.Bot() }));
        var many = new[] { Sup.Bot(), Sup.Bot("B", "b", 12, "fight"), Sup.Bot("C", "c", 13, "disconnected"), Sup.Bot("D", "d", 0, "offline"), Sup.Bot("E", "e", 0, "offline") };
        Assert.Equal("5 bots · 2 en jeu · 1 déconnecté · 2 hors ligne", SupervisorFormat.Overview(many));
        Assert.Null(SupervisorFormat.Problem(new[] { Sup.Bot(), Sup.Bot("D", "d", 0, "offline") }));
        Assert.Equal("C (déconnecté), F (mort)", SupervisorFormat.Problem(new[] { Sup.Bot("C", "c", 13, "disconnected"), Sup.Bot("F", "f", 14, "dead") }));
        Assert.EndsWith("+1", SupervisorFormat.Problem(Enumerable.Range(0, 4).Select(i => Sup.Bot("X" + i, "x", 20 + i, "dead")).ToList())!);
    }
}
