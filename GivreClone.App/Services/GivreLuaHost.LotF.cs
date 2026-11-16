using System;
using System.Collections.Generic;
using System.Threading;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ==================================================================================================================
// Lot F — survie : PV réels hors combat, régénération (consommables REGEN_FOOD puis assis + attente bornée), énergie,
// paliers d'XP. Verbes liés AVANT DoString (Bind -> BindLotF) : marchent au top-level et dans RunOnce. Convention :
// -1 = inconnu. Regenerate tourne UNIQUEMENT sur le thread du script (lectures moteur = ConcurrentDictionary).
// ==================================================================================================================
public sealed partial class GivreLuaHost
{
    private const int RegenChunkMs = 15000;        // tranche d'attente : NoteProgress au moins toutes les 15 s (watchdog 180 s)
    private const int RegenFoodMaxUses = 100;      // borne dure du nombre de consommables par régénération

    private void BindLotF(Script s, CancellationToken ct)
    {
        var g = s.Globals;
        g["getLifePoints"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.SelfLife(_pid)?.Cur ?? -1));
        g["getMaxLifePoints"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.SelfLife(_pid)?.Max ?? -1));
        g["getLifePointsPercent"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.SelfLifePercent(_pid) ?? -1));
        g["getEnergyPoints"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.SelfEnergy(_pid)?.Cur ?? -1));
        g["getMaxEnergyPoints"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.SelfEnergy(_pid)?.Max ?? -1));
        g["getXpNextLevel"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.CharacterXpNextLevel(_pid)));
        g["getXpLevelFloor"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.CharacterXpLevelFloor(_pid)));
        // Altérations (élixirs uchroniques 50/51/52, bonus, malus) : hasAlteration(id) -> true / false, nil si la liste n'a
        // jamais été reçue ; alterationValue(id) -> valeur d'expiration (combats restants pour un élixir), -1 si absente.
        g["hasAlteration"] = DynValue.NewCallback((_, a) => _launch.HasAlteration(_pid, ArgN(a, 0)) is bool b ? DynValue.NewBoolean(b) : DynValue.Nil);
        g["alterationValue"] = DynValue.NewCallback((_, a) =>
        {
            int id = ArgN(a, 0);
            foreach (var x in _launch.Alterations(_pid)) if (x.Id == id) return DynValue.NewNumber(x.ExpirationValue);
            return DynValue.NewNumber(-1);
        });
        g["getAlterations"] = DynValue.NewCallback((_, a) =>
        {
            var list = new Table(s); int i = 1;
            foreach (var x in _launch.Alterations(_pid))
            {
                var row = new Table(s);
                row["id"] = DynValue.NewNumber(x.Id); row["type"] = DynValue.NewNumber(x.ExpirationType); row["value"] = DynValue.NewNumber(x.ExpirationValue);
                list.Set(i++, DynValue.NewTable(row));
            }
            return DynValue.NewTable(list);
        });
        // regenerate(minPct [, maxWaitSec]) -> bool. minPct nil/0 -> MIN_LIFE_PERCENT si > 0, sinon 100. Tolère l'appel colon.
        g["regenerate"] = DynValue.NewCallback((_, a) =>
        {
            int pct = (int)(ArgV(a, 0).CastToNumber() ?? 0);
            int wait = (int)(ArgV(a, 1).CastToNumber() ?? -1);
            var options = _launch.RegenerationOptionsFor(_pid);
            if (options.Enabled)
                return DynValue.NewBoolean(Regenerate(pct > 0 ? pct : options.Stop, wait, ct, options.FoodGid, authoritative: true));
            if (pct <= 0) { int m = ReadInt(s, "MIN_LIFE_PERCENT", 0); pct = m > 0 ? m : 100; }
            return DynValue.NewBoolean(Regenerate(pct, wait, ct));
        });

        // ---- character: alias tiers (character.vb). Table get-or-create: ne JAMAIS écraser une clé existante (Lot B/E). ----
        var chV = g.Get("character");
        var ch = chV.Type == DataType.Table ? chV.Table : new Table(s);
        void Add(string key, DynValue fn) { if (ch.Get(key).IsNil()) ch[key] = fn; }
        Add("lifePoints", g.Get("getLifePoints"));
        Add("maxLifePoints", g.Get("getMaxLifePoints"));
        Add("lifePointsP", g.Get("getLifePointsPercent"));
        Add("energyPoints", g.Get("getEnergyPoints"));
        Add("maxEnergyPoints", g.Get("getMaxEnergyPoints"));
        Add("experience", DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.CharacterXp(_pid))));
        Add("level", DynValue.NewCallback((_, a) => DynValue.NewNumber(_world.Get(_pid).Level ?? 0)));
        Add("hasAlteration", g.Get("hasAlteration"));
        Add("alterationValue", g.Get("alterationValue"));
        Add("alterations", g.Get("getAlterations"));
        g["character"] = DynValue.NewTable(ch);
    }

    // Liste d'entiers d'un global : un nombre -> 1 élément ; une table -> éléments 1..Length (nombres seulement, ordre gardé).
    private List<int> ReadIntList(string key)
    {
        var list = new List<int>();
        var v = _script?.Globals.Get(key) ?? DynValue.Nil;
        if (v.Type == DataType.Number) list.Add((int)v.Number);
        else if (v.Type == DataType.Table)
            for (int i = 1; i <= v.Table.Length; i++)
                if (v.Table.Get(i) is { Type: DataType.Number } n) list.Add((int)n.Number);
        return list;
    }

    /// <summary>Régénère jusqu'à <paramref name="targetPct"/> % de PV : consommables (REGEN_FOOD, dans l'ordre), puis assis
    /// (REGEN_SIT / REGEN_SIT_EMOTE) et attente bornée (maxWaitSec &gt; 0, sinon estimation plafonnée par REGEN_MAX_WAIT).
    /// Bloquant, annulable (Stop/Planning). true si PV% ≥ cible à la fin ; false en combat, mort, PV inconnus, délai, agression.</summary>
    private bool Regenerate(int targetPct, int maxWaitSec, CancellationToken ct, int? selectedFood = null, bool authoritative = false)
    {
        ThrowIfStopping(ct);
        targetPct = Math.Clamp(targetPct, 1, 100);
        if (_launch.IsInFight(_pid)) { Warn("vie", Loc.T("L.Lua.RegenInFight")); return false; }
        if (_launch.LifeStatusOf(_pid) != 0) return false;   // mort : la résurrection (RunLoop/HandleDeath) s'en charge
        if (_launch.SelfLife(_pid) is null) WaitUntil(() => _launch.SelfLife(_pid) is not null, 3000, ct);
        if (_launch.SelfLife(_pid) is null) { Warn("vie", Loc.T("L.Lua.RegenUnknown")); return false; }
        int Pct() => (authoritative && _launch.AuthoritativeLifePercent(_pid) is null ? null : _launch.SelfLifePercent(_pid)) ?? 0;
        (int Cur, int Max) Life() => _launch.SelfLife(_pid) ?? (0, 0);
        if (Pct() >= targetPct) return true;   // silencieux (appelé à chaque tick par la clé d'étape / MIN_LIFE_PERCENT)
        var l0 = Life();
        Info("vie", Loc.Fmt("L.Lua.RegenStart", Pct(), targetPct, l0.Cur, l0.Max));

        // 1) Consommables (REGEN_FOOD, dans l'ordre) : un par un, chacun confirmé par une HAUSSE des PV autoritaires au-delà de
        //    l'estimation d'avant usage (la fiche refire souvent — kamas/pods/XP — : un simple changement de séquence ne prouve rien).
        int uses = 0;
        foreach (int gid in selectedFood is int food ? new List<int> { food } : ReadIntList("REGEN_FOOD"))
        {
            if (gid <= 0) continue;
            int fails = 0;
            bool announced = false;
            while (Pct() < targetPct && _launch.InventoryCount(_pid, gid) > 0 && uses < RegenFoodMaxUses && fails < 2)
            {
                if (_launch.IsInFight(_pid)) { Warn("vie", Loc.T("L.Lua.RegenInterrupted")); return false; }
                long seq = _launch.LifeSeq(_pid);
                int before = Life().Cur;   // estimation (régénération comprise) : un objet sans effet ne la dépasse pas
                if (!_launch.UseInventoryItem(_pid, gid, 1))
                {
                    if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) Warn("vie", Loc.T("L.Lua.NoPermGather"));
                    break;
                }
                uses++;
                _launch.NoteProgress(_pid);
                if (!announced)
                {
                    var l = Life();
                    Info("vie", Loc.Fmt("L.Lua.RegenEat", _launch.ItemDisplayName(gid), l.Cur, l.Max, _launch.InventoryCount(_pid, gid)));
                    announced = true;
                }
                if (!WaitUntil(() => _launch.LifeSeq(_pid) != seq && (_launch.SelfLifeAuthoritative(_pid) ?? 0) > before, 3000, ct)) { fails++; continue; }
                fails = 0;
                InterruptibleDelay(400, ct);   // humanise (jitter ±15 % dans InterruptibleDelay)
            }
            if (fails >= 2) Warn("vie", Loc.Fmt("L.Lua.RegenFoodNoEffect", _launch.ItemDisplayName(gid)));
        }
        if (Pct() >= targetPct) { var ld = Life(); Ok("vie", Loc.Fmt("L.Lua.RegenDone", Pct(), ld.Cur, ld.Max)); return true; }

        // 2) Assis (régénération doublée). Pas besoin de se relever : le prochain déplacement annule la position en jeu.
        bool sittingForRecovery = false;
        if (ReadBool(_script!, "REGEN_SIT", true))
        {
            long t0 = Environment.TickCount64;
            if (_launch.SendEmote(_pid, ReadInt(_script!, "REGEN_SIT_EMOTE", 1)))
            {
                Info("vie", Loc.T("L.Lua.RegenSit"));
                sittingForRecovery = WaitUntil(() => _launch.EmoteAckAt(_pid) > t0 || _launch.EmoteErrorAt(_pid) > t0, 2500, ct)
                    && _launch.EmoteErrorAt(_pid) <= t0;
                if (_launch.EmoteErrorAt(_pid) > t0) Warn("vie", Loc.T("L.Lua.RegenSitRefused"));
                InterruptibleDelay(800, ct);   // laisse arriver le nouveau taux de régénération (assis)
            }
        }

        // 3) Attente bornée, par tranches (NoteProgress : le watchdog anti-blocage ne doit pas se déclencher pendant l'attente).
        var lw = Life();
        int need = Math.Max(0, (int)Math.Ceiling(targetPct * lw.Max / 100.0) - lw.Cur);
        long budgetMs = maxWaitSec > 0
            ? maxWaitSec * 1000L
            : Math.Clamp((long)need * _launch.SelfRegenMsPerHp(_pid) * 3 / 2 + 10000, 15000L, Math.Max(15, ReadInt(_script!, "REGEN_MAX_WAIT", 600)) * 1000L);
        long elapsed = 0;
        bool confirmationRequested = false;
        while (elapsed < budgetMs)
        {
            int chunk = (int)Math.Min(RegenChunkMs, budgetMs - elapsed);
            if (WaitUntil(() => Pct() >= targetPct || _launch.IsInFight(_pid) || _launch.LifeStatusOf(_pid) != 0, chunk, ct)) break;
            elapsed += chunk;
            // An estimate can schedule the end of sitting, but never confirm healing.
            // Request the end of the confirmed sitting emote; still await a life observation.
            if (authoritative && sittingForRecovery && !confirmationRequested
                && (_launch.SelfLifePercent(_pid) ?? 0) >= targetPct)
            {
                confirmationRequested = true;
                _launch.SendEmote(_pid, ReadInt(_script!, "REGEN_SIT_EMOTE", 1));
            }
            _launch.NoteProgress(_pid);   // ⚠ watchdog anti-stuck (seuil 180 s)
            var lp = Life();
            Info("vie", Loc.Fmt("L.Lua.RegenProgress", Pct(), lp.Cur, lp.Max));
        }
        if (_launch.IsInFight(_pid)) { Warn("vie", Loc.T("L.Lua.RegenInterrupted")); return false; }
        var le = Life();
        if (Pct() >= targetPct) { Ok("vie", Loc.Fmt("L.Lua.RegenDone", Pct(), le.Cur, le.Max)); return true; }
        Warn("vie", Loc.Fmt("L.Lua.RegenTimeout", budgetMs / 1000, Pct()));
        return false;
    }
}
