using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.Combat;

public sealed record TeamTargetIntent(long ActorId, int ExpectedDamage, bool ExpectedKill = false, int? LifeBefore = null);
public sealed record TeamSupportIntent(long AllyActorId, int Heal = 0, int Shield = 0, int? LifeBefore = null, int? ShieldBefore = null);
public sealed record TeamIntent(long SelfActorId, int Round, IReadOnlyList<TeamTargetIntent> Targets,
    int EndCell, IReadOnlyList<TeamSupportIntent> Support, long Timestamp);
public sealed record TeamAllyExposure(long ActorId, int Life, int MaxLife, int IncomingDamage, int Shield = 0);

/// <summary>Other controlled allies' current decisions and previous-round positional hints.</summary>
public sealed class TeamIntentView
{
    public static TeamIntentView Empty { get; } = new(Array.Empty<TeamIntent>());
    public IReadOnlyList<TeamIntent> Intents { get; }
    public IReadOnlySet<long> ReservedKills { get; }
    public IReadOnlyDictionary<long, int> AllyEndCells { get; }

    internal TeamIntentView(IEnumerable<TeamIntent> intents, int round = 0)
    {
        Intents = Array.AsReadOnly(intents.ToArray());
        _current = Intents.Where(i => round == 0 || i.Round == round).ToArray();
        ReservedKills = _current.SelectMany(i => i.Targets).Where(t => t.ExpectedKill)
            .Select(t => t.ActorId).ToHashSet();
        AllyEndCells = Intents.ToDictionary(i => i.SelfActorId, i => i.EndCell);
    }

    private readonly IReadOnlyList<TeamIntent> _current;
    public int ExpectedDamageOn(long actor) => _current.SelectMany(i => i.Targets)
        .Where(t => t.ActorId == actor).Sum(t => Math.Max(0, t.ExpectedDamage));

    public static int EffectiveRemainingLife(int life, int pendingDamage) => Math.Max(0, life - Math.Max(0, pendingDamage));

    public int EffectiveRemainingLife(long actor, int life)
    {
        var hits = _current.SelectMany(i => i.Targets).Where(t => t.ActorId == actor).ToArray();
        // Classic casts have already reached the server. Compare with their pre-cast life so
        // confirmed damage is not subtracted twice; planner-only intents may omit that baseline.
        int baseline = hits.Where(t => t.LifeBefore.HasValue).Select(t => t.LifeBefore!.Value).DefaultIfEmpty(life).Max();
        return Math.Min(life, EffectiveRemainingLife(baseline, ExpectedDamageOn(actor)));
    }

    /// <summary>Reserved enemies stay last, not removed: reachable alternatives win without losing a legal cast.</summary>
    public List<T> OrderTargets<T>(IEnumerable<T> targets, Func<T, long> actorOf, Func<long, int?> lifeOf, int killBudget)
        => targets.OrderBy(t => ReservedKills.Contains(actorOf(t)) || lifeOf(actorOf(t)) is int hp && EffectiveRemainingLife(actorOf(t), hp) == 0)
            .ThenBy(t => lifeOf(actorOf(t)) is int hp && EffectiveRemainingLife(actorOf(t), hp) is > 0 and var remaining && remaining <= killBudget ? 0 : 1)
            .ThenBy(t => ExpectedDamageOn(actorOf(t)) > 0 ? 0 : 1)
            .ThenBy(t => lifeOf(actorOf(t)) is int hp ? EffectiveRemainingLife(actorOf(t), hp) : int.MaxValue).ToList();

    public long? MostExposedAlly(IEnumerable<TeamAllyExposure> allies)
        => allies.Where(a => a.MaxLife > 0 && a.Life > 0)
            .OrderBy(a => (double)(ProtectedLife(a) - Math.Max(0, a.IncomingDamage)) / a.MaxLife)
            .ThenBy(a => a.Life).Select(a => (long?)a.ActorId).FirstOrDefault();

    public int EffectiveLife(long actor, int life, int maxLife)
    {
        var support = _current.SelectMany(i => i.Support).Where(s => s.AllyActorId == actor).ToArray();
        int baseline = support.Where(s => s.LifeBefore.HasValue).Select(s => s.LifeBefore!.Value)
            .DefaultIfEmpty(life).Min();
        int protectedLife = Math.Max(life, baseline + support.Where(s => s.LifeBefore.HasValue).Sum(s => Math.Max(0, s.Heal)));
        return Math.Min(maxLife, protectedLife + support.Where(s => !s.LifeBefore.HasValue).Sum(s => Math.Max(0, s.Heal)));
    }

    public int EffectiveShield(long actor, int observedShield)
    {
        var support = _current.SelectMany(i => i.Support).Where(s => s.AllyActorId == actor).ToArray();
        int shieldBaseline = support.Where(s => s.ShieldBefore.HasValue).Select(s => s.ShieldBefore!.Value)
            .DefaultIfEmpty(observedShield).Min();
        int shield = Math.Max(observedShield, shieldBaseline + support.Where(s => s.ShieldBefore.HasValue).Sum(s => Math.Max(0, s.Shield)));
        return shield + support.Where(s => !s.ShieldBefore.HasValue).Sum(s => Math.Max(0, s.Shield));
    }

    private int ProtectedLife(TeamAllyExposure ally)
        => EffectiveLife(ally.ActorId, ally.Life, ally.MaxLife) + EffectiveShield(ally.ActorId, ally.Shield);
}

/// <summary>One lock serializes round advancement, publication, snapshots and fight teardown.</summary>
public sealed class TeamIntentBoard
{
    private sealed class Fight
    {
        public int Round;
        public readonly Dictionary<(long Actor, int Round), TeamIntent> Intents = new();
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, Fight> _fights = new();

    public void Publish(string fightKey, TeamIntent intent)
    {
        lock (_gate)
        {
            var fight = Advance(fightKey, intent.Round);
            if (intent.Round < fight.Round - 1) return;
            var key = (intent.SelfActorId, intent.Round);
            if (fight.Intents.TryGetValue(key, out var previous) && previous.Timestamp > intent.Timestamp) return;
            fight.Intents[key] = intent with
            {
                Targets = Array.AsReadOnly(intent.Targets.ToArray()),
                Support = Array.AsReadOnly(intent.Support.ToArray())
            };
        }
    }

    public TeamIntentView Read(string fightKey, int round, long selfActorId = 0)
    {
        lock (_gate)
        {
            var fight = Advance(fightKey, round);
            return round >= fight.Round - 1
                ? new TeamIntentView(fight.Intents.Values.Where(i => i.SelfActorId != selfActorId && i.Round <= round && i.Round >= round - 1)
                    .GroupBy(i => i.SelfActorId).Select(g => g.OrderByDescending(i => i.Round).First()), round) : TeamIntentView.Empty;
        }
    }

    public void Clear(string fightKey) { lock (_gate) _fights.Remove(fightKey); }

    private Fight Advance(string key, int round)
    {
        if (!_fights.TryGetValue(key, out var fight)) _fights[key] = fight = new Fight { Round = round };
        if (round > fight.Round)
        {
            fight.Round = round;
            foreach (var actor in fight.Intents.Where(p => p.Value.Round < round - 1).Select(p => p.Key).ToArray())
                fight.Intents.Remove(actor);
        }
        return fight;
    }
}
