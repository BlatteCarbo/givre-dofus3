using System.Collections.Generic;

namespace GivreClone.App.Services.Combat;

/// <summary>Ordre de jeu DÉDUIT des débuts de tour (aucun message du jeu ne donne la liste) : chaque nouveau combattant qui joue est
/// ajouté à la suite ; au premier retour d'un combattant déjà vu, le tour de table est complet. Une invocation apparue plus tard
/// s'insère là où elle a joué pour la première fois.</summary>
public sealed class TurnOrder
{
    private readonly List<long> _order = new();
    public IReadOnlyList<long> Order => _order;
    public long Current { get; private set; }
    /// <summary>Vrai dès qu'un combattant rejoue : l'ordre couvre alors tout le monde (sauf invocations à venir).</summary>
    public bool Complete { get; private set; }

    public void OnTurn(long actorId)
    {
        if (actorId == 0) return;
        int at = _order.IndexOf(actorId);
        if (at >= 0) Complete = true;
        else
        {
            // Nouveau venu après un tour complet (invocation) : juste après celui qui jouait avant lui.
            int prev = _order.IndexOf(Current);
            if (Complete && prev >= 0) _order.Insert(prev + 1, actorId);
            else _order.Add(actorId);
        }
        Current = actorId;
    }
}
