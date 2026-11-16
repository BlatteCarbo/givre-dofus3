using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ==================================================================================================================
// Lot K — confort (petits verbes) : obstacles personnalisés (map:setCustomObstacles / resetCustomObstacles /
// getCustomObstacles), map:sendMapRequest, clé de route lockedCustom, statut privé (setPrivate / setPrivateTeam /
// getPlayerStatus / isPrivate), resetStats, défis actifs et HTTP
//. Tout est lié à la FIN de Bind (BindComfort);
// les tables de module sont récupérées-ou-créées, jamais écrasées. Détection de `self` PAR RÉFÉRENCE (un 1er argument
// qui est lui-même une table — map.setCustomObstacles({1,2}) — n'est pas pris pour self). Callbacks AOT-safe
// (DynValue.NewCallback). Exécuté UNIQUEMENT sur le thread du script.
// ==================================================================================================================
public sealed partial class GivreLuaHost
{
    // Verrou lockedCustom PAR ÉQUIPE (clé = pid du chef ; hors équipe = son propre pid -> se comporte comme custom).
    private static readonly ConcurrentDictionary<uint, SemaphoreSlim> _teamCustomLocks = new();
    private const int LockedCustomWaitMs = 120000;
    private const int MapRequestReplyTimeoutMs = 4000;

    private static Table GetOrCreateTable(Script s, string name)
    {
        var v = s.Globals.Get(name);
        if (v.Type == DataType.Table) return v.Table;
        var t = new Table(s); s.Globals[name] = DynValue.NewTable(t); return t;
    }

    // self présent (appel colon) ? Détecté PAR RÉFÉRENCE à la table du module.
    private static int Off(CallbackArguments a, Table self) => a.Count > 0 && a[0].Type == DataType.Table && ReferenceEquals(a[0].Table, self) ? 1 : 0;
    private static DynValue At(CallbackArguments a, Table self, int i) { int j = Off(a, self) + i; return j < a.Count ? a[j] : DynValue.Nil; }

    private void BindComfort(Script s, CancellationToken ct)
    {
        BindComfortMap(s, ct);
        BindComfortStatus(s, ct);
        BindComfortChallenges(s);
        BindComfortHttp(s, ct);
    }

