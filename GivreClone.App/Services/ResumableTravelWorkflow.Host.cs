using System;
using System.Threading;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

public sealed partial class GivreLuaHost
{
    // Option de diagnostic par etape route.cell, strictement locale a ce script / personnage.
    // La sequence du MoveTo peut etre capturee avant injection, mais aucun ACK public ne
    // la re-fournit a SendAndWaitAsync : le re-dispatch ResumableRoute est donc INACTIF.
    private ResumableTravelWorkflow? _routeCellWorkflow;
    private ResumableTravelProbeStatus? _routeCellLastProbeStatus;
    private int _routeCellRunGeneration;

    private bool RunRouteCellWithRecoveryDiagnostic(int cell, CancellationToken ct)
    {
        long currentMap = _launch.CurrentMap(_pid) ?? 0;
        var state = _world.Get(_pid);
        long generation = Interlocked.Read(ref state.MapGeneration);
        var map = new ResumableRouteMap(currentMap, generation);
        if (_routeCellWorkflow is null || _routeCellWorkflow.Destination != cell ||
            _routeCellWorkflow.Map.MapId != map.MapId || _routeCellRunGeneration != _runGen)
        {
            _routeCellWorkflow = ResumableTravelWorkflow.Start(map, cell, maxAttempts: 1);
            _routeCellLastProbeStatus = null;
            _routeCellRunGeneration = _runGen;
        }
        else if (_routeCellWorkflow.Map != map)
            _routeCellWorkflow = _routeCellWorkflow.RebindMapGeneration(map);

        ResumableTravelSnapshot Observe()
        {
            // Ces champs sont publies ensemble par BotWorldStore pour les observations d'acteurs;
            // une lecture sous le meme gate evite de combiner la cellule d'avant avec l'epoque d'apres.
            lock (state.ActorPublicationGate)
                return new ResumableTravelSnapshot(
                    new ResumableRouteMap(_launch.CurrentMap(_pid) ?? 0,
                        Interlocked.Read(ref state.MapGeneration)),
                    state.CellsMap, state.SelfCellMap, state.SelfCell,
                    state.SelfReadyFor(currentMap), ct.IsCancellationRequested,
                    LinkSuspended || _launch.IsInFight(_pid));
        }

        var probe = _routeCellWorkflow.Probe(Observe(), state.IsWalkableForScript, state.StepAllowed);
        if (probe.Status == ResumableTravelProbeStatus.AlreadyAtDestination)
        {
            _routeCellLastProbeStatus = probe.Status;
            return true; // cellule deja observee et confirmee, aucun nouveau jrw
        }
        if (!probe.CanAttempt)
        {
            if (_routeCellLastProbeStatus != probe.Status)
                Warn("trajet", $"Diagnostic de reprise cellule {cell} : {probe.Status} " +
                     $"(carte={currentMap}, generation={generation}, tentatives={_routeCellWorkflow.AttemptCount}).");
            _routeCellLastProbeStatus = probe.Status;
            return false; // aucune action de l'etape ne depend d'une position speculative
        }

        // Validation de la route et des obstacles, SANS encodage ni envoi direct depuis
        // le diagnostic. GoToCell/MoveTo conservent leurs propres gardes et FIFO historiques.
        // Reprendre le snapshot juste avant l'appel permet de refuser un changement de
        // carte/generation survenu pendant le calcul du chemin.
        var atDispatch = Observe();
        if (atDispatch.Map != map || atDispatch.GeometryMap != currentMap ||
            !atDispatch.PositionConfirmed ||
            atDispatch.ServerCell != probe.AdvisoryRoute?.Checkpoint)
            return false;
        _routeCellWorkflow = _routeCellWorkflow.NoteLegacyAttempt(probe);
        _routeCellLastProbeStatus = null;
        bool reached = GoToCell(cell, ct);
        if (!reached) return false;

        // Un TCS Arrived ne transporte pas la sequence dans son ACK : on exige aussi
        // la vraie position self observee sur la meme generation pour poursuivre l'etape.
        var after = Observe();
        if (after.Map != map || !after.PositionConfirmed || after.ServerCell != cell)
        {
            Warn("trajet", $"Diagnostic de reprise cellule {cell} : arrivee non corroborée par la position serveur.");
            return false;
        }
        // Position observee != ACK correlable. Cette ancienne option reste un diagnostic
        // sans autorisation de re-executer la suite, meme si GoToCell a rendu Arrived.
        Warn("trajet", $"Diagnostic de reprise cellule {cell} : arrivee observee mais sequence ACK non disponible ; trajet suspendu.");
        return false;
    }
}
