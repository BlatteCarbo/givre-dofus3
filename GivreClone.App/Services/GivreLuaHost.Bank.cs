using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Engine.LuaTooling;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ===================== Lot D : banque / échange côté script (ouvrir, retirer, déposer sauf, kamas, échange bloquant) =====================
public sealed partial class GivreLuaHost
{
    // ============ Lot D : banque / échange côté script (thread du script uniquement ; moteur = LaunchService) ============
    private Table? _bankModule;   // table bank: (restaurée si le script déclare function bank())
    private bool _maintDone;      // maintenance pods (OPEN_BAGS/AUTO_DELETE) déjà jouée pour cet épisode « pods pleins »
    private long _maintAt;        // TickCount64 de la dernière maintenance (rejouée au plus toutes les 20 s)

    /// <summary>Un script qui déclare <c>function bank()</c> ÉCRASE le global bank (module) pendant DoString -> bank:open()
    /// dans move()/bank() plantait (« attempt to index a function »). On remet la TABLE en global et on lui donne un
    /// métaméthode __call = la fonction du script : bank() reste appelable (reçoit la table en 1er argument, ignoré).
    /// Appelée JUSTE APRÈS DoString, avant messagesRegistering() (RunLoop et RunOnce) : tout code exécuté ensuite voit la table.
    /// Renvoie la fonction bank() du script (celle que la boucle appelle), ou Nil si bank n'était pas une fonction.</summary>
    private DynValue RestoreBankModule(Script s)
    {
        var g = s.Globals.Get("bank");
        if (g.Type != DataType.Function || _bankModule is null) return DynValue.Nil;
        var mt = _bankModule.MetaTable ?? new Table(s);   // garde le __index de repli de noms
        mt["__call"] = g;
        _bankModule.MetaTable = mt;
        s.Globals["bank"] = DynValue.NewTable(_bankModule);
        return g;
    }

