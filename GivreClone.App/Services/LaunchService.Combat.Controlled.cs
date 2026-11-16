using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    private sealed class ControlledCombatContext
    {
        public required uint Pid;
        public required ControlledFighter Fighter;
        public bool ControlGranted;
        public int Turn;
        public Combat.CastLedger Ledger = new();
    }

    // Server-granted control only. An allied roster or a negative id never grants control.
    private readonly ConcurrentDictionary<uint, ControlledCombatContext> _controlledCombat = new();
    private readonly ConcurrentDictionary<(uint Pid, long Actor), ControlledCombatContext> _controlledHistory = new();
    private readonly ConcurrentDictionary<uint, long> _serverTurnActor = new();
    private readonly ConcurrentDictionary<uint, byte> _controlledTurnStarted = new();
    private readonly AsyncLocal<ControlledCombatContext?> _engineControlled = new();
    private readonly ConcurrentDictionary<uint, CancellationTokenSource> _automaticTurnStop = new();
    private ControlledCombatContext? EngineControlled(uint pid) => _engineControlled?.Value is { } c && c.Pid == pid ? c : null;

    private bool HasControlledTurn(uint pid) => _controlledCombat.TryGetValue(pid, out var c)
        && _serverTurnActor.GetValueOrDefault(pid) == c.Fighter.ActorId;

    private void ObserveServerFightTurn(uint pid, long actor)
    {
        _serverTurnActor[pid] = actor;
        if (IsInFight(pid) && actor != CharacterId(pid)
            && _controlledHistory.TryGetValue((pid, actor), out var context) && context.ControlGranted
            && _fighters.TryGetValue(pid, out var fighters) && fighters.Any(f => f.ActorId == actor))
            _controlledCombat[pid] = context;
        else
            _controlledCombat.TryRemove(pid, out _);
    }

    private void ObserveControlledFighter(uint pid, ControlledFighter control)
    {
        if (!IsInFight(pid) || control.MasterId != CharacterId(pid)) return;
        if (control.ActorId == control.MasterId)
        {
            ReleaseControlledFighter(pid);
            return;
        }
        var context = _controlledHistory.GetOrAdd((pid, control.ActorId), _ => new() { Pid = pid, Fighter = control });
        context.Fighter = control;
        context.ControlGranted = true;
        _controlledCombat[pid] = context;
        _allies.GetOrAdd(pid, static _ => new())[control.ActorId] = 0;
        TransportLog.Line($"[combat] contrôle serveur : combattant {control.ActorId}, {control.Spells.Count} sorts propres");
        TryStartControlledTurn(pid);
    }

    private void ReleaseControlledFighter(uint pid)
    {
        if (HasControlledTurn(pid))
        {
            _myTurnOpen[pid] = false;
            _turnEpoch.AddOrUpdate(pid, 1, static (_, n) => n + 1);
            CancelAutomaticFightTurn(pid);
        }
        _controlledCombat.TryRemove(pid, out _);
    }

    private void RevokeControlledFighter(uint pid, long master, long actor)
    {
        if (!_controlledHistory.TryGetValue((pid, actor), out var context)
            || context.Fighter.MasterId != master) return;
        context.ControlGranted = false;
        if (_controlledCombat.TryGetValue(pid, out var active) && active.Fighter.ActorId == actor)
            ReleaseControlledFighter(pid);
    }

    private void TryStartControlledTurn(uint pid)
    {
        if (!IsInFight(pid) || !HasControlledTurn(pid) || !_myTurnOpen.GetValueOrDefault(pid)
            || !(EngineAutoPlays(pid) || ScriptDrivesTurns(pid)) || !_controlledTurnStarted.TryAdd(pid, 0)) return;
        var context = _controlledCombat[pid];
        int epoch = _turnEpoch.AddOrUpdate(pid, 1, static (_, n) => n + 1);
        ArmTurnPlay(pid);
        TransportLog.Line($"[combat] début de tour : COMPAGNON {context.Fighter.ActorId}");
        _ = RunAutomaticFightTurn(pid, epoch, context);
    }

    private void CancelAutomaticFightTurn(uint pid)
    {
        if (!_automaticTurnStop.TryRemove(pid, out var old)) return;
        try { old.Cancel(); } catch (ObjectDisposedException) { }
    }

    private async Task RunAutomaticFightTurn(uint pid, int epoch, ControlledCombatContext? controlled = null, int journalRound = 0)
    {
        CancelAutomaticFightTurn(pid);
        using var stop = new CancellationTokenSource();
        _automaticTurnStop[pid] = stop;
        try { await RunFightTurn(pid, epoch, ct: stop.Token, controlled: controlled, journalRound: journalRound).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { _automaticTurnStop.TryRemove(new KeyValuePair<uint, CancellationTokenSource>(pid, stop)); }
    }

    private void ResetControlledCombat(uint pid)
    {
        CancelAutomaticFightTurn(pid);
        _controlledCombat.TryRemove(pid, out _);
        foreach (var key in _controlledHistory.Keys.Where(k => k.Pid == pid)) _controlledHistory.TryRemove(key, out _);
        _serverTurnActor.TryRemove(pid, out _);
        _controlledTurnStarted.TryRemove(pid, out _);
    }
}
