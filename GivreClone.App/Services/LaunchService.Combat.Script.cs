using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Transport;
using GivreClone.Engine.Accounts;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Ankama;
using GivreLauncher.Core.Launch;
using GivreLauncher.Core.Proxy;
using GivreLauncher.Core.Transport;
using GivreLauncher.Core.Zaap;
using CoreAccount = GivreLauncher.Core.Accounts.Account;

namespace GivreClone.App.Services;

// ===================== Combat piloté par script (Lua) : tour onTurn, lectures et verbes d'action, placement onPlacement =====================
public sealed partial class LaunchService
{
    // ── TOUR PILOTÉ PAR SCRIPT (Lot C1 : hook Lua onTurn/onPlacement) ─────────────────────────────────────────────────
    // Le moteur ne JOUE PAS le tour : il le « tend » au thread du script (jeton d'époque en attente), qui le RÉCLAME en
    // sondant (TryClaimScriptTurn). Les gates d'autorité (jzc / front jyj / _myTurnOpen) sont les MÊMES que RunFightTurn —
    // jamais de récence ni de fail-open jyj=0 côté script (risque de tour fantôme). Aucun hook + aucun override = moteur
    // strictement inchangé (EngineAutoPlays ≡ profil.AutoFight).
    private sealed class ScriptTurnState
    {
        public int Epoch, TurnNo, ApLeft, MpLeft, TotalCasts;
        public int ApObservation, MpObservation;
        public int Ended;        // 0/1 Interlocked : jxy déjà envoyé pour CE tour script (endTurn / auto / budget / playTurn)
        public int Expired;      // 0/1 : budget dépassé (l'hôte le journalise une fois revenu sur le thread du script)
        public volatile bool EnginePlayed;   // playTurn() a confié CE tour à RunFightTurn
        public volatile bool Acted;          // au moins un lancer/déplacement injecté pour CE tour (repli moteur sur erreur du hook)
        public volatile bool ViaJyj;         // réclamé par le repli front jyj (jzc pas encore vu) : un jzc tardif le RÉUTILISE
        public volatile object? Owner;       // jeton du run de script qui a réclamé CE tour (reprise par un run relancé)
        public readonly Dictionary<int, int> CastsPerSpell = new();
        public readonly Dictionary<(int Spell, long Actor), int> CastsPerTarget = new();
    }
    private readonly ConcurrentDictionary<uint, ScriptTurnState> _scriptTurn = new();
    private readonly ConcurrentDictionary<uint, int> _scriptTurnPending = new();          // pid -> époque d'un tour pas encore réclamé
    private readonly ConcurrentDictionary<uint, object> _scriptTurnHook = new();          // pid -> jeton du run qui a onTurn
    private readonly ConcurrentDictionary<uint, object> _scriptPlacementHook = new();     // pid -> jeton du run qui a onPlacement
    private readonly ConcurrentDictionary<uint, (object Owner, bool Value)> _autoFightOverride = new();
    private readonly ConcurrentDictionary<uint, IReadOnlyList<int>> _placementCells = new();
    private readonly ConcurrentDictionary<uint, IReadOnlyList<int>> _scriptPlacementPending = new();
    private readonly ConcurrentDictionary<uint, bool> _placementOpen = new();
    private readonly ConcurrentDictionary<uint, bool> _scriptReadySent = new();
    private readonly ConcurrentDictionary<uint, bool> _scriptReadyChosen = new();   // combat:ready() appelé (vrai OU faux) : pas de prêt auto
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<long, byte>> _summonIds = new();   // toute invocation vue (tout propriétaire)
    private const int ScriptClaimTimeoutMs = 8000;
    // Repli SANS jzc (front jyj) : on laisse ce délai à un jzc éventuel pour arriver (il suit jyj de 60-310 ms à
    // l'ouverture d'un combat) -> sinon un même tour serait réclamé deux fois (front jyj PUIS jzc).
    private const long ScriptJyjGraceMs = 500;

    /// <summary>État de tour script du tour COURANT (époque identique), sinon null.</summary>
    private ScriptTurnState? ActiveScriptTurn(uint pid)
        => _scriptTurn.TryGetValue(pid, out var st) && st.Epoch == _turnEpoch.GetValueOrDefault(pid) ? st : null;

    /// <summary>Le MOTEUR joue-t-il les tours de ce bot ? Sans hook ni override = EXACTEMENT profil.AutoFight (comme avant).</summary>
    private bool EngineAutoPlays(uint pid)
    {
        if (ManualCombatOn(pid)) return false;
        if (!_combat.TryGetValue(pid, out var pr)) return false;
        if (_autoFightOverride.TryGetValue(pid, out var ov)) return ov.Value && LicenseService.HasPermission(LicenseService.Perm.Fight);
        return pr.AutoFight && !_scriptTurnHook.ContainsKey(pid);
    }
    private bool ScriptDrivesTurns(uint pid) => !ManualCombatOn(pid) && _scriptTurnHook.ContainsKey(pid) && !EngineAutoPlays(pid)
        && LicenseService.HasPermission(LicenseService.Perm.Fight);
    private bool ScriptOwnsPlacement(uint pid) => !ManualCombatOn(pid) && _scriptPlacementHook.ContainsKey(pid) && LicenseService.HasPermission(LicenseService.Perm.Fight);
    /// <summary>Le moteur joue-t-il les tours de ce bot (profil + override script) ? Pour combat:isAutoFight.</summary>
    public bool IsAutoFightEffective(uint pid) => EngineAutoPlays(pid);

    // Remise à zéro de l'état script de combat (début/fin de combat, oubli du pid). Les hooks/override (propriété du RUN
    // de script, pas du combat) ne sont PAS touchés ici.
    private void ResetScriptFightState(uint pid)
    {
        _scriptTurn.TryRemove(pid, out _); _scriptTurnPending.TryRemove(pid, out _); _scriptPlacementPending.TryRemove(pid, out _);
        _placementOpen.TryRemove(pid, out _); _placementCells.TryRemove(pid, out _); _scriptReadySent.TryRemove(pid, out _);
        _scriptReadyChosen.TryRemove(pid, out _);
        _summonIds.TryRemove(pid, out _);
    }

