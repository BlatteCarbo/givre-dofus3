using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

// ==================================================================================================================
// Compatibilité tiers — pré-combat du MOTEUR après prefightManagement / challengeManagement (GivreLuaHost.SnowScript.cs).
// Les callbacks un bot tiers ne PRENNENT PAS le placement: le hook de placement du script sert seulement à les appeler au bon
// moment (thread du script). Ensuite le moteur fait SON pré-combat, miroir de RunPreFight (FightManager.vb: Un bot tiers appelle
// prefightManagement puis se place quand même si « placement » est actif) : placement selon le style du profil (sauf si le
// script s'est placé lui-même), attente bornée de l'équipe, défi selon le profil (kty + attente de la gate), puis « prêt ».
// Aucun appel Lua ici (tâche de fond) ; jamais d'exception vers l'appelant.
// ==================================================================================================================
public sealed partial class LaunchService
{
    /// <summary>Lance le pré-combat du moteur pour un placement confié aux callbacks un bot tiers. <paramref name="place"/> = false:
    /// le script s'est déjà placé (combat:place / fightAction:chooseCell) -> pas de placement du moteur. true = le moteur a
    /// pris le pré-combat (il enverra « prêt ») ; false = rien lancé (droit Combat absent, placement fermé, pré-combat auto
    /// coupé dans le profil : comme sans hook, le moteur ne fait alors rien).</summary>
    public bool SnowStartEnginePreFight(uint pid, bool place)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight) || !IsPlacementOpen(pid)) return false;
        if (!_combat.TryGetValue(pid, out var live) || !live.AutoPreFight) return false;
        IReadOnlyList<int> cells = _placementCells.TryGetValue(pid, out var c) ? c : Array.Empty<int>();
        TransportLog.Line(place
            ? "[combat] prefightManagement terminé -> pré-combat du moteur (placement selon le mode)"
            : "[combat] prefightManagement s'est placé lui-même -> pré-combat du moteur sans placement");
        _ = SnowEnginePreFightAsync(pid, cells, place);
        return true;
    }

    /// <summary>challengeManagement terminé (chef / solo) : le script a eu sa chance -> la gate de défi est libérée, « prêt »
    /// part sans attendre les 8 s du mode « script ». Déjà libérée = sans effet.</summary>
    public void SnowReleaseChallengeGate(uint pid)
        => _challengeGate.GetOrAdd(pid, static _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(true);

    // MIROIR de RunPreFight (mêmes délais / attentes / règles follower), + placement optionnel et prêt unique (respecte un
    // combat:ready du script et le prêt d'office du budget). Anti-crash : prêt quoi qu'il arrive.
    private async Task SnowEnginePreFightAsync(uint pid, IReadOnlyList<int> myCells, bool place)
    {
        long fightStart = _fightStartAt.GetValueOrDefault(pid);
        _snowPreFightRunning[pid] = 0;
        try
        {
            if (!_combat.TryGetValue(pid, out var live) || !live.AutoPreFight) return;
            if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) return;
            var profile = live.Clone();
            int Step() => profile.Speed switch { CombatSpeed.Turbo => 20, CombatSpeed.Instant => 150, CombatSpeed.Fast => Random.Shared.Next(350, 700), _ => Random.Shared.Next(800, 1600) };   // Turbo : comme RunPreFight

            if (place)
            {
                // Comme RunPreFight : attendre qu'au moins un ENNEMI soit connu (poll borné ~1,6 s) avant de choisir la case.
                bool HasEnemies() => _fighters.TryGetValue(pid, out var ff) && ff.Any(x => IsEnemy(pid, x.ActorId));
                for (int w = 0; !HasEnemies() && w < 1600; w += 120)
                    await Task.Delay(120).ConfigureAwait(false);
                if (!HasEnemies()) await Task.Delay(Step()).ConfigureAwait(false);
                if (!IsCurrentTeamPlacement(pid, fightStart)) return;
                int cell = ChoosePlacementCell(pid, myCells, profile.Style);
                long myId = SelfActorId(pid);
                if (cell >= 0 && myId != 0 && cell != SelfFighterCell(pid) && IsPlacementOpen(pid) && !_scriptReadySent.GetValueOrDefault(pid))
                    SendPlacement(pid, myId, cell);
                TransportLog.Line($"[combat] placement mode={profile.PlacementMode} style={profile.Style} -> case {cell} ({PlacementDistanceWord(profile.PlacementMode, profile.Style)} ; {PlacementWhy(pid)})");
            }

            // Follower : ni défi ni attente d'équipe (le CHEF décide) -> prêt vite. Chef : attente des followers AVANT
            // défi et prêt (valider un défi ferme le combat aux nouveaux arrivants).
            bool follower = IsTeamFollower(pid);
            if (!IsCurrentTeamPlacement(pid, fightStart)) return;
            if (!follower && !await WaitTeamJoinBeforeChallengeAsync(pid, System.Threading.CancellationToken.None, untilJoined: true).ConfigureAwait(false)) return;
            // challengeManagement présent = mode « script » : le choix revient au script (combat:selectChallenge dans
            // challengeManagement, appelé par la pompe dès que les propositions arrivent) ; attente bornée comme RunPreFight.
            bool byScript = profile.ChallengeByScript && _scriptPlacementHook.ContainsKey(pid);
            if ((profile.AutoChallenge || byScript) && !follower && IsPlacementOpen(pid))
            {
                await Task.Delay(Step()).ConfigureAwait(false);
                if (!_challengeRequested.ContainsKey(pid))
                    SendChallengeRequest(pid);   // kty : déclenche les propositions (sinon rien en headless)
                var gate = _challengeGate.GetOrAdd(pid, static _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
                await Task.WhenAny(gate.Task, Task.Delay(byScript ? ScriptChallengeWaitMs : 4500)).ConfigureAwait(false);
            }
            await Task.Delay(follower ? Math.Min(Step(), 250) : Step()).ConfigureAwait(false);
            if (IsCurrentTeamPlacement(pid, fightStart)) SnowReadyOnce(pid);
        }
        catch { try { if (IsCurrentTeamPlacement(pid, fightStart)) SnowReadyOnce(pid); } catch { } }
        finally { if (_fightStartAt.GetValueOrDefault(pid) == fightStart) _snowPreFightRunning.TryRemove(pid, out _); }
    }

    // Pré-combat un bot tiers en cours (placement / attente d'équipe / gate de défi): ReleaseScriptCombat ne relance PAS RunPreFight
    // par-dessus (sinon 2e placement + 2e « prêt » si le script s'arrête pendant ce pré-combat).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, byte> _snowPreFightRunning = new();
    private bool SnowPreFightRunning(uint pid) => _snowPreFightRunning.ContainsKey(pid);

    // « Prêt » une seule fois : jamais après un combat:ready(true|false) du script ni après le prêt d'office du budget.
    private void SnowReadyOnce(uint pid)
    {
        if (!IsPlacementOpen(pid) || _scriptReadySent.GetValueOrDefault(pid) || _scriptReadyChosen.GetValueOrDefault(pid)) return;
        if (SendReady(pid)) _scriptReadySent[pid] = true;
    }
}
