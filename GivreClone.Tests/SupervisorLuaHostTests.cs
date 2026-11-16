using GivreClone.App.Services.Supervisor;
using MoonSharp.Interpreter;
using Xunit;

namespace GivreClone.Tests;

internal sealed class FakeDirectory : IBotDirectory
{
    public List<BotSnapshot> Bots = new() { Sup.Bot("Farmeur", "Njalla", 10), Sup.Bot("Banquier", "Sereneth", 11, script: true), Sup.Bot("Off", "Zed", 0, "offline", level: 12) };
    public IReadOnlyList<BotSnapshot> Snapshot() => Bots.ToList();
}

internal sealed class FakeOrders : IBotOrders
{
    public List<string> Calls = new();
    public OrderResult Result = OrderResult.Yes;
    public TaskCompletionSource<OrderResult>? Hang;
    public int StopAll = 3, DisconnectAll = 2;
    private Task<OrderResult> R(string call) { Calls.Add(call); return Hang?.Task ?? Task.FromResult(Result); }
    public Task<OrderResult> ConnectAsync(string bot, string? c, string? s, CancellationToken ct) => R($"connect {bot} {c} {s}");
    public Task<OrderResult> DisconnectAsync(string bot, CancellationToken ct) => R($"disconnect {bot}");
    public Task<OrderResult> ReconnectAsync(string bot, CancellationToken ct) => R($"reconnect {bot}");
    public Task<OrderResult> SwitchCharacterAsync(string bot, string? c, string? s, bool restart, CancellationToken ct) => R($"switch {bot} {c} {s} {restart}");
    public Task<OrderResult> StartScriptAsync(string bot, string code, CancellationToken ct) => R($"start {bot} {code}");
    public Task<OrderResult> StopScriptAsync(string bot, CancellationToken ct) => R($"stop {bot}");
    public Task<OrderResult> RunAsync(string bot, string code, CancellationToken ct) => R($"run {bot} {code}");
    public Task<int> StopAllScriptsAsync(CancellationToken ct) { Calls.Add("stopAll"); return Task.FromResult(StopAll); }
    public Task<int> DisconnectAllAsync(CancellationToken ct) { Calls.Add("disconnectAll"); return Task.FromResult(DisconnectAll); }
}

internal sealed class FakeAccounts : IAccountsPort
{
    public List<AccountInfo> Items = new() { new("a1", "Farmeur 3", "Njalla", "ingame", "socket", false, true, "Box 4G", null) };
    public List<string> Calls = new();
    public IReadOnlyList<AccountInfo> List() => Items;
    public AccountInfo? Get(string key) => Items.FirstOrDefault(a => a.Id == key || a.Label.Equals(key, StringComparison.OrdinalIgnoreCase));
    public Task<(AccountInfo?, string)> AddAsync(string login, string? pw, string? label, string? proxy, CancellationToken ct)
    {
        Calls.Add($"add {login} pw={(pw is null ? "-" : "set")} {label} {proxy}");
        return Task.FromResult<(AccountInfo?, string)>(login == "dup" ? (null, "compte déjà enregistré") : (new AccountInfo("n1", label ?? login, "", "offline", "socket", true, false, proxy, null), ""));
    }
    public Task<(IReadOnlyList<AccountInfo>, int)> ImportFileAsync(string a, string? p, CancellationToken ct)
        => Task.FromResult<(IReadOnlyList<AccountInfo>, int)>((new[] { new AccountInfo("i1", "Imp", "", "offline", "socket", true, false, null, null, Added: true) }, 2));
    public OrderResult SetProxy(string a, string? p) { Calls.Add($"setProxy {a} {p}"); return OrderResult.Yes; }
    public OrderResult SetGameProxy(string a, string? p) { Calls.Add($"setGameProxy {a} {p}"); return OrderResult.Yes; }
    public OrderResult SetMode(string a, string m) { Calls.Add($"setMode {a} {m}"); return OrderResult.Yes; }
    public OrderResult SetAutoScript(string a, bool on) { Calls.Add($"auto {a} {on}"); return OrderResult.Yes; }
    public OrderResult SetLabel(string a, string l) { Calls.Add($"label {a} {l}"); return OrderResult.Yes; }
    public CharacterSpec? LastSpec;
    public OrderResult CreateCharacter(string a, CharacterSpec s) { LastSpec = s; Calls.Add($"create {a}"); return OrderResult.Yes; }
    public string? LastDelete;
    public OrderResult DeleteCharacter(string a, string? server, string? name, string? answer) { LastDelete = $"{server}|{name}|{answer}"; Calls.Add($"delete {a}"); return OrderResult.Yes; }
    public OrderResult CancelCharacter(string a) { Calls.Add($"cancel {a}"); return OrderResult.No("rien en attente"); }
}

