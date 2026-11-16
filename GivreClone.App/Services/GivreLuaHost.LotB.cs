using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ==================================================================================================================
// Lot B — ponts Lua vers l'état moteur EXISTANT (lectures) + primitive générique d'usage d'interactif (map:useById).
// Tout est lié AVANT DoString (Bind -> BindModules / BindLotB) : les lectures marchent au top-level. Callbacks AOT-safe
// (DynValue.NewCallback), appel colon (self en a[0]) ET point tolérés. Exécuté UNIQUEMENT sur le thread du script.
// ==================================================================================================================
public sealed partial class GivreLuaHost
{
    /// <summary>Issue de la primitive <see cref="UseElement"/> (map:useById / map:useCell ; réutilisée par les lots à venir).</summary>
    internal enum UseOutcome { Used, Ended, MapChanged, Error, Timeout, NotFound, NoSkill, NoReach, MoveFailed, Denied, InFight, NotSent }
    // Grâce après la confirmation serveur d'un interactif posé sur une cellule de TRANSITION world-graph (porte / escalier) :
    // la bascule de carte arrive après l'InteractiveUsedEvent.
    private const int TransitionGraceMs = 3000;

    // Arguments tolérants colon/point (self = table en a[0]).
    private static int ArgOff(CallbackArguments a) => a.Count > 0 && a[0].Type == DataType.Table ? 1 : 0;
    private static int ArgCount(CallbackArguments a) => a.Count - ArgOff(a);
    private static DynValue ArgV(CallbackArguments a, int i) { int j = ArgOff(a) + i; return j < a.Count ? a[j] : DynValue.Nil; }
    private static long ArgL(CallbackArguments a, int i) => (long)(ArgV(a, i).CastToNumber() ?? 0);
    private static int ArgN(CallbackArguments a, int i) => (int)(ArgV(a, i).CastToNumber() ?? 0);
    private static string ArgS(CallbackArguments a, int i) => ArgV(a, i).IsNil() ? "" : (ArgV(a, i).CastToString() ?? "");

    private void ResetSessionCounters()
    {
        Interlocked.Exchange(ref _sHarvests, 0); Interlocked.Exchange(ref _sResources, 0);
        Interlocked.Exchange(ref _sFights, 0); Interlocked.Exchange(ref _sWon, 0); Interlocked.Exchange(ref _sLost, 0);
        Interlocked.Exchange(ref _afterFight, 0);
    }

    /// <summary>Jeton stable du statut (getStatus / character:getStatus).</summary>
    private static string StatusToken(BotActivity a) => a switch
    {
        BotActivity.Dead => "dead", BotActivity.Combat => "fight", BotActivity.Gathering => "gathering",
        BotActivity.Moving => "moving", BotActivity.InGame => "ingame", BotActivity.Connecting => "connecting",
        BotActivity.Disconnected => "disconnected", _ => "offline",
    };

    private static string OutcomeToken(UseOutcome o) => o switch
    {
        UseOutcome.Used => "used", UseOutcome.Ended => "ended", UseOutcome.MapChanged => "mapchanged", UseOutcome.Error => "error",
        UseOutcome.Timeout => "timeout", UseOutcome.NotFound => "notfound", UseOutcome.NoSkill => "noskill", UseOutcome.NoReach => "noreach",
        UseOutcome.MoveFailed => "movefail", UseOutcome.Denied => "denied", UseOutcome.InFight => "fight", _ => "notsent",
    };

    private static DynValue OutcomeTuple(UseOutcome o)
        => DynValue.NewTuple(DynValue.NewBoolean(o is UseOutcome.Used or UseOutcome.Ended or UseOutcome.MapChanged), DynValue.NewString(OutcomeToken(o)));

