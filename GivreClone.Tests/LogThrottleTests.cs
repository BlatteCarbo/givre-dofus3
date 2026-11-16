using System;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class LogThrottleTests
{
    [Fact]
    public void First_line_of_each_key_always_passes_and_repeats_are_counted()
    {
        var t = new LogThrottle(TimeSpan.FromSeconds(3));
        Assert.True(t.ShouldLog("a", 0, out var s0)); Assert.Equal(0, s0);
        Assert.False(t.ShouldLog("a", 500, out _));
        Assert.False(t.ShouldLog("a", 2900, out _));
        Assert.True(t.ShouldLog("b", 1000, out _));                 // une autre clé n'est pas gênée
        Assert.True(t.ShouldLog("a", 3000, out var skipped));
        Assert.Equal(2, skipped);                                    // deux lignes masquées entre-temps
        Assert.False(t.ShouldLog("a", 3500, out _));
    }

    [Fact]
    public void Memory_is_bounded()
    {
        var t = new LogThrottle(TimeSpan.FromSeconds(1), maxKeys: 16);
        for (int i = 0; i < 1000; i++) Assert.True(t.ShouldLog("k" + i, i, out _));   // jamais de refus pour une clé neuve, jamais plus de 16 clés gardées
    }

    [Theory]
    [InlineData("[jeu] MapMovementEvent (115o)", "[jeu] MapMovementEvent")]
    [InlineData("[jeu] CharacterDeletionRequest (kso) 80o", "[jeu] CharacterDeletionRequest")]
    [InlineData("LoginMessage/Identification 127o", "LoginMessage/Identification")]
    [InlineData("→ PingRequest (kne) 40o", "→ PingRequest")]
    [InlineData("texte libre", "texte libre")]
    public void KeyOf_keeps_the_message_name_only(string message, string key) => Assert.Equal(key, LogThrottle.KeyOf(message));
}
