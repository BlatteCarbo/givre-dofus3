extern alias F06App;

using System.Net;
using System.Net.Sockets;
using System.Reflection;
using GivreClone.Engine.Accounts;
using GivreCore.Protobuf;
using Xunit;
using WorldMirrorAcceptor = F06App::GivreClone.App.Services.Transport.Socket.WorldMirrorAcceptor;
using WorldMirrorArmRequest = F06App::GivreClone.App.Services.Transport.Socket.WorldMirrorArmRequest;
using WorldMirrorArmStatus = F06App::GivreClone.App.Services.Transport.Socket.WorldMirrorArmStatus;
using WorldMirrorClientRequest = F06App::GivreClone.App.Services.Transport.Socket.WorldMirrorClientRequest;
using WorldMirrorClientStatus = F06App::GivreClone.App.Services.Transport.Socket.WorldMirrorClientStatus;
using WorldMirrorDelivery = F06App::GivreClone.App.Services.Transport.Socket.WorldMirrorDelivery;
using WorldMirrorFrameCache = F06App::GivreClone.App.Services.Transport.Socket.WorldMirrorFrameCache;
using WorldMirrorIds = F06App::GivreClone.App.Services.Transport.Socket.WorldMirrorIds;
using WorldMirrorLive = F06App::GivreClone.App.Services.Transport.Socket.WorldMirrorLive;
using WorldMirrorSession = F06App::GivreClone.App.Services.Transport.Socket.WorldMirrorSession;
using WorldMirrorSnapshot = F06App::GivreClone.App.Services.Transport.Socket.WorldMirrorSnapshot;
using WorldMirrorWait = F06App::GivreClone.App.Services.Transport.Socket.WorldMirrorWait;
using WorldMirrorWaitResult = F06App::GivreClone.App.Services.Transport.Socket.WorldMirrorWaitResult;
using HotMirrorPlan = F06App::GivreClone.App.Services.Transport.Socket.HotMirrorPlan;
using LocalHandshakeRelay = F06App::GivreClone.App.Services.LocalLaunch.LocalHandshakeRelay;
using ConnectionMessages = GivreClone.App.Services.Transport.Socket.ConnectionMessages;

namespace GivreClone.Tests;

public sealed class HotMirrorTests
{
    private const long Character = 42;
    private const long Map = 10;
    private const int Pipeline = 3;
    private const string Scope = "catalog-build";
    private static readonly string[] Five =
    [
        "CharacterListEvent",
        "CharacterSelectionEvent",
        "CharacterLoadingCompleteEvent",
        "MapCurrentEvent",
        "MapComplementaryInformationEvent",
    ];

    public static IEnumerable<object[]> RequiredNames() => Five.Select(name => new object[] { name });

    [Fact]
    public void Required_names_and_budget_match_the_mirror_contract()
    {
        Assert.True(Five.SequenceEqual(WorldMirrorSession.RequiredWorldFrames));
        Assert.Equal(300_000, WorldMirrorSession.AttemptTimeoutMs);
        Assert.False(WorldMirrorSession.PausesScript);
        Assert.Equal("world-mirror-armed", WorldMirrorSession.ArmedLog);
        Assert.Equal("world-mirror-launch:socket-session-preserved", WorldMirrorSession.LaunchLog);
        Assert.Equal("world-mirror-live-forwarding:socket-session-preserved", WorldMirrorSession.LiveForwardLog);
        Assert.Equal("world-mirror-hud-not-verified:map-transport-ready-only", WorldMirrorSession.HudUnverifiedLog);
        Assert.Equal("world-mirror-map-replay-rejected:world-changed", WorldMirrorSession.MapRejectedLog);
        Assert.Equal("world-mirror-bootstrap-timeout:socket-session-preserved", WorldMirrorSession.BudgetLog);
        Assert.Equal("world-mirror-character-selection-rejected", WorldMirrorSession.SelectionRejectedLog);
        Assert.Equal("world-mirror-inventory-replay-failed:socket-session-preserved", WorldMirrorSession.InventoryReplayFailedLog);
        Assert.Equal("world-mirror-login-slot-missing:socket-session-preserved", WorldMirrorSession.LoginMissingLog);
        Assert.Equal("world-mirror-cancelled:socket-session-preserved", WorldMirrorSession.CancelledLog);
        Assert.Equal("La session Socket à chaud a été abandonnée.", HotMirrorPlan.HotSessionDropped);
        Assert.Equal("La session Socket reste connectée.", HotMirrorPlan.SocketPreserved);
        Assert.Equal("Fenêtre de jeu ouverte. La session Socket reste connectée.", HotMirrorPlan.WindowOpened);
        Assert.Equal("Le combat a dépassé le délai du miroir. La session Socket reste connectée.", HotMirrorPlan.FightBudget);
    }

