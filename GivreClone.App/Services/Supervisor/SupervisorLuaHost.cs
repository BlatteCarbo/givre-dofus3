using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services.Supervisor;

/// <summary>Dépendances d'un superviseur : tout ce qu'il peut voir et ordonner passe par ces ports (testables, sans UI).</summary>
public sealed record SupervisorServices(IBotDirectory Bots, IBotOrders Orders, IAccountsPort Accounts, IProxiesPort Proxies,
                                        ISupervisorPermissions Perms, SupervisorBus Bus)
{
    public ITeamSupervisorPort? Teams { get; init; }
    /// <summary>sendWebhook(url, message) : poste un message texte ; false si refusé (droit absent, adresse invalide). Fourni par l'application.</summary>
    public Func<string, string, bool> Webhook { get; init; } = (_, _) => false;
}

/// <summary>Conversion valeurs Lua ↔ données simples (copie profonde pour les messages : jamais de fonction, profondeur et taille bornées).</summary>
public static class LuaData
{
    public const int MaxDepth = 8, MaxNodes = 2000;

    public static object? ToObject(DynValue v) => ToObject(v, 0, new int[1]);

    private static object? ToObject(DynValue v, int depth, int[] nodes)
    {
        if (++nodes[0] > MaxNodes) throw new ArgumentException("donnée trop volumineuse");
        if (depth > MaxDepth) throw new ArgumentException("donnée trop profonde");
        switch (v.Type)
        {
            case DataType.Nil: case DataType.Void: return null;
            case DataType.Boolean: return v.Boolean;
            case DataType.Number: return v.Number;
            case DataType.String: return v.String;
            case DataType.Table:
                var t = v.Table;
                int n = t.Length;
                var pairs = t.Pairs.ToList();
                if (n > 0 && pairs.Count == n)   // suite 1..n pure -> liste
                    return Enumerable.Range(1, n).Select(i => ToObject(t.Get(i), depth + 1, nodes)).ToList();
                var d = new Dictionary<string, object?>();
                foreach (var p in pairs) d[p.Key.Type == DataType.String ? p.Key.String : p.Key.CastToString()] = ToObject(p.Value, depth + 1, nodes);
                return d;
            default: throw new ArgumentException("une fonction ne peut pas être transmise");
        }
    }

    public static DynValue FromObject(Script s, object? o) => o switch
    {
        null => DynValue.Nil,
        bool b => DynValue.NewBoolean(b),
        string str => DynValue.NewString(str),
        double d => DynValue.NewNumber(d),
        int i => DynValue.NewNumber(i),
        long l => DynValue.NewNumber(l),
        IReadOnlyList<object?> list => ListTable(s, list),
        IReadOnlyDictionary<string, object?> dict => DictTable(s, dict),
        _ => DynValue.NewString(o.ToString() ?? ""),
    };

    private static DynValue ListTable(Script s, IReadOnlyList<object?> list)
    {
        var t = new Table(s);
        for (int i = 0; i < list.Count; i++) t[i + 1] = FromObject(s, list[i]);
        return DynValue.NewTable(t);
    }

    private static DynValue DictTable(Script s, IReadOnlyDictionary<string, object?> dict)
    {
        var t = new Table(s);
        foreach (var kv in dict) t[kv.Key] = FromObject(s, kv.Value);
        return DynValue.NewTable(t);
    }
}

/// <summary>Arrêt demandé du script superviseur (jamais une erreur).</summary>
public sealed class SupervisorStopException : Exception { }

/// <summary>
/// Script SUPERVISEUR (portail) : tourne côté logiciel, ne pilote aucun perso, surveille les bots et leur donne des ordres.
/// Définit move() (rappelée en boucle ; false = fin) et/ou onBotEvent(bot, évènement, infos) / onBotMessage(bot, sujet, données).
/// API : bots:, accounts:, proxies:, givreController: (chemins superviseur) + print*, delay, stopScript.
/// Les callbacks sont servis sur le thread du script, entre deux move() et pendant les attentes. Noms tolérants (casse, snake_case).
/// </summary>
public sealed partial class SupervisorLuaHost : IDisposable
{
    public const int DefaultOrderTimeoutMs = 15000, MaxWaitMs = 30 * 60 * 1000;