    // ------------------------------------------------------------------ map: obstacles + rafraîchissement
    private void BindComfortMap(Script s, CancellationToken ct)
    {
        var map = GetOrCreateTable(s, "map");
        // setCustomObstacles({c1, c2, ...}) OU setCustomObstacles(c1, c2, ...) -> bool. CUMULATIF (portée = carte courante).
        map["setCustomObstacles"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            // Même source de vérité que les lecteurs (IsWalkableForScript / CustomObstacleCells comparent à BotWorldState.MapId) :
            // CurrentMap(pid) peut encore valoir l'ancienne carte entre MapChanged et la mise à jour de _lastMap (jpo).
            var st = _world.Get(_pid);
            long cur = st.MapId;
            if (cur == 0) { Warn("carte", Loc.T("L.Lua.ObstaclesNoMap")); return DynValue.False; }
            var cells = new List<int>();
            int off = Off(a, map);
            for (int j = off; j < a.Count; j++)
            {
                var v = a[j];
                if (v.Type == DataType.Table) { foreach (var x in v.Table.Values) if (x.CastToNumber() is double d) cells.Add((int)d); }
                else if (v.CastToNumber() is double d) cells.Add((int)d);
            }
            st.AddCustomObstacles(cur, cells);
            int n = st.CustomObstacleCount;
            Info("carte", Loc.Fmt("L.Lua.ObstaclesSet", n));
            // Carte changée pendant l'ajout (vidage concurrent par le relais) : le jeu serait ignoré -> on le signale.
            return DynValue.NewBoolean(cells.Count == 0 || n > 0);
        });
        map["resetCustomObstacles"] = DynValue.NewCallback((_, a) =>
        {
            _world.Get(_pid).ClearCustomObstacles();
            Info("carte", Loc.T("L.Lua.ObstaclesReset"));
            return DynValue.Nil;
        });
        map["getCustomObstacles"] = DynValue.NewCallback((_, a) => BuildNums(s, _world.Get(_pid).CustomObstacleCells));
        // sendMapRequest() -> bool : redemande la carte courante et ATTEND la réponse (compteur de jpo, pas de _codeWait
        // qui n'admet qu'un attendant par pid et écraserait une séquence banque/PNJ en cours).
        map["sendMapRequest"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            int s0 = _launch.MapInfoSeq(_pid);
            if (!_launch.SendMapInformationRequest(_pid)) { Warn("carte", Loc.T("L.Lua.MapRequestFail")); return DynValue.False; }
            bool got = WaitUntil(() => _launch.MapInfoSeq(_pid) != s0, MapRequestReplyTimeoutMs, ct);
            if (got) { WaitSelfReady(ct); Ok("carte", Loc.T("L.Lua.MapRequestDone")); }
            else Warn("carte", Loc.T("L.Lua.MapRequestNoReply"));
            return DynValue.NewBoolean(got);
        });
    }

    // ------------------------------------------------------------------ statut joueur + reset des caractéristiques
    private void BindComfortStatus(Script s, CancellationToken ct)
    {
        var g = GetOrCreateTable(s, "global");
        // Argument booléen : absent = true (setPrivate() = passer en privé).
        static bool Flag(DynValue v) => v.IsNil() || v.CastToBool();

        DynValue setPrivate = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            bool priv = Flag(At(a, g, 0));
            bool ok = _launch.SetPlayerStatus(_pid, priv ? LaunchService.PlayerStatusPrivate : LaunchService.PlayerStatusAvailable);
            if (ok) Ok("statut", Loc.T(priv ? "L.Lua.StatusPrivate" : "L.Lua.StatusAvailable"));
            else Warn("statut", Loc.T("L.Lua.StatusFail"));
            return DynValue.NewBoolean(ok);
        });
        // setPrivateTeam(bool) -> nombre de membres basculés. CHEF seulement, Perm.Multi.
        DynValue setPrivateTeam = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            bool priv = Flag(At(a, g, 0));
            if (!_launch.IsTeamLeader(_pid) || !LicenseService.HasPermission(LicenseService.Perm.Multi))
            { Warn("statut", Loc.T("L.Lua.StatusTeamNotLeader")); return DynValue.NewNumber(0); }
            int status = priv ? LaunchService.PlayerStatusPrivate : LaunchService.PlayerStatusAvailable;
            int n = 0;
            foreach (var m in _launch.TeamMembers(_pid))
                if (_launch.CurrentMap(m.Pid) is not null && _launch.SetPlayerStatus(m.Pid, status)) n++;
            Info("statut", Loc.Fmt("L.Lua.StatusTeamDone", n));
            return DynValue.NewNumber(n);
        });
        DynValue getStatus = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.PlayerStatusOf(_pid)));
        DynValue isPrivate = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.PlayerStatusOf(_pid) == LaunchService.PlayerStatusPrivate));
        g["setPrivate"] = setPrivate; g["setPrivateTeam"] = setPrivateTeam; g["getPlayerStatus"] = getStatus; g["isPrivate"] = isPrivate;
        s.Globals["setPrivate"] = setPrivate; s.Globals["setPrivateTeam"] = setPrivateTeam;
        s.Globals["getPlayerStatus"] = getStatus; s.Globals["isPrivate"] = isPrivate;

        // resetStats() / character:resetStats() -> bool. Hors combat ; gate Perm.Fight + cooldown côté moteur.
        DynValue resetStats = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            if (_launch.IsInFight(_pid)) { Warn("carac", Loc.T("L.Lua.ResetStatsInFight")); return DynValue.False; }
            bool ok = _launch.ResetCharacteristics(_pid);
            if (ok) Ok("carac", Loc.T("L.Lua.ResetStatsDone")); else Warn("carac", Loc.T("L.Lua.ResetStatsFail"));
            return DynValue.NewBoolean(ok);
        });
        GetOrCreateTable(s, "character")["resetStats"] = resetStats;
        s.Globals["resetStats"] = resetStats;
    }

    // ------------------------------------------------------------------ défis actifs (lecture pure)
    private void BindComfortChallenges(Script s)
    {
        static string StatusOf(int r) => r switch { 1 => "success", 0 => "failed", _ => "running" };
        DynValue Rows() => BuildList(s, _launch.FightChallenges(_pid), (row, x) =>
        {
            var c = x.Challenge;
            row["id"] = DynValue.NewNumber(c.Id);
            row["state"] = DynValue.NewNumber(c.State);
            row["status"] = DynValue.NewString(StatusOf(x.Result));
            row["xpBonus"] = DynValue.NewNumber(c.XpBonus);
            row["dropBonus"] = DynValue.NewNumber(c.DropBonus);
            row["targets"] = BuildList(s, c.Targets, (t, tg) => { t["id"] = DynValue.NewNumber(tg.TargetId); t["cell"] = DynValue.NewNumber(tg.TargetCell); });
        });

        var combat = GetOrCreateTable(s, "combat");
        combat["challenges"] = DynValue.NewCallback((_, a) => Rows());

        // fightChallenge:. id optionnel: -1 / absent = 1er défi (plus petit id).
        var fc = GetOrCreateTable(s, "fightChallenge");
        FightPrepReader.FightChallenge? Pick(CallbackArguments a)
        {
            var list = _launch.FightChallenges(_pid);
            var v = At(a, fc, 0);
            int id = v.CastToNumber() is double d ? (int)d : -1;
            return id == -1 ? list.FirstOrDefault().Challenge : list.FirstOrDefault(x => x.Challenge.Id == id).Challenge;
        }
        fc["getChallengeIds"] = DynValue.NewCallback((_, a) => BuildNums(s, _launch.FightChallenges(_pid).Select(x => x.Challenge.Id)));
        fc["getTargetIds"] = DynValue.NewCallback((_, a) =>
        {
            var t = new Table(s); int i = 1;
            if (Pick(a) is { } c) foreach (var tg in c.Targets) t.Set(i++, DynValue.NewNumber(tg.TargetId));
            return DynValue.NewTable(t);
        });
        fc["getTargetCells"] = DynValue.NewCallback((_, a) => BuildNums(s, Pick(a) is { } c ? c.Targets.Select(x => x.TargetCell) : Enumerable.Empty<int>()));
        fc["getXpBonus"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(Pick(a)?.XpBonus ?? 0));
        fc["getDropBonus"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(Pick(a)?.DropBonus ?? 0));
        fc["getResultFromId"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.ChallengeResult(_pid, (int)(At(a, fc, 0).CastToNumber() ?? 0))));
        fc["getFightChallenges"] = DynValue.NewCallback((_, a) => Rows());
        // Pas de données statiques de défis extraites aujourd'hui (Challenges.nameId) -> chaîne vide + avertissement unique.
        fc["challengeName"] = DynValue.NewCallback((_, a) => DynValue.NewString(_launch.GameNames.ChallengeName((int)(At(a, fc, 0).CastToNumber() ?? 0)) ?? ""));
    }

    // ------------------------------------------------------------------ HTTP (Perm.PmRelay, https public uniquement)
    private void BindComfortHttp(Script s, CancellationToken ct)
    {
        var http = GetOrCreateTable(s, "http");
        var dev = GetOrCreateTable(s, "developer");

        // Table Lua { ["Nom"] = "valeur" } -> paires (clés non-chaînes ignorées).
        static List<KeyValuePair<string, string>> HeadersOf(DynValue v)
        {
            var l = new List<KeyValuePair<string, string>>();
            if (v.Type != DataType.Table) return l;
            foreach (var p in v.Table.Pairs)
                if (p.Key.Type == DataType.String) l.Add(new(p.Key.String, p.Value.CastToString() ?? ""));
            return l;
        }
        // Deux listes parallèles.
        static List<KeyValuePair<string, string>> HeadersOfLists(DynValue names, DynValue values)
        {
            var l = new List<KeyValuePair<string, string>>();
            if (names.Type != DataType.Table || values.Type != DataType.Table) return l;
            int n = names.Table.Length;
            for (int i = 1; i <= n; i++)
            {
                string k = names.Table.Get(i).CastToString() ?? "";
                if (k.Length > 0) l.Add(new(k, values.Table.Get(i).CastToString() ?? ""));
            }
            return l;
        }

        ScriptHttpResult Do(string method, string url, string? body, List<KeyValuePair<string, string>> headers)
        {
            ThrowIfStopping(ct);
            ScriptHttpResult r;
            try { r = ScriptHttpService.Send(method, url, body, headers, ct); }
            catch (OperationCanceledException) { ThrowIfStopping(ct); r = ScriptHttpResult.Fail("timeout"); }
            string host = Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.Length > 0 ? u.Host : "?";   // HÔTE seulement (jamais chemin/requête/en-têtes)
            if (r.Error is null) Info("http", Loc.Fmt("L.Lua.HttpDone", method, host, r.Status));
            else Warn("http", Loc.Fmt("L.Lua.HttpFailed", method, host, r.Error));
            return r;
        }
        DynValue ToTable(ScriptHttpResult r)
        {
            var t = new Table(s);
            t["ok"] = DynValue.NewBoolean(r.Ok);
            t["status"] = DynValue.NewNumber(r.Status);
            t["body"] = DynValue.NewString(r.Body);
            t["contentType"] = r.ContentType is null ? DynValue.Nil : DynValue.NewString(r.ContentType);
            t["location"] = r.Location is null ? DynValue.Nil : DynValue.NewString(r.Location);
            t["error"] = r.Error is null ? DynValue.Nil : DynValue.NewString(r.Error);
            return DynValue.NewTable(t);
        }
        static string Str(DynValue v) => v.IsNil() ? "" : (v.CastToString() ?? "");

        http["get"] = DynValue.NewCallback((_, a) => ToTable(Do("GET", Str(At(a, http, 0)), null, HeadersOf(At(a, http, 1)))));
        http["post"] = DynValue.NewCallback((_, a) => ToTable(Do("POST", Str(At(a, http, 0)), Str(At(a, http, 1)), HeadersOf(At(a, http, 2)))));
        // developer:getRequest(url [, names, values]) / postRequest(url, data [, names, values]) -> corps ("" en échec).
        // Les arguments proxy un bot tiers (5e/6e) sont ignorés: connexion directe.
        dev["getRequest"] = DynValue.NewCallback((_, a) =>
        {
            var r = Do("GET", Str(At(a, dev, 0)), null, HeadersOfLists(At(a, dev, 1), At(a, dev, 2)));
            return DynValue.NewString(r.Error is null ? r.Body : "");
        });
        dev["postRequest"] = DynValue.NewCallback((_, a) =>
        {
            var r = Do("POST", Str(At(a, dev, 0)), Str(At(a, dev, 1)), HeadersOfLists(At(a, dev, 2), At(a, dev, 3)));
            return DynValue.NewString(r.Error is null ? r.Body : "");
        });
    }

    // ------------------------------------------------------------------ clé de route lockedCustom
    // `custom` exécuté sous EXCLUSION MUTUELLE à l'échelle de l'équipe. Attente bornée
    // (120 s : aucun membre ne peut bloquer l'équipe indéfiniment) ; un combat lancé dans la closure garde le verrou pendant
    // le combat (sérialisation voulue). Aucun gate : le verrou n'envoie rien, chaque verbe de la closure a le sien.
    // true = l'appelant rend la main (carte changée / combat / verrou indisponible -> étape retentée au tick suivant).
    private bool RunLockedCustom(Table step, long mapId, CancellationToken ct)
    {
        long before = _launch.CurrentMap(_pid) ?? mapId;
        uint key = _launch.TeamViewOf(_pid)?.LeaderPid ?? _pid;
        var sem = _teamCustomLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        bool got = sem.Wait(0);
        if (!got)
        {
            Info("trajet", Loc.T("L.Lua.LockedCustomWait"));
            got = WaitUntil(() => sem.Wait(0), LockedCustomWaitMs, ct, pump: true);   // arrêt du script -> ScriptStopException (verrou non pris)
        }
        if (!got) { Warn("trajet", Loc.Fmt("L.Lua.LockedCustomTimeout", LockedCustomWaitMs / 1000)); InterruptibleDelay(1000, ct); return true; }
        try
        {
            // Re-vérification APRÈS acquisition (surtout après attente) : un follower tiré dans le combat du chef, ou dont la
            // carte a changé pendant l'attente (suivi, mort/Phénix), ne doit pas exécuter une closure prévue pour l'ancien
            // contexte (invariant « move() suspendu en combat »). true -> l'étape est re-matchée au tick suivant.
            if (_launch.IsInFight(_pid) || (_launch.CurrentMap(_pid) ?? before) != before) return true;
            Info("trajet", Loc.T("L.Lua.LockedCustomRun"));
            try { CallLua(_script!, step.Get("lockedCustom"), ct); }
            catch (ScriptStopException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Warn("trajet", Loc.Fmt("L.Lua.TravelCustomError", ex.Message)); }
        }
        finally { sem.Release(); }
        ThrowIfStopping(ct);
        return (_launch.CurrentMap(_pid) ?? before) != before || _launch.IsInFight(_pid);
    }
}
