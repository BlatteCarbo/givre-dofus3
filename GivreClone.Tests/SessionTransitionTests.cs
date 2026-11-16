extern alias F06App;

using System.Reflection;
using GivreClone.Engine;
using GivreClone.Engine.Accounts;
using Xunit;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;

namespace GivreClone.Tests;

public sealed class SessionTransitionTests
{
    private static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly SessionIdentity Identity = new(261, 123456789, "Exact-name");

    private sealed class Rig
    {
        public readonly SessionTransition Operation = new();
        public readonly List<string> Calls = [];
        public bool Owned = true;
        public Func<CancellationToken, Task<string?>> Preflight = _ => Task.FromResult<string?>(null);
        public Func<Task> Close = () => Task.CompletedTask;
        public Func<Task<bool>> Launch = () => Task.FromResult(true);
        public Func<CancellationToken, Task<bool>> Confirm = _ => Task.FromResult(true);
        public Func<bool>? TryCommit;
        public Task<SessionTransitionResult> Run(CancellationToken token = default) => Operation.RunAsync(
            Preflight, () => Owned, async () => { Calls.Add("close"); await Close(); },
            async () => { Calls.Add("launch"); return await Launch(); },
            async ct => { Calls.Add("confirm"); return await Confirm(ct); },
            _ => Calls.Add("disconnect"), _ => { }, token, TryCommit);
    }

    [Fact]
    public async Task Rejected_preflight_preserves_the_source_and_never_authenticates()
    {
        var rig = new Rig { Preflight = _ => Task.FromResult<string?>("route refused") };
        var result = await rig.Run();
        Assert.Equal(SessionTransitionState.Rejected, result.State);
        Assert.False(result.Committed);
        Assert.Empty(rig.Calls);
    }

