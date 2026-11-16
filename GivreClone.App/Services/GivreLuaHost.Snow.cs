using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using GivreClone.Engine;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ==================================================================================================================
// Compatibilité tiers — socle. Lié en DERNIER dans Bind (BindSnow, après tous les lots): ne fait que COMPLÉTER les tables
// existantes (clé déjà présente = gardée), jamais écraser un verbe GivreClone.
// * global: / config: / givreController: / d2data:;
// * mémoire RAM typée (addInMemory…, AddInGlobalMemory…, givreController:addInMemory…): nombres, booléens, chaînes et
//    tables gardent leur type (table : la MÊME instance pour le run qui l'a rangée, une copie pour un autre Script),
//    contrairement à memory:/sharedMemory: (disque, chaînes) ;
//  * tolérance de nom façon MoonSharp userdata : sale:sellItem -> SellItem, map:toZaap -> ToZaap, map:change_map -> changeMap ;
//  * dofile / load (texte) dans le même bac à sable.
// Détection de `self` PAR RÉFÉRENCE (Off, Lot K) : une table passée en appel point n'est jamais prise pour self. AOT-safe
// (DynValue.NewCallback, aucune réflexion). Exécuté UNIQUEMENT sur le thread du script.
// ==================================================================================================================
public sealed partial class GivreLuaHost
{
    // ---- mémoire RAM ----
    // Table mémorisée : instantané Items (copié pour tout AUTRE Script : autre bot, rechargement — une table MoonSharp appartient
    // à un seul Script et ne se lit que sur son fil) + table VIVANTE de son Script propriétaire (: remember rend la
    // MÊME table, une mutation en place `table.insert(global:remember("k"), v)` persiste). Owner/Live/Run : niveau racine seulement.
    private sealed record RamTable(List<KeyValuePair<object, object?>> Items, Script? Owner = null, Table? Live = null, CancellationToken Run = default);
    // global:addInMemory… : par PERSONNAGE (clé = nom), survit aux rechargements ET aux reconnexions (global:reconnect*, PLANNING,
    // relance : le pid change). Avant identification du perso : compartiment du pid.
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, object?>> s_ramByCharacter = new(StringComparer.OrdinalIgnoreCase);
    private ConcurrentDictionary<string, object?> RamAccount()
        => s_ramByCharacter.GetOrAdd(_world.Get(_pid).CharacterName is { Length: > 0 } n && _launch.ServerIdOf(_pid) is var sid && sid != 0 ? sid + ":" + n : "#" + _pid,
                                     static _ => new(StringComparer.Ordinal));
    private static readonly ConcurrentDictionary<string, object?> s_ramGlobal = new(StringComparer.Ordinal);        // global:AddInGlobalMemory… (tous les bots)
    private static readonly ConcurrentDictionary<string, object?> s_ramController = new(StringComparer.Ordinal);    // givreController:addInMemory… (toute l'app)
    private const int RamMaxDepth = 8, RamMaxItems = 10000;
    private bool _ramFnWarned;

    // ---- console (global:consoleLines) ----
    private const int ConsoleRingMax = 300;
    private readonly Queue<string> _consoleRing = new();
    private long _firstStartAt;   // 1er Start() de cet hôte (global:elapsedTime)

    /// <summary>Source du dernier script lancé par Start() (relance après global:reconnect*).</summary>
    public string? LastScriptText => _lastScriptText;

    private readonly HashSet<string> _warnedSnow = new(StringComparer.Ordinal);
    private void WarnSnow(string fn) { lock (_warnedSnow) if (!_warnedSnow.Add(fn)) return; Warn("script", Loc.Fmt("L.Lua.SnowUnsupported", fn)); }

    /// <summary>Mémorise une ligne de console (global:consoleLines). Appelé par Emit (tout thread).</summary>
    private void RememberConsoleLine(LuaLogEntry e)
    {
        lock (_consoleRing)
        {
            _consoleRing.Enqueue(e.Message);
            while (_consoleRing.Count > ConsoleRingMax) _consoleRing.Dequeue();
        }
    }

    // Modules dont les clés tolèrent les variantes de nom tiers (casse de la 1re lettre, snake_case).
    private static readonly string[] SnowModules =
    {
        "map", "combat", "fightAction", "fightChallenge", "fightCharacter", "fightBasic", "fightDebug", "fightSlave",
        "inventory", "exchange", "sale", "craft", "merchant", "bank", "trade", "zaap", "zaapi", "job", "team", "character",
        "quest", "data", "d2data", "developer", "global", "npc", "chat", "eco", "soul", "treasure", "mount", "config", "givreController",
        "stats", "memory", "sharedMemory", "http",
    };

    // ------------------------------------------------------------------ point d'entrée
    private void BindSnow(Script s, CancellationToken ct)
    {
        BindSnowGlobal(s, ct);
        BindSnowConfig(s);
        BindSnowController(s, ct);
        BindSnowScriptFiles(s, ct);
        BindSnowMap(s, ct);     // map: (mapplug.vb)
        BindSnowFight(s, ct);   // fightAction / fightCharacter / fightBasic / fightDebug / fightSlave
        BindSnowEconomy(s, ct); // inventory / exchange / sale / craft / merchant
        BindSnowNpc(s, ct);     // npc: + global:getReplies
        BindSnowChat(s);        // chat:
        BindSnowEco(s);         // eco: (Atelier éco)
        BindSnowSoul(s);        // soul: (capture d'âmes)
        BindSnowQuest(s, ct);   // quest: textes + initialize bloquant
        BindSnowCharacter(s, ct); // character: / mount:
        BindSnowD2Data(s);      // d2data: (= data: + textes)
        BindSnowTreasure(s, ct);// treasure: (chasse au trésor)
        // developer:getRequest2 = getRequest (connexion directe, mêmes limites).
        var dev = GetOrCreateTable(s, "developer");
        if (dev.Get("getRequest").Type == DataType.ClrFunction) Fill(dev, "getRequest2", dev.Get("getRequest"));
        ApplySnowNameFallback(s);   // en DERNIER : toutes les tables existent
    }

