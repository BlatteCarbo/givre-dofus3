extern alias F06App;
using System.Reflection;
using DofusEnhanced = F06App::GivreClone.App.Services.DofusEnhanced;
using EnhancedVerdict = F06App::GivreClone.App.Services.EnhancedVerdict;
using ClientLightening = F06App::GivreClone.App.Services.ClientLightening;
using Xunit;

namespace GivreClone.Tests;

public sealed class ClientLighteningTests
{
    private const string LiveGame = "ad013415455d93bd865aef70fcf1b7c6f704c799f85e2ef301aa7fa610ef2591";
    private const string LiveMeta = "ad2880162d7e0aee219fdc81e4a3817cc86cca4aba3df3ce86fd470c1bb9a6af";

    private static Dictionary<string, string> Release(string version, string game, string meta) => DofusEnhanced.ParseIni(
        $"format=1\r\nclientVersion={version}\r\nrevision=126\r\ngameHash={game}\r\nmetadataHash={meta}\r\nDofusMiniatures.dll=21e7\r\n");

    [Fact]
    public void Release_for_another_version_or_hash_is_a_mismatch()
    {
        Assert.True(DofusEnhanced.ReleaseMismatch(Release("3.7.12.12", "70b3", LiveMeta), "3.7.13.17", LiveGame, LiveMeta));
        Assert.True(DofusEnhanced.ReleaseMismatch(Release("3.7.13.17", "70b3", LiveMeta), null, LiveGame, LiveMeta));
        Assert.True(DofusEnhanced.ReleaseMismatch(Release("3.7.13.17", LiveGame, "ba1e"), null, LiveGame, LiveMeta));
    }

    [Fact]
    public void Matching_release_is_not_a_mismatch_and_unknown_live_values_prove_nothing()
    {
        Assert.False(DofusEnhanced.ReleaseMismatch(Release("3.7.13.17", LiveGame.ToUpperInvariant(), LiveMeta), "3.7.13.17", LiveGame, LiveMeta));
        Assert.False(DofusEnhanced.ReleaseMismatch(Release("3.7.12.12", "70b3", "ba1e"), null, null, null));
    }

    private const string RealLog =
        "2026-11-7 11:38:12 LOAD_NOT_CONFIRMED no retry for this process pid=11260\n" +
        "NO_COMPATIBLE_RELEASE: cette mise a jour de Dofus attend une version adaptee du mod; jeu laisse intact\n" +
        "UPDATE_CHECK GitHub Altansar69/dofus-enhanced\n" +
        "UPDATE_NO_COMPATIBLE_RELEASE\n" +
        "UPDATE_CHECK_FINISHED\n" +
        "2026-11-7 11:40:25 LOAD_NOT_CONFIRMED no retry for this process pid=35592\r\n" +
        "NO_COMPATIBLE_RELEASE: cette mise a jour de Dofus attend une version adaptee du mod; jeu laisse intact\r\n" +
        "2026-11-7 12:3:59 WATCH_STARTED pre-existing clients ignored\n" +
        "2026-11-1 16:28:47 LOAD_ATTEMPT pid=9708\n" +
        "Etat : READY\n" +
        "2026-11-1 16:28:50 LOAD_READY pid=9708\n";

    [Theory]
    [InlineData(11260, true)]
    [InlineData(35592, true)]
    [InlineData(9708, false)]   // chargé : LOAD_READY
    [InlineData(1126, false)]   // pid inconnu (et pas de collision sur un préfixe)
    public void Autoload_log_decision_is_per_pid(int pid, bool expected)
        => Assert.Equal(expected, DofusEnhanced.AutoloadLogSaysNoCompatible(RealLog, pid));

    [Fact]
    public void Update_check_line_alone_is_not_an_incompatibility()
        => Assert.False(DofusEnhanced.AutoloadLogSaysNoCompatible(
            "2026-11-7 11:40:25 LOAD_ATTEMPT pid=77\nUPDATE_NO_COMPATIBLE_RELEASE\n", 77));