    // Budget partagé moteur/script : les points courants d'un combattant sont autoritaires, zéro compris.
    // La fiche hors combat et le repli 6/3 servent uniquement en l'absence de stats du combattant.
    private (int Ap, int Mp) TurnStartBudget(uint pid)
    {
        if (SelfStats(pid) is { FighterId: not 0 } live)
            return (System.Math.Max(0, live.Pa), System.Math.Max(0, live.Pm));
        if (EngineControlled(pid) is not null) return (0, 0);
        int ap = 6, mp = 3; bool apLive = false;
        if (SelfStats(pid) is { } s)
        {
            ap = s.Pa > 0 ? s.Pa : (s.MaxPa > 0 ? s.MaxPa : 6);
            mp = s.Pm > 0 ? s.Pm : (s.MaxPm > 0 ? s.MaxPm : 3);
            apLive = s.Pa > 0;
        }
        if (EngineControlled(pid) is null && _fighterStats.TryGetValue(pid, out var byId) && byId.TryGetValue(0, out var perso))
        {
            mp = System.Math.Max(mp, perso.MaxPm > 0 ? perso.MaxPm : perso.Pm);
            if (!apLive) ap = System.Math.Max(ap, perso.MaxPa > 0 ? perso.MaxPa : perso.Pa);
        }
        return (ap, mp);
    }

    /// <summary>Thread du SCRIPT (pompe) : réclame le tour tendu au script. Renvoie l'époque (&gt;0) à jouer, 0 = rien.
    /// Autorité = jeton jzc (époque + fenêtre ouverte), sinon UN front jyj non consommé (même règle que WaitTurnPlayEdge,
    /// SANS le fail-open jyj=0).</summary>
    public int TryClaimScriptTurn(uint pid)
    {
        if (!IsInFight(pid) || !ScriptDrivesTurns(pid)) return 0;
        object? owner = _scriptTurnHook.TryGetValue(pid, out var ow) ? ow : null;
        // Script RELANCÉ en plein tour (planning / anti-blocage / utilisateur) : le tour réclamé par l'ancien run, ni fini
        // ni confié au moteur, est REPRIS tel quel (compteurs conservés, pas de nouveau n° de tour) par le nouveau run.
        if (owner is not null && ActiveScriptTurn(pid) is { } orphan && orphan.Owner is not null && !ReferenceEquals(orphan.Owner, owner)
            && orphan.Ended == 0 && orphan.Expired == 0 && !orphan.EnginePlayed && _myTurnOpen.GetValueOrDefault(pid))
        {
            orphan.Owner = owner;
            TransportLog.Line(pid, $"[combat] tour {_teamIntentRound.GetValueOrDefault(pid, orphan.TurnNo)} repris par le script relancé");
            return orphan.Epoch;
        }
        int ep; bool viaJyj = false;
        if (_jzcSeen.GetValueOrDefault(pid))
        {
            if (!_scriptTurnPending.TryRemove(pid, out ep)) return 0;
            if (ep != _turnEpoch.GetValueOrDefault(pid) || !_myTurnOpen.GetValueOrDefault(pid)) return 0;   // tour déjà refermé
        }
        else
        {
            int consumed = _jyjConsumed.GetValueOrDefault(pid), edges = _jyjCount.GetValueOrDefault(pid);
            if (edges <= consumed) return 0;
            long nowMs = System.Environment.TickCount64, lastPlay = _lastTurnPlay.GetValueOrDefault(pid);
            if (nowMs - lastPlay < ScriptJyjGraceMs) return 0;   // laisse sa chance à jzc
            // Front PÉRIMÉ (notre jxy est postérieur au dernier jyj, ou front trop vieux : le thread du script était occupé
            // et le serveur a déjà fait expirer le tour) : on le CONSOMME sans ouvrir de tour -> jamais d'action hors tour.
            if (_turnEndedAt.GetValueOrDefault(pid) >= lastPlay || nowMs - lastPlay > ScriptClaimTimeoutMs)
            {
                _jyjConsumed[pid] = edges;
                TransportLog.Line("[combat] début de tour périmé ignoré (tour script non réclamé à temps)");
                return 0;
            }
            _jyjConsumed[pid] = edges; _myTurnOpen[pid] = true;
            ep = _turnEpoch.AddOrUpdate(pid, 1, static (_, v) => v + 1);
            viaJyj = true;
        }
        int turnNo = _fightTurnNo.AddOrUpdate(pid, 1, static (_, v) => v + 1);
        var (ap, mp) = TurnStartBudget(pid);
        _scriptTurn[pid] = new ScriptTurnState { Epoch = ep, TurnNo = turnNo, ApLeft = ap, MpLeft = mp, ViaJyj = viaJyj, Owner = owner };
        _blockedCells.TryRemove(pid, out _); _moveGuardUntil[pid] = 0; _seqArmIsq[pid] = _isqCount.GetValueOrDefault(pid);   // même mise en place de tour que RunFightTurn
        TransportLog.Line(pid, $"[combat] tour {_teamIntentRound.GetValueOrDefault(pid, turnNo)} pris en charge par le script (PA{ap} PM{mp})");
        return ep;
    }

    private bool TryEndScriptTurn(uint pid, int ep, string why)
    {
        var st = ActiveScriptTurn(pid);
        if (st is null || st.Epoch != ep) return false;
        if (Interlocked.CompareExchange(ref st.Ended, 1, 0) != 0) return false;
        if (!_myTurnOpen.GetValueOrDefault(pid)) return false;   // fenêtre déjà refermée : JAMAIS de jxy hors tour (tour fantôme)
        TransportLog.Line($"[combat] fin du tour script ({why})");
        return SendEndTurn(pid);
    }