    // ------------------------------------------------------------------ helpers
    private static void Fill(Table t, string key, DynValue v)
    {
        var cur = t.RawGet(key);
        if (cur is null || cur.IsNil()) t[key] = v;
    }

    /// <summary>Enveloppe <paramref name="fn"/> pour l'appeler depuis la table <paramref name="self"/> : retire `self` (appel
    /// colon, détecté par référence) puis transmet les autres arguments tels quels. <paramref name="fn"/> est capturée au bind :
    /// un script qui redéfinit le global homonyme ne détourne pas l'alias.</summary>
    private static DynValue SnowFwd(Script s, Table self, DynValue fn) => DynValue.NewCallback((ctx, a) =>
    {
        if (fn.Type is not (DataType.ClrFunction or DataType.Function)) return DynValue.Nil;
        int off = Off(a, self);
        var args = new DynValue[Math.Max(0, a.Count - off)];
        for (int i = 0; i < args.Length; i++) args[i] = a[i + off];
        return fn.Type == DataType.ClrFunction ? fn.Callback.Invoke(ctx, args) : s.Call(fn, args);
    });

    // __index de repli : variantes de nom, puis égalité sans casse (dernier recours). Clé non-chaîne -> nil.
    private static DynValue SnowIndex(ScriptExecutionContext _, CallbackArguments a)
    {
        if (a.Count < 2 || a[0].Type != DataType.Table || a[1].Type != DataType.String) return DynValue.Nil;
        var t = a[0].Table;
        string k = a[1].String;
        foreach (var c in LuaSnowCompat.NameCandidates(k))
        {
            var v = t.RawGet(c);
            if (v is not null && !v.IsNil()) return v;
        }
        foreach (var p in t.Pairs)
            if (p.Key.Type == DataType.String && string.Equals(p.Key.String, k, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return DynValue.Nil;
    }

    private static void ApplySnowNameFallback(Script s)
    {
        var index = DynValue.NewCallback(SnowIndex);
        foreach (var name in SnowModules)
        {
            var v = s.Globals.Get(name);
            if (v.Type != DataType.Table) continue;
            var mt = v.Table.MetaTable ?? new Table(s);
            var cur = mt.RawGet("__index");
            if (cur is not null && !cur.IsNil()) continue;
            mt["__index"] = index;
            v.Table.MetaTable = mt;
        }
    }

    // ------------------------------------------------------------------ mémoire RAM typée
    private object? ToRam(DynValue v, int depth = 0)
    {
        switch (v.Type)
        {
            case DataType.Boolean: return v.Boolean;
            case DataType.Number: return v.Number;
            case DataType.String: return v.String;
            case DataType.Table:
            {
                if (depth >= RamMaxDepth) return null;
                var items = new List<KeyValuePair<object, object?>>();
                foreach (var p in v.Table.Pairs)
                {
                    if (items.Count >= RamMaxItems) break;
                    object? k = p.Key.Type switch
                    {
                        DataType.Number => p.Key.Number,
                        DataType.String => p.Key.String,
                        DataType.Boolean => p.Key.Boolean,
                        _ => null,
                    };
                    if (k is null) continue;
                    items.Add(new(k, ToRam(p.Value, depth + 1)));
                }
                return new RamTable(items);
            }
            case DataType.Function:
            case DataType.ClrFunction:
                if (!_ramFnWarned) { _ramFnWarned = true; Warn("script", Loc.T("L.Lua.SnowMemoryFunction")); }
                return null;
            default: return null;
        }
    }

    private static DynValue FromRam(Script s, object? o)
    {
        switch (o)
        {
            case null: return DynValue.Nil;
            case bool b: return DynValue.NewBoolean(b);
            case double d: return DynValue.NewNumber(d);
            case string str: return DynValue.NewString(str);
            case RamTable rt:
            {
                var t = new Table(s);   // copie de l'instantané : une table MoonSharp appartient à un seul Script
                foreach (var kv in rt.Items)
                {
                    var val = FromRam(s, kv.Value);
                    switch (kv.Key)
                    {
                        case double dk: t.Set(DynValue.NewNumber(dk), val); break;
                        case string sk: t.Set(sk, val); break;
                        case bool bk: t.Set(DynValue.NewBoolean(bk), val); break;
                    }
                }
                return DynValue.NewTable(t);
            }
            default: return DynValue.Nil;
        }
    }

    // Binde les 5 verbes mémoire un bot tiers (noms donnés) sur `self`, adossés au magasin rendu par `storeOf` (résolu à chaque appel). `ct` = jeton du run de `s`.
    private void BindRamVerbs(Script s, CancellationToken ct, Table self, Func<ConcurrentDictionary<string, object?>> storeOf,
                              string add, string edit, string get, string del, string delAll)
    {
        string Key(CallbackArguments a) => At(a, self, 0).CastToString() ?? "";
        // Une table garde, en plus de son instantané, sa référence VIVANTE (rendue telle quelle à CE Script).
        object? Stored(DynValue v) => v.Type == DataType.Table && ToRam(v) is RamTable rt ? rt with { Owner = s, Live = v.Table, Run = ct } : ToRam(v);
        // Lecture : le Script propriétaire reçoit sa table VIVANTE (l'instantané des autres lecteurs est rafraîchi au passage, sur
        // SON fil) ; tout autre Script reçoit une copie de l'instantané, qu'il ADOPTE comme table vivante si le run propriétaire est
        // terminé (rechargement : les mutations en place du nouveau run persistent aussi). Jamais de lecture d'une table d'un autre fil.
        DynValue Read(string k, object? v)
        {
            if (v is not RamTable rt) return FromRam(s, v);
            if (rt.Live is { } live && ReferenceEquals(rt.Owner, s))
            {
                if (ToRam(DynValue.NewTable(live)) is RamTable snap) storeOf().TryUpdate(k, rt with { Items = snap.Items }, v);
                return DynValue.NewTable(live);
            }
            var copy = FromRam(s, rt);
            if (copy.Type == DataType.Table && (rt.Live is null || rt.Run.IsCancellationRequested))
                storeOf().TryUpdate(k, rt with { Owner = s, Live = copy.Table, Run = ct }, v);
            return copy;
        }
        Fill(self, add, DynValue.NewCallback((_, a) => { string k = Key(a); if (k.Length > 0) storeOf().TryAdd(k, Stored(At(a, self, 1))); return DynValue.Nil; }));
        Fill(self, edit, DynValue.NewCallback((_, a) =>
        {
            string k = Key(a);
            if (k.Length > 0 && storeOf().ContainsKey(k)) storeOf()[k] = Stored(At(a, self, 1));
            return DynValue.Nil;
        }));
        Fill(self, get, DynValue.NewCallback((_, a) => { string k = Key(a); return storeOf().TryGetValue(k, out var v) ? Read(k, v) : DynValue.Nil; }));
        // deleteMemory rend l'ANCIENNE valeur (givreController); sans effet sur les scripts qui l'ignorent (global:).
        Fill(self, del, DynValue.NewCallback((_, a) =>
        {
            if (!storeOf().TryRemove(Key(a), out var old)) return DynValue.Nil;
            return old is RamTable { Live: { } live } rt && ReferenceEquals(rt.Owner, s) ? DynValue.NewTable(live) : FromRam(s, old);
        }));
        Fill(self, delAll, DynValue.NewCallback((_, a) => { storeOf().Clear(); return DynValue.Nil; }));
    }

    // ------------------------------------------------------------------ global:
    private void BindSnowGlobal(Script s, CancellationToken ct)
    {
        var g = GetOrCreateTable(s, "global");
        var G = s.Globals;
        void Alias(string key, string flat) { var fn = G.Get(flat); if (fn.Type is DataType.ClrFunction or DataType.Function) Fill(g, key, SnowFwd(s, g, fn)); }

        foreach (var n in new[] { "printMessage", "printSuccess", "printError", "printWarning", "printColor", "delay", "isScriptPlaying",
                                  "isInFight", "afterFight", "getCountFight", "getCountGather", "resetCountFight", "resetCountGather",
                                  "joinBoss", "inviteTeam", "isInParty", "reloadScript", "getCharacterName", "getCharacterLevel",
                                  "getKamas", "getMapId", "getCurrentCell", "sendWebhook", "notify" })
            Alias(n, n);
        Alias("RestartScript", "reloadScript");
        Alias("RestartScriptThread", "reloadScript");
        Alias("reloadScriptInTheNewThread", "reloadScript");
        Alias("reloadScriptInTheNewThreadWithFile", "reloadScript");
        Alias("rechargerScript", "reloadScript");
        Fill(g, "getProcessId", DynValue.NewCallback((_, a) => DynValue.NewNumber(Environment.ProcessId)));
        Fill(g, "proxyIsEnabled", DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.GetProxySettings(_pid) is not null)));
        Fill(g, "getCurrentProxyAddress", DynValue.NewCallback((_, a) => DynValue.NewString(_launch.GetProxySettings(_pid)?.Host ?? "")));

        // finishScript() : arrêt définitif du script (stopped("script") est quand même appelé).
        Fill(g, "finishScript", DynValue.NewCallback((_, a) => { StopRun(ct, "script"); ThrowIfStopping(ct); return DynValue.Nil; }));
        // leaveDialog: ferme le dialogue / l'échange puis laisse ~250 ms au serveur.
        Fill(g, "leaveDialog", DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            bool ok = _launch.LeaveDialog(_pid);
            InterruptibleDelay(250, ct, pump: true);
            return DynValue.NewBoolean(ok);
        }));

        // loadAndStart(chemin [, casserScript]) / LoadAndStartThread(chemin) : l'argument est d'abord essayé TEL QUEL (= loadScript :
        // un nom de bibliothèque du planning peut contenir « / » ou « \ », ex. « Mine 1/2 »), puis réduit à son NOM de fichier
        //. Un chemin enraciné (lecteur, séparateur initial) ou bâti sur getCurrentScriptDirectory (« scripts\… »)
        // n'est jamais un nom de bibliothèque : réduit d'emblée (pas de « introuvable » journalisé avec le chemin). Aucun chemin
        // disque n'est lu.
        var loadFn = G.Get("loadScript");
        DynValue LoadByPath(ScriptExecutionContext ctx, string raw)
        {
            raw = raw.Trim().Trim('"');
            if (raw.Length == 0 || loadFn.Type != DataType.ClrFunction) return DynValue.False;
            DynValue Load(string n) => loadFn.Callback.Invoke(ctx, new[] { DynValue.NewString(n) });
            if (raw.IndexOfAny(new[] { '/', '\\' }) < 0) return Load(raw);   // sans séparateur : exactement loadScript
            if (!LooksLikeDiskPath(raw) && Load(raw) is { } r && r.CastToBool()) return r;
            string name = ScriptNameFromPath(raw);
            return name.Length == 0 ? DynValue.False : Load(name);
        }
        DynValue loadByPath = DynValue.NewCallback((ctx, a) => LoadByPath(ctx, At(a, g, 0).CastToString() ?? ""));
        Fill(g, "loadAndStart", loadByPath);
        Fill(g, "LoadAndStartThread", loadByPath);
        G["loadAndStart"] = DynValue.NewCallback((ctx, a) => LoadByPath(ctx, a.Count > 0 ? a[0].CastToString() ?? "" : ""));   // version plate

        // ---- mémoire RAM ----
        BindRamVerbs(s, ct, g, RamAccount, "addInMemory", "editInMemory", "remember", "deleteMemory", "deleteAllMemory");
        BindRamVerbs(s, ct, g, () => s_ramGlobal, "AddInGlobalMemory", "EditInGlobalMemory", "GetInGlobalMemory", "DeleteFromGlobalMemory", "DeleteAllGlobalMemory");

        // ---- équipe ----
        List<LaunchService.TeamMemberInfo> TeamOrdered()
        {
            if (!_launch.IsTeamLeader(_pid)) return new();
            var all = _launch.TeamMembers(_pid);
            return all.Where(m => m.Pid == _pid).Concat(all.Where(m => m.Pid != _pid)).ToList();
        }
        Fill(g, "isBoss", DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.IsTeamLeader(_pid))));
        DynValue names = DynValue.NewCallback((_, a) => BuildStrings(s, TeamOrdered().Select(m => m.Name)));
        Fill(g, "getTeamMembers", names);
        Fill(g, "getTeamMembersNames", names);
        Fill(g, "getTeamMembersIds", DynValue.NewCallback((_, a) => BuildNumsL(s, TeamOrdered().Select(m => _launch.CharacterId(m.Pid)))));
        Fill(g, "getTeamMembersUsernames", DynValue.NewCallback((_, a) => BuildStrings(s, TeamOrdered().Select(m => _launch.AccountAliasOf(m.Pid)))));
        Fill(g, "isInTeam", DynValue.NewCallback((_, a) =>
        {
            string n = (At(a, g, 0).CastToString() ?? "").Trim();
            if (n.Length == 0) return DynValue.False;
            return DynValue.NewBoolean(TeamOrdered().Any(m => string.Equals(m.Name, n, StringComparison.OrdinalIgnoreCase)
                || string.Equals(_launch.AccountAliasOf(m.Pid), n, StringComparison.OrdinalIgnoreCase)
                || (_launch.FindScriptAccount(n) is { } acc && acc.Pid == m.Pid)));
        }));

        // ---- compte / session ----
        // username() / getAlias() : ALIAS du compte (libellé, sinon perso) — jamais l'identifiant de connexion.
        DynValue alias = DynValue.NewCallback((_, a) => DynValue.NewString(_launch.AccountAliasOf(_pid) is { Length: > 0 } x ? x : (_world.Get(_pid).CharacterName ?? "")));
        Fill(g, "username", alias);
        Fill(g, "getAlias", alias);
        Fill(g, "accountTag", UnsupportedLua("global:accountTag"));
        Fill(g, "elapsedTime", DynValue.NewCallback((_, a) =>
            DynValue.NewNumber(_firstStartAt == 0 ? 0 : (Environment.TickCount64 - _firstStartAt) / 3_600_000)));
        Fill(g, "random", DynValue.NewCallback((_, a) =>
        {
            int lo = (int)(At(a, g, 0).CastToNumber() ?? 0), hi = (int)(At(a, g, 1).CastToNumber() ?? 0);
            if (hi < lo) (lo, hi) = (hi, lo);
            return DynValue.NewNumber(hi == int.MaxValue ? Random.Shared.Next(lo, hi) : Random.Shared.Next(lo, hi + 1));
        }));
        // disconnect() : déconnecte ce bot (et son équipe s'il en est le chef). reconnect(h) / reconnectBis(min) / reconnectSec(s) /
        // reconnectWithToken(s) : idem puis relance automatique après le délai. Exécuté par l'onglet Bots (thread UI).
        DynValue Disc(long delayMs) => DynValue.NewBoolean(RequestDisconnect(delayMs));
        Fill(g, "disconnect", DynValue.NewCallback((_, a) => Disc(0)));
        Fill(g, "reconnect", DynValue.NewCallback((_, a) => Disc((long)(Math.Max(0, At(a, g, 0).CastToNumber() ?? 0) * 3_600_000))));
        Fill(g, "reconnectBis", DynValue.NewCallback((_, a) => Disc((long)(Math.Max(0, At(a, g, 0).CastToNumber() ?? 0) * 60_000))));
        DynValue recoSec = DynValue.NewCallback((_, a) => Disc((long)(Math.Max(0, At(a, g, 0).CastToNumber() ?? 0) * 1000)));
        Fill(g, "reconnectSec", recoSec);
        Fill(g, "reconnectWithToken", recoSec);

        // fightMessage([n]) : bilan du DERNIER combat (combat:lastFightResult) + fightCounter / duration (ms serveur).
        Fill(g, "fightMessage", DynValue.NewCallback((_, a) =>
        {
            if (_launch.LastFightResult(_pid) is not { } r) return DynValue.Nil;
            var t = FightResultTable(s, r);
            if (t.Type == DataType.Table)
            {
                t.Table["fightCounter"] = DynValue.NewNumber(Volatile.Read(ref _sFights));
                t.Table["duration"] = DynValue.NewNumber(r.ServerDuration);
            }
            return t;
        }));

        // ---- console ----
        Fill(g, "consoleLines", DynValue.NewCallback((_, a) => { lock (_consoleRing) return BuildStrings(s, _consoleRing.ToList()); }));
        Fill(g, "clearConsole", DynValue.NewCallback((_, a) => { lock (_consoleRing) _consoleRing.Clear(); return DynValue.Nil; }));
        Fill(g, "playAudio", DynValue.NewCallback((_, a) => { AudioService.PlayUi(AudioService.Notify); return DynValue.Nil; }));
        Fill(g, "exportAllPublicMethods", DynValue.NewCallback((_, a) => DynValue.NewString(ExportPublicMethods(s))));

        // ---- dossiers (jamais de chemin réel : il contient le nom d'utilisateur Windows) ----
        Fill(g, "getCurrentScriptDirectory", DynValue.NewCallback((_, a) => DynValue.NewString("scripts")));
        Fill(g, "getCurrentDirectory", DynValue.NewCallback((_, a) => DynValue.NewString("scripts")));
        Fill(g, "getAllFilesNameInDirectory", DynValue.NewCallback((_, a) =>
        {
            string ext = (At(a, g, 1).CastToString() ?? "").Trim().TrimStart('*', '.');
            if (ext.Length == 0) ext = "lua";
            if (!IsPlainExtension(ext)) return DynValue.NewTable(new Table(s));   // motif avec séparateur / « .. » : table vide
            return BuildStrings(s, ScriptsFolderFiles(ext));
        }));

        // ---- sans équivalent (stubs sûrs : jamais « attempt to call a nil value ») ----
        DynValue Stub(string fn) => UnsupportedLua("global:" + fn);
        // askInput(texte [, défaut]) -> chaîne saisie ("" si annulé / délai) ; askQuestion(texte) -> oui = true. Fenêtre de l'app,
        // le script attend la réponse (5 min max) ; arrêter le script ferme la fenêtre.
        g["askInput"] = DynValue.NewCallback((_, a) => DynValue.NewString(
            AskUserBlocking(At(a, g, 0).CastToString() ?? "", At(a, g, 1).CastToString() ?? "", false, ct) ?? ""));
        g["askQuestion"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(
            AskUserBlocking(At(a, g, 0).CastToString() ?? "", "", true, ct) == "yes"));
        foreach (var n in new[] { "rcAskFight", "rcAskGather", "isKoliServer", "isStuffForKoli", "isRegisteredInKoli",
                                  "unregisterFromKoliIfRegistered", "editAlias", "loadConfiguration", "LoadConfigurationWithoutScript",
                                  "checkModerator", "antiModeratorActivated", "groupCharacters", "ungroupCharacters", "openMitm" })
            Fill(g, n, Stub(n));
        foreach (var n in new[] { "blabla", "activateAntiModerator" }) Fill(g, n, Stub(n));
        // Identifiants : JAMAIS exposés à un script (mot de passe, jeton de licence).
        DynValue Refused(string fn) => UnsupportedLua("global:" + fn, "API refusée par le bac à sable");
        Fill(g, "password", Refused("password"));
        Fill(g, "getToken", Refused("getToken"));
        Fill(g, "charactersList", DynValue.NewCallback((_, a) =>
        {
            var t = new Table(s);
            if (_world.Get(_pid).CharacterName is { Length: > 0 } me)
            {
                var row = new Table(s);
                row["name"] = DynValue.NewString(me);
                row["id"] = DynValue.NewNumber(_launch.CharacterId(_pid));
                row["level"] = DynValue.NewNumber(_world.Get(_pid).Level ?? 0);
                t.Set(1, DynValue.NewTable(row));
            }
            return DynValue.NewTable(t);
        }));
        // Contrôleurs de compte : proxy { name, alias, pid, isLeader, executeLUA(src) } (executeLUA = team:runOnce sur ce membre).
        Fill(g, "thisAccountController", DynValue.NewCallback((_, a) => AccountProxy(s, _pid, ct)));
        Fill(g, "getController", DynValue.NewCallback((_, a) => AccountProxy(s, _pid, ct)));
        Fill(g, "leaderAccountController", DynValue.NewCallback((_, a) =>
            _launch.TeamViewOf(_pid) is { } tv ? AccountProxy(s, tv.LeaderPid, ct) : DynValue.Nil));
    }

    private static string ScriptNameFromPath(string raw) => LuaSnowCompat.ScriptNameFromPath(raw);

    // Chemin disque manifeste (loadAndStart) : enraciné (« C:\… », « \… », « /… »), relatif explicite (« .\ », « ..\ ») ou bâti
    // sur le pseudo-dossier « scripts » de getCurrentScriptDirectory().
    private static bool LooksLikeDiskPath(string raw)
    {
        try { if (Path.IsPathRooted(raw)) return true; } catch { return true; }
        foreach (var p in new[] { "./", ".\\", "../", "..\\", "scripts/", "scripts\\" })
            if (raw.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // Extension de getAllFilesNameInDirectory : lettres / chiffres ASCII, 1 à 10 caractères. Tout autre texte irait dans le MOTIF
    // de recherche, où « x/../../* » listerait un dossier HORS de scripts/.
    private static bool IsPlainExtension(string ext)
    {
        if (ext.Length is < 1 or > 10) return false;
        foreach (char c in ext)
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) return false;
        return true;
    }

    private static List<string> ScriptsFolderFiles(string ext)
    {
        try
        {
            if (!IsPlainExtension(ext) || !Directory.Exists(ScriptsRootDir)) return new();
            string root = Path.GetFullPath(ScriptsRootDir);
            if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;
            // Défense en profondeur : seuls les fichiers DIRECTEMENT sous scripts/ (chemin complet vérifié) sont listés.
            return Directory.EnumerateFiles(root, "*." + ext, SearchOption.TopDirectoryOnly)
                .Select(Path.GetFullPath)
                .Where(f => f.StartsWith(root, StringComparison.OrdinalIgnoreCase) && f.IndexOfAny(new[] { '\\', '/' }, root.Length) < 0)
                .Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Take(500).ToList();
        }
        catch { return new(); }
    }

    private static DynValue BuildStrings(Script s, IEnumerable<string> xs)
    {
        var t = new Table(s); int i = 1;
        foreach (var x in xs) t.Set(i++, DynValue.NewString(x ?? ""));
        return DynValue.NewTable(t);
    }

    private bool RequestDisconnect(long delayMs)
    {
        bool team = _launch.IsTeamLeader(_pid);
        bool ok = _launch.EnqueueAccountAction(new LaunchService.ScriptAccountAction(_pid, LaunchService.ScriptAccountActionKind.Disconnect, null,
            Math.Min(delayMs, 48L * 3_600_000), team));
        if (!ok) { Warn("compte", Loc.T("L.Lua.SnowAccountQueueFull")); return false; }
        if (delayMs > 0) Info("compte", Loc.Fmt("L.Lua.SnowReconnectPlanned", Math.Max(1, (int)Math.Ceiling(delayMs / 60000.0))));
        else Info("compte", Loc.T("L.Lua.SnowDisconnectPlanned"));
        return true;
    }

    // Proxy de contrôleur de compte: lecture + executeLUA sur un membre d'équipe.
    private DynValue AccountProxy(Script s, uint pid, CancellationToken ct)
    {
        if (pid == 0) return DynValue.Nil;
        var t = new Table(s);
        string name = _launch.TeamMembers(_pid).FirstOrDefault(m => m.Pid == pid).Name
                      ?? (pid == _pid ? _world.Get(_pid).CharacterName ?? "" : "");
        if (string.IsNullOrEmpty(name) && pid == _pid) name = _world.Get(_pid).CharacterName ?? "";
        t["name"] = DynValue.NewString(name ?? "");
        t["alias"] = DynValue.NewString(_launch.AccountAliasOf(pid));
        t["pid"] = DynValue.NewNumber(pid);
        t["isLeader"] = DynValue.NewBoolean(_launch.IsTeamLeader(pid));
        t["mapId"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.CurrentMap(pid) ?? 0));
        t["isInFight"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.IsInFight(pid)));
        t["executeLUA"] = DynValue.NewCallback((_, a) =>
        {
            string src = At(a, t, 0).CastToString() ?? "";
            if (pid == _pid) { WarnSnow("executeLUA (soi-même)"); return DynValue.False; }
            return DynValue.NewBoolean(TeamRunOnce(name ?? "", src, ct));
        });
        return DynValue.NewTable(t);
    }

    private static string ExportPublicMethods(Script s)
    {
        var sb = new StringBuilder();
        foreach (var p in s.Globals.Pairs.OrderBy(p => p.Key.CastToString(), StringComparer.Ordinal))
        {
            if (p.Key.Type != DataType.String) continue;
            if (p.Value.Type == DataType.Table && SnowModules.Contains(p.Key.String))
                foreach (var f in p.Value.Table.Pairs.Where(x => x.Key.Type == DataType.String && x.Value.Type is DataType.ClrFunction or DataType.Function)
                                                     .Select(x => x.Key.String).OrderBy(x => x, StringComparer.Ordinal))
                    sb.Append(p.Key.String).Append(':').Append(f).Append("()\n");
            else if (p.Value.Type == DataType.ClrFunction)
                sb.Append(p.Key.String).Append("()\n");
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ config:
    private void BindSnowConfig(Script s)
    {
        var cfg = GetOrCreateTable(s, "config");
        List<double> Nums(CallbackArguments a)
        {
            var list = new List<double>();
            int off = Off(a, cfg);
            for (int j = off; j < a.Count; j++)
            {
                var v = a[j];
                if (v.Type == DataType.Table) { foreach (var x in v.Table.Values) if (x.CastToNumber() is double d) list.Add(d); }
                else if (v.CastToNumber() is double d) list.Add(d);
            }
            return list;
        }
        DynValue ListTable(IEnumerable<double> xs) { var t = new Table(s); int i = 1; foreach (var x in xs) t.Set(i++, DynValue.NewNumber(x)); return DynValue.NewTable(t); }
        IEnumerable<double> ReadList(string key)
        {
            var v = s.Globals.Get(key);
            if (key == "ELEMENTS_TO_GATHER" && v.Type != DataType.Table) v = s.Globals.Get("GATHER");   // même repli que le moteur
            if (v.Type == DataType.Number) return new[] { v.Number };
            if (v.Type != DataType.Table) return Array.Empty<double>();
            return v.Table.Values.Select(x => x.CastToNumber()).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        }
        void ListPair(string setName, string getName, string global)
        {
            Fill(cfg, setName, DynValue.NewCallback((_, a) => { s.Globals[global] = ListTable(Nums(a)); return DynValue.Nil; }));
            Fill(cfg, getName, DynValue.NewCallback((_, a) => ListTable(ReadList(global))));
        }
        ListPair("setGatherList", "getGatherList", "ELEMENTS_TO_GATHER");
        // Listes de monstres : ids ET noms (SelectMonster filtre les deux). Nums() jetait les noms, si bien que
        // setForbiddenMonsters({ ..., "Rouquette" }) n'interdisait rien (Shadowbot, 10/11 : 17 attaques de monstres « interdits »).
        void MonsterListPair(string setName, string getName, string global)
        {
            Fill(cfg, setName, DynValue.NewCallback((_, a) =>
            {
                var t = new Table(s); int i = 1;
                for (int j = Off(a, cfg); j < a.Count; j++)
                    foreach (var x in a[j].Type == DataType.Table ? a[j].Table.Values : new[] { a[j] })
                        if (x.Type == DataType.String && x.CastToNumber() is null) t.Set(i++, DynValue.NewString(x.String));
                        else if (x.CastToNumber() is double d) t.Set(i++, DynValue.NewNumber(d));
                s.Globals[global] = DynValue.NewTable(t);
                return DynValue.Nil;
            }));
            Fill(cfg, getName, DynValue.NewCallback((_, a) => s.Globals.Get(global).Type == DataType.Table ? s.Globals.Get(global) : DynValue.NewTable(new Table(s))));
        }
        MonsterListPair("setMandatoryMonsters", "getMandatoryMonsters", "FORCE_MONSTERS");
        MonsterListPair("setForbiddenMonsters", "getForbiddenMonsters", "FORBIDDEN_MONSTERS");
        void IntPair(string setName, string getName, string global, int def)
        {
            Fill(cfg, setName, DynValue.NewCallback((_, a) =>
            {
                int n = (int)(At(a, cfg, 0).CastToNumber() ?? def);
                s.Globals[global] = DynValue.NewNumber(Math.Clamp(n, 1, 8));
                return DynValue.Nil;
            }));
            Fill(cfg, getName, DynValue.NewCallback((_, a) => DynValue.NewNumber(ReadInt(s, global, def))));
        }
        IntPair("setMinMonsters", "getMinMonsters", "MIN_MONSTERS", 1);
        IntPair("setMaxMonsters", "getMaxMonsters", "MAX_MONSTERS", 999);   // défaut = valeur appliquée par la sélection (999 = sans plafond)
        // setAmountOfSpecificMonsters({{gid, min, max}, ...}) -> global MONSTERS_AMOUNT (lu par la sélection de groupe).
        Fill(cfg, "setAmountOfSpecificMonsters", DynValue.NewCallback((_, a) =>
        {
            var src = At(a, cfg, 0);
            var t = new Table(s); int i = 1;
            if (src.Type == DataType.Table)
                foreach (var row in src.Table.Values)
                {
                    if (row.Type != DataType.Table) continue;
                    double gid = row.Table.Get(1).CastToNumber() ?? 0, min = row.Table.Get(2).CastToNumber() ?? 0, max = row.Table.Get(3).CastToNumber() ?? 8;
                    if (gid <= 0) continue;
                    var r = new Table(s);
                    r.Set(1, DynValue.NewNumber(gid)); r.Set(2, DynValue.NewNumber(min)); r.Set(3, DynValue.NewNumber(max));
                    t.Set(i++, DynValue.NewTable(r));
                }
            s.Globals["MONSTERS_AMOUNT"] = DynValue.NewTable(t);
            return DynValue.Nil;
        }));
        Fill(cfg, "getAmountOfSpecificMonsters", DynValue.NewCallback((_, a) =>
        {
            var v = s.Globals.Get("MONSTERS_AMOUNT");
            return v.Type == DataType.Table ? v : DynValue.NewTable(new Table(s));
        }));
    }

    /// <summary>Contraintes « nombre de monstres de tel gid » (config:setAmountOfSpecificMonsters / global MONSTERS_AMOUNT) :
    /// gid -&gt; (min, max). Vide si absent.</summary>
    private Dictionary<int, (int Min, int Max)> ReadMonsterAmounts()
    {
        var d = new Dictionary<int, (int, int)>();
        var v = _script?.Globals.Get("MONSTERS_AMOUNT") ?? DynValue.Nil;
        if (v.Type != DataType.Table) return d;
        foreach (var row in v.Table.Values)
        {
            if (row.Type != DataType.Table) continue;
            int gid = (int)(row.Table.Get(1).CastToNumber() ?? 0);
            int min = (int)(row.Table.Get(2).CastToNumber() ?? 0), max = (int)(row.Table.Get(3).CastToNumber() ?? 8);
            if (gid > 0) d[gid] = (min, max);
        }
        return d;
    }

    // ------------------------------------------------------------------ givreController:
    private void BindSnowController(Script s, CancellationToken ct)
    {
        var sc = GetOrCreateTable(s, "givreController");
        var G = s.Globals;
        foreach (var n in new[] { "printMessage", "printSuccess", "printError", "printWarning", "delay" })
            if (G.Get(n).Type is DataType.ClrFunction or DataType.Function) Fill(sc, n, SnowFwd(s, sc, G.Get(n)));
        Fill(sc, "version", DynValue.NewCallback((_, a) => DynValue.NewString("PC")));
        BindRamVerbs(s, ct, sc, () => s_ramController, "addInMemory", "editInMemory", "remember", "deleteMemory", "deleteAllMemory");
        var dev = GetOrCreateTable(s, "developer");
        foreach (var n in new[] { "getRequest", "postRequest" })
            if (dev.Get(n).Type == DataType.ClrFunction) Fill(sc, n, SnowFwd(s, sc, dev.Get(n)));

        IReadOnlyList<LaunchService.ScriptAccountInfo> Accs() => _launch.ScriptAccounts;
        DynValue Proxy(LaunchService.ScriptAccountInfo acc)
        {
            var t = new Table(s);
            t["alias"] = DynValue.NewString(acc.Alias);
            t["name"] = DynValue.NewString(acc.Character);
            t["pid"] = DynValue.NewNumber(acc.Pid);
            t["loaded"] = DynValue.NewBoolean(acc.Loaded);
            t["mapId"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(acc.Pid == 0 ? 0 : _launch.CurrentMap(acc.Pid) ?? 0));
            t["isInFight"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(acc.Pid != 0 && _launch.IsInFight(acc.Pid)));
            t["executeLUA"] = DynValue.NewCallback((_, a) =>
            {
                string src = At(a, t, 0).CastToString() ?? "";
                if (acc.Pid == 0 || acc.Pid == _pid) { WarnSnow("executeLUA"); return DynValue.False; }
                // Exécuté par l'onglet Bots seulement pour un membre de MON équipe en jeu (team:runOnce) : sinon refus immédiat
                // (avant : true puis abandon silencieux).
                var member = _launch.TeamMembers(_pid).FirstOrDefault(m => m.Pid == acc.Pid);
                if (member.Pid == 0) { Warn("équipe", Loc.Fmt("L.Lua.TeamRunOnceNoMember", acc.Alias)); return DynValue.False; }
                return DynValue.NewBoolean(TeamRunOnce(member.Name, src, ct));
            });
            return DynValue.NewTable(t);
        }
        Fill(sc, "getLoadedAccounts", DynValue.NewCallback((_, a) =>
        {
            var t = new Table(s); int i = 1;
            foreach (var acc in Accs().Where(x => x.Loaded)) t.Set(i++, Proxy(acc));
            return DynValue.NewTable(t);
        }));
        Fill(sc, "getAccount", DynValue.NewCallback((_, a) => _launch.FindScriptAccount(At(a, sc, 0).CastToString()) is { } acc ? Proxy(acc) : DynValue.Nil));
        DynValue Aliases(Func<LaunchService.ScriptAccountInfo, bool> pred) => BuildStrings(s, Accs().Where(pred).Select(x => x.Alias));
        foreach (var n in new[] { "getUsernameLoadedAccounts", "getUsernameOfLoadedAccounts", "getAliasOfLoadedAccounts", "getAliasLoadedAccounts" })
            Fill(sc, n, DynValue.NewCallback((_, a) => Aliases(x => x.Loaded)));
        foreach (var n in new[] { "getUsernameNotLoadedAccounts", "getUsernameAccountNotLoaded", "getAliasNotLoadedAccounts" })
            Fill(sc, n, DynValue.NewCallback((_, a) => Aliases(x => !x.Loaded)));
        foreach (var n in new[] { "getUsernameAllRegisteredAccounts", "getAliasAllRegisteredAccounts" })
            Fill(sc, n, DynValue.NewCallback((_, a) => Aliases(_ => true)));
        Fill(sc, "accountIsLoaded", DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.FindScriptAccount(At(a, sc, 0).CastToString()) is { Loaded: true })));
        Fill(sc, "accountIsRegistered", DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.FindScriptAccount(At(a, sc, 0).CastToString()) is not null)));
        // loadAnAccount(nom [, connexionAuto]) / unloadAnAccount(nom) : lance / arrête un compte ENREGISTRÉ (onglet Bots, thread UI ;
        // mêmes gardes que le planning : licence, multi-compte, mode Socket).
        DynValue Act(LaunchService.ScriptAccountActionKind kind) => DynValue.NewCallback((_, a) =>
        {
            string n = (At(a, sc, 0).CastToString() ?? "").Trim();
            if (_launch.FindScriptAccount(n) is null) { Warn("compte", Loc.Fmt("L.Lua.SnowAccountUnknown", n)); return DynValue.False; }
            return DynValue.NewBoolean(_launch.EnqueueAccountAction(new LaunchService.ScriptAccountAction(_pid, kind, n, 0, false)));
        });
        Fill(sc, "loadAnAccount", Act(LaunchService.ScriptAccountActionKind.Launch));
        Fill(sc, "unloadAnAccount", Act(LaunchService.ScriptAccountActionKind.Stop));
        // Création de compte / de perso, proxys, Bouclier, mots de passe : JAMAIS depuis un script.
        // Création de compte / de perso, proxys, Bouclier : refus de SÉCURITÉ (jamais depuis un script) ; les autres sont sans équivalent.
        DynValue Stub(string fn) => UnsupportedLua("givreController:" + fn);
        foreach (var n in new[] { "loadTeamAccount", "loadTeamAccountbak", "loadControllerAccount", "registerAnAccount", "createCharacter",
                                  "assignProxyToAnAccount", "addPushBulletKey", "unlockShield", "activateAntiModerator" })
            Fill(sc, n, Stub(n));
        Fill(sc, "getPassword", UnsupportedLua("givreController:getPassword"));
    }

    // ------------------------------------------------------------------ dofile / load (texte)
    // Un bot tiers tourne sans bac à sable (io, dofile, loadstring). GivreClone garde le bac à sable: dofile(chemin) charge le FICHIER
    // de même NOM depuis scripts/lib puis scripts (≤ 512 Ko, pas de lien) ; load/loadstring compilent un TEXTE dans le même bac.
    private const int DofileMaxDepth = 16;

    private void BindSnowScriptFiles(Script s, CancellationToken ct)
    {
        // Garde de récursion (comme require) : fichiers en cours d'exécution, par run (thread du script). Sans elle, un fichier qui
        // se dofile lui-même (ou deux qui se dofilent) ré-entre MoonSharp sur la pile C# jusqu'au StackOverflow, NON rattrapable :
        // tout le processus (tous les bots) tombe.
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        s.Globals["dofile"] = DynValue.NewCallback((ctx, a) =>
        {
            string name = ScriptNameFromPath(a.Count > 0 ? a[0].CastToString() ?? "" : "");
            var key = LuaModulePath.Normalize(name);
            if (key is null) throw new ScriptRuntimeException(Loc.Fmt("L.Lua.RequireBadName", name));
            if (running.Contains(key) || running.Count >= DofileMaxDepth) throw new ScriptRuntimeException(Loc.Fmt("L.Lua.SnowDofileCircular", key));
            string? code = null;
            foreach (var root in new[] { LibDir, ScriptsRootDir })
            {
                try
                {
                    if (!Directory.Exists(root) || !LuaModulePath.TryResolve(root, key, out var full, out _) || !File.Exists(full)) continue;
                    code = ReadScriptFile(root, full, key, "L.Lua.RequireNotFound", "L.Lua.RequireTooLarge");
                    break;
                }
                catch (ScriptRuntimeException) { throw; }
                catch { /* dossier suivant */ }
            }
            if (code is null) throw new ScriptRuntimeException(Loc.Fmt("L.Lua.RequireNotFound", key));
            running.Add(key);
            try { return CallLua(s, s.LoadString(code, null, "file:" + key), ct); }
            finally { running.Remove(key); }
        });
        DynValue load = DynValue.NewCallback((_, a) =>
        {
            if (a.Count == 0 || a[0].Type != DataType.String) return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("load: seul un texte est accepté"));
            try { return s.LoadString(a[0].String, null, a.Count > 1 && a[1].Type == DataType.String ? a[1].String : "chunk"); }
            catch (InterpreterException ex) { return DynValue.NewTuple(DynValue.Nil, DynValue.NewString(ex.DecoratedMessage ?? ex.Message)); }
        });
        if (s.Globals.Get("load").IsNil()) s.Globals["load"] = load;
        if (s.Globals.Get("loadstring").IsNil()) s.Globals["loadstring"] = load;
    }
}
