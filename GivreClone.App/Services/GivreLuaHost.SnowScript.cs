using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ==================================================================================================================
// Compatibilité tiers — moteur de script (ReadScript.vb / FightManager.vb): ce que un bot tiers fait AUTOUR des verbes.
//  * plusieurs étapes pour la même carte = fusionnées clé par clé, la DERNIÈRE qui définit une clé gagne (MergeSteps) ;
//  * callback phenix() : trajet de fantôme vers le Phénix, clé d'étape `phenix = <cellule de la statue>` ;
//  * callbacks prefightManagement(cellules de mon équipe, cellules adverses) et challengeManagement(nbDéfis, estSuiveur)
//    au placement : de simples NOTIFICATIONS, le moteur fait ensuite son pré-combat (LaunchService.SnowPlacement.cs) ;
// * globals PLANNING (heures de déconnexion), GIVRE_TIMEOUT, FIGHT_KICK (expulsion des joueurs
//    étrangers au placement, délai ms). IGNORE_REQUEST_GUILD est porté par la politique du script (LaunchService).
// Tout tourne sur le thread du script (boucle RunLoopCore / attente de fight() / hook de placement / pompe des hooks).
// ==================================================================================================================
public sealed partial class GivreLuaHost
{
    private bool _planningDisconnectAsked;
    private int _snowTimeoutSec;
    private readonly Dictionary<long, long> _fightKickSeen = new();   // acteur -> 1re vue au placement (TickCount64), -1 = déjà expulsé
    // phenix() : étape jouée sans effet (carte inchangée, toujours fantôme) -> temporisation, puis le moteur reprend.
    private long _phenixIdleMap;
    private int _phenixIdle;
    private const int PhenixIdleMax = 3;
    // challengeManagement : fonction du run (null = absente) + appel en attente pour le placement du combat _snowCmFight.
    private DynValue? _snowCmFn;
    private bool _snowCmPending;
    private long _snowCmFight;
    private int _scriptPlaceOk;   // ++ à chaque combat:place / fightAction:chooseCell réussi (thread du script)

    /// <summary>Seuil anti-blocage demandé par le script, ou null.</summary>
    public int? ScriptWatchdogSeconds => Volatile.Read(ref _snowTimeoutSec) is int s and > 0 ? s : null;

    // Réglages un bot tiers lus au démarrage du run (après le top-level).
    private void ReadSnowRunGlobals(Script script)
    {
        Volatile.Write(ref _snowTimeoutSec, Math.Max(0, ReadInt(script, "GIVRE_TIMEOUT", 0)));
        _planningDisconnectAsked = false;
        _fightKickSeen.Clear();
        _phenixIdleMap = 0; _phenixIdle = 0;
    }

    // ------------------------------------------------------------------ étapes de trajet
    /// <summary>Étapes de la carte courante : id exact d'abord (jamais mélangé aux étapes en coordonnée : un intérieur partage
    /// la coordonnée de l'extérieur), sinon coordonnée. Plusieurs étapes dans la passe retenue -> fusion clé par clé, la
    /// DERNIÈRE qui définit une clé gagne.</summary>
    private Table? MatchStepHere(Table route, long mapId, int? px, int? py)
    {
        var matches = new List<Table>();
        foreach (var v in route.Values)
        {
            if (v.Type != DataType.Table) continue;
            if (long.TryParse((v.Table.Get("map").CastToString() ?? "").Trim(), out long mid) && mid == mapId) matches.Add(v.Table);
        }
        if (matches.Count == 0)
            foreach (var v in route.Values)
            {
                if (v.Type != DataType.Table) continue;
                if (TryParseCoord(v.Table.Get("map").CastToString() ?? "", out int mx, out int my) && px == mx && py == my) matches.Add(v.Table);
            }
        return matches.Count switch { 0 => null, 1 => matches[0], _ => MergeSteps(matches) };
    }

    // Fusion dans l'ordre du tableau : chaque étape ÉCRASE les clés qu'elle définit (une clé absente d'une étape ne l'efface
    // pas). Un bot tiers résout chaque clé en parcourant toutes les étapes de la carte et garde la dernière valeur trouvée: deux
    // étapes `path` différentes -> la 2e ; `door` d'une étape + `path` d'une autre -> les deux (RunRoute joue door en priorité).
    private Table MergeSteps(List<Table> steps)
    {
        var t = new Table(steps[0].OwnerScript ?? _script!);
        foreach (var s in steps)
            foreach (var p in s.Pairs)
                t.Set(p.Key, p.Value);
        return t;
    }

