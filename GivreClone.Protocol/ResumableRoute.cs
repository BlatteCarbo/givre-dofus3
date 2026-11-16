using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.Protocol;

/// <summary>
/// Carte et generation de session/transport fournies par l'appelant. Reconnecter sur la meme carte
/// exige une nouvelle generation : aucun evenement de l'ancienne session ne confirme la nouvelle.
/// </summary>
public readonly record struct ResumableRouteMap(long MapId, long Generation)
{
    public bool IsValid => MapId > 0 && Generation > 0;
}

/// <summary>Bornes imposees pour empecher de renvoyer indefiniment un trajet refuse ou immobile.</summary>
public sealed record ResumableRoutePolicy(
    int MaxDispatches = 8,
    int MaxStalls = 2,
    int MaxVisitsPerCell = 2,
    int MaxReplans = 4)
{
    public bool IsValid => MaxDispatches is >= 1 and <= 32 && MaxStalls is >= 1 and <= 8 &&
                           MaxVisitsPerCell is >= 1 and <= 8 && MaxReplans is >= 1 and <= 16;
}

public enum ResumableRoutePhase
{
    Ready,              // keyCells peuvent etre remis au transport une seule fois
    InFlight,           // aucun autre depart avant confirmation/refus authentique
    AwaitingPosition,   // refus sans case serveur prouvee : pas de reprise speculative
    Blocked,            // pas de chemin : Refresh peut reevaluer des obstacles mis a jour
    Completed,
    Exhausted,          // coupe-circuit atteint
    Invalidated         // carte/generation ou observation incoherentes
}

public enum ResumableRouteReason
{
    None, InvalidInput, NoPath, GenerationChanged, OffRouteCheckpoint,
    InvalidCheckpoint, DispatchBudget, StallBudget, VisitBudget, ReplanBudget, PositionUnconfirmed
}

/// <summary>
/// Planificateur immutable et SANS envoi reseau. Un seul trajet peut etre InFlight a la fois.
/// Integration : Start/Refresh -> si Ready, obtenir KeyCells; appeler BeginDispatch(map, seq) AVANT
/// d'enfiler le deplacement (seq reel du dispatcher, jamais un ACK invente); si l'enfilement echoue,
/// DispatchFailed(seq); a la resolution serveur, appeler Confirmed/Refused uniquement pour
/// le personnage et la sequence correles, avec une position reellement confirmee.
/// Un echo de mouvement ou une mise en file ne certifie PAS la position.
/// Toute methode retourne un NOUVEL etat; l'appelant doit enregistrer le retour atomiquement.
/// </summary>
public sealed record ResumableRoute
{
    private int[] _cells = Array.Empty<int>();
    private int[] _keys = Array.Empty<int>();
    private byte[] _visits = new byte[MapPathfinder.CellCount];
    private ResumableRoutePolicy _policy = new();

    public ResumableRouteMap Map { get; private init; }
    public int Checkpoint { get; private init; }
    public int Destination { get; private init; }
    public bool AllowDiagonal { get; private init; }
    public ResumableRoutePhase Phase { get; private init; }
    public ResumableRouteReason Reason { get; private init; }
    public long? PendingSequence { get; private init; }
    public long LastSequence { get; private init; }
    public int DispatchCount { get; private init; }
    public int StallCount { get; private init; }
    public int ReplanCount { get; private init; }
    public bool CanDispatch => Phase == ResumableRoutePhase.Ready && _keys.Length > 0;

    /// <summary>Cellules du trajet exact restant, source observee et destination comprises.</summary>
    public IReadOnlyList<int> Cells => Array.AsReadOnly(_cells);

    /// <summary>Encodage EXACT de la route restante, pret pour MapMovementRequest (pas un ordre d'envoi).</summary>
    public IReadOnlyList<int> KeyCells => Array.AsReadOnly(_keys);

    private ResumableRoute() { }

    public static ResumableRoute Start(ResumableRouteMap map, int source, int destination,
        Func<int, bool> walkable, bool allowDiagonal = true,
        Func<int, int, bool>? stepAllowed = null, ResumableRoutePolicy? policy = null)
    {
        var limits = policy ?? new ResumableRoutePolicy();
        var state = new ResumableRoute
        {
            Map = map, Checkpoint = source, Destination = destination,
            AllowDiagonal = allowDiagonal, _policy = limits
        };
        if (!map.IsValid || (uint)source >= MapPathfinder.CellCount ||
            (uint)destination >= MapPathfinder.CellCount || walkable is null || !limits.IsValid)
            return state with { Phase = ResumableRoutePhase.Invalidated, Reason = ResumableRouteReason.InvalidInput };

        state._visits[source] = 1;
        if (source == destination)
            return state with { Phase = ResumableRoutePhase.Completed };
        return state.PlanNewPath(walkable, stepAllowed, countReplan: false);
    }

