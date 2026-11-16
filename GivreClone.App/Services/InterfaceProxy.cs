using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

/// <summary>
/// Proxy HTTP (CONNECT + HTTP simple) bindé sur l'IP d'une interface : tout le trafic sortant
/// est envoyé depuis cette interface (donc son IP publique). Écoute sur un port libre attribué
/// par le système. Un proxy = une interface = une IP de sortie.
/// </summary>
public sealed class InterfaceProxy
{
    private readonly IPAddress _bindIp;
    private readonly Func<string>? _name;   // libellé pour les logs (label/nom courant de l'interface)
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public int Port { get; private set; }

    private string Src => _name?.Invoke() ?? _bindIp.ToString();

    public InterfaceProxy(IPAddress bindIp, Func<string>? name = null)
    {
        _bindIp = bindIp;
        _name = name;
    }

    /// <summary>Démarre l'écoute et renvoie le port local. port=0 -> port libre attribué par l'OS ; sinon port fixe.</summary>
    public int Start(int port = 0)
    {
        _cts = new CancellationTokenSource();
        // AUDIT M6 : on ECOUTE en loopback (127.0.0.1), PAS sur l'IP de l'interface. Le seul client légitime est le
        // navigateur WebAuth local (même machine) ; écouter sur l'IP LAN faisait de nous un proxy HTTP CONNECT OUVERT
        // joignable par tout le réseau (relais/SSRF + flood distant). L'EGRESS reste forcé via `_bindIp` (outbound.Bind
        // plus bas) : le routage par interface est préservé, seule l'exposition d'écoute est fermée.
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoopAsync(_cts.Token);
        AdapterLog.Info(Src, $"proxy actif sur 127.0.0.1:{Port} (egress via {_bindIp})");
        return Port;
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { /* déjà fermé */ }
        AdapterLog.Info(Src, "proxy arrêté");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch { break; }
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                var clientStream = client.GetStream();
                var buffer = new byte[8192];
                var read = await clientStream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0) return;

                var requestText = Encoding.ASCII.GetString(buffer, 0, read);
                var firstLine = requestText.Split('\n')[0].Trim();
                var parts = firstLine.Split(' ');
                if (parts.Length < 2) return;

                var method = parts[0];
                var target = parts[1];
                string host;
                int port;
                byte[]? initial = null;    // requete HTTP simple : a renvoyer telle quelle vers la cible
                byte[]? pipelined = null;   // octets deja recus APRES l'en-tete CONNECT (1re trame pipelinee du client)

                if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
                {
                    var hp = target.Split(':');
                    host = hp[0];
                    port = hp.Length > 1 && int.TryParse(hp[1], out var p) ? p : 443;
                    // Un client peut pipeliner sa 1re trame juste derriere "CONNECT ...\r\n\r\n" dans le meme
                    // segment TCP. Cette 1re lecture les a alors deja consommes : il faut les relayer vers la
                    // cible, sinon la poignee de main arrive tronquee et le serveur ferme (down=0), de maniere
                    // intermittente selon la segmentation TCP. On repere la fin d'en-tete et on garde le reste.
                    int headerEnd = HeaderEnd(buffer, read);
                    if (headerEnd >= 0 && headerEnd < read)
                        pipelined = buffer[headerEnd..read];
                }
                else
                {
                    if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
                    {
                        var m = Regex.Match(requestText, @"^Host:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                        if (!m.Success) return;
                        if (!Uri.TryCreate($"http://{m.Groups[1].Value.Trim()}{target}", UriKind.Absolute, out uri)) return;
                    }
                    host = uri.Host;
                    port = uri.Port;
                    initial = buffer[..read];
                }

                AdapterLog.Info(Src, $"{method} {host}:{port}");
                using var outbound = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                outbound.Bind(new IPEndPoint(_bindIp, 0));   // sortie forcée par cette interface
                try { await outbound.ConnectAsync(host, port, ct).ConfigureAwait(false); }
                catch (Exception ex) { AdapterLog.Warn(Src, $"{host}:{port} injoignable : {ex.Message}"); return; }
                await using var serverStream = new NetworkStream(outbound, ownsSocket: false);

                if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
                {
                    await clientStream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n"), ct).ConfigureAwait(false);
                    if (pipelined is not null)   // relaye la 1re trame pipelinee AVANT de lancer le splice
                        await serverStream.WriteAsync(pipelined, ct).ConfigureAwait(false);
                }
                else if (initial is not null)
                    await serverStream.WriteAsync(initial, ct).ConfigureAwait(false);

                var up = Pipe(clientStream, serverStream, ct);
                var down = Pipe(serverStream, clientStream, ct);
                await Task.WhenAny(up, down).ConfigureAwait(false);
            }
        }
        catch { /* client/serveur fermé */ }
    }

    // Renvoie l'offset juste APRES le \r\n\r\n de fin d'en-tete dans buf[0..len], ou -1 si absent.
    private static int HeaderEnd(byte[] buf, int len)
    {
        for (int i = 0; i + 3 < len; i++)
            if (buf[i] == (byte)'\r' && buf[i + 1] == (byte)'\n' && buf[i + 2] == (byte)'\r' && buf[i + 3] == (byte)'\n')
                return i + 4;
        return -1;
    }

    private static async Task Pipe(Stream from, Stream to, CancellationToken ct)
    {
        try { await from.CopyToAsync(to, 81920, ct).ConfigureAwait(false); }
        catch { /* fin de flux */ }
    }
}
