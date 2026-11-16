using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Transport.Socket;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services.Relay;

/// <summary>
/// Pont SOCKS5 LOCAL (127.0.0.1, port libre) vers GivreRelay : chaque connexion SOCKS5 devient un flux du relais. Un
/// partage est donc un proxy comme un autre pour TOUT le code existant (authentification HAAPI via WebProxy, relais du mode
/// classique, mode Socket, test de proxy). Identifiant SOCKS5 = exit_id du partage, mot de passe = <see cref="Secret"/>.
///
/// Nom plutôt qu'IP : l'agent de l'ami filtre par nom d'hôte (scope « Dofus uniquement »). Une destination donnée en IP
/// (mode classique : c'est le client Dofus qui résout) est convertie en nom quand on le connaît (<see cref="RelayHostMap"/>).
/// </summary>
public sealed class RelaySocksBridge
{
    private readonly GivreRelayClient _client;
    private readonly RelayHostMap _hosts = new();
    private TcpListener? _listener;
    private readonly object _gate = new();

    public RelaySocksBridge(GivreRelayClient client) => _client = client;

    /// <summary>Mot de passe SOCKS5 du pont, tiré au hasard à chaque lancement de l'app.</summary>
    public string Secret { get; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

    /// <summary>Démarre le pont si besoin et renvoie son port local.</summary>
    public int EnsureStarted()
    {
        lock (_gate)
        {
            if (_listener is not null) return ((IPEndPoint)_listener.LocalEndpoint).Port;
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            _listener = l;
            _hosts.Start();
            _ = Task.Run(() => AcceptLoopAsync(l));
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            TransportLog.Line($"[relais] pont local prêt (port {port})");
            return port;
        }
    }

    public int Port { get { lock (_gate) return _listener is null ? 0 : ((IPEndPoint)_listener.LocalEndpoint).Port; } }

    private async Task AcceptLoopAsync(TcpListener l)
    {
        while (true)
        {
            TcpClient c;
            try { c = await l.AcceptTcpClientAsync().ConfigureAwait(false); }
            catch { return; }
            c.NoDelay = true;
            _ = Task.Run(() => HandleAsync(c));
        }
    }

    private async Task HandleAsync(TcpClient tcp)
    {
        using var _ = tcp;
        var s = tcp.GetStream();
        RelayStream? relay = null;
        try
        {
            using var hs = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            // 1) salutation : on exige l'authentification par identifiant (porte l'exit_id du partage).
            var g = await ReadExactly(s, 2, hs.Token);
            if (g[0] != 0x05) return;
            var methods = await ReadExactly(s, g[1], hs.Token);
            if (!methods.Contains((byte)0x02)) { await s.WriteAsync(new byte[] { 0x05, 0xFF }, hs.Token); return; }
            await s.WriteAsync(new byte[] { 0x05, 0x02 }, hs.Token);
            // 2) RFC 1929 : [1][ulen][user][plen][pass]
            var a = await ReadExactly(s, 2, hs.Token);
            string exitId = Encoding.UTF8.GetString(await ReadExactly(s, a[1], hs.Token));
            var pl = await ReadExactly(s, 1, hs.Token);
            string pass = Encoding.UTF8.GetString(await ReadExactly(s, pl[0], hs.Token));
            bool authOk = exitId.Length > 0 && pass == Secret;   // secret par lancement de l'app : aucun autre programme local n'emprunte le relais
            await s.WriteAsync(new byte[] { 0x01, (byte)(authOk ? 0x00 : 0x01) }, hs.Token);
            if (!authOk) return;
            // 3) requête CONNECT
            var h = await ReadExactly(s, 4, hs.Token);
            if (h[1] != 0x01) { await Reply(s, 0x07, hs.Token); return; }
            string host;
            IPAddress? ip = null;
            switch (h[3])
            {
                case 0x01: ip = new IPAddress(await ReadExactly(s, 4, hs.Token)); host = ip.ToString(); break;
                case 0x04: ip = new IPAddress(await ReadExactly(s, 16, hs.Token)); host = ip.ToString(); break;
                case 0x03:
                    var len = await ReadExactly(s, 1, hs.Token);
                    host = Encoding.ASCII.GetString(await ReadExactly(s, len[0], hs.Token));
                    break;
                default: await Reply(s, 0x08, hs.Token); return;
            }
            var pb = await ReadExactly(s, 2, hs.Token);
            int port = (pb[0] << 8) | pb[1];
            string label = _client.GetExit(exitId)?.Label ?? exitId;

            // 4) une IP -> le NOM (l'agent filtre par nom). Ordre : table IP->nom ; HTTPS : nom lu dans la poignée TLS (SNI)
            //    après une réponse SOCKS anticipée ; sinon on envoie l'IP (l'agent la vérifie contre ses propres résolutions).
            byte[]? first = null;
            bool replied = false;
            if (ip is not null)
            {
                if (_hosts.NameFor(ip) is null && port != 443) await _hosts.RefreshAsync(TimeSpan.FromSeconds(2));
                if (_hosts.NameFor(ip) is { } known) host = known;
                else if (port == 443)
                {
                    await Reply(s, 0x00, hs.Token);
                    replied = true;
                    first = await ReadFirstRecordAsync(s, hs.Token);
                    if (first is null) return;
                    if (TlsSni.Parse(first) is { } sni) host = sni;
                }
            }
            bool sniff = port == 5555 && (host == RelayHostMap.ConnectionHost || ip is not null && host == ip.ToString());

            var (stream, reason) = await _client.OpenStreamAsync(exitId, host, port, hs.Token);
            // IP inconnue refusée en 5555 : c'est le serveur de connexion sur une IP que le DNS ne donne pas toujours (il en
            // tourne plusieurs ; un serveur de jeu, lui, est dans les résolutions de l'agent) -> nouvelle tentative par son nom.
            if (stream is null && reason == "scope_denied" && port == 5555 && ip is not null && host == ip.ToString())
            {
                TransportLog.Line($"[relais] {label} : {host}:{port} refusé par IP, nouvel essai via {RelayHostMap.ConnectionHost}");
                host = RelayHostMap.ConnectionHost;
                (stream, reason) = await _client.OpenStreamAsync(exitId, host, port, hs.Token);
            }
            if (stream is null)
            {
                TransportLog.Line($"[relais] {label} : flux refusé vers {host}:{port} — {GivreRelayClient.DescribeReason(reason)}");
                // Codes SOCKS5 : 0x02 refusé par la règle, 0x04 hôte injoignable, 0x05 connexion refusée, 0x01 échec général.
                byte rep = reason switch
                {
                    "scope_denied" => 0x02,
                    "unreachable" or "timeout" or "dns_failed" => 0x04,
                    "connect_failed" => 0x05,
                    _ => 0x01,
                };
                if (!replied) await Reply(s, rep, hs.Token);
                return;
            }
            relay = stream;
            if (!replied) await Reply(s, 0x00, hs.Token);
            if (first is not null) await relay.WriteAsync(first, hs.Token);

            // 5) pompe bidirectionnelle. Flux du serveur de connexion : on y lit le nom du serveur de jeu choisi.
            var up = s.CopyToAsync(relay, 16 * 1024);
            var down = PumpDownAsync(relay, s, sniff);
            await Task.WhenAny(up, down).ConfigureAwait(false);
            // Coupure imposée par le relais (quota atteint, agent parti…) : motif lisible dans le journal de transport.
            if (relay.CloseReason is { Length: > 0 } why)
                TransportLog.Line($"[relais] {label} : flux vers {host}:{port} fermé — {GivreRelayClient.DescribeReason(why)}");
        }
        catch { /* client ou flux coupé */ }
        finally
        {
            relay?.Dispose();
        }
    }

    // Serveur -> client. Sur le flux du serveur de connexion, la réponse de sélection de serveur donne le NOM du serveur de
    // jeu : on le résout AVANT de remettre la réponse au client, pour que sa connexion de jeu (par IP) trouve déjà son nom.
    private async Task PumpDownAsync(RelayStream from, NetworkStream to, bool sniff)
    {
        var buf = new byte[16 * 1024];
        var acc = sniff ? new List<byte>() : null;
        while (true)
        {
            int n = await from.ReadAsync(buf).ConfigureAwait(false);
            if (n <= 0) return;
            if (acc is not null)
            {
                acc.AddRange(buf.AsSpan(0, n).ToArray());
                while (TryDeframe(acc, out var body))
                {
                    ConnDecoded? d = null;
                    try { d = ConnectionMessages.Decode(body); } catch { /* trame illisible : rien à apprendre */ }
                    if (d is { Type: ConnDecoded.Kind.SelectServerSuccess } && !string.IsNullOrEmpty(d.GameHost))
                        await _hosts.LearnAsync(d.GameHost, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                if (acc.Count > 256 * 1024) acc = null;   // borne : seul le début de la session nous intéresse
            }
            await to.WriteAsync(buf.AsMemory(0, n)).ConfigureAwait(false);
        }
    }

    // Premier enregistrement envoyé par le client après la réponse SOCKS (poignée TLS en HTTPS). Rend ce qui est arrivé
    // en 5 s au plus (vide si le client attend que le serveur parle), null si le client a fermé.
    private static async Task<byte[]?> ReadFirstRecordAsync(Stream s, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var acc = new List<byte>(2048);
        var buf = new byte[4096];
        try
        {
            while (acc.Count < 16 * 1024)
            {
                int n = await s.ReadAsync(buf, cts.Token).ConfigureAwait(false);
                if (n <= 0) return acc.Count > 0 ? acc.ToArray() : null;
                acc.AddRange(buf.AsSpan(0, n).ToArray());
                // TLS : [0x16][version 2][longueur 2] -> on attend l'enregistrement complet (ClientHello parfois en 2 segments).
                if (acc[0] != 0x16 || acc.Count >= 5 && acc.Count >= 5 + ((acc[3] << 8) | acc[4])) break;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* rien d'autre n'arrive : on part avec ce qu'on a */ }
        return acc.ToArray();
    }

    private static bool TryDeframe(List<byte> acc, out byte[] body)
    {
        body = Array.Empty<byte>();
        int len = 0, shift = 0, i = 0;
        while (true)
        {
            if (i >= acc.Count) return false;
            byte b = acc[i++];
            len |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift > 28) { acc.Clear(); return false; }
        }
        if (acc.Count - i < len) return false;
        body = acc.GetRange(i, len).ToArray();
        acc.RemoveRange(0, i + len);
        return true;
    }

    private static Task Reply(Stream s, byte rep, CancellationToken ct)
        => s.WriteAsync(new byte[] { 0x05, rep, 0x00, 0x01, 0, 0, 0, 0, 0, 0 }, ct).AsTask();

    private static async Task<byte[]> ReadExactly(Stream s, int n, CancellationToken ct)
    {
        var b = new byte[n];
        if (n > 0) await s.ReadExactlyAsync(b, ct).ConfigureAwait(false);
        return b;
    }
}

/// <summary>IP -&gt; nom d'hôte Ankama, pour transmettre un NOM à l'agent même quand on n'a reçu qu'une IP : serveur de
/// connexion (résolu au démarrage puis toutes les 5 min, toutes ses adresses) et serveurs de jeu appris dans la réponse
/// de sélection de serveur. Une IP inconnue reste une IP (l'agent la vérifie alors contre ses propres résolutions).</summary>
public sealed class RelayHostMap
{
    public const string ConnectionHost = "dofus2-co-production.ankama-games.com";
    private static readonly TimeSpan Keep = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, (string Host, DateTime Until)> _byIp = new();
    private readonly ConcurrentDictionary<string, byte> _names = new(StringComparer.OrdinalIgnoreCase) { [ConnectionHost] = 0 };
    private int _started;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                foreach (var n in _names.Keys.ToList()) await ResolveAsync(n).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMinutes(5)).ConfigureAwait(false);
            }
        });
    }

    /// <summary>Nom du serveur de jeu lu dans la réponse de sélection de serveur : retenu et résolu (attente bornée).</summary>
    public async Task LearnAsync(string host, TimeSpan wait)
    {
        if (!host.EndsWith(".ankama-games.com", StringComparison.OrdinalIgnoreCase) && !host.EndsWith(".ankama.com", StringComparison.OrdinalIgnoreCase)) return;
        if (_names.TryAdd(host, 0)) TransportLog.Line($"[relais] serveur de jeu appris : {host}");
        await Task.WhenAny(ResolveAsync(host), Task.Delay(wait)).ConfigureAwait(false);
    }

    /// <summary>Re-résout tous les noms connus (IP inconnue reçue : le serveur de connexion a pu changer d'adresse).</summary>
    public Task RefreshAsync(TimeSpan wait)
        => Task.WhenAny(Task.WhenAll(_names.Keys.ToList().Select(ResolveAsync)), Task.Delay(wait));

    public string? NameFor(IPAddress ip)
        => _byIp.TryGetValue(ip.ToString(), out var e) && e.Until > DateTime.UtcNow ? e.Host : null;

    private async Task ResolveAsync(string host)
    {
        try
        {
            foreach (var a in await Dns.GetHostAddressesAsync(host).ConfigureAwait(false))
                _byIp[a.ToString()] = (host, DateTime.UtcNow + Keep);
        }
        catch { /* DNS indisponible : on réessaiera au prochain cycle */ }
    }
}
