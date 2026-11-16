using System;
using System.Linq;
using System.Threading;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ==================================================================================================================
// Compatibilité tiers — économie: inventory: (inventory.vb), exchange: (Exchangeplug.vb), sale: (Sale.vb), craft: (craft.vb),
// merchant: (merchant.vb). Gates au POINT D'ACTION dans le moteur (Perm.Gather : sac/banque/échange/craft ; Perm.Hdv : HDV).
// Un bot tiers attendait l'accusé serveur après chaque action d'échange: putAllItems / getAllItems / putItem attendent ici aussi
// la mise à jour avant de rendre la main, pour qu'un podsP() lu juste après soit à jour. Accusé ADAPTÉ au contexte : échange
// joueur = Exchange* (l'inventaire ne bouge qu'à la conclusion) ; coffre = sac réellement diminué (dépôt par GID, une requête
// par pile) ou rafale d'événements sac/coffre retombée (transfert en bloc). Noms = catalogue (data/matching_current.json).
// ==================================================================================================================
public sealed partial class GivreLuaHost
{
    private const int ExchangeAckMs = 3000, TradeAckMs = 1500, StorageQuietMs = 300;
    // Échange JOUEUR : le serveur accuse la pose par ExchangeObjects* (pas d'événement d'inventaire avant la conclusion).
    private static readonly string[] TradeAckEvents = { "ExchangeObjectsAddedEvent", "ExchangeObjectsModifiedEvent", "ExchangeErrorEvent" };
    // Transfert sac <-> coffre : événements du sac (unitaires et par lot) et du coffre.
    private static readonly string[] StorageAckEvents =
    {
        "InventoryWeightEvent", "ObjectQuantityEvent", "ObjectDeletedEvent", "ObjectAddedEvent", "ObjectsQuantityEvent", "ObjectsDeletedEvent",
        "ObjectsAddedEvent", "StorageObjectUpdateEvent", "StorageObjectsUpdateEvent", "StorageObjectRemovedEvent", "StorageObjectsRemovedEvent",
    };

    private void BindSnowEconomy(Script s, CancellationToken ct)
    {
        BindSnowInventory(s, ct);
        BindSnowExchange(s, ct);
        BindSnowSale(s, ct);
        BindSnowCraft(s, ct);
        BindSnowMerchant(s);
    }

    // Diagnostic explicite pour les modules tiers conservés par compatibilité.
    // Leurs anciennes sentinelles (false / -1 / nil / {}) ne permettent pas de distinguer « absent » d'« inconnu ».
    private static Table SnowStatusTable(Script s, bool available, string reason)
    {
        var t = new Table(s);
        t["available"] = DynValue.NewBoolean(available);
        t["reason"] = DynValue.NewString(reason);
        return t;
    }

    // Envoie puis attend un accusé positif ; un envoi seul, un délai ou ExchangeErrorEvent ne confirme rien.
    private bool SendAndAck(Func<bool> send, string[] acks, int timeoutMs, CancellationToken ct)
    {
        ThrowIfStopping(ct);
        bool sent = false;
        var ack = _launch.SendAndWaitForCodeAsync(_pid, () => sent = send(), acks, timeoutMs, ct).GetAwaiter().GetResult();
        bool ok = sent && ack is not null && ack.Name != "ExchangeErrorEvent";
        if (!ok) Warn("échange", "exchange:putItem : refus ou accusé serveur absent ; opération non confirmée.");
        return ok;
    }

    // Transfert sac <-> coffre EN BLOC (putAllItems / getAllItems) : 1er accusé (≤ 3 s), puis la rafale (un paquet par pile ou
    // par lot) jusqu'à 300 ms de calme, bornée à 3 s de plus. Rendre la main au 1er accusé laissait un podsP() périmé.
    private bool SendAndSettleStorage(Func<bool> send, CancellationToken ct)
    {
        ThrowIfStopping(ct);
        bool sent = false;
        var first = _launch.SendAndWaitForCodeAsync(_pid, () => sent = send(), StorageAckEvents, ExchangeAckMs, ct).GetAwaiter().GetResult();
        if (!sent || first is null)
        {
            Warn("banque", "Transfert de coffre : accusé serveur absent ; opération non confirmée.");
            return false;
        }
        long end = Environment.TickCount64 + ExchangeAckMs;
        while (true)
        {
            ThrowIfStopping(ct);
            int quiet = (int)Math.Min(StorageQuietMs, end - Environment.TickCount64);
            if (quiet <= 0 || _launch.SendAndWaitForCodeAsync(_pid, () => true, StorageAckEvents, quiet, ct).GetAwaiter().GetResult() is null) break;
        }
        return true;
    }

