using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Engine.LuaTooling;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ===================== Liaison des verbes Lua : Bind, BindMemory, BindSale, BindModules + helpers de callbacks (NativeAOT) =====================
public sealed partial class GivreLuaHost
{
    // ------------------------------------------------------------------ bindings (verbes exposes au script)
    private void Bind(Script s, CancellationToken ct)
    {
        LuaSnowCompatRuntime.ConfigureCoroutines(s, ct, ThrowIfHookTurnGone);
        // --- controle / journal ---
        // printMessage(msg [, "info"|"green"|"success"|"yellow"|"warning"|"red"|"danger"]) : message colore par niveau nomme.
        s.Globals["printMessage"] = DynValue.NewCallback((_, a) =>
        {
            string msg = a.Count > 0 ? a[0].CastToString() : "";
            string? lvl = a.Count > 1 ? a[1].CastToString() : null;
            Emit(ParseLevel(lvl), "script", msg);
            return DynValue.Nil;
        });
        s.Globals["print"] = LuaPrint.Callback(msg => Emit(LuaLogLevel.Info, "script", msg));
        s.Globals["printSuccess"] = LuaPrint.Callback(msg => Emit(LuaLogLevel.Success, "script", msg));
        s.Globals["printWarning"] = LuaPrint.Callback(msg => Emit(LuaLogLevel.Warning, "script", msg));
        s.Globals["printError"] = LuaPrint.Callback(msg => Emit(LuaLogLevel.Danger, "script", msg));
        // printColor(msg, "#RRGGBB").
        s.Globals["printColor"] = DynValue.NewCallback((_, a) =>
        {
            string p0 = a.Count > 0 ? a[0].CastToString() : "";
            string p1 = a.Count > 1 ? a[1].CastToString() : "";
            (string msg, string hex) = IsHex(p0) ? (p1, p0) : (p0, p1);
            Emit(LuaLogLevel.Info, "script", msg, IsHex(hex) ? hex : null);
            return DynValue.Nil;
        });
        // Messagerie en jeu : MP par pseudo, ou message de canal (0 général, 5 commerce, 6 recrutement, 2 guilde…).
        // Code+champs résolus par nom via le mapping ; durcissement anti-injection dans LaunchService. Renvoie true si injecté.
        // sendPrivateMessage(pseudo, message). Tolère l'ordre tiers (message, pseudo): un pseudo Dofus n'a pas d'espace ->
        // si arg1 contient un espace et arg2 aucun, on remet dans l'ordre (avertissement une fois par hôte). Limite : un
        // message SANS espace dans l'ordre tiers (« ok », « Bob ») n'est pas détectable.
        s.Globals["sendPrivateMessage"] = DynValue.NewCallback((_, a) =>
        {
            string p0 = a.Count > 0 ? a[0].CastToString() ?? "" : "";
            string p1 = a.Count > 1 ? a[1].CastToString() ?? "" : "";
            if (LuaCompat.ShouldSwapPmArgs(p0, p1))   // ordre tiers (contenu, destinataire)
            {
                (p0, p1) = (p1, p0);
                if (!_pmSwapWarned) { _pmSwapWarned = true; Warn("script", Loc.T("L.Lua.PmArgsSwapped")); }
            }
            return DynValue.NewBoolean(_launch.SendPrivateMessage(_pid, p0, p1));   // gate Perm.PmRelay au point d'action (moteur)
        });
        s.Globals["sendChannelMessage"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(
            _launch.SendChannelMessage(_pid, a.Count > 0 ? (int)(a[0].CastToNumber() ?? 0) : 0, a.Count > 1 ? a[1].CastToString() ?? "" : "")));
        // delay(ms) : point SÛR de la pompe des hooks de paquets (Lot G) ; sans hook -> exactement l'ancienne attente.
        s.Globals["delay"] = DynValue.NewCallback((_, a) => { InterruptibleDelay(LuaSnowCompatRuntime.DelayMilliseconds(a.Count > 0 ? a[0] : DynValue.Nil), ct, pump: true); return DynValue.Nil; });
        s.Globals["stopScript"] = Cb((Action)(() => { StopRun(ct, "script"); ThrowIfStopping(ct); }));   // raison "script" pour stopped(reason)
        // voyage multi-cartes : raccourci zaap (havre-sac) si utile, sinon marche. travelTo(mapId) | travelTo(x, y) | travelTo("x,y").
        s.Globals["travelTo"] = DynValue.NewCallback((_, a) =>
        {
            bool ok = TravelToArgs(a, ct);
            return CreateTravelToResult(ok, _lastTravelFailure);
        });
        s.Globals["goToMapId"] = s.Globals.Get("travelTo");   // nom tiers

        // --- getters d'etat ---
        s.Globals["getMapId"] = Cb((Func<double>)(() => _launch.CurrentMap(_pid) ?? 0));
        s.Globals["getCurrentPos"] = Cb((Func<string>)(() => { var st = _world.Get(_pid); return st.MapX is int x && st.MapY is int y ? $"{x},{y}" : ""; }));
        s.Globals["getCurrentCell"] = Cb((Func<int>)(() => _world.Get(_pid).SelfCell ?? -1));
        s.Globals["getPods"] = Cb((Func<int>)(() => _world.Get(_pid).Pods.Cur));
        s.Globals["getPodsMax"] = Cb((Func<int>)(() => _world.Get(_pid).Pods.Max));
        s.Globals["getPodsPercent"] = Cb((Func<int>)(() => _world.Get(_pid).PodsPercent));
        s.Globals["getCharacterLevel"] = Cb((Func<int>)(() => _world.Get(_pid).Level ?? 0));
        s.Globals["getCharacterName"] = Cb((Func<string>)(() => _world.Get(_pid).CharacterName ?? ""));
        s.Globals["getCharacterXp"] = Cb((Func<double>)(() => _launch.CharacterXp(_pid)));   // XP totale (-1 si inconnue)
        s.Globals["getMapIdAt"] = Cb((Func<int, int, double>)((x, y) => _launch.MapIdAt(x, y, _launch.CurrentWorldMap(_pid)) ?? 0));   // meme calque que le perso
        s.Globals["isInFight"] = Cb((Func<bool>)(() => _launch.IsInFight(_pid)));

        // --- deplacement ---
        s.Globals["goToCellId"] = Cb((Func<int, bool>)(cell => GoToCell(cell, ct)));
        // changeMap("top") | "top(123)" (cellule de sortie forcée) | "left|right" (une au hasard) | "zaap(id)" | "zaapi(id)" | "zaapcarte(id)" | "havenbag".
        s.Globals["changeMap"] = DynValue.NewCallback((_, a) =>
        {
            string raw = a.Count > 0 ? a[0].CastToString() ?? "" : "";
            string pick = LuaCompat.PickAlternative(raw, Random.Shared);
            if (LuaCompat.TryParseTeleportKeyword(pick, out var kw, out long kwMap)) return DynValue.NewBoolean(RunTeleportKeyword(kw, kwMap, ct));
            return DynValue.NewBoolean(ChangeMap(pick, ct));
        });

        // --- interactifs / recolte ---
        s.Globals["goUseInteractive"] = Cb((Func<int, bool>)(cell => UseInteractive(cell, ct, verbose: true)));
        s.Globals["gatherAll"] = Cb((Func<bool>)(() => GatherAll(ct)));
        // gatherOne() : recolte UNE seule ressource (la plus proche, filtree par ELEMENTS_TO_GATHER) et rend true si une a
        // ete tentee, false si plus rien. Brique pour paces la recolte (ex : delay(2000) entre chaque) vs gatherAll (tout d'un coup).
        s.Globals["gatherOne"] = Cb((Func<bool>)(() => GatherOne(ct)));

        // --- combat ---
        s.Globals["fight"] = Cb((Func<bool>)(() => FightOne(ct, -1)));
        s.Globals["fightCell"] = Cb((Func<int, bool>)(cell => FightOne(ct, cell)));

        // --- INVENTAIRE : modele uid->(gid,qty) tenu par LaunchService (seede par le snapshot InventoryContentEvent au
        //     login, maintenu par ObjectAdded/Quantity/Deleted). Le wire ne porte que l'UID -> LaunchService convertit
        // GID->UID avant de forger iuu/iur. Si l'inventaire n'a pas encore ete recu (pas de relog),
        //     getInventoryItemCount rend 0 et use/drop journalisent "inventaire pas encore recu".
        s.Globals["getInventoryItemCount"] = Cb((Func<int, int>)(gid => _launch.InventoryCount(_pid, gid)));
        s.Globals["getItemName"] = Cb((Func<int, string>)(gid => _launch.ItemDisplayName(gid)));   // nom clair d'un gid (pour de jolis logs)
        s.Globals["getKamas"] = Cb((Func<double>)(() => _launch.Kamas(_pid)));                      // kamas du perso (capté sur ivx), -1 si inconnu
        // useInventoryItem : alias de inventory.use (lié dans BindModules, qui journalise lui-même).
        s.Globals["DropItem"] = Cb((Func<int, int, bool>)((gid, count) => DropItemLogged(gid, count)));
        s.Globals["dropItem"] = Cb((Func<int, int, bool>)((gid, count) => DropItemLogged(gid, count)));

        // --- Lot 2 (PNJ / banque generique) : PAS encore implemente. STUBS surs (evite "attempt to call a nil value").
        s.Globals["enterHavenBag"] = Cb((Func<bool>)(() =>
        {
            bool ok = _launch.EnterHavenBag(_pid);
            if (ok) Ok("havre-sac", Loc.T("L.Lua.HavenBagEntered"));
            else Warn("havre-sac", Loc.T("L.Lua.HavenBagFail"));
            return ok;
        }));
        // Métier : niveau d'un métier par job_id (ex Mineur 26, Bûcheron 24…). 0 = jamais reçu (récolter/craft une fois met à jour).
        s.Globals["getJobLevel"] = Cb((Func<int, int>)(jobId => _launch.JobLevel(_pid, jobId)));
        // PNJ / dialogue : parler, répondre (par INDEX 1-based de la réponse visible), répondre-puis-changer-de-carte, fermer.
        // talkNpc(id): id d'ACTEUR (négatif, map:getNpcs.id) ou id de MODÈLE; attend la QUESTION avant de
        // rendre la main. replyNpc(index) : attend (≤ 2 s) la question suivante / la fermeture -> un replyNpc enchaîné lit
        // toujours les réponses de la BONNE question.
        s.Globals["talkNpc"] = Cb((Func<int, bool>)(npcId =>
        {
            long actor = ResolveNpc(npcId);
            return actor != 0 && _launch.NpcActionAsync(_pid, actor, 3, ct).GetAwaiter().GetResult();
        }));
        s.Globals["replyNpc"] = Cb((Func<int, bool>)(index =>
        {
            long seq0 = _launch.NpcDialogSeq(_pid);
            if (!_launch.NpcReply(_pid, index)) return false;
            return WaitUntil(() => _launch.NpcDialogSeq(_pid) != seq0, 2000, ct, pump: true);
        }));
        s.Globals["replyNpcAndChangeMap"] = Cb((Func<int, bool>)(index => _launch.NpcReplyAndChangeMapAsync(_pid, index, ct).GetAwaiter().GetResult()));
        s.Globals["leaveDialog"] = Cb((Func<bool>)(() => _launch.LeaveDialog(_pid)));
        // Banque générique (sur un coffre DÉJÀ ouvert) : déposer tout / déposer les objets déjà présents / retirer tout / retirer les existants.
        s.Globals["putAllItems"] = Cb((Func<bool>)(() => _launch.BankDepositAll(_pid)));
        s.Globals["putExistingItems"] = Cb((Func<bool>)(() => _launch.BankDepositExisting(_pid)));
        s.Globals["getAllItems"] = Cb((Func<bool>)(() => _launch.BankWithdrawAll(_pid)));
        s.Globals["getExistingItems"] = Cb((Func<bool>)(() => _launch.BankWithdrawExisting(_pid)));
        // Dépôt SÉLECTIF d'un objet par GID dans un coffre DÉJÀ ouvert (count<=0 = tout le stack) — équivaut au storage:DropItem d'ailleurs.
        s.Globals["depositItem"] = DynValue.NewCallback((_, a) =>
        {
            int off = a.Count > 0 && a[0].Type == DataType.Table ? 1 : 0;
            long gid = off < a.Count ? (long)(a[off].CastToNumber() ?? 0) : 0;
            int count = (off + 1) < a.Count ? (int)(a[off + 1].CastToNumber() ?? 0) : 0;
            return DynValue.NewBoolean(_launch.BankDepositItem(_pid, gid, count));
        });
        // Webhook Discord (relais texte) depuis le script — gaté Perm.PmRelay (messagerie/relais) au POINT D'ACTION.
        s.Globals["sendWebhook"] = DynValue.NewCallback((_, a) =>
        {
            int off = a.Count > 0 && a[0].Type == DataType.Table ? 1 : 0;
            string url = off < a.Count ? (a[off].CastToString() ?? "") : "";
            string message = (off + 1) < a.Count ? (a[off + 1].CastToString() ?? "") : "";
            if (!LicenseService.HasPermission(LicenseService.Perm.PmRelay)) return DynValue.NewBoolean(false);
            WebhookService.Post(url, message);
            return DynValue.NewBoolean(true);
        });

        // Craft BLOQUANT en 1 appel : craftItem(elementId, skillUid, recipe, count) où recipe = { {gid=…, quantity=…}, … }.
        // Ouvre l'établi, pose les ingrédients (GID->UID), règle le nombre, valide, attend le résultat, ferme.
        // Renvoie ok, quantité produite, gid produit (0, 0 en cas d'échec) : le 1er retour reste le booléen -> les scripts
        // qui ne testent que `if craftItem(...)` sont inchangés. Au retour, l'objet est déjà compté par getInventoryItemCount.
        s.Globals["craftItem"] = DynValue.NewCallback((_, a) =>
        {
            int selfOff = a.Count > 0 && a[0].Type == DataType.Table ? 1 : 0;   // décalage appel colon (self)
            int Arg(int i) { int j = i + selfOff; return j < a.Count ? (int)(a[j].CastToNumber() ?? 0) : 0; }
            int element = Arg(0), skill = Arg(1), count = Arg(3);
            var recipe = new System.Collections.Generic.List<(long, int)>();
            int ri = selfOff + 2;
            if (ri < a.Count && a[ri].Type == DataType.Table)
                foreach (var row in a[ri].Table.Values)
                    if (row.Type == DataType.Table)
                    {
                        long gid = (long)(row.Table.Get("gid").CastToNumber() ?? 0);
                        int qty = (int)((row.Table.Get("quantity").CastToNumber() ?? row.Table.Get("qty").CastToNumber()) ?? 0);
                        if (gid > 0 && qty > 0) recipe.Add((gid, qty));
                    }
            if (count < 1) count = 1;
            var r = _launch.CraftAsync(_pid, element, skill, recipe, count, ct).GetAwaiter().GetResult();
            return DynValue.NewTuple(DynValue.NewBoolean(r.Ok), DynValue.NewNumber(r.Ok ? r.Qty : 0), DynValue.NewNumber(r.Ok ? r.Gid : 0));
        });

        // Forgemagie BLOQUANTE : forgeItem(elementId, skillUid, item, targets, maxSteps) où item = uid ou gid d'un objet du sac et
        // targets = { {stat="Fo", target=80, priority=1, rune="Ra Fo"}, … } (règles « Cibles » de ForgeEngine). Renvoie la raison
        // d'arrêt (« terminé », « rune absente du sac : … »…) et le nombre de runes passées. ⚠ séquence d'atelier non validée en jeu.
        s.Globals["forgeItem"] = DynValue.NewCallback((_, a) =>
        {
            int selfOff = a.Count > 0 && a[0].Type == DataType.Table && a.Count > 5 ? 1 : 0;
            DynValue At(int i) => i + selfOff < a.Count ? a[i + selfOff] : DynValue.Nil;
            int element = (int)(At(0).CastToNumber() ?? 0), skill = (int)(At(1).CastToNumber() ?? 0);
            long item = (long)(At(2).CastToNumber() ?? 0);
            int maxSteps = (int)(At(4).CastToNumber() ?? 100);
            var targets = new System.Collections.Generic.List<Eco.ForgeTarget>();
            if (At(3).Type == DataType.Table)
                foreach (var row in At(3).Table.Values)
                    if (row.Type == DataType.Table && row.Table.Get("stat").CastToString() is { Length: > 0 } stat
                        && row.Table.Get("rune").CastToString() is { Length: > 0 } rune)
                        targets.Add(new Eco.ForgeTarget(stat, (int)(row.Table.Get("target").CastToNumber() ?? 0),
                            (int)(row.Table.Get("priority").CastToNumber() ?? 1), rune));
            if (targets.Count == 0 || _launch.BagUidForForge(_pid, item) is not { } uid)
                return DynValue.NewTuple(DynValue.NewString(targets.Count == 0 ? "aucune cible" : "objet absent du sac"), DynValue.NewNumber(0));
            var r = _launch.ForgeAsync(_pid, element, skill, uid, (stats, _) => Eco.ForgeEngine.DecideTargets(stats, targets),
                Math.Clamp(maxSteps, 1, 1000), ct).GetAwaiter().GetResult();
            return DynValue.NewTuple(DynValue.NewString(r.Stop), DynValue.NewNumber(r.Steps.Count));
        });

        // --- HDV : voyage+ouverture auto (goToHdv) + table sale: (SellItem/EditPriceByGID/GetPriceItem/...) ---
        s.Globals["goToHdv"] = Cb((Func<int, bool>)(m => GoToHdvAndOpenSeller(m, ct)));   // voyage -> hôtelier (iwo) -> mode vente
        s.Globals["goToHdvBuy"] = Cb((Func<int, bool>)(m => GoToHdvAndOpenBuyer(m, ct)));  // voyage -> hôtelier -> mode ACHAT (recherche jzn + sale:buyItem)
        BindSale(s, ct);
        BindModules(s, ct);
        BindMemory(s);
        BindLotB(s, ct);
        BindLotE(s, ct);
        BindLotF(s, ct);   // Lot F: PV réels, régénération, énergie, paliers d'XP
        BindLotI(s, ct);   // Lot I : quest:, data:, job:name, map: sous-zones/zones, inventory: niveau/poids + équipement
        BindPlatform(s, NewRuntime(s, ct), ct);   // Lot G : hooks paquets, require, loadScript, notify, team:runOnce/inviteAll/isInParty
        BindComfort(s, ct);   // Lot K : obstacles personnalisés, sendMapRequest, statut privé, resetStats, défis actifs, http:
        BindSnow(s, ct);      // compat tiers: global/config/givreController/d2data/npc/chat/craft/… + noms tolérants (EN DERNIER)
        BindFm(s, ct);          // forgemagie scriptée (fm:item/setTarget/advise/apply/autoRun)
        BindLotM(s, ct);        // MZP23 lot M : achievement/anomaly/teleport/dungeon/document/data/quest/npc boutiques/character
        BindSupervisor(s, ct);  // MZP23 : supervisor:send/isRunning/wait côté bot
        BindRoadCreator(s, ct); // AnkaBot Road Creator : io.open limité aux fichiers Road_*.lua et choix de carte
        BindFinalLuaContract(s); // les modules tardifs ne doivent pas réintroduire un stub neutre
    }

