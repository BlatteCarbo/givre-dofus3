using MoonSharp.Interpreter;
using GivreClone.Engine.LuaTooling;
using Xunit;

namespace GivreClone.Tests.LuaTooling;

public sealed class ShortGatherTemplateTests
{
    [Fact]
    public void Gather_bank_template_separates_harvesting_and_deposit_routes()
    {
        var script = new Script(CoreModules.None);
        script.DoString(LuaTemplates.Scripts.Single(t => t.Id == "recolte-banque").Body);

        var gather = script.Call(script.Globals.Get("move")).Table;
        var bank = script.Call(script.Globals.Get("bank")).Table;

        Assert.Equal(4, gather.Length);
        foreach (var step in gather.Values)
        {
            Assert.True(step.Table.Get("gather").Boolean);
            Assert.True(step.Table.Get("npcBank").IsNil());
            Assert.Equal(DataType.String, step.Table.Get("path").Type);
        }
        Assert.Equal(1, bank.Length);
        Assert.True(bank.Get(1).Table.Get("npcBank").Boolean);
        Assert.True(bank.Get(1).Table.Get("gather").IsNil());
        Assert.Equal(90, script.Globals.Get("MAX_PODS").Number);
    }

    [Theory]
    [InlineData(0, 0, 3)]
    [InlineData(90, 0, 0)]
    [InlineData(80, 10, 1)]
    public void Short_gather_stops_at_pass_limit_or_full_bag(int pods, int gainedPods, int expectedHarvests)
    {
        var script = new Script(CoreModules.None);
        int harvests = 0;
        int currentPods = pods;
        script.Globals["getPodsPercent"] = (Func<int>)(() => currentPods);
        script.Globals["gatherAll"] = (Action)(() => { harvests++; currentPods += gainedPods; });
        script.DoString(LuaTemplates.Scripts.Single(t => t.Id == "recolte-courte").Body);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!script.Call(script.Globals.Get("move")).Boolean) break;
        }

        Assert.Equal(expectedHarvests, harvests);
        Assert.False(script.Call(script.Globals.Get("move")).Boolean);
        Assert.Equal(expectedHarvests, harvests);
    }
}
