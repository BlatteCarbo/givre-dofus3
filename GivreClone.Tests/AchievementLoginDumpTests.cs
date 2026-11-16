using GivreClone.App.Services.LotM;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class AchievementLoginDumpTests
{
    [Fact]
    public void A_login_window_dumps_once_until_the_next_login_opens_it()
    {
        var cap = new AchievementLoginCapture();
        Assert.True(cap.TryTake());
        Assert.False(cap.TryTake());
        cap.OpenLogin();
        cap.OpenLogin();
        Assert.True(cap.TryTake());
        Assert.False(cap.TryTake());
    }

    [Fact]
    public void The_full_event_survives_the_transport_line_cap_including_a_late_unmapped_field()
    {
        var payload = new List<ProtoField>();
        for (int id = 1; id <= 180; id++)
        {
            var entry = new List<ProtoField>
            {
                new() { Field = 1, Wire = WireType.Varint, Varint = (ulong)id },
                new() { Field = 2, Wire = WireType.Varint, Varint = 900_000 },
            };
            if (id == 180)
                entry.Add(new ProtoField { Field = 9, Wire = WireType.Varint, Varint = 424_242 });
            payload.Add(new ProtoField { Field = 1, Wire = WireType.Len, Message = entry });
        }

        string text = AchievementEventDump.Format("mdp", payload);
        Assert.Contains("FULL 180 champ(s)", text);
        Assert.Contains("FULL-END champs=180", text);
        Assert.Contains("#9 int 424242", text);
        Assert.True(text.Split('\n').Length > 239);
        Assert.True(text.Length > AchievementEventDump.TransportLineMax);

        var parts = AchievementEventDump.Chunks(text);
        Assert.True(parts.Count > 1);
        Assert.All(parts, p => Assert.InRange(p.Length, 1, AchievementEventDump.TransportLineMax));
        Assert.Equal(text, string.Concat(parts));
        Assert.Contains("#9 int 424242", string.Concat(parts));
    }

    [Fact]
    public void Chunks_keep_a_line_longer_than_the_cap_and_a_short_text_whole()
    {
        string longLine = new string('a', 5000);
        var parts = AchievementEventDump.Chunks(longLine);
        Assert.Equal(longLine, string.Concat(parts));
        Assert.All(parts, p => Assert.True(p.Length <= AchievementEventDump.TransportLineMax));
        Assert.Equal(new[] { "court\n" }, AchievementEventDump.Chunks("court\n"));
    }
}
