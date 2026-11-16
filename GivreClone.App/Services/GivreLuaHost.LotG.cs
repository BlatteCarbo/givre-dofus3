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
// Lot G — plateforme de scripting : hooks de paquets (registerMessage / unRegisterMessage / waitFor / messagesRegistering,
// table developer un bot tiers), hook onPrivateMessage, require (scripts/lib), loadScript / reloadScript, notify, et team:
// runOnce / inviteAll / isInParty / partyId / partyMembers.
//
// FILS : les abonnements (PacketDecoded / PrivateMessageReceived) tournent sur le thread RÉSEAU (ou sur le thread script via
// ShowInjected) et ne font QU'ENFILER (jamais de Lua, jamais d'exception). La pompe (PumpHooks) tourne UNIQUEMENT sur le
// thread du script, aux points sûrs : delay(), attente de fin de combat de fight(), boucle RunLoop (hors combat / combat),
// et dans waitFor. Un script SANS hook garde exactement l'ancien chemin d'attente (aucune pompe).
// ==================================================================================================================
public sealed partial class GivreLuaHost
{
    // État d'UN run (Start/RunOnce) : chaque run possède ses tables -> deux threads (ancien run qui se déroule + nouveau) ne
    // partagent jamais un Dictionary non thread-safe.
    private sealed class ScriptRuntime
    {
        public required Script Script;
        public required CancellationToken Token;
        // thread du script uniquement :
        // clé = code wire. registerMessage REMPLACE (un gestionnaire); developer:registerMessage AJOUTE.
        public readonly Dictionary<string, List<Hook>> Hooks = new(StringComparer.Ordinal);
        public Table? PacketMeta;       // métatable des tables de paquets : noms camelCase / PascalCase, Count (champ absent = nil)
        public Table? SnowPacketMeta;   // idem + défauts proto3 (0 / false / "" / {}): consommateurs seulement
        public int HookCount => Hooks.Values.Sum(l => l.Count);
        public readonly Dictionary<string, DynValue> Modules = new(StringComparer.OrdinalIgnoreCase);  // cache require
        public readonly HashSet<string> Loading = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<Waiter> Waiters = new();   // pile des waitFor actifs (imbriqués : onTurn / action pendant un waitFor)
        public bool InPump;
        public DynValue? PmFn;
        // multi-threads :
        public volatile IReadOnlyDictionary<string, bool> Interest = new Dictionary<string, bool>();   // code -> sortants acceptés
        public volatile bool PmArmed;
        public readonly ConcurrentQueue<(long Seq, DecodedPacket P)> Inbox = new();
        public int InboxCount;
        public long Seq;
        public int Dropped;
        public readonly ConcurrentQueue<(string From, string Text, long Id)> PmInbox = new();
        public int PmCount;
        // Paquets reçus: dernier paquet ENTRANT par
        // code, enregistré en permanence (borné par le nombre de messages du protocole). developer:listenPackets() et chaque
        // suspendScriptUntil*(…, écoute=true) posent la marque (ListenPackets = CleanPackets) ; un paquet reconnu par une attente
        // est CONSOMMÉ (RemovePacketName) -> une attente suivante ne rend jamais deux fois le même paquet.
        public readonly ConcurrentDictionary<string, (long Seq, DecodedPacket P)> Listened = new(StringComparer.Ordinal);
        public long ListenMark;   // thread du script uniquement

        public bool HasWork => Hooks.Count > 0 || PmFn is not null;

