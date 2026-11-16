using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class ProxyImportParserTests
{
    [Theory]
    [InlineData("1.2.3.4:1080", "SOCKS5", "1.2.3.4", 1080, null, null)]
    [InlineData("1.2.3.4:8080:bob:s3cret", "SOCKS5", "1.2.3.4", 8080, "bob", "s3cret")]
    [InlineData("bob:s3cret@1.2.3.4:8080", "SOCKS5", "1.2.3.4", 8080, "bob", "s3cret")]
    [InlineData("http://bob:s3cret@proxy.example:3128", "HTTP", "proxy.example", 3128, "bob", "s3cret")]
    [InlineData("socks5://proxy.example:1080", "SOCKS5", "proxy.example", 1080, null, null)]
    [InlineData("HTTPS://proxy.example:443", "HTTP", "proxy.example", 443, null, null)]
    public void Recognised_formats(string line, string proto, string host, int port, string? user, string? pass)
    {
        var r = ProxyImportParser.Parse(line);
        var p = Assert.Single(r.Valid);
        Assert.Equal((proto, host, port, user, pass), (p.Protocol, p.Host, p.Port, p.User, p.Pass));
        Assert.Empty(r.Errors);
    }

    [Fact]
    public void Password_may_contain_colons()
        => Assert.Equal("a:b:c", Assert.Single(ProxyImportParser.Parse("h.example:1080:u:a:b:c").Valid).Pass);

    [Fact]
    public void Default_protocol_applies_unless_a_scheme_overrides_it()
    {
        var r = ProxyImportParser.Parse("1.1.1.1:80\nsocks5://2.2.2.2:81", "HTTP");
        Assert.Equal(new[] { "HTTP", "SOCKS5" }, r.Valid.Select(p => p.Protocol));
    }

    [Theory]
    [InlineData("nonsense", "format non reconnu")]
    [InlineData("1.2.3.4:0", "port invalide")]
    [InlineData("1.2.3.4:70000", "port invalide")]
    [InlineData("1.2.3.4:abc", "port invalide")]
    [InlineData("1.2.3.4:-5", "port invalide")]
    [InlineData("ftp://1.2.3.4:21", "protocole inconnu")]
    [InlineData("nopass@1.2.3.4:80", "identifiants user:pass attendus avant @")]
    [InlineData(":80", "hôte invalide")]
    public void Rejected_lines_carry_a_reason_and_never_the_content(string line, string reason)
    {
        var r = ProxyImportParser.Parse(line);
        Assert.Empty(r.Valid);
        var e = Assert.Single(r.Errors);
        Assert.Equal(reason, e.Reason);
        Assert.Equal(1, e.Line);
    }

    [Fact]
    public void Blank_lines_and_comments_are_skipped_and_line_numbers_are_kept()
    {
        var r = ProxyImportParser.Parse("# liste\n\n1.1.1.1:80\r\nbad\n\n2.2.2.2:81");
        Assert.Equal(new[] { 3, 6 }, r.Valid.Select(p => p.Line));
        Assert.Equal(4, Assert.Single(r.Errors).Line);
    }

    [Fact]
    public void Duplicates_are_counted_once_per_protocol_host_port_user()
    {
        var r = ProxyImportParser.Parse("1.1.1.1:80\n1.1.1.1:80\n1.1.1.1:80:u:p\nhttp://1.1.1.1:80");
        Assert.Equal(3, r.Valid.Count);   // même hôte:port mais utilisateur ou protocole différent = distinct
        Assert.Equal(1, r.Duplicates);
    }

    [Fact]
    public void Summary_never_leaks_credentials()
    {
        var r = ProxyImportParser.Parse("secretuser:secretpass@1.1.1.1:80\nbad secretpass");
        Assert.Equal("1 proxy(s) valide(s), 1 ligne(s) ignorée(s)", r.Summary);
        Assert.DoesNotContain("secret", r.Summary);
        Assert.DoesNotContain("secret", r.Errors.Single().Reason);
    }

    [Fact]
    public void Null_or_empty_text_is_an_empty_result()
    {
        Assert.Empty(ProxyImportParser.Parse(null).Valid);
        Assert.Empty(ProxyImportParser.Parse("   \n  ").Errors);
    }

    [Theory]
    [InlineData("Mon proxy", 3, "Mon proxy 3")]
    [InlineData("  ", 1, "Proxy 1")]
    [InlineData(null, 2, "Proxy 2")]
    public void Names_are_numbered(string? nameBase, int i, string expected) => Assert.Equal(expected, ProxyImportParser.NameFor(nameBase, i));
}

public class ProxyImportParserDashCommentTests
{
    [Fact]
    public void Lines_starting_with_double_dash_are_comments_too()
    {
        var r = GivreClone.App.Services.ProxyImportParser.Parse("-- liste du jour\n1.1.1.1:80\n--2.2.2.2:81");
        Assert.Equal("1.1.1.1", Assert.Single(r.Valid).Host);
        Assert.Empty(r.Errors);
    }
}
