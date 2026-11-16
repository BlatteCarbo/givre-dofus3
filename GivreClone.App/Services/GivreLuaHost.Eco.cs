using System;
using System.Linq;
using MoonSharp.Interpreter;
using GivreClone.App.Services.Eco;

namespace GivreClone.App.Services;

// eco: l'Atelier éco en script (mêmes calculs que l'interface et que les outils MCP).
public sealed partial class GivreLuaHost
{
    private void BindSnowEco(Script s)
    {
        var eco = GetOrCreateTable(s, "eco");
        string Str(CallbackArguments a, int i) => At(a, eco, i).CastToString() ?? "";
        long Num(CallbackArguments a, int i, long dflt = 0) => At(a, eco, i).CastToNumber() is { } d ? (long)d : dflt;
        static DynValue Opt(decimal? v) => v is { } d ? DynValue.NewNumber((double)d) : DynValue.Nil;
        Table Row(params (string k, DynValue v)[] kv) { var t = new Table(s); foreach (var (k, v) in kv) t[k] = v; return t; }
        DynValue Arr(System.Collections.Generic.IEnumerable<Table> rows) { var t = new Table(s); int i = 1; foreach (var r in rows) t[i++] = DynValue.NewTable(r); return DynValue.NewTable(t); }

        // eco.find("texte", limite = 10) -> { {gid, name, level}, ... }
        Fill(eco, "find", DynValue.NewCallback((_, a) =>
        {
            if (_launch.GameData is not { } db || Str(a, 0).Trim().Length < 2) return Arr(Array.Empty<Table>());
            return Arr(db.FindItems(Str(a, 0).Trim(), (int)Math.Clamp(Num(a, 1, 10), 1, 50))
                .Select(m => Row(("gid", DynValue.NewNumber(m.Gid)), ("name", DynValue.NewString(m.Name)), ("level", DynValue.NewNumber(m.Level)))));
        }));

        // eco.price(gid) -> { unit, source, ageMs } ou nil (HDV observé par ce bot, sinon prix saisi à la main)
        Fill(eco, "price", DynValue.NewCallback((_, a) =>
        {
            if (EcoService.PriceFor((uint)_pid, Num(a, 0)) is not { } q) return DynValue.Nil;
            return DynValue.NewTable(Row(("unit", DynValue.NewNumber((double)q.Unit)), ("source", DynValue.NewString(q.Source)),
                                         ("ageMs", q.AgeMs is { } ms ? DynValue.NewNumber(ms) : DynValue.Nil)));
        }));

        // eco.setPrice(gid, kamas) : prix saisi à la main (0 = l'effacer) pour le serveur de ce bot.
        Fill(eco, "setPrice", DynValue.NewCallback((_, a) =>
        {
            long gid = Num(a, 0); long price = Num(a, 1, -1);
            if (gid <= 0 || price < 0) return DynValue.False;
            lock (typeof(EcoService)) EcoService.Book.Set(_pid > 0 ? _launch.ServerIdOf((uint)_pid) : 0, gid, price);
            EcoService.SavePrices();
            return DynValue.True;
        }));

        // eco.craftAdvice(gid | "nom", quantité = 1) -> { gid, name, craftCost, buyCost, saving, savingPercent, resaleProfit, recommendation,
        //                                                  missingPrices = {gid...}, shopping = { {gid, name, quantity, cost}, ... } }
        Fill(eco, "craftAdvice", DynValue.NewCallback((_, a) =>
        {
            var db = _launch.GameData; if (db is null) return DynValue.Nil;
            long gid = At(a, eco, 0).Type == DataType.Number ? (long)At(a, eco, 0).Number : 0;
            if (gid == 0)
            {
                string q = Str(a, 0).Trim(); if (q.Length < 2) return DynValue.Nil;
                var hit = db.FindItems(q, 10).Where(m => db.Recipe(m.Gid) is not null).Select(m => (long?)m.Gid).FirstOrDefault();
                if (hit is null) return DynValue.Nil;
                gid = hit.Value;
            }
            if (EcoService.Advice((uint)_pid, gid, Math.Clamp(Num(a, 1, 1), 1, 100000)) is not { } adv) return DynValue.Nil;
            var shopping = CraftAdvisor.ShoppingList(adv.Root).Select(x => Row(("gid", DynValue.NewNumber(x.Gid)), ("name", DynValue.NewString(x.Name)),
                ("quantity", DynValue.NewNumber(x.Quantity)), ("cost", Opt(x.Cost))));
            var missing = new Table(s); int i = 1; foreach (var m in adv.MissingPrices) missing[i++] = DynValue.NewNumber(m);
            var res = Row(("gid", DynValue.NewNumber(gid)), ("name", DynValue.NewString(adv.Root.Name)), ("craftCost", Opt(adv.CraftCost)), ("buyCost", Opt(adv.BuyCost)),
                          ("saving", Opt(adv.Saving)), ("savingPercent", adv.SavingPercent is { } p ? DynValue.NewNumber(p) : DynValue.Nil),
                          ("resaleProfit", Opt(adv.ResaleProfit)), ("recommendation", DynValue.NewString(adv.Recommendation)));
            res["missingPrices"] = DynValue.NewTable(missing);
            res["shopping"] = Arr(shopping);
            return DynValue.NewTable(res);
        }));

        // eco.jobPlan(métier, deNiveau, aNiveau, mode = "cheapest" | "fastest") -> { reached, finalLevel, totalCrafts, totalXp, totalCost, steps = { {fromLevel, toLevel, gid, recipe, crafts, xp, cost}, ... } }
        Fill(eco, "jobPlan", DynValue.NewCallback((_, a) =>
        {
            if (_launch.GameData?.Job(At(a, eco, 0).Type == DataType.Number ? ((int)At(a, eco, 0).Number).ToString() : Str(a, 0)) is not { } job) return DynValue.Nil;
            bool cheapest = !string.Equals(Str(a, 3), "fastest", StringComparison.OrdinalIgnoreCase);
            if (EcoService.JobPlan((uint)_pid, job.Id, (int)Num(a, 1, 1), (int)Num(a, 2, 100), cheapest) is not { } plan) return DynValue.Nil;
            var res = Row(("reached", DynValue.NewBoolean(plan.Reached)), ("finalLevel", DynValue.NewNumber(plan.FinalLevel)), ("totalCrafts", DynValue.NewNumber(plan.TotalCrafts)),
                          ("totalXp", DynValue.NewNumber(plan.TotalXp)), ("totalCost", Opt(plan.TotalCost)));
            res["steps"] = Arr(plan.Steps.Select(s => Row(("fromLevel", DynValue.NewNumber(s.FromLevel)), ("toLevel", DynValue.NewNumber(s.ToLevel)), ("gid", DynValue.NewNumber(s.Recipe.Gid)),
                ("recipe", DynValue.NewString(s.Recipe.Name)), ("crafts", DynValue.NewNumber(s.Crafts)), ("xp", DynValue.NewNumber(s.Xp)), ("cost", Opt(s.Cost)))));
            return DynValue.NewTable(res);
        }));

        // eco.resources([texte, métier]) -> { {itemId, name, job, jobName, level}, ... } ; métier : 2 bûcheron, 24 mineur, 26 alchimiste, 28 paysan, 36 pêcheur, 1 eau
        Fill(eco, "resources", DynValue.NewCallback((_, a) =>
            Arr(Resources.ResourceMap.Default.Find(Str(a, 0), At(a, eco, 1).IsNil() ? null : (int?)Num(a, 1)).Select(r =>
                Row(("itemId", DynValue.NewNumber(r.ItemId)), ("name", DynValue.NewString(r.Name)), ("job", DynValue.NewNumber(r.Job)),
                    ("jobName", DynValue.NewString(Resources.ResourceMap.JobName(r.Job))), ("level", DynValue.NewNumber(r.Level)))))));

        // eco.resourceMaps(ressource, [limite = 10, x, y, intérieurs = true]) -> { {mapId, x, y, count, distance, indoor}, ... } (les plus proches de ce bot, ou de (x, y), d'abord)
        Fill(eco, "resourceMaps", DynValue.NewCallback((_, a) =>
        {
            var map = Resources.ResourceMap.Default;
            var res = At(a, eco, 0).Type == DataType.Number ? map.Resolve(((int)At(a, eco, 0).Number).ToString()) : map.Resolve(Str(a, 0));
            if (res is null) return DynValue.Nil;
            int limit = (int)Math.Clamp(Num(a, 1, 10), 1, 200);
            var origin = EcoService.OriginOf((uint)_pid);
            int? x = At(a, eco, 2).IsNil() ? origin?.X : (int)Num(a, 2), y = At(a, eco, 3).IsNil() ? origin?.Y : (int)Num(a, 3);
            bool indoor = At(a, eco, 4).IsNil() || At(a, eco, 4).CastToBool();
            int? world = At(a, eco, 2).IsNil() ? origin?.World : null;
            return Arr(map.Best(res.ItemId, x, y, world, indoor, limit).Select(h => Row(("mapId", DynValue.NewNumber(h.Node.MapId)), ("x", DynValue.NewNumber(h.Node.X)),
                ("y", DynValue.NewNumber(h.Node.Y)), ("count", DynValue.NewNumber(h.Node.Count)), ("distance", DynValue.NewNumber(h.Distance)), ("indoor", DynValue.NewBoolean(h.Node.Indoor)))));
        }));

        // eco.resourceRoute(ressource, [nombre = 6, intérieurs = false]) -> { {mapId, x, y, count}, ... } : circuit de récolte (cartes riches et proches, tournée du plus proche voisin)
        Fill(eco, "resourceRoute", DynValue.NewCallback((_, a) =>
        {
            var map = Resources.ResourceMap.Default;
            var res = At(a, eco, 0).Type == DataType.Number ? map.Resolve(((int)At(a, eco, 0).Number).ToString()) : map.Resolve(Str(a, 0));
            if (res is null) return DynValue.Nil;
            var origin = EcoService.OriginOf((uint)_pid);
            var route = Resources.GatherScript.Route(map, res.ItemId, origin?.X, origin?.Y, origin?.World, (int)Math.Clamp(Num(a, 1, 6), 1, 30), !At(a, eco, 2).IsNil() && At(a, eco, 2).CastToBool());
            return Arr(route.Select(n => Row(("mapId", DynValue.NewNumber(n.MapId)), ("x", DynValue.NewNumber(n.X)), ("y", DynValue.NewNumber(n.Y)), ("count", DynValue.NewNumber(n.Count)))));
        }));

        // eco.wealth() -> { bag, bank, kamasInBag, kamasInBank, total } : valeur estimée (prix HDV observés ou saisis) — nil si hors ligne
        Fill(eco, "wealth", DynValue.NewCallback((_, a) =>
        {
            if (_pid <= 0) return DynValue.Nil;
            uint wp = (uint)_pid;
            decimal? P(long g) => EcoService.PriceFor(wp, g)?.Unit;
            var bag = Wealth.Compute(_launch.Inventory.Items(wp).Select(i => (i.Gid, EcoService.NameOf(i.Gid), (long)i.Qty)), P, 20);
            var bank = Wealth.Compute(_launch.Bank.Items(wp).Select(i => (i.Gid, EcoService.NameOf(i.Gid), (long)i.Qty)), P, 20);
            long k = Math.Max(0, _launch.Kamas(wp)), kb = Math.Max(0, _launch.BankKamas(wp));
            return DynValue.NewTable(Row(("bag", DynValue.NewNumber((double)bag.Total)), ("bank", DynValue.NewNumber((double)bank.Total)),
                ("kamasInBag", DynValue.NewNumber(k)), ("kamasInBank", DynValue.NewNumber(kb)), ("total", DynValue.NewNumber((double)(bag.Total + bank.Total + k + kb)))));
        }));

        // eco.deals(minDiscountPercent = 25, minLotGain = 1000, maxAgeMinutes = 180) -> { {gid, name, lotSize, unitPrice, averagePrice, discountPercent, lotGain}, ... }
        Fill(eco, "deals", DynValue.NewCallback((_, a) =>
        {
            if (_pid <= 0) return Arr(Array.Empty<Table>());
            uint dp = (uint)_pid;
            var quotes = _launch.Hdv.KnownGids(dp).Select(g =>
            {
                long age = _launch.Hdv.PriceAgeMs(dp, g); long avg = _launch.Hdv.AverageFor(dp, g);
                if (avg <= 0 && g > 0 && g <= int.MaxValue) avg = _launch.AveragePrice(dp, (int)g);
                return new MarketQuote(g, EcoService.NameOf(g), _launch.Hdv.MarketPricesFor(dp, g), avg, age == long.MaxValue ? null : age);
            }).ToList();
            long maxAge = Num(a, 2, 180);
            var deals = MarketDeals.Find(quotes, (int)Math.Clamp(Num(a, 0, 25), 1, 95), (int)Math.Clamp(Num(a, 1, 1000), 0, int.MaxValue), maxAge > 0 ? maxAge * 60_000L : null);
            return Arr(deals.Take(50).Select(d => Row(("gid", DynValue.NewNumber(d.Gid)), ("name", DynValue.NewString(d.Name)), ("lotSize", DynValue.NewNumber(d.LotSize)),
                ("unitPrice", DynValue.NewNumber((double)d.UnitPrice)), ("averagePrice", DynValue.NewNumber((double)d.Average)),
                ("discountPercent", DynValue.NewNumber(Math.Round(d.DiscountPercent, 1))), ("lotGain", DynValue.NewNumber((double)d.LotGain)))));
        }));

        // eco.subscriptions() -> { {account, character, endUtc, remaining, level}, ... } (dernière fin d'abonnement vue par compte)
        Fill(eco, "subscriptions", DynValue.NewCallback((_, a) =>
        {
            var now = DateTime.UtcNow;
            var entries = SubscriptionStore.Locked(b => b.Entries.OrderBy(e => e.EndUtc).ToList());
            return Arr(entries.Select(e => Row(("account", DynValue.NewString(e.Login)), ("character", DynValue.NewString(e.Character ?? "")),
                ("endUtc", e.EndUtc.Year < 2000 ? DynValue.Nil : DynValue.NewString(e.EndUtc.ToString("o"))),
                ("remaining", DynValue.NewString(SubscriptionTracker.Describe(e.EndUtc, now))),
                ("level", DynValue.NewString(SubscriptionTracker.Level(e.EndUtc, now).ToString().ToLowerInvariant())))));
        }));

        // eco.addAlert(gid, "below" | "above", prix) : alerte Discord quand un bot observe ce prix à l'HDV ; eco.removeAlert(gid) ; eco.alerts()
        Fill(eco, "addAlert", DynValue.NewCallback((_, a) =>
        {
            long gid = Num(a, 0); string dir = Str(a, 1).ToLowerInvariant(); long target = Num(a, 2);
            if (gid <= 0 || target <= 0 || dir is not ("below" or "above")) return DynValue.False;
            PriceAlertStore.Locked(b =>
            {
                b.Alerts.RemoveAll(x => x.Gid == gid && x.Below == (dir == "below"));
                b.Alerts.Add(new PriceAlert { Gid = gid, Name = EcoService.NameOf(gid), Below = dir == "below", Target = target });
                return 0;
            });
            PriceAlertStore.Save();
            return DynValue.True;
        }));
        Fill(eco, "removeAlert", DynValue.NewCallback((_, a) =>
        {
            long gid = Num(a, 0);
            int n = PriceAlertStore.Locked(b => b.Alerts.RemoveAll(x => x.Gid == gid));
            if (n > 0) PriceAlertStore.Save();
            return DynValue.NewBoolean(n > 0);
        }));
        Fill(eco, "alerts", DynValue.NewCallback((_, a) =>
            Arr(PriceAlertStore.Locked(b => b.Alerts.ToList()).Select(x => Row(("gid", DynValue.NewNumber(x.Gid)), ("name", DynValue.NewString(x.Name)),
                ("direction", DynValue.NewString(x.Below ? "below" : "above")), ("target", DynValue.NewNumber((double)x.Target)))))));
    }
}