    /// <summary>Attente maximale du résultat d'un ordre (15 s au plus selon le portail). Réglable pour les tests.</summary>
    public int OrderTimeoutMs { get; set; } = DefaultOrderTimeoutMs;
    public const string NeedMulti = "permission Multi requise";

    private readonly string _name;
    private readonly SupervisorServices _svc;
    private readonly ConcurrentQueue<(BotSnapshot Bot, BotEvent Event)> _events = new();
    private readonly Mailbox _box;
    private CancellationTokenSource? _cts;
    private Task? _task;
    private Script? _script;
    private DynValue _onEvent = DynValue.Nil, _onMessage = DynValue.Nil;
    private string _stopReason = "";

    public SupervisorLuaHost(string name, SupervisorServices svc)
    {
        _name = name;
        _svc = svc;
        _box = svc.Bus.RegisterSupervisor(name);
    }

    /// <summary>Journal du script : (niveau, texte) avec niveau info | success | warning | error ; « system » = cycle de vie du superviseur.</summary>
    public event Action<string, string>? Log;
    /// <summary>Fin du script : raison (ended | stopped | error | user…).</summary>
    public event Action<string>? Ended;

    public bool Running => _task is { IsCompleted: false };
    public string StopReason => _stopReason;

    public void Start(string code)
    {
        if (Running) return;
        _cts = new CancellationTokenSource();
        _stopReason = "";
        var ct = _cts.Token;
        _task = Task.Factory.StartNew(() => RunLoop(code, ct), TaskCreationOptions.LongRunning);
    }

