using System;
using System.IO;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class MapCacheManifestTests
{
    [Fact]
    public void IncompleteExtractionNeverCountsAsWarmEvenWithMatchingBundleList()
    {
        var manifest = new MapCacheManifest { Version = 4, MapCount = 80, Complete = true };
        Assert.False(manifest.Covers(100, 80));
        Assert.False(manifest.Covers(100, 100));
        manifest.MapCount = 100;
        Assert.False(manifest.Covers(100, 80)); // un fichier .mbin manquant ou corrompu
        manifest.Complete = false;
        Assert.False(manifest.Covers(100, 100));
        manifest.Complete = true;
        Assert.True(manifest.Covers(100, 100)); // seulement après reprise et validation complète
    }

    [Fact]
    public void FingerprintDetectsBytesChangedWithoutLengthOrTimestampChange()
    {
        string file = Path.Combine(Path.GetTempPath(), "givre-map-hash-" + Guid.NewGuid().ToString("N") + ".bundle");
        try
        {
            File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4, 5, 6 });
            var before = MapCacheBundle.Inspect(file);
            File.WriteAllBytes(file, new byte[] { 1, 2, 99, 4, 5, 6 });
            File.SetLastWriteTimeUtc(file, new DateTime(before.MTimeUtcTicks, DateTimeKind.Utc));
            var after = MapCacheBundle.Inspect(file);
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.Length, after.Length);
            Assert.Equal(before.MTimeUtcTicks, after.MTimeUtcTicks);
            Assert.NotEqual(before.Sha256, after.Sha256);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }
}
