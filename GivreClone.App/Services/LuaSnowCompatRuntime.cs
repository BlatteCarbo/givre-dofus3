using System;
using System.Threading;
using System.Threading.Tasks;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// Socle testable sans client, transport ni compte. Les retours neutres restent réservés
// aux lectures effectivement branchées (état inconnu), jamais aux API non portées.
internal static class LuaSnowCompatRuntime
{
    internal static async Task RunAfterAsync(Task? previous, CancellationToken ct, Action body)
    {
        if (previous is not null)
        {
            try { await previous.ConfigureAwait(false); }
            catch { /* un échec précédent n'interdit pas la nouvelle demande */ }
        }
        if (!ct.IsCancellationRequested) body();
    }

    internal static DynValue Unsupported(string api, string reason = "API non implémentée dans GivreClone")
        => DynValue.NewCallback((_, _) => throw new ScriptRuntimeException($"{api} : {reason}. Aucune action effectuée."));

    internal static int DelayMilliseconds(DynValue value)
    {
        double? ms = value.CastToNumber();
        if (ms is not { } n || !double.IsFinite(n) || n < 0 || n > int.MaxValue)
            throw new ScriptRuntimeException("delay(ms) : délai attendu entre 0 et 2147483647 millisecondes");
        return (int)n;
    }

    internal static void ConfigureCoroutines(Script script, CancellationToken ct, Action? checkpoint = null)
    {
        var module = script.Globals.Get("coroutine").Table;
        DynValue Resume(Coroutine co, DynValue[] args, bool wrap)
        {
            void Check() { ct.ThrowIfCancellationRequested(); checkpoint?.Invoke(); }
            Check();
            if (co.Type == Coroutine.CoroutineType.Coroutine) co.AutoYieldCounter = 1000;
            DynValue result;
            try
            {
                result = co.Resume(args);
                while (co.State == CoroutineState.ForceSuspended)
                {
                    Check();
                    result = co.Resume();
                }
            }
            catch (ScriptRuntimeException ex) when (!wrap && !ct.IsCancellationRequested)
            {
                return DynValue.NewTuple(DynValue.False, DynValue.NewString(ex.DecoratedMessage ?? ex.Message));
            }
            Check();
            if (wrap) return result;
            var values = result.Type == DataType.Tuple ? result.Tuple : new[] { result };
            var tuple = new DynValue[values.Length + 1];
            tuple[0] = DynValue.True;
            Array.Copy(values, 0, tuple, 1, values.Length);
            return DynValue.NewTuple(tuple);
        }
        module["resume"] = DynValue.NewCallback((_, a) =>
        {
            if (a.Count == 0 || a[0].Type != DataType.Thread)
                throw new ScriptRuntimeException("coroutine.resume : coroutine attendue");
            var args = new DynValue[a.Count - 1];
            for (int i = 1; i < a.Count; i++) args[i - 1] = a[i];
            return Resume(a[0].Coroutine, args, false);
        });
        module["wrap"] = DynValue.NewCallback((_, a) =>
        {
            if (a.Count == 0 || a[0].Type is not (DataType.Function or DataType.ClrFunction))
                throw new ScriptRuntimeException("coroutine.wrap : fonction Lua attendue");
            var co = script.CreateCoroutine(a[0]).Coroutine;
            return DynValue.NewCallback((_, call) =>
            {
                var args = new DynValue[call.Count];
                for (int i = 0; i < call.Count; i++) args[i] = call[i];
                return Resume(co, args, true);
            });
        });
    }

    // Une boucle Lua pure doit être arrêtable, même si elle ne rappelle aucun verbe CLR.
    // AutoYieldCounter ne s'applique qu'à cette coroutine ; les coroutines du script
    // conservent leur comportement. Le jeton est contrôlé aussi AVANT et APRÈS chaque appel.
    internal static DynValue Call(Script script, DynValue fn, CancellationToken ct,
                                  Action? checkpoint = null, params DynValue[] args)
    {
        void Check() { ct.ThrowIfCancellationRequested(); checkpoint?.Invoke(); }
        Check();
        // Une coroutine CLR n'a pas de processeur Lua/AutoYieldCounter. Ses appels
        // Lua imbriqués (placement tiers) repassent eux-mêmes par ce point d'entrée.
        if (fn.Type == DataType.ClrFunction)
        {
            var nativeResult = script.Call(fn, args);
            Check();
            return nativeResult;
        }
        var co = script.CreateCoroutine(fn).Coroutine;
        co.AutoYieldCounter = 1000;
        var result = co.Resume(args);
        while (co.State != CoroutineState.Dead)
        {
            Check();
            result = co.Resume();
        }
        Check();
        return result;
    }
}

