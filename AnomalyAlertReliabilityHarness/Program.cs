using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GivreClone.App.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("server/subarea/direction identity", Identity),
    ("retry only after real success", Retry),
    ("bounded memory and inflight", Bounded),
    ("HTTP 2xx vs failures and safe diagnostics", HttpOutcomes),
    ("DPAPI store migration and backups", Migration),
    ("Discord 429 rate limit and retry", DiscordReliabilityTests.RateLimitRetry),
    ("Discord redirects and uncertain responses", DiscordReliabilityTests.NoFalseAck),
};

int passed = 0;
foreach (var (name, run) in tests)
{
    try { await run(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { Console.Error.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}"); }
}
Console.WriteLine($"{passed}/{tests.Length} passed");
return passed == tests.Length ? 0 : 1;

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

static Task Identity()
{
    const string url = "https://discord.com/api/webhooks/test-id/test-secret-only-in-memory";
    var a = AnomalyAlertKey.Create(url, "Brumen", 11, 55, true);
    var sameServer = AnomalyAlertKey.Create(url, "brumen ", 23, 55, true);
    Require(a == sameServer, "Same server should coalesce across PIDs");
    Require(a != AnomalyAlertKey.Create(url, "Agride", 11, 55, true), "Different servers must not coalesce");
    Require(a != AnomalyAlertKey.Create(url, "Brumen", 11, 56, true), "Different subareas must not coalesce");
    Require(a != AnomalyAlertKey.Create(url, "Brumen", 11, 55, false), "Opened and closed differ");
    Require(a != AnomalyAlertKey.Create(url + "2", "Brumen", 11, 55, true), "Different webhooks differ");
    Require(AnomalyAlertKey.Create(url, "-", 1, 55, true) != AnomalyAlertKey.Create(url, "", 2, 55, true), "Unknown server must use PID");
    Require(!a.ToString().Contains("test-secret", StringComparison.Ordinal), "No token in key representation");
    return Task.CompletedTask;
}

static Task Retry()
{
    var d = new AnomalyAlertDeduplicator(capacity: 3, dedupMs: 1000);
    var key = AnomalyAlertKey.Create("https://example.test/secret", "alpha", 1, 10, true);
    Require(d.TryBegin(key, 10) == AnomalyAlertBegin.Reserved, "Reserve first request");
    Require(d.TryBegin(key, 20) == AnomalyAlertBegin.Duplicate, "Inflight request must coalesce");
    d.Complete(key, false, 30);
    Require(d.TryBegin(key, 40) == AnomalyAlertBegin.Reserved, "Failed HTTP allows retry");
    d.Complete(key, true, 50);
    Require(d.TryBegin(key, 100) == AnomalyAlertBegin.Duplicate, "HTTP success starts TTL");
    Require(d.TryBegin(key, 1051) == AnomalyAlertBegin.Reserved, "After TTL may send again");
    d.Complete(key, false, 1060);
    Require(d.Count == 0, "Failures do not consume memory");
    return Task.CompletedTask;
}

static Task Bounded()
{
    var d = new AnomalyAlertDeduplicator(capacity: 2, dedupMs: 100_000);
    var a = AnomalyAlertKey.Create("https://a.test", "one", 1, 1, true);
    var b = AnomalyAlertKey.Create("https://b.test", "one", 1, 1, true);
    var c = AnomalyAlertKey.Create("https://c.test", "one", 1, 1, true);
    Require(d.TryBegin(a, 0) == AnomalyAlertBegin.Reserved, "A pending");
    Require(d.TryBegin(b, 0) == AnomalyAlertBegin.Reserved, "B pending");
    Require(d.TryBegin(c, 0) == AnomalyAlertBegin.Saturated, "All pending = bounded rejection");
    d.Complete(a, true, 1);
    Require(d.TryBegin(c, 2) == AnomalyAlertBegin.Saturated, "Do not evict an unexpired success");
    Require(d.Count == 2, "Hard memory cap");
    Require(d.TryBegin(c, 100002) == AnomalyAlertBegin.Reserved, "Expired success frees capacity");
    d.Complete(c, false, 100003);
    d.Complete(b, false, 3);
    Require(d.Count == 0, "Release pending requests");
    return Task.CompletedTask;
}

static async Task HttpOutcomes()
{
    string secret = "test-secret-DO-NOT-LOG";
    string url = "https://discord.com/api/webhooks/id/" + secret;
    List<string> warnings = new();
    var handler = new StubHandler(HttpStatusCode.NoContent);
    using var client = new HttpClient(handler);
    Require(await WebhookService.PostWithClientAsync(url, "line \"quoted\"\nnext", client, warnings.Add), "204 is delivered");
    Require(handler.Posts == 1 && handler.Body!.Contains("\\\"quoted\\\"", StringComparison.Ordinal), "JSON escaping preserved");
    handler.StatusCode = HttpStatusCode.TooManyRequests;
    Require(!await WebhookService.PostWithClientAsync(url, "message", client, warnings.Add), "429 rejected");
    handler.StatusCode = HttpStatusCode.InternalServerError;
    Require(!await WebhookService.PostWithClientAsync(url, "message", client, warnings.Add), "500 rejected");
    handler.Throw = true;
    Require(!await WebhookService.PostWithClientAsync(url, "message", client, warnings.Add), "Transport failure rejected");
    Require(!await WebhookService.PostWithClientAsync("http://example.test/", "message", client, warnings.Add), "Insecure URL rejected");
    Require(warnings.Count >= 4, "Failures observable");
    Require(warnings.All(s => !s.Contains(secret, StringComparison.Ordinal) && !s.Contains(url, StringComparison.Ordinal)), "No secret in logs");
}

static Task Migration()
{
    if (!OperatingSystem.IsWindows()) { Console.WriteLine("INFO Windows-only DPAPI case skipped"); return Task.CompletedTask; }
    string dir = Path.Combine(Path.GetTempPath(), "givre-alert-harness-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    MZP19LocalClone.DataRoot = dir;
    string path = Path.Combine(dir, "bot-settings.json");
    const string url = "https://discord.com/api/webhooks/1111111/TEST_SECRET_legacy_migration";
    try
    {
        var legacy = "{\"Settings\":{\"Character\":{\"DiscordWebhookUrl\":\"" + url + "\",\"NotifyAnomalyOpened\":true}}}";
        File.WriteAllText(path, legacy);
        File.WriteAllText(path + ".bak", legacy);
        BotSettingsStore.Load();
        var cfg = BotSettingsStore.Get("Character");
        Require(cfg.DiscordWebhookUrl == url, "UI reading existing URL still works");
        string modern = File.ReadAllText(path);
        string backup = File.ReadAllText(path + ".bak");
        Require(!modern.Contains(url, StringComparison.Ordinal) && !backup.Contains(url, StringComparison.Ordinal), "Both files scrub legacy token");
        Require(!modern.Contains("\"DiscordWebhookUrl\"", StringComparison.Ordinal), "Legacy property not serialized");
        Require(modern.Contains("DiscordWebhookProtected", StringComparison.Ordinal), "Protected property persisted");
        BotSettingsStore.Load();
        Require(BotSettingsStore.Get("Character").DiscordWebhookUrl == url, "Reload decrypts correctly");
        // Le backup peut être une ancienne version NON migrée même si le principal est déjà protégé.
        File.WriteAllText(path + ".bak", legacy);
        BotSettingsStore.Load();
        Require(!File.ReadAllText(path + ".bak").Contains(url, StringComparison.Ordinal),
            "Legacy backup scrubbed independently after an already-encrypted main file");
        cfg = BotSettingsStore.Get("Character");
        cfg.DiscordWebhookUrl = "";
        BotSettingsStore.Save();
        BotSettingsStore.Load();
        Require(BotSettingsStore.Get("Character").DiscordWebhookUrl == "", "URL clearing persists");

        var damaged = new BotSettings { DiscordWebhookProtected = "invalid-base64-secret-ciphertext" };
        Require(damaged.DiscordWebhookUrl == "", "Corrupt cipher safely fails closed");
        damaged.ProtectWebhookForSave();
        Require(damaged.DiscordWebhookProtected == "invalid-base64-secret-ciphertext", "Unrelated saves preserve unreadable cipher");
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
    return Task.CompletedTask;
}

sealed class StubHandler(HttpStatusCode statusCode) : HttpMessageHandler
{
    public HttpStatusCode StatusCode { get; set; } = statusCode;
    public bool Throw { get; set; }
    public int Posts { get; private set; }
    public string? Body { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Posts++;
        Body = await request.Content!.ReadAsStringAsync(token);
        if (Throw) throw new HttpRequestException("sensitive URL https://discord.com/api/webhooks/private/test-secret-DO-NOT-LOG");
        return new HttpResponseMessage(StatusCode);
    }
}

// Test harness stubs for App-level dependencies. Production BotSettingsStore, AtomicJson,
// WebhookService and BotSettings are compiled verbatim from the source tree.
namespace GivreClone.App.Services
{
    internal static class MZP19LocalClone { internal static string DataRoot = ""; }
    internal static class Logger
    {
        public static void Warn(string message) => Console.WriteLine("DIAGNOSTIC " + message);
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(BotSettingsFile))]
    internal sealed partial class AppJsonContext : JsonSerializerContext { }
}
