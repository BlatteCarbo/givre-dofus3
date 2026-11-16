using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.LocalLaunch;
using GivreCore.Protobuf;

namespace GivreClone.App.Services.Transport.Socket;

/// <summary>
/// Second écouteur local. Le hook envoie le client visible ici. Aucune socket vers l'hôte du jeu n'est ouverte :
/// les réponses de login viennent du cache, puis les trames de jeu sont rejouées ou recopiées sur le Socket déjà ouvert.
/// </summary>
public sealed class WorldMirrorAcceptor : IDisposable
{
    private readonly Func<uint, uint?> _socketForVisible;
    private readonly Func<uint, bool> _ownedAndLive;
    private readonly Func<byte[], WorldMirrorInbound> _classify;
    private readonly Func<uint, string, byte[]?> _loginFrame;
    private readonly Func<uint, WorldMirrorSession?> _session;
    private readonly Func<uint, WorldMirrorLive> _live;
    private readonly Func<uint, byte[], bool> _inject;
    private readonly Action<uint, string> _log;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();

    public WorldMirrorAcceptor(
        Func<uint, uint?> socketForVisible,
        Func<uint, bool> ownedAndLive,
        Func<byte[], WorldMirrorInbound> classify,
        Func<uint, string, byte[]?> loginFrame,
        Func<uint, WorldMirrorSession?> session,
        Func<uint, WorldMirrorLive> live,
        Func<uint, byte[], bool> inject,
        Action<uint, string> log,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _socketForVisible = socketForVisible;
        _ownedAndLive = ownedAndLive;
        _classify = classify;
        _loginFrame = loginFrame;
        _session = session;
        _live = live;
        _inject = inject;
        _log = log;
        _delay = delay ?? ((slice, token) => Task.Delay(slice, token));
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start(16);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        if (Port is 5657 or 5658 or 26666)
        {
            _listener.Stop();
            _shutdown.Dispose();
            throw new InvalidOperationException("Port réservé à un GivreClone d'origine.");
        }
        _ = AcceptLoop(_shutdown.Token);
    }

    public int Port { get; }

    public void Dispose()
    {
        try { _shutdown.Cancel(); } catch { /* déjà arrêté */ }
        try { _listener.Stop(); } catch { /* déjà arrêté */ }
        _shutdown.Dispose();
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (Exception) when (ct.IsCancellationRequested) { return; }
            catch (SocketException) { return; }
            catch (ObjectDisposedException) { return; }
            _ = Serve(client, ct);
        }
    }

    private async Task Serve(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            var header = new byte[LocalHandshakeRelay.HandshakeLength];
            try
            {
                using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
                to.CancelAfter(TimeSpan.FromSeconds(5));
                await stream.ReadExactlyAsync(header, to.Token).ConfigureAwait(false);
            }
            catch { _log(0, "[miroir] connexion entrante sans en-tête du hook (5 s) : fermée"); return; }

            var handshake = LocalHandshakeRelay.ParseHandshake(header);
            if (handshake is null) { _log(0, "[miroir] en-tête du hook illisible : connexion fermée"); return; }
            uint visiblePid = handshake.ProcessId;
            uint? socket = _socketForVisible(visiblePid);
            if (socket is not uint socketPid || !_ownedAndLive(visiblePid))
            {
                _log(visiblePid, "[miroir] pid non lié ou non possédé : connexion fermée");
                return;
            }
            _log(socketPid, $"[miroir] fenêtre {visiblePid} connectée au miroir");

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var gate = new object();
            bool attached = false;
            var pending = new List<byte>();
            var scratch = new byte[8192];
            try
            {
                while (!linked.IsCancellationRequested)
                {
                    int n = await stream.ReadAsync(scratch, linked.Token).ConfigureAwait(false);
                    if (n == 0) { _log(socketPid, $"[miroir] fenêtre {visiblePid} a fermé la connexion"); return; }
                    pending.AddRange(scratch.AsSpan(0, n).ToArray());
                    List<byte[]> frames;
                    try { frames = RawProtobuf.ConsumeFrames(pending.ToArray(), out var rest); pending = new List<byte>(rest); }
                    catch { return; }
                    foreach (var body in frames)
                        await Dispatch(body, stream, gate, client, socketPid, linked, attachedFlag: () => attached, markAttached: () => attached = true).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* fermeture de la fenêtre visible */ }
            catch (Exception) { /* la fenêtre visible est partie ; l'amont Socket n'est pas ici */ }
        }
    }

    private async Task Dispatch(byte[] body, NetworkStream stream, object gate, TcpClient client, uint socketPid,
        CancellationTokenSource linked, Func<bool> attachedFlag, Action markAttached)
    {
        WorldMirrorInbound inbound;
        try { inbound = _classify(body); }
        catch { _log(socketPid, $"[miroir] trame client illisible ({body.Length} o)"); return; }
        if (inbound.Class == WorldMirrorFrameClass.Login)
        {
            var replay = _loginFrame(socketPid, inbound.Name);
            if (replay is not { Length: > 0 })
                _log(socketPid, WorldMirrorSession.LoginMissingLog);
            else
            {
                _log(socketPid, $"[miroir] connexion : rejoue {inbound.Name}");
                Write(stream, gate, replay);
            }
            return;
        }
        if (inbound.Class != WorldMirrorFrameClass.Game || string.IsNullOrEmpty(inbound.Name))
        {
            _log(socketPid, $"[miroir] trame client non reconnue ({inbound.Class}, {body.Length} o) : ignorée");
            return;
        }
        var session = _session(socketPid);
        if (session is null) { _log(socketPid, $"[miroir] {inbound.Name} : aucune session miroir armée"); return; }
        if (!attachedFlag())
        {
            markAttached();
            session.AttachVisible(
                bytes => Write(stream, gate, bytes),
                () =>
                {
                    try { linked.Cancel(); } catch { /* déjà fermé */ }
                    try { client.Close(); } catch { /* déjà fermé */ }
                });
        }
        var wire = inbound.Wire is { Length: > 0 } ? inbound.Wire : WorldMirrorFraming.Prefix(body);
        while (!linked.IsCancellationRequested)
        {
            var live = _live(socketPid);
            var result = session.HandleClientRequest(new WorldMirrorClientRequest(inbound.Name, inbound.CharacterId, wire, live));
            foreach (var part in result.ToClient) Write(stream, gate, part);
            try { WorldMirrorDelivery.ForwardUpstream(result, frame => _inject(socketPid, frame)); }
            catch { /* l'injecteur refuse ; la fenêtre a déjà sa réponse locale */ }
            if (result.Status is not (WorldMirrorClientStatus.Waiting or WorldMirrorClientStatus.Forwarded))
                _log(socketPid, $"[miroir] {inbound.Name} -> {result.Status} ({result.ToClient.Length} trame(s))");
            if (result.Status != WorldMirrorClientStatus.Waiting) return;
            await _delay(TimeSpan.FromMilliseconds(25), linked.Token).ConfigureAwait(false);
        }
    }

    private static void Write(Stream stream, object gate, byte[] bytes)
    {
        if (bytes is not { Length: > 0 }) return;
        try { lock (gate) stream.Write(bytes, 0, bytes.Length); }
        catch { /* la fenêtre visible est partie */ }
    }
}
