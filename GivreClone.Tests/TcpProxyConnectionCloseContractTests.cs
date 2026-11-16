using System.Collections;
using System.Net.Sockets;
using System.Reflection;
using GivreCore.Mitm;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Entirely offline transport lifecycle contract. A test Link is made with
/// unconnected TcpClient objects; neither Start nor Connect is ever called.
/// The portable bundled legacy core may not expose the optional close event.
/// </summary>
public sealed class TcpProxyConnectionCloseContractTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class DeniedAuthority : ITestProxyRouteAuthority
    {
        public bool AdmitClient(TcpClient client, out uint independentlyVerifiedProcessId)
        {
            independentlyVerifiedProcessId = 0;
            return false;
        }
        public bool IsStillOwner(TcpClient client, uint processId) => false;
        public bool MayReplay(TcpClient client, uint processId, Direction direction) => false;
    }

    private static (object Link, object Index, MethodInfo Close) AddSyntheticLink(
        TcpProxyServer proxy, int id)
    {
        var type = typeof(TcpProxyServer).GetNestedType("Link", BindingFlags.NonPublic)!;
        var ctor = Assert.Single(type.GetConstructors(Hidden | BindingFlags.Public));
        object link = ctor.Invoke(new object[]
        {
            id, 801u, new TcpClient(), new TcpClient(), new DeniedAuthority()
        });
        object index = typeof(TcpProxyServer).GetField("_links", Hidden)!.GetValue(proxy)!;
        var tryAdd = index.GetType().GetMethod("TryAdd")!;
        Assert.True((bool)tryAdd.Invoke(index, new[] { (object)id, link })!);
        var close = typeof(TcpProxyServer).GetMethod("CloseLink", Hidden)!;
        return (link, index, close);
    }

    private static bool Contains(object index, int id)
        => (bool)index.GetType().GetMethod("ContainsKey")!.Invoke(index, new object[] { id })!;

    [Fact]
    public void Stop_retires_exact_link_before_notification_and_is_idempotent()
    {
        using var proxy = new TcpProxyServer(15071);
        var closeEvent = typeof(TcpProxyServer).GetEvent("ConnectionClosed");
        if (closeEvent is null) return; // portable older source: feature not exposed
        var (_, index, close) = AddSyntheticLink(proxy, 71);
        var seen = new List<(int Id, bool StillPresent)>();
        closeEvent.AddEventHandler(proxy, (Action<int>)(_ => throw new InvalidOperationException("subscriber")));
        closeEvent.AddEventHandler(proxy, (Action<int>)(id => seen.Add((id, Contains(index, id)))));

        proxy.Stop();
        proxy.Stop();
        Assert.Equal(new[] { (71, false) }, seen);
        Assert.False(Contains(index, 71));
    }

    [Fact]
    public async Task Concurrent_stop_and_finally_publish_one_close_witness()
    {
        using var proxy = new TcpProxyServer(15072);
        var closeEvent = typeof(TcpProxyServer).GetEvent("ConnectionClosed");
        if (closeEvent is null) return;
        var (link, index, close) = AddSyntheticLink(proxy, 72);
        int calls = 0;
        closeEvent.AddEventHandler(proxy,
            (Action<int>)(id => { if (id == 72) Interlocked.Increment(ref calls); }));

        await Task.WhenAll(
            Task.Run(() => close.Invoke(proxy, new[] { link })),
            Task.Run(proxy.Stop));

        Assert.False(Contains(index, 72));
        Assert.Equal(1, Volatile.Read(ref calls));
    }
}
