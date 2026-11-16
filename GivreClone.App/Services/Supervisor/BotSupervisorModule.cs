using System;
using System.Collections.Generic;
using System.Threading;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services.Supervisor;

/// <summary>
/// Côté BOT du superviseur (portail) : module <c>supervisor:</c> (send / isRunning / wait) et callback
/// <c>onSupervisorMessage(sujet, données, superviseur)</c>, servi aux points sûrs du script (<see cref="Pump"/>) sur le thread du script.
/// Autonome (sans hôte complet) pour être testable ; l'hôte Lua du bot lui fournit ses propres conversions d'arrêt et son journal.
/// </summary>
public sealed class BotSupervisorModule
{
    public const int WaitMaxMs = 120_000;   // 2 min au plus, sous le seuil anti-blocage (portail)

    private readonly int _pid;
    private readonly Script _s;
    private readonly CancellationToken _ct;
    private readonly SupervisorBus _bus;
    private readonly Func<int, BotSnapshot?> _snapshotFor;
    private readonly Func<Exception, bool> _isStop;
    private readonly Func<Exception> _makeStop;
    private readonly Action<string> _warn;

    public BotSupervisorModule(int pid, Script script, CancellationToken ct, SupervisorBus bus, Func<int, BotSnapshot?> snapshotFor,
                               Func<Exception, bool> isStop, Func<Exception> makeStop, Action<string> warn)
        => (_pid, _s, _ct, _bus, _snapshotFor, _isStop, _makeStop, _warn) = (pid, script, ct, bus, snapshotFor, isStop, makeStop, warn);

    public void Bind()
    {
        var t = new Table(_s);
        var byNorm = new Dictionary<string, DynValue>();
        void Verb(string name, Func<List<DynValue>, DynValue> body)
        {
            var cb = DynValue.NewCallback((_, a) =>
            {
                var l = new List<DynValue>();
                for (int i = 0; i < a.Count; i++) l.Add(a[i]);
                if (l.Count > 0 && l[0].Type == DataType.Table && ReferenceEquals(l[0].Table, t)) l.RemoveAt(0);   // supervisor:verbe(...)
                return body(l);
            });
            t[name] = cb;
            byNorm[name.Replace("_", "").ToLowerInvariant()] = cb;
        }
        static DynValue Arg(List<DynValue> a, int i) => i < a.Count ? a[i] : DynValue.Nil;

        Verb("send", a =>
        {
            if (Arg(a, 0).Type != DataType.String) return DynValue.NewNumber(0);
            object? data;
            try { data = LuaData.ToObject(Arg(a, 1)); } catch (ArgumentException e) { throw new ScriptRuntimeException(e.Message); }
            var me = _snapshotFor(_pid) ?? new BotSnapshot("", "", _pid, "", BotStatus.InGame);
            return DynValue.NewNumber(_bus.SendToSupervisors(me, Arg(a, 0).String, data));
        });
        Verb("isRunning", _ => DynValue.NewBoolean(_bus.AnySupervisorRunning));
        Verb("wait", a =>
        {
            if (Arg(a, 0).Type != DataType.String) return DynValue.Nil;
            int ms = (int)Math.Clamp(Arg(a, 1).CastToNumber() ?? 10000, 0, WaitMaxMs);
            BusMessage? m;
            try { m = _bus.BotBox(_pid).Wait(Arg(a, 0).String, TimeSpan.FromMilliseconds(ms), _ct, Serve); }
            catch (OperationCanceledException) { throw _makeStop(); }
            return m is null ? DynValue.Nil : DynValue.NewTuple(LuaData.FromObject(_s, m.Data), DynValue.NewString(m.Sender));
        });

        var meta = new Table(_s);
        meta["__index"] = DynValue.NewCallback((_, a) =>
            a.Count >= 2 && a[1].Type == DataType.String && byNorm.TryGetValue(a[1].String.Replace("_", "").ToLowerInvariant(), out var f) ? f : DynValue.Nil);
        t.MetaTable = meta;
        _s.Globals["supervisor"] = DynValue.NewTable(t);
    }

    /// <summary>Sert un message à onSupervisorMessage. False (message gardé pour supervisor:wait) si le script n'en définit pas.</summary>
    private bool Serve(BusMessage m)
    {
        var fn = _s.Globals.Get("onSupervisorMessage");
        if (fn.Type != DataType.Function) return false;
        try { _s.Call(fn, DynValue.NewString(m.Subject), LuaData.FromObject(_s, m.Data), DynValue.NewString(m.Sender)); }
        catch (Exception ex)
        {
            if (_ct.IsCancellationRequested || _isStop(ex)) throw _makeStop();
            _warn($"onSupervisorMessage : {(ex is ScriptRuntimeException sre ? sre.DecoratedMessage ?? sre.Message : ex.Message)}");
        }
        return true;
    }

    /// <summary>Vide la boîte du bot vers onSupervisorMessage (ou la garde, 32 au plus, pour supervisor:wait). Thread du script uniquement.</summary>
    public void Pump()
    {
        var box = _bus.BotBox(_pid);
        if (box.PendingCount == 0) return;
        box.Pump(Serve, budget: 16);
    }
}