        public void RebuildInterest()
        {
            var d = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var list in Hooks.Values)
                foreach (var h in list) d[h.Code] = (d.TryGetValue(h.Code, out var o) && o) || h.Outgoing;
            foreach (var w in Waiters) { foreach (var c in w.Codes) d.TryAdd(c, false); if (w.FailCode is { } fc) d.TryAdd(fc, false); }
            Interest = d;
        }
    }

    private sealed class Hook
    {
        public Hook(string code, string name, DynValue fn, bool outgoing, bool snow)
        { Code = code; Name = name; Fn = fn; Outgoing = outgoing; Snow = snow; }
        public string Code { get; }
        public string Name { get; }
        public DynValue Fn { get; }
        public bool Outgoing { get; }
        public bool Snow { get; }   // developer:registerMessage: table de paquet avec défauts proto3
        public int Errors;
    }

    private sealed class Waiter
    {
        public required HashSet<string> Codes;   // codes de SUCCÈS (waitFor : un seul ; waitForAny : plusieurs)
        public string? FailCode;
        public long MinSeq;
        public (DecodedPacket P, bool Fail)? Result;
    }

    private ScriptRuntime? _rtField;
    private ScriptRuntime? CurrentRuntime => Volatile.Read(ref _rtField);
    private Action<DecodedPacket>? _onPacket;
    private Action<uint, string, string, long>? _onPm;
    private const int InboxMax = 256, PmInboxMax = 32, MaxHooks = 64, PumpBudget = 32;
    private const int WaitForDefaultMs = 10000, WaitForMinMs = 50, WaitForMaxMs = 120000;   // < seuil anti-stuck (180 s)
    private const int RequireMaxBytes = 512 * 1024;
    private const int NotifyMinIntervalMs = 2000, NotifyMaxChars = 200;
    private long _lastNotifyAt;
    private bool _waitForInHookWarned;
    private volatile bool _listenerOnly;

    /// <summary>Le run courant est un script « écouteur » (hooks de paquets / MP, sans move() ni hook de combat) : inactif PAR
    /// CONCEPTION -> l'anti-stuck ne doit pas le juger figé.</summary>
    public bool IsListenerOnly => _listenerOnly && Running;

    private static string ScriptsRootDir => Path.Combine(MZP19LocalClone.DataRoot, "scripts");
    private static string LibDir => Path.Combine(ScriptsRootDir, "lib");

    // ------------------------------------------------------------------ abonnements (constructeur / Dispose)
    private void SubscribeLotG()
    {
        _onPacket = p =>
        {
            try
            {
                var rt = CurrentRuntime;
                if (rt is null || p.Pid != _pid || rt.Token.IsCancellationRequested) return;
                // Enregistré AVANT le filtre d'intérêt: une attente un bot tiers armée après l'arrivée du paquet le voit quand même.
                // Jamais un message porteur de secrets (jetons…) : inaccessible à Lua, il n'a pas à être retenu ici.
                if (!p.ClientToServer && !LuaSnowCompat.IsSensitiveMessage(p.Name)) rt.Listened[p.Code] = (Interlocked.Increment(ref rt.Seq), p);
                if (!rt.Interest.TryGetValue(p.Code, out bool outOk) || (p.ClientToServer && !outOk)) return;
                if (Interlocked.Increment(ref rt.InboxCount) > InboxMax)
                {
                    Interlocked.Decrement(ref rt.InboxCount);
                    Interlocked.Increment(ref rt.Dropped);
                    return;
                }
                rt.Inbox.Enqueue((Interlocked.Increment(ref rt.Seq), p));
            }
            catch { /* ne JAMAIS casser OnDecoded (PacketDecoded est levé en premier) */ }
        };
        _onPm = (pid, from, text, id) =>
        {
            try
            {
                var rt = CurrentRuntime;
                if (rt is null || pid != _pid || !rt.PmArmed || rt.Token.IsCancellationRequested) return;
                if (Interlocked.Increment(ref rt.PmCount) > PmInboxMax) { Interlocked.Decrement(ref rt.PmCount); Interlocked.Increment(ref rt.Dropped); return; }
                rt.PmInbox.Enqueue((from ?? "", text ?? "", id));
            }
            catch { /* idem */ }
        };
        _launch.PacketDecoded += _onPacket;
        _launch.PrivateMessageReceived += _onPm;
    }

    private void UnsubscribeLotG()
    {
        if (_onPacket is not null) _launch.PacketDecoded -= _onPacket;
        if (_onPm is not null) _launch.PrivateMessageReceived -= _onPm;
    }

    /// <summary>Nouveau runtime pour le run lié à <paramref name="ct"/> (appelé par Bind, AVANT DoString).</summary>
    private ScriptRuntime NewRuntime(Script s, CancellationToken ct)
    {
        var rt = new ScriptRuntime { Script = s, Token = ct };
        Volatile.Write(ref _rtField, rt);
        _waitForInHookWarned = false;
        return rt;
    }

    /// <summary>Fin d'un run : plus rien n'est enfilé pour lui (un run plus récent n'est jamais touché).</summary>
    private void RetireRuntime(CancellationToken ct)
    {
        var rt = CurrentRuntime;
        if (rt is null || rt.Token != ct) return;
        rt.PmArmed = false;
        rt.Interest = new Dictionary<string, bool>();
        Interlocked.CompareExchange(ref _rtField, null, rt);
    }

    // onPrivateMessage relu (fonction définie tard ou retirée) : Globals.Get sur le thread du script, bon marché.
    private static void RefreshPmHook(ScriptRuntime rt)
    {
        var f = rt.Script.Globals.Get("onPrivateMessage");
        rt.PmFn = f.Type is DataType.Function or DataType.ClrFunction ? f : null;
        rt.PmArmed = rt.PmFn is not null;
    }

    /// <summary>Après le top-level (DoString): onPrivateMessage + messagesRegistering.
    /// Un arrêt se propage ; une erreur Lua est journalisée sans arrêter le script.</summary>
    private void AfterTopLevel(Script script, CancellationToken ct)
    {
        var rt = CurrentRuntime;
        if (rt is null || rt.Token != ct) return;
        RefreshPmHook(rt);
        var reg = script.Globals.Get("messagesRegistering");
        if (reg.Type != DataType.Function) return;
        try { CallLua(script, reg, ct); }
        catch (Exception ex) when (!(ct.IsCancellationRequested || Root(ex) is ScriptStopException))
        { ScriptErr("hook", ex, d => Loc.Fmt("L.Lua.HookError", "messagesRegistering", d), LuaLogLevel.Warning); }
    }

    /// <summary>true si le run lié à <paramref name="ct"/> a des hooks de paquets / MP (pompe nécessaire).</summary>
    private bool PacketHooksActive(CancellationToken ct)
    {
        var rt = CurrentRuntime;
        return rt is not null && rt.Token == ct && rt.HasWork;
    }

    // ------------------------------------------------------------------ pompe (thread du script UNIQUEMENT)
    private void PumpHooks(CancellationToken ct)
    {
        PumpSupervisorMessages(ct);   // MZP23 : onSupervisorMessage (aucun hook de paquets requis)
        var rt = CurrentRuntime;
        if (rt is null || rt.Token != ct || rt.InPump) return;
        ThrowIfStopping(ct);
        rt.InPump = true;
        try
        {
            int budget = PumpBudget;
            int d = Interlocked.Exchange(ref rt.Dropped, 0);
            if (d > 0) Warn("hook", Loc.Fmt("L.Lua.HookDropped", d));
            while (budget-- > 0 && rt.PmInbox.TryDequeue(out var pm))
            {
                Interlocked.Decrement(ref rt.PmCount);
                if (rt.PmFn is { } f)
                    CallHook(rt, null, "onPrivateMessage", f, DynValue.NewString(pm.From), DynValue.NewString(pm.Text), DynValue.NewNumber(pm.Id));
            }
            while (budget-- > 0 && rt.Inbox.TryDequeue(out var e))
            {
                Interlocked.Decrement(ref rt.InboxCount);
                if (!e.P.ClientToServer)
                    foreach (var w in rt.Waiters)   // tous les waitFor en cours (un waitFor imbriqué ne prive pas l'externe)
                        if (w.Result is null && e.Seq > w.MinSeq && (w.Codes.Contains(e.P.Code) || e.P.Code == w.FailCode))
                            w.Result = (e.P, !w.Codes.Contains(e.P.Code));
                if (rt.Hooks.TryGetValue(e.P.Code, out var hs))
                    foreach (var h in hs.ToArray())   // copie : un gestionnaire peut (dés)enregistrer pendant l'appel
                        if (!e.P.ClientToServer || h.Outgoing)
                            CallHook(rt, h, h.Name, h.Fn, PacketToLua(rt.Script, e.P, h.Name, h.Snow));
            }
        }
        finally { rt.InPump = false; }
    }

    private void CallHook(ScriptRuntime rt, Hook? hook, string name, DynValue fn, params DynValue[] args)
    {
        try
        {
            CallLua(rt.Script, fn, rt.Token, args);
            if (hook is not null) hook.Errors = 0;
        }
        catch (Exception ex)
        {
            if (rt.Token.IsCancellationRequested || Root(ex) is ScriptStopException) throw new ScriptStopException();   // un arrêt DANS un hook déroule le script
            if (Root(ex) is HookExpiredException) throw new HookExpiredException();                                     // waitFor dans onTurn : tour perdu
            ScriptErr("hook", ex, d => Loc.Fmt("L.Lua.HookError", name, d), LuaLogLevel.Warning);
            if (hook is not null && ++hook.Errors >= 5)
            {
                if (rt.Hooks.TryGetValue(hook.Code, out var cur) && cur.Remove(hook) && cur.Count == 0) rt.Hooks.Remove(hook.Code);
                rt.RebuildInterest();
                Warn("hook", Loc.Fmt("L.Lua.HookDisabled", name));
            }
        }
    }

    private static string LuaErr(Exception ex)
        => (ex as InterpreterException ?? Root(ex) as InterpreterException)?.DecoratedMessage ?? Root(ex).Message;

    // snow=true: consommateur (developer:registerMessage, waitForAny, lastMessage, historicalMessage) -> défauts
    // proto3 sur les champs absents. snow=false (registerMessage / waitFor globaux) : comportement documenté, champ absent = nil.
    private DynValue PacketToLua(Script s, DecodedPacket p, string regName, bool snow = false)
    {
        // Défense en profondeur : un message porteur de secrets de session n'est JAMAIS converti pour Lua (R2).
        if (LuaSnowCompat.IsSensitiveMessage(p.Name ?? regName)) return DynValue.Nil;
        var tree = PacketTree.Build(_launch.Catalog, p.Name ?? regName, p.Payload, p.ClientToServer);
        var rt = CurrentRuntime;
        Table? meta = null;
        if (rt is not null && ReferenceEquals(rt.Script, s))
            meta = snow ? (rt.SnowPacketMeta ??= NewPacketMeta(s, defaults: true)) : (rt.PacketMeta ??= NewPacketMeta(s, defaults: false));
        return ToLua(s, tree, meta);
    }

    // Métatable des tables de paquets: msg.messageId / msg.MessageId ->
    // message_id ; t.Count -> nombre d'éléments d'une LISTE (un vrai champ `count` d'un message prime). defaults=true
    // seulement : champ ABSENT du message racine (proto3 omet 0 / false / "") -> sa valeur par défaut au lieu de nil. Les clés
    // snake_case d'origine restent lues en direct (aucun coût).
    private Table NewPacketMeta(Script s, bool defaults)
    {
        var mt = new Table(s);
        mt["__index"] = DynValue.NewCallback((_, a) =>
        {
            if (a.Count < 2 || a[0].Type != DataType.Table || a[1].Type != DataType.String) return DynValue.Nil;
            var t = a[0].Table;
            string k = a[1].String;
            var nm = t.RawGet("__name");
            if (k == "Count")
            {
                // Objet CLR un bot tiers: List(Of T).Count, ou la propriété Count d'un message qui a un champ `count`
                // (ExchangeCraftCountModifiedEvent) -> le champ prime ; sinon nombre d'éléments.
                var cv = t.RawGet("count");
                if (cv is not null && !cv.IsNil()) return cv;
                bool hasCountField = nm is { Type: DataType.String } && _launch.Catalog is { } cc
                                     && (cc.FieldNumber(nm.String, "count") is not null || cc.Message(nm.String)?.ByName.ContainsKey("count") == true);
                if (!hasCountField) return DynValue.NewNumber(t.Length);
                // champ count absent (proto3 : 0 omis) -> défaut ci-dessous (defaults) ou nil
            }
            string snake = LuaSnowCompat.SnakeCase(k);
            if (snake != k)
            {
                var v = t.RawGet(snake);
                if (v is not null && !v.IsNil()) return v;
            }
            if (!defaults) return DynValue.Nil;   // registerMessage / waitFor globaux : champ absent = nil (documenté)
            if (nm is { Type: DataType.String } && _launch.Catalog?.Message(nm.String) is { } def && def.ByName.TryGetValue(snake, out var fd))
            {
                if (fd.Repeated) { var e = new Table(s); e.MetaTable = mt; return DynValue.NewTable(e); }
                string ty = fd.ProtoType.TrimStart('.');
                string simple = ty[(ty.LastIndexOf('.') + 1)..];
                switch (simple)
                {
                    case "string": return DynValue.NewString("");
                    case "bool": return DynValue.False;
                    case "bytes": return DynValue.Nil;
                }
                bool isMsg = _launch.Catalog is { } c && (c.Message(simple) is not null || c.HasFields(simple));
                return isMsg ? DynValue.Nil : DynValue.NewNumber(0);   // sous-message absent -> nil ; nombre / enum -> 0
            }
            return DynValue.Nil;
        });
        return mt;
    }

    private static DynValue ToLua(Script s, object? v, Table? meta = null)
    {
        switch (v)
        {
            case Dictionary<string, object?> d:
            {
                var t = new Table(s);
                foreach (var kv in d) t.Set(kv.Key, ToLua(s, kv.Value, meta));
                if (meta is not null) t.MetaTable = meta;
                return DynValue.NewTable(t);
            }
            case List<object?> l:
            {
                var t = new Table(s);
                for (int i = 0; i < l.Count; i++) t.Set(i + 1, ToLua(s, l[i], meta));
                if (meta is not null) t.MetaTable = meta;
                return DynValue.NewTable(t);
            }
            case long x: return DynValue.NewNumber(x);
            case double x: return DynValue.NewNumber(x);
            case bool b: return DynValue.NewBoolean(b);
            case string str: return DynValue.NewString(str);
            default: return DynValue.Nil;
        }
    }

    /// <summary>Code wire d'un message : nom clair du mapping, code brut accepté tel quel (messages sans nom clair), puis
    /// compat tiers: identifiant PUBLIC Dofus 2 (ex. 5617), nom Dofus 2 (« NpcDialogQuestionMessage ») traduit par la table
    /// d'alias, enfin règle générique « XxxMessage » -&gt; XxxEvent / XxxRequest / XxxResponse / Xxx.
    /// Un message porteur de secrets de session (<see cref="LuaSnowCompat.IsSensitiveMessage"/> : jetons, ticket, clé d'API…)
    /// n'est JAMAIS résolu, quelle que soit la forme demandée (nom clair, alias / id Dofus 2, code brut) : null + refus signalé
    /// une fois. Couvre tous les points d'entrée Lua (registerMessage global et developer:, waitFor, waitForAny,
    /// suspendScriptUntil*, lastMessage, historicalMessage, createMessage).</summary>
    private string? ResolveCode(string name)
    {
        var (code, sensitive) = ResolveCore(name);
        if (!sensitive) return code;
        WarnSnowRefused(name.Trim());
        return null;
    }

    private (string? Code, bool Sensitive) ResolveCore(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || _launch.Catalog is not { } cat) return (null, false);
        name = name.Trim();
        string? code = null, matched = null;
        if (cat.CodeForName(name) is { } c) { code = c; matched = name; }
        // Code OBFUSQUÉ passé tel quel (ex "iva") : accepté SEULEMENT pour une licence développeur. Sinon on refuse de le
        // résoudre -> un utilisateur normal ne peut pas énumérer la table code -> nom clair via createMessage / waitFor / etc.
        else if (LicenseService.IsDeveloper && cat.NameForCode(name) is not null) code = name;
        else
            foreach (var candidate in LuaSnowCompat.MessageNameCandidates(name))
                if (cat.CodeForName(candidate) is { } c2) { code = c2; matched = candidate; break; }
        if (code is null) return (null, false);
        // Nom demandé ET nom de réception du code (confirmed.json peut les faire diverger) : l'un OU l'autre suffit à refuser.
        return (code, LuaSnowCompat.IsSensitiveMessage(matched) || LuaSnowCompat.IsSensitiveMessage(cat.NameForCode(code)));
    }

    /// <summary>Nom non résolu : « message inconnu » (une seule fois par nom si <paramref name="once"/>) — rien si le nom a été
    /// refusé (message sensible : ResolveCode l'a déjà signalé).</summary>
    private void WarnUnknownMessage(string name, bool once = false)
    {
        if (ResolveCore(name).Sensitive) return;
        if (once) lock (_warnedSnow) if (!_warnedSnow.Add("?msg:" + name.Trim())) return;
        Warn("hook", Loc.Fmt("L.Lua.HookUnknownMessage", name));
    }

    private static bool IsNoOperation(string name) => LuaSnowCompat.IsNoOperation(name);

    // ------------------------------------------------------------------ verbes (liés à la fin de Bind)
    private void BindPlatform(Script s, ScriptRuntime rt, CancellationToken ct)
    {
        static int B(CallbackArguments a) => a.Count > 0 && a[0].Type == DataType.Table ? 1 : 0;   // self présent ? (appel colon)
        static string S(CallbackArguments a, int i) { int j = B(a) + i; return j < a.Count ? (a[j].CastToString() ?? "") : ""; }
        static DynValue Arg(CallbackArguments a, int i) { int j = B(a) + i; return j < a.Count ? a[j] : DynValue.Nil; }
        static bool IsFn(DynValue v) => v.Type is DataType.Function or DataType.ClrFunction;

        // ---- hooks de paquets ----
        // register(name, fn [, outgoing]) : global registerMessage = UN gestionnaire par message (remplace), paquets entrants
        // seulement par défaut, champ absent = nil. developer:registerMessage (snow developer.vb:
        // callMessages.Add à CHAQUE appel) AJOUTE un gestionnaire — le ré-enregistrer dans une boucle le fait donc appeler N
        // fois, et chaque appel compte dans la limite de 64 —, voit AUSSI les paquets sortants (…Request) sauf outgoing=false,
        // et reçoit des tables avec défauts proto3 (0 / false / "" au lieu de nil).
        DynValue Register(bool snow) => DynValue.NewCallback((_, a) =>
        {
            string name = S(a, 0);
            var fn = Arg(a, 1);
            if (name.Length == 0 || !IsFn(fn)) { Warn("hook", Loc.T("L.Lua.HookBadArgs")); return DynValue.False; }
            if (ResolveCode(name) is not { } code) { WarnUnknownMessage(name); return DynValue.False; }
            int existing = rt.Hooks.TryGetValue(code, out var cur) ? cur.Count : 0;
            if (rt.HookCount - (snow ? 0 : existing) >= MaxHooks) { Warn("hook", Loc.Fmt("L.Lua.HookLimit", MaxHooks)); return DynValue.False; }
            var o = Arg(a, 2);
            bool outgoing = o.IsNil() ? snow : o.CastToBool();
            var h = new Hook(code, name.Trim(), fn, outgoing, snow);
            if (snow && cur is not null) cur.Add(h);
            else rt.Hooks[code] = new List<Hook> { h };
            rt.RebuildInterest();
            return DynValue.True;
        });
        var register = Register(snow: false);
        var unregister = DynValue.NewCallback((_, a) =>
        {
            string name = S(a, 0);
            if (ResolveCode(name) is not { } code) { WarnUnknownMessage(name); return DynValue.False; }
            bool removed = rt.Hooks.Remove(code);   // TOUS les gestionnaires de ce message
            rt.RebuildInterest();
            return DynValue.NewBoolean(removed);
        });
        var waitFor = DynValue.NewCallback((_, a) => WaitForPacket(rt, a, ct));
        s.Globals["registerMessage"] = register;
        s.Globals["unRegisterMessage"] = unregister;
        s.Globals["unregisterMessage"] = unregister;
        s.Globals["waitFor"] = waitFor;
        var dev = new Table(s);
        dev["registerMessage"] = Register(snow: true);   // AJOUTE (pas de remplacement) + sortants par défaut : voir Register
        dev["unRegisterMessage"] = unregister;
        dev["unregisterMessage"] = unregister;
        dev["waitFor"] = waitFor;
        s.Globals["developer"] = DynValue.NewTable(dev);   // portabilité un bot tiers: developer:registerMessage(...)
        BindSnowDeveloper(s, rt, dev, ct);                 // compat tiers: suspendScriptUntil, listenPackets, historicalMessage…

        // ---- require(name) : module Lua de %APPDATA%/GivreClone/scripts/lib (même Script, même sandbox) ----
        s.Globals["require"] = DynValue.NewCallback((_, a) => Require(rt, S(a, 0)));

        // ---- loadScript / reloadScript : bascule du script PRINCIPAL (résolue par le VM, thread UI) ----
        var load = DynValue.NewCallback((_, a) =>
        {
            string name = S(a, 0).Trim();
            if (name.Length == 0) return DynValue.False;
            return SwitchScript(name, ct);
        });
        s.Globals["loadScript"] = load;
        s.Globals["loadAndStart"] = load;
        s.Globals["reloadScript"] = DynValue.NewCallback((_, a) => SwitchScript(null, ct));

        // ---- notify(msg [, sound=true]) : toast local + son + centre de notifications ----
        s.Globals["notify"] = DynValue.NewCallback((_, a) =>
        {
            string msg = SanitizeNotify(S(a, 0));
            if (msg.Length == 0) return DynValue.False;
            var sv = Arg(a, 1);
            bool sound = sv.IsNil() || sv.CastToBool();
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastNotifyAt) < NotifyMinIntervalMs) return DynValue.False;
            Interlocked.Exchange(ref _lastNotifyAt, now);
            string who = _world.Get(_pid).CharacterName is { Length: > 0 } n ? n : _pid.ToString();
            Toasts.Post(Loc.Fmt("L.Lua.NotifyToast", who, msg), success: true, sound);
            return DynValue.True;
        });

        // ---- team: ajouts (la table est créée par BindModules) ----
        var teamDv = s.Globals.Get("team");
        if (teamDv.Type == DataType.Table)
        {
            var team = teamDv.Table;
            team["runOnce"] = DynValue.NewCallback((_, a) => DynValue.NewBoolean(TeamRunOnce(S(a, 0), S(a, 1), ct)));
            var inviteAll = DynValue.NewCallback((_, a) => DynValue.NewNumber(TeamInviteAll(ct)));
            team["inviteAll"] = inviteAll;
            var isInParty = DynValue.NewCallback((_, a) =>
            {
                var v = Arg(a, 0);
                if (v.IsNil()) return DynValue.NewBoolean(_launch.PartyIdOf(_pid) != 0);
                if (v.Type == DataType.Number) return DynValue.NewBoolean(_launch.IsInParty(_pid, (long)v.Number));
                return DynValue.NewBoolean(_launch.IsInParty(_pid, v.CastToString() ?? ""));
            });
            team["isInParty"] = isInParty;
            team["partyId"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.PartyIdOf(_pid)));
            team["partyMembers"] = DynValue.NewCallback((_, a) => BuildList(s, _launch.PartyMembers(_pid), (r, m) =>
            {
                r["id"] = DynValue.NewNumber(m.Id); r["name"] = DynValue.NewString(m.Name); r["leader"] = DynValue.NewBoolean(m.Leader);
            }));
            s.Globals["inviteTeam"] = inviteAll;
            s.Globals["isInParty"] = isInParty;
        }
    }

    // waitFor(name [, failName] [, timeoutMs] [, action]) -> t | false, tFail | nil (délai)
    private DynValue WaitForPacket(ScriptRuntime rt, CallbackArguments a, CancellationToken ct)
    {
        ThrowIfStopping(ct);
        int o = a.Count > 0 && a[0].Type == DataType.Table ? 1 : 0;
        if (rt.InPump)
        {
            if (!_waitForInHookWarned) { _waitForInHookWarned = true; Warn("hook", Loc.T("L.Lua.WaitForInHook")); }
            return DynValue.Nil;
        }
        string name = o < a.Count ? (a[o].CastToString() ?? "") : "";
        string? failName = null; int timeout = WaitForDefaultMs; DynValue? action = null;
        for (int i = o + 1; i < a.Count; i++)
        {
            var v = a[i];
            if (v.Type == DataType.String && failName is null) failName = v.String;
            else if (v.Type == DataType.Number) timeout = (int)Math.Clamp(v.Number, WaitForMinMs, WaitForMaxMs);
            else if (v.Type is DataType.Function or DataType.ClrFunction) action = v;
        }
        timeout = Math.Clamp(timeout, WaitForMinMs, WaitForMaxMs);
        if (ResolveCode(name) is not { } code) { WarnUnknownMessage(name); return DynValue.Nil; }
        string? failCode = null;
        if (!string.IsNullOrWhiteSpace(failName))
        {
            failCode = ResolveCode(failName);
            if (failCode is null) { WarnUnknownMessage(failName); return DynValue.Nil; }
        }
        // Armé AVANT l'action : une réponse rapide à la requête envoyée par l'action ne peut pas être manquée.
        var w = new Waiter { Codes = new HashSet<string>(StringComparer.Ordinal) { code }, FailCode = failCode, MinSeq = Interlocked.Read(ref rt.Seq) };
        rt.Waiters.Add(w);
        rt.RebuildInterest();
        try
        {
            if (action is not null) CallLua(rt.Script, action, ct);
            long end = Environment.TickCount64 + timeout;
            while (true)
            {
                ThrowIfStopping(ct);
                ThrowIfHookTurnGone();                 // dans onTurn : tour perdu -> on déroule le hook
                if (HooksActive) PumpFightHooks(ct);   // les tours de combat restent réclamés pendant l'attente
                PumpHooks(ct);                         // les autres hooks de paquets continuent d'être servis
                if (w.Result is { } res)
                {
                    var tbl = PacketToLua(rt.Script, res.P, res.Fail ? failName! : name);
                    return res.Fail ? DynValue.NewTuple(DynValue.False, tbl) : tbl;
                }
                long left = end - Environment.TickCount64;
                if (left <= 0) return DynValue.Nil;
                if (ct.WaitHandle.WaitOne((int)Math.Min(50, left))) throw new ScriptStopException();
            }
        }
        finally
        {
            rt.Waiters.Remove(w);   // CETTE instance seulement : un waitFor externe reste armé
            rt.RebuildInterest();
            // Sans hook enregistré ni autre waitFor en cours, personne ne pomperait les restes (doublons du message attendu) :
            // on vide la file pour ne pas grignoter sa capacité.
            if (!rt.HasWork && rt.Waiters.Count == 0) while (rt.Inbox.TryDequeue(out _)) Interlocked.Decrement(ref rt.InboxCount);
        }
    }

    // require(name) -> module (cache par run ; nom validé ; jamais de chemin absolu dans les messages : il contient le nom
    // d'utilisateur Windows et le mode stream ne doit pas le montrer).
    private DynValue Require(ScriptRuntime rt, string name)
    {
        var key = LuaModulePath.Normalize(name);
        if (key is null) throw new ScriptRuntimeException(Loc.Fmt("L.Lua.RequireBadName", name));
        if (rt.Modules.TryGetValue(key, out var cached)) return cached;
        if (rt.Loading.Contains(key)) throw new ScriptRuntimeException(Loc.Fmt("L.Lua.RequireCircular", key));
        string full;
        try
        {
            Directory.CreateDirectory(LibDir);
            if (!LuaModulePath.TryResolve(LibDir, key, out full, out _)) throw new ScriptRuntimeException(Loc.Fmt("L.Lua.RequireBadName", name));
        }
        catch (ScriptRuntimeException) { throw; }
        catch { throw new ScriptRuntimeException(Loc.Fmt("L.Lua.RequireNotFound", key)); }
        string code = ReadScriptFile(LibDir, full, key, "L.Lua.RequireNotFound", "L.Lua.RequireTooLarge");
        rt.Loading.Add(key);
        try
        {
            var r = CallLua(rt.Script, rt.Script.LoadString(code, null, "lib:" + key), rt.Token);
            if (r.Type == DataType.Tuple) r = r.Tuple.Length > 0 ? r.Tuple[0] : DynValue.Nil;
            if (r.IsNil() || r.IsVoid()) r = DynValue.True;
            rt.Modules[key] = r;
            return r;
        }
        finally { rt.Loading.Remove(key); }
    }

    /// <summary>Lit un fichier Lua SOUS <paramref name="root"/> : doit exister, ≤ 512 Ko, sans point de réanalyse (lien /
    /// jonction) sur le fichier ni sur ses dossiers intermédiaires. Lève une erreur Lua (pcall-able) sinon.</summary>
    private static string ReadScriptFile(string root, string full, string key, string notFoundKey, string tooLargeKey)
    {
        try
        {
            var fi = new FileInfo(full);
            if (!fi.Exists) throw new ScriptRuntimeException(Loc.Fmt(notFoundKey, key));
            if (HasReparsePoint(root, fi)) throw new ScriptRuntimeException(Loc.Fmt("L.Lua.RequireBadName", key));
            if (fi.Length > RequireMaxBytes) throw new ScriptRuntimeException(Loc.Fmt(tooLargeKey, key));
            return File.ReadAllText(full, Encoding.UTF8);
        }
        catch (ScriptRuntimeException) { throw; }
        catch { throw new ScriptRuntimeException(Loc.Fmt(notFoundKey, key)); }
    }

    internal static bool HasReparsePoint(string root, FileInfo fi)
    {
        if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
        string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        for (var d = fi.Directory; d is not null; d = d.Parent)
        {
            if (d.FullName.TrimEnd(Path.DirectorySeparatorChar).Length <= rootFull.Length) break;   // on remonte jusqu'à la racine (exclue)
            if (d.Attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
        }
        return false;
    }

    /// <summary>Résout un script de la bibliothèque disque %APPDATA%/GivreClone/scripts/&lt;name&gt;.lua (repli de loadScript).
    /// null si absent / invalide. Appelé par le VM (thread UI).</summary>
    public static string? ReadScriptsFolderScript(string name)
    {
        try
        {
            var key = LuaModulePath.Normalize(name);
            if (key is null || !Directory.Exists(ScriptsRootDir)) return null;
            if (!LuaModulePath.TryResolve(ScriptsRootDir, key, out var full, out _)) return null;
            return ReadScriptFile(ScriptsRootDir, full, key, "L.Lua.LoadScriptNotFound", "L.Lua.RequireTooLarge");
        }
        catch { return null; }
    }

    private DynValue SwitchScript(string? name, CancellationToken ct)
    {
        // Run déjà arrêté (Stop utilisateur, MODERATOR_STOP, fin de plage, /abortCmd — ex. appel depuis stopped()) : on
        // n'enfile RIEN, sinon la bascule annulerait l'arrêt au Tick suivant.
        if (ct.IsCancellationRequested) return DynValue.False;
        var tcs = _launch.EnqueueScriptSwitch(_pid, name, ct);
        var task = tcs.Task;
        try
        {
            // Si CET hôte est celui qui bascule, Start() annule notre jeton -> WaitUntil lève ScriptStopException (déroulé propre).
            bool done = WaitUntil(() => task.IsCompleted, 3000, ct);
            return DynValue.NewBoolean(done && task.IsCompletedSuccessfully && task.Result);
        }
        finally
        {
            // Délai dépassé ou run arrêté pendant l'attente : la demande est ABANDONNÉE (le VM saute une demande déjà résolue)
            // -> jamais de bascule tardive après que le script a lu « false ».
            tcs.TrySetResult(false);
        }
    }

    private static string SanitizeNotify(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(Math.Min(s.Length, NotifyMaxChars));
        foreach (char c in s)
        {
            if (char.IsControl(c)) continue;
            sb.Append(c);
            if (sb.Length >= NotifyMaxChars) break;
        }
        return sb.ToString().Trim();
    }

    // ------------------------------------------------------------------ team: runOnce / inviteAll
    private bool TeamRunOnce(string memberName, string source, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;   // run arrêté (ex. depuis stopped()) : rien n'est envoyé aux membres
        if (!LicenseService.HasPermission(LicenseService.Perm.Multi)) { Warn("équipe", Loc.T("L.Perm.MultiDenied")); return false; }
        var m =_launch.TeamMembers(_pid).FirstOrDefault(x => string.Equals(x.Name, memberName?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (m.Pid == 0 || m.Pid == _pid || (_launch.CurrentMap(m.Pid) ?? 0) == 0)
        {
            Warn("équipe", Loc.Fmt("L.Lua.TeamRunOnceNoMember", memberName));
            return false;
        }
        if (!_launch.EnqueueTeamRunOnce(_pid, m.Pid, source))
        {
            Warn("équipe", Loc.T("L.Lua.TeamRunOnceRejected"));   // source vide / trop longue, ou file pleine
            return false;
        }
        return true;
    }

    private int TeamInviteAll(CancellationToken ct)
    {
        ThrowIfStopping(ct);
        if (!_launch.IsTeamLeader(_pid)) { Warn("équipe", Loc.T("L.Lua.TeamInviteNotLeader")); return 0; }
        int n = 0;
        foreach (var m in _launch.TeamMembers(_pid))
        {
            if (m.Pid == _pid || string.IsNullOrWhiteSpace(m.Name) || (_launch.CurrentMap(m.Pid) ?? 0) == 0) continue;
            if (_launch.IsInParty(_pid, m.Pid, m.Name)) continue;
            if (_launch.PartyInvite(_pid, m.Name)) n++;
            else Warn("équipe", Loc.Fmt("L.Lua.TeamInviteFailed", m.Name, _launch.LastPartyInviteError ?? ""));
            InterruptibleDelay(400, ct);   // anti-flood (pas de pompe)
        }
        Info("équipe", Loc.Fmt("L.Lua.TeamInvitedAll", n));
        return n;
    }
}
