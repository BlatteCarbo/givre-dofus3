using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Engine.LuaTooling;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ===================== Combat côté script : mort / Phénix, sélection et attaque de groupes, réglages C2, bilan, hooks onTurn / onPlacement =====================
public sealed partial class GivreLuaHost
{
    private const int PhoenixSkillId = 211;   // skill de résurrection porté par l'interactif Phénix

    /// <summary>Séquence de résurrection au Phénix: TOMBE(1) -&gt; libère l'âme (FreeSoulRequest); FANTÔME(2)
    /// -&gt; laisse l'état se stabiliser, voyage jusqu'à la carte du Phénix (donnée par le serveur), trouve l'interactif Phénix
    /// (skill 211 ACTIF) et l'utilise. Un pas par tick : l'appelant reboucle jusqu'au retour "vivant".</summary>
    private void HandleDeath(int life, CancellationToken ct)
    {
        if (life == 1)   // TOMBE -> on libère l'âme pour devenir fantôme
        {
            _launch.SendFreeSoul(_pid);
            InterruptibleDelay(1500, ct);   // laisse le serveur confirmer le passage en fantôme
            return;
        }
        // FANTÔME (2) -> rejoindre puis utiliser le Phénix.
        InterruptibleDelay(4000, ct);   //: laisse l'état fantôme se stabiliser avant de bouger
        if (ct.IsCancellationRequested) return;
        long pmap = _launch.PhoenixMapId(_pid);
        if (pmap > 0 && (_launch.CurrentMap(_pid) ?? 0) != pmap)
        {
            Info("mort", Loc.Fmt("L.Lua.RevivePhoenixTravel", pmap));
            if (!GoToMapId((int)pmap, ct)) { InterruptibleDelay(2000, ct); return; }   // pas encore arrivé -> retenté au tick suivant
        }
        WaitSelfReady(ct);   // self-cell + interactifs confirmés pour la carte courante
        var phoenix = _world.Get(_pid).Interactives.FirstOrDefault(i => i.Skills.Any(s => s.SkillId == PhoenixSkillId && s.Enabled));
        if (phoenix is null) { Warn("mort", Loc.Fmt("L.Lua.RevivePhoenixMissing", _launch.CurrentMap(_pid) ?? 0)); InterruptibleDelay(2500, ct); return; }
        Info("mort", Loc.T("L.Lua.ReviveUsingPhoenix"));
        UseInteractive(phoenix.CellId, ct, verbose: true);   // marche jusqu'au Phénix + InteractiveUseRequest (skill actif = résurrection)
        InterruptibleDelay(2500, ct);   // attend le CharacterLifeStatusEvent "vivant"
    }

    // Lot E (forcefight) : groupes dont l'engagement a ÉCHOUÉ (chemin refusé / combat pas lancé) sur la carte campée. Utilisé
    // UNIQUEMENT en mode camp (camp=true) : sinon un groupe injoignable bouclait à l'infini (et NoteProgress masquait le blocage
    // au watchdog). Remis à zéro au changement de carte et 1×/min (un groupe peut bouger). Les engagements ordinaires utilisent le délai individuel ci-dessous.
    private readonly HashSet<long> _campFightBlocked = new();
    private long _campFightBlockedMap = -1;
    private readonly GivreClone.Engine.EngagementCooldown _engagementCooldown = new();

