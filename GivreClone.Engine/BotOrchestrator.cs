using GivreClone.Engine.Accounts;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreClone.Engine.Recovery;

namespace GivreClone.Engine;

/// <summary>
/// Possede la carte connectionId -&gt; BotSession et fabrique une session (liee au sink du proxy) par nouvelle
/// connexion. Point d'ancrage de l'orchestration multi-compte + sync d'equipe (Phase 4). La liaison
/// compte&lt;-&gt;connexion par pid arrive en Phase 1 (le pid du handshake n'est pas encore expose par GivreCore).
/// </summary>
public sealed class BotOrchestrator
{
    private readonly ProtocolCatalog _catalog;
    private readonly TcpProxyServer _proxy;
    private readonly IPacer _pacer;

    public AccountManager Accounts { get; }
    public SessionBinder Binder { get; }

    public BotOrchestrator(ProtocolCatalog catalog, TcpProxyServer proxy, AccountManager accounts,
        IPacer? pacer = null, Func<CapturedMessage, MZP1GuardedEngineWiring>? guardedSessionFactory = null,
        Func<CapturedMessage, ProtocolCatalog, IFrameSink, IPacer, MZP1GuardedEngineWiring>? hostBoundGuardedFactory = null)
    {
        if (guardedSessionFactory is not null && hostBoundGuardedFactory is not null)
            throw new ArgumentException("Select only one guarded session factory.");
        _catalog = catalog;
        _proxy = proxy;
        _pacer = pacer ?? new HumanPacer();
        Accounts = accounts;
        // The explicit guarded route cannot silently fall back to ActionSender.
        // It must be supplied by a host with independently authenticated origin,
        // session birth/generation and server response proof.
        // Unlike a caller-supplied sink, these concrete runtime ports originate
        // inside THIS BotOrchestrator's catalog and THIS proxy instance.
        Binder = hostBoundGuardedFactory is not null
            ? new SessionBinder(capture => new BotSession(capture.ConnectionId,
                _catalog, hostBoundGuardedFactory(capture, _catalog,
                    new ProvenanceBoundFrameSink(_proxy, capture.Provenance), _pacer)))
            : guardedSessionFactory is not null
                ? new SessionBinder(capture => new BotSession(capture.ConnectionId,
                    _catalog, guardedSessionFactory(capture)))
                : new SessionBinder(CreateDeniedSession);
        // SessionBinder owns both subscriptions so MessageCaptured and
        // ConnectionClosed share exactly one source-proxy/attachment epoch.
        // A permanent orchestrator callback would lose that epoch after
        // Detach/Attach and could revoke a successor's recycled numeric ID.
        Binder.Attach(proxy);
    }

    public IReadOnlyList<BotSession> Sessions => Binder.Sessions;
    public IReadOnlyList<BotSession> AuthenticatedSessions => Binder.AuthenticatedSessions;

    /// <summary>
    /// Raw source-bound neutral protobuf observations from the actual TCP pump.
    /// Subscribers must preserve the supplied provenance when passing bytes to
    /// a downstream decoder; this alone is not a game authentication or ACK.
    /// </summary>
    public event Action<TransportFrameObservation> TransportFrameObserved
    {
        add => _proxy.TransportFrameObserved += value;
        remove => _proxy.TransportFrameObserved -= value;
    }

    /// <summary>
    /// Actual transport's exact-link EOF/closure witness. No game event or
    /// server ACK is inferred; a caller must match every field of the link
    /// provenance when retiring its own observations.
    /// </summary>
    public event Action<TransportLinkProvenance> TransportLinkClosed
    {
        add => _proxy.TransportLinkClosed += value;
        remove => _proxy.TransportLinkClosed -= value;
    }

    public bool IsCurrentTransportLink(TransportLinkProvenance provenance) =>
        _proxy.IsCurrentTransportLink(provenance);

    /// <summary>
    /// Existing admitted session's exact relay link/run. Consumer callbacks
    /// must recheck TcpProxyServer.IsCurrentTransportLink at point of use.
    /// This does not establish game response or server ACK provenance.
    /// </summary>
    public bool TryGetCurrentTransportLink(BotSession session,
        out TransportLinkProvenance provenance) =>
        Binder.TryGetCurrentTransportLink(session, out provenance);

    // Never let a session's action sink silently follow a numeric connection ID
    // onto a different transport. A capture missing genuine link provenance
    // gets an inert sink, even when an external factory supplies an action guard.
    private sealed class ProvenanceBoundFrameSink(
        TcpProxyServer proxy, TransportLinkProvenance? identity) : IFrameSink
    {
        public bool Send(Direction direction, byte[] gameMessageBytes) =>
            identity is { } bound && proxy.Replay(bound, direction, gameMessageBytes);
    }

    /// <summary>
    /// Manual source-scoped close integration port. Automatic transport
    /// ConnectionClosed subscriptions belong to SessionBinder.Attach, where
    /// the attachment epoch is known. Never synthesize an ID or ACK here.
    /// </summary>
    public bool NotifyConnectionClosed(int connectionId) =>
        Binder.ForgetFrom(_proxy, connectionId);

    /// <summary>Preferred exact-link close port for a real accepted connection.</summary>
    public bool NotifyConnectionClosed(TransportLinkProvenance provenance) =>
        Binder.ForgetFrom(_proxy, provenance);

    // A missing factory MUST NOT silently recreate the historical unguarded
    // ActionSender. This fallback is a real engine graph with a permanently
    // denied IActionSender; it still supports honest read-only diagnostics.
    private BotSession CreateDeniedSession(CapturedMessage capture)
        => new(capture.ConnectionId, _catalog,
            MZP1GuardedEngineWiringFactory.Create(capture.ConnectionId,
                "3.7.10.10", _catalog,
                new ProvenanceBoundFrameSink(_proxy, capture.Provenance),
                _pacer, null, null, null));
}