    // ------------------------------------------------------------------ phenix()
    /// <summary>Fantôme + script qui définit phenix() : joue le trajet renvoyé (clé `phenix = cellule` ou "a|b" = utiliser la
    /// statue, comme `door` ; sinon path / door / custom comme un trajet normal). false = pas d'étape pour cette carte, ou
    /// étape restée sans effet <see cref="PhenixIdleMax"/> fois sur cette carte -> le moteur reprend (HandleDeath : voyage
    /// vers le Phénix indiqué par le serveur) jusqu'au prochain changement de carte.</summary>
    private bool RunScriptPhenix(Script script, DynValue fn, CancellationToken ct)
    {
        long mapId = _launch.CurrentMap(_pid) ?? 0;
        if (mapId == 0) return false;
        if (mapId != _phenixIdleMap) { _phenixIdleMap = mapId; _phenixIdle = 0; }
        if (_phenixIdle >= PhenixIdleMax) return false;   // étape sans effet ici : le moteur garde la main sur cette carte
        DynValue r;
        try { r = CallLua(script, fn, ct); }
        catch (Exception ex) when (!(ct.IsCancellationRequested || Root(ex) is ScriptStopException))
        {
            ScriptErr("mort", ex, d => Loc.Fmt("L.Lua.SnowPhenixError", d), LuaLogLevel.Warning);
            return false;
        }
        if (r.Type != DataType.Table) return false;
        WaitSelfReady(ct);
        mapId = _launch.CurrentMap(_pid) ?? 0;   // relue : phenix() peut elle-même appeler des verbes
        if (mapId == 0) return false;
        if (mapId != _phenixIdleMap) { _phenixIdleMap = mapId; _phenixIdle = 0; }
        var st = _world.Get(_pid);
        var step = MatchStepHere(r.Table, mapId, st.MapX, st.MapY);
        if (step is null) return false;
        var ph = step.Get("phenix");
        int cell = ph.Type is DataType.Number or DataType.String ? ChooseDoorCell(ph.CastToString() ?? "") : -1;
        if (cell >= 0)
        {
            Info("mort", Loc.T("L.Lua.ReviveUsingPhoenix"));
            UseInteractive(cell, ct, verbose: true);
            InterruptibleDelay(2500, ct);   // attend le retour à la vie
        }
        else RunRoute(r.Table, ct);
        // Étape SANS effet (pas de statue à cette cellule, étape terminale, porte absente, custom qui ne bouge pas…) : RunRoute
        // rend la main tout de suite et la boucle (qui saute sa temporisation après phenix) rappelait phenix() À CHAUD. On
        // temporise (pompe des hooks) ; après PhenixIdleMax essais sur la même carte, le moteur reprend (HandleDeath).
        if ((_launch.CurrentMap(_pid) ?? 0) == mapId && _launch.LifeStatusOf(_pid) == 2)
        {
            if (++_phenixIdle >= PhenixIdleMax) Warn("mort", Loc.Fmt("L.Lua.SnowPhenixIdle", mapId));
            else InterruptibleDelay(1500, ct, pump: true);
        }
        else _phenixIdle = 0;
        return true;
    }

