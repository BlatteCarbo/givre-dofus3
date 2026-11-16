extern alias F06App;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreCore.Protobuf;
using GivreClone.Protocol;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using Packet = F06App::GivreClone.App.Services.DecodedPacket;
using Xunit;

namespace GivreClone.Tests;

[Collection("F08InventoryMapping")]
public sealed class TradeSettlementRegressionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IDictionary State(LaunchService s, string name) => (IDictionary)typeof(LaunchService).GetField(name, Hidden)!.GetValue(s)!;
    private static LaunchService Service()
    {
        var s = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        foreach (var name in new[] { "_tradeOpen", "_tradeStartedPkt", "_tradeExpected", "_tradeStep", "_lastMap", "_tradeAutoTeam", "_tradePlaced", "_tradeContent" })
        {
            var f = typeof(LaunchService).GetField(name, Hidden)!;
            f.SetValue(s, Activator.CreateInstance(f.FieldType));
        }
        typeof(LaunchService).GetField("_catalog", Hidden)!.SetValue(s, ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data")));
        State(s, "_tradeOpen")[(uint)9] = (42L, 123L);
        State(s, "_lastMap")[(uint)9] = 123L;
        State(s, "_tradeStartedPkt")[(uint)9] = new Packet(9, false, "fixture", "ExchangeStartedWithPodsEvent", Array.Empty<ProtoField>(), []);
        return s;
    }
    private static bool Settled(LaunchService s) => (bool)typeof(LaunchService).GetMethod("SettleTradeStep", Hidden)!.Invoke(s, [(uint)9, 0])!;

    [Theory]
    [InlineData(2, 0, false)]
    [InlineData(2, 1, false)]
    [InlineData(2, 2, true)]
    [InlineData(0, 0, true)]
    public void Only_fully_receipted_local_intentions_can_be_validated_after_the_deadline(int expected, int confirmed, bool allowed)
    {
        var s = Service();
        State(s, "_tradeExpected")[(uint)9] = Enumerable.Range(0, Math.Max(0, expected - confirmed)).Select(_ => (Func<Packet, bool>)(_ => false)).ToList();
        State(s, "_tradeStep")[(uint)9] = confirmed;
        Assert.Equal(allowed, Settled(s));
    }
    [Fact]
    public void Closure_map_change_or_missing_session_cannot_authorize_validation()
    {
        var s = Service(); State(s, "_tradeOpen").Clear(); Assert.False(Settled(s));
        s = Service(); State(s, "_lastMap")[(uint)9] = 124L; Assert.False(Settled(s));
        s = Service(); State(s, "_tradeStartedPkt").Clear(); Assert.False(Settled(s));
    }
    [Fact]
    public void Delayed_auto_ready_requires_enabled_option_and_the_original_exchange()
    {
        var s = Service();
        var original = State(s, "_tradeStartedPkt")[(uint)9];
        bool Allowed() => (bool)typeof(LaunchService).GetMethod("CanAutoReadyTrade", Hidden)!.Invoke(s, [(uint)9, original])!;
        State(s, "_tradeAutoTeam")[(uint)9] = true;
        Assert.True(Allowed());
        State(s, "_tradeAutoTeam")[(uint)9] = false; // auto-accept remains enabled, auto-ready disabled
        Assert.False(Allowed());
        State(s, "_tradeAutoTeam")[(uint)9] = true;
        State(s, "_tradeStartedPkt")[(uint)9] = new Packet(9, false, "fixture", "ExchangeStartedWithPodsEvent", Array.Empty<ProtoField>(), []);
        Assert.False(Allowed());
    }

    private static object? Call(LaunchService s, string method, params object?[] args)
        => typeof(LaunchService).GetMethod(method, Hidden)!.Invoke(s, args);

    [Fact]
    public void Remote_wire_steps_never_settle_a_missing_local_receipt()
    {
        var s = Service();
        State(s, "_tradeExpected")[(uint)9] = new List<Func<Packet, bool>> { _ => false };
        State(s, "_tradeStep")[(uint)9] = 100;
        Assert.False(Settled(s));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Receipt_is_reserved_before_synchronous_injection_and_failed_send_rolls_back(bool success)
    {
        var s = Service();
        Func<Packet, bool> match = p => p.Name == "local-receipt";
        bool sent = (bool)Call(s, "SendTradeMutation", (uint)9, State(s, "_tradeStartedPkt")[(uint)9], match, (Func<bool>)(() =>
        {
            Assert.False(Settled(s));
            if (success) Call(s, "ConfirmTradeMutation", new Packet(9, false, "fixture", "local-receipt", [], []));
            return success;
        }))!;
        Assert.Equal(success, sent);
        Assert.True(Settled(s));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authorization_changed_during_actual_settlement_blocks_dispatch(bool replace)
    {
        var s = Service();
        var original = State(s, "_tradeStartedPkt")[(uint)9];
        State(s, "_tradeAutoTeam")[(uint)9] = true;
        var pending = new List<Func<Packet, bool>> { _ => false };
        State(s, "_tradeExpected")[(uint)9] = pending;
        int sends = 0;
        Thread? worker = null;
        var result = Task.Run(() =>
        {
            Volatile.Write(ref worker, Thread.CurrentThread);
            return (bool)Call(s, "SettleAndSendTradeReady", (uint)9, original, true,
                (Func<bool>)(() => { sends++; return true; }))!;
        });
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref worker) is { } thread
            && (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, 1000));
        // The pending intention forces the real settlement loop to wait. The same lock
        // as production orders option/session replacement before releasing its receipt.
        lock (State(s, "_tradeStartedPkt"))
        {
            if (replace) State(s, "_tradeStartedPkt")[(uint)9] = new Packet(9, false, "fixture", "ExchangeStartedWithPodsEvent", [], []);
            else State(s, "_tradeAutoTeam")[(uint)9] = false;
            pending.Clear();
        }
        Assert.False(await result);
        Assert.Equal(0, sends);
    }

    private static LaunchService WaiterService()
    {
        var s = Service();
        var field = typeof(LaunchService).GetField("_codeWait", Hidden)!;
        field.SetValue(s, Activator.CreateInstance(field.FieldType));
        return s;
    }

    [Fact]
    public async Task Real_waiter_does_not_dispatch_when_already_cancelled_and_releases_ownership()
    {
        var s = WaiterService();
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        int sends = 0;
        Assert.Null(await s.SendAndWaitForCodeAsync(9, () => { sends++; return true; }, ["ack"], 100, ct.Token));
        Assert.Equal(0, sends);
        Assert.Empty(State(s, "_codeWait"));
    }

    [Fact]
    public async Task Real_waiter_preserves_concurrent_owner_and_accepts_synchronous_receipt()
    {
        var s = WaiterService();
        using var cancellation = new CancellationTokenSource();
        var owner = s.SendAndWaitForCodeAsync(9, () => true, ["ack"], 10000, cancellation.Token);
        int competingSends = 0;
        Assert.Null(await s.SendAndWaitForCodeAsync(9, () => { competingSends++; return true; }, ["ack"], 100));
        Assert.Equal(0, competingSends);
        Assert.Single(State(s, "_codeWait"));
        cancellation.Cancel();
        Assert.Null(await owner);
        Assert.Empty(State(s, "_codeWait"));
        var receipt = new Packet(9, false, "fixture", "ack", [], []);
        Assert.Same(receipt, await s.SendAndWaitForCodeAsync(9, () =>
        {
            var entry = (ITuple)State(s, "_codeWait")[(uint)9]!;
            ((TaskCompletionSource<Packet>)entry[2]!).SetResult(receipt);
            return true;
        }, ["ack"], 100));
        Assert.Empty(State(s, "_codeWait"));
    }

    [Fact]
    public void Actual_remote_packet_does_not_confirm_a_local_item_intention()
    {
        var s = Service();
        var cat = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        int F(string m, string f) => cat.FieldNumber(m, f)!.Value;
        ProtoField V(int f, int v) => new() { Field = f, Wire = WireType.Varint, Varint = (ulong)v };
        ProtoField M(int f, params ProtoField[] v) => new() { Field = f, Wire = WireType.Len, Message = v.ToList() };
        Packet Receipt(bool remote) => new(9, false, "fixture", "ExchangeObjectsAddedEvent",
            [V(F("ExchangeObjectsAddedEvent", "remote"), remote ? 1 : 0),
             M(F("ExchangeObjectsAddedEvent", "objects"), M(F("ObjectItemInventory", "item"),
                V(F("ObjectItem", "uid"), 10), V(F("ObjectItem", "gid"), 100), V(F("ObjectItem", "quantity"), 2)))], []);
        Assert.True((bool)Call(s, "SendTradeItemMutation", (uint)9, State(s, "_tradeStartedPkt")[(uint)9], 10, 2, (Func<bool>)(() => true))!);
        Call(s, "OnExchangePacket", Receipt(true));
        Assert.Equal(1, State(s, "_tradeStep")[(uint)9]);
        Assert.False(Settled(s));
        Call(s, "OnExchangePacket", Receipt(false));
        Assert.True(Settled(s));
    }

    [Fact]
    public void Overlapping_mutation_is_refused_and_manual_unready_remains_possible()
    {
        var s = Service(); var session = State(s, "_tradeStartedPkt")[(uint)9];
        Func<Packet, bool> receipt = _ => false;
        Assert.True((bool)Call(s, "SendTradeMutation", (uint)9, session, receipt, (Func<bool>)(() => true))!);
        int sends = 0;
        Assert.False((bool)Call(s, "SendTradeMutation", (uint)9, session, receipt, (Func<bool>)(() => { sends++; return true; }))!);
        Assert.Equal(0, sends);
        Assert.True((bool)Call(s, "SendTradeReady", (uint)9, session, false, (Func<bool>)(() => true), false)!);
        Assert.False(Settled(s));
    }

    [Fact]
    public void Late_packet_on_another_map_cannot_confirm_pending_content()
    {
        var s = Service();
        var pending = new List<Func<Packet, bool>> { _ => true };
        State(s, "_tradeExpected")[(uint)9] = pending;
        State(s, "_lastMap")[(uint)9] = 124L;
        Call(s, "OnExchangePacket", new Packet(9, false, "fixture", "ExchangeKamaModifiedEvent", [], []));
        Assert.Single(pending);
        Assert.Empty(State(s, "_tradeStep"));
    }
    [Fact]
    public void Malformed_receipt_predicate_is_left_unconfirmed_without_breaking_decoder()
    {
        var s = Service();
        var pending = new List<Func<Packet, bool>> { _ => throw new FormatException("malformed fixture") };
        State(s, "_tradeExpected")[(uint)9] = pending;
        Call(s, "ConfirmTradeMutation", new Packet(9, false, "fixture", "ExchangeKamaModifiedEvent", [], []));
        Assert.Single(pending);
        Assert.False(Settled(s));
    }

}
