using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using GivreLauncher.Core.Proxy;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services.Transport.Socket;

/// <summary>
/// Ouverture TCP du mode Socket (serveur de connexion ET serveur de jeu) — MÊME politique de route que le relais MITM
/// (<c>HandshakeRelay.ConnectUpstreamAsync</c>) :
///  - compte routé par un ADAPTATEUR (proxy d'interface GivreClone, <c>EgressBindIp</c> = IP de la carte réseau) : la socket
///    est bindée DIRECTEMENT sur l'interface (BIND-DIRECT), sans le saut HTTP CONNECT loopback 127.0.0.1. Ce saut était le
///    SEUL chemin commun à tous les « fantômes » (perso resté en jeu après Déconnexion, login suivant rejeté) : la fermeture
///    ne parvenait pas au serveur de jeu, alors qu'un FIN en direct / via le relais MITM libère le perso en ~90 ms ;
///  - vrai proxy externe (SOCKS5 / HTTP CONNECT) : tunnel, 3 essais avec backoff (proxy 4G/résidentiel qui refuse un
///    CONNECT juste après une fermeture) ;
///  - aucun proxy : direct (IP réelle — l'appelant a vérifié que c'est un choix EXPLICITE « Sans proxy », jamais un repli).
/// Le flux rendu POSSÈDE sa socket (TcpClient.GetStream) : Dispose = Shutdown + fermeture.
/// </summary>
internal static class SocketDialer
{
    private const int DialAttemptTimeoutMs = 15_000;

    public static async Task<Stream> DialAsync(ProxySettings? proxy, byte[] ipv4, int port, uint logPid, string tag, CancellationToken ct)
    {
        if (proxy is { Kind: ProxyKind.HttpConnect, EgressBindIp: { } egress }
            && IPAddress.TryParse(egress, out var bindIp) && !IPAddress.IsLoopback(bindIp) && IsLocalIp(bindIp))
        {
            SocketLog.Info(logPid, $"{tag} BIND-DIRECT sur l'interface {bindIp} (sans saut proxy loopback, comme le relais MITM)");
            var tcp = new TcpClient(new IPEndPoint(bindIp, 0)) { NoDelay = true };
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
            bound.CancelAfter(DialAttemptTimeoutMs);
            try { await tcp.ConnectAsync(new IPAddress(ipv4), port, bound.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { tcp.Dispose(); throw new TimeoutException($"pas de réponse en {DialAttemptTimeoutMs / 1000} s (interface {bindIp})"); }
            catch { tcp.Dispose(); throw; }
            return tcp.GetStream();
        }
        if (proxy is { Kind: ProxyKind.HttpConnect, EgressBindIp: { } gone })
            // Interface disparue (4G coupée / IP changée) : on garde le proxy d'interface (il sort par la carte, jamais par
            // l'IP réelle) — seulement le saut loopback en plus.
            SocketLog.Info(logPid, $"{tag} TUNNEL via le proxy d'interface ({gone} n'est plus une IP locale de cette machine)");

        int attempts = proxy is null ? 1 : 3;
        Exception? last = null;
        for (int i = 1; i <= attempts; i++)
        {
            // Chaque essai est BORNÉ : un proxy qui accepte le TCP puis ne répond jamais au CONNECT/SOCKS laissait sinon le
            // compte « connexion… » indéfiniment (ni session, ni reconnexion). Délai dépassé = échec retentable, pas un Stop.
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(DialAttemptTimeoutMs);
            try
            {
                return proxy switch
                {
                    null => await DirectAsync(ipv4, port, attempt.Token).ConfigureAwait(false),
                    { Kind: ProxyKind.HttpConnect } => await HttpConnectForwarder.ConnectAsync(proxy, ipv4, port, attempt.Token).ConfigureAwait(false),
                    _ => await Socks5Forwarder.ConnectAsync(proxy, ipv4, port, attempt.Token).ConfigureAwait(false),
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                last = e is OperationCanceledException ? new TimeoutException($"pas de réponse en {DialAttemptTimeoutMs / 1000} s") : e;
                SocketLog.Info(logPid, $"{tag} ouverture ÉCHOUÉE (essai {i}/{attempts}) : {last.GetType().Name} : {last.Message}");
                if (i < attempts) await Task.Delay(250 * i, ct).ConfigureAwait(false);
            }
        }
        throw last ?? new IOException("ouverture TCP impossible");
    }

    /// <summary>Fermeture PROPRE et tracée : FIN explicite (Shutdown) puis libération. Rend ce qui a été fait, pour le log
    /// (le diagnostic des fantômes a besoin de savoir si le FIN est vraiment parti).</summary>
    public static string Close(Stream s)
    {
        string how = "fermée";
        if (s is NetworkStream { Socket: { } sock })
        {
            try { sock.Shutdown(SocketShutdown.Both); how = "FIN envoyé (Shutdown)"; }
            catch (Exception e) { how = $"Shutdown impossible ({e.GetType().Name}) — déjà fermée par le pair ?"; }
        }
        try { s.Dispose(); } catch { }
        return how;
    }

    private static async Task<Stream> DirectAsync(byte[] ipv4, int port, CancellationToken ct)
    {
        var tcp = new TcpClient { NoDelay = true };
        try { await tcp.ConnectAsync(new IPAddress(ipv4), port, ct).ConfigureAwait(false); }
        catch { tcp.Dispose(); throw; }
        return tcp.GetStream();
    }

    // Vrai si l'IP appartient à une interface LOCALE de cette machine (= notre proxy d'interface, pas un proxy distant).
    private static bool IsLocalIp(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.Equals(ip)) return true;
        }
        catch { /* énumération indisponible -> non local (tunnel) */ }
        return false;
    }
}
