extern alias F06App;
using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Packet = F06App::GivreClone.App.Services.DecodedPacket;
using Config = F06App::GivreClone.App.Services.AppConfig;
using HdvVm = F06App::GivreClone.App.ViewModels.HdvViewModel;
using SellRow = F06App::GivreClone.App.ViewModels.HdvSellRow;

namespace GivreClone.Tests;

[Collection("F08InventoryMapping")]
public sealed class QaHdvTradeBankTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static FieldInfo Field(Type type, string name) => type.GetField(name, Hidden)!;
    private static IDictionary State(Launch s, string name) => (IDictionary)Field(typeof(Launch), name).GetValue(s)!;
    private static object? Call(Launch s, string name, params object[] args) => typeof(Launch).GetMethod(name, Hidden)!.Invoke(s, args);

    private static Launch Service()
    {
        var s = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (var name in new[] { "<Inventory>k__BackingField", "<Bank>k__BackingField", "_itemEffects",
            "_storageOpenMap", "_lastMap", "_socketInjectors", "_charLevel", "_codeWait", "_lastAction",
            "_lastNpcDialog", "_bankKamas", "_lastPods" })
        {
            var f = Field(typeof(Launch), name);
            f.SetValue(s, Activator.CreateInstance(f.FieldType));
        }
        Field(typeof(Launch), "_catalog").SetValue(s, Catalog);
        State(s, "_lastMap")[9u] = 123L;
        State(s, "_storageOpenMap")[9u] = 123L;
        return s;
    }

    private static void Bind(Launch s, int uid, int effect)
        => State(s, "_itemEffects")[(9u, uid)] = new InventoryReader.ItemEffect[] { new(effect, 0, 0, 0, 0, null, 0) };

    private static IReadOnlyList<ProtoField> Payload(byte[] packet, string name)
    {
        var frames = RawProtobuf.ConsumeFrames(packet, out var rest);
        Assert.Empty(rest);
        var hit = Assert.Single(RawProtobuf.FindTypeUrls(RawProtobuf.Parse(Assert.Single(frames))));
        Assert.Equal(Catalog.CodeForName(name), hit.Code);
        return hit.Payload!;
    }

    private static int[] DepositUids(byte[] packet) => InventoryReader.ReadUids(
        Payload(packet, "ExchangeObjectTransferListFromInventoryRequest"),
        Catalog.FieldNumber("ExchangeObjectTransferListFromInventoryRequest", "objects_uid")!.Value).Order().ToArray();

    [Fact]
    public async Task Achievement_claim_ignores_another_reward_before_its_own_rejection()
    {
        var service = Service();
        var lot = Field(typeof(Launch), "_lotM");
        lot.SetValue(service, Activator.CreateInstance(lot.FieldType));
        int claims = 0;
        State(service, "_socketInjectors")[9u] = (Func<byte[], bool>)(packet =>
        {
            Payload(packet, "AchievementRewardRequest");
            claims++;
            foreach (var (id, success) in new[] { (88, true), (77, false) })
            {
                var response = new Packet(9, false, "", "AchievementRewardResultEvent",
                    new ProtoField[]
                    {
                        new() { Field = Catalog.FieldNumber("AchievementRewardResultEvent", "achievement_id")!.Value,
                            Wire = WireType.Varint, Varint = (ulong)id },
                        new() { Field = Catalog.FieldNumber("AchievementRewardResultEvent", "success")!.Value,
                            Wire = WireType.Varint, Varint = success ? 1UL : 0UL },
                    }, []);
                var waiter = (ITuple)State(service, "_codeWait")[9u]!;
                var accept = (Func<Packet, bool>?)waiter[1];
                if (accept?.Invoke(response) ?? true)
                    ((TaskCompletionSource<Packet>)waiter[2]!).TrySetResult(response);
            }
            return true;
        });

        Assert.False(await service.AchievementClaimAsync(9, 77, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, claims);
        Assert.Empty(State(service, "_codeWait"));
    }

    [Fact]
    public async Task One_fresh_buyer_search_returns_all_lot_prices_without_researching_each_size()
    {
        var service = Service();
        foreach (string name in new[] { "<Hdv>k__BackingField", "_hdvBuyerOpen", "_hdvBuyerType", "_gameNames",
            "_hdvAnyOpen", "_hdvBuyerSearchLocks", "_serverInfo", "_charId",
            "_linkDown", "_returnWait" })
        {
            var field = Field(typeof(Launch), name);
            field.SetValue(service, Activator.CreateInstance(field.FieldType));
        }
        State(service, "_hdvBuyerOpen")[9u] = true;
        // The real V9 search boundary requires a current seller/buyer-mode
        // session, character, server and LOCAL decoder lifetime. This is
        // test-only fixture provenance; it does not synthesize a server ACK.
        State(service, "_hdvAnyOpen")[9u] = true;
        State(service, "_serverInfo")[9u] = (292, "Orukam");
        State(service, "_charId")[9u] = 99100L;
        var decoder = new F06App::GivreClone.App.Services.CaptureDecoder(Catalog);
        decoder.Feed(9u, false, Array.Empty<byte>());
        Field(typeof(Launch), "_decoder").SetValue(service, decoder);
        int followedSearches = 0;
        State(service, "_socketInjectors")[9u] = (Func<byte[], bool>)(packet =>
        {
            var payload = Payload(packet, "ExchangeBidHouseSearchRequest");
            int follow = Catalog.FieldNumber("ExchangeBidHouseSearchRequest", "follow")!.Value;
            if (PayloadReader.Int(payload, follow, 0) == 0) return true;
            followedSearches++;
            service.Hdv.SetOffer(9, 1519, 88, [50, 480, 4500]);
            ((Action<uint>?)Field(typeof(Launch), "HdvUpdated").GetValue(service))?.Invoke(9);
            var waiter = (ITuple)State(service, "_codeWait")[9u]!;
            ((TaskCompletionSource<Packet>)waiter[2]!).TrySetResult(
                new(9, false, "", "ExchangeTypesItemsExchangerDescriptionForUserEvent", [], []));
            return true;
        });

        var prices = await service.HdvGetBuyerLotPricesAsync(9, 1519)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new long[] { 50, 480, 4500 }, prices);
        Assert.Equal(1, followedSearches);
        prices[0] = 1;
        Assert.Equal(50, service.Hdv.OfferPriceFor(9, 1519, 0));
    }

    [Fact]
    public void All_character_bound_items_produce_no_deposit_packet()
    {
        var s = Service();
        s.Inventory.Seed(9, [new(10, 100, 5)]);
        s.Bank.Seed(9, [new(20, 100, 5)]);
        Bind(s, 10, 981);
        Assert.Empty((byte[])Call(s, "DepositAllPacket", 9u)!);
    }

    [Fact]
    public void All_deposit_filters_character_binding_but_keeps_account_binding_and_excludes_equipment()
    {
        var s = Service();
        s.Inventory.Seed(9, [new(10, 100, 5), new(11, 100, 6), new(12, 200, 7), new(13, 300, 1, 0)]);
        Bind(s, 10, 981);
        Bind(s, 11, 982);
        Assert.Equal(new[] { 11, 12 }, DepositUids((byte[])Call(s, "DepositAllPacket", 9u)!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Existing_deposit_filters_bound_instances_and_only_deposits_existing_gids(bool configured)
    {
        var s = Service();
        s.Inventory.Seed(9, [new(10, 100, 5), new(11, 100, 6), new(12, 200, 7), new(13, 300, 1, 0)]);
        s.Bank.Seed(9, [new(20, 100, 50), new(21, 300, 50)]);
        Bind(s, 10, 981);
        Bind(s, 11, 982);
        byte[]? sent = null;
        State(s, "_socketInjectors")[9u] = (Func<byte[], bool>)(packet => { sent = packet; return true; });
        string previous = Config.Current.BankDeposit;
        try
        {
            Config.Current.BankDeposit = "existing";
            if (configured) Assert.True(s.BankDepositConfigured(9));
            else Assert.True(s.BankDepositExisting(9));
            Assert.Equal(new[] { 11 }, DepositUids(Assert.IsType<byte[]>(sent)));
        }
        finally { Config.Current.BankDeposit = previous; }
    }

    [Fact]
    public async Task Deposit_all_receipt_does_not_wait_for_character_bound_items_that_were_not_sent()
    {
        var s = Service();
        s.Inventory.Seed(9, [new(10, 100, 5), new(11, 100, 6)]);
        Bind(s, 10, 981);
        State(s, "_socketInjectors")[9u] = (Func<byte[], bool>)(packet =>
        {
            var hit = RawProtobuf.FindTypeUrls(RawProtobuf.Parse(RawProtobuf.ConsumeFrames(packet, out _).Single())).Single();
            if (hit.Code == Catalog.CodeForName("DialogLeaveRequest"))
            {
                var waiter = (ITuple)State(s, "_codeWait")[9u]!;
                ((TaskCompletionSource<Packet>)waiter[2]!).TrySetResult(new(9, false, "", "ExchangeLeaveEvent", [], []));
            }
            else
            {
                var deleted = new ProtoField { Field = Catalog.FieldNumber("ObjectDeletedEvent", "object_uid")!.Value,
                    Wire = WireType.Varint, Varint = 11 };
                ((Action<Packet>?)Field(typeof(Launch), "PacketDecoded").GetValue(s))?.Invoke(
                    new(9, false, "", "ObjectDeletedEvent", [deleted], []));
            }
            return true;
        });
        string previous = Config.Current.BankDeposit;
        try
        {
            Config.Current.BankDeposit = "all";
            Assert.True(await s.BankDepositAllAsync(9, 123).WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { Config.Current.BankDeposit = previous; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_bulk_deposit_never_dispatches_an_inventory_mutation(bool except)
    {
        var s = new GivreClone.App.Services.LaunchService();
        s.Inventory.Seed(9, [new(10, 100, 5)]);
        s.OpenBank(9);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        if (except)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.BankDepositAllExceptAsync(9, [], stop.Token));
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.BankDepositAllAsync(9, 123, stop.Token));
        Assert.Equal(0, s.Relay.Sends);
    }

    [Fact]
    public void Modal_checked_count_only_includes_current_eligible_checked_rows()
    {
        var vm = (HdvVm)RuntimeHelpers.GetUninitializedObject(typeof(HdvVm));
        Field(typeof(HdvVm), "_checkedGids").SetValue(vm, new HashSet<long> { 100, 200, 300 });
        Field(typeof(HdvVm), "<SellRows>k__BackingField").SetValue(vm,
            new ObservableCollection<SellRow> { new() { Gid = 100, Checked = true }, new() { Gid = 400 } });
        Assert.Equal(1, vm.CheckedCount);
        Assert.True(vm.HasChecked);
        vm.SellRows.Clear();
        Assert.Equal(0, vm.CheckedCount);
        Assert.False(vm.HasChecked);
    }
}
