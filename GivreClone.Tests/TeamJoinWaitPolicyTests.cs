extern alias F06App;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using Stores = F06App::GivreClone.App.Services.Stores;
using Xunit;

namespace GivreClone.Tests;

// These production-policy fixtures temporarily mutate Stores.World (a static
// singleton shared by otherwise unrelated xUnit collections). Running them
// against other world/session tests can inject synthetic follower progress or
// delay the 8-second idle assertion; isolation preserves the real timeout and
// assertions rather than relaxing their safety bounds.
[CollectionDefinition("TeamJoinWaitPolicy-IsolatedWorld", DisableParallelization = true)]
public sealed class TeamJoinWaitPolicyIsolationCollection { }

[Collection("TeamJoinWaitPolicy-IsolatedWorld")]
public class TeamJoinWaitPolicyTests
{
    [Fact]
    public void Progressing_at_8_seconds_keeps_waiting()
    {
        // Fact 1: follower still progressing at 8 s -> true (keep waiting)
        bool keepWaiting = LaunchService.KeepWaitingForFollowers(elapsedMs: 8000, msSinceLastProgress: 0, allPlaced: false);
        Assert.True(keepWaiting);
    }

    [Fact]
    public void All_placed_at_9_seconds_stops_waiting()
    {
        // Fact 2: all followers placed at 9 s -> false (stop waiting immediately)
        bool keepWaiting = LaunchService.KeepWaitingForFollowers(elapsedMs: 9000, msSinceLastProgress: 0, allPlaced: true);
        Assert.False(keepWaiting);
    }

    [Fact]
    public void No_progress_for_8_seconds_stops_waiting()
    {
        // Fact 3: no progress for 8 s -> false (stop waiting)
        bool keepWaiting = LaunchService.KeepWaitingForFollowers(elapsedMs: 8000, msSinceLastProgress: 8000, allPlaced: false);
        Assert.False(keepWaiting);
    }

    [Fact]
    public void Elapsed_20_seconds_while_progressing_stops_waiting()
    {
        // Fact 4: elapsed 20 s while progressing -> false (hard cap reached, stop waiting)
        bool keepWaiting = LaunchService.KeepWaitingForFollowers(elapsedMs: 20000, msSinceLastProgress: 0, allPlaced: false);
        Assert.False(keepWaiting);
    }

    // ===== TASK 23 — RÉGRESSION CONNECTÉE de l'attente d'équipe de PRODUCTION ============================
    // Fixture : un VRAI LaunchService (état injecté par réflexion, même technique que StrictTeamWaitTests) :
    // le chef (pid 1) est en combat, placement ouvert ; le suiveur attendu (pid 2) est sur une AUTRE carte et
    // n'arrive pas. L'attente de PRODUCTION (WaitTeamJoinBeforeChallengeAsync, untilJoined: true) doit être
    // BORNÉE par la politique de progrès (8 s sans progrès, plafond dur 20 s) au lieu d'attendre indéfiniment.

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static IDictionary State(LaunchService s, string name)
        => (IDictionary)typeof(LaunchService).GetField(name, Private)!.GetValue(s)!;

    private static LaunchService LeaderWithMissingFollower(uint followerPid = 2)
    {
        var s = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        foreach (string name in new[] { "_lastMap", "_inFight", "_fightId", "_fightStartAt", "_placementOpen", "_teamFollower", "_manualOwner", "_fighters", "_scriptReadySent" })
        {
            var f = typeof(LaunchService).GetField(name, Private)!;
            f.SetValue(s, Activator.CreateInstance(f.FieldType));
        }
        s.SetTeamCombatWait(new Dictionary<uint, uint[]> { [1] = [followerPid] });   // le chef 1 attend ce suiveur
        State(s, "_lastMap")[(uint)1] = 10L;              // le chef est en combat, sur sa carte
        State(s, "_lastMap")[followerPid] = 20L;          // le suiveur attendu est encore sur une autre carte
        State(s, "_fightId")[(uint)1] = 500L;
        State(s, "_fightStartAt")[(uint)1] = 42L;
        State(s, "_inFight")[(uint)1] = true;
        State(s, "_placementOpen")[(uint)1] = true;
        Stores.World.Get(followerPid).SelfCell = null;    // état observé GLOBAL isolé : le suiveur ne bouge pas par défaut
        return s;
    }

