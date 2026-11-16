extern alias F06App;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using MoonSharp.Interpreter;
using GivreClone.App.Services;
using GivreClone.Protocol;
using Xunit;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using World = F06App::GivreClone.App.Services.BotWorldStore;

namespace GivreClone.Tests;

public class ZaapiRoutingTests
{
    private const long BontaBank = 10001;
    private const long BontaBankZaapi = 10002;
    private const long BontaWorkshopZaapi = 10050;
    private const long BontaWorkshop = 10051;

    private const long BrakmarBank = 20001;
    private const long BrakmarBankZaapi = 20002;

    private const long AstrubBank = 30001;
    private const long AstrubZaapi = 30002;

    [Fact]
    public void City_name_normalization_handles_accents_and_prefixes()
    {
        Assert.Equal("bonta", ZaapiRegistry.NormalizeCity("Bonta"));
        Assert.Equal("bonta", ZaapiRegistry.NormalizeCity("Cité de Bonta"));
        Assert.Equal("bonta", ZaapiRegistry.NormalizeCity("Centre-ville de Bonta"));
        Assert.Equal("brakmar", ZaapiRegistry.NormalizeCity("Brâkmar"));
        Assert.Equal("brakmar", ZaapiRegistry.NormalizeCity("Cité de Brâkmar"));
        Assert.Equal("astrub", ZaapiRegistry.NormalizeCity("Astrub"));
        Assert.Equal("astrub", ZaapiRegistry.NormalizeCity("Cité d'Astrub"));
        Assert.Equal("bourgade", ZaapiRegistry.NormalizeCity("La Bourgade"));
        Assert.Equal("bourgade", ZaapiRegistry.NormalizeCity("Frigost"));
        Assert.Equal("", ZaapiRegistry.NormalizeCity(""));
        Assert.Equal("", ZaapiRegistry.NormalizeCity(null));
    }

    [Fact]
    public void Registry_records_interactive_observations()
    {
        var reg = new ZaapiRegistry();

        // Interactif Zaapi : ElementTypeId 106 ou skill 157
        bool r1 = reg.RegisterInteractive(BontaBankZaapi, 10, 106, new[] { (1, true) }, "Bonta", "Zaapi Banque");
        Assert.True(r1);
        Assert.True(reg.IsKnownZaapi(BontaBankZaapi));
        Assert.Equal("bonta", reg.GetCityForMap(BontaBankZaapi));

        // Interactif avec skill 157
        bool r2 = reg.RegisterInteractive(BontaWorkshopZaapi, 11, 16, new[] { (157, true) }, "Bonta", "Zaapi Atelier");
        Assert.True(r2);
        Assert.True(reg.IsKnownZaapi(BontaWorkshopZaapi));

        // Interactif non-Zaapi
        bool r3 = reg.RegisterInteractive(99999, 10, 16, new[] { (114, true) }, "Bonta", "Porte");
        Assert.False(r3);
        Assert.False(reg.IsKnownZaapi(99999));
    }

    [Fact]
    public void Registry_records_teleport_destinations_event()
    {
        var reg = new ZaapiRegistry();
        var dests = new[]
        {
            new TeleportReader.Destination(BontaBankZaapi, 20, 10, 80, true, 1, true),
            new TeleportReader.Destination(BontaWorkshopZaapi, 20, 11, 80, true, 1, true),
        };

        int count = reg.RegisterTeleportDestinations(BontaBankZaapi, dests, sub => sub is 10 or 11 ? "Cité de Bonta" : null);
        Assert.Equal(3, count); // source + 2 destinations (idempotent overwrite)
        Assert.True(reg.IsKnownZaapi(BontaBankZaapi));
        Assert.True(reg.IsKnownZaapi(BontaWorkshopZaapi));
        Assert.Equal("bonta", reg.GetCityForMap(BontaWorkshopZaapi));
    }

    [Fact]
    public void Registry_records_from_world_graph()
    {
        var transitions = new Dictionary<int, IReadOnlyList<MapTransition>>
        {
            [10002] = new MapTransition[] { new(10002, 10050, 0, 1, SkillId: ZaapiTravel.SkillId) },
            [10050] = new MapTransition[] { new(10050, 10002, 0, 1, SkillId: ZaapiTravel.SkillId) },
            [99999] = new MapTransition[] { new(99999, 10001, 0, 1, SkillId: 184) } // porte normale
        };
        var graph = new WorldGraph(transitions);

        var reg = new ZaapiRegistry();
        int added = reg.RegisterFromWorldGraph(graph, m => 10, _ => "Bonta");
        Assert.Equal(2, added);
        Assert.True(reg.IsKnownZaapi(10002));
        Assert.True(reg.IsKnownZaapi(10050));
        Assert.False(reg.IsKnownZaapi(99999));
    }

    [Fact]
    public void AreInSameCity_identifies_intra_city_vs_cross_city()
    {
        var reg = new ZaapiRegistry();
        reg.Register(BontaBank, 10, "Bonta");
        reg.Register(BontaWorkshop, 11, "Bonta");
        reg.Register(AstrubBank, 20, "Astrub");
        reg.Register(BrakmarBank, 30, "Brâkmar");

        // Même ville
        Assert.True(reg.AreInSameCity(BontaBank, BontaWorkshop));
        Assert.True(reg.AreInSameCity(BontaBank, BontaBank));

        // Villes différentes
        Assert.False(reg.AreInSameCity(BontaBank, AstrubBank));
        Assert.False(reg.AreInSameCity(BontaBank, BrakmarBank));
        Assert.False(reg.AreInSameCity(AstrubBank, BrakmarBank));
    }