    /// <summary>Fin de tour AUTOMATIQUE après le retour de onTurn (si le script ne l'a pas déjà fini). Délai = cadence du profil.</summary>
    public async Task FinishScriptTurnAsync(uint pid, int ep, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var speed = _combat.TryGetValue(pid, out var pr) ? pr.Speed : CombatSpeed.Fast;
        int delay = speed switch { CombatSpeed.Turbo or CombatSpeed.Instant => 150, CombatSpeed.Fast => Random.Shared.Next(300, 700), _ => Random.Shared.Next(700, 1500) };
        var st = ActiveScriptTurn(pid);
        if (st is null || st.Epoch != ep || st.Ended == 1 || !_myTurnOpen.GetValueOrDefault(pid)) return;   // rien à finir -> pas d'attente inutile
        // Les verbes Turbo script (cast/move) attendent eux-mêmes SequenceEnd(self)+ACK visible correspondant. Si le hook
        // n'a joué aucune action, jxy peut partir immédiatement; sinon la sécurité a déjà été payée à la dernière action.
        if (speed != CombatSpeed.Turbo || IsSocketPid(pid)) await Task.Delay(delay, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (IsInFight(pid)) TryEndScriptTurn(pid, ep, "fin de onTurn");
    }

    /// <summary>Timer (thread pool) : budget du tour script écoulé -> fin de tour d'office. Aucun appel Lua ici.</summary>
    public void ExpireScriptTurn(uint pid, int ep)
    {
        if (!_scriptTurn.TryGetValue(pid, out var st) || st.Epoch != ep || st.EnginePlayed) return;   // moteur aux commandes : il gère son rythme
        Interlocked.Exchange(ref st.Expired, 1);
        TryEndScriptTurn(pid, ep, "budget");
    }
    public bool ScriptTurnExpired(uint pid, int ep) => _scriptTurn.TryGetValue(pid, out var st) && st.Epoch == ep && st.Expired == 1;

    // Filet : tour tendu au script mais jamais réclamé (thread du script bloqué dans un appel Lua) -> on passe le tour
    // plutôt que de laisser le serveur le faire expirer.
    private void ArmScriptClaimTimeout(uint pid, int ep)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(ScriptClaimTimeoutMs).ConfigureAwait(false);
            if (_scriptTurnPending.TryRemove(new KeyValuePair<uint, int>(pid, ep))
                && ep == _turnEpoch.GetValueOrDefault(pid) && _myTurnOpen.GetValueOrDefault(pid))
            { TransportLog.Line("[combat] le script n'a pas pris son tour à temps -> tour passé"); SendEndTurn(pid); }
        });
    }

    /// <summary>combat:playTurn() : le MOTEUR joue entièrement le tour script COURANT (rotation du profil), puis le finit.</summary>
    public async Task<bool> PlayTurnForScriptAsync(uint pid, int expectEpoch = 0, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) return false;
        if (HookEpochStale(pid, expectEpoch)) return false;
        var st = ActiveScriptTurn(pid);
        if (st is null || st.EnginePlayed || st.Ended == 1 || !_myTurnOpen.GetValueOrDefault(pid) || !_combat.ContainsKey(pid)) return false;
        st.EnginePlayed = true;
        try { await RunFightTurn(pid, st.Epoch, scriptDriven: true, ct: ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            st.EnginePlayed = false; // ReleaseScriptCombat can restore the independent profile engine.
            throw;
        }
        Interlocked.Exchange(ref st.Ended, 1);
        return true;
    }

    /// <summary>Déclare les hooks de combat du run de script <paramref name="token"/> pour ce bot.</summary>
    public void SetScriptFightHooks(uint pid, object token, bool onTurn, bool onPlacement)
    {
        if (onTurn) _scriptTurnHook[pid] = token;
        if (onPlacement) _scriptPlacementHook[pid] = token;
    }

    /// <summary>Retire le hook de tour du run <paramref name="token"/> (le moteur reprend selon le profil). Sans effet sinon.</summary>
    public void DropScriptTurnHook(uint pid, object token)
    {
        if (_scriptTurnHook.TryGetValue(pid, out var th) && ReferenceEquals(th, token))
            _scriptTurnHook.TryRemove(new KeyValuePair<uint, object>(pid, th));
    }

    /// <summary>Auto-combat du PROFIL (case UI), sans override ni hook.</summary>
    public bool ProfileAutoFight(uint pid) => _combat.TryGetValue(pid, out var pr) && pr.AutoFight;

    /// <summary>Action émise depuis un hook de tour (<paramref name="expectEpoch"/> = époque RÉCLAMÉE par ce hook, 0 = hors
    /// hook) : refusée si ce tour n'est plus le tour script courant, ou s'il est déjà fini / hors budget. Empêche un hook
    /// qui a dépassé son budget d'agir dans le tour SUIVANT (ou de le finir avant que le script ne le réclame).</summary>
    private bool HookEpochStale(uint pid, int expectEpoch)
    {
        if (expectEpoch == 0) return false;
        if (ScriptTurnLive(pid, expectEpoch)) return false;
        TransportLog.Line("[combat] action du hook ignorée : son tour est terminé ou dépassé");
        return true;
    }

    // Une action Turbo a déjà été injectée mais sa fermeture SequenceEnd+self ACK n'a pas pu être corrélée dans la fenêtre
    // bornée. Son résultat est donc INCONNU, pas "non envoyé" : on interdit toute action suivante de ce tour pour éviter un
    // retry ambigu (double cast/double move). Si le tour est encore ouvert, on tente seulement de le terminer proprement.
    private void CloseScriptTurnAfterTurboBarrierFailure(uint pid, int expectEpoch, string action)
    {
        if (ActiveScriptTurn(pid) is { } st && (expectEpoch == 0 || st.Epoch == expectEpoch))
        {
            Interlocked.Exchange(ref st.Expired, 1);
            TransportLog.Line($"[combat] {action} : barrière Turbo inconnue -> tour script invalidé, aucun retry autorisé");
            if (_myTurnOpen.GetValueOrDefault(pid)) TryEndScriptTurn(pid, st.Epoch, "barrière Turbo inconnue");
            return;
        }
        TransportLog.Line($"[combat] {action} : barrière Turbo inconnue -> fin de tour de sécurité, aucun retry autorisé");
        if (_myTurnOpen.GetValueOrDefault(pid)) SendEndTurn(pid);
    }

    /// <summary>Le tour script <paramref name="ep"/> est-il toujours le tour courant, non fini et dans son budget ?</summary>
    public bool ScriptTurnLive(uint pid, int ep)
        => ActiveScriptTurn(pid) is { } st && st.Epoch == ep && st.Ended == 0 && st.Expired == 0;

    /// <summary>Le tour script <paramref name="ep"/> est-il PERDU pour son hook (budget écoulé, tour suivant tendu, fin de
    /// combat) ? Un tour simplement fini par le script (endTurn) n'est PAS perdu : le hook peut finir sa logique.</summary>
    public bool ScriptTurnGone(uint pid, int ep)
        => ActiveScriptTurn(pid) is not { } st || st.Epoch != ep || st.Expired == 1;

    /// <summary>Hook de tour en ERREUR sans avoir rien fait (aucun lancer/déplacement, tour ni fini ni hors budget) alors que
    /// l'auto-combat du PROFIL est actif: le moteur joue ce tour (ex: bloc un bot tiers fightManagement resté dans un script
    /// porté, dont les tables fightCharacter/fightBasic n'existent pas). true = le moteur a joué le tour.</summary>
    public async Task<bool> EngineTakeoverAfterHookErrorAsync(uint pid, int ep, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!ProfileAutoFight(pid)) return false;
        if (ActiveScriptTurn(pid) is not { } st || st.Epoch != ep || st.Acted || st.Ended == 1 || st.Expired == 1 || st.EnginePlayed) return false;
        TransportLog.Line("[combat] hook de tour en erreur sans action -> le moteur joue ce tour");
        return await PlayTurnForScriptAsync(pid, ep, ct).ConfigureAwait(false);
    }

    /// <summary>Fin du run de script <paramref name="token"/> : libère ses hooks/override et, si le moteur doit jouer
    /// (AutoFight), lui rend la main sur le tour/placement EN COURS. AutoFight OFF -> rien (tour manuel dans le client).</summary>
    public void ReleaseScriptCombat(uint pid, object token)
    {
        if (_autoFightOverride.TryGetValue(pid, out var ov) && ReferenceEquals(ov.Owner, token))
            _autoFightOverride.TryRemove(new KeyValuePair<uint, (object, bool)>(pid, ov));
        bool ownedTurn = _scriptTurnHook.TryGetValue(pid, out var th) && ReferenceEquals(th, token)
                         && _scriptTurnHook.TryRemove(new KeyValuePair<uint, object>(pid, th));
        bool ownedPlacement = _scriptPlacementHook.TryGetValue(pid, out var ph) && ReferenceEquals(ph, token)
                              && _scriptPlacementHook.TryRemove(new KeyValuePair<uint, object>(pid, ph));
        try
        {
            if (ownedTurn && IsInFight(pid) && EngineAutoPlays(pid))
            {
                if (_scriptTurnPending.TryGetValue(pid, out int pep) && _scriptTurnPending.TryRemove(new KeyValuePair<uint, int>(pid, pep)))
                {
                    if (pep == _turnEpoch.GetValueOrDefault(pid) && _myTurnOpen.GetValueOrDefault(pid))
                    {
                        TransportLog.Line("[combat] script arrêté -> le moteur reprend le tour en attente");
                        _ = RunFightTurn(pid, pep);
                    }
                }
                else if (ActiveScriptTurn(pid) is { } st && !st.EnginePlayed && st.Ended == 0 && _myTurnOpen.GetValueOrDefault(pid))
                {
                    st.EnginePlayed = true;
                    TransportLog.Line("[combat] script arrêté -> le moteur reprend le tour en cours");
                    _ = RunFightTurn(pid, st.Epoch, scriptDriven: true);
                }
            }
            if (ownedPlacement && _placementOpen.GetValueOrDefault(pid) && !_scriptReadySent.GetValueOrDefault(pid) && !SnowPreFightRunning(pid)
                && _combat.TryGetValue(pid, out var pr) && pr.AutoPreFight && _placementCells.TryGetValue(pid, out var cells))
            {
                TransportLog.Line("[combat] script arrêté -> le moteur reprend le placement");
                _ = RunPreFight(pid, cells);
            }
        }
        catch { /* best-effort : un échec de reprise ne doit pas faire planter l'arrêt du script */ }
    }

    /// <summary>combat:setAutoFight : override EN MÉMOIRE. Seul son propriétaire peut le retirer.</summary>
    public void SetAutoFightOverride(uint pid, object owner, bool? value)
    {
        if (value is null)
        {
            if (_autoFightOverride.TryGetValue(pid, out var current) && ReferenceEquals(current.Owner, owner))
                ((ICollection<KeyValuePair<uint, (object Owner, bool Value)>>)_autoFightOverride)
                    .Remove(new(pid, current));
        }
        else _autoFightOverride[pid] = (owner, value.Value);
    }

    // ===== Lecture de combat exposée au SCRIPT LUA (couche scripting, Lot 2-F). Projections PUBLIQUES des états PRIVÉS
    //       du combat -> le host per-pid les rend au module combat: (pure LECTURE, aucune action, aucun risque moteur). =====
    /// <summary>Combattants de mon combat : cellule, actorId, ennemi ?, moi ?, PV% (100 si inconnu). Pour combat:fighters/enemies/allies.</summary>
    public System.Collections.Generic.IReadOnlyList<(int Cell, long ActorId, bool IsEnemy, bool IsSelf, int Hp)> FightersView(uint pid)
    {
        if (!_fighters.TryGetValue(pid, out var fs) || fs.Count == 0)
            return System.Array.Empty<(int, long, bool, bool, int)>();
        long self = SelfActorId(pid);
        var outp = new System.Collections.Generic.List<(int, long, bool, bool, int)>(fs.Count);
        foreach (var f in fs) outp.Add((f.Cell, f.ActorId, IsEnemy(pid, f.ActorId), f.ActorId == self, FighterHpPercent(pid, f.ActorId) ?? 100));
        return outp;
    }
    /// <summary>Ma cellule en combat, ou -1.</summary>
    public int FightSelfCell(uint pid) => SelfFighterCell(pid);
    /// <summary>N° du tour courant (1-based), 0 hors combat.</summary>
    public int FightTurnNo(uint pid) => _fightTurnNo.TryGetValue(pid, out var n) ? n : 0;
    /// <summary>Est-ce MON tour (fenêtre de jeu ouverte) ?</summary>
    public bool IsMyTurnOpen(uint pid) => _myTurnOpen.GetValueOrDefault(pid);
    /// <summary>Forme d'AoE (shape ASCII / rayon / rayon-min) d'un sort au grade appris par ce pid, ou null. Pour combat:spellZone (Lua).</summary>
    public (int Shape, int Size, int MinSize)? SpellZoneOf(int spellId, uint pid) => _gameNames.SpellZone(spellId, LearnedSpellLevel(pid, spellId));

    // ===== Verbes d'ACTION de combat pour le SCRIPT LUA (Lot 2-F). Le script (auto-combat UI OFF) joue son tour en
    //       appelant ces verbes : ils tournent sur le thread du script, forgent+injectent (thread-safe), self-gate sur
    //       _myTurnOpen. AUCUN changement de RunFightTurn : la rotation intégrée reste le chemin de l'auto-combat. =====
    /// <summary>Lance un sort sur une cellule (combat:castOnCell). false si hors tour / refusé / droit "fight" absent.
    /// Dans un tour SCRIPT : refus local (aucune injection) seulement sur les PLAFONDS (1 lancers/tour, 2 relance du profil,
    /// 4 lancers/cible) ; géométrie et PA restent jugés par le serveur (comme avant). Lancer injecté -> suivi (relance,
    /// PA restants, compteurs) + ennemi confirmé si la cible est déjà classée ennemie.</summary>
    public bool CastSpell(uint pid, int cell, int spellId, int expectEpoch = 0)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) return false;   // combat = Perm.Fight au POINT D'ACTION (scripting combat inclus)
        if (HookEpochStale(pid, expectEpoch)) return false;
        bool inFight = IsInFight(pid);
        bool turboMitm = inFight && !IsSocketPid(pid)
            && _combat.TryGetValue(pid, out var liveProfile) && liveProfile.Speed == CombatSpeed.Turbo;
        var st = inFight ? ActiveScriptTurn(pid) : null;
        long actor = ActorAtCell(pid, cell);
        if (st is not null)
        {
            int cap = ScriptCapVerdict(pid, spellId, actor, st);
            if (cap != 0) { TransportLog.Line($"[combat] cast {spellId} refusé : {CapReasonText(cap)}"); return false; }
        }
        int apObservation = 0;
        if (st is not null) lock (st) apObservation = st.ApObservation;
        if (!SendCast(pid, cell, spellId, out var turboRequestAt)) return false;
        if (!inFight) return true;
        RecordCast(pid, spellId, FightTurnNo(pid));
        if (st is not null)
        {
            int cost = SpellCombatLive(pid, spellId, LearnedSpellLevel(pid, spellId))?.Ap ?? 0;
            st.Acted = true;
            lock (st)
            {
                if (st.ApObservation == apObservation)
                    st.ApLeft = System.Math.Max(0, st.ApLeft - cost);
                st.CastsPerSpell[spellId] = st.CastsPerSpell.GetValueOrDefault(spellId) + 1;
                if (actor != 0) { var k = (spellId, actor); st.CastsPerTarget[k] = st.CastsPerTarget.GetValueOrDefault(k) + 1; }
                st.TotalCasts++;
            }
        }
        if (actor != 0 && IsEnemy(pid, actor)) MarkEnemyConfirmed(pid, actor);
        // Le callback Lua est déjà synchrone/bloquant. En Turbo MITM on remplace donc le délai fixe implicite par la même
        // barrière autoritaire que le moteur intégré (fin de séquence + accusé, du bot en Turbo-ack ou du client visible).
        if (turboMitm && turboRequestAt > 0
            && !WaitTurboActionBarrierWithGrace(pid, turboRequestAt).GetAwaiter().GetResult())
            CloseScriptTurnAfterTurboBarrierFailure(pid, expectEpoch, $"cast {spellId}");
        return true;
    }

    private static string CapReasonText(int r) => r switch
    {
        1 => "nombre de lancers par tour atteint",
        2 => "temps de relance (profil) pas écoulé",
        4 => "nombre de lancers sur cette cible atteint",
        _ => r.ToString(),
    };

    // Combattant présent sur une cellule (0 = aucun).
    private long ActorAtCell(uint pid, int cell)
    {
        if (!_fighters.TryGetValue(pid, out var fs)) return 0;
        foreach (var f in fs) if (f.Cell == cell) return f.ActorId;
        return 0;
    }

    // Temps de relance combiné (profil + jeu).
    private bool ProfileCooldownActive(uint pid, int spellId, int turnNo)
    {
        if (!_combat.TryGetValue(pid, out var pr)) return false;
        var cfg = pr.Spells.FirstOrDefault(sp => sp.SpellId == spellId && sp.Cooldown > 0);
        int profileCd = cfg?.Cooldown ?? 0;
        int grade = LearnedSpellLevel(pid, spellId);
        var desc = _gameNames.GetSpellDescriptor(spellId, grade);
        int minInterval = desc?.MinCastInterval ?? 0;
        int initCd = desc?.InitialCooldown ?? 0;
        return ActiveLedger(pid).IsRechargeActive(spellId, turnNo, profileCd, minInterval, initCd);
    }

    // Plafonds seuls (1/2/4) avec les MÊMES données que ScriptCanCast. 0 = OK.
    private int ScriptCapVerdict(uint pid, int spellId, long actor, ScriptTurnState st)
    {
        int grade = LearnedSpellLevel(pid, spellId);
        if (ProfileCooldownActive(pid, spellId, st.TurnNo)) return 2;
        var caps = _gameNames.GetSpellDamage(spellId, grade);
        int perTurn = caps?.MaxPerTurn ?? 0, perTarget = caps?.MaxPerTarget ?? 0;
        lock (st)
        {
            if (perTurn > 0 && st.CastsPerSpell.GetValueOrDefault(spellId) >= perTurn) return 1;
            if (perTarget > 0 && actor != 0 && st.CastsPerTarget.GetValueOrDefault((spellId, actor)) >= perTarget) return 4;
        }
        return 0;
    }

    /// <summary>Termine mon tour de combat (combat:endTurn). Droit "fight" requis. REFUSÉ hors de ma fenêtre de tour (un
    /// jxy hors tour relance l'écho serveur = boucle de tours fantômes). Idempotent dans un tour script.</summary>
    public bool EndTurn(uint pid, int expectEpoch = 0)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) return false;
        if (HookEpochStale(pid, expectEpoch)) return false;
        if (!_myTurnOpen.GetValueOrDefault(pid)) { TransportLog.Line("[combat] fin de tour ignorée : hors de ma fenêtre"); return false; }
        if (ActiveScriptTurn(pid) is { } st) return TryEndScriptTurn(pid, st.Epoch, "script");
        return SendEndTurn(pid);
    }

    // Bloqueur de LdV de la carte de relief (_lastMap) : case absente ou `Los == false` -> bloque (comme RunFightTurn).
    private Func<int, bool> LosBlocker(uint pid)
    {
        var (_, cells) = LoadFightMap(pid);
        var los = new bool[MapPathfinder.CellCount];
        if (cells is not null) foreach (var c in cells) if ((uint)c.CellId < los.Length) los[c.CellId] = c.Los;
        return c => (uint)c < los.Length && !los[c];
    }

    /// <summary>combat:canCast — lançable ? + raison. fromCell &lt; 0 = ma case de combat. Marche hors de
    /// mon tour (PA live). Géométrie IDENTIQUE à CanCast (LdV seulement si le sort l'exige et d &gt; 1).</summary>
    public (bool Ok, int Reason) ScriptCanCast(uint pid, int spellId, int cell, int fromCell)
    {
        int grade = LearnedSpellLevel(pid, spellId);
        var learned = LearnedSpells(pid);
        if (learned.Count > 0 && !learned.Contains(spellId)) return (false, (int)SpellCastVerdict.UnknownSpell);
        if (SpellCombatLive(pid, spellId, grade, SelfActorId(pid)) is not { } s) return (false, (int)SpellCastVerdict.Unknown);
        int myCell = SelfFighterCell(pid);
        int from = fromCell >= 0 ? fromCell : myCell;
        if (from < 0 || (uint)cell >= MapPathfinder.CellCount) return (false, (int)SpellCastVerdict.Unknown);
        int maxR = System.Math.Max(s.MinRange, s.Range + (s.Rangeable ? SelfRange(pid) : 0));
        var caps = _gameNames.GetSpellDamage(spellId, grade);
        var fc = _gameNames.SpellCastConstraints(spellId, grade);
        var fighters = _fighters.TryGetValue(pid, out var fl) ? fl : (IReadOnlyList<(int Cell, long ActorId)>)System.Array.Empty<(int Cell, long ActorId)>();
        long actor = 0; bool targetOcc = false;
        foreach (var f in fighters) if (f.Cell == cell) { actor = f.ActorId; targetOcc = true; break; }
        var st = ActiveScriptTurn(pid);
        int castsTurn = 0, castsTarget = 0, turnNo = st?.TurnNo ?? FightTurnNo(pid);
        if (st is not null) lock (st) { castsTurn = st.CastsPerSpell.GetValueOrDefault(spellId); castsTarget = actor != 0 ? st.CastsPerTarget.GetValueOrDefault((spellId, actor)) : 0; }
        var occ = new HashSet<int>();
        foreach (var f in fighters) if (f.Cell != from && f.Cell != cell && f.Cell != myCell) occ.Add(f.Cell);
        var input = new SpellCastInput(from, cell, s.Ap, s.MinRange, maxR, s.CastInLine, s.CastInDiagonal, s.CastTestLos,
            ScriptPointsLeft(pid).Ap, ProfileCooldownActive(pid, spellId, turnNo),
            castsTurn, caps?.MaxPerTurn ?? 0, castsTarget, caps?.MaxPerTarget ?? 0,
            fc?.NeedFreeCell ?? false, fc?.NeedTakenCell ?? false, targetOcc);
        var v = SpellCastCheck.Evaluate(input, LosBlocker(pid), occ.Contains);
        return (v == SpellCastVerdict.None, (int)v);
    }

    /// <summary>combat:hasLos — ligne de vue a-&gt;b (relief + combattants autres que a, b et moi ; hors combat : relief seul).</summary>
    public bool HasLos(uint pid, int a, int b)
    {
        var occ = new HashSet<int>();
        if (IsInFight(pid) && _fighters.TryGetValue(pid, out var fs))
        {
            int me = SelfFighterCell(pid);
            foreach (var f in fs) if (f.Cell != a && f.Cell != b && f.Cell != me) occ.Add(f.Cell);
        }
        return MapPathfinder.HasLineOfSight(a, b, LosBlocker(pid), occ.Contains);
    }

    /// <summary>combat:isFreeCell en combat : marchable EN COMBAT, non bloquée, sans combattant (ni moi).</summary>
    public bool IsFreeFightCell(uint pid, int c)
        => BuildFightGeom(pid) is { } g && c != g.SelfCell && g.Walk(c);

    /// <summary>combat:fightPath — chemin ORTHOGONAL from-&gt;to (from exclu, to inclus ; to peut être occupée). PM ignorés.</summary>
    public IReadOnlyList<int> FightPath(uint pid, int from, int to)
    {
        if (BuildFightGeom(pid) is not { } g || (uint)from >= MapPathfinder.CellCount || (uint)to >= MapPathfinder.CellCount || from == to)
            return System.Array.Empty<int>();
        var key = MapPathfinder.KeyMovements(from, to, g.Walk, allowDiag: false, g.Step);
        if (key.Count == 0) return System.Array.Empty<int>();
        return MapPathfinder.ExpandPath(key.Select(k => k & 0xFFF).ToList()).Skip(1).ToList();
    }

    /// <summary>PA/PM restants : tracker du tour script s'il est actif, sinon budget de début de tour.</summary>
    public (int Ap, int Mp) ScriptPointsLeft(uint pid)
    {
        if (ActiveScriptTurn(pid) is { } st) lock (st) return (st.ApLeft, st.MpLeft);
        return TurnStartBudget(pid);
    }

    private Func<int, int, bool> CombatPolicyStep(uint pid, Func<int, int, bool>? terrain)
    {
        var enemies = _fighters.TryGetValue(pid, out var fighters)
            ? fighters.Where(f => IsEnemy(pid, f.ActorId)).Select(f => f.Cell).ToArray() : Array.Empty<int>();
        return CombatMovementDecision.RestrictSteps(
            _combat.TryGetValue(pid, out var profile) ? profile.MovementPolicy : CombatMovementPolicy.Standard,
            cell => enemies.Length == 0 ? 0 : enemies.Min(enemy => MapPathfinder.CellDistance(cell, enemy)), terrain);
    }

    /// <summary>combat:moveToCell — déplacement de combat TRONQUÉ aux PM restants (repli : case atteignable la plus proche
    /// par le chemin), bloquant : barrière de séquence, injection, attente de la confirmation serveur. (moved, case finale).</summary>
    public async Task<(bool Moved, int Cell)> ScriptCombatMoveAsync(uint pid, int target, CancellationToken ct, int expectEpoch = 0)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) return (false, -1);   // déplacement EN COMBAT = Perm.Fight
        if (HookEpochStale(pid, expectEpoch)) return (false, SelfFighterCell(pid));
        if (!IsInFight(pid) || !_myTurnOpen.GetValueOrDefault(pid))
        {
            TransportLog.Line($"[combat] déplacement vers {target} ANNULÉ : hors de ma fenêtre de tour (aucune injection)");
            return (false, SelfFighterCell(pid));
        }
        if (BuildFightGeom(pid) is not { } g || g.SelfCell < 0) return (false, -1);
        if (target == g.SelfCell) return (true, g.SelfCell);   // déjà sur la case
        int moveJournalRound = System.Math.Max(1, _teamIntentRound.GetValueOrDefault(pid,
            ActiveScriptTurn(pid)?.TurnNo ?? _fightTurnNo.GetValueOrDefault(pid)));
        long journalFightStart = _fightStartAt.GetValueOrDefault(pid);
        int mp = ScriptPointsLeft(pid).Mp;
        int dest = CombatMovePlanner.PickDestination(g.SelfCell, target, mp, g.Walk, CombatPolicyStep(pid, g.Step), out _);
        if (dest < 0)
        {
            TransportLog.Line($"[combat] déplacement vers {target} : aucune case atteignable ne rapproche (PM{mp}) -> pas de mouvement");
            return (false, g.SelfCell);
        }
        var speed = _combat.TryGetValue(pid, out var pr) ? pr.Speed : CombatSpeed.Fast;
        await WaitMoveClear(pid,
            speed switch { CombatSpeed.Turbo or CombatSpeed.Instant => 140, CombatSpeed.Fast => 250, _ => 350 },
            speed switch { CombatSpeed.Turbo or CombatSpeed.Instant => 700, CombatSpeed.Fast => 1100, _ => 1500 }).ConfigureAwait(false);
        if (ct.IsCancellationRequested || !_myTurnOpen.GetValueOrDefault(pid) || HookEpochStale(pid, expectEpoch)) return (false, SelfFighterCell(pid));
        // RELECTURE après la barrière (poussée / ennemis qui ont bougé) : géométrie et case fraîches.
        if (BuildFightGeom(pid) is not { } g2 || g2.SelfCell < 0) return (false, -1);
        int src = g2.SelfCell;
        mp = ScriptPointsLeft(pid).Mp;
        var policyStep = CombatPolicyStep(pid, g2.Step);
        dest = CombatMovePlanner.PickDestination(src, target, mp, g2.Walk, policyStep, out _);
        if (dest < 0) return (false, src);
        if (dest != src && !g2.Walk(dest))
        {
            // Case bloquée seulement dans un TOUR SCRIPT (la réclamation suivante la libère) : sinon elle resterait murée
            // tout le combat pour un script sans hook (personne ne vide _blockedCells).
            if (ActiveScriptTurn(pid) is not null) _blockedCells.GetOrAdd(pid, static _ => new ConcurrentDictionary<int, byte>())[dest] = 0;
            TransportLog.Line($"[combat] déplacement {src}->{dest} annulé : case occupée (position fraîche relue après barrière)");
            return (false, src);
        }
        if (dest == src) return (true, src);
        var enemies = _fighters.TryGetValue(pid, out var fighters)
            ? fighters.Where(f => IsEnemy(pid, f.ActorId)).Select(f => f.Cell).ToList() : new List<int>();
        if (!CombatMovementDecision.CanMove(
                _combat.TryGetValue(pid, out var currentProfile) ? currentProfile.MovementPolicy : CombatMovementPolicy.Standard,
                enemies.Count == 0 ? 0 : enemies.Min(c => MapPathfinder.CellDistance(src, c)),
                enemies.Count == 0 ? 0 : enemies.Min(c => MapPathfinder.CellDistance(dest, c))))
            return (false, src);
        var reach = MapPathfinder.OrthoReachable(src, mp, g2.Walk, policyStep);
        int prevArm = _seqArmIsq.GetValueOrDefault(pid);
        ArmSeqEnd(pid);
        _moveGuardUntil[pid] = 0;
        long injAt = System.Environment.TickCount64;
        int refused0 = _moveRefused.GetValueOrDefault(pid);
        var scriptTurn = ActiveScriptTurn(pid);
        int mpObservation = 0;
        if (scriptTurn is not null) lock (scriptTurn) mpObservation = scriptTurn.MpObservation;
        if (!MoveTo(pid, g2.ForgeMap, src, dest, g2.Walk, allowDiag: false, policyStep)) { _seqArmIsq[pid] = prevArm; return (false, src); }
        long turboRequestAt = NoteTurboRequestTx(pid, $"script-move:{src}->{dest}");
        if (ActiveScriptTurn(pid) is { } stAct) stAct.Acted = true;
        _moveGuardUntil[pid] = 0;
        while (SelfFighterCell(pid) == src && System.Environment.TickCount64 - injAt < MoveConfirmWindowMs(pid))
        {
            if (_moveRefused.GetValueOrDefault(pid) > refused0) break;
            await Task.Delay(PollMs(pid, 15)).ConfigureAwait(false);
        }
        int moveDelay = speed switch { CombatSpeed.Turbo or CombatSpeed.Instant => 0, CombatSpeed.Fast => Random.Shared.Next(600, 1100), _ => Random.Shared.Next(1200, 3000) };
        int leftover = moveDelay - (int)(System.Environment.TickCount64 - injAt);
        if (leftover > 0) await Task.Delay(leftover).ConfigureAwait(false);
        int after = SelfFighterCell(pid);
        string moveOutcome = _moveRefused.GetValueOrDefault(pid) > refused0 ? "refused" : after >= 0 && after != src ? "confirmed" : "timeout";
        if (_fightStartAt.GetValueOrDefault(pid) == journalFightStart)
            TransportLog.Line(pid, $"[combat] move_result owner=self round={moveJournalRound} from={src} target={dest} final={after} outcome={moveOutcome}");
        if (after == src || after < 0)
        {
            if (ActiveScriptTurn(pid) is not null) _blockedCells.GetOrAdd(pid, static _ => new ConcurrentDictionary<int, byte>())[dest] = 0;
            _seqArmIsq[pid] = prevArm;   // move droppé : sa séquence n'existera jamais
            TransportLog.Line($"[combat] déplacement {src}->{dest} refusé/ignoré par le serveur -> case bloquée ce tour");
            return (false, src);
        }
        if (speed == CombatSpeed.Turbo && IsSocketPid(pid) && turboRequestAt > 0)
            await WaitTurboActionBarrier(pid, turboRequestAt, 1200).ConfigureAwait(false);
        else if (speed == CombatSpeed.Turbo && !IsSocketPid(pid) && turboRequestAt > 0
            && !await WaitTurboActionBarrierWithGrace(pid, turboRequestAt).ConfigureAwait(false))
            CloseScriptTurnAfterTurboBarrierFailure(pid, expectEpoch, $"déplacement {src}->{after}");
        // A portal exit is not a walked distance. Confirmed remaining MP wins
        // over local spending; otherwise charge the requested walking endpoint.
        int used = reach.TryGetValue(dest, out var u) ? u : MapPathfinder.CellDistance(src, dest);
        if (scriptTurn is not null && ReferenceEquals(scriptTurn, ActiveScriptTurn(pid)))
            lock (scriptTurn)
                if (scriptTurn.MpObservation == mpObservation)
                    scriptTurn.MpLeft = System.Math.Max(0, scriptTurn.MpLeft - used);
        // The sequence barrier can include a portal teleport after the walk.
        return (true, SelfFighterCell(pid));
    }

    // ---- Placement piloté par script (hook onPlacement / combat:place / ready) ----
    public bool TryClaimScriptPlacement(uint pid)
        => ScriptOwnsPlacement(pid) && _placementOpen.GetValueOrDefault(pid) && _scriptPlacementPending.TryRemove(pid, out _);
    /// <summary>Mes cases de placement (vide hors phase de placement).</summary>
    public IReadOnlyList<int> PlacementCells(uint pid)
        => _placementOpen.GetValueOrDefault(pid) && _placementCells.TryGetValue(pid, out var c) ? c : System.Array.Empty<int>();
    public bool IsPlacementOpen(uint pid) => IsInFight(pid) && _placementOpen.GetValueOrDefault(pid);

    /// <summary>combat:place(cell) — phase de placement seulement ; la case doit être une de mes cases proposées.</summary>
    public bool ScriptPlace(uint pid, int cell)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight) || !IsPlacementOpen(pid)) return false;
        if (_placementCells.TryGetValue(pid, out var cells) && cells.Count > 0 && !cells.Contains(cell))
        { TransportLog.Line($"[combat] placement script -> case {cell} refusé : pas une case de placement proposée"); return false; }
        long me = SelfActorId(pid);
        if (me == 0) return false;
        if (SelfFighterCell(pid) == cell) return true;
        return SendPlacement(pid, me, cell);
    }

    private bool SendUnready(uint pid)
    {
        var relay = RelayForInject(); if (relay is null) return false;
        byte[] pkt = MoveForge.BuildReadyFramed(false);   // FightReadyRequest vide (is_ready=0 omis)
        if (pkt.Length == 0) { TransportLog.Line($"[combat] « plus prêt » : forge refusée ({MoveForge.LastForgeError})"); return false; }
        bool ok = relay.Inject(pid, pkt); ShowInjected(pid, pkt);
        TransportLog.Line($"[combat] pid {pid} : PLUS PRÊT inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>combat:ready([bool]) — phase de placement seulement.</summary>
    public bool ScriptReady(uint pid, bool ready)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight) || !IsPlacementOpen(pid)) return false;
        bool ok = ready ? SendReady(pid) : SendUnready(pid);
        if (ok) { _scriptReadySent[pid] = ready; _scriptReadyChosen[pid] = true; }
        return ok;
    }

    /// <summary>Après onPlacement : prêt automatique (chef : attente stricte des followers, comme RunPreFight) sauf si déjà fait.</summary>
    public async Task ScriptFinishPlacementAsync(uint pid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // combat:ready(true|false) déjà appelé par le script = choix explicite (un ready(false) n'est PAS écrasé).
        if (_scriptReadySent.GetValueOrDefault(pid) || _scriptReadyChosen.GetValueOrDefault(pid) || !IsPlacementOpen(pid)) return;
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) return;
        if (!await WaitTeamJoinBeforeChallengeAsync(pid, ct, untilJoined: true).ConfigureAwait(false)) return;
        ct.ThrowIfCancellationRequested();
        if (IsPlacementOpen(pid) && !_scriptReadySent.GetValueOrDefault(pid) && !_scriptReadyChosen.GetValueOrDefault(pid) && SendReady(pid)) _scriptReadySent[pid] = true;
    }

    /// <summary>Timer (thread pool) : budget de onPlacement écoulé -> prêt d'office (is_ready=1 idempotent).</summary>
    public void ExpireScriptPlacement(uint pid)
    {
        if (!IsPlacementOpen(pid) || _scriptReadySent.GetValueOrDefault(pid) || _scriptReadyChosen.GetValueOrDefault(pid)
            || !LicenseService.HasPermission(LicenseService.Perm.Fight)) return;
        if (SendReady(pid))
        {
            _scriptReadySent[pid] = true;
            TransportLog.Line("[combat] onPlacement trop long -> prêt envoyé d'office");
        }
    }

    /// <summary>combat:kick(id) — expulse un combattant (le serveur décide ; en pratique pendant le placement).</summary>
    public bool KickFighter(uint pid, long id)
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight) || !IsInFight(pid)) return false;
        if (id == 0 || id == SelfActorId(pid)) return false;
        return SendKickFromFight(pid, id);
    }

    /// <summary>Combattant détaillé pour le script (combat:fighters) : PV/PA/PM ABSOLUS (0 si inconnus), équipe (-1 si inconnue).
    /// <paramref name="Breed"/> est la classe Dofus observée du combattant (0 si inconnue, cas d'un monstre) : elle sert à
    /// afficher l'icône de classe sur la pastille de la carte de combat, au lieu d'un simple rond de couleur.</summary>
    public readonly record struct ScriptFighter(long ActorId, int Cell, bool IsSelf, bool IsEnemy, int Team, int HpPercent,
        int Life, int MaxLife, int Pa, int Pm, bool IsSummon, bool IsMySummon, bool Alive, int Breed = 0);

    public IReadOnlyList<ScriptFighter> FightersDetail(uint pid)
    {
        if (!_fighters.TryGetValue(pid, out var fs) || fs.Count == 0) return System.Array.Empty<ScriptFighter>();
        long self = SelfActorId(pid);
        _fighterStats.TryGetValue(pid, out var byId);
        _summonIds.TryGetValue(pid, out var summons);
        _summonOwners.TryGetValue(pid, out var owners);
        _fighterBreeds.TryGetValue(pid, out var breedsById);   // classe observée (déjà décodée des paquets) -> icône sur la carte
        var outp = new List<ScriptFighter>(fs.Count);
        foreach (var f in fs)
        {
            bool isSelf = f.ActorId == self;
            FighterStats? st = isSelf ? SelfStats(pid) : (byId is not null && byId.TryGetValue(f.ActorId, out var s0) ? s0 : null);
            int life = st?.Life ?? 0, maxLife = st?.MaxLife ?? 0;
            bool isMine = owners is not null && owners.TryGetValue(f.ActorId, out long owner) && owner == self;
            outp.Add(new ScriptFighter(f.ActorId, f.Cell, isSelf, IsEnemy(pid, f.ActorId), TeamOf(pid, f.ActorId) ?? -1,
                FighterHpPercent(pid, f.ActorId) ?? 100, life, maxLife, st?.Pa ?? 0, st?.Pm ?? 0,
                isMine || (summons is not null && summons.ContainsKey(f.ActorId)), isMine, !(maxLife > 0 && life <= 0),
                breedsById is not null && breedsById.TryGetValue(f.ActorId, out int breed) ? breed : 0));
        }
        return outp;
    }
}
