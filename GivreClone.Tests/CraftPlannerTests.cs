using GivreClone.App.Services.LotM;
using Microsoft.Data.Sqlite;
using MoonSharp.Interpreter;
using Xunit;

namespace GivreClone.Tests;

public class CraftPlannerTests
{
    [Fact]
    public void Shared_stock_is_used_once_and_subcrafts_precede_result()
    {
        using var fixture = new FakeGameDb();
        using var db = GameDb.TryOpen(fixture.File)!;
        var stock = new Dictionary<long, int> { [100] = 4, [101] = 1 };
        var plan = CraftPlanner.Build(db, new CraftNeed(102, 2), gid => stock.GetValueOrDefault(gid));
        Assert.Equal(new long[] { 101, 102 }, plan.Crafts.Select(c => c.Recipe.ResultId));
        Assert.Equal(new[] { 5, 2 }, plan.Crafts.Select(c => c.Count));
        Assert.Equal(new CraftNeed(100, 5), Assert.Single(plan.Materials));
        Assert.Equal(4, plan.AlreadyOwned.Single(n => n.Gid == 100).Quantity);
        Assert.Equal(1, plan.AlreadyOwned.Single(n => n.Gid == 101).Quantity);
        Assert.Equal(4, stock[100]);
    }

    [Fact]
    public void Owned_result_needs_no_materials()
    {
        using var fixture = new FakeGameDb();
        using var db = GameDb.TryOpen(fixture.File)!;
        var plan = CraftPlanner.Build(db, new CraftNeed(102, 2), gid => gid == 102 ? 2 : 0);
        Assert.Empty(plan.Crafts);
        Assert.Empty(plan.Materials);
        Assert.Equal(new CraftNeed(102, 2), Assert.Single(plan.AlreadyOwned));
    }

    [Fact]
    public void Cyclic_recipe_fails_explicitly()
    {
        using var fixture = new FakeGameDb();
        using (var c = new SqliteConnection($"Data Source={fixture.File}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "insert into RecipeData values ('cycle',13,'[1]',100,1); insert into RecipeData_ingredientIds_junction values ('cycle','102');";
            cmd.ExecuteNonQuery();
        }
        using var db = GameDb.TryOpen(fixture.File)!;
        Assert.Throws<InvalidOperationException>(() => CraftPlanner.Build(db, new CraftNeed(102, 1), _ => 0));
    }

    [Fact]
    public void Lua_plan_reads_stock_and_jobs_without_action_permission()
    {
        using var game = new FakeGame { CanAct = false };
        game.Items[100] = 4;
        game.Items[101] = 1;
        game.JobLevels[13] = 5;
        var script = new Script(CoreModules.Preset_SoftSandbox);
        Table Module(string name)
        {
            if (script.Globals.Get(name).Type == DataType.Table) return script.Globals.Get(name).Table;
            var table = new Table(script); script.Globals[name] = table; return table;
        }
        new LotMLuaModule(script, game, CancellationToken.None, Module, () => new OperationCanceledException(), _ => { }).Bind();
        script.DoString("""
            local p = craft:plan('Pain', 2)
            assert(p.item.gid == 102 and p.quantity == 2)
            assert(#p.crafts == 2 and p.crafts[1].gid == 101 and p.crafts[1].crafts == 5)
            assert(p.crafts[2].jobTooLow and p.crafts[2].characterJobLevel == 5)
            assert(#p.materials == 1 and p.materials[1].missing == 5)
            assert(type(p.materials[1].sources) == 'table')
            local missing, err = craft.plan('inconnu')
            assert(missing == nil and type(err) == 'string')
            local invalid, why = craft:plan(102, -1)
            assert(invalid == nil and type(why) == 'string')
            """);
    }
}
