extern alias F06App;
using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using World = F06App::GivreClone.App.Services.BotWorldStore;
using GameDb = F06App::GivreClone.App.Services.LotM.GameDb;
using Item = F06App::GivreClone.App.Services.InventoryStore.Item;
using Outcome = F06App::GivreClone.App.Services.LuaRunOutcome;
using Xunit;

namespace GivreClone.Tests;

public sealed class FmPreparationControlTests
{
    [Theory]
    [InlineData(64, 10, true)]
    [InlineData(64, 9, false)]
    [InlineData(63, 10, false)]
    [InlineData(64, 0, false)]
    public async Task Preparation_uses_local_skill_compatibility_and_observed_level(int job, int level, bool admitted)
    {
        using var fake = new FakeGameDb();
        using (var connection = new SqliteConnection($"Data Source={fake.File}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                alter table ItemData add column typeId integer;
                alter table SkillData add column parentJobId integer;
                alter table SkillData add column isForgemagus integer;
                create table ItemTypeData(id integer, superTypeId integer);
                create table ItemSuperTypeData(id integer, possiblePositions text);
                create table SkillData_modifiableItemTypeIds_junction(SkillData_id text, target_id text);
                insert into ItemSuperTypeData values(1,'[6]');
                insert into ItemTypeData values(10,1);
                insert into SkillData(id,parentJobId,isForgemagus) values(9876,64,1);
                insert into SkillData_modifiableItemTypeIds_junction values('9876','10');
                insert into ItemData(id,level,typeId) values(1000,10,10);
                """;
            command.ExecuteNonQuery();
        }
        using var db = GameDb.TryOpen(fake.File)!;
        var launch = new Launch();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Launch).GetField("_gameDb", flags)!.SetValue(launch, db);
        var jobs = (ConcurrentDictionary<uint, ConcurrentDictionary<int, int>>)typeof(Launch).GetField("_jobLevels", flags)!.GetValue(launch)!;
        jobs[717] = new();
        jobs[717][job] = level;
        launch.Inventory.Seed(717, new[] { new Item(991, 1000, 1) });
        using var host = new Host(717, launch, new World(launch));

        host.Start($$"""
            function move()
                local result, reason = fm:prepare(991, {{job}})
                assert((result ~= nil) == {{admitted.ToString().ToLowerInvariant()}})
                if result then
                    assert(result.uid == 991 and result.gid == 1000)
                    assert(result.jobId == 64 and result.jobLevel == 10 and result.itemLevel == 10)
                end
                assert(type(reason) == "string" and #reason > 0)
                return false
            end
            """);

        Assert.Equal(Outcome.Completed, await host.RunCompletion.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(new Item(991, 1000, 1), launch.Inventory.Get(717, 991));
    }
}
