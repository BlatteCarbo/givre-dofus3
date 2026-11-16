extern alias F06App;

using System.Reflection;
using Microsoft.Data.Sqlite;
using GivreClone.Protocol;
using Xunit;
using SmithMagicViewModel = F06App::GivreClone.App.ViewModels.SmithMagicViewModel;
using GameDb = F06App::GivreClone.App.Services.LotM.GameDb;
using WorldPlace = F06App::GivreClone.App.Services.LotM.WorldPlace;
using PlaceDoor = F06App::GivreClone.App.Services.LotM.PlaceDoor;

namespace GivreClone.Tests;

public sealed class SmithWorkshopTravelTests
{
    private const long WorkshopMap = 212601859; // HintData: Bonta, atelier des forgemages.
    private static readonly WorldPlace Place = new("Atelier des forgemages", "Atelier", WorkshopMap,
        "-29,-57", "", "", "", 1, Array.Empty<PlaceDoor>());
    private static MapInteractive Interactive(int skill, int uid = 701, bool enabled = true)
        => new(100, 501, 0, 0, [new(uid, skill, 0, enabled)]);

    private static SmithMagicViewModel Model()
    {
        var vm = new SmithMagicViewModel
        {
            RequiredSkill = _ => 113,
            WorkshopPlaces = _ => [Place],
            CurrentMap = _ => 100,
            InteractivesForPid = _ => []
        };
        typeof(SmithMagicViewModel).GetField("_pid", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, 101);
        return vm;
    }

