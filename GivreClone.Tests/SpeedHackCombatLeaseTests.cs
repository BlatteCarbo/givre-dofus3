extern alias F06App;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using GivreCore.Protobuf;
using GivreLauncher.Core.Launch;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using DecodedPacket = F06App::GivreClone.App.Services.DecodedPacket;
using Xunit;

namespace GivreClone.Tests;

public sealed class SpeedHackCombatLeaseTests
{
    [Fact]
    public async Task Construction_epoch_survives_matching_ready_and_same_map_reload_allocates_another()
    {
        await using var fixture = new LeaseFixture();
        var tactical = fixture.NewState(9, tactical: true);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = tactical;
        fixture.BeginMap(1234);
        string[] pending = File.ReadAllText(fixture.Construction).Split(' ');
        Assert.Equal("9", pending[6]);
        Assert.False(File.Exists(fixture.TacticalLease));
        fixture.MapReady(5678);
        Assert.False(File.Exists(fixture.TacticalLease));
        Assert.True(File.Exists(fixture.Construction));

        fixture.MapReady(1234);
        Assert.Equal(pending[4], File.ReadAllText(fixture.TacticalLease).Split(' ')[4]);
        fixture.RefreshConstruction(tactical);
        Assert.False(File.Exists(fixture.Construction));
        fixture.BeginMap(1234);
        Assert.NotEqual(pending[4], File.ReadAllText(fixture.Construction).Split(' ')[4]);
        Assert.False(File.Exists(fixture.TacticalLease));
    }

    [Fact]
    public async Task Supersession_and_revocation_cannot_recreate_capture_but_stale_heartbeat_cannot_delete_successor()
    {
        await using var fixture = new LeaseFixture();
        var old = fixture.NewState(9, tactical: true);
        var current = fixture.NewState(10, tactical: true);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = old;
        fixture.BeginMap(1234);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = current;
        fixture.RefreshConstruction(current);
        Assert.False(File.Exists(fixture.Construction));
        fixture.RefreshConstruction(old);
        Assert.False(File.Exists(fixture.Construction));

        fixture.BeginMap(1234);
        string successor = File.ReadAllText(fixture.Construction);
        fixture.RefreshConstruction(old);
        Assert.Equal(successor, File.ReadAllText(fixture.Construction));
        fixture.RevokeMap();
        fixture.RefreshConstruction(current);
        fixture.MapReady(1234);
        Assert.False(File.Exists(fixture.Construction));
        Assert.False(File.Exists(fixture.TacticalLease));
    }

    [Fact]
    public async Task After_forward_map_observation_cannot_grant_construction_even_with_enabled_profile()
    {
        await using var fixture = new LeaseFixture();
        var tactical = fixture.NewState(9, tactical: true);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = tactical;
        fixture.BeginMap(1234, beforeForward: false);
        fixture.RefreshConstruction(tactical);
        Assert.False(File.Exists(fixture.Construction));
        fixture.MapReady(1234);
        Assert.True(File.Exists(fixture.TacticalLease));
    }