internal sealed class FakeProxies : IProxiesPort
{
    public List<string> Calls = new();
    public IReadOnlyList<ProxyInfo> List() => new[] { new ProxyInfo("p1", "Box 4G", "socks5", "10.0.0.2", 1080, true, 2), new ProxyInfo("p2", "Carte", "adapter", null, null, null, 0) };
    public (ProxyInfo?, string) Add(string spec, string? name) { Calls.Add($"add {spec} {name}"); return spec == "bad" ? (null, "format non reconnu") : (new ProxyInfo("pn", name ?? spec, "socks5", "h", 1, false, 0), ""); }
    public IReadOnlyList<ProxyInfo> ImportFile(string path) => Array.Empty<ProxyInfo>();
    public Task<ProxyTestResult> TestAsync(string proxy, CancellationToken ct)
        => Task.FromResult(proxy == "Box 4G" ? new ProxyTestResult(true, "1.2.3.4", 80, 20) : new ProxyTestResult(false, "", null, null, "injoignable"));
}

internal sealed class Perms : ISupervisorPermissions { public bool HasMulti { get; set; } = true; }

internal sealed class Harness : IDisposable
{
    public readonly FakeDirectory Dir = new();
    public readonly FakeOrders Orders = new();
    public readonly FakeAccounts Accounts = new();
    public readonly FakeProxies Proxies = new();
    public readonly Perms Perm = new();
    public readonly SupervisorBus Bus = new();
    public readonly List<(string Level, string Text)> Logs = new();
    public SupervisorLuaHost Host = null!;
    public string? EndReason;

    public Harness(string name = "S") { Host = new SupervisorLuaHost(name, new SupervisorServices(Dir, Orders, Accounts, Proxies, Perm, Bus)); Host.Log += (l, t) => { lock (Logs) Logs.Add((l, t)); }; Host.Ended += r => EndReason = r; }

    public string[] Prints { get { lock (Logs) return Logs.Where(l => l.Level == "info").Select(l => l.Text).ToArray(); } }
    public string[] Errors { get { lock (Logs) return Logs.Where(l => l.Level == "error").Select(l => l.Text).ToArray(); } }

    /// <summary>Lance le script et attend sa fin naturelle (move() rend false) ; échoue si rien ne se termine en 10 s.</summary>
    public Harness Run(string code)
    {
        Host.Start(code);
        Assert.True(Host.WaitEnded(TimeSpan.FromSeconds(10)), "le script ne s'est pas terminé : " + string.Join(" | ", Errors));
        return this;
    }

    public void Dispose() => Host.Dispose();
}

public class SupervisorLuaHostTests
{
    [Fact]
    public void Bots_list_and_get_expose_the_bot_table()
    {
        using var h = new Harness().Run("""
            function move()
              local l = bots:list()
              print(#l)
              local b = bots:get("banquier")
              print(b.account, b.name, b.pid, b.mode, b.status, b.online, b.inGame, b.level, b.kamas, b.podsPercent, b.scriptRunning, b.script)
              print(bots:get("zed").status, tostring(bots:get("personne")), bots:get("11").name)
              return false
            end
            """);
        Assert.Equal(new[] { "3", "Banquier\tSereneth\t11\tsocket\tingame\ttrue\ttrue\t50\t1234\t10\ttrue\tfarm", "offline\tnil\tSereneth" }, h.Prints);
    }

