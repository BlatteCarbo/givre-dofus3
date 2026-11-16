using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Transport;
using GivreClone.Engine;
using GivreLauncher.Core.Proxy;

namespace GivreClone.App.Services.LocalLaunch;

/// <summary>
/// Relais MITM de l'édition locale. Le hook natif (GivreKama.dll) redirige chaque connect() de Dofus.exe vers CE relais et envoie d'abord un
/// en-tête de 26 octets : adresse d'origine (16 octets, IPv6 ou IPv4 mappé), longueur de sockaddr (int), port (ushort), PID (uint) — disposition
/// vérifiée par le banc d'essai tools/MZP23HookHarness. Le relais lit cet en-tête, se connecte à la VRAIE destination, copie les octets dans les deux
/// sens (alimente le décodeur par <see cref="OnData"/>) et permet l'injection de trames vers le serveur.
/// Garde-fous : seul un client lancé PAR l'application est admis (PID connu ET propriétaire vérifié) ; une destination en boucle locale est refusée
/// (le relais ne se rappelle pas lui-même) ; un compte avec proxy est REFUSÉ (le relais n'en gère pas : jamais de contournement silencieux).
/// </summary>
public sealed class LocalHandshakeRelay : IMitmRelay, IDisposable
{
    public const int HandshakeLength = 26;

    public sealed record Handshake(IPAddress Address, int AddrLen, ushort Port, uint ProcessId);

    /// <summary>
    /// Source-issued local TCP link identity. It proves only that these bytes
    /// were observed on a particular currently owned relay tunnel. It does NOT
    /// prove a server ACK, a cast, or any effect of a locally sent request.
    /// </summary>
    public readonly record struct GameRelayLink(Guid RelayInstanceId, uint OwnerPid,
        long RunEpoch, long TunnelId);

    /// <summary>Décode l'en-tête du hook ; null si invalide (longueur, adresse non IPv4-mappée/IPv6 exploitable, port ou PID nuls).</summary>
    public static Handshake? ParseHandshake(ReadOnlySpan<byte> b)
    {
        if (b.Length != HandshakeLength) return null;
        var addr16 = b[..16];
        int addrLen = BitConverter.ToInt32(b[16..20]);
        ushort port = BitConverter.ToUInt16(b[20..22]);
        uint pid = BitConverter.ToUInt32(b[22..26]);
        if (port == 0 || pid == 0 || addrLen is not (16 or 28)) return null;
        bool mapped = addr16[10] == 0xFF && addr16[11] == 0xFF;
        for (int i = 0; i < 10 && mapped; i++) if (addr16[i] != 0) mapped = false;
        var ip = mapped ? new IPAddress(addr16[12..16]) : new IPAddress(addr16);
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return null;
        return new Handshake(ip, addrLen, port, pid);
    }

    private readonly Func<uint, bool> _knownPid;
    private readonly Func<uint, bool> _isOwned;
    private readonly Func<uint, ProxySettings?> _proxyForPid;
    private readonly Func<uint, ProxySettings?>? _gameProxyForPid;
    private readonly bool _allowLoopbackUpstream;
    private readonly Action<string> _log;
    private readonly ConcurrentDictionary<uint, Tunnel> _gameTunnel = new();   // dernière connexion « jeu » par PID (cible de l'injection)
    private MitmRelayAdmission _admission = new();                             // reset across Stop/Start, never reuse old PID admission
    private readonly object _admitGate = new();                                // Commit et CloseGameClient : ne pas fermer la socket de jeu qui vient d'être installée
    private TcpListener? _listener;
    private CancellationTokenSource? _shutdown;
    private int _port;
    private long _runEpoch;                                                    // real listener lifecycle, not decoder/PID epoch
    private readonly Guid _relayInstanceId = Guid.NewGuid();
    private long _lastTunnelId;

    private bool IsCurrentRun(long runEpoch) =>
        runEpoch > 0 && Volatile.Read(ref _runEpoch) == runEpoch;

    private bool IsCurrentTunnel(Tunnel tunnel) =>
        IsCurrentRun(tunnel.RunEpoch) &&
        (tunnel.ConnectionServer ||
         (_gameTunnel.TryGetValue(tunnel.Pid, out var current) && ReferenceEquals(current, tunnel)));

