using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public sealed class MZP3FrameCodecTests
{
    [Fact]
    public void Two_valid_frames_larger_than_one_frame_budget_can_share_a_receive_buffer()
    {
        const int frameLimit = 128;
        byte[] single = MZP3FrameCodec.Prefix(new byte[100], maxFrameBytes: frameLimit);
        byte[] combined = new byte[single.Length * 2];
        single.CopyTo(combined, 0);
        single.CopyTo(combined, single.Length);

        var first = MZP3FrameCodec.TryReadOne(combined, maxFrameBytes: frameLimit, requireExact: false);
        Assert.True(first.Complete);
        Assert.Equal(single.Length, first.Consumed);
        Assert.Equal(100, first.BodyLength);

        var second = MZP3FrameCodec.TryReadOne(combined.AsSpan(first.Consumed), maxFrameBytes: frameLimit);
        Assert.True(second.Complete);
        Assert.Equal(single.Length, second.Consumed);
        Assert.Equal(MZP3WireError.TooLarge, MZP3FrameCodec.TryReadOne(new byte[] { 0x81, 0x01 }, maxFrameBytes: frameLimit).Error);
    }
}
