using System.Net;
using GivreClone.App.Services.Mcp;
using Xunit;

namespace GivreClone.Tests;

public class RemoteAccessPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1", false, true)]
    [InlineData("::1", false, true)]
    [InlineData("100.64.0.1", false, true)]      // Tailscale
    [InlineData("100.127.255.254", false, true)]
    [InlineData("100.63.0.1", false, false)]     // hors de 100.64.0.0/10
    [InlineData("100.128.0.1", false, false)]
    [InlineData("fd7a:115c:a1e0::1", false, true)]
    [InlineData("192.168.1.20", false, false)]   // LAN refusé par défaut
    [InlineData("192.168.1.20", true, true)]
    [InlineData("10.0.0.5", true, true)]
    [InlineData("172.16.3.4", true, true)]
    [InlineData("172.32.0.1", true, false)]
    [InlineData("8.8.8.8", false, false)]        // Internet : jamais
    [InlineData("8.8.8.8", true, false)]
    [InlineData("::ffff:100.80.1.2", false, true)]   // IPv4 mappée
    [InlineData("::ffff:203.0.113.7", true, false)]
    [InlineData("2a01:e0a::1", true, false)]
    public void IsAllowed_matrix(string ip, bool lan, bool expected)
        => Assert.Equal(expected, RemoteAccessPolicy.IsAllowed(IPAddress.Parse(ip), lan));

    [Fact]
    public void Token_is_random_url_safe_and_compared_exactly()
    {
        string a = RemoteAccessPolicy.NewToken(), b = RemoteAccessPolicy.NewToken();
        Assert.NotEqual(a, b);
        Assert.Equal(32, a.Length);
        Assert.DoesNotContain('+', a); Assert.DoesNotContain('/', a); Assert.DoesNotContain('=', a);
        Assert.True(RemoteAccessPolicy.TokenMatches(a, a));
        Assert.False(RemoteAccessPolicy.TokenMatches(a, b));
        Assert.False(RemoteAccessPolicy.TokenMatches(a, a + "x"));
        Assert.False(RemoteAccessPolicy.TokenMatches(a, null));
        Assert.False(RemoteAccessPolicy.TokenMatches("", ""));   // jeton vide = jamais d'accès
    }

    [Theory]
    [InlineData("list_bots", true)]
    [InlineData("get_craft_advice", true)]
    [InlineData("a", false)]
    [InlineData("List_Bots", false)]
    [InlineData("../etc", false)]
    [InlineData("tool name", false)]
    [InlineData("", false)]
    public void IsToolName_accepts_only_snake_case(string name, bool expected) => Assert.Equal(expected, RemoteAccessPolicy.IsToolName(name));
}