    // API memory: (PAR PERSONNAGE) et sharedMemory: (GLOBAL) — état persistant clé/valeur pour les scripts (compteurs,
    // reprise après crash, coordination chef↔mules). Valeurs = string (les scripts font tonumber au besoin).
    // ⚠️ La clé perso est résolue PARESSEUSEMENT au nom courant : un memory:set/get appelé AU TOP-LEVEL (avant identification)
    // tombe dans le bucket "?" -> à utiliser dans move(). sharedMemory: n'est pas concerné (fichier unique).
    private void BindMemory(Script s)
    {
        static int B(CallbackArguments a) => a.Count > 0 && a[0].Type == DataType.Table ? 1 : 0;   // self présent ? (appel colon)
        static string S(CallbackArguments a, int i) { int j = B(a) + i; return j < a.Count ? (a[j].CastToString() ?? "") : ""; }
        static DynValue Def(CallbackArguments a, int i) { int j = B(a) + i; return j < a.Count ? a[j] : DynValue.Nil; }   // valeur de repli de get(key, default)
        string CharKey() { var n = _world.Get(_pid).CharacterName; return string.IsNullOrWhiteSpace(n) ? "?" : n; }

        var mem = new Table(s);
        mem["set"] = DynValue.NewCallback((_, a) => { LuaMemoryStore.Set(CharKey(), S(a, 0), S(a, 1)); return DynValue.True; });
        mem["get"] = DynValue.NewCallback((_, a) => { var v = LuaMemoryStore.Get(CharKey(), S(a, 0)); return v is null ? Def(a, 1) : DynValue.NewString(v); });
        mem["delete"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(LuaMemoryStore.Delete(CharKey(), S(a, 0))));
        mem["all"] = DynValue.NewCallback((_, a) => { var t = new Table(s); foreach (var kv in LuaMemoryStore.All(CharKey())) t.Set(kv.Key, DynValue.NewString(kv.Value)); return DynValue.NewTable(t); });
        s.Globals["memory"] = DynValue.NewTable(mem);

        var shm = new Table(s);
        shm["set"] = DynValue.NewCallback((_, a) => { LuaMemoryStore.SharedSet(S(a, 0), S(a, 1)); return DynValue.True; });
        shm["get"] = DynValue.NewCallback((_, a) => { var v = LuaMemoryStore.SharedGet(S(a, 0)); return v is null ? Def(a, 1) : DynValue.NewString(v); });
        shm["delete"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(LuaMemoryStore.SharedDelete(S(a, 0))));
        shm["all"] = DynValue.NewCallback((_, a) => { var t = new Table(s); foreach (var kv in LuaMemoryStore.SharedAll()) t.Set(kv.Key, DynValue.NewString(kv.Value)); return DynValue.NewTable(t); });
        s.Globals["sharedMemory"] = DynValue.NewTable(shm);
    }

