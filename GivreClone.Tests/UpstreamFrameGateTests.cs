using System.Linq;
using GivreClone.App.Services.LocalLaunch;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

public class UpstreamFrameGateTests
{
    private static byte[] Frame(params byte[] body) => RawProtobuf.EncodeVarint((ulong)body.Length).Concat(body).ToArray();

    [Fact]
    public void Passes_every_byte_when_nothing_is_dropped()
    {
        var gate = new UpstreamFrameGate();
        var stream = Frame(1, 2, 3).Concat(Frame(4, 5)).ToArray();
        Assert.Equal(stream, gate.Process(7, stream, (_, _) => false));
    }

    [Fact]
    public void Drops_only_the_selected_frame_and_keeps_the_others_intact()
    {
        var gate = new UpstreamFrameGate();
        var stream = Frame(1, 2, 3).Concat(Frame(9, 9)).Concat(Frame(4, 5)).ToArray();
        var output = gate.Process(7, stream, (_, f) => f.Length == 2 && f[0] == 9);
        Assert.Equal(Frame(1, 2, 3).Concat(Frame(4, 5)).ToArray(), output);
        Assert.Equal(1, gate.Dropped);
    }

    [Fact]
    public void Reassembles_a_frame_split_across_segments()
    {
        var gate = new UpstreamFrameGate();
        var frame = Frame(10, 11, 12, 13);
        var first = gate.Process(7, frame[..3], (_, _) => false);
        Assert.Empty(first);
        var second = gate.Process(7, frame[3..], (_, _) => false);
        Assert.Equal(frame, second);
    }

    [Fact]
    public void Fails_open_on_an_invalid_stream()
    {
        var gate = new UpstreamFrameGate();
        var garbage = Enumerable.Repeat((byte)0xFF, 24).ToArray();
        Assert.Equal(garbage, gate.Process(7, garbage, (_, _) => true));
        var later = new byte[] { 1, 2, 3 };
        Assert.Equal(later, gate.Process(7, later, (_, _) => true));
    }
}