    [Theory]
    [MemberData(nameof(RequiredNames))]
    public void Arm_fails_when_a_required_frame_is_missing(string name)
    {
        var frames = FullFrames();
        Assert.True(frames.Remove(name));
        var session = new WorldMirrorSession();
        Assert.Equal(WorldMirrorArmStatus.MissingFrame, session.TryArm(Request(frames, FullMaps())));
        Assert.Equal(WorldMirrorSession.StageOff, session.Stage);
    }

    [Fact]
    public void Arm_fails_when_a_required_frame_is_empty_or_the_current_map_differs()
    {
        var empty = FullFrames();
        empty["MapCurrentEvent"] = Array.Empty<byte>();
        Assert.Equal(WorldMirrorArmStatus.MissingFrame, new WorldMirrorSession().TryArm(Request(empty, FullMaps())));

        var stale = FullMaps();
        stale["MapCurrentEvent"] = Map + 1;
        var session = new WorldMirrorSession();
        Assert.Equal(WorldMirrorArmStatus.StaleMap, session.TryArm(Request(FullFrames(), stale)));
        Assert.Equal(WorldMirrorSession.StageOff, session.Stage);
    }

    [Fact]
    public void Arm_accepts_a_complementary_map_whose_id_did_not_parse_and_rejects_a_different_one()
    {
        var maps = FullMaps();
        maps["MapComplementaryInformationEvent"] = null;
        Assert.Equal(WorldMirrorArmStatus.Armed, new WorldMirrorSession().TryArm(Request(FullFrames(), maps)));

        maps["MapComplementaryInformationEvent"] = Map + 5;
        Assert.Equal(WorldMirrorArmStatus.StaleMap, new WorldMirrorSession().TryArm(Request(FullFrames(), maps)));
    }

    [Theory]
    [InlineData("3.7.11.11", "3.7.13.17", true)]
    [InlineData("3.7.13.17", "3.7.13.17", true)]
    [InlineData("3.7.13.17", "3.8.0.1", false)]
    [InlineData("3.7.13.17", null, false)]
    [InlineData("", "", false)]
    public void Same_protocol_family_arms_across_patches_but_not_across_families(string? catalog, string? runtime, bool armed)
        => Assert.Equal(armed ? WorldMirrorArmStatus.Armed : WorldMirrorArmStatus.ScopeMismatch,
            new WorldMirrorSession().TryArm(Request(FullFrames(), FullMaps(), catalog: catalog, runtime: runtime)));

    [Fact]
    public void Arm_fails_closed_without_an_inventory_frame_or_a_matching_scope()
    {
        var frames = FullFrames();
        Assert.True(frames.Remove(WorldMirrorSession.InventoryFrame));
        Assert.Equal(WorldMirrorArmStatus.MissingInventory, new WorldMirrorSession().TryArm(Request(frames, FullMaps())));
        Assert.Equal(WorldMirrorArmStatus.ScopeMismatch, new WorldMirrorSession().TryArm(Request(FullFrames(), FullMaps(), runtime: "other-build")));
        Assert.Equal(WorldMirrorArmStatus.ScopeMismatch, new WorldMirrorSession().TryArm(Request(FullFrames(), FullMaps(), catalog: "", runtime: "")));
        Assert.Equal(WorldMirrorArmStatus.IdentityMismatch, new WorldMirrorSession().TryArm(Request(FullFrames(), FullMaps(), notedGeneration: Pipeline + 1)));
        Assert.Null(typeof(WorldMirrorSession).GetMethod("TrafficInventoryContent", Any));
        Assert.Null(typeof(WorldMirrorFrameCache).GetMethod("TrafficInventoryContent", Any));
        Assert.Null(typeof(WorldMirrorAcceptor).GetMethod("TrafficInventoryContent", Any));
    }

