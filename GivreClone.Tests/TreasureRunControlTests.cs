extern alias F06App;

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Engine.Recovery;
using FouduglenScript = F06App::GivreClone.App.Services.Treasure.FouduglenScript;
using HuntChestMaps = F06App::GivreClone.App.Services.Treasure.HuntChestMaps;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using GivreLuaHost = F06App::GivreClone.App.Services.GivreLuaHost;
using TreasureScript = F06App::GivreClone.App.Services.Treasure.TreasureScript;
using TreasureViewModel = F06App::GivreClone.App.ViewModels.TreasureViewModel;
using Xunit;

namespace GivreClone.Tests;

public sealed class TreasureRunControlTests
{
    [Fact]
    public void Auto_stop_targets_only_that_script_and_leaves_the_decoded_step()
    {
        var harness = new HuntHarness();
        harness.Put(1, 3, new LaunchService.TreasureStep("poi", 0, 0, 42, 0));
        harness.Vm.Refresh();
        string step = harness.Vm.CurrentStepText;
        Assert.Contains("Bosquet", step);

        harness.Vm.AutoHuntLevel = 40;
        harness.Vm.AutoHuntMaxFails = 2;
        harness.Vm.StartAutoHuntCommand.Execute(null);

        Assert.NotNull(harness.AutoStart);
        Assert.Equal((harness.Pid, 40, 2), harness.AutoStart.Value);
        Assert.Contains("L'étape en cours s'affiche ici", harness.Vm.Status);
        Assert.DoesNotContain("console", harness.Vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(harness.Vm.StartAutoHuntCommand.CanExecute(null));
        Assert.False(harness.Vm.StartFouduglenCommand.CanExecute(null));
        Assert.True(harness.Vm.CanStopHunt);
        Assert.Equal(step, harness.Vm.CurrentStepText);

        harness.Vm.StopHuntCommand.Execute(null);

        Assert.Equal("Chasse au trésor.lua", Assert.Single(harness.Stops).File);
        Assert.Equal(harness.Pid, harness.Stops[0].Pid);
        Assert.Equal("Chasse automatique arrêtée.", harness.Vm.Status);
        Assert.False(harness.Vm.CanStopHunt);
        Assert.False(harness.Running);
        Assert.Equal(step, harness.Vm.CurrentStepText);
        Assert.NotNull(harness.Service.Treasure(harness.Pid));
        Assert.Equal(123, harness.Vm.FouduglenBudget);

        harness.Vm.Refresh();
        Assert.Equal("Chasse automatique arrêtée.", harness.Vm.Status);
        Assert.Single(harness.Stops);
    }

    [Fact]
    public void Fouduglen_stop_targets_only_that_script_and_keeps_the_budget()
    {
        var harness = new HuntHarness();
        harness.Vm.FouduglenCount = 0;
        Assert.False(harness.Vm.StartFouduglenCommand.CanExecute(null));
        harness.Vm.FouduglenCount = 1;
        harness.Vm.FouduglenBudget = -1;
        Assert.False(harness.Vm.StartFouduglenCommand.CanExecute(null));
        harness.Vm.FouduglenBudget = 0;
        harness.Vm.FouduglenMaxUnitPrice = -1;
        Assert.False(harness.Vm.StartFouduglenCommand.CanExecute(null));
        harness.Vm.FouduglenMaxUnitPrice = 0;
        Assert.True(harness.Vm.StartFouduglenCommand.CanExecute(null));
        Assert.Null(harness.FouduglenStart);

        var unmapped = new HuntHarness(onMap: false);
        Assert.False(unmapped.Vm.StartFouduglenCommand.CanExecute(null));
        unmapped.Vm.StartFouduglenCommand.Execute(null);
        Assert.Null(unmapped.FouduglenStart);

        harness.Vm.FouduglenCount = 3;
        harness.Vm.FouduglenBudget = 12345;
        harness.Vm.FouduglenMaxUnitPrice = 99;
        harness.Vm.StartFouduglenCommand.Execute(null);

        Assert.NotNull(harness.FouduglenStart);
        Assert.Equal((harness.Pid, 3, 12345, 99), harness.FouduglenStart.Value);
        Assert.Null(harness.AutoStart);
        Assert.Contains("L'étape en cours s'affiche ici", harness.Vm.Status);
        Assert.DoesNotContain("console", harness.Vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.True(harness.Vm.CanStopHunt);
        Assert.False(harness.Vm.StartAutoHuntCommand.CanExecute(null));

        harness.Put(4, 2, new LaunchService.TreasureStep("direction", 2, 0, 0, 3));
        harness.Vm.Refresh();
        Assert.Equal($"Étape en cours : 1/2 · {TreasureRequestTracker.Direction(2)} Parcourir 3 carte(s)", harness.Vm.CurrentStepText);
        Assert.Equal("Parcourir 3 carte(s)", Assert.Single(harness.Vm.Steps).Label);
        Assert.True(harness.Vm.CanStopHunt);

        harness.Vm.StopHuntCommand.Execute(null);

        Assert.Equal("Fouduglen.lua", Assert.Single(harness.Stops).File);
        Assert.NotEqual("Chasse au trésor.lua", harness.Stops[0].File);
        Assert.Equal("Fouduglen arrêté.", harness.Vm.Status);
        Assert.Equal(12345, harness.Vm.FouduglenBudget);
        Assert.Equal(99, harness.Vm.FouduglenMaxUnitPrice);
        Assert.Equal(3, harness.Vm.FouduglenCount);
        Assert.Contains("Parcourir 3 carte(s)", harness.Vm.CurrentStepText);
        Assert.False(harness.Vm.CanStopHunt);
    }

    [Fact]
    public void Stop_stays_disarmed_without_a_host_and_a_refusal_can_be_retried()
    {
        var harness = new HuntHarness();
        harness.Vm.StopHuntScript = null;
        harness.Vm.StartAutoHuntCommand.Execute(null);
        Assert.True(harness.Running);
        Assert.False(harness.Vm.StopHuntCommand.CanExecute(null));
        harness.Vm.StopHuntCommand.Execute(null);
        Assert.Empty(harness.Stops);
        Assert.True(harness.Running);

        harness.Vm.StopHuntScript = (pid, file) =>
        {
            harness.Stops.Add(new HuntStop(pid, file));
            return false;
        };
        Assert.True(harness.Vm.StopHuntCommand.CanExecute(null));
        harness.Vm.StopHuntCommand.Execute(null);
        Assert.Equal("Arrêt du script refusé.", harness.Vm.Status);
        Assert.True(harness.Vm.CanStopHunt);
        Assert.True(harness.Running);

        harness.Vm.StopHuntScript = (pid, file) =>
        {
            harness.Stops.Add(new HuntStop(pid, file));
            harness.Running = false;
            return true;
        };
        harness.Vm.StopHuntCommand.Execute(null);
        Assert.Equal(2, harness.Stops.Count);
        Assert.All(harness.Stops, stop => Assert.Equal("Chasse au trésor.lua", stop.File));
        Assert.Equal("Chasse automatique arrêtée.", harness.Vm.Status);
        Assert.False(harness.Vm.CanStopHunt);
    }

    [Fact]
    public void A_script_we_did_not_launch_cannot_be_stopped_and_a_refused_launch_arms_nothing()
    {
        var harness = new HuntHarness { Running = true };
        Assert.False(harness.Vm.StartAutoHuntCommand.CanExecute(null));
        Assert.False(harness.Vm.StartFouduglenCommand.CanExecute(null));
        Assert.False(harness.Vm.CanStopHunt);
        harness.Vm.StartAutoHuntCommand.Execute(null);
        harness.Vm.StopHuntCommand.Execute(null);
        Assert.Null(harness.AutoStart);
        Assert.Empty(harness.Stops);

        var refused = new HuntHarness { AcceptStart = false };
        refused.Vm.StartAutoHuntCommand.Execute(null);
        Assert.NotNull(refused.AutoStart);
        Assert.Contains("refusée", refused.Vm.Status);
        Assert.False(refused.Vm.CanStopHunt);
        refused.Vm.StartFouduglenCommand.Execute(null);
        Assert.NotNull(refused.FouduglenStart);
        Assert.Contains("refusé", refused.Vm.Status);
        Assert.False(refused.Running);
        Assert.False(refused.Vm.CanStopHunt);
        Assert.Empty(refused.Stops);
    }

    [Fact]
    public void Natural_end_does_not_call_stop_and_a_late_start_is_not_treated_as_already_finished()
    {
        var harness = new HuntHarness();
        harness.Put(1, 1, new LaunchService.TreasureStep("fight", 0, 0, 0, 0));
        harness.Vm.Refresh();
        Assert.Equal("Étape en cours : 1/1 · Combat", harness.Vm.CurrentStepText);
        harness.Vm.StartAutoHuntCommand.Execute(null);
        harness.Running = false;
        harness.Vm.Refresh();

        Assert.Equal("Script de chasse terminé.", harness.Vm.Status);
        Assert.Equal("Étape en cours : 1/1 · Combat", harness.Vm.CurrentStepText);
        Assert.Empty(harness.Stops);
        Assert.False(harness.Vm.CanStopHunt);
        Assert.True(harness.Vm.StartAutoHuntCommand.CanExecute(null));
        Assert.NotNull(harness.Service.Treasure(harness.Pid));
        harness.Vm.Refresh();
        Assert.Equal("Script de chasse terminé.", harness.Vm.Status);

        var late = new HuntHarness();
        late.Vm.StartAutoHuntScript = (_, _, _) => true;
        late.Vm.StartAutoHuntCommand.Execute(null);
        late.Vm.Refresh();
        Assert.Contains("L'étape en cours s'affiche ici", late.Vm.Status);
        Assert.False(late.Vm.CanStopHunt);
        Assert.Empty(late.Stops);

        late.Running = true;
        late.Vm.Refresh();
        Assert.True(late.Vm.CanStopHunt);
        Assert.Contains("L'étape en cours s'affiche ici", late.Vm.Status);
        late.Running = false;
        late.Vm.Refresh();
        Assert.Equal("Script de chasse terminé.", late.Vm.Status);
        Assert.Empty(late.Stops);
    }

    [Fact]
    public void Current_step_follows_the_latest_server_step_not_the_selected_row()
    {
        var harness = new HuntHarness();
        Assert.Equal("Aucune étape en cours.", harness.Vm.CurrentStepText);

        harness.Put(1, 3, new LaunchService.TreasureStep("poi", 0, 0, 42, 0));
        harness.Vm.Refresh();
        Assert.Equal($"Étape en cours : 1/3 · {TreasureRequestTracker.Direction(0)} Indice : Bosquet", harness.Vm.CurrentStepText);
        harness.Vm.SelectedIndex = 0;

        harness.Put(2, 3,
            new LaunchService.TreasureStep("poi", 0, 0, 42, 0),
            new LaunchService.TreasureStep("hint", 4, 7, 0, 0));
        harness.Vm.Refresh();

        Assert.Equal(0, harness.Vm.SelectedIndex);
        Assert.Equal($"Étape en cours : 2/3 · {TreasureRequestTracker.Direction(4)} Phorreur : Gobby", harness.Vm.CurrentStepText);
        Assert.Equal("Indice : Bosquet", harness.Vm.Steps[0].Label);
        Assert.Equal("Phorreur : Gobby", harness.Vm.Steps[1].Label);
        Assert.DoesNotContain("Bosquet", harness.Vm.CurrentStepText);

        harness.Vm.PoiName = _ => null;
        harness.Put(3, 1, new LaunchService.TreasureStep("poi", 6, 0, 99, 0));
        harness.Vm.Refresh();
        Assert.Equal($"Étape en cours : 1/1 · {TreasureRequestTracker.Direction(6)} Indice : #99", harness.Vm.CurrentStepText);

        harness.Put(4, 4, Array.Empty<LaunchService.TreasureStep>());
        harness.Vm.Refresh();
        Assert.Equal("Étape en cours : indice pas encore reçu.", harness.Vm.CurrentStepText);
        Assert.Empty(harness.Vm.Steps);
    }

    [Fact]
    public void Stop_remains_while_the_script_continues_after_the_hunt()
    {
        var dropped = new HuntHarness();
        dropped.Vm.StartAutoHuntCommand.Execute(null);
        dropped.Put(1, 1, new LaunchService.TreasureStep("dig", 0, 0, 0, 0));
        dropped.Vm.Refresh();
        Assert.Equal("Étape en cours : 1/1 · Creuser", dropped.Vm.CurrentStepText);
        dropped.RemoveHunt();
        dropped.Vm.Refresh();
        Assert.Equal("Aucune chasse active observée.", dropped.Vm.Status);
        Assert.Equal("Aucune étape en cours.", dropped.Vm.CurrentStepText);
        Assert.True(dropped.Vm.CanStopHunt);
        Assert.Empty(dropped.Stops);

        var finished = new HuntHarness();
        finished.Vm.StartFouduglenCommand.Execute(null);
        finished.Put(2, 1, new LaunchService.TreasureStep("fight", 0, 0, 0, 0));
        finished.Vm.Refresh();
        finished.Finish(9);
        finished.Vm.Refresh();

        Assert.Equal("Fin de chasse reçue du serveur.", finished.Vm.Status);
        Assert.Equal("Aucune étape en cours.", finished.Vm.CurrentStepText);
        Assert.True(finished.Vm.CanStopHunt);
        Assert.False(finished.Vm.StartFouduglenCommand.CanExecute(null));
        Assert.Empty(finished.Stops);

        finished.Vm.StopHuntCommand.Execute(null);
        Assert.Equal("Fouduglen.lua", Assert.Single(finished.Stops).File);
        Assert.Equal("Fouduglen arrêté.", finished.Vm.Status);
    }

    [Fact]
    public void Switching_character_does_not_stop_the_running_hunt()
    {
        var harness = new HuntHarness();
        harness.Vm.StartAutoHuntCommand.Execute(null);
        harness.Map(8, 50);
        harness.Vm.AttachPid(8);

        Assert.False(harness.Vm.CanStopHunt);
        Assert.Empty(harness.Stops);
        Assert.Equal("En attente d'une chasse reçue du serveur.", harness.Vm.Status);
        Assert.True(harness.Running);

        harness.Vm.AttachPid((int)harness.Pid);
        Assert.True(harness.Vm.CanStopHunt);
        Assert.Equal("En attente d'une chasse reçue du serveur.", harness.Vm.Status);
        harness.Vm.StopHuntCommand.Execute(null);
        Assert.Equal(harness.Pid, Assert.Single(harness.Stops).Pid);
        Assert.Equal("Chasse au trésor.lua", harness.Stops[0].File);
    }

    [Fact]
    public async Task Hunt_stop_does_not_cancel_the_single_step_trip()
    {
        var harness = new HuntHarness();
        harness.Put(1, 1, new LaunchService.TreasureStep("poi", 0, 0, 42, 0));
        harness.Vm.Refresh();
        Assert.True(harness.Vm.CanGo);

        var resolved = new TaskCompletionSource<(long?, long, string?)>();
        CancellationToken token = default;
        int travels = 0;
        harness.Vm.ResolveStepMap = (_, _, ct) => { token = ct; return resolved.Task; };
        harness.Vm.Travel = (_, _, _) => { travels++; return Task.FromResult(true); };

        Task going = harness.Vm.GoToStepCommand.ExecuteAsync(null);
        Assert.True(harness.Vm.Going);
        Assert.Equal("Recherche de la carte de l'étape…", harness.Vm.Status);
        Assert.False(harness.Vm.StopHuntCommand.CanExecute(null));

        harness.Vm.StopHuntCommand.Execute(null);

        Assert.False(token.IsCancellationRequested);
        Assert.True(harness.Vm.Going);
        Assert.Equal(0, travels);
        Assert.Empty(harness.Stops);
        Assert.Equal("Recherche de la carte de l'étape…", harness.Vm.Status);

        harness.Vm.StopGoCommand.Execute(null);
        Assert.True(token.IsCancellationRequested);
        resolved.SetResult((200, 100, null));
        await going;
        Assert.False(harness.Vm.Going);
        Assert.Equal(0, travels);
        Assert.Equal("Trajet annulé.", harness.Vm.Status);
    }

    // docs/codex/LIVE_TEST_20261030.md : « depart 88212481 », distinct de la Malle 128452097.
    private const long CitedDeparture = 88212481;

    [Fact]
    public void Chest_ids_are_the_element_and_the_map_already_used_by_the_hunt_code()
    {
        var element = typeof(GivreLuaHost).GetField("TreasureStartElement", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(element);
        Assert.Equal(HuntChestMaps.ElementId, Assert.IsType<int>(element!.GetValue(null)));
        Assert.Contains("travelTo(" + HuntChestMaps.MalleAuxTresors, FouduglenScript.Build(1, 0, 0));
        Assert.Contains("startRefusals >= 3", TreasureScript.Build(0, 3));
        Assert.Equal(HuntChestMaps.MalleAuxTresors, HuntChestMaps.Nearest(CitedDeparture, id => id == CitedDeparture ? (0, 0) : (4, 1)));
        Assert.Equal(HuntChestMaps.MalleAuxTresors, HuntChestMaps.Nearest(HuntChestMaps.MalleAuxTresors, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Auto_hunt_shows_the_trip_then_starts_on_the_cited_chest(bool serverUpdate)
    {
        var harness = new HuntHarness();
        harness.Map(harness.Pid, CitedDeparture);
        harness.Vm.AutoHuntLevel = 40;
        harness.Vm.AutoHuntMaxFails = 2;
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        long requested = 0;
        string? step = null;
        string? status = null;
        CancellationToken tripToken = default;
        harness.Vm.Travel = (pid, map, token) =>
        {
            tripToken = token;
            requested = map;
            step = harness.Vm.CurrentStepText;
            status = harness.Vm.Status;
            Assert.True(harness.Vm.Going);
            harness.Map(pid, map);
            return gate.Task;
        };

        var running = harness.Vm.StartAutoHuntCommand.ExecuteAsync(null);

        Assert.Equal(HuntChestMaps.MalleAuxTresors, requested);
        Assert.Contains(HuntChestMaps.MalleAuxTresors.ToString(), step);
        Assert.Contains("coffre de chasse", step, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("coffre de chasse", status, StringComparison.OrdinalIgnoreCase);
        Assert.Null(harness.AutoStart);
        Assert.False(harness.Vm.CanStopHunt);

        if (serverUpdate)
        {
            harness.Put(1, 1, new LaunchService.TreasureStep("poi", 0, 0, 42, 0));
            harness.Vm.Refresh();
        }
        Assert.False(tripToken.IsCancellationRequested);

        gate.SetResult(true);
        await running;

        Assert.Equal((harness.Pid, 40, 2), harness.AutoStart);
        Assert.Contains("L'étape en cours s'affiche ici", harness.Vm.Status);
        if (serverUpdate) Assert.Contains("Bosquet", harness.Vm.CurrentStepText);
        else Assert.Contains(HuntChestMaps.MalleAuxTresors.ToString(), harness.Vm.CurrentStepText);
        Assert.True(harness.Vm.CanStopHunt);
        Assert.False(harness.Vm.Going);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task Auto_hunt_restores_the_current_decoded_step_after_the_chest_trip(bool serverUpdate, bool arrives)
    {
        var harness = new HuntHarness();
        harness.Put(1, 1, new LaunchService.TreasureStep("poi", 0, 0, 42, 0));
        harness.Vm.Refresh();
        string decoded = harness.Vm.CurrentStepText;
        harness.Map(harness.Pid, CitedDeparture);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? during = null;
        harness.Vm.Travel = (pid, map, _) =>
        {
            during = harness.Vm.CurrentStepText;
            harness.Map(pid, map);
            return gate.Task;
        };

        var running = harness.Vm.StartAutoHuntCommand.ExecuteAsync(null);
        Assert.NotNull(during);
        Assert.Contains("coffre de chasse", during);
        Assert.Contains("Bosquet", decoded);
        if (serverUpdate)
        {
            harness.Put(2, 2, new LaunchService.TreasureStep("direction", 4, 0, 0, 2));
            harness.Vm.Refresh();
            decoded = harness.Vm.CurrentStepText;
        }
        gate.SetResult(arrives);
        await running;

        Assert.Equal(arrives, harness.AutoStart is not null);
        Assert.Equal(decoded, harness.Vm.CurrentStepText);
    }

    [Fact]
    public void Auto_hunt_on_the_chest_or_on_its_element_does_not_travel()
    {
        var onChest = new HuntHarness();
        int travels = 0;
        onChest.Vm.Travel = (_, _, _) => { travels++; return Task.FromResult(true); };
        onChest.Vm.StartAutoHuntCommand.Execute(null);
        Assert.Equal(0, travels);
        Assert.NotNull(onChest.AutoStart);

        var onElement = new HuntHarness();
        onElement.Map(onElement.Pid, CitedDeparture);
        onElement.Vm.MapHasHuntChest = (_, map) => map == CitedDeparture;
        onElement.Vm.Travel = (_, _, _) => { travels++; return Task.FromResult(true); };
        onElement.Vm.StartAutoHuntCommand.Execute(null);
        Assert.Equal(0, travels);
        Assert.NotNull(onElement.AutoStart);
    }

    [Fact]
    public async Task A_failed_or_missing_chest_trip_does_not_start_the_script()
    {
        var missing = new HuntHarness();
        missing.Map(missing.Pid, CitedDeparture);
        missing.Vm.Travel = null;
        missing.Vm.StartAutoHuntCommand.Execute(null);
        Assert.Null(missing.AutoStart);
        Assert.Contains("indisponible", missing.Vm.Status);

        var failed = new HuntHarness();
        failed.Map(failed.Pid, CitedDeparture);
        failed.Vm.Travel = (_, map, _) =>
        {
            Assert.Equal(HuntChestMaps.MalleAuxTresors, map);
            return Task.FromResult(false);
        };
        await failed.Vm.StartAutoHuntCommand.ExecuteAsync(null);
        Assert.Null(failed.AutoStart);
        Assert.Contains("échoué", failed.Vm.Status);
        Assert.Equal("Aucune étape en cours.", failed.Vm.CurrentStepText);
        Assert.False(failed.Vm.CanStopHunt);
    }

    [Fact]
    public async Task A_successful_trip_without_an_observed_chest_does_not_start_the_hunt()
    {
        var harness = new HuntHarness();
        harness.Map(harness.Pid, CitedDeparture);
        harness.Vm.Travel = (_, _, _) => Task.FromResult(true);

        await harness.Vm.StartAutoHuntCommand.ExecuteAsync(null);

        Assert.Null(harness.AutoStart);
        Assert.False(harness.Running);
        Assert.True(harness.Vm.CanStartAutoHunt);
    }

    [Fact]
    public async Task Cancelling_the_chest_trip_does_not_start_the_script()
    {
        var harness = new HuntHarness();
        harness.Map(harness.Pid, CitedDeparture);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken seen = default;
        harness.Vm.Travel = (_, _, token) =>
        {
            seen = token;
            return gate.Task;
        };

        var running = harness.Vm.StartAutoHuntCommand.ExecuteAsync(null);
        Assert.True(harness.Vm.Going);
        harness.Vm.StopGoCommand.Execute(null);
        Assert.True(seen.IsCancellationRequested);
        gate.TrySetResult(true);
        await running;

        Assert.Null(harness.AutoStart);
        Assert.Equal("Trajet annulé.", harness.Vm.Status);
        Assert.False(harness.Vm.Going);
        Assert.Equal("Aucune étape en cours.", harness.Vm.CurrentStepText);
    }

    private sealed record HuntStop(uint Pid, string File);

    [Theory]
    [InlineData(0, false, false, false, false, F06App::GivreClone.App.ViewModels.AutoHuntReadiness.NoCharacter)]
    [InlineData(7, false, false, false, false, F06App::GivreClone.App.ViewModels.AutoHuntReadiness.NoMap)]
    [InlineData(7, true, true, false, false, F06App::GivreClone.App.ViewModels.AutoHuntReadiness.Travelling)]
    [InlineData(7, true, false, true, false, F06App::GivreClone.App.ViewModels.AutoHuntReadiness.Unavailable)]
    [InlineData(7, true, false, false, true, F06App::GivreClone.App.ViewModels.AutoHuntReadiness.ScriptRunning)]
    [InlineData(7, true, false, false, false, F06App::GivreClone.App.ViewModels.AutoHuntReadiness.Ready)]
    public void Readiness_reason_matches_the_start_gate(uint pid, bool onMap, bool travelling,
        bool unavailable, bool running, F06App::GivreClone.App.ViewModels.AutoHuntReadiness expected)
    {
        var harness = new HuntHarness(pid, onMap) { Running = running };
        harness.Vm.Going = travelling;
        if (unavailable) harness.Vm.StartAutoHuntScript = null;

        harness.Vm.Refresh();

        Assert.Equal(expected, harness.Vm.AutoHuntReadyState);
        Assert.Equal(expected == F06App::GivreClone.App.ViewModels.AutoHuntReadiness.Ready,
            harness.Vm.StartAutoHuntCommand.CanExecute(null));
    }

    private sealed class HuntHarness
    {
        public uint Pid { get; }
        public LaunchService Service { get; }
        public TreasureViewModel Vm { get; }
        public bool Running { get; set; }
        public bool AcceptStart { get; set; } = true;
        public List<HuntStop> Stops { get; } = new();
        public (uint Pid, int Level, int Fails)? AutoStart { get; private set; }
        public (uint Pid, int Count, int Budget, int Price)? FouduglenStart { get; private set; }

        public HuntHarness(uint pid = 7, bool onMap = true)
        {
            Pid = pid;
            Service = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
            foreach (string name in new[] { "_treasure", "_treasureFinished", "_lastMap" })
            {
                var field = typeof(LaunchService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(field);
                field!.SetValue(Service, Activator.CreateInstance(field.FieldType)!);
            }
            if (onMap) Map(pid, 128452097);
            Vm = new TreasureViewModel(Service);
            Vm.ScriptRunning = _ => Running;
            Vm.StartAutoHuntScript = (p, level, fails) =>
            {
                AutoStart = (p, level, fails);
                if (!AcceptStart) return false;
                Running = true;
                return true;
            };
            Vm.StartFouduglenScript = (p, count, budget, price) =>
            {
                FouduglenStart = (p, count, budget, price);
                if (!AcceptStart) return false;
                Running = true;
                return true;
            };
            Vm.StopHuntScript = (p, file) =>
            {
                Stops.Add(new HuntStop(p, file));
                Running = false;
                return true;
            };
            Vm.PoiName = id => id == 42 ? "Bosquet" : null;
            Vm.NpcName = id => id == 7 ? "Gobby" : null;
            Vm.FouduglenBudget = 123;
            Vm.AttachPid((int)pid);
        }

        public void Map(uint pid, long mapId) => Maps("_lastMap")[pid] = mapId;

        public void Put(long seq, int total, params LaunchService.TreasureStep[] steps)
            => Hunts()[Pid] = new LaunchService.TreasureHunt(1, 100, total, 0, 1, 3,
                Array.Empty<LaunchService.TreasureFlag>(), steps, seq);

        public void RemoveHunt() => Hunts().Remove(Pid);

        public void Finish(long seq)
        {
            Hunts().Remove(Pid);
            Finished()[Pid] = seq;
        }

        private IDictionary Hunts() => Maps("_treasure");
        private IDictionary Finished() => Maps("_treasureFinished");
        private IDictionary Maps(string name)
        {
            var value = typeof(LaunchService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Service);
            Assert.NotNull(value);
            return (IDictionary)value!;
        }
    }
}
