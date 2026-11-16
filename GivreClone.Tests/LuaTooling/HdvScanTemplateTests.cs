using MoonSharp.Interpreter;
using GivreClone.Engine.LuaTooling;
using Xunit;

namespace GivreClone.Tests.LuaTooling;

public sealed class HdvScanTemplateTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(25, 20)]
    public void Scan_reads_only_requested_prices_up_to_cap_and_stays_stopped(int count, int expected)
    {
        var script = new Script(CoreModules.Basic | CoreModules.TableIterators);
        var seen = new List<int>();
        var eco = new Table(script);
        eco["price"] = DynValue.NewCallback((_, args) =>
        {
            seen.Add((int)args[1].Number);
            if (seen.Count % 2 == 0) return DynValue.Nil;
            var quote = new Table(script);
            quote["unit"] = 42;
            quote["source"] = "hdv";
            quote["ageMs"] = 1500;
            return DynValue.NewTable(quote);
        });
        script.Globals["eco"] = eco;
        int known = 0, unknown = 0;
        script.Globals["printMessage"] = (Action<string, string>)((_, _) => known++);
        script.Globals["printWarning"] = (Action<string>)(_ => unknown++);
        string gids = string.Join(", ", Enumerable.Range(101, count));
        script.DoString(LuaTemplates.Scripts.Single(t => t.Id == "hdv-scan").Body
            .Replace("local SCAN_GIDS = { 289, 303, 312 }", "local SCAN_GIDS = { " + gids + " }"));

        for (int i = 0; i <= count; i++)
            if (!script.Call(script.Globals.Get("move")).Boolean) break;

        Assert.Equal(Enumerable.Range(101, expected), seen);
        Assert.Equal((expected + 1) / 2, known);
        Assert.Equal(expected / 2, unknown);
        Assert.False(script.Call(script.Globals.Get("move")).Boolean);
        Assert.Equal(expected, seen.Count);
    }
}
