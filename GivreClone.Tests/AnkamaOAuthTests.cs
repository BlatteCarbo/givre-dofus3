using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GivreClone.App.Services.LocalLaunch;
using GivreLauncher.Core.Ankama;
using GivreLauncher.Core.Accounts;
using Xunit;

namespace GivreClone.Tests;

public class AnkamaOAuthTests
{
    [Fact]
    public void Pkce_challenge_matches_the_rfc7636_vector()
        => Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", AnkamaOAuth.CodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void Verifiers_are_long_distinct_and_url_safe()
    {
        var a = AnkamaOAuth.NewCodeVerifier(); var b = AnkamaOAuth.NewCodeVerifier();
        Assert.Equal(96, a.Length); Assert.NotEqual(a, b);
        Assert.All(a, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
    }

    [Fact]
    public void Login_url_targets_the_registered_client_and_local_redirect()
    {
        var u = AnkamaOAuth.LoginUrl("V");
        Assert.StartsWith("https://auth.ankama.com/login/ankama?", u);
        Assert.Contains("client_id=102", u);
        Assert.Contains(Uri.EscapeDataString("http://127.0.0.1:9001/authorized"), u);
        Assert.Contains("code_challenge=" + AnkamaOAuth.CodeChallenge("V"), u);
        Assert.DoesNotContain("V&", u.Replace("code_challenge=" + AnkamaOAuth.CodeChallenge("V"), ""));   // le verifier lui-même n'est jamais dans l'URL
    }

    [Fact]
    public void Certificate_hash_is_sha256_of_hm1_plus_the_decrypted_certificate()
    {
        var (hm1, hm2) = AnkamaOAuth.Device.FromId("device-1");
        Assert.Equal(32, hm1.Length); Assert.Equal(new string(hm1.Reverse().ToArray()), hm2);
        const string plain = "certificat-de-test";
        using var aes = Aes.Create(); aes.Mode = CipherMode.ECB; aes.Padding = PaddingMode.PKCS7; aes.Key = Encoding.UTF8.GetBytes(hm2);
        var enc = Convert.ToBase64String(aes.CreateEncryptor().TransformFinalBlock(Encoding.UTF8.GetBytes(plain), 0, plain.Length));
        string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hm1 + plain))).ToLowerInvariant();
        Assert.Equal(expected, AnkamaOAuth.ComputeCertificateHash(enc, hm1, hm2));
    }

    [Fact]
    public void Device_fingerprint_is_stable_across_calls_and_persisted()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ankoauth_" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "dev.id");
        try
        {
            AnkamaOAuth.Device.PathOverride = file;
            var a = AnkamaOAuth.Device.Fingerprint(); var b = AnkamaOAuth.Device.Fingerprint();
            Assert.Equal(a, b); Assert.True(File.Exists(file));
        }
        finally { AnkamaOAuth.Device.PathOverride = null; if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Account_json_gives_login_and_shield_flag()
    {
        using var d1 = JsonDocument.Parse("{\"login\":\"x@y.z\",\"security\":[\"SHIELD\"]}");
        var a = AnkamaOAuth.ParseAccount(d1.RootElement); Assert.Equal("x@y.z", a.Login); Assert.True(a.ShieldActive);
        using var d2 = JsonDocument.Parse("{\"email\":\"e@y.z\",\"security\":[]}");
        var b = AnkamaOAuth.ParseAccount(d2.RootElement); Assert.Equal("e@y.z", b.Login); Assert.False(b.ShieldActive);
        using var d3 = JsonDocument.Parse("{}");
        Assert.Null(AnkamaOAuth.ParseAccount(d3.RootElement).Login);
    }

    [Fact]
    public void Certificate_json_accepts_flat_and_nested_shapes_with_numeric_or_text_id()
    {
        using var a = JsonDocument.Parse("{\"id\":123,\"encodedCertificate\":\"AAA=\"}");
        Assert.Equal(("123", "AAA="), (AnkamaOAuth.ParseCertificate(a.RootElement).Id, AnkamaOAuth.ParseCertificate(a.RootElement).Encoded));
        using var b = JsonDocument.Parse("{\"certificate\":{\"id\":\"9\",\"encoded_certificate\":\"BBB=\"}}");
        Assert.Equal(("9", "BBB="), (AnkamaOAuth.ParseCertificate(b.RootElement).Id, AnkamaOAuth.ParseCertificate(b.RootElement).Encoded));
    }

    private sealed class Capture : HttpMessageHandler
    {
        public HttpRequestMessage? Last;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Last = r;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"token\":\"TOK\"}") });
        }
    }

    private sealed class NoPrompt : IShieldCodePrompt { public Task<string?> RequestSecurityCodeAsync(string login, CancellationToken ct = default) => Task.FromResult<string?>(null); }

    [Fact]
    public async Task Create_token_sends_certificate_id_and_hash_and_the_launcher_agent_when_a_certificate_is_stored()
    {
        var h = new Capture();
        using var auth = new AnkamaAuth(new HttpClient(h), new NoPrompt()) { CertificateHasher = enc => "HASH-" + enc };
        var tok = await auth.AuthenticateAsync(new Account { Login = "l", ApiKey = "KEY", Certificate = new ShieldCertificate { Id = "77", EncodedCertificate = "ENC" } });
        Assert.Equal("TOK", tok);
        var q = h.Last!.RequestUri!.Query;
        Assert.Contains("game=1", q); Assert.Contains("certificate_id=77", q); Assert.Contains("certificate_hash=HASH-ENC", q);
        Assert.Contains("Zaap", string.Join(" ", h.Last.Headers.GetValues("User-Agent")));
        Assert.Equal("KEY", h.Last.Headers.GetValues("apiKey").Single());
    }

    [Fact]
    public async Task Create_token_without_certificate_or_hasher_sends_no_certificate_parameters()
    {
        var h = new Capture();
        using var auth = new AnkamaAuth(new HttpClient(h), new NoPrompt());
        await auth.AuthenticateAsync(new Account { Login = "l", ApiKey = "KEY", Certificate = new ShieldCertificate { Id = "1", EncodedCertificate = "E" } });
        Assert.DoesNotContain("certificate", h.Last!.RequestUri!.Query);
    }

    private sealed class Fixed : HttpMessageHandler
    {
        private readonly HttpStatusCode _code; private readonly string _body; public Uri? Url;
        public Fixed(HttpStatusCode c, string b) => (_code, _body) = (c, b);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        { Url = r.RequestUri; return Task.FromResult(new HttpResponseMessage(_code) { Content = new StringContent(_body) }); }
    }

    [Fact]
    public async Task Security_code_request_triggers_the_email_and_tells_otp_accounts_apart()
    {
        var ok = new Fixed(HttpStatusCode.OK, "{\"domain\":\"gmail.com\"}");
        Assert.Equal(AnkamaOAuth.SecurityCodeMode.EmailSent, await AnkamaOAuth.RequestSecurityCodeAsync(new HttpClient(ok), "K", false, default));
        Assert.Contains("Shield/SecurityCode?transportType=EMAIL", ok.Url!.ToString());
        var sms = new Fixed(HttpStatusCode.OK, "{}");
        await AnkamaOAuth.RequestSecurityCodeAsync(new HttpClient(sms), "K", true, default);
        Assert.Contains("transportType=SMS", sms.Url!.ToString());
        var otp = new Fixed(HttpStatusCode.Forbidden, "{\"status\":403,\"message\":\"SECUREDBYOTP\"}");
        Assert.Equal(AnkamaOAuth.SecurityCodeMode.OtpRequired, await AnkamaOAuth.RequestSecurityCodeAsync(new HttpClient(otp), "K", false, default));
        var none = new Fixed(HttpStatusCode.Forbidden, "{\"status\":403,\"message\":\"NONEEDTOBESECURED\"}");
        Assert.Equal(AnkamaOAuth.SecurityCodeMode.NotNeeded, await AnkamaOAuth.RequestSecurityCodeAsync(new HttpClient(none), "K", false, default));
        var bad = new Fixed(HttpStatusCode.InternalServerError, "{\"message\":\"SECRET-DETAIL\"}");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AnkamaOAuth.RequestSecurityCodeAsync(new HttpClient(bad), "K", false, default));
        Assert.DoesNotContain("SECRET-DETAIL", ex.Message);
        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task Validate_code_uses_the_email_endpoint_and_validate_otp_the_other()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ankoauth2_" + Guid.NewGuid().ToString("N"));
        try
        {
            AnkamaOAuth.Device.PathOverride = Path.Combine(dir, "d.id");
            var h1 = new Fixed(HttpStatusCode.OK, "{\"id\":5,\"encodedCertificate\":\"QQ==\"}");
            var c1 = await AnkamaOAuth.ValidateSecurityCodeAsync(new HttpClient(h1), "K", "123456", otp: false, default);
            Assert.Equal("5", c1.Id);
            Assert.Contains("/Shield/ValidateCode?", h1.Url!.ToString());
            Assert.Contains("code=123456", h1.Url.ToString()); Assert.Contains("game_id=102", h1.Url.ToString());
            var h2 = new Fixed(HttpStatusCode.OK, "{\"id\":6,\"encodedCertificate\":\"QQ==\"}");
            await AnkamaOAuth.ValidateOtpAsync(new HttpClient(h2), "K", "654321", default);
            Assert.Contains("/Shield/ValidateOtp?", h2.Url!.ToString());
        }
        finally { AnkamaOAuth.Device.PathOverride = null; if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
