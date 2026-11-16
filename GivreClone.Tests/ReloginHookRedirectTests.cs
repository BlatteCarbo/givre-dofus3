using Xunit;

namespace GivreClone.Tests;

public sealed class ReloginHookRedirectTests
{
    [Fact]
    public void Relogin_hook_redirects_plain_ipv4_port_5555_and_still_redirects_mapped_ipv6()
    {
        string hook = File.ReadAllText(Path.Combine(MappingScan.RepoRoot(), "native", "GivreKama", "GivreKama.cpp"));
        Assert.Contains("name->sa_family == AF_INET6", hook);
        Assert.Contains("ntohs(t->sin6_port) == 5555", hook);
        Assert.Contains("name->sa_family == AF_INET", hook);
        Assert.Contains("ntohs(v4->sin_port) == 5555", hook);
        Assert.Contains("h.Address[10] = 0xFF", hook);
        Assert.Contains("h.Address[11] = 0xFF", hook);
        Assert.Contains("h.Addrlen   = 28", hook);
        Assert.Contains("htonl(INADDR_LOOPBACK)", hook);
        Assert.Contains("htons(PROXY_PORT)", hook);
    }
}
