extern alias F06App;

using System.Reflection;
using BotHostService = F06App::GivreClone.App.Services.BotHostService;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// In-memory tests for the real clientless socket provenance/lease boundaries.
/// Reflection is used only because SocketGameClient and SocketProbe are
/// intentionally internal App implementations. No TCP, game, auth or build
/// process is started by these tests.
/// </summary>
public sealed class TransportSessionIntegrationTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Assembly App = typeof(BotHostService).Assembly;
    private static readonly Type SourceType = RequiredType(
        "GivreClone.App.Services.Transport.Socket.SocketLinkProvenance");
    private static readonly Type ObservedType = RequiredType(
        "GivreClone.App.Services.Transport.Socket.SocketTransportFrameObservation");
    private static readonly Type GameType = RequiredType(
        "GivreClone.App.Services.Transport.Socket.SocketGameClient");
    private static readonly Type ParamsType = RequiredType(
        "GivreClone.App.Services.Transport.Socket.SocketGameParams");
    private static readonly Type LeasesType = RequiredType(
        "GivreClone.App.Services.Transport.Socket.SocketSessionOwnership");

    private static Type RequiredType(string name) =>
        App.GetType(name, throwOnError: true)!;

    private static object Source(uint pid, long epoch = 1, Guid? client = null,
        Guid? token = null) =>
        Activator.CreateInstance(SourceType,
            [client ?? Guid.NewGuid(), epoch, token ?? Guid.NewGuid(), pid])!;

    private static Func<T, bool> AdaptPredicate<T>(Func<object, bool> predicate) =>
        value => predicate(value!);

    private static Action<T> AdaptObserver<T>(Action<object> observer) =>
        value => observer(value!);

    private static Delegate Predicate(Func<object, bool> predicate) =>
        (Delegate)typeof(TransportSessionIntegrationTests)
            .GetMethod(nameof(AdaptPredicate), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(SourceType).Invoke(null, [predicate])!;

    private static Delegate Observer(Action<object> callback) =>
        (Delegate)typeof(TransportSessionIntegrationTests)
            .GetMethod(nameof(AdaptObserver), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(ObservedType).Invoke(null, [callback])!;

    private static object NewOwnership() => Activator.CreateInstance(LeasesType, true)!;

    private static object Claim(object registry, uint pid, Action revoke,
        Func<object, bool> validate, Action<uint> unregister) =>
        LeasesType.GetMethod("Claim", Hidden)!
            .Invoke(registry, [pid, revoke, Predicate(validate), unregister])!;

    private static bool Register(object registry, object lease, Action action) =>
        (bool)LeasesType.GetMethod("TryRegister", Hidden)!
            .Invoke(registry, [lease, action])!;

    private static bool Release(object registry, object lease, Action<uint> unregister) =>
        (bool)LeasesType.GetMethod("Release", Hidden)!
            .Invoke(registry, [lease, unregister])!;

    private static bool Current(object registry, object source) =>
        (bool)LeasesType.GetMethod("IsCurrentSocketLink", Hidden)!
            .Invoke(registry, [source])!;

    private static object NewGame(uint pid) => Activator.CreateInstance(GameType, [pid])!;

    private static void Set(object subject, string field, object? value) =>
        subject.GetType().GetField(field, Hidden)!.SetValue(subject, value);

    private static bool IsCurrent(object game, object source) =>
        (bool)GameType.GetMethod("IsCurrentSocketLink")!
            .Invoke(game, [source])!;

    private static object NewParams(Delegate observer)
    {
        var parameters = Activator.CreateInstance(ParamsType, true)!;
        ParamsType.GetProperty("FeedObserved")!.SetValue(parameters, observer);
        return parameters;
    }

    private static void Publish(object game, object parameters, bool toServer, byte[] wire) =>
        GameType.GetMethod("PublishObserved", Hidden)!
            .Invoke(game, [parameters, toServer, wire]);

    [Fact]
    public void Old_cleanup_cannot_erase_new_injector_with_same_synthetic_pid()
    {
        var registry = NewOwnership();
        var oldSource = Source(7001);
        var nextSource = Source(7001, epoch: 2);
        var installed = new List<string>();
        int oldRevoked = 0, removed = 0;
        Action<uint> unregister = _ => Interlocked.Increment(ref removed);

        var first = Claim(registry, 7001, () => Interlocked.Increment(ref oldRevoked),
            value => Equals(value, oldSource), unregister);
        Assert.True(Register(registry, first, () => installed.Add("first")));
        Assert.True(Current(registry, oldSource));

        var successor = Claim(registry, 7001, () => { },
            value => Equals(value, nextSource), unregister);
        Assert.Equal(1, oldRevoked);
        Assert.Equal(1, removed); // first injector revoked on replacement
        Assert.False(Current(registry, oldSource));
        Assert.True(Current(registry, nextSource));
        Assert.False(Register(registry, first, () => installed.Add("stale")));
        Assert.True(Register(registry, successor, () => installed.Add("second")));
        Assert.False(Release(registry, first, unregister));
        Assert.Equal(1, removed); // old finally cannot remove the new injector
        Assert.Equal(new[] { "first", "second" }, installed);
        Assert.True(Release(registry, successor, unregister));
        Assert.Equal(2, removed);
        Assert.False(Current(registry, nextSource));
    }

    [Fact]
    public void Reentrant_old_revocation_cannot_register_after_new_lease_wins()
    {
        var registry = NewOwnership();
        object? old = null;
        int staleRegisters = 0;
        Action<uint> unregister = _ => { };
        var oldSource = Source(7002);
        old = Claim(registry, 7002,
            () =>
            {
                if (Register(registry, old!, () => Interlocked.Increment(ref staleRegisters)))
                    throw new InvalidOperationException("Stale lease regained control.");
            },
            value => Equals(value, oldSource), unregister);
        var nextSource = Source(7002, epoch: 4);
        var next = Claim(registry, 7002, () => { },
            value => Equals(value, nextSource), unregister);
        Assert.Equal(0, staleRegisters);
        Assert.True(Current(registry, nextSource));
        Assert.True(Release(registry, next, unregister));
    }

    [Fact]
    public void Separate_synthetic_pid_leases_do_not_revoke_one_another()
    {
        var registry = NewOwnership();
        var firstSource = Source(8001);
        var otherSource = Source(8002);
        int revocations = 0;
        Action<uint> unregister = _ => Interlocked.Increment(ref revocations);
        var one = Claim(registry, 8001, () => { },
            value => Equals(value, firstSource), unregister);
        var two = Claim(registry, 8002, () => { },
            value => Equals(value, otherSource), unregister);
        Assert.True(Register(registry, one, () => { }));
        Assert.True(Register(registry, two, () => { }));
        Assert.True(Release(registry, one, unregister));
        Assert.True(Current(registry, otherSource));
        Assert.False(Current(registry, firstSource));
        Assert.True(Release(registry, two, unregister));
        Assert.Equal(2, revocations);
    }

    [Fact]
    public void Source_equality_requires_instance_epoch_connection_and_pid_and_dies_on_stop()
    {
        var game = NewGame(9001);
        var source = Source(9001, epoch: 42);
        var differentPid = Source(9002, 42);
        using var lifetime = new CancellationTokenSource();
        Assert.False(IsCurrent(game, source)); // no DialAsync, no real lease
        Set(game, "_sessionCancellation", lifetime);
        Set(game, "_currentSource", source); // fixture models a completed local dial

        Assert.True(IsCurrent(game, source));
        Assert.False(IsCurrent(game, differentPid));
        Assert.False(IsCurrent(game, Source(9001, epoch: 43)));
        Assert.False(IsCurrent(game, Source(9001, epoch: 42)));
        var outArgs = new object?[] { null };
        Assert.True((bool)GameType.GetMethod("TryGetCurrentSocketLink")!
            .Invoke(game, outArgs)!);
        Assert.Equal(source, outArgs[0]);

        lifetime.Cancel(); // Stop/EOF-like cancellation
        Assert.False(IsCurrent(game, source));
        Assert.False((bool)GameType.GetMethod("TryGetCurrentSocketLink")!
            .Invoke(game, new object?[] { null })!);
    }

    [Fact]
    public void Typed_observer_keeps_wire_snapshot_and_monotonic_per_source_sequence()
    {
        var game = NewGame(9003);
        var source = Source(9003, 77);
        using var lifetime = new CancellationTokenSource();
        Set(game, "_sessionCancellation", lifetime);
        Set(game, "_currentSource", source);
        var received = new List<object>();
        var parameters = NewParams(Observer(received.Add));
        byte[] raw = [0x02, 0x08, 0x01];

        Publish(game, parameters, false, raw);
        raw[1] = 0xFF; // caller cannot mutate the observation
        Publish(game, parameters, false, [0x02, 0x08, 0x02]);
        Assert.Equal(2, received.Count);
        Assert.Equal(source, ObservedType.GetProperty("Source")!.GetValue(received[0]));
        Assert.Equal(1L, ObservedType.GetProperty("Sequence")!.GetValue(received[0]));
        Assert.Equal(2L, ObservedType.GetProperty("Sequence")!.GetValue(received[1]));
        Assert.False((bool)ObservedType.GetProperty("ClientToServer")!.GetValue(received[0])!);
        Assert.Equal(new byte[] { 0x02, 0x08, 0x01 },
            (byte[])ObservedType.GetProperty("WireFrame")!.GetValue(received[0])!);
    }

    [Fact]
    public void Reentrant_observation_is_queued_behind_the_current_callback()
    {
        var game = NewGame(9006);
        var source = Source(9006, 100);
        using var lifetime = new CancellationTokenSource();
        Set(game, "_sessionCancellation", lifetime);
        Set(game, "_currentSource", source);
        var order = new List<long>();
        object? parameters = null;
        parameters = NewParams(Observer(observed =>
        {
            long sequence = (long)ObservedType.GetProperty("Sequence")!.GetValue(observed)!;
            order.Add(sequence);
            if (sequence == 1)
                Publish(game, parameters!, false, [0x02, 0x08, 0x02]);
        }));
        Publish(game, parameters, false, [0x02, 0x08, 0x01]);
        Assert.Equal(new long[] { 1, 2 }, order);
    }

    [Fact]
    public void Reentrant_stop_in_first_typed_subscriber_skips_remaining_stale_subscribers()
    {
        var game = NewGame(9004);
        var source = Source(9004, epoch: 88);
        using var lifetime = new CancellationTokenSource();
        Set(game, "_sessionCancellation", lifetime);
        Set(game, "_currentSource", source);
        int first = 0, later = 0;
        var firstHandler = Observer(_ =>
        {
            Interlocked.Increment(ref first);
            GameType.GetMethod("RevokeForReplacement")!.Invoke(game, null);
        });
        var nextHandler = Observer(_ => Interlocked.Increment(ref later));
        var parameters = NewParams(Delegate.Combine(firstHandler, nextHandler));
        Publish(game, parameters, false, [0x02, 0x08, 0x01]);
        Publish(game, parameters, false, [0x02, 0x08, 0x02]);
        Assert.Equal(1, first);
        Assert.Equal(0, later);
        Assert.False(IsCurrent(game, source));
    }

    [Fact]
    public void An_old_queued_injection_is_denied_after_exact_source_is_revoked()
    {
        var game = NewGame(9005);
        var source = Source(9005, 99);
        using var lifetime = new CancellationTokenSource();
        Set(game, "_sessionCancellation", lifetime);
        Set(game, "_currentSource", source);
        Set(game, "_injectionReady", 1);
        Set(game, "_injectorPublished", 1);
        GameType.GetMethod("RevokeForReplacement")!.Invoke(game, null);
        Assert.False((bool)GameType.GetMethod("Inject")!
            .Invoke(game, [new byte[] { 2, 8, 1 }])!);
        Assert.False(IsCurrent(game, source));
    }
}
