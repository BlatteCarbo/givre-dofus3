using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using GivreClone.App.Services;
using GivreClone.App.Services.ProtocolUpdates;
using Xunit;

namespace GivreClone.Tests;

public sealed class ProtocolObservedDriftTests
{
    private static string Sha(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static ProtocolUpdateReport Report(string version, string digest) =>
        ProtocolUpdateProbe.Compare(version,
            new ProtocolArtifactHashes(Sha("exe:" + digest), Sha("assembly:" + digest),
                Sha("metadata:" + digest), Sha("marker:" + digest)),
            version);

    [Fact]
    public void First_seen_is_only_an_observation_and_changed_digest_remains_flagged()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Givre-observation-" + Guid.NewGuid().ToString("N"));
        string installation = Path.Combine(directory, "game");
        string state = Path.Combine(directory, "observations");
        Directory.CreateDirectory(installation);
        try
        {
            var first = ProtocolObservedDrift.CompareAndRemember(state, installation, Report("3.7.11.11", "v1"));
            Assert.False(first.HasBaseline);
            Assert.True(first.BaselineCreated);
            Assert.False(first.Changed);

            var same = ProtocolObservedDrift.CompareAndRemember(state, installation, Report("3.7.11.11", "v1"));
            Assert.True(same.HasBaseline);
            Assert.False(same.Changed);

            var changed = ProtocolObservedDrift.CompareAndRemember(state, installation, Report("3.7.11.11", "v2"));
            Assert.True(changed.HasBaseline);
            Assert.True(changed.Changed);
            Assert.True(ProtocolObservedDrift.CompareAndRemember(state, installation,
                Report("3.7.11.11", "v2")).Changed); // jamais accepté automatiquement
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Version_drift_is_reported_even_when_binary_hash_matches()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Givre-observation-" + Guid.NewGuid().ToString("N"));
        string installation = Path.Combine(directory, "game");
        Directory.CreateDirectory(installation);
        try
        {
            ProtocolObservedDrift.CompareAndRemember(directory, installation, Report("3.7.11.11", "same"));
            Assert.True(ProtocolObservedDrift.CompareAndRemember(directory, installation,
                Report("3.7.12.12", "same")).Changed);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