    [Fact]
    public void PlanZaapiHop_prefers_zaapi_when_distance_exceeds_threshold()
    {
        var reg = new ZaapiRegistry();
        reg.Register(BontaBankZaapi, 10, "Bonta");
        reg.Register(BontaWorkshopZaapi, 11, "Bonta");

        // Marche simulée :
        // BontaBank -> BontaBankZaapi = 1 saut
        // BontaWorkshopZaapi -> BontaWorkshop = 1 saut
        // BontaBank -> BontaWorkshop direct = 10 sauts
        int Hops(int from, int to)
        {
            if (from == to) return 0;
            if (from == BontaBank && to == BontaBankZaapi) return 1;
            if (from == BontaBankZaapi && to == BontaBank) return 1;
            if (from == BontaWorkshop && to == BontaWorkshopZaapi) return 1;
            if (from == BontaWorkshopZaapi && to == BontaWorkshop) return 1;
            if (from == BontaBank && to == BontaWorkshop) return 10;
            return -1;
        }

        // Cas 1 : 10 sauts à pied > seuil (4) -> Zaapi préféré !
        // Marche avec Zaapi : 1 (vers zaapi) + 1 (vers destination) = 2 sauts (économise 8 sauts).
        var hop = reg.PlanZaapiHop(
            BontaBank,
            BontaWorkshop,
            directWalkHops: 10,
            threshold: 4,
            hopsFinder: Hops,
            subAreaLookup: m => m is BontaBank or BontaBankZaapi ? 10 : 11,
            cityLookup: _ => "Bonta",
            kamas: 100);

        Assert.NotNull(hop);
        Assert.Equal(BontaBankZaapi, hop.Value.SourceZaapiMap);
        Assert.Equal(BontaWorkshopZaapi, hop.Value.DestZaapiMap);
        Assert.Equal(1, hop.Value.HopsToSource);
        Assert.Equal(1, hop.Value.HopsFromDest);
        Assert.Equal(2, hop.Value.TotalWalkHops);
        Assert.Equal(8, hop.Value.SavedHops);
    }

    [Fact]
    public void PlanZaapiHop_refuses_when_below_threshold()
    {
        var reg = new ZaapiRegistry();
        reg.Register(BontaBankZaapi, 10, "Bonta");
        reg.Register(BontaWorkshopZaapi, 11, "Bonta");

        // Trajet court (2 sauts) sous le seuil (4) -> marche directe
        var hop = reg.PlanZaapiHop(
            BontaBank,
            BontaWorkshop,
            directWalkHops: 2,
            threshold: 4,
            hopsFinder: (_, _) => 1,
            subAreaLookup: _ => 10,
            cityLookup: _ => "Bonta",
            kamas: 100);

        Assert.Null(hop);
    }

    [Fact]
    public void PlanZaapiHop_refuses_when_different_cities()
    {
        var reg = new ZaapiRegistry();
        reg.Register(BontaBankZaapi, 10, "Bonta");
        reg.Register(AstrubZaapi, 20, "Astrub");

        var hop = reg.PlanZaapiHop(
            BontaBank,
            AstrubBank,
            directWalkHops: 15,
            threshold: 4,
            hopsFinder: (_, _) => 1,
            subAreaLookup: m => m == BontaBank ? 10 : 20,
            cityLookup: s => s == 10 ? "Bonta" : "Astrub",
            kamas: 100);

        Assert.Null(hop);
    }

    [Fact]
    public void PlanZaapiHop_refuses_when_insufficient_kamas()
    {
        var reg = new ZaapiRegistry();
        reg.Register(BontaBankZaapi, 10, "Bonta");
        reg.Register(BontaWorkshopZaapi, 11, "Bonta");

        var hop = reg.PlanZaapiHop(
            BontaBank,
            BontaWorkshop,
            directWalkHops: 10,
            threshold: 4,
            hopsFinder: (_, _) => 1,
            subAreaLookup: _ => 10,
            cityLookup: _ => "Bonta",
            kamas: 10); // Moins de 20 kamas

        Assert.Null(hop);
    }

    [Fact]
    public void Registry_serializes_and_deserializes_json()
    {
        var reg = new ZaapiRegistry();
        reg.Register(BontaBankZaapi, 10, "Bonta", "Banque");
        reg.Register(BontaWorkshopZaapi, 11, "Bonta", "Atelier");

        string json = reg.ToJson();
        Assert.Contains("10002", json);
        Assert.Contains("bonta", json);

        var restored = ZaapiRegistry.FromJson(json);
        Assert.Equal(2, restored.Count);
        Assert.True(restored.IsKnownZaapi(BontaBankZaapi));
        Assert.Equal("bonta", restored.GetCityForMap(BontaBankZaapi));
    }

    [Fact]
    public void GivreLuaHost_registers_toZaapi_and_ToZaapi_on_map()
    {
        var launch = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        var host = new Host(717, launch, new World(launch));
        var script = new Script(CoreModules.Preset_SoftSandbox);
        typeof(Host).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, [script, CancellationToken.None]);

        var map = script.Globals.Get("map");
        Assert.Equal(DataType.Table, map.Type);
        Assert.Equal(DataType.ClrFunction, map.Table.Get("toZaapi").Type);
        Assert.Equal(DataType.ClrFunction, map.Table.Get("ToZaapi").Type);
    }
}
