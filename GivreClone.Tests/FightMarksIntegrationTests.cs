extern alias F06App;
using System.Collections;
using System.Reflection;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using DecodedPacket = F06App::GivreClone.App.Services.DecodedPacket;
using Xunit;

namespace GivreClone.Tests;

public sealed class FightMarksIntegrationTests
{
    [Fact]
    public void Packet_observation_is_isolated_by_client_and_purged_with_fight_state()
    {
        var service = new LaunchService();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        IDictionary Map(string name) => (IDictionary)typeof(LaunchService).GetField(name, flags)!.GetValue(service)!;
        var catalog = ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
        int F(string message, string field) => catalog.FieldNumber(message, field)
            ?? throw new InvalidOperationException($"Missing mapping {message}.{field}");
        ProtoField V(string message, string field, int value) => new()
            { Field = F(message, field), Wire = WireType.Varint, Varint = (ulong)value };
        ProtoField M(string message, string field, params ProtoField[] children) => new()
            { Field = F(message, field), Wire = WireType.Len, Message = children.ToList() };
        void Place(uint pid) => typeof(LaunchService).GetMethod("OnDecoded", flags)!.Invoke(service,
            new object[] { new DecodedPacket(pid, false, "jwe", "GameActionFightEvent", new[] {
                M("GameActionFightEvent", "mark_cells", M("MarkCells", "mark",
                    V("FightMark", "mark_id", 19), V("FightMark", "author_id", 999),
                    V("FightMark", "active", 1), M("FightMark", "cells", V("MarkedCell", "gask", 261))))
            }, Array.Empty<byte>()) });
        Map("_inFight")[101u] = true;
        Map("_inFight")[102u] = true;
        Place(101);
        Place(102);
        Place(103);
        var marks = Map("_fightMarks");
        Assert.Equal(2, marks.Count);
        Assert.Equal(19, Assert.Single(((FightMarks)marks[101u]!).Snapshot).Key);

        // La case marquee doit remonter au planificateur : elle est refusee comme case de LANCER par le serveur
        // (message 229 « Il y a un piege sur cette case »), 365 refus mesures sur Shadowbot le 09/11.
        var marked = (HashSet<int>)typeof(LaunchService)
            .GetMethod("MarkedCells", flags)!.Invoke(service, new object[] { 101u })!;
        Assert.Equal(new[] { 261 }, marked);
        // Un client sans marque observee ne restreint rien (ensemble vide, pas de blocage du tour).
        Assert.Empty((HashSet<int>)typeof(LaunchService)
            .GetMethod("MarkedCells", flags)!.Invoke(service, new object[] { 103u })!);

        typeof(LaunchService).GetMethod("PurgeFightState", flags)!.Invoke(service, new object[] { 101u });

        Assert.False(marks.Contains(101u));
        Assert.Equal(19, Assert.Single(((FightMarks)marks[102u]!).Snapshot).Key);
        // Fin de combat : plus aucune case interdite pour le combat suivant.
        Assert.Empty((HashSet<int>)typeof(LaunchService)
            .GetMethod("MarkedCells", flags)!.Invoke(service, new object[] { 101u })!);
    }
}
