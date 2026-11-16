using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ==================================================================================================================
// Lot J — coffres et maisons à code : map:lockedStorage / map:lockedHouse (bloquants), map:houses / map:isInHouse
// (lectures) + clés de route lockedStorage / lockedHouse (move() et bank()). Verbes liés AVANT DoString (BindModules ->
// BindMapLotJ). Callbacks AOT-safe (DynValue.NewCallback / NewTuple), appel colon ET point. Exécuté UNIQUEMENT sur le thread
// du script ; la séquence réseau vit dans LaunchService (LockedStorageOpenAsync / LockedHouseEnterAsync), appelée via
// GetResult. Le CODE n'apparaît JAMAIS dans la console Lua ni dans le journal transport.
// ==================================================================================================================
public sealed partial class GivreLuaHost
{
    private const int MaxLockFails = 5;
    private int _lockFails;

    // Réponse d'un verbe de verrou : ok, jeton de raison (stable, pour les scripts), message localisé (déjà formaté).
    private readonly record struct LockResult(bool Ok, string Reason, string Message);

    private void BindMapLotJ(Script s, Table map, CancellationToken ct)
    {
        static DynValue Tup(LockResult r) => DynValue.NewTuple(DynValue.NewBoolean(r.Ok), DynValue.NewString(r.Reason));
        // lockedStorage(cell, code [, skillId]) -> ok, reason. Le coffre RESTE OUVERT (putAllItems / depositItem / getAllItems
        // puis leaveDialog). code = CHAÎNE ("-1" ou "" = pas de code).
        map["lockedStorage"] = DynValue.NewCallback((_, a) => Tup(LockedStorage(ArgN(a, 0), ArgS(a, 1), ArgN(a, 2), ct, log: true)));
        // lockedHouse(cell, code [, owner [, skillId]]) -> ok, reason. owner = "Pseudo" ou "Pseudo#1234" (instance de maison).
        map["lockedHouse"] = DynValue.NewCallback((_, a) => Tup(LockedHouse(ArgN(a, 0), ArgS(a, 1), ArgS(a, 2), ArgN(a, 3), ct, log: true)));
        map["isInHouse"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.InHouse(_pid)));
        map["houses"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.MapHouses(_pid), (r, h) =>
        {
            r["houseId"] = DynValue.NewNumber(h.HouseId);
            var d = new Table(s); int i = 1;
            foreach (var e in h.DoorElementIds) d.Set(i++, DynValue.NewNumber(e));
            r["doors"] = DynValue.NewTable(d);
            r["instances"] = BuildList(s, h.Instances, (ri, x) =>
            {
                ri["id"] = DynValue.NewNumber(x.InstanceId); ri["owner"] = DynValue.NewString(x.Nickname);
                ri["tag"] = DynValue.NewString(x.Tag); ri["locked"] = DynValue.NewBoolean(x.IsLocked);
            });
        }));
    }

    // ------------------------------------------------------------------ résolution de la cible
    // Interactif À la cellule, sinon celui dont la cellule mapData == cellule, sinon (l'utilisateur a noté la case où il se
    // TENAIT) l'UNIQUE interactif dont la cellule est voisine (8-voisinage) — ambigu (plusieurs voisins) -> null. Attend
    // d'abord le décodage asynchrone des interactifs de la carte courante (comme la récolte).
    private MapInteractive? ResolveLockTarget(int cell, CancellationToken ct)
    {
        long map = _launch.CurrentMap(_pid) ?? 0;
        if (map != 0) WaitUntil(() => _launch.InteractivesDecodedMap(_pid) == map, InteractivesDecodeTimeoutMs, ct);
        var st = _world.Get(_pid);
        var hit = st.InteractiveAt(cell)
            ?? (map != 0 ? st.Interactives.FirstOrDefault(i => _launch.InteractionCell(map, i.ElementId) == cell) : null);
        if (hit is not null) return hit;
        var near = st.Interactives.Where(i => MapPathfinder.InteractionAdjacent(i.CellId, cell)).Take(2).ToList();
        return near.Count == 1 ? near[0] : null;
    }

    // Skill: sel == 0 -> skill par défaut;
    // sel > 0 -> uid d'instance puis skillId ; sel < 0 -> N-ième actif (même règle que map:useById). null -> liste des
    // skills ACTIFS dans la console pour aider l'utilisateur à choisir.
    private InteractiveSkill? PickLockSkill(MapInteractive inter, int sel, bool log)
    {
        var sk = inter.PickSkill(sel);
        if (sk is null && log)
        {
            var list = inter.Skills.Where(x => x.Enabled)
                .Select(x => _launch.GameNames.SkillName(x.SkillId) is { Length: > 0 } n ? $"{x.SkillId}({n})" : x.SkillId.ToString(CultureInfo.InvariantCulture));
            string txt = string.Join(", ", list);
            Warn("verrou", Loc.Fmt("L.Lua.LockSkills", txt.Length > 0 ? txt : "-"));
        }
        return sk;
    }

    // Marche jusqu'à une voisine de l'élément (déjà voisin -> sur place ; sinon la voisine au chemin le plus court, à égalité orthogonale puis diagonale) et ATTEND l'arrivée — même logique
    // que la primitive map:useById (Lot B).
    private bool ApproachInteractive(MapInteractive inter, InteractiveSkill skill, CancellationToken ct)
    {
        CloseHdvBeforeMove(ct);
        WaitSelfReady(ct);
        long map = _launch.CurrentMap(_pid) ?? 0;
        var st = _world.Get(_pid);
        if (map == 0 || st.SelfCell is not int src) return false;
        if (_launch.InteractiveStandCell(_pid, map, src, inter, skill, st.IsWalkable, st.StepAllowed, out var standWalk) is not int stand) return false;
        if (stand == src) return true;
        var mv = _launch.SendAndWaitAsync(_pid, () => _launch.MoveTo(_pid, map, src, stand, standWalk, true, st.StepAllowed), MoveTimeoutMs, ct)
                        .GetAwaiter().GetResult();
        ThrowIfStopping(ct);
        return mv == LaunchService.MoveOutcome.Arrived;
    }

    // ------------------------------------------------------------------ verbes
    private LockResult LockFailure(string reason, string msg, bool log)
    {
        if (log) Warn("verrou", msg);
        return new LockResult(false, reason, msg);
    }

    private LockResult FromOutcome(LaunchService.LockOutcome o, bool log)
    {
        var r = o switch
        {
            LaunchService.LockOutcome.Opened => new LockResult(true, "opened", Loc.T("L.Lua.LockStorageOpened")),
            LaunchService.LockOutcome.Entered => new LockResult(true, "entered", Loc.Fmt("L.Lua.LockHouseEntered", _launch.CurrentMap(_pid) ?? 0)),
            LaunchService.LockOutcome.WrongCode => new LockResult(false, "wrong_code", Loc.T("L.Lua.LockWrongCode")),
            LaunchService.LockOutcome.Forbidden => new LockResult(false, "forbidden", Loc.T("L.Lua.LockForbidden")),
            LaunchService.LockOutcome.Busy => new LockResult(false, "busy", Loc.T("L.Lua.LockBusy")),
            LaunchService.LockOutcome.CodeRequired => new LockResult(false, "code_required", Loc.T("L.Lua.LockCodeRequired")),
            LaunchService.LockOutcome.Denied => new LockResult(false, "denied", Loc.T("L.Lua.NoPermGather")),
            LaunchService.LockOutcome.ForgeFailed => new LockResult(false, "unsupported", Loc.T("L.Lua.LockTimeout")),
            _ => new LockResult(false, "timeout", Loc.T("L.Lua.LockTimeout")),
        };
        if (log) { if (r.Ok) Ok("verrou", r.Message); else Warn("verrou", r.Message); }
        return r;
    }

    // Préambule commun : combat / permission / code / cible / skill / approche. null = tout est prêt.
    private LockResult? LockPrelude(int cell, string rawCode, int skillSel, bool log, CancellationToken ct,
                                    out string? code, out MapInteractive? inter, out InteractiveSkill? skill)
    {
        code = null; inter = null; skill = null;
        ThrowIfStopping(ct);
        if (_launch.IsInFight(_pid)) return LockFailure("fight", Loc.T("L.Lua.UseByIdInFight"), log);
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) return LockFailure("denied", Loc.T("L.Lua.NoPermGather"), log);
        if (!LockCode.TryNormalize(rawCode, out code)) return LockFailure("bad_code", Loc.T("L.Lua.LockBadCode"), log);
        inter = ResolveLockTarget(cell, ct);
        if (inter is null) return LockFailure("not_found", Loc.Fmt("L.Lua.LockNotFound", cell), log);
        skill = PickLockSkill(inter, skillSel, log: true);   // liste des skills actifs : diagnostic utile même en route
        if (skill is null) return LockFailure("not_found", Loc.Fmt("L.Lua.LockNotFound", cell), log);
        if (!ApproachInteractive(inter, skill, ct))
        {
            // Agressé pendant l'approche : pas un échec de verrou (la route ignore "fight", comme TravelCapped).
            if (_launch.IsInFight(_pid) || _launch.MsSinceFightEnd(_pid) < 5000) return LockFailure("fight", Loc.T("L.Lua.UseByIdInFight"), log);
            return LockFailure("unreachable", Loc.Fmt("L.Lua.LockUnreachable", cell), log);
        }
        return null;
    }

    private LockResult LockedStorage(int cell, string rawCode, int skillSel, CancellationToken ct, bool log)
    {
        if (LockPrelude(cell, rawCode, skillSel, log, ct, out var code, out var inter, out var skill) is { } fail) return fail;
        Info("coffre", Loc.Fmt("L.Lua.LockStorageOpening", cell));
        var o = _launch.LockedStorageOpenAsync(_pid, inter!.ElementId, skill!.SkillInstanceUid, code, ct).GetAwaiter().GetResult();
        ThrowIfStopping(ct);
        return FromOutcome(o, log);
    }

    private LockResult LockedHouse(int cell, string rawCode, string owner, int skillSel, CancellationToken ct, bool log)
    {
        if (LockPrelude(cell, rawCode, skillSel, log, ct, out var code, out var inter, out var skill) is { } fail) return fail;
        int instanceId = 0;
        if (!string.IsNullOrWhiteSpace(owner))
        {
            var houses = _launch.MapHouses(_pid);
            var door = houses.Where(h => h.DoorElementIds.Contains(inter!.ElementId)).ToList();
            // Porte non rattachée à une maison décodée (doors_on_map vide / autre numérotation) : repli sur toutes les
            // maisons de la carte.
            var pool = door.Count > 0 ? door : houses.ToList();
            var inst = pool.SelectMany(h => h.Instances).FirstOrDefault(i => LockCode.OwnerMatches(i, owner));
            if (inst is null)
            {
                string owners = string.Join(", ", pool.SelectMany(h => h.Instances).Select(i => i.Tag.Length > 0 ? $"{i.Nickname}#{i.Tag}" : i.Nickname));
                return LockFailure("owner_not_found", Loc.Fmt("L.Lua.LockOwnerNotFound", owner.Trim(), owners.Length > 0 ? owners : "-"), log);
            }
            instanceId = inst.InstanceId;
        }
        Info("maison", Loc.Fmt("L.Lua.LockHouseEntering", cell));
        var o = _launch.LockedHouseEnterAsync(_pid, inter!.ElementId, skill!.SkillInstanceUid, instanceId, code, ct).GetAwaiter().GetResult();
        ThrowIfStopping(ct);
        return FromOutcome(o, log);
    }

    // Forme un bot tiers `lockedHouse = "owner|code"`: porte de la maison dont une instance appartient à `owner`. -1 = introuvable.
    private int ResolveHouseDoorByOwner(string owner, CancellationToken ct)
    {
        long map = _launch.CurrentMap(_pid) ?? 0;
        if (map != 0) WaitUntil(() => _launch.InteractivesDecodedMap(_pid) == map, InteractivesDecodeTimeoutMs, ct);
        var st = _world.Get(_pid);
        foreach (var h in _launch.MapHouses(_pid))
        {
            if (!h.Instances.Any(i => LockCode.OwnerMatches(i, owner))) continue;
            foreach (int elem in h.DoorElementIds)
                if (st.Interactives.FirstOrDefault(i => i.ElementId == elem && i.Harvestable) is { } it) return it.CellId;
        }
        return -1;
    }

    // ------------------------------------------------------------------ clés de route
    // Retour : true = RunRoute rend la main (échec, entrée dans la maison, coffre plein) ; false = pas de clé de verrou, ou
    // dépôt réussi -> la suite de l'étape (cell / custom / door / path) s'exécute.
    private bool RunLockRouteKeys(Table step, CancellationToken ct)
    {
        DynValue ls = step.Get("lockedStorage");
        if (ls.Type is DataType.String or DataType.Number)
        {
            string raw = ls.CastToString() ?? "";
            if (!LockCode.TrySplitSpec(raw, out var lp) || !int.TryParse(lp[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lcell))
            {
                Err("coffre", Loc.Fmt("L.Lua.LockBadFormat", SpecHead(raw), "lockedStorage")); StuckStop(); return true;
            }
            int lskill = lp.Length > 2 && int.TryParse(lp[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sk) ? sk : 0;
            int podsBefore = _world.Get(_pid).PodsPercent;
            var r = LockedStorage(lcell, lp[1], lskill, ct, log: false);
            if (r.Ok)
            {
                Info("coffre", Loc.T("L.Lua.LockDepositing"));
                _launch.StorageDepositAndCloseAsync(_pid, ct).GetAwaiter().GetResult();
                InterruptibleDelay(600, ct);
                int podsAfter = _world.Get(_pid).PodsPercent;
                int maxPods = _script is { } sc ? ReadInt(sc, "MAX_PODS", 95) : 95;
                if (podsAfter >= podsBefore && podsAfter >= maxPods) { Warn("coffre", Loc.T("L.Lua.LockChestFull")); LockFail(); return true; }
                _lockFails = 0; Ok("coffre", Loc.T("L.Lua.LockDeposited"));
                // Dépôt fait, coffre fermé : on POURSUIT l'étape (cell / custom / door / path) dans le même tick, sinon
                // l'étape de l'intérieur rouvrirait le coffre à chaque tick sans jamais ressortir par sa `door`.
                return false;
            }
            LockRouteFailure(r, ct);
            return true;
        }
        DynValue lh = step.Get("lockedHouse");
        if (lh.Type is DataType.String or DataType.Number)
        {
            string raw = lh.CastToString() ?? "";
            if (!LockCode.TrySplitSpec(raw, out var lp) || lp[0].Length == 0)
            {
                Err("maison", Loc.Fmt("L.Lua.LockBadFormat", SpecHead(raw), "lockedHouse")); StuckStop(); return true;
            }
            LockResult r;
            if (int.TryParse(lp[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hcell))
                r = LockedHouse(hcell, lp[1], lp.Length > 2 ? lp[2] : "", 0, ct, log: false);   // Un bot tiers: cell|code[|owner]
            else
            {
                int doorCell = ResolveHouseDoorByOwner(lp[0], ct);                                  // Un bot tiers: owner|code
                r = doorCell >= 0
                    ? LockedHouse(doorCell, lp[1], lp[0], 0, ct, log: false)
                    : new LockResult(false, "owner_not_found", Loc.Fmt("L.Lua.LockOwnerNotFound", lp[0], "-"));
            }
            if (r.Ok) { _lockFails = 0; Ok("maison", r.Message); }
            else LockRouteFailure(r, ct);
            return true;
        }
        return false;
    }

    // Partie « cellule » seulement (le code n'est JAMAIS affiché).
    private static string SpecHead(string raw) => raw.Split('|', ';')[0].Trim() + "|…";

    private void LockRouteFailure(LockResult r, CancellationToken ct)
    {
        switch (r.Reason)
        {
            case "fight":
                return;   // aggro : le combat prend la main, pas un échec du verrou
            case "wrong_code": case "forbidden": case "code_required": case "bad_code": case "owner_not_found":
                Err("verrou", r.Message);   // erreur de CONFIGURATION: on arrête
                StuckStop();
                return;
            case "busy":
                Warn("verrou", r.Message);
                InterruptibleDelay(5000, ct);   //: coffre occupé -> nouvel essai 5 s plus tard
                LockFail();
                return;
            default:
                Warn("verrou", r.Message);
                InterruptibleDelay(1500, ct);
                LockFail();
                return;
        }
    }

    private void LockFail()
    {
        if (LinkSuspended) return;   // lien de jeu suspendu (MITM) : injections refusées -> pas un échec du verrou, non compté
        if (++_lockFails >= MaxLockFails)
        {
            Err("verrou", Loc.Fmt("L.Lua.LockTooManyFails", _lockFails));
            _lockFails = 0;
            StuckStop();
        }
    }

    // Tête de RunRoute : intérieur de maison ABSENT du world-graph. Si le trajet a une étape à l'id EXACT de la carte courante,
    // on la joue au lieu de la garde hors-graphe (qui attendait puis arrêtait le script). Sans maison, InHouse est faux ->
    // aucun effet sur les scripts existants.
    private bool IsHouseStep(Table route, long mapId)
        => _launch.InHouse(_pid) && route.Values.Any(v => v.Type == DataType.Table
               && long.TryParse((v.Table.Get("map").CastToString() ?? "").Trim(), out long hm) && hm == mapId);
}