    [Fact]
    public async Task Roleplay_readiness_preserves_the_incoming_map_epoch()
    {
        await using var fixture = new LeaseFixture();
        var tactical = fixture.NewState(9, tactical: true);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = tactical;
        var service = new LaunchService();
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var catalog = GivreClone.Protocol.ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        typeof(LaunchService).GetField("_catalog", hidden)!.SetValue(service, catalog);
        var decode = typeof(LaunchService).GetMethod("OnDecoded", hidden)!;
        int currentField = catalog.FieldNumber("MapCurrentEvent", "map_id")!.Value;
        decode.Invoke(service, new object[] { new DecodedPacket((uint)fixture.Pid, false, "", "MapCurrentEvent",
            new[] { new ProtoField { Field = currentField, Wire = WireType.Varint, Varint = 1234 } }, Array.Empty<byte>()) });
        long epoch = fixture.TransitionEpoch;
        // Direct decoded ingress intentionally cannot publish capture, but must
        // retain its authoritative epoch through the ordinary information request.
        decode.Invoke(service, new object[] { new DecodedPacket((uint)fixture.Pid, true, "", "MapInformationRequest",
            Array.Empty<ProtoField>(), Array.Empty<byte>()) });
        int field = catalog.FieldNumber("MapComplementaryInformationEvent", "map_id")!.Value;
        var payload = new ProtoField { Field = field, Wire = WireType.Varint, Varint = 1234 };
        typeof(LaunchService).GetMethod("OnDecoded", hidden)!.Invoke(service,
            new object[] { new DecodedPacket((uint)fixture.Pid, false, "", "MapComplementaryInformationEvent", new[] { payload }, Array.Empty<byte>()) });
        Assert.Equal("1234", File.ReadAllText(fixture.TacticalLease).Split(' ')[5]);
        Assert.Equal(epoch.ToString(System.Globalization.CultureInfo.InvariantCulture), File.ReadAllText(fixture.TacticalLease).Split(' ')[4]);
        Assert.False(File.Exists(fixture.Construction));
    }

    [Fact]
    public async Task Failed_enhanced_handover_reapplies_the_current_profile_after_releasing_reservation()
    {
        await using var fixture = new LeaseFixture();
        var service = F06App::GivreClone.App.Services.Stores.Launch;
        var config = F06App::GivreClone.App.Services.AppConfig.Current;
        bool mods = config.ModsEnabled, items = config.ItemIdOverlay, enhanced = config.DofusEnhancedEnabled;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var runtime = typeof(LaunchService).Assembly.GetType("GivreClone.App.Services.NativeTurboRuntime")!;
        var provider = typeof(F06App::GivreClone.App.Services.DofusEnhanced);
        var activations = (System.Collections.Concurrent.ConcurrentDictionary<int, byte>)provider.GetField("Activations", flags)!.GetValue(null)!;
        try
        {
            config.ModsEnabled = config.ItemIdOverlay = true;
            Assert.True(activations.TryAdd(fixture.Pid, 0));
            service.SetCombatProfile((uint)fixture.Pid, new F06App::GivreClone.App.Services.CombatProfile());
            // Exercise actual handover preparation, but never the installed loader.
            await ((Task)runtime.GetMethod("PrepareForEnhancedAsync", flags)!.Invoke(null, new object[] { fixture.Pid })!).WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.BlockApplyAsync();
            Assert.Equal(false, fixture.Desired[fixture.Pid]!.GetType().GetProperty("ItemIds")!.GetValue(fixture.Desired[fixture.Pid]));
            provider.GetMethod("CompleteActivation", flags)!.Invoke(null, new object[] { fixture.Pid, fixture.StartTicks, true });

            Assert.False(activations.ContainsKey(fixture.Pid));
            Assert.Equal(true, fixture.Desired[fixture.Pid]!.GetType().GetProperty("ItemIds")!.GetValue(fixture.Desired[fixture.Pid]));
        }
        finally
        {
            activations.TryRemove(fixture.Pid, out _);
            service.ClearCombatProfile((uint)fixture.Pid);
            config.ModsEnabled = mods; config.ItemIdOverlay = items;
            config.DofusEnhancedEnabled = enhanced;
        }
    }

