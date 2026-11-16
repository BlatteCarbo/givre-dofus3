using System.Linq;
using GivreClone.App.Services.Chat;
using Xunit;

namespace GivreClone.Tests;

public class ChatBufferTests
{
    [Fact]
    public void Clearing_history_keeps_the_cursor_monotonic()
    {
        var buffer = new ChatBuffer();
        var first = buffer.Add(0, "A", "message");
        buffer.Clear();
        Assert.Empty(buffer.Since(0));
        var next = buffer.Add(0, "B", "nouveau");
        Assert.True(next.Id > first.Id);
        Assert.Equal(next, Assert.Single(buffer.Since(first.Id)));
    }
    [Fact]
    public void Numbers_messages_and_returns_only_newer_ones()
    {
        var b = new ChatBuffer();
        b.Add(0, "A", "salut"); b.Add(10, "B", "mp"); b.Add(2, "C", "guilde");
        Assert.Equal(new[] { "mp", "guilde" }, b.Since(1).Select(e => e.Content).ToArray());
        Assert.Equal(3, b.LatestId);
    }

    [Fact]
    public void Filters_by_channel_and_labels_private_messages()
    {
        var b = new ChatBuffer();
        b.Add(0, "A", "x"); b.Add(10, "B", "y", outgoing: true);
        Assert.Equal(new[] { "y" }, b.Since(0, 50, "mp").Select(e => e.Content).ToArray());
        Assert.Equal(new[] { "x" }, b.Since(0, 50, "0").Select(e => e.Content).ToArray());
        Assert.Equal("MP", b.Since(0, 50, "mp")[0].Label);
        Assert.Equal("Commerce", ChatBuffer.Label(5)); Assert.Equal("Canal 42", ChatBuffer.Label(42));
    }

    [Fact]
    public void Keeps_only_the_most_recent_messages()
    {
        var b = new ChatBuffer(20);
        for (int i = 0; i < 50; i++) b.Add(0, "s", "m" + i);
        var all = b.Since(0, 1000);
        Assert.Equal(20, all.Count);
        Assert.Equal("m49", all[^1].Content);
    }
}
