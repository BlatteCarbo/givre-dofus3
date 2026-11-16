using GivreClone.App.Services;
using GivreClone.App.Services.Mcp;
using Xunit;

namespace GivreClone.Tests;

public class McpExtrasTests
{
    [Theory]
    [InlineData("player@example.com", true)]
    [InlineData("PLAYER.ONE85", true)]
    [InlineData("player@example.com", true)]
    [InlineData("player@example.com", false)]
    [InlineData("", false)]
    public void Blocked_accounts_match_on_the_local_part_only(string login, bool expected)
        => Assert.Equal(expected, McpGuards.IsBlocked(login, McpGuards.DefaultBlockedLogins));

    [Fact]
    public void Fight_metrics_measure_the_wait_between_my_last_marker_and_my_next_turn()
    {
        string[] log =
        {
            "21:02:58.156  <1> [combat] début de tour : MOI (round 1)",
            "21:02:59.032  <1> [turbo-ready] tour prêt envoyé par le bot : marqueur du serveur",
            "21:02:59.605  <1> [turbo-ack] accusé envoyé par le bot (séquence de -1) action=22",
            "21:03:00.003  <1> [turbo-ready] tour prêt envoyé par le bot : marqueur du serveur",
            "21:03:01.778  <1> [combat] début de tour : MOI (round 2)",
            "21:03:01.907  <1> [turbo-ready] tour prêt envoyé par le bot : marqueur du serveur",
            "21:03:02.027  <1> [combat] début de tour : MOI (round 3)",
            "21:03:12.092  <1> [combat] début de tour : MOI (round 1)",
        };
        var r = FightLogMetrics.Analyze(log);
        Assert.Equal(2, r.Fights.Count);
        Assert.Equal(3, r.Fights[0].Rounds);
        Assert.Equal(new[] { 1775, 120 }, r.Fights[0].GapsMs);   // 1,775 s après le dernier marqueur, puis 0,12 s
        Assert.Equal(1, r.Fights[0].ForeignAcks);
        Assert.Equal(1775, r.MaxGapMs);
        Assert.Equal(0.5, r.ShareUnder400Ms);
    }
}

public class CharacterPickTests
{
    private static readonly (long Id, string? Name, int Level)[] Chars = { (1, "Alpha", 20), (2, "Bravo", 180), (3, "Charlie", 75) };

    [Fact]
    public void Star_picks_the_highest_level_and_a_name_picks_that_character()
    {
        Assert.Equal(2L, GivreClone.App.Services.CharacterPick.Pick(Chars, "*"));
        Assert.Equal(3L, GivreClone.App.Services.CharacterPick.Pick(Chars, "charlie"));
        Assert.Null(GivreClone.App.Services.CharacterPick.Pick(Chars, "Inconnu"));
        Assert.Null(GivreClone.App.Services.CharacterPick.Pick(System.Array.Empty<(long, string?, int)>(), "*"));
    }
}
