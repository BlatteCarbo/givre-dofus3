using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Views;

namespace GivreClone.App.ViewModels;

public partial class HdvViewModel
{
    public ObservableCollection<HdvUidOperatorRow> UidDiagnostics { get; } = new();
    private long _uidGeneration;
    private HdvUidOperatorWindow? _uidWindow;

    [RelayCommand]
    private void OpenUidOperator()
    {
        if (_uidWindow is not null) { _uidWindow.Activate(); return; }
        _uidWindow = new HdvUidOperatorWindow { DataContext = this };
        _uidWindow.Closed += (_, _) => _uidWindow = null;
        _uidWindow.Show();
    }

    internal void ObserveUidDiagnostic(uint routedPid, string message)
    {
        var row = HdvUidOperatorRow.Parse(message);
        if (row is null || (routedPid != 0 && routedPid != row.Pid)) return;
        long generation = System.Threading.Interlocked.Read(ref _uidGeneration);
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _uidGeneration || row.Pid != _pid) return;
            UidDiagnostics.Insert(0, row);
            while (UidDiagnostics.Count > 150) UidDiagnostics.RemoveAt(UidDiagnostics.Count - 1);
        });
    }

    [RelayCommand]
    private void ClearUidDiagnostics()
    {
        System.Threading.Interlocked.Increment(ref _uidGeneration);
        UidDiagnostics.Clear();
    }
}
