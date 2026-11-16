using System;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>
/// Snapshot de navigation releve par le workflow, jamais reconstruit a partir d'un mouvement optimiste.
/// Un resultat de SendAndWaitAsync ne fournit pas aujourd'hui la sequence dans son ACK :
/// ce module n'appellera donc pas ResumableRoute.Confirmed a sa place.
/// </summary>
public readonly record struct ResumableTravelSnapshot(
    ResumableRouteMap Map,
    long GeometryMap,
    long? PositionMap,
    int? ServerCell,
    bool PositionConfirmed,
    bool Interrupted,
    bool TransportSuspended);

public enum ResumableTravelProbeStatus
{
    ReadyForLegacyDispatch,
    AlreadyAtDestination,
    AwaitingCorrelatedAck,
    PositionUnconfirmed,
    GeometryStale,
    Interrupted,
    TransportSuspended,
    GenerationChanged,
    Unreachable,
    AttemptBudgetExceeded,
    InvalidInput
}

public readonly record struct ResumableTravelProbe(
    ResumableTravelProbeStatus Status,
    ResumableTravelWorkflow Workflow,
    ResumableRoute? AdvisoryRoute)
{
    // Uniquement une autorisation de tenter le MoveTo EXISTANT.
    // Il ne s'agit ni d'une confirmation d'ACK ni d'une admission pour rejouer un trajet en vol.
    public bool CanAttempt => Status == ResumableTravelProbeStatus.ReadyForLegacyDispatch;
}

/// <summary>
/// Diagnostic borne pour la commande Lua route.cell. Le trajet A* pur est verifie avant la
/// primitive MoveTo existante. Aucune sequence d'ACK n'etant exposee a ce niveau, le diagnostic
/// ne promeut jamais un echo/timeout en checkpoint; la reprise active reste fail-closed.
/// Immuable : l'hote remplace son instance entre deux observations, sans etat partage par PID.
/// </summary>
public sealed record ResumableTravelWorkflow
{
    public ResumableRouteMap Map { get; private init; }
    public int Destination { get; private init; }
    public int AttemptCount { get; private init; }
    public int MaxAttempts { get; private init; }
    /// <summary>Latched once a legacy send has been attempted; neither a pid-only TCS result
    /// nor a matching observed cell is a sequence-correlated ACK.</summary>
    public bool AwaitingCorrelatedAck { get; private init; }

    private ResumableTravelWorkflow() { }

    public static ResumableTravelWorkflow Start(ResumableRouteMap map, int destination, int maxAttempts = 3)
        => new() { Map = map, Destination = destination,
                   MaxAttempts = Math.Clamp(maxAttempts, 1, 8) };

    /// <summary>
    /// Un nouveau snapshot d'acteurs peut incrementer la generation sans changement de carte.
    /// Re-lier la preuve de carte conserve le budget de tentatives : aucun reset de boucle
    /// simplement parce qu'un event de snapshot est survenu.
    /// </summary>
    public ResumableTravelWorkflow RebindMapGeneration(ResumableRouteMap map)
        => map.IsValid && Map.MapId == map.MapId ? this with { Map = map } : this;

    /// <summary>
    /// Revalide position, generation et relief actuels; chaque appel recalcule les obstacles.
    /// Un diagnostic n'envoie jamais de mouvement et n'invente jamais de sequence serveur.
    /// </summary>
    public ResumableTravelProbe Probe(ResumableTravelSnapshot observation,
        Func<int, bool> walkable, Func<int, int, bool>? stepAllowed = null)
    {
        ResumableTravelProbe Deny(ResumableTravelProbeStatus reason) => new(reason, this, null);
        if (!Map.IsValid || (uint)Destination >= MapPathfinder.CellCount || walkable is null)
            return Deny(ResumableTravelProbeStatus.InvalidInput);
        if (AwaitingCorrelatedAck) return Deny(ResumableTravelProbeStatus.AwaitingCorrelatedAck);
        if (observation.Map != Map) return Deny(ResumableTravelProbeStatus.GenerationChanged);
        if (observation.Interrupted) return Deny(ResumableTravelProbeStatus.Interrupted);
        if (observation.TransportSuspended) return Deny(ResumableTravelProbeStatus.TransportSuspended);
        if (observation.GeometryMap != Map.MapId) return Deny(ResumableTravelProbeStatus.GeometryStale);
        if (!observation.PositionConfirmed || observation.PositionMap != Map.MapId ||
            observation.ServerCell is not int source || (uint)source >= MapPathfinder.CellCount)
            return Deny(ResumableTravelProbeStatus.PositionUnconfirmed);
        if (source == Destination) return Deny(ResumableTravelProbeStatus.AlreadyAtDestination);
        if (AttemptCount >= MaxAttempts) return Deny(ResumableTravelProbeStatus.AttemptBudgetExceeded);

        var route = ResumableRoute.Start(Map, source, Destination, walkable, true, stepAllowed);
        if (!route.CanDispatch || !MapPathfinder.IsMovementPathLegal(route.Cells, walkable, true, stepAllowed))
            return new(ResumableTravelProbeStatus.Unreachable, this, route);
        // Route exact du MapPathfinder, sans supposer que le prochain jrw utilisera ce plan.
        return new(ResumableTravelProbeStatus.ReadyForLegacyDispatch, this, route);
    }

    /// <summary>
    /// Doit etre appelee juste avant le MoveTo historique, et UNE fois seulement par tentative.
    /// L'ACK n'est pas traite ici : le budget subsiste meme si un TCS se termine sans provenance.
    /// </summary>
    public ResumableTravelWorkflow NoteLegacyAttempt(ResumableTravelProbe probe)
    {
        if (!ReferenceEquals(probe.Workflow, this) || !probe.CanAttempt || AttemptCount >= MaxAttempts)
            return this;
        return this with { AttemptCount = AttemptCount + 1, AwaitingCorrelatedAck = true };
    }
}
