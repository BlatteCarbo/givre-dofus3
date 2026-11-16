extern alias F06App;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using Xunit;

namespace GivreClone.Tests;

public class StrictTeamWaitTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IDictionary State(LaunchService s, string name) => (IDictionary)typeof(LaunchService).GetField(name, Private)!.GetValue(s)!;
    private static LaunchService Service()
    {
        var s = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        foreach (string name in new[] { "_lastMap", "_inFight", "_fightId", "_fightStartAt", "_placementOpen", "_teamFollower", "_challengeChosen", "_challengeGate", "_manualOwner" })
        {
            var f = typeof(LaunchService).GetField(name, Private)!;
            f.SetValue(s, Activator.CreateInstance(f.FieldType));
        }
        s.SetTeamCombatWait(new Dictionary<uint, uint[]> { [1] = [2] });
        State(s, "_lastMap")[(uint)1] = 10L;
        State(s, "_lastMap")[(uint)2] = 20L;
        State(s, "_fightId")[(uint)1] = 500L;
        State(s, "_fightStartAt")[(uint)1] = 42L;
        State(s, "_inFight")[(uint)1] = true;
        State(s, "_placementOpen")[(uint)1] = true;
        return s;
    }
    private static Task<bool> Wait(LaunchService s, CancellationToken ct = default, bool untilJoined = false, int timeout = 0)
        => (Task<bool>)typeof(LaunchService).GetMethod("WaitTeamJoinBeforeChallengeAsync", Private)!
            .Invoke(s, [(uint)1, ct, untilJoined, timeout])!;

    [Theory]
    [InlineData("SendReady")]
    [InlineData("SendChallengeSelect")]
    [InlineData("SendChallengeValidate")]
    [InlineData("SendChallengeRequest")]
    public void Missing_follower_blocks_the_actual_send_before_transport_access(string method)
    {
        var s = Service(); // transport deliberately absent: reaching it would throw
        object[] args = method is "SendReady" or "SendChallengeRequest" ? [(uint)1] : [(uint)1, 8];
        Assert.Equal(false, typeof(LaunchService).GetMethod(method, Private)!.Invoke(s, args));
    }

    [Fact]
    public async Task Timeout_is_failure_until_all_expected_followers_join_the_same_fight()
    {
        var s = Service();
        Assert.False(await Wait(s));
        State(s, "_inFight")[(uint)2] = true;
        State(s, "_fightId")[(uint)2] = 499L;
        Assert.False(await Wait(s));
        State(s, "_fightId")[(uint)2] = 500L;
        Assert.True(await Wait(s));
    }

    [Fact]
    public async Task Automatic_wait_resumes_when_the_missing_member_leaves_the_expected_team()
    {
        var s = Service();
        Task<bool> wait = Wait(s, untilJoined: true);
        Assert.False(wait.IsCompleted);
        s.SetTeamCombatWait(new Dictionary<uint, uint[]>());
        Assert.True(await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Old_wait_cannot_resume_in_a_replacement_fight()
    {
        var s = Service();
        Task<bool> wait = Wait(s, untilJoined: true);
        Assert.False(wait.IsCompleted);
        State(s, "_fightStartAt")[(uint)1] = 43L;
        Assert.False(await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Script_stop_cancels_the_placement_wait()
    {
        var s = Service();
        using var stop = new CancellationTokenSource();
        Task<bool> wait = Wait(s, stop.Token, untilJoined: true);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }

    [Fact]
    public void Late_challenge_cleanup_cannot_release_the_new_fights_gate_or_remove_its_choice()
    {
        var s = Service();
        State(s, "_fightStartAt")[(uint)1] = 43L;
        State(s, "_challengeChosen")[(uint)1] = 8;
        var gate = new TaskCompletionSource<bool>();
        State(s, "_challengeGate")[(uint)1] = gate;
        typeof(LaunchService).GetMethod("FinishChallengeSelection", Private)!.Invoke(s, [(uint)1, 42L, 8, false]);
        Assert.Equal(8, State(s, "_challengeChosen")[(uint)1]);
        Assert.False(gate.Task.IsCompleted);
    }

    [Fact]
    public void Missing_follower_also_blocks_closing_the_fight_before_transport_access()
    {
        var s = Service();
        Assert.Equal(false, typeof(LaunchService).GetMethod("ToggleFightOptionOn", Private)!.Invoke(s, [(uint)1, 2]));
    }

    [Fact]
    public async Task Automatic_wait_resumes_after_the_follower_joins_the_same_fight()
    {
        var s = Service();
        Task<bool> wait = Wait(s, untilJoined: true);
        Assert.False(wait.IsCompleted);
        State(s, "_inFight")[(uint)2] = true;
        State(s, "_fightId")[(uint)2] = 500L;
        Assert.True(await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Manual_takeover_releases_team_wait_without_authorizing_ready_or_challenge()
    {
        var s = Service();
        Task<bool> wait = Wait(s, untilJoined: true);
        State(s, "_manualOwner")[1u] = new object();
        Assert.False(await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Server_end_of_placement_releases_the_wait_without_authorizing_an_action()
    {
        var s = Service();
        Task<bool> wait = Wait(s, untilJoined: true);
        State(s, "_placementOpen").Remove((uint)1);
        // The awaited continuation uses the shared ThreadPool; on a heavily
        // parallel full-suite run a 5s scheduler delay is not an authorization
        // failure. Preserve the false-only assertion and finite deadline.
        Assert.False(await wait.WaitAsync(TimeSpan.FromSeconds(15)));
    }
}