    private bool FightOne(CancellationToken ct, int preferredCell, bool camp = false)
    {
        ThrowIfStopping(ct);
        if (_launch.IsInFight(_pid)) return true;   // combat déjà en cours (client) -> ne pas interrompre
        // Droit serveur "fight" (audit P1-2 : gate au POINT D'ACTION). FightOne = point unique de démarrage de combat
        // (fight / fightCell / étape de trajet) -> refuser ici bloque tout lancement de combat par le bot.
        if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) { Warn("combat", Loc.T("L.Lua.NoPermFight")); return false; }
        WaitSelfReady(ct);   // attend la position self confirmee (surtout juste apres un changement de carte)
        var st = _world.Get(_pid);
        long map = _launch.CurrentMap(_pid) ?? 0;
        if (map == 0 || st.SelfCell is not int src) return false;
        if (camp && map != _campFightBlockedMap) { _campFightBlockedMap = map; _campFightBlocked.Clear(); }
        var exclude = camp ? _campFightBlocked : _engagementCooldown.Excluded(map, Environment.TickCount64);
        var target = SelectMonsterSettled(st, src, preferredCell, exclude, ct);
        if (target is null) return false;   // RIEN à combattre ici -> on NE fait PAS attendre l'équipe pour rien : sinon, carte NETTOYÉE, le chef gelait ~30 s avant de reprendre le trajet (le bug « il faisait plus rien après le combat »)
        if (!PrepareOperatorRegeneration(ct)) return _launch.IsInFight(_pid);
        st = _world.Get(_pid); src = st.SelfCell ?? src;
        target = SelectMonster(st, src, preferredCell, exclude);
        if (target is null) return false;
        // Lot F : MIN_LIFE_PERCENT > 0 et PV sous le seuil -> régénère AVANT d'engager (seulement s'il y a une cible : jamais
        // assis sur une carte vide). PV inconnus -> pas de blocage (fail-open). Délai dépassé -> on combat quand même.
        int minLife = ReadInt(_script!, "MIN_LIFE_PERCENT", 0);
        if (minLife > 0 && _launch.SelfLifePercent(_pid) is int lp && lp < minLife)
        {
            Info("vie", Loc.Fmt("L.Lua.RegenBeforeFight", lp, minLife));
            Regenerate(Math.Clamp(ReadInt(_script!, "REGEN_UP_TO_PERCENT", 100), Math.Min(minLife, 100), 100), -1, ct);
            if (_launch.IsInFight(_pid)) return true;          // agressé pendant la régén : combat en cours
            st = _world.Get(_pid); src = st.SelfCell ?? src;    // la cible a pu bouger / mourir
            target = SelectMonster(st, src, preferredCell, exclude);
            if (target is null) return false;
        }
        // ÉQUIPE : on a une cible -> le chef ATTEND que les followers EN JEU soient sur sa carte AVANT d'engager (sinon il
        // combattait SEUL et la mule ratait le combat). Borné par TeamWaitTimeoutMs (jamais de blocage infini) ; pendant
        // l'attente le TeamPilot fait venir les followers. On re-sélectionne ensuite (un monstre a pu bouger/mourir).
        if (_launch.TeamCombatWaitPending(_pid))
        {
            Info("combat", Loc.T("L.Lua.CombatWaitTeam"));
            if (!WaitUntil(() => !_launch.TeamCombatWaitPending(_pid), TeamWaitTimeoutMs, ct)) return false;
            if ((_launch.CurrentMap(_pid) ?? 0) != map) return false;
            st = _world.Get(_pid);
            if (st.MapId != map || !st.SelfReadyFor(map) || st.SelfCell is not int regroupedCell) return false;
            src = regroupedCell;
            target = SelectMonster(st, src, preferredCell, exclude);
            if (target is null) return false;
        }   // pas de cible eligible : silencieux (move() rappelle en boucle)
        // Reste-t-il UN AUTRE groupe éligible sur la carte ? Si oui, après ce combat on attend que la liste se repeuple avant de conclure ; sinon on passe tout de suite à la carte suivante.
        _otherEligibleExpected = SelectMonster(st, src, preferredCell, new HashSet<long>(exclude ?? new HashSet<long>()) { target.ActorId }) is not null;
        string label = MonsterLabel(target);
        Info("combat", Loc.Fmt("L.Lua.CombatAttack", label, target.CellId));
        if (!_launch.RegenerationTeamReady(_pid) || _launch.TeamCombatWaitPending(_pid) || (_launch.CurrentMap(_pid) ?? 0) != map
            || st.MapId != map || !st.SelfReadyFor(map)) return false;
        // A script may retain a group from before a newer server actor snapshot.
        // Apply the same observed-identity lease as the right-click map action;
        // never attack a recycled contextual ID after a delayed movement ACK.
        var firstLease = MonsterAttackTargetLease.Capture(st, map, target);
        bool attacked = firstLease is not null && _launch.AttackMonster(_pid, map, src,
            target.ActorId, target.CellId, st.IsWalkableForScript, label, st.StepAllowed,
            () => firstLease.IsCurrent(st), cell => firstLease.IsAtConfirmedArrival(st, cell),
            (cell, submit) => firstLease.TrySubmitFromConfirmedCell(st, cell, submit));
        // Juste après un changement de carte, l'id de carte / l'état lus peuvent retarder sur le serveur (« carte demandée ≠ carte serveur ») :
        // l'attaque échouait alors en « pas de chemin », fight() rendait false et l'expédition sautait la carte. On relit tout et on retente (2 fois).
        for (int retry = 0; retry < 2 && !attacked && !_launch.IsInFight(_pid); retry++)
        {
            InterruptibleDelay(700, ct);
            WaitSelfReady(ct);
            st = _world.Get(_pid); map = _launch.CurrentMap(_pid) ?? 0;
            if (map == 0 || st.SelfCell is not int src2) break;
            src = src2;
            var again = SelectMonster(st, src, preferredCell, exclude);
            if (again is null) break;
            target = again; label = MonsterLabel(target);
            if (!_launch.RegenerationTeamReady(_pid) || _launch.TeamCombatWaitPending(_pid)) return false;
            var retryWorld = st;
            var retryLease = MonsterAttackTargetLease.Capture(retryWorld, map, target);
            attacked = retryLease is not null && _launch.AttackMonster(_pid, map, src,
                target.ActorId, target.CellId, retryWorld.IsWalkableForScript, label, retryWorld.StepAllowed,
                () => retryLease.IsCurrent(retryWorld),
                cell => retryLease.IsAtConfirmedArrival(retryWorld, cell),
                (cell, submit) => retryLease.TrySubmitFromConfirmedCell(retryWorld, cell, submit));
        }
        if (!attacked) { if (camp) _campFightBlocked.Add(target.ActorId); else _engagementCooldown.Failed(map, target.ActorId, Environment.TickCount64); Warn("combat", Loc.T("L.Lua.CombatNoPath")); return false; }
        if (!WaitUntil(() => _launch.IsInFight(_pid), FightStartTimeoutMs, ct)) { if (camp) _campFightBlocked.Add(target.ActorId); else _engagementCooldown.Failed(map, target.ActorId, Environment.TickCount64); Warn("combat", Loc.T("L.Lua.CombatNoStart")); return false; }
        Info("combat", Loc.T("L.Lua.CombatEngaged"));
        // Lot G: hooks de paquets servis pendant le combat. Compat un bot tiers: le fil du script reste ICI tout le combat
        // (placement compris) -> FIGHT_KICK est servi dans la condition d'attente (no-op sans le global ni hors placement).
        if (!WaitUntil(() => { if (_script is { } sk) SnowFightKickTick(sk); return !_launch.IsInFight(_pid); }, FightEndTimeoutMs, ct, pump: true))
        {
            // A timeout is not a server-confirmed fight end. Keep the script in the fight path;
            // do not timestamp a fictitious end or announce success while combat is still active.
            Warn("combat", Loc.T("L.Lua.CombatEndTimeout"));
            return false;
        }
        // fight() is also called inside imperative move() functions: their next statement must see the roleplay map.
        WaitForPostFightRoleplay(ct);
        Ok("combat", Loc.T("L.Lua.CombatEnded"));
        _lastFightEndTick = Environment.TickCount64;
        return true;
    }

    private void WaitForPostFightRoleplay(CancellationToken ct)
    {
        while (_launch.IsPostFightMapPending(_pid) && !_launch.IsInFight(_pid))
            WaitUntil(() => !_launch.IsPostFightMapPending(_pid) || _launch.IsInFight(_pid), 5000, ct, pump: true);
    }

    // Juste après un combat, la liste des groupes de la carte se repeuple avec un léger retard : conclure tout de suite « rien à combattre » faisait quitter la carte après UN seul combat
    // alors qu'il restait d'autres groupes (expédition : 1 combat par carte). On attend donc, une seule fois et de façon bornée, que la liste reparaisse.
    private const int PostFightSettleMs = 3500;
    private long _lastFightEndTick;
    private bool _otherEligibleExpected;   // un autre groupe éligible était sur la carte avant le combat

    private MapActor? SelectMonsterSettled(BotWorldState st, int src, int preferredCell, HashSet<long>? exclude, CancellationToken ct)
    {
        var t = SelectMonster(st, src, preferredCell, exclude);
        if (t is not null || _lastFightEndTick == 0 || !_otherEligibleExpected) return t;   // aucun autre groupe attendu : carte nettoyée, on ne perd pas une seconde
        long since = Environment.TickCount64 - _lastFightEndTick;
        if (since < 0 || since >= PostFightSettleMs) return null;
        WaitUntil(() => _world.Get(_pid).Monsters.Any() || _launch.IsInFight(_pid), (int)(PostFightSettleMs - since), ct, pump: true);
        _lastFightEndTick = 0;   // une seule attente par combat terminé
        if (_launch.IsInFight(_pid)) return null;
        var st2 = _world.Get(_pid);
        return SelectMonster(st2, st2.SelfCell ?? src, preferredCell, exclude);
    }

    // ------------------------------------------------------------------ selection de groupe de monstres (filtres du script)
    private MapActor? SelectMonster(BotWorldState st, int src, int preferredCell, HashSet<long>? exclude = null)
    {
        var groups = st.Monsters.Where(g => exclude is null || !exclude.Contains(g.ActorId)).ToList();
        if (groups.Count == 0) return null;

        int min = ReadInt(_script!, "MIN_MONSTERS", 1);
        int max = ReadInt(_script!, "MAX_MONSTERS", 999);
        var (forbidNames, forbidIds) = ReadMonsterSet(_script!, "FORBIDDEN_MONSTERS");
        var (forceNames, forceIds) = ReadMonsterSet(_script!, "FORCE_MONSTERS");
        bool hasForce = forceNames.Count > 0 || forceIds.Count > 0;
        var amounts = ReadMonsterAmounts();   // compat tiers config:setAmountOfSpecificMonsters (global MONSTERS_AMOUNT)

        bool Allowed(MapActor g)
        {
            int size = g.Monsters?.Sum(m => m.Count) ?? 1;
            if (size < min || size > max) return false;
            var mons = g.Monsters ?? Array.Empty<MonsterEntry>();
            if (mons.Any(m => forbidIds.Contains(m.GenericId) || forbidNames.Contains((m.Name ?? "").ToLowerInvariant()))) return false;
            if (hasForce && !mons.Any(m => forceIds.Contains(m.GenericId) || forceNames.Contains((m.Name ?? "").ToLowerInvariant()))) return false;
            foreach (var (gid, range) in amounts)
            {
                int c = mons.Where(m => m.GenericId == gid).Sum(m => m.Count);
                if (c > 0 && (c < range.Min || c > range.Max)) return false;
            }
            return true;
        }

        var eligible = groups.Where(Allowed).ToList();
        if (eligible.Count == 0) return null;
        return Expedition.MonsterGroupReachability.Select(eligible, src, preferredCell, st.IsWalkableForScript,
            st.StepAllowed, geometryKnown: st.CellsMap == st.MapId && st.Cells.Count > 0);
    }

    private static string MonsterLabel(MapActor g) =>
        g.Monsters is { Count: > 0 } m ? string.Join("+", m.Select(x => x.Name)) : (g.Name ?? "groupe");

    // Ancien libellé PNJ de map:getNpcs() (avant que name ne devienne le nom seul) : « Nom (npcId) », ou « PNJ <npcId> » tel
    // quel quand le nom est inconnu (le lecteur pose alors ce repli dans Name). Compat des scripts qui comparaient ce texte.
    private static string NpcLegacyLabel(MapActor x)
    {
        string name = x.Name ?? "";
        if (x.NpcId <= 0) return name;
        string id = x.NpcId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return name.Length == 0 || name == "PNJ " + id ? "PNJ " + id : name + " (" + id + ")";
    }

    // ------------------------------------------------------------------ Lot C2 : réglages de combat, défis, bilan
    // Réglages : couche EN MÉMOIRE (LaunchService.ScriptSetCombat) au-dessus du profil de l'onglet Combat, jamais écrite dans
    // combat.json ; effacée au (re)démarrage / arrêt du script. Les noms sont parsés par switch (pas d'Enum.Parse : AOT).
    private static string StyleName(CombatStyle v) => v switch { CombatStyle.Fleeing => "Fleeing", CombatStyle.Passive => "Passive", _ => "Aggressive" };
    private static string TargetName(TargetMode v) => v switch { TargetMode.LowestHp => "LowestHp", TargetMode.Farthest => "Farthest", _ => "Closest" };
    private static string SpeedName(CombatSpeed v) => v switch { CombatSpeed.Turbo => "Turbo", CombatSpeed.Instant => "Instant", CombatSpeed.Slow => "Slow", _ => "Fast" };
    private static string ChallengeModeName(CombatProfile p) => p.ChallengeByScript ? "script" : p.AutoChallenge ? "auto" : "off";

    private void BindCombatC2(Script s, Table combat, CancellationToken ct)
    {
        static int B(CallbackArguments a) => a.Count > 0 && a[0].Type == DataType.Table ? 1 : 0;   // self présent ? (appel colon)
        static int N(CallbackArguments a, int i) { int j = B(a) + i; return j < a.Count ? (int)(a[j].CastToNumber() ?? 0) : 0; }
        static string S(CallbackArguments a, int i) { int j = B(a) + i; return j < a.Count ? (a[j].CastToString() ?? "") : ""; }
        static bool Bo(CallbackArguments a, int i) { int j = B(a) + i; return j < a.Count && a[j].CastToBool(); }
        static bool Has(CallbackArguments a, int i) { int j = B(a) + i; return j < a.Count && !a[j].IsNil(); }

        DynValue Set(string key, string value, Action<ScriptCombatOverrides> edit)
        {
            _launch.ScriptSetCombat(_pid, edit);
            Info("combat", Loc.Fmt("L.Lua.CombatSet", key, value));
            return DynValue.True;
        }
        DynValue Bad(string verb, string input, string expected)
        {
            Warn("combat", Loc.Fmt("L.Lua.CombatBadValue", verb, input, expected));
            return DynValue.False;
        }
        DynValue NoPerm() { Warn("combat", Loc.T("L.Lua.NoPermFight")); return DynValue.False; }
        bool CanFight() => LicenseService.HasPermission(LicenseService.Perm.Fight);

        combat["setStyle"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            string raw = S(a, 0);
            CombatStyle? v = raw.Trim().ToLowerInvariant() switch
            {
                "aggressive" or "agressif" or "aggro" => CombatStyle.Aggressive,
                "fleeing" or "fuyard" or "flee" or "kite" => CombatStyle.Fleeing,
                "passive" or "passif" => CombatStyle.Passive,
                _ => null,
            };
            if (v is not { } st) return Bad("combat:setStyle", raw, "Aggressive | Fleeing | Passive");
            return Set("style", StyleName(st), o => o.Style = st);
        });
        combat["setTargetMode"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            string raw = S(a, 0);
            TargetMode? v = raw.Trim().ToLowerInvariant() switch
            {
                "closest" or "nearest" or "proche" => TargetMode.Closest,
                "lowesthp" or "lowest" or "weakest" or "pv" => TargetMode.LowestHp,
                "farthest" or "furthest" or "loin" => TargetMode.Farthest,
                _ => null,
            };
            if (v is not { } tm) return Bad("combat:setTargetMode", raw, "Closest | LowestHp | Farthest");
            return Set("target", TargetName(tm), o => o.Target = tm);
        });
        combat["setSpeed"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            string raw = S(a, 0);
            CombatSpeed? v = raw.Trim().ToLowerInvariant() switch
            {
                "turbo" => CombatSpeed.Turbo,
                "instant" => CombatSpeed.Instant,
                "fast" or "rapide" => CombatSpeed.Fast,
                "slow" or "lent" => CombatSpeed.Slow,
                _ => null,
            };
            if (v is not { } sp) return Bad("combat:setSpeed", raw, "Turbo | Instant | Fast | Slow");
            return Set("speed", SpeedName(sp), o => o.Speed = sp);
        });
        // setKiteRange(min [, max]) : bornes 0..20 ; max 0 = illimité ; max < min -> max = min.
        combat["setKiteRange"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            int min = Math.Clamp(N(a, 0), 0, 20), max = Math.Clamp(N(a, 1), 0, 20);
            if (max > 0 && max < min) max = min;
            return Set("kite", $"{min}..{(max == 0 ? "∞" : max.ToString())}", o => { o.KiteMin = min; o.KiteMax = max; });
        });
        combat["setMaxCasts"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            int n = Math.Clamp(N(a, 0), 1, 99);
            return Set("maxCastsPerTurn", n.ToString(), o => o.MaxCastsPerTurn = n);
        });
        // setAutoSpells(bool) : mode automatique (tous les sorts appris à dégâts complètent la rotation).
        combat["setAutoSpells"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            bool v = Bo(a, 0);
            return Set("autoSpells", v ? "true" : "false", o => o.AutoSpells = v);
        });
        combat["setFinishKill"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            bool v = Bo(a, 0);
            return Set("finishKill", v ? "true" : "false", o => o.FinishKill = v);
        });
        // setSpellEnabled(spellId, bool) : active/désactive un sort DÉJÀ présent dans la rotation de l'onglet Combat.
        combat["setSpellEnabled"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            int id = N(a, 0); bool v = Bo(a, 1);
            if (!_launch.BaseHasSpell(_pid, id))
            {
                var ids = _launch.EffectiveCombatProfile(_pid)?.Spells.Select(x => x.SpellId.ToString()) ?? Enumerable.Empty<string>();
                return Bad("combat:setSpellEnabled", id.ToString(), string.Join(", ", ids));
            }
            return Set($"spell {id}", v ? "true" : "false", o => o.SpellEnabled[id] = v);
        });
        combat["setAutoPreFight"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            bool v = Bo(a, 0);
            if (v && !CanFight()) return NoPerm();
            return Set("autoPreFight", v ? "true" : "false", o => o.AutoPreFight = v);
        });
        // setChallengeMode("off" | "auto" | "script") : off = aucun défi ; auto = le moteur valide le défi accepté le plus
        // rentable (liste « Défis acceptés » de l'onglet Combat, cf ChallengeCatalog) ;
        // script = le moteur demande les propositions et attend (borné) combat:selectChallenge.
        DynValue SetChallengeMode(string verb, string raw)
        {
            string m = raw.Trim().ToLowerInvariant();
            if (m is not ("off" or "auto" or "script")) return Bad(verb, raw, "off | auto | script");
            if (m != "off" && !CanFight()) return NoPerm();
            bool auto = m == "auto", byScript = m == "script";
            return Set("challengeMode", m, o => { o.AutoChallenge = auto; o.ChallengeByScript = byScript; });
        }
        combat["setChallengeMode"] = DynValue.NewCallback((_, a) => { ThrowIfStopping(ct); return SetChallengeMode("combat:setChallengeMode", S(a, 0)); });
        combat["setAutoChallenge"] = DynValue.NewCallback((_, a) => { ThrowIfStopping(ct); return SetChallengeMode("combat:setAutoChallenge", Bo(a, 0) ? "auto" : "off"); });
        combat["resetProfile"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            bool had = _launch.ClearScriptCombatOverrides(_pid);
            if (had) Ok("combat", Loc.T("L.Lua.CombatProfileReset"));
            return DynValue.NewBoolean(had);
        });
        // getProfile() : profil EFFECTIF (onglet Combat + réglages du script), nil tant que l'UI n'en a pas poussé.
        combat["getProfile"] = DynValue.NewCallback((_, a) =>
        {
            if (_launch.EffectiveCombatProfile(_pid) is not { } p) return DynValue.Nil;
            bool? afo = _launch.AutoFightOverride(_pid);
            var t = new Table(s);
            t["autoFight"] = DynValue.NewBoolean(afo ?? p.AutoFight);
            t["autoPreFight"] = DynValue.NewBoolean(p.AutoPreFight);
            t["autoChallenge"] = DynValue.NewBoolean(p.AutoChallenge);
            t["challengeMode"] = DynValue.NewString(ChallengeModeName(p));
            t["style"] = DynValue.NewString(StyleName(p.Style));
            t["target"] = DynValue.NewString(TargetName(p.Target));
            t["speed"] = DynValue.NewString(SpeedName(p.Speed));
            t["finishKill"] = DynValue.NewBoolean(p.FinishKill);
            t["autoSpells"] = DynValue.NewBoolean(p.AutoSpells);
            t["maxCastsPerTurn"] = DynValue.NewNumber(p.MaxCastsPerTurn);
            t["kiteMin"] = DynValue.NewNumber(p.KiteMinDistance);
            t["kiteMax"] = DynValue.NewNumber(p.KiteMaxDistance);
            t["scripted"] = DynValue.NewBoolean(_launch.HasScriptCombatOverrides(_pid) || afo is not null);
            t["spells"] = BuildList(s, p.Spells, (r, sp) =>
            {
                r["id"] = DynValue.NewNumber(sp.SpellId); r["priority"] = DynValue.NewNumber(sp.Priority);
                r["enabled"] = DynValue.NewBoolean(sp.Enabled); r["perTurn"] = DynValue.NewNumber(sp.PerTurn);
            });
            return DynValue.NewTable(t);
        });

        // ---- Défis ----
        // challengeProposals([timeoutMs=3000]) : propositions du combat en cours { id, xpBonus, dropBonus, state, targets={{id, cell}} }.
        // Demande-les (kty) si rien n'est encore arrivé, puis attend (BLOQUANT, borné 0..10000 ms). Vide hors combat.
        combat["challengeProposals"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            int timeout = Has(a, 0) ? Math.Clamp(N(a, 0), 0, 10000) : 3000;
            // Chef d'équipe : la demande attend d'abord (borné) l'arrivée des followers ; le délai `timeoutMs` court APRÈS.
            if (_launch.IsInFight(_pid) && _launch.ChallengeProposals(_pid) is null && _launch.RequestChallengeProposalsAsync(_pid, ct).GetAwaiter().GetResult())
                WaitUntil(() => _launch.ChallengeProposals(_pid) is not null, timeout, ct);
            ThrowIfStopping(ct);
            var props = _launch.IsInFight(_pid) ? _launch.ChallengeProposals(_pid) ?? Array.Empty<FightPrepReader.ChallengeProposal>() : Array.Empty<FightPrepReader.ChallengeProposal>();
            return BuildList(s, props, (r, c) =>
            {
                r["id"] = DynValue.NewNumber(c.Id); r["xpBonus"] = DynValue.NewNumber(c.XpBonus);
                r["dropBonus"] = DynValue.NewNumber(c.DropBonus); r["state"] = DynValue.NewNumber(c.State);
                r["targets"] = BuildList(s, c.Targets, (tr, tg) => { tr["id"] = DynValue.NewNumber(tg.Id); tr["cell"] = DynValue.NewNumber(tg.Cell); });
            });
        });
        // selectChallenge(id) : sélection + validation d'un défi proposé (~350 ms) ; libère le pré-combat (-> prêt).
        combat["selectChallenge"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            int id = N(a, 0);
            var res = _launch.SelectChallengeAsync(_pid, id, ct).GetAwaiter().GetResult();
            ThrowIfStopping(ct);
            if (res == LaunchService.ChallengePick.Ok) { Ok("combat", Loc.Fmt("L.Lua.ChallengeSelected", id)); return DynValue.True; }
            if (res == LaunchService.ChallengePick.NoPerm) return NoPerm();
            string why = res switch
            {
                LaunchService.ChallengePick.NotInFight => "L.Lua.ChallengeWhyNotInFight",
                LaunchService.ChallengePick.PlacementOver => "L.Lua.ChallengeWhyPlacementOver",
                LaunchService.ChallengePick.Follower => "L.Lua.ChallengeWhyFollower",
                LaunchService.ChallengePick.Already => "L.Lua.ChallengeWhyAlready",
                LaunchService.ChallengePick.SendFailed => "L.Lua.ChallengeWhySendFailed",
                _ => "L.Lua.ChallengeWhyUnknown",
            };
            Warn("combat", Loc.Fmt("L.Lua.ChallengeRefused", id, Loc.T(why)));
            return DynValue.False;
        });
        // challengeResult(id): 1 réussi, 0 échoué, -1 inconnu.
        combat["challengeResult"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.ChallengeResult(_pid, N(a, 0))));
        // Bonus XP/Butin : le serveur suit le choix MÉMORISÉ par le client Dofus, pas la requête réseau -> non pilotable.
        combat["setChallengeBonus"] = UnsupportedLua("combat:setChallengeBonus", "Le bonus suit le choix mémorisé du client Dofus");

        // ---- Bilan du dernier combat ----
        combat["lastFightResult"] = DynValue.NewCallback((_, a) => _launch.LastFightResult(_pid) is { } r ? FightResultTable(s, r) : DynValue.Nil);
    }

    // ------------------------------------------------------------------ Lot C2 : bilan de combat (onFightEnd / lastFightResult)
    // Table Lua du bilan d'un combat. won/lost/draw dérivés de outcome (2/0/1 ; -1 inconnu) ; xp/kamas omis si non lus.
    private DynValue FightResultTable(Script s, FightResultInfo r)
    {
        var t = new Table(s);
        t["won"] = DynValue.NewBoolean(r.Outcome == 2);
        t["lost"] = DynValue.NewBoolean(r.Outcome == 0 && r.Decoded);
        t["draw"] = DynValue.NewBoolean(r.Outcome == 1);
        t["outcome"] = DynValue.NewNumber(r.Outcome);
        t["decoded"] = DynValue.NewBoolean(r.Decoded);
        t["durationMs"] = DynValue.NewNumber(r.DurationMs);
        t["serverDuration"] = DynValue.NewNumber(r.ServerDuration);
        if (r.Xp is long xp) t["xp"] = DynValue.NewNumber(xp);
        if (r.Kamas is long k) t["kamas"] = DynValue.NewNumber(k);
        t["loot"] = BuildList(s, r.Loot, (row, l) =>
        {
            row["gid"] = DynValue.NewNumber(l.Gid); row["qty"] = DynValue.NewNumber(l.Qty);
            row["name"] = DynValue.NewString(_launch.ItemDisplayName(l.Gid) ?? "");
        });
        t["challenges"] = BuildList(s, r.Challenges, (row, c) => { row["id"] = DynValue.NewNumber(c.Id); row["success"] = DynValue.NewBoolean(c.Success); });
        t["fightId"] = DynValue.NewNumber(r.FightId);
        t["msAgo"] = DynValue.NewNumber(Environment.TickCount64 - r.EndedAtTick);
        return DynValue.NewTable(t);
    }

    /// <summary>Thread du SCRIPT (RunLoop, hors combat, carte posée) : livre UNE fois le dernier bilan à onFightEnd(result).
    /// Plusieurs combats finis entre deux passages -> seul le dernier est livré (les autres via lastFightResult). Jamais
    /// appelé pendant un tour ni depuis un callback CLR. Erreur du hook = erreur de script (comme move()).</summary>
    private void DispatchFightEnd(Script script, CancellationToken ct)
    {
        var r = _launch.LastFightResult(_pid);
        if (r is null || r.Seq == _fightEndSeqSeen) return;
        _fightEndSeqSeen = r.Seq;
        var fn = script.Globals.Get("onFightEnd");   // relu à chaque passage (peut être défini tard)
        if (fn.Type == DataType.Function) CallLua(script, fn, ct, FightResultTable(script, r));
    }

    // ------------------------------------------------------------------ hooks de combat (thread du script UNIQUEMENT)
    private bool HooksActive => (_turnFn is not null || _placeFn is not null) && !_inHook;
    /// <summary>Époque du tour réclamé par le hook onTurn EN COURS (0 hors hook de tour) : lie les actions à CE tour.</summary>
    private int HookEpoch => _inHook ? _hookEp : 0;
    private void ThrowIfHookTurnGone()
    {
        int ep = HookEpoch;
        if (ep != 0 && _launch.ScriptTurnGone(_pid, ep)) throw new HookExpiredException();
    }

    // Réclame placement/tour tendus par le moteur et appelle le hook Lua correspondant. Appel Lua imbriqué depuis un
    // callback CLR sur le MÊME thread = supporté par MoonSharp (jamais d'appel depuis un autre thread).
    private void PumpFightHooks(CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !HooksActive || _script is null || !_launch.IsInFight(_pid)) return;   // run annulé (relance) : ne rien réclamer
        if (_placeFn is not null && _launch.TryClaimScriptPlacement(_pid)) RunPlacementHook(ct);
        SnowChallengeTick(ct);   // compat tiers: challengeManagement dès que les défis sont proposés (no-op sans lui)
        if (_turnFn is not null && _launch.TryClaimScriptTurn(_pid) is int ep and > 0) RunTurnHook(ep, ct);
    }

    private void RunTurnHook(int ep, CancellationToken ct)
    {
        var tok = _runToken;   // run propriétaire : un ancien thread qui revient tard ne touche pas l'état du run relancé
        _inHook = true; _hookEp = ep;
        bool failed = false;
        Emit(LuaLogLevel.Debug, "combat", Loc.Fmt("L.Lua.TurnHookStart", _launch.FightTurnNo(_pid), _turnFnName));
        // Budget : le timer ne touche QUE le moteur (dictionnaires concurrents + injection thread-safe), jamais Lua.
        using (new Timer(_ => { if (!ct.IsCancellationRequested) _launch.ExpireScriptTurn(_pid, ep); }, null, _turnBudgetMs, Timeout.Infinite))
        {
            try { CallLua(_script!, _turnFn!, ct); }
            catch (Exception ex) when (Root(ex) is HookExpiredException) { /* tour perdu (budget/fin) : hook déroulé, pas une erreur */ }
            catch (Exception ex) when (!(ct.IsCancellationRequested || Root(ex) is ScriptStopException))
            { ScriptErr("combat", ex, d => Loc.Fmt("L.Lua.TurnHookError", _turnFnName, d)); failed = true; }
            finally { if (ReferenceEquals(_runToken, tok)) { _inHook = false; _hookEp = 0; } }
        }
        if (_launch.ScriptTurnExpired(_pid, ep)) Warn("combat", Loc.Fmt("L.Lua.TurnHookBudget", _turnFnName, _turnBudgetMs));
        if (ct.IsCancellationRequested) return;
        // Hook en erreur SANS rien avoir fait + auto-combat du profil actif : le moteur joue ce tour (au lieu de le passer).
        // Alias un bot tiers fightManagement dans ce cas = bloc hérité inutilisable (tables fightCharacter/fightBasic absentes):
        // on le désactive pour le reste du run, le moteur reprend tous les tours suivants.
        if (failed && _launch.EngineTakeoverAfterHookErrorAsync(_pid, ep, ct).GetAwaiter().GetResult())
        {
            if (_turnFnName == "fightManagement" && ReferenceEquals(_runToken, tok))
            {
                _turnFn = null;
                _launch.DropScriptTurnHook(_pid, tok);
                Warn("combat", Loc.T("L.Lua.FightMgmtEngineFallback"));
            }
            return;
        }
        _launch.FinishScriptTurnAsync(_pid, ep, ct).GetAwaiter().GetResult();   // fin de tour auto si le script ne l'a pas faite
    }

    private void RunPlacementHook(CancellationToken ct)
    {
        var tok = _runToken;
        _inHook = true; _hookEp = 0;
        DynValue ret = DynValue.Nil;
        using (new Timer(_ => { if (!ct.IsCancellationRequested) _launch.ExpireScriptPlacement(_pid); }, null, _turnBudgetMs, Timeout.Infinite))
        {
            try { ret = CallLua(_script!, _placeFn!, ct); }
            catch (Exception ex) when (!(ct.IsCancellationRequested || Root(ex) is ScriptStopException))
            { ScriptErr("combat", ex, d => Loc.Fmt("L.Lua.PlacementHookError", d)); }
            finally { if (ReferenceEquals(_runToken, tok)) _inHook = false; }
        }
        // return false -> le script garde la main sur le prêt (sinon prêt automatique, attente bornée des followers).
        if (!ct.IsCancellationRequested && !(ret.Type == DataType.Boolean && !ret.Boolean))
            _launch.ScriptFinishPlacementAsync(_pid, ct).GetAwaiter().GetResult();
    }
}
