using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    // Publier tout le recalage AVANT de réveiller le script : RunContinuationsAsynchronously
    // ne garantit pas que la continuation attendra la fin du handler réseau.
    private void ApplyMovementRefusal(uint pid, (int X, int Y, int Cell)? position)
    {
        _botMoveConfirm.TryRemove(pid, out _);
        _pendingMapChange.TryRemove(pid, out _);
        _pendingHarvest.TryRemove(pid, out _);
        _pendingAttack.TryRemove(pid, out _);
        _movingUntil.TryRemove(pid, out _);
        if (position is { } r)
        {
            SelfMoved?.Invoke(pid, new[] { r.Cell }); // annule aussi l'animation UI
            SelfPositionCorrected?.Invoke(pid, r.Cell); // vérité serveur, jamais une prédiction
            if (_inFight.TryGetValue(pid, out var inFight) && inFight) SetSelfFighterCell(pid, r.Cell);
            TransportLog.Line($"[move] REFUSÉ (MapMovementRefusedEvent) x={r.X} y={r.Y} -> position self recalee sur cellule reelle {r.Cell}");
        }
        else TransportLog.Line("[move] REFUSÉ (MapMovementRefusedEvent) - coordonnees illisibles, position non recalee");
        // Un refus sans coordonnées reste un refus : le polling de trajet doit sortir aussi.
        _moveRefused.AddOrUpdate(pid, 1, static (_, n) => n + 1);
        _moveGuardUntil[pid] = 0;
        RaiseActivity(pid);
        SettleMove(pid, MoveOutcome.Refused);
    }
}
