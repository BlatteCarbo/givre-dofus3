extern alias F06App;

using System.Reflection;
using Microsoft.Data.Sqlite;
using GivreClone.Engine.Recovery;
using GivreClone.Protocol;
using Xunit;
using GameDb = F06App::GivreClone.App.Services.LotM.GameDb;
using SmithJobChoice = F06App::GivreClone.App.ViewModels.SmithJobChoice;
using SmithMagicViewModel = F06App::GivreClone.App.ViewModels.SmithMagicViewModel;
using SmithQueueRow = F06App::GivreClone.App.ViewModels.SmithQueueRow;

namespace GivreClone.Tests;

public sealed class SmithLevelingSessionTests : IDisposable
{
    private readonly string _crushDirectory = Path.Combine(Path.GetTempPath(), "givre-crusher-session-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_crushDirectory)) Directory.Delete(_crushDirectory, true); }

    private void SelectCrush(SmithMagicViewModel vm)
    {
        vm.CrushProvenance = new F06App::GivreClone.App.Services.SmithCrushProvenanceStore(
            Path.Combine(_crushDirectory, "provenance.json"));
        vm.CrushOwnerOf = _ => new("test-crushing", 1, 2);
        vm.CrushHistoryHeadOf = (_, _) => 0;
        vm.BindingOf = (_, _) => ItemBind.None;
        vm.IncludeOtherCrushItems = true;
        vm.Picks.Add(new() { ObjectUid = 7, Gid = 30, Name = "Anneau", Quantity = 2,
            InBag = true, HasSlot = true, IsChecked = true, CrushQuantity = 2 });
        vm.PickCrushCommand.Execute(null);
        vm.AddCheckedToCrushCommand.Execute(null);
    }
    private static SmithMagicViewModel Model()
    {
        var vm = new SmithMagicViewModel
        {
            AdaptiveLeveling = false,
            RequiredSkill = _ => 113,
            WorkshopPlaces = _ => [],
            CurrentMap = _ => 100,
            InteractivesForPid = _ => [],
            Travel = (_, _, _) => throw new InvalidOperationException("travel"),
            ItemLevelOf = gid => (int)gid,
            JobForItem = gid => gid == 2 ? 48 : 44,
            JobLevelOf = (_, job) => job == 44 ? 35 : null,
            StillInBag = (_, _) => true,
            RuneQuantities = (_, _) => (4, 6, true),
            LevelingRuneName = _ => throw new InvalidOperationException("rune name"),
            JobTitle = id => "Job " + id,
            CrushSkill = () => 181,
            CrusherPlaces = _ => [],
            CrushItems = (_, _, _, _, _) => throw new InvalidOperationException("crush"),
            CrushQuantityOf = (_, _) => 2,
            CrushGidOf = (_, _) => 30,
        };
        SetPid(vm, 101);
        vm.Leveling = true;
        vm.TargetJobLevel = 50;
        vm.LevelingRune = new SmithRune(10, 111, SmithRuneTier.Basic, 4, 0, 4, 6);
        vm.SelectedJob = new SmithJobChoice(44, "Joaillomage", 35);
        return vm;
    }

    private static void SetPid(SmithMagicViewModel vm, int pid)
        => typeof(SmithMagicViewModel).GetField("_pid", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, pid);

    private static async Task Run(SmithMagicViewModel vm) => await vm.StartCommand.ExecuteAsync(null);

    [Fact]
    public async Task Unknown_profession_level_does_not_travel_or_pass_a_rune()
    {
        var vm = Model();
        vm.JobLevelOf = (_, _) => null;
        vm.Queue.Add(new SmithQueueRow(1, "Anneau", "x1", 30, 1));
        await Run(vm);
        Assert.Contains(vm.SmithConsole, line => line.Contains("aucun trajet"));
    }

    [Fact]
    public async Task Profession_already_at_the_target_does_not_travel()
    {
        var vm = Model();
        vm.JobLevelOf = (_, _) => 50;
        vm.Queue.Add(new SmithQueueRow(1, "Anneau", "x1", 30, 1));
        await Run(vm);
        Assert.Contains(vm.SmithConsole, line => line.Contains("deja au niveau") || line.Contains("déjà au niveau"));
    }

    [Fact]
    public async Task Item_above_the_profession_is_skipped_and_does_not_open_a_workshop()
    {
        var vm = Model();
        vm.ItemLevelOf = _ => 40;
        vm.JobForItem = _ => 44;
        vm.Queue.Add(new SmithQueueRow(1, "Anneau", "x1", 40, 1));
        await Run(vm);
        Assert.Contains(vm.SmithConsole, line => line.Contains("40") && line.Contains("35"));
        Assert.Contains(vm.SmithConsole, line => line.Contains("Aucun objet forgeable"));
    }

    [Fact]
    public async Task Missing_workshop_stops_before_a_rune_name_or_forge_call()
    {
        var vm = Model();
        vm.Queue.Add(new SmithQueueRow(2, "Ceinture", "x1", 48, 1));
        vm.Queue.Add(new SmithQueueRow(1, "Anneau", "x1", 30, 1));
        await Run(vm);
        Assert.Contains(vm.SmithConsole, line => line.Contains("Ceinture"));
        Assert.Contains(vm.SmithConsole, line => line.Contains("Aucun atelier"));
    }

    [Fact]
    public async Task Empty_rune_stock_stops_before_travel_when_buying_is_off()
    {
        var vm = Model();
        vm.RuneQuantities = (_, _) => (0, 0, true);
        vm.Queue.Add(new SmithQueueRow(1, "Anneau", "x1", 30, 1));
        await Run(vm);
        Assert.Contains(vm.SmithConsole, line => line.Contains("absente du sac"));
    }

    [Fact]
    public async Task Objective_mode_skips_an_item_above_the_profession_without_travel()
    {
        var vm = Model();
        vm.Leveling = false;
        vm.ItemLevelOf = gid => gid == 30 ? 30 : 40;
        vm.JobForItem = _ => 44;
        vm.JobLevelOf = (_, _) => 35;
        vm.Queue.Add(new SmithQueueRow(9, "Trop haut", "x1", 40, 1));
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Contains(vm.SmithConsole, line => line.Contains("Trop haut") && line.Contains("40"));
        Assert.DoesNotContain(vm.SmithConsole, line => line.Contains("atelier"));
    }

    [Fact]
    public async Task Crush_without_a_known_crusher_does_not_break_anything()
    {
        var vm = Model();
        vm.Leveling = false;
        SelectCrush(vm);
        await vm.StartCrushCommand.ExecuteAsync(null);
        Assert.Single(vm.CrushQueue);
        Assert.Contains(vm.SmithConsole, line => line.Contains("aucun objet"));
    }

    [Fact]
    public async Task Closed_crusher_keeps_the_list()
    {
        var vm = Model();
        vm.Leveling = false;
        vm.InteractivesForPid = _ => [new MapInteractive(100, 501, 0, 0, [new(701, 181, 0, true)])];
        vm.CrushItems = (_, _, _, items, _) => Task.FromResult(new F06App::GivreClone.App.Services.LaunchService.CrushRunReport(false, 0, Array.Empty<int>(), items.Count, "concasseur non ouvert"));
        SelectCrush(vm);
        await vm.StartCrushCommand.ExecuteAsync(null);
        Assert.Single(vm.CrushQueue);
        Assert.Contains(vm.SmithConsole, line => line.Contains("non ouvert"));
    }

    [Fact]
    public void Game_rows_resolve_level_job_slot_resource_and_crush_skill()
    {
        using var fake = new FakeGameDb();
        using (var connection = new SqliteConnection($"Data Source={fake.File}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                alter table ItemData add column typeId integer;
                update ItemData set typeId = case id when 100 then 6 else 9 end;
                create table ItemTypeData (id integer, superTypeId integer);
                create table ItemSuperTypeData (id integer, possiblePositions text);
                insert into ItemTypeData values (6, 2), (9, 6);
                insert into ItemSuperTypeData values (2, '[1]'), (6, '[]');
                alter table SkillData add column parentJobId integer;
                insert into SkillData(id, nameId, gatheredRessourceItem, parentJobId) values (113, 113, -1, 44), (181, 181, -1, 1);
                insert into translations(id, value, lang) values ('181', 'Briser des objets', 'fr');
                """;
            command.ExecuteNonQuery();
        }
        using var db = GameDb.TryOpen(fake.File);
        Assert.NotNull(db);
        Assert.Equal(1, SmithMagicViewModel.ItemLevel(db, 100));
        Assert.Equal(10, SmithMagicViewModel.ItemLevel(db, 102));
        Assert.Null(SmithMagicViewModel.ItemLevel(db, 999));
        Assert.Equal(44, SmithMagicViewModel.JobForSkill(db, 113));
        Assert.Equal(181, SmithMagicViewModel.CrushSkillId(db));
        Assert.True(SmithMagicViewModel.HasEquipmentSlot(db, 100));
        Assert.False(SmithMagicViewModel.HasEquipmentSlot(db, 101));
        Assert.True(SmithMagicViewModel.IsGatheredResource(db, 100));
        Assert.False(SmithMagicViewModel.IsGatheredResource(db, 101));
    }
}
