extern alias F06App;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using World = F06App::GivreClone.App.Services.BotWorldStore;
using Outcome = F06App::GivreClone.App.Services.LuaRunOutcome;
using System.Text.Json;
using Xunit;

namespace GivreClone.Tests;

public sealed class StatsMapSummaryControlTests
{
    [Fact]
    public async Task Summary_exposes_persisted_map_visits_and_harvest_actions_not_quantities()
    {
        string character = "map-summary-" + Guid.NewGuid().ToString("N");
        string directory = Path.Combine(F06App::GivreClone.App.Services.MZP19LocalClone.DataRoot, "stats");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, character + ".json");
        long timestamp = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        var launch = new Launch();
        var world = new World(launch);
        world.Get(717).CharacterName = character;
        using var host = new Host(717, launch, world);
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                MapVisits = new[] { new[] { timestamp, 123L, 1L, 2L }, new[] { timestamp + 1000, 456L, 3L, 4L } },
                Harvests = new[] { new[] { timestamp + 100, 99L, 0L }, new[] { timestamp + 200, 40L, 0L } },
            }));

            host.Start("""
                function move()
                    local result = stats:summary("week")
                    assert(#result.maps == 2)
                    local byMap = {}
                    for _, row in ipairs(result.maps) do byMap[row.map] = row end
                    assert(byMap["1,2"].visits == 1)
                    assert(byMap["1,2"].harvests == 2)
                    assert(byMap["3,4"].visits == 1)
                    assert(byMap["3,4"].harvests == 0)
                    return false
                end
                """);

            Assert.Equal(Outcome.Completed, await host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { File.Delete(path); }
    }
}