    [Theory]
    // mismatch, logSaysNo, loaded, startupFailed, boundElapsed -> verdict
    [InlineData(false, false, true, false, false, EnhancedVerdict.Owns)]
    [InlineData(true, false, true, false, false, EnhancedVerdict.Owns)]            // déjà chargé et sain : il possède
    [InlineData(true, false, false, false, false, EnhancedVerdict.Incompatible)]   // version/empreinte
    [InlineData(false, true, false, false, false, EnhancedVerdict.Incompatible)]   // NO_COMPATIBLE_RELEASE
    [InlineData(false, false, true, true, false, EnhancedVerdict.Incompatible)]    // DLL chargée mais démarrage refusé
    [InlineData(false, false, false, false, true, EnhancedVerdict.Incompatible)]   // jamais chargé dans le délai
    [InlineData(false, false, false, false, false, EnhancedVerdict.Pending)]
    public void Verdict_table(bool mismatch, bool log, bool loaded, bool startupFailed, bool bound, EnhancedVerdict expected)
        => Assert.Equal(expected, DofusEnhanced.Judge(mismatch, log, loaded, startupFailed, bound));

    [Fact]
    public void Client_marked_incompatible_is_not_owned_even_if_a_module_looks_loaded()
    {
        const int pid = -171107;
        var enhanced = typeof(DofusEnhanced);
        var owns = typeof(ClientLightening).Assembly.GetType("GivreClone.App.Services.NativeTurboRuntime")!
            .GetMethod("EnhancedOwnsHooks", BindingFlags.NonPublic | BindingFlags.Static)!;
        enhanced.GetMethod("MarkIncompatible", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { pid });
        try
        {
            Assert.True((bool)enhanced.GetMethod("IsMarkedIncompatible", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { pid })!);
            Assert.Equal(false, owns.Invoke(null, new object[] { pid }));
        }
        finally
        {
            ((System.Collections.Concurrent.ConcurrentDictionary<int, byte>)enhanced.GetField("IncompatiblePids", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!)
                .TryRemove(pid, out _);
        }
    }

    [Fact]
    public void Bot_settings_lighten_and_never_enable_features_that_act_on_the_game()
    {
        var s = DofusEnhanced.BotSettings(legacy: false);
        foreach (string on in new[] { "noSpellEffects", "shortSpellCasts", "noAttackAnimations", "fastMapTransitions", "miniatureEnemies" })
            Assert.Equal("1", s[on]);
        foreach (string off in new[] { "perfOverlay", "mapTravelPlanner", "chatTravelLinks", "travelRoublardTemple", "soulAutoEquip", "metamobSync", "spouseTeleport" })
            Assert.Equal("0", s[off]);
    }

    [Fact]
    public void Merging_settings_keeps_other_keys_and_comments_and_is_idempotent()
    {
        var values = new Dictionary<string, string> { ["noSpellEffects"] = "1", ["perfOverlay"] = "0", ["newKey"] = "5" };
        string merged = DofusEnhanced.MergeIni("# header\r\nnoSpellEffects=0\r\nperfOverlay=1\r\nsoulPanelX=24\r\n", values);

        Assert.Equal("# header\r\nnoSpellEffects=1\r\nperfOverlay=0\r\nsoulPanelX=24\r\nnewKey=5\r\n", merged);
        Assert.Equal(merged, DofusEnhanced.MergeIni(merged, values));
        Assert.Equal("a=1\n", DofusEnhanced.MergeIni("", new Dictionary<string, string> { ["a"] = "1" }));
    }

    [Fact]
    public void Foreground_client_is_never_throttled_or_trimmed()
    {
        Assert.False(ClientLightening.ShouldThrottle(foreground: true));
        Assert.True(ClientLightening.ShouldThrottle(foreground: false));
        Assert.False(ClientLightening.ShouldTrim(foreground: true, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Background_trim_is_rate_limited_to_once_per_minute()
    {
        Assert.False(ClientLightening.ShouldTrim(false, TimeSpan.Zero));
        Assert.False(ClientLightening.ShouldTrim(false, TimeSpan.FromSeconds(59.9)));
        Assert.True(ClientLightening.ShouldTrim(false, TimeSpan.FromSeconds(60)));
    }
}