    [Fact]
    public void Fight_traffic_does_not_replace_the_cached_map_and_the_next_idle_frame_does()
    {
        var cache = new WorldMirrorFrameCache();
        var before = new byte[] { 1, 2, 3 };
        var during = new byte[] { 4, 5 };
        var after = new byte[] { 6, 7, 8 };
        cache.Observe(8, "MapCurrentEvent", before, Map, inFight: false);
        cache.Observe(8, "MapCurrentEvent", during, Map + 1, inFight: true);
        var frozen = cache.Copy(8);
        Assert.True(frozen.TryGet("MapCurrentEvent", out var kept));
        Assert.True(before.SequenceEqual(kept));
        Assert.True(frozen.TryParsedMapId("MapCurrentEvent", out var map));
        Assert.Equal(Map, map);
        cache.Observe(8, "MapCurrentEvent", after, Map + 2, inFight: false);
        var replaced = cache.Copy(8);
        Assert.True(replaced.TryGet("MapCurrentEvent", out var next));
        Assert.True(after.SequenceEqual(next));
        Assert.True(replaced.TryParsedMapId("MapCurrentEvent", out var nextMap));
        Assert.Equal(Map + 2, nextMap);
    }

    [Fact]
    public void A_different_character_selection_writes_nothing_and_stays_at_stage_1()
    {
        var session = Arm();
        var injected = new List<byte[]>();
        var result = session.HandleClientRequest(new WorldMirrorClientRequest("CharacterSelectionRequest", Character + 1, new byte[] { 9 }, Live()));
        WorldMirrorDelivery.ForwardUpstream(result, injected.Add);
        Assert.Equal(WorldMirrorClientStatus.Rejected, result.Status);
        Assert.Empty(result.ToClient);
        Assert.Null(result.ToUpstream);
        Assert.Empty(injected);
        Assert.Equal(WorldMirrorSession.StageArmed, session.Stage);
        Assert.Contains(WorldMirrorSession.SelectionRejectedLog, session.Messages);
        Assert.False(session.UpstreamClosed);
    }

    [Fact]
    public void Map_request_during_a_fight_writes_nothing_then_the_post_fight_snapshot_is_forwarded()
    {
        var session = Arm();
        Accept(session);
        var waiting = session.HandleClientRequest(new WorldMirrorClientRequest("MapInformationRequest", null, new byte[] { 1 }, Live(inFight: true)));
        Assert.Equal(WorldMirrorClientStatus.Waiting, waiting.Status);
        Assert.Empty(waiting.ToClient);
        Assert.Null(waiting.ToUpstream);
        Assert.False(session.ForwardingEnabled);
        Assert.Equal(WorldMirrorSession.StageCharacterAccepted, session.Stage);

        var current = new byte[] { 10, 20 };
        var complementary = new byte[] { 30, 40, 50 };
        var served = session.HandleClientRequest(new WorldMirrorClientRequest(
            "MapInformationRequest", null, new byte[] { 1 }, Live(frames: PostFight(current, complementary))));
        Assert.Equal(WorldMirrorClientStatus.Served, served.Status);
        Assert.Contains(served.ToClient, bytes => bytes.SequenceEqual(current));
        Assert.Contains(served.ToClient, bytes => bytes.SequenceEqual(complementary));
        Assert.True(current.SequenceEqual(served.ToClient[0]));
        Assert.True(complementary.SequenceEqual(served.ToClient[1]));
        Assert.True(session.ForwardingEnabled);
        Assert.Contains(WorldMirrorSession.LiveForwardLog, session.Messages);
        Assert.Contains(WorldMirrorSession.HudUnverifiedLog, session.Messages);
        Assert.False(session.UpstreamClosed);
    }

