using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services;
using GivreClone.App.Services.LotM;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

public partial class BotsViewModel
{
    private async Task<bool> TravelSmithWorkshopAsync(uint pid, long map, CancellationToken ct)
    {
        if (_luaHosts.TryGetValue((int)pid, out var running) && running.Running) return false;
        var host = NewFollowHost((int)pid);
        try
        {
            if (!await host.TravelToMapAsync(map, ct).ConfigureAwait(false)) return false;
            return await WaitSmithWorkshopDataAsync(pid, map, ct).ConfigureAwait(false);
        }
        finally { host.Dispose(); }
    }

    private static async Task<bool> WaitSmithWorkshopDataAsync(uint pid, long map, CancellationToken ct)
    {
        var launch = Stores.Launch;
        var decoded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool Ready() => launch.CurrentMap(pid) == map && launch.InteractivesDecodedMap(pid) == map;
        void OnDecoded(uint source, IReadOnlyList<MapInteractive> _)
        {
            if (source == pid && Ready()) decoded.TrySetResult(true);
        }
        launch.MapInteractivesDecoded += OnDecoded;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (Ready()) return true;
            if (launch.CurrentMap(pid) != map) return false;
            return await decoded.Task.WaitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        }
        catch (TimeoutException) { return false; }
        finally { launch.MapInteractivesDecoded -= OnDecoded; }
    }


    /// <summary>Se place à portée de la machine (atelier ou concasseur) sur la carte courante. Déjà à portée : rien n'est envoyé.</summary>
    private static async Task<bool> ApproachSmithMachineAsync(uint pid, int elementId, int skillUid, CancellationToken ct)
    {
        var launch = Stores.Launch;
        long map = launch.CurrentMap(pid) ?? 0;
        if (map <= 0) return false;
        var world = Stores.World.Get(pid);
        long readyUntil = Environment.TickCount64 + 3000;
        while (!world.SelfReadyFor(map) && Environment.TickCount64 < readyUntil)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(120, ct).ConfigureAwait(false);
            world = Stores.World.Get(pid);
        }
        if (!world.SelfReadyFor(map) || world.SelfCell is not int source) return false;
        var machine = world.Interactives?.FirstOrDefault(i => i.ElementId == elementId);
        var skill = machine?.Skills.FirstOrDefault(s => s.SkillInstanceUid == skillUid);
        if (machine is null || skill is null) return false;
        if (machine.CellId < 0) return true;   // élément sans case : le serveur l'accepte depuis la carte
        int? stand = launch.InteractiveStandCell(pid, map, source, machine, skill, world.IsWalkable, world.StepAllowed, out var standWalk);
        if (stand is null) return false;
        if (stand.Value == source) return true;
        var moved = await launch.SendAndWaitAsync(pid,
            () => launch.MoveTo(pid, map, source, stand.Value, standWalk, true, world.StepAllowed),
            16000, ct).ConfigureAwait(false);
        return moved == LaunchService.MoveOutcome.Arrived;
    }

    private async Task<bool> EnterSmithInteriorAsync(uint pid, string placeName, CancellationToken ct)
    {
        var launch = Stores.Launch;
        long outside = launch.CurrentMap(pid) ?? 0;
        if (outside <= 0) return false;
        var world = Stores.World.Get(pid);
        long readyUntil = Environment.TickCount64 + 3000;
        while (!world.SelfReadyFor(outside) && Environment.TickCount64 < readyUntil)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(120, ct).ConfigureAwait(false);
            world = Stores.World.Get(pid);
        }
        if (!world.SelfReadyFor(outside) || world.SelfCell is not int source) return false;
        if (SmithMagicViewModel.WorkshopDoor(world.Interactives, placeName) is not { } door) return false;
        if (SmithMagicViewModel.DoorSkill(door) is not { } skill) return false;
        int? stand = launch.InteractiveStandCell(pid, outside, source, door, skill, world.IsWalkable, world.StepAllowed, out var standWalk);
        if (stand is null) return false;
        if (stand.Value != source)
        {
            var moved = await launch.SendAndWaitAsync(pid,
                () => launch.MoveTo(pid, outside, source, stand.Value, standWalk, true, world.StepAllowed),
                16000, ct).ConfigureAwait(false);
            if (moved != LaunchService.MoveOutcome.Arrived) return false;
        }
        if (!launch.SendInteractiveSkill(pid, door, skill.SkillId)) return false;
        long deadline = Environment.TickCount64 + 8000;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            long now = launch.CurrentMap(pid) ?? 0;
            if (now != 0 && now != outside)
                return await WaitSmithWorkshopDataAsync(pid, now, ct).ConfigureAwait(false);
            await Task.Delay(120, ct).ConfigureAwait(false);
        }
        return false;
    }
}
