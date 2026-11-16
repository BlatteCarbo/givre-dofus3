using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.PhoneAlerts;

/// <summary>Un groupe de monstres sur la carte du bot : identifiant de l'acteur et ses membres (id de monstre, nom).</summary>
public sealed record SeenGroup(long ActorId, IReadOnlyList<(int MonsterId, string Name)> Members);

public sealed record ArchAlert(uint Pid, long MapId, long ActorId, string Names);

/// <summary>Détecte l'apparition d'un archimonstre / monstre à âme sur la carte d'un bot et ne le signale qu'une fois par groupe et par carte, puis pas avant <see cref="Cooldown"/> pour le même
/// monstre sur la même carte (les groupes repoussent). Pur : la liste des groupes, le test « capturable » et l'heure sont fournis.</summary>
public sealed class ArchAlertTracker
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);
    private readonly Func<int, bool> _isNotable;
    private readonly Dictionary<(uint Pid, long Map, int Monster), long> _lastAlert = new();
    private readonly HashSet<(uint Pid, long Map, long Actor)> _seen = new();
    private readonly Dictionary<uint, long> _mapOf = new();

    public ArchAlertTracker(Func<int, bool> isNotable) => _isNotable = isNotable;

    /// <summary>À appeler à chaque relevé de la carte d'un bot. Retourne les nouvelles alertes (vide la plupart du temps).</summary>
    public IReadOnlyList<ArchAlert> Observe(uint pid, long mapId, IReadOnlyList<SeenGroup> groups, long nowMs)
    {
        if (_mapOf.TryGetValue(pid, out var prev) && prev != mapId) _seen.RemoveWhere(s => s.Pid == pid);   // nouvelle carte : on repart de zéro
        _mapOf[pid] = mapId;
        var res = new List<ArchAlert>();
        foreach (var g in groups)
        {
            var notable = g.Members.Where(m => _isNotable(m.MonsterId)).ToList();
            if (notable.Count == 0 || !_seen.Add((pid, mapId, g.ActorId))) continue;
            bool fresh = false;
            foreach (var m in notable)
            {
                var key = (pid, mapId, m.MonsterId);
                if (_lastAlert.TryGetValue(key, out var last) && nowMs - last < (long)Cooldown.TotalMilliseconds) continue;
                _lastAlert[key] = nowMs; fresh = true;
            }
            if (fresh) res.Add(new ArchAlert(pid, mapId, g.ActorId, string.Join(" + ", notable.Select(m => m.Name).Distinct())));
        }
        if (_lastAlert.Count > 4000) _lastAlert.Clear();
        return res;
    }

    /// <summary>Oublie un bot (déconnecté).</summary>
    public void Forget(uint pid) { _mapOf.Remove(pid); _seen.RemoveWhere(s => s.Pid == pid); }
}
