extern alias F06App;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using World = F06App::GivreClone.App.Services.BotWorldStore;
using Entry = F06App::GivreClone.App.Services.HdvStatEntry;
using Journal = F06App::GivreClone.App.Services.HdvStatsStore;
using Outcome = F06App::GivreClone.App.Services.LuaRunOutcome;
using Xunit;

namespace GivreClone.Tests;

public sealed class HdvSummaryControlTests
{
    [Theory]
    [InlineData(1100, 200, 900)]
    [InlineData(0, 200, -200)]
    public async Task Summary_subtracts_paid_taxes_without_claiming_acquisition_costs(long sales, long taxes, long net)
    {
        string character = "hdv-test-" + Guid.NewGuid().ToString("N");
        string path = Path.Combine(F06App::GivreClone.App.Services.MZP19LocalClone.DataRoot, "hdv-stats", character + ".jsonl");
        long timestamp = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        var launch = new Launch();
        var world = new World(launch);
        world.Get(717).CharacterName = character;
        using var host = new Host(717, launch, world);
        try
        {
            Journal.RecordOfflineSales(character, new Entry[]
            {
                new() { Ts = timestamp, Amount = sales, IsTax = false, Key = "sale" },
                new() { Ts = timestamp, Amount = taxes, IsTax = true, Key = "tax" },
                new() { Ts = timestamp - 30L * 86_400_000, Amount = 999, Key = "old" },
            });

            host.Start($$"""
                function move()
                    local result = stats:hdvSummary("week")
                    assert(result.sales == {{sales}})
                    assert(result.taxes == {{taxes}})
                    assert(result.afterTaxes == {{net}})
                    assert(result.acquisitionCostKnown == false)
                    return false
                end
                """);

            Assert.Equal(Outcome.Completed, await host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Unidentified_character_has_no_summary_and_the_report_template_stops()
    {
        var launch = new Launch();
        using var host = new Host(717, launch, new World(launch));
        host.Start("function move() assert(stats:hdvSummary('week') == nil) return false end");
        Assert.Equal(Outcome.Completed, await host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10)));
        host.Start(GivreClone.Engine.LuaTooling.LuaTemplates.Scripts.Single(t => t.Id == "rapport-kamas").Body);
        Assert.Equal(Outcome.Completed, await host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
