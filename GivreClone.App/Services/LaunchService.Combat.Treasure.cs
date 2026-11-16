using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Engine;
using GivreClone.Protocol;
using GivreLauncher.Core.Transport;
using GivreCore.Protobuf;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<long, FightMonster>> _fightMonsters = new();
    private readonly ConcurrentDictionary<uint, byte> _treasureEncounters = new();
    private readonly ConcurrentDictionary<uint, (long Actor, int Cell, int Turn)> _treasureAttacked = new();

    private static bool TreasureName(string? name) => string.Equals(name, "Trésor", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Tresor", StringComparison.OrdinalIgnoreCase);
    private static bool GuardName(string? name) => string.Equals(name, "Vigicoffre", StringComparison.OrdinalIgnoreCase);

    private void ObserveTreasureFighters(uint pid, string name, IReadOnlyList<ProtoField> payload)
    {
        if (_catalog is null) return;
        foreach (var monster in FightMonsterReader.Read(name, payload, _catalog.FieldNumber))
            _fightMonsters.GetOrAdd(pid, static _ => new())[monster.ActorId] = monster;
        if (FightMonsterReader.ReadLookChange(name, payload, _catalog.FieldNumber) is { } change
            && _fightMonsters.TryGetValue(pid, out var byId) && byId.TryGetValue(change.Actor, out var old))
            byId[change.Actor] = old with { Appearance = change.Appearance };
    }

    private bool IsTreasureFight(uint pid)
    {
        if (_legendaryTreasure?.ContainsKey(pid) == true) return false;
        // The server snapshot survives reconnects, unlike the local launch marker.
        if (_treasure.TryGetValue(pid, out var currentHunt) && currentHunt.QuestType == 2) return false;
        if (_treasureEncounters.ContainsKey(pid)) return true;
        // The final quest step is also authoritative when optional client name bundles are unavailable.
        bool quest = _treasure.TryGetValue(pid, out var hunt) && hunt.Steps.LastOrDefault()?.Kind == "fight";
        bool named = _fightMonsters.TryGetValue(pid, out var monsters)
            && monsters.Values.Any(m => TreasureName(_gameNames.MonsterName(m.GenericId)) || GuardName(_gameNames.MonsterName(m.GenericId)));
        if (!quest && !named) return false;
        _treasureEncounters[pid] = 0;
        return true;
    }

    private TreasureFightPolicy.Guard? TreasureTarget(uint pid)
    {
        if (!_fightMonsters.TryGetValue(pid, out var monsters) || !_fighters.TryGetValue(pid, out var fighters)) return null;
        var chests = monsters.Values.Where(m => TreasureName(_gameNames.MonsterName(m.GenericId))).Select(m => m.ActorId).ToHashSet();
        // Context summons can omit monster_gid. In a confirmed final quest step, their common summoner
        // identifies the chest without optional localized names; it must have exactly four live summons.
        if (_treasure.TryGetValue(pid, out var hunt) && hunt.Steps.LastOrDefault()?.Kind == "fight")
            foreach (var owner in monsters.Values.Where(m => m.Summoner.HasValue && fighters.Any(f => f.ActorId == m.ActorId))
                .GroupBy(m => m.Summoner!.Value).Where(g => g.Count() == 4 && fighters.Any(f => f.ActorId == g.Key)))
                chests.Add(owner.Key);
        var guards = fighters.Where(f => monsters.TryGetValue(f.ActorId, out var m)
                && (GuardName(_gameNames.MonsterName(m.GenericId)) || m.Summoner is long owner && chests.Contains(owner)))
            .Select(f => new TreasureFightPolicy.Guard(f.ActorId, f.Cell, monsters[f.ActorId].Appearance)).ToList();
        var target = TreasureFightPolicy.Select(guards);
        if (target is not { } found || !IsEnemy(pid, found.Actor))
        {
            TransportLog.Line($"[combat] coffre : gardes observes [{string.Join("; ", guards.Select(g => $"{g.Actor}@{g.Cell} look={g.Appearance ?? "?"}"))}]");
            return null;
        }
        if (_treasureAttacked.TryGetValue(pid, out var previous))
        {
            // A successful hit can swap the special actor. Do not reuse the old cell while only half a
            // teleport pair has arrived. A swap is optional: the next confirmed turn must allow the
            // same special guard again. Re-read the complete layout before every cast.
            if (found.Actor == previous.Actor && found.Cell == previous.Cell
                && previous.Turn == _fightTurnNo.GetValueOrDefault(pid)) return null;
            _treasureAttacked.TryRemove(pid, out _);
        }
        return found;
    }

    private bool TreasureSpellAllowed(uint pid, int spellId) =>
        _gameNames.SpellIsSingleTarget(spellId, LearnedSpellLevel(pid, spellId));
}
