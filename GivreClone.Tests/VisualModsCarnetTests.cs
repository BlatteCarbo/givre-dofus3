using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GivreClone.App.Services.VisualMods;
using Xunit;

namespace GivreClone.Tests;

public sealed class VisualModsCarnetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "givre-visualmods-" + Guid.NewGuid().ToString("N"));
    public VisualModsCarnetTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private string FileAt(string name = "carnet.json") => Path.Combine(_dir, name);

    private static VisualPosition Pos(string label = "Mine", int x = -3, int y = 4)
    {
        Assert.True(VisualCarnetStore.TryCreatePosition(label, x.ToString(), y.ToString(), "Roche et entrée", true,
            out var position, out var error), error);
        return position!;
    }

    [Fact]
    public void Missing_save_reload_and_backup_keep_previous_valid_snapshot()
    {
        var store = new VisualCarnetStore(FileAt());
        Assert.False(store.Load().ReadOnly);
        Assert.Empty(store.Load().Document.Positions);
        store.Save(new VisualCarnetDocument { Positions = new List<VisualPosition> { Pos() } });
        var firstJson = File.ReadAllText(FileAt());
        var loaded = store.Load();
        Assert.False(loaded.ReadOnly);
        Assert.Equal((-3, 4, "Mine"), (loaded.Document.Positions[0].X, loaded.Document.Positions[0].Y,
            loaded.Document.Positions[0].Label));
        store.Save(new VisualCarnetDocument { Positions = new List<VisualPosition> { Pos(), Pos("Bois", 7, -8) } });
        Assert.Equal(firstJson, File.ReadAllText(FileAt() + ".bak"));
        Assert.Equal(2, store.Load().Document.Positions.Count);
        Assert.False(File.Exists(FileAt() + ".tmp"));
    }

    [Theory]
    [InlineData("", "0", "0", "")]
    [InlineData("  ", "0", "0", "")]
    [InlineData("Valid", "1.5", "0", "")]
    [InlineData("Valid", "10001", "0", "")]
    [InlineData("Valid", "-10001", "0", "")]
    [InlineData("Valid", "", "0", "")]
    [InlineData("a\nb", "0", "0", "")]
    public void Input_rejects_non_integer_or_malformed_coordinates_and_labels(string label, string x, string y, string note)
    {
        Assert.False(VisualCarnetStore.TryCreatePosition(label, x, y, note, false, out var pos, out _));
        Assert.Null(pos);
    }

    [Fact]
    public void Oversized_entries_duplicate_ids_and_future_schema_are_rejected()
    {
        var entry = Pos();
        var store = new VisualCarnetStore(FileAt());
        Assert.Throws<InvalidDataException>(() => store.Save(new VisualCarnetDocument { Version = 2 }));
        Assert.Throws<InvalidDataException>(() => store.Save(new VisualCarnetDocument
        {
            Positions = new List<VisualPosition> { entry, entry }
        }));
        Assert.Throws<InvalidDataException>(() => store.Save(new VisualCarnetDocument
        {
            Positions = Enumerable.Range(0, 251).Select(i => Pos("P" + i, i, 0)).ToList()
        }));
        Assert.Throws<InvalidDataException>(() => store.Save(new VisualCarnetDocument
        {
            Positions = new List<VisualPosition> { entry with { Note = new string('x', 801) } }
        }));
        Assert.False(File.Exists(FileAt()));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("{\"Version\":999,\"Positions\":[]}")]
    [InlineData("{\"Positions\":[]}")]
    [InlineData("{\"Version\":1}")]
    [InlineData("{\"Version\":1,\"Positions\":[{}]}")]
    public void Invalid_existing_files_are_read_only_and_preserved_byte_for_byte(string data)
    {
        File.WriteAllText(FileAt(), data);
        var store = new VisualCarnetStore(FileAt());
        var result = store.Load();
        Assert.True(result.ReadOnly);
        Assert.Empty(result.Document.Positions);
        Assert.Equal(data, File.ReadAllText(FileAt()));
        var failure = Record.Exception(() => store.Save(new VisualCarnetDocument()));
        Assert.True(failure is InvalidDataException or JsonException, failure?.ToString() ?? "No failure");
        Assert.Equal(data, File.ReadAllText(FileAt()));
    }

    [Fact]
    public void Files_over_the_size_limit_are_refused_without_truncation()
    {
        var content = new string('Z', VisualCarnetStore.MaxFileBytes + 1);
        File.WriteAllText(FileAt(), content);
        Assert.True(new VisualCarnetStore(FileAt()).Load().ReadOnly);
        Assert.Equal(content.Length, new FileInfo(FileAt()).Length);
    }

    [Fact]
    public void Symlink_destination_and_backup_are_rejected_when_supported()
    {
        string target = FileAt("real.json");
        File.WriteAllText(target, "private value");
        string link = FileAt("link.json");
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { return; }
        var store = new VisualCarnetStore(link);
        Assert.True(store.Load().ReadOnly);
        Assert.Throws<InvalidDataException>(() => store.Save(new VisualCarnetDocument()));
        Assert.Equal("private value", File.ReadAllText(target));

        var other = new VisualCarnetStore(FileAt("other.json"));
        File.CreateSymbolicLink(other.FilePath + ".bak", target);
        Assert.Throws<InvalidDataException>(() => other.Save(new VisualCarnetDocument()));
        Assert.Equal("private value", File.ReadAllText(target));
        Assert.False(File.Exists(other.FilePath));
    }

    [Fact]
    public void Parent_directory_links_are_denied_when_supported()
    {
        var actual = Path.Combine(_dir, "real-folder");
        Directory.CreateDirectory(actual);
        string redirect = Path.Combine(_dir, "redirect-folder");
        try { Directory.CreateSymbolicLink(redirect, actual); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { return; }

        var store = new VisualCarnetStore(Path.Combine(redirect, "carnet.json"));
        Assert.True(store.Load().ReadOnly);
        Assert.Throws<InvalidDataException>(() => store.Save(new VisualCarnetDocument()));
        Assert.Empty(Directory.GetFiles(actual));
    }
}

