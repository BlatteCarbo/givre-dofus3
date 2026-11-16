using System.Collections.Concurrent;
using System.Collections.Generic;
using GivreClone.App.Services.Combat;

namespace GivreClone.App.Services;

// Frise d'ordre de jeu : déduite des FightTurnEvent successifs (remise à zéro à FightStartingEvent). Lecture seule.
public sealed partial class LaunchService
{
    private readonly ConcurrentDictionary<uint, TurnOrder> _turnOrder = new();

    private void ObserveTurnOrder(uint pid, long actorId)
    {
        var t = _turnOrder.GetOrAdd(pid, _ => new TurnOrder());
        lock (t) t.OnTurn(actorId);
    }

    /// <summary>Ordre de jeu connu (copie), combattant en cours et vrai si le tour de table est complet.</summary>
    public (IReadOnlyList<long> Order, long Current, bool Complete) FightTurnOrder(uint pid)
    {
        if (!_turnOrder.TryGetValue(pid, out var t)) return (System.Array.Empty<long>(), 0, false);
        lock (t) return (new List<long>(t.Order), t.Current, t.Complete);
    }
}
