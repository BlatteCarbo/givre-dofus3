using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ==================================================================================================================
// Lot I — quêtes (quest:), sous-zones/zones (map:), données statiques (data:, job:name, inventory:itemLevel/itemWeight) et
// équipement (inventory:equip/unequip/stuff/destuff). Verbes liés AVANT DoString (Bind -> BindLotI) : ils existent au top-level
// (état vide avant l'entrée en jeu -> false / -1 / {}). Callbacks AOT-safe (DynValue.NewCallback), appel colon ET point.
// Envois synchrones puis sondage (WaitUntil, 80 ms) sur l'état moteur : exécuté UNIQUEMENT sur le thread du script.
// ==================================================================================================================
public sealed partial class GivreLuaHost
{
    private const int EquipWaitMs = 3000;
    private const int QuestAutoRefreshCooldownMs = 30000;
    // Dernière auto-actualisation (currentStep/objectives sans détails) par quête : évite de repayer 3 s à chaque appel si le
    // serveur ne répond pas à QuestStepInformationRequest (champ obfusqué omis, DEFERRED D7).
    // Clé (quête, étape) : une NOUVELLE étape a toujours droit à sa demande, même < 30 s après celle de l'étape précédente.
    private readonly ConcurrentDictionary<(int Quest, int Step), long> _questAutoRefreshAt = new();

