using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class HostsGuardTests
{
    [Fact]
    public void Active_loopback_redirects_of_ankama_hosts_are_reported()
    {
        var lines = new[]
        {
            "# commentaire", "127.0.0.1 dofus2-co-production.ankama-games.com", "  127.0.0.1\tzaap-chat.ankama.com # note", "0.0.0.0 client-telemetry.roblox.com",
            "# disabled - 127.0.0.1 dofus2-co-production.ankama-games.com", "10.0.0.5 dofus2-co-production.ankama-games.com", "",
        };
        var r = HostsGuard.ActiveRedirects(lines);
        Assert.Equal(2, r.Count);
        Assert.Contains("127.0.0.1 zaap-chat.ankama.com", r);
    }

    [Fact]
    public void A_clean_file_reports_nothing()
    {
        Assert.Empty(HostsGuard.ActiveRedirects(new[] { "# 127.0.0.1 localhost", "0.0.0.0 metrics.roblox.com" }));
    }
}
