using MoonSharp.Interpreter;
using Xunit;

namespace GivreClone.Tests;

public sealed class ReadonlyDiagnosticPresets20261101Tests
{
    private static Script Load(string file, string setup)
    {
        var directory = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, "scripts", file)));
        var lua = new Script(CoreModules.Preset_SoftSandbox);
        // Only read APIs exist: any attempt to invoke an action fails the run.
        lua.DoString("""
            lines = {}
            priceCalls = 0
            function print(line) lines[#lines+1] = line end
            function getServerName() return "fixture-server" end
            inventory = {}
            function inventory:getAveragePrice(gid)
                priceCalls = priceCalls + 1
                if gid == 10 then return 5 end
                return 0
            end
            """);
        lua.DoString(setup);
        lua.DoString(File.ReadAllText(Path.Combine(directory!.FullName, "scripts", file)));
        return lua;
    }

    private static Table Run(Script lua)
    {
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        int lines = lua.Globals.Get("lines").Table.Length;
        Assert.False(lua.Call(lua.Globals.Get("move")).Boolean);
        Assert.Equal(lines, lua.Globals.Get("lines").Table.Length);
        return lua.Globals.Get("READONLY_DIAGNOSTIC_RESULT").Table;
    }

    [Fact]
    public void Audit_groups_bag_stacks_excludes_equipment_and_preserves_unknown_values()
    {
        var lua = Load("Readonly-Inventory-Audit-20261101.lua", """
            function inventory:items() return {
                {gid=20,qty=3,pos=63,name="unknown"},
                {gid=10,qty=2,pos=63,name="known"},
                {gid=10,qty=4,pos=63,name="known"},
                {gid=10,qty=1,pos=0,name="equipped"}
            } end
            """);
        var report = Run(lua);
        Assert.Equal(3, report.Get("bagStacks").Number);
        Assert.Equal(1, report.Get("equippedStacks").Number);
        Assert.Equal(9, report.Get("bagQuantity").Number);
        Assert.Equal(30, report.Get("knownSubtotal").Number);
        Assert.Equal(1, report.Get("unknownPriceRows").Number);
        Assert.True(report.Get("estimatedTotal").IsNil());
        Assert.Equal(10, report.Get("rows").Table.Get(1).Table.Get("gid").Number);
        Assert.Equal(6, report.Get("rows").Table.Get(1).Table.Get("quantity").Number);
        Assert.Equal(2, lua.Globals.Get("priceCalls").Number);
    }

    [Fact]
    public void Audit_bounds_output_but_totals_include_omitted_rows()
    {
        var lua = Load("Readonly-Inventory-Audit-20261101.lua", """
            function inventory:items()
                local rows={}
                for gid=1,45 do rows[#rows+1]={gid=gid,qty=1,pos=63,name=""} end
                return rows
            end
            function inventory:getAveragePrice(gid) return 2 end
            """);
        var report = Run(lua);
        Assert.Equal(5, report.Get("omittedRows").Number);
        Assert.Equal(90, report.Get("estimatedTotal").Number);
        Assert.Equal(42, lua.Globals.Get("lines").Table.Length);
    }

    [Fact]
    public void Unidentified_server_never_uses_a_price_as_a_known_valuation()
    {
        var lua = Load("Readonly-Inventory-Audit-20261101.lua", """
            function getServerName() return "" end
            function inventory:items() return {{gid=10,qty=2,pos=63,name=""}} end
            """);
        var report = Run(lua);
        Assert.Equal("unknown", report.Get("server").String);
        Assert.True(report.Get("estimatedTotal").IsNil());
        Assert.Equal(0, lua.Globals.Get("priceCalls").Number);
    }

    [Fact]
    public void Craft_missing_cost_is_partial_when_one_resource_price_is_unknown()
    {
        var lua = Load("Readonly-Craft-Cost-20261101.lua", """
            craft={}
            function craft:plan(gid,quantity)
                assert(gid==16520 and quantity==1)
                return {
                    materials={{gid=20,missing=4},{gid=10,missing=3}},
                    alreadyOwned={{gid=30,quantity=2}},
                    crafts={{gid=16520,crafts=1,jobLevel=20,characterJobLevel=10,jobTooLow=true}}
                }
            end
            """);
        var report = Run(lua);
        Assert.Equal("ok", report.Get("status").String);
        Assert.Equal(15, report.Get("knownSubtotal").Number);
        Assert.Equal(7, report.Get("missingQuantity").Number);
        Assert.Equal(1, report.Get("unknownPriceRows").Number);
        Assert.Equal(1, report.Get("jobTooLowSteps").Number);
        Assert.True(report.Get("estimatedMissingCost").IsNil());
        Assert.Equal(2, report.Get("alreadyOwned").Table.Get(1).Table.Get("quantity").Number);
    }

    [Fact]
    public void Already_owned_target_requires_no_missing_resource_cost()
    {
        var lua = Load("Readonly-Craft-Cost-20261101.lua", """
            craft={}
            function craft:plan(gid,quantity)
                return {materials={},crafts={},alreadyOwned={{gid=gid,quantity=quantity}}}
            end
            """);
        var report = Run(lua);
        Assert.Equal(0, report.Get("estimatedMissingCost").Number);
        Assert.Equal(0, report.Get("craftSteps").Number);
        Assert.Equal(0, lua.Globals.Get("priceCalls").Number);
    }

    [Fact]
    public void Missing_recipe_data_is_unavailable_not_a_zero_cost_success()
    {
        var lua = Load("Readonly-Craft-Cost-20261101.lua", """
            craft={}
            function craft:plan(gid,quantity) return nil,"fixture-no-data" end
            """);
        var report = Run(lua);
        Assert.Equal("unavailable", report.Get("status").String);
        Assert.Equal("fixture-no-data", report.Get("reason").String);
        Assert.True(report.Get("estimatedMissingCost").IsNil());
        Assert.Equal(0, lua.Globals.Get("priceCalls").Number);
        Assert.Equal(2, lua.Globals.Get("lines").Table.Length);
    }
}
