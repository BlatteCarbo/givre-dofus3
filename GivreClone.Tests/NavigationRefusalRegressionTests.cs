extern alias F06App;

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using BotWorldStore = F06App::GivreClone.App.Services.BotWorldStore;
using Xunit;

namespace GivreClone.Tests;

// Vrai service et vraies souscriptions du store, sans constructeur : aucun
// launcher, port, compte, pipe, application Avalonia ni transport n'est ouvert.
public sealed class NavigationRefusalRegressionTests
{
    private const uint Pid = 0x40000001;
    private const long Map = 88086276;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static LaunchService OfflineService()
    {
        var service = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        foreach (var name in new[] { "_moveGuardUntil", "_botMoveConfirm", "_pendingMapChange",
            "_pendingHarvest", "_pendingAttack", "_inFight", "_moveRefused", "_movingUntil", "_moveWait" })
        {
            var field = typeof(LaunchService).GetField(name, Private)!;
            field.SetValue(service, Activator.CreateInstance(field.FieldType));
        }
        return service;
    }

    private static void Refuse(LaunchService service, (int X, int Y, int Cell)? position)
        => typeof(LaunchService).GetMethod("ApplyMovementRefusal", Private)!.Invoke(service, [Pid, position]);

    [Fact]
    public async Task Refusal_at_exit_is_confirmed_before_the_script_resumes()
    {
        var service = OfflineService();
        var world = new BotWorldStore(service);
        var state = world.Get(Pid);
        state.MapId = Map;
        state.SelfCell = 76; // destination prédite de la récolte précédente
        state.SelfCellMap = Map;
        state.SelfCellConfirmed = false;
        var wait = service.SendAndWaitAsync(Pid, () => true, 30000);
        bool correctionObserved = false;
        service.SelfMoved += (_, _) => Assert.False(wait.IsCompleted);
        service.SelfPositionCorrected += (_, _) =>
        {
            Assert.False(wait.IsCompleted);
            correctionObserved = state.SelfCell == 543 && state.SelfCellConfirmed;
        };

        Refuse(service, (30, -8, 543)); // journal 15:40:21.473, sortie 543 à +52 ms

        Assert.Equal(LaunchService.MoveOutcome.Refused, await wait);
        Assert.True(correctionObserved);
        Assert.True(state.SelfReadyFor(Map));
        Assert.True(state.SelfCellConfirmed);
        Assert.Equal(543, state.SelfCell);
        Assert.Equal(1, service.MoveRefusedCount(Pid));
    }

    [Fact]
    public async Task Unreadable_refusal_is_counted_and_clears_arrival_actions_without_inventing_a_position()
    {
        var service = OfflineService();
        var world = new BotWorldStore(service);
        var state = world.Get(Pid);
        state.MapId = Map;
        state.SelfCell = 76;
        state.SelfCellMap = Map;
        ((IDictionary)typeof(LaunchService).GetField("_pendingMapChange", Private)!.GetValue(service)!)[Pid]
            = (88086275, Environment.TickCount64, default(CancellationToken));
        ((IDictionary)typeof(LaunchService).GetField("_pendingHarvest", Private)!.GetValue(service)!)[Pid]
            = (1, 2, true, default(CancellationToken));
        ((IDictionary)typeof(LaunchService).GetField("_pendingAttack", Private)!.GetValue(service)!)[Pid]
            = (3L, (string?)null, default(CancellationToken), Map,
                (Func<bool>)(() => true), Environment.TickCount64, 100L, 543,
                (Func<int, bool>)(_ => true),
                (Func<int, Func<bool>, bool>)((_, submit) => submit()));
        ((IDictionary)typeof(LaunchService).GetField("_movingUntil", Private)!.GetValue(service)!)[Pid]
            = Environment.TickCount64 + 6000;
        var wait = service.SendAndWaitAsync(Pid, () => true, 30000);
        service.ActivityChanged += _ =>
        {
            Assert.False(wait.IsCompleted);
            Assert.False(service.HasPendingMapChange(Pid));
            Assert.Equal(1, service.MoveRefusedCount(Pid));
        };
        Refuse(service, null);
        Assert.Equal(LaunchService.MoveOutcome.Refused, await wait);
        Assert.Equal(1, service.MoveRefusedCount(Pid));
        Assert.False(service.HasPendingMapChange(Pid));
        foreach (var name in new[] { "_pendingHarvest", "_pendingAttack", "_movingUntil" })
            Assert.False(((IDictionary)typeof(LaunchService).GetField(name, Private)!.GetValue(service)!).Contains(Pid));
        Assert.False(state.SelfCellConfirmed);
        Assert.Equal(76, state.SelfCell);
    }
}
