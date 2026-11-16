using System.Xml.Linq;
using GivreClone.App.Services;
using GivreClone.App.Services.Supervisor;

if (args.Contains("--windows-smoke", StringComparer.Ordinal))
{
    if (args.Length != 1) throw new ArgumentException("Use --windows-smoke alone.");
    var adapter = new WindowsLocalToastDelivery();
    var sendCalls = await RunWindowsSmokeAsync(
        () => adapter.CheckAvailabilityAsync(CancellationToken.None),
        () => adapter.SendAsync(new LocalBotNotice("", LocalBotEvent.None, "GivreWork",
            "Test local des notifications Windows. Aucun compte ni jeu."), CancellationToken.None),
        line => Console.WriteLine(DateTimeOffset.UtcNow.ToString("O") + " " + line));
    Console.WriteLine("WINDOWS_SMOKE SendCalls=" + sendCalls + "; displayed=unverified; no account/game access.");
    return;
}

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); checks++;
}

var directory = Path.Combine(Path.GetTempPath(), "GivreWork-R6-Notifications-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "preferences.json");
    var preferences = new LocalNotificationPreferences(path);
    Check(!preferences.Get("A").Enabled, "opt in by default");
    var enabled = new LocalNotificationPreference(true, LocalBotEvent.All, 100, 30);
    preferences.Set("A", enabled);
    Check(new LocalNotificationPreferences(path).Get("A") == enabled, "account preferences survive reload");
    Check(!preferences.Get("B").Enabled, "account isolation");
    bool invalidRejected = false;
    try { preferences.Set("A", enabled with { KamasThreshold = 0 }); } catch (ArgumentOutOfRangeException) { invalidRejected = true; }
    Check(invalidRejected && preferences.Get("A") == enabled, "invalid preference preserves saved state");

    var now = DateTimeOffset.UnixEpoch;
    var notices = new List<LocalBotNotice>();
    var engine = new LocalBotNotifications(preferences.Get, notices.Add, () => now);
    var baseline = new BotSnapshot("alias", "character", 10, "socket", BotStatus.InGame, Kamas: 1000);
    engine.Observe("A", baseline);
    Check(notices.Count == 0, "initial balance is not a gain");
    engine.Pods("A", 100, 100); engine.Pods("A", 100, 100);
    Check(notices.Count(n => n.Event == LocalBotEvent.PodsFull) == 1, "full pods edge deduplicates");
    now += TimeSpan.FromSeconds(31);
    engine.Pods("A", 90, 100); engine.Pods("A", 100, 100);
    Check(notices.Count(n => n.Event == LocalBotEvent.PodsFull) == 2, "pods rearm after unloading");
    engine.Pods("A", 100, 0); engine.Pods("B", 100, 100);
    Check(notices.Count == 2, "unknown capacity and disabled account stay silent");
    engine.Observe("A", baseline with { Kamas = 1060 });
    engine.Observe("A", baseline with { Kamas = 1100 });
    Check(notices.Count(n => n.Event == LocalBotEvent.KamasGained) == 1, "cumulative positive gains reach threshold");
    engine.Observe("A", baseline with { Kamas = 500 });
    Check(notices.Count(n => n.Event == LocalBotEvent.KamasGained) == 1, "spending is not a gain");
    engine.Observe("A", baseline with { Pid = 11, Kamas = 900000 });
    Check(notices.Count(n => n.Event == LocalBotEvent.KamasGained) == 1, "new session resets balance baseline");
    engine.Observe("A", baseline with { Pid = 11, Kamas = 900000, ScriptRunning = true });
    engine.Observe("A", baseline with { Pid = 0, Status = BotStatus.Disconnected, Kamas = -1 });
    engine.Observe("A", baseline with { Pid = 0, Status = BotStatus.Disconnected, Kamas = -1 });
    Check(notices.Count(n => n.Event == LocalBotEvent.Disconnected) == 1 && notices.Count(n => n.Event == LocalBotEvent.ScriptEnded) == 1,
        "disconnect and script stop survive pid removal and deduplicate");
    engine.Raise("A", LocalBotEvent.Death); engine.Raise("A", LocalBotEvent.Death);
    engine.Raise("A", LocalBotEvent.FightWon); engine.Raise("A", LocalBotEvent.FightLost);
    Check(notices.Count(n => n.Event == LocalBotEvent.Death) == 1 && notices.Any(n => n.Event == LocalBotEvent.FightWon) && notices.Any(n => n.Event == LocalBotEvent.FightLost),
        "death cooldown and independent fight outcomes");
    preferences.Set("A", enabled with { Events = LocalBotEvent.Death });
    now += TimeSpan.FromSeconds(31);
    int count = notices.Count;
    engine.Raise("A", LocalBotEvent.FightWon);
    Check(notices.Count == count, "event mask applied immediately");
    Check(notices.All(n => !n.Body.Contains("character") && !n.Body.Contains("alias")), "desktop notice excludes identity");
    engine.Retain(new HashSet<string>());
    engine.Observe("A", baseline with { Status = BotStatus.Disconnected });
    Check(notices.Count == count, "removed accounts lose transition history");

    var xml = WindowsLocalToastDelivery.ToastXml(new("A", LocalBotEvent.Death, "<danger>&\"", "x</text><script>bad</script>"));
    var document = XDocument.Parse(xml);
    Check(document.Descendants("text").Count() == 2 && !document.Descendants("script").Any() && document.Descendants("text").First().Value == "<danger>&\"",
        "toast text cannot inject XML markup");
    File.WriteAllText(path, "{broken");
    bool corruptRejected = false;
    try { _ = new LocalNotificationPreferences(path); } catch (System.Text.Json.JsonException) { corruptRejected = true; }
    Check(corruptRejected && File.ReadAllText(path) == "{broken", "corrupt preference file preserved");
    File.WriteAllText(path, "{\"A\":null}");
    bool nullRejected = false;
    try { _ = new LocalNotificationPreferences(path); } catch (InvalidDataException) { nullRejected = true; }
    Check(nullRejected, "null preference entry rejected at disk boundary");
    string probe = WindowsLocalToastDelivery.PowerShellScript(null);
    Check(probe.Contains("$manager.Setting") && probe.Contains("exit 10") && !probe.Contains("LoadXml") && !probe.Contains(".Show("),
        "availability probe registers identity and checks OS setting without constructing or sending toast");
    string send = WindowsLocalToastDelivery.PowerShellScript(new("A", LocalBotEvent.Death, "'; exit 99; #", "$(bad)"));
    Check(!send.Contains("exit 99") && !send.Contains("$(bad)") && send.Contains("FromBase64String") && send.Contains("$manager.Show($toast)"),
        "PowerShell command encodes untrusted text as data");
    Check(WindowsLocalToastDelivery.DecodeExitCode(2).Status == LocalToastStatus.Failed &&
        WindowsLocalToastDelivery.DecodeExitCode(1).Status == LocalToastStatus.Failed,
        "WinRT or PowerShell failure never becomes success");
    Check(WindowsLocalToastDelivery.DecodeExitCode(0).Status == LocalToastStatus.Submitted &&
        WindowsLocalToastDelivery.DecodeExitCode(10).Status == LocalToastStatus.Available,
        "submission and availability remain distinct");
    Check(WindowsLocalToastDelivery.DecodeExitCode(23) == new LocalToastResult(LocalToastStatus.Suppressed, 3),
        "OS refusal preserves numeric notification setting");
    foreach (var status in new[] { LocalToastStatus.Suppressed, LocalToastStatus.Failed, LocalToastStatus.Unsupported,
        LocalToastStatus.TimedOut, LocalToastStatus.Busy })
    {
        int actualSends = 0;
        var lines = new List<string>();
        int sends = await RunWindowsSmokeAsync(() => Task.FromResult(new LocalToastResult(status, 3)),
            () => { actualSends++; return Task.FromResult(new LocalToastResult(LocalToastStatus.Submitted)); }, lines.Add);
        Check(sends == 0 && actualSends == 0 && lines.Any(line => line.Contains("Availability=" + status)) &&
            lines.Any(line => line.Contains("Send=Skipped")), "smoke does not call delivery when availability is " + status);
    }
    int allowedSends = 0;
    var smokeLines = new List<string>();
    int allowedCalls = await RunWindowsSmokeAsync(() => Task.FromResult(new LocalToastResult(LocalToastStatus.Available)),
        () => { allowedSends++; return Task.FromResult(new LocalToastResult(LocalToastStatus.Suppressed, 2)); }, smokeLines.Add);
    Check(allowedCalls == 1 && allowedSends == 1 && smokeLines.Any(line => line.Contains("Delivery=Suppressed Setting=2")),
        "available smoke sends exactly once and preserves refusal if OS permission changes");
    if (args.Length == 1)
    {
        Directory.CreateDirectory(args[0]);
        File.WriteAllText(Path.Combine(args[0], "probe.fixture.ps1"), probe);
        File.WriteAllText(Path.Combine(args[0], "send.fixture.ps1"), send);
    }
    Console.WriteLine($"RESULT: {checks} checks passed; no toast, process, account or network access executed.");
}
finally
{
    foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
    Directory.Delete(directory);
}

static async Task<int> RunWindowsSmokeAsync(Func<Task<LocalToastResult>> availability,
    Func<Task<LocalToastResult>> send, Action<string> log)
{
    LocalToastResult probe = await availability();
    log("Availability=" + probe.Status + " Setting=" + (probe.PlatformSetting?.ToString() ?? "n/a"));
    if (probe.Status != LocalToastStatus.Available)
    {
        log("Send=Skipped SendCalls=0; availability is not Available; no toast submission attempted.");
        return 0;
    }
    log("SendCalls=1; sending one generic GivreWork test toast.");
    LocalToastResult delivery = await send();
    log("Delivery=" + delivery.Status + " Setting=" + (delivery.PlatformSetting?.ToString() ?? "n/a") + "; displayed=unverified");
    return 1;
}
