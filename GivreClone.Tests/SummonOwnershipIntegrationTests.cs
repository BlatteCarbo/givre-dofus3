extern alias F06App;
using System.Collections;
using System.Reflection;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using DecodedPacket = F06App::GivreClone.App.Services.DecodedPacket;
using Xunit;

namespace GivreClone.Tests;

public sealed class SummonOwnershipIntegrationTests
{
    [Fact]
    public void Living_tree_ownership_does_not_consume_the_official_summon_capacity()
    {
        var service = new LaunchService();
        using var db = F06App::GivreClone.App.Services.LotM.GameDb.TryOpen(RealGameData.Path!)!;
        const uint pid = 109;
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        IDictionary Map(string name) => (IDictionary)typeof(LaunchService).GetField(name, hidden)!.GetValue(service)!;
        typeof(LaunchService).GetField("_gameDb", hidden)!.SetValue(service, db);
        Map("_charId")[pid] = 1L;
        Map("_inFight")[pid] = true;
        Map("_fighters")[pid] = new List<(int Cell, long ActorId)> { (100, 1) };
        var summon = typeof(LaunchService).GetMethod("OnSummoned", hidden)!;
        summon.Invoke(service, new object?[] { pid, new FightStatsReader.SummonedFighter(-1, 102, 0, 1), 1L });
        summon.Invoke(service, new object?[] { pid, new FightStatsReader.SummonedFighter(-2, 103, 0, 1), 1L });
        Map("_fightMonsters")[pid] = new System.Collections.Concurrent.ConcurrentDictionary<long, FightMonster>(
            new[] { new KeyValuePair<long, FightMonster>(-1, new(-1, 5894, 1, null, 1)),
                new KeyValuePair<long, FightMonster>(-2, new(-2, 5899, 3, null, 1)) });

        Assert.All(service.FightersDetail(pid).Where(f => f.ActorId < 0), f => Assert.True(f.IsMySummon));
        int count = (int)typeof(LaunchService).GetMethod("CountMySummons", hidden)!.Invoke(service, new object[] { pid })!;
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData(1, true, true, true, true)]
    [InlineData(1, true, true, true, false)]
    [InlineData(-20000, true, true, false, true)]
    [InlineData(0, true, true, false, true)]
    [InlineData(1, false, true, false, true)]
    [InlineData(1, true, false, false, true)]
    public void Shown_fighter_with_explicit_living_summon_identity_restores_ownership(long owner, bool alive, bool summoned, bool mine, bool hasRoster)
    {
        var service = new LaunchService();
        const uint pid = 105;
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        IDictionary Map(string name) => (IDictionary)typeof(LaunchService).GetField(name, hidden)!.GetValue(service)!;
        Map("_charId")[pid] = 1L;
        Map("_inFight")[pid] = true;
        if (hasRoster) Map("_fighters")[pid] = new List<(int Cell, long ActorId)> { (100, 1) };
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        FightStatsReader.UseCatalog(catalog.FieldNumber);
        int F(string message, string field) => catalog.FieldNumber(message, field)
            ?? throw new InvalidOperationException($"Missing mapping {message}.{field}");
        static ProtoField I(int field, long value) => new()
            { Field = field, Wire = WireType.Varint, Varint = unchecked((ulong)value) };
        static ProtoField M(int field, params ProtoField[] children) => new()
            { Field = field, Wire = WireType.Len, Message = children.ToList() };
        var payload = new[] { M(F("FightFighterShowEvent", "information"),
            I(F("ActorPositionInformation", "actor_id"), -1),
            M(F("ActorPositionInformation", "disposition"), I(F("EntityDisposition", "cell_id"), 102)),
            M(F("ActorPositionInformation", "actor_information"), M(F("ActorInformation", "fighter"),
                M(F("FightFighterInformation", "spawn_information"), I(F("SpawnInformation", "alive"), alive ? 1 : 0)),
                M(F("FightFighterInformation", "stats"), I(F("FightCharacteristics", "summoned"), summoned ? 1 : 0),
                    I(F("FightCharacteristics", "summoner"), owner))))) };
        uint scope = GivreLauncher.Core.Transport.TransportLog.Scope.Value;
        try
        {
            typeof(LaunchService).GetMethod("OnDecoded", hidden)!.Invoke(service,
                new object[] { new DecodedPacket(pid, false, catalog.CodeForName("FightFighterShowEvent")!,
                    "FightFighterShowEvent", payload, Array.Empty<byte>()) });
            var fighters = service.FightersDetail(pid);
            if (alive && summoned && owner != 0)
                Assert.Contains(fighters, f => f.ActorId == -1);
            else
                Assert.DoesNotContain(fighters, f => f.ActorId == -1);
            if (!hasRoster)
            {
                // SelfActorId requires the player's presence in the authoritative roster.
                var positions = new[]
                {
                    M(F("EntitiesDispositionEvent", "dispositions"), I(F("EntityDisposition", "entity_id"), 1),
                        I(F("EntityDisposition", "cell_id"), 100)),
                    M(F("EntitiesDispositionEvent", "dispositions"), I(F("EntityDisposition", "entity_id"), -1),
                        I(F("EntityDisposition", "cell_id"), 103))
                };
                typeof(LaunchService).GetMethod("OnDecoded", hidden)!.Invoke(service,
                    new object[] { new DecodedPacket(pid, false, catalog.CodeForName("EntitiesDispositionEvent")!,
                        "EntitiesDispositionEvent", positions, Array.Empty<byte>()) });
                fighters = service.FightersDetail(pid);
                Assert.Equal(103, Assert.Single(fighters, f => f.ActorId == -1).Cell);
            }
            var shown = fighters.SingleOrDefault(f => f.ActorId == -1);
            Assert.Equal(mine, shown.IsMySummon);
            int count = (int)typeof(LaunchService).GetMethod("CountMySummons", hidden)!.Invoke(service, new object[] { pid })!;
            Assert.Equal(mine ? 1 : 0, count);
        }
        finally { GivreLauncher.Core.Transport.TransportLog.Scope.Value = scope; }
    }

