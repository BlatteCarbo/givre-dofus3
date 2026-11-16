extern alias F06App;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Engine.Breeding;
using Xunit;
using BreedingSession = F06App::GivreClone.App.Services.Breeding.BreedingSession;
using BreedingRunState = F06App::GivreClone.App.Services.Breeding.BreedingRunState;
using PaddockOperation = F06App::GivreClone.App.Services.Breeding.PaddockOperation;
using PaddockActionReceipt = F06App::GivreClone.App.Services.Breeding.PaddockActionReceipt;
using BreedingViewModel = F06App::GivreClone.App.ViewModels.BreedingViewModel;

namespace GivreClone.Tests;

public sealed class BreedingTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private static BreedingValue<T> Known<T>(T value) where T : struct => new(value, BreedingValueSource.Observed, At);
    private static BreedingLot Lot(int serenity = -1000, MountFamily family = MountFamily.Dragoturkey, int fuel = 200_000) =>
        new("paddock-7", family, Known(3), Known(serenity), Known(true),
            Known(0), Known(0), Known(0), Known(0),
            Enum.GetValues<BreedingGauge>().ToDictionary(g => g, _ => Known(fuel)));

    [Theory]
    [InlineData(MountFamily.Dragoturkey)]
    [InlineData(MountFamily.Muldo)]
    [InlineData(MountFamily.Volkorne)]
    public void All_mount_families_use_the_reference_batch_rules(MountFamily family)
    {
        var plan = BreedingEngine.NextStage(Lot(family: family), new(100));
        Assert.True(plan.CanStart);
        Assert.Equal(BreedingStageKind.Progression, plan.Stage!.Kind);
        Assert.Equal(new[] { BreedingGauge.Endurance, BreedingGauge.Maturity }, plan.Stage.Gauges);
        Assert.Equal(500, plan.Stage.Cycles);
    }

    [Fact]
    public void Empty_paddock_is_not_a_completed_or_runnable_batch()
    {
        var result = BreedingEngine.NextStage(Lot() with { MountCount = Known(0) }, new(0));
        Assert.False(result.CanStart);
        Assert.False(result.Complete);
        Assert.NotNull(result.BlockedReason);
    }

    [Theory]
    [InlineData(-2001, BreedingGauge.Endurance, BreedingGauge.Xp)]
    [InlineData(-2000, BreedingGauge.Endurance, BreedingGauge.Maturity)]
    [InlineData(-1, BreedingGauge.Endurance, BreedingGauge.Maturity)]
    [InlineData(0, BreedingGauge.Maturity, BreedingGauge.Xp)]
    [InlineData(1, BreedingGauge.Maturity, BreedingGauge.Love)]
    [InlineData(2000, BreedingGauge.Maturity, BreedingGauge.Love)]
    [InlineData(2001, BreedingGauge.Love, BreedingGauge.Xp)]
    public void Serenity_boundaries_select_exactly_the_valid_gauges(int serenity, BreedingGauge first, BreedingGauge second)
    {
        var stage = BreedingEngine.NextStage(Lot(serenity), new(100)).Stage!;
        Assert.Equal(new[] { first, second }, stage.Gauges);
    }

    [Fact]
    public void Nonunified_batch_uses_highest_serenity_then_minus_and_xp()
    {
        var lot = Lot(3000) with { Unified = Known(false) };
        var stage = BreedingEngine.NextStage(lot, new(500)).Stage!;
        Assert.Equal(BreedingStageKind.Unification, stage.Kind);
        Assert.Equal(-5000, stage.SerenityTarget);
        Assert.Equal(8000, stage.Needs[BreedingGauge.SerenityMinus]);
        Assert.Equal(new[] { BreedingGauge.SerenityMinus, BreedingGauge.Xp }, stage.Gauges);
        var predicted = BreedingEngine.Predict(lot, stage, TimeSpan.FromSeconds(stage.Cycles * 10), At);
        Assert.Equal(-5000, predicted.Serenity.Value);
        Assert.True(predicted.Unified.Value);
        Assert.Equal(BreedingValueSource.Predicted, predicted.Unified.Source);
    }

    [Fact]
    public void Minus_boundary_is_inherently_unified_and_complete_target_stays_complete()
    {
        var lot = Lot(-5000) with { Unified = Known(false) };
        Assert.Equal(BreedingStageKind.Progression, BreedingEngine.NextStage(lot, new(0)).Stage!.Kind);
        var complete = lot with { Endurance = Known(20_000), Maturity = Known(20_000), Love = Known(20_000) };
        Assert.True(BreedingEngine.NextStage(complete, new(0)).Complete);
        Assert.Equal(BreedingStageKind.Xp, BreedingEngine.NextStage(complete, new(1)).Stage!.Kind);
    }

    [Fact]
    public void Movement_prefers_intersection_and_uses_interior_margin()
    {
        var lot = Lot(5000) with { Love = Known(20_000) };
        var stage = BreedingEngine.NextStage(lot, new(0)).Stage!;
        Assert.Equal(BreedingStageKind.MoveSerenity, stage.Kind);
        Assert.Equal(-500, stage.SerenityTarget);
        Assert.Equal(5500, stage.Needs[BreedingGauge.SerenityMinus]);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(80000, 10)]
    [InlineData(80001, 20)]
    [InlineData(140000, 20)]
    [InlineData(140001, 30)]
    [InlineData(180000, 30)]
    [InlineData(180001, 40)]
    [InlineData(200000, 40)]
    public void Fuel_tier_thresholds_are_strict(int fuel, int tier) =>
        Assert.Equal(tier, BreedingEngine.FuelTier(fuel));

    [Fact]
    public void Consumption_crosses_tiers_and_respects_partial_cycles_and_empty_fuel()
    {
        var first = BreedingEngine.Consume(80010, 50, 1);
        Assert.Equal(new FuelConsumption(1, 20, 79990, false), first);
        Assert.Equal(new FuelConsumption(4, 50, 79960, false), BreedingEngine.Consume(80010, 50));
        Assert.Equal(new FuelConsumption(0, 0, 0, true), BreedingEngine.Consume(0, 1));
        Assert.Equal(new FuelConsumption(1, 5, 0, true), BreedingEngine.Consume(5, 20));
        Assert.Equal(new FuelConsumption(1, 5, 0, false), BreedingEngine.Consume(5, 5));
    }

    [Fact]
    public void Unknown_and_predicted_values_block_but_manual_values_remain_manual()
    {
        Assert.False(BreedingEngine.NextStage(Lot() with { Serenity = BreedingValue<int>.Unknown }, new(0)).CanStart);
        Assert.False(BreedingEngine.NextStage(Lot() with { Serenity = new(-1000, BreedingValueSource.Predicted, At) }, new(0)).CanStart);
        var manual = Lot() with { Serenity = new(-1000, BreedingValueSource.Manual, At) };
        Assert.True(BreedingEngine.NextStage(manual, new(0)).CanStart);
        Assert.Equal(BreedingValueSource.Manual, manual.Serenity.Source);
    }

    [Fact]
    public void Missing_fuel_is_not_a_default_fill_and_zero_fuel_is_not_unknown()
    {
        var missing = Lot() with { Fuel = new Dictionary<BreedingGauge, BreedingValue<int>>() };
        Assert.Null(BreedingEngine.NextStage(missing, new(0)).Stage);
        var empty = BreedingEngine.NextStage(Lot(fuel: 0), new(0));
        Assert.NotNull(empty.Stage);
        Assert.Equal(0, empty.Stage.Cycles);
        Assert.Equal(2, empty.Stage.EmptyGauges.Count);
        Assert.False(empty.CanStart);
    }

    [Fact]
    public void Shorter_gauge_stops_while_longest_primary_determines_reference_duration()
    {
        var lot = Lot() with { Endurance = Known(19_960) };
        var stage = BreedingEngine.NextStage(lot, new(0)).Stage!;
        Assert.Equal(500, stage.Cycles);
        var result = BreedingEngine.Predict(lot, stage, TimeSpan.FromDays(1), At.AddDays(1));
        Assert.Equal(20_000, result.Endurance.Value);
        Assert.Equal(199_960, result.Fuel[BreedingGauge.Endurance].Value);
        Assert.Equal(20_000, result.Maturity.Value);
    }

    [Fact]
    public void Prediction_uses_whole_cycles_and_preserves_original_observations()
    {
        var lot = Lot();
        var stage = BreedingEngine.NextStage(lot, new(0)).Stage!;
        var before = BreedingEngine.Predict(lot, stage, TimeSpan.FromSeconds(9), At);
        var after = BreedingEngine.Predict(lot, stage, TimeSpan.FromSeconds(10), At.AddSeconds(10));
        Assert.Equal(0, before.Endurance.Value);
        Assert.Equal(40, after.Endurance.Value);
        Assert.Equal(BreedingValueSource.Predicted, after.Endurance.Source);
        Assert.Equal(BreedingValueSource.Observed, lot.Endurance.Source);
        Assert.Equal(0, lot.Endurance.Value);
        Assert.False(BreedingEngine.NextStage(after, new(0)).CanStart);
    }

    [Fact]
    public void Prudent_time_changes_duration_not_simulated_cycles()
    {
        var normal = BreedingEngine.NextStage(Lot(), new(0)).Stage!;
        var prudent = BreedingEngine.NextStage(Lot(), new(0, true)).Stage!;
        Assert.Equal(normal.Cycles, prudent.Cycles);
        Assert.Equal(TimeSpan.FromSeconds(5500), prudent.EstimatedDuration);
    }

    [Fact]
    public void Invalid_numeric_inputs_do_not_become_plausible_predictions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BreedingEngine.Consume(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BreedingEngine.Consume(1, -1));
        Assert.False(BreedingEngine.NextStage(Lot(5001), new(0)).CanStart);
        Assert.False(BreedingEngine.NextStage(Lot() with { Endurance = Known(20_001) }, new(0)).CanStart);
    }

    [Fact]
    public async Task Session_requires_real_binding_and_prediction_never_confirms_completion()
    {
        var unbound = new BreedingSession();
        await Assert.ThrowsAsync<InvalidOperationException>(() => unbound.StartAsync(Lot(), new(0)));
        var actions = new List<PaddockOperation>();
        var session = new BreedingSession((action, _) => {
            actions.Add(action.Operation);
            return Task.FromResult(new PaddockActionReceipt(true, "test fixture receipt"));
        }, () => At);
        await session.StartAsync(Lot(), new(0));
        Assert.Equal(BreedingRunState.Running, session.State);
        session.Progress(At.AddSeconds(10));
        Assert.Equal(40, session.Predicted!.Endurance.Value);
        session.Progress(At.AddDays(1));
        Assert.Equal(1, session.ProgressFraction);
        Assert.Equal(BreedingRunState.AwaitingObservation, session.State);
        Assert.True(session.IsBusy);
        await session.StopAsync();
        Assert.Equal(BreedingRunState.Stopped, session.State);
        Assert.Equal(new[] { PaddockOperation.ActivateGauges, PaddockOperation.DeactivateGauges }, actions);
    }

    [Fact]
    public async Task Stop_during_activation_cancels_then_deactivates_a_late_positive_receipt()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receipt = new TaskCompletionSource<PaddockActionReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actions = new List<PaddockOperation>();
        CancellationToken activationToken = default;
        var session = new BreedingSession((action, token) => {
            actions.Add(action.Operation);
            if (action.Operation == PaddockOperation.DeactivateGauges)
            {
                Assert.False(token.IsCancellationRequested);
                return Task.FromResult(new PaddockActionReceipt(true, "deactivated"));
            }
            activationToken = token;
            entered.SetResult();
            return receipt.Task;
        }, () => At);
        var start = session.StartAsync(Lot(), new(0));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = session.StopAsync();
        Assert.True(activationToken.IsCancellationRequested);
        Assert.False(stop.IsCompleted);
        receipt.SetResult(new(true, "activated before cancel settled"));
        await Task.WhenAll(start, stop).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(BreedingRunState.Stopped, session.State);
        Assert.Equal(new[] { PaddockOperation.ActivateGauges, PaddockOperation.DeactivateGauges }, actions);
    }

    [Fact]
    public async Task Rejected_activation_does_not_run_or_dispatch_fake_stop()
    {
        int calls = 0;
        var session = new BreedingSession((_, _) => {
            calls++;
            return Task.FromResult(new PaddockActionReceipt(false, "not acknowledged"));
        });
        await session.StartAsync(Lot(), new(0));
        Assert.Equal(BreedingRunState.Failed, session.State);
        session.Progress(At.AddDays(1));
        Assert.Null(session.Predicted);
        await session.StopAsync();
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Unconfirmed_stop_stays_busy_and_can_be_retried()
    {
        int stops = 0;
        var session = new BreedingSession((action, _) => Task.FromResult(
            new PaddockActionReceipt(action.Operation == PaddockOperation.ActivateGauges || ++stops > 1, "fixture")));
        await session.StartAsync(Lot(), new(0));
        await session.StopAsync();
        Assert.Equal(BreedingRunState.Failed, session.State);
        Assert.True(session.IsBusy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(Lot(), new(0)));
        await session.StopAsync();
        Assert.False(session.IsBusy);
        Assert.Equal(2, stops);
    }

    [Fact]
    public async Task Viewmodel_exposes_commands_and_separate_observed_predicted_unknown_values()
    {
        var unbound = new BreedingViewModel();
        var partial = Lot() with { Serenity = BreedingValue<int>.Unknown };
        unbound.Load(partial, new(0));
        Assert.Null(unbound.Readings.Single(r => r.Name == "Serenity").Value);
        Assert.False(unbound.StartCommand.CanExecute(null));
        unbound.Load(Lot(), new(0));
        Assert.True(unbound.Plan!.CanStart);
        Assert.False(unbound.CanStart);

        var vm = new BreedingViewModel(new BreedingSession(
            (_, _) => Task.FromResult(new PaddockActionReceipt(true, "fixture")), () => At));
        vm.Load(Lot(), new(0));
        Assert.True(vm.StartCommand.CanExecute(null));
        await vm.StartAsync();
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.True(vm.StopCommand.CanExecute(null));
        vm.Refresh(At.AddSeconds(10));
        Assert.Equal(BreedingValueSource.Observed, vm.Readings.Single(r => r.Name == "Endurance").Source);
        Assert.Equal(BreedingValueSource.Predicted, vm.PredictedReadings.Single(r => r.Name == "Endurance").Source);
        await vm.StopAsync();
        Assert.Equal(0, vm.CurrentLot!.Endurance.Value);
        Assert.False(vm.IsBusy);
    }
}
