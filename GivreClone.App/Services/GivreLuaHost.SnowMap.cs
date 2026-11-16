using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ==================================================================================================================
// Compatibilité tiers — map: (mapplug.vb) et tables de combat fightCharacter / fightBasic / fightDebug / fightSlave +
// verbes manquants de fightAction (FightManager.vb). Tout est un ENVELOPPEUR des primitives moteur existantes (déplacement,
// portes, zaaps, sélection de groupe, auto-combat) : aucun nouveau paquet. `self` détecté par référence (Off / At).
// Écarts assumés (documentés) : moveNext / moveRoadNext font le trajet COMPLET (bloquant) jusqu'à la cible chargée ;
// map:aggression (agression PvP en masse) n'est pas proposé ; fightSlave (invocations contrôlées) lève une erreur explicite (sauf status).
// ==================================================================================================================
public sealed partial class GivreLuaHost
{
    private long _roadTarget;                    // map:loadMove* -> cible de moveNext / moveRoadNext
    private (long Map, int Seq)? _mapChangeMark;  // map:listenChange / hasChanged
    private readonly Dictionary<int, int> _fightForce = new();   // fightCharacter:forceAP/MP/PO (valables le tour courant)
    private long _fightForceTurn = -1;   // (combat, tour) des valeurs forcées
    private readonly Dictionary<uint, (int Min, int Max)> _monsterAmountConstraints = new();
    
    internal static bool MonsterAmountsAllowed(IReadOnlyList<MonsterEntry> monsters,
        IReadOnlyDictionary<int, (int Min, int Max)> amounts)
    {
        foreach (var (gid, range) in amounts)
        {
            long count = monsters.Where(m => m.GenericId == gid).Sum(m => (long)m.Count);
            // SelectMonster applique les bornes aux types presents seulement.
            if (count > 0 && (count < range.Min || count > range.Max)) return false;
        }
        return true;
    }

