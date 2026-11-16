using System.Security.Cryptography;
using System.Text;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class CombatVisualModPolicyTests
{
    [Fact]
    public void Distributed_bundle_passes_preflight_and_missing_or_changed_modules_disable_it()
    {
        string project = Path.Combine(MappingScan.RepoRoot(), "GivreClone.App", "GivreClone.App.csproj");
        var entries = System.Xml.Linq.XDocument.Load(project).Descendants("None")
            .Where(e => ((string?)e.Attribute("Link"))?.StartsWith("NativeTurboRuntime\\", StringComparison.Ordinal) == true)
            .Where(e => !((string)e.Attribute("Link")!).Contains('%'))
            .ToDictionary(e => Path.GetFileName((string)e.Attribute("Link")!),
                e => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, (string)e.Attribute("Include")!)));
        string dir = Path.Combine(Path.GetTempPath(), "givre-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var module in CombatVisualModPolicy.StandaloneNoAnimModules.Concat(CombatVisualOptions.Modules).Distinct())
            {
                Assert.True(entries.TryGetValue(module.Name, out var source), $"Module non livré : {module.Name}");
                File.Copy(source!, Path.Combine(dir, module.Name));
            }
            Assert.True(CombatVisualModPolicy.HasPinnedStandaloneSkipper(dir));
            Assert.True(CombatVisualOptions.Available(dir));
            foreach (var module in CombatVisualOptions.Modules)
            {
                string path = Path.Combine(dir, module.Name);
                var bytes = File.ReadAllBytes(path);
                File.Delete(path);
                Assert.False(CombatVisualOptions.Available(dir));
                File.WriteAllText(path, "altered");
                Assert.False(CombatVisualOptions.Available(dir));
                File.WriteAllBytes(path, bytes);
                Assert.True(CombatVisualOptions.Available(dir));
            }
            File.Delete(Path.Combine(dir, "DofusNativeBootstrap.dll"));
            Assert.False(CombatVisualModPolicy.HasPinnedStandaloneSkipper(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Isolated_clone_never_promises_a_movement_teleport_or_collapse()
    {
        var state = CombatVisualModPolicy.Evaluate(true, true, true, false,
            pinnedSkipperInstalled: true, independentlyVerifiedLegacyRenderer: true);
        Assert.True(state.CanRequestNoAnim);
        Assert.False(state.CanRequestInstantMove);
        Assert.False(state.CanRequestCollapse);
        Assert.False(state.ServerTeleportSupported);
        var effective = CombatVisualModPolicy.Effective(state, true, true, true);
        Assert.Equal((true, false, false), effective);
        Assert.False(CombatVisualModPolicy.CanUseHistoricalMods(true, true));
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Permission_and_mods_switch_fail_closed(bool mods, bool permission, bool skipper)
    {
        var state = CombatVisualModPolicy.Evaluate(true, mods, permission,
            socketOnly: false, pinnedSkipperInstalled: skipper,
            independentlyVerifiedLegacyRenderer: false);
        Assert.False(state.CanRequestInstantMove);
        Assert.False(state.CanRequestCollapse);
        if (!mods || !permission) Assert.False(state.CanRequestNoAnim);
    }

    [Fact]
    public void Socket_has_no_visual_renderer_even_with_pinned_file()
    {
        var state = CombatVisualModPolicy.Evaluate(true, true, true, true, true, true);
        Assert.False(state.CanRequestNoAnim);
        Assert.False(state.CanRequestInstantMove);
    }

    [Fact]
    public void Standalone_sha_preflight_is_exact_and_rejects_wrong_bytes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "givre-native-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string filename = Path.Combine(dir, "module.dll");
            File.WriteAllText(filename, "verified");
            string sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("verified")));
            var manifest = new[] { new CombatVisualModPolicy.Module("module.dll", sha) };
            Assert.True(CombatVisualModPolicy.VerifyModules(dir, manifest));
            File.WriteAllText(filename, "changed");
            Assert.False(CombatVisualModPolicy.VerifyModules(dir, manifest));
            Assert.False(CombatVisualModPolicy.VerifyModules(dir,
                [new CombatVisualModPolicy.Module("../module.dll", sha)]));
            Assert.False(CombatVisualModPolicy.VerifyModules(dir,
                [new CombatVisualModPolicy.Module("module.dll", "invalid")]));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Actual_pinned_manifest_lists_only_four_independent_no_anim_dependencies()
    {
        Assert.Equal(4, CombatVisualModPolicy.StandaloneNoAnimModules.Count);
        Assert.Contains(CombatVisualModPolicy.StandaloneNoAnimModules, m =>
            m.Name == "DofusCombatAnimationSkipper.dll");
        Assert.DoesNotContain(CombatVisualModPolicy.StandaloneNoAnimModules, m =>
            m.Name == "GivreMods.dll");
    }
}
