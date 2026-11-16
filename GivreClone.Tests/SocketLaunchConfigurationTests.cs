using System.Text.Json;
using GivreClone.Engine.Accounts;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Offline regression of the persisted mode and routing values consumed by the launch dispatcher.</summary>
public sealed class SocketLaunchConfigurationTests
{
    [Fact]
    public void Existing_account_without_mode_remains_mitm_after_loading()
    {
        var account = JsonSerializer.Deserialize<Account>("{\"Login\":\"fixture\"}");
        Assert.NotNull(account);
        Assert.Equal(ConnectionMode.Mitm, account.Mode);
    }

    [Theory]
    [InlineData(AuthSource.Browser, 0)]
    [InlineData(AuthSource.KeyData, 1)]
    [InlineData(AuthSource.Browser, 2)]
    public void Socket_mode_and_server_selection_survive_account_persistence(AuthSource source, int selectMode)
    {
        var account = new Account
        {
            Login = "fixture", Mode = ConnectionMode.Socket, Source = source,
            AutoConnectType = selectMode, ProxyId = "connection-proxy", GameProxyId = "game-proxy"
        };
        var loaded = JsonSerializer.Deserialize<Account>(JsonSerializer.Serialize(account));
        Assert.NotNull(loaded);
        Assert.Equal(ConnectionMode.Socket, loaded.Mode);
        Assert.Equal(source, loaded.Source);
        Assert.Equal(selectMode, loaded.AutoConnectType);
        Assert.Equal("connection-proxy", loaded.ProxyId);
        Assert.Equal("game-proxy", loaded.GameProxyId);
    }

    [Fact]
    public void Socket_account_without_game_proxy_uses_optional_route()
    {
        var account = new Account { Mode = ConnectionMode.Socket, ProxyId = "primary" };
        var loaded = JsonSerializer.Deserialize<Account>(JsonSerializer.Serialize(account));
        Assert.NotNull(loaded);
        Assert.Equal("primary", loaded.ProxyId);
        Assert.Null(loaded.GameProxyId);
    }
}