    private void BindSnowMap(Script s, CancellationToken ct)
    {
        var map = GetOrCreateTable(s, "map");
        var G = s.Globals;
        DynValue V(CallbackArguments a, int i) => At(a, map, i);
        int N(CallbackArguments a, int i) => (int)(V(a, i).CastToNumber() ?? 0);
        long L(CallbackArguments a, int i) => (long)(V(a, i).CastToNumber() ?? 0);
        string S(CallbackArguments a, int i) => V(a, i).IsNil() ? "" : (V(a, i).CastToString() ?? "");

        // changeMap(dir) : 'top|right' (au hasard), 'dir(cell)', 'zaap(id)', 'zaapi(id)', 'zaapcarte(id)', 'havenbag'.
        Fill(map, "changeMap", DynValue.NewCallback((_, a) =>
        {
            string pick = LuaCompat.PickAlternative(S(a, 0), Random.Shared);
            if (LuaCompat.TryParseTeleportKeyword(pick, out var kw, out long m)) return DynValue.NewBoolean(RunTeleportKeyword(kw, m, ct));
            return DynValue.NewBoolean(ChangeMap(pick, ct));
        }));
        Fill(map, "moveToCell", DynValue.NewCallback((_, a) => DynValue.NewBoolean(GoToCell(N(a, 0), ct))));
        // door(cell | "a|b") : marche vers la porte / l'interactif et l'utilise (même code que la clé de route door=).
        Fill(map, "door", DynValue.NewCallback((_, a) =>
        {
            int c = ChooseDoorCell(S(a, 0));
            return DynValue.NewBoolean(c >= 0 && UseInteractive(c, ct, verbose: true));
        }));
        if (G.Get("gatherAll").Type == DataType.ClrFunction) Fill(map, "gather", G.Get("gatherAll"));
        if (G.Get("fight").Type == DataType.ClrFunction) { Fill(map, "fight", G.Get("fight")); Fill(map, "launchFight", G.Get("fight")); }
        // forceFight: combat dès qu'un groupe éligible est présent, sinon attend les respawns.
        // Garde-fous GivreClone pendant l'attente (la boucle RunLoop ne tourne pas tant que ce callback bloque) : sans droit « fight »
        // -> false d'emblée (sinon boucle infinie de refus) ; entre deux tranches de camp : popup modérateur + MODERATOR_STOP ->
        // arrêt (même séquence qu'en tête de RunLoop), PLANNING un bot tiers, hooks de paquets / MP pompés. CampWait note la progression.
        Fill(map, "forceFight", DynValue.NewCallback((_, a) =>
        {
            if (_launch.IsInFight(_pid)) return DynValue.True;
            if (!LicenseService.HasPermission(LicenseService.Perm.Fight)) { Warn("combat", Loc.T("L.Lua.NoPermFight")); return DynValue.False; }
            long start = _launch.CurrentMap(_pid) ?? 0;
            while (true)
            {
                ThrowIfStopping(ct);
                if (Volatile.Read(ref _modStopPending) == 1 && ReadBool(s, "MODERATOR_STOP", false) && Interlocked.Exchange(ref _modStopPending, 0) == 1)
                {
                    Warn("modération", Loc.T("L.Lua.ModeratorStop"));
                    StopRun(ct, "moderator");
                    ModeratorStopped?.Invoke();
                    ThrowIfStopping(ct);
                }
                if (_launch.IsInFight(_pid) || FightOne(ct, -1, camp: true)) return DynValue.True;
                long cur = _launch.CurrentMap(_pid) ?? 0;
                if (cur == 0 || cur != start) return DynValue.False;
                CampWait(cur, _world.Get(_pid).MapX is int x && _world.Get(_pid).MapY is int y ? $"{x},{y}" : cur.ToString(), false, true, ct);
                SnowPlanningTick(s);                  // compat tiers: PLANNING (heures de déconnexion) même en camp
                InterruptibleDelay(0, ct, pump: true); // Lot G : hooks de paquets / MP servis entre deux tranches (no-op sans hook)
            }
        }));
        // ToZaap(mapId [, sourceType]) : zaap de la carte courante (sourceType 1 = zaapi).
        DynValue toZaap = DynValue.NewCallback((_, a) =>
        {
            long m = L(a, 0);
            return DynValue.NewBoolean(N(a, 1) == 1 ? ZaapiGoto(m, ct) : ZaapGoto(m, ct));
        });
        Fill(map, "ToZaap", toZaap);
        Fill(map, "toZaap", toZaap);

        // ToZaapi(mapId) / toZaapi(mapId) : zaapi intra-ville vers mapId.
        DynValue toZaapi = DynValue.NewCallback((_, a) =>
        {
            long m = L(a, 0);
            return DynValue.NewBoolean(ZaapiGoto(m, ct));
        });
        Fill(map, "ToZaapi", toZaapi);
        Fill(map, "toZaapi", toZaapi);
        if (map.RawGet("idAt") is { Type: DataType.ClrFunction } idAt) { Fill(map, "GetMapIdFromCoords", idAt); Fill(map, "getMapIdFromCoords", idAt); }

        // monsterGroups() -> { {cellId, contextualId, monsters = {{id, level}, ...}}, ... } (chef puis membres, un par monstre).
        Fill(map, "monsterGroups", DynValue.NewCallback((_, a) => BuildList(s, _world.Get(_pid).Monsters, (r, m) =>
        {
            r["cellId"] = DynValue.NewNumber(m.CellId); r["cell"] = DynValue.NewNumber(m.CellId);
            r["contextualId"] = DynValue.NewNumber(m.ActorId); r["id"] = DynValue.NewNumber(m.ActorId);
            IEnumerable<(int Gid, int Level)> members = m.Members is { Count: > 0 } mm
                ? mm.Select(x => (x.GenericId, x.Level))
                : (m.Monsters ?? (IReadOnlyList<MonsterEntry>)Array.Empty<MonsterEntry>()).SelectMany(e => Enumerable.Repeat((e.GenericId, 0), Math.Max(1, e.Count)));
            r["monsters"] = BuildList(s, members, (t, e) =>
            {
                t["id"] = DynValue.NewNumber(e.Gid); t["gid"] = DynValue.NewNumber(e.Gid);
                t["genericId"] = DynValue.NewNumber(e.Gid); t["level"] = DynValue.NewNumber(e.Level);
            });
        })));

        // ---- voyage ----
        var travel = G.Get("travelTo");
        if (travel.Type == DataType.ClrFunction)
        {
            Fill(map, "moveToward", SnowFwd(s, map, travel));
            Fill(map, "gotomap", SnowFwd(s, map, travel));
        }
        long TargetOf(CallbackArguments a, int from)
        {
            var v0 = V(a, from); var v1 = V(a, from + 1);
            if (v0.Type == DataType.Number && v1.Type == DataType.Number)
                return _launch.MapIdAt((int)v0.Number, (int)v1.Number, _launch.CurrentWorldMap(_pid)) ?? 0;
            if (v0.Type == DataType.String && TryParseCoord(v0.String, out int x, out int y))
                return _launch.MapIdAt(x, y, _launch.CurrentWorldMap(_pid)) ?? 0;
            return (long)(v0.CastToNumber() ?? 0);
        }
        bool Reachable(long target)
        {
            long cur = _launch.CurrentMap(_pid) ?? 0;
            if (target <= 0) return false;
            if (cur == target) return true;
            long a2 = _launch.GraphMapForId(cur), b2 = _launch.GraphMapForId(target);
            if (a2 == b2 || _launch.WorldPath((int)a2, (int)b2, null, _pid).Count > 0) return true;
            foreach (long z in _launch.KnownZaapMaps(_pid))   // repli zaap : un zaap connu d'où la cible est joignable à pied
                if (z == target || (_launch.WorldGraphKnows((int)z) && _launch.WorldPath((int)z, (int)b2, null, _pid).Count > 0)) return true;
            return false;
        }
        DynValue LoadMove(CallbackArguments a, int from)
        {
            long t = TargetOf(a, from);
            _roadTarget = Reachable(t) ? t : 0;
            return DynValue.NewBoolean(_roadTarget != 0);
        }
        Fill(map, "loadMove", DynValue.NewCallback((_, a) => LoadMove(a, 0)));
        Fill(map, "loadMoveToMapId", DynValue.NewCallback((_, a) => LoadMove(a, 0)));
        Fill(map, "loadMoveToCoord", DynValue.NewCallback((_, a) => LoadMove(a, 0)));
        // loadRoadToMapId([from,] to) : seule la destination compte (le départ est toujours la carte courante).
        Fill(map, "loadRoadToMapId", DynValue.NewCallback((_, a) => LoadMove(a, V(a, 1).IsNil() ? 0 : 1)));
        DynValue moveNext = DynValue.NewCallback((_, a) =>
        {
            long t = _roadTarget;
            if (t == 0) return DynValue.False;
            if ((_launch.CurrentMap(_pid) ?? 0) == t) { _roadTarget = 0; return DynValue.False; }
            bool ok = TravelToMapId(t, ct);
            if ((_launch.CurrentMap(_pid) ?? 0) == t) _roadTarget = 0;
            return DynValue.NewBoolean(ok);
        });
        Fill(map, "moveNext", moveNext);
        Fill(map, "moveRoadNext", moveNext);
        // moveTowardMap(x, y) / moveTowardMapId(id) : UN changement de carte dans la bonne direction (au hasard parmi les valides).
        DynValue towardMap = DynValue.NewCallback((_, a) =>
        {
            var st = _world.Get(_pid);
            if (st.MapX is not int cx || st.MapY is not int cy) return DynValue.False;
            int tx, ty;
            if (V(a, 1).Type == DataType.Number) { tx = N(a, 0); ty = N(a, 1); }
            else if (_launch.MapCoordOf(L(a, 0)) is { } c) { tx = c.X; ty = c.Y; }
            else return DynValue.False;
            if (tx == cx && ty == cy) return DynValue.True;
            var dirs = new List<string>();
            if (tx > cx) dirs.Add("right"); else if (tx < cx) dirs.Add("left");
            if (ty > cy) dirs.Add("bottom"); else if (ty < cy) dirs.Add("top");
            foreach (var d in dirs.OrderBy(_ => Random.Shared.Next()))
                if (ChangeMap(d, ct)) return DynValue.True;
            return DynValue.False;
        });
        Fill(map, "moveTowardMap", towardMap);
        Fill(map, "moveTowardMapId", towardMap);
        Fill(map, "listenChange", DynValue.NewCallback((_, a) => { _mapChangeMark = (_launch.CurrentMap(_pid) ?? 0, _launch.MapInfoSeq(_pid)); return DynValue.Nil; }));
        Fill(map, "hasChanged", DynValue.NewCallback((_, a) =>
            DynValue.NewBoolean(_mapChangeMark is { } mk && (mk.Map != (_launch.CurrentMap(_pid) ?? 0) || mk.Seq != _launch.MapInfoSeq(_pid)))));

        // ---- zaaps ----
        long NearestZaap(long target, HashSet<long> excluded)
        {
            long cur = _launch.CurrentMap(_pid) ?? 0;
            if (_launch.KnownZaapMaps(_pid).Count == 0 && cur != 0 && OpenGroundZaap(cur, ct)) _launch.LeaveDialog(_pid);   // liste apprise au zaap de la carte
            long best = 0; int bestHops = int.MaxValue;
            foreach (long z in _launch.KnownZaapMaps(_pid))
            {
                if (excluded.Contains(z)) continue;
                if (z == target) { best = z; bestHops = 0; break; }
                if (!_launch.WorldGraphKnows((int)z)) continue;
                int hops = _launch.WorldPath((int)z, (int)_launch.GraphMapForId(target), null, _pid).Count;
                if (hops > 0 && hops < bestHops) { bestHops = hops; best = z; }
            }
            if (best == 0) return 0;
            if (best == cur) return -1;
            _roadTarget = target;
            return best;
        }
        DynValue nearest = DynValue.NewCallback((_, a) =>
        {
            var ex = new HashSet<long>();
            var v = V(a, 1);
            if (v.Type == DataType.Table) foreach (var x in v.Table.Values) if (x.CastToNumber() is double d) ex.Add((long)d);
            return DynValue.NewNumber(NearestZaap(L(a, 0), ex));
        });
        foreach (var n in new[] { "getNearestZaap", "closestzaap", "getNearestZaap2", "closestzaap2", "getNearestZaapbak" }) Fill(map, n, nearest);
        Fill(map, "knownZaap", DynValue.NewCallback((_, a) => BuildNumsL(s, _launch.KnownZaapMaps(_pid))));
        Fill(map, "haveThisZaap", DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.KnownZaapMaps(_pid).Contains(L(a, 0)))));
        // ForbiddIle(zaapMap, destMap) (mapplug.vb) : true = le zaap sert pour la destination. Même zone -> true ; zones
        // différentes -> false si l'une est une île (nom de zone), sinon true (continent -> continent). Zone inconnue -> false.
        Fill(map, "ForbiddIle", DynValue.NewCallback((_, a) =>
        {
            int za = AreaFor(L(a, 0)), zb = AreaFor(L(a, 1));
            if (za < 0 || zb < 0) return DynValue.False;
            return DynValue.NewBoolean(za == zb || (!IsSnowIslandArea(za) && !IsSnowIslandArea(zb)));
        }));

        // ---- archimonstres / joueurs / géométrie ----
        Fill(map, "getArchiMonster", DynValue.NewCallback((_, a) => BuildNums(s,
            _world.Get(_pid).Monsters.SelectMany(m => m.Monsters ?? (IReadOnlyList<MonsterEntry>)Array.Empty<MonsterEntry>())
                .Where(e => ArchMonsters.IsArch(e.GenericId)).Select(e => e.GenericId))));
        Fill(map, "containsArchi", DynValue.NewCallback((_, a) => DynValue.NewBoolean(
            _world.Get(_pid).Monsters.Any(m => m.Monsters != null && m.Monsters.Any(e => ArchMonsters.IsArch(e.GenericId))))));
        Fill(map, "shouldFightArchi", DynValue.NewCallback((_, a) =>
        {
            var group = _world.Get(_pid).Monsters.FirstOrDefault(g => g.Monsters != null && g.Monsters.Any(e => ArchMonsters.IsArch(e.GenericId)));
            if (group?.Monsters is null)
                return DynValue.NewTuple(DynValue.False, DynValue.NewString("Aucun archimonstre sur la carte"));
            var targetArchi = group.Monsters.First(e => ArchMonsters.IsArch(e.GenericId));
            int highestLevel = group.Members is { Count: > 0 } members ? members.Max(m => m.Level) : 0;
            if (highestLevel <= 0)
                return DynValue.NewTuple(DynValue.False, DynValue.NewString("Niveau des monstres du groupe inconnu"));
            var stone = GivreClone.App.Services.Combat.SoulCapture.RequiredStone(highestLevel);
            if (stone is null)
                return DynValue.NewTuple(DynValue.False, DynValue.NewString($"Niveau {highestLevel} hors de portée des pierres d'âme"));
            long stonePrice = _launch.AveragePrice(_pid, stone.GenericId);
            long archiPrice = _launch.AveragePrice(_pid, targetArchi.GenericId);
            double marginKamas = a.Count > 0 && a[0].CastToNumber() is { } mk ? mk : 0;
            if (stonePrice > 0 && archiPrice > 0)
            {
                if (archiPrice < stonePrice + marginKamas)
                    return DynValue.NewTuple(DynValue.False, DynValue.NewString($"Non rentable : Archi ({archiPrice:N0} k) < {stone.Name} ({stonePrice:N0} k)"));
                return DynValue.NewTuple(DynValue.True, DynValue.NewString($"Rentable ! Marge estimée : +{(archiPrice - stonePrice):N0} kamas ({stone.Name})"));
            }
            return DynValue.NewTuple(DynValue.True, DynValue.NewString($"Prix HDV non indexés, {stone.Name} requise (niv max {highestLevel})"));
        }));
        Fill(map, "toZaapi", DynValue.NewCallback((_, a) => DynValue.NewBoolean(ZaapiGoto(L(a, 0), ct))));
        Fill(map, "exitHavenBag", DynValue.NewCallback((_, a) =>
        {
            long cur = _launch.CurrentMap(_pid) ?? 0;
            if (!InHavenBag(cur)) return DynValue.True;
            return DynValue.NewBoolean(ToggleOutOfHavenBag(cur, ct));
        }));
        Fill(map, "useHavenBagZaap", DynValue.NewCallback((_, a) =>
        {
            long destMap = L(a, 0);
            if (destMap <= 0 || _launch.IsInFight(_pid)) return DynValue.False;
            long startMap = _launch.CurrentMap(_pid) ?? 0;
            if (startMap == destMap) return DynValue.True;
            if (!InHavenBag(startMap))
            {
                if (!_launch.EnterHavenBag(_pid)) return DynValue.False;
                if (!WaitUntil(() => InHavenBag(_launch.CurrentMap(_pid) ?? 0), MapChangeAttemptTimeoutMs, ct)) return DynValue.False;
            }
            return DynValue.NewBoolean(ZaapGoto(destMap, ct));
        }));
        Fill(map, "Archimonster", BuildNums(s, ArchMonsters.Ids.OrderBy(x => x)));
        Fill(map, "players", DynValue.NewCallback((_, a) => BuildList(s, _world.Get(_pid).Actors.Where(x => x.Kind == MapActorKind.Player), (r, x) =>
        {
            r["contextualId"] = DynValue.NewNumber(x.ActorId); r["id"] = DynValue.NewNumber(x.ActorId);
            r["name"] = DynValue.NewString(x.Name ?? "");
            r["cellId"] = DynValue.NewNumber(x.CellId); r["cell"] = DynValue.NewNumber(x.CellId);
            var disp = new Table(s); disp["cellId"] = DynValue.NewNumber(x.CellId); disp["direction"] = DynValue.NewNumber(x.Direction);
            r["disposition"] = DynValue.NewTable(disp);
        })));
        if (map.RawGet("cellDistance") is { Type: DataType.ClrFunction } cd) Fill(map, "getDistance", cd);
        Fill(map, "getAdjacentCells", DynValue.NewCallback((_, a) =>
        {
            int c = N(a, 0);
            if (c < 0) return DynValue.Nil;
            var (x, y) = MapPathfinder.MapPointOf(c);
            return BuildNums(s, new[] { MapPathfinder.CellFromMapPoint(x - 1, y), MapPathfinder.CellFromMapPoint(x + 1, y),
                                        MapPathfinder.CellFromMapPoint(x, y - 1), MapPathfinder.CellFromMapPoint(x, y + 1) }.Where(v => v >= 0));
        }));
        Fill(map, "isHandToHand", DynValue.NewCallback((_, a) => DynValue.NewBoolean(MapPathfinder.CellDistance(N(a, 0), N(a, 1)) <= 1)));
        Fill(map, "checkMonsterAmountConstraints", DynValue.NewCallback((_, a) =>
        {
            // Identifiant d'acteur du groupe, jamais une case ou un identifiant de monstre.
            long actor = L(a, 0);
            var group = _world.Get(_pid).Monsters.FirstOrDefault(g => g.ActorId == actor);
            if (group?.Monsters is not { } monsters) return DynValue.Nil;
            bool allowed = MonsterAmountsAllowed(monsters, ReadMonsterAmounts());
            if (allowed && _monsterAmountConstraints.TryGetValue(_pid, out var bounds))
            {
                int totalCount = monsters.Sum(m => m.Count);
                if (totalCount < bounds.Min || totalCount > bounds.Max) allowed = false;
            }
            return DynValue.NewBoolean(allowed);
        }));
        Fill(map, "hasNpc", DynValue.NewCallback((_, a) =>
        {
            long id = L(a, 0);
            return DynValue.NewBoolean(_world.Get(_pid).Actors.Where(x => x.Kind == MapActorKind.Npc).Any(x => x.NpcId == id || x.ActorId == id));
        }));
        Fill(map, "hasMonster", DynValue.NewCallback((_, a) =>
        {
            long monsterId = L(a, 0);
            return DynValue.NewBoolean(_world.Get(_pid).Monsters.SelectMany(m => m.Monsters ?? (IReadOnlyList<MonsterEntry>)Array.Empty<MonsterEntry>()).Any(e => e.GenericId == monsterId));
        }));
        Fill(map, "getCurrentMap", DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.CurrentMap(_pid) ?? 0)));
        Fill(map, "getMapId", DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.CurrentMap(_pid) ?? 0)));
        Fill(map, "setMonsterAmountConstraint", DynValue.NewCallback((_, a) =>
        {
            int min = N(a, 0), max = N(a, 1);
            _monsterAmountConstraints[_pid] = (min, max);
            return DynValue.Nil;
        }));
        Fill(map, "Precompute", DynValue.False);
        // getMap: résumé en lecture seule.
        Fill(map, "getMap", DynValue.NewCallback((_, a) =>
        {
            var st = _world.Get(_pid);
            var t = new Table(s);
            t["mapId"] = DynValue.NewNumber(_launch.CurrentMap(_pid) ?? 0);
            t["subAreaId"] = DynValue.NewNumber(_launch.CurrentSubArea(_pid));
            t["x"] = st.MapX is int x ? DynValue.NewNumber(x) : DynValue.Nil;
            t["y"] = st.MapY is int y ? DynValue.NewNumber(y) : DynValue.Nil;
            t["actors"] = BuildList(s, st.Actors, (r, x) =>
            {
                r["contextualId"] = DynValue.NewNumber(x.ActorId); r["cellId"] = DynValue.NewNumber(x.CellId);
                r["name"] = DynValue.NewString(x.Name ?? ""); r["kind"] = DynValue.NewString(x.Kind.ToString().ToLowerInvariant());
                r["npcId"] = DynValue.NewNumber(x.NpcId);   // id de MODÈLE (PNJ / indice de chasse) ; 0 pour les autres acteurs
            });
            t["monsters"] = BuildList(s, st.Monsters, (r, m) => { r["contextualId"] = DynValue.NewNumber(m.ActorId); r["cellId"] = DynValue.NewNumber(m.CellId); });
            t["interactives"] = BuildList(s, st.Interactives, (r, i) =>
            {
                r["elementId"] = DynValue.NewNumber(i.ElementId); r["cellId"] = DynValue.NewNumber(i.CellId);
                r["elementTypeId"] = DynValue.NewNumber(i.ElementTypeId); r["name"] = DynValue.NewString(i.Name ?? "");
            });
            return DynValue.NewTable(t);
        }));
        Fill(map, "aggression", UnsupportedLua("map:aggression"));
    }

    // Voyage bloquant vers une carte (même chemin que travelTo).
    private bool TravelToMapId(long target, CancellationToken ct) => target > 0 && GoToMapId(target, ct);

    // Zone « île » au sens d'un bot tiers ForbiddIle: NOM de zone contenant Île / Saharach / Archipel / Nimotopia (mots FR de
    // Un bot tiers + équivalents EN/ES, la langue des noms suit l'I18n chargé). Nom inconnu -> pas une île.
    private static readonly string[] SnowIslandWords = { "Île", "Isle", "Isla", "Saharach", "Archipel", "Archipiélago", "Nimotopia" };
    private bool IsSnowIslandArea(int area)
    {
        string? n = _launch.GameNames.AreaName(area);
        return n is { Length: > 0 } && SnowIslandWords.Any(w => n.Contains(w, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ tables de combat un bot tiers
    private void BindSnowFight(Script s, CancellationToken ct)
    {
        var combat = GetOrCreateTable(s, "combat");
        var fa = GetOrCreateTable(s, "fightAction");
        int NA(CallbackArguments a, int i) => (int)(At(a, fa, i).CastToNumber() ?? 0);
        int SelfCell() { int c = _launch.FightSelfCell(_pid); return c >= 0 ? c : _world.Get(_pid).SelfCell ?? -1; }
        IReadOnlyList<LaunchService.ScriptFighter> Fighters() => _launch.FightersDetail(_pid);

        // Marchabilité STATIQUE de la carte (relief seul, SANS l'occupation des combattants Cells(c).Mov /
        // checkmap.IsWalkable) : en combat, géométrie de combat (Mov && !NonWalkableDuringFight) ; hors combat, Mov.
        // null = géométrie de la carte pas encore chargée.
        Func<int, bool>? StaticWalk()
        {
            var st = _world.Get(_pid);
            var cells = st.Cells;
            if (cells.Count == 0) return null;
            if (!_launch.IsInFight(_pid)) return st.IsWalkable;
            var w = new bool[MapPathfinder.CellCount];
            foreach (var c in cells) if ((uint)c.CellId < (uint)w.Length) w[c.CellId] = c.Mov && !c.NonWalkableDuringFight;
            return c => (uint)c < (uint)w.Length && w[c];
        }

        // ---- fightAction : verbes manquants (FightManager.vb) ----
        // getCells_*: (Zones\Square/Cross/Lozenge.vb, method_0), seules les cases MARCHABLES de la carte sont
        // rendues (combattants NON exclus : « ennemis dans mon losange » reste possible). Le centre est ajouté sans test de relief
        // quand min = 0 pour la croix, et pour un rayon nul (carré / losange / diagonale). Géométrie inconnue -> forme pure.
        DynValue Shape(char kind) => DynValue.NewCallback((_, a) =>
        {
            int center = NA(a, 0), min = NA(a, 1), radius = NA(a, 2);
            var cells = CombatZones.SimpleShape(kind, center, min, radius);
            if (StaticWalk() is not { } walk) return BuildNums(s, cells);
            bool keepCenter = min == 0 && (kind == 'X' || radius == 0);
            return BuildNums(s, cells.Where(c => walk(c) || (keepCenter && c == center)));
        });
        Fill(fa, "getCells_square", Shape('S'));
        Fill(fa, "getCells_cross", Shape('X'));
        Fill(fa, "getCells_lozenge", Shape('C'));
        Fill(fa, "getCells_diagonal", Shape('D'));
        Fill(fa, "cellsAligned", DynValue.NewCallback((_, a) => DynValue.NewBoolean(CombatZones.Aligned(NA(a, 0), NA(a, 1)))));
        Fill(fa, "cellsInDiagonal", DynValue.NewCallback((_, a) => DynValue.NewBoolean(CombatZones.InDiagonal(NA(a, 0), NA(a, 1)))));
        // GetShortestPath(from, to [, obstacles]) -> cellules du chemin de combat.
        Fill(fa, "GetShortestPath", DynValue.NewCallback((_, a) => BuildNums(s, _launch.FightPath(_pid, NA(a, 0), NA(a, 1)))));
        Fill(fa, "getCurrentMapId", DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.CurrentMap(_pid) ?? 0)));
        int Nearest(bool enemy)
        {
            int me = SelfCell();
            if (me < 0) return -1;
            var cand = Fighters().Where(f => f.Alive && !f.IsSelf && f.IsEnemy == enemy).ToList();
            return cand.Count == 0 ? -1 : cand.OrderBy(f => MapPathfinder.CellDistance(me, f.Cell)).First().Cell;
        }
        Fill(fa, "getNearestEnemy", DynValue.NewCallback((_, a) => DynValue.NewNumber(Nearest(true))));
        Fill(fa, "getNearestAlly", DynValue.NewCallback((_, a) => DynValue.NewNumber(Nearest(false))));
        Fill(fa, "getRealReachableCells", DynValue.NewCallback((_, a) => BuildNums(s, _launch.CombatReachable(_pid))));
        // isWalkable(cell) : relief seul (FightManager.isWalkable = checkmap.IsWalkable) ; une case OCCUPÉE reste marchable —
        // l'occupation relève de isFreeCell.
        Fill(fa, "isWalkable", DynValue.NewCallback((_, a) => DynValue.NewBoolean(StaticWalk() is { } walk && walk(NA(a, 0)))));
        Fill(fa, "getAdjacentCells", DynValue.NewCallback((_, a) =>
        {
            var (x, y) = MapPathfinder.MapPointOf(NA(a, 0));
            return BuildNums(s, new[] { MapPathfinder.CellFromMapPoint(x - 1, y), MapPathfinder.CellFromMapPoint(x + 1, y),
                                        MapPathfinder.CellFromMapPoint(x, y - 1), MapPathfinder.CellFromMapPoint(x, y + 1) }.Where(v => v >= 0));
        }));
        // getFighter(cell) -> id du combattant sur la case, -1 si aucun ou hors combat (FightManager.getFighter2).
        // ScriptFighter est un struct : FirstOrDefault rendrait l'id 0 -> projection nullable.
        Fill(fa, "getFighter", DynValue.NewCallback((_, a) =>
        {
            int c = NA(a, 0);
            if (!_launch.IsInFight(_pid)) return DynValue.NewNumber(-1);
            long? hit = Fighters().Where(f => f.Alive && f.Cell == c).Select(f => (long?)f.ActorId).FirstOrDefault();
            return DynValue.NewNumber(hit ?? -1);
        }));
        // getAllEntities: champs tiers (PlugCombat) + clés GivreClone. ⚠ Team un bot tiers = true pour un ENNEMI (PlugCombat
        // New(Ennemi) ; getNearestEnemy filtre Team = True), false pour moi et mes alliés. CreatureGenericId / Level : 0 = inconnu
        // (valeurs par défaut de PlugCombat ; non exposés par le moteur).
        Fill(fa, "getAllEntities", DynValue.NewCallback((_, a) => BuildList(s, Fighters().Where(f => f.Alive), (r, f) =>
        {
            r["Id"] = DynValue.NewNumber(f.ActorId); r["id"] = DynValue.NewNumber(f.ActorId);
            r["CellId"] = DynValue.NewNumber(f.Cell); r["cellId"] = DynValue.NewNumber(f.Cell); r["cell"] = DynValue.NewNumber(f.Cell);
            r["LifePoints"] = DynValue.NewNumber(f.Life); r["lifePoints"] = DynValue.NewNumber(f.Life);
            r["MaxLifePoints"] = DynValue.NewNumber(f.MaxLife); r["maxLifePoints"] = DynValue.NewNumber(f.MaxLife);
            r["AP"] = DynValue.NewNumber(f.Pa); r["MP"] = DynValue.NewNumber(f.Pm);
            r["CreatureGenericId"] = DynValue.NewNumber(0); r["Level"] = DynValue.NewNumber(0);
            r["Team"] = DynValue.NewBoolean(f.IsEnemy && !f.IsSelf); r["isEnemy"] = DynValue.NewBoolean(f.IsEnemy);
            r["Mycharacter"] = DynValue.NewBoolean(f.IsSelf); r["isSummon"] = DynValue.NewBoolean(f.IsSummon);
            r["Companion"] = DynValue.NewBoolean(f.IsMySummon);
        })));
        Fill(fa, "getEntitiesCount", DynValue.NewCallback((_, a) => DynValue.NewNumber(Fighters().Count(f => f.Alive))));
        Fill(fa, "getWeakestEnemyId", DynValue.NewCallback((_, a) =>
        {
            if (!_launch.IsInFight(_pid)) return DynValue.Nil;
            long? id = Fighters().Where(f => f.IsEnemy && f.Alive && !f.IsSummon).OrderBy(f => f.Life).Select(f => (long?)f.ActorId).FirstOrDefault();
            return id is long v ? DynValue.NewNumber(v) : DynValue.Nil;
        }));
        Fill(fa, "getWeakestEnemy", fa.RawGet("getWeakestEnemyId") ?? DynValue.Nil);
        Fill(fa, "getEnemies", DynValue.NewCallback((_, a) =>
            !_launch.IsInFight(_pid) ? DynValue.NewTable(new Table(s)) : BuildNumsL(s, Fighters().Where(f => f.IsEnemy && f.Alive).Select(f => f.ActorId))));
        Fill(fa, "getEnemyCell", DynValue.NewCallback((_, a) =>
        {
            if (!_launch.IsInFight(_pid)) return DynValue.NewNumber(-1);
            long tid = (long)(At(a, fa, 0).CastToNumber() ?? 0);
            int? cell = Fighters().Where(f => f.Alive && f.ActorId == tid).Select(f => (int?)f.Cell).FirstOrDefault();
            return DynValue.NewNumber(cell ?? -1);
        }));
        Fill(fa, "getSpellApCost", DynValue.NewCallback((_, a) =>
        {
            int sid = NA(a, 0);
            return DynValue.NewNumber(_launch.GameNames.SpellCombat(sid, _launch.LearnedSpellLevel(_pid, sid))?.Ap ?? 0);
        }));
        Fill(fa, "isCellOccupied", DynValue.NewCallback((_, a) =>
        {
            int c = NA(a, 0);
            return DynValue.NewBoolean(_launch.IsInFight(_pid) && Fighters().Any(f => f.Alive && f.Cell == c));
        }));
        Fill(fa, "getMovementCostToCell", DynValue.NewCallback((_, a) =>
        {
            if (!_launch.IsInFight(_pid)) return DynValue.NewNumber(-1);
            int me = SelfCell(), dest = NA(a, 0);
            if (me < 0 || dest < 0) return DynValue.NewNumber(-1);
            if (me == dest) return DynValue.NewNumber(0);
            var path = _launch.FightPath(_pid, me, dest);
            return DynValue.NewNumber(path.Count > 0 ? path.Count : -1);
        }));
        Fill(fa, "getSafeMovementCells", DynValue.NewCallback((_, a) =>
        {
            if (!_launch.IsInFight(_pid)) return DynValue.NewTable(new Table(s));
            var enemies = Fighters().Where(f => f.IsEnemy && f.Alive).Select(f => f.Cell).ToList();
            var reach = _launch.CombatReachable(_pid);
            return BuildNums(s, enemies.Count == 0 ? reach : reach.Where(c => enemies.All(e => MapPathfinder.CellDistance(c, e) >= 2)));
        }));
        Fill(fa, "getTacticalCells", DynValue.NewCallback((_, a) =>
        {
            if (!_launch.IsInFight(_pid)) return DynValue.NewTable(new Table(s));
            int minD = NA(a, 0), maxD = NA(a, 1);
            var enemies = Fighters().Where(f => f.IsEnemy && f.Alive).Select(f => f.Cell).ToList();
            if (enemies.Count == 0) return DynValue.NewTable(new Table(s));
            var reach = _launch.CombatReachable(_pid);
            return BuildNums(s, reach.Where(c => { int d = enemies.Min(e => MapPathfinder.CellDistance(c, e)); return d >= minD && d <= maxD; }));
        }));
        // forceCellidEntity : cache piloté par le serveur, modification locale refusée explicitement.
        Fill(fa, "forceCellidEntity", UnsupportedLua("fightAction:forceCellidEntity", "Le cache des positions est piloté par le serveur"));

        // ---- fightCharacter (characterAI.vb) ----
        var fc = GetOrCreateTable(s, "fightCharacter");
        int Forced(int key, int live)
        {
            long turn = _launch.CurrentFightId(_pid) * 1000 + _launch.FightTurnNo(_pid);
            if (turn != _fightForceTurn) { _fightForce.Clear(); _fightForceTurn = turn; }
            return _fightForce.TryGetValue(key, out var v) ? v : live;
        }
        void Force(int key, CallbackArguments a)
        {
            long turn = _launch.CurrentFightId(_pid) * 1000 + _launch.FightTurnNo(_pid);
            if (turn != _fightForceTurn) { _fightForce.Clear(); _fightForceTurn = turn; }
            _fightForce[key] = (int)(At(a, fc, 0).CastToNumber() ?? 0);
        }
        Fill(fc, "getAP", DynValue.NewCallback((_, a) => DynValue.NewNumber(Forced(0, _launch.ScriptPointsLeft(_pid).Item1))));
        Fill(fc, "getMP", DynValue.NewCallback((_, a) => DynValue.NewNumber(Forced(1, _launch.ScriptPointsLeft(_pid).Item2))));
        Fill(fc, "getPO", DynValue.NewCallback((_, a) => DynValue.NewNumber(Forced(2, _launch.SelfRange(_pid)))));
        Fill(fc, "getRange", DynValue.NewCallback((_, a) => DynValue.NewNumber(Forced(2, _launch.SelfRange(_pid)))));
        Fill(fc, "forceAP", DynValue.NewCallback((_, a) => { Force(0, a); return DynValue.Nil; }));
        Fill(fc, "forceMP", DynValue.NewCallback((_, a) => { Force(1, a); return DynValue.Nil; }));
        Fill(fc, "forcePO", DynValue.NewCallback((_, a) => { Force(2, a); return DynValue.Nil; }));
        (int Cur, int Max) Life() => _launch.SelfLife(_pid) is { } l ? (l.Cur, l.Max) : (_launch.SelfStats(_pid)?.Life ?? 0, _launch.SelfStats(_pid)?.MaxLife ?? 0);
        Fill(fc, "getLifePoints", DynValue.NewCallback((_, a) => DynValue.NewNumber(Life().Cur)));
        Fill(fc, "getLifePointsMax", DynValue.NewCallback((_, a) => DynValue.NewNumber(Life().Max)));
        Fill(fc, "getLifePointsP", DynValue.NewCallback((_, a) => { var l = Life(); return DynValue.NewNumber(l.Max > 0 ? l.Cur * 100 / l.Max : 0); }));
        Fill(fc, "getId", DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.CharacterId(_pid))));
        Fill(fc, "getCellId", DynValue.NewCallback((_, a) => DynValue.NewNumber(SelfCell())));
        Fill(fc, "getLevel", DynValue.NewCallback((_, a) => DynValue.NewNumber(_world.Get(_pid).Level ?? 0)));
        Fill(fc, "isMule", DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.TeamViewOf(_pid) is not null && !_launch.IsTeamLeader(_pid))));
        Fill(fc, "getBreed", DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.SelfCharacterInfo(_pid)?.BreedIndex is int bi ? ClassIconProvider.BreedId(bi) : 0)));
        Fill(fc, "isItMyTurn", DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.IsMyTurnOpen(_pid))));
        Fill(fc, "hasState", DynValue.NewCallback((_, a) =>
        {
            int stateId = (int)(At(a, fc, 0).CastToNumber() ?? 0);
            return DynValue.NewBoolean(_launch.FighterHasState(_pid, _launch.CharacterId(_pid), stateId));
        }));
        Fill(fc, "getActiveStateIds", DynValue.NewCallback((_, a) =>
            BuildNums(s, _launch.FighterActiveStates(_pid, _launch.CharacterId(_pid)))));

        // ---- fightDebug (debugAI.vb) ----
        var fd = GetOrCreateTable(s, "fightDebug");
        Fill(fd, "print", DynValue.NewCallback((_, a) => { Info("combat", At(a, fd, 0).CastToString() ?? ""); return DynValue.Nil; }));
        Fill(fd, "delay", DynValue.NewCallback((_, a) => { InterruptibleDelay(Math.Clamp((int)(At(a, fd, 0).CastToNumber() ?? 0), 0, 60000), ct); return DynValue.Nil; }));

        // ---- fightBasic (BasicAI.vb) : rotation posée par le script + tour joué par l'IA de GivreClone ----
        var fb = GetOrCreateTable(s, "fightBasic");
        Fill(fb, "setFightSpeed", DynValue.NewCallback((_, a) =>
        {
            var speed = ParseLuaFightSpeed(At(a, fb, 0));
            ct.ThrowIfCancellationRequested();
            _launch.ScriptSetCombat(_pid, o => o.Speed = speed);
            return DynValue.Nil;
        }));
        Fill(fb, "setApproachDistance", DynValue.NewCallback((_, a) =>
        {
            // Distance exacte, dans CE callback seulement : min = max. Les champs kite de l'UI ne sont pas couplés.
            int d = (int)(At(a, fb, 0).CastToNumber() ?? 0);
            _launch.ScriptSetCombat(_pid, o => o.HoldExactApproachDistance(d));
            return DynValue.Nil;
        }));
        Fill(fb, "clearSpells", DynValue.NewCallback((_, a) => { _launch.ScriptSetCombat(_pid, o => o.Spells = new List<SpellConfig>()); return DynValue.Nil; }));
        // addSpell(spellId, cible, tours, relances, corpsACorps, pvCible%) — cible = TeamEnum un bot tiers (TeamEnum.vb): 0 ennemi,
        // 1 allié, 2 soi, 3/4/5 case LIBRE adjacente à l'ennemi / l'allié / moi, 6 invocation ennemie, 7 ennemi non invoqué,
        // 8 invocation alliée, 9 allié non invoqué, 10 compagnon, 11-15 = case libre adjacente à 6/7/8/9/10. Le moteur ne
        // distingue pas invocations / compagnons -> ennemi ou allié. tours = relance tous les N tours ; relances = lancers par tour.
        // Cibles « case libre adjacente » (GetAdjacentCellNearestFighter : invocation, piège…) : sort à case libre (needFreeCell,
        // ou inconnu pour 5) -> IsSummon (le moteur choisit une case LIBRE à portée ; 5 = collée à moi) ; sinon, approximation
        // sur le combattant lui-même (5 -> sur moi), sans plafond de portée à 1.
        Fill(fb, "addSpell", DynValue.NewCallback((_, a) =>
        {
            int id = (int)(At(a, fb, 0).CastToNumber() ?? 0);
            if (id <= 0) return DynValue.Nil;
            int target = (int)(At(a, fb, 1).CastToNumber() ?? 0), turns = (int)(At(a, fb, 2).CastToNumber() ?? 1);
            int relaunch = (int)(At(a, fb, 3).CastToNumber() ?? 1);
            bool cac = At(a, fb, 4).CastToBool();
            int life = (int)(At(a, fb, 5).CastToNumber() ?? 100);
            bool adjacent = target is 3 or 4 or 5 or (>= 11 and <= 15);
            bool ally = target is 1 or 4 or 8 or 9 or 10 or 13 or 14 or 15;
            bool? needFree = _launch.GameNames.SpellCastConstraints(id, _launch.LearnedSpellLevel(_pid, id))?.NeedFreeCell;
            bool summon = adjacent && (needFree ?? (target == 5));
            bool onMe = target == 2 || (target == 5 && !summon);
            var sc = new SpellConfig
            {
                SpellId = id, PerTurn = Math.Clamp(relaunch, 1, 20), Cooldown = Math.Max(0, turns - 1),
                OnMe = onMe, TargetKind = ally && !summon ? SpellTargetKind.Ally : SpellTargetKind.Enemy,   // case libre -> branche invocation (la branche Allié, prioritaire, refuserait une case libre)
                SelfBuff = onMe, IsSummon = summon,
                TgtHpMax = life is > 0 and < 100 ? life : 100,
                CastRangeMax = cac || (summon && target == 5) ? 1 : 0,
                AllowApproach = true,
            };
            // Sans clearSpells préalable, on AJOUTE à la rotation courante (onglet Combat) sur sa liste de sorts.
            var current = _launch.EffectiveCombatProfile(_pid)?.Spells ?? new List<SpellConfig>();
            _launch.ScriptSetCombat(_pid, o =>
            {
                o.Spells ??= current;
                sc.Priority = o.Spells.Count;
                o.Spells.Add(sc);
            });
            return DynValue.Nil;
        }));
        // playTurn(tactique) : 0 immobile (passif), 1 corps à corps (agressif), 2 distance (fuyard), 3 aucune action (fin de tour).
        Fill(fb, "playTurn", DynValue.NewCallback((_, a) =>
        {
            int tactic = (int)(At(a, fb, 0).CastToNumber() ?? 5);
            if (tactic == 3) return DynValue.NewBoolean(_launch.EndTurn(_pid, HookEpoch));
            CombatStyle? st = tactic switch { 0 => CombatStyle.Passive, 1 => CombatStyle.Aggressive, 2 => CombatStyle.Fleeing, _ => null };
            if (st is { } sty) _launch.ScriptSetCombat(_pid, o => o.Style = sty);
            bool ok = _launch.PlayTurnForScriptAsync(_pid, HookEpoch, ct).GetAwaiter().GetResult();
            if (!ok) Warn("combat", Loc.T("L.Lua.PlayTurnRefused"));
            return DynValue.NewBoolean(ok);
        }));

        // ---- fightSlave : le moteur décode les invocations mais ne choisit/commande pas une invocation contrôlée. ----
        var fsl = GetOrCreateTable(s, "fightSlave");
        Fill(fsl, "status", DynValue.NewCallback((_, a) =>
        {
            var t = SnowStatusTable(s, false, "controlled_summon_lua_context_unavailable");
            // Pilotage automatique moteur disponible ; aucune facade publique de contexte controle pour Lua.
            // Ce compteur lit le roster, il ne prouve pas une autorisation de controle.
            t["observedOwnSummons"] = DynValue.NewNumber(_launch.FightersDetail(_pid).Count(f => f.Alive && f.IsMySummon));
            return DynValue.NewTable(t);
        }));
        DynValue Neutral(string fn) => UnsupportedLua("fightSlave:" + fn);
        Fill(fsl, "entity", Neutral("entity"));
        Fill(fsl, "name", Neutral("name"));
        foreach (var n in new[] { "index", "creatureId", "cellId", "getNearestEnemy", "getNearestAlly", "canCastSpellOnCell", "canCastSpellOnCellAfterMove" })
            Fill(fsl, n, Neutral(n));
        foreach (var n in new[] { "getReachableCells", "getRealReachableCells", "showSpells" })
            Fill(fsl, n, UnsupportedLua("fightSlave:" + n));
        foreach (var n in new[] { "isCellReachable", "castSpellOnCell", "moveTowardCell" }) Fill(fsl, n, Neutral(n));
        Fill(fsl, "InvalidateCache", UnsupportedLua("fightSlave:InvalidateCache"));

        // fightChallenge:getLastChallengeProposalMessage() -> propositions de défi courantes (combat:challengeProposals).
        var fch = GetOrCreateTable(s, "fightChallenge");
        if (combat.RawGet("challengeProposals") is { Type: DataType.ClrFunction } cp) Fill(fch, "getLastChallengeProposalMessage", SnowFwd(s, fch, cp));
    }
}