    // ------------------------------------------------------------------ placement : prefightManagement / challengeManagement
    /// <summary>Hook de placement construit à partir des callbacks un bot tiers (si onPlacement est absent). Ils ne PRENNENT PAS le
    /// placement : le hook sert seulement à appeler prefightManagement au début du placement sur le thread du script, puis le
    /// moteur fait son pré-combat (placement selon le style SAUF si le script s'est placé lui-même, défi, prêt).
    /// prefightManagement(mesCellules, cellulesAdverses) reçoit les tables tiers { [cellule] = occupant | -1 }
    /// (dict_ChallengersPositions / cellidfighters) ; `return false` = même contrat qu'onPlacement (ni placement ni prêt du
    /// moteur). challengeManagement(n, estSuiveur) est appelé ensuite par la pompe des hooks, dès que les défis sont proposés
    /// (SnowChallengeTick) ; sa présence met le choix des défis en mode « script » (combat:selectChallenge).</summary>
    private DynValue? BuildSnowPlacementHook(Script script, CancellationToken ct)
    {
        _snowCmFn = null; _snowCmPending = false;
        var pf = script.Globals.Get("prefightManagement");
        var cm = script.Globals.Get("challengeManagement");
        bool hasPf = pf.Type == DataType.Function, hasCm = cm.Type == DataType.Function;
        if (!hasPf && !hasCm) return null;
        if (hasCm)
        {
            _snowCmFn = cm;
            if (LicenseService.HasPermission(LicenseService.Perm.Fight))
                _launch.ScriptSetCombat(_pid, o => { o.AutoChallenge = false; o.ChallengeByScript = true; });
        }
        return DynValue.NewCallback((ctx, a) =>
        {
            int placed0 = _scriptPlaceOk;
            bool keep = false;
            if (hasPf)
            {
                // Comme RunPreFight : au début du placement les combattants sont souvent encore inconnus (ils arrivent PENDANT
                // la phase de placement) -> attente bornée (~1,6 s) d'au moins un ennemi, sinon les tables n'auraient aucun occupant.
                WaitUntil(() => _launch.FightersDetail(_pid).Any(f => f.IsEnemy), 1600, ct);
                var fighters = _launch.FightersDetail(_pid);
                // Cases de placement ADVERSES : relues du dernier FightPlacementPossiblePositionsEvent de CE bot (celui qui a
                // ouvert ce placement ; le moteur ne garde que les miennes).
                IReadOnlyList<int> foe = Array.Empty<int>();
                if (_launch.LastReceived(_pid, "FightPlacementPossiblePositionsEvent") is { } kba
                    && FightPrepReader.ReadPlacementCells(kba.Payload) is { } pc) foe = pc.Defenders;
                // Erreur du script: journalisée, le moteur place quand même (un bot tiers: « Erreur dans prefightManagement » puis
                // placement auto). Un arrêt demandé remonte (le hook est déroulé).
                try
                {
                    var ret = CallLua(script, pf, ct, SnowPlacementTable(script, _launch.PlacementCells(_pid), fighters, mine: true),
                                              SnowPlacementTable(script, foe, fighters, mine: false));
                    keep = ret.Type == DataType.Boolean && !ret.Boolean;
                }
                catch (Exception ex) when (!(ct.IsCancellationRequested || Root(ex) is ScriptStopException))
                { ScriptErr("combat", ex, d => Loc.Fmt("L.Lua.HookError", "prefightManagement", d)); }
            }
            bool placedByScript = _scriptPlaceOk != placed0;   // combat:place / fightAction:chooseCell réussi pendant l'appel
            // Pré-combat du MOTEUR (sauf `return false`) : il envoie lui-même « prêt » après le défi -> le hook renvoie
            // toujours false (pas de prêt immédiat de ScriptFinishPlacementAsync, qui n'attend pas la gate du défi). Pré-combat
            // auto coupé dans le profil : comme sans hook, ni placement ni prêt du moteur.
            bool engine = !keep && _launch.SnowStartEnginePreFight(_pid, place: !placedByScript);
            if (hasCm)
            {
                _snowCmPending = true;
                _snowCmFight = _launch.CurrentFightId(_pid);
                // Sans pré-combat du moteur, personne ne demande les défis (kty) : on les demande ici, non bloquant (chef :
                // attente bornée des followers d'abord). Un suiveur ne demande rien (le chef choisit).
                if (!engine && !SnowIsFollower()) _ = _launch.RequestChallengeProposalsAsync(_pid, ct);
            }
            return DynValue.False;
        });
    }

    private bool SnowIsFollower() => _launch.TeamViewOf(_pid) is not null && !_launch.IsTeamLeader(_pid);

    // Table un bot tiers { [cellule] = occupant | -1 } (FightManager.vb / PacketAnalyser.vb): cases de placement de l'équipe
    // pré-remplies à -1, puis la case de chaque combattant vivant de cette équipe (mine = mon équipe, sinon l'adverse) = son
    // id. Un id de combat négatif (monstre, invocation: -1, -2…) est donné en valeur ABSOLUE: Un bot tiers y mettait l'id de
    // monstre générique (toujours > 0) et -1 est réservé aux cases LIBRES (sinon le monstre -1 passait pour une case vide).
    private static DynValue SnowPlacementTable(Script script, IReadOnlyList<int> cells, IReadOnlyList<LaunchService.ScriptFighter> fighters, bool mine)
    {
        var t = new Table(script);
        foreach (int c in cells)
            if (c >= 0) t.Set(c, DynValue.NewNumber(-1));
        foreach (var f in fighters)
            if (f.Alive && f.Cell >= 0 && f.ActorId != 0 && f.IsEnemy != mine)
                t.Set(f.Cell, DynValue.NewNumber(f.ActorId > 0 ? f.ActorId : -f.ActorId));
        return DynValue.NewTable(t);
    }