    /// <summary>A strictly current source-issued game tunnel, never an ACK.</summary>
    public bool IsCurrentGameLink(GameRelayLink link)
    {
        if (link.RelayInstanceId != _relayInstanceId || link.OwnerPid == 0
            || link.RunEpoch <= 0 || link.TunnelId <= 0
            || !IsCurrentRun(link.RunEpoch)) return false;
        return _gameTunnel.TryGetValue(link.OwnerPid, out var current)
            && current.Link == link && IsCurrentTunnel(current)
            && _knownPid(link.OwnerPid) && _isOwned(link.OwnerPid);
    }

    /// <summary>
    /// When set, this source-stamped callback replaces legacy OnData ONLY for
    /// game-tunnel bytes (both directions). Client-to-server bytes are emitted
    /// only after the exact post-filter bytes have been written to the local
    /// upstream stream; server-to-client bytes retain decode-before-forward
    /// ordering. Neither direction proves remote receipt or a gameplay ACK.
    /// Login/server-connection bytes still use OnData. Consumers must validate
    /// IsCurrentGameLink at use and must not merge another source by PID.
    /// </summary>
    public Action<GameRelayLink, long, bool, byte[]>? OnGameData { get; set; }

    /// <summary>
    /// Exact link revocation, including stale link closure. The bool is whether
    /// this was the current game tunnel at closure, not a remote server ACK.
    /// Callback consumers must revoke only this exact link identity.
    /// </summary>
    public Action<GameRelayLink, bool>? OnGameLinkClosed { get; set; }

    private void PublishData(Tunnel tunnel, bool clientToServer, byte[] data)
    {
        if (!IsCurrentTunnel(tunnel)) return;
        if (!tunnel.ConnectionServer && OnGameData is { } gameObserver)
        {
            long sequence = tunnel.NextChunkSequence(clientToServer);
            if (sequence <= 0 || !IsCurrentGameLink(tunnel.Link)) return;
            gameObserver(tunnel.Link, sequence, clientToServer, data);
        }
        else
            OnData?.Invoke(tunnel.Pid, clientToServer, data);
    }

    public LocalHandshakeRelay(int port, Func<uint, bool> knownPid, Func<uint, bool> isOwned, Func<uint, ProxySettings?> proxyForPid,
                               Func<uint, ProxySettings?>? gameProxyForPid = null, bool allowLoopbackUpstream = false, Action<string>? log = null)
        => (_port, _knownPid, _isOwned, _proxyForPid, _gameProxyForPid, _allowLoopbackUpstream, _log) =
           (port, knownPid, isOwned, proxyForPid, gameProxyForPid, allowLoopbackUpstream, log ?? (_ => { }));

    /// <summary>Port d'écoute effectif (connu après <see cref="Start"/> si 0 a été demandé).</summary>
    public int Port => _port;

    public Action<uint, bool, byte[]>? OnData { get; set; }
    [ThreadStatic] private static uint _observingBeforeForward;
    internal static bool ObservingBeforeForward(uint pid) => pid != 0 && _observingBeforeForward == pid;
    public Action<uint, bool, bool>? OnClosed { get; set; }
    /// <summary>Décideur de filtrage des trames client->serveur de la connexion de JEU (Turbo : accusés déjà envoyés par le bot).</summary>
    public Func<uint, byte[], bool>? DropClientFrame { get; set; }
    /// <summary>Compatibility setting for callers; game ingress is always decoded before forwarding.
    /// This also preserves Turbo acknowledgement ordering outside tactical transitions.</summary>
    public Func<uint, bool>? DecodeBeforeForward { get; set; }
    public Action<uint, string, bool, bool>? OnGameTunnelOpened { get; set; }

    public void Start()
    {
        lock (_admitGate)
        {
            if (_listener is not null) throw new InvalidOperationException("Relais déjà démarré.");
            if (_port is 5657 or 5658 or 26666) throw new InvalidOperationException("Port réservé à un GivreClone d'origine : choisissez un port libre.");
            if (_runEpoch == long.MaxValue) throw new InvalidOperationException("Époque du relais épuisée.");
            var shutdown = new CancellationTokenSource();
            var l = new TcpListener(IPAddress.Loopback, _port);
            try { l.Start(16); }
            catch { shutdown.Dispose(); throw; }
            _listener = l;
            _shutdown = shutdown;
            _port = ((IPEndPoint)l.LocalEndpoint).Port;
            long runEpoch = _runEpoch + 1;
            Volatile.Write(ref _runEpoch, runEpoch);
            _ = AcceptAsync(l, runEpoch, shutdown.Token);
        }
    }

