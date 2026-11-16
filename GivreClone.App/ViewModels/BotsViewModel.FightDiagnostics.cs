using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

public partial class BotsViewModel
{
    [ObservableProperty] public partial string FightDiagnosticText { get; set; } = "";

    [RelayCommand]
    private void RefreshFightDiagnostics()
    {
        if (SelectedBot is not { Pid: > 0 } bot) { FightDiagnosticText = Loc.T("L.FightDiag.Empty"); return; }
        var report = TurboLogDiagnostics.Analyze(bot.TransportConsole, (uint)bot.Pid);
        FightDiagnosticText = report.Requests + report.Timeouts + report.NativeErrors == 0 ? Loc.T("L.FightDiag.Empty")
            : Loc.Fmt("L.FightDiag.Counts", report.Requests, report.Closed, report.Timeouts, report.GraceRetries)
              + Environment.NewLine + Loc.Fmt("L.FightDiag.Waits", report.MaxWaitMs, report.MaxSequenceToAckMs, report.EndsAfterTimeout, report.NativeErrors)
              + Environment.NewLine + Loc.T("L.FightDiag.Scope");
    }
}