    /// <summary>Pompe des hooks (thread du script, hors hook) : challengeManagement(n, estSuiveur) UNE fois par placement.
    /// Chef / solo : dès que les propositions de défi sont arrivées (n = nombre de propositions ; aucune -> pas d'appel, comme
    /// Un bot tiers sans défi), puis la gate de défi du moteur est libérée (le script a eu sa chance -> prêt sans attendre 8 s).
    /// Suiveur: tout de suite. Placement fini -> abandon.</summary>
    private void SnowChallengeTick(CancellationToken ct)
    {
        if (!_snowCmPending) return;
        if (_snowCmFn is not { } cm || _script is not { } script || !ReferenceEquals(cm.Function?.OwnerScript, script)
            || !_launch.IsPlacementOpen(_pid) || _launch.CurrentFightId(_pid) != _snowCmFight)
        { _snowCmPending = false; return; }
        bool follower = SnowIsFollower();
        var props = _launch.ChallengeProposals(_pid);
        if (!follower && props is null) return;   // chef / solo : propositions pas encore reçues
        _snowCmPending = false;
        int n = props?.Count ?? 0;
        if (follower) n = Math.Max(1, n);
        if (n > 0)
        {
            var tok = _runToken;
            _inHook = true; _hookEp = 0;   // pas de pompe réentrante (placement / tour) pendant le callback
            try { CallLua(script, cm, ct, DynValue.NewNumber(n), DynValue.NewBoolean(follower)); }
            catch (Exception ex) when (!(ct.IsCancellationRequested || Root(ex) is ScriptStopException))
            { ScriptErr("combat", ex, d => Loc.Fmt("L.Lua.HookError", "challengeManagement", d)); }
            finally { if (ReferenceEquals(_runToken, tok)) _inHook = false; }
        }
        if (!follower && !ct.IsCancellationRequested) _launch.SnowReleaseChallengeGate(_pid);
    }

    // ------------------------------------------------------------------ PLANNING (heures de déconnexion, chef ou solo)
    private void SnowPlanningTick(Script script)
    {
        if (_planningDisconnectAsked) return;
        var v = script.Globals.Get("PLANNING");
        if (v.Type != DataType.Table) return;
        if (_launch.TeamViewOf(_pid) is not null && !_launch.IsTeamLeader(_pid)) return;   // Un bot tiers: le chef décide pour l'équipe
        var hours = new HashSet<int>();
        foreach (var x in v.Table.Values)
            if (x.CastToNumber() is double d && d >= 0 && d < 24) hours.Add((int)d);
        if (hours.Count == 0) return;
        var now = DateTime.Now;
        if (!hours.Contains(now.Hour)) return;
        if (hours.Count >= 24) { _planningDisconnectAsked = true; Warn("planning", Loc.T("L.Lua.SnowPlanningAlwaysOff")); return; }
        var next = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind).AddHours(1);
        while (hours.Contains(next.Hour)) next = next.AddHours(1);
        long delayMs = (long)(next - now).TotalMilliseconds + Random.Shared.Next(5_000, 90_000);   // humanisé
        Warn("planning", Loc.Fmt("L.Lua.SnowPlanningOff", next.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)));
        _planningDisconnectAsked = RequestDisconnect(delayMs);   // file pleine -> nouvel essai au prochain tick
    }

    // ------------------------------------------------------------------ FIGHT_KICK (placement : expulser les joueurs étrangers)
    // Un bot tiers expulse par défaut; ici OPT-IN: FIGHT_KICK = délai en ms (> 0) avant d'expulser un joueur qui n'est pas de l'équipe.
    private void SnowFightKickTick(Script script)
    {
        var v = script.Globals.Get("FIGHT_KICK");
        if (v.Type != DataType.Number || v.Number <= 0 || !_launch.IsPlacementOpen(_pid)) { _fightKickSeen.Clear(); return; }
        if (_launch.TeamViewOf(_pid) is not null && !_launch.IsTeamLeader(_pid)) return;   // seul le chef (ou un solo) expulse
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) return;
        var mine = new HashSet<long>(_launch.TeamMembers(_pid).Select(m => _launch.CharacterId(m.Pid))) { _launch.CharacterId(_pid) };
        long now = Environment.TickCount64;
        foreach (var f in _launch.FightersDetail(_pid))
        {
            if (f.IsEnemy || f.IsSelf || f.IsSummon || f.ActorId <= 0 || mine.Contains(f.ActorId)) continue;
            if (!_fightKickSeen.TryGetValue(f.ActorId, out long seen)) { _fightKickSeen[f.ActorId] = now; continue; }
            if (seen < 0 || now - seen < (long)v.Number) continue;
            _fightKickSeen[f.ActorId] = -1;   // une seule tentative par joueur et par placement
            if (_launch.KickFighter(_pid, f.ActorId)) Info("combat", Loc.Fmt("L.Lua.SnowFightKicked", ResolveCharacterName(f.ActorId)));
        }
    }
}
