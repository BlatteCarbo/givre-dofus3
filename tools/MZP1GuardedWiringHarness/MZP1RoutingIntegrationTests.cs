using GivreClone.Engine;
using GivreClone.Engine.Accounts;
using GivreClone.Engine.Recovery;
using GivreClone.Protocol;
using GivreCore.Mitm;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// The real BotOrchestrator -> SessionBinder -> BotSession chain is compiled
/// directly from production source; only GivreCore proxy is a no-network stub.
/// </summary>
public sealed class MZP1RoutingIntegrationTests
{
    private static ProtocolCatalog Catalog()
        => ProtocolCatalog.LoadFromStrings(
            "message GameMessage { int64 uid = 1; }",
            "{\"type.ankama.com/sample\":\"MessageType.GameMessage\"}",
            null,
            "{\"GameMessage\":{\"uid\":1}}");

    private static CapturedMessage Capture(uint pid, int connectionId = 41, long seq = 1)
        => new(seq, Direction.ServerToClient, connectionId, pid,
            "type.ankama.com/sample", Array.Empty<ProtoField>(), Array.Empty<byte>());

    [Fact]
    public void Captured_message_uses_real_protected_BotSession_constructor_and_never_falls_back()
    {
        var catalog = Catalog();
        var proxy = new TcpProxyServer();
        MZP1GuardedEngineWiring? wired = null;
        var orchestrator = new BotOrchestrator(catalog, proxy, new AccountManager(),
            guardedSessionFactory: captured => wired = MZP1GuardedEngineWiringFactory.Create(
                captured.ConnectionId, "3.7.10.10", catalog,
                new ProxyFrameSink(proxy, captured.ConnectionId), new HumanPacer(),
                null, null, null));

        proxy.FeedFixture(Capture(241));
        var session = Assert.Single(orchestrator.Sessions);
        Assert.Same(wired!.State, session.State);
        Assert.Same(wired.Bus, session.Bus);
        Assert.Same(wired.Actions, session.Actions);
        Assert.Same(wired.Context, session.Context);
        Assert.Equal(MZP1Admission.MissingJournal, wired.Admission);
        Assert.Equal((uint)241, session.ProcessId);
        Assert.Equal(41, session.ConnectionId);
        Assert.False(session.IsAuthenticated);
        Assert.Empty(orchestrator.AuthenticatedSessions);
    }

    [Fact]
    public void Connection_mismatch_cannot_bind_unguarded_or_guarded_actions()
    {
        var cat = Catalog();
        var proxy = new TcpProxyServer();
        var denied = MZP1GuardedEngineWiringFactory.Create(9, "3.7.10.10", cat,
            new ProxyFrameSink(proxy, 9), new HumanPacer(), null, null, null);
        Assert.Throws<ArgumentException>(() => new BotSession(8, cat, denied));
    }

    [Fact]
    public void Detaching_proxy_ends_reception_without_affecting_any_live_process()
    {
        var catalog = Catalog();
        var proxy = new TcpProxyServer();
        var binder = new SessionBinder(captured => new BotSession(captured.ConnectionId,
            catalog, MZP1GuardedEngineWiringFactory.Create(captured.ConnectionId,
                "3.7.10.10", catalog, new ProxyFrameSink(proxy, captured.ConnectionId),
                new HumanPacer(), null, null, null)));
        var observedCount = 0;
        var authenticatedCount = 0;
        binder.SessionOpened += _ => observedCount++;
        binder.SessionAuthenticated += _ => authenticatedCount++;
        binder.Attach(proxy);
        proxy.FeedFixture(Capture(311));
        Assert.Single(binder.Sessions);
        Assert.Equal(1, observedCount);
        Assert.Equal(0, authenticatedCount);
        Assert.Empty(binder.AuthenticatedSessions);
        binder.Detach(proxy);
        proxy.FeedFixture(Capture(312, connectionId: 42));
        Assert.Single(binder.Sessions);
        Assert.Equal(1, observedCount);
        Assert.Equal(0, authenticatedCount);
    }

    [Fact]
    public void Legacy_unprotected_bot_session_is_never_labeled_authenticated()
    {
        var catalog = Catalog();
        var proxy = new TcpProxyServer();
        var binder = new SessionBinder(id => new BotSession(
            id, catalog, new ProxyFrameSink(proxy, id), new HumanPacer()));
        var count = 0;
        binder.SessionAuthenticated += _ => count++;
        binder.Attach(proxy);
        proxy.FeedFixture(Capture(211));
        Assert.Single(binder.Sessions);
        Assert.Empty(binder.AuthenticatedSessions);
        Assert.False(binder.Find(41)!.IsAuthenticated);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Host_bound_factory_receives_the_real_catalog_and_connection_owned_sink()
    {
        var catalog = Catalog();
        var proxy = new TcpProxyServer();
        var called = 0;
        var orchestrator = new BotOrchestrator(catalog, proxy, new AccountManager(),
            hostBoundGuardedFactory: (capture, actualCatalog, actualSink, pacer) =>
            {
                called++;
                Assert.Same(catalog, actualCatalog);
                Assert.Equal(capture.ConnectionId,
                    Assert.IsType<ProxyFrameSink>(actualSink).ConnectionId);
                Assert.NotNull(pacer);
                return MZP1GuardedEngineWiringFactory.Create(capture.ConnectionId,
                    "3.7.10.10", actualCatalog, actualSink, pacer, null, null, null);
            });
        proxy.FeedFixture(Capture(901));
        var session = Assert.Single(orchestrator.Sessions);
        Assert.Equal(1, called);
        Assert.False(session.IsAuthenticated);
        Assert.Empty(orchestrator.AuthenticatedSessions);
    }

    [Fact]
    public void Host_rejects_simultaneous_unguarded_and_bound_factory_configurations()
    {
        var catalog = Catalog();
        var proxy = new TcpProxyServer();
        Assert.Throws<ArgumentException>(() => new BotOrchestrator(
            catalog, proxy, new AccountManager(),
            guardedSessionFactory: capture => throw new Exception("must not run"),
            hostBoundGuardedFactory: (capture, c, s, p) =>
                throw new Exception("must not run")));
    }

    [Fact]
    public async Task Default_orchestrator_never_creates_an_unguarded_action_sender()
    {
        var catalog = Catalog();
        var proxy = new TcpProxyServer();
        var orchestrator = new BotOrchestrator(catalog, proxy, new AccountManager());
        proxy.FeedFixture(Capture(803));
        var session = Assert.Single(orchestrator.Sessions);
        Assert.False(session.IsAuthenticated);
        Assert.Empty(orchestrator.AuthenticatedSessions);
        await Assert.ThrowsAsync<MZP1AdmissionException>(() =>
            session.Actions.SendRequestAsync("GameMessage", _ => { }));
    }
}
