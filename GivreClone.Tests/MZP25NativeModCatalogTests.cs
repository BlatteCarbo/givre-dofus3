using System.Security.Cryptography;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class MZP25NativeModCatalogTests
{
    [Fact]
    public void Missing_bundle_never_claims_a_native_mod_is_ready()
    {
        var result = MZP25NativeModCatalog.Inspect(
            Path.Combine(Path.GetTempPath(), "mzp25-not-present-" + Guid.NewGuid()));
        Assert.Equal(8, result.Count);
        Assert.All(result, r => Assert.False(r.Pinned));
    }

    [Fact]
    public void Source_built_hook_is_pinned_to_a_non_original_digest()
    {
        Assert.Equal(64, MZP25NativeModCatalog.OwnedCaptureHook.Sha256.Length);
        Assert.False(string.Equals(
            "4F94B008B13CED359D27B976DDF489CDD92E0F3BDEBF7E2BA29B7ED0F7D067B1",
            MZP25NativeModCatalog.OwnedCaptureHook.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Only_an_exact_hash_match_is_reported_as_pinned()
    {
        var root = Path.Combine(Path.GetTempPath(), "mzp25-mod-catalog-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "module.dll");
            var contents = new byte[] { 12, 25, 42 };
            File.WriteAllBytes(path, contents);
            var sha = Convert.ToHexString(SHA256.HashData(contents));
            var spec = new MZP25NativeModCatalog.ModFile("module.dll", sha);
            Assert.True(MZP25NativeModCatalog.CheckFile(root, spec).Pinned);
            File.WriteAllBytes(path, new byte[] { 12, 25, 41 });
            Assert.False(MZP25NativeModCatalog.CheckFile(root, spec).Pinned);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("/absolute.dll")]
    [InlineData("x/../../escape.dll")]
    [InlineData("x\\..\\escape.dll")]
    public void Paths_outside_bundle_are_rejected(string relative)
    {
        Assert.False(MZP25NativeModCatalog.CheckFile(Path.GetTempPath(),
            new MZP25NativeModCatalog.ModFile(relative, "x")).Pinned);
    }
}
