using GivreClone.App.Services.Supervisor;
using MoonSharp.Interpreter;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Exécute une action sur un thread DÉDIÉ (pas le pool) : robuste quand la suite complète sature le pool de threads.</summary>
internal static class Bg
{
    public static Task Run(Action a)
    {
        var tcs = new TaskCompletionSource();
        new Thread(() => { try { a(); tcs.SetResult(); } catch (Exception e) { tcs.SetException(e); } }) { IsBackground = true }.Start();
        return tcs.Task;
    }
}

internal static class Sup
{
    public static BotSnapshot Bot(string account = "Farmeur", string name = "Njalla", int pid = 10, string status = BotStatus.InGame, int level = 50,
                                  int pods = 10, int podsMax = 100, bool script = false, long map = 88213269, string disconnect = "", string lastStop = "")
        => new(account, name, pid, "socket", status, MapId: map, Level: level, Pods: pods, PodsMax: podsMax, ScriptRunning: script,
               Script: script ? "farm" : "", Disconnect: disconnect, LastStop: lastStop, Kamas: 1234);
}

public class BotEventDiffTests
{
    private static IEnumerable<string> Names(BotSnapshot? p, BotSnapshot c) => BotEventDiff.Diff(p, c).Select(e => e.Name);

    [Fact]
    public void Arriving_in_game_is_connected()
    {
        Assert.Equal(new[] { "connected" }, Names(Sup.Bot(status: "offline", pid: 0), Sup.Bot()));
        Assert.Equal(new[] { "connected" }, Names(null, Sup.Bot()));
        Assert.Empty(Names(Sup.Bot(), Sup.Bot()));   // rien ne change
    }

    [Fact]
    public void Moving_between_world_statuses_is_not_a_connection()
        => Assert.Empty(Names(Sup.Bot(status: "ingame"), Sup.Bot(status: "gathering")));

    [Fact]
    public void Disconnected_carries_the_reason()
    {
        var e = Assert.Single(BotEventDiff.Diff(Sup.Bot(), Sup.Bot(status: "disconnected", disconnect: "kick")));
        Assert.Equal("disconnected", e.Name);
        Assert.Equal("kick", e.Info!["reason"]);
    }

    [Fact]
    public void Offline_reason_falls_back_to_the_last_stop_then_stopped()
    {
        Assert.Equal("user", BotEventDiff.Diff(Sup.Bot(), Sup.Bot(status: "offline", pid: 0, lastStop: "user")).Single(e => e.Name == "offline").Info!["reason"]);
        Assert.Equal("stopped", BotEventDiff.Diff(Sup.Bot(), Sup.Bot(status: "offline", pid: 0)).Single(e => e.Name == "offline").Info!["reason"]);
    }

    [Fact]
    public void Level_up_and_death()
    {
        var up = Assert.Single(BotEventDiff.Diff(Sup.Bot(level: 50), Sup.Bot(level: 51)));
        Assert.Equal(("levelUp", 51, 50), (up.Name, up.Info!["level"], up.Info["from"]));
        Assert.Equal(new[] { "death" }, Names(Sup.Bot(), Sup.Bot(status: "dead")));
        Assert.Empty(Names(Sup.Bot(level: 0), Sup.Bot(level: 12)));   // niveau encore inconnu : pas un gain
    }

    [Fact]
    public void Pods_full_fires_once_when_crossing_90_percent()
    {
        Assert.Equal(new[] { "podsFull" }, Names(Sup.Bot(pods: 89), Sup.Bot(pods: 90)));
        Assert.Empty(Names(Sup.Bot(pods: 90), Sup.Bot(pods: 95)));      // déjà au-dessus
        Assert.Empty(Names(Sup.Bot(pods: 10), Sup.Bot(pods: 50)));
        Assert.Equal(91, BotEventDiff.Diff(Sup.Bot(pods: 10), Sup.Bot(pods: 91)).Single().Info!["podsPercent"]);
    }

    [Fact]
    public void Script_started_and_stopped()
    {
        var start = Assert.Single(BotEventDiff.Diff(Sup.Bot(), Sup.Bot(script: true)));
        Assert.Equal(("scriptStarted", "farm"), (start.Name, start.Info!["script"]));
        var stop = Assert.Single(BotEventDiff.Diff(Sup.Bot(script: true), Sup.Bot(lastStop: "moderator")));
        Assert.Equal(("scriptStopped", "farm", "moderator"), (stop.Name, stop.Info!["script"], stop.Info["reason"]));
        Assert.Equal("ended", BotEventDiff.Diff(Sup.Bot(script: true), Sup.Bot()).Single().Info!["reason"]);
    }

    [Fact]
    public void Matcher_finds_by_pid_account_or_character_ignoring_case()
    {
        var all = new[] { Sup.Bot("Farmeur", "Njalla", 10), Sup.Bot("Banquier", "Sereneth", 11), Sup.Bot("Off", "Zed", 0, "offline") };
        Assert.Equal("Banquier", BotMatcher.Find(all, "banquier")!.Account);
        Assert.Equal("Banquier", BotMatcher.Find(all, "SERENETH")!.Account);
        Assert.Equal("Farmeur", BotMatcher.Find(all, "10")!.Account);
        Assert.Equal("Off", BotMatcher.Find(all, "zed")!.Account);
        Assert.Null(BotMatcher.Find(all, "inconnu"));
        Assert.Null(BotMatcher.Find(all, ""));
        Assert.Null(BotMatcher.Find(all, "0"));   // pid 0 = hors ligne, jamais une clé
    }

