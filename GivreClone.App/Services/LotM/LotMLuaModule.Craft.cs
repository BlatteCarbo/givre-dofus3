using System;
using System.Linq;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services.LotM;

public sealed partial class LotMLuaModule
{
    private void BindCraftPlan(Table craft)
    {
        Add(craft, "plan", a =>
        {
            if (_g.Db is not { } db) return Nope(NoData);
            long? gid = Num(Arg(a, 0)) ?? (Str(Arg(a, 0)) is { } name ? db.ItemGidByName(name) : null);
            if (gid is null || db.ItemName(gid.Value) is null) return Nope("objet inconnu");
            long qty = Num(Arg(a, 1)) ?? 1;
            if (qty < 1 || qty > int.MaxValue) return Nope("quantité invalide");
            CraftPlan plan;
            try { plan = CraftPlanner.Build(db, new CraftNeed(gid.Value, (int)qty), _g.ItemCount); }
            catch (InvalidOperationException ex) { return Nope(ex.Message); }
            catch (OverflowException) { return Nope("quantité trop grande pour ce plan"); }
            DynValue Need(CraftNeed n, string field) => Tbl(("gid", n.Gid), ("name", db.ItemName(n.Gid) ?? ""), (field, n.Quantity));
            return Tbl(("item", Tbl(("gid", gid.Value), ("name", db.ItemName(gid.Value)))), ("quantity", qty),
                ("crafts", Arr(plan.Crafts, c => Tbl(("gid", c.Recipe.ResultId), ("name", c.Recipe.Name),
                    ("crafts", c.Count), ("job", c.Recipe.Job), ("jobLevel", c.Recipe.Level),
                    ("characterJobLevel", _g.JobLevel(c.Recipe.JobId)),
                    ("jobTooLow", _g.JobLevel(c.Recipe.JobId) < c.Recipe.Level),
                    ("ingredients", Arr(c.Recipe.Ingredients, i => Tbl(("gid", i.Gid), ("name", i.Name), ("perCraft", i.Quantity))))))),
                ("materials", Arr(plan.Materials.OrderBy(n => db.ItemName(n.Gid)), n =>
                {
                    var row = Need(n, "missing");
                    var sources = _s.Call(_module("data").Get("sources"), DynValue.NewNumber(n.Gid));
                    if (sources.Type == DataType.Table) row.Table["sources"] = sources;
                    return row;
                })),
                ("alreadyOwned", Arr(plan.AlreadyOwned, n => Need(n, "quantity"))));
        });
    }
}
