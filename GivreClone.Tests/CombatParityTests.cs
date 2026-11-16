extern alias F06App;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Transport;
using Xunit;
using Xunit.Sdk;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Packet = F06App::GivreClone.App.Services.DecodedPacket;
using Profile = F06App::GivreClone.App.Services.CombatProfile;
using Style = F06App::GivreClone.App.Services.CombatStyle;
using Mode = F06App::GivreClone.App.Services.PlacementMode;
using Policy = F06App::GivreClone.App.Services.CombatMovementPolicy;
using Import = F06App::GivreClone.App.Services.CombatImportLogic;
using Overrides = F06App::GivreClone.App.Services.ScriptCombatOverrides;
using Config = F06App::GivreClone.App.Services.AppConfig;

namespace GivreClone.Tests;

[Collection("Transport journal and timing")]
public sealed class CombatParityTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const uint Pid = 91001;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(
        Path.Combine(AppContext.BaseDirectory, "data"));

    private static FieldInfo Field(string name) => typeof(Launch).GetField(name, Private)!;
    private static IDictionary Map(Launch service, string name) => (IDictionary)Field(name).GetValue(service)!;
    private static object? Call(Launch service, string method, params object?[] args)
        => typeof(Launch).GetMethod(method, Private)!.Invoke(service, args);

    private static Launch Bare()
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (var field in typeof(Launch).GetFields(Private | BindingFlags.Public))
        {
            if (field.GetValue(service) is not null) continue;
            if (!field.FieldType.IsGenericType) continue;
            if (field.FieldType.GetGenericTypeDefinition() != typeof(ConcurrentDictionary<,>)) continue;
            field.SetValue(service, Activator.CreateInstance(field.FieldType));
        }
        return service;
    }

    private static Launch Fight()
    {
        var service = Bare();
        foreach (string name in new[] { "_engineControlled", "_controlledHistory", "_charId", "_fighters",
            "_fighterStats", "_fighterTeam", "_allies", "_confirmedEnemies" })
        {
            if (Field(name).GetValue(service) is null)
                Field(name).SetValue(service, Activator.CreateInstance(Field(name).FieldType));
        }
        Map(service, "_charId")[Pid] = 999L;
        Map(service, "_fighters")[Pid] = new List<(int Cell, long ActorId)> { (500, 999), (200, -1) };
        return service;
    }

    private static Launch Locks(out List<byte[]> sent)
    {
        var service = Bare();
        var profile = new Profile { LockSpectators = true, LockParty = true };
        Map(service, "_combat")[Pid] = profile;
        Map(service, "_inFight")[Pid] = true;
        Map(service, "_fightStartAt")[Pid] = 42L;
        Map(service, "_fightId")[Pid] = 7L;
        Map(service, "_placementOpen")[Pid] = true;
        var frames = new List<byte[]>();
        Map(service, "_socketInjectors")[Pid] = (Func<byte[], bool>)(frame => { frames.Add(frame); return true; });
        sent = frames;
        return service;
    }

    [Fact]
    public void Spectator_and_party_locks_are_sent_before_any_delay_when_placement_is_open()
    {
        var service = Locks(out var sent);
        var lines = new List<string>();
        void On(uint who, string line) { if (who == Pid || line.Contains($"pid {Pid}")) lines.Add(line); }
        bool was = TransportLog.Enabled;
        TransportLog.Enabled = true;
        TransportLog.EmittedWithPid += On;
        IReadOnlyList<int>? applied = null;
        service.FightLocksApplied += (_, opts) => applied = opts;
        try
        {
            var task = (Task)Call(service, "ApplyFightLocksAsync", Pid, 7L)!;
            Assert.True(task.IsCompletedSuccessfully, task.IsFaulted
                ? task.Exception!.GetBaseException().ToString()
                : "verrous 0/1 encore en attente : un délai précède l'envoi");
            var secret = MoveForge.BuildFightOptionToggleFramed(0);
            var party = MoveForge.BuildFightOptionToggleFramed(1);
            var closed = MoveForge.BuildFightOptionToggleFramed(2);
            Assert.NotEmpty(secret);
            Assert.NotEmpty(party);
            Assert.Equal(2, sent.Count);
            Assert.True(secret.SequenceEqual(sent[0]));
            Assert.True(party.SequenceEqual(sent[1]));
            Assert.DoesNotContain(sent, frame => frame.SequenceEqual(closed));
            Assert.Equal(new[] { 0, 1 }, applied);
            Assert.DoesNotContain(lines, line => line.Contains("verrous non envoyés"));
        }
        finally
        {
            TransportLog.EmittedWithPid -= On;
            TransportLog.Enabled = was;
        }
    }

    [Fact]
    public void Locks_log_why_nothing_was_sent_and_a_later_open_placement_still_sends()
    {
        var service = Locks(out var sent);
        Map(service, "_placementOpen").Remove(Pid);
        var lines = new List<string>();
        void On(uint who, string line) { if (who == Pid || line.Contains($"pid {Pid}")) lines.Add(line); }
        bool was = TransportLog.Enabled;
        TransportLog.Enabled = true;
        TransportLog.EmittedWithPid += On;
        try
        {
            var waiting = (Task)Call(service, "ApplyFightLocksAsync", Pid, 7L)!;
            Assert.True(waiting.IsCompletedSuccessfully, waiting.IsFaulted
                ? waiting.Exception!.GetBaseException().ToString()
                : "l'absence de placement ne doit pas attendre le délai");
            Assert.Empty(sent);
            Assert.Contains(lines, line => line.Contains("verrous non envoyés") && line.Contains("placement pas encore ouvert"));
            int skipped = lines.Count(line => line.Contains("placement pas encore ouvert"));
            var repeat = (Task)Call(service, "ApplyFightLocksAsync", Pid, 7L)!;
            Assert.True(repeat.IsCompletedSuccessfully);
            Assert.Equal(skipped, lines.Count(line => line.Contains("placement pas encore ouvert")));

            Map(service, "_placementOpen")[Pid] = false;
            var closed = (Task)Call(service, "ApplyFightLocksAsync", Pid, 7L)!;
            Assert.True(closed.IsCompletedSuccessfully);
            Assert.Empty(sent);
            Assert.Contains(lines, line => line.Contains("placement fermé"));

            Map(service, "_placementOpen")[Pid] = true;
            Call(service, "SendSpectatorAndPartyLocks", Pid, 7L);
            Assert.Equal(2, sent.Count);
            Assert.True(MoveForge.BuildFightOptionToggleFramed(0).SequenceEqual(sent[0]));
            Assert.True(MoveForge.BuildFightOptionToggleFramed(1).SequenceEqual(sent[1]));
        }
        finally
        {
            TransportLog.EmittedWithPid -= On;
            TransportLog.Enabled = was;
        }
    }

    [Fact]
    public void Follower_and_already_toggled_options_send_nothing_and_say_why()
    {
        var follower = Locks(out var followerFrames);
        Map(follower, "_teamFollower")[Pid] = true;
        var lines = new List<string>();
        void On(uint who, string line) { if (who == Pid || line.Contains($"pid {Pid}")) lines.Add(line); }
        bool was = TransportLog.Enabled;
        TransportLog.Enabled = true;
        TransportLog.EmittedWithPid += On;
        try
        {
            var task = (Task)Call(follower, "ApplyFightLocksAsync", Pid, 7L)!;
            Assert.True(task.IsCompletedSuccessfully);
            Assert.Empty(followerFrames);
            Assert.Contains(lines, line => line.Contains("suiveur"));

            var again = Locks(out var frames);
            var state = new ConcurrentDictionary<int, bool> { [0] = true, [1] = true };
            Map(again, "_fightOptState")[Pid] = state;
            lines.Clear();
            var skipped = (Task)Call(again, "ApplyFightLocksAsync", Pid, 7L)!;
            Assert.True(skipped.IsCompletedSuccessfully);
            Assert.Empty(frames);
            Assert.Contains(lines, line => line.Contains("déjà actifs ou déjà basculés ce combat"));
        }
        finally
        {
            TransportLog.EmittedWithPid -= On;
            TransportLog.Enabled = was;
        }
    }

    [Fact]
    public void A_sent_option_is_not_toggled_again()
    {
        var service = Locks(out var sent);
        var first = (Task)Call(service, "ApplyFightLocksAsync", Pid, 7L)!;
        Assert.True(first.IsCompletedSuccessfully, first.Exception?.GetBaseException().ToString());
        Assert.Equal(2, sent.Count);
        Call(service, "SendSpectatorAndPartyLocks", Pid, 7L);
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public void Placement_mode_is_independent_of_style_and_keep_stays_or_sends_nothing()
    {
        var bare = Fight();
        Field("_combat").SetValue(bare, null);
        var offered = CellsAroundEnemy(200);
        int legacyNear = (int)Call(bare, "ChoosePlacementCell", Pid, offered, Style.Aggressive)!;
        int legacyFar = (int)Call(bare, "ChoosePlacementCell", Pid, offered, Style.Fleeing)!;
        Assert.NotEqual(legacyNear, legacyFar);

        var service = Fight();
        var profile = new Profile { Style = Style.Aggressive, PlacementMode = Mode.Far };
        Map(service, "_combat")[Pid] = profile;
        Assert.Equal(legacyFar, (int)Call(service, "ChoosePlacementCell", Pid, offered, Style.Aggressive)!);
        profile.PlacementMode = Mode.Near;
        profile.Style = Style.Fleeing;
        Assert.Equal(legacyNear, (int)Call(service, "ChoosePlacementCell", Pid, offered, Style.Fleeing)!);

        int closer = CloserThan(500, 200);
        profile.PlacementMode = Mode.Keep;
        profile.Style = Style.Aggressive;
        Assert.Equal(500, (int)Call(service, "ChoosePlacementCell", Pid, new[] { 500, closer }, Style.Aggressive)!);
        Assert.NotEqual(500, (int)Call(bare, "ChoosePlacementCell", Pid, new[] { 500, closer }, Style.Aggressive)!);
        Assert.Equal(-1, (int)Call(service, "ChoosePlacementCell", Pid, new[] { closer }, Style.Aggressive)!);
    }

    [Fact]
    public void Never_approach_spends_the_least_steps_for_an_aggressive_end_cell_and_fleeing_still_kites()
    {
        var service = Fight();
        var (near, far) = Ends(200);
        var planType = typeof(Launch).GetNestedType("ChallengePlan", Private)!;
        var plan = Activator.CreateInstance(planType, nonPublic: true)!;
        int Pick(Profile profile, List<KeyValuePair<int, int>> cands)
            => (int)Call(service, "PreferEndCell", Pid, plan, profile, cands, null)!;

        var approach = new List<KeyValuePair<int, int>> { new(near, 4), new(far, 1) };
        var standard = new Profile { Style = Style.Aggressive, MovementPolicy = Policy.Standard };
        var never = new Profile { Style = Style.Aggressive, MovementPolicy = Policy.NeverApproach };
        Assert.True(standard.AllowsApproach);
        Assert.False(never.AllowsApproach);
        Assert.Equal(near, Pick(standard, approach));
        Assert.Equal(far, Pick(never, approach));

        var kite = new List<KeyValuePair<int, int>> { new(near, 1), new(far, 4) };
        var flee = new Profile { Style = Style.Fleeing, MovementPolicy = Policy.NeverApproach };
        Assert.Equal(far, Pick(new Profile { Style = Style.Fleeing }, kite));
        Assert.Equal(far, Pick(flee, kite));
        Assert.Equal(near, Pick(never, kite));
    }

    [Fact]
    public void Placement_and_movement_policy_survive_import_clone_and_json()
    {
        var source = new Profile
        {
            Style = Style.Passive,
            PlacementMode = Mode.Far,
            MovementPolicy = Policy.NeverApproach,
        };
        var target = new Profile { Style = Style.Aggressive };
        Import.Apply(target, source, new HashSet<string> { "PlacementMode", "MovementPolicy" }, new HashSet<int>(), false);
        Assert.Equal(Mode.Far, target.PlacementMode);
        Assert.Equal(Policy.NeverApproach, target.MovementPolicy);
        Assert.Equal(Style.Aggressive, target.Style);

        var clone = source.Clone();
        Assert.Equal(Mode.Far, clone.PlacementMode);
        Assert.Equal(Policy.NeverApproach, clone.MovementPolicy);

        string json = JsonSerializer.Serialize(source);
        Assert.Contains("Far", json);
        Assert.Contains("NeverApproach", json);
        Assert.DoesNotContain("AllowsApproach", json);
        var restored = JsonSerializer.Deserialize<Profile>(json)!;
        Assert.Equal(Mode.Far, restored.PlacementMode);
        Assert.False(restored.AllowsApproach);
        var empty = JsonSerializer.Deserialize<Profile>("{}")!;
        Assert.Equal(Mode.LegacyByStyle, empty.PlacementMode);
        Assert.Equal(Policy.Standard, empty.MovementPolicy);
        Assert.True(empty.AllowsApproach);
        Assert.Equal(Mode.LegacyByStyle, new Profile().PlacementMode);
    }

    [Fact]
    public void Set_approach_distance_holds_min_and_max_only_inside_the_callback()
    {
        var exact = new Overrides();
        exact.HoldExactApproachDistance(3);
        var profile = new Profile { KiteMinDistance = 1, KiteMaxDistance = 8 };
        exact.ApplyTo(profile, true);
        Assert.Equal(3, profile.KiteMinDistance);
        Assert.Equal(3, profile.KiteMaxDistance);

        var minOnly = new Overrides { KiteMin = 2 };
        var untouched = new Profile { KiteMaxDistance = 8 };
        minOnly.ApplyTo(untouched, true);
        Assert.Equal(2, untouched.KiteMinDistance);
        Assert.Equal(8, untouched.KiteMaxDistance);

        var high = new Overrides();
        high.HoldExactApproachDistance(99);
        Assert.Equal(20, high.KiteMin);
        Assert.Equal(20, high.KiteMax);
        var low = new Overrides();
        low.HoldExactApproachDistance(-4);
        Assert.Equal(0, low.KiteMin);
        Assert.Equal(0, low.KiteMax);
    }

    [Fact]
    public void Reward_waiter_accepts_only_the_matching_result_and_the_ledger_still_sees_the_other()
    {
        var service = Bare();
        Map(service, "_inFight")[Pid] = false;
        var stateType = typeof(Launch).GetNestedType("LotMState", Private)!;
        var state = Activator.CreateInstance(stateType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, null, null)!;
        Map(service, "_lotM")[Pid] = state;
        var ledger = stateType.GetField("Ledger")!.GetValue(state)!;
        ledger.GetType().GetMethod("OnFinished")!.Invoke(ledger, new object[] { 88, 1 });
        ledger.GetType().GetMethod("OnFinished")!.Invoke(ledger, new object[] { 77, 1 });
        var has = ledger.GetType().GetMethod("HasReward")!.CreateDelegate<Func<int?, bool>>(ledger);
        var done = ledger.GetType().GetMethod("IsDone")!.CreateDelegate<Func<int, bool>>(ledger);
        Assert.True(has(88));
        Assert.True(has(77));

        var tcs = new TaskCompletionSource<Packet>(TaskCreationOptions.RunContinuationsAsynchronously);
        Map(service, "_codeWait")[Pid] = (
            new HashSet<string> { "AchievementRewardResultEvent" },
            (Func<Packet, bool>)(packet => LotMReader.RewardResult(packet.Payload).Id == 77),
            tcs);

        bool dump = Config.Current.PacketDump;
        Config.Current.PacketDump = false;
        try
        {
            Call(service, "OnDecoded", Reward(88, true));
            Assert.False(has(88));
            Assert.True(has(77));
            Assert.False(tcs.Task.IsCompleted);
            Assert.True(Map(service, "_codeWait").Contains(Pid));

            Call(service, "OnDecoded", Reward(77, true));
            Assert.False(has(77));
            Assert.True(tcs.Task.IsCompletedSuccessfully);
            Assert.False(Map(service, "_codeWait").Contains(Pid));

            Call(service, "OnDecoded", Achievements(5));
            Assert.True(done(5));
            Assert.False(has(5));
            var claimable = (IEnumerable)ledger.GetType().GetMethod("AutoClaimable")!.Invoke(ledger, null)!;
            Assert.Empty(claimable.Cast<object>());
        }
        finally { Config.Current.PacketDump = dump; }
    }

    private static int[] CellsAroundEnemy(int enemy)
    {
        var (near, far) = Ends(enemy);
        return new[] { near, far };
    }

    private static (int Near, int Far) Ends(int enemy)
    {
        int near = -1, far = -1, nearD = int.MaxValue, farD = -1;
        for (int cell = 0; cell < MapPathfinder.CellCount; cell++)
        {
            if (cell == enemy || cell == 500) continue;
            int distance = MapPathfinder.CellDistance(cell, enemy);
            if (distance == int.MaxValue) continue;
            if (distance < nearD) { nearD = distance; near = cell; }
            if (distance > farD) { farD = distance; far = cell; }
        }
        Assert.NotEqual(near, far);
        Assert.True(nearD < farD);
        return (near, far);
    }

    private static int CloserThan(int cell, int enemy)
    {
        int mine = MapPathfinder.CellDistance(cell, enemy);
        for (int other = 0; other < MapPathfinder.CellCount; other++)
        {
            if (other == cell || other == enemy) continue;
            int distance = MapPathfinder.CellDistance(other, enemy);
            if (distance < mine) return other;
        }
        throw new XunitException($"aucune case plus proche de {enemy} que {cell}");
    }

    private static Packet Reward(int id, bool success) => new(Pid, false, "", "AchievementRewardResultEvent",
        new ProtoField[]
        {
            new()
            {
                Field = Catalog.FieldNumber("AchievementRewardResultEvent", "achievement_id")!.Value,
                Wire = WireType.Varint, Varint = (ulong)id,
            },
            new()
            {
                Field = Catalog.FieldNumber("AchievementRewardResultEvent", "success")!.Value,
                Wire = WireType.Varint, Varint = success ? 1UL : 0UL,
            },
        }, Array.Empty<byte>());

    private static Packet Achievements(int id)
    {
        int list = Catalog.FieldNumber("AchievementsEvent", "achieved_achievements")!.Value;
        int achievement = Catalog.FieldNumber("AchievedAchievement", "achievement_id")!.Value;
        return new Packet(Pid, false, "", "AchievementsEvent", new ProtoField[]
        {
            new()
            {
                Field = list, Wire = WireType.Len,
                Message = new List<ProtoField>
                {
                    new() { Field = achievement, Wire = WireType.Varint, Varint = (ulong)id },
                },
            },
        }, Array.Empty<byte>());
    }
}