    public void Stop(string reason = "user")
    {
        if (_stopReason.Length == 0) _stopReason = reason;
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>Attend la fin du script (tests, arrêt propre).</summary>
    public bool WaitEnded(TimeSpan timeout) => _task?.Wait(timeout) ?? true;

    /// <summary>Dépose un évènement d'un bot ; servi à onBotEvent sur le thread du script. Thread-safe.</summary>
    public void Raise(BotSnapshot bot, BotEvent ev) { if (Running) _events.Enqueue((bot, ev)); }

    public void Dispose()
    {
        Stop("dispose");
        try { _task?.Wait(2000); } catch { }
        _svc.Bus.UnregisterSupervisor(_name);
        _cts?.Dispose();
    }

    // ---------------------------------------------------------------- boucle
    private void RunLoop(string code, CancellationToken ct)
    {
        string reason = "ended";
        try
        {
            var s = _script = new Script(CoreModules.Preset_SoftSandbox);
            Bind(s, ct);
            s.DoString(code, null, "superviseur");
            _onEvent = s.Globals.Get("onBotEvent");
            _onMessage = s.Globals.Get("onBotMessage");
            var move = s.Globals.Get("move");
            if (move.Type != DataType.Function && _onEvent.Type != DataType.Function && _onMessage.Type != DataType.Function)
            { Emit("error", "le script doit définir move() et/ou onBotEvent / onBotMessage"); reason = "error"; return; }
            Emit("system", "superviseur démarré");
            while (!ct.IsCancellationRequested)
            {
                Pump(ct);
                if (move.Type == DataType.Function)
                {
                    var r = s.Call(move);
                    if (r.Type == DataType.Boolean && !r.Boolean) break;
                    Sleep(20, ct);   // jamais de rotation à vide si move() rend tout de suite
                }
                else Sleep(100, ct);
            }
            if (ct.IsCancellationRequested) reason = _stopReason.Length > 0 ? _stopReason : "stopped";
        }
        catch (Exception ex)
        {
            if (IsStop(ex) || ct.IsCancellationRequested) reason = _stopReason.Length > 0 ? _stopReason : "stopped";
            else { Emit("error", Describe(ex)); reason = "error"; }
        }
        finally
        {
            _stopReason = reason;
            Emit("system", $"superviseur arrêté ({reason})");
            Ended?.Invoke(reason);
        }
    }

    private static bool IsStop(Exception e)
    {
        for (var x = e; x is not null; x = x.InnerException)
            if (x is SupervisorStopException or OperationCanceledException) return true;
        return false;
    }

    private static string Describe(Exception e) => e is ScriptRuntimeException sre ? sre.DecoratedMessage ?? sre.Message : e.Message;

    private void Emit(string level, string text) => Log?.Invoke(level, text);

    private static void Sleep(int ms, CancellationToken ct)
    {
        if (ct.WaitHandle.WaitOne(ms)) throw new SupervisorStopException();
    }

    // ---------------------------------------------------------------- callbacks
    /// <summary>Sert les évènements et messages en attente (thread du script uniquement).</summary>
    private void Pump(CancellationToken ct)
    {
        if (_script is not { } s) return;
        int budget = 64;
        while (budget-- > 0 && _events.TryDequeue(out var e))
        {
            ct.ThrowIfCancellationRequested();
            if (_onEvent.Type != DataType.Function) continue;
            var info = e.Event.Info is null ? DynValue.Nil : LuaData.FromObject(s, e.Event.Info as IReadOnlyDictionary<string, object?>);
            CallSafe("onBotEvent", _onEvent, BotTable(s, e.Bot), DynValue.NewString(e.Event.Name), info);
        }
        int dropped = _box.TakeDropped();
        if (dropped > 0) Emit("warning", $"{dropped} message(s) perdu(s) (64 en attente au plus)");
        _box.Pump(m => OnMessage(s, m));
    }

    private bool OnMessage(Script s, BusMessage m)
    {
        if (_onMessage.Type != DataType.Function) return false;   // gardé pour un bots:waitMessage
        CallSafe("onBotMessage", _onMessage, m.SenderBot is null ? DynValue.Nil : BotTable(s, m.SenderBot),
                 DynValue.NewString(m.Subject), LuaData.FromObject(s, m.Data));
        return true;
    }

    private void CallSafe(string what, DynValue fn, params DynValue[] args)
    {
        try { _script!.Call(fn, args); }
        catch (Exception ex) when (!IsStop(ex)) { Emit("error", $"{what} : {Describe(ex)}"); }
    }

    // ---------------------------------------------------------------- tables
    public static DynValue BotTable(Script s, BotSnapshot b)
    {
        var t = new Table(s)
        {
            ["account"] = b.Account, ["name"] = b.Name, ["pid"] = b.Pid, ["mode"] = b.Mode, ["status"] = b.Status,
            ["online"] = b.Online, ["inGame"] = b.InGame, ["mapId"] = (double)b.MapId, ["coords"] = b.Coords, ["level"] = b.Level,
            ["kamas"] = (double)b.Kamas, ["pods"] = b.Pods, ["podsMax"] = b.PodsMax, ["podsPercent"] = b.PodsPercent, ["life"] = b.Life,
            ["maxLife"] = b.MaxLife, ["inFight"] = b.InFight, ["script"] = b.Script, ["scriptRunning"] = b.ScriptRunning, ["team"] = b.Team,
            ["leader"] = b.Leader, ["lastStop"] = b.LastStop, ["disconnect"] = b.Disconnect, ["since"] = b.SinceSec,
        };
        return DynValue.NewTable(t);
    }

    private static DynValue AccountTable(Script s, AccountInfo a)
    {
        var t = new Table(s)
        {
            ["id"] = a.Id, ["label"] = a.Label, ["character"] = a.Character, ["status"] = a.Status, ["mode"] = a.Mode,
            ["browser"] = a.Browser, ["autoScript"] = a.AutoScript,
        };
        if (a.Proxy is not null) t["proxy"] = a.Proxy;
        if (a.GameProxy is not null) t["gameProxy"] = a.GameProxy;
        if (a.Pending is { } p)
        {
            t["pendingCharacter"] = DynValue.NewTable(new Table(s) { ["serverId"] = p.ServerId, ["server"] = p.Server, ["breed"] = p.Breed, ["sex"] = p.Sex, ["name"] = p.Name });
        }
        if (a.Added is bool added) t["added"] = added;
        return DynValue.NewTable(t);
    }

    private static DynValue ProxyTable(Script s, ProxyInfo p)
    {
        var t = new Table(s) { ["id"] = p.Id, ["name"] = p.Name, ["type"] = p.Type, ["accounts"] = p.Accounts };
        if (p.Host is not null) t["host"] = p.Host;
        if (p.Port is int port) t["port"] = port;
        if (p.Auth is bool auth) t["auth"] = auth;
        return DynValue.NewTable(t);
    }

    private static DynValue ToArrayTable<T>(Script s, IEnumerable<T> items, Func<Script, T, DynValue> make)
    {
        var t = new Table(s);
        int i = 1;
        foreach (var it in items) t[i++] = make(s, it);
        return DynValue.NewTable(t);
    }

    // ---------------------------------------------------------------- liaison des verbes
    private sealed class Module
    {
        public readonly Table Table;
        public readonly Dictionary<string, DynValue> ByNorm = new();
        public Module(Script s) { Table = new Table(s); }
    }

    private static string Norm(string s) => s.Replace("_", "").ToLowerInvariant();

    private Module NewModule(Script s, string name)
    {
        var m = new Module(s);
        var meta = new Table(s);
        meta["__index"] = DynValue.NewCallback((_, a) =>
            a.Count >= 2 && a[1].Type == DataType.String && m.ByNorm.TryGetValue(Norm(a[1].String), out var f) ? f : DynValue.Nil);
        m.Table.MetaTable = meta;
        s.Globals[name] = DynValue.NewTable(m.Table);
        return m;
    }

    /// <summary>Déclare un verbe : appelable en <c>mod:verbe(...)</c> (le premier argument « self » est retiré) ou <c>mod.verbe(...)</c>.</summary>
    private static void Verb(Module m, string name, Func<List<DynValue>, DynValue> body)
    {
        var cb = DynValue.NewCallback((_, a) =>
        {
            var list = new List<DynValue>(a.Count);
            for (int i = 0; i < a.Count; i++) list.Add(a[i]);
            if (list.Count > 0 && list[0].Type == DataType.Table && ReferenceEquals(list[0].Table, m.Table)) list.RemoveAt(0);
            return body(list);
        });
        m.Table[name] = cb;
        m.ByNorm[Norm(name)] = cb;
    }

    private static DynValue Arg(List<DynValue> a, int i) => i < a.Count ? a[i] : DynValue.Nil;
    private static string? Str(DynValue v) => v.Type == DataType.String ? v.String : v.Type == DataType.Number ? v.CastToString() : null;
    private static DynValue Pair(bool ok, string reason = "") => DynValue.NewTuple(DynValue.NewBoolean(ok), DynValue.NewString(reason));
    private static DynValue Pair(OrderResult r) => Pair(r.Ok, r.Reason);

    private void Bind(Script s, CancellationToken ct)
    {
        s.Globals["print"] = DynValue.NewCallback((_, a) => { Emit("info", Join(a)); return DynValue.Nil; });
        s.Globals["printSuccess"] = DynValue.NewCallback((_, a) => { Emit("success", Join(a)); return DynValue.Nil; });
        s.Globals["printWarning"] = DynValue.NewCallback((_, a) => { Emit("warning", Join(a)); return DynValue.Nil; });
        s.Globals["printError"] = DynValue.NewCallback((_, a) => { Emit("error", Join(a)); return DynValue.Nil; });
        s.Globals["sendWebhook"] = DynValue.NewCallback((_, a) =>
        {
            int off = a.Count > 0 && a[0].Type == DataType.Table ? 1 : 0;   // sendWebhook(url, msg) ou supervisor:sendWebhook(url, msg)
            string url = off < a.Count ? (a[off].CastToString() ?? "") : "";
            string msg = off + 1 < a.Count ? (a[off + 1].ToPrintString()) : "";
            return DynValue.NewBoolean(url.Length > 0 && _svc.Webhook(url, msg));
        });
        s.Globals["stopScript"] = DynValue.NewCallback((_, _) => { Stop("script"); throw new SupervisorStopException(); });
        s.Globals["delay"] = DynValue.NewCallback((_, a) => { PumpingDelay(a.Count > 0 ? (int)a[0].CastToNumber().GetValueOrDefault() : 0, ct); return DynValue.Nil; });

        BindBots(s, NewModule(s, "bots"), ct);
        BindTeams(s, NewModule(s, "teams"), ct);
        BindAccounts(s, NewModule(s, "accounts"), ct);
        BindProxies(s, NewModule(s, "proxies"), ct);
        BindController(s, NewModule(s, "givreController"), ct);
    }

    private static string Join(CallbackArguments a)
    {
        var parts = new List<string>();
        for (int i = 0; i < a.Count; i++) parts.Add(a[i].ToPrintString());
        return string.Join("\t", parts);
    }

    /// <summary>Attente qui sert les callbacks (évènements, messages) toutes les ~40 ms et réagit tout de suite à l'arrêt.</summary>
    private void PumpingDelay(int ms, CancellationToken ct)
    {
        var until = DateTime.UtcNow.AddMilliseconds(Math.Max(0, ms));
        while (true)
        {
            Pump(ct);
            var left = (until - DateTime.UtcNow).TotalMilliseconds;
            if (left <= 0) return;
            Sleep((int)Math.Min(40, left), ct);
        }
    }

    /// <summary>Attend une tâche d'ordre (15 s au plus), en servant les callbacks ; refuse sans le droit Multi.</summary>
    private OrderResult Order(Func<CancellationToken, Task<OrderResult>> run, CancellationToken ct, bool needsMulti = true)
    {
        if (needsMulti && !_svc.Perms.HasMulti) return OrderResult.No(NeedMulti);
        var t = run(ct);
        var until = DateTime.UtcNow.AddMilliseconds(OrderTimeoutMs);
        while (!t.IsCompleted)
        {
            if (DateTime.UtcNow >= until) return OrderResult.No($"délai dépassé ({(OrderTimeoutMs >= 1000 ? OrderTimeoutMs / 1000 + " s" : OrderTimeoutMs + " ms")})");
            Pump(ct);
            Sleep(25, ct);
        }
        return t.IsFaulted ? OrderResult.No(t.Exception?.GetBaseException().Message ?? "erreur") : t.Result;
    }

    private T Await<T>(Task<T> t, CancellationToken ct, int maxMs)
    {
        var until = DateTime.UtcNow.AddMilliseconds(maxMs);
        while (!t.IsCompleted)
        {
            if (DateTime.UtcNow >= until) throw new TimeoutException("délai dépassé");
            Pump(ct);
            Sleep(25, ct);
        }
        return t.GetAwaiter().GetResult();
    }

    private BotSnapshot? Find(string? key) => BotMatcher.Find(_svc.Bots.Snapshot(), key);

    // ---------------------------------------------------------------- bots:
    private void BindBots(Script s, Module m, CancellationToken ct)
    {
        Verb(m, "list", _ => ToArrayTable(s, _svc.Bots.Snapshot(), BotTable));
        Verb(m, "get", a => Find(Str(Arg(a, 0))) is { } b ? BotTable(s, b) : DynValue.Nil);

        Verb(m, "connect", a =>
        {
            string? who = Str(Arg(a, 0)), chr = null, srv = null;
            var o = Arg(a, 1);
            if (o.Type == DataType.Table) { chr = Str(o.Table.Get("character")); srv = Str(o.Table.Get("server")); }
            else chr = Str(o);
            return Pair(who is null ? OrderResult.No("bot manquant") : Order(c => _svc.Orders.ConnectAsync(who, chr, srv, c), ct));
        });
        Verb(m, "disconnect", a => Pair(Str(Arg(a, 0)) is { } w ? Order(c => _svc.Orders.DisconnectAsync(w, c), ct) : OrderResult.No("bot manquant")));
        Verb(m, "reconnect", a => Pair(Str(Arg(a, 0)) is { } w ? Order(c => _svc.Orders.ReconnectAsync(w, c), ct) : OrderResult.No("bot manquant")));

        Verb(m, "switchCharacter", a =>
        {
            string? who = Str(Arg(a, 0)), chr, srv; bool restart = true;
            var o = Arg(a, 1);
            if (o.Type == DataType.Table)
            {
                chr = Str(o.Table.Get("character")); srv = Str(o.Table.Get("server"));
                var r = o.Table.Get("restart"); if (r.Type == DataType.Boolean) restart = r.Boolean;
            }
            else
            {
                chr = Str(o); srv = Str(Arg(a, 2));
                var r = Arg(a, 3); if (r.Type == DataType.Boolean) restart = r.Boolean;
            }
            return Pair(who is null ? OrderResult.No("bot manquant") : Order(c => _svc.Orders.SwitchCharacterAsync(who, chr, srv, restart, c), ct));
        });

        Verb(m, "waitReady", a =>
        {
            var key = Str(Arg(a, 0));
            double sec = Arg(a, 1).CastToNumber() ?? 300;
            int ms = (int)Math.Clamp(sec * 1000, 0, MaxWaitMs);
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (true)
            {
                if (Find(key) is { InGame: true, MapId: > 0 }) return DynValue.True;
                if (DateTime.UtcNow >= until) return DynValue.False;
                Pump(ct);
                Sleep(100, ct);
            }
        });

        Verb(m, "startScript", a =>
        {
            string? who = Str(Arg(a, 0)), code = Str(Arg(a, 1));
            return Pair(who is null || code is null ? OrderResult.No("bot ou script manquant") : Order(c => _svc.Orders.StartScriptAsync(who, code, c), ct));
        });
        Verb(m, "stopScript", a => Pair(Str(Arg(a, 0)) is { } w ? Order(c => _svc.Orders.StopScriptAsync(w, c), ct) : OrderResult.No("bot manquant")));
        Verb(m, "run", a =>
        {
            string? who = Str(Arg(a, 0)), code = Str(Arg(a, 1));
            return Pair(who is null || code is null ? OrderResult.No("bot ou code manquant") : Order(c => _svc.Orders.RunAsync(who, code, c), ct));
        });

        Verb(m, "send", a =>
        {
            if (!_svc.Perms.HasMulti) return DynValue.False;
            var b = Find(Str(Arg(a, 0))); var subject = Str(Arg(a, 1));
            if (b is null || b.Pid <= 0 || subject is null) return DynValue.False;
            return DynValue.NewBoolean(_svc.Bus.SendToBot(b.Pid, _name, subject, Data(Arg(a, 2))));
        });
        Verb(m, "broadcast", a =>
        {
            var subject = Str(Arg(a, 0));
            if (!_svc.Perms.HasMulti || subject is null) return DynValue.NewNumber(0);
            var pids = _svc.Bots.Snapshot().Where(b => b.Pid > 0 && b.ScriptRunning).Select(b => b.Pid).ToList();
            return DynValue.NewNumber(_svc.Bus.Broadcast(pids, _name, subject, Data(Arg(a, 1))));
        });
        Verb(m, "waitMessage", a =>
        {
            var subject = Str(Arg(a, 0));
            if (subject is null) return DynValue.Nil;
            int ms = (int)Math.Clamp(Arg(a, 1).CastToNumber() ?? 10000, 0, MaxWaitMs);
            var msg = _box.Wait(subject, TimeSpan.FromMilliseconds(ms), ct, other => OnMessage(s, other));
            if (msg is null) return DynValue.Nil;
            return DynValue.NewTuple(msg.SenderBot is null ? DynValue.Nil : BotTable(s, msg.SenderBot), LuaData.FromObject(s, msg.Data));
        });

        Verb(m, "stopAllScripts", _ => DynValue.NewNumber(_svc.Perms.HasMulti ? Await(_svc.Orders.StopAllScriptsAsync(ct), ct, OrderTimeoutMs) : 0));
        Verb(m, "disconnectAll", _ => DynValue.NewNumber(_svc.Perms.HasMulti ? Await(_svc.Orders.DisconnectAllAsync(ct), ct, OrderTimeoutMs) : 0));
    }

    private static object? Data(DynValue v)
    {
        try { return LuaData.ToObject(v); }
        catch (ArgumentException e) { throw new ScriptRuntimeException(e.Message); }
    }

    // ---------------------------------------------------------------- accounts:
    private void BindAccounts(Script s, Module m, CancellationToken ct)
    {
        Verb(m, "list", _ => ToArrayTable(s, _svc.Accounts.List(), AccountTable));
        Verb(m, "get", a => _svc.Accounts.Get(Str(Arg(a, 0)) ?? "") is { } acc ? AccountTable(s, acc) : DynValue.Nil);

        Verb(m, "add", a =>
        {
            if (!_svc.Perms.HasMulti) return DynValue.NewTuple(DynValue.Nil, DynValue.NewString(NeedMulti));
            var login = Str(Arg(a, 0));
            if (login is null) return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("identifiant manquant"));
            var pw = Arg(a, 1); var opt = Arg(a, 2);
            if (pw.Type == DataType.Table && opt.Type == DataType.Nil) { opt = pw; pw = DynValue.Nil; }
            string? label = opt.Type == DataType.Table ? Str(opt.Table.Get("label")) : null;
            string? proxy = opt.Type == DataType.Table ? Str(opt.Table.Get("proxy")) : null;
            try
            {
                var (acc, reason) = Await(_svc.Accounts.AddAsync(login, Str(pw), label, proxy, ct), ct, MaxWaitMs);
                return acc is null ? DynValue.NewTuple(DynValue.Nil, DynValue.NewString(reason)) : AccountTable(s, acc);
            }
            catch (TimeoutException) { return DynValue.NewTuple(DynValue.Nil, DynValue.NewString("fenêtre de connexion non fermée (30 min)")); }
        });

        Verb(m, "importFile", a =>
        {
            if (!_svc.Perms.HasMulti) return DynValue.NewTuple(DynValue.NewTable(new Table(s)), DynValue.NewNumber(0));
            var (accs, fails) = Await(_svc.Accounts.ImportFileAsync(Str(Arg(a, 0)) ?? "", Str(Arg(a, 1)), ct), ct, MaxWaitMs);
            return DynValue.NewTuple(ToArrayTable(s, accs, AccountTable), DynValue.NewNumber(fails));
        });

        Verb(m, "setProxy", a => Setting(a, (acc, v) => _svc.Accounts.SetProxy(acc, Str(v))));
        Verb(m, "setGameProxy", a => Setting(a, (acc, v) => _svc.Accounts.SetGameProxy(acc, Str(v))));
        Verb(m, "setMode", a => Setting(a, (acc, v) => Str(v)?.ToLowerInvariant() is "socket" or "mitm"
            ? _svc.Accounts.SetMode(acc, Str(v)!.ToLowerInvariant()) : OrderResult.No("mode inconnu (socket | mitm)")));
        Verb(m, "setAutoScript", a => Setting(a, (acc, v) => v.Type == DataType.Boolean ? _svc.Accounts.SetAutoScript(acc, v.Boolean) : OrderResult.No("booléen attendu")));
        Verb(m, "setLabel", a => Setting(a, (acc, v) => Str(v) is { Length: > 0 } l ? _svc.Accounts.SetLabel(acc, l) : OrderResult.No("libellé vide")));

        Verb(m, "createCharacter", a =>
        {
            if (!_svc.Perms.HasMulti) return Pair(false, NeedMulti);
            var acc = Str(Arg(a, 0)); var o = Arg(a, 1);
            if (acc is null || o.Type != DataType.Table) return Pair(false, "compte ou paramètres manquants");
            return Pair(_svc.Accounts.CreateCharacter(acc, ParseSpec(o.Table)));
        });
        // accounts:deleteCharacter(compte, { server = "Brial", name = "Perso", answer = "réponse secrète" }) — compte avec code secret uniquement.
        Verb(m, "deleteCharacter", a =>
        {
            if (!_svc.Perms.HasMulti) return Pair(false, NeedMulti);
            var acc = Str(Arg(a, 0)); var o = Arg(a, 1);
            if (acc is null || o.Type != DataType.Table) return Pair(false, "compte ou paramètres manquants");
            return Pair(_svc.Accounts.DeleteCharacter(acc, Str(o.Table.Get("server")), Str(o.Table.Get("name")), Str(o.Table.Get("answer"))));
        });
        Verb(m, "cancelCharacter", a => Pair(!_svc.Perms.HasMulti ? OrderResult.No(NeedMulti) : _svc.Accounts.CancelCharacter(Str(Arg(a, 0)) ?? "")));
    }