    // L'entrée de PRODUCTION réellement appelée par RunPreFight (et par SnowPlacement / LotE / script) :
    // WaitTeamJoinBeforeChallengeAsync(pid, ct, untilJoined: true, timeoutMs).
    private static Task<bool> ProductionWait(LaunchService s, CancellationToken ct = default)
        => (Task<bool>)typeof(LaunchService).GetMethod("WaitTeamJoinBeforeChallengeAsync", Private)!
            .Invoke(s, [(uint)1, ct, true, 0])!;

    [Fact]
    public async Task Production_wait_stops_when_the_expected_follower_never_arrives()
    {
        var s = LeaderWithMissingFollower();
        long startedAt = Environment.TickCount64;
        bool joined = await ProductionWait(s).WaitAsync(TimeSpan.FromSeconds(25));
        long elapsed = Environment.TickCount64 - startedAt;
        Assert.False(joined);                       // le suiveur n'est jamais arrivé : l'attente EXPIRE
        Assert.InRange(elapsed, 7000L, 15000L);    // arrêt sur la fenêtre SANS PROGRÈS (8 s), pas d'attente infinie
    }

    [Fact]
    public async Task A_progressing_follower_keeps_the_production_wait_alive_until_the_hard_cap()
    {
        var s = LeaderWithMissingFollower();
        using var cts = new CancellationTokenSource();
        var pump = Task.Run(async () =>
        {
            long map = 1000;
            while (!cts.IsCancellationRequested)
            {
                map++;                                        // le suiveur PROGRESSE (sa carte change à chaque tour)
                State(s, "_lastMap")[(uint)2] = map;
                await Task.Delay(100).ConfigureAwait(false);
            }
        });
        try
        {
            long startedAt = Environment.TickCount64;
            bool joined = await ProductionWait(s).WaitAsync(TimeSpan.FromSeconds(30));
            long elapsed = Environment.TickCount64 - startedAt;
            Assert.False(joined);                            // toujours pas arrivé : l'attente expire quand même
            Assert.InRange(elapsed, 19000L, 30000L);           // le PLAFOND DUR de 20 s tranche (pas la fenêtre de 8 s)
        }
        finally
        {
            cts.Cancel();
            await pump.ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Real_follower_cell_movement_within_the_same_map_refreshes_the_idle_deadline()
    {
        const uint followerPid = 2302;                 // pid isolé : aucun autre test ne touche cet état GLOBAL
        var s = LeaderWithMissingFollower(followerPid);
        var follower = Stores.World.Get(followerPid);
        follower.SelfCell = 100;                       // le suiveur est sur SA carte (20), hors combat
        using var cts = new CancellationTokenSource();
        var pump = Task.Run(async () =>
        {
            int cell = 100;
            while (!cts.IsCancellationRequested)
            {
                // The production loop samples every 200 ms. A two-cell toggle every
                // 100 ms can always return to the same cell between observations.
                cell = (cell + 1) % 560;
                follower.SelfCell = cell;
                await Task.Delay(100).ConfigureAwait(false);
            }
        });
        try
        {
            long startedAt = Environment.TickCount64;
            bool joined = await ProductionWait(s).WaitAsync(TimeSpan.FromSeconds(30));
            long elapsed = Environment.TickCount64 - startedAt;
            Assert.False(joined);                            // toujours pas arrivé : l'attente expire quand même
            Assert.InRange(elapsed, 19000L, 30000L);           // sans la cellule : 8 s ; avec elle : PLAFOND DUR de 20 s
        }
        finally
        {
            cts.Cancel();
            await pump.ConfigureAwait(false);
            follower.SelfCell = null;
        }
    }

    [Fact]
    public async Task The_production_wait_stops_as_soon_as_the_placement_identity_is_lost()
    {
        var s = LeaderWithMissingFollower();
        Task<bool> wait = ProductionWait(s);
        State(s, "_fightStartAt")[(uint)1] = 43L;   // le combat a été remplacé -> l'attente n'a plus de sens
        Assert.False(await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Cancellation_interrupts_the_production_team_wait()
    {
        var s = LeaderWithMissingFollower();
        using var cts = new CancellationTokenSource();
        Task<bool> wait = ProductionWait(s, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}
