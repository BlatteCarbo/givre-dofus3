using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MoonSharp.Interpreter;
using GivreClone.App.Services.Eco;
using GivreClone.App.Services.Forgemagie;

namespace GivreClone.App.Services;

// Module Lua fm: (forgemagie scriptée), comme l'onglet Forgemagie :
//   fm:item(objet)                      -> { uid, gid, name, lines = { { effectId, name, value }, … } } ou nil
//   fm:setTarget(objet, ligne, valeur)  -> objectif d'une ligne (ligne = libellé « Force », code « Fo » ou id d'effet) ; fm:clearTargets(objet)
//   fm:advise(objet)                    -> nom de la rune conseillée (ou nil), raison
//   fm:apply(objet, rune)               -> passe UNE rune précise à l'atelier de la carte : « succès » / « neutre » / « échec » / raison d'arrêt
//   fm:autoRun(objet [, runesMax])      -> passe les runes conseillées jusqu'aux objectifs : raison d'arrêt, nombre de runes
// objet = uid ou gid d'un objet du sac. Objectifs gardés pour la durée du script. ⚠ Séquence d'atelier pas encore confirmée par une capture.
public sealed partial class GivreLuaHost
{
    private readonly Dictionary<int, Dictionary<int, int>> _fmTargets = new();   // uid objet -> (id d'effet -> objectif)