    [Fact]
    public void Names_are_tolerant_and_dot_calls_work()
    {
        using var h = new Harness().Run("""
            function move()
              print(#bots:List(), #bots.list(), #bots:LIST())
              bots:switch_character("Farmeur", "Njalla")
              bots:SwitchCharacter("Farmeur", nil, "Hell Mina", false)
              return false
            end
            """);
        Assert.Equal("3\t3\t3", h.Prints[0]);
        Assert.Equal(new[] { "switch Farmeur Njalla  True", "switch Farmeur  Hell Mina False" }, h.Orders.Calls);
    }

    [Fact]
    public void Orders_return_ok_and_reason_and_reach_the_app()
    {
        var h = new Harness();
        h.Orders.Result = OrderResult.No("niveau plafonné");
        h.Run("""
            function move()
              local ok, why = bots:connect("Off", { character = "Zed", server = "Dakal" })
              print(tostring(ok), why)
              print(select('#', bots:disconnect("Farmeur")))
              bots:reconnect("Farmeur"); bots:startScript("Farmeur", "farm-otomai"); bots:stopScript("Farmeur"); bots:run("Farmeur", "travelTo(1)")
              return false
            end
            """);
        Assert.Equal("false\tniveau plafonné", h.Prints[0]);
        Assert.Equal("2", h.Prints[1]);
        Assert.Equal(new[] { "connect Off Zed Dakal", "disconnect Farmeur", "reconnect Farmeur", "start Farmeur farm-otomai", "stop Farmeur", "run Farmeur travelTo(1)" }, h.Orders.Calls);
        h.Dispose();
    }

    [Fact]
    public void Without_the_multi_permission_orders_are_refused_and_never_reach_the_app()
    {
        var h = new Harness();
        h.Perm.HasMulti = false;
        h.Run("""
            function move()
              print(bots:connect("Off"))
              print(tostring(bots:send("Farmeur", "x")), bots:broadcast("x"), bots:stopAllScripts(), bots:disconnectAll())
              print(accounts:setMode("a1", "socket"))
              print(tostring((accounts:add("x"))))
              print(proxies:add("1.1.1.1:80"))
              return false
            end
            """);
        Assert.Equal("false\tpermission Multi requise", h.Prints[0]);
        Assert.Equal("false\t0\t0\t0", h.Prints[1]);
        Assert.Equal("false\tpermission Multi requise", h.Prints[2]);
        Assert.Equal("nil", h.Prints[3]);
        Assert.Empty(h.Orders.Calls);
        Assert.Empty(h.Accounts.Calls);
        Assert.Empty(h.Proxies.Calls);
        h.Dispose();
    }

    [Fact]
    public void A_hanging_order_gives_up_after_the_timeout_instead_of_blocking_forever()
    {
        var h = new Harness();
        h.Host.OrderTimeoutMs = 300;
        h.Orders.Hang = new TaskCompletionSource<OrderResult>();
        h.Host.Start("function move() local ok, why = bots:connect('Off') print(tostring(ok), why) return false end");
        Assert.True(h.Host.WaitEnded(TimeSpan.FromSeconds(10)));
        Assert.Equal("false\tdélai dépassé (300 ms)", h.Prints.Single());
        Assert.Equal(15000, SupervisorLuaHost.DefaultOrderTimeoutMs);   // le portail : 15 s au plus
        h.Dispose();
    }

    [Fact]
    public void Stop_command_all_scripts_and_disconnect_all_return_counts()
    {
        using var h = new Harness().Run("function move() print(bots:stopAllScripts(), bots:disconnectAll()) return false end");
        Assert.Equal("3\t2", h.Prints.Single());
    }

    [Fact]
    public void WaitReady_returns_when_the_bot_reaches_the_game_and_false_on_timeout()
    {
        var h = new Harness();
        h.Dir.Bots[2] = Sup.Bot("Off", "Zed", 0, "offline");
        var flip = Bg.Run(() => { Thread.Sleep(300); h.Dir.Bots[2] = Sup.Bot("Off", "Zed", 12, "ingame"); });
        h.Run("function move() print(tostring(bots:waitReady('Off', 5)), tostring(bots:waitReady('Farmeur', 1)))  h = bots:get('Off') print(h.pid) return false end");
        flip.Wait();
        Assert.Equal("true\ttrue", h.Prints[0]);
        var h2 = new Harness();
        h2.Dir.Bots[2] = Sup.Bot("Off", "Zed", 0, "offline");
        h2.Run("function move() print(tostring(bots:waitReady('Off', 0.3))) return false end");
        Assert.Equal("false", h2.Prints.Single());
        h.Dispose(); h2.Dispose();
    }

