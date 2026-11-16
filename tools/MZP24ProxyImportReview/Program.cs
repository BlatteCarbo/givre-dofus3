using GivreClone.App.Services;

// Offline companion to the clone's existing bulk import. Uses redirected STDIN,
// never reads an application file or starts a network connection.
if (args.Length == 1 && args[0] == "--self-test")
    return ReviewTests.Run();

if (!Console.IsInputRedirected ||
    args.Length > 2 ||
    (args.Length == 2 && args[0] != "--protocol") ||
    (args.Length == 1 && args[0] is not "--help"))
{
    Console.Error.WriteLine("Usage: <texte collé> | dotnet run --project tools/MZP24ProxyImportReview [-- --protocol HTTP]");
    Console.Error.WriteLine("       dotnet run --project tools/MZP24ProxyImportReview -- --self-test");
    return args.Length == 1 && args[0] == "--help" ? 0 : 2;
}

string proto = args.Length == 2 ? args[1] : "SOCKS5";
try
{
    var review = MZP24ProxyImportReview.Inspect(Console.In.ReadToEnd(), proto);
    Console.WriteLine(review.Summary);
    foreach (var row in review.Rows)
    {
        // Only masked previews and fixed explanation strings. Never echo source.
        string detail = row.MaskedPreview is null ? row.Reason : row.MaskedPreview + " — " + row.Reason;
        Console.WriteLine($"Ligne {row.Line} [{row.Status}] {detail}");
    }
    return review.Rejected > 0 ? 1 : 0;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine("Prévol refusé : " + ex.Message);
    return 2;
}

static class ReviewTests
{
    public static int Run()
    {
        int passed = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("MZP24 FAIL " + label);
            passed++;
        }
        const string secret = "secret-password-42";
        var review = MZP24ProxyImportReview.Inspect(
            "# commentaire\nSOCKS5://foo.example:01080\r\nfoo.EXAMPLE:1080\n" +
            "u:" + secret + "@proxy.example:80\nhttps://proxy.example:443\n" +
            "ftp://broken.example:20\nproxy.example/path:80\n999.999.999.999:80\n-- ignored");

        Check(review.Accepted == 2, "accepts recognized endpoints");
        Check(review.Duplicates == 1, "detects duplicates after canonicalizing port and host");
        Check(review.Rejected == 4, "rejects explicit HTTPS and invalid inputs");
        Check(review.Rows[0].Line == 2, "preserves original line number");
        Check(review.Rows[1].DuplicateOf == 2, "links original duplicate line");
        Check(review.Rows[0].MaskedPreview == "SOCKS5 foo.example:1080 (sans identifiants)", "canonical endpoint");
        Check(review.Rows[2].MaskedPreview == "SOCKS5 proxy.example:80 (identifiants masqués)", "redacts user and password");
        Check(review.Rows.All(r => !(r.Reason + r.MaskedPreview).Contains(secret, StringComparison.Ordinal)), "never echoes password");
        Check(review.Rows.All(r => !(r.Reason + r.MaskedPreview).Contains("u:", StringComparison.Ordinal)), "never echoes username");
        Check(review.Rows.Any(r => r.Reason.Contains("HTTP CONNECT", StringComparison.Ordinal)), "rejects misleading HTTPS");
        Check(review.Rows.Any(r => r.Reason.Contains("nom DNS", StringComparison.Ordinal)), "rejects host path");
        Check(review.Rows.Any(r => r.Reason.Contains("nom DNS", StringComparison.Ordinal)), "rejects malformed IPV4");
        Check(MZP24ProxyImportReview.Inspect(null).Rows.Count == 0, "handles empty input");
        Check(MZP24ProxyImportReview.Inspect("127.0.0.1:3128", "HTTP").Rows.Single().MaskedPreview ==
              "HTTP 127.0.0.1:3128 (sans identifiants)", "default HTTP");
        Check(MZP24ProxyImportReview.Inspect("proxy.example:80:joe:pass:with:colon").Accepted == 1,
              "compatible with historical password colons");
        var duplicateUsers = MZP24ProxyImportReview.Inspect("host.example:1080:JOE:a\nhost.example:1080:joe:b");
        Check(duplicateUsers.Accepted == 1 && duplicateUsers.Duplicates == 1,
              "duplicate key matches historical case-insensitive user policy");
        Check(MZP24ProxyImportReview.Inspect("broken:0").Rows.Single().Reason.Contains("65535"),
              "actionable port advice");
        Check(MZP24ProxyImportReview.Inspect(new string('x', MZP24ProxyImportReview.MaxLineChars + 1))
              .Rejected == 1, "bounds per-line input");
        try { MZP24ProxyImportReview.Inspect(new string('x', MZP24ProxyImportReview.MaxInputChars + 1)); Check(false, "max input"); }
        catch (ArgumentOutOfRangeException) { passed++; }
        try { MZP24ProxyImportReview.Inspect("foo:80\n", "SSH"); Check(false, "unknown protocol"); }
        catch (ArgumentException) { passed++; }
        Console.WriteLine($"PASS MZP24ProxyImportReview {passed} checks. No store, account or network touched.");
        return 0;
    }
}