    private void BindFm(Script s, CancellationToken ct)
    {
        var fm = GetOrCreateTable(s, "fm");

        int? Uid(DynValue v) => v.CastToNumber() is double d && d > 0 ? _launch.BagUidForForge(_pid, (long)d) : null;
        static RuneFamily? Line(DynValue v)
        {
            if (v.Type == DataType.Number) return RuneCatalog.Of((int)v.Number);
            string k = v.CastToString() ?? "";
            return RuneCatalog.Families.FirstOrDefault(f => string.Equals(f.Label, k, StringComparison.OrdinalIgnoreCase) || string.Equals(f.Code, k, StringComparison.OrdinalIgnoreCase));
        }
        Dictionary<int, int> Values(int uid) => ForgeState.ById(_launch.ItemEffects(_pid, uid));

        // Stock de runes du sac par (libellé, palier) pour RuneAdvisor.
        Dictionary<(string Stat, string Tier), int> Stock()
        {
            var runes = _launch.RuneItemsByGid();
            var stock = new Dictionary<(string, string), int>();
            foreach (var it in _launch.Inventory.Items(_pid).Where(i => i.Position == 63 && i.Qty > 0))
                if (runes.TryGetValue(it.Gid, out var r) && RuneCatalog.Of(r.EffectId) is { } f)
                    stock[(f.Label, r.Tier)] = stock.GetValueOrDefault((f.Label, r.Tier)) + it.Qty;
            return stock;
        }
        RuneAdvice Advise(int uid, IReadOnlyDictionary<int, int>? current = null)
        {
            var vals = current ?? Values(uid);
            var targets = _fmTargets.GetValueOrDefault(uid) ?? new Dictionary<int, int>();
            var lines = targets.Where(t => RuneCatalog.Of(t.Key) is not null)
                .Select(t => new ForgeLine(RuneCatalog.Of(t.Key)!.Label, vals.GetValueOrDefault(t.Key), 0, 0, t.Value))
                .ToList();
            return RuneAdvisor.Advise(lines, Stock(), new AdvisorSettings(AllowOver: true), RuneCatalog.Gains);
        }
        static string RuneName(RuneAdvice a) => a.Rune is { } r && RuneCatalog.Families.FirstOrDefault(f => f.Label == r.Stat) is { } f ? RuneCatalog.ItemName(f, r.Tier) : "";

        fm["prepare"] = DynValue.NewCallback((_, a) =>
        {
            if (Uid(At(a, fm, 0)) is not { } uid)
                return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("objet absent du sac"));
            if (_launch.GameData is not { } db)
                return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("base locale indisponible"));
            int jobId = (int)(At(a, fm, 1).CastToNumber() ?? 0);
            if (!_launch.JobLevels(_pid).TryGetValue(jobId, out int level) || level <= 0)
                return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("niveau du métier inconnu"));
            if (_launch.Inventory.Get(_pid, uid) is not { Position: 63, Qty: > 0 } item)
                return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("objet absent du sac"));
            var support = db.SmithLevelingSupports(jobId, level).FirstOrDefault(candidate => candidate.Gid == item.Gid);
            if (support is null)
                return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("support incompatible, trop haut niveau ou métadonnées absentes"));
            if (!GivreClone.Engine.Recovery.ItemBindingRules.CanForge(_launch.ItemBindingOf(_pid, uid)))
                return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("objet lié"));
            var result = new Table(s);
            result["uid"] = uid;
            result["gid"] = item.Gid;
            result["jobId"] = jobId;
            result["jobLevel"] = level;
            result["itemLevel"] = support.Level;
            return DynValue.NewTuple(DynValue.NewTable(result), DynValue.NewString("support compatible ; aucune rune consommée"));
        });

        fm["item"] = DynValue.NewCallback((_, a) =>
        {
            if (Uid(At(a, fm, 0)) is not { } uid) return DynValue.Nil;
            var it = _launch.Inventory.Items(_pid).FirstOrDefault(i => i.Uid == uid);
            var t = new Table(s);
            t["uid"] = DynValue.NewNumber(uid); t["gid"] = DynValue.NewNumber(it.Gid); t["name"] = DynValue.NewString(_launch.ItemDisplayName(it.Gid));
            var lines = new Table(s);
            foreach (var (id, v) in Values(uid))
            {
                var r = new Table(s);
                r["effectId"] = DynValue.NewNumber(id); r["value"] = DynValue.NewNumber(v);
                r["name"] = DynValue.NewString(RuneCatalog.Of(id)?.Label ?? "");
                lines.Append(DynValue.NewTable(r));
            }
            t["lines"] = DynValue.NewTable(lines);
            return DynValue.NewTable(t);
        });

        fm["setTarget"] = DynValue.NewCallback((_, a) =>
        {
            if (Uid(At(a, fm, 0)) is not { } uid || Line(At(a, fm, 1)) is not { } f) return DynValue.False;
            int target = (int)(At(a, fm, 2).CastToNumber() ?? 0);
            (_fmTargets.TryGetValue(uid, out var d) ? d : _fmTargets[uid] = new())[f.EffectId] = target;
            return DynValue.True;
        });

        fm["clearTargets"] = DynValue.NewCallback((_, a) =>
        {
            if (Uid(At(a, fm, 0)) is { } uid) _fmTargets.Remove(uid);
            return DynValue.Nil;
        });

        fm["advise"] = DynValue.NewCallback((_, a) =>
        {
            if (Uid(At(a, fm, 0)) is not { } uid) return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("objet absent du sac"));
            var adv = Advise(uid);
            return DynValue.NewTuple(adv.Kind == AdviceKind.Rune ? DynValue.NewString(RuneName(adv)) : DynValue.Nil, DynValue.NewString(adv.Reason));
        });

        fm["apply"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            if (Uid(At(a, fm, 0)) is not { } uid) return DynValue.NewString("objet absent du sac");
            string rune = At(a, fm, 1).CastToString() ?? "";
            if (rune.Length == 0) return DynValue.NewString("rune manquante");
            if (RuneCatalog.Workshop(_world.Get(_pid).Interactives) is not { } ws) return DynValue.NewString("aucun atelier de forgemagie sur la carte");
            bool sent = false;
            var r = _launch.ForgeAsync(_pid, ws.ElementId, ws.SkillUid, uid, (_, _) => sent ? ForgeAction.Finish : (sent = true) ? ForgeAction.Use(rune) : ForgeAction.Finish, 1, ct)
                .GetAwaiter().GetResult();
            return DynValue.NewString(r.Steps.Count == 0 ? r.Stop : r.Steps[0].Code switch
            {
                GivreClone.Protocol.InventoryReader.CraftSuccess => "succès", GivreClone.Protocol.InventoryReader.CraftNeutral => "neutre",
                GivreClone.Protocol.InventoryReader.CraftFailed => "échec", var c => $"code {c}",
            });
        });

        fm["autoRun"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            if (Uid(At(a, fm, 0)) is not { } uid) return DynValue.NewTuple(DynValue.NewString("objet absent du sac"), DynValue.NewNumber(0));
            if (!_fmTargets.ContainsKey(uid)) return DynValue.NewTuple(DynValue.NewString("aucun objectif (fm:setTarget)"), DynValue.NewNumber(0));
            if (RuneCatalog.Workshop(_world.Get(_pid).Interactives) is not { } ws) return DynValue.NewTuple(DynValue.NewString("aucun atelier de forgemagie sur la carte"), DynValue.NewNumber(0));
            int max = Math.Clamp((int)(At(a, fm, 1).CastToNumber() ?? 100), 1, 1000);
            ForgeAction Decide(IReadOnlyDictionary<string, int> stats, int? pool)
            {
                var byId = RuneCatalog.Families.Where(f => stats.ContainsKey(f.Code)).ToDictionary(f => f.EffectId, f => stats[f.Code]);
                var adv = Advise(uid, byId);
                return adv.Kind == AdviceKind.Rune ? ForgeAction.Use(RuneName(adv)) : RuneAdvisor.StopAction(adv);
            }
            var r = _launch.ForgeAsync(_pid, ws.ElementId, ws.SkillUid, uid, Decide, max, ct).GetAwaiter().GetResult();
            return DynValue.NewTuple(DynValue.NewString(r.Stop), DynValue.NewNumber(r.Steps.Count));
        });
    }
}
