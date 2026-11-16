extern alias F06App;

using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Engine.Accounts;
using GivreClone.Engine.Proxies;
using Xunit;
using AccountsVm = F06App::GivreClone.App.ViewModels.AccountsViewModel;
using Row = F06App::GivreClone.App.ViewModels.AccountRow;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using WorldStore = F06App::GivreClone.App.Services.BotWorldStore;
using Snapshot = F06App::GivreClone.App.Services.Supervisor.BotSnapshot;

namespace GivreClone.Tests;

public sealed class BotDirectoryMapRegressionTests
{
    [Theory]
    [InlineData(162791424, 0, 0)]
    [InlineData(90703360, 13, 24)]
    [InlineData(0, null, null)]
    public void Live_world_map_and_coordinates_override_stale_account_row(long currentMap, int? x, int? y)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var launch = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (var field in typeof(Launch).GetFields(hidden).Where(f => f.FieldType.IsGenericType
            && f.FieldType.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>)))
            field.SetValue(launch, Activator.CreateInstance(field.FieldType));
        var world = new WorldStore(launch);
        var state = world.Get(0x40000001);
        state.MapId = currentMap;
        state.MapX = x; state.MapY = y;
        var row = new Row(new Account { Id = "offline-map-fixture", Login = "fixture", Mode = ConnectionMode.Socket },
            new ObservableCollection<ProxyEntry>(), "fixture")
        {
            Pid = 0x40000001, Running = true, CharacterName = "fixture",
            MapId = 88081688, MapCoords = "stale"
        };
        var accounts = (AccountsVm)RuntimeHelpers.GetUninitializedObject(typeof(AccountsVm));
        typeof(AccountsVm).GetField("<Accounts>k__BackingField", hidden)!.SetValue(accounts,
            new ObservableCollection<Row> { row });
        var directoryType = typeof(Launch).Assembly.GetType("GivreClone.App.Services.Supervisor.AppBotDirectory")!;
        var directory = directoryType.GetConstructor(hidden, null,
            [typeof(AccountsVm), typeof(WorldStore), typeof(Launch)], null)!.Invoke([accounts, world, launch]);

        directoryType.GetMethod("Refresh")!.Invoke(directory, null);

        var snapshot = (Snapshot)directoryType.GetMethod("ByAccountId")!.Invoke(directory, ["offline-map-fixture"])!;
        Assert.Equal(currentMap, snapshot.MapId);
        Assert.Equal(x is not null && y is not null ? $"{x}, {y}" : "", snapshot.Coords);
        Assert.Equal(88081688, row.MapId); // The stale UI row must not decide a current map snapshot.
    }
}
