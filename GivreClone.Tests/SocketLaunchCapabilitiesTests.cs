using System;
using System.IO;
using GivreClone.App.Services;
using GivreClone.App.Services.Diagnostics;
using GivreClone.App.Services.Supervisor;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Offline lifecycle checks. The test project intentionally does not load Avalonia or a live
/// SocketGameClient; source-order assertions cover the owned VM transitions, while the pure
/// diagnostics verify that a map/"ingame" status never implies a confirmed action.
/// </summary>
public sealed class SocketLaunchCapabilitiesTests
{
    private static string Vm(string file)
    {
        var root = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, "GivreClone.App", "ViewModels", file)));
        if (root is null) throw new DirectoryNotFoundException("Repository root missing");
        return File.ReadAllText(Path.Combine(root.FullName, "GivreClone.App", "ViewModels", file));
    }

    [Fact]
    public void Manual_reconnect_clears_old_map_synchronously_before_opening_next_session()
    {
        string vm = Vm("AccountsViewModel.Socket.cs");
        int manual = vm.IndexOf("if (manual)", StringComparison.Ordinal);
        Assert.True(manual > 0);
        int clearUi = vm.IndexOf("await Dispatcher.UIThread.InvokeAsync(() =>", manual, StringComparison.Ordinal);
        int clearMap = vm.IndexOf("Stores.World.ClearMap((uint)pid)", clearUi, StringComparison.Ordinal);
        int nextSession = vm.IndexOf("fails = 0; attended = true;", clearMap, StringComparison.Ordinal);
        Assert.True(manual < clearUi && clearUi < clearMap && clearMap < nextSession,
            "Manual reconnect must finish clearing the old map before trying the next game session.");
    }

    [Fact]
    public void Stop_revokes_socket_injection_before_dropping_the_bot_identity()
    {
        string vm = Vm("AccountsViewModel.Launch.cs");
        int stop = vm.IndexOf("private void StopCore(AccountRow row, bool cancelTransition = true)", StringComparison.Ordinal);
        int cancel = vm.IndexOf("row.SocketCts?.Cancel()", stop, StringComparison.Ordinal);
        int unregister = vm.IndexOf("_launch.UnregisterSocketInjector((uint)pid)", cancel, StringComparison.Ordinal);
        int forget = vm.IndexOf("_launch.Forget((uint)pid)", unregister, StringComparison.Ordinal);
        Assert.True(stop >= 0 && stop < cancel && cancel < unregister && unregister < forget,
            "Stopping the account must unpublish its injector without waiting for SocketGameClient finally.");
    }

    [Fact]
    public void Read_only_socket_capability_is_queried_from_real_dispatch_registration()
    {
        string vm = Vm("AccountsViewModel.Socket.cs");
        int method = vm.IndexOf("SocketControlObservation ObserveSocketControl", StringComparison.Ordinal);
        int registered = vm.IndexOf("_launch.HasSocketInjector(socketPid)", method, StringComparison.Ordinal);
        int observationOnly = vm.IndexOf("\"observation-only\"", registered, StringComparison.Ordinal);
        int channel = vm.IndexOf("\"channel-registered\"", observationOnly, StringComparison.Ordinal);
        Assert.True(method >= 0 && method < registered && registered < observationOnly && observationOnly < channel);
        Assert.Contains("ACK serveur encore à vérifier", vm);
    }

    [Fact]
    public void Map_and_ingame_status_are_observations_not_a_command_acknowledgement()
    {
        string status = BotStatusMap.From(running: true, disconnectedHint: false, BotActivity.InGame);
        Assert.Equal(BotStatus.InGame, status);
        var report = BotReadiness.Evaluate(new BotReadiness.Input(
            Status: status, Pid: 0x40000001, MapId: 123456, InFight: false,
            ScriptRunning: false, LinkSuspended: false, CellConfirmed: null,
            ProtocolVerified: null, ServerAcknowledgementVerified: null,
            HdvSellReview: false, HdvRemoveReview: false, HdvRepriceReview: false,
            DisconnectReason: null));
        Assert.Equal("not-verified", report.Verdict);
        Assert.False(report.MayAssumeGameplayConfirmed);
        Assert.Contains(report.Checks, c => c.Id == "server-ack" && c.State == "unknown");
    }
}
