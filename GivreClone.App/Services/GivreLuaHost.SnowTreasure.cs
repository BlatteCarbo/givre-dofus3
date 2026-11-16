using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ==================================================================================================================
// Compatibilité tiers — treasure: (treasure.vb): chasse au trésor. État décodé PAR NOM (TreasureHuntEvent) côté moteur;
// les verbes envoient drapeau / creuser / abandon (forges par nom) et ATTENDENT la réponse serveur (≤ 30 s, sous le seuil
// anti-blocage de 180 s; Un bot tiers attendait 300 s). Index d'étape: 0-based (-1 = dernière étape).
// Champs publics un bot tiers lisibles en direct: treasure.treasure (état), treasure._isStarted, treasure._isHunting,
// treasure.giveUp, treasure.nextMapId (toujours 0 : pas de résolveur d'indices externe), treasure.validated.
// Non portés : gdPggXxViU (appelle des sites tiers — à refaire avec developer:getRequest si voulu), sondes de licence.
// ==================================================================================================================
public sealed partial class GivreLuaHost
{
    private const int TreasureAnswerMs = 30000;
    private const int TreasureStartElement = 484993;   // élément interactif « coffre de chasse »
    private bool _treasureGivingUp;
    private int _treasureValidated;
    private int _treasureCooldownMin;

