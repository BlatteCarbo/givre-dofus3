extern alias F06App;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using DecodedPacket = F06App::GivreClone.App.Services.DecodedPacket;
using Xunit;

namespace GivreClone.Tests;

public class WaveTeamSynchronizationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly ProtocolCatalog Catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static int F(string message, string field) => Catalog.FieldNumber(message, field)
        ?? throw new InvalidOperationException($"Missing mapping {message}.{field}");
    private static object? Call(LaunchService service, string name, params object[] args) =>
        typeof(LaunchService).GetMethod(name, Private)!.Invoke(service, args);
    private static IDictionary Map(LaunchService service, string name) =>
        (IDictionary)typeof(LaunchService).GetField(name, Private)!.GetValue(service)!;
    private static ProtoField I(int field, long value) => new()
        { Field = field, Wire = WireType.Varint, Varint = unchecked((ulong)value) };
    private static ProtoField M(int field, params ProtoField[] children) => new()
        { Field = field, Wire = WireType.Len, Message = children.ToList() };

    // Synthetic entries resolved through the current mapping, including a proto3 omitted zero team.
    private static ProtoField Fighter(long id, int? team, bool complete)
    {
        var fighter = new List<ProtoField> { I(F("FightFighterInformation", "wave"), 2) };
        if (team is int t)
            fighter.Add(M(F("FightFighterInformation", "spawn_information"),
                t == 0 ? I(F("SpawnInformation", "alive"), 1) : I(F("SpawnInformation", "team"), t)));
        if (complete)
            fighter.Add(M(F("FightFighterInformation", "stats"),
                I(F("FightCharacteristics", "invisibility_state"), 2),
                M(F("FightCharacteristics", "characteristics"),
                    I(F("CharacterCharacteristic", "characteristic_id"), FightStatsReader.IdPa),
                    M(F("CharacterCharacteristic", "value"), I(F("CharacterCharacteristicValue", "total"), 6)))));
        var entry = new List<ProtoField>
        {
            I(F("ActorPositionInformation", "actor_id"), id),
            M(F("ActorPositionInformation", "actor_information"), M(F("ActorInformation", "fighter"), fighter.ToArray()))
        };
        if (complete) entry.Add(M(F("ActorPositionInformation", "disposition"), I(F("EntityDisposition", "cell_id"), 105)));
        return M(F("FightSynchronizeEvent", "fighters"), entry.ToArray());
    }

    private static LaunchService Service(int mine)
    {
        var service = new LaunchService();
        typeof(LaunchService).GetField("_catalog", Private)!.SetValue(service, Catalog);
        FightStatsReader.UseCatalog(Catalog.FieldNumber);
        FightVisibilityReader.UseCatalog(Catalog.FieldNumber);
        Map(service, "_inFight")[1u] = true;
        Map(service, "_charId")[1u] = 999L;
        Map(service, "_fighters")[1u] = new List<(int Cell, long ActorId)> { (100, 999), (101, -4) };
        Map(service, "_fighterTeam")[1u] = new ConcurrentDictionary<long, int>(new[]
        {
            new KeyValuePair<long, int>(999, mine), new KeyValuePair<long, int>(-4, mine)
        });
        return service;
    }

    private static void Dispatch(LaunchService service, string name, params ProtoField[] payload)
    {
        uint scope = GivreLauncher.Core.Transport.TransportLog.Scope.Value;
        try { Call(service, "OnDecoded", new DecodedPacket(1, false, Catalog.CodeForName(name)!, name, payload, Array.Empty<byte>())); }
        finally { GivreLauncher.Core.Transport.TransportLog.Scope.Value = scope; }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void Wave_sync_refreshes_known_enemy_team_even_without_stats_or_position(int mine, bool complete)
    {
        var service = Service(mine);
        Assert.Equal(false, Call(service, "IsEnemy", 1u, -4L));
        Dispatch(service, "FightNewWaveEvent");
        Dispatch(service, "FightSynchronizeEvent", Fighter(-4, 1 - mine, complete));
        Assert.Equal(1 - mine, Call(service, "TeamOf", 1u, -4L));
        Assert.Equal(true, Call(service, "IsEnemy", 1u, -4L));
        Assert.Single(service.FightersDetail(1), f => f.ActorId == -4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Wave_sync_adds_new_monsters_and_keeps_companions_and_owned_summons_allied(int mine)
    {
        var service = Service(mine);
        Call(service, "ObserveControlledFighter", 1u, new ControlledFighter(999, -20000, new Dictionary<int, int>()));
        Call(service, "OnSummoned", 1u, new FightStatsReader.SummonedFighter(-10, 103, mine, 999), 999L);
        Dispatch(service, "FightNewWaveEvent");
        Dispatch(service, "FightSynchronizeEvent", Fighter(-4, 1 - mine, true), Fighter(-5, 1 - mine, true),
            Fighter(-20000, mine, true), Fighter(-10, mine, true));
        Assert.Equal(true, Call(service, "IsEnemy", 1u, -4L));
        Assert.Equal(true, Call(service, "IsEnemy", 1u, -5L));
        Assert.Equal(false, Call(service, "IsEnemy", 1u, -20000L));
        Assert.Equal(false, Call(service, "IsEnemy", 1u, -10L));
        Assert.Equal(false, Call(service, "IsEnemy", 1u, 999L));
        Assert.Single(service.FightersDetail(1), f => f.ActorId == -5);
        var vis = (FightVisibilityReducer)Map(service, "_fighterVisibility")[1u]!;
        Assert.True(vis.Get(-4).CanTarget);
        Assert.True(vis.Get(-5).CanTarget);
    }

    [Fact]
    public void Wave_sync_without_spawn_preserves_the_known_team()
    {
        var service = Service(0);
        var teams = (ConcurrentDictionary<long, int>)Map(service, "_fighterTeam")[1u]!;
        teams[-4] = 1;
        Dispatch(service, "FightSynchronizeEvent", Fighter(-4, null, true));
        Assert.Equal(1, Call(service, "TeamOf", 1u, -4L));
        Assert.Equal(true, Call(service, "IsEnemy", 1u, -4L));
    }
}