    // API HDV façon « sale: » (syntaxe colon MoonSharp). Sur un appel colon `sale:X(...)`, MoonSharp passe la table `sale`
    // en 1er argument (self) -> on décale les vrais args de 1 ; on tolère aussi l'appel point `sale.X(...)` (pas de self).
    // Tout est BLOQUANT sur le thread de script (comme travelTo) et gaté par le moteur (Perm.Hdv + HDV vendeur ouvert).
    // ACHAT + PNJ = stubs sûrs (pas encore supportés) pour éviter « attempt to call a nil value ». AOT-safe (NewCallback).
    private void BindSale(Script s, CancellationToken ct)
    {
        var t = new Table(s);
        long L(CallbackArguments a, int i) => (long)(At(a, t, i).CastToNumber() ?? 0);
        int N(CallbackArguments a, int i) => (int)(At(a, t, i).CastToNumber() ?? 0);
        // Lua numbers are doubles. An NaN, Infinity, fractional price, or a
        // value outside exact IEEE-754 integer range must not become a real
        // HDV order through a narrowing/saturating C# conversion.
        bool PositiveExact(CallbackArguments a, int i, out long value)
            => HdvLuaOrderNumeric.TryPositiveLong(At(a, t, i).CastToNumber(), out value);
        bool PositiveInt(CallbackArguments a, int i, out int value)
            => HdvLuaOrderNumeric.TryPositiveInt(At(a, t, i).CastToNumber(), out value);
        // Mise en vente / reprice
        t["includeStorages"] = DynValue.NewCallback((_, a) =>
            DynValue.NewBoolean(_launch.SetMergedInventoryAsync(_pid,
                At(a, t, 0).CastToBool(), At(a, t, 1).CastToBool(), ct).GetAwaiter().GetResult()));
        t["SellItem"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(
            PositiveExact(a, 0, out long gid) && PositiveInt(a, 1, out int qty) &&
            PositiveExact(a, 2, out long price) &&
            _launch.HdvScriptSellByGidAsync(_pid, gid, qty, price, ct).GetAwaiter().GetResult()));
        t["EditPrice"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(
            PositiveInt(a, 0, out int uid) && PositiveExact(a, 1, out long price) &&
            PositiveInt(a, 2, out int qty) &&
            _launch.HdvScriptRepriceAsync(_pid, uid, qty, price, ct).GetAwaiter().GetResult()));
        // EditPriceByGID(GID, Price, Qtte): reprice TOUS mes lots (gid, qtte) dont le prix diffère. true aussi
        // quand tout était déjà au bon prix ; false si aucun lot correspondant ou si un reprice a échoué.
        t["EditPriceByGID"] = DynValue.NewCallback((_, a) =>
        {
            if (!PositiveExact(a, 0, out long gid) || !PositiveExact(a, 1, out long price) ||
                !PositiveInt(a, 2, out int qty)) return DynValue.False;
            var r = _launch.HdvScriptRepriceEveryByGidAsync(_pid, gid, qty, price, ct).GetAwaiter().GetResult();
            return DynValue.NewBoolean(r.Matched > 0 && r.Succeeded == r.Attempted);
        });
        // Prix marché
        // getAveragePriceItem(GID, Lot) : prix moyen UNITAIRE (ExchangeBidPriceForSellerEvent.average_price) × taille du lot.
        // Lot = index un bot tiers 1..4 (1/10/100/1000) ou taille 10/100/1000; absent = 1. -1 si inconnu (inchangé).
        t["getAveragePriceItem"] = DynValue.NewCallback((_, a) =>
        {
            long avg = _launch.HdvGetMarketPriceAsync(_pid, L(a, 0), ct).GetAwaiter().GetResult();
            int lot = LuaCompat.LotSizeFromArg(N(a, 1));
            return DynValue.NewNumber(avg > 0 && lot > 0 && avg <= long.MaxValue / lot
                ? avg * lot : -1);
        });
        // GetPriceItem(GID, Lot) : prix du lot : ACHAT (offre jzn) en mode acheteur, sinon marché MIN vendeur (jzs).
        // Lot = index un bot tiers 1..4 OU taille 10/100/1000 (LuaCompat.LotSizeFromArg). -1 si inconnu.
        t["GetPriceItem"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(
            _launch.HdvGetLotPriceAsync(_pid, L(a, 0), LuaCompat.LotSizeFromArg(N(a, 1)), ct).GetAwaiter().GetResult()));
        // UpdateAllItems — sémantique tiers: repositionne TOUS mes lots face au marché (sous-cote de 1). Contrairement à
        // Un bot tiers, ne REMONTE jamais un prix et ne se sous-cote jamais lui-même (garde anti-auto-sous-cote de HdvRepriceAllAsync).
        // Renvoie le nombre de lots repositionnés. HDV en mode VENDEUR requis (goToHdv).
        t["UpdateAllItems"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.HdvScriptRepriceAllAsync(_pid, 1, ct).GetAwaiter().GetResult()));
        // refreshPrices() : ANCIEN comportement (lecture seule) de UpdateAllItems — rafraîchit le prix marché de chaque gid en vente.
        t["refreshPrices"] = DynValue.NewCallback((_, a) => { foreach (var g in _launch.Hdv.MyListings(_pid).Select(x => x.Gid).Distinct().ToList()) { if (ct.IsCancellationRequested) break; _launch.HdvGetMarketPriceAsync(_pid, g, ct).GetAwaiter().GetResult(); } return DynValue.Nil; });
        // repriceAll([undercut]) : sous-cote TOUS mes lots au marché min − undercut (défaut 1), garde anti-auto-sous-cote. Renvoie le nb repositionné. HDV en mode VENDEUR requis (goToHdv).
        t["repriceAll"] = DynValue.NewCallback((_, a) =>
        {
            double? raw = At(a, t, 0).CastToNumber();
            long undercut = 1;
            if (raw is not null &&
                !HdvLuaOrderNumeric.TryNonNegativeLong(raw, out undercut))
                return DynValue.NewNumber(-1);
            return DynValue.NewNumber(_launch.HdvScriptRepriceAllAsync(
                _pid, Math.Max(1, undercut), ct).GetAwaiter().GetResult());
        });
        // Lecture de MES lots en vente
        t["ItemsOnSale"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.Hdv.MyListingCount(_pid)));
        // 1-based (for i = 1, sale:ItemsOnSale). Index 0 = alias of 1 for compatibility with old GivreClone
        // 0-based scripts. Order = MyListings (sorted by gid, then qty).
        t["GetItemGID"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.Hdv.MyListingAt(_pid, LuaCompat.ZeroBasedFromLuaIndex(N(a, 0)))?.Gid ?? 0));
        t["GetItemGUID"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.Hdv.MyListingAt(_pid, LuaCompat.ZeroBasedFromLuaIndex(N(a, 0)))?.Uid ?? 0));
        t["GetItemPrice"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.Hdv.MyListingAt(_pid, LuaCompat.ZeroBasedFromLuaIndex(N(a, 0)))?.Price ?? 0));
        t["GetItemQuantity"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.Hdv.MyListingAt(_pid, LuaCompat.ZeroBasedFromLuaIndex(N(a, 0)))?.Qty ?? 0));
        t["getGID"] = DynValue.NewCallback((_, a) => { int uid = N(a, 0); return DynValue.NewNumber(_launch.Hdv.MyListings(_pid).FirstOrDefault(x => x.Uid == uid).Gid); });
        // GetLots(qtte) -> INDEX 1..4 (1->1, 10->2, 100->3, 1000->4), 0 si la quantité n'est pas un lot standard.
        t["GetLots"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(LuaCompat.LotIndexOfQuantity(N(a, 0))));
        t["AvailableSpace"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.HdvFreeSlots(_pid)));   // -1 tant que le maximum vendeur est inconnu
        // Retirer une de MES ventes (moteur HdvRemoveAsync déjà implémenté) : RemoveItem(GUID, Qtte)
        t["RemoveItem"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(
            PositiveInt(a, 0, out int uid) && PositiveInt(a, 1, out int qty) &&
            _launch.HdvRemoveAsync(_pid, uid, qty, ct).GetAwaiter().GetResult()));
        // ACHAT HDV par GID : recherche l'offre (jzn -> uid+prix par lot) puis achète le lot demandé (kei). Le HDV doit être
        // ouvert en mode ACHAT (goToHdvBuy). buyItem(GID, lots, prixMax) — prixMax=0 = pas de plafond. 100% mapping-driven.
        t["buyItem"] = DynValue.NewCallback((_, a) =>
        {
            if (!PositiveExact(a, 0, out long gid) || !PositiveInt(a, 1, out int qty))
                return DynValue.False;
            // Only an actually absent maximum keeps the documented unlimited
            // buy mode. NaN/overflow/fractional money cannot become max=0.
            double? rawMax = At(a, t, 2).CastToNumber();
            if (rawMax is not null &&
                !HdvLuaOrderNumeric.TryNonNegativeLong(rawMax, out long ignoredMax))
                return DynValue.False;
            long maxPrice = rawMax is null ? 0 : (long)rawMax.Value;
            return DynValue.NewBoolean(_launch.HdvBuyByGidAsync(
                _pid, gid, qty, maxPrice, ct).GetAwaiter().GetResult());
        });
        t["offers"] = DynValue.NewCallback((_, a) =>
        {
            var rows = _launch.HdvGetBuyerOffersAsync(_pid, L(a, 0), ct).GetAwaiter().GetResult();
            return BuildList(s, rows, (r, offer) =>
            {
                r["uid"] = DynValue.NewNumber(offer.Uid); r["gid"] = DynValue.NewNumber(offer.Gid);
                r["prices"] = BuildNumsL(s, offer.Prices);
                r["effects"] = BuildList(s, offer.Effects, (e, fx) =>
                {
                    e["action"] = DynValue.NewNumber(fx.ActionId); e["value"] = DynValue.NewNumber(fx.Value);
                    e["min"] = DynValue.NewNumber(fx.Min); e["max"] = DynValue.NewNumber(fx.Max);
                    e["text"] = DynValue.NewString(_launch.DescribeEffect(fx)?.Text ?? "");
                });
            });
        });
        t["buyOffer"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(
            _launch.HdvBuySelectedOfferAsync(_pid, L(a, 0), N(a, 1), N(a, 2), L(a, 3), ct).GetAwaiter().GetResult()));
        // PNJ marchand (buyToPnj, sellToPnj, sellAllItemToPnj…) : posés par BindSnowSale (GivreLuaHost.SnowEconomy.cs).

        s.Globals["sale"] = DynValue.NewTable(t);
    }

    // Construit une table Lua = LISTE (1-based) à partir d'items projetés en sous-tables (façon {cell=..,name=..}).
    private static DynValue BuildList<T>(Script s, IEnumerable<T> src, Action<Table, T> fill)
    {
        var list = new Table(s); int i = 1;
        foreach (var it in src) { var row = new Table(s); fill(row, it); list.Set(i++, DynValue.NewTable(row)); }
        return DynValue.NewTable(list);
    }

    // Table Lua = tableau (1-based) de NOMBRES bruts.
    private static DynValue BuildNums(Script s, IEnumerable<int> xs)
    {
        var t = new Table(s); int i = 1;
        foreach (var x in xs) t.Set(i++, DynValue.NewNumber(x));
        return DynValue.NewTable(t);
    }

    // API organisée par MODULES (syntaxe colon façon sale:) : map:/combat:/inventory:/bank:/zaap:/job:. Chaque méthode
    // tolère l'appel colon (self = table en arg[0]) ET point. Ce sont des WRAPPERS PURS sur des primitives moteur DÉJÀ
    // existantes (BotWorldStore / LaunchService) — aucune capture nouvelle. Les verbes plats historiques restent inchangés.
    private void BindModules(Script s, CancellationToken ct)
    {
        static int B(CallbackArguments a) => a.Count > 0 && a[0].Type == DataType.Table ? 1 : 0;   // self présent ? (appel colon)
        static long L(CallbackArguments a, int i) { int j = B(a) + i; return j < a.Count ? (long)(a[j].CastToNumber() ?? 0) : 0; }
        static int N(CallbackArguments a, int i) { int j = B(a) + i; return j < a.Count ? (int)(a[j].CastToNumber() ?? 0) : 0; }
        static string S(CallbackArguments a, int i) { int j = B(a) + i; return j < a.Count ? (a[j].CastToString() ?? "") : ""; }
        // Délai en SECONDES (argument i) : nombre > 0, sinon la valeur par défaut. Borné à 1 h.
        static int TimeoutSecArg(CallbackArguments a, int i, int def) { int j = B(a) + i; double v = j < a.Count ? (a[j].CastToNumber() ?? 0) : 0; return v > 0 ? (int)Math.Min(3600, Math.Ceiling(v)) : def; }

        // ---- map: carte courante (monstres / ressources récoltables / PNJ / résolution coord->id) ----
        var map = new Table(s);
        map["getMonsters"] = DynValue.NewCallback((_, a) => BuildList(s, _world.Get(_pid).Monsters, (r, m) => { r["cell"] = DynValue.NewNumber(m.CellId); r["name"] = DynValue.NewString(m.Name ?? ""); r["id"] = DynValue.NewNumber(m.ActorId); r["arch"] = DynValue.NewBoolean(m.Monsters != null && m.Monsters.Any(e => ArchMonsters.IsArch(e.GenericId))); FillMonsterGroupLotB(s, r, m); }));
        // Groupes contenant un ARCHIMONSTRE seulement : { cell, id, name (de l'archi), gid }.
        map["getArchMonsters"] = DynValue.NewCallback((_, a) => BuildList(s,
            _world.Get(_pid).Monsters.Where(m => m.Monsters != null && m.Monsters.Any(e => ArchMonsters.IsArch(e.GenericId))),
            (r, m) => { var arch = m.Monsters.First(e => ArchMonsters.IsArch(e.GenericId)); r["cell"] = DynValue.NewNumber(m.CellId); r["id"] = DynValue.NewNumber(m.ActorId); r["name"] = DynValue.NewString(arch.Name ?? ""); r["gid"] = DynValue.NewNumber(arch.GenericId); }));
        map["getResources"] = DynValue.NewCallback((_, a) => BuildList(s, _world.Get(_pid).Interactives.Where(x => x.Harvestable), (r, x) => { r["cell"] = DynValue.NewNumber(x.CellId); r["name"] = DynValue.NewString(x.Name ?? ""); r["skill"] = DynValue.NewNumber(x.HarvestSkill?.SkillId ?? 0); }));
        // getNpcs() : { cell, name (nom localisé SEUL, « PNJ <id> » si inconnu), id (acteur, négatif), npcId (modèle), label }.
        // label = ancien libellé « Nom (npcId) » gardé pour les scripts qui le comparaient (compat) ; matcher de préférence par npcId.
        map["getNpcs"] = DynValue.NewCallback((_, a) => BuildList(s, _world.Get(_pid).Actors.Where(x => x.Kind == MapActorKind.Npc), (r, x) => { r["cell"] = DynValue.NewNumber(x.CellId); r["name"] = DynValue.NewString(x.Name ?? ""); r["id"] = DynValue.NewNumber(x.ActorId); r["npcId"] = DynValue.NewNumber(x.NpcId); r["label"] = DynValue.NewString(NpcLegacyLabel(x)); }));
        // Lectures géométriques PURES (wrappers sur BotWorldState + MapPathfinder statique, zéro wire).
        map["isWalkable"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_world.Get(_pid).IsWalkableForScript(N(a, 0))));                                   // isWalkable(cell)
        map["cellDistance"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(GivreClone.Protocol.MapPathfinder.CellDistance(N(a, 0), N(a, 1))));       // distance d'interaction (diamant)
        map["adjacentCells"] = DynValue.NewCallback((_, a) =>   // 4 voisines orthogonales marchables d'une cellule (MapPoint ±1)
        {
            var (x, y) = GivreClone.Protocol.MapPathfinder.MapPointOf(N(a, 0));
            var cells = new[]
            {
                GivreClone.Protocol.MapPathfinder.CellFromMapPoint(x - 1, y), GivreClone.Protocol.MapPathfinder.CellFromMapPoint(x + 1, y),
                GivreClone.Protocol.MapPathfinder.CellFromMapPoint(x, y - 1), GivreClone.Protocol.MapPathfinder.CellFromMapPoint(x, y + 1),
            }.Where(c => c >= 0);
            return BuildList(s, cells, (r, c) => r["cell"] = DynValue.NewNumber(c));
        });
        map["playersOnMap"] = DynValue.NewCallback((_, a) => BuildList(s,   // autres JOUEURS visibles (self exclu) { cell, name, id }
            _world.Get(_pid).Actors.Where(x => x.Kind == MapActorKind.Player && x.ActorId != _world.Get(_pid).SelfActorId),
            (r, x) => { r["cell"] = DynValue.NewNumber(x.CellId); r["name"] = DynValue.NewString(x.Name ?? ""); r["id"] = DynValue.NewNumber(x.ActorId); }));
        map["idAt"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.MapIdAt(N(a, 0), N(a, 1), _launch.CurrentWorldMap(_pid)) ?? 0));   // meme calque que le perso
        map["getId"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.CurrentMap(_pid) ?? 0));
        map["id"] = map.Get("getId");             // ALIAS tolérant (scripts IA / portés d'autres bots appellent map:id())
        map["currentMapId"] = map.Get("getId");   // idem : map:currentMapId()
        // (lectures pures): onMap(mapId | "x,y"), onCell(cell), currentMap -> "x,y", currentCell -> cellule.
        map["onMap"] = DynValue.NewCallback((_, a) =>
        {
            int j = B(a); if (j >= a.Count) return DynValue.False;
            long cur = _launch.CurrentMap(_pid) ?? 0; var v = a[j];
            if (v.Type == DataType.Number) return DynValue.NewBoolean(cur != 0 && (long)v.Number == cur);
            string sv = (v.CastToString() ?? "").Trim();
            if (long.TryParse(sv, out long id)) return DynValue.NewBoolean(cur != 0 && id == cur);
            var st = _world.Get(_pid);
            return DynValue.NewBoolean(TryParseCoord(sv, out int x, out int y) && st.MapX == x && st.MapY == y);
        });
        map["onCell"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_world.Get(_pid).SelfCell is int c && c == N(a, 0)));
        map["currentMap"] = DynValue.NewCallback((_, a) => { var st = _world.Get(_pid); return DynValue.NewString(st.MapX is int x && st.MapY is int y ? $"{x},{y}" : ""); });
        map["currentCell"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_world.Get(_pid).SelfCell ?? -1));
        // ---- Lot B ----
        BindMapLotB(s, map, ct);
        // ---- Lot J : coffres / maisons à code ----
        BindMapLotJ(s, map, ct);
        s.Globals["map"] = DynValue.NewTable(map);

        // ---- combat: stats du perso en combat (décodées jxg/kub) + sorts appris ----
        var combat = new Table(s);
        // Lot F : SelfLife = mon combattant en combat (inchangé) ; hors combat = vrais PV (fiche + régénération) au lieu du max.
        combat["getHp"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.SelfLife(_pid)?.Cur ?? _launch.SelfStats(_pid)?.Life ?? 0));
        combat["getMaxHp"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.SelfLife(_pid)?.Max ?? _launch.SelfStats(_pid)?.MaxLife ?? 0));
        combat["getPa"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.SelfStats(_pid)?.Pa ?? 0));
        combat["getPm"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.SelfStats(_pid)?.Pm ?? 0));
        combat["getRange"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.SelfRange(_pid)));
        combat["getSpellLevel"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.LearnedSpellLevel(_pid, N(a, 0))));
        combat["getSpells"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.LearnedSpells(_pid), (r, id) => { r["id"] = DynValue.NewNumber(id); r["level"] = DynValue.NewNumber(_launch.LearnedSpellLevel(_pid, id)); }));
        // Lecture du PLATEAU (Lot 2-F) : combattants + ma cellule + n° de tour + est-ce mon tour + zone d'AoE d'un sort.
        combat["getCell"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.FightSelfCell(_pid)));
        combat["getTurnNo"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.FightTurnNo(_pid)));
        combat["isMyTurn"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.IsMyTurnOpen(_pid)));
        // Combattants : clés historiques INCHANGÉES (cell, id, enemy, self, hp = % PV) + clés détaillées (PV/PA/PM absolus,
        // équipe, invocation, vivant). enemies()/allies() : mêmes clés (sans enemy/self historiques, comme avant).
        void FillFighter(Table r, LaunchService.ScriptFighter f, bool legacyFlags)
        {
            r["cell"] = DynValue.NewNumber(f.Cell); r["id"] = DynValue.NewNumber(f.ActorId);
            if (legacyFlags) { r["enemy"] = DynValue.NewBoolean(f.IsEnemy); r["self"] = DynValue.NewBoolean(f.IsSelf); }
            r["hp"] = DynValue.NewNumber(f.HpPercent);
            r["hpPercent"] = DynValue.NewNumber(f.HpPercent);
            r["lifePoints"] = DynValue.NewNumber(f.Life); r["maxLifePoints"] = DynValue.NewNumber(f.MaxLife);
            r["pa"] = DynValue.NewNumber(f.Pa); r["pm"] = DynValue.NewNumber(f.Pm);
            r["team"] = DynValue.NewNumber(f.Team); r["isEnemy"] = DynValue.NewBoolean(f.IsEnemy);
            r["isSummon"] = DynValue.NewBoolean(f.IsSummon); r["isMySummon"] = DynValue.NewBoolean(f.IsMySummon);
            r["alive"] = DynValue.NewBoolean(f.Alive);
        }
        combat["fighters"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.FightersDetail(_pid), (r, f) => FillFighter(r, f, true)));
        combat["turnOrder"] = DynValue.NewCallback((_, a) =>   // ordre de jeu déduit des débuts de tour : { id, current }, complet après le 1er tour de table
        {
            var (order, current, _) = _launch.FightTurnOrder(_pid);
            return BuildList(s, order, (r, id) => { r["id"] = DynValue.NewNumber(id); r["current"] = DynValue.NewBoolean(id == current); });
        });
        combat["enemies"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.FightersDetail(_pid).Where(f => f.IsEnemy), (r, f) => FillFighter(r, f, false)));
        combat["allies"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.FightersDetail(_pid).Where(f => !f.IsEnemy && !f.IsSelf), (r, f) => FillFighter(r, f, false)));
        combat["spellZone"] = DynValue.NewCallback((_, a) =>   // cellules touchées par un sort (spellId, centerCell) — géométrie AoE réelle
        {
            int spellId = N(a, 0), center = N(a, 1);
            if (_launch.SpellZoneOf(spellId, _pid) is not { } zz) return BuildList(s, System.Array.Empty<int>(), (r, c) => { });
            var cells = GivreClone.Protocol.CombatZones.GetCells(zz.Shape, center,
                GivreClone.Protocol.CombatZones.OrientationTo(_launch.FightSelfCell(_pid), center), zz.MinSize, zz.Size);
            return BuildList(s, cells, (r, c) => r["cell"] = DynValue.NewNumber(c));
        });
        // ACTIONS (Lot 2-F) : le script (auto-combat UI OFF) joue son tour. Self-gate _myTurnOpen côté moteur.
        combat["castOnCell"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.CastSpell(_pid, N(a, 1), N(a, 0), HookEpoch)));  // (spellId, cell)
        // moveToCell(cell) -> moved, finalCell : tronqué aux PM restants, BLOQUANT jusqu'à la confirmation serveur.
        DynValue MoveToCell(CallbackArguments a)
        {
            var (moved, cell) = _launch.ScriptCombatMoveAsync(_pid, N(a, 0), ct, HookEpoch).GetAwaiter().GetResult();
            return DynValue.NewTuple(DynValue.NewBoolean(moved), DynValue.NewNumber(cell));
        }
        combat["moveToCell"] = DynValue.NewCallback((_, a) => MoveToCell(a));
        combat["reachableCells"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.CombatReachable(_pid), (r, c) => r["cell"] = DynValue.NewNumber(c)));
        combat["endTurn"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.EndTurn(_pid, HookEpoch)));

        // ---- Lot C1 : lectures (sans droit) ----
        // canCast(spellId, cell [, fromCell]) -> ok, raison.
        combat["canCast"] = DynValue.NewCallback((_, a) =>
        {
            int j = B(a) + 2;
            int from = j < a.Count && a[j].Type == DataType.Number ? (int)a[j].Number : -1;
            var (ok, reason) = _launch.ScriptCanCast(_pid, N(a, 0), N(a, 1), from);
            return DynValue.NewTuple(DynValue.NewBoolean(ok), DynValue.NewNumber(reason));
        });
        combat["hasLos"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.HasLos(_pid, N(a, 0), N(a, 1))));
        // zoneCells(shape, center, minSize, size [, fromCell]) : shape = code (88) ou lettre ("X").
        IReadOnlyList<int> ZoneCells(CallbackArguments a)
        {
            int j = B(a);
            int shape = j < a.Count && a[j].Type == DataType.String ? (a[j].String.Length > 0 ? a[j].String[0] : 0) : N(a, 0);
            int center = N(a, 1), minSize = N(a, 2), size = N(a, 3);
            int fj = j + 4;
            int from = fj < a.Count && a[fj].Type == DataType.Number ? (int)a[fj].Number : -1;
            if (from < 0) from = _launch.FightSelfCell(_pid);
            if (from < 0) from = _world.Get(_pid).SelfCell ?? center;
            return GivreClone.Protocol.CombatZones.GetCells(shape, center, GivreClone.Protocol.CombatZones.OrientationTo(from, center), minSize, size);
        }
        combat["zoneCells"] = DynValue.NewCallback((_, a) => BuildList(s, ZoneCells(a), (r, c) => r["cell"] = DynValue.NewNumber(c)));
        bool IsFreeCell(int cell)
        {
            if (_launch.IsInFight(_pid)) return _launch.IsFreeFightCell(_pid, cell);
            var st = _world.Get(_pid);
            return st.IsWalkable(cell) && !st.Actors.Any(x => x.CellId == cell) && !st.Monsters.Any(x => x.CellId == cell);
        }
        combat["isFreeCell"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(IsFreeCell(N(a, 0))));
        combat["fightPath"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.FightPath(_pid, N(a, 0), N(a, 1)), (r, c) => r["cell"] = DynValue.NewNumber(c)));
        combat["placementCells"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.PlacementCells(_pid), (r, c) => r["cell"] = DynValue.NewNumber(c)));
        combat["isPlacement"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.IsPlacementOpen(_pid)));
        combat["pointsLeft"] = DynValue.NewCallback((_, a) => { var (ap, mp) = _launch.ScriptPointsLeft(_pid); return DynValue.NewTuple(DynValue.NewNumber(ap), DynValue.NewNumber(mp)); });
        combat["isAutoFight"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.IsAutoFightEffective(_pid)));

        // ---- Lot C1 : actions (Perm.Fight au point d'action, côté moteur) ----
        combat["playTurn"] = DynValue.NewCallback((_, a) =>
        {
            bool ok = _launch.PlayTurnForScriptAsync(_pid, HookEpoch, ct).GetAwaiter().GetResult();
            if (!ok) Warn("combat", Loc.T("L.Lua.PlayTurnRefused"));
            return DynValue.NewBoolean(ok);
        });
        combat["setAutoFight"] = DynValue.NewCallback((_, a) =>
        {
            int j = B(a);
            bool v = j < a.Count && a[j].CastToBool();
            if (v && !LicenseService.HasPermission(LicenseService.Perm.Fight)) { Warn("combat", Loc.T("L.Lua.NoPermFight")); return DynValue.False; }
            _launch.SetAutoFightOverride(_pid, _runToken, v);
            Info("combat", Loc.T(v ? "L.Lua.AutoFightScriptOn" : "L.Lua.AutoFightScriptOff"));
            return DynValue.NewBoolean(_launch.IsAutoFightEffective(_pid));
        });
        combat["place"] = DynValue.NewCallback((_, a) =>
        {
            bool ok = _launch.ScriptPlace(_pid, N(a, 0));
            if (ok) _scriptPlaceOk++;   // compat tiers: prefightManagement s'est placé -> pas de placement du moteur ensuite
            return DynValue.NewBoolean(ok);
        });
        combat["ready"] = DynValue.NewCallback((_, a) => { int j = B(a); bool rd = j >= a.Count || a[j].CastToBool(); return DynValue.NewBoolean(_launch.ScriptReady(_pid, rd)); });   // défaut true
        combat["kick"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.KickFighter(_pid, L(a, 0))));
        BindCombatC2(s, combat, ct);
        s.Globals["combat"] = DynValue.NewTable(combat);

        // ---- fightAction: alias tiers (portabilité), délégations PURES; listes = tableaux de NOMBRES ----
        var fa = new Table(s);
        fa["castSpellOnCell"] = combat.Get("castOnCell");
        fa["canCastSpellOnCell"] = DynValue.NewCallback((_, a) =>   // (startCell, spell, cell) ; startCell nil -> ma case
        {
            int j = B(a);
            int start = j < a.Count && a[j].Type == DataType.Number ? (int)a[j].Number : -1;
            return DynValue.NewNumber(_launch.ScriptCanCast(_pid, N(a, 1), N(a, 2), start).Reason);
        });
        fa["inLineOfSight"] = combat.Get("hasLos");
        fa["moveTowardCell"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.ScriptCombatMoveAsync(_pid, N(a, 0), ct, HookEpoch).GetAwaiter().GetResult().Moved));
        fa["passTurn"] = combat.Get("endTurn");
        fa["isFreeCell"] = combat.Get("isFreeCell");
        fa["getReachableCells"] = DynValue.NewCallback((_, a) => BuildNums(s, _launch.CombatReachable(_pid)));
        fa["getSpellZone"] = DynValue.NewCallback((_, a) =>
        {
            int spellId = N(a, 0), center = N(a, 1);
            if (_launch.SpellZoneOf(spellId, _pid) is not { } zz) return BuildNums(s, System.Array.Empty<int>());
            return BuildNums(s, GivreClone.Protocol.CombatZones.GetCells(zz.Shape, center,
                GivreClone.Protocol.CombatZones.OrientationTo(_launch.FightSelfCell(_pid), center), zz.MinSize, zz.Size));
        });
        fa["getCurrentTurn"] = combat.Get("getTurnNo");
        fa["getDistance"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(GivreClone.Protocol.MapPathfinder.CellDistance(N(a, 0), N(a, 1))));
        fa["isHandToHand"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(GivreClone.Protocol.MapPathfinder.CellDistance(N(a, 0), N(a, 1)) <= 1));
        fa["chooseCell"] = combat.Get("place");
        s.Globals["fightAction"] = DynValue.NewTable(fa);

        // ---- team: groupe DOFUS au niveau paquet (invite PAR NOM / accept / leave) — mêmes forges que l'auto-équipe ----
        var team = new Table(s);
        team["invite"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.PartyInvite(_pid, S(a, 0))));
        team["accept"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.PartyAccept(_pid, L(a, 0))));
        team["leave"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.PartyLeave(_pid, L(a, 0))));
        // Composition de l'ÉQUIPE GivreClone (snapshot poussé par BotsViewModel) : chaque membre EN JEU avec sa carte/combat VIVANTS.
        team["members"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.TeamMembers(_pid), (r, m) =>
        {
            r["pid"] = DynValue.NewNumber(m.Pid); r["name"] = DynValue.NewString(m.Name); r["leader"] = DynValue.NewBoolean(m.IsLeader);
            r["mapId"] = DynValue.NewNumber(_launch.CurrentMap(m.Pid) ?? 0);        // carte VIVANTE (pas figée dans le snapshot)
            r["inFight"] = DynValue.NewBoolean(_launch.IsInFight(m.Pid));
            r["self"] = DynValue.NewBoolean(m.Pid == _pid);
        }));
        team["isLeader"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.IsTeamLeader(_pid)));       // suis-je le chef de mon équipe ?
        team["leaderName"] = DynValue.NewCallback((_, a) => DynValue.NewString(_launch.TeamLeaderName(_pid)));    // nom du chef ("" hors équipe)
        // broadcast(script) : LANCE le snippet Lua sur tous les AUTRES membres EN JEU de mon équipe (remplace leur script courant,
        // « run on all »). Enfilé ici, exécuté par BotsViewModel au Tick (thread UI, où vivent les hôtes).
        team["broadcast"] = DynValue.NewCallback((_, a) => { _launch.EnqueueTeamBroadcast(_pid, S(a, 0)); return DynValue.True; });
        // Lot B : team:joinLeader() (= /joinBoss) : le CHEF ordonne aux membres absents de sa carte de la rejoindre. Non bloquant.
        team["joinLeader"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(JoinLeader()));
        s.Globals["team"] = DynValue.NewTable(team);

        // ---- trade: ÉCHANGE joueur↔joueur (forges Exchange*). La cible est un JOUEUR VISIBLE sur la carte courante :
        //      withPlayer(name) résout le nom -> id d'acteur roleplay (même store que map:getNpcs) puis ouvre l'échange.
        //      Poser/retirer objets (par GID) + kamas, puis ready()/validate(). accept() = accepter une demande reçue.
        //      ⚠️ PIÈGE move() : verbes à appeler dans move()/bank(). Lot D : withPlayer/ready/validate/acceptFrom sont BLOQUANTS
        //      (attente d'ouverture / d'« échange conclu ») ; pending/acceptFrom/refuse/isOpen/autoAcceptTeam (mode mule). ----
        var trade = new Table(s);
        // withPlayer(name [, timeoutSec=30]) : demande l'échange et ATTEND l'ouverture (true) / le refus (false) / le délai (false, demande annulée).
        trade["withPlayer"] = DynValue.NewCallback((_, a) =>
        {
            string name = S(a, 0);
            var actor = _world.Get(_pid).Actors.FirstOrDefault(x => x.Kind == MapActorKind.Player
                && string.Equals(x.Name, name, System.StringComparison.OrdinalIgnoreCase));
            if (actor is null) { Warn("échange", Loc.Fmt("L.Lua.TradeNoPlayer", name)); return DynValue.False; }
            return DynValue.NewBoolean(TradeWithPlayerBlocking(actor.ActorId, name, TimeoutSecArg(a, 1, 30), ct));
        });
        trade["putItem"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.TradePutItem(_pid, L(a, 0), N(a, 1))));
        trade["removeItem"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.TradeRemoveItem(_pid, L(a, 0), N(a, 1))));
        trade["putKamas"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.TradePutKamas(_pid, L(a, 0))));
        // ready([ready=true [, timeoutSec=60]]) : false = retire son « prêt » (non bloquant, inchangé) ; sinon se déclare prêt
        // et ATTEND la fin de l'échange (true = conclu).
        trade["ready"] = DynValue.NewCallback((_, a) =>
        {
            int j = B(a);
            bool rd = j >= a.Count || a[j].IsNil() || a[j].CastToBool();   // défaut true
            if (!rd) return DynValue.NewBoolean(_launch.TradeSetReady(_pid, false));
            return DynValue.NewBoolean(TradeReadyBlocking(TimeoutSecArg(a, 1, 60), ct));
        });
        trade["validate"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(TradeReadyBlocking(TimeoutSecArg(a, 0, 60), ct)));   // alias ready(true, t)
        trade["accept"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.TradeAccept(_pid)));             // accepte une demande reçue (non bloquant)
        trade["cancel"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.TradeCancel(_pid)));
        trade["refuse"] = trade.Get("cancel");   // refuser une demande reçue = fermer le dialogue (DialogLeaveRequest)
        trade["isOpen"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.TradeOpen(_pid)));
        // pending() : demande d'échange REÇUE en attente { id, name, type (exchange_type), age (s) } ou nil.
        trade["pending"] = DynValue.NewCallback((_, a) =>
        {
            if (_launch.TradePending(_pid) is not { } p) return DynValue.Nil;
            var t = new Table(s);
            t["id"] = DynValue.NewNumber(p.SourceId);
            t["name"] = DynValue.NewString(ResolveCharacterName(p.SourceId));
            t["type"] = DynValue.NewNumber(p.Type);
            t["age"] = DynValue.NewNumber(Math.Round(p.AgeMs / 1000.0, 1));
            return DynValue.NewTable(t);
        });
        // acceptFrom([name] [, timeoutSec=10]) : attend une demande (de `name`, ou de n'importe qui si "") puis l'accepte et
        // attend l'ouverture de l'échange. Nombre seul en 1er argument = délai.
        trade["acceptFrom"] = DynValue.NewCallback((_, a) =>
        {
            int j = B(a);
            string name = ""; int tIdx = 0;
            if (j < a.Count && a[j].Type == DataType.String) { name = a[j].String; tIdx = 1; }
            else if (j < a.Count && a[j].IsNil()) tIdx = 1;
            int t = TimeoutSecArg(a, tIdx, 10);
            bool Match() => _launch.TradePending(_pid) is { } p && (name.Length == 0 || string.Equals(ResolveCharacterName(p.SourceId), name, System.StringComparison.OrdinalIgnoreCase));
            if (!WaitUntil(Match, t * 1000, ct)) { Warn("échange", Loc.Fmt("L.Lua.TradeNoPending", name)); return DynValue.False; }
            string who = name.Length > 0 ? name : (_launch.TradePending(_pid) is { } p2 ? ResolveCharacterName(p2.SourceId) : "");
            bool ok = _launch.TradeAcceptAsync(_pid, ct).GetAwaiter().GetResult();
            if (ok) Ok("échange", Loc.Fmt("L.Lua.TradeStarted", who));
            else Warn("échange", Loc.Fmt("L.Lua.TradeNotStarted", who));
            return DynValue.NewBoolean(ok);
        });
        // autoAcceptTeam(on [, autoReady=true]) : mode MULE — accepte les échanges des autres bots de l'équipe (+ prêt auto).
        trade["autoAcceptTeam"] = DynValue.NewCallback((_, a) =>
        {
            int j = B(a);
            bool on = j >= a.Count || a[j].IsNil() || a[j].CastToBool();
            bool autoReady = j + 1 >= a.Count || a[j + 1].IsNil() || a[j + 1].CastToBool();
            bool ok = _launch.SetTradeAutoAcceptTeam(_pid, on, autoReady);
            if (on && !ok) Warn("échange", Loc.T("L.Lua.NoPermMulti"));
            else if (on) Ok("échange", Loc.T("L.Lua.TradeAutoTeamOn"));
            else Info("échange", Loc.T("L.Lua.TradeAutoTeamOff"));
            return DynValue.NewBoolean(ok);
        });
        // players() : joueurs VISIBLES sur la carte courante { name, id, cell } — aide le script à choisir une cible.
        trade["players"] = DynValue.NewCallback((_, a) => BuildList(s, _world.Get(_pid).Actors.Where(x => x.Kind == MapActorKind.Player),
            (r, x) => { r["name"] = DynValue.NewString(x.Name ?? ""); r["id"] = DynValue.NewNumber(x.ActorId); r["cell"] = DynValue.NewNumber(x.CellId); }));
        s.Globals["trade"] = DynValue.NewTable(trade);

        // ---- inventory: sac (uid/gid/qté) + métadonnées objet + pods ----
        var inv = new Table(s);
        void FillInvItem(Table r, InventoryStore.Item it) { r["uid"] = DynValue.NewNumber(it.Uid); r["gid"] = DynValue.NewNumber(it.Gid); r["qty"] = DynValue.NewNumber(it.Qty); r["name"] = DynValue.NewString(_launch.ItemDisplayName(it.Gid) ?? ""); r["pos"] = DynValue.NewNumber(it.Position); r["equipped"] = DynValue.NewBoolean(it.Position != 63); }
        inv["items"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.Inventory.Items(_pid), FillInvItem));
        // Lot B : objets ÉQUIPÉS (position != 63 = sac), mêmes clés que items() (pos = position d'équipement brute).
        inv["equipped"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.Inventory.Equipped(_pid), FillInvItem));   // Lot I : triés par position
        inv["count"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.InventoryCount(_pid, L(a, 0))));
        inv["category"] = DynValue.NewCallback((_, a) => DynValue.NewString(_launch.ItemCategory(L(a, 0)) ?? ""));
        inv["typeId"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.ItemTypeId(L(a, 0))));
        inv["getPodsPercent"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_world.Get(_pid).PodsPercent));
        // use(gid [, count]) : UTILISE/OUVRE un objet par GID (ObjectUseRequest iuu). count omis/0 = TOUS les exemplaires
        // du sac (ex: ouvrir tous les « Sac de Blé »). useInventoryItem. Gaté Perm.Gather côté moteur.
        // Journalisation DANS le verbe : les alias useItem / useInventoryItem journalisent aussi.
        inv["use"] = DynValue.NewCallback((_, a) =>
        {
            long gid = L(a, 0);
            int cnt = N(a, 1);                                   // tolère l'appel colon (avant : a[1] = gid lu comme quantité)
            if (cnt <= 0) cnt = _launch.InventoryCount(_pid, gid);
            if (gid <= 0 || cnt <= 0) return DynValue.False;     // rien à utiliser : SILENCIEUX (scripts qui sondent à chaque tick)
            bool ok = UseItems(gid, cnt, ct);                    // Lot D : n>1 -> ObjectUseMultipleRequest (repli iuu) ; gate Perm.Gather au point d'action (moteur)
            if (ok) Ok("inventaire", Loc.Fmt("L.Lua.ItemUsed", cnt, _launch.ItemDisplayName(gid), gid));
            else Warn("inventaire", Loc.Fmt("L.Lua.ItemUseFail", _launch.ItemDisplayName(gid), gid));
            return DynValue.NewBoolean(ok);
        });
        // openBags() : ouvre TOUS les sacs de ressources du sac (objets « Sac de … »/« Sac d'… ») d'un coup. Renvoie le nb ouvert.
        inv["openBags"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(OpenResourceBags(ct)));
        // delete(gid [, count]) : DÉTRUIT un objet par GID (ObjectDeleteRequest itd ; ≠ drop = supprimé, pas au sol).
        // count omis/0 = tout le stack. Vide-poche automatique : global AUTO_DELETE (+ OPEN_BAGS), joué par la boucle quand
        // les pods atteignent MAX_PODS (RunPodsMaintenance). Gaté Perm.Gather côté moteur.
        inv["delete"] = DynValue.NewCallback((_, a) => { long gid = L(a, 0); return DynValue.NewBoolean(gid > 0 && _launch.DeleteInventoryItem(_pid, gid, N(a, 1))); });
        s.Globals["inventory"] = DynValue.NewTable(inv);
        // Alias GLOBAUX.
        s.Globals["useItem"] = inv.Get("use");
        s.Globals["useInventoryItem"] = inv.Get("use");
        s.Globals["openBags"] = inv.Get("openBags");
        s.Globals["deleteItem"] = inv.Get("delete");
        s.Globals["deleteInventoryItem"] = inv.Get("delete");

        // ---- bank: banque (dépôt tout en 1 appel via voyage+ouverture, kamas, contenu) ----
        var bank = new Table(s);
        bank["depositAll"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.BankDepositAllAsync(_pid, L(a, 0), ct).GetAwaiter().GetResult()));
        bank["getKamas"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.BankKamas(_pid)));
        bank["count"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.Bank.CountByGid(_pid, L(a, 0))));
        bank["items"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.Bank.Items(_pid), (r, it) => { r["uid"] = DynValue.NewNumber(it.Uid); r["gid"] = DynValue.NewNumber(it.Gid); r["qty"] = DynValue.NewNumber(it.Qty); r["name"] = DynValue.NewString(_launch.ItemDisplayName(it.Gid) ?? ""); }));
        // Lot D : banque PILOTÉE (le banquier doit être sur la carte courante, pas de voyage). Tous BLOQUANTS, à appeler dans
        // move()/bank(). Les verbes de retrait/dépôt exigent un coffre ouvert (bank:open) ; globals plats inchangés.
        bank["open"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(BankOpenLua(L(a, 0), ct)));                 // open([npcMapId])
        bank["close"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(BankCloseLua(ct)));
        bank["isOpen"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.StorageOpen(_pid)));
        bank["withdraw"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(BankWithdrawLua(L(a, 0), N(a, 1), "bank:withdraw", ct)));   // withdraw(gid [, qty])
        bank["putAllItemsExcept"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(BankPutAllExceptLua(GidSetArgs(a, bank), "bank:putAllItemsExcept", ct)));
        bank["putKamas"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(BankPutKamasLua(L(a, 0), "bank:putKamas", ct)));
        bank["withdrawKamas"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(BankWithdrawKamasLua(L(a, 0), "bank:withdrawKamas", ct)));
        bank["deposit"] = s.Globals.Get("depositItem");   // deposit(gid [, n]) = global depositItem (sans garde coffre, inchangé)
        _bankModule = bank;   // gardé : un script qui déclare function bank() écrase ce global pendant DoString (RestoreBankModule)
        s.Globals["bank"] = DynValue.NewTable(bank);

        // ---- exchange: compat tiers (Exchangeplug) — route vers l'échange joueur OUVERT, sinon vers le coffre ouvert. ----
        var exchange = new Table(s);
        exchange["putItem"] = DynValue.NewCallback((_, a) =>
        {
            long gid = L(a, 0); int n = N(a, 1);
            if (_launch.TradeOpen(_pid)) return DynValue.NewBoolean(_launch.TradePutItem(_pid, gid, n));
            if (_launch.StorageOpen(_pid)) return DynValue.NewBoolean(_launch.BankDepositItem(_pid, gid, n));
            Warn("banque", Loc.Fmt("L.Lua.BankNotOpen", "exchange:putItem"));
            return DynValue.False;
        });
        exchange["getItem"] = DynValue.NewCallback((_, a) =>   // getItem(gid, qty [, delay]) : delay ignoré
        {
            long gid = L(a, 0); int n = N(a, 1);
            if (_launch.StorageOpen(_pid)) return DynValue.NewBoolean(BankWithdrawLua(gid, n, "exchange:getItem", ct));
            if (_launch.TradeOpen(_pid)) return DynValue.NewBoolean(_launch.TradeRemoveItem(_pid, gid, n));
            Warn("banque", Loc.Fmt("L.Lua.BankNotOpen", "exchange:getItem"));
            return DynValue.False;
        });
        exchange["putKamas"] = DynValue.NewCallback((_, a) =>
        {
            long n = L(a, 0);
            if (_launch.TradeOpen(_pid))
            {
                if (n <= 0) n = _launch.Kamas(_pid);
                return DynValue.NewBoolean(n > 0 && _launch.TradePutKamas(_pid, n));
            }
            return DynValue.NewBoolean(BankPutKamasLua(n, "exchange:putKamas", ct));
        });
        // ⚠ getKamas(n) = RETIRER du coffre; bank:getKamas reste la LECTURE du solde.
        exchange["getKamas"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(BankWithdrawKamasLua(L(a, 0), "exchange:getKamas", ct)));
        exchange["putAllItems"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.BankDepositAll(_pid)));
        exchange["getAllItems"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.BankWithdrawAll(_pid)));
        exchange["putAllItemsExcept"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(BankPutAllExceptLua(GidSetArgs(a, exchange), "exchange:putAllItemsExcept", ct)));
        exchange["getAllItemsExcept"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(BankWithdrawAllExceptLua(GidSetArgs(a, exchange), ct)));
        exchange["storageItems"] = DynValue.NewCallback((_, a) => BuildNumsL(s, _launch.Bank.Items(_pid).Select(i => i.Gid)));
        exchange["storageItemQuantity"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.Bank.CountByGid(_pid, L(a, 0))));
        exchange["storageItemCount"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.Bank.StackCount(_pid)));
        exchange["storageKamas"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.BankKamas(_pid)));
        exchange["leave"] = bank.Get("close");
        exchange["ready"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(TradeReadyBlocking(60, ct)));
        exchange["Ready"] = exchange.Get("ready");
        exchange["launchExchangeWithPlayer"] = DynValue.NewCallback((_, a) => { long id = L(a, 0); return DynValue.NewBoolean(id != 0 && TradeWithPlayerBlocking(id, id.ToString(System.Globalization.CultureInfo.InvariantCulture), 30, ct)); });
        exchange["launchExchangeWithPlayerByName"] = trade.Get("withPlayer");
        s.Globals["exchange"] = DynValue.NewTable(exchange);

        // ---- zaap: téléport DIRECT vers un zaap CONNU (false si non appris ou téléport refusé) ----
        var zaap = new Table(s);
        zaap["travel"] = DynValue.NewCallback((_, a) => { long m = L(a, 0); return DynValue.NewBoolean(_launch.CanTeleportTo(_pid, m) && _launch.Teleport(_pid, m)); });
        // goto(mapId) : marche jusqu'au zaap PRÉSENT sur la carte courante, l'ouvre, et téléporte vers `mapId` (destination de ce zaap).
        zaap["goto"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(ZaapGoto(L(a, 0), ct)));
        zaap["canGoTo"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.CanTeleportTo(_pid, L(a, 0))));
        zaap["known"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.KnownZaapMaps(_pid), (r, m) => { r["mapId"] = DynValue.NewNumber(m); }));
        // fromMap(mapId) : TeleportRequest source_type 5, sans zaap au sol ni havre-sac. true seulement si la carte devient mapId.
        zaap["fromMap"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(TravelFromWorldMap(L(a, 0), ct)));
        // mapCost(mapId) : dernier TeleportDestination.cost connu, ou nil si le coût (ou le zaap) est inconnu.
        zaap["mapCost"] = DynValue.NewCallback((_, a) =>
        {
            var offer = _launch.AssessWorldMapZaap(_pid, L(a, 0));
            return offer.Known && offer.CostKamas is long c ? DynValue.NewNumber(c) : DynValue.Nil;
        });
        // route("auto"|"carte"|"sol"|"havre"|"zaapi") : choix du raccourci. Le havre-sac et le zaap au sol restent disponibles.
        zaap["route"] = DynValue.NewCallback((_, a) =>
        {
            if (!WorldMapZaapTravel.TryParseRoute(S(a, 0), out var route))
            {
                Warn("trajet", WorldMapZaapTravel.RouteNamesReason);
                return DynValue.False;
            }
            _launch.SetZaapRoute(_pid, route);
            Info("trajet", WorldMapZaapTravel.ChosenReason(route));
            return DynValue.True;
        });
        // ⚠ `goto` est un MOT-CLÉ Lua (MoonSharp) : `zaap:goto(x)` est une ERREUR DE SYNTAXE. Alias appelable : zaap:go(x).
        zaap["go"] = zaap.Get("goto");
        s.Globals["zaap"] = DynValue.NewTable(zaap);

        // ---- zaapi: téléport par le zaapi de la carte courante. Les destinations viennent du dernier
        //      TeleportDestinationsEvent type 1 (nom, quartier, coordonnées, coût, onglet). go = forme appelable
        //      (zaapi:goto est une erreur de syntaxe : `goto` est un mot-clé). ----
        var zaapi = new Table(s);
        zaapi["goto"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(ZaapiGoto(L(a, 0), ct)));
        zaapi["go"] = zaapi.Get("goto");
        zaapi["canGoTo"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.CanTeleportTo(_pid, L(a, 0))));
        zaapi["destinations"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.ZaapiStops(_pid), (r, stop) =>
        {
            r["mapId"] = DynValue.NewNumber(stop.MapId);
            r["name"] = DynValue.NewString(stop.Name ?? "");
            r["district"] = DynValue.NewString(stop.District ?? "");
            r["subAreaId"] = DynValue.NewNumber(stop.SubAreaId);
            if (stop.X is int x) r["x"] = DynValue.NewNumber(x);
            if (stop.Y is int y) r["y"] = DynValue.NewNumber(y);
            r["coords"] = DynValue.NewString(ZaapiTravel.Coordinates(stop.X, stop.Y));
            if (stop.CostKamas is long cost) r["cost"] = DynValue.NewNumber(cost);
            r["category"] = DynValue.NewString(stop.Category ?? "");
            if (stop.CategoryId > 0) r["categoryId"] = DynValue.NewNumber(stop.CategoryId);
        }));
        zaapi["cost"] = DynValue.NewCallback((_, a) =>
            _launch.TryZaapiCost(_pid, L(a, 0), out var cost) && cost is long c ? DynValue.NewNumber(c) : DynValue.Nil);
        s.Globals["zaapi"] = DynValue.NewTable(zaapi);

        // ---- job: métiers (niveau par id + table complète id->niveau) ----
        var job = new Table(s);
        job["level"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.JobLevel(_pid, N(a, 0))));
        job["all"] = DynValue.NewCallback((_, a) => { var t2 = new Table(s); foreach (var kv in _launch.JobLevels(_pid)) t2.Set(kv.Key, DynValue.NewNumber(kv.Value)); return DynValue.NewTable(t2); });
        s.Globals["job"] = DynValue.NewTable(job);
    }

    // ---- NativeAOT : MoonSharp ne convertit PAS les delegate Func<>/Action<> assignes aux Globals en callback
    //      appelable (marshalling par reflexion/dynamic code) -> le global devient nil ("attempt to call a nil value").
    //      On enveloppe donc CHAQUE verbe dans un DynValue.NewCallback explicite (zero reflexion), comme printMessage.
    //      Marshalling manuel args/retour. (JIT identique -> aucun changement de comportement hors AOT.)
    private static int ArgInt(CallbackArguments a, int i) => (int)(a[i].CastToNumber() ?? 0);
    private static string ArgStr(CallbackArguments a, int i) => a[i].CastToString() ?? "";
    private static DynValue Cb(Action f) => DynValue.NewCallback((_, a) => { f(); return DynValue.Nil; });
    private static DynValue Cb(Action<int> f) => DynValue.NewCallback((_, a) => { f(ArgInt(a, 0)); return DynValue.Nil; });
    private static DynValue Cb(Action<string> f) => DynValue.NewCallback((_, a) => { f(ArgStr(a, 0)); return DynValue.Nil; });
    private static DynValue Cb(Func<bool> f) => DynValue.NewCallback((_, a) => DynValue.NewBoolean(f()));
    private static DynValue Cb(Func<int> f) => DynValue.NewCallback((_, a) => DynValue.NewNumber(f()));
    private static DynValue Cb(Func<double> f) => DynValue.NewCallback((_, a) => DynValue.NewNumber(f()));
    private static DynValue Cb(Func<string> f) => DynValue.NewCallback((_, a) => DynValue.NewString(f() ?? ""));
    private static DynValue Cb(Func<int, int> f) => DynValue.NewCallback((_, a) => DynValue.NewNumber(f(ArgInt(a, 0))));
    private static DynValue Cb(Func<int, string> f) => DynValue.NewCallback((_, a) => DynValue.NewString(f(ArgInt(a, 0)) ?? ""));
    private static DynValue Cb(Func<int, bool> f) => DynValue.NewCallback((_, a) => DynValue.NewBoolean(f(ArgInt(a, 0))));
    private static DynValue Cb(Func<string, bool> f) => DynValue.NewCallback((_, a) => DynValue.NewBoolean(f(ArgStr(a, 0))));
    private static DynValue Cb(Func<int, int, bool> f) => DynValue.NewCallback((_, a) => DynValue.NewBoolean(f(ArgInt(a, 0), ArgInt(a, 1))));
    private static DynValue Cb(Func<int, int, double> f) => DynValue.NewCallback((_, a) => DynValue.NewNumber(f(ArgInt(a, 0), ArgInt(a, 1))));

    private bool _pmSwapWarned;   // sendPrivateMessage : avertissement « arguments inversés » émis UNE fois par hôte

    private bool DropItemLogged(int gid, int count)
    {
        bool ok = _launch.DropInventoryItem(_pid, gid, count);
        if (ok) Ok("inventaire", Loc.Fmt("L.Lua.ItemDropped", count, _launch.ItemDisplayName(gid), gid));
        else Warn("inventaire", Loc.Fmt("L.Lua.ItemDropFail", _launch.ItemDisplayName(gid), gid));
        return ok;
    }
}
