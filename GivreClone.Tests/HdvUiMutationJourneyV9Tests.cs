extern alias F06App;

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using HdvVm = F06App::GivreClone.App.ViewModels.HdvViewModel;
using HdvRow = F06App::GivreClone.App.ViewModels.HdvListingRow;
using Listing = F06App::GivreClone.App.Services.BidHouseStore.Listing;

namespace GivreClone.Tests;

/// <summary>
/// Exercises the genuine manual HDV UI admission check and the existing
/// RemoveListingCore asynchronous dispatch boundary without a game client.
/// An offer UID or identical display name across characters is NOT an
/// identity proof, and no local click is a server receipt.
/// </summary>
public sealed class HdvUiMutationJourneyV9Tests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type TicketType = typeof(HdvVm)
        .GetNestedType("HdvUiMutationTicket", BindingFlags.NonPublic)!;
    private static readonly Type IntentType = typeof(HdvVm)
        .GetNestedType("HdvListingIntent", BindingFlags.NonPublic)!;

    private static (Launch Launch, HdvVm Vm) NewModel(uint pid = 391)
    {
        var launch = new Launch();
        var vm = (HdvVm)RuntimeHelpers.GetUninitializedObject(typeof(HdvVm));
        typeof(HdvVm).GetField("_launch", Private)!.SetValue(vm, launch);
        typeof(HdvVm).GetField("_pid", Private)!.SetValue(vm, pid);
        Seed(launch, pid, 292, 99100);
        return (launch, vm);
    }

    private static void Seed(Launch launch, uint pid, int server, long actor)
    {
        var serverTable = (ConcurrentDictionary<uint, (int Id, string Name)>)
            typeof(Launch).GetField("_serverInfo", Private)!.GetValue(launch)!;
        var actorTable = (ConcurrentDictionary<uint, long>)
            typeof(Launch).GetField("_charId", Private)!.GetValue(launch)!;
        serverTable[pid] = (server, "same-looking-character-name");
        actorTable[pid] = actor;
    }

    private static object Ticket(HdvVm vm) =>
        typeof(HdvVm).GetMethod("CaptureHdvUiMutationTicket", Private)!
            .Invoke(vm, null)!;

    private static bool Current(HdvVm vm, object ticket) =>
        (bool)typeof(HdvVm).GetMethod("IsCurrentHdvUiMutationTicket", Private)!
            .Invoke(vm, [ticket])!;

    private static object Intent(int uid, long gid, int qty, long price) =>
        Activator.CreateInstance(IntentType, [uid, gid, qty, price])!;

    private static bool ValidListing(HdvVm vm, object ticket, object intent) =>
        (bool)typeof(HdvVm).GetMethod("ValidHdvListingIntent", Private)!
            .Invoke(vm, [ticket, intent])!;

    [Fact]
    public void Real_manual_preflight_accepts_exact_profile_and_observed_listing_snapshot()
    {
        var (launch, vm) = NewModel();
        launch.Hdv.SeedMine(391, [new Listing(981, 2416, 10, 3511)]);
        var ticket = Ticket(vm);
        Assert.Equal(TicketType, ticket.GetType());
        Assert.True(Current(vm, ticket));
        Assert.True(ValidListing(vm, ticket, Intent(981, 2416, 10, 3511)));
        Assert.False(ValidListing(vm, ticket, Intent(981, 2416, 10, 3510)));
        Assert.False(ValidListing(vm, ticket, Intent(981, 2416, 1, 3511)));
        Assert.False(ValidListing(vm, ticket, Intent(982, 2416, 10, 3511)));
        Assert.False(ValidListing(vm, ticket, Intent(981, 0, 10, 3511)));
    }

    [Fact]
    public void Same_pid_same_name_and_same_listing_uid_on_another_character_are_rejected()
    {
        var (launch, vm) = NewModel();
        launch.Hdv.SeedMine(391, [new Listing(981, 2416, 10, 3511)]);
        var first = Ticket(vm);
        var intent = Intent(981, 2416, 10, 3511);
        Assert.True(ValidListing(vm, first, intent));
        Seed(launch, 391, 292, 99200);
        Assert.False(Current(vm, first));
        Assert.False(ValidListing(vm, first, intent));
        Seed(launch, 391, 353, 99100);
        Assert.False(Current(vm, first));
        Seed(launch, 391, 292, 99100);
        typeof(HdvVm).GetField("_mutationEpoch", Private)!.SetValue(vm, 7);
        Assert.False(Current(vm, first));
    }

    [Fact]
    public void Missing_identity_and_switched_selected_pid_refuse_manual_admission()
    {
        var (launch, vm) = NewModel();
        var first = Ticket(vm);
        typeof(HdvVm).GetField("_pid", Private)!.SetValue(vm, 392u);
        Seed(launch, 392, 292, 99100);
        Assert.False(Current(vm, first));
        Seed(launch, 392, 0, 0);
        Assert.False(Current(vm, Ticket(vm)));
    }

    [Fact]
    public async Task Actual_async_remove_entry_never_reaches_transport_after_identity_replacement()
    {
        var (launch, vm) = NewModel();
        launch.Hdv.SeedMine(391, [new Listing(981, 2416, 10, 3511)]);
        var ticket = Ticket(vm);
        var intent = Intent(981, 2416, 10, 3511);
        var row = new HdvRow
        {
            Uid = 981, Gid = 2416, Qty = 10,
            Price = 3511, Name = "test"
        };
        Seed(launch, 391, 292, 99101);
        var action = typeof(HdvVm).GetMethod("RemoveListingCore", Private)!;
        var result = (Task)action.Invoke(vm, [row, intent, ticket])!;
        await result.WaitAsync(TimeSpan.FromSeconds(5));
        // The stale call must not even mutate the observed listing cache.
        Assert.Equal(3511, Assert.Single(launch.Hdv.MyListings(391)).Price);
        Assert.False(Current(vm, ticket));
    }
}