    /// <summary>
    /// Arme le verrou FIFO AVANT l'injection transport; la sequence est fournie par le dispatcher.
    /// Appeler DispatchFailed si l'injection n'a pas ete acceptee. Un meme seq ne peut etre arme deux fois.
    /// </summary>
    public ResumableRoute BeginDispatch(ResumableRouteMap map, long sequence)
    {
        if (map != Map) return Stop(ResumableRoutePhase.Invalidated, ResumableRouteReason.GenerationChanged);
        if (!CanDispatch || sequence <= LastSequence || sequence <= 0) return this;
        if (DispatchCount >= _policy.MaxDispatches)
            return Stop(ResumableRoutePhase.Exhausted, ResumableRouteReason.DispatchBudget);
        return this with
        {
            Phase = ResumableRoutePhase.InFlight, Reason = ResumableRouteReason.None,
            PendingSequence = sequence, LastSequence = sequence, DispatchCount = DispatchCount + 1
        };
    }

    /// <summary>Echec local de la mise en file; ce n'est PAS un refus du serveur.</summary>
    public ResumableRoute DispatchFailed(ResumableRouteMap map, long sequence,
        Func<int, bool> walkable, Func<int, int, bool>? stepAllowed = null)
    {
        if (map != Map || Phase != ResumableRoutePhase.InFlight || PendingSequence != sequence)
            return this;
        if (DispatchCount >= _policy.MaxDispatches)
            return Stop(ResumableRoutePhase.Exhausted, ResumableRouteReason.DispatchBudget);
        return (this with { Phase = ResumableRoutePhase.Ready, PendingSequence = null })
            .Refresh(map, walkable, stepAllowed);
    }

    /// <summary>
    /// Progression uniquement si le transport a resolu le mouvement ET si la cellule du personnage
    /// est confirmee par le serveur. Les echos anticipes et sequences perimees ne changent rien.
    /// Pour une route serveur alternative, la destination finale reste admissible.
    /// </summary>
    public ResumableRoute Confirmed(ResumableRouteMap map, long sequence, int observedCell,
        bool positionConfirmed, Func<int, bool> walkable, Func<int, int, bool>? stepAllowed = null)
    {
        if (map != Map || Phase != ResumableRoutePhase.InFlight || PendingSequence != sequence ||
            !positionConfirmed) return this;
        return ResumeAt(observedCell, walkable, stepAllowed, enforceIssuedPath: true);
    }

    /// <summary>
    /// Refus serveur correle. Une position manquante verrouille le trajet jusqu'a ReanchorAfterRefusal.
    /// Une position refusee sur le trajet peut fournir un vrai checkpoint partiel.
    /// </summary>
    public ResumableRoute Refused(ResumableRouteMap map, long sequence, int? observedCell,
        bool positionConfirmed, Func<int, bool> walkable, Func<int, int, bool>? stepAllowed = null)
    {
        if (map != Map || Phase != ResumableRoutePhase.InFlight || PendingSequence != sequence)
            return this;
        if (!positionConfirmed || observedCell is null)
            return this with
            {
                Phase = ResumableRoutePhase.AwaitingPosition, Reason = ResumableRouteReason.PositionUnconfirmed,
                _cells = Array.Empty<int>(), _keys = Array.Empty<int>()
            };
        return ResumeAt(observedCell.Value, walkable, stepAllowed, enforceIssuedPath: true);
    }

    /// <summary>
    /// Apres un refus illisible, une nouvelle observation du personnage, verifiee et correlee
    /// a la meme sequence/transport, peut autoriser une replanification depuis la case reelle.
    /// L'appelant doit assurer la provenance; on ne fabrique jamais de cellule ni d'ACK.
    /// </summary>
    public ResumableRoute ReanchorAfterRefusal(ResumableRouteMap map, long sequence, int observedCell,
        bool positionConfirmed, Func<int, bool> walkable, Func<int, int, bool>? stepAllowed = null)
    {
        if (map != Map || Phase != ResumableRoutePhase.AwaitingPosition ||
            PendingSequence != sequence || !positionConfirmed) return this;
        if ((uint)observedCell >= MapPathfinder.CellCount)
            return Stop(ResumableRoutePhase.Invalidated, ResumableRouteReason.InvalidCheckpoint);
        return ResumeAt(observedCell, walkable, stepAllowed, enforceIssuedPath: false);
    }

