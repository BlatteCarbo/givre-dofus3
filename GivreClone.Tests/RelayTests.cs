using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using GivreClone.App.Services.Relay;
using GivreClone.Engine.Proxies;
using Xunit;

namespace GivreClone.Tests;

/// <summary>GivreRelay côté client : nom d'hôte lu dans la poignée TLS (le mode classique ne voit que l'IP des appels HTTPS du
/// client Dofus ; l'agent « Dofus uniquement » filtre par nom) et entrée stable d'un partage dans la liste des proxys.</summary>
public sealed class RelayTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "givre-relay-tests-" + Guid.NewGuid().ToString("N"));

    public RelayTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ } }

    // Premier enregistrement TLS émis par un vrai SslStream .NET (même pile que l'authentification HAAPI).
    private static async Task<byte[]> CaptureClientHelloAsync(string targetHost)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try
        {
            using var c = new TcpClient();
            var accept = l.AcceptTcpClientAsync(ct);
            await c.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)l.LocalEndpoint).Port, ct);
            using var server = await accept;
            using var ssl = new SslStream(c.GetStream(), false, (_, _, _, _) => true);
            var hs = ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = targetHost }, ct);
            var s = server.GetStream();
            try
            {
                // A TCP read may return only part of the TLS header or body.
                var header = new byte[5];
                await s.ReadExactlyAsync(header, ct);
                var hello = new byte[5 + ((header[3] << 8) | header[4])];
                header.CopyTo(hello, 0);
                await s.ReadExactlyAsync(hello.AsMemory(5), ct);
                return hello;
            }
            finally
            {
                c.Close();
                try { await hs.WaitAsync(ct); }
                catch (IOException) { /* poignée volontairement interrompue */ }
                catch (AuthenticationException) { /* idem */ }
            }
        }
        finally { l.Stop(); }
    }

    [Theory]
    [InlineData("haapi.ankama.com")]
    [InlineData("dofus2-co-production.ankama-games.com")]
    public async Task Sni_is_read_from_a_real_client_hello(string host)
    {
        var hello = await CaptureClientHelloAsync(host);
        Assert.Equal(host, TlsSni.Parse(hello));
    }

    [Fact]
    public async Task Truncated_or_foreign_bytes_give_no_name()
    {
        var hello = await CaptureClientHelloAsync("auth.ankama.com");
        Assert.Null(TlsSni.Parse(hello.AsSpan(0, 60)));                    // ClientHello coupé avant les extensions
        Assert.Null(TlsSni.Parse(new byte[] { 0x0A, 0x03, 0x08, 0x01 }));  // trame Dofus (varint + protobuf), pas du TLS
        Assert.Null(TlsSni.Parse(ReadOnlySpan<byte>.Empty));
    }

    // ------------------------------------------------------------------ notifications (ce qui a changé entre deux listes)

    private static RelayExit Exit(string id, string label = "Chez Paul", bool online = true, string scope = "ankama",
                                  long used = 0, long quota = 10L << 30)
        => new(id, label, online, "FR", scope, "92.184.112.79", used, quota, null);

    private static Dictionary<string, RelayExit> Map(params RelayExit[] e) => e.ToDictionary(x => x.Id);

    private static RelayChangeKind[] Kinds(List<RelayChange> c) => c.Select(x => x.Kind).ToArray();

    [Fact]
    public void First_list_only_announces_agents_created_while_the_app_was_closed()
    {
        var now = Map(Exit("a"), Exit("b", "Box", online: false));
        var c = RelayChanges.Diff(null, now, listed: true, new HashSet<string> { "a" });
        Assert.Equal(new[] { RelayChangeKind.Added, RelayChangeKind.OfflineAtStart }, Kinds(c));
        Assert.All(c, x => Assert.Equal("b", x.Id));
        Assert.Empty(RelayChanges.Diff(null, now, listed: false, new HashSet<string>()).Where(x => x.Kind == RelayChangeKind.Added));
    }

    [Fact]
    public void Every_change_of_a_known_agent_is_reported()
    {
        var before = Map(Exit("a"));
        var after = Map(Exit("a", "Chez Paul (box)", online: false, scope: "all", used: 20L << 30, quota: 20L << 30));
        var c = RelayChanges.Diff(before, after, listed: false, new HashSet<string> { "a" });
        Assert.Equal(new[] { RelayChangeKind.Renamed, RelayChangeKind.Scope, RelayChangeKind.Quota, RelayChangeKind.QuotaReached,
                             RelayChangeKind.WentOffline }, Kinds(c));
        Assert.Equal("Chez Paul", c[0].Before!.Label);

        var back = RelayChanges.Diff(after, Map(Exit("a", "Chez Paul (box)", scope: "all", used: 20L << 30, quota: 20L << 30)),
                                     listed: false, new HashSet<string> { "a" });
        Assert.Equal(new[] { RelayChangeKind.BackOnline }, Kinds(back));
    }

    [Fact]
    public void Additions_and_deletions_come_only_from_a_complete_list()
    {
        var before = Map(Exit("a"), Exit("b"));
        var stored = new HashSet<string> { "a", "b" };
        // exit_update : jamais de suppression (il ne porte qu'un agent)
        Assert.Empty(RelayChanges.Diff(before, Map(Exit("a")), listed: false, stored));
        // liste complète : « b » supprimé sur le portail, « c » créé
        var c = RelayChanges.Diff(before, Map(Exit("a"), Exit("c", "Nouveau")), listed: true, stored);
        Assert.Contains(c, x => x.Kind == RelayChangeKind.Removed && x.Id == "b" && x.Before!.Label == "Chez Paul");
        Assert.Contains(c, x => x.Kind == RelayChangeKind.Added && x.Id == "c");
        Assert.Equal(2, c.Count);
        // un agent dans la liste des proxys mais jamais vu cette session (supprimé app fermée) : supprimé aussi
        Assert.Equal(new[] { RelayChangeKind.Removed }, Kinds(RelayChanges.Diff(null, Map(), listed: true, new HashSet<string> { "z" })));
    }

    [Fact]
    public void Relay_share_keeps_a_stable_entry_across_renames_and_reloads()
    {
        var path = Path.Combine(_dir, "proxies.json");
        var store = new ProxyStore(path);
        var a = store.UpsertRelay("abc123", "Chez Paul");
        Assert.Equal("relay-abc123", a.Id);
        Assert.True(a.FromRelay);
        Assert.False(a.FromAdapter);

        var b = store.UpsertRelay("abc123", "Chez Paul (box)");
        Assert.Same(a, b);                              // même instance : les sélections des comptes tiennent
        Assert.Equal("Chez Paul (box)", b.Name);
        Assert.Single(store.Proxies);

        var reloaded = new ProxyStore(path).Get("relay-abc123");
        Assert.NotNull(reloaded);
        Assert.Equal("abc123", reloaded!.RelayExitId);
        Assert.Equal("Chez Paul (box)", reloaded.Name);
    }
}