    [Fact]
    public void Budget_expiry_cancels_the_mirror_and_leaves_the_upstream_flag_down()
    {
        var session = Arm();
        Accept(session);
        var expired = session.HandleClientRequest(new WorldMirrorClientRequest(
            "MapInformationRequest", null, new byte[] { 1 },
            Live(inFight: true, now: WorldMirrorSession.AttemptTimeoutMs)));
        Assert.Equal(WorldMirrorClientStatus.BudgetExpired, expired.Status);
        Assert.Empty(expired.ToClient);
        Assert.Null(expired.ToUpstream);
        Assert.Equal(WorldMirrorSession.StageOff, session.Stage);
        Assert.False(session.ForwardingEnabled);
        Assert.False(session.UpstreamClosed);
        Assert.Contains(WorldMirrorSession.BudgetLog, session.Messages);
    }

    [Fact]
    public void A_changed_pipeline_rejects_the_map_without_cancelling_the_mirror()
    {
        var session = Arm();
        Accept(session);
        var rejected = session.HandleClientRequest(new WorldMirrorClientRequest(
            "MapInformationRequest", null, new byte[] { 1 }, Live(pipeline: Pipeline + 1)));
        Assert.Equal(WorldMirrorClientStatus.Rejected, rejected.Status);
        Assert.Empty(rejected.ToClient);
        Assert.Equal(WorldMirrorSession.StageCharacterAccepted, session.Stage);
        Assert.False(session.UpstreamClosed);
        Assert.Contains(WorldMirrorSession.MapRejectedLog, session.Messages);
    }

    [Fact]
    public void Stage_3_drops_character_selection_and_forwards_one_ordinary_frame_once()
    {
        var session = Arm();
        Accept(session);
        var map = session.HandleClientRequest(new WorldMirrorClientRequest("MapInformationRequest", null, new byte[] { 1 }, Live()));
        Assert.Equal(WorldMirrorClientStatus.Served, map.Status);
        Assert.True(session.ForwardingEnabled);

        var raw = new byte[] { 9, 8, 7 };
        var injected = new List<byte[]>();
        var selection = session.HandleClientRequest(new WorldMirrorClientRequest("CharacterSelectionRequest", Character, new byte[] { 4 }, Live()));
        WorldMirrorDelivery.ForwardUpstream(selection, injected.Add);
        Assert.Equal(WorldMirrorClientStatus.Dropped, selection.Status);
        Assert.Null(selection.ToUpstream);
        Assert.Empty(injected);

        var ordinary = session.HandleClientRequest(new WorldMirrorClientRequest("MapMovementRequest", null, raw, Live()));
        WorldMirrorDelivery.ForwardUpstream(ordinary, injected.Add);
        Assert.Equal(WorldMirrorClientStatus.Forwarded, ordinary.Status);
        Assert.NotNull(ordinary.ToUpstream);
        Assert.True(raw.SequenceEqual(ordinary.ToUpstream!));
        Assert.Single(injected);
        Assert.True(raw.SequenceEqual(injected[0]));
    }

    [Fact]
    public void Same_identity_mirror_delegates_preserve_the_socket_and_explicit_reconnect_still_closes_then_launches()
    {
        var hot = HotMirrorPlan.SourceDelegates(true);
        Assert.True(hot.SequenceEqual(new[] { HotMirrorPlan.PreserveUpstream }));
        Assert.DoesNotContain(HotMirrorPlan.SocketCancel, hot);
        Assert.DoesNotContain(HotMirrorPlan.UnregisterSocketInjector, hot);
        Assert.True(HotMirrorPlan.UseHotPath(ConnectionMode.Socket, ConnectionMode.Mitm, sameIdentity: true, explicitDrop: false));
        Assert.False(HotMirrorPlan.UseHotPath(ConnectionMode.Socket, ConnectionMode.Mitm, sameIdentity: true, explicitDrop: true));
        Assert.False(HotMirrorPlan.UseHotPath(ConnectionMode.Socket, ConnectionMode.Mitm, sameIdentity: false, explicitDrop: false));
        Assert.False(HotMirrorPlan.UseHotPath(ConnectionMode.Socket, ConnectionMode.Socket, sameIdentity: true, explicitDrop: false));

        var cold = HotMirrorPlan.SourceDelegates(false);
        int close = IndexOf(cold, HotMirrorPlan.Close);
        int launch = IndexOf(cold, HotMirrorPlan.Launch);
        Assert.Contains(HotMirrorPlan.SocketCancel, cold);
        Assert.Contains(HotMirrorPlan.UnregisterSocketInjector, cold);
        Assert.Contains(HotMirrorPlan.Forget, cold);
        Assert.Contains(HotMirrorPlan.ClearMap, cold);
        Assert.True(close >= 0 && launch > close);
    }

