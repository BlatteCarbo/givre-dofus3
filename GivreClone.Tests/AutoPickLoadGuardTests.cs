using GivreClone.Engine;
using Xunit;

namespace GivreClone.Tests;

public sealed class AutoPickLoadGuardTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Native_host_and_speedhack_wait_for_the_map(bool mapLoaded, bool hold)
        => Assert.Equal(hold, CharacterLoadGuard.HoldNativeUntilMap(mapLoaded));

    [Theory]
    [InlineData(null, null, false, 12_000, false)]
    [InlineData(0L, null, false, 11_999, false)]
    [InlineData(0L, null, false, 12_000, true)]
    [InlineData(0L, 2_200L, false, 2_200 + 19_999, false)]
    [InlineData(0L, 2_200L, false, 2_200 + 20_000, true)]
    [InlineData(0L, 2_200L, true, 2_200 + 20_000, false)]
    [InlineData(null, 2_200L, false, 60_000, false)]
    public void Load_stall_starts_at_selection_and_waits_out_context_creation(
        long? selectedAt, long? contextAt, bool mapLoaded, long now, bool stall)
        => Assert.Equal(stall, CharacterLoadWatch.DeclareStall(selectedAt, contextAt, mapLoaded, now));

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, true, false)]
    public void Client_selection_or_a_busy_native_host_suppresses_the_inject(
        bool inFlight, bool alreadyPlaying, bool nativeBusy, bool send)
        => Assert.Equal(send, CharacterAutoPick.ShouldInject(inFlight, alreadyPlaying, nativeBusy));

    [Fact]
    public void AutoPick_and_the_profile_push_use_those_guards()
    {
        string autoPick = Source("GivreClone.App/Services/LaunchService.AutoPick.cs");
        string packets = Source("GivreClone.App/Services/LaunchService.Packets.cs");
        string combat = Source("GivreClone.App/Services/LaunchService.Combat.cs");
        Assert.Contains("CharacterAutoPick.ShouldInject", autoPick);
        Assert.Contains("MarkCharacterLoading(pid)", autoPick);
        Assert.Contains("engaged.Host || engaged.SpeedHack", autoPick);
        Assert.Contains("_clientSelectedAt[p.Pid]", packets);
        Assert.Contains("MarkCharacterLoading(p.Pid)", packets);
        Assert.Contains("NoteCharacterContext(p.Pid)", packets);
        Assert.Contains("CharacterLoadGuard.HoldNativeUntilMap", combat);
        Assert.Contains("HoldNativeUntilMap(CurrentMap(pid) is > 0 || _serverMapId.GetValueOrDefault(pid) > 0 || IsInFight(pid))", combat);
        Assert.Contains("RequestState((int)pid, enabled: false, combatSpeedHack: false, itemIds: itemIds, tactical: false,", combat);
        Assert.Contains("RequestState((int)pid, nativeFallback, turboMitm, itemIds:", combat);
    }

    private static string Source(string relative)
        => File.ReadAllText(Path.Combine(MappingScan.RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));
}
