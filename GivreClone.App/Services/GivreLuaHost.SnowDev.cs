using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ==================================================================================================================
// Compatibilité tiers — table developer: (developer.vb). Attentes BOOLÉENNES (suspendScriptUntil,
// suspendScriptUntilMultiplePackets), marque d'écoute (listenPackets), historique (historicalMessage : paquets de la
// connexion jusqu'à la 1re carte ; lastMessage : dernière instance reçue), typeoff, requêtes HTTP de compat.
// Noms de message : nom Dofus 3 du catalogue, nom Dofus 2 (« …Message ») ou identifiant PUBLIC Dofus 2 (ResolveCode).
// Refusés par conception : sendMessage / createMessage émis tels quels et cancelMessageToClient (un paquet forgé librement
// contournerait les droits vérifiés au point d'action et les gardes du moteur), dofusRequest (API Ankama).
// Thread du script uniquement.
// ==================================================================================================================
public sealed partial class GivreLuaHost
{
    private const int SuspendMaxMs = 30 * 60 * 1000;   // Un bot tiers acceptait des délais « infinis »: borné à 30 min


    private void BindSnowDeveloper(Script s, ScriptRuntime rt, Table dev, CancellationToken ct)
    {
        string NameArg(CallbackArguments a, int i)
        {
            var v = At(a, dev, i);
            return v.Type == DataType.Number ? ((long)v.Number).ToString(CultureInfo.InvariantCulture) : (v.CastToString() ?? "");
        }

        // suspendScriptUntil(nom|id, délaiMs [, afficherDélai=true] [, bypass | texteDélai] [, pas | écoute=true] [, écoute=true]) -> bool
        // Un bot tiers (developer.vb / MoveLua.SuspendScriptUntilByName): un 4e argument TEXTE (« failMessageName ») n'est PAS un
        // paquet d'échec mais la ligne affichée à l'expiration à la place du « TimeOut » par défaut. Un nom
        // inconnu (ou refusé) ne rend pas la main tout de suite: Un bot tiers attend tout le délai (boucle repeat…until sans pause).
        Fill(dev, "suspendScriptUntil", DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            string name = NameArg(a, 0);
            int timeout = (int)Math.Clamp(At(a, dev, 1).CastToNumber() ?? WaitForDefaultMs, WaitForMinMs, SuspendMaxMs);
            var show = At(a, dev, 2);
            bool showTimeout = show.IsNil() || show.CastToBool();
            var a3 = At(a, dev, 3);
            string? timeoutText = a3.Type == DataType.String && a3.String.Trim().Length > 0 ? a3.String.Trim() : null;
            var listenArg = a3.Type == DataType.String ? At(a, dev, 5) : At(a, dev, 4);
            bool listenNow = listenArg.Type != DataType.Boolean || listenArg.Boolean;
            if (IsNoOperation(name))
            {
                if (listenNow) ListenReset(rt);   // Un bot tiers: ListenPackets avant l'attente, même pour le « pas d'opération »
                InterruptibleDelay(200, ct, pump: true);
                return DynValue.True;
            }
            var codes = new HashSet<string>(StringComparer.Ordinal);
            if (ResolveCode(name) is { } code) codes.Add(code);
            else WarnUnknownMessage(name, once: true);
            var (p, _) = WaitCore(rt, codes, null, timeout, listenNow, null, ct);
            if (p is null && showTimeout)
            {
                if (timeoutText is not null) Info("hook", timeoutText);
                else Warn("hook", Loc.Fmt("L.Lua.SnowWaitTimeout", name, timeout / 1000.0));
            }
            return DynValue.NewBoolean(p is not null);
        }));

        // suspendScriptUntilMultiplePackets({noms}, délaiMs [, afficherDélai] [, bypass|pas] [, écoute=true]) -> nom reçu | "False"
        // Aucun nom résolu: attente de tout le délai puis "False", jamais un retour immédiat.
        DynValue multi = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            var list = At(a, dev, 0);
            int timeout = (int)Math.Clamp(At(a, dev, 1).CastToNumber() ?? WaitForDefaultMs, WaitForMinMs, SuspendMaxMs);
            var show = At(a, dev, 2);
            bool showTimeout = show.IsNil() || show.CastToBool();
            var listenArg = At(a, dev, 4);
            bool listenNow = listenArg.Type != DataType.Boolean || listenArg.Boolean;
            var byCode = new Dictionary<string, string>(StringComparer.Ordinal);
            var asked = new List<string>();
            if (list.Type == DataType.Table)
                foreach (var v in list.Table.Values)
                {
                    string n = v.Type == DataType.Number ? ((long)v.Number).ToString(CultureInfo.InvariantCulture) : (v.CastToString() ?? "");
                    if (n.Length == 0) continue;
                    asked.Add(n);
                    if (ResolveCode(n) is { } c) byCode.TryAdd(c, n);
                    else WarnUnknownMessage(n, once: true);
                }
            var (p, _) = WaitCore(rt, byCode.Keys.ToHashSet(StringComparer.Ordinal), null, timeout, listenNow, null, ct);
            if (p is null) { if (showTimeout) Warn("hook", Loc.Fmt("L.Lua.SnowWaitTimeout", string.Join("/", asked), timeout / 1000.0)); return DynValue.NewString("False"); }
            return DynValue.NewString(byCode.TryGetValue(p.Code, out var hit) ? hit : (p.Name ?? ""));
        });
        Fill(dev, "suspendScriptUntilMultiplePackets", multi);

        // waitForAny({noms} [, délaiMs] [, action]) -> nom, paquet | nil (forme GivreClone : renvoie aussi la table du paquet).
        DynValue waitAny = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            if (rt.InPump) return DynValue.Nil;
            var list = At(a, dev, 0);
            int timeout = WaitForDefaultMs; DynValue? action = null;
            for (int i = 1; i < 4; i++)
            {
                var v = At(a, dev, i);
                if (v.Type == DataType.Number) timeout = (int)Math.Clamp(v.Number, WaitForMinMs, WaitForMaxMs);
                else if (v.Type is DataType.Function or DataType.ClrFunction) action = v;
            }
            var byCode = new Dictionary<string, string>(StringComparer.Ordinal);
            if (list.Type == DataType.Table)
                foreach (var v in list.Table.Values)
                {
                    if (v.CastToString() is not { Length: > 0 } n) continue;
                    if (ResolveCode(n) is { } c) byCode.TryAdd(c, n);
                    else WarnUnknownMessage(n);   // refus sensible (nom ou alias) : déjà journalisé, pas de double avertissement
                }
            if (byCode.Count == 0) return DynValue.Nil;
            var (p, _) = WaitCore(rt, byCode.Keys.ToHashSet(StringComparer.Ordinal), null, timeout, null, action, ct);
            if (p is null) return DynValue.Nil;
            string asked = byCode.TryGetValue(p.Code, out var x) ? x : (p.Name ?? "");
            return DynValue.NewTuple(DynValue.NewString(asked), PacketToLua(rt.Script, p, asked, snow: true));
        });
        Fill(dev, "waitForAny", waitAny);
        s.Globals["waitForAny"] = waitAny;

        // listenPackets: marque d'écoute — seuls les paquets reçus APRÈS cet appel
        // comptent pour les suspendScriptUntil(…, écoute=false) suivants (envoyer, puis attendre sans rater une réponse rapide).
        // L'enregistrement continue ensuite jusqu'à la prochaine marque ; chaque paquet reconnu est consommé.
        Fill(dev, "listenPackets", DynValue.NewCallback((ctx, a) =>
        {
            ListenReset(rt);
            return DynValue.Nil;
        }));

        // historicalMessage(nom|id) / historicalMessageDofus3(nom) -> liste des paquets de ce type reçus depuis la connexion au
        // serveur de jeu jusqu'au 1er chargement de carte (le « snapshot » de connexion), du plus ancien au plus récent.
        DynValue history = DynValue.NewCallback((_, a) =>
        {
            string name = NameArg(a, 0);
            var t = new Table(s);
            if (ResolveCode(name) is not { } code || _launch.Catalog?.NameForCode(code) is not { } clear) return DynValue.NewTable(t);
            int i = 1;
            foreach (var p in _launch.LoginBurst(_pid, clear)) t.Set(i++, PacketToLua(s, p, clear, snow: true));
            return DynValue.NewTable(t);
        });
        Fill(dev, "historicalMessage", history);
        Fill(dev, "historicalMessageDofus3", history);
        // lastMessage(nom) -> dernière instance REÇUE (à tout moment), ou nil. Ex. lire la question d'un PNJ après talkNpc().
        // Messages porteurs de secrets (jetons…) : refusés par ResolveCode -> nil.
        DynValue last = DynValue.NewCallback((_, a) =>
        {
            string name = NameArg(a, 0);
            if (ResolveCode(name) is not { } code || _launch.Catalog?.NameForCode(code) is not { } clear) return DynValue.Nil;
            return _launch.LastReceived(_pid, clear) is { } p ? PacketToLua(s, p, clear, snow: true) : DynValue.Nil;
        });
        Fill(dev, "lastMessage", last);
        s.Globals["lastMessage"] = last;

        Fill(dev, "typeoff", DynValue.NewCallback((_, a) =>
        {
            var v = At(a, dev, 0);
            if (v.Type == DataType.Table && v.Table.RawGet("__name") is { Type: DataType.String } n && n.String.Length > 0) return n;
            return DynValue.NewString(v.Type.ToString().ToLowerInvariant());
        }));
        Fill(dev, "toggleMultiThreading", UnsupportedLua("developer:toggleMultiThreading", "Les hooks Lua tournent sur le seul thread du script"));
        if (s.Globals.Get("isScriptPlaying").Type == DataType.ClrFunction) Fill(dev, "isScriptPlaying", SnowFwd(s, dev, s.Globals.Get("isScriptPlaying")));
        Fill(dev, "hasScript", DynValue.NewCallback((_, a) => DynValue.NewBoolean(LastScriptText is not null)));
        Fill(dev, "startScript", DynValue.NewCallback((_, a) =>
            BotScripts.Get((int)_pid)?.Running == true ? DynValue.True : SwitchScript(null, ct)));
        Fill(dev, "getNameByIdentifier", DynValue.NewCallback((_, a) =>
            DynValue.NewString(LuaMessageAliases.D2Ids.TryGetValue((int)(At(a, dev, 0).CastToNumber() ?? 0), out var n) ? n : "")));
        // Identifiant PUBLIC Dofus 2 d'un nom Dofus 2 (jamais un code du protocole courant).
        Fill(dev, "getIdentifierByName", DynValue.NewCallback((_, a) =>
        {
            string n = At(a, dev, 0).CastToString() ?? "";
            foreach (var kv in LuaMessageAliases.D2Ids) if (string.Equals(kv.Value, n, StringComparison.OrdinalIgnoreCase)) return DynValue.NewString(kv.Key.ToString(CultureInfo.InvariantCulture));
            return DynValue.NewString("");
        }));
        // createMessage(nom|id) -> table { __name } (lecture/composition seulement : sendMessage est refusé).
        Fill(dev, "createMessage", DynValue.NewCallback((_, a) =>
        {
            string name = NameArg(a, 0);
            if (ResolveCode(name) is not { } code || _launch.Catalog?.NameForCode(code) is not { } clear) { WarnUnknownMessage(name); return DynValue.Nil; }
            var t = new Table(s);
            t["__name"] = DynValue.NewString(clear);
            return DynValue.NewTable(t);
        }));
        DynValue Refuse(string fn) => UnsupportedLua("developer:" + fn, "API refusée par le bac à sable");
        Fill(dev, "sendMessage", Refuse("sendMessage"));
        Fill(dev, "cancelMessageToClient", Refuse("cancelMessageToClient"));
        Fill(dev, "unCancelMessageToClient", UnsupportedLua("developer:unCancelMessageToClient"));
        Fill(dev, "dofusRequest", UnsupportedLua("developer:dofusRequest"));
        // getRequestWithDelay(url, délaiMs) -> corps, ou "Error: <raison>" (getRequest2 : lié dans BindSnow, après le Lot K).
        Fill(dev, "getRequestWithDelay", DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            string url = At(a, dev, 0).CastToString() ?? "";
            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            int timeout = Math.Clamp((int)(At(a, dev, 1).CastToNumber() ?? 10000), 1, 10000);
            requestCts.CancelAfter(timeout);
            ScriptHttpResult r;
            try { r = ScriptHttpService.Send("GET", url, null, new List<KeyValuePair<string, string>>(), requestCts.Token); }
            catch (OperationCanceledException) { ThrowIfStopping(ct); r = ScriptHttpResult.Fail("timeout"); }
            return DynValue.NewString(r.Error is null ? r.Body : "Error: " + r.Error);
        }));
    }

    private void WarnSnowRefused(string fn) { lock (_warnedSnow) if (!_warnedSnow.Add(fn)) return; Warn("script", Loc.Fmt("L.Lua.SnowRefused", fn)); }

    // Attente générique : le 1er paquet ENTRANT dont le code est dans `codes` (succès) ou vaut `failCode` (échec). `codes` vide :
    // on attend simplement tout le délai. Délai > 120 s: la progression est notée au watchdog
    // (attente voulue par le script, pas un blocage).
    // listen: true = marque posée d'abord (ListenPackets) puis paquet NOUVEAU; false =
    // tout paquet reçu depuis la dernière marque compte, même arrivé avant l'appel. Dans les deux cas la source est
    // l'enregistrement continu (Listened, rempli AVANT le filtre d'intérêt : aucune fenêtre d'armement où un paquet se perd) et
    // le paquet reconnu est CONSOMMÉ. listen=null (waitForAny) : waiter classique armé avant l'action, marque non touchée.
    private (DecodedPacket? P, bool Fail) WaitCore(ScriptRuntime rt, HashSet<string> codes, string? failCode, int timeoutMs,
                                                   bool? listen, DynValue? action, CancellationToken ct)
    {
        if (rt.InPump)
        {
            if (!_waitForInHookWarned) { _waitForInHookWarned = true; Warn("hook", Loc.T("L.Lua.WaitForInHook")); }
            return (null, false);
        }
        Waiter? w = null;
        if (listen is null)
        {
            w = new Waiter { Codes = codes, FailCode = failCode, MinSeq = Interlocked.Read(ref rt.Seq) };
            rt.Waiters.Add(w);
            rt.RebuildInterest();
        }
        else if (listen.Value) ListenReset(rt);
        try
        {
            if (action is not null) CallLua(rt.Script, action, ct);
            long end = Environment.TickCount64 + timeoutMs;
            long nextNote = Environment.TickCount64 + 30000;
            while (true)
            {
                ThrowIfStopping(ct);
                ThrowIfHookTurnGone();
                if (HooksActive) PumpFightHooks(ct);
                PumpHooks(ct);
                if (w?.Result is { } res) return (res.P, res.Fail);
                if (listen is not null && TakeListened(rt, codes, failCode) is { } hit) return hit;
                long now = Environment.TickCount64;
                long left = end - now;
                if (left <= 0) return (null, false);
                if (timeoutMs > WaitForMaxMs && now >= nextNote) { _launch.NoteProgress(_pid); nextNote = now + 30000; }
                if (ct.WaitHandle.WaitOne((int)Math.Min(50, left))) throw new ScriptStopException();
            }
        }
        finally
        {
            if (w is not null)
            {
                rt.Waiters.Remove(w);
                rt.RebuildInterest();
                if (!rt.HasWork && rt.Waiters.Count == 0) while (rt.Inbox.TryDequeue(out _)) Interlocked.Decrement(ref rt.InboxCount);
            }
        }
    }

    // Marque d'écoute: seuls les paquets reçus après comptent. La marque (numéro de
    // séquence) exclut un paquet en vol enregistré juste après le vidage.
    private static void ListenReset(ScriptRuntime rt)
    {
        long mark = Interlocked.Read(ref rt.Seq);
        rt.ListenMark = mark;
        // Retrait des SEULES entrées antérieures à la marque : un vidage aveugle perdait un paquet arrivé entre la lecture de
        // la marque et le vidage (fil réseau), qui compte pourtant (seq > marque).
        foreach (var kv in rt.Listened)
            if (kv.Value.Seq <= mark) rt.Listened.TryRemove(kv);
    }

    // Paquet enregistré depuis la marque dont le code est attendu (succès ou échec) : le plus ANCIEN gagne, et il est CONSOMMÉ
    // -> deux attentes successives ne rendent jamais le même paquet. Retrait conditionnel: un
    // paquet plus récent du même code arrivé entre-temps reste disponible.
    private static (DecodedPacket P, bool Fail)? TakeListened(ScriptRuntime rt, HashSet<string> codes, string? failCode)
    {
        long mark = rt.ListenMark;
        string? bestCode = null;
        (long Seq, DecodedPacket P) best = default;
        foreach (var c in codes) Consider(c);
        if (failCode is not null) Consider(failCode);
        if (bestCode is null) return null;
        rt.Listened.TryRemove(new KeyValuePair<string, (long Seq, DecodedPacket P)>(bestCode, best));
        return (best.P, !codes.Contains(bestCode));

        void Consider(string code)
        {
            if (rt.Listened.TryGetValue(code, out var e) && e.Seq > mark && (bestCode is null || e.Seq < best.Seq)) { bestCode = code; best = e; }
        }
    }
}