    [Fact]
    public async Task Wait_returns_without_delaying_when_occupied_idle_or_already_expired()
    {
        var occupied = await WorldMirrorWait.UntilAsync(() => true, () => true, () => 0, 0, DelayFails, CancellationToken.None);
        Assert.Equal(WorldMirrorWaitResult.Busy, occupied);
        var idle = await WorldMirrorWait.UntilAsync(() => false, () => false, () => 0, 0, DelayFails, CancellationToken.None);
        Assert.Equal(WorldMirrorWaitResult.Idle, idle);
        var expired = await WorldMirrorWait.UntilAsync(
            () => true, () => false, () => WorldMirrorSession.AttemptTimeoutMs, 0, DelayFails, CancellationToken.None);
        Assert.Equal(WorldMirrorWaitResult.BudgetExpired, expired);
    }

    [Fact]
    public async Task Wait_delays_once_while_the_fight_is_inside_the_budget_then_sees_idle()
    {
        int calls = 0;
        long now = 0;
        bool fight = true;
        var result = await WorldMirrorWait.UntilAsync(
            () => fight,
            () => false,
            () => now,
            0,
            (slice, _) =>
            {
                calls++;
                now += (long)slice.TotalMilliseconds;
                fight = false;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        Assert.Equal(WorldMirrorWaitResult.Idle, result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Parsed_ids_stay_null_when_the_catalog_field_is_missing_or_not_positive()
    {
        var payload = new List<ProtoField> { new() { Field = 1, Wire = WireType.Varint, Varint = 10 } };
        Assert.Equal(10L, WorldMirrorIds.Positive(1, payload));
        Assert.Null(WorldMirrorIds.Positive(null, payload));
        Assert.Null(WorldMirrorIds.Positive(0, payload));
        Assert.Null(WorldMirrorIds.Positive(2, payload));
        Assert.Null(WorldMirrorIds.Positive(1, Array.Empty<ProtoField>()));
    }

    [Fact]
    public void Select_server_recognition_follows_the_existing_login_encoder()
    {
        var selected = RawProtobuf.ConsumeFrames(ConnectionMessages.BuildSelectServer("1", 40), out var selectedRest);
        Assert.Empty(selectedRest);
        Assert.Single(selected);
        Assert.True(ConnectionMessages.TryIsSelectServerRequest(selected[0]));

        var identified = RawProtobuf.ConsumeFrames(
            ConnectionMessages.BuildIdentification("0", "device", "1.0", "token", 0, ""), out var identifiedRest);
        Assert.Empty(identifiedRest);
        Assert.False(ConnectionMessages.TryIsSelectServerRequest(identified[0]));
        Assert.True(ConnectionMessages.TryReadDeviceIdentifier(identified[0], out var device));
        Assert.Equal("device", device);
        Assert.False(ConnectionMessages.TryIsSelectServerRequest(Array.Empty<byte>()));
    }

    [Fact]
    public void The_visible_acceptor_declares_no_upstream_connect()
    {
        Assert.DoesNotContain(
            typeof(WorldMirrorAcceptor).GetMethods(Any),
            method => method.Name.Contains("Connect", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unbound_visible_pid_is_refused_with_one_log_line()
    {
        var lines = new List<string>();
        var refused = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var acceptor = new WorldMirrorAcceptor(
            socketForVisible: _ => null,
            ownedAndLive: _ => true,
            classify: _ => throw new InvalidOperationException("classify"),
            loginFrame: (_, _) => null,
            session: _ => null,
            live: _ => throw new InvalidOperationException("live"),
            inject: (_, _) => false,
            log: (_, line) =>
            {
                lock (lines) lines.Add(line);
                if (line.Contains("non lié ou non possédé", StringComparison.Ordinal)) refused.TrySetResult(line);
            });

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, acceptor.Port);
        var header = new byte[LocalHandshakeRelay.HandshakeLength];
        new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 127, 0, 0, 1 }.CopyTo(header, 0);
        BitConverter.GetBytes(16).CopyTo(header, 16);
        BitConverter.GetBytes((ushort)5555).CopyTo(header, 20);
        BitConverter.GetBytes(4242u).CopyTo(header, 22);
        await client.GetStream().WriteAsync(header);

        var line = await refused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("non lié ou non possédé", line);
        lock (lines) Assert.Single(lines, l => l.Contains("non lié ou non possédé", StringComparison.Ordinal));
    }

    private static Task DelayFails(TimeSpan _, CancellationToken _token) => throw new InvalidOperationException("delay");

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static int IndexOf(IReadOnlyList<string> steps, string name)
    {
        for (int i = 0; i < steps.Count; i++) if (steps[i] == name) return i;
        return -1;
    }

    private static WorldMirrorSession Arm()
    {
        var session = new WorldMirrorSession();
        Assert.Equal(WorldMirrorArmStatus.Armed, session.TryArm(Request(FullFrames(), FullMaps())));
        Assert.Contains(WorldMirrorSession.ArmedLog, session.Messages);
        Assert.DoesNotContain(WorldMirrorSession.LaunchLog, session.Messages);
        return session;
    }

    private static void Accept(WorldMirrorSession session)
    {
        var result = session.HandleClientRequest(new WorldMirrorClientRequest("CharacterSelectionRequest", Character, new byte[] { 1 }, Live()));
        Assert.Equal(WorldMirrorClientStatus.Served, result.Status);
        Assert.Null(result.ToUpstream);
        Assert.Equal(WorldMirrorSession.StageCharacterAccepted, session.Stage);
    }

    private static Dictionary<string, byte[]> FullFrames()
    {
        var frames = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in Five) frames[name] = new byte[] { (byte)name.Length, 1 };
        frames[WorldMirrorSession.InventoryFrame] = new byte[] { 7, 7, 7 };
        return frames;
    }

    private static Dictionary<string, long?> FullMaps() => new(StringComparer.Ordinal)
    {
        ["MapCurrentEvent"] = Map,
        ["MapComplementaryInformationEvent"] = null,
    };

    private static Dictionary<string, byte[]> PostFight(byte[] current, byte[] complementary)
    {
        var frames = FullFrames();
        frames["MapCurrentEvent"] = current;
        frames["MapComplementaryInformationEvent"] = complementary;
        return frames;
    }

    private static WorldMirrorArmRequest Request(Dictionary<string, byte[]> frames, Dictionary<string, long?> maps,
        string? catalog = Scope, string? runtime = Scope, int notedGeneration = 0)
        => new(Character, Character, Map, Pipeline, 0, false, true, catalog, runtime,
            WorldMirrorSnapshot.Create(frames, maps), notedGeneration);

    private static WorldMirrorLive Live(bool inFight = false, long now = 0, int? pipeline = null, Dictionary<string, byte[]>? frames = null)
        => new()
        {
            NowMs = now,
            InFight = inFight,
            MapId = Map,
            CharacterId = Character,
            PipelineGeneration = pipeline ?? Pipeline,
            UpstreamWritable = true,
            CatalogIdentity = Scope,
            RuntimeScope = Scope,
            Snapshot = WorldMirrorSnapshot.Create(frames ?? FullFrames(), FullMaps()),
        };
}
