using System.IO;
using System.IO.Compression;
using System.Linq;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class IncidentReportTests
{
    [Fact]
    public void Zip_holds_summary_and_redacted_logs()
    {
        string dir = Path.Combine(Path.GetTempPath(), "incident-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllLines(Path.Combine(dir, "transport-1.log"), new[] { "ok line", "password=hunter2 next", "access_token: abc.def" });
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "ignored");
            string zip = Path.Combine(dir, "out", "r.zip");
            Assert.Equal(1, IncidentReport.Write(dir, zip, "resume\r\npassword=summary-password\r\nAuthorization: Bearer summary-token\nfin"));
            using var z = ZipFile.OpenRead(zip);
            Assert.Equal(new[] { "logs/transport-1.log", "manifeste.json", "resume.txt" }, z.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray());
            string log = new StreamReader(z.GetEntry("logs/transport-1.log")!.Open()).ReadToEnd();
            Assert.Contains("ok line", log);
            Assert.DoesNotContain("hunter2", log);
            Assert.DoesNotContain("abc.def", log);
            using var summaryReader = new StreamReader(z.GetEntry("resume.txt")!.Open());
            string summary = summaryReader.ReadToEnd();
            Assert.StartsWith("resume\r\n", summary);
            Assert.EndsWith("\nfin", summary);
            Assert.DoesNotContain("summary-password", summary);
            Assert.DoesNotContain("summary-token", summary);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("Authorization: Bearer abc123.def-456_ghi", "abc123")]
    [InlineData("GET https://alice:hunter2@example.org/path", "hunter2")]
    [InlineData("WEBHOOK_URL = \"https://discord.com/api/webhooks/123456789012345678/AbCd-EfGh_1234\"", "AbCd-EfGh")]
    [InlineData("SESSION_ID=9f8e7d6c5b4a", "9f8e7d6c5b4a")]
    [InlineData("cookie: sid=zzz999; path=/", "zzz999")]
    [InlineData("{\"email\":\"joueur@exemple.fr\",\"ok\":true}", "joueur@exemple.fr")]
    [InlineData("jeton eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV", "SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV")]
    public void Credentials_are_masked_in_every_common_shape(string line, string secret)
    {
        string r = IncidentReport.Redact(line, out bool changed);
        Assert.True(changed);
        Assert.DoesNotContain(secret, r);
    }

    [Theory]
    [InlineData("ticket accepté par le serveur de jeu")]
    [InlineData("[jeu] nouvelle session de jeu -> lien rétabli")]
    [InlineData("[combat] sort 32443 -> cible 233 (dist 4)")]
    [InlineData("Récolte de Cuivre (cellule 292) token=")]
    public void Ordinary_game_lines_are_left_alone(string line)
    {
        Assert.Equal(line, IncidentReport.Redact(line, out bool changed));
        Assert.False(changed);
    }

    [Fact]
    public void Manifest_lists_each_written_file_with_a_matching_hash_and_the_masked_line_count()
    {
        string dir = Path.Combine(Path.GetTempPath(), "incident-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllLines(Path.Combine(dir, "a.log"), new[] { "ok", "password=x1", "token: y2", "fin" });
            string zip = Path.Combine(dir, "r.zip");
            IncidentReport.Write(dir, zip, "resume\npassword=summary-secret\nfin");
            using var z = ZipFile.OpenRead(zip);
            using var doc = System.Text.Json.JsonDocument.Parse(new StreamReader(z.GetEntry("manifeste.json")!.Open()).ReadToEnd());
            var files = doc.RootElement.GetProperty("fichiers").EnumerateArray().ToDictionary(e => e.GetProperty("nom").GetString()!);
            Assert.Equal(2, files["logs/a.log"].GetProperty("lignesMasquees").GetInt32());
            Assert.Equal(1, files["resume.txt"].GetProperty("lignesMasquees").GetInt32());
            Assert.Equal(3, files["resume.txt"].GetProperty("lignes").GetInt32());
            foreach (var file in files)
            {
                using var stream = z.GetEntry(file.Key)!.Open();
                using var content = new MemoryStream();
                stream.CopyTo(content);
                byte[] bytes = content.ToArray();
                string expected = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
                Assert.Equal(bytes.LongLength, file.Value.GetProperty("octets").GetInt64());
                Assert.Equal(expected, file.Value.GetProperty("sha256").GetString());
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(65001)]
    [InlineData(1200)]
    [InlineData(12000)]
    public void Large_log_keeps_complete_chronological_tail_with_bounded_read_provenance(int codePage)
    {
        string dir = Path.Combine(Path.GetTempPath(), "incident-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "large.log");
            var encoding = System.Text.Encoding.GetEncoding(codePage);
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                byte[] preamble = encoding.GetPreamble();
                file.Write(preamble);
                // FF FE followed by sparse NULs is also the UTF-32LE BOM.
                // A real first text line keeps the requested encoding unambiguous.
                file.Write(encoding.GetBytes("prefix\n"));
                file.Position = 64 * 1024 * 1024;
                using var writer = new StreamWriter(file, encoding);
                writer.Write("\npassword=");
                writer.Write(new string('x', 4 * 1024 * 1024));
                writer.Write("\ndiscarded\nfirst-\u00e9\npassword=tail-secret\nlast\n");
            }
            string zip = Path.Combine(dir, "r.zip");
            Assert.Equal(1, IncidentReport.Write(dir, zip, "resume", maxLinesPerLog: 4));
            using var z = ZipFile.OpenRead(zip);
            using var reader = new StreamReader(z.GetEntry("logs/large.log")!.Open());
            string log = reader.ReadToEnd();
            string[] lines = log.Split('\n');
            Assert.Equal(5, lines.Length);
            Assert.Equal("first-\u00e9", lines[0]);
            Assert.DoesNotContain("tail-secret", log);
            Assert.DoesNotContain("discarded", log);
            Assert.Equal("last", lines[2]);
            Assert.Equal(string.Empty, lines[3]);
            Assert.Equal(string.Empty, lines[4]);
            using var manifestReader = new StreamReader(z.GetEntry("manifeste.json")!.Open());
            using var doc = System.Text.Json.JsonDocument.Parse(manifestReader.ReadToEnd());
            var item = doc.RootElement.GetProperty("fichiers").EnumerateArray()
                .Single(e => e.GetProperty("nom").GetString() == "logs/large.log");
            long sourceBytes = item.GetProperty("octetsSource").GetInt64();
            long readStart = item.GetProperty("debutLecture").GetInt64();
            Assert.Equal(new FileInfo(path).Length, sourceBytes);
            Assert.True(readStart > 0);
            Assert.InRange(sourceBytes - readStart, 1L, 4L * 1024 * 1024);
            Assert.Equal(4, item.GetProperty("lignes").GetInt32());
            Assert.Equal(1, item.GetProperty("lignesMasquees").GetInt32());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Oversized_unterminated_line_is_not_exported_as_an_unkeyed_secret_fragment()
    {
        string dir = Path.Combine(Path.GetTempPath(), "incident-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "long.log"), "password=" + new string('x', 4 * 1024 * 1024));
            string zip = Path.Combine(dir, "r.zip");
            Assert.Equal(1, IncidentReport.Write(dir, zip, "resume"));
            using var z = ZipFile.OpenRead(zip);
            Assert.Equal(0, z.GetEntry("logs/long.log")!.Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Missing_log_folder_still_writes_the_summary()
    {
        string zip = Path.Combine(Path.GetTempPath(), "incident-" + Path.GetRandomFileName() + ".zip");
        try { Assert.Equal(0, IncidentReport.Write(Path.Combine(Path.GetTempPath(), "nope-" + Path.GetRandomFileName()), zip, "x")); }
        finally { File.Delete(zip); }
    }
}