    // Ensemble de GID passé en table {gid,...} ET/OU en varargs de nombres (ou chaînes numériques). `self` = la table du
    // module (appel colon) : ignorée par RÉFÉRENCE (une table de gids passée en appel point n'est pas prise pour self).
    private static HashSet<long> GidSetArgs(CallbackArguments a, Table self)
    {
        var set = new HashSet<long>();
        static void Add(HashSet<long> set, DynValue v)
        {
            if (v.Type == DataType.Number) { if (v.Number > 0) set.Add((long)v.Number); }
            else if (v.Type == DataType.String && double.TryParse(v.String, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && d > 0) set.Add((long)d);
        }
        int start = a.Count > 0 && a[0].Type == DataType.Table && ReferenceEquals(a[0].Table, self) ? 1 : 0;
        for (int i = start; i < a.Count; i++)
        {
            var v = a[i];
            if (v.Type == DataType.Table) { foreach (var x in v.Table.Values) Add(set, x); }
            else Add(set, v);
        }
        return set;
    }

    private static DynValue BuildNumsL(Script s, IEnumerable<long> xs)
    {
        var t = new Table(s); int i = 1;
        foreach (var x in xs) t.Set(i++, DynValue.NewNumber(x));
        return DynValue.NewTable(t);
    }

    private bool BankOpenLua(long npcMapId, CancellationToken ct)
    {
        bool ok = _launch.BankOpenAsync(_pid, npcMapId, true, ct).GetAwaiter().GetResult();
        if (ok) Ok("banque", Loc.Fmt("L.Lua.BankOpened", _launch.Bank.StackCount(_pid), System.Math.Max(0, _launch.BankKamas(_pid))));
        else Warn("banque", Loc.T("L.Lua.BankOpenFailed"));
        return ok;
    }

    private bool BankCloseLua(CancellationToken ct)
    {
        bool ok = _launch.BankCloseAsync(_pid, ct).GetAwaiter().GetResult();
        Info("banque", Loc.T("L.Lua.BankClosed"));
        return ok;
    }

    private bool BankWithdrawLua(long gid, int qty, string verb, CancellationToken ct)
    {
        if (gid <= 0) return false;
        if (!_launch.StorageOpen(_pid)) { Warn("banque", Loc.Fmt("L.Lua.BankNotOpen", verb)); return false; }
        string name = _launch.ItemDisplayName(gid);
        int have = _launch.Bank.CountByGid(_pid, gid);
        if (have <= 0) { Warn("banque", Loc.Fmt("L.Lua.BankItemMissing", name, gid)); return false; }
        int wanted = qty > 0 ? qty : have;
        int n = _launch.BankWithdrawAsync(_pid, gid, wanted, ct).GetAwaiter().GetResult();
        if (n >= wanted) { Ok("banque", Loc.Fmt("L.Lua.BankWithdrawn", n, name)); return true; }
        Warn("banque", Loc.Fmt("L.Lua.BankWithdrawPartial", name, n, wanted));
        return false;
    }

    private bool BankPutAllExceptLua(HashSet<long> except, string verb, CancellationToken ct)
    {
        if (!_launch.StorageOpen(_pid)) { Warn("banque", Loc.Fmt("L.Lua.BankNotOpen", verb)); return false; }
        var (ok, stacks) = _launch.BankDepositAllExceptAsync(_pid, except, ct).GetAwaiter().GetResult();
        if (ok) Ok("banque", Loc.Fmt("L.Lua.BankDepositedExcept", stacks, except.Count));
        return ok;
    }

    // getAllItemsExcept : un retrait (ack par stack) par GID distinct du coffre hors liste. Variante un-paquet = DEFERRED
    // (ExchangeObjectTransferList*ToInventoryRequest : champs obfusqués, capture requise).
    private bool BankWithdrawAllExceptLua(HashSet<long> except, CancellationToken ct)
    {
        if (!_launch.StorageOpen(_pid)) { Warn("banque", Loc.Fmt("L.Lua.BankNotOpen", "exchange:getAllItemsExcept")); return false; }
        var gids = _launch.Bank.Items(_pid).Select(i => i.Gid).Where(g => !except.Contains(g)).Distinct().ToList();
        bool all = true;
        for (int i = 0; i < gids.Count; i++)
        {
            ThrowIfStopping(ct);
            long gid = gids[i];
            int have = _launch.Bank.CountByGid(_pid, gid);
            if (have <= 0) continue;
            int n = _launch.BankWithdrawAsync(_pid, gid, 0, ct).GetAwaiter().GetResult();
            if (n < have) all = false;
            if (n > 0) Ok("banque", Loc.Fmt("L.Lua.BankWithdrawn", n, _launch.ItemDisplayName(gid)));
            if (i + 1 < gids.Count) InterruptibleDelay(300, ct);
        }
        return all;
    }

    private bool BankPutKamasLua(long n, string verb, CancellationToken ct)
    {
        if (!_launch.StorageOpen(_pid)) { Warn("banque", Loc.Fmt("L.Lua.BankNotOpen", verb)); return false; }
        long have = _launch.Kamas(_pid);
        if (n <= 0) { if (have < 0) return false; n = have; }   // tout (solde inconnu -> rien)
        else if (have >= 0) n = System.Math.Min(n, have);
        if (n == 0) return true;
        bool ok = _launch.BankMoveKamasAsync(_pid, n, ct).GetAwaiter().GetResult();
        if (ok) Ok("banque", Loc.Fmt("L.Lua.BankKamasDeposited", n));
        else Warn("banque", Loc.Fmt("L.Lua.BankKamasUnconfirmed", n));
        return ok;
    }

    private bool BankWithdrawKamasLua(long n, string verb, CancellationToken ct)
    {
        if (!_launch.StorageOpen(_pid)) { Warn("banque", Loc.Fmt("L.Lua.BankNotOpen", verb)); return false; }
        long have = _launch.BankKamas(_pid);
        if (n <= 0) { if (have < 0) return false; n = have; }
        else if (have >= 0) n = System.Math.Min(n, have);
        if (n == 0) return true;
        bool ok = _launch.BankMoveKamasAsync(_pid, -n, ct).GetAwaiter().GetResult();
        if (ok) Ok("banque", Loc.Fmt("L.Lua.BankKamasWithdrawn", n));
        else Warn("banque", Loc.Fmt("L.Lua.BankKamasUnconfirmed", n));
        return ok;
    }

    private bool TradeWithPlayerBlocking(long actorId, string name, int timeoutSec, CancellationToken ct)
    {
        // true = ouvert, false = refusé/erreur, null = pas de réponse dans le délai (demande annulée). Stop() interrompt (ct).
        bool? r = _launch.TradeWithPlayerAsync(_pid, actorId, timeoutSec * 1000, ct).GetAwaiter().GetResult();
        ThrowIfStopping(ct);
        if (r == true) { Ok("échange", Loc.Fmt("L.Lua.TradeStarted", name)); return true; }
        if (r == false) Warn("échange", Loc.Fmt("L.Lua.TradeNotStarted", name));
        else Warn("échange", Loc.Fmt("L.Lua.TradeStartTimeout", name, timeoutSec));
        return false;
    }

    private bool TradeReadyBlocking(int timeoutSec, CancellationToken ct)
    {
        // Pas d'échange joueur ouvert : false immédiat (le moteur n'injecte rien), message dédié au lieu de « annulé ».
        if (!_launch.TradeOpen(_pid)) { Warn("échange", Loc.T("L.Lua.TradeNotOpen")); return false; }
        bool? r = _launch.TradeReadyAndWaitAsync(_pid, timeoutSec * 1000, ct).GetAwaiter().GetResult();
        ThrowIfStopping(ct);
        if (r == true) { Ok("échange", Loc.T("L.Lua.TradeDone")); return true; }
        if (r == false) Warn("échange", Loc.T("L.Lua.TradeCancelled"));
        else Warn("échange", Loc.Fmt("L.Lua.TradeReadyTimeout", timeoutSec));
        return false;
    }

    // Nom d'un personnage par characterId : acteur visible sur la carte, sinon bot de l'équipe, sinon "".
    private string ResolveCharacterName(long charId)
    {
        if (charId == 0) return "";
        var actor = _world.Get(_pid).Actors.FirstOrDefault(x => x.ActorId == charId);
        if (actor?.Name is { Length: > 0 } n) return n;
        foreach (var m in _launch.TeamMembers(_pid))
            if (_launch.CharacterId(m.Pid) == charId) return m.Name ?? "";
        return "";
    }
}
