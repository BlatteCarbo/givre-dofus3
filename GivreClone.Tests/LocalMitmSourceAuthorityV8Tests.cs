extern alias F06App;

using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using LocalRelay = F06App::GivreClone.App.Services.LocalLaunch.LocalHandshakeRelay;

namespace GivreClone.Tests;

/// <summary>
/// Tests the actual LaunchService receiver used by LocalHandshakeRelay's
/// source-issued OnGameData callback, not a PID-only packet or a fabricated
/// gameplay ACK. These are inert local callbacks: no listener/client opened.
/// </summary>
public sealed class LocalMitmSourceAuthorityV8Tests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static Launch NewService()
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        Type store = typeof(Launch).Assembly.GetType(
            "GivreClone.App.Services.SocketSourceAuthorityLedger", throwOnError: true)!;
        // Constructor requiresConnectionToken=false: the true local MITM
        // relay issues source/run/tunnel rather than a Socket GUID token.
        object ledger = Activator.CreateInstance(store,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [false], CultureInfo.InvariantCulture)!;
        typeof(Launch).GetField("_localMitmSourceAuthority", Hidden)!
            .SetValue(service, ledger);
        return service;
    }

    private static bool Feed(Launch service, LocalRelay.GameRelayLink link,
        long sequence, bool outgoing, int bytes,
        Func<LocalRelay.GameRelayLink, bool> isCurrent)
        => (bool)typeof(Launch).GetMethod("ObserveLocalMitmRelayFrame", Hidden)!
            .Invoke(service, [link, sequence, outgoing, bytes, isCurrent])!;

    private static void Revoke(Launch service, LocalRelay.GameRelayLink link)
    {
        object ledger = typeof(Launch).GetField("_localMitmSourceAuthority", Hidden)!
            .GetValue(service)!;
        var method = ledger.GetType().GetMethod("RevokeExact")!;
        var capture = new F06App::GivreClone.App.Services.CaptureLinkIdentity(
            link.RelayInstanceId, link.OwnerPid, link.RunEpoch, link.TunnelId);
        method.Invoke(ledger, [capture]);
    }

    [Fact]
    public void Source_bound_local_mitm_game_tunnel_is_visible_but_never_server_ack()
    {
        var svc = NewService();
        var source = new LocalRelay.GameRelayLink(Guid.NewGuid(), 901, 17, 13);
        Assert.True(Feed(svc, source, 1, false, 158, observed => observed == source));
        Assert.True(Feed(svc, source, 1, true, 32, observed => observed == source));
        var state = svc.LocalMitmSourceAuthorityOf(901)!.Value;
        Assert.True(state.CurrentSourceWasObserved);
        Assert.Equal(17, state.RunEpoch);
        Assert.Equal(1, state.ServerFramesObserved);
        Assert.Equal(158, state.ServerWireBytes);
        Assert.Equal(1, state.LocalProtocolFramesObserved);
        Assert.Equal(0, state.EngineWritesCompleted); // no MITM Inject callback claim
        Assert.False(state.ServerAckAttributed);
        Assert.False(state.GameplayAuthorizationAvailable);
    }

    [Fact]
    public void Late_close_cannot_remove_replacement_tunnel_for_same_pid()
    {
        var svc = NewService();
        var issuer = Guid.NewGuid();
        var first = new LocalRelay.GameRelayLink(issuer, 902, 4, 100);
        var second = new LocalRelay.GameRelayLink(issuer, 902, 4, 101);
        Assert.True(Feed(svc, first, 1, false, 11, value => value == first));
        Assert.True(Feed(svc, second, 1, false, 22, value => value == second));
        Revoke(svc, first);
        Assert.Equal(22, svc.LocalMitmSourceAuthorityOf(902)!.Value.ServerWireBytes);
        Assert.False(Feed(svc, first, 2, false, 11, _ => false));
        Revoke(svc, second);
        Assert.Null(svc.LocalMitmSourceAuthorityOf(902));
    }

    [Fact]
    public void Reentrantly_invalidated_source_is_retired_without_promoting_data()
    {
        var svc = NewService();
        var source = new LocalRelay.GameRelayLink(Guid.NewGuid(), 903, 5, 7);
        int checks = 0;
        Assert.False(Feed(svc, source, 1, false, 22,
            value => value == source && ++checks < 2));
        Assert.Null(svc.LocalMitmSourceAuthorityOf(903));
        Assert.False(Feed(svc, source, 0, false, 22, _ => true));
        Assert.False(Feed(svc, source, 1, false, 0, _ => true));
    }

    [Fact]
    public void A_legacy_or_unowned_game_link_never_populates_read_only_authority()
    {
        var svc = NewService();
        var foreign = new LocalRelay.GameRelayLink(Guid.NewGuid(), 904, 1, 1);
        Assert.False(Feed(svc, foreign, 1, false, 42, _ => false));
        Assert.Null(svc.LocalMitmSourceAuthorityOf(904));
        Assert.False(Feed(svc, foreign with { RelayInstanceId = Guid.Empty },
            1, false, 42, _ => true));
        Assert.Null(svc.LocalMitmSourceAuthorityOf(904));
    }
}