    [Fact]
    public void OnBotEvent_receives_the_bot_the_event_name_and_infos()
    {
        var h = new Harness();
        h.Host.Start("""
            function onBotEvent(bot, ev, infos)
              print(bot.account, ev, infos and infos.reason or "-", infos and infos.level or "-")
            end
            function move() delay(20) end
            """);
        h.Host.Raise(Sup.Bot("Farmeur"), BotEvent.Of("disconnected", ("reason", "kick")));
        h.Host.Raise(Sup.Bot("Farmeur"), BotEvent.Of("levelUp", ("level", 51), ("from", 50)));
        h.Host.Raise(Sup.Bot("Farmeur"), BotEvent.Of("death"));
        SpinUntil(() => h.Prints.Length >= 3);
        h.Host.Stop();
        Assert.Equal(new[] { "Farmeur\tdisconnected\tkick\t-", "Farmeur\tlevelUp\t-\t51", "Farmeur\tdeath\t-\t-" }, h.Prints);
        h.Dispose();
    }

    [Fact]
    public void An_error_inside_a_callback_is_logged_and_the_supervisor_keeps_running()
    {
        var h = new Harness();
        h.Host.Start("""
            function onBotEvent(bot, ev) if ev == "boom" then error("oups") end print(ev) end
            function move() delay(20) end
            """);
        h.Host.Raise(Sup.Bot(), BotEvent.Of("boom"));
        h.Host.Raise(Sup.Bot(), BotEvent.Of("ok"));
        SpinUntil(() => h.Prints.Length >= 1);
        Assert.True(h.Host.Running);
        Assert.Contains(h.Errors, e => e.Contains("onBotEvent") && e.Contains("oups"));
        Assert.Equal("ok", h.Prints.Single());
        h.Dispose();
    }

    [Fact]
    public void Bot_messages_reach_onBotMessage_with_a_copy_of_the_data()
    {
        var h = new Harness();
        h.Host.Start("""
            function onBotMessage(bot, sujet, d) print(bot.name, sujet, d.besoin, d.liste[2], d.sous.ok) end
            function move() delay(20) end
            """);
        var data = new Dictionary<string, object?> { ["besoin"] = 200000.0, ["liste"] = new List<object?> { "a", "b" }, ["sous"] = new Dictionary<string, object?> { ["ok"] = true } };
        Assert.Equal(1, h.Bus.SendToSupervisors(Sup.Bot("Farmeur", "Njalla"), "kamas", data));
        SpinUntil(() => h.Prints.Length >= 1);
        Assert.Equal("Njalla\tkamas\t200000\tb\ttrue", h.Prints.Single());
        h.Dispose();
    }

    [Fact]
    public void WaitMessage_returns_the_sender_and_data_or_nil_on_timeout()
    {
        var h = new Harness();
        h.Host.Start("""
            function move()
              local bot, d = bots:waitMessage("arrive", 5000)
              print(bot.name, d.carte)
              print(tostring(bots:waitMessage("jamais", 100)))
              return false
            end
            """);
        Thread.Sleep(200);
        h.Bus.SendToSupervisors(Sup.Bot("Banquier", "Sereneth"), "arrive", new Dictionary<string, object?> { ["carte"] = 88213269.0 });
        Assert.True(h.Host.WaitEnded(TimeSpan.FromSeconds(10)));
        Assert.Equal(new[] { "Sereneth\t88213269", "nil" }, h.Prints);
        h.Dispose();
    }

    [Fact]
    public void A_message_without_callback_is_kept_for_a_later_waitMessage()
    {
        var h = new Harness();
        h.Bus.SendToSupervisors(Sup.Bot("Banquier", "Sereneth"), "tot", 7.0);
        h.Run("function move() local b, d = bots:waitMessage('tot', 1000) print(b.name, d) return false end");
        Assert.Equal("Sereneth\t7", h.Prints.Single());
        h.Dispose();
    }

