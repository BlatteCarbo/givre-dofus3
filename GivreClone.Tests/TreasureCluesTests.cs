using System;
using System.Linq;
using GivreClone.App.Services.Treasure;
using Xunit;

namespace GivreClone.Tests;

public class TreasureCluesTests
{
    // Réponse réelle de DofusDB (x=-26, y=-36, direction droite), abrégée.
    private const string Right = """
        {"total":3,"limit":50,"skip":0,"data":[
          {"posX":-20,"posY":-36,"distance":6,"pois":[{"id":11,"name":{"fr":"Théière à rayures"}},{"id":12,"name":{"fr":"Poupée koalak"}}]},
          {"posX":-19,"posY":-36,"distance":7,"pois":[{"id":12,"name":{"fr":"Poupée koalak"}}]},
          {"posX":-18,"posY":-36,"distance":8,"pois":[{"id":13,"name":{"fr":"Crâne de likrone"}}]}]}
        """;

    [Theory]
    [InlineData("0", 0)] [InlineData("right", 0)] [InlineData("Est", 0)] [InlineData("bottom", 2)] [InlineData("down", 2)] [InlineData("4", 4)]
    [InlineData("gauche", 4)] [InlineData("top", 6)] [InlineData("NORD", 6)]
    public void Direction_names_are_understood(string text, int expected)
    {
        Assert.True(TreasureClues.TryDirection(text, out var d));
        Assert.Equal(expected, d);
    }

    [Theory] [InlineData("")] [InlineData("5")] [InlineData("diagonal")] [InlineData(null)]
    public void Unknown_direction_is_refused(string? text) => Assert.False(TreasureClues.TryDirection(text, out _));

    [Fact]
    public void RequestPath_matches_the_dofusdb_query()
        => Assert.Equal("/treasure-hunt?x=-26&y=-36&direction=0&%24limit=50&%24skip=0&lang=fr", TreasureClues.RequestPath(-26, -36, 0, 0));

    [Fact]
    public void RequestPath_refuses_bad_input()
    {
        Assert.Throws<ArgumentException>(() => TreasureClues.RequestPath(2000, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => TreasureClues.RequestPath(0, 0, 3, 0));
        Assert.Throws<ArgumentException>(() => TreasureClues.RequestPath(0, 0, 0, -1));
    }

    [Fact]
    public void ParsePage_reads_every_clue_with_its_map()
    {
        var p = TreasureClues.ParsePage(Right, -26, -36, 0, 0);
        Assert.Equal((3, 0, 3), (p.Total, p.Skip, p.Count));
        Assert.Equal(4, p.Clues.Count);
        Assert.Contains(p.Clues, c => c is { X: -20, Y: -36, Distance: 6, Name: "Théière à rayures" });
    }

    [Fact]
    public void ParsePage_rejects_a_map_that_is_not_aligned_with_the_direction()
    {
        // même réponse lue comme « bas » : les cartes sont à droite, pas en dessous
        Assert.Throws<FormatException>(() => TreasureClues.ParsePage(Right, -26, -36, 2, 0));
    }

    [Fact]
    public void ParsePage_rejects_a_wrong_distance_and_bad_labels()
    {
        Assert.Throws<FormatException>(() => TreasureClues.ParsePage(Right.Replace("\"distance\":6", "\"distance\":9"), -26, -36, 0, 0));
        Assert.Throws<FormatException>(() => TreasureClues.ParsePage(Right.Replace("Théière à rayures", "<script>"), -26, -36, 0, 0));
        Assert.Throws<FormatException>(() => TreasureClues.ParsePage("{\"name\":\"NotFound\"}", -26, -36, 0, 0));
        Assert.Throws<FormatException>(() => TreasureClues.ParsePage(Right, -26, -36, 0, 50));   // skip incohérent
    }

    [Fact]
    public void ParsePage_keeps_aligned_clues_when_api_includes_an_offset_map()
    {
        // Reduced live response from (13,-63), south, on 2026-10-31.
        const string page = """
            {"total":2,"skip":0,"data":[
              {"posX":13,"posY":-58,"distance":5,"pois":[{"id":852,"name":{"fr":"Arbre à épines"}}]},
              {"posX":12,"posY":-57,"distance":6,"pois":[{"id":885,"name":{"fr":"Clef dorée"}}]}]}
            """;

        var result = TreasureClues.ParsePage(page, 13, -63, 2, 0);

        Assert.Equal(2, result.Count);
        Assert.Contains(new TreasureClue(852, 13, -58, 5, "Arbre à épines"), result.Clues);
        Assert.Contains(new TreasureClue(885, 12, -57, 6, "Clef dorée"), result.Clues);
    }

    [Fact]
    public void ParsePage_finds_the_target_on_a_map_offset_from_the_requested_axis()
    {
        const string page = """
            {"total":1,"skip":0,"data":[
              {"posX":12,"posY":-57,"distance":7,"pois":[{"id":888,"name":{"fr":"Coquillage à pois"}}]}]}
            """;

        var result = TreasureClues.ParsePage(page, 13, -64, 2, 0);

        Assert.Equal(new TreasureClue(888, 12, -57, 7, "Coquillage à pois"),
            Assert.Single(TreasureClues.Matches(result.Clues, "Coquillage à pois")));
    }

    [Fact]
    public void SearchKey_ignores_case_accents_apostrophes_and_spacing()
    {
        Assert.Equal(TreasureClues.SearchKey("Théière à rayures"), TreasureClues.SearchKey("  THEIERE   a rayures "));
        Assert.Equal(TreasureClues.SearchKey("Sève qui s'écoule"), TreasureClues.SearchKey("seve qui s’ecoule"));
        Assert.Equal("", TreasureClues.SearchKey("   "));
    }

    [Fact]
    public void Matches_returns_the_nearest_map_first_and_prefers_exact_names()
    {
        var p = TreasureClues.ParsePage(Right, -26, -36, 0, 0);
        var m = TreasureClues.Matches(p.Clues, "poupee koalak");
        Assert.Equal(new[] { -20, -19 }, m.Select(c => c.X).ToArray());   // la plus proche d'abord
        Assert.Single(TreasureClues.Matches(p.Clues, "likrone"));           // contient, faute de nom exact
        Assert.Empty(TreasureClues.Matches(p.Clues, "inconnu"));
        Assert.Empty(TreasureClues.Matches(p.Clues, ""));
    }

    [Fact]
    public void Distinct_keeps_the_nearest_occurrence_of_each_clue()
    {
        var p = TreasureClues.ParsePage(Right, -26, -36, 0, 0);
        var d = TreasureClues.Distinct(p.Clues);
        Assert.Equal(3, d.Count);
        Assert.Equal(-20, d.Single(c => c.Id == 12).X);
    }
}
