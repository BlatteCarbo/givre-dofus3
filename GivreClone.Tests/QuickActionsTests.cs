using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GivreClone.App.Services;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public class QuickActionsTests
{
    private const long Astrub = 191105026;
    private const long Cote = 88085249;
    private const long Bonta = 212600323;

    [Fact]
    public void Dungeon_only_when_inside_and_out_of_fight()
    {
        Assert.Equal(QuickActions.NoCharacter, QuickActions.AdmitDungeon(false, false, true, false, 0).Status);
        Assert.False(QuickActions.AdmitDungeon(false, false, true, false, 0).Fire);

        var fight = QuickActions.AdmitDungeon(true, true, true, false, 5000);
        Assert.False(fight.Fire);
        Assert.Equal(QuickActions.Fight, fight.Status);

        var busy = QuickActions.AdmitDungeon(true, false, true, true, 0);
        Assert.False(busy.Fire);
        Assert.Equal(QuickActions.Busy, busy.Status);

        var outside = QuickActions.AdmitDungeon(true, false, false, false, 0);
        Assert.False(outside.Fire);
        Assert.Equal(QuickActions.NotInDungeon, outside.Status);

        foreach (var energy in new[] { -1, 0, 1, 10_000 })
        {
            var ready = QuickActions.AdmitDungeon(true, false, true, false, energy);
            Assert.True(ready.Fire);
            Assert.Equal(QuickActions.DungeonHere(null), ready.Status);
        }

        Assert.Equal("Dans le donjon « Forêt ».", QuickActions.DungeonTip(true, false, true, false, "Forêt", 0));
        Assert.Equal(QuickActions.Fight, QuickActions.DungeonTip(true, true, true, false, "Forêt", 0));
    }

    [Fact]
    public void Dungeon_result_keeps_the_server_reason()
    {
        Assert.Equal("Donjon quitté. Carte 42.", QuickActions.DungeonResult(true, "", 42));
        Assert.Equal("Donjon quitté.", QuickActions.DungeonResult(true, "ignoré", 0));
        Assert.Equal("Sortie du donjon refusée : Impossible en combat.", QuickActions.DungeonResult(false, "fight", 7));
        Assert.Equal("Sortie du donjon refusée : droit Récolte auto absent.",
            QuickActions.DungeonResult(false, "droit Récolte auto absent", 7));
        Assert.Equal("Sortie du donjon refusée : pas de réponse.", QuickActions.DungeonResult(false, "pas de réponse.", 7));
        Assert.Equal("Sortie du donjon refusée : résultat absent.", QuickActions.DungeonResult(false, "  ", 0));
    }

    [Fact]
    public void Zaap_list_is_the_book_filtered_with_cost_and_energy_is_ignored()
    {
        var known = new (long MapId, long? CostKamas, string Label)[]
        {
            (Cote, 210, "Côte d'Astrub"),
            (Bonta, null, "Bonta"),
            (Astrub, 500, "Astrub"),
            (Cote, 999, "doublon"),
            (0, 1, "vide"),
        };
        var low = QuickActions.ZaapChoices(known, Cote, 10, "", 0);
        var high = QuickActions.ZaapChoices(known, Cote, 10, "", 10_000);
        Assert.Equal(Dump(low), Dump(high));
        Assert.Equal(new[] { Astrub, Bonta, Cote }, low.Select(r => r.MapId).ToArray());

        var here = low.Single(r => r.MapId == Cote);
        Assert.False(here.CanSend);
        Assert.Equal(QuickActions.AlreadyHere, here.Reason);
        Assert.Equal("210 kamas", here.CostText);

        var unknown = low.Single(r => r.MapId == Bonta);
        Assert.True(unknown.CanSend);
        Assert.Equal(QuickActions.UnknownCostText, unknown.CostText);
        Assert.Equal("coût inconnu", unknown.CostText);
        Assert.Equal(QuickActions.CostLater, unknown.Reason);
        Assert.Equal(QuickActions.CostLater, unknown.Detail);
        Assert.True(QuickActions.AdmitZaap(true, false, false, false, unknown, 0).Fire);

        var poor = low.Single(r => r.MapId == Astrub);
        Assert.False(poor.CanSend);
        Assert.Contains("Kamas insuffisants", poor.Reason, StringComparison.Ordinal);
        Assert.Equal("500 kamas", poor.CostText);

        var paid = QuickActions.ZaapChoices(known, 1, 500, "cote", -1);
        Assert.Equal(new[] { Cote }, paid.Select(r => r.MapId).ToArray());
        Assert.True(paid[0].CanSend);
        Assert.Equal("210 kamas", paid[0].CostText);
        Assert.Empty(QuickActions.ZaapChoices(known, 1, 500, "aucun-lieu", 0));
    }

    [Fact]
    public void Zaapi_list_contains_only_observed_stops()
    {
        var stops = new[]
        {
            Stop(54530067, "Port", "Quartier du port", 4, -5, 20, "Divers"),
            Stop(54169896, "", "Sortie", null, null, null, ""),
        };
        var rows = QuickActions.ZaapiChoices(stops, 1, 100, "", 0);
        var again = QuickActions.ZaapiChoices(stops, 1, 100, "", 80_000);
        Assert.Equal(Dump(rows), Dump(again));
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.MapId == Bonta);

        var byDistrict = QuickActions.ZaapiChoices(stops, 1, 100, "quartier", 0);
        Assert.Equal(new[] { 54530067L }, byDistrict.Select(r => r.MapId).ToArray());
        Assert.Contains("4,-5", byDistrict[0].Detail, StringComparison.Ordinal);
        Assert.Equal("20 kamas", byDistrict[0].CostText);

        var byCoords = QuickActions.ZaapiChoices(stops, 1, 5, "4,-5", 0);
        Assert.Single(byCoords);
        Assert.False(byCoords[0].CanSend);
        Assert.Contains("Kamas insuffisants", byCoords[0].Reason, StringComparison.Ordinal);

        var unnamed = rows.Single(r => r.MapId == 54169896);
        Assert.Equal("54169896", unnamed.Title);
        Assert.Contains("Sortie", unnamed.Detail, StringComparison.Ordinal);
        Assert.Equal("inconnu", unnamed.CostText);
        Assert.False(unnamed.CanSend);
        Assert.Empty(QuickActions.ZaapiChoices(Array.Empty<ZaapiStop>(), 1, 100, "", 0));
    }

    [Fact]
    public void Fight_never_fires_and_energy_never_blocks()
    {
        var ready = new QuickTeleportLine(Astrub, "Astrub", "prêt", "10 kamas", true, "prêt");
        foreach (var energy in new[] { -1, 0, 9_000 })
        {
            Assert.True(QuickActions.AdmitZaap(true, false, false, false, ready, energy).Fire);
            Assert.True(QuickActions.CanBrowseZaap(true, false, false, 2, energy));
            Assert.True(QuickActions.CanBrowseZaapi(true, false, false, false, new ZaapiHost(9, 3), energy));
            var walk = QuickActions.AdmitZaapi(true, false, false, false, false, new ZaapiHost(9, 3), 1, ready, energy);
            Assert.True(walk.Fire);
            Assert.Equal(9, walk.HostMap);
        }

        Assert.False(QuickActions.AdmitZaap(true, true, false, false, ready, 0).Fire);
        Assert.Equal(WorldMapZaapTravel.CombatReason, QuickActions.AdmitZaap(true, true, false, false, ready, 0).Status);
        Assert.False(QuickActions.CanBrowseZaap(true, true, false, 4, 0));
        Assert.False(QuickActions.CanBrowseZaapi(true, true, false, true, new ZaapiHost(9, 0), 0));
        Assert.Equal(ZaapiTravel.CombatReason,
            QuickActions.AdmitZaapi(true, true, false, false, true, new ZaapiHost(9, 1), 1, ready, 0).Status);
        Assert.False(QuickActions.AdmitDungeon(true, true, true, false, 0).Fire);

        Assert.Equal(QuickActions.ScriptOwns, QuickActions.AdmitZaap(true, false, true, false, ready, 0).Status);
        Assert.Equal(QuickActions.Busy, QuickActions.AdmitZaapi(true, false, false, true, true, null, 1, ready, 0).Status);
        Assert.False(QuickActions.AdmitZaap(true, false, false, false,
            new QuickTeleportLine(Astrub, "Astrub", "non", "inconnu", false, WorldMapZaapTravel.UnknownCostReason), 0).Fire);
    }

    [Fact]
    public void Zaapi_click_walks_only_to_another_host()
    {
        var dest = new QuickTeleportLine(4, "Atelier", "prêt", "15 kamas", true, "prêt");
        Assert.Equal(QuickActions.ZaapiUnreached,
            QuickActions.AdmitZaapi(true, false, false, false, false, null, 1, dest, 0).Status);

        var other = QuickActions.AdmitZaapi(true, false, false, false, false, new ZaapiHost(9, 2), 1, dest, 0);
        Assert.True(other.Fire);
        Assert.Equal(9, other.HostMap);

        var itself = QuickActions.AdmitZaapi(true, false, false, false, false, new ZaapiHost(4, 2), 1, dest, 50);
        Assert.False(itself.Fire);
        Assert.Equal(QuickActions.NoOtherZaapi, itself.Status);

        var standing = QuickActions.AdmitZaapi(true, false, false, false, true, null, 1, dest, 0);
        Assert.True(standing.Fire);
        Assert.Equal(0, standing.HostMap);

        var alreadyHost = QuickActions.AdmitZaapi(true, false, false, false, false, new ZaapiHost(9, 0), 1, dest, 0);
        Assert.True(alreadyHost.Fire);
        Assert.Equal(0, alreadyHost.HostMap);

        Assert.Equal(QuickActions.AlreadyHere,
            QuickActions.AdmitZaapi(true, false, false, false, true, new ZaapiHost(4, 0), 4, dest, 0).Status);
        Assert.Equal(QuickActions.ZaapiOnMap, QuickActions.ZaapiTip(true, false, false, true, null, 0));
        Assert.Equal(QuickActions.ZaapiReachable(3), QuickActions.ZaapiTip(true, false, false, false, new ZaapiHost(9, 3), 0));
        Assert.Equal(QuickActions.ZaapiUnreached, QuickActions.ZaapiTip(true, false, false, false, null, 0));
        Assert.Equal(ZaapiTravel.CombatReason, QuickActions.ZaapiTip(true, true, false, true, new ZaapiHost(9, 1), 0));
        Assert.False(QuickActions.CanBrowseZaapi(true, false, false, false, null, 0));
        Assert.True(QuickActions.CanBrowseZaapi(true, false, false, true, null, 0));
    }

    [Fact]
    public void Nearest_zaapi_uses_the_planner_distance()
    {
        int Hops(long map) => map switch
        {
            10 => 4,
            20 => 2,
            30 => 2,
            5 => throw new InvalidOperationException("carte illisible"),
            6 => 1,
            _ => -1,
        };
        var hosts = new long[] { 30, 10, 20, 20, 0, 40 };
        Assert.Equal(new ZaapiHost(20, 2), QuickActions.NearestZaapi(1, hosts, Hops, 0));
        Assert.Equal(new ZaapiHost(20, 2), QuickActions.NearestZaapi(1, hosts, Hops, 4_000));
        Assert.Equal(new ZaapiHost(1, 0), QuickActions.NearestZaapi(1, new long[] { 20, 1 }, _ => 9, 0));
        Assert.Equal(new ZaapiHost(6, 1), QuickActions.NearestZaapi(1, new long[] { 5, 6 }, Hops, 0));
        Assert.Null(QuickActions.NearestZaapi(1, new long[] { 40 }, Hops, 0));
        Assert.Null(QuickActions.NearestZaapi(0, hosts, Hops, 0));
    }

    [Fact]
    public void World_graph_finds_a_zaapi_skill_without_crossing_a_locked_edge()
    {
        var graph = new WorldGraph(new Dictionary<int, IReadOnlyList<MapTransition>>
        {
            [1] = new MapTransition[]
            {
                new(1, 2, 0, 1),
                new(1, 8, 0, 1, "Qf=1"),
            },
            [2] = new MapTransition[]
            {
                new(2, 3, 0, 1, SkillId: ZaapiTravel.SkillId),
                new(2, 4, 0, 1),
            },
            [4] = new MapTransition[] { new(4, 5, 0, 1, SkillId: ZaapiTravel.SkillId) },
        });

        Assert.Equal(new[] { 2, 4 }, graph.SourceMapsWithSkill(ZaapiTravel.SkillId).OrderBy(id => id).ToArray());
        Assert.Empty(graph.SourceMapsWithSkill(0));
        Assert.Equal<(int, int)?>((2, 0), graph.NearestFree(2, new HashSet<int> { 2, 5 }));
        Assert.Equal<(int, int)?>((2, 1), graph.NearestFree(1, new HashSet<int> { 2, 4 }));
        Assert.Equal<(int, int)?>((5, 1), graph.NearestFree(4, new HashSet<int> { 5 }, 1));
        Assert.Null(graph.NearestFree(1, new HashSet<int> { 5 }, 1));
        Assert.Null(graph.NearestFree(1, new HashSet<int> { 5 }, 2));
        Assert.Equal<(int, int)?>((5, 3), graph.NearestFree(1, new HashSet<int> { 5 }, 3));
        Assert.Null(graph.NearestFree(1, new HashSet<int> { 8 }));
    }

    [Fact]
    public void Header_and_settings_expose_the_actions()
    {
        string root = MappingScan.RepoRoot();
        string axaml = File.ReadAllText(Path.Combine(root, "GivreClone.App", "Views", "BotsView.axaml"));
        int dungeon = axaml.IndexOf("QuickExitDungeonCommand", StringComparison.Ordinal);
        int zaaps = axaml.IndexOf("QuickZaaps", StringComparison.Ordinal);
        int zaapis = axaml.IndexOf("QuickZaapis", StringComparison.Ordinal);
        int character = axaml.IndexOf("Changer de personnage", StringComparison.Ordinal);
        Assert.True(dungeon >= 0 && zaaps > dungeon && zaapis > zaaps && character > zaapis);
        Assert.Contains("L.Travel.ExitDungeon", axaml, StringComparison.Ordinal);
        Assert.Contains("Settings.ZaapTravelIndex", axaml, StringComparison.Ordinal);

        int auto = axaml.IndexOf("L.Bots.Settings.ZaapRouteAuto", StringComparison.Ordinal);
        int world = axaml.IndexOf("L.Bots.Settings.ZaapRouteWorld", StringComparison.Ordinal);
        int ground = axaml.IndexOf("L.Bots.Settings.ZaapRouteGround", StringComparison.Ordinal);
        int haven = axaml.IndexOf("L.Bots.Settings.ZaapRouteHaven", StringComparison.Ordinal);
        int zaapi = axaml.IndexOf("L.Bots.Settings.ZaapRouteZaapi", StringComparison.Ordinal);
        Assert.True(auto >= 0 && world > auto && ground > world && haven > ground && zaapi > haven);

        foreach (var lang in new[] { "fr.axaml", "en.axaml", "es.axaml" })
        {
            string text = File.ReadAllText(Path.Combine(root, "GivreClone.App", "Lang", lang));
            Assert.Contains("L.Bots.Settings.ZaapRouteZaapi", text, StringComparison.Ordinal);
        }

        string vm = File.ReadAllText(Path.Combine(root, "GivreClone.App", "ViewModels", "BotsViewModel.QuickActions.cs"));
        Assert.Contains("DungeonExitAsync", vm, StringComparison.Ordinal);
        Assert.Contains("QuickWorldZaapAsync", vm, StringComparison.Ordinal);
        Assert.Contains("QuickZaapiAsync", vm, StringComparison.Ordinal);
        Assert.Contains("SelfEnergy", vm, StringComparison.Ordinal);
        int status = axaml.IndexOf("Text=\"{Binding QuickActionStatus}\"", StringComparison.Ordinal);
        int panel = axaml.LastIndexOf("</WrapPanel>", status, StringComparison.Ordinal);
        Assert.True(status > character && panel > zaapis && status > panel);
        string caption = axaml.Substring(panel, status - panel);
        Assert.Contains("TextWrapping=\"NoWrap\"", caption, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", caption, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding QuickActionStatus}\"", caption, StringComparison.Ordinal);
        Assert.DoesNotContain("TextWrapping=\"Wrap\"", caption, StringComparison.Ordinal);
        Assert.DoesNotContain("energy <", vm, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("energy >", vm, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("energy ==", vm, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cur <", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("Cur >", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void Reasons_stay_plain_french_and_the_login_list_can_be_sent()
    {
        foreach (var text in new[]
        {
            WorldMapZaapTravel.UnknownListReason,
            WorldMapZaapTravel.UnknownReason,
            ZaapiTravel.UnknownListReason,
            ZaapiTravel.UnknownReason,
            QuickActions.ZaapiUnreached,
            QuickActions.CostLater,
            QuickActions.ZaapKnown(0),
            QuickActions.ZaapKnown(47),
        })
        {
            Assert.DoesNotContain("Event", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Teleport", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\n", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        }

        Assert.Equal("Aucun zaap connu pour l'instant.", WorldMapZaapTravel.UnknownListReason);
        Assert.Equal("Aucun zaapi joignable.", QuickActions.ZaapiUnreached);
        string live = QuickActions.LiveSummary(false, QuickActions.NotInDungeon, QuickActions.ZaapKnown(47), QuickActions.ZaapiUnreached, 0);
        Assert.DoesNotContain("Event", live, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", live, StringComparison.Ordinal);
        Assert.Contains("47 zaaps connus.", live, StringComparison.Ordinal);

        var seeded = QuickActions.ZaapChoices(new (long, long?, string)[] { (Bonta, null, "Bonta") }, here: 1, kamas: 0, query: "", energy: -1);
        Assert.Single(seeded);
        Assert.True(seeded[0].CanSend);
        Assert.Equal("coût inconnu", seeded[0].CostText);

        var announced = QuickActions.ZaapChoices(new (long, long?, string)[] { (Bonta, 210, "Bonta") }, here: 1, kamas: 210, query: "", energy: 0);
        Assert.Equal("210 kamas", announced[0].CostText);
        Assert.True(announced[0].CanSend);
        Assert.Equal(QuickActions.KnownCost, announced[0].Detail);
    }

    private static ZaapiStop Stop(long map, string name, string district, int? x, int? y, long? cost, string category)
        => new(map, name, district, x, y, cost, 2, category, 0);

    private static string Dump(IReadOnlyList<QuickTeleportLine> rows)
        => string.Join(";", rows.Select(r => r.MapId + ":" + r.CanSend + ":" + r.CostText + ":" + r.Reason));
}