    private DynValue Setting(List<DynValue> a, Func<string, DynValue, OrderResult> apply)
    {
        if (!_svc.Perms.HasMulti) return Pair(false, NeedMulti);
        var acc = Str(Arg(a, 0));
        return acc is null ? Pair(false, "compte manquant") : Pair(apply(acc, Arg(a, 1)));
    }

    /// <summary>Couleurs : nombre 0xRRGGBB, « #RRGGBB » ou « default », par clé (skin, hair, outfit1..3) ou liste 1..5.</summary>
    public static CharacterSpec ParseSpec(Table t)
    {
        IReadOnlyDictionary<string, string>? colors = null;
        if (t.Get("colors") is { Type: DataType.Table } c)
        {
            var d = new Dictionary<string, string>();
            string[] order = { "skin", "hair", "outfit1", "outfit2", "outfit3" };
            foreach (var p in c.Table.Pairs)
            {
                string key = p.Key.Type == DataType.Number ? (p.Key.Number is >= 1 and <= 5 ? order[(int)p.Key.Number - 1] : "") : p.Key.CastToString();
                if (key.Length > 0) d[key.ToLowerInvariant()] = p.Value.Type == DataType.Number ? "#" + ((int)p.Value.Number & 0xFFFFFF).ToString("X6") : p.Value.CastToString();
            }
            colors = d;
        }
        int? Int(string k) => t.Get(k) is { Type: DataType.Number } v ? (int)v.Number : null;
        return new CharacterSpec(Str(t.Get("server")), Str(t.Get("breed")), Str(t.Get("sex")), Str(t.Get("name")), Int("head"), Int("body"), colors,
                                 !(t.Get("leaveTutorial") is { Type: DataType.Boolean } lt) || lt.Boolean);
    }