public sealed class VisualModsPlannerTests
{
    [Fact]
    public void Planner_requires_verified_exact_manual_hint_and_is_not_pathfinding()
    {
        Assert.False(ManualTreasurePlanner.TryPlan("1", "2", "4", "-3", "Arbre", false, out _, out _));
        Assert.False(ManualTreasurePlanner.TryPlan("1", "2", "4", "-3", "   ", true, out _, out _));
        Assert.False(ManualTreasurePlanner.TryPlan("1", "2", "4.5", "-3", "Arbre", true, out _, out _));
        Assert.True(ManualTreasurePlanner.TryPlan("1", "2", "4", "-3", "Arbre", true,
            out var route, out var error), error);
        Assert.Equal((3, -5, 8, "Est + Nord"), (route!.DeltaX, route.DeltaY, route.ManhattanDistance,
            route.Direction));
        Assert.Equal("Arbre", route.Hint);
        Assert.True(ManualTreasurePlanner.TryPlan("4", "-3", "4", "-3", "Arbre", true,
            out var same, out _));
        Assert.Equal("Même position", same!.Direction);
        Assert.Equal(0, same.ManhattanDistance);
    }

    [Fact]
    public void Map_is_stable_centered_bounded_and_does_not_invent_points()
    {
        Assert.Empty(VisualMapProjection.Project(Array.Empty<VisualPosition>()));
        var single = Assert.Single(VisualMapProjection.Project(new[] { Create(-3, 7) }));
        Assert.Equal(220d, single.Left);
        Assert.Equal(105d, single.Top);
        var pins = VisualMapProjection.Project(new[] { Create(-12, -6), Create(23, 27) });
        Assert.Equal(2, pins.Count);
        Assert.All(pins, p =>
        {
            Assert.InRange(p.Left, 22d, 418d);
            Assert.InRange(p.Top, 22d, 188d);
        });
        Assert.True(pins[0].Left < pins[1].Left);
        Assert.True(pins[0].Top < pins[1].Top);
        Assert.Throws<ArgumentOutOfRangeException>(() => VisualMapProjection.Project(new[] { Create(0, 0) }, double.NaN));
        Assert.Throws<InvalidDataException>(() => VisualMapProjection.Project(new[] { Create(10001, 0) }));
    }

    private static VisualPosition Create(int x, int y) => new(Guid.NewGuid(), "Test", x, y, "", false, DateTimeOffset.UtcNow);
}
