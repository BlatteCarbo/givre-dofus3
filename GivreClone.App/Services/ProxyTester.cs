using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using GivreLauncher.Core.Proxy;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

/// <summary>
/// Teste un proxy : (1) IP de sortie via ipify (affichage + tag fenêtre), (2) ping REPRÉSENTATIF = temps
/// d'ouverture d'un tunnel proxifié vers le serveur de connexion Dofus. On mesure le même chemin que le
/// ping en jeu (proxy -> serveur jeu), pas un GET HTTP vers un serveur de test lointain (qui sur-estimait).
/// </summary>
public static class ProxyTester
{
    // Serveur de connexion Dofus 3 (écoute en 5555 ET 443 ; on ping le 443 : universellement autorisé par les proxys).
    private const string GameHost = "dofus2-co-production.ankama-games.com";
    private const int GamePingPort = 443;

    public static async Task<(bool ok, string ip, long ms)> TestAsync(ProxySettings? proxy, CancellationToken ct = default)
    {
        // Partage GivreRelay : ipify serait refusé (« Dofus uniquement ») -> IP publique de l'ami telle que le relais la
        // voit, puis le même ping jeu à travers le partage.
        if (Relay.GivreRelay.ExitFor(proxy) is { } exit)
        {
            if (string.IsNullOrWhiteSpace(exit.PublicIp)) return (false, Relay.GivreRelay.Unavailable(exit.Id) ?? "?", 0);
            return (true, exit.PublicIp, await PingGameAsync(proxy, ct).ConfigureAwait(false));
        }

        // 1) IP de sortie (non chronométrée : ce n'est pas le ping)
        string ip;
        try
        {
            var handler = new HttpClientHandler();
            if (proxy is not null) { handler.Proxy = proxy.ToWebProxy(); handler.UseProxy = true; }
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(9) };
            ip = (await http.GetStringAsync("https://api.ipify.org", ct).ConfigureAwait(false)).Trim();
        }
        catch (Exception ex)
        {
            return (false, Describe(ex), 0);   // vraie cause (chaîne inner) -> échec franc DIAGNOSTIQUABLE côté Proxys
        }

        // 2) Ping ~ jeu : ouverture d'un tunnel vers le serveur de connexion (proxy -> serveur), 1 aller-retour.
        var ms = await PingGameAsync(proxy, ct).ConfigureAwait(false);
        return (true, ip, ms);
    }

    private static async Task<long> PingGameAsync(ProxySettings? proxy, CancellationToken ct)
    {
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(GameHost, ct).ConfigureAwait(false);
            var a = addrs.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
            if (a is null) return 0;
            var ipv4 = a.GetAddressBytes();

            var sw = Stopwatch.StartNew();
            Stream tunnel = proxy switch
            {
                null => await DirectAsync(ipv4, GamePingPort, ct).ConfigureAwait(false),
                { Kind: ProxyKind.HttpConnect } => await HttpConnectForwarder.ConnectAsync(proxy, ipv4, GamePingPort, ct).ConfigureAwait(false),
                _ => await Socks5Forwarder.ConnectAsync(proxy, ipv4, GamePingPort, ct).ConfigureAwait(false),
            };
            sw.Stop();
            tunnel.Dispose();
            return sw.ElapsedMilliseconds;
        }
        catch
        {
            return 0;   // le ping jeu peut échouer sans invalider l'IP de sortie
        }
    }

    private static async Task<Stream> DirectAsync(byte[] ipv4, int port, CancellationToken ct)
    {
        var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(new IPAddress(ipv4), port, ct).ConfigureAwait(false);
        return tcp.GetStream();
    }

    // Aplati la chaîne d'exceptions : « The SSL connection could not be established, see inner exception » ne dit
    // rien sans son inner (certificat/interception TLS, refus/timeout du proxy, 407 auth proxy…). On renvoie la vraie
    // cause à la ligne Proxys au lieu du message générique tronqué.
    private static string Describe(Exception ex)
    {
        var parts = new System.Collections.Generic.List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException) parts.Add(e.Message);
        return string.Join(" → ", parts);
    }
}