    // ---------------------------------------------------------------- proxies:
    private void BindProxies(Script s, Module m, CancellationToken ct)
    {
        Verb(m, "list", _ => ToArrayTable(s, _svc.Proxies.List(), ProxyTable));
        Verb(m, "add", a =>
        {
            if (!_svc.Perms.HasMulti) return DynValue.NewTuple(DynValue.Nil, DynValue.NewString(NeedMulti));
            var (p, reason) = _svc.Proxies.Add(Str(Arg(a, 0)) ?? "", Str(Arg(a, 1)));
            return p is null ? DynValue.NewTuple(DynValue.Nil, DynValue.NewString(reason)) : ProxyTable(s, p);
        });
        Verb(m, "importFile", a => _svc.Perms.HasMulti ? ToArrayTable(s, _svc.Proxies.ImportFile(Str(Arg(a, 0)) ?? ""), ProxyTable) : DynValue.NewTable(new Table(s)));
        Verb(m, "test", a =>
        {
            var p = Str(Arg(a, 0));
            if (p is null) return Pair(false, "proxy manquant");
            var r = Await(_svc.Proxies.TestAsync(p, ct), ct, 60000);
            return r.Ok
                ? DynValue.NewTuple(DynValue.True, DynValue.NewString(r.Ip), r.Ms is int ms ? DynValue.NewNumber(ms) : DynValue.Nil, r.MsProxy is int mp ? DynValue.NewNumber(mp) : DynValue.Nil)
                : DynValue.NewTuple(DynValue.False, DynValue.NewString(r.Reason));
        });
    }