    [Fact]
    public async Task Cancel_during_preflight_preserves_the_source()
    {
        using var cancel = new CancellationTokenSource();
        var gate = Signal<string?>();
        var rig = new Rig { Preflight = _ => gate.Task };
        var run = rig.Run(cancel.Token);
        cancel.Cancel();
        gate.SetResult(null);
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SessionTransitionState.Cancelled, result.State);
        Assert.False(result.Committed);
        Assert.Empty(rig.Calls);
    }

    [Fact]
    public async Task Replaced_source_after_preflight_is_not_cut()
    {
        var gate = Signal<string?>();
        var rig = new Rig { Preflight = _ => gate.Task };
        var run = rig.Run();
        rig.Owned = false;
        gate.SetResult(null);
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SessionTransitionState.Superseded, result.State);
        Assert.Empty(rig.Calls);
    }

    [Fact]
    public async Task Session_that_ends_at_commit_is_not_replaced_or_disconnected()
    {
        var rig = new Rig { TryCommit = () => false };
        var result = await rig.Run();
        Assert.Equal(SessionTransitionState.Superseded, result.State);
        Assert.False(result.Committed);
        Assert.Empty(rig.Calls);
    }

    [Fact]
    public async Task Graphical_auth_waits_for_the_entire_socket_exit()
    {
        var exited = Signal<bool>();
        var rig = new Rig { Close = () => exited.Task };
        var run = rig.Run();
        Assert.Equal(SessionTransitionState.Closing, rig.Operation.State);
        Assert.Equal(["close"], rig.Calls);
        Assert.False(run.IsCompleted);
        exited.SetResult(true);
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.Ok);
        Assert.Equal(["close", "launch", "confirm"], rig.Calls);
    }

    [Fact]
    public async Task Cancel_after_commit_still_awaits_exit_and_never_authenticates()
    {
        using var cancel = new CancellationTokenSource();
        var exited = Signal<bool>();
        var rig = new Rig { Close = () => exited.Task };
        var run = rig.Run(cancel.Token);
        cancel.Cancel();
        Assert.False(run.IsCompleted);
        exited.SetResult(true);
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.Committed);
        Assert.Equal(SessionTransitionState.Cancelled, result.State);
        Assert.Equal(["close", "disconnect"], rig.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_after_commit_is_disconnected_not_restored(bool identityFailure)
    {
        var rig = new Rig
        {
            Launch = () => Task.FromResult(identityFailure),
            Confirm = _ => Task.FromResult(false),
        };
        var result = await rig.Run();
        Assert.Equal(SessionTransitionState.Disconnected, result.State);
        Assert.True(result.Committed);
        Assert.Equal("disconnect", rig.Calls[^1]);
    }

    [Fact]
    public async Task Obsolete_launch_completion_cannot_disconnect_newer_owner()
    {
        var gate = Signal<bool>();
        var rig = new Rig { Launch = () => gate.Task };
        var run = rig.Run();
        rig.Owned = false;
        gate.SetResult(true);
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SessionTransitionState.Superseded, result.State);
        Assert.DoesNotContain("disconnect", rig.Calls);
        Assert.DoesNotContain("confirm", rig.Calls);
    }

    [Fact]
    public async Task Duplicate_execution_cannot_start_a_second_authentication()
    {
        var gate = Signal<string?>();
        var rig = new Rig { Preflight = _ => gate.Task };
        var first = rig.Run();
        var duplicate = await rig.Run();
        Assert.Equal(SessionTransitionState.Rejected, duplicate.State);
        Assert.True(rig.Operation.Active);
        gate.SetResult(null);
        Assert.True((await first.WaitAsync(TimeSpan.FromSeconds(5))).Ok);
        Assert.Single(rig.Calls.Where(c => c == "launch"));
    }

    [Fact]
    public async Task Cancellation_during_identity_confirmation_disconnects_owned_target()
    {
        using var cancel = new CancellationTokenSource();
        var confirmation = Signal<bool>();
        var rig = new Rig { Confirm = ct => confirmation.Task.WaitAsync(ct) };
        var run = rig.Run(cancel.Token);
        Assert.Equal(SessionTransitionState.Confirming, rig.Operation.State);
        cancel.Cancel();
        Assert.Equal(SessionTransitionState.Cancelled, (await run.WaitAsync(TimeSpan.FromSeconds(5))).State);
        Assert.Equal("disconnect", rig.Calls[^1]);
    }

    [Fact]
    public void Identity_and_source_ownership_include_server_character_and_socket_generation()
    {
        Assert.True(Identity.Matches(Identity));
        Assert.False(Identity.Matches(Identity with { ServerId = 262 }));
        Assert.False(Identity.Matches(Identity with { CharacterId = Identity.CharacterId + 1 }));
        Assert.False(Identity.Matches(Identity with { Character = "Other" }));
        Assert.False(Identity.Matches(Identity with { ServerId = 0 }));
        var source = new SessionSource("account", AuthSource.Browser, 3, 100, ConnectionMode.Socket, 7, Identity);
        Assert.NotEqual(source, source with { SocketSequence = 8 });
        Assert.NotEqual(source, source with { Generation = 4 });
        Assert.NotEqual(source, source with { AccountId = "another" });
        Assert.NotEqual(source, source with { Authentication = AuthSource.KeyData });
        Assert.NotEqual(source, source with { Pid = 101 });
        Assert.NotEqual(source, source with { Mode = ConnectionMode.Mitm });
    }

    [Fact]
    public async Task Explicit_selection_scope_does_not_leak_across_parallel_account_launches()
    {
        var queue = new CharacterSelectionQueue();
        var bound = Signal<bool>();
        var release = Signal<bool>();
        async Task BindExplicit()
        {
            using var scope = queue.Expect(Identity);
            bound.SetResult(true);
            await release.Task;
            queue.Bind(101, Identity.Character);
        }
        var launch = BindExplicit();
        await bound.Task;
        queue.Bind(102, "Other");
        release.SetResult(true);
        await launch.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(queue.TryClaim(101, out var exact));
        Assert.Equal(Identity, exact.Target);
        Assert.True(queue.TryClaim(102, out var ordinary));
        Assert.Null(ordinary.Target);
        queue.Bind(101, "Replacement");
        Assert.False(queue.IsCurrent(exact));
        queue.Complete(exact);
        Assert.True(queue.TryClaim(101, out var replacement));
        Assert.Null(replacement.Target);
    }

    [Fact]
    public void Missing_explicit_socket_server_never_falls_back_to_another_server()
    {
        var assembly = typeof(LaunchService).Assembly;
        var serverType = assembly.GetType("GivreClone.App.Services.Transport.Socket.ConnServer")!;
        var characterType = assembly.GetType("GivreClone.App.Services.Transport.Socket.ConnCharacter")!;
        var server = Activator.CreateInstance(serverType)!;
        serverType.GetField("Id")!.SetValue(server, 261);
        var characters = (System.Collections.IList)serverType.GetField("Characters")!.GetValue(server)!;
        characters.Add(Activator.CreateInstance(characterType));
        var servers = Array.CreateInstance(serverType, 1);
        servers.SetValue(server, 0);
        var client = assembly.GetType("GivreClone.App.Services.Transport.Socket.SocketConnectionClient")!;
        var pick = client.GetMethod("PickServer", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(0, pick.Invoke(null, [servers, 999]));
        Assert.Equal(261, pick.Invoke(null, [servers, 261]));
        Assert.Equal(261, pick.Invoke(null, [servers, 0]));
    }
    [Fact]
    public void Requested_socket_character_is_never_replaced()
    {
        static GivreCore.Protobuf.ProtoField Character(long id, string name) => new()
        {
            Field = 1, Wire = GivreCore.Protobuf.WireType.Len, Message =
            [
                new() { Field = 2, Wire = GivreCore.Protobuf.WireType.Varint, Varint = (ulong)id },
                new() { Field = 3, Wire = GivreCore.Protobuf.WireType.Len, Message =
                    [new() { Field = 4, Wire = GivreCore.Protobuf.WireType.Len, String = name }] },
            ],
        };
        Func<string, string, int> numbers = (_, field) => field switch
        {
            "characters" => 1, "id" => 2, "character_basic_information" => 3, "name" => 4, _ => 5,
        };
        List<GivreCore.Protobuf.ProtoField> list = [Character(11, "First"), Character(22, "Second")];
        var client = typeof(LaunchService).Assembly.GetType("GivreClone.App.Services.Transport.Socket.SocketGameClient")!;
        var pick = client.GetMethod("PickCharacter", BindingFlags.Static | BindingFlags.NonPublic)!;
        long Picked(string? preferred) => (((long, string?, int))pick.Invoke(null, [list, preferred, numbers])!).Item1;
        Assert.Equal(22, Picked("second"));
        Assert.Equal(0, Picked("Deleted"));
        Assert.Equal(11, Picked(null));
    }
}
