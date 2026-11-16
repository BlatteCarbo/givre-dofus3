using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GivreClone.App.Services.ScriptCatalog;
using Xunit;

namespace GivreClone.Tests;

public sealed class ScriptCatalogSecurityTests
{
    [Fact]
    public void Valid_observation_manifest_can_be_previewed_but_never_executed()
    {
        using var fixture = new CatalogFixture();
        fixture.Put(capabilities: ["map.read", "ui.overlay"]);
        var review = fixture.Catalog.Inspect("demo");

        Assert.Equal(ScriptReviewStatus.VerifiedForPreview, review.Status);
        Assert.True(review.CanPreview);
        Assert.False(review.CanExecute);
        Assert.Contains("print('hello')", review.Preview);
        Assert.All(review.Capabilities, c => Assert.Equal(ScriptCapabilityRisk.Observation, c.Risk));
        Assert.Single(fixture.Catalog.Scan());
    }

    [Theory]
    [InlineData("../outside.lua")]
    [InlineData("sub/../../outside.lua")]
    [InlineData("sub/../main.lua")]
    [InlineData("C:\\outside.lua")]
    [InlineData("/tmp/outside.lua")]
    [InlineData("sub\\main.lua")]
    [InlineData("main.lua:evil")]
    [InlineData("./main.lua")]
    [InlineData("CON.lua")]
    [InlineData("COM1.lua")]
    [InlineData("nested./main.lua")]
    public void Entrypoint_path_traversal_or_alternate_data_stream_is_rejected(string entry)
    {
        using var fixture = new CatalogFixture();
        fixture.Put(entryPoint: entry);

        var review = fixture.Catalog.Inspect("demo");
        Assert.Equal(ScriptReviewStatus.Invalid, review.Status);
        Assert.Null(review.Preview);
        Assert.Contains(review.Findings, f => f.Contains("Unsafe Lua entry", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../demo")]
    [InlineData("other/demo")]
    [InlineData("C:\\temp")]
    [InlineData("DEMO")]
    [InlineData("nul")]
    [InlineData("")]
    public void Package_id_cannot_escape_root(string packageId)
    {
        using var fixture = new CatalogFixture();
        Assert.Equal(ScriptReviewStatus.Invalid, fixture.Catalog.Inspect(packageId).Status);
    }

    [Fact]
    public void Changed_file_is_never_previewed_when_hash_does_not_match()
    {
        using var fixture = new CatalogFixture();
        fixture.Put();
        File.WriteAllText(Path.Combine(fixture.Root, "demo", "main.lua"), "print('TAMPERED')");

        var report = fixture.Catalog.Inspect("demo");
        Assert.Equal(ScriptReviewStatus.Invalid, report.Status);
        Assert.Null(report.Preview);
        Assert.Contains(report.Findings, f => f.Contains("SHA-256 mismatch", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("client.inject")]
    [InlineData("runtime.native")]
    [InlineData("process.launch")]
    [InlineData("filesystem.write")]
    [InlineData("unknown.surprise")]
    public void Dangerous_or_unrecognized_capability_is_blocked(string capability)
    {
        using var fixture = new CatalogFixture();
        fixture.Put(capabilities: ["map.read", capability]);

        var report = fixture.Catalog.Inspect("demo");
        Assert.Equal(ScriptReviewStatus.Blocked, report.Status);
        Assert.False(report.CanPreview);
        Assert.False(report.CanExecute);
        Assert.Null(report.Preview);
        Assert.Contains(report.Capabilities, c => c.Name == capability && c.Risk == ScriptCapabilityRisk.Denied);
    }

    [Fact]
    public void Mutating_capability_requires_a_separate_grant()
    {
        using var fixture = new CatalogFixture();
        fixture.Put(capabilities: ["movement.request"]);
        var report = fixture.Catalog.Inspect("demo");

        Assert.Equal(ScriptReviewStatus.NeedsPermissionReview, report.Status);
        Assert.True(report.CanPreview);
        Assert.False(report.CanExecute);
        Assert.Contains(report.Capabilities, c => c.Risk == ScriptCapabilityRisk.RequiresGrant);
    }

    [Fact]
    public void Unsupported_api_or_schema_versions_are_rejected()
    {
        using var fixture = new CatalogFixture();
        fixture.Put(apiVersion: "2.0");
        Assert.Equal(ScriptReviewStatus.Invalid, fixture.Catalog.Inspect("demo").Status);
        fixture.Put(schemaVersion: 2);
        Assert.Equal(ScriptReviewStatus.Invalid, fixture.Catalog.Inspect("demo").Status);
    }

    [Fact]
    public void Duplicate_fields_and_duplicate_capabilities_are_rejected()
    {
        using var fixture = new CatalogFixture();
        fixture.Put(capabilities: ["map.read", "map.read"]);
        Assert.Equal(ScriptReviewStatus.Invalid, fixture.Catalog.Inspect("demo").Status);

        fixture.Put();
        string manifestPath = Path.Combine(fixture.Root, "demo", "manifest.json");
        string json = File.ReadAllText(manifestPath);
        File.WriteAllText(manifestPath, json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal));
        Assert.Equal(ScriptReviewStatus.Invalid, fixture.Catalog.Inspect("demo").Status);
    }

    [Fact]
    public void Oversized_script_is_rejected_before_preview()
    {
        using var fixture = new CatalogFixture();
        fixture.Put();
        File.WriteAllBytes(Path.Combine(fixture.Root, "demo", "main.lua"), new byte[LocalScriptCatalog.MaxLuaBytes + 1]);
        Assert.Equal(ScriptReviewStatus.Invalid, fixture.Catalog.Inspect("demo").Status);
    }

    [Fact]
    public void A_link_to_a_file_outside_the_package_is_rejected_when_supported()
    {
        using var fixture = new CatalogFixture();
        fixture.Put();
        string entryPath = Path.Combine(fixture.Root, "demo", "main.lua");
        string outside = Path.Combine(fixture.Root, "outside.lua");
        File.WriteAllText(outside, "print('outside')");
        File.Delete(entryPath);
        try { File.CreateSymbolicLink(entryPath, outside); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Some Windows build agents do not permit symlink creation.
            return;
        }

        var report = fixture.Catalog.Inspect("demo");
        Assert.Equal(ScriptReviewStatus.Invalid, report.Status);
        Assert.False(report.CanPreview);
    }

    private sealed class CatalogFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "givre-catalog-" + Guid.NewGuid().ToString("N"));
        public LocalScriptCatalog Catalog { get; }

        public CatalogFixture()
        {
            Directory.CreateDirectory(Root);
            Catalog = new LocalScriptCatalog(Root);
        }

        public void Put(string entryPoint = "main.lua", string[]? capabilities = null,
            string apiVersion = GivreScriptApiV1.Version, int schemaVersion = LocalScriptCatalog.SchemaVersion)
        {
            const string lua = "print('hello')";
            string packageDir = Path.Combine(Root, "demo");
            Directory.CreateDirectory(packageDir);
            File.WriteAllText(Path.Combine(packageDir, "main.lua"), lua);
            var manifest = new ScriptPackageManifest
            {
                SchemaVersion = schemaVersion,
                Id = "demo",
                Version = "1.0.0",
                Name = "Demo",
                EntryPoint = entryPoint,
                ApiVersion = apiVersion,
                Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lua))),
                Capabilities = capabilities ?? ["map.read"]
            };
            File.WriteAllText(Path.Combine(packageDir, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
