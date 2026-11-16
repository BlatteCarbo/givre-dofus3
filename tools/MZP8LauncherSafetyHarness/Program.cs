using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using GivreLauncher.Core.Accounts;
using GivreLauncher.Core.Ankama;
using GivreLauncher.Core.Launch;
using GivreLauncher.Core.Transport;
using GivreLauncher.Core.Zaap;

var checkedCases = 0;
void Check(bool cond, string message)
{
    if (!cond) throw new Exception(message);
    checkedCases++;
}

void Refuses<T>(Action action, string description) where T : Exception
{
    try { action(); }
    catch (T) { checkedCases++; return; }
    throw new Exception("Not refused: " + description);
}

var root = Path.Combine(Path.GetTempPath(), "MZP8_LauncherSafety_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var file = Path.Combine(root, "Dofus.exe");
var wrong = Path.Combine(root, "GivreClone.exe");
try
{
    File.WriteAllBytes(file, new byte[] { 0x4D, 0x5A, 0x90, 0, 0, 0 });
    File.WriteAllBytes(wrong, new byte[] { 0x4D, 0x5A });
    Check(GameLauncher.ResolveDofusPath(file) == Path.GetFullPath(file), "explicit test path");
    Check(GameLauncher.ResolveDofusPath(wrong).Length == 0, "not Dofus.exe");
    Check(GameLauncher.ResolveDofusPath(file + ".nonexistent").Length == 0, "missing file");
    Check(GameLauncher.ResolveDofusPath(Path.Combine(root, "..", "outside.exe")).Length == 0, "outside path");
    Refuses<InvalidOperationException>(() => new GameLauncher().Launch(Guid.NewGuid().ToString(), 2, file), "default launch forbidden");
    var denied = new RefusingAuthority();
    Refuses<InvalidOperationException>(() => new GameLauncher(denied).Launch(Guid.NewGuid().ToString(), 2, file), "independent authority denied");
    Check(denied.Calls == 1, "authority consulted");
    Refuses<ArgumentException>(() => new GameLauncher(denied).Launch("malformed-id", 2, file), "session ID invalid");
    Refuses<ArgumentException>(() => new GameLauncher(denied).Launch(Guid.NewGuid().ToString(), 99, file), "mode invalid");
    var forged = new WrongExeAuthority();
    Refuses<InvalidOperationException>(() => new GameLauncher(forged).Launch(Guid.NewGuid().ToString(), 2, file), "wrong exe attempt");
    Check(!forged.Confirmed, "wrong executable blocked pre-confirmation");
    var auth = new FakeHandler(HttpStatusCode.OK, "{\"token\":\"test-server-response\"}");
    using (var client = new HttpClient(auth))
    using (var exchange = new AnkamaAuth(client, new CancelShield()))
    {
        var token = await exchange.AuthenticateAsync(new Account { Login = "test-alias", ApiKey = "test-api-key" });
        Check(token == "test-server-response", "real parsed server response");
        Check(auth.RequestCount == 1 && auth.CheckedHttps && auth.CheckedHeader, "auth request host + route + header");
        await RefusesAsync<AnkamaAuthException>(() => exchange.AuthenticateAsync(new Account { Login = "test-alias" }), "no apiKey cannot auth");
        Check(auth.RequestCount == 1, "no request for empty API key");
    }
    foreach (var (status, body) in new[]
    {
        (HttpStatusCode.Unauthorized, "{\"token\":\"not-an-ack\"}"),
        (HttpStatusCode.OK, "{\"error\":\"missing\"}"),
        (HttpStatusCode.OK, "broken-json"),
        (HttpStatusCode.Redirect, "{}"),
    })
    {
        using var client = new HttpClient(new FakeHandler(status, body));
        using var exchange = new AnkamaAuth(client, new CancelShield());
        await RefusesAsync<AnkamaAuthException>(
            () => exchange.AuthenticateAsync(new Account { Login = "test-alias", ApiKey = "test-api-key" }),
            "HTTP reject / bad response cannot make token");
    }
    await RefusesAsync<InvalidOperationException>(() => ZaapKeydataReader.ReadAllAsync(), "no default installed-keydata access");
    Refuses<ArgumentException>(() => ZaapKeydataReader.ConfigureTestFixture(Path.Combine(root, "test-keydata.json")),
        "cannot redirect keydata reader outside isolated TEST root");
    var isolated = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GivreReplica", "TestRuns", "MZP8-Harness-" + Guid.NewGuid().ToString("N"));
    var testData = Path.Combine(isolated, "test-keydata.json");
    Directory.CreateDirectory(isolated);
    try
    {
        File.WriteAllText(testData, "[{\"login\":\"test-user\",\"apiKey\":\"offline-test-api\"}]");
        ZaapKeydataReader.ConfigureTestFixture(testData);
        var parsed = await ZaapKeydataReader.ReadAllAsync();
        Check(parsed.Count == 1 && parsed[0].Login == "test-user", "explicit TEST fixture parsed, no installation accessed");
        File.WriteAllText(testData,
            "[{\"login\":\"duplicate\",\"apiKey\":\"one\"},{\"login\":\"DUPLICATE\",\"apiKey\":\"two\"}]");
        await RefusesAsync<InvalidDataException>(() => ZaapKeydataReader.ReadAllAsync(), "duplicate account fixture rejected");
    }
    finally
    {
        ZaapKeydataReader.ConfigureTestFixture(null);
        File.Delete(testData);
        Directory.Delete(isolated);
    }
    using (var zaap = new ZaapEmulator(26666))
    {
        Refuses<InvalidOperationException>(() => zaap.Start(), "historical Zaap port protected");
        Refuses<ArgumentException>(() => zaap.RegisterAccount("fake", "token", "test", 2), "invalid session ticket registration");
        zaap.RegisterAccount(Guid.NewGuid().ToString(), "test-server-response", "test-account", 2);
        Check(true, "ephemeral registration without binding protected port");
    }
    using (var zaap = new ZaapEmulator(45555))
        Refuses<InvalidOperationException>(() => zaap.Start(), "no external Zaap TEST wire authority");
    using (var relay = new HandshakeRelay(_ => null, 5658, _ => false))
        Refuses<InvalidOperationException>(() => relay.Start(), "historical MITM relay port protected");
    using (var relay = new HandshakeRelay(_ => null, 45556, _ => false))
        Refuses<InvalidOperationException>(() => relay.Start(), "no external relay PID admission authority");
    System.Console.WriteLine($"MZP8 Launcher TEST safety: PASS ({checkedCases} offline checks). No actual process launched.");
}

finally
{
    File.Delete(file);
    File.Delete(wrong);
    Directory.Delete(root, recursive: false);
}

async Task RefusesAsync<T>(Func<Task> action, string description) where T : Exception
{
    try { await action(); }
    catch (T) { checkedCases++; return; }
    throw new Exception("Not refused: " + description);
}

internal sealed class CancelShield : IShieldCodePrompt
{
    public Task<string?> RequestSecurityCodeAsync(string login, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}

internal sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public int RequestCount { get; private set; }
    public bool CheckedHttps { get; private set; }
    public bool CheckedHeader { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        CheckedHttps = request.RequestUri?.Scheme == Uri.UriSchemeHttps &&
            request.RequestUri.Host == "haapi.ankama.com" && request.RequestUri.AbsolutePath.EndsWith("CreateToken");
        CheckedHeader = request.Headers.Contains("apiKey") && request.Method == HttpMethod.Get;
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }
}

internal sealed class RefusingAuthority : IIndependentTestGameLaunchAuthority
{
    public int Calls { get; private set; }
    public ProcessStartInfo? CreateAuthorizedStartInfo(string exePath, string exeSha256, string accountSessionHash,
        int connectionType, string? hardwareSeed) { Calls++; return null; }
    public bool ConfirmStillAuthorized(string exePath, string exeSha256, string accountSessionHash) => false;
}

internal sealed class WrongExeAuthority : IIndependentTestGameLaunchAuthority
{
    public bool Confirmed { get; private set; }
    public ProcessStartInfo? CreateAuthorizedStartInfo(string exePath, string exeSha256, string accountSessionHash,
        int connectionType, string? hardwareSeed) => new() { FileName = "C:\\Windows\\System32\\cmd.exe", UseShellExecute = false };
    public bool ConfirmStillAuthorized(string exePath, string exeSha256, string accountSessionHash)
    { Confirmed = true; return true; }
}