    // Quantité d'un GID DANS LE SAC (piles non équipées) : la seule que BankDepositItem déplace.
    private int BagQty(long gid) => _launch.Inventory.Items(_pid).Where(i => i.Gid == gid && i.Position == 63).Sum(i => i.Qty);

    // ------------------------------------------------------------------ inventory:
    private void BindSnowInventory(Script s, CancellationToken ct)
    {
        var inv = GetOrCreateTable(s, "inventory");
        long L(CallbackArguments a, int i) => (long)(At(a, inv, i).CastToNumber() ?? 0);
        int N(CallbackArguments a, int i) => (int)(At(a, inv, i).CastToNumber() ?? 0);

        Fill(inv, "pods", DynValue.NewCallback((_, a) => DynValue.NewNumber(_world.Get(_pid).Pods.Cur)));
        Fill(inv, "podsMax", DynValue.NewCallback((_, a) => DynValue.NewNumber(_world.Get(_pid).Pods.Max)));
        // podsP: 100 quand le poids max est inconnu.
        Fill(inv, "podsP", DynValue.NewCallback((_, a) => { var st = _world.Get(_pid); return DynValue.NewNumber(st.Pods.Max > 0 ? st.PodsPercent : 100); }));
        if (inv.RawGet("category") is { Type: DataType.ClrFunction } cat) Fill(inv, "getTypeName", cat);
        if (inv.RawGet("count") is { Type: DataType.ClrFunction } cnt) Fill(inv, "itemCount", cnt);
        if (inv.RawGet("delete") is { Type: DataType.ClrFunction } del) Fill(inv, "deleteItem", del);
        var data = s.Globals.Get("data");
        if (data.Type == DataType.Table && data.Table.RawGet("itemName") is { Type: DataType.ClrFunction } nm) Fill(inv, "itemNameId", nm);
        // inventoryContent: lignes GivreClone (uid, gid, qty, pos…) + champs tiers (objectGID, objectUID, quantity, position).
        Fill(inv, "inventoryContent", DynValue.NewCallback((_, a) => BuildList(s, _launch.Inventory.Items(_pid), (r, it) =>
        {
            r["uid"] = DynValue.NewNumber(it.Uid); r["gid"] = DynValue.NewNumber(it.Gid); r["qty"] = DynValue.NewNumber(it.Qty);
            r["pos"] = DynValue.NewNumber(it.Position); r["name"] = DynValue.NewString(_launch.ItemDisplayName(it.Gid) ?? "");
            r["equipped"] = DynValue.NewBoolean(it.Position != 63);
            r["objectUID"] = DynValue.NewNumber(it.Uid); r["objectGID"] = DynValue.NewNumber(it.Gid);
            r["quantity"] = DynValue.NewNumber(it.Qty); r["position"] = DynValue.NewNumber(it.Position);
            r["effects"] = DynValue.NewTable(new Table(s));
        })));
        Fill(inv, "getAveragePrice", DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.AveragePrice(_pid, (int)L(a, 0)))));
        Fill(inv, "getUID", DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.Inventory.FirstBagUidByGid(_pid, L(a, 0)) ?? _launch.Inventory.FirstUidByGid(_pid, L(a, 0)) ?? -1)));
        Fill(inv, "getGID", DynValue.NewCallback((_, a) =>
        {
            int uid = N(a, 0);
            var it = _launch.Inventory.Items(_pid).FirstOrDefault(i => i.Uid == uid);
            return DynValue.NewNumber(uid != 0 && it.Uid == uid ? it.Gid : -1);
        }));
        // useItem(gid [, n=1]): UN exemplaire par défaut; inventory:use(gid) garde « tous ».
        DynValue UseN(long gid, int n)
        {
            if (gid <= 0 || n <= 0 || _launch.InventoryCount(_pid, gid) <= 0) return DynValue.False;
            bool ok = UseItems(gid, n, ct);
            if (ok) Ok("inventaire", Loc.Fmt("L.Lua.ItemUsed", n, _launch.ItemDisplayName(gid), gid));
            else Warn("inventaire", Loc.Fmt("L.Lua.ItemUseFail", _launch.ItemDisplayName(gid), gid));
            return DynValue.NewBoolean(ok);
        }
        Fill(inv, "useItem", DynValue.NewCallback((_, a) => UseN(L(a, 0), N(a, 1) > 0 ? N(a, 1) : 1)));
        Fill(inv, "useMultipleItem", DynValue.NewCallback((_, a) => UseN(L(a, 0), Math.Max(1, N(a, 1)))));
        Fill(inv, "dropItem", DynValue.NewCallback((_, a) => DynValue.NewBoolean(DropItemLogged((int)L(a, 0), N(a, 1)))));
        Fill(inv, "useItemuid", DynValue.NewCallback((_, a) =>
        {
            int uid = N(a, 0), n = N(a, 1) > 0 ? N(a, 1) : 1;
            if (uid <= 0 || n <= 0) return DynValue.False;
            bool ok = n <= 1 || s_useMultipleOff ? _launch.UseItemUid(_pid, uid) : _launch.UseInventoryItemMultiple(_pid, uid, n);
            if (ok) Ok("inventaire", Loc.Fmt("L.Lua.ItemUsed", n, "uid " + uid, uid));
            else Warn("inventaire", Loc.Fmt("L.Lua.ItemUseFail", "uid " + uid, uid));
            return DynValue.NewBoolean(ok);
        }));
        Fill(inv, "dropItemuid", DynValue.NewCallback((_, a) =>
        {
            int uid = N(a, 0), n = N(a, 1);
            bool ok = _launch.DropItemUid(_pid, uid, n);
            if (ok) Ok("inventaire", Loc.Fmt("L.Lua.ItemDropped", n, "uid " + uid, uid));
            else Warn("inventaire", Loc.Fmt("L.Lua.ItemDropFail", "uid " + uid, uid));
            return DynValue.NewBoolean(ok);
        }));
        Fill(inv, "deleteItemuid", DynValue.NewCallback((_, a) =>
        {
            int uid = N(a, 0), n = N(a, 1);
            return DynValue.NewBoolean(uid > 0 && _launch.DeleteItemUid(_pid, uid, n));
        }));
        Fill(inv, "objectName", DynValue.NewCallback((_, a) => DynValue.NewString(_launch.ItemDisplayName(L(a, 0)) ?? "")));
        Fill(inv, "getTypeId", DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.ItemTypeId(L(a, 0)))));
    }

    // ------------------------------------------------------------------ exchange:
    private void BindSnowExchange(Script s, CancellationToken ct)
    {
        var ex = GetOrCreateTable(s, "exchange");
        long L(CallbackArguments a, int i) => (long)(At(a, ex, i).CastToNumber() ?? 0);
        int N(CallbackArguments a, int i) => (int)(At(a, ex, i).CastToNumber() ?? 0);
        // putItem(gid, qty): échange joueur, coffre OU établi de craft ouvert; attend l'accusé.
        ex["putItem"] = DynValue.NewCallback((_, a) =>
        {
            long gid = L(a, 0); int n = N(a, 1);
            if (_launch.CraftOpen(_pid)) return DynValue.NewBoolean(_launch.CraftPutItemAsync(_pid, gid, n, ct).GetAwaiter().GetResult());
            // Échange joueur: accusé Exchange*, jamais les 3 s d'un événement de sac absent.
            if (_launch.TradeOpen(_pid)) return DynValue.NewBoolean(SendAndAck(() => _launch.TradePutItem(_pid, gid, n), TradeAckEvents, TradeAckMs, ct));
            if (_launch.StorageOpen(_pid))
            {
                ThrowIfStopping(ct);
                int before = BagQty(gid), moved = n > 0 ? Math.Min(n, before) : before;
                if (!_launch.BankDepositItem(_pid, gid, n)) return DynValue.False;
                // Une requête et un accusé PAR PILE : on attend que le sac ait perdu tout le lot (≤ 3 s), pas le 1er accusé venu.
                bool confirmed = WaitUntil(() => BagQty(gid) <= before - moved, ExchangeAckMs, ct);
                if (!confirmed) Warn("banque", "exchange:putItem : quantité transférée non confirmée par le serveur.");
                return DynValue.NewBoolean(confirmed);
            }
            Warn("banque", Loc.Fmt("L.Lua.BankNotOpen", "exchange:putItem"));
            return DynValue.False;
        });
        // Rien à déplacer (sac connu sans pile non équipée / coffre ouvert vide) : envoi sans attente (pas 3 s perdues).
        ex["putAllItems"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            bool empty = _launch.Inventory.StackCount(_pid) > 0 && !_launch.Inventory.Items(_pid).Any(i => i.Position == 63 && i.Qty > 0);
            return DynValue.NewBoolean(empty ? _launch.BankDepositAll(_pid) : SendAndSettleStorage(() => _launch.BankDepositAll(_pid), ct));
        });
        ex["getAllItems"] = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            bool empty = _launch.StorageOpen(_pid) && _launch.Bank.StackCount(_pid) == 0;
            return DynValue.NewBoolean(empty ? _launch.BankWithdrawAll(_pid) : SendAndSettleStorage(() => _launch.BankWithdrawAll(_pid), ct));
        });
    }

    // ------------------------------------------------------------------ sale:
    private void BindSnowSale(Script s, CancellationToken ct)
    {
        var sale = GetOrCreateTable(s, "sale");
        // AvailableSpace() : emplacements libres = max du compte (conditions de vente du HDV ouvert) - mes lots - entrées sans
        // quantité ni prix (comptées occupées, comme le moteur) ; -1 si inconnu. Même valeur que la mise en vente de l'onglet HDV.
        sale["AvailableSpace"] = DynValue.NewCallback((_, a) => DynValue.NewNumber(_launch.HdvFreeSlots(_pid)));
        // Boutique PNJ DÉJÀ ouverte (npc:npc(id, 1) ou npc:openShop(id)) : achat / vente par gid, mêmes envois que la fenêtre
        // d'interaction (ExchangeBuyRequest / vente des piles du sac, jamais l'équipement porté). Gaté Perm.Gather par le moteur.
        int Gid(CallbackArguments a) => (int)(At(a, sale, 0).CastToNumber() ?? 0);
        // Argument omis : 1 (compat). Quantité explicitement <= 0 : refuser, sans achat/vente accidentel d'un objet.
        int Qty(CallbackArguments a) => At(a, sale, 1).IsNil() ? 1 : (int)(At(a, sale, 1).CastToNumber() ?? 0);
        sale["buyToPnj"] = DynValue.NewCallback((_, a) =>
        {
            int gid = Gid(a), qty = Qty(a);
            if (gid <= 0 || qty <= 0) return DynValue.False;
            ThrowIfStopping(ct);
            var r = _launch.NpcBuyAsync(_pid, gid, qty, ct).GetAwaiter().GetResult();
            if (r.Done > 0) Ok("boutique", Loc.Fmt("L.Lua.NpcBought", r.Done, _launch.ItemDisplayName(gid)));
            if (!r.Ok || r.Done < qty) Warn("boutique", Loc.Fmt("L.Lua.NpcBuyFail", _launch.ItemDisplayName(gid), r.Reason));
            return DynValue.NewBoolean(r.Ok && r.Done >= qty);
        });
        sale["sellToPnj"] = DynValue.NewCallback((_, a) =>
        {
            int gid = Gid(a), qty = Qty(a);
            if (gid <= 0 || qty <= 0) return DynValue.False;
            ThrowIfStopping(ct);
            if (!CanSellNpcGid(gid)) return DynValue.False;
            var r = _launch.NpcSellAsync(_pid, gid, qty, ct).GetAwaiter().GetResult();
            if (r.Done > 0) Ok("boutique", Loc.Fmt("L.Lua.NpcSold", r.Done, _launch.ItemDisplayName(gid)));
            if (!r.Ok || r.Done < qty) Warn("boutique", Loc.Fmt("L.Lua.NpcSellFail", _launch.ItemDisplayName(gid), r.Reason));
            return DynValue.NewBoolean(r.Ok && r.Done >= qty);
        });
        // sellAllItemToPnj() : vend TOUT le sac au PNJ (piles du sac seulement). « Secret » : même vente, sans une ligne par objet.
        DynValue SellAll(bool quiet) => DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            int stacks = 0, failed = 0;
            foreach (var g in _launch.Inventory.Items(_pid).Where(i => i.Position == 63 && i.Qty > 0).GroupBy(i => i.Gid).ToList())
            {
                ThrowIfStopping(ct);
                if (g.Any(i => !_launch.ItemCanTrade(_pid, i.Uid))) continue;
                int wanted = g.Sum(i => i.Qty);
                var r = _launch.NpcSellAsync(_pid, (int)g.Key, wanted, ct).GetAwaiter().GetResult();
                if (r.Reason is "noshop" or "noperm")
                {
                    failed++;
                    Warn("boutique", Loc.Fmt("L.Lua.NpcSellFail", "—", r.Reason));
                    break;
                }
                if (r.Done > 0) { stacks++; if (!quiet) Ok("boutique", Loc.Fmt("L.Lua.NpcSold", r.Done, _launch.ItemDisplayName(g.Key))); }
                if (!r.Ok || r.Done < wanted) failed++;
            }
            Ok("boutique", Loc.Fmt("L.Lua.NpcSoldAll", stacks, failed));
            return DynValue.Nil;
        });
        sale["sellAllItemToPnj"] = SellAll(false);
        sale["sellAllItemToPnjSecret"] = SellAll(true);
        Fill(sale, "itemToUnfollow", DynValue.NewNumber(0));
    }

    // The NPC engine selects stacks by GID, not by UID. Refuse a mixed GID as well:
    // reducing its requested quantity cannot guarantee which stack the engine sells.
    private bool CanSellNpcGid(long gid) => !_launch.Inventory.Items(_pid)
        .Any(i => i.Gid == gid && i.Position == 63 && i.Qty > 0 && !_launch.ItemCanTrade(_pid, i.Uid));

    private void WarnSnowNotYet(string fn) { lock (_warnedSnow) if (!_warnedSnow.Add(fn)) return; Warn("script", Loc.Fmt("L.Lua.SnowNotYet", fn)); }

    // ------------------------------------------------------------------ craft: (établi déjà ouvert, ex. map:useById)
    private void BindSnowCraft(Script s, CancellationToken ct)
    {
        var craft = GetOrCreateTable(s, "craft");
        long L(CallbackArguments a, int i) => (long)(At(a, craft, i).CastToNumber() ?? 0);
        int N(CallbackArguments a, int i) => (int)(At(a, craft, i).CastToNumber() ?? 0);
        bool Open() { if (_launch.CraftOpen(_pid)) return true; Warn("craft", Loc.T("L.Lua.SnowCraftNotOpen")); return false; }
        DynValue put = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            if (!Open()) return DynValue.False;
            InterruptibleDelay(300, ct);   // cadence humaine entre deux poses
            return DynValue.NewBoolean(_launch.CraftPutItemAsync(_pid, L(a, 0), N(a, 1), ct).GetAwaiter().GetResult());
        });
        Fill(craft, "PutItem", put);
        Fill(craft, "putItem", put);
        DynValue count = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            return DynValue.NewBoolean(Open() && _launch.CraftSetCountAsync(_pid, Math.Max(1, N(a, 0)), ct).GetAwaiter().GetResult());
        });
        Fill(craft, "ChangeQuantityToCraft", count);
        Fill(craft, "changeQuantityToCraft", count);
        DynValue ready = DynValue.NewCallback((_, a) =>
        {
            ThrowIfStopping(ct);
            if (!Open()) return DynValue.False;
            InterruptibleDelay(500, ct);
            bool ok = _launch.CraftReadyAsync(_pid, ct).GetAwaiter().GetResult();
            if (ok) Ok("craft", Loc.T("L.Lua.SnowCraftDone")); else Warn("craft", Loc.T("L.Lua.SnowCraftFailed"));
            return DynValue.NewBoolean(ok);
        });
        Fill(craft, "Ready", ready);
        Fill(craft, "ready", ready);
        Fill(craft, "isOpen", DynValue.NewCallback((_, a) => DynValue.NewBoolean(_launch.CraftOpen(_pid))));
        if (s.Globals.Get("craftItem").Type == DataType.ClrFunction) Fill(craft, "craftItem", SnowFwd(s, craft, s.Globals.Get("craftItem")));
    }

    // ------------------------------------------------------------------ merchant:
    private void BindSnowMerchant(Script s)
    {
        var m = GetOrCreateTable(s, "merchant");
        // Le mode « marchand joueur » historique n'a pas de fournisseur moteur.
        // npc:openShop / npc:shopItems / npc:buy / npc:sell concernent la boutique d'un PNJ, pas merchant:.
        Fill(m, "status", DynValue.NewCallback((_, a) =>
            DynValue.NewTable(SnowStatusTable(s, false, "player_merchant_mode_unavailable"))));
        DynValue Stub(string fn) => UnsupportedLua("merchant:" + fn);
        Fill(m, "open", Stub("open"));
        Fill(m, "buyItem", Stub("buyItem"));
        Fill(m, "priceItem", Stub("priceItem"));
        Fill(m, "quantityItem", Stub("quantityItem"));
        Fill(m, "existItem", Stub("existItem"));
    }
}
