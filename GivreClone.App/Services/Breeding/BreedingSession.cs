using System;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Engine.Breeding;

namespace GivreClone.App.Services.Breeding;

public enum BreedingRunState { Idle, Starting, Running, AwaitingObservation, Stopping, Stopped, Failed }
public enum PaddockOperation { ActivateGauges, DeactivateGauges }
public sealed record PaddockAction(string PaddockId, BreedingStage Stage, PaddockOperation Operation);
public sealed record PaddockActionReceipt(bool Confirmed, string Evidence);

/// <summary>
/// Implement only for verified, session-bound existing actions. Return Confirmed only after a correlated
/// real receipt, never after a write alone. Cancellation must settle after any in-flight side effect is
/// known; if activation happened, return its receipt even when cancellation was requested, so Stop can
/// deactivate. A rejected/failed activation must leave no active gauges.
/// </summary>
public delegate Task<PaddockActionReceipt> PaddockActionExecutor(PaddockAction action, CancellationToken cancellationToken);

/// <summary>
/// One bounded stage, with no background timer or automatic chaining. The owner calls Progress on
/// observed events or its existing UI clock. Elapsed time predicts progress, never confirms completion.
/// All calls belong to the owner UI context; the delegate may await asynchronous protocol receipts.
/// </summary>
public sealed class BreedingSession
{
    private readonly PaddockActionExecutor? _execute;
    private readonly Func<DateTimeOffset> _now;
    private CancellationTokenSource? _startCancellation;
    private Task? _startTask;
    private Task? _stopTask;
    private bool _activated;
    private DateTimeOffset _started;
    private BreedingLot? _initial;
    private BreedingStage? _stage;

    public BreedingSession(PaddockActionExecutor? execute = null, Func<DateTimeOffset>? now = null)
    {
        _execute = execute;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public event Action? Changed;
    public bool HasExecutionBinding => _execute is not null;
    public BreedingRunState State { get; private set; }
    public string Status { get; private set; } = "No verified paddock action binding.";
    public BreedingLot? Predicted { get; private set; }
    public double ProgressFraction { get; private set; }
    public TimeSpan Remaining { get; private set; }
    public bool IsBusy => State is BreedingRunState.Starting or BreedingRunState.Running
        or BreedingRunState.AwaitingObservation or BreedingRunState.Stopping || _activated;

    public Task StartAsync(BreedingLot lot, BreedingTarget target)
    {
        if (IsBusy) throw new InvalidOperationException("Stop the current stage first.");
        if (_execute is null) throw new InvalidOperationException("No verified paddock action binding.");
        var plan = BreedingEngine.NextStage(lot, target);
        if (!plan.CanStart) throw new InvalidOperationException(plan.BlockedReason ?? "Breeding target already reached.");
        _initial = lot;
        _stage = plan.Stage!;
        Predicted = null;
        ProgressFraction = 0;
        Remaining = _stage.EstimatedDuration;
        _stopTask = null;
        _startCancellation?.Dispose();
        _startCancellation = new();
        SetState(BreedingRunState.Starting, "Awaiting gauge activation receipt.");
        _startTask = ActivateAsync(_startCancellation.Token);
        return _startTask;
    }

    private async Task ActivateAsync(CancellationToken token)
    {
        try
        {
            var receipt = await _execute!(new(_initial!.PaddockId, _stage!, PaddockOperation.ActivateGauges), token);
            _activated = receipt.Confirmed;
            if (!receipt.Confirmed)
            {
                SetState(BreedingRunState.Failed, receipt.Evidence);
                return;
            }
            _started = _now();
            if (!token.IsCancellationRequested)
                SetState(BreedingRunState.Running, receipt.Evidence);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            SetState(BreedingRunState.Stopped, "Activation cancelled; no confirmed activation.");
        }
        catch (Exception ex)
        {
            SetState(BreedingRunState.Failed, ex.Message);
        }
    }

    public void Progress(DateTimeOffset now)
    {
        if (State is not (BreedingRunState.Running or BreedingRunState.AwaitingObservation)) return;
        var elapsed = now > _started ? now - _started : TimeSpan.Zero;
        Predicted = BreedingEngine.Predict(_initial!, _stage!, elapsed, now);
        ProgressFraction = Math.Clamp(elapsed.TotalSeconds / _stage!.EstimatedDuration.TotalSeconds, 0, 1);
        Remaining = elapsed < _stage.EstimatedDuration ? _stage.EstimatedDuration - elapsed : TimeSpan.Zero;
        if (Remaining == TimeSpan.Zero)
            SetState(BreedingRunState.AwaitingObservation, "Estimated stage end; read gauges, then Stop before the next stage.");
        else Changed?.Invoke();
    }

    public Task StopAsync()
    {
        if (_stopTask is { IsCompleted: false }) return _stopTask;
        _startCancellation?.Cancel();
        SetState(BreedingRunState.Stopping, "Cancelling activation and awaiting gauge deactivation.");
        _stopTask = StopCoreAsync();
        return _stopTask;
    }

    private async Task StopCoreAsync()
    {
        if (_startTask is not null) await _startTask;
        if (_activated)
        {
            try
            {
                // The cancelled start token must not prevent the real stop action.
                var receipt = await _execute!(new(_initial!.PaddockId, _stage!, PaddockOperation.DeactivateGauges), CancellationToken.None);
                if (!receipt.Confirmed)
                {
                    SetState(BreedingRunState.Failed, "Gauge stop unconfirmed: " + receipt.Evidence);
                    return;
                }
                _activated = false;
                SetState(BreedingRunState.Stopped, receipt.Evidence);
            }
            catch (Exception ex)
            {
                SetState(BreedingRunState.Failed, "Gauge stop unconfirmed: " + ex.Message);
            }
        }
        else SetState(BreedingRunState.Stopped, "Stopped; no confirmed active gauges.");
    }

    private void SetState(BreedingRunState state, string status)
    {
        State = state;
        Status = status;
        Changed?.Invoke();
    }
}