    [Fact]
    public async Task Enhanced_preflight_failure_does_not_republish_native_state()
    {
        await using var fixture = new LeaseFixture();
        var service = F06App::GivreClone.App.Services.Stores.Launch;
        var fights = (IDictionary)typeof(LaunchService).GetField("_inFight", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        fights[(uint)fixture.Pid] = true;
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => F06App::GivreClone.App.Services.DofusEnhanced.ActivateAsync(fixture.Pid));
            Assert.Equal(fixture.State, fixture.Desired[fixture.Pid]);
        }
        finally { fights.Remove((uint)fixture.Pid); }
    }

    [Theory]
    [InlineData("current")]
    [InlineData("clear")]
    [InlineData("newer-request")]
    [InlineData("request-during-handover")]
    [InlineData("wrong-start")]
    [InlineData("reserved")]
    public async Task Enhanced_recovery_respects_current_profile_and_publication_boundaries(string boundary)
    {
        await using var fixture = new LeaseFixture();
        var service = F06App::GivreClone.App.Services.Stores.Launch;
        var config = F06App::GivreClone.App.Services.AppConfig.Current;
        bool mods = config.ModsEnabled, items = config.ItemIdOverlay;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var provider = typeof(F06App::GivreClone.App.Services.DofusEnhanced);
        var activations = (System.Collections.Concurrent.ConcurrentDictionary<int, byte>)provider.GetField("Activations", flags)!.GetValue(null)!;
        try
        {
            await fixture.BlockApplyAsync();
            config.ModsEnabled = config.ItemIdOverlay = true;
            Assert.True(activations.TryAdd(fixture.Pid, 0));
            service.SetCombatProfile((uint)fixture.Pid, new F06App::GivreClone.App.Services.CombatProfile());
            if (boundary == "current")
            {
                config.ItemIdOverlay = false;
                service.SetCombatProfile((uint)fixture.Pid, new F06App::GivreClone.App.Services.CombatProfile { Tactical = true });
                ((IDictionary)typeof(LaunchService).GetField("_lastMap", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!)[(uint)fixture.Pid] = 1234L;
            }
            if (boundary == "request-during-handover") fixture.Request(false);
            if (boundary != "reserved") activations.TryRemove(fixture.Pid, out _);
            long generation = (long)fixture.Desired[fixture.Pid]!.GetType().GetProperty("Generation")!.GetValue(fixture.Desired[fixture.Pid])!;
            if (boundary == "clear") service.ClearCombatProfile((uint)fixture.Pid);
            if (boundary == "newer-request") fixture.Request(false);
            object before = fixture.Desired[fixture.Pid]!;

            typeof(LaunchService).GetMethod("RecoverNativeAfterEnhanced", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(service, new object[] { (uint)fixture.Pid, boundary == "wrong-start" ? fixture.StartTicks - 1 : fixture.StartTicks, generation });

            if (boundary == "current")
            {
                Assert.NotEqual(before, fixture.Desired[fixture.Pid]);
                Assert.Equal(false, fixture.Desired[fixture.Pid]!.GetType().GetProperty("ItemIds")!.GetValue(fixture.Desired[fixture.Pid]));
                Assert.Equal(F06App::GivreClone.App.Services.LicenseService.HasPermission(F06App::GivreClone.App.Services.LicenseService.Perm.Tactical),
                    fixture.Desired[fixture.Pid]!.GetType().GetProperty("Tactical")!.GetValue(fixture.Desired[fixture.Pid]));
                Assert.True(service.EffectiveCombatProfile((uint)fixture.Pid)!.Tactical);
            }
            else Assert.Equal(before, fixture.Desired[fixture.Pid]);
            if (boundary == "clear")
            {
                typeof(LaunchService).GetMethod("FinishCharacterLoad", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, new object[] { (uint)fixture.Pid });
                Assert.Equal(before, fixture.Desired[fixture.Pid]);
                Assert.Null(service.EffectiveCombatProfile((uint)fixture.Pid));
            }
        }
        finally
        {
            activations.TryRemove(fixture.Pid, out _);
            service.ClearCombatProfile((uint)fixture.Pid);
            config.ModsEnabled = mods; config.ItemIdOverlay = items;
            ((IDictionary)typeof(LaunchService).GetField("_lastMap", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!).Remove((uint)fixture.Pid);
        }
    }

    [Fact]
    public async Task Enhanced_reservation_revokes_standalone_lease_even_when_a_profile_requests_all_modules()
    {
        await using var fixture = new LeaseFixture();
        // The fixture's synthetic generation 1 may equal the first runtime publication.
        // Use a disjoint generation before exercising the real global sequence.
        var prior = fixture.NewState(-1);
        fixture.Desired[fixture.Pid] = prior;
        fixture.Combat(true);
        Assert.True(File.Exists(fixture.Lease));
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var assembly = typeof(LaunchService).Assembly;
        var enhanced = assembly.GetType("GivreClone.App.Services.DofusEnhanced")!;
        var activations = (System.Collections.Concurrent.ConcurrentDictionary<int, byte>)enhanced
            .GetField("Activations", flags)!.GetValue(null)!;
        var runtime = assembly.GetType("GivreClone.App.Services.NativeTurboRuntime")!;
        Assert.True(activations.TryAdd(fixture.Pid, 0));
        try
        {
            runtime.GetMethod("RequestState", flags)!.Invoke(null,
                new object[] { fixture.Pid, true, true, true, true, false, "ProviderOwnershipRegression" });
            var disabled = fixture.Desired[fixture.Pid]!;
            await ((Task)runtime.GetMethod("ApplyDesiredAsync", flags)!
                .Invoke(null, new[] { (object)fixture.Pid, disabled })!).WaitAsync(TimeSpan.FromSeconds(10));
            runtime.GetMethod("RefreshLease", flags)!.Invoke(null, new[] { (object)fixture.Pid, prior });

            Assert.False(File.Exists(fixture.Lease));
            Assert.All(new[] { "Enabled", "CombatSpeedHack", "ItemIds", "Tactical", "WantsVisuals" },
                name => Assert.Equal(false, disabled.GetType().GetProperty(name)!.GetValue(disabled)));
        }
        finally { activations.TryRemove(fixture.Pid, out _); }
    }

    [Fact]
    public async Task Context_ready_publishes_the_authoritative_server_map_not_the_previous_displayed_map()
    {
        await using var fixture = new LeaseFixture();
        var tactical = fixture.NewState(2, tactical: true);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = tactical;
        var service = new LaunchService();
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        ((IDictionary)typeof(LaunchService).GetField("_serverMapId", hidden)!.GetValue(service)!)[(uint)fixture.Pid] = 5678L;
        ((IDictionary)typeof(LaunchService).GetField("_lastMap", hidden)!.GetValue(service)!)[(uint)fixture.Pid] = 1234L;
        fixture.BeginMap(5678);
        Assert.False(File.Exists(fixture.TacticalLease));

        typeof(LaunchService).GetMethod("OnDecoded", hidden)!.Invoke(service,
            new object[] { new DecodedPacket((uint)fixture.Pid, true, "", "ContextReadyRequest", Array.Empty<ProtoField>(), Array.Empty<byte>()) });

        Assert.Equal("5678", File.ReadAllText(fixture.TacticalLease).Split(' ')[5]);
    }

    [Theory]
    [InlineData("MapChangeRequest")]
    [InlineData("MapInformationRequest")]
    public async Task Outgoing_map_requests_revoke_the_tactical_lease_before_the_next_map(string name)
    {
        await using var fixture = new LeaseFixture();
        var tactical = fixture.NewState(2, tactical: true);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = tactical;
        fixture.BeginMap(1234);
        fixture.MapReady(1234);
        Assert.True(File.Exists(fixture.TacticalLease));
        var service = new LaunchService();
        var decode = typeof(LaunchService).GetMethod("OnDecoded", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var payload = Array.Empty<ProtoField>();

        decode.Invoke(service, new object[] { new DecodedPacket((uint)fixture.Pid, true, "", name, payload, Array.Empty<byte>()) });
        fixture.RefreshTactical(tactical);

        Assert.False(File.Exists(fixture.TacticalLease));
    }

    [Theory]
    [InlineData(false, "MapCurrentEvent")]
    [InlineData(true, "MapChangeRequest")]
    [InlineData(true, "MapInformationRequest")]
    public async Task Map_authority_is_revoked_even_when_a_packet_observer_throws(bool clientToServer, string name)
    {
        await using var fixture = new LeaseFixture();
        var tactical = fixture.NewState(2, tactical: true);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = tactical;
        fixture.BeginMap(1234);
        fixture.MapReady(1234);
        var service = new LaunchService();
        service.PacketDecoded += _ => throw new InvalidOperationException("observer failure");
        var decode = typeof(LaunchService).GetMethod("OnDecoded", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var error = Assert.Throws<TargetInvocationException>(() => decode.Invoke(service,
            new object[] { new DecodedPacket((uint)fixture.Pid, clientToServer, "", name,
                Array.Empty<ProtoField>(), Array.Empty<byte>()) }));

        Assert.IsType<InvalidOperationException>(error.InnerException);
        fixture.RefreshTactical(tactical);
        Assert.False(File.Exists(fixture.TacticalLease));
        Assert.False(File.Exists(fixture.Construction));
    }

    [Fact]
    public async Task Tactical_cleanup_accepts_only_current_disabled_map_acknowledgement()
    {
        await using var fixture = new LeaseFixture();
        var disabled = fixture.NewState(2);
        // Cleanup applies to an already staged module; disabling must not stage one.
        Directory.CreateDirectory(fixture.TacticalRoot);
        var enabled = fixture.NewState(1, tactical: true);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = enabled;
        fixture.BeginMap(1234);
        fixture.MapReady(1234);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = disabled;
        fixture.RefreshTactical(disabled);
        Assert.False(fixture.CleanupAcknowledged(disabled));

        // Native acknowledgement echoes the identity token, not the lease expiry.
        string first = string.Join(" ", File.ReadAllText(fixture.TacticalLease).Split(' ').Take(8));
        File.WriteAllText(fixture.TacticalAck, first);
        Assert.True(fixture.CleanupAcknowledged(disabled));

        fixture.RevokeMap();
        Assert.False(fixture.CleanupAcknowledged(disabled));
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = enabled;
        fixture.BeginMap(5678);
        fixture.MapReady(5678);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = disabled;
        fixture.RefreshTactical(disabled);
        Assert.False(fixture.CleanupAcknowledged(disabled));
        File.WriteAllText(fixture.TacticalAck, string.Join(" ", File.ReadAllText(fixture.TacticalLease).Split(' ').Take(8)));
        Assert.True(fixture.CleanupAcknowledged(disabled));

        var nextDisabled = fixture.NewState(3);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = nextDisabled;
        fixture.RefreshTactical(nextDisabled);
        Assert.False(fixture.CleanupAcknowledged(nextDisabled));
        File.WriteAllText(fixture.TacticalAck, string.Join(" ", File.ReadAllText(fixture.TacticalLease).Split(' ').Take(8)));
        Assert.True(fixture.CleanupAcknowledged(nextDisabled));
        Assert.False(fixture.CleanupAcknowledged(disabled));

        enabled = fixture.NewState(4, tactical: true);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = enabled;
        fixture.RefreshTactical(enabled);
        File.WriteAllText(fixture.TacticalAck, string.Join(" ", File.ReadAllText(fixture.TacticalLease).Split(' ').Take(8)));
        Assert.False(fixture.CleanupAcknowledged(enabled));
        Assert.False(fixture.CleanupAcknowledged(disabled));
    }

    [Fact]
    public async Task Tactical_map_lease_requires_ready_map_and_cannot_return_after_revocation()
    {
        await using var fixture = new LeaseFixture();
        var tactical = fixture.NewState(2, tactical: true);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = tactical;
        string lease = fixture.TacticalLease;

        fixture.RefreshTactical(tactical);
        Assert.False(File.Exists(lease));
        fixture.BeginMap(1234);
        fixture.MapReady(1234);
        var first = File.ReadAllText(lease).Split(' ');
        Assert.Equal("1234", first[5]);
        Assert.Equal("2", first[6]);
        Assert.Equal("1", first[7]);

        fixture.RevokeMap();
        fixture.RefreshTactical(tactical);
        Assert.False(File.Exists(lease));

        fixture.BeginMap(5678);
        fixture.MapReady(5678);
        var second = File.ReadAllText(lease).Split(' ');
        Assert.Equal("5678", second[5]);
        Assert.NotEqual(first[4], second[4]);
        fixture.RefreshTactical(tactical);
        Assert.True(File.Exists(lease));
    }

    [Fact]
    public async Task Tactical_same_map_reload_invalidates_previous_cleanup_receipt_but_duplicate_ready_does_not()
    {
        await using var fixture = new LeaseFixture();
        var enabled = fixture.NewState(2, tactical: true);
        var disabled = fixture.NewState(3);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = enabled;
        fixture.BeginMap(1234);
        fixture.MapReady(1234);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = disabled;
        fixture.RefreshTactical(disabled);
        string token = string.Join(" ", File.ReadAllText(fixture.TacticalLease).Split(' ').Take(8));
        File.WriteAllText(fixture.TacticalAck, token);
        Assert.True(fixture.CleanupAcknowledged(disabled));

        lock (fixture.Gate) fixture.Desired[fixture.Pid] = enabled;
        fixture.MapReady(1234);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = disabled;
        fixture.RefreshTactical(disabled);
        Assert.True(fixture.CleanupAcknowledged(disabled));

        fixture.RevokeMap();
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = enabled;
        fixture.BeginMap(1234);
        fixture.MapReady(1234);
        lock (fixture.Gate) fixture.Desired[fixture.Pid] = disabled;
        fixture.RefreshTactical(disabled);
        Assert.False(fixture.CleanupAcknowledged(disabled));
        string reloadedToken = string.Join(" ", File.ReadAllText(fixture.TacticalLease).Split(' ').Take(8));
        Assert.NotEqual(token, reloadedToken);
        File.WriteAllText(fixture.TacticalAck, reloadedToken);
        Assert.True(fixture.CleanupAcknowledged(disabled));
    }

    [Fact]
    public async Task Combat_end_revokes_immediately_and_heartbeat_cannot_republish_until_next_combat()
    {
        await using var fixture = new LeaseFixture();
        fixture.Combat(true);
        Assert.True(File.Exists(fixture.Lease));

        fixture.Combat(false);
        Assert.False(File.Exists(fixture.Lease));
        Assert.Equal(fixture.RestoreToken, File.ReadAllText(fixture.RestoreRequest));
        Assert.True((bool)fixture.State.GetType().GetProperty("CombatSpeedHack")!.GetValue(fixture.State)!);

        // Invoke the exact heartbeat operation, without waiting for its 750 ms timer.
        fixture.Refresh();
        Assert.False(File.Exists(fixture.Lease));
        Assert.Equal(fixture.RestoreToken, File.ReadAllText(fixture.RestoreRequest));

        fixture.Combat(true);
        Assert.True(File.Exists(fixture.Lease));
        Assert.Equal($"{fixture.StartTicks}:1", File.ReadAllText(fixture.Lease));
        File.SetLastWriteTimeUtc(fixture.Lease, DateTime.UnixEpoch);
        fixture.Refresh();
        Assert.True(File.GetLastWriteTimeUtc(fixture.Lease) > DateTime.UnixEpoch);

        // A superseded heartbeat cannot delete the next profile's live lease.
        fixture.Desired[fixture.Pid] = fixture.NewState(2);
        fixture.Refresh();
        Assert.True(File.Exists(fixture.Lease));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_different_process_combat_owner_rejects_current_enabled_profile(bool differentProcess)
    {
        await using var fixture = new LeaseFixture();
        fixture.Combat(true);
        Assert.True(File.Exists(fixture.Lease));
        lock (fixture.Gate)
        {
            if (differentProcess) fixture.Owners[fixture.Pid] = fixture.StartTicks - 1;
            else fixture.Owners.Remove(fixture.Pid);
        }
        fixture.Refresh();
        Assert.False(File.Exists(fixture.Lease));
        Assert.Equal(fixture.RestoreToken, File.ReadAllText(fixture.RestoreRequest));
    }

    [Fact]
    public async Task StartCombatMap_activates_deferred_native_speedhack_before_monster_turn()
    {
        await using var fixture = new LeaseFixture();
        var service = F06App::GivreClone.App.Services.Stores.Launch;
        var config = F06App::GivreClone.App.Services.AppConfig.Current;
        bool mods = config.ModsEnabled;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            config.ModsEnabled = true;
            service.SetCombatProfile((uint)fixture.Pid, new F06App::GivreClone.App.Services.CombatProfile { Speed = F06App::GivreClone.App.Services.CombatSpeed.Turbo });
            var deferredField = typeof(LaunchService).GetField("_deferredNative", flags)!;
            var deferredMap = (IDictionary)deferredField.GetValue(service)!;
            Assert.True(deferredMap.Contains((uint)fixture.Pid));

            await ((Task)typeof(LaunchService).GetMethod("StartCombatMapAsync", flags)!.Invoke(service, new object[] { (uint)fixture.Pid })!).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(deferredMap.Contains((uint)fixture.Pid));
            Assert.True(File.Exists(fixture.Lease));
        }
        finally
        {
            service.ClearCombatProfile((uint)fixture.Pid);
            config.ModsEnabled = mods;
        }
    }

    private sealed class LeaseFixture : IAsyncDisposable
    {
        private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly Type Runtime = typeof(LaunchService).Assembly.GetType("GivreClone.App.Services.NativeTurboRuntime")!;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "givre-speedgate-" + Guid.NewGuid().ToString("N"));
        private readonly Process _process;
        private SemaphoreSlim? _blockedApply;
        public int Pid => _process.Id;
        public long StartTicks { get; }
        public object State { get; }
        public object Gate => Runtime.GetField("TacticalMapGate", Flags)!.GetValue(null)!;
        public IDictionary Owners => (IDictionary)Runtime.GetField("CombatLeaseOwners", Flags)!.GetValue(null)!;
        public IDictionary Desired => (IDictionary)Runtime.GetField("Desired", Flags)!.GetValue(null)!;
        public string Lease => (string)Runtime.GetMethod("LeasePath", Flags)!.Invoke(null, new object[] { Pid })!;
        public string TacticalRoot => (string)Runtime.GetMethod("StageRoot", Flags)!.Invoke(null, new object[] { Pid })!;
        public string TacticalLease => Path.Combine(TacticalRoot, "tactical-map.lease");
        public string TacticalAck => Path.Combine(TacticalRoot,
            (string)Runtime.GetField("TacticalMapAckFileName", Flags)!.GetRawConstantValue()!);
        public bool CleanupAcknowledged(object state) =>
            (bool)Runtime.GetMethod("TacticalCleanupAcknowledged", Flags)!.Invoke(null, new[] { (object)Pid, state })!;
        public string RestoreRequest => (string)Runtime.GetMethod("RestoreRequestPath", Flags)!.Invoke(null, new object[] { Pid })!;
        public string RestoreToken => (string)Runtime.GetMethod("RestoreToken", Flags)!.Invoke(null, new[] { State })!;

        public LeaseFixture()
        {
            // ProcessStartTicks deliberately rejects testhost. Use a private inert Dofus-named
            // process, never an installed game, and the launcher's existing ownership seam.
            Directory.CreateDirectory(_root);
            string executable = Path.Combine(_root, "Dofus.exe");
            File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), executable);
            _process = Process.Start(new ProcessStartInfo(executable, "/d /q")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            })!;
            try
            {
                StartTicks = _process.StartTime.ToUniversalTime().Ticks;
                Assert.Equal(true, typeof(MZP9OwnedClientRegistry).GetMethod("EnrollNewlyStarted", Flags)!.Invoke(null, new object[] { _process }));
                Assert.Equal(StartTicks, Runtime.GetMethod("ProcessStartTicks", Flags)!.Invoke(null, new object[] { Pid }));
                // Seed desired state directly: no injection, native apply task, or timer is scheduled.
                State = NewState(1);
                lock (Gate) Desired[Pid] = State;
            }
            catch
            {
                _process.Kill();
                _process.WaitForExit();
                _process.Dispose();
                Directory.Delete(_root, true);
                throw;
            }
        }

        public object NewState(long generation, bool tactical = false) => Activator.CreateInstance(Runtime.GetNestedType("DesiredState", BindingFlags.NonPublic)!,
            new object[] { true, true, false, tactical, 0, 0, 0, "", "", generation, StartTicks, false })!;

        public void BeginMap(long map, bool beforeForward = true) => Runtime.GetMethod("BeginTacticalMapConstruction", Flags)!.Invoke(null, new object[] { Pid, map, beforeForward });
        public string Construction => Path.Combine(TacticalRoot, "tactical-map.construction");
        public long TransitionEpoch
        {
            get
            {
                lock (Gate)
                {
                    var transitions = (IDictionary)Runtime.GetField("TacticalTransitions", Flags)!.GetValue(null)!;
                    object transition = transitions[Pid]!;
                    return (long)transition.GetType().GetProperty("Epoch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transition)!;
                }
            }
        }
        public void RefreshConstruction(object state)
        {
            lock (Gate) Runtime.GetMethod("RefreshTacticalConstructionLocked", Flags)!.Invoke(null, new[] { (object)Pid, state });
        }
        public void MapReady(long map) => Runtime.GetMethod("PublishTacticalMapReady", Flags)!.Invoke(null, new object[] { Pid, map });
        public void RevokeMap() => Runtime.GetMethod("RevokeTacticalMapReady", Flags)!.Invoke(null, new object[] { Pid });
        public void RefreshTactical(object state)
        {
            lock (Gate) Runtime.GetMethod("RefreshTacticalLeaseLocked", Flags)!.Invoke(null, new[] { (object)Pid, state });
        }

        public void Combat(bool active) => Runtime.GetMethod("SetCombatActive", Flags)!.Invoke(null, new object[] { Pid, active });
        public void Request(bool enabled) => Runtime.GetMethod("RequestState", Flags)!.Invoke(null,
            new object[] { Pid, enabled, false, false, false, false, "RecoveryRegression" });
        public async Task BlockApplyAsync()
        {
            var gates = (System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim>)Runtime.GetField("Gates", Flags)!.GetValue(null)!;
            var gate = gates.GetOrAdd(Pid, _ => new SemaphoreSlim(1, 1));
            Assert.True(await gate.WaitAsync(TimeSpan.FromSeconds(10)));
            _blockedApply = gate;
        }
        public void Refresh() => Runtime.GetMethod("RefreshLease", Flags)!.Invoke(null, new[] { (object)Pid, State });

        private static async Task DeleteExitedFixtureDirectoryAsync(string root)
        {
            // On Windows the image section for our private, already-exited
            // cmd.exe copy can still briefly deny deletion after WaitForExitAsync
            // and Process.Dispose. Retry only the fixture-owned temporary tree;
            // exhaustion remains a test failure, never a silent cleanup skip.
            const int maxRetries = 20;
            for (int retry = 0; ; retry++)
            {
                try { Directory.Delete(root, recursive: true); return; }
                catch (UnauthorizedAccessException) when (retry < maxRetries) { }
                catch (IOException) when (retry < maxRetries) { }
                await Task.Delay(25).ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            RevokeMap();
            try { if (Directory.Exists(TacticalRoot)) Directory.Delete(TacticalRoot, true); } catch { }
            Combat(false);
            lock (Gate)
            {
                Desired.Remove(Pid);
                Owners.Remove(Pid);
            }
            _blockedApply?.Release();
            string leaseRoot = Path.GetDirectoryName(Lease)!;
            try { if (Directory.Exists(leaseRoot)) Directory.Delete(leaseRoot, true); } catch { }
            Task exited = _process.WaitForExitAsync();
            try
            {
                await _process.StandardInput.WriteLineAsync("exit");
                _process.StandardInput.Close();
                await exited.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                if (!_process.HasExited) { _process.Kill(); await exited.WaitAsync(TimeSpan.FromSeconds(10)); }
                _process.Dispose();
                try { if (Directory.Exists(TacticalRoot)) Directory.Delete(TacticalRoot, true); } catch { }
                try { if (Directory.Exists(leaseRoot)) Directory.Delete(leaseRoot, true); } catch { }
                await DeleteExitedFixtureDirectoryAsync(_root).ConfigureAwait(false);
            }
        }
    }
}