    private void BindSnowTreasure(Script s, CancellationToken ct)
    {
        var tr = GetOrCreateTable(s, "treasure");
        int N(CallbackArguments a, int i, int def = 0) { var v = At(a, tr, i); return v.IsNil() ? def : (int)(v.CastToNumber() ?? def); }
        LaunchService.TreasureHunt? Hunt() => _launch.Treasure(_pid);
        LaunchService.TreasureStep? StepAt(int index)
        {
            var h = Hunt();
            if (h is null || h.Steps.Count == 0) return null;
            if (index < 0) return h.Steps[^1];
            return index < h.Steps.Count ? h.Steps[index] : null;
        }
        static string DirName(int d) => d switch { 0 => "right", 2 => "bottom", 4 => "left", 6 => "top", _ => "5" };
        bool WaitSeq(Func<bool> cond, int ms) => WaitUntil(cond, ms, ct, pump: true);

        // ---- état ----
        DynValue State()
        {
            var h = Hunt();
            if (h is null) return DynValue.Nil;
            var t = new Table(s);
            t["questType"] = DynValue.NewNumber(h.QuestType);
            t["startMapId"] = DynValue.NewNumber(h.StartMapId);
            t["totalStepCount"] = DynValue.NewNumber(h.TotalStepCount);
            t["checkPointCurrent"] = DynValue.NewNumber(h.CurrentCheckPoint); t["currentCheckPoint"] = t.Get("checkPointCurrent");
            t["checkPointTotal"] = DynValue.NewNumber(h.TotalCheckPoint); t["totalCheckPoint"] = t.Get("checkPointTotal");
            t["availableRetryCount"] = DynValue.NewNumber(h.AvailableRetryCount);
            t["flags"] = BuildList(s, h.Flags, (r, f) => { r["mapId"] = DynValue.NewNumber(f.MapId); r["state"] = DynValue.NewNumber(f.State); });
            var steps = BuildList(s, h.Steps, (r, st) =>
            {
                r["type"] = DynValue.NewString(st.Kind);
                r["direction"] = DynValue.NewNumber(st.Direction);
                if (st.NpcId != 0) r["npcId"] = DynValue.NewNumber(st.NpcId);
                if (st.PoiLabelId != 0) r["poiLabelId"] = DynValue.NewNumber(st.PoiLabelId);
                if (st.MapCount != 0) r["mapCount"] = DynValue.NewNumber(st.MapCount);
            });
            t["knownStepsList"] = steps; t["knownSteps"] = steps;
            return DynValue.NewTable(t);
        }
        Fill(tr, "state", DynValue.NewCallback((_, a) => State()));

        // ---- indices : treasure.findClue([nom, x, y, direction]) -> { x, y, distance, mapId, name } ou nil ; treasure.findClues(...) -> liste (la plus proche d'abord).
        // Sans argument : l'indice et la direction de l'étape en cours, depuis la carte où se trouve le perso. Données DofusDB (cache disque).
        IReadOnlyList<Services.Treasure.TreasureClue> ClueMatches(CallbackArguments a, out string why)
        {
            why = "";
            var step = StepAt(-1);
            string name = At(a, tr, 0).IsNil() ? (step is { Kind: "poi" } ? _launch.GameNames.PoiName(step.PoiLabelId) ?? "" : "") : At(a, tr, 0).CastToString() ?? "";
            var cur = _launch.CurrentMap(_pid) is long cm && cm > 0 ? _launch.MapCoordOf(cm) : null;
            int x = At(a, tr, 1).IsNil() ? cur?.X ?? int.MinValue : (int)(At(a, tr, 1).CastToNumber() ?? int.MinValue);
            int y = At(a, tr, 2).IsNil() ? cur?.Y ?? int.MinValue : (int)(At(a, tr, 2).CastToNumber() ?? int.MinValue);
            int dir = -1;
            var dv = At(a, tr, 3);
            if (dv.IsNil()) { if (step is { Kind: "poi" or "hint" or "direction" }) dir = step.Direction; }
            else if (!Services.Treasure.TreasureClues.TryDirection(dv.Type == DataType.Number ? ((int)dv.Number).ToString() : dv.CastToString(), out dir)) dir = -1;
            if (name.Trim().Length == 0 || x == int.MinValue || y == int.MinValue || dir < 0) { why = "arguments manquants (nom, x, y, direction)"; return Array.Empty<Services.Treasure.TreasureClue>(); }
            try { return Services.Treasure.TreasureClues.Matches(Services.Treasure.TreasureClueService.LoadAsync(x, y, dir, ct).GetAwaiter().GetResult(), name); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException or FormatException or ArgumentException or TaskCanceledException) { why = "DofusDB injoignable ou réponse invalide (" + e.GetType().Name + ")"; return Array.Empty<Services.Treasure.TreasureClue>(); }
        }
        Table ClueRow(Services.Treasure.TreasureClue c)
        {
            var r = new Table(s);
            r["x"] = DynValue.NewNumber(c.X); r["y"] = DynValue.NewNumber(c.Y); r["distance"] = DynValue.NewNumber(c.Distance); r["name"] = DynValue.NewString(c.Name);
            long? origin = Hunt()?.StartMapId ?? _launch.CurrentMap(_pid);
            long? mapId = _launch.MapIdAt(c.X, c.Y, origin is long m0 && m0 > 0 ? _launch.WorldMapOf(m0) : null);
            r["mapId"] = mapId is { } id ? DynValue.NewNumber(id) : DynValue.Nil;
            return r;
        }
        Fill(tr, "findClue", DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            var m = ClueMatches(a, out var why);
            if (m.Count == 0) { if (why.Length > 0) WarnSnow("treasure:findClue " + why); return DynValue.Nil; }
            return DynValue.NewTable(ClueRow(m[0]));
        }));
        Fill(tr, "findClues", DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            var m = ClueMatches(a, out var ignoredWhy);
            var t = new Table(s); int i = 1; foreach (var c in m) t[i++] = DynValue.NewTable(ClueRow(c));
            return DynValue.NewTable(t);
        }));

        // ---- démarrage : startHunt(niveau) -> 1 ok, 5 déjà en cours, 0 échec, -(minutes+1) délai avant une nouvelle chasse ----
        Fill(tr, "startHunt", DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            int level = N(a, 0);
            int myLevel = Math.Min(_world.Get(_pid).Level ?? 1, 200);
            int idx = level != 0 ? (int)(Math.Floor(myLevel / 20.0) + 1 - Math.Floor(level / 20.0)) : 1;
            // Tranche hors de portée (niveau demandé au-dessus du perso): Un bot tiers getskill -> -1 -> startHunt rend 0, SANS rien
            // envoyer. Jamais de repli sur la 1re compétence (chasse lancée dans une autre tranche). Au-delà du nombre de
            // compétences actives: PickSkill -> null -> NoSkill -> 0.
            if (idx < 1) return DynValue.NewNumber(0);
            long seq0 = _launch.SnowSeq;
            var outcome = UseElement(TreasureStartElement, -idx, ct);
            if (outcome is UseOutcome.NotFound or UseOutcome.NoSkill or UseOutcome.Denied or UseOutcome.InFight) return DynValue.NewNumber(0);
            WaitSeq(() => _launch.TreasureStartAnswer(_pid) is { } an && an.Seq > seq0
                       || _launch.LastTextInfo(_pid) is { MessageId: 437 } ti && ti.Seq > seq0, 9000);
            if (_launch.TreasureStartAnswer(_pid) is { } ans && ans.Seq > seq0)
            {
                if (ans.Result == 1 && !WaitSeq(() => Hunt() is { } h && h.Seq > seq0, 3000)) return DynValue.NewNumber(0);
                if (ans.Result is 1 or 5) Ok("trésor", Loc.T("L.Lua.SnowTreasureStarted"));
                return DynValue.NewNumber(ans.Result);
            }
            if (_launch.LastTextInfo(_pid) is { MessageId: 437 } t437 && t437.Seq > seq0)
            {
                int min = t437.Params.Count > 0 && int.TryParse(t437.Params[0], out var m) ? m : 0;
                Warn("trésor", Loc.Fmt("L.Lua.SnowTreasureCooldown", min + 1));
                return DynValue.NewNumber(-(min + 1));
            }
            return DynValue.NewNumber(0);
        }));

        Fill(tr, "startLegendary", DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            if (Hunt() is not null) return DynValue.False;
            long floor = _launch.SnowSeq;
            return DynValue.NewBoolean(_launch.TreasureSendLegendary(_pid, N(a, 0))
                && WaitSeq(() => Hunt() is { } h && h.Seq > floor, 12000));
        }));

        // ---- drapeaux / creuser ----
        Fill(tr, "huntFlag", DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            if (Hunt() is not { } h) return DynValue.False;
            long seq0 = _launch.SnowSeq;
            if (!_launch.TreasureSendFlag(_pid, h.Flags.Count, h.QuestType)) return DynValue.False;
            WaitSeq(() => Hunt() is { } h2 && h2.Seq > seq0 || _launch.TreasureFlagAnswer(_pid) is { } fa && fa.Seq > seq0, TreasureAnswerMs);
            if (Hunt() is { } h3 && h3.Seq > seq0) return DynValue.True;
            // Réponse explicite : 1 = drapeau posé (l'état suit), sinon refus.
            if (_launch.TreasureFlagAnswer(_pid) is { } fa2 && fa2.Seq > seq0 && fa2.Result == 1)
            {
                WaitSeq(() => Hunt() is { } h4 && h4.Seq > seq0, 2000);   // laisse arriver l'état mis à jour
                return DynValue.True;
            }
            return DynValue.False;
        }));
        DynValue Dig()
        {
            ThrowIfStopping(ct);
            _treasureValidated = -1;
            if (Hunt() is not { } h) return DynValue.False;
            long seq0 = _launch.SnowSeq;
            bool fightStep = StepAt(-1)?.Kind == "fight";
            if (!_launch.TreasureSendDig(_pid, h.QuestType)) return DynValue.False;
            if (fightStep) return DynValue.True;   // le combat final démarre
            WaitSeq(() => _launch.TreasureDigAnswer(_pid) is { } d && d.Seq > seq0, TreasureAnswerMs);
            // treasure.validated (MITM.vb, réponse au creusage): 1 si result == 1, sinon 0; -1 = pas de réponse.
            // Le code brut reste lisible via treasure:lastDigResult().
            if (_launch.TreasureDigAnswer(_pid) is { } dig && dig.Seq > seq0) _treasureValidated = dig.Result == 1 ? 1 : 0;
            return DynValue.NewBoolean(_treasureValidated == 1);
        }
        Fill(tr, "huntDig", DynValue.NewCallback((_, a) => Dig()));
        Fill(tr, "huntDigTry", DynValue.NewCallback((_, a) =>
        {
            if (Dig().CastToBool()) return DynValue.True;
            var last = Hunt()?.Flags.LastOrDefault();
            return DynValue.NewBoolean(last is not null && last.State == 1 && last.MapId == (_launch.CurrentMap(_pid) ?? 0));
        }));
        Fill(tr, "lastDigResult", DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.TreasureDigAnswer(_pid)?.Result ?? -1)));
        Fill(tr, "huntClear", DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            if (Hunt() is not { } h || h.Flags.Count == 0) return DynValue.True;
            long seq0 = _launch.SnowSeq;
            if (!_launch.TreasureSendFlagRemove(_pid, 0, h.QuestType)) return DynValue.False;
            return DynValue.NewBoolean(WaitSeq(() => Hunt() is { } h2 && h2.Seq > seq0, 2000));
        }));

        // ---- lectures ----
        Fill(tr, "getStartMapId", DynValue.NewCallback((_, a) =>
            DynValue.NewNumber(Hunt() is { } h ? (h.Flags.Count > 0 ? h.Flags[^1].MapId : h.StartMapId) : 0)));
        Fill(tr, "getStepCount", DynValue.NewCallback((_, a) => DynValue.NewNumber(Hunt()?.TotalStepCount ?? 0)));
        Fill(tr, "isHunting", DynValue.NewCallback((_, a) => DynValue.NewBoolean(Hunt() is not null)));
        DynValue ready = DynValue.NewCallback((_, a) =>
            DynValue.NewBoolean(Hunt() is { } h && (h.Flags.Count == h.TotalStepCount || StepAt(-1)?.Kind == "fight")));
        Fill(tr, "isReadyToDig", ready);
        Fill(tr, "FuAJ2GhY5548587", ready);   // alias obfusqué public d'un bot tiers
        Fill(tr, "isPhorreurStep", DynValue.NewCallback((_, a) => DynValue.NewBoolean(StepAt(-1)?.Kind == "hint")));
        Fill(tr, "isFightStep", DynValue.NewCallback((_, a) => DynValue.NewBoolean(StepAt(-1)?.Kind == "fight")));
        Fill(tr, "isHintStep", DynValue.NewCallback((_, a) => DynValue.NewBoolean(StepAt(-1)?.Kind == "poi")));
        Fill(tr, "stepType", DynValue.NewCallback((_, a) => DynValue.NewString(StepAt(N(a, 0, -1))?.Kind ?? "")));
        Fill(tr, "isPhorreurInMap", DynValue.NewCallback((_, a) =>
        {
            var st = StepAt(-1);
            if (st?.Kind != "hint") return DynValue.False;
            return DynValue.NewBoolean(_world.Get(_pid).Actors.Any(x => x.Kind == MapActorKind.TreasureHint && x.NpcId == st.NpcId));
        }));
        Fill(tr, "getNextFlagDirection", DynValue.NewCallback((_, a) =>
        {
            var st = StepAt(N(a, 0, -1));
            return DynValue.NewString(st is { Kind: "poi" or "hint" or "direction" } ? DirName(st.Direction) : "");
        }));
        Fill(tr, "getNextFlagMapCount", DynValue.NewCallback((_, a) =>
            DynValue.NewNumber(StepAt(N(a, 0, -1)) is { Kind: "direction" } step ? step.MapCount : 0)));
        DynValue flagName = DynValue.NewCallback((_, a) =>
        {
            var st = StepAt(N(a, 0, -1));
            string? n = st?.Kind switch
            {
                "poi" => _launch.GameNames.PoiName(st.PoiLabelId),
                "hint" => _launch.GameNames.NpcName(st.NpcId),
                _ => null,
            };
            return DynValue.NewString(n ?? "");
        });
        Fill(tr, "GetNextFlagName", flagName);
        Fill(tr, "getNextFlagName", flagName);
        Fill(tr, "getNextFlagId", DynValue.NewCallback((_, a) =>
        {
            var st = StepAt(N(a, 0, -1));
            return DynValue.NewNumber(st?.Kind switch { "poi" => st.PoiLabelId, "hint" => st.NpcId, _ => -1 });
        }));
        Fill(tr, "getPos", DynValue.NewCallback((_, a) =>
        {
            long id = (long)(At(a, tr, 0).CastToNumber() ?? 0);
            return DynValue.NewString(_launch.MapCoordOf(id) is { } c ? $"[{c.X},{c.Y}]" : "");
        }));

        // ---- abandon ----
        Fill(tr, "quit", DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            if (Hunt() is not { } h) return DynValue.True;
            _treasureGivingUp = true;
            _treasureCooldownMin = 0;   // pas de délai périmé d'un refus précédent
            long seq0 = _launch.SnowSeq;
            if (!_launch.TreasureSendGiveUp(_pid, h.QuestType)) { _treasureGivingUp = false; return DynValue.False; }
            WaitSeq(() => _launch.TreasureFinishedSeq(_pid) > seq0 || _launch.LastTextInfo(_pid) is { } ti && ti.Seq > seq0, 5000);
            if (_launch.TreasureFinishedSeq(_pid) > seq0) { _treasureGivingUp = false; _treasureCooldownMin = 0; return DynValue.True; }
            // Délai avant abandon (message système): Un bot tiers se déconnectait N+1 min; ici on le SIGNALE (treasure:giveUpCooldown).
            if (_launch.LastTextInfo(_pid) is { } t && t.Seq > seq0 && t.Params.Count > 0 && int.TryParse(t.Params[0], out int _cooldown))   // délai = 1er paramètre numérique
            {
                _treasureCooldownMin = (t.Params.Count > 0 && int.TryParse(t.Params[0], out var m) ? m : 0) + 1;
                Warn("trésor", Loc.Fmt("L.Lua.SnowTreasureGiveUpWait", _treasureCooldownMin));
            }
            return DynValue.False;
        }));
        Fill(tr, "quitNow", DynValue.NewCallback((_, a) =>
        {
            if (Hunt() is not { } h) return DynValue.True;
            _treasureGivingUp = false;
            return DynValue.NewBoolean(_launch.TreasureSendGiveUp(_pid, h.QuestType));
        }));
        Fill(tr, "isGivingUp", DynValue.NewCallback((_, a) => DynValue.NewBoolean(_treasureGivingUp)));
        Fill(tr, "giveUpCooldown", DynValue.NewCallback((_, a) => DynValue.NewNumber(_treasureCooldownMin)));

        // ---- rapports / résolveur externe (vides ou non portés chez GivreClone) ----
        Fill(tr, "lastHuntReport", UnsupportedLua("treasure:lastHuntReport"));
        foreach (var n in new[] { "sendLastHuntReport", "printLastHuntReport", "treasureInactivity" }) Fill(tr, n, UnsupportedLua("treasure:" + n));
        Fill(tr, "gdPggXxViU", UnsupportedLua("treasure:gdPggXxViU"));

        // ---- champs publics un bot tiers (lecture directe): __index dynamique, puis repli de noms tolérants ----
        var mt = tr.MetaTable ?? new Table(s);
        mt["__index"] = DynValue.NewCallback((ctx, a) =>
        {
            if (a.Count >= 2 && a[1].Type == DataType.String)
                switch (a[1].String)
                {
                    case "treasure": return State();
                    case "_isStarted": case "_isHunting": return DynValue.NewBoolean(Hunt() is not null);
                    case "giveUp": return DynValue.NewBoolean(_treasureGivingUp);
                    case "nextMapId": throw new ScriptRuntimeException("treasure.nextMapId : API non implémentée dans GivreClone. Aucune action effectuée.");
                    case "validated": return DynValue.NewNumber(_treasureValidated);
                    case "treasureHuntRequestAnswerMessage":   // dernière réponse au démarrage { result }
                    {
                        var t = new Table(s);
                        t["result"] = DynValue.NewNumber(_launch.TreasureStartAnswer(_pid)?.Result ?? 0);
                        return DynValue.NewTable(t);
                    }
                    case "giveUpRequest":
                    {
                        var t = new Table(s);
                        t["questType"] = DynValue.NewNumber(Hunt()?.QuestType ?? 0);
                        return DynValue.NewTable(t);
                    }
                }
            return SnowIndex(ctx, a);
        });
        tr.MetaTable = mt;
    }
}
