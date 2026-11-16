using GivreClone.Engine;
using Xunit;

namespace GivreClone.Tests;

public class CharacterSelectionQueueTests
{
    [Fact]
    public void Lists_arriving_in_reverse_launch_order_keep_their_own_selection()
    {
        var q = new CharacterSelectionQueue();
        q.Bind(11, "Rangerbot"); q.Bind(12, "Sadida");
        Assert.False(q.TryClaim(13, out _));
        Assert.True(q.TryClaim(12, out var second));
        Assert.Equal("Sadida", second.Character);
        Assert.True(q.TryClaim(11, out var first));
        Assert.Equal("Rangerbot", first.Character);
        Assert.False(q.TryClaim(11, out _));
        Assert.True(q.IsCurrent(first));
        q.Complete(first);
        Assert.False(q.IsCurrent(first));
        Assert.True(q.IsCurrent(second));
    }

    [Fact]
    public void Stopped_client_and_reused_pid_invalidate_the_old_delayed_send()
    {
        var q = new CharacterSelectionQueue(); q.Bind(11, "Rangerbot");
        Assert.True(q.TryClaim(11, out var old));
        q.Forget(11);
        Assert.False(q.IsCurrent(old));
        q.Bind(11, "Rangerbot");
        Assert.True(q.TryClaim(11, out var current));
        Assert.False(q.IsCurrent(old));
        q.Complete(old);
        Assert.True(q.IsCurrent(current));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Launch_without_a_character_cannot_inherit_a_previous_selection(string? name)
    {
        var q = new CharacterSelectionQueue(); q.Bind(11, "Rangerbot");
        Assert.True(q.TryClaim(11, out var old));
        q.Bind(11, name);
        Assert.False(q.IsCurrent(old));
        Assert.False(q.TryClaim(11, out _));
    }
}