    // ---------------------------------------------------------------- givreController: (chemins superviseur)
    private void BindController(Script s, Module m, CancellationToken ct)
    {
        Verb(m, "registerAnAccount", a =>
        {
            if (!_svc.Perms.HasMulti) return DynValue.False;
            var login = Str(Arg(a, 0)); if (login is null) return DynValue.False;
            if (_svc.Accounts.Get(login) is not null) return DynValue.True;   // déjà enregistré
            var (acc, _) = Await(_svc.Accounts.AddAsync(login, Str(Arg(a, 1)), Str(Arg(a, 2)), null, ct), ct, MaxWaitMs);
            return DynValue.NewBoolean(acc is not null);
        });
        Verb(m, "assignProxyToAnAccount", a =>
        {
            if (!_svc.Perms.HasMulti) return DynValue.False;
            string? acc = Str(Arg(a, 0)), ip = Str(Arg(a, 1)), port = Str(Arg(a, 2)), user = Str(Arg(a, 3)), pass = Str(Arg(a, 4));
            if (acc is null || ip is null || port is null) return DynValue.False;
            var spec = user is null ? $"{ip}:{port}" : $"{ip}:{port}:{user}:{pass}";
            var (p, _) = _svc.Proxies.Add(spec, null);
            return DynValue.NewBoolean(p is not null && _svc.Accounts.SetProxy(acc, p.Id).Ok);
        });
        Verb(m, "createCharacter", a =>
        {
            if (!_svc.Perms.HasMulti) return DynValue.False;
            string? acc = Str(Arg(a, 0));
            if (acc is null) return DynValue.False;
            var colors = new Dictionary<string, string>();
            string[] order = { "skin", "hair", "outfit1", "outfit2", "outfit3" };
            for (int i = 0; i < 5; i++) if (Arg(a, 5 + i) is { Type: not DataType.Nil } v) colors[order[i]] = v.Type == DataType.Number ? "#" + ((int)v.Number & 0xFFFFFF).ToString("X6") : v.CastToString();
            return DynValue.NewBoolean(_svc.Accounts.CreateCharacter(acc, new CharacterSpec(Str(Arg(a, 4)), Str(Arg(a, 2)), Str(Arg(a, 3)), Str(Arg(a, 1)), null, null, colors, true)).Ok);
        });
    }
}