    private async Task AcceptAsync(TcpListener l, long runEpoch, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await l.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (Exception) when (ct.IsCancellationRequested) { return; }
            catch (SocketException) { return; }
            if (!IsCurrentRun(runEpoch)) { c.Dispose(); return; }
            _ = ServeAsync(c, runEpoch, ct);
        }
    }

    private async Task ServeAsync(TcpClient client, long runEpoch, CancellationToken ct)
    {
        using (client)
        {
            if (!IsCurrentRun(runEpoch) || ct.IsCancellationRequested) return;
            client.NoDelay = true;
            var cs = client.GetStream();
            var hs = new byte[HandshakeLength];
            try
            {
                using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
                to.CancelAfter(TimeSpan.FromSeconds(5));
                await cs.ReadExactlyAsync(hs, to.Token).ConfigureAwait(false);
            }
            catch { _log("[relais] en-tête du hook absent ou incomplet : connexion fermée"); return; }

            var h = ParseHandshake(hs);
            if (h is null) { _log("[relais] en-tête du hook invalide : connexion fermée"); return; }
            bool known = _knownPid(h.ProcessId);
            bool owned = known && _isOwned(h.ProcessId);
            if (!_admission.WouldAdmit(h.ProcessId, known, owned)) { _log($"[relais] pid {h.ProcessId} non lancé par l'application : refusé"); return; }
            uint preview = _admission.ResolveOwner(h.ProcessId, known);
            if (!_allowLoopbackUpstream && IPAddress.IsLoopback(h.Address)) { _log("[relais] destination en boucle locale refusée"); return; }
            if (h.Port == _port && IPAddress.IsLoopback(h.Address)) return;
            try
            {
                if (_proxyForPid(preview) is not null || _gameProxyForPid?.Invoke(preview) is not null)
                { _log($"[relais] pid {preview} : un proxy est configuré, non géré par le relais local : refusé (pas de repli direct)"); return; }
            }
            catch { return; }

            using var upstream = new TcpClient { NoDelay = true };
            try
            {
                using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
                to.CancelAfter(TimeSpan.FromSeconds(10));
                await upstream.ConnectAsync(new IPEndPoint(h.Address, h.Port), to.Token).ConfigureAwait(false);
            }
            catch (Exception e) { _log($"[relais] pid {preview} : connexion amont impossible ({e.GetType().Name})"); return; }

            // Commit seulement une fois l'amont ouvert : un refus ou un échec de connect ne consomme pas le saut de relogin.
            MitmRelayAdmission.Decision decision;
            Tunnel? tunnel;
            lock (_admitGate)
            {
                // A completed upstream ConnectAsync must not install a link
                // into a later Stop/Start epoch. Recheck actual ownership too:
                // the handshake PID or adopted account may have exited during
                // the async connect, or a PID may have been reused.
                bool knownNow = _knownPid(h.ProcessId);
                bool ownedNow = knownNow && _isOwned(h.ProcessId);
                uint ownerNow = _admission.ResolveOwner(h.ProcessId, knownNow);
                bool ownerStillOwned = _knownPid(ownerNow) && _isOwned(ownerNow);
                decision = IsCurrentRun(runEpoch) && !ct.IsCancellationRequested &&
                    knownNow == known && ownedNow == owned && ownerStillOwned
                    ? _admission.Commit(h.ProcessId, knownNow, ownedNow)
                    : new MitmRelayAdmission.Decision(h.ProcessId, false, false, false);
                if (!decision.Admit) tunnel = null;
                else if (decision.ConnectionServer || _lastTunnelId < long.MaxValue)
                {
                    long tunnelId = decision.ConnectionServer ? 0 : ++_lastTunnelId;
                    tunnel = new Tunnel(decision.OwnerPid, client, upstream, decision.ConnectionServer,
                        runEpoch, new GameRelayLink(_relayInstanceId, decision.OwnerPid, runEpoch, tunnelId));
                    if (!decision.ConnectionServer) _gameTunnel[decision.OwnerPid] = tunnel;
                }
                else tunnel = null; // Link identity exhausted: never wrap.
            }
            if (tunnel is null) { _log($"[relais] pid {h.ProcessId} non lancé par l'application : refusé"); return; }
            uint owner = decision.OwnerPid;
            if (decision.Adopted && h.ProcessId != owner)
                _log($"[relais] pid {owner} : reconnexion adoptée (socket pid {h.ProcessId})");
            if (!IsCurrentRun(runEpoch)) return;
            try { OnGameTunnelOpened?.Invoke(owner, $"{h.Address}:{h.Port}", decision.ConnectionServer, false); } catch { }
            _log($"[relais] pid {owner} : {(decision.ConnectionServer ? "serveur de connexion" : "serveur de jeu")} {h.Address}:{h.Port}");
            try
            {
                var c2s = PumpAsync(tunnel, cs, upstream.GetStream(), true, ct);
                var s2c = PumpAsync(tunnel, upstream.GetStream(), cs, false, ct);
                await Task.WhenAny(c2s, s2c).ConfigureAwait(false);
            }
            finally
            {
                bool wasCurrent = IsCurrentRun(runEpoch) &&
                    _gameTunnel.TryGetValue(owner, out var cur) && ReferenceEquals(cur, tunnel);
                if (wasCurrent) _gameTunnel.TryRemove(new KeyValuePair<uint, Tunnel>(owner, tunnel));
                tunnel.Dispose();
                if (!tunnel.ConnectionServer)
                    try { OnGameLinkClosed?.Invoke(tunnel.Link, wasCurrent); } catch { }
                // A late EOF from a previous relay instance cannot close the
                // bot session that is now using the same PID in a new run.
                if (IsCurrentRun(runEpoch))
                    try { OnClosed?.Invoke(owner, wasCurrent, tunnel.ServerClosedFirst); } catch { }
            }
        }
    }

    private async Task PumpAsync(Tunnel t, Stream src, Stream dst, bool clientToServer, CancellationToken ct)
    {
        var buf = new byte[16 * 1024];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await src.ReadAsync(buf, ct).ConfigureAwait(false);
                if (n == 0 || !_knownPid(t.Pid) || !IsCurrentTunnel(t))
                { t.NoteFirstClose(!clientToServer); break; }
                if (clientToServer)
                {
                    var drop = DropClientFrame;
                    byte[] forwarded;
                    if (drop is not null && !t.ConnectionServer && t.Gate is { } gate)
                    {
                        // Process may retain an incomplete frame or remove
                        // already-acknowledged frames. Its result is the ONLY
                        // byte sequence this pump is allowed to forward or
                        // publish; the raw socket read is not TX evidence.
                        forwarded = gate.Process(t.Pid, buf[..n], drop);
                    }
                    else
                        forwarded = buf[..n];

                    // No complete forwarded bytes yet (split frame), or all
                    // complete frames were dropped. Neither case is decoder
                    // input, a transport observation, nor a server ACK.
                    if (forwarded.Length == 0) continue;

                    lock (t.WriteGate)
                    {
                        // DropClientFrame is an external callback and may
                        // synchronously Stop/replace the relay. Revalidate the
                        // exact game source after filtering and immediately
                        // before the local upstream write.
                        bool current = t.ConnectionServer
                            ? _knownPid(t.Pid) && IsCurrentTunnel(t)
                            : IsCurrentGameLink(t.Link);
                        if (!current) break;
                        dst.Write(forwarded, 0, forwarded.Length);
                    }

                    // Publish only after the corresponding upstream write
                    // completed, and publish the exact forwarded bytes (which
                    // may combine split reads or exclude dropped frames).
                    try { PublishData(t, true, forwarded); }
                    catch { /* un observateur ne casse jamais le flux */ }
                    continue;
                }
                else
                {
                    // All game ingress must reach MapCurrent observation before its
                    // completing bytes reach Dofus. decode_ms includes synchronous
                    // subscribers and atomic admission I/O; it is not a latency bound.
                    bool decodeFirst = !t.ConnectionServer;
                    if (decodeFirst)
                    {
                        long receivedAt = Environment.TickCount64;
                        var decodeWatch = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            _observingBeforeForward = t.Pid;
                            PublishData(t, false, buf[..n]);
                        }
                        catch { /* un observateur ne casse jamais le flux */ }
                        finally { _observingBeforeForward = 0; }
                        double decodeMs = decodeWatch.Elapsed.TotalMilliseconds;
                        await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                        _log($"[turbo-relay] pid={t.Pid} received_monotonic={receivedAt} bytes={n} decode_ms={decodeMs:F2} forward_ms={decodeWatch.Elapsed.TotalMilliseconds - decodeMs:F2}");
                        continue;
                    }
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                }
                try { PublishData(t, false, buf[..n]); }
                catch { /* un observateur ne casse jamais le flux */ }
            }
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { t.NoteFirstClose(!clientToServer); }
        finally { try { t.Client.Close(); t.Upstream.Close(); } catch { } }
    }

    /// <summary>Écrit une trame (déjà préfixée) vers le SERVEUR de jeu du client ; faux si aucune connexion de jeu active. Jamais renvoyée à <see cref="OnData"/>.</summary>
    public bool Inject(uint pid, byte[] framedToServer)
    {
        if (framedToServer is not { Length: > 0 and <= 1_048_576 } || !_knownPid(pid) || !_gameTunnel.TryGetValue(pid, out var t)) return false;
        try { lock (t.WriteGate) { if (!IsCurrentTunnel(t)) return false; t.Upstream.GetStream().Write(framedToServer); return true; } }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) { return false; }
    }

    /// <summary>Prochain socket de ce compte = serveur de connexion du relogin, même si le pid a déjà joué.</summary>
    public void ArmRelogin(uint pid) => _admission.Arm(pid);

    public void DisarmRelogin(uint pid) => _admission.Disarm(pid);

    public void NoteReloginLinkClosed(uint pid) => _admission.NoteLinkClosed(pid);

    public bool GameSocketReplaced(uint pid) => _admission.GameSocketReplaced(pid);

    /// <summary>Ferme la socket de jeu encore en place. Ne fait rien si la socket de jeu du relogin est déjà installée.</summary>
    public bool CloseGameClient(uint pid)
    {
        Tunnel? tunnel;
        lock (_admitGate)
        {
            if (_admission.GameSocketReplaced(pid)) return false;
            if (!_gameTunnel.TryGetValue(pid, out tunnel)) return false;
        }
        try { tunnel.Client.Close(); return true; }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) { return false; }
    }

    public void Stop()
    {
        lock (_admitGate)
        {
            if (_runEpoch < long.MaxValue) Volatile.Write(ref _runEpoch, _runEpoch + 1);
            _shutdown?.Cancel();
            _listener?.Stop();
            _listener = null;
            foreach (var t in _gameTunnel.Values) t.Dispose();
            _gameTunnel.Clear();
            _shutdown?.Dispose();
            _shutdown = null;
            _admission = new MitmRelayAdmission();
        }
    }

    public void Dispose() => Stop();

    private sealed class Tunnel : IDisposable
    {
        private int _closed;   // 0 = rien, 1 = serveur fermé en premier, 2 = client fermé en premier
        public uint Pid { get; }
        public TcpClient Client { get; }
        public TcpClient Upstream { get; }
        public bool ConnectionServer { get; }
        public long RunEpoch { get; }
        public GameRelayLink Link { get; }
        public readonly object WriteGate = new();
        public UpstreamFrameGate? Gate { get; } = new();
        private long _outboundChunks;
        private long _inboundChunks;
        public Tunnel(uint pid, TcpClient client, TcpClient upstream, bool connectionServer,
            long runEpoch, GameRelayLink link) =>
            (Pid, Client, Upstream, ConnectionServer, RunEpoch, Link) =
            (pid, client, upstream, connectionServer, runEpoch, link);
        public long NextChunkSequence(bool clientToServer) => clientToServer
            ? Interlocked.Increment(ref _outboundChunks)
            : Interlocked.Increment(ref _inboundChunks);
        public void NoteFirstClose(bool server) => Interlocked.CompareExchange(ref _closed, server ? 1 : 2, 0);
        public bool ServerClosedFirst => Volatile.Read(ref _closed) == 1;
        public void Dispose() { try { Client.Dispose(); Upstream.Dispose(); } catch { } }
    }
}
