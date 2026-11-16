using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    // One context ticket for both roleplay and combat. Unlike a fight_id/map_id, it
    // also rejects an earlier session which happened to use the same map and PID.
    private readonly object _combatMapGate = new();
    private readonly ConcurrentDictionary<uint, object> _combatMapTickets = new();

    private void ObserveServerMap(uint pid, long mapId)
    {
        if (mapId is <= 0 or > int.MaxValue)
        {
            NativeTurboRuntime.RevokeTacticalMapReady((int)pid);
            lock (_combatMapGate)
            {
                _serverMapId.TryRemove(pid, out _);
                _combatMapTickets.TryRemove(pid, out _);
            }
            return;
        }
        if (MZP25NativeModOwnership.MayControl((int)pid))
        lock (_linkGate)
        {
            if (!IsSocketPid(pid) && !IsLinkSuspended(pid))
                NativeTurboRuntime.BeginTacticalMapConstruction((int)pid, mapId,
                    LocalLaunch.LocalHandshakeRelay.ObservingBeforeForward(pid));
            else NativeTurboRuntime.RevokeTacticalMapReady((int)pid);
        }
        lock (_combatMapGate)
        {
            _serverMapId[pid] = mapId;
            _combatMapTickets[pid] = new object();
        }
        if (IsInFight(pid)) _ = RefreshCombatMapAsync(pid);
        FinishCharacterLoad(pid);
    }

    private void InvalidateCombatMap(uint pid)
    {
        NativeTurboRuntime.RevokeTacticalMapReady((int)pid);
        lock (_combatMapGate) _combatMapTickets.TryRemove(pid, out _);
    }

    private Task StartCombatMapAsync(uint pid)
    {
        lock (_combatMapGate)
        {
            _inFight[pid] = true;
            FinishCharacterLoad(pid);
            if (_combat is not null && _combat.TryGetValue(pid, out var profile))
                PublishCombatProfileNative(pid, profile);
            NativeTurboRuntime.SetCombatActive((int)pid, true);
            _combatMapTickets[pid] = new object();
            return RefreshCombatMapAsync(pid);
        }
    }

    // Always resolve the authoritative map at the point of use: the background UI
    // reload need not have finished before placement/the first automatic or Lua turn.
    // Some instances have no bundled geometry; only those keep the existing fallback.
    private (long MapId, MapCell[]? Cells) LoadFightMap(uint pid)
    {
        long fallback, target;
        bool fighting;
        object? ticket;
        lock (_combatMapGate)
        {
            fallback = _lastMap.GetValueOrDefault(pid);
            target = _serverMapId.GetValueOrDefault(pid);
            fighting = IsInFight(pid);
            ticket = _combatMapTickets.GetValueOrDefault(pid);
        }
        (long MapId, MapCell[]? Cells) result;
        if (fighting && target != 0 && target != fallback
            && _cellReader.LoadCells(target) is { Length: > 0 } authoritative)
            result = (target, authoritative);
        else result = (fallback, fallback != 0 ? _cellReader.LoadCells(fallback) : null);
        lock (_combatMapGate)
        {
            if (IsInFight(pid) != fighting || _lastMap.GetValueOrDefault(pid) != fallback
                || _serverMapId.GetValueOrDefault(pid) != target
                || !ReferenceEquals(ticket, _combatMapTickets.GetValueOrDefault(pid))) return (0, null);
        }
        return result;
    }

    private Task RefreshCombatMapAsync(uint pid)
    {
        object ticket;
        long target;
        lock (_combatMapGate)
        {
            if (!IsInFight(pid) || (target = _serverMapId.GetValueOrDefault(pid)) == 0)
                return Task.CompletedTask;
            // A duplicate readiness request must not invalidate a synchronous
            // geometry read already in progress for this same context.
            ticket = _combatMapTickets.GetOrAdd(pid, static _ => new object());
        }
        return Task.Run(() =>
        {
            var cells = _cellReader.LoadCells(target);
            if (cells is not { Length: > 0 }) return;
            var exits = _cellReader.LoadExits(target);
            PublishCombatMap(pid, target, ticket, cells, exits);
        });
    }

    private void PublishCombatMap(uint pid, long target, object ticket, MapCell[] cells, MapExits? exits)
    {
        lock (_combatMapGate)
        {
            if (!IsInFight(pid) || _serverMapId.GetValueOrDefault(pid) != target
                || !_combatMapTickets.TryGetValue(pid, out var current) || !ReferenceEquals(current, ticket)) return;
            // Keep the roleplay fallback separate, and never erase the live roster or
            // interactives from delayed geometry work. Only publish the arena relief.
            MapCellsLoaded?.Invoke(pid, target, cells);
            if (exits is not null) MapExitsLoaded?.Invoke(pid, exits);
        }
    }

    private object BeginRoleplayMap(uint pid, long mapId)
    {
        lock (_combatMapGate)
        {
            _lastMap[pid] = mapId;
            var ticket = new object();
            _combatMapTickets[pid] = ticket;
            return ticket;
        }
    }

    private void PublishRoleplayMap(uint pid, long mapId, object ticket, Action publish)
    {
        lock (_combatMapGate)
        {
            if (IsInFight(pid) || _lastMap.GetValueOrDefault(pid) != mapId
                || !_combatMapTickets.TryGetValue(pid, out var current) || !ReferenceEquals(current, ticket)) return;
            publish();
        }
    }
}
