using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class BuildProvenanceTests
{
    [Fact]
    public void Binary_fingerprint_is_the_sha256_of_the_file_on_disk()
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            string expected = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3 })).ToLowerInvariant();
            Assert.Equal(expected, BuildProvenance.FileSha256(path));
        }
        finally { System.IO.File.Delete(path); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Z:/nope/missing.dll")]
    public void Missing_binary_is_reported_unavailable(string? path)
        => Assert.Equal("indisponible", BuildProvenance.FileSha256(path));

    [Fact]
    public void Incident_manifest_carries_the_build_provenance()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "prov-" + System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            string zip = System.IO.Path.Combine(dir, "r.zip");
            IncidentReport.Write(dir, zip, "x");
            using var z = System.IO.Compression.ZipFile.OpenRead(zip);
            using var doc = System.Text.Json.JsonDocument.Parse(new System.IO.StreamReader(z.GetEntry("manifeste.json")!.Open()).ReadToEnd());
            var root = doc.RootElement;
            Assert.Equal(BuildProvenance.FileSha256(typeof(IncidentReport).Assembly.Location), root.GetProperty("empreinteBinaire").GetString());
            Assert.Equal(64, root.GetProperty("empreinteBinaire").GetString()!.Length);
            Assert.Equal(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, root.GetProperty("runtime").GetString());
            Assert.False(string.IsNullOrEmpty(root.GetProperty("systeme").GetString()));
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }
}
