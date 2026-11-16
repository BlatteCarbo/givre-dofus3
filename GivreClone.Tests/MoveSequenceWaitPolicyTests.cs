extern alias F06App;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.App.Services.CombatParity;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using CombatProfile = F06App::GivreClone.App.Services.CombatProfile;
using CombatSpeed = F06App::GivreClone.App.Services.CombatSpeed;
using Xunit;

namespace GivreClone.Tests;

public class MoveSequenceWaitPolicyTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IDictionary Map(LaunchService s, string name) => (IDictionary)typeof(LaunchService).GetField(name, Private)!.GetValue(s)!;
    private static LaunchService Service(int depth, bool startedInSequence = false)
    {
        var s = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        foreach (string name in new[] { "_isqCount", "_seqArmIsq", "_turboSeqDepth", "_turnStartedInSequence", "_jzcSeen", "_myTurnOpen", "_lastFightPkt", "_combat" })
        {
            var f = typeof(LaunchService).GetField(name, Private)!;
            f.SetValue(s, Activator.CreateInstance(f.FieldType));
        }
        Map(s, "_isqCount")[(uint)1] = 10;
        Map(s, "_seqArmIsq")[(uint)1] = 10;
        Map(s, "_turboSeqDepth")[(uint)1] = depth;
        Map(s, "_turnStartedInSequence")[(uint)1] = startedInSequence;
        Map(s, "_jzcSeen")[(uint)1] = true;
        Map(s, "_myTurnOpen")[(uint)1] = true;
        Map(s, "_combat")[(uint)1] = new CombatProfile { Speed = CombatSpeed.Turbo };
        return s;
    }
    private static Task Wait(LaunchService s) => (Task)typeof(LaunchService).GetMethod("WaitMoveClear", Private)!.Invoke(s, [(uint)1, 0, 5000])!;

    [Fact]
    public void Real_wait_returns_synchronously_for_already_closed_turn_sequence()
        => Assert.True(Wait(Service(0)).IsCompletedSuccessfully);

    [Fact]
    public async Task Real_wait_still_blocks_until_an_open_sequence_finishes()
    {
        var s = Service(1);
        Task wait = Wait(s);
        Assert.False(wait.IsCompleted);
        Map(s, "_turboSeqDepth")[(uint)1] = 0;
        Map(s, "_isqCount")[(uint)1] = 11;
        await wait.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Turn_resynchronization_must_not_hide_a_sequence_that_started_before_the_turn_event()
    {
        var s = Service(0, startedInSequence: true);
        Task wait = Wait(s);
        Assert.False(wait.IsCompleted);
        Map(s, "_isqCount")[(uint)1] = 11;
        await wait.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Closed_sequence_before_turn_does_not_require_an_invented_extra_end()
        => Assert.True(MoveSequenceWaitPolicy.CanCheckQuiet(10, 11, true, true, true, 0));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(null)]
    public void Open_or_unknown_depth_keeps_the_existing_wait(int? depth)
        => Assert.False(MoveSequenceWaitPolicy.CanCheckQuiet(10, 11, true, true, true, depth));

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void Shortcut_requires_turbo_and_authoritative_own_turn(bool turbo, bool authority, bool own)
        => Assert.False(MoveSequenceWaitPolicy.CanCheckQuiet(10, 11, turbo, authority, own, 0));

    [Fact]
    public void Existing_completed_sequence_and_missing_mapping_fallback_are_preserved()
    {
        Assert.True(MoveSequenceWaitPolicy.CanCheckQuiet(11, 11, false, false, false, null));
        Assert.True(MoveSequenceWaitPolicy.CanCheckQuiet(0, 1, false, false, false, null));
    }
}