    [Fact]
    public void Forget_removes_old_fighters_and_ownership_before_the_same_pid_is_reused()
    {
        var service = new LaunchService();
        const uint pid = 101, other = 102;
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        IDictionary Map(string name) => (IDictionary)typeof(LaunchService).GetField(name, hidden)!.GetValue(service)!;
        var summon = typeof(LaunchService).GetMethod("OnSummoned", hidden)!;
        foreach (uint client in new[] { pid, other })
        {
            Map("_charId")[client] = 1L;
            Map("_inFight")[client] = true;
            Map("_fightId")[client] = 987L;
            Map("_myTurnOpen")[client] = true;
            Map("_fighters")[client] = new List<(int Cell, long ActorId)> { (100, 1) };
            summon.Invoke(service, new object?[] { client, new FightStatsReader.SummonedFighter(-1, 102, 0, 1), 1L });
        }
        using var turnStop = new CancellationTokenSource();
        Map("_automaticTurnStop")[pid] = turnStop;
        Assert.True(service.FightersDetail(pid).Single(f => f.ActorId == -1).IsMySummon);

        service.Forget(pid);

        Assert.Empty(service.FightersDetail(pid));
        Assert.Equal(0, service.CurrentFightId(pid));
        Assert.False(service.IsMyTurnOpen(pid));
        Assert.True(turnStop.IsCancellationRequested);
        Assert.True(service.FightersDetail(other).Single(f => f.ActorId == -1).IsMySummon);
        Assert.Equal(987, service.CurrentFightId(other));
        // A fresh roster may reuse actor IDs without replaying old ownership.
        Map("_charId")[pid] = 1L;
        Map("_fighters")[pid] = new List<(int Cell, long ActorId)> { (100, 1), (102, -1) };
        Assert.False(service.FightersDetail(pid).Single(f => f.ActorId == -1).IsMySummon);
    }

    [Fact]
    public void Summon_capacity_and_script_ownership_follow_the_actor_not_the_observation_turn()
    {
        var service = new LaunchService();
        const uint pid = 101;
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        IDictionary Map(string name) => (IDictionary)typeof(LaunchService).GetField(name, hidden)!.GetValue(service)!;
        Map("_fighters")[pid] = new List<(int Cell, long ActorId)> { (100, 1), (101, -20000) };
        Map("_charId")[pid] = 1L;
        Map("_inFight")[pid] = true;
        typeof(LaunchService).GetMethod("ObserveControlledFighter", hidden)!.Invoke(service,
            new object[] { pid, new ControlledFighter(1, -20000, new Dictionary<int, int>()) });
        var summon = typeof(LaunchService).GetMethod("OnSummoned", hidden)!;
        summon.Invoke(service, new object?[] { pid, new FightStatsReader.SummonedFighter(-1, 102, 0, 1), 1L });
        summon.Invoke(service, new object?[] { pid, new FightStatsReader.SummonedFighter(-2, 103, 0, -20000), -20000L });
        summon.Invoke(service, new object?[] { pid, new FightStatsReader.SummonedFighter(-3, 104, 0, -20000), -20000L });
        int Count() => (int)typeof(LaunchService).GetMethod("CountMySummons", hidden)!.Invoke(service, new object[] { pid })!;
        Assert.Equal(1, Count());

        typeof(LaunchService).GetMethod("ObserveServerFightTurn", hidden)!.Invoke(service, new object[] { pid, -20000L });
        var local = typeof(LaunchService).GetField("_engineControlled", hidden)!.GetValue(service)!;
        local.GetType().GetProperty("Value")!.SetValue(local, Map("_controlledCombat")[pid]);
        try
        {
            Assert.Equal(2, Count());
            var fighters = service.FightersDetail(pid);
            Assert.False(fighters.Single(f => f.ActorId == -1).IsMySummon);
            Assert.True(fighters.Single(f => f.ActorId == -2).IsMySummon);
            Assert.True(fighters.Single(f => f.ActorId == -3).IsMySummon);

            var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
            int F(string message, string field) => catalog.FieldNumber(message, field)
                ?? throw new InvalidOperationException($"Missing mapping {message}.{field}");
            var death = new ProtoField
            {
                Field = F("GameActionFightEvent", "death"), Wire = WireType.Len,
                Message = new List<ProtoField> { new()
                {
                    Field = F("Death", "target_id"), Wire = WireType.Varint,
                    Varint = unchecked((ulong)-2L)
                } }
            };
            typeof(LaunchService).GetMethod("OnDecoded", hidden)!.Invoke(service,
                new object[] { new DecodedPacket(pid, false, "jwe", "GameActionFightEvent", new[] { death }, Array.Empty<byte>()) });

            Assert.Equal(1, Count());
            Assert.DoesNotContain(service.FightersDetail(pid), f => f.ActorId == -2);
            Assert.True(service.FightersDetail(pid).Single(f => f.ActorId == -3).IsMySummon);
        }
        finally { local.GetType().GetProperty("Value")!.SetValue(local, null); }
    }
}
