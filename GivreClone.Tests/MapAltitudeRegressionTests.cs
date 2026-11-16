extern alias F06App;

using System.Reflection;
using GivreClone.Protocol;
using Xunit;
using Host = F06App::GivreClone.App.Services.GivreLuaHost;
using WorldState = F06App::GivreClone.App.Services.BotWorldState;

namespace GivreClone.Tests;

public sealed class MapAltitudeRegressionTests
{
    // Walkable cells from map_90703360, mapdata_assets_world_346.bundle (2026-11-01).
    // The rejected Socket path started 279(-130) > 265(-130) > 250(0), bypassing the stairs.
    private const string Upper = "6 7 8 19 20 21 33 34 35 46 47 48 61 62 74 88 89 102 103 107 108 114 115 116 117 118 121 122 123 124 128 129 130 131 132 135 136 137 138 141 142 143 145 146 147 148 149 150 151 152 153 154 155 156 159 160 161 162 163 164 165 166 167 168 169 170 174 175 176 177 178 179 180 181 182 183 188 189 190 191 192 193 194 195 196 197 203 204 205 206 207 209 210 217 218 220 223 224 235 236 237 249 250 264";
    private const string Lower = "246 251 261 265 275 278 279 289 290 291 292 293 302 303 304 305 306 307 316 317 318 319 320 321 329 330 331 333 334 343 344 345 348 356 357 358 370 371 372 383 384 385 397 398 399 411 412 413 426 427 440 441 442 455 456 457 469 470 471 484 485 486 497 498 499 511 512 513 524 525 526 538 539 540 551 552 553";

    private static Dictionary<string, object?>[] Fixture()
    {
        var cells = Enumerable.Range(0, 560).Select(i => new Dictionary<string, object?>
        {
            ["cellNumber"] = (long)i, ["mov"] = 0L, ["los"] = 1L, ["altitude"] = 0L,
            ["moveZone"] = 0L, ["linkedZone"] = 0L
        }).ToArray();
        foreach (string id in Upper.Split(' ')) { cells[int.Parse(id)]["mov"] = 1L; cells[int.Parse(id)]["linkedZone"] = 17L; }
        foreach (string id in Lower.Split(' ')) { cells[int.Parse(id)]["mov"] = 1L; cells[int.Parse(id)]["altitude"] = -130L; cells[int.Parse(id)]["linkedZone"] = 16L; }
        foreach (int id in new[] { 8, 114, 205, 218, 224 }) cells[id]["linkedZone"] = 16L;
        foreach (int id in new[] { 190, 204, 205, 218, 265, 278, 291 }) cells[id]["moveZone"] = 1L;
        foreach (var (id, altitude) in new[] { (219, -30), (233, -30), (234, -60), (247, -60), (248, -90), (262, -90), (263, -120), (276, -120) })
        {
            cells[id]["mov"] = 1L; cells[id]["altitude"] = (long)altitude;
            cells[id]["moveZone"] = 1L; cells[id]["linkedZone"] = 16L;
        }
        return cells;
    }

    private static MapCell[] Parse(Dictionary<string, object?>[] cells)
    {
        var md = new Dictionary<string, object?> { ["cellsData"] = cells.Cast<object?>().ToList(),
            ["topNeighbourId"] = 90703617L, ["bottomNeighbourId"] = 90703361L,
            ["leftNeighbourId"] = 90702848L, ["rightNeighbourId"] = 90703872L };
        var reader = typeof(Host).Assembly.GetType("GivreClone.App.Services.MapFastReader")!;
        var value = reader.GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
            [md, new List<(long Rid, int ElementId, int Cell)>()]);
        return ((ValueTuple<MapCell[], MapExits, Dictionary<int, int>>)value!).Item1;
    }

    [Fact]
    public void Client_map_altitude_routes_from_Sufokia_dock_to_the_north_border_via_the_stairs()
    {
        var cells = Parse(Fixture());
        var world = new WorldState { MapId = 90703360, CellsMap = 90703360, Cells = cells };
        typeof(WorldState).GetMethod("RebuildWalk", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, null);
        Assert.Equal(-130, cells[279].Floor);
        Assert.Equal(-130, cells[265].Floor);
        Assert.Equal(0, cells[250].Floor);
        Assert.False(world.StepAllowed(265, 250));

        var keys = MapPathfinder.KeyMovements(279, 8, world.IsWalkable, true, world.StepAllowed);
        Assert.NotEmpty(keys);
        var path = MapPathfinder.ExpandPath(keys.Select(k => k & 0xFFF).ToArray());
        Assert.Equal(279, path[0]);
        Assert.Equal(8, path[^1]);
        Assert.Contains(path, id => cells[id].Floor is > -130 and < 0);
        for (int i = 1; i < path.Count; i++) Assert.True(world.StepAllowed(path[i - 1], path[i]));
    }

    [Fact]
    public void Existing_floor_field_still_takes_precedence_over_altitude()
    {
        var raw = Fixture();
        raw[279]["floor"] = -60L;
        Assert.Equal(-60, Parse(raw)[279].Floor);
    }
}
