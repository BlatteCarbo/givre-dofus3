using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using GivreClone.App.Services.Breeding;
using GivreClone.Engine.Breeding;

namespace GivreClone.App.ViewModels;

public sealed record BreedingReadingRow(string Name, int? Value, BreedingValueSource Source, DateTimeOffset? At);

/// <summary>
/// Native host seam. Load observed/manual inputs on the UI thread, bind the commands and call Refresh
/// from existing host events/clock only while busy. No timer, protocol guesses or Lua mount stub.
/// </summary>
public sealed class BreedingViewModel : INotifyPropertyChanged
{
    private readonly BreedingSession _session;
    private readonly Command _next;
    private readonly Command _start;
    private readonly Command _stop;
    private string? _error;

    public BreedingViewModel() : this(new BreedingSession()) { }

    public BreedingViewModel(BreedingSession session)
    {
        _session = session;
        _next = new(() => { Recalculate(); return Task.CompletedTask; }, () => CurrentLot is not null && !IsBusy, Fail);
        _start = new(StartAsync, () => CanStart, Fail);
        _stop = new(StopAsync, () => IsBusy, Fail);
        _session.Changed += Notify;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public BreedingLot? CurrentLot { get; private set; }
    public BreedingTarget? Target { get; private set; }
    public BreedingPlan? Plan { get; private set; }
    public BreedingStage? NextStage => Plan?.Stage;
    public BreedingLot? Prediction => _session.Predicted;
    public bool HasExecutionBinding => _session.HasExecutionBinding;
    public bool IsBusy => _session.IsBusy;
    public bool CanStart => !IsBusy && HasExecutionBinding && Plan?.CanStart == true;
    public BreedingRunState State => _session.State;
    public double Progress => _session.ProgressFraction;
    public TimeSpan Remaining => _session.Remaining;
    public string Status => _error ?? (IsBusy || State is BreedingRunState.Failed or BreedingRunState.Stopped
        ? _session.Status : Plan?.BlockedReason ?? (Plan?.Complete == true ? "Target confirmed reached."
        : !HasExecutionBinding ? "Planning only: verified paddock actions are not bound." : "Ready."));
    public ICommand NextStageCommand => _next;
    public ICommand StartCommand => _start;
    public ICommand StopCommand => _stop;

    public IReadOnlyList<BreedingReadingRow> Readings => Rows(CurrentLot);
    public IReadOnlyList<BreedingReadingRow> PredictedReadings => Rows(Prediction);

    public void Load(BreedingLot lot, BreedingTarget target)
    {
        if (IsBusy && CurrentLot?.PaddockId != lot.PaddockId)
            throw new InvalidOperationException("Stop before changing paddock.");
        CurrentLot = lot;
        Target = target;
        Recalculate();
    }

    public void Recalculate()
    {
        _error = null;
        Plan = CurrentLot is not null && Target is not null ? BreedingEngine.NextStage(CurrentLot, Target) : null;
        Notify();
    }

    public async Task StartAsync()
    {
        if (CurrentLot is null || Target is null) throw new InvalidOperationException("Load a breeding lot first.");
        _error = null;
        await _session.StartAsync(CurrentLot, Target);
        Notify();
    }

    public async Task StopAsync()
    {
        _error = null;
        await _session.StopAsync();
        // A stopped stage never overwrites observed values with its forecast.
        Recalculate();
    }

    public void Refresh(DateTimeOffset now) => _session.Progress(now);

    private void Fail(Exception ex) { _error = ex.Message; Notify(); }

    private void Notify()
    {
        // Empty name means all bindings; no periodic work is created by this viewmodel.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        _next.RaiseCanExecuteChanged();
        _start.RaiseCanExecuteChanged();
        _stop.RaiseCanExecuteChanged();
    }

    private static IReadOnlyList<BreedingReadingRow> Rows(BreedingLot? lot)
    {
        if (lot is null) return Array.Empty<BreedingReadingRow>();
        BreedingReadingRow Row(string name, BreedingValue<int> value) =>
            new(name, value.IsKnown ? value.Value : null, value.Source, value.At);
        return new[] {
            Row("MountCount", lot.MountCount), Row("Serenity", lot.Serenity), Row("Endurance", lot.Endurance),
            Row("Maturity", lot.Maturity), Row("Love", lot.Love), Row("Xp", lot.Xp)
        }.Concat(Enum.GetValues<BreedingGauge>().Select(g =>
            Row("Fuel." + g, lot.Fuel.TryGetValue(g, out var value) ? value : BreedingValue<int>.Unknown))).ToArray();
    }

    private sealed class Command(Func<Task> execute, Func<bool> canExecute, Action<Exception> failed) : ICommand
    {
        private bool _executing;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => !_executing && canExecute();
        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;
            _executing = true;
            RaiseCanExecuteChanged();
            try { await execute(); }
            catch (Exception ex) { failed(ex); }
            finally { _executing = false; RaiseCanExecuteChanged(); }
        }
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
