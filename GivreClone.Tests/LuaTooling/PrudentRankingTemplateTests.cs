using MoonSharp.Interpreter;
using GivreClone.Engine.LuaTooling;
using Xunit;

namespace GivreClone.Tests.LuaTooling;

public sealed class PrudentRankingTemplateTests
{
    [Theory]
    [InlineData(4, 0)]
    [InlineData(5, 2)]
    public void Ranking_requires_two_sampled_maps_and_sorts_by_harvests_per_visit(int visits, int expectedReports)
    {
        var script = new Script(CoreModules.Preset_HardSandbox);
        var maps = new Table(script);
        foreach (var (name, count, harvests) in new[] { ("low", visits, 5), ("high", visits, 20), ("excluded", 1, 1000) })
        {
            var map = new Table(script);
            map["map"] = name;
            map["visits"] = count;
            map["harvests"] = harvests;
            maps.Append(DynValue.NewTable(map));
        }
        var summary = new Table(script);
        summary["maps"] = maps;
        var stats = new Table(script);
        stats["summary"] = DynValue.NewCallback((_, _) => DynValue.NewTable(summary));
        script.Globals["stats"] = stats;
        var reports = new List<string>();
        script.Globals["printMessage"] = (Action<string, string>)((message, _) => reports.Add(message));
        script.Globals["printWarning"] = (Action<string>)(_ => { });
        script.DoString(LuaTemplates.Scripts.Single(t => t.Id == "classement-prudent").Body);

        Assert.False(script.Call(script.Globals.Get("move")).Boolean);

        Assert.Equal(expectedReports, reports.Count);
        if (expectedReports != 0)
            Assert.Equal(new[] { "high", "low" }, reports.Select(r => r.Split(':')[0].Trim()));
    }
}