    private void BindLotI(Script s, CancellationToken ct)
    {
        var names = _launch.GameNames;
        static DynValue Str(string? v) => DynValue.NewString(v ?? "");

        // ---------------------------------------------------------------- quest:
        var q = new Table(s);
        q["loaded"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.QuestsLoaded(_pid)));
        // Un bot tiers initialize demande la liste (QuestListRequest): message absent du protocole courant (DEFERRED D1) ->
        // alias de loaded() (snapshot de connexion + quêtes suivies + informations d'étape).
        q["initialize"] = q.Get("loaded");
        q["isFinished"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.IsQuestFinished(_pid, ArgN(a, 0))));
        q["finished"] = DynValue.NewCallback((_, a) => BuildNums(s, _launch.FinishedQuests(_pid)));
        q["active"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.ActiveQuests(_pid), (r, x) =>
        {
            r["id"] = DynValue.NewNumber(x.Id);
            r["step"] = DynValue.NewNumber(x.StepId > 0 ? x.StepId : -1);
            r["name"] = Str(names.QuestName(x.Id));
        }));
        q["activeQuests"] = DynValue.NewCallback((_, a) => BuildNums(s, _launch.ActiveQuests(_pid).Select(x => x.Id)));
        q["isActive"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.ActiveQuest(_pid, ArgN(a, 0)) is not null));
        q["questActive"] = q.Get("isActive");
        q["currentStep"] = DynValue.NewCallback((_, a) =>
        {
            int id = ArgN(a, 0);
            var cur = _launch.ActiveQuest(_pid, id);
            if (cur is null) return DynValue.NewNumber(-1);
            if (cur.StepId <= 0) cur = QuestWithDetails(id, ct) ?? cur;   // étape inconnue -> UNE demande d'information
            return DynValue.NewNumber(cur.StepId > 0 ? cur.StepId : -1);
        });
        q["questCurrentStep"] = q.Get("currentStep");
        q["objectives"] = DynValue.NewCallback((_, a) =>
        {
            var cur = QuestWithDetails(ArgN(a, 0), ct);
            return BuildList(s, cur?.Objectives ?? (IReadOnlyList<QuestReader.Objective>)Array.Empty<QuestReader.Objective>(), (r, o) =>
            {
                r["id"] = DynValue.NewNumber(o.Id); r["reached"] = DynValue.NewBoolean(o.Reached);
                r["current"] = DynValue.NewNumber(o.Current); r["max"] = DynValue.NewNumber(o.Max);
            });
        });
        //: nil si la quête n'est pas en cours (et nil aussi si ses objectifs restent inconnus). ⚠ objective_reached = true signifie FAIT (un bot tiers/Dofus 2:
        // objectiveStatus = true signifiait « encore à faire ») — les alias ci-dessous traduisent correctement.
        // nil = INCONNU : quête pas en cours, OU objectifs non détaillés même après la demande d'information (jamais {} dans ce
        // cas : `#r == 0` voudrait dire « étape finie »).
        q["doneObjectives"] = DynValue.NewCallback((_, a) =>
            QuestWithDetails(ArgN(a, 0), ct) is { HasDetails: true } cur ? BuildNums(s, cur.Objectives.Where(o => o.Reached).Select(o => o.Id)) : DynValue.Nil);
        q["questDoneObjectives"] = q.Get("doneObjectives");
        q["remainingObjectives"] = DynValue.NewCallback((_, a) =>
            QuestWithDetails(ArgN(a, 0), ct) is { HasDetails: true } cur ? BuildNums(s, cur.Objectives.Where(o => !o.Reached).Select(o => o.Id)) : DynValue.Nil);
        q["questRemainingObjectives"] = q.Get("remainingObjectives");
        q["questObjectiveDone"] = DynValue.NewCallback((_, a) =>
        {
            int o = ArgN(a, 1);
            return DynValue.NewBoolean(QuestWithDetails(ArgN(a, 0), ct) is { } cur && cur.Objectives.Any(x => x.Id == o && x.Reached));
        });
        q["refresh"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(RefreshQuest(ArgN(a, 0), TimeoutMsArg(a, 1, 3000), ct)));
        q["validateObjective"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(ValidateQuestObjective(ArgN(a, 0), ArgN(a, 1), TimeoutMsArg(a, 2, 5000), ct)));
        q["name"] = DynValue.NewCallback((_, a) => Str(names.QuestName(ArgN(a, 0))));
        q["stepName"] = DynValue.NewCallback((_, a) => Str(names.QuestStepName(ArgN(a, 0))));
        q["npcName"] = DynValue.NewCallback((_, a) => Str(names.NpcName(ArgN(a, 0))));
        q["monsterName"] = DynValue.NewCallback((_, a) => Str(names.MonsterName(ArgN(a, 0))));
        s.Globals["quest"] = DynValue.NewTable(q);

        // ---------------------------------------------------------------- data: (lectures statiques pures, sans gate)
        // ⚠ Un script qui assigne un global `data = …` masque ce module POUR LUI SEUL (utiliser `local data` pour ses données).
        var d = new Table(s);
        d["monsterName"] = q.Get("monsterName");
        d["npcName"] = q.Get("npcName");
        d["itemName"] = DynValue.NewCallback((_, a) => Str(names.ItemName(ArgN(a, 0))));
        d["spellName"] = DynValue.NewCallback((_, a) => Str(names.SpellName(ArgN(a, 0))));
        d["jobName"] = DynValue.NewCallback((_, a) => Str(names.JobName(ArgN(a, 0))));
        d["questName"] = q.Get("name");
        d["subAreaName"] = DynValue.NewCallback((_, a) => Str(names.SubAreaName(ArgN(a, 0))));
        d["areaName"] = DynValue.NewCallback((_, a) => Str(names.AreaName(ArgN(a, 0))));
        d["itemLevel"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(names.ItemLevel(ArgN(a, 0))));
        d["itemWeight"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(names.ItemWeight(ArgN(a, 0))));
        s.Globals["data"] = DynValue.NewTable(d);

        // ---------------------------------------------------------------- job:name
        if (s.Globals.Get("job").Type == DataType.Table) s.Globals.Get("job").Table["name"] = d.Get("jobName");

        // ---------------------------------------------------------------- map: sous-zones / zones
        //: currentSubArea = NOM, currentSubAreaId = id (un bot tiers rendait à tort l'id de zone; currentArea
        // rendait un id — corrigés ici). mapId omis/0 = carte courante (valeur live), sinon table statique.
        if (s.Globals.Get("map").Type == DataType.Table)
        {
            var map = s.Globals.Get("map").Table;
            map["currentSubAreaId"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(SubAreaFor(ArgL(a, 0))));
            map["currentSubArea"] = DynValue.NewCallback((_, a) => Str(names.SubAreaName(SubAreaFor(ArgL(a, 0)))));
            map["subAreaName"] = DynValue.NewCallback((_, a) => { int id = ArgN(a, 0); return Str(names.SubAreaName(id > 0 ? id : SubAreaFor(0))); });
            map["currentAreaId"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(AreaFor(ArgL(a, 0))));
            map["currentArea"] = DynValue.NewCallback((_, a) => { int area = AreaFor(ArgL(a, 0)); return Str(area >= 0 ? names.AreaName(area) : null); });
            // areaName(areaId) : 0 = Amakna (VALIDE). Sans argument = zone courante.
            map["areaName"] = DynValue.NewCallback((_, a) =>
            {
                int area = ArgV(a, 0).IsNil() ? AreaFor(0) : ArgN(a, 0);
                return Str(area >= 0 ? names.AreaName(area) : null);
            });
        }

        // ---------------------------------------------------------------- inventory: niveau/poids + équipement
        if (s.Globals.Get("inventory").Type == DataType.Table)
        {
            var inv = s.Globals.Get("inventory").Table;
            inv["itemLevel"] = d.Get("itemLevel");
            inv["itemLevelId"] = d.Get("itemLevel");
            inv["getLevel"] = d.Get("itemLevel");
            inv["itemWeight"] = d.Get("itemWeight");
            if (!inv.Get("typeId").IsNil()) inv["itemTypeId"] = inv.Get("typeId");
            // itemPosition(gid): position d'équipement d'un exemplaire (équipé en priorité; 63 = sac), -1 si absent.
            inv["itemPosition"] = DynValue.NewCallback((_, a) =>
            {
                long gid = ArgL(a, 0);
                var items = _launch.Inventory.Items(_pid).Where(i => i.Gid == gid).ToList();
                if (items.Count == 0) return DynValue.NewNumber(-1);
                var eq = items.Where(i => i.Position != 63).OrderBy(i => i.Position).FirstOrDefault();
                return DynValue.NewNumber(eq.Uid != 0 ? eq.Position : 63);
            });
            inv["equip"] = DynValue.NewCallback((_, a) =>
            {
                int pos = ArgV(a, 1).IsNil() ? -1 : ArgN(a, 1);
                return DynValue.NewBoolean(EquipGid(ArgL(a, 0), pos, ct));
            });
            inv["equipItem"] = inv.Get("equip");
            inv["equipUid"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(EquipUid(ArgN(a, 0), ArgN(a, 1), ct)));
            inv["equipItemuid"] = inv.Get("equipUid");
            inv["unequip"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(UnequipPos(ArgN(a, 0), ct)));
            inv["unequipItem"] = DynValue.NewCallback((_, a) =>
            {
                long gid = ArgL(a, 0);
                var it = _launch.Inventory.Equipped(_pid).FirstOrDefault(i => i.Gid == gid);
                if (it.Uid == 0) { Warn("inventaire", Loc.Fmt("L.Lua.EquipNoItem", _launch.ItemDisplayName(gid), gid)); return DynValue.False; }
                return DynValue.NewBoolean(UnequipPos(it.Position, ct));
            });
            inv["stuff"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(Stuff(DelayArg(a, 0), ct)));
            inv["destuff"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(Destuff(DelayArg(a, 0), ct)));
        }
    }

    private static int TimeoutMsArg(CallbackArguments a, int i, int def) { int v = ArgN(a, i); return v > 0 ? Math.Min(v, 120000) : def; }
    private static int DelayArg(CallbackArguments a, int i) { var v = ArgV(a, i); return v.IsNil() ? 500 : Math.Clamp(ArgN(a, i), 0, 60000); }

    // ------------------------------------------------------------------ sous-zones
    private int SubAreaFor(long mapId)
    {
        long cur = _launch.CurrentMap(_pid) ?? 0;
        return mapId <= 0 || mapId == cur ? _launch.CurrentSubArea(_pid) : _launch.SubAreaOf(mapId);
    }

    private int AreaFor(long mapId) { int sub = SubAreaFor(mapId); return sub > 0 ? _launch.GameNames.AreaOfSubArea(sub) : -1; }

    // ------------------------------------------------------------------ quêtes
    // Quête en cours, avec UNE demande d'information si ses détails (étape/objectifs) sont inconnus (au plus une par couple
    // (quête, étape) toutes les 30 s). null si la quête n'est pas en cours. Peut rendre un enregistrement SANS détails (serveur
    // muet / cooldown) : les appelants testent HasDetails.
    private QuestReader.Quest? QuestWithDetails(int id, CancellationToken ct)
    {
        var cur = _launch.ActiveQuest(_pid, id);
        if (cur is null || cur.HasDetails) return cur;
        long now = Environment.TickCount64;
        var key = (id, cur.StepId);
        if (_questAutoRefreshAt.TryGetValue(key, out var last) && now - last < QuestAutoRefreshCooldownMs) return cur;
        _questAutoRefreshAt[key] = now;
        RefreshQuest(id, 3000, ct);
        return _launch.ActiveQuest(_pid, id) ?? cur;
    }

    private bool RefreshQuest(int id, int timeoutMs, CancellationToken ct)
    {
        ThrowIfStopping(ct);
        if (id <= 0) return false;
        long t0 = Environment.TickCount64;
        bool ok = _launch.SendQuestStepInformation(_pid, id) && WaitUntil(() => _launch.QuestInfoSince(_pid, id, t0), timeoutMs, ct);
        if (!ok) Warn("quête", Loc.Fmt("L.Lua.QuestInfoFail", id));
        return ok;
    }

    private bool ValidateQuestObjective(int questId, int objectiveId, int timeoutMs, CancellationToken ct)
    {
        ThrowIfStopping(ct);
        if (questId <= 0 || objectiveId <= 0) return false;
        if (_launch.ActiveQuest(_pid, questId) is { } cur && cur.Objectives.Any(o => o.Id == objectiveId && o.Reached)) return true;   // déjà fait
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) { Warn("quête", Loc.T("L.Lua.NoPermGather")); return false; }
        long t0 = Environment.TickCount64;
        if (!_launch.SendQuestObjectiveValidation(_pid, questId, objectiveId))
        {
            Warn("quête", Loc.Fmt("L.Lua.QuestObjectiveFail", questId, objectiveId));
            return false;
        }
        bool ok = WaitUntil(() => _launch.QuestObjectiveConfirmedSince(_pid, questId, objectiveId, t0), timeoutMs, ct);
        if (ok) Ok("quête", Loc.Fmt("L.Lua.QuestObjectiveOk", questId, objectiveId));
        else Warn("quête", Loc.Fmt("L.Lua.QuestObjectiveFail", questId, objectiveId));
        return ok;
    }

    // ------------------------------------------------------------------ équipement
    // Garde commune des changements d'équipement (message dans la console du script ; le moteur re-vérifie au point d'action).
    private bool EquipAllowed()
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.Gather)) { Warn("inventaire", Loc.T("L.Lua.NoPermGather")); return false; }
        if (_launch.IsInFight(_pid)) { Warn("inventaire", Loc.T("L.Lua.EquipInFight")); return false; }
        return true;
    }

    // Équipement standard et compagnon, selon les emplacements autorisés par les données du jeu.
    private List<int> EquipSlots(long gid) => _launch.GameNames.EquipPositions((int)gid).Where(p => p is >= 0 and <= 15 or 28).ToList();

    /// <summary>Équipe un exemplaire DU SAC de `gid` en `pos` (pos &lt; 0 = 1er emplacement libre, sinon le 1er possible = échange).</summary>
    private bool EquipGid(long gid, int pos, CancellationToken ct, bool verbose = true)
    {
        ThrowIfStopping(ct);
        string name = _launch.ItemDisplayName(gid);
        if (!EquipAllowed()) return false;
        if (pos >= 0 && _launch.Inventory.ItemAt(_pid, pos) is { } there && there.Gid == gid) return true;   // déjà en place
        if (_launch.Inventory.FirstBagUidByGid(_pid, gid) is not int uid)
        {
            if (verbose) Warn("inventaire", Loc.Fmt("L.Lua.EquipNoItem", name, gid));
            return false;
        }
        if (pos < 0)
        {
            var slots = EquipSlots(gid);
            if (slots.Count == 0) { if (verbose) Warn("inventaire", Loc.Fmt("L.Lua.EquipNoSlot", name, gid)); return false; }
            pos = slots.FirstOrDefault(p => _launch.Inventory.ItemAt(_pid, p) is null, slots[0]);
        }
        return SendEquipAndWait(uid, gid, pos, name, ct, verbose);
    }

    private bool EquipUid(int uid, int pos, CancellationToken ct)
    {
        ThrowIfStopping(ct);
        if (!EquipAllowed()) return false;
        var it = _launch.Inventory.Items(_pid).FirstOrDefault(i => i.Uid == uid);
        if (it.Uid == 0 || uid == 0) { Warn("inventaire", Loc.Fmt("L.Lua.EquipNoItem", $"uid {uid}", "?")); return false; }
        string name = _launch.ItemDisplayName(it.Gid);
        if (it.Position == pos) return true;   // déjà en place
        return SendEquipAndWait(uid, it.Gid, pos, name, ct, true);
    }

    // Succès = un objet de ce gid à `pos` ET aucune ObjectErrorEvent depuis l'envoi. Test sur (gid, position) et non sur l'uid :
    // équiper 1 exemplaire d'une pile de 2+ crée un nouvel uid (ObjectAddedEvent).
    private bool SendEquipAndWait(int uid, long gid, int pos, string name, CancellationToken ct, bool verbose)
    {
        long t0 = Environment.TickCount64;
        bool sent = _launch.SendObjectSetPosition(_pid, uid, pos);
        bool InPlace() => _launch.Inventory.Items(_pid).Any(i => i.Gid == gid && i.Position == pos);
        bool ok = sent && WaitUntil(() => InPlace() || _launch.ObjectErrorSince(_pid, t0), EquipWaitMs, ct)
                  && InPlace() && !_launch.ObjectErrorSince(_pid, t0);
        if (ok) Ok("inventaire", Loc.Fmt("L.Lua.EquipOk", name, gid, pos));
        else if (verbose || sent) Warn("inventaire", Loc.Fmt("L.Lua.EquipFail", name, gid));
        return ok;
    }

    private bool UnequipPos(int pos, CancellationToken ct)
    {
        ThrowIfStopping(ct);
        // Emplacement d'équipement uniquement : 63 (sac) ou négatif = rien à déséquiper, aucun envoi (paquet anormal sinon).
        if (pos < 0 || pos == 63) { Warn("inventaire", Loc.Fmt("L.Lua.UnequipFail", pos)); return false; }
        if (!EquipAllowed()) return false;
        if (_launch.Inventory.ItemAt(_pid, pos) is not { } it || it.Position == 63) { Warn("inventaire", Loc.Fmt("L.Lua.UnequipFail", pos)); return false; }
        long t0 = Environment.TickCount64;
        bool sent = _launch.SendObjectSetPosition(_pid, it.Uid, 63);
        bool ok = sent && WaitUntil(() => _launch.Inventory.ItemAt(_pid, pos) is null || _launch.ObjectErrorSince(_pid, t0), EquipWaitMs, ct)
                  && _launch.Inventory.ItemAt(_pid, pos) is null && !_launch.ObjectErrorSince(_pid, t0);
        if (ok) Ok("inventaire", Loc.Fmt("L.Lua.UnequipOk", pos));
        else Warn("inventaire", Loc.Fmt("L.Lua.UnequipFail", pos));
        return ok;
    }

    /// <summary>Remplit les emplacements LIBRES avec les objets du sac (niveau &lt;= niveau du perso, plus haut niveau d'abord).
    /// Jamais d'échange ; un gid déjà porté n'est pas ré-équipé (le jeu interdit les doublons). Renvoie le nombre équipé.</summary>
    private int Stuff(int delayMs, CancellationToken ct)
    {
        ThrowIfStopping(ct);
        if (!EquipAllowed()) return 0;
        int level = _world.Get(_pid).Level ?? 0;
        var names = _launch.GameNames;
        var worn = _launch.Inventory.Equipped(_pid).Select(i => i.Gid).ToHashSet();
        var taken = new HashSet<int>();
        for (int p = 0; p <= 15; p++) if (_launch.Inventory.ItemAt(_pid, p) is not null) taken.Add(p);
        var candidates = _launch.Inventory.Items(_pid)
            .Where(i => i.Position == 63 && i.Qty > 0 && !worn.Contains(i.Gid))
            .Select(i => i.Gid).Distinct()
            .Select(g => (Gid: g, Level: names.ItemLevel((int)g), Slots: names.EquipPositions((int)g)))
            .Where(c => c.Level >= 0 && c.Level <= level && LuaCompat.AutoStuffEligible(c.Slots))   // exclut 22/69 et >= 16 (DEFERRED D8)
            .OrderByDescending(c => c.Level).ThenBy(c => c.Gid)
            .ToList();
        int done = 0; bool first = true;
        foreach (var c in candidates)
        {
            ThrowIfStopping(ct);
            if (_launch.IsInFight(_pid)) break;
            int slot = c.Slots.FirstOrDefault(p => !taken.Contains(p), -1);
            if (slot < 0) continue;
            taken.Add(slot);   // réservé même en cas de refus (critères non remplis) : jamais d'échange
            if (!first) InterruptibleDelay(delayMs, ct);
            first = false;
            if (EquipGid(c.Gid, slot, ct, verbose: false)) done++;
            _launch.NoteProgress(_pid);
        }
        Ok("inventaire", Loc.Fmt("L.Lua.StuffDone", done));
        return done;
    }

    /// <summary>Déséquipe tout ce qui est porté en 0..15 (délai entre deux objets). Renvoie le nombre déséquipé.</summary>
    private int Destuff(int delayMs, CancellationToken ct)
    {
        ThrowIfStopping(ct);
        if (!EquipAllowed()) return 0;
        var positions = _launch.Inventory.Equipped(_pid).Where(i => i.Position >= 0 && i.Position <= 15).Select(i => i.Position).Distinct().ToList();
        int done = 0; bool first = true;
        foreach (int p in positions)
        {
            ThrowIfStopping(ct);
            if (_launch.IsInFight(_pid)) break;
            if (!first) InterruptibleDelay(delayMs, ct);
            first = false;
            if (UnequipPos(p, ct)) done++;
            _launch.NoteProgress(_pid);
        }
        Ok("inventaire", Loc.Fmt("L.Lua.DestuffDone", done));
        return done;
    }
}
