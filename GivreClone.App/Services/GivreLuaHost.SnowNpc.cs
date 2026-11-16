using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ==================================================================================================================
// Compatibilité tiers — npc: (npc.vb), global:getReplies, chat: (Chat.vb), quest: (textes), character: (character.vb),
// mount: (mount.vb), job:, d2data: (d2data.vb).
// Identifiants de PNJ: Un bot tiers raisonne en ids de MODÈLE (522 = banquier…), GivreClone en ids d'ACTEUR (négatifs). ResolveNpc
// accepte les deux : &lt; 0 = acteur tel quel ; 0 = 1er PNJ de la carte ; &gt; 0 = acteur dont le modèle vaut cet id (repli : id d'acteur).
// npc:reply(n): n &lt; 0 = n-ième réponse (1-based), n &gt; 0 = identifiant de réponse. replyNpc(i) reste un INDEX.
// ==================================================================================================================
public sealed partial class GivreLuaHost
{
    private const int NpcReplyWaitMs = 5000, QuestInitWaitMs = 3000;
    private static readonly int[] BankerTemplates = { 522, 520, 100 };

    /// <summary>Acteur (id contextuel) d'un PNJ désigné par un script (voir en-tête). 0 = introuvable.</summary>
    private long ResolveNpc(long id)
    {
        if (id < 0) return id;
        var npcs = _world.Get(_pid).Actors.Where(a => a.Kind is MapActorKind.Npc or MapActorKind.TreasureHint).ToList();
        if (id == 0) return npcs.FirstOrDefault(a => a.Kind == MapActorKind.Npc)?.ActorId ?? 0;   // 1er vrai PNJ (pas un indice de chasse)
        return npcs.FirstOrDefault(a => a.NpcId == id)?.ActorId ?? 0;   // modèle absent de la carte -> introuvable (échec immédiat)
    }

    // Attend (≤ ms) la question SUIVANTE ou la fermeture du dialogue. true = nouvelle question affichée.
    private bool WaitNextNpcQuestion(long seq0, int ms, CancellationToken ct)
    {
        WaitUntil(() => _launch.NpcDialogSeq(_pid) != seq0, ms, ct, pump: true);
        return _launch.NpcDialogSeq(_pid) != seq0 && _launch.NpcDialog(_pid) is not null;
    }

    private DynValue NpcQuestionTable(Script s)
    {
        if (_launch.NpcDialog(_pid) is not { } d) return DynValue.Nil;
        var t = new Table(s);
        t["messageId"] = DynValue.NewNumber(d.MessageId);
        t["params"] = BuildStrings(s, d.Params);
        t["replies"] = BuildNums(s, d.ReplyIds);
        t["text"] = DynValue.NewString(_launch.GameNames.NpcMessageText(d.MessageId, d.Params) ?? "");
        return DynValue.NewTable(t);
    }