    // ------------------------------------------------------------------ globals + character: + stats:
    private void BindLotB(Script s, CancellationToken ct)
    {
        var g = s.Globals;
        g["getCharacterId"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.CharacterId(_pid)));
        g["isDead"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.LifeStatusOf(_pid) != 0));
        g["getLifeStatus"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.LifeStatusOf(_pid)));
        g["getPhoenixMapId"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.PhoenixMapId(_pid)));
        g["msSinceFightEnd"] = DynValue.NewCallback((_, a) => { long ms = _launch.MsSinceFightEnd(_pid); return DynValue.NewNumber(ms == long.MaxValue ? -1 : ms); });
        // afterFight: vrai UNE fois après chaque fin de combat, puis se réarme.
        g["afterFight"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(Interlocked.Exchange(ref _afterFight, 0) == 1));
        g["getServerName"] = DynValue.NewCallback((_, a) => DynValue.NewString(_launch.ServerNameOf(_pid)));
        g["getStatus"] = DynValue.NewCallback((_, a) => DynValue.NewString(StatusToken(_launch.ActivityOf(_pid))));
        // Script PRINCIPAL du perso (pas un hôte /cmd transitoire).
        g["isScriptPlaying"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(BotScripts.Get((int)_pid)?.Running ?? false));
        g["getCountGather"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(Volatile.Read(ref _sHarvests)));
        g["getCountFight"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(Volatile.Read(ref _sFights)));
        g["resetCountGather"] = DynValue.NewCallback((_, a) => { Interlocked.Exchange(ref _sHarvests, 0); Interlocked.Exchange(ref _sResources, 0); return DynValue.Nil; });
        g["resetCountFight"] = DynValue.NewCallback((_, a) => { Interlocked.Exchange(ref _sFights, 0); Interlocked.Exchange(ref _sWon, 0); Interlocked.Exchange(ref _sLost, 0); return DynValue.Nil; });
        g["joinBoss"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(JoinLeader()));

        // ---- character: identité du perso piloté + montée de caractéristiques ----
        var ch = new Table(s);
        ch["id"] = g.Get("getCharacterId");
        ch["identifier"] = DynValue.NewCallback((_, a) => DynValue.NewString(_launch.CharacterId(_pid).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        ch["name"] = DynValue.NewCallback((_, a) => DynValue.NewString(_world.Get(_pid).CharacterName ?? ""));
        ch["level"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_world.Get(_pid).Level ?? 0));
        ch["breed"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.SelfCharacterInfo(_pid)?.BreedIndex is int bi ? ClassIconProvider.BreedId(bi) : 0));
        ch["breedName"] = DynValue.NewCallback((_, a) => DynValue.NewString(_launch.SelfCharacterInfo(_pid)?.Breed ?? ""));
        // Un bot tiers: true = FEMME. proto3: GenderIndex 0 = homme (pas « inconnu »).
        ch["sex"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.SelfCharacterInfo(_pid)?.GenderIndex == 1));
        ch["server"] = g.Get("getServerName");
        ch["serverId"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.ServerIdOf(_pid)));
        ch["getStatus"] = g.Get("getStatus");
        ch["statPoints"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.StatPointsLeft(_pid)));
        // Tutoriel guidé d'un perso neuf = sous-zone « Tutoriel guidé » (doc de l'original : character:inTutorial()).
        Func<bool> inTutorial = () => _launch.GameNames.SubAreaName(SubAreaFor(0)) is { } n && n.Contains("Tutoriel", StringComparison.OrdinalIgnoreCase);
        ch["inTutorial"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(inTutorial()));
        g["inTutorial"] = ch["inTutorial"];
        // Sortie du tutoriel : le message de sortie (GuideModQuitRequest, champ fpat) n'est pas encore résolu -> jamais envoyé, raison « notsent ».
        ch["leaveTutorial"] = UnsupportedLua("character:leaveTutorial", "GuideModQuitRequest non résolu");
        g["leaveTutorial"] = ch["leaveTutorial"];
        ch["upgradeStat"] = DynValue.NewCallback((_, a) =>
        {
            var (ok, left) = UpgradeStat(ArgS(a, 0), ArgN(a, 1), ct);
            return DynValue.NewTuple(DynValue.NewBoolean(ok), DynValue.NewNumber(left));
        });
        // Alias un bot tiers: ne renvoient que ok.
        void Alias(string name, string stat) => ch[name] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(UpgradeStat(stat, ArgN(a, 0), ct).Ok));
        Alias("UpgradeVitality", "vitality"); Alias("UpgradeWisdom", "wisdom");
        Alias("UpgradeStrenght", "strength"); Alias("UpgradeStrength", "strength");
        Alias("UpgradeIntelligence", "intelligence"); Alias("UpgradeChance", "chance"); Alias("UpgradeAgility", "agility");
        g["character"] = DynValue.NewTable(ch);

        // ---- stats: compteurs de SESSION du script + résumé PERSISTÉ (lecture seule de BotStatsStore) ----
        var st = new Table(s);
        st["harvests"] = g.Get("getCountGather");
        st["resources"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(Volatile.Read(ref _sResources)));
        st["fights"] = g.Get("getCountFight");
        st["won"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(Volatile.Read(ref _sWon)));
        st["lost"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(Volatile.Read(ref _sLost)));
        st["reset"] = DynValue.NewCallback((_, a) => { ResetSessionCounters(); return DynValue.Nil; });   // NE touche PAS BotStatsStore
        st["summary"] = DynValue.NewCallback((_, a) =>
        {
            var t = new Table(s);
            string? name = _world.Get(_pid).CharacterName;
            if (string.IsNullOrWhiteSpace(name)) return DynValue.NewTable(t);   // perso pas encore identifié (top-level) -> {}
            var snap = ArgS(a, 0).Trim().ToLowerInvariant() switch
            {
                "day" => Stores.Stats.Compute(name, StatsPeriod.Day),
                "week" => Stores.Stats.Compute(name, StatsPeriod.Week),
                "month" => Stores.Stats.Compute(name, StatsPeriod.Month),
                _ => Stores.Stats.Compute(name, StatsPeriod.Day, DateTime.Today),   // "today" (défaut) = jour civil local
            };
            t["harvests"] = DynValue.NewNumber(snap.Harvests); t["resources"] = DynValue.NewNumber(snap.ResourcesQty);
            t["fights"] = DynValue.NewNumber(snap.Fights); t["won"] = DynValue.NewNumber(snap.FightsWon); t["lost"] = DynValue.NewNumber(snap.FightsLost);
            t["deaths"] = DynValue.NewNumber(snap.Deaths); t["kamas"] = DynValue.NewNumber(snap.KamasGained); t["xp"] = DynValue.NewNumber(snap.XpGained);
            t["bankVisits"] = DynValue.NewNumber(snap.BankVisits); t["bottingSec"] = DynValue.NewNumber(snap.BottingSec);
            var maps = new Table(s);
            foreach (var row in snap.Heatmap)
            {
                var map = new Table(s);
                map["map"] = row.MapKey;
                map["visits"] = row.Passages;
                map["harvests"] = row.Harvests;
                maps.Append(DynValue.NewTable(map));
            }
            t["maps"] = maps;
            return DynValue.NewTable(t);
        });
        st["hdvSummary"] = DynValue.NewCallback((_, a) =>
        {
            string? name = _world.Get(_pid).CharacterName;
            if (string.IsNullOrWhiteSpace(name)) return DynValue.Nil;
            string period = ArgS(a, 0).Trim().ToLowerInvariant();
            var range = BotStatsStore.Window(period switch
            {
                "week" => StatsPeriod.Week,
                "month" => StatsPeriod.Month,
                _ => StatsPeriod.Day,
            }, period is "day" or "week" or "month" ? null : DateTime.Today);
            var entries = HdvStatsStore.EntriesInWindow(name, range.From, range.To);
            decimal sales = entries.Where(e => !e.IsTax).Sum(e => (decimal)e.Amount);
            decimal taxes = entries.Where(e => e.IsTax).Sum(e => (decimal)e.Amount);
            var result = new Table(s);
            result["sales"] = DynValue.NewNumber((double)sales);
            result["taxes"] = DynValue.NewNumber((double)taxes);
            result["afterTaxes"] = DynValue.NewNumber((double)(sales - taxes));
            result["acquisitionCostKnown"] = DynValue.False;
            return DynValue.NewTable(result);
        });
        g["stats"] = DynValue.NewTable(st);
    }

    // character:upgradeStat : bloquant (attend la fiche serveur confirmée). (ok, restants) ; (false, -1) si refusé / pas de réponse.
    private (bool Ok, int Left) UpgradeStat(string name, int n, CancellationToken ct)
    {
        ThrowIfStopping(ct);
        int amount = Math.Clamp(n, 1, 1000);
        var left = _launch.UpgradeCharacteristicAsync(_pid, name, amount, ct).GetAwaiter().GetResult();
        if (left is int r)
        {
            Ok("perso", Loc.Fmt("L.Lua.StatUpgradeOk", amount, LaunchService.NormalizeStatName(name) ?? name, r));
            return (true, r);
        }
        if (LaunchService.NormalizeStatName(name) is null) Warn("perso", Loc.Fmt("L.Lua.StatUpgradeBadName", name));
        else if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) Warn("perso", Loc.T("L.Lua.NoPermStatUpgrade"));
        else if (_launch.AutoLevelActive(_pid)) Warn("perso", Loc.T("L.Lua.StatUpgradeBusy"));
        else Warn("perso", Loc.Fmt("L.Lua.StatUpgradeFail", LaunchService.NormalizeStatName(name)));
        return (false, -1);
    }

    // team:joinLeader / joinBoss : met en file (moteur) ; le VM exécute /joinBoss au Tick. Non bloquant.
    private bool JoinLeader()
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Multi)) { Warn("equipe", Loc.T("L.Perm.MultiDenied")); return false; }
        if (_launch.TeamViewOf(_pid) is null) { Warn("equipe", Loc.T("L.Lua.JoinLeaderNotInTeam")); return false; }
        if (!_launch.IsTeamLeader(_pid)) { Warn("equipe", Loc.T("L.Lua.JoinLeaderNotLeader")); return false; }
        bool ok = _launch.EnqueueTeamJoinLeader(_pid);
        if (ok) Ok("equipe", Loc.T("L.Lua.JoinLeaderSent"));
        return ok;
    }

    // map:getMonsters enrichi : count (monstres), level (somme, 0 si inconnu), monsters={{gid,name,count}}, members={{gid,level}}.
    private static void FillMonsterGroupLotB(Script s, Table r, MapActor m)
    {
        int count = m.Members?.Count ?? m.Monsters?.Sum(e => e.Count) ?? 0;
        r["count"] = DynValue.NewNumber(count);
        r["level"] = DynValue.NewNumber(m.GroupLevel);
        r["monsters"] = BuildList(s, m.Monsters ?? (IReadOnlyList<MonsterEntry>)Array.Empty<MonsterEntry>(),
            (t, e) => { t["gid"] = DynValue.NewNumber(e.GenericId); t["name"] = DynValue.NewString(e.Name ?? ""); t["count"] = DynValue.NewNumber(e.Count); });
        r["members"] = BuildList(s, m.Members ?? (IReadOnlyList<MonsterMember>)Array.Empty<MonsterMember>(),
            (t, e) => { t["gid"] = DynValue.NewNumber(e.GenericId); t["level"] = DynValue.NewNumber(e.Level); });
    }

    // ------------------------------------------------------------------ map: (bloc Lot B, appelé DANS la construction de la table map)
    private void BindMapLotB(Script s, Table map, CancellationToken ct)
    {
        long MapArg(CallbackArguments a, int i) { long id = ArgL(a, i); return id != 0 ? id : (_launch.CurrentMap(_pid) ?? 0); }

        // coordsOf([mapId]) -> x, y (nil si inconnue). (0,0) = aussi la sentinelle « coord non déterminée » de l'extracteur.
        map["coordsOf"] = DynValue.NewCallback((_, a) =>
            _launch.MapCoordOf(MapArg(a, 0)) is (int x, int y) ? DynValue.NewTuple(DynValue.NewNumber(x), DynValue.NewNumber(y)) : DynValue.Nil);
        map["getX"] = DynValue.NewCallback((_, a) => _launch.MapCoordOf(MapArg(a, 0)) is (int x, int _) ? DynValue.NewNumber(x) : DynValue.Nil);
        map["getY"] = DynValue.NewCallback((_, a) => _launch.MapCoordOf(MapArg(a, 0)) is (int _, int y) ? DynValue.NewNumber(y) : DynValue.Nil);

        // pathDistance(a[, b]) : nb de sauts world-graph à PIED (zaaps non comptés, cap 80). 1 arg = depuis la carte courante.
        map["pathDistance"] = DynValue.NewCallback((_, a) =>
        {
            long from, to;
            if (ArgCount(a) >= 2) { from = ArgL(a, 0); to = ArgL(a, 1); }
            else { from = _launch.CurrentMap(_pid) ?? 0; to = ArgL(a, 0); }
            if (from <= 0 || to <= 0) return DynValue.NewNumber(-1);
            long a2 = _launch.GraphMapForId(from), b2 = _launch.GraphMapForId(to);   // ids périmés -> nœud vivant par coordonnée
            if (a2 == b2) return DynValue.NewNumber(0);
            var p = _launch.WorldPath((int)a2, (int)b2, null, _pid);
            return DynValue.NewNumber(p.Count > 0 ? p.Count : -1);
        });
        map["getPathDistance"] = map.Get("pathDistance");

        // neighbourId(dir[, mapId]) : carte RÉELLEMENT atteinte par changeMap(dir), 0 si aucune.
        map["neighbourId"] = DynValue.NewCallback((_, a) =>
        {
            if (!TryParseDir(ArgS(a, 0), out var d)) return DynValue.NewNumber(0);
            return DynValue.NewNumber(_launch.NeighbourMapId(MapArg(a, 1), d));
        });

        // getInteractives() : TOUS les interactifs de la carte (pas seulement les récoltables).
        map["getInteractives"] = DynValue.NewCallback((_, a) =>
        {
            long m = _launch.CurrentMap(_pid) ?? 0;
            if (m != 0 && !_launch.IsInFight(_pid) && _launch.InteractivesDecodedMap(_pid) != m)
                WaitUntil(() => _launch.InteractivesDecodedMap(_pid) == m, InteractivesDecodeTimeoutMs, ct);
            return BuildList(s, _world.Get(_pid).Interactives, (r, x) =>
            {
                r["cell"] = DynValue.NewNumber(x.CellId); r["elementId"] = DynValue.NewNumber(x.ElementId);
                r["typeId"] = DynValue.NewNumber(x.ElementTypeId); r["elementTypeId"] = DynValue.NewNumber(x.ElementTypeId);
                r["name"] = DynValue.NewString(x.Name ?? ""); r["action"] = DynValue.NewString(x.ActionName ?? "");
                r["state"] = DynValue.NewNumber(x.State); r["usable"] = DynValue.NewBoolean(x.Harvestable);
                r["resource"] = DynValue.NewBoolean(_launch.ProducesGatheredItem(x));
                r["interactionCell"] = DynValue.NewNumber(m != 0 ? _launch.InteractionCell(m, x.ElementId) ?? -1 : -1);
                r["skills"] = BuildList(s, x.Skills, (t, sk) =>
                {
                    t["uid"] = DynValue.NewNumber(sk.SkillInstanceUid); t["id"] = DynValue.NewNumber(sk.SkillId);
                    t["name"] = DynValue.NewString(_launch.GameNames.SkillName(sk.SkillId) ?? ""); t["enabled"] = DynValue.NewBoolean(sk.Enabled);
                });
            });
        });

        // useById(elementId[, skill]) / useCell(cell[, skill]) -> ok, outcome. skill : 0 = défaut, >0 = uid d'instance puis
        // skillId, -N = N-ième skill ACTIF.
        map["useById"] = DynValue.NewCallback((_, a) => OutcomeTuple(UseElement(ArgN(a, 0), ArgN(a, 1), ct)));
        map["useCell"] = DynValue.NewCallback((_, a) =>
        {
            int cell = ArgN(a, 0);
            if (_launch.IsInFight(_pid)) { Warn("interactif", Loc.T("L.Lua.UseByIdInFight")); return OutcomeTuple(UseOutcome.InFight); }
            long m = _launch.CurrentMap(_pid) ?? 0;
            if (m != 0) WaitUntil(() => _launch.InteractivesDecodedMap(_pid) == m, InteractivesDecodeTimeoutMs, ct);
            if (_world.Get(_pid).InteractiveAt(cell) is not { } it) { Warn("interactif", Loc.Fmt("L.Lua.UseCellMissing", cell)); return OutcomeTuple(UseOutcome.NotFound); }
            return OutcomeTuple(UseElement(it.ElementId, ArgN(a, 1), ct));
        });
        map["cellIdToInteractive"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_world.Get(_pid).InteractiveAt(ArgN(a, 0))?.ElementId ?? -1));
        map["interactiveIdToCell"] = DynValue.NewCallback((_, a) =>
        {
            int elem = ArgN(a, 0);
            if (_world.Get(_pid).Interactives.FirstOrDefault(i => i.ElementId == elem) is { } it) return DynValue.NewNumber(it.CellId);
            long m = _launch.CurrentMap(_pid) ?? 0;
            return DynValue.NewNumber(m != 0 ? _launch.InteractionCell(m, elem) ?? -1 : -1);
        });

        // Joueurs: countPlayers INCLUT self (; #map:playersOnMap = les autres seulement).
        map["countPlayers"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_world.Get(_pid).Actors.Count(x => x.Kind == MapActorKind.Player)));
        map["isPlayerOnMap"] = DynValue.NewCallback((_, a) =>
        {
            var v = ArgV(a, 0);
            var actors = _world.Get(_pid).Actors;
            if (v.Type == DataType.Number) { long id = (long)v.Number; return DynValue.NewBoolean(actors.Any(x => x.ActorId == id)); }   // tout acteur (monstres négatifs)
            string n = v.IsNil() ? "" : (v.CastToString() ?? "");
            return DynValue.NewBoolean(n.Length > 0 && actors.Any(x => x.Kind == MapActorKind.Player && string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase)));
        });

        // Géométrie : marchable EN COMBAT (même règle que la géométrie de combat : Mov && !NonWalkableDuringFight).
        map["isWalkableFight"] = DynValue.NewCallback((_, a) =>
        {
            int cell = ArgN(a, 0);
            if ((uint)cell >= MapPathfinder.CellCount) return DynValue.False;
            var cells = _world.Get(_pid).Cells;
            var c = cell < cells.Count && cells[cell].CellId == cell ? cells[cell] : cells.FirstOrDefault(x => x.CellId == cell);
            return DynValue.NewBoolean(c is not null && c.Mov && !c.NonWalkableDuringFight);
        });
        // getWalkableCells([hasPath]): TABLEAU de nombres. hasPath = seulement celles atteignables depuis self.
        map["getWalkableCells"] = DynValue.NewCallback((_, a) =>
        {
            var st = _world.Get(_pid);
            long m = _launch.CurrentMap(_pid) ?? 0;
            if (!st.CellsReadyFor(m)) return BuildNums(s, Array.Empty<int>());
            var all = Enumerable.Range(0, MapPathfinder.CellCount).Where(st.IsWalkable);
            if (!ArgV(a, 0).CastToBool()) return BuildNums(s, all);
            if (st.SelfCell is not int src) return BuildNums(s, Array.Empty<int>());
            var reach = new HashSet<int>(MapPathfinder.OrthoReachable(src, MapPathfinder.CellCount, st.IsWalkable, st.StepAllowed).Keys);
            foreach (var c in all)   // reliquat joignable seulement en diagonale : A* sur ces cases uniquement
                if (!reach.Contains(c) && MapPathfinder.KeyMovements(src, c, st.IsWalkable, true, st.StepAllowed).Count > 0) reach.Add(c);
            return BuildNums(s, reach.Where(st.IsWalkable).OrderBy(c => c));
        });
    }

    // ------------------------------------------------------------------ primitive générique d'usage d'interactif
    // Même structure que GatherResource : se placer (awaité) PUIS envoyer l'InteractiveUseRequest et attendre l'issue serveur
    // (iwq erreur / iwi fin / changement de carte / combat / InteractiveUsedEvent pour une action non-récolte). Réutilisée par
    // les lots suivants (zaapi, coffres/maisons à code). Gate réel au point d'action (LaunchService.UseInteractiveElement).
    private UseOutcome UseElement(int elementId, int skillSel, CancellationToken ct, bool verbose = true)
    {
        ThrowIfStopping(ct);
        if (_launch.IsInFight(_pid)) { if (verbose) Warn("interactif", Loc.T("L.Lua.UseByIdInFight")); return UseOutcome.InFight; }
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) { if (verbose) Warn("interactif", Loc.T("L.Lua.NoPermGather")); return UseOutcome.Denied; }
        CloseHdvBeforeMove(ct);   // un HDV ouvert fait refuser les déplacements
        long map = _launch.CurrentMap(_pid) ?? 0;
        if (map != 0) WaitUntil(() => _launch.InteractivesDecodedMap(_pid) == map, InteractivesDecodeTimeoutMs, ct);
        WaitSelfReady(ct);
        var st = _world.Get(_pid);
        var inter = st.Interactives.FirstOrDefault(i => i.ElementId == elementId);
        if (inter is null) { if (verbose) Warn("interactif", Loc.Fmt("L.Lua.UseByIdMissing", elementId)); return UseOutcome.NotFound; }
        string what = inter.Name ?? $"#{elementId}";
        var skill = inter.PickSkill(skillSel);
        if (skill is null) { if (verbose) Warn("interactif", Loc.Fmt("L.Lua.UseByIdNoSkill", what, elementId, skillSel)); return UseOutcome.NoSkill; }
        if (map == 0 || st.SelfCell is not int src) { if (verbose) Warn("interactif", Loc.T("L.Lua.HarvestUnknownCell")); return UseOutcome.NoReach; }

        int? stand = _launch.InteractiveStandCell(_pid, map, src, inter, skill, st.IsWalkable, st.StepAllowed, out var standWalk);
        if (stand is null) { if (verbose) Warn("interactif", Loc.Fmt("L.Lua.UseByIdNoReach", what)); return UseOutcome.NoReach; }
        if (stand.Value != src)
        {
            int standCell = stand.Value;
            var mv = _launch.SendAndWaitAsync(_pid, () => _launch.MoveTo(_pid, map, src, standCell, standWalk, true, st.StepAllowed), MoveTimeoutMs, ct)
                            .GetAwaiter().GetResult();
            ThrowIfStopping(ct);
            if (mv != LaunchService.MoveOutcome.Arrived) { if (verbose) Warn("interactif", Loc.Fmt("L.Lua.UseByIdMoveFail", what, mv)); return UseOutcome.MoveFailed; }
        }

        string skillLabel = _launch.GameNames.SkillName(skill.SkillId) ?? skill.SkillId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (verbose) Info("interactif", Loc.Fmt("L.Lua.UseByIdStart", what, elementId, skillLabel));
        Volatile.Write(ref _usedElement, 0);
        Volatile.Write(ref _usedDuration, 0);
        Volatile.Write(ref _endedElement, 0);
        Volatile.Write(ref _errorElement, 0);
        bool gatherSkill = _launch.IsGatherSkill(inter, skill);   // MÊME règle que le moteur (skill réellement envoyé)
        if (!_launch.UseInteractiveElement(_pid, inter, skill)) { if (verbose) Warn("interactif", Loc.Fmt("L.Lua.HarvestSendFail", what)); return UseOutcome.NotSent; }

        bool Terminal() =>
            Volatile.Read(ref _errorElement) == elementId
            || Volatile.Read(ref _endedElement) == elementId
            || (_launch.CurrentMap(_pid) is long currentMap && currentMap > 0 && currentMap != map)                          // porte / escalier / zaapi
            || _launch.IsInFight(_pid);
        // Phase 1 : issue terminale OU confirmation serveur (InteractiveUsedEvent, émis au DÉBUT de l'action).
        WaitUntil(() => Terminal() || Volatile.Read(ref _usedElement) == elementId,
            gatherSkill ? HarvestTimeoutMs : InteractiveUseTimeoutMs, ct);
        // Phase 2 : la confirmation arrive AVANT l'effet (animation de `duration`, bascule de carte d'une porte / d'un
        // escalier, fin de récolte) -> on attend l'issue réelle avant de rendre « used », sinon le script enchaîne sur
        // l'ancienne carte / pendant l'animation (actions refusées par le serveur).
        if (!Terminal() && Volatile.Read(ref _usedElement) == elementId)
        {
            int dur = Volatile.Read(ref _usedDuration);
            int extra = dur > 0 ? dur * 100 + 1500 : 0;
            if (gatherSkill) extra = Math.Max(extra, HarvestTimeoutMs);                     // récolte : attendre l'iwi de fin
            if (_launch.IsWorldTransitionCell(map, inter.CellId) || _launch.IsWorldTransitionCell(map, stand.Value))
                extra = Math.Max(extra, TransitionGraceMs);                                  // porte / passage : bascule de carte différée
            if (extra > 0) WaitUntil(Terminal, extra, ct);
        }

        UseOutcome o;
        if (Volatile.Read(ref _errorElement) == elementId) o = UseOutcome.Error;
        else if (Volatile.Read(ref _endedElement) == elementId) o = UseOutcome.Ended;
        else if ((_launch.CurrentMap(_pid) is long currentMap && currentMap > 0 && currentMap != map)) o = UseOutcome.MapChanged;
        else if (_launch.IsInFight(_pid)) o = UseOutcome.InFight;
        else if (Volatile.Read(ref _usedElement) == elementId) o = UseOutcome.Used;
        else o = UseOutcome.Timeout;

        if (!verbose) return o;
        if (o is UseOutcome.Used or UseOutcome.Ended or UseOutcome.MapChanged) Ok("interactif", Loc.Fmt("L.Lua.UseByIdOk", what));
        else if (o == UseOutcome.Error) Warn("interactif", Loc.Fmt("L.Lua.UseByIdRefused", what));
        else if (o == UseOutcome.Timeout) Warn("interactif", Loc.Fmt("L.Lua.UseByIdTimeout", what));
        return o;
    }
}
