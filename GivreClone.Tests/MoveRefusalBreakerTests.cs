using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

// Journal du 24/10 12:37 : 256->231 sur la carte 178783234 renvoyé toutes les 200 ms, refusé à chaque fois.
public class MoveRefusalBreakerTests
{
    [Fact]
    public void Same_refused_move_is_no_longer_sent_after_the_threshold()
    {
        var b = new MoveRefusalBreaker();
        Assert.False(b.NoteRefused(178783234, 256, 231, 0));
        Assert.False(b.NoteRefused(178783234, 256, 231, 200));
        Assert.True(b.Allows(178783234, 256, 231, 300));
        Assert.True(b.NoteRefused(178783234, 256, 231, 400));
        Assert.False(b.Allows(178783234, 256, 231, 600));
        Assert.True(b.Allows(178783234, 256, 230, 600));      // autre destination : non concernée
        Assert.True(b.Allows(178783234, 256, 231, 400 + MoveRefusalBreaker.CooldownMs));
    }

    [Fact]
    public void Old_refusals_outside_the_window_do_not_accumulate()
    {
        var b = new MoveRefusalBreaker();
        b.NoteRefused(1, 10, 20, 0);
        b.NoteRefused(1, 10, 20, 100);
        Assert.False(b.NoteRefused(1, 10, 20, MoveRefusalBreaker.WindowMs + 500));
        Assert.True(b.Allows(1, 10, 20, MoveRefusalBreaker.WindowMs + 600));
    }

    [Fact]
    public void Arrival_clears_the_history()
    {
        var b = new MoveRefusalBreaker();
        b.NoteRefused(1, 10, 20, 0);
        b.NoteRefused(1, 10, 20, 10);
        b.NoteArrived(1, 10, 20);
        Assert.False(b.NoteRefused(1, 10, 20, 20));
    }
}