    [Fact]
    public void Send_and_broadcast_target_only_bots_with_a_running_script()
    {
        var h = new Harness();
        h.Bus.BotHasMainScript = pid => pid == 11;
        h.Run("""
            function move()
              print(tostring(bots:send("Banquier", "stop", { raison = "test" })), tostring(bots:send("Farmeur", "stop")), tostring(bots:send("inconnu", "x")))
              print(bots:broadcast("zone", { carte = 1 }))
              return false
            end
            """);
        Assert.Equal(new[] { "true\tfalse\tfalse", "1" }, h.Prints);
        var msg = h.Bus.BotBox(11).TryTake("stop")!;   // livrée dans la boîte du bot 11, pas dans celle du bot 10
        Assert.Equal("S", msg.Sender);
        Assert.Equal("test", ((Dictionary<string, object?>)msg.Data!)["raison"]);
        Assert.Null(h.Bus.BotBox(10).TryTake("stop"));
        h.Dispose();
    }

    [Fact]
    public void Message_data_cannot_carry_functions()
    {
        var h = new Harness().Run("function move() local ok, e = pcall(function() bots:send('Banquier', 'x', { f = print }) end) print(tostring(ok), tostring(e):find('fonction') ~= nil) return false end");
        Assert.Equal("false\ttrue", h.Prints.Single());
        h.Dispose();
    }

    [Fact]
    public void Accounts_tables_expose_label_proxy_and_never_credentials()
    {
        using var h = new Harness().Run("""
            function move()
              local c = accounts:get("Farmeur 3")
              print(c.id, c.label, c.character, c.status, c.mode, tostring(c.browser), tostring(c.autoScript), c.proxy, tostring(c.gameProxy))
              for k, _ in pairs(c) do if k == "login" or k == "password" then print("FUITE " .. k) end end
              local l, fails = accounts:importFile("c.txt", "p.txt")
              print(#l, fails, tostring(l[1].added))
              return false
            end
            """);
        Assert.Equal("a1\tFarmeur 3\tNjalla\tingame\tsocket\tfalse\ttrue\tBox 4G\tnil", h.Prints[0]);
        Assert.Equal("1\t2\ttrue", h.Prints[1]);
        Assert.DoesNotContain(h.Prints, p => p.StartsWith("FUITE"));
    }

    [Fact]
    public void Account_settings_go_through_the_port_and_validate_the_mode()
    {
        var h = new Harness().Run("""
            function move()
              accounts:setMode("Farmeur 3", "SOCKET"); accounts:setAutoScript("Farmeur 3", true); accounts:setLabel("Farmeur 3", "F3")
              accounts:setProxy("Farmeur 3", "Box 4G"); accounts:setGameProxy("Farmeur 3", nil)
              print(accounts:setMode("Farmeur 3", "telepathie"))
              print(accounts:setAutoScript("Farmeur 3", "oui"))
              print(tostring(accounts:cancelCharacter("Farmeur 3")))
              return false
            end
            """);
        Assert.Equal(new[] { "setMode Farmeur 3 socket", "auto Farmeur 3 True", "label Farmeur 3 F3", "setProxy Farmeur 3 Box 4G", "setGameProxy Farmeur 3 ", "cancel Farmeur 3" }, h.Accounts.Calls);
        Assert.Equal("false\tmode inconnu (socket | mitm)", h.Prints[0]);
        Assert.Equal("false\tbooléen attendu", h.Prints[1]);
        h.Dispose();
    }

    [Fact]
    public void Add_returns_the_account_or_nil_and_the_reason()
    {
        using var h = new Harness().Run("""
            function move()
              local c = accounts:add("mon.compte@mail.fr", "pw", { label = "Farmeur 4", proxy = "socks5://u:p@10.0.0.2:1080" })
              print(c.label, c.proxy)
              local d, why = accounts:add("dup")
              print(tostring(d), why)
              return false
            end
            """);
        Assert.Equal(new[] { "Farmeur 4\tsocks5://u:p@10.0.0.2:1080", "nil\tcompte déjà enregistré" }, h.Prints);
        Assert.Equal("add mon.compte@mail.fr pw=set Farmeur 4 socks5://u:p@10.0.0.2:1080", h.Accounts.Calls[0]);
    }

