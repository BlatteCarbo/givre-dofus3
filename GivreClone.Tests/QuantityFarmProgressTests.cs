extern alias F06App;

using Q = F06App::GivreClone.App.Services.QuantityFarm;
using Xunit;

namespace GivreClone.Tests;

public class QuantityFarmProgressTests
{
    [Fact]
    public void Parser_accepts_a_consistent_line_and_an_overshoot_with_zero_remaining()
    {
        Assert.True(Q.QuantityFarmProgress.TryParse("[quantity-farm] gid=885 owned=4 target=10 remaining=6", out var progress));
        Assert.Equal(885, progress.Gid);
        Assert.Equal(4, progress.Owned);
        Assert.Equal(10, progress.Target);
        Assert.Equal(6, progress.Remaining);
        Assert.Equal("[quantity-farm] gid=885 owned=4 target=10 remaining=6", Q.QuantityFarmProgress.FormatLine(885, 4, 10));

        Assert.True(Q.QuantityFarmProgress.TryParse("[quantity-farm] gid=885 owned=11 target=10 remaining=0", out var over));
        Assert.Equal(0, over.Remaining);
        Assert.Equal(11, over.Owned);
        Assert.Equal("Possédé 11 · reste 0 · objectif 10", Q.QuantityFarmBoard.Format(over));
    }

    [Fact]
    public void Parser_rejects_a_missing_field_a_bad_remainder_and_a_line_past_one_hundred_chars()
    {
        Assert.False(Q.QuantityFarmProgress.TryParse("[quantity-farm] gid=885 owned=4 target=10", out _));
        Assert.False(Q.QuantityFarmProgress.TryParse("[quantity-farm] gid=885 owned=11 target=10 remaining=1", out _));
        Assert.False(Q.QuantityFarmProgress.TryParse("[quantity-farm] gid=885 owned=4 target=10 remaining=9", out _));
        Assert.False(Q.QuantityFarmProgress.TryParse("[quantity-farm] gid=0 owned=4 target=10 remaining=6", out _));
        Assert.False(Q.QuantityFarmProgress.TryParse("[quantity-farm] gid=885 owned=0 target=0 remaining=0", out _));

        string valid = "[quantity-farm] gid=885 owned=4 target=10 remaining=6";
        Assert.True(valid.Length < Q.QuantityFarmProgress.MaxLength);
        string tooLong = valid + new string('x', Q.QuantityFarmProgress.MaxLength - valid.Length + 1);
        Assert.True(tooLong.Length > Q.QuantityFarmProgress.MaxLength);
        Assert.False(Q.QuantityFarmProgress.TryParse(tooLong, out _));
    }
}