    private void BindSnowNpc(Script s, CancellationToken ct)
    {
        var npc = GetOrCreateTable(s, "npc");
        var g = GetOrCreateTable(s, "global");
        long L(CallbackArguments a, int i) => (long)(At(a, npc, i).CastToNumber() ?? 0);
        int N(CallbackArguments a, int i) => (int)(At(a, npc, i).CastToNumber() ?? 0);

        // npc(npcId [, action=3]) : 1 boutique PNJ, 3 parler, 5 HDV vente, 6 HDV achat.
        // Pour 1, exiger l'ExchangeNpcShopStartedEvent (et pas juste une ouverture générique de dialogue).
        Fill(npc, "npc", DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            long actor = ResolveNpc(L(a, 0));
            int action = At(a, npc, 1).IsNil() ? 3 : N(a, 1);
            if (actor == 0) { Warn("pnj", Loc.T("L.Lua.SnowNpcNotFound")); return DynValue.False; }
            if (action is 5 or 6 && !LicenseService.HasPermission(LicenseService.Perm.Hdv)) { Warn("hdv", Loc.T("L.Lua.SnowNoPermHdv")); return DynValue.False; }
            bool ok = action == 1
                ? _launch.ShopOpenAsync(_pid, actor, ct).GetAwaiter().GetResult()
                : _launch.NpcActionAsync(_pid, actor, action, ct).GetAwaiter().GetResult();
            if (!ok) Warn("pnj", Loc.Fmt("L.Lua.SnowNpcNoAnswer", actor, action));
            return DynValue.NewBoolean(ok);
        }));
        // npcBank([npcId]) : ouvre la banque (-1 / 0 / nil = banquier de la carte).
        Fill(npc, "npcBank", DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            long id = L(a, 0);
            long actor = -20000;
            if (id > 0) actor = ResolveNpc(id);
            else if (id < -1) actor = id;
            else if (_world.Get(_pid).Actors.FirstOrDefault(x => x.Kind == MapActorKind.Npc && BankerTemplates.Contains(x.NpcId)) is { } banker) actor = banker.ActorId;
            if (actor == 0) { Warn("pnj", Loc.T("L.Lua.SnowNpcNotFound")); return DynValue.False; }   // modèle absent de la carte
            bool ok = _launch.BankOpenAsync(_pid, 0, true, ct, actor).GetAwaiter().GetResult();
            if (!ok && actor == -20000) ok = _launch.BankOpenAsync(_pid, 0, true, ct, -20001).GetAwaiter().GetResult();
            if (ok) Ok("banque", Loc.Fmt("L.Lua.BankOpened", _launch.Bank.StackCount(_pid), Math.Max(0, _launch.BankKamas(_pid))));
            else Warn("banque", Loc.T("L.Lua.BankOpenFailed"));
            return DynValue.NewBoolean(ok);
        }));
        // reply(n) : n < 0 = n-ième réponse visible, n > 0 = identifiant de réponse. true = une nouvelle question est arrivée.
        Fill(npc, "reply", DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            int n = N(a, 0);
            long seq0 = _launch.NpcDialogSeq(_pid);
            bool sent = n < 0 ? _launch.NpcReply(_pid, -n) : _launch.NpcReplyById(_pid, n);
            if (!sent) return DynValue.False;
            return DynValue.NewBoolean(WaitNextNpcQuestion(seq0, NpcReplyWaitMs, ct));
        }));
        DynValue sendReply = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.NpcReplyById(_pid, N(a, 0))));
        Fill(npc, "SendReply", sendReply);
        Fill(npc, "sendReply", sendReply);
        // Variante Road/Quest Creator : réponse par ID PROTO (pas index 1-based),
        // puis attente bornée de la téléportation effectivement confirmée.
        // Ne présume JAMAIS qu'un reply_id est un index visible.
        Fill(npc, "replyByIdAndChangeMap", DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            int replyId = N(a, 0);
            long from = _launch.CurrentMap(_pid) ?? 0;
            if (replyId <= 0 || from == 0 || !_launch.NpcReplyById(_pid, replyId)) return DynValue.False;
            return DynValue.NewBoolean(WaitUntil(
                () => _launch.CurrentMap(_pid) is long target && target > 0 && target != from,
                5500, ct, pump: true));
        }));
        if (g.RawGet("leaveDialog") is { Type: DataType.ClrFunction } leave) Fill(npc, "leaveDialog", SnowFwd(s, npc, leave));
        DynValue getReplies = DynValue.NewCallback((_, a) => BuildNums(s, _launch.NpcDialog(_pid)?.ReplyIds ?? (IReadOnlyList<int>)Array.Empty<int>()));
        Fill(npc, "getReplies", getReplies);
        Fill(g, "getReplies", getReplies);
        s.Globals["getReplies"] = getReplies;
        Fill(npc, "getQuestionId", DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.NpcDialog(_pid)?.MessageId ?? 0)));
        Fill(npc, "question", DynValue.NewCallback((_, a) => NpcQuestionTable(s)));
        Fill(npc, "questionText", DynValue.NewCallback((_, a) =>
            DynValue.NewString(_launch.NpcDialog(_pid) is { } d ? _launch.GameNames.NpcMessageText(d.MessageId, d.Params) ?? "" : "")));
        Fill(npc, "hasReply", DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.NpcDialog(_pid)?.ReplyIds.Contains(N(a, 0)) ?? false)));
        Fill(npc, "isDialogOpen", DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.NpcDialog(_pid) is not null)));
    }

    // ------------------------------------------------------------------ chat:
    private void BindSnowChat(Script s)
    {
        var chat = GetOrCreateTable(s, "chat");
        string S(CallbackArguments a, int i) => At(a, chat, i).CastToString() ?? "";
        Fill(chat, "sendPrivateMessage", DynValue.NewCallback((_, a) =>
        {
            string content = S(a, 0), to = S(a, 1);
            bool ok = _launch.SendPrivateMessage(_pid, to, content);   // Perm.PmRelay vérifié au point d'action (moteur)
            if (ok) Info("mp", Loc.Fmt("L.Lua.SnowPmSent", to, content));
            return DynValue.NewBoolean(ok);
        }));
        DynValue Channel(int ch) => DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.SendChannelMessage(_pid, ch, S(a, 0))));
        Fill(chat, "sendGeneralMessage", Channel(0));
        Fill(chat, "sendGuildMessage", Channel(2));
        Fill(chat, "sendAllianceMessage", Channel(3));
        Fill(chat, "sendTradeMessage", Channel(5));
        Fill(chat, "sendRecruitmentMessage", Channel(6));
        Fill(chat, "sendChannelMessage", DynValue.NewCallback((_, a) =>
            DynValue.NewBoolean(_launch.SendChannelMessage(_pid, (int)(At(a, chat, 0).CastToNumber() ?? 0), S(a, 1)))));
    }

    // ------------------------------------------------------------------ quest: (textes + initialize bloquant)
    private void BindSnowQuest(Script s, CancellationToken ct)
    {
        var q = GetOrCreateTable(s, "quest");
        var names = _launch.GameNames;
        int N(CallbackArguments a, int i) => (int)(At(a, q, i).CastToNumber() ?? 0);
        static DynValue Str(string? v) => DynValue.NewString(v ?? "");
        // initialize: Un bot tiers rendait true si la liste des quêtes était déjà là, sinon la DEMANDAIT et BLOQUAIT jusqu'à la réponse.
        // Ici la liste n'arrive qu'avec la connexion (QuestListRequest n'existe plus) : même blocage si elle manque encore, mais
        // borné à 3 s et UNE fois par run — un instantané manqué (script lancé en cours de session) ne fige pas chaque tick de move().
        bool questWaited = false;
        q["initialize"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            if (!questWaited && !_launch.QuestsLoaded(_pid))
            {
                questWaited = true;
                WaitUntil(() => _launch.QuestsLoaded(_pid), QuestInitWaitMs, ct);
            }
            return DynValue.NewBoolean(_launch.QuestsLoaded(_pid));
        });
        // QuestListRequest absent : initialize attend seulement le snapshot reçu à la connexion.
        // Ne pas présenter une liste vide comme une liste réellement chargée.
        Fill(q, "status", DynValue.NewCallback((_, a) =>
        {
            bool loaded = _launch.QuestsLoaded(_pid);
            var t = SnowStatusTable(s, loaded, loaded ? "" : "quest_snapshot_missing_no_list_request");
            t["snapshotLoaded"] = DynValue.NewBoolean(loaded);
            t["canRequestList"] = DynValue.False;
            t["objectiveTextAvailable"] = DynValue.False;
            return DynValue.NewTable(t);
        }));
        Fill(q, "text", DynValue.NewCallback((_, a) => Str(names.I18nText(N(a, 0)))));
        Fill(q, "challengeName", DynValue.NewCallback((_, a) => Str(names.ChallengeName(N(a, 0)))));
        Fill(q, "objectiveTypeName", DynValue.NewCallback((_, a) => Str(names.QuestObjectiveTypeName(N(a, 0)))));
        Fill(q, "stepDescription", DynValue.NewCallback((_, a) => Str(names.QuestStepDescription(N(a, 0)))));
        // objectiveText(id) : texte d'objectif (pas de table fiable côté Dofus 3 -> nom du type d'objectif si connu, sinon "").
        Fill(q, "objectiveText", UnsupportedLua("quest:objectiveText"));
        // path / mainPath: chemins de fichiers du pack de quêtes un bot tiers — sans objet (scripts résolus par nom).
        Fill(q, "path", UnsupportedLua("quest:path"));
        Fill(q, "mainPath", UnsupportedLua("quest:mainPath"));
        var fc = GetOrCreateTable(s, "fightChallenge");
        fc["challengeName"] = DynValue.NewCallback((_, a) => Str(names.ChallengeName((int)(At(a, fc, 0).CastToNumber() ?? 0))));
    }

    // ------------------------------------------------------------------ character: / job: / mount:
    private void BindSnowCharacter(Script s, CancellationToken ct)
    {
        var ch = GetOrCreateTable(s, "character");
        var G = s.Globals;
        foreach (var (key, flat) in new[] { ("serverName", "getServerName"), ("isInFight", "isInFight"), ("kamas", "getKamas") })
            if (G.Get(flat).Type == DataType.ClrFunction) Fill(ch, key, SnowFwd(s, ch, G.Get(flat)));
        if (ch.RawGet("statPoints") is { Type: DataType.ClrFunction } sp) Fill(ch, "statsPoint", sp);
        // Caractéristiques de BASE (dernier CharacterCharacteristicsEvent, lu par nom) : -1 si inconnues.
        void Base(string key, int charId) => Fill(ch, key, DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.CharacteristicBase(_pid, charId))));
        Base("getVitalityBase", 11); Base("getWisdomBase", 12); Base("getStrenghtBase", 10); Base("getStrengthBase", 10);
        Base("getIntelligenceBase", 15); Base("getChanceBase", 13); Base("getAgilityBase", 14);
        // getStats() : table du dernier CharacterCharacteristicsEvent (snake_case + noms camelCase tolérés).
        Fill(ch, "getStats", DynValue.NewCallback((_, a) =>
            _launch.LastReceived(_pid, "CharacterCharacteristicsEvent") is { } p ? PacketToLua(s, p, "CharacterCharacteristicsEvent", snow: true) : DynValue.Nil));
        // Paliers de caracteristiques installes apres Lot M par BindFinalLuaContract.
        // Abonnement (AccountInformationUpdateEvent.subscription_end_date) : -1 / nil si inconnu.
        Fill(ch, "getSecondsBeforeFreeMode", DynValue.NewCallback((_, a) =>
            _launch.SubscriptionEndUtc(_pid) is { } end ? DynValue.NewNumber(Math.Max(0, Math.Floor((end - DateTime.UtcNow).TotalSeconds))) : DynValue.NewNumber(-1)));
        Fill(ch, "freeMode", DynValue.NewCallback((_, a) =>
            _launch.SubscriptionEndUtc(_pid) is { } end ? DynValue.NewBoolean(end <= DateTime.UtcNow) : DynValue.Nil));
        Fill(ch, "getSpellMinPlayerLevel", DynValue.NewCallback((_, a) =>
        {
            int id = (int)(At(a, ch, 0).CastToNumber() ?? 0);
            int level = _launch.GameData?.SpellUnlockLevel(id) ?? 0;
            return level > 0 ? DynValue.NewNumber(level) : DynValue.Nil;
        }));
        Fill(ch, "nickname", UnsupportedLua("character:nickname"));
        Fill(ch, "accountId", UnsupportedLua("character:accountId"));
        Fill(ch, "trusted", UnsupportedLua("character:trusted"));
        Fill(ch, "certified", UnsupportedLua("character:certified"));
        Fill(ch, "getURLFaceSkin", UnsupportedLua("character:getURLFaceSkin"));
        Fill(ch, "getURLBodySkin", UnsupportedLua("character:getURLBodySkin"));
        // Code du Bouclier : JAMAIS depuis un script (sécurité du compte).
        foreach (var n in new[] { "askShieldCode", "sendShieldCode", "askShieldCodeNew", "sendShieldCodeNew" })
            Fill(ch, n, UnsupportedLua("character:" + n));

        // mount: aucun fournisseur des états/commandes montures : erreurs explicites et status lisible.
        var mount = GetOrCreateTable(s, "mount");
        Fill(mount, "status", DynValue.NewCallback((_, a) =>
            DynValue.NewTable(SnowStatusTable(s, false, "mount_telemetry_and_actions_unavailable"))));
        DynValue M(string fn) => UnsupportedLua("mount:" + fn);
        foreach (var n in new[] { "hasMount", "isRiding", "isRideable", "toggleRiding", "setXpRatio", "feedMount", "mountInformation",
                                  "switchMount", "RetireMountSet", "UpdateCertificate", "DeleteMountList" })
            Fill(mount, n, M(n));
        foreach (var n in new[] { "level", "getEnergy", "getMaxEnergy", "id", "maxPods" }) Fill(mount, n, M(n));
        Fill(mount, "myMount", M("myMount"));
        Fill(mount, "moveMounts", M("moveMounts"));
        foreach (var n in new[] { "inventoryMounts", "inventoryMountsData", "stabledMounts", "paddockedMounts", "newBorns", "getPaddockInformations" })
            Fill(mount, n, UnsupportedLua("mount:" + n));
        foreach (var n in new[] { "movePaddockItem", "addPaddockItem", "putPaddockItem", "removePaddockItem" }) Fill(mount, n, M(n));
        Fill(mount, "TiredMount", DynValue.NewNumber(210));
        foreach (var n in new[] { "BankReturn", "LevelUp", "BoucleNumber" }) Fill(mount, n, DynValue.NewNumber(0));
        foreach (var n in new[] { "CellsObjects", "IdsBabyWorn", "MountSet", "MountInventory", "MountEtable", "MountEnclos", "NewBornsData",
                                  "guidWaitCertificat", "paddockInformations" })
            Fill(mount, n, DynValue.NewTable(new Table(s)));
    }

    // ------------------------------------------------------------------ d2data: (= data: + textes)
    private void BindSnowD2Data(Script s)
    {
        var data = GetOrCreateTable(s, "data");
        var names = _launch.GameNames;
        Fill(data, "text", DynValue.NewCallback((_, a) => DynValue.NewString(names.I18nText((int)(At(a, data, 0).CastToNumber() ?? 0)) ?? "")));
        Fill(data, "challengeName", DynValue.NewCallback((_, a) => DynValue.NewString(names.ChallengeName((int)(At(a, data, 0).CastToNumber() ?? 0)) ?? "")));
        Fill(data, "poiName", DynValue.NewCallback((_, a) => DynValue.NewString(names.PoiName((int)(At(a, data, 0).CastToNumber() ?? 0)) ?? "")));
        // mapData(mapId) -> { mapId, x, y, subAreaId, areaId }.
        Fill(data, "mapData", DynValue.NewCallback((_, a) =>
        {
            long id = (long)(At(a, data, 0).CastToNumber() ?? 0);
            if (id <= 0) id = _launch.CurrentMap(_pid) ?? 0;
            var t = new Table(s);
            t["mapId"] = DynValue.NewNumber(id);
            if (_launch.MapCoordOf(id) is { } c) { t["x"] = DynValue.NewNumber(c.X); t["y"] = DynValue.NewNumber(c.Y); }
            int sub = SubAreaFor(id);
            t["subAreaId"] = DynValue.NewNumber(sub);
            t["areaId"] = DynValue.NewNumber(sub > 0 ? names.AreaOfSubArea(sub) : -1);
            return DynValue.NewTable(t);
        }));
        // objectFromD2O / allObjectsFromD2O : pas d'accès générique aux tables D2O (formats Dofus 2).
        Fill(data, "objectFromD2O", UnsupportedLua("d2data:objectFromD2O"));
        Fill(data, "allObjectsFromD2O", UnsupportedLua("d2data:allObjectsFromD2O"));
        // export* : écriture de fichiers de données sur disque — jamais depuis un script (bac à sable).
        foreach (var n in new[] { "exportD2O", "exportAllD2O", "exportMapData", "exportAllMapData" })
            Fill(data, n, UnsupportedLua("d2data:" + n));
        if (s.Globals.Get("d2data").IsNil()) s.Globals["d2data"] = DynValue.NewTable(data);
    }
}
