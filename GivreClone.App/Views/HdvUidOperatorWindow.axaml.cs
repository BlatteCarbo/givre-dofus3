using System;
using Avalonia.Controls;
using GivreClone.App.ViewModels;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Views;

public partial class HdvUidOperatorWindow : Window
{
    public HdvUidOperatorWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private HdvViewModel? _observer;
    private void OnOpened(object? sender, EventArgs e)
    {
        _observer = DataContext as HdvViewModel;
        _observer?.ClearUidDiagnosticsCommand.Execute(null);
        TransportLog.EmittedWithPid += Observe;
    }

    private void Observe(uint pid, string message) => _observer?.ObserveUidDiagnostic(pid, message);

    private void OnClosed(object? sender, EventArgs e)
    {
        TransportLog.EmittedWithPid -= Observe;
        _observer?.ClearUidDiagnosticsCommand.Execute(null);
        _observer = null;
    }
}
