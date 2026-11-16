using System.Net;
using System.Net.Sockets;
using GivreClone.App.Services.LocalLaunch;
using GivreLauncher.Core.Zaap;
using Xunit;

namespace GivreClone.Tests;

public class LocalZaapAuthorityTests : IDisposable
{
    private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(10));
    private CancellationToken Ct => _deadline.Token;
    public void Dispose() => _deadline.Dispose();

    private static int FreePort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }

    private async Task<ThriftBinary.Message> Call(NetworkStream s, string name, int seq, params (short Id, object V)[] args)
    {
        var d = new Dictionary<short, object>(); foreach (var (i, v) in args) d[i] = v;
        var bytes = ThriftBinary.EncodeCall(name, seq, d);
        await s.WriteAsync(bytes, Ct);
        // la réponse a la même forme qu'un message (un seul champ 0 « success » ou aucun)
        return await ThriftBinary.ReadMessageAsync(s, Ct);
    }

    [Fact]
    public async Task Port_must_not_be_a_protected_one_and_needs_an_authority()
    {
        using var z = new ZaapEmulator(26666);
        using var a = new LocalZaapAuthority(26666, FreePort(), "x:1");
        z.ConfigureOwnedLoopbackTest(a);
        Assert.False(z.HasIndependentTestAuthority);   // 26666 jamais admis
        using var z2 = new ZaapEmulator(FreePort());
        Assert.False(z2.HasIndependentTestAuthority);  // pas encore d'autorité
        z2.ConfigureOwnedLoopbackTest(a);
        Assert.True(z2.HasIndependentTestAuthority);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Full_thrift_session_serves_token_once_and_connection_type()
    {
        int port = FreePort();
        var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var zaap = new ZaapEmulator(port);
        using var auth = new LocalZaapAuthority(port, FreePort(), "Dofus:127.0.0.1:1", logs.Enqueue);
        zaap.ConfigureOwnedLoopbackTest(auth);
        string hash = Guid.NewGuid().ToString();
        zaap.RegisterAccount(hash, "JETON-TEST", "login", 2);
        auth.Expect(hash);
        zaap.Start();

        using var c = new TcpClient();
        await c.ConnectAsync(IPAddress.Loopback, port, Ct);
        var s = c.GetStream();
        var r1 = await Call(s, "connect", 1, ((short)4, hash));
        Assert.Equal(hash, r1.Args[0]);
        var r2 = await Call(s, "auth_getGameToken", 2, ((short)1, hash));
        Assert.Equal("JETON-TEST", r2.Args[0]);
        var r3 = await Call(s, "settings_get", 3, ((short)2, "autoConnectType"));
        Assert.Equal("2", r3.Args[0]);
        var r4 = await Call(s, "auth_getGameToken", 4, ((short)1, hash));
        Assert.Equal("", r4.Args[0]);                  // usage unique
        var r5 = await Call(s, "zaapMustUpdate_get", 5);
        Assert.Equal(false, r5.Args[0]);
        var r6 = await Call(s, "hasGameAccess", 6, ((short)1, hash), ((short)2, 1));
        Assert.Equal(true, r6.Args[0]);
        var r7 = await Call(s, "auth_getGameTokenWithWindowId", 7, ((short)1, hash));
        Assert.Equal("", r7.Args[0]);                // l'alias ne doit pas contourner l'usage unique
        // Réponses Thrift ci-dessus sont la preuve de l'usage unique.
        // Les libellés du journal peuvent varier selon le fournisseur Zaap
        // (jeton initial ou rafraîchi) ; ne pas coupler le contrat aux mots.
        Assert.Contains(logs, l => l.Contains("auth_getGameToken", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, l => l.Contains("JETON-TEST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_hash_is_refused_and_gets_no_token()
    {
        int port = FreePort();
        using var zaap = new ZaapEmulator(port);
        using var auth = new LocalZaapAuthority(port, FreePort(), "x:1");
        zaap.ConfigureOwnedLoopbackTest(auth);
        zaap.RegisterAccount(Guid.NewGuid().ToString(), "SECRET", "l", 0);
        zaap.Start();
        using var c = new TcpClient();
        await c.ConnectAsync(IPAddress.Loopback, port, Ct);
        var s = c.GetStream();
        var r = await Call(s, "connect", 1, ((short)4, Guid.NewGuid().ToString()));
        Assert.Equal("", r.Args[0]);
        int n = -1;
        try
        {
            await s.WriteAsync(ThriftBinary.EncodeCall("auth_getGameToken", 2, new Dictionary<short, object> { [1] = "x" }), Ct);
            n = await s.ReadAsync(new byte[64], Ct);   // le serveur a fermé : fin de flux...
        }
        catch (IOException) { n = 0; }                    // ...ou connexion coupée : dans les deux cas, aucun jeton lisible
        Assert.Equal(0, n);
    }

    [Fact]
    public void Launch_recipe_needs_expected_session_and_real_dofus_exe()
    {
        using var auth = new LocalZaapAuthority(40001, 40002, "Dofus:127.0.0.1:1");
        string hash = Guid.NewGuid().ToString(); string sha = new string('a', 64);
        Assert.Null(auth.CreateAuthorizedStartInfo(@"C:\x\Dofus.exe", sha, hash, 2, null));   // session non annoncée
        auth.Expect(hash);
        Assert.Null(auth.CreateAuthorizedStartInfo(@"C:\x\Autre.exe", sha, hash, 2, null));
        var psi = auth.CreateAuthorizedStartInfo(@"C:\x\Dofus.exe", sha, hash, 2, "seed");
        Assert.NotNull(psi);
        Assert.Contains("--hash", psi!.ArgumentList);
        Assert.Contains(hash, psi.ArgumentList);
        Assert.Equal("40001", psi.ArgumentList[psi.ArgumentList.IndexOf("--port") + 1]);
        Assert.Equal("5555", psi.ArgumentList[psi.ArgumentList.IndexOf("--connectionPort") + 1]);
        Assert.Contains("divazaap.json", psi.ArgumentList[psi.ArgumentList.IndexOf("--configUrl") + 1]);   // recette éprouvée (ZaapEmu, Givre)
        Assert.Equal("true", psi.EnvironmentVariables["ZAAP_CAN_AUTH"]);
        Assert.False(psi.UseShellExecute);
        auth.UseConfigUrl = false;
        var psi2 = auth.CreateAuthorizedStartInfo(@"C:\x\Dofus.exe", sha, hash, 2, null);
        Assert.DoesNotContain("--configUrl", psi2!.ArgumentList);
    }

    [Fact]
    public void Config_json_carries_the_requested_connection_host()
    {
        var j = LocalZaapAuthority.BuildConfigJson("Dofus:127.0.0.1:5555");
        Assert.Contains("\"connectionHosts\":[\"Dofus:127.0.0.1:5555\"]", j);
    }

    [Fact]
    public async Task Later_token_requests_get_a_fresh_token_from_the_refresher_like_the_real_launcher()
    {
        int port = FreePort();
        using var zaap = new ZaapEmulator(port);
        using var auth = new LocalZaapAuthority(port, FreePort(), "x:1");
        zaap.ConfigureOwnedLoopbackTest(auth);
        string hash = Guid.NewGuid().ToString();
        zaap.RegisterAccount(hash, "T1", "login", 0);
        auth.Expect(hash);
        int n = 1;
        auth.SetTokenRefresher(hash, _ => Task.FromResult<string?>("T" + (++n)));
        zaap.Start();
        using var c = new TcpClient();
        await c.ConnectAsync(IPAddress.Loopback, port, Ct);
        var s = c.GetStream();
        await Call(s, "connect", 1, ((short)4, hash));
        Assert.Equal("T1", (await Call(s, "auth_getGameToken", 2, ((short)1, hash))).Args[0]);
        Assert.Equal("T2", (await Call(s, "auth_getGameToken", 3, ((short)1, hash))).Args[0]);
        Assert.Equal("T3", (await Call(s, "auth_getGameTokenWithWindowId", 4, ((short)1, hash), ((short)2, 1), ((short)3, 0))).Args[0]);
    }

    [Fact]
    public async Task A_failing_refresher_gives_an_empty_token_not_a_crash()
    {
        int port = FreePort();
        using var zaap = new ZaapEmulator(port);
        using var auth = new LocalZaapAuthority(port, FreePort(), "x:1");
        zaap.ConfigureOwnedLoopbackTest(auth);
        string hash = Guid.NewGuid().ToString();
        zaap.RegisterAccount(hash, "T1", "login", 0);
        auth.Expect(hash);
        auth.SetTokenRefresher(hash, _ => throw new InvalidOperationException("HAAPI refusé"));
        zaap.Start();
        using var c = new TcpClient();
        await c.ConnectAsync(IPAddress.Loopback, port, Ct);
        var s = c.GetStream();
        await Call(s, "connect", 1, ((short)4, hash));
        await Call(s, "auth_getGameToken", 2, ((short)1, hash));
        Assert.Equal("", (await Call(s, "auth_getGameToken", 3, ((short)1, hash))).Args[0]);
        Assert.Equal(false, (await Call(s, "zaapMustUpdate_get", 4)).Args[0]);   // la session continue
    }
}