    [Fact]
    public async Task Arrival_is_awaited_before_returning_the_forge_interactive()
    {
        var vm = Model();
        var arrival = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        long map = 100;
        vm.CurrentMap = _ => map;
        vm.InteractivesForPid = pid => { Assert.Equal(101u, pid); return map == WorkshopMap ? [Interactive(113)] : []; };
        vm.Travel = (pid, target, _) =>
        {
            Assert.Equal(101u, pid);
            Assert.Equal(WorkshopMap, target);
            return arrival.Task;
        };
        var preparation = vm.PrepareWorkshopAsync(101, 10, CancellationToken.None);
        Assert.False(preparation.IsCompleted);
        map = WorkshopMap;
        arrival.SetResult(true);
        Assert.Equal((501, 701), await preparation.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Failed_travel_wrong_map_or_incompatible_skill_prevents_forge_admission(bool travel, bool correctMap, bool compatible)
    {
        var vm = Model();
        bool moved = false;
        vm.Travel = (_, _, _) => { moved = true; return Task.FromResult(travel); };
        vm.CurrentMap = _ => correctMap && moved ? WorkshopMap : 100;
        vm.InteractivesForPid = _ => moved ? [Interactive(compatible ? 113 : 168)] : [];
        Assert.Null(await vm.PrepareWorkshopAsync(101, 10, CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_during_travel_rejects_even_a_late_success()
    {
        var vm = Model();
        using var cts = new CancellationTokenSource();
        var arrival = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.Travel = (_, _, token) => { Assert.Equal(cts.Token, token); return arrival.Task; };
        var preparation = vm.PrepareWorkshopAsync(101, 10, cts.Token);
        cts.Cancel();
        arrival.SetResult(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Switching_bot_during_travel_never_reads_the_new_bots_workshop()
    {
        var vm = Model();
        var arrival = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.Travel = (_, _, _) => arrival.Task;
        var preparation = vm.PrepareWorkshopAsync(101, 10, CancellationToken.None);
        vm.AttachPid(0); // No live store needed; exactly the selection-change reset.
        vm.InteractivesForPid = _ => throw new InvalidOperationException("Stale continuation read the new bot");
        arrival.SetResult(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Server_rejected_machine_is_not_reused_when_another_machine_is_available()
    {
        var vm = Model();
        vm.InteractivesForPid = _ =>
        [
            Interactive(113, 701),
            new MapInteractive(101, 502, 0, 0, [new(702, 113, 0, true)])
        ];
        vm.RejectedWorkshopElements.Add(501);

        Assert.Equal((502, 702), await vm.PrepareWorkshopAsync(101, 10, CancellationToken.None));
    }

    [Fact]
    public async Task Inaccessible_machine_is_skipped_before_trying_another_local_machine()
    {
        var vm = Model();
        vm.InteractivesForPid = _ =>
        [
            Interactive(113),
            new MapInteractive(101, 502, 0, 0, [new(702, 113, 0, true)])
        ];
        var approached = new List<int>();
        vm.ApproachWorkshop = (_, element, _, _) =>
        {
            approached.Add(element);
            return Task.FromResult(element == 502);
        };

        Assert.Equal((502, 702), await vm.PrepareWorkshopAsync(101, 10, CancellationToken.None));
        Assert.Equal([501, 502], approached);
    }

    [Fact]
    public async Task Existing_workshop_requires_the_items_exact_enabled_skill_and_refreshes_instance_uid()
    {
        var vm = Model();
        vm.InteractivesForPid = _ => [Interactive(168), Interactive(113, 800, false), Interactive(113, 900)];
        vm.Travel = (_, _, _) => throw new InvalidOperationException("Already at a compatible workshop");
        Assert.Equal((501, 900), await vm.PrepareWorkshopAsync(101, 10, CancellationToken.None));
        vm.InteractivesForPid = _ => [Interactive(113, 901)];
        Assert.Equal((501, 901), await vm.PrepareWorkshopAsync(101, 10, CancellationToken.None));
    }

    [Fact]
    public async Task Unreachable_candidate_is_skipped_but_only_confirmed_compatible_arrival_is_usable()
    {
        var vm = Model();
        vm.WorkshopPlaces = _ => [Place with { MapId = 1 }, Place];
        long map = 100;
        var visited = new List<long>();
        vm.CurrentMap = _ => map;
        vm.InteractivesForPid = _ => map == WorkshopMap ? [Interactive(113)] : [];
        vm.Travel = (_, target, _) =>
        {
            visited.Add(target);
            if (target != WorkshopMap) return Task.FromResult(false);
            map = target;
            return Task.FromResult(true);
        };
        Assert.NotNull(await vm.PrepareWorkshopAsync(101, 10, CancellationToken.None));
        Assert.Equal(new long[] { 1, WorkshopMap }, visited);
    }

    [Fact]
    public void Resource_lookup_does_not_materialize_the_entire_skill_table_for_each_item()
    {
        using var fake = new FakeGameDb();
        using (var connection = new SqliteConnection($"Data Source={fake.File}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                with recursive ids(id) as (
                    select 1000 union all select id + 1 from ids where id < 3000
                )
                insert into SkillData select id, 11, id from ids;
                """;
            command.ExecuteNonQuery();
        }
        using var db = GameDb.TryOpen(fake.File);
        Assert.NotNull(db);
        Assert.True(SmithMagicViewModel.IsGatheredResource(db, 100));
        Assert.False(SmithMagicViewModel.IsGatheredResource(db, 9999));

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 8; i++)
            Assert.False(SmithMagicViewModel.IsGatheredResource(db, 9999 + i));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.InRange(allocated, 0, 1_000_000);
    }

    [Fact]
    public void Item_type_relationship_selects_exact_profession_without_guessing_unknown_items()
    {
        using var fake = new FakeGameDb();
        using (var connection = new SqliteConnection($"Data Source={fake.File}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                alter table ItemData add column typeId integer;
                update ItemData set typeId = case id when 100 then 6 when 101 then 9 else 999 end;
                create table SkillData_modifiableItemTypeIds_junction (SkillData_id integer, target_id integer);
                insert into SkillData_modifiableItemTypeIds_junction values (113, 6), (168, 9), (1, 999);
                """;
            command.ExecuteNonQuery();
        }
        using var db = GameDb.TryOpen(fake.File);
        Assert.NotNull(db);
        Assert.Equal(113, SmithMagicViewModel.SkillForItem(db, 100));
        Assert.Equal(168, SmithMagicViewModel.SkillForItem(db, 101));
        Assert.Null(SmithMagicViewModel.SkillForItem(db, 102));
        Assert.Null(SmithMagicViewModel.SkillForItem(db, 999));
    }

    [Fact]
    public void Workshop_door_is_only_the_named_enter_skill()
    {
        var door = new MapInteractive(332, 522940, 158, 0, [new InteractiveSkill(1, 84, 0, true)], "Atelier des forgemages");
        var house = new MapInteractive(295, 522175, 1, 0, [new InteractiveSkill(2, 84, 0, true)], "Maison");
        var disabled = new MapInteractive(401, 522088, 158, 0, [new InteractiveSkill(3, 84, 0, false)], "Atelier des forgemages");
        var anvil = Interactive(168);
        Assert.Equal(522940, SmithMagicViewModel.WorkshopDoor([house, disabled, anvil, door], "Atelier des Forgemages")!.ElementId);
        Assert.Null(SmithMagicViewModel.WorkshopDoor([house, anvil], "Atelier des forgemages"));
        Assert.Null(SmithMagicViewModel.WorkshopDoor(null, "Atelier des forgemages"));
        var useDoor = new MapInteractive(401, 522088, 158, 0, [new InteractiveSkill(4, 184, 0, true)], "Atelier des forgemages");
        var plainUse = new MapInteractive(318, 488127, -1, 0, [new InteractiveSkill(5, 184, 0, true)], "Utiliser");
        var sign = new MapInteractive(417, 522693, 158, 0, [new InteractiveSkill(6, 361, 0, true)], "Atelier des forgemages");
        Assert.Equal(522088, SmithMagicViewModel.WorkshopDoor([plainUse, sign, useDoor], "Atelier des forgemages")!.ElementId);
        Assert.Equal(184, SmithMagicViewModel.DoorSkill(useDoor)!.SkillId);
        Assert.Equal(84, SmithMagicViewModel.DoorSkill(door)!.SkillId);
        Assert.Null(SmithMagicViewModel.WorkshopDoor([plainUse, sign], "Atelier des forgemages"));
    }

    [Fact]
    public async Task Named_use_skill_door_is_entered_before_any_rune()
    {
        var vm = Model();
        long map = 100;
        var entered = new List<string>();
        vm.CurrentMap = _ => map;
        vm.InteractivesForPid = _ => map == WorkshopMap
            ? [new MapInteractive(401, 522088, 158, 0, [new InteractiveSkill(4, 184, 0, true)], "Atelier des forgemages")]
            : map == 999 ? [Interactive(113)] : [];
        vm.Travel = (_, target, _) => { map = target; return Task.FromResult(true); };
        vm.EnterInterior = (_, name, _) => { entered.Add(name); map = 999; return Task.FromResult(true); };
        Assert.Equal((501, 701), await vm.PrepareWorkshopAsync(101, 10, CancellationToken.None));
        Assert.Equal(["Atelier des forgemages"], entered);
    }

    [Fact]
    public async Task Hint_map_without_the_profession_skill_enters_before_the_forge_is_usable()
    {
        var vm = Model();
        long map = 100;
        var entered = new List<string>();
        vm.CurrentMap = _ => map;
        vm.InteractivesForPid = _ => map == WorkshopMap
            ? [new MapInteractive(332, 522940, 158, 0, [new InteractiveSkill(1, 84, 0, true)], "Atelier des forgemages")]
            : map == 999 ? [Interactive(113)] : [];
        vm.Travel = (_, target, _) =>
        {
            Assert.Equal(WorkshopMap, target);
            map = target;
            return Task.FromResult(true);
        };
        vm.EnterInterior = (_, name, _) =>
        {
            entered.Add(name);
            map = 999;
            return Task.FromResult(true);
        };
        Assert.Equal((501, 701), await vm.PrepareWorkshopAsync(101, 10, CancellationToken.None));
        Assert.Equal(["Atelier des forgemages"], entered);
    }

    [Fact]
    public async Task Door_that_does_not_open_the_profession_workshop_passes_no_rune()
    {
        var vm = Model();
        long map = 100;
        vm.CurrentMap = _ => map;
        vm.InteractivesForPid = _ => map == WorkshopMap
            ? [new MapInteractive(332, 522940, 158, 0, [new InteractiveSkill(1, 84, 0, true)], Place.Name)]
            : [];
        vm.Travel = (_, target, _) => { map = target; return Task.FromResult(true); };
        vm.EnterInterior = (_, _, _) => Task.FromResult(false);
        Assert.Null(await vm.PrepareWorkshopAsync(101, 10, CancellationToken.None));
    }

    [Fact]
    public async Task Machine_is_approached_before_it_is_used_and_a_failed_approach_passes_no_rune()
    {
        var vm = Model();
        vm.InteractivesForPid = _ => [Interactive(113, 900)];
        var approached = new List<(int, int)>();
        vm.ApproachWorkshop = (_, element, skill, _) => { approached.Add((element, skill)); return Task.FromResult(true); };
        Assert.Equal((501, 900), await vm.PrepareWorkshopAsync(101, 10, CancellationToken.None));
        Assert.Equal([(501, 900)], approached);
        vm.ApproachWorkshop = (_, _, _, _) => Task.FromResult(false);
        Assert.Null(await vm.PrepareWorkshopAsync(101, 10, CancellationToken.None));
    }
}