    // Régression live 02/11 : connect_bot « dedef » (hors ligne, perso vide) lançait ArcherbotLeRetour (aucun perso connu).
    [Fact]
    public void Offline_bot_with_empty_name_never_matches_another_account_without_character()
    {
        var offline = Sup.Bot("dedef", "", 0, "offline");
        Assert.True(BotMatcher.SameAccount(offline, "dedef", null));
        Assert.False(BotMatcher.SameAccount(offline, "ArcherbotLeRetour", null));
        Assert.False(BotMatcher.SameAccount(offline, "ArcherbotLeRetour", ""));
        Assert.True(BotMatcher.SameAccount(Sup.Bot("Autre", "Sylvanbot", 10), "dedef", "sylvanbot"));
    }

    [Fact]
    public void Snapshot_derived_values()
    {
        var b = Sup.Bot(pods: 45, podsMax: 90);
        Assert.Equal(50, b.PodsPercent);
        Assert.True(b.InGame && b.Online);
        Assert.False(Sup.Bot(status: "offline", pid: 0).Online);
        Assert.True(Sup.Bot(status: "connecting").Online && !Sup.Bot(status: "connecting").InGame);
        Assert.Equal(0, Sup.Bot(podsMax: 0).PodsPercent);
    }
}

public class MailboxTests
{
    private static BusMessage M(string subject, object? data = null) => new("s", null, subject, data);

    [Fact]
    public void Pending_is_capped_at_64_and_counts_the_loss()
    {
        var b = new Mailbox();
        for (int i = 0; i < 64; i++) Assert.True(b.Post(M("a")));
        Assert.False(b.Post(M("a")));
        Assert.Equal(1, b.TakeDropped());
        Assert.Equal(0, b.TakeDropped());
    }

    [Fact]
    public void Unconsumed_messages_are_kept_up_to_32_oldest_lost()
    {
        var b = new Mailbox();
        for (int i = 0; i < 40; i++) b.Post(M("k", (double)i));
        b.Pump(_ => false);
        Assert.Equal(32, b.KeptCount);
        Assert.Equal(8.0, b.TryTake("k")!.Data);   // les 8 plus anciens ont été perdus
    }

    [Fact]
    public void Consumed_messages_are_not_kept()
    {
        var b = new Mailbox();
        b.Post(M("x"));
        b.Pump(_ => true);
        Assert.Equal(0, b.KeptCount);
        Assert.Null(b.TryTake("x"));
    }

    [Fact]
    public void Wait_returns_a_kept_message_immediately_and_matches_case_insensitively()
    {
        var b = new Mailbox();
        b.Post(M("Arrive", 1.0));
        b.Pump(_ => false);
        Assert.Equal(1.0, b.Wait("arrive", TimeSpan.FromSeconds(1), default)!.Data);
    }

    [Fact]
    public void Wait_serves_other_subjects_to_the_callback_while_waiting()
    {
        var b = new Mailbox();
        var got = new List<string>();
        b.Post(M("other"));
        var hit = b.Wait("target", TimeSpan.FromSeconds(10), default, m =>
        {
            got.Add(m.Subject);
            b.Post(M("target", "ok"));
            return true;
        });
        Assert.Equal("ok", hit!.Data);
        Assert.Equal(new[] { "other" }, got);
    }

    [Fact]
    public void Wait_times_out_with_null_and_honours_cancellation()
    {
        var b = new Mailbox();
        Assert.Null(b.Wait("nope", TimeSpan.FromMilliseconds(120), default));
        var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => b.Wait("nope", TimeSpan.FromSeconds(5), cts.Token));
    }

    [Fact]
    public void Bus_routes_bot_messages_to_every_running_supervisor_and_counts_them()
    {
        var bus = new SupervisorBus();
        Assert.Equal(0, bus.SendToSupervisors(Sup.Bot(), "kamas", null));   // sans superviseur : 0
        var a = bus.RegisterSupervisor("A"); bus.RegisterSupervisor("B");
        Assert.Equal(2, bus.SendToSupervisors(Sup.Bot(), "kamas", 5.0));
        Assert.Equal(1, a.PendingCount);
        bus.UnregisterSupervisor("B");
        Assert.Equal(1, bus.SendToSupervisors(Sup.Bot(), "kamas", null));
        Assert.True(bus.AnySupervisorRunning);
    }

    [Fact]
    public void Bus_only_reaches_bots_with_a_main_script()
    {
        var bus = new SupervisorBus { BotHasMainScript = pid => pid == 10 };
        Assert.True(bus.SendToBot(10, "S", "go", null));
        Assert.False(bus.SendToBot(11, "S", "go", null));
        Assert.False(bus.SendToBot(0, "S", "go", null));
        Assert.Equal(1, bus.Broadcast(new[] { 10, 11 }, "S", "all", null));
    }
}