    [Fact]
    public void CreateCharacter_parses_every_color_form()
    {
        var h = new Harness().Run("""
            function move()
              accounts:createCharacter("Farmeur 3", { server = "Dakal", breed = "Osamodas", sex = "f", name = "Bobette", head = 3, body = 2,
                  colors = { hair = "#3A6FD8", skin = 0xFF8800, outfit1 = "default" }, leaveTutorial = false })
              return false
            end
            """);
        var s = h.Accounts.LastSpec!;
        Assert.Equal(("Dakal", "Osamodas", "f", "Bobette", 3, 2, false), (s.Server, s.Breed, s.Sex, s.Name, s.Head, s.Body, s.LeaveTutorial));
        Assert.Equal("#3A6FD8", s.Colors!["hair"]);
        Assert.Equal("#FF8800", s.Colors["skin"]);
        Assert.Equal("default", s.Colors["outfit1"]);
        h.Dispose();
    }

    [Fact]
    public void DeleteCharacter_verb_passes_server_name_and_answer()
    {
        var h = new Harness().Run("""
            function move()
              accounts:deleteCharacter("Farmeur 3", { server = "Brial", name = "Test", answer = "Dofus" })
              return false
            end
            """);
        Assert.Equal("Brial|Test|Dofus", h.Accounts.LastDelete);
        Assert.Contains("delete Farmeur 3", h.Accounts.Calls);
        h.Dispose();
    }

    [Fact]
    public void CreateCharacter_defaults_leave_tutorial_on_and_accept_a_color_list()
    {
        var h = new Harness().Run("function move() accounts:createCharacter('Farmeur 3', { breed = 'cra', colors = { 0x112233, 0x445566 } }) return false end");
        var s = h.Accounts.LastSpec!;
        Assert.True(s.LeaveTutorial);
        Assert.Equal(("#112233", "#445566"), (s.Colors!["skin"], s.Colors["hair"]));
        Assert.Null(s.Name);
        h.Dispose();
    }

    [Fact]
    public void Proxies_list_add_and_test()
    {
        using var h = new Harness().Run("""
            function move()
              for _, p in ipairs(proxies:list()) do print(p.name, p.type, tostring(p.host), tostring(p.port), tostring(p.auth), p.accounts) end
              local p, why = proxies:add("bad"); print(tostring(p), why)
              print(proxies:add("socks5://u:p@10.0.0.2:1080", "Box").name)
              print(proxies:test("Box 4G"))
              print(proxies:test("Inconnu"))
              return false
            end
            """);
        Assert.Equal(new[] { "Box 4G\tsocks5\t10.0.0.2\t1080\ttrue\t2", "Carte\tadapter\tnil\tnil\tnil\t0", "nil\tformat non reconnu", "Box", "true\t1.2.3.4\t80\t20", "false\tinjoignable" }, h.Prints);
    }

    [Fact]
    public void GivreController_supervisor_paths_map_onto_accounts_and_proxies()
    {
        var h = new Harness().Run("""
            function move()
              print(tostring(givreController:registerAnAccount("Farmeur 3", "pw")))
              print(tostring(givreController:registerAnAccount("nouveau", "pw", "Nouveau")))
              print(tostring(givreController:assignProxyToAnAccount("Farmeur 3", "10.0.0.2", 1080, "u", "p")))
              print(tostring(givreController:createCharacter("Farmeur 3", "Bob", "Osamodas", "f", "Dakal", 0x112233)))
              return false
            end
            """);
        Assert.Equal(new[] { "true", "true", "true", "true" }, h.Prints);
        Assert.Contains("add nouveau pw=set Nouveau ", h.Accounts.Calls);
        Assert.Equal("add 10.0.0.2:1080:u:p ", h.Proxies.Calls.Single());
        Assert.Equal("Dakal", h.Accounts.LastSpec!.Server);
        Assert.Equal("#112233", h.Accounts.LastSpec.Colors!["skin"]);
        h.Dispose();
    }