    /// <summary>
    /// Reevalue la geometrie AVANT un nouveau depart. InFlight reste verrouille jusqu'a resolution;
    /// une nouvelle generation invalide toujours le plan. Aucune memoisation de walkable/stepAllowed.
    /// </summary>
    public ResumableRoute Refresh(ResumableRouteMap map,
        Func<int, bool> walkable, Func<int, int, bool>? stepAllowed = null)
    {
        if (map != Map) return Stop(ResumableRoutePhase.Invalidated, ResumableRouteReason.GenerationChanged);
        if (walkable is null) return Stop(ResumableRoutePhase.Invalidated, ResumableRouteReason.InvalidInput);
        if (Phase is not (ResumableRoutePhase.Ready or ResumableRoutePhase.Blocked)) return this;
        if (Phase == ResumableRoutePhase.Ready &&
            MapPathfinder.IsMovementPathLegal(_cells, walkable, AllowDiagonal, stepAllowed)) return this;
        return PlanNewPath(walkable, stepAllowed, countReplan: true);
    }

    private ResumableRoute ResumeAt(int cell, Func<int, bool> walkable,
        Func<int, int, bool>? stepAllowed, bool enforceIssuedPath)
    {
        if (walkable is null || (uint)cell >= MapPathfinder.CellCount)
            return Stop(ResumableRoutePhase.Invalidated, ResumableRouteReason.InvalidCheckpoint);

        int index = Array.IndexOf(_cells, cell);
        if (enforceIssuedPath && index < 0)
            return Stop(ResumableRoutePhase.Invalidated, ResumableRouteReason.OffRouteCheckpoint);

        bool progressed = cell != Checkpoint;
        int stalls = progressed ? 0 : StallCount + 1;
        if (stalls >= _policy.MaxStalls)
            return Stop(ResumableRoutePhase.Exhausted, ResumableRouteReason.StallBudget);
        var visits = (byte[])_visits.Clone();
        if (progressed && ++visits[cell] > _policy.MaxVisitsPerCell)
            return Stop(ResumableRoutePhase.Exhausted, ResumableRouteReason.VisitBudget);

        var route = this with
        {
            Checkpoint = cell, StallCount = stalls, _visits = visits, PendingSequence = null,
            Reason = ResumableRouteReason.None
        };
        if (cell == Destination)
            return route with { Phase = ResumableRoutePhase.Completed, _cells = Array.Empty<int>(), _keys = Array.Empty<int>() };
        if (DispatchCount >= _policy.MaxDispatches)
            return route.Stop(ResumableRoutePhase.Exhausted, ResumableRouteReason.DispatchBudget);

        if (index >= 0 && index < _cells.Length - 1)
        {
            var suffix = _cells[index..];
            if (MapPathfinder.IsMovementPathLegal(suffix, walkable, AllowDiagonal, stepAllowed))
                return route.WithPath(suffix);
        }
        return route.PlanNewPath(walkable, stepAllowed, countReplan: true);
    }

    private ResumableRoute PlanNewPath(Func<int, bool> walkable, Func<int, int, bool>? stepAllowed,
        bool countReplan)
    {
        if (countReplan && ReplanCount >= _policy.MaxReplans)
            return Stop(ResumableRoutePhase.Exhausted, ResumableRouteReason.ReplanBudget);
        var cells = MapPathfinder.PathCells(Checkpoint, Destination, walkable, AllowDiagonal, stepAllowed);
        var next = this with { ReplanCount = ReplanCount + (countReplan ? 1 : 0) };
        if (cells.Count < 2)
            return next with { Phase = ResumableRoutePhase.Blocked, Reason = ResumableRouteReason.NoPath,
                PendingSequence = null, _cells = Array.Empty<int>(), _keys = Array.Empty<int>() };
        return next.WithPath(cells.ToArray());
    }

    private ResumableRoute WithPath(int[] cells)
    {
        // Compress est exactement le meme encodeur que KeyMovements, sans heuristique de reprise.
        var keys = MapPathfinder.Compress(cells.ToList()).ToArray();
        return this with
        {
            Phase = ResumableRoutePhase.Ready, Reason = ResumableRouteReason.None,
            PendingSequence = null, _cells = cells, _keys = keys
        };
    }

    private ResumableRoute Stop(ResumableRoutePhase phase, ResumableRouteReason reason) =>
        this with
        {
            Phase = phase, Reason = reason, PendingSequence = null,
            _cells = Array.Empty<int>(), _keys = Array.Empty<int>()
        };
}