    [Fact]
    public void SendWebhook_goes_through_the_service_and_reports_refusal()
    {
        var h = new Harness();
        var sent = new List<(string, string)>();
        h.Host.Dispose();
        var host = new SupervisorLuaHost("W", new SupervisorServices(h.Dir, h.Orders, h.Accounts, h.Proxies, h.Perm, h.Bus) { Webhook = (u, m) => { sent.Add((u, m)); return u.StartsWith("https://") ; } });
        var prints = new List<string>();
        host.Log += (l, t) => { if (l == "info") prints.Add(t); };
        host.Start("function move() print(tostring(sendWebhook('https://x/y', 'Modérateur sur ' .. 'Njalla')), tostring(sendWebhook('ftp://x', 'a')), tostring(sendWebhook(''))) return false end");
        Assert.True(host.WaitEnded(TimeSpan.FromSeconds(10)));
        Assert.Equal("true	false	false", prints.Single());
        Assert.Equal(("https://x/y", "Modérateur sur Njalla"), sent[0]);
        host.Dispose();
    }

    [Fact]
    public void Script_without_move_or_callbacks_is_refused()
    {
        using var h = new Harness().Run("x = 1");
        Assert.Contains(h.Errors, e => e.Contains("move()"));
        Assert.Equal("error", h.EndReason);
    }

    [Fact]
    public void Syntax_and_runtime_errors_end_the_script_with_a_message()
    {
        using var a = new Harness().Run("function move( end");
        Assert.Equal("error", a.EndReason);
        Assert.NotEmpty(a.Errors);
        using var b = new Harness().Run("function move() error('plantage') end");
        Assert.Equal("error", b.EndReason);
        Assert.Contains(b.Errors, e => e.Contains("plantage"));
    }

    [Fact]
    public void Stop_interrupts_a_long_delay_quickly_and_unregisters_on_dispose()
    {
        var h = new Harness("Long");
        h.Host.Start("function move() delay(60000) end");
        Thread.Sleep(150);
        Assert.True(h.Host.Running);
        Assert.True(h.Bus.AnySupervisorRunning);
        var t0 = DateTime.UtcNow;
        h.Host.Stop("user");
        Assert.True(h.Host.WaitEnded(TimeSpan.FromSeconds(3)));
        Assert.True(DateTime.UtcNow - t0 < TimeSpan.FromSeconds(2));
        Assert.Equal("user", h.EndReason);
        h.Dispose();
        Assert.False(h.Bus.AnySupervisorRunning);
    }

    [Fact]
    public void StopScript_ends_the_supervisor_from_inside()
    {
        using var h = new Harness().Run("function move() print('avant') stopScript() print('apres') end");
        Assert.Equal(new[] { "avant" }, h.Prints);
        Assert.Equal("script", h.EndReason);
    }

    [Fact]
    public void Move_is_called_in_a_loop_until_it_returns_false()
    {
        using var h = new Harness().Run("n = 0 function move() n = n + 1 if n >= 5 then print(n) return false end end");
        Assert.Equal("5", h.Prints.Single());
    }

    [Fact]
    public void Sandbox_has_no_io_or_os_execute()
    {
        using var h = new Harness().Run("function move() print(tostring(io), tostring(os and os.execute), type(os and os.time)) return false end");
        Assert.Equal("nil\tnil\tfunction", h.Prints.Single());
    }

    [Fact]
    public void LuaData_round_trips_nested_data_and_rejects_functions()
    {
        var s = new Script();
        var v = s.DoString("return { a = 1, b = { 'x', 'y' }, c = { d = true } }");
        var o = (Dictionary<string, object?>)LuaData.ToObject(v)!;
        Assert.Equal(1.0, o["a"]);
        Assert.Equal(new object?[] { "x", "y" }, ((List<object?>)o["b"]!).ToArray());
        Assert.Equal(true, ((Dictionary<string, object?>)o["c"]!)["d"]);
        var back = LuaData.FromObject(s, o).Table;
        Assert.Equal("y", back.Get("b").Table.Get(2).String);
        Assert.Throws<ArgumentException>(() => LuaData.ToObject(s.DoString("return { f = function() end }")));
        // profondeur excessive
        Assert.Throws<ArgumentException>(() => LuaData.ToObject(s.DoString("local t = {} local c = t for i = 1, 20 do c.x = {} c = c.x end return t")));
    }

    private static void SpinUntil(Func<bool> cond, int ms = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!cond() && DateTime.UtcNow < until) Thread.Sleep(20);
    }
}
